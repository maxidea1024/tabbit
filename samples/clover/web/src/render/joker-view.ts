// 조커 한 장.
//
// **무엇을 하는지 얼굴에 적혀 있어야 합니다.** 이름만으로는 살지 말지를 정할 수 없고,
// 그 설명은 `core/describe.ts` 가 효과 행에서 만듭니다 — 손으로 적은 문장이 아닙니다.
//
// 그림 파일이 아직 없으므로 식별자에서 만든 문양으로 그립니다. 같은 조커는 언제나 같은
// 모양이고, 희귀도가 테두리 색입니다.

import { Container, Graphics, Sprite, Text } from 'pixi.js'

import { EditionKind } from '../generated/enums/edition-kind'
import type { JokerInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { artFor } from './art'
import { drawGlyph, glyphFor, hashOf, hsl, shade, tintUp } from './glyph'
import { Motion, sway } from './motion'
import { COLOR, rarityColor, SIZE } from './theme'
import type { EditionLook } from './card-view'

const EDITION_SHADER: Partial<Record<EditionKind, EditionShader>> = {
  [EditionKind.Foil]: 'foil',
  [EditionKind.Holographic]: 'holo',
  [EditionKind.Polychrome]: 'poly',
  [EditionKind.Negative]: 'negative',
}

/** 식별자에서 색상 하나. 같은 조커는 언제나 같은 색입니다. */
function hueOf(text: string): number {
  return hashOf(text) % 360
}

export interface JokerLook {
  name: string
  rarity: number
  lines: string[]
  edition?: EditionLook
}

export class JokerView extends Container {
  readonly uid: number
  readonly motion = new Motion()
  look: JokerLook

  private readonly shadow = new Graphics()
  private readonly plate = new Graphics()
  private readonly emblem = new Graphics()
  /** 그림이 있으면 이것이 문양을 대신합니다. */
  private art?: Sprite
  private readonly nameText = new Text({
    text: '',
    style: {
      fontSize: 11, fill: COLOR.ink, align: 'center', fontWeight: '700',
      wordWrap: true, wordWrapWidth: SIZE.jokerWidth - 10,
    },
  })
  private readonly counter = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.mult, fontWeight: '800' },
  })
  private edition?: EditionFilter

  hovered = false
  pointer = 0

  constructor(joker: JokerInstance, look: JokerLook) {
    super()
    this.uid = joker.uid
    this.look = look
    this.addChild(this.shadow, this.plate, this.emblem, this.nameText, this.counter)
    this.pivot.set(SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
    this.set(joker, look)
  }

  set(joker: JokerInstance, look: JokerLook): void {
    this.look = look
    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const hue = hueOf(joker.jokerId)
    const edge = rarityColor(look.rarity)

    this.shadow.clear()
    this.shadow.roundRect(3, 5, w, h, 9).fill({ color: 0x000000, alpha: 0.4 })

    this.plate.clear()
    this.plate.roundRect(0, 0, w, h, 9).fill(hsl(hue, 0.35, 0.16))
    // 이름 띠. 희귀도 색을 옅게 깔아 어느 등급인지 얼굴에서 읽힙니다.
    this.plate.roundRect(0, h - 32, w, 32, 9).fill({ color: shade(edge, 0.62), alpha: 0.9 })
    this.plate.roundRect(0, 0, w, 26, 9).fill({ color: 0xffffff, alpha: 0.06 })
    this.plate.roundRect(1.5, 1.5, w - 3, h - 3, 9).stroke({ color: edge, width: 2.5 })
    this.plate.roundRect(4, 4, w - 8, h - 8, 7)
      .stroke({ color: 0xffffff, width: 1, alpha: 0.12 })

    // 문양.
    //
    // **그림 파일이 아직 없습니다.** 그때까지 도형 셋으로 두면 무엇을 사는 것인지 알 수
    // 없으므로, 뜻이 읽히는 문양 스물 중 하나를 식별자로 골라 그립니다 — 같은 조커는 언제나
    // 같은 문양입니다.
    this.emblem.clear()

    const plateTop = hsl(hue, 0.55, 0.30)
    const plateBottom = hsl((hue + 22) % 360, 0.6, 0.14)
    const glyphInk = tintUp(hsl(hue, 0.7, 0.62), 0.25)

    // 문양이 앉는 창. 액자 안의 그림처럼 보이게 합니다.
    const frameX = 7
    const frameY = 30
    const frameW = w - 14
    const frameH = 52
    this.emblem.roundRect(frameX, frameY, frameW, frameH, 6).fill(plateBottom)
    this.emblem.roundRect(frameX, frameY, frameW, frameH * 0.55, 6)
      .fill({ color: plateTop, alpha: 0.75 })
    this.emblem.roundRect(frameX + 0.75, frameY + 0.75, frameW - 1.5, frameH - 1.5, 5)
      .stroke({ color: shade(edge, 0.25), width: 1.5 })

    // **그림이 있으면 그림입니다.** 없는 동안만 문양을 그립니다.
    this.art?.destroy()
    this.art = undefined

    const texture = artFor('joker', joker.jokerId)
    if (texture) {
      const sprite = new Sprite(texture)
      sprite.width = frameW - 3
      sprite.height = frameH - 3
      sprite.position.set(frameX + 1.5, frameY + 1.5)
      this.art = sprite
      this.addChildAt(sprite, this.getChildIndex(this.emblem) + 1)
    } else {
      drawGlyph(this.emblem, glyphFor(joker.jokerId), w / 2, frameY + frameH / 2, 40, {
        fill: glyphInk,
        line: shade(glyphInk, 0.62),
      })
    }

    this.nameText.text = look.name
    this.nameText.anchor.set(0.5, 0)
    this.nameText.position.set(w / 2, h - 27)

    // 누적값을 얼굴에 적습니다 — 늘어나는 조커는 그것이 전부이기 때문입니다.
    const { chips, multAdd, multMul } = joker.counters
    const parts: string[] = []
    if (chips !== 0) parts.push(`+${chips}칩`)
    if (multAdd !== 0) parts.push(`+${(multAdd / 10_000).toFixed(0)}`)
    // 0 은 「곱이 없다」가 아니라 「아직 값이 없다」입니다. 적지 않습니다.
    if (multMul !== 10_000 && multMul !== 0) parts.push(`×${(multMul / 10_000).toFixed(2)}`)
    this.counter.text = parts.join(' ')
    this.counter.anchor.set(0.5, 0)
    this.counter.position.set(w / 2, 8)

    this.alpha = joker.disabled ? 0.35 : 1

    const shader = EDITION_SHADER[joker.edition]
    if (shader && look.edition) {
      this.edition = new EditionFilter(shader, {
        strength: look.edition.strength,
        flowSpeed: look.edition.flowSpeed,
        noise: look.edition.noise,
      })
      this.filters = [this.edition]
    } else {
      this.filters = []
      this.edition = undefined
    }
  }

  place(x: number, y: number): void {
    this.motion.to(x, y, 0)
  }

  /** 발동할 때 튀어오릅니다. */
  pop(strength = 1): void {
    this.motion.y.kick(-300 * strength)
    this.motion.rotation.kick((Math.random() - 0.5) * 22)
    this.motion.scale.target = 1 + 0.16 * strength
  }

  advance(seconds: number, time: number): void {
    this.motion.advance(seconds)
    this.edition?.advance(seconds, this.pointer)

    const wobble = sway(time, this.motion.phase, 1.1, 1.1)
    this.x = this.motion.x.value
    this.y = this.motion.y.value - (this.hovered ? 10 : 0)
      + sway(time, this.motion.phase * 1.3, 1.8, 0.7)
    this.rotation = (this.motion.rotation.value + wobble) * (Math.PI / 180)

    const want = this.hovered ? 1.1 : 1
    if (Math.abs(this.motion.scale.target - want) > 0.001) this.motion.scale.target = want
    this.scale.set(this.motion.scale.value)
    this.zIndex = this.hovered ? 300 : 0
  }
}
