// 마지막 핸드를 내는 순간을 프레임으로 봅니다.
//
// **격파가 마지막 핸드보다 먼저 처리되는지**를 찍어 확인합니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, grantJoker, peek, pickCards, pressPlay, settle, STAGE_W,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5216

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-LAST1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await tap(page, STAGE_W / 2, 473)
  await page.waitForTimeout(1000)
  await tap(page, 20, 20)
  await page.waitForTimeout(600)
  await clickPrimary(page)
  await settle(page)

  // 마지막 핸드 하나가 남을 때까지 버립니다. **버려도 핸드는 줄지 않습니다** — 그래서
  // 약한 패를 내서 핸드를 줄입니다.
  for (let turn = 0; turn < 12; turn++) {
    const state = await peek(page)
    if (state.phase !== 'round') break
    console.log(`핸드 ${state.hands} · 점수 ${state.score} / ${state.target}`)
    const left = await handsLeft(page)
    if (left <= 1) break
    // 한 장씩 내서 점수를 조금만 올립니다.
    await pickCards(page, [0])
    await pressPlay(page)
    await settle(page)
    await page.waitForTimeout(200)
  }

  const before = await peek(page)
  console.log('마지막 핸드 직전 · 점수', before.score, '/', before.target,
    '· 남은 핸드', await handsLeft(page))
  if (before.phase !== 'round') {
    console.log('라운드가 아닙니다 — 확인할 수 없습니다')
    await browser.close()
    await server.close()
    return 1
  }

  // **격파하게 만듭니다.** 조커 다섯을 얹어야 마지막 한 판으로 목표를 넘깁니다.
  await grantJoker(page, 5)
  await page.waitForTimeout(900)

  // 목표를 넘길 만한 다섯 장으로 냅니다.
  await pickCards(page, chooseFive(before.hand))
  await page.waitForTimeout(300)
  await pressPlay(page)

  for (let i = 0; i < 20; i++) {
    await page.waitForTimeout(260)
    const now = await peek(page)
    await shot(page, `last-${String(i).padStart(2, '0')}`)
    console.log(`${i * 260 + 260}ms · 국면 ${now.phase} · 점수 ${now.score}`
      + ` · 핸드 ${now.hands} · 목표 ${now.target}`
      + ` · 낸 카드 ${now.played} · 다음 박자 ${now.coming || '-'}`)
  }

  await browser.close()
  await server.close()
  return 0
}

/** 남은 핸드. */
async function handsLeft(page: Page): Promise<number> {
  return (await peek(page)).hands
}

async function tap(page: Page, x: number, y: number): Promise<void> {
  const spot = await at(page, x, y)
  await page.mouse.move(spot.x, spot.y)
  await page.waitForTimeout(80)
  await page.mouse.down()
  await page.waitForTimeout(55)
  await page.mouse.up()
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
