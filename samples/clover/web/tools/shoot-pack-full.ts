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
  buyAffordablePack, clickSpot, grantConsumable, grantJoker, grantMoney, openRun, peek,
  skipLogin, spot, winRound,
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

  await openRun(page)

  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)

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

  // 펼쳐진 첫 장을 누릅니다. 바꾸기 판이 서야 합니다. **자리는 화면이 알립니다.**
  const middle = await spot(page, 'pack:0')
  await page.mouse.move(middle.x, middle.y)
  await page.waitForTimeout(200)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(400)
  // **누르는 것은 고르는 것까지입니다.** 집는 것은 그 밑에 서는 「바꿔 집는다」 이고, 그
  // 자리는 화면이 알립니다. 자리가 없으므로 그것을 누르면 바꾸기 판이 섭니다.
  await clickSpot(page, 'held')
  await page.waitForTimeout(700)
  await shot(page, 'packfull-2')

  const before = await peek(page)
  // 바꾸기 판의 첫 줄. **자리는 화면이 알립니다** — 줄의 높이가 설명글의 길이로 정해지고
  // 그 길이는 말에 따라 달라지므로, 적어 두면 다른 말에서는 줄 사이를 누릅니다.
  await clickSpot(page, 'swap:0')
  // 판 돈이 솟는 그 순간을 잡습니다.
  await page.waitForTimeout(320)
  await shot(page, 'packfull-coin')
  await page.waitForTimeout(1400)
  const after = await peek(page)
  console.log('팩', before.packOpen, '->', after.packOpen,
    '· 소모품', before.consumables, '->', after.consumables,
    '· 금액', before.money, '->', after.money)
  await shot(page, 'packfull-3')
  // **바꾼 것은 판 돈으로 봅니다.** 내놓은 것의 값이 들어오므로 금액이 오릅니다. 팩이
  // 닫혔는지로 보면 두 장을 집는 팩에서는 한 장을 바꿔 집고도 열려 있어 「못 바꿨다」 가
  // 되고, 소모품 수로 보면 하나 나가고 하나 들어와 그대로입니다.
  if (after.money <= before.money) {
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
