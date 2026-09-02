// 순위 조회의 시간을 잽니다.
//
//     npx tsx tools/seed-fake.ts --accounts 10000
//     npx tsx tools/bench.ts
//
// **L2 의 판정이 이 수치입니다.** 사람이 만명일 때 어느 쪽 순위도 100ms 안에 나와야 합니다.

import { build } from '../src/app'
import { accountFor } from '../src/accounts'
import { startSession } from '../src/auth/session'

/** 길마다 몇 번 재는가. 첫 번째는 연결을 여는 값이 섞이므로 중앙값을 봅니다. */
const ROUNDS = 20

const PATHS = [
  '/boards',
  '/boards/ascent',
  '/boards/ascent?page=200',
  '/boards/ascent?around=me',
  '/boards/besthand?period=all',
  '/boards/fewesthands',
  '/me',
]

const { app, context } = await build()
const server = app.listen(0)
const address = server.address()
const port = typeof address === 'object' && address ? address.port : 0

const accountId = await accountFor(context.db, 'github', `bench-${Date.now()}`)
const { access } = await startSession(context.db, context.env.jwtSecret, accountId, 'bench')

const [row] = await context.db('profile').count<{ count: number }[]>('* as count')
console.log(`계정 ${Number(row.count).toLocaleString('en-US')}명\n`)
console.log('경로'.padEnd(32) + '중앙값'.padStart(10) + '최대'.padStart(10))
console.log('-'.repeat(52))

let worst = 0
for (const path of PATHS) {
  const times: number[] = []
  for (let round = 0; round < ROUNDS; round++) {
    const started = performance.now()
    const response = await fetch(`http://127.0.0.1:${port}${path}`,
                                 { headers: { authorization: `Bearer ${access}` } })
    await response.json()
    times.push(performance.now() - started)
  }
  times.sort((a, b) => a - b)
  const median = times[Math.floor(ROUNDS / 2)]
  const max = times[ROUNDS - 1]
  worst = Math.max(worst, median)
  console.log(path.padEnd(32)
    + `${median.toFixed(1)}ms`.padStart(10)
    + `${max.toFixed(1)}ms`.padStart(10))
}

console.log('-'.repeat(52))
console.log(worst < 100 ? `가장 느린 중앙값 ${worst.toFixed(1)}ms — 100ms 안입니다`
                        : `가장 느린 중앙값 ${worst.toFixed(1)}ms — 100ms 를 넘습니다`)

server.close()
await context.cache.quit()
await context.db.destroy()
process.exit(worst < 100 ? 0 : 1)
