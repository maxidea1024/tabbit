// 규칙 지문을 씁니다.
//
//     npx tsx tools/write-version.ts
//     npx tsx tools/write-version.ts --check   # 쓰지 않고 어긋난 것만 보고합니다
//
// **지문은 구워 둔 리플레이에서 나옵니다.** 코어의 소스 해시는 주석 하나에 바뀌고 데이터의
// 해시는 조커 그림 하나에 바뀝니다 — 둘 다 규칙이 그대로인데 지문이 달라지고, 그러면
// 정직한 제출이 전부 거부됩니다. 리플레이의 해시와 지표는 **규칙이 바뀔 때만** 달라집니다.
//
// 지표를 함께 담는 이유는 하나입니다. 지표를 세는 셈만 바뀌면 상태 해시는 그대로이고
// 순위만 달라지는데, 그때도 클라이언트와 서버가 갈라진 것입니다.
//
// 규격은 `doc/leaderboard/submission.md` 의 「규칙 지문」입니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { fnv1a32 } from '../src/core/hash'
import type { Replay } from '../src/headless'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPLAYS = path.resolve(HERE, '../../design-data/out/replay')
const OUT = path.resolve(HERE, '../public/data/version.json')

export interface Version {
  /** 이 빌드의 규칙 지문. 제출마다 서버의 것과 견줍니다. */
  fingerprint: string
  /** 지문에 들어간 리플레이 수. 값이 아니라 사람이 읽는 표시입니다. */
  replays: number
}

/**
 * 구워 둔 리플레이에서 지문을 냅니다.
 *
 * **이름 순서가 규격입니다.** 파일이 늘어도 늘어난 자리에 끼어들 뿐이고, 순서가 흔들리면
 * 규칙이 그대로인데 지문이 달라집니다.
 */
export function fingerprintOf(dir: string): Version {
  const names = fs.readdirSync(dir).filter(name => name.endsWith('.json')).sort()

  const parts: string[] = []
  for (const name of names) {
    const replay = JSON.parse(fs.readFileSync(path.join(dir, name), 'utf8')) as Replay
    const hash = replay.hashes?.[replay.hashes.length - 1]
    const metrics = replay.metrics
    if (hash === undefined || metrics === undefined) {
      throw new Error(`${name} 에 해시나 지표가 없습니다. 먼저 bake-replays 를 부릅니다`)
    }
    // 지표는 열쇠를 정렬해 적습니다 — 필드가 늘어난 자리 때문에 지문이 달라지지 않습니다.
    const sealed = Object.keys(metrics).sort()
      .map(key => `${key}=${String((metrics as unknown as Record<string, unknown>)[key])}`).join(',')
    parts.push(`${name}:${hash}:${sealed}`)
  }

  const fingerprint = fnv1a32(parts.join('\n')).toString(16).padStart(8, '0')
  return { fingerprint, replays: names.length }
}

function main(argv: string[]): number {
  const checkOnly = argv.includes('--check')
  const now = fingerprintOf(REPLAYS)
  const text = JSON.stringify(now, null, 2) + '\n'

  const was = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : ''
  if (was === text) {
    console.log(`지문 ${now.fingerprint} · 리플레이 ${now.replays}개 — 그대로입니다`)
    return 0
  }

  if (checkOnly) {
    const old = was === '' ? '(없음)' : (JSON.parse(was) as Version).fingerprint
    console.log(`지문이 어긋납니다: ${old} → ${now.fingerprint}`)
    console.log('다시 쓰려면 --check 없이 부릅니다')
    return 1
  }

  fs.writeFileSync(OUT, text, 'utf8')
  console.log(`지문 ${now.fingerprint} · 리플레이 ${now.replays}개 — 다시 썼습니다`)
  return 0
}

// 임포트해서 쓸 때는 돌지 않습니다 — 게이트가 `fingerprintOf` 만 부릅니다.
if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(fileURLToPath(import.meta.url))) {
  process.exitCode = main(process.argv.slice(2))
}
