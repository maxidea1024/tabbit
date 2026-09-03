// 판 밖이 잘리고 있는가.
//
// 판은 1280 × 800 하나에 맞춰 그려지고 창의 비율은 기계마다 다릅니다 — 갤럭시 폴드는
// 접으면 2.58, 펴면 1.25 이고, 폰을 가로로 쥐면 2.17 입니다. 남는 자리를 배경으로 채우면
// 판이 더 넓은 화면 가운데에 놓인 사각형 하나로 보이고, 비율마다 다른 화면이 됩니다.
//
// **그래서 판 밖은 잘라 냅니다.** 무대에 마스크 하나를 걸어서입니다.
//
// **이것이 지켜지는지는 눈으로 확인할 수 없습니다.** 사각형이 옳은 자리에 그려져 있어도
// 무대에 걸리지 않은 채일 수 있고, 그러면 배경과 번쩍임과 모달의 막이 그대로 새어 나가는데
// 화면은 아무 말도 하지 않습니다. 새는 것이 배경만이 아니라는 것이 이 도구가 있는 이유입니다 —
// 번쩍임은 `-2000` 부터 그리고 모달의 막은 판의 3배입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { openRun, pass, skipLogin, STAGE_H, STAGE_W } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5262

/**
 * 재는 비율들.
 *
 * **기준 비율 하나로는 아무것도 확인되지 않습니다** — 1.6 에서는 남는 자리가 없어서 마스크가
 * 없어도 통과합니다. 좌우가 남는 것과 위아래가 남는 것을 함께 봅니다.
 */
const SIZES = [
  { name: '기준 16:10', width: 1280, height: 800 },
  { name: '폰 가로 19.5:9', width: 1170, height: 540 },
  { name: '폴드 접음 2.58', width: 1240, height: 480 },
  { name: '폴드 펴짐 1.25', width: 1000, height: 800 },
  { name: '세로 9:19.5', width: 540, height: 1170 },
]

interface Crop {
  box?: [number, number, number, number]
  masked: boolean
  sheet: [number, number, number, number]
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const bad: string[] = []

  for (const one of SIZES) {
    const page = await browser.newPage({ viewport: { width: one.width, height: one.height } })
    await skipLogin(page)
    await page.goto(`http://localhost:${PORT}/?seed=CLOVER-CROP`, { waitUntil: 'networkidle' })
    await pass(page, 1400)
    await openRun(page)

    const crop = await read(page)
    // 판이 놓이는 자리. **`game.ts` 의 `layout` 과 같은 셈입니다.**
    const scale = Math.min(one.width / STAGE_W, one.height / STAGE_H)
    const want = [
      Math.round((one.width - STAGE_W * scale) / 2),
      Math.round((one.height - STAGE_H * scale) / 2),
      Math.ceil(STAGE_W * scale),
      Math.ceil(STAGE_H * scale),
    ]

    const off = crop.box === undefined
      || crop.box.some((value, i) => Math.abs(value - want[i]) > 1)
    const loose = crop.sheet.some((value, i) => Math.abs(value - want[i]) > 1)
    console.log(`${one.name.padEnd(16)} ${one.width}x${one.height}`
      + ` 자르는 자리 ${crop.box ? crop.box.join(',') : '없음'}`
      + ` 마스크 ${crop.masked ? '걸림' : '없음'}`
      + (off || loose || !crop.masked ? '  ✗' : ''))

    if (!crop.masked) bad.push(`${one.name} — 무대에 마스크가 걸려 있지 않습니다`)
    if (off) bad.push(`${one.name} — 자르는 자리가 판의 사각형과 다릅니다`
      + ` (${crop.box?.join(',')} ≠ ${want.join(',')})`)
    if (loose) bad.push(`${one.name} — 배경이 판의 사각형을 넘어갑니다`
      + ` (${crop.sheet.join(',')} ≠ ${want.join(',')})`)
    await page.close()
  }

  await browser.close()
  await server.close()

  console.log(bad.length === 0
    ? '\n어느 비율에서도 판 밖은 잘립니다'
    : '\n' + bad.join('\n'))
  return bad.length === 0 ? 0 : 1
}

async function read(page: Page): Promise<Crop> {
  return page.evaluate(() => {
    const hook = (window as unknown as {
      __clover: { cropRegion?(): unknown }
    }).__clover
    return hook.cropRegion?.() as never
  })
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
