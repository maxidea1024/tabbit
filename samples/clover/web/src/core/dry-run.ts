// 상태를 복제해 득점을 미리 세는 것.
//
// **인사이트가 득점 규칙을 다시 구현하지 않기 위해 있습니다.** 다시 구현하면 그 둘이
// 어긋나고, 어긋난 쪽이 인사이트이면 사람이 화면에 적힌 숫자를 믿지 않게 됩니다.
//
// **원본은 한 바이트도 바뀌지 않습니다.** 득점은 조커의 누적값을 늘리고 카드를 부수고 돈을
// 옮기므로, 복제가 얕으면 판을 열어 보는 것만으로 런이 달라집니다. 게이트가 `snapshotHash`
// 로 그것을 봅니다.
//
// 규격은 `doc/insight.md` 의 「결정 3」과 「결정 4」입니다.

import { Pcg32 } from './rng'
import { scoreHand, type ScoreResult } from './scoring'
import { newVm, type ChanceSkip } from './vm'
import type { Data } from './data'
import type { CardInstance, GameEvent, RunState } from './state'

/**
 * 상태 하나의 깊은 복제본.
 *
 * **난수만 손으로 옮깁니다.** `Pcg32` 는 클래스이므로 `structuredClone` 이 옮기지 못하고,
 * 옮기지 못한 채로 두면 복제본과 원본이 같은 난수를 함께 씁니다 — 미리 세어 본 것이 그대로
 * 실제 판의 난수를 소비합니다. 세이브가 쓰는 두 값으로 다시 만듭니다.
 */
export function cloneState(state: RunState): RunState {
  const rng: Record<string, Pcg32> = {}
  for (const name of Object.keys(state.rng)) {
    rng[name] = Pcg32.restore(state.rng[name].save())
  }
  return { ...structuredClone({ ...state, rng: {} }), rng }
}

/** 미리 세어 본 한 판. */
export interface DryRun {
  result: ScoreResult
  /** 득점이 낸 이벤트 전부. 누가 얼마를 더했는지가 여기에 있습니다. */
  events: GameEvent[]
  /** 굴리지 않고 넘어간 확률 효과들. */
  chanced: ChanceSkip[]
}

/**
 * 이 카드들을 내면 몇 점인가.
 *
 * `jokerOrder` 를 주면 조커를 그 차례로 세웁니다 — 자리를 바꾸면 점수가 오르는지를 보는
 * 자리이고, **바꾼 차례로 실제 득점을 돌려 보는 것 말고는 확인할 방법이 없습니다.**
 * 조커의 효과는 순서에 따라 곱이 걸리는 값이 달라지기 때문입니다.
 *
 * 낼 수 없는 조합이면 비어 있습니다.
 */
export function dryScore(data: Data, state: RunState, uids: readonly number[],
                         jokerOrder?: readonly number[]): DryRun | undefined {
  if (uids.length === 0 || uids.length > data.run.maxPlayedCards) return undefined

  const copy = cloneState(state)

  if (jokerOrder !== undefined) {
    const byUid = new Map(copy.jokers.map(joker => [joker.uid, joker]))
    const moved = jokerOrder
      .map(uid => byUid.get(uid))
      .filter((joker): joker is (typeof copy.jokers)[number] => joker !== undefined)
    // **하나라도 못 찾으면 세지 않습니다.** 빠진 조커로 세면 그 조커가 없는 판의 점수가
    // 나오고, 그것은 자리를 바꾼 결과가 아닙니다.
    if (moved.length !== copy.jokers.length) return undefined
    copy.jokers = moved
  }

  const cards = uids
    .map(uid => copy.deck.find(card => card.uid === uid))
    .filter((card): card is CardInstance => card !== undefined)
  if (cards.length !== uids.length) return undefined

  // **`play` 액션이 득점 전에 하는 것과 같은 순서입니다.** 남은 핸드를 줄이지 않고 세면
  // `CondFirstHand` 와 `CondLastHand` 가 어긋나고, 그 둘을 가진 조커의 값이 예상 점수에서
  // 빠집니다.
  copy.handsLeft--
  copy.handsPlayedThisRun++
  copy.hand = copy.hand.filter(uid => !uids.includes(uid))
  copy.played = uids.slice()
  for (const uid of uids) copy.cardsPlayedThisAnte.push(uid)

  const vm = newVm(data, copy)
  vm.chanceMode = 'never'
  const result = scoreHand(vm, cards)

  return { result, events: vm.events, chanced: vm.chanceSkipped ?? [] }
}
