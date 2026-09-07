// 재로 부서지는 전환을 눈으로 봅니다. **고르는 동안만 쓰는 도구입니다.**
//
// `shoot-transition.ts` 는 여덟 자리를 세 컷씩 찍습니다 — 자리마다 전환이 다르다는 것을
// 보는 데는 그것으로 충분하지만, **모습 하나를 고치는 동안에는 세 컷으로 모자랍니다.**
// 조각이 어디서 떨어져 나와 어디까지 가는지는 지워지는 동안을 촘촘히 봐야 합니다.
//
//     npx tsx tools/shoot-ash.ts [자리]

import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import { closeGuide, crossed, pass, peek, settle, skipLogin, startNewRun } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/ash')
const PORT = 5273
const STEP_MS = 16

/** 어느 정도 지워진 자리를 보는가. */
const MARKS = [0.10, 0.22, 0.36, 0.50, 0.64, 0.78, 0.92]

async function shoot(page: Page, id: string, lite: boolean): Promise<string> {
  await page.evaluate(name => {
    (window as unknown as { __clover: { cross?(id: string): void } }).__clover.cross?.(name)
  }, id)

  const took: string[] = []
  let next = 0
  for (let i = 0; i < 300 && next < MARKS.length; i++) {
    const now = (await peek(page)).transition
    if (now && now.stage === 'out' && now.cover >= MARKS[next]) {
      const name = `${id}${lite ? '-lite' : ''}-${String(Math.round(MARKS[next] * 100)).padStart(2, '0')}`
      await page.screenshot({ path: path.join(OUT, `${name}.png`) })
      took.push(`${Math.round(now.cover * 100)}%`)
      next++
      continue
    }
    if (now && (now.stage === 'hold' || now.stage === 'off') && took.length > 0) break
    await pass(page, STEP_MS)
  }
  await crossed(page)
  return took.join(' · ')
}

/**
 * 도는 동안 한 프레임이 몇 밀리초인가.
 *
 * **재는 픽셀마다 바람의 길을 거슬러 오릅니다.** 그 값이 어느 기계에서 얼마인지는 재야
 * 알고, 여기서 재는 것은 **이 기계에서의 서로 견줌**입니다 — 다른 전환과 나란히 재므로
 * 재가 그것들보다 몇 배인지가 나옵니다. 그 기계에서의 실제 값은 `check-android.ts` 가
 * 붙어서 잽니다.
 */
async function timeIt(page: Page, id: string): Promise<string> {
  // **글로 넘깁니다.** 함수로 넘기면 번들러가 끼워 넣은 도움 이름이 페이지에 없어 그 자리에서
  // 멈춥니다 — `doc/ui/desktop.md` 의 확인 도구가 같은 자리에서 걸렸습니다.
  const got = await page.evaluate(`(async () => {
    const marks = []
    let last = performance.now()
    let stop = false
    const tick = () => {
      const now = performance.now()
      marks.push(now - last)
      last = now
      if (!stop) requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
    if (${JSON.stringify(id)} !== '') window.__clover.cross(${JSON.stringify(id)})
    await new Promise(done => setTimeout(done, 1700))
    stop = true
    const kept = marks.slice(3).sort((a, b) => a - b)
    return {
      frames: kept.length,
      mid: kept[Math.floor(kept.length / 2)] || 0,
      worst: kept[Math.floor(kept.length * 0.95)] || 0,
    }
  })()`) as { frames: number; mid: number; worst: number }
  return `${got.frames}프레임 · 중간값 ${got.mid.toFixed(1)}ms · 상위 5% ${got.worst.toFixed(1)}ms`
}

async function main(): Promise<number> {
  const id = process.argv[2] ?? 'run_lost'
  // **모바일 몫도 눈으로 봅니다.** 짚는 수를 줄인 쪽은 그 기계에서만 도는 길이라, 여기서
  // 켜 보지 않으면 성기어진 모습을 아무도 보지 않은 채로 나갑니다.
  const lite = process.argv.includes('--lite')
  // **시계를 손으로 돌리면 값을 잴 수 없습니다.** 재는 자리에서는 화면의 시계로 돕니다.
  const timing = process.argv.includes('--time')
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  await fs.mkdir(OUT, { recursive: true })
  // **값을 재려면 실제 GPU 여야 합니다.** 머리 없는 크로미움은 소프트웨어로 그리므로 픽셀
  // 하나의 셈이 몇십 배로 커지고, 그 값으로는 어느 것이 무거운지도 뒤집힙니다 — 그림만
  // 찍을 때는 머리가 없어도 같은 그림이 나옵니다.
  // 값을 재는 두 자리. **둘 다 봐야 합니다.**
  //
  // |어디|무엇이 나오는가|
  // |--|--|
  // |실제 GPU|이 기계에서 60프레임을 놓치지 않는가. 수직 동기를 끊으면 프레임 값이 GPU 를
  // 기다리지 않으므로, 놓친 프레임의 수로 봅니다|
  // |소프트웨어 그리기(`--soft`)|픽셀 하나의 셈이 몇십 배로 커지므로 **다른 전환과 견줄
  // 때** 씁니다. 약한 GPU 를 대신하는 자리이고, 절대값이 아니라 배수를 봅니다|
  //
  // 그 기계에서의 실제 값은 `check-android.ts` 가 붙어서 잽니다.
  const soft = process.argv.includes('--soft')
  const browser = await chromium.launch(timing && !soft
    // **수직 동기를 켠 채로 놓친 프레임을 셉니다.** 끊으면 프레임 값이 GPU 를 기다리지
    // 않아 0.3ms 로 나오고, 그것은 GPU 가 얼마나 걸리는지와 아무 상관이 없습니다 —
    // 1.7초에 100프레임이면 놓친 것이 없고, 그것이 보아야 하는 값입니다.
    ? { headless: false }
    : {})
  // **재는 자리에서는 픽셀 밀도를 2로 둡니다.** 모바일이 그렇고, 배율을 1로 낮추는 몫이
  // 밀도 1에서는 아무것도 바꾸지 않으므로 그 자리에서는 그 몫이 보이지 않습니다.
  const page = await browser.newPage({
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: timing ? 2 : 1,
  })
  // **셰이더가 컴파일되지 않으면 필터가 아무것도 하지 않습니다.** 그 화면은 전환이 도는
  // 중인데 아무 일도 일어나지 않은 것으로 보이므로, 값으로는 「전환이 돌았다」와 구분되지
  // 않습니다. 콘솔의 오류를 그대로 흘려 둡니다.
  page.on('console', one => {
    if (one.type() === 'error' || one.type() === 'warning') console.log(`[${one.type()}] ${one.text()}`)
  })
  page.on('pageerror', one => console.log(`[pageerror] ${one.message}`))
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-ASH${timing ? '' : '&tick=manual'}`,
    { waitUntil: 'networkidle' })
  if (lite) {
    await page.evaluate(() => {
      (window as unknown as { __clover: { crossLite?(on: boolean): void } })
        .__clover.crossLite?.(true)
    })
  }
  await pass(page, 1500)
  await crossed(page)
  await startNewRun(page)
  await crossed(page)
  await pass(page, 500)
  await closeGuide(page)
  await settle(page)
  await pass(page, 300)

  if (timing) {
    // 견줄 것들. **재만 재면 그 값이 큰지 작은지 알 수 없습니다.**
    // **아무것도 걸지 않은 값을 먼저 잽니다.** 그것을 빼야 필터 하나가 더한 몫이 나옵니다 —
    // 판을 그리는 값이 함께 들어 있으면 두 전환의 배수가 1에 가깝게 보입니다.
    for (const one of ['', 'run_title', 'run_restart', id]) {
      const label = one === '' ? '(전환 없음)' : one
      console.log(`${label}${lite ? ' (모바일 몫)' : ''}: ${await timeIt(page, one)}`)
      await pass(page, 200)
    }
  } else {
    console.log(`${id}${lite ? ' (모바일 몫)' : ''}: ${await shoot(page, id, lite)}`)
  }

  await browser.close()
  await server.close()
  console.log(`${OUT} 에 찍었습니다`)
  return 0
}

void main().then(code => process.exit(code))
