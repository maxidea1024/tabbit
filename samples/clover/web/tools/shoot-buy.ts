// 상점에서 하나 사는 그 동안.
//
// **울렁 → 이동 → 안착**입니다. 그리고 그 내내 상점 판이 서 있어야 합니다 — 큰 판이
// 사라졌다 다시 서면 무엇을 산 것보다 판이 없어진 것이 먼저 보입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  grantMoney, openRun, settle, shopBuySpot, shopSlot, skipLogin, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5209

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BUY1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await openRun(page)

  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)

  // **살 돈을 넣습니다.** 훅으로 이긴 라운드의 정산은 조커 값에 못 미칩니다.
  await grantMoney(page, 40)
  await settle(page)
  await page.waitForTimeout(600)

  await shot(page, 'buy-0')

  // 첫 칸을 삽니다. **두 번 누릅니다** — 딱지를 누르는 것은 고르는 것까지이고, 사는 것은
  // 그 밑에 서는 단추입니다. 한 번만 누르고 여덟 장을 굽던 동안 이 도구는 사지 않은 상점을
  // 여덟 번 찍고 있었습니다.
  const tile = await shopSlot(page, 0)
  await page.mouse.click(tile.x, tile.y)
  await page.waitForTimeout(300)
  const buy = await shopBuySpot(page)
  await page.mouse.click(buy.x, buy.y)

  // 사는 그 동안을 여덟 장으로. **연출의 시계로 잡습니다** — 스크린샷 자체가 시간을
  // 먹으므로 기다린 시간만으로는 어느 순간인지 알 수 없습니다.
  for (let i = 1; i <= 8; i++) {
    await shot(page, `buy-${i}`)
  }
  await page.waitForTimeout(600)
  await shot(page, 'buy-9')

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
