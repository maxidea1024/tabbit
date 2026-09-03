// 조커와 소모품이 자기 자리를 넘어가지 않는가.
//
// **칸 수는 규칙이 정합니다** — 덱·바우처·챌린지가 조커 칸과 소모품 칸을 늘리고 줄입니다.
// 칸마다 사각형을 그리던 동안에는 줄의 너비가 그 수를 따라갔고, 조커가 8칸이면 그 줄이
// 소모품 줄과 겹쳤습니다.
//
// 지금은 자리가 고정된 사각형 둘이고 몇 개든 그 안에서 배치됩니다. **그것이 지켜지는지는
// 눈으로 확인할 수 없습니다** — 몇 개까지 담기는지 세어 볼 수 없고, 자리를 넘어간 한 장은
// 옆 줄이나 화면 밖에 서므로 화면 안쪽만 보고 있으면 보이지 않습니다. 그래서 화면이 알리는
// 두 사각형을 견줍니다.
import * as path from 'path'
import * as fs from 'fs'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  grantConsumable, grantJoker, itemSpot, jokerSpot, openRun, pass, peek, skipLogin,
  STAGE_W, type Rect,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5248

/**
 * 몇 개까지 놓아 보는가.
 *
 * **기본 칸 수를 넘겨 봅니다.** 조커 5·소모품 2 까지만 재면 지금 그 수에 맞춰 둔 자리가
 * 딱 맞는다는 것만 확인되고, 정작 고치려던 것 — 규칙이 칸을 늘렸을 때 — 은 재지 않습니다.
 */
const STEPS = [1, 2, 3, 5, 8, 14] as const

function inside(one: Rect, room: Rect): boolean {
  return one.x >= room.x - 0.5 && one.y >= room.y - 0.5
    && one.x + one.width <= room.x + room.width + 0.5
    && one.y + one.height <= room.y + room.height + 0.5
}

function overlaps(a: Rect, b: Rect): boolean {
  return a.x < b.x + b.width && b.x < a.x + a.width
    && a.y < b.y + b.height && b.y < a.y + a.height
}

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TRAY1`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)

  const bad: string[] = []
  const trays = (await peek(page)).trays
  if (!trays) {
    console.log('화면이 자리를 알리지 않습니다')
    await browser.close()
    await server.close()
    return 1
  }
  console.log(`조커  자리 ${describe(trays.joker)}`)
  console.log(`소모품 자리 ${describe(trays.item)}`)

  // **두 자리가 서로 겹치면 그 아래의 확인은 아무 말도 아닙니다.** 카드가 자기 자리 안에
  // 있어도 그 자리가 이미 옆 자리를 덮고 있습니다.
  if (overlaps(trays.joker, trays.item)) bad.push('두 자리가 겹칩니다')

  let has = 0
  for (const want of STEPS) {
    await grantJoker(page, want - has)
    await grantConsumable(page, want - has)
    has = want
    await pass(page, 900)

    const now = await peek(page)
    const cards = now.trayCards
    if (!cards) {
      bad.push(`${want}개에서 화면이 카드의 자리를 알리지 않습니다`)
      continue
    }
    for (const [which, room, row] of [
      ['조커', trays.joker, cards.joker],
      ['소모품', trays.item, cards.item],
    ] as const) {
      const out = row.filter(one => !inside(one, room))
      const spill = row.filter(one => overlaps(one,
        which === '조커' ? trays.item : trays.joker))
      const gap = row.length > 1 ? row[1].x - row[0].x : 0
      console.log(`  ${which} ${String(row.length).padStart(2)}장`
        + ` 간격 ${gap.toFixed(1)}`
        + ` 왼쪽 ${row[0] ? row[0].x.toFixed(1) : '-'}`
        + ` 오른쪽 ${row.length ? (row[row.length - 1].x + row[0].width).toFixed(1) : '-'}`
        + (out.length === 0 && spill.length === 0 ? '' : '  ✗'))
      if (out.length > 0) bad.push(`${which} ${row.length}장 중 ${out.length}장이 자리를 넘어갑니다`)
      if (spill.length > 0) bad.push(`${which} ${row.length}장 중 ${spill.length}장이 옆 자리를 덮습니다`)
    }
    await shot(page, `tray-${String(want).padStart(2, '0')}`)
  }

  // ------------------------------------------- 끝 칸을 골랐을 때의 단추 줄
  //
  // **가운데를 맞춘 줄은 끝 칸에서 화면을 넘어갑니다.** 소모품 줄은 화면 오른쪽에 붙어
  // 있어서 마지막 칸 아래의 「쓴다 · 판다」가 잘렸고, 잘린 쪽은 줄의 오른쪽 끝이라
  // 첫 단추의 자리만 보고 있으면 보이지 않습니다.
  for (const [which, index] of [
    ['소모품', STEPS[STEPS.length - 1] - 1],
    ['조커', 0],
  ] as const) {
    const where = which === '소모품'
      ? await itemSpot(page, index) : await jokerSpot(page, index)
    await page.mouse.click(where.x, where.y)
    await pass(page, 400)
    const held = (await peek(page)).heldBox
    if (!held) {
      bad.push(`${which} ${index}번을 골랐는데 단추 줄이 서지 않습니다`)
      continue
    }
    const off = held.x < 0 || held.x + held.width > STAGE_W
    console.log(`  ${which} ${index}번의 단추 줄`
      + ` x ${held.x.toFixed(1)}~${(held.x + held.width).toFixed(1)}`
      + (off ? '  ✗' : ''))
    if (off) bad.push(`${which} ${index}번의 단추 줄이 화면을 넘어갑니다`)
    await page.keyboard.press('Escape')
    await pass(page, 250)
  }

  await browser.close()
  await server.close()

  for (const one of errors.slice(0, 8)) console.error('오류: ' + one)
  console.log(bad.length === 0 && errors.length === 0
    ? '\n몇 개가 되어도 자리 안에 섭니다'
    : '\n' + bad.concat(errors.slice(0, 8)).join('\n'))
  return bad.length === 0 && errors.length === 0 ? 0 : 1
}

function describe(one: Rect): string {
  return `x ${one.x.toFixed(1)}~${(one.x + one.width).toFixed(1)}`
    + ` y ${one.y.toFixed(1)}~${(one.y + one.height).toFixed(1)}`
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
