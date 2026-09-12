// 게임 방법.
//
// **규칙을 모르면 화면이 아무리 좋아도 게임이 아닙니다.** 첫 판에서 저절로 한 번 열리고,
// 그 뒤로는 왼쪽 아래 버튼으로 언제든 다시 엽니다.
//
// **전면 화면입니다.** 갈래 여덟이 왼쪽에 세로로 서고 오른쪽이 그 갈래의 본문입니다 —
// 두 단으로 늘어놓았던 동안은 여섯 마디가 한 화면에 밀어 넣어져 글자가 작아졌고, 어느
// 것을 읽고 있는지가 화면에 남지 않았습니다. 규격은 `doc/ui/language.md` 와 캔버스의
// `Guide` 아트보드입니다.
//
// 내용은 손으로 적습니다 — 데이터에서 뽑을 수 있는 것은 족보 목록 쪽이고, 여기 있는 것은
// 「무엇을 하는 게임인가」라서 표에 없습니다.

import { Container, Graphics, Rectangle, Text } from 'pixi.js'

import { t } from '../core/strings'
import { leading, SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { wellTint } from '../render/skin'
import { piece } from './chrome'
import { FULL_BODY_TOP, FULL_EDGE, fullFrame, type ModalPanel } from './modal'
import { sectionHead } from './parts'
import { richBlock, richLeading, richStyle, type RichStyle } from './rich'
import { Button } from './widgets'

/**
 * 갈래 여덟. **차례가 곧 읽는 차례입니다** — 한 판이 어떻게 도는지부터이고, 그다음이 그
 * 판을 바꾸는 것들이며, 마지막이 어디서 더 보는가입니다.
 */
const TOPICS = ['round', 'score', 'joker', 'item', 'shop', 'goal', 'deck', 'controls'] as const

/** 득점의 차례를 적는 칸 넷. 족보 → 카드 → 조커 → 곱셈입니다. */
const ORDER = ['hand', 'card', 'joker', 'mult'] as const

/**
 * 이 판의 글에 붙는 강조.
 *
 * **읽는 화면이므로 계단 한 칸 위입니다.** 12픽셀로 두었더니 핸드폰에서 읽히지 않았고,
 * 이 자리는 한 갈래만 읽는 자리라 줄 수를 아낄 이유가 없습니다.
 */
const rich = (): RichStyle => richStyle('title')

/** 왼쪽 갈래 목록. */
const NAV_W = 320
const NAV_H = 63
/** 고른 갈래 밑에 깔리는 판. 줄 사이보다 5픽셀 좁습니다. */
const NAV_PLATE_H = 58
const NAV_TOP = FULL_BODY_TOP + 4

/** 오른쪽 본문. */
const BODY_X = FULL_EDGE + NAV_W + 48
const BODY_W = SIZE.width - FULL_EDGE - BODY_X

/** 보기 판 — 칩 × 배수 = 점수. */
const SHOW_Y = FULL_BODY_TOP + 112
const SHOW_H = 118

/** 차례 칸 넷. */
const CELL_GAP = 14
const CELL_H = 60
const CELL_Y = SHOW_Y + SHOW_H + 60

/** 보기 판에 적는 수. **데이터가 아니라 예시입니다** — 어느 판에서나 같은 수입니다. */
const SHOW_CHIPS = 340
const SHOW_MULT = 12

/**
 * 게임 방법.
 *
 * **뒤를 덮는 것도 가운데에 놓는 것도 이 판이 하지 않습니다** — `Modals` 가 맡습니다.
 * 판이 저마다 자기를 띄우면 규칙이 저마다 달라집니다.
 */
export class Guide implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: SIZE.width, height: SIZE.height }
  readonly fullscreen = true

  /** 지금 펼친 갈래. */
  private at = 0

  /** 도구가 짚는 자리들. **좌표를 적어 두면 말을 바꾼 판에서 빈자리를 누릅니다.** */
  readonly spotNodes = new Map<string, { node: Container; cx: number; cy: number }>()

  constructor(private readonly onClose: () => void,
              private readonly onHandList: () => void) {
    this.build()
  }

  /** 글을 다시 읽습니다. **말이 바뀌면 이 판도 바뀌어야 합니다.** */
  relabel(): void {
    this.build()
  }

  /** 다시 열 때는 첫 갈래부터입니다. */
  reopen(): void {
    this.at = 0
    this.build()
  }

  private build(): void {
    this.view.removeChildren().forEach(child => child.destroy({ children: true }))
    this.spotNodes.clear()

    const key = TOPICS[this.at]

    // 오른쪽 위 — 몇 번째 갈래인가와 이 화면의 이름.
    const right = new Container()
    const count = new Text({
      text: `${this.at + 1} / ${TOPICS.length}`,
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    count.anchor.set(1, 0)
    right.addChild(count)
    const name = new Text({
      text: t('ui.button.guide'),
      style: { fontSize: TEXT.base, fill: UI.money, fontWeight: WEIGHT.bold },
    })
    name.anchor.set(1, 0)
    name.position.set(0, 26)
    right.addChild(name)

    // 아래 띠에는 나아가는 단추 하나뿐입니다. **글을 두지 않습니다.**
    const foot = new Container()
    const next = new Button(t('ui.guide.next'), 240, 60, 'primary', () => {
      this.at = (this.at + 1) % TOPICS.length
      this.build()
    })
    next.position.set(SIZE.width - FULL_EDGE * 2 - 240, 21)
    foot.addChild(next)
    this.spotNodes.set('guide:next', { node: next, cx: 120, cy: 30 })

    this.view.addChild(fullFrame(t(`ui.guide.${key}.head`), [t('ui.button.guide')],
      () => this.onClose(), right, foot))

    this.drawNav()
    this.drawBody(key)
  }

  /** 왼쪽 갈래 목록. 고른 것은 판 위에 서고 왼쪽 변에 값의 색 한 줄이 붙습니다. */
  private drawNav(): void {
    TOPICS.forEach((key, index) => {
      const top = NAV_TOP + index * NAV_H
      const row = new Container()
      row.position.set(FULL_EDGE, top)

      if (index === this.at) {
        // **판의 색입니다.** 전면 화면의 바닥은 덮개(거의 검정)이므로 칸의 색으로
        // 두면 바닥과 같아 보이지 않습니다 — 칸은 바닥보다 밝아야 칸입니다.
        const skin = piece('well', NAV_W, NAV_PLATE_H, wellTint(UI.panel))
        if (skin !== undefined) row.addChild(skin)
        else {
          const plate = new Graphics()
          plate.rect(0, 0, NAV_W, NAV_PLATE_H).fill({ color: UI.panel, alpha: 0.9 })
          row.addChild(plate)
        }
        const bar = new Graphics()
        bar.rect(0, 0, 4, NAV_PLATE_H).fill(UI.money)
        row.addChild(bar)
      }

      const label = new Text({
        text: t(`ui.guide.${key}.head`),
        style: {
          fontSize: TEXT.base,
          fill: index === this.at ? UI.ink : UI.inkDim,
          fontWeight: index === this.at ? WEIGHT.bold : WEIGHT.normal,
        },
      })
      label.anchor.set(0, 0.5)
      label.position.set(20, NAV_PLATE_H / 2)
      row.addChild(label)

      row.eventMode = 'static'
      row.cursor = 'pointer'
      row.hitArea = new Rectangle(0, 0, NAV_W, NAV_PLATE_H)
      row.on('pointertap', () => {
        if (this.at === index) return
        this.at = index
        this.build()
      })
      this.spotNodes.set(`guide:${key}`, { node: row, cx: NAV_W / 2, cy: NAV_PLATE_H / 2 })
      this.view.addChild(row)
    })
  }

  /** 오른쪽 본문. 갈래 하나의 글과, 점수 갈래에만 붙는 보기 판입니다. */
  private drawBody(key: string): void {
    // **줄 사이가 넉넉합니다.** 이 자리는 한 화면에 여러 마디를 밀어 넣는 자리가 아니라
    // 한 갈래만 읽는 자리이므로, 12픽셀 글에 24픽셀 계단의 줄 사이를 씁니다.
    // **줄 사이는 글의 크기에서 옵니다.** 24픽셀 글에 12픽셀 계단의 줄 사이를 걸었더니
    // 세 줄이 서로 겹쳤습니다 — 같은 계단의 줄 사이에 한 칸을 더 둡니다.
    const text = richBlock(bodyLines(t(`ui.guide.${key}.body`)), rich(),
                           richLeading('title') + 8, BODY_W)
    text.position.set(BODY_X, FULL_BODY_TOP + 10)
    this.view.addChild(text)

    if (key !== 'score') {
      // 족보 목록은 「정보」 갈래에서 엽니다. **어느 갈래에나 두지 않습니다** — 여덟 곳에
      // 같은 단추가 서면 그것이 이 화면의 단추로 읽히고, 갈래를 넘길 때마다 자리만
      // 지킵니다. 어디서 더 보는가를 적는 갈래가 그 자리입니다.
      if (key === 'controls') {
        const open = new Button(t('ui.button.hand_list_open'), 240, 48, 'neutral',
          () => this.onHandList())
        open.position.set(BODY_X, FULL_BODY_TOP + 10 + text.height + 28)
        this.view.addChild(open)
        this.spotNodes.set('guide:hands', { node: open, cx: 120, cy: 24 })
      }
      return
    }

    // 보기 판 — 칩 × 배수 = 점수.
    const show = new Container()
    show.position.set(BODY_X, SHOW_Y)
    const skin = piece('well', BODY_W, SHOW_H, wellTint(UI.panel))
    if (skin !== undefined) show.addChild(skin)
    else {
      const plate = new Graphics()
      plate.rect(0, 0, BODY_W, SHOW_H).fill({ color: UI.panel, alpha: 0.9 })
      show.addChild(plate)
    }

    const parts: { text: string; ink: number }[] = [
      { text: String(SHOW_CHIPS), ink: UI.chips },
      { text: TIMES, ink: UI.inkFaint },
      { text: String(SHOW_MULT), ink: UI.mult },
      { text: EQUALS, ink: UI.inkFaint },
      { text: (SHOW_CHIPS * SHOW_MULT).toLocaleString('en-US'), ink: UI.ink },
    ]
    const nodes = parts.map(part => new Text({
      text: part.text,
      style: { fontSize: TEXT.display, fill: part.ink, fontWeight: WEIGHT.bold },
    }))
    const gap = 22
    const span = nodes.reduce((sum, one) => sum + one.width, 0) + gap * (nodes.length - 1)
    let x = (BODY_W - span) / 2
    for (const node of nodes) {
      node.anchor.set(0, 0.5)
      node.position.set(x, SHOW_H / 2)
      show.addChild(node)
      x += node.width + gap
    }
    this.view.addChild(show)

    // 득점의 차례 넷.
    const head = sectionHead(BODY_W, t('ui.guide.order.head'), undefined, false)
    head.position.set(BODY_X, CELL_Y - 34)
    this.view.addChild(head)

    const cellW = (BODY_W - CELL_GAP * (ORDER.length - 1)) / ORDER.length
    ORDER.forEach((one, index) => {
      const cell = new Container()
      cell.position.set(BODY_X + index * (cellW + CELL_GAP), CELL_Y)
      const face = piece('well', cellW, CELL_H, wellTint(UI.panel))
      if (face !== undefined) cell.addChild(face)
      else {
        const plate = new Graphics()
        plate.rect(0, 0, cellW, CELL_H).fill({ color: UI.panel, alpha: 0.9 })
        cell.addChild(plate)
      }
      const label = new Text({
        text: t(`ui.guide.order.${one}`),
        style: { fontSize: TEXT.base, fill: UI.ink, fontWeight: WEIGHT.bold },
      })
      label.anchor.set(0.5, 0.5)
      label.position.set(cellW / 2, CELL_H / 2 + Math.round(leading(TEXT.base) * 0.06))
      cell.addChild(label)
      this.view.addChild(cell)
    })
  }
}

/**
 * 본문을 줄로 가릅니다.
 *
 * **번호가 붙은 글은 번호마다 한 줄입니다.** 한 문단으로 흘리면 「1.」 과 「2.」 가 줄
 * 가운데에서 만나 목록으로 읽히지 않고, 접히는 자리가 말마다 달라 어느 말에서는 번호가
 * 앞 줄의 끝에 붙습니다. 번호가 없는 글은 그대로 한 줄입니다.
 */
function bodyLines(body: string): string[] {
  const parts = body.split(/\s+(?=\d+\.\s)/).map(one => one.trim()).filter(one => one !== '')
  return parts.length > 1 ? parts : [body]
}

/** 곱셈과 등호. **글 표에 두지 않습니다** — 어느 말에서나 같은 기호입니다. */
const TIMES = String.fromCharCode(0xd7)
const EQUALS = '='
