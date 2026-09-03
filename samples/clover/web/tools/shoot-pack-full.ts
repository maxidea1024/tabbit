// 팩을 뜯었는데 자리가 없을 때.
//
// **눌렀는데 아무 일도 없는 것이 가장 나쁩니다.** 코어는 자리가 없으면 아무것도 하지
// 않는데, 화면이 그것을 모른 채 소리와 조각을 내고 있었습니다.
//
// 그리고 뜯은 팩 위로 왼쪽 아래 버튼이 올라오지 않는지도 함께 봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, buyAffordablePack, chooseFive, clickPrimary, discardHand, grantConsumable, grantJoker,
  grantMoney, peek, playHand, rate, settle, spare, STAGE_W, skipLogin, TITLE_START,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5211

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('console', m => { if (m.text().includes('[팩]')) console.log(m.text()) })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-PACK1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
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
  await page.waitForTimeout(1600)

  // 칸을 전부 채우고 돈을 넉넉히 둡니다. **어느 갈래의 팩이 나와도 자리가 없어야 합니다.**
  await grantConsumable(page, 2)
  await grantJoker(page, 5)
  await grantMoney(page, 40)
  await page.waitForTimeout(500)

  await buyAffordablePack(page)
  if (!(await peek(page)).packOpen) {
    console.log('팩을 살 돈이 없습니다')
    await browser.close()
    await server.close()
    return 1
  }
  await page.waitForTimeout(900)
  await shot(page, 'packfull-1')
  console.log(await page.evaluate(() => {
    const hook = (window as unknown as { __clover: Record<string, unknown> }).__clover
    return { 소모품: hook.consumables, 팩: hook.packOpen }
  }))

  // 가운데 카드를 누릅니다. 바꾸기 판이 서야 합니다.
  const middle = await at(page, STAGE_W / 2, 430)
  await page.mouse.move(middle.x, middle.y)
  await page.waitForTimeout(200)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(700)
  await shot(page, 'packfull-2')

  const before = await peek(page)
  // 바꾸기 판의 첫 줄. 판이 화면 가운데에 서고 첫 줄은 그 가운데에서 조금 위입니다.
  const row = await at(page, STAGE_W / 2, 403)
  await page.mouse.click(row.x, row.y)
  // 판 돈이 솟는 그 순간을 잡습니다.
  await page.waitForTimeout(320)
  await shot(page, 'packfull-coin')
  await page.waitForTimeout(1400)
  const after = await peek(page)
  console.log('팩', before.packOpen, '->', after.packOpen,
    '· 소모품', before.consumables, '->', after.consumables,
    '· 금액', before.money, '->', after.money)
  await shot(page, 'packfull-3')
  if (after.packOpen) {
    console.log('바꾸지 못했습니다')
    await browser.close()
    await server.close()
    return 1
  }
  console.log('바꿔서 집었습니다')

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
