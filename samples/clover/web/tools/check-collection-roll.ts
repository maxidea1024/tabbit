// 도감을 끝까지 굴리는 동안 화면이 멀쩡한가.
//
// **`check-collection` 은 수를 세고, 이 도구는 굴리는 동안을 봅니다.** 조커 500종을 전부
// 만나 본 저장으로 열어 조커 탭을 끝까지 굴리면서 셋을 확인합니다.
//
// |보는 것|왜|
// |--|--|
// |버려진 그림을 가리키는 스프라이트가 없습니다|그림의 상한이 넘치면 오래된 것부터 버립니다. 버려진 그림을 가리킨 채로 그리면 그 프레임의 그리기 전체가 예외로 끝나고, **화면 전체가 까맣게 됩니다**|
// |프레임이 던지지 않습니다|위의 것이 화면에 드러나는 자리입니다. `pageerror` 와 `__clover.errors` 둘 다 봅니다|
// |줄 하나를 지나며 짓는 칸이 한 줄 남짓입니다|줄 경계마다 60칸을 버리고 60칸을 다시 지으면 그 프레임이 눌려 굴림이 덜덜거립니다|
// |켤 때는 아무것도 짓지 않습니다|판을 한 번도 열지 않아도 조커 60칸을 짓고 그림 60장을 읽고 있었습니다. 그 60장이 상한을 거의 다 채웁니다|
// |스켈레톤에서 그림으로 겹쳐 흐릅니다|그 자리에서 갈아 끼우면 줄마다 열 칸이 저마다 다른 순간에 바뀌어 격자가 깜박입니다|
//
//     npx tsx tools/check-collection-roll.ts

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

import { clickSpot, pass, peek, pressTitle, skipLogin, spot } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '..', '..', 'design-data', 'out', 'check')
const PORT = 5219

/** 굴리는 걸음의 수. 한 걸음이 바퀴 한 칸(52픽셀)입니다. */
const STEPS = 120
/** 격자의 한 줄 높이. `ui/collection.ts` 의 `CELL_Y` 와 같습니다. */
const CELL_Y = 152
/** 한 줄이 칸 몇 개인가. `ui/collection.ts` 의 `COLUMNS` 와 같습니다. */
const COLUMNS = 10

interface Census {
  tab: string
  cells: number
  found: number
  offset: number
  built: number
  /** 스켈레톤에서 그림으로 겹쳐 흐르는 중인 칸. */
  fading: number
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

  const pageErrors: string[] = []
  page.on('pageerror', one => pageErrors.push(one.message))
  // **개발 서버의 로그인 대리 오류는 뺍니다.** 인증 서버가 없으면 `/auth/` 가 500 을 내고,
  // 그것은 이 도구가 보는 것이 아닙니다.
  page.on('console', one => {
    if (one.type() !== 'error' || one.location().url.includes('/auth/')) return
    pageErrors.push(one.text())
  })

  let failed = 0
  const check = (name: string, ok: boolean, note = ''): void => {
    if (!ok) failed++
    console.log(`  ${ok ? '✓' : '✗'} ${name}${note ? '  —  ' + note : ''}`)
  }

  // **그림 요청을 셉니다.** 판을 열기 전에 나간 것이 있으면 켤 때 격자를 지은 것입니다.
  let beforeOpen = 0
  let panelUp = false
  page.on('request', one => {
    if (one.url().includes('/art/joker/') && !panelUp) beforeOpen++
  })

  await page.goto(`http://localhost:${PORT}/?seed=roll`, { waitUntil: 'domcontentloaded' })
  await pass(page, 2600)

  // **전부 만나 본 저장입니다.** 만나지 않은 칸은 그림을 부르지 않으므로, 새 저장으로는
  // 그림이 열 몇 장뿐이고 상한에 닿을 길이 없습니다.
  await page.evaluate(async () => {
    const list = await (await fetch('./art/index.json')).json() as string[]
    const jokers = list.filter(one => one.startsWith('joker/')).map(one => one.slice('joker/'.length))
    localStorage.setItem('clover.collection', JSON.stringify({ joker: jokers }))
    localStorage.setItem('clover.guide.seen', '1')
  })
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)

  // 켠 채로 잠깐 세워 둡니다. 켤 때 짓는다면 이 사이에 요청이 나갑니다.
  await page.waitForTimeout(1500)
  check('켤 때는 조커 그림을 읽지 않습니다', beforeOpen === 0, `${beforeOpen}건`)

  // **여는 순간부터 스켈레톤이 겹쳐 흐릅니다.** 그림을 일부러 늦게 주어 그 자리를 만듭니다 —
  // 로컬에서는 60장이 100ms 안에 닿아 겹치는 그 순간을 잡을 수 없습니다.
  //
  // **길을 걷지 않고 늦추기만 끕니다.** 걷는 자리에 아직 처리하지 않은 요청이 남아 있으면
  // 그것이 예외가 되고, 도구는 정작 보려던 것을 보기 전에 멈춥니다.
  let slow = true
  await page.route('**/art/joker/**', async route => {
    if (slow) await new Promise(done => setTimeout(done, 700))
    try {
      await route.continue()
    } catch {
      // 판을 떠난 뒤에 닿은 요청입니다. 답을 받을 쪽이 없습니다.
    }
  })

  panelUp = true
  await pressTitle(page, 'collection')
  // **한 순간을 집어 보지 않습니다.** 겹침은 0.22초이고 그림은 0.7초 뒤에 닿으므로, 어느
  // 한 시각을 재면 그 앞뒤로 몇십 밀리초에 답이 갈립니다 — 창을 훑어 가장 큰 값을 봅니다.
  let crossed = 0
  for (let step = 0; step < 30; step++) {
    await page.waitForTimeout(60)
    const now = (await peek(page)).collection as Census | undefined
    crossed = Math.max(crossed, now?.fading ?? 0)
  }
  check('스켈레톤에서 그림으로 겹쳐 흐릅니다', crossed > 0, `가장 많이 겹친 때 ${crossed}칸`)
  slow = false
  await pass(page, 900)
  await clickSpot(page, 'collection:range:all')
  await pass(page, 900)

  const opened = (await peek(page)).collection as Census | undefined
  check('조커 500종이 전부 앞면입니다', opened?.cells === 500 && opened?.found === 500,
        `${opened?.cells}칸 · 앞면 ${opened?.found}`)
  const builtAtStart = opened?.built ?? 0

  const where = await spot(page, 'collection:tab:joker')
  const middle = { x: where.x, y: where.y + 260 }
  await page.mouse.move(middle.x, middle.y)

  let worstDead = 0
  let firstDeadAt = -1
  let frameErrors: string[] = []
  let shot = false
  for (let step = 1; step <= STEPS; step++) {
    await page.mouse.wheel(0, 120)
    await pass(page, 60)
    const seen = await peek(page) as unknown as {
      deadArt?: [number, number]; errors?: string[]; collection?: Census; artBytes?: number
    }
    const dead = seen.deadArt?.[0] ?? 0
    if (dead > worstDead) worstDead = dead
    if (dead > 0 && firstDeadAt < 0) firstDeadAt = step
    if (seen.errors && seen.errors.length > 0) frameErrors = seen.errors
    // 상한에 닿은 뒤의 한 장. **그림이 버려지기 시작한 뒤가 이 도구가 보아야 하는 자리입니다.**
    if (!shot && step === Math.floor(STEPS / 2)) {
      shot = true
      await page.screenshot({ path: path.join(OUT, 'collection-roll-middle.png') })
    }
  }
  await pass(page, 1200)
  const end = await peek(page) as unknown as {
    deadArt?: [number, number]; errors?: string[]; collection?: Census; artBytes?: number
  }
  await page.screenshot({ path: path.join(OUT, 'collection-roll-end.png') })

  const rolled = -(end.collection?.offset ?? 0)
  const rowsCrossed = Math.floor(rolled / CELL_Y)
  const built = (end.collection?.built ?? 0) - builtAtStart
  console.log(`  굴린 거리 ${rolled}px · 지난 줄 ${rowsCrossed} · 지은 칸 ${built}`
    + ` · 그림 ${((end.artBytes ?? 0) / 1024 / 1024).toFixed(1)}MB`)

  check('끝까지 굴러갔습니다', rowsCrossed >= 20, `${rowsCrossed}줄`)
  check('버려진 그림을 가리키는 스프라이트가 없습니다', worstDead === 0,
        worstDead > 0 ? `${worstDead}개 · ${firstDeadAt}걸음에서 처음` : '')
  check('프레임이 던지지 않습니다', frameErrors.length === 0 && (end.errors?.length ?? 0) === 0,
        (end.errors ?? frameErrors).slice(0, 2).join(' / '))
  check('페이지 오류가 없습니다', pageErrors.length === 0,
        `${pageErrors.length}건 · ${pageErrors.slice(0, 1).join('').slice(0, 120)}`)
  // **줄 하나를 지나며 짓는 것은 그 줄만이어야 합니다.** 위아래 한 줄씩의 여유가 있으므로
  // 두 줄까지 봅니다 — 60칸씩 다시 짓고 있으면 여기서 여섯 배가 나옵니다.
  const perRow = rowsCrossed > 0 ? built / rowsCrossed : 0
  check('줄 하나를 지나며 짓는 칸이 두 줄 아래입니다', perRow <= COLUMNS * 2,
        `줄마다 ${perRow.toFixed(1)}칸`)

  console.log(`\n  화면: ${OUT}`)
  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
