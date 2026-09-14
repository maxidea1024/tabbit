// 덱 더미의 두께.
//
// **화면이 없어도 확인할 수 있습니다.** 두께를 정하는 것은 남은 장수 하나를 받는 순수
// 함수이고, 이 값이 화면의 오른쪽 변을 넘지 않는 것이 그 함수의 유일한 제약입니다.

import { describe, expect, it } from 'vitest'

import { deckSheets, DECK_SHEETS, DECK_X } from '../src/game/metrics'
import { SIZE } from '../src/render/theme'

describe('덱 더미의 두께', () => {
  it('가득 찬 덱이 가장 두껍습니다', () => {
    expect(deckSheets(52, 52)).toBe(DECK_SHEETS)
  })

  it('뽑을수록 얇아집니다', () => {
    const steps = [52, 40, 30, 20, 10, 4].map(left => deckSheets(left, 52))
    for (let i = 1; i < steps.length; i++) expect(steps[i]).toBeLessThanOrEqual(steps[i - 1])
    expect(steps[0]).toBeGreaterThan(steps[steps.length - 1])
  })

  it('남아 있는 동안은 두 장 아래로 얇아지지 않습니다', () => {
    // **한 장이 되면 더미가 아니라 낱장입니다.** 그러면 손패의 카드와 같은 물건으로
    // 보입니다.
    for (let left = 1; left <= 52; left++) expect(deckSheets(left, 52)).toBeGreaterThanOrEqual(2)
  })

  it('다 뽑으면 한 장입니다', () => {
    expect(deckSheets(0, 52)).toBe(1)
  })

  it('가장 두꺼울 때도 화면의 오른쪽 변을 넘지 않습니다', () => {
    // 밑장은 한 장마다 오른쪽으로 2픽셀씩 비켜섭니다.
    const rightMost = DECK_X + SIZE.cardWidth / 2 + (DECK_SHEETS - 1) * 2
    expect(rightMost).toBeLessThanOrEqual(SIZE.width)
  })

  it('덱의 크기가 달라도 비율로 셉니다', () => {
    // 덱은 카드를 사서 늘어나고 팔아서 줄어듭니다. 52장으로 못박으면 60장 덱의 더미가
    // 가득 찼을 때도 가득 차 보이지 않습니다.
    expect(deckSheets(60, 60)).toBe(DECK_SHEETS)
    expect(deckSheets(30, 60)).toBe(deckSheets(26, 52))
  })
})
