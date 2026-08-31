// 카드 한 장의 그림과 움직임.
//
// **그림 파일이 없습니다.** 카드는 무늬 4종과 랭크 13종의 조합이므로 그리는 편이 맞습니다.
//
// 움직임이 절반입니다 — 카드는 늘 조금씩 흔들리고, 마우스를 따라 기울고, 골라지면
// 튀어오르고, 득점하면 한 번 커집니다. 곧바로 목표 자리로 가는 카드는 죽어 보입니다.

import { Container, Graphics, Rectangle, Sprite, Text } from 'pixi.js'

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { CardInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { roundedMask } from '../shader/mask'
import { PickFilter } from '../shader/pick'
import { artFor } from './art'
import { cardArtId, drawFace, drawSuit } from './pips'
import { Motion, sway } from './motion'
import { clearCardBack, drawCardBack } from './card-back'
import { COLOR, SIZE } from './theme'

/** 족보 도움의 색. **고른 카드의 초록과 달라야 헷갈리지 않습니다.** */
const HINT_COLOR = 0xffc53d

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
  /**
   * 카드의 종이 자체. **에디션 셰이더가 이것에만 걸립니다.**
   *
   * 필터는 그 물체를 감싸는 사각형 위에서 돕니다. 그림자와 도움 외곽선까지 함께 감싸면 그
   * 사각형이 카드보다 커지고, 셰이더에 넘긴 모양 그림이 카드와 어긋납니다 — 그래서 카드
   * 넓이만 담는 통을 하나 두고 그 넓이를 못박습니다.
   */
  private readonly body = new Container()
  private readonly paper = new Graphics()
  private readonly cornerTop = new Text({
    text: '', style: { fontSize: 19, fill: COLOR.black, fontWeight: '800' },
  })
  private readonly cornerBottom = new Text({
    text: '', style: { fontSize: 19, fill: COLOR.black, fontWeight: '800' },
  })
  /**
   * 카드의 얼굴.
   *
   * **글자 하나가 아니라 그린 것입니다.** 5는 무늬가 다섯이어야 5로 읽히고, 킹에는 그림이
   * 있어야 킹으로 읽힙니다 — 큰 무늬 하나로는 랭크가 모서리의 글자에만 남습니다.
   */
  private readonly face = new Graphics()
  /**
   * 트럼프 그림.
   *
   * **있으면 이것이 얼굴 전부입니다.** 트럼프의 얼굴은 몇백 년째 같은 모양이고, 새로
   * 지어낸 얼굴은 그 낱말을 잃습니다 — 그래서 이 한 벌만 공유 재산을 씁니다
   * (`public/art/card/readme.md`). 아직 안 읽혔으면 `face` 가 그려서 채웁니다.
   */
  private readonly picture = new Sprite()
  private readonly mark = new Text({
    text: '', style: { fontSize: 10, fill: 0x3a3226, fontWeight: '700' },
  })
  private readonly seal = new Graphics()
  /**
   * 족보 도움의 외곽선.
   *
   * **필터가 아니라 그린 선입니다.** 필터를 걸면 카드가 매 프레임 그림으로 구워져 글씨가
   * 흐려지고, 알파의 기울기로 만든 테두리는 그림자 색이 바뀐 것으로 보입니다.
   */
  private readonly hintRing = new Graphics()
  private edition?: EditionFilter
  /**
   * 고름 표시.
   *
   * **고른 것을 밝히는 것만으로는 부족합니다** — 고르지 않은 것이 물러나야 몇 장을 골랐는지가
   * 한눈에 읽힙니다. 그 둘을 한 필터가 합니다.
   */
  private readonly pick = new PickFilter()
  /** 1 고름 · -1 고르지 않음 · 0 그대로. */
  private pickMode = 0
  /**
   * 득점의 빛. 1 에서 0 으로 잦아듭니다.
   *
   * **조각이 터지는 대신 빛이 돕니다.** 다섯 장이 차례로 터지면 화면이 시끄러워지고, 정작
   * 카드 위에 뜬 숫자가 그 조각에 묻힙니다.
   */
  private glow = 0
  /** 지금 자리로 달라붙는 중인가. 닿으면 용수철을 원래대로 돌립니다. */
  private slamming = false
  /**
   * 뒷면으로 보이는가.
   *
   * **덱에서 오는 동안은 뒷면입니다.** 앞면인 채로 날아오면 이미 아는 카드가 자리를 옮기는
   * 것이고, 뽑는다는 것은 무엇이 올지 모르는 채로 기다리는 일입니다.
   */
  private showBack = false
  /** 뒤집는 중. 1 에서 0 으로 갑니다. 절반에서 앞뒤가 바뀝니다. */
  private flip = 0
  /**
   * 뒤집기까지 남은 시간. 초입니다.
   *
   * **용수철이 멈추는 것을 기다리지 않습니다.** 멈추는 시각은 카드가 얼마나 멀리서 오는지에
   * 따라 달라지고, 그러면 여덟 장이 거의 같은 순간에 뒤집힙니다 — 한 장씩 뽑은 것이 한
   * 장씩으로 읽히려면 뒤집는 것도 한 장씩이어야 합니다.
   */
  private flipIn = 0
  /** 마지막으로 받은 카드. 뒤집을 때 다시 그립니다. */
  private last?: { card: CardInstance; look?: EditionLook }

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
    this.body.addChild(this.paper, this.picture, this.face, this.cornerTop, this.cornerBottom,
      this.mark, this.seal)
    this.picture.width = SIZE.cardWidth
    this.picture.height = SIZE.cardHeight
    // **넓이를 못박습니다.** 그리는 것에 따라 재면 획이 삐져나온 만큼 사각형이 커지고,
    // 그만큼 모양 그림이 밀립니다.
    this.body.boundsArea = new Rectangle(0, 0, SIZE.cardWidth, SIZE.cardHeight)
    this.addChild(this.shadow, this.body, this.hintRing)
    this.drawHintRing()
    this.pivot.set(SIZE.cardWidth / 2, SIZE.cardHeight / 2)
    this.set(card, look)
  }

  set(card: CardInstance, look?: EditionLook): void {
    this.last = { card, look }
    this.render()
  }

  private render(): void {
    if (!this.last) return
    const { card, look } = this.last
    const w = SIZE.cardWidth
    const h = SIZE.cardHeight
    const stone = card.enhancement === EnhancementKind.Stone
    const red = card.suit === SuitKind.Heart || card.suit === SuitKind.Diamond

    this.shadow.clear()
    this.shadow.roundRect(3, 6, w, h, SIZE.cardRadius).fill({ color: 0x000000, alpha: 0.35 })

    this.paper.clear()
    clearCardBack(this.paper)
    if (card.faceDown || this.showBack) {
      drawCardBack(this.paper, w, h, SIZE.cardRadius,
        { ground: COLOR.cardBack, ink: COLOR.cardBackEdge })
      this.cornerTop.visible = false
      this.cornerBottom.visible = false
      this.face.clear()
      this.picture.visible = false
      this.mark.visible = false
      this.seal.clear()
      this.edition = undefined
      this.restack()
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

    this.face.clear()
    this.picture.visible = false

    if (stone) {
      // 석재는 랭크도 무늬도 없습니다. **돌 하나입니다.**
      this.cornerTop.visible = false
      this.cornerBottom.visible = false
      this.face.circle(w / 2, h / 2, 22).fill(0x6f6a60)
      this.face.circle(w / 2 - 5, h / 2 - 6, 7).fill({ color: 0x8b8578, alpha: 0.6 })
    } else {
      const texture = artFor('card', cardArtId(card.suit, card.rank))
      if (texture) {
        // 그림이 곧 얼굴입니다. 글자도 무늬도 그 안에 있습니다.
        this.picture.texture = texture
        this.picture.width = w
        this.picture.height = h
        this.picture.visible = true
        // 강화는 그림에 색을 입혀 알립니다 — 그림 위에 덧그리면 얼굴이 가려집니다.
        this.picture.tint = card.debuffed ? 0x8d8d8d : paperColor
        this.cornerTop.visible = false
        this.cornerBottom.visible = false
      } else {
        drawFace(this.face, card.suit, card.rank, w, h, ink)
        this.cornerTop.visible = true
        this.cornerBottom.visible = true
        this.cornerTop.text = RANK_TEXT[card.rank] ?? '?'
        this.cornerBottom.text = this.cornerTop.text
        this.cornerTop.style.fill = ink
        this.cornerBottom.style.fill = ink
        // 모서리에는 랭크 아래에 작은 무늬가 붙습니다. **트럼프의 모서리가 그렇습니다** —
        // 손에 부챗살로 쥐었을 때 보이는 것이 그 둘뿐이기 때문입니다.
        drawSuit(this.face, card.suit, 14, 33, 12, ink)
        drawSuit(this.face, card.suit, w - 14, h - 33, 12, ink, true)
      }
    }

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
    this.edition = shader && look
      ? new EditionFilter(shader, {
        strength: look.strength,
        flowSpeed: look.flowSpeed,
        noise: look.noise,
        shape: roundedMask(SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius),
      })
      : undefined
    this.restack()
  }

  /**
   * 지금 걸려 있어야 할 필터.
   *
   * **필요할 때만 겁니다** — 늘 걸어 두면 카드가 매 프레임 그림으로 한 번 구워져 글씨가
   * 뿌옇게 됩니다.
   */
  private restack(): void {
    // 에디션은 종이에만, 고름 표시는 카드 전체에. **고름 표시의 빛은 카드 밖으로 나가야
    // 하고, 에디션의 무늬는 카드 안에 머물러야 합니다.**
    this.body.filters = this.edition ? [this.edition] : []
    const lit = this.pickMode !== 0 || this.glow > 0
    // 득점의 빛이 도는 동안은 그 모드가 앞섭니다 — 득점하는 카드는 물러나 있지 않습니다.
    this.pick.mode = this.glow > 0 ? 2 : this.pickMode
    this.filters = lit ? [this.pick] : []
  }

  /**
   * 득점의 빛.
   *
   * `tint` 는 0..1 의 세 값입니다 — 칩이면 파랑, 배수면 붉은색.
   */
  shine(tint: [number, number, number], strength = 1): void {
    this.glow = Math.max(this.glow, Math.min(1, strength))
    this.pick.setTint(tint[0], tint[1], tint[2])
    this.pick.glow = this.glow
    this.restack()
  }

  /** 족보 도움. 이것도 고르면 더 높은 족보가 되는 카드입니다. */
  set hint(value: boolean) {
    this.hintRing.visible = value
  }

  get hint(): boolean {
    return this.hintRing.visible
  }

  private drawHintRing(): void {
    const w = SIZE.cardWidth
    const h = SIZE.cardHeight
    const g = this.hintRing
    g.clear()
    g.visible = false
    // 두 겹입니다 — 카드에 붙은 선 하나와 그 밖의 옅은 선 하나. 밖의 것이 있어야 선이
    // 종이의 무늬가 아니라 카드를 두른 것으로 읽힙니다.
    g.roundRect(-4.5, -4.5, w + 9, h + 9, SIZE.cardRadius + 4)
      .stroke({ color: HINT_COLOR, width: 2, alpha: 0.4 })
    g.roundRect(-1.5, -1.5, w + 3, h + 3, SIZE.cardRadius + 1)
      .stroke({ color: HINT_COLOR, width: 3 })
  }

  /** 1 고름 · -1 고르지 않음 · 0 그대로. */
  setPick(mode: number, tint: [number, number, number]): void {
    this.pick.setTint(tint[0], tint[1], tint[2])
    if (mode === this.pickMode) return
    this.pickMode = mode
    this.pick.mode = mode
    this.restack()
  }

  /** 이 카드가 지금 있어야 할 자리. 용수철이 따라갑니다. */
  place(x: number, y: number, rotation: number): void {
    this.motion.to(x, y, rotation)
  }

  /**
   * 덱에서 뽑혀 자리로 갑니다.
   *
   * **절도 있게 갑니다.** 손패에 놓일 때의 부드러운 용수철로 오면 카드가 흘러 들어오는
   * 것으로 보이고, 한 장씩 뽑는 것이 한 장씩으로 읽히지 않습니다.
   */
  deal(x: number, y: number, rotation: number): void {
    // 오는 동안은 뒷면입니다. 자리에 닿을 즈음 뒤집습니다.
    this.showBack = true
    this.flip = 0
    this.flipIn = 0.2
    this.render()
    this.motion.hard()
    this.motion.to(x, y, rotation)
    this.motion.scale.snap(0.86)
    this.motion.scale.target = 1
    this.slamming = true
  }

  /**
   * 자리에 「짝」 달라붙습니다.
   *
   * 용수철을 세게 만들어 빠르게 가서 멈추고, 닿으면 원래 강성으로 돌아옵니다. 닿는 순간에
   * 살짝 눌립니다 — 그 한 번의 눌림이 「붙었다」로 읽힙니다.
   */
  slam(x: number, y: number): void {
    this.motion.hard()
    this.motion.to(x, y, 0)
    this.motion.scale.snap(1.16)
    this.motion.scale.target = 1
    this.slamming = true
  }

  /**
   * 그 자리에 곧바로 놓습니다.
   *
   * **그리는 자리도 함께 옮깁니다.** 용수철만 옮기면 다음 프레임이 올 때까지 카드가 원점에
   * 남아 있고, 새로 뽑은 카드가 화면 왼쪽 위에 한 프레임 번쩍입니다.
   */
  placeNow(x: number, y: number): void {
    this.motion.snap(x, y)
    this.position.set(x, y)
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
    if (this.slamming && this.motion.x.settled && this.motion.y.settled) {
      this.slamming = false
      this.motion.soft()
    }

    if (this.flipIn > 0) {
      this.flipIn = Math.max(0, this.flipIn - seconds)
      // 자리에 닿을 즈음입니다. **여기서 뒤집습니다.**
      if (this.flipIn === 0 && this.showBack) this.flip = 1
    }

    if (this.flip > 0) {
      this.flip = Math.max(0, this.flip - seconds * 6)
      // 절반을 지나면 앞면으로 바뀝니다 — 좁아졌다가 벌어지는 그 순간입니다.
      if (this.showBack && this.flip <= 0.5) {
        this.showBack = false
        this.render()
      }
    }
    this.edition?.advance(seconds, this.pointer)
    if (this.pickMode !== 0 || this.glow > 0) this.pick.time = time

    if (this.glow > 0) {
      this.glow = Math.max(0, this.glow - seconds * 1.5)
      this.pick.glow = this.glow
      if (this.glow === 0) this.restack()
    }

    // 숨쉬듯 밝아집니다. **한 번에 다 밝으면 눈이 가지 않고, 깜빡이면 거슬립니다.**
    if (this.hintRing.visible) {
      this.hintRing.alpha = 0.42 + 0.42 * (0.5 + 0.5 * Math.sin(time * 3.4))
    }

    // 도움을 받는 카드는 조금만 들립니다. **고른 카드만큼 들리면 이미 고른 것으로 보입니다.**
    const lift = this.hovered ? 16 : this.selected ? 26 : this.hintRing.visible ? 7 : 0
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
    // 뒤집는 동안 가로만 좁아집니다. **종이 한 장이 돌아가는 모습입니다.**
    const turn = this.flip > 0 ? Math.abs(Math.cos((1 - this.flip) * Math.PI)) : 1
    this.scale.set(this.motion.scale.value * Math.max(0.02, turn), this.motion.scale.value)
    this.zIndex = this.hovered ? 200 : this.selected ? 100 : 0
  }
}
