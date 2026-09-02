// 조커 풀 고르기.
//
// **고르기 전에 무엇이 들어 있는지 볼 수 있어야 합니다.** 「기본 150종」과 「확장 500종」이
// 라는 글만 두면 무엇이 늘어나는지 알 수 없고, 그러면 고르는 일이 찍는 일이 됩니다 — 그래서
// 이 판은 고른 풀의 조커를 카드로 늘어놓고, 마우스를 올리면 설명 쪽지를 띄웁니다.
//
// **설명 자리를 판에 붙박지 않습니다.** 오른쪽에 칸을 두었더니 판의 4분의 1을 늘 차지하고
// 그만큼 카드가 줄었습니다 — 설명은 하나를 볼 때만 필요하므로 그때만 뜨는 편이 맞고, 그러면
// 같은 판에 카드가 40장 들어갑니다.
//
// **쪽지도 카드도 게임에서 쓰는 것과 같은 것입니다.** `Tooltip` 과 `JokerView` 를 그대로
// 세우므로 글의 모양도 그림도 희귀도 테두리도 상점에서 보는 것과 같습니다.
//
// 효과 글은 `describe()` 가 효과 행에서 만듭니다. **설명문을 손으로 적어 둔 데이터가
// 없으므로** 이 판이 데이터와 어긋날 자리가 없습니다.

import { Container, Text } from 'pixi.js'

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
import { Tooltip } from './tooltip'
import { Button } from './widgets'

const WIDTH = 1092
const HEIGHT = 744

/**
 * 격자. 카드가 88 × 124 이고 이름이 그 아래에 들어가므로 세로 간격이 146 입니다.
 *
 * **쪽 단추는 아래가 아니라 머리에 있습니다.** 아래에 두었다가 마지막 줄의 카드 위에
 * 얹혔습니다 — 격자가 판을 가득 쓰면 아랫단에 둘 자리가 없습니다.
 */
const COLUMNS = 10
const ROWS = 4
const PER_PAGE = COLUMNS * ROWS
const CELL_X = 104
const CELL_Y = 146
const GRID_X = 34
const GRID_Y = 132

/** 머리의 단추들이 서는 줄. */
const HEAD_Y = 62
const HEAD_H = 54

const RARITY_KEYS = ['', 'ui.rarity.common', 'ui.rarity.uncommon',
                     'ui.rarity.rare', 'ui.rarity.legendary']

/**
 * 무엇으로 줄을 세우는가.
 *
 * `order` 가 수집 목록의 순서이고 **그것이 기본입니다** — 계열이 뭉쳐 있으므로 무엇이
 * 한 묶음인지가 그 순서에서만 보입니다. 나머지 셋은 찾을 때 씁니다.
 */
type SortKey = 'order' | 'rarity' | 'name' | 'cost'

const SORTS: { key: SortKey; label: string }[] = [
  { key: 'order', label: 'ui.pool.sortOrder' },
  { key: 'rarity', label: 'ui.pool.sortRarity' },
  { key: 'name', label: 'ui.pool.sortName' },
  { key: 'cost', label: 'ui.pool.sortCost' },
]

export class JokerPoolPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  private readonly grid = new Container()
  private readonly tooltip = new Tooltip()

  private readonly pageLabel = new Text({
    text: '', style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
  })
  /** 아랫단의 한 줄. 이 판이 무엇을 하는 곳인지 적습니다. */
  private readonly hint = new Text({
    text: '', style: { fontSize: 13, fill: COLOR.inkDim },
  })

  private readonly poolButtons: { choice: PoolChoice; button: Button; key: string }[] = []
  private readonly sortButtons: { key: SortKey; button: Button; label: string }[] = []
  private order?: Button
  private prev?: Button
  private next?: Button
  private frame?: Container

  private views: JokerView[] = []
  private page = 0
  /** 다음 프레임에 다시 세워야 하는가. 그림이 들어오면 켜집니다. */
  private dirty = false

  private sort: SortKey = 'order'
  /** 오름차순인가. 내림차순이면 거꾸로 세웁니다. */
  private ascending = true

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
    // **그 자리에서 세우지 않고 표시만 남깁니다.** 그림 하나마다 부르므로 한 쪽을 열면
    // 40번이 오고, 그때마다 카드 40장을 버리고 다시 만들면 한 프레임에 1,600장입니다.
    onArtReady(() => { this.dirty = true })
  }

  /**
   * 틀을 세웁니다.
   *
   * **말이 바뀌면 다시 세웁니다.** 제목은 `panelFrame` 안에 그려지므로 한 번 만들고 두면
   * 판을 처음 세운 때의 말로 남습니다 — 일본어로 바꿔도 제목만 영어였던 것이 그것입니다.
   */
  private buildFrame(): void {
    if (this.frame) {
      this.view.removeChild(this.frame)
      this.frame.destroy({ children: true })
    }
    this.frame = panelFrame(WIDTH, HEIGHT, t('ui.pool.title'), this.onClose,
                            undefined, false)
    this.view.addChildAt(this.frame, 0)
  }

  private build(): void {
    this.buildFrame()
    this.view.addChild(this.body)

    // 풀 단추 둘. **나란히 둡니다** — 하나를 고르는 일이므로 목록이 아니라 두 갈래입니다.
    const pw = 200
    for (const [index, choice] of (['base', 'all'] as PoolChoice[]).entries()) {
      const key = choice === 'all' ? 'ui.pool.all' : 'ui.pool.base'
      const button = new Button(t(key), pw, HEAD_H, 0x2f5f8f,
                                () => this.choose(choice), 17)
      button.position.set(GRID_X + index * (pw + 14), HEAD_Y)
      this.poolButtons.push({ choice, button, key })
      this.body.addChild(button)
    }

    // 줄 세우기. **기준 넷과 방향 하나입니다** — 500종이 되면 「그 조커가 어디 있더라」가
    // 눈으로 훑어서는 풀리지 않습니다.
    const sw = 70
    for (const [index, one] of SORTS.entries()) {
      const button = new Button(t(one.label), sw, HEAD_H, 0x2a3446,
                                () => this.sortBy(one.key), 14)
      button.position.set(480 + index * (sw + 6), HEAD_Y)
      this.sortButtons.push({ key: one.key, button, label: one.label })
      this.body.addChild(button)
    }
    this.order = new Button('', 40, HEAD_H, 0x2a3446, () => this.flip(), 18)
    this.order.position.set(790, HEAD_Y)
    this.body.addChild(this.order)

    this.body.addChild(this.grid)

    // 쪽 넘김. 머리의 오른쪽 끝입니다.
    this.prev = new Button('◀', 56, HEAD_H, 0x2a3446, () => this.turn(-1), 20)
    this.prev.position.set(862, HEAD_Y)
    this.pageLabel.anchor.set(0.5, 0.5)
    this.pageLabel.position.set(950, HEAD_Y + HEAD_H / 2)
    this.next = new Button('▶', 56, HEAD_H, 0x2a3446, () => this.turn(1), 20)
    this.next.position.set(982, HEAD_Y)
    this.body.addChild(this.prev, this.next, this.pageLabel)

    this.hint.anchor.set(0.5, 0.5)
    this.hint.position.set(WIDTH / 2, HEIGHT - 26)
    this.body.addChild(this.hint)

    // **쪽지는 맨 위입니다.** 카드 위에 떠야 하므로 판의 마지막 자식입니다.
    this.view.addChild(this.tooltip)
  }

  /** 지금 풀의 조커들을 고른 기준으로 세운 것. */
  private rows() {
    const pools = poolsOf(this.choice)
    const all = this.data.tables.joker.records.filter(row => pools.includes(row.pool))

    const name = (id: string, fallback: string) =>
      nameOf(this.data, 'joker', id, fallback)

    const sorted = [...all]
    if (this.sort === 'rarity') {
      // 같은 희귀도 안에서는 수집 순서입니다. **되풀이해도 같은 줄이어야** 쪽을 넘겼다
      // 돌아왔을 때 자리가 바뀌지 않습니다.
      sorted.sort((a, b) => a.rarity - b.rarity || a.sortOrder - b.sortOrder)
    } else if (this.sort === 'cost') {
      sorted.sort((a, b) => a.cost - b.cost || a.sortOrder - b.sortOrder)
    } else if (this.sort === 'name') {
      sorted.sort((a, b) =>
        name(a.jokerId, a.name).localeCompare(name(b.jokerId, b.name)))
    } else {
      sorted.sort((a, b) => a.sortOrder - b.sortOrder)
    }
    if (!this.ascending) sorted.reverse()
    return sorted
  }

  private choose(choice: PoolChoice): void {
    if (this.choice === choice) return
    this.choice = choice
    this.page = 0
    this.onPick?.(choice)
    this.rebuild()
  }

  private sortBy(key: SortKey): void {
    if (this.sort === key) {
      this.flip()
      return
    }
    this.sort = key
    this.page = 0
    this.rebuild()
  }

  private flip(): void {
    this.ascending = !this.ascending
    this.page = 0
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
    this.tooltip.hide()

    const all = this.rows()
    const pages = Math.max(1, Math.ceil(all.length / PER_PAGE))
    this.page = Math.min(this.page, pages - 1)
    const shown = all.slice(this.page * PER_PAGE, (this.page + 1) * PER_PAGE)

    for (const one of this.poolButtons) {
      one.button.text = t(one.key)
      // **눌린 채로 두고 나머지를 흐리게 합니다.** 둘 중 하나만 하면 어두운 바탕에서
      // 어느 것이 고른 것인지가 눈에 들지 않습니다.
      const on = one.choice === this.choice
      one.button.highlight = on
      one.button.alpha = on ? 1 : 0.55
    }
    for (const one of this.sortButtons) {
      one.button.text = t(one.label)
      const on = one.key === this.sort
      one.button.highlight = on
      one.button.alpha = on ? 1 : 0.55
    }
    if (this.order) this.order.text = this.ascending ? '▲' : '▼'

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
      const x = GRID_X + (index % COLUMNS) * CELL_X + SIZE.jokerWidth / 2
      const y = GRID_Y + Math.floor(index / COLUMNS) * CELL_Y + SIZE.jokerHeight / 2
      view.motion.snap(x, y)
      view.eventMode = 'static'
      view.cursor = 'pointer'
      // 손가락에는 마우스 오버가 없으므로 누르는 것도 같은 일을 합니다.
      view.on('pointerover', () => this.hover(row.jokerId, x, y))
      view.on('pointertap', () => this.hover(row.jokerId, x, y))
      view.on('pointerout', () => this.tooltip.hide())
      this.grid.addChild(view)
      this.views.push(view)
    })

    this.pageLabel.text = tf('ui.pool.page', { at: this.page + 1, of: pages })
    if (this.prev) this.prev.enabled = pages > 1
    if (this.next) this.next.enabled = pages > 1

    this.hint.text = this.choice === 'all'
      ? `${t('ui.pool.hint')}   ·   ${t('ui.pool.allNote')}`
      : `${t('ui.pool.hint')}   ·   ${t('ui.pool.baseNote')}`
  }

  /** 하나를 가리켰습니다. 쪽지를 그 카드 아래에 띄웁니다. */
  private hover(jokerId: string, x: number, y: number): void {
    const row = this.data.tables.joker.findByJokerId(jokerId)
    if (!row) return
    const lines = describe(this.data, this.data.jokerEffects.get(jokerId) ?? [])
    this.tooltip.show(
      nameOf(this.data, 'joker', jokerId, row.name),
      t(RARITY_KEYS[row.rarity] ?? ''), row.rarity, lines,
      x, y + SIZE.jokerHeight / 2, { width: WIDTH, height: HEIGHT }, row.cost)
  }

  relabel(): void {
    this.buildFrame()
    this.rebuild()
  }

  onClosed(): void {
    this.tooltip.hide()
  }

  advance(seconds: number, clock: number): void {
    if (this.dirty) {
      this.dirty = false
      this.rebuild()
    }
    for (const view of this.views) view.advance(seconds, clock)
  }
}
