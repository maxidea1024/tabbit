// 상점에서 하나 사는 그 동안.
//
// **울렁 → 이동 → 안착**입니다. 그리고 그 내내 상점 판이 서 있어야 합니다 — 큰 판이
// 사라졌다 다시 서면 무엇을 산 것보다 판이 없어진 것이 먼저 보입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, discardHand, peek, playHand, rate, settle, shopSlot, spare,
  STAGE_W,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5209

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BUY1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, STAGE_W / 2, 436 + 27)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  for (let turn = 0; turn < 40; turn++) {
    const state = await peek(page)
    if (state.phase !== 'round') break
    const picks = chooseFive(state.hand)
    if (rate(picks.map(i => state.hand[i])) < 60 && state.discards > 0) {
      await discardHand(page, spare(state.hand, picks))
    } else {
      await playHand(page, picks)
    }
    await settle(page)
    await page.waitForTimeout(200)
  }
  await page.waitForTimeout(1400)
  const h = 46 + 16 + 2 * 34 + 14 + 56
  const take = await at(page, STAGE_W / 2, (800 - h) / 2 + h - 56 / 2)
  await page.mouse.click(take.x, take.y)
  await page.waitForTimeout(1800)

  await shot(page, 'buy-0')

  // 첫 칸을 삽니다.
  const tile = await shopSlot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()

  // 사는 그 동안을 여덟 장으로. **연출의 시계로 잡습니다** — 스크린샷 자체가 시간을
  // 먹으므로 기다린 시간만으로는 어느 순간인지 알 수 없습니다.
  for (let i = 1; i <= 8; i++) {
    await shot(page, `buy-${i}`)
  }
  await page.waitForTimeout(600)
  await shot(page, 'buy-9')

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
