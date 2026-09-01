// 태그 칩이 선 자리가 흔들리지 않는가.
//
// **「잠깐 왼쪽 위로 튄다」는 프레임 몇 개짜리입니다.** 눈으로는 어느 프레임에 어디였는지를
// 말할 수 없고, 말할 수 없으면 고쳤는지도 말할 수 없습니다 — 실제로 고쳤다가 재발했습니다.
//
// 개발 서버의 손잡이로 태그를 하나 쥐어 주고, 그 뒤 2초를 프레임마다 재어 **자리가 한 번도
// 바뀌지 않는지**를 봅니다. 발동으로 크기는 바뀌지만 자리는 그대로여야 합니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, peek, STAGE_W, TITLE_START_Y } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5242

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TAGSPOT`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)
  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(1600)

  // 태그를 쥐어 줍니다. **개발 서버에서만 있는 손잡이입니다.**
  await page.evaluate(() => {
    const hook = (window as unknown as { __clover: { grantActive?(): void } }).__clover
    hook.grantActive?.()
  })

  const seen: string[] = []
  for (let i = 0; i < 40; i++) {
    await page.waitForTimeout(50)
    const spots = (await peek(page)).tagAt ?? []
    const mark = spots.map(one => `${one.x},${one.y}`).join(' | ')
    if (mark !== seen[seen.length - 1]) seen.push(mark)
  }

  for (const one of seen) console.log('  ' + (one === '' ? '(없음)' : one))

  await browser.close()
  await server.close()
  // 없다가 생기는 것은 한 번 바뀝니다. **생긴 뒤로는 그대로여야 합니다.**
  const after = seen.filter(one => one !== '')
  const steady = new Set(after).size <= 1
  console.log(steady ? '자리가 흔들리지 않습니다' : `자리가 ${new Set(after).size}가지로 흔들립니다`)
  return steady ? 0 : 1
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
