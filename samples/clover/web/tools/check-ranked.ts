// 랭크 런 한 판이 실제로 순위에 오르는가.
//
//     npx tsx tools/check-ranked.ts
//
// **여기가 L3 의 판정입니다.** 브라우저에서 랭크 런을 시작해 끝까지 두고, 끝난 판에 순위가
// 적히고 서버에 그 제출이 남는지를 봅니다 — 그 사이의 어느 한 곳이라도 끊기면 실패합니다.
//
// 서버가 떠 있어야 하고 세션이 있어야 합니다.
//
//     docker compose up -d           # samples/clover/server
//     npx tsx tools/check-ranked.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { execFileSync } from 'child_process'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  chooseFive, clickPrimary, peek, playHand, settle, spare, discardHand, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5195
const API = 'http://localhost:8787'

/**
 * 랭크 단추의 자리.
 *
 * **값이 `ui/title.ts` 의 상수에서 나옵니다.** 바가 216 이고 안쪽 여백이 26, 윗줄이 34,
 * 틈이 10, 아랫줄이 62 입니다.
 */
const DOCK_Y = 800 - 216
const UPPER_Y = DOCK_Y + 26
const LEFT = Math.round((1280 - (196 + 132 * 3 + 10 * 3)) / 2)
const RANKED = { x: LEFT + 196 + 10 + (132 + 10) * 2 + 66, y: UPPER_Y + 17 }

/** 한 판이 끝날 때까지의 상한. 지지 않으면 여기서 멈춥니다. */
const LIMIT = 240

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail === '' ? '' : `  — ${detail}`}`)
}

interface Peeked {
  scene: string; phase: string; ranked: boolean; seed: string
  netBusy: boolean; modalUp: boolean; hands: number; discards: number
  gameOver: boolean
}

/** 리더보드가 더한 칸들은 `harness` 의 `Peek` 에 없으므로 여기서 넓힙니다. */
async function look(page: Page): Promise<Peeked> {
  return await peek(page) as unknown as Peeked
}

/** 통신이 멎을 때까지 기다렸다 누릅니다. 도는 동안은 입력이 막힙니다. */
async function press(page: Page, x: number, y: number): Promise<void> {
  for (let wait = 0; wait < 40; wait++) {
    if (!(await look(page)).netBusy) break
    await page.waitForTimeout(200)
  }
  await page.mouse.click(x, y)
}

async function main(): Promise<number> {
  const minted = execFileSync('npx',
    ['tsx', 'tools/mint-session.ts', '--handle', 'ranked_probe'],
    { cwd: path.resolve(HERE, '../../server'), encoding: 'utf8', shell: true })
  const session = (JSON.parse(minted) as {
    accountId: number; session: { access: string; refresh: string }
  })

  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: PORT }, logLevel: 'error',
  })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ locale: 'ko-KR', viewport: { width: 1280, height: 800 } })
  await skipLogin(page)

  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  page.on('console', message => {
    // WebGL 의 성능 알림은 우리 것이 아닙니다.
    if (message.type() === 'error' && !message.text().includes('WebGL')) {
      errors.push(`${message.type()}: ${message.text()}`)
    }
  })
  await page.addInitScript(
    `localStorage.setItem('clover.session', ${JSON.stringify(JSON.stringify(session.session))})`)
  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'domcontentloaded' })
  await look(page)
  await page.waitForTimeout(1_600)

  await press(page, RANKED.x, RANKED.y)
  await page.waitForTimeout(2_400)

  const started = await look(page)
  check('랭크 런이 시작됩니다', started.scene === 'run' && started.ranked,
        `${started.scene} · ${started.seed}`)
  if (!started.ranked) {
    await browser.close()
    await server.close()
    console.log('\n시작하지 못했으므로 나머지는 보지 않습니다')
    return 1
  }

  // 끝까지 둡니다. **잘 두려고 하지 않습니다** — 끝나는 것이 목적입니다.
  let steps = 0
  while (steps < LIMIT) {
    const now = await look(page)
    if (now.phase === 'won' || now.phase === 'lost') break
    steps++

    if (now.modalUp) {
      await page.keyboard.press('Escape')
      await page.waitForTimeout(200)
      continue
    }

    const hand = (await peek(page)).hand

    if (hand.length === 0) {
      await clickPrimary(page)
      await settle(page)
      continue
    }

    const picks = chooseFive(hand)
    if (now.discards > 0 && now.hands > 1 && steps % 3 === 0) {
      await discardHand(page, spare(hand, picks))
    } else {
      await playHand(page, picks)
    }
    await settle(page)
  }

  const ended = await look(page)
  check('런이 끝났습니다', ended.phase === 'won' || ended.phase === 'lost',
        `${ended.phase} · ${steps}수`)

  // **끝난 판이 뜨기를 기다립니다.** 카드가 다 걷힌 뒤에 서고, 제출은 그때 나갑니다.
  let shown = false
  for (let wait = 0; wait < 60; wait++) {
    if ((await look(page)).gameOver) { shown = true; break }
    await page.waitForTimeout(250)
  }
  check('끝난 판이 뜹니다', shown)

  // 제출이 오갈 시간을 줍니다.
  for (let wait = 0; wait < 40; wait++) {
    if (!(await look(page)).netBusy) break
    await page.waitForTimeout(250)
  }
  await page.waitForTimeout(1_200)

  // 서버에 그 판이 남았는가.
  const listed = await fetch(`${API}/runs/0`, {
    headers: { authorization: `Bearer ${session.session.access}` },
  }).then(() => true).catch(() => false)
  check('서버에 닿습니다', listed)

  const mine = await fetch(`${API}/me`, {
    headers: { authorization: `Bearer ${session.session.access}` },
  }).then(response => response.json()) as { ranks: { boardId: string }[] }
  check('순위표에 올랐습니다', mine.ranks.length > 0,
        mine.ranks.map(one => one.boardId).slice(0, 4).join(' · '))

  check('오류가 없습니다', errors.length === 0, errors.slice(0, 2).join(' | '))

  // 끝난 판을 한 장 남깁니다. **게이트가 아니라 눈으로 보는 자리입니다.**
  const shot = process.env.SHOT_DIR
  if (shot) await page.screenshot({ path: `${shot}/ranked-end.png` })

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
