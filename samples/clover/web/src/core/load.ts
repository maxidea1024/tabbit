// 브라우저에서 테이블을 읽습니다.
//
// **Node 쪽은 `load-node.ts` 입니다.** 갈린 이유가 그 파일에 적혀 있습니다. 읽는 바이트는
// 같습니다 — `readBinaryFrom` 이 `Uint8Array` 위에서 돌기 때문입니다.

import { CloverData } from '../generated/clover-data'
import { build, type Data } from './data'

/** 브라우저에서. 게임이 씁니다. */
export async function loadFromUrl(basePath: string): Promise<Data> {
  const tables = new CloverData()
  const names = tableNames(tables)

  await Promise.all(names.map(async name => {
    const response = await fetch(`${basePath}/${name}.tcb`)
    if (!response.ok) throw new Error(`${name}.tcb 를 읽지 못했습니다: ${response.status}`)
    const bytes = new Uint8Array(await response.arrayBuffer())
    const table = (tables as unknown as Record<string, { readBinaryFrom(data: Uint8Array): void }>)[
      lowerFirst(name)]
    table.readBinaryFrom(bytes)
  }))

  return build(tables)
}

/**
 * 읽어야 할 테이블의 이름.
 *
 * 접근자가 가진 테이블 프로퍼티에서 뽑습니다 — 목록을 손으로 적으면 테이블을 하나 더할
 * 때마다 여기가 뒤처집니다.
 */
function tableNames(tables: CloverData): string[] {
  const proto = Object.getPrototypeOf(tables) as object
  return Object.getOwnPropertyNames(proto)
    .filter(name => {
      const descriptor = Object.getOwnPropertyDescriptor(proto, name)
      return descriptor?.get !== undefined && name !== 'constructor'
    })
    .map(upperFirst)
}

function upperFirst(text: string): string {
  return text.charAt(0).toUpperCase() + text.slice(1)
}

function lowerFirst(text: string): string {
  return text.charAt(0).toLowerCase() + text.slice(1)
}
