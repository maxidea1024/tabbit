// 덱 15종의 뒷면을 한 장에 굽습니다.
//
// **눈으로 보는 것 말고 판정할 방법이 없습니다.** 무늬가 뭉갰는지 · 색이 바탕에 묻히는지 ·
// 열다섯이 서로 구분되는지는 값으로 물어볼 수 있는 것이 아닙니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5231

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  // 큰 창입니다 — 열다섯을 한 장에 담으면서 작은 쪽도 알아볼 수 있어야 합니다.
  const page = await browser.newPage({ viewport: { width: 1600, height: 1100 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/backs.html`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1800)
  await page.screenshot({ path: path.join(OUT, 'backs.png') })

  await browser.close()
  await server.close()
  console.log('backs.png')
  return 0
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
