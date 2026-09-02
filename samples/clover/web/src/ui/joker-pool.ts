// 조커 풀 고르기.
//
// **고르기 전에 무엇이 들어 있는지 볼 수 있어야 합니다.** 「기본 150종」과 「확장 500종」이
// 라는 글만 두면 무엇이 늘어나는지 알 수 없고, 그러면 고르는 일이 찍는 일이 됩니다 — 그래서
// 이 판은 고른 풀의 조커를 카드로 늘어놓고, 하나를 누르면 이름과 희귀도와 값과 효과를 옆에
// 적습니다.
//
// **카드는 게임에서 쓰는 것과 같은 것입니다.** `JokerView` 를 그대로 세우므로 그림도
// 희귀도 테두리도 에디션도 판에서 보이는 것과 같습니다 — 여기서만 쓰는 그림을 따로 두면
// 그림이 들어왔을 때 한 곳이 남습니다.
//
// 효과 글은 `describe()` 가 효과 행에서 만듭니다. **설명문을 손으로 적어 둔 데이터가
// 없으므로** 이 판이 데이터와 어긋날 자리가 없습니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { poolsOf, type PoolChoice } from '../core/pool'
import { describe } from '../core/describe'
import { nameOf, t, tf } from '../core/strings'
import { onArtReady } from '../render/art'
import { JokerView } from '../render/joker-view'
import { COLOR, SIZE } from '../render/theme'
import { newCounters, type JokerInstance } from '../core/state'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { Button } from './widgets'

const WIDTH = 1180
const HEIGHT = 744

/**
 * 격자. 카드가 88 × 124 이고 이름이 그 아래에 들어가므로 세로 간격이 146 입니다.
 *
 * **쪽 단추는 아래가 아니라 머리에 있습니다.** 아래에 두었다가 마지막 줄의 카드 위에
 * 얹혔습니다 — 격자가 판을 가득 쓰면 아랫단에 둘 자리가 없습니다.
 */
const COLUMNS = 8
const ROWS = 4
const PER_PAGE = COLUMNS * ROWS
const CELL_X = 104
const CELL_Y = 146
const GRID_X = 34
const GRID_Y = 132

/** 오른쪽의 설명 자리. */
const SIDE_X = GRID_X + COLUMNS * CELL_X + 18
const SIDE_W = WIDTH - SIDE_X - 30
const SIDE_H = HEIGHT - GRID_Y - 26

const RARITY_KEYS = ['', 'ui.rarity.common', 'ui.rarity.uncommon',
                     'ui.rarity.rare', 'ui.rarity.legendary']

export class JokerPoolPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  private readonly grid = new Container()
  private readonly side = new Container()
  private readonly sideBoard = new Graphics()

  private readonly heading = new Text({
    text: '', style: { fontSize: 15, fill: COLOR.inkDim, fontWeight: '700' },
  })
  private readonly pageLabel = new Text({
    text: '', style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly sideName = new Text({
    text: '', style: { fontSize: 22, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly sideMeta = new Text({
    text: '', style: { fontSize: 14, fill: COLOR.inkDim, fontWeight: '700' },
  })
  private readonly sideLines = new Text({
    text: '',
    style: {
      fontSize: 15, fill: COLOR.ink, fontWeight: '600', lineHeight: 22,
      wordWrap: true, wordWrapWidth: SIDE_W - 32, breakWords: true,
    },
  })
  private readonly sideHint = new Text({
    text: '', style: { fontSize: 13, fill: COLOR.inkDim, lineHeight: 18 },
  })

  private readonly poolButtons: { choice: PoolChoice; button: Button; key: string }[] = []
  private prev?: Button
  private next?: Button

  private views: JokerView[] = []
  private page = 0
  private picked = ''
  /** 다음 프레임에 다시 세워야 하는가. 그림이 들어오면 켜집니다. */
  private dirty = false

  /** 풀이 바뀌었을 때. 화면이 받아 적고 다음 판에 씁니다. */
  onPick?: (choice: PoolChoice) => void

  constructor(private readonly data: Data,
              private choice: PoolChoice,
              private readonly onClose: () => void) {
    this.build()
    this.rebuild()

    // **그림은 늦게 들어옵니다.** `artFor()` 가 처음엔 `undefined` 를 내고 그때 카드는
    // 문양을 그립니다 — 다 읽힌 뒤에 다시 세우지 않으면 이 판은 언제나 문양입니다.
    //
    // **그 자리에서 세우지 않고 표시만 남깁니다.** 그림 하나마다 부르므로 한 쑥을 열면
    // 32번이 오고, 그때마다 카드 32장을 버리고 다시 만들면 한 프레임에 1,024장입니다.
    onArtReady(() => { this.dirty = true })
  }

  private build(): void {
    const frame = panelFrame(WIDTH, HEIGHT, t('ui.pool.title'), this.onClose,
                             undefined, false)
    this.view.addChild(frame, this.body)

    // 풀 단추 둘. **나란히 둡니다** — 하나를 고르는 일이므로 목록이 아니라 두 갈래입니다.
    const bw = 250
    const bh = 54
    for (const [index, choice] of (['base', 'all'] as PoolChoice[]).entries()) {
      const key = choice === 'all' ? 'ui.pool.all' : 'ui.pool.base'
      const button = new Button(t(key), bw, bh, 0x2f5f8f, () => this.choose(choice), 18)
      button.position.set(GRID_X + index * (bw + 14), 62)
      this.poolButtons.push({ choice, button, key })
      this.body.addChild(button)
    }

    this.heading.position.set(GRID_X + 2 * (bw + 14) + 12, 80)
    this.body.addChild(this.heading)

    this.body.addChild(this.grid)

    // 오른쪽 설명 자리.
    this.sideBoard.roundRect(0, 0, SIDE_W, SIDE_H, 12)
      .fill({ color: 0x1a2334, alpha: 0.85 })
      .stroke({ color: COLOR.panelEdge, width: 1.5, alpha: 0.8 })
    this.side.position.set(SIDE_X, GRID_Y - 4)
    this.sideName.position.set(16, 16)
    this.sideMeta.position.set(16, 46)
    this.sideLines.position.set(16, 78)
    this.sideHint.position.set(16, 16)
    this.sideHint.style.wordWrap = true
    this.sideHint.style.wordWrapWidth = SIDE_W - 32
    this.side.addChild(this.sideBoard, this.sideName, this.sideMeta, this.sideLines,
                       this.sideHint)
    this.body.addChild(this.side)

    // 쪽 넘김.
    const headY = 62
    this.prev = new Button('◀', 56, 54, 0x2a3446, () => this.turn(-1), 20)
    this.prev.position.set(WIDTH - 30 - 56 * 2 - 104, headY)
    this.pageLabel.anchor.set(0.5, 0.5)
    this.pageLabel.position.set(WIDTH - 30 - 56 - 52, headY + 27)
    this.next = new Button('▶', 56, 54, 0x2a3446, () => this.turn(1), 20)
    this.next.position.set(WIDTH - 30 - 56, headY)
    this.body.addChild(this.prev, this.next, this.pageLabel)
  }

  /** 지금 풀의 조커들. 표의 순서가 곧 수집 목록의 순서입니다. */
  private rows() {
    const pools = poolsOf(this.choice)
    return this.data.tables.joker.records.filter(row => pools.includes(row.pool))
  }

  private choose(choice: PoolChoice): void {
    if (this.choice === choice) return
    this.choice = choice
    this.page = 0
    this.picked = ''
    this.onPick?.(choice)
    this.rebuild()
  }

  private turn(by: number): void {
    const pages = Math.max(1, Math.ceil(this.rows().length / PER_PAGE))
    this.page = (this.page + by + pages) % pages
    this.rebuild()
  }

  /** 카드와 글을 지금 상태로 다시 세웁니다. */
  private rebuild(): void {
    for (const view of this.views) view.destroy()
    this.views = []
    this.grid.removeChildren()

    const all = this.rows()
    const pages = Math.max(1, Math.ceil(all.length / PER_PAGE))
    this.page = Math.min(this.page, pages - 1)
    const shown = all.slice(this.page * PER_PAGE, (this.page + 1) * PER_PAGE)

    for (const one of this.poolButtons) {
      one.button.text = t(one.key)
      // 고른 것이 밝습니다. **테두리가 아니라 밝기로 표시합니다** — 테두리는 희귀도가
      // 이미 쓰고 있습니다.
      one.button.alpha = one.choice === this.choice ? 1 : 0.55
    }
    this.heading.text = tf('ui.pool.count', { count: all.length })

    shown.forEach((row, index) => {
      const joker: JokerInstance = {
        uid: index, jokerId: row.jokerId,
        edition: 0 as JokerInstance['edition'],
        sticker: 0 as JokerInstance['sticker'],
        counters: newCounters(), age: 0, disabled: false,
      }
      const view = new JokerView(joker, {
        name: nameOf(this.data, 'joker', row.jokerId, row.name),
        rarity: row.rarity,
        lines: [],
      })
      view.motion.snap(GRID_X + (index % COLUMNS) * CELL_X + SIZE.jokerWidth / 2,
                       GRID_Y + Math.floor(index / COLUMNS) * CELL_Y
                       + SIZE.jokerHeight / 2)
      view.eventMode = 'static'
      view.cursor = 'pointer'
      view.on('pointertap', () => this.show(row.jokerId))
      view.on('pointerover', () => this.show(row.jokerId))
      this.grid.addChild(view)
      this.views.push(view)
    })

    this.pageLabel.text = tf('ui.pool.page', { at: this.page + 1, of: pages })
    if (this.prev) this.prev.enabled = pages > 1
    if (this.next) this.next.enabled = pages > 1

    if (!shown.some(row => row.jokerId === this.picked)) this.picked = ''
    this.paint()
  }

  /** 하나를 골라 옆에 적습니다. */
  private show(jokerId: string): void {
    if (this.picked === jokerId) return
    this.picked = jokerId
    this.paint()
  }

  private paint(): void {
    const row = this.picked
      ? this.data.tables.joker.findByJokerId(this.picked)
      : undefined

    const empty = !row
    this.sideHint.visible = empty
    this.sideName.visible = !empty
    this.sideMeta.visible = !empty
    this.sideLines.visible = !empty

    if (empty) {
      this.sideHint.text = this.choice === 'all'
        ? `${t('ui.pool.hint')}\n\n${t('ui.pool.allNote')}`
        : `${t('ui.pool.hint')}\n\n${t('ui.pool.baseNote')}`
      return
    }

    this.sideName.text = nameOf(this.data, 'joker', row.jokerId, row.name)
    const rarity = t(RARITY_KEYS[row.rarity] ?? '')
    this.sideMeta.text = `${rarity}   ·   $${row.cost}`
    const lines = describe(this.data, this.data.jokerEffects.get(row.jokerId) ?? [])
    this.sideLines.text = lines.join('\n')
  }

  relabel(): void {
    this.rebuild()
  }

  advance(seconds: number, clock: number): void {
    if (this.dirty) {
      this.dirty = false
      this.rebuild()
    }
    for (const view of this.views) view.advance(seconds, clock)
  }
}
