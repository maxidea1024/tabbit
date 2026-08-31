// 왼쪽 패널.
//
// **눈이 여기부터 갑니다.** 블라인드가 무엇을 요구하는지, 지금 점수가 얼마인지, 칩과 배수가
// 얼마인지가 한 덩어리로 붙어 있어야 판단이 됩니다.

import { Container, Graphics, Text } from 'pixi.js'
import { tf } from '../core/strings'

import { mix, plate, slotStyle } from './skin'
import { COLOR } from './theme'

/** 값 하나가 들어가는 칸. */
export class Slot extends Container {
  private readonly plate = new Graphics()
  private readonly caption_ = new Text({
    text: '', style: { fontSize: 10, fill: COLOR.inkDim, fontWeight: '700' },
  })
  private readonly value = new Text({
    text: '0',
    style: {
      fontSize: 23, fill: COLOR.ink, fontWeight: '800',
      stroke: { color: 0x0a0f18, width: 3 },
    },
  })

  /**
   * 이 칸이 얼마나 타고 있는가. 0..1.
   *
   * **바탕이 비칩니다.** 불은 칸 뒤에 있고 칸은 불투명이라, 그대로 두면 아무것도 보이지
   * 않습니다 — 위로 옮기면 점수 칸을 덮고, 아래로 옮기면 바닥에서 새어 나오는 것으로
   * 보입니다. 타는 것은 이 칸이므로 이 칸이 비쳐야 합니다.
   */
  private burn = 0
  private shown = 0
  private wanted = 0
  private numeric = true
  /** 값이 바뀌었을 때의 튐. **툭 바뀌는 숫자는 아무 느낌도 주지 않습니다.** */
  private pop = 0
  private lastText = ''
  /** 이미 조용한 모습으로 돌려놓았는가. 매 프레임 다시 그리지 않기 위한 것입니다. */
  private settledLook = true

  constructor(caption: string, private readonly boxWidth: number,
              private readonly boxHeight: number, private readonly ink: number,
              valueSize = 23) {
    super()
    this.value.style.fontSize = valueSize
    this.addChild(this.plate, this.caption_, this.value)
    this.caption_.text = caption
    this.caption_.anchor.set(0.5, 0)
    this.caption_.position.set(boxWidth / 2, 6)
    this.value.anchor.set(0.5, 0.5)
    this.value.position.set(boxWidth / 2, boxHeight / 2 + 6)
    this.value.style.fill = ink
    this.draw()
  }

  private draw(glow = 0): void {
    const style = slotStyle(this.ink)
    this.plate.clear()
    plate(this.plate, this.boxWidth, this.boxHeight, {
      ...style,
      top: glow > 0 ? mix(style.top, this.ink, glow * 0.35) : style.top,
      weight: 1.5 + glow * 2 + this.burn * 1.5,
    })
    this.plate.alpha = 1 - this.burn * 0.82
  }

  /** 칸의 이름. **말이 바뀌면 갈아 끼웁니다** — 만들 때 한 번 읽고 마는 글입니다. */
  set caption(value: string) {
    this.caption_.text = value
  }

  /** 이 칸이 타오르는 세기. 바탕이 그만큼 비칩니다. */
  set heat(value: number) {
    const next = Math.max(0, Math.min(1, value))
    if (Math.abs(next - this.burn) < 0.01) return
    this.burn = next
    this.draw()
    this.settledLook = false
  }

  /** 숫자가 아닌 값. 바뀌면 한 번 튑니다. */
  set text(value: string) {
    this.numeric = false
    if (this.value.text !== value) {
      if (this.lastText !== '') this.pop = 1
      this.lastText = value
    }
    this.value.text = value
  }

  reset(value: number): void {
    this.numeric = true
    this.shown = value
    this.wanted = value
    this.redraw()
  }

  set target(value: number) {
    this.numeric = true
    if (value !== this.wanted) this.pop = Math.min(1, Math.abs(value - this.shown) / 400 + 0.35)
    this.wanted = value
  }

  get settled(): boolean { return this.shown === this.wanted }

  /**
   * 남은 거리의 일부씩 좁힙니다. 큰 수일수록 오래 굴러갑니다.
   *
   * **굴러가는 동안 숫자가 떱니다.** 값이 매끄럽게 올라가기만 하면 「바뀌었다」로 읽히고,
   * 흔들리면서 올라가면 「쌓이고 있다」로 읽힙니다. 흔드는 세기는 남은 거리에 따릅니다 —
   * 큰 수가 굴러갈 때 크게 떨고, 다 굴러가면 조용히 제자리에 섭니다.
   */
  advance(deltaMs: number): void {
    const rolling = this.numeric && this.shown !== this.wanted
    const heat = rolling
      ? Math.min(1, Math.abs(this.wanted - this.shown) / 240 + 0.4)
      : 0

    if (this.pop > 0) this.pop = Math.max(0, this.pop - deltaMs / 260)

    const ease = this.pop * this.pop
    const shake = Math.max(heat, ease)
    if (shake > 0.002) {
      // 튀는 것과 떠는 것을 같이 얹습니다.
      this.value.scale.set(1 + ease * 0.42 + heat * 0.14)
      this.value.x = this.boxWidth / 2 + (Math.random() - 0.5) * 7 * shake
      this.value.y = this.boxHeight / 2 + 6 - ease * 5 + (Math.random() - 0.5) * 6 * shake
      this.value.rotation = (Math.random() - 0.5) * 0.13 * shake
      this.draw(Math.min(1, shake))
    } else if (this.settledLook !== true) {
      this.settledLook = true
      this.value.scale.set(1)
      this.value.position.set(this.boxWidth / 2, this.boxHeight / 2 + 6)
      this.value.rotation = 0
      this.draw()
    }
    if (shake > 0.002) this.settledLook = false

    if (!rolling) return
    const gap = this.wanted - this.shown
    const step = Math.max(1, Math.abs(gap) * (deltaMs / 130))
    this.shown = gap > 0
      ? Math.min(this.wanted, this.shown + step)
      : Math.max(this.wanted, this.shown - step)
    this.redraw()
  }

  /** 값이 클수록 크게, 그리고 테두리가 밝아집니다. */
  emphasize(scale: number): void {
    if (this.pop > 0) return
    this.value.scale.set(scale)
    this.draw(Math.max(0, Math.min(1, (scale - 1) * 2)))
  }

  private redraw(): void {
    const shown = Math.round(this.shown)
    this.value.text = shown >= 1_000_000
      ? shown.toExponential(2).replace('e+', 'e')
      : shown.toLocaleString('en-US')
  }
}

/**
 * 블라인드 하나의 딱지.
 *
 * **색이 어느 블라인드인지 말합니다** — 스몰은 파랑, 빅은 보라, 보스는 붉습니다. 이름을 읽지
 * 않아도 어디까지 왔는지가 보입니다.
 */
export class BlindBadge extends Container {
  private readonly plate = new Graphics()
  private readonly title = new Text({
    text: '', style: { fontSize: 17, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly need = new Text({
    text: '',
    style: {
      fontSize: 30, fill: COLOR.chips, fontWeight: '800',
      stroke: { color: 0x0a0f18, width: 4 },
    },
  })
  private readonly note = new Text({
    text: '',
    style: {
      fontSize: 11, fill: COLOR.inkDim, lineHeight: 15,
      wordWrap: true, wordWrapWidth: 234,
    },
  })
  private readonly reward = new Text({
    text: '', style: { fontSize: 13, fill: COLOR.money, fontWeight: '700' },
  })

  constructor(private readonly boxWidth: number) {
    super()
    this.addChild(this.plate, this.title, this.need, this.reward, this.note)
  }

  set(name: string, target: number, reward: number, note: string,
      boss: boolean, big = false): void {
    const height = 138 + (note.length > 0 ? 26 : 0)
    const tint = boss ? 0x3d1622 : big ? 0x2a2140 : 0x1b2c44
    const edge = boss ? COLOR.bad : big ? 0xa279e0 : 0x5d92d6

    this.plate.clear()
    plate(this.plate, this.boxWidth, height, {
      top: mix(tint, 0xffffff, 0.14), bottom: mix(tint, 0x000000, 0.3),
      border: edge, radius: 12, weight: 2, drop: 5, gloss: 0.24,
    })
    // 이름이 앉는 띠.
    this.plate.roundRect(6, 6, this.boxWidth - 12, 32, 8)
      .fill({ color: edge, alpha: 0.28 })

    this.title.text = name
    this.title.anchor.set(0.5, 0)
    this.title.position.set(this.boxWidth / 2, 12)

    this.need.text = target.toLocaleString('en-US')
    this.need.anchor.set(0.5, 0)
    this.need.position.set(this.boxWidth / 2, 52)

    this.reward.text = tf('ui.blind.reward', { n: reward })
    this.reward.anchor.set(0.5, 0)
    this.reward.position.set(this.boxWidth / 2, 92)

    this.note.text = note
    this.note.anchor.set(0.5, 0)
    this.note.position.set(this.boxWidth / 2, 116)
  }
}
