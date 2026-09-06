// 챌린지 고르기.
//
// **20칸이 한 화면에 다 들어갑니다.** 조커 500종과 달리 쪽을 넘길 것이 없으므로, 격자가
// 목록이 아니라 판 하나입니다 — 무엇이 열려 있고 무엇이 남았는지가 한눈에 보여야 해금이
// 순서라는 것이 읽힙니다.
//
// **규칙 글은 데이터에서 나옵니다.** `describe()` 가 효과 행을 문장으로 만드므로, 손으로
// 적어 둔 설명문이 없고 데이터와 어긋날 자리가 없습니다.
//
// **설명은 쪽지입니다.** 고른 것의 규칙과 금지 목록을 오른쪽 칸에 늘 펼쳐 두었는데, 그 칸
// 하나가 판의 3분의 1을 차지하면서 격자와 나란히 서서 「어느 쪽을 보라는 화면인가」가
// 흐려졌습니다 — 덱 고르기와 같은 규칙으로 바꾸었습니다.
//
// 잠긴 칸은 이름과 순서만 보입니다. 규칙을 미리 보여 주면 해금이 순서라는 것이 무효가
// 됩니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { nameOf, t, tf } from '../core/strings'
import { COLOR, UI } from '../render/theme'
import type { ToolSpot } from './layout'
import type { TipRequest } from './run-panel'
import { Button } from './widgets'

const WIDTH = 760

/** 격자. 20칸이므로 5 × 4 입니다. */
const COLUMNS = 5
const CELL_W = 118
const CELL_H = 92
const GRID_X = Math.round((WIDTH - COLUMNS * CELL_W) / 2)
const GRID_Y = 0

/**
 * 시작 단추. **격자 아래에 혼자 섭니다.**
 *
 * **새 런 탭의 단추와 같은 줄입니다.** 탭을 바꿀 때 누를 것이 위아래로 움직이면 두 탭이
 * 다른 판으로 보입니다 — 판의 높이는 가장 높은 몸통이 정하므로 그 밑변에 맞춥니다.
 */
const BTN_W = 300
const BTN_H = 48
const BTN_X = Math.round((WIDTH - BTN_W) / 2)
const BTN_Y = 500

const HEIGHT = BTN_Y + BTN_H

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
      beaten: Array.isArray(found.beaten)
        ? found.beaten.filter(one => typeof one === 'string') : [],
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

export class ChallengeBody {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }
  /** 내용이 시작하는 자리. 20칸의 윗변입니다. */
  readonly top = GRID_Y

  private readonly body = new Container()
  private readonly grid = new Container()

  private readonly rows: { challengeId: string; sortOrder: number; name: string }[]
  private picked = 0
  private start?: Button
  /** 지금 그려져 있는 칸들. **도구가 이것을 짚습니다** — 다시 그릴 때마다 갈아 끼웁니다. */
  private cells: Container[] = []

  /** 고른 챌린지로 판을 엽니다. */
  onStart?: (challengeId: string) => void
  /** 무엇을 가리켰는지 알립니다. 쪽지를 띄우는 것은 판이 합니다. */
  onTip?: (tip: TipRequest | undefined) => void

  constructor(private readonly data: Data,
              private readonly progress: ChallengeProgress) {
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
    this.view.addChild(this.body)
    this.body.addChild(this.grid)

    this.start = new Button(t('ui.challenge.start'), BTN_W, BTN_H, UI.yellow,
                            () => this.fire(), 19)
    this.start.position.set(BTN_X, BTN_Y)
    this.body.addChild(this.start)
  }

  /** 도구가 짚을 자리들. 20칸과 시작 단추입니다. */
  spots(): [string, ToolSpot][] {
    const out: [string, ToolSpot][] = this.cells.map((cell, at) =>
      [`cell:${at}`, { node: cell, cx: (CELL_W - 10) / 2, cy: (CELL_H - 10) / 2 }])
    if (this.start) out.push(['startChallenge', { node: this.start, cx: BTN_W / 2, cy: BTN_H / 2 }])
    return out
  }

  /** 지금 고른 것. 잠겨 있으면 빈 문자열입니다. */
  pickedId(): string {
    const row = this.rows[this.picked]
    return row && this.isOpen(this.picked) ? row.challengeId : ''
  }

  private fire(): void {
    const id = this.pickedId()
    if (id === '') return
    this.onStart?.(id)
  }

  private isOpen(index: number): boolean {
    return index < openCount(this.progress)
  }

  private rebuild(): void {
    this.drawGrid()
    if (this.start) this.start.enabled = this.isOpen(this.picked)
  }

  /** 20칸. 깬 것 · 열린 것 · 잠긴 것 셋으로 갈립니다. */
  private drawGrid(): void {
    this.grid.removeChildren().forEach(child => child.destroy({ children: true }))
    this.cells = []

    for (let i = 0; i < this.rows.length; i++) {
      const row = this.rows[i]
      const open = this.isOpen(i)
      const beaten = this.progress.beaten.includes(row.challengeId)
      const here = i === this.picked

      const cell = new Container()
      const cx = GRID_X + (i % COLUMNS) * CELL_W
      const cy = GRID_Y + Math.floor(i / COLUMNS) * CELL_H
      cell.position.set(cx, cy)

      const board = new Graphics()
      board.roundRect(0, 0, CELL_W - 10, CELL_H - 10, 8)
        .fill({ color: UI.cell })
        .stroke({
          color: here ? UI.pick : beaten ? UI.green : UI.hairline,
          width: here ? 2 : 1.5,
        })
      cell.addChild(board)

      const order = new Text({
        text: String(row.sortOrder),
        style: { fontSize: 12, fill: open ? COLOR.inkDim : UI.locked, fontWeight: '800' },
      })
      order.position.set(8, 6)
      cell.addChild(order)

      // **잠긴 칸에도 이름은 적힙니다.** 이름까지 가리면 무엇이 남았는지 셀 수 없습니다.
      const name = new Text({
        text: row.name,
        style: {
          fontSize: 13, fill: open ? COLOR.ink : UI.locked, fontWeight: '800',
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
          style: { fontSize: 15, fill: UI.green, fontWeight: '800' },
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
        this.tip(i, cx, cy)
      })
      // **손가락에는 마우스 오버가 없으므로 누르는 것도 쪽지를 띄웁니다.**
      cell.on('pointerover', () => this.tip(i, cx, cy))
      cell.on('pointerout', () => this.onTip?.(undefined))
      this.grid.addChild(cell)
      this.cells.push(cell)
    }
  }

  /**
   * 그 챌린지의 규칙을 쪽지로.
   *
   * **잠긴 것의 규칙은 가려집니다.** 미리 보여 주면 해금이 순서라는 것이 무효가 됩니다 —
   * 무엇으로 열리는지만 적힙니다.
   */
  private tip(index: number, cx: number, cy: number): void {
    const row = this.rows[index]
    if (!row) return

    const at = { x: cx + (CELL_W - 10) / 2, top: cy, bottom: cy + CELL_H - 10 }
    const open = this.isOpen(index)
    const none = openCount(this.progress) === 0

    if (!open) {
      this.onTip?.({
        name: row.name,
        chip: t('ui.challenge.chip.locked'),
        chipTone: UI.locked,
        // **`Challenge.unlock` 을 적지 않습니다.** 그 칸은 기획자가 시트에서 읽는 글이고
        // 한국어 하나뿐이며, 여는 조건은 `openCount` 가 정합니다 — 시트의 글과 실제 조건이
        // 어긋날 수도 있습니다.
        lines: [none ? t('ui.challenge.lockedAll') : t('ui.challenge.locked')],
        ...at,
      })
      return
    }

    const beaten = this.progress.beaten.includes(row.challengeId)
    const lines: string[] = []
    const rules = describe(this.data, this.data.challengeEffects.get(row.challengeId) ?? [])
    lines.push(...(rules.length > 0 ? rules : [t('ui.challenge.noRules')]))

    // 금지 목록은 갈래마다 몇 개인지만 적습니다. 이름을 전부 적으면 `bare_field` 하나가
    // 쪽지를 넘어갑니다 — 그 칸에서 알아야 하는 것은 「무엇이 막혔는가」입니다.
    const bans = this.data.tables.challengeBan.records
      .filter(one => one.owner === row.challengeId)
    if (bans.length > 0) {
      const kinds = new Map<number, number>()
      for (const one of bans) kinds.set(one.kind, (kinds.get(one.kind) ?? 0) + 1)
      for (const [kind, count] of kinds) {
        lines.push(tf('ui.challenge.banLine', { what: t(BAN_KEYS[kind] ?? ''), n: count }))
      }
    }

    // **조커 풀이 고정이라는 것이 적혀 있어야 합니다.** 확장을 켜 둔 사람이 확장 조커를
    // 못 보게 되고, 아무 말이 없으면 그것이 고장으로 보입니다.
    lines.push(t('ui.challenge.basePool'))

    this.onTip?.({
      name: row.name,
      chip: beaten ? t('ui.challenge.chip.beaten') : t('ui.challenge.chip.open'),
      chipTone: beaten ? UI.green : UI.pick,
      lines,
      ...at,
    })
  }

  /** 말이 바뀌었을 때. 판을 처음 세운 때의 말로 남지 않게 합니다. */
  relabel(): void {
    for (const one of this.rows) {
      const row = this.data.tables.challenge.findByChallengeId(one.challengeId)
      if (row) one.name = nameOf(this.data, 'challenge', one.challengeId, row.name)
    }
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
