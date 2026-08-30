// 화면의 조각들.
//
// 그리는 규칙은 `render/skin.ts` 에 있고 여기는 그것을 쓰는 자리입니다. **버튼과 패널이 같은
// 손으로 그려져야 화면이 한 벌로 보입니다.**

import { Container, Graphics, Text } from 'pixi.js'

import { buttonStyle, mix, plate, PANEL, type PlateStyle } from '../render/skin'
import { COLOR } from '../render/theme'

export class Panel extends Container {
  private readonly board = new Graphics()

  constructor(width: number, height: number, tint?: number) {
    super()
    this.addChild(this.board)
    this.resize(width, height, tint)
  }

  resize(width: number, height: number, tint?: number): void {
    const style: PlateStyle = tint === undefined
      ? PANEL
      : { ...PANEL, top: mix(tint, 0xffffff, 0.1), bottom: tint }
    this.board.clear()
    plate(this.board, width, height, style)
  }
}

export class Button extends Container {
  private readonly board = new Graphics()
  private readonly caption = new Text({
    text: '',
    style: {
      fontSize: 15, fill: COLOR.ink, fontWeight: '800',
      stroke: { color: 0x0a1610, width: 3 },
    },
  })

  private enabledState = true
  private lit = false

  constructor(text: string, private readonly boxWidth: number,
              private readonly boxHeight: number,
              private readonly base: number, onPress: () => void) {
    super()
    this.addChild(this.board, this.caption)
    this.caption.anchor.set(0.5)
    this.caption.position.set(boxWidth / 2, boxHeight / 2)
    this.text = text

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.on('pointertap', () => { if (this.enabledState) onPress() })
    this.on('pointerover', () => { if (this.enabledState) this.setLit(true) })
    this.on('pointerout', () => this.setLit(this.held))
    this.on('pointerdown', () => { if (this.enabledState) this.caption.y = boxHeight / 2 + 2 })
    this.on('pointerup', () => { this.caption.y = boxHeight / 2 })
    this.draw()
  }

  set text(value: string) {
    this.caption.text = value
  }

  set enabled(value: boolean) {
    if (this.enabledState === value) return
    this.enabledState = value
    this.alpha = value ? 1 : 0.42
    this.cursor = value ? 'pointer' : 'default'
    this.draw()
  }

  /**
   * 눌린 채로 두는 것.
   *
   * **탭에 씁니다** — 지금 보고 있는 탭이 어느 것인지가 보이지 않으면 그것은 탭이 아니라
   * 버튼 줄입니다. 마우스를 올렸을 때와 같은 모습이라 따로 배울 것이 없습니다.
   */
  set highlight(value: boolean) {
    this.held = value
    this.setLit(value)
  }

  private held = false

  private setLit(value: boolean): void {
    if (this.held && !value) return
    if (this.lit === value) return
    this.lit = value
    this.caption.y = this.boxHeight / 2
    this.draw()
  }

  private draw(): void {
    this.board.clear()
    plate(this.board, this.boxWidth, this.boxHeight, buttonStyle(this.base, this.lit))
  }
}
