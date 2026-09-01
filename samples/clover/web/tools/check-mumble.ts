// 조커의 웅얼거림이 실제로 소리 길을 타는가.
//
// **소리는 조용히 실패합니다.** WebAudio 는 잘못된 값에 예외를 내는데 그것을 받는 곳이
// 없어서, 소리가 안 나는 것과 예외로 죽은 것을 화면에서 가릴 수 없습니다 — 목소리를 여럿
// 불러 보고 예외가 없는지, 그리고 실제로 노드가 붙는지를 셉니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, clickPrimary, settle, STAGE_W, TITLE_START_Y } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5217

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error') problems.push(message.text())
  })
  page.on('pageerror', error => problems.push(String(error)))

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-MUMBLE1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  // **소리 길은 누른 뒤에 열립니다.** 시작을 눌러 그것을 엽니다.
  const start = await at(page, STAGE_W / 2, TITLE_START_Y)
  await page.mouse.click(start.x, start.y)
  await page.waitForTimeout(900)
  await page.mouse.click(20, 20)
  await page.waitForTimeout(400)
  await clickPrimary(page)
  await settle(page)

  // 만들어지는 노드를 셉니다.
  const made = await page.evaluate(() => {
    const box = { osc: 0, filter: 0 }
    const proto = AudioContext.prototype as unknown as {
      createOscillator(): OscillatorNode
      createBiquadFilter(): BiquadFilterNode
    }
    const oldOsc = proto.createOscillator
    const oldFilter = proto.createBiquadFilter
    proto.createOscillator = function (this: AudioContext) {
      box.osc++
      return oldOsc.call(this)
    }
    proto.createBiquadFilter = function (this: AudioContext) {
      box.filter++
      return oldFilter.call(this)
    }

    const hook = (window as unknown as { __clover: { mumble?(v: number): void } }).__clover
    // 목소리 여덟. **음절 수가 목소리마다 다르므로 여러 번 불러야 갈래를 다 지납니다.**
    for (let voice = 1; voice <= 8; voice++) hook.mumble?.(voice)

    proto.createOscillator = oldOsc
    proto.createBiquadFilter = oldFilter
    return box
  })

  console.log('목청', made.osc, '· 입', made.filter)
  if (problems.length > 0) {
    console.log('오류:')
    for (const one of problems.slice(0, 6)) console.log('  ' + one)
  }

  await browser.close()
  await server.close()
  const good = made.osc >= 8 && made.filter >= 8 && problems.length === 0
  console.log(good ? '웅얼거립니다' : '어긋납니다')
  return good ? 0 : 1
}

main().then(code => process.exit(code))
