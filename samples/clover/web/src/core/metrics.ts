// 리더보드의 지표.
//
// **상태에 칸을 더하지 않습니다.** 해시에 들어가지 않는 값을 상태에 두면 두 구현이 다르게
// 세어도 대조가 그것을 보지 못하고, 해시에 넣으면 구워 둔 리플레이의 해시가 전부
// 달라집니다. 그래서 여기는 상태와 이벤트를 읽기만 합니다.
//
// **누적이 필요한 것은 하나뿐입니다.** `apply` 가 상태를 그 자리에서 고치므로, 스텝을 모아
// 두어도 같은 객체가 늘어설 뿐이고 지나간 값이 남지 않습니다 — 지나간 값을 보아야 하는
// `bestHand` 만 액션마다 이벤트에서 받아 둡니다. 나머지 다섯은 끝난 상태 하나에서 나옵니다.
//
// 규격은 `doc/leaderboard/boards.md` 의 「지표」입니다. **클라이언트와 서버가 이 함수를
// 같이 씁니다** — 두 곳에 같은 셈을 적으면 한쪽만 고쳐지는 날이 옵니다.

import { BlindKind } from '../generated/enums/blind-kind'
import { StakeKind } from '../generated/enums/stake-kind'
import type { Data } from './data'
import type { GameEvent, RunState } from './state'

/** 한 안테의 블라인드 수. 스몰 · 빅 · 보스입니다. */
const BLINDS_PER_ANTE = 3

/**
 * 등정에서 한 스테이크가 차지하는 폭.
 *
 * **블라인드 수보다 하나 넓습니다.** 지나온 블라인드가 0부터 24까지 **25가지**이므로,
 * 24로 잡으면 「흰 스테이크 완주」와 「붉은 스테이크 시작」이 같은 수가 됩니다 — 순서는
 * 맞지만 그 수에서 스테이크와 자리를 되읽을 수 없게 됩니다.
 */
export function ascentPerStake(data: Data): number {
  return data.run.winAnte * BLINDS_PER_ANTE + 1
}

/** 순위에 올라가는 값들. */
export interface Metrics {
  /** 등정. `(스테이크 − 1) × 25 + 지나온 블라인드`. 흰 완주가 24, 금 완주가 199 입니다. */
  ascent: number
  /** 한 손이 낸 가장 큰 점수. */
  bestHand: number
  /** 이 런에서 낸 핸드 수. **완주한 런에서만 뜻이 있습니다.** */
  handsPlayed: number
  /** 끝났을 때의 소지금. **완주한 런에서만 뜻이 있습니다.** */
  money: number
  /** 건너뛴 블라인드 수. **완주한 런에서만 뜻이 있습니다.** */
  skips: number
  /** 완주하였는가. */
  won: boolean
}

/** 런이 도는 동안 들고 있는 것. */
export interface MetricsAcc {
  bestHand: number
}

export function newMetrics(): MetricsAcc {
  return { bestHand: 0 }
}

/**
 * 액션 하나가 낸 이벤트를 봅니다.
 *
 * **`ScoreResolved` 의 `score` 는 그 한 손의 점수입니다** — 라운드의 누계가 아닙니다.
 * 누계를 보면 라운드가 끝날 때 0으로 돌아가므로 뺄셈이 음수가 되는 자리가 생깁니다.
 */
export function observe(acc: MetricsAcc, events: readonly GameEvent[]): void {
  for (const event of events) {
    if (event.t === 'ScoreResolved' && event.score > acc.bestHand) {
      acc.bestHand = event.score
    }
  }
}

/**
 * 지나온 블라인드 수.
 *
 * **깬 블라인드가 아니라 지나온 블라인드입니다.** 건너뛴 것도 그만큼 나아간 것이므로 함께
 * 셉니다 — 깬 것만 세면 다섯 번 건너뛰고 완주한 사람이 마지막 보스에서 진 사람보다 아래에
 * 놓입니다.
 *
 * 완주하면 `ante` 가 승리 안테를 넘어서므로 상한으로 눌러 둡니다.
 */
export function progressOf(data: Data, state: RunState): number {
  const passed = (state.ante - 1) * BLINDS_PER_ANTE + blindIndex(state.blind)
  return Math.max(0, Math.min(passed, data.run.winAnte * BLINDS_PER_ANTE))
}

/**
 * 이 스테이크가 몇 번째인가. 흰색이 1이고 금색이 8입니다.
 *
 * **적히는 형태가 셋입니다.** 리플레이와 `newRun` 은 enum 의 이름(`White`)을 적고, 화면에서
 * 값이 문자열로 오기도 하며(`1`), 시트의 `name` 칸은 표시 이름(`흰색`)입니다. 셋 다
 * 받습니다 — 하나만 받으면 나머지 둘이 조용히 흰색으로 떨어집니다.
 */
export function stakeIndexOf(data: Data, stake: string): number {
  const row = data.tables.stake.records.find(entry =>
    StakeKind[entry.stake] === stake || String(entry.stake) === stake || entry.name === stake)
  return row ? Number(row.stake) : 1
}

/**
 * 끝난 런의 지표.
 *
 * **끝나지 않은 런에도 값이 나옵니다.** `ascent` 는 도중에도 뜻이 있고, 나머지는 완주한
 * 런에서만 순위에 올라갑니다 — 어느 지표가 완주를 요구하는지는 보드가 정하고 여기는
 * 정하지 않습니다.
 */
export function seal(data: Data, acc: MetricsAcc, state: RunState): Metrics {
  const perStake = ascentPerStake(data)
  const stake = stakeIndexOf(data, state.stake)
  return {
    ascent: (stake - 1) * perStake + progressOf(data, state),
    bestHand: acc.bestHand,
    handsPlayed: state.handsPlayedThisRun,
    money: state.money,
    skips: state.blindsSkipped,
    won: state.phase === 'won',
  }
}

function blindIndex(blind: BlindKind): number {
  switch (blind) {
    case BlindKind.Small: return 0
    case BlindKind.Big: return 1
    case BlindKind.Boss: return 2
    default: return 0
  }
}
