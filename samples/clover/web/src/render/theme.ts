// 색과 치수.
//
// **연출의 수치는 여기 없습니다** — 그것은 `Const_Feel` 이고 데이터입니다. 여기 있는 것은
// 팔레트와 카드의 크기처럼 데이터가 아닌 것들입니다.
//
// **색을 손으로 적는 자리는 아래의 씨앗 8개뿐입니다.** 겉면 하나가 색상각 하나와 채도
// 하나와 밝기 하나이고, 나머지는 `palette.ts` 의 표가 만듭니다 — 그 이유와 배수는 그쪽에
// 적혀 있습니다.

import { buildSurface, type Surface, type SurfaceSeed } from './palette'

/**
 * 겉면 여덟.
 *
 * **넷은 무채색에 가깝고 넷은 색이 있습니다.** 여덟 다 어두운 이유는 카드가 크림색
 * 종이이기 때문입니다 — 판이 밝으면 카드가 판에 묻힙니다.
 *
 * **이름으로 고르는 것이 아닙니다.** 옵션은 겉면마다 작은 판 하나를 그려 보여 주고, 고르는
 * 사람은 그 색을 보고 고릅니다 — 이름은 그 아래에 붙는 딱지입니다.
 *
 * `level` 은 판의 상대휘도입니다. **겉면 하나의 밝기가 이 숫자 하나입니다** — 칸도 단추도
 * 선도 이 값에서 배수로 나오므로, 밝기를 바꾸려면 여기만 고칩니다.
 */
const SEEDS: Record<string, SurfaceSeed> = {
  /** 기본. 남흑에 따뜻한 갈색 테 — 참고한 카드룸의 것입니다. */
  slate: { hue: 274, chroma: 0.016, level: 0.0300, alpha: 0.96, edgeHue: 73 },
  /** 검정. 거의 검정에 회색 테. 판이 배경에 잠기고 카드만 남습니다. */
  ink: { hue: 264, chroma: 0.005, level: 0.0240, alpha: 0.97 },
  /** 남색. 차가운 남색에 푸른 테 — 이 게임이 오래 쓰던 색입니다. */
  navy: { hue: 261, chroma: 0.051, level: 0.0400, alpha: 0.96 },
  /** 밝은 회색. 판과 테가 뚜렷하게 밝아 판의 경계가 멀리서도 보입니다. */
  bright: { hue: 261, chroma: 0.020, level: 0.0580, alpha: 0.98 },
  /** 초록. 카드를 늘어놓는 상의 색입니다 — 이 갈래의 게임에서 가장 오래된 색입니다. */
  green: { hue: 160, chroma: 0.029, level: 0.0320, alpha: 0.96 },
  /** 와인. 짙은 자주 — 붉음이 뜻을 가진 색이므로 판은 그보다 훨씬 어둡습니다. */
  wine: { hue: 350, chroma: 0.034, level: 0.0290, alpha: 0.96 },
  /** 갈색. 따뜻한 쪽입니다 — 크림색 카드와 같은 계열이라 판과 카드가 한 벌로 보입니다. */
  brown: { hue: 63, chroma: 0.018, level: 0.0300, alpha: 0.96 },
  /** 자주. 남색보다 한 걸음 더 간 쪽이고, 금색이 가장 잘 서는 바탕입니다. */
  violet: { hue: 291, chroma: 0.044, level: 0.0310, alpha: 0.96 },
}

/** 겉면의 이름들. 옵션의 칸이 이 순서로 놓입니다. */
export const UI_THEME_KEYS = ['slate', 'ink', 'navy', 'bright',
                              'green', 'wine', 'brown', 'violet'] as const

/**
 * 만들어 둔 겉면 여덟.
 *
 * **불러올 때 한 번 만듭니다.** 겉면 하나가 색 50 남짓이고 색 하나가 이분법 24회이므로 전부
 * 합해 밀리초 단위입니다 — 갈아입을 때마다 다시 만들 이유가 없습니다.
 */
export const UI_THEMES: Record<string, Surface> = Object.fromEntries(
  Object.entries(SEEDS).map(([key, seed]) => [key, buildSurface(seed)]),
)

/** 겉면 하나가 정하는 색들. 이름은 `palette.ts` 의 `Surface` 에 있습니다. */
export type UiTheme = Surface

/**
 * 지금 쓰는 색 한 벌.
 *
 * **객체 하나를 계속 씁니다.** `setUiTheme` 가 그 안의 값만 갈아 끼우므로, 그리는 자리는
 * `UI.panel` 처럼 그때그때 읽으면 됩니다 — 값을 미리 베껴 둔 자리는 겉면을 바꿔도 옛 색을
 * 그대로 씁니다(그래서 `skin.ts` 의 판때기 규격이 상수가 아니라 함수입니다).
 *
 * **뜻이 있는 색도 여기 있습니다.** 돈의 노랑과 되돌릴 수 없는 것의 붉음은 약속이지만,
 * 약속인 것은 계열이지 값 하나가 아닙니다 — 색상각은 겉면과 무관하게 고정이고 밝기만
 * 판을 따라갑니다.
 */
export const UI: Surface = { ...UI_THEMES.slate }

/**
 * 겉면을 갈아 끼웁니다. 없는 이름이면 기본입니다.
 *
 * **그린 것이 저절로 바뀌지는 않습니다.** 이미 그려 둔 판때기는 그때의 색으로 삼각화되어
 * 있으므로, 부르는 쪽이 다시 그려야 합니다.
 */
export function setUiTheme(key: string): void {
  Object.assign(UI, UI_THEMES[key] ?? UI_THEMES.slate)
}

/**
 * 글자 크기.
 *
 * **자리마다 적던 수를 이름으로 바꾼 것입니다.** 화면 전체에 18가지 크기가 있었고 그중
 * 12·13·14·15가 절반이었습니다 — 자리마다 고르다 보면 13이어야 할 자리에 12가 들어가고,
 * 그 둘의 차이는 고친 사람 말고는 아무도 알아보지 못합니다.
 *
 * **카드는 이 표를 쓰지 않습니다.** 카드 얼굴의 인덱스와 무늬는 종이 위의 인쇄물이고,
 * 화면의 글과 같은 단계를 나눌 이유가 없습니다 — `COLOR` 가 카드의 색을 따로 두는 것과
 * 같은 갈래입니다.
 */
export const TEXT = {
  /** 곁들이는 수 · 칸 아래의 개수. */
  micro: 10,
  /** 칩 · 딱지. */
  mini: 11,
  /** 이름표 · 흐린 설명. */
  small: 12,
  /** 본문. **가장 많이 쓰는 크기입니다.** */
  body: 13,
  /** 조금 큰 본문. 줄이 그 자리의 주인공일 때입니다. */
  copy: 14,
  /** 단추의 글. */
  base: 15,
  /** 판의 제목 줄 · 핸드의 이름. */
  big: 17,
  /** 판의 큰 제목. */
  lead: 20,
  /** 점수 · 끝난 판의 머리. */
  head: 23,
  /** 값 하나가 그 판의 주인공일 때. */
  display: 26,
  /** 굴러가는 점수. */
  banner: 30,
  /** 뒤에 옅게 깔리는 큰 글자. */
  hero: 34,
  /** 정산의 합계. */
  giant: 40,
} as const

/**
 * 줄 사이.
 *
 * **글자 크기에서 나옵니다.** 자리마다 적던 동안은 12픽셀 글에 12와 16이 함께 있었고,
 * 그 둘은 같은 문단이 다른 밀도로 놓이는 것입니다. 1.45는 한글의 받침이 윗줄에 닿지 않는
 * 가장 좁은 값입니다.
 */
export function leading(size: number): number {
  return Math.round(size * 1.45)
}

/** 글자의 굵기. **셋뿐입니다** — 넷째를 더하면 어느 것이 더 무거운지가 보이지 않습니다. */
export const WEIGHT = {
  /** 곁들이는 글. */
  normal: '700',
  /** 본문과 이름. */
  bold: '800',
  /** 그 판에서 가장 큰 것 하나. */
  heavy: '900',
} as const

/**
 * 모서리.
 *
 * **네 단계입니다.** 4·6·8·12 이고, 그 사이의 값(5·7·9·10)은 들여 그린 테가 계산해
 * 내는 것이지 고르는 것이 아닙니다 — `insetRadius()` 가 그 일을 합니다.
 */
export const RADIUS = {
  /** 칩 · 작은 딱지. */
  tight: 4,
  /** 칸 · 단추. */
  small: 6,
  /** 판. */
  base: 8,
  /** 크게 뜨는 판 · 알림. */
  large: 12,
} as const

/**
 * 테의 굵기.
 *
 * **셋입니다.** 1은 옅은 선, 1.5는 판과 단추의 테, 2는 고른 것입니다 — 고른 것이 굵어지는
 * 것은 색과 함께 두 가지로 알리기 위한 것이고, 색만으로 알리면 색을 가리기 어려운 사람에게
 * 아무것도 알리지 않는 것이 됩니다.
 */
export const STROKE = {
  hair: 1,
  base: 1.5,
  picked: 2,
} as const

/**
 * 사이의 자리.
 *
 * **8을 기준으로 오르내립니다.** 판 안의 여백과 줄 사이가 이 표에서 나옵니다 — 자리마다
 * 고르면 같은 갈래의 판 둘이 10과 12로 갈라지고, 그 차이는 나란히 놓았을 때만 보입니다.
 */
export const SPACE = {
  hair: 2,
  tight: 4,
  small: 6,
  base: 8,
  wide: 12,
  large: 16,
  huge: 24,
} as const

export const SIZE = {
  /** 기준 해상도. 화면이 이보다 크면 통째로 키웁니다. */
  width: 1280,
  height: 800,

  cardWidth: 88,
  cardHeight: 124,
  cardRadius: 9,

  /**
   * 조커 딱지의 크기. **플레잉 카드와 같습니다.**
   *
   * 조커는 카드입니다 — 크기가 다르면 줄에 섰을 때 다른 갈래의 물건으로 보이고, 그림도
   * 카드 비율로 그려 두었는데 담을 자리가 다른 비율이면 잘리거나 남습니다.
   */
  jokerWidth: 88,
  jokerHeight: 124,
} as const

/**
 * 왼쪽 판이 차지한 자리.
 *
 * **화면의 붙박이입니다.** 판이 도는 동안 늘 거기 있고, 떠 있는 판은 그것을 덮지
 * 않습니다 — 덮으면 지금 몇 점인지와 무엇이 걸려 있는지가 판을 여는 동안 사라집니다.
 */
export const SIDE_PANEL = { x: 16, width: 264, gap: 14 } as const

/**
 * 떠 있는 판의 왼쪽 변.
 *
 * **화면의 가로 가운데입니다.** 다만 가운데에 두었을 때 왼쪽 판을 침범하면 그만큼
 * 오른쪽으로 밀어 둡니다 — 완전한 가운데보다 왼쪽 판이 보이는 것이 먼저입니다.
 */
export function popupLeft(width: number): number {
  const keepOut = SIDE_PANEL.x + SIDE_PANEL.width + SIDE_PANEL.gap
  const center = SIZE.width / 2 - width / 2
  if (center >= keepOut) return center
  // **밀 자리가 없으면 가운데에 그대로 둡니다.** 넓은 판을 왼쪽 판 밖으로 밀면 오른쪽이
  // 화면을 넘어가고, 넘어간 만큼은 잘려 나갑니다 — 무대에 마스크가 걸려 있습니다.
  const limit = SIZE.width - width - SIDE_PANEL.x
  return keepOut <= limit ? keepOut : center
}

/** 그 판의 가로 가운데. 가운데를 기준으로 놓는 자리가 씁니다. */
export function popupCenter(width: number): number {
  return popupLeft(width) + width / 2
}

/** 희귀도 하나의 색. */
export function rarityColor(rarity: number): number {
  switch (rarity) {
    case 2: return UI.uncommon
    case 3: return UI.rare
    case 4: return UI.legendary
    default: return UI.common
  }
}
