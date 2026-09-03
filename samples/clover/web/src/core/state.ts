// 런의 상태.
//
// **테이블은 변하지 않는 것이고 여기 있는 것이 변합니다.** 규격은
// `doc/effect-vm/state.md` 이고, 세이브와 리플레이가 이 모양을 그대로 씁니다.

import type { JokerPool } from '../generated/enums/joker-pool'
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

/**
 * 규칙 하나를 바꾸는 것.
 *
 * **규칙은 누적이 아니라 매번 다시 세웁니다.** 누적으로 두면 원인이 사라지거나 새로 생겼을 때
 * 아무도 다시 계산하지 않습니다 — 보스가 걸어 둔 것이 보스가 지나가도 남고, 상점에서 산 조커의
 * 것은 아예 걸리지 않았습니다.
 */
export interface RuleDelta {
  rule: number
  value: number
  absolute: boolean
}

/** 규칙의 지금 값. 효과가 이것을 바꿉니다. */
export interface Rules {
  handSize: number
  handsPerRound: number
  discardsPerRound: number
  jokerSlots: number
  consumableSlots: number
  /**
   * 돈이 내려갈 수 있는 바닥. **0 이하의 값입니다.**
   *
   * 기본 0 이면 빚을 낼 수 없고, `-20` 이면 잔액이 `-20` 까지 내려갈 수 있습니다. 데이터의
   * `OpChangeRule DebtLimit -20` 이 그대로 더해지는 값이고, 돈이 나가는 곳은 모두
   * `money - cost < debtLimit` 하나로 판정합니다 — 코어의 상점·버리기와 화면의 `canPay` 가
   * 같은 식입니다.
   */
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
  /** 블라인드 갈래마다 격파 보상을 끕니다. 챌린지가 씁니다. */
  noSmallBlindReward: boolean
  noBigBlindReward: boolean
  noBossBlindReward: boolean
  /** 칩이 지금 보유액을 넘지 못합니다. */
  chipsCappedByMoney: boolean
  /** 뽑는 카드가 뒤집힐 확률의 분모. 0이면 없습니다. */
  faceDownDrawRate: number
  /** 보유액이 이만큼일 때마다 패 크기가 하나 줄어듭니다. 0이면 없습니다. */
  handSizePerMoney: number
  /** 모든 조커에 `Eternal`. `Joker.eternalOk` 가 거짓인 것은 빠집니다. */
  allJokersEternal: boolean
  /** 낸 카드가 득점을 마치면 무력화됩니다. **라운드가 끝나도 풀리지 않습니다.** */
  debuffPlayedAfterScoring: boolean
  /** 살 때마다 상점의 값이 이만큼 영구히 오릅니다. */
  priceRisePerPurchase: number
  /** 상점의 카드 칸에 조커가 나오지 않습니다. */
  noJokersInShop: boolean
  /** 카드를 버릴 때마다 내는 금액. */
  discardCost: number
  /** 옮길 수 없는 조커의 자리. 1부터 세고 0이면 없습니다. */
  pinnedJokerSlot: number
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
  /**
   * 이 런의 챌린지. 빈 문자열이면 챌린지가 아닙니다.
   *
   * **`pools` 와 같은 갈래의 런 설정입니다** — 해시에 들어가지 않으므로 구워 둔 리플레이가
   * 그대로 유효합니다.
   */
  challengeId: string
  /**
   * 이 런에서 쓰는 조커 풀. 시작할 때 정해지고 런 도중에 바뀌지 않습니다.
   *
   * **해심에는 들어가지 않습니다.** `seed` · `deckId` · `stake` 와 같은 런의 설정이고,
   * 설정이 다르면 그 결과가 상황에서 갈라집니다 — 설정 자슴를 다으면 예전 리플레이의
   * 해시가 전부 달라집니다.
   */
  pools: JokerPool[]

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
  /**
   * 이번 안테의 스몰·빅을 건너뛰면 받는 태그. `[스몰, 빅]` 입니다.
   *
   * **건너뛰기 전에 정해져 있어야 합니다.** 건너뛴 다음에 뽑으면 무엇을 받는지 모르는 채로
   * 건너뛸지를 정하게 되고, 그것은 선택이 아니라 찍기입니다. 안테가 바뀔 때 둘을 함께
   * 뽑습니다 — 스몰과 빅이 한 화면에 나란히 서므로 둘 다 적혀 있어야 합니다.
   */
  tagOffer: string[]

  /**
   * 한 번 걸리고 남는 규칙 변경.
   *
   * **원인이 사라져도 남습니다** — 유령 카드가 손패를 하나 줄이면 그 카드는 없어져도 손패는
   * 그대로입니다. 규칙을 다시 세울 때 이것들을 다시 얹습니다.
   */
  ruleDeltas: RuleDelta[]
  /** 이번 라운드에만 걸린 것. 라운드가 끝나면 사라집니다. */
  roundRules: RuleDelta[]
  /** 다음 라운드에 걸릴 것. 라운드가 시작할 때 `roundRules` 로 옮겨집니다. */
  pendingRules: RuleDelta[]

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

  /**
   * 지금까지 오른 상점 가격. `priceRisePerPurchase` 가 쌓는 값입니다.
   *
   * **규칙이 아니라 상태입니다** — 규칙은 「한 번에 얼마씩 오르는가」이고 매번 다시 세우므로,
   * 누적값을 거기 두면 다시 세울 때마다 0으로 돌아갑니다.
   */
  priceRise: number

  nextUid: number
  rng: Record<string, Pcg32>
}

/**
 * 코어가 내는 것. 연출이 이것을 받아 그립니다.
 *
 * **득점 하나가 이벤트 수십 개로 풀립니다.** 굵게 내면 연출이 보간으로 흉내를 내게 되고,
 * 그러면 「누가 얼마를 더했는가」가 화면에서 사라집니다 — 조커의 누적값과 에디션이 실제로
 * 그렇게 사라져 있었습니다.
 *
 * 값을 더한 것은 **셋 중 하나가 냅니다** — 카드가 낸 것은 `CardScored`, 조커가 낸 것은
 * `JokerTriggered`, 덱과 바우처와 보스가 낸 것은 `RunTriggered` 입니다. 셋 다 그 뒤에
 * `ChipsMultChanged` 가 따라와 지금의 칩과 배수를 알립니다.
 */
export type GameEvent =
  /** 블라인드를 건너뛰어 태그를 얻었습니다. */
  | { t: 'TagGained'; tagId: string }
  /** 들고 있던 태그가 쓰였습니다. **쓰면 없어집니다.** */
  | { t: 'TagUsed'; tagId: string }
  | { t: 'HandPlayed'; uids: number[] }
  | { t: 'HandEvaluated'; hand: PokerHandKind; level: number; chips: number; mult: number; cards: number[] }
  | { t: 'CardScored'; uid: number; op: string; chips: number; mult: number; money: number; source: string }
  | { t: 'JokerTriggered'; slot: number; jokerId: string; op: string; chips: number; mult: number; money: number }
  | { t: 'RunTriggered'; owner: string; op: string; chips: number; mult: number; money: number }
  | { t: 'JokerFizzled'; slot: number; jokerId: string; num: number; den: number }
  | { t: 'Retriggered'; uid: number; times: number }
  | { t: 'ChipsMultChanged'; chips: number; mult: number }
  | { t: 'ScoreResolved'; score: number; target: number }
  | { t: 'HandDiscarded'; uids: number[] }
  | { t: 'HandDrawn'; uids: number[] }
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
  | {
    t: 'RuleChanged'
    /** 어느 규칙인가. `RuleKind` 의 이름입니다. */
    rule: string
    /** 바뀌기 전과 뒤의 값. 값을 가지지 않는 규칙이면 둘 다 `null` 입니다. */
    before: number | null
    after: number | null
    /** 켜고 끄는 규칙인가. 수를 세는 규칙과 읽는 법이 다릅니다. */
    flag: boolean
  }
