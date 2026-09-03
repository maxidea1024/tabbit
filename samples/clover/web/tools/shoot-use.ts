// 소모품을 쓰는 네 마디.
//
// **울렁 → 이동 → 번쩍 → 사라짐**입니다. 제자리에서 그냥 타면 무엇을 쓴 것인지가 오른쪽
// 구석의 일로 남습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { grantConsumable, heldButton, itemSpot, openRun, peek, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5212

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-USE1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await openRun(page)

  // 행성 카드 하나를 놓습니다. 대상이 필요 없어서 그냥 쓸 수 있습니다.
  await grantConsumable(page, 1)
  await page.waitForTimeout(600)

  // 카드를 눌러 고릅니다. **자리는 화면이 알립니다** — 소모품은 자기 자리 안에서
  // 가운데로 모이므로 좌표를 적어 두면 빈자리를 누릅니다.
  const tile = await itemSpot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(500)
  await shot(page, 'use-0')

  // 「쓴다」. 고른 카드 아래에 선 첫 단추이고, 화면이 그 자리를 알립니다.
  const use = await heldButton(page)
  await page.mouse.click(use.x, use.y)

  // 네 마디를 여섯 장으로. **연출의 시계도 함께 적습니다.**
  for (let i = 1; i <= 6; i++) {
    const now = await peek(page)
    console.log(`use-${i} 시계 ${now.clock.toFixed(2)}`)
    await shot(page, `use-${i}`)
  }

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
