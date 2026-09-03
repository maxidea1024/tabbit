// 타이틀에서 적은 시드가 정말 그 판을 만드는가.
//
// **같은 시드는 같은 패입니다** — 적어 넣은 것과 주소로 연 것의 첫 패를 견줍니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, openRun, pass, peek, skipLogin, TITLE_OPTIONS } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5203

/**
 * 시드 탭과 적는 칸.
 *
 * **좌표를 적어 두지 않습니다.** 탭 줄은 판의 안쪽 폭을 탭 수로 고르게 나누고 판의 높이는
 * 말에 따라 달라지므로, 여기 적어 둔 값은 탭이 하나 늘거나 글이 길어지는 날에 어긋납니다 —
 * 그런데 이 도구는 어긋난 자리를 눌러 놓고도 「시드가 걸리지 않았습니다」 라고만 하므로,
 * 무엇이 잘못된 것인지가 남지 않습니다. 실제로 **탭 대신 「日本語」 를 누르고 있었습니다.**
 *
 * 그래서 화면이 알리는 자리를 짚습니다. `options.ts` 의 `toolSpots` 가 그리는 그 자리를
 * 그대로 알립니다.
 */
async function optionSpot(page: Page, name: string): Promise<{ x: number; y: number }> {
  for (let wait = 0; wait < 20; wait++) {
    const spot = (await peek(page)).spots?.[`option:${name}`]
    if (spot) return spot
    await pass(page, 100)
  }
  throw new Error(`옵션 판에 ${name} 이 없습니다`)
}

const SEED = 'SEED-TEST-42'

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  const typed = await firstHand(browser, undefined, async page => {
    // 옵션 → 시드 탭 → 칸을 누르고 새로 적습니다.
    await tap(page, TITLE_OPTIONS.x, TITLE_OPTIONS.y)
    await pass(page, 700)
    const tab = await optionSpot(page, 'tab:seed')
    await tap(page, tab.x, tab.y)
    await pass(page, 400)
    const field = await optionSpot(page, 'field:seed')
    await tap(page, field.x, field.y)
    await pass(page, 300)
    await page.screenshot({ path: path.join(OUT, 'seed-edit.png') })

    for (let i = 0; i < 30; i++) await page.keyboard.press('Backspace')
    await page.keyboard.type(SEED)
    await pass(page, 250)
    await page.screenshot({ path: path.join(OUT, 'seed-typed.png') })
    await page.keyboard.press('Enter')
    await pass(page, 300)
    // 판을 닫습니다. 바깥을 누르면 닫힙니다.
    await tap(page, 60, 60)
    await pass(page, 500)
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
  await skipLogin(page)
  const query = seed ? `?seed=${seed}&tick=manual` : '?tick=manual'
  await page.goto(`http://localhost:${PORT}/${query}`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  if (before) await before(page)
  const title = await page.title()

  await openRun(page)

  // **다 깔릴 때까지 기다립니다.** `settle` 은 연출만 보므로 깔리는 중에 읽으면 한 장만
  // 읽힙니다 — 한 장이 같은 것은 같은 판의 증거가 되지 못합니다.
  let hand = ''
  for (let i = 0; i < 40; i++) {
    const now = (await peek(page)).hand
    if (now.length >= 8) {
      hand = now.map(card => `${card.rank}.${card.suit}`).join(' ')
      break
    }
    await pass(page, 200)
  }
  await page.close()
  return { title, hand }
}

/** 한 번 누릅니다. `mouse.click` 은 너무 빨라 `pointertap` 이 서지 않는 자리가 있습니다. */
async function tap(page: Page, x: number, y: number): Promise<void> {
  const spot = await at(page, x, y)
  await page.mouse.move(spot.x, spot.y)
  await pass(page, 90)
  await page.mouse.down()
  await pass(page, 60)
  await page.mouse.up()
}

main().then(code => process.exit(code))
