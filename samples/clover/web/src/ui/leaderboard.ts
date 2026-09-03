// 리더보드.
//
// **보드는 시트가 정합니다.** 서버가 `/boards` 로 넘겨 준 것을 그대로 탭과 목록으로
// 세우므로, 보드를 하나 더하는 데 이 파일이 바뀌지 않습니다.
//
// **이름은 조립합니다.** 보드 64개의 이름을 6개 언어로 시트에 두면 384줄이고, 그 값의
// 대부분이 이미 `StringTable` 에 있습니다 — 지표 이름과 스테이크 · 덱 · 챌린지의 이름이
// 그것입니다. 화면이 그 둘을 이어 붙입니다.
//
// 이 판이 지키는 것 셋입니다.
//
// **하나 — 내 자리가 항상 보입니다.** 순위표를 여는 이유가 그것이므로, 몇 쪽을 보고 있든
// 아래에 내 줄이 붙박이로 있습니다. 목록 안의 한 줄로만 두면 내가 400쪽에 있을 때 그 줄이
// 어디에도 없고, 그러면 이 판이 남의 순위만 보여 주는 판이 됩니다.
//
// **둘 — 목록이 굴러갑니다.** 챌린지 탭이 21개이고 덱 탭이 15개이므로, 자르면 뒤의 것들이
// 화면에서 없는 것이 됩니다 — [`scroll.ts`](scroll.ts) 가 그 자리입니다.
//
// **셋 — 기다리는 동안 화면이 튀지 않습니다.** 「가져오는 중」이 표를 지우고 잠깐 떴다
// 사라지면 그것은 알림이 아니라 깜빡임입니다. 처음에는 빈 줄로 자리를 잡아 두고, 그다음부터는
// 보고 있던 표를 그대로 둔 채 옅게 덮습니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { ascentPerStake } from '../core/metrics'
import { nameOf, t, tf } from '../core/strings'
import * as board from '../net/leaderboard'
import { loggedIn } from '../net/session'
import type { BoardInfo, BoardPage } from '../net/leaderboard'
import { COLOR } from '../render/theme'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { ScrollView } from './scroll'
import { Button } from './widgets'

const WIDTH = 1180
const HEIGHT = 744

/** 판의 안쪽 여백. 사방이 같습니다. */
const PAD = 26

/** 머리 띠 아래에서 내용이 시작하는 자리. */
const TOP = 62

const TAB_H = 32
const TAB_GAP = 16

/** 내용이 시작하는 세로 자리. */
const BODY_Y = TOP + TAB_H + TAB_GAP

/** 내 줄과 쪽 넘김이 놓이는 아래쪽. */
const MINE_H = 40
const FOOT_H = 44

/** 내 줄이 시작하는 자리. **아래에서부터 셉니다** — 자리가 고정입니다. */
const MINE_Y = HEIGHT - PAD - FOOT_H - MINE_H - 8

/** 왼쪽 목록이 보이는 높이. */
const BODY_H = MINE_Y - BODY_Y - 8

/** 왼쪽 목록. */
const LIST_W = 280
const LIST_ROW = 34

/** 오른쪽 표. */
const TABLE_X = PAD + LIST_W + 18
const TABLE_W = WIDTH - TABLE_X - PAD
const HEAD_H = 26
const ROW_H = 26

/** 표가 시작하는 자리와 보이는 높이. **한 쪽이 25행이므로 다 보이지 않고 굴러갑니다.** */
const TABLE_TOP = BODY_Y + HEAD_H + 8
const TABLE_H = MINE_Y - 10 - TABLE_TOP

/** 표의 칸. **머리와 줄과 내 줄이 같은 값을 씁니다.** */
const COL = {
  rank: 48,
  badge: 64,
  name: 84,
  value: TABLE_W - 18,
} as const

/** 한 쪽에 몇 행인가. 서버와 같아야 합니다. */
const PAGE_SIZE = 25

/**
 * 「가져오는 중」이 나타나기까지 기다리는 시간과, 한 번 나타나면 머무는 시간.
 *
 * **빠른 조회에는 나타나지 않고, 나타났으면 읽을 만큼 머뭅니다.** 순위 조회가 대개 10ms
 * 안에 끝나므로 그때마다 표가 지워졌다 돌아오면 그것은 깜빡임입니다.
 */
const WAIT_BEFORE_MS = 220
const WAIT_LEAST_MS = 340

const TABS = ['Main', 'Stake', 'Deck', 'Challenge'] as const
type Tab = (typeof TABS)[number]

/**
 * 보드 하나의 표시 이름.
 *
 * **지표 이름과 축의 이름을 잇습니다.** 축의 이름은 그 물건의 것을 그대로 씁니다 — 덱과
 * 챌린지의 이름이 판에서 보이는 것과 같아야 하고, 여기서만 쓰는 이름을 두면 한 곳이
 * 남습니다.
 */
export function boardLabel(data: Data, one: BoardInfo): string {
  const metric = t(`ui.lb.metric.${one.metric}`)
  const pool = one.pool === 'all' ? ` · ${t('ui.lb.pool.all')}` : ''

  switch (one.split) {
    case 'Stake':
      return `${nameOf(data, 'stake', one.splitRef, one.splitRef)} ${metric}${pool}`
    case 'Deck':
      return `${nameOf(data, 'deck', one.splitRef, one.splitRef)} ${metric}${pool}`
    case 'Challenge':
      return nameOf(data, 'challenge', one.splitRef, one.splitRef)
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
      const rows = data.tables.stake.records
      const perStake = ascentPerStake(data)
      const stake = Math.min(Math.floor(value / perStake) + 1, rows.length)
      const progress = value % perStake
      const row = rows.find(one => Number(one.stake) === stake)
      const name = row ? nameOf(data, 'stake', String(row.stake), row.name) : ''
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

  private readonly tabsRow = new Container()
  private readonly listScroll = new ScrollView(LIST_W, BODY_H)
  private readonly tableHead = new Container()
  private readonly tableScroll = new ScrollView(TABLE_W, TABLE_H)
  private readonly mineBar = new Container()
  private readonly foot = new Container()
  private readonly veil = new Container()

  private boards: BoardInfo[] = []
  private tab: Tab = 'Main'
  private picked = ''
  private period: 'season' | 'all' = 'season'
  private page = 0
  private shown?: BoardPage
  private problem = ''
  private waiting = false
  private waitTimer?: ReturnType<typeof setTimeout>

  /** 이름을 눌렀을 때. 프로필 판을 여는 자리입니다. */
  onProfile?: (handle: string) => void
  /**
   * 계정을 연결하겠다고 했습니다.
   *
   * **여기가 로그인으로 가는 자리입니다.** 순위표를 보고 나서 그 표에 오르고 싶어진
   * 사람이 누르는 것이므로, 무엇을 얻는지를 이미 본 뒤입니다.
   */
  onNeedAccount?: () => void

  constructor(private readonly data: Data, private readonly onClose: () => void) {
    this.view.addChild(panelFrame(WIDTH, HEIGHT, t('ui.lb.title'), this.onClose,
                                  undefined, false))

    // 왼쪽 목록의 바탕. **굴러가는 것은 안쪽이고 바탕은 가만히 있습니다.**
    const listPlate = new Graphics()
    listPlate.roundRect(PAD - 8, BODY_Y - 8, LIST_W + 16, BODY_H + 16, 10)
      .fill({ color: 0x111823, alpha: 0.72 })
    this.listScroll.position.set(PAD, BODY_Y)

    this.tableScroll.position.set(TABLE_X, TABLE_TOP)

    this.view.addChild(this.tabsRow, listPlate, this.listScroll,
                       this.tableHead, this.tableScroll, this.veil,
                       this.mineBar, this.foot)
    this.redraw()
    void this.loadBoards()
  }

  // -------------------------------------------------------------------------
  // 기다림
  // -------------------------------------------------------------------------

  /**
   * 조회 하나를 감쌉니다.
   *
   * **빨리 끝나면 아무 일도 없었던 것처럼 바뀝니다.** 늦어질 때만 덮개가 나타나고, 한 번
   * 나타났으면 그 덮개가 읽힐 만큼 머뭅니다.
   */
  private async whileWaiting<T>(work: Promise<T>): Promise<T> {
    let shown = false
    let shownAt = 0
    this.waitTimer = setTimeout(() => {
      this.waiting = true
      shown = true
      shownAt = Date.now()
      this.drawVeil()
    }, WAIT_BEFORE_MS)

    try {
      return await work
    } finally {
      clearTimeout(this.waitTimer)
      if (shown) {
        const left = WAIT_LEAST_MS - (Date.now() - shownAt)
        if (left > 0) await new Promise(done => setTimeout(done, left))
      }
      this.waiting = false
      this.drawVeil()
    }
  }

  // -------------------------------------------------------------------------
  // 받아오기
  // -------------------------------------------------------------------------

  private async loadBoards(): Promise<void> {
    try {
      this.boards = await this.whileWaiting(board.boards())
    } catch {
      // 알림은 `NetStatus` 가 띄웁니다. 여기는 판이 빈 채로 남는 것만 막습니다.
      this.problem = t('ui.lb.fail.title')
      this.redraw()
      return
    }
    this.problem = ''
    this.pickFirstInTab()
    await this.loadPage()
  }

  private pickFirstInTab(): void {
    const first = this.inTab()[0]
    this.picked = first ? first.boardId : ''
    this.page = 0
  }

  private inTab(): BoardInfo[] {
    return this.boards
      .filter(one => one.group === this.tab)
      .sort((one, two) => one.sortOrder - two.sortOrder)
  }

  /**
   * 누른 그 자리에서 다시 그리지 않습니다.
   *
   * **눌린 것을 지우는 일이 그 눌림을 처리하는 중에 일어나면 화면이 멈춥니다.** 그다음
   * 차례를 기다리던 것이 없어진 객체를 만나기 때문입니다 — 이 게임의 판들이 전부 다시
   * 그리는 식이므로, 누름에서 시작하는 것은 한 박자 뒤로 미룹니다.
   */
  private later(work: () => void): void {
    queueMicrotask(work)
  }

  private async loadPage(around?: 'me'): Promise<void> {
    if (this.picked === '') {
      this.shown = undefined
      this.redraw()
      return
    }
    // **보고 있던 표를 지우지 않습니다.** 새 것이 오면 그때 바뀝니다.
    this.redraw()
    try {
      const got = await this.whileWaiting(board.boardPage(this.picked, {
        period: this.period, page: this.page, around,
      }))
      if (around === 'me') this.page = Math.floor(got.from / PAGE_SIZE)
      this.shown = got
      this.problem = ''
    } catch {
      this.shown = undefined
      this.problem = t('ui.lb.fail.title')
    }
    this.redraw()
  }

  private redraw(): void {
    this.drawTabs()
    this.drawList()
    this.drawTable()
    this.drawMine()
    this.drawFoot()
    this.drawVeil()
  }

  // -------------------------------------------------------------------------
  // 탭
  // -------------------------------------------------------------------------

  private drawTabs(): void {
    this.tabsRow.removeChildren().forEach(child => child.destroy({ children: true }))

    let x = PAD
    for (const tab of TABS) {
      // **보드가 없는 탭은 그리지 않습니다.** 챌린지가 들어오기 전에는 그 탭이 없습니다.
      if (this.boards.length > 0 && !this.boards.some(one => one.group === tab)) continue

      const chip = this.chip(t(`ui.lb.tab.${tab}`), tab === this.tab, () => {
        if (this.tab === tab) return
        this.tab = tab
        this.pickFirstInTab()
        this.listScroll.toTop()
        this.later(() => void this.loadPage())
      })
      chip.position.set(x, TOP)
      this.tabsRow.addChild(chip)
      x += chip.width + 8
    }

    // 기간. **보드의 열이 아니라 조회의 인자입니다.**
    let right = WIDTH - PAD
    for (const period of ['all', 'season'] as const) {
      const chip = this.chip(t(`ui.lb.period.${period}`), period === this.period, () => {
        if (this.period === period) return
        this.period = period
        this.page = 0
        this.later(() => void this.loadPage())
      })
      chip.position.set(right - chip.width, TOP)
      this.tabsRow.addChild(chip)
      right -= chip.width + 8
    }
  }

  private chip(label: string, on: boolean, onPress: () => void): Container {
    const node = new Container()
    const text = new Text({
      text: label,
      style: { fontSize: 14, fill: on ? 0x0d1520 : 0x9fb0c4, fontWeight: '700' },
    })
    const width = Math.round(text.width) + 28
    const plate = new Graphics()
    plate.roundRect(0, 0, width, TAB_H, 8)
      .fill({ color: on ? COLOR.good : 0x1b2431 })
      .stroke({ color: on ? COLOR.good : 0x2c3849, width: 1.5 })
    text.anchor.set(0.5)
    text.position.set(width / 2, TAB_H / 2)
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
    const list = this.listScroll.content
    list.removeChildren().forEach(child => child.destroy({ children: true }))

    const rows = this.inTab()
    for (let at = 0; at < rows.length; at++) {
      const one = rows[at]
      const on = one.boardId === this.picked
      const y = at * LIST_ROW

      const row = new Container()
      const back = new Graphics()
      back.roundRect(2, y + 2, LIST_W - 14, LIST_ROW - 4, 7)
        .fill({ color: on ? 0x24354a : 0xffffff, alpha: on ? 1 : 0.0001 })
      if (on) back.roundRect(2, y + 7, 3, LIST_ROW - 14, 2).fill(COLOR.good)
      row.addChild(back)

      const label = new Text({
        text: boardLabel(this.data, one),
        style: {
          fontSize: 13, fill: on ? COLOR.ink : 0x93a3b8,
          fontWeight: on ? '700' : '400',
        },
      })
      label.anchor.set(0, 0.5)
      label.position.set(16, y + LIST_ROW / 2)
      row.addChild(label)

      row.eventMode = 'static'
      row.cursor = 'pointer'
      row.on('pointerover', () => { if (!on) back.alpha = 0.45 })
      row.on('pointerout', () => { back.alpha = on ? 1 : 0.0001 })
      row.on('pointertap', () => {
        // 굴리려고 끈 것은 고른 것이 아닙니다.
        if (this.listScroll.dragged) return
        if (this.picked === one.boardId) return
        this.picked = one.boardId
        this.page = 0
        this.later(() => void this.loadPage())
      })
      list.addChild(row)
    }

    this.listScroll.refresh()
  }

  // -------------------------------------------------------------------------
  // 오른쪽 표
  // -------------------------------------------------------------------------

  private drawTable(): void {
    this.tableHead.removeChildren().forEach(child => child.destroy({ children: true }))
    const table = this.tableScroll.content
    table.removeChildren().forEach(child => child.destroy({ children: true }))

    for (const [label, x, anchor] of [
      [t('ui.lb.col.rank'), COL.rank, 1],
      [t('ui.lb.col.name'), COL.name, 0],
      [t('ui.lb.col.value'), COL.value, 1],
    ] as [string, number, number][]) {
      const cell = new Text({
        text: label,
        style: { fontSize: 11, fill: 0x76869b, fontWeight: '700', letterSpacing: 1 },
      })
      cell.anchor.set(anchor, 0)
      cell.position.set(TABLE_X + x, BODY_Y + 4)
      this.tableHead.addChild(cell)
    }

    const line = new Graphics()
    line.rect(TABLE_X, BODY_Y + HEAD_H, TABLE_W, 1).fill({ color: 0x2c3849 })
    this.tableHead.addChild(line)

    if (this.problem !== '') {
      this.say(table, this.problem)
      this.tableScroll.refresh()
      return
    }
    const shown = this.shown
    if (!shown) {
      // **처음에는 빈 줄로 자리를 잡습니다.** 「가져오는 중」 한 줄이 가운데에 뜨면
      // 표가 들어오는 순간 판 전체가 다시 짜입니다.
      this.drawSkeleton(table)
      this.tableScroll.refresh()
      return
    }
    if (shown.rows.length === 0) {
      this.say(table, t('ui.lb.empty'))
      this.tableScroll.refresh()
      return
    }

    const mine = shown.me?.rank
    for (let at = 0; at < shown.rows.length; at++) {
      const row = shown.rows[at]
      this.drawRow(table, row.rank, row.handle, row.tier, row.value, shown.metric,
                   at * ROW_H, row.rank === mine, at % 2 === 1)
    }
    this.tableScroll.toTop()
    this.tableScroll.refresh()
  }

  /** 아직 아무것도 없을 때의 빈 줄들. 자리만 잡습니다. */
  private drawSkeleton(into: Container): void {
    for (let at = 0; at < PAGE_SIZE; at++) {
      const bar = new Graphics()
      const y = at * ROW_H
      bar.roundRect(COL.name, y + 7, 120, ROW_H - 16, 4)
        .fill({ color: 0xffffff, alpha: 0.045 })
      bar.roundRect(COL.value - 70, y + 7, 70, ROW_H - 16, 4)
        .fill({ color: 0xffffff, alpha: 0.03 })
      into.addChild(bar)
    }
  }

  private say(into: Container, message: string): void {
    const text = new Text({
      text: message,
      style: { fontSize: 14, fill: 0x76869b },
    })
    text.anchor.set(0.5, 0)
    text.position.set(TABLE_W / 2, 90)
    into.addChild(text)
  }

  private drawRow(into: Container, rank: number, handle: string, tier: string,
                  value: number, metric: string, y: number,
                  mine: boolean, striped: boolean): void {
    const row = new Container()

    const back = new Graphics()
    if (mine) {
      back.roundRect(0, y, TABLE_W, ROW_H - 2, 6)
        .fill({ color: 0x1f3348 })
        .stroke({ color: COLOR.good, width: 1, alpha: 0.55 })
    } else {
      // **한 줄 걸러 옅게.** 25줄이 붙어 있으면 눈이 줄을 놓칩니다.
      back.roundRect(0, y, TABLE_W, ROW_H - 2, 6)
        .fill({ color: 0xffffff, alpha: striped ? 0.022 : 0.0001 })
    }
    row.addChild(back)

    const middle = y + (ROW_H - 2) / 2

    const place = new Text({
      text: String(rank),
      style: {
        fontSize: 13, fill: mine ? COLOR.good : 0x8a99ad,
        fontWeight: mine ? '800' : '700',
      },
    })
    place.anchor.set(1, 0.5)
    place.position.set(COL.rank, middle)
    row.addChild(place)

    // 등급 배지. **없는 사람도 칸은 남습니다** — 있는 줄과 없는 줄의 이름이 어긋나면
    // 표가 울퉁불퉁합니다.
    if (tier !== '' && tier !== 'None') {
      const badge = new Graphics()
      badge.moveTo(0, -5).lineTo(5, 0).lineTo(0, 5).lineTo(-5, 0).closePath()
        .fill(this.tierColor(tier))
      badge.position.set(COL.badge, middle)
      row.addChild(badge)
    }

    const name = new Text({
      text: handle,
      style: {
        fontSize: 13, fill: mine ? COLOR.ink : 0xb9c6d8,
        fontWeight: mine ? '800' : '400',
      },
    })
    name.anchor.set(0, 0.5)
    name.position.set(COL.name, middle)
    row.addChild(name)

    const amount = new Text({
      text: valueLabel(this.data, metric, value),
      style: { fontSize: 13, fill: COLOR.money, fontWeight: '700' },
    })
    amount.anchor.set(1, 0.5)
    amount.position.set(COL.value, middle)
    row.addChild(amount)

    row.position.set(0, 0)
    if (handle !== '' && handle !== t('ui.lb.mine')) {
      row.eventMode = 'static'
      row.cursor = 'pointer'
      row.on('pointerover', () => { back.alpha = 0.55 })
      row.on('pointerout', () => { back.alpha = 1 })
      row.on('pointertap', () => this.onProfile?.(handle))
    }
    into.addChild(row)
  }

  private tierColor(tier: string): number {
    const row = this.data.tables.tier.records.find(one => one.name === tier
      || String(one.tier) === tier)
    if (row) return Number.parseInt(row.color.slice(1), 16)
    // 시트에 없는 이름이면 회색입니다. 화면이 그 이름 때문에 그려지지 않는 일은 없습니다.
    return 0x6f7d90
  }

  // -------------------------------------------------------------------------
  // 내 줄 — **붙박이입니다**
  // -------------------------------------------------------------------------

  private drawMine(): void {
    this.mineBar.removeChildren().forEach(child => child.destroy({ children: true }))

    const y = MINE_Y
    const shown = this.shown

    const plate = new Graphics()
    plate.roundRect(TABLE_X, y, TABLE_W, MINE_H, 9)
      .fill({ color: 0x16202e })
      .stroke({ color: shown?.me ? COLOR.good : 0x2c3849, width: 1.5,
                alpha: shown?.me ? 0.7 : 1 })
    this.mineBar.addChild(plate)

    const tag = new Text({
      text: t('ui.lb.mine'),
      style: { fontSize: 11, fill: COLOR.good, fontWeight: '800', letterSpacing: 1 },
    })
    tag.anchor.set(0, 0.5)
    tag.position.set(TABLE_X + 12, y + MINE_H / 2)
    this.mineBar.addChild(tag)

    if (!shown?.me) {
      // **계정이 없는 사람과 아직 기록이 없는 사람이 다릅니다.** 앞의 사람에게는 여기가
      // 계정을 연결하는 자리이고, 뒤의 사람에게는 한 판 더 두라는 말입니다.
      const guest = !loggedIn()
      const none = new Text({
        text: guest ? t('ui.account.needLink') : t('ui.lb.noRecord'),
        style: { fontSize: 13, fill: 0x76869b },
      })
      none.anchor.set(0, 0.5)
      none.position.set(TABLE_X + 68, y + MINE_H / 2)
      this.mineBar.addChild(none)

      if (guest) {
        const link = new Button(t('ui.account.link'), 120, 26, 0x2f8f52,
                                () => this.later(() => this.onNeedAccount?.()), 12)
        link.position.set(TABLE_X + TABLE_W - 132, y + (MINE_H - 26) / 2)
        this.mineBar.addChild(link)
      }
      return
    }

    const rank = new Text({
      text: `#${shown.me.rank.toLocaleString('en-US')}`,
      style: { fontSize: 19, fill: COLOR.ink, fontWeight: '800' },
    })
    rank.anchor.set(0, 0.5)
    rank.position.set(TABLE_X + 68, y + MINE_H / 2)
    this.mineBar.addChild(rank)

    // 몇 명 중 몇 번째인가. **등수만으로는 그것이 좋은지 알 수 없습니다.**
    const of = new Text({
      text: `/ ${shown.total.toLocaleString('en-US')}`,
      style: { fontSize: 12, fill: 0x76869b },
    })
    of.anchor.set(0, 0.5)
    of.position.set(TABLE_X + 68 + rank.width + 8, y + MINE_H / 2 + 2)
    this.mineBar.addChild(of)

    const amount = new Text({
      text: valueLabel(this.data, shown.metric, shown.me.value),
      style: { fontSize: 15, fill: COLOR.money, fontWeight: '800' },
    })
    amount.anchor.set(1, 0.5)
    amount.position.set(TABLE_X + COL.value, y + MINE_H / 2)
    this.mineBar.addChild(amount)

    // **내 자리로 가는 것이 이 줄 안에 있습니다.** 내 등수를 본 다음에 하는 일이 그것이고,
    // 밑단에 두면 눈이 한 번 더 옮겨 갑니다.
    const onPage = shown.rows.some(row => row.rank === shown.me?.rank)
    if (!onPage) {
      const jump = new Button(t('ui.button.toMe'), 96, 26, 0x2f5f8f,
                              () => this.later(() => void this.loadPage('me')), 12)
      jump.position.set(TABLE_X + COL.value - amount.width - 118, y + (MINE_H - 26) / 2)
      this.mineBar.addChild(jump)
    }
  }

  // -------------------------------------------------------------------------
  // 밑단
  // -------------------------------------------------------------------------

  private drawFoot(): void {
    this.foot.removeChildren().forEach(child => child.destroy({ children: true }))

    // **자리가 고정입니다.** 표의 길이에 따라 오르내리면 같은 단추를 매번 찾아야 합니다.
    const y = HEIGHT - PAD - FOOT_H
    const shown = this.shown
    if (!shown) return

    const pages = Math.max(1, Math.ceil(shown.total / PAGE_SIZE))
    const at = Math.floor(shown.from / PAGE_SIZE) + 1
    const middle = y + FOOT_H / 2

    const total = new Text({
      text: tf('ui.lb.total', { n: shown.total.toLocaleString('en-US') }),
      style: { fontSize: 12, fill: 0x76869b },
    })
    total.anchor.set(0, 0.5)
    total.position.set(TABLE_X + 2, middle)
    this.foot.addChild(total)

    const label = new Text({
      text: tf('ui.lb.page', { at, all: pages }),
      style: { fontSize: 13, fill: 0xb9c6d8, fontWeight: '700' },
    })
    label.anchor.set(0.5, 0.5)
    label.position.set(TABLE_X + TABLE_W - 52, middle)
    this.foot.addChild(label)

    const back = this.arrow('◂', at > 1, () => {
      this.page--
      this.later(() => void this.loadPage())
    })
    back.position.set(TABLE_X + TABLE_W - 104, middle - 15)
    const next = this.arrow('▸', at < pages, () => {
      this.page++
      this.later(() => void this.loadPage())
    })
    next.position.set(TABLE_X + TABLE_W - 30, middle - 15)
    this.foot.addChild(back, next)
  }

  private arrow(glyph: string, live: boolean, onPress: () => void): Container {
    const node = new Container()
    const plate = new Graphics()
    plate.roundRect(0, 0, 30, 30, 7)
      .fill({ color: live ? 0x1b2431 : 0x161d28 })
      .stroke({ color: live ? 0x2c3849 : 0x1f2833, width: 1 })
    const text = new Text({
      text: glyph,
      style: { fontSize: 16, fill: live ? 0xb9c6d8 : 0x4a5568, fontWeight: '700' },
    })
    text.anchor.set(0.5)
    text.position.set(15, 15)
    node.addChild(plate, text)
    if (live) {
      node.eventMode = 'static'
      node.cursor = 'pointer'
      node.on('pointertap', onPress)
    }
    return node
  }

  /** 기다리는 동안 표 위에 덮이는 것. **지우지 않고 덮습니다.** */
  private drawVeil(): void {
    this.veil.removeChildren().forEach(child => child.destroy({ children: true }))
    if (!this.waiting) return

    const cover = new Graphics()
    cover.roundRect(TABLE_X, TABLE_TOP - 4, TABLE_W, TABLE_H + 8, 8)
      .fill({ color: 0x0c121b, alpha: 0.55 })
    this.veil.addChild(cover)

    const text = new Text({
      text: t('ui.lb.loading'),
      style: { fontSize: 13, fill: 0x9fb0c4, fontWeight: '700' },
    })
    text.anchor.set(0.5)
    text.position.set(TABLE_X + TABLE_W / 2, TABLE_TOP + TABLE_H / 2)
    this.veil.addChild(text)
  }

  /** 말이 바뀌면 다시 그립니다. */
  relabel(): void {
    this.redraw()
  }

  onClosed(): void {
    clearTimeout(this.waitTimer)
  }
}
