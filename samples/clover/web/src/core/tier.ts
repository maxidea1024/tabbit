// 등급 한 줄을 찾는 자리.
//
// **적히는 형태가 둘입니다.** 서버는 enum 의 이름(`Bronze`)이나 숫자 문자열(`1`)로 보내고,
// 시트의 `name` 칸은 기획자가 읽는 이름(`브론즈`)입니다.
//
// 찾는 곳이 셋이었고 셋이 저마다 비교했습니다 — 카드와 프로필 판과 순위표입니다.
// [`stake.ts`](stake.ts) 와 같은 규약으로 비교를 여기 하나 둡니다.

import { TierKind } from '../generated/enums/tier-kind'
import type { TierRecord } from '../generated/tables/tier'
import type { Data } from './data'
import { nameOf } from './strings'

/** 이 등급의 표 한 줄. 어느 형태로 적혀 있어도 찾습니다. */
export function tierRow(data: Data, tier: string): TierRecord | undefined {
  return data.tables.tier.records.find(
    row => String(row.tier) === tier || row.name === tier || TierKind[row.tier] === tier)
}

/** 그 등급의 현지화 열쇠에 쓰는 조각. `tier.bronze.name` 의 `bronze` 입니다. */
export function tierSlug(tier: TierKind): string {
  return (TierKind[tier] ?? '').toLowerCase()
}

/**
 * 등급의 표시 이름.
 *
 * **글 표에서 옵니다.** 시트의 `name` 은 기획자가 읽는 이름이고 한국어 하나뿐입니다 —
 * 그것을 화면에 적으면 어느 말로 켜도 등급만 한국어로 남습니다. 표에 없는 등급이면
 * 받은 그대로 돌려줍니다.
 */
export function tierName(data: Data, tier: string): string {
  const row = tierRow(data, tier)
  return row === undefined ? tier : nameOf(data, 'tier', tierSlug(row.tier), row.name)
}
