// 챌린지 고르기.
//
// **20칸이 한 화면에 다 들어갑니다.** 조커 500종과 달리 쪽을 넘길 것이 없으므로, 격자가
// 목록이 아니라 판 하나입니다 — 무엇이 열려 있고 무엇이 남았는지가 한눈에 보여야 해금이
// 순서라는 것이 읽힙니다.
//
// **규칙 글은 데이터에서 나옵니다.** `describe()` 가 효과 행을 문장으로 만들므로, 손으로
// 적어 둔 설명문이 없고 데이터와 어긋날 자리가 없습니다.
//
// 잠긴 칸은 이름과 순서만 보입니다. 규칙을 미리 보여 주면 해금이 순서라는 것이 무효가
// 됩니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { nameOf, t, tf } from '../core/strings'
import { COLOR, SIZE, UI } from '../render/theme'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { richBlock, rowsOf, type RichStyle } from './rich'
import { Button } from './widgets'

const WIDTH = 1180
const HEIGHT = 744

/** 격자. 20칸이므로 5 × 4 입니다. */
const COLUMNS = 5
const CELL_W = 118
const CELL_H = 92
const GRID_X = 34
const GRID_Y = 108

/** 오른쪽의 설명 자리. */
const SIDE_X = GRID_X + COLUMNS * CELL_W + 22
const SIDE_W = WIDTH - SIDE_X - 30
const SIDE_H = HEIGHT - GRID_Y - 96

// **`wordWrap` 을 켜지 않습니다.** 접는 것은 `place()` 가 `maxWidth` 로 하고, Pixi 쪽에도
// 켜 두면 글 조각 하나가 자기 기본 폭에서 한 번 더 접혀 `place()` 가 그것을 한 줄로 셉니다 —
// 줄이 겹쳐 그려진 원인이 그것이었습니다.
const RULE_STYLE: RichStyle = {
  base: { fontSize: 13, fill: COLOR.ink },
  number: COLOR.money,
  term: COLOR.good,
}

/** 깬 챌린지의 목록. **판이 아니라 저장이 가지는 값입니다.** */
export interface ChallengeProgress {
  /** 깬 것들의 식별자. */
  beaten: string[]
  /** 챌린지가 열렸는가. 원작은 덱 5종으로 이겨야 열립니다. */
  unlocked: boolean
}

export function defaultProgress(): ChallengeProgress {
  return { beaten: [], unlocked: false }
}

const KEY = 'clover.challenge'

export function loadProgress(): ChallengeProgress {
  try {
    const raw = localStorage.getItem(KEY)
    if (raw === null) return defaultProgress()
    const found = JSON.parse(raw) as Partial<ChallengeProgress>
    return {
      beaten: Array.isArray(found.beaten) ? found.beaten.filter(one => typeof one === 'string') : [],
      unlocked: found.unlocked === true,
    }
  } catch {
    // 저장을 읽지 못하는 곳이 있습니다 — 사생활 보호 창이나 저장을 막은 브라우저입니다.
    return defaultProgress()
  }
}

export function saveProgress(progress: ChallengeProgress): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(progress))
  } catch {
    // 저장하지 못해도 판은 돌아야 합니다.
  }
}

/**
 * 몇 번째까지 열렸는가.
 *
 * **앞의 것을 깨야 다음이 열립니다.** 처음 다섯이 함께 열리고, 하나를 깰 때마다 하나가
 * 더 열립니다 — 원작의 규칙입니다.
 */
export function openCount(progress: ChallengeProgress): number {
  if (!progress.unlocked) return 0
  return Math.min(20, 5 + progress.beaten.length)
}

export class ChallengePanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }

  private readonly body = new Container()
  private readonly grid = new Container()
  private readonly side = new Container()

  private readonly rows: { challengeId: string; sortOrder: number; name: string }[]
  private picked = 0
  private frame?: Container
  private start?: Button

  /** 고른 챌린지로 판을 엽니다. */
  onStart?: (challengeId: string) => void

  constructor(private readonly data: Data,
              private readonly progress: ChallengeProgress,
              private readonly onClose: () => void) {
    this.rows = data.tables.challenge.records
      .map(row => ({
        challengeId: row.challengeId,
        sortOrder: row.sortOrder,
        name: nameOf(data, 'challenge', row.challengeId, row.name),
      }))
      .sort((one, two) => one.sortOrder - two.sortOrder)

    this.build()
    this.rebuild()
  }

  private build(): void {
    this.buildFrame()
    this.view.addChild(this.body)
    this.body.addChild(this.grid, this.side)

    // **시작은 판의 아래에 혼자 섭니다.** 격자 옆에 두면 칸 하나를 누르는 것과 판을 여는
    // 것이 같은 무게로 보입니다.
    const bw = 240
    this.start = new Button(t('ui.challenge.start'), bw, 46, UI.yellow,
                            () => this.fire(), 18)
    this.start.position.set(SIDE_X + (SIDE_W - bw) / 2, GRID_Y + SIDE_H + 16)
    this.body.addChild(this.start)
  }

  private buildFrame(): void {
    if (this.frame) {
      this.view.removeChild(this.frame)
      this.frame.destroy({ children: true })
    }
    this.frame = panelFrame(WIDTH, HEIGHT, t('ui.challenge.title'), this.onClose,
                            undefined, false)
    this.view.addChildAt(this.frame, 0)
  }

  private fire(): void {
    const row = this.rows[this.picked]
    if (!row || !this.isOpen(this.picked)) return
    this.onStart?.(row.challengeId)
  }

  private isOpen(index: number): boolean {
    return index < openCount(this.progress)
  }

  private rebuild(): void {
    this.drawGrid()
    this.drawSide()
    if (this.start) this.start.enabled = this.isOpen(this.picked)
  }

  /** 왼쪽의 20칸. 깬 것 · 열린 것 · 잠긴 것 셋으로 갈립니다. */
  private drawGrid(): void {
    this.grid.removeChildren().forEach(child => child.destroy({ children: true }))

    for (let i = 0; i < this.rows.length; i++) {
      const row = this.rows[i]
      const open = this.isOpen(i)
      const beaten = this.progress.beaten.includes(row.challengeId)
      const here = i === this.picked

      const cell = new Container()
      cell.position.set(GRID_X + (i % COLUMNS) * CELL_W,
                        GRID_Y + Math.floor(i / COLUMNS) * CELL_H)

      const board = new Graphics()
      board.roundRect(0, 0, CELL_W - 10, CELL_H - 10, 8)
        .fill({ color: beaten ? 0x1d3a26 : open ? 0x201f26 : 0x17161b })
        .stroke({ color: here ? COLOR.good : beaten ? 0x2f8f52 : 0x33313c, width: here ? 3 : 2 })
      cell.addChild(board)

      const order = new Text({
        text: String(row.sortOrder),
        style: { fontSize: 12, fill: open ? COLOR.inkDim : 0x4a4854, fontWeight: '800' },
      })
      order.position.set(8, 6)
      cell.addChild(order)

      // **잠긴 칸에도 이름은 적힙니다.** 이름까지 가리면 무엇이 남았는지 셀 수 없습니다.
      const name = new Text({
        text: row.name,
        style: {
          fontSize: 13, fill: open ? COLOR.ink : 0x5a5866, fontWeight: '800',
          wordWrap: true, wordWrapWidth: CELL_W - 26, align: 'center',
          lineHeight: 16,
        },
      })
      name.anchor.set(0.5, 0.5)
      name.position.set((CELL_W - 10) / 2, (CELL_H - 10) / 2 + 6)
      cell.addChild(name)

      if (beaten) {
        const mark = new Text({
          text: '✓',
          style: { fontSize: 15, fill: COLOR.good, fontWeight: '800' },
        })
        mark.anchor.set(1, 0)
        mark.position.set(CELL_W - 18, 4)
        cell.addChild(mark)
      }

      cell.eventMode = 'static'
      cell.cursor = 'pointer'
      cell.on('pointertap', () => {
        this.picked = i
        this.rebuild()
      })
      this.grid.addChild(cell)
    }
  }

  /** 오른쪽. 고른 챌린지의 규칙과 시작 소지품과 금지 목록입니다. */
  private drawSide(): void {
    this.side.removeChildren().forEach(child => child.destroy({ children: true }))

    const board = new Graphics()
    board.roundRect(SIDE_X, GRID_Y, SIDE_W, SIDE_H, 10)
      .fill({ color: 0x1a1920 })
      .stroke({ color: 0x33313c, width: 2 })
    this.side.addChild(board)

    const row = this.rows[this.picked]
    if (!row) return

    const open = this.isOpen(this.picked)
    let y = GRID_Y + 16

    const title = new Text({
      text: row.name,
      style: { fontSize: 22, fill: COLOR.ink, fontWeight: '800' },
    })
    title.position.set(SIDE_X + 18, y)
    this.side.addChild(title)
    y += 34

    const record = this.data.tables.challenge.findByChallengeId(row.challengeId)
    // **아무것도 안 열렸으면 그 사실이 먼저입니다.** 칸마다의 해금 조건을 적어도 첫
    // 하나가 무엇으로 열리는지는 어디에도 없습니다.
    const none = openCount(this.progress) === 0
    const unlock = new Text({
      text: this.progress.beaten.includes(row.challengeId)
        ? t('ui.challenge.beaten')
        : open ? t('ui.challenge.open')
        : none ? t('ui.challenge.lockedAll') : (record?.unlock ?? ''),
      style: {
        fontSize: 13, fill: open ? COLOR.good : COLOR.inkDim, fontWeight: '700',
        wordWrap: true, wordWrapWidth: SIDE_W - 36, lineHeight: 18,
      },
    })
    unlock.position.set(SIDE_X + 18, y)
    this.side.addChild(unlock)
    y += 28

    // **잠긴 것의 규칙은 가려집니다.** 미리 보여 주면 해금이 순서라는 것이 무효가 됩니다.
    if (!open) {
      if (none) return
      const veil = new Text({
        text: t('ui.challenge.locked'),
        style: {
          fontSize: 14, fill: COLOR.inkDim, wordWrap: true,
          wordWrapWidth: SIDE_W - 36, lineHeight: 20,
        },
      })
      veil.position.set(SIDE_X + 18, y + 10)
      this.side.addChild(veil)
      return
    }

    y = this.section(t('ui.challenge.rules'), y)
    const rules = describe(this.data, this.data.challengeEffects.get(row.challengeId) ?? [])
    const block = richBlock(rules.length > 0 ? rules : [t('ui.challenge.noRules')],
                            RULE_STYLE, 19, SIDE_W - 36)
    block.position.set(SIDE_X + 18, y)
    this.side.addChild(block)
    y += rowsOf(block) * 19 + 18

    // 금지 목록은 갈래마다 몇 개인지만 적습니다. 이름을 전부 적으면 `bare_field` 하나가
    // 판을 넘어갑니다 — 그 칸에서 알아야 하는 것은 「무엇이 막혔는가」입니다.
    const bans = this.data.tables.challengeBan.records
      .filter(one => one.owner === row.challengeId)
    if (bans.length > 0) {
      y = this.section(t('ui.challenge.bans'), y)
      const kinds = new Map<number, number>()
      for (const one of bans) kinds.set(one.kind, (kinds.get(one.kind) ?? 0) + 1)
      const lines = [...kinds.entries()].map(([kind, count]) =>
        tf('ui.challenge.banLine', { what: t(BAN_KEYS[kind] ?? ''), n: count }))
      const list = richBlock(lines, RULE_STYLE, 18, SIDE_W - 36)
      list.position.set(SIDE_X + 18, y)
      this.side.addChild(list)
      y += rowsOf(list) * 18 + 14
    }

    // **조커 풀이 고정이라는 것이 적혀 있어야 합니다.** 확장을 켜 둔 사람이 확장 조커를
    // 못 보게 되고, 아무 말이 없으면 그것이 고장으로 보입니다.
    const note = new Text({
      text: t('ui.challenge.basePool'),
      style: {
        fontSize: 12, fill: COLOR.inkDim, wordWrap: true, wordWrapWidth: SIDE_W - 36,
        lineHeight: 16,
      },
    })
    note.position.set(SIDE_X + 18, GRID_Y + SIDE_H - 40)
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
    if (this.start) this.start.text = t('ui.challenge.start')
    this.rebuild()
  }
}

/** `BanKind` 의 값마다 어느 낱말인가. 표의 순서가 아니라 값이 열쇠입니다. */
const BAN_KEYS: Record<number, string> = {
  1: 'ui.challenge.banJoker',
  2: 'ui.challenge.banVoucher',
  3: 'ui.challenge.banTarot',
  4: 'ui.challenge.banPlanet',
  5: 'ui.challenge.banSpectral',
  6: 'ui.challenge.banTag',
  7: 'ui.challenge.banPack',
  8: 'ui.challenge.banBoss',
}

/** 판을 화면 가운데에 두는 데 쓰는 값. `SIZE` 를 읽는 자리를 한 곳으로 둡니다. */
export const CHALLENGE_PANEL = { width: WIDTH, height: HEIGHT, screen: SIZE }
