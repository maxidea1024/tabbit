// 조커의 악기가 실제로 소리 길을 타는가, 그리고 조커마다 갈리는가.
//
// **소리는 조용히 실패합니다.** WebAudio 는 잘못된 값에 예외를 내는데 그것을 받는 곳이
// 없어서, 소리가 안 나는 것과 예외로 죽은 것을 화면에서 가릴 수 없습니다 — 조커를 여럿
// 불러 보고 예외가 없는지, 그리고 실제로 노드가 붙는지를 셉니다.
//
// **갈리는지도 봅니다.** 음색이 조커를 가리키는 것이므로, 스물을 불렀는데 부분음의 수가
// 한 가지뿐이면 그것은 음색이 하나라는 뜻입니다 — 소리는 나지만 아무것도 가리키지
// 않습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { openRun, pass, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5217

/**
 * 봐도 되는 콘솔 오류.
 *
 * **계정 서버가 없습니다.** 이 도구는 개발 서버만 띄우므로 타이틀이 `/auth/providers` 를
 * 조회할 때 500 이 돌아옵니다 — 소리를 보는 도구이므로 그것은 넘깁니다.
 */
function noise(line: string): boolean {
  return line.includes('500 (Internal Server Error)') || line.includes('/auth/')
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error' && !noise(message.text())) problems.push(message.text())
  })
  page.on('pageerror', error => { if (!noise(String(error))) problems.push(String(error)) })

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-VOICE1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  // **소리 길은 누른 뒤에 열립니다.** 시작을 눌러 그것을 엽니다.
  await openRun(page)

  // 만들어지는 노드를 셉니다. **조커마다 몇 개인지가 곧 음색입니다.**
  const made = await page.evaluate(() => {
    let osc = 0
    const shapes = new Set<string>()
    const proto = AudioContext.prototype as unknown as {
      createOscillator(): OscillatorNode
    }
    const oldOsc = proto.createOscillator
    proto.createOscillator = function (this: AudioContext) {
      osc++
      return oldOsc.call(this)
    }

    const hook = (window as unknown as {
      __clover: { jokerVoice?(uid: number): void }
    }).__clover
    // 조커 스물. **음색이 다섯 가지이므로 그만큼은 불러야 다 지납니다.**
    for (let uid = 1; uid <= 20; uid++) {
      const before = osc
      hook.jokerVoice?.(uid)
      shapes.add(String(osc - before))
    }

    proto.createOscillator = oldOsc
    return { osc, kinds: shapes.size }
  })

  console.log('목청', made.osc, '· 갈린 음색', made.kinds, '가지')
  if (problems.length > 0) {
    console.log('오류:')
    for (const one of problems.slice(0, 6)) console.log('  ' + one)
  }

  await browser.close()
  await server.close()
  // 부분음이 한 개인 음색(`pluck`)이 있으므로 갈래가 다섯이어도 개수는 넷까지 겹칩니다.
  const good = made.osc >= 20 && made.kinds >= 3 && problems.length === 0
  console.log(good ? '조커마다 갈립니다' : '어긋납니다')
  return good ? 0 : 1
}

main().then(code => process.exit(code))
