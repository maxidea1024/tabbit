// 보드와 순위.
//
// **어느 보드가 있는가는 시트가 정합니다.** 서버는 그 행을 읽어 Redis 에 정렬 집합을
// 마련하기만 합니다 — 보드 하나를 더하는 데 이 파일이 바뀌지 않습니다.
//
// **Redis 는 캐시입니다.** 비어도 `rebuild` 가 PostgreSQL 에서 전부 다시 만듭니다.

import { Router } from 'express'
import { BoardGroup } from '../../web/src/generated/enums/board-group'
import { LeaderboardMetric } from '../../web/src/generated/enums/leaderboard-metric'
import { PoolChoice as PoolChoiceEnum } from '../../web/src/generated/enums/pool-choice'
import { SplitKind } from '../../web/src/generated/enums/split-kind'
import { TierKind } from '../../web/src/generated/enums/tier-kind'
import type { LeaderboardRecord } from '../../web/src/generated/tables/leaderboard'
import type { Metrics } from '../../web/src/core/metrics'
import { loadData, type PoolChoice } from './core'
import { KEY, type Cache } from './redis'
import { fail, guard, requireLogin, type Context } from './http'
import type { Db } from './db'

/** 한 쪽에 몇 행인가. */
export const PAGE_SIZE = 25

/**
 * 동점을 가르는 시각의 폭.
 *
 * **먼저 낸 쪽이 위입니다.** 지표의 정수부 뒤 소수부에 시각을 담으므로, 지표가 클수록
 * 그 분해능이 거칠어집니다 — 지금 값(한 손 최고가 10^5 대)에서는 1초보다 곱습니다.
 * 지표가 10^9 을 넘으면 몇십 초 안의 제출이 같은 자리가 되고, 그때는 자리를 넓힙니다.
 */
const TIE_SPAN_SECONDS = 10 * 365 * 24 * 3_600

/** 시각을 담기 시작하는 때. 2026-01-01. */
const TIE_EPOCH_MS = Date.UTC(2026, 0, 1)

/** 소수부가 가질 수 있는 가장 큰 값. 1 이 되면 정수부로 올라갑니다. */
const LARGEST_FRACTION = 0.999_999_999

export interface Board {
  boardId: string
  metric: LeaderboardMetric
  pool: PoolChoice
  split: SplitKind
  splitRef: string
  group: BoardGroup
  sortOrder: number
  name: string
}

/** 이 지표는 작은 것이 위인가. */
export function smallerIsBetter(metric: LeaderboardMetric): boolean {
  return metric === LeaderboardMetric.FewestHands
}

/** 이 지표는 완주한 런만 올라가는가. */
export function needsWin(metric: LeaderboardMetric): boolean {
  return metric === LeaderboardMetric.FewestHands
    || metric === LeaderboardMetric.MoneyAtWin
    || metric === LeaderboardMetric.Skips
}

/** 이 지표는 런 하나가 아니라 계정의 값인가. */
export function isAccountMetric(metric: LeaderboardMetric): boolean {
  return metric === LeaderboardMetric.Wins || metric === LeaderboardMetric.ChallengesBeaten
}

export function loadBoards(dataPath: string): Board[] {
  return loadData(dataPath).tables.leaderboard.records.map((row: LeaderboardRecord) => ({
    boardId: row.boardId,
    metric: row.metric,
    pool: row.pool === PoolChoiceEnum.All ? 'all' : 'base',
    split: row.split,
    splitRef: row.hasSplitRef ? row.splitRef : '',
    group: row.group,
    sortOrder: row.sortOrder,
    name: row.name,
  }))
}

export interface RunFacts {
  deck: string
  stake: string
  pool: PoolChoice
  challenge: string
}

/**
 * 이 런이 올라가는 보드들.
 *
 * **챌린지 런은 챌린지 보드에만 오릅니다.** 시작 조건이 다른 것을 같은 표에 두지 않습니다 —
 * 원작이 챌린지에서 해금을 올리지 않는 것과 같은 이유입니다.
 */
export function boardsFor(boards: Board[], run: RunFacts, metrics: Metrics): Board[] {
  const challenged = run.challenge !== ''

  return boards.filter(board => {
    if (isAccountMetric(board.metric)) return false
    if (board.pool !== run.pool) return false
    if (needsWin(board.metric) && !metrics.won) return false

    if (challenged) {
      // 챌린지 런은 그 챌린지의 보드 하나뿐입니다.
      return board.split === SplitKind.Challenge && board.splitRef === run.challenge
    }
    if (board.group === BoardGroup.Challenge) return false

    switch (board.split) {
      case SplitKind.None: return true
      case SplitKind.Stake: return board.splitRef === run.stake
      case SplitKind.Deck: return board.splitRef === run.deck
      default: return false
    }
  })
}

/** 이 지표의 값. */
export function valueOf(metric: LeaderboardMetric, metrics: Metrics): number {
  switch (metric) {
    case LeaderboardMetric.Ascent: return metrics.ascent
    case LeaderboardMetric.BestHand: return metrics.bestHand
    case LeaderboardMetric.FewestHands: return metrics.handsPlayed
    case LeaderboardMetric.MoneyAtWin: return metrics.money
    case LeaderboardMetric.Skips: return metrics.skips
    default: return 0
  }
}

/**
 * ZSET 에 넣을 수.
 *
 * **큰 것이 위가 되도록 맞춥니다.** 작은 것이 위인 지표는 부호를 뒤집고, 소수부에 시각을
 * 담아 같은 값이면 먼저 낸 쪽이 위가 되게 합니다.
 */
export function scoreOf(metric: LeaderboardMetric, value: number, at: Date): number {
  const elapsed = Math.max(0, (at.getTime() - TIE_EPOCH_MS) / 1_000)
  // **1 이 되지 않게 눌러 둡니다.** 소수부가 정확히 1 이면 정수부로 올라가고, 그러면
  // 되읽을 때 값이 하나 어긋납니다 — 시각이 정확히 시작점일 때 그렇습니다.
  const earlier = Math.max(0, Math.min(1 - elapsed / TIE_SPAN_SECONDS, LARGEST_FRACTION))
  return (smallerIsBetter(metric) ? -value : value) + earlier
}

/**
 * 그 수에서 지표를 되읽습니다.
 *
 * **소수부가 시각이므로 내림입니다.** 뒤집어 넣은 지표는 `-31 + 0.9` 가 `-30.1` 이고 그
 * 내림이 `-31` 이므로, 부호만 되돌리면 값이 그대로 나옵니다.
 */
export function valueFromScore(metric: LeaderboardMetric, score: number): number {
  const whole = Math.floor(score)
  if (!smallerIsBetter(metric)) return whole
  // **`-0` 을 내보내지 않습니다.** 같은 수이지만 JSON 에 `-0` 으로 적히고, 화면이 그것을
  // 그대로 그립니다.
  return whole === 0 ? 0 : -whole
}

/**
 * 받아들여진 제출 하나를 보드에 올립니다.
 *
 * **한 사람이 한 보드에 한 자리입니다.** 더 나을 때만 바뀌므로 한 사람이 10위부터 20위까지를
 * 차지하지 않습니다.
 */
export async function publish(context: Context, accountId: number, run: RunFacts,
                              metrics: Metrics, at: Date): Promise<string[]> {
  const boards = loadBoards(context.env.dataPath)
  const moved: string[] = []

  for (const board of boardsFor(boards, run, metrics)) {
    const score = scoreOf(board.metric, valueOf(board.metric, metrics), at)
    // `GT` 는 지금 것보다 클 때만 바꿉니다. 부호를 이미 맞추었으므로 「더 나을 때만」입니다.
    for (const key of [KEY.board(context.season.id, board.boardId),
                       KEY.board('all', board.boardId)]) {
      await context.cache.zadd(key, 'GT', 'CH', score, String(accountId))
    }
    moved.push(board.boardId)
  }

  await publishAccountMetrics(context, accountId, at)
  return moved
}

/** 계정의 값인 지표들. 제출이 받아들여질 때마다 다시 셉니다. */
export async function publishAccountMetrics(context: Context, accountId: number,
                                            at: Date): Promise<void> {
  const boards = loadBoards(context.env.dataPath).filter(one => isAccountMetric(one.metric))
  if (boards.length === 0) return

  const wins = await context.db('submission')
    .join('run_metric', 'run_metric.submission_id', 'submission.id')
    .where('submission.account_id', accountId)
    .andWhere('submission.season_id', context.season.id)
    .andWhere('run_metric.won', true)
    .count<{ count: number }[]>('* as count')

  const beaten = await context.db('submission')
    .join('run_metric', 'run_metric.submission_id', 'submission.id')
    .where('submission.account_id', accountId)
    .andWhere('submission.season_id', context.season.id)
    .andWhere('run_metric.won', true)
    .andWhere('submission.challenge', '<>', '')
    .countDistinct<{ count: number }[]>('submission.challenge as count')

  for (const board of boards) {
    const value = board.metric === LeaderboardMetric.Wins
      ? Number(wins[0]?.count ?? 0)
      : Number(beaten[0]?.count ?? 0)
    if (value === 0) continue
    const score = scoreOf(board.metric, value, at)
    for (const key of [KEY.board(context.season.id, board.boardId),
                       KEY.board('all', board.boardId)]) {
      await context.cache.zadd(key, 'GT', 'CH', score, String(accountId))
    }
  }
}

export interface RankRow {
  rank: number
  accountId: number
  handle: string
  tier: string
  value: number
}

/** 순위표 한 쪽. */
export async function pageOf(context: Context, board: Board, season: number | 'all',
                             from: number, count: number): Promise<RankRow[]> {
  const key = KEY.board(season, board.boardId)
  const raw = await context.cache.zrevrange(key, from, from + count - 1, 'WITHSCORES')
  if (raw.length === 0) return []

  const ids: number[] = []
  const scores: number[] = []
  for (let at = 0; at < raw.length; at += 2) {
    ids.push(Number(raw[at]))
    scores.push(Number(raw[at + 1]))
  }

  const profiles = await context.db('profile').whereIn('account_id', ids)
    .select<{ account_id: number; handle: string | null; tier: string }[]>(
      'account_id', 'handle', 'tier')
  const byId = new Map(profiles.map(row => [row.account_id, row]))

  return ids.map((accountId, at) => ({
    rank: from + at + 1,
    accountId,
    handle: byId.get(accountId)?.handle ?? '',
    tier: byId.get(accountId)?.tier ?? '',
    value: valueFromScore(board.metric, scores[at]),
  }))
}

/** 이 사람의 자리. 없으면 `undefined` 입니다. */
export async function rankOf(cache: Cache, boardId: string, season: number | 'all',
                             accountId: number): Promise<{ rank: number; score: number }
                             | undefined> {
  const key = KEY.board(season, boardId)
  const rank = await cache.zrevrank(key, String(accountId))
  if (rank === null) return undefined
  const score = await cache.zscore(key, String(accountId))
  return { rank: rank + 1, score: Number(score) }
}

/**
 * 시즌 등정 보드의 백분위에서 등급을 냅니다.
 *
 * **`min_players` 가 첫 주의 문제를 방지합니다.** 사람이 10명이면 1위가 가장 위의 등급이
 * 되는데, 10명 중 1위는 그 뜻이 아닙니다.
 */
export async function tierOf(context: Context, accountId: number): Promise<TierKind> {
  const data = loadData(context.env.dataPath)
  const ascent = loadBoards(context.env.dataPath).find(board =>
    board.metric === LeaderboardMetric.Ascent && board.split === SplitKind.None
    && board.pool === 'base')
  if (!ascent) return TierKind.None

  const key = KEY.board(context.season.id, ascent.boardId)
  const total = await context.cache.zcard(key)
  const mine = await context.cache.zrevrank(key, String(accountId))
  if (total === 0 || mine === null) return TierKind.None

  const percentile = ((mine + 1) / total) * 100

  // 위에서부터 봅니다. 사람이 모자란 등급은 건너뜁니다.
  const rows = [...data.tables.tier.records]
    .filter(row => row.tier !== TierKind.None)
    .sort((a, b) => a.topPercent - b.topPercent)

  for (const row of rows) {
    if (total < row.minPlayers) continue
    if (percentile <= row.topPercent) return row.tier
  }
  return TierKind.None
}

/** 이 사람의 등급을 다시 세어 프로필에 적습니다. */
export async function refreshTier(context: Context, accountId: number): Promise<string> {
  const tier = await tierOf(context, accountId)
  const name = TierKind[tier] ?? ''
  await context.db('profile').where('account_id', accountId).update({ tier: name })
  return name
}

/**
 * Redis 를 PostgreSQL 에서 다시 만듭니다.
 *
 * **부팅할 때 비어 있으면 부릅니다.** Redis 가 비는 것은 사고가 아니라 캐시의 성질입니다.
 */
export async function rebuild(context: Context): Promise<number> {
  const boards = loadBoards(context.env.dataPath)
  const rows = await context.db('submission')
    .join('run_metric', 'run_metric.submission_id', 'submission.id')
    .where('submission.status', 'accepted')
    .orderBy('submission.submitted_at', 'asc')
    .select<{
      account_id: number; season_id: number; deck: string; stake: string; pool: string
      challenge: string; submitted_at: Date; ascent: number; best_hand: number
      hands_played: number; money: number; skips: number; won: boolean
    }[]>('submission.account_id', 'submission.season_id', 'submission.deck',
          'submission.stake', 'submission.pool', 'submission.challenge',
          'submission.submitted_at', 'run_metric.ascent', 'run_metric.best_hand',
          'run_metric.hands_played', 'run_metric.money', 'run_metric.skips', 'run_metric.won')

  // **한 번에 보냅니다.** 제출 하나가 보드 대여섯에 오르므로, 왕복으로 하면 10만 번입니다.
  const pipeline = context.cache.pipeline()
  let count = 0
  for (const row of rows) {
    const metrics: Metrics = {
      ascent: row.ascent,
      bestHand: Number(row.best_hand),
      handsPlayed: row.hands_played,
      money: row.money,
      skips: row.skips,
      won: row.won,
    }
    const run: RunFacts = {
      deck: row.deck, stake: row.stake,
      pool: row.pool === 'all' ? 'all' : 'base',
      challenge: row.challenge,
    }
    for (const board of boardsFor(boards, run, metrics)) {
      const score = scoreOf(board.metric, valueOf(board.metric, metrics), row.submitted_at)
      const keys = row.season_id === context.season.id
        ? [KEY.board(row.season_id, board.boardId), KEY.board('all', board.boardId)]
        : [KEY.board('all', board.boardId)]
      for (const key of keys) {
        pipeline.zadd(key, 'GT', 'CH', score, String(row.account_id))
      }
    }
    count++
  }
  await pipeline.exec()
  return count
}

export function boardsRouter(context: Context): Router {
  const router = Router()

  const find = (boardId: string): Board | undefined =>
    loadBoards(context.env.dataPath).find(one => one.boardId === boardId)

  router.get('/boards', requireLogin(context), (_req, res) => {
    res.json({
      boards: loadBoards(context.env.dataPath).map(board => ({
        boardId: board.boardId,
        name: board.name,
        metric: LeaderboardMetric[board.metric],
        pool: board.pool,
        split: SplitKind[board.split],
        splitRef: board.splitRef,
        group: BoardGroup[board.group],
        sortOrder: board.sortOrder,
        smallerIsBetter: smallerIsBetter(board.metric),
      })),
    })
  })

  router.get('/boards/:id', requireLogin(context), guard(async (req, res) => {
    const board = find(String(req.params.id))
    if (!board) {
      fail(res, 404, 'not_found', '없는 보드입니다')
      return
    }
    const season: number | 'all' = req.query.period === 'all' ? 'all' : context.season.id
    const accountId = req.accountId as number

    const mine = await rankOf(context.cache, board.boardId, season, accountId)

    let from = Math.max(0, Number(req.query.page ?? 0)) * PAGE_SIZE
    if (req.query.around === 'me' && mine) {
      // 내 자리를 가운데로. 쪽의 경계에 맞추지 않습니다 — 맞추면 내가 맨 위나 맨 아래에
      // 붙는 쪽이 나옵니다.
      from = Math.max(0, mine.rank - 1 - Math.floor(PAGE_SIZE / 2))
    }

    const total = await context.cache.zcard(KEY.board(season, board.boardId))
    res.json({
      boardId: board.boardId,
      metric: LeaderboardMetric[board.metric],
      smallerIsBetter: smallerIsBetter(board.metric),
      period: season === 'all' ? 'all' : 'season',
      total,
      from,
      rows: await pageOf(context, board, season, from, PAGE_SIZE),
      me: mine ? { rank: mine.rank, value: valueFromScore(board.metric, mine.score) } : null,
    })
  }))

  return router
}

/**
 * 이 사람의 보드별 자리. `/me` 와 `/profiles/{handle}` 이 같은 것을 씁니다.
 *
 * **보드마다 왕복하지 않습니다.** 보드가 64개이고 자리와 값이 각각이므로 왕복이 128번이
 * 됩니다 — 부팅마다 부르는 길이라 그 값이 그대로 첫 화면의 시간입니다.
 */
export async function ranksOf(context: Context, accountId: number): Promise<{
  boardId: string; name: string; group: string; rank: number; value: number
}[]> {
  const boards = loadBoards(context.env.dataPath)
  const member = String(accountId)

  const pipeline = context.cache.pipeline()
  for (const board of boards) {
    const key = KEY.board(context.season.id, board.boardId)
    pipeline.zrevrank(key, member)
    pipeline.zscore(key, member)
  }
  const replies = await pipeline.exec() ?? []

  const out = []
  for (let at = 0; at < boards.length; at++) {
    const rank = replies[at * 2]?.[1]
    const score = replies[at * 2 + 1]?.[1]
    if (rank === null || rank === undefined || score === null || score === undefined) continue
    out.push({
      boardId: boards[at].boardId,
      name: boards[at].name,
      group: BoardGroup[boards[at].group],
      rank: Number(rank) + 1,
      value: valueFromScore(boards[at].metric, Number(score)),
    })
  }
  return out
}

/** 부팅할 때 Redis 가 비어 있으면 다시 만듭니다. */
export async function rebuildIfEmpty(context: Context, db: Db): Promise<number> {
  void db
  const boards = loadBoards(context.env.dataPath)
  for (const board of boards) {
    if (await context.cache.zcard(KEY.board(context.season.id, board.boardId)) > 0) return 0
  }
  return rebuild(context)
}
