// 제출과 조회.
//
// **점수를 받지 않고 리플레이를 받습니다.** 받는 자리는 큐에 넣기만 하고, 재현은 worker 가
// 합니다 — 재현이 0.1초라도 동시에 1,000개면 100초이고, 그동안 HTTP 를 잡아 두면 서버가
// 그 수만큼의 연결을 듭니다.

import { Router } from 'express'
import { KEY, underLimit } from './redis'
import { fail, guard, requireLogin, type Context } from './http'
import { claimSeed } from './ranked'
import { isPoolChoice } from './core'
import { MAX_ACTIONS } from './judge'

/** 한 시간에 낼 수 있는 제출 수. 시드의 한도와 나란합니다. */
const SUBMIT_LIMIT = 40

/** 본문의 상한. 구워 둔 것 중 가장 긴 것이 11KB 입니다. */
export const MAX_BODY = '256kb'

export function runsRouter(context: Context): Router {
  const router = Router()
  const { db, cache } = context

  router.post('/runs', requireLogin(context), guard(async (req, res) => {
    const accountId = req.accountId as number
    const body = req.body ?? {}

    const submitted = {
      seed: String(body.seed ?? ''),
      deck: String(body.deck ?? ''),
      stake: String(body.stake ?? ''),
      pool: String(body.pool ?? 'base'),
      challenge: String(body.challenge ?? ''),
      actions: Array.isArray(body.actions) ? body.actions : undefined,
      fingerprint: String(body.fingerprint ?? ''),
      // **순위에 쓰지 않습니다.** 서버가 센 것과 다르면 그 사실만 기록에 남습니다 —
      // 클라이언트의 `metrics.ts` 가 서버와 어긋난 것을 찾는 자리입니다.
      claimed: typeof body.claimed === 'object' && body.claimed !== null
        ? body.claimed as Record<string, unknown> : undefined,
    }

    if (!submitted.actions) {
      fail(res, 400, 'bad_body', 'actions 가 배열이 아닙니다')
      return
    }
    if (!isPoolChoice(submitted.pool)) {
      fail(res, 400, 'bad_body', '풀은 base 또는 all 입니다')
      return
    }
    if (submitted.actions.length > MAX_ACTIONS) {
      fail(res, 413, 'too_long', `액션이 ${MAX_ACTIONS}개를 넘습니다`)
      return
    }

    // **지문을 여기서 봅니다.** 재현까지 가서 거절하면 낡은 클라이언트가 큐를 채웁니다.
    if (submitted.fingerprint !== context.fingerprint) {
      const [id] = await record(context, accountId, submitted, 'rejected', 'bad_fingerprint')
      fail(res, 409, 'bad_fingerprint',
           '클라이언트가 낡았습니다. 새로 고치면 다시 올릴 수 있습니다')
      void id
      return
    }

    if (!await underLimit(cache, KEY.rate(accountId, 'submit'), SUBMIT_LIMIT, 3_600)) {
      fail(res, 429, 'too_many_runs', '제출이 너무 잦습니다')
      return
    }

    // **시드를 여기서 씁니다.** 재현보다 먼저 보는 이유는 같은 시드로 두 번 오는 것을
    // 큐에 넣기 전에 끊기 위해서입니다.
    if (!await claimSeed(context, accountId, submitted)) {
      await record(context, accountId, submitted, 'rejected', 'bad_seed')
      fail(res, 409, 'bad_seed', '이 시드로 시작한 랭크 런이 아닙니다')
      return
    }

    const [submissionId] = await record(context, accountId, submitted, 'pending', '')
    await cache.lpush(KEY.queue, String(submissionId))

    res.status(202).json({ submissionId, status: 'pending' })
  }))

  router.get('/runs/:id', requireLogin(context), guard(async (req, res) => {
    const row = await db('submission')
      .where('id', Number(req.params.id))
      .andWhere('account_id', req.accountId as number)
      .first<{ id: number; status: string; reason: string } | undefined>(
        'id', 'status', 'reason')

    if (!row) {
      fail(res, 404, 'not_found', '없는 제출입니다')
      return
    }

    const metrics = await db('run_metric').where('submission_id', row.id)
      .first<Record<string, unknown> | undefined>()

    res.json({
      submissionId: row.id,
      status: row.status,
      reason: row.reason,
      metrics: metrics ?? null,
    })
  }))

  return router
}

/** 제출을 남깁니다. 거절된 것도 남습니다 — 무엇이 왜 거절되었는지가 그 자리입니다. */
async function record(context: Context, accountId: number, submitted: {
  seed: string; deck: string; stake: string; pool: string; challenge: string
  actions: unknown[]
  claimed?: Record<string, unknown>
}, status: string, reason: string): Promise<number[]> {
  const rows = await context.db('submission').insert({
    account_id: accountId,
    season_id: context.season.id,
    seed: submitted.seed,
    deck: submitted.deck,
    stake: submitted.stake,
    pool: submitted.pool,
    challenge: submitted.challenge,
    replay: JSON.stringify({
      seed: submitted.seed,
      deck: submitted.deck,
      stake: submitted.stake,
      pool: submitted.pool,
      challenge: submitted.challenge,
      actions: submitted.actions,
      claimed: submitted.claimed ?? null,
    }),
    status,
    reason,
    judged_at: status === 'pending' ? null : new Date(),
  }).returning<{ id: number }[]>('id')

  return rows.map(row => row.id)
}
