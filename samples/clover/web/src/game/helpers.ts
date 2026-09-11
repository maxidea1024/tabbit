// 어디서나 쓰는 작은 것들.
//
// **상태를 읽지 않습니다.** 인자만 받아 값을 내놓으므로 어느 부분에서 불러도 같은
// 답이고, 그래서 어느 부분에도 속하지 않습니다.

import { BlurFilter, Graphics } from 'pixi.js'
import { PAINT } from '../render/ink'
import { mix } from '../render/skin'
import { UI } from '../render/theme'

/**
 * 순위 글 안에서 숫자가 있는 자리.
 *
 * **글이 6개 언어이므로 자리를 수로 찾습니다.** 「등정 #412 ↑12」 에서 첫 `#숫자` 하나가
 * 굴러 내려가는 값이고, 그 앞뒤의 말은 언어마다 다릅니다.
 */
export const RANK_MARK = /#\d+/

/** 줄바꿈. 문자열 안에 그대로 적으면 이 파일을 고치는 도구들이 자꾸 끊어 놓습니다. */
export const NEWLINE = String.fromCharCode(10)

/**
 * 가장자리 픽셀을 늘려 쓰는 흐림 하나.
 *
 * **생성 옵션에 없는 값입니다.** `repeatEdgePixels` 는 프로퍼티로만 있고, 그것을 세우면
 * 여백을 다시 셈해 0 으로 둡니다.
 *
 * 해상도는 렌더러를 받은 뒤 `layout` 이 정합니다.
 */
export function edgeBlur(): BlurFilter {
  const one = new BlurFilter({ strength: 0, quality: 3, resolution: 0.5 })
  one.repeatEdgePixels = true
  return one
}

/**
 * 흐림을 굽는 해상도. **화면 해상도의 절반입니다.**
 *
 * `0.5` 를 못박아 두었더니 **핸드폰에서 흐림이 뭉개졌습니다.** 필터의 `resolution` 은
 * 비율이 아니라 절대값이고, 화면은 픽셀 밀도만큼 — 핸드폰은 2에서 3 — 굽습니다. 그래서
 * 0.5 는 데스크탑에서 2분의 1이지만 핸드폰에서는 **4분의 1에서 6분의 1**이었습니다.
 *
 * 뭉갠 그림 위에 판이 떠 있다가 판이 사라질 때 필터를 놓으면, 그 순간 화면이 뭉갠 것에서
 * 온전한 것으로 한 프레임에 돌아옵니다 — 흐림이 잦아드는 것이 아니라 뚝 끊기는 것으로
 * 보이던 까닭입니다.
 *
 * 절반으로 두면 텍셀이 어느 기계에서나 4분의 1이므로 값싼 것은 그대로이고, 놓을 때의
 * 차이는 데스크탑에서와 같은 만큼입니다.
 */
export function blurResolution(rendered: number): number {
  return Math.max(0.5, Math.min(1.5, rendered * 0.5))
}

/**
 * 칩·배수 상자가 밝을 때의 채움색.
 *
 * **짙게 눌러 씁니다.** 원색 그대로는 흰 숫자가 눌러앉지 못합니다.
 */
/**
 * 가장 나중에 받은 것. **번호가 가장 큰 것입니다.**
 *
 * 줄의 끝으로 보면 안 됩니다 — 자리를 갈아 끼우는 것(`swap`)은 판 그 자리에 들어오므로
 * 끝이 아닙니다.
 */
export function newest<T extends { uid: number }>(rows: readonly T[]): T | undefined {
  let found: T | undefined
  for (const one of rows) if (!found || one.uid > found.uid) found = one
  return found
}

export function boxInk(tint: number): number {
  return mix(tint, UI.outline, 0.52)
}

/**
 * 작은 것 위에 얹는 흰 빛.
 *
 * **셰이더 대신입니다.** 카드에 쓰는 `ArriveFilter` 는 카드 크기에 맞춰 여백을 잡아 두어서
 * 작은 것에 걸면 그림이 밀립니다 — 26픽셀짜리에 필요한 것은 왜곡이 아니라 밝아짐 하나이고,
 * 그것은 흰 원 하나를 얹는 것으로 됩니다.
 */
export function glare(size: number, strength: number): Graphics {
  const lit = new Graphics()
  lit.circle(size / 2, size / 2, size / 2).fill({ color: PAINT.sheen, alpha: strength })
  lit.blendMode = 'add'
  return lit
}

/** 점 하나가 어느 자리를 가운데로 하는 네모 안에 있는가. */
export function inBox(point: { x: number; y: number }, cx: number, cy: number,
               width: number, height: number): boolean {
  return Math.abs(point.x - cx) <= width / 2 && Math.abs(point.y - cy) <= height / 2
}

/**
 * 점 하나가 이 뷰 위에 있는가.
 *
 * **쉬는 자리와 그려진 자리를 둘 다 요구합니다.** 쉬는 자리만 보면 아직 오지도 않은
 * 카드가 잡히고 — 용수철의 목적지는 배치가 바뀌는 그 프레임에 갈아 끼워지므로 카드는
 * 아직 화면 저쪽에 있습니다 — 그려진 자리만 보면 앞을 지나가는 카드가 차례로 잡힙니다.
 * 둘 다 요구하면 **쉬는 자리가 커서 밑이고 실제로 거기 그려져 있을 때**만 잡힙니다.
 */
export function near(point: { x: number; y: number },
              motion: { x: { target: number; value: number }
                        y: { target: number; value: number } },
              width: number, height: number): boolean {
  return inBox(point, motion.x.target, motion.y.target, width, height)
    && inBox(point, motion.x.value, motion.y.value, width, height)
}

/** 색 하나를 셰이더가 받는 0..1 셋으로. */
export function rgbOf(color: number): [number, number, number] {
  return [
    ((color >> 16) & 0xff) / 255,
    ((color >> 8) & 0xff) / 255,
    (color & 0xff) / 255,
  ]
}

/**
 * 로그인 화면을 건너뛰고 열라고 적혀 있는가.
 *
 * **도구를 위한 것입니다.** 화면을 눌러 판을 두는 도구 50여 개가 저마다 로그인 화면을
 * 지나야 할 이유가 없습니다 — 「계정 없이 시작」을 누른 것과 같은 자리이므로, 사람이 이
 * 주소로 열어도 게임이 하는 일은 그 단추를 누른 것과 같습니다.
 *
 * 주소의 `?guest=1` 과 `window.__cloverGuest` 둘입니다. 도구는 주소를 저마다 짓기 때문에
 * 페이지를 열기 전에 표시 하나를 심는 쪽이 한 줄로 끝납니다.
 */
export function guestBoot(): boolean {
  if ((globalThis as { __cloverGuest?: boolean }).__cloverGuest === true) return true
  try {
    return new URLSearchParams(location.search).get('guest') === '1'
  } catch {
    // 주소를 읽을 수 없는 자리에서는 로그인 화면부터입니다.
    return false
  }
}
