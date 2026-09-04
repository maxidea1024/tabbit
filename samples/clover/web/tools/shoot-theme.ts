// 판의 겉면 넷을 굽습니다.
//
// **고른 그 자리에서 갈아입는지를 봅니다.** 옵션에서 고르고 판을 닫으면 그 화면이 바로
// 바뀌어야 합니다 — 다시 읽어야 바뀌는 것이면 그것은 갈아입은 것이 아닙니다.
//
//     npx tsx tools/shoot-theme.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { clickSpot, closeGuide, pass, pressTitle, settle, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5233

/** `UI_SURFACE_KEYS` 와 같은 순서입니다. */
const SURFACES = ['slate', 'ink', 'navy', 'bright'] as const

const problems: string[] = []

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `look-${name}.png`) })
  console.log(`look-${name}.png`)
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('console', message => { if (message.type() === 'error') problems.push(message.text()) })
  page.on('pageerror', error => problems.push(String(error)))
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-SHOT6&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // 판이 도는 화면에서 고릅니다 — 왼쪽 판이 그 자리에서 갈아입는지가 여기서만 보입니다.
  await pressTitle(page, 'start')
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 500)
  await clickSpot(page, 'pick')
  await settle(page)
  await pass(page, 400)

  for (const surface of SURFACES) {
    // 인게임에서는 메뉴를 거쳐야 하므로, 타이틀의 옵션 대신 화면의 옵션 단추를 씁니다.
    await clickSpot(page, 'menu')
    await pass(page, 500)
    await clickSpot(page, 'menu:options')
    await pass(page, 600)
    await clickSpot(page, 'option:tab:video')
    await pass(page, 500)
    if (surface === SURFACES[0]) await shot(page, 'options-video')
    await clickSpot(page, `option:uiTheme:${surface}`)
    await pass(page, 600)
    await shot(page, `theme-${surface}-panel`)
    await page.keyboard.press('Escape')
    await pass(page, 600)
    await shot(page, `theme-${surface}`)
  }

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    console.error(problems.join('\n'))
    return 1
  }
  return 0
}

main().then(code => process.exit(code))
