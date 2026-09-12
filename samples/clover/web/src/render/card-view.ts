// 카드 한 장의 그림과 움직임.
//
// **그림 파일이 없습니다.** 카드는 무늬 4종과 랭크 13종의 조합이므로 그리는 편이 맞습니다.
//
// 움직임이 절반입니다 — 카드는 늘 조금씩 흔들리고, 마우스를 따라 기울고, 골라지면
// 튀어오르고, 득점하면 한 번 커집니다. 곧바로 목표 자리로 가는 카드는 죽어 보입니다.

import { COLOR, DEAD, ENHANCEMENT_INK, ENHANCEMENT_PAPER, ENHANCEMENT_PLAIN, HINT_INK, PAINT, SEAL_INK } from './ink'
import { Container, Graphics, Sprite, Text, type Filter } from 'pixi.js'
import { t } from '../core/strings'

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import type { CardInstance } from '../core/state'
import { EDITION_SHADER, EditionFilter, type EditionLook } from '../shader/editions'
import { roundedMask } from '../shader/mask'
import { tornSprite, tornTexture } from '../ui/chrome'
import { PickFilter } from '../shader/pick'
import { ERODE_SWEEP, ErodeFilter } from '../shader/erode'
import { BlightFilter } from '../shader/blight'
import {
  cardFaceTexture, clearCardFace, drawCardFaceVector, faceInk,
} from './card-face'
import { cardPaper, suitInk } from './card-set'
import { fraction, Motion, sway, Spring } from './motion'
import { pinBox } from './pin'
import { cardBack, clearCardBack, drawCardBack } from './card-back'
import { UI, SIZE } from './theme'
import { type MotesHandle, startMotes } from './motes-layer'

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


export type { EditionLook }

/**
 * 득점한 카드가 내려올 때의 용수철.
 *
 * 올라갈 때는 380·39 입니다. **네 배 강성이 두 배 빠르기**이고, 감쇠는 그 비율을 지켜야
 * 튀지 않고 자리에 멎습니다.
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
  /**
   * 강화를 적는 칩.
   *
   * **글만 얹어 두면 읽히지 않습니다.** 종이색이 강화마다 다르고 그 위에 얼굴의 획까지
   * 지나가므로, 같은 어두운 글씨가 어떤 종이 위에서는 배경으로 묻힙니다 — 어두운 판을
   * 깔고 그 위에 밝은 글씨를 얹으면 종이가 무엇이든 같은 정도로 읽힙니다.
   */
  private readonly markPlate = new Graphics()
  private readonly mark = new Text({
    text: '', style: { fontSize: 10, fill: ENHANCEMENT_PLAIN, fontWeight: '800' },
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
  private pick?: PickFilter
  /**
   * 고름 표시가 필요해진 자리. **없으면 만듭니다.**
   *
   * 카드 하나가 살아 있는 동안 내내 들고 있을 것이 아닙니다 — 덱 보기가 52장이고 도감의
   * 카드 탭도 카드로 그리는데, 그 가운데 고르거나 득점하는 것은 없습니다. `edition` 과
   * 같은 규약입니다.
   */
  private picker(): PickFilter {
    this.pick ??= new PickFilter()
    return this.pick
  }
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
  /** 뒤집는 중. 1 에서 0 으로 갑니다. 절반에서 보이는 면이 갈립니다. */
  private flip = 0
  /**
   * 이번 반 바퀴에서 이미 갈아 끼웠는가.
   *
   * **반 바퀴에 한 번입니다.** 절반을 지났는지로만 보면 그 뒤의 프레임마다 다시 갈리고,
   * 뒷면으로 간 그 다음 프레임에 곧바로 앞면으로 돌아옵니다 — 뒷면이 한 프레임도 보이지
   * 않습니다.
   */
  private swapped = true
  /**
   * 뒷면을 거쳐 갈아 끼울 카드.
   *
   * **얼굴은 뒷면 뒤에서 갈립니다.** 앞면인 채로 반 바퀴만 돌면 좁아졌다 벌어지는 그 한
   * 순간에 갈리는데, 그것은 눈이 「뒤집혔다」로 읽기 전입니다.
   */
  private turnBack?: { card: CardInstance; look?: EditionLook; hold: number }
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

  /** 마우스가 올라와 있는가. 기울기와 크기가 이것을 확인합니다. */
  hovered = false
  selected = false
  /**
   * 커서가 어느 쪽에 있는가. −1 에서 1 입니다.
   *
   * **넘겨받는 것은 목표이고, 실제로 쓰는 것은 그쪽으로 따라가는 값입니다.** 커서는
   * 프레임마다 껑충 뛰므로 받은 값을 그대로 무늬의 위상에 더하면 무늬가 그만큼 순간
   * 이동합니다 — 무늬가 밀리는 것으로 보이는 것이 그것입니다. 기울어지는 데 시간이
   * 걸려야 「기울었다」로 읽힙니다.
   */
  set pointer(value: number) {
    this.pointerAim = value
  }

  get pointer(): number {
    return this.pointerAt
  }

  private pointerAim = 0
  private pointerAt = 0

  /** 목표 쪽으로 한 단계 따라갑니다. */
  private easePointer(seconds: number): void {
    this.pointerAt += (this.pointerAim - this.pointerAt) * fraction(seconds, 9)
  }
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
    this.body.addChild(this.faceSprite, this.faceNode, this.backNode,
                       this.markPlate, this.mark, this.seal)
    this.faceSprite.setSize(SIZE.cardWidth, SIZE.cardHeight)
    // **그림자는 한 번만 그립니다.** 카드가 무엇이든 같은 꼴이고, 바뀌는 것은 이 통의
    // 자리와 알파뿐입니다. **뜯긴 변을 따라갑니다** — 마스크 그림을 어둡게 물들인 것입니다.
    const shade = tornSprite(card.uid, SIZE.cardWidth, SIZE.cardHeight)
    if (shade !== undefined) {
      shade.tint = PAINT.veil
      shade.alpha = 0.35
      shade.position.set(3, 6)
      this.shadow.addChild(shade)
    } else {
      this.shadow.rect(3, 6, SIZE.cardWidth, SIZE.cardHeight)
        .fill({ color: PAINT.veil, alpha: 0.35 })
    }
    // **얼굴은 뜯긴 가장자리로 오려 냅니다.** 가위로 자른 네모는 멋이 없고 둥근 모서리는
    // 웹의 문법입니다.
    const torn = tornSprite(card.uid, SIZE.cardWidth, SIZE.cardHeight)
    if (torn !== undefined) {
      this.body.addChild(torn)
      this.body.mask = torn
    }
    // **넓이를 고정합니다.** 그리는 것에 따라 재면 획이 삐져나온 만큼 사각형이 커지고,
    // 그만큼 모양 그림이 밀립니다. **필터 사각형도 함께 고정합니다** — 이 통에 에디션과
    // 득점의 빛이 걸리고, 경계만 고정하면 구운 사진에서 이 통이 빠집니다(`pin.ts`).
    pinBox(this.body, SIZE.cardWidth, SIZE.cardHeight)
    this.addChild(this.shadow, this.body, this.hintRing)
    this.drawHintRing()
    this.pivot.set(SIZE.cardWidth / 2, SIZE.cardHeight / 2)
    this.set(card, look)
  }

  set(card: CardInstance, look?: EditionLook): void {
    this.last = { card, look }
    this.render()
  }

  /**
   * 그 자리에서 한 번 뒤집혀 **다른 카드가 되어 돌아옵니다.**
   *
   * 얼굴을 그 프레임에 갈아 끼우면 카드가 이미 바뀐 채로 그려지고, 무엇이 무엇으로 바뀐
   * 것인지가 화면에 남지 않습니다 — 타로를 써도 인장을 붙여도 화면에서 일어나는 것이
   * 같았던 까닭이 그것입니다.
   *
   * **뽑을 때의 뒤집기와 같은 몸짓입니다.** 좁아졌다가 벌어지는 그 절반에서 얼굴이
   * 갈립니다 — 다른 것은 시작이 뒷면이 아니라 앞면이라는 것뿐입니다.
   */
  turnInto(card: CardInstance, look?: EditionLook): void {
    this.flip = 1
    this.swapped = false
    this.turning = { card, look }
  }

  /**
   * 뒷면을 거쳐 다른 카드가 되어 돌아옵니다.
   *
   * **앞면 → 뒷면 → 앞면입니다.** 반 바퀴 하나로 앞면에서 앞면으로 갈던 동안은 좁아졌다
   * 벌어지는 62밀리초 안에 얼굴이 갈렸고, 눈이 그것을 「뒤집혔다」로 읽기 전에 이미 새
   * 얼굴이었습니다 — 갈린 것이 아니라 잠깐 찌그러진 것으로 보였습니다.
   *
   * 뒷면으로 `hold` 만큼 멈춰 있는 동안 얼굴을 갈아 끼우고, 앞면으로 돌아올 때
   * `onFlipped` 이 불립니다 — 새 얼굴이 보이는 그 순간이고, 글과 소리가 거기서 납니다.
   */
  turnOver(card: CardInstance, look: EditionLook | undefined, hold: number): void {
    this.flip = 1
    this.swapped = false
    this.turnBack = { card, look, hold }
  }

  /** 뒷면으로 세웁니다. 뒤집어 앞면으로 오는 것은 `turnUp` 입니다. */
  faceBack(): void {
    this.showBack = true
    this.render()
  }

  /** 뒷면에서 앞면으로 뒤집힙니다. 벌어지는 자리에서 `onFlipped` 이 불립니다. */
  turnUp(): void {
    if (!this.showBack) return
    this.flip = 1
    this.swapped = false
  }

  /**
   * 이 카드가 줄에서 갖는 그리기 차례.
   *
   * **올렸다가 되돌릴 자리가 있어야 합니다.** 가리키거나 고른 카드는 위로 올라와야 하고,
   * 떼면 줄의 차례로 돌아와야 합니다 — 돌아갈 값을 어디에도 두지 않으면 되돌릴 수
   * 없습니다.
   */
  rowZ = 0

  /** 뒤집는 절반에서 갈아 끼울 카드. 앞면에서 앞면입니다. */
  private turning?: { card: CardInstance; look?: EditionLook }

  /** 모래로 삭아 사라지는 중인가. */
  private erode?: ErodeFilter
  /** 삭기 시작한 뒤 지난 시간. 초입니다. */
  private age = 0
  private eroding = false
  /** 풀려 나간 알갱이. **그릴 수 없는 기계에서는 없습니다.** */
  private motes?: MotesHandle

  /** 시드는 금. 번지는 동안만 걸립니다. */
  private blight?: BlightFilter
  /** 금이 어디까지 번졌는가. */
  private blighting?: number
  /** 다 번진 자리에서 갈아 끼울 카드. 죽은 얼굴입니다. */
  private withering?: { card: CardInstance; look?: EditionLook }

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
      this.markPlate.clear()
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
      paper: ENHANCEMENT_PAPER[card.enhancement] ?? cardPaper(),
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
    this.markPlate.clear()
    if (markKey !== undefined) {
      const ink = ENHANCEMENT_INK[card.enhancement] ?? ENHANCEMENT_PLAIN
      this.mark.style.fill = card.debuffed ? DEAD.mark : ink
      this.mark.text = t(markKey)
      this.mark.anchor.set(0.5, 0.5)
      this.mark.scale.set(1)

      // **글의 넓이가 칩의 넓이입니다.** 말에 따라 「+칩」이 `+Chips` 가 되고 독일어는 더
      // 깁니다 — 못박아 두면 그 말에서 글자가 칩 밖으로 나갑니다.
      //
      // **모서리의 인덱스는 남깁니다.** 칩이 카드의 폭을 다 쓰면 아래 오른쪽의 랭크와
      // 무늬를 덮고, 그러면 겹친 손패에서 무슨 카드인지가 사라집니다 — 그 안에 들어가지
      // 않는 말은 글씨를 줄입니다.
      const chipH = 15
      const room = w - 30
      if (this.mark.width > room) this.mark.scale.set(room / this.mark.width)
      const chipW = Math.round(this.mark.width * this.mark.scale.x) + 12
      const cy = h - 21
      this.mark.position.set(w / 2, cy)
      this.markPlate
        .rect(Math.round((w - chipW) / 2), cy - chipH / 2, chipW, chipH)
        .fill({ color: COLOR.slate, alpha: 0.86 })
        .stroke({ color: ink, width: 1, alpha: card.debuffed ? 0.3 : 0.75 })
    }

    this.seal.clear()
    const sealColor = SEAL_INK[card.seal]
    if (sealColor !== undefined) {
      this.seal.circle(w - 15, 16, 7).fill(sealColor)
      this.seal.circle(w - 15, 16, 7).stroke({ color: PAINT.sheen, width: 1, alpha: 0.6 })
    }

    // **덧붙은 칩은 강화 칩의 왼쪽입니다.** 가운데는 강화가 쓰므로, 아래 변에 가로로
    // 길게 두면 그 둘이 겹칩니다 — 둘 다 붙는 카드가 드물지 않습니다.
    if (card.bonusChips > 0) {
      this.seal.rect(8, h - 27, 12, 12).fill({ color: UI.chips, alpha: 0.9 })
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
        shape: tornTexture(this.uid) ?? roundedMask(SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius),
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
    // **삭는 동안은 그것 하나입니다.** 다른 필터를 함께 걸면 풀려 가는 종이 위에서 무늬가
    // 계속 흐르고, 그것은 삭는 것으로 읽히지 않습니다.
    if (this.eroding) {
      if (!this.erode) {
        this.erode = new ErodeFilter()
        // **격자를 판의 크기로 세웁니다.** 넘기지 않으면 판 하나가 한 칸입니다.
        this.erode.fit(SIZE.cardWidth, SIZE.cardHeight)
      }
      this.body.filters = [this.erode]
      this.filters = []
      return
    }

    const lit = this.pickMode !== 0 || this.glow > 0
    // 득점의 빛이 도는 동안은 그 모드가 앞섭니다 — 득점하는 카드는 물러나 있지 않습니다.
    // **걸지 않을 것이면 만들지도 않습니다.**
    if (lit || this.pick) this.picker().mode = this.glow > 0 ? 2 : this.pickMode

    // **둘 다 종이에만 겁니다.** 카드 전체에 걸면 그림자까지 함께 빛나고, 득점하는 카드가
    // 들려 있는 동안에는 그 그림자가 카드에서 떨어져 있어 빛나는 얼룩 하나가 따로 남습니다.
    //
    // 차례가 있습니다 — 무늬를 먼저 얹고 그 결과의 둘레에 빛을 두릅니다.
    const stack: Filter[] = []
    if (this.edition) stack.push(this.edition)
    if (lit) stack.push(this.picker())
    // 시드는 것도 맨 위입니다. 둘이 함께 걸릴 일은 없습니다 — 갈리는 것과 죽는 것입니다.
    if (this.blight) stack.push(this.blight)
    this.body.filters = stack
    this.filters = []
  }

  /**
   * 무력해집니다.
   *
   * **있던 그대로 죽는 일입니다.** 금이 가운데에서 바깥으로 번지고, 번진 자리는 색이
   * 빠집니다 — 다 번지면 죽은 얼굴로 갈아 끼웁니다. 카드가 이미 죽은 채로 그려져 있으면
   * 무엇이 방금 일어난 것인지 화면에 없습니다.
   *
   * @param card 다 번진 자리의 카드. 넘기지 않으면 얼굴은 그대로입니다.
   */
  wither(card?: CardInstance, look?: EditionLook): void {
    this.blight ??= new BlightFilter()
    this.blight.amount = 1
    this.blight.spread = 0
    this.blighting = 0
    this.withering = card ? { card, look } : undefined
    this.restack()
  }

  /**
   * 타서 사라집니다.
   *
   * **조커가 없어지는 것과 같은 몸짓입니다.** 카드가 부서지는 것도 없어지는 일이므로,
   * 옅어지며 지워지면 「치웠다」이지 「없앴다」가 아닙니다 — 소멸선이 위에서 아래로 훑고
   * 지나가며 표면이 모래알로 풀리고, 풀린 것이 바람에 실려 아래로 흩어집니다.
   */
  crumble(): void {
    if (this.eroding) return
    this.eroding = true
    this.age = 0
    this.eventMode = 'none'
    this.blight = undefined
    this.blighting = undefined
    // **거는 것보다 먼저 굽습니다.** 뒤에 구우면 이미 삭기 시작한 판이 구워집니다.
    this.motes = startMotes(this.body, SIZE.cardWidth, SIZE.cardHeight)
    this.restack()
  }

  /**
   * 지금 뒷면이 보이는가. **검증 도구가 묻는 값입니다.**
   *
   * 뒤집히는 것은 125밀리초씩 두 번이라 눈으로는 「뭔가 돌았다」까지만 보이고, 뒷면을 실제로
   * 거쳤는지는 프레임을 읽어야 갈립니다.
   */
  get facingBack(): boolean {
    return this.showBack
  }

  /** 지금 시드는 중인가. **검증 도구가 묻는 값입니다.** */
  get blighted(): boolean {
    return this.blighting !== undefined
  }

  /**
   * 판이 다 삭았는가. 그때 지웁니다.
   *
   * **알갱이를 기다리지 않습니다.** 흩어지는 것은 무대의 층에 남아 스스로 끝나므로, 판이
   * 없어진 프레임에 이 카드를 치워도 연출이 끊기지 않습니다.
   */
  get burnt(): boolean {
    return this.eroding && this.age >= ERODE_SWEEP
  }

  /**
   * 득점의 빛.
   *
   * `tint` 는 0..1 의 세 값입니다 — 칩이면 파랑, 배수면 붉은색.
   */
  shine(tint: [number, number, number], strength = 1): void {
    this.glow = Math.max(this.glow, Math.min(1, strength))
    const pick = this.picker()
    pick.setTint(tint[0], tint[1], tint[2])
    pick.glow = this.glow
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
    g.circle(w / 2, h + 11, 4.5).fill(HINT_INK)
    g.circle(w / 2, h + 11, 4.5).stroke({ color: UI.outline, width: 1.5 })
  }

  /** 1 고름 · -1 고르지 않음 · 0 그대로. */
  setPick(mode: number, tint: [number, number, number]): void {
    // **색은 걸려 있을 때만 넣습니다.** 이 함수는 `refresh` 마다 손패 전부에 불리므로,
    // 여기서 필터를 찾으면 고르지 않은 카드도 하나씩 갖게 됩니다.
    if (mode !== 0 || this.pick) this.picker().setTint(tint[0], tint[1], tint[2])
    if (mode === this.pickMode) return
    this.pickMode = mode
    this.picker().mode = mode
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
    // **떠나는 순간에 기울어집니다.** 곧게 미끄러져 곧게 멈추던 동안은 카드가 옮겨 놓인
    // 것이지 던져진 것이 아니었습니다 — 기울기는 자리와 같은 용수철이라 날아가는 동안
    // 0 으로 돌아옵니다.
    this.motion.rotation.snap((Math.random() - 0.5) * 16)
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

  /**
   * 득점할 때 한 번 튀어오릅니다.
   *
   * **위로만 뛰지 않습니다.** 세로로만 움직이면 다섯 장이 한 줄로 오르내려 한 덩어리가
   * 출렁이는 것으로 보입니다 — 장마다 기울기와 크기가 함께 튀어야 그 한 장에서 일어난
   * 일로 읽힙니다.
   */
  pop(strength = 1): void {
    this.motion.y.kick(-260 * strength)
    this.motion.scale.target = 1
    this.motion.scale.kick(2.2 * strength)
    this.motion.rotation.kick((Math.random() - 0.5) * 18 * strength)
  }

  advance(seconds: number, time: number): void {
    this.easePointer(seconds)
    this.motion.advance(seconds)

    if (this.eroding) {
      // **판은 제자리에서 조금 내려앉습니다.** 떠오르는 것은 타서 가벼워진 종이이고, 모래로
      // 풀리는 판은 무게를 잃지 않습니다 — 기울지도 않습니다.
      // **조커와 같은 값입니다**(`JokerView.advance`).
      this.age += seconds
      if (this.erode) this.erode.erode = this.age / ERODE_SWEEP
      this.y += seconds * 7
      // **알갱이는 이 판의 안쪽 좌표를 씁니다.** 판이 내려앉으면 알갱이도 따라갑니다.
      this.motes?.place(this.body)
      return
    }

    // 금이 번집니다. **다 번지는 자리에서 얼굴이 갈립니다** — 그 앞에서 갈면 아직 살아
    // 있는 자리가 죽은 얼굴로 그려집니다.
    if (this.blighting !== undefined) {
      this.blighting += seconds / 0.52
      if (this.withering && this.blighting >= 0.72) {
        const one = this.withering
        this.withering = undefined
        this.set(one.card, one.look)
      }
      if (this.blighting >= 1) {
        this.blighting = undefined
        this.blight = undefined
        this.restack()
      } else if (this.blight) {
        this.blight.spread = this.blighting
        // 다 번진 뒤에는 잦아듭니다. 죽은 모습은 얼굴이 들고 있습니다.
        this.blight.amount = this.blighting < 0.8 ? 1 : (1 - this.blighting) / 0.2
      }
    }

    if (this.slamming && this.motion.x.settled && this.motion.y.settled) {
      this.slamming = false
      this.motion.soft()
      // **닿는 순간에 한 번 눌립니다.** 자리에 「짝」 붙는 것은 멈추는 것만으로는 남지
      // 않습니다 — 종이 한 장이 판에 닿아 튀는 그 한 번이 「놓았다」와 「던졌다」를 가릅니다.
      this.motion.y.kick(-120)
      this.motion.rotation.kick((Math.random() - 0.5) * 10)
      this.motion.scale.target = 1
      this.motion.scale.kick(1.4)
    }

    if (this.flipAt !== undefined && time >= this.flipAt) {
      this.flipAt = undefined
      // 뒷면으로 멈춰 있던 것이 돌아옵니다.
      if (this.showBack) {
        this.flip = 1
        this.swapped = false
      }
    }

    // 8분의 1초입니다. 파도로 뒤집히므로 한 장이 길면 앞 장과 겹쳐 한 덩어리로 보입니다.
    if (this.flip > 0) {
      this.flip = Math.max(0, this.flip - seconds * 8)
      // 절반을 지나면 보이는 면이 갈립니다 — 좁아졌다가 벌어지는 그 순간입니다.
      if (this.flip <= 0.5 && !this.swapped) {
        this.swapped = true
        if (this.showBack) {
          this.showBack = false
          this.render()
          // **뒤집히는 그 순간에 소리가 나야 합니다.** 뽑는 것은 무엇이 올지 모르는 채로
          // 기다리는 일이고, 그 기다림이 끝나는 자리가 여기입니다. 갈린 카드도 같습니다 —
          // 새 얼굴이 보이는 그 순간이 무엇이 되었는지를 아는 자리입니다.
          this.onFlipped?.()
        } else if (this.turnBack !== undefined) {
          // 앞면에서 뒷면으로. **얼굴은 뒷면 뒤에서 갈아 끼웁니다** — 그동안 보이는 것은
          // 뒷면이므로 갈리는 것이 화면에 남지 않습니다.
          const one = this.turnBack
          this.turnBack = undefined
          this.showBack = true
          this.set(one.card, one.look)
          // 뒷면으로 멈춰 있다가 돌아옵니다.
          this.flipAt = time + one.hold
        } else if (this.turning !== undefined) {
          // 앞면에서 앞면으로. 엎어지는 것이 이 길입니다 — 그 카드가 뒷면이 됩니다.
          const one = this.turning
          this.turning = undefined
          this.set(one.card, one.look)
          this.onFlipped?.()
        }
      }
    }
    // 들린 만큼 종이만 올라갑니다. 그림자는 자리에 남습니다.
    this.lift.advance(seconds)
    this.body.y = -this.lift.value

    this.edition?.at(time, this.pointer)
    if (this.pick && (this.pickMode !== 0 || this.glow > 0)) this.pick.time = time

    if (this.glow > 0) {
      this.glow = Math.max(0, this.glow - seconds * 1.5)
      this.picker().glow = this.glow
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
    // **겹치는 차례는 여기서 정하지 않습니다.** 줄이 정합니다(`Game.restackRow`) — 여기서
    // 가리킨 것과 고른 것만 올리고 나머지를 0 으로 두었더니, 줄이 적어 둔 차례가 매 프레임
    // 지워졌습니다. 손패는 9장부터 겹치고 그때 겹치는 차례는 발동하는 차례입니다.
  }
}
