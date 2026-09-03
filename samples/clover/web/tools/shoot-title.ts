// 타이틀과 정산 판. **누를 것이 하나로 남았는지, 정산의 뼈대 줄이 도는지 봅니다.**
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, discardHand, peek, playHand, rate, settle, spare, STAGE_W,
  TITLE_START_Y, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5215

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TITLE1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1600)
  await shot(page, 'title-1')

  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  // 블라인드를 넘깁니다. 정산 판이 서는 그 순간을 잡습니다.
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

  // 판이 열리자마자 · 줄이 쌓이는 중 · 다 선 뒤.
  for (let i = 0; i < 90; i++) {
    if ((await peek(page)).payout) break
    await page.waitForTimeout(50)
  }
  await shot(page, 'payout-1')
  await shot(page, 'payout-2')
  await page.waitForTimeout(700)
  await shot(page, 'payout-3')

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
