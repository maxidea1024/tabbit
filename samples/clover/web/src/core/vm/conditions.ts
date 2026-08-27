// 조건 41종.
//
// **조건은 하나만 봅니다.** 확률과 「첫 대상만」은 조건이 아니라 효과 행의 칸이므로 여기
// 없습니다 — `run.ts` 가 조건 앞뒤에서 봅니다.

import type { Condition } from '../../generated/structs/condition'
import { Compare } from '../../generated/enums/compare'
import { EnhancementKind } from '../../generated/enums/enhancement-kind'
import { SuitKind } from '../../generated/enums/suit-kind'
import { PokerHandKind } from '../../generated/enums/poker-hand-kind'
import { CounterField } from '../../generated/enums/counter-field'
import { TargetKind } from '../../generated/enums/target-kind'
import type { EffectRow } from '../data'
import { isFace } from '../hand'
import type { CardInstance, Counters } from '../state'
import type { EffectHost, Vm } from './context'

function compare(actual: number, wanted: number, how: Compare): boolean {
  switch (how) {
    case Compare.AtLeast: return actual >= wanted
    case Compare.AtMost: return actual <= wanted
    case Compare.Exactly: return actual === wanted
    default: return false
  }
}

function counterOf(counters: Counters, field: CounterField): number {
  switch (field) {
    case CounterField.Chips: return counters.chips
    case CounterField.MultAdd: return counters.multAdd
    case CounterField.MultMul: return counters.multMul
    case CounterField.Money: return counters.money
    case CounterField.SellValue: return counters.sellValue
    case CounterField.Charge: return counters.charge
    case CounterField.Tick: return counters.tick
    default: return 0
  }
}

export function setCounter(counters: Counters, field: CounterField, value: number): void {
  switch (field) {
    case CounterField.Chips: counters.chips = value; break
    case CounterField.MultAdd: counters.multAdd = value; break
    case CounterField.MultMul: counters.multMul = value; break
    case CounterField.Money: counters.money = value; break
    case CounterField.SellValue: counters.sellValue = value; break
    case CounterField.Charge: counters.charge = value; break
    case CounterField.Tick: counters.tick = value; break
  }
}

export { counterOf }

/** 그 무늬인가. 와일드 카드는 어느 무늬로도 셉니다. */
function hasSuit(card: CardInstance, suit: SuitKind, merged: boolean): boolean {
  if (card.enhancement === EnhancementKind.Wild) return true
  if (card.enhancement === EnhancementKind.Stone) return false
  if (!merged) return card.suit === suit
  const pair = (s: SuitKind) =>
    s === SuitKind.Heart || s === SuitKind.Diamond ? SuitKind.Heart : SuitKind.Spade
  return pair(card.suit) === pair(suit)
}

/** 지금 처리 중인 카드. 없으면 조건이 성립하지 않습니다. */
function target(vm: Vm, host: EffectHost): CardInstance | undefined {
  return vm.scoring?.card ?? host.card
}

export function holds(vm: Vm, row: EffectRow, host: EffectHost): boolean {
  const cond: Condition = row.condition
  const state = vm.state
  const scoring = vm.scoring

  switch (cond.kind) {
    case 'CondAlways':
      return true

    case 'CondHandContains':
      return scoring !== undefined && contains(scoring.hand, cond.hand)

    case 'CondHandIs':
      return scoring?.hand === cond.hand

    case 'CondCardSuit': {
      const card = target(vm, host)
      return card !== undefined && hasSuit(card, cond.suit, state.rules.suitsMerged)
    }

    case 'CondCardRankSet': {
      const card = target(vm, host)
      return card !== undefined && row.ranks.includes(card.rank)
        && card.enhancement !== EnhancementKind.Stone
    }

    case 'CondCardIsFace': {
      const card = target(vm, host)
      return card !== undefined && isFace(card, state.rules)
    }

    case 'CondCardEnhancement': {
      const card = target(vm, host)
      return card?.enhancement === cond.enhancement
    }

    case 'CondCardEnhanced': {
      const card = target(vm, host)
      return card !== undefined && card.enhancement !== EnhancementKind.None
    }

    case 'CondCardSeal': {
      const card = target(vm, host)
      return card?.seal === cond.seal
    }

    case 'CondCardEdition': {
      const card = target(vm, host)
      return card?.edition === cond.edition
    }

    case 'CondCardCount':
      return scoring !== undefined && compare(scoring.played.length, cond.n, cond.compare)

    case 'CondAllSuitsPresent': {
      if (!scoring) return false
      const merged = state.rules.suitsMerged
      return [SuitKind.Spade, SuitKind.Heart, SuitKind.Club, SuitKind.Diamond]
        .every(suit => scoring.scoringCards.some(card => hasSuit(card, suit, merged)))
    }

    case 'CondSuitPair': {
      if (!scoring) return false
      const merged = state.rules.suitsMerged
      const wanted = scoring.scoringCards.filter(card => hasSuit(card, cond.suit, merged))
      const other = scoring.scoringCards.filter(card => !hasSuit(card, cond.suit, merged))
      return wanted.length > 0 && other.length > 0
    }

    case 'CondAllHeldSuit': {
      const held = state.hand.map(uid => cardOf(vm, uid))
      if (held.length === 0) return false
      const merged = state.rules.suitsMerged
      return held.every(card => row.suits.some(suit => hasSuit(card, suit, merged)))
    }

    case 'CondBlindKind':
      return state.blind === cond.blind

    case 'CondMoney':
      return compare(state.money, cond.n, cond.compare)

    case 'CondDiscardsLeft':
      return compare(state.discardsLeft, cond.n, cond.compare)

    case 'CondHandsLeft':
      return compare(state.handsLeft, cond.n, cond.compare)

    case 'CondDiscardsUnused':
      return state.discardsLeft === state.rules.discardsPerRound

    case 'CondHandRepeated':
      return scoring !== undefined
        && state.handTypesThisRound.filter(h => h === PokerHandKind[scoring.hand]).length > 1

    case 'CondIsMostPlayedHand':
      return scoring !== undefined && mostPlayed(vm) === PokerHandKind[scoring.hand]

    case 'CondNotMostPlayedHand':
      return scoring !== undefined && mostPlayed(vm) !== PokerHandKind[scoring.hand]

    case 'CondFirstHand':
      return state.handsLeft === state.rules.handsPerRound - 1

    case 'CondLastHand':
      return state.handsLeft === 0

    case 'CondFirstDiscard':
      return state.discardsLeft === state.rules.discardsPerRound - 1

    case 'CondEveryNHands':
      return cond.n > 0 && state.handsPlayedThisRun > 0
        && state.handsPlayedThisRun % cond.n === 0

    case 'CondCounterAtLeast': {
      if (!host.joker) return false
      const value = counterOf(host.joker.counters, cond.counter)
      if (value < cond.n) return false
      if (cond.consume) setCounter(host.joker.counters, cond.counter, value - cond.n)
      return true
    }

    case 'CondCounterAtMost':
      return host.joker !== undefined
        && counterOf(host.joker.counters, cond.counter) <= cond.n

    case 'CondChargeLeft':
      return host.joker !== undefined && host.joker.counters.charge > 0

    case 'CondTargetMatch': {
      const card = target(vm, host)
      switch (cond.target) {
        case TargetKind.Hand:
          return scoring?.hand === state.targets.hand
        case TargetKind.Rank:
          return card !== undefined && card.rank === state.targets.rank
        case TargetKind.Suit:
          return card !== undefined
            && hasSuit(card, state.targets.suit, state.rules.suitsMerged)
        case TargetKind.Card:
          return card !== undefined && card.rank === state.targets.cardRank
            && hasSuit(card, state.targets.cardSuit, state.rules.suitsMerged)
        default:
          return false
      }
    }

    case 'CondDeckEnhancedAtLeast':
      return state.deck.filter(card => card.enhancement !== EnhancementKind.None).length >= cond.n

    case 'CondBossTriggered':
      return state.bossTriggeredThisHand

    case 'CondScoreRatioAtLeast':
      return state.target > 0
        && state.score * cond.den >= state.target * cond.num

    case 'CondNoFaceScored':
      return scoring !== undefined
        && !scoring.scoringCards.some(card => isFace(card, state.rules))

    case 'CondFaceScored':
      return scoring !== undefined
        && scoring.scoringCards.some(card => isFace(card, state.rules))

    case 'CondDiscardedFaceAtLeast':
      return state.discarded.map(uid => cardOf(vm, uid))
        .filter(card => isFace(card, state.rules)).length >= cond.n

    case 'CondHandContainsRankAndHand':
      return scoring !== undefined && contains(scoring.hand, cond.hand)
        && scoring.played.some(card => row.ranks.includes(card.rank))

    case 'CondFirstHandSingleCard':
      return scoring !== undefined && scoring.played.length === 1
        && state.handsLeft === state.rules.handsPerRound - 1

    case 'CondFirstHandSingleRank':
      return scoring !== undefined && scoring.played.length === 1
        && row.ranks.includes(scoring.played[0].rank)
        && state.handsLeft === state.rules.handsPerRound - 1

    case 'CondFirstDiscardSingleCard':
      return state.discarded.length === 1
        && state.discardsLeft === state.rules.discardsPerRound - 1

    case 'CondConsumableKind':
      return vm.lastConsumableKind === cond.consumable

    default:
      return false
  }
}

/** 낸 족보가 그것을 포함하는가. 높은 족보는 낮은 족보를 포함합니다. */
function contains(actual: PokerHandKind, wanted: PokerHandKind): boolean {
  if (actual === wanted) return true
  const implied = IMPLIES[actual]
  return implied !== undefined && implied.includes(wanted)
}

/**
 * 어떤 족보가 어떤 족보를 포함하는가.
 *
 * **표로 두는 것이 요점입니다.** 「포카드는 트리플을 포함한다」를 조건마다 다시 판단하면
 * 두 구현이 다른 답을 낼 자리가 생깁니다.
 */
const IMPLIES: Partial<Record<PokerHandKind, PokerHandKind[]>> = {
  [PokerHandKind.Pair]: [],
  [PokerHandKind.TwoPair]: [PokerHandKind.Pair],
  [PokerHandKind.ThreeOfAKind]: [PokerHandKind.Pair],
  [PokerHandKind.Straight]: [],
  [PokerHandKind.Flush]: [],
  [PokerHandKind.FullHouse]: [PokerHandKind.Pair, PokerHandKind.TwoPair, PokerHandKind.ThreeOfAKind],
  [PokerHandKind.FourOfAKind]: [PokerHandKind.Pair, PokerHandKind.ThreeOfAKind],
  [PokerHandKind.StraightFlush]: [PokerHandKind.Straight, PokerHandKind.Flush],
  [PokerHandKind.FiveOfAKind]: [
    PokerHandKind.Pair, PokerHandKind.ThreeOfAKind, PokerHandKind.FourOfAKind],
  [PokerHandKind.FlushHouse]: [
    PokerHandKind.Pair, PokerHandKind.TwoPair, PokerHandKind.ThreeOfAKind,
    PokerHandKind.FullHouse, PokerHandKind.Flush],
  [PokerHandKind.FlushFive]: [
    PokerHandKind.Pair, PokerHandKind.ThreeOfAKind, PokerHandKind.FourOfAKind,
    PokerHandKind.FiveOfAKind, PokerHandKind.Flush],
}

function mostPlayed(vm: Vm): string | undefined {
  let best: string | undefined
  let count = -1
  for (const [hand, times] of Object.entries(vm.state.handPlayCounts)) {
    if (times > count) { count = times; best = hand }
  }
  return count > 0 ? best : undefined
}

export function cardOf(vm: Vm, uid: number): CardInstance {
  const card = vm.state.deck.find(entry => entry.uid === uid)
  if (!card) throw new Error(`덱에 없는 카드입니다: ${uid}`)
  return card
}

export { hasSuit, contains }
