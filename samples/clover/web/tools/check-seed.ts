// 타이틀에서 적은 시드가 정말 그 판을 만드는가.
//
// **같은 시드는 같은 패입니다** — 적어 넣은 것과 주소로 연 것의 첫 패를 견줍니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickPrimary, peek, settle, STAGE_W, TITLE_OPTIONS, TITLE_START_Y,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5203

/** 타이틀의 「옵션」 버튼과, 그 판의 「시드」 탭과 칸. */
// 타이틀의 옵션은 오른쪽 아래의 톱니 아이콘입니다.
const TAB_Y = 279
/**
 * 시드 탭의 가운데.
 *
 * **맨 오른쪽입니다** — 판을 정하는 것이라 판 안에서 고치는 것들 뒤에 섭니다. `options.ts` 의
 * `buildTabs` 와 같은 셈입니다: 판이 화면 가운데에 서고, 탭 줄이 판의 안쪽 폭을 고르게
 * 나눕니다.
 */
const PANEL_W = 520
const TABS = 5
const TAB_X = STAGE_W / 2 - PANEL_W / 2 + 24
  + ((PANEL_W - 48) / TABS) * (TABS - 0.5)
const FIELD_X = 560
const FIELD_Y = 390
const SEED = 'SEED-TEST-42'

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  const typed = await firstHand(browser, undefined, async page => {
    // 옵션 → 시드 탭 → 칸을 누르고 새로 적습니다.
    await tap(page, TITLE_OPTIONS.x, TITLE_OPTIONS.y)
    await page.waitForTimeout(700)
    await tap(page, TAB_X, TAB_Y)
    await page.waitForTimeout(400)
    await tap(page, FIELD_X, FIELD_Y)
    await page.waitForTimeout(300)
    await page.screenshot({ path: path.join(OUT, 'seed-edit.png') })

    for (let i = 0; i < 30; i++) await page.keyboard.press('Backspace')
    await page.keyboard.type(SEED)
    await page.waitForTimeout(250)
    await page.screenshot({ path: path.join(OUT, 'seed-typed.png') })
    await page.keyboard.press('Enter')
    await page.waitForTimeout(300)
    // 판을 닫습니다. 바깥을 누르면 닫힙니다.
    await tap(page, 60, 60)
    await page.waitForTimeout(500)
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

  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
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

/** 한 번 누릅니다. `mouse.click` 은 너무 빨라 `pointertap` 이 서지 않는 자리가 있습니다. */
async function tap(page: Page, x: number, y: number): Promise<void> {
  const spot = await at(page, x, y)
  await page.mouse.move(spot.x, spot.y)
  await page.waitForTimeout(90)
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
}

main().then(code => process.exit(code))
