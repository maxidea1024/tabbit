// 리플레이를 다시 굽습니다.
//
// **굽는 명령이 없었습니다.** 리플레이 13개는 손으로 `headless.ts` 를 열세 번 불러 만든
// 것이었고, 그래서 코어가 바뀐 뒤에 다시 굽는 사람이 없었습니다 — `verify.py` 가 13개 전부
// 다른 해시를 낸다고 보고하는 상태로 열 번 넘는 커밋이 지나갔습니다. 다시 굽는 것이 한
// 줄이면 다시 굽습니다.
//
//     npx tsx tools/bake-replays.ts
//     npx tsx tools/bake-replays.ts --check    # 굽지 않고 어긋난 것만 보고합니다
//
// **무엇을 구울지는 이미 구워진 것이 정합니다.** 시드와 덱과 스테이크가 그 파일 안에 있으므로
// 목록을 따로 들고 있을 필요가 없고, 목록을 들고 있으면 파일과 어긋납니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { autoplay, type Replay } from '../src/headless'
import type { Metrics } from '../src/core/metrics'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const REPLAYS = path.resolve(HERE, '../../design-data/out/replay')

/**
 * 자동 진행의 상한.
 *
 * **끝날 때까지 갑니다.** 지금 구워진 것 중 가장 긴 것이 143수이고, 200은 그것이 자라도
 * 닿지 않을 자리입니다 — 상한에 걸려 끊긴 리플레이는 「여기까지가 이 판이다」가 아니라
 * 「여기서 그만 셌다」이므로 대조의 뜻이 옅어집니다.
 */
const LIMIT = 200

function main(argv: string[]): number {
  const checkOnly = argv.includes('--check')
  const names = fs.readdirSync(REPLAYS).filter(name => name.endsWith('.json')).sort()

  let moved = 0
  for (const name of names) {
    const at = path.join(REPLAYS, name)
    const was = JSON.parse(fs.readFileSync(at, 'utf8')) as Replay
    const { replay, report } = autoplay(was.seed, was.deck, was.stake, LIMIT)

    const before = was.hashes?.[was.hashes.length - 1] ?? '(없음)'
    const after = report.finalHash
    // **지표도 함께 봅니다.** 상태가 같아도 지표를 세는 셈이 바뀌면 순위가 달라지고,
    // 해시만 보면 그것이 지나갑니다.
    const same = before === after && sameMetrics(was.metrics, report.metrics)
    if (!same) moved++

    if (!same && !checkOnly) {
      fs.writeFileSync(at, JSON.stringify(replay, null, 2) + '\n', 'utf8')
    }

    const mark = same ? '=' : checkOnly ? '!' : '→'
    console.log(`  ${mark} ${name.padEnd(26)} ${before}  ${mark}  ${after}`
      + `   ${report.phase} 안테 ${report.ante} 액션 ${report.actions}`)
  }

  if (moved === 0) {
    console.log(`\n${names.length}개 모두 그대로입니다`)
    return 0
  }
  console.log(checkOnly
    ? `\n${moved} / ${names.length} 이 어긋납니다. 다시 구우려면 --check 없이 부릅니다`
    : `\n${moved} / ${names.length} 을 다시 구웠습니다`)
  // 다시 구운 것은 성공입니다. 어긋난 것을 알리기만 한 `--check` 만 실패로 끝납니다.
  return checkOnly ? 1 : 0
}

/** 지표가 그대로인가. 적혀 있지 않던 것은 어긋난 것으로 봅니다 — 채워 넣어야 합니다. */
function sameMetrics(was: Metrics | undefined, now: Metrics): boolean {
  if (!was) return false
  return (Object.keys(now) as (keyof Metrics)[]).every(key => was[key] === now[key])
}

process.exitCode = main(process.argv.slice(2))
