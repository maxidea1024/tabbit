// Redis.
//
// **캐시입니다.** 순위표를 PostgreSQL 만으로도 낼 수 있지만 「내 순위」가
// `COUNT(*) WHERE score > ?` 이고 그것은 보드마다 한 번 전체를 셉니다. `ZREVRANK` 는
// O(log N) 입니다. **비어도 PostgreSQL 에서 전부 다시 만듭니다.**

import Redis from 'ioredis'

export type Cache = Redis

export function newCache(url: string): Cache {
  return new Redis(url, { maxRetriesPerRequest: 3, lazyConnect: false })
}

export const KEY = {
  /** 시즌 순위. member 는 계정, score 는 지표. */
  board: (season: number | 'all', boardId: string) => `lb:${season}:${boardId}`,
  /** 랭크 시드의 빠른 길. PostgreSQL 에도 같은 것이 있습니다. */
  seed: (seed: string) => `seed:${seed}`,
  /** 로그인 뒤 한 번 쓰는 교환 code. */
  code: (code: string) => `code:${code}`,
  /** 한도. */
  rate: (accountId: number, what: string) => `rate:${accountId}:${what}`,
  /** 재현 대기. */
  queue: 'queue:judge',
} as const

/**
 * 창 하나짜리 한도.
 *
 * **처음 센 때부터 창이 흐릅니다.** 미끄러지는 창으로 두면 열쇠마다 목록을 들고 있어야
 * 하고, 시드 20개를 세는 데 그만한 것이 필요하지 않습니다.
 */
export async function underLimit(cache: Cache, key: string,
                                 limit: number, windowSeconds: number): Promise<boolean> {
  const count = await cache.incr(key)
  if (count === 1) await cache.expire(key, windowSeconds)
  return count <= limit
}
