// 환희의 겹이 오르고 터지고 물러나는가.
//
// 칩 × 배수가 문턱을 넘으면 배경의 프랙탈 위로 기를 모으는 겹이 오르고, 정산하는 순간에
// 터진 뒤 사라집니다.
//
// **눈으로는 세 가지를 가릴 수 없습니다.**
//
// - 겹이 **서서히** 오르는가. 한 프레임에 다 서면 배경이 갈아치워진 것으로 보이는데, 그
//   한 프레임은 눈으로 잡을 수 없습니다
// - 터진 뒤 **정말로 물러나는가.** 남아 있으면 상점과 타이틀에서도 기를 모으고 있게 되고,
//   그 화면을 다시 볼 때까지 아무도 모릅니다
// - 문턱 **아래에서는 서지 않는가.** 문턱을 코드에 적어 두고 비교를 한 자리 틀리면 모든
//   판에서 겹이 오르는데, 그러면 그것이 특별한 순간이 아니게 됩니다
//
// 문턱은 지금 확인용 값입니다 — `render/euphoria.ts` 의 `TIERS` 에 있습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { closeGuide, openRun, pass, playHand, skipLogin, swept } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5269
/** 한 프레임. `game.ts` 의 `STEP_MS` 와 같습니다. */
const STEP_MS = 1000 / 60
/** 문턱. `render/euphoria.ts` 의 첫 줄입니다. */
const OVER = 400_000
const UNDER = 399_999

interface Look {
  phase: string
  visual?: string
  charge: number
  fade: number
  /** 지금 그려지는 것이 영상인가. 거짓이면 셰이더입니다. */
  reel: boolean
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-EUPHORIA&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await closeGuide(page)

  const bad: string[] = []

  // 판에 들어선 자리에서는 아무 겹도 없습니다.
  const idle = await look(page)
  console.log(`판에 들어선 자리 ${idle.phase}`)
  if (idle.phase !== 'off') bad.push(`판에 들어선 자리에서 이미 겹이 있습니다 — ${idle.phase}`)

  // **문턱 아래.** 1 이 모자란 곱으로는 아무것도 하지 않습니다.
  await force(page, UNDER)
  await pass(page, 600)
  const below = await look(page)
  console.log(`곱 ${UNDER.toLocaleString('en-US')} ${below.phase}`)
  if (below.phase !== 'off') {
    bad.push(`문턱 아래인 ${UNDER} 에서 겹이 올랐습니다 — ${below.phase}`)
  }

  // **문턱 위.** 프레임마다 짙기를 읽어 곧게 오르는지 봅니다.
  await force(page, OVER)
  const rise: number[] = []
  for (let i = 0; i < 40; i++) {
    rise.push((await look(page)).fade)
    await step(page)
  }
  console.log(`짙기 ${rise.slice(0, 12).map(one => one.toFixed(2)).join(' → ')} …`)
  if (rise[0] > 0.2) bad.push(`겹이 서는 첫 프레임에 이미 짙습니다 — ${rise[0]}`)
  for (let i = 1; i < rise.length; i++) {
    if (rise[i] < rise[i - 1] - 0.001) {
      bad.push(`${i}번째 프레임에서 짙기가 되돌아갑니다 — ${rise[i - 1]} → ${rise[i]}`)
      break
    }
  }

  await pass(page, 700)
  const charged = await look(page)
  console.log(`모으는 중 ${charged.phase} · 연출 ${charged.visual ?? '없음'}`
    + ` · ${charged.reel ? '영상' : '셰이더'}`
    + ` · 모은 정도 ${charged.charge} · 짙기 ${charged.fade}`)
  if (charged.phase !== 'charge') bad.push(`모으는 중이어야 합니다 — ${charged.phase}`)
  if (charged.fade < 0.9) bad.push(`1.3초가 지났는데 겹이 다 서지 않았습니다 — ${charged.fade}`)
  if (charged.visual !== 'ki_gather') bad.push(`연출의 이름이 다릅니다 — ${charged.visual}`)

  // **정산.** 터지고, 잠깐 남았다가 물러납니다.
  await release(page)
  const burst = await look(page)
  console.log(`정산한 순간 ${burst.phase}`)
  if (burst.phase !== 'burst') bad.push(`정산한 순간에 터지지 않았습니다 — ${burst.phase}`)

  let gone = -1
  for (let i = 0; i < 400; i++) {
    if ((await look(page)).phase === 'off') {
      gone = Math.round(i * STEP_MS)
      break
    }
    await step(page)
  }
  console.log(`물러나기까지 ${gone < 0 ? '끝나지 않음' : gone + 'ms'}`)
  if (gone < 0) bad.push('터진 뒤에도 겹이 물러나지 않습니다')
  else if (gone < 1200) bad.push(`터진 것이 너무 빨리 사라집니다 — ${gone}ms`)
  else if (gone > 4000) bad.push(`터진 것이 너무 오래 남습니다 — ${gone}ms`)

  // **다음 판이 시작되면 남지 않습니다.** 카드를 내는 박자가 지난 판의 겹을 물러나게
  // 합니다 — 남아 있으면 다음 판의 카드가 그 배경 위로 올라옵니다.
  await force(page, OVER)
  await pass(page, 500)
  const before = await look(page)
  if (before.phase !== 'charge') bad.push(`다시 모으지 못했습니다 — ${before.phase}`)
  await playHand(page)
  await pass(page, 1600)
  const after = await look(page)
  console.log(`다음 판을 낸 뒤 ${after.phase}`)
  if (after.phase !== 'off') bad.push(`다음 판이 시작됐는데 겹이 남아 있습니다 — ${after.phase}`)
  await swept(page)

  await browser.close()
  await server.close()
  for (const one of errors.slice(0, 5)) console.error('오류: ' + one)
  const ok = bad.length === 0 && errors.length === 0
  console.log(ok
    ? '\n환희의 겹이 문턱 위에서만 오르고, 터진 뒤 물러납니다'
    : '\n' + bad.concat(errors.slice(0, 5)).join('\n'))
  return ok ? 0 : 1
}

async function look(page: Page): Promise<Look> {
  return page.evaluate(() => {
    const hook = (window as unknown as { __clover: { euphoria?(): unknown } }).__clover
    return (hook.euphoria?.() ?? { phase: '없음', charge: 0, fade: 0, reel: false }) as never
  })
}

async function force(page: Page, product: number): Promise<void> {
  await page.evaluate(value => {
    const hook = (window as unknown as {
      __clover: { forceEuphoria?(product: number, release?: boolean): void }
    }).__clover
    hook.forceEuphoria?.(value)
  }, product)
}

async function release(page: Page): Promise<void> {
  await page.evaluate(() => {
    const hook = (window as unknown as {
      __clover: { forceEuphoria?(product: number, release?: boolean): void }
    }).__clover
    hook.forceEuphoria?.(0, true)
  })
}

/** 한 프레임만 돌립니다. */
async function step(page: Page): Promise<void> {
  await page.evaluate(ms => {
    const hook = (window as unknown as {
      __clover: { advance?(ms: number): Promise<void> }
    }).__clover
    return hook.advance?.(ms)
  }, STEP_MS)
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
