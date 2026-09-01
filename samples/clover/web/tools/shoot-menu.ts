// 인게임의 메뉴 판. **닫는 길이 몇 개인지, 안에 든 것이 판을 넘지 않는지 봅니다.**
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, clickPrimary, settle, STAGE_W , TITLE_START_Y } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5214

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-MENU1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  // 왼쪽 아래의 「메뉴」.
  const menu = await at(page, 16 + 134 + 59, 700 + 17)
  await page.mouse.click(menu.x, menu.y)
  await page.waitForTimeout(700)
  await page.screenshot({ path: path.join(OUT, 'menu-1.png') })

  await browser.close()
  await server.close()
  return 0
}

main().then(code => process.exit(code))
