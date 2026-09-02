// 합성 계정으로 순위표를 채웁니다.
//
//     npx tsx tools/seed-fake.ts --accounts 10000
//
// **L2 의 판정이 이것 위에서입니다.** 사람이 만명일 때 어느 쪽 순위도 100ms 안에 나와야
// 합니다. 값은 구워 둔 리플레이 13개의 지표를 흔들어 만듭니다 — 분포가 실제와 닮아야
// 정렬 집합의 크기가 뜻을 가집니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { readEnv } from '../src/env'
import { migrate, newDb } from '../src/db'
import { newCache } from '../src/redis'
import { buildFingerprint } from '../src/core'
import { openIfNone } from '../src/season'
import { rebuild } from '../src/boards'
import type { Context } from '../src/http'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPLAYS = path.resolve(HERE, '../../design-data/out/replay')

interface Baked {
  deck: string; stake: string; challenge?: string
  metrics: { ascent: number; bestHand: number; handsPlayed: number
             money: number; skips: number; won: boolean }
}

/** 되풀이되는 무작위. 같은 씨앗이 같은 표를 만듭니다. */
function rng(seed: number): () => number {
  let state = seed >>> 0
  return () => {
    state = (Math.imul(state, 1_664_525) + 1_013_904_223) >>> 0
    return state / 4_294_967_296
  }
}

async function main(argv: string[]): Promise<number> {
  const at = argv.indexOf('--accounts')
  const wanted = at >= 0 ? Number(argv[at + 1]) : 10_000

  const env = readEnv()
  const db = newDb(env.databaseUrl)
  const cache = newCache(env.redisUrl)
  await migrate(db)
  const fingerprint = buildFingerprint(env.replayPath)
  const season = await openIfNone(db, fingerprint)
  const context: Context = { env, db, cache, season, fingerprint }

  const baked = fs.readdirSync(REPLAYS).filter(one => one.endsWith('.json')).sort()
    .map(name => JSON.parse(fs.readFileSync(path.join(REPLAYS, name), 'utf8')) as Baked)

  const stakes = ['White', 'Red', 'Green', 'Black', 'Blue', 'Purple', 'Orange', 'Gold']
  const next = rng(20_260_902)
  const started = Date.now()

  const CHUNK = 500
  let made = 0
  while (made < wanted) {
    const size = Math.min(CHUNK, wanted - made)

    // **빈 객체의 배열을 넣지 못합니다.** 컬럼이 하나도 없는 `INSERT` 가 되어 knex 가
    // 빈 질의로 봅니다 — 기본값만으로 여러 행을 만드는 것은 `generate_series` 입니다.
    const accounts = (await db.raw(
      'INSERT INTO account (created_at) SELECT now() FROM generate_series(1, ?) RETURNING id',
      [size])).rows as { id: number }[]

    await db('profile').insert(accounts.map((row, index) => ({
      account_id: row.id,
      handle: `fake_${made + index}`,
      handle_folded: `fake_${made + index}`,
    })))

    const submissions = accounts.map((row, index) => {
      const source = baked[Math.floor(next() * baked.length)]
      // 등정을 흔들어 분포를 만듭니다. 스테이크가 앞자리이므로 자리가 넓게 퍼집니다.
      const stakeIndex = Math.floor(next() * stakes.length)
      const progress = 1 + Math.floor(next() * 24)
      // 한 스테이크의 폭은 25 입니다 — `metrics.ts` 의 `ascentPerStake` 와 같아야 합니다.
      return {
        account_id: row.id,
        season_id: season.id,
        seed: `FAKE-${made + index}`,
        deck: source.deck,
        stake: stakes[stakeIndex],
        pool: 'base',
        challenge: '',
        replay: JSON.stringify({ synthetic: true }),
        status: 'accepted',
        reason: '',
        submitted_at: new Date(started - Math.floor(next() * 86_400_000)),
        judged_at: new Date(),
        _ascent: stakeIndex * 25 + progress,
        _best: Math.floor(source.metrics.bestHand * (0.5 + next() * 2)),
        _hands: source.metrics.handsPlayed + Math.floor(next() * 10),
        _money: source.metrics.money + Math.floor(next() * 50),
        _skips: Math.floor(next() * 6),
        _won: progress >= 24,
      }
    })

    const inserted = await db('submission')
      .insert(submissions.map(({ _ascent, _best, _hands, _money, _skips, _won, ...rest }) => {
        void _ascent; void _best; void _hands; void _money; void _skips; void _won
        return rest
      }))
      .returning<{ id: number }[]>('id')

    await db('run_metric').insert(inserted.map((row, index) => ({
      submission_id: row.id,
      ascent: submissions[index]._ascent,
      best_hand: submissions[index]._best,
      hands_played: submissions[index]._hands,
      money: submissions[index]._money,
      skips: submissions[index]._skips,
      won: submissions[index]._won,
    })))

    made += size
    if (made % 2_000 === 0) console.log(`  계정 ${made} / ${wanted}`)
  }

  console.log(`계정 ${made}개 · ${((Date.now() - started) / 1_000).toFixed(1)}초`)

  const rebuiltFrom = Date.now()
  const count = await rebuild(context)
  console.log(`순위를 다시 만들었습니다: 제출 ${count}건 · `
    + `${((Date.now() - rebuiltFrom) / 1_000).toFixed(1)}초`)

  await cache.quit()
  await db.destroy()
  return 0
}

process.exitCode = await main(process.argv.slice(2))
