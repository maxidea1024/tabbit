// 판때기를 그리는 법.
//
// **버튼과 패널과 칸이 전부 같은 손으로 그려져야 화면이 한 벌로 보입니다.** 그래서 그리는
// 규칙을 여기 한 곳에 두고, 위젯들은 값만 넘깁니다.
//
// 층이 넷입니다 — 그림자, 바탕 그라디언트, 테두리, 안쪽 하이라이트. 마지막 하나가 화면을
// 「눌린 종이」가 아니라 「올라온 물건」으로 보이게 합니다.
//
// **테두리가 한 줄이면 웹 화면입니다.** 굵기 하나에 색 하나짜리 둥근 사각형은 어느 사이트에나
// 있는 것이고, 게임의 테두리는 그 안에 두께가 있습니다 — 여기서 더하는 것이 셋입니다.
//
// |것|무엇|
// |--|--|
// |두 겹 테두리|바깥 테두리, 그 안쪽에 어두운 홈 한 줄, 그 안에 밝은 실선 한 줄. 금속 판에 테를 박은 모습입니다|
// |한 방향의 빛|안쪽 한 겹을 사방에 같은 밝기로 두르면 그것은 윤곽선입니다. 위·왼쪽이 밝고 아래·오른쪽이 어두워야 두께가 있는 물건이 됩니다|
// |네 귀의 꺾쇠|귀마다 ㄱ자 두 획. **게임 화면이라는 것을 가장 적은 획으로 알리는 것이 이것입니다**|
//
// 뒤의 둘은 넉넉한 판때기에만 붙습니다 — 작은 칸에 넣으면 글자와 다툽니다.

import { FillGradient, Graphics } from 'pixi.js'

import { UI } from './theme'

export interface PlateStyle {
  /** 바탕. 위에서 아래로 흐릅니다. */
  top: number
  bottom: number
  border: number
  radius?: number
  /** 테두리의 굵기. */
  weight?: number
  /** 그림자를 얼마나 아래로 떨어뜨리는가. */
  drop?: number
  /** 위쪽에 얹는 밝은 띠. 0이면 없습니다. */
  gloss?: number
  alpha?: number
}

/**
 * 위에서 아래로 흐르는 그라디언트.
 *
 * **좌표를 낱개로 넘기지 않습니다** — 그 형태는 Pixi 가 예고 폐기로 알리고, 콘솔이 그 경고로
 * 덮이면 진짜 오류가 그 밑에 묻힙니다.
 */
/** 만들어 둔 그라디언트. 높이와 두 색이 열쇠입니다. */
const GRADIENTS = new Map<string, FillGradient>()

function gradient(width: number, height: number, top: number, bottom: number): FillGradient {
  void width
  // **같은 그라디언트는 한 번만 만듭니다.** `FillGradient` 하나가 캔버스 하나와 텍스처
  // 하나이고, 판때기를 다시 그릴 때마다 새로 만들면 점수가 굴러가는 동안 초당 240개가
  // 생기고 지워지지 않습니다 — 색과 높이가 같으면 같은 텍스처입니다.
  const key = `${height}|${top}|${bottom}`
  let found = GRADIENTS.get(key)
  if (!found) {
    found = new FillGradient({
      start: { x: 0, y: 0 },
      end: { x: 0, y: height },
      colorStops: [
        { offset: 0, color: top },
        { offset: 1, color: bottom },
      ],
      textureSpace: 'global',
    })
    GRADIENTS.set(key, found)
  }
  return found
}

/**
 * 판때기 하나. **채우기 하나와 테 하나입니다.**
 *
 * 그림자 · 그라디언트 · 광택 · 안쪽 하이라이트 · 꺾쇠가 있었습니다. 다 걷었습니다 — 판의
 * 문법을 「남흑색 단색에 얇은 테」 하나로 두면 판 · 칸 · 단추가 같은 손으로 그려진 것으로
 * 보이고, 층을 겹칠수록 웹의 설정 창으로 돌아갑니다. `PlateStyle` 의 `bottom` · `drop` ·
 * `gloss` 는 부르는 쪽이 아직 넘기므로 받되 쓰지 않습니다.
 */
export function plate(g: Graphics, width: number, height: number, style: PlateStyle): void {
  const radius = style.radius ?? 8
  const weight = style.weight ?? 1.5
  const alpha = style.alpha ?? 1

  g.roundRect(0, 0, width, height, radius).fill({ color: style.top, alpha })
  g.roundRect(weight / 2, weight / 2, width - weight, height - weight, radius)
    .stroke({ color: style.border, width: weight })
}

/** 그라디언트를 쓰는 곳이 남아 있을 때를 위해 둡니다. 판때기는 더 쓰지 않습니다. */
export { gradient }

/**
 * 무리를 가르는 줄 하나.
 *
 * **가로줄 하나는 웹 문서의 것입니다.** 파인 자리로 보이려면 밝은 줄과 그 아래 어두운 줄이
 * 한 쌍이어야 하고, 그 줄에 장식 하나가 박혀 있어야 그은 것이 아니라 만들어 넣은 것으로
 * 보입니다 — 양 끝은 실선이고 사이가 대시이며 가운데에 마름모가 앉습니다.
 */
export function groove(g: Graphics, x: number, y: number, width: number,
                       color = UI.rule): void {
  // **선 하나입니다.** 홈과 대시와 마름모가 있었고, 그것은 판의 장식이었습니다.
  g.rect(x, y, width, 1.5).fill(color)
}

/** 화면 위에 뜨는 판. */
export const FLOATING: PlateStyle = {
  top: UI.panel, bottom: UI.panel, border: UI.panelEdge, alpha: UI.panelAlpha, radius: 8,
}

/** 붙박이 패널. 떠 있는 판과 같은 색입니다 — 둘이 다르면 판이 둘로 보입니다. */
export const PANEL: PlateStyle = {
  top: UI.panel, bottom: UI.panel, border: UI.panelEdge, alpha: UI.panelAlpha, radius: 8,
}

/**
 * 값이 들어가는 작은 칸.
 *
 * **테의 색은 값의 색을 따르지 않습니다.** 칸마다 다른 색 테를 두르면 왼쪽 판에 색이
 * 다섯입니다 — 테는 옅은 선 하나이고, 무엇의 값인지는 숫자의 색이 말합니다. `ink` 는
 * 부르는 쪽이 아직 넘기므로 받되 쓰지 않습니다.
 */
export function slotStyle(ink: number): PlateStyle {
  void ink
  return { top: UI.cell, bottom: UI.cell, border: UI.hairline, weight: 1, radius: 6 }
}

/**
 * 누를 수 있는 것. 색은 그 버튼의 성격이 정합니다.
 *
 * **납작합니다.** 위에 마우스가 오면 조금 밝아지는 것이 전부이고, 테는 잉크색 하나입니다.
 */
export function buttonStyle(base: number, lit: boolean): PlateStyle {
  return {
    top: lit ? mix(base, 0xffffff, 0.1) : base,
    bottom: base,
    border: UI.ink,
    radius: 6,
    weight: 1.5,
  }
}

export function mix(a: number, b: number, t: number): number {
  const channel = (shift: number) => {
    const ca = (a >> shift) & 0xff
    const cb = (b >> shift) & 0xff
    return Math.round(ca + (cb - ca) * t) & 0xff
  }
  return (channel(16) << 16) | (channel(8) << 8) | channel(0)
}
