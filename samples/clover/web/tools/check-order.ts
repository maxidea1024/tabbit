// 자리를 바꾸는 것이 실제로 되는지 봅니다.
//
// **자리가 규칙입니다** — 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터입니다. 그래서
// 끌어서 자리를 바꾸는 것이 되는지, 그리고 조커를 누르는 것이 파는 것이 아니라 고르는 것인지를
// 확인합니다.
//
//     npx tsx tools/check-order.ts

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  at, BOARD_X, clickPrimary, closeGuide, grantConsumable, grantJoker, HAND_Y, itemSpot,
  jokerSpot, pass, peek, settle, skipLogin, TITLE_START
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5179 } })
  await server.listen()

  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1680, height: 960 } })
  await skipLogin(page)
  const problems: string[] = []
  page.on('pageerror', error => problems.push(String(error)))
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })

  let failed = 0
  const verdict = (good: boolean, line: string) => {
    if (!good) failed++
    console.log(`  ${good ? '✓' : '✗'} ${line}`)
  }

  await page.goto('http://localhost:5179/?seed=CLOVER-SHOT6&tick=manual', { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // 타이틀 → 시작 → 안내 닫기 → 블라인드 고르기.
  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await pass(page, 800)
  await closeGuide(page)
  await pass(page, 400)
  await clickPrimary(page)
  await settle(page)
  await pass(page, 700)

  // ------------------------------------------------------------ 손패의 자리
  console.log('손패')
  // **다 깔릴 때까지 기다립니다.** 깔리는 중에 재면 앞뒤의 장수가 달라, 자리가 바뀌었는지가
  // 아니라 장수가 늘었는지를 보게 됩니다.
  let before = (await peek(page)).handOrder
  for (let i = 0; i < 40; i++) {
    await pass(page, 200)
    const now = (await peek(page)).handOrder
    if (now.length >= 8 && now.join() === before.join()) break
    before = now
  }
  const held = before.length
  const spacing = Math.min(100, 720 / Math.max(1, held))
  const startX = BOARD_X - ((held - 1) * spacing) / 2

  await dragBy(page, await at(page, startX, HAND_Y),
    await at(page, startX + spacing * 3, HAND_Y))
  await page.screenshot({ path: path.join(OUT, 'hand-order.png') })

  const after = (await peek(page)).handOrder
  console.log(`  전 ${before.join(',')}`)
  console.log(`  후 ${after.join(',')}`)
  verdict(before[0] !== after[0] && before.length === after.length, '자리가 바뀝니다')

  // ------------------------------------------------------------ 조커 둘
  await grantJoker(page, 2)
  await pass(page, 600)

  const jokers = (await peek(page)).jokerOrder
  console.log(`\n조커 ${jokers.length}장`)
  if (jokers.length === 0) {
    console.log('  ✗ 조커를 얻지 못해 확인하지 못했습니다')
    failed++
  }

  // ------------------------------------------------------------ 눌러도 팔리지 않아야 합니다
  if (jokers.length >= 1) {
    const where = await jokerSpot(page, 0)
    await page.mouse.click(where.x, where.y)
    await pass(page, 400)
    await page.mouse.move(40, 40)
    await pass(page, 400)
    await page.screenshot({ path: path.join(OUT, 'joker-held.png') })
    verdict((await peek(page)).jokerOrder.length === jokers.length, '눌러도 팔리지 않습니다')
    await page.keyboard.press('Escape')
    await pass(page, 300)
  }

  // ------------------------------------------------------------ 조커의 자리
  if (jokers.length >= 2) {
    await dragBy(page, await jokerSpot(page, 0), await jokerSpot(page, 1))
    await page.screenshot({ path: path.join(OUT, 'joker-order.png') })
    const moved = (await peek(page)).jokerOrder
    console.log(`  전 ${jokers.join(',')}`)
    console.log(`  후 ${moved.join(',')}`)
    verdict(jokers[0] !== moved[0] && jokers.length === moved.length, '자리가 바뀝니다')
  }

  // ------------------------------------------------------------ 소모품도 같습니다
  await grantConsumable(page, 1)
  await pass(page, 500)
  const items = (await peek(page)).consumables
  console.log(`
소모품 ${items}장`)
  if (items >= 1) {
    const where = await itemSpot(page, 0)
    await page.mouse.click(where.x, where.y)
    await pass(page, 400)
    await page.mouse.move(40, 40)
    await pass(page, 400)
    await page.screenshot({ path: path.join(OUT, 'consumable-held.png') })
    verdict((await peek(page)).consumables === items, '눌러도 쓰이지 않습니다')
  } else {
    console.log('  ✗ 소모품을 얻지 못해 확인하지 못했습니다')
    failed++
  }

  await browser.close()
  await server.close()

  for (const problem of problems.slice(0, 8)) console.error('오류: ' + problem)
  console.log(failed === 0 && problems.length === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 || problems.length > 0 ? 1 : 0
}

/**
 * 한 자리에서 다른 자리로 끕니다.
 *
 * **한 번에 옮기지 않습니다** — 눌렀다 뗀 것과 끈 것은 손가락이 움직였는지로 갈리므로,
 * 곧바로 옮기면 화면이 그것을 누른 것으로 봅니다. 끝나면 커서를 치웁니다. 놓은 것 위에
 * 남아 있으면 그것만 들린 채로 찍힙니다.
 */
async function dragBy(page: Page, from: { x: number; y: number },
                      to: { x: number; y: number }): Promise<void> {
  await page.mouse.move(from.x, from.y)
  await page.mouse.down()
  for (let step = 1; step <= 12; step++) {
    await page.mouse.move(from.x + (to.x - from.x) * step / 12,
      from.y + (to.y - from.y) * step / 12 - 14)
    await pass(page, 30)
  }
  await page.mouse.up()
  await pass(page, 300)
  await page.mouse.move(40, 40)
  await pass(page, 800)
}

main().then(code => process.exit(code))
