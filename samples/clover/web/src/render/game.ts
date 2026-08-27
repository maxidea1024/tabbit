// 화면.
//
// **코어를 부르고 이벤트를 받아 그립니다.** 규칙은 여기 없습니다 — 여기 있는 것은 어디에
// 놓을지와 얼마나 세게 보일지뿐이고, 뒤쪽의 수치는 `Const_Feel` 이므로 데이터입니다.

import { Container, Graphics, Text, type Application } from 'pixi.js'

import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import type { Data } from '../core/data'
import { apply, newRun, type Action } from '../core/run'
import { rerollCost } from '../core/shop'
import type { CardInstance, GameEvent, RunState } from '../core/state'
import { Audio } from './audio'
import { CardView, type EditionLook } from './card-view'
import { JokerView } from './joker-view'
import {
  buildTimeline, readFeel, scaleOf, semitonesOf, shakeOf, TimelinePlayer,
  type Beat, type Feel,
} from './juice'
import { COLOR, SIZE } from './theme'
import { Button, Counter, Panel } from '../ui/widgets'

export class Game {
  private readonly world = new Container()
  private readonly boardLayer = new Container()
  private readonly popupLayer = new Container()

  private readonly state: RunState
  private readonly feel: Feel
  private readonly audio: Audio
  private readonly player: TimelinePlayer

  private readonly cards = new Map<number, CardView>()
  /** 낸 카드. **연출이 끝날 때까지 화면에 남습니다** — 득점이 어디서 일어나는지 보여야 합니다. */
  private readonly playedViews: CardView[] = []
  private readonly jokers = new Map<number, JokerView>()
  private readonly selected = new Set<number>()

  private readonly chips = new Counter('칩', COLOR.chips, 150)
  private readonly mult = new Counter('배수', COLOR.mult, 150)
  private readonly score = new Counter('점수', COLOR.ink, 220)
  private readonly money = new Counter('$', COLOR.money, 110)

  private readonly headline = new Text({
    text: '', style: { fontSize: 22, fill: COLOR.ink, fontWeight: '700' },
  })
  private readonly subline = new Text({
    text: '', style: { fontSize: 13, fill: COLOR.inkDim },
  })

  private readonly playButton: Button
  private readonly discardButton: Button
  private readonly primaryButton: Button
  private readonly rerollButton: Button

  /** 요구 점수 게이지. **넘긴 만큼 더 채워집니다** — 얼마나 넘겼는지가 보여야 합니다. */
  private readonly gauge = new Graphics()
  /** 블라인드를 고르는 자리. */
  private readonly blindPanel = new Container()

  private shake = 0
  private liveChips = 0
  private liveMult = 10_000

  constructor(private readonly app: Application, private readonly data: Data, seed: string) {
    this.feel = readFeel(data.feel)
    this.audio = new Audio(data.tables)
    this.state = newRun(data, seed, 'red_deck', 'White').state
    this.player = new TimelinePlayer(beat => this.showBeat(beat))

    app.stage.addChild(this.world)
    this.world.addChild(this.boardLayer, this.popupLayer)

    this.buildHud()
    this.playButton = new Button('낸다', 120, 40, 0x2c6b46, () => this.play())
    this.discardButton = new Button('버린다', 120, 40, 0x6b3a2c, () => this.discard())
    this.primaryButton = new Button('시작', 180, 44, 0x2c6b46, () => this.primary())
    this.rerollButton = new Button('리롤', 110, 40, 0x3a4c6b, () => this.act({ t: 'reroll' }))

    this.world.addChild(this.playButton, this.discardButton, this.primaryButton, this.rerollButton)
    this.playButton.position.set(760, 660)
    this.discardButton.position.set(900, 660)
    this.primaryButton.position.set(SIZE.width / 2 - 90, 620)
    this.rerollButton.position.set(SIZE.width / 2 - 250, 626)

    app.canvas.addEventListener('pointerdown', () => this.audio.unlock())
    window.addEventListener('keydown', () => {
      this.audio.unlock()
      if (this.player.busy) this.player.hurry(this.feel)
    })

    this.refresh()
    app.ticker.add(ticker => this.tick(ticker.deltaMS))
  }

  // ---------------------------------------------------------------- 화면 뼈대

  private buildHud(): void {
    const left = new Panel(300, 150)
    left.position.set(24, 24)
    this.world.addChild(left)

    this.chips.position.set(40, 40)
    this.mult.position.set(190, 40)
    this.score.position.set(40, 100)
    this.money.position.set(24, 190)
    this.world.addChild(this.chips, this.mult, this.score, this.money)

    this.headline.position.set(SIZE.width / 2 - 200, 30)
    this.subline.position.set(SIZE.width / 2 - 200, 62)
    this.world.addChild(this.headline, this.subline, this.gauge, this.blindPanel)
  }

  /** 요구 점수까지 얼마나 왔는가. 넘기면 넘긴 만큼 더 칠합니다. */
  private drawGauge(): void {
    const x = SIZE.width / 2 - 200
    const y = 88
    const width = 400
    const height = 10

    this.gauge.clear()
    this.gauge.visible = this.state.phase === 'round'
    if (!this.gauge.visible) return

    const ratio = this.state.target > 0 ? this.state.score / this.state.target : 0
    this.gauge.roundRect(x, y, width, height, 5).fill(0x1d2b22)
    this.gauge.roundRect(x, y, width * Math.min(1, ratio), height, 5)
      .fill(ratio >= 1 ? COLOR.good : COLOR.chips)

    if (ratio > 1) {
      const over = Math.min(1, ratio - 1)
      this.gauge.roundRect(x, y - 4, width * over, 4, 2).fill(COLOR.mult)
    }
  }

  /** 블라인드를 고르는 화면. 스킵하면 태그를 받습니다. */
  private drawBlindPanel(): void {
    this.blindPanel.removeChildren().forEach(child => child.destroy())
    this.blindPanel.visible = this.state.phase === 'blind-select'
    if (!this.blindPanel.visible) return

    const state = this.state
    const boss = state.blind === BlindKind.Boss
    const row = this.data.tables.blind.findByBlind(state.blind)

    const card = new Panel(300, 200, boss ? 0x3a1c22 : COLOR.panel)
    card.position.set(SIZE.width / 2 - 150, 220)

    const name = new Text({
      text: boss
        ? this.data.tables.bossBlind.findByBossId(state.bossId)?.name ?? '보스'
        : blindName(state.blind) + ' 블라인드',
      style: { fontSize: 20, fill: COLOR.ink, fontWeight: '700' },
    })
    name.anchor.set(0.5, 0)
    name.position.set(150, 18)

    const need = new Text({
      text: `요구 ${state.target}`,
      style: { fontSize: 26, fill: COLOR.chips, fontWeight: '700' },
    })
    need.anchor.set(0.5, 0)
    need.position.set(150, 62)

    const reward = new Text({
      text: `보상 $${row?.reward ?? 0}`,
      style: { fontSize: 15, fill: COLOR.money },
    })
    reward.anchor.set(0.5, 0)
    reward.position.set(150, 104)

    card.addChild(name, need, reward)
    this.blindPanel.addChild(card)

    if (!boss) {
      const skip = new Button('건너뛴다', 140, 34, 0x3a3a4c, () => this.act({ t: 'skip_blind' }))
      skip.position.set(SIZE.width / 2 - 70, 440)
      this.blindPanel.addChild(skip)
    }
  }

  /**
   * 화면 밖에서 지금 무슨 국면인지 볼 수 있게 둡니다.
   *
   * **그림을 굽는 도구가 이것을 읽습니다** — 캔버스 하나뿐인 화면은 밖에서 상태를 알
   * 방법이 없습니다. 매 프레임 갱신하는 것은 「연출이 도는 중인가」가 프레임마다 바뀌기
   * 때문입니다.
   */
  private publishPeek(): void {
    const state = this.state
    ;(window as unknown as { __clover?: unknown }).__clover = {
      phase: state.phase, ante: state.ante, blind: state.blind,
      money: state.money, score: state.score, target: state.target,
      jokers: state.jokers.length, discards: state.discardsLeft,
      busy: this.player.busy || !this.score.settled,
      // 고르는 판단은 도구가 하고 화면은 값만 내어 둡니다.
      hand: state.hand.map(uid => {
        const card = state.deck.find(entry => entry.uid === uid)
        return { rank: card?.rank ?? 0, suit: card?.suit ?? 0 }
      }),
    }
  }

  /** 화면 크기에 맞춰 통째로 키웁니다. **기준 해상도 하나로 그립니다.** */
  layout(width: number, height: number): void {
    const scale = Math.min(width / SIZE.width, height / SIZE.height)
    this.world.scale.set(scale)
    this.world.position.set(
      (width - SIZE.width * scale) / 2,
      (height - SIZE.height * scale) / 2)
  }

  // ---------------------------------------------------------------- 액션

  private act(action: Action): void {
    if (this.player.busy) return

    const step = apply(this.data, this.state, action)
    this.refresh()
    this.startTimeline(step.events)
  }

  private primary(): void {
    switch (this.state.phase) {
      case 'blind-select': this.act({ t: 'select_blind' }); break
      case 'shop': this.act({ t: 'leave_shop' }); break
      default: break
    }
  }

  private play(): void {
    if (this.selected.size === 0 || this.player.busy) return
    const cards = [...this.selected]
    this.selected.clear()

    // 손에서 떼어 낸 자리로 옮깁니다. 코어를 부르기 **전**이어야 그 뷰가 살아 있습니다.
    this.liftToPlayArea(cards)
    this.act({ t: 'play', cards })
  }

  /** 낸 카드를 가운데 줄로 옮깁니다. `syncCards` 가 이것들을 지우지 않습니다. */
  private liftToPlayArea(uids: number[]): void {
    const spacing = SIZE.cardWidth + 14
    const startX = SIZE.width / 2 - ((uids.length - 1) * spacing) / 2

    uids.forEach((uid, index) => {
      const view = this.cards.get(uid)
      if (!view) return
      this.cards.delete(uid)
      this.playedViews.push(view)
      view.eventMode = 'none'
      view.position.set(startX + index * spacing, SIZE.playY)
      view.rotation = 0
      view.zIndex = 100 + index
    })
  }

  /** 연출이 끝나면 낸 카드를 치웁니다. */
  private clearPlayArea(): void {
    for (const view of this.playedViews) view.destroy()
    this.playedViews.length = 0
  }

  private discard(): void {
    if (this.selected.size === 0) return
    const cards = [...this.selected]
    this.selected.clear()
    this.act({ t: 'discard', cards })
  }

  private toggle(uid: number): void {
    if (this.player.busy) return
    if (this.selected.has(uid)) this.selected.delete(uid)
    else if (this.selected.size < this.data.run.maxPlayedCards) this.selected.add(uid)
    this.audio.play('card_select')
    this.refresh()
  }

  // ---------------------------------------------------------------- 연출

  private startTimeline(events: GameEvent[]): void {
    const beats = buildTimeline(events, this.feel)
    if (beats.length === 0) {
      this.settleCounters()
      return
    }

    this.liveChips = 0
    this.liveMult = 10_000
    this.player.play(beats)
  }

  private showBeat(beat: Beat): void {
    const event = beat.event
    const semitones = semitonesOf(beat.intensity, this.feel)

    switch (event.t) {
      case 'HandEvaluated':
        this.liveChips = event.chips
        this.liveMult = event.mult
        this.headline.text = `${this.handName(event.hand)}  레벨 ${event.level}`
        this.audio.play('score_count', semitones)
        break

      case 'CardScored': {
        this.liveChips += event.chips
        const view = this.viewOf(event.uid)
        this.popAt(view, `+${event.chips}`, COLOR.chips, beat.intensity)
        if (view) view.lift = 14
        this.audio.play('card_chip', semitones)
        break
      }

      case 'JokerTriggered': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        const cue = event.op === 'MulMult' ? 'joker_mul'
          : event.op === 'AddMoney' ? 'joker_money' : 'joker_add'
        const text = event.op === 'MulMult'
          ? `×${(event.mult / 10_000).toFixed(2)}`
          : event.chips !== 0 ? `+${event.chips}`
          : event.mult !== 0 ? `+${(event.mult / 10_000).toFixed(0)}`
          : `$${event.money}`
        this.popAt(view, text, event.chips !== 0 ? COLOR.chips : COLOR.mult, beat.intensity)
        this.audio.play(cue, semitones)
        if (view) view.tilt = 0.35
        break
      }

      case 'JokerFizzled': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        this.popAt(view, `${event.num}/${event.den}`, COLOR.inkDim, 0)
        this.audio.play('joker_fizzle')
        break
      }

      case 'Retriggered':
        this.popAt(this.viewOf(event.uid), '다시', COLOR.good, beat.intensity)
        this.audio.play('retrigger', semitones)
        break

      case 'ScoreResolved':
        this.score.target = event.score
        this.audio.play('score_settle', semitones)
        this.shake = shakeOf(beat.intensity, this.feel)
        break

      case 'BlindCleared':
        this.headline.text = '넘겼습니다'
        this.audio.play('blind_clear')
        // 라운드가 끝났으므로 칩과 배수를 되돌립니다. 다음 라운드의 값이 아니라 이번
        // 라운드가 끝났다는 표시입니다.
        this.liveChips = 0
        this.liveMult = 10_000
        break

      case 'RunLost':
        this.headline.text = '여기까지입니다'
        this.audio.play('run_lose')
        break

      case 'RunWon':
        this.headline.text = '끝까지 갔습니다'
        this.audio.play('run_win')
        break

      default:
        break
    }

    this.chips.target = this.liveChips
    this.mult.target = Math.round(this.liveMult / 10_000)
    this.chips.emphasize(scaleOf(beat.intensity, this.feel))
    this.mult.emphasize(scaleOf(beat.intensity, this.feel))
  }

  /** 대상 위로 떠오르는 숫자 하나. */
  private popAt(target: Container | undefined, text: string, tint: number,
                intensity: number): void {
    const label = new Text({
      text,
      style: { fontSize: 18 + intensity * 12, fill: tint, fontWeight: '700' },
    })
    label.anchor.set(0.5, 1)
    label.position.set(
      target ? target.x : SIZE.width / 2,
      (target ? target.y : SIZE.height / 2) - 40)
    this.popupLayer.addChild(label)

    let life = 0
    const rise = () => {
      life += this.app.ticker.deltaMS
      label.y -= this.app.ticker.deltaMS * 0.045
      label.alpha = Math.max(0, 1 - life / 620)
      if (life >= 620) {
        this.app.ticker.remove(rise)
        label.destroy()
      }
    }
    this.app.ticker.add(rise)
  }

  /** 족보의 표시 이름. **식별자가 아니라 번역 대조본의 값입니다.** */
  private handName(hand: PokerHandKind): string {
    const key = `hand.${PokerHandKind[hand]}.name`
    return this.data.tables.stringTable.findByStringId(key)?.ko ?? PokerHandKind[hand]
  }

  /** 손에 있든 낸 자리에 있든 그 카드의 뷰. */
  private viewOf(uid: number): CardView | undefined {
    return this.cards.get(uid) ?? this.playedViews.find(view => view.uid === uid)
  }

  private jokerUidAt(slot: number): number {
    return this.state.jokers[slot]?.uid ?? -1
  }

  private settleCounters(): void {
    this.chips.reset(0)
    this.mult.reset(0)
    this.score.target = this.state.score
  }

  // ---------------------------------------------------------------- 매 프레임

  private tick(deltaMs: number): void {
    const seconds = deltaMs / 1000

    this.player.advance(deltaMs)
    this.publishPeek()
    if (this.state.phase === 'round') this.drawGauge()
    this.chips.advance(deltaMs)
    this.mult.advance(deltaMs)
    this.score.advance(deltaMs)
    this.money.advance(deltaMs)

    for (const view of this.cards.values()) view.advance(seconds)
    for (const view of this.jokers.values()) {
      view.advance(seconds)
      view.tilt *= 0.9
      view.rotation = view.tilt * 0.25
    }

    // 흔들림은 줄어듭니다. 값이 클수록 오래 남습니다.
    if (this.shake > 0.05) {
      this.boardLayer.position.set(
        (Math.random() - 0.5) * this.shake,
        (Math.random() - 0.5) * this.shake)
      this.shake *= 0.86
    } else if (this.boardLayer.x !== 0) {
      this.boardLayer.position.set(0, 0)
      this.shake = 0
    }

    if (!this.player.busy) {
      this.chips.emphasize(1)
      this.mult.emphasize(1)
      if (this.playedViews.length > 0 && this.score.settled) {
        this.clearPlayArea()
        this.refresh()
      }
      if (this.state.phase !== 'round') {
        this.chips.target = 0
        this.mult.target = 0
      }
    }

    // 낸 카드는 떠올랐다가 가라앉습니다.
    for (const view of this.playedViews) {
      view.lift *= 0.88
      view.y = SIZE.playY - view.lift
    }
  }

  // ---------------------------------------------------------------- 다시 그리기

  private editionLook(edition: EditionKind): EditionLook | undefined {
    const row = this.data.tables.editionVisual.findByEdition(edition)
    if (!row || row.shader === 'none') return undefined
    return {
      shader: row.shader as EditionLook['shader'],
      strength: row.strength,
      flowSpeed: row.flowSpeed,
      noise: row.noise,
    }
  }

  /** 상태를 화면에 다시 얹습니다. */
  private refresh(): void {
    const state = this.state

    this.publishPeek()

    this.money.target = state.money
    this.score.target = state.score
    this.headline.text = phaseLabel(state)
    this.subline.text = subLabel(state, this.data)

    this.drawGauge()
    this.drawBlindPanel()
    this.syncCards()
    this.syncJokers()
    this.syncShop()

    const inRound = state.phase === 'round'
    this.playButton.visible = inRound
    this.discardButton.visible = inRound
    this.playButton.enabled = inRound && this.selected.size > 0 && state.handsLeft > 0
    this.discardButton.enabled = inRound && this.selected.size > 0 && state.discardsLeft > 0

    this.primaryButton.visible = state.phase === 'blind-select' || state.phase === 'shop'
    this.primaryButton.text = state.phase === 'shop' ? '다음으로' : '이 블라인드로'
    this.rerollButton.visible = state.phase === 'shop'
    this.rerollButton.text = `리롤 $${rerollCost(this.data, state, state.shop)}`
  }

  private syncCards(): void {
    const wanted = new Set(this.state.hand)

    for (const [uid, view] of this.cards) {
      if (!wanted.has(uid)) {
        view.destroy()
        this.cards.delete(uid)
      }
    }

    const hand = this.state.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    const spacing = Math.min(SIZE.cardWidth + 12, 900 / Math.max(1, hand.length))
    const startX = SIZE.width / 2 - ((hand.length - 1) * spacing) / 2

    hand.forEach((card, index) => {
      let view = this.cards.get(card.uid)
      if (!view) {
        view = new CardView(card, this.editionLook(card.edition))
        view.eventMode = 'static'
        view.cursor = 'pointer'
        view.on('pointertap', () => this.toggle(card.uid))
        view.on('pointerover', () => { view!.lift = this.feel.cardHoverLiftPx })
        view.on('pointerout', () => { view!.lift = 0 })
        this.cards.set(card.uid, view)
        this.boardLayer.addChild(view)
        this.audio.play('card_draw')
      } else {
        view.set(card, this.editionLook(card.edition))
      }

      const chosen = this.selected.has(card.uid)
      view.position.set(startX + index * spacing, SIZE.handY - (chosen ? 26 : 0) - view.lift)
      view.rotation = (index - (hand.length - 1) / 2) * 0.02
      view.zIndex = index
    })
  }

  private syncJokers(): void {
    const wanted = new Set(this.state.jokers.map(joker => joker.uid))

    for (const [uid, view] of this.jokers) {
      if (!wanted.has(uid)) {
        view.destroy()
        this.jokers.delete(uid)
      }
    }

    this.state.jokers.forEach((joker, index) => {
      const row = this.data.tables.joker.findByJokerId(joker.jokerId)
      const look = {
        name: row?.name ?? joker.jokerId,
        rarity: row?.rarity ?? 1,
        edition: this.editionLook(joker.edition),
      }

      let view = this.jokers.get(joker.uid)
      if (!view) {
        view = new JokerView(joker, look)
        view.eventMode = 'static'
        view.cursor = 'pointer'
        view.on('pointertap', () => this.act({ t: 'sell_joker', index }))
        this.jokers.set(joker.uid, view)
        this.boardLayer.addChild(view)
      } else {
        view.set(joker, look)
      }

      view.position.set(360 + index * (SIZE.jokerWidth + 10), SIZE.jokerY)
    })
  }

  private readonly shopLayer = new Container()

  private syncShop(): void {
    if (!this.shopLayer.parent) this.world.addChild(this.shopLayer)
    this.shopLayer.removeChildren().forEach(child => child.destroy())
    this.shopLayer.visible = this.state.phase === 'shop'
    if (!this.shopLayer.visible) return

    this.state.shop.cards.forEach((item, slot) => {
      const tile = new Panel(140, 96)
      tile.position.set(SIZE.width / 2 - 152 + slot * 152, 250)

      const name = new Text({
        text: shopLabel(item.kind, item.id, this.data),
        style: { fontSize: 12, fill: COLOR.ink, wordWrap: true, wordWrapWidth: 124 },
      })
      name.position.set(8, 8)

      const price = new Text({
        text: `$${item.cost}`,
        style: { fontSize: 16, fill: COLOR.money, fontWeight: '700' },
      })
      price.position.set(8, 70)

      tile.addChild(name, price)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => {
        this.audio.play('shop_buy')
        this.act({ t: 'buy', slot })
      })
      this.shopLayer.addChild(tile)
    })

    if (this.state.shop.voucher) {
      const tile = new Panel(170, 60, 0x1c2f3f)
      tile.position.set(SIZE.width / 2 - 85, 370)
      const name = new Text({
        text: `${this.data.tables.voucher.findByVoucherId(this.state.shop.voucher)?.name ?? ''}  $${this.data.economy.voucherCost}`,
        style: { fontSize: 12, fill: COLOR.ink, wordWrap: true, wordWrapWidth: 154 },
      })
      name.position.set(8, 8)
      tile.addChild(name)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => this.act({ t: 'buy_voucher' }))
      this.shopLayer.addChild(tile)
    }

    this.state.consumables.forEach((item, index) => {
      const tile = new Panel(120, 44, 0x2a2440)
      tile.position.set(SIZE.width / 2 - 126 + index * 132, 450)
      const name = new Text({ text: item.id, style: { fontSize: 11, fill: COLOR.ink } })
      name.position.set(8, 6)
      const hint = new Text({ text: '쓴다', style: { fontSize: 10, fill: COLOR.inkDim } })
      hint.position.set(8, 24)
      tile.addChild(name, hint)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => this.act({ t: 'use_consumable', index }))
      this.shopLayer.addChild(tile)
    })
  }
}

function phaseLabel(state: RunState): string {
  switch (state.phase) {
    case 'blind-select': return `안테 ${state.ante} — ${blindName(state.blind)}`
    case 'round': return `안테 ${state.ante} — ${blindName(state.blind)}`
    case 'shop': return '상점'
    case 'won': return '끝까지 갔습니다'
    case 'lost': return '여기까지입니다'
    default: return ''
  }
}

function blindName(blind: BlindKind): string {
  return blind === BlindKind.Small ? '스몰' : blind === BlindKind.Big ? '빅' : '보스'
}

function subLabel(state: RunState, data: Data): string {
  if (state.phase === 'shop') return `$${state.money} · 다음 안테로 갑니다`
  const boss = state.blind === BlindKind.Boss
    ? ` · ${data.tables.bossBlind.findByBossId(state.bossId)?.name ?? ''}`
    : ''
  return `요구 ${state.target} · 핸드 ${state.handsLeft} · 버리기 ${state.discardsLeft}${boss}`
}

function shopLabel(kind: ShopItemKind, id: string, data: Data): string {
  switch (kind) {
    case ShopItemKind.Joker: return data.tables.joker.findByJokerId(id)?.name ?? id
    case ShopItemKind.Tarot: return data.tables.tarot.findByTarotId(id)?.name ?? id
    case ShopItemKind.Planet: return data.tables.planet.findByPlanetId(id)?.name ?? id
    case ShopItemKind.Spectral: return data.tables.spectral.findBySpectralId(id)?.name ?? id
    default: return id
  }
}

/** 그림자 하나. 화면이 비어 보이지 않게 하는 것뿐입니다. */
export function backdrop(width: number, height: number): Graphics {
  const graphics = new Graphics()
  graphics.rect(0, 0, width, height).fill(COLOR.ground)
  return graphics
}
