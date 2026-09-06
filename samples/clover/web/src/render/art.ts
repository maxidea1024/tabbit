// 그림.
//
// **그림은 있으면 쓰고 없으면 문양을 그립니다.** 전부를 한 번에 만들지 않으므로, 절반만
// 있는 상태에서도 화면이 돌아야 합니다 — 있는 것은 그림이고 없는 것은 `glyph.ts` 의 문양
// 입니다.
//
// 목록(`public/art/index.json`)을 먼저 읽습니다. 목록이 없으면 그림마다 없는 파일을 찾아
// 404를 내고, 콘솔이 그것으로 덮여 셰이더 오류를 못 보게 됩니다.
//
// **들고 있는 양에 상한이 있습니다.** 부탁받은 것을 읽어 두기만 하고 놓지 않으면, 도감을
// 끝까지 굴린 것만으로 파일 전부가 GPU 에 올라갑니다 — 파일로는 15MB 남짓이지만 GPU 에서는
// 픽셀마다 4바이트라 그 수십 배입니다. 손전화의 WebView 는 그 앞에서 끝납니다.

import { Assets, Texture } from 'pixi.js'

export type ArtKind = 'joker' | 'tarot' | 'planet' | 'spectral' | 'card' | 'tag' | 'boss'
  | 'pack'

/**
 * 그림이 있는 폴더.
 *
 * **트럼프는 갈래가 아니라 폴더입니다.** 카드 세트마다 한 벌이므로 `card` 하나로는 모자라고,
 * `CardSet.art_dir` 이 정하는 이름이 그대로 폴더가 됩니다 — 코어도 이 파일도 어느 세트가
 * 있는지 모릅니다.
 */
export type ArtDir = ArtKind | string

/**
 * 들고 있어도 되는 그림의 크기. **GPU 에 올라간 크기로 셉니다.**
 *
 * 파일은 압축되어 있지만 GPU 에 올라간 것은 픽셀마다 4바이트입니다 — 320 × 480 한 장이
 * 614KB 이고, 목록을 끝까지 굴리면 그런 것이 수백 장입니다. **상한이 없으면 그것이
 * 그대로 쌓입니다.**
 *
 * 한 화면이 한 번에 쓰는 것은 30장 남짓(약 18MB)이므로, 이 값은 그보다 몇 곱절 넉넉합니다.
 */
const BUDGET = 96 * 1024 * 1024

/**
 * 놓은 그림을 실제로 버리기까지 기다리는 틱 수.
 *
 * **놓는 것과 버리는 것이 다른 순간이어야 합니다.** 놓는 순간에 버리면, 그 그림을 쓰고
 * 있던 스프라이트가 다음에 다시 그려질 때까지 없는 텍스처를 가리킵니다 — 그리는 쪽은
 * `onArtReady` 를 받아 다시 그리지만 그것이 다음 틱이므로, 그 사이에 한 프레임이 있습니다.
 */
const RETIRE_TICKS = 2

interface Held {
  texture: Texture
  /** 읽어 온 자리. 놓을 때 `Assets` 에도 알려야 합니다. */
  url: string
  /** GPU 에서 차지하는 크기. */
  bytes: number
  /** 마지막으로 부탁받은 때. 넘칠 때 오래된 것부터 놓습니다. */
  used: number
}

let base = './art'
const known = new Set<string>()
const ready = new Map<string, Held>()
const loading = new Set<string>()
/** 그림이 새로 들어올 때마다 부릅니다. 화면이 그때 다시 그립니다. */
const listeners: (() => void)[] = []

/** 지금 들고 있는 크기의 합. */
let heldBytes = 0
/** 부탁받은 차례. 값 자체에는 뜻이 없고 큰 쪽이 최근입니다. */
let clock = 0
/** 지금까지 흐른 틱. `RETIRE_TICKS` 를 세는 데만 씁니다. */
let frame = 0
/** 놓았지만 아직 버리지 않은 것. */
const retiring: { texture: Texture; url: string; at: number }[] = []

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
  if (have) {
    have.used = ++clock
    return have.texture
  }
  if (loading.has(key)) return undefined

  const url = `${base}/${key}.${extensionOf(kind)}`
  loading.add(key)
  void Assets.load<Texture>(url).then(texture => {
    loading.delete(key)
    const bytes = texture.source.pixelWidth * texture.source.pixelHeight * 4
    ready.set(key, { texture, url, bytes, used: ++clock })
    heldBytes += bytes
    trim()
    for (const listener of listeners) listener()
  }).catch(() => {
    loading.delete(key)
    // 한 번 실패하면 다시 시도하지 않습니다. 문양으로 남습니다.
    known.delete(key)
  })

  return undefined
}

/**
 * 넘친 만큼 오래된 것부터 놓습니다.
 *
 * **놓는 것은 지우는 것이 아닙니다.** 여기서는 목록에서 빼고 「버릴 것」에 옮겨만 두고,
 * 실제로 버리는 것은 `artTick` 이 몇 틱 뒤에 합니다 — 그 사이에 `onArtReady` 를 받은
 * 쪽이 다시 그려 그 그림을 놓습니다.
 *
 * **화면에 있는 것은 거의 걸리지 않습니다.** 보이는 것은 다시 그릴 때마다 부탁받으므로
 * 차례가 늘 최근이고, 오래된 쪽은 지나쳐 온 것들입니다.
 */
function trim(): void {
  if (heldBytes <= BUDGET) return

  const order = [...ready].sort((one, other) => one[1].used - other[1].used)
  for (const [key, one] of order) {
    if (heldBytes <= BUDGET) break
    ready.delete(key)
    heldBytes -= one.bytes
    retiring.push({ texture: one.texture, url: one.url, at: frame })
  }
}

/**
 * 한 틱.
 *
 * **놓은 것을 실제로 버리는 자리입니다.** 화면이 매 틱 부르고, `RETIRE_TICKS` 가 지난
 * 것만 버립니다.
 */
export function artTick(): void {
  frame++
  while (retiring.length > 0 && frame - retiring[0].at >= RETIRE_TICKS) {
    const one = retiring.shift()
    if (!one) break
    void Assets.unload(one.url).catch(() => undefined)
  }
}

/** 지금 들고 있는 그림의 크기. **검증 도구가 이것으로 상한이 도는지 봅니다.** */
export function artBytes(): number {
  return heldBytes
}

/** 소모품의 갈래 번호를 그림의 갈래 이름으로. */
export function artKindOf(kind: number): ArtKind {
  return kind === 2 ? 'planet' : kind === 3 ? 'spectral' : 'tarot'
}
