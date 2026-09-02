// 웹의 코어를 그대로 씁니다.
//
// **복사하지 않습니다.** 복사하면 둘이 갈라지고, 갈라지면 `invalid_action` 이 정직한
// 제출에서 납니다. `headless.ts` 가 하는 것과 같은 임포트이고, 데이터도 웹이 쓰는 `.tcb`
// 그대로입니다.

import * as path from 'path'
import { fileURLToPath } from 'url'

import { loadFromDisk } from '../../web/src/core/load-node'
import { poolsOf, type PoolChoice } from '../../web/src/core/pool'
import { fingerprintOf } from '../../web/tools/write-version'
import type { Data } from '../../web/src/core/data'

export type { Data }
export { poolsOf }
export type { PoolChoice }

const HERE = path.dirname(fileURLToPath(import.meta.url))

/**
 * 데이터를 한 번만 읽습니다.
 *
 * **읽는 데 시간이 듭니다.** 제출마다 다시 읽으면 재현 0.1초에 데이터 읽기가 얹힙니다 —
 * 데이터는 프로세스가 사는 동안 바뀌지 않으므로 한 번이면 됩니다.
 */
let cached: Data | undefined

export function loadData(dataPath: string): Data {
  if (!cached) cached = loadFromDisk(path.resolve(HERE, '..', dataPath))
  return cached
}

/** 이 빌드의 규칙 지문. */
export function buildFingerprint(replayPath: string): string {
  return fingerprintOf(path.resolve(HERE, '..', replayPath)).fingerprint
}

/** `base` 도 `all` 도 아닌 것은 받지 않습니다. */
export function isPoolChoice(value: unknown): value is PoolChoice {
  return value === 'base' || value === 'all'
}
