// 판에 들어가기 전의 두 화면. **로그인 화면과 타이틀만 굽습니다.**
//
// 그 둘의 모습을 고치는 동안 쓰는 도구입니다 — 전체를 굽는 `shoot-look.ts` 는 판을 돌리며
// 스무 장을 만들고, 그 사이에 화면 하나를 고쳐 보는 것이 한 번에 몇 분입니다.
//
//     npx tsx tools/shoot-front.ts
//
// 콘솔 오류가 하나라도 있으면 실패로 끝냅니다.

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'

import { pass, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5237

const problems: string[] = []

async function open(browser: Browser, query: string, login: boolean): Promise<Page> {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('console', message => { if (message.type() === 'error') problems.push(message.text()) })
  page.on('pageerror', error => problems.push(String(error)))
  if (!login) await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/${query}`, { waitUntil: 'networkidle' })
  await pass(page, 1600)
  return page
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  // **로그인은 건너뛰지 않습니다.** 그 화면을 보려는 것이므로 여기서만 그대로 둡니다.
  const login = await open(browser, '?tick=manual', true)
  await login.screenshot({ path: path.join(OUT, 'front-login.png') })

  // 진행 띠. **뒤가 흐려진 그 상태를 확인합니다** — 「로그인 없이 시작」이 그 띠를 지납니다.
  // **자리를 수로 적습니다.** 이 화면은 도구가 짚을 자리를 알리지 않고, 「로그인 없이
  // 시작」은 판의 가로 가운데에 자리가 고정입니다(`login-scene.ts` 의 `singleY`).
  await login.mouse.click(640, 612)
  await pass(login, 400)
  await login.screenshot({ path: path.join(OUT, 'front-band.png') })
  await login.close()

  const title = await open(browser, '?seed=CLOVER-SHOT6&tick=manual', false)
  await title.screenshot({ path: path.join(OUT, 'front-title.png') })
  await title.close()

  await browser.close()
  await server.close()
  if (problems.length > 0) {
    console.error(problems.join('\n'))
    return 1
  }
  console.log('front-login.png · front-title.png')
  return 0
}

main().then(code => process.exit(code))
