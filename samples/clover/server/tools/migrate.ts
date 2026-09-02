// 마이그레이션을 손으로 돌립니다. 서버는 부팅할 때 스스로 돌리므로 이것은 확인용입니다.
//
//     npx tsx tools/migrate.ts
//     npx tsx tools/migrate.ts --down    # 되돌립니다. 개발에서만 씁니다

import { readEnv } from '../src/env'
import { migrate, newDb } from '../src/db'

const env = readEnv()
const db = newDb(env.databaseUrl)
try {
  if (process.argv.includes('--down')) {
    await db.migrate.rollback(undefined, true)
    console.log('전부 되돌렸습니다')
  } else {
    const ran = await migrate(db)
    console.log(ran.length === 0 ? '돌릴 것이 없습니다' : `돌렸습니다: ${ran.join(' · ')}`)
  }
} finally {
  await db.destroy()
}
