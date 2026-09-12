// 판을 여는 자리.
//
// **시작을 누르면 여기가 열립니다.** 무엇으로 시작하는가가 화면 여럿에 흩어져 있었습니다 —
// 타이틀에 「덱과 스테이크」 단추가 하나, 「챌린지」 단추가 하나, 「랭크」 단추가 하나였고,
// 셋 다 판을 여는 일인데 서로 다른 자리에 있었습니다. 하나로 모으면 무엇으로 시작할지를
// 고르는 자리가 하나입니다.
//
// **탭으로 늘어놓지 않고 단계를 둡니다.** 1단계가 큰 메뉴 셋이고 2단계가 세부입니다 —
// 디자인 언어의 「전면 화면」이 정본입니다.
//
// |1단계|2단계|
// |--|--|
// |새 런|덱과 스테이크를 고르고 시작합니다|
// |이어하기 (저장된 판이 있을 때)|그만둔 자리와 「이어서 하기」·「버리기」|
// |챌린지 (열리기 전에는 잠긴 채로)|20칸에서 하나를 골라 시작합니다|
//
// **세부 화면의 ESC 는 큰 메뉴로 돌아갑니다.** 큰 메뉴의 ESC 가 판을 닫습니다.
//
// **몸통은 저마다 자기 좌표로 그립니다.** 판이 그것을 받아 전면 화면의 몸통 자리에 놓습니다.
//
// **설명 쪽지는 판이 하나만 가집니다.** 몸통마다 자기 쪽지를 두면 화면에 둘이 뜰 수 있고,
// 쪽지가 화면 밖으로 나가지 않게 하는 셈이 몸통마다 달라집니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import type { SavedRun } from '../core/save-run'
import { nameOf, t, tf } from '../core/strings'
import { StakeKind } from '../generated/enums/stake-kind'
import { stakeSlug } from '../core/stake'
import { plateTint, wellTint } from '../render/skin'
import { UI, SIZE, TEXT, WEIGHT } from '../render/theme'
import { ConsumableKind } from '../generated/enums/consumable-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { itemFace } from '../render/faces'
import { NUMERALS } from './font'
import { sectionHead } from './parts'
import { ChallengeBody, openCount, type ChallengeProgress } from './challenge'
import { glowEdge, piece } from './chrome'
import type { ToolSpot } from './layout'
import { FULL_BODY_TOP, FULL_EDGE, FULL_FOOT_Y, fullFrame, type ModalPanel } from './modal'
import { SetupBody, setupLabel, type RunSetup } from './setup'
import { Tooltip } from './tooltip'
import { Button } from './widgets'

/** 큰 메뉴의 판 셋. 타이틀의 큰 판과 같은 규격입니다. */
const CARD_W = 352
const CARD_H = 520
const CARD_Y = 160
const CARD_GAP = 32
/** 판 위쪽의 그림 자리. */
const ART_H = 236
/** 판 아래의 나아가는 단추. 높이 계단의 `lg` 입니다. */
const GO_H = 60

/** 화면 하나의 이름. `menu` 가 1단계이고 나머지가 2단계입니다. */
export type RunTab = 'new' | 'resume' | 'challenge'
type Page = 'menu' | RunTab

/**
 * 무엇을 가리켰는가.
 *
 * 좌표는 **그 몸통의 지역 좌표**입니다. 판이 몸통의 자리를 더해 자기 좌표로 옮깁니다.
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

/** 2단계 화면 하나의 몸통. */
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
  readonly size = { width: SIZE.width, height: SIZE.height }
  readonly centered = true
  readonly fullscreen = true

  private readonly frameLayer = new Container()
  private readonly menuLayer = new Container()
  private readonly bodyLayer = new Container()
  private readonly tip = new Tooltip()

  private readonly setupBody: SetupBody
  private readonly challengeBody: ChallengeBody
  private readonly resumeBody: ResumeBody

  private page: Page = 'menu'
  private saved?: SavedRun

  /** 도구가 짚을 자리들. 화면이 바뀔 때마다 다시 셉니다. */
  private readonly toolNodes = new Map<string, ToolSpot>()

  get toolSpots(): [string, ToolSpot][] {
    // **몸통이 그린 자리도 함께 알립니다.** 이어하기의 단추 둘은 저장된 판이 있을 때만
    // 그려지므로, 판이 스스로 세면 그것이 없는 날에 빈자리를 가리킵니다.
    const out: [string, ToolSpot][] = [...this.toolNodes]
    if (this.page === 'new') out.push(...this.setupBody.spots())
    if (this.page === 'resume') out.push(...this.resumeBody.spots())
    if (this.page === 'challenge') out.push(...this.challengeBody.spots())
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

  constructor(private readonly data: Data, setup: RunSetup,
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

    this.view.addChild(this.frameLayer, this.menuLayer, this.bodyLayer, this.tip)
    for (const body of this.bodies()) {
      body.view.position.set(FULL_EDGE, FULL_BODY_TOP - body.top)
      this.bodyLayer.addChild(body.view)
    }
    this.setupBody.onTip = tip => this.showTip(this.setupBody, tip)
    this.challengeBody.onTip = tip => this.showTip(this.challengeBody, tip)
    this.show('menu')
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
                  { width: SIZE.width, height: SIZE.height }, undefined, tip.chipTone)
  }

  /**
   * 저장된 판을 알립니다.
   *
   * **없으면 이어하기 판이 잠깁니다.** 눌러 보고 「없습니다」가 적혀 있는 것보다 잠긴
   * 채로 보이는 편이 그 자리에서 끝납니다.
   */
  setSaved(saved: SavedRun | undefined): void {
    this.saved = saved
    this.resumeBody.show(saved)
    if (saved === undefined && this.page === 'resume') this.page = 'menu'
    this.draw()
  }

  /** 바깥에서 고른 덱과 스테이크가 바뀌었을 때. */
  setSetup(setup: RunSetup): void {
    this.setupBody.setSetup(setup)
    if (this.page === 'menu' || this.page === 'new') this.draw()
  }

  /** 로그인 상태. 랭크로 시작할 수 있는지가 이것으로 갈립니다. */
  setSignedIn(signedIn: boolean): void {
    this.setupBody.setSignedIn(signedIn)
  }

  /**
   * 엽니다. **큰 메뉴가 먼저입니다.**
   *
   * 이어할 것이 있어도 그 화면으로 바로 가지 않습니다 — 큰 메뉴의 이어하기 판에 그만둔
   * 자리가 적혀 있고, 무엇으로 시작할지는 거기서 고릅니다.
   */
  open(): void {
    this.show('menu')
  }

  /** ESC 와 바깥 누르기. 세부 화면에서는 큰 메뉴로 돌아가고, 큰 메뉴에서는 닫힙니다. */
  onBack(): boolean {
    if (this.page === 'menu') return false
    this.show('menu')
    return true
  }

  private show(page: Page): void {
    if (page === 'resume' && this.saved === undefined) page = 'menu'
    this.page = page
    this.tip.hide()
    this.draw()
  }

  /**
   * 그림이 닿았으니 다시 그립니다.
   *
   * **그림은 늦게 닿습니다.** 이어하기의 딱지는 만들 때 그림이 없어 빈 칸으로 서고,
   * 다시 그리지 않으면 그대로 남습니다 — 판 안의 통들과 같은 규칙입니다(`onArtReady`).
   */
  repaintArt(): void {
    if (this.page === 'resume' || this.page === 'menu') this.draw()
  }

  private draw(): void {
    this.frameLayer.removeChildren().forEach(child => child.destroy({ children: true }))
    this.menuLayer.removeChildren().forEach(child => child.destroy({ children: true }))
    this.toolNodes.clear()

    this.setupBody.view.visible = this.page === 'new'
    this.resumeBody.view.visible = this.page === 'resume'
    this.challengeBody.view.visible = this.page === 'challenge'

    if (this.page === 'menu') {
      this.frameLayer.addChild(fullFrame(t('ui.run.title'), [], this.hooks.onClose,
                                         undefined, undefined, t('ui.run.subtitle')))
      this.drawMenu()
      return
    }

    const back = (): void => { this.show('menu') }
    if (this.page === 'new') {
      // 오른쪽 위에 지금 고른 덱.
      const right = new Container()
      const label = new Text({
        text: t('ui.setup.picked'),
        style: { fontSize: TEXT.small, fill: UI.inkDim, letterSpacing: 1 },
      })
      label.anchor.set(1, 0)
      const picked = new Text({
        text: setupLabel(this.data, this.setupBody.picked()),
        style: { fontSize: TEXT.display, fill: UI.red, fontWeight: WEIGHT.bold },
      })
      picked.anchor.set(1, 0)
      picked.position.set(0, 16)
      right.addChild(label, picked)
      this.frameLayer.addChild(fullFrame(t('ui.setup.title'), [t('ui.run.title'), t('ui.run.tab.new')],
                                         back, right))
      return
    }
    if (this.page === 'resume') {
      this.frameLayer.addChild(fullFrame(t('ui.run.tab.resume'), [t('ui.run.title'), t('ui.run.tab.resume')],
                                         back))
      return
    }
    this.frameLayer.addChild(fullFrame(t('ui.run.tab.challenge'),
                                       [t('ui.run.title'), t('ui.run.tab.challenge')], back))
  }

  /**
   * 큰 메뉴. **판 셋이고 새 런만 금색입니다.**
   *
   * 판마다 위에 그림 자리, 이름, 두 줄 설명, 아래에 나아가는 단추입니다. 이어하기는 저장된
   * 판이 없으면 잠기고, 챌린지는 열리기 전에는 잠깁니다 — 잠긴 판은 채도를 뺍니다.
   */
  private drawMenu(): void {
    const saved = this.saved
    const opened = openCount(this.progress)
    const cards: {
      key: RunTab; title: string; lines: string[]; go: string; tone: number
      primary: boolean; locked: boolean
    }[] = [
      {
        key: 'new', title: t('ui.run.tab.new'),
        lines: [t('ui.run.new_desc'), setupLabel(this.data, this.setupBody.picked())],
        go: t('ui.run.pick'), tone: UI.red, primary: true, locked: false,
      },
      {
        key: 'resume', title: t('ui.run.tab.resume'),
        lines: saved ? this.resumeBody.summary(saved) : [],
        go: t('ui.run.continue'), tone: UI.bar, primary: false, locked: saved === undefined,
      },
      {
        key: 'challenge', title: t('ui.run.tab.challenge'),
        lines: [t('ui.run.challenge_desc'), opened === 0 ? t('ui.challenge.lockedAll') : ''],
        go: t('ui.run.open'), tone: UI.money, primary: false, locked: opened === 0,
      },
    ]
    const left = (SIZE.width - (CARD_W * cards.length + CARD_GAP * (cards.length - 1))) / 2
    cards.forEach((card, index) => {
      const node = new Container()
      node.position.set(left + index * (CARD_W + CARD_GAP), CARD_Y)

      const plate = piece('plate', CARD_W, CARD_H, plateTint(UI.panel))
      if (plate) node.addChild(plate)
      else {
        const g = new Graphics()
        g.rect(0, 0, CARD_W, CARD_H).fill({ color: UI.panel, alpha: UI.panelAlpha })
        node.addChild(g)
      }
      const art = piece('tray', CARD_W - 2, ART_H, wellTint(card.tone))
      if (art) {
        art.position.set(1, 1)
        art.alpha = card.locked ? 0.2 : 0.4
        node.addChild(art)
      }
      const band = glowEdge(CARD_W, card.primary ? UI.yellow : UI.rule)
      if (band) node.addChild(band)

      const title = new Text({
        text: card.title,
        style: { fontSize: TEXT.display, fill: card.locked ? UI.inkDim : UI.ink, fontWeight: WEIGHT.bold },
      })
      title.position.set(24, ART_H + 26)
      node.addChild(title)

      card.lines.filter(line => line !== '').forEach((line, at) => {
        const text = new Text({
          text: line,
          style: {
            fontSize: TEXT.small, fill: card.locked ? UI.inkFaint : UI.inkDim,
            wordWrap: true, wordWrapWidth: CARD_W - 48, breakWords: true,
          },
        })
        text.position.set(24, ART_H + 26 + 48 + at * 28)
        node.addChild(text)
      })

      const go = new Button(card.locked ? t('ui.run.locked') : card.go, CARD_W - 48, GO_H,
                            card.primary ? 'primary' : 'neutral', () => this.show(card.key))
      go.position.set(24, CARD_H - 24 - GO_H)
      go.enabled = !card.locked
      node.addChild(go)
      this.toolNodes.set(`tab:${card.key}`, { node: go, cx: (CARD_W - 48) / 2, cy: GO_H / 2 })

      if (!card.locked) {
        // **판 어디를 눌러도 그 단추입니다.** 큰 판이 곧 누르는 자리입니다.
        node.eventMode = 'static'
        node.cursor = 'pointer'
        node.on('pointertap', event => {
          if (event.target === go || go.children.includes(event.target as never)) return
          this.show(card.key)
        })
      } else {
        node.alpha = 0.7
      }
      this.menuLayer.addChild(node)
    })
  }

  relabel(): void {
    for (const body of this.bodies()) body.relabel()
    this.draw()
  }

  /** 겉면을 갈아 끼운 뒤. 판을 통째로 다시 그립니다. */
  restyle(): void {
    this.draw()
  }
}

// ---------------------------------------------------------------------------
// 이어하기
// ---------------------------------------------------------------------------

/**
 * 그만둔 자리.
 *
 * **화면의 폭을 다 씁니다.** 왼쪽에 560짜리 한 장을 두고 오른쪽에 딱지를 세웠더니 화면의
 * 오른쪽 절반이 비었고, 값 셋은 그 한 장 안에서 다시 왼쪽으로 몰렸습니다 — 머리 한 줄,
 * 값 칸 셋, 들고 있던 것 두 줄이 저마다 화면의 폭을 씁니다.
 */
const RESUME_HEAD_H = 108
const RESUME_CELL_Y = 132
const RESUME_CELL_H = 92
const RESUME_GAP = 16
/** 들고 있던 것 두 줄. 이름표 한 줄과 딱지 한 줄입니다. */
const HELD_JOKER_Y = 256
const HELD_ITEM_Y = 392
/** 딱지의 배율과 사이. 판의 딱지(88×124)를 4분의 3으로 둡니다. */
const HELD_SCALE = 0.75
const HELD_STEP = Math.round(SIZE.jokerWidth * HELD_SCALE) + 12

/**
 * 그만둔 자리 한 장.
 *
 * **되살리지 않고 적습니다.** 적어 둔 것 안에 안테와 금액과 조커 수가 함께 있으므로,
 * 목록을 그리려고 액션을 다시 돌릴 이유가 없습니다 — 되돌리는 것은 「이어서 하기」를
 * 누른 뒤입니다.
 */
class ResumeBody {
  readonly view = new Container()
  readonly size = { width: SIZE.width - FULL_EDGE * 2, height: FULL_FOOT_Y - FULL_BODY_TOP }
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

  /** 큰 메뉴의 이어하기 판에 적히는 두 줄 — 무엇으로, 어디까지. */
  summary(saved: SavedRun): string[] {
    return [
      this.deckLine(saved),
      tf('ui.run.stopped', { where: t(PHASE_KEYS[saved.phase] ?? 'ui.run.phase.round') }),
    ]
  }

  private deckLine(saved: SavedRun): string {
    const deck = this.data.tables.deck.findByDeckId(saved.deckId)
    const deckName = deck
      ? nameOf(this.data, 'deck', saved.deckId, deck.name) : saved.deckId
    const stakeRow = this.data.tables.stake.records
      .find(one => StakeKind[one.stake] === saved.stake)
    const stakeName = stakeRow
      ? nameOf(this.data, 'stake', stakeSlug(stakeRow.stake), stakeRow.name) : saved.stake
    return `${deckName} · ${stakeName}`
  }

  private draw(): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))
    this.resumeButton = undefined
    this.discardButton = undefined
    const saved = this.saved
    if (!saved) return
    const width = this.size.width

    // ── 머리 한 줄. 덱과 스테이크, 그리고 오른쪽에 시드와 그만둔 때 ──────────
    const head = new Container()
    const headSkin = piece('well', width, RESUME_HEAD_H, wellTint(UI.panel))
    if (headSkin) head.addChild(headSkin)
    else {
      const g = new Graphics()
      g.rect(0, 0, width, RESUME_HEAD_H).fill(UI.panel)
      head.addChild(g)
    }

    const title = new Text({
      text: this.deckLine(saved),
      style: { fontSize: TEXT.head, fill: UI.ink, fontWeight: WEIGHT.bold },
    })
    title.anchor.set(0, 0.5)
    title.position.set(24, 40)
    head.addChild(title)

    // 챌린지 런이면 그 이름이 덱 이름보다 큰 표시입니다.
    const mark = saved.challengeId !== ''
      ? (() => {
        const row = this.data.tables.challenge.findByChallengeId(saved.challengeId)
        return row ? nameOf(this.data, 'challenge', saved.challengeId, row.name)
          : saved.challengeId
      })()
      : saved.ranked ? t('ui.lb.ranked') : ''
    if (mark !== '') {
      const tag = new Text({
        text: mark,
        style: { fontSize: TEXT.small, fill: UI.yellow, fontWeight: WEIGHT.bold },
      })
      tag.anchor.set(0, 0.5)
      tag.position.set(24 + title.width + 20, 42)
      head.addChild(tag)
    }

    const where = new Text({
      text: tf('ui.run.stopped',
               { where: t(PHASE_KEYS[saved.phase] ?? 'ui.run.phase.round') }),
      style: { fontSize: TEXT.small, fill: UI.inkDim },
    })
    where.anchor.set(0, 0.5)
    where.position.set(24, 78)
    head.addChild(where)

    const when = new Text({
      text: agoText(saved.savedAt),
      style: { fontSize: TEXT.small, fill: UI.inkFaint },
    })
    when.anchor.set(1, 0.5)
    when.position.set(width - 24, 40)
    head.addChild(when)

    const seed = new Text({
      text: saved.seed,
      style: { fontSize: TEXT.small, fill: UI.inkFaint, fontWeight: WEIGHT.normal,
        letterSpacing: 1 },
    })
    seed.anchor.set(1, 0.5)
    seed.position.set(width - 24, 78)
    head.addChild(seed)
    this.body.addChild(head)

    // ── 값 칸 셋. 안테가 어디까지 갔는가가 먼저이고, 금액과 조커 수가 그 판의 모습입니다 ──
    const facts: [string, string, number][] = [
      [t('ui.slot.ante'), `${saved.ante} / ${this.data.run.winAnte}`, UI.ink],
      [t('ui.slot.money'), `$${saved.money}`, UI.money],
      [t('ui.insight.group.joker'), String(saved.jokers), UI.chips],
    ]
    const cellW = (width - RESUME_GAP * (facts.length - 1)) / facts.length
    facts.forEach((fact, index) => {
      const cell = new Container()
      cell.position.set(index * (cellW + RESUME_GAP), RESUME_CELL_Y)
      const skin = piece('well', cellW, RESUME_CELL_H, wellTint(UI.panel))
      if (skin) cell.addChild(skin)
      else {
        const g = new Graphics()
        g.rect(0, 0, cellW, RESUME_CELL_H).fill(UI.panel)
        cell.addChild(g)
      }
      const name = new Text({
        text: fact[0],
        style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal,
          letterSpacing: 1 },
      })
      name.anchor.set(0, 0.5)
      name.position.set(20, 28)
      const value = new Text({
        text: fact[1],
        style: { fontSize: TEXT.display, fill: fact[2], fontWeight: WEIGHT.bold,
          fontFamily: NUMERALS },
      })
      value.anchor.set(1, 1)
      value.position.set(cellW - 20, RESUME_CELL_H - 16)
      cell.addChild(name, value)
      this.body.addChild(cell)
    })

    // ── 들고 있던 것 두 줄. 어떤 판이었는지는 이것으로 읽힙니다 ──────────────
    const jokers = saved.jokerIds.map(id => ({ kind: ShopItemKind.Joker, id }))
    const consumables = saved.consumableList.map(one => ({
      kind: ShopItemKind[ConsumableKind[one.kind] as keyof typeof ShopItemKind]
        ?? ShopItemKind.Tarot,
      id: one.id,
    }))
    this.heldRow(this.body, t('ui.button.jokers'), jokers, HELD_JOKER_Y, width)
    this.heldRow(this.body, t('ui.resume.consumables'), consumables, HELD_ITEM_Y, width)

    // **나아가는 줄입니다.** 아래 띠의 오른쪽에 놓이고 금색은 하나입니다.
    const footY = FULL_FOOT_Y - FULL_BODY_TOP + 12
    const resume = new Button(t('ui.run.resume'), 320, GO_H, 'primary',
                              () => this.onResume?.())
    resume.position.set(width - 320, footY)
    this.resumeButton = resume

    const discard = new Button(t('ui.run.discard'), 160, GO_H, 'neutral',
                               () => this.onDiscard?.())
    discard.position.set(width - 320 - 12 - 160, footY)
    this.discardButton = discard

    this.body.addChild(discard, resume)
  }

  /**
   * 들고 있던 것 한 줄. 이름표 아래에 딱지가 왼쪽부터 섭니다. **없으면 「없음」 한 낱말입니다.**
   */
  private heldRow(into: Container, label: string, items: { kind: ShopItemKind; id: string }[],
                  y: number, width: number): void {
    const head = sectionHead(width, label, undefined, true)
    head.position.set(0, y)
    into.addChild(head)
    if (items.length === 0) {
      const none = new Text({
        text: t('ui.resume.none'),
        style: { fontSize: TEXT.small, fill: UI.inkFaint },
      })
      none.position.set(0, y + 40)
      into.addChild(none)
      return
    }
    items.forEach((item, at) => {
      const face = itemFace(this.data, item)
      face.scale.set(HELD_SCALE)
      face.position.set(at * HELD_STEP, y + 40)
      into.addChild(face)
    })
  }

  /** 도구가 짚을 자리. 판이 모아 갑니다. */
  spots(): [string, ToolSpot][] {
    const out: [string, ToolSpot][] = []
    if (this.resumeButton) out.push(['resume', { node: this.resumeButton, cx: 160, cy: GO_H / 2 }])
    if (this.discardButton) {
      out.push(['discard', { node: this.discardButton, cx: 80, cy: GO_H / 2 }])
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
