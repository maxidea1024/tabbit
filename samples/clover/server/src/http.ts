// HTTP 의 공통 부분.
//
// **오류의 모양이 하나입니다.** `{ error, message }` 이고, 클라이언트가 `error` 로 갈래를
// 나눕니다 — 사람이 읽는 글은 클라이언트가 자기 말로 적습니다.

import type { NextFunction, Request, Response } from 'express'
import type { Cache } from './redis'
import type { Db } from './db'
import type { Env } from './env'
import type { Season } from './season'
import { readAccess } from './auth/session'

export interface Context {
  env: Env
  db: Db
  cache: Cache
  season: Season
  fingerprint: string
}

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Express {
    interface Request {
      accountId?: number
      sessionId?: number
    }
  }
}

export function fail(res: Response, status: number, error: string, message = ''): void {
  res.status(status).json({ error, message })
}

/**
 * 로그인이 필요한 길.
 *
 * **표를 읽지 않습니다.** access token 의 서명과 만료만 봅니다 — 15분이므로 그동안 지워진
 * 세션이 살아 있을 수 있고, 그 창이 짧은 것이 이 설계의 값입니다.
 */
export function requireLogin(context: Context) {
  return (req: Request, res: Response, next: NextFunction): void => {
    const header = req.header('authorization') ?? ''
    const token = header.startsWith('Bearer ') ? header.slice(7) : ''
    const claims = token === '' ? undefined : readAccess(context.env.jwtSecret, token)
    if (!claims) {
      fail(res, 401, 'unauthorized', '로그인이 필요합니다')
      return
    }
    req.accountId = claims.accountId
    req.sessionId = claims.sessionId
    next()
  }
}

/**
 * 로그인했으면 누구인지 적고, 아니어도 지나갑니다.
 *
 * **순위표는 계정이 없어도 볼 수 있습니다.** 오르는 데 계정이 필요한 것이지 보는 데
 * 필요한 것이 아니고, 무엇을 위해 계정을 만드는지는 그 표를 봐야 압니다 — 로그인 창부터
 * 띄우면 사람이 무엇을 얻는지 모르는 채로 고르게 됩니다.
 *
 * 로그인한 사람에게만 「내 자리」가 있습니다. `req.accountId` 가 없으면 그 줄이 없습니다.
 */
export function readLogin(context: Context) {
  return (req: Request, _res: Response, next: NextFunction): void => {
    const header = req.header('authorization') ?? ''
    const token = header.startsWith('Bearer ') ? header.slice(7) : ''
    const claims = token === '' ? undefined : readAccess(context.env.jwtSecret, token)
    if (claims) {
      req.accountId = claims.accountId
      req.sessionId = claims.sessionId
    }
    next()
  }
}

/** `throw` 된 것을 500 으로 바꿉니다. `async` 라우트에서 `throw` 하면 express 4 가 `catch` 하지 못합니다. */
export function guard(handler: (req: Request, res: Response) => Promise<void>) {
  return (req: Request, res: Response, next: NextFunction): void => {
    handler(req, res).catch(next)
  }
}
