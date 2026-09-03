// 배경음 3곡이 브라우저에서 읽히는가, 그리고 한 바퀴 도는 자리가 들리는가.
//
// **`music.py` 가 재는 것과 다른 것을 잽니다.** 저쪽은 libsndfile 로 굽기 직전의 파형을
// 보지만, 게임에서 소리를 내는 것은 브라우저의 `decodeAudioData` 입니다 — 굽힌 것이 그쪽에서
// 그대로 풀리는지는 그쪽에서 풀어 보아야 압니다.
//
// 보는 것 셋입니다. **앞뒤에 무음이 없어야 하고**, 마지막 표본과 첫 표본이 튀지 않아야 하고,
// 한 바퀴 도는 자리의 음량 층이 그 곡 자신의 기복 안에 들어야 합니다. 셋째가 있는 이유는,
// 층이 3dB 라도 그 곡이 원래 마디마다 5dB 씩 오르내리면 거기만 들리지는 않기 때문입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5207
/** `music.ts` 가 읽는 이름들. **이름이 곧 그 화면입니다.** */
const NAMES = ['title', 'round', 'shop']
/** 앞뒤 무음이 이보다 길면 한 바퀴마다 그 자리가 들립니다. */
const SILENCE_MS = 20

interface Report {
  name: string
  ok: boolean
  seconds: number
  peak: number
  lead: number
  trail: number
  /** 한 바퀴 도는 자리의 음량 층. */
  seam: number
  /** 그 곡이 원래 오르내리는 폭. 층을 이것과 견줍니다. */
  usual: number
  /** 마지막 표본과 첫 표본의 차이를, 그 곡의 표본 층과 견준 것. */
  step: number
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage()
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'networkidle' })

  const found: Report[] = await page.evaluate(async (list: string[]) => {
    const context = new AudioContext()
    const out: Report[] = []
    const blank = { seconds: 0, peak: 0, lead: 0, trail: 0, seam: 0, usual: 0, step: 0 }

    for (const name of list) {
      try {
        const file = await fetch(`./music/${name}.ogg`)
        if (!file.ok) { out.push({ name, ok: false, ...blank }); continue }
        const buffer = await context.decodeAudioData(await file.arrayBuffer())
        const wave = buffer.getChannelData(0)
        const rate = buffer.sampleRate

        let peak = 0
        for (let i = 0; i < wave.length; i++) peak = Math.max(peak, Math.abs(wave[i]))
        const quiet = peak * 0.005
        let lead = 0
        for (let i = 0; i < wave.length; i++) {
          if (Math.abs(wave[i]) > quiet) { lead = i / rate; break }
        }
        let trail = 0
        for (let i = wave.length - 1; i >= 0; i--) {
          if (Math.abs(wave[i]) > quiet) { trail = (wave.length - 1 - i) / rate; break }
        }

        // 0.2초씩 끊어 음량을 재고, 첫 토막과 마지막 토막의 층을 봅니다.
        const span = Math.floor(rate * 0.2)
        const loud: number[] = []
        for (let at = 0; at + span <= wave.length; at += span) {
          let sum = 0
          for (let i = at; i < at + span; i++) sum += wave[i] * wave[i]
          loud.push(20 * Math.log10(Math.sqrt(sum / span) + 1e-9))
        }
        const steps = []
        for (let i = 1; i < loud.length; i++) steps.push(Math.abs(loud[i] - loud[i - 1]))
        steps.sort((a, b) => a - b)

        // 표본 하나의 층. 이것이 그 곡의 여느 층보다 크면 한 바퀴마다 「딱」 소리가 납니다.
        const jumps = []
        for (let i = 1; i < wave.length; i++) jumps.push(Math.abs(wave[i] - wave[i - 1]))
        jumps.sort((a, b) => a - b)
        const usual = jumps[Math.floor(jumps.length * 0.999)]

        out.push({
          name, ok: true,
          seconds: Number(buffer.duration.toFixed(2)),
          peak: Number(peak.toFixed(3)),
          lead: Number((lead * 1000).toFixed(1)),
          trail: Number((trail * 1000).toFixed(1)),
          seam: Number(Math.abs(loud[0] - loud[loud.length - 1]).toFixed(1)),
          usual: Number(steps[Math.floor(steps.length * 0.9)].toFixed(1)),
          step: Number((Math.abs(wave[0] - wave[wave.length - 1]) / (usual + 1e-9)).toFixed(2)),
        })
      } catch {
        out.push({ name, ok: false, ...blank })
      }
    }
    return out
  }, NAMES)

  const bad = found.filter(one => !one.ok)
  console.log(`곡 ${found.length}개 · 읽힌 것 ${found.length - bad.length}개`)
  if (bad.length > 0) console.log('읽지 못한 것: ' + bad.map(one => one.name).join(' '))

  const trouble: string[] = []
  for (const one of found.filter(report => report.ok)) {
    console.log(`${one.name.padEnd(6)} ${one.seconds}초  봉우리 ${one.peak}  `
      + `앞 ${one.lead}ms  뒤 ${one.trail}ms  이음매 ${one.seam}dB (그 곡 ${one.usual}dB)  `
      + `표본 층 ${one.step}배`)
    if (one.lead > SILENCE_MS || one.trail > SILENCE_MS) {
      trouble.push(`${one.name}: 앞뒤에 무음이 있습니다`)
    }
    if (one.seam > one.usual) trouble.push(`${one.name}: 이음매의 음량 층이 그 곡의 기복보다 큽니다`)
    if (one.step > 1) trouble.push(`${one.name}: 이음매에서 표본이 튑니다`)
    if (one.peak > 0.99) trouble.push(`${one.name}: 봉우리가 깎일 자리입니다`)
  }
  for (const one of trouble) console.log(one)

  await browser.close()
  await server.close()
  return bad.length === 0 && trouble.length === 0 ? 0 : 1
}

main().then(code => process.exit(code))
