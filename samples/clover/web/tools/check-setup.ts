// 덱과 스테이크를 고른 것이 실제로 그 판이 되는가.
//
// **게이트가 아니라 확인 도구입니다.** 여기서 보는 것은 넷입니다 — 판이 열리는가 · 뒷면
// 15종이 그려지는가 · 고른 것이 저장되는가 · 시작한 판이 고른 덱과 스테이크인가.
//
// 마지막 하나가 이 도구를 만든 이유입니다. **화면만 보면 갈리지 않습니다** — 덱은 뒷면과
// 시작 조건으로만 나타나고 스테이크는 요구 점수로만 나타나므로, 잘못된 덱으로 시작해도
// 그림은 그럴듯합니다. 그래서 `deck` 과 `stake` 를 화면이 알리게 두고 그것을 봅니다.
//
//     npx tsx tools/check-setup.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

import { clickPrimary, confirmYes, settle, type Peek, skipLogin, pass,
         pressRunPanel, pressTitle } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'check')

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5198 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, locale: 'ko-KR' })
  await skipLogin(page)

  const errors: string[] = []
  page.on('pageerror', one => errors.push(one.message))
  page.on('console', one => { if (one.type() === 'error') errors.push(one.text()) })

  let failed = 0
  const check = (name: string, ok: boolean, note = '') => {
    if (!ok) failed++
    console.log(`  ${ok ? '✓' : '✗'} ${name}${note ? '  —  ' + note : ''}`)
  }
  const peek = () => page.evaluate(() =>
    (window as unknown as { __clover?: Peek }).__clover)

  await page.goto('http://localhost:5198/', { waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  // 저장을 비우고 다시 엽니다. **처음 여는 사람의 상태**가 붉은 덱 · 흰색이어야 합니다.
  await page.evaluate(() => {
    localStorage.removeItem('clover.options')
    localStorage.setItem('clover.guide.seen', '1')
  })
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  await page.screenshot({ path: path.join(OUT, 'setup-0-title.png') })

  const before = await peek()
  check('처음은 붉은 덱 · 흰색입니다',
        before?.deck === 'red_deck' && before?.stake === 'White',
        `${before?.deck} · ${before?.stake}`)

  // **자리는 화면이 알립니다.** 여기 수로 적어 두면 배치가 바뀐 날에 빈 곳을 누르고,
  // 그 뒤의 검사가 전부 「저장되지 않았다」로 끝납니다.
  await pressTitle(page, 'start')
  await pass(page, 900)
  await page.screenshot({ path: path.join(OUT, 'setup-1-panel.png') })

  // 검은 덱(다섯째 칸)과 파란 스테이크(다섯째 칸).
  await pressRunPanel(page, 'deck:4')
  await pass(page, 400)
  await pressRunPanel(page, 'stake:4')
  await pass(page, 500)
  await page.screenshot({ path: path.join(OUT, 'setup-2-picked.png') })

  const saved = await page.evaluate(() => localStorage.getItem('clover.options'))
  const parsed = JSON.parse(saved ?? '{}') as { deck?: string; stake?: string }
  check('고른 것이 저장됩니다',
        parsed.deck === 'black_deck' && parsed.stake === 'Blue',
        `${parsed.deck} · ${parsed.stake}`)

  // 마지막 덱까지 눌러 봅니다. **뒷면 무늬 11종이 전부 그려지는 자리는 여기뿐입니다.**
  await pressRunPanel(page, 'deck:14')
  await pass(page, 400)
  await page.screenshot({ path: path.join(OUT, 'setup-3-last.png') })
  await pressRunPanel(page, 'deck:4')
  await pass(page, 300)

  await pressRunPanel(page, 'startNew')
  // **묻고 나서 시작합니다.** 새 판을 여는 것은 저장된 판을 덮는 일입니다.
  await pass(page, 500)
  await confirmYes(page)
  await pass(page, 2000)
  await page.screenshot({ path: path.join(OUT, 'setup-4-run.png') })

  const after = await peek()
  check('고른 덱과 스테이크로 판이 섭니다',
        after?.deck === 'black_deck' && after?.stake === 'Blue',
        `${after?.deck} · ${after?.stake}`)
  check('판으로 들어갔습니다', after?.scene === 'run', after?.scene)

  // **블라인드를 하나 골라야 핸드와 버리기에 수가 들어갑니다.** 판이 서기만 한 자리에서는
  // 둘이 0이고, 0으로는 시작 조건이 걸렸는지 갈리지 않습니다.
  await clickPrimary(page)
  await settle(page)
  await page.screenshot({ path: path.join(OUT, 'setup-5-round.png') })

  const round = await peek()
  // 검은 덱은 조커 슬롯 +1 · 핸드 -1 이고 파란 스테이크는 버리기 -1 입니다. 기본은 핸드 4 ·
  // 버리기 3 이므로 3 과 2 여야 합니다. **시작 조건이 실제로 걸렸는지는 이 수로만
  // 확인됩니다** — 뒷면과 요구 점수로는 갈리지 않습니다.
  check('검은 덱의 핸드 -1 이 걸렸습니다', round?.hands === 3, `핸드 ${round?.hands}`)
  check('파란 스테이크의 버리기 -1 이 걸렸습니다', round?.discards === 2,
        `버리기 ${round?.discards}`)

  check('오류 없이 돕니다', errors.length === 0, errors.slice(0, 2).join(' / '))
  console.log(`\n  화면: ${OUT}`)

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
