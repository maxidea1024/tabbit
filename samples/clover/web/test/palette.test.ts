// 겉면의 대비.
//
// **색을 손으로 고르지 않는 것이 요점이고, 이 게이트가 그것을 지킵니다.** 겉면 하나가
// 색상각·채도·밝기 셋이고 나머지는 `palette.ts` 의 배수 표에서 나오므로, 배수를 하나
// 고치면 여덟 겉면이 함께 움직입니다 — 그 움직임이 어느 겉면에서 어긋나는지는 눈으로
// 보이지 않습니다.
//
// 손으로 고르던 동안 실제로 이랬습니다.
//
// |무엇|그때|
// |--|--|
// |단추와 잠긴 단추|1.19~1.31. **켜진 단추가 잠긴 단추와 같은 색이었습니다**|
// |단추와 가리킨 단추|1.30~1.36|
// |판과 구획선|1.40~2.02. 굵기 1의 선이므로 그중 일곱은 나타나지 않았습니다|
// |칸과 칸의 테|1.22~1.78|
//
// **얇은 선에는 0.4를 얹습니다.** 굵기 1~1.5의 선은 같은 대비에서 넓은 면보다 흐리게
// 보입니다 — 선과 면에 같은 값을 요구하면 선만 사라집니다.

import { describe, expect, it } from 'vitest'

import { contrast, hueOf, luminance } from '../src/render/color'
import { buildSurface, CONTRAST_GATE } from '../src/render/palette'
import { UI_THEMES, UI_THEME_KEYS } from '../src/render/theme'

/** 얇은 선에 얹는 값. */
const THIN = 0.4

describe('겉면의 대비', () => {
  it('겉면 여덟이 표의 하한을 전부 넘습니다', () => {
    const short: string[] = []
    for (const key of UI_THEME_KEYS) {
      const look = UI_THEMES[key]
      for (const rule of CONTRAST_GATE) {
        const got = contrast(look[rule.a], look[rule.b])
        // 판보다 어두운 자리는 낼 수 있는 만큼까지만 요구합니다.
        const room = 0.95 * (luminance(look[rule.b]) + 0.05) / 0.05
        const least = rule.room
          ? Math.min(rule.least, room)
          : rule.least + (rule.line ? THIN : 0)
        if (got + 1e-9 < least) {
          short.push(`${key} · ${rule.what} · ${got.toFixed(2)} < ${least.toFixed(2)}`)
        }
      }
    }
    expect(short).toEqual([])
  })

  it('겉면 여덟이 서로 다른 판을 가집니다', () => {
    const panels = UI_THEME_KEYS.map(key => UI_THEMES[key].panel)
    expect(new Set(panels).size).toBe(UI_THEME_KEYS.length)
  })

  /**
   * **뜻이 있는 색의 계열은 겉면과 무관합니다.**
   *
   * 「돈은 노랑」이 약속인 것이지 `0xf5c518` 이 약속인 것은 아니므로 밝기는 겉면을 따르되,
   * 색상각이 따라 움직이면 갈색 겉면의 노랑과 남색 겉면의 노랑이 다른 색이 됩니다.
   */
  it('뜻이 있는 색의 색상각이 겉면마다 같습니다', () => {
    const named = ['yellow', 'money', 'bar', 'chips', 'mult', 'green', 'good',
                   'red', 'bad', 'dare', 'discard', 'confirm', 'caution',
                   'uncommon', 'rare', 'legendary'] as const
    for (const name of named) {
      const hues = UI_THEME_KEYS.map(key => hueOf(UI_THEMES[key][name]) ?? -1)
      const spread = Math.max(...hues) - Math.min(...hues)
      // 8비트로 반올림한 만큼만 벌어집니다. 1도 아래는 같은 색입니다.
      expect(`${name} ${spread < 1.5}`).toBe(`${name} true`)
    }
  })

  /**
   * **판의 테는 판의 계열입니다.**
   *
   * 한때 판의 테도 그 겉면의 강조색이었고, 이 게이트는 테와 고른 것이 한 색상각인지를
   * 확인하였습니다. **검정 겉면에서 파란 테가 나왔습니다** — 그 겉면의 강조색이 `hue: 238`
   * 이기 때문입니다. 판이 검정인데 테가 파란 것은 그 겉면을 고른 뜻과 어긋나므로, 테를
   * 강조색에서 떼어 판의 계열로 옮겼습니다.
   *
   * 그래서 확인하는 것이 **테가 판과 한 계열인가**로 바뀌었습니다. 무채색 겉면에서는
   * 채도가 0이므로 색상각이 없고, 그때는 판과 견줄 것이 없으므로 통과입니다.
   *
   * 강조색끼리의 계열은 「뜻이 있는 색의 색상각이 겉면마다 같습니다」가 이미 확인합니다.
   */
  it('판의 테가 판과 한 계열입니다', () => {
    for (const key of UI_THEME_KEYS) {
      const look = UI_THEMES[key]
      const edge = hueOf(look.panelEdge)
      const panel = hueOf(look.panel)
      // 색상각이 없는 것은 무채색입니다. 검정 겉면의 테가 그렇습니다.
      const same = edge === undefined || panel === undefined
        || Math.abs(edge - panel) < 12
      expect(`${key} ${same}`).toBe(`${key} true`)
    }
  })

  /**
   * **씨앗 하나를 옮기면 겉면 전체가 함께 움직입니다.**
   *
   * 배수 표가 판을 기준으로 적혀 있으므로, 판을 밝히면 칸도 단추도 선도 같은 비로 밝아져야
   * 합니다 — 어느 하나가 손으로 적힌 값이면 거기서 관계가 끊깁니다.
   */
  it('판을 밝혀도 판과 단추의 대비가 그대로입니다', () => {
    const dark = buildSurface({ hue: 250, chroma: 0.03, level: 0.020, alpha: 1 })
    const light = buildSurface({ hue: 250, chroma: 0.03, level: 0.055, alpha: 1 })
    // 8비트로 반올림한 만큼 어긋납니다.
    const same = (a: number, b: number): boolean => Math.abs(a - b) < 0.12
    expect(same(contrast(light.btn, light.panel), contrast(dark.btn, dark.panel)))
      .toBe(true)
    expect(same(contrast(light.rule, light.panel), contrast(dark.rule, dark.panel)))
      .toBe(true)
  })
})
