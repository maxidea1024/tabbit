// 팩을 뜯은 화면을 몇 장으로 봅니다.
import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { buyAffordablePack, openRun, peek, skipLogin, spot, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5199 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1680, height: 960 } })
  await skipLogin(page)
  await page.goto('http://localhost:5199/?seed=CLOVER-PACK1', { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await openRun(page)

  // 블라인드를 하나 넘기고 정산을 받습니다.
  // 봇이 이기기를 기다리지 않습니다. 훅으로 이기고 정산을 받아 상점까지 갑니다.
  await winRound(page)

  await buyAffordablePack(page)
  if (!(await peek(page)).packOpen) {
    console.log('팩을 살 돈이 없습니다')
    await shot(page, 'pack-none')
    await browser.close()
    await server.close()
    return 1
  }

  // 펼쳐지는 동안을 석 장으로.
  // **상점이 내려간 뒤에 카드가 나옵니다.** 뜯은 직후에는 판이 내려가는 중이고, 카드는 그
  // 뒤에 아래에서 올라옵니다 — 첫 장은 그 내려가는 판을 찍습니다.
  for (const [index, wait] of [200, 500, 700].entries()) {
    await page.waitForTimeout(wait)
    await shot(page, `pack-${index + 1}`)
  }

  // 펼쳐진 첫 장에 마우스를 올립니다. **자리는 화면이 알립니다** — 몇 장이 펼쳐지는지는
  // 팩의 갈래가 정하므로, 가운데를 짚으면 장수가 짝수인 팩에서는 두 장 사이입니다.
  const middle = await spot(page, 'pack:0')
  await page.mouse.move(middle.x, middle.y)
  await page.waitForTimeout(400)
  await shot(page, 'pack-hover')

  // 그 카드를 집습니다.
  await page.mouse.down()
  await page.waitForTimeout(60)
  await page.mouse.up()
  await page.waitForTimeout(700)
  await shot(page, 'pack-picked')

  await browser.close()
  await server.close()
  return 0
}

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `${name}.png`) })
}

main().then(code => process.exit(code))
