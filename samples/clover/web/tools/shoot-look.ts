// 새 판 셋 — 정산 · 상점(진열 중) · 게임오버 — 을 굽습니다.
//
// **되는지가 아니라 보이는지를 봅니다.** 판의 배치는 타입이 잡아 주지 않습니다.
//
//     npx tsx tools/shoot-look.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, clearBlind, openRun, pass, peek, pickCards, pressPlay, settle, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5231

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `look-${name}.png`) })
  console.log(`look-${name}.png`)
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const problems: string[] = []

  // 정산과 상점.
  {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
    page.on('console', message => { if (message.type() === 'error') problems.push(message.text()) })
    page.on('pageerror', error => problems.push(String(error)))
    await skipLogin(page)
    await page.goto(`http://localhost:${PORT}/?seed=CLOVER-SHOT6&tick=manual`, { waitUntil: 'networkidle' })
    await pass(page, 1500)
    await openRun(page)
    await clearBlind(page)
    await pass(page, 1400)
    for (let wait = 0; wait < 60; wait++) {
      if ((await peek(page)).spots?.take) break
      await pass(page, 200)
    }
    await pass(page, 250)
    await shot(page, 'payout-1')
    await pass(page, 1400)
    await shot(page, 'payout-2')

    const take = (await peek(page)).spots?.take
    if (take) {
      const here = await at(page, take.x, take.y)
      await page.mouse.click(here.x, here.y)
    }
    // 판이 올라오고 진열되는 동안을 다섯 장으로. 연출의 시계를 함께 적습니다.
    for (const [i, wait] of [420, 350, 450, 600, 1400].entries()) {
      await pass(page, wait)
      const now = await peek(page)
      console.log('shop', i + 1, 'clock', now.clock, 'shopY', now.shopY, 'shopUp', now.shopUp)
      await shot(page, `shop-${i + 1}`)
    }
    await page.close()
  }

  // 게임오버. 한 장씩 내서 집니다.
  {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
    page.on('pageerror', error => problems.push(String(error)))
    await skipLogin(page)
    await page.goto(`http://localhost:${PORT}/?seed=CLOVER-LOSE1&tick=manual`, { waitUntil: 'networkidle' })
    await pass(page, 1500)
    await openRun(page)
    for (let turn = 0; turn < 12; turn++) {
      const state = await peek(page)
      if (state.phase !== 'round') break
      await pickCards(page, [0])
      await pressPlay(page)
      await settle(page)
      await pass(page, 200)
    }
    await pass(page, 2600)
    await shot(page, 'over')
    await page.close()
  }

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    console.error(problems.join('\n'))
    return 1
  }
  return 0
}

main().then(code => process.exit(code))
