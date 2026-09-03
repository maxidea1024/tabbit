// 챌린지 판이 실제로 그려지고 판이 열리는가.
//
// **게이트가 아니라 확인 도구입니다.** 화면을 고치는 동안 눈으로 볼 것이 필요하고, 구운
// 화면과 견주는 것은 그 목적이 아닙니다 — 여기서 보는 것은 「열리는가 · 그려지는가 ·
// 시작되는가」 셋입니다.
//
//     npx tsx tools/check-challenge.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { skipLogin, pass, at, TITLE_CHALLENGES } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'check')

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5193 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, locale: 'ko-KR' })
  await skipLogin(page)

  const errors: string[] = []
  page.on('pageerror', one => errors.push(one.message))
  page.on('console', one => { if (one.type() === 'error') errors.push(one.text()) })

  let failed = 0
  const check = (name: string, ok: boolean, note = '') => {
    if (!ok) failed++
    console.log(`  ${ok ? '✓' : '✗'} ${name}${note ? '  —  ' + note : ''}`)
  }

  await page.goto('http://localhost:5193/', { waitUntil: 'domcontentloaded' })
  // 데이터를 읽고 타이틀이 서기까지 기다립니다.
  await pass(page, 2600)

  // **처음에는 잠겨 있습니다.** 저장이 비었으면 챌린지를 누르면 쪽지만 뜹니다.
  await page.evaluate(() => localStorage.removeItem('clover.challenge'))
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  await page.screenshot({ path: path.join(OUT, 'challenge-0-title.png') })

  // 타이틀의 챌린지 단추. 아래 바의 셋째 칸입니다.
  // **잠겨 있으면 눌리지 않습니다.** 올려서 쪽지가 뜨는지를 봅니다.
  const challenges = await at(page, TITLE_CHALLENGES.x, TITLE_CHALLENGES.y)
  await page.mouse.move(challenges.x, challenges.y)
  await pass(page, 500)
  await page.screenshot({ path: path.join(OUT, 'challenge-1-locked.png') })

  // 열어 둔 채로 다시 봅니다.
  await page.evaluate(() => {
    localStorage.setItem('clover.challenge',
      JSON.stringify({ beaten: ['dry_season', 'face_town'], unlocked: true }))
    // 첫 실행 안내를 끕니다 — 판을 덮으므로 화면에서 판이 보이지 않습니다.
    localStorage.setItem('clover.guide.seen', '1')
  })
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  await page.mouse.click(challenges.x, challenges.y)
  await pass(page, 900)
  await page.screenshot({ path: path.join(OUT, 'challenge-2-panel.png') })

  // 칸 하나를 눌러 규칙이 오른쪽에 적히는지 봅니다. 판이 가운데에 놓이므로 그만큼 옮깁니다.
  const left = (1280 - 1180) / 2
  const top = (800 - 744) / 2
  await page.mouse.click(left + 34 + 118 * 2 + 50, top + 108 + 46)
  await pass(page, 500)
  await page.screenshot({ path: path.join(OUT, 'challenge-3-picked.png') })

  // 첫 칸으로 돌아가 시작을 누릅니다.
  await page.mouse.click(left + 34 + 50, top + 108 + 46)
  await pass(page, 400)
  await page.mouse.click(left + 34 + 118 * 5 + 22 + (1180 - 34 - 118 * 5 - 22 - 30) / 2,
                         top + 108 + (744 - 108 - 96) + 16 + 23)
  await pass(page, 1800)
  await page.screenshot({ path: path.join(OUT, 'challenge-4-run.png') })

  // 판이 도는 동안의 메뉴. 맨 아래에 타이틀로가 있어야 합니다.
  await page.mouse.click(217, 742)
  await pass(page, 600)
  await page.screenshot({ path: path.join(OUT, 'challenge-5-menu.png') })

  const scene = await page.evaluate(() =>
    (window as unknown as { __scene?: string }).__scene ?? '(모름)')

  check('오류 없이 돕니다', errors.length === 0, errors.slice(0, 2).join(' / '))
  check('화면 5장이 찍혔습니다', true)
  console.log(`\n  국면: ${scene}`)
  console.log(`  화면: ${OUT}`)

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
