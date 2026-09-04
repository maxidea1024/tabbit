// 카드 한 장의 그림과 움직임.
//
// **그림 파일이 없습니다.** 카드는 무늬 4종과 랭크 13종의 조합이므로 그리는 편이 맞습니다.
//
// 움직임이 절반입니다 — 카드는 늘 조금씩 흔들리고, 마우스를 따라 기울고, 골라지면
// 튀어오르고, 득점하면 한 번 커집니다. 곧바로 목표 자리로 가는 카드는 죽어 보입니다.

import { Container, Graphics, Rectangle, Sprite, Text, type Filter } from 'pixi.js'
import { t } from '../core/strings'

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import type { CardInstance } from '../core/state'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { roundedMask } from '../shader/mask'
import { PickFilter } from '../shader/pick'
import {
  cardFaceTexture, clearCardFace, drawCardFaceVector, faceInk,
} from './card-face'
import { cardPaper, suitInk } from './card-set'
import { Motion, sway, Spring } from './motion'
import { cardBack, clearCardBack, drawCardBack } from './card-back'
import { COLOR, SIZE } from './theme'

/** 족보 도움의 색. **고른 카드의 초록과 달라야 헷갈리지 않습니다.** */
const HINT_COLOR = 0xffc53d

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

/**
 * 강화가 카드에 다는 글의 열쇠.
 *
 * **글이 아니라 열쇠를 둡니다.** 모듈의 상수는 임포트할 때 만들어지고 그것은 `useStrings`
 * 로 글 표를 넘기기 전입니다 — 여기서 `t()` 를 부르면 8개가 열쇠 그대로 고정되고, 나중에
 * 말을 바꾸어도 그대로 남습니다. 찾는 것은 그리는 자리에서 합니다.
 */
const ENHANCEMENT_MARK_KEY: Partial<Record<EnhancementKind, string>> = {
  [EnhancementKind.Bonus]: 'ui.label.plus_chips',
  [EnhancementKind.Mult]: 'ui.label.plus_mult',
  [EnhancementKind.Wild]: 'ui.enhancement.wild',
  [EnhancementKind.Glass]: 'ui.enhancement.glass',
  [EnhancementKind.Steel]: 'ui.enhancement.steel',
  [EnhancementKind.Stone]: 'ui.enhancement.stone',
  [EnhancementKind.Gold]: 'ui.enhancement.gold',
  [EnhancementKind.Lucky]: 'ui.enhancement.lucky',
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
 * 득점한 카드가 내려올 때의 용수철.
 *
 * 올라갈 때는 380·39 입니다. **네 배 강성이 두 배 빠르기**이고, 감쇠는 그 비율을 지켜야
 * 튀지 않고 자리에 섭니다.
 */
const LIFT_DOWN_K = 1_520
const LIFT_DOWN_D = 78

export class CardView extends Container {
  readonly uid: number
  readonly motion = new Motion()

  private readonly shadow = new Graphics()
  /**
   * 득점하는 카드가 들린 정도.
   *
   * **그림자는 그대로 두고 종이만 올립니다.** 카드 전체를 올리면 자리를 옮긴 것으로
   * 보이지만, 그림자가 남아 있으면 그 자리에서 손으로 밀어 올린 것으로 보입니다.
   */
  private readonly lift = new Spring(0, 380, 39)
  /**
   * 카드의 종이 자체. **에디션 셰이더가 이것에만 걸립니다.**
   *
   * 필터는 그 물체를 감싸는 사각형 위에서 돕니다. 그림자와 도움 외곽선까지 함께 감싸면 그
   * 사각형이 카드보다 커지고, 셰이더에 넘긴 모양 그림이 카드와 어긋납니다 — 그래서 카드
   * 넓이만 담는 통을 하나 두고 그 넓이를 못박습니다.
   */
  private readonly body = new Container()
  /**
   * 뒷면이 담기는 통.
   *
   * **앞면과 따로입니다.** 뒷면은 그린 선 하나가 아니라 통 하나입니다 — 무늬가 판 밖으로
   * 나가지 않게 자르는 것이 마스크이고, 마스크는 자식으로 붙습니다.
   */
  private readonly backNode = new Container()
  /**
   * 구워 둔 앞면.
   *
   * **종이 · 얼굴 · 테두리 · 모서리가 한 장에 있습니다.** 그 넷을 정하는 것은 「무늬 ·
   * 랭크 · 종이색 · 디버프」뿐이고, `card-face.ts` 가 그 열쇠로 한 번 굽습니다 — 그러니
   * 여기서 하는 일은 그림 하나를 걸어 주는 것입니다. 벡터로 그리던 때는 한 장이 채우기
   * 명령 약 48개에 글 둘이었고, 그것이 `refresh` 마다 손패 전부에 다시 일어났습니다.
   */
  private readonly faceSprite = new Sprite()
  /**
   * 굽지 못할 때의 앞면.
   *
   * **렌더러를 받기 전에도 카드가 그려집니다** — 타이틀의 카드와 미리보기 도구가 그렇습니다.
   * 그때는 선으로 그리고, 이 통이 그것을 담습니다.
   */
  private readonly faceNode = new Container()
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
  /** 지금 걸린 에디션 필터가 어느 에디션의 것인가. 같으면 다시 만들지 않습니다. */
  private editionKind?: EditionKind
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
  /** 앞면으로 뒤집힌 순간. 소리를 내는 쪽이 겁니다. */
  onFlipped?: () => void
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
   * 뒤집는 시각. 화면의 시계입니다.
   *
   * **부르는 쪽이 정합니다.** 카드마다 자기 시계로 세면 여덟 장이 저마다 도착한 뒤 따로
   * 뒤집히고, 그것은 한 장씩 뽑아 한 장씩 까는 것입니다 — 뽑기는 뒷면으로 우르르 붙고,
   * 다 붙은 뒤에 왼쪽부터 파도로 뒤집히는 두 단계이므로, 뒤집는 시각은 그 패 전체를 아는
   * 쪽이 정해 넘겨 줍니다.
   */
  private flipAt?: number
  /** 마지막으로 받은 카드. 뒤집을 때 다시 그립니다. */
  private last?: { card: CardInstance; look?: EditionLook }

  /** 마우스가 올라와 있는가. 기울기와 크기가 이것을 봅니다. */
  hovered = false
  selected = false
  /** 마우스가 카드 안 어디에 있는가. -1 에서 1 입니다. */
  pointer = 0
  /**
   * 늘 흔들리는 정도.
   *
   * **손패는 0 입니다.** 여덟 장이 저마다의 박자로 흔들리면 그 위에서 카드 하나를 고르는
   * 일이 흔들리는 것을 맞히는 일이 됩니다 — 손패에서 눈에 보여야 하는 움직임은 가리킨
   * 것이 들리는 것과 고른 것이 올라가는 것뿐입니다. 판으로 나간 카드는 값을 내는 동안
   * 흔들립니다(`0.4` · `0.15`).
   */
  idle = 0

  constructor(card: CardInstance, look?: EditionLook) {
    super()
    this.uid = card.uid
    this.body.addChild(this.faceSprite, this.faceNode, this.backNode, this.mark, this.seal)
    this.faceSprite.setSize(SIZE.cardWidth, SIZE.cardHeight)
    // **그림자는 한 번만 그립니다.** 카드가 무엇이든 같은 사각형이고, 바뀌는 것은 이 통의
    // 자리와 알파뿐입니다.
    this.shadow.roundRect(3, 6, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius)
      .fill({ color: 0x000000, alpha: 0.35 })
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

    clearCardBack(this.backNode)
    if (card.faceDown || this.showBack) {
      drawCardBack(this.backNode, w, h, SIZE.cardRadius, cardBack())
      this.faceSprite.visible = false
      clearCardFace(this.faceNode)
      this.mark.visible = false
      this.seal.clear()
      this.edition = undefined
      this.editionKind = undefined
      this.restack()
      return
    }

    // **앞면은 그림 하나입니다.** 종이 · 얼굴 · 테두리 · 모서리가 그 안에 함께 구워져
    // 있고, 그것을 정하는 것이 이 다섯입니다.
    const face = {
      suit: card.suit,
      rank: card.rank,
      paper: ENHANCEMENT_TINT[card.enhancement] ?? cardPaper(),
      debuffed: card.debuffed,
      stone,
    }
    const ink = faceInk(card.debuffed, suitInk(card.suit))
    const baked = cardFaceTexture(w, h, SIZE.cardRadius, face, ink)
    if (baked) {
      // **같은 스프라이트를 계속 씁니다.** 다시 만들면 `refresh` 마다 손패만큼의 스프라이트가
      // 버려지고, 그 값은 벡터를 다시 그리는 것보다 작을 뿐 0이 아닙니다.
      clearCardFace(this.faceNode)
      this.faceSprite.texture = baked
      this.faceSprite.setSize(w, h)
      this.faceSprite.visible = true
    } else {
      this.faceSprite.visible = false
      clearCardFace(this.faceNode)
      drawCardFaceVector(this.faceNode, w, h, SIZE.cardRadius, face, ink)
    }

    const markKey = ENHANCEMENT_MARK_KEY[card.enhancement]
    this.mark.visible = markKey !== undefined
    if (markKey !== undefined) {
      this.mark.text = t(markKey)
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
      this.edition = undefined
      this.editionKind = undefined
    } else if (this.editionKind !== edition || !this.edition) {
      // **같은 에디션이면 필터를 그대로 둡니다.** 다시 그릴 때마다 새로 만들면 유니폼
      // 묶음이 그만큼 생기고, 뽑히는 카드는 뒤집히면서 두 번 그려집니다.
      this.editionKind = edition
      this.edition = new EditionFilter(shader, {
        strength: look.strength,
        flowSpeed: look.flowSpeed,
        noise: look.noise,
        shape: roundedMask(SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius),
      })
    }
    this.restack()
  }

  /**
   * 지금 걸려 있어야 할 필터.
   *
   * **필요할 때만 겁니다** — 늘 걸어 두면 카드가 매 프레임 그림으로 한 번 구워져 글씨가
   * 뿌옇게 됩니다.
   */
  private restack(): void {
    const lit = this.pickMode !== 0 || this.glow > 0
    // 득점의 빛이 도는 동안은 그 모드가 앞섭니다 — 득점하는 카드는 물러나 있지 않습니다.
    this.pick.mode = this.glow > 0 ? 2 : this.pickMode

    // **둘 다 종이에만 겁니다.** 카드 전체에 걸면 그림자까지 함께 빛나고, 득점하는 카드가
    // 들려 있는 동안에는 그 그림자가 카드에서 떨어져 있어 빛나는 얼룩 하나가 따로 남습니다.
    //
    // 차례가 있습니다 — 무늬를 먼저 얹고 그 결과의 둘레에 빛을 두릅니다.
    const stack: Filter[] = []
    if (this.edition) stack.push(this.edition)
    if (lit) stack.push(this.pick)
    this.body.filters = stack
    this.filters = []
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
    // **카드 아래의 동그라미 하나입니다.** 카드를 두르고 들어 올리던 것을 걷었습니다 —
    // 그러면 도움을 받는 카드가 이미 고른 카드처럼 보여서, 무엇을 고른 것인지가 갈리지
    // 않았습니다. 표시는 카드 밖에 있고 카드는 가만히 있습니다.
    g.circle(w / 2, h + 11, 4.5).fill(HINT_COLOR)
    g.circle(w / 2, h + 11, 4.5).stroke({ color: 0x0a0f18, width: 1.5 })
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
   * 것으로 보이고, 뽑은 것이 뽑은 것으로 읽히지 않습니다.
   *
   * `flipAt` 은 뒤집는 시각입니다. 오는 동안은 뒷면이고 그 시각에 뒤집힙니다.
   */
  deal(x: number, y: number, rotation: number, flipAt: number): void {
    this.showBack = true
    this.flip = 0
    this.flipAt = flipAt
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
   * 득점하는 카드인가. 그렇다면 그 자리에서 살며시 올라갑니다.
   *
   * **내려오는 것은 올라가는 것보다 두 배 빠릅니다.** 올라가는 것은 「이 카드가 센다」 를
   * 알리는 것이라 눈이 따라갈 만큼 느려야 하고, 내려오는 것은 그 일이 끝났다는 것이라
   * 밍기적거릴 이유가 없습니다.
   *
   * 빠르기는 강성이 정하고, 강성은 제곱근으로 빠르기가 됩니다 — 두 배 빠르려면 네 배입니다.
   */
  set scoring(value: boolean) {
    if (value) this.lift.soft()
    else this.lift.hard(LIFT_DOWN_K, LIFT_DOWN_D)
    this.lift.target = value ? 8 : 0
  }

  /**
   * 물러납니다.
   *
   * **딜러에게 갑니다.** 나가는 자리는 부르는 쪽이 정하고, 그 자리는 화면 오른쪽 위 밖의
   * 한 점입니다 — 보이지는 않지만 카드를 거두는 사람이 있는 자리입니다. 같은 높이로 곧게
   * 빠지면 카드가 옆으로 치워지는 것이고, 그 높이 그 자리에는 덱이 있어 버린 카드가 덱으로
   * 들어가는 것으로 보였습니다.
   *
   * **직선입니다.** 위로 띄우고 26도 기울여 보낸 적이 있는데, 그러면 카드가 휘어 올라가며
   * 사라집니다 — 목표점 자체가 위에 있으면 비스듬한 직선이고 휘지 않습니다. 기울기는 나가는
   * 방향으로 조금만 줍니다.
   */
  retire(x: number, y: number): void {
    this.retiring = true
    this.eventMode = 'none'
    this.motion.to(x, y, -9)
    this.motion.scale.target = 0.84
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

    if (this.flipAt !== undefined && time >= this.flipAt) {
      this.flipAt = undefined
      if (this.showBack) this.flip = 1
    }

    // 8분의 1초입니다. 파도로 뒤집히므로 한 장이 길면 앞 장과 겹쳐 한 덩어리로 보입니다.
    if (this.flip > 0) {
      this.flip = Math.max(0, this.flip - seconds * 8)
      // 절반을 지나면 앞면으로 바뀝니다 — 좁아졌다가 벌어지는 그 순간입니다.
      if (this.showBack && this.flip <= 0.5) {
        this.showBack = false
        this.render()
        // **뒤집히는 그 순간에 소리가 나야 합니다.** 뽑는 것은 무엇이 올지 모르는 채로
        // 기다리는 일이고, 그 기다림이 끝나는 자리가 여기입니다.
        this.onFlipped?.()
      }
    }
    // 들린 만큼 종이만 올라갑니다. 그림자는 자리에 남습니다.
    this.lift.advance(seconds)
    this.body.y = -this.lift.value

    this.edition?.at(time, this.pointer)
    if (this.pickMode !== 0 || this.glow > 0) this.pick.time = time

    if (this.glow > 0) {
      this.glow = Math.max(0, this.glow - seconds * 1.5)
      this.pick.glow = this.glow
      if (this.glow === 0) this.restack()
    }

    // 숨쉬듯 밝아집니다. **한 번에 다 밝으면 눈이 가지 않고, 깜빡이면 거슬립니다.**
    if (this.hintRing.visible) {
      this.hintRing.alpha = 0.6 + 0.4 * (0.5 + 0.5 * Math.sin(time * 3.4))
    }

    // **도움을 받는 카드는 들리지 않습니다.** 표시는 카드 아래의 동그라미이고, 들리는 것은
    // 가리킨 것과 고른 것뿐입니다.
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
    // 뒤집는 동안 가로만 좁아집니다. **종이 한 장이 돌아가는 모습입니다.**
    const turn = this.flip > 0 ? Math.abs(Math.cos((1 - this.flip) * Math.PI)) : 1
    this.scale.set(this.motion.scale.value * Math.max(0.02, turn), this.motion.scale.value)
    this.zIndex = this.hovered ? 200 : this.selected ? 100 : 0
  }
}
