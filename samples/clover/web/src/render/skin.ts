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
  //
  // **빛이 한 방향에서 옵니다.** 사방에 같은 밝기로 두르면 그것은 두께가 아니라 윤곽선이고,
  // 윤곽선은 그린 것을 납작하게 만듭니다 — 위·왼쪽이 밝고 아래·오른쪽이 어두워야 눈이
  // 그것을 「위에서 빛을 받는 판」으로 읽습니다.
  bevel(g, width, height, radius)

  g.roundRect(weight / 2, weight / 2, width - weight, height - weight, radius)
    .stroke({ color: style.border, width: weight })

  // **넉넉한 판때기에만 붙습니다.** 작은 칸에 테를 한 겹 더 두르면 남는 자리가 없어서,
  // 그 안의 글자가 테에 끼인 것으로 보입니다.
  if (width < 96 || height < 44) return

  const step = weight + 2
  const inner = Math.max(1, radius - step)
  // 어두운 홈 한 줄과 그 안의 밝은 실선 한 줄. **둘이 한 쌍입니다** — 홈만 두면 그은
  // 자리가 지저분하고, 실선만 두면 테두리가 굵어진 것으로 보입니다.
  g.roundRect(step, step, width - step * 2, height - step * 2, inner)
    .stroke({ color: 0x000000, width: 1, alpha: 0.45 })
  g.roundRect(step + 1.5, step + 1.5, width - step * 2 - 3, height - step * 2 - 3,
    Math.max(1, inner - 1.5))
    .stroke({ color: mix(style.border, 0xffffff, 0.22), width: 1, alpha: 0.5 })

  // **꺾쇠는 큰 판때기에만.** 화면의 판때기마다 같은 귀 표시가 붙으면 그것은 디테일이
  // 아니라 무늬이고, 작은 칸에서는 네 귀의 획이 글자와 자리를 다툽니다 — 판과 블라인드
  // 딱지와 떠 있는 판들이 다는 것이고, 값이 들어가는 칸과 단추는 달지 않습니다.
  if (width < 200 || height < 96) return

  brackets(g, width, height, radius, mix(style.border, 0xffffff, 0.5))
}

/**
 * 안쪽 한 겹. 위·왼쪽이 밝고 아래·오른쪽이 어둡습니다.
 *
 * **네 변을 낱개로 긋습니다.** 둥근 사각형 하나로 두르면 굵기와 색이 사방에 같아지고,
 * 그것은 두께가 아닙니다 — 귀의 둥근 자리는 비워 두므로 네 변이 서로 닿지 않습니다.
 */
function bevel(g: Graphics, width: number, height: number, radius: number): void {
  const at = 1.5
  const from = radius + 1
  const to = (span: number) => span - radius - 1
  if (to(width) <= from || to(height) <= from) return

  g.moveTo(from, at).lineTo(to(width), at)
    .stroke({ color: 0xffffff, width: 1.5, alpha: 0.16 })
  g.moveTo(at, from).lineTo(at, to(height))
    .stroke({ color: 0xffffff, width: 1.5, alpha: 0.1 })
  g.moveTo(from, height - at).lineTo(to(width), height - at)
    .stroke({ color: 0x000000, width: 1.5, alpha: 0.32 })
  g.moveTo(width - at, from).lineTo(width - at, to(height))
    .stroke({ color: 0x000000, width: 1.5, alpha: 0.24 })
}

/**
 * 네 귀의 꺾쇠.
 *
 * **귀의 둥근 자리 안쪽에 섭니다.** 귀의 꼭짓점에 두면 둥글린 바깥으로 밀려 나가므로,
 * 반지름의 0.7배만큼 안으로 들인 자리를 꺾이는 점으로 씁니다.
 */
function brackets(g: Graphics, width: number, height: number,
                  radius: number, color: number): void {
  const pad = radius * 0.7 + 1.5
  const leg = 9
  const corners: [number, number, number, number][] = [
    [pad, pad, 1, 1],
    [width - pad, pad, -1, 1],
    [pad, height - pad, 1, -1],
    [width - pad, height - pad, -1, -1],
  ]
  for (const [x, y, sx, sy] of corners) {
    g.moveTo(x, y + sy * leg).lineTo(x, y).lineTo(x + sx * leg, y)
      .stroke({ color, width: 2, alpha: 0.85, join: 'miter', cap: 'butt' })
  }
}

/**
 * 무리를 가르는 줄 하나.
 *
 * **가로줄 하나는 웹 문서의 것입니다.** 파인 자리로 보이려면 밝은 줄과 그 아래 어두운 줄이
 * 한 쌍이어야 하고, 그 줄에 장식 하나가 박혀 있어야 그은 것이 아니라 만들어 넣은 것으로
 * 보입니다 — 양 끝은 실선이고 사이가 대시이며 가운데에 마름모가 앉습니다.
 */
export function groove(g: Graphics, x: number, y: number, width: number,
                       color = 0x46536a): void {
  const mid = x + width / 2
  const gem = 5
  const cap = 14
  const dash = 6
  const gap = 5

  const paint = (from: number, to: number): void => {
    if (to - from < 0.5) return
    g.moveTo(from, y + 1).lineTo(to, y + 1)
      .stroke({ color: 0x0a0f18, width: 1, alpha: 0.7 })
    g.moveTo(from, y).lineTo(to, y)
      .stroke({ color, width: 1, alpha: 0.9 })
  }

  // 양 끝은 실선입니다. **대시로 시작하면 줄이 흩어진 것으로 보입니다.**
  paint(x, x + cap)
  paint(x + width - cap, x + width)

  // 사이는 대시이고, 가운데의 마름모가 앉을 자리는 비웁니다.
  for (let at = x + cap + gap; at < x + width - cap; at += dash + gap) {
    const to = Math.min(at + dash, x + width - cap)
    if (to > mid - gem - 3 && at < mid + gem + 3) continue
    paint(at, to)
  }

  const points = [mid, y - gem, mid + gem, y, mid, y + gem, mid - gem, y]
  g.poly(points).fill({ color: 0x0f1620 })
  g.poly(points).stroke({ color: mix(color, 0xffffff, 0.35), width: 1.2 })
}

/** 화면 위에 뜨는 것. 그림자가 더 깊고 테두리가 밝습니다. */
export const FLOATING: PlateStyle = {
  top: 0x2e3849, bottom: 0x18202c, border: 0x55637a, drop: 6, gloss: 0.4,
}

/** 붙박이 패널. */
export const PANEL: PlateStyle = {
  top: 0x1d2431, bottom: 0x111721, border: 0x36404f, drop: 3, gloss: 0.16,
}

/** 값이 들어가는 작은 칸. */
export function slotStyle(ink: number): PlateStyle {
  return { top: 0x1a212c, bottom: 0x0f141d, border: ink, weight: 1.5, drop: 2, gloss: 0.3 }
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
