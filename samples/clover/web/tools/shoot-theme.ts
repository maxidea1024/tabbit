// 판의 겉면 여덟을 굽습니다.
//
// **두 가지를 확인합니다.** 하나는 고른 그 자리에서 갈아입는가 — 옵션에서 고르고 판을
// 닫으면 그 화면이 바로 바뀌어야 하고, 다시 읽어야 바뀌는 것이면 그것은 갈아입은 것이
// 아닙니다. 다른 하나는 여덟이 저마다의 판으로 보이는가입니다.
//
// **뒤쪽은 눌러서 고르지 않습니다.** 겉면이 여덟이 되면서 옵션의 칸이 두 줄이 되었고,
// 둘째 줄은 굴림 창 밖에 있어 그 자리를 눌러도 닿지 않습니다 — 저장소에 적고 켭니다.
// 갈아입는 것 자체는 앞의 하나로 확인합니다.
//
//     npx tsx tools/shoot-theme.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, clickSpot, openRun, pass, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5233
const HOME = `http://localhost:${PORT}`
const SEED = `${HOME}/?seed=CLOVER-SHOT6&tick=manual`

/** `UI_THEME_KEYS` 와 같은 순서입니다. */
const SURFACES = ['slate', 'ink', 'navy', 'bright',
                  'green', 'wine', 'brown', 'violet'] as const

const problems: string[] = []

async function shot(page: Page, name: string): Promise<void> {
  await page.screenshot({ path: path.join(OUT, `look-${name}.png`) })
  console.log(`look-${name}.png`)
}

function watch(page: Page): void {
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', error => problems.push(String(error)))
}

/** 옵션에서 고른 그 자리에서 갈아입는지. **앞줄의 하나로 확인합니다.** */
async function swapsInPlace(browser: Browser): Promise<void> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  watch(page)
  await skipLogin(page)
  await page.goto(SEED, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await pass(page, 400)

  // 인게임에서는 메뉴를 거쳐야 하므로, 타이틀의 옵션 대신 화면의 옵션 단추를 씁니다.
  await clickSpot(page, 'menu')
  await pass(page, 500)
  await clickSpot(page, 'menu:options')
  await pass(page, 600)
  await clickSpot(page, 'option:tab:video')
  await pass(page, 500)
  await shot(page, 'options-video')
  await clickSpot(page, 'option:uiTheme:navy')
  await pass(page, 600)
  await shot(page, 'theme-swap-panel')
  await page.keyboard.press('Escape')
  await pass(page, 600)
  await shot(page, 'theme-swap')
  await page.close()
}

/** 겉면 하나를 저장소에 적고 켜서 판을 엽니다. */
async function lookOf(browser: Browser, surface: string): Promise<void> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  watch(page)
  await skipLogin(page)
  await page.goto(HOME, { waitUntil: 'domcontentloaded' })
  await page.evaluate(name => {
    localStorage.setItem('clover.options', JSON.stringify({ uiTheme: name }))
  }, surface)
  await page.goto(SEED, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  await pass(page, 400)
  // **두 장을 고릅니다.** 아무것도 고르지 않은 화면에서는 「낸다」와 「버린다」가 잠겨 있어
  // 나아가는 단추와 되돌릴 수 없는 단추의 색이 한 장에도 나오지 않습니다.
  for (const x of [474, 565]) {
    const spot = await at(page, x, 610)
    await page.mouse.click(spot.x, spot.y)
    await pass(page, 250)
  }
  await pass(page, 400)
  await shot(page, `theme-${surface}`)
  await page.close()
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  await swapsInPlace(browser)
  for (const surface of SURFACES) await lookOf(browser, surface)

  await browser.close()
  await server.close()
  // **로그인 서버가 없는 기계에서는 그 실패를 세지 않습니다.** 겉면을 보는 도구이고,
  // 서버를 띄우지 않은 자리에서 붉게 뜨면 그 도구를 아무도 돌리지 않습니다.
  const real = problems.filter(one => !/auth|500|Failed to load resource/i.test(one))
  if (real.length > 0) {
    console.error(real.join('\n'))
    return 1
  }
  return 0
}

main().then(code => process.exit(code))
