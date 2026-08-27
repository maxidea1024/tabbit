// 화면.
//
// **코어를 부르고 이벤트를 받아 그립니다.** 규칙은 여기 없습니다 — 어디에 놓을지와 얼마나
// 세게 보일지뿐이고, 뒤쪽의 수치는 `Const_Feel` 이므로 데이터입니다.
//
// 배치는 왼쪽에 판돈과 점수, 위에 조커와 소모품, 가운데에 낸 카드, 아래에 패입니다.
// 시선이 왼쪽에서 오른쪽으로 한 번 흐르게 두었습니다.

import { Container, Graphics, Sprite, Text, Texture, type Application } from 'pixi.js'

import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { evaluate } from '../core/hand'
import { apply, newRun, type Action } from '../core/run'
import { rerollCost } from '../core/shop'
import type { CardInstance, GameEvent, RunState } from '../core/state'
import { BackgroundFilter } from '../shader/background'
import { PunchFilter } from '../shader/punch'
import { Audio } from './audio'
import { CardView, type EditionLook } from './card-view'
import { BlindBadge, Slot } from './hud'
import { JokerView } from './joker-view'
import {
  buildTimeline, particlesOf, readFeel, scaleOf, semitonesOf, shakeOf, TimelinePlayer,
  type Beat, type Feel,
} from './juice'
import { Particles } from './particles'
import { COLOR, rarityColor, SIZE } from './theme'
import { Button, Panel } from '../ui/widgets'
import { Guide } from '../ui/guide'
import { Tooltip } from '../ui/tooltip'

// 화면의 자리. **원작의 배치를 따릅니다** — 왼쪽에 판돈과 점수가 세로로 쌓이고, 위에 조커와
// 소모품이 나란히 서고, 가운데에서 카드를 내고, 아래에 패가 부챗살로 펴집니다.
const LEFT = 16
const PANEL_W = 264
/** 판이 놓이는 자리의 가운데. 왼쪽 패널을 뺀 나머지의 한가운데입니다. */
const BOARD_X = (LEFT + PANEL_W + 20 + SIZE.width) / 2
const JOKER_Y = 108
/** 조커 5칸이 시작하는 자리. */
const JOKER_X = 372
const CONSUMABLE_X = 962
const PLAY_Y = 352
/** 고른 카드가 무슨 족보인지 뜨는 자리. */
const PREVIEW_Y = 500
const HAND_Y = 646
/** 버튼 줄. **패 아래입니다** — 패와 겹치면 카드를 고를 수가 없습니다. */
const BUTTON_Y = 742
const DECK_X = SIZE.width - 76
const DECK_Y = 646

export class Game {
  private readonly world = new Container()
  private readonly backdrop = new Container()
  private readonly board = new Container()
  private readonly overlay = new Container()

  private readonly state: RunState
  private readonly feel: Feel
  private readonly audio: Audio
  private readonly player: TimelinePlayer
  private readonly background = new BackgroundFilter()
  private readonly particles = new Particles()
  private readonly punch = new PunchFilter(SIZE.width, SIZE.height)
  private readonly tooltip = new Tooltip()

  private readonly cards = new Map<number, CardView>()
  private readonly playedViews: CardView[] = []
  private readonly jokers = new Map<number, JokerView>()
  private readonly selected = new Set<number>()

  private readonly badge = new BlindBadge(PANEL_W)
  private readonly score = new Slot('라운드 점수', PANEL_W, 68, COLOR.ink)
  private readonly chips = new Slot('칩', 118, 56, COLOR.chips)
  private readonly mult = new Slot('배수', 118, 56, COLOR.mult)
  private readonly hands = new Slot('핸드', 124, 52, COLOR.good)
  private readonly discards = new Slot('버리기', 124, 52, 0xff9d5c)
  private readonly money = new Slot('금액', 124, 52, COLOR.money)
  private readonly anteSlot = new Slot('안테', 124, 52, COLOR.ink)

  private readonly headline = new Text({
    text: '', style: { fontSize: 20, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly gauge = new Graphics()
  private readonly frames = new Graphics()
  private readonly jokerCount = new Text({
    text: '', style: { fontSize: 11, fill: COLOR.inkDim, fontWeight: '700' },
  })
  private readonly consumableCount = new Text({
    text: '소모품', style: { fontSize: 11, fill: 0x9b8fd0, fontWeight: '700' },
  })
  private readonly deckLabel = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
  })

  private readonly playButton: Button
  private readonly discardButton: Button
  private readonly primaryButton: Button
  private readonly skipButton: Button
  private readonly rerollButton: Button
  private readonly sortRankButton: Button
  private readonly sortSuitButton: Button
  private readonly infoButton: Button
  private readonly guideButton: Button
  /** 게임 방법. **첫 판에서 저절로 한 번 열립니다.** */
  private readonly guide = new Guide(() => this.guide.close())
  /**
   * 지금 무엇을 하면 되는가.
   *
   * **국면마다 한 줄입니다.** 화면에 버튼이 여럿 있어도 다음에 누를 것이 무엇인지가
   * 적혀 있지 않으면 처음 여는 사람은 움직이지 못합니다.
   */
  private readonly hint = new Text({
    text: '',
    style: {
      fontSize: 13, fill: 0xa9c6b3, fontWeight: '700', lineHeight: 19,
      wordWrap: true, wordWrapWidth: PANEL_W - 12,
    },
  })
  private readonly shopLayer = new Container()
  private readonly consumableLayer = new Container()
  /** 고른 카드가 무슨 족보인지. **이것이 없으면 감으로 내야 합니다.** */
  private readonly preview = new Container()
  private readonly previewPlate = new Graphics()
  private readonly previewHand = new Text({
    text: '', style: { fontSize: 19, fill: COLOR.ink, fontWeight: '800' },
  })
  private readonly previewValue = new Text({
    text: '', style: { fontSize: 16, fill: COLOR.inkDim, fontWeight: '700' },
  })
  /** 족보 목록. 무엇이 몇 점인지 볼 수 있어야 무엇을 키울지 정합니다. */
  private readonly handList = new Container()
  private handListOpen = false
  /** 끝났을 때 덮는 판. */
  private readonly gameOver = new Container()

  private shake = 0
  /** 점수가 멈춘 뒤 낸 카드를 얼마나 붙잡아 두었는가. */
  private holdAfterScore = 0
  private liveChips = 0
  private liveMult = 10_000
  private clock = 0
  private pointerAt = { x: 0, y: 0 }

  constructor(private readonly app: Application, private readonly data: Data, seed: string) {
    this.feel = readFeel(data.feel)
    this.audio = new Audio(data.tables)
    this.state = newRun(data, seed, 'red_deck', 'White').state
    this.player = new TimelinePlayer(beat => this.showBeat(beat))

    // 배경은 흰 스프라이트 한 장에 셰이더를 얹은 것입니다.
    const sheet = new Sprite(Texture.WHITE)
    sheet.width = SIZE.width
    sheet.height = SIZE.height
    sheet.filters = [this.background]
    this.backdrop.addChild(sheet)

    app.stage.addChild(this.world)
    this.world.addChild(this.backdrop, this.board, this.particles, this.overlay, this.tooltip)
    this.board.sortableChildren = true

    this.buildPanel()

    this.playButton = new Button('낸다', 128, 46, 0x2f7a52, () => this.play())
    this.discardButton = new Button('버린다', 128, 46, 0x8a4632, () => this.discard())
    this.primaryButton = new Button('이 블라인드로', 210, 50, 0x2f7a52, () => this.primary())
    this.skipButton = new Button('건너뛴다', 150, 38, 0x3f4560,
      () => this.act({ t: 'skip_blind' }))
    this.rerollButton = new Button('리롤', 128, 44, 0x3a4c6b, () => this.reroll())
    this.sortRankButton = new Button('랭크순', 92, 32, 0x24402f, () => this.sortHand('rank'))
    this.sortSuitButton = new Button('무늬순', 92, 32, 0x24402f, () => this.sortHand('suit'))
    this.infoButton = new Button('족보 목록', 118, 34, 0x2a3550, () => this.toggleHandList())
    this.guideButton = new Button('게임 방법', 118, 34, 0x2a3550, () => this.guide.open())

    this.overlay.addChild(this.playButton, this.discardButton, this.primaryButton,
      this.skipButton, this.rerollButton, this.shopLayer,
      this.sortRankButton, this.sortSuitButton, this.infoButton, this.guideButton,
      this.preview, this.handList, this.gameOver, this.guide)

    this.preview.addChild(this.previewPlate, this.previewHand, this.previewValue)
    this.preview.visible = false
    this.handList.visible = false
    this.gameOver.visible = false
    this.playButton.position.set(BOARD_X - 152, BUTTON_Y)
    this.discardButton.position.set(BOARD_X + 24, BUTTON_Y)
    this.primaryButton.position.set(BOARD_X - 105, 520)
    this.skipButton.position.set(BOARD_X - 75, 586)
    this.rerollButton.position.set(BOARD_X - 64, 600)
    this.sortRankButton.position.set(LEFT + PANEL_W + 30, BUTTON_Y + 7)
    this.sortSuitButton.position.set(LEFT + PANEL_W + 130, BUTTON_Y + 7)
    this.infoButton.position.set(LEFT - 2, 700)
    this.guideButton.position.set(LEFT + 134, 700)

    app.canvas.addEventListener('pointerdown', () => this.audio.unlock())
    app.stage.eventMode = 'static'
    app.stage.hitArea = { contains: () => true }
    app.stage.on('globalpointermove', event => {
      this.pointerAt = this.world.toLocal(event.global)
    })
    window.addEventListener('keydown', () => {
      this.audio.unlock()
      if (this.player.busy) this.player.hurry(this.feel)
    })

    this.refresh()
    app.ticker.add(ticker => this.tick(ticker.deltaMS))

    // 처음 여는 사람에게 한 번 펼쳐 줍니다. 두 번째부터는 버튼으로만 엽니다.
    try {
      if (localStorage.getItem('clover.guide.seen') === null) {
        this.guide.open()
        localStorage.setItem('clover.guide.seen', '1')
      }
    } catch {
      // 저장소가 막힌 브라우저에서는 그냥 열지 않습니다.
    }
  }

  // ---------------------------------------------------------------- 뼈대

  private buildPanel(): void {
    const panel = new Panel(PANEL_W + 24, SIZE.height - 44, 0x081410)
    panel.position.set(LEFT - 12, 22)
    this.board.addChild(panel, this.frames)

    this.badge.position.set(LEFT, 34)
    this.score.position.set(LEFT, 212)
    this.chips.position.set(LEFT, 290)
    this.mult.position.set(LEFT + 140, 290)
    this.hands.position.set(LEFT, 362)
    this.discards.position.set(LEFT + 134, 362)
    this.money.position.set(LEFT, 426)
    this.anteSlot.position.set(LEFT + 134, 426)

    const times = new Text({ text: '×', style: { fontSize: 24, fill: COLOR.inkDim } })
    times.anchor.set(0.5)
    times.position.set(LEFT + 129, 324)

    this.board.addChild(this.badge, this.score, this.chips, times, this.mult,
      this.hands, this.discards, this.money, this.anteSlot)

    this.headline.anchor.set(0.5, 0)
    this.headline.position.set(BOARD_X, 244)

    this.jokerCount.anchor.set(0.5, 1)
    this.jokerCount.position.set(
      JOKER_X + (SIZE.jokerWidth + 12) * 2, JOKER_Y - SIZE.jokerHeight / 2 - 12)
    this.consumableCount.anchor.set(0.5, 1)
    this.consumableCount.position.set(
      CONSUMABLE_X + (SIZE.jokerWidth + 12) / 2, JOKER_Y - SIZE.jokerHeight / 2 - 12)

    this.deckLabel.anchor.set(0.5, 0)
    this.deckLabel.position.set(DECK_X, DECK_Y + 76)

    // 덱 더미. **카드가 어디에서 오는지 보여야 뽑는 연출이 뜻을 가집니다.**
    const pile = new Graphics()
    for (let i = 4; i >= 0; i--) {
      pile.roundRect(DECK_X - SIZE.cardWidth / 2 + i * 2, DECK_Y - SIZE.cardHeight / 2 - i * 3,
        SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius)
        .fill(COLOR.cardBack)
      pile.roundRect(DECK_X - SIZE.cardWidth / 2 + i * 2, DECK_Y - SIZE.cardHeight / 2 - i * 3,
        SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius)
        .stroke({ color: 0x2f7a52, width: 1.5 })
    }

    // 지시문은 패널의 빈 자리에 둡니다. **눈이 마지막으로 머무는 곳이 여기입니다.**
    this.hint.position.set(LEFT + 2, 640)

    this.board.addChild(pile, this.headline, this.gauge, this.jokerCount,
      this.consumableCount, this.deckLabel, this.consumableLayer, this.hint)
  }

  /** 조커와 소모품의 빈 자리. **비어 있어도 자리가 보여야 무엇을 모으는 게임인지 압니다.** */
  private drawFrames(): void {
    const g = this.frames
    g.clear()

    for (let i = 0; i < this.state.rules.jokerSlots; i++) {
      const x = JOKER_X + i * (SIZE.jokerWidth + 12)
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .fill({ color: 0x08130e, alpha: 0.55 })
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .stroke({ color: COLOR.panelEdge, width: 1.5, alpha: 0.8 })
    }

    for (let i = 0; i < this.state.rules.consumableSlots; i++) {
      const x = CONSUMABLE_X + i * (SIZE.jokerWidth + 12)
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .fill({ color: 0x140f22, alpha: 0.55 })
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .stroke({ color: 0x4a3f6b, width: 1.5, alpha: 0.9 })
    }
  }

  layout(width: number, height: number): void {
    const scale = Math.min(width / SIZE.width, height / SIZE.height)
    this.world.scale.set(scale)
    this.world.position.set(
      (width - SIZE.width * scale) / 2, (height - SIZE.height * scale) / 2)
    this.background.setAspect(SIZE.width / SIZE.height)
    this.sharpen(scale)
  }

  /**
   * 글씨를 화면 배율에 맞춰 다시 굽습니다.
   *
   * **월드를 통째로 확대하므로 글씨가 그대로면 뿌옇습니다.** 글씨는 한 번 그림으로 구워서
   * 쓰는 것이라, 구울 때의 배율이 화면 배율보다 작으면 늘려 놓은 그림이 됩니다.
   */
  private sharpen(scale: number): void {
    const want = Math.min(3, Math.max(1, scale) * (this.app.renderer.resolution ?? 1))
    const walk = (node: Container) => {
      if (node instanceof Text && node.resolution !== want) node.resolution = want
      for (const child of node.children) walk(child as Container)
    }
    walk(this.world)
    this.textScale = want
  }

  private textScale = 1

  // ---------------------------------------------------------------- 액션

  private act(action: Action): void {
    if (this.player.busy) return
    const step = apply(this.data, this.state, action)
    this.refresh()
    this.startTimeline(step.events)
  }

  private primary(): void {
    if (this.state.phase === 'blind-select') this.act({ t: 'select_blind' })
    else if (this.state.phase === 'shop') this.act({ t: 'leave_shop' })
  }

  private reroll(): void {
    this.audio.play('shop_reroll')
    this.act({ t: 'reroll' })
  }

  private play(): void {
    if (this.selected.size === 0 || this.player.busy) return
    const cards = this.orderedSelection()
    this.selected.clear()
    this.liftToPlayArea(cards)
    this.act({ t: 'play', cards })
  }

  private discard(): void {
    if (this.selected.size === 0 || this.player.busy) return
    const cards = this.orderedSelection()
    this.selected.clear()
    for (const uid of cards) {
      const view = this.cards.get(uid)
      if (view) this.particles.burst(view.x, view.y, 6, 0xff9d5c, 0.6)
    }
    this.audio.play('card_destroy')
    this.act({ t: 'discard', cards })
  }

  /** 고른 카드를 패의 순서대로. **낸 순서가 득점 순서입니다.** */
  private orderedSelection(): number[] {
    return this.state.hand.filter(uid => this.selected.has(uid))
  }

  /** 패를 정렬합니다. **낼 것을 고르는 일이 훨씬 쉬워집니다.** */
  private sortHand(by: 'rank' | 'suit'): void {
    if (this.player.busy) return
    const cards = this.state.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    cards.sort((a, b) => by === 'rank'
      ? b.rank - a.rank || a.suit - b.suit
      : a.suit - b.suit || b.rank - a.rank)

    this.state.hand = cards.map(card => card.uid)
    this.audio.play('card_select')
    this.refresh()
  }

  /** 족보 목록을 열고 닫습니다. */
  private toggleHandList(): void {
    this.handListOpen = !this.handListOpen
    this.drawHandList()
  }

  private toggle(uid: number): void {
    if (this.player.busy) return
    if (this.selected.has(uid)) this.selected.delete(uid)
    else if (this.selected.size < this.data.run.maxPlayedCards) this.selected.add(uid)
    this.audio.play('card_select')
    this.refresh()
  }

  private liftToPlayArea(uids: number[]): void {
    const spacing = SIZE.cardWidth + 16
    const startX = BOARD_X - ((uids.length - 1) * spacing) / 2

    uids.forEach((uid, index) => {
      const view = this.cards.get(uid)
      if (!view) return
      this.cards.delete(uid)
      this.playedViews.push(view)
      view.eventMode = 'none'
      view.hovered = false
      view.selected = false
      view.idle = 0.4
      view.place(startX + index * spacing, PLAY_Y, 0)
      view.zIndex = 100 + index
    })
  }

  /** 낸 카드를 물러나게 합니다. 화면 밖으로 나가면 그때 지웁니다. */
  private clearPlayArea(): void {
    for (const view of this.playedViews) view.retire()
  }

  /** 물러난 카드를 치웁니다. */
  private reapPlayArea(): void {
    for (let i = this.playedViews.length - 1; i >= 0; i--) {
      if (!this.playedViews[i].gone) continue
      this.playedViews[i].destroy()
      this.playedViews.splice(i, 1)
    }
  }

  // ---------------------------------------------------------------- 연출

  private startTimeline(events: GameEvent[]): void {
    const beats = buildTimeline(events, this.feel)
    if (beats.length === 0) {
      this.chips.reset(0)
      this.mult.reset(0)
      this.score.target = Number(this.state.score)
      return
    }

    this.liveChips = 0
    this.liveMult = 10_000
    this.player.play(beats)
  }

  private showBeat(beat: Beat): void {
    const event = beat.event
    const semitones = semitonesOf(beat.intensity, this.feel)
    const dust = particlesOf(beat.intensity, this.feel)

    switch (event.t) {
      case 'HandEvaluated':
        this.liveChips = event.chips
        this.liveMult = event.mult
        this.headline.text = `${this.handName(event.hand)}   레벨 ${event.level}`
        this.audio.play('score_count', semitones)
        break

      case 'CardScored': {
        this.liveChips += event.chips
        const view = this.viewOf(event.uid)
        if (view) {
          view.pop(0.6 + beat.intensity)
          this.particles.burst(view.x, view.y - 40, 4 + dust, COLOR.chips, 0.6 + beat.intensity)
        }
        this.popAt(view, `+${event.chips}`, COLOR.chips, beat.intensity)
        this.audio.play('card_chip', semitones)
        this.jolt(1.6 + beat.intensity * 4, 0.3 + beat.intensity)
        break
      }

      case 'JokerTriggered': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        const mul = event.op === 'MulMult'
        const cue = mul ? 'joker_mul' : event.op === 'AddMoney' ? 'joker_money' : 'joker_add'
        const text = mul ? `×${(event.mult / 10_000).toFixed(2)}`
          : event.chips !== 0 ? `+${event.chips}`
            : event.mult !== 0 ? `+${(event.mult / 10_000).toFixed(0)}`
              : `$${event.money}`

        if (view) {
          view.pop(mul ? 1.2 : 0.8)
          this.particles.burst(view.x, view.y, 6 + dust,
            mul ? COLOR.mult : event.chips !== 0 ? COLOR.chips : COLOR.money, 1)
        }
        this.popAt(view, text, mul || event.chips === 0 ? COLOR.mult : COLOR.chips,
          beat.intensity + (mul ? 0.3 : 0))
        this.audio.play(cue, semitones)
        this.jolt(mul ? 7 + beat.intensity * 8 : 3 + beat.intensity * 5,
          mul ? 1.2 + beat.intensity : 0.5 + beat.intensity)
        break
      }

      case 'JokerFizzled': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        this.popAt(view, `${event.num}/${event.den}`, COLOR.inkDim, 0)
        this.audio.play('joker_fizzle')
        break
      }

      case 'Retriggered': {
        const view = this.viewOf(event.uid)
        if (view) view.pop(0.9)
        this.popAt(view, '다시', COLOR.good, beat.intensity)
        this.audio.play('retrigger', semitones)
        this.jolt(3, 0.6)
        break
      }

      case 'ScoreResolved':
        this.score.target = Number(event.score)
        this.audio.play('score_settle', semitones)
        this.jolt(6 + shakeOf(beat.intensity, this.feel), 1.4 + beat.intensity * 1.6)
        this.particles.burst(LEFT + PANEL_W / 2, 250, 16 + dust * 2, COLOR.ink,
          1.2 + beat.intensity)
        this.particles.burst(BOARD_X, PLAY_Y, 12 + dust, COLOR.mult, 1 + beat.intensity)
        break

      case 'BlindCleared':
        this.headline.text = '넘겼습니다'
        this.audio.play('blind_clear')
        this.particles.burst(BOARD_X, PLAY_Y, 48, COLOR.good, 1.6)
        this.jolt(10, 2)
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
        this.particles.burst(BOARD_X, SIZE.height / 2, 80, COLOR.money, 2)
        break

      default:
        break
    }

    this.chips.target = this.liveChips
    this.mult.target = Math.round(this.liveMult / 10_000)
    this.chips.emphasize(scaleOf(beat.intensity, this.feel))
    this.mult.emphasize(scaleOf(beat.intensity, this.feel))
  }

  /** 한 방. 흔들림과 색수차를 함께 겁니다. */
  private jolt(shake: number, chroma: number): void {
    this.shake = Math.max(this.shake, shake)
    this.punch.hit(Math.min(chroma, this.feel.chromaticMaxPx * 2))
  }

  private popAt(target: Container | undefined, text: string, tint: number,
                intensity: number): void {
    const label = new Text({
      text,
      style: {
        fontSize: 20 + intensity * 16, fill: tint, fontWeight: '800',
        stroke: { color: 0x06100b, width: 4 },
      },
    })
    label.anchor.set(0.5, 1)
    label.position.set(
      target ? target.x : BOARD_X, (target ? target.y : SIZE.height / 2) - 46)
    label.resolution = this.textScale
    this.overlay.addChild(label)

    // **그냥 뜨면 심심합니다.** 튀어나왔다가 부르르 떨며 올라갑니다.
    const homeX = label.x
    const homeY = label.y
    const drift = (Math.random() - 0.5) * 34
    const rumble = 3 + intensity * 7
    const span = 760
    let life = 0

    label.scale.set(0.4)
    const rise = () => {
      life += this.app.ticker.deltaMS
      const t = Math.min(1, life / span)

      // 처음 120밀리초는 튀어나오는 구간입니다. 1을 넘겼다가 돌아옵니다.
      const grow = life < 120
        ? 0.4 + 0.85 * (life / 120)
        : 1.25 - 0.25 * Math.min(1, (life - 120) / 220)
      label.scale.set(grow + t * 0.18)

      const fade = Math.max(0, 1 - t)
      const shiver = rumble * fade * fade
      label.x = homeX + drift * t + (Math.random() - 0.5) * shiver
      label.y = homeY - t * 46 + (Math.random() - 0.5) * shiver
      label.rotation = (Math.random() - 0.5) * 0.05 * fade
      label.alpha = fade

      if (life >= span) {
        this.app.ticker.remove(rise)
        label.destroy()
      }
    }
    this.app.ticker.add(rise)
  }

  private handName(hand: PokerHandKind): string {
    const key = `hand.${PokerHandKind[hand]}.name`
    return this.data.tables.stringTable.findByStringId(key)?.ko ?? PokerHandKind[hand]
  }

  private viewOf(uid: number): CardView | undefined {
    return this.cards.get(uid) ?? this.playedViews.find(view => view.uid === uid)
  }

  private jokerUidAt(slot: number): number {
    return this.state.jokers[slot]?.uid ?? -1
  }

  // ---------------------------------------------------------------- 매 프레임

  private tick(deltaMs: number): void {
    const seconds = deltaMs / 1000
    this.clock += seconds

    this.player.advance(deltaMs)
    this.publishPeek()

    this.background.advance(seconds)
    this.punch.advance(seconds)

    // **필터는 필요할 때만 겁니다.** 늘 걸어 두면 판이 매 프레임 그림으로 한 번 구워지고,
    // 그 그림이 화면 배율에 늘어나 글씨가 뿌옇게 됩니다.
    const punching = !this.punch.quiet
    const filtered = (this.board.filters as unknown[] | null)?.length ?? 0
    if (punching && filtered === 0) this.board.filters = [this.punch]
    else if (!punching && filtered > 0) this.board.filters = []
    this.background.setHeat(this.heat())
    this.particles.advance(seconds)

    for (const slot of [this.score, this.chips, this.mult, this.money]) slot.advance(deltaMs)

    for (const view of this.cards.values()) {
      view.pointer = this.tiltFor(view)
      view.advance(seconds, this.clock)
    }
    for (const view of this.playedViews) view.advance(seconds, this.clock)
    const before = this.playedViews.length
    this.reapPlayArea()
    if (before > 0 && this.playedViews.length === 0) {
      this.holdAfterScore = 0
      this.refresh()
    }
    for (const view of this.jokers.values()) {
      view.pointer = this.tiltFor(view)
      view.advance(seconds, this.clock)
    }

    this.gauge.visible = this.state.phase === 'round'
    if (this.gauge.visible) this.drawGauge()

    // 흔들림은 줄어듭니다. **판만 흔들고 배경은 가만히 둡니다** — 둘 다 흔들면 무엇이
    // 맞은 것인지 읽히지 않습니다.
    if (this.shake > 0.08) {
      const angle = Math.random() * Math.PI * 2
      this.board.position.set(
        Math.cos(angle) * this.shake, Math.sin(angle) * this.shake)
      this.overlay.position.set(this.board.x * 0.5, this.board.y * 0.5)
      this.shake *= 0.84
    } else if (this.board.x !== 0 || this.board.y !== 0) {
      this.board.position.set(0, 0)
      this.overlay.position.set(0, 0)
      this.shake = 0
    }

    if (!this.player.busy) {
      this.chips.emphasize(1)
      this.mult.emphasize(1)
      // **결과를 읽을 시간을 둡니다.** 점수가 다 굴러간 뒤에도 잠깐 남아 있어야
      // 무엇을 냈고 얼마가 되었는지가 보입니다.
      if (this.playedViews.length > 0 && this.score.settled) {
        this.holdAfterScore += deltaMs
        if (this.holdAfterScore > 700 && !this.playedViews[0].retiring) {
          this.clearPlayArea()
        }
      } else {
        this.holdAfterScore = 0
      }
      if (this.state.phase !== 'round') {
        this.chips.target = 0
        this.mult.target = 0
      }
    }
  }

  /** 배경이 얼마나 뜨거운가. 점수가 요구에 가까울수록 올라갑니다. */
  private heat(): number {
    if (this.state.phase === 'shop') return 0.15
    if (this.state.target <= 0) return 0.1
    return Math.max(0.08, Math.min(1, Number(this.state.score) / Number(this.state.target)))
  }

  private tiltFor(view: Container): number {
    return Math.max(-1, Math.min(1, (this.pointerAt.x - view.x) / 90))
  }

  private drawGauge(): void {
    const x = BOARD_X - 220
    const y = 276
    const width = 440
    const height = 12

    this.gauge.clear()
    const ratio = this.state.target > 0
      ? Number(this.state.score) / Number(this.state.target) : 0

    this.gauge.roundRect(x, y, width, height, 6).fill(0x0a1811)
    this.gauge.roundRect(x, y, width * Math.min(1, ratio), height, 6)
      .fill(ratio >= 1 ? COLOR.good : COLOR.chips)
    this.gauge.roundRect(x - 0.5, y - 0.5, width + 1, height + 1, 6)
      .stroke({ color: COLOR.panelEdge, width: 1 })
  }

  private publishPeek(): void {
    const state = this.state
    ;(window as unknown as { __clover?: unknown }).__clover = {
      phase: state.phase, ante: state.ante, blind: state.blind,
      money: state.money, score: Number(state.score), target: Number(state.target),
      jokers: state.jokers.length, discards: state.discardsLeft,
      busy: this.player.busy || !this.score.settled,
      hand: state.hand.map(uid => {
        const card = state.deck.find(entry => entry.uid === uid)
        return { rank: card?.rank ?? 0, suit: card?.suit ?? 0 }
      }),
    }
  }

  // ---------------------------------------------------------------- 다시 그리기

  private editionLook(edition: EditionKind): EditionLook | undefined {
    const row = this.data.tables.editionVisual.findByEdition(edition)
    if (!row || row.shader === 'none') return undefined
    return {
      shader: row.shader as EditionLook['shader'],
      strength: row.strength, flowSpeed: row.flowSpeed, noise: row.noise,
    }
  }

  private refresh(): void {
    const state = this.state
    this.publishPeek()

    this.money.target = state.money
    this.score.target = Number(state.score)
    this.hands.text = String(state.handsLeft)
    this.discards.text = String(state.discardsLeft)
    this.anteSlot.text = `${state.ante} / ${this.data.run.winAnte}`
    this.deckLabel.text = `덱  ${state.drawPile.length} / ${state.deck.length}`
    this.jokerCount.text = `조커  ${state.jokers.length} / ${state.rules.jokerSlots}`

    this.syncBadge()
    this.drawFrames()
    this.syncCards()
    this.syncJokers()
    this.syncConsumables()
    this.syncShop()
    this.syncButtons()
    this.syncMood()
    this.drawPreview()
    this.drawHandList()
    this.drawGameOver()
    this.hint.text = this.hintText()
    this.sharpen(this.world.scale.x)
  }

  /** 지금 국면에서 다음에 할 것. */
  private hintText(): string {
    const state = this.state
    switch (state.phase) {
      case 'blind-select':
        return state.blind === BlindKind.Boss
          ? '보스 블라인드입니다. 효과를 확인하고 「이 블라인드로」 를 누릅니다.'
          : '「이 블라인드로」 를 누르면 판이 시작됩니다. 「건너뛴다」 는 태그를 받고 넘깁니다.'
      case 'round':
        if (this.selected.size === 0) {
          return `패에서 카드를 눌러 최대 5장까지 고릅니다. 남은 핸드 ${state.handsLeft}회.`
        }
        return '「낸다」 로 점수를 내거나, 「버린다」 로 고른 카드를 바꿉니다.'
      case 'shop':
        return state.jokers.length === 0
          ? '조커를 눌러 삽니다. 조커가 없으면 점수가 좀처럼 오르지 않습니다.'
          : '살 것을 누릅니다. 끝나면 「다음 블라인드로」 를 누릅니다.'
      default:
        return ''
    }
  }

  /**
   * 고른 카드가 무슨 족보이고 얼마짜리인지.
   *
   * **조커를 뺀 순수한 값입니다** — 조커까지 미리 세면 득점 연출이 볼 것이 없어집니다.
   */
  private drawPreview(): void {
    const picked = this.orderedSelection()
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    this.preview.visible = this.state.phase === 'round' && picked.length > 0
    if (!this.preview.visible) return

    const { hand } = evaluate(picked, this.state.rules)
    const row = this.data.tables.pokerHand.findByHand(hand)
    const level = this.state.handLevels[PokerHandKind[hand]] ?? 1
    const chips = (row?.baseChips ?? 0) + (row?.chipsPerLevel ?? 0) * (level - 1)
    const mult = (row?.baseMult ?? 0) + (row?.multPerLevel ?? 0) * (level - 1)

    this.previewHand.text = `${this.handName(hand)}   레벨 ${level}`
    this.previewValue.text = `칩 ${chips}  ×  배수 ${mult}   =   ${chips * mult}`

    const width = Math.max(this.previewHand.width, this.previewValue.width) + 40
    this.previewPlate.clear()
    this.previewPlate.roundRect(0, 0, width, 66, 10).fill({ color: 0x08150f, alpha: 0.92 })
    this.previewPlate.roundRect(0.5, 0.5, width - 1, 65, 10)
      .stroke({ color: COLOR.chips, width: 1.5, alpha: 0.8 })

    this.previewHand.position.set(20, 10)
    this.previewValue.position.set(20, 36)
    this.preview.position.set(BOARD_X - width / 2, PREVIEW_Y)
  }

  /** 족보 목록. 레벨이 오른 것이 위로 오지 않고 표의 순서 그대로입니다. */
  private drawHandList(): void {
    this.handList.removeChildren().forEach(child => child.destroy())
    this.handList.visible = this.handListOpen
    if (!this.handListOpen) return

    const rows = this.data.tables.pokerHand.records
    const width = 420
    const height = 60 + rows.length * 30

    const plate = new Graphics()
    plate.roundRect(0, 0, width, height, 12).fill({ color: 0x07120d, alpha: 0.97 })
    plate.roundRect(0.5, 0.5, width - 1, height - 1, 12)
      .stroke({ color: COLOR.panelEdge, width: 2 })
    this.handList.addChild(plate)

    const title = new Text({
      text: '족보', style: { fontSize: 17, fill: COLOR.ink, fontWeight: '800' },
    })
    title.position.set(18, 14)
    const close = new Text({
      text: '닫기', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
    })
    close.anchor.set(1, 0)
    close.position.set(width - 18, 18)
    this.handList.addChild(title, close)

    rows.forEach((row, index) => {
      const key = PokerHandKind[row.hand]
      const level = this.state.handLevels[key] ?? 1
      const chips = row.baseChips + row.chipsPerLevel * (level - 1)
      const mult = row.baseMult + row.multPerLevel * (level - 1)
      const seen = row.visibleFromStart || (this.state.handPlayCounts[key] ?? 0) > 0
      const y = 50 + index * 30

      const name = new Text({
        text: seen ? this.handName(row.hand) : '???',
        style: { fontSize: 13, fill: seen ? COLOR.ink : COLOR.inkDim, fontWeight: '700' },
      })
      name.position.set(18, y)

      const lv = new Text({
        text: `Lv.${level}`,
        style: { fontSize: 12, fill: level > 1 ? COLOR.good : COLOR.inkDim, fontWeight: '700' },
      })
      lv.position.set(190, y + 1)

      const value = new Text({
        text: seen ? `${chips}  ×  ${mult}` : '—',
        style: { fontSize: 13, fill: seen ? COLOR.chips : COLOR.inkDim, fontWeight: '700' },
      })
      value.position.set(250, y)

      const played = new Text({
        text: `${this.state.handPlayCounts[key] ?? 0}회`,
        style: { fontSize: 11, fill: COLOR.inkDim },
      })
      played.anchor.set(1, 0)
      played.position.set(width - 18, y + 2)

      this.handList.addChild(name, lv, value, played)
    })

    this.handList.position.set(BOARD_X - width / 2, 170)
    this.handList.eventMode = 'static'
    this.handList.cursor = 'pointer'
    this.handList.on('pointertap', () => this.toggleHandList())
  }

  /**
   * 끝났을 때 덮는 판.
   *
   * **지고 나서 아무것도 없는 것이 가장 나쁩니다.** 어디까지 갔는지 보여주고 다시 시작할
   * 자리를 둡니다.
   */
  private drawGameOver(): void {
    const done = this.state.phase === 'lost' || this.state.phase === 'won'
    this.gameOver.removeChildren().forEach(child => child.destroy())
    this.gameOver.visible = done
    if (!done) return

    const won = this.state.phase === 'won'
    const veil = new Graphics()
    veil.rect(0, 0, SIZE.width, SIZE.height).fill({ color: 0x04090a, alpha: 0.82 })
    this.gameOver.addChild(veil)

    const plate = new Graphics()
    plate.roundRect(0, 0, 460, 300, 16).fill(won ? 0x123324 : 0x2a1218)
    plate.roundRect(0.5, 0.5, 459, 299, 16)
      .stroke({ color: won ? COLOR.good : COLOR.bad, width: 2.5 })
    plate.position.set(SIZE.width / 2 - 230, SIZE.height / 2 - 150)
    this.gameOver.addChild(plate)

    const title = new Text({
      text: won ? '끝까지 갔습니다' : '여기까지입니다',
      style: { fontSize: 30, fill: COLOR.ink, fontWeight: '800' },
    })
    title.anchor.set(0.5, 0)
    title.position.set(SIZE.width / 2, SIZE.height / 2 - 118)

    const lines = [
      `안테  ${this.state.ante} / ${this.data.run.winAnte}`,
      `이번 런에 낸 핸드  ${this.state.handsPlayedThisRun}`,
      `모은 조커  ${this.state.jokers.length}`,
      `시드  ${this.state.seed}`,
    ]
    const body = new Text({
      text: lines.join('\n'),
      style: { fontSize: 15, fill: 0xcfe3d6, lineHeight: 26, align: 'center' },
    })
    body.anchor.set(0.5, 0)
    body.position.set(SIZE.width / 2, SIZE.height / 2 - 62)

    const again = new Button('다시 시작', 200, 52, won ? 0x2f7a52 : 0x8a4632, () => {
      const seed = `CLOVER-${Math.floor(Math.random() * 1e6).toString().padStart(6, '0')}`
      location.href = `${location.pathname}?seed=${seed}`
    })
    again.position.set(SIZE.width / 2 - 100, SIZE.height / 2 + 68)

    this.gameOver.addChild(title, body, again)
    this.gameOver.zIndex = 10_000
  }

  private syncBadge(): void {
    const state = this.state

    if (state.phase === 'shop') {
      this.badge.set('상점', 0, 0,
        '조커와 소모품을 삽니다. 바우처는 런 내내 남습니다.', false)
      return
    }

    const boss = state.blind === BlindKind.Boss
    const row = this.data.tables.blind.findByBlind(state.blind)
    const bossRow = boss ? this.data.tables.bossBlind.findByBossId(state.bossId) : undefined

    const note = bossRow
      ? describe(this.data, this.data.bossEffects.get(state.bossId) ?? []).join(' · ')
      : ''

    this.badge.set(
      bossRow?.name ?? `${blindName(state.blind)} 블라인드`,
      Number(state.target), row?.reward ?? 0, note, boss)
  }

  private syncMood(): void {
    if (this.state.phase === 'shop') {
      this.background.setMood([0.03, 0.055, 0.09], [0.35, 0.55, 0.95])
    } else if (this.state.blind === BlindKind.Boss) {
      this.background.setMood([0.075, 0.02, 0.035], [0.95, 0.28, 0.35])
    } else {
      this.background.setMood([0.031, 0.075, 0.055], [0.25, 0.85, 0.55])
    }
  }

  private syncButtons(): void {
    const state = this.state
    const inRound = state.phase === 'round'

    this.playButton.visible = inRound
    this.discardButton.visible = inRound
    this.playButton.enabled = inRound && this.selected.size > 0 && state.handsLeft > 0
    this.discardButton.enabled = inRound && this.selected.size > 0 && state.discardsLeft > 0

    const choosing = state.phase === 'blind-select'
    this.primaryButton.visible = choosing || state.phase === 'shop'
    this.primaryButton.text = state.phase === 'shop' ? '다음 블라인드로' : '이 블라인드로'
    this.primaryButton.position.set(BOARD_X - 105, state.phase === 'shop' ? 672 : 520)
    this.skipButton.visible = choosing && state.blind !== BlindKind.Boss
    this.sortRankButton.visible = inRound
    this.sortSuitButton.visible = inRound
    this.infoButton.visible = state.phase !== 'lost' && state.phase !== 'won'
    this.rerollButton.visible = state.phase === 'shop'
    this.rerollButton.text = `리롤  $${rerollCost(this.data, state, state.shop)}`
    this.rerollButton.enabled = state.money >= rerollCost(this.data, state, state.shop)
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

    const spacing = Math.min(SIZE.cardWidth + 12, 720 / Math.max(1, hand.length))
    const startX = BOARD_X - ((hand.length - 1) * spacing) / 2

    hand.forEach((card, index) => {
      let view = this.cards.get(card.uid)

      if (!view) {
        view = new CardView(card, this.editionLook(card.edition))
        view.eventMode = 'static'
        view.cursor = 'pointer'
        const handle = view
        view.on('pointertap', () => this.toggle(card.uid))
        view.on('pointerover', () => { handle.hovered = true })
        view.on('pointerout', () => { handle.hovered = false })
        this.cards.set(card.uid, view)
        this.board.addChild(view)
        // 덱에서 날아옵니다. **곧바로 자리에 있으면 뽑았다는 느낌이 없습니다.**
        view.placeNow(DECK_X, DECK_Y)
        this.audio.play('card_draw')
      } else {
        view.set(card, this.editionLook(card.edition))
      }

      view.selected = this.selected.has(card.uid)

      // 부채꼴로 폅니다. 가운데가 높고 양끝이 기울어집니다.
      const offset = index - (hand.length - 1) / 2
      view.place(startX + index * spacing, HAND_Y + offset * offset * 1.1, offset * 2.2)
    })
  }

  private syncJokers(): void {
    const wanted = new Set(this.state.jokers.map(joker => joker.uid))

    for (const [uid, view] of this.jokers) {
      if (!wanted.has(uid)) {
        this.particles.burst(view.x, view.y, 14, rarityColor(view.look.rarity), 1)
        view.destroy()
        this.jokers.delete(uid)
      }
    }

    this.state.jokers.forEach((joker, index) => {
      const row = this.data.tables.joker.findByJokerId(joker.jokerId)
      const look = {
        name: row?.name ?? joker.jokerId,
        rarity: row?.rarity ?? 1,
        lines: describe(this.data, this.data.jokerEffects.get(joker.jokerId) ?? []),
        edition: this.editionLook(joker.edition),
      }

      let view = this.jokers.get(joker.uid)
      if (!view) {
        view = new JokerView(joker, look)
        view.eventMode = 'static'
        view.cursor = 'pointer'
        const handle = view
        view.on('pointertap', () => this.act({ t: 'sell_joker', index }))
        view.on('pointerover', () => {
          handle.hovered = true
          this.showTooltip(handle)
        })
        view.on('pointerout', () => {
          handle.hovered = false
          this.tooltip.hide()
        })
        this.jokers.set(joker.uid, view)
        this.board.addChild(view)
        view.motion.snap(JOKER_X + index * (SIZE.jokerWidth + 12), JOKER_Y - 160)
      } else {
        view.set(joker, look)
      }

      view.place(JOKER_X + index * (SIZE.jokerWidth + 12), JOKER_Y)
    })
  }

  private showTooltip(view: JokerView): void {
    const rarityName = ['', '커먼', '언커먼', '레어', '전설'][view.look.rarity] ?? ''
    this.tooltip.show(view.look.name, rarityName, view.look.rarity, view.look.lines,
      view.x, view.y + SIZE.jokerHeight / 2, SIZE)
  }

  private syncConsumables(): void {
    this.consumableLayer.removeChildren().forEach(child => child.destroy())

    this.state.consumables.forEach((item, index) => {
      const name = this.consumableName(item.kind, item.id)
      const lines = this.consumableLines(item.kind, item.id)

      const tile = new Panel(SIZE.jokerWidth, SIZE.jokerHeight, 0x241d3a)
      tile.position.set(
        CONSUMABLE_X + index * (SIZE.jokerWidth + 12) - SIZE.jokerWidth / 2,
        JOKER_Y - SIZE.jokerHeight / 2)

      const label = new Text({
        text: name,
        style: {
          fontSize: 11, fill: COLOR.ink, align: 'center', fontWeight: '700',
          wordWrap: true, wordWrapWidth: SIZE.jokerWidth - 12,
        },
      })
      label.anchor.set(0.5, 0)
      label.position.set(SIZE.jokerWidth / 2, 14)

      const use = new Text({ text: '눌러서 사용', style: { fontSize: 10, fill: 0xb9a8ff } })
      use.anchor.set(0.5, 1)
      use.position.set(SIZE.jokerWidth / 2, SIZE.jokerHeight - 8)

      tile.addChild(label, use)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => {
        this.audio.play('shop_buy')
        this.particles.burst(tile.x + SIZE.jokerWidth / 2, tile.y + SIZE.jokerHeight / 2,
          14, 0xb9a8ff, 1)
        this.act({ t: 'use_consumable', index, targets: this.orderedSelection() })
      })
      tile.on('pointerover', () => {
        this.tooltip.show(name, '소모품', 0, lines,
          tile.x + SIZE.jokerWidth / 2, tile.y + SIZE.jokerHeight, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.consumableLayer.addChild(tile)
    })
  }

  private consumableName(kind: number, id: string): string {
    if (kind === 1) return this.data.tables.tarot.findByTarotId(id)?.name ?? id
    if (kind === 2) return this.data.tables.planet.findByPlanetId(id)?.name ?? id
    return this.data.tables.spectral.findBySpectralId(id)?.name ?? id
  }

  private consumableLines(kind: number, id: string): string[] {
    if (kind === 1) return describe(this.data, this.data.tarotEffects.get(id) ?? [])
    if (kind === 3) return describe(this.data, this.data.spectralEffects.get(id) ?? [])
    const planet = this.data.tables.planet.findByPlanetId(id)
    return planet ? [`${this.handName(planet.hand)} 레벨 +1`] : []
  }

  private syncShop(): void {
    this.shopLayer.removeChildren().forEach(child => child.destroy())
    this.shopLayer.visible = this.state.phase === 'shop'
    if (!this.shopLayer.visible) return

    const slots = this.state.shop.cards
    const spacing = 172
    const startX = BOARD_X - ((Math.max(1, slots.length) - 1) * spacing) / 2

    slots.forEach((item, slot) => {
      const name = shopLabel(item.kind, item.id, this.data)
      const lines = this.shopLines(item)
      const rarity = item.kind === ShopItemKind.Joker
        ? this.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0

      const tile = new Panel(160, 162, 0x0f2118)
      tile.position.set(startX + slot * spacing - 80, 300)

      const label = new Text({
        text: name,
        style: {
          fontSize: 13, fill: COLOR.ink, fontWeight: '700',
          wordWrap: true, wordWrapWidth: 132,
        },
      })
      label.position.set(12, 12)

      const blurb = new Text({
        text: lines.slice(0, 2).join('\n'),
        style: {
          fontSize: 10, fill: 0xa9c6b2, lineHeight: 13,
          wordWrap: true, wordWrapWidth: 132,
        },
      })
      blurb.position.set(12, 46)

      const kindLabel = new Text({
        text: kindName(item.kind),
        style: {
          fontSize: 10, fontWeight: '700',
          fill: rarity > 0 ? rarityColor(rarity) : 0x9b8fd0,
        },
      })
      kindLabel.position.set(12, 128)

      const price = new Text({
        text: `$${item.cost}`,
        style: {
          fontSize: 18, fontWeight: '800',
          fill: this.state.money >= item.cost ? COLOR.money : 0x7a6a45,
        },
      })
      price.anchor.set(1, 1)
      price.position.set(148, 152)

      tile.addChild(label, blurb, kindLabel, price)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => {
        if (this.state.money < item.cost) return
        this.audio.play('shop_buy')
        this.particles.burst(tile.x + 80, tile.y + 81, 16, COLOR.money, 1)
        this.act({ t: 'buy', slot })
      })
      tile.on('pointerover', () => {
        this.tooltip.show(name, kindName(item.kind), rarity, lines,
          tile.x + 80, tile.y + 162, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.shopLayer.addChild(tile)
    })

    if (this.state.shop.voucher) {
      const id = this.state.shop.voucher
      const row = this.data.tables.voucher.findByVoucherId(id)
      const lines = describe(this.data, this.data.voucherEffects.get(id) ?? [])

      const tile = new Panel(300, 62, 0x132639)
      tile.position.set(BOARD_X - 150, 486)

      const label = new Text({
        text: row?.name ?? '',
        style: { fontSize: 14, fill: COLOR.ink, fontWeight: '700' },
      })
      label.position.set(14, 10)

      const kindLabel = new Text({
        text: '바우처 — 런 내내 남습니다', style: { fontSize: 10, fill: 0x7fb0e0 },
      })
      kindLabel.position.set(14, 34)

      const price = new Text({
        text: `$${this.data.economy.voucherCost}`,
        style: { fontSize: 17, fill: COLOR.money, fontWeight: '800' },
      })
      price.anchor.set(1, 0.5)
      price.position.set(286, 31)

      tile.addChild(label, kindLabel, price)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => this.act({ t: 'buy_voucher' }))
      tile.on('pointerover', () => {
        this.tooltip.show(row?.name ?? '', '바우처', 0, lines, tile.x + 150, tile.y + 62, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.shopLayer.addChild(tile)
    }
  }

  private shopLines(item: { kind: ShopItemKind; id: string }): string[] {
    switch (item.kind) {
      case ShopItemKind.Joker:
        return describe(this.data, this.data.jokerEffects.get(item.id) ?? [])
      case ShopItemKind.Tarot: return this.consumableLines(1, item.id)
      case ShopItemKind.Planet: return this.consumableLines(2, item.id)
      case ShopItemKind.Spectral: return this.consumableLines(3, item.id)
      default: return []
    }
  }
}

function blindName(blind: BlindKind): string {
  return blind === BlindKind.Small ? '스몰' : blind === BlindKind.Big ? '빅' : '보스'
}

function kindName(kind: ShopItemKind): string {
  switch (kind) {
    case ShopItemKind.Joker: return '조커'
    case ShopItemKind.Tarot: return '타로'
    case ShopItemKind.Planet: return '행성'
    case ShopItemKind.Spectral: return '유령'
    default: return '카드'
  }
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
