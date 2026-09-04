// 새 문법의 화면들을 굽습니다.
//
// **되는지가 아니라 보이는지를 봅니다.** 판의 배치는 타입이 잡아 주지 않습니다.
//
//     npx tsx tools/shoot-look.ts
//
// 콘솔 오류가 하나라도 있으면 실패로 끝냅니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, buyAffordablePack, clearBlind, clickSpot, closeGuide, grantMoney, openDeckView,
  pass, peek, pickCards, pressPlay, pressTitle, settle, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5231

const problems: string[] = []

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `look-${name}.png`) })
  console.log(`look-${name}.png`)
}

async function open(browser: Browser, query: string, login = false): Promise<Page> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('console', message => { if (message.type() === 'error') problems.push(message.text()) })
  page.on('pageerror', error => problems.push(String(error)))
  if (!login) await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/${query}`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  return page
}

/** 로그인 · 타이틀 · 덱과 스테이크 · 옵션 · 조커 도감. */
async function shootFront(browser: Browser): Promise<void> {
  // **로그인은 건너뛰지 않습니다.** 그 화면을 보려는 것이므로 여기서만 그대로 둡니다.
  const login = await open(browser, '?tick=manual', true)
  await shot(login, 'login')
  await login.close()

  const page = await open(browser, '?seed=CLOVER-SHOT6&tick=manual')
  await shot(page, 'title')
  await clickSpot(page, 'title:setup')
  await pass(page, 700)
  await shot(page, 'setup')
  await page.keyboard.press('Escape')
  await pass(page, 500)
  await clickSpot(page, 'title:options')
  await pass(page, 700)
  await shot(page, 'options')
  await page.keyboard.press('Escape')
  await pass(page, 500)
  await clickSpot(page, 'title:jokers')
  await pass(page, 900)
  await shot(page, 'jokers')
  await page.keyboard.press('Escape')
  await pass(page, 500)
  await clickSpot(page, 'title:leaderboard')
  await pass(page, 1200)
  await shot(page, 'leaderboard')
  await page.keyboard.press('Escape')
  await pass(page, 500)
  await clickSpot(page, 'title:challenges')
  await pass(page, 800)
  await shot(page, 'challenges')
  await page.keyboard.press('Escape')
  await pass(page, 500)
  await clickSpot(page, 'title:guide')
  await pass(page, 800)
  await shot(page, 'guide')
  await page.close()
}

/** 블라인드 고르기 · 라운드 · 정산 · 상점 진열. */
async function shootRun(browser: Browser): Promise<void> {
  const page = await open(browser, '?seed=CLOVER-SHOT6&tick=manual')
  await pressTitle(page, 'start')
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 600)
  await shot(page, 'blind')

  await clickSpot(page, 'pick')
  await settle(page)
  await pass(page, 400)
  await shot(page, 'round')

  await clearBlind(page)
  await pass(page, 1400)
  // 줄이 쌓이는 중. **합계는 아직 $ 낱개입니다** — 「받는다」 는 그 뒤에 열립니다.
  await pass(page, 900)
  await shot(page, 'payout-1')
  for (let wait = 0; wait < 60; wait++) {
    if ((await peek(page)).spots?.take) break
    await pass(page, 200)
  }
  await pass(page, 250)
  await shot(page, 'payout-2')

  const take = (await peek(page)).spots?.take
  if (take) {
    const here = await at(page, take.x, take.y)
    await page.mouse.click(here.x, here.y)
  }
  // 판이 올라오고 진열되는 동안을 다섯 장으로.
  for (const [i, wait] of [420, 350, 450, 600, 3200].entries()) {
    await pass(page, wait)
    await shot(page, `shop-${i + 1}`)
  }

  // 런 정보 판.
  await openDeckView(page)
  await pass(page, 700)
  await shot(page, 'runinfo')
  await page.keyboard.press('Escape')
  await pass(page, 500)

  // 팩을 뜯는 판. 돈을 넉넉히 놓고 첫 팩을 뜯습니다.
  await grantMoney(page, 30)
  await pass(page, 700)
  await buyAffordablePack(page)
  await pass(page, 900)
  await shot(page, 'pack')
  await page.close()
}

/** 게임오버. 한 장씩 내서 집니다. */
async function shootOver(browser: Browser): Promise<void> {
  const page = await open(browser, '?seed=CLOVER-LOSE1&tick=manual')
  await pressTitle(page, 'start')
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 400)
  await clickSpot(page, 'pick')
  await settle(page)
  for (let turn = 0; turn < 12; turn++) {
    if ((await peek(page)).phase !== 'round') break
    await pickCards(page, [0])
    await pressPlay(page)
    await settle(page)
    await pass(page, 200)
  }
  await pass(page, 2600)
  await shot(page, 'over')
  await page.close()
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  await shootFront(browser)
  await shootRun(browser)
  await shootOver(browser)

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    console.error(problems.join('\n'))
    return 1
  }
  return 0
}

main().then(code => process.exit(code))
