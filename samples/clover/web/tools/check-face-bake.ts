// 카드 앞면을 몇 번이나 다시 그리는가.
//
// **앞면은 「무늬 · 랭크 · 종이색 · 디버프」가 같으면 같은 그림입니다.** 그래서 한 번 구워
// 두고 다시 씁니다 — 이 도구가 보는 것은 그 굽기가 실제로 재사용되는가입니다.
//
// 카드를 고르고 무르는 것이 `refresh` 를 부르고, `refresh` 는 손패 전부에 `set()` 을
// 부릅니다. **그 왕복을 여러 번 해도 구운 장수는 늘지 않아야 합니다** — 늘면 열쇠에 매번
// 바뀌는 값이 섞인 것이고, 그러면 굽기가 낭비만 됩니다.
//
//     npx tsx tools/check-face-bake.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

import {
  clickCards, clickSpot, closeGuide, pass, peek, pressTitle, settle, skipLogin, swept,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5247
/** 몇 번 고르고 무르는가. */
const ROUNDS = 8

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail === '' ? '' : `  — ${detail}`}`)
}

async function main(): Promise<number> {
  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: PORT }, logLevel: 'error',
  })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BAKE1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await pressTitle(page, 'start')
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 400)
  await clickSpot(page, 'pick')
  await settle(page)
  await swept(page)

  const held = (await peek(page)).hand.length
  const first = (await peek(page)).faceBakes
  // **한 왕복은 버립니다.** 그림은 늦게 옵니다(`onArtReady`) — 없을 때 구운 것과 온 뒤에
  // 구운 것이 다른 그림이므로 카드마다 한 번 더 굽고, 그것은 낭비가 아니라 그림이 온
  // 것입니다. 재는 것은 그 뒤부터입니다.
  await clickCards(page, [0], held)
  await pass(page, 200)
  await clickCards(page, [0], held)
  await pass(page, 200)
  const dealt = (await peek(page)).faceBakes
  if (!dealt) {
    check('화면이 굽기 수를 알립니다', false)
    await browser.close()
    await server.close()
    return 1
  }
  console.log(`손패 ${held}장 · 깔린 뒤 구운 것 ${first?.baked ?? 0}장`
    + ` · 그림이 온 뒤까지 ${dealt.baked}장`)
  check('앞면을 굽습니다', dealt.baked > 0, `${dealt.baked}장`)

  for (let round = 0; round < ROUNDS; round++) {
    await clickCards(page, [0], held)
    await pass(page, 120)
    await clickCards(page, [0], held)
    await pass(page, 120)
  }

  const after = (await peek(page)).faceBakes
  if (!after) {
    check('굽기 수를 계속 알립니다', false)
  } else {
    const grew = after.baked - dealt.baked
    const reused = after.reused - dealt.reused
    console.log(`고르고 무르기 ${ROUNDS}회 뒤 — 더 구운 것 ${grew}장 · 다시 쓴 것 ${reused}회`)
    // **한 장도 더 굽지 않아야 합니다.** 고르는 것은 앞면을 정하는 넷을 하나도 바꾸지
    // 않으므로, 여기서 구운 장수가 늘면 열쇠가 잘못된 것입니다.
    check('다시 그리는 동안 더 굽지 않습니다', grew === 0, `${grew}장`)
    check('구운 것을 다시 씁니다', reused >= ROUNDS * 2, `${reused}회`)
    // 한 벌이 52장이고, 그림이 오기 전의 것이 카드마다 하나 더 있을 수 있습니다.
    check('쥐고 있는 그림이 한 벌의 두 배를 넘지 않습니다',
          after.held <= 104, `${after.held}장`)
    // **묶여 있어야 합니다.** 강화 8종과 디버프와 그림 유무가 곱해지므로 한 판 내내
    // 쌓이면 수백 장이 되고, 배율 3에서 한 장이 380KB 입니다.
    const mb = after.bytes / 1024 / 1024
    console.log(`쥐고 있는 그림 ${after.held}장 · ${mb.toFixed(1)}MB · 놓은 것 ${after.dropped}장`)
    check('메모리가 48MB 안입니다', mb <= 48, `${mb.toFixed(1)}MB`)
  }

  check('오류가 없습니다', errors.length === 0, errors.join(' · '))

  await browser.close()
  await server.close()
  console.log('')
  console.log(failed === 0 ? '다 통과했습니다' : `${failed}개 실패`)
  return failed === 0 ? 0 : 1
}

main().then(code => process.exit(code))
