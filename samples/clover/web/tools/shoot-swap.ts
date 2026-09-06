// 자리가 없을 때 줄에서 내놓을 것을 고르는 화면. 상점이 내려가고 · 줄이 밝게 남고 · 글이 서고 ·
// 내놓은 뒤 새 조커가 오는 것까지 넉 장입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { grantJoker, heldButton, openRun, peek, shopSlot, skipLogin, spot, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5202 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto('http://localhost:5202/?seed=CLOVER-SHOT6', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await openRun(page)

  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)

  // 조커 칸을 꽉 채우고 조커를 삽니다.
  await grantJoker(page, 5)
  await page.waitForTimeout(600)
  // **조커가 선 칸을 짚습니다.** 0번 칸을 짚고 있어서 소모품이 서 있으면 그냥 샀고, 그다음
  // 줄의 조커를 눌러 팔았습니다 — 자리를 비우는 화면은 한 장도 찍히지 않았습니다.
  const kinds = (await peek(page)).shopKinds ?? []
  const slot = kinds.indexOf(1)
  if (slot < 0) {
    console.log('상점에 조커가 없습니다. 시드를 바꿔야 합니다:', kinds.join(' '))
    await browser.close()
    await server.close()
    return 1
  }
  const tile = await shopSlot(page, slot)
  await page.mouse.move(tile.x, tile.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(500)
  // **딱지를 누르는 것은 고르는 것까지입니다.** 바꿔 집는 것은 그 밑의 단추입니다.
  const swap = await heldButton(page)
  await page.mouse.click(swap.x, swap.y)
  // 상점이 내려가고 줄에서 고르는 화면이 듭니다.
  await page.waitForTimeout(900)
  await shot(page, 'swap-1')

  // 줄의 첫 조커를 고릅니다. 그 밑에 내놓는 단추가 섭니다.
  const first = await spot(page, 'joker:0')
  await page.mouse.click(first.x, first.y)
  await page.waitForTimeout(400)
  await shot(page, 'swap-2')

  // 내놓습니다. 새 조커가 글 옆의 카드에서 날아오고 상점이 돌아옵니다.
  const give = await heldButton(page)
  await page.mouse.click(give.x, give.y)
  await page.waitForTimeout(350)
  await shot(page, 'swap-3')
  await page.waitForTimeout(1200)
  await shot(page, 'swap-4')
  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
