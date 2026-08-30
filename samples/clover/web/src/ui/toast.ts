// 토스트.
//
// **무엇이 일어났는지를 글로 알립니다.** 소모품을 쓰면 카드가 바뀌거나 족보 레벨이 오르는데,
// 그 변화가 화면 여러 곳에 흩어져 있어서 무엇을 썼는지가 남지 않습니다.
//
// 여러 개가 동시에 뜰 수 있습니다 — 소모품 하나가 여러 가지를 하는 경우가 있고, 그것들이
// 서로를 덮으면 안 됩니다. 위에서부터 쌓이고 위의 것이 사라지면 아래가 올라옵니다.

import { Container, Graphics, Text } from 'pixi.js'

import { plate, FLOATING } from '../render/skin'
import { COLOR, SIZE } from '../render/theme'
import { richBlock, type RichStyle } from './rich'

/** 이 줄의 글에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 12, fill: 0xb4c4dc },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

const NEWLINE = String.fromCharCode(10)

const WIDTH = 380
/** 두 줄이 들어가는 최소 높이. 글이 길면 그만큼 자랍니다. */
const HEIGHT = 56
const GAP = 8
/** 첫 토스트의 자리. 조커 줄 아래이고 낸 카드 위입니다. */
const TOP = 268

interface Entry {
  box: Container
  life: number
  span: number
  /** 지금 몇 번째 자리에 있는가. 위의 것이 사라지면 줄어듭니다. */
  slot: number
  /** 이 하나의 높이. 글에 따라 다르므로 쌓을 때 앞의 것들을 더해 자리를 냅니다. */
  height: number
  /** 지금 그려지는 높이. 자리가 바뀌면 목표로 미끄러집니다. */
  shown: number
}

export class Toasts extends Container {
  private readonly live: Entry[] = []

  constructor() {
    super()
    this.eventMode = 'none'
    this.zIndex = 8_000
  }

  /**
   * 한 줄 띄웁니다.
   *
   * `note` 는 둘째 줄입니다 — 무엇을 썼는가와 그것이 무엇을 했는가를 갈라 적습니다.
   */
  push(title: string, note: string, tint: number, seconds = 2.6): void {
    const box = new Container()

    // **글에 맞춰 자랍니다.** 높이를 못박으면 긴 이름이 판 밖으로 나가고, 규칙 이름은
    // 「한 라운드에 같은 족보를 두 번 낼 수 없음」처럼 깁니다.
    const heading = new Text({
      text: title,
      style: {
        fontSize: 15, fill: COLOR.ink, fontWeight: '800', lineHeight: 21,
        wordWrap: true, wordWrapWidth: WIDTH - 44,
      },
    })
    heading.position.set(24, 9)

    // **수와 이름은 다른 색입니다.** 「8 → 10」 에서 사람이 보는 것은 그 둘입니다.
    const body = richBlock(note.split(NEWLINE), RICH, 17)
    body.position.set(24, 12 + heading.height)

    const height = Math.max(HEIGHT, body.y + body.height + 12)

    const board = new Graphics()
    plate(board, WIDTH, height, {
      ...FLOATING,
      top: 0x212b3a, bottom: 0x141b26, border: tint, radius: 12, weight: 2, gloss: 0.1,
    })

    // 왼쪽에 색 띠 하나. 무엇에 관한 것인지가 색으로 먼저 읽힙니다.
    const stripe = new Graphics()
    stripe.roundRect(8, 10, 5, height - 20, 3).fill(tint)

    box.addChild(board, stripe, heading, body)
    box.pivot.set(WIDTH / 2, 0)
    this.addChild(box)

    const slot = this.live.length
    // 처음 뜨는 것은 미끄러지지 않고 제자리에서 시작합니다.
    let shown = TOP
    for (const above of this.live) shown += above.height + GAP
    this.live.push({ box, life: seconds, span: seconds, slot, height, shown })
  }

  get busy(): boolean {
    return this.live.length > 0
  }

  advance(seconds: number): void {
    for (let i = this.live.length - 1; i >= 0; i--) {
      const entry = this.live[i]
      entry.life -= seconds

      if (entry.life <= 0) {
        entry.box.destroy()
        this.live.splice(i, 1)
        // 아래의 것들이 한 칸 올라옵니다.
        for (const rest of this.live) if (rest.slot > entry.slot) rest.slot--
        continue
      }

      const gone = 1 - entry.life / entry.span

      // 들어올 때 위에서 내려오며 커집니다.
      const enter = Math.min(1, gone / 0.12)
      const scale = enter < 1 ? 0.7 + 0.42 * enter : 1.12 - 0.12 * Math.min(1, (gone - 0.12) / 0.1)

      // 앞의 것들이 차지한 높이만큼 내려갑니다. **높이가 저마다 달라서 자리마다 셉니다.**
      let want = TOP
      for (const above of this.live) {
        if (above.slot < entry.slot) want += above.height + GAP
      }
      // 자리로 미끄러집니다. 위의 것이 사라져 자리가 바뀌어도 튀지 않습니다.
      entry.shown += (want - entry.shown) * Math.min(1, seconds * 12)

      entry.box.scale.set(scale)
      entry.box.position.set(SIZE.width / 2, entry.shown - (1 - enter) * 26)
      entry.box.alpha = Math.min(enter, Math.min(1, entry.life / 0.4))
    }
  }
}
