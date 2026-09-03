// 조커 풀.
//
// 조커를 뽑는 자리가 4곳입니다 — 상점의 카드 칸 · 광대 팩 · 태그가 남긴 선물 ·
// `CreateCard`. **전부 이 함수를 지납니다.** 같은 `filter` 를 네 번 적으면 풀을 하나
// 늘릴 때 한 곳을 빼먹고, 그러면 어떤 경로로만 확장 조커가 나옵니다.
//
// 런이 어느 풀을 쓰는지는 `RunState.pools` 에 있고 시작할 때 정해집니다. 기본값은
// `Base` 하나이므로, **아무것도 넘기지 않으면 구워 둔 리플레이가 그대로 유효합니다.**

import type { Data } from './data'
import type { RunState } from './state'
import type { JokerRecord } from '../generated/tables/joker'
import type { Rarity } from '../generated/enums/rarity'
import { JokerPool } from '../generated/enums/joker-pool'
import { BanKind } from '../generated/enums/ban-kind'

/**
 * 이 런에서 나올 수 있는 조커들. `rarity` 를 주면 그 희귀도만 남깁니다.
 *
 * 빈 배열이 돌아올 수 있습니다 — 풀과 희귀도의 조합에 아무것도 없는 경우이고, 부르는 쪽이
 * 그때 뽑기를 건너뜁니다.
 */
export function jokerPool(data: Data, state: RunState, rarity?: Rarity): JokerRecord[] {
  // **같은 조건이면 한 번 고른 것을 다시 씁니다.** 상점 칸마다 500행을 다시 거르고 있었고,
  // 풀과 챌린지는 런 안에서 바뀌지 않습니다. 부르는 쪽은 읽기만 합니다 — 넷 다 한 장을
  // 뽑아 갈 뿐입니다.
  let byKey = POOL_CACHE.get(data)
  if (!byKey) {
    byKey = new Map()
    POOL_CACHE.set(data, byKey)
  }
  const key = `${state.pools.join(',')}|${state.challengeId}|${rarity ?? ''}`
  let found = byKey.get(key)
  if (found === undefined) {
    const no = banned(data, state, BanKind.Joker)
    found = data.tables.joker.records.filter(row =>
      state.pools.includes(row.pool) && (rarity === undefined || row.rarity === rarity)
      && !no.has(row.jokerId))
    byKey.set(key, found)
  }
  return found
}

/** 풀·챌린지·희귀도로 고른 조커들. 데이터마다 따로입니다 — 테스트는 표를 바꾼 데이터를 씁니다. */
const POOL_CACHE = new WeakMap<Data, Map<string, JokerRecord[]>>()

/**
 * 사람이 고를는 것. 옵션에 이 값이 적혀 다음 판에 쓰입니다.
 *
 * **풀의 목록이 아니라 둘 중 하나입니다.** 확장만 켜고 기본을 끄는 조합은 둔
 * 이유가 없습니다 — 기본 150종이 원작 대조본이고 그것이 한 토대이기 때문입니다.
 */
export type PoolChoice = 'base' | 'all'

export function poolsOf(choice: PoolChoice): JokerPool[] {
  return choice === 'all' ? [JokerPool.Base, JokerPool.Greenhouse] : [JokerPool.Base]
}

/**
 * 챌린지가 금지한 것들.
 *
 * **금지를 보는 곳이 이 파일 하나입니다.** 같은 필터를 뽑는 자리마다 적으면 한 곳을
 * 빼먹고, 그러면 어떤 경로로만 금지된 것이 나옵니다 — 조커를 `jokerPool()` 로 모은 이유와
 * 같습니다.
 */
function banned(data: Data, state: RunState, kind: BanKind): Set<string> {
  if (state.challengeId === '') return EMPTY
  const key = `${state.challengeId}.${kind}`
  let found = BAN_CACHE.get(key)
  if (found === undefined) {
    found = new Set(data.tables.challengeBan.records
      .filter(row => row.owner === state.challengeId && row.kind === kind)
      .map(row => row.refId))
    BAN_CACHE.set(key, found)
  }
  return found
}

const EMPTY: Set<string> = new Set()
/** 챌린지는 런 도중에 바뀌지 않으므로 한 번 센 것을 남겨 둡니다. */
const BAN_CACHE = new Map<string, Set<string>>()

/** 금지되지 않은 것만 남깁니다. */
function allow<T>(data: Data, state: RunState, kind: BanKind,
                  rows: readonly T[], id: (row: T) => string): T[] {
  const no = banned(data, state, kind)
  return no.size === 0 ? rows.slice() : rows.filter(row => !no.has(id(row)))
}

export function tarotPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Tarot, data.tables.tarot.records, row => row.tarotId)
}

export function planetPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Planet, data.tables.planet.records, row => row.planetId)
}

export function spectralPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Spectral, data.tables.spectral.records, row => row.spectralId)
}

export function voucherPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Voucher, data.tables.voucher.records, row => row.voucherId)
}

export function packPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Pack, data.tables.boosterPack.records, row => row.packId)
}

export function tagPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Tag, data.tables.tag.records, row => row.tagId)
}

export function bossPool(data: Data, state: RunState) {
  return allow(data, state, BanKind.Boss, data.tables.bossBlind.records, row => row.bossId)
}
