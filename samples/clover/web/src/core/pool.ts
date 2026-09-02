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

/**
 * 이 런에서 나올 수 있는 조커들. `rarity` 를 주면 그 희귀도만 남깁니다.
 *
 * 빈 배열이 돌아올 수 있습니다 — 풀과 희귀도의 조합에 아무것도 없는 경우이고, 부르는 쪽이
 * 그때 뽑기를 건너뜁니다.
 */
export function jokerPool(data: Data, state: RunState, rarity?: Rarity): JokerRecord[] {
  return data.tables.joker.records.filter(row =>
    state.pools.includes(row.pool) && (rarity === undefined || row.rarity === rarity))
}

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
