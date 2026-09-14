// 손패 줄의 자리.
//
// **화면 없이 확인합니다.** 한 장마다 나아가는 거리는 장수 하나를 받는 순수 함수이고,
// 그 값이 지키는 것은 「언제나 겹친다」와 「줄이 판 자리를 넘지 않는다」 둘입니다.

import { describe, expect, it } from 'vitest'

import { BOARD_X, HAND_OVERLAP, HAND_SPAN, handSpacing, LEFT, PANEL_W } from '../src/game/metrics'
import { SIZE } from '../src/render/theme'

/** 그 장수의 손패가 차지하는 가로. */
function rowWidth(count: number): number {
  return (count - 1) * handSpacing(count) + SIZE.cardWidth
}

describe('손패 줄', () => {
  it('장수와 무관하게 겹칩니다', () => {
    // 2픽셀을 띄우고 나란히 놓이던 동안 그 줄은 쥔 패가 아니라 진열된 카드였습니다.
    for (let count = 2; count <= 16; count++) {
      expect(handSpacing(count), `${count}장`).toBeLessThan(SIZE.cardWidth)
    }
  })

  it('여덟 장은 겹침의 폭이 정합니다', () => {
    expect(handSpacing(8)).toBe(SIZE.cardWidth - HAND_OVERLAP)
  })

  it('장수가 늘면 가로가 겹침을 더 깊게 합니다', () => {
    // 바우처로 열두 장을 쥐는 판에서 겹침만으로 자리를 잡으면 줄이 화면의 변을 넘습니다.
    expect(handSpacing(12)).toBeLessThan(handSpacing(8))
    expect(handSpacing(12)).toBe(HAND_SPAN / 12)
  })

  it('줄이 왼쪽 판을 침범하지 않습니다', () => {
    for (let count = 1; count <= 16; count++) {
      const left = BOARD_X - rowWidth(count) / 2
      expect(left, `${count}장`).toBeGreaterThan(LEFT + PANEL_W)
    }
  })

  it('줄이 화면의 오른쪽 변을 넘지 않습니다', () => {
    for (let count = 1; count <= 16; count++) {
      expect(BOARD_X + rowWidth(count) / 2, `${count}장`).toBeLessThanOrEqual(SIZE.width)
    }
  })
})
