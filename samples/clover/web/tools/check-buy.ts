// 산 것이 오는 길이 보이는가.
//
// **스크린샷으로는 잡히지 않습니다** — 한 장 찍는 데 0.2초가 걸려서, 0.5초짜리 연출은
// 찍는 사이에 지나갑니다. 조커가 그려진 자리를 30밀리초마다 물어봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickPrimary, grantMoney, pass, peek, settle, shopBuySpot, shopSlot, skipLogin, TITLE_START,
  winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5210

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BUY1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await pass(page, 900)
  await page.mouse.click(20, 20)
  await pass(page, 400)
  await clickPrimary(page)
  await settle(page)

  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)

  // **딱지를 누르는 것은 고르는 것까지입니다.** 사는 것은 그 밑의 「산다」 입니다 —
  // 딱지만 누르고 오는 길을 재고 있어서, 아무것도 사지 않은 채 「어긋납니다」 로 끝났습니다.
  // **살 돈을 넣고 조커가 선 칸을 짚습니다.** 훅으로 이긴 라운드의 정산은 조커 값에 못
  // 미치고, 소모품 칸을 사면 `jokerX` 가 잴 것이 없습니다.
  await grantMoney(page, 40)
  await settle(page)
  await pass(page, 600)
  const slot = ((await peek(page)).shopKinds ?? []).indexOf(1)
  if (slot < 0) {
    console.log('상점에 조커가 없습니다. 시드를 바꿔야 합니다')
    await browser.close()
    await server.close()
    return 1
  }
  const tile = await shopSlot(page, slot)
  await page.mouse.move(tile.x, tile.y)
  await pass(page, 120)
  await page.mouse.down()
  await pass(page, 60)
  await page.mouse.up()
  await pass(page, 350)
  const buy = await shopBuySpot(page)
  await page.mouse.click(buy.x, buy.y)

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
    await pass(page, 30)
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
