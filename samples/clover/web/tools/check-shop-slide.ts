// 줄의 조커를 누르면 상점의 칸이 미끄러지는가.
//
// **같은 물건이 같은 값으로 둘 서 있는 상점에서 그랬습니다.** 지난 자리를 물건마다 하나씩
// 담고 있어서, 두 칸의 열쇠가 같으면 왼쪽 칸이 오른쪽 칸의 자리를 받았습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { grantJoker, grantMoney, jokerSpot, openRun, pass, peek, skipLogin, spot, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5288

async function shopAt(page: Page): Promise<number[][]> {
  const now = await peek(page)
  return (now as unknown as { shopAt?: number[][] }).shopAt ?? []
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  const seed = process.argv[2] ?? 'CLOVER-FLOW2'
  await page.goto(`http://localhost:${PORT}/?seed=${seed}&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await grantJoker(page, 2)
  await grantMoney(page, 4000)
  await pass(page, 600)
  await winRound(page)
  await pass(page, 1200)

  const at = await jokerSpot(page, 0)
  let bad = 0
  const rounds = Number(process.argv[3] ?? 30)
  for (let round = 0; round < rounds; round++) {
    await page.mouse.click(at.x, at.y)
    for (let i = 0; i < 6; i++) {
      for (const one of await shopAt(page)) {
        if (Math.abs(one[1] - one[2]) > 1) {
          console.log(`  실패  [${round}] 칸${one[0]} x=${one[1]} base=${one[2]}`)
          bad++
        }
      }
      await pass(page, 40)
    }
    const reroll = await spot(page, 'reroll')
    await page.mouse.click(reroll.x, reroll.y)
    await pass(page, 1400)
  }
  console.log(bad === 0 ? `  통과  ${rounds}번 다시 세워도 칸이 제자리입니다`
    : `  실패  ${bad}번 미끄러졌습니다`)

  await browser.close()
  await server.close()
  return bad === 0 ? 0 : 1
}

main().then(code => process.exit(code))
