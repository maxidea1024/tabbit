// 팩을 뜯은 화면을 몇 장으로 봅니다.
import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, buyAffordablePack, chooseFive, clickPrimary, discardHand, peek, playHand, rate, settle,
  spare, STAGE_W, skipLogin, TITLE_START,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5199 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1680, height: 960 } })
  await skipLogin(page)
  await page.goto('http://localhost:5199/?seed=CLOVER-PACK1', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  // 블라인드를 하나 넘기고 정산을 받습니다.
  for (let turn = 0; turn < 40; turn++) {
    const state = await peek(page)
    if (state.phase !== 'round') break
    const picks = chooseFive(state.hand)
    if (rate(picks.map(i => state.hand[i])) < 60 && state.discards > 0) {
      await discardHand(page, spare(state.hand, picks))
    } else {
      await playHand(page, picks)
    }
    await settle(page)
    await page.waitForTimeout(200)
  }
  await page.waitForTimeout(1400)
  const h = 46 + 16 + 2 * 34 + 14 + 56
  const take = await at(page, STAGE_W / 2, (800 - h) / 2 + h - 56 / 2)
  await page.mouse.click(take.x, take.y)
  await page.waitForTimeout(1400)

  await buyAffordablePack(page)
  if (!(await peek(page)).packOpen) {
    console.log('팩을 살 돈이 없습니다')
    await shot(page, 'pack-none')
    await browser.close()
    await server.close()
    return 1
  }

  // 펼쳐지는 동안을 석 장으로.
  for (const [index, wait] of [180, 260, 500].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `pack-${index + 1}`)
  }

  // 가운데 카드에 마우스를 올립니다.
  const middle = await at(page, STAGE_W / 2, 430)
  await page.mouse.move(middle.x, middle.y)
  await page.waitForTimeout(400)
  await shot(page, 'pack-hover')

  // 그 카드를 집습니다.
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(700)
  await shot(page, 'pack-picked')

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
