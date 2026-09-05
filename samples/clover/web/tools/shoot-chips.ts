// 득점하는 동안 칩이 날아가는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  chooseFive, clickPrimary, closeGuide, peek, pickCards, pressPlay, skipLogin,
  startNewRun
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5205

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-CHIP1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await startNewRun(page)
  await page.waitForTimeout(900)
  await closeGuide(page)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await page.waitForTimeout(2000)

  const state = await peek(page)
  await pickCards(page, chooseFive(state.hand))
  await page.waitForTimeout(400)
  await pressPlay(page)

  // 득점이 도는 동안을 여섯 장으로.
  for (const [index, wait] of [700, 300, 300, 300, 300, 400].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `chip-${index + 1}`)
  }

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
