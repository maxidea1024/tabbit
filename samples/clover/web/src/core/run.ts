// 런의 흐름.
//
// **코어의 경계가 여기입니다** — 입력은 액션 하나, 출력은 상태와 이벤트 배열입니다. 코어는
// 시간을 모르고 프레임을 모르고 애니메이션을 모릅니다. 연출이 이벤트를 자기 속도로
// 재생합니다.

import { JokerPool } from '../generated/enums/joker-pool'
import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { RuleKind } from '../generated/enums/rule-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { Trigger } from '../generated/enums/trigger'
import type { Data } from './data'
import { streamRng, type Pcg32 } from './rng'
import { scoreHand } from './scoring'
import { stakeRow } from './stake'
import { newCounters, type CardInstance, type GameEvent, type JokerInstance, type RunState, type Rules } from './state'
import {
  changeRule, collect, newVm, RUN_HOST, runCardEffects, runRow, runTrigger, sellPrice,
  stickerFor, type EffectHost, type Vm,
} from './vm'
import { emptyShop, openPack, rerollCost, stock } from './shop'
import { bossPool, tagPool } from './pool'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { newCounters as freshCounters } from './state'
import type { EffectRow } from './data'

export type Action =
  | { t: 'select_blind' }
  | { t: 'skip_blind' }
  | { t: 'play'; cards: number[] }
  | { t: 'discard'; cards: number[] }
  | { t: 'use_consumable'; index: number; targets?: number[] }
  | { t: 'sell_joker'; index: number }
  | { t: 'sell_consumable'; index: number }
  | { t: 'buy'; slot: number }
  /** 자리가 없을 때. `index` 가 팔 것이고 `slot` 이 그 자리에 놓을 것입니다. */
  | { t: 'swap'; slot: number; index: number }
  | { t: 'buy_pack'; slot: number }
  | { t: 'pick_pack'; index: number }
  /**
   * 자리를 비우고 팩의 한 장을 집습니다.
   *
   * **`pick_pack` 은 자리가 없으면 아무것도 하지 않습니다.** 그러면 화면에서는 눌렀는데
   * 아무 일도 일어나지 않는 것이 되므로, 무엇을 내놓을지 정하는 길이 따로 있어야 합니다 —
   * 상점의 `swap` 과 같은 짝이고 다만 값이 오가지 않습니다.
   */
  | { t: 'swap_pack'; index: number; held: number }
  | { t: 'skip_pack' }
  | { t: 'buy_voucher' }
  | { t: 'reroll' }
  | { t: 'leave_shop' }

export interface Step {
  state: RunState
  events: GameEvent[]
}

const STREAMS = [
  'Shuffle', 'ShopSlot', 'ShopRarity', 'ShopVoucher', 'Pack',
  'JokerProc', 'CardProc', 'Boss', 'Tag', 'Misprint', 'Sticker',
]

export function defaultRules(data: Data): Rules {
  const run = data.run
  const economy = data.economy
  return {
    handSize: run.handSize,
    handsPerRound: run.handsPerRound,
    discardsPerRound: run.discardsPerRound,
    jokerSlots: run.jokerSlots,
    consumableSlots: run.consumableSlots,
    debtLimit: 0,
    freeRerolls: 0,
    rerollCostDelta: 0,
    rerollStartsFree: false,
    interestPer5: economy.interestPer5,
    interestCap: economy.interestCap,
    shopCardSlots: economy.shopCardSlots,
    shopDiscount: 0,
    shopAllowsPlayingCards: false,
    shopAllowsSpectral: false,
    shopWeightTarotScale: 1,
    shopWeightPlanetScale: 1,
    freePlanets: false,
    allCardsScore: false,
    allCardsAreFace: false,
    flushStraightCards: 5,
    straightGap: 0,
    suitsMerged: false,
    probabilityScale: 1,
    allowDuplicates: false,
    balanceChipsAndMult: false,
    bossRerollsPerAnte: 0,
    anteDelta: 0,
    editionWeightScale: 1,
    planetGivesMultBp: 0,
    noInterest: false,
    moneyPerHandLeft: economy.moneyPerHandLeft,
    moneyPerDiscardLeft: economy.moneyPerDiscardLeft,
    doubleTagOnBossDefeat: false,
    blindSizeScaleBp: 10_000,
    nextShopFree: false,
    noSmallBlindReward: false,
    noBigBlindReward: false,
    noBossBlindReward: false,
    chipsCappedByMoney: false,
    faceDownDrawRate: 0,
    handSizePerMoney: 0,
    allJokersEternal: false,
    debuffPlayedAfterScoring: false,
    priceRisePerPurchase: 0,
    noJokersInShop: false,
    discardCost: 0,
    pinnedJokerSlot: 0,
    noRepeatHandTypes: false,
    singleHandTypeOnly: false,
    mustPlayFiveCards: false,
    alwaysDrawThree: false,
    halveBaseChipsAndMult: false,
    debuffUntilJokerSold: false,
    forceCardSelected: false,
  }
}

/**
 * 이번 안테의 태그 둘을 뽑습니다.
 *
 * **`Tag` 흐름만 씁니다.** 흐름이 갈라져 있으므로 이 자리에서 난수를 더 쓰더라도 덱 섞기와
 * 상점과 확률 발동은 같은 시드에서 그대로입니다.
 */
function rollTagOffer(data: Data, state: RunState): void {
  const pool = tagPool(data, state).filter(row => row.minAnte <= state.ante)
  state.tagOffer = pool.length === 0 ? [] : [
    pool[state.rng.Tag.below(pool.length)].tagId,
    pool[state.rng.Tag.below(pool.length)].tagId,
  ]
}

/** 지금 블라인드를 건너뛰면 받는 태그. 보스는 건너뛸 수 없으므로 없습니다. */
export function tagFor(state: RunState, blind: BlindKind): string | undefined {
  if (blind === BlindKind.Small) return state.tagOffer[0]
  if (blind === BlindKind.Big) return state.tagOffer[1]
  return undefined
}

/** 런 하나를 시작합니다. */
export function newRun(data: Data, seed: string, deckId: string, stake: string,
                       pools: JokerPool[] = [JokerPool.Base],
                       challengeId = ''): Step {
  // **챌린지는 조커 150종으로 돕니다.** 원작의 금지 목록이 그 150종을 상대로 쓰였으므로,
  // 확장 350종이 켜지면 금지가 걸린 채로 금지의 뜻이 없어집니다.
  if (challengeId !== '') pools = [JokerPool.Base]

  const rng: Record<string, Pcg32> = {}
  for (const stream of STREAMS) rng[stream] = streamRng(seed, stream)

  const state: RunState = {
    seed, deckId, stake, pools, challengeId,
    phase: 'blind-select',
    ante: 1,
    blind: BlindKind.Small,
    bossId: '',
    bossesSeen: [],
    money: data.run.startingMoney,
    handsLeft: 0,
    discardsLeft: 0,
    score: 0,
    target: 0,
    deck: [],
    drawPile: [],
    hand: [],
    played: [],
    discarded: [],
    selected: [],
    jokers: [],
    consumables: [],
    vouchers: [],
    tagsPending: [],
    tagOffer: [],
    ruleDeltas: [],
    roundRules: [],
    pendingRules: [],
    handLevels: {},
    handPlayCounts: {},
    handsPlayedThisRun: 0,
    discardsUnusedThisRun: 0,
    blindsSkipped: 0,
    tarotUsed: 0,
    planetsUsed: [],
    handTypesThisRound: [],
    cardsPlayedThisAnte: [],
    targets: rollTargets(data, rng.Boss),
    rules: defaultRules(data),
    bossTriggeredThisHand: false,
    bossDisabled: false,
    duplicateNextTag: false,
    shop: emptyShop(),
    pack: null,
    priceRise: 0,
    nextUid: 1,
    rng,
  }

  // 표준 52장. 덱 효과가 이 뒤에 걸러내거나 바꿉니다.
  //
  // **챌린지가 시작 덱을 적어 두었으면 그것이 표준을 대신합니다.** 연산으로 더하고 빼는
  // 것이 아니라 「무엇이 몇 벌」의 목록이므로, 덱에 몇 장이 있는지를 표에서 셀 수 있습니다.
  const spec = challengeId === ''
    ? [] : (data.tables.challengeCard.records.filter(row => row.owner === challengeId))
  for (const row of data.tables.baseDeckCard.records) {
    const copies = spec.length === 0 ? 1 : spec.reduce((sum, one) =>
      sum + ((one.ranks.length === 0 || one.ranks.includes(row.rank))
             && (one.suits.length === 0 || one.suits.includes(row.suit)) ? one.copies : 0), 0)
    const look = spec.find(one =>
      (one.ranks.length === 0 || one.ranks.includes(row.rank))
      && (one.suits.length === 0 || one.suits.includes(row.suit)))
    for (let i = 0; i < copies; i++) {
      state.deck.push({
        uid: state.nextUid++,
        baseCardId: row.cardId,
        rank: row.rank,
        suit: row.suit,
        enhancement: look?.enhancement ?? EnhancementKind.None,
        seal: look?.seal ?? SealKind.None,
        edition: look?.edition ?? EditionKind.Base,
        bonusChips: 0,
        debuffed: false,
        faceDown: false,
      })
    }
  }

  const vm = newVm(data, state)
  runTrigger(vm, Trigger.OnRunStart)
  rebuildRules(vm)
  pickBoss(vm)
  rollTagOffer(data, state)
  state.target = blindTarget(vm)

  return { state, events: vm.events }
}

/**
 * 규칙을 처음부터 다시 세웁니다.
 *
 * **누적하지 않습니다.** 누적으로 두면 원인이 사라지거나 새로 생겼을 때 아무도 다시 계산하지
 * 않습니다 — 보스가 걸어 둔 것이 보스가 지나가도 남고, 상점에서 산 조커의 것은 아예 걸리지
 * 않았습니다. 그래서 무언가 달라질 때마다 기본값에서 다시 쌓습니다.
 *
 * 쌓는 차례가 규칙입니다.
 *
 * 1. 기본값과 스테이크
 * 2. 한 번 걸리고 남는 것들 — 원인이 이미 없어진 것들입니다
 * 3. 지금 있는 것들의 `Passive` — 덱 · 바우처 · 보스 · 조커 · 태그
 * 4. 이번 라운드에만 걸린 것
 */
export function rebuildRules(vm: Vm): void {
  const state = vm.state
  state.rules = defaultRules(vm.data)
  applyStake(vm)

  // **다시 세우는 동안에는 아무것도 적지 않습니다.** 그러지 않으면 다시 얹는 것이 그때마다
  // 목록에 한 줄씩 더해져 규칙이 걸릴수록 불어납니다.
  vm.rebuilding = true
  for (const delta of state.ruleDeltas) {
    changeRule(vm, delta.rule as RuleKind, delta.value, delta.absolute, [])
  }
  runTrigger(vm, Trigger.Passive)
  for (const delta of state.roundRules) {
    changeRule(vm, delta.rule as RuleKind, delta.value, delta.absolute, [])
  }
  vm.rebuilding = false
}

/** 스테이크가 더하는 규칙. 표의 값이 그 스테이크에서의 최종값입니다. */
function applyStake(vm: Vm): void {
  const row = stakeRow(vm.data, vm.state.stake)
  if (!row) return
  vm.state.rules.discardsPerRound += row.discardsDelta
}

/** 라운드마다 바뀌는 지정 대상. */
function rollTargets(data: Data, rng: Pcg32) {
  const hands = data.tables.pokerHand.records
  const ranks = data.tables.rank.records
  const suits = data.tables.suit.records
  return {
    hand: hands[rng.below(hands.length)].hand,
    rank: ranks[rng.below(ranks.length)].rank,
    suit: suits[rng.below(suits.length)].suit,
    cardRank: ranks[rng.below(ranks.length)].rank,
    cardSuit: suits[rng.below(suits.length)].suit,
  }
}

/** 이번 안테의 보스. 후보가 바닥나면 다시 채웁니다. */
function pickBoss(vm: Vm): void {
  const state = vm.state
  const showdown = state.ante % vm.data.run.showdownEvery === 0
  let pool = bossPool(vm.data, state).filter(
    row => row.isShowdown === showdown && row.minAnte <= state.ante)

  const fresh = pool.filter(row => !state.bossesSeen.includes(row.bossId))
  if (fresh.length > 0) pool = fresh
  else state.bossesSeen = []

  if (pool.length === 0) return
  const chosen = pool[state.rng.Boss.below(pool.length)]
  state.bossId = chosen.bossId
  state.bossesSeen.push(chosen.bossId)
}

/**
 * 그 블라인드의 요구 점수.
 *
 * **지금 것이 아닌 것도 셀 수 있어야 합니다** — 블라인드 셋을 한 자리에 세우려면 아직
 * 오지 않은 것의 요구 점수도 화면에 적혀야 합니다.
 */
export function targetOf(data: Data, state: RunState, blind: BlindKind): number {
  const column = stakeRow(data, state.stake)?.anteColumn ?? 1

  const ante = Math.max(0, state.ante + state.rules.anteDelta)
  const row = data.tables.ante.findByAnte(Math.min(ante, 8))
  let base = column === 3 ? row?.basePurple : column === 2 ? row?.baseGreen : row?.baseWhite
  base = base ?? 100

  // 안테 9 이상은 표가 아니라 식입니다. **원작의 값을 수집하지 못했으므로 우리 값입니다.**
  for (let step = 8; step < ante; step++) {
    base = Math.floor((base * data.run.endlessGrowthBp) / 10_000)
  }

  const blindRow = data.tables.blind.getByBlindOrThrow(blind)
  let mul = blindRow.scoreMul
  if (blind === BlindKind.Boss) {
    mul = data.tables.bossBlind.findByBossId(state.bossId)?.scoreMul ?? mul
  }

  return Math.floor((Math.floor((base * mul) / 10_000) * state.rules.blindSizeScaleBp) / 10_000)
}

/** 이번 블라인드의 요구 점수. */
function blindTarget(vm: Vm): number {
  return targetOf(vm.data, vm.state, vm.state.blind)
}

/** 라운드를 시작합니다. 패를 채우고 자원을 되돌립니다. */
function beginRound(vm: Vm): void {
  const state = vm.state
  state.phase = 'round'
  state.score = 0

  // **예약된 것이 이 라운드에 걸립니다.** 태그 하나가 다음 라운드의 손패를 늘리는 것이
  // 이것이고, 라운드가 끝나면 사라집니다.
  state.roundRules = state.pendingRules
  state.pendingRules = []
  // **보스가 여기서 규칙에 들어옵니다.** 보스는 자기 블라인드에서만 `collect` 에 잡히므로,
  // 블라인드가 정해진 다음에 다시 세워야 그 효과가 걸립니다.
  rebuildRules(vm)

  state.handsLeft = state.rules.handsPerRound
  state.discardsLeft = state.rules.discardsPerRound
  state.handTypesThisRound = []
  state.bossTriggeredThisHand = false
  state.target = blindTarget(vm)

  state.drawPile = state.deck.map(card => card.uid)
  state.hand = []
  state.played = []
  state.discarded = []
  for (const card of state.deck) {
    card.debuffed = false
    card.faceDown = false
  }

  state.rng.Shuffle.shuffle(state.drawPile)
  runTrigger(vm, Trigger.OnBlindSelect)
  runTrigger(vm, Trigger.OnRoundStart)
  draw(vm)
}

/**
 * 패가 찰 때까지 뽑습니다.
 *
 * **뽑은 것을 알립니다.** 화면은 이 이벤트를 받을 때까지 새 카드를 그리지 않습니다 — 득점
 * 연출이 도는 동안 다음 패가 이미 깔려 있으면, 무엇을 낸 판인지가 흐려집니다.
 */
/**
 * 지금의 패 크기.
 *
 * **규칙이 아니라 그 시점의 값입니다.** 보유액이 라운드 도중에 바뀌므로, 규칙에 담으면
 * 규칙을 다시 세울 때까지 따라오지 않습니다 — 그래서 쓰는 자리에서 셉니다.
 */
export function handSizeNow(state: RunState): number {
  const per = state.rules.handSizePerMoney
  if (per <= 0) return state.rules.handSize
  return Math.max(1, state.rules.handSize - Math.floor(Math.max(0, state.money) / per))
}

function draw(vm: Vm, limit?: number): void {
  const state = vm.state
  const want = limit ?? handSizeNow(state)
  const drawn: number[] = []
  while (state.hand.length < want && state.drawPile.length > 0) {
    const uid = state.drawPile.shift() as number
    state.hand.push(uid)
    drawn.push(uid)

    // **뽑을 때마다 굴립니다.** 세는 것으로 적으면 뽑는 장수가 라운드마다 달라질 때
    // 어디서부터 세는지가 정해지지 않습니다. `CardProc` 흐름이므로 덱 섞기와 갈라집니다.
    const rate = state.rules.faceDownDrawRate
    if (rate > 0) {
      const card = state.deck.find(entry => entry.uid === uid)
      if (card && state.rng.CardProc.below(rate) === 0) card.faceDown = true
    }
  }
  if (drawn.length > 0) vm.events.push({ t: 'HandDrawn', uids: drawn })
}

/**
 * 그 시점에 뜻을 가지는 태그를 쓰고, 쓴 것을 버립니다.
 *
 * **태그는 한 번 쓰면 없어집니다.** 들고 있는 것으로만 두면 상점에 들어갈 때마다 같은 태그가
 * 다시 돕니다.
 */
function useTags(vm: Vm, trigger: Trigger): void {
  const state = vm.state
  const spent: string[] = []

  for (const tag of state.tagsPending) {
    const rows = (vm.data.tagEffects.get(tag) ?? []).filter(row => row.trigger === trigger)
    if (rows.length === 0) continue
    for (const row of rows) runRow(vm, row, RUN_HOST)
    spent.push(tag)
  }

  for (const tag of spent) {
    const at = state.tagsPending.indexOf(tag)
    if (at >= 0) state.tagsPending.splice(at, 1)
    vm.events.push({ t: 'TagUsed', tagId: tag })
  }

  // 태그가 규칙을 걸었을 수 있습니다. 들고 있는 목록이 바뀌었으므로 다시 세웁니다.
  if (spent.length > 0) rebuildRules(vm)
}

/**
 * 그 블라인드를 깨면 받는 금액.
 *
 * **화면도 이 함수를 씁니다.** `Blind` 표의 값을 화면이 직접 읽으면 규칙이 보상을 껐을 때
 * 화면에는 그대로 적혀 있게 됩니다 — `dry_season` 에서 「격파 보상 $3」이 적혀 있었습니다.
 */
export function rewardOf(data: Data, state: RunState, blind: BlindKind): number {
  let reward = data.tables.blind.findByBlind(blind)?.reward ?? 0

  const stake = stakeRow(data, state.stake)
  if (blind === BlindKind.Small && stake) reward = stake.smallBlindReward

  // **보스는 자기 보상을 가집니다.** 최종 보스가 나머지보다 많이 주므로, 블라인드의 값
  // 하나로는 그 둘이 구분되지 않습니다.
  if (blind === BlindKind.Boss) {
    const boss = data.tables.bossBlind.findByBossId(state.bossId)
    if (boss) reward = boss.reward
  }

  // 챌린지가 갈래마다 따로 끕니다 — 셋 다인 것과 스몰·빅만인 것이 있습니다.
  const rules = state.rules
  if ((blind === BlindKind.Small && rules.noSmallBlindReward)
      || (blind === BlindKind.Big && rules.noBigBlindReward)
      || (blind === BlindKind.Boss && rules.noBossBlindReward)) reward = 0

  return reward
}

/** 라운드를 이깁니다. 보상과 이자를 정산합니다. */
function winRound(vm: Vm): void {
  const state = vm.state
  const reward = rewardOf(vm.data, state, state.blind)

  state.money += reward
  vm.events.push({ t: 'BlindCleared', blind: state.blind, reward })

  // **들어오는 돈은 갈래마다 따로 알립니다.** 합계만 알리면 무엇으로 번 것인지 알 수 없고,
  // 연출도 한 번에 끝나 버립니다.
  if (reward !== 0) vm.events.push({ t: 'MoneyChanged', delta: reward, reason: 'blind' })

  if (!state.rules.noInterest) {
    const interest = Math.min(
      state.rules.interestCap,
      Math.floor(Math.max(0, state.money) / 5) * state.rules.interestPer5)
    state.money += interest
    if (interest !== 0) vm.events.push({ t: 'MoneyChanged', delta: interest, reason: 'interest' })
  }

  const fromHands = state.handsLeft * state.rules.moneyPerHandLeft
  state.money += fromHands
  if (fromHands !== 0) {
    vm.events.push({ t: 'MoneyChanged', delta: fromHands, reason: 'hands_left' })
  }

  const fromDiscards = state.discardsLeft * state.rules.moneyPerDiscardLeft
  state.money += fromDiscards
  if (fromDiscards !== 0) {
    vm.events.push({ t: 'MoneyChanged', delta: fromDiscards, reason: 'discards_left' })
  }
  state.discardsUnusedThisRun += state.discardsLeft

  if (state.blind === BlindKind.Boss) {
    runTrigger(vm, Trigger.OnBossDefeated)
    useTags(vm, Trigger.OnBossDefeated)
    state.cardsPlayedThisAnte = []
  }

  runTrigger(vm, Trigger.OnRoundEnd)
  ageJokers(vm)

  // 라운드가 끝나면 패를 치웁니다. **여기서 비우지 않으면 상점에 카드가 남습니다** —
  // 화면이 `state.hand` 를 그대로 그리기 때문입니다.
  state.hand = []
  state.drawPile = []
  state.played = []
  state.discarded = []

  state.phase = 'shop'

  // **이번 라운드에만 걸린 것과 보스가 여기서 빠집니다.** 단계가 바뀐 다음이어야 합니다 —
  // 보스는 판을 두는 동안에만 `collect` 에 잡히므로, 그 전에 세우면 아직 남아 있습니다.
  state.roundRules = []
  rebuildRules(vm)

  runTrigger(vm, Trigger.OnShopEnter)
  useTags(vm, Trigger.OnShopEnter)
  state.shop.rerollsUsed = 0
  stock(vm, state.shop)
}

/** `Perishable` 은 5라운드 뒤에 무력화됩니다. */
function ageJokers(vm: Vm): void {
  for (const joker of vm.state.jokers) {
    joker.age++
    if (joker.sticker === 2 && joker.age >= 5) joker.disabled = true
    if (joker.sticker === 3) vm.state.money -= 3
  }
}

/** 다음 블라인드로. 보스를 넘겼으면 안테가 오릅니다. */
function advance(vm: Vm): void {
  const state = vm.state

  if (state.blind === BlindKind.Boss) {
    state.ante++
    state.blind = BlindKind.Small
    state.bossesSeen = []
    pickBoss(vm)
    rollTagOffer(vm.data, state)
    if (state.ante > vm.data.run.winAnte) {
      state.phase = 'won'
      vm.events.push({ t: 'RunWon', ante: state.ante - 1 })
      return
    }
  } else {
    state.blind = state.blind === BlindKind.Small ? BlindKind.Big : BlindKind.Boss
  }

  state.targets = rollTargets(vm.data, state.rng.Boss)
  state.phase = 'blind-select'
  state.target = blindTarget(vm)
}

/**
 * 조커 하나를 팝니다.
 *
 * `Eternal` 은 팔리지 않습니다 — 그때는 아무 일도 없고 `false` 를 냅니다.
 */
function sellJoker(vm: Vm, index: number): boolean {
  const state = vm.state
  const joker = state.jokers[index]
  if (!joker || joker.sticker === 1) return false

  for (const row of vm.data.jokerEffects.get(joker.jokerId) ?? []) {
    if (row.trigger === Trigger.OnSell) {
      runOne(vm, row, { kind: 'joker', joker, slot: index })
    }
  }

  const price = sellPrice(vm, joker)
  state.money += price
  vm.events.push({ t: 'MoneyChanged', delta: price, reason: 'sell' })
  state.jokers.splice(index, 1)
  vm.events.push({ t: 'JokerDestroyed', uid: joker.uid, jokerId: joker.jokerId })
  runTrigger(vm, Trigger.OnJokerSold)
  // **판 조커의 규칙이 여기서 빠집니다.** 다시 세우면 그것이 없는 상태로 계산됩니다.
  rebuildRules(vm)
  state.rules.debuffUntilJokerSold = false
  return true
}

/** 소모품 하나를 팝니다. 값은 어느 것이나 같습니다. */
function sellConsumable(vm: Vm, index: number): boolean {
  const state = vm.state
  const item = state.consumables[index]
  if (!item) return false

  const price = vm.data.economy.sellMin
  state.money += price
  vm.events.push({ t: 'MoneyChanged', delta: price, reason: 'sell' })
  state.consumables.splice(index, 1)
  return true
}

/** 액션 하나. **코어의 표면이 이것 하나입니다.** */
export function apply(data: Data, state: RunState, action: Action): Step {
  const vm = newVm(data, state)

  switch (action.t) {
    case 'select_blind':
      if (state.phase !== 'blind-select') break
      beginRound(vm)
      break

    case 'skip_blind': {
      if (state.phase !== 'blind-select' || state.blind === BlindKind.Boss) break
      state.blindsSkipped++
      // **적혀 있던 그 태그를 받습니다.** 여기서 뽑으면 카드에 적힌 것과 달라집니다.
      const tag = tagFor(state, state.blind)
      if (tag) {
        state.tagsPending.push(tag)
        vm.events.push({ t: 'TagGained', tagId: tag })
        // **뽑는 그 자리에서 도는 것은 `OnUse` 뿐입니다.** 나머지는 상점에 들어갈 때나 다음
        // 라운드에 뜻을 가지므로 들고 있다가 그때 돕니다.
        useTags(vm, Trigger.OnUse)
      }
      advance(vm)
      break
    }

    case 'play': {
      if (state.phase !== 'round' || state.handsLeft <= 0) break
      const cards = action.cards
        .map(uid => state.deck.find(card => card.uid === uid))
        .filter((card): card is CardInstance => card !== undefined)
      if (cards.length === 0 || cards.length > data.run.maxPlayedCards) break

      state.handsLeft--
      state.handsPlayedThisRun++
      state.hand = state.hand.filter(uid => !action.cards.includes(uid))
      state.played = action.cards.slice()
      for (const uid of action.cards) state.cardsPlayedThisAnte.push(uid)

      // **낸 것이 판에 올라가는 것도 사건입니다.** 카드가 아직 날아가는 중에 득점이 시작되면
      // 다섯 장이 한 덩어리로 보이고, 무엇을 냈는지가 남지 않습니다.
      vm.events.push({ t: 'HandPlayed', uids: action.cards.slice() })

      const result = scoreHand(vm, cards)
      const name = PokerHandKind[result.hand]
      state.handPlayCounts[name] = (state.handPlayCounts[name] ?? 0) + 1
      state.handTypesThisRound.push(name)
      state.score += result.score

      vm.scoring = undefined
      runTrigger(vm, Trigger.OnScoreResolved)

      // **라운드가 끝나도 풀리지 않습니다.** 보스가 거는 무력화와 다른 갈래입니다.
      if (state.rules.debuffPlayedAfterScoring) {
        for (const uid of state.played) {
          const card = state.deck.find(entry => entry.uid === uid)
          if (card) card.debuffed = true
        }
      }

      // 낸 카드는 덱으로 돌아가지 않습니다 — 이번 라운드에는 다시 뽑히지 않습니다.
      state.played = []

      if (state.score >= state.target) {
        winRound(vm)
      } else if (state.handsLeft <= 0) {
        // **패배를 막는 것은 라운드를 넘기는 것입니다.**
        //
        // 막기만 하고 아무것도 하지 않았습니다 — 판정을 건너뛰고 다음 패를 깔았는데, 낼
        // 핸드가 0이므로 그 패로는 아무것도 할 수 없습니다. 버리기가 남아 있으면 버리는 것만
        // 되고, 그것으로는 점수가 오르지 않으므로 **그 라운드가 영영 끝나지 않습니다.**
        // 「패배를 막습니다」는 죽지 않는다는 뜻이고, 죽지 않았으면 그 블라인드를 넘긴
        // 것입니다.
        if (vm.lossPrevented) {
          winRound(vm)
        } else {
          state.phase = 'lost'
          vm.events.push({ t: 'RunLost', ante: state.ante })
        }
      } else {
        draw(vm)
      }
      break
    }

    case 'discard': {
      if (state.phase !== 'round' || state.discardsLeft <= 0) break
      // **돈이 모자라면 버릴 수 없습니다.** 빚 한도가 있으면 그만큼까지입니다.
      const cost = state.rules.discardCost
      if (cost > 0 && state.money - cost < state.rules.debtLimit) break
      state.discardsLeft--
      if (cost > 0) {
        state.money -= cost
        vm.events.push({ t: 'MoneyChanged', delta: -cost, reason: 'discard' })
      }
      state.discarded = action.cards.slice()
      state.hand = state.hand.filter(uid => !action.cards.includes(uid))
      vm.events.push({ t: 'HandDiscarded', uids: action.cards.slice() })

      for (const uid of action.cards) {
        const card = state.deck.find(entry => entry.uid === uid)
        if (!card) continue
        vm.scoring = undefined
        runCardDiscard(vm, card)
      }
      runTrigger(vm, Trigger.OnHandDiscarded)
      state.discarded = []
      draw(vm)
      break
    }

    case 'use_consumable': {
      const item = state.consumables[action.index]
      if (!item) break
      vm.selection = (action.targets ?? [])
        .map(uid => state.deck.find(card => card.uid === uid))
        .filter((card): card is CardInstance => card !== undefined)
      vm.lastConsumableKind = item.kind

      const rows = item.kind === 1
        ? data.tarotEffects.get(item.id)
        : item.kind === 3 ? data.spectralEffects.get(item.id) : undefined

      if (item.kind === 2) {
        const planet = data.tables.planet.findByPlanetId(item.id)
        if (planet) {
          const name = PokerHandKind[planet.hand]
          state.handLevels[name] = (state.handLevels[name] ?? 1) + 1
          vm.events.push({ t: 'HandLevelled', hand: planet.hand, level: state.handLevels[name] })
          if (!state.planetsUsed.includes(item.id)) state.planetsUsed.push(item.id)
        }
      }

      for (const row of rows ?? []) {
        if (row.trigger === Trigger.OnUse) runOne(vm, row)
      }

      if (item.kind === 1) state.tarotUsed++
      state.consumables.splice(action.index, 1)
      vm.events.push({ t: 'ConsumableUsed', id: item.id })
      runTrigger(vm, Trigger.OnConsumableUsed)
      break
    }

    case 'sell_joker':
      sellJoker(vm, action.index)
      break

    case 'sell_consumable':
      sellConsumable(vm, action.index)
      break

    case 'buy': {
      if (state.phase !== 'shop') break
      const item = state.shop.cards[action.slot]
      if (!item || state.money - item.cost < -state.rules.debtLimit) break
      if (!takeItem(vm, item)) break
      state.money -= item.cost
      state.priceRise += state.rules.priceRisePerPurchase
      vm.events.push({ t: 'MoneyChanged', delta: -item.cost, reason: 'shop' })
      state.shop.cards.splice(action.slot, 1)
      break
    }

    /**
     * 자리가 없을 때 하나를 팔고 그 자리에 새로 놓습니다.
     *
     * **파는 것과 사는 것이 한 액션입니다.** 둘로 나누면 판 값을 받은 다음 사기 전에 판이
     * 한 번 멈추고, 그 사이에 다른 것을 눌러 값만 잃을 수 있습니다.
     */
    case 'swap': {
      if (state.phase !== 'shop') break
      const item = state.shop.cards[action.slot]
      if (!item) break

      const joker = item.kind === ShopItemKind.Joker
      const sold = joker ? sellJoker(vm, action.index) : sellConsumable(vm, action.index)
      if (!sold) break

      if (state.money - item.cost < -state.rules.debtLimit) break
      if (!takeItem(vm, item)) break
      state.money -= item.cost
      state.priceRise += state.rules.priceRisePerPurchase
      vm.events.push({ t: 'MoneyChanged', delta: -item.cost, reason: 'shop' })
      state.shop.cards.splice(action.slot, 1)
      break
    }

    // 팩은 사는 순간 물건이 들어오지 않습니다. 뜯어 놓고 고르게 합니다.
    case 'buy_pack': {
      if (state.phase !== 'shop' || state.pack) break
      const packId = state.shop.packs[action.slot]
      if (!packId) break
      const row = data.tables.boosterPack.findByPackId(packId)
      if (!row || state.money - row.cost < -state.rules.debtLimit) break

      const open = openPack(vm, packId)
      if (!open) break

      state.money -= row.cost
      state.priceRise += state.rules.priceRisePerPurchase
      vm.events.push({ t: 'MoneyChanged', delta: -row.cost, reason: 'shop' })
      state.shop.packs.splice(action.slot, 1)
      state.pack = open
      vm.events.push({ t: 'PackOpened', packId })
      break
    }

    case 'pick_pack': {
      const open = state.pack
      if (!open || open.picksLeft <= 0) break
      const item = open.options[action.index]
      if (!item || open.taken[action.index]) break
      // **자리가 없으면 고르지 못합니다.** 팩은 그대로 열려 있으므로 다른 것을 고르거나
      // 건너뜁니다.
      if (!takeItem(vm, item)) break

      open.taken[action.index] = true
      open.picksLeft--
      if (open.picksLeft <= 0) {
        state.pack = null
        vm.events.push({ t: 'PackClosed' })
      }
      break
    }

    case 'swap_pack': {
      const open = state.pack
      if (!open || open.picksLeft <= 0) break
      const item = open.options[action.index]
      if (!item || open.taken[action.index]) break

      const joker = item.kind === ShopItemKind.Joker
      const sold = joker ? sellJoker(vm, action.held) : sellConsumable(vm, action.held)
      if (!sold) break
      if (!takeItem(vm, item)) break

      open.taken[action.index] = true
      open.picksLeft--
      if (open.picksLeft <= 0) {
        state.pack = null
        vm.events.push({ t: 'PackClosed' })
      }
      break
    }

    case 'skip_pack':
      if (!state.pack) break
      state.pack = null
      vm.events.push({ t: 'PackClosed' })
      break

    case 'buy_voucher': {
      if (state.phase !== 'shop' || !state.shop.voucher) break
      const cost = data.economy.voucherCost
      if (state.money - cost < -state.rules.debtLimit) break
      state.money -= cost
      state.vouchers.push(state.shop.voucher)
      // **다시 세우면 산 바우처가 함께 얹힙니다.** 그 자리에서 한 줄만 돌리면 나중에 다시
      // 세울 때 그 바우처만 빠집니다.
      rebuildRules(vm)
      state.shop.voucher = null
      state.shop.voucherBought = true
      break
    }

    case 'reroll': {
      if (state.phase !== 'shop') break
      const cost = rerollCost(data, state, state.shop)
      if (state.money - cost < -state.rules.debtLimit) break
      state.money -= cost
      state.shop.rerollsUsed++
      runTrigger(vm, Trigger.OnReroll)
      stock(vm, state.shop)
      break
    }

    case 'leave_shop':
      if (state.phase !== 'shop' || state.pack) break
      runTrigger(vm, Trigger.OnShopExit)
      state.rules.nextShopFree = false
      state.pack = null
      state.shop = emptyShop()
      state.shop.voucherBought = false
      advance(vm)
      break
  }

  return { state, events: vm.events }
}

/** 산 것을 실제로 받습니다. 자리가 없으면 사지 못합니다. */
function takeItem(vm: Vm, item: {
  kind: ShopItemKind; id: string; edition: number
  enhancement?: EnhancementKind; seal?: SealKind
}): boolean {
  const state = vm.state

  switch (item.kind) {
    case ShopItemKind.Joker: {
      const negative = item.edition === 4
      if (!negative && state.jokers.length >= state.rules.jokerSlots) return false
      state.jokers.push({
        uid: state.nextUid++,
        jokerId: item.id,
        edition: item.edition as never,
        sticker: stickerFor(vm, item.id, 0) as never,
        counters: freshCounters(),
        age: 0,
        disabled: false,
      })
      vm.events.push({ t: 'JokerAdded', uid: state.nextUid - 1, jokerId: item.id })
      // **조커가 걸어 두는 규칙이 여기서 걸립니다.** 넣기만 하면 손패를 늘리는 조커를 사도
      // 손패가 그대로였습니다.
      rebuildRules(vm)
      return true
    }

    case ShopItemKind.Tarot:
    case ShopItemKind.Planet:
    case ShopItemKind.Spectral: {
      if (state.consumables.length >= state.rules.consumableSlots) return false
      const kind = item.kind === ShopItemKind.Tarot ? 1 : item.kind === ShopItemKind.Planet ? 2 : 3
      state.consumables.push({
        uid: state.nextUid++, kind: kind as never, id: item.id, edition: item.edition as never,
      })
      vm.events.push({ t: 'ConsumableAdded', uid: state.nextUid - 1, id: item.id })
      return true
    }

    case ShopItemKind.PlayingCard: {
      const base = vm.data.tables.baseDeckCard.findByCardId(item.id)
      if (!base) return false
      state.deck.push({
        uid: state.nextUid++,
        baseCardId: base.cardId,
        rank: base.rank,
        suit: base.suit,
        enhancement: item.enhancement ?? EnhancementKind.None,
        seal: item.seal ?? SealKind.None,
        edition: item.edition as never,
        bonusChips: 0,
        debuffed: false,
        faceDown: false,
      })
      vm.events.push({ t: 'CardAdded', uid: state.nextUid - 1 })
      runTrigger(vm, Trigger.OnCardAdded)
      return true
    }

    default:
      return false
  }
}

function runOne(vm: Vm, row: EffectRow, host?: EffectHost): void {
  runRow(vm, row, host ?? RUN_HOST)
}

/** 버린 카드 한 장에 반응하는 것들. 그 카드가 대상이 되도록 임자에 실어 보냅니다. */
function runCardDiscard(vm: Vm, card: CardInstance): void {
  runCardEffects(vm, Trigger.OnCardDiscarded, card)
  for (const [row, host] of collect(vm, Trigger.OnCardDiscarded)) {
    runRow(vm, row, { ...host, card })
  }
}

export { blindTarget, beginRound }
export type { JokerInstance }
export { newCounters }
