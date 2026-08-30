// 화면을 굽습니다.
//
// **되는지가 아니라 보이는지를 봅니다.** 셰이더는 컴파일이 통과해도 화면에서 틀릴 수
// 있고, 배치는 타입이 잡아 주지 않습니다. 빌드한 것을 브라우저에서 열어 그림으로 뽑고,
// 사람이 그것을 봅니다.
//
//     npx tsx tools/shoot.ts
//
// 콘솔 오류가 하나라도 있으면 실패로 끝냅니다 — 셰이더 컴파일 오류가 거기 나옵니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'shot')

/** 화면이 밖에 내어 둔 것. `game.ts` 의 `__clover` 입니다. */
interface Peek {
  phase: string
  ante: number
  money: number
  score: number
  target: number
  busy: boolean
  discards: number
  jokers: number
  packOpen: boolean
  packs: number
  played: number
  blind: number
  coins: boolean
  cleared: boolean
  consumables: number
  hand: { rank: number; suit: number }[]
}

async function peek(page: Page): Promise<Peek> {
  return page.evaluate(() => (window as unknown as { __clover: Peek }).__clover)
}

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })

  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5177 } })
  await server.listen()

  const browser = await chromium.launch()
  // **기준 해상도보다 크게, 그리고 픽셀 밀도를 올려서 찍습니다.** 1280 × 800 을 밀도 1로만
  // 찍으면 배율이 1이라 배치가 틀려도 드러나지 않습니다 — 화면 오른쪽과 아래에 빈 곳이
  // 남던 결함이 그래서 이 도구를 통과했습니다. 가로세로비는 기준과 같게 둡니다.
  const page = await browser.newPage({
    viewport: { width: 1680, height: 960 },
    deviceScaleFactor: 2,
  })

  let shotPack = false
  let shotCoins = false
  let shotClear = false
  let shotToast = false
  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', error => problems.push(String(error)))

  await page.goto('http://localhost:5177/?seed=CLOVER-SHOT6', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  const shoot = async (name: string) => {
    await page.screenshot({ path: path.join(OUT, `${name}.png`) })
    console.log(`${name}.png`)
  }

  // 타이틀. **게임은 여기서 시작합니다.**
  await shoot('0a-title')
  // 옵션. 탭 셋이고 여기 있는 것은 전부 화면에 걸립니다.
  const optionSpot = await at(page, STAGE_W / 2, 568 + 22)
  await page.mouse.click(optionSpot.x, optionSpot.y)
  await page.waitForTimeout(700)
  await shoot('0b-options')
  await page.keyboard.press('Escape')
  await page.waitForTimeout(600)
  const startSpot = await at(page, STAGE_W / 2, 446 + 27)
  await page.mouse.click(startSpot.x, startSpot.y)
  await page.waitForTimeout(700)

  // 처음 여는 사람에게 저절로 펼쳐지는 판입니다. 찍어 두고 닫습니다.
  await shoot('0-guide')
  await page.mouse.click(20, 20)
  await page.waitForTimeout(300)

  await shoot('1-blind-select')

  await clickPrimary(page)
  await page.waitForTimeout(700)
  await shoot('2-round')

  // **족보 도움.** 고른 것이 없을 때 어느 카드가 권해지는지를 찍습니다.
  await page.waitForTimeout(500)
  await shoot('2a-hint')

  // **고른 카드와 고르지 않은 카드가 구분되는지**를 봅니다. 셋 중 둘만 골라 찍습니다.
  const picks = chooseFive((await peek(page)).hand)
  await pickCards(page, picks.slice(0, 3))
  await page.mouse.move(10, 10)
  await page.waitForTimeout(400)
  await shoot('2b-selected')

  // 연출이 도는 중을 찍습니다 — 끝난 뒤에는 낸 카드가 이미 치워져 있어서, 득점이 어떻게
  // 보이는지가 그림에 남지 않습니다.
  // **남은 카드.** 무엇이 덱에 남았는지 보는 판입니다.
  await openDeckView(page)
  await shoot('2c-deck')
  await page.keyboard.press('Escape')
  await page.waitForTimeout(500)

  // **족보 목록.** 줄에 마우스를 올리면 그 족보가 카드로 보입니다.
  const listSpot = await at(page, 16 - 2 + 59, 662 + 17)
  await page.mouse.click(listSpot.x, listSpot.y)
  await page.waitForTimeout(600)
  // 족보 판의 높이는 `game.ts` 의 `drawHandList` 와 같은 계산입니다.
  const listH = 46 + 20 + 12 * 36 + 14 + 56
  const rowSpot = await at(page, STAGE_W / 2 - 60,
    (STAGE_H - listH) / 2 + 46 + 20 + 2 * 36 + 10)
  await page.mouse.move(rowSpot.x, rowSpot.y)
  await page.waitForTimeout(400)
  await shoot('2d-hand-list')
  await page.keyboard.press('Escape')
  await page.waitForTimeout(500)

  await pickCards(page, picks.slice(3))
  await page.waitForTimeout(150)
  await pressPlay(page)
  // 낸 카드가 자리에 붙고 득점 카드를 세기 시작한 뒤입니다.
  await page.waitForTimeout(1500)
  await shoot('3-scoring')

  // 마지막 한 방. **점수가 합쳐지는 순간이 이 게임에서 가장 큰 장면입니다.**
  // **마지막 한 방을 정확히 잡습니다.** 시간으로 재면 스크린샷 자체가 느려 어긋납니다 —
  // 「연출이 끝났고 낸 카드가 아직 판에 있다」가 그 순간입니다.
  for (let wait = 0; wait < 220; wait++) {
    const state = await peek(page)
    if (!state.busy && state.played > 0) break
    await page.waitForTimeout(60)
  }
  await shoot('3b-resolve')

  await page.waitForTimeout(2000)
  await shoot('4-scored')

  // 상점까지 갑니다. **연출이 끝나기를 기다립니다** — 도는 중에 누르면 화면이 받지
  // 않습니다.
  for (let turn = 0; turn < 60; turn++) {
    const before = await peek(page)
    if (before.phase === 'round') {
      const picks = chooseFive(before.hand)
      if (rate(picks.map(index => before.hand[index])) < 60 && before.discards > 0) {
        await discardHand(page, spare(before.hand, picks))
      } else {
        await playHand(page, picks)
      }
      // 「넘겼습니다」 가 튀어나오는 순간.
      if (!shotClear) {
        for (let wait = 0; wait < 240; wait++) {
          const now = await peek(page)
          if (now.cleared) {
            shotClear = true
            await shoot('4b-cleared')
            break
          }
          if (!now.busy && now.phase !== 'round') break
          await page.waitForTimeout(50)
        }
      }

      // **격파 보상이 들어오는 순간.** 동전이 날아가는 것을 여기서 잡습니다.
      for (let wait = 0; wait < 260; wait++) {
        const now = await peek(page)
        if (now.coins) {
          if (!shotCoins) {
            shotCoins = true
            await shoot('13-coins')
          }
          break
        }
        if (now.phase === 'shop' && !now.busy) break
        await page.waitForTimeout(50)
      }
    }

    await settle(page)
    const state = await peek(page)
    if (state.phase === 'shop' || state.phase === 'lost' || state.phase === 'won') break
    if (state.phase === 'blind-select') await clickPrimary(page)
    await page.waitForTimeout(200)
  }

  const finished = await peek(page)
  if (finished.phase === 'shop') {
    await shoot('5-shop')

    // 조커를 먼저 삽니다. **조커가 없는 화면은 이 게임의 화면이 아닙니다** — 팩을 먼저
    // 뜯으면 돈이 모자라 조커를 못 삽니다.
    //
    // 시드는 **첫 상점에 살 수 있는 조커가 놓이는 것**으로 골랐습니다. 아무 시드나 쓰면
    // 상점에 타로와 행성만 놓이는 판이 나오고, 그러면 조커 화면이 빈 채로 찍힙니다.
    await buyFirstAffordable(page)
    await settle(page)
    await page.waitForTimeout(700)
    await shoot('6-joker')

    // 조커 위에 마우스를 올려 설명을 띄웁니다.
    const spot = await at(page, 372, 108)
    await page.mouse.move(spot.x, spot.y)
    await page.waitForTimeout(500)
    await shoot('7-tooltip')
    await page.mouse.move(10, 10)

    // 소모품이 손에 들어오면 써서 토스트를 찍습니다. 상점의 남은 칸을 사 봅니다.
    for (let slot = 0; slot < 2 && (await peek(page)).consumables === 0; slot++) {
      const spot = await shopSlot(page, slot)
      await page.mouse.click(spot.x, spot.y)
      await page.waitForTimeout(400)
    }
    if ((await peek(page)).consumables > 0) {
      shotToast = true
      const spot = await at(page, 962, 108)
      await page.mouse.click(spot.x, spot.y)
      await page.waitForTimeout(340)
      await shoot('15-toast')
      await page.mouse.move(10, 10)
      await page.waitForTimeout(300)
    }

    // 조커를 데리고 다음 판으로. **조커가 발동하는 장면이 이 게임의 얼굴입니다.**
    await clickPrimary(page)
    await settle(page)
    await page.waitForTimeout(400)
    await clickPrimary(page)
    await settle(page)
    await page.waitForTimeout(400)
    await playHand(page, chooseFive((await peek(page)).hand))
    // 조커 줄까지 내려온 뒤입니다 — 카드를 다 세고 나서야 조커가 돕니다.
    await page.waitForTimeout(2600)
    await shoot('8-joker-fires')
  } else {
    console.log(`상점까지 가지 못했습니다 — 지금은 ${finished.phase} 입니다`)
  }

  // 끝날 때까지 둡니다. 도중에 팩을 뜯을 수 있으면 뜯고, 끝난 판까지 찍습니다.
  for (let turn = 0; turn < 300; turn++) {
    await settle(page)
    const state = await peek(page)
    if (state.phase === 'lost' || state.phase === 'won') break

    if (state.packOpen) {
      if (!shotPack) {
        shotPack = true
        await shoot('9-pack')

        // **소모품이 손에 들어오게 고릅니다.** 자리마다 눌러 보고 소모품이 늘면 멈춥니다 —
        // 토스트를 찍으려면 쓸 것이 있어야 합니다.
        const before = (await peek(page)).consumables
        for (let slot = 0; slot < 5; slot++) {
          if (!(await peek(page)).packOpen) break
          const spot = await at(page, BOARD_X - 78 + slot * 156, 322 + 79)
          await page.mouse.click(spot.x, spot.y)
          await page.waitForTimeout(500)
          if ((await peek(page)).consumables > before) break
        }
        await shoot('10-pack-picked')
      }
      if ((await peek(page)).packOpen) {
        const skip = await at(page, BOARD_X, 494 + 20)
        await page.mouse.click(skip.x, skip.y)
        await page.waitForTimeout(300)
      }
      continue
    }

    if (turn % 8 === 0) {
      console.log(`  [${turn}] ${state.phase} 소모품 ${state.consumables} 팩 ${state.packs}`)
    }

    // 소모품을 써서 토스트를 띄웁니다. **무엇을 썼는지가 글로 남아야 합니다.**
    if (!shotToast && state.consumables > 0 && state.phase !== 'round') {
      shotToast = true
      const spot = await at(page, 962, 108)
      await page.mouse.click(spot.x, spot.y)
      await page.waitForTimeout(320)
      await shoot('15-toast')
      await page.mouse.move(10, 10)
      await page.waitForTimeout(400)
      continue
    }

    if (state.phase === 'shop') {
      // 소모품이 아직 없으면 상점의 칸을 사 봅니다. **여러 상점을 지나면 타로나 행성이
      // 나옵니다** — 토스트를 찍으려면 쓸 것이 있어야 합니다.
      if (!shotToast && state.consumables === 0) {
        await buyFirstAffordable(page)
        await page.waitForTimeout(300)
      }
      if (!shotPack && state.packs > 0) await buyAffordablePack(page)
      if (!(await peek(page)).packOpen) await clickPrimary(page)
    } else if (state.phase === 'blind-select') {
      await clickPrimary(page)
    } else if (state.phase === 'round') {
      const picks = chooseFive(state.hand)
      if (rate(picks.map(index => state.hand[index])) < 60 && state.discards > 0) {
        await discardHand(page, spare(state.hand, picks))
      } else {
        await playHand(page, picks)
      }
    }
    await page.waitForTimeout(180)
  }

  const end = await peek(page)
  if (end.phase === 'lost' || end.phase === 'won') {
    // 럼블이 도는 중을 찍습니다.
    await page.waitForTimeout(180)
    await shoot('11-over-rumble')
    await page.waitForTimeout(900)
    await shoot('12-over')
  } else {
    console.log(`끝까지 가지 못했습니다 — 지금은 ${end.phase} 입니다`)
  }

  // 그림이 들어온 것을 확인합니다. **202장을 한 번에 만들지 않으므로 있는 것과 없는 것이
  // 섞인 상태에서도 화면이 돌아야 합니다.**
  await page.goto('http://localhost:5177/artcheck.html', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1600)
  await shoot('16-art')

  // 에디션 셰이더는 상점 추첨으로만 붙으므로 게임 안에서는 눈으로 보기 어렵습니다.
  // 나란히 세운 페이지를 따로 찍습니다.
  await page.goto('http://localhost:5177/editions.html', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1400)
  await shoot('14-editions')

  if (!shotPack) console.log('팩을 뜯지 못했습니다 — 돈이 모자랍니다')
  if (!shotCoins) console.log('동전이 날아가는 장면을 잡지 못했습니다')
  if (!shotClear) console.log('「넘겼습니다」 장면을 잡지 못했습니다')
  if (!shotToast) console.log('소모품을 쓰지 못했습니다')

  await browser.close()
  await server.close()

  if (problems.length > 0) {
    console.error('브라우저가 오류를 냈습니다:')
    for (const problem of problems.slice(0, 10)) console.error('  ' + problem)
    return 1
  }

  console.log('오류 없음')
  return 0
}

/** 연출이 끝날 때까지 기다립니다. */
async function settle(page: Page): Promise<void> {
  for (let wait = 0; wait < 60; wait++) {
    const state = await peek(page)
    if (!state.busy) return
    await page.waitForTimeout(200)
  }
}

/** 살 수 있는 팩이 있으면 뜯습니다. */
async function buyAffordablePack(page: Page): Promise<void> {
  const tileW = 210
  const gap = 16
  for (let slot = 0; slot < 2; slot++) {
    const packs = (await peek(page)).packs
    if (packs <= slot) return
    const span = packs * tileW + (packs - 1) * gap
    const left = BOARD_X - SHOP_W / 2 + (SHOP_W - span) / 2
    const spot = await at(page, left + slot * (tileW + gap) + tileW / 2,
      SHOP_Y + SHOP_ITEMS + 172 + 20 + 26 + 42)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(600)
    if ((await peek(page)).packOpen) return
  }
}

/**
 * 상점의 칸을 살 수 있으면 삽니다.
 *
 * 자리는 `game.ts` 의 `syncShop` 과 같은 계산입니다 — 판이 가운데에 서고 물건이 그 안에서
 * 가운데로 모입니다.
 */
async function buyFirstAffordable(page: Page): Promise<void> {
  for (let slot = 0; slot < 4; slot++) {
    const spot = await shopSlot(page, slot)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(500)
    if ((await peek(page)).jokers > 0) return
  }
}

/** 상점의 물건 칸 하나의 가운데. */
async function shopSlot(page: Page, slot: number, count = 2): Promise<{ x: number; y: number }> {
  const tileW = 158
  const gap = 14
  const span = count * tileW + (count - 1) * gap
  const left = BOARD_X - SHOP_W / 2 + (SHOP_W - span) / 2
  return at(page, left + slot * (tileW + gap) + tileW / 2, SHOP_Y + SHOP_ITEMS + 86)
}

/** `game.ts` 의 `syncShop` 과 같은 값들. */
const SHOP_W = 780
const SHOP_ITEMS = 86
const SHOP_Y = 90

/** 화면 좌표를 캔버스 위의 자리로. 기준 해상도는 1280 × 720 입니다. */
/**
 * 기준 좌표를 캔버스 위의 자리로.
 *
 * **판은 창을 꽉 채우지 않습니다** — 기준 비율에 맞춰 가운데에 놓이고 남는 자리는 배경이
 * 덮습니다. 그래서 캔버스의 비율만으로 환산하면 어긋납니다. `game.ts` 의 `layout` 과 같은
 * 계산을 여기서도 합니다.
 */
async function at(page: Page, x: number, y: number): Promise<{ x: number; y: number }> {
  const box = await (await page.$('#stage'))?.boundingBox()
  if (!box) return { x, y }
  const scale = Math.min(box.width / STAGE_W, box.height / STAGE_H)
  const originX = box.x + Math.round((box.width - STAGE_W * scale) / 2)
  const originY = box.y + Math.round((box.height - STAGE_H * scale) / 2)
  return { x: originX + x * scale, y: originY + y * scale }
}

// 화면의 자리들. `render/game.ts` 의 상수와 같아야 합니다.
const STAGE_W = 1280
const STAGE_H = 800
const BOARD_X = (16 + 264 + 20 + STAGE_W) / 2
const HAND_Y = 620
const CARD_SPACING = 100
const BUTTON_Y = 742

/**
 * 가운데 큰 버튼. 블라인드 선택과 상점이 씁니다.
 *
 * **상점에서는 자리가 다릅니다** — 바우처 딱지와 겹치지 않게 아래로 내려가 있습니다.
 */
async function clickPrimary(page: Page): Promise<void> {
  if ((await peek(page)).phase === 'shop') {
    // 상점의 밑단. **판 하나로 정돈되면서 버튼도 그 안으로 들어왔습니다.**
    const spot = await at(page, BOARD_X + 83, SHOP_Y + 572 - 56 / 2)
    await page.mouse.click(spot.x, spot.y)
    return
  }
  // 블라인드 선택은 **판 셋이 나란히** 서고 지금 차례인 것만 자기 버튼을 가집니다.
  // `game.ts` 의 `drawBlindPick` 과 같은 계산입니다 — 아랫변을 맞추므로 버튼의 자리는
  // 판의 높이와 상관없이 아래에서 셉니다.
  const cardW = 226
  const gap = 20
  const bottom = 754
  const index = { 1: 0, 2: 1, 3: 2 }[(await peek(page)).blind as 1 | 2 | 3] ?? 0
  const startX = BOARD_X - (2 * (cardW + gap)) / 2 - cardW / 2
  const spot = await at(page, startX + index * (cardW + gap) + cardW / 2, bottom - 106 + 22)
  await page.mouse.click(spot.x, spot.y)
}

/**
 * 패에서 다섯 장을 골라 냅니다.
 *
 * **화면을 실제로 누르는 것이 요점입니다** — 코어를 직접 부르면 화면이 도는지를 확인하지
 * 못합니다. 카드는 108픽셀 간격으로 가운데 놓이고, 8장일 때 첫 장의 중심이 262 입니다.
 */
async function playHand(page: Page, picks: number[] = [0, 1, 2, 3, 4]): Promise<void> {
  await pickCards(page, picks)
  await pressPlay(page)
}

/** 남은 카드 판을 열고 닫습니다. 왼쪽 패널 아래의 버튼입니다. */
async function openDeckView(page: Page): Promise<void> {
  const spot = await at(page, 16 - 2 + 59, 700 + 17)
  await page.mouse.click(spot.x, spot.y)
  await page.waitForTimeout(350)
}

/** 고르기만 합니다. 고른 카드의 셰이더를 찍으려면 낸다를 누르기 전에 멈춰야 합니다. */
async function pickCards(page: Page, picks: number[]): Promise<void> {
  const held = (await peek(page)).hand.length
  await clickCards(page, picks, held)
}

async function pressPlay(page: Page): Promise<void> {
  const play = await at(page, BOARD_X - 176 + 64, BUTTON_Y + 23)
  await page.mouse.click(play.x, play.y)
}

/** 부채꼴로 편 패에서 몇 장을 누릅니다. */
async function clickCards(page: Page, picks: number[], held: number): Promise<void> {
  const spacing = Math.min(CARD_SPACING, 720 / Math.max(1, held))
  const startX = BOARD_X - ((held - 1) * spacing) / 2

  for (const i of picks) {
    const offset = i - (held - 1) / 2
    const spot = await at(page, startX + i * spacing, HAND_Y + offset * offset * 1.1)
    await page.mouse.click(spot.x, spot.y)
    await page.waitForTimeout(80)
  }
}

/** 고른 것을 버립니다. */
async function discardHand(page: Page, picks: number[]): Promise<void> {
  const held = (await peek(page)).hand.length
  await clickCards(page, picks, held)
  const discard = await at(page, BOARD_X + 48 + 64, BUTTON_Y + 23)
  await page.mouse.click(discard.x, discard.y)
}

/** 쓸 만한 다섯 장에 들지 못한 카드들. 버릴 대상입니다. */
function spare(hand: { rank: number }[], picks: number[]): number[] {
  const keep = new Set(picks)
  return hand.map((_, index) => index).filter(index => !keep.has(index)).slice(0, 5)
}

/**
 * 패에서 쓸 만한 다섯 장.
 *
 * 다섯 장 조합 56가지를 전부 보고 가장 높은 족보를 고릅니다. **잘 두려는 것이 아니라
 * 상점까지 가려는 것입니다** — 무작정 왼쪽 다섯 장을 내면 안테 1 에서 끝납니다.
 *
 * 족보의 값은 여기 손으로 적혀 있습니다. 도구이므로 그래도 되고, 게임의 값은 시트에
 * 있습니다.
 */
function chooseFive(hand: { rank: number; suit: number }[]): number[] {
  let best: number[] = [0, 1, 2, 3, 4].filter(i => i < hand.length)
  let bestScore = -1

  const indices = hand.map((_, index) => index)
  for (const combo of fiveOf(indices)) {
    const value = rate(combo.map(index => hand[index]))
    if (value > bestScore) {
      bestScore = value
      best = combo
    }
  }
  return best.sort((a, b) => a - b)
}

function* fiveOf(indices: number[]): Generator<number[]> {
  const want = Math.min(5, indices.length)
  const combo: number[] = []
  const walk = (start: number): Generator<number[]> | void => undefined
  void walk

  const stack: number[][] = [[]]
  while (stack.length > 0) {
    const current = stack.pop() as number[]
    if (current.length === want) { yield current; continue }
    const from = current.length === 0 ? 0 : current[current.length - 1] + 1
    for (let i = indices.length - 1; i >= from; i--) stack.push([...current, indices[i]])
  }
  void combo
}

/** 족보의 대략적인 값. 순서만 맞으면 됩니다. */
function rate(cards: { rank: number; suit: number }[]): number {
  const ranks = new Map<number, number>()
  const suits = new Map<number, number>()
  for (const card of cards) {
    ranks.set(card.rank, (ranks.get(card.rank) ?? 0) + 1)
    suits.set(card.suit, (suits.get(card.suit) ?? 0) + 1)
  }

  const counts = [...ranks.values()].sort((a, b) => b - a)
  const flush = [...suits.values()].some(count => count >= 5)
  const sorted = [...ranks.keys()].sort((a, b) => a - b)
  const straight = sorted.length >= 5
    && sorted[sorted.length - 1] - sorted[0] === sorted.length - 1

  const high = Math.max(...cards.map(card => card.rank)) / 100
  if (counts[0] >= 5) return 400 + high
  if (flush && counts[0] >= 3 && counts[1] >= 2) return 380 + high
  if (flush && straight) return 300 + high
  if (counts[0] >= 4) return 200 + high
  if (counts[0] >= 3 && counts[1] >= 2) return 160 + high
  if (flush) return 140 + high
  if (straight) return 120 + high
  if (counts[0] >= 3) return 90 + high
  if (counts[0] >= 2 && counts[1] >= 2) return 60 + high
  if (counts[0] >= 2) return 30 + high
  return high
}

main().then(code => { process.exitCode = code })
