// 연출.
//
// **타임라인을 만드는 것은 순수 함수입니다.** 그래서 화면 없이 값을 확인할 수 있고, 그것이
// 만드는 쪽과 재생하는 쪽을 가른 이유입니다.

import { describe, expect, it } from 'vitest'

import { PokerHandKind } from '../src/generated/enums/poker-hand-kind'
import {
  buildTimeline, intensityOf, scaleOf, semitonesOf, shakeOf, timelineLength, TimelinePlayer,
  type Beat, type Feel,
} from '../src/render/juice'
import type { GameEvent } from '../src/core/state'

const FEEL: Feel = {
  scoreStepMs: 120,
  jokerStepMs: 140,
  retriggerStepMs: 90,
  handLabelMs: 180,
  multiplyMs: 400,
  settleMs: 300,
  moneyStepMs: 260,
  hitStopMs: 120,
  fastForwardScale: 4,
  shakeMaxPx: 12,
  shakeThresholdMult: 200_000,
  shakeMaxMult: 3_000_000,
  numberScaleMaxBp: 16_000,
  pitchMaxSemitones: 12,
  particleMax: 30,
  chromaticMaxPx: 2,
  cardHoverLiftPx: 12,
  cardHoverTiltDeg: 6,
  drawStaggerMs: 40,
  drawLandMs: 200,
  flipStaggerMs: 25,
  playStaggerMs: 90,
  playLandMs: 260,
  tagGainMs: 760,
  tagUseMs: 300,
}

const HAND: GameEvent = {
  t: 'HandEvaluated', hand: PokerHandKind.Pair, level: 1,
  chips: 10, mult: 20_000, cards: [1, 2],
}

function scored(uid: number, chips: number): GameEvent {
  return { t: 'CardScored', uid, op: 'AddChips', chips, mult: 0, money: 0, source: 'rank' }
}

describe('타임라인', () => {
  it('이벤트가 차례로 놓입니다', () => {
    const beats = buildTimeline([HAND, scored(1, 2), scored(2, 2)], FEEL)

    expect(beats).toHaveLength(3)
    expect(beats[0].at).toBe(0)
    expect(beats[1].at).toBe(FEEL.handLabelMs)
    expect(beats[2].at).toBe(FEEL.handLabelMs + FEEL.scoreStepMs)
  })

  it('값이 바뀐 것은 자기 시간을 쓰지 않습니다', () => {
    const withChange = buildTimeline(
      [HAND, { t: 'ChipsMultChanged', chips: 12, mult: 40_000 }, scored(1, 2)], FEEL)
    const without = buildTimeline([HAND, scored(1, 2)], FEEL)

    expect(timelineLength(withChange)).toBe(timelineLength(without))
  })

  it('값이 바뀌면 그 앞 박자의 세기가 올라갑니다', () => {
    const quiet = buildTimeline([HAND, scored(1, 2)], FEEL)
    const loud = buildTimeline(
      [HAND, { t: 'ChipsMultChanged', chips: 12, mult: 2_000_000 }, scored(1, 2)], FEEL)

    expect(quiet[0].intensity).toBe(0)
    expect(loud[0].intensity).toBeGreaterThan(0.5)
  })
})

describe('세기', () => {
  it('문턱 아래는 0 입니다', () => {
    expect(intensityOf(10_000, FEEL)).toBe(0)
    expect(intensityOf(FEEL.shakeThresholdMult, FEEL)).toBe(0)
  })

  it('상한 위는 1 입니다', () => {
    expect(intensityOf(FEEL.shakeMaxMult * 10, FEEL)).toBe(1)
  })

  it('로그입니다 — 배수 3 과 300 이 같지 않습니다', () => {
    const low = intensityOf(300_000, FEEL)
    const high = intensityOf(3_000_000, FEEL)
    expect(low).toBeGreaterThan(0)
    expect(high).toBe(1)
    expect(high - low).toBeGreaterThan(0.3)
  })

  it('세기가 화면 값으로 갑니다', () => {
    expect(shakeOf(0, FEEL)).toBe(0)
    expect(shakeOf(1, FEEL)).toBe(FEEL.shakeMaxPx)
    expect(scaleOf(0, FEEL)).toBe(1)
    expect(scaleOf(1, FEEL)).toBeCloseTo(FEEL.numberScaleMaxBp / 10_000)
    expect(semitonesOf(1, FEEL)).toBe(FEEL.pitchMaxSemitones)
  })
})

describe('재생', () => {
  it('시각이 되면 박자를 냅니다', () => {
    const seen: Beat[] = []
    const player = new TimelinePlayer(beat => seen.push(beat))
    player.play(buildTimeline([HAND, scored(1, 2), scored(2, 2)], FEEL))

    player.advance(10)
    expect(seen).toHaveLength(1)

    player.advance(FEEL.handLabelMs)
    expect(seen).toHaveLength(2)

    player.advance(FEEL.scoreStepMs)
    expect(seen).toHaveLength(3)
    expect(player.busy).toBe(false)
  })

  it('빠르게 넘기기가 배속을 올립니다', () => {
    const seen: Beat[] = []
    const player = new TimelinePlayer(beat => seen.push(beat))
    player.play(buildTimeline([HAND, scored(1, 2), scored(2, 2)], FEEL))

    player.hurry(FEEL)
    player.advance(FEEL.handLabelMs)
    expect(seen.length).toBeGreaterThanOrEqual(3)
  })

  it('두 번 누르면 즉시 끝냅니다', () => {
    const seen: Beat[] = []
    const player = new TimelinePlayer(beat => seen.push(beat))
    player.play(buildTimeline([HAND, scored(1, 2), scored(2, 2)], FEEL))

    player.hurry(FEEL)
    player.hurry(FEEL)
    expect(seen).toHaveLength(3)
    expect(player.busy).toBe(false)
  })

  it('빗나간 조커도 박자를 가집니다', () => {
    const beats = buildTimeline([
      HAND,
      { t: 'JokerFizzled', slot: 0, jokerId: 'trade_card', num: 1, den: 2 },
    ], FEEL)

    // **보여주지 않으면 그 조커가 무엇을 하는지 배우지 못합니다.**
    expect(beats).toHaveLength(2)
    expect(beats[1].hold).toBe(FEEL.jokerStepMs)
  })
})
