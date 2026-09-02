// 스테이크 한 줄을 찾는 자리.
//
// **적히는 형태가 셋입니다.** 리플레이와 `newRun` 은 enum 의 이름(`White`)을 적고, 화면에서
// 값이 숫자 문자열로 오기도 하며(`1`), 시트의 `name` 칸은 표시 이름(`흰색`)입니다.
//
// **찾는 곳이 넷이었고 넷이 저마다 비교했습니다.** 그중 셋이 `name` 과 숫자만 보았으므로
// `White` 로는 아무 줄도 찾지 못했고, 안테 열과 버리기 증감과 스몰 블라인드 보상이 흰색을
// 뺀 7종에서 걸리지 않았습니다. 흰색만 드러나지 않은 것은 그 셋의 기본값이 흰색 줄의 값과
// 같기 때문입니다 — 안테 1열이고 버리기 증감이 0이며, 스몰 블라인드 보상 $3 은 `Blind` 표에
// 적힌 값과 같습니다.
//
// 그래서 비교를 여기 하나 두고 부르는 쪽은 형태를 모릅니다.

import { StakeKind } from '../generated/enums/stake-kind'
import type { StakeRecord } from '../generated/tables/stake'
import type { Data } from './data'

/**
 * 이 스테이크의 표 한 줄. 어느 형태로 적혀 있어도 찾습니다.
 *
 * enum 의 이름을 먼저 봅니다. **숫자 enum 은 되돌림 이름도 키로 가지므로** `StakeKind['1']`
 * 이 `'White'` 라는 문자열이 되고, 그것을 그대로 색인에 넘기면 아무 줄도 나오지 않습니다 —
 * 그래서 숫자가 나온 것만 씁니다.
 */
export function stakeRow(data: Data, stake: string): StakeRecord | undefined {
  const byName = (StakeKind as Record<string, StakeKind | string | undefined>)[stake]
  if (typeof byName === 'number') return data.tables.stake.findByStake(byName)

  return data.tables.stake.records.find(
    row => String(row.stake) === stake || row.name === stake)
}

/** 그 스테이크의 현지화 키에 쓰는 조각. `stake.white.name` 의 `white` 입니다. */
export function stakeSlug(stake: StakeKind): string {
  return (StakeKind[stake] ?? '').toLowerCase()
}
