// 족보 판정.
//
// **판정은 가장 높은 족보 하나로 하고, 그 족보를 이루는 카드만 득점합니다.** 나머지는 배수에
// 기여하지 않습니다 — `downpour` 가 그 규칙을 바꿉니다.
//
// 규칙을 바꾸는 것이 다섯 있고 전부 조커입니다. 판정 **전에** 적용되므로 여기 있습니다.

import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { CardInstance, Rules } from './state'

export interface HandResult {
  hand: PokerHandKind
  /** 득점하는 카드. 낸 순서 그대로입니다 — **순서가 규칙입니다.** */
  scoring: CardInstance[]
}

/** 무늬가 없는 카드. 족보를 이루지 않고, 그러면서 항상 득점합니다. */
function isStone(card: CardInstance): boolean {
  return card.enhancement === EnhancementKind.Stone
}

/** 모든 무늬로 취급되는 카드. */
function isWild(card: CardInstance): boolean {
  return card.enhancement === EnhancementKind.Wild
}

/** `smudged_pane` 이 켜지면 하트와 다이아가, 스페이드와 클럽이 한 무늬가 됩니다. */
function suitKey(suit: SuitKind, merged: boolean): number {
  if (!merged) return suit
  if (suit === SuitKind.Heart || suit === SuitKind.Diamond) return SuitKind.Heart
  return SuitKind.Spade
}

/** 그림 카드인가. `face_pattern` 이 이 판정을 바꿉니다. */
export function isFace(card: CardInstance, rules: Rules): boolean {
  if (isStone(card)) return false
  if (rules.allCardsAreFace) return true
  return card.rank >= 11 && card.rank <= 13
}

/** 같은 랭크끼리 묶습니다. 낸 순서를 지킵니다. */
function groupByRank(cards: CardInstance[]): CardInstance[][] {
  const groups = new Map<number, CardInstance[]>()
  for (const card of cards) {
    const list = groups.get(card.rank)
    if (list) list.push(card)
    else groups.set(card.rank, [card])
  }
  // 큰 묶음이 앞, 같으면 높은 랭크가 앞입니다.
  return [...groups.values()].sort(
    (a, b) => b.length - a.length || b[0].rank - a[0].rank)
}

/** 플러시를 이루는 카드. 이루지 못하면 빈 배열입니다. */
function findFlush(cards: CardInstance[], rules: Rules): CardInstance[] {
  const need = rules.flushStraightCards
  if (cards.length < need) return []

  const wilds = cards.filter(isWild)
  const bySuit = new Map<number, CardInstance[]>()

  for (const card of cards) {
    if (isWild(card)) continue
    const key = suitKey(card.suit, rules.suitsMerged)
    const list = bySuit.get(key)
    if (list) list.push(card)
    else bySuit.set(key, [card])
  }

  // 와일드는 어느 무늬에도 들어가므로, 가장 큰 무늬에 붙여 봅니다.
  let best: CardInstance[] = []
  for (const list of bySuit.values()) {
    if (list.length + wilds.length >= need && list.length > best.length) best = list
  }

  if (best.length === 0 && wilds.length >= need) best = []
  const total = best.length + wilds.length
  if (total < need) return []

  const chosen = new Set([...best, ...wilds].map(card => card.uid))
  return cards.filter(card => chosen.has(card.uid))
}

/**
 * 스트레이트를 이루는 카드.
 *
 * `straightGap` 이 1 이면 랭크 한 칸의 빈틈을 허용합니다. A 는 양쪽에 서므로 두 번 봅니다.
 */
function findStraight(cards: CardInstance[], rules: Rules): CardInstance[] {
  const need = rules.flushStraightCards
  if (cards.length < need) return []

  const byRank = new Map<number, CardInstance>()
  for (const card of cards) if (!byRank.has(card.rank)) byRank.set(card.rank, card)
  if (byRank.size < need) return []

  const step = rules.straightGap + 1
  const ranks = [...byRank.keys()].sort((a, b) => a - b)

  // A 를 1 로도 봅니다. `A 2 3 4 5` 가 스트레이트인 이유입니다.
  const candidates: number[][] = [ranks]
  if (byRank.has(14)) candidates.push([1, ...ranks.filter(r => r !== 14)].sort((a, b) => a - b))

  for (const list of candidates) {
    for (let start = 0; start + need <= list.length; start++) {
      const run: number[] = [list[start]]
      for (let i = start + 1; i < list.length && run.length < need; i++) {
        const delta = list[i] - run[run.length - 1]
        if (delta >= 1 && delta <= step) run.push(list[i])
        else if (delta > step) break
      }

      if (run.length >= need) {
        const wanted = new Set(run.map(rank => (rank === 1 ? 14 : rank)))
        return cards.filter(card => wanted.has(card.rank) && byRank.get(card.rank) === card)
      }
    }
  }

  return []
}

/**
 * 낸 카드에서 족보와 득점 카드를 정합니다.
 *
 * 돌 카드는 족보를 이루지 않고 항상 득점하므로, 판정에서 빼고 득점에서 더합니다.
 */
export function evaluate(played: CardInstance[], rules: Rules): HandResult {
  const stones = played.filter(isStone)
  const cards = played.filter(card => !isStone(card))

  const groups = groupByRank(cards)
  const biggest = groups.length > 0 ? groups[0].length : 0
  const second = groups.length > 1 ? groups[1].length : 0

  const flush = findFlush(cards, rules)
  const straight = findStraight(cards, rules)
  const fullHouse = biggest >= 3 && second >= 2

  let hand: PokerHandKind
  let scoring: CardInstance[]

  if (biggest >= 5 && flush.length > 0) {
    hand = PokerHandKind.FlushFive
    scoring = groups[0].slice(0, 5)
  } else if (fullHouse && flush.length > 0) {
    hand = PokerHandKind.FlushHouse
    scoring = [...groups[0].slice(0, 3), ...groups[1].slice(0, 2)]
  } else if (biggest >= 5) {
    hand = PokerHandKind.FiveOfAKind
    scoring = groups[0].slice(0, 5)
  } else if (flush.length > 0 && straight.length > 0) {
    hand = PokerHandKind.StraightFlush
    scoring = straight
  } else if (biggest >= 4) {
    hand = PokerHandKind.FourOfAKind
    scoring = groups[0].slice(0, 4)
  } else if (fullHouse) {
    hand = PokerHandKind.FullHouse
    scoring = [...groups[0].slice(0, 3), ...groups[1].slice(0, 2)]
  } else if (flush.length > 0) {
    hand = PokerHandKind.Flush
    scoring = flush
  } else if (straight.length > 0) {
    hand = PokerHandKind.Straight
    scoring = straight
  } else if (biggest >= 3) {
    hand = PokerHandKind.ThreeOfAKind
    scoring = groups[0].slice(0, 3)
  } else if (biggest >= 2 && second >= 2) {
    hand = PokerHandKind.TwoPair
    scoring = [...groups[0].slice(0, 2), ...groups[1].slice(0, 2)]
  } else if (biggest >= 2) {
    hand = PokerHandKind.Pair
    scoring = groups[0].slice(0, 2)
  } else {
    hand = PokerHandKind.HighCard
    scoring = groups.length > 0 ? [groups[0][0]] : []
  }

  if (rules.allCardsScore) scoring = cards.slice()

  // 낸 순서로 되돌리고 돌 카드를 더합니다. **순서가 규칙입니다.**
  const chosen = new Set([...scoring, ...stones].map(card => card.uid))
  return { hand, scoring: played.filter(card => chosen.has(card.uid)) }
}

/** 족보의 표시 이름을 위한 식별자. `PokerHand` 테이블의 키와 같습니다. */
export function handName(hand: PokerHandKind): string {
  return PokerHandKind[hand]
}
