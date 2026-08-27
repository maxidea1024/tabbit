// 런의 상태.
//
// **테이블은 변하지 않는 것이고 여기 있는 것이 변합니다.** 규격은
// `doc/effect-vm/state.md` 이고, 세이브와 리플레이가 이 모양을 그대로 씁니다.

import type { RankKind } from '../generated/enums/rank-kind'
import type { SuitKind } from '../generated/enums/suit-kind'
import type { EnhancementKind } from '../generated/enums/enhancement-kind'
import type { SealKind } from '../generated/enums/seal-kind'
import type { EditionKind } from '../generated/enums/edition-kind'
import type { StickerKind } from '../generated/enums/sticker-kind'
import type { PokerHandKind } from '../generated/enums/poker-hand-kind'
import type { BlindKind } from '../generated/enums/blind-kind'
import type { ConsumableKind } from '../generated/enums/consumable-kind'
import type { Pcg32 } from './rng'
import type { PackOpen, ShopState } from './shop'

/**
 * 덱의 카드 한 장.
 *
 * `uid` 가 있는 이유는 하나입니다 — 같은 스페이드 A 가 덱에 둘 있을 수 있고 하나에만
 * 인장이 붙습니다. **값으로 가리키면 그 둘이 구분되지 않습니다.**
 */
export interface CardInstance {
  uid: number
  baseCardId: string
  rank: RankKind
  suit: SuitKind
  enhancement: EnhancementKind
  seal: SealKind
  edition: EditionKind
  /** 영구히 붙은 칩. `wanderer` 가 올립니다. */
  bonusChips: number
  /** 이번 라운드에 무력화되었는가. 보스가 정합니다. */
  debuffed: boolean
  /** 뒤집힌 채로 뽑혔는가. */
  faceDown: boolean
}

/** 조커가 누적하는 값. **칸이 고정입니다** — 조커를 더해도 세이브의 모양이 바뀌지 않습니다. */
export interface Counters {
  chips: number
  multAdd: number
  multMul: number
  money: number
  sellValue: number
  charge: number
  tick: number
}

export function newCounters(): Counters {
  return { chips: 0, multAdd: 0, multMul: 10_000, money: 0, sellValue: 0, charge: 0, tick: 0 }
}

export interface JokerInstance {
  uid: number
  jokerId: string
  edition: EditionKind
  sticker: StickerKind
  counters: Counters
  /** `Perishable` 이 센 라운드. */
  age: number
  /** 이번 핸드 동안 꺼져 있는가. `Crimson Heart` 가 정합니다. */
  disabled: boolean
}

export interface ConsumableInstance {
  uid: number
  kind: ConsumableKind
  id: string
  edition: EditionKind
}

/** 라운드마다 바뀌는 지정 대상. `chore_list` 계열이 봅니다. */
export interface RoundTargets {
  hand: PokerHandKind
  rank: RankKind
  suit: SuitKind
  cardRank: RankKind
  cardSuit: SuitKind
}

/** 규칙의 지금 값. 효과가 이것을 바꿉니다. */
export interface Rules {
  handSize: number
  handsPerRound: number
  discardsPerRound: number
  jokerSlots: number
  consumableSlots: number
  debtLimit: number
  freeRerolls: number
  rerollCostDelta: number
  rerollStartsFree: boolean
  interestPer5: number
  interestCap: number
  shopCardSlots: number
  shopDiscount: number
  shopAllowsPlayingCards: boolean
  shopAllowsSpectral: boolean
  shopWeightTarotScale: number
  shopWeightPlanetScale: number
  freePlanets: boolean
  allCardsScore: boolean
  allCardsAreFace: boolean
  flushStraightCards: number
  straightGap: number
  suitsMerged: boolean
  probabilityScale: number
  allowDuplicates: boolean
  balanceChipsAndMult: boolean
  bossRerollsPerAnte: number
  anteDelta: number
  editionWeightScale: number
  planetGivesMultBp: number
  noInterest: boolean
  moneyPerHandLeft: number
  moneyPerDiscardLeft: number
  doubleTagOnBossDefeat: boolean
  blindSizeScaleBp: number
  nextShopFree: boolean
  /** 보스가 켜는 것들. 라운드가 끝나면 꺼집니다. */
  noRepeatHandTypes: boolean
  singleHandTypeOnly: boolean
  mustPlayFiveCards: boolean
  alwaysDrawThree: boolean
  halveBaseChipsAndMult: boolean
  debuffUntilJokerSold: boolean
  forceCardSelected: boolean
}

export type Phase =
  | 'blind-select'
  | 'round'
  | 'shop'
  | 'won'
  | 'lost'

export interface RunState {
  seed: string
  deckId: string
  stake: string

  phase: Phase
  ante: number
  blind: BlindKind
  /** 이번 라운드의 보스. 보스 라운드가 아니어도 안테마다 정해져 있습니다. */
  bossId: string
  /** 이번 안테에 이미 나온 보스들. 후보가 바닥나면 비웁니다. */
  bossesSeen: string[]

  money: number
  handsLeft: number
  discardsLeft: number
  score: number
  target: number

  deck: CardInstance[]
  drawPile: number[]
  hand: number[]
  played: number[]
  discarded: number[]
  selected: number[]

  jokers: JokerInstance[]
  consumables: ConsumableInstance[]
  vouchers: string[]
  tagsPending: string[]

  handLevels: Record<string, number>
  handPlayCounts: Record<string, number>
  handsPlayedThisRun: number
  discardsUnusedThisRun: number
  blindsSkipped: number
  tarotUsed: number
  planetsUsed: string[]
  handTypesThisRound: string[]
  cardsPlayedThisAnte: number[]

  targets: RoundTargets
  rules: Rules
  /** 이번 라운드에 보스 능력이 발동했는가. `bullfighter` 가 봅니다. */
  bossTriggeredThisHand: boolean
  /** 보스 효과가 꺼져 있는가. `hushbell` 과 `ring_fighter` 가 끕니다. */
  bossDisabled: boolean
  /** 다음에 고르는 태그를 복제하는가. `Double Tag` 가 남깁니다. */
  duplicateNextTag: boolean

  /** 지금 상점에 놓인 것. 상점을 나가면 비웁니다. */
  shop: ShopState
  /** 지금 뜯어 놓은 팩. 고를 것을 다 고르면 `null` 로 돌아갑니다. */
  pack: PackOpen | null

  nextUid: number
  rng: Record<string, Pcg32>
}

/** 코어가 내는 것. 연출이 이것을 받아 그립니다. */
export type GameEvent =
  | { t: 'HandEvaluated'; hand: PokerHandKind; level: number; chips: number; mult: number; cards: number[] }
  | { t: 'CardScored'; uid: number; chips: number; mult: number; source: string }
  | { t: 'JokerTriggered'; slot: number; jokerId: string; op: string; chips: number; mult: number; money: number }
  | { t: 'JokerFizzled'; slot: number; jokerId: string; num: number; den: number }
  | { t: 'Retriggered'; uid: number; times: number }
  | { t: 'ChipsMultChanged'; chips: number; mult: number }
  | { t: 'ScoreResolved'; score: number; target: number }
  | { t: 'BlindCleared'; blind: BlindKind; reward: number }
  | { t: 'RunLost'; ante: number }
  | { t: 'RunWon'; ante: number }
  | { t: 'MoneyChanged'; delta: number; reason: string }
  | { t: 'CardModified'; uid: number; what: string }
  | { t: 'CardDestroyed'; uid: number }
  | { t: 'CardAdded'; uid: number }
  | { t: 'JokerAdded'; uid: number; jokerId: string }
  | { t: 'PackOpened'; packId: string }
  | { t: 'PackClosed' }
  | { t: 'JokerDestroyed'; uid: number; jokerId: string }
  | { t: 'ConsumableAdded'; uid: number; id: string }
  | { t: 'ConsumableUsed'; id: string }
  | { t: 'HandLevelled'; hand: PokerHandKind; level: number }
  | { t: 'RuleChanged'; rule: string; value: number }
