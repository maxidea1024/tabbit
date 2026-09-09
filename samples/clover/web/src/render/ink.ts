// 겉면을 따라가지 않는 색.
//
// **판의 색은 `theme.ts` 의 `UI` 이고, 여기 있는 것은 그것과 갈래가 다릅니다.** 카드는 판
// 위에 놓인 종이이고, 판의 색이 바뀌었다고 종이의 색이 바뀌면 그것은 다른 카드입니다 —
// 이 게임에서 가장 먼저 읽혀야 하는 것이 카드이므로 어느 겉면에서나 같은 종이여야 합니다.
//
// 상표의 색과 흰 마스크도 같은 이유로 여기 있습니다. 상표는 그 회사의 것이고, 마스크의
// 흰색은 색이 아니라 「전부」라는 뜻입니다.
//
// **손으로 적는 색은 이 파일과 겉면의 씨앗 여덟이 전부입니다.** 그리는 자리에 리터럴이
// 남아 있으면 그것은 어느 쪽에도 속하지 않은 것이고, 고칠 때 찾지 못합니다.

import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { PackKind } from '../generated/enums/pack-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { StakeKind } from '../generated/enums/stake-kind'

/** 종이와 잉크. */
export const COLOR = {
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

  /** 카드 모양으로 그리는 딱지의 종이. 팩과 참고의 카드가 씁니다. */
  slip: 0xefe6d3,
  /** 그 종이 위의 글. */
  slipInk: 0x2a2420,
  /** 그 종이 위의 흐린 글. */
  slipDim: 0x6b6255,
  /** 참고에서 아직 못 본 것의 뒷면. */
  unseen: 0x1b2431,
  unseenInk: 0x2f3d50,

  /** 카드 위에 까는 어두운 띠. 이름과 값이 그 위에 놓입니다. */
  band: 0x0b1018,
  /** 팩과 조커 딱지의 바탕. */
  slate: 0x141b26,
} as const

/**
 * 색이 아니라 도구인 것들.
 *
 * **마스크의 흰색은 색이 아니라 「전부」입니다.** 겉면을 따라가면 안 되고, 리터럴로 두면
 * 겉면의 색을 손으로 적은 자리와 구분되지 않습니다.
 */
export const PAINT = {
  /** 마스크. 이 자리가 보입니다. */
  mask: 0xffffff,
  /** 누를 수 있게만 하는 자리. 알파 0으로 깔립니다. */
  hit: 0x000000,
  /** 뒤를 덮는 검정. 알파는 부르는 쪽이 정합니다. */
  veil: 0x000000,
  /** 위에 얹는 흰빛. 줄무늬와 광택입니다. */
  sheen: 0xffffff,
} as const

/**
 * 힘없는 카드.
 *
 * **회색이 아니라 푸른 회색입니다.** 온전한 회색으로 두면 흑백 사진처럼 보이고, 판 위의
 * 다른 것들과 다른 갈래의 물건이 됩니다.
 */
export const DEAD = {
  ink: 0x5d6879,
  art: 0x4c5566,
  edge: 0x2a3140,
  mark: 0x9aa3ad,
} as const

/** 강화가 카드 바탕에 주는 색. */
export const ENHANCEMENT_PAPER: Partial<Record<EnhancementKind, number>> = {
  [EnhancementKind.Bonus]: 0xcfe0f5,
  [EnhancementKind.Mult]: 0xf5ccd2,
  [EnhancementKind.Wild]: 0xe6d6f5,
  [EnhancementKind.Glass]: 0xd8f0f5,
  [EnhancementKind.Steel]: 0xd6d6d6,
  [EnhancementKind.Stone]: 0xa9a396,
  [EnhancementKind.Gold]: 0xf3dc99,
  [EnhancementKind.Lucky]: 0xd2f0c6,
}

/**
 * 강화 칩의 글씨색.
 *
 * **종이색과 짝입니다.** 칩의 바탕이 어두우므로 글씨는 그 강화의 밝은 쪽이고, 그러면
 * 무엇이 붙었는지가 글을 읽기 전에 색으로 먼저 읽힙니다.
 */
export const ENHANCEMENT_INK: Partial<Record<EnhancementKind, number>> = {
  [EnhancementKind.Bonus]: 0x9ecbff,
  [EnhancementKind.Mult]: 0xff9fae,
  [EnhancementKind.Wild]: 0xd5aef7,
  [EnhancementKind.Glass]: 0x9fe4f0,
  [EnhancementKind.Steel]: 0xdadada,
  [EnhancementKind.Stone]: 0xd8d0bf,
  [EnhancementKind.Gold]: 0xffd873,
  [EnhancementKind.Lucky]: 0xa6ea8e,
}

/** 강화 칩의 기본 글씨색. 표에 없는 강화가 씁니다. */
export const ENHANCEMENT_PLAIN = 0xf2f6fb

export const SEAL_INK: Partial<Record<SealKind, number>> = {
  [SealKind.Red]: 0xd23b3b,
  [SealKind.Blue]: 0x3b7fd2,
  [SealKind.Gold]: 0xe0b53b,
  [SealKind.Purple]: 0x9a5bd2,
}

/** 팩 겉장의 색. */
export const PACK_INK: Partial<Record<PackKind, number>> = {
  [PackKind.Arcana]: 0x4a3a6b,
  [PackKind.Celestial]: 0x264a6b,
  [PackKind.Spectral]: 0x3a2a52,
  [PackKind.Buffoon]: 0x6b3a3a,
}
/** 표에 없는 팩. 카드 팩입니다. */
export const PACK_PLAIN = 0x2f5c42

/**
 * 스테이크의 색.
 *
 * **이름이 곧 색입니다.** 「붉은 스테이크」가 붉지 않으면 그 이름이 뜻을 잃으므로 겉면을
 * 따라가지 않습니다.
 */
export const STAKE_INK: Record<StakeKind, number> = {
  [StakeKind.White]: 0xf2ece0,
  [StakeKind.Red]: 0xc0392f,
  [StakeKind.Green]: 0x3d8b52,
  [StakeKind.Black]: 0x26262c,
  [StakeKind.Blue]: 0x2f6fc0,
  [StakeKind.Purple]: 0x9a5bd2,
  [StakeKind.Orange]: 0xd07a2f,
  [StakeKind.Gold]: 0xe0b53b,
}

/** 블라인드 표시. 큰 것과 작은 것입니다. */
export const BLIND_INK = { big: 0xa279e0, small: 0x5d92d6 } as const

/** 상점의 조커 딱지. **플레잉 카드는 카드의 테를 씁니다.** */
export const SHOP_JOKER = 0x9b8fd0

/** 족보 도움의 색. **고른 카드의 초록과 달라야 헷갈리지 않습니다.** */
export const HINT_INK = 0xffc53d
