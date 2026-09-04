// 게임 방법.
//
// **규칙을 모르면 화면이 아무리 좋아도 게임이 아닙니다.** 첫 판에서 저절로 한 번 열리고,
// 그 뒤로는 왼쪽 아래 버튼으로 언제든 다시 엽니다.
//
// 내용은 손으로 적습니다 — 데이터에서 뽑을 수 있는 것은 족보 목록 쪽이고, 여기 있는 것은
// 「무엇을 하는 게임인가」라서 표에 없습니다.

import { Container, Graphics, Text } from 'pixi.js'

import { COLOR, UI } from '../render/theme'
import { FOOTER_BAR, panelFrame, TITLE_BAR, type ModalPanel } from './modal'
import { richBlock, type RichStyle } from './rich'
import { t } from '../core/strings'
import { Button } from './widgets'

/**
 * 마디 하나.
 *
 * **글은 줄로 쪼개 두지 않습니다.** 줄바꿈을 손으로 넣으면 그 자리가 한국어의 길이에만
 * 맞고, 다른 말로 바꾸면 어긋납니다 — 접는 것은 그리는 쪽이 합니다.
 */
interface Section {
  head: string
  body: string
}

const LEFT_KEYS = ['goal', 'score', 'round'] as const
const RIGHT_KEYS = ['joker', 'shop', 'controls'] as const

function sectionsOf(keys: readonly string[]): Section[] {
  return keys.map(key => ({
    head: t(`ui.guide.${key}.head`),
    body: t(`ui.guide.${key}.body`),
  }))
}

/** 이 판의 글에 붙는 강조. */
const RICH: RichStyle = {
  base: { fontSize: 14, fill: COLOR.ink },
  number: COLOR.accentNumber,
  term: COLOR.accentTerm,
}

const WIDTH = 940
/** 판의 가장 낮은 높이. 글이 길면 그만큼 자랍니다. */
const MIN_HEIGHT = 616 + FOOTER_BAR - 40

/**
 * 게임 방법.
 *
 * **뒤를 덮는 것도 가운데에 놓는 것도 이 판이 하지 않습니다** — `Modals` 가 맡습니다.
 * 판이 저마다 자기를 띄우면 규칙이 저마다 달라집니다.
 */
export class Guide implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: MIN_HEIGHT }

  /** 지금 판의 높이. 글이 정합니다. */
  private set height(value: number) {
    ;(this.size as { width: number; height: number }).height = value
  }

  private get height(): number {
    return this.size.height
  }

  private readonly body = this.view

  constructor(private readonly onClose: () => void,
              private readonly onHandList: () => void) {
    this.build()
  }

  /** 글을 다시 읽습니다. **말이 바뀌면 이 판도 바뀌어야 합니다.** */
  relabel(): void {
    this.view.removeChildren().forEach(child => child.destroy())
    this.build()
  }

  private build(): void {
    // **글을 먼저 세우고 판을 나중에 그립니다.** 판의 높이를 글이 정하므로, 글이 얼마나
    // 되는지 알기 전에는 판을 그릴 수 없습니다 — 못박아 두면 말을 바꿨을 때(독일어가
    // 한국어보다 깁니다) 마지막 마디가 판 아래로 넘칩니다.
    const content = new Container()
    const left = this.column(content, sectionsOf(LEFT_KEYS), 44)
    const right = this.column(content, sectionsOf(RIGHT_KEYS), WIDTH / 2 + 8)
    this.height = Math.max(MIN_HEIGHT, Math.max(left, right) + 16 + FOOTER_BAR)

    // **판 위를 누르는 것으로는 닫히지 않습니다.** 닫는 것은 바깥이거나 `Esc` 입니다.
    // 족보 목록은 이 판에서 바로 열립니다. **판 위에 판이 얹힙니다** — 닫으면 이 판으로
    // 돌아오므로, 규칙을 읽다 말고 처음부터 다시 찾아 들어갈 일이 없습니다.
    const hands = new Button(t('ui.button.hand_list_open'), 168, 34, UI.slate,
      () => this.onHandList())
    this.body.addChild(
      panelFrame(WIDTH, this.height, t('ui.button.guide'), () => this.onClose(), hands))

    const lead = new Text({
      text: t('ui.guide.lead'),
      style: { fontSize: 15, fill: COLOR.ink, fontWeight: '700' },
    })
    lead.anchor.set(0.5, 0)
    lead.position.set(WIDTH / 2, TITLE_BAR + 18)
    this.body.addChild(lead, content)
  }

  /** 한 단을 세우고, 그 단이 쓴 높이를 돌려줍니다. */
  private column(into: Container, sections: Section[], x: number): number {
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
      // **단이 둘이라 좁습니다.** 왼쪽 단의 글이 오른쪽 단의 글머리(`WIDTH / 2 + 8` 에서
      // 다시 14) 앞에서 끝나야 하므로, 반쪽 넓이에서 두 단의 안쪽 여백을 다 뺍니다.
      const text = richBlock([section.body], RICH, 23, WIDTH / 2 - 72)
      text.position.set(x + 14, y + 30)

      into.addChild(rule, head, text)
      // 접힌 줄만큼 아래가 밀립니다. 줄 수로 세면 긴 줄 하나가 다음 자리를 덮습니다.
      y += 30 + text.height + 20
    }
    return y
  }
}
