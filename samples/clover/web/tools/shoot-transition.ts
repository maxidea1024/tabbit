// 전환 여덟 자리를 눈으로 봅니다.
//
// **모습은 값으로 볼 수 없습니다.** `check-transition.ts` 가 보는 것은 「보이지 않는 자리에서
// 갈렸는가」이고, 그것이 참이어도 어떻게 지워지는지는 아무 말도 하지 않습니다.
//
// **씬을 실제로 갈지 않습니다.** `__clover.cross(id)` 가 갈아 끼우는 것 없이 전환만
// 돌리므로, 이긴 판의 전환을 보려고 안테 8까지 둘 일이 없습니다.
//
//     npx tsx tools/shoot-transition.ts

import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'

import { closeGuide, crossed, pass, peek, settle, skipLogin, startNewRun } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
// **`out/check` 입니다.** 이 그림들은 문서에 실리는 것이 아니라 눈으로 한 번 보는 것이고,
// 그 자리는 저장소에 담지 않습니다.
const OUT = path.resolve(HERE, '../../design-data/out/check/cross')
const PORT = 5272
/** 얼마마다 보는가. 덮인 정도를 짚어야 하므로 한 프레임에 가깝게. */
const STEP_MS = 20

/** 어느 자리를 어느 화면에서 보는가. */
const SHOTS: { id: string; where: 'title' | 'run' }[] = [
  { id: 'boot_first', where: 'title' },
  { id: 'title_login', where: 'title' },
  { id: 'title_run', where: 'title' },
  { id: 'run_title', where: 'run' },
  { id: 'run_restart', where: 'run' },
  { id: 'run_lost', where: 'run' },
  { id: 'run_won', where: 'run' },
  { id: 'login_title', where: 'title' },
]

/** 전환 하나를 돌리며 세 컷을 찍습니다. */
async function shoot(page: Page, id: string): Promise<string> {
  await page.evaluate(name => {
    (window as unknown as { __clover: { cross?(id: string): void } }).__clover.cross?.(name)
  }, id)

  const took = new Set<string>()
  for (let i = 0; i < 120; i++) {
    const now = (await peek(page)).transition
    if (!now || now.stage === 'off') {
      if (took.size > 0) break
      await pass(page, STEP_MS)
      continue
    }
    // 지워지는 중 절반 · 다 지워진 자리 · 되돌아오는 중 절반.
    const cut = now.stage === 'hold' ? 'held'
      : now.stage === 'out' && now.cover > 0.45 ? 'out'
      : now.stage === 'in' && now.cover < 0.55 ? 'in'
      : ''
    if (cut && !took.has(cut)) {
      took.add(cut)
      await page.screenshot({ path: path.join(OUT, `${id}-${cut}.png`) })
    }
    await pass(page, STEP_MS)
  }
  await crossed(page)
  return [...took].join(' · ')
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  await fs.mkdir(OUT, { recursive: true })
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-CROSS&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await crossed(page)

  // 타이틀에서 볼 것들.
  for (const one of SHOTS.filter(one => one.where === 'title')) {
    console.log(`${one.id}: ${await shoot(page, one.id)}`)
  }

  // 판을 열고 나머지.
  await startNewRun(page)
  await crossed(page)
  await pass(page, 500)
  await closeGuide(page)
  await settle(page)
  await pass(page, 300)
  for (const one of SHOTS.filter(one => one.where === 'run')) {
    console.log(`${one.id}: ${await shoot(page, one.id)}`)
  }

  await browser.close()
  await server.close()
  console.log(`${OUT} 에 찍었습니다`)
  return 0
}

void main().then(code => process.exit(code))
