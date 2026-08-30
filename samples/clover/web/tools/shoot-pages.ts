// 게임을 거치지 않는 그림 두 장.
//
// **판을 끝까지 플레이하는 하네스와 갈라 둡니다.** `shoot.ts` 는 한 판을 실제로 끝까지 두므로
// 20분이 넘고, 이 둘은 페이지를 열어 한 번 찍는 것이 전부입니다 — 그림이나 셰이더만 고쳤을
// 때 그 20분을 다시 치를 이유가 없습니다.
//
//     npx tsx tools/shoot-pages.ts

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/shot')

const PAGES: Array<{ name: string, url: string, wait: number }> = [
  { name: '16-art', url: 'artcheck.html', wait: 1_600 },
  { name: '14-editions', url: 'editions.html', wait: 1_400 },
]

async function main(): Promise<number> {
  fs.mkdirSync(OUT, { recursive: true })
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5178 } })
  await server.listen()

  const browser = await chromium.launch()
  const page = await browser.newPage({
    viewport: { width: 1680, height: 960 },
    deviceScaleFactor: 2,
  })

  for (const one of PAGES) {
    await page.goto(`http://localhost:5178/${one.url}`, { waitUntil: 'networkidle' })
    await page.waitForTimeout(one.wait)
    await page.screenshot({ path: path.join(OUT, `${one.name}.png`) })
    console.log(one.name)
  }

  await browser.close()
  await server.close()
  return 0
}

main().then(code => process.exit(code))
