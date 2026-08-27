// 연출.
//
// **코어는 상태와 이벤트만 냅니다.** 이벤트 하나가 언제 얼마나 세게 보일지는 여기서 정하고,
// 그 수치는 `Const_Feel` 이므로 데이터입니다 — 유니티가 같은 문턱을 읽습니다.
//
// 규격은 `doc/presentation.md` 입니다.
//
// **타임라인을 만드는 것과 재생하는 것을 갈랐습니다.** 앞쪽은 순수 함수라 테스트가 값을
// 확인할 수 있고, 뒤쪽만 화면을 압니다.

import type { FeelConstants } from '../core/data'
import type { GameEvent } from '../core/state'

/** 화면에서 한 번에 일어나는 것. */
export interface Beat {
  /** 시작 시각. 밀리초 */
  at: number
  /** 이 박자의 길이 */
  hold: number
  event: GameEvent
  /** 0 부터 1. 흔들림 · 숫자 크기 · 음높이가 전부 이것을 씁니다. */
  intensity: number
}

export interface Feel {
  scoreStepMs: number
  jokerStepMs: number
  retriggerStepMs: number
  handLabelMs: number
  multiplyMs: number
  settleMs: number
  moneyStepMs: number
  hitStopMs: number
  fastForwardScale: number
  shakeMaxPx: number
  shakeThresholdMult: number
  shakeMaxMult: number
  numberScaleMaxBp: number
  pitchMaxSemitones: number
  particleMax: number
  chromaticMaxPx: number
  cardHoverLiftPx: number
  cardHoverTiltDeg: number
  drawStaggerMs: number
}

export function readFeel(feel: FeelConstants): Feel {
  return feel as unknown as Feel
}

/**
 * 값이 얼마나 큰가를 0..1 로.
 *
 * **로그입니다.** 배수 3 과 300 의 연출이 같으면 큰 배수가 크게 느껴지지 않고, 선형이면
 * 작은 쪽이 전부 0 이 됩니다.
 */
export function intensityOf(mult: number, feel: Feel): number {
  const low = Math.max(1, feel.shakeThresholdMult)
  const high = Math.max(low + 1, feel.shakeMaxMult)
  if (mult <= low) return 0
  const t = Math.log(mult / low) / Math.log(high / low)
  return Math.max(0, Math.min(1, t))
}

/** 이벤트 하나가 차지하는 시간. */
function holdOf(event: GameEvent, feel: Feel): number {
  switch (event.t) {
    case 'HandEvaluated': return feel.handLabelMs
    case 'CardScored': return feel.scoreStepMs
    case 'JokerTriggered': return feel.jokerStepMs
    case 'JokerFizzled': return feel.jokerStepMs
    case 'Retriggered': return feel.retriggerStepMs
    case 'ScoreResolved': return feel.multiplyMs + feel.settleMs
    case 'BlindCleared': return feel.settleMs
    // **돈이 오가는 것도 사건입니다.** 동전이 날아가 꽂히는 시간을 자기 몫으로 씁니다.
    case 'MoneyChanged': return event.delta === 0 ? 0 : feel.moneyStepMs
    case 'RunLost':
    case 'RunWon': return feel.settleMs * 2
    // 값이 바뀐 것을 알리는 이벤트는 자기 시간을 쓰지 않습니다 — 앞의 박자에 얹힙니다.
    case 'ChipsMultChanged': return 0
    default: return 0
  }
}

/**
 * 이벤트 배열을 박자 배열로.
 *
 * `ChipsMultChanged` 가 시간을 쓰지 않는 것이 요점입니다 — 그것은 「지금 칩과 배수가
 * 얼마인가」이지 별도의 사건이 아니고, 앞의 박자가 끝날 때의 값으로 보여야 합니다.
 */
export function buildTimeline(events: readonly GameEvent[], feel: Feel): Beat[] {
  const beats: Beat[] = []
  let at = 0
  let mult = 10_000

  for (const event of events) {
    if (event.t === 'ChipsMultChanged') {
      mult = event.mult
      if (beats.length > 0) beats[beats.length - 1].intensity = intensityOf(mult, feel)
      continue
    }

    const hold = holdOf(event, feel)
    if (hold === 0) continue

    beats.push({ at, hold, event, intensity: intensityOf(mult, feel) })
    at += hold
  }

  return beats
}

/** 타임라인 전체의 길이. */
export function timelineLength(beats: readonly Beat[]): number {
  if (beats.length === 0) return 0
  const last = beats[beats.length - 1]
  return last.at + last.hold
}

/** 세기에서 화면 값으로. **문턱은 데이터이고 이 계산은 규격입니다.** */
export function shakeOf(intensity: number, feel: Feel): number {
  return intensity * feel.shakeMaxPx
}

export function scaleOf(intensity: number, feel: Feel): number {
  return 1 + intensity * (feel.numberScaleMaxBp / 10_000 - 1)
}

export function semitonesOf(intensity: number, feel: Feel): number {
  return intensity * feel.pitchMaxSemitones
}

export function particlesOf(intensity: number, feel: Feel): number {
  return Math.round(intensity * feel.particleMax)
}

/**
 * 타임라인을 재생하는 것.
 *
 * 화면을 모릅니다 — 「지금 이 박자를 보여라」를 불러 줄 뿐입니다. 빠르게 넘기기가 배속을
 * 올리는 것도 여기입니다.
 */
export class TimelinePlayer {
  private beats: Beat[] = []
  private cursor = 0
  private clock = 0
  private speed = 1

  constructor(private readonly onBeat: (beat: Beat) => void) {}

  play(beats: Beat[]): void {
    this.beats = beats
    this.cursor = 0
    this.clock = 0
    this.speed = 1
  }

  /** 아무 키나 누르면 빨라지고, 두 번 누르면 끝냅니다. */
  hurry(feel: Feel): void {
    if (this.speed === 1) this.speed = feel.fastForwardScale
    else this.finish()
  }

  finish(): void {
    while (this.cursor < this.beats.length) this.onBeat(this.beats[this.cursor++])
    this.clock = Number.MAX_SAFE_INTEGER
  }

  get busy(): boolean {
    return this.cursor < this.beats.length
  }

  advance(deltaMs: number): void {
    if (!this.busy) return
    this.clock += deltaMs * this.speed
    while (this.cursor < this.beats.length && this.beats[this.cursor].at <= this.clock) {
      this.onBeat(this.beats[this.cursor++])
    }
  }
}
