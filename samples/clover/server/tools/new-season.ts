// 시즌을 나눕니다.
//
//     npx tsx tools/new-season.ts
//     npx tsx tools/new-season.ts --check    # 나눠야 하는지만 봅니다
//
// **지문이 바뀌는 배포가 곧 시즌 교체입니다.** 규칙이 바뀌면 예전 점수와 견줄 수 없고,
// 그것을 알리는 자리가 시즌입니다 — 그래서 서버는 지문이 어긋나면 부팅하지 않습니다.
//
// 이 도구가 여는 시즌은 **이 빌드의 지문**으로 열립니다. 배포 절차의 한 줄이고, 사람이
// 부릅니다 — 자동으로 열면 잘못된 지문으로 배포한 것이 시즌 교체로 보입니다.

import { readEnv } from '../src/env'
import { migrate, newDb } from '../src/db'
import { buildFingerprint } from '../src/core'

const env = readEnv()
const db = newDb(env.databaseUrl)
const checkOnly = process.argv.includes('--check')

try {
  await migrate(db)
  const fingerprint = buildFingerprint(env.replayPath)

  const open = await db('season').whereNull('ends_at')
    .first<{ id: number; fingerprint: string } | undefined>('id', 'fingerprint')

  if (open && open.fingerprint === fingerprint) {
    console.log(`시즌 ${open.id} 이 이 빌드의 지문(${fingerprint})으로 열려 있습니다`)
    process.exit(0)
  }

  if (checkOnly) {
    console.log(open
      ? `나눠야 합니다: 시즌 ${open.id} 의 ${open.fingerprint} → 빌드의 ${fingerprint}`
      : `열려 있는 시즌이 없습니다. 빌드의 지문은 ${fingerprint} 입니다`)
    process.exit(1)
  }

  await db.transaction(async trx => {
    // **예전 시즌을 닫습니다.** 그 순위는 「지난 시즌」으로 남고 전체 기간에도 남습니다.
    if (open) await trx('season').where('id', open.id).update({ ends_at: new Date() })
    await trx('season').insert({ fingerprint })
  })

  const now = await db('season').whereNull('ends_at').first<{ id: number }>('id')
  console.log(open
    ? `시즌 ${open.id} 을 닫고 시즌 ${now.id} 을 열었습니다 · 지문 ${fingerprint}`
    : `시즌 ${now.id} 을 열었습니다 · 지문 ${fingerprint}`)
} finally {
  await db.destroy()
}
