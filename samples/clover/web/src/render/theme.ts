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
  panel: 0x232b38,
  panelEdge: 0x3f4a5c,

  ink: 0xeef2f7,
  inkDim: 0x93a1b5,

  /** 칩은 파랑, 배수는 빨강, 돈은 금색. **이 셋만 채도가 높습니다.** */
  chips: 0x3f9dff,
  mult: 0xfe5f55,
  money: 0xffc53d,

  cardFace: 0xf6f2e8,
  cardEdge: 0x2b2a26,
  cardBack: 0x8f2f3c,
  cardBackEdge: 0xd1626c,
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

export const SIZE = {
  /** 기준 해상도. 화면이 이보다 크면 통째로 키웁니다. */
  width: 1280,
  height: 800,

  cardWidth: 88,
  cardHeight: 124,
  cardRadius: 9,

  jokerWidth: 84,
  jokerHeight: 116,
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
