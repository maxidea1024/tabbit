// 조커가 만든 소모품이 오는 길이 보이는가.
//
// **상점과 팩만 그 몫을 들고 있었습니다.** `itemFlying` 을 부르는 자리가 둘뿐이라, 조커가
// 만들고 태그가 주는 소모품은 칸에 툭 나타났습니다 — `OpCreateCard` 로 소모품을 만드는
// 것이 `JokerEffect` 29줄 · `TagEffect` 6줄입니다.
//
// `card_reader` 로 잽니다. 블라인드를 고를 때 확률 없이 타로 한 장을 만드는 조커입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { clickPrimary, grantJoker, pass, peek, skipLogin, startNewRun } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5236

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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-MADE1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // **`openRun` 을 쓰지 않습니다.** 그것은 블라인드까지 골라 버리는데, 조커는 고르기
  // 전에 들려야 합니다 — 만드는 것이 고르는 그 순간입니다.
  await startNewRun(page)
  await pass(page, 900)
  if ((await peek(page)).modalUp === true) await page.keyboard.press('Escape')
  await pass(page, 400)

  await grantJoker(page, 'card_reader')
  await pass(page, 400)
  const before = await peek(page)
  console.log('고르기 전 · 소모품', before.consumables, '· 자리를 잡아 준 횟수', before.flyAsked)

  await clickPrimary(page)

  // **기다리지 않습니다.** 기다리면 오는 길이 그 안에서 다 지나갑니다.
  let firstSeen = -1
  for (let i = 0; i < 160; i++) {
    const now = await peek(page)
    if (now.consumables > before.consumables && firstSeen < 0) firstSeen = i
    await pass(page, 30)
  }

  const after = await peek(page)
  console.log('고른 뒤 · 소모품', after.consumables, '· 자리를 잡아 준 횟수', after.flyAsked,
              '· 잡을 것이 없어 돌아온 횟수', after.flyMissed)
  console.log('소모품이 화면에 나타난 표본 번호', firstSeen)

  // **자리를 잡아 준 횟수가 늘어야 합니다.** 그것이 곧 「오는 길이 있었다」입니다 —
  // 툭 나타나는 것은 그 횟수가 그대로입니다.
  const flew = (after.flyAsked ?? 0) > (before.flyAsked ?? 0)
  const missed = (after.flyMissed ?? 0) === (before.flyMissed ?? 0)
  console.log('오는 길이 잡혔는가', flew, '· 잡을 것이 없어 돌아온 적은 없는가', missed)

  const good = after.consumables === before.consumables + 1 && flew && missed
  console.log(good ? '조커가 만든 소모품도 오는 길이 있습니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
