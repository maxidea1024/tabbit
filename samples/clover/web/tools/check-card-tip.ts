// 손패의 카드에 마우스를 올렸을 때 설명이 뜨는가. 그리고 카드 위의 셈 쪽지가 없는가.
//
//     npx tsx tools/check-card-tip.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  clickSpot, closeGuide, handSpot, pass, peek, pickCards, startNewRun, settle, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5244

const problems: string[] = []

async function main(): Promise<void> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('console', m => {
    // 로그인 판을 세우지 않았으므로 그 요청 하나는 500 입니다.
    if (m.type() === 'error' && !m.text().includes('500')) problems.push(m.text())
  })
  page.on('pageerror', e => problems.push(String(e)))
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TIP&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1200)
  await startNewRun(page)
  await settle(page)
  await closeGuide(page)
  await pass(page, 400)
  await clickSpot(page, 'pick')
  await settle(page)

  await pickCards(page, [0, 1, 2])
  await pass(page, 400)
  await page.screenshot({ path: path.join(OUT, 'cardtip-picked.png') })

  // 마우스를 다섯째 장 위로. 움직임 두 번이어야 「움직였다」 가 성립합니다.
  //
  // **자리는 화면이 알립니다.** 같은 셈을 여기에 적어 두고 있었고 간격의 상한이
  // 달랐습니다 — 가운데 장이라 카드 안에 떨어져 지나가고 있었을 뿐입니다. 부채꼴로
  // 폈을 때의 높이 보정도 함께 걷습니다(손패는 한 줄입니다).
  const spot = await handSpot(page, 4)
  await page.mouse.move(spot.x - 30, spot.y - 30)
  await pass(page, 120)
  await page.mouse.move(spot.x, spot.y)
  // 튀어나오는 중간. **작고 옅어야 합니다** — 그냥 나타나면 이 그림이 다 나온 것과 같습니다.
  await pass(page, 60)
  await page.screenshot({ path: path.join(OUT, 'cardtip-pop.png') })
  await pass(page, 300)
  if (!(await peek(page)).tip) problems.push('카드에 올렸는데 설명이 뜨지 않습니다')
  await page.screenshot({ path: path.join(OUT, 'cardtip-hover.png') })

  // 벗어나면 닫힙니다.
  await page.mouse.move(200, 200)
  await pass(page, 300)
  if ((await peek(page)).tip) problems.push('카드를 벗어났는데 설명이 남아 있습니다')

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    for (const one of problems) console.error(`- ${one}`)
    process.exit(1)
  }
  console.log('손패 카드의 설명 확인')
}

void main()
