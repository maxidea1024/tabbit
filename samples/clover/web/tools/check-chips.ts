// 득점하는 동안 칩이 실제로 나는가.
//
// **눈으로 찍어서는 놓칩니다** — 한 번 나는 데 0.4초가 채 안 되므로, 50밀리초마다 화면에
// 물어봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, chooseFive, clickPrimary, peek, pickCards, pressPlay, STAGE_W , TITLE_START_Y } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5208

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-CHIP1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await page.waitForTimeout(2000)

  const state = await peek(page)
  await pickCards(page, chooseFive(state.hand))
  await page.waitForTimeout(400)
  await pressPlay(page)

  let seen = 0
  const marks: string[] = []
  for (let i = 0; i < 160; i++) {
    const now = await peek(page)
    if (now.flying) seen++
    marks.push(now.flying ? '칩' : '·')
    if (now.flying && seen <= 3) {
      await page.screenshot({
        path: path.resolve(HERE, `../../design-data/out/check/flying-${seen}.png`) })
    }
    await page.waitForTimeout(50)
  }
  console.log(marks.join(''))
  console.log('칩이 날던 표본', seen, '/ 160')

  await browser.close()
  await server.close()
  return seen > 0 ? 0 : 1
}

main().then(code => process.exit(code))
