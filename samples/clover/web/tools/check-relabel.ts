// 말을 바꾸면 화면이 멈추지 않는가.
//
//     npx tsx tools/check-relabel.ts
//
// **누른 그 자리에서 자기를 지우면 화면이 멈춥니다.** 옵션의 고르기 칸이 그러했습니다 —
// 누름을 처리하는 중에 `onChange` 가 판 전체를 지우고, 그다음 차례를 기다리던 것이
// 없어진 객체를 만났습니다.
//
// 그래서 이 확인이 보는 것은 「글이 바뀌었는가」가 아니라 **「그다음에도 눌리는가」**
// 입니다. 글이 바뀌는 것은 `check-lang.ts` 가 데이터로 봅니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  closeGuide, MENU_BUTTON, TITLE_START
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5215

/** 로그인 화면의 「계정 없이 시작하기」와 말 칩. 자리가 고정입니다. */
const SINGLE = { x: 640, y: 800 - 214 + 26 }
const LANG_CHIP = { x: 1280 - 30 - 66, y: 47 }
/** 펼쳐진 목록의 둘째 줄 — 영어입니다. */
const LANG_EN = { x: LANG_CHIP.x, y: 30 + 40 + 32 * 1 + 15 }

/** 타이틀의 옵션 아이콘. 바의 오른쪽 끝입니다. */
const DOCK_Y = 800 - 216
const ROW_Y = DOCK_Y + 26 + 34 + 10
const OPTIONS = { x: 1280 - 26 - 31, y: ROW_Y + 31 }

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail === '' ? '' : `  — ${detail}`}`)
}

interface Peek {
  scene: string
  modalUp: boolean
  netBusy: boolean
  /** 눌러야 하는 것들의 자리. 옵션 판의 고르기 칸도 여기에 옵니다. */
  spots?: Record<string, { x: number; y: number } | undefined>
}

async function peek(page: Page): Promise<Peek> {
  for (let wait = 0; wait < 30; wait++) {
    const seen = await page.evaluate('window.__clover') as Peek | undefined
    if (seen) return seen
    await page.waitForTimeout(200)
  }
  throw new Error('화면이 상태를 알리지 않습니다')
}

async function main(): Promise<number> {
  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: PORT }, logLevel: 'error',
  })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ locale: 'ko-KR', viewport: { width: 1280, height: 800 } })

  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  await page.addInitScript(
    'window.addEventListener("unhandledrejection", e =>'
    + ' ((window.__rejects = window.__rejects || []).push(String(e.reason))))')

  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'domcontentloaded' })
  await peek(page)
  await page.waitForTimeout(1_400)

  // 1) 로그인 화면에서 말을 바꿉니다.
  await page.mouse.click(LANG_CHIP.x, LANG_CHIP.y)
  await page.waitForTimeout(500)
  await page.mouse.click(LANG_EN.x, LANG_EN.y)
  await page.waitForTimeout(900)

  const afterLang = await peek(page)
  check('로그인 화면에서 말을 바꿔도 삽니다', afterLang.scene === 'login', afterLang.scene)

  // **그다음에도 눌립니까.** 멈춘 화면은 그림이 남아 있어도 눌리지 않습니다.
  await page.mouse.click(SINGLE.x, SINGLE.y)
  await page.waitForTimeout(1_200)
  const onTitle = await peek(page)
  check('그다음 누름이 먹습니다', onTitle.scene === 'title', onTitle.scene)

  // 2) 옵션을 열고 닫습니다.
  //
  // **옵션 안의 칸을 이 도구가 짚지 못합니다.** 줄의 자리가 탭과 글 길이에 따라 달라지고,
  // 좌표를 못박으면 옵션에 줄이 하나 늘 때마다 이 도구가 거짓으로 통과합니다. 그래서
  // 여기서는 판이 열리고 닫히는 것까지만 봅니다 — **말을 바꾸는 그 길 자체는 위의 1)이
  // 지납니다.** `applyOptions` 가 `optionsPanel.relabel()` 을 부르므로 옵션 판을 지우고
  // 다시 만드는 일이 거기서 일어납니다.
  //
  // 남는 것은 「옵션 판 **안에서** 누른 그 순간에 지워지는가」 하나이고, 그것은 사람이
  // 봅니다.
  await page.mouse.click(OPTIONS.x, OPTIONS.y)
  await page.waitForTimeout(1_000)
  check('옵션이 열립니다', (await peek(page)).modalUp)

  await page.keyboard.press('Escape')
  await page.waitForTimeout(600)
  check('옵션이 닫힙니다', !(await peek(page)).modalUp)

  const alive = await peek(page)
  check('그 뒤에도 화면이 삽니다', alive.scene === 'title', alive.scene)

  // 3) **판이 도는 중에 옵션 판 안에서 말을 바꿉니다.**
  //
  // 여기가 오래 비어 있던 자리입니다 — 위의 1)은 로그인 화면의 말 칩이고, 그것은 자기를
  // 지우지 않습니다. 옵션 판은 말이 바뀌면 자기를 다시 세우므로, **누른 칸이 그 누름을
  // 처리하는 중에 지워지는 유일한 자리**입니다. 그리고 실제로 그 자리에서 화면이 멈췄습니다.
  //
  // 칸의 자리는 화면이 알립니다. 좌표를 못박으면 줄이 하나 늘 때마다 빈자리를 누르고
  // 통과합니다 — 이 확인이 한동안 그러했습니다.
  await page.mouse.click(TITLE_START.x, TITLE_START.y)
  await page.waitForTimeout(1_200)
  await closeGuide(page)
  await page.waitForTimeout(400)
  check('판이 섰습니다', (await peek(page)).scene === 'run', (await peek(page)).scene)

  await page.mouse.click(MENU_BUTTON.x, MENU_BUTTON.y)
  await page.waitForTimeout(700)
  // 메뉴의 첫 줄이 옵션입니다. 판은 화면 가운데에 섭니다.
  const menuH = 46 + 18 + 3 * 46 + 8
  await page.mouse.click(640, (800 - menuH) / 2 + 46 + 18 + 19)
  await page.waitForTimeout(800)
  check('판이 도는 중에 옵션이 열립니다', (await peek(page)).modalUp)

  // **여섯 말을 차례로 누릅니다.** 한 번만 눌러 보면 그 한 번이 지나가는 것만 확인됩니다.
  for (const want of ['en', 'ja', 'zh-Hans', 'zh-Hant', 'de', 'ko']) {
    const spot = (await peek(page)).spots?.[`option:language:${want}`]
    if (spot === undefined) {
      check(`${want} 칸이 화면에 있습니다`, false, '화면이 자리를 알리지 않습니다')
      break
    }
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(600)
    const after = await peek(page)
    // **판이 그대로 떠 있고 그다음도 눌립니까.** 멈춘 화면은 그림이 남아 있어도 그다음
    // 누름이 먹지 않으므로, 다음 바퀴가 그것을 확인합니다.
    check(`${want} 로 바꿔도 삽니다`,
          after.scene === 'run' && after.modalUp && errors.length === 0,
          `${after.scene} · 판 ${after.modalUp} · 오류 ${errors.length}`)
    if (errors.length > 0) break
  }

  await page.keyboard.press('Escape')
  await page.waitForTimeout(600)
  const back = await peek(page)
  check('바꾼 뒤에도 판이 닫히고 화면이 삽니다',
        !back.modalUp && back.scene === 'run', `${back.scene} · 판 ${back.modalUp}`)

  const rejects = await page.evaluate('window.__rejects || []') as string[]
  check('오류가 없습니다', errors.length === 0 && rejects.length === 0,
        [...errors, ...rejects].slice(0, 2).join(' | ').slice(0, 200))

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
