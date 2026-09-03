// 표를 읽기 전에 보이는 한 줄도 기계의 말을 따르는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5189 } })
  await server.listen()
  const browser = await chromium.launch()
  let failed = 0

  // **세 줄 중 어느 것이든 그 말이면 됩니다.** 데이터 · 글꼴 · 그림 순서로 읽고, 개발
  // 서버에서는 400밀리초 안에 셋째까지 갑니다 — 첫 줄만 기다리면 기계가 빠를수록 실패합니다.
  for (const [locale, want] of [
    ['ko-KR', ['데이터를 읽는 중입니다', '글꼴을 읽는 중입니다', '그림을 읽는 중입니다']],
    ['ja-JP', ['データを読み込んでいます', 'フォントを読み込んでいます', '画像を読み込んでいます']],
    ['de-DE', ['Daten werden geladen', 'Schriften werden geladen', 'Grafiken werden geladen']],
    ['zh-TW', ['正在讀取資料', '正在讀取字型', '正在讀取圖像']],
    ['zh-CN', ['正在读取数据', '正在读取字体', '正在读取图像']],
    ['fr-FR', ['Loading data', 'Loading fonts', 'Loading art']],
  ] as const) {
    const page = await browser.newPage({ locale })
    await skipLogin(page)
    // 화면이 뜨기 전에 그 글을 읽어야 합니다. 데이터를 다 읽으면 사라집니다.
    await page.goto('http://localhost:5189/', { waitUntil: 'domcontentloaded' })
    await page.waitForTimeout(400)
    const got = await page.evaluate(() => document.getElementById('boot')?.textContent ?? '(없음)')
    const errors = await page.evaluate(() => (window as unknown as { __bootError?: string }).__bootError ?? '')
    if (errors !== '') console.log('    오류: ' + errors)
    const good = (want as readonly string[]).includes(got)
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
