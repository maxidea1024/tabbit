// 소리.
//
// **음원 파일이 없습니다.** 소리를 파형으로 만듭니다 — 칩이 더해질 때의 음높이가 값을 따라
// 올라가야 하는데, 그것은 녹음된 소리로는 되지 않습니다. 어느 소리가 값을 따라가는지는
// `SoundCue` 테이블에 있습니다.
//
// 원작의 음원을 쓰지 않습니다.

import type { CloverData } from '../generated/clover-data'

const BASE_HZ = 220

export class Audio {
  private context?: AudioContext
  private master?: GainNode
  private readonly follows = new Map<string, boolean>()
  /**
   * 잡음 한 토막.
   *
   * **카드와 종이와 동전은 음이 아니라 잡음입니다.** 사인파로 만든 「탁」은 어떤 값을 줘도
   * 전자음이고, 카드가 놓이는 소리로 들리지 않습니다.
   *
   * 한 번 만들어 두고 돌려 씁니다 — 소리마다 만들면 그 만드는 값이 소리보다 큽니다.
   */
  private hiss?: AudioBuffer

  /** 소리를 끄는가. 옵션이 정합니다. */
  muted = false

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

  constructor(tables: CloverData) {
    for (const cue of tables.soundCue.records) {
      this.follows.set(cue.cueId, cue.pitchFollowsValue)
    }
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

    const seconds = 0.5
    const frames = Math.floor(this.context.sampleRate * seconds)
    this.hiss = this.context.createBuffer(1, frames, this.context.sampleRate)
    const wave = this.hiss.getChannelData(0)
    for (let i = 0; i < frames; i++) wave[i] = Math.random() * 2 - 1
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

    const follows = this.follows.get(cueId) ?? false
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
      gain.gain.linearRampToValueAtTime(shape.gain, now + 0.006)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + shape.length)

      osc.connect(gain).connect(master)
      osc.start(now)
      osc.stop(now + shape.length + 0.02)
    }

    if (shape.noise !== undefined) this.hissAt(shape.noise, now)
  }

  /**
   * 잡음 한 번.
   *
   * 좁은 대역만 남깁니다 — 그 대역이 어디냐가 「종이」와 「금속」과 「바람」을 가릅니다.
   * 대역이 움직이면 쓸리는 소리가 됩니다.
   */
  private hissAt(noise: Noise, now: number): void {
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
    gain.gain.linearRampToValueAtTime(noise.gain, now + 0.004)
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

