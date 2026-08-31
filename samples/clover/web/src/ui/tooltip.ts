// 설명 쪽지.
//
// 조커 위에 마우스를 올리면 무엇을 하는지 뜹니다. **문장은 데이터에서 나옵니다** —
// `core/describe.ts` 가 효과 행을 읽어 만듭니다.

import { Container, Graphics, Text } from 'pixi.js'

import { COLOR, rarityColor } from '../render/theme'
import { richBlock, type RichStyle } from './rich'

/** 이 쪽지의 글에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 12, fill: 0xd8ecdc },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

const WIDTH = 240

export class Tooltip extends Container {
  private readonly plate = new Graphics()
  private readonly title = new Text({
    text: '', style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly rarity = new Text({
    text: '', style: { fontSize: 10, fill: COLOR.inkDim, fontWeight: '700' },
  })
  /** 설명 줄들. **수와 이름은 다른 색입니다** — 조커가 얼마를 주는지가 먼저 읽혀야 합니다. */
  private readonly body = new Container()

  constructor() {
    super()
    this.addChild(this.plate, this.title, this.rarity, this.body)
    this.visible = false
    this.eventMode = 'none'
  }

  show(name: string, rarityName: string, rarityValue: number, lines: string[],
       x: number, y: number, bounds: { width: number; height: number }): void {
    this.title.text = name
    this.rarity.text = rarityName
    this.rarity.style.fill = rarityColor(rarityValue)
    this.body.removeChildren().forEach(child => child.destroy())
    const shown = lines.length > 0 ? lines.map(line => `· ${line}`) : ['—']
    this.body.addChild(richBlock(shown, RICH, 17, WIDTH - 24))

    this.title.position.set(12, 10)
    this.rarity.position.set(12, 30)
    this.body.position.set(12, 48)

    const height = 56 + this.body.height
    this.plate.clear()
    this.plate.roundRect(0, 0, WIDTH, height, 10).fill({ color: 0x0d1a14, alpha: 0.96 })
    this.plate.roundRect(0.5, 0.5, WIDTH - 1, height - 1, 10)
      .stroke({ color: rarityColor(rarityValue), width: 1.5 })

    // 화면 밖으로 나가지 않게 접습니다.
    const px = Math.min(Math.max(8, x - WIDTH / 2), bounds.width - WIDTH - 8)
    const py = y + height + 16 > bounds.height ? y - height - 16 : y + 16
    this.position.set(px, Math.max(8, py))
    this.visible = true
    this.zIndex = 9000
  }

  hide(): void {
    this.visible = false
  }
}
