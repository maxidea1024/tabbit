// 칩과 배수의 바탕에 흐르는 파형을 눈으로 봅니다. **고르는 동안만 쓰는 도구입니다.**
//
// 화면 전체를 찍으면 이 층은 264 × 58 이라 판 하나의 2%이고, 그 크기에서는 파형이 선 한 줄로
// 보입니다 — **그 사각형만 오려 3배로 굽습니다.** 자리는 화면이 알린 것을 씁니다.
//
//     npx tsx tools/shoot-wave.ts
//
// 굽는 것은 넷입니다 — 조용한 자리 · 더해지는 동안 · 정산 뒤 · 배당이 큰 자리입니다.
// **세기는 함께 적힙니다.** 그림으로 세기를 판정하지 않습니다.

import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  at, chooseFive, clickPrimary, closeGuide, pass, peek, pickCards, pressPlay, scoreWave,
  skipLogin, startNewRun,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/wave')
const PORT = 5281

/**
 * 오려 찍습니다. **자리는 화면이 알린 것입니다.**
 *
 * `deviceScaleFactor` 가 3이므로 264 × 58 이 792 × 174 로 나옵니다 — 그 크기에서 흰 심과
 * 색 번짐 두 겹이 갈립니다.
 */
/**
 * 오려 찍고 그 앞뒤의 세기를 함께 적습니다.
 *
 * **한 순간의 값이 아닙니다.** 그림을 굽는 데 글꼴을 기다리는 시간이 들어서 값 하나를 앞에서
 * 읽으면 그림과 다른 순간이 되고, 잦아드는 0.42초 안에서는 그 차이가 값의 절반입니다 —
 * 앞뒤를 함께 적어 그림이 그 사이의 것임을 남깁니다.
 */
async function shot(page: Page, name: string): Promise<string> {
  const before = await scoreWave(page)
  const [left, top, width, height] = before.box
  // **판의 좌표를 창의 자리로 환산합니다.** 판은 창을 꽉 채우지 않습니다.
  const spot = await at(page, left, top)
  const far = await at(page, left + width, top + height)
  await page.screenshot({
    path: path.join(OUT, `${name}.png`),
    clip: { x: spot.x, y: spot.y, width: far.x - spot.x, height: far.y - spot.y },
  })
  const after = await scoreWave(page)
  const span = (from: number, to: number) => from === to
    ? from.toFixed(2)
    : `${from.toFixed(2)}~${to.toFixed(2)}`
  return `${name}  칩 ${span(before.chips, after.chips)}`
    + ` · 배수 ${span(before.mult, after.mult)}`
    + ` · 배당 ${span(before.level, after.level)}${before.shown ? '' : ' · 꺼짐'}`
}

/**
 * 배당마다 파형이 얼마나 빠르게 흐르는가.
 *
 * **빠르기는 그림으로 확인되지 않습니다.** 흐르는 것을 정지한 컷 여러 장으로 판정하려면
 * 컷마다 같은 무늬를 찾아 그 이동을 세야 하고, 파형에는 같은 무늬가 없습니다 — 위상의
 * 차이를 그 사이의 시간으로 나눈 것이 곧 빠르기입니다.
 *
 * **비만 봅니다.** 겨누는 값은 조용한 자리에서 초당 1.9 이고 마지막 단에서 9.5 이므로 비가
 * 5.0 인데, 헤드리스에서 나오는 절대값은 그 0.70배쯤입니다 — 이 도구는 GPU 없이 돌아서
 * 프레임이 60에 못 미치고, Pixi 의 티커가 `minFPS` 로 한 프레임의 길이를 100밀리초에서
 * 끊으므로 그만큼의 시간이 누적되지 않습니다. 실제 기계에서는 끊기는 자리가 없습니다.
 */
async function speeds(page: Page): Promise<string[]> {
  const out: string[] = []
  for (const [name, chips, mult] of [
    ['조용', 0, 0],
    ['중간', 90_000, 250],
    ['마지막', 4_000_000, 100],
  ] as const) {
    await page.evaluate(([c, m]) => {
      (window as unknown as { __clover: { forceScore?(c: number, m: number): void } })
        .__clover.forceScore?.(c, m)
    }, [chips, mult])
    // **굴러가는 것이 끝난 뒤에 셈합니다.** 굴러가는 동안은 배당이 계속 오르므로 빠르기도
    // 그 사이에 바뀝니다.
    await pass(page, 3200)
    const from = await scoreWave(page)
    await pass(page, 1000)
    const to = await scoreWave(page)
    out.push(`${name}(배당 ${to.level.toFixed(2)}) 초당 ${(to.phase - from.phase).toFixed(2)}`)
  }
  return out
}

async function main(): Promise<number> {
  await fs.mkdir(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({
    viewport: { width: 1280, height: 800 }, deviceScaleFactor: 3,
  })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-WAVE1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await startNewRun(page)
  await page.waitForTimeout(900)
  await closeGuide(page)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await page.waitForTimeout(2000)

  const said: string[] = []
  // **조용한 자리입니다.** 여기서 가운데 줄이 보여야 하고, 노이즈가 없어야 합니다.
  said.push(await shot(page, 'wave-1-quiet'))

  const state = await peek(page)
  await pickCards(page, chooseFive(state.hand))
  await page.waitForTimeout(400)
  await pressPlay(page)

  // 더해지는 동안을 촘촘히. **여기가 요동치는 자리입니다.**
  for (const [index, wait] of [520, 260, 260, 260, 260].entries()) {
    await page.waitForTimeout(wait)
    said.push(await shot(page, `wave-2-add-${index + 1}`))
  }

  // 잦아든 뒤. **배당이 작으면 조용한 자리로 돌아가야 합니다.**
  await page.waitForTimeout(2400)
  said.push(await shot(page, 'wave-3-after'))

  // 배당이 큰 두 자리. **잦아들지 않고 계속 요동쳐야 합니다.**
  //
  // 실제로 그 배당까지 판을 굴리는 것은 이 도구의 일이 아니므로 칸에 수를 넣습니다 —
  // `euphoria` 의 사다리가 `chips × mult / 10,000` 으로 40 · 400 · 4,000 · 40,000 이고,
  // 아래 둘이 그 셋째 단쯤과 마지막 단입니다.
  //
  // **마지막 단의 수는 칸보다 넓습니다.** 그 배당의 칩은 일곱 자리이고 칸은 115픽셀이므로
  // 숫자가 상자 밖으로 나갑니다 — 파형과 무관한 것이고, 그래서 칸에 들어가는 자리도 함께
  // 굽습니다.
  for (const [name, chips, mult, wait] of [
    ['wave-4-high', 90_000, 250, 3200],
    ['wave-5-top', 4_000_000, 100, 3200],
  ] as const) {
    await page.evaluate(([c, m]) => {
      (window as unknown as { __clover: { forceScore?(c: number, m: number): void } })
        .__clover.forceScore?.(c, m)
    }, [chips, mult])
    // **굴러가는 것이 끝나기를 기다립니다.** 굴러가는 동안은 얹히는 것이 함께 있으므로
    // 바닥만 남은 자리를 보려면 그 뒤여야 합니다.
    await pass(page, wait)
    said.push(await shot(page, name))
  }

  console.log(said.join('\n'))
  console.log(`\n빠르기 — ${(await speeds(page)).join(' · ')}`)
  console.log(`\n${OUT}`)

  await browser.close()
  await server.close()
  return 0
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
