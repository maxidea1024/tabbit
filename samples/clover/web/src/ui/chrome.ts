// 화면의 부품 그림.
//
// **런타임에 도형을 그리지 않습니다.** 판·단추·칸·자리·키캡·게이지는 모양이 고정이므로
// 9분할 그림으로 굽고 스프라이트로 놓습니다. 실행 중에 남는 비용이 0 입니다.
//
// **9분할입니다.** 그림 하나를 네 귀·네 변·가운데로 갈라, 귀는 그대로 두고 변만 늘립니다.
// 판의 잘린 귀가 귀 조각 안에 구워져 있으므로 판이 아무리 커져도 컷이 같습니다.
//
// **흰 금속으로 구워 두고 겉면 색을 물들입니다.** 물들이기가 색을 곱하는 것이므로 원본에
// 색이 있으면 그 색이 섞여 겉면을 따라가지 못합니다. 가장 밝은 곳이 흰색에 가까워야
// 밝은 겉면에서도 밝은 부품이 나옵니다.
//
// **두 배로 굽고 절반으로 그립니다.** 1대1로 그리면 2배 밀도 화면에서 늘려 쓰게 되어
// 가장자리가 흐려집니다.
//
// 규격은 `design-data/tools/ui.py` 가 굽고 `atlas.ts` 에 적습니다. 여기서 다시 적지
// 않습니다.

import { Assets, NineSliceSprite, Sprite, Texture } from 'pixi.js'

import { ATLAS, BAKE_SCALE, RUNG, TORN_COUNT, type RungName, type Slice } from './atlas'

/** 쓰는 그림들. 파일 이름 그대로입니다. */
export type ChromeName = keyof typeof ATLAS

const ready = new Map<string, Texture>()

/**
 * 부품 그림을 미리 읽습니다.
 *
 * **화면을 세우기 전에 읽습니다.** 그리는 자리에서 읽기 시작하면 첫 프레임에 부품이 없는
 * 판이 한 번 보입니다.
 */
export async function loadChrome(base = './ui'): Promise<void> {
  await Promise.all(Object.keys(ATLAS).map(async name => {
    try {
      ready.set(name, await Assets.load<Texture>(`${base}/${name}.png`))
    } catch {
      // 없으면 그림 없이 갑니다. 부르는 쪽이 `undefined` 를 받고 지금까지의 길로 갑니다.
    }
  }))
}

export function chromeReady(name: ChromeName): boolean {
  return ready.has(name)
}

/**
 * 부품 하나를 그 크기로 놓습니다. 없으면 `undefined` 입니다.
 *
 * **크기는 만들 때 넘깁니다.** 뒤에서 `width` 로 넣으면 배율과 어느 쪽이 적용되는지가
 * 분명하지 않습니다 — 만들 때의 것은 격자이고 배율은 그 위에 걸립니다.
 *
 * 겉면 밖으로 나가는 그림자가 있는 부품은 그만큼 크게 놓고 그만큼 물러앉습니다. 그래야
 * 부르는 쪽이 적은 자리가 곧 겉면의 자리입니다.
 */
export function piece(name: ChromeName, width: number, height: number, tint?: number):
    NineSliceSprite | undefined {
  const texture = ready.get(name)
  if (texture === undefined) return undefined

  const cut: Slice = ATLAS[name]
  const pad = cut.pad
  const w = (width + pad * 2) / BAKE_SCALE
  const h = (height + pad * 2) / BAKE_SCALE
  // **귀 조각에는 여백까지 들어갑니다.** 여백을 빼고 자르면 귀 조각이 여백만 담고 얼굴의
  // 사선이 늘어나는 가운데 칸에 들어갑니다 — 단추의 잘린 귀가 가로로 늘어나 보였습니다.
  // 좁은 단추에서는 두 귀가 겹치지 않을 만큼만 잡습니다.
  const side = Math.min(cut.left + pad, (width + pad * 2) / 2)
  const sprite = new NineSliceSprite({
    texture,
    leftWidth: side / BAKE_SCALE,
    rightWidth: Math.min(cut.right + pad, (width + pad * 2) / 2) / BAKE_SCALE,
    topHeight: (cut.top + pad) / BAKE_SCALE,
    bottomHeight: (cut.bottom + pad) / BAKE_SCALE,
    width: w, height: h,
  })
  sprite.scale.set(BAKE_SCALE)
  sprite.position.set(-pad, -pad)
  if (tint !== undefined) sprite.tint = tint
  return sprite
}

/**
 * 있는 부품의 크기를 고칩니다.
 *
 * **부품은 한 번 만들고 고쳐 씁니다.** 상태가 바뀔 때마다 새로 만들면 새 그림의 자리가
 * 다음 프레임까지 정해지지 않아, 그 사이에 들어온 누름이 그 단추를 맞히지 못합니다 —
 * 가리키는 순간 단추가 새로 만들어지고 곧바로 누른 것이 빈자리 누름으로 처리되어 고른
 * 것을 놓았습니다.
 */
export function refit(sprite: NineSliceSprite, name: ChromeName, width: number, height: number): void {
  const cut: Slice = ATLAS[name]
  const pad = cut.pad
  sprite.width = (width + pad * 2) / BAKE_SCALE
  sprite.height = (height + pad * 2) / BAKE_SCALE
  sprite.position.set(-pad, -pad)
}

/**
 * 그 높이에 해당하는 단추의 칸.
 *
 * **계단 넷뿐입니다** — 36 · 48 · 60 · 72. 그 사이 값이 들어오면 가장 가까운 칸으로
 * 접힙니다. 높이를 부르는 자리에서 정하면 화면마다 갈라집니다.
 */
export function rungFor(height: number): RungName {
  let best: RungName = 'button'
  for (const name of Object.keys(RUNG) as RungName[]) {
    if (Math.abs(RUNG[name].height - height) < Math.abs(RUNG[best].height - height)) best = name
  }
  return best
}

/** 그 칸의 높이와 글자 크기. */
export function rungOf(name: RungName): { height: number; font: number; cut: number } {
  return RUNG[name]
}

/**
 * 테두리의 빛.
 *
 * **「무엇의 경계인가」를 알리는 변에만 둡니다.** 판이 시작되는 윗변 · 머리 판이 끝나는
 * 밑줄 · 물건 자리의 윗변 · 전면 화면의 제목 줄 · 큰 판의 머리띠 다섯입니다. 아무 변에나
 * 두면 화면이 번들거립니다.
 */
export function glowEdge(width: number, tint: number): NineSliceSprite | undefined {
  const sprite = piece('glow-edge', width, ATLAS['glow-edge'].h, tint)
  if (sprite !== undefined) sprite.blendMode = 'add'
  return sprite
}

/**
 * 카드의 뜯긴 가장자리 마스크 한 장. 없으면 `undefined` 입니다.
 *
 * **둥근 모서리 대신 뜯긴 변입니다.** 넷을 돌려 쓰므로 카드가 몇 장이든 비용이 같습니다 —
 * 어느 것을 쓸지는 그 카드를 가리키는 수에서 고릅니다. 같은 카드는 늘 같은 변입니다.
 */
export function tornTexture(pick: number): Texture | undefined {
  const index = ((Math.abs(Math.floor(pick)) % TORN_COUNT) + 1)
  return ready.get(`card-torn-${index}`)
}

/**
 * 그 크기로 늘린 마스크 스프라이트.
 *
 * 마스크로 걸거나(`node.mask`), 어둡게 물들여 그림자로 놓습니다 — 그림자도 뜯긴 변을
 * 따라가야 카드가 종이로 보입니다.
 */
export function tornSprite(pick: number, width: number, height: number): Sprite | undefined {
  const texture = tornTexture(pick)
  if (texture === undefined) return undefined
  const sprite = new Sprite(texture)
  sprite.width = width
  sprite.height = height
  return sprite
}
