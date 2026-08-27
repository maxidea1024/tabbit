// 득점과 효과 VM.
//
// **값이 정확히 얼마인지를 봅니다.** 「대충 늘었다」가 아니라 「28 이 84 가 되었다」여야,
// 유니티 쪽이 같은 답을 냈는지 판정할 수 있습니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import { EditionKind } from '../src/generated/enums/edition-kind'
import { EnhancementKind } from '../src/generated/enums/enhancement-kind'
import { PokerHandKind } from '../src/generated/enums/poker-hand-kind'
import { RankKind } from '../src/generated/enums/rank-kind'
import { SealKind } from '../src/generated/enums/seal-kind'
import { SuitKind } from '../src/generated/enums/suit-kind'
import type { Data } from '../src/core/data'
import { loadFromDisk } from '../src/core/load-node'
import { newRun } from '../src/core/run'
import { scoreHand } from '../src/core/scoring'
import { newCounters, type CardInstance, type RunState } from '../src/core/state'
import { newVm } from '../src/core/vm'
import { MULT_ONE } from '../src/core/units'

const DATA = path.resolve(__dirname, '..', 'public', 'data')

let data: Data

beforeAll(() => {
  data = loadFromDisk(DATA)
})

function freshState(): RunState {
  return newRun(data, 'TEST-0001', 'red_deck', 'White').state
}

let uid = 10_000

function card(rank: number, suit: SuitKind, enhancement = EnhancementKind.None): CardInstance {
  return {
    uid: uid++,
    baseCardId: 'T',
    rank: rank as RankKind,
    suit,
    enhancement,
    seal: SealKind.None,
    edition: EditionKind.Base,
    bonusChips: 0,
    debuffed: false,
    faceDown: false,
  }
}

/** 조커를 심고 한 패를 냅니다. 점수만 돌려줍니다. */
function scoreWith(jokerIds: string[], cards: CardInstance[],
                   edition = EditionKind.Base) {
  const state = freshState()
  state.deck = cards.slice()
  state.hand = []

  for (const jokerId of jokerIds) {
    state.jokers.push({
      uid: uid++,
      jokerId,
      edition,
      sticker: 0 as never,
      counters: newCounters(),
      age: 0,
      disabled: false,
    })
  }

  const vm = newVm(data, state)
  const result = scoreHand(vm, cards)
  return { result, state, vm }
}

const PAIR_OF_TWOS = () => [card(2, SuitKind.Spade), card(2, SuitKind.Heart)]

describe('기본값', () => {
  it('조커가 없으면 족보의 값 그대로입니다', () => {
    const { result } = scoreWith([], PAIR_OF_TWOS())

    // 페어는 칩 10 · 배수 2 입니다. 카드 둘이 칩 2씩 더하므로 14 × 2 입니다.
    expect(result.hand).toBe(PokerHandKind.Pair)
    expect(result.chips).toBe(14)
    expect(result.mult).toBe(2 * MULT_ONE)
    expect(result.score).toBe(28)
  })

  it('A 는 칩 11 입니다', () => {
    const { result } = scoreWith([], [card(14, SuitKind.Spade), card(14, SuitKind.Heart)])
    expect(result.chips).toBe(10 + 11 + 11)
  })
})

describe('연산', () => {
  it('`AddMult` — 둥근 잔가지가 배수 +4', () => {
    const { result } = scoreWith(['twig'], PAIR_OF_TWOS())
    expect(result.mult).toBe(6 * MULT_ONE)
    expect(result.score).toBe(14 * 6)
  })

  it('`AddMult` + 조건 — 휘파람새가 페어에 배수 +8', () => {
    const { result } = scoreWith(['warbler'], PAIR_OF_TWOS())
    expect(result.mult).toBe(10 * MULT_ONE)
  })

  it('`AddChips` + 조건 — 귀뚜라미가 페어에 칩 +50', () => {
    const { result } = scoreWith(['cricket'], PAIR_OF_TWOS())
    expect(result.chips).toBe(64)
    expect(result.score).toBe(128)
  })

  it('`MulMult` — 맺음이 페어에 배수 ×2', () => {
    const { result } = scoreWith(['the_bond'], PAIR_OF_TWOS())
    expect(result.mult).toBe(4 * MULT_ONE)
  })

  it('`PerUnit` — 엉킴이 조커 하나마다 배수 +3', () => {
    const { result } = scoreWith(['tangle', 'twig'], PAIR_OF_TWOS())
    // 조커가 둘이므로 +6, 거기에 잔가지의 +4 입니다.
    expect(result.mult).toBe((2 + 6 + 4) * MULT_ONE)
  })

  it('`PerUnit` 의 `MulMult` — 빈 액자가 빈 슬롯마다 ×1', () => {
    const { result } = scoreWith(['empty_frame'], PAIR_OF_TWOS())
    // 슬롯 5개 중 하나를 자기가 쓰므로 빈 자리가 4 입니다.
    expect(result.mult).toBe(2 * 4 * MULT_ONE)
  })

  it('카드 무늬 조건 — 클로버꽃이 클럽마다 배수 +3', () => {
    const clubs = [card(2, SuitKind.Club), card(2, SuitKind.Club)]
    const { result } = scoreWith(['clover_bloom'], clubs)
    expect(result.mult).toBe((2 + 3 + 3) * MULT_ONE)
  })

  it('랭크 집합 조건 — 짝수 담쟁이가 짝수마다 배수 +4', () => {
    const { result } = scoreWith(['even_ivy'], PAIR_OF_TWOS())
    expect(result.mult).toBe((2 + 4 + 4) * MULT_ONE)
  })

  it('그림 카드 조건 — 무서운 가면이 그림마다 칩 +30', () => {
    const faces = [card(13, SuitKind.Spade), card(13, SuitKind.Heart)]
    const { result } = scoreWith(['grim_mask'], faces)
    expect(result.chips).toBe(10 + 10 + 10 + 30 + 30)
  })
})

describe('강화와 인장과 에디션', () => {
  it('`Bonus` 는 칩 +30', () => {
    const cards = [card(2, SuitKind.Spade, EnhancementKind.Bonus), card(2, SuitKind.Heart)]
    const { result } = scoreWith([], cards)
    expect(result.chips).toBe(14 + 30)
  })

  it('`Mult` 는 배수 +4', () => {
    const cards = [card(2, SuitKind.Spade, EnhancementKind.Mult), card(2, SuitKind.Heart)]
    const { result } = scoreWith([], cards)
    expect(result.mult).toBe(6 * MULT_ONE)
  })

  it('`Glass` 는 배수 ×2', () => {
    const cards = [card(2, SuitKind.Spade, EnhancementKind.Glass), card(2, SuitKind.Heart)]
    const { result } = scoreWith([], cards)
    expect(result.mult).toBe(4 * MULT_ONE)
  })

  it('`Stone` 은 칩 +50 이고 랭크값이 없습니다', () => {
    const cards = [
      card(2, SuitKind.Spade), card(2, SuitKind.Heart),
      card(9, SuitKind.Club, EnhancementKind.Stone),
    ]
    const { result } = scoreWith([], cards)
    expect(result.chips).toBe(14 + 50)
  })

  it('`Red Seal` 은 그 카드를 한 번 더 발동합니다', () => {
    const cards = PAIR_OF_TWOS()
    cards[0].seal = SealKind.Red
    const { result } = scoreWith([], cards)
    // 2 가 한 번 더 들어갑니다.
    expect(result.chips).toBe(16)
  })

  it('조커의 `Foil` 은 칩 +50', () => {
    const plain = scoreWith(['twig'], PAIR_OF_TWOS()).result
    const foil = scoreWith(['twig'], PAIR_OF_TWOS(), EditionKind.Foil).result
    expect(foil.chips).toBe(plain.chips + 50)
  })

  it('조커의 `Polychrome` 은 배수 ×1.5', () => {
    const plain = scoreWith(['twig'], PAIR_OF_TWOS()).result
    const poly = scoreWith(['twig'], PAIR_OF_TWOS(), EditionKind.Polychrome).result
    expect(poly.mult).toBe(Math.floor((plain.mult * 15_000) / MULT_ONE))
  })
})

describe('무력화', () => {
  it('무력화된 카드는 칩과 강화를 잃고 족보에는 남습니다', () => {
    const cards = [card(2, SuitKind.Spade, EnhancementKind.Bonus), card(2, SuitKind.Heart)]
    cards[0].debuffed = true
    const { result } = scoreWith([], cards)

    expect(result.hand).toBe(PokerHandKind.Pair)
    expect(result.chips).toBe(10 + 2)
  })
})

describe('조커의 순서', () => {
  it('가산이 먼저인지 곱이 먼저인지가 자리로 정해집니다', () => {
    const addThenMul = scoreWith(['twig', 'the_bond'], PAIR_OF_TWOS()).result
    const mulThenAdd = scoreWith(['the_bond', 'twig'], PAIR_OF_TWOS()).result

    // (2+4)×2 = 12 이고 2×2+4 = 8 입니다. **자리를 바꾸면 점수가 바뀝니다.**
    expect(addThenMul.mult).toBe(12 * MULT_ONE)
    expect(mulThenAdd.mult).toBe(8 * MULT_ONE)
  })
})

describe('`Custom` 의 개수', () => {
  it('문서에 있는 것만 돕니다', () => {
    const state = freshState()
    const vm = newVm(data, state)
    scoreHand(vm, PAIR_OF_TWOS())
    for (const handler of vm.customsRun) expect(['pruning_shears']).toContain(handler)
  })
})
