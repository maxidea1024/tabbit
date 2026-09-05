// 도감이 표와 같은 것을 세우는가.
//
// **게이트가 아니라 확인 도구입니다.** 여기서 보는 것은 셋입니다 — 갈래마다 칸의 수가 표의
// 행 수와 같은가 · 저장을 채우면 뒷면이 앞면이 되는가 · 판을 열고 갈래 아홉을 지나도 오류가
// 없는가.
//
// 첫째가 이 도구를 만든 이유입니다. **화면만 보면 갈리지 않습니다** — 한 쪽에 40칸까지이고
// 바우처는 32개이므로, 한 쪽만 보고서는 32개인지 30개인지 알 수 없습니다. 그래서 화면이
// `collection` 으로 세어 알리게 두고 그 수를 표와 견줍니다.
//
//     npx tsx tools/check-collection.ts

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

import { clickSpot, pass, peek, pressTitle, skipLogin, spot } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'check')
const GRIDS = path.resolve(HERE, '..', '..', 'design-data', 'data')
const PORT = 5197

/**
 * 그 표의 행 수.
 *
 * **격자를 셉니다.** 화면이 읽는 것은 변환된 이진 파일이므로 여기서 셀 수 없고, 격자는
 * 기획자가 적는 그 파일입니다 — 표에 한 줄을 더했는데 도감에 칸이 늘지 않으면 그것이
 * 이 도구가 잡아야 하는 것입니다. `:` 로 시작하는 세 줄이 선언이고 나머지가 행입니다.
 */
function rows(table: string): number {
  return fs.readFileSync(path.join(GRIDS, `${table}.tsv`), 'utf-8')
    .split(/\r?\n/)
    .filter(line => line.trim() !== '' && !line.startsWith(':'))
    .length
}

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: PORT },
  })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, locale: 'ko-KR' })
  await skipLogin(page)

  const errors: string[] = []
  page.on('pageerror', one => errors.push(one.message))
  page.on('console', one => { if (one.type() === 'error') errors.push(one.text()) })

  let failed = 0
  const check = (name: string, ok: boolean, note = ''): void => {
    if (!ok) failed++
    console.log(`  ${ok ? '✓' : '✗'} ${name}${note ? '  —  ' + note : ''}`)
  }

  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  // 저장을 비우고 다시 엽니다. **아무것도 만나지 않은 상태**가 시작점입니다.
  await page.evaluate(() => {
    localStorage.removeItem('clover.collection')
    localStorage.removeItem('clover.options')
    localStorage.setItem('clover.guide.seen', '1')
  })
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)

  await pressTitle(page, 'collection')
  await pass(page, 1000)
  await page.screenshot({ path: path.join(OUT, 'collection-0-joker.png') })

  // **「없음」과 「기본」은 칸이 되지 않습니다.** 강화 · 인장 · 에디션의 표에는 아무것도
  // 붙지 않은 줄이 하나씩 있고, 그것은 붙일 수 있는 것이 아니라 붙지 않은 상태입니다.
  const want: { tab: string; cells: number; note: string }[] = [
    { tab: 'joker', cells: 150, note: '기본 150종' },
    { tab: 'consumable', cells: rows('Tarot') + rows('Planet') + rows('Spectral'),
      note: '타로 · 행성 · 유령' },
    { tab: 'voucher', cells: rows('Voucher'), note: '바우처' },
    { tab: 'card', cells: rows('Enhancement') + rows('Seal') + rows('Edition') - 3,
      note: '강화 · 인장 · 에디션에서 「없음」 셋을 뺀 것' },
    { tab: 'pack', cells: rows('BoosterPack'), note: '팩' },
    { tab: 'tag', cells: rows('Tag'), note: '태그' },
    { tab: 'blind', cells: rows('Blind') + rows('BossBlind'), note: '블라인드 · 보스' },
    { tab: 'stake', cells: rows('Stake'), note: '스테이크' },
    { tab: 'deck', cells: rows('Deck'), note: '덱' },
  ]

  for (const one of want) {
    await clickSpot(page, `collection:tab:${one.tab}`)
    await pass(page, 500)
    const seen = (await peek(page)).collection
    check(`${one.tab} 칸이 ${one.cells}개입니다`,
          seen?.tab === one.tab && seen?.cells === one.cells,
          `${seen?.tab} ${seen?.cells}개 · ${one.note}`)
    check(`${one.tab} 가 처음에는 전부 뒷면입니다`, seen?.found === 0,
          `발견 ${seen?.found}`)
  }
  await page.screenshot({ path: path.join(OUT, 'collection-1-deck.png') })

  // 확장까지 켜면 조커가 500종입니다. **옵션이 아니라 보는 범위입니다.**
  await clickSpot(page, 'collection:tab:joker')
  await pass(page, 400)
  await clickSpot(page, 'collection:range:all')
  await pass(page, 700)
  const all = (await peek(page)).collection
  check('확장까지 보면 조커가 500종입니다', all?.cells === rows('Joker'),
        `${all?.cells}종`)

  // 굴림. **쪽 넘김이 아니라 굴림입니다** — 500종이 한 줄로 이어져 있고, 바퀴가 없는
  // 기계에서는 끌어서 굴립니다.
  const where = await spot(page, 'collection:tab:joker')
  const middle = { x: where.x, y: where.y + 260 }

  await page.mouse.move(middle.x, middle.y)
  await page.mouse.wheel(0, 400)
  await pass(page, 700)
  const rolled = (await peek(page)).collection
  check('바퀴로 굴러갑니다', (rolled?.offset ?? 0) < 0, `${rolled?.offset}`)

  // **손가락으로도 굴러야 합니다.** 바퀴가 없는 기계에는 끌기 말고 굴릴 길이 없습니다.
  await page.mouse.move(middle.x, middle.y + 160)
  await page.mouse.down()
  for (let step = 1; step <= 8; step++) {
    await page.mouse.move(middle.x, middle.y + 160 - step * 18)
    await pass(page, 30)
  }
  await page.mouse.up()
  await pass(page, 900)
  const dragged = (await peek(page)).collection
  check('끌어서도 굴러갑니다', (dragged?.offset ?? 0) < (rolled?.offset ?? 0),
        `${rolled?.offset} → ${dragged?.offset}`)
  await page.screenshot({ path: path.join(OUT, 'collection-4-rolled.png') })

  // 막대를 잡고 끌기. **손가락으로 목록을 끄는 것과 다른 일입니다** — 긴 목록에서 아래쪽을
  // 보려고 목록을 여러 번 쓸어 올리는 것이 막대에서는 한 번입니다.
  await clickSpot(page, 'collection:tab:joker')
  await pass(page, 500)
  const bar = await spot(page, 'collection:bar')
  await page.mouse.move(bar.x, bar.y)
  await page.mouse.down()
  for (let step = 1; step <= 6; step++) {
    await page.mouse.move(bar.x, bar.y + step * 24)
    await pass(page, 30)
  }
  await page.mouse.up()
  await pass(page, 500)
  const barred = (await peek(page)).collection
  check('막대를 잡고 끌면 굴러갑니다', (barred?.offset ?? 0) < -400, `${barred?.offset}`)
  await page.screenshot({ path: path.join(OUT, 'collection-5-bar.png') })

  // 막대의 빈 자리를 누르면 그 자리로 옮겨 갑니다.
  const lane = await spot(page, 'collection:bar')
  await page.mouse.click(lane.x, lane.y - 120)
  await pass(page, 500)
  const jumped = (await peek(page)).collection
  check('막대의 빈 자리를 누르면 그리로 옮겨 갑니다',
        (jumped?.offset ?? 0) > (barred?.offset ?? 0), `${jumped?.offset}`)

  // 탭을 바꾸면 맨 위로 돌아갑니다. **앞 탭에서 굴려 둔 자리를 물려받지 않습니다.**
  await clickSpot(page, 'collection:tab:tag')
  await pass(page, 500)
  await clickSpot(page, 'collection:tab:joker')
  await pass(page, 500)
  const back = (await peek(page)).collection
  check('탭을 바꾸면 맨 위입니다', back?.offset === 0, `${back?.offset}`)

  // 저장을 채우고 다시 엽니다. **앞면이 되는지가 이 도구의 둘째 판정입니다.**
  await page.keyboard.press('Escape')
  await pass(page, 400)
  await page.evaluate(() => {
    localStorage.setItem('clover.collection', JSON.stringify({
      deck: ['red_deck', 'blue_deck'],
      stake: ['White'],
      blind: ['Small', 'Big', 'Boss'],
      seal: ['Red'],
    }))
    // 얼굴이 실제로 그려지는지는 사람이 봅니다. **판정에는 쓰지 않습니다** — 위의
    // 저장과 갈라 두어야 세는 수가 흔들리지 않습니다.
    localStorage.setItem('clover.collection.look', '1')
  })
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  await pressTitle(page, 'collection')
  await pass(page, 900)

  const marks: { tab: string; found: number }[] = [
    { tab: 'deck', found: 2 },
    { tab: 'stake', found: 1 },
    { tab: 'blind', found: 3 },
    { tab: 'card', found: 1 },
    { tab: 'joker', found: 0 },
  ]
  for (const one of marks) {
    await clickSpot(page, `collection:tab:${one.tab}`)
    await pass(page, 500)
    const seen = (await peek(page)).collection
    check(`${one.tab} 에서 ${one.found}개가 앞면입니다`, seen?.found === one.found,
          `발견 ${seen?.found}`)
  }
  await clickSpot(page, 'collection:tab:deck')
  await pass(page, 500)
  await page.screenshot({ path: path.join(OUT, 'collection-2-deck.png') })
  await clickSpot(page, 'collection:tab:blind')
  await pass(page, 500)
  await page.screenshot({ path: path.join(OUT, 'collection-3-blind.png') })

  check('오류 없이 돕니다', errors.length === 0, errors.slice(0, 2).join(' / '))
  console.log(`\n  화면: ${OUT}`)

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
