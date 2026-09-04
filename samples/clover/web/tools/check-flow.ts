// 마무리가 끝난 뒤에 다음 것이 오는가.
//
// 둘을 봅니다. **덱이 카드를 받자마자 빠지지 않는가**와 **상점의 빈자리가 산 물건이 사라진
// 뒤에 메워지는가**입니다. 둘 다 「먼저 온 것이 아직 끝나지 않았는데 다음 것이 시작한다」는
// 한 가지 결함이고, 눈으로는 「마무리가 덜 된 느낌」까지만 보입니다 — 어느 프레임에 무엇이
// 겹쳤는지는 보이지 않으므로 프레임마다 재어야 말할 수 있습니다.
//
//     npx tsx tools/check-flow.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  grantMoney, openRun, pass, peek, settle, shopBuySpot, shopSlot, skipLogin, takePayout,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5245
/** 한 프레임. `game.ts` 의 `STEP_MS` 와 같습니다. */
const STEP_MS = 1000 / 60

/** 마지막 카드가 덱에 닿고 나서 덱이 남아야 하는 가장 짧은 시간. `DECK_LINGER` 보다 짧게 봅니다. */
const DECK_MIN = 0.3

interface Shot {
  recalls: number
  deckX: number
  leaving: number
  /** 그중 아직 온전히 서 있는 것. 사라지기 시작한 딱지는 빠집니다. */
  lingering: number
  tiles: [number, number, number][]
  /** 조커 줄과 소모품 칸에 실제로 서 있는 것의 수. 상태의 수가 아닙니다. */
  drawn: number
}

async function shot(page: Page): Promise<Shot> {
  const now = await peek(page) as unknown as Record<string, unknown>
  return {
    recalls: (now.bins as { recalls?: number })?.recalls ?? 0,
    deckX: now.deckX as number,
    leaving: now.leaving as number,
    lingering: now.lingering as number,
    tiles: (now.shopAt ?? []) as [number, number, number][],
    drawn: (now.drawnJokers as number) + (now.drawnItems as number),
  }
}

async function step(page: Page): Promise<void> {
  await page.evaluate(ms => {
    const hook = (window as unknown as {
      __clover: { advance?(ms: number): Promise<void> }
    }).__clover
    return hook.advance?.(ms)
  }, STEP_MS)
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BUY1&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)

  const bad: string[] = []

  // ------------------------------------------------------------------ 1. 덱
  //
  // 훅으로 격파하고, 돌아온 카드가 마지막으로 덱에 닿는 프레임을 찾습니다. 그 뒤로 덱이
  // 몇 프레임이나 자리에 남는지를 셉니다.
  await page.evaluate(() => {
    const hook = (window as unknown as { __clover: { clearBlind?(): void } }).__clover
    hook.clearBlind?.()
  })

  let sawRecalls = false
  let landed = -1
  let leftAt = -1
  for (let frame = 0; frame < 60 * 14; frame++) {
    const now = await shot(page)
    if (now.recalls > 0) sawRecalls = true
    // 마지막 한 장이 닿은 프레임.
    if (sawRecalls && now.recalls === 0 && landed < 0) landed = frame
    // 덱이 물러나기 시작한 프레임.
    if (landed >= 0 && now.deckX > 2) { leftAt = frame; break }
    await step(page)
  }

  if (!sawRecalls) {
    bad.push('돌아오는 카드를 보지 못했습니다')
  } else if (landed < 0) {
    bad.push('카드가 덱에 다 돌아오지 않았습니다')
  } else if (leftAt < 0) {
    bad.push('덱이 물러나지 않았습니다')
  } else {
    const held = (leftAt - landed) / 60
    console.log(`덱이 남는 시간 ${held.toFixed(2)}초 (프레임 ${leftAt - landed}개)`)
    if (held < DECK_MIN) bad.push(`덱이 ${held.toFixed(2)}초만 남았습니다 — ${DECK_MIN}초 아래입니다`)
  }

  // ------------------------------------------------------------------ 2. 상점
  //
  // 하나 사고, **산 딱지가 그 자리에 있는 동안 남은 것이 움직였는가**와 **사라진 뒤에
  // 미끄러졌는가**를 봅니다. 툭 나타나면 미끄러진 프레임이 하나도 없습니다.
  await takePayout(page)
  await grantMoney(page, 40)
  await settle(page)
  await pass(page, 600)

  const before = await shot(page)
  if (before.tiles.length < 2) {
    console.log(`상점에 살 것이 ${before.tiles.length}개뿐입니다. 시드를 바꿔야 합니다`)
    bad.push('상점의 칸이 둘이 아닙니다')
  } else {
    const stay = before.tiles[1][1]
    const tile = await shopSlot(page, 0)
    await page.mouse.click(tile.x, tile.y)
    await pass(page, 300)
    const buy = await shopBuySpot(page)
    await page.mouse.click(buy.x, buy.y)

    let movedWhileThere = false
    let twice = false
    let sliding = 0
    let settled = -1
    let stood = -1
    for (let frame = 0; frame < 60 * 4; frame++) {
      const now = await shot(page)
      // **산 물건이 두 곳에 보이면 안 됩니다.** 딱지가 온전히 그 자리에 서 있는 동안 제
      // 칸에도 서면 같은 물건이 한 화면에 둘입니다 — 붙드는 표시를 액션 뒤에 세워서 이미
      // 만들어진 뷰에 걸리지 않던 때가 그러했습니다. **사라지기 시작한 뒤는 셀지
      // 않습니다** — 딱지가 엉어지는 그 자리에서 물건이 날아오르므로, 그 겹침은
      // 넘겨주는 몸짓입니다.
      if (now.lingering > 0 && now.drawn > before.drawn) twice = true
      if (now.drawn > before.drawn && stood < 0) stood = frame
      const one = now.tiles[0]
      if (one) {
        // 산 딱지가 아직 있습니다. 남은 것은 제자리에 있어야 합니다.
        if (now.leaving > 0 && Math.abs(one[1] - stay) > 2) movedWhileThere = true
        // 닿을 자리와 지금 자리가 다르면 미끄러지는 중입니다.
        if (Math.abs(one[1] - one[2]) > 2) sliding++
        else if (sliding > 0 && settled < 0) { settled = frame; break }
      }
      await step(page)
    }

    console.log(`남은 딱지: 산 것이 있는 동안 ${movedWhileThere ? '움직였습니다' : '제자리'}`
      + ` · 미끄러진 프레임 ${sliding}개`)
    console.log(`산 물건이 제 칸에 선 프레임 ${stood < 0 ? '없음' : stood}`)
    if (movedWhileThere) bad.push('산 물건이 아직 있는데 빈자리가 메워졌습니다')
    if (sliding < 3) bad.push('남은 딱지가 미끄러지지 않고 새 자리에 나타났습니다')
    if (twice) bad.push('산 물건이 상점의 딱지와 제 칸에 함께 보였습니다')
    if (stood < 0) bad.push('산 물건이 제 칸에 서지 않았습니다')
  }

  await browser.close()
  await server.close()

  for (const one of bad) console.log('  ' + one)
  console.log(bad.length === 0 ? '\n마무리가 끝난 뒤에 다음 것이 옵니다' : '\n어긋납니다')
  return bad.length === 0 ? 0 : 1
}

main().then(code => process.exit(code), error => { console.error(error); process.exit(1) })
