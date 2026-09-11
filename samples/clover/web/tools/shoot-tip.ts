// 쪽지가 어디서나 같은 체계로 뜨는가. **눈으로만 판정됩니다.**
//
// 넉 장을 굽습니다 — 조커를 가리킨 것 · 소모품을 가리킨 것 · 도감의 칸을 가리킨 것 ·
// 상점의 칸을 가리킨 것. 가리킨 것이 들리고 커지는지와, 쪽지의 글자가 판을 덮지 않는지를
// 함께 봅니다.
//
//     npx tsx tools/shoot-tip.ts
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, grantConsumableId, grantJoker, grantMoney, openRun, pass, pressTitle, settle,
  shopSlot, skipLogin, spot, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5262

/** 그 자리로 커서를 옮기고 쪽지가 뜰 만큼 기다렸다 굽습니다. */
async function hover(page: Page, where: { x: number; y: number }, name: string): Promise<void> {
  // **한 픽셀 비껴 들어갑니다.** 같은 자리에 이미 있으면 「들어왔다」가 나지 않습니다.
  await page.mouse.move(where.x - 40, where.y - 40)
  await pass(page, 120)
  await page.mouse.move(where.x, where.y)
  await pass(page, 500)
  await page.screenshot({ path: path.join(OUT, `tip-${name}.png`) })
  console.log('  구움', name)
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TIP1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // 1. 도감. 판을 열기 전이므로 타이틀에서 갑니다.
  await pressTitle(page, 'collection')
  await pass(page, 800)
  const cell = await spot(page, 'collection:tab:joker')
  // 탭이 아니라 격자의 칸입니다. 첫 칸은 탭 줄 아래에 있습니다.
  await hover(page, { x: cell.x + 60, y: cell.y + 150 }, 'collection')
  await page.keyboard.press('Escape')
  await pass(page, 600)

  // 2. 판 위의 조커와 소모품.
  await openRun(page)
  await pass(page, 900)
  await grantJoker(page, 3)
  await grantConsumableId(page, 'the_fool')
  await pass(page, 500)
  await hover(page, await at(page, (await spot(page, 'joker:0')).x,
                             (await spot(page, 'joker:0')).y), 'joker')
  const item = await spot(page, 'item:0')
  await hover(page, await at(page, item.x, item.y), 'item')

  // 3. 상점의 칸.
  await winRound(page)
  await grantMoney(page, 60)
  await settle(page)
  await pass(page, 900)
  const tile = await shopSlot(page, 0)
  await hover(page, tile, 'shop')

  await browser.close()
  await server.close()
  return 0
}

main().then(code => process.exit(code))
