// 런의 흐름.
//
// **코어의 경계가 여기입니다** — 입력은 액션 하나, 출력은 상태와 이벤트 배열입니다. 코어는
// 시간을 모르고 프레임을 모르고 애니메이션을 모릅니다. 연출이 이벤트를 자기 속도로
// 재생합니다.

import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { Trigger } from '../generated/enums/trigger'
import type { Data } from './data'
import { streamRng, type Pcg32 } from './rng'
import { scoreHand } from './scoring'
import { newCounters, type CardInstance, type GameEvent, type JokerInstance, type RunState, type Rules } from './state'
import {
  collect, newVm, RUN_HOST, runCardEffects, runRow, runTrigger, sellPrice,
  type EffectHost, type Vm,
} from './vm'
import { emptyShop, rerollCost, stock } from './shop'
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
  | { t: 'buy_voucher' }
  | { t: 'reroll' }
  | { t: 'leave_shop' }

export interface Step {
  state: RunState
  events: GameEvent[]
}

const STREAMS = [
  'Shuffle', 'ShopSlot', 'ShopRarity', 'ShopVoucher', 'Pack',
  'JokerProc', 'CardProc', 'Boss', 'Tag', 'Misprint',
]

function defaultRules(data: Data): Rules {
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
    moneyPerHandLeft: 0,
    moneyPerDiscardLeft: 0,
    doubleTagOnBossDefeat: false,
    blindSizeScaleBp: 10_000,
    nextShopFree: false,
    noRepeatHandTypes: false,
    singleHandTypeOnly: false,
    mustPlayFiveCards: false,
    alwaysDrawThree: false,
    halveBaseChipsAndMult: false,
    debuffUntilJokerSold: false,
    forceCardSelected: false,
  }
}

/** 런 하나를 시작합니다. */
export function newRun(data: Data, seed: string, deckId: string, stake: string): Step {
  const rng: Record<string, Pcg32> = {}
  for (const stream of STREAMS) rng[stream] = streamRng(seed, stream)

  const state: RunState = {
    seed, deckId, stake,
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
    nextUid: 1,
    rng,
  }

  // 표준 52장. 덱 효과가 이 뒤에 걸러내거나 바꿉니다.
  for (const row of data.tables.baseDeckCard.records) {
    state.deck.push({
      uid: state.nextUid++,
      baseCardId: row.cardId,
      rank: row.rank,
      suit: row.suit,
      enhancement: EnhancementKind.None,
      seal: SealKind.None,
      edition: EditionKind.Base,
      bonusChips: 0,
      debuffed: false,
      faceDown: false,
    })
  }

  const vm = newVm(data, state)
  runTrigger(vm, Trigger.OnRunStart)
  runTrigger(vm, Trigger.Passive)
  applyStake(vm)
  pickBoss(vm)
  state.target = blindTarget(vm)

  return { state, events: vm.events }
}

/** 스테이크가 더하는 규칙. 표의 값이 그 스테이크에서의 최종값입니다. */
function applyStake(vm: Vm): void {
  const row = vm.data.tables.stake.records.find(
    entry => entry.name === vm.state.stake || String(entry.stake) === vm.state.stake)
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
  let pool = vm.data.tables.bossBlind.records.filter(
    row => row.isShowdown === showdown && row.minAnte <= state.ante)

  const fresh = pool.filter(row => !state.bossesSeen.includes(row.bossId))
  if (fresh.length > 0) pool = fresh
  else state.bossesSeen = []

  if (pool.length === 0) return
  const chosen = pool[state.rng.Boss.below(pool.length)]
  state.bossId = chosen.bossId
  state.bossesSeen.push(chosen.bossId)
}

/** 이번 블라인드의 요구 점수. */
function blindTarget(vm: Vm): number {
  const state = vm.state
  const stake = vm.data.tables.stake.records.find(
    row => row.name === state.stake || String(row.stake) === state.stake)
  const column = stake?.anteColumn ?? 1

  const ante = Math.max(0, state.ante + state.rules.anteDelta)
  const row = vm.data.tables.ante.findByAnte(Math.min(ante, 8))
  let base = column === 3 ? row?.basePurple : column === 2 ? row?.baseGreen : row?.baseWhite
  base = base ?? 100

  // 안테 9 이상은 표가 아니라 식입니다. **원작의 값을 수집하지 못했으므로 우리 값입니다.**
  for (let step = 8; step < ante; step++) {
    base = Math.floor((base * vm.data.run.endlessGrowthBp) / 10_000)
  }

  const blindRow = vm.data.tables.blind.getByBlindOrThrow(state.blind)
  let mul = blindRow.scoreMul
  if (state.blind === BlindKind.Boss) {
    mul = vm.data.tables.bossBlind.findByBossId(state.bossId)?.scoreMul ?? mul
  }

  return Math.floor((Math.floor((base * mul) / 10_000) * state.rules.blindSizeScaleBp) / 10_000)
}

/** 라운드를 시작합니다. 패를 채우고 자원을 되돌립니다. */
function beginRound(vm: Vm): void {
  const state = vm.state
  state.phase = 'round'
  state.score = 0
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

/** 패가 찰 때까지 뽑습니다. */
function draw(vm: Vm, limit?: number): void {
  const state = vm.state
  const want = limit ?? state.rules.handSize
  while (state.hand.length < want && state.drawPile.length > 0) {
    state.hand.push(state.drawPile.shift() as number)
  }
}

/** 라운드를 이깁니다. 보상과 이자를 정산합니다. */
function winRound(vm: Vm): void {
  const state = vm.state
  const blindRow = vm.data.tables.blind.getByBlindOrThrow(state.blind)

  let reward = blindRow.reward
  const stake = vm.data.tables.stake.records.find(
    row => row.name === state.stake || String(row.stake) === state.stake)
  if (state.blind === BlindKind.Small && stake) reward = stake.smallBlindReward

  state.money += reward
  vm.events.push({ t: 'BlindCleared', blind: state.blind, reward })

  if (!state.rules.noInterest) {
    const interest = Math.min(
      state.rules.interestCap,
      Math.floor(Math.max(0, state.money) / 5) * state.rules.interestPer5)
    state.money += interest
  }

  state.money += state.handsLeft * state.rules.moneyPerHandLeft
  state.money += state.discardsLeft * state.rules.moneyPerDiscardLeft
  state.discardsUnusedThisRun += state.discardsLeft

  if (state.blind === BlindKind.Boss) {
    runTrigger(vm, Trigger.OnBossDefeated)
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
  runTrigger(vm, Trigger.OnShopEnter)
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
      const tags = data.tables.tag.records.filter(row => row.minAnte <= state.ante)
      if (tags.length > 0) {
        const tag = tags[state.rng.Tag.below(tags.length)]
        state.tagsPending.push(tag.tagId)
        for (const row of data.tagEffects.get(tag.tagId) ?? []) {
          if (row.trigger === Trigger.OnUse) runRow(vm, row, RUN_HOST)
        }
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

      const result = scoreHand(vm, cards)
      const name = PokerHandKind[result.hand]
      state.handPlayCounts[name] = (state.handPlayCounts[name] ?? 0) + 1
      state.handTypesThisRound.push(name)
      state.score += result.score

      vm.scoring = undefined
      runTrigger(vm, Trigger.OnScoreResolved)

      // 낸 카드는 덱으로 돌아가지 않습니다 — 이번 라운드에는 다시 뽑히지 않습니다.
      state.played = []

      if (state.score >= state.target) {
        winRound(vm)
      } else if (state.handsLeft <= 0 && !vm.lossPrevented) {
        state.phase = 'lost'
        vm.events.push({ t: 'RunLost', ante: state.ante })
      } else {
        draw(vm)
      }
      break
    }

    case 'discard': {
      if (state.phase !== 'round' || state.discardsLeft <= 0) break
      state.discardsLeft--
      state.discarded = action.cards.slice()
      state.hand = state.hand.filter(uid => !action.cards.includes(uid))

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

    case 'sell_joker': {
      const joker = state.jokers[action.index]
      if (!joker || joker.sticker === 1) break
      for (const row of data.jokerEffects.get(joker.jokerId) ?? []) {
        if (row.trigger === Trigger.OnSell) {
          runOne(vm, row, { kind: 'joker', joker, slot: action.index })
        }
      }
      state.money += sellPrice(vm, joker)
      state.jokers.splice(action.index, 1)
      vm.events.push({ t: 'JokerDestroyed', uid: joker.uid, jokerId: joker.jokerId })
      state.rules.debuffUntilJokerSold = false
      runTrigger(vm, Trigger.OnJokerSold)
      break
    }

    case 'sell_consumable': {
      const item = state.consumables[action.index]
      if (!item) break
      state.money += data.economy.sellMin
      state.consumables.splice(action.index, 1)
      break
    }

    case 'buy': {
      if (state.phase !== 'shop') break
      const item = state.shop.cards[action.slot]
      if (!item || state.money - item.cost < -state.rules.debtLimit) break
      if (!takeItem(vm, item)) break
      state.money -= item.cost
      vm.events.push({ t: 'MoneyChanged', delta: -item.cost, reason: 'shop' })
      state.shop.cards.splice(action.slot, 1)
      break
    }

    case 'buy_voucher': {
      if (state.phase !== 'shop' || !state.shop.voucher) break
      const cost = data.economy.voucherCost
      if (state.money - cost < -state.rules.debtLimit) break
      state.money -= cost
      state.vouchers.push(state.shop.voucher)
      for (const row of data.voucherEffects.get(state.shop.voucher) ?? []) {
        if (row.trigger === Trigger.Passive) runRow(vm, row, RUN_HOST)
      }
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
      if (state.phase !== 'shop') break
      runTrigger(vm, Trigger.OnShopExit)
      state.rules.nextShopFree = false
      state.shop = emptyShop()
      state.shop.voucherBought = false
      advance(vm)
      break
  }

  return { state, events: vm.events }
}

/** 산 것을 실제로 받습니다. 자리가 없으면 사지 못합니다. */
function takeItem(vm: Vm, item: { kind: ShopItemKind; id: string; edition: number }): boolean {
  const state = vm.state

  switch (item.kind) {
    case ShopItemKind.Joker: {
      const negative = item.edition === 4
      if (!negative && state.jokers.length >= state.rules.jokerSlots) return false
      state.jokers.push({
        uid: state.nextUid++,
        jokerId: item.id,
        edition: item.edition as never,
        sticker: 0 as never,
        counters: freshCounters(),
        age: 0,
        disabled: false,
      })
      vm.events.push({ t: 'JokerAdded', uid: state.nextUid - 1, jokerId: item.id })
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
        enhancement: EnhancementKind.None,
        seal: SealKind.None,
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
