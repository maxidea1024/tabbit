// 도중에 그만둔 판.
//
// **상태가 아니라 액션을 적습니다.** 서버의 판정도 `headless` 도 `newRun` 뒤에 `apply` 를
// 차례로 돌려 같은 판을 다시 만들고, 저장이 그 길을 그대로 쓰면 되살린 판과 서버가 세는
// 판이 어긋날 자리가 없습니다. 상태를 통째로 적으면 `Pcg32` 를 손으로 옮겨야 하고, 상태에
// 칸이 하나 늘 때마다 예전 저장을 못 쓰게 됩니다.
//
// **되살린 것이 저장한 것과 같은지 봅니다.** `apply` 는 받을 수 없는 액션을 조용히
// 넘기므로, 저장이 손상되어도 오류가 나지 않고 다른 판이 하나 생깁니다 — 마지막 상태의
// 해시를 함께 적어 두고 되살린 뒤에 견줍니다.

import type { PoolChoice } from './pool'
import type { Action } from './run'
import type { RunState } from './state'

/** 저장의 갈래. **모양이 바뀌면 올립니다** — 예전 저장은 그 자리에서 버려집니다. */
const VERSION = 1

const KEY = 'clover.run'

/** 랭크 런이면 서버가 준 것들. 이어서 해도 올릴 수 있어야 합니다. */
export interface SavedRanked {
  seed: string
  deck: string
  stake: string
  pool: string
  challenge: string
}

/** 도중에 그만둔 판 하나. */
export interface SavedRun {
  version: number
  seed: string
  deckId: string
  stake: string
  pool: PoolChoice
  /** 챌린지 런이면 그 식별자. 없으면 빈 문자열입니다. */
  challengeId: string
  actions: Action[]
  /** 마지막 액션 뒤의 상태 해시. */
  hash: string
  /** 랭크 런이 아니면 없습니다. */
  ranked?: SavedRanked
  savedAt: number
  /** 되살리지 않고 목록에 적을 것들. */
  ante: number
  money: number
  jokers: number
  phase: string
}

/** 이 판을 목록에 적을 값들. 되살리지 않고 읽을 수 있어야 합니다. */
function digest(state: RunState): Pick<SavedRun, 'ante' | 'money' | 'jokers' | 'phase'> {
  return {
    ante: state.ante,
    money: state.money,
    jokers: state.jokers.length,
    phase: state.phase,
  }
}

/**
 * 이 판을 적어 둡니다.
 *
 * **끝난 판은 적지 않습니다.** 이어서 할 것이 없는 판을 적어 두면 타이틀에 「이어하기」가
 * 남고, 눌러 보면 진 자리로 되돌아갑니다.
 */
export function saveRun(entry: Omit<SavedRun, 'version' | 'savedAt' | 'ante' | 'money'
                                              | 'jokers' | 'phase'>,
                        state: RunState): void {
  if (state.phase === 'lost' || state.phase === 'won') {
    clearRun()
    return
  }
  const saved: SavedRun = {
    ...entry,
    ...digest(state),
    version: VERSION,
    savedAt: Date.now(),
  }
  try {
    localStorage.setItem(KEY, JSON.stringify(saved))
  } catch {
    // 저장하지 못해도 판은 돌아야 합니다. 이번 판에만 적용된다는 뜻입니다.
  }
}

/**
 * 저장된 판. 없거나 읽을 수 없으면 `undefined` 입니다.
 *
 * **믿지 않고 읽습니다.** 손으로 고친 저장소나 예전 판의 값이 들어 있을 수 있고, 그것을
 * 그대로 `newRun` 에 넘기면 시작 조건이 하나도 걸리지 않은 판이 조용히 돌아갑니다.
 */
export function loadRun(): SavedRun | undefined {
  try {
    const raw = localStorage.getItem(KEY)
    if (raw === null) return undefined
    const found = JSON.parse(raw) as Partial<SavedRun>
    if (found.version !== VERSION) return undefined
    if (typeof found.seed !== 'string' || found.seed === '') return undefined
    if (typeof found.deckId !== 'string' || typeof found.stake !== 'string') return undefined
    if (typeof found.hash !== 'string') return undefined
    if (!Array.isArray(found.actions)) return undefined
    if (found.phase === 'lost' || found.phase === 'won') return undefined
    return {
      version: VERSION,
      seed: found.seed,
      deckId: found.deckId,
      stake: found.stake,
      pool: found.pool === 'all' ? 'all' : 'base',
      challengeId: typeof found.challengeId === 'string' ? found.challengeId : '',
      actions: found.actions as Action[],
      hash: found.hash,
      ...(found.ranked ? { ranked: found.ranked } : {}),
      savedAt: typeof found.savedAt === 'number' ? found.savedAt : 0,
      ante: typeof found.ante === 'number' ? found.ante : 1,
      money: typeof found.money === 'number' ? found.money : 0,
      jokers: typeof found.jokers === 'number' ? found.jokers : 0,
      phase: typeof found.phase === 'string' ? found.phase : 'blind-select',
    }
  } catch {
    // 읽지 못하는 저장은 없는 것으로 봅니다.
    return undefined
  }
}

export function clearRun(): void {
  try {
    localStorage.removeItem(KEY)
  } catch {
    // 지우지 못해도 다음 저장이 덮어씁니다.
  }
}
