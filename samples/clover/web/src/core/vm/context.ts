// 효과를 실행할 때 필요한 것들.
//
// **효과는 자기가 어디에 붙어 있는지를 알아야 합니다** — `GrowSelf` 가 늘리는 것은 그 조커의
// 카운터이고, `ScoredCard` 가 가리키는 것은 지금 처리 중인 카드입니다. 그 둘이 `EffectHost` 와
// `Scoring` 입니다.

import type { Data, EffectRow } from '../data'
import type { CardInstance, GameEvent, JokerInstance, RunState } from '../state'
import type { PokerHandKind } from '../../generated/enums/poker-hand-kind'
import type { ConsumableKind } from '../../generated/enums/consumable-kind'

/** 효과의 임자. 누구의 효과인가입니다. */
export interface EffectHost {
  kind: 'joker' | 'card' | 'run'
  joker?: JokerInstance
  /** 조커 줄에서의 자리. 왼쪽이 0 입니다. */
  slot?: number
  card?: CardInstance
}

export const RUN_HOST: EffectHost = { kind: 'run' }

/** 득점 중에만 있는 것. */
export interface Scoring {
  hand: PokerHandKind
  level: number
  chips: number
  mult: number
  played: CardInstance[]
  scoringCards: CardInstance[]
  /** 지금 처리 중인 카드. `ScoredCard` · `HeldCard` 가 가리킵니다. */
  card?: CardInstance
  /** 이 트리거에서 조건을 만족한 적이 있는 효과들. `first_only` 가 봅니다. */
  matched: Set<string>
  /** 재발동이 재발동을 부르는 깊이. 상한이 `Const_Score` 에 있습니다. */
  depth: number
}

export interface Vm {
  data: Data
  state: RunState
  events: GameEvent[]
  scoring?: Scoring
  /** 플레이어가 고른 카드. 소모품이 씁니다. */
  selection: CardInstance[]
  /** 방금 쓴 소모품의 갈래. `star_chart` 가 봅니다. */
  lastConsumableKind?: ConsumableKind
  /** 선언으로 적히지 않아 코드로 남은 것들. **개수가 지표입니다.** */
  customsRun: string[]

  /**
   * 확률을 굴리는가.
   *
   * **`'never'` 는 굴리지 않고 발동하지 않은 것으로 넘어갑니다.** 굴리는 것만으로 난수
   * 스트림이 소비되므로, 상태를 복제해 미리 세어 보는 쪽이 굴리면 그 뒤의 판이 달라집니다 —
   * 판을 열어 본 것과 열어 보지 않은 것이 다른 런이 됩니다.
   *
   * 비어 있으면 굴립니다. 규격은 `doc/insight.md` 의 「결정 4」입니다.
   */
  chanceMode?: 'roll' | 'never'

  /** `'never'` 에서 넘어간 확률 효과들. 굴리지 않은 것을 부르는 쪽이 알아야 합니다. */
  chanceSkipped?: ChanceSkip[]

  // 효과가 남기고 파이프라인이 읽는 것들. **효과가 직접 하지 않는 이유**는 순서에 있습니다 —
  // 재발동은 그 자리에서 즉시여야 하고, 상점 선물은 다음 상점에서 뜻을 가집니다.

  /** 이 카드를 몇 번 더 발동할 것인가. */
  pendingRetrigger?: number
  /** 지금이 재발동 중인가. **재발동 중에는 재발동을 받지 않습니다.** */
  retriggering?: boolean
  /** 능력을 빌려 올 조커. `tracing` · `mirror_note` 가 남깁니다. */
  copyTarget?: JokerInstance
  /** 패배를 막았는가. `old_bones` 가 남깁니다. */
  lossPrevented?: boolean
  /** 보스를 다시 뽑는가. */
  rerollBoss?: boolean
  /** 다음 상점에 놓을 것들. */
  shopGifts: ShopGift[]

  /**
   * 효과 하나가 시작한 자리.
   *
   * **누가 했는가가 무엇이 바뀌었는가보다 먼저 나와야 합니다.** 값을 바꾸는 것은 연산 안쪽이고
   * 누가 했는지는 연산이 끝나야 알므로, 그대로 뒤에 붙이면 순서가 뒤집힙니다 — 화면은 앞
   * 박자에 그 값을 얹게 되고, 조커가 올린 배수가 그 앞 카드의 것으로 보입니다.
   */
  mark?: number

  /**
   * 규칙을 다시 세우는 중인가.
   *
   * **다시 세우는 동안에는 아무것도 적지 않습니다.** 그러지 않으면 다시 얹는 것이 그때마다
   * 목록에 한 줄씩 더해져, 규칙이 걸릴수록 값이 불어납니다.
   */
  rebuilding?: boolean
}

/**
 * 굴리지 않고 넘어간 확률 효과 하나.
 *
 * **행과 임자를 그대로 담습니다.** 부르는 쪽이 설명 문장과 이름을 만들어야 하고, 여기서
 * 문장으로 만들면 그 문장이 두 곳에서 만들어집니다.
 */
export interface ChanceSkip {
  row: EffectRow
  host: EffectHost
  /** 전역 배율을 이미 곱한 분자입니다. */
  num: number
  den: number
}

/** 다음 상점에 놓이는 것. 태그가 남깁니다. */
export interface ShopGift {
  create: number
  rarity?: number
  edition?: number
  free: boolean
  count: number
}

export function newVm(data: Data, state: RunState): Vm {
  return { data, state, events: [], selection: [], customsRun: [], shopGifts: [] }
}

/** 효과 하나를 가리키는 이름. `first_only` 가 이것으로 기억합니다. */
export function effectKey(row: EffectRow, host: EffectHost): string {
  const owner = host.joker ? String(host.joker.uid) : row.owner
  return `${row.source}:${owner}:${row.order}`
}
