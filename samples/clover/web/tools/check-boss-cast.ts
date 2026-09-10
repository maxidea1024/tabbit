// 보스가 거는 것이 화면에 나타나는가.
//
// **보스는 판이 시작할 때 한 번 크게 개입합니다.** 그런데 그것들은 이벤트조차 내지 않아서,
// 화면이 어느새 회색이 되어 있고 손패가 어느새 엎어져 있었습니다 — 무엇이 그렇게 만든
// 것인지 화면 어디에도 없었습니다.
//
// 재는 것 둘입니다. 덱에 거는 보스(`the_club`)와 조커의 차례를 섞는 보스(`amber_acorn`).
//
// **덱에 거는 것은 그때 손패가 없습니다.** 걸리는 순간에 화면에 있는 카드가 하나도 없으므로,
// 깔리는 카드가 그 자리에서 시드는 것이 그 보스가 한 일입니다 — 미뤄 두었다가 거는 길이
// 실제로 도는지가 이 도구가 재는 것입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  clickPrimary, forceBoss, grantJoker, openRun, pass, peek, settle, skipLogin,
  startNewRun, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5235

/**
 * 상점을 나섭니다. **국면이 바뀔 때까지 다시 누릅니다.**
 *
 * 연출이 도는 중에 누르면 `act` 가 그 누름을 버리고, 버렸다는 것은 화면 어디에도 적히지
 * 않습니다 — 도구가 그것을 「나섰다」로 보고 다음 줄로 넘어가면 그 뒤가 통째로 어긋납니다.
 */
async function leaveShop(page: import('playwright').Page): Promise<void> {
  for (let tries = 0; tries < 12; tries++) {
    if ((await peek(page)).phase !== 'shop') return
    await settle(page)
    await clickPrimary(page)
    await pass(page, 400)
  }
  throw new Error('상점을 나서지 못했습니다')
}

type Page = import('playwright').Page

let bad = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '어긋남'}  ${what}`)
  if (!ok) bad++
}

/**
 * 안테 1의 보스 블라인드까지 갑니다. **그 자리에서 고르기 전에 멈춥니다** — 고르는 순간에
 * 보스가 걸고, 그 순간을 재는 것이 이 도구입니다.
 */
async function walkToBoss(page: Page, bossId: string): Promise<void> {
  await forceBoss(page, bossId)
  await winRound(page)
  await leaveShop(page)
  await pass(page, 600)
  await settle(page)
  await clickPrimary(page)
  await settle(page)
  await winRound(page)
  await leaveShop(page)
  await pass(page, 600)
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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BOSS1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // 1. 덱에 거는 보스. **클럽을 무력화합니다.**
  await openRun(page)
  await walkToBoss(page, 'the_club')
  const atBoss = await peek(page)
  check(atBoss.blind === 3, `보스 블라인드에 닿습니다 (블라인드 ${atBoss.blind})`)

  // **고르고 나서 기다리지 않습니다.** 기다리면 카드가 깔리고 시드는 것이 그 안에서
  // 다 지나갑니다.
  await clickPrimary(page)

  let dealt = 0
  let withered = 0
  let atOnce = 0
  let told = 0
  for (let i = 0; i < 200; i++) {
    const now = await peek(page)
    dealt = Math.max(dealt, now.hand.length)
    // **덱이 나와서 알립니다.** 걸리는 순간에 화면에 카드가 하나도 없으므로, 덱이 나와
    // 한 번 눌리고 몇 장인지가 그 위에 뜹니다.
    if (now.deckPeek === true) told++
    // **한 프레임에 몇 장이 시들었는가.** 여덟 장이 한꺼번이면 한 덩어리가 죽은 것으로
    // 보이므로, 차례로 걸리는지가 이 값으로 확인됩니다.
    atOnce = Math.max(atOnce, now.withering ?? 0)
    if ((now.withering ?? 0) > 0) withered++
    await pass(page, 30)
  }
  const after = await peek(page)
  console.log(`  깔린 손패 ${dealt} · 시드는 것이 보인 표본 ${withered} · 한 번에 가장 많이 ${atOnce}`)
  check(told > 0, '화면에 카드가 없을 때는 덱이 나와 알립니다')
  check(dealt > 0 && withered > 0, '깔리는 카드가 그 자리에서 시듭니다')
  check(atOnce < dealt, '한꺼번에 걸리지 않고 차례로 걸립니다')
  check((after.withering ?? 0) === 0, '다 걸리고 나면 걷힙니다')

  // 2. 조커의 차례를 섞는 보스. **딱지가 둘 있어야 섞입니다.**
  //
  // **판을 다시 엽니다.** 도는 판에서 타이틀로 돌아가려면 메뉴를 거쳐야 하고, 그것은 이
  // 도구가 재려는 것과 무관한 길입니다 — 주소를 다시 열면 처음부터입니다.
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BOSS2&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await startNewRun(page)
  await pass(page, 900)
  if ((await peek(page)).modalUp === true) await page.keyboard.press('Escape')
  await pass(page, 400)
  await grantJoker(page, 'twig')
  await grantJoker(page, 'spinner')
  await pass(page, 300)
  await clickPrimary(page)
  await settle(page)
  await walkToBoss(page, 'amber_acorn')
  await clickPrimary(page)

  for (let i = 0; i < 160; i++) {
    if (((await peek(page)).beats ?? []).includes('JokersShuffled')) break
    await pass(page, 40)
  }
  const shuffled = ((await peek(page)).beats ?? []).includes('JokersShuffled')
  check(shuffled, '조커의 차례가 섞이는 것이 그려집니다')

  console.log(bad === 0 ? '보스가 거는 것이 화면에 나타납니다' : `${bad}건 어긋납니다`)

  await browser.close()
  await server.close()
  return bad === 0 ? 0 : 1
}

main().then(code => process.exit(code))
