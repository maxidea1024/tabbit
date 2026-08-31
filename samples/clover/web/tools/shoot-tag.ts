// 블라인드를 건너뛸 때 무엇을 받는지가 적혀 있는가, 받은 것이 보이는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, peek, settle, STAGE_W } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5204

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TAG1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, STAGE_W / 2, 446 + 27)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(600)
  await shot(page, 'tag-blind')

  // 스몰의 「건너뛴다」. 판의 아랫변에서 셉니다.
  const cardW = 226
  const gap = 20
  const boardX = (16 + 264 + 20 + STAGE_W) / 2
  const startX = boardX - (2 * (cardW + gap)) / 2 - cardW / 2
  const skip = await at(page, startX + cardW / 2, 754 - 52 + 18)
  await page.mouse.move(skip.x, skip.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await settle(page)
  await page.waitForTimeout(700)
  await shot(page, 'tag-gained')

  // 빅도 건너뜁니다. 태그가 둘 쌓입니다.
  const skip2 = await at(page, startX + (cardW + gap) + cardW / 2, 754 - 52 + 18)
  await page.mouse.move(skip2.x, skip2.y)
  await page.waitForTimeout(120)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await settle(page)
  await page.waitForTimeout(1800)
  await shot(page, 'tag-two')

  console.log('국면', (await peek(page)).phase)
  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
