// L1 의 판정.
//
// **구워 둔 리플레이 13개를 API 로 넣어 전부 `accepted` 여야 합니다.** 그리고 서버가 센
// 지표가 골든에 적힌 것과 같아야 합니다 — 클라이언트가 보낸 숫자를 쓰지 않는다는 것의
// 증거가 그것입니다.
//
// PostgreSQL 과 Redis 가 떠 있어야 합니다: `docker compose up -d postgres redis`

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import type { Server } from 'http'

import { afterAll, beforeAll, describe, expect, it } from 'vitest'

import { build } from '../src/app'
import { accountFor } from '../src/accounts'
import { startSession } from '../src/auth/session'
import { judgeOne } from '../src/worker'
import type { Context } from '../src/http'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPLAYS = path.resolve(HERE, '../../design-data/out/replay')

interface Baked {
  seed: string
  deck: string
  stake: string
  challenge?: string
  actions: unknown[]
  metrics: {
    ascent: number; bestHand: number; handsPlayed: number
    money: number; skips: number; won: boolean
  }
}

const names = fs.readdirSync(REPLAYS).filter(one => one.endsWith('.json')).sort()
const baked = names.map(name =>
  JSON.parse(fs.readFileSync(path.join(REPLAYS, name), 'utf8')) as Baked)

let server: Server
let context: Context
let base: string
let access: string
let accountId: number

beforeAll(async () => {
  const built = await build()
  context = built.context
  server = built.app.listen(0)
  const address = server.address()
  base = `http://127.0.0.1:${typeof address === 'object' && address ? address.port : 0}`

  // **OAuth 를 지나지 않고 계정을 만듭니다.** 제공자의 열쇠가 없어도 그 뒤의 전부를
  // 확인할 수 있어야 합니다 — 로그인의 흐름은 제공자마다 다르고 그 뒤는 하나입니다.
  accountId = await accountFor(context.db, 'github', `test-${Date.now()}`)
  access = (await startSession(context.db, context.env.jwtSecret, accountId, 'vitest')).access
})

afterAll(async () => {
  await new Promise<void>(done => server.close(() => done()))
  await context.cache.quit()
  await context.db.destroy()
})

function post(url: string, body: unknown, token = access) {
  return fetch(`${base}${url}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
    body: JSON.stringify(body),
  })
}

/** `Response.json()` 이 `unknown` 을 돌려줍니다. 시험에서 매번 좁히지 않으려고 여기 둡니다. */
async function body<T>(response: Response): Promise<T> {
  return await response.json() as T
}

function get(url: string, token = access) {
  return fetch(`${base}${url}`, { headers: { authorization: `Bearer ${token}` } })
}

/** 서버가 그 시드를 발급한 것으로 둡니다. 구워 둔 리플레이는 자기 시드로만 재현됩니다. */
async function issue(replay: Baked): Promise<void> {
  await context.db('ranked_seed').insert({
    seed: replay.seed,
    account_id: accountId,
    deck: replay.deck,
    stake: replay.stake,
    pool: 'base',
    challenge: replay.challenge ?? '',
    expires_at: new Date(Date.now() + 3_600_000),
    used_at: null,
    // **모든 칸을 덮습니다.** `used_at` 만 되돌리면 앞선 실행의 계정이 그대로 남고, 그러면
    // 시드가 남의 것이 되어 전부 `bad_seed` 입니다.
  }).onConflict('seed').merge()
}

function submissionOf(replay: Baked) {
  return {
    seed: replay.seed,
    deck: replay.deck,
    stake: replay.stake,
    pool: 'base',
    challenge: replay.challenge ?? '',
    actions: replay.actions,
    fingerprint: context.fingerprint,
  }
}

describe('부팅', () => {
  it('지문과 시즌과 제공자를 알립니다', async () => {
    const health = await body<{ fingerprint: string; season: number; providers: string[] }>(
      await fetch(`${base}/health`))
    expect(health.fingerprint).toBe(context.fingerprint)
    expect(health.season).toBeGreaterThan(0)
    expect(health.providers).toContain('github')
  })

  it('로그인 없이는 제출할 수 없습니다', async () => {
    const response = await fetch(`${base}/runs`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({}),
    })
    expect(response.status).toBe(401)
  })
})

describe('구워 둔 리플레이 13개', () => {
  it('13개입니다', () => {
    expect(baked.length).toBe(13)
  })

  for (let index = 0; index < baked.length; index++) {
    it(`${names[index]} 가 accepted 이고 지표가 골든과 같습니다`, async () => {
      const replay = baked[index]
      await issue(replay)

      const response = await post('/runs', submissionOf(replay))
      // 몸통을 한 번만 읽습니다 — 실패한 자리에 무엇이 왔는지도 이 글에서 나옵니다.
      const said = await response.text()
      expect(response.status, said).toBe(202)
      const { submissionId } = JSON.parse(said) as { submissionId: number }

      const verdict = await judgeOne(context.db, context.cache, context.env.dataPath,
                                     submissionId, context)
      expect(verdict).toBe('accepted')

      const got = await body<{ status: string; metrics: Record<string, unknown> }>(
        await get(`/runs/${submissionId}`))

      expect(got.status).toBe('accepted')
      // **서버가 센 것입니다.** 클라이언트는 지표를 보내지도 않았습니다.
      expect(got.metrics.ascent).toBe(replay.metrics.ascent)
      expect(Number(got.metrics.best_hand)).toBe(replay.metrics.bestHand)
      expect(got.metrics.hands_played).toBe(replay.metrics.handsPlayed)
      expect(got.metrics.money).toBe(replay.metrics.money)
      expect(got.metrics.skips).toBe(replay.metrics.skips)
      expect(got.metrics.won).toBe(replay.metrics.won)
    })
  }
})

describe('거절', () => {
  it('시드를 두 번 쓰지 못합니다', async () => {
    const replay = baked[0]
    await issue(replay)
    expect((await post('/runs', submissionOf(replay))).status).toBe(202)

    const again = await post('/runs', submissionOf(replay))
    expect(again.status).toBe(409)
    expect((await body<{ error: string }>(again)).error).toBe('bad_seed')
  })

  it('발급받지 않은 시드는 오르지 않습니다', async () => {
    const response = await post('/runs',
      { ...submissionOf(baked[0]), seed: 'CLOVER-NOTMINE' })
    expect(response.status).toBe(409)
    expect((await body<{ error: string }>(response)).error).toBe('bad_seed')
  })

  it('덱을 바꾸면 그 시드의 런이 아닙니다', async () => {
    const replay = baked[0]
    await issue(replay)
    const response = await post('/runs', { ...submissionOf(replay), deck: 'blue_deck' })
    expect(response.status).toBe(409)
    expect((await body<{ error: string }>(response)).error).toBe('bad_seed')
  })

  it('지문이 다르면 큐에 들어가지 않습니다', async () => {
    const replay = baked[0]
    await issue(replay)
    const response = await post('/runs', { ...submissionOf(replay), fingerprint: 'deadbeef' })
    expect(response.status).toBe(409)
    expect((await body<{ error: string }>(response)).error).toBe('bad_fingerprint')
  })

  it('끝나지 않은 런은 거절됩니다', async () => {
    const replay = baked[0]
    await issue(replay)
    const cut = { ...submissionOf(replay), actions: replay.actions.slice(0, 5) }

    const response = await post('/runs', cut)
    expect(response.status).toBe(202)
    const { submissionId } = await body<{ submissionId: number }>(response)

    const verdict = await judgeOne(context.db, context.cache, context.env.dataPath,
                                   submissionId)
    expect(verdict).toBe('rejected:unfinished')
  })

  it('클라이언트가 부른 지표를 쓰지 않습니다', async () => {
    // **여기가 이 설계의 값입니다.** 리플레이는 진짜이고 부른 값만 부풀렸습니다 — 서버는
    // 부른 값을 보지 않고 자기가 센 것을 올립니다.
    const replay = baked[1]
    await issue(replay)

    const response = await post('/runs', {
      ...submissionOf(replay),
      claimed: { ascent: 999, bestHand: 999_999_999, handsPlayed: 1, money: 9_999,
                 skips: 0, won: true },
    })
    expect(response.status).toBe(202)
    const { submissionId } = await body<{ submissionId: number }>(response)

    expect(await judgeOne(context.db, context.cache, context.env.dataPath, submissionId,
                          context)).toBe('accepted')

    const got = await body<{ metrics: Record<string, unknown> }>(
      await get(`/runs/${submissionId}`))
    expect(got.metrics.ascent).toBe(replay.metrics.ascent)
    expect(Number(got.metrics.best_hand)).toBe(replay.metrics.bestHand)
    expect(got.metrics.won).toBe(replay.metrics.won)
  })

  it('액션에 손을 대면 다른 런이 됩니다', async () => {
    // **코어는 규칙을 어긴 액션을 무시합니다.** 그래서 손댄 리플레이가 예외로 잡히는 것이
    // 아니라 그냥 다른 런이 되고, 그 결과가 그대로 올라갑니다 — 손대서 얻을 것이 없다는
    // 것이 이 설계의 값입니다. 해시가 달라지는 것으로 확인합니다.
    const replay = baked[0]
    await issue(replay)

    const cut = replay.actions.slice(0, replay.actions.length - 1)
    const response = await post('/runs', { ...submissionOf(replay), actions: cut })
    expect(response.status).toBe(202)
    const { submissionId } = await body<{ submissionId: number }>(response)

    const verdict = await judgeOne(context.db, context.cache, context.env.dataPath,
                                   submissionId)
    // 마지막 한 수가 빠지면 그 런은 끝나지 않은 런입니다.
    expect(verdict).toBe('rejected:unfinished')
  })
})

describe('한 계정 · 여러 기계', () => {
  it('기계마다 세션이 따로 있고 하나를 끊어도 나머지가 남습니다', async () => {
    const one = await startSession(context.db, context.env.jwtSecret, accountId, '기계 하나')
    const two = await startSession(context.db, context.env.jwtSecret, accountId, '기계 둘')

    const before = await body<{ devices: { label: string }[] }>(await get('/me', one.access))
    expect(before.devices.length).toBeGreaterThanOrEqual(2)

    expect((await post('/auth/logout', {}, one.access)).status).toBe(200)

    // 둘째 기계는 그대로입니다.
    const after = await get('/me', two.access)
    expect(after.status).toBe(200)
    const labels = (await body<{ devices: { label: string }[] }>(after))
      .devices.map(row => row.label)
    expect(labels).toContain('기계 둘')
    expect(labels).not.toContain('기계 하나')
  })

  it('refresh 는 한 번 쓰면 바뀌고, 바로 앞의 것도 잠시 받습니다', async () => {
    const first = await startSession(context.db, context.env.jwtSecret, accountId, '바꾸기')

    const rotated = await body<{ refresh: string }>(
      await post('/auth/refresh', { refresh: first.refresh }))
    expect(rotated.refresh).not.toBe(first.refresh)

    // **유예 안에서는 예전 것도 받습니다** — 응답을 받지 못한 기계의 재시도입니다.
    const retry = await post('/auth/refresh', { refresh: first.refresh })
    expect(retry.status).toBe(200)
    expect((await body<{ refresh: string }>(retry)).refresh).toBe(first.refresh)
  })
})
