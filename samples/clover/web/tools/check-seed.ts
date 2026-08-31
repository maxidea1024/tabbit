// 타이틀에서 적은 시드가 정말 그 판을 만드는가.
//
// **같은 시드는 같은 패입니다** — 적어 넣은 것과 주소로 연 것의 첫 패를 견줍니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, clickPrimary, peek, settle, STAGE_W } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5203

/** 시드 줄. `ui/title.ts` 의 `SEED_Y` 와 같습니다. */
const SEED_Y = 634
const SEED_H = 36
const SEED = 'SEED-TEST-42'

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  const typed = await firstHand(browser, undefined, async page => {
    // 시드 칸을 누르고, 지운 뒤 새로 적습니다.
    const field = await at(page, STAGE_W / 2 - 100, SEED_Y + SEED_H / 2)
    await page.mouse.move(field.x, field.y)
    await page.waitForTimeout(120)
    await page.mouse.down()
    await page.waitForTimeout(60)
    await page.mouse.up()
    await page.waitForTimeout(300)
    await page.screenshot({ path: path.join(OUT, 'seed-edit.png') })

    for (let i = 0; i < 30; i++) await page.keyboard.press('Backspace')
    await page.keyboard.type(SEED)
    await page.waitForTimeout(250)
    await page.screenshot({ path: path.join(OUT, 'seed-typed.png') })
    await page.keyboard.press('Enter')
    await page.waitForTimeout(300)
  })

  const direct = await firstHand(browser, SEED)

  console.log('적어 넣은 것 ', typed.title, typed.hand)
  console.log('주소로 연 것 ', direct.title, direct.hand)
  const same = typed.hand === direct.hand && typed.hand !== ''

  await browser.close()
  await server.close()
  console.log(same ? '같은 판입니다' : '다른 판입니다 — 시드가 걸리지 않았습니다')
  return same ? 0 : 1
}

/** 그 시드로 판을 열고 첫 패를 적어 옵니다. */
async function firstHand(browser: Browser, seed?: string,
                         before?: (page: Page) => Promise<void>):
                         Promise<{ title: string; hand: string }> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  const query = seed ? `?seed=${seed}` : ''
  await page.goto(`http://localhost:${PORT}/${query}`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  if (before) await before(page)
  const title = await page.title()

  const start = await at(page, STAGE_W / 2, 446 + 27)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  // **다 깔릴 때까지 기다립니다.** `settle` 은 연출만 보므로 깔리는 중에 읽으면 한 장만
  // 읽힙니다 — 한 장이 같은 것은 같은 판의 증거가 되지 못합니다.
  let hand = ''
  for (let i = 0; i < 40; i++) {
    const now = (await peek(page)).hand
    if (now.length >= 8) {
      hand = now.map(card => `${card.rank}.${card.suit}`).join(' ')
      break
    }
    await page.waitForTimeout(200)
  }
  await page.close()
  return { title, hand }
}

main().then(code => process.exit(code))
