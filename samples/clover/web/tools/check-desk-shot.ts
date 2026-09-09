// 데스크탑 앱(ANGLE/D3D11)에서 **구운 화면에 빈 자리가 있는가.**
//
// 브라우저와 데스크탑은 같은 코드가 다른 드라이버 위에서 돕니다 — 스텐실을 붙이는 자리가
// 그 둘에서 갈렸고, 검은 구멍은 데스크탑 쪽에서만 보였습니다. **그래서 여기서 한 번 잽니다.**
//
//     cd samples/clover/web && CLOVER_DEV_HOOKS=1 npx vite build
//     cd ../desktop && (env -u ELECTRON_RUN_AS_NODE node run.cjs --remote-debugging-port=9222 &)
//     cd ../web && npx tsx tools/check-desk-shot.ts
import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { openRun, pass, peek } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/desk')

async function main(): Promise<number> {
  await fs.mkdir(OUT, { recursive: true })
  const browser = await chromium.connectOverCDP('http://127.0.0.1:9222')
  const page = browser.contexts().flatMap(one => one.pages())
    .find(one => !one.url().startsWith('devtools:'))
  if (!page) { console.log('  창이 없습니다'); return 1 }
  page.on('pageerror', error => console.log('  [터짐]', error.message))
  await page.addInitScript('window.__cloverGuest = true')
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2500)

  const gl = await page.evaluate(`(() => {
    const cv = document.createElement('canvas')
    const g = cv.getContext('webgl2') || cv.getContext('webgl')
    const d = g && g.getExtension('WEBGL_debug_renderer_info')
    return d ? g.getParameter(d.UNMASKED_RENDERER_WEBGL) : '모름'
  })()`)
  console.log('  그리는 것', gl)

  await openRun(page)
  await page.evaluate(`window.__clover.grantJoker(3, 1)`)
  await pass(page, 1200)
  await page.evaluate(`window.__clover.loseRound()`)
  for (let i = 0; i < 60; i++) {
    if ((await peek(page)).gameOver) break
    await pass(page, 300)
  }
  await pass(page, 2000)
  const now = await peek(page)
  console.log(`  국면 ${now.phase} · 판 ${now.gameOver} · 조커 ${now.jokers}`)

  for (const old of [true, false]) {
    await page.evaluate(`window.__oldShot = ${old}`)
    const got = await page.evaluate(`window.__clover.shotHoles()`) as
      { holes: number; total: number }
    console.log(`  ${old ? '옛 길(패스 둘)' : '새 길(먼저 붙임)'} · 빈 자리 `
      + `${got.holes} / ${got.total}`)
  }
  await page.evaluate(`window.__oldShot = false`)
  const holes = await page.evaluate(`window.__clover.shotHoles()`) as
    { holes: number; total: number }
  const shot = await page.evaluate(`window.__clover.shotDump()`) as string
  if (shot) await fs.writeFile(path.join(OUT, 'shot.png'), Buffer.from(shot.split(',')[1], 'base64'))
  await page.screenshot({ path: path.join(OUT, 'live.png') })

  await browser.close()
  return holes.holes === 0 ? 0 : 1
}

main().then(code => process.exit(code))
