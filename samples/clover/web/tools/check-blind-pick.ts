// 블라인드 판이 국면이 바뀐 뒤 연출이 끝나는 대로 스스로 서는가.
//
// **판을 세우는 것은 `tick` 이고, `refresh` 가 어느 순간에 불렸는가에 맡기지 않습니다.**
// 전에는 프레임마다 「바쁨→안 바쁨」 전환을 표본으로 잡아 그때 한 번 다시 그렸고, 한 프레임
// 안에서 시작해 끝나는 연출은 표본에 잡히지 않았습니다 — 상점을 나설 때 발동하는 조커의
// 박자 하나가 그랬고, 그때 판은 조커나 소모품을 눌러 `refresh` 가 불릴 때까지 서지
// 않았습니다.
//
// 넷을 확인합니다. 모두 **아무것도 누르지 않고** 판이 떠야 합니다.
// 1. 조커 없이 상점을 나섬 — 누른 그 자리에서 서는가.
// 2. 건너뛰기 — 태그 칩이 날아가 앉고 발동한 뒤에 다음 블라인드의 판이 서는가.
// 3. 박자 하나짜리 조커(`spent_note`, OnShopExit · GrowSelf)를 들고 나섬.
// 4. 박자 둘과 동전을 내는 조커(`paper_bag`, OnShopExit · 돈 3)를 들고 나섬 — 동전이
//    닿은 뒤에 서는가.
//
//     npx tsx tools/check-blind-pick.ts
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickSpot, grantJoker, openRun, pass, peek, settle, shopStanding, skipLogin, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5297

let failed = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '실패'}  ${what}`)
  if (!ok) failed++
}

/**
 * 블라인드 판이 뜨기까지 걸린 시간(밀리초). 한도 안에 뜨지 않으면 -1 입니다.
 *
 * `blindBoard` 는 `블라인드:칸수` 이고 블라인드는 스몰 1 · 빅 2 · 보스 3 입니다. 건너뛰는
 * 동안은 지난 판이 그대로 떠 있으므로, `was` 를 주면 그것과 다른 판이 뜰 때까지 기다립니다.
 */
async function untilBoard(page: Page, limitMs: number, was = 'hidden'):
    Promise<{ ms: number; board: string }> {
  const start = (await peek(page)).clock
  for (;;) {
    const now = await peek(page)
    const board = (now as unknown as { blindBoard: string }).blindBoard
    const ms = Math.round((now.clock - start) * 1000)
    if (board !== 'hidden' && board !== was) return { ms, board }
    if (ms >= limitMs) return { ms: -1, board }
    await pass(page, 40)
  }
}

/**
 * 상점의 「다음 블라인드로」 를 누릅니다.
 *
 * **진열이 끝나기 전에는 단추가 잠겨 있습니다.** 눌러도 국면이 그대로면 잠긴 것이므로
 * 조금 기다려 다시 누릅니다 — 진열의 길이는 칸 수를 따르므로 한 번의 기다림으로 맞출 수
 * 없습니다.
 */
async function leaveShop(page: Page): Promise<void> {
  // **정산 판이 남아 있으면 다시 받습니다.** 「받는다」 는 줄이 다 선 뒤에야 열리고 줄 수는
  // 라운드마다 다르므로, 한 번의 기다림 뒤에 누른 것이 잠긴 단추에 닿을 수 있습니다.
  for (let wait = 0; wait < 40 && !(await peek(page)).shopUp; wait++) {
    const take = (await peek(page)).spots?.take
    if (take) {
      const here = await at(page, take.x, take.y)
      await page.mouse.click(here.x, here.y)
    }
    await pass(page, 300)
  }
  await shopStanding(page)
  for (let tries = 0; tries < 30; tries++) {
    await pass(page, 300)
    // **자리가 없으면 그냥 다시 돕니다.** 던지면 무엇이 어떤 상태였는지가 남지 않고,
    // 판이 아직 올라오는 중인 것과 영영 서지 않는 것이 같은 오류가 됩니다.
    const here = (await peek(page)).spots?.nextBlind
    if (!here) continue
    const at2 = await at(page, here.x, here.y)
    await page.mouse.click(at2.x, at2.y)
    if ((await peek(page)).phase !== 'shop') return
  }
  throw new Error(`상점을 나서지 못했습니다 — ${await where(page)}`)
}

/** 실패했을 때 무엇이 어떤 상태였는지. */
/**
 * 블라인드를 고릅니다. **국면이 바뀔 때까지 다시 누릅니다.**
 *
 * `act` 는 연출이 도는 동안의 누름을 버리고 그 버림은 화면 어디에도 적히지 않습니다 —
 * 도구가 그것을 「골랐다」로 보고 다음 줄로 넘어가면, 라운드에 들지 못한 채로 이기려
 * 하다가 상점을 기다리며 멈춥니다. `leaveShop` 과 같은 자리이고 같은 까닭입니다.
 */
async function pickBlind(page: Page): Promise<void> {
  for (let tries = 0; tries < 12; tries++) {
    if ((await peek(page)).phase === 'round') return
    await settle(page)
    await clickSpot(page, 'pick')
    await pass(page, 400)
  }
  throw new Error(`블라인드를 고르지 못했습니다 — ${await where(page)}`)
}

async function where(page: Page): Promise<string> {
  const now = await peek(page) as unknown as Record<string, unknown>
  return `phase=${now.phase} shown=${now.shownPhase} busy=${now.busy} coins=${now.coins}`
    + ` shopUp=${now.shopUp} jokers=${now.jokers} blind=${now.blindBoard} bins=${JSON.stringify(now.bins)}`
}

async function main(): Promise<number> {
  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: PORT, hmr: false },
  })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BLIND1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await pass(page, 900)

  // 1. 조커 없이. 누른 그 자리에서 뜹니다. **`openRun` 이 스몰을 이미 골라 두었습니다.**
  await winRound(page)
  await leaveShop(page)
  const plain = await untilBoard(page, 2000)
  check(plain.ms >= 0 && plain.ms <= 200 && plain.board.startsWith('2:'),
    `조커 없이 나서면 그 자리에서 빅의 판이 섭니다 (${plain.ms}ms, ${plain.board})`)

  // 2. 건너뛰기. 빅을 건너뛰면 보스의 판이 서야 합니다.
  await pass(page, 1200)
  await clickSpot(page, 'skip')
  const skipped = await untilBoard(page, 6000, plain.board)
  check(skipped.ms >= 0 && skipped.board.startsWith('3:'),
    `건너뛴 뒤 보스의 판이 스스로 섭니다 (${skipped.ms}ms, ${skipped.board})`)
  // 태그 칩이 날아가 앉는 동안은 판이 뜨지 않아야 합니다.
  check(skipped.ms >= 400, `건너뛰기 연출이 끝난 뒤에 섭니다 (${skipped.ms}ms)`)

  // 3. 박자 하나짜리 조커. 전에는 여기서 판이 뜨지 않았습니다.
  await pickBlind(page)
  await pass(page, 1500)
  await winRound(page)
  await grantJoker(page, 'spent_note')
  await pass(page, 200)
  await leaveShop(page)
  const one = await untilBoard(page, 3000)
  check(one.ms >= 0 && one.board.startsWith('1:'),
    `박자 하나짜리 조커를 들고 나서도 판이 스스로 섭니다 (${one.ms}ms, ${one.board})`)
  if (one.ms < 0) console.log('    ', await where(page))

  // 4. 박자 둘과 동전. 동전이 닿은 뒤에 서야 합니다.
  await pickBlind(page)
  await pass(page, 1500)
  await winRound(page)
  await grantJoker(page, 'paper_bag')
  await pass(page, 200)
  await leaveShop(page)
  const coins = await untilBoard(page, 4000)
  check(coins.ms >= 0 && coins.board.startsWith('2:'),
    `동전을 내는 조커를 들고 나서면 동전이 닿은 뒤 판이 섭니다 (${coins.ms}ms, ${coins.board})`)
  if (coins.ms < 0) console.log('    ', await where(page))
  check(coins.ms >= 300, `동전이 나는 동안은 서지 않습니다 (${coins.ms}ms)`)

  await browser.close()
  await server.close()
  return failed
}

main().then(bad => {
  console.log(bad === 0 ? '모두 통과' : `실패 ${bad}건`)
  process.exit(bad === 0 ? 0 : 1)
}).catch(error => { console.error(error); process.exit(1) })
