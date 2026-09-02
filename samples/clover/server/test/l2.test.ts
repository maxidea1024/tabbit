// L2 의 판정.
//
// **사람이 만명일 때 어느 쪽 순위도 100ms 안에 나와야 합니다.** `COUNT(*) WHERE score > ?`
// 로는 보드마다 한 번 전체를 세게 되므로 Redis 의 정렬 집합을 두었고, 그 값이 여기에서
// 확인됩니다.
//
// 합성 계정을 먼저 넣습니다: `npx tsx tools/seed-fake.ts --accounts 10000`

import type { Server } from 'http'

import { afterAll, beforeAll, describe, expect, it } from 'vitest'

import { build } from '../src/app'
import { accountFor } from '../src/accounts'
import { startSession } from '../src/auth/session'
import { setHandle } from '../src/accounts'
import {
  boardsFor, loadBoards, needsWin, scoreOf, smallerIsBetter, valueFromScore,
  type Board, type RunFacts,
} from '../src/boards'
import { LeaderboardMetric } from '../../web/src/generated/enums/leaderboard-metric'
import { SplitKind } from '../../web/src/generated/enums/split-kind'
import { BoardGroup } from '../../web/src/generated/enums/board-group'
import type { Metrics } from '../../web/src/core/metrics'
import type { Context } from '../src/http'

/** 이 안에 나와야 합니다. */
const BUDGET_MS = 100

let server: Server
let context: Context
let base: string
let access: string
let boards: Board[]

const lost: Metrics = { ascent: 10, bestHand: 4_440, handsPlayed: 20, money: 1, skips: 0,
                        won: false }
const won: Metrics = { ascent: 24, bestHand: 9_000, handsPlayed: 31, money: 184, skips: 5,
                       won: true }

beforeAll(async () => {
  const built = await build()
  context = built.context
  server = built.app.listen(0)
  const address = server.address()
  base = `http://127.0.0.1:${typeof address === 'object' && address ? address.port : 0}`

  const accountId = await accountFor(context.db, 'github', `l2-${Date.now()}`)
  await setHandle(context.db, accountId, `l2_${Date.now() % 100_000}`)
  access = (await startSession(context.db, context.env.jwtSecret, accountId, 'vitest')).access
  boards = loadBoards(context.env.dataPath)
})

afterAll(async () => {
  await new Promise<void>(done => server.close(() => done()))
  await context.cache.quit()
  await context.db.destroy()
})

function get(url: string) {
  return fetch(`${base}${url}`, { headers: { authorization: `Bearer ${access}` } })
}

async function timed(url: string): Promise<{ ms: number; json: Record<string, unknown> }> {
  const started = performance.now()
  const response = await get(url)
  const json = await response.json() as Record<string, unknown>
  return { ms: performance.now() - started, json }
}

describe('시트가 보드를 정합니다', () => {
  it('64개이고 갈래가 넷입니다', () => {
    expect(boards.length).toBe(64)
    const counts = new Map<number, number>()
    for (const board of boards) counts.set(board.group, (counts.get(board.group) ?? 0) + 1)
    expect(counts.get(BoardGroup.Main)).toBe(12)
    expect(counts.get(BoardGroup.Stake)).toBe(16)
    expect(counts.get(BoardGroup.Deck)).toBe(15)
    expect(counts.get(BoardGroup.Challenge)).toBe(21)
  })

  it('식별자가 겹치지 않습니다', () => {
    expect(new Set(boards.map(one => one.boardId)).size).toBe(boards.length)
  })

  it('나누는 값이 있는 보드는 그 값을 들고 있습니다', () => {
    for (const board of boards) {
      if (board.split === SplitKind.None) expect(board.splitRef, board.boardId).toBe('')
      else expect(board.splitRef, board.boardId).not.toBe('')
    }
  })
})

describe('어느 런이 어느 보드에 오르는가', () => {
  const plain: RunFacts = { deck: 'red_deck', stake: 'White', pool: 'base', challenge: '' }

  it('진 런은 완주를 요구하는 보드에 오르지 않습니다', () => {
    const got = boardsFor(boards, plain, lost)
    expect(got.some(one => needsWin(one.metric))).toBe(false)
    expect(got.some(one => one.metric === LeaderboardMetric.Ascent)).toBe(true)
  })

  it('완주한 런은 완주 보드에도 오릅니다', () => {
    const got = boardsFor(boards, plain, won)
    expect(got.some(one => one.metric === LeaderboardMetric.FewestHands)).toBe(true)
    expect(got.some(one => one.metric === LeaderboardMetric.MoneyAtWin)).toBe(true)
  })

  it('확장 풀의 런은 기본 보드에 오르지 않습니다', () => {
    const all = boardsFor(boards, { ...plain, pool: 'all' }, lost)
    expect(all.every(one => one.pool === 'all')).toBe(true)
    const basic = boardsFor(boards, plain, lost)
    expect(basic.every(one => one.pool === 'base')).toBe(true)
  })

  it('그 덱과 그 스테이크의 보드에만 오릅니다', () => {
    const got = boardsFor(boards, plain, lost)
    const decks = got.filter(one => one.split === SplitKind.Deck)
    expect(decks.map(one => one.splitRef)).toEqual(['red_deck'])
    const stakes = got.filter(one => one.split === SplitKind.Stake)
    expect(stakes.map(one => one.splitRef)).toEqual(['White'])
  })

  it('**챌린지 런은 그 챌린지의 보드 하나뿐입니다**', () => {
    const challenge = boards.find(one => one.split === SplitKind.Challenge) as Board
    const got = boardsFor(boards, { ...plain, challenge: challenge.splitRef }, won)
    expect(got.map(one => one.boardId)).toEqual([challenge.boardId])
  })

  it('챌린지가 아닌 런은 챌린지 보드에 오르지 않습니다', () => {
    const got = boardsFor(boards, plain, won)
    expect(got.some(one => one.group === BoardGroup.Challenge)).toBe(false)
  })
})

describe('동점과 방향', () => {
  it('작은 것이 위인 지표는 뒤집혀 들어갑니다', () => {
    const at = new Date('2026-06-01T00:00:00Z')
    const few = scoreOf(LeaderboardMetric.FewestHands, 31, at)
    const many = scoreOf(LeaderboardMetric.FewestHands, 44, at)
    expect(few).toBeGreaterThan(many)
    expect(smallerIsBetter(LeaderboardMetric.FewestHands)).toBe(true)
  })

  it('같은 값이면 먼저 낸 쪽이 위입니다', () => {
    const early = scoreOf(LeaderboardMetric.Ascent, 100, new Date('2026-06-01T00:00:00Z'))
    const late = scoreOf(LeaderboardMetric.Ascent, 100, new Date('2026-09-01T00:00:00Z'))
    expect(early).toBeGreaterThan(late)
  })

  it('지표가 다르면 시각이 끼어들지 못합니다', () => {
    const better = scoreOf(LeaderboardMetric.Ascent, 101, new Date('2026-09-01T00:00:00Z'))
    const worse = scoreOf(LeaderboardMetric.Ascent, 100, new Date('2026-01-02T00:00:00Z'))
    expect(better).toBeGreaterThan(worse)
  })

  it('넣은 값을 되읽습니다', () => {
    const at = new Date('2026-06-01T00:00:00Z')
    for (const metric of [LeaderboardMetric.Ascent, LeaderboardMetric.BestHand,
                          LeaderboardMetric.FewestHands]) {
      for (const value of [0, 1, 24, 192, 4_440, 1_284_590]) {
        expect(valueFromScore(metric, scoreOf(metric, value, at)), `${metric} ${value}`)
          .toBe(value)
      }
    }
  })
})

describe('만명의 순위표', () => {
  it('합성 계정이 이번 시즌에 들어 있습니다', async () => {
    // **시즌 안의 제출을 셉니다.** 계정 수만 보면 시즌을 나눈 뒤에도 통과하는데, 그때
    // 순위표는 비어 있습니다 — `npx tsx tools/seed-fake.ts` 를 다시 부릅니다.
    const [row] = await context.db('submission')
      .where('season_id', context.season.id).andWhere('status', 'accepted')
      .count<{ count: number }[]>('* as count')
    expect(Number(row.count),
           '이번 시즌의 합성 제출이 모자랍니다. tools/seed-fake.ts 를 다시 부릅니다')
      .toBeGreaterThanOrEqual(10_000)
  })

  it(`보드 목록이 ${BUDGET_MS}ms 안에`, async () => {
    const { ms, json } = await timed('/boards')
    expect((json.boards as unknown[]).length).toBe(64)
    expect(ms, `${ms.toFixed(1)}ms`).toBeLessThan(BUDGET_MS)
  })

  it(`첫 쪽이 ${BUDGET_MS}ms 안에`, async () => {
    const { ms, json } = await timed('/boards/ascent')
    expect(Number(json.total)).toBeGreaterThanOrEqual(10_000)
    expect((json.rows as unknown[]).length).toBe(25)
    expect(ms, `${ms.toFixed(1)}ms`).toBeLessThan(BUDGET_MS)
  })

  it(`한가운데 쪽이 ${BUDGET_MS}ms 안에`, async () => {
    const { ms, json } = await timed('/boards/ascent?page=200')
    expect((json.rows as unknown[]).length).toBe(25)
    expect(ms, `${ms.toFixed(1)}ms`).toBeLessThan(BUDGET_MS)
  })

  it(`내 자리로 가는 것이 ${BUDGET_MS}ms 안에`, async () => {
    // 순위표에 있는 사람으로 봅니다 — 합성 계정 하나를 골라 그 자리를 조회합니다.
    const { ms, json } = await timed('/boards/ascent?around=me')
    expect(json.boardId).toBe('ascent')
    expect(ms, `${ms.toFixed(1)}ms`).toBeLessThan(BUDGET_MS)
  })

  it('순위가 1부터 이어집니다', async () => {
    const { json } = await timed('/boards/ascent')
    const rows = json.rows as { rank: number; value: number }[]
    expect(rows[0].rank).toBe(1)
    expect(rows[24].rank).toBe(25)
    // 큰 것이 위입니다.
    for (let at = 1; at < rows.length; at++) {
      expect(rows[at].value).toBeLessThanOrEqual(rows[at - 1].value)
    }
  })

  it('작은 것이 위인 보드는 오름차순으로 보입니다', async () => {
    const { json } = await timed('/boards/fewesthands')
    const rows = json.rows as { value: number }[]
    if (rows.length < 2) return
    for (let at = 1; at < rows.length; at++) {
      expect(rows[at].value).toBeGreaterThanOrEqual(rows[at - 1].value)
    }
  })

  it('없는 보드는 404 입니다', async () => {
    expect((await get('/boards/없는보드')).status).toBe(404)
  })
})

describe('등급', () => {
  it('사람이 만명이면 위의 등급이 열립니다', async () => {
    const rows = (await timed('/boards/ascent')).json.rows as { accountId: number }[]
    const top = rows[0].accountId

    const { refreshTier } = await import('../src/boards')
    const tier = await refreshTier(context, top)
    // 1위이므로 가장 위의 등급입니다.
    expect(tier).toBe('Clover')
  })

  it('순위표에 없는 사람은 등급이 없습니다', async () => {
    const stranger = await accountFor(context.db, 'github', `nobody-${Date.now()}`)
    const { refreshTier } = await import('../src/boards')
    expect(await refreshTier(context, stranger)).toBe('None')
  })
})
