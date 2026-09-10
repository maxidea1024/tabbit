// 규칙이 바뀐 것이 손패 줄 위의 판으로 뜨는가.
//
// **토스트로 보내면 지나가는 알림으로 읽힙니다.** 조커가 걸고 소모품이 걸고 보스가 거는
// 규칙은 그 판의 셈법을 통째로 바꾸는 것인데, 화면 오른쪽 구석의 작은 글 두 줄이 전부였고
// 「어, 뭐가 바뀐 거지」가 그것이었습니다.
//
// 규칙을 거는 조커 하나를 사서 잽니다. 사는 것이 곧 규칙이 걸리는 것입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { grantJoker, openRun, pass, peek, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5234

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') console.log('  [콘솔]', one.text().slice(0, 200))
  })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-RULE1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  await pass(page, 400)

  // **손패의 크기를 늘리는 조커입니다.** 들리는 순간 규칙이 다시 세워지고, 그때 실제로
  // 달라진 것 하나가 나옵니다.
  const handBefore = (await peek(page)).hand.length
  await grantJoker(page, 'wide_bed')

  let box: { x: number; y: number; width: number; height: number } | undefined
  let seen = 0
  const heard = new Set<string>()
  for (let i = 0; i < 100; i++) {
    const now = await peek(page)
    if (now.ruleBanner) {
      seen++
      box = box ?? now.ruleBanner
    }
    for (const cue of now.sounds ?? []) heard.add(cue)
    await pass(page, 40)
  }

  const after = await peek(page)
  console.log('판이 떠 있던 표본', seen, '/ 100')
  console.log('판의 자리', box ? `${box.x},${box.y} ${box.width}×${box.height}` : '없음')
  console.log('난 소리', [...heard].join(' · ') || '없음')
  console.log('손패', handBefore, '→', after.hand.length)

  // **손패 줄 위 가운데입니다.** 손패는 608 에 가운데가 있고 카드 높이가 124 이므로
  // 윗변이 546 입니다 — 판의 아랫변이 그보다 위여야 손패를 덮지 않습니다.
  const above = box !== undefined && box.y + box.height <= 546
  // 가운데에 섰는가. 판 가운데는 화면의 가운데가 아닙니다(왼쪽에 판때기가 있습니다).
  const middle = box !== undefined && Math.abs(box.x + box.width / 2 - 782) <= 40
  console.log('손패 위에 섰는가', above, '· 가운데인가', middle)

  const good = seen > 0 && above && middle && (after.ruleBanner === undefined)
  console.log(good ? '규칙 변경이 판으로 뜹니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
