// 보스가 카드에 거는 연출을 프레임마다 남깁니다.
//
// **게이트가 아닙니다** — 판정하지 않고 그림만 남깁니다. 무력해지는 것이 종이에 스미는
// 것으로 보이는지, 여러 장이 차례로 걸리는지, 글이 카드 너머에서 읽히는지를 봅니다.
//
//     npx tsx tools/shoot-cast.ts
//
// **시계를 손으로 돌립니다**(`tick=manual`). 사진 한 장 찍는 데 드는 시간이 프레임 간격보다
// 길어서, 실제 시간으로 찍으면 연출이 끝난 뒤의 모습만 남습니다.
//
// **보스를 만나야만 볼 수 있는 연출입니다.** 안테 1의 스몰에서 보스까지 가야 하고, 그
// 보스가 무력화를 거는 것이어야 하고, 그 무늬가 손에 있어야 합니다 — 그래서 화면이
// `castOnHand` 을 내어 두었고 이 도구가 그것을 부릅니다.
//
// **판을 건드리지 않는 훅입니다.** 그래서 `hide` 는 엎어졌다가 다시 앞면으로 돌아옵니다 —
// 실제로는 판에 `faceDown` 이 켜져 있어 뒷면으로 남습니다. 여기서 보는 것은 그 사이의
// 몸짓이고, 끝난 모습은 카드의 얼굴이 들고 있습니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import { clickPrimary, closeGuide, pass, skipLogin, startNewRun } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/cast')
const PORT = 5312
const CLIP = { x: 380, y: 430, width: 560, height: 290 }

async function cast(page: Page, kind: string, many: number): Promise<void> {
  await page.evaluate(([one, count]) => {
    const hook = (window as unknown as {
      __clover: { castOnHand?(kind: string, many: number): void }
    }).__clover
    hook.castOnHand?.(one as string, count as number)
  }, [kind, many] as [string, number])
}

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({
    viewport: { width: 1280, height: 800 }, deviceScaleFactor: 2,
  })
  page.on('pageerror', error => console.log('오류', String(error)))
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-LOOK&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await startNewRun(page)
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 400)
  await clickPrimary(page)
  await pass(page, 2400)

  for (const kind of ['wither', 'hide']) {
    await cast(page, kind, 3)
    for (let step = 0; step < 12; step++) {
      await pass(page, 55)
      await page.screenshot({
        path: path.join(OUT, `${kind}-${String(step + 1).padStart(2, '0')}.png`), clip: CLIP,
      })
    }
    await pass(page, 900)
  }

  await browser.close()
  await server.close()
  console.log(`거는 연출을 ${OUT} 에 남겼습니다`)
  return 0
}

main().then(code => process.exit(code))
