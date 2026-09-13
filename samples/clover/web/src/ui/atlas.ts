// 이 파일은 design-data/tools/ui.py 가 씁니다. 손으로 고치지 않습니다.
//
// 굽는 배율이 2 이므로 놓을 때 1 / 2 로 줄입니다. 여기의 값은 전부 1배 기준입니다.

export type Slice = {
  /** 1배 기준의 본디 크기입니다. */
  w: number
  h: number
  /** 겉면 밖으로 나가는 그림자의 여백입니다. 놓을 때 이만큼 물러앉습니다. */
  pad: number
  left: number
  right: number
  top: number
  bottom: number
}

export const BAKE_SCALE = 1 / 2

export const ATLAS: Record<string, Slice> = {
  'hud-shell': { w: 264, h: 756, pad: 0, left: 24, right: 24, top: 72, bottom: 64 },
  'blind-badge': { w: 264, h: 212, pad: 0, left: 28, right: 28, top: 52, bottom: 34 },
  'plate': { w: 96, h: 96, pad: 0, left: 24, right: 24, top: 6, bottom: 24 },
  'well': { w: 96, h: 48, pad: 0, left: 18, right: 18, top: 12, bottom: 12 },
  'tray': { w: 96, h: 96, pad: 0, left: 14, right: 14, top: 8, bottom: 8 },
  'head': { w: 96, h: 64, pad: 0, left: 14, right: 14, top: 8, bottom: 10 },
  'keycap': { w: 64, h: 36, pad: 14, left: 12, right: 12, top: 10, bottom: 14 },
  'gauge': { w: 48, h: 12, pad: 0, left: 6, right: 6, top: 0, bottom: 0 },
  'gauge-fill': { w: 48, h: 8, pad: 0, left: 6, right: 6, top: 0, bottom: 0 },
  'glow-edge': { w: 64, h: 2, pad: 0, left: 0, right: 0, top: 0, bottom: 0 },
  'button-sm': { w: 108, h: 36, pad: 14, left: 20, right: 20, top: 10, bottom: 14 },
  'button': { w: 144, h: 48, pad: 14, left: 22, right: 22, top: 10, bottom: 14 },
  'button-lg': { w: 180, h: 60, pad: 14, left: 24, right: 24, top: 10, bottom: 14 },
  'button-xl': { w: 216, h: 72, pad: 14, left: 26, right: 26, top: 10, bottom: 14 },
  'card-torn-1': { w: 88, h: 124, pad: 0, left: 0, right: 0, top: 0, bottom: 0 },
  'card-torn-2': { w: 88, h: 124, pad: 0, left: 0, right: 0, top: 0, bottom: 0 },
  'card-torn-3': { w: 88, h: 124, pad: 0, left: 0, right: 0, top: 0, bottom: 0 },
  'card-torn-4': { w: 88, h: 124, pad: 0, left: 0, right: 0, top: 0, bottom: 0 },
}

/** 단추 높이의 계단 넷입니다. 그 사이 값은 쓰지 않습니다. */
export const RUNG = {
  'button-sm': { height: 36, font: 12, cut: 8 },
  'button': { height: 48, font: 24, cut: 10 },
  'button-lg': { height: 60, font: 24, cut: 12 },
  'button-xl': { height: 72, font: 36, cut: 14 }
} as const

export type RungName = keyof typeof RUNG

/** 카드의 뜯긴 가장자리 마스크의 수. 카드마다 하나를 돌려 씁니다. */
export const TORN_COUNT = 4
