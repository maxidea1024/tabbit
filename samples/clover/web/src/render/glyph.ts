// 문양.
//
// **그림 파일이 없던 동안의 대역이었고, 그 일은 끝났습니다.** 조커·태그·보스·소모품에 그림이
// 다 들어왔으므로 식별자에서 문양을 뽑아 세우던 세 자리를 걷었습니다 — 그림이 아닌데 그
// 물건의 표시처럼 보여 오해만 남겼습니다.
//
// **남은 것은 대역이 아니라 실제 그림입니다.** 트럼프의 J·Q·K 가 드는 칼과 잔과 왕관
// (`pips.ts`)과 조커 뒷면의 인장이 그것이고, 둘 다 어느 그림 파일도 대신하지 않습니다.
//
// 색을 만드는 셋(`shade` · `tintUp` · `hsl`)과 `hashOf` 도 여기 있습니다. 부르는 자리가
// 화면 곳곳이므로 옮기지 않습니다.

import { Graphics } from 'pixi.js'

/**
 * 그릴 수 있는 문양.
 *
 * **넷뿐입니다.** 스물둘이었고 그 가운데 18개는 식별자에서 뽑아 세우던 대역이었습니다 —
 * 그 일이 끝났으므로 그리는 코드도 함께 걷었습니다. 남은 넷은 J·Q·K 의 것 셋과 조커 뒷면의
 * 인장이고, 넷 다 부르는 자리가 코드에 적혀 있습니다.
 */
export type GlyphName = 'blade' | 'chalice' | 'crown' | 'sigil'

/** 식별자에서 값 하나. 같은 글자열은 언제나 같은 값입니다. */
export function hashOf(text: string): number {
  let hash = 2166136261
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i)
    hash = Math.imul(hash, 16777619) >>> 0
  }
  return hash >>> 0
}

export interface GlyphStyle {
  /** 채우는 색. */
  fill: number
  /** 선의 색. 없으면 채우는 색을 어둡게 씁니다. */
  line?: number
  /** 선의 굵기. */
  weight?: number
}

/**
 * 문양 하나를 그립니다.
 *
 * `(cx, cy)` 가 가운데이고 `size` 가 지름입니다. 좌표는 전부 `size` 에 대한 비율이므로
 * 어느 크기에서도 같은 모양이 됩니다.
 */
export function drawGlyph(g: Graphics, name: GlyphName,
                          cx: number, cy: number, size: number, style: GlyphStyle): void {
  const r = size / 2
  const line = style.line ?? shade(style.fill, 0.45)
  const weight = style.weight ?? Math.max(1.2, size * 0.045)
  const fill = { color: style.fill }
  const stroke = { color: line, width: weight }

  switch (name) {
    case 'crown': {
      g.moveTo(cx - r * 0.92, cy + r * 0.54)
        .lineTo(cx - r * 0.72, cy - r * 0.60)
        .lineTo(cx - r * 0.30, cy + r * 0.02)
        .lineTo(cx, cy - r * 0.86)
        .lineTo(cx + r * 0.30, cy + r * 0.02)
        .lineTo(cx + r * 0.72, cy - r * 0.60)
        .lineTo(cx + r * 0.92, cy + r * 0.54)
        .closePath()
        .fill(fill).stroke(stroke)
      g.roundRect(cx - r * 0.92, cy + r * 0.54, r * 1.84, r * 0.28, r * 0.1)
        .fill({ color: line })
      break
    }

    case 'chalice': {
      g.moveTo(cx - r * 0.66, cy - r * 0.62)
        .lineTo(cx + r * 0.66, cy - r * 0.62)
        .quadraticCurveTo(cx + r * 0.52, cy + r * 0.30, cx, cy + r * 0.34)
        .quadraticCurveTo(cx - r * 0.52, cy + r * 0.30, cx - r * 0.66, cy - r * 0.62)
        .closePath()
        .fill(fill).stroke(stroke)
      g.roundRect(cx - r * 0.08, cy + r * 0.30, r * 0.16, r * 0.40, r * 0.05).fill(fill)
      g.roundRect(cx - r * 0.44, cy + r * 0.70, r * 0.88, r * 0.18, r * 0.07).fill(fill).stroke(stroke)
      break
    }

    case 'blade': {
      g.moveTo(cx, cy - r * 0.98)
        .lineTo(cx + r * 0.20, cy - r * 0.62)
        .lineTo(cx + r * 0.12, cy + r * 0.36)
        .lineTo(cx - r * 0.12, cy + r * 0.36)
        .lineTo(cx - r * 0.20, cy - r * 0.62)
        .closePath()
        .fill(fill).stroke(stroke)
      g.roundRect(cx - r * 0.52, cy + r * 0.36, r * 1.04, r * 0.16, r * 0.06).fill({ color: line })
      g.roundRect(cx - r * 0.10, cy + r * 0.52, r * 0.20, r * 0.42, r * 0.07).fill({ color: line })
      break
    }

    /** 행성 — 고리가 있는 원. 천체 소모품이 씁니다. */
    /** 사인 — 유령 소모품이 씁니다. 삼각형과 원과 선. */
    case 'sigil': {
      g.circle(cx, cy, r * 0.88).stroke({ color: line, width: weight })
      g.moveTo(cx, cy - r * 0.78)
        .lineTo(cx + r * 0.68, cy + r * 0.44)
        .lineTo(cx - r * 0.68, cy + r * 0.44)
        .closePath()
        .fill({ color: style.fill, alpha: 0.55 }).stroke(stroke)
      g.circle(cx, cy + r * 0.06, r * 0.24).fill({ color: line })
      break
    }
  }
}

/** 색을 어둡게. 선 색을 채우는 색에서 만듭니다. */
export function shade(color: number, amount: number): number {
  const channel = (shift: number) => {
    const value = (color >> shift) & 0xff
    return Math.round(value * (1 - amount)) & 0xff
  }
  return (channel(16) << 16) | (channel(8) << 8) | channel(0)
}

/** 색을 밝게. */
export function tintUp(color: number, amount: number): number {
  const channel = (shift: number) => {
    const value = (color >> shift) & 0xff
    return Math.round(value + (255 - value) * amount) & 0xff
  }
  return (channel(16) << 16) | (channel(8) << 8) | channel(0)
}

/** 색상환에서 색 하나. 식별자에서 만든 값을 그대로 넣습니다. */
export function hsl(hue: number, saturation: number, lightness: number): number {
  const a = saturation * Math.min(lightness, 1 - lightness)
  const channel = (n: number) => {
    const k = (n + hue / 30) % 12
    const value = lightness - a * Math.max(-1, Math.min(k - 3, Math.min(9 - k, 1)))
    return Math.round(value * 255)
  }
  return (channel(0) << 16) | (channel(8) << 8) | channel(4)
}
