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
// **설명은 쪽지입니다.** 고른 것의 시작 조건과 스테이크 규칙을 오른쪽 칸에 늘 펼쳐 두었는데,
// 그 칸 하나가 판의 3분의 1을 차지하면서 격자와 나란히 서서 「어느 쪽을 보라는 화면인가」가
// 흐려졌습니다 — 판 안의 조커와 같은 규칙으로 바꾸었습니다. 가리킨 것의 설명이 그 자리에
// 뜨고, 아무것도 가리키지 않으면 화면에 격자만 남습니다.
//
// **잠긴 칸이 없습니다.** 결제가 없는 로그라이트에서 해금은 순수한 지연이므로 15덱과 8종을
// 처음부터 전부 엽니다 — `Deck.unlock` 은 표시용 문자열로 남아 있고 이 화면은 그것을 읽지
// 않습니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { describe } from '../core/describe'
import type { PoolChoice } from '../core/pool'
import { stakeSlug } from '../core/stake'
import { nameOf, t, tf } from '../core/strings'
import { StakeKind } from '../generated/enums/stake-kind'
import { backLookOf, drawCardBack } from '../render/card-back'
import { COLOR, SIZE, UI } from '../render/theme'
import type { ToolSpot } from './layout'
import type { TipRequest } from './run-panel'
import { Button } from './widgets'

const WIDTH = 760

/** 덱 격자. 15칸이므로 5 × 3 입니다. */
const COLUMNS = 5
const CELL_W = 132
const CELL_H = 118
const GRID_X = Math.round((WIDTH - COLUMNS * CELL_W) / 2)
const GRID_Y = 24

/** 칸 안의 뒷면. **손패보다 작지만 무늬가 읽히는 크기입니다.** */
const BACK_W = Math.round(SIZE.cardWidth * 0.62)
const BACK_H = Math.round(SIZE.cardHeight * 0.62)

/** 스테이크 줄. 덱 격자 아래에 한 줄로 섭니다. */
const STAKE_COUNT = 8
const STAKE_W = 80
const STAKE_H = 58
const STAKE_X = Math.round((WIDTH - STAKE_COUNT * STAKE_W) / 2)
const STAKE_HEAD_Y = GRID_Y + 3 * CELL_H + 14
const STAKE_Y = STAKE_HEAD_Y + 24

/**
 * 조커 풀 줄. 스테이크 아래에 단추 둘입니다.
 *
 * **런의 설정이므로 여기입니다.** 도감 안에 하나만 서 있었는데, 풀은 덱 · 스테이크와 같이
 * 판을 시작할 때 정해져 세이브와 제출에 함께 적히는 값입니다 — 무엇으로 시작하는가가 한
 * 자리에 모여 있어야 챌린지 탭이 기본 150종으로 고정인 이유도 그 옆에서 읽힙니다.
 */
const POOL_COUNT = 2
const POOL_W = 200
const POOL_H = 42
const POOL_GAP = 12
const POOL_X = Math.round((WIDTH - (POOL_COUNT * POOL_W + POOL_GAP)) / 2)
const POOL_HEAD_Y = STAKE_Y + STAKE_H + 14
const POOL_Y = POOL_HEAD_Y + 20

/** 단추 줄. 시작과 랭크가 나란히 섭니다. */
const START_W = 400
const RANKED_W = 160
const BTN_GAP = 12
const BTN_H = 48
const BTN_X = Math.round((WIDTH - (START_W + BTN_GAP + RANKED_W)) / 2)
const BTN_Y = POOL_Y + POOL_H + 18

const HEIGHT = BTN_Y + BTN_H

/**
 * 이 몸통의 높이.
 *
 * **판이 이것을 읽습니다.** 판에 수로 베껴 적어 두었더니 이 몸통에 한 줄을 더한 날부터
 * 시작 단추가 판의 밑변 아래에 그려졌고, 그 자리를 누르는 것은 판 바깥을 누르는 것이라
 * 판이 닫혔습니다.
 */
export const SETUP_HEIGHT = HEIGHT

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
  /**
   * 어느 조커 풀로 시작하는가.
   *
   * **기본이 `base` 입니다.** 켜진 채로 시작하면 원작을 기대한 사람이 모를 조커를 만나게
   * 되고, 굽어 둔 리플레이와도 어긋납니다.
   */
  pool: PoolChoice
}

export function defaultSetup(): RunSetup {
  return { deckId: 'red_deck', stake: StakeKind[StakeKind.White], pool: 'base' }
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
  const pool = setup.pool === 'all' ? 'all' : fallback.pool
  return { deckId, stake, pool }
}

/** 그 덱과 스테이크의 표시 이름을 한 줄로. 시작을 묻는 판이 이것을 적습니다. */
export function setupLabel(data: Data, setup: RunSetup): string {
  const deck = data.tables.deck.findByDeckId(setup.deckId)
  const deckName = deck
    ? nameOf(data, 'deck', setup.deckId, deck.name) : setup.deckId
  const row = data.tables.stake.records.find(one => StakeKind[one.stake] === setup.stake)
  const stakeName = row
    ? nameOf(data, 'stake', stakeSlug(row.stake), row.name) : setup.stake
  return `${deckName} · ${stakeName}`
}

export class SetupBody {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }
  /** 내용이 시작하는 자리. 「덱」이라고 적힌 작은 제목입니다. */
  readonly top = 0

  private readonly body = new Container()
  private readonly grid = new Container()
  private readonly stakes = new Container()

  private readonly decks: { deckId: string; name: string }[]
  private readonly stakeRows: { stake: StakeKind; name: string }[]

  private deckAt = 0
  private stakeAt = 0
  /** 지금 고른 조커 풀. */
  private pool: PoolChoice = 'base'
  private readonly poolButtons: { choice: PoolChoice; button: Button; key: string }[] = []
  private poolHead?: Text
  private startButton?: Button
  private rankedButton?: Button
  /** 로그인했는가. 랭크로 시작할 수 있는지가 이것으로 갈립니다. */
  private signedIn = false
  /** 지금 그려져 있는 칸들. **도구가 이것을 짚습니다** — 다시 그릴 때마다 갈아 끼웁니다. */
  private deckCells: Container[] = []
  private stakeCells: Container[] = []

  /** 고른 것이 바뀔 때마다 부릅니다. 저장하는 쪽이 받습니다. */
  onPick?: (setup: RunSetup) => void
  /** 고른 것으로 판을 엽니다. */
  onStart?: (setup: RunSetup) => void
  /**
   * 랭크로 시작합니다.
   *
   * **여기 있습니다.** 타이틀에 단추 하나로 서 있었는데, 서버가 준 시드로 연다는 것 말고는
   * 새 런과 같은 일이므로 판을 여는 자리에 함께 있어야 합니다.
   */
  onStartRanked?: () => void
  /** 무엇을 가리켰는지 알립니다. 쪽지를 띄우는 것은 판이 합니다. */
  onTip?: (tip: TipRequest | undefined) => void

  constructor(private readonly data: Data, setup: RunSetup) {
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
    this.pool = setup.pool
  }

  /** 지금 고른 것. */
  picked(): RunSetup {
    return {
      deckId: this.decks[this.deckAt]?.deckId ?? defaultSetup().deckId,
      stake: StakeKind[this.stakeRows[this.stakeAt]?.stake ?? StakeKind.White],
      pool: this.pool,
    }
  }

  /** 바깥에서 고른 것이 바뀌었을 때. 말이 바뀌어 판을 다시 세울 때도 씁니다. */
  setSetup(setup: RunSetup): void {
    this.point(validSetup(this.data, setup))
    this.rebuild()
  }

  private build(): void {
    this.view.addChild(this.body)
    this.body.addChild(this.grid, this.stakes)

    // 조커 풀. **두 갈래이므로 목록이 아니라 단추 둘입니다.**
    const poolHead = this.head(t('ui.pool.title'))
    poolHead.position.set(POOL_X + 4, POOL_HEAD_Y)
    this.body.addChild(poolHead)
    this.poolHead = poolHead

    for (const [index, choice] of (['base', 'all'] as PoolChoice[]).entries()) {
      const key = choice === 'all' ? 'ui.pool.all' : 'ui.pool.base'
      const button = new Button(t(key), POOL_W, POOL_H, UI.btn,
                                () => this.pickPool(choice), 16)
      button.position.set(POOL_X + index * (POOL_W + POOL_GAP), POOL_Y)
      // 무엇이 늘어나는지는 도감에서 봅니다. 여기서는 무엇으로 시작할지만 정합니다.
      button.on('pointerover', () => this.onTip?.({
        name: t(key),
        lines: [t(choice === 'all' ? 'ui.pool.allNote' : 'ui.pool.baseNote')],
        x: POOL_X + index * (POOL_W + POOL_GAP) + POOL_W / 2,
        top: POOL_Y,
        bottom: POOL_Y + POOL_H,
      }))
      button.on('pointerout', () => this.onTip?.(undefined))
      this.poolButtons.push({ choice, button, key })
      this.body.addChild(button)
    }

    this.startButton = new Button(t('ui.setup.start'), START_W, BTN_H, UI.yellow,
                                  () => this.onStart?.(this.picked()), 19)
    this.startButton.position.set(BTN_X, BTN_Y)

    // **랭크는 조용합니다.** 같은 색으로 같은 크기면 눌러야 하는 것이 둘로 보입니다 —
    // 이 화면에서 대개 누르는 것은 왼쪽의 하나입니다.
    this.rankedButton = new Button(t('ui.lb.ranked'), RANKED_W, BTN_H, UI.btn,
                                   () => this.onStartRanked?.(), 15)
    this.rankedButton.position.set(BTN_X + START_W + BTN_GAP, BTN_Y)

    // 랭크가 잠긴 이유는 **올렸을 때 적힙니다.** 단추 밑에 한 줄을 늘 두면 로그인한
    // 사람에게도 그 줄이 남고, 그러면 판의 아래가 한 줄 길어집니다.
    this.rankedButton.on('pointerover', () => {
      if (this.signedIn) return
      this.onTip?.({
        name: t('ui.lb.ranked'),
        lines: [t('ui.account.needLink')],
        x: BTN_X + START_W + BTN_GAP + RANKED_W / 2,
        top: BTN_Y,
        bottom: BTN_Y + BTN_H,
      })
    })
    this.rankedButton.on('pointerout', () => this.onTip?.(undefined))

    this.body.addChild(this.startButton, this.rankedButton)
    this.syncRanked()
  }

  /**
   * 도구가 짚을 자리들.
   *
   * **단추의 자리를 도구에 적어 두지 않기 위한 것입니다.** 이 몸통은 탭 안에 얹히므로
   * 화면에서의 자리가 탭 줄의 높이와 판의 크기를 따릅니다.
   */
  spots(): [string, ToolSpot][] {
    const out: [string, ToolSpot][] = []
    this.deckCells.forEach((cell, at) => {
      out.push([`deck:${at}`, { node: cell, cx: (CELL_W - 10) / 2, cy: (CELL_H - 10) / 2 }])
    })
    this.stakeCells.forEach((cell, at) => {
      out.push([`stake:${at}`, { node: cell, cx: (STAKE_W - 10) / 2, cy: STAKE_H / 2 }])
    })
    for (const one of this.poolButtons) {
      out.push([`pool:${one.choice}`,
                { node: one.button, cx: POOL_W / 2, cy: POOL_H / 2 }])
    }
    if (this.startButton) {
      out.push(['startNew', { node: this.startButton, cx: START_W / 2, cy: BTN_H / 2 }])
    }
    if (this.rankedButton) {
      out.push(['startRanked', { node: this.rankedButton, cx: RANKED_W / 2, cy: BTN_H / 2 }])
    }
    return out
  }

  /** 로그인 상태를 알립니다. 랭크로 시작할 수 있는지가 이것으로 갈립니다. */
  setSignedIn(signedIn: boolean): void {
    this.signedIn = signedIn
    this.syncRanked()
  }

  private syncRanked(): void {
    if (this.rankedButton) this.rankedButton.enabled = this.signedIn
  }

  private rebuild(): void {
    this.drawGrid()
    this.drawStakes()
    this.syncPool()
  }

  /**
   * 고른 풀을 단추에 얹습니다.
   *
   * **눌린 채로 두고 나머지를 흐리게 합니다.** 둘 중 하나만 하면 어두운 바탕에서 어느
   * 것이 고른 것인지가 눈에 들지 않습니다.
   */
  private syncPool(): void {
    for (const one of this.poolButtons) {
      one.button.text = t(one.key)
      const on = one.choice === this.pool
      one.button.highlight = on
      one.button.alpha = on ? 1 : 0.55
    }
    if (this.poolHead) this.poolHead.text = t('ui.pool.title')
  }

  private pickPool(choice: PoolChoice): void {
    if (this.pool === choice) return
    this.pool = choice
    this.syncPool()
    this.onPick?.(this.picked())
  }

  /** 작은 제목 하나. */
  private head(label: string): Text {
    return new Text({
      text: label,
      style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800', letterSpacing: 1 },
    })
  }

  /** 왼쪽의 15칸. 칸마다 그 덱의 뒷면이 섭니다. */
  private drawGrid(): void {
    this.grid.removeChildren().forEach(child => child.destroy({ children: true }))
    this.deckCells = []

    const head = this.head(t('ui.setup.decks'))
    head.position.set(GRID_X + 4, 0)
    this.grid.addChild(head)

    for (let i = 0; i < this.decks.length; i++) {
      const row = this.data.tables.deck.findByDeckId(this.decks[i].deckId)
      if (!row) continue
      const here = i === this.deckAt

      const cell = new Container()
      const cx = GRID_X + (i % COLUMNS) * CELL_W
      const cy = GRID_Y + Math.floor(i / COLUMNS) * CELL_H
      cell.position.set(cx, cy)

      // **칸의 바탕은 겉면의 것입니다.** 색을 손으로 적어 두었더니 겉면을 갈아입어도 이
      // 격자만 앞 겉면의 색으로 남았습니다 — 고른 것의 파랑만 약속된 색이므로 고정입니다.
      const board = new Graphics()
      board.roundRect(0, 0, CELL_W - 10, CELL_H - 10, 8)
        .fill({ color: UI.cell })
        .stroke({ color: here ? UI.pick : UI.hairline, width: here ? 2 : 1.5 })
      if (here) {
        board.roundRect(0, 0, CELL_W - 10, CELL_H - 10, 8)
          .fill({ color: UI.pick, alpha: 0.22 })
      }
      cell.addChild(board)

      const back = new Container()
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
      // **손가락에는 마우스 오버가 없으므로 누르는 것도 쪽지를 띄웁니다.** 조커 도감이
      // 같은 규칙입니다.
      cell.on('pointerover', () => this.tipDeck(i, cx, cy))
      cell.on('pointertap', () => this.tipDeck(i, cx, cy))
      cell.on('pointerout', () => this.onTip?.(undefined))
      this.grid.addChild(cell)
      this.deckCells.push(cell)
    }
  }

  /** 그 덱의 시작 조건을 쪽지로. */
  private tipDeck(index: number, cx: number, cy: number): void {
    const deck = this.decks[index]
    if (!deck) return
    const rules = describe(this.data, this.data.deckEffects.get(deck.deckId) ?? [])
    this.onTip?.({
      name: deck.name,
      lines: rules.length > 0 ? rules : [t('ui.note.no_rules')],
      x: cx + (CELL_W - 10) / 2,
      top: cy,
      bottom: cy + CELL_H - 10,
    })
  }

  /** 스테이크 여덟. 이름이 곧 색이므로 칸을 그 색으로 칠합니다. */
  private drawStakes(): void {
    this.stakes.removeChildren().forEach(child => child.destroy({ children: true }))
    this.stakeCells = []

    const head = this.head(t('ui.setup.stakes'))
    head.position.set(STAKE_X + 4, STAKE_HEAD_Y)
    this.stakes.addChild(head)

    for (let i = 0; i < this.stakeRows.length; i++) {
      const row = this.stakeRows[i]
      const here = i === this.stakeAt
      const tint = STAKE_COLOR[row.stake] ?? COLOR.inkDim

      const cell = new Container()
      const cx = STAKE_X + i * STAKE_W
      cell.position.set(cx, STAKE_Y)

      const board = new Graphics()
      board.roundRect(0, 0, STAKE_W - 10, STAKE_H, 8)
        .fill({ color: UI.cell })
        .stroke({ color: here ? UI.pick : UI.hairline, width: here ? 2 : 1.5 })
      if (here) {
        board.roundRect(0, 0, STAKE_W - 10, STAKE_H, 8)
          .fill({ color: UI.pick, alpha: 0.22 })
      }
      // 색 조각 하나. **글자에 색을 입히지 않습니다** — 검은색과 흰색이 글자로는 배경에
      // 묻히고, 조각으로 두면 여덟이 같은 밝기로 읽힙니다.
      board.roundRect((STAKE_W - 10) / 2 - 13, 8, 26, 16, 4)
        .fill({ color: tint })
        .stroke({ color: UI.ink, width: 1 })
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
      cell.on('pointerover', () => this.tipStake(i, cx))
      cell.on('pointertap', () => this.tipStake(i, cx))
      cell.on('pointerout', () => this.onTip?.(undefined))
      this.stakes.addChild(cell)
      this.stakeCells.push(cell)
    }
  }

  /** 그 스테이크의 규칙을 쪽지로. **런 정보 판과 같은 문장입니다.** */
  private tipStake(index: number, cx: number): void {
    const row = this.stakeRows[index]
    if (!row) return
    const record = this.data.tables.stake.findByStake(row.stake)
    if (!record) return
    this.onTip?.({
      name: row.name,
      lines: [tf('ui.stake.note', {
        column: record.anteColumn,
        reward: record.smallBlindReward,
        discards: record.discardsDelta,
      })],
      x: cx + (STAKE_W - 10) / 2,
      top: STAKE_Y,
      bottom: STAKE_Y + STAKE_H,
    })
  }

  private pick(deckAt: number, stakeAt: number): void {
    this.deckAt = deckAt
    this.stakeAt = stakeAt
    this.rebuild()
    // **고른 그 자리에서 저장합니다.** 판을 닫을 때 저장하면 판을 닫지 않고 시작한 판이
    // 다음 번에 다른 덱으로 열립니다.
    this.onPick?.(this.picked())
  }

  /** 말이 바뀌었을 때. 판을 처음 세운 때의 말로 남지 않게 합니다. */
  relabel(): void {
    for (const one of this.decks) {
      const row = this.data.tables.deck.findByDeckId(one.deckId)
      if (row) one.name = nameOf(this.data, 'deck', one.deckId, row.name)
    }
    for (const one of this.stakeRows) {
      const row = this.data.tables.stake.findByStake(one.stake)
      if (row) one.name = nameOf(this.data, 'stake', stakeSlug(one.stake), row.name)
    }
    if (this.startButton) this.startButton.text = t('ui.setup.start')
    if (this.rankedButton) this.rankedButton.text = t('ui.lb.ranked')
    this.rebuild()
  }
}
