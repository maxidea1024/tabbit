// 글자에 두른 테두리가 획 사이를 메우지 않는가.
//
// **눈으로 봅니다.** 굵기의 상한은 `test/outline.test.ts` 가 지키지만, 그 굵기로 실제 화면이
// 어떻게 보이는지는 값으로 판정할 수 없습니다 — 말마다 한 장씩 굽고, 판의 글을 크게 오려
// 한 장을 더 굽습니다.
//
//     npx tsx tools/check-outline.ts
//
// 콘솔 오류가 하나라도 있으면 실패로 끝냅니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'

import { LANGUAGES, type Language } from '../src/core/strings'
import { clickSpot, closeGuide, pass, peek, pressTitle, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5233

const problems: string[] = []

/**
 * 봐도 되는 콘솔 오류.
 *
 * **계정 서버가 없습니다.** 이 도구는 개발 서버만 띄우므로 타이틀이 `/auth/providers` 를
 * 조회할 때 500 이 돌아옵니다 — 글자를 보는 도구이므로 그것은 넘깁니다.
 */
function noise(line: string): boolean {
  return line.includes('500 (Internal Server Error)') || line.includes('/auth/')
}

/**
 * 글이 가장 촘촘하게 모이는 자리.
 *
 * 왼쪽 판의 칸 이름과 단추 줄입니다 — 15 ~ 23픽셀 글이 여기 다 있습니다.
 */
const CROP = { x: 0, y: 300, width: 640, height: 460 }

async function open(browser: Browser, language: Language): Promise<Page> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('console', message => {
    if (message.type() === 'error' && !noise(message.text())) problems.push(message.text())
  })
  page.on('pageerror', error => { if (!noise(String(error))) problems.push(String(error)) })
  await skipLogin(page)
  // 고른 말을 저장해 두고 엽니다. 옵션을 눌러 고르는 것과 같은 자리입니다.
  await page.addInitScript(`try { localStorage.setItem('clover.options',
    JSON.stringify({ language: ${JSON.stringify(language)} })) } catch {}`)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-INK1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)
  return page
}

/**
 * 말마다의 배수. `src/ui/font.ts` 의 `OUTLINE_RATIO` 와 같아야 합니다.
 *
 * **여기 베껴 적는 이유**는 화면이 실제로 그 굵기를 걸었는지를 보기 위해서입니다 — 같은
 * 함수를 불러 비교하면 「부르지 않았다」와 「불렀다」가 같은 답으로 돌아옵니다.
 */
const RATIO: Record<Language, number> = {
  ko: 0.045, ja: 0.045, 'zh-Hans': 0.045, 'zh-Hant': 0.04, en: 0.075, de: 0.075,
}

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail === '' ? '' : `  — ${detail}`}`)
}

/** 화면이 알린 굵기가 그 말의 배수와 맞는가. */
async function checkWidths(page: Page, language: Language, where: string): Promise<void> {
  const ink = (await peek(page)).inkWidth
  if (ink === undefined) {
    check(`${where} 굵기를 알립니다`, false)
    return
  }
  const ratio = RATIO[language]
  const want = { hand: 17 * ratio, headline: 34 * ratio, button: 15 * ratio }
  for (const key of ['hand', 'headline', 'button'] as const) {
    const got = ink[key]
    check(`${where} ${key} = ${want[key].toFixed(3)}`,
          Math.abs(got - want[key]) < 0.001, got.toFixed(3))
  }
}

/**
 * 옵션을 열어 말을 바꿉니다.
 *
 * **화면이 알린 자리만 누릅니다.** 좌표를 적으면 옵션에 줄이 하나 늘 때마다 이 도구가
 * 빈자리를 눌러 놓고 통과합니다.
 */
async function switchLanguage(page: Page, to: Language): Promise<void> {
  await clickSpot(page, 'title:options')
  await pass(page, 900)
  // 말은 「일반」의 첫 줄이고, 고르는 칸이 단추 6개로 늘 펼쳐져 있습니다.
  await clickSpot(page, `option:language:${to}`)
  await pass(page, 1200)
  await page.keyboard.press('Escape')
  await pass(page, 700)
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  for (const language of LANGUAGES) {
    const page = await open(browser, language)
    await checkWidths(page, language, `${language} 처음부터`)
    await page.screenshot({ path: path.join(OUT, `outline-${language}-title.png`) })
    await pressTitle(page, 'start')
    await pass(page, 900)
    await closeGuide(page)
    await pass(page, 400)
    await clickSpot(page, 'pick')
    await pass(page, 1200)
    await page.screenshot({ path: path.join(OUT, `outline-${language}.png`) })
    // **오린 것을 3배로 키웁니다.** 1배로는 획 사이가 메워졌는지가 보이지 않습니다.
    await page.screenshot({ path: path.join(OUT, `outline-${language}-near.png`), clip: CROP })
    console.log(`outline-${language}.png · outline-${language}-near.png`)
    await page.close()
  }

  // **말을 바꾸면 굵기가 따라오는가.** 칸과 단추는 한 번 만들고 글만 갈아 끼우므로, 만들
  // 때의 말로 정해 둔 굵기가 그대로 남을 수 있습니다. 라틴에서 번체로 갑니다 — 배수가
  // 0.075 에서 0.04 로, 둘 사이가 가장 멉니다.
  const page = await open(browser, 'en')
  await checkWidths(page, 'en', '바꾸기 전')
  await switchLanguage(page, 'zh-Hant')
  await checkWidths(page, 'zh-Hant', '바꾼 뒤')
  await page.screenshot({ path: path.join(OUT, 'outline-switched.png') })
  await page.close()

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    console.error(problems.join('\n'))
    return 1
  }
  return 0
}

main().then(code => process.exit(code))
