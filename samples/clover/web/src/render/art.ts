// 그림.
//
// **그림은 있으면 쓰고 없으면 문양을 그립니다.** 202장을 한 번에 만들지 않으므로, 절반만
// 있는 상태에서도 화면이 돌아야 합니다 — 있는 것은 그림이고 없는 것은 `glyph.ts` 의 문양
// 입니다.
//
// 목록(`public/art/index.json`)을 먼저 읽습니다. 목록이 없으면 그림마다 없는 파일을 찾아
// 404를 내고, 콘솔이 그것으로 덮여 셰이더 오류를 못 보게 됩니다.

import { Assets, Texture } from 'pixi.js'

export type ArtKind = 'joker' | 'tarot' | 'planet' | 'spectral' | 'card' | 'tag' | 'boss'

/**
 * 그림이 있는 폴더.
 *
 * **트럼프는 갈래가 아니라 폴더입니다.** 카드 세트마다 한 벌이므로 `card` 하나로는 모자라고,
 * `CardSet.art_dir` 이 정하는 이름이 그대로 폴더가 됩니다 — 코어도 이 파일도 어느 세트가
 * 있는지 모릅니다.
 */
export type ArtDir = ArtKind | string

let base = './art'
const known = new Set<string>()
const ready = new Map<string, Texture>()
const loading = new Set<string>()
/** 그림이 새로 들어올 때마다 부릅니다. 화면이 그때 다시 그립니다. */
const listeners: (() => void)[] = []

/** 목록을 읽습니다. 없으면 그림이 하나도 없는 것으로 봅니다. */
export async function loadArtIndex(url = './art'): Promise<number> {
  base = url
  try {
    const response = await fetch(`${url}/index.json`)
    if (!response.ok) return 0
    const list = (await response.json()) as string[]
    for (const entry of list) known.add(entry)
  } catch {
    // 목록이 없는 것은 오류가 아닙니다. 문양으로 갑니다.
  }
  return known.size
}

/**
 * 그 갈래의 파일 확장자.
 *
 * **트럼프만 `png` 입니다.** 모서리가 둥근 투명 그림이라 그렇고, 나머지는 결이 있는 사각형
 * 그림이라 `png` 로 두면 장당 400KB 입니다 — 202장이면 77MB 이고, 화면에서 가장 크게 쓰이는
 * 자리는 88 × 124 입니다.
 *
 * **세트의 그림은 `webp` 입니다.** 정본 한 벌만 모서리까지 그려진 투명 그림이고, 우리가
 * 굽는 세트는 그림이 카드를 덮고 모서리를 화면이 그 위에 그리므로 투명할 곳이 없습니다 —
 * 그래서 `card` 하나만 `png` 이고 `card/cats` 는 아닙니다.
 */
function extensionOf(kind: ArtDir): string {
  return kind === 'card' ? 'png' : 'webp'
}

export function onArtReady(listener: () => void): void {
  listeners.push(listener)
}

/**
 * 이 식별자의 그림.
 *
 * 이미 읽어 둔 것만 돌려줍니다. 아직 없으면 읽기를 시작하고 `undefined` 를 냅니다 — 부르는
 * 쪽은 그동안 문양을 그리고, 다 읽히면 `onArtReady` 로 다시 그립니다.
 */
export function artFor(kind: ArtDir, id: string): Texture | undefined {
  const key = `${kind}/${id}`
  if (!known.has(key)) return undefined

  const have = ready.get(key)
  if (have) return have
  if (loading.has(key)) return undefined

  loading.add(key)
  void Assets.load<Texture>(`${base}/${key}.${extensionOf(kind)}`).then(texture => {
    loading.delete(key)
    ready.set(key, texture)
    for (const listener of listeners) listener()
  }).catch(() => {
    loading.delete(key)
    // 한 번 실패하면 다시 시도하지 않습니다. 문양으로 남습니다.
    known.delete(key)
  })

  return undefined
}

/** 소모품의 갈래 번호를 그림의 갈래 이름으로. */
export function artKindOf(kind: number): ArtKind {
  return kind === 2 ? 'planet' : kind === 3 ? 'spectral' : 'tarot'
}
