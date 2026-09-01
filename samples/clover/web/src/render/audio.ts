// 소리.
//
// **녹음된 것을 쓰고, 합성은 바탕으로 남습니다.** 종이가 스치는 소리와 칩이 부딪히는 소리는
// 물리적으로 복잡해서 오실레이터 하나로는 되지 않습니다 — `public/sound/<신호>.ogg` 가 있으면
// 그것을 내고, 없거나 읽지 못하면 아래의 파형으로 냅니다. 소리가 아예 안 나는 것보다는
// 전자음이라도 나는 편이 낫습니다.
//
// 음높이가 값을 따라 오르는 것은 **재생 속도**로 합니다. 칩이 하나씩 더해질 때마다 음이
// 오르는 그 소리가 원작의 그것이고, 녹음된 소리로도 됩니다.
//
// **크기는 스스로 맞춥니다.** 꾸러미에서 온 파일은 저마다 음량이 달라서, 그대로 두면 어떤
// 것은 들리지 않고 어떤 것은 귀를 찌릅니다 — 읽을 때 실효값을 재서 한 크기로 맞추고, 뜻으로
// 키우거나 줄이는 것만 `SoundCue.gain` 에 둡니다.
//
// 원작의 음원을 쓰지 않습니다. 가져온 것은 CC0 이고 `public/sound/readme.md` 에 적혀 있습니다.

import type { CloverData } from '../generated/clover-data'
import { Music } from './music'

/**
 * 배경음의 곡들.
 *
 * **파일 이름이 곧 그 화면입니다** — 효과음이 신호의 이름을 쓰는 것과 같습니다.
 */
const MUSIC = ['title', 'round', 'shop'] as const

const BASE_HZ = 220

/**
 * 맞추는 크기. 실효값이 이것이 되도록 곱합니다.
 *
 * **꾸러미의 파일은 저마다 음량이 다릅니다.** 카드가 스치는 소리는 작고 확인음은 큽니다 —
 * 그것을 그대로 두면 어떤 신호는 안 들리고 어떤 신호는 찌릅니다.
 */
const TARGET_RMS = 0.09
/** 맞추는 정도의 상한과 하한. 거의 빈 파일이 폭발하지 않게 합니다. */
const GAIN_RANGE = [0.05, 6] as const

/**
 * 겹친 것으로 세는 시간.
 *
 * **소리 하나가 그만큼 남아 있다고 봅니다.** 카드 소리는 0.1초 남짓이고, 잇달아 나는
 * 것들의 사이가 그보다 짧으면 귀에는 한 덩어리입니다.
 */
const CROWD_SPAN = 0.18
/** 이 수를 넘으면 내지 않습니다. */
const CROWD_MOST = 4

export class Audio {
  private context?: AudioContext
  private master?: GainNode
  private readonly follows = new Map<string, boolean>()
  /** 신호마다의 크기. 데이터가 정합니다. */
  private readonly wanted = new Map<string, number>()
  /**
   * 읽어 둔 소리.
   *
   * 없는 신호는 합성으로 갑니다. **읽기를 기다리지 않습니다** — 읽히기 전에 난 소리는
   * 합성으로 나고, 읽힌 다음부터 녹음된 것으로 바뀝니다.
   */
  private readonly samples = new Map<string,
    { buffer: AudioBuffer; gain: number; lead: number }>()
  /**
   * 아직 풀지 않은 소리의 바이트.
   *
   * **받는 것은 누르기를 기다리지 않습니다.** 소리 길은 사람이 무언가를 누른 뒤에만 열리지만
   * 파일을 받는 것은 그 전에 됩니다 — 미리 받아 두지 않으면 첫 두어 소리가 합성으로 나고,
   * 그 둘이 첫인상입니다.
   */
  private bytes?: Promise<Map<string, ArrayBuffer>>
  /**
   * 잡음 한 토막.
   *
   * **카드와 종이와 동전은 음이 아니라 잡음입니다.** 사인파로 만든 「탁」은 어떤 값을 줘도
   * 전자음이고, 카드가 놓이는 소리로 들리지 않습니다.
   *
   * 한 번 만들어 두고 돌려 씁니다 — 소리마다 만들면 그 만드는 값이 소리보다 큽니다.
   */
  private hiss?: AudioBuffer
  /**
   * 신호마다 마지막으로 난 시각들.
   *
   * **같은 소리가 겹치면 커집니다.** 카드 다섯 장이 잇달아 사라질 때 그 소리가 다섯 번
   * 나는데, 소리는 힘으로 더해지므로 다섯이면 하나보다 곱절 넘게 큽니다 — 그것이 「볼륨
   * 게이지가 올라가는」 느낌이고, 그 순간만 화면의 다른 소리를 다 덮습니다.
   */
  private readonly recent = new Map<string, number[]>()

  /** 소리를 끄는가. 옵션이 정합니다. */
  muted = false

  /** 배경음. 효과음과 길은 같고 음량은 따로입니다. */
  readonly music = new Music(MUSIC)

  private level = 0.35

  /**
   * 음량. 0 에서 1 입니다.
   *
   * **이미 열려 있는 소리 길에도 바로 걸립니다** — 값만 두고 다음 소리부터 적용하면, 옵션을
   * 만지는 동안에는 무엇이 바뀌었는지 들리지 않습니다.
   */
  set volume(value: number) {
    this.level = Math.max(0, Math.min(1, value))
    if (this.master) this.master.gain.value = this.level
  }

  get volume(): number {
    return this.level
  }

  constructor(private readonly tables: CloverData) {
    for (const cue of tables.soundCue.records) {
      this.follows.set(cue.cueId, cue.pitchFollowsValue)
      this.wanted.set(cue.cueId, cue.gain)
    }
    this.bytes = this.grab()
  }

  /** 파일을 받아 둡니다. 푸는 것은 소리 길이 열린 뒤입니다. */
  private async grab(): Promise<Map<string, ArrayBuffer>> {
    const out = new Map<string, ArrayBuffer>()
    await Promise.all(this.tables.soundCue.records.map(async cue => {
      try {
        const answer = await fetch(`./sound/${cue.cueId}.ogg`)
        if (answer.ok) out.set(cue.cueId, await answer.arrayBuffer())
      } catch {
        // 없는 것은 합성으로 갑니다.
      }
    }))
    return out
  }

  /** 브라우저는 사람이 무언가를 누른 뒤에만 소리를 냅니다. */
  unlock(): void {
    if (this.context) return
    const Ctor = (window.AudioContext
      ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)
    this.context = new Ctor()
    this.master = this.context.createGain()
    this.master.gain.value = this.level
    this.master.connect(this.context.destination)
    // **배경음도 같은 길을 씁니다.** 소리 길은 하나이고, 음량만 따로입니다.
    this.music.open(this.context, this.context.destination)

    const seconds = 0.5
    const frames = Math.floor(this.context.sampleRate * seconds)
    this.hiss = this.context.createBuffer(1, frames, this.context.sampleRate)
    const wave = this.hiss.getChannelData(0)
    for (let i = 0; i < frames; i++) wave[i] = Math.random() * 2 - 1

    void this.load()
  }

  /**
   * 소리를 읽어 둡니다.
   *
   * **하나가 없어도 나머지는 읽습니다.** 파일 하나가 빠졌다고 소리가 통째로 없어지면,
   * 빠진 것을 찾는 것이 더 어려워집니다.
   */
  private async load(): Promise<void> {
    const context = this.context
    if (!context || !this.bytes) return

    const bytes = await this.bytes
    await Promise.all([...bytes].map(async ([cue, raw]) => {
      try {
        // **한 번만 풉니다.** `decodeAudioData` 는 넘긴 버퍼를 비우므로 베껴 넘깁니다.
        const buffer = await context.decodeAudioData(raw.slice(0))
        this.samples.set(cue, {
          buffer,
          gain: this.levelFor(buffer) * (this.wanted.get(cue) ?? 1),
          lead: this.leadOf(buffer),
        })
      } catch {
        // 풀지 못한 것은 합성으로 갑니다.
      }
    }))
  }

  /**
   * 앞의 묵음이 몇 초인가.
   *
   * **꾸러미의 파일은 앞이 비어 있습니다.** 재어 보면 38개 중 18개가 8밀리초를 넘고
   * 가장 긴 것은 97밀리초입니다 — 그만큼 늦게 들리므로 카드가 놓이는 그림과 어긋납니다.
   * 파일을 고치지 않고 **그 자리부터 재생합니다.**
   */
  private leadOf(buffer: AudioBuffer): number {
    const wave = buffer.getChannelData(0)
    // 앞의 0.3초 안에서만 찾습니다. 그보다 뒤라면 묵음이 아니라 뜸입니다.
    const span = Math.min(wave.length, Math.floor(buffer.sampleRate * 0.3))
    for (let i = 0; i < span; i++) {
      if (Math.abs(wave[i]) > 0.01) return i / buffer.sampleRate
    }
    return 0
  }

  /** 그 소리를 한 크기로 맞추는 배수. 실효값을 재서 정합니다. */
  private levelFor(buffer: AudioBuffer): number {
    const wave = buffer.getChannelData(0)
    // **묵음 다음부터 0.4초를 봅니다.** 앞의 빈 자리까지 세면 그만큼 작게 재어져,
    // 묵음이 긴 파일이 더 크게 나옵니다.
    const from = Math.floor(this.leadOf(buffer) * buffer.sampleRate)
    const span = Math.min(wave.length - from, Math.floor(buffer.sampleRate * 0.4))
    let sum = 0
    for (let i = 0; i < span; i++) sum += wave[from + i] * wave[from + i]
    const rms = Math.sqrt(sum / Math.max(1, span))
    if (rms < 1e-5) return 1
    return Math.max(GAIN_RANGE[0], Math.min(GAIN_RANGE[1], TARGET_RMS / rms))
  }

  /**
   * 소리 하나.
   *
   * `semitones` 는 값의 크기에서 옵니다 — `SoundCue.pitch_follows_value` 가 참인 것만
   * 그것을 씁니다.
   */
  play(cueId: string, semitones = 0): void {
    const context = this.context
    const master = this.master
    if (!context || !master || this.muted) return

    // **겹치는 만큼 줄입니다.** 넘치면 아예 내지 않습니다 — 이미 넉이 울리고 있으면 다섯째는
    // 들리지 않고 크기만 보탭니다.
    const room = this.crowding(cueId, context.currentTime)
    if (room <= 0) return

    const follows = this.follows.get(cueId) ?? false

    // **녹음된 것이 있으면 그것입니다.** 음높이는 재생 속도로 올립니다.
    const sample = this.samples.get(cueId)
    if (sample) {
      const source = context.createBufferSource()
      source.buffer = sample.buffer
      source.playbackRate.value = Math.pow(2, (follows ? semitones : 0) / 12)

      const gain = context.createGain()
      gain.gain.value = sample.gain * room
      source.connect(gain).connect(master)
      // **묵음을 건너뛰고 시작합니다.** 그것이 곧 「소리가 그림과 같이 난다」입니다.
      source.start(context.currentTime, sample.lead)
      return
    }

    const shape = SHAPE[cueId] ?? DEFAULT_SHAPE
    const now = context.currentTime
    const hz = BASE_HZ * Math.pow(2, ((follows ? semitones : 0) + shape.offset) / 12)

    if (shape.gain > 0) {
      const osc = context.createOscillator()
      osc.type = shape.wave
      osc.frequency.setValueAtTime(hz, now)
      if (shape.glide !== 0) {
        osc.frequency.exponentialRampToValueAtTime(
          hz * Math.pow(2, shape.glide / 12), now + shape.length)
      }

      const gain = context.createGain()
      gain.gain.setValueAtTime(0, now)
      gain.gain.linearRampToValueAtTime(shape.gain * room, now + 0.006)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + shape.length)

      osc.connect(gain).connect(master)
      osc.start(now)
      osc.stop(now + shape.length + 0.02)
    }

    if (shape.noise !== undefined) this.hissAt(shape.noise, now, room)
  }

  /**
   * 이 신호가 지금 몇 개나 겹쳐 있는가.
   *
   * **소리는 힘으로 더해집니다.** 같은 소리 넷이 겹치면 하나보다 두 배쯤 큰데, 각자를
   * 겹친 수의 제곱근으로 나누면 합이 하나만큼으로 남습니다 — 개수로 나누면 도리어 작아지고,
   * 그러면 한 장씩 사라지는 소리가 들리지 않습니다.
   *
   * 넷을 넘기면 0 입니다. 다섯째는 들리지 않고 크기만 보탭니다.
   */
  private crowding(cueId: string, now: number): number {
    const times = (this.recent.get(cueId) ?? []).filter(at => now - at < CROWD_SPAN)
    times.push(now)
    this.recent.set(cueId, times)
    if (times.length > CROWD_MOST) return 0
    return 1 / Math.sqrt(times.length)
  }

  /**
   * 조커가 웅얼거리는 소리.
   *
   * **말이 아닙니다.** 무슨 말인지 알아들을 수 있으면 그때부터 그 말이 매번 같은 말이 되고,
   * 한 판에 열 번 발동하는 조커에서 그것은 곧 지겨움입니다 — 알아들을 수 없는 웅얼거림은
   * 매번 달라도 같은 것으로 들립니다.
   *
   * 만드는 것이지 녹음이 아닙니다. **녹음은 반복이 들립니다** — 같은 파일이 열 번 나면
   * 열 번째에는 그 파일이 들립니다. 목청 하나(톱니파)를 입 모양(띠 통과 여과기) 뒤에 두고
   * 음절마다 그 입 모양을 옮기면 「아」 와 「우」 사이의 무엇이 되고, 그 값을 매번 조금씩
   * 흔들면 같은 조커가 같은 목소리로 다른 말을 합니다.
   *
   * `voice` 는 그 조커를 가리키는 수입니다 — **목소리는 조커마다 고정입니다.** 발동할 때마다
   * 목소리가 바뀌면 누가 말한 것인지 남지 않습니다.
   */
  mumble(voice: number, strength = 1): void {
    const context = this.context
    const master = this.master
    if (!context || !master || this.muted) return

    // 그 조커의 목소리. 낮게도 높게도 갑니다 — 한 옥타브 안입니다.
    const tone = ((voice * 2_654_435_761) % 12) / 12
    const base = 112 * Math.pow(2, tone)
    // 음절 둘에서 넷. **하나는 「윽」 이고 다섯이면 문장입니다.**
    const beats = 2 + (voice % 3)

    const osc = context.createOscillator()
    osc.type = 'sawtooth'
    // 입. **띠 하나만 남깁니다** — 그 띠가 어디냐가 모음을 가릅니다.
    const mouth = context.createBiquadFilter()
    mouth.type = 'bandpass'
    mouth.Q.value = 5.5
    const gain = context.createGain()
    gain.gain.setValueAtTime(0, context.currentTime)
    osc.connect(mouth).connect(gain).connect(master)

    const now = context.currentTime
    let at = now
    for (let i = 0; i < beats; i++) {
      const span = 0.062 + Math.random() * 0.05
      // 모음 하나. 낮으면 「우」 쪽이고 높으면 「애」 쪽입니다.
      const vowel = 480 + Math.random() * 760
      osc.frequency.setValueAtTime(base * (0.92 + Math.random() * 0.2), at)
      osc.frequency.linearRampToValueAtTime(base * (0.84 + Math.random() * 0.3), at + span)
      mouth.frequency.setValueAtTime(vowel, at)
      mouth.frequency.linearRampToValueAtTime(vowel * (0.68 + Math.random() * 0.7), at + span)
      // 음절의 앞이 서고 뒤가 눕습니다. 네모난 봉투는 말이 아니라 신호음입니다.
      gain.gain.linearRampToValueAtTime(0.135 * strength, at + 0.018)
      gain.gain.linearRampToValueAtTime(0.0001, at + span)
      at += span + 0.026
    }

    osc.start(now)
    osc.stop(at + 0.05)
  }

  /**
   * 잡음 한 번.
   *
   * 좁은 대역만 남깁니다 — 그 대역이 어디냐가 「종이」와 「금속」과 「바람」을 가릅니다.
   * 대역이 움직이면 쓸리는 소리가 됩니다.
   */
  private hissAt(noise: Noise, now: number, room = 1): void {
    const context = this.context
    const master = this.master
    if (!context || !master || !this.hiss) return

    const source = context.createBufferSource()
    source.buffer = this.hiss
    source.loop = true

    const band = context.createBiquadFilter()
    band.type = 'bandpass'
    band.Q.value = noise.q
    band.frequency.setValueAtTime(noise.hz, now)
    if (noise.sweep !== 0) {
      band.frequency.exponentialRampToValueAtTime(
        Math.max(80, noise.hz * noise.sweep), now + noise.length)
    }

    const gain = context.createGain()
    gain.gain.setValueAtTime(0, now)
    gain.gain.linearRampToValueAtTime(noise.gain * room, now + 0.004)
    gain.gain.exponentialRampToValueAtTime(0.0001, now + noise.length)

    source.connect(band).connect(gain).connect(master)
    source.start(now)
    source.stop(now + noise.length + 0.02)
  }
}

/** 잡음 층 하나. `sweep` 은 끝날 때 대역이 몇 배가 되는가입니다. */
interface Noise {
  gain: number
  length: number
  hz: number
  q: number
  sweep: number
}

interface Shape {
  wave: OscillatorType
  /** 기준음에서 반음 몇 개 위인가. */
  offset: number
  length: number
  /** 음의 크기. **0 이면 음 없이 잡음만 납니다** — 카드와 종이가 그렇습니다. */
  gain: number
  /** 소리가 나는 동안 음이 얼마나 움직이는가. */
  glide: number
  noise?: Noise
}

const DEFAULT_SHAPE: Shape = { wave: 'triangle', offset: 12, length: 0.09, gain: 0.35, glide: 0 }

/**
 * 소리마다의 파형. **연산마다 다르고 조커마다 다르지 않습니다.**
 *
 * 카드와 종이와 동전은 `gain` 이 0 이거나 작고 잡음이 본체입니다 — 사인파로 만든 「탁」은
 * 어떤 값을 줘도 전자음이고, 카드가 놓이는 소리로 들리지 않습니다.
 */
const SHAPE: Record<string, Shape> = {
  // ---------------------------------------------------------------- 득점
  card_chip: { wave: 'triangle', offset: 19, length: 0.07, gain: 0.30, glide: 0 },
  card_mult: { wave: 'sawtooth', offset: 14, length: 0.08, gain: 0.26, glide: 0 },
  joker_add: { wave: 'square', offset: 17, length: 0.08, gain: 0.22, glide: 0 },
  joker_mul: {
    wave: 'sawtooth', offset: 21, length: 0.16, gain: 0.30, glide: 5,
    noise: { gain: 0.10, length: 0.14, hz: 900, q: 1.2, sweep: 3 },
  },
  joker_money: { wave: 'triangle', offset: 24, length: 0.10, gain: 0.26, glide: 3 },
  joker_fizzle: {
    wave: 'sine', offset: 5, length: 0.10, gain: 0.10, glide: -4,
    noise: { gain: 0.12, length: 0.16, hz: 1600, q: 0.8, sweep: 0.3 },
  },
  retrigger: { wave: 'square', offset: 22, length: 0.05, gain: 0.20, glide: 2 },
  score_count: { wave: 'triangle', offset: 16, length: 0.05, gain: 0.16, glide: 0 },
  score_settle: { wave: 'sine', offset: 12, length: 0.30, gain: 0.34, glide: 7 },
  blind_clear: { wave: 'triangle', offset: 24, length: 0.45, gain: 0.38, glide: 12 },
  blind_fail: { wave: 'sawtooth', offset: 3, length: 0.55, gain: 0.30, glide: -12 },

  // ---------------------------------------------------------------- 카드
  //
  // **전부 잡음입니다.** 종이가 스치고 닿는 소리이지 음이 아닙니다. 대역이 어디냐가 「스침」
  // 과 「닿음」을 가르고, 대역이 움직이면 쓸리는 소리가 됩니다.
  card_draw: {
    wave: 'sine', offset: 20, length: 0.04, gain: 0,
    glide: 0, noise: { gain: 0.16, length: 0.055, hz: 2600, q: 0.7, sweep: 0.35 },
  },
  card_select: {
    wave: 'sine', offset: 26, length: 0.03, gain: 0.05,
    glide: 0, noise: { gain: 0.11, length: 0.035, hz: 3400, q: 1.4, sweep: 1 },
  },
  /** 손패의 자리에 놓입니다. 짧고 마른 소리. */
  card_place: {
    wave: 'sine', offset: 10, length: 0.03, gain: 0.04,
    glide: 0, noise: { gain: 0.18, length: 0.05, hz: 1500, q: 0.9, sweep: 0.4 },
  },
  /** 낸 카드가 판에 「짝」 붙습니다. 더 낮고 더 세게. */
  card_slam: {
    wave: 'square', offset: 4, length: 0.04, gain: 0.10, glide: -6,
    noise: { gain: 0.26, length: 0.07, hz: 900, q: 0.7, sweep: 0.28 },
  },
  /** 뒷면이 앞면으로 뒤집힙니다. */
  card_flip: {
    wave: 'sine', offset: 14, length: 0.03, gain: 0.05,
    glide: 0, noise: { gain: 0.14, length: 0.06, hz: 2000, q: 0.6, sweep: 2.2 },
  },
  card_destroy: {
    wave: 'sawtooth', offset: 8, length: 0.16, gain: 0.16, glide: -8,
    noise: { gain: 0.20, length: 0.22, hz: 1800, q: 0.5, sweep: 0.2 },
  },

  // ---------------------------------------------------------------- 돈
  //
  // **음이 하나씩 올라가는 것이 이 연출의 절반입니다.**
  coin_land: {
    wave: 'triangle', offset: 31, length: 0.06, gain: 0.18, glide: 2,
    noise: { gain: 0.10, length: 0.05, hz: 5200, q: 3, sweep: 0.7 },
  },
  coin_lose: { wave: 'sine', offset: 9, length: 0.09, gain: 0.18, glide: -6 },

  // ---------------------------------------------------------------- 조커
  joker_buy: {
    wave: 'triangle', offset: 22, length: 0.12, gain: 0.26, glide: 5,
    noise: { gain: 0.10, length: 0.06, hz: 2400, q: 1.2, sweep: 1.6 },
  },
  joker_sell: {
    wave: 'triangle', offset: 18, length: 0.12, gain: 0.22, glide: -5,
    noise: { gain: 0.10, length: 0.07, hz: 3000, q: 2, sweep: 0.5 },
  },
  /** 타서 사라집니다. 불이 붙는 소리라 대역이 넓고 길게 꺼집니다. */
  joker_burn: {
    wave: 'sawtooth', offset: 2, length: 0.24, gain: 0.10, glide: -10,
    noise: { gain: 0.22, length: 0.42, hz: 2400, q: 0.4, sweep: 0.16 },
  },
  /** 조커의 자리를 바꿉니다. 카드보다 무겁게. */
  joker_move: {
    wave: 'sine', offset: 6, length: 0.03, gain: 0.06,
    glide: 0, noise: { gain: 0.14, length: 0.05, hz: 1100, q: 0.8, sweep: 0.5 },
  },

  // ---------------------------------------------------------------- 소모품과 팩
  consumable_use: {
    wave: 'sine', offset: 27, length: 0.26, gain: 0.24, glide: 9,
    noise: { gain: 0.09, length: 0.20, hz: 3600, q: 1.4, sweep: 2.4 },
  },
  /** 봉지를 뜯습니다. **길게 쓸리는 잡음 하나가 전부입니다.** */
  pack_open: {
    wave: 'sine', offset: 8, length: 0.05, gain: 0.06, glide: 0,
    noise: { gain: 0.24, length: 0.30, hz: 3200, q: 0.5, sweep: 0.22 },
  },
  pack_pick: {
    wave: 'triangle', offset: 24, length: 0.09, gain: 0.22, glide: 4,
    noise: { gain: 0.10, length: 0.05, hz: 2800, q: 1.2, sweep: 1 },
  },

  // ---------------------------------------------------------------- 상점과 판
  shop_enter: { wave: 'triangle', offset: 14, length: 0.22, gain: 0.26, glide: 4 },
  shop_buy: { wave: 'triangle', offset: 22, length: 0.10, gain: 0.28, glide: 3 },
  shop_reroll: {
    wave: 'square', offset: 12, length: 0.09, gain: 0.18, glide: -2,
    noise: { gain: 0.14, length: 0.12, hz: 2200, q: 0.6, sweep: 0.4 },
  },
  voucher_buy: { wave: 'triangle', offset: 19, length: 0.20, gain: 0.28, glide: 7 },
  blind_select: { wave: 'triangle', offset: 16, length: 0.16, gain: 0.26, glide: 5 },
  blind_skip: { wave: 'sine', offset: 11, length: 0.14, gain: 0.20, glide: -3 },
  boss_reveal: {
    wave: 'sawtooth', offset: -5, length: 0.60, gain: 0.32, glide: -3,
    noise: { gain: 0.10, length: 0.50, hz: 400, q: 0.5, sweep: 0.5 },
  },

  // ---------------------------------------------------------------- 화면
  //
  // **작게 냅니다.** 버튼과 판은 자주 눌리므로, 득점만큼 들리면 그 소리가 화면을 덮습니다.
  button: {
    wave: 'sine', offset: 24, length: 0.025, gain: 0.07,
    glide: 0, noise: { gain: 0.06, length: 0.03, hz: 3800, q: 2, sweep: 1 },
  },
  panel_open: { wave: 'sine', offset: 18, length: 0.10, gain: 0.12, glide: 5 },
  panel_close: { wave: 'sine', offset: 18, length: 0.09, gain: 0.10, glide: -5 },

  run_win: { wave: 'triangle', offset: 24, length: 0.9, gain: 0.40, glide: 12 },
  run_lose: { wave: 'sine', offset: 0, length: 1.1, gain: 0.34, glide: -12 },
}

