// 상점 판이 아래에서 올라오는가.
//
// **판이 서는 첫 프레임에 다 선 자리에 한 번 그려지던 결함이 있었습니다.** `syncShop` 이
// 용수철만 `snap` 하고 그리는 자리는 다음 틱에 옮기고 있어서, 그 한 프레임 동안 판이 제자리에
// 서 있다가 화면 아래로 내려가 다시 올라왔습니다 — 눈에는 한 번 튀는 것으로 보입니다.
//
// **눈으로는 원인을 가릴 수 없습니다.** 「튄다」까지는 보이지만 그것이 자리인지 높이인지
// 알파인지, 첫 프레임인지 마지막인지는 보이지 않습니다. 그래서 프레임마다 판의 높이를 읽고
// **첫 프레임의 값**과 **되돌아가는 곳이 있는가**를 봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clearBlind, closeGuide, openRun, pass, peek, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5263
/** 한 프레임. `game.ts` 의 `STEP_MS` 와 같습니다. */
const STEP_MS = 1000 / 60
/** 몇 프레임을 보는가. `SHOP_RISE` 가 0.34초이므로 그보다 넉넉히. */
const FRAMES = 60

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-RISE&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await closeGuide(page)

  // 한 판 이기고 정산의 「받는다」를 누릅니다.
  //
  // **기다리는 것은 여기서 직접 합니다.** 하네스의 `takePayout` 은 상점이 설 때까지
  // 100밀리초씩 돌리므로 **서는 첫 프레임이 그 사이로 지나갑니다** — 그러면 재려던 그
  // 프레임을 못 보고 「곧게 올라온다」로 끝납니다.
  await clearBlind(page)
  await pass(page, 1400)
  let pressed = false
  for (let wait = 0; wait < 60 && !pressed; wait++) {
    const spot = (await peek(page)).spots?.take
    if (spot) {
      // 판이 다 들어온 뒤에 누릅니다. 자리는 판이 열리는 그 프레임에 알려지고, 판은 그
      // 뒤 잠깐 움직이며 들어옵니다.
      await pass(page, 400)
      const here = await at(page, spot.x, spot.y)
      await page.mouse.click(here.x, here.y)
      pressed = true
      break
    }
    await pass(page, 100)
  }

  // **판이 보이기 시작한 그 프레임부터 잡습니다.** 한 프레임씩입니다.
  const seen: number[] = []
  let began = false
  for (let i = 0; i < FRAMES * 4; i++) {
    const now = await peek(page)
    if (now.shopUp) {
      began = true
      seen.push(now.shopY ?? -1)
      if (seen.length >= FRAMES) break
    } else if (began) {
      break
    }
    await step(page)
  }
  if (!pressed) console.log('정산의 「받는다」를 누르지 못했습니다')

  const bad: string[] = []
  if (seen.length < 6) {
    bad.push(`상점이 서는 것을 보지 못했습니다 — 표본 ${seen.length}개`)
  } else {
    console.log(`판의 높이 ${seen.slice(0, 24).join(' → ')}`)
    // **첫 프레임은 화면 아래여야 합니다.** 0 이면 다 선 모습이 한 번 그려진 것입니다.
    if (seen[0] < 100) {
      bad.push(`서는 첫 프레임에 판이 이미 제자리입니다 — 높이 ${seen[0]}`)
    }
    // **되돌아가는 곳이 없어야 합니다.** 올라오다 내려가면 그것이 튀는 것입니다.
    for (let i = 1; i < seen.length; i++) {
      if (seen[i] > seen[i - 1] + 1) {
        bad.push(`${i}번째 프레임에서 판이 되돌아갑니다 — ${seen[i - 1]} → ${seen[i]}`)
        break
      }
    }
    console.log(`처음 ${seen[0]} · 마지막 ${seen[seen.length - 1]}`)
  }

  await browser.close()
  await server.close()
  for (const one of errors.slice(0, 5)) console.error('오류: ' + one)
  console.log(bad.length === 0 && errors.length === 0
    ? '\n상점 판이 아래에서 곧게 올라옵니다'
    : '\n' + bad.concat(errors.slice(0, 5)).join('\n'))
  return bad.length === 0 && errors.length === 0 ? 0 : 1
}

/** 한 프레임만 돌립니다. */
async function step(page: Page): Promise<void> {
  await page.evaluate(ms => {
    const hook = (window as unknown as {
      __clover: { advance?(ms: number): Promise<void> }
    }).__clover
    return hook.advance?.(ms)
  }, STEP_MS)
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
