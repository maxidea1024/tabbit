// 족보 판정 12종과, 그것을 바꾸는 규칙 다섯.
//
// **판정이 틀리면 그 아래가 전부 틀립니다.** 그래서 여기가 가장 촘촘합니다.

import { describe, expect, it } from 'vitest'

import { EnhancementKind } from '../src/generated/enums/enhancement-kind'
import { PokerHandKind } from '../src/generated/enums/poker-hand-kind'
import { RankKind } from '../src/generated/enums/rank-kind'
import { SealKind } from '../src/generated/enums/seal-kind'
import { EditionKind } from '../src/generated/enums/edition-kind'
import { SuitKind } from '../src/generated/enums/suit-kind'
import { evaluate } from '../src/core/hand'
import type { CardInstance, Rules } from '../src/core/state'

let uid = 1

/** `S10` 처럼 적습니다 — 무늬 한 글자와 랭크입니다. */
function card(spec: string, enhancement = EnhancementKind.None): CardInstance {
  const suit = { S: SuitKind.Spade, H: SuitKind.Heart, C: SuitKind.Club, D: SuitKind.Diamond }[
    spec[0] as 'S' | 'H' | 'C' | 'D']
  const rankText = spec.slice(1)
  const rank = ({
    '2': 2, '3': 3, '4': 4, '5': 5, '6': 6, '7': 7, '8': 8, '9': 9, '10': 10,
    J: 11, Q: 12, K: 13, A: 14,
  } as Record<string, number>)[rankText]

  return {
    uid: uid++,
    baseCardId: spec,
    rank: rank as RankKind,
    suit: suit as SuitKind,
    enhancement,
    seal: SealKind.None,
    edition: EditionKind.Base,
    bonusChips: 0,
    debuffed: false,
    faceDown: false,
  }
}

function rules(overrides: Partial<Rules> = {}): Rules {
  return {
    flushStraightCards: 5,
    straightGap: 0,
    suitsMerged: false,
    allCardsScore: false,
    allCardsAreFace: false,
    ...overrides,
  } as Rules
}

function handOf(specs: string[], overrides: Partial<Rules> = {}): PokerHandKind {
  return evaluate(specs.map(spec => card(spec)), rules(overrides)).hand
}

describe('족보 12종', () => {
  it('하이 카드', () => {
    expect(handOf(['S2', 'H5', 'C9', 'DJ', 'SK'])).toBe(PokerHandKind.HighCard)
  })

  it('페어', () => {
    expect(handOf(['S2', 'H2', 'C9', 'DJ', 'SK'])).toBe(PokerHandKind.Pair)
  })

  it('투 페어', () => {
    expect(handOf(['S2', 'H2', 'C9', 'D9', 'SK'])).toBe(PokerHandKind.TwoPair)
  })

  it('트리플', () => {
    expect(handOf(['S2', 'H2', 'C2', 'DJ', 'SK'])).toBe(PokerHandKind.ThreeOfAKind)
  })

  it('스트레이트', () => {
    expect(handOf(['S2', 'H3', 'C4', 'D5', 'S6'])).toBe(PokerHandKind.Straight)
  })

  it('플러시', () => {
    expect(handOf(['S2', 'S5', 'S9', 'SJ', 'SK'])).toBe(PokerHandKind.Flush)
  })

  it('풀 하우스', () => {
    expect(handOf(['S2', 'H2', 'C2', 'DJ', 'SJ'])).toBe(PokerHandKind.FullHouse)
  })

  it('포 카드', () => {
    expect(handOf(['S2', 'H2', 'C2', 'D2', 'SK'])).toBe(PokerHandKind.FourOfAKind)
  })

  it('스트레이트 플러시', () => {
    expect(handOf(['S2', 'S3', 'S4', 'S5', 'S6'])).toBe(PokerHandKind.StraightFlush)
  })

  it('파이브 오브 어 카인드', () => {
    expect(handOf(['S2', 'H2', 'C2', 'D2', 'H2'])).toBe(PokerHandKind.FiveOfAKind)
  })

  it('플러시 하우스', () => {
    expect(handOf(['S2', 'S2', 'S2', 'SJ', 'SJ'])).toBe(PokerHandKind.FlushHouse)
  })

  it('플러시 파이브', () => {
    expect(handOf(['S2', 'S2', 'S2', 'S2', 'S2'])).toBe(PokerHandKind.FlushFive)
  })
})

describe('A 는 양쪽에 섭니다', () => {
  it('A 2 3 4 5', () => {
    expect(handOf(['SA', 'H2', 'C3', 'D4', 'S5'])).toBe(PokerHandKind.Straight)
  })

  it('10 J Q K A', () => {
    expect(handOf(['S10', 'HJ', 'CQ', 'DK', 'SA'])).toBe(PokerHandKind.Straight)
  })

  it('K A 2 3 4 는 스트레이트가 아닙니다', () => {
    expect(handOf(['SK', 'HA', 'C2', 'D3', 'S4'])).not.toBe(PokerHandKind.Straight)
  })
})

describe('판정을 바꾸는 규칙', () => {
  it('`four_knuckles` 는 4장으로 플러시를 이룹니다', () => {
    const specs = ['S2', 'S5', 'S9', 'SJ', 'HK']
    expect(handOf(specs)).toBe(PokerHandKind.HighCard)
    expect(handOf(specs, { flushStraightCards: 4 })).toBe(PokerHandKind.Flush)
  })

  it('`stepping_stone` 은 빈틈 하나를 허용합니다', () => {
    const specs = ['S3', 'H5', 'C6', 'D8', 'S10']
    expect(handOf(specs)).toBe(PokerHandKind.HighCard)
    expect(handOf(specs, { straightGap: 1 })).toBe(PokerHandKind.Straight)
  })

  it('`smudged_pane` 은 두 무늬를 하나로 봅니다', () => {
    const specs = ['S2', 'C5', 'S9', 'CJ', 'SK']
    expect(handOf(specs)).toBe(PokerHandKind.HighCard)
    expect(handOf(specs, { suitsMerged: true })).toBe(PokerHandKind.Flush)
  })

  it('와일드 카드는 어느 무늬로도 셉니다', () => {
    const cards = [
      card('S2'), card('S5'), card('S9'), card('SJ'), card('HK', EnhancementKind.Wild),
    ]
    expect(evaluate(cards, rules()).hand).toBe(PokerHandKind.Flush)
  })

  it('`downpour` 는 낸 카드 전부를 득점하게 합니다', () => {
    const specs = ['S2', 'H2', 'C9', 'DJ', 'SK']
    expect(evaluate(specs.map(s => card(s)), rules()).scoring).toHaveLength(2)
    expect(evaluate(specs.map(s => card(s)), rules({ allCardsScore: true })).scoring)
      .toHaveLength(5)
  })
})

describe('돌 카드', () => {
  it('족보를 이루지 않고 항상 득점합니다', () => {
    const cards = [
      card('S2'), card('H2'), card('C9'), card('DJ'),
      card('SK', EnhancementKind.Stone),
    ]
    const result = evaluate(cards, rules())

    expect(result.hand).toBe(PokerHandKind.Pair)
    // 페어 2장에 돌 1장입니다.
    expect(result.scoring).toHaveLength(3)
    expect(result.scoring.some(c => c.enhancement === EnhancementKind.Stone)).toBe(true)
  })
})

describe('득점 카드의 순서', () => {
  it('낸 순서 그대로입니다', () => {
    const cards = [card('SK'), card('H2'), card('C9'), card('D2'), card('S5')]
    const result = evaluate(cards, rules())
    const order = result.scoring.map(c => c.uid)
    expect(order).toEqual([...order].sort((a, b) => a - b))
  })
})
