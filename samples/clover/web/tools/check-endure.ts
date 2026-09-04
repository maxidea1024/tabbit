// 오래 두어도 진행이 멈추지 않는가.
//
// **누르는 자리에서 난 예외는 브라우저가 조용히 삼킵니다.** 화면은 그대로 돌고 그 손잡이의
// 뒷부분만 죽으므로, 잡고 있던 것이 잡힌 채로 남거나 다음 차례가 오지 않습니다 — 사람에게는
// 「어느 순간부터 진행이 안 되고 드래그가 이상해진다」로 보입니다.
//
// 그래서 이 도구가 보는 것은 화면이 아니라 **콘솔과 상태의 멈춤**입니다. 판을 오래 두면서
// 예외를 전부 받아 적고, 상태가 몇 차례 이어서 그대로면 거기서 멈춘 것으로 봅니다.
//
//     npx tsx tools/check-endure.ts [판 수]
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, closeGuide, discardHand, pass, peek, playHand, rate,
  settle, shopSlot, skipLogin, spare, takePayout, TITLE_START
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5233

/** 상태가 이만큼 이어서 그대로면 멈춘 것으로 봅니다. */
const STUCK_AFTER = 6

async function main(argv: string[]): Promise<number> {
  const rounds = Number(argv[0] ?? 60)
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)

  const blew: string[] = []
  page.on('pageerror', error => blew.push(error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') blew.push(one.text())
  })

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-ENDURE&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 400)

  let same = 0
  let mark = ''
  let acted = 0
  // **어디를 지났는지 적습니다.** 지나지 않은 자리는 이 도구가 본 것이 아니고, 「터진 것
  // 0건」 은 그 자리에 대해서는 아무 말도 아닙니다.
  const seen = new Set<string>()

  for (let turn = 0; turn < rounds; turn++) {
    const state = await peek(page)
    // 무엇 하나라도 달라졌으면 진행 중입니다.
    const now = `${state.phase}|${state.ante}|${state.blind}|${state.money}`
      + `|${state.score}|${state.hands}|${state.discards}|${state.hand.length}`
    same = now === mark ? same + 1 : 0
    mark = now
    seen.add(state.phase)

    if (same >= STUCK_AFTER) {
      console.log(`${turn}번째에 멈췄습니다 — ${now}`)
      break
    }
    if (state.phase === 'lost' || state.phase === 'won') {
      console.log(`판이 끝났습니다 — ${state.phase}, 안테 ${state.ante}`)
      // **끝난 판에는 카드가 남지 않아야 합니다.** 끝났다는 판이 그 위에 서는데 그 밑에
      // 손패가 그대로 있으면, 끝난 것과 아직 쥐고 있는 것이 한 화면에 겹칩니다.
      await pass(page, 2600)
      const after = await peek(page)
      const bins = after.bins
      console.log(`끝난 뒤 카드 ${after.views}장`
        + (bins ? ` — 손패 ${bins.hand} · 낸 것 ${bins.played} · 걷는 중 ${bins.fades}`
          + ` · 깔 것 ${bins.deals} · 화면이 주장하는 패 ${bins.shown}` : ''))
      break
    }

    if (state.phase === 'round') {
      // **한 라운드는 넘겨 줍니다.** 자동 진행은 안테 1을 넘기지 못하고 지므로, 그대로
      // 두면 상점을 한 번도 지나지 않은 채 「터진 것 0건」 으로 끝납니다.
      if (!seen.has('shop')) {
        await page.evaluate(() => {
          const hook = (window as unknown as { __clover: { clearBlind?(): void } }).__clover
          hook.clearBlind?.()
        })
        await settle(page)
        await pass(page, 400)
        acted++
        continue
      }
      const picks = chooseFive(state.hand)
      if (rate(picks.map(i => state.hand[i])) < 60 && state.discards > 0) {
        await discardHand(page, spare(state.hand, picks))
      } else {
        await playHand(page, picks)
      }
      acted++
    } else {
      // **상점에서는 한 칸을 골라 봅니다.** 고르면 그 칸이 들리고 그 밑에 단추가 서는데,
      // 그 둘은 매 프레임 도는 코드입니다 — 골라 보지 않으면 그 코드에 닿지 않고, 닿지
      // 않으면 거기서 터지는 것을 이 도구가 보지 못합니다. 실제로 그렇게 지나갔습니다.
      if (state.phase === 'shop' && state.payout) {
        // 정산 판이 아직 서 있습니다. 「받는다」 를 눌러야 상점이 섭니다.
        await takePayout(page)
        acted++
        continue
      }
      // **상품 줄이 있을 때만 짚습니다.** 다 산 줄은 없어지므로 짚을 칸이 없습니다.
      if (state.phase === 'shop' && state.shopUp && (state.shopKinds?.length ?? 0) > 0) {
        const tile = await shopSlot(page, 0)
        await page.mouse.click(tile.x, tile.y)
        await pass(page, 320)
        // 고른 채로 상점을 나갑니다. **고른 것이 지워지는 그 순간이 위험한 자리입니다.**
      }
      // 블라인드를 고르거나 상점을 나갑니다. **누를 것이 하나로 남는 자리입니다.**
      //
      // **상점 판이 아직 서지 않았으면 그 차례는 넘깁니다.** 국면이 상점이 되는 것과 판이
      // 서는 것은 다른 순간이고, 서지 않은 판의 단추를 누를 수는 없습니다.
      if (state.phase === 'shop' && !state.shopUp) {
        // **카드가 걷혀 덱으로 돌아가고 정산이 서기까지입니다.** 격파한 뒤 낸 카드와 손패가
        // 물러나고 뒷면이 덱으로 돌아온 다음에 정산 판이 서므로, 그 사이가 2초 가까이
        // 됩니다 — 300밀리초씩 여섯 번이면 멈춘 것으로 잘못 봅니다.
        for (let wait = 0; wait < 20; wait++) {
          const now = await peek(page)
          if (now.payout || now.shopUp) break
          await pass(page, 200)
        }
        continue
      }
      await clickPrimary(page)
      acted++
    }
    await settle(page)
    await pass(page, 120)
  }

  const last = await peek(page)
  console.log(`둔 차례 ${acted} · 마지막 ${last.phase} 안테 ${last.ante}`)
  console.log(`지난 국면 ${[...seen].join(' · ')}`)
  console.log(`터진 것 ${blew.length}건`)
  for (const one of blew.slice(0, 5)) console.log('  ' + one.split('\n').slice(0, 3).join('\n    '))

  await browser.close()
  await server.close()
  return blew.length === 0 && same < STUCK_AFTER ? 0 : 1
}

main(process.argv.slice(2)).then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
