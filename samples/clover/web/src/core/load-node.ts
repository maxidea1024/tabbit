// Node 에서 테이블을 읽습니다. 헤드리스 러너와 테스트가 씁니다.
//
// **브라우저 쪽과 파일이 갈린 이유가 있습니다.** 생성 리더의 `readAllBytes` 가
// `require('fs')` 로 파일을 열고, ESM 모듈에는 `require` 가 없습니다 — 그래서 여기서 하나
// 놓아 줍니다. 그 한 줄을 `load.ts` 에 두면 브라우저 번들이 `module` 을 끌어오게 되므로,
// Node 전용인 이 파일에 둡니다.
//
// 자세한 것은 `doc/tool-findings.md` §6 입니다. 결함이 닫히면 이 파일이 없어집니다.

import { createRequire } from 'module'

import { CloverData } from '../generated/clover-data'
import { build, type Data } from './data'

const shim = globalThis as unknown as { require?: unknown }
if (typeof shim.require === 'undefined') shim.require = createRequire(import.meta.url)

/** `.tcb` 를 폴더에서 읽습니다. 참조 연결까지 생성 코드가 합니다. */
export function loadFromDisk(basePath: string): Data {
  const tables = new CloverData()
  tables.readAllBinarySync(basePath, '.tcb')
  return build(tables)
}
