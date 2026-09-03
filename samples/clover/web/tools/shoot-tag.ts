// 블라인드를 건너뛸 때 무엇을 받는지가 적혀 있는가, 받은 것이 보이는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, peek, settle, skipLogin, TITLE_START } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5204

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TAG1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(600)
  await shot(page, 'tag-blind')

  // 「건너뛴다」. **화면이 알린 자리를 누릅니다.**
  await tapSkip(page)
  await settle(page)
  await page.waitForTimeout(700)
  await shot(page, 'tag-gained')

  // 빅도 건너뜁니다. 태그가 둘 쌓입니다.
  await tapSkip(page)
  await settle(page)
  await page.waitForTimeout(1800)
  await shot(page, 'tag-two')

  console.log('국면', (await peek(page)).phase)
  await browser.close()
  await server.close()
  return 0
}

/** 지금 블라인드의 「건너뛴다」 를 누릅니다. */
async function tapSkip(page: Page): Promise<void> {
  const skip = (await peek(page)).spots?.skip
  if (!skip) throw new Error('건너뛰기 버튼의 자리를 화면이 알리지 않았습니다')
  const spot = await at(page, skip.x, skip.y)
  await page.mouse.move(spot.x, spot.y)
  await page.waitForTimeout(100)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
