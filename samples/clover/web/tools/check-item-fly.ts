// 산 소모품이 오는 길이 보이는가.
//
// **조커는 날아오는데 소모품은 제 칸에 툭 나타났습니다.** 조커는 뷰가 용수철을 들고 있어서
// 오는 길이 있고, 소모품 칸은 화면을 다시 그릴 때마다 새로 만들어지므로 그럴 것이 없었습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, discardHand, grantMoney, peek, playHand, rate, settle,
  shopSlot, spare, STAGE_W, TITLE_START_Y,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5218

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-FLY1`, { waitUntil: 'networkidle' })
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
  await page.waitForTimeout(1700)
  await grantMoney(page, 40)
  await page.waitForTimeout(400)

  // 소모품이 하나 들어오는 칸을 찾습니다. **조커가 나오면 다음 칸으로 갑니다.**
  let bought = false
  const track: number[] = []
  for (let slot = 0; slot < 4 && !bought; slot++) {
    const before = (await peek(page)).consumables
    const tile = await shopSlot(page, slot)
    await page.mouse.move(tile.x, tile.y)
    await page.waitForTimeout(100)
    await page.mouse.down()
    await page.waitForTimeout(60)
    await page.mouse.up()

    // 오는 길을 잽니다.
    for (let i = 0; i < 24; i++) {
      const spot = await page.evaluate(() => {
        const hook = (window as unknown as {
          __clover: { itemX?(): number | undefined }
        }).__clover
        return hook.itemX?.() ?? -1
      })
      if (spot >= 0) track.push(Math.round(spot))
      await page.waitForTimeout(25)
    }
    bought = (await peek(page)).consumables > before
    if (!bought) track.length = 0
  }

  if (!bought) {
    console.log('소모품을 사지 못했습니다')
    await browser.close()
    await server.close()
    return 1
  }

  const moves = track.filter((one, i) => i > 0 && one !== track[i - 1]).length
  console.log('자리', track.slice(0, 14).join(' → '))
  console.log('자리가 바뀐 표본', moves, '/', track.length)

  await browser.close()
  await server.close()
  const good = moves >= 4
  console.log(good ? '오는 길이 보입니다' : '툭 나타납니다')
  return good ? 0 : 1
}

main().then(code => process.exit(code))
