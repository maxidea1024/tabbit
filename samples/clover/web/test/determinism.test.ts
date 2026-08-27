// 결정론.
//
// **이 샘플의 판정 기준이 여기서 시작합니다.** 같은 시드와 같은 액션이 같은 해시를 내지
// 않으면, 유니티 쪽과 대조할 것이 없습니다.

import { describe, expect, it } from 'vitest'

import { applyBp, mulBp, MULT_ONE, sellValue } from '../src/core/units'
import { fnv1a64, Pcg32, streamRng } from '../src/core/rng'
import { autoplay, play } from '../src/headless'

describe('만분율', () => {
  it('×1.5 는 15000 입니다', () => {
    expect(mulBp(MULT_ONE, 15_000)).toBe(15_000)
    expect(mulBp(40_000, 15_000)).toBe(60_000)
  })

  it('내림은 음의 무한 방향입니다', () => {
    // 0 방향으로 내리면 -1 이 됩니다. 규격은 -2 입니다.
    expect(mulBp(-15_000, 10_001)).toBe(-15_002)
    expect(applyBp(-3, 5_000)).toBe(-2)
    expect(applyBp(3, 5_000)).toBe(1)
  })

  it('판매가는 절반을 내리고 최소 1 입니다', () => {
    expect(sellValue(5, 2, 1)).toBe(2)
    expect(sellValue(1, 2, 1)).toBe(1)
    expect(sellValue(10, 2, 1)).toBe(5)
  })
})

describe('난수', () => {
  it('같은 시드는 같은 수열을 냅니다', () => {
    const a = streamRng('CLOVER-0001', 'Shuffle')
    const b = streamRng('CLOVER-0001', 'Shuffle')
    for (let i = 0; i < 32; i++) expect(a.next()).toBe(b.next())
  })

  it('스트림이 다르면 수열이 다릅니다', () => {
    const shuffle = streamRng('CLOVER-0001', 'Shuffle')
    const shop = streamRng('CLOVER-0001', 'ShopSlot')
    const left = Array.from({ length: 16 }, () => shuffle.next())
    const right = Array.from({ length: 16 }, () => shop.next())
    expect(left).not.toEqual(right)
  })

  it('시드가 다르면 수열이 다릅니다', () => {
    const one = streamRng('CLOVER-0001', 'Shuffle')
    const two = streamRng('CLOVER-0002', 'Shuffle')
    expect(one.next()).not.toBe(two.next())
  })

  it('`below` 는 상한 아래만 냅니다', () => {
    const rng = streamRng('CLOVER-0001', 'Boss')
    for (let i = 0; i < 500; i++) {
      const value = rng.below(7)
      expect(value).toBeGreaterThanOrEqual(0)
      expect(value).toBeLessThan(7)
    }
  })

  it('상태를 저장하고 되돌리면 같은 수가 나옵니다', () => {
    const rng = streamRng('CLOVER-0001', 'Pack')
    for (let i = 0; i < 10; i++) rng.next()

    const saved = rng.save()
    const expected = Array.from({ length: 8 }, () => rng.next())
    const restored = Pcg32.restore(saved)
    expect(Array.from({ length: 8 }, () => restored.next())).toEqual(expected)
  })

  it('해시는 우리 것입니다', () => {
    // 값이 아니라 **성질**을 봅니다 — 같은 입력에 같은 값이고, 한 글자가 달라지면 달라집니다.
    expect(fnv1a64('clover')).toBe(fnv1a64('clover'))
    expect(fnv1a64('clover')).not.toBe(fnv1a64('clovel'))
  })
})

describe('리플레이', () => {
  it('같은 리플레이는 같은 해시를 냅니다', () => {
    const first = autoplay('CLOVER-0001', 'red_deck', 'White', 400)
    const second = play(first.replay)
    expect(second.finalHash).toBe(first.report.finalHash)
    expect(second.hashes).toEqual(first.report.hashes)
  })

  it('액션마다의 해시가 갈라진 지점을 가리킵니다', () => {
    const run = autoplay('CLOVER-0003', 'red_deck', 'White', 400)
    expect(run.report.hashes.length).toBe(run.report.actions + 1)
  })

  it('시드가 다르면 결과가 다릅니다', () => {
    const one = autoplay('CLOVER-0001', 'red_deck', 'White', 400)
    const two = autoplay('CLOVER-0002', 'red_deck', 'White', 400)
    expect(one.report.finalHash).not.toBe(two.report.finalHash)
  })

  it('덱이 다르면 결과가 다릅니다', () => {
    const red = autoplay('CLOVER-0001', 'red_deck', 'White', 400)
    const blue = autoplay('CLOVER-0001', 'blue_deck', 'White', 400)
    expect(red.report.finalHash).not.toBe(blue.report.finalHash)
  })
})
