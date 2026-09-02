// 물어보는 판.
//
// **되돌릴 수 없는 것은 묻습니다.** 로그아웃과 계정 삭제가 그렇습니다 — 한 번 눌러서
// 일어나면, 잘못 누른 사람에게는 그것이 사고입니다.
//
// **판이 하나입니다.** 묻는 자리마다 판을 만들면 단추의 자리와 색이 조금씩 달라지고,
// 그러면 「예」가 어느 쪽인지를 매번 읽어야 합니다.

import { Container, Text } from 'pixi.js'

import { t } from '../core/strings'
import { COLOR } from '../render/theme'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { Button } from './widgets'

const WIDTH = 460
const HEIGHT = 208

export class ConfirmPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  /**
   * @param title  머리 띠에 적힙니다
   * @param body   무엇이 일어나는가
   * @param yes    「예」 쪽의 글
   * @param danger 되돌릴 수 없는 것인가. 「예」가 붉어집니다
   */
  constructor(title: string, body: string, yes: string, danger: boolean,
              private readonly onYes: () => void,
              private readonly onClose: () => void) {
    this.view.addChild(panelFrame(WIDTH, HEIGHT, title, this.onClose, undefined, false))

    const text = new Text({
      text: body,
      style: {
        fontSize: 14, fill: COLOR.ink, wordWrap: true, wordWrapWidth: WIDTH - 72,
        align: 'center', lineHeight: 21,
      },
    })
    text.anchor.set(0.5, 0)
    text.position.set(WIDTH / 2, 74)
    this.view.addChild(text)

    // **「아니오」가 왼쪽입니다.** 되돌리는 쪽이 손이 먼저 가는 자리에 있어야 합니다.
    const gap = 12
    const width = (WIDTH - 60 - gap) / 2
    const y = HEIGHT - 30 - 44

    const no = new Button(t('ui.button.no'), width, 44, 0x39424f,
                          () => this.onClose(), 16)
    no.position.set(30, y)

    const ok = new Button(yes, width, 44, danger ? 0xa63f3f : 0x2f8f52, () => {
      this.onClose()
      this.onYes()
    }, 16)
    ok.position.set(30 + width + gap, y)

    this.view.addChild(no, ok)
  }
}
