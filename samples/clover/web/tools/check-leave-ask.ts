// 메뉴의 「타이틀로」가 묻고 나서 가는가.
//
// **런이 사라지는 것이므로 묻습니다.** 이어서 하는 길이 없으니 잘못 누른 사람에게는 그것이
// 사고입니다. 「아니오」로 그 자리에 남는 것까지 봅니다.
//
//     npx tsx tools/check-leave-ask.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { clickSpot, confirmYes, closeGuide, pass, peek, startNewRun, settle, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5245

async function main(): Promise<void> {
  const problems: string[] = []
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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-LEAVE&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1200)
  await startNewRun(page)
  await settle(page)
  await closeGuide(page)
  await pass(page, 400)

  // 메뉴 → 타이틀로. **여기서 곧바로 가면 안 됩니다.**
  await clickSpot(page, 'menu')
  await pass(page, 500)
  await clickSpot(page, 'menu:toTitle')
  await pass(page, 700)
  await page.screenshot({ path: path.join(OUT, 'leave-ask.png') })
  const asked = await peek(page)
  if (asked.scene !== 'run') problems.push('묻지 않고 타이틀로 갔습니다')

  // 아니오 — 판에 남습니다.
  await page.keyboard.press('Escape')
  await pass(page, 600)
  if ((await peek(page)).scene !== 'run') problems.push('아니오 뒤에 판에 남지 않았습니다')

  // 다시 열어 예를 누르면 갑니다.
  await clickSpot(page, 'menu')
  await pass(page, 500)
  await clickSpot(page, 'menu:toTitle')
  await pass(page, 700)
  // 「예」 쪽 단추. **자리는 화면이 알립니다** — 물어보는 판의 높이는 그 안에 무엇이
  // 적히는지에 따라 자랍니다.
  await confirmYes(page)
  await pass(page, 1200)
  const left = await peek(page)
  if (left.scene !== 'title') problems.push(`예를 눌렀는데 씬이 ${left.scene} 입니다`)

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    for (const one of problems) console.error(`- ${one}`)
    process.exit(1)
  }
  console.log('타이틀로가 묻고 나서 갑니다')
}

void main()
