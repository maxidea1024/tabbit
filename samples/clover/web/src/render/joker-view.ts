// 조커 한 장.
//
// **무엇을 하는지 얼굴에 적혀 있어야 합니다.** 이름만으로는 살지 말지를 정할 수 없고,
// 그 설명은 `core/describe.ts` 가 효과 행에서 만듭니다 — 손으로 적은 문장이 아닙니다.
//
// 그림 파일이 아직 없으므로 식별자에서 만든 문양으로 그립니다. 같은 조커는 언제나 같은
// 모양이고, 희귀도가 테두리 색입니다.

import { COLOR, PAINT } from './ink'
import { Container, type Filter, Graphics, Sprite, Text } from 'pixi.js'
import { tf } from '../core/strings'

import { EditionKind } from '../generated/enums/edition-kind'
import type { JokerInstance } from '../core/state'
import { DissolveFilter } from '../shader/dissolve'
import { BlightFilter } from '../shader/blight'
import { ArriveFilter } from '../shader/arrive'
import { EDITION_SHADER, EditionFilter, type EditionLook } from '../shader/editions'
import { insetRadius } from './skin'
import { roundedMask } from '../shader/mask'
import { artFor } from './art'
import { drawGlyph, glyphFor, hashOf, hsl, shade, tintUp } from './glyph'
import { Motion, sway } from './motion'
import { pinBox } from './pin'
import { UI, SIZE, rarityColor } from './theme'

/** 카드의 모서리와 이름 띠의 높이. */
const RADIUS = 9
const BAND = 26

/** 식별자에서 색상 하나. 같은 조커는 언제나 같은 색입니다. */
function hueOf(text: string): number {
  return hashOf(text) % 360
}

/**
 * 카드의 테두리. **금속 테 하나에 리벳 넷입니다.**
 *
 * `skin.ts` 의 판때기 문법은 「채우기 하나와 테 하나」이고, 두께를 내는 것을 한 번 얹었다가
 * 되돌린 기록이 그 파일 머리에 있습니다 — 되돌린 이유는 테가 두 겹이 되면 판 안의 글과
 * 칸이 그만큼 좁아진다는 것이었습니다.
 *
 * **카드에는 그 이유가 걸리지 않습니다.** 카드 안쪽은 글이 아니라 그림이고, 이름은 아래
 * 띠에 놓입니다. 그래서 여기서만 두께를 냅니다. 판때기는 그 문법을 그대로 지킵니다.
 *
 * 층이 다섯입니다 — 바깥 어두운 윤곽 · 금속 테의 어두운 쪽 · 밝은 쪽 · 안쪽 어두운 선 ·
 * 네 귀의 리벳. **밝은 쪽을 안쪽에 두는 것이 경사를 만듭니다**: 빛이 위에서 오는 판이므로
 * 테의 안쪽 면이 밝고 바깥 면이 어둡습니다.
 */
function drawCardFrame(g: Graphics, w: number, h: number, edge: number): void {
  g.clear()

  // 바깥 윤곽. **이 화풍의 그림이 굵은 어두운 윤곽을 가지므로 카드도 같아야 합니다.**
  g.roundRect(0.75, 0.75, w - 1.5, h - 1.5, insetRadius(RADIUS, 0.75))
    .stroke({ color: FRAME_INK, width: 1.5 })

  // 금속 테. 어두운 쪽이 바깥, 밝은 쪽이 안쪽입니다.
  g.roundRect(2.75, 2.75, w - 5.5, h - 5.5, insetRadius(RADIUS, 2.75))
    .stroke({ color: shade(edge, 0.58), width: 2.5 })
  g.roundRect(4.5, 4.5, w - 9, h - 9, insetRadius(RADIUS, 4.5))
    .stroke({ color: tintUp(edge, 0.32), width: 1.5 })

  // 안쪽 선. 테와 그림을 갈라 줍니다 — 없으면 밝은 그림에서 테가 그림에 섞입니다.
  g.roundRect(5.75, 5.75, w - 11.5, h - 11.5, insetRadius(RADIUS, 5.75))
    .stroke({ color: FRAME_INK, width: 1, alpha: 0.75 })

  // 네 귀의 리벳. **이 하나가 웹 테두리와 게임 테두리를 가릅니다.**
  const inset = 6.5
  for (const [x, y] of [[inset, inset], [w - inset, inset],
                        [inset, h - inset], [w - inset, h - inset]]) {
    g.circle(x, y, 2.1).fill(shade(edge, 0.5))
    g.circle(x - 0.35, y - 0.35, 1.35).fill(tintUp(edge, 0.5))
  }
}

/** 테두리의 어두운 층. **겉면을 따라가지 않습니다** — 카드는 판 위에 놓이는 물건입니다. */
const FRAME_INK = 0x0a0d14

/**
 * 테두리의 안쪽 경계.
 *
 * **이름 띠가 이 선 안에 들어와야 합니다.** 띠를 카드 폭 전체로 그리면 왼쪽과 오른쪽에서
 * 테두리를 덮어 끊고, 그 자리만 두께가 사라져 카드가 한 벌로 보이지 않습니다.
 * `drawCardFrame` 의 안쪽 선과 같은 값입니다.
 */
const FRAME_IN = 5.75

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
  /**
   * 그림자를 뺀 딱지 전부 — 그림과 그 위의 글. **타고 울렁이는 것은 이것에 걸립니다.**
   *
   * 그림자까지 함께 감싸면 필터가 도는 사각형이 딱지보다 커지고, 딱지가 들려 있는 동안
   * 빛나는 얼룩 하나가 그 아래에 따로 남습니다. `card-view.ts` 와 같은 이유입니다.
   */
  private readonly sheet = new Container()
  /**
   * 그림 부분. **에디션 셰이더가 이것에만 걸립니다.**
   *
   * 셰이더에 넘긴 모양 그림이 딱지와 겹치도록 넓이를 딱지 크기로 고정합니다.
   */
  private readonly body = new Container()
  /**
   * 글 부분 — 이름 띠·테두리·이름·누적값. **에디션 셰이더 밖입니다.**
   *
   * 글은 선명해야 읽힙니다. 셰이더를 거치면 그림으로 한 번 구워져 글자의 가장자리가
   * 흐려지고, 네거티브는 띠를 밝게 뒤집어 그 위의 밝은 글자가 읽히지 않습니다. 띠와 테두리도
   * 함께 나옵니다 — 띠만 나오면 테두리의 아랫변을 덮고, 테두리는 희귀도의 색이므로
   * 뒤집히면 안 됩니다.
   */
  private readonly face = new Container()
  private readonly plate = new Graphics()
  /** 그림을 카드 모양으로 오려 내는 것. */
  private readonly clip = new Graphics()
  /** 이름이 앉는 띠. 그림 위에 얹힙니다. */
  private readonly band = new Graphics()
  /** 테두리. 희귀도의 색입니다. */
  private readonly frame = new Graphics()
  /** 누적값이 앉는 바탕. */
  private readonly counterPlate = new Graphics()
  private readonly emblem = new Graphics()
  /** 그림이 있으면 이것이 문양을 대신합니다. */
  private art?: Sprite
  private readonly nameText = new Text({
    text: '',
    style: {
      fontSize: 11, fill: UI.ink, align: 'center', fontWeight: '800',
      // **낙말을 중간에서 자르지 않습니다.** 자르면 독일어의 합성어가
      // 「Messinggewic / ht」처럼 끝어져 읽힐 수 없게 됩니다 — 넘치는 것은
      // 아래에서 글자를 줄여 맞춥니다.
      wordWrap: true, wordWrapWidth: SIZE.jokerWidth - 8, breakWords: false, lineHeight: 12,
    },
  })
  private readonly counter = new Text({
    text: '', style: { fontSize: 12, fill: UI.mult, fontWeight: '800' },
  })
  private edition?: EditionFilter
  /**
   * 지금 걸려 있는 에디션.
   *
   * **같은 것이면 다시 만들지 않습니다.** 새로 만들면 셰이더의 시계가 0으로 돌아가는데,
   * `set` 은 화면을 다시 그릴 때마다 불립니다 — 패를 한 장 깔 때마다 흐름이 끊깁니다.
   */
  private editionKind?: EditionKind
  /**
   * 타서 사라지는 중.
   *
   * **팔린 조커는 미끄러져 나가지 않습니다.** 나가는 것은 「치웠다」이고, 판 것은 없앤
   * 것입니다 — 종이가 타는 모습이 그 둘을 가릅니다.
   */
  /**
   * 이 딱지가 줄에서 갖는 그리기 차례. 되돌릴 값입니다 — 까닭은 `CardView.rowZ` 와 같습니다.
   */
  rowZ = 0

  /** 시드는 금. 번지는 동안만 걸립니다. */
  private blight?: BlightFilter
  /** 금이 어디까지 번졌는가. */
  private blighting?: number
  private dissolve?: DissolveFilter
  private burn = 0
  private burning = false
  /**
   * 사서 오는 동안 걸리는 것.
   *
   * **살 때 한 번 만들고 다 쓰면 놓습니다.** 판이 도는 내내 물결을 굽고 있을 이유가
   * 없습니다 — 조커 줄에 다섯이 놓여 있으면 그 다섯이 전부 도는 것이 됩니다.
   */
  private arrive?: ArriveFilter
  /** 울렁이는 정도. 오는 동안 1 에서 0 으로 잦아듭니다. */
  private warp = 0
  /** 번쩍이는 정도. 닿는 순간 1 이고 곧 0 으로 갑니다. */
  private glow = 0

  hovered = false
  /**
   * 고른 것인가. **그러면 커서에 반응하지 않습니다.**
   *
   * 고른 딱지는 이미 `HELD_RISE` 만큼 올라가 있고 그 밑에 단추가 놓였습니다 — 거기에 커서의
   * 10픽셀과 1.1배가 더 얹히면 조커 줄은 화면의 맨 위라 윗변이 화면 밖으로 나갑니다.
   * 그리고 커서를 단추로 옮기는 동안 딱지가 10픽셀 내려앉는 것도 없어집니다.
   */
  held = false
  pointer = 0
  /** 발동해서 흔들리는 정도. 0 이면 조용합니다. */
  private rattle = 0
  /** 흔들리는 위상. 잦아드는 동안 좌우로 오갑니다. */
  private shiver = 0

  constructor(joker: JokerInstance, look: JokerLook) {
    super()
    this.uid = joker.uid
    this.look = look
    this.body.addChild(this.plate, this.emblem, this.clip)
    this.face.addChild(this.band, this.frame, this.nameText, this.counterPlate, this.counter)
    // **둘 다 필터가 걸리는 통입니다.** 경계와 필터 사각형을 함께 고정합니다 — 하나만
    // 두면 구운 사진에서 이 통이 빠집니다(`pin.ts`).
    pinBox(this.body, SIZE.jokerWidth, SIZE.jokerHeight)
    pinBox(this.sheet, SIZE.jokerWidth, SIZE.jokerHeight)
    this.sheet.addChild(this.body, this.face)
    this.addChild(this.shadow, this.sheet)
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
    this.shadow.roundRect(3, 5, w, h, RADIUS).fill({ color: PAINT.veil, alpha: 0.4 })

    // 카드의 바탕. **그림이 덮으므로 보이는 것은 모서리뿐입니다** — 그림이 아직 안 읽혔을
    // 때 흰 자리가 번쩍이지 않게 어두운 색을 깝니다.
    // **조커마다의 색조를 걷었습니다.** 그림의 배경이 이제 소재마다 다른 색이므로, 판까지
    // 색을 돌리면 두 색이 겹쳐 부딪칩니다. 중립으로 둡니다.
    this.plate.clear()
    this.plate.roundRect(0, 0, w, h, RADIUS).fill(FRAME_INK)

    // 그림이 앉을 자리를 오려 냅니다. 카드의 둥근 모서리를 그림도 따릅니다.
    // **그림이 있을 때만 채웁니다** — 마스크로 쓰이지 않는 동안에는 이것이 그대로 흰
    // 사각형으로 그려져 카드를 덮습니다.
    this.clip.clear()

    // **그림이 카드를 가득 채웁니다.** 액자 안의 작은 그림으로 두면 카드가 아니라 아이콘이
    // 되고, 무엇을 사는 것인지 줄에서 읽히지 않습니다.
    const texture = artFor('joker', joker.jokerId)
    // **같은 그림이면 스프라이트를 그대로 둡니다.** 다시 그릴 때마다 버리고 새로 만들면
    // 들고 있는 조커 다섯이 `refresh` 마다 다섯 번 그 일을 합니다.
    if (this.art && this.art.texture !== texture) {
      this.art.destroy()
      this.art = undefined
    }
    if (texture) this.clip.roundRect(0, 0, w, h, RADIUS).fill(PAINT.sheen)
    if (texture && !this.art) {
      const sprite = new Sprite(texture)
      // 넓이에 맞추고 남는 세로를 가운데에서 자릅니다. 그림에 테두리가 있으므로 조금
      // 잘려도 티가 나지 않습니다.
      const scale = Math.max(w / texture.width, h / texture.height)
      sprite.width = texture.width * scale
      sprite.height = texture.height * scale
      sprite.position.set((w - sprite.width) / 2, (h - sprite.height) / 2)
      sprite.mask = this.clip
      this.art = sprite
      this.body.addChildAt(sprite, this.body.getChildIndex(this.plate) + 1)
    }

    // 그림이 아직 없으면 문양 하나를 그립니다. **202장을 한 번에 만들지 않으므로 절반만
    // 있는 상태에서도 화면이 돌아야 합니다.**
    this.emblem.clear()
    if (!texture) {
      const glyphInk = tintUp(hsl(hue, 0.7, 0.62), 0.25)
      this.emblem.roundRect(0, 0, w, h, RADIUS).fill(hsl((hue + 22) % 360, 0.6, 0.12))
      drawGlyph(this.emblem, glyphFor(joker.jokerId), w / 2, h / 2 - 8, 46, {
        fill: glyphInk,
        line: shade(glyphInk, 0.62),
      })
    }

    // 이름 띠. **그림 위에 얹힙니다** — 카드 아래를 덮어야 이름이 그림의 일부가 아니라
    // 이 카드의 이름으로 읽힙니다.
    // **테두리 안쪽에 들어옵니다.** 아래 모서리만 카드의 곡률을 따르고 윗변은 직선입니다.
    this.band.clear()
    const bandX = FRAME_IN
    const bandW = w - FRAME_IN * 2
    const bandTop = h - BAND
    const bandH = BAND - FRAME_IN
    const bandRadius = insetRadius(RADIUS, FRAME_IN)
    this.band.roundRect(bandX, bandTop, bandW, bandH, bandRadius)
      .fill({ color: COLOR.band, alpha: 0.92 })
    this.band.rect(bandX, bandTop, bandW, bandH - bandRadius)
      .fill({ color: COLOR.band, alpha: 0.92 })
    // 띠의 윗변. **테두리와 같은 문법입니다** — 어두운 선 위에 희귀도 색이 얹힙니다.
    this.band.rect(bandX, bandTop, bandW, 1).fill({ color: FRAME_INK, alpha: 0.9 })
    this.band.rect(bandX, bandTop + 1, bandW, 1.5).fill({ color: edge, alpha: 0.95 })

    // 테두리. **희귀도가 테두리입니다** — 줄에 여럿이 서면 그 색이 먼저 읽힙니다.
    drawCardFrame(this.frame, w, h, edge)

    this.nameText.text = look.name
    this.nameText.anchor.set(0.5, 0.5)
    // **띠의 가운데입니다.** 띠가 테두리 안쪽으로 들어와 아래가 짧아졌으므로, 카드 기준으로
    // 두면 이름이 띠보다 3픽셀 아래에 앉습니다.
    this.nameText.position.set(w / 2, (bandTop + bandTop + bandH) / 2)
    // 한 낙말이 카드보다 길면 줄바꿈으로는 들어가지 않습니다. 그때만 줄입니다.
    this.nameText.scale.set(1)
    const room = w - 8
    if (this.nameText.width > room) {
      this.nameText.scale.set(Math.max(0.62, room / this.nameText.width))
    }

    // 누적값을 얼굴에 적습니다 — 늘어나는 조커는 그것이 전부이기 때문입니다.
    const { chips, multAdd, multMul } = joker.counters
    const parts: string[] = []
    if (chips !== 0) parts.push(tf('ui.counter.chips', { n: chips }))
    if (multAdd !== 0) parts.push(`+${(multAdd / 10_000).toFixed(0)}`)
    // 0 은 「곱이 없다」가 아니라 「아직 값이 없다」입니다. 적지 않습니다.
    if (multMul !== 10_000 && multMul !== 0) parts.push(`×${(multMul / 10_000).toFixed(2)}`)
    this.counter.text = parts.join(' ')

    // 누적값은 그림 위이므로 바탕을 하나 깝니다. 없으면 그림에 묻힙니다.
    this.counterPlate.clear()
    if (this.counter.text !== '') {
      const pad = 6
      const width = this.counter.width + pad * 2
      this.counterPlate.roundRect((w - width) / 2, 5, width, 18, 6)
        .fill({ color: COLOR.band, alpha: 0.85 })
    }
    this.counter.anchor.set(0.5, 0)
    this.counter.position.set(w / 2, 7)

    this.alpha = joker.disabled ? 0.35 : 1

    const shader = EDITION_SHADER[joker.edition]
    if (shader && look.edition) {
      if (this.editionKind !== joker.edition) {
        this.editionKind = joker.edition
        this.edition = new EditionFilter(shader, {
          strength: look.edition.strength,
          flowSpeed: look.edition.flowSpeed,
          noise: look.edition.noise,
          shape: roundedMask(SIZE.jokerWidth, SIZE.jokerHeight, RADIUS),
        })
        this.restack()
      }
    } else if (this.editionKind !== undefined) {
      this.editionKind = undefined
      this.edition = undefined
      this.restack()
    }
  }

  /**
   * 지금 걸릴 것들을 한자리에서 쌓습니다.
   *
   * **에디션은 그림에만, 타고 울렁이는 것은 글까지.** 에디션은 판이 도는 내내 걸려 있으므로
   * 글이 그 안에 있으면 이름이 내내 뿌옇습니다. 타는 것은 글도 함께 타야 하고, 울렁이는
   * 것은 잠깐이며 글만 제자리에 남으면 딱지에서 떨어진 것으로 보입니다.
   *
   * 그림자는 어디에도 넣지 않습니다 — 통째로 걸면 그림자에도 걸려, 딱지가 들려 있는 동안
   * 빛나는 얼룩 하나가 그 아래에 따로 남습니다.
   */
  private restack(): void {
    if (this.burning) {
      // **타기 시작할 때 만듭니다.** 딱지 하나가 살아 있는 동안 내내 들고 있을 것이
      // 아닙니다 — 도감 한 화면이 딱지 60개이고 그 가운데 타는 것은 없습니다. 바로 위의
      // `arrive` 와 같은 규약입니다.
      this.dissolve ??= new DissolveFilter()
      this.body.filters = []
      this.sheet.filters = [this.dissolve]
      return
    }
    const onBody: Filter[] = []
    if (this.edition) onBody.push(this.edition)
    this.body.filters = onBody
    const onSheet: Filter[] = []
    if (this.arrive) onSheet.push(this.arrive)
    // 시드는 금은 맨 위입니다. 무늬 위를 지나가야 그 딱지에서 일어난 일로 보입니다.
    if (this.blight) onSheet.push(this.blight)
    this.sheet.filters = onSheet
  }

  /**
   * 무력해집니다. **보스가 조커 하나를 끄는 것이 이 자리입니다.**
   *
   * 카드가 죽는 것과 같은 몸짓이고 같은 셰이더입니다 — 꺼진 딱지가 옅어져 있는 것은
   * 그 뒤의 모습이고, 여기서 보이는 것은 그렇게 되는 순간입니다.
   */
  wither(): void {
    if (this.burning) return
    this.blight ??= new BlightFilter()
    this.blight.amount = 1
    this.blight.spread = 0
    this.blighting = 0
    this.restack()
  }

  place(x: number, y: number): void {
    this.motion.to(x, y, 0)
  }

  /** 태우기 시작합니다. 다 타면 `gone` 이 참이 됩니다. */
  ignite(): void {
    if (this.burning) return
    this.burning = true
    this.burn = 0
    this.eventMode = 'none'
    this.arrive = undefined
    this.blight = undefined
    this.blighting = undefined
    this.restack()
  }

  /** 지금 시드는 중인가. **검증 도구가 묻는 값입니다.** */
  get blighted(): boolean {
    return this.blighting !== undefined
  }

  /** 다 탔는가. 그때 지웁니다. */
  get gone(): boolean {
    return this.burning && this.burn >= 1
  }

  /**
   * 발동할 때 튀어오르고 좌우로 흔들립니다.
   *
   * **조각을 터뜨리지 않습니다.** 조커는 한 판에 열 번도 발동하는데 그때마다 조각이
   * 터지면 화면이 시끄러워지고, 정작 카드 위에 뜬 숫자가 그 조각에 묻힙니다 — 좌우로
   * 정신없이 흔들리는 것이 훨씬 생동감 있습니다.
   */
  pop(strength = 1): void {
    this.bounce(strength)
    // 흔들림은 겹칩니다 — 잇달아 발동하면 아직 흔들리는 위에 더 얹힙니다.
    this.rattle = Math.min(1.6, this.rattle + strength)
  }

  /**
   * 자리에 닿을 때 한 번 튀어오릅니다.
   *
   * **흔들리지 않습니다.** 흔들림은 「발동했다」는 뜻이므로, 사서 줄에 꽂히는 것에 그것을
   * 걸면 아무 이유 없이 난리치는 것으로 보입니다.
   */
  bounce(strength = 1): void {
    this.motion.y.kick(-300 * strength)
    this.motion.rotation.kick((Math.random() - 0.5) * 22)
    this.motion.scale.target = 1 + 0.16 * strength
  }

  /**
   * 사서 오기 시작합니다. **오는 내내 울렁입니다.**
   *
   * 산 자리에서 줄까지 날아오는 그 동안이고, 닿으면 `landing` 이 이어받습니다.
   */
  buying(): void {
    this.arrive ??= new ArriveFilter()
    this.warp = 1
    // 오는 길이 보이도록 느리게 갑니다. 닿으면 원래 용수철로 돌아옵니다.
    this.motion.drift()
    this.restack()
  }

  /** 자리에 닿았습니다. **딱지 전체가 한 번 하얗게 번쩍입니다.** */
  landing(): void {
    this.arrive ??= new ArriveFilter()
    this.glow = 1
    this.warp = 0
    this.motion.soft()
    this.restack()
  }

  /**
   * 겉면만 한 틱. **자리는 건드리지 않습니다.**
   *
   * 줄에 선 딱지는 `advance` 가 자리와 겉면을 함께 돌리지만, **상점의 칸 · 팩에 펼친 카드 ·
   * 진 판의 판에 선 것은 자리를 부르는 쪽이 정합니다** — 그것들에까지 `advance` 를 부르면
   * 용수철이 딱지를 제 목표(0, 0)로 끌어갑니다. 그렇다고 아무것도 부르지 않으면 판의
   * 셰이더가 시각을 받지 못해 `uTime` 이 0 에 굳고, 무늬가 흐르지 않습니다.
   */
  lookAt(time: number): void {
    this.edition?.at(time, this.pointer)
  }

  /**
   * 판의 셰이더가 지금 보고 있는 시각과 기울기. 없으면 `undefined`. **도구가 조회합니다.**
   *
   * 둘 다 무늬의 위상에 그대로 들어갑니다 — 흐르지 않는 것은 시각이 멈춘 것이고, 튀는
   * 것은 기울기가 뛴 것입니다. **눈으로는 그 둘이 갈리지 않습니다.**
   */
  get editionAt(): { time: number; tilt: number } | undefined {
    return this.edition?.seen
  }

  advance(seconds: number, time: number): void {
    this.motion.advance(seconds)
    this.edition?.at(time, this.pointer)

    if (this.arrive) {
      // 울렁임은 천천히, 번쩍임은 빠르게 잦아듭니다. **번쩍임이 오래 남으면 그 자리가
      // 하얀 딱지가 되고, 무엇을 산 것인지 도리어 안 보입니다.**
      this.warp = Math.max(0, this.warp - seconds * 1.6)
      this.glow = Math.max(0, this.glow - seconds * 2.2)
      this.arrive.at(time)
      this.arrive.warp = this.warp
      // 잦아드는 끝이 밋밋하지 않게 제곱으로 뺍니다.
      this.arrive.flash = this.glow * this.glow
      if (this.warp <= 0 && this.glow <= 0) {
        this.arrive = undefined
        this.restack()
      }
    }

    if (this.blighting !== undefined) {
      this.blighting += seconds / 0.52
      if (this.blighting >= 1) {
        this.blighting = undefined
        this.blight = undefined
        this.restack()
      } else if (this.blight) {
        this.blight.spread = this.blighting
        this.blight.amount = this.blighting < 0.8 ? 1 : (1 - this.blighting) / 0.2
      }
    }

    if (this.burning) {
      // **아래에서 위로, 그리고 조금 떠오릅니다.** 종이가 타면 가벼워집니다.
      this.burn = Math.min(1, this.burn + seconds * 1.6)
      if (this.dissolve) this.dissolve.burn = this.burn
      this.y -= seconds * 26
      this.rotation += seconds * 0.12
      return
    }

    // **좌우로 정신없이.** 잦아드는 동안 빠르게 오가고, 세로가 아니라 가로입니다 —
    // 세로로 흔들면 튀어오르는 것과 섞여 무엇이 일어난 것인지 흐려집니다.
    if (this.rattle > 0) {
      this.rattle = Math.max(0, this.rattle - seconds * 1.9)
      this.shiver += seconds * 52
    } else {
      this.shiver = 0
    }
    // **흔들리지 않으면 세지 않습니다.** 답이 0인데 `Math.sin` 셋을 부르고 있었고,
    // 도감 한 화면이면 딱지 60개에 프레임마다 180번입니다.
    const shake = this.rattle * this.rattle
    const aside = shake > 0
      ? (Math.sin(this.shiver) * 15 + Math.sin(this.shiver * 2.7) * 7) * shake
      : 0

    const lifts = this.hovered && !this.held
    const wobble = sway(time, this.motion.phase, 1.1, 1.1)
    this.x = this.motion.x.value + aside
    this.y = this.motion.y.value - (lifts ? 10 : 0)
      + sway(time, this.motion.phase * 1.3, 1.8, 0.7)
    this.rotation = (this.motion.rotation.value + wobble
      + (shake > 0 ? Math.sin(this.shiver * 1.3) * 8 * shake : 0)) * (Math.PI / 180)

    const want = lifts ? 1.1 : 1
    if (Math.abs(this.motion.scale.target - want) > 0.001) this.motion.scale.target = want
    this.scale.set(this.motion.scale.value)
    // **겹치는 차례는 줄이 정합니다.** 까닭은 `CardView` 와 같습니다 — 여기서는 가리킨
    // 것만 올렸으므로 고른 딱지가 올라오지 않았습니다.
  }
}
