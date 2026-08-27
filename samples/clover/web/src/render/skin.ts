// 판때기를 그리는 법.
//
// **버튼과 패널과 칸이 전부 같은 손으로 그려져야 화면이 한 벌로 보입니다.** 그래서 그리는
// 규칙을 여기 한 곳에 두고, 위젯들은 값만 넘깁니다.
//
// 층이 넷입니다 — 그림자, 바탕 그라디언트, 테두리, 안쪽 하이라이트. 마지막 하나가 화면을
// 「눌린 종이」가 아니라 「올라온 물건」으로 보이게 합니다.

import { FillGradient, Graphics } from 'pixi.js'

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

function gradient(width: number, height: number, top: number, bottom: number): FillGradient {
  const fill = new FillGradient(0, 0, 0, height)
  fill.addColorStop(0, top)
  fill.addColorStop(1, bottom)
  void width
  return fill
}

/** 판때기 하나. 그림자부터 하이라이트까지 한 번에 그립니다. */
export function plate(g: Graphics, width: number, height: number, style: PlateStyle): void {
  const radius = style.radius ?? 10
  const weight = style.weight ?? 1.5
  const drop = style.drop ?? 4
  const alpha = style.alpha ?? 1

  if (drop > 0) {
    g.roundRect(0, drop, width, height, radius).fill({ color: 0x000000, alpha: 0.35 })
  }

  g.roundRect(0, 0, width, height, radius)
    .fill({ fill: gradient(width, height, style.top, style.bottom), alpha })

  if ((style.gloss ?? 0) > 0) {
    g.roundRect(2, 2, width - 4, height * (style.gloss ?? 0), radius - 1)
      .fill({ color: 0xffffff, alpha: 0.06 })
  }

  // 안쪽 한 겹. 물건에 두께가 있어 보입니다.
  g.roundRect(1.5, 1.5, width - 3, height - 3, radius - 1)
    .stroke({ color: 0xffffff, width: 1, alpha: 0.08 })

  g.roundRect(weight / 2, weight / 2, width - weight, height - weight, radius)
    .stroke({ color: style.border, width: weight })
}

/** 화면 위에 뜨는 것. 그림자가 더 깊고 테두리가 밝습니다. */
export const FLOATING: PlateStyle = {
  top: 0x14261d, bottom: 0x0a1710, border: 0x2f5c42, drop: 6, gloss: 0.4,
}

/** 붙박이 패널. */
export const PANEL: PlateStyle = {
  top: 0x0d1a14, bottom: 0x061009, border: 0x1f3c2c, drop: 3, gloss: 0.16,
}

/** 값이 들어가는 작은 칸. */
export function slotStyle(ink: number): PlateStyle {
  return { top: 0x102019, bottom: 0x07120d, border: ink, weight: 1.5, drop: 2, gloss: 0.3 }
}

/** 누를 수 있는 것. 색은 그 버튼의 성격이 정합니다. */
export function buttonStyle(base: number, lit: boolean): PlateStyle {
  return {
    top: lit ? mix(base, 0xffffff, 0.28) : mix(base, 0xffffff, 0.12),
    bottom: lit ? base : mix(base, 0x000000, 0.25),
    border: mix(base, 0xffffff, 0.45),
    radius: 9,
    weight: 1.5,
    drop: lit ? 2 : 4,
    gloss: 0.45,
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
