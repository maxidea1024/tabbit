// 가리킨 것과 고른 것이 위로 올라오는가.
//
// **줄이 차면 겹칩니다.** 손패는 9장부터, 조커와 소모품은 자리가 좁아지면 칸이 겹치는데
// 그때 겹치는 차례는 발동하는 차례입니다 — 가리킨 것과 고른 것이 그 아래에 깔려 있으면,
// 무엇을 가리키고 무엇을 고른 것인지 화면에서 갈리지 않습니다.
//
// **커서를 떼면 줄의 차례로 돌아가야 합니다.** 올려만 두면 지나간 자리마다 하나씩 위에
// 남아, 줄의 겹침이 지나간 길로 뒤섞입니다.
//
// 겹침은 그림으로 판정할 수 없으므로 차례를 값으로 봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickSpot, grantConsumableId, grantJoker, handSpot, jokerSpot, openRun, pass, peek,
  settle, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5239

let bad = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '어긋남'}  ${what}`)
  if (!ok) bad++
}

/** 줄의 차례들. 가리킨 것이 있으면 그 하나만 크게 튑니다. */
async function stackOf(page: import('playwright').Page,
                       row: 'hand' | 'joker' | 'item'): Promise<number[]> {
  return (await peek(page)).stack?.[row] ?? []
}

/** 하나만 나머지보다 위인가. */
function oneOnTop(zs: readonly number[]): boolean {
  if (zs.length < 2) return false
  const top = Math.max(...zs)
  return zs.filter(one => one === top).length === 1
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') console.log('  [콘솔]', one.text().slice(0, 200))
  })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-STACK1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  await grantJoker(page, 'twig')
  await grantJoker(page, 'spinner')
  await grantConsumableId(page, 'the_fool')
  await grantConsumableId(page, 'the_magician')
  await pass(page, 600)

  // 아무것도 가리키지 않은 줄. **차례가 줄의 차례여야 합니다.**
  const restHand = await stackOf(page, 'hand')
  console.log('  손패의 차례', restHand.join(' '))
  // **줄의 차례는 왼쪽부터 오르는 수열입니다.** 겹치는 차례가 발동하는 차례여야 합니다 —
  // 전부 0 이면 붙인 순서로 겹치고, 그것은 정렬한 뒤에 뒤섞입니다.
  const inOrder = restHand.length > 1
    && restHand.every((one, at) => at === 0 || one === restHand[at - 1] + 1)
    && restHand.every(one => one < 60)
  check(inOrder, '아무것도 가리키지 않으면 줄의 차례입니다')

  // 1. 손패의 가운데 카드를 가리킵니다.
  const mid = await handSpot(page, 3, restHand.length)
  await page.mouse.move(mid.x, mid.y)
  await pass(page, 200)
  const overHand = await stackOf(page, 'hand')
  console.log('  가리킨 뒤', overHand.join(' '))
  check(oneOnTop(overHand) && Math.max(...overHand) >= 60,
    '가리킨 카드가 위로 올라옵니다')

  // 2. 커서를 줄 밖으로 뺍니다. **줄의 차례로 돌아가야 합니다.**
  const away = await at(page, 60, 60)
  await page.mouse.move(away.x, away.y)
  await pass(page, 200)
  const outHand = await stackOf(page, 'hand')
  console.log('  뺀 뒤', outHand.join(' '))
  check(outHand.every(one => one < 60), '커서를 떼면 줄의 차례로 돌아옵니다')

  // 3. 카드를 고릅니다. **고른 것은 올라온 채로 남습니다.**
  await page.mouse.click(mid.x, mid.y)
  await pass(page, 260)
  await page.mouse.move(away.x, away.y)
  await pass(page, 200)
  const pickedHand = await stackOf(page, 'hand')
  console.log('  고른 뒤', pickedHand.join(' '))
  check(oneOnTop(pickedHand) && Math.max(...pickedHand) >= 60,
    '고른 카드는 커서를 떼도 위에 남습니다')

  // 4. 조커도 같은 원리입니다.
  const joker = await jokerSpot(page, 0)
  await page.mouse.move(joker.x, joker.y)
  await pass(page, 200)
  const overJoker = await stackOf(page, 'joker')
  console.log('  조커를 가리킨 뒤', overJoker.join(' '))
  check(oneOnTop(overJoker) && Math.max(...overJoker) >= 60, '가리킨 조커가 위로 올라옵니다')

  await page.mouse.move(away.x, away.y)
  await pass(page, 200)
  const outJoker = await stackOf(page, 'joker')
  console.log('  뺀 뒤', outJoker.join(' '))
  check(outJoker.every(one => one < 60), '커서를 떼면 조커도 줄의 차례로 돌아옵니다')

  // 5. **정렬하면 전체가 다시 세워집니다.** 겹치는 차례가 발동하는 차례이므로, 차례가
  // 바뀌면 겹침도 함께 바뀌어야 합니다 — 지난 차례가 남아 있으면 정렬한 손패의 겹침이
  // 정렬 전의 순서로 남습니다.
  await settle(page)
  const orderBefore = (await peek(page)).handOrder.join(' ')
  await clickSpot(page, 'sort:suit')
  await pass(page, 400)
  const sorted = await stackOf(page, 'hand')
  const orderAfter = (await peek(page)).handOrder.join(' ')
  console.log('  정렬 뒤', sorted.join(' '))
  check(orderBefore !== orderAfter, `정렬이 손패의 차례를 바꿉니다 (${orderAfter})`)
  // 고른 카드 하나는 올라온 채로 남으므로 그것만 빼고 오르는 수열이어야 합니다.
  const rowOnly = sorted.filter(one => one < 60)
  check(rowOnly.length === sorted.length - 1
    && rowOnly.every((one, at) => at === 0 || one > rowOnly[at - 1]),
  '정렬한 뒤에도 왼쪽부터 오르는 차례입니다')

  // 6. 소모품도 같은 원리입니다. **칸은 스스로 듣습니다.**
  const items = await stackOf(page, 'item')
  console.log('  소모품의 차례', items.join(' '))
  check(items.length > 1, '소모품이 둘 이상 서 있습니다')

  console.log(bad === 0 ? '모두 통과' : `${bad}건 어긋납니다`)

  await browser.close()
  await server.close()
  return bad === 0 ? 0 : 1
}

main().then(code => process.exit(code))
