// 화면의 조각들.
//
// 그리는 규칙은 `render/skin.ts` 에 있고 여기는 그것을 쓰는 자리입니다. **버튼과 패널이 같은
// 손으로 그려져야 화면이 한 벌로 보입니다.**

import { Container, Graphics, Rectangle, Sprite, Text } from 'pixi.js'

import { buttonStyle, mix, panelStyle, plate, type PlateStyle } from '../render/skin'
import { COLOR, UI } from '../render/theme'
import { outlined, outlineOf, outlineWidth, strokeWidthOf } from './font'
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
      ? panelStyle()
      : { ...panelStyle(), top: mix(tint, 0xffffff, 0.1), bottom: tint }
    this.board.clear()
    plate(this.board, width, height, style)
  }
}

/** 색의 밝기. 0 이 검정, 1 이 흰색입니다. */
function luminance(color: number): number {
  const r = ((color >> 16) & 0xff) / 255
  const g = ((color >> 8) & 0xff) / 255
  const b = (color & 0xff) / 255
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/**
 * 단추 글씨의 테두리 색.
 *
 * **한 자리에 둡니다.** 글을 적을 때마다 굵기를 다시 정하므로 색을 두 곳에 적게 됩니다.
 */
const CAPTION_OUTLINE = 0x0a1610

/**
 * 누른 자리에서 이만큼 움직이면 끈 것입니다. 화면 픽셀입니다.
 *
 * **굴리는 판 안의 단추를 위한 것입니다.** 목록은 줄로 가득하고 그 줄이 단추이므로,
 * 손가락으로 굴려 손을 떼는 자리는 언제나 어느 단추 위입니다 — 가리지 않으면 굴릴 때마다
 * 무언가가 눌립니다.
 */
const DRAG_SLOP = 12

/**
 * 테마를 따라가는 단추의 색들.
 *
 * **만들 때 받은 수가 아니라 그 수의 이름을 기억합니다.** 단추는 색을 수로 받고, 겉면을
 * 갈아 끼우면 그 수는 앞 겉면의 색입니다 — 뜻이 있는 색(노랑 · 붉음)은 고정이므로 그대로
 * 두고, 판의 색으로 만든 단추만 그때그때 다시 읽습니다.
 */
const THEMED = ['btn', 'light', 'cell', 'locked'] as const
type ThemedKey = typeof THEMED[number]

/** 이 색이 지금 겉면의 어느 색인가. 아니면 `undefined` 입니다. */
function themedKeyOf(color: number): ThemedKey | undefined {
  return THEMED.find(key => UI[key] === color)
}

export class Button extends Container {
  private readonly board = new Graphics()
  private readonly caption = new Text({
    text: '',
    style: {
      ...outlined(15, CAPTION_OUTLINE),
      fill: COLOR.ink, fontWeight: '800',
    },
  })

  private enabledState = true
  /** 이 누름이 시작된 화면의 자리. 끌기와 누르기를 가르는 데 씁니다. */
  private downAt?: { x: number; y: number }
  /** 마지막으로 적은 글. 같은 글을 다시 적지 않기 위한 것입니다. */
  private captionShown?: string
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
    this.themed = themedKeyOf(base)
    this.textSize = textSize
    this.caption.style.fontSize = textSize
    this.addChild(this.board, this.caption)
    this.caption.anchor.set(0.5)
    this.caption.position.set(boxWidth / 2, boxHeight / 2)
    this.text = text

    this.eventMode = 'static'
    this.cursor = 'pointer'
    // **버튼 소리는 여기 한 자리입니다.** 부르는 쪽마다 걸면 새로 만드는 버튼에서 반드시
    // 하나가 빠지고, 그 버튼만 소리 없이 눌립니다.
    this.on('pointertap', event => {
      if (!this.enabledState) return
      // 끌고 와서 이 단추 위에서 손을 뗀 것이면 누른 것이 아닙니다.
      const from = this.downAt
      this.downAt = undefined
      if (from) {
        const dx = event.global.x - from.x
        const dy = event.global.y - from.y
        if (dx * dx + dy * dy > DRAG_SLOP * DRAG_SLOP) return
      }
      Button.onPressed?.()
      onPress()
    })
    this.on('pointerover', () => { if (this.enabledState) this.setLit(true) })
    this.on('pointerout', () => this.setLit(this.held))
    this.on('pointerdown', event => {
      this.downAt = { x: event.global.x, y: event.global.y }
      if (this.enabledState) this.caption.y = boxHeight / 2 + 2
    })
    this.on('pointerup', () => { this.caption.y = boxHeight / 2 })
    this.draw()
  }

  /** 넘겨받은 글자 크기. 글이 길어 줄였다가 되돌릴 때 씁니다. */
  private textSize = 15

  /**
   * 단추에 적히는 글.
   *
   * **칸을 넘치면 글자를 줄입니다.** 말마다 길이가 다르므로 한국어에 맞춘 칸이 독일어에서
   * 넘칩니다 — 「Plasma-Deck · Violetter Einsatz」 가 200픽셀 칸의 양쪽으로 삐져나와 있었고,
   * 넘친 글은 잘리지도 않고 옆의 단추 위에 그려집니다.
   *
   * 칸을 넓히는 것으로는 끝나지 않습니다. 어느 말이 가장 긴지는 데이터가 정하고, 덱 15종과
   * 스테이크 8종의 조합이므로 가장 긴 것을 미리 셀 수도 없습니다.
   */
  set text(value: string) {
    // **같은 글이면 손대지 않습니다.** 아래의 줄이기가 글자 크기를 바꿀 때마다 글을 다시
    // 굽고, 조커 풀은 쪽을 넘길 때마다 단추 7개에 같은 글을 다시 적습니다.
    if (value === this.captionShown) return
    this.captionShown = value
    this.caption.style.fontSize = this.textSize
    this.caption.text = value

    // 양쪽에 8픽셀씩 남깁니다. 글이 테두리에 닿으면 칸이 터진 것으로 보입니다.
    const room = this.boxWidth - 16
    let size = this.textSize
    while (size > 9 && this.caption.width > room) {
      size -= 1
      this.caption.style.fontSize = size
    }

    // **테두리를 여기서 다시 정합니다.** 굵기는 글자 크기와 고른 말에서 나오는 값이고 둘
    // 다 여기서 바뀝니다 — 위의 줄이기가 크기를 9까지 내리고, 말이 바뀌면 `relabel` 이
    // 이 자리로 새 글을 넣습니다.
    this.applyInk()
  }

  /**
   * 글의 색과 테두리.
   *
   * **단추의 밝기가 정합니다.** 노랑 · 하늘 · 크림 위에 흰 글을 검은 테로 두르면 읽히지
   * 않으므로, 밝은 단추의 글은 어둡고 테가 없습니다.
   *
   * **글자 크기도 봅니다.** 테두리의 굵기는 크기에서 나오는 값이고, 긴 글은 위의 줄이기가
   * 크기를 9까지 내립니다.
   */
  private applyInk(): void {
    const light = luminance(this.shownBase) > 0.5
    this.caption.style.fill = light ? UI.onLight : COLOR.ink
    const size = this.caption.style.fontSize as number
    this.caption.style.stroke = outlineOf(light ? 0 : outlineWidth(size), CAPTION_OUTLINE)
  }

  /** 지금 글에 걸려 있는 테두리의 굵기. **검증 도구가 읽습니다.** */
  get inkWidth(): number {
    return strokeWidthOf(this.caption)
  }

  set enabled(value: boolean) {
    if (this.enabledState === value) return
    this.enabledState = value
    // **잠긴 단추는 옅어지는 것이 아니라 회색이 됩니다.** 밝은 단추를 알파로 죽이면 뒤의
    // 배경이 그 색에 섞여 노랑이 흙색으로, 붉음이 자주색으로 보입니다 — 잠긴 것은 잠긴
    // 것의 색을 가져야 합니다.
    this.alpha = 1
    this.caption.alpha = value ? 1 : 0.5
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

  /**
   * 지금 그릴 색.
   *
   * **고른 탭은 크림입니다.** 참고의 탭이 그렇고, 그러면 어느 탭을 보고 있는지가 밝기
   * 하나로 갈립니다 — 같은 색을 조금 밝히는 것으로는 고른 것이 드러나지 않습니다.
   * 잠긴 것은 잠긴 색을 가집니다.
   */
  private get shownBase(): number {
    if (!this.enabledState) return UI.locked
    if (this.held) return UI.light
    // 판의 색으로 만든 단추는 지금의 겉면에서 다시 읽습니다.
    return this.themed ? UI[this.themed] : this.base
  }

  /**
   * 겉면이 바뀌었을 때 다시 그립니다.
   *
   * **판때기는 그릴 때의 색으로 삼각화되어 있습니다.** 그래서 색만 갈아 끼워도 이미 그려
   * 둔 단추는 앞 겉면의 색으로 남습니다 — 화면에 오래 서 있는 단추들이 그렇습니다.
   */
  restyle(): void {
    this.draw()
  }

  /** 만들 때 받은 색이 겉면의 어느 것이었는가. 뜻이 있는 색이면 없습니다. */
  private readonly themed?: ThemedKey

  private draw(): void {
    const base = this.shownBase
    this.board.clear()
    plate(this.board, this.boxWidth, this.boxHeight, buttonStyle(base, this.lit))
    this.applyInk()
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

  /** 겉면이 바뀌었을 때 다시 그립니다. 단추와 같은 이유입니다. */
  restyle(): void {
    this.draw()
  }

  private draw(): void {
    this.board.clear()
    plate(this.board, this.box, this.box, buttonStyle(UI.cell, this.lit))
    if (this.mark) this.mark.tint = this.lit ? COLOR.ink : COLOR.inkDim
  }
}
