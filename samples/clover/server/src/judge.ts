// 제출을 다시 돌려 판정합니다.
//
// **점수를 받지 않고 리플레이를 받는 이유가 여기 있습니다.** 코어가 결정론적이므로 같은
// 리플레이가 같은 지표를 냅니다 — 클라이언트가 보낸 숫자를 믿을 자리가 없습니다.
//
// 규격은 `doc/leaderboard/submission.md` 의 「서버의 재현」입니다.

import { apply, newRun, type Action } from '../../web/src/core/run'
import { newMetrics, observe, seal, type Metrics } from '../../web/src/core/metrics'
import type { RunState } from '../../web/src/core/state'
import { loadData, poolsOf, type PoolChoice } from './core'

/** 클라이언트가 보내는 것. */
export interface Submitted {
  seed: string
  deck: string
  stake: string
  pool: PoolChoice
  /** 챌린지가 아니면 빈 문자열입니다. */
  challenge: string
  actions: Action[]
}

export type Verdict =
  | { ok: true; metrics: Metrics }
  | { ok: false; reason: RejectReason }

export type RejectReason = 'invalid_action' | 'unfinished' | 'too_long'

/** 액션 수의 상한. 지금 구워 둔 것 중 가장 긴 것이 143수입니다. */
export const MAX_ACTIONS = 3_000

/** 재현에 쓸 수 있는 시간. 헤드리스가 리플레이 하나를 0.1초 안에 돕니다. */
export const TIME_BUDGET_MS = 5_000

/**
 * 리플레이 하나를 다시 돌립니다.
 *
 * **예외를 판정으로 바꿉니다.** 코어가 `throw` 하는 것은 「이 액션은 이 상태에서 있을 수
 * 없다」는 뜻이고, 그것은 서버가 멈출 일이 아니라 그 제출이 거부될 일입니다.
 */
export function judge(dataPath: string, submitted: Submitted): Verdict {
  if (submitted.actions.length > MAX_ACTIONS) return { ok: false, reason: 'too_long' }

  const data = loadData(dataPath)
  const started = Date.now()

  let state: RunState
  const acc = newMetrics()
  try {
    const start = newRun(data, submitted.seed, submitted.deck, submitted.stake,
                         poolsOf(submitted.pool), submitted.challenge)
    state = start.state
    observe(acc, start.events)
  } catch {
    return { ok: false, reason: 'invalid_action' }
  }

  for (const action of submitted.actions) {
    if (Date.now() - started > TIME_BUDGET_MS) return { ok: false, reason: 'too_long' }
    try {
      const step = apply(data, state, action)
      state = step.state
      observe(acc, step.events)
    } catch {
      return { ok: false, reason: 'invalid_action' }
    }
    if (state.phase === 'won' || state.phase === 'lost') break
  }

  // **끝나지 않은 런은 순위에 올라가지 않습니다.** 도중에 그만둔 것을 올리면 좋은 자리에서
  // 멈추는 것이 전략이 됩니다.
  if (state.phase !== 'won' && state.phase !== 'lost') {
    return { ok: false, reason: 'unfinished' }
  }

  return { ok: true, metrics: seal(data, acc, state) }
}
