// 왼쪽 판 맨 위 딱지를 굽습니다.
//
// **글 하나가 상황마다 다른 것을 적는 자리입니다** — 블라인드를 고르는 중 · 판이 도는 중 ·
// 상점입니다. 수가 강조되는지, 강조된 수가 접힌 줄에서 어긋나지 않는지를 봅니다.
//
//     npx tsx tools/shoot-badge.ts
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { openRun, settle, skipLogin, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5233

/** 왼쪽 판의 맨 위만 잘라 냅니다. 딱지 하나를 보려고 화면 전체를 볼 이유가 없습니다. */
async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({
    path: path.join(OUT, `badge-${name}.png`),
    clip: { x: 0, y: 0, width: 300, height: 230 },
  })
  console.log(`badge-${name}.png`)
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 },
                                       deviceScaleFactor: 2 })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-BADGE`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await openRun(page)
  await settle(page)
  await shot(page, 'round')

  // 상점. **요구 점수 대신 다음 블라인드와 그 점수가 서는 자리입니다.**
  await winRound(page)
  await settle(page)
  await page.waitForTimeout(900)
  await shot(page, 'shop')

  await browser.close()
  await server.close()
  return 0
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
