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
