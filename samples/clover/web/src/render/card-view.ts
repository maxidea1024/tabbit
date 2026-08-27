// 카드 한 장의 그림과 움직임.
//
// **그림 파일이 없습니다.** 카드는 무늬 4종과 랭크 13종의 조합이므로 그리는 편이 맞습니다.
//
// 움직임이 절반입니다 — 카드는 늘 조금씩 흔들리고, 마우스를 따라 기울고, 골라지면
// 튀어오르고, 득점하면 한 번 커집니다. 곧바로 목표 자리로 가는 카드는 죽어 보입니다.

import { Container, Graphics, Text } from 'pixi.js'

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { CardInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { Motion, sway } from './motion'
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

/** 강화가 카드 바탕에 주는 색. */
const ENHANCEMENT_TINT: Partial<Record<EnhancementKind, number>> = {
  [EnhancementKind.Bonus]: 0xcfe0f5,
  [EnhancementKind.Mult]: 0xf5ccd2,
  [EnhancementKind.Wild]: 0xe6d6f5,
  [EnhancementKind.Glass]: 0xd8f0f5,
  [EnhancementKind.Steel]: 0xd6d6d6,
  [EnhancementKind.Stone]: 0xa9a396,
  [EnhancementKind.Gold]: 0xf3dc99,
  [EnhancementKind.Lucky]: 0xd2f0c6,
}

const ENHANCEMENT_MARK: Partial<Record<EnhancementKind, string>> = {
  [EnhancementKind.Bonus]: '+칩',
  [EnhancementKind.Mult]: '+배수',
  [EnhancementKind.Wild]: '와일드',
  [EnhancementKind.Glass]: '유리',
  [EnhancementKind.Steel]: '강철',
  [EnhancementKind.Stone]: '석재',
  [EnhancementKind.Gold]: '황금',
  [EnhancementKind.Lucky]: '행운',
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

export class CardView extends Container {
  readonly uid: number
  readonly motion = new Motion()

  private readonly shadow = new Graphics()
  private readonly paper = new Graphics()
  private readonly cornerTop = new Text({
    text: '', style: { fontSize: 19, fill: COLOR.black, fontWeight: '800' },
  })
  private readonly cornerBottom = new Text({
    text: '', style: { fontSize: 19, fill: COLOR.black, fontWeight: '800' },
  })
  private readonly pip = new Text({ text: '', style: { fontSize: 44, fill: COLOR.black } })
  private readonly mark = new Text({
    text: '', style: { fontSize: 10, fill: 0x3a3226, fontWeight: '700' },
  })
  private readonly seal = new Graphics()
  private edition?: EditionFilter

  /** 마우스가 올라와 있는가. 기울기와 크기가 이것을 봅니다. */
  hovered = false
  selected = false
  /** 마우스가 카드 안 어디에 있는가. -1 에서 1 입니다. */
  pointer = 0
  /** 늘 흔들리는 정도. 낸 카드는 얌전합니다. */
  idle = 1

  constructor(card: CardInstance, look?: EditionLook) {
    super()
    this.uid = card.uid
    this.addChild(this.shadow, this.paper, this.pip, this.cornerTop, this.cornerBottom,
      this.mark, this.seal)
    this.pivot.set(SIZE.cardWidth / 2, SIZE.cardHeight / 2)
    this.set(card, look)
  }

  set(card: CardInstance, look?: EditionLook): void {
    const w = SIZE.cardWidth
    const h = SIZE.cardHeight
    const stone = card.enhancement === EnhancementKind.Stone
    const red = card.suit === SuitKind.Heart || card.suit === SuitKind.Diamond

    this.shadow.clear()
    this.shadow.roundRect(3, 6, w, h, SIZE.cardRadius).fill({ color: 0x000000, alpha: 0.35 })

    this.paper.clear()
    if (card.faceDown) {
      this.paper.roundRect(0, 0, w, h, SIZE.cardRadius).fill(COLOR.cardBack)
      this.paper.roundRect(7, 7, w - 14, h - 14, SIZE.cardRadius - 4)
        .stroke({ color: 0x3d9166, width: 2 })
      for (let i = 0; i < 4; i++) {
        this.paper.circle(w / 2, 22 + i * 30, 6).stroke({ color: 0x3d9166, width: 1.5 })
      }
      this.cornerTop.visible = false
      this.cornerBottom.visible = false
      this.pip.visible = false
      this.mark.visible = false
      this.seal.clear()
      this.filters = []
      return
    }

    const paperColor = ENHANCEMENT_TINT[card.enhancement] ?? COLOR.cardFace
    this.paper.roundRect(0, 0, w, h, SIZE.cardRadius).fill(paperColor)
    this.paper.roundRect(3, 3, w - 6, h - 6, SIZE.cardRadius - 3)
      .stroke({ color: 0xffffff, width: 1, alpha: 0.5 })
    this.paper.roundRect(0.5, 0.5, w - 1, h - 1, SIZE.cardRadius)
      .stroke({ color: card.debuffed ? 0x6b6b6b : COLOR.cardEdge, width: 2 })

    if (card.debuffed) {
      this.paper.roundRect(0, 0, w, h, SIZE.cardRadius).fill({ color: 0x2a2a2a, alpha: 0.55 })
    }

    const ink = card.debuffed ? 0x9a9a9a : red ? COLOR.red : COLOR.black

    this.cornerTop.visible = !stone
    this.cornerBottom.visible = !stone
    this.pip.visible = true

    if (stone) {
      this.pip.text = '⬤'
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
    this.cornerTop.position.set(8, 5)
    this.cornerBottom.anchor.set(1, 1)
    this.cornerBottom.position.set(w - 8, h - 5)

    const markText = ENHANCEMENT_MARK[card.enhancement]
    this.mark.visible = markText !== undefined
    if (markText !== undefined) {
      this.mark.text = markText
      this.mark.anchor.set(0.5, 0)
      this.mark.position.set(w / 2, h - 22)
    }

    this.seal.clear()
    const sealColor = SEAL_COLOR[card.seal]
    if (sealColor !== undefined) {
      this.seal.circle(w - 15, 16, 7).fill(sealColor)
      this.seal.circle(w - 15, 16, 7).stroke({ color: 0xffffff, width: 1, alpha: 0.6 })
    }

    if (card.bonusChips > 0) {
      this.seal.roundRect(7, h - 20, 30, 13, 4).fill({ color: COLOR.chips, alpha: 0.9 })
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

  /** 이 카드가 지금 있어야 할 자리. 용수철이 따라갑니다. */
  place(x: number, y: number, rotation: number): void {
    this.motion.to(x, y, rotation)
  }

  placeNow(x: number, y: number): void {
    this.motion.snap(x, y)
  }

  /**
   * 물러납니다.
   *
   * **곧바로 지우지 않습니다** — 카드가 사라지는 것이 보여야 「이 판이 끝났다」가 읽힙니다.
   */
  retire(): void {
    this.retiring = true
    this.eventMode = 'none'
    this.motion.to(SIZE.width + 120, this.motion.y.target - 40, 26)
    this.motion.scale.target = 0.82
  }

  retiring = false

  /** 물러나기가 끝났는가. 화면 밖으로 나가면 지웁니다. */
  get gone(): boolean {
    return this.retiring && this.motion.x.value > SIZE.width + 40
  }

  /** 득점할 때 한 번 튀어오릅니다. */
  pop(strength = 1): void {
    this.motion.y.kick(-260 * strength)
    this.motion.scale.target = 1 + 0.12 * strength
    this.motion.rotation.kick((Math.random() - 0.5) * 6)
  }

  advance(seconds: number, time: number): void {
    this.motion.advance(seconds)
    this.edition?.advance(seconds, this.pointer)

    const lift = this.hovered ? 16 : this.selected ? 26 : 0
    const wobble = sway(time, this.motion.phase, 1.6 * this.idle, 1.4)
    const bob = sway(time, this.motion.phase * 1.7, 2.2 * this.idle, 0.9)

    this.x = this.motion.x.value
    this.y = this.motion.y.value - lift + bob
    this.rotation = (this.motion.rotation.value + wobble) * (Math.PI / 180)

    // 마우스가 올라오면 그쪽으로 기웁니다. **카드가 손에 잡힌 것처럼 보이는 자리입니다.**
    if (this.hovered) this.rotation += this.pointer * 0.12

    if (this.retiring) this.alpha = Math.max(0, 1 - (this.motion.x.value - 900) / 380)

    const want = this.retiring ? 0.82 : this.hovered ? 1.08 : this.selected ? 1.04 : 1
    if (!this.motion.scale.settled || Math.abs(this.motion.scale.target - want) > 0.001) {
      this.motion.scale.target = want
    }
    this.scale.set(this.motion.scale.value)
    this.zIndex = this.hovered ? 200 : this.selected ? 100 : 0
  }
}
