// 개발용 세션 하나를 찍어 냅니다.
//
//     npx tsx tools/mint-session.ts --handle demo
//
// **제공자의 열쇠 없이 로그인한 화면을 보기 위한 것입니다.** OAuth 를 지나지 않고 계정과
// 세션을 만들어 token 쌍을 적어 주므로, 브라우저의 `clover.session` 에 넣으면 로그인한
// 상태가 됩니다.
//
// **개발 환경에서만 돕니다.** `NODE_ENV=production` 이면 아무것도 만들지 않습니다 — 이
// 도구가 배포에 남으면 그것이 로그인 없이 계정을 만드는 길입니다.

import { readEnv } from '../src/env'
import { migrate, newDb } from '../src/db'
import { accountFor, setHandle } from '../src/accounts'
import { startSession } from '../src/auth/session'

function arg(name: string): string | undefined {
  const at = process.argv.indexOf(`--${name}`)
  return at >= 0 && at + 1 < process.argv.length ? process.argv[at + 1] : undefined
}

const env = readEnv()
if (env.production) {
  console.error('배포 환경에서는 돌지 않습니다')
  process.exit(1)
}

const handle = arg('handle') ?? `dev_${Date.now() % 100_000}`
const db = newDb(env.databaseUrl)

try {
  await migrate(db)
  // 같은 이름으로 다시 부르면 같은 계정입니다 — 화면을 고치는 동안 계정이 늘지 않습니다.
  const accountId = await accountFor(db, 'github', `dev:${handle}`)
  await setHandle(db, accountId, handle)
  const tokens = await startSession(db, env.jwtSecret, accountId, 'dev tool')

  console.log(JSON.stringify({
    accountId,
    handle,
    session: { access: tokens.access, refresh: tokens.refresh },
  }, null, 2))
  console.error('\n브라우저 콘솔에 넣습니다:')
  console.error(`localStorage.setItem('clover.session', '${
    JSON.stringify({ access: tokens.access, refresh: tokens.refresh })}')`)
} finally {
  await db.destroy()
}
