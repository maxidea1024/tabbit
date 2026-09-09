// 이름.
//
// **로그인 화면과 타이틀이 같은 것을 씁니다.** 같은 게임의 첫 두 화면이고, 이름이 두
// 화면에서 다르게 적히면 그 사이를 지날 때 갈아 끼워진 것으로 보입니다 — 크기만 다릅니다.
//
// **글자 위의 도안을 걷었습니다.** 네 잎을 원 넷으로 그려 이름 위에 얹어 두었는데, 원 넷은
// 어느 배율에서도 잎으로 읽히지 않고 이름과 겹쳐 덩어리 하나가 되었습니다. 이름 자체가
// 표식이므로 그 위에 표식을 하나 더 둘 자리가 없습니다.
//
// **위가 밝고 아래가 짙습니다.** 단색으로 두면 이 크기에서 색 하나가 화면의 가운데를
// 통째로 차지하고, 그 색이 곧 화면의 밝기가 됩니다.

import { Container, FillGradient, Text } from 'pixi.js'

import { outlineOf } from './font'

/**
 * 이름의 색.
 *
 * **위에서 아래로 셋입니다.** 둘이면 가운데가 두 색의 평균으로 지나가고, 그 평균은
 * 어느 쪽도 아닌 색입니다 — 가운데에 가장 밝은 초록을 두면 글자가 안에서 빛나는 것으로
 * 보입니다.
 */
/**
 * **브랜드 색입니다.** `COLOR.good`(승리의 초록)과 떼어 놓습니다 — 그 초록은 게임 안의
 * 약속이므로 브랜드를 그 위에 얹으면 승리 표시가 함께 바뀝니다.
 *
 * 초록이던 동안 배경 그림의 밝은 크림색 붓질과 붙어 이름이 묻혔습니다. 자홍 계열은 그
 * 크림과 어긋나므로 밝은 자리에서도 떨어져 보입니다.
 */
const TOP = 0xfff0fb
const MIDDLE = 0xff5fc8
const BOTTOM = 0x9c1c6b
const STOPS = [
  { offset: 0, color: TOP },
  { offset: 0.42, color: MIDDLE },
  { offset: 1, color: BOTTOM },
] as const

/** 테두리. **남보라 바탕과 같은 계열의 어두움입니다** — 검정이면 글자만 오려 붙인 것이 됩니다. */
const EDGE = 0x0a1024

/** 그림자. 이름 하나에만 걸립니다 — 판때기의 문법과는 다른 자리입니다. */
const SHADOW = 0x05030f

/**
 * 브랜드 이름.
 *
 * **한 자리에 둡니다.** 타이틀과 로그인이 같은 것을 쓰므로, 이름이 바뀔 때 고치는 자리가
 * 하나여야 합니다. 리브랜딩 계획은 `notes/hypephoria-rebrand-plan.md` 에 있습니다.
 */
export const BRAND = 'HYPEPHORIA'

export class Wordmark extends Container {
  private readonly word: Text
  private time = 0

  /**
   * @param size 글자 크기.
   * @param float 위아래로 흔들리는 폭. 0이면 멈춰 있습니다.
   */
  constructor(size: number, private readonly float = 0) {
    super()
    this.word = new Text({
      text: BRAND,
      style: {
        fontSize: size,
        fontWeight: '800',
        // **10글자에 맞춘 자간입니다.** `clover` 6글자 때는 0.07 이었고, 그 값으로
        // `HYPEPHORIA` 를 적으면 화면 폭을 넘습니다.
        letterSpacing: Math.round(size * 0.02),
        fill: new FillGradient({
          start: { x: 0, y: 0 },
          end: { x: 0, y: 1 },
          colorStops: [...STOPS],
          textureSpace: 'local',
        }),
        // **여기만 굵기를 손으로 정합니다.** 배수는 어느 글자가 올지 모르는 자리의 위쪽
        // 한계이고, 이 글은 `clover` 여섯 자로 고정이라 그 한계보다 굵어도 속이 막히지
        // 않습니다.
        stroke: outlineOf(Math.round(size * 0.13), EDGE),
        dropShadow: {
          color: SHADOW, alpha: 0.9, blur: 12,
          distance: Math.round(size * 0.05), angle: Math.PI / 2,
        },
      },
    })
    this.word.anchor.set(0.5, 0)
    this.addChild(this.word)
  }

  /** 글자가 실제로 차지한 높이. 그 아래에 무엇을 둘지 정하는 쪽이 씁니다. */
  get wordHeight(): number {
    return this.word.height
  }

  advance(seconds: number): void {
    if (this.float === 0) return
    this.time += seconds
    this.word.y = Math.sin(this.time * 0.7) * this.float
  }
}
