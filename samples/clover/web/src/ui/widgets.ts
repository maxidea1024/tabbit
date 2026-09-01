// 화면의 조각들.
//
// 그리는 규칙은 `render/skin.ts` 에 있고 여기는 그것을 쓰는 자리입니다. **버튼과 패널이 같은
// 손으로 그려져야 화면이 한 벌로 보입니다.**

import { Container, Graphics, Rectangle, Sprite, Text } from 'pixi.js'

import { buttonStyle, mix, plate, PANEL, type PlateStyle } from '../render/skin'
import { COLOR } from '../render/theme'
import { iconFor, type IconName } from './icon'

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

  /** 아무 버튼이나 눌렸을 때. 소리를 내는 쪽이 겁니다. */
  static onPressed?: () => void

  /**
   * @param textSize 글자 크기. **큰 단추는 글자도 커야 합니다** — 236 × 72 짜리 시작
   *   단추에 15픽셀 글자를 얹으면 단추 가운데에 작은 딱지가 하나 놓인 것으로 보입니다.
   *   판 안의 단추들은 기본값 그대로입니다.
   */
  constructor(text: string, private readonly boxWidth: number,
              private readonly boxHeight: number,
              private readonly base: number, onPress: () => void, textSize = 15) {
    super()
    this.caption.style.fontSize = textSize
    this.addChild(this.board, this.caption)
    this.caption.anchor.set(0.5)
    this.caption.position.set(boxWidth / 2, boxHeight / 2)
    this.text = text

    this.eventMode = 'static'
    this.cursor = 'pointer'
    // **버튼 소리는 여기 한 자리입니다.** 부르는 쪽마다 걸면 새로 만드는 버튼에서 반드시
    // 하나가 빠지고, 그 버튼만 소리 없이 눌립니다.
    this.on('pointertap', () => {
      if (!this.enabledState) return
      Button.onPressed?.()
      onPress()
    })
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

/**
 * 아이콘 하나짜리 버튼.
 *
 * **글이 없습니다.** 화면 구석에 서는 것들이라, 글을 넣으면 그 글의 길이가 자리를 정하고
 * 말이 바뀌는 날마다 배치가 흔들립니다 — 물음표와 톱니는 어느 말에서나 같은 것을 뜻합니다.
 *
 * 아이콘은 **가져온 것**입니다(`ui/icon.ts`). 직접 그려 보았는데 톱니가 해처럼 보였습니다.
 */
export class IconButton extends Container {
  private readonly board = new Graphics()
  private readonly mark?: Sprite
  private lit = false

  constructor(private readonly box: number, icon: IconName, onPress: () => void) {
    super()
    this.addChild(this.board)

    const texture = iconFor(icon)
    if (texture) {
      // **아이콘은 칸의 절반 조금 넘게.** 꽉 채우면 테두리에 붙어 답답하고, 작으면 무엇인지
      // 읽히지 않습니다.
      const size = box * 0.52
      this.mark = new Sprite(texture)
      this.mark.width = size
      this.mark.height = size
      this.mark.position.set((box - size) / 2, (box - size) / 2)
      this.addChild(this.mark)
    }

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.hitArea = new Rectangle(0, 0, box, box)
    this.on('pointertap', () => {
      Button.onPressed?.()
      onPress()
    })
    this.on('pointerover', () => this.setLit(true))
    this.on('pointerout', () => this.setLit(false))
    this.on('pointerdown', () => { if (this.mark) this.mark.y += 2 })
    this.on('pointerup', () => this.place())
    this.draw()
  }

  private place(): void {
    if (!this.mark) return
    this.mark.y = (this.box - this.mark.height) / 2
  }

  private setLit(value: boolean): void {
    if (this.lit === value) return
    this.lit = value
    this.place()
    this.draw()
  }

  private draw(): void {
    this.board.clear()
    plate(this.board, this.box, this.box, buttonStyle(0x3a4658, this.lit))
    if (this.mark) this.mark.tint = this.lit ? COLOR.ink : COLOR.inkDim
  }
}
