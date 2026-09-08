// 누름 하나가 하는 일. **규격은 `doc/ui.md` 의 「누름과 그 밑의 단추」입니다.**
//
// 두 가지를 봅니다.
//
// 1. **서 있는 쪽지가 있으면 그 누름은 닫는 누름입니다** — 꾸욱 눌러 세운 쪽지 위에서 누르면
//    쪽지만 닫히고 단추는 서지 않습니다. 한 번 더 눌러야 섭니다.
// 2. **고른 것 밖을 누르면 놓습니다** — 빈자리뿐 아니라 다른 물건과 다른 단추도 같습니다.
//
// 조커와 소모품 둘을 같은 순서로 봅니다. **갈래마다 다르게 적으면 어느 것은 한 번에 팔리고
// 어느 것은 두 번에 팔립니다.**
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, grantConsumable, grantJoker, HAND_Y, openRun, pass, peek, skipLogin, spot,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5215

let failed = 0

function check(good: boolean, what: string, note = ''): void {
  console.log(`  ${good ? '통과' : '어긋남'}  ${what}${note ? ` (${note})` : ''}`)
  if (!good) failed++
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  // **손가락으로 엽니다.** 꾸욱 누르기는 손가락에만 있습니다 — 마우스의 쪽지는 커서를 따라
  // 뜨고 벗어나면 닫히므로 서 있는 쪽지가 없습니다.
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 }, hasTouch: true, isMobile: false,
  })
  const page = await context.newPage()
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-PRESS1&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  await grantJoker(page, 2)
  await grantConsumable(page, 1)
  await pass(page, 800)

  for (const name of ['joker:0', 'item:0']) {
    console.log(name === 'joker:0' ? '조커' : '소모품')
    const one = await spot(page, name)

    // 1. 꾸욱 누릅니다. 쪽지가 서고 단추는 서지 않습니다.
    await press(page, one, 'down')
    await pass(page, 900)
    await press(page, one, 'up')
    await pass(page, 300)
    let now = await peek(page)
    check(now.tip && !bar(now), '꾸욱 누르면 쪽지가 서고 단추는 서지 않습니다',
      `쪽지 ${now.tip} · 단추 ${bar(now)}`)

    // 2. 한 번 누릅니다. **닫는 누름입니다** — 쪽지가 닫히고 단추는 서지 않습니다.
    await tap(page, one)
    await pass(page, 300)
    now = await peek(page)
    check(!now.tip && !bar(now), '쪽지가 서 있을 때의 누름은 닫기만 합니다',
      `쪽지 ${now.tip} · 단추 ${bar(now)}`)

    // 3. 다시 누릅니다. 이제 단추가 섭니다.
    await tap(page, one)
    await pass(page, 300)
    now = await peek(page)
    check(bar(now), '쪽지가 없을 때의 누름은 단추를 세웁니다')

    // 4. 같은 것을 다시 누릅니다. 놓습니다.
    await tap(page, one)
    await pass(page, 300)
    check(!bar(await peek(page)), '같은 것을 다시 누르면 놓습니다')

    // 5. 다시 세우고, **다른 곳을 누릅니다.** 손패의 카드입니다.
    await tap(page, one)
    await pass(page, 300)
    if (!bar(await peek(page))) {
      check(false, '다른 곳을 누르기 전에 단추가 서지 않았습니다')
      continue
    }
    const card = await at(page, 640, HAND_Y)
    await tap(page, card)
    await pass(page, 300)
    check(!bar(await peek(page)), '다른 물건을 누르면 놓습니다')

    // 6. 다시 세우고, **단추 줄 밖의 단추를 누릅니다.**
    //
    // **정렬입니다.** 「낸다」로 재면 그 누름이 실제로 패를 내므로 그 뒤의 확인이 득점이
    // 도는 동안으로 넘어가고, 그때는 아무것도 골라지지 않습니다 — 이 도구가 무엇을 재는
    // 것인지와 상관없는 이유로 어긋납니다.
    await tap(page, one)
    await pass(page, 300)
    if (!bar(await peek(page))) {
      check(false, '단추를 누르기 전에 단추 줄이 서지 않았습니다')
      continue
    }
    const sort = await spot(page, 'sort:rank')
    await tap(page, sort)
    await pass(page, 300)
    check(!bar(await peek(page)), '다른 단추가 눌리면 놓습니다')

    // **고른 손패를 되돌립니다.** 다음 갈래를 볼 때 판이 처음과 같아야 합니다.
    await tap(page, card)
    await pass(page, 300)
  }

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '누름의 규칙이 지켜집니다' : `${failed}개 어긋납니다`)
  return failed === 0 ? 0 : 1
}

/** 고른 것 밑에 단추 줄이 서 있는가. **화면이 알린 자리로 봅니다.** */
function bar(now: { spots?: Record<string, unknown> }): boolean {
  return now.spots?.held !== undefined
}

/**
 * 손가락으로 누릅니다.
 *
 * **`touchscreen.tap` 은 누르고 있는 시간을 정할 수 없습니다.** 꾸욱 누르기를 재려면 그
 * 시간이 요점이므로 이벤트를 손으로 냅니다. `check-hold.ts` 와 같은 자리입니다 — 안쪽에
 * 이름 붙은 함수를 두지 않습니다(넘길 때 `__name` 이라는 도우미가 끼어들고, 그것은 페이지에
 * 없습니다).
 */
async function press(page: Page, one: { x: number; y: number },
                     what: 'down' | 'up'): Promise<void> {
  await page.evaluate(([x, y, type]) => {
    const canvas = document.getElementById('stage') as HTMLCanvasElement
    canvas.dispatchEvent(new PointerEvent(`pointer${type}`, {
      pointerId: 7, pointerType: 'touch', isPrimary: true,
      clientX: x, clientY: y, bubbles: true, cancelable: true,
    }))
  }, [one.x, one.y, what] as [number, number, string])
}

/** 짧게 누릅니다. 꾸욱 누르기의 문턱(`HOLD_TIP`)보다 짧아야 합니다. */
async function tap(page: Page, one: { x: number; y: number }): Promise<void> {
  await press(page, one, 'down')
  await pass(page, 100)
  await press(page, one, 'up')
}

main().then(code => process.exit(code))
