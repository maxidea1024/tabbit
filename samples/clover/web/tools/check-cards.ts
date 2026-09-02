// 카드 한 벌을 갈아입는 것이 화면에 나타나는가.
//
// **게이트가 아니라 확인 도구입니다.** 세트는 겉모습이므로 규칙에 닿지 않고, 그래서
// **갈아입었는데 화면이 그대로여도 어긋나는 값이 하나도 없습니다** — 눈으로 보아야 합니다.
//
// 세 벌을 차례로 세워 판을 하나씩 돌립니다. 정본은 그림 52장이고, 선화와 4색은 그림 없이
// 문양을 그린 것이며, 그 둘은 무늬의 색으로 갈립니다.
//
//     npx tsx tools/check-cards.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

import { at, clickPrimary, settle, type Peek } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'check')

/** 세 벌. `CardSet` 표의 순서와 같습니다. */
/**
 * 판에 세워 보는 벌들.
 *
 * **전부 세우지 않습니다.** 그리는 길은 셋뿐이므로(그림 · 그린 얼굴 · 4색) 그 셋과 결이
 * 다른 몇을 고릅니다 — 열일곱을 다 돌리면 3분이고, 고르는 화면의 그림 하나가 열일곱을
 * 함께 보여 줍니다.
 */
const SETS = ['classic', 'line', 'four_color', 'cats', 'dragons', 'blooms']

async function main(): Promise<number> {
  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: 5191 }, logLevel: 'error',
  })
  await server.listen()
  const browser = await chromium.launch()

  const errors: string[] = []
  let failed = 0
  const check = (name: string, ok: boolean, note = '') => {
    if (!ok) failed++
    console.log(`  ${ok ? '✓' : '✗'} ${name}${note ? '  —  ' + note : ''}`)
  }

  for (const set of SETS) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 },
                                         locale: 'ko-KR' })
    page.on('pageerror', one => errors.push(`${set}: ${one.message}`))
    page.on('console', one => {
      if (one.type() === 'error') errors.push(`${set}: ${one.text()}`)
    })

    // **옵션 판이 하는 것과 같은 저장입니다.** 눌러서 고르는 것은 언어 줄과 같은 기계이고,
    // 여기서 보는 것은 고른 값이 카드에 닿는가입니다.
    await page.addInitScript(`localStorage.setItem('clover.options', JSON.stringify({
      cardSet: ${JSON.stringify(set)}, deck: 'red_deck', stake: 'White' }));
      localStorage.setItem('clover.guide.seen', '1')`)
    await page.goto('http://localhost:5191/', { waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(2600)

    // 시작 · 블라인드 선택 · 손패까지. 카드가 화면에 서야 봅니다.
    await page.mouse.click(640, 488)
    await page.waitForTimeout(1600)
    await clickPrimary(page)
    await settle(page)
    await page.screenshot({ path: path.join(OUT, `cards-${set}.png`) })

    // 덱 보기도 함께 봅니다. **작은 카드가 따로 그려지므로** 손패만 맞고 여기가 틀릴 수
    // 있습니다 — 실제로 색을 고르는 자리가 넷이고 그중 둘이 이 화면입니다.
    // 덱 더미를 누르면 남은 카드가 무늬별로 섭니다. `game.ts` 의 `DECK_X`·`DECK_Y` 입니다.
    const pile = await at(page, 1280 - 62, 608)
    await page.mouse.click(pile.x, pile.y)
    await page.waitForTimeout(900)
    await page.screenshot({ path: path.join(OUT, `cards-${set}-deck.png`) })

    const seen = await page.evaluate(() =>
      (window as unknown as { __clover?: Peek }).__clover)
    check(`${set} — 판이 돕니다`, seen?.scene === 'run', seen?.scene)
    await page.close()
  }

  // 옵션의 카드 탭. **눌러서 고르는 자리가 있는지는 그림으로 봅니다.**
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 },
                                       locale: 'ko-KR' })
  page.on('pageerror', one => errors.push(`options: ${one.message}`))
  await page.addInitScript(`localStorage.setItem('clover.guide.seen', '1')`)
  await page.goto('http://localhost:5191/', { waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(2600)
  // 타이틀 오른쪽 아래의 톱니. `ui/title.ts` 의 자리입니다.
  await page.mouse.click(1280 - 30 - 29, 800 - 30 - 29)
  await page.waitForTimeout(900)
  await page.screenshot({ path: path.join(OUT, 'cards-options.png') })

  // 탭 여섯 가운데 넷째가 카드입니다 — 일반 · 소리 · 화면 · 카드 · 게임 · 시드.
  //
  // **판의 자리는 화면에게 물어 옵니다.** 판은 가운데에 놓이고 높이는 내용이 정하므로,
  // 여기서 다시 세면 판이 자란 날에 엉뚱한 곳을 누르고 아무 말도 하지 않습니다.
  const box = (await page.evaluate(() =>
    (window as unknown as { __clover?: Peek }).__clover))?.modalBox
  if (!box) {
    check('옵션 판이 떴습니다', false, '판의 자리를 알 수 없습니다')
  } else {
    const step = (box.width - 48) / 6
    await page.mouse.click(box.x + 24 + step * 3 + step / 2, box.y + 46 + 14 + 18)
    await page.waitForTimeout(700)
    await page.screenshot({ path: path.join(OUT, 'cards-tab.png') })
    check('옵션 판이 떴습니다', true, `${Math.round(box.width)} × ${Math.round(box.height)}`)

    // **바퀴로 굴러갑니다.** 세트가 열둘이면 3열 격자가 판보다 길어지고, 굴러가지 않으면
    // 마지막 줄은 어디에도 없습니다.
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
    for (let i = 0; i < 6; i++) await page.mouse.wheel(0, 120)
    await page.waitForTimeout(500)
    await page.screenshot({ path: path.join(OUT, 'cards-tab-scrolled.png') })
  }
  await page.close()

  check('오류 없이 돕니다', errors.length === 0, errors.slice(0, 2).join(' / '))
  console.log(`\n  화면: ${OUT}`)

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
