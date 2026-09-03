// 자리 잡기.
//
// **Pixi 에는 레이아웃이 없습니다.** 그리는 것이라 `x`·`y`·`pivot` 과 Sprite·Text 의
// `anchor` 가 전부이고, 유니티의 `RectTransform` 이나 언리얼의 앵커·정렬 같은 것이 없습니다.
// 그래서 자리를 상수로 적게 되고, 칸 하나의 높이를 고치면 그 아래의 상수를 손으로 전부
// 따라 고쳐야 합니다 — 한 곳을 빠뜨리면 그 칸만 어긋납니다.
//
// 여기 있는 것은 그 손일을 대신하는 **최소한**입니다. 사각형을 나누고 그 안에 붙이는 것
// 둘뿐이고, 흐름 배치(flexbox)나 자동 크기 조정은 없습니다 — 그것이 필요해지면 그때
// `@pixi/layout` 같은 것을 들여올 자리입니다.

import { Container, Text } from 'pixi.js'

/** 사각형 하나. 자리와 크기입니다. */
export interface Box {
  x: number
  y: number
  width: number
  height: number
}

/**
 * 사각형 안의 한 자리.
 *
 * **0 에서 1 로 적습니다** — `x` 는 0 이 왼쪽이고 1 이 오른쪽, `y` 는 0 이 위이고 1 이
 * 아래입니다. 픽셀이 아니라 비율이므로 사각형이 커지거나 작아져도 뜻이 같습니다.
 */
export interface Anchor {
  x: number
  y: number
}

export const TOP_LEFT: Anchor = { x: 0, y: 0 }
export const TOP: Anchor = { x: 0.5, y: 0 }
export const TOP_RIGHT: Anchor = { x: 1, y: 0 }
export const LEFT: Anchor = { x: 0, y: 0.5 }
export const CENTER: Anchor = { x: 0.5, y: 0.5 }
export const RIGHT: Anchor = { x: 1, y: 0.5 }
export const BOTTOM_LEFT: Anchor = { x: 0, y: 1 }
export const BOTTOM: Anchor = { x: 0.5, y: 1 }
export const BOTTOM_RIGHT: Anchor = { x: 1, y: 1 }

export function box(x: number, y: number, width: number, height: number): Box {
  return { x, y, width, height }
}

/** 사각형을 안으로 줄입니다. 하나만 주면 네 변 모두입니다. */
export function inset(one: Box, pad: number,
                      right = pad, bottom = pad, left = right): Box {
  return {
    x: one.x + left,
    y: one.y + pad,
    width: one.width - left - right,
    height: one.height - pad - bottom,
  }
}

/**
 * 가로로 나눕니다. 무게의 비율대로입니다.
 *
 * `gap` 은 나눈 것들 **사이**의 빈 자리이고, 그만큼을 빼고 나눕니다 — 그러지 않으면 사이를
 * 둔 만큼 전체가 넘칩니다.
 */
export function splitX(one: Box, weights: readonly number[], gap = 0): Box[] {
  const total = weights.reduce((sum, w) => sum + w, 0)
  const room = one.width - gap * (weights.length - 1)
  const out: Box[] = []
  let at = one.x
  for (const weight of weights) {
    const width = (room * weight) / total
    out.push({ x: at, y: one.y, width, height: one.height })
    at += width + gap
  }
  return out
}

/** 세로로 나눕니다. `splitX` 와 같은 규칙입니다. */
export function splitY(one: Box, weights: readonly number[], gap = 0): Box[] {
  const total = weights.reduce((sum, w) => sum + w, 0)
  const room = one.height - gap * (weights.length - 1)
  const out: Box[] = []
  let at = one.y
  for (const weight of weights) {
    const height = (room * weight) / total
    out.push({ x: one.x, y: at, width: one.width, height })
    at += height + gap
  }
  return out
}

/** 그 사각형 안의 한 자리. 붙일 것 없이 좌표만 필요할 때 씁니다. */
export function pointOf(one: Box, at: Anchor): { x: number; y: number } {
  return { x: one.x + one.width * at.x, y: one.y + one.height * at.y }
}

/**
 * 글을 사각형 안에 붙입니다.
 *
 * **Text 는 자기 `anchor` 를 가지므로 크기를 물어볼 필요가 없습니다** — 글이 바뀌어 넓어져도
 * 붙인 자리가 그대로입니다. 오른쪽 정렬이 「오른쪽 끝에서 왼쪽으로 자란다」 가 되는 것이
 * 이것이고, 크기를 재서 좌표를 계산하면 글이 바뀔 때마다 다시 재야 합니다.
 */
export function putText(node: Text, one: Box, at: Anchor,
                        offset: { x?: number; y?: number } = {}): void {
  node.anchor.set(at.x, at.y)
  const spot = pointOf(one, at)
  node.position.set(spot.x + (offset.x ?? 0), spot.y + (offset.y ?? 0))
}

/**
 * 크기를 아는 것을 사각형 안에 붙입니다.
 *
 * **크기를 받습니다.** `node.width` 는 자식이 그려진 만큼이라, 아직 안 그린 것이나 그림자가
 * 딸린 것에서는 그 값이 뜻하는 바가 달라집니다 — 판이 스스로 아는 크기를 넘기는 편이
 * 어긋나지 않습니다.
 */
export function put(node: Container, one: Box, at: Anchor,
                    size: { width: number; height: number },
                    offset: { x?: number; y?: number } = {}): void {
  node.position.set(
    one.x + (one.width - size.width) * at.x + (offset.x ?? 0),
    one.y + (one.height - size.height) * at.y + (offset.y ?? 0))
}

/**
 * 검증 도구가 짚을 한 자리. 어느 컨테이너의 어디인가입니다.
 *
 * **화면 좌표로 바꾸는 것은 화면을 띄운 쪽이 합니다.** 판이 어디에 서는지는 그쪽이 알고,
 * 그리는 쪽은 자기 안에서의 자리만 압니다.
 */
export interface ToolSpot {
  node: Container
  cx: number
  cy: number
}
