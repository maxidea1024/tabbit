// 소모품이 타는 것 · 정산에서 상점으로 넘어가는 동안 · 사는 그 순간을 몇 장으로 봅니다.
import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, discardHand, peek, playHand, rate, settle, shopSlot, spare,
  STAGE_W, TITLE_START_Y, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5197 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1680, height: 960 } })
  await skipLogin(page)
  await page.goto('http://localhost:5197/?seed=CLOVER-SHOT6', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
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
  await shot(page, 'order-1')

  // 「받는다」 를 누릅니다. 판의 밑단 띠 가운데입니다.
  const h = 46 + 16 + 2 * 34 + 14 + 56
  const spot = await at(page, 1280 / 2, (800 - h) / 2 + h - 56 / 2)
  await page.mouse.click(spot.x, spot.y)

  // 하나씩 서는 동안을 넉 장으로 봅니다.
  for (const [index, wait] of [200, 250, 300, 700].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `open-${index + 1}`)
  }

  console.log('산 뒤를 봅니다 — 조커', (await peek(page)).jokers, '장')
  // 첫째 칸을 삽니다. 살 수 없으면 그대로 한 장 찍습니다.
  const tile = await shopSlot(page, 0)
  await page.mouse.click(tile.x, tile.y)
  for (const [index, wait] of [90, 130, 200, 400].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `buy-${index + 1}`)
  }

  // 그 칸을 다 삽니다. **칸이 비면 그 칸 자체가 없어져야 합니다.**
  const rest = await shopSlot(page, 0, 1)
  await page.mouse.click(rest.x, rest.y)
  await page.waitForTimeout(900)
  await shot(page, 'sold-1')

  const after = await peek(page)
  console.log('산 뒤 · 조커', after.jokers, '장 · 소모품', after.consumables, '장 · 금액', after.money)

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
