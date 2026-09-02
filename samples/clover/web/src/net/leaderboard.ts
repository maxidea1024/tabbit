// 리더보드의 통신.
//
// **계정 위에 얹힙니다.** 세션과 로그인은 [`session.ts`](session.ts) 의 것이고, 여기는
// 그것이 이미 있다고 보고 보드와 제출만 다룹니다 — 의존이 한 방향이어야 계정이 다른
// 기능에도 쓰입니다.

import { ApiError, call, loggedIn } from './session'

/** 아직 보내지 못한 제출. 다음 부팅에서 다시 보냅니다. */
const PENDING = 'clover.pending'

export interface RankRow {
  rank: number
  accountId: number
  handle: string
  tier: string
  value: number
}

export interface BoardInfo {
  boardId: string
  name: string
  metric: string
  pool: 'base' | 'all'
  split: string
  splitRef: string
  group: string
  sortOrder: number
  smallerIsBetter: boolean
}

export interface BoardPage {
  boardId: string
  metric: string
  smallerIsBetter: boolean
  period: 'season' | 'all'
  total: number
  from: number
  rows: RankRow[]
  me: { rank: number; value: number } | null
}

export interface Verdict {
  submissionId: number
  status: 'pending' | 'accepted' | 'rejected'
  reason: string
  metrics: Record<string, number | boolean> | null
}

export async function boards(): Promise<BoardInfo[]> {
  return (await call<{ boards: BoardInfo[] }>('/boards')).boards
}

export function boardPage(boardId: string,
                          options: { period?: 'season' | 'all'; page?: number
                                     around?: 'me' } = {}): Promise<BoardPage> {
  const query = new URLSearchParams()
  if (options.period) query.set('period', options.period)
  if (options.page !== undefined) query.set('page', String(options.page))
  if (options.around) query.set('around', options.around)
  const tail = query.toString()
  return call<BoardPage>(`/boards/${boardId}${tail === '' ? '' : `?${tail}`}`)
}


// ---------------------------------------------------------------------------
// 랭크 런
// ---------------------------------------------------------------------------

export interface RankedSeed {
  seed: string
  deck: string
  stake: string
  pool: string
  challenge: string
  expiresAt: string
}

export function rankedSeed(options: { deck?: string; stake?: string; pool?: string
                                      challenge?: string }): Promise<RankedSeed> {
  return call<RankedSeed>('/ranked/seed', {
    method: 'POST',
    body: JSON.stringify(options),
  })
}

export interface Submission {
  seed: string
  deck: string
  stake: string
  pool: string
  challenge: string
  actions: unknown[]
  fingerprint: string
  claimed?: Record<string, number | boolean>
}

/** 판정을 기다리는 동안의 간격과 횟수. 재현이 0.1초이므로 대개 첫 조회에서 끝나 있습니다. */
const JUDGE_TRIES = 12
const JUDGE_GAP_MS = 400

export async function submitRun(run: Submission): Promise<Verdict> {
  const posted = await call<{ submissionId: number }>('/runs', {
    method: 'POST',
    body: JSON.stringify(run),
  })

  // **재현은 worker 가 합니다.** 큐에 적체가 있으면 `pending` 으로 돌아오므로, 몇 번
  // 되물어봅니다 — 한 번만 보고 끝내면 붐비는 시각에 늘 「세는 중」으로 남습니다.
  let seen = await call<Verdict>(`/runs/${posted.submissionId}`)
  for (let tries = 0; tries < JUDGE_TRIES && seen.status === 'pending'; tries++) {
    await new Promise(done => setTimeout(done, JUDGE_GAP_MS))
    seen = await call<Verdict>(`/runs/${posted.submissionId}`)
  }
  return seen
}

/**
 * 보내지 못한 것을 남겨 둡니다.
 *
 * **시드의 24시간 안이면 다음 부팅에서 받습니다.** 서버가 잠시 없었다고 한 판이 없어지지
 * 않아야 합니다.
 */
export function keepPending(run: Submission): void {
  try {
    const kept = pending()
    kept.push(run)
    localStorage.setItem(PENDING, JSON.stringify(kept.slice(-8)))
  } catch {
    // 저장하지 못하면 이번 판만 잃습니다.
  }
}

export function pending(): Submission[] {
  try {
    const raw = localStorage.getItem(PENDING)
    if (raw === null) return []
    const kept = JSON.parse(raw) as unknown
    return Array.isArray(kept) ? kept as Submission[] : []
  } catch {
    return []
  }
}

/** 남아 있던 것을 보냅니다. 받아들여진 것과 영영 거절된 것을 지웁니다. */
export async function flushPending(): Promise<number> {
  const kept = pending()
  if (kept.length === 0 || !loggedIn()) return 0

  const left: Submission[] = []
  let sent = 0
  for (const run of kept) {
    try {
      await submitRun(run)
      sent++
    } catch (error) {
      // **다시 보내도 안 되는 것은 버립니다.** 시드가 만료되었거나 클라이언트가 낡은
      // 것이고, 둘 다 기다린다고 달라지지 않습니다.
      const kind = error instanceof ApiError ? error.kind : 'unknown'
      if (kind === 'offline' || kind === 'too_many') left.push(run)
    }
  }

  try {
    if (left.length === 0) localStorage.removeItem(PENDING)
    else localStorage.setItem(PENDING, JSON.stringify(left))
  } catch {
    // 지우지 못해도 다음에 다시 시도합니다.
  }
  return sent
}

/** 이 빌드의 규칙 지문. **없으면 랭크 런을 시작하지 않습니다.** */
let fingerprintCache: string | undefined

export async function fingerprint(): Promise<string> {
  if (fingerprintCache !== undefined) return fingerprintCache
  try {
    const response = await fetch('data/version.json')
    const body = await response.json() as { fingerprint?: string }
    fingerprintCache = body.fingerprint ?? ''
  } catch {
    fingerprintCache = ''
  }
  return fingerprintCache
}
