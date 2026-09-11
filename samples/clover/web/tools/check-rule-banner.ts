// 규칙이 바뀐 것이 손패 줄 위의 판으로 뜨는가.
//
// **토스트로 보내면 지나가는 알림으로 읽힙니다.** 조커가 걸고 소모품이 걸고 보스가 거는
// 규칙은 그 판의 셈법을 통째로 바꾸는 것인데, 화면 오른쪽 구석의 작은 글 두 줄이 전부였고
// 「어, 뭐가 바뀐 거지」가 그것이었습니다.
//
// **바우처를 사서 잽니다.** 규칙으로 들어가는 유일한 물건이고, 사는 것이 곧 규칙이 걸리는
// 것입니다 — 라운드가 시작하고 끝나며 보스의 규칙이 들어오고 나가는 것은 알리지 않습니다.
// 그것은 판마다 같은 두세 줄이 되풀이되는 일이고, 보스가 무엇을 거는지는 블라인드 딱지에
// 그 라운드 내내 적혀 있습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  clickSpot, grantConsumableId, grantMoney, heldButton, itemSpot, openRun, pass, peek,
  settle, skipLogin, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5234

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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-RULE1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)

  // 1. 족보 레벨도 같은 판입니다. 그 판의 값 칸에 레벨이 적힙니다.
  //
  // **라운드 안에서 씁니다.** 소모품은 손패를 앞에 두고 쓰는 것이므로 상점에서는 눌리지
  // 않습니다 — 상점으로 간 뒤에 쓰려 했고, 그래서 아무 일도 일어나지 않았습니다.
  await grantConsumableId(page, 'pluto')
  await pass(page, 400)
  const tile = await itemSpot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await pass(page, 120)
  await page.mouse.down()
  await pass(page, 60)
  await page.mouse.up()
  await pass(page, 350)
  const use = await heldButton(page)
  await page.mouse.click(use.x, use.y)

  let levelled = false
  for (let i = 0; i < 90; i++) {
    const now = await peek(page)
    if (((now.beats ?? []).includes('HandLevelled')) && now.ruleBanner) {
      levelled = true
      break
    }
    await pass(page, 40)
  }
  console.log('  족보 레벨도 판으로 떴는가', levelled)


  // 2. **라운드가 도는 것만으로는 뜨지 않습니다.** 보스의 규칙이 들어오고 나가는 것을
  // 알리면 판마다 같은 두세 줄이 되풀이됩니다.
  await winRound(page)
  await settle(page)
  await pass(page, 900)
  const quiet = await peek(page)
  console.log('  라운드를 돈 뒤 판이 떠 있는가', quiet.ruleBanner !== undefined)

  // 3. 바우처를 삽니다. 사는 것이 곧 규칙이 걸리는 것입니다.
  await grantMoney(page, 30)
  await settle(page)
  await pass(page, 400)
  // **누르는 것은 고르는 것까지입니다.** 카드 칸과 같은 규칙이 되어, 사는 것은 그 밑에 서는
  // 단추입니다.
  await clickSpot(page, 'voucher')
  await pass(page, 300)
  const buy = await heldButton(page)
  await page.mouse.click(buy.x, buy.y)

  let box: { x: number; y: number; width: number; height: number } | undefined
  let head: string | undefined
  let seen = 0
  const heard = new Set<string>()
  for (let i = 0; i < 100; i++) {
    const now = await peek(page)
    if (now.ruleBanner) {
      seen++
      box = box ?? now.ruleBanner
      head = head ?? now.ruleBannerHead
    }
    for (const cue of now.sounds ?? []) heard.add(cue)
    await pass(page, 40)
  }

  const after = await peek(page)
  console.log('  판이 떠 있던 표본', seen, '/ 100')
  console.log('  판의 자리', box ? `${box.x},${box.y} ${box.width}×${box.height}` : '없음')

  // **손패 줄 위 가운데입니다.** 손패는 608 에 가운데가 있고 카드 높이가 124 이므로
  // 윗변이 546 입니다 — 판의 아랫변이 그보다 위여야 손패를 덮지 않습니다.
  const above = box !== undefined && box.y + box.height <= 546
  const middle = box !== undefined && Math.abs(box.x + box.width / 2 - 782) <= 40
  console.log('  손패 위에 섰는가', above, '· 가운데인가', middle)

  // **머리글이 이름이어야 합니다.** 없는 열쇠는 `text` 가 그대로 돌려주므로, 짐작한 앞
  // 토막을 쓰면 `voucher.magic_trick.name` 이 판에 그대로 떴습니다.
  const named = head !== undefined && head !== '' && !head.includes('.name')
    && !/^[a-z_]+\.[a-z_]+$/.test(head)
  console.log('  머리글', JSON.stringify(head ?? null), '· 이름인가', named)

  const good = quiet.ruleBanner === undefined && seen > 0 && above && middle
    && named && after.ruleBanner === undefined && levelled
  console.log(good ? '규칙 변경이 판으로 뜹니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
