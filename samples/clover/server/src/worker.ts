// 재현하는 프로세스.
//
// **서버와 따로 뜹니다.** 한 프로세스에 합치면 개발에서는 되고 배포에서 큐에 적체가
// 생기는 것을 개발이 보지 못합니다.
//
//     npx tsx src/worker.ts

import { newDb, migrate, type Db } from './db'
import { newCache, KEY, type Cache } from './redis'
import { buildFingerprint } from './core'
import { judge, type Submitted } from './judge'
import { publish, refreshTier } from './boards'
import { openSeason } from './season'
import type { Context } from './http'
import { readEnv } from './env'

/** 큐가 비었을 때 기다리는 시간. 0 이면 영영 기다리므로 종료 신호를 받지 못합니다. */
const WAIT_SECONDS = 5

export async function judgeOne(db: Db, cache: Cache, dataPath: string,
                               submissionId: number,
                               context?: Context): Promise<string> {
  const row = await db('submission').where('id', submissionId)
    .first<{ id: number; account_id: number; submitted_at: Date
             replay: Submitted & { claimed?: Record<string, unknown> }
             status: string } | undefined>(
      'id', 'account_id', 'submitted_at', 'replay', 'status')

  if (!row) return 'not_found'
  // 이미 판정이 난 것은 다시 하지 않습니다. 큐에 두 번 들어간 것이 그런 경우입니다.
  if (row.status !== 'pending') return row.status

  const verdict = judge(dataPath, row.replay)

  if (!verdict.ok) {
    await db('submission').where('id', row.id)
      .update({ status: 'rejected', reason: verdict.reason, judged_at: new Date() })
    return `rejected:${verdict.reason}`
  }

  // **클라이언트가 센 것과 견주기만 합니다.** 순위에 올라가는 것은 서버가 센 것이고,
  // 어긋난 것은 두 곳의 셈이 갈라졌다는 표시입니다.
  const claimed = row.replay.claimed
  if (claimed) {
    const off = Object.entries(verdict.metrics)
      .filter(([key, value]) => claimed[key] !== undefined && claimed[key] !== value)
    if (off.length > 0) {
      console.warn(`  #${row.id} 클라이언트가 센 것과 다릅니다: `
        + off.map(([key, value]) => `${key} ${String(claimed[key])} → ${String(value)}`)
          .join(' · '))
    }
  }

  await db.transaction(async trx => {
    await trx('submission').where('id', row.id)
      .update({ status: 'accepted', reason: '', judged_at: new Date() })
    await trx('run_metric').insert({
      submission_id: row.id,
      ascent: verdict.metrics.ascent,
      best_hand: verdict.metrics.bestHand,
      hands_played: verdict.metrics.handsPlayed,
      money: verdict.metrics.money,
      skips: verdict.metrics.skips,
      won: verdict.metrics.won,
    }).onConflict('submission_id').merge()
  })

  // **순위에 올리는 것은 판정이 끝난 뒤입니다.** 먼저 올리면 거절된 것이 잠시 순위표에
  // 보입니다.
  if (context) {
    await publish(context, row.account_id, {
      deck: row.replay.deck,
      stake: row.replay.stake,
      pool: row.replay.pool,
      challenge: row.replay.challenge,
    }, verdict.metrics, row.submitted_at)
    await refreshTier(context, row.account_id)
  }

  void cache
  return 'accepted'
}

async function main(): Promise<void> {
  const env = readEnv()
  const db = newDb(env.databaseUrl)
  const cache = newCache(env.redisUrl)

  await migrate(db)
  const fingerprint = buildFingerprint(env.replayPath)
  const season = await openSeason(db, fingerprint)
  const context: Context = { env, db, cache, season, fingerprint }
  console.log(`worker 가 큐를 봅니다 · 지문 ${fingerprint} · 시즌 ${season.id}`)

  let running = true
  for (const signal of ['SIGINT', 'SIGTERM'] as const) {
    process.on(signal, () => { running = false })
  }

  while (running) {
    const got = await cache.brpop(KEY.queue, WAIT_SECONDS)
    if (!got) continue
    const submissionId = Number(got[1])
    try {
      const verdict = await judgeOne(db, cache, env.dataPath, submissionId, context)
      console.log(`  #${submissionId} ${verdict}`)
    } catch (error) {
      // **제출 하나의 `throw` 로 worker 가 끝나지 않습니다.** 그 제출이 잘못된 것과
      // worker 가 내려가는 것은 다른 일입니다.
      console.error(`  #${submissionId} 판정에서 예외가 발생하였습니다`, error)
      await db('submission').where('id', submissionId)
        .update({ status: 'rejected', reason: 'invalid_action', judged_at: new Date() })
    }
  }

  await cache.quit()
  await db.destroy()
}

// 임포트해서 `judgeOne` 만 쓸 때는 돌지 않습니다.
if (process.argv[1]?.endsWith('worker.ts')) {
  main().catch(error => {
    console.error(error)
    process.exit(1)
  })
}
