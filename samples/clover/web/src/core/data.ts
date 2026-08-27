// 데이터.
//
// 시트에서 나온 테이블 36개를 읽고, **효과 테이블 9개를 하나의 모양으로 정규화합니다.**
// 아홉이 같은 컬럼 구성이므로 읽는 쪽도 하나면 됩니다 — 그것이 시트를 그렇게 만든 이유
// 입니다.

import { CloverData } from '../generated/clover-data'
import type { Condition } from '../generated/structs/condition'
import type { Operation } from '../generated/structs/operation'
import { Scope } from '../generated/enums/scope'
import { Trigger } from '../generated/enums/trigger'
import { Compare } from '../generated/enums/compare'
import { CounterField } from '../generated/enums/counter-field'
import { UnitKind } from '../generated/enums/unit-kind'
import { HandPick } from '../generated/enums/hand-pick'
import { CreateKind } from '../generated/enums/create-kind'
import { ModifyKind } from '../generated/enums/modify-kind'
import { JokerPick } from '../generated/enums/joker-pick'
import { DebuffKind } from '../generated/enums/debuff-kind'
import { TargetKind } from '../generated/enums/target-kind'
import { RuleKind } from '../generated/enums/rule-kind'
import { CardTrait } from '../generated/enums/card-trait'
import { SuitKind } from '../generated/enums/suit-kind'
import { RankKind } from '../generated/enums/rank-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { Rarity } from '../generated/enums/rarity'
import { BlindKind } from '../generated/enums/blind-kind'
import { ConsumableKind } from '../generated/enums/consumable-kind'
import { RunConst } from '../generated/constants/run-const'
import { ScoreConst } from '../generated/constants/score-const'
import { EconomyConst } from '../generated/constants/economy-const'
import { FeelConst } from '../generated/constants/feel-const'

/** 효과 행 하나. 아홉 테이블이 이 모양으로 옵니다. */
export interface EffectRow {
  /** 어느 테이블에서 왔는가. 보고와 대조에 씁니다. */
  readonly source: EffectSource
  readonly owner: string
  readonly order: number
  readonly trigger: Trigger
  readonly chanceNum: number | null
  readonly chanceDen: number | null
  readonly firstOnly: boolean
  readonly ranks: readonly RankKind[]
  readonly suits: readonly SuitKind[]
  readonly scope: Scope
  readonly scopeCount: number | null
  readonly condition: Condition
  readonly operation: Operation
}

export type EffectSource =
  | 'joker' | 'tarot' | 'spectral' | 'boss' | 'voucher' | 'tag' | 'deck'
  | 'enhancement' | 'seal'

/** 같은 소유자의 효과를 순서대로 묶어 둔 것. */
export type EffectIndex = ReadonlyMap<string, readonly EffectRow[]>

/** 읽은 데이터 전부. 런이 이것을 들고 돕니다. */
export interface Data {
  readonly tables: CloverData

  readonly jokerEffects: EffectIndex
  readonly tarotEffects: EffectIndex
  readonly spectralEffects: EffectIndex
  readonly bossEffects: EffectIndex
  readonly voucherEffects: EffectIndex
  readonly tagEffects: EffectIndex
  readonly deckEffects: EffectIndex
  readonly enhancementEffects: EffectIndex
  readonly sealEffects: EffectIndex

  /**
   * enum 값에서 이름으로.
   *
   * **설명을 조립할 때 씁니다** — `Trigger.OnCardScored` 가 `2` 로 오므로, 그 `2` 가 어느
   * 이름인지 알아야 문구를 찾습니다.
   */
  readonly enumNames: Record<string, Record<number, string>>

  /** 상수셋. 시트의 `Const_*` 입니다. */
  readonly run: RunConstants
  readonly score: ScoreConstants
  readonly economy: EconomyConstants
  readonly feel: FeelConstants
}

export interface RunConstants {
  startingMoney: number
  handsPerRound: number
  discardsPerRound: number
  handSize: number
  jokerSlots: number
  consumableSlots: number
  maxPlayedCards: number
  winAnte: number
  showdownEvery: number
  endlessGrowthBp: number
}

export interface ScoreConstants {
  multScale: number
  multDefault: number
  roundDownToNegativeInfinity: boolean
  maxRetriggerDepth: number
}

export interface EconomyConstants {
  interestPer5: number
  interestCap: number
  sellDivisor: number
  sellMin: number
  legendaryBaseCost: number
  tarotCost: number
  planetCost: number
  spectralCost: number
  playingCardCost: number
  voucherCost: number
  shopCardSlots: number
  shopPackSlots: number
  soulChanceNum: number
  soulChanceDen: number
}

export interface FeelConstants {
  [name: string]: number
}

/** enum 의 숫자에서 이름으로. 생성 코드의 enum 은 양방향 맵입니다. */
function reverse(source: object): Record<number, string> {
  const out: Record<number, string> = {}
  for (const [key, value] of Object.entries(source)) {
    if (typeof value === 'number') out[value] = key
  }
  return out
}

/** 어느 테이블에서든 같은 모양으로 읽습니다. */
interface RawEffect {
  owner: string
  order: number
  trigger: Trigger
  chanceNum: number
  hasChanceNum: boolean
  chanceDen: number
  hasChanceDen: boolean
  firstOnly: boolean
  hasFirstOnly: boolean
  ranks: RankKind[]
  hasRanks: boolean
  suits: SuitKind[]
  hasSuits: boolean
  scope: Scope
  scopeCount: number
  hasScopeCount: boolean
  condition: Condition
  operation: Operation
}

function index(source: EffectSource, records: readonly RawEffect[],
               ownerOf: (raw: RawEffect) => string = raw => raw.owner): EffectIndex {
  const byOwner = new Map<string, EffectRow[]>()

  for (const raw of records) {
    const row: EffectRow = {
      source,
      owner: ownerOf(raw),
      order: raw.order,
      trigger: raw.trigger,
      chanceNum: raw.hasChanceNum ? raw.chanceNum : null,
      chanceDen: raw.hasChanceDen ? raw.chanceDen : null,
      firstOnly: raw.hasFirstOnly ? raw.firstOnly : false,
      ranks: raw.hasRanks ? raw.ranks : [],
      suits: raw.hasSuits ? raw.suits : [],
      scope: raw.scope,
      scopeCount: raw.hasScopeCount ? raw.scopeCount : null,
      condition: raw.condition,
      operation: raw.operation,
    }

    const list = byOwner.get(row.owner)
    if (list) list.push(row)
    else byOwner.set(row.owner, [row])
  }

  // 순서가 규칙이므로 여기서 한 번 정렬해 둡니다. 시트의 행 순서에 기대지 않습니다.
  for (const list of byOwner.values()) list.sort((a, b) => a.order - b.order)
  return byOwner
}

/**
 * enum 키인 효과 테이블은 `owner` 가 숫자로 옵니다 — 문자열 키로 맞춥니다.
 *
 * **레코드를 펼쳐 복사하지 않습니다.** 생성 레코드의 `condition` 과 `operation` 은
 * 프로토타입의 게터이고, 스프레드는 그것을 옮기지 않습니다. 옮겨진 것은 `_condition` 뿐이라
 * 판별자가 0 으로 읽히고, 조건이 통째로 사라집니다.
 */
function indexByEnum(source: EffectSource, records: readonly RawEffect[]): EffectIndex {
  return index(source, records, raw => String(raw.owner))
}

/**
 * 읽은 테이블을 런이 쓰는 모양으로 만듭니다.
 *
 * **상수셋은 생성된 클래스에서 그대로 옵니다.** 이름으로 다시 찾으면 오타가 통과하고,
 * 그것이 상수셋을 코드로 내는 이유입니다.
 */
export function build(tables: CloverData): Data {
  return {
    tables,
    enumNames: {
      Trigger: reverse(Trigger),
      Scope: reverse(Scope),
      Compare: reverse(Compare),
      CounterField: reverse(CounterField),
      UnitKind: reverse(UnitKind),
      HandPick: reverse(HandPick),
      CreateKind: reverse(CreateKind),
      ModifyKind: reverse(ModifyKind),
      JokerPick: reverse(JokerPick),
      DebuffKind: reverse(DebuffKind),
      TargetKind: reverse(TargetKind),
      RuleKind: reverse(RuleKind),
      CardTrait: reverse(CardTrait),
      SuitKind: reverse(SuitKind),
      RankKind: reverse(RankKind),
      PokerHandKind: reverse(PokerHandKind),
      Rarity: reverse(Rarity),
      BlindKind: reverse(BlindKind),
      ConsumableKind: reverse(ConsumableKind),
    },
    jokerEffects: index('joker', tables.jokerEffect.records as unknown as RawEffect[]),
    tarotEffects: index('tarot', tables.tarotEffect.records as unknown as RawEffect[]),
    spectralEffects: index('spectral', tables.spectralEffect.records as unknown as RawEffect[]),
    bossEffects: index('boss', tables.bossEffect.records as unknown as RawEffect[]),
    voucherEffects: index('voucher', tables.voucherEffect.records as unknown as RawEffect[]),
    tagEffects: index('tag', tables.tagEffect.records as unknown as RawEffect[]),
    deckEffects: index('deck', tables.deckEffect.records as unknown as RawEffect[]),
    enhancementEffects:
      indexByEnum('enhancement', tables.enhancementEffect.records as unknown as RawEffect[]),
    sealEffects: indexByEnum('seal', tables.sealEffect.records as unknown as RawEffect[]),
    run: RunConst,
    score: ScoreConst,
    economy: EconomyConst,
    feel: FeelConst as unknown as FeelConstants,
  }
}
