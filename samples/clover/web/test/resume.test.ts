// 그만둔 판이 그대로 이어지는가.
//
// **저장은 상태가 아니라 액션 목록입니다.** 그래서 「이어하기가 된다」는 곧 **액션 목록이
// 판의 전부를 담는다**는 뜻입니다 — 액션을 거치지 않고 바뀐 것이 하나라도 있으면 되살린
// 판은 그만두던 판과 다른 판입니다.
//
// 손패와 조커의 자리가 그런 자리였습니다. 자리는 규칙이고(득점은 낸 카드의 왼쪽부터, 조커도
// 왼쪽부터), 자리를 옮기는 것은 화면에서만 일어났습니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import type { Data } from '../src/core/data'
import { loadFromDisk } from '../src/core/load-node'
import { JokerPool } from '../src/generated/enums/joker-pool'
import { canonical, snapshotHash } from '../src/core/hash'
import { choiceOf, poolsOf } from '../src/core/pool'
import { apply, newRun, type Action } from '../src/core/run'
import type { RunState } from '../src/core/state'

const DATA = path.resolve(__dirname, '..', 'public', 'data')

let data: Data

beforeAll(() => {
  data = loadFromDisk(DATA)
})

/** 액션 목록을 처음부터 다시 돌립니다. **이어하기가 지나는 길과 같습니다.** */
function replay(actions: Action[], seed = 'CLOVER-0001', deck = 'red_deck',
                stake = 'White'): RunState {
  const state = newRun(data, seed, deck, stake, [JokerPool.Base], '').state
  for (const action of actions) {
    apply(data, state, action)
    if (state.phase === 'lost' || state.phase === 'won') break
  }
  return state
}

/** 라운드가 돌고 손에 카드가 있는 자리까지 갑니다. */
function inRound(): { state: RunState; actions: Action[] } {
  const actions: Action[] = [{ t: 'select_blind' }]
  const state = replay(actions)
  expect(state.phase).toBe('round')
  expect(state.hand.length).toBeGreaterThan(1)
  return { state, actions }
}

/** 조커 하나를 줄 끝에 세웁니다. **상점을 지나지 않고 자리만 만듭니다.** */
function give(state: RunState, jokerId: string): void {
  state.jokers.push({
    uid: state.nextUid++, jokerId,
    edition: 0 as never, sticker: 0 as never,
    counters: {
      chips: 0, multAdd: 0, multMul: 0, money: 0, sellValue: 0, charge: 0, tick: 0,
    },
    age: 0, disabled: false,
  })
}

describe('자리 바꾸기가 액션입니다', () => {
  it('손패의 차례를 바꿉니다', () => {
    const { state } = inRound()
    const order = state.hand.slice().reverse()
    apply(data, state, { t: 'reorder', what: 'hand', order })
    expect(state.hand).toEqual(order)
  })

  it('조커의 차례를 바꿉니다', () => {
    const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base], '').state
    // 조커 둘을 손으로 세웁니다.
    give(state, 'twig')
    give(state, 'albatross')
    const order = state.jokers.map(joker => joker.uid).reverse()
    apply(data, state, { t: 'reorder', what: 'joker', order })
    expect(state.jokers.map(joker => joker.uid)).toEqual(order)
    // 조커가 통째로 살아 있어야 합니다 — 자리만 바뀐 것이지 다시 만든 것이 아닙니다.
    expect(state.jokers.map(joker => joker.jokerId)).toEqual(['albatross', 'twig'])
  })

  it('같은 것들이 아니면 받지 않습니다', () => {
    const { state } = inRound()
    const kept = state.hand.slice()

    // 없는 `uid`.
    apply(data, state, { t: 'reorder', what: 'hand', order: [...kept.slice(1), 999_999] })
    expect(state.hand).toEqual(kept)

    // 한 장이 빠진 것. **받으면 손패가 그 자리에서 줄어듭니다.**
    apply(data, state, { t: 'reorder', what: 'hand', order: kept.slice(1) })
    expect(state.hand).toEqual(kept)

    // 같은 것이 둘. **받으면 카드 하나가 복제됩니다.**
    apply(data, state, { t: 'reorder', what: 'hand', order: kept.map(() => kept[0]) })
    expect(state.hand).toEqual(kept)
  })

  it('되살린 판이 적어 둔 판과 같습니다', () => {
    const { state, actions } = inRound()
    const shuffled = [...state.hand].sort((a, b) => (a % 7) - (b % 7) || a - b)
    const log = [...actions, { t: 'reorder', what: 'hand', order: shuffled } as Action]
    const live = replay(log)

    expect(live.hand).toEqual(shuffled)
    // **해시로 갈립니다.** 이어하기가 저장을 버릴지 말지를 이것으로 정합니다.
    expect(snapshotHash(replay(log))).toBe(snapshotHash(live))
    // 해시는 표본이므로, 담기지 않은 칸까지 통째로 견줍니다.
    expect(canonical(replay(log))).toBe(canonical(live))
  })

  it('자리를 적지 않으면 되살린 판이 어긋납니다', () => {
    // **이 시험이 결함 자체입니다.** 화면에서만 자리를 옮기던 때가 이 상태였습니다.
    const { state, actions } = inRound()
    const moved = [state.hand[1], state.hand[0], ...state.hand.slice(2)]
    state.hand = moved
    expect(snapshotHash(state)).not.toBe(snapshotHash(replay(actions)))
  })
})

describe('조커의 자리가 점수를 바꿉니다', () => {
  it('더하기와 곱하기의 차례를 바꾸면 다른 점수가 납니다', () => {
    // **자리가 점수를 바꾸지 않는다면 적어 둘 이유도 없습니다.** 그것이 이 시험입니다 —
    // 더하기 뒤의 곱하기와 곱하기 뒤의 더하기는 다른 값을 냅니다.
    const score = (order: string[]): number => {
      const { state } = inRound()
      for (const id of order) give(state, id)
      apply(data, state, { t: 'play', cards: state.hand.slice(0, 5) })
      return state.score
    }

    expect(score(['twig', 'albatross'])).not.toBe(score(['albatross', 'twig']))
  })

  it('자리를 옮긴 뒤의 점수가 그 자리에서 시작한 것과 같습니다', () => {
    // 되살리는 길은 **처음부터 다시 도는 것**이므로, 옮긴 결과가 처음부터 그 차례였던
    // 것과 같아야 합니다.
    const laid = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base], '').state
    apply(data, laid, { t: 'select_blind' })
    give(laid, 'albatross')
    give(laid, 'twig')

    const moved = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base], '').state
    apply(data, moved, { t: 'select_blind' })
    give(moved, 'twig')
    give(moved, 'albatross')
    apply(data, moved, {
      t: 'reorder', what: 'joker',
      order: moved.jokers.map(joker => joker.uid).reverse(),
    })

    const cards = laid.hand.slice(0, 5)
    apply(data, laid, { t: 'play', cards })
    apply(data, moved, { t: 'play', cards })
    expect(moved.score).toBe(laid.score)
  })
})

describe('판의 설정은 판에서 읽습니다', () => {
  it('풀의 갈래가 오갑니다', () => {
    expect(choiceOf(poolsOf('base'))).toBe('base')
    expect(choiceOf(poolsOf('all'))).toBe('all')
    // **옵션이 아니라 판을 봅니다.** 판이 도는 동안 옵션이 바뀌어도 저장은 그대로여야
    // 합니다 — 다른 풀로 되살아난 판은 상점부터 다릅니다.
    const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White', poolsOf('all'), '').state
    expect(choiceOf(state.pools)).toBe('all')
  })
})
