// 물어보는 판.
//
// **되돌릴 수 없는 것은 묻습니다.** 로그아웃과 계정 삭제가 그렇습니다 — 한 번 눌러서
// 일어나면, 잘못 누른 사람에게는 그것이 사고입니다.
//
// **판이 하나입니다.** 묻는 자리마다 판을 만들면 단추의 자리와 색이 조금씩 달라지고,
// 그러면 「예」가 어느 쪽인지를 매번 읽어야 합니다.
//
// **무엇이 걸리는지도 여기 적힙니다.** 「이 덱으로 시작할까요?」에 덱 이름만 있으면 그
// 덱이 무엇을 바꾸는지는 앞 화면으로 되돌아가 쪽지를 다시 띄워야 알 수 있습니다 —
// 누르기 직전의 자리이므로 그것이 여기 있어야 합니다. 판의 높이는 그만큼 자랍니다.

import { Container, Graphics, Text } from 'pixi.js'

import { t } from '../core/strings'
import { COLOR, UI } from '../render/theme'
import type { ToolSpot } from './layout'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { richBlock, rowsOf, type RichStyle } from './rich'
import { Button } from './widgets'

const WIDTH = 460
const BODY_Y = 74
const LINE = 19

/** 아래에 붙는 목록의 강조. **판 안의 쪽지와 같은 색입니다.** */
const NOTE_STYLE: RichStyle = {
  base: { fontSize: 13, fill: COLOR.ink },
  number: COLOR.money,
  term: UI.yellow,
}

export class ConfirmPanel implements ModalPanel {
  readonly view = new Container()
  readonly size: { width: number; height: number }

  /**
   * 도구가 짚을 자리 둘.
   *
   * **판의 높이가 내용에 따라 자랍니다.** 단추의 자리를 도구가 셈하면 물음에 목록이 붙은
   * 날부터 빈 곳을 누릅니다.
   */
  readonly toolSpots: [string, ToolSpot][] = []

  /**
   * @param title  머리 띠에 적힙니다
   * @param body   무엇이 일어나는가
   * @param yes    「예」 쪽의 글
   * @param danger 되돌릴 수 없는 것인가. 「예」가 붉어집니다
   * @param notes  무엇이 걸리는가. 있으면 글 아래에 제목 하나와 함께 붙습니다
   * @param notesHead 그 목록의 제목. 비우면 「시작 조건」입니다
   */
  constructor(title: string, body: string, yes: string, danger: boolean,
              private readonly onYes: () => void,
              private readonly onClose: () => void,
              notes: readonly string[] = [],
              notesHead = '') {
    const text = new Text({
      text: body,
      style: {
        fontSize: 14, fill: COLOR.ink, wordWrap: true, wordWrapWidth: WIDTH - 72,
        align: 'center', lineHeight: 21,
      },
    })
    text.anchor.set(0.5, 0)
    text.position.set(WIDTH / 2, BODY_Y)

    // **높이는 내용이 정합니다.** 못박아 두면 두 줄짜리 물음에서는 아래가 비고 목록이
    // 붙은 물음에서는 단추를 덮습니다.
    const block = notes.length > 0
      ? richBlock(notes.slice(), NOTE_STYLE, LINE, WIDTH - 72) : undefined
    const blockTop = BODY_Y + Math.ceil(text.height) + 20
    const blockH = block ? 24 + rowsOf(block) * LINE : 0
    const height = Math.max(208, blockTop + blockH + 22 + 44 + 30)
    this.size = { width: WIDTH, height }

    this.view.addChild(panelFrame(WIDTH, height, title, this.onClose, undefined, false))
    this.view.addChild(text)

    if (block) {
      const rule = new Graphics()
      rule.rect(36, blockTop, WIDTH - 72, 1).fill(UI.hairline)
      const head = new Text({
        text: notesHead === '' ? t('ui.setup.effects') : notesHead,
        style: { fontSize: 11, fill: COLOR.inkDim, fontWeight: '800', letterSpacing: 1 },
      })
      head.position.set(36, blockTop + 8)
      block.position.set(36, blockTop + 26)
      this.view.addChild(rule, head, block)
    }

    // **「아니오」가 왼쪽입니다.** 되돌리는 쪽이 손이 먼저 가는 자리에 있어야 합니다.
    const gap = 12
    const width = (WIDTH - 60 - gap) / 2
    const y = height - 30 - 44

    const no = new Button(t('ui.button.no'), width, 44, UI.btn,
                          () => this.onClose(), 16)
    no.position.set(30, y)

    const ok = new Button(yes, width, 44, danger ? UI.red : 0x2f8f52, () => {
      this.onClose()
      this.onYes()
    }, 16)
    ok.position.set(30 + width + gap, y)

    this.view.addChild(no, ok)
    this.toolSpots.push(['no', { node: no, cx: width / 2, cy: 22 }],
                        ['yes', { node: ok, cx: width / 2, cy: 22 }])
  }
}
