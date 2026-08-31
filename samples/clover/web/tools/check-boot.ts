// 표를 읽기 전에 보이는 한 줄도 기계의 말을 따르는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5189 } })
  await server.listen()
  const browser = await chromium.launch()
  let failed = 0

  for (const [locale, want] of [
    ['ko-KR', '데이터를 읽는 중입니다'],
    ['ja-JP', 'データを読み込んでいます'],
    ['de-DE', 'Daten werden geladen'],
    ['zh-TW', '正在讀取資料'],
    ['zh-CN', '正在读取数据'],
    ['fr-FR', 'Loading data'],
  ] as const) {
    const page = await browser.newPage({ locale })
    // 화면이 뜨기 전에 그 글을 읽어야 합니다. 데이터를 다 읽으면 사라집니다.
    await page.goto('http://localhost:5189/', { waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(400)
    const got = await page.evaluate(() => document.getElementById('boot')?.textContent ?? '(없음)')
    const errors = await page.evaluate(() => (window as unknown as { __bootError?: string }).__bootError ?? '')
    if (errors !== '') console.log('    오류: ' + errors)
    const good = got === want
    if (!good) failed++
    console.log(`  ${good ? '✓' : '✗'} ${locale} → ${got}`)
    await page.close()
  }

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}
main().then(code => process.exit(code))
