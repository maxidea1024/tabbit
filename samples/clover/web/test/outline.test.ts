// 글자에 두르는 테두리의 굵기.
//
// **픽셀 서체에는 테두리가 없습니다.** 한글·영어·독일어는 물마루이고, 반 픽셀의 테두리가
// 격자에 맞춘 획을 무너뜨립니다 — 그 말들은 0 이어야 합니다.
//
// **한자를 쓰는 말은 굵기가 그 글자의 획 사이 틈보다 좁아야 합니다.** 넓으면 획 사이가
// 메워져 검게 뭉치고, 한자는 같은 크기 안에 획이 서너 배 들어가 틈이 좁습니다 — 그래서
// 말마다 배수가 다르고, 그 순서를 여기서 고정합니다.

import { describe, expect, it } from 'vitest'

import { LANGUAGES, setLanguage } from '../src/core/strings'
import { outline, outlined, outlineWidth } from '../src/ui/font'

describe('테두리 굵기', () => {
  it('픽셀 서체를 쓰는 말에는 테두리가 없습니다', () => {
    for (const language of ['ko', 'en', 'de'] as const) {
      setLanguage(language)
      expect(outlineWidth(24)).toBe(0)
      expect(outlineWidth(24, true)).toBe(0)
    }
  })

  it('번체는 일본어보다 촘촘합니다', () => {
    setLanguage('ja')
    const ja = outlineWidth(24)
    expect(ja).toBeGreaterThan(0)
    setLanguage('zh-Hans')
    expect(outlineWidth(24)).toBe(ja)
    setLanguage('zh-Hant')
    expect(outlineWidth(24)).toBeLessThan(ja)
  })

  it('실제로 쓰는 글자의 하위 10% 틈을 넘지 않습니다', () => {
    // `design-data/out/font-chars.json` 의 글자 전부를 8배로 구워 획 사이 틈을 세고,
    // 글자마다의 중앙값을 모아 하위 10% 를 본 값입니다. 굵기가 이것을 넘으면 열에 하나
    // 넘는 글자에서 획 사이가 메워집니다.
    const gap: Record<string, Record<number, number>> = {
      ko: { 15: 0.75, 17: 0.88, 23: 1.25, 34: 2.00 },
      ja: { 15: 0.75, 17: 0.88, 23: 1.13, 34: 1.88 },
      'zh-Hans': { 15: 0.75, 17: 0.88, 23: 1.25, 34: 1.88 },
      'zh-Hant': { 15: 0.63, 17: 0.75, 23: 1.00, 34: 1.63 },
      en: { 15: 1.13, 17: 1.38, 23: 1.75, 34: 2.63 },
      de: { 15: 1.13, 17: 1.38, 23: 1.75, 34: 2.63 },
    }
    // 픽셀 서체를 쓰는 말은 0 이므로 어느 틈보다도 좁습니다.
    for (const language of LANGUAGES) {
      setLanguage(language)
      for (const size of [15, 17, 23, 34]) {
        expect(outlineWidth(size)).toBeLessThanOrEqual(gap[language][size])
      }
    }
  })

  it('숫자 글꼴의 틈도 넘지 않습니다', () => {
    // 숫자 12자를 같은 방법으로 잰 최솟값입니다. 한자를 쓰는 말에서만 테두리가 있습니다.
    setLanguage('ja')
    for (const [size, gap] of [[15, 1.13], [17, 1.38], [23, 1.75], [34, 2.75]] as const) {
      expect(outlineWidth(size, true)).toBeLessThanOrEqual(gap)
    }
  })

  it('크기에 비례합니다', () => {
    setLanguage('ja')
    expect(outlineWidth(48)).toBeCloseTo(outlineWidth(24) * 2)
  })

  it('숫자 글꼴은 한자를 쓰는 말 사이에서 같습니다', () => {
    setLanguage('ja')
    const latin = outlineWidth(24, true)
    setLanguage('zh-Hant')
    expect(outlineWidth(24, true)).toBe(latin)
  })

  it('이음매가 round 입니다', () => {
    // 기본값 miter 는 miterLimit 이 10이라 예각에서 굵기의 10배까지 뻗습니다.
    setLanguage('ja')
    expect(outline(15, 0x000000).join).toBe('round')
    expect(outline(15, 0x000000).miterLimit).toBe(2)
  })

  it('크기와 테두리가 함께 나옵니다', () => {
    setLanguage('ko')
    const style = outlined(17, 0x0a0f18)
    expect(style.fontSize).toBe(17)
    expect(style.stroke.width).toBe(outlineWidth(17))
  })
})
