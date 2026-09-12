import { COLOR, DEAD, ENHANCEMENT_PAPER, SEAL_INK } from '../render/ink'
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
import { SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { Button } from '../ui/widgets'
import { Guide } from '../ui/guide'
import { CollectionPanel } from '../ui/collection'
import { RunPanel } from '../ui/run-panel'
import { ConfirmPanel } from '../ui/confirm'
import { FOOTER_BAR, type ModalPanel, Modals, panelFrame, TITLE_BAR } from '../ui/modal'
import { SECTION_H, sectionHead } from '../ui/parts'
import { ScrollView } from '../ui/scroll'
import { richBlock, richLeading, richLine, richStyle } from '../ui/rich'
import { OptionsPanel } from '../ui/options'
import { LEFT, MENU_PAD, PANEL_ROWS, PANEL_W } from './metrics'
import { blindName, HAND_SHAPE, INSIGHT_COLOR, ruleValue, snake } from './tables'
import { type RunInfoTab } from './types'
import { type Game } from './game'
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
    size: { width: 540, height: 60 },
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
   * 족보 목록.
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

    const rows = this.game.data.tables.pokerHand.records
    const rowH = 36
    // 갈래 단추가 머리띠 아래 한 줄을 차지합니다.
    const top = TITLE_BAR + 54
    // **네 갈래가 같은 크기입니다.** 갈래마다 다르면 단추를 누를 때마다 판과 단추가 함께
    // 자리를 옮기고, 그러면 다음 갈래를 누르려는 손이 빈자리를 누릅니다. 폭은 상수이고
    // 높이는 족보 목록이 정합니다 — 넷 가운데 가장 긴 것이 그것입니다.
    const width = 620
    const body = rows.length * rowH
    const height = top + body + 14 + FOOTER_BAR

    layer.addChild(panelFrame(width, height, t('ui.run_info.title'),
      () => this.game.cards.toggleHandList()))

    // 네 갈래. **한 판을 도는 동안 궁금해지는 것이 넷입니다** — 어느 족보가 몇 점인지,
    // 이 안테의 블라인드가 무엇인지, 지금 난이도가 무엇을 바꾸는지, 그리고 지금 이 판에서
    // 다음 한 수를 무엇으로 두어야 하는지. 판을 넷 만들면 그 넷을 여는 방법이 저마다
    // 달라지므로 한 판 안의 갈래로 둡니다.
    const tabs: { key: RunInfoTab; label: string }[] = [
      { key: 'hands', label: t('ui.kind.poker_hand') },
      { key: 'blinds', label: t('ui.tab.blinds') },
      { key: 'stakes', label: t('ui.tab.stakes') },
      { key: 'insight', label: t('ui.tab.insight') },
    ]
    const tabW = 140
    const tabGap = 8
    const tabsX = (width - (tabs.length * tabW + (tabs.length - 1) * tabGap)) / 2
    tabs.forEach((tab, index) => {
      const here = this.runInfoTab === tab.key
      const button = new Button(tab.label, tabW, 36, here ? 'select' : 'neutral', () => {
        if (this.runInfoTab !== tab.key) this.insightScroll?.toTop()
        this.runInfoTab = tab.key
        this.drawHandList()
      })
      button.position.set(tabsX + index * (tabW + tabGap), TITLE_BAR + 12)
      layer.addChild(button)
      // **자리는 화면이 알립니다.** 판이 닫히면 이 단추가 지워지고 `lateSpots` 가 그것을
      // 봅니다 — 도구가 좌표를 적어 두면 폭이 바뀔 때 빈자리를 누르고 통과합니다.
      this.game.spotNodes.set(`runInfoTab:${tab.key}`,
                         { node: button, cx: tabW / 2, cy: 15 })
    })

    if (this.runInfoTab === 'insight') {
      this.drawInsightRows(layer, width, top, body)
      this.handList.size.width = width
      this.handList.size.height = height
      layer.eventMode = 'static'
      return
    }

    if (this.runInfoTab !== 'hands') {
      this.drawRunInfoRows(layer, width, top)
      this.handList.size.width = width
      this.handList.size.height = height
      layer.eventMode = 'static'
      return
    }

    const band = new Graphics()
    layer.addChild(band)

    rows.forEach((row, index) => {
      const key = PokerHandKind[row.hand]
      const level = this.game.state.handLevels[key] ?? 1
      const chips = row.baseChips + row.chipsPerLevel * (level - 1)
      const mult = row.baseMult + row.multPerLevel * (level - 1)
      const seen = row.visibleFromStart || (this.game.state.handPlayCounts[key] ?? 0) > 0
      const y = top + index * rowH

      const name = new Text({
        text: seen ? this.handName(row.hand) : '???',
        style: { fontSize: TEXT.base, fill: seen ? UI.ink : UI.inkDim,
          fontWeight: WEIGHT.normal },
      })
      name.position.set(28, y + 2)

      const lv = new Text({
        text: tf('ui.hand.level_short', { level }),
        style: { fontSize: TEXT.body, fill: level > 1 ? UI.good : UI.inkDim,
          fontWeight: WEIGHT.normal },
      })
      lv.position.set(246, y + 3)

      const value = new Text({
        text: seen ? `${chips}  ×  ${mult}` : '—',
        style: { fontSize: TEXT.base, fill: seen ? UI.chips : UI.inkDim,
          fontWeight: WEIGHT.normal },
      })
      value.position.set(318, y + 2)

      const played = new Text({
        text: tf('ui.hand.times', { n: this.game.state.handPlayCounts[key] ?? 0 }),
        style: { fontSize: TEXT.small, fill: UI.inkDim },
      })
      played.anchor.set(1, 0)
      played.position.set(width - 28, y + 4)

      layer.addChild(name, lv, value, played)
      this.handRows.push({ hand: row.hand, seen, y, height: rowH })
    })

    // **가리킨 줄의 그림은 맨 위입니다.** 줄보다 먼저 붙이면 글자가 그림 위에 겹칩니다.
    const preview = new Container()
    preview.visible = false
    layer.addChild(preview)
    this.handBand = band
    this.handPreview = preview
    this.handHovered = -1

    // 자리는 모달 더미가 정합니다. 이쪽은 넓이만 알립니다.
    this.handList.size.width = width
    this.handList.size.height = height
    layer.eventMode = 'static'
  }

  /**
   * 블라인드와 스테이크의 줄들.
   *
   * **둘이 같은 모양입니다** — 이름과 값 몇 개가 한 줄이고, 지금 것에 표가 붙습니다.
   * 갈래마다 판을 따로 만들면 그 셋이 서로 다르게 생기고, 그러면 한 판 안의 갈래가
   * 아니라 판 셋이 됩니다.
   */
  private drawRunInfoRows(layer: Container, width: number, top: number): void {
    const rowH = 36
    const rows: { name: string; note: string; value: string; here: boolean }[] = []

    if (this.runInfoTab === 'blinds') {
      // 이 안테의 세 라운드. **요구 점수는 안테가 정하므로 판마다 다릅니다.**
      for (const blind of [BlindKind.Small, BlindKind.Big, BlindKind.Boss]) {
        const bossRow = blind === BlindKind.Boss
          ? this.game.data.tables.bossBlind.findByBossId(this.game.state.bossId) : undefined
        rows.push({
          name: bossRow
            ? nameOf(this.game.data, 'boss', this.game.state.bossId, bossRow.name)
            : blindName(blind),
          note: bossRow
            ? describe(this.game.data, this.game.data.bossEffects.get(this.game.state.bossId)
                ?? []).join(' · ')
            : t('ui.note.no_rules'),
          value: `${targetOf(this.game.data, this.game.state, blind).toLocaleString('en-US')}`
            + `   ${tf('ui.blind.reward', { n: rewardOf(this.game.data, this.game.state, blind) })}`,
          here: this.game.state.blind === blind,
        })
      }
    } else {
      // 난이도. **누적입니다** — 뒤의 것은 앞의 것을 전부 포함합니다.
      const here = stakeRow(this.game.data, this.game.state.stake)?.stake
      for (const row of this.game.data.tables.stake.records) {
        rows.push({
          name: nameOf(this.game.data, 'stake', stakeSlug(row.stake), row.name),
          note: tf('ui.stake.note', {
            column: row.anteColumn, reward: row.smallBlindReward, discards: row.discardsDelta,
          }),
          value: '',
          here: row.stake === here,
        })
      }
    }

    rows.forEach((row, index) => {
      const y = top + index * rowH

      if (row.here) {
        const band = new Graphics()
        band.rect(16, y - 4, width - 32, rowH - 4)
          .fill({ color: UI.pick, alpha: 0.22 })
        layer.addChild(band)
      }

      const name = new Text({
        text: row.name,
        style: { fontSize: TEXT.base, fill: row.here ? UI.ink : UI.inkDim,
          fontWeight: WEIGHT.bold },
      })
      name.position.set(28, y)

      const note = new Text({
        text: row.note,
        style: {
          fontSize: TEXT.mini, fill: UI.inkDim,
          wordWrap: true, wordWrapWidth: width - 220, breakWords: true, lineHeight: 13,
        },
      })
      note.position.set(28, y + 18)

      const value = new Text({
        text: row.value,
        style: { fontSize: TEXT.copy, fill: UI.chips, fontWeight: WEIGHT.normal },
      })
      value.anchor.set(1, 0)
      value.position.set(width - 28, y + 2)

      layer.addChild(name, note, value)
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
   * 인사이트 갈래의 줄들.
   *
   * **갈래 머리로 묶어 세웁니다.** 줄만 늘어놓으면 「이 줄이 무엇에 대한 것인가」를 문장에서
   * 읽어야 하고, 문장에는 그것이 적혀 있지 않습니다.
   *
   * 줄이 길어 접히므로 **높이를 미리 세지 않습니다** — 줄마다 그린 뒤에 그 높이만큼
   * 내립니다. 말을 바꾸면 접히는 자리가 달라지고, 미리 센 높이는 한국어에만 맞습니다.
   */
  private drawInsightRows(layer: Container, width: number, top: number,
                          height: number): void {
    const rows = this.insightRows()
    const inner = width - 36
    const scroll = this.insightScroll ?? new ScrollView(inner, height)
    this.insightScroll = scroll
    scroll.position.set(18, top)
    layer.addChild(scroll)

    // **답이 같으면 다시 그리지 않습니다.** 판이 떠 있는 동안 `refresh` 마다 여기를 지나고,
    // 줄마다 판과 접힌 글을 만드는 것이 그 비용입니다.
    const key = this.insightCache?.key ?? ''
    if (this.insightDrawn === key) return
    this.insightDrawn = key
    scroll.content.removeChildren().forEach(child => child.destroy())

    if (rows.length === 0) {
      const none = new Text({
        text: t('ui.insight.none'),
        style: { fontSize: TEXT.copy, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      none.anchor.set(0.5, 0)
      none.position.set(inner / 2, 26)
      scroll.content.addChild(none)
      scroll.refresh()
      return
    }

    let y = 0
    let group = ''
    for (const row of rows) {
      if (row.group !== group) {
        // 무리 사이는 10 입니다. 붙여 두면 앞 무리의 마지막 줄이 다음 머리에 닿습니다.
        if (group !== '') y += 10
        group = row.group
        const head = sectionHead(inner, t(`ui.insight.group.${row.group}`), undefined, false)
        head.position.set(0, y)
        scroll.content.addChild(head)
        y += SECTION_H + 2
      }
      y += this.drawInsightLine(scroll.content, inner, y, row) + 6
    }

    scroll.refresh()
  }

  /**
   * 줄 하나. 그린 높이를 돌려줍니다.
   *
   * **등급은 왼쪽 끝의 띠입니다.** 글의 색으로 알리면 읽는 색이 셋이 되고, 그러면 강조한
   * 숫자와 구분되지 않습니다.
   */
  private drawInsightLine(into: Container, width: number, y: number, row: Insight): number {
    const step = richLeading('body')
    const text = richBlock([tf(`ui.insight.${row.key}`, row.values)],
                           richStyle('body'), step, width - 46)
    const wrapped = (text as Container & { rows?: number }).rows ?? 1
    const rowH = Math.max(30, wrapped * step + 13)

    const node = new Container()
    node.position.set(0, y)

    const plate = new Graphics()
    plate.rect(0, 0, width, rowH).fill(UI.cell)
    plate.rect(0.5, 0.5, width - 1, rowH - 1)
      .stroke({ color: UI.hairline, width: 1 })
    plate.rect(0, 5, 4, rowH - 10).fill(INSIGHT_COLOR[row.level])
    node.addChild(plate)

    text.position.set(16, (rowH - wrapped * 17) / 2 + 1)
    node.addChild(text)

    // 쪽지를 가진 줄에만 표시가 붙습니다. **표시가 없으면 눌러 볼 것이 있는지 알 수 없습니다.**
    if (row.lines.length > 0) {
      const mark = new Text({
        text: '···',
        style: { fontSize: TEXT.copy, fill: UI.inkDim, fontWeight: WEIGHT.bold },
      })
      mark.anchor.set(1, 0.5)
      mark.position.set(width - 12, rowH / 2)
      node.addChild(mark)

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
  private activeEntries(): { label: string; value: string; lines: string[] }[] {
    const out: { label: string; value: string; lines: string[] }[] = []

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
      out.push({
        label: this.ruleName(key),
        value: `${ruleValue(key, is)}   (${delta > 0 ? '+' : ''}${ruleValue(key, delta)})`,
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
    const rowH = 26
    const shown = Math.min(entries.length, entries.length > 4 ? 3 : 4)

    // 구획 머리 하나. 판 안의 다른 구획과 같은 것입니다.
    const head = sectionHead(PANEL_W, tf('ui.active.count', { n: entries.length }), undefined,
      false)
    head.position.set(LEFT, top - 6)
    this.game.tray.activeLayer.addChild(head)

    entries.slice(0, shown).forEach((entry, index) => {
      // 머리글과 첫 줄 사이는 22 입니다. 20 은 머리글의 밑변에서 6픽셀이라 그 글이 첫
      // 줄의 딱지에 닿아 있었습니다.
      const y = top + 22 + index * rowH
      const line = new Container()
      line.position.set(LEFT, y)

      const plate = new Graphics()
      plate.rect(0, 0, PANEL_W, rowH - 4).fill(UI.cell)
      plate.rect(0.5, 0.5, PANEL_W - 1, rowH - 5)
        .stroke({ color: UI.hairline, width: 1 })
      line.addChild(plate)
      // **방금 들어온 줄은 값의 색 테로 밝습니다.** 바우처가 규칙으로 들어갔다는 것이 이
      // 줄이 밝아지는 것으로 남습니다 — 산 자리에서 이름이 한 번 뜨는 것만으로는 어디로 간
      // 것인지가 없었습니다.
      if (glow && entry.label === glow.label) {
        const lit = new Graphics()
        lit.rect(0, 0, PANEL_W, rowH - 4).fill({ color: UI.money, alpha: 0.18 })
        lit.rect(0.5, 0.5, PANEL_W - 1, rowH - 5)
          .stroke({ color: UI.money, width: 1.5 })
        line.addChild(lit)
        glow.plate = lit
      }

      const name = new Text({
        text: entry.label,
        style: { fontSize: TEXT.small, fill: UI.ink, fontWeight: WEIGHT.normal },
      })
      name.position.set(8, 4)
      line.addChild(name)

      const value = richLine(entry.value,
                             richStyle('note', { fontWeight: WEIGHT.normal }))
      value.position.set(PANEL_W - 8 - value.width, 4)
      line.addChild(value)

      line.eventMode = 'static'
      line.cursor = 'pointer'
      line.hitArea = new Rectangle(0, 0, PANEL_W, rowH - 4)
      this.game.input.tipOn(line, at => {
        this.game.input.tooltip.show(entry.label, entry.value, 0, entry.lines, at, SIZE)
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
      more.position.set(LEFT + 4, top + 22 + shown * rowH + 4)
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
