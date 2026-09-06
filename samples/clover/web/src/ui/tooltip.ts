// 설명 쪽지.
//
// 조커 위에 마우스를 올리면 무엇을 하는지 뜹니다. **문장은 데이터에서 나옵니다** —
// `core/describe.ts` 가 효과 행을 읽어 만듭니다.
//
// **종류와 가격은 이름과 같은 줄의 오른쪽 끝에 칩으로 섭니다.** 이름 아래에 한 줄을 더
// 두면 쪽지가 그만큼 길어지고, 정작 읽어야 하는 효과가 아래로 밀립니다 — 종류는 한 낱말
// 이고 가격은 두어 글자이므로 이름의 남는 자리에 들어갑니다.

import { Container, Graphics, Text } from 'pixi.js'

import { COLOR, rarityColor, UI } from '../render/theme'
import { richBlock, type RichStyle } from './rich'

/** 이 쪽지의 글에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 12, fill: 0xd8ecdc },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

/** 가장 좁을 때의 너비. 이름과 칩이 길면 여기서 자랍니다. */
/**
 * 쪽지가 붙는 자리. **가리킨 것이 차지한 세로 구간입니다.**
 *
 * 점 하나가 아니라 위와 아래를 함께 받습니다 — 쪽지는 위를 먼저 쓰므로 그 물건의 윗변을
 * 알아야 하고, 자리가 없어 아래로 갈 때는 밑변을 알아야 합니다. 값은 **쪽지가 놓이는
 * 좌표계의 것**입니다 — 부르는 쪽이 자기 지역 좌표를 그대로 넘기면 층이 옮겨진 만큼
 * 어긋납니다.
 */
export interface TipBox {
  /** 가로 가운데. */
  x: number
  top: number
  bottom: number
}

const MIN_WIDTH = 240
/** 가장 넓을 때. 이보다 넓어지면 쪽지가 아니라 판이 됩니다. */
const MAX_WIDTH = 330
const PAD = 12
/** 튀어나오는 데 걸리는 시간. **짧습니다** — 읽으려고 올린 것이므로 기다리게 하지 않습니다. */
const POP_TIME = 0.15

/**
 * 판보다 크게 그리는 정도의 상한.
 *
 * 폰의 배율이 0.6 이므로 1.67 이면 화면에서 데스크탑과 같은 크기입니다. 그보다 작은
 * 창에서는 상한이 걸립니다.
 */
const GROW_MOST = 1.7
/** 칩의 높이. 이름 글자와 가운데가 맞습니다. */
const CHIP_H = 20

/**
 * 칩 하나.
 *
 * **테두리와 옅은 바탕입니다.** 꽉 찬 색으로 두면 이름보다 먼저 눈에 들어오고, 종류는
 * 이름을 읽은 다음에 보는 것입니다.
 */
function chip(label: string, color: number): Container {
  const node = new Container()
  const text = new Text({
    text: label,
    style: { fontSize: 11, fill: color, fontWeight: '800' },
  })
  const width = Math.ceil(text.width) + 16
  const plate = new Graphics()
  plate.roundRect(0, 0, width, CHIP_H, CHIP_H / 2)
    .fill({ color, alpha: 0.14 })
    .stroke({ color, width: 1, alpha: 0.75 })
  text.position.set(8, (CHIP_H - text.height) / 2)
  node.addChild(plate, text)
  return node
}

export class Tooltip extends Container {
  private readonly plate = new Graphics()
  private readonly title = new Text({
    text: '', style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
  })
  /** 종류와 가격의 칩. 뜰 때마다 다시 만듭니다. */
  private readonly chips = new Container()
  /** 설명 줄들. **수와 이름은 다른 색입니다** — 조커가 얼마를 주는지가 먼저 읽혀야 합니다. */
  private readonly body = new Container()

  /**
   * 튀어나오는 동안. 0에서 1이고, 1이면 다 나왔습니다.
   *
   * **그냥 나타나면 어디에서 나온 것인지가 없습니다.** 가리킨 것에 붙은 변에서 자라야
   * 그 물건의 설명으로 읽힙니다 — 그래서 자라는 기준점을 자리마다 따로 둡니다.
   */
  private pop = 1
  private restX = 0
  private restY = 0
  /** 자라는 기준점. 가리킨 것에 붙은 변의 가운데입니다. */
  private fromX = 0
  private fromY = 0

  constructor() {
    super()
    this.addChild(this.plate, this.title, this.chips, this.body)
    this.visible = false
    this.eventMode = 'none'
  }

  /**
   * 쪽지를 띄웁니다.
   *
   * `kindName` 은 종류이거나 희귀도입니다 — 비우면 칩이 서지 않습니다. `cost` 도 같습니다.
   */
  show(name: string, kindName: string, rarityValue: number, lines: string[],
       at: TipBox, bounds: { width: number; height: number },
       cost?: number,
       /**
        * 칩의 색을 손으로 정합니다.
        *
        * **희귀도가 아닌 것도 칩으로 섭니다.** 챌린지가 열렸는지 · 깼는지가 그것입니다 —
        * 되풀이되는 한 낱말은 글의 첫 줄이 아니라 이름 옆의 칩입니다.
        */
       kindTone?: number): void {
    this.title.text = name

    // 칩을 먼저 만듭니다. 너비가 이것에 달려 있습니다.
    this.chips.removeChildren().forEach(child => child.destroy())
    const made: Container[] = []
    if (kindName !== '') made.push(chip(kindName, kindTone ?? rarityColor(rarityValue)))
    if (cost !== undefined) made.push(chip(`$${cost}`, COLOR.accentNumber))

    let chipsWidth = 0
    for (const one of made) {
      one.position.set(chipsWidth, 0)
      chipsWidth += one.width + 6
      this.chips.addChild(one)
    }
    chipsWidth = Math.max(0, chipsWidth - 6)

    // **이름과 칩이 한 줄에 들어가는 만큼 넓힙니다.** 넘치면 이름이 줄고 칩은 그대로입니다 —
    // 종류와 가격은 두어 글자라 줄일 자리가 없습니다.
    const head = PAD * 2 + Math.ceil(this.title.width) + 10 + chipsWidth
    const width = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, head))
    const room = width - PAD * 2 - chipsWidth - 10
    this.title.scale.set(1)
    if (this.title.width > room) this.title.scale.set(room / this.title.width)

    this.body.removeChildren().forEach(child => child.destroy())
    const shown = lines.length > 0 ? lines.map(line => `· ${line}`) : ['—']
    this.body.addChild(richBlock(shown, RICH, 17, width - PAD * 2))

    const headTop = 11
    const headHeight = Math.max(this.title.height, made.length > 0 ? CHIP_H : 0)
    this.title.position.set(PAD, headTop + (headHeight - this.title.height) / 2)
    this.chips.position.set(width - PAD - chipsWidth,
                            headTop + (headHeight - CHIP_H) / 2)
    this.body.position.set(PAD, headTop + headHeight + 10)

    const height = headTop + headHeight + 10 + this.body.height + 12
    // **바탕과 테는 겉면을 따라갑니다.** 희귀도가 있는 것만 테에 그 색이 듭니다 —
    // 희귀도는 뜻이 있는 색이고, 그것이 없는 쪽지까지 한 가지 색으로 두르면 그 색이
    // 뜻을 잃습니다.
    this.plate.clear()
    this.plate.roundRect(0, 0, width, height, 10).fill({ color: UI.tipBack, alpha: 0.96 })
    this.plate.roundRect(0.5, 0.5, width - 1, height - 1, 10)
      .stroke({ color: rarityValue > 0 ? rarityColor(rarityValue) : UI.tipEdge, width: 1.5 })

    // 화면 밖으로 나가지 않게 접습니다. **자란 크기로 셉니다** — 폰에서는 이 쪽지가
    // 판보다 덜 줄어들므로, 자라기 전의 크기로 세면 오른쪽과 아래가 화면 밖으로 나갑니다.
    const grown = width * this.grow
    const tall = height * this.grow
    const px = Math.min(Math.max(8, at.x - grown / 2), bounds.width - grown - 8)
    // **위를 먼저 씁니다.** 손가락으로 누르는 화면에서는 아래에 띄운 쪽지가 그 손가락에
    // 가려집니다 — 가려진 쪽지는 없는 것과 같습니다. 위에 자리가 없을 때만 아래로 갑니다.
    const above = at.top - tall - 12
    const py = above >= 8 ? above : Math.min(at.bottom + 12, bounds.height - tall - 8)
    this.restX = px
    this.restY = Math.max(8, py)
    // 가리킨 것에 가까운 변에서 자랍니다. 위에 떴으면 아래 변, 아래에 떴으면 위 변입니다.
    // **쪽지 안의 자리이므로 자라기 전의 크기입니다.**
    this.fromX = Math.min(Math.max(0, at.x - px), grown) / this.grow
    this.fromY = above >= 8 ? height : 0
    this.pop = 0
    this.place()
    this.visible = true
    this.zIndex = 9000
  }

  /**
   * 튀어나오는 동안을 흘립니다.
   *
   * **화면의 시계로 돕니다.** 자기 시계로 돌면 손 시계로 세운 도구가 찍는 순간마다 다른
   * 크기가 찍힙니다.
   */
  advance(seconds: number): void {
    if (!this.visible || this.pop >= 1) return
    this.pop = Math.min(1, this.pop + seconds / POP_TIME)
    this.place()
  }

  hide(): void {
    this.visible = false
  }

  /**
   * 판이 얼마나 줄어 있는가.
   *
   * **이 쪽지는 그만큼 덜 줄어듭니다.** 판은 1280 × 800 하나에 맞춰 그려지고 폰에서는
   * 0.6배로 들어가는데, 그 배율이 설명글에 그대로 걸리면 12픽셀 글이 화면에서 7픽셀이
   * 됩니다 — 읽을 수 없는 크기입니다.
   *
   * **판 안의 것에는 이렇게 할 수 없습니다.** 카드와 단추는 서로의 자리가 정해져 있어서
   * 하나만 키우면 배치가 어긋납니다. 이 쪽지는 아무것도 밀어내지 않는 겹이라 혼자 커질
   * 수 있고, 그래서 **화면에서의 크기가 어느 기계에서나 같습니다.**
   *
   * 상한을 둡니다 — 아주 작은 창에서 쪽지가 판을 통째로 덮는 것은 읽기 편한 것이 아닙니다.
   */
  setBoardScale(scale: number): void {
    this.grow = Math.min(GROW_MOST, Math.max(1, 1 / Math.max(0.001, scale)))
  }

  /** 지금의 `pop` 으로 크기와 자리를 정합니다. 한 번 넘겼다가 돌아옵니다. */
  private place(): void {
    const pop = this.pop
    const bounce = pop < 0.6
      ? 0.78 + (1.06 - 0.78) * (pop / 0.6)
      : 1.06 - 0.06 * ((pop - 0.6) / 0.4)
    const scale = this.grow * bounce
    this.scale.set(scale)
    // 기준점이 제자리에 남도록 그만큼 밀어 줍니다. **자란 크기가 제자리입니다.**
    this.position.set(this.restX + this.fromX * (this.grow - scale),
                      this.restY + this.fromY * (this.grow - scale))
    this.alpha = Math.min(1, pop * 3.5)
  }

  /** 판보다 얼마나 크게 그리는가. 1 이면 판과 같습니다. */
  private grow = 1
}
