// 색과 치수.
//
// **연출의 수치는 여기 없습니다** — 그것은 `Const_Feel` 이고 데이터입니다. 여기 있는 것은
// 팔레트와 카드의 크기처럼 데이터가 아닌 것들입니다.

export const COLOR = {
  /**
   * 글 속에서 강조하는 색.
   *
   * **수는 칩과 같은 파랑, 이름은 금색입니다.** 화면의 다른 곳에서 수를 파랗게 쓰고 있으므로
   * 글 속의 수도 같은 파랑이어야 같은 것으로 읽힙니다.
   */
  accentNumber: 0x7fc4ff,
  accentTerm: 0xffd479,
  /** 배경. **짙은 남색입니다** — 초록 단색이면 화면이 한 가지 색으로 눌립니다. */
  ground: 0x0e1420,
  /**
   * 판 밖. **검정입니다.**
   *
   * 판은 1280 × 800 하나이고 창의 비율은 기계마다 다릅니다 — 남는 자리는 화면의 일부가
   * 아니라 잘라 낸 자리이므로, 배경과 가까운 색으로 두면 판의 끝이 어디인지가 흐려집니다.
   *
   * **같은 값이 세 곳에 있습니다** — 렌더러가 지우는 색, `index.html` 의 쪽 배경,
   * 데스크탑 창의 배경입니다. 셋 다 판 밖에 보이는 색이고, 하나만 다르면 그 기계에서만
   * 판의 옆에 다른 색 한 줄이 남습니다.
   */
  crop: 0x000000,
  panel: 0x232b38,
  panelEdge: 0x3f4a5c,

  ink: 0xeef2f7,
  inkDim: 0x93a1b5,

  /** 칩은 파랑, 배수는 빨강, 돈은 금색. **이 셋만 채도가 높습니다.** */
  chips: 0x0093ff,
  mult: 0xfe5f55,
  money: 0xffc53d,

  cardFace: 0xf6f2e8,
  cardEdge: 0x2b2a26,
  /**
   * 뒷면.
   *
   * **바탕은 크림이고 무늬가 붉습니다.** 붉은 바탕에 무늬를 얹으면 앞면과 뒤집힌 관계가
   * 되어, 뒤집히는 순간 종이가 바뀐 것으로 보입니다 — 같은 종이의 반대쪽이어야 합니다.
   */
  cardBack: 0xf2ece0,
  cardBackEdge: 0xc0392f,
  red: 0xd7343f,
  black: 0x1f2024,

  /** 희귀도. 상점과 조커 테두리가 씁니다. */
  common: 0x9aa8bb,
  uncommon: 0x4ec9a0,
  rare: 0xfe5f55,
  legendary: 0xb98cff,

  good: 0x63d68f,
  bad: 0xff7a7a,
} as const

/**
 * 테마 하나가 정하는 것.
 *
 * **열입니다.** 판의 겉면 일곱(바탕 · 테 · 선 둘 · 칸 · 바의 바탕)과 단추 셋입니다.
 *
 * **뜻이 있는 색은 테마에 없습니다.** 돈의 노랑, 되돌릴 수 없는 것의 붉음, 고른 것의 파랑,
 * 승리의 초록은 약속이므로 고정입니다 — 「돈은 노랑」 이 테마마다 달라지면 그것은 약속이
 * 아닙니다. 뜻이 없는 단추(닫기 · 메뉴 · 타이틀로 · 정렬)는 판의 일부이므로 테마를
 * 따라갑니다.
 */
export interface UiTheme {
  /** 판. 배경 위에 얹히므로 조금 비칩니다. */
  panel: number
  panelAlpha: number
  /** 판의 바깥 테. */
  panelEdge: number
  /** 구획을 나누는 선. */
  rule: number
  /** 줄과 줄을 가르는 더 옅은 선 · 칸의 테. */
  hairline: number
  /** 칸 · 입력 · 물건 칸의 바탕. 판보다 한 단 어둡습니다. */
  cell: number
  /** 진행 바의 바탕. */
  well: number

  /**
   * 설명 쪽지의 바탕과 테.
   *
   * **판보다 어둡습니다.** 쪽지는 판 위에 뜨는 것이라 판과 같은 색이면 어디까지가 쪽지인지가
   * 흐려집니다. 테는 희귀도가 있는 것에만 그 색이 들고, 없는 것은 이 색입니다.
   */
  tipBack: number
  tipEdge: number

  /**
   * 그 밖의 단추.
   *
   * **일반 단추는 테마를 따라갑니다.** 「닫기」·「메뉴」·「타이틀로」 처럼 뜻이 없는 단추가
   * 판과 다른 계열의 회색이면 판마다 두 벌의 회색이 섞입니다 — 뜻이 있는 단추(나아감의
   * 노랑, 되돌릴 수 없는 것의 붉음)만 고정입니다.
   */
  btn: number
  /** 밝은 단추 · 고른 탭. 어두운 단추와 짝입니다. */
  light: number
  /**
   * 잠긴 단추.
   *
   * **겉면의 색입니다.** 회색 하나로 고정해 두었더니 잠긴 단추만 판과 다른 계열이 되고,
   * 잠기는 단추가 많은 화면(낼 수 없는 동안의 「낸다」·「버린다」, 살 수 없는 물건)에서는
   * 그 회색이 판보다 먼저 보입니다 — 단추의 색을 판 쪽으로 절반쯤 당긴 값입니다.
   */
  locked: number
}

/**
 * 고를 수 있는 테마들.
 *
 * **넷 다 어둡습니다.** 밝은 테마를 두지 않은 이유는 카드가 크림색 종이이기 때문입니다 —
 * 판이 밝으면 카드가 판에 묻히고, 이 게임에서 가장 먼저 읽혀야 하는 것이 카드입니다.
 *
 * **이름으로 고르는 것이 아닙니다.** 옵션은 테마마다 작은 판 하나를 그려 보여 주고,
 * 고르는 사람은 그 색을 보고 고릅니다 — 이름은 그 아래에 붙는 딱지입니다.
 */
export const UI_THEMES: Record<string, UiTheme> = {
  /** 기본. 남흑에 따뜻한 갈색 테 — 참고한 카드룸의 것입니다. */
  slate: {
    tipBack: 0x12141a, tipEdge: 0x6b5a45,
    panel: 0x1b1d25, panelAlpha: 0.96, panelEdge: 0x6b5a45,
    rule: 0x3a3d4a, hairline: 0x2c2f3a, cell: 0x14161c, well: 0x0f1117,
    btn: 0x3d4450, light: 0xc9e3ee, locked: 0x2e323d,
  },
  /** 검정. 거의 검정에 회색 테. 판이 배경에 잠기고 카드만 남습니다. */
  ink: {
    tipBack: 0x0a0b0d, tipEdge: 0x44454b,
    panel: 0x101113, panelAlpha: 0.97, panelEdge: 0x44454b,
    rule: 0x2e2f33, hairline: 0x212226, cell: 0x08090a, well: 0x000000,
    btn: 0x33343a, light: 0xcfcfd4, locked: 0x232428,
  },
  /** 남색. 차가운 남색에 푸른 테 — 이 게임이 오래 쓰던 색입니다. */
  navy: {
    tipBack: 0x101a2c, tipEdge: 0x46618f,
    panel: 0x18263f, panelAlpha: 0.96, panelEdge: 0x46618f,
    rule: 0x33486b, hairline: 0x243450, cell: 0x101a2e, well: 0x0a1120,
    btn: 0x34465f, light: 0xbcd2ea, locked: 0x273751,
  },
  /** 밝은 회색. 판과 테가 뚜렷하게 밝아 판의 경계가 멀리서도 보입니다. */
  bright: {
    tipBack: 0x1f232b, tipEdge: 0x8b93a2,
    panel: 0x2f353f, panelAlpha: 0.98, panelEdge: 0x8b93a2,
    rule: 0x5a6273, hairline: 0x454c5a, cell: 0x21252d, well: 0x171a20,
    btn: 0x4a5160, light: 0xd7dde8, locked: 0x3e4451,
  },
  /** 초록. 카드를 늘어놓는 상의 색입니다 — 이 갈래의 게임에서 가장 오래된 색입니다. */
  green: {
    tipBack: 0x0d1913, tipEdge: 0x4f6f52,
    panel: 0x14251c, panelAlpha: 0.96, panelEdge: 0x4f6f52,
    rule: 0x2c4634, hairline: 0x1e3325, cell: 0x0e1b14, well: 0x081109,
    btn: 0x304a37, light: 0xc7e2cc, locked: 0x23392b,
  },
  /** 와인. 짙은 자주 — 붉음이 뜻을 가진 색이므로 판은 그보다 훨씬 어둡습니다. */
  wine: {
    tipBack: 0x1c0f16, tipEdge: 0x86505d,
    panel: 0x2a1720, panelAlpha: 0.96, panelEdge: 0x86505d,
    rule: 0x4c2d38, hairline: 0x371f29, cell: 0x1e1017, well: 0x150a10,
    btn: 0x4e2e3a, light: 0xecc9d2, locked: 0x3e242e,
  },
  /** 갈색. 따뜻한 쪽입니다 — 크림색 카드와 같은 계열이라 판과 카드가 한 벌로 보입니다. */
  brown: {
    tipBack: 0x18120d, tipEdge: 0x8a6c48,
    panel: 0x241c15, panelAlpha: 0.96, panelEdge: 0x8a6c48,
    rule: 0x483a29, hairline: 0x342a1e, cell: 0x1a140f, well: 0x120d09,
    btn: 0x4d3c29, light: 0xe8d8ba, locked: 0x3b2e20,
  },
  /** 자주. 남색보다 한 걸음 더 간 쪽이고, 금색이 가장 잘 서는 바탕입니다. */
  violet: {
    tipBack: 0x151222, tipEdge: 0x6b5f9e,
    panel: 0x1f1b32, panelAlpha: 0.96, panelEdge: 0x6b5f9e,
    rule: 0x3b3459, hairline: 0x282342, cell: 0x161327, well: 0x0f0c1c,
    btn: 0x3f376a, light: 0xd2cbf2, locked: 0x312a51,
  },
}

/** 테마의 이름들. 옵션의 칸이 이 순서로 섭니다. */
export const UI_THEME_KEYS = ['slate', 'ink', 'navy', 'bright',
                              'green', 'wine', 'brown', 'violet'] as const

/**
 * 지금 쓰는 색 한 벌.
 *
 * **객체 하나를 계속 씁니다.** `setUiTheme` 가 그 안의 값만 갈아 끼우므로, 그리는 자리는
 * `UI.panel` 처럼 그때그때 읽으면 됩니다 — 값을 미리 베껴 둔 자리는 테마를 바꿔도 옛 색을
 * 그대로 씁니다(그래서 `skin.ts` 의 판때기 규격이 상수가 아니라 함수입니다).
 *
 * **강조색은 테마에 없습니다.** 값 · 돈 · 고른 것 · 잠긴 것의 색은 약속이므로 고정입니다.
 */
export const UI = {
  ...UI_THEMES.slate,

  /** 구획 머리의 마름모. */
  mark: 0xcfd6e2,
  /** 모든 테의 잉크. 단추와 카드의 테입니다. */
  ink: 0x15171d,

  /** 값 · 돈 · 나아가는 단추. */
  yellow: 0xf5c518,
  /** 진행 바 · 요구 점수 · 최고 핸드. */
  bar: 0x35c5f0,
  /** 고른 것. 목록의 줄과 물건 칸. */
  pick: 0x1a7ad9,
  /** 승리 · 핸드 수. */
  green: 0x6fe0a8,
  /** 패배 · 모자란 수 · 살 수 없는 값 · 버리기. */
  red: 0xf07a6a,
  /**
   * 걸어 보는 것. **블라인드를 건너뛰는 단추입니다.**
   *
   * 상금을 버리고 태그 하나를 받는 것이므로 「그 밖의 일」이 아닙니다 — 판의 색으로 두면
   * 닫기와 같은 무게로 보이고, 노랑으로 두면 나아가는 길로 보입니다. 둘 다 아닌 자리에
   * 주황이 하나 있습니다.
   */
  dare: 0xd9772f,
  /** 밝은 단추 위의 글. */
  onLight: 0x1b1a17,
}

/**
 * 테마를 갈아 끼웁니다. 없는 이름이면 기본입니다.
 *
 * **그린 것이 저절로 바뀌지는 않습니다.** 이미 그려 둔 판때기는 그때의 색으로 삼각화되어
 * 있으므로, 부르는 쪽이 다시 그려야 합니다.
 */
export function setUiTheme(key: string): void {
  Object.assign(UI, UI_THEMES[key] ?? UI_THEMES.slate)
}

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
  return Math.max(keepOut, SIZE.width / 2 - width / 2)
}

/** 그 판의 가로 가운데. 가운데를 기준으로 놓는 자리가 씁니다. */
export function popupCenter(width: number): number {
  return popupLeft(width) + width / 2
}

/** 희귀도 하나의 색. */
export function rarityColor(rarity: number): number {
  switch (rarity) {
    case 2: return COLOR.uncommon
    case 3: return COLOR.rare
    case 4: return COLOR.legendary
    default: return COLOR.common
  }
}
