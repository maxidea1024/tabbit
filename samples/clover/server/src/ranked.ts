// 랭크 시드.
//
// **시드를 서버가 주는 것이 검증의 반입니다.** 시드를 고르게 두면 좋은 시드를 오프라인에서
// 찾아 오는 것이 가능하고, 그것은 실력이 아니라 계산입니다. 시드를 한 번만 쓸 수 있게 두면
// 같은 런을 두 번 올리는 것도 함께 막힙니다.

import * as crypto from 'crypto'
import { Router } from 'express'
import { KEY, underLimit } from './redis'
import { fail, guard, requireLogin, type Context } from './http'
import { isPoolChoice, loadData } from './core'
import { stakeIndexOf } from '../../web/src/core/metrics'
import { StakeKind } from '../../web/src/generated/enums/stake-kind'
import type { Data } from '../../web/src/core/data'

/**
 * 흰 스테이크를 가리키는 꼴인가.
 *
 * **`stakeIndexOf` 는 모르는 것도 1을 돌려줍니다** — 순위를 부풀리지 않는 방향이지만,
 * 받아들일지를 정하는 자리에서는 「흰색인 것」과 「모르는 것」을 갈라야 합니다.
 */
function isWhite(data: Data, stake: string): boolean {
  const row = data.tables.stake.records.find(one => Number(one.stake) === StakeKind.White)
  if (!row) return false
  return StakeKind[row.stake] === stake || String(row.stake) === stake || row.name === stake
}

/** 시드의 수명. 받아 두고 하루 안에 시작합니다. */
export const SEED_HOURS = 24

/** 한 시간에 받을 수 있는 시드 수. */
export const SEED_LIMIT = 20

export interface SeedRow {
  seed: string
  account_id: number
  deck: string
  stake: string
  pool: string
  challenge: string
  expires_at: Date
  used_at: Date | null
}

/** `CLOVER-` 뒤 8자. 사람이 고르는 시드와 같은 자리에 들어갑니다. */
function newSeed(): string {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  const bytes = crypto.randomBytes(8)
  let body = ''
  for (const byte of bytes) body += alphabet[byte % alphabet.length]
  return `CLOVER-${body}`
}

export function rankedRouter(context: Context): Router {
  const router = Router()
  const { env, db, cache } = context

  router.post('/ranked/seed', requireLogin(context), guard(async (req, res) => {
    const accountId = req.accountId as number
    const data = loadData(env.dataPath)

    const challenge = String(req.body?.challenge ?? '')
    const hasSettings = req.body?.deck !== undefined || req.body?.stake !== undefined
      || req.body?.pool !== undefined

    let deck: string
    let stake: string
    let pool: string

    if (challenge !== '') {
      // **챌린지가 덱과 스테이크와 풀을 다 정합니다.** 함께 오면 거절합니다 — 무시하고
      // 덮으면 클라이언트가 무엇으로 시작하는지 모르는 채로 시작합니다.
      if (hasSettings) {
        fail(res, 400, 'settings_with_challenge',
             '챌린지를 고르면 덱 · 스테이크 · 풀을 함께 보내지 않습니다')
        return
      }
      if (!data.tables.challenge.records.some(row => row.challengeId === challenge)) {
        fail(res, 400, 'unknown_challenge', '없는 챌린지입니다')
        return
      }
      deck = ''
      stake = 'White'
      pool = 'base'
    } else {
      deck = String(req.body?.deck ?? '')
      stake = String(req.body?.stake ?? '')
      pool = String(req.body?.pool ?? 'base')

      if (!data.tables.deck.records.some(row => row.deckId === deck)) {
        fail(res, 400, 'unknown_deck', '없는 덱입니다')
        return
      }
      // **스테이크는 세 가지 꼴로 옵니다.** enum 의 이름(`White`)과 값(`1`)과 시트의
      // 표시 이름(`흰색`)입니다 — `stakeIndexOf` 가 셋을 다 받고, 여기서 따로 판정하면
      // 그 하나가 빠집니다. 실제로 `White` 가 빠져 있었습니다.
      if (stakeIndexOf(data, stake) === 1 && !isWhite(data, stake)) {
        fail(res, 400, 'unknown_stake', '없는 스테이크입니다')
        return
      }
      if (!isPoolChoice(pool)) {
        fail(res, 400, 'unknown_pool', '풀은 base 또는 all 입니다')
        return
      }
    }

    if (!await underLimit(cache, KEY.rate(accountId, 'seed'), SEED_LIMIT, 3_600)) {
      fail(res, 429, 'too_many_seeds', '시드를 너무 자주 받고 있습니다')
      return
    }

    const seed = newSeed()
    const expiresAt = new Date(Date.now() + SEED_HOURS * 3_600_000)

    await db('ranked_seed').insert({
      seed, account_id: accountId, deck, stake, pool, challenge, expires_at: expiresAt,
    })
    // 빠른 길입니다. 없어도 PostgreSQL 에 같은 것이 있습니다.
    await cache.set(KEY.seed(seed), String(accountId), 'EX', SEED_HOURS * 3_600)

    res.json({ seed, deck, stake, pool, challenge, expiresAt: expiresAt.toISOString() })
  }))

  return router
}

/**
 * 이 제출이 그 시드로 시작한 것인가.
 *
 * **설정까지 봅니다.** 시드만 맞고 덱이 다르면 그것은 다른 런입니다.
 */
export async function claimSeed(context: Context, accountId: number, submitted: {
  seed: string; deck: string; stake: string; pool: string; challenge: string
}): Promise<boolean> {
  const rows = await context.db<SeedRow>('ranked_seed')
    .where('seed', submitted.seed)
    .andWhere('account_id', accountId)
    .andWhere('challenge', submitted.challenge)
    .andWhere('stake', submitted.stake)
    .andWhere('pool', submitted.pool)
    .andWhere('expires_at', '>', new Date())
    .whereNull('used_at')
    // 챌린지는 덱을 스스로 정하므로 발급 때 비워 둡니다.
    .andWhere(builder => builder.where('deck', submitted.deck).orWhere('deck', ''))
    // **한 번만 쓰는 것을 여기서 정합니다.** 조건을 걸어 갱신하므로 동시에 둘이 와도
    // 하나만 지납니다.
    .update({ used_at: new Date() })
    .returning<{ seed: string }[]>('seed')

  return rows.length > 0
}
