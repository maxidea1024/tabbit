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

async function shoot(page: Page, id: string): Promise<string> {
  await page.evaluate(name => {
    (window as unknown as { __clover: { cross?(id: string): void } }).__clover.cross?.(name)
  }, id)

  const took: string[] = []
  let next = 0
  for (let i = 0; i < 300 && next < MARKS.length; i++) {
    const now = (await peek(page)).transition
    if (now && now.stage === 'out' && now.cover >= MARKS[next]) {
      const name = `${id}-${String(Math.round(MARKS[next] * 100)).padStart(2, '0')}`
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

async function main(): Promise<number> {
  const id = process.argv[2] ?? 'run_lost'
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  await fs.mkdir(OUT, { recursive: true })
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-ASH&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await crossed(page)
  await startNewRun(page)
  await crossed(page)
  await pass(page, 500)
  await closeGuide(page)
  await settle(page)
  await pass(page, 300)

  console.log(`${id}: ${await shoot(page, id)}`)

  await browser.close()
  await server.close()
  console.log(`${OUT} 에 찍었습니다`)
  return 0
}

void main().then(code => process.exit(code))
