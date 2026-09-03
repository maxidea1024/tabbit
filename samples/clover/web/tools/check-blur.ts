// 판 뒤가 흐려질 때 화면이 어긋나지 않는가.
//
// **눈으로는 한 프레임짜리 어긋남을 잡을 수 없습니다.** 판이 뜰 때 한 번, 사라질 때 한 번
// 화면이 통째로 옮겨 그려지던 결함이었고, 원인은 굽는 자리가 바뀌는 것이었습니다.
//
// |무엇|왜 그것이 어긋남이 되는가|
// |--|--|
// |흐림의 여백|`repeatEdgePixels` 가 꺼져 있으면 Pixi 가 여백을 반지름의 두 배로 잡고 정수로 자릅니다. 반지름이 0에서 1.5로 오르는 동안 여백이 0 · 1 · 2 · 3 으로 뚝뚝 넘어갑니다|
// |굽는 자리|여백이 바뀌면 자리가 커지고, 그 자리는 반 해상도의 텍셀 격자에 맞춰 잘립니다 — 맞추는 자리가 달라지므로 그림 전체가 최대 2픽셀 옮겨 그려집니다|
// |통의 경계|자리를 못박아 두지 않으면 통에 든 것들의 경계가 굽는 자리가 됩니다. 카드와 조각이 움직이므로 그 자리가 프레임마다 달라집니다|
//
// 그래서 재는 것은 **여백이 0 이고 자리가 못박혀 있는가**입니다. 그 둘이 지켜지면 흐림은
// 제자리에서 번지는 일이 됩니다.
import * as path from 'path'
import { fileURLToPath } from 'url'

import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import { at, HAND_LIST_BUTTON, openRun, pass, settle, skipLogin, STAGE_H, STAGE_W } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5219

interface Region {
  padding: number
  backPadding: number
  strength: number
  area?: [number, number, number, number]
  filtered: boolean
}

async function region(page: Page): Promise<Region> {
  return page.evaluate(() => {
    const hook = (window as unknown as {
      __clover: { blurRegion?(): unknown }
    }).__clover
    return hook.blurRegion?.() as never
  })
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: STAGE_W, height: STAGE_H } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BLUR`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)
  await openRun(page)
  await settle(page)

  const seen: Region[] = []

  // 판 하나를 열고 닫으면서 흐림이 오르내리는 동안을 여러 번 읽습니다.
  const info = await at(page, HAND_LIST_BUTTON.x, HAND_LIST_BUTTON.y)
  await page.mouse.click(info.x, info.y)
  for (let i = 0; i < 8; i++) {
    await pass(page, 25)
    seen.push(await region(page))
    if (i === 1) await page.screenshot({ path: path.join(OUT, 'blur-in.png') })
  }
  await page.keyboard.press('Escape')
  for (let i = 0; i < 8; i++) {
    await pass(page, 25)
    seen.push(await region(page))
    if (i === 1) await page.screenshot({ path: path.join(OUT, 'blur-out.png') })
  }

  await browser.close()
  await server.close()

  const bad: string[] = []
  const want = [0, 0, STAGE_W, STAGE_H].join(',')
  for (const one of seen) {
    if (one.padding !== 0 || one.backPadding !== 0) {
      bad.push(`반지름 ${one.strength} 에서 여백이 ${one.padding}·${one.backPadding} 입니다`)
    }
    if (one.area?.join(',') !== want) {
      bad.push(`굽는 자리가 못박혀 있지 않습니다 — ${one.area?.join(',') ?? '없음'}`)
    }
  }
  // 흐림이 실제로 걸렸는지도 봅니다. **걸리지 않았으면 위의 둘은 아무것도 재지 않습니다.**
  if (!seen.some(one => one.filtered && one.strength > 0.3)) {
    bad.push('흐림이 걸린 프레임이 없습니다')
  }

  for (const line of new Set(bad)) console.log(`  ${line}`)
  console.log(bad.length === 0 ? '흐림 자리 고정' : `어긋난 것 ${new Set(bad).size}가지`)
  return bad.length === 0 ? 0 : 1
}

main().then(code => process.exit(code))
