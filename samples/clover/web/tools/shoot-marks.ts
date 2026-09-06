// 카드에 붙는 표시를 한 장에 굽습니다.
//
// **눈으로 보는 것 말고 판정할 방법이 없습니다.** 강화의 칩이 종이색에 묻히는지 · 인장과
// 겹치는지 · 말이 길어졌을 때 칩이 카드를 넘는지는 값으로 물어볼 수 있는 것이 아닙니다.
//
//     npx tsx tools/shoot-marks.ts
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5232

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 900, height: 760 },
                                       deviceScaleFactor: 2 })
  const problems: string[] = []
  page.on('console', one => { if (one.type() === 'error') problems.push(one.text()) })
  page.on('pageerror', one => problems.push(String(one)))

  // **말마다 한 장입니다.** 칩의 넓이를 정하는 것이 글이라 한국어에서 맞는 것이 독일어에서
  // 넘칩니다 — 가장 짧은 쪽과 가장 긴 쪽을 함께 봅니다.
  for (const lang of (process.argv.slice(2).length > 0 ? process.argv.slice(2) : ['ko', 'de'])) {
    await page.goto(`http://localhost:${PORT}/marks.html?lang=${lang}`,
                    { waitUntil: 'networkidle' })
    await page.waitForTimeout(1800)
    await page.screenshot({ path: path.join(OUT, `marks-${lang}.png`) })
    console.log(`marks-${lang}.png`)
  }

  await browser.close()
  await server.close()
  for (const one of problems) console.log('오류', one)
  return problems.length > 0 ? 1 : 0
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
