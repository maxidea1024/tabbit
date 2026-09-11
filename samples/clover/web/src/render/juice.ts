// 연출.
//
// **코어는 상태와 이벤트만 냅니다.** 이벤트 하나가 언제 얼마나 세게 보일지는 여기서 정하고,
// 그 수치는 `Const_Feel` 이므로 데이터입니다 — 코드가 아니라 시트에서 고칩니다.
//
// 규격은 `doc/presentation.md` 입니다.
//
// **타임라인을 만드는 것과 재생하는 것을 갈랐습니다.** 앞쪽은 순수 함수라 테스트가 값을
// 확인할 수 있고, 뒤쪽만 화면을 압니다.

import type { FeelConstants } from '../core/data'
import type { GameEvent } from '../core/state'

/** 화면에서 한 번에 일어나는 것. */
/**
 * 한 박자로 묶이는 카드 변화들.
 *
 * **13장이 바뀌면 13번 하지 않습니다.** 한 몸짓에 13장을 담는 것이지 12장을 빼는 것이
 * 아닙니다 — 연달아 오는 것을 묶는 자리가 여기입니다.
 */
export interface CardChanges {
  modified: number[]
  destroyed: number[]
  added: number[]
}

/** 연달아 오면 한 박자로 묶는 이벤트들. */
const CARD_CHANGE: ReadonlySet<string> = new Set([
  'CardModified', 'CardDestroyed', 'CardAdded',
])

/**
 * 규칙 변화도 묶입니다.
 *
 * **한 액션에 여럿 걸립니다.** 챌린지와 보스와 바우처가 그렇고, 판 여럿이 잇달아 뜨면
 * 어느 것이 방금 온 것인지 알 수 없습니다 — 한 판에 담습니다.
 */
const RULE_CHANGE: ReadonlySet<string> = new Set(['RuleChanged', 'HandLevelled'])

export interface Beat {
  /** 시작 시각. 밀리초 */
  at: number
  /** 이 박자의 길이 */
  hold: number
  event: GameEvent
  /**
   * 이 박자에 묶인 카드 변화들. **`CardModified` 계열의 박자에만 있습니다.**
   *
   * 이벤트는 카드 한 장에 하나씩 오는데 화면이 하는 일은 한 몸짓이므로, 묶은 것을 박자가
   * 들고 갑니다 — 이벤트 쪽을 굵게 내면 토스트와 인사이트가 세는 수가 달라집니다.
   */
  cards?: CardChanges
  /** 이 박자에 묶인 규칙 변화들. **`RuleChanged` 계열의 박자에만 있습니다.** */
  rules?: GameEvent[]
  /** 0 부터 1. 흔들림 · 숫자 크기 · 음높이가 전부 이것을 씁니다. */
  intensity: number
  /**
   * 이 박자가 끝났을 때의 칩과 배수.
   *
   * **박자가 값을 들고 다녀야 합니다.** 값이 바뀐 것을 알리는 이벤트는 자기 시간을 쓰지
   * 않으므로 화면이 그것을 따로 받을 자리가 없고, 없으면 조커가 올린 배수가 칸에 반영되지
   * 않은 채 마지막에 점수만 튀어나옵니다.
   */
  chips?: number
  mult?: number
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
  /** 뽑은 카드가 뒷면으로 자리에 붙는 장마다의 간격. */
  drawStaggerMs: number
  /** 마지막 장이 뒷면으로 자리에 붙기까지. */
  drawLandMs: number
  /** 다 붙은 뒤 왼쪽부터 뒤집는 장마다의 간격. */
  flipStaggerMs: number
  /** 낸 카드가 판으로 올라갈 때 장마다의 간격. */
  playStaggerMs: number
  /** 마지막 장이 자리에 붙고 득점이 시작되기까지. */
  playLandMs: number
  /** 건너뛰어 받은 태그 칩이 커져서 머리띠로 날아가 앉기까지. */
  tagGainMs: number
  /** 받자마자 쓰이는 태그가 발동하는 시간. */
  tagUseMs: number
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
    // **낸 카드가 한 장씩 날아가 붙는 동안은 아무것도 세지 않습니다.** 아직 자리에 없는
    // 카드 위에 숫자가 뜨면 다섯 장이 한 덩어리로 보입니다.
    case 'HandPlayed':
      return event.uids.length * feel.playStaggerMs + feel.playLandMs
    case 'HandEvaluated': return feel.handLabelMs
    case 'CardScored': return feel.scoreStepMs
    case 'JokerTriggered': return feel.jokerStepMs
    case 'RunTriggered': return feel.jokerStepMs
    case 'JokerFizzled': return feel.jokerStepMs
    case 'Retriggered': return feel.retriggerStepMs
    case 'ScoreResolved': return feel.multiplyMs + feel.settleMs
    case 'BlindCleared': return feel.settleMs
    // **돈이 오가는 것도 사건입니다.** 동전이 날아가 꽂히는 시간을 자기 몫으로 씁니다.
    case 'MoneyChanged': return event.delta === 0 ? 0 : feel.moneyStepMs
    case 'RunLost':
    case 'RunWon': return feel.settleMs * 2
    // **건너뛰기도 사건입니다.** 받은 태그가 카드에서 머리띠로 날아가 앉는 동안 판은 그대로
    // 있고, 그 자리에서 쓰이는 태그는 앉은 뒤에 발동합니다. 둘이 자기 시간을 씁니다.
    case 'TagGained': return feel.tagGainMs
    case 'TagUsed': return feel.tagUseMs
    // 버린 카드도 한 장씩 나갑니다.
    case 'HandDiscarded': return event.uids.length * feel.playStaggerMs
    // 다음 패는 득점이 끝난 뒤에 깔립니다. **나오기와 까기가 두 단계입니다** — 뒷면으로
    // 우르르 붙고, 다 붙은 뒤에 왼쪽부터 파도로 뒤집힙니다. 둘을 합한 것이 자기 몫입니다.
    case 'HandDrawn':
      return event.uids.length * (feel.drawStaggerMs + feel.flipStaggerMs) + feel.drawLandMs
    // **보스가 거는 것도 사건입니다.** 카드 몇 장이 한꺼번에 표시되므로 그것을 읽는
    // 시간이고, 족보 이름을 읽는 박자와 같은 몫입니다.
    case 'CardsDebuffed':
    case 'CardsHidden': return feel.handLabelMs
    // 조커에 걸리는 것들. **조커가 값을 내는 것과 같은 한 박자입니다** — 새 상수를 두지
    // 않는 것은 이것들이 조커 딱지 하나에서 일어나는 일이기 때문입니다.
    // 생긴 소모품이 만든 자리에서 날아오는 데까지.
    case 'ConsumableAdded':
    case 'JokerAdded':
    case 'JokerDestroyed':
    case 'JokerDisabled':
    case 'JokersShuffled':
    case 'JokerModified':
    case 'JokerCopied': return feel.jokerStepMs
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
  let chips = 0
  let mult = 10_000

  for (let i = 0; i < events.length; i++) {
    const event = events[i]

    // **카드 변화는 연달아 오는 것을 통째로 묶습니다.** 타로 하나가 다섯 장을 바꾸면
    // `CardModified` 가 다섯 개 오는데, 화면이 하는 일은 다섯 장이 함께 나와 뒤집히는 한
    // 몸짓입니다 — 박자를 다섯으로 두면 같은 판이 다섯 번 서고 걷힙니다.
    if (CARD_CHANGE.has(event.t)) {
      const cards: CardChanges = { modified: [], destroyed: [], added: [] }
      let end = i
      while (end < events.length && CARD_CHANGE.has(events[end].t)) {
        const one = events[end]
        if (one.t === 'CardModified') cards.modified.push(one.uid)
        else if (one.t === 'CardDestroyed') cards.destroyed.push(one.uid)
        else if (one.t === 'CardAdded') cards.added.push(one.uid)
        end++
      }
      const count = cards.modified.length + cards.destroyed.length + cards.added.length
      beats.push({
        at, hold: cardChangeHold(count, feel), event, cards,
        intensity: intensityOf(mult, feel), chips, mult,
      })
      at += beats[beats.length - 1].hold
      i = end - 1
      continue
    }

    // 규칙 변화도 연달아 오는 것을 묶습니다. **판 하나에 담깁니다.**
    if (RULE_CHANGE.has(event.t)) {
      const rules: GameEvent[] = []
      let end = i
      while (end < events.length && RULE_CHANGE.has(events[end].t)) {
        rules.push(events[end])
        end++
      }
      beats.push({
        at, hold: feel.handLabelMs, event, rules,
        intensity: intensityOf(mult, feel), chips, mult,
      })
      at += feel.handLabelMs
      i = end - 1
      continue
    }

    if (event.t === 'ChipsMultChanged') {
      chips = event.chips
      mult = event.mult
      const last = beats[beats.length - 1]
      if (last) {
        last.chips = chips
        last.mult = mult
        last.intensity = intensityOf(mult, feel)
      }
      continue
    }

    const hold = holdOf(event, feel)
    if (hold === 0) continue

    // 족보가 정해지는 자리에서 기본값으로 갈아탑니다. **그 앞의 값은 지난 판의 것입니다.**
    if (event.t === 'HandEvaluated') {
      chips = event.chips
      mult = event.mult
    }

    beats.push({
      at, hold, event,
      intensity: intensityOf(mult, feel),
      chips, mult,
    })
    at += hold
  }

  return beats
}

/**
 * 반 바퀴 하나. 카드가 좁아졌다가 벌어지는 데까지입니다.
 *
 * **`CardView` 의 잦아드는 빠르기가 정합니다** — `flip` 이 1 에서 0 으로 초당 8씩 줄고,
 * 보이는 면은 그 절반에서 갈립니다. 한쪽만 고치면 여기서 세는 시각과 실제로 뒤집히는
 * 시각이 어긋납니다.
 */
export const TURN_HALF_MS = 125
/**
 * 뒷면으로 멈춰 있는 시간. **그 뒤에서 얼굴이 갈립니다.**
 *
 * 앞면에서 앞면으로 반 바퀴만 돌던 동안은 좁아졌다 벌어지는 그 한 순간에 얼굴이 갈렸고,
 * 눈이 「뒤집혔다」로 읽기 전에 이미 새 얼굴이었습니다 — 갈린 것이 아니라 잠깐 찌그러진
 * 것으로 보였습니다. 뒷면이 온전히 보이는 동안이 있어야 그 앞과 뒤가 다른 카드가 됩니다.
 */
export const TURN_BACK_MS = 155
/** 한 장이 뒷면을 거쳐 다 뒤집히는 데. 반 바퀴 둘과 그 사이의 멈춤입니다. */
export const TURN_FULL_MS = TURN_HALF_MS * 2 + TURN_BACK_MS
/**
 * 여러 장이 바뀔 때 장마다의 간격.
 *
 * **앞 장이 벌어지기 시작할 때 다음 장이 닫힙니다.** 낸 카드가 판으로 올라가는 간격
 * (`playStaggerMs`, 90밀리초)을 빌려 쓰던 동안은 그것이 반 바퀴(125밀리초)보다 짧아 장마다
 * 뒤집힘이 겹쳤고, 세 장부터 한 덩어리가 통째로 갈리는 것으로 보였습니다.
 */
export const TURN_STEP_MS = 220

/**
 * 카드가 판 위로 나와 바뀌고 돌아가는 데 걸리는 시간.
 *
 * **나오는 데 · 한 장씩 뒤집히는 데 · 보고 나서 돌아가는 데** 셋입니다. 나오고 돌아가는
 * 것은 뽑는 것과 같은 시간이고, 가운데만 뒤집기의 시간입니다 — 마지막 장이 다 뒤집히는
 * 시각이 그 끝입니다.
 */
export function cardChangeHold(count: number, feel: Feel): number {
  if (count === 0) return 0
  return feel.drawLandMs + (count - 1) * TURN_STEP_MS + TURN_FULL_MS + feel.handLabelMs
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

  /** 옵션이 정한 배속. 빠르게 넘기기는 이 위에 얹힙니다. */
  base = 1

  play(beats: Beat[]): void {
    this.beats = beats
    this.cursor = 0
    this.clock = 0
    this.speed = this.base
  }

  /** 아무 키나 누르면 빨라지고, 두 번 누르면 끝냅니다. */
  hurry(feel: Feel): void {
    if (this.speed <= this.base) this.speed = this.base * feel.fastForwardScale
    else this.finish()
  }

  finish(): void {
    while (this.cursor < this.beats.length) this.onBeat(this.beats[this.cursor++])
    this.clock = Number.MAX_SAFE_INTEGER
  }

  get busy(): boolean {
    return this.cursor < this.beats.length
  }

  /**
   * 남은 박자를 버립니다.
   *
   * **`finish` 와 다릅니다.** 그것은 남은 것을 전부 보여 주고 끝내는 것이고, 이것은 그
   * 판이 없어졌으니 보여 줄 데가 없는 것입니다 — 타이틀로 돌아갈 때뿐입니다.
   */
  drop(): void {
    this.beats = []
    this.cursor = 0
    this.clock = 0
  }

  /**
   * 다음 박자를 미룰 것인가.
   *
   * **카드가 자리에 닿기 전에는 세지 않습니다.** 카드는 실제 시계로 날아가고 연출은 배속과
   * 히트스톱을 타므로, 시간만으로 맞추면 배속을 올린 순간 어긋납니다.
   */
  blocked?: () => boolean

  /**
   * 다음에 올 박자가 무엇인가.
   *
   * **기다리는 동안을 채우려면 무엇을 기다리는지 알아야 합니다.** 시계로 재면 배속과
   * 히트스톱에서 어긋나므로, 박자를 보고 정합니다.
   */
  get coming(): string | undefined {
    return this.beats[this.cursor]?.event.t
  }

  advance(deltaMs: number): void {
    if (!this.busy) return
    if (this.blocked?.() === true) return

    this.clock += deltaMs * this.speed

    // **한 프레임에 한 박자입니다.** 득점은 한 장씩 세는 것이고, 둘이 같은 프레임에 뜨면
    // 그 둘은 한 번에 일어난 것으로 보입니다 — 프레임이 길어지면(탭을 전환했다 오거나
    // 그림을 굽느라 멈추면) 밀린 박자가 한 번에 터집니다.
    //
    // **빠르게 넘기는 중에는 그러지 않습니다.** 그때는 넘기는 것이 목적이고, 한 프레임에
    // 하나씩이면 넘기는 데 오히려 더 걸립니다.
    const hurrying = this.speed > this.base
    do {
      if (this.cursor >= this.beats.length) break
      if (this.beats[this.cursor].at > this.clock) break
      this.onBeat(this.beats[this.cursor++])
    } while (hurrying)
  }
}
