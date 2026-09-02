// 덱과 스테이크 고르기.
//
// **데이터와 코어는 진작 있었고 화면만 없었습니다.** `Deck` 15줄과 `DeckEffect` 29줄과
// `Stake` 8줄이 채워져 있고 리플레이도 굽혀 있는데, 판을 시작하는 자리가 `red_deck` 과
// `White` 로 고정되어 있어 게임에서는 붉은 덱 하나만 돌았습니다.
//
// **뒷면을 칸마다 그립니다.** 덱이 정하는 것 중 한 판 내내 보이는 것이 뒷면이므로, 이름만
// 적힌 목록에서는 무엇을 고르는지가 판을 시작한 뒤에야 보입니다. `render/card-back.ts` 의
// 그리는 함수를 그대로 부르므로 여기서 맞으면 판에서도 맞습니다.
//
// **잠긴 칸이 없습니다.** 결제가 없는 로그라이트에서 해금은 순수한 지연이므로 15덱과 8종을
// 처음부터 전부 엽니다 — `Deck.unlock` 은 표시용 문자열로 남아 있고 이 화면은 그것을 읽지
// 않습니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { stakeSlug } from '../core/stake'
import { nameOf, t, tf } from '../core/strings'
import { StakeKind } from '../generated/enums/stake-kind'
import { backLookOf, drawCardBack } from '../render/card-back'
import { COLOR, SIZE } from '../render/theme'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { richBlock, rowsOf, type RichStyle } from './rich'
import { Button } from './widgets'

const WIDTH = 1080
const HEIGHT = 640

/** 덱 격자. 15칸이므로 5 × 3 입니다. */
const COLUMNS = 5
const CELL_W = 132
const CELL_H = 118
const GRID_X = 30
const GRID_Y = 92

/** 칸 안의 뒷면. **손패보다 작지만 무늬가 읽히는 크기입니다.** */
const BACK_W = Math.round(SIZE.cardWidth * 0.62)
const BACK_H = Math.round(SIZE.cardHeight * 0.62)

/** 스테이크 줄. 덱 격자 아래에 한 줄로 섭니다. */
const STAKE_Y = GRID_Y + 3 * CELL_H + 18
const STAKE_W = 80
const STAKE_H = 58

/** 오른쪽의 설명 자리. */
const SIDE_X = GRID_X + COLUMNS * CELL_W + 20
const SIDE_W = WIDTH - SIDE_X - 30
const SIDE_H = STAKE_Y + STAKE_H - GRID_Y

const RULE_STYLE: RichStyle = {
  base: { fontSize: 13, fill: COLOR.ink },
  number: COLOR.money,
  term: COLOR.good,
}

/**
 * 스테이크 여덟의 색.
 *
 * **데이터가 아니라 표시입니다.** 스테이크의 이름이 곧 색이므로 칸을 그 색으로 칠하면
 * 이름을 읽지 않고도 어느 것인지 보이고, 여덟이 한 줄에 섰을 때 순서가 색으로 읽힙니다.
 */
const STAKE_COLOR: Record<number, number> = {
  [StakeKind.White]: 0xf2ece0,
  [StakeKind.Red]: 0xc0392f,
  [StakeKind.Green]: 0x3d8b52,
  [StakeKind.Black]: 0x26262c,
  [StakeKind.Blue]: 0x2f6fc0,
  [StakeKind.Purple]: 0x9a5bd2,
  [StakeKind.Orange]: 0xd07a2f,
  [StakeKind.Gold]: 0xe0b53b,
}

/** 이 판을 무엇으로 시작하는가. **판이 아니라 저장이 가지는 값입니다.** */
export interface RunSetup {
  deckId: string
  /** `StakeKind` 의 이름입니다 — 리플레이에 적히는 형태와 같습니다. */
  stake: string
}

export function defaultSetup(): RunSetup {
  return { deckId: 'red_deck', stake: StakeKind[StakeKind.White] }
}

/**
 * 표에 있는 것으로 고쳐 줍니다.
 *
 * **저장된 값을 믿지 않습니다.** 손으로 고친 저장소나 예전 판의 값이 표에 없는 덱을 가리킬
 * 수 있고, 그러면 시작 조건이 하나도 걸리지 않은 판이 조용히 돌아갑니다.
 */
export function validSetup(data: Data, setup: RunSetup): RunSetup {
  const fallback = defaultSetup()
  const deckId = data.tables.deck.findByDeckId(setup.deckId)
    ? setup.deckId : fallback.deckId
  const stake = data.tables.stake.records.some(row => StakeKind[row.stake] === setup.stake)
    ? setup.stake : fallback.stake
  return { deckId, stake }
}

/** 그 덱과 스테이크의 표시 이름을 한 줄로. 타이틀의 단추가 이것을 답니다. */
export function setupLabel(data: Data, setup: RunSetup): string {
  const deck = data.tables.deck.findByDeckId(setup.deckId)
  const deckName = deck
    ? nameOf(data, 'deck', setup.deckId, deck.name) : setup.deckId
  const row = data.tables.stake.records.find(one => StakeKind[one.stake] === setup.stake)
  const stakeName = row
    ? nameOf(data, 'stake', stakeSlug(row.stake), row.name) : setup.stake
  return `${deckName} · ${stakeName}`
}

export class SetupPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  private readonly grid = new Container()
  private readonly stakes = new Container()
  private readonly side = new Container()

  private readonly decks: { deckId: string; name: string }[]
  private readonly stakeRows: { stake: StakeKind; name: string }[]

  private deckAt = 0
  private stakeAt = 0
  private frame?: Container
  private startButton?: Button

  /** 고른 것이 바뀔 때마다 부릅니다. 저장하는 쪽이 받습니다. */
  onPick?: (setup: RunSetup) => void
  /** 고른 것으로 판을 엽니다. */
  onStart?: (setup: RunSetup) => void

  constructor(private readonly data: Data, setup: RunSetup,
              private readonly onClose: () => void) {
    this.decks = data.tables.deck.records
      .slice()
      .sort((one, two) => one.sortOrder - two.sortOrder)
      .map(row => ({
        deckId: row.deckId,
        name: nameOf(data, 'deck', row.deckId, row.name),
      }))

    // **스테이크에는 순서 컬럼이 없습니다.** enum 의 값이 곧 순서이고, 표의 선언에 적힌
    // 「누적이므로 뒤의 것은 앞의 것을 전부 포함합니다」가 그 순서입니다.
    this.stakeRows = data.tables.stake.records
      .slice()
      .sort((one, two) => one.stake - two.stake)
      .map(row => ({
        stake: row.stake,
        name: nameOf(data, 'stake', stakeSlug(row.stake), row.name),
      }))

    this.point(validSetup(data, setup))
    this.build()
    this.rebuild()
  }

  /** 저장된 것을 격자의 자리로 옮깁니다. */
  private point(setup: RunSetup): void {
    const deck = this.decks.findIndex(one => one.deckId === setup.deckId)
    const stake = this.stakeRows.findIndex(one => StakeKind[one.stake] === setup.stake)
    this.deckAt = deck < 0 ? 0 : deck
    this.stakeAt = stake < 0 ? 0 : stake
  }

  /** 지금 고른 것. */
  private picked(): RunSetup {
    return {
      deckId: this.decks[this.deckAt]?.deckId ?? defaultSetup().deckId,
      stake: StakeKind[this.stakeRows[this.stakeAt]?.stake ?? StakeKind.White],
    }
  }

  /** 바깥에서 고른 것이 바뀌었을 때. 말이 바뀌어 판을 다시 세울 때도 씁니다. */
  setSetup(setup: RunSetup): void {
    this.point(validSetup(this.data, setup))
    this.rebuild()
  }

  private build(): void {
    this.buildFrame()
    this.view.addChild(this.body)
    this.body.addChild(this.grid, this.stakes, this.side)

    const bw = SIDE_W - 40
    this.startButton = new Button(t('ui.setup.start'), bw, 46, 0x2f8f52,
                                  () => this.onStart?.(this.picked()), 18)
    this.startButton.position.set(SIDE_X + (SIDE_W - bw) / 2, GRID_Y + SIDE_H + 14)
    this.body.addChild(this.startButton)
  }

  private buildFrame(): void {
    if (this.frame) {
      this.view.removeChild(this.frame)
      this.frame.destroy({ children: true })
    }
    this.frame = panelFrame(WIDTH, HEIGHT, t('ui.setup.title'), this.onClose,
                            undefined, false)
    this.view.addChildAt(this.frame, 0)
  }

  private rebuild(): void {
    this.drawGrid()
    this.drawStakes()
    this.drawSide()
  }

  /** 왼쪽의 15칸. 칸마다 그 덱의 뒷면이 섭니다. */
  private drawGrid(): void {
    this.grid.removeChildren().forEach(child => child.destroy({ children: true }))

    const head = new Text({
      text: t('ui.setup.decks'),
      style: { fontSize: 12, fill: COLOR.good, fontWeight: '800', letterSpacing: 1 },
    })
    head.position.set(GRID_X + 4, GRID_Y - 22)
    this.grid.addChild(head)

    for (let i = 0; i < this.decks.length; i++) {
      const row = this.data.tables.deck.findByDeckId(this.decks[i].deckId)
      if (!row) continue
      const here = i === this.deckAt

      const cell = new Container()
      cell.position.set(GRID_X + (i % COLUMNS) * CELL_W,
                        GRID_Y + Math.floor(i / COLUMNS) * CELL_H)

      const board = new Graphics()
      board.roundRect(0, 0, CELL_W - 10, CELL_H - 10, 8)
        .fill({ color: here ? 0x1d3a26 : 0x201f26 })
        .stroke({ color: here ? COLOR.good : 0x33313c, width: here ? 3 : 2 })
      cell.addChild(board)

      const back = new Graphics()
      back.position.set((CELL_W - 10 - BACK_W) / 2, 10)
      drawCardBack(back, BACK_W, BACK_H, Math.round(SIZE.cardRadius * 0.62),
                   backLookOf(row))
      cell.addChild(back)

      const name = new Text({
        text: this.decks[i].name,
        style: {
          fontSize: 12, fill: here ? COLOR.ink : COLOR.inkDim, fontWeight: '800',
          wordWrap: true, wordWrapWidth: CELL_W - 22, align: 'center', lineHeight: 14,
        },
      })
      name.anchor.set(0.5, 0)
      name.position.set((CELL_W - 10) / 2, 12 + BACK_H + 6)
      cell.addChild(name)

      cell.eventMode = 'static'
      cell.cursor = 'pointer'
      cell.on('pointertap', () => this.pick(i, this.stakeAt))
      this.grid.addChild(cell)
    }
  }

  /** 스테이크 여덟. 이름이 곧 색이므로 칸을 그 색으로 칠합니다. */
  private drawStakes(): void {
    this.stakes.removeChildren().forEach(child => child.destroy({ children: true }))

    const head = new Text({
      text: t('ui.setup.stakes'),
      style: { fontSize: 12, fill: COLOR.good, fontWeight: '800', letterSpacing: 1 },
    })
    head.position.set(GRID_X + 4, STAKE_Y - 20)
    this.stakes.addChild(head)

    for (let i = 0; i < this.stakeRows.length; i++) {
      const row = this.stakeRows[i]
      const here = i === this.stakeAt
      const tint = STAKE_COLOR[row.stake] ?? COLOR.inkDim

      const cell = new Container()
      cell.position.set(GRID_X + i * STAKE_W, STAKE_Y)

      const board = new Graphics()
      board.roundRect(0, 0, STAKE_W - 10, STAKE_H, 8)
        .fill({ color: 0x201f26 })
        .stroke({ color: here ? COLOR.good : 0x33313c, width: here ? 3 : 2 })
      // 색 조각 하나. **글자에 색을 입히지 않습니다** — 검은색과 흰색이 글자로는 배경에
      // 묻히고, 조각으로 두면 여덟이 같은 밝기로 읽힙니다.
      board.roundRect((STAKE_W - 10) / 2 - 13, 8, 26, 16, 4)
        .fill({ color: tint })
        .stroke({ color: 0x0d0d10, width: 1 })
      cell.addChild(board)

      // **두 줄까지 접힙니다.** 한국어의 이름은 색 하나(`흰색`)이지만 다른 말에는
      // 「스테이크」가 붙어(`ホワイトステーク` · `Weißer Einsatz`) 한 줄로는 칸을 넘칩니다.
      const name = new Text({
        text: row.name,
        style: {
          fontSize: 10, fill: here ? COLOR.ink : COLOR.inkDim, fontWeight: '800',
          wordWrap: true, wordWrapWidth: STAKE_W - 18, align: 'center', lineHeight: 12,
          // **글자 단위로 끊습니다.** 일본어와 중국어에는 공백이 없어 낱말 단위로만
          // 접으면 `ホワイトステーク` 가 한 줄로 남아 옆 칸을 덮습니다.
          breakWords: true,
        },
      })
      name.anchor.set(0.5, 0)
      name.position.set((STAKE_W - 10) / 2, 27)
      cell.addChild(name)

      cell.eventMode = 'static'
      cell.cursor = 'pointer'
      cell.on('pointertap', () => this.pick(this.deckAt, i))
      this.stakes.addChild(cell)
    }
  }

  private pick(deckAt: number, stakeAt: number): void {
    this.deckAt = deckAt
    this.stakeAt = stakeAt
    this.rebuild()
    // **고른 그 자리에서 저장합니다.** 판을 닫을 때 저장하면 판을 닫지 않고 시작한 판이
    // 다음 번에 다른 덱으로 열립니다.
    this.onPick?.(this.picked())
  }

  /** 오른쪽. 고른 덱의 시작 조건과 고른 스테이크의 규칙입니다. */
  private drawSide(): void {
    this.side.removeChildren().forEach(child => child.destroy({ children: true }))

    const board = new Graphics()
    board.roundRect(SIDE_X, GRID_Y, SIDE_W, SIDE_H, 10)
      .fill({ color: 0x1a1920 })
      .stroke({ color: 0x33313c, width: 2 })
    this.side.addChild(board)

    const deck = this.decks[this.deckAt]
    if (!deck) return
    let y = GRID_Y + 16

    const title = new Text({
      text: deck.name,
      style: { fontSize: 22, fill: COLOR.ink, fontWeight: '800' },
    })
    title.position.set(SIDE_X + 18, y)
    this.side.addChild(title)
    y += 36

    y = this.section(t('ui.setup.effects'), y)
    const rules = describe(this.data, this.data.deckEffects.get(deck.deckId) ?? [])
    const block = richBlock(rules.length > 0 ? rules : [t('ui.note.no_rules')],
                            RULE_STYLE, 19, SIDE_W - 36)
    block.position.set(SIDE_X + 18, y)
    this.side.addChild(block)
    y += rowsOf(block) * 19 + 20

    const stake = this.stakeRows[this.stakeAt]
    const record = this.data.tables.stake.findByStake(stake?.stake ?? StakeKind.White)
    if (!stake || !record) return

    y = this.section(t('ui.setup.stakes'), y)

    const name = new Text({
      text: stake.name,
      style: { fontSize: 16, fill: COLOR.ink, fontWeight: '800' },
    })
    name.position.set(SIDE_X + 18, y)
    this.side.addChild(name)
    y += 24

    // 스테이크의 규칙은 런 정보 패널과 같은 문장을 씁니다. 같은 것을 두 문장으로 적으면
    // 두 화면의 말이 갈립니다.
    const note = richBlock([tf('ui.stake.note', {
      column: record.anteColumn,
      reward: record.smallBlindReward,
      discards: record.discardsDelta,
    })], RULE_STYLE, 18, SIDE_W - 36)
    note.position.set(SIDE_X + 18, y)
    this.side.addChild(note)
  }

  /** 작은 제목 하나. 다음 글이 시작할 `y` 를 돌려줍니다. */
  private section(label: string, y: number): number {
    const head = new Text({
      text: label,
      style: { fontSize: 12, fill: COLOR.good, fontWeight: '800', letterSpacing: 1 },
    })
    head.position.set(SIDE_X + 18, y)
    this.side.addChild(head)
    return y + 20
  }

  /** 말이 바뀌었을 때. 판을 처음 세운 때의 말로 남지 않게 합니다. */
  relabel(): void {
    this.buildFrame()
    for (const one of this.decks) {
      const row = this.data.tables.deck.findByDeckId(one.deckId)
      if (row) one.name = nameOf(this.data, 'deck', one.deckId, row.name)
    }
    for (const one of this.stakeRows) {
      const row = this.data.tables.stake.findByStake(one.stake)
      if (row) one.name = nameOf(this.data, 'stake', stakeSlug(one.stake), row.name)
    }
    if (this.startButton) this.startButton.text = t('ui.setup.start')
    this.rebuild()
  }
}
