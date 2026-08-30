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

  volume = 0.35
  /** 소리를 끄는가. 옵션이 정합니다. */
  muted = false

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
    this.master.gain.value = this.volume
    this.master.connect(this.context.destination)
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
}

interface Shape {
  wave: OscillatorType
  /** 기준음에서 반음 몇 개 위인가. */
  offset: number
  length: number
  gain: number
  /** 소리가 나는 동안 음이 얼마나 움직이는가. */
  glide: number
}

const DEFAULT_SHAPE: Shape = { wave: 'triangle', offset: 12, length: 0.09, gain: 0.35, glide: 0 }

/** 소리마다의 파형. **연산마다 다르고 조커마다 다르지 않습니다.** */
const SHAPE: Record<string, Shape> = {
  card_chip: { wave: 'triangle', offset: 19, length: 0.07, gain: 0.30, glide: 0 },
  card_mult: { wave: 'sawtooth', offset: 14, length: 0.08, gain: 0.26, glide: 0 },
  joker_add: { wave: 'square', offset: 17, length: 0.08, gain: 0.22, glide: 0 },
  joker_mul: { wave: 'sawtooth', offset: 21, length: 0.14, gain: 0.30, glide: 5 },
  joker_money: { wave: 'triangle', offset: 24, length: 0.10, gain: 0.26, glide: 3 },
  joker_fizzle: { wave: 'sine', offset: 5, length: 0.10, gain: 0.18, glide: -4 },
  retrigger: { wave: 'square', offset: 22, length: 0.05, gain: 0.20, glide: 2 },
  score_count: { wave: 'triangle', offset: 16, length: 0.05, gain: 0.16, glide: 0 },
  score_settle: { wave: 'sine', offset: 12, length: 0.30, gain: 0.34, glide: 7 },
  blind_clear: { wave: 'triangle', offset: 24, length: 0.45, gain: 0.38, glide: 12 },
  blind_fail: { wave: 'sawtooth', offset: 3, length: 0.55, gain: 0.30, glide: -12 },
  card_draw: { wave: 'sine', offset: 20, length: 0.05, gain: 0.14, glide: 0 },
  // 카드가 자리에 달라붙는 소리. 짧고 낮게.
  card_slam: { wave: 'square', offset: 6, length: 0.045, gain: 0.20, glide: -6 },
  card_select: { wave: 'sine', offset: 26, length: 0.04, gain: 0.16, glide: 0 },
  card_destroy: { wave: 'sawtooth', offset: 8, length: 0.16, gain: 0.24, glide: -8 },
  // 동전이 꽂히는 소리. **음이 하나씩 올라가는 것이 이 연출의 절반입니다.**
  coin_land: { wave: 'triangle', offset: 31, length: 0.06, gain: 0.20, glide: 2 },
  coin_lose: { wave: 'sine', offset: 9, length: 0.09, gain: 0.18, glide: -6 },
  shop_enter: { wave: 'triangle', offset: 14, length: 0.22, gain: 0.26, glide: 4 },
  shop_buy: { wave: 'triangle', offset: 22, length: 0.10, gain: 0.28, glide: 3 },
  shop_reroll: { wave: 'square', offset: 12, length: 0.09, gain: 0.20, glide: -2 },
  boss_reveal: { wave: 'sawtooth', offset: -5, length: 0.60, gain: 0.32, glide: -3 },
  run_win: { wave: 'triangle', offset: 24, length: 0.9, gain: 0.40, glide: 12 },
  run_lose: { wave: 'sine', offset: 0, length: 1.1, gain: 0.34, glide: -12 },
}
