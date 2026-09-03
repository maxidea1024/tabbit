// 씬 셋이 순서대로 도는가.
//
// **데이터 로딩 → 타이틀 → 판**, 그리고 판에서 타이틀로 돌아올 때 로딩이 다시 보이지
// 않는가. 예전에는 돌아가는 길이 페이지를 다시 읽는 것이어서, 접을 때마다 데이터·글꼴·
// 그림을 처음부터 읽었습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, clickPrimary, clickSpot, peek, pickCards, pressPlay, settle, skipLogin, TITLE_START, pass,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5207

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', error => problems.push(String(error)))

  // **데이터를 몇 번 읽는가.** 타이틀로 돌아가며 다시 읽으면 이 수가 늘어납니다.
  let reads = 0
  page.on('request', request => {
    if (request.url().includes('/data/')) reads++
  })

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-LOSE1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  const bootAfterLoad = await bootText(page)
  // 첫 화면이 서기까지 데이터를 읽은 횟수. **접었다 편 뒤에도 이 수가 그대로여야 합니다.**
  const firstReads = reads
  const first = await peek(page)
  console.log('처음 씬', first.scene, '· 로딩 줄', bootAfterLoad ? '보임' : '걷힘')

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await pass(page, 900)
  await page.mouse.click(20, 20)
  await pass(page, 400)
  const inRun = await peek(page)
  console.log('시작 뒤 씬', inRun.scene)

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
  await pass(page, 2400)
  const lost = await peek(page)
  if (lost.phase !== 'lost') {
    console.log('지지 않았습니다 — 확인할 수 없습니다')
    await browser.close()
    await server.close()
    return 1
  }
  await page.screenshot({ path: path.join(OUT, 'scene-1.png') })

  // 「타이틀로」. **자리는 화면이 알립니다** — 끝난 판의 높이가 랭크 런인지에 따라 달라집니다.
  await clickSpot(page, 'home')
  await pass(page, 1400)

  const back = await peek(page)
  const bootBack = await bootText(page)
  console.log('돌아온 씬', back.scene, '· 남은 카드 뷰', back.views,
    '· 로딩 줄', bootBack ? `보임 (${bootBack})` : '걷힘')
  await page.screenshot({ path: path.join(OUT, 'scene-2.png') })

  // 다시 시작해서 판이 서는가.
  const again = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(again.x, again.y)
  await pass(page, 1200)
  const second = await peek(page)
  console.log('두 번째 판', second.scene, '· 국면', second.phase, '· 시드가 바뀌었는가',
    second.seed !== first.seed ? '예' : '아니오')
  await page.screenshot({ path: path.join(OUT, 'scene-3.png') })

  console.log('데이터를 읽은 횟수 — 처음', firstReads, '· 지금까지', reads)
  if (problems.length > 0) {
    console.log('오류:')
    for (const one of problems.slice(0, 8)) console.log('  ' + one)
  }

  const good = back.scene === 'title' && back.views === 0 && !bootBack
    && second.scene === 'run' && reads === firstReads && problems.length === 0
  console.log(good ? '씬이 순서대로 돕니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

/** 로딩 줄이 지금 보이는가. 걷혔으면 빈 문자열입니다. */
async function bootText(page: import('playwright').Page): Promise<string> {
  return page.evaluate(() => {
    const node = document.getElementById('boot')
    if (!node || node.className.includes('gone')) return ''
    return node.textContent ?? ''
  })
}

main().then(code => process.exit(code))
