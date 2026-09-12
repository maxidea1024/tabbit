// 그림.
//
// **그림은 늦게 닿습니다.** 부탁한 그 프레임에는 없고, 읽히면 `onArtReady` 로 알립니다 —
// 그 사이에 그리는 쪽은 스켈레톤을 둡니다(`render/skeleton.ts`).
//
// **대신 세울 무늬를 두지 않습니다.** 식별자에서 뽑은 문양을 그 자리에 세웠던 적이 있고,
// 그림 파일이 하나도 없던 동안은 그것이 맞았습니다. 지금은 갈래마다 그림이 다 있으므로
// 그 문양이 보이는 때는 「아직 안 닿은 1초」뿐이고, 그 1초에 그 물건의 것이 아닌 무늬가
// 놓여 있으면 사람은 그것을 그 물건의 그림으로 읽습니다.
//
// 목록(`public/art/index.json`)을 먼저 읽습니다. 목록이 없으면 그림마다 없는 파일을 찾아
// 404를 내고, 콘솔이 그것으로 덮여 셰이더 오류를 못 보게 됩니다.
//
// **화면에 그려지는 크기로 풉니다.** 파일은 320 × 480 이고 일부는 640 × 960 인데, 화면에서
// 가장 크게 그려지는 자리는 88 × 124 에 배율을 곱한 것입니다 — 핸드폰(배율 1.2)에서
// 106 × 149, 큰 데스크탑(배율 3)에서 264 × 372 입니다. 파일 크기 그대로 GPU 에 올리면 조커
// 500장이 442MB 이고 그림 전부가 645MB 인데, 푸는 자리에서 줄이면 핸드폰에서 전부 80MB
// 남짓입니다. **그래서 전부 들고 있어도 됩니다** — 놓고 다시 푸는 일이 없어지고, 그것이
// 도감의 조커 탭을 굴릴수록 무거워지다 앱이 끝나던 원인이었습니다.
//
// **Pixi 의 `Assets` 를 지나지 않습니다.** 푸는 크기를 정하려면 `createImageBitmap` 의
// `resizeHeight` 를 우리가 넘겨야 하고, `Assets` 는 그 자리를 열어 두지 않습니다. 놓는 것도
// 우리가 합니다 — `Assets.unload` 가 캐시를 지운 뒤에도 읽기 약속을 한 번 더 기다렸다가
// 지우는 틈에 같은 주소를 다시 부탁하면 곧 버려질 그 그림을 그대로 돌려주었고, 그것이
// 화면을 까맣게 굳히던 원인이었습니다. 우리 손에 있으면 그 틈이 없습니다.
//
// **상한은 남겨 둡니다.** 화면 크기로 풀면 넘칠 일이 없지만, 그림이 더 늘거나 밀도가 더
// 높은 기계가 오는 날의 안전판입니다. 넘치면 오래 전에 부탁받은 것부터 놓고, 다시 필요하면
// 다시 풉니다.

import { ImageSource, Texture } from 'pixi.js'
import { coarsePointer } from '../shader/device'
import { SIZE } from './theme'

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
 * 화면 크기로 풀면 핸드폰(배율 1.2)에서 그림 전부가 80MB 남짓, 큰 데스크탑(배율 3)에서
 * 400MB 남짓입니다. 둘 다 그 안에 다 들어가는 값이고, 그래서 이 상한은 평소에 걸리지
 * 않습니다 — 걸리는 날은 그림이 늘었거나 밀도가 더 높은 기계가 온 날이고, 그때는 오래 전에
 * 부탁받은 것부터 놓습니다.
 *
 * **핸드폰이 더 낮습니다.** WebView 의 렌더러는 GPU 자리가 넘치면 소리 없이 끝납니다 —
 * 96MB 상한을 두기 전에 285MB 에서 실제로 그랬습니다.
 */
const BUDGET = (coarsePointer() ? 160 : 512) * 1024 * 1024

/**
 * 놓은 그림을 실제로 버리기까지 기다리는 틱 수.
 *
 * **놓는 것과 버리는 것이 다른 순간이어야 합니다.** 놓는 순간에 버리면, 그 그림을 쓰고
 * 있던 스프라이트가 다음에 다시 그려질 때까지 없는 텍스처를 가리킵니다 — 그리는 쪽은
 * `onArtReady` 를 받아 다시 그리지만, 판은 들어온 것을 0.1초 모아서 한 번에 다시
 * 그리므로(`game.ts`) 그 모으는 시간보다 길어야 합니다. 30틱은 120Hz 에서 0.25초입니다.
 */
const RETIRE_TICKS = 30

/**
 * 화면에서 가장 크게 그려지는 자리에 얹는 여유.
 *
 * 가리키면 1.1배로 커지고(`TIP_GROW`), 카드가 늘 조금 기울어 있어 그림이 화면의 픽셀과
 * 어긋납니다 — 딱 맞춰 풀면 그 둘에서 흐려집니다. 카드 앞면을 배율보다 한 단 높게 굽는
 * 것과 같은 까닭입니다.
 */
const HEADROOM = 1.25

/**
 * 푼 그림의 세로 상한. **화면의 배율이 정합니다** — `setArtDensity` 가 갱신합니다.
 *
 * 처음 값은 배율 1입니다. 화면을 세우는 `layout` 이 그림을 부탁하기 전에 실제 배율로
 * 바꾸므로, 이 값으로 풀리는 그림은 없습니다.
 */
let decodeHeight = Math.ceil(SIZE.jokerHeight * HEADROOM)

interface Held {
  texture: Texture
  /** 푼 그림. 놓을 때 닫아야 그 메모리가 그 자리에서 풀립니다. */
  bitmap: ImageBitmap
  /** GPU 에서 차지하는 크기. */
  bytes: number
  /** 마지막으로 부탁받은 때. 넘칠 때 오래된 것부터 놓습니다. */
  used: number
}

let base = './art'
const known = new Set<string>()
const ready = new Map<string, Held>()
const loading = new Set<string>()
/**
 * 그림이 들어오거나 놓일 때마다 부릅니다. 화면이 그때 다시 그립니다.
 *
 * **어느 그림인지를 넘깁니다.** 받는 쪽이 자기가 쓰는 것인지 가릴 수 있어야 합니다 —
 * 넘기지 않으면 도감을 굴리는 중에 도착한 조커 그림 하나가 옵션 판과 상점을 함께 다시
 * 그리게 합니다.
 *
 * **들어온 것과 놓은 것을 가리지 않고 알립니다.** 받는 쪽이 해야 하는 일이 둘 다 같기
 * 때문입니다 — 그 열쇠의 그림을 쓰는 자리를 다시 그리는 것입니다. 들어온 것이면 스켈레톤이
 * 그림으로 바뀌고, 놓은 것이면 그림이 스켈레톤으로 돌아가며 **버려질 그림을 가리키지 않게
 * 됩니다.**
 *
 * **열쇠를 보고 거르는 쪽은 놓은 것도 받아야 합니다.** 걸러 놓고 들어온 것만 처리하면,
 * 놓인 그림을 쓰던 자리가 버려진 그림을 가리킨 채로 남습니다. 예외는 원본을 들고 있지
 * 않은 쪽 — 구워서 쓰는 도감 — 뿐입니다.
 */
const listeners: ((key: string, gone: boolean) => void)[] = []

/** 지금 들고 있는 크기의 합. */
let heldBytes = 0
/** 부탁받은 차례. 값 자체에는 뜻이 없고 큰 쪽이 최근입니다. */
let clock = 0
/** 지금까지 흐른 틱. `RETIRE_TICKS` 를 세는 데만 씁니다. */
let frame = 0
/** 놓았지만 아직 버리지 않은 것. **다시 부탁받으면 여기서 되살립니다.** */
const retiring: { key: string; held: Held; at: number }[] = []

/** 목록을 읽습니다. 없으면 그림이 하나도 없는 것으로 봅니다. */
export async function loadArtIndex(url = './art'): Promise<number> {
  base = url
  try {
    const response = await fetch(`${url}/index.json`)
    if (!response.ok) return 0
    const list = (await response.json()) as string[]
    for (const entry of list) known.add(entry)
  } catch {
    // 목록이 없는 것은 오류가 아닙니다. 스켈레톤으로 갑니다.
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

/**
 * @param listener 그 열쇠의 그림이 들어왔거나(`gone` 이 거짓) 놓였습니다(참). **놓인 것을
 *   흘려도 되는 쪽은 그 그림을 들고 있지 않은 쪽뿐입니다** — 구워서 쓰는 도감이 그렇습니다.
 */
export function onArtReady(listener: (key: string, gone: boolean) => void): void {
  listeners.push(listener)
}

function tell(key: string, gone: boolean): void {
  for (const listener of listeners) listener(key, gone)
}

/**
 * 화면의 배율. **푸는 크기가 이것을 따릅니다.**
 *
 * `layout` 이 글씨를 굽는 배율(`textScale`)을 그대로 넘깁니다 — 글씨와 카드 앞면과 그림이
 * 한 배율이어야 한 화면에서 어느 하나만 흐리거나 또렷하지 않습니다.
 *
 * **더 촘촘해지면 다 놓습니다.** 이미 푼 것은 낮은 배율의 크기이므로 그대로 두면 창을 키운
 * 뒤로 그림만 흐릿합니다. 덜 촘촘해지는 쪽은 그대로 둡니다 — 큰 것을 작게 그리는 것은
 * 흐려지지 않습니다.
 */
export function setArtDensity(density: number): void {
  const next = Math.ceil(SIZE.jokerHeight * Math.min(3, Math.max(1, density)) * HEADROOM)
  if (next <= decodeHeight) return
  const grew = next > decodeHeight * 1.2
  decodeHeight = next
  if (grew && ready.size > 0) dropAllArt()
}

/** 지금 푸는 세로 상한. **검증 도구가 푼 그림이 이보다 크지 않은지 봅니다.** */
export function artDecodeHeight(): number {
  return decodeHeight
}

/**
 * 이 식별자의 그림.
 *
 * 이미 읽어 둔 것만 돌려줍니다. 아직 없으면 읽기를 시작하고 `undefined` 를 냅니다 — 부르는
 * 쪽은 그동안 스켈레톤을 두고, 다 읽히면 `onArtReady` 로 다시 그립니다.
 */
export function artFor(kind: ArtDir, id: string): Texture | undefined {
  const key = `${kind}/${id}`
  if (!known.has(key)) return undefined

  const have = ready.get(key)
  if (have) {
    // **바탕이 없는 것은 내주지 않습니다.** 버리는 것이 이 파일 안에서만 일어나므로 여기
    // 남을 길은 없지만, 남았다면 그것을 내주는 것이 곧 까만 화면입니다 — 표에서 빼고 새로 풉니다.
    if (have.texture.destroyed) {
      ready.delete(key)
      heldBytes -= have.bytes
    } else {
      have.used = ++clock
      return have.texture
    }
  }
  // **놓았지만 아직 버리지 않은 것은 되살립니다.** 다시 푸는 것보다 싸고, 그 그림을 아직
  // 들고 있는 스프라이트가 있으면 그것도 그대로 맞습니다.
  const back = retiring.findIndex(one => one.key === key)
  if (back >= 0) {
    const [one] = retiring.splice(back, 1)
    one.held.used = ++clock
    ready.set(key, one.held)
    heldBytes += one.held.bytes
    return one.held.texture
  }
  if (loading.has(key)) return undefined

  loading.add(key)
  void decode(`${base}/${key}.${extensionOf(kind)}`).then(bitmap => {
    loading.delete(key)
    const source = new ImageSource({
      resource: bitmap,
      // Pixi 의 그림 읽기와 같은 값입니다. 푸는 쪽도 그쪽과 같이 기본값으로 풉니다.
      alphaMode: 'premultiply-alpha-on-upload',
      resolution: 1,
    })
    const texture = new Texture({ source })
    const bytes = bitmap.width * bitmap.height * 4
    ready.set(key, { texture, bitmap, bytes, used: ++clock })
    heldBytes += bytes
    // **놓은 것도 함께 알립니다.** 놓는 것과 버리는 것이 떨어져 있는 것은 그 사이에 받는
    // 쪽이 다시 그려 그 그림을 놓으라는 뜻입니다 — 놓은 열쇠를 알리지 않으면 받는 쪽은
    // 자기가 그 그림을 쓰고 있다는 것을 알 길이 없습니다.
    tell(key, false)
    for (const one of trim()) tell(one, true)
  }).catch(() => {
    loading.delete(key)
    // 한 번 실패하면 다시 시도하지 않습니다. 스켈레톤으로 남습니다.
    known.delete(key)
  })

  return undefined
}

/**
 * 파일을 읽어 화면 크기로 풉니다.
 *
 * **두 번에 풉니다.** 파일의 크기를 모르므로 한 번 푼 뒤 그 높이를 보고, 상한보다 크면 그
 * 그림에서 상한 높이로 다시 뽑고 큰 것을 닫습니다 — 폭은 비율을 지켜 따라옵니다. 작은
 * 것(태그와 보스의 256)은 키우지 않습니다. 파일 머리에서 크기를 읽어 한 번에 푸는 길도
 * 있지만 형식마다 다르게 적혀 있고, 그것이 어긋나는 날 그림이 통째로 안 나옵니다.
 *
 * 크게 푼 것은 이 함수 안에서만 살고 닫히므로, 폰에서 열 장이 한꺼번에 풀려도 잠깐입니다.
 */
async function decode(url: string): Promise<ImageBitmap> {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`${response.status} ${url}`)
  const full = await createImageBitmap(await response.blob())
  if (full.height <= decodeHeight) return full
  try {
    const small = await createImageBitmap(full, {
      resizeHeight: decodeHeight, resizeQuality: 'high',
    })
    full.close()
    return small
  } catch {
    // 줄여 푸는 것을 모르는 브라우저입니다. 크게 푼 것을 그대로 씁니다.
    return full
  }
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
function trim(): string[] {
  if (heldBytes <= BUDGET) return []

  const dropped: string[] = []
  const order = [...ready].sort((one, other) => one[1].used - other[1].used)
  for (const [key, held] of order) {
    if (heldBytes <= BUDGET) break
    ready.delete(key)
    heldBytes -= held.bytes
    retiring.push({ key, held, at: frame })
    dropped.push(key)
  }
  return dropped
}

/** 그림 하나를 실제로 버립니다. GPU 의 것과 푼 것을 함께 놓습니다. */
function discard(held: Held): void {
  held.texture.destroy(true)
  held.bitmap.close()
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
    discard(one.held)
  }
}

/**
 * 들고 있는 것을 전부 놓습니다. **상한이 넘쳤을 때와 같은 길입니다.**
 *
 * 화면의 배율이 더 촘촘해졌을 때 이 파일이 스스로 부르고, 검증 도구도 부릅니다 — 넘치는
 * 자리는 그림을 수백 장 읽고 나서야 오므로 도구가 거기까지 가지 않는데, 놓인 그림을 쓰고
 * 있던 쪽이 다시 그리지 않으면 그 카드는 그대로 빈 채로 남습니다. **그 자리를 여기서
 * 만듭니다.**
 */
export function dropAllArt(): string[] {
  const dropped: string[] = []
  for (const [key, held] of ready) {
    retiring.push({ key, held, at: frame })
    dropped.push(key)
  }
  ready.clear()
  heldBytes = 0
  for (const one of dropped) tell(one, true)
  return dropped
}

/** 지금 들고 있는 그림의 크기. **검증 도구가 이것으로 상한이 도는지 봅니다.** */
export function artBytes(): number {
  return heldBytes
}

/** 이 기계의 상한. 검증 도구가 `artBytes` 를 이것과 견줍니다. */
export function artBudget(): number {
  return BUDGET
}

/** 들고 있는 그림 가운데 가장 높은 것의 세로. **검증 도구가 화면 크기로 풀렸는지 이것으로 봅니다.** */
export function artTallest(): number {
  let tallest = 0
  for (const held of ready.values()) tallest = Math.max(tallest, held.bitmap.height)
  return tallest
}

/** 소모품의 갈래 번호를 그림의 갈래 이름으로. */
export function artKindOf(kind: number): ArtKind {
  return kind === 2 ? 'planet' : kind === 3 ? 'spectral' : 'tarot'
}
