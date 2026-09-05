// 판을 여는 자리.
//
// **시작을 누르면 여기가 열립니다.** 무엇으로 시작하는가가 화면 여럿에 흩어져 있었습니다 —
// 타이틀에 「덱과 스테이크」 단추가 하나, 「챌린지」 단추가 하나, 「랭크」 단추가 하나였고,
// 셋 다 판을 여는 일인데 서로 다른 자리에 있었습니다. 하나로 모으면 무엇으로 시작할지를
// 고르는 자리가 하나입니다.
//
// **탭 셋입니다.**
//
// |탭|언제|무엇|
// |--|--|--|
// |새 런|늘|덱과 스테이크를 고르고 시작합니다|
// |이어하기|저장된 판이 있을 때|그만둔 자리와 「이어서 하기」·「버리기」|
// |챌린지|늘. 열리기 전에는 잠긴 채로|20칸에서 하나를 골라 시작합니다|
//
// **몸통은 저마다 자기 좌표로 그립니다.** 판이 그것을 받아 가로 가운데에 놓고 내용의
// 윗변을 탭 줄 아래에 맞춥니다 — 몸통이 자기 자리를 알면 탭 줄의 높이를 고칠 때마다
// 몸통 셋을 함께 고쳐야 합니다.
//
// **설명 쪽지는 판이 하나만 가집니다.** 몸통마다 자기 쪽지를 두면 화면에 둘이 뜰 수 있고,
// 쪽지가 판 밖으로 나가지 않게 하는 셈이 몸통마다 달라집니다 — 몸통은 「무엇을 가리켰다」만
// 알리고, 어디에 어떻게 띄우는지는 판이 정합니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import type { SavedRun } from '../core/save-run'
import { nameOf, t, tf } from '../core/strings'
import { StakeKind } from '../generated/enums/stake-kind'
import { stakeSlug } from '../core/stake'
import { COLOR, UI } from '../render/theme'
import { ChallengeBody, openCount, type ChallengeProgress } from './challenge'
import type { ToolSpot } from './layout'
import { panelFrame, TITLE_BAR, type ModalPanel } from './modal'
import { SetupBody, SETUP_HEIGHT, type RunSetup } from './setup'
import { Tooltip } from './tooltip'
import { Button } from './widgets'

const WIDTH = 760

/** 탭 줄. 제목 아래에 섭니다. */
const TAB_Y = TITLE_BAR + 12
const TAB_H = 40
const TAB_W = 168
const TAB_GAP = 6

/** 몸통의 내용이 시작하는 자리. */
const BODY_TOP = TAB_Y + TAB_H + 22

/**
 * 가장 높은 몸통은 새 런입니다. **판의 높이가 탭마다 바뀌면 밑변이 움직입니다.**
 *
 * **그 몸통에게 묻습니다.** 수로 베껴 적어 두면 몸통에 한 줄을 더한 날부터 그 줄이 판의
 * 밑변 아래에 그려지고, 거기를 누르는 것은 판 바깥을 누르는 것이라 판이 닫힙니다.
 */
const BODY_H = SETUP_HEIGHT
const HEIGHT = BODY_TOP + BODY_H + 26

/** 탭 하나의 이름. */
export type RunTab = 'new' | 'resume' | 'challenge'

/**
 * 무엇을 가리켰는가.
 *
 * 좌표는 **그 몸통의 지역 좌표**입니다. 판이 몸통의 자리를 더해 자기 좌표로 옮깁니다 —
 * 몸통이 자기가 어디에 얹혔는지 알면, 탭 줄의 높이를 고칠 때마다 몸통 셋을 함께 고쳐야
 * 합니다.
 */
export interface TipRequest {
  name: string
  lines: string[]
  /**
   * 이름 옆에 서는 칩.
   *
   * **되풀이되는 한 낱말은 여기입니다.** 「열려 있습니다」·「깼습니다」를 글의 첫 줄로 적으면
   * 어느 칸을 가리켜도 같은 문장이 먼저 읽히고, 정작 규칙이 아래로 밀립니다.
   */
  chip?: string
  chipTone?: number
  x: number
  top: number
  bottom: number
}

/**
 * 탭 하나의 몸통.
 *
 * `top` 은 그 몸통 안에서 내용이 시작하는 `y` 입니다. 판이 그만큼 끌어올려 놓으므로 셋의
 * 윗변이 같습니다.
 */
interface TabBody {
  readonly view: Container
  readonly size: { width: number; height: number }
  readonly top: number
  relabel(): void
}

export interface RunPanelHooks {
  onClose: () => void
  /** 고른 덱과 스테이크로 새 판을 엽니다. **묻는 것은 부르는 쪽이 합니다.** */
  onStartNew: (setup: RunSetup) => void
  /** 고른 것이 바뀌었습니다. 저장하는 쪽이 받습니다. */
  onPickSetup: (setup: RunSetup) => void
  /** 랭크로 시작합니다. 로그인 상태에서만 눌립니다. */
  onStartRanked: () => void
  /** 저장된 판을 이어서 합니다. */
  onResume: () => void
  /** 저장된 판을 버립니다. */
  onDiscard: () => void
  /** 고른 챌린지로 판을 엽니다. */
  onStartChallenge: (challengeId: string) => void
}

export class RunPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: HEIGHT }
  /**
   * 가로 가운데에 놓입니다.
   *
   * **왼쪽 판을 비껴 서지 않습니다.** 그 규칙은 판이 도는 동안 왼쪽에 있는 것을 가리지
   * 않기 위한 것이고, 이 판은 타이틀에서만 열리므로 비껴 설 대상이 없습니다 — 비껴 서면
   * 가운데에서 오른쪽으로 밀린 자리에 섭니다.
   */
  readonly centered = true

  private readonly tabRow = new Container()
  private readonly bodyLayer = new Container()
  private readonly tip = new Tooltip()
  private frame?: Container

  private readonly setupBody: SetupBody
  private readonly challengeBody: ChallengeBody
  private readonly resumeBody: ResumeBody

  private tab: RunTab = 'new'
  private saved?: SavedRun

  /** 도구가 짚을 자리들. **탭이 늘거나 폭이 바뀌면 여기서 함께 옮겨 갑니다.** */
  private readonly toolNodes = new Map<string, ToolSpot>()

  get toolSpots(): [string, ToolSpot][] {
    // **몸통이 그린 자리도 함께 알립니다.** 이어하기의 단추 둘은 저장된 판이 있을 때만
    // 그려지므로, 판이 스스로 세면 그것이 없는 날에 빈자리를 가리킵니다.
    const out: [string, ToolSpot][] = [...this.toolNodes]
    if (this.tab === 'new') out.push(...this.setupBody.spots())
    if (this.tab === 'resume') out.push(...this.resumeBody.spots())
    if (this.tab === 'challenge') out.push(...this.challengeBody.spots())
    return out
  }

  /** 지금 고른 덱과 스테이크. 시작을 묻는 판이 이것을 적습니다. */
  get pickedSetup(): RunSetup {
    return this.setupBody.picked()
  }

  /** 저장된 판이 있는가. 시작을 묻는 글이 이것으로 갈립니다. */
  get hasSaved(): boolean {
    return this.saved !== undefined
  }

  constructor(data: Data, setup: RunSetup,
              private readonly progress: ChallengeProgress,
              private readonly hooks: RunPanelHooks) {
    this.setupBody = new SetupBody(data, setup)
    this.setupBody.onPick = next => hooks.onPickSetup(next)
    this.setupBody.onStart = next => hooks.onStartNew(next)
    this.setupBody.onStartRanked = () => hooks.onStartRanked()

    this.challengeBody = new ChallengeBody(data, progress)
    this.challengeBody.onStart = challengeId => hooks.onStartChallenge(challengeId)

    this.resumeBody = new ResumeBody(data)
    this.resumeBody.onResume = () => hooks.onResume()
    this.resumeBody.onDiscard = () => hooks.onDiscard()

    this.buildFrame()
    this.view.addChild(this.tabRow, this.bodyLayer, this.tip)
    for (const body of this.bodies()) {
      body.view.position.set(Math.round((WIDTH - body.size.width) / 2),
                             BODY_TOP - body.top)
      this.bodyLayer.addChild(body.view)
    }
    this.setupBody.onTip = tip => this.showTip(this.setupBody, tip)
    this.challengeBody.onTip = tip => this.showTip(this.challengeBody, tip)
    this.show('new')
  }

  private bodies(): TabBody[] {
    return [this.setupBody, this.resumeBody, this.challengeBody]
  }

  /** 몸통이 가리킨 것을 판의 좌표로 옮겨 띄웁니다. */
  private showTip(body: TabBody, tip: TipRequest | undefined): void {
    if (!tip) {
      this.tip.hide()
      return
    }
    const dx = body.view.x
    const dy = body.view.y
    this.tip.show(tip.name, tip.chip ?? '', 0, tip.lines,
                  { x: tip.x + dx, top: tip.top + dy, bottom: tip.bottom + dy },
                  { width: WIDTH, height: HEIGHT }, undefined, tip.chipTone)
  }

  private buildFrame(): void {
    if (this.frame) {
      this.view.removeChild(this.frame)
      this.frame.destroy({ children: true })
    }
    this.frame = panelFrame(WIDTH, HEIGHT, t('ui.run.title'), this.hooks.onClose,
                            undefined, false)
    this.view.addChildAt(this.frame, 0)
  }

  /**
   * 저장된 판을 알립니다.
   *
   * **없으면 이어하기 탭이 없습니다.** 눌러 보고 「없습니다」가 적혀 있는 것보다 탭이
   * 없는 편이 그 자리에서 끝납니다.
   */
  setSaved(saved: SavedRun | undefined): void {
    this.saved = saved
    this.resumeBody.show(saved)
    if (saved === undefined && this.tab === 'resume') this.tab = 'new'
    this.drawTabs()
    this.syncBodies()
  }

  /** 바깥에서 고른 덱과 스테이크가 바뀌었을 때. */
  setSetup(setup: RunSetup): void {
    this.setupBody.setSetup(setup)
  }

  /** 로그인 상태. 랭크로 시작할 수 있는지가 이것으로 갈립니다. */
  setSignedIn(signedIn: boolean): void {
    this.setupBody.setSignedIn(signedIn)
  }

  /**
   * 어느 탭으로 엽니다.
   *
   * **이어할 것이 있으면 그것이 먼저입니다.** 판을 두다 그만둔 사람이 다음에 하려는 것은
   * 대개 그 판이고, 새 런은 그 옆에 있습니다.
   */
  open(): void {
    this.show(this.saved ? 'resume' : 'new')
  }

  private show(tab: RunTab): void {
    if (tab === 'resume' && this.saved === undefined) tab = 'new'
    this.tab = tab
    this.tip.hide()
    this.drawTabs()
    this.syncBodies()
  }

  private syncBodies(): void {
    this.setupBody.view.visible = this.tab === 'new'
    this.resumeBody.view.visible = this.tab === 'resume'
    this.challengeBody.view.visible = this.tab === 'challenge'
  }

  /** 지금 서는 탭들. 이어하기는 저장된 판이 있을 때만입니다. */
  private tabs(): { key: RunTab; label: string; locked: boolean }[] {
    const rows: { key: RunTab; label: string; locked: boolean }[] = [
      { key: 'new', label: t('ui.run.tab.new'), locked: false },
    ]
    if (this.saved) {
      rows.push({ key: 'resume', label: t('ui.run.tab.resume'), locked: false })
    }
    // **열리기 전에도 보입니다.** 할 것이 더 있다는 것이 처음부터 보여야 하고, 무엇으로
    // 열리는지는 그 탭 안의 쪽지에 적혀 있습니다.
    rows.push({
      key: 'challenge',
      label: t('ui.run.tab.challenge'),
      locked: openCount(this.progress) === 0,
    })
    return rows
  }

  private drawTabs(): void {
    this.tabRow.removeChildren().forEach(child => child.destroy({ children: true }))
    for (const key of [...this.toolNodes.keys()]) {
      if (key.startsWith('tab:')) this.toolNodes.delete(key)
    }

    const rows = this.tabs()
    const total = rows.length * TAB_W + (rows.length - 1) * TAB_GAP
    let x = Math.round((WIDTH - total) / 2)

    for (const row of rows) {
      const here = row.key === this.tab
      const cell = new Container()
      cell.position.set(x, TAB_Y)

      const plate = new Graphics()
      plate.roundRect(0, 0, TAB_W, TAB_H, 9)
        .fill({ color: here ? UI.cell : 0x14131a })
        .stroke({ color: here ? UI.pick : UI.hairline, width: here ? 2 : 1.5 })
      cell.addChild(plate)

      // **고른 탭은 잠겨 있어도 또렷합니다.** 지금 보고 있는 것이 무엇인지가 먼저이고,
      // 잠긴 것은 그 안의 20칸이 잠긴 채로 보이는 것으로 이미 읽힙니다.
      const label = new Text({
        text: row.label,
        style: {
          fontSize: 15,
          fill: here ? COLOR.ink : row.locked ? UI.locked : COLOR.inkDim,
          fontWeight: '800',
        },
      })
      label.anchor.set(0.5)
      label.position.set(TAB_W / 2, TAB_H / 2)
      cell.addChild(label)

      cell.eventMode = 'static'
      cell.cursor = 'pointer'
      cell.on('pointertap', () => this.show(row.key))
      this.tabRow.addChild(cell)
      this.toolNodes.set(`tab:${row.key}`, { node: cell, cx: TAB_W / 2, cy: TAB_H / 2 })

      x += TAB_W + TAB_GAP
    }
  }

  tick(seconds: number): void {
    this.tip.advance(seconds)
  }

  onClosed(): void {
    this.tip.hide()
  }

  relabel(): void {
    this.buildFrame()
    for (const body of this.bodies()) body.relabel()
    this.drawTabs()
  }
}

// ---------------------------------------------------------------------------
// 이어하기
// ---------------------------------------------------------------------------

const CARD_W = 520
const CARD_H = 300

/**
 * 그만둔 자리 한 장.
 *
 * **되살리지 않고 적습니다.** 적어 둔 것 안에 안테와 금액과 조커 수가 함께 있으므로,
 * 목록을 그리려고 액션을 다시 돌릴 이유가 없습니다 — 되돌리는 것은 「이어서 하기」를
 * 누른 뒤입니다.
 */
class ResumeBody {
  readonly view = new Container()
  readonly size = { width: CARD_W, height: CARD_H }
  readonly top = 0

  private readonly body = new Container()
  private resumeButton?: Button
  private discardButton?: Button
  private saved?: SavedRun

  onResume?: () => void
  onDiscard?: () => void

  constructor(private readonly data: Data) {
    this.view.addChild(this.body)
  }

  show(saved: SavedRun | undefined): void {
    this.saved = saved
    this.draw()
  }

  relabel(): void {
    this.draw()
  }

  private draw(): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))
    this.resumeButton = undefined
    this.discardButton = undefined
    const saved = this.saved
    if (!saved) return

    const plate = new Graphics()
    plate.roundRect(0, 0, CARD_W, CARD_H, 12)
      .fill({ color: UI.cell })
      .stroke({ color: UI.hairline, width: 2 })
    this.body.addChild(plate)

    const deck = this.data.tables.deck.findByDeckId(saved.deckId)
    const deckName = deck
      ? nameOf(this.data, 'deck', saved.deckId, deck.name) : saved.deckId
    const stakeRow = this.data.tables.stake.records
      .find(one => StakeKind[one.stake] === saved.stake)
    const stakeName = stakeRow
      ? nameOf(this.data, 'stake', stakeSlug(stakeRow.stake), stakeRow.name) : saved.stake

    const title = new Text({
      text: `${deckName} · ${stakeName}`,
      style: { fontSize: 22, fill: COLOR.ink, fontWeight: '800' },
    })
    title.position.set(24, 22)
    this.body.addChild(title)

    // 챌린지 런이면 그 이름이 덱 이름보다 큰 표시입니다.
    if (saved.challengeId !== '') {
      const row = this.data.tables.challenge.findByChallengeId(saved.challengeId)
      const name = new Text({
        text: row ? nameOf(this.data, 'challenge', saved.challengeId, row.name)
                  : saved.challengeId,
        style: { fontSize: 13, fill: UI.yellow, fontWeight: '800' },
      })
      name.position.set(24, 52)
      this.body.addChild(name)
    } else if (saved.ranked) {
      const mark = new Text({
        text: t('ui.lb.ranked'),
        style: { fontSize: 13, fill: UI.yellow, fontWeight: '800' },
      })
      mark.position.set(24, 52)
      this.body.addChild(mark)
    }

    // 그만둔 자리. **셋을 한 줄로 둡니다** — 안테가 어디까지 갔는가가 먼저이고, 금액과
    // 조커 수가 그 판의 모습입니다.
    const facts: [string, string][] = [
      [t('ui.slot.ante'), String(saved.ante)],
      [t('ui.slot.money'), `$${saved.money}`],
      [t('ui.insight.group.joker'), String(saved.jokers)],
    ]
    for (let i = 0; i < facts.length; i++) {
      const x = 24 + i * 160
      const head = new Text({
        text: facts[i][0],
        style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800', letterSpacing: 1 },
      })
      head.position.set(x, 92)
      const value = new Text({
        text: facts[i][1],
        style: { fontSize: 26, fill: COLOR.ink, fontWeight: '800' },
      })
      value.position.set(x, 110)
      this.body.addChild(head, value)
    }

    const where = new Text({
      text: tf('ui.run.stopped',
               { where: t(PHASE_KEYS[saved.phase] ?? 'ui.run.phase.round') }),
      style: { fontSize: 13, fill: COLOR.inkDim },
    })
    where.position.set(24, 158)
    this.body.addChild(where)

    const seed = new Text({
      text: saved.seed,
      style: { fontSize: 12, fill: 0x6f7d90, fontWeight: '700', letterSpacing: 1 },
    })
    seed.position.set(24, 182)
    this.body.addChild(seed)

    const when = new Text({
      text: agoText(saved.savedAt),
      style: { fontSize: 12, fill: 0x6f7d90 },
    })
    when.anchor.set(1, 0)
    when.position.set(CARD_W - 24, 182)
    this.body.addChild(when)

    // **이어서 하기가 큽니다.** 버리는 것은 되돌릴 수 없으므로 같은 크기로 나란히 두면
    // 잘못 누르는 일이 생깁니다.
    const resume = new Button(t('ui.run.resume'), 320, 48, UI.yellow,
                              () => this.onResume?.(), 18)
    resume.position.set(24, CARD_H - 72)
    this.resumeButton = resume

    const discard = new Button(t('ui.run.discard'), 132, 48, UI.btn,
                               () => this.onDiscard?.(), 15)
    discard.position.set(CARD_W - 24 - 132, CARD_H - 72)
    this.discardButton = discard

    this.body.addChild(resume, discard)
  }

  /** 도구가 짚을 자리. 판이 모아 갑니다. */
  spots(): [string, ToolSpot][] {
    const out: [string, ToolSpot][] = []
    if (this.resumeButton) out.push(['resume', { node: this.resumeButton, cx: 160, cy: 24 }])
    if (this.discardButton) {
      out.push(['discard', { node: this.discardButton, cx: 66, cy: 24 }])
    }
    return out
  }
}

/** `RunState.phase` 마다 어느 낱말인가. */
const PHASE_KEYS: Record<string, string> = {
  'blind-select': 'ui.run.phase.blindSelect',
  round: 'ui.run.phase.round',
  shop: 'ui.run.phase.shop',
}

/**
 * 얼마나 지났는가.
 *
 * **시각을 적지 않습니다.** 「9월 3일 14시 22분」은 그 판을 언제 두었는지를 세어 보게
 * 하고, 여기서 알아야 하는 것은 그것이 최근의 판인가입니다.
 */
function agoText(savedAt: number): string {
  if (savedAt <= 0) return ''
  const minutes = Math.max(0, Math.floor((Date.now() - savedAt) / 60_000))
  if (minutes < 1) return t('ui.run.ago.now')
  if (minutes < 60) return tf('ui.run.ago.minutes', { n: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return tf('ui.run.ago.hours', { n: hours })
  return tf('ui.run.ago.days', { n: Math.floor(hours / 24) })
}
