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
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, BOARD_X, buyAffordablePack, buyFirstAffordable, chooseFive, clickPrimary,
  discardHand, hurry, openDeckView, peek, pickCards, playHand, pressPlay, rate, settle,
  shopSlot, spare, STAGE_H, STAGE_W, TITLE_START, TITLE_OPTIONS, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'shot')

/** 화면이 밖에 내어 둔 것. `game.ts` 의 `__clover` 입니다. */
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
  await skipLogin(page)

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
  const optionSpot = await at(page, TITLE_OPTIONS.x, TITLE_OPTIONS.y)
  await page.mouse.click(optionSpot.x, optionSpot.y)
  await page.waitForTimeout(700)
  await shoot('0b-options')
  await page.keyboard.press('Escape')
  await page.waitForTimeout(600)
  const startSpot = await at(page, TITLE_START.x, TITLE_START.y)
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

  // **여기서부터는 빠르게 둡니다.** 연출을 찍는 구간은 위에서 끝났고, 아래는 판을 끝까지
  // 두는 것이 목적입니다 — 한 손마다 뜸을 다 기다리면 20분이 넘습니다. 아래에서 찍는 것들은
  // 뜸이 아니라 판의 상태를 찍는 것이고, 판이 끝나는 럼블은 이 속도를 타지 않습니다.
  await hurry(page, 6)

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

main().then(code => { process.exitCode = code })
