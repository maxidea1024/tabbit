// 파형이 칩에서는 왼쪽으로, 배수에서는 오른쪽으로 흐르는가.
//
// **눈으로 판정할 수 없는 자리입니다.** 정지한 컷 하나에는 방향이 없고, 컷 둘을 나란히 놓아도
// 파형에는 눈으로 짚을 같은 무늬가 없습니다 — **두 컷의 산줄기를 상호상관으로 견주어 어느
// 쪽으로 몇 픽셀 옮겨졌는지를 셈합니다.**
//
//     npx tsx tools/check-wave-flow.ts
//
// 칩은 음수(왼쪽), 배수는 양수(오른쪽)여야 합니다. **한쪽이라도 0 에 가까우면 실패입니다** —
// 성분들이 서로 반대로 흘러 상쇄되면 그 값이 0 근처에 섭니다. 실제로 그렇게 있었습니다.

import * as fs from 'fs/promises'
import * as path from 'path'
import { spawnSync } from 'child_process'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  at, clickPrimary, closeGuide, pass, scoreWave, skipLogin, startNewRun,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/wave-flow')
const PORT = 5283

/**
 * 두 그림의 산줄기를 견주어 옮겨진 픽셀을 셈합니다.
 *
 * **파이썬이 셈합니다.** 노드 쪽에 PNG 를 푸는 것이 없고, 이 도구 하나를 위해 꾸러미를
 * 더하는 것보다 이미 있는 것을 쓰는 편이 낫습니다.
 *
 * 두 가지를 조심합니다.
 *
 * **흰 숫자를 색으로 걷어 냅니다.** 34픽셀 숫자가 파형보다 밝으므로 밝기로 산줄기를 뽑으면
 * 숫자가 앉은 칸이 전부 숫자를 가리킵니다. 숫자는 세 채널이 같고 파형은 파랑 또는 붉음이므로
 * **채널을 빼면 숫자가 상쇄되고 파형만 남습니다.**
 *
 * **파장의 절반 안에서만 찾습니다.** 파형은 되풀이하므로 상관의 골이 파장마다 다시 나옵니다 —
 * 넓게 찾으면 엉뚱한 골을 골라 부호가 뒤집힌 값이 나옵니다. 처음에 ±24 와 ±40 으로 찾다가
 * 그 자리에서 0 과 −39 를 얻었고, 둘 다 딴 골이었습니다.
 */
function shiftOf(before: string, after: string, box: 'chips' | 'mult',
                 span: number): number {
  const code = `
import sys, numpy as np
from PIL import Image

box = sys.argv[3]
span = int(sys.argv[4])

def ridge(path):
    a = np.asarray(Image.open(path).convert('RGB'), dtype=np.float64)
    h, w, _ = a.shape
    # 상자 하나만 봅니다. 층은 상자 둘과 그 사이(34/264)를 덮습니다.
    wide = w * 115 // 264
    a = a[:, :wide] if box == 'chips' else a[:, w - wide:]
    R, B = a[:, :, 0], a[:, :, 2]
    # 흰 숫자는 상쇄되고 파형만 남습니다.
    s = np.clip((B - R) if box == 'chips' else (R - B), 0, None)
    s = s[int(h * 0.10):int(h * 0.90), :]
    ys = np.arange(s.shape[0])[:, None]
    weight = s.sum(axis=0)
    # 무게가 있는 칸만. 밝은 줄 하나가 아니라 **무게 중심**입니다 — 그 편이 매끄럽습니다.
    return np.where(weight > 1e-6, (s * ys).sum(axis=0) / np.maximum(weight, 1e-6), np.nan)

b, c = ridge(sys.argv[1]), ridge(sys.argv[2])
edge = span + 5
best, score = 0, None
for s in range(-span, span + 1):
    x = np.roll(c, s)
    ok = ~(np.isnan(b) | np.isnan(x))
    ok[:edge] = False
    ok[-edge:] = False
    if ok.sum() < 40:
        continue
    d = float(np.mean(np.abs(b[ok] - x[ok])))
    if score is None or d < score:
        best, score = s, d
print(best)
`
  const ran = spawnSync('python',
    ['-c', code, before, after, box, String(Math.max(3, Math.round(span)))],
    { encoding: 'utf8' })
  if (ran.status !== 0) throw new Error(`셈하지 못했습니다: ${ran.stderr}`)
  // **부호를 뒤집습니다.** 위의 셈은 「뒤의 것을 얼마나 밀면 앞의 것과 맞는가」이므로,
  // 무늬가 간 쪽은 그 반대입니다.
  return -Number(ran.stdout.trim())
}

async function crop(page: Page, name: string): Promise<string> {
  const now = await scoreWave(page)
  const [left, top, width, height] = now.box
  const spot = await at(page, left, top)
  const far = await at(page, left + width, top + height)
  const file = path.join(OUT, `${name}.png`)
  await page.screenshot({
    path: file,
    clip: { x: spot.x, y: spot.y, width: far.x - spot.x, height: far.y - spot.y },
  })
  return file
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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-FLOW`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)
  await startNewRun(page)
  await page.waitForTimeout(900)
  await closeGuide(page)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await page.waitForTimeout(2000)

  let bad = 0
  // **배당마다 봅니다.** 성분마다 빠르기가 다르므로 한 배당에서만 맞을 수도 있습니다.
  for (const [name, chips, mult] of [
    ['낮음', 300, 15],
    ['중간', 1_000, 30],
    ['높음', 200_000, 40],
  ] as const) {
    await page.evaluate(([c, m]) => {
      (window as unknown as { __clover: { forceScore?(c: number, m: number): void } })
        .__clover.forceScore?.(c, m)
    }, [chips, mult])
    // 굴러가는 것과 더해진 것이 잦아든 뒤. 흐름만 남은 자리입니다.
    await pass(page, 3200)

    const tag = `${chips}x${mult}`
    const level = (await scoreWave(page)).level
    const freq = 2.4 + level * 3.6
    const boxPx = 115 * 3    // 판픽셀 × deviceScaleFactor
    const wave = boxPx / freq

    // **위상을 손으로 옮깁니다.** 그림 한 장을 굽는 데 1초쯤 들어서 시간으로는 컷 사이의
    // 위상 차이를 정할 수 없습니다 — 800라디안이 넘게 가고, 그것은 파장의 여러 배이므로
    // 어느 골을 골랐는지 알 수 없습니다.
    //
    // 옮기는 크기가 `TAU / 4` 이므로 **어느 배당에서나 정확히 산의 4분의 1**입니다.
    const step = Math.PI / 2
    const want = -wave / 4
    // **파장의 0.45 안에서만 찾습니다.** 절반을 넘으면 옆의 골을 고릅니다.
    const span = wave * 0.45

    const hold = async (phase: number) => {
      await page.evaluate(p => {
        (window as unknown as { __clover: { holdWave?(p: number): void } })
          .__clover.holdWave?.(p)
      }, phase)
      await pass(page, 40)
    }
    await hold(0)
    const before = await crop(page, `${tag}-1`)
    await hold(step)
    const after = await crop(page, `${tag}-2`)

    const moved = {
      chips: shiftOf(before, after, 'chips', span),
      mult: shiftOf(before, after, 'mult', span),
    }
    // 칩은 왼쪽(음수), 배수는 오른쪽(양수)입니다. **0 은 상쇄된 것입니다.**
    //
    // 크기도 봅니다 — 겨누는 값의 절반은 넘어야 합니다. 부호만 보면 한 픽셀 흔들린 것도
    // 통과하고, 그것은 흐르는 것이 아닙니다.
    const least = Math.abs(want) * 0.5
    const ok = moved.chips <= -least && moved.mult >= least
    if (!ok) bad++
    const sign = (v: number) => `${v > 0 ? '+' : ''}${v}px`
    console.log(`${ok ? 'OK  ' : '실패'} ${name}(배당 ${level.toFixed(2)}`
      + `, 산 ${wave.toFixed(0)}px, 겨눔 ${want.toFixed(1)}px)`
      + `  칩 ${sign(moved.chips)}  배수 ${sign(moved.mult)}`)
  }

  await browser.close()
  await server.close()
  if (bad > 0) {
    console.log(`\n${bad}개의 배당에서 흐르는 쪽이 어긋납니다.`)
    console.log('칩은 음수(왼쪽), 배수는 양수(오른쪽)여야 합니다.')
  } else {
    console.log('\n세 배당 모두 칩은 왼쪽으로, 배수는 오른쪽으로 흐릅니다.')
  }
  return bad > 0 ? 1 : 0
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
