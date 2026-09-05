// 판때기를 그리는 법.
//
// **버튼과 패널과 칸이 전부 같은 손으로 그려져야 화면이 한 벌로 보입니다.** 그래서 그리는
// 규칙을 여기 한 곳에 두고, 위젯들은 값만 넘깁니다.
//
// **층이 둘입니다** — 단색 채우기 하나와 테 하나. 그라디언트 · 그림자 · 광택 · 안쪽 한 겹 ·
// 네 귀의 꺾쇠가 있었고, 전부 걷었습니다.
//
// 한때 이 자리에 「테두리가 한 줄이면 웹 화면이다」 가 적혀 있었고, 두께를 내는 셋을 다시
// 얹어 보았습니다. **판 안이 답답해졌습니다** — 테가 두 겹이 되면 그 안의 글과 칸이 그만큼
// 좁아지고, 작은 칸에서는 남는 자리가 없습니다. 되돌렸습니다.
//
// 게임 화면으로 보이게 하는 다른 길을 찾을 때까지 이 문법을 지킵니다 — 무엇을 더하든
// **판 안의 자리를 빼앗지 않는 것**이 조건입니다.

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
  const inset = weight / 2

  g.roundRect(0, 0, width, height, radius).fill({ color: style.top, alpha })
  g.roundRect(inset, inset, width - weight, height - weight, insetRadius(radius, inset))
    .stroke({ color: style.border, width: weight })
}

/**
 * 안쪽으로 들여 그리는 테의 반지름.
 *
 * **들여 그린 만큼 반지름도 줄어듭니다.** 같은 반지름으로 그리면 네 귀퉁이에서만 테가
 * 채움의 가장자리보다 안쪽으로 물러나고, 그 사이로 채움의 모서리가 삐져나옵니다 — 변에서는
 * 맞고 귀퉁이에서만 어긋나므로 모서리가 깎여 나간 것처럼 보입니다. 큰 화면에서만 눈에
 * 들었습니다.
 *
 * 음수가 되지 않게 0에서 멈춥니다 — 테가 반지름보다 굵으면 귀퉁이가 직각입니다.
 */
export function insetRadius(radius: number, inset: number): number {
  return Math.max(0, radius - inset)
}

/**
 * 떠오르는 글 뒤의 번쩍임.
 *
 * **만화가 소리를 적을 때 쓰는 그 모양입니다.** 판 위에는 카드와 그림이 깔려 있어서 테를
 * 두른 글자만으로는 그 위에서 읽히지 않습니다 — 어두운 안쪽이 글의 바탕이 되고, 뾰족한
 * 테가 그 사건의 세기를 알립니다.
 *
 * **세기가 모양을 정합니다.** 조용한 것은 뾰족함이 적고 얕으며, 배수를 곱하는 것처럼 큰
 * 것은 날카롭습니다. 끝의 길이를 조금씩 달리해 자로 그린 별처럼 보이지 않게 두었고,
 * 그 값은 난수가 아니라 자리에서 나옵니다 — 한 번 그리고 마는 그림이므로 프레임마다
 * 달라지면 안 됩니다.
 */
export function burst(g: Graphics, halfW: number, halfH: number,
                      intensity: number, tint: number): void {
  const heat = Math.min(1.4, Math.max(0, intensity))
  const spikes = Math.round(9 + heat * 4)
  const dip = 0.74 - heat * 0.16
  const points: number[] = []
  for (let i = 0; i < spikes * 2; i++) {
    const angle = (Math.PI * i) / spikes - Math.PI / 2
    const reach = i % 2 === 0 ? 0.9 + ((i * 37) % 21) / 100 : dip
    points.push(Math.cos(angle) * halfW * reach, Math.sin(angle) * halfH * reach)
  }
  g.poly(points).fill({ color: 0x0a0f18, alpha: 0.82 })
  g.poly(points).stroke({ color: tint, width: 1.5, alpha: 0.85 })
}

/** 그라디언트를 쓰는 곳이 남아 있을 때를 위해 둡니다. 판때기는 더 쓰지 않습니다. */
export { gradient }

/**
 * 무리를 가르는 줄 하나. **양 끝은 실선이고 사이가 대시입니다.**
 *
 * 실선 한 줄이면 구획 머리의 선과 같은 것이 되는데, 그 둘은 하는 일이 다릅니다 — 구획
 * 머리의 선은 이름 아래에 붙어 그 아래가 그 구획임을 말하고, 이 줄은 이름 없이 위아래를
 * 갈라 놓기만 합니다. 대시가 그 차이입니다.
 *
 * **표식은 두지 않습니다.** 가르는 데 필요하지 않고, 그것 하나가 판마다 붙으면 디테일이
 * 아니라 무늬입니다.
 */
export function groove(g: Graphics, x: number, y: number, width: number,
                       color = UI.rule): void {
  const cap = 14
  const dash = 6
  const gap = 5

  const paint = (from: number, to: number): void => {
    if (to - from < 0.5) return
    g.moveTo(from, y).lineTo(to, y).stroke({ color, width: 1 })
  }

  // 양 끝은 실선입니다. **대시로 시작하면 줄이 흩어진 것으로 보입니다.**
  paint(x, x + cap)
  paint(x + width - cap, x + width)

  for (let at = x + cap + gap; at < x + width - cap; at += dash + gap) {
    paint(at, Math.min(at + dash, x + width - cap))
  }
}

/**
 * 화면 위에 뜨는 판.
 *
 * **상수가 아니라 함수입니다.** 상수로 두면 불러올 때의 색을 베껴 두므로, 옵션에서 겉면을
 * 갈아 끼워도 판때기만 옛 색으로 남습니다 — 그릴 때 읽어야 합니다.
 */
export function floatingStyle(): PlateStyle {
  return {
    top: UI.panel, bottom: UI.panel, border: UI.panelEdge, alpha: UI.panelAlpha, radius: 8,
  }
}

/** 붙박이 패널. 떠 있는 판과 같은 색입니다 — 둘이 다르면 판이 둘로 보입니다. */
export function panelStyle(): PlateStyle {
  return floatingStyle()
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
