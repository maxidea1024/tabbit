// 상점이 처음 설 때 소리가 몇 번 나는가.
//
// **한동안 24번이었습니다.** 세우는 것마다 하나씩 냈고 — 칸 7 · 물건 7 · 값 7 · 구획 머리
// 3 — 그 음원이 0.689초라 여섯이 함께 울렸습니다. 물건이 앉을 때만 남겨 그 수가 절반
// 아래로 내려갔고, 남은 것은 왼쪽부터 한 칸씩 오릅니다.
//
// **소리 하나가 마디를 여럿 만들므로 묶어 셉니다.** 8밀리초 안에 시작한 것은 한 소리이고,
// 음원 없이 부분음만 셋인 것이 진열의 음입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { openRun, pass, skipLogin, winRound, shopStanding } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5231
/**
 * 진열이 낼 수 있는 소리의 수.
 *
 * **정확한 수를 적지 않습니다.** 상점의 칸 수는 안테와 바우처가 정하므로 시드마다
 * 다릅니다 — 지켜야 하는 것은 「세우는 것마다 하나씩」 으로 돌아가지 않는 것입니다.
 */
const NOTES_MOST = 10
/** 아래로도 봅니다. 0 이면 진열에 소리가 없어진 것입니다. */
const NOTES_LEAST = 3

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-STOCK1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  // **소리 길은 사람이 누른 뒤에 열립니다.** 훅으로 이기면 열리지 않습니다.
  await openRun(page)
  await pass(page, 600)

  await page.evaluate(() => {
    const one = window as unknown as { __starts: [string, number][]; __zero: number }
    one.__starts = []
    one.__zero = performance.now()
    const made = AudioContext.prototype.createOscillator
    AudioContext.prototype.createOscillator = function (...args: never[]) {
      one.__starts.push(['o', Math.round(performance.now() - one.__zero)])
      return made.apply(this, args as never)
    }
    const began = AudioBufferSourceNode.prototype.start
    AudioBufferSourceNode.prototype.start = function (...args: never[]) {
      one.__starts.push(['b', Math.round(performance.now() - one.__zero)])
      return began.apply(this, args as never)
    }
    const one2 = window as unknown as { __phase: [number, string][]; __zero: number
      __clover?: { phase?: string } }
    one2.__phase = []
    setInterval(() => {
      one2.__phase.push([Math.round(performance.now() - one2.__zero),
        one2.__clover?.phase ?? '-'])
    }, 60)
  })

  await winRound(page)
  await shopStanding(page)
  await pass(page, 3000)

  const { starts, phase } = await page.evaluate(() => {
    const one = window as unknown as {
      __starts: [string, number][]; __phase: [number, string][]
    }
    return { starts: one.__starts, phase: one.__phase }
  })
  // 상점이 된 시각부터 셉니다.
  const began = phase.find(one => one[1] === 'shop')?.[0] ?? 0
  const after = starts.filter(one => one[1] >= began && one[1] < began + 5000)
  console.log(`상점 국면이 된 시각 ${began}ms`)

  // 마디를 묶어 「소리 한 번」으로 셉니다. 8ms 안에 시작한 것은 한 소리입니다.
  const hits: { at: number; osc: number; buf: number }[] = []
  for (const [kind, at] of after) {
    const last = hits[hits.length - 1]
    if (!last || at - last.at > 8) hits.push({ at, osc: 0, buf: 0 })
    const now = hits[hits.length - 1]
    if (kind === 'o') now.osc++
    else now.buf++
  }
  // **음원 없이 부분음만 있는 것이 진열의 음입니다.** 마림바는 부분음 셋입니다.
  const notes = hits.filter(one => one.buf === 0 && one.osc === 3)
  console.log(`상점이 서는 동안 소리 ${hits.length}번 · 그중 진열의 음 ${notes.length}번`)
  console.log('진열의 음: ' + notes.map(one => one.at).join(' '))
  console.log('전부: ' + hits.map(one => `${one.at}(o${one.osc}b${one.buf})`).join(' '))

  const good = notes.length >= NOTES_LEAST && notes.length <= NOTES_MOST
  console.log(good ? '진열이 하나씩 오릅니다'
    : `진열의 음이 ${NOTES_LEAST}~${NOTES_MOST}번을 벗어났습니다`)

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
