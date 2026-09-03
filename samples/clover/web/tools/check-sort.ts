// 「랭크순」 · 「무늬순」 이 화면의 자리를 바꾸는가.
//
//     npx tsx tools/check-sort.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, BUTTON_Y, clickPrimary, peek, settle, STAGE_W , TITLE_START_Y, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5190 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1680, height: 960 } })
  await skipLogin(page)
  const problems: string[] = []
  page.on('pageerror', error => problems.push(String(error)))

  let failed = 0
  const verdict = (good: boolean, line: string) => {
    if (!good) failed++
    console.log(`  ${good ? '✓' : '✗'} ${line}`)
  }

  await page.goto('http://localhost:5190/?seed=CLOVER-SHOT6', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)
  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)
  await page.waitForTimeout(800)

  const before = (await peek(page)).hand.map(c => `${c.rank}${c.suit}`).join(' ')

  const rank = await at(page, 16 + 264 + 30 + 46, BUTTON_Y + 7 + 16)
  await page.mouse.click(rank.x, rank.y)
  await page.waitForTimeout(600)
  const byRank = (await peek(page)).hand
  console.log(`  전     ${before}`)
  console.log(`  랭크순 ${byRank.map(c => `${c.rank}${c.suit}`).join(' ')}`)
  verdict(byRank.every((c, i) => i === 0 || byRank[i - 1].rank >= c.rank),
    '랭크가 내림차순입니다')

  const suit = await at(page, 16 + 264 + 130 + 46, BUTTON_Y + 7 + 16)
  await page.mouse.click(suit.x, suit.y)
  await page.waitForTimeout(600)
  const bySuit = (await peek(page)).hand
  console.log(`  무늬순 ${bySuit.map(c => `${c.rank}${c.suit}`).join(' ')}`)
  verdict(bySuit.every((c, i) => i === 0 || bySuit[i - 1].suit <= c.suit),
    '무늬가 오름차순입니다')

  await browser.close()
  await server.close()
  for (const one of problems.slice(0, 5)) console.error('오류: ' + one)
  console.log(failed === 0 && problems.length === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 || problems.length > 0 ? 1 : 0
}
main().then(code => process.exit(code))
