// 계정과 표시 이름.
//
// **계정과 신원을 나누어 둡니다.** Google 로 만든 계정에 나중에 Discord 를 붙이면
// `identity` 한 줄이 늘어나고 기록은 그대로입니다.

import type { Db } from './db'
import type { Provider } from './env'

/** 이름의 규칙. 3~16자의 영문 · 숫자 · 밑줄입니다. */
export const HANDLE_SHAPE = /^[A-Za-z0-9_]{3,16}$/

/** 이름을 바꿀 수 있는 간격. */
export const HANDLE_COOLDOWN_DAYS = 30

export interface Profile {
  accountId: number
  handle: string
  tier: string
  lastSeasonTier: string
}

/**
 * 이 신원의 계정. 없으면 만듭니다.
 *
 * **이메일과 이름을 받지 않습니다.** 제공자가 주지만 들고 있지 않은 것은 새지 않습니다.
 */
export async function accountFor(db: Db, provider: Provider,
                                 subject: string): Promise<number> {
  const found = await db('identity')
    .where({ provider, subject })
    .first<{ account_id: number } | undefined>('account_id')
  if (found) return found.account_id

  return db.transaction(async trx => {
    const [account] = await trx('account').insert({}).returning<{ id: number }[]>('id')
    await trx('identity').insert({ provider, subject, account_id: account.id })
    await trx('profile').insert({ account_id: account.id })
    return account.id
  })
}

export async function profileOf(db: Db, accountId: number): Promise<Profile | undefined> {
  const row = await db('profile').where('account_id', accountId)
    .first<{ handle: string | null; tier: string; last_season_tier: string } | undefined>(
      'handle', 'tier', 'last_season_tier')
  if (!row) return undefined
  return {
    accountId,
    handle: row.handle ?? '',
    tier: row.tier,
    lastSeasonTier: row.last_season_tier,
  }
}

export async function profileByHandle(db: Db, handle: string): Promise<Profile | undefined> {
  const row = await db('profile').where('handle_folded', handle.toLowerCase())
    .first<{ account_id: number; handle: string; tier: string; last_season_tier: string }
      | undefined>('account_id', 'handle', 'tier', 'last_season_tier')
  if (!row) return undefined
  return {
    accountId: row.account_id,
    handle: row.handle,
    tier: row.tier,
    lastSeasonTier: row.last_season_tier,
  }
}

export type HandleResult = 'ok' | 'shape' | 'taken' | 'cooldown'

/**
 * 이름을 정하거나 바꿉니다.
 *
 * **대소문자를 구분하지 않고 유일합니다.** 보이는 대로 두고 찾을 때만 접습니다 — 접어서
 * 저장하면 사람이 고른 모양이 없어집니다.
 */
export async function setHandle(db: Db, accountId: number,
                                handle: string): Promise<HandleResult> {
  if (!HANDLE_SHAPE.test(handle)) return 'shape'

  const now = new Date()
  const current = await db('profile').where('account_id', accountId)
    .first<{ handle: string | null; handle_changed_at: Date | null } | undefined>(
      'handle', 'handle_changed_at')

  if (current?.handle_changed_at) {
    const next = new Date(current.handle_changed_at.getTime()
      + HANDLE_COOLDOWN_DAYS * 86_400_000)
    if (next > now) return 'cooldown'
  }

  try {
    await db('profile').where('account_id', accountId).update({
      handle,
      handle_folded: handle.toLowerCase(),
      // **처음 정하는 것은 바꾼 것이 아닙니다.** 첫 이름에 30일을 걸면 오타 하나가 한 달
      // 남습니다.
      handle_changed_at: current?.handle ? now : null,
    })
    return 'ok'
  } catch {
    // 유일 제약이 걸린 것입니다. 먼저 조회하고 넣으면 그 사이에 남이 가져갈 수 있습니다.
    return 'taken'
  }
}

/** 계정과 그에 딸린 전부를 지웁니다. `ON DELETE CASCADE` 가 나머지를 따라갑니다. */
export async function deleteAccount(db: Db, accountId: number): Promise<void> {
  await db('account').where('id', accountId).delete()
}
