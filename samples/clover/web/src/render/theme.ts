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
 * **일곱입니다.** 바탕 · 테 · 선 둘 · 칸 · 진행 바의 바탕이고, 전부 어두운 무채색입니다 —
 * 강조색은 테마와 무관하게 고정입니다. 그래야 「돈은 노랑」 같은 약속이 테마를 바꿔도
 * 그대로 남습니다.
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
    panel: 0x1b1d25, panelAlpha: 0.96, panelEdge: 0x4a3f36,
    rule: 0x3a3d4a, hairline: 0x2c2f3a, cell: 0x14161c, well: 0x0f1117,
  },
  /** 검정. 중성 검정에 회색 테. 색이 가장 적습니다. */
  ink: {
    panel: 0x17181c, panelAlpha: 0.96, panelEdge: 0x3d3d40,
    rule: 0x343437, hairline: 0x292a2d, cell: 0x101113, well: 0x0b0c0d,
  },
  /** 남색. 차가운 남색 — 이 게임이 오래 쓰던 색입니다. */
  navy: {
    panel: 0x1a2130, panelAlpha: 0.96, panelEdge: 0x3f4a5c,
    rule: 0x35404f, hairline: 0x28303c, cell: 0x131a26, well: 0x0d121b,
  },
  /** 밝은 회색. 판과 선이 한 단 밝아 테두리가 뚜렷합니다. */
  bright: {
    panel: 0x232733, panelAlpha: 0.98, panelEdge: 0x6a6f7d,
    rule: 0x4b5160, hairline: 0x3a3f4b, cell: 0x1a1e28, well: 0x12151d,
  },
}

/** 테마의 이름들. 옵션의 칸이 이 순서로 섭니다. */
export const UI_THEME_KEYS = ['slate', 'ink', 'navy', 'bright'] as const

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
  /** 그 밖의 단추. */
  sky: 0xc9e3ee,
  /** 고른 탭 · 권하지 않는 나아감(계정 없이 시작하기). */
  cream: 0xefe6d3,
  /** 물러나는 단추. */
  slate: 0x3d4450,
  /** 잠긴 단추. */
  locked: 0x6a6f78,
  /** 진행 바 · 요구 점수 · 최고 핸드. */
  bar: 0x35c5f0,
  /** 고른 것. 목록의 줄과 물건 칸. */
  pick: 0x1a7ad9,
  /** 승리 · 핸드 수. */
  green: 0x6fe0a8,
  /** 패배 · 모자란 수 · 살 수 없는 값 · 버리기. */
  red: 0xf07a6a,
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

/** 희귀도 하나의 색. */
export function rarityColor(rarity: number): number {
  switch (rarity) {
    case 2: return COLOR.uncommon
    case 3: return COLOR.rare
    case 4: return COLOR.legendary
    default: return COLOR.common
  }
}
