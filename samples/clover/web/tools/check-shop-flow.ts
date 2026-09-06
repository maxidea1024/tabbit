// 사는 흐름에서 상점이 자리를 비켜 주는가.
//
// 셋을 봅니다.
// 1. 팩을 뜯으면 **상점이 내려간 뒤에** 카드가 펼쳐지는가 · 닫히면 상점이 다시 올라오는가.
// 2. 팩에서 플레잉 카드를 집으면 덱이 나와 받는가(그 팩에 플레잉 카드가 있을 때만).
// 3. 자리가 없는 조커를 사면 묻는 판 대신 **위 줄에서 고르는 화면**이 들고, 상점이 내려가
//    있으며, 줄의 조커를 눌러 내놓으면 화면이 걷히고 상점이 돌아오는가.
//
// **한 프레임씩 돕니다.** 상점이 내려가는 것과 카드가 나오는 것의 차례는 수십 밀리초의
// 일이라, 실제 시계로 기다리면 재려던 그 프레임이 사이로 지나갑니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickSpot, grantJoker, grantMoney, heldButton, openRun, packSlot, pass, peek, settle, shopSlot,
  skipLogin, spot, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5231

interface Sample {
  t: number
  shopY: number
  cards: number
  open: boolean
  focus: boolean
  deckPeek: boolean
  deckX: number
  pulse: boolean
}

async function sample(page: Page): Promise<Sample> {
  const now = await peek(page)
  return {
    t: Math.round(now.clock * 1000),
    shopY: now.shopY ?? 0,
    cards: now.packCards ?? 0,
    open: now.packOpen,
    focus: now.focus ?? false,
    deckPeek: now.deckPeek ?? false,
    deckX: now.deckX ?? 0,
    pulse: now.countPulse ?? false,
  }
}

async function track(page: Page, frames: number, step = 40): Promise<Sample[]> {
  const out: Sample[] = []
  for (let i = 0; i < frames; i++) {
    out.push(await sample(page))
    await pass(page, step)
  }
  return out
}

let failed = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '실패'}  ${what}`)
  if (!ok) failed++
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-FLOW2&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  // 왼쪽 판의 딱지가 상황마다 무엇을 적는지 석 장 굽습니다. **눈으로만 판정됩니다.**
  const shot = (name: string) =>
    page.screenshot({ path: path.resolve(HERE, `../../design-data/out/check/${name}.png`) })
  await pass(page, 900)
  await shot('badge-blind')
  await winRound(page)
  await shot('badge-shop')
  await grantMoney(page, 120)
  await settle(page)
  await pass(page, 600)

  // 0. 상점에서 보는 덱은 전부 쓸 수 있는 카드입니다.
  console.log('상점의 덱')
  {
    const now = await peek(page)
    check(now.drawLeft === now.deckSize && (now.deckSize ?? 0) > 0,
      `뽑을 패가 덱 전체입니다 (${now.drawLeft} / ${now.deckSize})`)
  }

  // 1. 팩. 상점이 내려간 뒤에 카드가 나오는가.
  console.log('팩을 뜯는 것')
  // **표준 팩이 있으면 그것을 뜯습니다.** 플레잉 카드가 덱으로 가는 것은 그 팩에서만 봅니다.
  let ids = (await peek(page)).packIds ?? []
  if (!ids.some(id => id.startsWith('standard'))) {
    // 없으면 첫 칸을 표준 팩으로 바꿉니다. **개발 서버에서만 됩니다.**
    await page.evaluate(() => {
      const hook = (window as unknown as { __clover: { stockPack?(id: string): void } }).__clover
      hook.stockPack?.('standard_normal')
    })
    await pass(page, 300)
    ids = (await peek(page)).packIds ?? []
  }
  const standard = ids.findIndex(id => id.startsWith('standard'))
  const which = standard >= 0 ? standard : 0
  console.log(`   팩 칸: ${ids.join(' · ')} → ${which}번`)
  const packTile = await packSlot(page, which)
  await page.mouse.click(packTile.x, packTile.y)
  await pass(page, 300)
  const buyPack = await heldButton(page)
  await page.mouse.click(buyPack.x, buyPack.y)
  // 값을 치르는 박자(1.15초) 뒤에 상점이 내려가고 그다음 카드가 깔립니다.
  const opening = await track(page, 80)
  const firstCards = opening.find(one => one.cards > 0)
  const opened = opening.some(one => one.open)
  check(opened, '팩이 열렸습니다')
  check(firstCards !== undefined, '카드가 펼쳐졌습니다')
  if (firstCards) {
    check(firstCards.shopY > 300,
      `첫 카드가 나오는 프레임에 상점이 내려가 있습니다 (shopY ${firstCards.shopY})`)
    // 내려가는 동안 되돌아오는 곳이 없어야 합니다.
    let rising = true
    for (let i = 1; i < opening.length && opening[i].cards === 0; i++) {
      if (opening[i].shopY < opening[i - 1].shopY - 1) rising = false
    }
    check(rising, '상점이 내려가는 동안 되돌아오는 곳이 없습니다')
  }
  console.log('   ' + opening.slice(24, 48).map(one => `${one.shopY}/${one.cards}`).join(' '))

  // 2. 첫 장을 집습니다. 플레잉 카드면 덱이 나와 받아야 합니다.
  await shot('badge-pack')
  const cardSpot = await spot(page, 'pack:0')
  await page.mouse.click(cardSpot.x, cardSpot.y)
  await pass(page, 260)
  const take = (await peek(page)).spots?.held
  check(take !== undefined, '펼친 카드를 고르면 그 밑에 단추가 섭니다')
  if (take) {
    const where = await at(page, take.x, take.y)
    await page.mouse.click(where.x, where.y)
    // 덱으로 가는 길을 두 장 굽습니다. **덱이 나와 있고 카드가 그리로 가는지는 눈으로만 판정됩니다.**
    const taking = await track(page, 6)
    await page.screenshot({ path: path.resolve(HERE, '../../design-data/out/check/pack-deck-1.png') })
    taking.push(...await track(page, 8))
    await page.screenshot({ path: path.resolve(HERE, '../../design-data/out/check/pack-deck-2.png') })
    // 덱이 받고 물러나는 것(2.1초)과 상점이 올라와 서는 것까지입니다.
    taking.push(...await track(page, 90))
    // **닿기 전에는 올라오지 않습니다.** 집은 것이 줄에 닿는 데 0.52초입니다.
    check(taking.slice(0, 12).every(one => one.shopY > 300), '집은 것이 닿기 전에는 상점이 올라오지 않습니다')
    const peeked = taking.filter(one => one.deckPeek)
    if (peeked.length > 0) {
      const shown = taking.some(one => one.deckPeek && one.deckX < 40)
      check(shown, `덱이 나와서 받습니다 (deckX 최소 ${Math.min(...peeked.map(one => one.deckX))})`)
      const now = await peek(page)
      check(now.drawLeft === now.deckSize,
        `들어온 카드도 뽑을 패에 있습니다 (${now.drawLeft} / ${now.deckSize})`)
    } else {
      console.log('  참고  이 팩에는 플레잉 카드가 없어서 덱 연출은 이번에 보지 않았습니다')
    }
    const closed = taking.find(one => !one.open)
    if (closed) {
      const back = taking.some(one => !one.open && one.shopY < 1)
      check(back, '팩이 닫히면 상점이 다시 올라옵니다')
    } else {
      console.log('  참고  두 장을 고르는 팩이라 아직 열려 있습니다. 건너뜁니다')
    }
  }
  // 팩이 아직 열려 있으면 건너뜁니다. **자리는 화면이 알립니다.**
  if ((await peek(page)).packOpen) {
    await clickSpot(page, 'packSkip')
    await pass(page, 400)
  }
  await settle(page)
  await pass(page, 1200)

  // 2.5 리롤. 있던 카드가 걷히고 새것이 내려와 앉는가.
  console.log('리롤')
  {
    await grantMoney(page, 30)
    await settle(page)
    const before = ((await peek(page)).shopAt ?? []).length
    await clickSpot(page, 'reroll')
    const rolling = []
    for (let i = 0; i < 30; i++) {
      const now = await peek(page)
      rolling.push({ tiles: (now.shopAt ?? []).length, leaving: now.leaving ?? 0 })
      await pass(page, 40)
    }
    check(rolling.some(one => one.leaving > 0 && one.tiles === 0),
      '있던 카드가 걷히는 동안 새 카드는 아직 없습니다')
    check((rolling[rolling.length - 1]?.tiles ?? 0) >= Math.min(before, 1),
      `새 카드가 다시 섭니다 (${before} → ${rolling[rolling.length - 1]?.tiles})`)
    console.log('   ' + rolling.slice(0, 14).map(one => `${one.tiles}/${one.leaving}`).join(' '))
    await settle(page)
    await pass(page, 1500)
  }

  // 3. 자리가 없는 조커. 줄에서 고르는 화면.
  console.log('자리를 비우는 것')
  await grantJoker(page, 5)
  await grantMoney(page, 60)
  await settle(page)
  await pass(page, 400)
  const kinds = (await peek(page)).shopKinds ?? []
  const jokerSlot = kinds.indexOf(1)
  if (jokerSlot < 0) {
    console.log('  참고  상점에 조커가 없습니다. 시드를 바꿔야 합니다:', kinds.join(' '))
  } else {
    const jokersBefore = (await peek(page)).jokers
    const tile = await shopSlot(page, jokerSlot)
    await page.mouse.click(tile.x, tile.y)
    await pass(page, 300)
    const swap = await heldButton(page)
    await page.mouse.click(swap.x, swap.y)
    const entering = await track(page, 30)
    check(entering.some(one => one.focus), '줄에서 고르는 화면이 들었습니다')
    check(entering.some(one => one.focus && one.shopY > 300), '그동안 상점이 내려가 있습니다')
    check(!(await peek(page)).modalUp, '묻는 판은 뜨지 않습니다')

    // 줄의 첫 조커를 누르고, 그 밑의 단추로 내놓습니다.
    const first = await spot(page, 'joker:0')
    await page.mouse.click(first.x, first.y)
    await pass(page, 260)
    const give = (await peek(page)).spots?.held
    check(give !== undefined, '줄의 조커를 누르면 그 밑에 내놓는 단추가 섭니다')
    if (give) {
      const where = await at(page, give.x, give.y)
      const moneyBefore = (await peek(page)).money
      await page.mouse.click(where.x, where.y)
      // 타고(0.42) · 닿고(0.52) · 보고(0.8) · 상점이 올라와 서는 것까지입니다.
      const leaving = await track(page, 90)
      check(leaving.some(one => !one.focus), '내놓으면 화면이 걷힙니다')
      // 내놓은 것이 타고 새것이 닿는 데 0.42 + 0.52초입니다.
      check(leaving.slice(0, 20).every(one => one.shopY > 300), '새것이 닿기 전에는 상점이 올라오지 않습니다')
      check(leaving.some(one => !one.focus && one.shopY < 1), '상점이 다시 올라옵니다')
      const now = await peek(page)
      check(now.jokers === jokersBefore, `조커 수가 그대로입니다 (${jokersBefore} → ${now.jokers})`)
      check(leaving.some(one => one.pulse), '닿은 자리에서 칸 수가 강조됩니다')
      console.log(`   금액 ${moneyBefore} → ${now.money}`)
    }
  }

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '모두 통과' : `${failed}개 실패`)
  return failed === 0 ? 0 : 1
}

main().then(code => process.exit(code))
