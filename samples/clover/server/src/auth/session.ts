// 세션.
//
// **한 계정이 여러 기계에서 동시에 로그인합니다.** `session` 표에 기계마다 한 줄이고,
// 한 줄을 지우는 것이 그 기계의 로그아웃입니다 — 다른 기계는 그대로입니다.
//
// 쿠키가 아니라 token 인 이유는 데스크탑과 안드로이드입니다. 같은 웹 빌드가 `file://` 과
// Capacitor 안에서도 도는데 거기에서 쿠키는 각각 다르게 동작합니다.

import * as crypto from 'crypto'
import jwt from 'jsonwebtoken'
import type { Db } from '../db'

/** access token 의 수명. 짧게 두고 서버가 상태를 갖지 않습니다. */
const ACCESS_SECONDS = 15 * 60

/** refresh token 의 수명. */
const REFRESH_DAYS = 30

/**
 * 한 계정이 들 수 있는 기계 수.
 *
 * **상한이 있는 이유는 표가 자라기 때문입니다.** 넘으면 가장 오래 쓰지 않은 것을
 * 지웁니다 — 지금 쓰고 있는 기계가 밀려나지 않는 방향입니다.
 */
export const MAX_SESSIONS = 10

/**
 * 바꾼 직후의 유예.
 *
 * **네트워크가 끊긴 재시도를 위한 것입니다.** 서버가 바꾼 응답을 받지 못한 기계가 예전
 * token 으로 한 번 더 오는 일이 실제로 있고, 그때 로그아웃시키면 사람이 이유를 알 수
 * 없습니다.
 */
const GRACE_SECONDS = 60

export interface Tokens {
  access: string
  refresh: string
  expiresIn: number
}

export interface Claims {
  accountId: number
  sessionId: number
}

function hash(token: string): string {
  return crypto.createHash('sha256').update(token).digest('hex')
}

function newToken(): string {
  return crypto.randomBytes(32).toString('base64url')
}

function signAccess(secret: string, claims: Claims): string {
  return jwt.sign({ sid: claims.sessionId }, secret, {
    subject: String(claims.accountId),
    expiresIn: ACCESS_SECONDS,
  })
}

/** 서명과 만료만 봅니다. **표를 읽지 않습니다** — 그래서 15분입니다. */
export function readAccess(secret: string, token: string): Claims | undefined {
  try {
    const payload = jwt.verify(token, secret) as { sub?: string; sid?: number }
    if (!payload.sub || typeof payload.sid !== 'number') return undefined
    return { accountId: Number(payload.sub), sessionId: payload.sid }
  } catch {
    return undefined
  }
}

/** 새 기계 하나가 들어옵니다. */
export async function startSession(db: Db, secret: string,
                                   accountId: number, label: string): Promise<Tokens> {
  const refresh = newToken()
  const expiresAt = new Date(Date.now() + REFRESH_DAYS * 86_400_000)

  const [row] = await db('session')
    .insert({ account_id: accountId, refresh_hash: hash(refresh), label, expires_at: expiresAt })
    .returning<{ id: number }[]>('id')

  await evictOldest(db, accountId)

  return {
    access: signAccess(secret, { accountId, sessionId: row.id }),
    refresh,
    expiresIn: ACCESS_SECONDS,
  }
}

/**
 * refresh token 을 새 쌍으로 바꿉니다.
 *
 * **한 번 쓰면 새 것으로 바뀝니다.** 같은 줄을 고쳐 쓰므로 그 기계의 자리와 이름과 만든
 * 시각이 그대로 남습니다 — 지우고 새로 만들면 기계 목록에서 그 기계가 방금 생긴 것으로
 * 보입니다.
 */
export async function rotate(db: Db, secret: string,
                             refresh: string): Promise<Tokens | undefined> {
  const given = hash(refresh)
  const graceFrom = new Date(Date.now() - GRACE_SECONDS * 1_000)

  const row = await db('session')
    .where('refresh_hash', given)
    .orWhere(builder => builder.where('prev_hash', given).where('rotated_at', '>', graceFrom))
    .andWhere('expires_at', '>', new Date())
    .first<{ id: number; account_id: number; refresh_hash: string } | undefined>()

  if (!row) return undefined

  // 유예 안에서 예전 것으로 온 것이면 바꾸지 않고 지금의 것을 그대로 씁니다. 여기서 또
  // 바꾸면 재시도마다 한 번씩 바뀌고, 먼저 도착한 응답이 곧바로 낡습니다.
  if (row.refresh_hash === given) {
    const next = newToken()
    await db('session').where('id', row.id).update({
      refresh_hash: hash(next),
      prev_hash: given,
      rotated_at: new Date(),
      used_at: new Date(),
    })
    return {
      access: signAccess(secret, { accountId: row.account_id, sessionId: row.id }),
      refresh: next,
      expiresIn: ACCESS_SECONDS,
    }
  }

  await db('session').where('id', row.id).update({ used_at: new Date() })
  return {
    access: signAccess(secret, { accountId: row.account_id, sessionId: row.id }),
    refresh,
    expiresIn: ACCESS_SECONDS,
  }
}

/** 이 기계만 로그아웃합니다. */
export async function endSession(db: Db, sessionId: number): Promise<void> {
  await db('session').where('id', sessionId).delete()
}

/** 모든 기계에서 로그아웃합니다. 열쇠가 샜을 때 사람이 부르는 길입니다. */
export async function endAllSessions(db: Db, accountId: number): Promise<number> {
  return db('session').where('account_id', accountId).delete()
}

export interface DeviceRow {
  id: number
  label: string
  createdAt: string
  usedAt: string
}

/** 지금 로그인되어 있는 기계들. 프로필 판이 이것을 보여 줍니다. */
export async function listSessions(db: Db, accountId: number): Promise<DeviceRow[]> {
  const rows = await db('session')
    .where('account_id', accountId).andWhere('expires_at', '>', new Date())
    .orderBy('used_at', 'desc')
    .select<{ id: number; label: string; created_at: Date; used_at: Date }[]>(
      'id', 'label', 'created_at', 'used_at')
  return rows.map(row => ({
    id: row.id,
    label: row.label,
    createdAt: row.created_at.toISOString(),
    usedAt: row.used_at.toISOString(),
  }))
}

/** 만료된 것과 상한을 넘은 것을 지웁니다. */
async function evictOldest(db: Db, accountId: number): Promise<void> {
  await db('session').where('account_id', accountId)
    .andWhere('expires_at', '<=', new Date()).delete()

  const keep = await db('session').where('account_id', accountId)
    .orderBy('used_at', 'desc').limit(MAX_SESSIONS)
    .select<{ id: number }[]>('id')

  if (keep.length < MAX_SESSIONS) return
  await db('session').where('account_id', accountId)
    .whereNotIn('id', keep.map(row => row.id)).delete()
}
