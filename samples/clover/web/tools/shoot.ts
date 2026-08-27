// 화면을 굽습니다.
//
// **되는지가 아니라 보이는지를 봅니다.** 셰이더는 컴파일이 통과해도 화면에서 틀릴 수
// 있고, 배치는 타입이 잡아 주지 않습니다. 빌드한 것을 브라우저에서 열어 그림으로 뽑고,
// 사람이 그것을 봅니다.
//
//     npx tsx tools/shoot.ts
//
// 콘솔 오류가 하나라도 있으면 실패로 끝냅니다 — 셰이더 컴파일 오류가 거기 나옵니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'shot')

/** 화면이 밖에 내어 둔 것. `game.ts` 의 `__clover` 입니다. */
interface Peek {
  phase: string
  ante: number
  money: number
  score: number
  target: number
  busy: boolean
  discards: number
  hand: { rank: number; suit: number }[]
}

async function peek(page: Page): Promise<Peek> {
  return page.evaluate(() => (window as unknown as { __clover: Peek }).__clover)
}

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })

  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5177 } })
  await server.listen()

  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } })

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', error => problems.push(String(error)))

  await page.goto('http://localhost:5177/?seed=CLOVER-SHOT', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const shoot = async (name: string) => {
    await page.screenshot({ path: path.join(OUT, `${name}.png`) })
    console.log(`${name}.png`)
  }

  await shoot('1-blind-select')

  await clickPrimary(page)
  await page.waitForTimeout(700)
  await shoot('2-round')

  // 연출이 도는 중을 찍습니다 — 끝난 뒤에는 낸 카드가 이미 치워져 있어서, 득점이 어떻게
  // 보이는지가 그림에 남지 않습니다.
  await playHand(page, chooseFive((await peek(page)).hand))
  await page.waitForTimeout(280)
  await shoot('3-scoring')

  await page.waitForTimeout(2000)
  await shoot('4-scored')

  // 상점까지 갑니다. **연출이 끝나기를 기다립니다** — 도는 중에 누르면 화면이 받지
  // 않습니다.
  for (let turn = 0; turn < 60; turn++) {
    await settle(page)
    const state = await peek(page)
    if (state.phase === 'shop' || state.phase === 'lost' || state.phase === 'won') break

    if (state.phase === 'blind-select') {
      await clickPrimary(page)
    } else if (state.phase === 'round') {
      const picks = chooseFive(state.hand)
      // 패가 시원찮고 버릴 여유가 있으면 버립니다. 그러지 않으면 안테 1 을 넘지 못합니다.
      if (rate(picks.map(index => state.hand[index])) < 60 && state.discards > 0) {
        await discardHand(page, spare(state.hand, picks))
      } else {
        await playHand(page, picks)
      }
    }
    await page.waitForTimeout(250)
  }

  const finished = await peek(page)
  if (finished.phase === 'shop') await shoot('5-shop')
  else console.log(`상점까지 가지 못했습니다 — 지금은 ${finished.phase} 입니다`)

  await browser.close()
  await server.close()

  if (problems.length > 0) {
    console.error('브라우저가 오류를 냈습니다:')
    for (const problem of problems.slice(0, 10)) console.error('  ' + problem)
    return 1
  }

  console.log('오류 없음')
  return 0
}

/** 연출이 끝날 때까지 기다립니다. */
async function settle(page: Page): Promise<void> {
  for (let wait = 0; wait < 60; wait++) {
    const state = await peek(page)
    if (!state.busy) return
    await page.waitForTimeout(200)
  }
}

/** 화면 좌표를 캔버스 위의 자리로. 기준 해상도는 1280 × 720 입니다. */
async function at(page: Page, x: number, y: number): Promise<{ x: number; y: number }> {
  const box = await (await page.$('#stage'))?.boundingBox()
  if (!box) return { x, y }
  return { x: box.x + (x / 1280) * box.width, y: box.y + (y / 720) * box.height }
}

/** 가운데 큰 버튼. 블라인드 선택과 상점이 씁니다. */
async function clickPrimary(page: Page): Promise<void> {
  const spot = await at(page, 640, 642)
  await page.mouse.click(spot.x, spot.y)
}

/**
 * 패에서 다섯 장을 골라 냅니다.
 *
 * **화면을 실제로 누르는 것이 요점입니다** — 코어를 직접 부르면 화면이 도는지를 확인하지
 * 못합니다. 카드는 108픽셀 간격으로 가운데 놓이고, 8장일 때 첫 장의 중심이 262 입니다.
 */
async function playHand(page: Page, picks: number[] = [0, 1, 2, 3, 4]): Promise<void> {
  for (const i of picks) {
    const spot = await at(page, 262 + i * 108, 560)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(70)
  }

  const play = await at(page, 820, 680)
  await page.mouse.click(play.x, play.y)
}

/** 고른 것을 버립니다. */
async function discardHand(page: Page, picks: number[]): Promise<void> {
  for (const i of picks) {
    const spot = await at(page, 262 + i * 108, 560)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(70)
  }

  const discard = await at(page, 960, 680)
  await page.mouse.click(discard.x, discard.y)
}

/** 쓸 만한 다섯 장에 들지 못한 카드들. 버릴 대상입니다. */
function spare(hand: { rank: number }[], picks: number[]): number[] {
  const keep = new Set(picks)
  return hand.map((_, index) => index).filter(index => !keep.has(index)).slice(0, 5)
}

/**
 * 패에서 쓸 만한 다섯 장.
 *
 * 다섯 장 조합 56가지를 전부 보고 가장 높은 족보를 고릅니다. **잘 두려는 것이 아니라
 * 상점까지 가려는 것입니다** — 무작정 왼쪽 다섯 장을 내면 안테 1 에서 끝납니다.
 *
 * 족보의 값은 여기 손으로 적혀 있습니다. 도구이므로 그래도 되고, 게임의 값은 시트에
 * 있습니다.
 */
function chooseFive(hand: { rank: number; suit: number }[]): number[] {
  let best: number[] = [0, 1, 2, 3, 4].filter(i => i < hand.length)
  let bestScore = -1

  const indices = hand.map((_, index) => index)
  for (const combo of fiveOf(indices)) {
    const value = rate(combo.map(index => hand[index]))
    if (value > bestScore) {
      bestScore = value
      best = combo
    }
  }
  return best.sort((a, b) => a - b)
}

function* fiveOf(indices: number[]): Generator<number[]> {
  const want = Math.min(5, indices.length)
  const combo: number[] = []
  const walk = (start: number): Generator<number[]> | void => undefined
  void walk

  const stack: number[][] = [[]]
  while (stack.length > 0) {
    const current = stack.pop() as number[]
    if (current.length === want) { yield current; continue }
    const from = current.length === 0 ? 0 : current[current.length - 1] + 1
    for (let i = indices.length - 1; i >= from; i--) stack.push([...current, indices[i]])
  }
  void combo
}

/** 족보의 대략적인 값. 순서만 맞으면 됩니다. */
function rate(cards: { rank: number; suit: number }[]): number {
  const ranks = new Map<number, number>()
  const suits = new Map<number, number>()
  for (const card of cards) {
    ranks.set(card.rank, (ranks.get(card.rank) ?? 0) + 1)
    suits.set(card.suit, (suits.get(card.suit) ?? 0) + 1)
  }

  const counts = [...ranks.values()].sort((a, b) => b - a)
  const flush = [...suits.values()].some(count => count >= 5)
  const sorted = [...ranks.keys()].sort((a, b) => a - b)
  const straight = sorted.length >= 5
    && sorted[sorted.length - 1] - sorted[0] === sorted.length - 1

  const high = Math.max(...cards.map(card => card.rank)) / 100
  if (counts[0] >= 5) return 400 + high
  if (flush && counts[0] >= 3 && counts[1] >= 2) return 380 + high
  if (flush && straight) return 300 + high
  if (counts[0] >= 4) return 200 + high
  if (counts[0] >= 3 && counts[1] >= 2) return 160 + high
  if (flush) return 140 + high
  if (straight) return 120 + high
  if (counts[0] >= 3) return 90 + high
  if (counts[0] >= 2 && counts[1] >= 2) return 60 + high
  if (counts[0] >= 2) return 30 + high
  return high
}

main().then(code => { process.exitCode = code })
