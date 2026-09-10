// 판(에디션)의 셰이더가 **판의 시계를 따라가는가.**
//
// **눈으로도 그림으로도 잡히지 않습니다.** 무늬가 멈춘 것과 흐르는 것은 컷 한 장에서 같아
// 보이고, 「누를 때마다 처음으로 돌아간다」는 컷 두 장을 나란히 놓아도 그 사이에 무엇이
// 있었는지가 없습니다. 셰이더가 보는 시각을 값으로 봅니다.
//
// 통 둘을 확인합니다.
//
// |통|무엇|
// |--|--|
// |줄|`this.jokers`. `advance` 가 자리와 겉면을 함께 돌립니다|
// |줄 밖|상점의 칸 · 팩에 펼친 카드 · 진 판의 판. **`lookAt` 이 겉면만 돌립니다** — 이쪽이 틱을 못 받아 `uTime` 0 에 굳어 있었습니다|
//
// **기울기도 같은 자리에서 봅니다.** 값을 잘라 두기만 했더니 한 장 너비를 넘어선 것이 전부
// ±1 이었고, 커서가 줄을 지나가면 그 값이 한꺼번에 뒤집혀 무늬의 위상이 1.2 라디안 뛰었습니다 —
// 마우스를 움직이면 무늬가 밀리는 것으로 보였습니다. 멀면 0 이고, 옮기는 동안 한 프레임에
// 뛰지 않아야 합니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, openRun, pass, peek, skipLogin } from './harness'

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
  // 판이 걸린 소모품 둘. **줄 밖과 같은 길로 시각을 받습니다** — 얼굴은 `refresh` 마다
  // 새로 만들어지므로 셰이더도 새것이고, 넣어 줄 자리가 없으면 0 에 굳습니다.
  await page.evaluate(`window.__clover.grantConsumable(2, 1)`)
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
  check(four.at.look.length >= 2, `줄 밖에 판이 걸린 것이 섰습니다 (${four.at.look.length}개)`)
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

  // **커서가 멀면 기울기가 0 입니다.** 줄에 선 것이 전부 최대로 기운 채이면, 커서가
  // 그 줄을 지나갈 때 그 값이 한꺼번에 뒤집힙니다.
  const corner = await at(page, 20, 780)
  await page.mouse.move(corner.x, corner.y)
  await pass(page, 600)
  const far = await sample(page)
  const worstFar = Math.max(...far.at.tray.map(one => Math.abs(one.tilt)), 0)
  console.log(`  커서가 먼 자리 · 기울기 ${far.at.tray.map(one => one.tilt.toFixed(2)).join(' ')}`)
  check(worstFar < 0.1, `커서가 멀면 기울기가 0 입니다 (가장 큰 것 ${worstFar.toFixed(2)})`)

  // **옮기는 동안 한 프레임에 뛰지 않습니다.** 줄을 왼쪽에서 오른쪽으로 지나갑니다.
  let jump = 0
  let last = far.at.tray.map(one => one.tilt)
  for (let step = 0; step <= 12; step++) {
    const here = await at(page, 200 + step * 70, 90)
    await page.mouse.move(here.x, here.y)
    await pass(page, 34)
    const now = (await sample(page)).at.tray.map(one => one.tilt)
    for (let i = 0; i < now.length; i++) {
      jump = Math.max(jump, Math.abs(now[i] - (last[i] ?? now[i])))
    }
    last = now
  }
  console.log(`  줄을 지나가는 동안 한 프레임의 가장 큰 변화 ${jump.toFixed(2)}`)
  check(jump < 0.35, `기울기가 한 프레임에 뛰지 않습니다 (${jump.toFixed(2)})`)

  await browser.close()
  await server.close()
  return failed === 0 ? 0 : 1
}

main().then(code => process.exit(code))
