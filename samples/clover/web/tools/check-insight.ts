// 인사이트 갈래가 실제로 서는가.
//
// **줄의 값은 `test/insight.test.ts` 가 봅니다.** 여기서 보는 것은 그 줄이 화면에 서는지와,
// 국면과 고름을 바꿀 때 줄이 따라 바뀌는지입니다 — 코어가 옳은 답을 내면서 화면은 낡은 답을
// 그리고 있을 수 있고, 그것은 값으로 판정되지 않습니다.
//
//     npx tsx tools/check-insight.ts
//
// **자리는 화면이 알린 것만 누릅니다.** 갈래 단추의 좌표를 적어 두면 판의 폭이 바뀔 때
// 빈자리를 눌러 놓고 통과합니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  at, chooseFive, clickSpot, closeGuide, pass, peek, pickCards, pressTitle, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5271

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail === '' ? '' : `  — ${detail}`}`)
}

/**
 * 봐도 되는 콘솔 오류.
 *
 * **계정 서버가 없습니다.** 이 도구는 개발 서버만 띄우므로 타이틀이 `/auth/providers` 를
 * 조회할 때 500 이 돌아옵니다.
 */
function noise(line: string): boolean {
  return line.includes('500 (Internal Server Error)') || line.includes('/auth/')
}

/**
 * 판이 다 서기를 기다립니다.
 *
 * **서는 중의 자리는 마지막 자리가 아닙니다.** 판은 아래에서 밀려 올라와 한 번 넘치고,
 * 그 동안 화면이 알리는 단추의 자리는 프레임마다 다릅니다 — 그 자리를 누르면 판 옆의
 * 빈 곳을 누릅니다. `modalBox` 는 다 선 판에만 값이 있으므로 그것을 봅니다.
 */
async function settled(page: Page): Promise<void> {
  for (let wait = 0; wait < 40; wait++) {
    if ((await peek(page)).modalBox !== undefined) return
    await pass(page, 100)
  }
  throw new Error('판이 다 서지 않았습니다')
}

/**
 * 인사이트 갈래를 엽니다. 이미 그 판이 떠 있으면 갈래만 바꿉니다.
 *
 * **떠 있는지는 갈래 단추의 자리로 봅니다.** 「판이 떠 있는가」로 보면 다른 판이 떠
 * 있을 때도 참이고, 그때 갈래 단추를 누르면 덮개를 눌러 그 판을 닫습니다.
 */
async function openInsight(page: Page): Promise<void> {
  if ((await peek(page)).spots['runInfoTab:insight'] === undefined) {
    await clickSpot(page, 'runInfo')
    await settled(page)
  }
  await clickSpot(page, 'runInfoTab:insight')
  await settled(page)
}

/**
 * 판을 닫습니다.
 *
 * **판 밖을 누르기 전에 닫아야 합니다.** 덮개가 판 밖의 누름을 받아 맨 위 판을 닫으므로,
 * 열어 둔 채로 단추를 누르면 그 누름은 단추에 닿지 않고 판만 닫힙니다.
 */
async function shut(page: Page): Promise<void> {
  if ((await peek(page)).modalUp !== true) return
  await page.keyboard.press('Escape')
  await pass(page, 700)
}

/** 지금 서 있는 줄들의 열쇠. 판이 닫혀 있으면 비어 있습니다. */
async function shownKeys(page: Page): Promise<string[]> {
  return (await peek(page)).insight?.keys ?? []
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error' && !noise(message.text())) problems.push(message.text())
  })
  page.on('pageerror', error => { if (!noise(String(error))) problems.push(String(error)) })

  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-SEE&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // 블라인드를 고르는 자리. **여기에도 줄이 서야 합니다** — 건너뛸지를 정하는 자리이고,
  // 그것이 이 기능이 답하려는 물음의 하나입니다.
  await pressTitle(page, 'start')
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 400)

  await openInsight(page)
  const atSelect = await shownKeys(page)
  check('블라인드를 고르는 자리에 줄이 섭니다', atSelect.length > 0, `${atSelect.length}줄`)
  check('그 자리에는 고른 카드 갈래가 없습니다',
        !atSelect.some(key => key.startsWith('pick.') || key.startsWith('option.')),
        atSelect.filter(key => key.startsWith('pick.')).join(' · '))
  await page.screenshot({ path: path.join(OUT, 'insight-blind-select.png') })

  // 판으로 들어갑니다.
  await shut(page)
  await clickSpot(page, 'pick')
  await pass(page, 1400)

  await openInsight(page)
  const inRound = await shownKeys(page)
  check('라운드에서 줄이 섭니다', inRound.length > 0, `${inRound.length}줄`)
  check('라운드 갈래가 나옵니다', inRound.some(key => key.startsWith('round.')),
        inRound.join(' · '))
  check('국면이 바뀌면 줄도 바뀝니다', inRound.join() !== atSelect.join())
  check('고르기 전에는 고른 카드 갈래가 없습니다',
        !inRound.some(key => key.startsWith('pick.')), inRound.join(' · '))

  // 카드를 고릅니다. **고름은 액션이 아니므로** 화면이 그것을 넘겨야 줄이 바뀝니다 —
  // 넘기지 않으면 여기서 아무 일도 일어나지 않습니다.
  const before = inRound.join()
  await shut(page)
  const hand = (await peek(page)).hand
  await pickCards(page, chooseFive(hand))
  await pass(page, 700)
  await openInsight(page)

  const picked = await shownKeys(page)
  check('카드를 고르면 고른 카드 갈래가 나옵니다',
        picked.some(key => key.startsWith('pick.')), picked.join(' · '))
  check('고르면 줄이 바뀝니다', picked.join() !== before)
  check('예상 점수가 적힙니다', picked.includes('pick.score'))
  await page.screenshot({ path: path.join(OUT, 'insight-round.png') })

  // **판 안쪽을 크게 오려 한 장 더 굽습니다.** 1배로는 줄의 등급 띠와 접힌 글이 보이지
  // 않습니다.
  await page.screenshot({
    path: path.join(OUT, 'insight-near.png'),
    clip: { x: 330, y: 100, width: 620, height: 560 },
  })

  // **굴려 봅니다.** 상한 14줄에 갈래 머리 8개가 함께 서므로 몸통이 넘치고, 넘친 것이
  // 잘린 것으로 보이지 않으면 없는 것으로 보입니다.
  const box = (await peek(page)).modalBox
  if (box !== undefined) {
    const middle = await at(page, box.x + box.width / 2, box.y + box.height / 2)
    await page.mouse.move(middle.x, middle.y)
    await page.mouse.wheel(0, 400)
    await pass(page, 400)
    await page.screenshot({
      path: path.join(OUT, 'insight-scrolled.png'),
      clip: { x: 330, y: 100, width: 620, height: 560 },
    })
    check('굴려도 줄이 그대로입니다', (await shownKeys(page)).length === picked.length)
  }

  check('상한을 넘지 않습니다', picked.length <= 14, `${picked.length}줄`)
  check('갈래마다 3줄을 넘지 않습니다', overflowing(picked).length === 0,
        overflowing(picked).join(' · '))

  // 갈래를 오갔다 돌아옵니다. **굴림통을 살려 두는 길에 판이 비어 남을 수 있습니다.**
  await clickSpot(page, 'runInfoTab:hands')
  await pass(page, 400)
  check('다른 갈래로 가면 인사이트가 아닙니다', (await shownKeys(page)).length === 0)
  await clickSpot(page, 'runInfoTab:insight')
  await pass(page, 400)
  check('돌아오면 줄이 그대로 섭니다', (await shownKeys(page)).length === picked.length,
        `${(await shownKeys(page)).length}줄`)

  // 판을 닫았다 다시 엽니다. **덮개를 눌러 닫습니다** — 판 밖의 누름은 그것이 받습니다.
  await shut(page)
  check('닫으면 알리지 않습니다', (await shownKeys(page)).length === 0)
  await openInsight(page)
  check('다시 열면 그 갈래입니다', (await shownKeys(page)).length > 0,
        `${(await shownKeys(page)).length}줄`)

  const blank = (await peek(page)).blankTaps ?? 0
  check('빈자리를 누르지 않았습니다', blank === 0, `${blank}번`)
  check('페이지 오류가 없습니다', problems.length === 0, problems.slice(0, 3).join(' | '))

  await page.close()
  await browser.close()
  await server.close()

  console.log('')
  console.log(failed === 0 ? '오류 없음' : `실패 ${failed}건`)
  return failed === 0 ? 0 : 1
}

/** 3줄을 넘긴 갈래들. */
function overflowing(keys: readonly string[]): string[] {
  const counts = new Map<string, number>()
  for (const key of keys) {
    const group = key.split('.')[0]
    counts.set(group, (counts.get(group) ?? 0) + 1)
  }
  return [...counts.entries()].filter(([, n]) => n > 3).map(([group, n]) => `${group} ${n}줄`)
}

main().then(code => process.exit(code), error => {
  console.error(error)
  process.exit(1)
})
