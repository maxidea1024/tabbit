// 카드 한 장의 그림.
//
// **그림 파일이 없습니다.** 카드는 무늬 4종과 랭크 13종의 조합이므로 그리는 편이 맞습니다 —
// 52장을 굽는 대신 `Graphics` 로 그립니다. 강화와 인장은 그 위의 색이고, 에디션은 셰이더
// 입니다.

import { Container, Graphics, Text } from 'pixi.js'

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { CardInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { COLOR, SIZE } from './theme'

const PIP: Record<SuitKind, string> = {
  [SuitKind.Spade]: '♠',
  [SuitKind.Heart]: '♥',
  [SuitKind.Club]: '♣',
  [SuitKind.Diamond]: '♦',
}

const RANK_TEXT: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A',
}

/** 강화가 카드 바탕에 주는 색. 없으면 종이색입니다. */
const ENHANCEMENT_TINT: Partial<Record<EnhancementKind, number>> = {
  [EnhancementKind.Bonus]: 0xd8e6f7,
  [EnhancementKind.Mult]: 0xf7d8dc,
  [EnhancementKind.Wild]: 0xe8dcf7,
  [EnhancementKind.Glass]: 0xdff3f7,
  [EnhancementKind.Steel]: 0xdcdcdc,
  [EnhancementKind.Stone]: 0xb9b3a6,
  [EnhancementKind.Gold]: 0xf5e0a3,
  [EnhancementKind.Lucky]: 0xd9f2cf,
}

const SEAL_COLOR: Partial<Record<SealKind, number>> = {
  [SealKind.Red]: 0xd23b3b,
  [SealKind.Blue]: 0x3b7fd2,
  [SealKind.Gold]: 0xe0b53b,
  [SealKind.Purple]: 0x9a5bd2,
}

const EDITION_SHADER: Partial<Record<EditionKind, EditionShader>> = {
  [EditionKind.Foil]: 'foil',
  [EditionKind.Holographic]: 'holo',
  [EditionKind.Polychrome]: 'poly',
  [EditionKind.Negative]: 'negative',
}

export interface EditionLook {
  shader: EditionShader
  strength: number
  flowSpeed: number
  noise: number
}

/**
 * 화면 위의 카드 하나.
 *
 * **상태를 들고 있지 않습니다** — `set` 이 불릴 때마다 카드가 지금 무엇인지를 다시 그립니다.
 * 연출이 위치와 기울기를 만지고, 규칙은 코어에 있습니다.
 */
export class CardView extends Container {
  readonly uid: number

  private readonly paper = new Graphics()
  private readonly cornerTop = new Text({ text: '', style: { fontSize: 20, fill: COLOR.black, fontWeight: '700' } })
  private readonly cornerBottom = new Text({ text: '', style: { fontSize: 20, fill: COLOR.black, fontWeight: '700' } })
  private readonly pip = new Text({ text: '', style: { fontSize: 46, fill: COLOR.black } })
  private readonly seal = new Graphics()
  private edition?: EditionFilter

  /** 들어 올린 정도. 연출이 만지고 배치가 읽습니다. */
  lift = 0
  tilt = 0
  selected = false

  constructor(card: CardInstance, look?: EditionLook) {
    super()
    this.uid = card.uid

    this.addChild(this.paper, this.pip, this.cornerTop, this.cornerBottom, this.seal)
    this.pivot.set(SIZE.cardWidth / 2, SIZE.cardHeight / 2)
    this.set(card, look)
  }

  /** 카드가 지금 무엇인지를 다시 그립니다. */
  set(card: CardInstance, look?: EditionLook): void {
    const w = SIZE.cardWidth
    const h = SIZE.cardHeight
    const stone = card.enhancement === EnhancementKind.Stone
    const red = card.suit === SuitKind.Heart || card.suit === SuitKind.Diamond

    this.paper.clear()
    if (card.faceDown) {
      this.paper.roundRect(0, 0, w, h, SIZE.cardRadius).fill(COLOR.cardBack)
      this.paper.roundRect(6, 6, w - 12, h - 12, SIZE.cardRadius - 4)
        .stroke({ color: 0x2f7a52, width: 2 })
      this.cornerTop.visible = false
      this.cornerBottom.visible = false
      this.pip.visible = false
      this.seal.clear()
      return
    }

    const paperColor = ENHANCEMENT_TINT[card.enhancement] ?? COLOR.cardFace
    this.paper.roundRect(0, 0, w, h, SIZE.cardRadius).fill(paperColor)
    this.paper.roundRect(0.5, 0.5, w - 1, h - 1, SIZE.cardRadius)
      .stroke({ color: COLOR.cardEdge, width: card.debuffed ? 1 : 2, alpha: card.debuffed ? 0.35 : 1 })

    const ink = card.debuffed ? 0x8a8a8a : red ? COLOR.red : COLOR.black

    this.cornerTop.visible = !stone
    this.cornerBottom.visible = !stone
    this.pip.visible = true

    if (stone) {
      this.pip.text = '●'
      this.pip.style.fill = 0x6f6a60
    } else {
      this.pip.text = PIP[card.suit]
      this.pip.style.fill = ink
      this.cornerTop.text = RANK_TEXT[card.rank] ?? '?'
      this.cornerBottom.text = this.cornerTop.text
      this.cornerTop.style.fill = ink
      this.cornerBottom.style.fill = ink
    }

    this.pip.anchor.set(0.5)
    this.pip.position.set(w / 2, h / 2)
    this.cornerTop.position.set(8, 6)
    this.cornerBottom.anchor.set(1, 1)
    this.cornerBottom.position.set(w - 8, h - 6)

    this.seal.clear()
    const sealColor = SEAL_COLOR[card.seal]
    if (sealColor !== undefined) this.seal.circle(w - 14, 16, 6).fill(sealColor)

    if (card.bonusChips > 0) {
      this.seal.roundRect(8, h - 22, 26, 14, 4).fill({ color: COLOR.chips, alpha: 0.85 })
    }

    this.applyEdition(card.edition, look)
  }

  private applyEdition(edition: EditionKind, look?: EditionLook): void {
    const shader = EDITION_SHADER[edition]
    if (!shader || !look) {
      this.filters = []
      this.edition = undefined
      return
    }

    this.edition = new EditionFilter(shader, {
      strength: look.strength,
      flowSpeed: look.flowSpeed,
      noise: look.noise,
    })
    this.filters = [this.edition]
  }

  /** 매 프레임. 셰이더의 시간과 기울기를 밀어 줍니다. */
  advance(seconds: number): void {
    this.edition?.advance(seconds, this.tilt)
  }
}
