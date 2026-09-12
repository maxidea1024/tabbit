import { COLOR, DEAD, ENHANCEMENT_PAPER, SEAL_INK, STAKE_INK } from '../render/ink'
import { Container, Graphics, Rectangle, Sprite, Text } from 'pixi.js'
import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { RankKind } from '../generated/enums/rank-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { describe, rankDisplay } from '../core/describe'
import { type Insight, insights } from '../core/insight'
import { snapshotHash } from '../core/hash'
import { defaultRules, rewardOf, targetOf } from '../core/run'
import { nameOf, t, text, tf } from '../core/strings'
import { stakeRow, stakeSlug } from '../core/stake'
import { type CardInstance } from '../core/state'
import { artFor } from '../render/art'
import { cardArtDir, cardPaper, drawsIndex, suitInk } from '../render/card-set'
import { MINI_RANK, SUIT_PIP } from '../render/faces'
import { cardArtId, drawFace } from '../render/pips'
import { SIZE, STEP, TEXT, UI, WEIGHT } from '../render/theme'
import { NUMERALS } from '../ui/font'
import { Button } from '../ui/widgets'
import { Guide } from '../ui/guide'
import { CollectionPanel } from '../ui/collection'
import { RunPanel } from '../ui/run-panel'
import { ConfirmPanel } from '../ui/confirm'
import {
  FOOTER_BAR, FULL_EDGE, FULL_FOOT_Y, fullFrame, type ModalPanel, Modals, panelFrame, TITLE_BAR,
} from '../ui/modal'
import { piece } from '../ui/chrome'
import { mix, plateTint, wellTint } from '../render/skin'
import { SECTION_H, sectionHead } from '../ui/parts'
import { ScrollView } from '../ui/scroll'
import { richBlock, richLeading, richLine, richStyle } from '../ui/rich'
import { OptionsPanel } from '../ui/options'
import {
  MENU_PAD, PANEL_ROWS, IN_X, IN_W,
} from './metrics'
import { blindName, HAND_SHAPE, INSIGHT_COLOR, ruleValue, snake } from './tables'
import { type RunInfoTab } from './types'
import { type Game } from './game'

/** 곱셈 기호와 줄표. **글 표에 두지 않습니다** — 어느 말에서나 같은 기호입니다. */
const TIMES_SIGN = String.fromCharCode(0xd7)
const DASH = String.fromCharCode(0x2014)

/**
 * 런 정보의 자. **캔버스의 `RunInfo` 아트보드에서 옵니다.**
 *
 * 갈래 줄은 왼쪽 변에서 시작해 다섯이 나란히 서고, 그 밑에 열 이름 한 줄, 그 밑이 본문입니다.
 */
const RUN_TAB_Y = 176
const RUN_TAB_W = 168
const RUN_TAB_GAP = 10
/** 열 이름 한 줄. */
const RUN_HEAD_Y = 222
/** 본문의 첫 줄. */
const RUN_ROWS_Y = 248
const RUN_ROW_H = 44
/** 족보 갈래의 두 열. */
const RUN_COL_LEVEL = 356
const RUN_COL_VALUE = 456

/** 블라인드 갈래의 딱지 셋. */
const BLIND_TOP = 240
const BLIND_H = 298
const BLIND_GAP = 32
/** 안테별 요구 점수. */
const ANTE_HEAD_Y = 604
const ANTE_ROW_Y = 636
const ANTE_ROW_H = 76

/** 스테이크 갈래의 줄 높이. 색 조각과 이름과 설명이 한 줄입니다. */
const STAKE_ROW_H = 54

/** 인사이트 갈래의 줄. */
const INSIGHT_ROW_H = 58
const INSIGHT_TEXT_X = 152

/** 기록 갈래의 값 칸. */
const LOG_CELL_Y = 240
const LOG_CELL_H = 52

/** 「적용 중」 목록의 한 줄. `delta` 가 있으면 값이 수이고 크게 놓입니다. */
interface ActiveEntry {
  label: string
  value: string
  delta?: string
  lines: string[]
}

/** 「적용 중」 목록의 줄 높이. 머리글(12)과 첫 줄 사이가 22 이고, 그다음은 이 간격입니다. */
const ACTIVE_ROW_H = 34
export class PanelsPart {
  constructor(private readonly game: Game) {}

  /** 「적용 중」 을 펼친 판. */
  readonly activePanel: ModalPanel = {
    view: new Container(),
    size: { width: 460, height: 60 },
  }

  readonly menu: ModalPanel = {
    view: new Container(),
    size: { width: 260, height: 60 },
  }

  /** 게임 방법. **첫 판에서 저절로 한 번 열립니다.** */
  /**
   * 떠 있는 판들.
   *
   * **여는 순서가 곧 위아래입니다.** 판마다 자기 층에 붙어 있으면 어느 것이 위인지가 붙인
   * 순서로 정해지고, 족보 목록이 블라인드 판 아래로 들어가는 일이 생깁니다.
   */
  readonly modals = new Modals()

  readonly guide = new Guide(
    () => this.modals.close(this.guide), () => this.game.cards.toggleHandList())

  optionsPanel!: OptionsPanel

  /** 조커 풀을 고르고 들여다보는 판. 팀이틀에서만 엽니다. */
  collection!: CollectionPanel

  /** 판을 여는 자리. 새 런 · 이어하기 · 챌린지가 탭 셋으로 들어 있습니다. */
  runPanel!: RunPanel

  /** 족보 목록. 무엇이 몇 점인지 볼 수 있어야 무엇을 키울지 정합니다. */
  readonly handList: ModalPanel = {
    view: new Container(),
    size: { width: SIZE.width, height: SIZE.height },
    // **전면 화면입니다.** 갈래 다섯이 저마다 다른 자를 쓰므로 판 하나에 담으면 갈래를
    // 옮길 때마다 판이 들썩입니다.
    fullscreen: true,
  }

  /** 족보 목록의 줄들. 어느 줄을 가리키고 있는지를 자리로 셉니다. */
  /** 「런 정보」 의 어느 갈래를 보고 있는가. */
  runInfoTab: RunInfoTab = 'hands'

  handRows: { hand: PokerHandKind; seen: boolean;
                               y: number; height: number }[] = []

  handBand?: Graphics

  handPreview?: Container

  handHovered = -1

  /** 「적용 중」에서 밝게 남아 있는 줄. 바우처를 산 직후입니다. */
  activeGlow?: { label: string; until: number; plate?: Graphics }

  /**
   * 마지막으로 센 인사이트.
   *
   * **판이 떠 있는 동안 `refresh` 마다 다시 그려지고**, 세는 것은 건식 실행 열몇 번입니다.
   * 열쇠가 같으면 다시 세지 않습니다 — 열쇠에 담는 것이 답을 바꾸는 것 전부이고, 그
   * 목록은 `doc/insight.md` 에 있습니다.
   */
  private insightCache?: { key: string; rows: Insight[] }

  /** 인사이트 갈래의 굴림통. **갈래를 오갈 때 굴린 자리를 물려받지 않습니다.** */
  insightScroll?: ScrollView

  /** 굴림통에 지금 그려져 있는 답의 열쇠. 같으면 다시 그리지 않습니다. */
  private insightDrawn?: string

  /** 지금 떠 있는 물음. 도구가 짚을 자리를 알리는 데 씁니다. */
  confirmUp?: ConfirmPanel

  handName(hand: PokerHandKind): string {
    const key = `hand.${PokerHandKind[hand]}.name`
    return text(this.game.data, key)
  }

  /**
   * 무엇이 무엇으로 바뀌었는가. **바뀌기 전과 뒤를 견줍니다.**
   *
   * 코어는 어느 칸이 바뀌었는지를 이벤트에 적어 보내지만(`CardModified.what`), 견주는 쪽이
   * 더 정확합니다 — 한 줄이 여러 칸을 함께 바꾸는 것이 있고(`CopyRight`), 같은 값으로
   * 덮어쓴 것은 바뀐 것이 아닙니다.
   *
   * **둘까지 적습니다.** 셋 이상이 갈린 것은 다른 카드가 된 것이므로 그 카드를 적습니다.
   */
  changeText(was: CardInstance, now: CardInstance): string {
    const parts: string[] = []
    if (was.suit !== now.suit) {
      parts.push(`${SUIT_PIP[was.suit] ?? ''} → ${SUIT_PIP[now.suit] ?? ''}`)
    }
    if (was.rank !== now.rank) {
      parts.push(`${rankDisplay(this.game.data, was.rank)} → ${rankDisplay(this.game.data, now.rank)}`)
    }
    // **이름은 지역화를 거칩니다.** 표의 `display` 는 글 표의 열쇠이고, 그대로 적으면
    // `enhancement.steel.name` 이 카드 위에 뜹니다.
    if (was.enhancement !== now.enhancement) parts.push(this.enhancementName(now.enhancement))
    if (was.seal !== now.seal) parts.push(this.sealName(now.seal))
    if (was.edition !== now.edition) parts.push(this.editionName(now.edition))
    if (was.bonusChips !== now.bonusChips) {
      parts.push(tf('ui.counter.chips', { n: now.bonusChips - was.bonusChips }))
    }
    const said = parts.filter(one => one !== '')
    if (said.length === 0 || said.length > 2) return this.cardLabel(now)
    return said.join('  ')
  }

  /** 카드 한 장을 짧게 적은 것 — `♥ 7`. 기호와 표의 글자이므로 말에 기대지 않습니다. */
  cardLabel(card: CardInstance): string {
    return `${SUIT_PIP[card.suit] ?? ''} ${rankDisplay(this.game.data, card.rank)}`
  }

  /**
   * 런 정보. **전면 화면이고 갈래가 다섯입니다.**
   *
   * 한 판을 도는 동안 궁금해지는 것이 다섯입니다 — 어느 족보가 몇 점인지, 이 안테의
   * 블라인드가 무엇인지, 지금 난이도가 무엇을 바꾸는지, 다음 한 수를 무엇으로 두어야
   * 하는지, 그리고 여기까지 무엇을 했는지. 판 다섯을 만들면 그 다섯을 여는 방법이
   * 저마다 달라지므로 한 화면 안의 갈래로 둡니다.
   *
   * **줄에 마우스를 올리면 그 족보를 카드로 보여 줍니다.** 「투 페어」가 무엇인지는 낱말이
   * 아니라 카드 다섯 장의 모양이고, 그 모양을 본 적이 없으면 이름만으로는 배울 수 없습니다.
   */
  drawHandList(): void {
    const layer = this.handList.view
    // **인사이트의 굴림통은 살려 둡니다.** 판이 떠 있는 동안 `refresh` 마다 여기를 지나므로,
    // 통을 새로 만들면 굴려 내려 둔 자리가 카드를 고를 때마다 맨 위로 돌아갑니다.
    if (this.insightScroll !== undefined) layer.removeChild(this.insightScroll)
    layer.removeChildren().forEach(child => child.destroy())
    this.handRows.length = 0

    const tabs: { key: RunInfoTab; label: string }[] = [
      { key: 'hands', label: t('ui.kind.poker_hand') },
      { key: 'blinds', label: t('ui.tab.blinds') },
      { key: 'stakes', label: t('ui.tab.stakes') },
      { key: 'insight', label: t('ui.tab.insight') },
      { key: 'log', label: t('ui.tab.log') },
    ]
    const here = tabs.find(tab => tab.key === this.runInfoTab) ?? tabs[0]

    layer.addChild(fullFrame(here.label, [t('ui.run_info.title')],
      () => this.game.cards.toggleHandList(), this.runInfoCorner()))

    tabs.forEach((tab, index) => {
      const chosen = this.runInfoTab === tab.key
      const button = new Button(tab.label, RUN_TAB_W, 36, chosen ? 'select' : 'neutral', () => {
        if (this.runInfoTab !== tab.key) this.insightScroll?.toTop()
        this.runInfoTab = tab.key
        this.drawHandList()
      })
      button.position.set(FULL_EDGE + index * (RUN_TAB_W + RUN_TAB_GAP), RUN_TAB_Y)
      layer.addChild(button)
      // **자리는 화면이 알립니다.** 판이 닫히면 이 단추가 지워지고 `lateSpots` 가 그것을
      // 봅니다 — 도구가 좌표를 적어 두면 폭이 바뀔 때 빈자리를 누르고 통과합니다.
      this.game.spotNodes.set(`runInfoTab:${tab.key}`,
                         { node: button, cx: RUN_TAB_W / 2, cy: 18 })
    })

    this.handList.size.width = SIZE.width
    this.handList.size.height = SIZE.height
    layer.eventMode = 'static'

    if (this.runInfoTab === 'blinds') return this.drawBlindsTab(layer)
    if (this.runInfoTab === 'stakes') return this.drawStakesTab(layer)
    if (this.runInfoTab === 'insight') return this.drawInsightTab(layer)
    if (this.runInfoTab === 'log') return this.drawLogTab(layer)
    this.drawHandsTab(layer)
  }

  /**
   * 오른쪽 위에 적는 것. **갈래마다 다릅니다** — 그 갈래를 읽는 동안 곁에 두고 싶은 값 하나입니다.
   */
  private runInfoCorner(): Container {
    const label: string = this.runInfoTab === 'stakes' ? t('ui.run_info.current')
      : this.runInfoTab === 'insight' ? t('ui.run_info.read')
        : this.runInfoTab === 'log' ? t('ui.log.moves') : t('ui.slot.ante')
    let value = `${this.game.state.ante} / ${this.game.data.run.winAnte}`
    let ink = UI.ink
    if (this.runInfoTab === 'stakes') {
      const row = stakeRow(this.game.data, this.game.state.stake)
      value = row === undefined ? '' : nameOf(this.game.data, 'stake', stakeSlug(row.stake),
                                              row.name)
      ink = UI.money
    } else if (this.runInfoTab === 'insight') {
      value = String(this.insightRows().length)
    } else if (this.runInfoTab === 'log') {
      value = tf('ui.log.moves_unit', { n: this.game.state.handsPlayedThisRun })
    }

    const node = new Container()
    const head = new Text({
      text: label,
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    head.anchor.set(1, 0)
    node.addChild(head)
    const big = new Text({
      text: value,
      style: { fontSize: TEXT.base, fill: ink, fontWeight: WEIGHT.bold },
    })
    big.anchor.set(1, 0)
    big.position.set(0, 26)
    node.addChild(big)
    return node
  }

  /** 갈래의 본문에 놓는 칸 머리 한 줄. 열 이름들입니다. */
  private columnHead(layer: Container, cols: { text: string; x: number; right?: boolean }[],
                     top = RUN_HEAD_Y): void {
    for (const col of cols) {
      const node = new Text({
        text: col.text,
        style: { fontSize: TEXT.small, fill: UI.inkFaint, fontWeight: WEIGHT.normal },
      })
      node.anchor.set(col.right === true ? 1 : 0, 0)
      node.position.set(col.x, top)
      layer.addChild(node)
    }
  }

  /** 족보 갈래. 이름 · 레벨 · 칩 × 배수 · 친 횟수입니다. */
  private drawHandsTab(layer: Container): void {
    // **아직 못 본 족보는 한 줄로 묶습니다.** 열두 줄을 다 세우면 마지막 셋이 아래 변을
    // 넘고, 그 셋은 이름도 값도 없는 줄입니다 — 몇 개가 남았는지만 적습니다.
    const all = this.game.data.tables.pokerHand.records
    const rows = all.filter(row => row.visibleFromStart
      || (this.game.state.handPlayCounts[PokerHandKind[row.hand]] ?? 0) > 0)
    const hidden = all.length - rows.length
    const left = FULL_EDGE + 20
    const right = SIZE.width - FULL_EDGE - 20
    this.columnHead(layer, [
      { text: t('ui.kind.poker_hand'), x: left },
      { text: t('ui.col.level'), x: RUN_COL_LEVEL },
      { text: t('ui.col.chips_mult'), x: RUN_COL_VALUE },
      { text: t('ui.col.played'), x: right, right: true },
    ])

    const band = new Graphics()
    layer.addChild(band)

    rows.forEach((row, index) => {
      const key = PokerHandKind[row.hand]
      const level = this.game.state.handLevels[key] ?? 1
      const chips = row.baseChips + row.chipsPerLevel * (level - 1)
      const mult = row.baseMult + row.multPerLevel * (level - 1)
      const seen = row.visibleFromStart || (this.game.state.handPlayCounts[key] ?? 0) > 0
      const y = RUN_ROWS_Y + index * RUN_ROW_H

      // **한 줄 걸러 한 줄만 옅게 깔립니다.** 아홉 줄이 같은 바탕이면 눈이 가로로 미끄러져
      // 이름과 횟수가 어긋나 읽힙니다.
      if (index % 2 === 0) {
        const plate = new Graphics()
        plate.rect(FULL_EDGE, y - 6, SIZE.width - FULL_EDGE * 2, RUN_ROW_H)
          .fill({ color: UI.cell, alpha: 0.5 })
        layer.addChild(plate)
      }

      const name = new Text({
        text: seen ? this.handName(row.hand) : '???',
        style: { fontSize: TEXT.base, fill: seen ? UI.ink : UI.inkDim, fontWeight: WEIGHT.bold },
      })
      name.anchor.set(0, 0.5)
      name.position.set(left, y + RUN_ROW_H / 2 - 6)

      const lv = new Text({
        text: tf('ui.hand.level_short', { level }),
        style: { fontSize: TEXT.small, fill: level > 1 ? UI.chips : UI.inkDim,
          fontWeight: level > 1 ? WEIGHT.bold : WEIGHT.normal },
      })
      lv.anchor.set(0, 0.5)
      lv.position.set(RUN_COL_LEVEL, y + RUN_ROW_H / 2 - 6)

      const value = new Container()
      if (seen) {
        const c = new Text({
          text: String(chips),
          style: { fontSize: TEXT.base, fill: UI.chips, fontWeight: WEIGHT.bold },
        })
        c.anchor.set(0, 0.5)
        const sign = new Text({
          text: TIMES_SIGN,
          style: { fontSize: TEXT.small, fill: UI.inkFaint, fontWeight: WEIGHT.normal },
        })
        sign.anchor.set(0, 0.5)
        sign.position.set(c.width + 12, 0)
        const m = new Text({
          text: String(mult),
          style: { fontSize: TEXT.base, fill: UI.mult, fontWeight: WEIGHT.bold },
        })
        m.anchor.set(0, 0.5)
        m.position.set(c.width + 12 + sign.width + 12, 0)
        value.addChild(c, sign, m)
      } else {
        const dash = new Text({
          text: DASH, style: { fontSize: TEXT.base, fill: UI.inkFaint },
        })
        dash.anchor.set(0, 0.5)
        value.addChild(dash)
      }
      value.position.set(RUN_COL_VALUE, y + RUN_ROW_H / 2 - 6)

      const played = new Text({
        text: tf('ui.hand.times', { n: this.game.state.handPlayCounts[key] ?? 0 }),
        style: { fontSize: TEXT.small, fill: UI.inkDim },
      })
      played.anchor.set(1, 0.5)
      played.position.set(right, y + RUN_ROW_H / 2 - 6)

      layer.addChild(name, lv, value, played)
      this.handRows.push({ hand: row.hand, seen, y: y - 6, height: RUN_ROW_H })
    })

    if (hidden > 0) {
      const y = RUN_ROWS_Y + rows.length * RUN_ROW_H
      const more = new Text({
        text: '???',
        style: { fontSize: TEXT.base, fill: UI.inkFaint, fontWeight: WEIGHT.bold },
      })
      more.anchor.set(0, 0.5)
      more.position.set(left, y + RUN_ROW_H / 2 - 6)
      const count = new Text({
        text: tf('ui.active.more', { n: hidden }),
        style: { fontSize: TEXT.small, fill: UI.inkFaint },
      })
      count.anchor.set(1, 0.5)
      count.position.set(right, y + RUN_ROW_H / 2 - 6)
      layer.addChild(more, count)
    }

    // **가리킨 줄의 그림은 맨 위입니다.** 줄보다 먼저 붙이면 글자가 그림 위에 겹칩니다.
    const preview = new Container()
    preview.visible = false
    layer.addChild(preview)
    this.handBand = band
    this.handPreview = preview
    this.handHovered = -1
  }

  /**
   * 블라인드 갈래.
   *
   * **이 안테의 셋을 딱지로 늘어놓고, 그 아래에 안테별 요구 점수를 한 줄로 둡니다.**
   * 줄로만 적으면 지금 어느 것과 붙고 있는지가 글에서만 읽히고, 요구 점수가 안테를 따라
   * 어떻게 자라는지는 어디에도 남지 않습니다.
   */
  private drawBlindsTab(layer: Container): void {
    const kinds = [BlindKind.Small, BlindKind.Big, BlindKind.Boss]
    const cardW = (SIZE.width - FULL_EDGE * 2 - BLIND_GAP * 2 - 88) / 3
    const startX = (SIZE.width - (cardW * 3 + BLIND_GAP * 2)) / 2

    kinds.forEach((blind, index) => {
      const bossRow = blind === BlindKind.Boss
        ? this.game.data.tables.bossBlind.findByBossId(this.game.state.bossId) : undefined
      const tone = blind === BlindKind.Boss ? UI.red
        : blind === BlindKind.Big ? UI.legendary : UI.bar
      const x = startX + index * (cardW + BLIND_GAP)
      const card = new Container()
      card.position.set(x, BLIND_TOP)

      const skin = piece('plate', cardW, BLIND_H, plateTint(UI.panel))
      if (skin !== undefined) card.addChild(skin)
      else {
        const plate = new Graphics()
        plate.rect(0, 0, cardW, BLIND_H).fill(UI.panel)
        card.addChild(plate)
      }
      const band = piece('head', cardW, 48, mix(tone, UI.panel, 0.62))
      if (band !== undefined) card.addChild(band)

      const name = new Text({
        text: bossRow
          ? nameOf(this.game.data, 'boss', this.game.state.bossId, bossRow.name)
          : tf('ui.blind.named', { name: blindName(blind) }),
        style: { fontSize: TEXT.base, fill: mix(tone, UI.ink, 0.35), fontWeight: WEIGHT.bold },
      })
      name.anchor.set(0.5, 0.5)
      name.position.set(cardW / 2, 24)
      card.addChild(name)

      const caption = new Text({
        text: t('ui.label.target'),
        style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      caption.anchor.set(0.5, 0)
      caption.position.set(cardW / 2, 74)
      card.addChild(caption)

      const need = new Text({
        text: targetOf(this.game.data, this.game.state, blind).toLocaleString('en-US'),
        style: { fontSize: STEP[3], fill: tone, fontWeight: WEIGHT.bold, fontFamily: NUMERALS },
      })
      need.anchor.set(0.5, 0)
      need.position.set(cardW / 2, 96)
      card.addChild(need)

      const reward = richLine(tf('ui.blind.reward',
        { n: rewardOf(this.game.data, this.game.state, blind) }),
        richStyle('note', { fontWeight: WEIGHT.normal }))
      reward.position.set((cardW - reward.width) / 2, 162)
      card.addChild(reward)

      if (bossRow) {
        const note = richBlock(
          [describe(this.game.data, this.game.data.bossEffects.get(this.game.state.bossId)
            ?? []).join(' · ')],
          richStyle('note'), richLeading('note'), cardW - 40, 'center')
        note.position.set(20, 206)
        card.addChild(note)
      }

      layer.addChild(card)

      // 딱지 밑에 이 블라인드가 지금 어디인지 한 낱말.
      const where = this.game.state.blind === blind ? t('ui.run_info.current')
        : blind < this.game.state.blind ? t('ui.blind.done') : t('ui.blind.next')
      const mark = new Text({
        text: where,
        style: {
          fontSize: TEXT.small,
          fill: this.game.state.blind === blind ? UI.money : UI.inkDim,
          fontWeight: this.game.state.blind === blind ? WEIGHT.bold : WEIGHT.normal,
        },
      })
      mark.anchor.set(0.5, 0)
      mark.position.set(x + cardW / 2, BLIND_TOP + BLIND_H + 14)
      layer.addChild(mark)
    })

    // 안테별 요구 점수.
    const head = sectionHead(SIZE.width - FULL_EDGE * 2, t('ui.run_info.ante_targets'),
      undefined, false)
    head.position.set(FULL_EDGE, ANTE_HEAD_Y)
    layer.addChild(head)

    // **안테 0 은 적지 않습니다.** 표의 첫 줄은 셈의 바닥이고 사람이 붙는 판이 아닙니다.
    const antes = this.game.data.tables.ante.records.filter(row => row.ante > 0)
    const cellW = (SIZE.width - FULL_EDGE * 2) / antes.length
    antes.forEach((row, index) => {
      const at = row.ante === this.game.state.ante
      const x = FULL_EDGE + index * cellW
      if (at) {
        const plate = new Graphics()
        plate.rect(x, ANTE_ROW_Y, cellW, ANTE_ROW_H).fill({ color: UI.cell, alpha: 0.9 })
        plate.rect(x, ANTE_ROW_Y, cellW, 2).fill(UI.chips)
        layer.addChild(plate)
      }
      const label = new Text({
        text: `${t('ui.slot.ante')} ${row.ante}`,
        style: { fontSize: TEXT.small, fill: at ? UI.chips : UI.inkDim,
          fontWeight: at ? WEIGHT.bold : WEIGHT.normal },
      })
      label.anchor.set(0.5, 0)
      label.position.set(x + cellW / 2, ANTE_ROW_Y + 18)
      const value = new Text({
        text: targetOf(this.game.data, { ...this.game.state, ante: row.ante },
          BlindKind.Small).toLocaleString('en-US'),
        style: { fontSize: TEXT.base, fill: at ? UI.ink : UI.inkDim, fontWeight: WEIGHT.bold,
          fontFamily: NUMERALS },
      })
      value.anchor.set(0.5, 0)
      value.position.set(x + cellW / 2, ANTE_ROW_Y + 38)
      layer.addChild(label, value)
    })
  }

  /**
   * 스테이크 갈래.
   *
   * **누적입니다** — 뒤의 것은 앞의 것을 전부 포함합니다. 그래서 줄의 차례가 곧 난이도의
   * 차례이고, 지금 것 위쪽은 이미 걸려 있는 것입니다.
   */
  private drawStakesTab(layer: Container): void {
    const lead = new Text({
      text: t('ui.stake.order'),
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    lead.position.set(FULL_EDGE, RUN_HEAD_Y)
    layer.addChild(lead)

    const now = stakeRow(this.game.data, this.game.state.stake)?.stake
    const rows = this.game.data.tables.stake.records
    const left = FULL_EDGE + 20
    const right = SIZE.width - FULL_EDGE - 20
    rows.forEach((row, index) => {
      const at = row.stake === now
      const y = RUN_ROWS_Y + index * STAKE_ROW_H

      const plate = new Graphics()
      plate.rect(FULL_EDGE, y - 6, SIZE.width - FULL_EDGE * 2, STAKE_ROW_H)
        .fill({ color: UI.cell, alpha: at ? 0.95 : index % 2 === 0 ? 0.5 : 0.2 })
      if (at) plate.rect(FULL_EDGE, y - 6, 4, STAKE_ROW_H).fill(UI.chips)
      layer.addChild(plate)

      // 그 스테이크의 색 한 조각. **이름보다 이것이 먼저 눈에 듭니다.**
      const chip = new Graphics()
      chip.rect(left, y + STAKE_ROW_H / 2 - 16, 60, 22)
        .fill({ color: STAKE_INK[row.stake] ?? UI.inkDim, alpha: at ? 1 : 0.55 })
      layer.addChild(chip)

      const name = new Text({
        text: nameOf(this.game.data, 'stake', stakeSlug(row.stake), row.name),
        style: { fontSize: TEXT.base, fill: at ? UI.ink : UI.inkDim, fontWeight: WEIGHT.bold },
      })
      name.anchor.set(0, 0.5)
      name.position.set(left + 80, y + STAKE_ROW_H / 2 - 5)

      const note = new Text({
        text: tf('ui.stake.note', {
          column: row.anteColumn, reward: row.smallBlindReward, discards: row.discardsDelta,
        }),
        style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      note.anchor.set(0, 0.5)
      note.position.set(left + 260, y + STAKE_ROW_H / 2 - 5)

      const mark = new Text({
        text: at ? t('ui.run_info.current') : t('ui.run_info.locked'),
        style: { fontSize: TEXT.small, fill: at ? UI.chips : UI.inkFaint,
          fontWeight: at ? WEIGHT.bold : WEIGHT.normal },
      })
      mark.anchor.set(1, 0.5)
      mark.position.set(right, y + STAKE_ROW_H / 2 - 5)

      layer.addChild(name, note, mark)
    })
  }

  /**
   * 지금 판의 인사이트.
   *
   * **판이 떠 있을 때만 셉니다.** 세는 것은 건식 실행 열몇 번이고, 닫힌 판을 위해 그것을
   * 도는 것은 낭비입니다 — `refresh` 가 떠 있는 판만 다시 그립니다.
   *
   * 열쇠가 같으면 앞서 센 답을 그대로 씁니다. **열쇠는 상태의 해시와 고른 카드입니다** —
   * 답을 바꾸는 것을 손으로 세어 적으면 하나가 빠지고, 빠진 것이 바뀐 판에 낡은 조언을
   * 남깁니다. 고름은 아직 액션이 아니므로 상태에 없고, 그래서 따로 붙습니다.
   */
  insightRows(): Insight[] {
    const picked = [...this.game.cards.selected].sort((a, b) => a - b)
    const key = `${snapshotHash(this.game.state)}|${picked.join('.')}`
    if (this.insightCache?.key !== key) {
      this.insightCache = { key, rows: insights(this.game.data, this.game.state, picked) }
    }
    return this.insightCache.rows
  }

  /**
   * 인사이트 갈래.
   *
   * **등급은 왼쪽 끝의 띠입니다.** 글의 색으로 알리면 읽는 색이 셋이 되고, 그러면 강조한
   * 숫자와 구분되지 않습니다. 오른쪽 위에 그 띠 셋이 무엇인지 적힙니다.
   */
  private drawInsightTab(layer: Container): void {
    const lead = new Text({
      text: t('ui.insight.lead'),
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    lead.position.set(FULL_EDGE, RUN_HEAD_Y)
    layer.addChild(lead)

    // 띠 셋의 뜻. 오른쪽 끝에서 왼쪽으로 쌓습니다.
    let x = SIZE.width - FULL_EDGE
    for (const level of ['warn', 'advise', 'info'] as const) {
      const name = new Text({
        text: t(`ui.insight.level.${level}`),
        style: { fontSize: TEXT.small, fill: INSIGHT_COLOR[level], fontWeight: WEIGHT.bold },
      })
      name.anchor.set(1, 0)
      name.position.set(x, RUN_HEAD_Y)
      const bar = new Graphics()
      bar.rect(x - name.width - 8, RUN_HEAD_Y + 1, 2, 12).fill(INSIGHT_COLOR[level])
      layer.addChild(bar, name)
      x -= name.width + 26
    }

    const width = SIZE.width - FULL_EDGE * 2
    const scroll = this.insightScroll ?? new ScrollView(width, FULL_FOOT_Y - RUN_ROWS_Y - 8)
    this.insightScroll = scroll
    scroll.position.set(FULL_EDGE, RUN_ROWS_Y - 6)
    layer.addChild(scroll)

    // **답이 같으면 다시 그리지 않습니다.** 판이 떠 있는 동안 `refresh` 마다 여기를 지나고,
    // 줄마다 판과 접힌 글을 만드는 것이 그 비용입니다.
    const key = this.insightCache?.key ?? ''
    if (this.insightDrawn === key) return
    this.insightDrawn = key
    scroll.content.removeChildren().forEach(child => child.destroy())

    const rows = this.insightRows()
    if (rows.length === 0) {
      const none = new Text({
        text: t('ui.insight.none'),
        style: { fontSize: TEXT.base, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      none.anchor.set(0.5, 0)
      none.position.set(width / 2, 40)
      scroll.content.addChild(none)
      scroll.refresh()
      return
    }

    let y = 0
    for (const row of rows) y += this.drawInsightLine(scroll.content, width, y, row)
    scroll.refresh()
  }

  /**
   * 줄 하나. 그린 높이를 돌려줍니다.
   *
   * **갈래 이름이 줄 안에 있습니다.** 무리 머리로 묶어 세우던 것을 걷었습니다 — 갈래마다
   * 한 줄인 대목이 많아 머리와 줄이 번갈아 놓였고, 그러면 목록이 아니라 계단으로 읽힙니다.
   */
  private drawInsightLine(into: Container, width: number, y: number, row: Insight): number {
    const step = richLeading('body')
    const text = richBlock([tf(`ui.insight.${row.key}`, row.values)],
                           richStyle('body'), step, width - INSIGHT_TEXT_X - 120)
    const wrapped = (text as Container & { rows?: number }).rows ?? 1
    const rowH = Math.max(INSIGHT_ROW_H, wrapped * step + 24)

    const node = new Container()
    node.position.set(0, y)

    const plate = new Graphics()
    plate.rect(0, 0, width, rowH).fill({ color: UI.cell, alpha: 0.5 })
    plate.rect(0, 0, width, 1).fill({ color: UI.hairline, alpha: 0.5 })
    plate.rect(20, (rowH - 16) / 2, 4, 16).fill(INSIGHT_COLOR[row.level])
    node.addChild(plate)

    const group = new Text({
      text: t(`ui.insight.group.${row.group}`),
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    group.anchor.set(0, 0.5)
    group.position.set(44, rowH / 2 - 5)
    node.addChild(group)

    text.position.set(INSIGHT_TEXT_X, (rowH - wrapped * step) / 2)
    node.addChild(text)

    const tag = new Text({
      text: t(`ui.insight.level.${row.level}`),
      style: { fontSize: TEXT.small, fill: INSIGHT_COLOR[row.level], fontWeight: WEIGHT.bold },
    })
    tag.anchor.set(1, 0.5)
    tag.position.set(width - 20, rowH / 2 - 5)
    node.addChild(tag)

    // 쪽지를 가진 줄만 눌러 볼 것이 있습니다.
    if (row.lines.length > 0) {
      node.eventMode = 'static'
      node.cursor = 'pointer'
      node.hitArea = new Rectangle(0, 0, width, rowH)
      const label = t(`ui.insight.group.${row.group}`)
      // **쪽지는 커서를 따라갑니다.** 줄의 자리로 띄우면 굴린 만큼 어긋나고, 굴린 양은
      // 이 자리에서 알 수 없습니다.
      this.game.input.tipOn(node, at => {
        this.game.input.tooltip.show(label, '', 0, row.lines, at, SIZE)
      })
    }

    into.addChild(node)
    return rowH
  }

  /**
   * 기록 갈래.
   *
   * **아직 붙지 않은 기능입니다.** 화면과 문법을 먼저 정해 두는 자리이고, 지금 셀 수 있는
   * 값 셋만 위에 놓습니다 — 한 수씩 남기기 시작하면 그 아래를 그대로 채웁니다.
   */
  private drawLogTab(layer: Container): void {
    const cells: { name: string; value: string; ink: number }[] = [
      { name: t('ui.log.played'), value: String(this.game.state.handsPlayedThisRun),
        ink: UI.ink },
      { name: t('ui.over.best_hand'),
        value: this.game.session.metrics.bestHand.toLocaleString('en-US'), ink: UI.chips },
      { name: t('ui.over.money'), value: `$${this.game.state.money}`, ink: UI.money },
    ]
    const gap = 16
    const cellW = (SIZE.width - FULL_EDGE * 2 - gap * (cells.length - 1)) / cells.length
    cells.forEach((cell, index) => {
      const x = FULL_EDGE + index * (cellW + gap)
      const box = new Container()
      box.position.set(x, LOG_CELL_Y)
      const skin = piece('well', cellW, LOG_CELL_H, wellTint(UI.cell))
      if (skin !== undefined) box.addChild(skin)
      else {
        const plate = new Graphics()
        plate.rect(0, 0, cellW, LOG_CELL_H).fill(UI.cell)
        box.addChild(plate)
      }
      const name = new Text({
        text: cell.name,
        style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      name.anchor.set(0, 0.5)
      name.position.set(16, LOG_CELL_H / 2 - 5)
      const value = new Text({
        text: cell.value,
        style: { fontSize: TEXT.base, fill: cell.ink, fontWeight: WEIGHT.bold,
          fontFamily: NUMERALS },
      })
      value.anchor.set(1, 0.5)
      value.position.set(cellW - 16, LOG_CELL_H / 2 - 5)
      box.addChild(name, value)
      layer.addChild(box)
    })

    // **값 칸 아래입니다.** 열 이름 줄의 자리에 두었더니 칸들과 겹쳤습니다.
    this.columnHead(layer, [
      { text: t('ui.col.cards'), x: FULL_EDGE + 20 },
      { text: t('ui.col.chips_mult'), x: RUN_COL_VALUE },
      { text: t('ui.col.score'), x: SIZE.width - FULL_EDGE - 20, right: true },
    ], LOG_CELL_Y + LOG_CELL_H + 20)

    const soon = new Text({
      text: t('ui.log.soon'),
      style: { fontSize: TEXT.base, fill: UI.money, fontWeight: WEIGHT.bold },
    })
    soon.anchor.set(0.5, 0)
    soon.position.set(SIZE.width / 2, 420)
    const note = new Text({
      text: t('ui.log.note'),
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    note.anchor.set(0.5, 0)
    note.position.set(SIZE.width / 2, 456)
    layer.addChild(soon, note)
  }

  /**
   * 어느 줄을 가리키고 있는가.
   *
   * **화면이 이미 재고 있는 커서 자리를 씁니다.** 줄마다 사건을 붙이는 것보다 자리 하나를
   * 견주는 편이 확실합니다 — 그리는 것이 없는 통은 저절로 잡히지 않습니다.
   */
  updateHandHover(): void {
    if (!this.modals.has(this.handList) || this.handRows.length === 0) return
    if (this.runInfoTab !== 'hands') return

    const local = this.handList.view.toLocal(this.game.world.toGlobal(this.game.input.pointerAt))
    const width = this.handList.size.width
    let found = -1
    if (local.x >= 12 && local.x <= width - 12) {
      found = this.handRows.findIndex(
        row => local.y >= row.y - 4 && local.y < row.y + row.height - 6)
    }
    if (found === this.handHovered) return
    this.handHovered = found

    const band = this.handBand
    if (band) {
      band.clear()
      const row = this.handRows[found]
      if (row) {
        band.rect(12, row.y - 4, width - 24, row.height - 2)
          .fill({ color: UI.pick, alpha: 0.32 })
      }
    }

    const preview = this.handPreview
    if (!preview) return
    const row = this.handRows[found]
    if (!row) {
      preview.visible = false
      return
    }
    this.showHandShape(preview, row.hand, row.seen, width,
      row.y + row.height - 4, this.handList.size.height)
  }

  /**
   * 그 족보가 어떤 모양인가.
   *
   * **카드 다섯 장으로 보여 줍니다.** 족보에 드는 카드는 밝고 들지 않는 카드는 물러납니다 —
   * 「투 페어」에서 다섯째 장이 세지 않는다는 것이 그 그림에 있어야 합니다.
   *
   * 가리킨 줄 바로 아래에, **판의 너비를 꽉 채워** 놓입니다. 판 아래로 넘치면 줄 위로
   * 올라갑니다.
   */
  private showHandShape(into: Container, hand: PokerHandKind, seen: boolean,
                        width: number, below: number, panelHeight: number): void {
    into.removeChildren().forEach(child => child.destroy())
    into.visible = true

    const cardW = 52
    const cardH = 73
    const gap = 8
    const boxW = width - 24
    const shape = seen ? HAND_SHAPE[hand] : undefined
    const boxH = shape ? cardH + 24 : 46

    const board = new Graphics()
    board.rect(0, 0, boxW, boxH).fill({ color: UI.panel, alpha: 0.98 })
    board.rect(0.5, 0.5, boxW - 1, boxH - 1)
      .stroke({ color: UI.panelEdge, width: 1.5 })
    into.addChild(board)

    if (!shape) {
      const veiled = new Text({
        text: t('ui.hand.never_played'),
        style: { fontSize: TEXT.small, fill: UI.inkDim },
      })
      veiled.anchor.set(0.5, 0.5)
      veiled.position.set(boxW / 2, boxH / 2)
      into.addChild(veiled)
    } else {
      const span = shape.length * cardW + (shape.length - 1) * gap
      const startX = (boxW - span) / 2
      shape.forEach((spot, index) => {
        const card: CardInstance = {
          uid: -1 - index, baseCardId: '', rank: spot.rank, suit: spot.suit,
          enhancement: EnhancementKind.None, seal: SealKind.None, edition: EditionKind.Base,
          bonusChips: 0, debuffed: false, faceDown: false,
        }
        const mini = this.miniCard(card, spot.counts, cardW, cardH)
        mini.position.set(startX + index * (cardW + gap), 12)
        into.addChild(mini)
      })
    }

    // 판 아래로 넘치면 줄 위로 올라갑니다.
    const under = below + 6
    const floor = panelHeight - FOOTER_BAR - 8
    const y = under + boxH > floor ? below - boxH - 40 : under
    into.position.set(12, y)
  }

  drawDeckView(): void {
    const layer = this.game.cards.deckView.view
    layer.removeChildren().forEach(child => child.destroy())

    const state = this.game.state
    const suits = [...this.game.data.tables.suit.records].sort((a,
      b) => a.sortOrder - b.sortOrder)
    const alive = new Set(state.drawPile)
    const held = new Set(this.game.shown.hand)

    const rows = suits.map(suitRow => ({
      suit: suitRow,
      cards: state.deck
        .filter(card => card.suit === suitRow.suit)
        .sort((a, b) => a.rank - b.rank),
    }))
    const widest = Math.max(1, ...rows.map(row => row.cards.length))

    const cardW = 56
    const cardH = 79
    // 겹치는 폭. **얼굴의 왼쪽 절반이 보이면 랭크와 무늬가 읽힙니다.**
    const step = 30
    const left = 84
    const rowH = cardH + 16
    const width = left + (widest - 1) * step + cardW + 26
    const gridTop = TITLE_BAR + 62
    const height = gridTop + rows.length * rowH + 16 + FOOTER_BAR

    layer.addChild(panelFrame(width, height, t('ui.button.deck_view'),
      () => this.game.cards.toggleDeckView()))

    const label = (text: string, size: number, fill: number, weight = '700') =>
      new Text({ text, style: { fontSize: size, fill, fontWeight: weight as never } })

    const total = label(`${state.drawPile.length} / ${state.deck.length}`, 15, UI.chips, '800')
    total.anchor.set(0.5, 0)
    total.position.set(width / 2, TITLE_BAR + 12)
    const legend = label(
      t('ui.deck_view.note'),
      11, UI.inkDim, '600')
    legend.anchor.set(0.5, 0)
    legend.position.set(width / 2, TITLE_BAR + 34)
    layer.addChild(total, legend)

    rows.forEach((row, line) => {
      const y = gridTop + line * rowH
      const leftIn = row.cards.filter(card => alive.has(card.uid)).length

      // **어두운 판 위이므로 검정이 아니라 잉크색으로 그립니다.** 세트가 검정으로 정한
      // 무늬는 이 판에서 보이지 않습니다.
      const dark = suitInk(row.suit.suit) === COLOR.black
      const mark = label(SUIT_PIP[row.suit.suit] ?? row.suit.letter, 26,
        dark ? UI.ink : suitInk(row.suit.suit), '800')
      mark.anchor.set(0.5, 0)
      mark.position.set(34, y + 16)
      const count = label(`${leftIn}/${row.cards.length}`, 11, UI.inkDim, '700')
      count.anchor.set(0.5, 0)
      count.position.set(34, y + 50)
      layer.addChild(mark, count)

      row.cards.forEach((card, index) => {
        const mini = this.miniCard(card, alive.has(card.uid), cardW, cardH)
        mini.position.set(left + index * step, y)
        // **오른쪽이 위입니다.** 손패를 부챗살로 펴는 것과 같은 순서라 눈이 헷갈리지 않습니다.
        mini.zIndex = index
        mini.eventMode = 'static'
        // 겹쳐 놓았으므로 **보이는 만큼만** 잡습니다. 카드 전체를 잡으면 뒤의 카드가
        // 앞의 카드에 가려 눌리지 않습니다.
        mini.hitArea = new Rectangle(0, 0,
          index === row.cards.length - 1 ? cardW : step, cardH)
        mini.cursor = 'pointer'
        mini.on('pointertap', event => {
          event.stopPropagation()
          if (this.game.input.ate()) return
          this.showCardTip(card, alive.has(card.uid), held.has(card.uid), mini)
        })
        layer.addChild(mini)
      })
    })

    layer.sortableChildren = true

    const remaining = state.deck.filter(card => alive.has(card.uid))
    const faces = remaining.filter(
      card => this.game.data.tables.rank.findByRank(card.rank)?.isFace).length
    const aces = remaining.filter(card => card.rank === RankKind.Ace).length
    const enhanced = remaining.filter(card => card.enhancement !== EnhancementKind.None).length
    const sealed = remaining.filter(card => card.seal !== SealKind.None).length

    const foot = label(
      tf('ui.deck_view.counts', { faces, aces, enhanced, sealed }),
      12, UI.inkDim)
    foot.anchor.set(0.5, 1)
    foot.position.set(width / 2, height - FOOTER_BAR - 10)
    layer.addChild(foot)

    this.game.cards.deckView.size.width = width
    this.game.cards.deckView.size.height = height
  }

  /**
   * 덱 판에 놓이는 카드 한 장.
   *
   * **손패의 카드와 같은 그림입니다.** 작다고 다른 얼굴을 쓰면 판을 보고 손패를 찾을 때
   * 한 번 더 옮겨 읽어야 합니다.
   */
  private miniCard(card: CardInstance, alive: boolean, w: number, h: number): Container {
    const node = new Container()
    const paint = ENHANCEMENT_PAPER[card.enhancement] ?? cardPaper()

    // **나간 카드도 불투명합니다.** 반투명하면 뒤의 카드가 비쳐 겹친 자리가 지저분해지고,
    // 겹쳐 놓은 줄에서는 그 자리가 카드마다 다릅니다 — 어둡게만 두면 깔끔합니다.
    const body = new Graphics()
    body.rect(0, 0, w, h).fill(alive ? cardPaper() : UI.locked)
    node.addChild(body)

    const ink = alive ? suitInk(card.suit) : DEAD.ink
    const dir = cardArtDir()
    const texture = dir === undefined
      ? undefined : artFor(dir, cardArtId(card.suit, card.rank))
    if (texture) {
      const picture = new Sprite(texture)
      picture.width = w
      picture.height = h
      picture.tint = alive ? paint : DEAD.art
      node.addChild(picture)
    }

    // **모서리의 랭크는 그림 위에도 적힙니다.** 정본 한 벌만 그림에 랭크가 들어 있고,
    // 우리가 굽는 세트는 그림 카드 12컷뿐입니다 — 여기서 빼면 이 판에서 J·Q·K 를 서로
    // 구별할 수 없습니다.
    if (texture === undefined || drawsIndex()) {
      const face = new Graphics()
      if (texture === undefined) drawFace(face, card.suit, card.rank, w, h, ink)
      const rank = new Text({
        text: MINI_RANK[card.rank] ?? '?',
        style: { fontSize: TEXT.mini, fill: ink, fontWeight: WEIGHT.bold },
      })
      rank.position.set(3, 1)
      node.addChild(face, rank)
    }

    // **테두리는 그림 위에 그립니다.** 그림이 카드를 덮으므로 종이에 그으면 가려집니다.
    const edge = new Graphics()
    edge.rect(0.5, 0.5, w - 1, h - 1)
      .stroke({ color: alive ? COLOR.cardEdge : DEAD.edge, width: 1 })
    node.addChild(edge)

    if (card.seal !== SealKind.None) {
      const seal = new Graphics()
      seal.circle(w - 9, 9, 4.5)
        .fill({ color: SEAL_INK[card.seal] ?? UI.ink, alpha: alive ? 1 : 0.4 })
      node.addChild(seal)
    }
    if (card.edition !== EditionKind.Base) {
      const spark = new Graphics()
      spark.rect(3, h - 8, w - 6, 4)
        .fill({ color: UI.mult, alpha: alive ? 0.9 : 0.3 })
      node.addChild(spark)
    }

    return node
  }

  /**
   * 덱 판에서 카드 한 장을 눌렀을 때.
   *
   * **지금 어디에 있는가가 첫 줄입니다** — 덱에 남았는지, 손에 있는지, 이미 나갔는지가
   * 이 판을 여는 이유이기 때문입니다.
   */
  showCardTip(card: CardInstance, alive: boolean, inHand: boolean,
                      at: Container): void {
    const rank = this.game.data.tables.rank.findByRank(card.rank)
    const name = `${MINI_RANK[card.rank] ?? '?'} ${SUIT_PIP[card.suit] ?? ''}`

    const lines: string[] = [
      alive ? t('ui.deck.left') : inHand ? t('ui.deck.in_hand') : t('ui.deck.gone'),
      tf('ui.card.chips', { n: (rank?.chips ?? 0) + card.bonusChips })
        + (card.bonusChips > 0 ? tf('ui.card.chips_split', { base: rank?.chips ?? 0,
          bonus: card.bonusChips }) : ''),
    ]
    if (card.enhancement !== EnhancementKind.None) {
      lines.push(tf('ui.card.enhancement', { name: this.enhancementName(card.enhancement) }))
    }
    if (card.seal !== SealKind.None) lines.push(tf('ui.card.seal',
      { name: this.sealName(card.seal) }))
    if (card.edition !== EditionKind.Base) {
      lines.push(tf('ui.card.edition', { name: this.editionName(card.edition) }))
    }
    if (card.debuffed) lines.push(t('ui.note.disabled_this_round'))

    this.game.input.tooltip.show(name, alive ? t('ui.kind.deck') : inHand ? t('ui.kind.hand')
        : t('ui.kind.gone'), alive ? 3 : 1,
      lines, this.game.input.tipBox(at), { width: SIZE.width, height: SIZE.height })
  }

  /**
   * 표시 이름 셋.
   *
   * **표의 `display` 를 거쳐 글 표로 갑니다** — 이름을 화면에 손으로 적으면 지역화가 그
   * 자리를 지나칩니다.
   */
  private enhancementName(kind: EnhancementKind): string {
    return this.localized(this.game.data.tables.enhancement.findByEnhancement(kind)?.display)
      ?? EnhancementKind[kind]
  }

  private sealName(kind: SealKind): string {
    return this.localized(this.game.data.tables.seal.findBySeal(kind)?.display) ?? SealKind[kind]
  }

  editionName(kind: EditionKind): string {
    return this.localized(this.game.data.tables.edition.findByEdition(kind)?.display)
      ?? EditionKind[kind]
  }

  /**
   * 규칙 하나의 이름.
   *
   * **글 표에서 옵니다.** 규칙의 이름은 `allCardsScore` 같은 식별자이고, 그것이 화면에
   * 그대로 뜨면 무엇이 바뀐 것인지 읽을 수 없습니다.
   *
   * 받는 이름은 `Rules` 의 필드 이름입니다 — 「적용 중」 목록도 쪽지도 그것으로 부릅니다.
   * 값을 남기지 않는 규칙만 `RuleKind` 의 이름이고, 그것들은 `Rules` 에 필드가 없어서
   * 두 이름이 겹치지 않습니다.
   */
  ruleName(rule: string): string {
    return text(this.game.data, `rule.${snake(rule)}.name`)
  }

  /**
   * `<표>.<식별자>` 하나의 이름.
   *
   * **없는 열쇠는 비웁니다.** `text` 는 없는 열쇠를 그대로 돌려주므로, 그것을 화면에 쓰면
   * `voucher.magic_trick.name` 이 머리글에 뜹니다 — 규칙 알림 판이 실제로 그랬습니다.
   */
  keyName(prefix: string): string | undefined {
    if (prefix === '' || prefix.endsWith('.')) return undefined
    const key = `${prefix}.name`
    const found = text(this.game.data, key)
    return found === key ? undefined : found
  }

  /** 그 표의 그 식별자의 이름. 없으면 비웁니다. */
  ownerName(source: string, owner: string): string | undefined {
    return this.keyName(`${source}.${owner}`)
  }

  /** 글 표에 있으면 그 말, 없으면 적힌 그대로. */
  private localized(key: string | undefined): string | undefined {
    if (key === undefined || key === '') return undefined
    return text(this.game.data, key)
  }

  /**
   * 지금 걸려 있는 것들.
   *
   * 셋을 한 목록으로 봅니다 — 들고 있는 태그, 산 바우처, **기본값과 다른 규칙**. 마지막
   * 것이 요점입니다: 손패가 11장인 이유는 조커일 수도 덱일 수도 바우처일 수도 있고,
   * 그것들을 하나씩 눌러 보게 할 수는 없습니다.
   */
  private activeEntries(): ActiveEntry[] {
    const out: ActiveEntry[] = []

    for (const tag of this.game.state.tagsPending) {
      out.push({
        label: nameOf(this.game.data, 'tag', tag, tag),
        value: t('ui.kind.tag'),
        lines: describe(this.game.data, this.game.data.tagEffects.get(tag) ?? []),
      })
    }

    for (const id of this.game.state.vouchers) {
      out.push({
        label: nameOf(this.game.data, 'voucher', id, id),
        value: t('ui.kind.voucher'),
        lines: describe(this.game.data, this.game.data.voucherEffects.get(id) ?? []),
      })
    }

    const base = defaultRules(this.game.data) as unknown as Record<string, unknown>
    const now = this.game.state.rules as unknown as Record<string, unknown>
    for (const key of Object.keys(base)) {
      const was = base[key]
      const is = now[key]
      if (was === is) continue
      if (typeof is === 'boolean') {
        out.push({ label: this.ruleName(key), value: is ? t('ui.option.on') : t('ui.option.off'),
          lines: [] })
        continue
      }
      if (typeof was !== 'number' || typeof is !== 'number') continue
      const delta = is - was
      // **값과 오르내림을 갈라 둡니다.** 값은 크게 흰 글로, 오르내림은 작게 파랑으로 —
      // 한 글로 붙이면 한 크기 한 색이 됩니다.
      out.push({
        label: this.ruleName(key),
        value: ruleValue(key, is),
        delta: `${delta > 0 ? '+' : ''}${ruleValue(key, delta)}`,
        lines: [],
      })
    }

    return out
  }

  /**
   * 왼쪽 패널의 「적용 중」.
   *
   * **자리가 좁습니다.** 다 넣으려 하면 글씨가 작아져 아무것도 안 읽히므로, 넷까지만 세우고
   * 나머지는 개수로 적습니다 — 누르면 판이 펼쳐집니다.
   */
  syncActive(): void {
    this.game.tray.activeLayer.removeChildren().forEach(child => child.destroy())
    const entries = this.activeEntries()
    if (entries.length === 0) return
    // **방금 들어온 줄은 맨 앞입니다.** 넷까지만 보이므로 뒤에 있으면 밝아진 것을 볼 수 없습니다.
    const glow = this.activeGlow && this.game.clock < this.activeGlow.until ? this.activeGlow
      : undefined
    if (glow) {
      const at = entries.findIndex(entry => entry.label === glow.label)
      if (at > 0) entries.unshift(...entries.splice(at, 1))
    }

    // **자기 무리입니다.** 552는 금액·안테 칸의 밑변에서 12픽셀이라 그 칸에 딸린 설명으로
    // 보였습니다 — 이것은 그 칸과 상관없는 다른 목록이고, 무리 사이는 26입니다.
    const top = PANEL_ROWS.active
    // **줄은 판때기 없이 놓입니다.** 칸마다 상자를 두면 위의 2×2 칸과 같은 것으로 읽히는데,
    // 이것은 값의 칸이 아니라 목록입니다 — 이름은 작게 왼쪽, 값은 크게 오른쪽입니다.
    const rowH = ACTIVE_ROW_H
    const shown = Math.min(entries.length, entries.length > 4 ? 3 : 4)

    // 구획 머리 하나. 판 안의 다른 구획과 같은 것이고, 왼쪽에 붉은 눈금 하나가 붙습니다 —
    // 규칙이 판을 바꾸는 것이라는 표시입니다.
    const head = sectionHead(IN_W, tf('ui.active.count', { n: entries.length }), undefined,
      false)
    head.position.set(IN_X, top - 6)
    const tick = new Graphics()
    tick.rect(-8, SECTION_H / 2 - 8, 2, 14).fill(UI.bad)
    head.addChild(tick)
    this.game.tray.activeLayer.addChild(head)

    entries.slice(0, shown).forEach((entry, index) => {
      const y = top + 22 + index * rowH
      const middle = (rowH - 4) / 2
      const line = new Container()
      line.position.set(IN_X, y)

      // **방금 들어온 줄은 값의 색으로 밝습니다.** 바우처가 규칙으로 들어갔다는 것이 이
      // 줄이 밝아지는 것으로 남습니다 — 산 자리에서 이름이 한 번 뜨는 것만으로는 어디로 간
      // 것인지가 없었습니다.
      if (glow && entry.label === glow.label) {
        const lit = new Graphics()
        lit.rect(-8, 0, IN_W + 16, rowH - 4).fill({ color: UI.money, alpha: 0.18 })
        line.addChild(lit)
        glow.plate = lit
      }

      const name = new Text({
        text: entry.label,
        style: { fontSize: TEXT.small, fill: UI.ink, fontWeight: WEIGHT.normal },
      })
      name.anchor.set(0, 0.5)
      name.position.set(0, middle)
      line.addChild(name)

      // 오른쪽 끝에 오르내림이 작게 파랑으로, 그 왼쪽에 값이 크게 흰 글로 놓입니다. 값이
      // 수가 아닌 줄(태그 · 바우처)은 값도 작고 흐립니다.
      let right = IN_W
      if (entry.delta !== undefined) {
        const more = new Text({
          text: entry.delta,
          style: { fontSize: TEXT.small, fill: UI.chips, fontWeight: WEIGHT.bold },
        })
        more.anchor.set(1, 0.5)
        more.position.set(right, middle)
        line.addChild(more)
        right -= more.width + 8
      }
      const value = new Text({
        text: entry.value,
        style: entry.delta !== undefined
          ? { fontSize: TEXT.base, fill: UI.ink, fontWeight: WEIGHT.bold, fontFamily: NUMERALS }
          : { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      value.anchor.set(1, 0.5)
      value.position.set(right, middle)
      line.addChild(value)

      line.eventMode = 'static'
      line.cursor = 'pointer'
      line.hitArea = new Rectangle(-8, 0, IN_W + 16, rowH - 4)
      this.game.input.tipOn(line, at => {
        this.game.input.tooltip.show(entry.label,
          entry.delta === undefined ? entry.value : `${entry.value}  ${entry.delta}`, 0,
          entry.lines, at, SIZE)
      })
      line.on('pointertap', () => {
        if (this.game.input.ate()) return
        this.game.cards.toggleActive()
      })
      this.game.tray.activeLayer.addChild(line)
    })

    if (entries.length > shown) {
      const more = new Text({
        text: tf('ui.active.more', { n: entries.length - shown }),
        style: { fontSize: TEXT.mini, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      more.position.set(IN_X, top + 22 + shown * rowH + 4)
      more.eventMode = 'static'
      more.cursor = 'pointer'
      more.on('pointertap', () => {
        if (this.game.input.ate()) return
        this.game.cards.toggleActive()
      })
      this.game.tray.activeLayer.addChild(more)
    }
  }

  drawActivePanel(): void {
    const layer = this.activePanel.view
    layer.removeChildren().forEach(child => child.destroy())

    const entries = this.activeEntries()
    const width = 460
    const top = TITLE_BAR + 18

    // **줄 높이가 저마다 다릅니다.** 설명이 접히면 그만큼 아래가 밀리므로, 자리를 먼저 잡고
    // 그 합으로 판의 높이를 정합니다.
    const rows = entries.map(entry => {
      const line = new Container()
      const name = new Text({
        text: entry.label,
        style: { fontSize: TEXT.copy, fill: UI.ink, fontWeight: WEIGHT.bold },
      })
      name.position.set(20, 6)
      line.addChild(name)

      const value = richLine(entry.value,
                             richStyle('body', { fill: UI.inkDim,
                                                 fontWeight: WEIGHT.normal }))
      value.position.set(width - 20 - value.width, 7)
      line.addChild(value)

      let height = 30
      // 무엇을 하는 것인지는 그 줄 아래에. **이름만으로는 왜 걸렸는지 모릅니다.**
      if (entry.lines.length > 0) {
        // **값 칸을 피해 접습니다.** 오른쪽 끝에 값이 놓여 있으므로 거기까지 가면 겹칩니다.
        const note = richLine(entry.lines[0], richStyle('note'),
                              width - 130, richLeading('note'))
        note.position.set(20, 22)
        line.addChild(note)
        height = 26 + note.height
      }
      return { line, height }
    })

    const body = rows.reduce((sum, row) => sum + row.height, 0)
    const height = top + Math.max(30, body) + 14 + FOOTER_BAR
    ;(this.activePanel.size as { width: number; height: number }).height = height

    layer.addChild(panelFrame(width, height, t('ui.active.head'),
      () => this.game.cards.toggleActive()))

    if (entries.length === 0) {
      const empty = new Text({
        text: t('ui.active.empty'),
        style: { fontSize: TEXT.body, fill: UI.inkDim },
      })
      empty.anchor.set(0.5, 0)
      empty.position.set(width / 2, top + 6)
      layer.addChild(empty)
      return
    }

    let y = top
    for (const row of rows) {
      row.line.position.set(0, y)
      layer.addChild(row.line)
      y += row.height
    }
  }

  /**
   * 나머지를 모아 둔 판.
   *
   * **줄 하나에 하나씩입니다.** 자주 쓰지 않는 것들이므로 찾기 쉬운 것이 빠른 것보다
   * 낫습니다.
   */
  openMenu(): void {
    const layer = this.menu.view
    layer.removeChildren().forEach(child => child.destroy())

    const width = 260
    // **옵션이 위입니다.** 판이 도는 동안 여는 것은 대개 소리나 속도를 고치려는 것이고,
    // 게임 방법은 첫 판에 한 번 보는 것입니다.
    const rows: { key: string; label: string; press: () => void }[] = [
      { key: 'options', label: t('ui.button.options'),
        press: () => this.game.session.openOptions() },
      { key: 'guide', label: t('ui.button.guide'), press: () => this.modals.open(this.guide) },
      // **도감은 판 안에서도 엽니다.** 상점에서 처음 본 조커가 무엇인지는 그 자리에서
      // 궁금해지고, 그때 타이틀로 돌아가야 한다면 그것은 판을 접는 일이 됩니다.
      { key: 'collection', label: t('ui.button.collection'),
        press: () => this.modals.open(this.collection) },
      // **타이틀로와 나가기가 맨 아래입니다.** 판을 접는 것이므로 옵션과 게임 방법과 같은
      // 무게로 가운데에 두면 잘못 누르는 일이 생깁니다.
      { key: 'toTitle', label: t('ui.button.toTitle'),
        press: () => this.game.session.askLeaveRun() },
      { key: 'quit', label: t('ui.button.quit'), press: () => this.game.session.askQuit() },
    ]
    // **밑단이 없습니다.** 머리의 `✕` 와 바깥 누르기와 `Esc` 로 닫히므로, 닫기를 또 두면
    // 같은 일을 하는 것이 판 하나에 둘입니다.
    const height = TITLE_BAR + MENU_PAD + rows.length * 56 + 8
    ;(this.menu.size as { width: number; height: number }).height = height

    layer.addChild(panelFrame(width, height, t('ui.button.menu'),
      () => this.modals.close(this.menu), undefined, false))

    rows.forEach((row, index) => {
      const button = new Button(row.label, width - 48, 48, 'neutral', () => {
        // **닫고 나서 엽니다.** 이 판 위에 또 판이 서면 뒤로 물러난 것이 보이고, 그것은
        // 메뉴가 아니라 판이 쌓인 것으로 보입니다.
        this.modals.close(this.menu)
        row.press()
      })
      button.position.set(24, TITLE_BAR + MENU_PAD + index * 56)
      layer.addChild(button)
      // **자리는 화면이 알립니다.** 판이 닫히면 이 단추는 지워지고, 지워진 것의 자리는
      // 알리지 않습니다 — `lateSpots` 가 그것을 맡습니다.
      this.game.spotNodes.set(`menu:${row.key}`,
                         { node: button, cx: (width - 48) / 2, cy: 24 })
    })

    this.modals.open(this.menu)
  }

  /**
   * 도감의 탭 아홉이 지금 어디에 있는가.
   *
   * **판이 떠 있을 때만 값이 있습니다.** 탭의 폭은 갈래의 수가 정하므로, 도구가 그 셈을
   * 베껴 적으면 갈래 하나가 늘거나 주는 날부터 빈자리를 누르고 통과합니다.
   */
  collectionSpots(): Record<string, { x: number; y: number }> {
    if (!this.modals.has(this.collection)) return {}
    const out: Record<string, { x: number; y: number }> = {}
    for (const [key, one] of this.collection.toolSpots) {
      if (one.node.destroyed) continue
      out[`collection:${key}`] = this.game.probe.spotOf(one.node, one.cx, one.cy)
    }
    return out
  }

  /** 물어보는 판의 단추 둘. **떠 있을 때만 값이 있습니다.** */
  confirmSpots(): Record<string, { x: number; y: number }> {
    const panel = this.confirmUp
    if (!panel || !this.modals.has(panel)) return {}
    const out: Record<string, { x: number; y: number }> = {}
    for (const [key, one] of panel.toolSpots) {
      if (one.node.destroyed) continue
      out[`confirm:${key}`] = this.game.probe.spotOf(one.node, one.cx, one.cy)
    }
    return out
  }
}
