// 라운드가 끝나지 않고 남는 자리가 있는가.
//
// **진행이 멈추는 결함은 화면에 아무 표시도 남기지 않습니다.** 오류도 나지 않고 그림도
// 멀쩡하며, 다만 무엇을 눌러도 다음이 오지 않습니다 — 사람은 그것을 「어느 순간부터 진행이
// 안 된다」로만 말할 수 있고, 그 말로는 어디를 봐야 할지 알 수 없습니다.
//
// 그래서 규칙 쪽에서 잠급니다: **낼 핸드가 0인 채로 라운드가 남아 있으면 그것은 막다른
// 길입니다.** 버리기가 남아 있어도 버리는 것으로는 점수가 오르지 않습니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import type { Data } from '../src/core/data'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun } from '../src/core/run'
import type { RunState } from '../src/core/state'

const DATA = path.resolve(__dirname, '..', 'public', 'data')

let data: Data

beforeAll(() => {
  data = loadFromDisk(DATA)
})

/**
 * 블라인드를 골라 라운드가 도는 상태.
 *
 * **패는 라운드가 시작되어야 깔립니다.** 국면만 `round` 로 적어 두면 손에 아무것도 없어서
 * 낼 것이 없고, 그러면 핸드가 줄지 않아 이 시험이 아무것도 보지 않습니다.
 */
function playingState(target: number): RunState {
  const state = newRun(data, 'TEST-STUCK', 'red_deck', 'White').state
  apply(data, state, { t: 'select_blind' })
  // 닿을 수 없는 요구 점수. **지는 쪽을 보려는 것입니다.**
  state.target = target
  return state
}

/** 낼 수 있는 아무 한 장. 점수는 상관없습니다 — 여기서 보는 것은 판정입니다. */
function playOne(state: RunState): void {
  apply(data, state, { t: 'play', cards: state.hand.slice(0, 1) })
}

describe('막다른 길', () => {
  it('핸드를 다 쓰면 라운드가 끝납니다', () => {
    const state = playingState(1_000_000_000)
    expect(state.phase).toBe('round')

    for (let guard = 0; guard < 20 && state.handsLeft > 0; guard++) playOne(state)

    expect(state.handsLeft).toBe(0)
    // **끝나야 합니다.** 점수가 모자란 채로 핸드가 0이면 진 것입니다.
    expect(state.phase).toBe('lost')
  })

  it('패배를 막으면 그 블라인드를 넘깁니다', () => {
    const state = playingState(1_000_000_000)
    // `old_bones` 를 들려 줍니다. **막기만 하고 아무것도 하지 않으면**, 핸드가 0인 채로
    // 라운드가 남아 그 판이 영영 끝나지 않았습니다.
    state.jokers = [{
      uid: state.nextUid++,
      jokerId: 'old_bones',
      edition: 0 as never,
      sticker: 0 as never,
      counters: { chips: 0, multAdd: 0, multMul: 10_000, money: 0, sellValue: 5, charge: 0, tick: 0 },
      age: 0,
      disabled: false,
    }]

    for (let guard = 0; guard < 20 && state.handsLeft > 0; guard++) playOne(state)

    expect(state.handsLeft).toBe(0)
    // 진 것도 아니고 라운드도 아닙니다 — 넘긴 것입니다.
    expect(state.phase).not.toBe('round')
  })
})
