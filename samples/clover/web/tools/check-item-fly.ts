// 산 소모품이 오는 길이 보이는가.
//
// **조커는 날아오는데 소모품은 제 칸에 툭 나타났습니다.** 조커는 뷰가 용수철을 들고 있어서
// 오는 길이 있고, 소모품 칸은 화면을 다시 그릴 때마다 새로 만들어지므로 그럴 것이 없었습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, grantMoney, openRun, pass, peek, settle, shopSlot, skipLogin, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5218

/** 오는 중인 소모품 한 장의 지금 자리와, 어디에서 오는 중인지. */
interface Fly {
  x: number
  y: number
  fromX: number
  fromY: number
  travel: number
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  // **누르는 자리에서 터진 것은 조용히 삼켜집니다.** 브라우저가 콘솔에만 적고 화면은
  // 그대로 도는데, 그러면 「샀는데 오는 길이 없다」 같은 결함이 원인 없이 남습니다.
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') console.log('  [콘솔]', one.text().slice(0, 200))
  })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-FLY1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)

  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)
  await grantMoney(page, 40)
  // **돈이 다 세어질 때까지 기다립니다.** 사는 것은 화면이 주장하는 금액으로 판정하고,
  // 그 금액은 동전이 날아가 꽂히는 동안 올라갑니다 — 400밀리초에 누르면 아직 모자라서
  // 그 누름이 아무 일도 하지 않고, 도구는 「샀다」고 볼 근거가 없는 채로 다음으로 갑니다.
  await settle(page)
  await pass(page, 600)

  // 소모품이 선 칸을 짚습니다.
  //
  // **네 칸을 차례로 눌러 보고 있었습니다.** 조커만 선 상점에서는 넷 다 사지 못하고
  // 「사지 못했습니다」 로 끝났고, 그 말은 오는 길이 있는지 없는지를 말해 주지 않습니다 —
  // 상점이 무엇을 세워 두었는지는 화면이 알려 주면 되는 것입니다.
  const kinds = (await peek(page)).shopKinds ?? []
  const wanted = kinds
    .map((kind, slot) => ({ kind, slot }))
    .filter(one => one.kind !== 1)
    .map(one => one.slot)
  if (wanted.length === 0) {
    console.log('상점에 소모품이 없습니다. 시드를 바꿔야 합니다:', kinds.join(' '))
    await browser.close()
    await server.close()
    return 1
  }

  let bought = false
  const track: Fly[] = []
  for (const slot of wanted) {
    if (bought) break
    const before = (await peek(page)).consumables
    const tile = await shopSlot(page, slot)
    // **이제 두 번 누릅니다.** 한 번은 고르는 것이고, 사는 것은 그 밑에 서는 단추입니다.
    await page.mouse.click(tile.x, tile.y)
    await pass(page, 260)
    const buy = (await peek(page)).spots?.held
    if (!buy) {
      console.log(`  칸 ${slot}: 고른 뒤에도 단추가 서지 않았습니다`)
      continue
    }
    // 고른 그 순간을 한 장 굽습니다. **단추가 어디에 섰는지는 눈으로만 판정됩니다.**
    await page.screenshot({
      path: path.resolve(HERE, '../../design-data/out/check/buy-picked.png') })
    const at2 = await at(page, buy.x, buy.y)
    await page.mouse.click(at2.x, at2.y)

    // 오는 길을 잽니다. **가로와 세로 둘 다입니다.**
    for (let i = 0; i < 24; i++) {
      const spot = await page.evaluate(() => {
        const hook = (window as unknown as { __clover: { fly?: Fly | null } }).__clover
        return hook.fly ?? null
      })
      if (spot) track.push(spot)
      await pass(page, 25)
    }
    const now = await peek(page)
    bought = now.consumables > before
    console.log(`  칸 ${slot}: 금액 ${now.money} · 소모품 ${before} → ${now.consumables}`
      + ` · 자리를 잡아 준 횟수 ${now.flyAsked ?? '?'}`)
    // **어느 길로 샀는지는 소리가 말해 줍니다.** `shop_buy` 면 상점의 그 자리이고,
    // 없으면 이 누름이 산 것이 아닙니다.
    console.log(`     소리: ${(now.sounds ?? []).slice(-6).join(' · ') || '없음'}`)
    if (!bought) track.length = 0
  }

  if (!bought) {
    console.log('소모품을 사지 못했습니다')
    await browser.close()
    await server.close()
    return 1
  }

  const moves = track.filter((one, i) =>
    i > 0 && (one.x !== track[i - 1].x || one.y !== track[i - 1].y)).length
  const first = track[0]
  if (first) {
    console.log('산 자리', `(${first.fromX}, ${first.fromY})`,
      '→ 칸', `(${track[track.length - 1].x}, ${track[track.length - 1].y})`)
  }
  console.log('자리', track.slice(0, 10)
    .map(one => `(${one.x},${one.y})@${one.travel}`).join(' → '))
  console.log('자리가 바뀐 표본', moves, '/', track.length)
  if (track.length === 0) {
    const last = await peek(page)
    console.log('자리를 잡아 준 횟수', last.flyAsked, '· 잡지 못한 횟수', last.flyMissed)
  }

  await browser.close()
  await server.close()
  const good = moves >= 4
  console.log(good ? '오는 길이 보입니다' : '툭 나타납니다')
  return good ? 0 : 1
}

main().then(code => process.exit(code))
