// 게임 방법.
//
// **규칙을 모르면 화면이 아무리 좋아도 게임이 아닙니다.** 첫 판에서 저절로 한 번 열리고,
// 그 뒤로는 왼쪽 아래 버튼으로 언제든 다시 엽니다.
//
// 내용은 손으로 적습니다 — 데이터에서 뽑을 수 있는 것은 족보 목록 쪽이고, 여기 있는 것은
// 「무엇을 하는 게임인가」라서 표에 없습니다.

import { Container, Graphics, Text } from 'pixi.js'

import { plate, FLOATING } from '../render/skin'
import { COLOR, SIZE } from '../render/theme'

interface Section {
  head: string
  lines: string[]
}

const LEFT_COLUMN: Section[] = [
  {
    head: '목표',
    lines: [
      '안테 8까지 블라인드를 차례로 격파합니다.',
      '블라인드마다 요구 점수가 있고, 정해진 핸드',
      '수 안에 그 점수를 넘겨야 합니다.',
      '넘기지 못하면 그 자리에서 런이 끝납니다.',
    ],
  },
  {
    head: '점수',
    lines: [
      '점수 = 칩 × 배수 입니다.',
      '낸 카드가 만드는 족보가 기본 칩과 배수를 정하고,',
      '득점한 카드의 숫자가 칩에 더해집니다',
      '(A 는 11, 그림은 10).',
    ],
  },
  {
    head: '한 라운드',
    lines: [
      '1.  패에서 최대 5장을 눌러 고릅니다.',
      '2.  「낸다」 로 점수를 냅니다.',
      '3.  패가 시원찮으면 「버린다」 — 버린 만큼',
      '     다시 뽑습니다. 버리기도 횟수가 있습니다.',
    ],
  },
]

const RIGHT_COLUMN: Section[] = [
  {
    head: '조커',
    lines: [
      '이 게임의 알맹이입니다. 상점에서 사고, 위쪽 줄에',
      '최대 5개까지 세웁니다.',
      '왼쪽부터 차례로 발동하므로 순서가 점수를 바꿉니다.',
      '마우스를 올리면 무엇을 하는지 보입니다.',
    ],
  },
  {
    head: '상점',
    lines: [
      '블라인드를 격파하면 열립니다.',
      '조커·소모품·바우처를 삽니다. 「리롤」 로 물건을',
      '바꿀 수 있습니다.',
      '돈은 격파 보상 + 남은 핸드 + 이자로 들어옵니다.',
    ],
  },
  {
    head: '조작',
    lines: [
      '카드를 누르면 고르고, 다시 누르면 풉니다.',
      '은은하게 물든 카드는 그것도 고르면 더 높은',
      '족보가 되는 카드입니다.',
      '「족보 목록」 은 어느 족보가 몇 점인지 보여줍니다.',
    ],
  },
]

const WIDTH = 940
const HEIGHT = 616

export class Guide extends Container {
  private readonly body = new Container()

  constructor(private readonly onClose: () => void) {
    super()
    this.visible = false
    this.zIndex = 9_000
    this.build()
  }

  open(): void {
    this.visible = true
  }

  close(): void {
    this.visible = false
  }

  private build(): void {
    // 뒤를 덮습니다. **덮지 않으면 뒤의 카드가 눌립니다.**
    const veil = new Graphics()
    veil.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
      .fill({ color: 0x070a10, alpha: 0.86 })
    veil.eventMode = 'static'
    veil.cursor = 'pointer'
    veil.on('pointertap', () => this.onClose())
    this.addChild(veil, this.body)

    const board = new Graphics()
    // 광택 띠를 거의 없앱니다 — 판이 크면 띠의 끝이 가로줄로 보입니다.
    plate(board, WIDTH, HEIGHT, { ...FLOATING, radius: 16, weight: 2.5, gloss: 0.06 })
    this.body.addChild(board)
    this.body.position.set((SIZE.width - WIDTH) / 2, (SIZE.height - HEIGHT) / 2)
    this.body.eventMode = 'static'
    this.body.cursor = 'pointer'
    this.body.on('pointertap', () => this.onClose())

    const title = new Text({
      text: '게임 방법',
      style: { fontSize: 28, fill: COLOR.ink, fontWeight: '800' },
    })
    title.anchor.set(0.5, 0)
    title.position.set(WIDTH / 2, 24)

    const lead = new Text({
      text: '포커 족보로 점수를 내고, 조커로 그 점수를 불립니다.',
      style: { fontSize: 15, fill: 0xb4c4dc, fontWeight: '700' },
    })
    lead.anchor.set(0.5, 0)
    lead.position.set(WIDTH / 2, 64)
    this.body.addChild(title, lead)

    this.column(LEFT_COLUMN, 44)
    this.column(RIGHT_COLUMN, WIDTH / 2 + 8)

    const close = new Text({
      text: '아무 곳이나 눌러 닫습니다',
      style: { fontSize: 13, fill: COLOR.inkDim, fontWeight: '700' },
    })
    close.anchor.set(0.5, 1)
    close.position.set(WIDTH / 2, HEIGHT - 16)
    this.body.addChild(close)
  }

  private column(sections: Section[], x: number): void {
    let y = 104
    for (const section of sections) {
      const rule = new Graphics()
      rule.roundRect(x, y + 6, 4, 17, 2).fill(COLOR.chips)

      const head = new Text({
        text: section.head,
        style: { fontSize: 18, fill: COLOR.ink, fontWeight: '800' },
      })
      head.position.set(x + 14, y)

      const text = new Text({
        text: section.lines.join('\n'),
        style: { fontSize: 14, fill: 0xd2dcea, lineHeight: 23 },
      })
      text.position.set(x + 14, y + 30)

      this.body.addChild(rule, head, text)
      y += 30 + section.lines.length * 23 + 20
    }
  }
}
