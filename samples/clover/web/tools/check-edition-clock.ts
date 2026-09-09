// 판(에디션)의 셰이더가 **판의 시계를 따라가는가.**
//
// **눈으로도 그림으로도 잡히지 않습니다.** 무늬가 멈춘 것과 흐르는 것은 컷 한 장에서 같아
// 보이고, 「누를 때마다 처음으로 돌아간다」는 컷 두 장을 나란히 놓아도 그 사이에 무엇이
// 있었는지가 없습니다. 셰이더가 보는 시각을 값으로 봅니다.
//
// 통 둘을 봅니다.
//
// |통|무엇|
// |--|--|
// |줄|`this.jokers`. `advance` 가 자리와 겉면을 함께 돌립니다|
// |줄 밖|상점의 칸 · 팩에 펼친 카드 · 진 판의 판. **`lookAt` 이 겉면만 돌립니다** — 이쪽이 틱을 못 받아 `uTime` 0 에 굳어 있었습니다|
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { openRun, pass, peek, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5298
/** 셰이더의 시각이 판의 시계에서 이만큼까지는 어긋나도 됩니다. 초입니다. */
const SLACK = 0.05

interface Spot { time: number; tilt: number }
interface Clocks { tray: Spot[]; look: Spot[] }

let failed = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '실패'}  ${what}`)
  if (!ok) failed++
}

async function sample(page: Page): Promise<{ clock: number; at: Clocks }> {
  const now = await peek(page)
  return { clock: now.clock, at: (now as never as { editionAt: Clocks }).editionAt }
}

/** 셰이더의 시각이 판의 시계만큼 나아갔는가. 빈 글이면 따라간 것입니다. */
function follows(before: { clock: number; at: Clocks }, after: { clock: number; at: Clocks },
                 which: 'tray' | 'look'): string {
  const gap = after.clock - before.clock
  const one = before.at[which]
  const two = after.at[which]
  if (two.length === 0) return '그 통에 판이 걸린 것이 없습니다'
  const worst = Math.max(...two.map((spot, i) => Math.abs(spot.time - (one[i]?.time ?? spot.time) - gap)))
  return worst <= SLACK ? ''
    : `시계는 ${gap.toFixed(2)}초 갔는데 셰이더는 ${worst.toFixed(2)}초 어긋납니다`
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-EDITION&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  // 판이 걸린 조커 셋. **맨 것에는 셰이더가 없습니다.**
  await page.evaluate(`window.__clover.grantJoker(3, 1)`)
  await pass(page, 1200)

  const one = await sample(page)
  await pass(page, 1000)
  const two = await sample(page)
  const trayGap = follows(one, two, 'tray')
  check(trayGap === '', `줄의 판이 시계를 따라갑니다${trayGap === '' ? '' : ` — ${trayGap}`}`)

  // **진 판의 판을 세웁니다.** 그 판의 조커가 줄 밖이고, 상점의 칸·팩의 카드와 같은 길입니다.
  await page.evaluate(`window.__clover.loseRound()`)
  for (let i = 0; i < 60; i++) {
    if ((await peek(page)).gameOver) break
    await pass(page, 300)
  }
  await pass(page, 1500)
  const three = await sample(page)
  await pass(page, 1000)
  const four = await sample(page)
  console.log(`  줄 ${four.at.tray.length}개 · 줄 밖 ${four.at.look.length}개`)
  const lookGap = follows(three, four, 'look')
  check(lookGap === '', `줄 밖의 판이 시계를 따라갑니다${lookGap === '' ? '' : ` — ${lookGap}`}`)
  check(four.at.look.every(spot => spot.time > 0), '줄 밖의 시각이 0 에 굳어 있지 않습니다')

  // **다시 그려도 시각이 뒤로 가지 않습니다.** 셰이더는 판의 시계를 그대로 받으므로 통을
  // 새로 만들어도 무늬가 처음으로 돌아가지 않아야 합니다.
  await page.evaluate(`window.__clover.grantMoney(1)`)
  await pass(page, 200)
  const five = await sample(page)
  check(five.at.tray.every((spot, i) => spot.time >= (four.at.tray[i]?.time ?? 0) - 0.001),
    '다시 그려도 줄의 시각이 뒤로 가지 않습니다')
  console.log(`  기울기 줄 ${four.at.tray.map(one => one.tilt.toFixed(2)).join(' ')}`
    + ` · 줄 밖 ${four.at.look.map(one => one.tilt.toFixed(2)).join(' ')}`)

  await browser.close()
  await server.close()
  return failed === 0 ? 0 : 1
}

main().then(code => process.exit(code))
