// 조커 한 장의 그림.
//
// **그림 파일이 아직 없습니다.** 150장의 일러스트가 들어오기 전까지는 식별자에서 만든
// 문양으로 그립니다 — 같은 조커는 언제나 같은 모양이고, 희귀도가 테두리 색입니다.
//
// 그림이 들어오면 `Assets.OnMissing` 을 `error` 로 바꾸고 이 문양을 스프라이트로 갈아
// 끼웁니다. `doc/progress.md` 의 남은 것에 있습니다.

import { Container, Graphics, Text } from 'pixi.js'

import { EditionKind } from '../generated/enums/edition-kind'
import type { JokerInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { COLOR, rarityColor, SIZE } from './theme'
import type { EditionLook } from './card-view'

const EDITION_SHADER: Partial<Record<EditionKind, EditionShader>> = {
  [EditionKind.Foil]: 'foil',
  [EditionKind.Holographic]: 'holo',
  [EditionKind.Polychrome]: 'poly',
  [EditionKind.Negative]: 'negative',
}

/** 식별자에서 색 하나. 같은 조커는 언제나 같은 색입니다. */
function hueOf(jokerId: string): number {
  let hash = 0
  for (let i = 0; i < jokerId.length; i++) hash = (hash * 31 + jokerId.charCodeAt(i)) >>> 0
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
  edition?: EditionLook
}

export class JokerView extends Container {
  readonly uid: number

  private readonly plate = new Graphics()
  private readonly emblem = new Graphics()
  private readonly nameText = new Text({
    text: '',
    style: { fontSize: 11, fill: COLOR.ink, align: 'center', wordWrap: true, wordWrapWidth: SIZE.jokerWidth - 10 },
  })
  private readonly counter = new Text({ text: '', style: { fontSize: 12, fill: COLOR.mult, fontWeight: '700' } })
  private edition?: EditionFilter

  tilt = 0

  constructor(joker: JokerInstance, look: JokerLook) {
    super()
    this.uid = joker.uid
    this.addChild(this.plate, this.emblem, this.nameText, this.counter)
    this.pivot.set(SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
    this.set(joker, look)
  }

  set(joker: JokerInstance, look: JokerLook): void {
    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const hue = hueOf(joker.jokerId)

    this.plate.clear()
    this.plate.roundRect(0, 0, w, h, 8).fill(hsl(hue, 0.35, 0.22))
    this.plate.roundRect(1, 1, w - 2, h - 2, 8)
      .stroke({ color: rarityColor(look.rarity), width: 2 })

    // 문양 — 식별자에서 만든 도형 셋입니다. 같은 조커는 언제나 같은 모양입니다.
    this.emblem.clear()
    const seed = hueOf(joker.jokerId + 'e')
    for (let i = 0; i < 3; i++) {
      const t = ((seed + i * 97) % 100) / 100
      const cx = w / 2 + (t - 0.5) * (w * 0.45)
      const cy = 44 + ((seed + i * 53) % 30) - 15
      const radius = 8 + ((seed + i * 29) % 12)
      this.emblem.circle(cx, cy, radius)
        .fill({ color: hsl((hue + i * 40) % 360, 0.6, 0.6), alpha: 0.75 })
    }

    this.nameText.text = look.name
    this.nameText.anchor.set(0.5, 0)
    this.nameText.position.set(w / 2, h - 30)

    // 누적값을 얼굴에 적습니다 — 늘어나는 조커는 그것이 전부이기 때문입니다.
    const { chips, multAdd, multMul } = joker.counters
    const parts: string[] = []
    if (chips !== 0) parts.push(`+${chips}`)
    if (multAdd !== 0) parts.push(`+${(multAdd / 10_000).toFixed(0)}배`)
    if (multMul !== 10_000) parts.push(`×${(multMul / 10_000).toFixed(2)}`)
    this.counter.text = parts.join(' ')
    this.counter.anchor.set(0.5, 0)
    this.counter.position.set(w / 2, 6)

    this.alpha = joker.disabled ? 0.4 : 1

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

  advance(seconds: number): void {
    this.edition?.advance(seconds, this.tilt)
  }
}
