// 꾸욱 누르면 설명이 뜨는가.
//
// **마우스에는 「올린다」가 있고 손가락에는 없습니다.** 그래서 손가락으로는 설명을 볼
// 방법이 없었습니다 — 누르고 기다리는 것이 그 자리를 대신합니다.
//
// 셋을 봅니다. 짧게 누르면 뜨지 않고 고르기가 되는가, 오래 누르면 뜨는가, 그리고 **오래
// 누른 것이 고르기가 되지 않는가.**
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickPrimary, HAND_Y, peek, settle, STAGE_W, TITLE_START_Y,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5213

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  // **손가락으로 엽니다.** 마우스로는 올리는 것만으로 뜨므로 확인이 되지 않습니다.
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 }, hasTouch: true, isMobile: false,
  })
  const page = await context.newPage()
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-HOLD1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.touchscreen.tap(start.x, start.y)
  await page.waitForTimeout(900)
  await page.touchscreen.tap(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  const held = (await peek(page)).hand.length
  if (held === 0) {
    console.log('패가 없습니다')
    await browser.close()
    await server.close()
    return 1
  }

  // 첫 장의 자리. `harness` 의 셈과 같습니다.
  const spacing = Math.min(100 + 12, 720 / Math.max(1, held))
  const cardX = (16 + 264 + 20 + STAGE_W) / 2 - ((held - 1) * spacing) / 2
  const spot = await at(page, cardX, HAND_Y)

  // 1. 짧게 누릅니다. 뜨지 않고 골라져야 합니다.
  await tap(page, spot, 120)
  await page.waitForTimeout(260)
  const quick = await peek(page)
  console.log('짧게 — 쪽지', quick.tip ? '떠 있음' : '없음', '· 고른 수', quick.picked)

  // 되돌립니다.
  await tap(page, spot, 120)
  await page.waitForTimeout(260)

  // 2. 오래 누릅니다. 뜨고, 골라지지 않아야 합니다.
  await page.touchscreen.tap(spot.x, spot.y - 300)
  await page.waitForTimeout(300)
  const before = await peek(page)
  await hold(page, spot, 900)
  const during = await shotPeek(page, 'hold-1')
  await press(page, spot, 'up')
  await page.waitForTimeout(300)
  const after = await peek(page)
  console.log('오래 — 누르는 중 쪽지', during.tip ? '떠 있음' : '없음',
    '· 뗀 뒤 고른 수', before.picked, '->', after.picked)

  await browser.close()
  await server.close()
  const good = !quick.tip && quick.picked === 1 && during.tip && after.picked === before.picked
  console.log(good ? '꾸욱 누르기가 됩니다' : '어긋납니다')
  return good ? 0 : 1
}

/**
 * 손가락으로 누릅니다.
 *
 * **`touchscreen.tap` 은 누르고 있는 시간을 정할 수 없습니다.** 꾸욱 누르기를 재려면 그
 * 시간이 요점이므로 이벤트를 손으로 냅니다.
 *
 * 안쪽에 이름 붙은 함수를 두지 않습니다 — 넘길 때 `__name` 이라는 도우미가 끼어들고,
 * 그것은 페이지에 없습니다.
 */
async function press(page: Page, spot: { x: number; y: number },
                     what: 'down' | 'up'): Promise<void> {
  await page.evaluate(([x, y, type]) => {
    const canvas = document.getElementById('stage') as HTMLCanvasElement
    canvas.dispatchEvent(new PointerEvent(`pointer${type}`, {
      pointerId: 7, pointerType: 'touch', isPrimary: true,
      clientX: x, clientY: y, bubbles: true, cancelable: true,
    }))
  }, [spot.x, spot.y, what] as [number, number, string])
}

/** 짧게 누릅니다. */
async function tap(page: Page, spot: { x: number; y: number }, ms: number): Promise<void> {
  await press(page, spot, 'down')
  await page.waitForTimeout(ms)
  await press(page, spot, 'up')
}

/** 누른 채로 기다립니다. **떼지 않고 그 사이를 재야 합니다.** */
async function hold(page: Page, spot: { x: number; y: number }, ms: number): Promise<void> {
  await press(page, spot, 'down')
  await page.waitForTimeout(ms)
}

/** 누르고 있는 그 순간을 재고 찍습니다. 떼는 것은 부르는 쪽에서 합니다. */
async function shotPeek(page: Page, name: string): Promise<{ tip: boolean }> {
  const now = await peek(page)
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
  return now
}

main().then(code => process.exit(code))
