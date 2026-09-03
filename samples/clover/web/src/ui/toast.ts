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

/**
 * 토스트의 넓이.
 *
 * **좁습니다.** 판 한가운데에 넓게 서면 둘째 것부터 낸 카드를 덮고, 그러면 읽는 것이 아니라
 * 사라지기를 기다리게 됩니다. 오른쪽 빈 자리에 세우려면 그만큼 좁아야 합니다.
 */
const WIDTH = 236
/** 두 줄이 들어가는 최소 높이. 글이 길면 그만큼 자랍니다. */
const HEIGHT = 52
const GAP = 8
/** 첫 토스트의 자리. **낸 카드의 오른쪽입니다.** */
const TOP = 214
/**
 * 한 번에 서는 수.
 *
 * **넷째부터는 읽을 수 없습니다.** 앞의 것이 아직 있는데 새것이 오면 앞의 것을 서둘러
 * 보냅니다 — 쌓아 두면 어느 것이 방금 온 것인지 알 수 없습니다.
 */
const STACK = 3

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

  /**
   * 줄이 서는 자리의 가운데.
   *
   * **판의 한가운데가 아닙니다.** 가운데에 세우면 낸 카드를 덮고, 그러면 읽는 것이 아니라
   * 사라지기를 기다리게 됩니다 — 낸 카드는 오른쪽으로 x 1030 에서 끝나고 그 오른쪽은
   * 덱까지 비어 있습니다.
   */
  constructor(private centerX: number = SIZE.width - 10 - WIDTH / 2) {
    super()
    this.eventMode = 'none'
    this.zIndex = 8_000
  }

  /**
   * 줄이 서는 자리를 옮깁니다.
   *
   * **판 밖에서는 가운데입니다.** 오른쪽으로 붙여 둔 것은 낸 카드를 덮지 않으려는
   * 것이므로, 카드가 없는 화면에서까지 구석에 붙어 있으면 그것은 그냥 구석입니다 —
   * 타이틀과 로그인 화면에서 알림이 오른쪽 끝에 나던 것이 그 때문입니다.
   */
  setCenter(x: number): void {
    this.centerX = x
  }

  /** 판 안에서의 자리. 낸 카드의 오른쪽입니다. */
  static readonly IN_RUN = SIZE.width - 10 - WIDTH / 2
  /** 판 밖에서의 자리. */
  static readonly OUT_RUN = SIZE.width / 2

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
        wordWrap: true, wordWrapWidth: WIDTH - 40, breakWords: true,
      },
    })
    heading.position.set(20, 8)

    // **수와 이름은 다른 색입니다.** 「8 → 10」 에서 사람이 보는 것은 그 둘입니다.
    const body = richBlock(note.split(NEWLINE), RICH, 15, WIDTH - 30)
    body.position.set(20, 10 + heading.height)

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

    // **셋까지만 섭니다.** 넘치면 가장 오래된 것을 서둘러 보냅니다.
    for (let i = 0; i < this.live.length - (STACK - 1); i++) {
      this.live[i].life = Math.min(this.live[i].life, 0.22)
    }

    const slot = this.live.length
    // 처음 뜨는 것은 미끄러지지 않고 제자리에서 시작합니다.
    let shown = TOP
    for (const above of this.live) shown += above.height + GAP
    this.live.push({ box, life: seconds, span: seconds, slot, height, shown })
  }

  get busy(): boolean {
    return this.live.length > 0
  }

  /** 떠 있는 줄을 전부 지웁니다. 판이 없어질 때뿐입니다. */
  clear(): void {
    for (const entry of this.live) entry.box.destroy()
    this.live.length = 0
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
      // 오른쪽에서 미끄러져 들어옵니다. 위에서 내려오면 조커 줄을 가로지릅니다.
      entry.box.position.set(this.centerX + (1 - enter) * 30, entry.shown)
      entry.box.alpha = Math.min(enter, Math.min(1, entry.life / 0.4))
    }
  }
}
