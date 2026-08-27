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
import { PackKind } from '../generated/enums/pack-kind'
import { PackSize } from '../generated/enums/pack-size'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { evaluate } from '../core/hand'
import { apply, newRun, type Action } from '../core/run'
import { rerollCost } from '../core/shop'
import { bestHand, valueOf } from '../core/suggest'
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
import { Coins } from './coins'
import { Spring } from './motion'
import { Particles } from './particles'
import { artFor, artKindOf, onArtReady, type ArtKind } from './art'
import { drawGlyph, glyphFor, hashOf, hsl, shade, type GlyphName } from './glyph'
import { COLOR, rarityColor, SIZE } from './theme'
import { Button, Panel } from '../ui/widgets'
import { Guide } from '../ui/guide'
import { Toasts } from '../ui/toast'
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
const PLAY_Y = 366
/** 고른 카드가 무슨 족보인지 뜨는 자리. */
const PREVIEW_Y = 466
const HAND_Y = 620
/** 버튼 줄. **패 아래입니다** — 패와 겹치면 카드를 고를 수가 없습니다. */
const BUTTON_Y = 742
/** 상점의 줄들. **팩 줄이 카드 줄과 바우처 사이에 들어갑니다.** */
const SHOP_CARD_Y = 252
const SHOP_PACK_Y = 424
const SHOP_VOUCHER_Y = 508

/** 고른 카드에 도는 빛의 색. 셰이더가 0..1 로 받습니다. */
const PICK_TINT: [number, number, number] = [0.45, 1.0, 0.68]

const DECK_X = SIZE.width - 62
const DECK_Y = 620

export class Game {
  private readonly world = new Container()
  private readonly backdrop = new Container()
  /** 배경을 칠하는 흰 판. 창 크기를 그대로 받습니다. */
  private readonly sheet = new Sprite(Texture.WHITE)
  private readonly board = new Container()
  private readonly overlay = new Container()

  private readonly state: RunState
  private readonly feel: Feel
  private readonly audio: Audio
  private readonly player: TimelinePlayer
  private readonly background = new BackgroundFilter()
  private readonly particles = new Particles()
  private readonly coins = new Coins()
  private readonly punch = new PunchFilter(SIZE.width, SIZE.height)
  private readonly tooltip = new Tooltip()
  /** 무엇이 일어났는지 알리는 줄들. 여러 개가 동시에 뜹니다. */
  private readonly toasts = new Toasts()

  private readonly cards = new Map<number, CardView>()
  private readonly playedViews: CardView[] = []
  /** 아직 날아가지 않은 카드들. 왼쪽부터 한 장씩 차례로 갑니다. */
  private readonly slams: { view: CardView; x: number; at: number }[] = []
  private readonly jokers = new Map<number, JokerView>()
  private readonly selected = new Set<number>()
  /** 도움. 이것도 고르면 더 높은 족보가 되는 카드들입니다. */
  private readonly hinted = new Set<number>()

  private readonly badge = new BlindBadge(PANEL_W)
  private readonly score = new Slot('라운드 점수', PANEL_W, 68, COLOR.ink)
  private readonly chips = new Slot('칩', 118, 56, COLOR.chips)
  private readonly mult = new Slot('배수', 118, 56, COLOR.mult)
  private readonly hands = new Slot('핸드', 124, 52, COLOR.good)
  private readonly discards = new Slot('버리기', 124, 52, 0xff9d5c)
  private readonly money = new Slot('금액', 124, 52, COLOR.money)
  private readonly anteSlot = new Slot('안테', 124, 52, COLOR.ink)

  private readonly headline = new Text({
    text: '',
    style: {
      fontSize: 34, fill: COLOR.ink, fontWeight: '800',
      stroke: { color: 0x0a0f18, width: 5 },
    },
  })
  private readonly gauge = new Graphics()
  private readonly frames = new Graphics()
  /** 덱 더미. 상점에서는 화면 밖으로 밀려 나갑니다. */
  private readonly deckLayer = new Container()
  private readonly deckSlide = new Spring(0, 150, 20)
  /** 패널 위에 얹는 빛. `panelGlow` 가 세기입니다. */
  private readonly panelFlash = new Graphics()
  /** 화면 전체에 얹는 빛. */
  private readonly screenFlash = new Graphics()
  /** 몇 장 골랐는가. **다섯 칸이 채워지는 것이 보여야 몇 장 더인지 압니다.** */
  private readonly pips = new Graphics()
  /** 조커 줄이 비었을 때의 안내. */
  private readonly jokerHint = new Text({
    text: '상점에서 조커를 사서 이 자리에 세웁니다',
    style: { fontSize: 11, fill: 0x74829a, fontWeight: '700' },
  })
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
  /** 고른 것을 한 번에 풉니다. **한 장씩 다시 누르는 것은 일입니다.** */
  private readonly clearButton: Button
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
      fontSize: 13, fill: 0xb4c4dc, fontWeight: '700',
      align: 'center',
      stroke: { color: 0x0a0f18, width: 3 },
    },
  })
  private readonly shopLayer = new Container()
  /** 뜯어 놓은 팩. 상점 위를 덮습니다. */
  private readonly packLayer = new Container()
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
  /** 머리글이 얼마나 남았는가. **계속 떠 있으면 지난 일이 지금 일처럼 보입니다.** */
  private headlineLife = 0
  private headlineSpan = 1
  /** 예약해 둔 소리. 음이 하나씩 올라가는 아르페지오를 이것으로 냅니다. */
  private readonly chimes: { at: number; cue: string; semitones: number }[] = []
  private wasBusy = false
  private gameOverShown = false
  private gameOverPop = 0
  private gameOverBoard?: Container

  private shake = 0
  /**
   * 히트스톱. **때린 순간 시간이 잠깐 멈추면 그 한 방이 무거워집니다.**
   *
   * 멈추는 것은 연출의 시계뿐입니다 — 용수철과 파티클은 계속 움직여야 화면이 얼어붙은 것처럼
   * 보이지 않습니다.
   */
  private freeze = 0
  /** 이번 득점에서 몇 번째 사건인가. 소리의 음과 세기가 이것으로 올라갑니다. */
  private chain = 0
  /** 왼쪽 패널의 번쩍임. 숫자가 바뀌는 자리를 파티클 대신 이것이 알립니다. */
  private panelGlow = 0
  private panelTint: number = COLOR.ink
  /** 화면 전체의 번쩍임. 큰 것에만 씁니다. */
  private screenGlow = 0
  private panelDrawn = false
  private screenDrawn = false
  private screenTint: number = COLOR.ink
  /** 점수가 멈춘 뒤 낸 카드를 얼마나 붙잡아 두었는가. */
  private holdAfterScore = 0
  private liveChips = 0
  private liveMult = 10_000
  private clock = 0
  private pointerAt = { x: 0, y: 0 }
  /** 지금 설명이 떠 있는 조커. 바뀔 때만 다시 그립니다. */
  private hoveredJoker?: JokerView

  constructor(private readonly app: Application, private readonly data: Data, seed: string) {
    this.feel = readFeel(data.feel)
    this.audio = new Audio(data.tables)
    this.state = newRun(data, seed, 'red_deck', 'White').state
    this.player = new TimelinePlayer(beat => this.showBeat(beat))

    // 배경은 흰 스프라이트 한 장에 셰이더를 얹은 것입니다.
    this.sheet.filters = [this.background]
    this.backdrop.addChild(this.sheet)

    // **배경은 세계 밖에 있습니다.** 판은 기준 해상도에 맞춰 가운데에 놓이므로 창의 비율이
    // 다르면 옆이나 아래가 남습니다 — 그 자리를 검정으로 두지 않고 배경이 창 전체를 덮습니다.
    app.stage.addChild(this.backdrop, this.world)
    this.world.addChild(this.board, this.particles, this.overlay,
      this.coins, this.toasts, this.screenFlash, this.tooltip)

    // 동전이 꽂힐 때마다 금액 칸이 튀고 음이 하나 올라갑니다.
    this.coins.onLand = (index, gain) => {
      this.audio.play(gain ? 'coin_land' : 'coin_lose', index * 2)
      this.money.target = this.state.money
      if (gain) this.flashPanel(COLOR.money, 0.5)
    }
    this.board.sortableChildren = true

    this.buildPanel()

    this.playButton = new Button('낸다', 128, 46, 0x2f6fb5, () => this.play())
    this.discardButton = new Button('버린다', 128, 46, 0xa63f3f, () => this.discard())
    this.clearButton = new Button('✕', 44, 46, 0x4a5568, () => this.clearSelection())
    this.primaryButton = new Button('이 블라인드로', 210, 50, 0x2f6fb5, () => this.primary())
    this.skipButton = new Button('건너뛴다', 150, 38, 0x4a5568,
      () => this.act({ t: 'skip_blind' }))
    this.rerollButton = new Button('리롤', 128, 44, 0x3f5f8f, () => this.reroll())
    this.sortRankButton = new Button('랭크순', 92, 32, 0x333e4e, () => this.sortHand('rank'))
    this.sortSuitButton = new Button('무늬순', 92, 32, 0x333e4e, () => this.sortHand('suit'))
    this.infoButton = new Button('족보 목록', 118, 34, 0x3a4658, () => this.toggleHandList())
    this.guideButton = new Button('게임 방법', 118, 34, 0x3a4658, () => this.guide.open())

    this.overlay.addChild(this.playButton, this.discardButton, this.primaryButton,
      this.clearButton, this.skipButton, this.rerollButton, this.shopLayer, this.packLayer,
      this.sortRankButton, this.sortSuitButton, this.infoButton, this.guideButton,
      this.preview, this.handList, this.gameOver, this.guide)

    this.preview.addChild(this.previewPlate, this.previewHand, this.previewValue)
    this.preview.visible = false
    this.handList.visible = false
    this.gameOver.visible = false
    // 낸다 · 취소 · 버린다. **취소가 가운데인 것이 맞습니다** — 둘 중 어느 쪽으로도
    // 가기 전에 되돌리는 것이기 때문입니다.
    this.playButton.position.set(BOARD_X - 176, BUTTON_Y)
    this.clearButton.position.set(BOARD_X - 22, BUTTON_Y)
    this.discardButton.position.set(BOARD_X + 48, BUTTON_Y)
    this.primaryButton.position.set(BOARD_X - 105, 520)
    this.skipButton.position.set(BOARD_X - 75, 586)
    this.rerollButton.position.set(BOARD_X - 64, 578)
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

    // 그림이 새로 들어오면 다시 그립니다. 문양이 그림으로 바뀝니다.
    onArtReady(() => this.refresh())

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
    const panel = new Panel(PANEL_W + 24, SIZE.height - 44, 0x141b26)
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

    // 가운데에서 커집니다. 위쪽을 붙잡고 키우면 글씨가 아래로 자라 보입니다.
    this.headline.anchor.set(0.5, 0.5)
    this.headline.position.set(BOARD_X, 214)

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
        .stroke({ color: COLOR.cardBackEdge, width: 1.5 })
    }

    // **지시문은 누를 버튼 바로 위입니다.** 패널 아래에 두면 눈이 화면 왼쪽 끝까지 갔다
    // 와야 하고, 정작 누를 것은 가운데에 있습니다.
    this.hint.anchor.set(0.5, 0.5)
    this.hint.position.set(BOARD_X, BUTTON_Y - 30)

    this.jokerHint.anchor.set(0.5, 0)
    this.jokerHint.position.set(JOKER_X + (SIZE.jokerWidth + 12) * 2,
      JOKER_Y + SIZE.jokerHeight / 2 + 10)

    // **덱은 판이 도는 동안만 화면에 있습니다.** 상점에서는 오른쪽으로 밀려 나가고,
    // 다음 블라인드로 가면 다시 들어옵니다 — 상점의 물건과 자리를 다투지 않습니다.
    this.deckLayer.addChild(pile, this.deckLabel)

    this.board.addChild(this.deckLayer, this.headline, this.gauge, this.jokerCount,
      this.consumableCount, this.consumableLayer, this.hint,
      this.jokerHint, this.pips, this.panelFlash)
  }

  /** 조커와 소모품의 빈 자리. **비어 있어도 자리가 보여야 무엇을 모으는 게임인지 압니다.** */
  private drawFrames(): void {
    const g = this.frames
    g.clear()

    for (let i = 0; i < this.state.rules.jokerSlots; i++) {
      const x = JOKER_X + i * (SIZE.jokerWidth + 12)
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .fill({ color: 0x161d29, alpha: 0.6 })
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .stroke({ color: COLOR.panelEdge, width: 1.5, alpha: 0.8 })
    }

    for (let i = 0; i < this.state.rules.consumableSlots; i++) {
      const x = CONSUMABLE_X + i * (SIZE.jokerWidth + 12)
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .fill({ color: 0x1d1a2c, alpha: 0.6 })
      g.roundRect(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
        SIZE.jokerWidth, SIZE.jokerHeight, 9)
        .stroke({ color: 0x5a4d80, width: 1.5, alpha: 0.9 })
    }
  }

  layout(width: number, height: number): void {
    const scale = Math.min(width / SIZE.width, height / SIZE.height)
    this.world.scale.set(scale)
    // 자리를 정수로 맞춥니다. 반 픽셀이 남으면 글씨가 흐려집니다.
    this.world.position.set(
      Math.round((width - SIZE.width * scale) / 2),
      Math.round((height - SIZE.height * scale) / 2))

    this.sheet.width = width
    this.sheet.height = height
    this.background.setAspect(width / Math.max(1, height))
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
    this.announce(step.events)
    this.refresh()
    this.startTimeline(step.events)
  }

  /**
   * 무엇이 일어났는지 글로 알립니다.
   *
   * **소모품은 결과가 화면 여러 곳에 흩어집니다** — 카드가 바뀌고 족보 레벨이 오르고 조커가
   * 사라지는데, 그것들이 각자의 자리에서 조용히 바뀌면 무엇을 쓴 것인지 남지 않습니다.
   *
   * 같은 갈래는 묶어서 한 줄로 냅니다. 카드 5장이 바뀌었다고 토스트가 5개 뜨면 읽을 수
   * 없습니다.
   */
  private announce(events: readonly GameEvent[]): void {
    let modified = 0
    let destroyed = 0
    let added = 0

    for (const event of events) {
      switch (event.t) {
        case 'ConsumableUsed': {
          const kind = this.state.consumables.find(item => item.id === event.id)?.kind
          const name = this.consumableName(kind ?? 1, event.id)
          this.toasts.push(`${name} 사용`,
            this.consumableLines(kind ?? 1, event.id).join(' · ') || '효과가 적용되었습니다',
            0xb9a8ff, 3)
          break
        }

        case 'HandLevelled':
          this.toasts.push(`${this.handName(event.hand)}  레벨 ${event.level}`,
            '이 족보의 칩과 배수가 올랐습니다', COLOR.chips, 2.8)
          break

        case 'JokerDestroyed': {
          const row = this.data.tables.joker.findByJokerId(event.jokerId)
          this.toasts.push(`${row?.name ?? event.jokerId} 파괴`, '조커 슬롯이 비었습니다',
            COLOR.bad, 2.6)
          break
        }

        case 'RuleChanged':
          this.toasts.push('규칙이 바뀌었습니다', ruleName(event.rule), COLOR.money, 2.6)
          break

        case 'CardModified': modified++; break
        case 'CardDestroyed': destroyed++; break
        case 'CardAdded': added++; break
        default: break
      }
    }

    if (modified > 0) {
      this.toasts.push(`카드 ${modified}장이 바뀌었습니다`, '덱의 카드가 달라졌습니다',
        COLOR.good, 2.4)
    }
    if (destroyed > 0) {
      this.toasts.push(`카드 ${destroyed}장이 사라졌습니다`, '덱에서 빠졌습니다', COLOR.bad, 2.4)
    }
    if (added > 0) {
      this.toasts.push(`카드 ${added}장이 들어왔습니다`, '덱에 더해졌습니다', COLOR.good, 2.4)
    }
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
    // **카드가 다 자리에 붙기 전에는 득점을 시작하지 않습니다.** 히트스톱이 연출의 시계를
    // 잡아 주므로 여기서 그 시간만큼 멈추면 됩니다.
    this.stop(cards.length * 85 + 140)
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
  private clearSelection(): void {
    if (this.selected.size === 0 || this.player.busy) return
    this.selected.clear()
    this.audio.play('card_select', -6)
    this.refresh()
  }

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

  /**
   * 낸 카드를 판으로 올립니다.
   *
   * **한꺼번에 움직이지 않습니다.** 왼쪽부터 한 장씩 차례로, 빠르게 가서 자리에 달라붙습니다 —
   * 다섯 장이 같이 미끄러지면 무엇을 냈는지가 한 덩어리로 보이고, 하나씩 「짝」 붙으면
   * 다섯 번의 사건이 됩니다.
   */
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
      view.setPick(0, PICK_TINT)
      view.hint = false
      view.idle = 0.4
      view.zIndex = 100 + index
      this.slams.push({
        view, x: startX + index * spacing, at: this.clock + index * 0.085,
      })
    })
  }

  /** 예약해 둔 한 장씩의 이동. */
  private advanceSlams(): void {
    while (this.slams.length > 0 && this.slams[0].at <= this.clock) {
      const next = this.slams.shift()
      if (!next) break
      next.view.slam(next.x, PLAY_Y)
      this.audio.play('card_slam')
      this.jolt(2.2, 0.35)
    }
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
        this.say(`${this.handName(event.hand)}   레벨 ${event.level}`, COLOR.ink, 3, 0.35)
        this.audio.play('score_count', semitones)
        break

      case 'CardScored': {
        this.liveChips += event.chips
        const view = this.viewOf(event.uid)
        // 카드가 차례로 득점할수록 세집니다. **뒤로 갈수록 커지는 것이 기대를 만듭니다.**
        const step = Math.min(1, this.chain / 5)
        this.chain++
        if (view) {
          view.pop(0.8 + beat.intensity + step * 0.5)
          this.particles.burst(view.x, view.y - 30, 14 + dust * 3, COLOR.chips,
            1.1 + beat.intensity + step)
        }
        this.popAt(view, `+${event.chips}`, COLOR.chips, beat.intensity + step * 0.4)
        this.audio.play('card_chip', semitones + this.chain * 2)
        this.jolt(3 + beat.intensity * 5 + step * 4, 0.6 + beat.intensity + step,
          0.16 + step * 0.16)
        this.flashPanel(COLOR.chips, 0.4 + step * 0.3)
        this.stop(28 + step * 26)
        break
      }

      case 'JokerTriggered': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        const mul = event.op === 'MulMult'
        const money = event.op === 'AddMoney'
        const cue = mul ? 'joker_mul' : money ? 'joker_money' : 'joker_add'
        const text = mul ? `×${(event.mult / 10_000).toFixed(2)}`
          : event.chips !== 0 ? `+${event.chips}`
            : event.mult !== 0 ? `+${(event.mult / 10_000).toFixed(0)}`
              : `$${event.money}`
        const tint = mul || event.chips === 0 ? COLOR.mult : COLOR.chips

        this.chain++
        if (view) {
          view.pop(mul ? 1.6 : 1.1)
          this.particles.burst(view.x, view.y, 18 + dust * 3,
            mul ? COLOR.mult : event.chips !== 0 ? COLOR.chips : COLOR.money,
            1.4 + (mul ? 0.8 : 0))
        }
        this.popAt(view, text, tint, beat.intensity + (mul ? 0.6 : 0.2))
        this.audio.play(cue, semitones + this.chain)

        // **배수를 곱하는 것이 이 게임에서 가장 큰 사건입니다.** 그 하나만 크게 다룹니다.
        if (mul) {
          this.jolt(12 + beat.intensity * 10, 2 + beat.intensity * 2, 0.62)
          this.flashScreen(COLOR.mult, 0.2 + beat.intensity * 0.16)
          this.flashPanel(COLOR.mult, 1)
          this.stop(120)
        } else {
          this.jolt(5 + beat.intensity * 6, 0.8 + beat.intensity, 0.24)
          this.flashPanel(tint, 0.6)
          this.stop(48)
        }

        if (money && event.money !== 0 && view) {
          this.coins.fly(event.money, { x: view.x, y: view.y }, this.moneySpot())
        }
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
        this.chain++
        if (view) {
          view.pop(1.2)
          this.particles.burst(view.x, view.y, 12, COLOR.good, 1.2)
        }
        this.popAt(view, '다시', COLOR.good, beat.intensity + 0.3)
        this.audio.play('retrigger', semitones + this.chain * 2)
        this.jolt(5, 0.9, 0.2)
        this.flashPanel(COLOR.good, 0.5)
        this.stop(40)
        break
      }

      case 'MoneyChanged': {
        if (event.delta === 0) break
        const spot = this.moneySpot()
        const from = this.state.phase === 'shop' && event.reason === 'shop'
          ? { x: BOARD_X, y: SHOP_CARD_Y + 81 }
          : { x: BOARD_X, y: PLAY_Y }
        this.coins.fly(event.delta, from, spot)
        this.flashPanel(event.delta > 0 ? COLOR.money : COLOR.bad, 0.7)
        this.audio.play(event.delta > 0 ? 'joker_money' : 'shop_reroll')
        if (event.delta > 0) this.jolt(3, 0.6, 0.18)

        // **무엇으로 번 돈인가**를 적습니다. 합계만 굴러가면 이유를 알 수 없습니다.
        const why = moneyReason(event.reason)
        if (why) {
          this.popAt(this.moneyLabelAnchor(), `${why}  ${event.delta > 0 ? '+' : ''}$${event.delta}`,
            event.delta > 0 ? COLOR.money : COLOR.bad, 0.3)
        }
        break
      }

      case 'ScoreResolved':
        this.score.target = Number(event.score)
        this.audio.play('score_settle', semitones)

        // **마지막 한 방이 앞의 것들보다 확실히 커야 합니다.** 그것이 없으면 득점이
        // 어디서 끝났는지 읽히지 않습니다.
        this.jolt(14 + shakeOf(beat.intensity, this.feel), 2.4 + beat.intensity * 2, 0.9)
        this.flashScreen(COLOR.ink, 0.26 + beat.intensity * 0.2)
        this.flashPanel(COLOR.ink, 1)
        this.stop(150)

        // 낸 카드가 멈춘 자리에서 크게 터집니다.
        this.burstAcrossPlayArea(26 + dust * 4, COLOR.mult, 1.8 + beat.intensity)
        this.chain = 0
        break

      case 'BlindCleared':
        // **이 게임에서 사람이 기다리는 순간입니다.** 채널을 전부 씁니다 — 큰 글씨가 튀어
        // 나오고, 화면이 번쩍이고, 판이 흔들리고, 배경이 밝아지고, 음이 여섯 번 올라갑니다.
        this.say('넘겼습니다', COLOR.good, 2.2)
        this.audio.play('blind_clear')
        this.chime('coin_land', 6, 3, 0.07)
        this.burstAcrossPlayArea(46, COLOR.good, 2.4, 2.6)
        this.particles.burst(BOARD_X, PLAY_Y - 60, 70, COLOR.money, 2.6, 2.8)
        this.particles.burst(BOARD_X, 210, 44, COLOR.good, 2.2, 2.4)
        this.jolt(26, 4.2, 1)
        this.flashScreen(COLOR.good, 0.46)
        this.stop(280)
        this.liveChips = 0
        this.liveMult = 10_000
        this.chain = 0
        break

      case 'RunLost':
        this.say('점수가 모자랍니다', COLOR.bad, 2)
        this.audio.play('blind_fail')
        this.jolt(9, 1.6, 0.5)
        this.flashScreen(COLOR.bad, 0.2)
        this.stop(160)
        break

      case 'RunWon':
        this.say('전부 넘겼습니다', COLOR.money, 2.8)
        this.chime('coin_land', 10, 2, 0.06)
        this.audio.play('blind_clear')
        this.particles.burst(BOARD_X, SIZE.height / 2, 120, COLOR.money, 2.6)
        this.jolt(22, 3.4, 1)
        this.flashScreen(COLOR.money, 0.44)
        this.stop(220)
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
  /**
   * 패널을 번쩍입니다.
   *
   * **왼쪽의 숫자들이 바뀌는 자리를 파티클로 알리면 숫자를 가립니다.** 패널 자체가 빛나면
   * 무엇이 바뀌었는지가 가려지지 않고 눈에 들어옵니다.
   */
  /** 금액이 왜 바뀌었는지를 띄울 자리. 판 가운데입니다. */
  private moneyLabelAnchor(): Container {
    const anchor = new Container()
    // 낸 카드 아래입니다. **카드 위에 겹치면 흰 종이에 흰 글씨가 됩니다.**
    anchor.position.set(BOARD_X, PLAY_Y + 200)
    return anchor
  }

  /** 금액 칸의 가운데. 동전이 여기로 꽂힙니다. */
  private moneySpot(): { x: number; y: number } {
    return { x: this.money.x + 62, y: this.money.y + 26 }
  }

  /** 낸 카드가 늘어선 폭 전체에서 터뜨립니다. 한 점에서 터지면 찔끔 나온 것으로 보입니다. */
  private burstAcrossPlayArea(perCard: number, tint: number, power: number,
                              linger = 1.9): void {
    if (this.playedViews.length === 0) {
      this.particles.burst(BOARD_X, PLAY_Y, perCard * 3, tint, power, linger)
      return
    }
    for (const view of this.playedViews) {
      this.particles.burst(view.x, view.y, perCard, tint, power, linger)
      this.particles.burst(view.x, view.y - 40, Math.round(perCard * 0.6),
        COLOR.chips, power * 0.8, linger)
    }
  }

  private flashPanel(tint: number, strength: number): void {
    this.panelTint = tint
    this.panelGlow = Math.min(1, Math.max(this.panelGlow, strength))
  }

  /** 화면 전체를 번쩍입니다. **큰 것에만 씁니다** — 잦으면 눈이 아픕니다. */
  private flashScreen(tint: number, strength: number): void {
    this.screenTint = tint
    this.screenGlow = Math.min(0.72, Math.max(this.screenGlow, strength))
  }

  /** 때린 순간 연출의 시계를 잠깐 멈춥니다. 그 한 방이 무거워집니다. */
  private stop(ms: number): void {
    this.freeze = Math.max(this.freeze, ms)
  }

  /**
   * 한 방.
   *
   * **채널을 한꺼번에 씁니다** — 흔들림 · 색수차 · 배경의 번쩍임. 하나만 쓰면 「움직였다」로
   * 읽히고, 셋이 같이 오면 「맞았다」로 읽힙니다.
   */
  /**
   * 연출이 다 끝났는가.
   *
   * **국면이 바뀌었어도 앞 국면의 연출이 돌고 있으면 화면을 갈지 않습니다.** 낸 카드가 아직
   * 판에 있는데 상점이 그 위에 그려지면 무엇을 보고 있는지 알 수 없습니다.
   */
  /**
   * 화면 가운데에 한 줄.
   *
   * **머리글은 사라져야 합니다.** 「넘겼습니다」가 상점에 가도 떠 있으면 지난 일이 지금 일처럼
   * 보입니다. 뜰 때 크게 튀었다가 잦아들고, 정해진 시간이 지나면 없어집니다.
   */
  private say(text: string, tint: number, seconds: number, pop = 1): void {
    this.headline.text = text
    this.headline.style.fill = tint
    this.headlineLife = seconds
    this.headlineSpan = seconds
    this.headline.visible = true
    this.headline.alpha = 1
    this.headline.scale.set(0.3 + 0.35 * (1 - pop))
  }

  private advanceHeadline(seconds: number): void {
    if (this.headlineLife <= 0) {
      if (this.headline.visible) this.headline.visible = false
      return
    }

    this.headlineLife = Math.max(0, this.headlineLife - seconds)
    const gone = 1 - this.headlineLife / this.headlineSpan

    // 처음 한 순간은 튀어나오는 구간입니다. 1을 넘겼다가 돌아옵니다.
    const grow = gone < 0.08
      ? 0.3 + 1.05 * (gone / 0.08)
      : 1.35 - 0.35 * Math.min(1, (gone - 0.08) / 0.14)
    const shiver = Math.max(0, 1 - gone / 0.3)
    const jitter = shiver * shiver

    this.headline.scale.set(grow)
    this.headline.position.set(
      BOARD_X + (Math.random() - 0.5) * 18 * jitter,
      214 + (Math.random() - 0.5) * 13 * jitter)
    this.headline.rotation = (Math.random() - 0.5) * 0.055 * jitter
    // 마지막 구간에서 사라집니다.
    this.headline.alpha = Math.min(1, (1 - gone) / 0.35)
  }

  /** 음이 하나씩 올라가는 소리 여러 개. **오르는 음이 「해냈다」로 읽힙니다.** */
  private chime(cue: string, count: number, step = 3, gap = 0.075): void {
    for (let i = 0; i < count; i++) {
      this.chimes.push({ at: this.clock + i * gap, cue, semitones: i * step })
    }
  }

  private advanceChimes(): void {
    while (this.chimes.length > 0 && this.chimes[0].at <= this.clock) {
      const next = this.chimes.shift()
      if (next) this.audio.play(next.cue, next.semitones)
    }
  }

  private get presented(): boolean {
    return !this.player.busy && this.playedViews.length === 0 && !this.coins.busy
  }

  private jolt(shake: number, chroma: number, pulse = 0): void {
    this.shake = Math.max(this.shake, shake)
    this.punch.hit(Math.min(chroma, this.feel.chromaticMaxPx * 2))
    if (pulse > 0) this.background.pulse(pulse)
  }

  private popAt(target: Container | undefined, text: string, tint: number,
                intensity: number): void {
    const label = new Text({
      text,
      style: {
        fontSize: 20 + intensity * 16, fill: tint, fontWeight: '800',
        stroke: { color: 0x0a0f18, width: 4 },
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

    // **히트스톱.** 연출의 시계만 멈춥니다 — 용수철과 파티클은 계속 움직여야 화면이
    // 얼어붙은 것으로 보이지 않습니다.
    if (this.freeze > 0) this.freeze = Math.max(0, this.freeze - deltaMs)
    else this.player.advance(deltaMs)
    this.publishPeek()

    this.coins.advance(seconds)
    this.toasts.advance(seconds)
    this.decayFlashes(seconds)

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

    this.updateHover()

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

    // 연출이 끝난 순간에 한 번 다시 그립니다. **그때가 다음 국면의 화면을 띄울 때입니다.**
    const busyNow = !this.presented
    if (this.wasBusy && !busyNow) this.refresh()
    this.wasBusy = busyNow

    this.advanceHeadline(seconds)
    this.advanceChimes()
    this.advanceSlams()

    // 덱은 판이 도는 동안만 자리에 있습니다.
    // **판이 도는 동안만 자리에 있습니다.** 블라인드를 고르는 중에도 아직 없습니다 —
    // 시작을 누르면 오른쪽에서 들어옵니다.
    const away = this.state.phase !== 'round'
    this.deckSlide.target = away ? 300 : 0
    this.deckSlide.advance(seconds)
    this.deckLayer.x = this.deckSlide.value
    this.deckLayer.visible = this.deckSlide.value < 296
    this.advanceGameOver(seconds)

    // **끝났다는 판은 연출이 다 끝난 뒤에 띄웁니다.** 마지막 카드의 결과를 보기 전에 덮이면
    // 무엇 때문에 끝난 것인지 알 수 없습니다.
    const finished = this.state.phase === 'lost' || this.state.phase === 'won'
    if (finished && !this.gameOverShown
        && !this.player.busy && this.score.settled && !this.coins.busy) {
      this.drawGameOver()
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
        if (this.holdAfterScore > 1_100 && !this.playedViews[0].retiring) {
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

  /** 번쩍임은 줄어듭니다. 패널은 빠르게, 화면은 더 빠르게 — 오래 남으면 눈이 아픕니다. */
  private decayFlashes(seconds: number): void {
    if (this.panelGlow > 0.002) {
      this.panelGlow = Math.max(0, this.panelGlow - seconds * 3.6)
      const ease = this.panelGlow * this.panelGlow
      this.panelFlash.clear()
      this.panelFlash.roundRect(LEFT - 12, 22, PANEL_W + 24, SIZE.height - 44, 12)
        .fill({ color: this.panelTint, alpha: ease * 0.3 })
      this.panelFlash.roundRect(LEFT - 11, 23, PANEL_W + 22, SIZE.height - 46, 11)
        .stroke({ color: this.panelTint, width: 1 + ease * 4, alpha: ease })
      this.panelDrawn = true
    } else if (this.panelDrawn) {
      this.panelFlash.clear()
      this.panelDrawn = false
    }

    if (this.screenGlow > 0.002) {
      this.screenGlow = Math.max(0, this.screenGlow - seconds * 4.4)
      this.screenFlash.clear()
      this.screenFlash.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
        .fill({ color: this.screenTint, alpha: this.screenGlow * this.screenGlow })
      this.screenDrawn = true
    } else if (this.screenDrawn) {
      this.screenFlash.clear()
      this.screenDrawn = false
    }
  }

  /** 배경이 얼마나 뜨거운가. 점수가 요구에 가까울수록 올라갑니다. */
  private heat(): number {
    if (this.state.phase === 'shop') return 0.15
    if (this.state.target <= 0) return 0.1
    return Math.max(0.08, Math.min(1, Number(this.state.score) / Number(this.state.target)))
  }

  /**
   * 마우스가 무엇 위에 있는가.
   *
   * **올라간 자리가 아니라 쉬는 자리로 판정합니다.** 마우스가 올라오면 카드가 위로
   * 들리는데, 들린 카드로 판정하면 카드가 마우스 밑에서 빠져나가 곧바로 내려오고, 내려오면
   * 다시 들립니다 — 카드 아래쪽 몇 픽셀에서 카드가 떨던 것이 그것입니다.
   *
   * 겹쳐 있을 때는 오른쪽 것이 위입니다. 손패를 그리는 순서가 그렇습니다.
   */
  private updateHover(): void {
    const blocked = this.guide.visible || this.state.pack !== null
      || this.state.phase === 'lost' || this.state.phase === 'won'

    let card: CardView | undefined
    let joker: JokerView | undefined

    if (!blocked) {
      for (const view of this.cards.values()) {
        if (!near(this.pointerAt, view.motion, SIZE.cardWidth, SIZE.cardHeight)) continue
        if (!card || view.motion.x.target > card.motion.x.target) card = view
      }
      for (const view of this.jokers.values()) {
        if (!near(this.pointerAt, view.motion, SIZE.jokerWidth, SIZE.jokerHeight)) continue
        if (!joker || view.motion.x.target > joker.motion.x.target) joker = view
      }
    }

    for (const view of this.cards.values()) view.hovered = view === card
    for (const view of this.jokers.values()) view.hovered = view === joker

    if (joker) {
      if (joker !== this.hoveredJoker) this.showTooltip(joker)
    } else if (this.hoveredJoker) {
      this.tooltip.hide()
    }
    this.hoveredJoker = joker
  }

  private tiltFor(view: Container): number {
    return Math.max(-1, Math.min(1, (this.pointerAt.x - view.x) / 90))
  }

  private drawGauge(): void {
    const x = BOARD_X - 220
    const y = 240
    const width = 440
    const height = 12

    this.gauge.clear()
    const ratio = this.state.target > 0
      ? Number(this.state.score) / Number(this.state.target) : 0

    this.gauge.roundRect(x, y, width, height, 6).fill(0x101724)
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
      packOpen: state.pack !== null, packs: state.shop.packs.length,
      played: this.playedViews.length, coins: this.coins.busy,
      cleared: this.headline.visible && this.headline.text === '넘겼습니다',
      consumables: state.consumables.length,
      busy: this.player.busy || !this.score.settled || this.coins.busy,
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

    this.updateHints()

    this.syncBadge()
    this.drawFrames()
    this.syncCards()
    this.syncJokers()
    this.syncConsumables()
    this.syncShop()
    this.syncPack()
    this.syncButtons()
    this.syncMood()
    this.drawPreview()
    this.drawHandList()
    // 다시 시작하면 판을 걷습니다. **띄우는 것은 `tick` 이 합니다** — 연출이 끝난 뒤여야
    // 하기 때문입니다.
    if (this.state.phase !== 'lost' && this.state.phase !== 'won') this.drawGameOver()
    this.hint.text = this.hintText()
    this.jokerHint.visible = this.state.jokers.length === 0
      && (this.state.phase === 'round' || this.state.phase === 'blind-select')
    this.drawPips()
    this.sharpen(this.world.scale.x)
  }

  /**
   * 도움을 다시 셉니다.
   *
   * **패에서 가장 값이 높은 조합을 찾아, 그 조합에 들어가는데 아직 고르지 않은 카드를
   * 표시합니다.** 지금 고른 것이 이미 그만큼 값이 나오면 아무것도 표시하지 않습니다 —
   * 잘 고른 사람에게 계속 권하면 방해입니다.
   *
   * 조커를 세지 않으므로 「더 높은 족보」이지 「더 높은 점수」가 아닙니다. 조커가 붙으면
   * 사람의 판단이 더 나을 수 있고, 그때 이 표시는 무시하면 됩니다.
   */
  private updateHints(): void {
    this.hinted.clear()
    if (this.state.phase !== 'round') return

    const held = this.state.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)
    if (held.length === 0) return

    const best = bestHand(this.data, this.state, held)
    if (!best) return

    const picked = held.filter(card => this.selected.has(card.uid))
    const now = picked.length > 0 ? valueOf(this.data, this.state, picked) : undefined
    if (now && now.value >= best.value) return

    for (const card of best.cards) {
      if (!this.selected.has(card.uid)) this.hinted.add(card.uid)
    }
    // 고른 것 전부가 최선의 조합에 들어 있지 않으면 권할 것이 없습니다 — 지금 고른 것을
    // 풀어야 하는 상황이므로 카드 표시로는 알릴 수 없습니다.
    const inBest = new Set(best.cards.map(card => card.uid))
    if (picked.some(card => !inBest.has(card.uid))) this.hinted.clear()
  }

  /**
   * 몇 장 골랐는가.
   *
   * **「최대 5장」 이라고 적어 두는 것으로는 부족합니다** — 칸 다섯이 채워지는 것이 보여야
   * 몇 장 더 고를 수 있는지가 세지 않고 읽힙니다.
   */
  private drawPips(): void {
    const g = this.pips
    g.clear()
    g.visible = this.state.phase === 'round'
    if (!g.visible) return

    const max = this.data.run.maxPlayedCards
    const filled = this.selected.size
    const gap = 26
    // 버튼 줄의 오른쪽입니다. **손패 위에 두면 족보 미리보기가 덮습니다.**
    const startX = BOARD_X + 196
    const y = BUTTON_Y + 23

    for (let i = 0; i < max; i++) {
      const x = startX + i * gap
      const on = i < filled
      g.roundRect(x - 9, y - 5, 18, 10, 5)
        .fill({ color: on ? COLOR.good : 0x1c2431, alpha: on ? 1 : 0.9 })
      g.roundRect(x - 9, y - 5, 18, 10, 5)
        .stroke({ color: on ? 0xcdf5de : 0x4a5666, width: on ? 2 : 1.4 })
    }
  }

  /** 지금 국면에서 다음에 할 것. */
  private hintText(): string {
    const state = this.state
    switch (state.phase) {
      case 'blind-select':
        return state.blind === BlindKind.Boss
          ? '보스 블라인드입니다. 효과를 확인하고 시작하십시오'
          : '「이 블라인드로」 시작 · 「건너뛴다」 는 태그를 받고 넘깁니다'
      case 'round':
        if (this.selected.size === 0) {
          return this.hinted.size > 0
            ? `테두리가 도는 ${this.hinted.size}장이 지금 가장 높은 족보입니다`
              + `   ·   남은 핸드 ${state.handsLeft}회`
            : `카드를 눌러 고릅니다 — 최대 ${this.data.run.maxPlayedCards}장`
              + `   ·   남은 핸드 ${state.handsLeft}회`
        }
        return `${this.selected.size}장 골랐습니다   ·   「낸다」 로 점수를 내거나`
          + ' 「버린다」 로 바꿉니다'
      case 'shop':
        if (state.pack) {
          return `펼쳐진 것 중에서 ${state.pack.picksLeft}장을 고릅니다`
            + '   ·   마음에 안 들면 건너뜁니다'
        }
        return state.jokers.length === 0
          ? '조커를 눌러 삽니다   ·   조커가 없으면 점수가 좀처럼 오르지 않습니다'
          : '살 것을 누릅니다   ·   끝나면 「다음 블라인드로」'
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

    const hinting = picked.length === 0 && this.hinted.size > 0
    this.preview.visible = this.state.phase === 'round' && (picked.length > 0 || hinting)
    if (!this.preview.visible) return

    // 고른 것이 없으면 **권하는 조합**을 보여 줍니다. 무엇을 고르면 되는지가 글로도
    // 적혀 있어야 카드의 표시가 무슨 뜻인지 압니다.
    const cards = picked.length > 0
      ? picked
      : this.state.hand
        .map(uid => this.state.deck.find(card => card.uid === uid))
        .filter((card): card is CardInstance => card !== undefined && this.hinted.has(card.uid))

    const { hand } = evaluate(cards, this.state.rules)
    const row = this.data.tables.pokerHand.findByHand(hand)
    const level = this.state.handLevels[PokerHandKind[hand]] ?? 1
    const chips = (row?.baseChips ?? 0) + (row?.chipsPerLevel ?? 0) * (level - 1)
    const mult = (row?.baseMult ?? 0) + (row?.multPerLevel ?? 0) * (level - 1)

    const ink = hinting ? COLOR.money : COLOR.chips
    this.previewHand.text = hinting
      ? `이 ${cards.length}장이면  ${this.handName(hand)}`
      : `${this.handName(hand)}   레벨 ${level}`
    this.previewHand.style.fill = ink
    this.previewValue.text = `칩 ${chips}  ×  배수 ${mult}   =   ${chips * mult}`

    const width = Math.max(this.previewHand.width, this.previewValue.width) + 40
    this.previewPlate.clear()
    this.previewPlate.roundRect(0, 0, width, 66, 10).fill({ color: 0x101724, alpha: 0.92 })
    this.previewPlate.roundRect(0.5, 0.5, width - 1, 65, 10)
      .stroke({ color: ink, width: 1.5, alpha: 0.8 })

    this.previewHand.position.set(20, 10)
    this.previewValue.position.set(20, 36)
    this.preview.position.set(BOARD_X - width / 2, PREVIEW_Y)
  }

  private drawHandList(): void {
    this.handList.removeChildren().forEach(child => child.destroy())
    this.handList.visible = this.handListOpen
    if (!this.handListOpen) return

    const rows = this.data.tables.pokerHand.records
    const width = 420
    const height = 60 + rows.length * 30

    const plate = new Graphics()
    plate.roundRect(0, 0, width, height, 12).fill({ color: 0x131a25, alpha: 0.97 })
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
    if (!done) {
      this.gameOver.removeChildren().forEach(child => child.destroy())
      this.gameOver.visible = false
      this.gameOverShown = false
      return
    }

    // **연출이 끝나기 전에는 띄우지 않습니다.** 마지막 카드를 낸 결과를 보기도 전에 판이
    // 덮이면 무엇 때문에 끝난 것인지 알 수 없습니다. `tick` 이 조건을 보고 부릅니다.
    if (this.gameOverShown) return
    this.gameOverShown = true
    this.gameOverPop = 1

    const won = this.state.phase === 'won'
    this.gameOver.removeChildren().forEach(child => child.destroy())
    this.gameOver.visible = true

    const veil = new Graphics()
    veil.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
      .fill({ color: 0x070a10, alpha: 0.82 })
    this.gameOver.addChild(veil)

    const board = new Container()
    const width = 480
    const height = 352

    const plate = new Graphics()
    plate.roundRect(-width / 2, -height / 2, width, height, 16)
      .fill(won ? 0x1d2c22 : 0x2c1a22)
    plate.roundRect(-width / 2 + 0.5, -height / 2 + 0.5, width - 1, height - 1, 16)
      .stroke({ color: won ? COLOR.good : COLOR.bad, width: 2.5 })
    board.addChild(plate)

    const title = new Text({
      text: won ? '승리' : '패배',
      style: {
        fontSize: 52, fill: won ? COLOR.good : COLOR.bad, fontWeight: '800',
        stroke: { color: 0x0a0f18, width: 6 },
      },
    })
    title.anchor.set(0.5)
    title.position.set(0, -height / 2 + 62)

    const lead = new Text({
      text: this.endLine(won),
      style: {
        fontSize: 15, fill: COLOR.ink, fontWeight: '700',
        wordWrap: true, wordWrapWidth: width - 60, align: 'center',
      },
    })
    lead.anchor.set(0.5, 0)
    lead.position.set(0, -height / 2 + 100)

    const lines = [
      `안테  ${this.state.ante} / ${this.data.run.winAnte}`,
      `낸 핸드  ${this.state.handsPlayedThisRun}`,
      `모은 조커  ${this.state.jokers.length}`,
      `시드  ${this.state.seed}`,
    ]
    const body = new Text({
      text: lines.join('\n'),
      style: { fontSize: 14, fill: 0xd2dcea, lineHeight: 24, align: 'center' },
    })
    body.anchor.set(0.5, 0)
    body.position.set(0, -height / 2 + 150)

    const again = new Button('다시 시작', 200, 52, won ? 0x2f7a52 : 0xa63f3f, () => {
      const seed = `CLOVER-${Math.floor(Math.random() * 1e6).toString().padStart(6, '0')}`
      location.href = `${location.pathname}?seed=${seed}`
    })
    again.position.set(-100, height / 2 - 76)

    board.addChild(title, lead, body, again)
    board.position.set(SIZE.width / 2, SIZE.height / 2)
    this.gameOver.addChild(board)
    this.gameOverBoard = board
    this.gameOver.zIndex = 10_000

    // 럼블. **판이 그냥 나타나면 아무 무게가 없습니다.**
    this.audio.play(won ? 'run_win' : 'run_lose')
    this.jolt(won ? 22 : 16, won ? 3.4 : 2.6, 1)
    this.flashScreen(won ? COLOR.money : COLOR.bad, won ? 0.5 : 0.34)
    if (won) this.particles.burst(SIZE.width / 2, SIZE.height / 2, 90, COLOR.money, 2.6)
  }

  /** 왜 끝났는가. **숫자가 있어야 다음 판에 무엇을 다르게 할지 압니다.** */
  private endLine(won: boolean): string {
    if (won) return `안테 ${this.data.run.winAnte}까지 넘겼습니다.`
    const short = Number(this.state.target) - Number(this.state.score)
    const where = `안테 ${this.state.ante} · ${blindName(this.state.blind)} 블라인드`
    return short > 0
      ? `${where}에서 ${short.toLocaleString('en-US')}점이 모자랐습니다.`
      : `${where}에서 멈췄습니다.`
  }

  /** 럼블. 크기가 넘쳤다가 잦아들고, 그동안 판이 조금씩 떱니다. */
  private advanceGameOver(seconds: number): void {
    const board = this.gameOverBoard
    if (!board || this.gameOverPop <= 0) return

    this.gameOverPop = Math.max(0, this.gameOverPop - seconds / 0.62)
    const t = 1 - this.gameOverPop
    // 0.45 에서 1.16 을 지나 1 로.
    const scale = t < 0.42
      ? 0.45 + (1.16 - 0.45) * (t / 0.42)
      : 1.16 - 0.16 * ((t - 0.42) / 0.58)
    const shiver = this.gameOverPop * this.gameOverPop * 12

    board.scale.set(scale)
    board.position.set(
      SIZE.width / 2 + (Math.random() - 0.5) * shiver,
      SIZE.height / 2 + (Math.random() - 0.5) * shiver)
    board.rotation = (Math.random() - 0.5) * shiver * 0.0022

    if (this.gameOverPop <= 0) {
      board.scale.set(1)
      board.position.set(SIZE.width / 2, SIZE.height / 2)
      board.rotation = 0
    }
  }

  private syncBadge(): void {
    const state = this.state

    // 연출이 도는 중에는 앞 국면의 딱지를 그대로 둡니다.
    if (!this.presented) return

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
      Number(state.target), row?.reward ?? 0, note, boss, state.blind === BlindKind.Big)
  }

  /**
   * 국면이 배경의 색을 정합니다.
   *
   * **어디에 있는지가 배경만 보고도 읽혀야 합니다.** 스몰은 초록, 빅은 호박, 보스는 붉고,
   * 상점은 푸르고, 끝났으면 색이 빠집니다.
   */
  private syncMood(): void {
    const state = this.state
    // 배경도 연출이 끝난 뒤에 갑니다. 득점 중에 색이 바뀌면 무엇이 끝난 것인지 흐려집니다.
    if (!this.presented) return

    if (state.phase === 'lost') {
      this.background.setMood([0.05, 0.05, 0.058], [0.55, 0.5, 0.55])
      return
    }
    if (state.phase === 'won') {
      this.background.setMood([0.075, 0.062, 0.026], [1, 0.82, 0.34])
      return
    }
    if (state.phase === 'shop') {
      this.background.setMood([0.032, 0.062, 0.072], [0.32, 0.86, 0.82])
      return
    }

    switch (state.blind) {
      case BlindKind.Boss:
        this.background.setMood([0.082, 0.024, 0.04], [1, 0.26, 0.33])
        break
      case BlindKind.Big:
        this.background.setMood([0.062, 0.042, 0.082], [0.72, 0.42, 0.98])
        break
      default:
        this.background.setMood([0.042, 0.052, 0.086], [0.30, 0.52, 0.98])
        break
    }
  }

  private syncButtons(): void {
    const state = this.state
    const inRound = state.phase === 'round'

    this.playButton.visible = inRound
    this.discardButton.visible = inRound
    this.clearButton.visible = inRound
    this.clearButton.enabled = inRound && this.selected.size > 0
    this.playButton.enabled = inRound && this.selected.size > 0 && state.handsLeft > 0
    this.discardButton.enabled = inRound && this.selected.size > 0 && state.discardsLeft > 0

    const choosing = state.phase === 'blind-select'
    this.primaryButton.visible = (choosing || state.phase === 'shop') && this.presented
    this.primaryButton.text = state.phase === 'shop' ? '다음 블라인드로' : '이 블라인드로'
    this.primaryButton.position.set(BOARD_X - 105, state.phase === 'shop' ? 632 : 520)
    this.skipButton.visible = choosing && state.blind !== BlindKind.Boss && this.presented
    this.sortRankButton.visible = inRound
    this.sortSuitButton.visible = inRound
    const playing = state.phase !== 'lost' && state.phase !== 'won'
    this.infoButton.visible = playing
    this.guideButton.visible = playing
    this.rerollButton.visible = state.phase === 'shop' && this.presented
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
        view.on('pointertap', () => this.toggle(card.uid))
        this.cards.set(card.uid, view)
        this.board.addChild(view)
        // 덱에서 날아옵니다. **곧바로 자리에 있으면 뽑았다는 느낌이 없습니다.**
        view.placeNow(DECK_X, DECK_Y)
        this.audio.play('card_draw')
      } else {
        view.set(card, this.editionLook(card.edition))
      }

      const chosen = this.selected.has(card.uid)
      view.selected = chosen
      // 고른 것이 하나도 없으면 아무것도 물러나지 않습니다 — 고르기 전에 화면이 어두워지면
      // 무엇이 잘못된 것처럼 보입니다.
      // 도움을 받는 카드는 물러나지 않습니다 — 어두워진 카드를 권할 수는 없습니다.
      const hint = !chosen && this.hinted.has(card.uid)
      view.hint = hint
      view.setPick(chosen ? 1 : this.selected.size === 0 || hint ? 0 : -1, PICK_TINT)

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
        view.on('pointertap', () => this.act({ t: 'sell_joker', index }))
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

      const family = consumableFamily(item.kind)
      const tile = new Panel(SIZE.jokerWidth, SIZE.jokerHeight, family.ink)
      tile.position.set(
        CONSUMABLE_X + index * (SIZE.jokerWidth + 12) - SIZE.jokerWidth / 2,
        JOKER_Y - SIZE.jokerHeight / 2)

      tile.addChild(consumableFace(item.kind, item.id, name))
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
    this.shopLayer.visible = this.state.phase === 'shop' && this.presented
    if (!this.shopLayer.visible) return

    const slots = this.state.shop.cards
    const spacing = 172
    const startX = BOARD_X - ((Math.max(1, slots.length) - 1) * spacing) / 2

    slots.forEach((item, slot) => {
      const name = shopLabel(item.kind, item.id, this.data)
      const lines = this.shopLines(item)
      const rarity = item.kind === ShopItemKind.Joker
        ? this.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0

      const tile = new Panel(160, 162, 0x1b2331)
      tile.position.set(startX + slot * spacing - 80, SHOP_CARD_Y)

      const label = new Text({
        text: name,
        style: {
          fontSize: 13, fill: COLOR.ink, fontWeight: '700',
          wordWrap: true, wordWrapWidth: 132,
        },
      })
      label.position.set(12, 12)

      const face = itemFace(item.kind, item.id, this.data, 42)
      face.position.set(130, 76)

      const blurb = new Text({
        text: lines.slice(0, 3).join('\n'),
        style: {
          fontSize: 10, fill: 0xb4c4dc, lineHeight: 13,
          wordWrap: true, wordWrapWidth: 96,
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

      tile.addChild(label, face, blurb, kindLabel, price)
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

      const tile = new Panel(300, 62, 0x1d3149)
      tile.position.set(BOARD_X - 150, SHOP_VOUCHER_Y)

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

    this.drawPackRow()
  }

  /**
   * 상점의 팩 줄.
   *
   * **팩은 사는 것이 아니라 뜯는 것입니다** — 값을 내면 몇 장이 펼쳐지고 그중에서 고릅니다.
   * 그래서 카드 칸과 다른 줄에 두고 생김새도 다르게 합니다.
   */
  private drawPackRow(): void {
    const packs = this.state.shop.packs
    const spacing = 176
    const startX = BOARD_X - ((Math.max(1, packs.length) - 1) * spacing) / 2

    packs.forEach((packId, slot) => {
      const row = this.data.tables.boosterPack.findByPackId(packId)
      if (!row) return

      const ink = packInk(row.kind)
      const tile = new Panel(164, 78, ink)
      tile.position.set(startX + slot * spacing - 82, SHOP_PACK_Y)

      const label = new Text({
        text: packName(row.kind, row.size),
        style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
      })
      label.position.set(12, 10)

      const note = new Text({
        text: `${row.cards}장 중 ${row.picks}장`,
        style: { fontSize: 10, fill: 0xdbe4f0 },
      })
      note.position.set(12, 34)

      const price = new Text({
        text: `$${row.cost}`,
        style: {
          fontSize: 17, fontWeight: '800',
          fill: this.state.money >= row.cost ? COLOR.money : 0x7a6a45,
        },
      })
      price.anchor.set(1, 1)
      price.position.set(152, 68)

      tile.addChild(label, note, price)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => {
        if (this.state.money < row.cost) return
        this.audio.play('shop_buy')
        this.particles.burst(tile.x + 82, tile.y + 39, 20, ink, 1.2)
        this.jolt(5, 3)
        this.act({ t: 'buy_pack', slot })
      })
      tile.on('pointerover', () => {
        this.tooltip.show(packName(row.kind, row.size), '팩', 0,
          [packBlurb(row.kind), `${row.cards}장이 펼쳐지고 ${row.picks}장을 고릅니다`],
          tile.x + 82, tile.y + 78, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.shopLayer.addChild(tile)
    })
  }

  /**
   * 뜯어 놓은 팩.
   *
   * **고르기 전에는 아무것도 못 합니다** — 뒤를 덮어 상점이 눌리지 않게 합니다.
   */
  private syncPack(): void {
    this.packLayer.removeChildren().forEach(child => child.destroy())
    const open = this.state.pack
    this.packLayer.visible = open !== null
    if (!open) return

    // 팩 딱지에 떠 있던 설명을 걷습니다. 뜯은 판 뒤에 남으면 지저분합니다.
    this.tooltip.hide()

    const row = this.data.tables.boosterPack.findByPackId(open.packId)
    const ink = packInk(open.kind)

    const veil = new Graphics()
    veil.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
      .fill({ color: 0x070a10, alpha: 0.78 })
    veil.eventMode = 'static'
    this.packLayer.addChild(veil)

    const left = open.options.filter((_, index) => !open.taken[index])
    const width = Math.max(520, 60 + open.options.length * 156)
    const height = 292

    const board = new Panel(width, height, ink)
    board.position.set(BOARD_X - width / 2, 244)
    this.packLayer.addChild(board)

    const title = new Text({
      text: row ? packName(row.kind, row.size) : '팩',
      style: { fontSize: 22, fill: COLOR.ink, fontWeight: '800' },
    })
    title.anchor.set(0.5, 0)
    title.position.set(BOARD_X, 262)

    const note = new Text({
      text: `${open.picksLeft}장 더 고릅니다`,
      style: { fontSize: 13, fill: 0xdbe4f0, fontWeight: '700' },
    })
    note.anchor.set(0.5, 0)
    note.position.set(BOARD_X, 292)
    this.packLayer.addChild(title, note)

    const spacing = 156
    const startX = BOARD_X - ((open.options.length - 1) * spacing) / 2

    open.options.forEach((item, index) => {
      if (open.taken[index]) return

      const name = shopLabel(item.kind, item.id, this.data)
      const lines = this.shopLines(item)
      const rarity = item.kind === ShopItemKind.Joker
        ? this.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0

      const tile = new Panel(144, 158, 0x1b2331)
      tile.position.set(startX + index * spacing - 72, 322)

      const label = new Text({
        text: name,
        style: {
          fontSize: 12, fill: COLOR.ink, fontWeight: '700',
          wordWrap: true, wordWrapWidth: 118,
        },
      })
      label.position.set(12, 11)

      const face = itemFace(item.kind, item.id, this.data, 40)
      face.position.set(116, 72)

      const blurb = new Text({
        text: lines.slice(0, 4).join('\n'),
        style: {
          fontSize: 10, fill: 0xb4c4dc, lineHeight: 13,
          wordWrap: true, wordWrapWidth: 84,
        },
      })
      blurb.position.set(12, 44)

      const kindLabel = new Text({
        text: kindName(item.kind),
        style: {
          fontSize: 10, fontWeight: '700',
          fill: rarity > 0 ? rarityColor(rarity) : 0x9b8fd0,
        },
      })
      kindLabel.anchor.set(0, 1)
      kindLabel.position.set(12, 146)

      tile.addChild(label, face, blurb, kindLabel)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      tile.on('pointertap', () => {
        this.audio.play('shop_buy')
        this.particles.burst(tile.x + 72, tile.y + 79, 22, ink, 1.3)
        this.jolt(6, 4)
        this.act({ t: 'pick_pack', index })
      })
      tile.on('pointerover', () => {
        this.tooltip.show(name, kindName(item.kind), rarity, lines,
          tile.x + 72, tile.y + 158, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.packLayer.addChild(tile)
    })

    const skip = new Button(left.length === open.options.length ? '건너뛴다' : '그만 고른다',
      160, 40, 0x4a5568, () => this.act({ t: 'skip_pack' }))
    skip.position.set(BOARD_X - 80, 494)
    this.packLayer.addChild(skip)
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

/** 점 하나가 쉬는 자리의 네모 안에 있는가. */
function near(point: { x: number; y: number },
              motion: { x: { target: number }; y: { target: number } },
              width: number, height: number): boolean {
  return Math.abs(point.x - motion.x.target) <= width / 2
    && Math.abs(point.y - motion.y.target) <= height / 2
}

/** 돈이 왜 오갔는가. 표에 없는 갈래는 적지 않습니다. */
/** 바뀐 규칙의 이름. 표에 없는 것은 식별자를 그대로 적습니다. */
function ruleName(rule: string): string {
  switch (rule) {
    case 'handSize': return '손패의 크기'
    case 'handsPerRound': return '라운드마다의 핸드 수'
    case 'discardsPerRound': return '라운드마다의 버리기 수'
    case 'jokerSlots': return '조커 슬롯'
    case 'consumableSlots': return '소모품 슬롯'
    default: return rule
  }
}

function moneyReason(reason: string): string {
  switch (reason) {
    case 'blind': return '격파 보상'
    case 'interest': return '이자'
    case 'hands_left': return '남은 핸드'
    case 'discards_left': return '남은 버리기'
    default: return ''
  }
}

function blindName(blind: BlindKind): string {
  return blind === BlindKind.Small ? '스몰' : blind === BlindKind.Big ? '빅' : '보스'
}

/**
 * 상점 칸과 팩 칸에 들어가는 작은 얼굴.
 *
 * **글씨만 있는 칸은 무엇을 사는 것인지 눈에 들어오지 않습니다.** 갈래마다 다른 문양을
 * 두어 값과 이름을 읽기 전에 「무엇」이 먼저 읽히게 합니다.
 */
function itemFace(kind: ShopItemKind, id: string, data: Data, size = 44): Container {
  const face = new Container()
  const art = new Graphics()
  const hue = hashOf(id) % 360

  if (kind === ShopItemKind.PlayingCard) {
    // 플레잉 카드는 문양이 아니라 그 카드입니다.
    const row = data.tables.baseDeckCard.findByCardId(id)
    const red = row?.suit === SuitKind.Heart || row?.suit === SuitKind.Diamond
    art.roundRect(-size * 0.32, -size / 2, size * 0.64, size, 4).fill(COLOR.cardFace)
    art.roundRect(-size * 0.32, -size / 2, size * 0.64, size, 4)
      .stroke({ color: COLOR.cardEdge, width: 1.5 })
    const pip = new Text({
      text: row ? SUIT_PIP[row.suit] ?? '' : '',
      style: { fontSize: size * 0.42, fill: red ? COLOR.red : COLOR.black },
    })
    pip.anchor.set(0.5)
    face.addChild(art, pip)
    return face
  }

  const artKind: ArtKind | undefined = kind === ShopItemKind.Joker ? 'joker'
    : kind === ShopItemKind.Tarot ? 'tarot'
      : kind === ShopItemKind.Planet ? 'planet'
        : kind === ShopItemKind.Spectral ? 'spectral' : undefined
  const texture = artKind ? artFor(artKind, id) : undefined
  if (texture) {
    const sprite = new Sprite(texture)
    sprite.width = size
    sprite.height = size
    sprite.position.set(-size / 2, -size / 2)
    face.addChild(sprite)
    return face
  }

  const glyph: GlyphName = kind === ShopItemKind.Planet ? 'planet'
    : kind === ShopItemKind.Spectral ? 'sigil'
      : glyphFor(id)
  const ink = kind === ShopItemKind.Planet ? hsl(hue, 0.6, 0.6)
    : kind === ShopItemKind.Spectral ? hsl((hue + 200) % 360, 0.5, 0.66)
      : hsl(hue, 0.64, 0.62)

  drawGlyph(art, glyph, 0, 0, size, { fill: ink, line: shade(ink, 0.6) })
  face.addChild(art)
  return face
}

/** 무늬 하나의 글자. 작은 카드 얼굴이 씁니다. */
const SUIT_PIP: Record<number, string> = {
  [SuitKind.Spade]: '♠',
  [SuitKind.Heart]: '♥',
  [SuitKind.Club]: '♣',
  [SuitKind.Diamond]: '♦',
}

/**
 * 소모품 세 갈래의 색과 문양.
 *
 * **타로·행성·유령은 서로 다른 것입니다.** 이름만 다르게 적어 두면 급할 때 구별되지 않으므로
 * 색과 문양을 갈랐습니다 — 행성은 고리가 있는 원, 유령은 사인, 타로는 식별자마다 다른 문양
 * 입니다.
 */
function consumableFamily(kind: number): { ink: number; glyph: GlyphName | null; label: string } {
  switch (kind) {
    case 2: return { ink: 0x1d3149, glyph: 'planet', label: '행성' }
    case 3: return { ink: 0x2a1d3a, glyph: 'sigil', label: '유령' }
    default: return { ink: 0x33234a, glyph: null, label: '타로' }
  }
}

/** 소모품 한 장의 얼굴. 문양과 이름과 갈래입니다. */
function consumableFace(kind: number, id: string, name: string): Container {
  const face = new Container()
  const w = SIZE.jokerWidth
  const h = SIZE.jokerHeight
  const family = consumableFamily(kind)
  const hue = hashOf(id) % 360
  const ink = kind === 2 ? hsl(hue, 0.6, 0.58)
    : kind === 3 ? hsl((hue + 200) % 360, 0.5, 0.66)
      : hsl(hue, 0.62, 0.62)

  const art = new Graphics()
  art.roundRect(6, 22, w - 12, 48, 6).fill({ color: 0x000000, alpha: 0.28 })

  const texture = artFor(artKindOf(kind), id)
  if (texture) {
    const sprite = new Sprite(texture)
    sprite.width = w - 14
    sprite.height = 46
    sprite.position.set(7, 23)
    face.addChild(sprite)
  } else {
    drawGlyph(art, family.glyph ?? glyphFor(id), w / 2, 46, 38, {
      fill: ink, line: shade(ink, 0.6),
    })
  }

  const label = new Text({
    text: name,
    style: {
      fontSize: 11, fill: COLOR.ink, align: 'center', fontWeight: '700',
      wordWrap: true, wordWrapWidth: w - 10,
    },
  })
  label.anchor.set(0.5, 0)
  label.position.set(w / 2, 4)

  const family_ = new Text({
    text: family.label,
    style: { fontSize: 10, fill: ink, fontWeight: '700' },
  })
  family_.anchor.set(0.5, 1)
  family_.position.set(w / 2, h - 6)

  face.addChild(art, label, family_)
  return face
}

/** 팩 이름. 표가 갈래와 크기만 정하므로 이름은 여기서 짓습니다. */
function packName(kind: PackKind, size: PackSize): string {
  const body = kind === PackKind.Arcana ? '비전'
    : kind === PackKind.Celestial ? '천체'
    : kind === PackKind.Spectral ? '유령'
    : kind === PackKind.Buffoon ? '어릿광대'
    : '표준'
  const scale = size === PackSize.Jumbo ? '점보 ' : size === PackSize.Mega ? '메가 ' : ''
  return `${scale}${body} 팩`
}

function packBlurb(kind: PackKind): string {
  switch (kind) {
    case PackKind.Arcana: return '타로가 들어 있습니다'
    case PackKind.Celestial: return '행성이 들어 있습니다'
    case PackKind.Spectral: return '유령이 들어 있습니다'
    case PackKind.Buffoon: return '조커가 들어 있습니다'
    default: return '강화·인장이 붙은 카드가 들어 있습니다'
  }
}

function packInk(kind: PackKind): number {
  switch (kind) {
    case PackKind.Arcana: return 0x4a3a6b
    case PackKind.Celestial: return 0x264a6b
    case PackKind.Spectral: return 0x3a2a52
    case PackKind.Buffoon: return 0x6b3a3a
    default: return 0x2f5c42
  }
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
