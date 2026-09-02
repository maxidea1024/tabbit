// 연산 36종.
//
// **값을 넣는 것과 상태를 바꾸는 것이 갈립니다.** 앞쪽은 득점 중에만 뜻이 있고, 뒤쪽은
// 언제든 돕니다. `scope` 가 누구에게인지를 정하고, 그 해석이 `targets()` 하나에 있습니다.

import type { Operation } from '../../generated/structs/operation'
import { CardClass } from '../../generated/enums/card-class'
import { CardTrait } from '../../generated/enums/card-trait'
import { CounterField } from '../../generated/enums/counter-field'
import { CreateKind } from '../../generated/enums/create-kind'
import { Duration } from '../../generated/enums/duration'
import { DebuffKind } from '../../generated/enums/debuff-kind'
import { EditionKind } from '../../generated/enums/edition-kind'
import { EnhancementKind } from '../../generated/enums/enhancement-kind'
import { HandPick } from '../../generated/enums/hand-pick'
import { JokerPick } from '../../generated/enums/joker-pick'
import { ModifyKind } from '../../generated/enums/modify-kind'
import { PerUnitMode } from '../../generated/enums/per-unit-mode'
import { PokerHandKind } from '../../generated/enums/poker-hand-kind'
import { Rarity } from '../../generated/enums/rarity'
import { RuleKind } from '../../generated/enums/rule-kind'
import { Scope } from '../../generated/enums/scope'
import { SealKind } from '../../generated/enums/seal-kind'
import { StickerKind } from '../../generated/enums/sticker-kind'
import { SuitKind } from '../../generated/enums/suit-kind'
import { Trigger } from '../../generated/enums/trigger'
import { UnitKind } from '../../generated/enums/unit-kind'
import type { EffectRow } from '../data'
import { jokerPool, planetPool, spectralPool, tarotPool } from '../pool'
import { isFace } from '../hand'
import { mulBp, MULT_ONE } from '../units'
import { stakeRow } from '../stake'
import type { CardInstance, GameEvent, JokerInstance } from '../state'
import { newCounters } from '../state'
import { cardOf, counterOf, hasSuit, setCounter } from './conditions'
import type { EffectHost, Vm } from './context'

/** `scope` 가 가리키는 카드들. */
function cardTargets(vm: Vm, row: EffectRow, host: EffectHost): CardInstance[] {
  const state = vm.state
  const count = row.scopeCount ?? 1

  switch (row.scope) {
    case Scope.ScoredCard:
      return vm.scoring?.card ? [vm.scoring.card] : []
    case Scope.HeldCard:
      return vm.scoring?.card ? [vm.scoring.card] : host.card ? [host.card] : []
    case Scope.Selected:
      return vm.selection.slice(0, count)
    case Scope.RandomInHand: {
      const held = state.hand.map(uid => cardOf(vm, uid))
      return pickMany(vm, held, count)
    }
    case Scope.AllInHand:
      return state.hand.map(uid => cardOf(vm, uid))
    case Scope.RandomInDeck:
      return pickMany(vm, state.deck.slice(), count)
    case Scope.AllInDeck:
      return state.deck.slice()
    case Scope.SelfTarget:
      return host.card ? [host.card] : []
    default:
      return []
  }
}

/** `scope` 가 가리키는 조커들. */
function jokerTargets(vm: Vm, row: EffectRow, host: EffectHost): JokerInstance[] {
  const jokers = vm.state.jokers
  const slot = host.slot ?? -1

  switch (row.scope) {
    case Scope.SelfTarget:
      return host.joker ? [host.joker] : []
    case Scope.JokerRight:
      return slot >= 0 && slot + 1 < jokers.length ? [jokers[slot + 1]] : []
    case Scope.JokerLeftmost:
      return jokers.length > 0 ? [jokers[0]] : []
    case Scope.RandomJoker:
      return pickMany(vm, jokers.slice(), 1)
    case Scope.AllOtherJokers:
      return jokers.filter(joker => joker !== host.joker)
    case Scope.AllJokers:
      return jokers.slice()
    default:
      return []
  }
}

function pickMany<T>(vm: Vm, items: T[], count: number): T[] {
  const rng = vm.state.rng.CardProc
  const out: T[] = []
  const pool = items.slice()
  for (let i = 0; i < count && pool.length > 0; i++) {
    out.push(pool.splice(rng.below(pool.length), 1)[0])
  }
  return out
}

/** 「무엇마다」의 개수. */
function unitCount(vm: Vm, op: { unit: UnitKind; enhancement: EnhancementKind; rarity: Rarity },
                   row: EffectRow, host: EffectHost): number {
  const state = vm.state

  switch (op.unit) {
    case UnitKind.JokerCount: return state.jokers.length
    case UnitKind.EmptyJokerSlots: return Math.max(0, state.rules.jokerSlots - state.jokers.length)
    case UnitKind.DeckRemaining: return state.drawPile.length
    case UnitKind.DeckDeficit: return Math.max(0, 52 - state.deck.length)
    case UnitKind.Money: return Math.max(0, state.money)
    case UnitKind.MoneyPer5: return Math.max(0, Math.floor(state.money / 5))
    case UnitKind.DiscardsLeft: return state.discardsLeft
    case UnitKind.HandsLeft: return state.handsLeft
    case UnitKind.BlindsSkipped: return state.blindsSkipped
    case UnitKind.TarotUsed: return state.tarotUsed
    case UnitKind.UniquePlanetUsed: return state.planetsUsed.length
    case UnitKind.DeckRankCount:
      return state.deck.filter(card => row.ranks.includes(card.rank)).length
    case UnitKind.DeckEnhancementCount:
      return state.deck.filter(card => card.enhancement === op.enhancement).length
    case UnitKind.JokerRarityCount:
      return state.jokers.filter(
        joker => vm.data.tables.joker.findByJokerId(joker.jokerId)?.rarity === op.rarity).length
    case UnitKind.HandPlayCount:
      return vm.scoring ? (state.handPlayCounts[PokerHandKind[vm.scoring.hand]] ?? 0) : 0
    case UnitKind.OtherJokerSellValue:
      return state.jokers
        .filter(joker => joker !== host.joker)
        .reduce((sum, joker) => sum + sellPrice(vm, joker), 0)
    case UnitKind.LowestHeldRankChips: {
      const held = state.hand.map(uid => cardOf(vm, uid))
        .filter(card => card.enhancement !== EnhancementKind.Stone)
      if (held.length === 0) return 0
      const lowest = held.reduce((min, card) => (card.rank < min.rank ? card : min))
      return vm.data.tables.rank.findByRank(lowest.rank)?.chips ?? 0
    }
    case UnitKind.CardsPlayed: return vm.scoring?.played.length ?? 0
    case UnitKind.SelfCounterMoney: return host.joker?.counters.money ?? 0
    case UnitKind.HandsPlayedThisRun: return state.handsPlayedThisRun
    case UnitKind.DiscardsUnusedThisRun: return state.discardsUnusedThisRun
    default: return 0
  }
}

export function sellPrice(vm: Vm, joker: JokerInstance): number {
  const row = vm.data.tables.joker.findByJokerId(joker.jokerId)
  const cost = row?.cost ?? vm.data.economy.legendaryBaseCost
  const base = Math.max(
    vm.data.economy.sellMin, Math.floor(cost / vm.data.economy.sellDivisor))
  return base + joker.counters.sellValue
}

function addMoney(vm: Vm, amount: number, reason: string, cap?: number | null): void {
  let delta = amount
  if (cap !== null && cap !== undefined) delta = Math.min(delta, cap)
  vm.state.money += delta
  vm.events.push({ t: 'MoneyChanged', delta, reason })
}

function addChips(vm: Vm, chips: number, source: string): void {
  if (!vm.scoring || chips === 0) return
  vm.scoring.chips += chips
  vm.events.push({ t: 'ChipsMultChanged', chips: vm.scoring.chips, mult: vm.scoring.mult })
  void source
}

function addMult(vm: Vm, mult: number): void {
  if (!vm.scoring || mult === 0) return
  vm.scoring.mult += mult
  vm.events.push({ t: 'ChipsMultChanged', chips: vm.scoring.chips, mult: vm.scoring.mult })
}

function mulMult(vm: Vm, bp: number): void {
  if (!vm.scoring) return
  vm.scoring.mult = mulBp(vm.scoring.mult, bp)
  vm.events.push({ t: 'ChipsMultChanged', chips: vm.scoring.chips, mult: vm.scoring.mult })
}

/**
 * 규칙 하나를 바꾸고, 그것이 언제까지인지를 적습니다.
 *
 * 세 갈래입니다.
 *
 * |어디서 왔는가|어떻게 남는가|
 * |--|--|
 * |`Passive`|아무 데도 적지 않습니다. 원인이 있는 동안만 걸리고, 다시 세울 때마다 새로 얹힙니다|
 * |`Permanent` 인 그 밖의 것|`ruleDeltas` 에 남습니다. 유령 카드처럼 원인이 사라져도 남는 것들입니다|
 * |`ThisRound` · `NextRound`|`roundRules` · `pendingRules`. 라운드가 끝나면 사라집니다|
 */
function recordRule(vm: Vm, row: EffectRow, rule: RuleKind, value: number,
                    absolute: boolean, duration: number): void {
  const delta = { rule: rule as number, value, absolute }

  if (duration === Duration.NextRound) {
    if (!vm.rebuilding) vm.state.pendingRules.push(delta)
    return
  }

  if (duration === Duration.ThisRound) {
    if (!vm.rebuilding) vm.state.roundRules.push(delta)
    changeRule(vm, rule, value, absolute, row.suits)
    return
  }

  // **`Passive` 는 적지 않습니다.** 원인이 있는 동안만 걸리는 것이고, 다시 세울 때 그 원인을
  // 다시 훑으므로 적어 두면 두 번 얹힙니다.
  if (row.trigger !== Trigger.Passive && !vm.rebuilding) vm.state.ruleDeltas.push(delta)
  changeRule(vm, rule, value, absolute, row.suits)
}

/** 규칙 하나를 바꿉니다. **목록이 여기 한 곳입니다.** */
function changeRule(vm: Vm, rule: RuleKind, value: number, absolute: boolean,
                    suits: readonly SuitKind[]): void {
  const rules = vm.state.rules
  const set = (key: keyof typeof rules, current: number) =>
    ((rules[key] as unknown as number) = absolute ? value : current + value)

  // **무엇이 바뀌었는지는 견주어 찾습니다.** 규칙마다 어느 칸을 건드리는지는 아래 `switch`
  // 하나에 적혀 있고, 그것을 이벤트 쪽에 한 번 더 적으면 두 목록이 갈라집니다.
  const before = { ...rules }

  switch (rule) {
    case RuleKind.HandSize: set('handSize', rules.handSize); break
    case RuleKind.HandsPerRound: set('handsPerRound', rules.handsPerRound); break
    case RuleKind.DiscardsPerRound: set('discardsPerRound', rules.discardsPerRound); break
    case RuleKind.JokerSlots: set('jokerSlots', rules.jokerSlots); break
    case RuleKind.ConsumableSlots: set('consumableSlots', rules.consumableSlots); break
    case RuleKind.DebtLimit: set('debtLimit', rules.debtLimit); break
    case RuleKind.FreeRerolls: set('freeRerolls', rules.freeRerolls); break
    case RuleKind.RerollCostDelta: set('rerollCostDelta', rules.rerollCostDelta); break
    case RuleKind.RerollStartsFree: rules.rerollStartsFree = value !== 0; break
    case RuleKind.InterestPer5: set('interestPer5', rules.interestPer5); break
    case RuleKind.InterestCap: set('interestCap', rules.interestCap); break
    case RuleKind.ShopCardSlots: set('shopCardSlots', rules.shopCardSlots); break
    case RuleKind.ShopDiscount: set('shopDiscount', rules.shopDiscount); break
    case RuleKind.ShopAllowsPlayingCards: rules.shopAllowsPlayingCards = value !== 0; break
    case RuleKind.ShopAllowsSpectral: rules.shopAllowsSpectral = value !== 0; break
    case RuleKind.ShopWeightTarot: rules.shopWeightTarotScale = value; break
    case RuleKind.ShopWeightPlanet: rules.shopWeightPlanetScale = value; break
    case RuleKind.FreePlanets: rules.freePlanets = value !== 0; break
    case RuleKind.AllCardsScore: rules.allCardsScore = value !== 0; break
    case RuleKind.AllCardsAreFace: rules.allCardsAreFace = value !== 0; break
    case RuleKind.FlushStraightCards: set('flushStraightCards', rules.flushStraightCards); break
    case RuleKind.StraightGap: set('straightGap', rules.straightGap); break
    case RuleKind.SuitsMerged: rules.suitsMerged = value !== 0; break
    case RuleKind.ProbabilityScale: rules.probabilityScale = value; break
    case RuleKind.AllowDuplicates: rules.allowDuplicates = value !== 0; break
    case RuleKind.BalanceChipsAndMult: rules.balanceChipsAndMult = value !== 0; break
    case RuleKind.BossRerollsPerAnte: set('bossRerollsPerAnte', rules.bossRerollsPerAnte); break
    case RuleKind.AnteDelta: set('anteDelta', rules.anteDelta); break
    case RuleKind.EditionWeightScale: rules.editionWeightScale = value; break
    case RuleKind.PlanetGivesMult: rules.planetGivesMultBp = value; break
    // **절대값이면 그 금액으로 맞춥니다.** 더하기만 하면 「$100 으로 시작」을 적을 수
    // 없고, 기본 금액이 얹혀 $104 가 됩니다.
    case RuleKind.StartingMoney:
      addMoney(vm, absolute ? value - vm.state.money : value, 'deck')
      break
    case RuleKind.NoInterest: rules.noInterest = value !== 0; break
    case RuleKind.MoneyPerHandLeft: set('moneyPerHandLeft', rules.moneyPerHandLeft); break
    case RuleKind.MoneyPerDiscardLeft: set('moneyPerDiscardLeft', rules.moneyPerDiscardLeft); break
    case RuleKind.RandomizeDeck: randomizeDeck(vm); break
    case RuleKind.DoubleTagOnBossDefeat: rules.doubleTagOnBossDefeat = value !== 0; break
    case RuleKind.BlindSizeScale: rules.blindSizeScaleBp = value; break
    case RuleKind.SetDiscardsZero: vm.state.discardsLeft = 0; break
    case RuleKind.NextShopFree: rules.nextShopFree = value !== 0; break
    case RuleKind.NoRepeatHandTypes: rules.noRepeatHandTypes = value !== 0; break
    case RuleKind.SingleHandTypeOnly: rules.singleHandTypeOnly = value !== 0; break
    case RuleKind.MustPlayFiveCards: rules.mustPlayFiveCards = value !== 0; break
    case RuleKind.AlwaysDrawThree: rules.alwaysDrawThree = value !== 0; break
    case RuleKind.HalveBaseChipsAndMult: rules.halveBaseChipsAndMult = value !== 0; break
    case RuleKind.DebuffUntilJokerSold: rules.debuffUntilJokerSold = value !== 0; break
    case RuleKind.ForceCardSelected: rules.forceCardSelected = value !== 0; break
    case RuleKind.NoSmallBlindReward: rules.noSmallBlindReward = value !== 0; break
    case RuleKind.NoBigBlindReward: rules.noBigBlindReward = value !== 0; break
    case RuleKind.NoBossBlindReward: rules.noBossBlindReward = value !== 0; break
    case RuleKind.ChipsCappedByMoney: rules.chipsCappedByMoney = value !== 0; break
    case RuleKind.FaceDownDrawRate: rules.faceDownDrawRate = value; break
    case RuleKind.HandSizePerMoney: rules.handSizePerMoney = value; break
    case RuleKind.AllJokersEternal: rules.allJokersEternal = value !== 0; break
    case RuleKind.DebuffPlayedAfterScoring: rules.debuffPlayedAfterScoring = value !== 0; break
    case RuleKind.PriceRisePerPurchase: rules.priceRisePerPurchase = value; break
    case RuleKind.NoJokersInShop: rules.noJokersInShop = value !== 0; break
    case RuleKind.DiscardCost: rules.discardCost = value; break
    case RuleKind.PinnedJokerSlot: rules.pinnedJokerSlot = value; break
    case RuleKind.RemoveFaceCards:
      vm.state.deck = vm.state.deck.filter(card => !isFace(card, rules))
      break
    case RuleKind.SuitsOnly: keepSuits(vm, suits); break
    // 팩과 상점의 나머지는 상점이 볼 때 읽습니다.
    default: break
  }

  // 값을 가지지 않는 규칙도 있습니다 — 덱을 다시 뽑거나 그림 카드를 빼는 것들입니다.
  const keys = Object.keys(rules) as (keyof typeof rules)[]
  const moved = keys.find(key => rules[key] !== before[key])

  vm.events.push({
    t: 'RuleChanged',
    rule: RuleKind[rule],
    before: moved === undefined ? null : Number(before[moved]),
    after: moved === undefined ? null : Number(rules[moved]),
    flag: moved !== undefined && typeof before[moved] === 'boolean',
  })
}

/** 시작 덱을 그 무늬들로만 채웁니다. 장수는 그대로입니다. */
function keepSuits(vm: Vm, suits: readonly SuitKind[]): void {
  if (suits.length === 0) return
  const rng = vm.state.rng.Shuffle
  for (const card of vm.state.deck) {
    if (!suits.includes(card.suit)) card.suit = suits[rng.below(suits.length)]
  }
}

/** 52장의 랭크와 무늬를 전부 다시 뽑습니다. `erratic_deck` 하나입니다. */
function randomizeDeck(vm: Vm): void {
  const rng = vm.state.rng.Shuffle
  const base = vm.data.tables.baseDeckCard.records
  for (const card of vm.state.deck) {
    const source = base[rng.below(base.length)]
    card.rank = source.rank
    card.suit = source.suit
    card.baseCardId = source.cardId
  }
}

function newCard(vm: Vm, rank: number, suit: SuitKind, baseCardId: string): CardInstance {
  return {
    uid: vm.state.nextUid++,
    baseCardId,
    rank: rank as CardInstance['rank'],
    suit,
    enhancement: EnhancementKind.None,
    seal: SealKind.None,
    edition: EditionKind.Base,
    bonusChips: 0,
    debuffed: false,
    faceDown: false,
  }
}

function randomBaseCard(vm: Vm, cardClass: CardClass) {
  const pool = vm.data.tables.baseDeckCard.records.filter(row => {
    switch (cardClass) {
      case CardClass.Face: return row.isFace
      case CardClass.Ace: return row.rank === 14
      case CardClass.Numbered: return !row.isFace && row.rank !== 14
      default: return true
    }
  })
  return pool[vm.state.rng.CardProc.below(pool.length)]
}

/**
 * 조커 하나에 붙을 스티커.
 *
 * **붙이는 자리가 여럿이므로 판정을 한 곳에 둡니다** — 상점 · 팩 · 태그 · `CreateCard` ·
 * 챌린지의 시작 소지품입니다. `Eternal` 이 붙지 않는 조커에 붙이면 스스로 파괴되는 조커가
 * 파괴되지 않고 남거나, 팔아야 효과가 나는 조커를 팔 수 없게 됩니다.
 */
export function stickerFor(vm: Vm, jokerId: string, want: number): number {
  const ok = vm.data.tables.joker.findByJokerId(jokerId)?.eternalOk ?? true
  if (want === 1 && !ok) return 0
  // 붙일 것이 정해져 온 것은 추첨하지 않습니다 — 챌린지의 시작 소지품이 그렇습니다.
  if (want !== 0) return want

  if (vm.state.rules.allJokersEternal && ok) return 1

  const stake = stakeRow(vm.data, vm.state.stake)
  if (!stake || stake.sticker === StickerKind.None || stake.stickerPercent <= 0) return 0

  // **자격을 보기 전에 굴립니다.** 조커 하나에 한 번씩 굴려야 그 뒤의 추첨이 어느 조커가
  // 나왔는지에 흔들리지 않습니다.
  const hit = vm.state.rng.Sticker.below(100) < stake.stickerPercent
  if (!hit) return 0
  if (stake.sticker === StickerKind.Eternal && !ok) return 0
  return stake.sticker
}

/** 무작위 조커 하나를 만듭니다. 희귀도를 정하면 그 풀에서 고릅니다. */
function createJoker(vm: Vm, rarity: Rarity | undefined, edition: EditionKind): void {
  const rng = vm.state.rng.ShopRarity
  const pool = jokerPool(vm.data, vm.state, rarity)
  if (pool.length === 0) return
  if (vm.state.jokers.length >= vm.state.rules.jokerSlots
      && edition !== EditionKind.Negative) return

  const row = pool[rng.below(pool.length)]
  const joker: JokerInstance = {
    uid: vm.state.nextUid++,
    jokerId: row.jokerId,
    edition,
    sticker: stickerFor(vm, row.jokerId, 0) as JokerInstance['sticker'],
    counters: newCounters(),
    age: 0,
    disabled: false,
  }
  vm.state.jokers.push(joker)
  vm.events.push({ t: 'JokerAdded', uid: joker.uid, jokerId: joker.jokerId })
}

function createConsumable(vm: Vm, kind: CreateKind, edition: EditionKind): void {
  if (vm.state.consumables.length >= vm.state.rules.consumableSlots
      && edition !== EditionKind.Negative) return

  const rng = vm.state.rng.Pack
  let id: string | undefined
  let consumableKind: 1 | 2 | 3 = 1

  if (kind === CreateKind.Tarot) {
    const pool = tarotPool(vm.data, vm.state)
    id = pool[rng.below(pool.length)].tarotId
    consumableKind = 1
  } else if (kind === CreateKind.Planet) {
    const pool = planetPool(vm.data, vm.state)
    id = pool[rng.below(pool.length)].planetId
    consumableKind = 2
  } else if (kind === CreateKind.Spectral) {
    const pool = spectralPool(vm.data, vm.state)
    id = pool[rng.below(pool.length)].spectralId
    consumableKind = 3
  }

  if (!id) return
  const item = {
    uid: vm.state.nextUid++,
    kind: consumableKind as never,
    id,
    edition,
  }
  vm.state.consumables.push(item)
  vm.events.push({ t: 'ConsumableAdded', uid: item.uid, id })
}

/** 족보의 레벨을 올립니다. */
function levelHand(vm: Vm, pick: HandPick, levels: number): void {
  const names: string[] = []
  const scoring = vm.scoring

  switch (pick) {
    case HandPick.Played:
      if (scoring) names.push(PokerHandKind[scoring.hand])
      break
    case HandPick.All:
      for (const row of vm.data.tables.pokerHand.records) names.push(PokerHandKind[row.hand])
      break
    case HandPick.Random: {
      const pool = vm.data.tables.pokerHand.records
      names.push(PokerHandKind[pool[vm.state.rng.Pack.below(pool.length)].hand])
      break
    }
    case HandPick.MostPlayed: {
      let best: string | undefined
      let count = -1
      for (const [hand, times] of Object.entries(vm.state.handPlayCounts)) {
        if (times > count) { count = times; best = hand }
      }
      if (best) names.push(best)
      break
    }
    case HandPick.FirstDiscarded:
      if (vm.state.handTypesThisRound.length > 0) names.push(vm.state.handTypesThisRound[0])
      break
  }

  for (const name of names) {
    const next = Math.max(1, (vm.state.handLevels[name] ?? 1) + levels)
    vm.state.handLevels[name] = next
    vm.events.push({
      t: 'HandLevelled',
      hand: PokerHandKind[name as keyof typeof PokerHandKind],
      level: next,
    })
  }
}

function destroyCard(vm: Vm, card: CardInstance): void {
  vm.state.deck = vm.state.deck.filter(entry => entry !== card)
  vm.state.hand = vm.state.hand.filter(uid => uid !== card.uid)
  vm.state.drawPile = vm.state.drawPile.filter(uid => uid !== card.uid)
  vm.events.push({ t: 'CardDestroyed', uid: card.uid })
}

function destroyJoker(vm: Vm, joker: JokerInstance): void {
  vm.state.jokers = vm.state.jokers.filter(entry => entry !== joker)
  vm.events.push({ t: 'JokerDestroyed', uid: joker.uid, jokerId: joker.jokerId })
}

/** 하나의 효과가 실제로 하는 일. */
export function apply(vm: Vm, row: EffectRow, host: EffectHost): void {
  const op: Operation = row.operation
  const state = vm.state

  // 이 효과가 낸 이벤트가 시작하는 자리. `report` 가 「누가 했는가」를 여기에 끼웁니다.
  const outer = vm.mark
  vm.mark = vm.events.length

  runOp(vm, row, host, op, state)

  vm.mark = outer
}

function runOp(vm: Vm, row: EffectRow, host: EffectHost, op: Operation,
               state: Vm['state']): void {
  switch (op.kind) {
    case 'OpAddChips':
      addChips(vm, op.chips, row.owner)
      report(vm, row, host, 'AddChips', op.chips, 0, 0)
      break

    case 'OpAddMult':
      addMult(vm, op.mult)
      report(vm, row, host, 'AddMult', 0, op.mult, 0)
      break

    case 'OpMulMult':
      mulMult(vm, op.mult)
      report(vm, row, host, 'MulMult', 0, op.mult, 0)
      break

    case 'OpAddMoney':
      addMoney(vm, op.money, row.owner, op.cap || null)
      report(vm, row, host, 'AddMoney', 0, 0, op.money)
      break

    case 'OpSetMoney':
      vm.events.push({ t: 'MoneyChanged', delta: op.money - state.money, reason: row.owner })
      state.money = op.money
      break

    case 'OpPerUnit': {
      const count = unitCount(vm, op as never, row, host)
      if (count <= 0 && op.mode !== PerUnitMode.MulMult) break
      const base = op.baseValue

      // **몇 개를 셌는지가 아니라 얼마가 되었는지를 알립니다.** 「단위마다」라고만 알리면
      // 화면에 뜰 숫자가 없고, 그 조커는 아무것도 하지 않은 것으로 보입니다.
      switch (op.mode) {
        case PerUnitMode.AddChips:
          addChips(vm, op.value * count, row.owner)
          report(vm, row, host, 'AddChips', op.value * count, 0, 0)
          break
        case PerUnitMode.AddMult:
          addMult(vm, op.value * count)
          report(vm, row, host, 'AddMult', 0, op.value * count, 0)
          break
        case PerUnitMode.MulMult:
          mulMult(vm, base + op.value * count)
          report(vm, row, host, 'MulMult', 0, base + op.value * count, 0)
          break
        case PerUnitMode.MulEach: {
          // 하나씩 곱합니다. **곱한 것을 합쳐서 한 번에 알립니다** — 화면에는 그 결과
          // 하나가 떠야 하고, 곱하기 다섯 번이 다섯 박자가 되면 무엇에 곱한 것인지가
          // 흩어집니다.
          let total = MULT_ONE
          for (let i = 0; i < count; i++) {
            mulMult(vm, op.value)
            total = mulBp(total, op.value)
          }
          report(vm, row, host, 'MulMult', 0, total, 0)
          break
        }
        case PerUnitMode.AddMoney: {
          const amount = op.value * count
          addMoney(vm, amount, row.owner, op.cap || null)
          report(vm, row, host, 'AddMoney', 0, 0, amount)
          break
        }
      }
      break
    }

    case 'OpRandomRange': {
      const span = op.max - op.min + 1
      const value = op.min + state.rng.Misprint.below(Math.max(1, span))
      if (op.mode === PerUnitMode.AddChips) addChips(vm, value, row.owner)
      else addMult(vm, value)
      report(vm, row, host, 'RandomRange', 0, value, 0)
      break
    }

    case 'OpRetrigger':
      // 재발동은 득점 파이프라인이 처리합니다 — 그 자리에서 즉시여야 하므로 여기서는
      // 요청만 남깁니다. **재발동 중의 요청은 받지 않습니다.**
      if (vm.scoring?.card && !vm.retriggering) {
        vm.pendingRetrigger = (vm.pendingRetrigger ?? 0) + op.times
        vm.events.push({ t: 'Retriggered', uid: vm.scoring.card.uid, times: op.times })
      }
      break

    case 'OpGrowSelf': {
      if (!host.joker) break
      const current = counterOf(host.joker.counters, op.counter)
      let next = current + op.step
      if (op.cap !== 0) next = Math.min(next, op.cap)
      if (op.step < 0) next = Math.max(next, op.floor)
      setCounter(host.joker.counters, op.counter, next)
      // **늘어난 것도 사건입니다.** 늘어나는 조커는 그 순간이 전부이고, 조용히 늘면
      // 얼굴의 숫자가 언제 왜 바뀌었는지 알 수 없습니다.
      if (next !== current) report(vm, row, host, 'GrowSelf', 0, 0, 0)
      break
    }

    case 'OpResetSelf':
      if (host.joker) {
        setCounter(host.joker.counters, op.counter,
          op.counter === CounterField.MultMul ? MULT_ONE : 0)
      }
      break

    case 'OpGrowOthers':
      for (const joker of jokerTargets(vm, row, host)) {
        setCounter(joker.counters, op.counter,
          counterOf(joker.counters, op.counter) + op.step)
      }
      break

    case 'OpLevelUpHand':
      levelHand(vm, op.handPick || HandPick.Played, op.levels)
      break

    case 'OpCreateCard': {
      const edition = op.edition
      for (let i = 0; i < op.count; i++) {
        if (op.create === CreateKind.Joker) {
          createJoker(vm, op.rarity || undefined, edition)
        } else if (op.create === CreateKind.Tarot || op.create === CreateKind.Planet
                   || op.create === CreateKind.Spectral) {
          createConsumable(vm, op.create, edition)
        } else if (op.create === CreateKind.Tag && op.refId !== '') {
          state.tagsPending.push(op.refId)
        }
        // `LastUsed` · `Pack` · `Voucher` 는 상점과 팩이 처리합니다.
      }
      break
    }

    case 'OpAddCard': {
      const cardClass = op.cardClass
      for (let i = 0; i < op.count; i++) {
        let card: CardInstance
        if (op.create === CreateKind.CopyOfScored && vm.scoring?.card) {
          card = { ...vm.scoring.card, uid: state.nextUid++ }
        } else if (op.create === CreateKind.CopyOfSelected && vm.selection.length > 0) {
          card = { ...vm.selection[0], uid: state.nextUid++ }
        } else {
          const base = randomBaseCard(vm, cardClass)
          card = newCard(vm, base.rank, base.suit, base.cardId)
          if (op.enhancement !== EnhancementKind.None) card.enhancement = op.enhancement
          else if (op.random) card.enhancement = randomEnhancement(vm)
          if (op.seal !== SealKind.None) card.seal = op.seal
          else if (op.random) card.seal = randomSeal(vm)
        }
        state.deck.push(card)
        state.hand.push(card.uid)
        vm.events.push({ t: 'CardAdded', uid: card.uid })
      }
      break
    }

    case 'OpDestroyCard':
      for (const card of cardTargets(vm, row, host).slice(0, op.count)) destroyCard(vm, card)
      break

    case 'OpModifyCard': {
      const cards = cardTargets(vm, row, host)
      // `random` 은 **scope 안에서 한 번 뽑아 전부에 같은 값**을 씁니다.
      const rolledSuit = op.random ? randomSuit(vm) : undefined
      const rolledRank = op.random ? randomRank(vm) : undefined
      const rolledEdition = op.random ? randomEdition(vm) : undefined

      for (const card of cards) {
        switch (op.modify) {
          case ModifyKind.Enhancement:
            card.enhancement = op.random ? randomEnhancement(vm) : op.enhancement
            break
          case ModifyKind.Seal:
            card.seal = op.random ? randomSeal(vm) : op.seal
            break
          case ModifyKind.Edition:
            card.edition = rolledEdition ?? op.edition
            break
          case ModifyKind.Suit:
            card.suit = rolledSuit ?? (op.suit || card.suit)
            break
          case ModifyKind.RankStep:
            card.rank = Math.min(14, card.rank + (op.value || 1)) as CardInstance['rank']
            break
          case ModifyKind.RankTo:
            card.rank = (rolledRank ?? card.rank) as CardInstance['rank']
            break
          case ModifyKind.BonusChips:
            card.bonusChips += op.value
            break
          case ModifyKind.CopyRight:
            if (cards.length >= 2) {
              const [left, right] = cards
              left.rank = right.rank
              left.suit = right.suit
              left.enhancement = right.enhancement
              left.seal = right.seal
              left.edition = right.edition
            }
            break
        }
        vm.events.push({ t: 'CardModified', uid: card.uid, what: ModifyKind[op.modify] })
        if (op.modify === ModifyKind.CopyRight) break
      }
      break
    }

    case 'OpModifyJoker':
      for (const joker of jokerTargets(vm, row, host)) {
        joker.edition = op.random ? randomEdition(vm) : op.edition
      }
      break

    case 'OpDestroyJoker': {
      const doomed = op.pick === JokerPick.SelfPick && host.joker
        ? [host.joker]
        : jokerTargets(vm, { ...row, scope: scopeForPick(op.pick) }, host)
      for (const joker of doomed) destroyJoker(vm, joker)
      break
    }

    case 'OpCopyJoker': {
      const source = jokerTargets(vm, { ...row, scope: scopeForPick(op.pick) }, host)[0]
      if (!source) break
      // 복사는 그 자리에서 능력을 빌리는 것이므로, 상태를 복제하지 않고 임자만 바꿉니다.
      vm.copyTarget = source
      break
    }

    case 'OpDebuff': {
      const cards = op.debuff === DebuffKind.AllCards
        ? state.deck
        : state.deck.filter(card => matchesDebuff(vm, card, op.debuff, op.suit || undefined))
      for (const card of cards) card.debuffed = true
      state.bossTriggeredThisHand = true
      break
    }

    case 'OpDisableBoss':
      state.bossDisabled = true
      break

    case 'OpPreventLoss':
      vm.lossPrevented = true
      break

    case 'OpChangeRule':
      recordRule(vm, row, op.rule, op.value, op.absolute === true, op.duration)
      break

    case 'OpChangeRuleByCounter':
      if (host.joker) {
        // 누적값을 따라가는 것은 기간이 없습니다 — 누적값이 곧 기간입니다.
        recordRule(vm, row, op.rule, counterOf(host.joker.counters, op.counter), false,
          Duration.Permanent)
      }
      break

    case 'OpCardTrait':
      // 카드의 성질은 강화가 이미 말하고 있으므로 여기서 다시 적지 않습니다.
      // 판정은 `hand.ts` 가 강화를 보고 합니다.
      void CardTrait
      break

    case 'OpMulMoney': {
      const value = op.value || MULT_ONE
      const doubled = mulBp(state.money, value)
      const capped = op.cap !== 0 ? Math.min(doubled, op.cap) : doubled
      vm.events.push({ t: 'MoneyChanged', delta: capped - state.money, reason: row.owner })
      state.money = capped
      break
    }

    case 'OpShopGift':
      vm.shopGifts.push({
        create: op.create,
        rarity: op.rarity || undefined,
        edition: op.edition || undefined,
        free: op.free === true,
        count: op.count || 1,
      })
      break

    case 'OpDuplicateNextTag':
      state.duplicateNextTag = true
      break

    case 'OpRerollBoss':
      vm.rerollBoss = true
      break

    case 'OpForceDiscard': {
      const held = state.hand.map(uid => cardOf(vm, uid))
      for (const card of pickMany(vm, held, op.count)) {
        state.hand = state.hand.filter(uid => uid !== card.uid)
        state.discarded.push(card.uid)
      }
      state.bossTriggeredThisHand = true
      break
    }

    case 'OpDrawFaceDown': {
      const cards = state.hand.map(uid => cardOf(vm, uid))
      const cardClass = op.cardClass
      for (const card of cards) {
        if (cardClass === CardClass.Face && !isFace(card, state.rules)) continue
        card.faceDown = true
      }
      state.bossTriggeredThisHand = true
      break
    }

    case 'OpFlipJokers':
      state.rng.Boss.shuffle(state.jokers)
      state.bossTriggeredThisHand = true
      break

    case 'OpDisableRandomJoker': {
      const pick = pickMany(vm, state.jokers.slice(), 1)[0]
      if (pick) pick.disabled = true
      state.bossTriggeredThisHand = true
      break
    }

    case 'OpGrant':
      if (op.create === CreateKind.Voucher && op.refId !== '') {
        state.vouchers.push(op.refId)
      } else if (op.create === CreateKind.Joker && op.refId !== '') {
        // **에디션과 스티커가 함께 옵니다.** 챌린지의 시작 조커는 대개 `Eternal` 이고
        // `Negative` 가 붙는 것도 있으므로, 준 다음에 따로 붙이면 두 곳이 됩니다.
        for (let i = 0; i < op.count; i++) {
          state.jokers.push({
            uid: state.nextUid++,
            jokerId: op.refId,
            edition: op.edition ?? EditionKind.Base,
            sticker: stickerFor(vm, op.refId, op.sticker ?? 0),
            counters: newCounters(),
            age: 0,
            disabled: false,
          })
          vm.events.push({ t: 'JokerAdded', uid: state.nextUid - 1, jokerId: op.refId })
        }
      } else if (op.refId !== '') {
        for (let i = 0; i < op.count; i++) {
          state.consumables.push({
            uid: state.nextUid++,
            kind: (op.create === CreateKind.Tarot ? 1 : op.create === CreateKind.Planet ? 2 : 3) as never,
            id: op.refId,
            edition: EditionKind.Base,
          })
        }
      }
      break

    case 'OpNothing':
      break

    case 'OpCustom':
      runCustom(vm, op.handler, row, host)
      break

    default:
      break
  }
}

function scopeForPick(pick: JokerPick): Scope {
  switch (pick) {
    case JokerPick.Right: return Scope.JokerRight
    case JokerPick.Leftmost: return Scope.JokerLeftmost
    case JokerPick.Random: return Scope.RandomJoker
    case JokerPick.AllOther: return Scope.AllOtherJokers
    default: return Scope.SelfTarget
  }
}

function matchesDebuff(vm: Vm, card: CardInstance, kind: DebuffKind,
                       suit: SuitKind | undefined): boolean {
  switch (kind) {
    case DebuffKind.BySuit:
      return suit !== undefined && hasSuit(card, suit, vm.state.rules.suitsMerged)
    case DebuffKind.FaceCards:
      return isFace(card, vm.state.rules)
    case DebuffKind.PlayedThisAnte:
      return vm.state.cardsPlayedThisAnte.includes(card.uid)
    default:
      return true
  }
}

function randomEnhancement(vm: Vm): EnhancementKind {
  const pool = vm.data.tables.enhancement.records
    .filter(row => row.enhancement !== EnhancementKind.None)
  return pool[vm.state.rng.CardProc.below(pool.length)].enhancement
}

function randomSeal(vm: Vm): SealKind {
  const pool = vm.data.tables.seal.records.filter(row => row.seal !== SealKind.None)
  return pool[vm.state.rng.CardProc.below(pool.length)].seal
}

/** `Aura` 가 뽑는 셋. 네거티브는 카드에 붙지 않습니다. */
function randomEdition(vm: Vm): EditionKind {
  const pool = [EditionKind.Foil, EditionKind.Holographic, EditionKind.Polychrome]
  return pool[vm.state.rng.CardProc.below(pool.length)]
}

function randomSuit(vm: Vm): SuitKind {
  const pool = vm.data.tables.suit.records
  return pool[vm.state.rng.CardProc.below(pool.length)].suit
}

function randomRank(vm: Vm): number {
  const pool = vm.data.tables.rank.records
  return pool[vm.state.rng.CardProc.below(pool.length)].rank
}

/**
 * 무엇이 얼마를 했는가.
 *
 * **임자 셋이 전부 냅니다.** 조커만 내던 동안 강화와 인장과 에디션은 화면에 흔적을 남기지
 * 못했고, 값은 늘었는데 이유가 없는 화면이 되었습니다.
 *
 * 이벤트를 뒤에 붙이지 않고 **그 효과가 시작한 자리에 끼웁니다.** 값이 바뀐 것을 알리는
 * 이벤트는 연산 안쪽에서 이미 나갔으므로, 뒤에 붙이면 「누가」가 「얼마」보다 늦게 나옵니다.
 */
function report(vm: Vm, row: EffectRow, host: EffectHost, op: string,
                chips: number, mult: number, money: number): void {
  const event: GameEvent | undefined =
    host.kind === 'joker' && host.joker !== undefined
      ? {
        t: 'JokerTriggered',
        slot: host.slot ?? 0,
        jokerId: host.joker.jokerId,
        op, chips, mult, money,
      }
      : host.kind === 'card'
        ? {
          t: 'CardScored',
          uid: (host.card ?? vm.scoring?.card)?.uid ?? 0,
          op, chips, mult, money,
          source: sourceOf(row),
        }
        : { t: 'RunTriggered', owner: row.owner, op, chips, mult, money }

  if (event === undefined) return
  if (event.t === 'CardScored' && event.uid === 0) return

  vm.events.splice(vm.mark ?? vm.events.length, 0, event)
}

/** 카드에 붙은 효과가 어디서 온 것인가. 화면이 색과 글로 가릅니다. */
function sourceOf(row: EffectRow): string {
  return row.source
}

/**
 * 선언으로 적히지 않는 것.
 *
 * **개수가 이 샘플의 지표입니다.** 여기 있는 것 하나가 두 구현이 갈라질 수 있는 자리 하나
 * 이고, 그래서 `doc/effect-vm.md` 의 목록과 이 `switch` 가 같아야 합니다.
 */
function runCustom(vm: Vm, handler: string, row: EffectRow, host: EffectHost): void {
  vm.customsRun.push(handler)

  switch (handler) {
    // 오른쪽 조커를 파괴하고 그 판매가의 2배를 자기 배수로 가져옵니다. 값이 다른 개체의
    // 상태에서 오므로 선언으로 적히지 않습니다.
    case 'pruning_shears': {
      const slot = host.slot ?? -1
      const right = slot >= 0 && slot + 1 < vm.state.jokers.length
        ? vm.state.jokers[slot + 1] : undefined
      if (!right || !host.joker) break
      const gain = sellPrice(vm, right) * 2
      host.joker.counters.multAdd += gain * MULT_ONE
      destroyJoker(vm, right)
      break
    }

    default:
      throw new Error(`문서에 없는 \`Custom\` 입니다: ${handler}`)
  }
  void row
}

export { cardTargets, jokerTargets, addMoney, changeRule, destroyJoker, levelHand }
