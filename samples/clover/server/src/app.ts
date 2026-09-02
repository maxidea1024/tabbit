// 서버.
//
//     npx tsx src/app.ts
//
// **부팅에서 확인하는 것이 셋입니다** — 환경변수, 마이그레이션, 그리고 시즌의 지문이 이
// 빌드와 같은지. 셋 다 요청이 오기 전에 봅니다.

import express from 'express'
import { readEnv } from './env'
import { migrate, newDb } from './db'
import { newCache } from './redis'
import { buildFingerprint, loadData } from './core'
import { openIfNone, openSeason } from './season'
import { authRouter } from './auth/routes'
import { rankedRouter } from './ranked'
import { runsRouter, MAX_BODY } from './runs'
import { meRouter } from './me'
import { boardsRouter, rebuildIfEmpty } from './boards'
import { fail, type Context } from './http'

export async function build(): Promise<{ app: express.Express; context: Context }> {
  const env = readEnv()
  const db = newDb(env.databaseUrl)
  const cache = newCache(env.redisUrl)

  const ran = await migrate(db)
  if (ran.length > 0) console.log(`마이그레이션: ${ran.join(' · ')}`)

  const fingerprint = buildFingerprint(env.replayPath)
  // **배포에서는 시즌을 사람이 엽니다.** 자동으로 열면 잘못된 지문으로 배포한 것이 시즌
  // 교체로 보입니다.
  const season = env.production
    ? await openSeason(db, fingerprint)
    : await openIfNone(db, fingerprint)

  // 데이터를 미리 읽어 둡니다 — 첫 제출이 데이터 읽기까지 기다리지 않습니다.
  loadData(env.dataPath)

  const context: Context = { env, db, cache, season, fingerprint }

  const app = express()
  app.use(express.json({ limit: MAX_BODY }))
  app.use(express.urlencoded({ extended: false }))

  app.get('/health', (_req, res) => {
    res.json({
      fingerprint,
      season: season.id,
      providers: env.providers,
    })
  })

  // **Redis 가 비어 있으면 PostgreSQL 에서 다시 만듭니다.** 캐시가 비는 것은 사고가
  // 아니라 캐시의 성질입니다.
  const rebuilt = await rebuildIfEmpty(context, db)
  if (rebuilt > 0) console.log(`순위를 다시 만들었습니다: 제출 ${rebuilt}건`)

  app.use(authRouter(context))
  app.use(meRouter(context))
  app.use(rankedRouter(context))
  app.use(runsRouter(context))
  app.use(boardsRouter(context))

  app.use((_req, res) => { fail(res, 404, 'not_found') })
  app.use((error: Error, _req: express.Request, res: express.Response,
           _next: express.NextFunction) => {
    console.error(error)
    fail(res, 500, 'internal', '')
  })

  return { app, context }
}

if (process.argv[1]?.endsWith('app.ts')) {
  const { app, context } = await build()
  app.listen(context.env.port, () => {
    console.log(`clover 리더보드가 ${context.env.port} 에서 받습니다`)
    console.log(`  지문 ${context.fingerprint} · 시즌 ${context.season.id}`)
    console.log(`  제공자 ${context.env.providers.join(' · ')}`)
  })
}
