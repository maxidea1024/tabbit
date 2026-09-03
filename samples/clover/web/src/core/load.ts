// 브라우저에서 테이블을 읽습니다.
//
// **Node 쪽은 `load-node.ts` 입니다.** 갈린 이유가 그 파일에 적혀 있습니다. 부르는 메서드는
// 같은 접근자의 것이고, 다른 것은 넘기는 로더 하나입니다 — 여기서는 `fetch`.

import { CloverData } from '../generated/clover-data'
import { build, type Data } from './data'

/** 브라우저에서. 게임이 씁니다. */
export async function loadFromUrl(basePath: string): Promise<Data> {
  const tables = new CloverData()

  await tables.readAllBinary(async name => {
    const response = await fetch(`${basePath}/${name}`)
    if (!response.ok) throw new Error(`${name} 를 읽지 못했습니다: ${response.status}`)
    return new Uint8Array(await response.arrayBuffer())
  })

  return build(tables)
}
