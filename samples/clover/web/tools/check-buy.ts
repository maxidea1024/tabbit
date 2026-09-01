// 산 것이 오는 길이 보이는가.
//
// **스크린샷으로는 잡히지 않습니다** — 한 장 찍는 데 0.2초가 걸려서, 0.5초짜리 연출은
// 찍는 사이에 지나갑니다. 조커가 그려진 자리를 30밀리초마다 물어봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  chooseFive, clickPrimary, discardHand, peek, playHand, rate, settle, shopSlot, spare, at,
  STAGE_W, TITLE_START_Y,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5210

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BUY1`, { waitUntil: 'networkidle' })
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
  const h = 46 + 16 + 2 * 34 + 14 + 56
  const take = await at(page, STAGE_W / 2, (800 - h) / 2 + h - 56 / 2)
  await page.mouse.click(take.x, take.y)
  await page.waitForTimeout(1800)

  const tile = await shopSlot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()

  // 자리와 상점이 서 있는지를 함께 봅니다.
  const track: { at: number; x: number; shop: boolean }[] = []
  const began = await now(page)
  for (let i = 0; i < 40; i++) {
    const sample = await page.evaluate(() => {
      const hook = (window as unknown as {
        __clover: { jokerX?(): number | undefined; shopUp?: boolean; clock?: number }
      }).__clover
      return { x: hook.jokerX?.() ?? -1, shop: hook.shopUp === true, clock: hook.clock ?? 0 }
    })
    track.push({ at: sample.clock - began, x: Math.round(sample.x), shop: sample.shop })
    await page.waitForTimeout(30)
  }

  const moving = track.filter(one => one.x >= 0)
  const first = moving[0]
  const last = moving[moving.length - 1]
  const span = moving.filter((one, i) => i > 0 && Math.abs(one.x - moving[i - 1].x) > 0.5)
  console.log('처음 자리', first?.x, '· 마지막 자리', last?.x)
  console.log('자리가 바뀐 표본', span.length, '· 걸린 시간',
    span.length > 0 ? `${(span[span.length - 1].at - first.at).toFixed(2)}초` : '없음')
  console.log('상점이 서 있지 않던 표본', track.filter(one => !one.shop).length, '/', track.length)

  const good = span.length >= 4 && track.every(one => one.shop)
  console.log(good ? '오는 길이 보이고 상점이 서 있습니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

async function now(page: import('playwright').Page): Promise<number> {
  return page.evaluate(
    () => (window as unknown as { __clover: { clock?: number } }).__clover.clock ?? 0)
}

main().then(code => process.exit(code))
