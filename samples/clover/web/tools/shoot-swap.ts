// 자리가 없을 때 뜨는 바꾸기 판. **설명이 두 줄인 조커가 딱지 밖으로 나가는지를 봅니다.**
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickPrimary, grantJoker, settle, shopSlot, skipLogin, TITLE_START, winRound,
} from './harness'

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

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)

  // 조커 칸을 꽉 채우고 조커를 삽니다.
  await grantJoker(page, 5)
  await page.waitForTimeout(600)
  const tile = await shopSlot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(900)

  await shot(page, 'swap-1')
  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
