// 판의 테두리 그림.
//
// **테는 하나의 조각입니다.** 얇은 헤어라인 테에 장식을 따로 얹어 보았고 — 9분할 액자 ·
// ㄱ자 꺾쇠 · 둥근 볼트 · 네모 리벳판 — 넷 다 붙여 놓은 것으로 보였습니다. 띠와 귀의
// 장식이 같은 두께와 같은 조명으로 함께 그려져 있어야 한 물건이 됩니다.
//
// **9분할입니다.** 그림 하나를 네 귀 · 네 변 · 가운데로 갈라, 귀는 그대로 두고 변만
// 늘립니다. 자를 자리는 띠의 두께가 아니라 **귀의 리벳판이 차지하는 크기**입니다 — 띠로
// 잡으면 리벳판이 귀 밖으로 나가 늘어나며 뭉개집니다.
//
// **흰 금속으로 구워 두고 겉면 색을 물들입니다.** 물들이기는 색을 곱하는 것이므로 원본에
// 색이 있으면 그 색이 섞여 겉면을 따라가지 못합니다. 가장 밝은 곳이 흰색에 가까워야
// 밝은 겉면에서도 밝은 테가 나옵니다.
//
// **두 배로 굽고 절반으로 그립니다.** 1대1로 그리면 2배 밀도 화면에서 늘려 쓰게 되어
// 테가 흐려집니다.

import { Assets, NineSliceSprite, Texture } from 'pixi.js'

import { UI } from '../render/theme'

/** 쓰는 그림들. 파일 이름 그대로입니다. */
export type ChromeName = 'panel-frame'

const NAMES: ChromeName[] = ['panel-frame']

/**
 * 그림을 화면에 얼마로 줄여 그리는가.
 *
 * **두 배 해상도로 굽고 절반으로 그립니다.** 1대1로 그리면 화면이 2배 밀도일 때 그림을
 * 늘려 쓰게 되어 테가 흐려집니다.
 */
/** 자를 자리. **귀의 리벳판 크기입니다** — 띠는 26px 이고 판은 39px 입니다. */
const SLICE = 39

const SCALE: Record<ChromeName, number> = {
  'panel-frame': 0.5,
}

const ready = new Map<ChromeName, Texture>()

/**
 * 테두리 그림을 미리 읽습니다.
 *
 * **화면을 세우기 전에 읽습니다.** 그리는 자리에서 읽기 시작하면 첫 프레임에 테가 없는
 * 판이 한 번 보입니다.
 */
export async function loadChrome(base = './ui'): Promise<void> {
  await Promise.all(NAMES.map(async name => {
    try {
      ready.set(name, await Assets.load<Texture>(`${base}/${name}.png`))
    } catch {
      // 없으면 그림 없이 갑니다. 판은 지금까지의 테로 그려집니다.
    }
  }))
}

export function chromeReady(name: ChromeName): boolean {
  return ready.has(name)
}

/**
 * 판의 테 하나. 없으면 `undefined` 입니다.
 *
 * **판 경계에 걸쳐 놓습니다.** 판 안의 자리를 빼앗지 않는 것이 `skin.ts` 의 조건이므로
 * 바깥으로 `FRAME_OUT` 만큼 나가고, 안쪽은 판이 이미 가진 여백에서 끝납니다.
 */
export function cornerPiece(width: number, height: number, tint: number):
    NineSliceSprite | undefined {
  const texture = ready.get('panel-frame')
  if (texture === undefined) return undefined

  const scale = SCALE['panel-frame']
  const w = width + FRAME_OUT * 2
  const h = height + FRAME_OUT * 2
  // **크기는 만들 때 넘깁니다.** 뒤에서 `width` 로 넣으면 배율과 어느 쪽이 적용되는지가
  // 분명하지 않습니다 — 만들 때의 것은 격자이고 배율은 그 위에 걸립니다.
  const sprite = new NineSliceSprite({
    texture,
    leftWidth: SLICE, topHeight: SLICE, rightWidth: SLICE, bottomHeight: SLICE,
    width: w / scale, height: h / scale,
  })
  sprite.scale.set(scale)
  sprite.position.set(-FRAME_OUT, -FRAME_OUT)
  sprite.tint = tint
  return sprite
}

/**
 * 판때기의 테가 판 밖으로 나가는 양.
 *
 * **테의 두께(38픽셀)보다 훨씬 작습니다.** 왼쪽 판이 화면의 x=4 에 서므로 13픽셀을 다 밖으로
 * 내면 9픽셀이 화면 밖으로 잘립니다. 경계에 걸쳐 놓으면 바깥 4픽셀로 화면에 들어오고,
 * 안쪽 9픽셀은 판이 이미 가진 12픽셀 여백 안에서 끝납니다 — 글자리는 그대로입니다.
 */
export const FRAME_OUT = 6

/**
 * 테를 물들이는 색. **판에서 뽑습니다.**
 *
 * 강조색(`panelEdge`)으로 물들여 보았고 겉면이 바뀌어도 테가 주황이었습니다 — 뜻이 있는
 * 색은 색상각이 고정이고 밝기만 판을 따라가기 때문입니다(`doc/ui.md` 의 겉면 절).
 *
 * **판을 밝히면 그 겉면의 금속이 됩니다.** 겉면 8개가 판의 색상각으로 갈리므로 테도 함께
 * 갈립니다. 그림이 흰 금속이므로 곱하기 하나로 끝납니다.
 */
export function frameTint(): number {
  // **테와 같은 색입니다.** 판 색에서 뽑아 보았고 회색 덩어리가 네 귀에 붙은 것으로
  // 보였습니다 — 꺾쇠는 테를 두껍게 하는 것이므로 테와 딴 색이면 딴 물건이 됩니다.
  return UI.panelEdge
}
