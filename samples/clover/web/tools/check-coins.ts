// 동전이 닿을 때 잔액이 바뀌는가 — 정산과 상점 구매.
//
// **잔액은 동전이 뜨는 순간이 아니라 닿는 순간에 바뀌어야 합니다.** 정산은 「받는다」 를 누른
// 뒤에, 구매는 산 자리에서 동전이 날아가 닿는 만큼 줄어듭니다. 40ms 마다 화면의 잔액과 코어의
// 잔액과 동전이 나는 중인지를 적고, 처음 바뀐 프레임이 동전이 뜬 뒤인지와 다 닿은 뒤 코어와
// 같은지를 봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clearBlind, grantMoney, openRun, pass, peek, settle, shopBuySpot, shopSlot, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5237

interface Sample { t: number; money: number; shown: number; coins: boolean }

async function sample(page: Page): Promise<Sample> {
  const now = await peek(page) as unknown as { clock: number; money: number; shownMoney: number; coins: boolean }
  return { t: Math.round(now.clock * 1000), money: now.money, shown: now.shownMoney, coins: now.coins }
}

async function track(page: Page, frames: number, step = 40): Promise<Sample[]> {
  const out: Sample[] = []
  for (let i = 0; i < frames; i++) {
    out.push(await sample(page))
    await pass(page, step)
  }
  return out
}

let failed = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '실패'}  ${what}`)
  if (!ok) failed++
}

function line(run: Sample[]): string {
  return run.map(one => `${one.shown}${one.coins ? '*' : ''}`).join(' ')
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [오류]', error.stack ?? error.message))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-COIN1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await pass(page, 900)

  console.log('정산')
  const before = await sample(page)
  await clearBlind(page)
  let take: { x: number; y: number } | undefined
  for (let wait = 0; wait < 80 && !take; wait++) {
    take = (await peek(page)).spots?.take
    if (!take) await pass(page, 100)
  }
  check(take !== undefined, '정산 판이 섰습니다')
  await pass(page, 1200)
  const pre = await sample(page)
  check(pre.money > before.money, `코어의 잔액은 올랐습니다 (${before.money} → ${pre.money})`)
  check(pre.shown === before.money, `판이 서 있는 동안 화면의 잔액은 그대로입니다 (${pre.shown})`)
  if (take) {
    const here = await at(page, take.x, take.y)
    await page.mouse.click(here.x, here.y)
  }
  const payout = await track(page, 60)
  console.log('   ' + line(payout))
  check(payout[0].shown === before.money, '누른 프레임에는 아직 오르지 않았습니다')
  check(payout.some(one => one.coins), '동전이 날았습니다')
  const firstRise = payout.findIndex(one => one.shown !== before.money)
  check(firstRise > 0 && payout[firstRise - 1].coins, `처음 오른 것은 동전이 뜬 뒤입니다 (${firstRise}번 프레임)`)
  check(payout.every((one, i) => i === 0 || one.shown >= payout[i - 1].shown), '오르기만 합니다')
  const last = payout[payout.length - 1]
  check(last.shown === last.money && !last.coins, `다 닿은 뒤 코어와 같습니다 (${last.shown} / ${last.money})`)

  console.log('상점 구매')
  for (let wait = 0; wait < 60; wait++) {
    const now = await peek(page)
    if (now.shopUp && (now.shopY ?? 1) === 0) break
    await pass(page, 100)
  }
  await grantMoney(page, 120)
  await settle(page)
  await pass(page, 600)
  const s0 = await sample(page)
  const slots = ((await peek(page)).shopAt ?? []).map(entry => entry[0])
  check(slots.length > 0, `상점 칸 ${slots.length}개`)
  const spot = await shopSlot(page, slots[0])
  await page.mouse.click(spot.x, spot.y)
  await pass(page, 350)
  const buy = await shopBuySpot(page)
  await page.mouse.click(buy.x, buy.y)
  const bought = await track(page, 60)
  console.log('   ' + line(bought))
  check(bought[0].money < s0.money, `코어의 잔액은 누른 자리에서 줄었습니다 (${s0.money} → ${bought[0].money})`)
  check(bought[0].shown === s0.money, '화면의 잔액은 누른 프레임에 그대로입니다')
  check(bought.some(one => one.coins), '동전이 날았습니다')
  const firstDrop = bought.findIndex(one => one.shown !== s0.money)
  check(firstDrop > 0 && bought[firstDrop - 1].coins, `처음 줄어든 것은 동전이 뜬 뒤입니다 (${firstDrop}번 프레임)`)
  check(bought.every((one, i) => i === 0 || one.shown <= bought[i - 1].shown), '줄기만 합니다')
  const end = bought[bought.length - 1]
  check(end.shown === end.money && !end.coins, `다 닿은 뒤 코어와 같습니다 (${end.shown} / ${end.money})`)

  await browser.close()
  await server.close()
  return failed
}

main().then(code => process.exit(code > 0 ? 1 : 0), error => { console.error(error); process.exit(1) })
