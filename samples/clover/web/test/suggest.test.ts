// 족보 도움.
//
// **화면이 권하는 조합과 헤드리스가 두는 수가 같은 계산에서 나옵니다.** 그래서 여기서 값을
// 확인하면 양쪽이 함께 확인됩니다.
//
// 조커를 세지 않는 것이 규격입니다 — 「무엇을 내면 좋은가」이지 「이번 판의 점수가 얼마인가」가
// 아닙니다.

import { describe, expect, it } from 'vitest'

import { PokerHandKind } from '../src/generated/enums/poker-hand-kind'
import { RankKind } from '../src/generated/enums/rank-kind'
import { SuitKind } from '../src/generated/enums/suit-kind'
import { EnhancementKind } from '../src/generated/enums/enhancement-kind'
import { SealKind } from '../src/generated/enums/seal-kind'
import { EditionKind } from '../src/generated/enums/edition-kind'
import { loadFromDisk } from '../src/core/load-node'
import { newRun } from '../src/core/run'
import { bestHand, valueOf } from '../src/core/suggest'
import type { CardInstance } from '../src/core/state'

const DATA = 'public/data'
const data = loadFromDisk(DATA)

function card(uid: number, rank: RankKind, suit: SuitKind): CardInstance {
  return {
    uid,
    baseCardId: `c${uid}`,
    rank, suit,
    enhancement: EnhancementKind.None,
    seal: SealKind.None,
    edition: EditionKind.Base,
    bonusChips: 0,
    debuffed: false,
    faceDown: false,
  }
}

function fresh() {
  return newRun(data, 'CLOVER-SUGGEST', 'red_deck', 'White').state
}

describe('족보 도움', () => {
  it('플러시가 있으면 플러시를 권합니다', () => {
    const state = fresh()
    const held = [
      card(1, RankKind.Two, SuitKind.Heart),
      card(2, RankKind.Five, SuitKind.Heart),
      card(3, RankKind.Nine, SuitKind.Heart),
      card(4, RankKind.Jack, SuitKind.Heart),
      card(5, RankKind.King, SuitKind.Heart),
      card(6, RankKind.King, SuitKind.Spade),
      card(7, RankKind.Three, SuitKind.Club),
      card(8, RankKind.Seven, SuitKind.Diamond),
    ]

    const best = bestHand(data, state, held)
    expect(best).toBeDefined()
    expect(best!.hand).toBe(PokerHandKind.Flush)
    expect(best!.cards.map(c => c.uid).sort()).toEqual([1, 2, 3, 4, 5])
  })

  it('페어 하나뿐이면 페어를 권합니다', () => {
    const state = fresh()
    const held = [
      card(1, RankKind.Two, SuitKind.Heart),
      card(2, RankKind.Two, SuitKind.Spade),
      card(3, RankKind.Nine, SuitKind.Club),
      card(4, RankKind.Jack, SuitKind.Diamond),
    ]

    const best = bestHand(data, state, held)
    expect(best!.hand).toBe(PokerHandKind.Pair)
    expect(best!.cards.map(c => c.uid).sort()).toEqual([1, 2])
  })

  it('값이 같으면 장수가 적은 쪽입니다', () => {
    // 페어 하나에 아무 카드를 더해도 족보는 페어이고 값이 같습니다. **패에 남기는 것이
    // 이득이므로** 두 장을 권해야 합니다.
    const state = fresh()
    const held = [
      card(1, RankKind.Two, SuitKind.Heart),
      card(2, RankKind.Two, SuitKind.Spade),
      card(3, RankKind.Nine, SuitKind.Club),
    ]

    const best = bestHand(data, state, held)
    expect(best!.cards.length).toBe(2)
  })

  it('레벨을 올린 족보가 더 높습니다', () => {
    // 투 페어 레벨 1보다 페어 레벨 10이 값이 큽니다. **등급이 아니라 값으로 견주는 것**이
    // 규격인 이유가 이것입니다.
    const state = fresh()
    state.handLevels[PokerHandKind[PokerHandKind.Pair]] = 12

    const held = [
      card(1, RankKind.Two, SuitKind.Heart),
      card(2, RankKind.Two, SuitKind.Spade),
      card(3, RankKind.Nine, SuitKind.Club),
      card(4, RankKind.Nine, SuitKind.Diamond),
    ]

    const best = bestHand(data, state, held)
    expect(best!.hand).toBe(PokerHandKind.Pair)
  })

  it('빈 조합은 값이 없습니다', () => {
    expect(valueOf(data, fresh(), [])).toBeUndefined()
  })

  it('한 장은 하이 카드입니다', () => {
    const value = valueOf(data, fresh(), [card(1, RankKind.Ace, SuitKind.Spade)])
    expect(value!.hand).toBe(PokerHandKind.HighCard)
    expect(value!.value).toBeGreaterThan(0)
  })

  it('낼 수 있는 장수를 넘지 않습니다', () => {
    const state = fresh()
    const held = Array.from({ length: 8 }, (_, i) =>
      card(i + 1, RankKind.Two, i % 4 as SuitKind))

    const best = bestHand(data, state, held)
    expect(best!.cards.length).toBeLessThanOrEqual(data.run.maxPlayedCards)
  })
})
