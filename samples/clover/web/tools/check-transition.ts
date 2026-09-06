// 씬이 갈릴 때 그 사이가 덮이는가.
//
// **눈으로는 갈아 끼운 자리를 볼 수 없습니다.** 반쯤 지워진 한 프레임에 새 화면이
// 비쳤는지는 그 프레임을 잡아야 알 수 있고, 그 프레임은 60분의 1초입니다. 그래서 40밀리초
// 마다 걸음과 지워진 정도와 씬을 함께 읽고, **씬이 바뀐 그 프레임의 지워진 정도**를 봅니다.
//
// 규격은 `doc/ui/transition.md` 입니다. 보는 것 넷.
//
// - 갈리는 자리마다 전환이 실제로 도는가
// - **씬이 바뀐 프레임에 아무것도 보이지 않았는가**
// - 도는 동안 누를 자리를 알리지 않는가
// - 앞 화면을 한 전환에 많아야 한 번 굽는가
//
//     npx tsx tools/check-transition.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  clickPrimary, clickSpot, closeGuide, confirmYes, crossed, pass, peek, pressRunPanel,
  pressTitle, settle, skipLogin, startNewRun,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5271
/** 얼마마다 읽는가. 한 프레임보다 조금 굵고, 가장 짧은 전환에서도 열 번 넘게 읽힙니다. */
const STEP_MS = 40

interface Frame {
  id: string
  stage: string
  cover: number
  scene: string
  /** 어느 판인가. **다시 시작은 씬이 그대로이므로 이것으로 봅니다.** */
  seed: string
  spots: number
  shots: number
}

const problems: string[] = []

/**
 * 한 번 누르고, 그 뒤 화면이 갈리는 동안을 프레임마다 읽습니다.
 *
 * **이름을 주면 세 컷을 찍습니다** — 덮이는 중 · 덮인 자리 · 걷히는 중입니다. 눈으로 볼
 * 것은 그 셋뿐이고, 나머지 프레임은 값으로만 봅니다.
 */
async function watch(page: Page, act: () => Promise<void>, steps: number,
                     name = ''): Promise<Frame[]> {
  const seen: Frame[] = []
  const took = new Set<string>()
  await act()
  for (let i = 0; i < steps; i++) {
    const now = await peek(page)
    const cross = now.transition
    const frame: Frame = {
      id: cross?.id ?? '',
      stage: cross?.stage ?? 'off',
      cover: cross?.cover ?? 0,
      scene: now.scene,
      seed: now.seed,
      spots: Object.keys(now.spots ?? {}).length,
      shots: cross?.shots ?? 0,
    }
    seen.push(frame)
    if (name) {
      const cut = frame.stage === 'hold' ? 'held'
        : frame.stage === 'out' && frame.cover > 0.35 && frame.cover < 0.8 ? 'out'
        : frame.stage === 'in' && frame.cover > 0.25 && frame.cover < 0.7 ? 'in'
        : ''
      if (cut && !took.has(cut)) {
        took.add(cut)
        await page.screenshot({ path: path.join(OUT, `cross-${name}-${cut}.png`) })
      }
    }
    // 다 끝났고 한 걸음이라도 돌았으면 그만 읽습니다.
    if (seen.length > 3 && seen[seen.length - 1].stage === 'off'
        && seen.some(one => one.stage !== 'off')) break
    await pass(page, STEP_MS)
  }
  return seen
}

/**
 * 이 자리의 전환이 규격대로 지나갔는가.
 *
 * **무엇이 바뀌었는지를 `mark` 가 정합니다.** 대개는 씬이지만 다시 시작은 판을 접고 곧바로
 * 펴므로 씬이 `run` 그대로입니다 — 그때 바뀌는 것은 어느 판인가입니다.
 */
function judge(where: string, want: string, frames: Frame[],
               mark: (one: Frame) => string = one => one.scene): void {
  const running = frames.filter(one => one.stage !== 'off')
  if (running.length === 0) {
    problems.push(`${where}: 전환이 돌지 않았습니다`)
    return
  }
  const id = running[0].id
  if (id !== want) problems.push(`${where}: ${want} 이어야 하는데 ${id} 입니다`)

  // **씬이 바뀐 프레임에 아무것도 보이지 않았는가.** 이 도구의 요점입니다.
  const first = mark(frames[0])
  const turned = frames.find(one => mark(one) !== first)
  if (!turned) problems.push(`${where}: 갈아 끼운 자리가 없습니다`)
  else if (turned.cover < 0.999) {
    problems.push(`${where}: 지워진 정도 ${turned.cover.toFixed(2)} 에서 씬이 바뀌었습니다`)
  }

  // 도는 동안에는 누를 자리를 알리지 않습니다.
  const loud = running.find(one => one.spots > 0)
  if (loud) problems.push(`${where}: 도는 동안 누를 자리를 ${loud.spots}개 알렸습니다`)

  // 앞 화면은 많아야 한 번 굽습니다.
  const shots = Math.max(...running.map(one => one.shots))
  if (shots > 1) problems.push(`${where}: 앞 화면을 ${shots}번 구웠습니다`)

  const stages = [...new Set(running.map(one => one.stage))].join('→')
  console.log(`${where}: ${id} · 걸음 ${stages} · ${running.length}칸 · 앞 화면 ${shots}장`)
}

/** 그 자리에서 집니다. 끝난 판이 설 때까지 기다립니다. */
async function lose(page: Page): Promise<void> {
  await page.evaluate(() => {
    (window as unknown as { __clover: { loseRound?(): void } }).__clover.loseRound?.()
  })
  for (let wait = 0; wait < 80; wait++) {
    if ((await peek(page)).gameOver === true) return
    await pass(page, 200)
  }
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('pageerror', error => problems.push(String(error)))
  page.on('console', m => {
    if (m.type() === 'error' && !m.text().includes('500')) problems.push(m.text())
  })
  await skipLogin(page)

  // 1. 로딩에서 첫 화면으로. **덮을 앞 화면이 없으므로 걷기만 합니다.**
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-CROSS&tick=manual`,
    { waitUntil: 'networkidle' })
  const boot = await watch(page, async () => { await pass(page, 60) }, 40, 'boot')
  const bootRun = boot.filter(one => one.stage !== 'off')
  if (bootRun.length === 0) problems.push('첫 화면: 전환이 돌지 않았습니다')
  else if (bootRun.some(one => one.stage === 'out')) {
    problems.push('첫 화면: 지울 앞 화면이 없는데 지웠습니다')
  } else console.log(`첫 화면: ${bootRun[0].id} · 되돌리기 ${bootRun.length}칸`)
  await crossed(page)

  // 2. 타이틀에서 판으로.
  //
  // **하네스의 `startNewRun` 을 쓰지 않습니다.** 그쪽은 판에 들어선 뒤까지 기다리므로,
  // 그것을 지나고 나면 볼 것이 이미 끝나 있습니다.
  const intoRun = await watch(page, async () => {
    await pressTitle(page, 'start')
    await pass(page, 500)
    await pressRunPanel(page, 'tab:new')
    await pass(page, 200)
    await pressRunPanel(page, 'startNew')
    await pass(page, 400)
    await confirmYes(page)
  }, 60, 'push')
  judge('타이틀 → 판', 'title_run', intoRun)
  await crossed(page)
  await pass(page, 400)
  await closeGuide(page)
  await settle(page)
  await pass(page, 300)

  // 3. 판에서 타이틀로. 문양 조리개입니다.
  const toTitle = await watch(page, async () => {
    await clickSpot(page, 'menu')
    await pass(page, 400)
    await clickSpot(page, 'menu:toTitle')
    await pass(page, 500)
    await confirmYes(page)
  }, 60, 'pull')
  judge('판 → 타이틀', 'run_title', toTitle)
  await crossed(page)
  await pass(page, 400)

  // 4. 진 판에서 나가는 길 둘. **다시 시작과 타이틀로가 서로 다른 전환입니다.**
  await startNewRun(page)
  await crossed(page)
  await pass(page, 400)
  await closeGuide(page)
  await clickPrimary(page)
  await settle(page)
  await lose(page)
  if ((await peek(page)).gameOver !== true) {
    problems.push('진 판이 서지 않아 나가는 길 둘을 보지 못했습니다')
  } else {
    const again = await watch(page, () => clickSpot(page, 'again'), 60, 'blocks')
    judge('다시 시작', 'run_restart', again, one => one.seed)
    await crossed(page)
    await pass(page, 400)
    await closeGuide(page)
    await clickPrimary(page)
    await settle(page)
    await lose(page)
    const home = await watch(page, () => clickSpot(page, 'home'), 60, 'burn')
    judge('진 판 → 타이틀', 'run_lost', home)
  }

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    for (const one of problems) console.error(`- ${one}`)
    return 1
  }
  console.log('씬이 갈리는 자리가 모두 아무것도 보이지 않는 프레임에 갈립니다')
  return 0
}

void main().then(code => process.exit(code))
