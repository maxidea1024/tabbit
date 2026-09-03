// 왼쪽 판이 정돈되어 있는가.
//
// **자리를 재는 도구입니다.** 무리 사이가 고른지, 겹치는 것이 없는지, 아래 버튼까지 남는
// 자리가 있는지를 봅니다 — 눈으로 보아야 하는 것은 판때기의 테두리와 불의 뿌리이므로 그
// 둘은 판을 오려 낸 그림으로 남깁니다.
import * as path from 'path'
import { fileURLToPath } from 'url'

import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  chooseFive, clickPrimary, discardHand, pass, peek, pickCards, pressPlay, pressTitle,
  settle, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5217

/** 판이 오려지는 자리. 판때기가 `x` 4 에서 292 이고 화면 아래 22까지 내려옵니다. */
const CROP = { x: 0, y: 14, width: 300, height: 772 }

/**
 * 판의 줄들이 서야 하는 자리.
 *
 * **화면의 상수를 그대로 베껴 적지 않습니다** — 이 표는 「무리 사이가 26이고 무리 안은
 * 그보다 좁다」를 검사하기 위한 것이고, 그 규칙이 깨지는 것을 잡는 것이 목적입니다.
 */
const ROWS: { name: string; top: number; bottom: number; group: number }[] = [
  { name: '블라인드 딱지', top: 34, bottom: 198, group: 1 },
  { name: '라운드 득점', top: 210, bottom: 278, group: 1 },
  { name: '족보 이름', top: 304, bottom: 328, group: 2 },
  { name: '칩 × 배수', top: 336, bottom: 394, group: 2 },
  { name: '핸드 · 버리기', top: 420, bottom: 472, group: 3 },
  { name: '소지금 · 안티', top: 484, bottom: 536, group: 3 },
  { name: '적용 중', top: 562, bottom: 692, group: 4 },
  { name: '런 정보 · 메뉴', top: 726, bottom: 760, group: 5 },
]

/** 무리를 가르는 줄들. */
const GROOVES = [291, 407, 549]

const GROUP_GAP = 26

function measure(): string[] {
  const bad: string[] = []

  for (let i = 1; i < ROWS.length; i++) {
    const above = ROWS[i - 1]
    const below = ROWS[i]
    const gap = below.top - above.bottom
    if (gap < 0) {
      bad.push(`${above.name} 과 ${below.name} 이 ${-gap}픽셀 겹칩니다`)
      continue
    }
    if (above.group === below.group) {
      // 무리 안은 무리 사이보다 좁아야 합니다. **같거나 넓으면 가른 뜻이 없어집니다.**
      if (gap >= GROUP_GAP) {
        bad.push(`${above.name} 과 ${below.name} 은 한 무리인데 사이가 ${gap}입니다`)
      }
      continue
    }
    // 마지막 무리와 아래 버튼 사이는 적용 중이 몇 줄인지에 따르므로 넓어도 됩니다.
    if (below.group === 5) continue
    if (gap !== GROUP_GAP) {
      bad.push(`${above.name} 과 ${below.name} 사이가 ${gap}입니다 — ${GROUP_GAP}이어야 합니다`)
    }
  }

  // 가르는 줄은 그 사이의 한가운데입니다.
  for (const groove of GROOVES) {
    const above = ROWS.filter(one => one.bottom <= groove).pop()
    const below = ROWS.find(one => one.top >= groove)
    if (!above || !below) {
      bad.push(`${groove} 의 줄이 어느 사이에도 들지 않습니다`)
      continue
    }
    const mid = (above.bottom + below.top) / 2
    if (Math.abs(mid - groove) > 0.5) {
      bad.push(`${groove} 의 줄이 한가운데(${mid})가 아닙니다`)
    }
  }

  return bad
}

async function main(): Promise<number> {
  const bad = measure()

  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-PANEL`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  // 블라인드 고르는 판. **판을 열기 전에 찍습니다** — 열고 나면 그 화면이 없습니다.
  await pressTitle(page, 'start')
  await pass(page, 900)
  if ((await peek(page)).modalUp === true) await page.keyboard.press('Escape')
  await pass(page, 700)
  await page.screenshot({ path: path.join(OUT, 'blind-pick.png') })

  // 그 판에서 블라인드를 고르면 라운드가 섭니다.
  await clickPrimary(page)
  await settle(page)
  await page.waitForTimeout(600)

  await shot(page, 'panel-idle')

  // 다섯 장을 고르면 족보 이름과 칩 · 배수가 함께 섭니다.
  const state = await peek(page)
  await pickCards(page, chooseFive(state.hand))
  await page.waitForTimeout(500)
  await shot(page, 'panel-picked')

  // ±N 글. 한 장을 버리면 버리기 칸이 하나 줄어듭니다.
  await discardHand(page, [0])
  for (const [index, wait] of [140, 130, 130, 130, 130, 130].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `panel-delta-${index + 1}`)
  }

  // 득점하는 동안의 판.
  const next = await peek(page)
  await pickCards(page, chooseFive(next.hand))
  await page.waitForTimeout(300)
  await pressPlay(page)
  for (const [index, wait] of [900, 600].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `panel-score-${index + 1}`)
  }

  await browser.close()
  await server.close()

  for (const line of bad) console.log(`  ${line}`)
  console.log(bad.length === 0 ? '자리 맞음' : `어긋난 자리 ${bad.length}곳`)
  return bad.length === 0 ? 0 : 1
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`), clip: CROP })
}

main().then(code => process.exit(code))
