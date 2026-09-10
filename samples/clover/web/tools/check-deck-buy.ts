// 상점에서 산 플레잉 카드가 덱으로 가는가.
//
// **소모품 칸으로 가는 것으로 보였습니다.** 코어는 덱에 넣는데 화면에는 그 길이 없었고,
// 닿았다고 알리는 자리가 「조커가 아니면 소모품」으로 세고 있어서 산 카드의 이름이 아무
// 상관 없는 소모품 칸 위에 떴습니다 — 팩에서 집는 길에는 이미 있던 갈래입니다.
//
// 재는 것 넷입니다. 덱이 한 장 늘었는가 · 소모품 칸은 그대로인가 · 덱이 나와서 받았는가 ·
// 상점이 그동안 물러났다가 돌아왔는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  grantConsumable, grantMoney, openRun, pass, peek, settle, shopBuySpot, shopSlot,
  skipLogin, stockPlayingCard, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5231

/** 한 표본. 덱이 어디까지 나왔는지와 상점이 어디까지 내려갔는지입니다. */
interface Sample {
  deckPeek: boolean
  deckX: number
  shopParked: boolean
  shopY: number
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') console.log('  [콘솔]', one.text().slice(0, 200))
  })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-DECKBUY&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  await winRound(page)
  await grantMoney(page, 40)
  // **돈이 다 세어질 때까지 기다립니다.** 사는 것은 화면이 주장하는 금액으로 판정하고,
  // 그 금액은 동전이 날아가 꽂히는 동안 올라갑니다.
  await settle(page)
  await pass(page, 600)

  // **소모품 한 장을 들려 둡니다.** 이 결함은 소모품 칸이 비어 있으면 나타나지 않습니다 —
  // 닿았다고 알리는 자리가 마지막 소모품을 찾아 그 위에 이름을 띄우는 것이라, 찾을 것이
  // 없으면 아무 일도 하지 않고 돌아갑니다.
  await grantConsumable(page, 1)
  await pass(page, 300)

  // 첫 칸을 플레잉 카드로 갈아 놓습니다. 시드는 이 칸을 정해 주지 못합니다.
  await stockPlayingCard(page)
  await pass(page, 400)
  const kinds = (await peek(page)).shopKinds ?? []
  if (kinds[0] !== 4) {
    console.log('첫 칸이 플레잉 카드가 아닙니다:', kinds.join(' '))
    await browser.close()
    await server.close()
    return 1
  }

  const before = await peek(page)
  console.log('사기 전 · 덱', before.deckSize, '· 뽑을 패', before.drawLeft,
              '· 소모품', before.consumables)

  // **딱지를 누르는 것은 고르는 것까지입니다.** 사는 것은 그 밑의 「산다」입니다.
  const tile = await shopSlot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await pass(page, 120)
  await page.mouse.down()
  await pass(page, 60)
  await page.mouse.up()
  await pass(page, 350)
  const buy = await shopBuySpot(page)
  await page.mouse.click(buy.x, buy.y)

  // 값을 치르는 박자 · 날아가는 데 · 덱이 나와 있는 시간까지 봅니다.
  const track: Sample[] = []
  const said = new Set<string>()
  const trays = before.trays
  let onItemTray = 0
  for (let i = 0; i < 110; i++) {
    const now = await peek(page)
    track.push({
      deckPeek: now.deckPeek === true,
      deckX: Math.round(now.deckX ?? 300),
      shopParked: now.shopParked === true,
      shopY: Math.round(now.shopY ?? 0),
    })
    for (const [text, x, y] of now.pops ?? []) {
      said.add(text)
      // **소모품 줄 위에 뜨는 글이 있으면 그것이 이 결함입니다.**
      const box = trays?.item
      if (box && x >= box.x && x <= box.x + box.width
        && y >= box.y - 40 && y <= box.y + box.height) onItemTray++
    }
    await pass(page, 30)
  }

  const after = await peek(page)
  console.log('산 뒤 · 덱', after.deckSize, '· 뽑을 패', after.drawLeft,
              '· 소모품', after.consumables)

  const deckGrew = (after.deckSize ?? 0) === (before.deckSize ?? 0) + 1
  const drawGrew = (after.drawLeft ?? 0) === (before.drawLeft ?? 0) + 1
  const itemsSame = after.consumables === before.consumables
  const peeked = track.filter(one => one.deckPeek).length
  const cameOut = track.some(one => one.deckX < 40)
  const parked = track.filter(one => one.shopParked).length
  const backUp = track[track.length - 1].shopY < 40
  const added = said.has('덱에 더해졌습니다')

  console.log('덱이 나와 있던 표본', peeked, '/', track.length,
              '· 제자리까지 나왔는가', cameOut)
  console.log('상점이 물러나 있던 표본', parked, '· 끝에 돌아왔는가', backUp)
  console.log('뜬 글', [...said].join(' · ') || '없음')
  console.log('소모품 줄 위에 뜬 글', onItemTray, '건')

  const good = deckGrew && drawGrew && itemsSame && peeked > 0 && cameOut
    && parked > 0 && backUp && added && onItemTray === 0
  console.log(good ? '산 카드가 덱으로 들어갑니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
