// 패에서 가장 좋은 조합.
//
// **화면과 헤드리스가 같은 계산을 씁니다.** 화면은 이것으로 「이 카드도 고르면 더 높은 족보가
// 됩니다」를 알리고, 헤드리스는 이것으로 다음 수를 정합니다 — 둘이 갈라지면 화면이 권한 수와
// 러너가 두는 수가 달라집니다.
//
// **조커를 세지 않습니다.** 조커까지 세면 「무엇을 내면 좋은가」가 아니라 「이번 판의 점수가
// 얼마인가」가 되고, 그것은 득점 연출이 보여줄 몫입니다.

import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import type { Data } from './data'
import { evaluate } from './hand'
import type { CardInstance, RunState } from './state'

export interface Suggestion {
  /** 고를 카드들. 패에 있던 그 인스턴스입니다. */
  cards: CardInstance[]
  hand: PokerHandKind
  /** 칩 × 배수. 족보 등급이 아니라 이 값으로 견줍니다 — 레벨을 올린 족보가 더 높습니다. */
  value: number
}

/**
 * 이 패에서 가장 값이 높은 조합.
 *
 * 패가 8장이면 조합이 218가지이므로 전수로 봅니다. **족보 등급이 아니라 칩 × 배수로
 * 견줍니다** — 레벨을 올린 투 페어가 레벨 1 스트레이트보다 높을 수 있기 때문입니다.
 */
export function bestHand(data: Data, state: RunState,
                         held: CardInstance[]): Suggestion | undefined {
  let best: Suggestion | undefined

  for (const subset of subsets(held, Math.min(5, data.run.maxPlayedCards))) {
    const value = valueOf(data, state, subset)
    if (!value) continue
    if (!best || value.value > best.value
        || (value.value === best.value && subset.length < best.cards.length)) {
      best = { cards: subset, hand: value.hand, value: value.value }
    }
  }

  return best
}

/** 이 조합 하나가 몇 점인가. 조커를 세지 않은 순수한 값입니다. */
export function valueOf(data: Data, state: RunState,
                        cards: CardInstance[]): { hand: PokerHandKind; value: number } | undefined {
  if (cards.length === 0) return undefined

  const { hand } = evaluate(cards, state.rules)
  const row = data.tables.pokerHand.findByHand(hand)
  if (!row) return undefined

  const level = state.handLevels[PokerHandKind[hand]] ?? 1
  const chips = row.baseChips + row.chipsPerLevel * (level - 1)
  const mult = row.baseMult + row.multPerLevel * (level - 1)
  return { hand, value: chips * mult }
}

/** 1장에서 `max` 장까지의 조합. 패가 8장이면 218가지입니다. */
export function* subsets(cards: CardInstance[], max: number): Generator<CardInstance[]> {
  const total = 1 << cards.length
  for (let mask = 1; mask < total; mask++) {
    // **개수를 먼저 셉니다.** `max` 장을 넘는 조합은 만들지 않고 넘어갑니다 — 패가 12장이면
    // 4,095개 중 1,585개만 쓰이고, 나머지 배열을 만들었다 버릴 이유가 없습니다.
    // 차례는 그대로입니다 — 같은 값이면 먼저 나온 조합이 남으므로 차례가 답을 정합니다.
    if (popcount(mask) > max) continue
    const chosen: CardInstance[] = []
    for (let i = 0; i < cards.length; i++) {
      if (mask & (1 << i)) chosen.push(cards[i])
    }
    yield chosen
  }
}

/** 켜진 비트의 수. */
function popcount(n: number): number {
  let count = 0
  while (n) {
    n &= n - 1
    count++
  }
  return count
}
