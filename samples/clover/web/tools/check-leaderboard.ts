// 리더보드가 화면에서 도는가.
//
//     npx tsx tools/check-leaderboard.ts
//
// **서버가 없어도 통과해야 합니다.** 로그아웃 상태의 게임이 지금과 한 줄도 다르지 않은
// 것이 이 확인의 첫 항목입니다 — 서버가 떠 있으면 로그인 판까지 봅니다.
//
// 스크린샷을 찍지 않습니다. 화면을 고치는 동안 그림을 다시 굽는 것은 확인이 아니라
// 손이 가는 일입니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5191

interface Peek {
  scene: string
  signedIn: boolean
  modalUp: boolean
  netBusy: boolean
  ranked: boolean
  seed: string
}

/** 로그인 화면의 「계정 없이 시작하기」. **자리가 고정입니다.** */
const SINGLE = { x: 640, y: 800 - 214 + 26 }

/**
 * 타이틀의 자리들.
 *
 * **아래 바 하나에 전부 들어 있습니다.** 값이 `ui/title.ts` 의 상수에서 그대로 나옵니다 —
 * 바가 216 이고 안쪽 여백이 26, 윗줄이 34, 틈이 10, 아랫줄이 62 입니다.
 */
const DOCK_Y = 800 - 216
const UPPER_Y = DOCK_Y + 26
const ROW_Y = UPPER_Y + 34 + 10
const LEFT = Math.round((1280 - (196 + 132 * 3 + 10 * 3)) / 2)
const TITLE = {
  start: { x: LEFT + 98, y: ROW_Y + 31 },
  leaderboard: { x: LEFT + 196 + 10 + (132 + 10) * 2 + 66, y: ROW_Y + 31 },
  ranked: { x: LEFT + 196 + 10 + (132 + 10) * 2 + 66, y: UPPER_Y + 17 },
  account: { x: 26 + 100, y: UPPER_Y + 53 },
}

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail === '' ? '' : `  — ${detail}`}`)
}

/**
 * 통신이 멎을 때까지 기다렸다 누릅니다.
 *
 * **도는 동안은 입력이 막힙니다.** 그것이 설계이므로, 도구도 사람과 같이 기다립니다 —
 * 기다리지 않고 누르면 그 누름이 막이에 먹히고 아무 일도 일어나지 않습니다.
 */
async function press(page: Page, x: number, y: number): Promise<void> {
  for (let wait = 0; wait < 40; wait++) {
    if (!(await peek(page)).netBusy) break
    await page.waitForTimeout(200)
  }
  await page.mouse.click(x, y)
}

async function peek(page: Page): Promise<Peek> {
  for (let wait = 0; wait < 30; wait++) {
    const seen = await page.evaluate('window.__clover') as Peek | undefined
    if (seen) return seen
    await page.waitForTimeout(200)
  }
  throw new Error('화면이 상태를 알리지 않습니다')
}

async function main(): Promise<number> {
  const server = await createServer({
    root: path.resolve(HERE, '..'),
    server: { port: PORT },
    logLevel: 'error',
  })
  await server.listen()
  const browser = await chromium.launch()
  // **창을 기준 해상도에 맞춥니다.** 그래야 화면의 자리와 누르는 자리가 1:1 입니다 —
  // 작으면 통째로 줄어들고, 그러면 좌표를 그 배율로 다시 세야 합니다.
  const page = await browser.newPage({
    locale: 'ko-KR',
    viewport: { width: 1280, height: 800 },
  })

  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))

  // 세션을 넣어 두면 로그인한 화면까지 봅니다.
  //   npx tsx ../server/tools/mint-session.ts --handle demo
  const given = process.env.CLOVER_SESSION
  if (given) await page.addInitScript(`localStorage.setItem('clover.session', ${
    JSON.stringify(given)})`)

  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'domcontentloaded' })
  const first = await peek(page)

  // **처음 여는 사람에게는 로그인 화면입니다.** 계정을 만들지 말지를 그 자리에서
  // 정하고, 그다음부터는 뜨지 않습니다.
  //
  // 세션을 넣어 두었으면 이미 정해진 사람이므로 이 대목을 지납니다.
  if (!given) {
    check('처음에는 로그인 화면입니다', first.scene === 'login', first.scene)

    await press(page, SINGLE.x, SINGLE.y)
    await page.waitForTimeout(900)
    const chosen = await peek(page)
    check('싱글플레이를 고르면 타이틀입니다', chosen.scene === 'title', chosen.scene)
    check('판이 떠 있지 않습니다', !chosen.modalUp)

    // 다시 열면 묻지 않습니다.
    await page.reload({ waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(1_600)
    const again = await peek(page)
    check('다시 열면 곧바로 타이틀입니다', again.scene === 'title', again.scene)
  } else {
    check('세션이 있으면 곧바로 타이틀입니다', first.scene === 'title', first.scene)
  }

  const up = await fetch(`http://localhost:${PORT}/api/health`)
    .then(response => response.ok).catch(() => false)
  console.log(up ? '  · 서버가 떠 있습니다' : '  · 서버가 없습니다')

  // **로그아웃 상태의 타이틀입니다.** 서버가 없으면 언제나 여기입니다.
  if (!first.signedIn) {
    check('로그아웃이면 랭크 런이 아닙니다', !first.ranked)
  }

  // 리더보드 단추. 줄의 오른쪽 끝입니다.
  //
  // **계정이 없어도 열립니다.** 오르는 데 계정이 필요한 것이지 보는 데 필요한 것이
  // 아닙니다 — 무엇을 위해 만드는지는 그 표를 봐야 압니다.
  await press(page, TITLE.leaderboard.x, TITLE.leaderboard.y)
  await page.waitForTimeout(1_600)
  check('리더보드가 열립니다', (await peek(page)).modalUp)

  await page.keyboard.press('Escape')
  await page.waitForTimeout(600)
  check('Esc 로 닫힙니다', !(await peek(page)).modalUp)

  // 그냥 시작. **로그아웃이든 아니든 이 길은 지금과 같아야 합니다.**
  await press(page, TITLE.start.x, TITLE.start.y)
  await page.waitForTimeout(1_400)
  const inRun = await peek(page)
  check('시작이 그대로 판을 엽니다', inRun.scene === 'run', inRun.scene)
  check('그냥 시작은 랭크 런이 아닙니다', !inRun.ranked)

  // **랭크 런.** 서버가 떠 있고 세션이 있을 때만 봅니다 — `CLOVER_SESSION` 에 넣습니다.
  if (up && process.env.CLOVER_SESSION) {
    await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'domcontentloaded' })
    await peek(page)
    await page.waitForTimeout(1_800)
    const signedIn = await peek(page)
    check('세션이 있으면 로그인 상태입니다', signedIn.signedIn)
    check('로그인되어 있으면 로그인 화면을 지나지 않습니다', signedIn.scene === 'title',
          signedIn.scene)

    // 랭크 단추는 시작 위, 설정 단추 오른쪽입니다.
    await press(page, TITLE.ranked.x, TITLE.ranked.y)
    await page.waitForTimeout(2_600)
    const ranked = await peek(page)
    check('랭크를 누르면 랭크 런이 시작됩니다', ranked.scene === 'run' && ranked.ranked,
          `${ranked.scene} · ${ranked.seed}`)
    check('시드는 서버가 준 것입니다', /^CLOVER-[A-Z2-9]{8}$/.test(ranked.seed), ranked.seed)
  }

  check('오류가 없습니다', errors.length === 0, errors.slice(0, 2).join(' | '))

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
