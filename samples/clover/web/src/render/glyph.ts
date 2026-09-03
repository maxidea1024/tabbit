// 문양.
//
// **그림 파일이 없습니다.** 150종의 일러스트를 그리는 것은 따로 할 일이고, 그때까지 「도형 세
// 개」로 두면 무엇을 사는 것인지 알 수 없습니다. 그래서 뜻이 읽히는 문양 한 벌을 벡터로
// 그립니다 — 같은 식별자는 언제나 같은 문양이고, 스물 남짓이면 조커 150종이 서로 구별됩니다.
//
// 그림이 들어오면 이 파일을 스프라이트로 갈아 끼웁니다. **그때 지워지는 것은 이 파일 하나
// 입니다** — 부르는 쪽은 「식별자와 자리와 색」만 넘기기 때문입니다.

import { Graphics } from 'pixi.js'

export type GlyphName =
  | 'sun' | 'moon' | 'star' | 'eye' | 'skull' | 'crown' | 'mask' | 'leaf'
  | 'drop' | 'flame' | 'gear' | 'key' | 'bolt' | 'ring' | 'spiral' | 'anvil'
  | 'chalice' | 'blade' | 'wand' | 'hourglass' | 'planet' | 'sigil'

const ALL: GlyphName[] = [
  'sun', 'moon', 'star', 'eye', 'skull', 'crown', 'mask', 'leaf',
  'drop', 'flame', 'gear', 'key', 'bolt', 'ring', 'spiral', 'anvil',
  'chalice', 'blade', 'wand', 'hourglass',
]

/** 식별자에서 값 하나. 같은 글자열은 언제나 같은 값입니다. */
export function hashOf(text: string): number {
  let hash = 2166136261
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i)
    hash = Math.imul(hash, 16777619) >>> 0
  }
  return hash >>> 0
}

/** 이 식별자의 문양. */
export function glyphFor(id: string): GlyphName {
  return ALL[hashOf(id) % ALL.length]
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
    case 'sun': {
      g.circle(cx, cy, r * 0.44).fill(fill).stroke(stroke)
      for (let i = 0; i < 8; i++) {
        const a = (i / 8) * Math.PI * 2
        const x0 = cx + Math.cos(a) * r * 0.62
        const y0 = cy + Math.sin(a) * r * 0.62
        const x1 = cx + Math.cos(a) * r * 0.98
        const y1 = cy + Math.sin(a) * r * 0.98
        g.moveTo(x0, y0).lineTo(x1, y1).stroke({ color: style.fill, width: weight * 1.4 })
      }
      break
    }

    case 'moon': {
      g.circle(cx + r * 0.12, cy, r * 0.86).fill(fill).stroke(stroke)
      g.circle(cx + r * 0.52, cy - r * 0.12, r * 0.72).cut()
      break
    }

    case 'star': {
      star(g, cx, cy, 5, r * 0.98, r * 0.42)
      g.fill(fill).stroke(stroke)
      break
    }

    case 'eye': {
      g.ellipse(cx, cy, r * 0.95, r * 0.58).fill(fill).stroke(stroke)
      g.circle(cx, cy, r * 0.30).fill({ color: line })
      g.circle(cx + r * 0.10, cy - r * 0.10, r * 0.10).fill({ color: 0xffffff, alpha: 0.8 })
      break
    }

    case 'skull': {
      g.roundRect(cx - r * 0.62, cy - r * 0.82, r * 1.24, r * 1.28, r * 0.5).fill(fill).stroke(stroke)
      g.roundRect(cx - r * 0.34, cy + r * 0.36, r * 0.68, r * 0.44, r * 0.12).fill(fill).stroke(stroke)
      g.ellipse(cx - r * 0.28, cy - r * 0.20, r * 0.20, r * 0.24).fill({ color: line })
      g.ellipse(cx + r * 0.28, cy - r * 0.20, r * 0.20, r * 0.24).fill({ color: line })
      g.roundRect(cx - r * 0.08, cy + r * 0.10, r * 0.16, r * 0.20, r * 0.06).fill({ color: line })
      break
    }

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

    case 'mask': {
      g.moveTo(cx - r * 0.92, cy - r * 0.52)
        .quadraticCurveTo(cx, cy - r * 0.86, cx + r * 0.92, cy - r * 0.52)
        .quadraticCurveTo(cx + r * 0.74, cy + r * 0.92, cx, cy + r * 0.96)
        .quadraticCurveTo(cx - r * 0.74, cy + r * 0.92, cx - r * 0.92, cy - r * 0.52)
        .closePath()
        .fill(fill).stroke(stroke)
      g.ellipse(cx - r * 0.34, cy - r * 0.16, r * 0.22, r * 0.14).fill({ color: line })
      g.ellipse(cx + r * 0.34, cy - r * 0.16, r * 0.22, r * 0.14).fill({ color: line })
      g.moveTo(cx - r * 0.28, cy + r * 0.44)
        .quadraticCurveTo(cx, cy + r * 0.66, cx + r * 0.28, cy + r * 0.44)
        .stroke({ color: line, width: weight })
      break
    }

    case 'leaf': {
      g.moveTo(cx, cy - r * 0.96)
        .quadraticCurveTo(cx + r * 0.92, cy - r * 0.10, cx, cy + r * 0.96)
        .quadraticCurveTo(cx - r * 0.92, cy - r * 0.10, cx, cy - r * 0.96)
        .closePath()
        .fill(fill).stroke(stroke)
      g.moveTo(cx, cy - r * 0.8).lineTo(cx, cy + r * 0.8).stroke({ color: line, width: weight })
      break
    }

    case 'drop': {
      g.moveTo(cx, cy - r * 0.96)
        .quadraticCurveTo(cx + r * 0.86, cy + r * 0.22, cx, cy + r * 0.94)
        .quadraticCurveTo(cx - r * 0.86, cy + r * 0.22, cx, cy - r * 0.96)
        .closePath()
        .fill(fill).stroke(stroke)
      break
    }

    case 'flame': {
      g.moveTo(cx, cy - r * 0.98)
        .quadraticCurveTo(cx + r * 0.78, cy - r * 0.10, cx + r * 0.40, cy + r * 0.62)
        .quadraticCurveTo(cx, cy + r * 0.98, cx - r * 0.40, cy + r * 0.62)
        .quadraticCurveTo(cx - r * 0.78, cy - r * 0.10, cx, cy - r * 0.98)
        .closePath()
        .fill(fill).stroke(stroke)
      g.moveTo(cx, cy - r * 0.24)
        .quadraticCurveTo(cx + r * 0.34, cy + r * 0.24, cx, cy + r * 0.62)
        .quadraticCurveTo(cx - r * 0.34, cy + r * 0.24, cx, cy - r * 0.24)
        .closePath()
        .fill({ color: 0xffffff, alpha: 0.35 })
      break
    }

    case 'gear': {
      // 톱니 하나. **같은 자리에 8번 겹쳐 그리던 것입니다** — 돌리지 않았으므로 모습은
      // 하나와 같고, 채우기 7번이 낭비였습니다.
      g.roundRect(cx - r * 0.14, cy - r * 0.98, r * 0.28, r * 0.34, r * 0.06)
      g.fill(fill)
      // 톱니를 돌려 그리는 대신 원 하나에 홈을 냅니다. 작은 크기에서 더 깔끔합니다.
      g.circle(cx, cy, r * 0.86).fill(fill).stroke(stroke)
      const teeth = 8
      for (let i = 0; i < teeth; i++) {
        const a = (i / teeth) * Math.PI * 2
        g.circle(cx + Math.cos(a) * r * 0.86, cy + Math.sin(a) * r * 0.86, r * 0.16)
          .fill(fill).stroke(stroke)
      }
      g.circle(cx, cy, r * 0.30).fill({ color: line })
      break
    }

    case 'key': {
      g.circle(cx, cy - r * 0.48, r * 0.36).stroke({ color: style.fill, width: weight * 2.2 })
      g.roundRect(cx - r * 0.09, cy - r * 0.16, r * 0.18, r * 1.02, r * 0.08).fill(fill)
      g.roundRect(cx, cy + r * 0.36, r * 0.34, r * 0.14, r * 0.05).fill(fill)
      g.roundRect(cx, cy + r * 0.66, r * 0.26, r * 0.14, r * 0.05).fill(fill)
      break
    }

    case 'bolt': {
      g.moveTo(cx + r * 0.32, cy - r * 0.98)
        .lineTo(cx - r * 0.56, cy + r * 0.12)
        .lineTo(cx - r * 0.04, cy + r * 0.12)
        .lineTo(cx - r * 0.28, cy + r * 0.98)
        .lineTo(cx + r * 0.62, cy - r * 0.16)
        .lineTo(cx + r * 0.06, cy - r * 0.16)
        .closePath()
        .fill(fill).stroke(stroke)
      break
    }

    case 'ring': {
      g.circle(cx, cy, r * 0.84).stroke({ color: style.fill, width: weight * 2.6 })
      g.circle(cx, cy, r * 0.40).stroke({ color: line, width: weight * 1.4 })
      break
    }

    case 'spiral': {
      let a = 0
      let rad = r * 0.12
      g.moveTo(cx + rad, cy)
      while (a < Math.PI * 5) {
        a += 0.25
        rad = r * 0.12 + (a / (Math.PI * 5)) * r * 0.84
        g.lineTo(cx + Math.cos(a) * rad, cy + Math.sin(a) * rad)
      }
      g.stroke({ color: style.fill, width: weight * 1.8 })
      break
    }

    case 'anvil': {
      g.roundRect(cx - r * 0.86, cy - r * 0.42, r * 1.72, r * 0.46, r * 0.1).fill(fill).stroke(stroke)
      g.roundRect(cx - r * 0.28, cy + r * 0.04, r * 0.56, r * 0.44, r * 0.06).fill(fill)
      g.roundRect(cx - r * 0.62, cy + r * 0.48, r * 1.24, r * 0.26, r * 0.08).fill(fill).stroke(stroke)
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

    case 'wand': {
      g.roundRect(cx - r * 0.09, cy - r * 0.30, r * 0.18, r * 1.26, r * 0.08)
        .fill(fill).stroke(stroke)
      star(g, cx, cy - r * 0.56, 4, r * 0.52, r * 0.16)
      g.fill({ color: style.fill }).stroke(stroke)
      break
    }

    case 'hourglass': {
      g.moveTo(cx - r * 0.60, cy - r * 0.86)
        .lineTo(cx + r * 0.60, cy - r * 0.86)
        .lineTo(cx + r * 0.10, cy)
        .lineTo(cx + r * 0.60, cy + r * 0.86)
        .lineTo(cx - r * 0.60, cy + r * 0.86)
        .lineTo(cx - r * 0.10, cy)
        .closePath()
        .fill(fill).stroke(stroke)
      g.roundRect(cx - r * 0.72, cy - r * 0.98, r * 1.44, r * 0.16, r * 0.06).fill({ color: line })
      g.roundRect(cx - r * 0.72, cy + r * 0.82, r * 1.44, r * 0.16, r * 0.06).fill({ color: line })
      break
    }

    /** 행성 — 고리가 있는 원. 천체 소모품이 씁니다. */
    case 'planet': {
      g.circle(cx, cy, r * 0.62).fill(fill).stroke(stroke)
      // 밝은 쪽과 어두운 쪽. 구로 보이게 합니다.
      g.circle(cx - r * 0.18, cy - r * 0.18, r * 0.34)
        .fill({ color: 0xffffff, alpha: 0.22 })
      g.ellipse(cx, cy + r * 0.06, r * 0.98, r * 0.26)
        .stroke({ color: line, width: weight * 1.6 })
      break
    }

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

function star(g: Graphics, cx: number, cy: number,
              points: number, outer: number, inner: number): void {
  for (let i = 0; i < points * 2; i++) {
    const a = (i / (points * 2)) * Math.PI * 2 - Math.PI / 2
    const rad = i % 2 === 0 ? outer : inner
    const x = cx + Math.cos(a) * rad
    const y = cy + Math.sin(a) * rad
    if (i === 0) g.moveTo(x, y)
    else g.lineTo(x, y)
  }
  g.closePath()
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
