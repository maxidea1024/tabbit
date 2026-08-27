// 조커 한 장.
//
// **무엇을 하는지 얼굴에 적혀 있어야 합니다.** 이름만으로는 살지 말지를 정할 수 없고,
// 그 설명은 `core/describe.ts` 가 효과 행에서 만듭니다 — 손으로 적은 문장이 아닙니다.
//
// 그림 파일이 아직 없으므로 식별자에서 만든 문양으로 그립니다. 같은 조커는 언제나 같은
// 모양이고, 희귀도가 테두리 색입니다.

import { Container, Graphics, Text } from 'pixi.js'

import { EditionKind } from '../generated/enums/edition-kind'
import type { JokerInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { Motion, sway } from './motion'
import { COLOR, rarityColor, SIZE } from './theme'
import type { EditionLook } from './card-view'

const EDITION_SHADER: Partial<Record<EditionKind, EditionShader>> = {
  [EditionKind.Foil]: 'foil',
  [EditionKind.Holographic]: 'holo',
  [EditionKind.Polychrome]: 'poly',
  [EditionKind.Negative]: 'negative',
}

/** 식별자에서 색 하나. 같은 조커는 언제나 같은 색입니다. */
function hueOf(text: string): number {
  let hash = 0
  for (let i = 0; i < text.length; i++) hash = (hash * 31 + text.charCodeAt(i)) >>> 0
  return hash % 360
}

function hsl(hue: number, saturation: number, lightness: number): number {
  const a = saturation * Math.min(lightness, 1 - lightness)
  const channel = (n: number) => {
    const k = (n + hue / 30) % 12
    const value = lightness - a * Math.max(-1, Math.min(k - 3, Math.min(9 - k, 1)))
    return Math.round(value * 255)
  }
  return (channel(0) << 16) | (channel(8) << 8) | channel(4)
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
    this.plate.roundRect(0, 0, w, h, 9).fill(hsl(hue, 0.4, 0.20))
    this.plate.roundRect(0, 0, w, 30, 9).fill({ color: hsl(hue, 0.5, 0.32), alpha: 0.7 })
    this.plate.roundRect(1.5, 1.5, w - 3, h - 3, 9).stroke({ color: edge, width: 2.5 })
    this.plate.roundRect(4, 4, w - 8, h - 8, 7)
      .stroke({ color: 0xffffff, width: 1, alpha: 0.12 })

    // 문양 — 식별자에서 만든 도형 셋입니다.
    this.emblem.clear()
    const seed = hueOf(joker.jokerId + 'e')
    for (let i = 0; i < 3; i++) {
      const t = ((seed + i * 97) % 100) / 100
      const cx = w / 2 + (t - 0.5) * (w * 0.5)
      const cy = 56 + ((seed + i * 53) % 26) - 13
      const radius = 7 + ((seed + i * 29) % 13)
      this.emblem.circle(cx, cy, radius)
        .fill({ color: hsl((hue + i * 47) % 360, 0.7, 0.62), alpha: 0.8 })
    }
    this.emblem.circle(w / 2, 56, 26).stroke({ color: 0xffffff, width: 1, alpha: 0.15 })

    this.nameText.text = look.name
    this.nameText.anchor.set(0.5, 0)
    this.nameText.position.set(w / 2, h - 34)

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
