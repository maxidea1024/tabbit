// 리더보드.
//
// **보드는 시트가 정합니다.** 서버가 `/boards` 로 넘겨 준 것을 그대로 탭과 목록으로
// 세우므로, 보드를 하나 더하는 데 이 파일이 바뀌지 않습니다.
//
// **이름은 조립합니다.** 보드 64개의 이름을 6개 언어로 시트에 두면 384줄이고, 그 값의
// 대부분이 이미 `StringTable` 에 있습니다 — 지표 이름과 스테이크 · 덱 · 챌린지의 이름이
// 그것입니다. 화면이 그 둘을 이어 붙입니다.
//
// 격자와 쪽 넘김의 구성은 [`joker-pool.ts`](joker-pool.ts) 와 같습니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { ascentPerStake } from '../core/metrics'
import { nameOf, t, tf } from '../core/strings'
import * as api from '../net/api'
import type { BoardInfo, BoardPage } from '../net/api'
import { COLOR } from '../render/theme'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { Button } from './widgets'

const WIDTH = 1180
const HEIGHT = 744

/** 왼쪽의 보드 목록. */
const LIST_X = 26
const LIST_W = 268
const LIST_Y = 118
const ROW_H = 30

/** 오른쪽의 순위표. */
const TABLE_X = LIST_X + LIST_W + 20
const TABLE_W = WIDTH - TABLE_X - 26
const TABLE_Y = LIST_Y
const TABLE_ROW = 21

/** 한 쪽에 몇 행인가. 서버와 같아야 합니다. */
const PAGE_SIZE = 25


const TABS = ['Main', 'Stake', 'Deck', 'Challenge'] as const
type Tab = (typeof TABS)[number]

/**
 * 보드 하나의 표시 이름.
 *
 * **지표 이름과 축의 이름을 잇습니다.** 축의 이름은 그 물건의 것을 그대로 씁니다 — 덱과
 * 챌린지의 이름이 판에서 보이는 것과 같아야 하고, 여기서만 쓰는 이름을 두면 한 곳이
 * 남습니다.
 */
export function boardLabel(data: Data, board: BoardInfo): string {
  const metric = t(`ui.lb.metric.${board.metric}`)
  const pool = board.pool === 'all' ? ` · ${t('ui.lb.pool.all')}` : ''

  switch (board.split) {
    case 'Stake':
      return `${nameOf(data, 'stake', board.splitRef, board.splitRef)} ${metric}${pool}`
    case 'Deck':
      return `${nameOf(data, 'deck', board.splitRef, board.splitRef)} ${metric}${pool}`
    case 'Challenge':
      return nameOf(data, 'challenge', board.splitRef, board.splitRef)
    default:
      return `${metric}${pool}`
  }
}

/**
 * 값을 사람이 읽는 꼴로.
 *
 * **지표마다 다릅니다.** 등정을 192 로 적으면 사람이 읽는 것이 아니고, 스테이크와 자리가
 * 그 수의 뜻입니다.
 */
export function valueLabel(data: Data, metric: string, value: number): string {
  switch (metric) {
    case 'Ascent': {
      // **스테이크가 앞자리입니다.** 사람이 읽는 것은 그 수가 아니라 「어느 스테이크에서
      // 어디까지」이므로, 자릿수를 풀어 적습니다.
      const rows = data.tables.stake.records
      const perStake = ascentPerStake(data)
      const stake = Math.min(Math.floor(value / perStake) + 1, rows.length)
      const progress = value % perStake
      const row = rows.find(one => Number(one.stake) === stake)
      const name = row ? nameOf(data, 'stake', String(row.stake), row.name) : ''
      // 다 지나온 것은 수가 아니라 「완주」로 적습니다.
      return progress >= perStake - 1
        ? `${name} · ${t('ui.lb.cleared')}`
        : `${name} · ${progress}`
    }
    case 'BestHand':
      return value.toLocaleString('en-US')
    case 'FewestHands':
      return tf('ui.lb.hands', { n: value })
    case 'MoneyAtWin':
      return `$${value}`
    default:
      return String(value)
  }
}

export class LeaderboardPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  private readonly list = new Container()
  private readonly table = new Container()

  private boards: BoardInfo[] = []
  private tab: Tab = 'Main'
  private picked = ''
  private period: 'season' | 'all' = 'season'
  private page = 0
  private shown?: BoardPage
  private note = ''

  /** 이름을 눌렀을 때. 프로필 판을 여는 자리입니다. */
  onProfile?: (handle: string) => void

  constructor(private readonly data: Data, private readonly onClose: () => void) {
    this.build()
    void this.loadBoards()
  }

  private build(): void {
    this.view.addChild(panelFrame(WIDTH, HEIGHT, t('ui.lb.title'), this.onClose,
                                  undefined, false))
    this.view.addChild(this.body)
    this.body.addChild(this.list, this.table)
  }

  private async loadBoards(): Promise<void> {
    this.note = t('ui.lb.loading')
    this.redraw()
    try {
      this.boards = await api.boards()
    } catch {
      // 알림은 `NetStatus` 가 띄웁니다. 여기는 판이 빈 채로 남는 것만 막습니다.
      this.note = t('ui.lb.fail.title')
      this.redraw()
      return
    }
    this.note = ''
    const first = this.inTab()[0]
    this.picked = first ? first.boardId : ''
    await this.loadPage()
  }

  private inTab(): BoardInfo[] {
    return this.boards
      .filter(board => board.group === this.tab)
      .sort((one, two) => one.sortOrder - two.sortOrder)
  }

  private async loadPage(around?: 'me'): Promise<void> {
    if (this.picked === '') {
      this.redraw()
      return
    }
    this.note = t('ui.lb.loading')
    this.redraw()
    try {
      this.shown = await api.boardPage(this.picked, {
        period: this.period,
        page: this.page,
        around,
      })
      if (around === 'me') this.page = Math.floor(this.shown.from / PAGE_SIZE)
      this.note = ''
    } catch {
      this.shown = undefined
      this.note = t('ui.lb.fail.title')
    }
    this.redraw()
  }

  private redraw(): void {
    this.drawTabs()
    this.drawList()
    this.drawTable()
  }

  // -------------------------------------------------------------------------
  // 탭
  // -------------------------------------------------------------------------

  private tabsRow?: Container

  private drawTabs(): void {
    if (this.tabsRow) {
      this.body.removeChild(this.tabsRow)
      this.tabsRow.destroy({ children: true })
    }
    const row = new Container()
    this.tabsRow = row
    this.body.addChild(row)

    let x = LIST_X
    for (const tab of TABS) {
      // **보드가 없는 탭은 그리지 않습니다.** 챌린지가 들어오기 전에는 그 탭이 없습니다.
      if (this.boards.length > 0 && !this.boards.some(board => board.group === tab)) continue

      const label = t(`ui.lb.tab.${tab}`)
      const chip = this.chip(label, tab === this.tab, () => {
        if (this.tab === tab) return
        this.tab = tab
        this.page = 0
        const first = this.inTab()[0]
        this.picked = first ? first.boardId : ''
        void this.loadPage()
      })
      chip.position.set(x, 66)
      row.addChild(chip)
      x += chip.width + 8
    }

    // 기간. **보드의 열이 아니라 조회의 인자입니다.**
    let right = WIDTH - 26
    for (const period of ['all', 'season'] as const) {
      const chip = this.chip(t(`ui.lb.period.${period}`), period === this.period, () => {
        if (this.period === period) return
        this.period = period
        this.page = 0
        void this.loadPage()
      })
      chip.position.set(right - chip.width, 66)
      row.addChild(chip)
      right -= chip.width + 8
    }
  }

  private chip(label: string, on: boolean, onPress: () => void): Container {
    const node = new Container()
    const text = new Text({
      text: label,
      style: { fontSize: 14, fill: on ? 0x0d1520 : COLOR.ink, fontWeight: '700' },
    })
    const width = text.width + 26
    const plate = new Graphics()
    plate.roundRect(0, 0, width, 30, 8)
      .fill({ color: on ? COLOR.good : 0x1d2634 })
      .stroke({ color: on ? COLOR.good : 0x2c3849, width: 1.5 })
    text.position.set(13, 6)
    node.addChild(plate, text)
    node.eventMode = 'static'
    node.cursor = 'pointer'
    node.on('pointertap', onPress)
    return node
  }

  // -------------------------------------------------------------------------
  // 왼쪽 목록
  // -------------------------------------------------------------------------

  private drawList(): void {
    this.list.removeChildren().forEach(child => child.destroy({ children: true }))

    const boards = this.inTab()
    const plate = new Graphics()
    plate.roundRect(LIST_X, LIST_Y - 8, LIST_W, HEIGHT - LIST_Y - 26, 10)
      .fill({ color: 0x121a26, alpha: 0.7 })
    this.list.addChild(plate)

    for (let at = 0; at < boards.length; at++) {
      const board = boards[at]
      const on = board.boardId === this.picked
      const y = LIST_Y + at * ROW_H

      const row = new Container()
      if (on) {
        const mark = new Graphics()
        mark.roundRect(LIST_X + 4, y - 2, LIST_W - 8, ROW_H - 2, 6)
          .fill({ color: 0x24354a })
        row.addChild(mark)
      }

      const label = new Text({
        text: boardLabel(this.data, board),
        style: {
          fontSize: 13, fill: on ? COLOR.ink : 0x9fb0c4, fontWeight: on ? '700' : '400',
        },
      })
      label.position.set(LIST_X + 14, y + 3)
      row.addChild(label)

      row.eventMode = 'static'
      row.cursor = 'pointer'
      row.on('pointertap', () => {
        if (this.picked === board.boardId) return
        this.picked = board.boardId
        this.page = 0
        void this.loadPage()
      })
      this.list.addChild(row)
    }
  }

  // -------------------------------------------------------------------------
  // 오른쪽 순위표
  // -------------------------------------------------------------------------

  private drawTable(): void {
    this.table.removeChildren().forEach(child => child.destroy({ children: true }))

    const head = new Container()
    for (const [label, x, anchor] of [
      [t('ui.lb.col.rank'), 10, 0],
      [t('ui.lb.col.name'), 66, 0],
      [t('ui.lb.col.value'), TABLE_W - 16, 1],
    ] as [string, number, number][]) {
      const cell = new Text({
        text: label,
        style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
      })
      cell.anchor.set(anchor, 0)
      cell.position.set(TABLE_X + x, TABLE_Y - 4)
      head.addChild(cell)
    }
    this.table.addChild(head)

    const line = new Graphics()
    line.rect(TABLE_X, TABLE_Y + 16, TABLE_W, 1).fill({ color: 0x2c3849 })
    this.table.addChild(line)

    if (this.note !== '') {
      this.say(this.note)
      return
    }
    const shown = this.shown
    if (!shown) {
      this.say(t('ui.lb.empty'))
      return
    }
    if (shown.rows.length === 0) {
      this.say(t('ui.lb.empty'))
    }

    const mine = shown.me?.rank
    for (let at = 0; at < shown.rows.length; at++) {
      const row = shown.rows[at]
      this.drawRow(row.rank, row.handle, row.tier, row.value, shown.metric,
                   TABLE_Y + 26 + at * TABLE_ROW, row.rank === mine)
    }

    // **내가 이 쪽에 없으면 표의 맨 아래에 내 행이 따로 붙습니다.**
    //
    // **자리는 실제로 그린 행 수에서 옵니다.** 한 쪽 분량으로 잡아 두면 마지막 쪽처럼
    // 몇 행뿐인 보드에서 내 행이 빈 자리 아래에 혼자 떨어집니다.
    const onPage = shown.rows.some(row => row.rank === mine)
    const bottom = TABLE_Y + 26 + Math.max(shown.rows.length, 1) * TABLE_ROW + 10
    if (shown.me && !onPage) {
      const split = new Graphics()
      split.rect(TABLE_X, bottom - 8, TABLE_W, 1).fill({ color: 0x2c3849 })
      this.table.addChild(split)
      this.drawRow(shown.me.rank, t('ui.lb.mine'), '', shown.me.value, shown.metric,
                   bottom, true)
    } else if (!shown.me) {
      const none = new Text({
        text: t('ui.lb.noRecord'),
        style: { fontSize: 12, fill: COLOR.inkDim },
      })
      none.position.set(TABLE_X + 10, bottom)
      this.table.addChild(none)
    }

    this.drawFoot(shown, bottom + 34)
  }

  private say(message: string): void {
    const text = new Text({
      text: message,
      style: { fontSize: 14, fill: COLOR.inkDim },
    })
    text.anchor.set(0.5, 0)
    text.position.set(TABLE_X + TABLE_W / 2, TABLE_Y + 120)
    this.table.addChild(text)
  }

  private drawRow(rank: number, handle: string, tier: string, value: number,
                  metric: string, y: number, mine: boolean): void {
    const row = new Container()

    if (mine) {
      const mark = new Graphics()
      mark.roundRect(TABLE_X, y - 3, TABLE_W, TABLE_ROW - 1, 5)
        .fill({ color: 0x203449 })
        .stroke({ color: COLOR.good, width: 1, alpha: 0.6 })
      row.addChild(mark)
    }

    const cells: [string, number, number, number, string][] = [
      [`${rank}`, 10, 0, mine ? COLOR.good : COLOR.inkDim, '700'],
      [handle, 66, 0, COLOR.ink, mine ? '700' : '400'],
      [valueLabel(this.data, metric, value), TABLE_W - 16, 1, COLOR.money, '700'],
    ]
    for (const [label, x, anchor, fill, weight] of cells) {
      const cell = new Text({
        text: label,
        style: { fontSize: 13, fill, fontWeight: weight as '400' | '700' },
      })
      cell.anchor.set(anchor, 0)
      cell.position.set(TABLE_X + x, y)
      row.addChild(cell)
    }

    // 등급 배지. **없는 사람도 칸은 남습니다** — 있는 줄과 없는 줄의 이름이 어긋나면
    // 표가 울퉁불퉁합니다.
    if (tier !== '' && tier !== 'None') {
      const badge = new Graphics()
      const color = this.tierColor(tier)
      badge.moveTo(0, -5).lineTo(5, 0).lineTo(0, 5).lineTo(-5, 0).closePath().fill(color)
      badge.position.set(TABLE_X + 50, y + 8)
      row.addChild(badge)
    }

    if (handle !== '' && handle !== t('ui.lb.mine')) {
      row.eventMode = 'static'
      row.cursor = 'pointer'
      row.on('pointertap', () => this.onProfile?.(handle))
    }
    this.table.addChild(row)
  }

  private tierColor(tier: string): number {
    const row = this.data.tables.tier.records.find(one => one.name === tier
      || String(one.tier) === tier)
    if (row) return Number.parseInt(row.color.slice(1), 16)
    // 시트에 없는 이름이면 회색입니다. 화면이 그 이름 때문에 그려지지 않는 일은 없습니다.
    return 0x6f7d90
  }

  private drawFoot(shown: BoardPage, y: number): void {
    const pages = Math.max(1, Math.ceil(shown.total / PAGE_SIZE))
    const at = Math.floor(shown.from / PAGE_SIZE) + 1

    const total = new Text({
      text: tf('ui.lb.total', { n: shown.total.toLocaleString('en-US') }),
      style: { fontSize: 12, fill: COLOR.inkDim },
    })
    total.position.set(TABLE_X + 10, y + 8)
    this.table.addChild(total)

    const label = new Text({
      text: tf('ui.lb.page', { at, all: pages }),
      style: { fontSize: 13, fill: COLOR.ink, fontWeight: '700' },
    })
    label.anchor.set(0.5, 0)
    label.position.set(TABLE_X + TABLE_W - 96, y + 6)
    this.table.addChild(label)

    const back = this.arrow('◂', () => {
      if (this.page === 0) return
      this.page--
      void this.loadPage()
    })
    back.position.set(TABLE_X + TABLE_W - 158, y)
    const next = this.arrow('▸', () => {
      if (at >= pages) return
      this.page++
      void this.loadPage()
    })
    next.position.set(TABLE_X + TABLE_W - 34, y)
    this.table.addChild(back, next)

    const me = new Button(t('ui.button.toMe'), 96, 28, 0x2f5f8f, () => {
      void this.loadPage('me')
    }, 12)
    me.position.set(TABLE_X + TABLE_W / 2 - 48, y + 2)
    this.table.addChild(me)
  }

  private arrow(glyph: string, onPress: () => void): Container {
    const node = new Container()
    const text = new Text({
      text: glyph,
      style: { fontSize: 18, fill: COLOR.ink, fontWeight: '700' },
    })
    text.anchor.set(0.5)
    text.position.set(14, 15)
    const hit = new Graphics()
    hit.roundRect(0, 0, 28, 30, 6).fill({ color: 0x1d2634 })
    node.addChild(hit, text)
    node.eventMode = 'static'
    node.cursor = 'pointer'
    node.on('pointertap', onPress)
    return node
  }

  /** 말이 바뀌면 다시 그립니다. */
  relabel(): void {
    this.redraw()
  }
}
