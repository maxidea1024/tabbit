// 게임이 끝난 뒤 「다시 시작」 이 정말 다시 서는가.
//
// **콘솔의 오류와 멈춤을 함께 봅니다** — 창이 뜨는 것과 그 안이 다시 도는 것은 다른 일입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickPrimary, peek, pressPlay, pickCards, settle, STAGE_W, skipLogin, TITLE_START, pass,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: 5201 } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', error => problems.push(String(error)))

  await page.goto('http://localhost:5201/?seed=CLOVER-LOSE1&tick=manual', { waitUntil: 'networkidle' })
  await pass(page, 1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await pass(page, 900)
  await page.mouse.click(20, 20)
  await pass(page, 400)
  await clickPrimary(page)
  await settle(page)

  // 한 장씩만 내서 집니다.
  for (let turn = 0; turn < 12; turn++) {
    const state = await peek(page)
    if (state.phase !== 'round') break
    await pickCards(page, [0])
    await pressPlay(page)
    await settle(page)
    await pass(page, 200)
  }

  await pass(page, 2200)
  const after = await peek(page)
  console.log('국면', after.phase)
  await page.screenshot({ path: path.join(OUT, 'restart-1.png') })

  if (after.phase !== 'lost') {
    console.log('지지 않았습니다 — 확인할 수 없습니다')
    await browser.close()
    await server.close()
    return 1
  }

  // 「다시 시작」. 판의 가운데에서 왼쪽으로 92, 아래로 126. 그 오른쪽이 「타이틀로」입니다.
  const again = await at(page, STAGE_W / 2 - 92, 400 + 126)
  await page.mouse.click(again.x, again.y)

  // 다시 서는 데 얼마나 걸리는가.
  const began = Date.now()
  let back = false
  for (let i = 0; i < 40; i++) {
    await pass(page, 500)
    try {
      const now = await peek(page)
      if (now.phase === 'blind-select' || now.phase === 'round') {
        back = true
        break
      }
    } catch {
      // 되읽는 중에는 창이 아직 없습니다.
    }
  }
  console.log(back ? `다시 섰습니다 — ${Date.now() - began}ms` : '20초 안에 다시 서지 않았습니다')
  await page.screenshot({ path: path.join(OUT, 'restart-2.png') })

  if (problems.length > 0) {
    console.log('오류:')
    for (const one of problems.slice(0, 8)) console.log('  ' + one)
  }

  await browser.close()
  await server.close()
  return back && problems.length === 0 ? 0 : 1
}

main().then(code => process.exit(code))
