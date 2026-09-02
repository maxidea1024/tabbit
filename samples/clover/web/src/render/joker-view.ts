// 조커 한 장.
//
// **무엇을 하는지 얼굴에 적혀 있어야 합니다.** 이름만으로는 살지 말지를 정할 수 없고,
// 그 설명은 `core/describe.ts` 가 효과 행에서 만듭니다 — 손으로 적은 문장이 아닙니다.
//
// 그림 파일이 아직 없으므로 식별자에서 만든 문양으로 그립니다. 같은 조커는 언제나 같은
// 모양이고, 희귀도가 테두리 색입니다.

import { Container, type Filter, Graphics, Rectangle, Sprite, Text } from 'pixi.js'
import { tf } from '../core/strings'

import { EditionKind } from '../generated/enums/edition-kind'
import type { JokerInstance } from '../core/state'
import { DissolveFilter } from '../shader/dissolve'
import { ArriveFilter } from '../shader/arrive'
import { EditionFilter, type EditionShader } from '../shader/editions'
import { roundedMask } from '../shader/mask'
import { artFor } from './art'
import { drawGlyph, glyphFor, hashOf, hsl, shade, tintUp } from './glyph'
import { Motion, sway } from './motion'
import { COLOR, rarityColor, SIZE } from './theme'
import type { EditionLook } from './card-view'

/** 카드의 모서리와 이름 띠의 높이. */
const RADIUS = 9
const BAND = 26

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
  /**
   * 조커 딱지 자체. **에디션 셰이더가 이것에만 걸립니다.**
   *
   * 그림자까지 함께 감싸면 필터가 도는 사각형이 딱지보다 커지고, 셰이더에 넘긴 모양 그림이
   * 딱지와 어긋납니다. `card-view.ts` 와 같은 이유입니다.
   */
  private readonly body = new Container()
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
      fontSize: 11, fill: COLOR.ink, align: 'center', fontWeight: '800',
      // **낙말을 중간에서 자르지 않습니다.** 자르면 독일어의 합성어가
      // 「Messinggewic / ht」처럼 끝어져 읽힐 수 없게 됩니다 — 넘치는 것은
      // 아래에서 글자를 줄여 맞춥니다.
      wordWrap: true, wordWrapWidth: SIZE.jokerWidth - 8, breakWords: false, lineHeight: 12,
    },
  })
  private readonly counter = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.mult, fontWeight: '800' },
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
  private readonly dissolve = new DissolveFilter()
  private burn = 0
  private burning = false
  /**
   * 사서 오는 동안 걸리는 것.
   *
   * **살 때 한 번 만들고 다 쓰면 놓습니다.** 판이 도는 내내 물결을 굽고 있을 이유가
   * 없습니다 — 조커 줄에 다섯이 서 있으면 그 다섯이 전부 도는 것이 됩니다.
   */
  private arrive?: ArriveFilter
  /** 울렁이는 정도. 오는 동안 1 에서 0 으로 잦아듭니다. */
  private warp = 0
  /** 번쩍이는 정도. 닿는 순간 1 이고 곧 0 으로 갑니다. */
  private glow = 0

  hovered = false
  pointer = 0
  /** 발동해서 흔들리는 정도. 0 이면 조용합니다. */
  private rattle = 0
  /** 흔들리는 위상. 잦아드는 동안 좌우로 오갑니다. */
  private shiver = 0

  constructor(joker: JokerInstance, look: JokerLook) {
    super()
    this.uid = joker.uid
    this.look = look
    this.body.addChild(this.plate, this.emblem, this.clip, this.band, this.frame,
      this.nameText, this.counterPlate, this.counter)
    this.body.boundsArea = new Rectangle(0, 0, SIZE.jokerWidth, SIZE.jokerHeight)
    this.addChild(this.shadow, this.body)
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
    this.shadow.roundRect(3, 5, w, h, RADIUS).fill({ color: 0x000000, alpha: 0.4 })

    // 카드의 바탕. **그림이 덮으므로 보이는 것은 모서리뿐입니다** — 그림이 아직 안 읽혔을
    // 때 흰 자리가 번쩍이지 않게 어두운 색을 깝니다.
    this.plate.clear()
    this.plate.roundRect(0, 0, w, h, RADIUS).fill(hsl(hue, 0.35, 0.14))

    // 그림이 앉을 자리를 오려 냅니다. 카드의 둥근 모서리를 그림도 따릅니다.
    // **그림이 있을 때만 채웁니다** — 마스크로 쓰이지 않는 동안에는 이것이 그대로 흰
    // 사각형으로 그려져 카드를 덮습니다.
    this.clip.clear()

    this.art?.destroy()
    this.art = undefined

    // **그림이 카드를 가득 채웁니다.** 액자 안의 작은 그림으로 두면 카드가 아니라 아이콘이
    // 되고, 무엇을 사는 것인지 줄에서 읽히지 않습니다.
    const texture = artFor('joker', joker.jokerId)
    if (texture) {
      this.clip.roundRect(0, 0, w, h, RADIUS).fill(0xffffff)
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
    this.band.clear()
    this.band.roundRect(0, h - BAND, w, BAND, RADIUS).fill({ color: 0x0b1018, alpha: 0.88 })
    this.band.rect(0, h - BAND, w, BAND - RADIUS).fill({ color: 0x0b1018, alpha: 0.88 })
    this.band.rect(0, h - BAND, w, 1.5).fill({ color: edge, alpha: 0.9 })

    // 테두리. **희귀도가 테두리입니다** — 줄에 여럿이 서면 그 색이 먼저 읽힙니다.
    this.frame.clear()
    this.frame.roundRect(1.25, 1.25, w - 2.5, h - 2.5, RADIUS - 1)
      .stroke({ color: edge, width: 2.5 })
    this.frame.roundRect(4, 4, w - 8, h - 8, RADIUS - 3)
      .stroke({ color: 0xffffff, width: 1, alpha: 0.10 })

    this.nameText.text = look.name
    this.nameText.anchor.set(0.5, 0.5)
    this.nameText.position.set(w / 2, h - BAND / 2)
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
        .fill({ color: 0x0b1018, alpha: 0.85 })
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
   * **`body` 에만 겁니다 — 그림자는 뺍니다.** 통째로 걸면 그림자에도 걸려, 딱지가 들려
   * 있는 동안 빛나는 얼룩 하나가 그 아래에 따로 남습니다.
   */
  private restack(): void {
    if (this.burning) {
      this.body.filters = [this.dissolve]
      return
    }
    const stack: Filter[] = []
    if (this.edition) stack.push(this.edition)
    if (this.arrive) stack.push(this.arrive)
    this.body.filters = stack
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
    this.restack()
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

    if (this.burning) {
      // **아래에서 위로, 그리고 조금 떠오릅니다.** 종이가 타면 가벼워집니다.
      this.burn = Math.min(1, this.burn + seconds * 1.6)
      this.dissolve.burn = this.burn
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
    const shake = this.rattle * this.rattle
    const aside = Math.sin(this.shiver) * 15 * shake
      + Math.sin(this.shiver * 2.7) * 7 * shake

    const wobble = sway(time, this.motion.phase, 1.1, 1.1)
    this.x = this.motion.x.value + aside
    this.y = this.motion.y.value - (this.hovered ? 10 : 0)
      + sway(time, this.motion.phase * 1.3, 1.8, 0.7)
    this.rotation = (this.motion.rotation.value + wobble
      + Math.sin(this.shiver * 1.3) * 8 * shake) * (Math.PI / 180)

    const want = this.hovered ? 1.1 : 1
    if (Math.abs(this.motion.scale.target - want) > 0.001) this.motion.scale.target = want
    this.scale.set(this.motion.scale.value)
    this.zIndex = this.hovered ? 300 : 0
  }
}
