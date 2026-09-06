// 프레임 상한이 실제로 그 값을 내는가.
//
// **평균이 아니라 걸음을 봅니다.** 60을 그대로 적으면 3초에 59.3fps 로 평균은 멀쩡한데,
// 그 안에서 두 프레임이 화면 한 칸씩 늦습니다 — 사람이 알아채는 것은 평균이 아니라 그
// 늦는 한 칸입니다.
//
// 문턱(16.667ms)이 60Hz 화면 한 칸의 길이와 같은 자리에 놓여서 그렇습니다. 그래서
// `FRAME_CAPS` 가 60과 30이 아니라 62.5와 30.3 이고, 이 테스트가 그 둘을 지킵니다 —
// 값을 「보기 좋게」 60으로 되돌리면 여기서 걸립니다.
import { describe, expect, it } from 'vitest'
import { Ticker } from 'pixi.js'

import { FRAME_CAPS } from '../src/ui/options'

/** 화면이 이 주기로 깨울 때 몇 프레임이 그려지는가. */
function framesAt(cap: number, refreshHz: number, seconds = 2): number {
  const ticker = new Ticker()
  ticker.autoStart = false
  ticker.maxFPS = cap

  let drawn = 0
  ticker.add(() => { drawn++ })

  const step = 1000 / refreshHz
  const ticks = Math.round(refreshHz * seconds)
  for (let i = 1; i <= ticks; i++) ticker.update(i * step)

  ticker.destroy()
  return drawn / seconds
}

/** 앞의 것보다 늦게 온 프레임이 몇 개인가. **0 이어야 걸음이 고릅니다.** */
function latecomers(cap: number, refreshHz: number, seconds = 3): number {
  const ticker = new Ticker()
  ticker.autoStart = false
  ticker.maxFPS = cap

  const step = 1000 / refreshHz
  const at: number[] = []
  let now = 0
  ticker.add(() => { at.push(now) })
  const ticks = Math.round(refreshHz * seconds)
  for (let i = 1; i <= ticks; i++) { now = i * step; ticker.update(now) }
  ticker.destroy()

  const gaps: number[] = []
  for (let i = 1; i < at.length; i++) gaps.push(Math.round((at[i] - at[i - 1]) / step))
  const usual = Math.min(...gaps)
  return gaps.filter(one => one > usual).length
}

describe('프레임 상한', () => {
  it('무제한은 화면이 깨우는 대로입니다', () => {
    expect(framesAt(0, 60)).toBeCloseTo(60, 0)
    expect(framesAt(0, 120)).toBeCloseTo(120, 0)
  })

  it('60 자리는 어느 화면에서나 60입니다', () => {
    const cap = FRAME_CAPS[2]
    expect(framesAt(cap, 60)).toBeCloseTo(60, 0)
    expect(framesAt(cap, 120)).toBeCloseTo(60, 0)
  })

  it('30 자리는 어느 화면에서나 30입니다', () => {
    const cap = FRAME_CAPS[1]
    expect(framesAt(cap, 60)).toBeCloseTo(30, 0)
    expect(framesAt(cap, 120)).toBeCloseTo(30, 0)
  })

  it('고른 값은 한 칸도 거르지 않습니다', () => {
    // **이 줄이 값을 62.5·30.3 으로 둔 이유입니다.** 60과 30은 평균이 멀쩡한데 걸음이
    // 고르지 않습니다 — 늦는 칸이 하나라도 있으면 그것이 보입니다.
    expect(latecomers(FRAME_CAPS[2], 60)).toBe(0)
    expect(latecomers(FRAME_CAPS[2], 120)).toBe(0)
    expect(latecomers(FRAME_CAPS[1], 60)).toBe(0)
    expect(latecomers(60, 60)).toBeGreaterThan(0)
  })
})
