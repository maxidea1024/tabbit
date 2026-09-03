// 소리가 어디서 비는가.
//
// **들어 보고 「비는 것 같다」로는 고칠 수 없습니다.** 한 판을 돌리며 소리가 난 시각을
// 적고, 그 사이가 크게 빈 자리를 찾아 **그때 무엇을 기다리고 있었는지**까지 적습니다 —
// 기다리는 것이 다음 국면이면 비어 있어도 되고, 화면에서 무언가 움직이고 있었다면
// 그 자리에 소리가 빠진 것입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { at, chooseFive, peek, pickCards, pressPlay, STAGE_W, skipLogin, pass } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5215
/** 이보다 크게 비면 이름과 함께 적습니다. */
const HOLE_MS = 400
/** 낸 뒤 몇 초를 보는가. */
const WATCH_MS = 8000

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-SOUND2&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1800)

  await tap(page, STAGE_W / 2, 473)
  await pass(page, 1100)
  await tap(page, 20, 20)
  await pass(page, 700)
  const pick = (await peek(page)).spots?.pick
  if (pick) await tap(page, pick.x, pick.y)
  await pass(page, 2600)

  const hand = (await peek(page)).hand
  await pickCards(page, chooseFive(hand))
  await pass(page, 300)

  // 소리가 난 시각과, 100밀리초마다 무엇을 기다리는지.
  await page.evaluate(watch => {
    const one = window as unknown as {
      __sound: number[]; __wait: [number, string][]; __zero: number
      __clover?: { coming?: string }
    }
    one.__sound = []
    one.__wait = []
    one.__zero = performance.now()
    const began = AudioBufferSourceNode.prototype.start
    AudioBufferSourceNode.prototype.start = function (...args: never[]) {
      one.__sound.push(Math.round(performance.now() - one.__zero))
      return began.apply(this, args as never)
    }
    const made = AudioContext.prototype.createOscillator
    AudioContext.prototype.createOscillator = function (...args: never[]) {
      one.__sound.push(Math.round(performance.now() - one.__zero))
      return made.apply(this, args as never)
    }
    const timer = setInterval(() => {
      one.__wait.push([Math.round(performance.now() - one.__zero),
        one.__clover?.coming ?? '-'])
      if (performance.now() - one.__zero > watch) clearInterval(timer)
    }, 100)
  }, WATCH_MS)

  await pressPlay(page)
  await pass(page, WATCH_MS + 500)

  const { sound, wait } = await page.evaluate(() => {
    const one = window as unknown as { __sound: number[]; __wait: [number, string][] }
    return { sound: one.__sound, wait: one.__wait }
  })

  console.log(`낸 뒤 ${WATCH_MS / 1000}초 동안 소리 ${sound.length}번`)

  const holes: string[] = []
  for (let i = 1; i < sound.length; i++) {
    const span = sound[i] - sound[i - 1]
    if (span < HOLE_MS) continue
    // 그 사이에 무엇을 기다리고 있었는가.
    const middle = (sound[i - 1] + sound[i]) / 2
    const near = wait.reduce((best, one) =>
      Math.abs(one[0] - middle) < Math.abs(best[0] - middle) ? one : best, wait[0] ?? [0, '-'])
    holes.push(`${span}ms (${sound[i - 1]}→${sound[i]}, ${near[1]} 기다림)`)
  }
  console.log(holes.length === 0
    ? `${HOLE_MS}ms 를 넘게 비는 자리 없음`
    : `${HOLE_MS}ms 를 넘게 비는 자리 ${holes.length}군데\n  ` + holes.join('\n  '))

  if (errors.length > 0) console.log('오류: ' + errors.slice(0, 3).join(' | '))
  await browser.close()
  await server.close()
  return errors.length === 0 ? 0 : 1
}

/** 한 번 누릅니다. `mouse.click` 은 너무 빨라 `pointertap` 이 서지 않는 자리가 있습니다. */
async function tap(page: Page, x: number, y: number): Promise<void> {
  const spot = await at(page, x, y)
  await page.mouse.move(spot.x, spot.y)
  await pass(page, 80)
  await page.mouse.down()
  await pass(page, 55)
  await page.mouse.up()
}

main().then(code => process.exit(code))
