// 시즌.
//
// **시즌은 규칙 지문에 묶입니다.** 밸런스가 바뀌면 예전 점수와 견줄 수 없는데, 바꾸지
// 못하면 게임이 멈춥니다. 시즌을 나누면 둘 다 됩니다.

import type { Db } from './db'

export interface Season {
  id: number
  fingerprint: string
}

/**
 * 지금 열려 있는 시즌.
 *
 * **지문이 다르면 부팅하지 않습니다.** 서버가 낡은 채로 제출을 받으면 전부
 * `bad_fingerprint` 가 되고, 그것은 클라이언트 탓으로 보입니다.
 */
export async function openSeason(db: Db, fingerprint: string): Promise<Season> {
  const row = await db<Season>('season')
    .select('id', 'fingerprint').whereNull('ends_at').first()

  if (!row) {
    throw new Error(
      `열려 있는 시즌이 없습니다. 이 빌드의 지문은 ${fingerprint} 입니다 — `
      + 'season 에 행 하나를 넣고 다시 띄웁니다')
  }
  if (row.fingerprint !== fingerprint) {
    throw new Error(
      `시즌의 지문과 이 빌드가 어긋납니다: 시즌 ${row.fingerprint} · 빌드 ${fingerprint}. `
      + '지문이 바뀌는 배포는 시즌을 나누는 배포입니다 — season 에 새 행을 넣습니다')
  }
  return row
}

/**
 * 열려 있는 시즌이 없으면 이 지문으로 하나 엽니다.
 *
 * **개발과 첫 배포에만 씁니다.** 배포 절차에서 시즌을 여는 것은 사람이 하는 일이고,
 * 자동으로 열면 잘못된 지문으로 배포한 것이 시즌 교체로 보입니다.
 */
export async function openIfNone(db: Db, fingerprint: string): Promise<Season> {
  const has = await db('season').whereNull('ends_at').first()
  if (!has) await db('season').insert({ fingerprint })
  return openSeason(db, fingerprint)
}
