// 겉면 하나를 걸고 화면 셋을 굽습니다.
//
// **겉면을 바꾸면 무너지는 자리를 눈으로 봅니다.** 구운 부품은 겉면의 색을 그대로 물들이므로
// 검은 겉면이면 판이 검어야 하고, 밝은 겉면이면 밝아야 합니다 — 타입은 그것을 잡지 못합니다.
//
//     npx tsx tools/shoot-theme.ts ink
//
// 나온 것은 `design-data/out/check/theme-<겉면>-*.png` 입니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { clickSpot, pass, skipLogin, startNewRun } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5233
const THEME = process.argv[2] ?? 'ink'

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `theme-${THEME}-${name}.png`) })
  console.log(`theme-${THEME}-${name}.png`)
}

async function main(): Promise<void> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
    await skipLogin(page)
    // **겉면은 옵션 저장에 적혀 있습니다.** 화면이 서기 전에 넣어 두면 그 겉면으로 켜집니다.
    await page.addInitScript((theme: string) => {
      const key = 'clover.options'
      const found = JSON.parse(localStorage.getItem(key) ?? '{}') as Record<string, unknown>
      found.uiTheme = theme
      localStorage.setItem(key, JSON.stringify(found))
    }, THEME)
    await page.goto(`http://localhost:${PORT}/?seed=CLOVER-SHOT6&tick=manual`, { waitUntil: 'networkidle' })
    await pass(page, 1500)
    await shot(page, 'title')
    await clickSpot(page, 'title:start')
    await pass(page, 900)
    await shot(page, 'start')
    await page.keyboard.press('Escape')
    await pass(page, 900)
    await startNewRun(page)
    await pass(page, 1500)
    await shot(page, 'round')
    await page.close()
  } finally {
    await browser.close()
    await server.close()
  }
}

void main()
