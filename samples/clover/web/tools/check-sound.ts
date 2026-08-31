// 소리 38개가 읽히는가, 그리고 크기가 고르게 맞는가.
//
// **들어 보고 판단할 수 없는 것을 재서 판단합니다.** 꾸러미에서 온 파일은 저마다 음량이
// 달라서, 그대로 두면 어떤 신호는 들리지 않고 어떤 신호는 귀를 찌릅니다 — `audio.ts` 가
// 읽을 때 맞추는 그 배수를 여기서 같은 식으로 재어, 원래 지나치게 크거나 작은 것을
// 이름으로 보고합니다.
import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5206
/** `audio.ts` 의 `TARGET_RMS` 와 같은 값이어야 합니다. */
const TARGET_RMS = 0.09

/** `SoundCue` 표의 신호들. **표가 기준입니다.** */
function cues(): string[] {
  const file = path.resolve(HERE, '../../design-data/data/SoundCue.tsv')
  return fs.readFileSync(file, 'utf-8').split('\n').slice(4)
    .map(line => line.split('\t')[1])
    .filter((one): one is string => Boolean(one))
}

async function main(): Promise<number> {
  const ids = cues()
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage()
  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(800)

  const found = await page.evaluate(async ([list, target]: [string[], number]) => {
    const context = new AudioContext()
    const out: { cue: string; ok: boolean; seconds: number; gain: number;
                 lead: number }[] = []

    for (const cue of list) {
      try {
        const file = await fetch(`./sound/${cue}.ogg`)
        if (!file.ok) {
          out.push({ cue, ok: false, seconds: 0, gain: 0, lead: 0 })
          continue
        }
        const buffer = await context.decodeAudioData(await file.arrayBuffer())
        const wave = buffer.getChannelData(0)
        const span = Math.min(wave.length, Math.floor(buffer.sampleRate * 0.4))
        let sum = 0
        for (let i = 0; i < span; i++) sum += wave[i] * wave[i]
        const rms = Math.sqrt(sum / Math.max(1, span))
        // **앞의 묵음.** 소리가 처음 문턱을 넘는 자리입니다 — 그만큼 늦게 들립니다.
        let lead = 0
        for (let i = 0; i < wave.length; i++) {
          if (Math.abs(wave[i]) > 0.01) { lead = i / buffer.sampleRate; break }
        }
        out.push({
          cue, ok: true,
          seconds: Number(buffer.duration.toFixed(2)),
          gain: Number(Math.max(0.05, Math.min(6, target / Math.max(rms, 1e-5))).toFixed(2)),
          lead: Number((lead * 1000).toFixed(1)),
        })
      } catch {
        out.push({ cue, ok: false, seconds: 0, gain: 0, lead: 0 })
      }
    }
    return out
  }, [ids, TARGET_RMS] as [string[], number])

  const bad = found.filter(one => !one.ok)
  const ok = found.filter(one => one.ok)
  console.log(`신호 ${found.length}개 · 읽힌 것 ${ok.length}개`)
  if (bad.length > 0) console.log('읽지 못한 것: ' + bad.map(one => one.cue).join(' '))

  if (ok.length > 0) {
    const seconds = ok.map(one => one.seconds)
    const gains = ok.map(one => one.gain)
    console.log(`길이 ${Math.min(...seconds)}~${Math.max(...seconds)}초`)
    console.log(`맞추는 배수 ${Math.min(...gains)}~${Math.max(...gains)}배`)

    const long = ok.filter(one => one.seconds > 1.2)
    if (long.length > 0) {
      console.log('1.2초를 넘는 것: '
        + long.map(one => `${one.cue} ${one.seconds}s`).join(' · '))
    }
    const brief = ok.filter(one => one.seconds < 0.06)
    if (brief.length > 0) {
      console.log('0.06초보다 짧은 것: '
        + brief.map(one => `${one.cue} ${one.seconds}s`).join(' · '))
    }
    const leads = ok.map(one => one.lead).sort((a, b) => b - a)
    console.log(`앞의 묵음 ${leads[leads.length - 1]}~${leads[0]}ms`)
    const late = ok.filter(one => one.lead > 8).sort((a, b) => b.lead - a.lead)
    if (late.length > 0) {
      console.log(`8ms 를 넘는 것 ${late.length}개: `
        + late.map(one => `${one.cue} ${one.lead}ms`).join(' · '))
    }
    const loud = ok.filter(one => one.gain <= 0.2)
    if (loud.length > 0) console.log('원래 아주 큰 것: ' + loud.map(one => one.cue).join(' '))
    const faint = ok.filter(one => one.gain >= 5)
    if (faint.length > 0) console.log('원래 아주 작은 것: ' + faint.map(one => one.cue).join(' '))
  }

  await browser.close()
  await server.close()
  return bad.length === 0 ? 0 : 1
}

main().then(code => process.exit(code))
