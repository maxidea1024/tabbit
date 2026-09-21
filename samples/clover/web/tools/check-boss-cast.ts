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
  clickPrimary, grantJoker, openRun, pass, peek, skipLogin, walkToBoss,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5235

let bad = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '어긋남'}  ${what}`)
  if (!ok) bad++
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  let page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
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
  /** 처음 시드는 것이 보였을 때 손패가 몇 장이었는가. */
  let handWhenCast = -1
  for (let i = 0; i < 200; i++) {
    const now = await peek(page)
    dealt = Math.max(dealt, now.hand.length)
    if (handWhenCast < 0 && (now.withering ?? 0) > 0) handWhenCast = now.hand.length
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
  console.log(`  처음 시들 때의 손패 ${handWhenCast}`)
  check(told > 0, '화면에 카드가 없을 때는 덱이 나와 알립니다')
  check(dealt > 0 && withered > 0, '손패의 카드가 그 자리에서 시듭니다')
  // **다 깔린 뒤입니다.** 깔리는 도중에 장마다 걸던 동안에는 카드가 아직 덱에서 날아오는
  // 중이라 눈이 그 줄에 와 있지 않았습니다 — 무엇이 일어난 것인지 볼 수 없는 자리에서
  // 일어나고 있었습니다.
  check(handWhenCast === dealt, '패가 다 깔린 뒤에 걸립니다')
  check(atOnce < dealt, '한꺼번에 걸리지 않고 차례로 걸립니다')
  check((after.withering ?? 0) === 0, '다 걸리고 나면 걷힙니다')

  // 2. 조커의 차례를 섞는 보스. **딱지가 둘 있어야 섞입니다.**
  //
  // **판을 다시 엽니다.** 도는 판에서 타이틀로 돌아가려면 메뉴를 거쳐야 하고, 그것은 이
  // 도구가 재려는 것과 무관한 길입니다 — 주소를 다시 열면 처음부터입니다.
  // **새 쪽에서 엽니다.** 같은 쪽을 다시 열면 앞 판의 자취가 남아 블라인드 판이 자리를
  // 알리지 않는 자리가 있었습니다 — 판이 갈리는 것은 쪽이 갈리는 것으로 확실히 합니다.
  await page.close()
  page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') console.log('  [콘솔]', one.text().slice(0, 200))
  })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BOSS2&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)
  // **판을 여는 길은 하나입니다.** 여기만 손으로 다시 적어 두었고, 그 줄이 판을 고른 뒤에
  // 한 번 더 고르려 해서 「블라인드 판의 버튼 자리를 화면이 알리지 않습니다」로 끝났습니다.
  await openRun(page)
  await grantJoker(page, 'twig')
  await grantJoker(page, 'spinner')
  await pass(page, 300)
  await walkToBoss(page, 'amber_acorn')
  await clickPrimary(page)

  for (let i = 0; i < 160; i++) {
    if (((await peek(page)).beats ?? []).includes('JokersShuffled')) break
    await pass(page, 40)
  }
  const atShuffle = await peek(page)
  console.log(`  안테 ${atShuffle.ante} · 블라인드 ${atShuffle.blind} · 조커 ${atShuffle.jokers}`)
  const shuffled = (atShuffle.beats ?? []).includes('JokersShuffled')
  check(shuffled, '조커의 차례가 섞이는 것이 그려집니다')

  console.log(bad === 0 ? '보스가 거는 것이 화면에 나타납니다' : `${bad}건 어긋납니다`)

  await browser.close()
  await server.close()
  return bad === 0 ? 0 : 1
}

main().then(code => process.exit(code))
