// Node 에서 테이블을 읽습니다. 헤드리스 러너와 서버와 테스트가 씁니다.
//
// **브라우저 쪽 `load.ts` 와 파일이 갈린 이유는 `fs` 입니다.** 디스크를 읽는 임포트는 브라우저
// 번들에 들어갈 수 없으므로, Node 전용인 이 파일에 둡니다. 생성 코드 자체는 어느 쪽도 가리지
// 않습니다 — 파일 이름을 받아 바이트를 돌려주는 함수를 넘기면 되고, 그 한 줄이 두 파일의
// 차이 전부입니다.

import * as fs from 'fs'
import * as path from 'path'

import { CloverData } from '../generated/clover-data'
import { build, type Data } from './data'

/** `.tcb` 를 폴더에서 읽습니다. 참조 연결까지 생성 코드가 합니다. */
export function loadFromDisk(basePath: string): Data {
  const tables = new CloverData()
  tables.readAllBinarySync(name => new Uint8Array(fs.readFileSync(path.join(basePath, name))))
  return build(tables)
}
