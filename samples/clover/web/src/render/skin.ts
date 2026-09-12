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

import { shade } from './color'
import { PAINT } from './ink'
import { UI, RADIUS, STROKE } from './theme'

/** `border` 에 이 값을 넘기면 테를 그리지 않습니다. 금속 테 그림이 그 일을 합니다. */
export const NO_BORDER = -1

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
  const radius = style.radius ?? RADIUS.base
  const weight = style.weight ?? STROKE.base
  const alpha = style.alpha ?? 1
  const half = weight / 2

  // **위가 아주 조금 밝습니다.** 빛이 위에서 오는 판입니다 — 단색으로 두면 판이 종이가
  // 아니라 오려 붙인 색면으로 보입니다. 0.02는 나란히 놓고 보아야 아는 차이이고, 그
  // 정도가 판을 판으로 보이게 하는 만큼입니다.
  const fill = style.top === style.bottom
    ? { color: style.top, alpha }
    : { fill: faceFill(height, style.top, style.bottom), alpha }
  g.roundRect(0, 0, width, height, radius).fill(fill)
  // **테를 그리지 않는 경우가 있습니다.** 금속 테 그림이 판 경계에 걸쳐 놓이면 이 선이 그
  // 안쪽에 한 줄 더 그려지고, 강조색의 얇은 선이 곧 웹 화면의 인상입니다.
  if (style.border !== NO_BORDER) {
    g.roundRect(half, half, width - weight, height - weight, insetRadius(radius, half))
      .stroke({ color: style.border, width: weight })
  }
}

/**
 * 파인 칸.
 *
 * **위 안쪽에 어두운 줄, 아래 안쪽에 밝은 줄입니다.** 빛이 위에서 오므로 파인 것은 위가
 * 그늘이고 아래가 밝습니다 — 그 두 줄이 없으면 칸은 어두운 색면 하나이고, 값이 그 위에
 * 얹힌 것으로 보이지 파인 자리에 들어간 것으로 보이지 않습니다.
 *
 * **판 안의 자리를 빼앗지 않습니다.** 테 안쪽에 1픽셀씩입니다.
 */
export function carve(g: Graphics, width: number, height: number,
                      face: number, radius: number = RADIUS.small): void {
  // **밝은 턱이 파임을 만듭니다.**
  //
  // 칸의 채움을 밝히고 어둡혀 두 줄을 두었고, 보이지 않았습니다 — 칸은 거의 검정이라
  // 거기서 0.10 을 더하거나 빼도 화면에서 같은 검정입니다. 파임이 보이는 것은 칸이 아니라
  // **판이 그 구멍의 아래 벽에서 빛을 받는 것**이므로, 밝은 쪽을 판의 색에서 뽑습니다.
  //
  // 빛이 위에서 오므로 아래와 오른쪽이 밝습니다. 위와 왼쪽의 그늘은 칸이 이미 검정이라
  // 따로 그리지 않습니다 — 그것이 그늘 자체입니다.
  void face
  const light = shade(UI.panel, 0.16)

  g.moveTo(radius, height - 1.5).lineTo(width - radius, height - 1.5)
    .stroke({ color: light, width: STROKE.base, alpha: 0.85 })
  g.moveTo(width - 1.5, radius).lineTo(width - 1.5, height - radius)
    .stroke({ color: light, width: STROKE.hair, alpha: 0.5 })
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
 * 캡슐의 둘레를 걷습니다. `at` 은 둘레를 따라 잰 거리이고, 돌려주는 것은 그 자리와
 * 바깥을 향한 방향입니다.
 *
 * **글이 짧으면 원이고 길면 양 끝이 둥근 띠입니다.** 곧은 변의 길이 `straight` 가 0이면
 * 반지름 `cap` 의 원이 됩니다.
 */
function onCapsule(at: number, straight: number, cap: number):
    [number, number, number, number] {
  const flat = 2 * straight
  const round = Math.PI * cap
  let left = at

  // 윗변. 왼쪽에서 오른쪽으로.
  if (left < flat) return [-straight + left, -cap, 0, -1]
  left -= flat

  // 오른쪽 끝. 위에서 아래로 반 바퀴.
  if (left < round) {
    const angle = -Math.PI / 2 + left / cap
    return [straight + Math.cos(angle) * cap, Math.sin(angle) * cap,
            Math.cos(angle), Math.sin(angle)]
  }
  left -= round

  // 아랫변. 오른쪽에서 왼쪽으로.
  if (left < flat) return [straight - left, cap, 0, 1]
  left -= flat

  // 왼쪽 끝.
  const angle = Math.PI / 2 + left / cap
  return [-straight + Math.cos(angle) * cap, Math.sin(angle) * cap,
          Math.cos(angle), Math.sin(angle)]
}

/**
 * 떠오르는 글 뒤의 번쩍임.
 *
 * **만화가 소리를 적을 때 쓰는 그 모양입니다.** 판 위에는 카드와 그림이 깔려 있어서 테를
 * 두른 글자만으로는 그 위에서 읽히지 않습니다 — 어두운 안쪽이 글의 바탕이 되고, 뾰족한
 * 테가 그 사건의 세기를 알립니다.
 *
 * **끝의 길이는 픽셀로 정합니다.** 몸통은 글을 감싸야 하므로 긴 글에서는 가로로 깁니다.
 * 그런데 전에는 단위원의 별에 가로 반지름과 세로 반지름을 각각 곱해 만들었고, 그러면
 * 끝의 길이까지 그 비율로 늘어났습니다 — 숫자 하나는 거의 둥근 별이었고 낱말 두 개짜리
 * 이름은 가로세로가 2.5 대 1인 별이었습니다. 몸통만 글을 따라 길어지고 끝은 어느
 * 쪽에서나 같은 길이입니다.
 *
 * **끝의 자리는 둘레를 따라 고릅니다.** 각도로 고르면 가로로 긴 몸통에서 양 끝에만
 * 몰리고 위아래 변에는 거의 놓이지 않습니다.
 *
 * **세기가 모양을 정합니다.** 조용한 것은 끝이 짧고 성글며, 배수를 곱하는 것처럼 큰 것은
 * 길고 촘촘합니다. 끝의 길이를 조금씩 달리해 자로 그린 별처럼 보이지 않게 두었고, 그 값은
 * 난수가 아니라 자리에서 나옵니다 — 한 번 그리고 마는 그림이므로 프레임마다 달라지면
 * 안 됩니다.
 *
 * `halfW` · `halfH` 는 몸통의 반지름입니다. 끝은 그 바깥으로 더 나갑니다.
 */
export function burst(g: Graphics, halfW: number, halfH: number,
                      intensity: number, tint: number): void {
  const heat = Math.min(1.4, Math.max(0, intensity))
  const cap = Math.max(1, halfH)
  const straight = Math.max(0, halfW - cap)
  const spike = 9 + heat * 9
  const around = 4 * straight + 2 * Math.PI * cap
  // 끝과 끝 사이가 늘 비슷하게 벌어지도록 둘레로 셉니다.
  const spikes = Math.min(28, Math.max(9, Math.round(around / (15 - heat * 2))))

  const points: number[] = []
  for (let i = 0; i < spikes * 2; i++) {
    const [x, y, nx, ny] = onCapsule((around * i) / (spikes * 2), straight, cap)
    const out = i % 2 === 0 ? spike * (0.85 + ((i * 37) % 21) / 70) : 0
    points.push(x + nx * out, y + ny * out)
  }

  g.poly(points).fill({ color: UI.outline, alpha: 0.82 })
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
                       color = UI.groove): void {
  const cap = 14
  const dash = 6
  const gap = 5

  const paint = (from: number, to: number): void => {
    if (to - from < 0.5) return
    g.moveTo(from, y).lineTo(to, y).stroke({ color, width: STROKE.hair })
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
/**
 * 구워 둔 판을 물들이는 색.
 *
 * **그림의 가장 밝은 곳이 흰색입니다.** 물들이기가 색을 곱하는 것이므로 넘기는 색이 곧
 * 판의 꼭대기입니다 — 판의 색을 그대로 넘기면 그 색이 꼭대기가 되고 아래로 내려가며
 * 어두워져, 판 전체가 바닥보다 어두워집니다.
 *
 * 그림의 세로 채움이 1.0 에서 0.35 로 내려가므로 가운데가 0.67 입니다. 판의 색이 그
 * 가운데에 오도록 올려 둡니다.
 */
export function plateTint(base: number): number {
  return mix(base, PAINT.sheen, 0.34)
}

export function floatingStyle(): PlateStyle {
  return {
    top: UI.panel, bottom: UI.panel, border: UI.panelEdge, alpha: UI.panelAlpha, radius: RADIUS.base,
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
  return { top: UI.cell, bottom: UI.cell, border: UI.hairline, weight: STROKE.hair, radius: RADIUS.small }
}

/**
 * 단추의 아래 턱.
 *
 * **누르면 이만큼 내려앉습니다.** 판때기 하나로 그리던 동안 단추는 색이 칠해진 네모였고,
 * 누르는 것과 놓인 것의 차이가 색뿐이었습니다 — 턱이 있으면 그 위의 얼굴이 실제로 내려가고,
 * 그것이 누른 것으로 읽힙니다.
 *
 * **판 안의 자리를 빼앗지 않습니다.** 글은 턱 위의 얼굴에 가운데로 놓이므로 단추의 바깥
 * 크기는 그대로입니다.
 */
export const LIP = 4

/**
 * 누를 수 있는 것.
 *
 * **판때기가 아니라 물건입니다.** 채움 하나와 테 하나로 그리던 동안 단추는 색이 칠해진
 * 네모였고, 그것은 웹의 단추이지 게임의 단추가 아닙니다. 층이 넷입니다.
 *
 * |층|무엇|
 * |--|--|
 * |턱|얼굴보다 어두운 같은 색. 단추의 두께입니다|
 * |얼굴|위가 밝고 아래가 바탕색인 세로 그라디언트|
 * |베벨|얼굴의 위쪽 안쪽에 한 줄. 빛이 위에서 옵니다|
 * |테|잉크색. 실루엣 전체를 두릅니다|
 *
 * **어두운 쪽과 밝은 쪽은 OKLCH 로 만듭니다.** 검정과 흰색을 섞으면 채도가 함께 빠져
 * 턱이 잿빛이 되고 베벨이 바랩니다 — 같은 색의 다른 면으로 보이려면 색상각과 채도가
 * 그대로여야 합니다.
 *
 * 판과 칸은 그대로 납작합니다. 두께가 필요한 것은 누르는 것뿐입니다.
 */
export function pressable(g: Graphics, width: number, height: number,
                          look: ButtonLook, pushed: boolean): void {
  const radius = RADIUS.small
  const half = STROKE.base / 2

  // **단추는 전부 두께를 가집니다.**
  //
  // 한때 길이 둘이었습니다 — 테가 있는 것은 바탕에 붙은 평면이고 꽉 찬 것만 턱을
  // 가졌습니다. 그것이 「잘 만든 웹의 어두운 화면」의 문법이고, 이 게임의 그림 화풍과는
  // 어긋납니다. 조용한 단추도 눌러서 내려앉는 물건이므로 턱이 있어야 합니다.
  //
  // 층이 넷입니다 — 아래의 턱 · 얼굴 · 얼굴 위의 밝은 줄 · 테.
  const faceH = height - LIP
  const top = pushed ? LIP : 0
  g.roundRect(0, 0, width, height, radius).fill(shade(look.face, -0.13))
  g.roundRect(0, top, width, faceH, radius)
    .fill(faceFill(faceH, shade(look.face, 0.05), look.face))
  g.moveTo(radius, top + 1.5).lineTo(width - radius, top + 1.5)
    .stroke({ color: shade(look.face, 0.13), width: STROKE.hair, alpha: 0.7 })

  // **테를 두르지 않습니다.**
  //
  // 밝은 테를 한 줄 둘러 보았고 답답했습니다 — 단추가 이미 턱으로 두께를 가지므로 테는
  // 그 두께를 한 줄로 가두는 것이 되고, 판 안에 단추가 여럿이면 그 줄들이 겹쳐 화면이
  // 좁아 보입니다. 단추의 윤곽은 아래의 턱과 얼굴의 경사가 만듭니다.
  //
  // `look.edge` 는 부르는 쪽이 아직 넘기므로 받되 쓰지 않습니다.
  void look.edge
  void half
}

/**
 * 단추 하나의 모습.
 *
 * **테가 있으면 납작하고 없으면 두껍습니다.** 판 계열의 단추는 밝은 테가 모양을 잡고,
 * 뜻이 있는 색의 단추는 그 색이 이미 모양을 잡으므로 테 대신 두께를 가집니다.
 */
export interface ButtonLook {
  face: number
  edge?: number
}

/** 얼굴의 그라디언트. **높이와 두 색이 같으면 같은 것을 다시 씁니다.** */
const FACES = new Map<string, FillGradient>()

function faceFill(height: number, top: number, bottom: number): FillGradient {
  const key = `${height}|${top}|${bottom}`
  let found = FACES.get(key)
  if (!found) {
    found = new FillGradient({
      start: { x: 0, y: 0 },
      end: { x: 0, y: 1 },
      colorStops: [{ offset: 0, color: top }, { offset: 1, color: bottom }],
      textureSpace: 'local',
    })
    FACES.set(key, found)
  }
  return found
}

export function mix(a: number, b: number, t: number): number {
  const channel = (shift: number) => {
    const ca = (a >> shift) & 0xff
    const cb = (b >> shift) & 0xff
    return Math.round(ca + (cb - ca) * t) & 0xff
  }
  return (channel(16) << 16) | (channel(8) << 8) | channel(0)
}
