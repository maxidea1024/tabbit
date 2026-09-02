// 설명 쪽지.
//
// 조커 위에 마우스를 올리면 무엇을 하는지 뜹니다. **문장은 데이터에서 나옵니다** —
// `core/describe.ts` 가 효과 행을 읽어 만듭니다.
//
// **종류와 가격은 이름과 같은 줄의 오른쪽 끝에 칩으로 섭니다.** 이름 아래에 한 줄을 더
// 두면 쪽지가 그만큼 길어지고, 정작 읽어야 하는 효과가 아래로 밀립니다 — 종류는 한 낱말
// 이고 가격은 두어 글자이므로 이름의 남는 자리에 들어갑니다.

import { Container, Graphics, Text } from 'pixi.js'

import { COLOR, rarityColor } from '../render/theme'
import { richBlock, type RichStyle } from './rich'

/** 이 쪽지의 글에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 12, fill: 0xd8ecdc },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

/** 가장 좁을 때의 너비. 이름과 칩이 길면 여기서 자랍니다. */
const MIN_WIDTH = 240
/** 가장 넓을 때. 이보다 넓어지면 쪽지가 아니라 판이 됩니다. */
const MAX_WIDTH = 330
const PAD = 12
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
       x: number, y: number, bounds: { width: number; height: number },
       cost?: number): void {
    this.title.text = name

    // 칩을 먼저 만듭니다. 너비가 이것에 달려 있습니다.
    this.chips.removeChildren().forEach(child => child.destroy())
    const made: Container[] = []
    if (kindName !== '') made.push(chip(kindName, rarityColor(rarityValue)))
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
    this.plate.clear()
    this.plate.roundRect(0, 0, width, height, 10).fill({ color: 0x0d1a14, alpha: 0.96 })
    this.plate.roundRect(0.5, 0.5, width - 1, height - 1, 10)
      .stroke({ color: rarityColor(rarityValue), width: 1.5 })

    // 화면 밖으로 나가지 않게 접습니다.
    const px = Math.min(Math.max(8, x - width / 2), bounds.width - width - 8)
    const py = y + height + 16 > bounds.height ? y - height - 16 : y + 16
    this.position.set(px, Math.max(8, py))
    this.visible = true
    this.zIndex = 9000
  }

  hide(): void {
    this.visible = false
  }
}
