// 게임 방법.
//
// **규칙을 모르면 화면이 아무리 좋아도 게임이 아닙니다.** 첫 판에서 저절로 한 번 열리고,
// 그 뒤로는 왼쪽 아래 버튼으로 언제든 다시 엽니다.
//
// 내용은 손으로 적습니다 — 데이터에서 뽑을 수 있는 것은 족보 목록 쪽이고, 여기 있는 것은
// 「무엇을 하는 게임인가」라서 표에 없습니다.

import { Container, Graphics, Text } from 'pixi.js'

import { COLOR } from '../render/theme'
import { FOOTER_BAR, panelFrame, TITLE_BAR, type ModalPanel } from './modal'
import { richBlock, type RichStyle } from './rich'
import { Button } from './widgets'

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

/** 이 판의 글에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 14, fill: 0xd2dcea },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

const WIDTH = 940
const HEIGHT = 616 + FOOTER_BAR - 40

/**
 * 게임 방법.
 *
 * **뒤를 덮는 것도 가운데에 놓는 것도 이 판이 하지 않습니다** — `Modals` 가 맡습니다.
 * 판이 저마다 자기를 띄우면 규칙이 저마다 달라집니다.
 */
export class Guide implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = this.view

  constructor(private readonly onClose: () => void,
              private readonly onHandList: () => void) {
    this.build()
  }

  private build(): void {
    // **판 위를 누르는 것으로는 닫히지 않습니다.** 닫는 것은 바깥이거나 `Esc` 입니다.
    // 족보 목록은 이 판에서 바로 열립니다. **판 위에 판이 얹힙니다** — 닫으면 이 판으로
    // 돌아오므로, 규칙을 읽다 말고 처음부터 다시 찾아 들어갈 일이 없습니다.
    const hands = new Button('족보 목록 보기', 168, 34, 0x3a4658, () => this.onHandList())
    this.body.addChild(
      panelFrame(WIDTH, HEIGHT, '게임 방법', () => this.onClose(), hands))

    const lead = new Text({
      text: '포커 족보로 점수를 내고, 조커로 그 점수를 불립니다.',
      style: { fontSize: 15, fill: 0xb4c4dc, fontWeight: '700' },
    })
    lead.anchor.set(0.5, 0)
    lead.position.set(WIDTH / 2, TITLE_BAR + 18)
    this.body.addChild(lead)

    this.column(LEFT_COLUMN, 44)
    this.column(RIGHT_COLUMN, WIDTH / 2 + 8)


  }

  private column(sections: Section[], x: number): void {
    let y = TITLE_BAR + 58
    for (const section of sections) {
      const rule = new Graphics()
      rule.roundRect(x, y + 6, 4, 17, 2).fill(COLOR.chips)

      const head = new Text({
        text: section.head,
        style: { fontSize: 18, fill: COLOR.ink, fontWeight: '800' },
      })
      head.position.set(x + 14, y)

      // **수와 이름은 다른 색입니다.** 「안테 8까지」에서 찾는 것은 8 이고, 그것이 문장과
      // 같은 색이면 문장을 처음부터 읽어야 찾습니다.
      const text = richBlock(section.lines, RICH, 23)
      text.position.set(x + 14, y + 30)

      this.body.addChild(rule, head, text)
      y += 30 + section.lines.length * 23 + 20
    }
  }
}
