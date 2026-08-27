// 색과 치수.
//
// **연출의 수치는 여기 없습니다** — 그것은 `Const_Feel` 이고 데이터입니다. 여기 있는 것은
// 팔레트와 카드의 크기처럼 데이터가 아닌 것들입니다.

export const COLOR = {
  /** 배경. 밤의 온실입니다. */
  ground: 0x0b1410,
  panel: 0x14231b,
  panelEdge: 0x24402f,

  ink: 0xe8f3ea,
  inkDim: 0x8fae98,

  chips: 0x5fb4ff,
  mult: 0xff5f6d,
  money: 0xffcf4a,

  cardFace: 0xf4f1e6,
  cardEdge: 0x2a2418,
  cardBack: 0x1d4d33,
  red: 0xc8323c,
  black: 0x241f1a,

  /** 희귀도. 상점과 조커 테두리가 씁니다. */
  common: 0x8fae98,
  uncommon: 0x4fc3a1,
  rare: 0xff6b6b,
  legendary: 0xc08bff,

  good: 0x6fe3a1,
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
