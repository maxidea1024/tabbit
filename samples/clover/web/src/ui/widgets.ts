// 화면의 조각들.
//
// 버튼과 패널과 숫자 칸. **연출이 여기를 만집니다** — 숫자가 커지고 흔들리는 것은 위젯이
// 아니라 위젯을 쥔 쪽이 정합니다.

import { Container, Graphics, Text } from 'pixi.js'

import { COLOR } from '../render/theme'

export class Panel extends Container {
  private readonly plate = new Graphics()

  constructor(width: number, height: number, tint: number = COLOR.panel) {
    super()
    this.addChild(this.plate)
    this.resize(width, height, tint)
  }

  resize(width: number, height: number, tint: number = COLOR.panel): void {
    this.plate.clear()
    this.plate.roundRect(0, 0, width, height, 10).fill(tint)
    this.plate.roundRect(0.5, 0.5, width - 1, height - 1, 10)
      .stroke({ color: COLOR.panelEdge, width: 1 })
  }
}

export class Button extends Container {
  private readonly plate = new Graphics()
  private readonly caption = new Text({
    text: '', style: { fontSize: 15, fill: COLOR.ink, fontWeight: '700' },
  })

  private enabledState = true

  constructor(text: string, private readonly boxWidth: number,
              private readonly boxHeight: number,
              private readonly inkColor: number, onPress: () => void) {
    super()
    this.addChild(this.plate, this.caption)
    this.caption.anchor.set(0.5)
    this.caption.position.set(boxWidth / 2, boxHeight / 2)
    this.text = text

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.on('pointertap', () => { if (this.enabledState) onPress() })
    this.on('pointerover', () => { if (this.enabledState) this.draw(1.12) })
    this.on('pointerout', () => this.draw(1))
    this.draw(1)
  }

  set text(value: string) {
    this.caption.text = value
  }

  set enabled(value: boolean) {
    this.enabledState = value
    this.alpha = value ? 1 : 0.4
    this.cursor = value ? 'pointer' : 'default'
  }

  private draw(brighten: number): void {
    const color = brighten === 1 ? this.inkColor : lighten(this.inkColor, brighten)
    this.plate.clear()
    this.plate.roundRect(0, 0, this.boxWidth, this.boxHeight, 8).fill(color)
    this.plate.roundRect(0.5, 0.5, this.boxWidth - 1, this.boxHeight - 1, 8)
      .stroke({ color: COLOR.panelEdge, width: 1 })
  }
}

function lighten(color: number, factor: number): number {
  const r = Math.min(255, Math.round(((color >> 16) & 0xff) * factor))
  const g = Math.min(255, Math.round(((color >> 8) & 0xff) * factor))
  const b = Math.min(255, Math.round((color & 0xff) * factor))
  return (r << 16) | (g << 8) | b
}

/**
 * 숫자 하나가 있는 칸.
 *
 * **세는 것이 여기 있습니다.** 값이 바뀌면 즉시 그 값이 되지 않고 굴러갑니다 — 그것이
 * 점수가 쌓이는 것을 보는 재미의 절반입니다.
 */
export class Counter extends Container {
  private readonly value = new Text({ text: '0', style: { fontSize: 30, fill: COLOR.ink, fontWeight: '700' } })
  private readonly heading = new Text({ text: '', style: { fontSize: 11, fill: COLOR.inkDim } })

  private shown = 0
  private wanted = 0

  constructor(caption: string, private readonly inkColor: number, width: number) {
    super()
    this.addChild(this.heading, this.value)
    this.heading.text = caption
    this.heading.position.set(0, 0)
    this.value.style.fill = inkColor
    this.value.anchor.set(0.5, 0)
    this.value.position.set(width / 2, 16)
  }

  /** 곧바로 그 값이 됩니다. 라운드가 바뀔 때 씁니다. */
  reset(value: number): void {
    this.shown = value
    this.wanted = value
    this.redraw()
  }

  set target(value: number) {
    this.wanted = value
  }

  get settled(): boolean {
    return this.shown === this.wanted
  }

  /** 남은 거리의 일부씩 좁힙니다. 큰 수일수록 오래 굴러갑니다. */
  advance(deltaMs: number): void {
    if (this.shown === this.wanted) return
    const gap = this.wanted - this.shown
    const step = Math.max(1, Math.abs(gap) * (deltaMs / 140))
    this.shown = gap > 0
      ? Math.min(this.wanted, this.shown + step)
      : Math.max(this.wanted, this.shown - step)
    this.redraw()
  }

  /** 값이 클수록 크게 보입니다. 연출이 부릅니다. */
  emphasize(scale: number): void {
    this.value.scale.set(scale)
  }

  private redraw(): void {
    const shown = Math.round(this.shown)
    this.value.text = shown >= 100_000 ? shown.toExponential(2).replace('e+', 'e') : String(shown)
    this.value.style.fill = this.inkColor
  }
}
