// 카드의 얼굴.
//
// **트럼프는 큰 무늬 하나가 아닙니다.** 5는 무늬가 다섯이고, 그 다섯이 놓이는 자리는 몇백
// 년째 같습니다 — 그 배치가 곧 「이것은 트럼프다」이고, 숫자만 적힌 종이는 아무리 잘 그려도
// 트럼프로 보이지 않습니다.
//
// 그림 파일은 없습니다. 무늬 4종과 랭크 13종의 조합이므로 그리는 편이 맞고, 그림으로
// 만들면 202장이 됩니다.

import { Graphics } from 'pixi.js'

import { SuitKind } from '../generated/enums/suit-kind'
import { drawGlyph, shade, type GlyphName } from './glyph'

/**
 * 무늬 하나. `(cx, cy)` 가 가운데이고 `size` 가 높이입니다.
 *
 * 좌표는 -1..1 의 상자 안이므로 어느 크기에서도 같은 모양이 됩니다. `flip` 이 참이면
 * 거꾸로 그립니다 — 카드 아래쪽 절반의 무늬는 거꾸로 서 있습니다.
 */
export function drawSuit(g: Graphics, suit: SuitKind, cx: number, cy: number,
                         size: number, color: number, flip = false): void {
  const r = size / 2
  const x = (u: number) => cx + u * r
  const y = (v: number) => cy + (flip ? -v : v) * r
  const fill = { color }

  switch (suit) {
    case SuitKind.Diamond:
      g.moveTo(x(0), y(-1))
        .lineTo(x(0.68), y(0))
        .lineTo(x(0), y(1))
        .lineTo(x(-0.68), y(0))
        .closePath()
        .fill(fill)
      break

    case SuitKind.Heart:
      g.moveTo(x(0), y(1))
        .bezierCurveTo(x(-1.32), y(-0.06), x(-0.70), y(-1.16), x(0), y(-0.40))
        .bezierCurveTo(x(0.70), y(-1.16), x(1.32), y(-0.06), x(0), y(1))
        .closePath()
        .fill(fill)
      break

    case SuitKind.Spade:
      // 하트를 뒤집은 몸통에 자루 하나.
      g.moveTo(x(0), y(-1))
        .bezierCurveTo(x(1.32), y(0.06), x(0.70), y(1.16), x(0), y(0.40))
        .bezierCurveTo(x(-0.70), y(1.16), x(-1.32), y(0.06), x(0), y(-1))
        .closePath()
        .fill(fill)
      g.moveTo(x(-0.38), y(1.02))
        .lineTo(x(-0.09), y(0.30))
        .lineTo(x(0.09), y(0.30))
        .lineTo(x(0.38), y(1.02))
        .closePath()
        .fill(fill)
      break

    case SuitKind.Club:
      g.circle(x(0), y(-0.44), r * 0.46).fill(fill)
      g.circle(x(-0.50), y(0.22), r * 0.46).fill(fill)
      g.circle(x(0.50), y(0.22), r * 0.46).fill(fill)
      g.moveTo(x(-0.38), y(1.02))
        .lineTo(x(-0.09), y(0.16))
        .lineTo(x(0.09), y(0.16))
        .lineTo(x(0.38), y(1.02))
        .closePath()
        .fill(fill)
      break

    default:
      g.circle(cx, cy, r * 0.7).fill(fill)
      break
  }
}

/**
 * 랭크마다의 무늬 자리.
 *
 * 값은 **안쪽 상자 안의 0..1** 입니다 — 가로는 왼쪽 칸이 0, 오른쪽 칸이 1, 가운데가 0.5.
 * 세로도 같습니다. **아래 절반의 무늬는 거꾸로** 섭니다.
 */
const LAYOUT: Record<number, [number, number][]> = {
  2: [[0.5, 0], [0.5, 1]],
  3: [[0.5, 0], [0.5, 0.5], [0.5, 1]],
  4: [[0, 0], [1, 0], [0, 1], [1, 1]],
  5: [[0, 0], [1, 0], [0.5, 0.5], [0, 1], [1, 1]],
  6: [[0, 0], [1, 0], [0, 0.5], [1, 0.5], [0, 1], [1, 1]],
  7: [[0, 0], [1, 0], [0.5, 0.25], [0, 0.5], [1, 0.5], [0, 1], [1, 1]],
  8: [[0, 0], [1, 0], [0.5, 0.25], [0, 0.5], [1, 0.5],
    [0.5, 0.75], [0, 1], [1, 1]],
  9: [[0, 0], [1, 0], [0, 1 / 3], [1, 1 / 3], [0.5, 0.5],
    [0, 2 / 3], [1, 2 / 3], [0, 1], [1, 1]],
  10: [[0, 0], [1, 0], [0.5, 1 / 6], [0, 1 / 3], [1, 1 / 3],
    [0, 2 / 3], [1, 2 / 3], [0.5, 5 / 6], [0, 1], [1, 1]],
}

/** 그림 카드의 문양. **셋뿐이므로 표가 아니라 여기 있습니다.** */
const COURT: Record<number, GlyphName> = {
  11: 'blade',
  12: 'leaf',
  13: 'crown',
}

/**
 * 카드 한 장의 얼굴을 그립니다.
 *
 * A 는 큰 무늬 하나, 2에서 10 은 그 랭크의 배치, J·Q·K 는 액자 안의 문양 하나입니다.
 */
export function drawFace(g: Graphics, suit: SuitKind, rank: number,
                         w: number, h: number, color: number): void {
  // 에이스. **가운데 하나가 크게** — 그것이 에이스의 얼굴입니다.
  if (rank === 14) {
    drawSuit(g, suit, w / 2, h / 2, Math.min(w, h) * 0.52, color)
    return
  }

  if (rank >= 11 && rank <= 13) {
    drawCourt(g, suit, rank, w, h, color)
    return
  }

  const spots = LAYOUT[rank]
  if (!spots) {
    drawSuit(g, suit, w / 2, h / 2, h * 0.3, color)
    return
  }

  // 안쪽 상자. 모서리의 글자와 겹치지 않는 자리입니다.
  const left = w * 0.30
  const right = w * 0.70
  const top = h * 0.17
  const bottom = h * 0.83
  const size = h * 0.155

  for (const [u, v] of spots) {
    drawSuit(g, suit, left + (right - left) * u, top + (bottom - top) * v,
      size, color, v > 0.5)
  }
}

/**
 * 그림 카드.
 *
 * **액자 하나와 위아래로 마주 보는 문양 둘입니다.** 트럼프의 그림 카드가 점대칭인 것에는
 * 이유가 있습니다 — 어느 쪽으로 쥐어도 같아야 하기 때문이고, 그 대칭이 없으면 그림 카드로
 * 보이지 않습니다.
 */
function drawCourt(g: Graphics, suit: SuitKind, rank: number,
                   w: number, h: number, color: number): void {
  const x = w * 0.20
  const y = h * 0.16
  const fw = w * 0.60
  const fh = h * 0.68

  g.roundRect(x, y, fw, fh, 5).fill({ color, alpha: 0.10 })
  g.roundRect(x + 0.5, y + 0.5, fw - 1, fh - 1, 5)
    .stroke({ color, width: 1.2, alpha: 0.65 })
  // 가운데 띠. 위아래가 마주 보는 자리입니다.
  g.moveTo(x, y + fh / 2).lineTo(x + fw, y + fh / 2)
    .stroke({ color, width: 1, alpha: 0.35 })

  const glyph = COURT[rank] ?? 'crown'
  const size = Math.min(fw, fh) * 0.52
  const style = { fill: color, line: shade(color, 0.45), weight: 1.1 }

  drawGlyph(g, glyph, x + fw / 2, y + fh * 0.26, size, style)
  drawGlyph(g, glyph, x + fw / 2, y + fh * 0.74, size, style)

  // 그 랭크의 무늬를 액자 밖 두 귀에 하나씩. **무엇의 킹인지가 얼굴에 있어야 합니다.**
  drawSuit(g, suit, x + fw * 0.5, y + fh + h * 0.075, h * 0.10, color)
}

/** 그림 파일의 이름. `public/art/card/<무늬>_<랭크>.png` 입니다. */
export function cardArtId(suit: SuitKind, rank: number): string {
  const name = SUIT_NAME[suit] ?? 'spade'
  const face = rank === 14 ? 'ace' : rank === 13 ? 'king' : rank === 12 ? 'queen'
    : rank === 11 ? 'jack' : String(rank)
  return `${name}_${face}`
}

const SUIT_NAME: Record<number, string> = {
  [SuitKind.Spade]: 'spade',
  [SuitKind.Heart]: 'heart',
  [SuitKind.Club]: 'club',
  [SuitKind.Diamond]: 'diamond',
}
