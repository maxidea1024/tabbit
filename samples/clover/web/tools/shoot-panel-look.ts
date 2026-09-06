// 왼쪽 판이 어떻게 보이는가. **고치는 동안 눈으로 보는 것이 목적입니다.**
//
// 게이트가 아닙니다 — 판정하지 않고 그림만 남깁니다. 칩 × 배수의 바탕, 자원 칸의
// 오르내림 색, ±N 이 뜨는 동안의 겹침을 보려고 만들었습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'

import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  chooseFive, clickPrimary, closeGuide, discardHand, peek, pickCards, pressPlay,
  skipLogin, startNewRun,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/panel-look')
const PORT = 5311
const CROP = { x: 0, y: 14, width: 300, height: 772 }

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`), clip: CROP })
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-LOOK`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await startNewRun(page)
  await page.waitForTimeout(900)
  await closeGuide(page)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await page.waitForTimeout(1800)
  await shot(page, 'idle')

  // 버리면 버리기 칸이 줄고 ±N 이 뜹니다. 그 동안을 촘촘히 봅니다.
  await discardHand(page, [0])
  for (const [index, wait] of [80, 80, 80, 80, 120, 120, 120, 120].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `delta-${String(index + 1).padStart(2, '0')}`)
  }

  // 득점하는 동안의 칩 × 배수.
  const next = await peek(page)
  await pickCards(page, chooseFive(next.hand))
  for (const [index, wait] of [40, 40, 40, 40, 60, 60, 60, 60].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `picked-${String(index + 1).padStart(2, '0')}`)
  }
  await pressPlay(page)
  for (const [index, wait] of [
    60, 60, 60, 60, 60, 60, 120, 120, 120, 200, 200, 300, 300, 600,
  ].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `score-${String(index + 1).padStart(2, '0')}`)
  }

  await browser.close()
  await server.close()
  console.log(`판 그림을 ${OUT} 에 남겼습니다`)
  return 0
}

main().then(code => process.exit(code))
