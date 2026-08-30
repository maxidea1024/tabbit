// 화면.
//
// **코어를 부르고 이벤트를 받아 그립니다.** 규칙은 여기 없습니다 — 어디에 놓을지와 얼마나
// 세게 보일지뿐이고, 뒤쪽의 수치는 `Const_Feel` 이므로 데이터입니다.
//
// 배치는 왼쪽에 판돈과 점수, 위에 조커와 소모품, 가운데에 낸 카드, 아래에 패입니다.
// 시선이 왼쪽에서 오른쪽으로 한 번 흐르게 두었습니다.

import {
  BlurFilter, Container, Graphics, Rectangle, Sprite, Text, Texture,
  type Application,
} from 'pixi.js'

import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { PackKind } from '../generated/enums/pack-kind'
import { PackSize } from '../generated/enums/pack-size'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { RankKind } from '../generated/enums/rank-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { Data } from '../core/data'
import { describe } from '../core/describe'
import { evaluate } from '../core/hand'
import { apply, newRun, targetOf, type Action } from '../core/run'
import { rerollCost, sellValueOf, type ShopItem } from '../core/shop'
import { bestHand, valueOf } from '../core/suggest'
import { newCounters, type CardInstance, type GameEvent, type RunState } from '../core/state'
import { BackgroundFilter } from '../shader/background'
import { PunchFilter } from '../shader/punch'
import { FlameFilter } from '../shader/flame'
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
import { artFor, onArtReady, type ArtKind } from './art'
import { drawGlyph, glyphFor, hashOf, hsl, shade, type GlyphName } from './glyph'
import { cardArtId, drawFace } from './pips'
import { COLOR, rarityColor, SIZE } from './theme'
import { Button, Panel } from '../ui/widgets'
import { Guide } from '../ui/guide'
import { Title } from '../ui/title'
import { FOOTER_BAR, panelFrame, TITLE_BAR } from '../ui/modal'
import { Modals, type ModalPanel } from '../ui/modal'
import { richLine } from '../ui/rich'
import {
  loadOptions, OptionsPanel, saveOptions, type Options,
} from '../ui/options'
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
/**
 * 고른 카드가 무슨 족보인지 뜨는 자리.
 *
 * **고른 카드는 줄에서 위로 올라옵니다.** 그만큼 띄워 두지 않으면 이 쪽지가 올라온 카드에
 * 걸터앉습니다.
 */
const PREVIEW_Y = 430
const HAND_Y = 620
/** 버튼 줄. **패 아래입니다** — 패와 겹치면 카드를 고를 수가 없습니다. */
const BUTTON_Y = 742
/** 상점의 줄들. **팩 줄이 카드 줄과 바우처 사이에 들어갑니다.** */
const SHOP_CARD_Y = 252

/** 고른 카드에 도는 빛의 색. 셰이더가 0..1 로 받습니다. */
const PICK_TINT: [number, number, number] = [0.45, 1.0, 0.68]

/** 줄바꿈. 문자열 안에 그대로 적으면 이 파일을 고치는 도구들이 자꾸 끊어 놓습니다. */
const NEWLINE = String.fromCharCode(10)

/** 칩과 배수 칸의 윗변. 불이 이 자리를 따라다닙니다. */
const CHIPS_Y = 286
/** 불이 칸 밖으로 번지는 폭과, 칸 위로 오르는 높이. */
const FIRE_PAD = 26
const FIRE_RISE = 64

/** 덱 판의 카드에 쓰는 것들. 손패의 카드와 같은 값이라 여기 한 벌만 둡니다. */
const MINI_RANK: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A',
}

const MINI_TINT: Partial<Record<number, number>> = {
  [EnhancementKind.Bonus]: 0xcfe0f5,
  [EnhancementKind.Mult]: 0xf5ccd2,
  [EnhancementKind.Wild]: 0xe6d6f5,
  [EnhancementKind.Glass]: 0xd8f0f5,
  [EnhancementKind.Steel]: 0xd6d6d6,
  [EnhancementKind.Stone]: 0xa9a396,
  [EnhancementKind.Gold]: 0xf3dc99,
  [EnhancementKind.Lucky]: 0xd2f0c6,
}

/**
 * 족보 하나가 어떤 모양인가.
 *
 * **규칙이 아니라 보기입니다.** 어느 카드로 예를 들지는 판정에 아무 영향이 없고, 그래서
 * 표가 아니라 여기 있습니다. `counts` 는 그 카드가 족보에 드는가입니다 — 들지 않는 카드가
 * 물러나 있어야 「다섯 장을 냈는데 둘만 센다」가 그림에 남습니다.
 */
const HAND_SHAPE: Partial<Record<PokerHandKind, { rank: number; suit: SuitKind;
                                                  counts: boolean }[]>> = {
  [PokerHandKind.HighCard]: [
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 10, suit: SuitKind.Heart, counts: false },
    { rank: 7, suit: SuitKind.Club, counts: false },
    { rank: 5, suit: SuitKind.Diamond, counts: false },
    { rank: 3, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.Pair]: [
    { rank: 9, suit: SuitKind.Spade, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: false },
    { rank: 6, suit: SuitKind.Diamond, counts: false },
    { rank: 2, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.TwoPair]: [
    { rank: 9, suit: SuitKind.Spade, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 4, suit: SuitKind.Club, counts: true },
    { rank: 4, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.ThreeOfAKind]: [
    { rank: 7, suit: SuitKind.Spade, counts: true },
    { rank: 7, suit: SuitKind.Heart, counts: true },
    { rank: 7, suit: SuitKind.Club, counts: true },
    { rank: 11, suit: SuitKind.Diamond, counts: false },
    { rank: 3, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.Straight]: [
    { rank: 5, suit: SuitKind.Spade, counts: true },
    { rank: 6, suit: SuitKind.Heart, counts: true },
    { rank: 7, suit: SuitKind.Club, counts: true },
    { rank: 8, suit: SuitKind.Diamond, counts: true },
    { rank: 9, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.Flush]: [
    { rank: 2, suit: SuitKind.Heart, counts: true },
    { rank: 6, suit: SuitKind.Heart, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 11, suit: SuitKind.Heart, counts: true },
    { rank: 13, suit: SuitKind.Heart, counts: true },
  ],
  [PokerHandKind.FullHouse]: [
    { rank: 8, suit: SuitKind.Spade, counts: true },
    { rank: 8, suit: SuitKind.Heart, counts: true },
    { rank: 8, suit: SuitKind.Club, counts: true },
    { rank: 3, suit: SuitKind.Diamond, counts: true },
    { rank: 3, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.FourOfAKind]: [
    { rank: 12, suit: SuitKind.Spade, counts: true },
    { rank: 12, suit: SuitKind.Heart, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: true },
    { rank: 12, suit: SuitKind.Diamond, counts: true },
    { rank: 5, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.StraightFlush]: [
    { rank: 9, suit: SuitKind.Club, counts: true },
    { rank: 10, suit: SuitKind.Club, counts: true },
    { rank: 11, suit: SuitKind.Club, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: true },
    { rank: 13, suit: SuitKind.Club, counts: true },
  ],
  [PokerHandKind.FiveOfAKind]: [
    { rank: 10, suit: SuitKind.Spade, counts: true },
    { rank: 10, suit: SuitKind.Heart, counts: true },
    { rank: 10, suit: SuitKind.Club, counts: true },
    { rank: 10, suit: SuitKind.Diamond, counts: true },
    { rank: 10, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.FlushHouse]: [
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Diamond, counts: true },
  ],
  [PokerHandKind.FlushFive]: [
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
  ],
}

const MINI_SEAL: Partial<Record<number, number>> = {
  [SealKind.Red]: 0xd23b3b,
  [SealKind.Blue]: 0x3b7fd2,
  [SealKind.Gold]: 0xe0b53b,
  [SealKind.Purple]: 0x9a5bd2,
}

const DECK_X = SIZE.width - 62
const DECK_Y = 620

export class Game {
  private readonly world = new Container()
  private readonly backdrop = new Container()
  /** 배경을 칠하는 흰 판. 창 크기를 그대로 받습니다. */
  private readonly sheet = new Sprite(Texture.WHITE)
  private readonly board = new Container()
  private readonly overlay = new Container()
  /**
   * 판이 떠 있을 때 뒤로 물러나는 것들.
   *
   * **판 뒤가 흐려져야 판이 앞에 있는 것으로 보입니다.** 어둡게만 덮으면 뒤의 글자가 읽히는
   * 채로 어두워질 뿐이고, 눈이 자꾸 뒤로 갑니다. 흐림은 이 통 하나에 걸립니다 — 판과 설명
   * 쪽지는 이 통 밖이라 또렷하게 남습니다.
   */
  private readonly scene = new Container()
  private readonly blur = new BlurFilter({ strength: 0, quality: 3 })
  /** 배경도 함께 흐립니다. **필터 하나를 둘에 걸지 않습니다** — 같은 프레임에 두 번 쓰입니다. */
  private readonly blurBack = new BlurFilter({ strength: 0, quality: 3 })
  /** 지금 흐린 정도. 판이 열리고 닫힐 때 잦아듭니다. */
  private blurShown = 0

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
  private readonly toasts = new Toasts(BOARD_X)

  private readonly cards = new Map<number, CardView>()
  private readonly playedViews: CardView[] = []
  /** 아직 날아가지 않은 카드들. 왼쪽부터 한 장씩 차례로 갑니다. */
  private readonly slams: { view: CardView; x: number; at: number }[] = []
  /** 아직 나가지 않은 버린 카드들. 이것도 왼쪽부터 한 장씩입니다. */
  private readonly fades: { view: CardView; at: number; spark: boolean }[] = []
  /** 아직 깔리지 않은 뽑은 카드들. **덱에서 한 장씩 옵니다.** */
  private readonly deals: { uid: number; at: number }[] = []
  private readonly jokers = new Map<number, JokerView>()
  /** 타는 중인 조커들. 다 타면 치웁니다. */
  private readonly burning: JokerView[] = []
  private readonly selected = new Set<number>()
  /**
   * 고른 조커나 소모품 하나.
   *
   * **누르는 것만으로는 아무것도 팔리지 않습니다.** 조커가 판의 전부인 게임에서 한 번
   * 잘못 누른 것이 판을 끝내면 안 됩니다 — 고르면 그 밑에 무엇을 할지가 버튼으로 서고,
   * 그 버튼을 눌러야 일어납니다.
   */
  private held?: { kind: 'joker' | 'consumable'; uid: number }
  /** 고른 것 밑에 서는 버튼들. */
  private readonly heldBar = new Container()
  /**
   * 끌고 있는 것.
   *
   * **자리가 규칙입니다.** 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터이므로,
   * 무엇을 어디에 두느냐가 최종 점수를 바꿉니다 — 그것을 정하지 못하면 판을 짜는 일의
   * 절반이 없습니다.
   */
  private drag?: {
    kind: 'hand' | 'joker'
    uid: number
    startX: number
    startY: number
    grabX: number
    moved: boolean
  }
  /** 손패가 놓인 자리. 끌 때 어느 칸으로 가는지를 이것으로 셉니다. */
  private handSpots = { startX: 0, spacing: 0 }
  /** 도움. 이것도 고르면 더 높은 족보가 되는 카드들입니다. */
  private readonly hinted = new Set<number>()

  private readonly badge = new BlindBadge(PANEL_W)
  private readonly score = new Slot('라운드 점수', PANEL_W, 68, COLOR.ink)
  // **이 둘이 화면에서 가장 큰 두 숫자입니다.** 점수는 이 둘의 곱이고, 나머지 칸들은
  // 그것을 설명하는 것들입니다 — 크기가 그 서열을 그대로 보여야 합니다.
  private readonly chips = new Slot('칩', 124, 78, COLOR.chips, 34)
  private readonly mult = new Slot('배수', 124, 78, COLOR.mult, 34)
  /** 두 칸 뒤에서 타오르는 불. 배수가 커지면 불길이 높아집니다. */
  private readonly chipsFire = new Sprite(Texture.WHITE)
  private readonly multFire = new Sprite(Texture.WHITE)
  private readonly chipsFlame = new FlameFilter([0.16, 0.45, 0.95], [0.72, 0.94, 1.0])
  private readonly multFlame = new FlameFilter([0.85, 0.16, 0.12], [1.0, 0.85, 0.35])
  /** 지금 얼마나 뜨거운가. 박자가 올리고 시간이 내립니다. */
  private fever = 0
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
  /**
   * 조커 칸이 몇 칸 찼는가.
   *
   * **「조커」라고 적지 않습니다.** 칸에 조커가 서는 줄이고, 그 줄 아래의 `0 / 5` 는 그
   * 줄에 관한 것 말고 다른 것일 수 없습니다.
   */
  private readonly jokerCount = new Text({
    text: '', style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800' },
  })
  private readonly consumableCount = new Text({
    text: '', style: { fontSize: 12, fill: 0x9b8fd0, fontWeight: '800' },
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
  private readonly deckButton: Button
  private readonly optionButton: Button
  /** 게임 방법. **첫 판에서 저절로 한 번 열립니다.** */
  /**
   * 떠 있는 판들.
   *
   * **여는 순서가 곧 위아래입니다.** 판마다 자기 층에 붙어 있으면 어느 것이 위인지가 붙인
   * 순서로 정해지고, 족보 목록이 블라인드 판 아래로 들어가는 일이 생깁니다.
   */
  private readonly modals = new Modals()
  private readonly guide = new Guide(
    () => this.modals.close(this.guide), () => this.toggleHandList())
  private readonly optionsPanel: OptionsPanel
  /** 타이틀. **시작을 누르기 전에는 판이 없습니다.** */
  private readonly title: Title
  private started = false
  /** 옵션. 타이틀이 고치고 화면이 읽습니다. */
  private readonly settings: Options = loadOptions()
  /**
   * 지금 무엇을 하면 되는가.
   *
   * **국면마다 한 줄입니다.** 화면에 버튼이 여럿 있어도 다음에 누를 것이 무엇인지가
   * 적혀 있지 않으면 처음 여는 사람은 움직이지 못합니다.
   */
  private readonly hint = new Container()
  /** 지금 적혀 있는 지시문. 같은 글이면 다시 만들지 않습니다. */
  private hintShown = ''
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
  private readonly handList: ModalPanel = {
    view: new Container(),
    size: { width: 540, height: 60 },
  }
  /** 족보 목록의 줄들. 어느 줄을 가리키고 있는지를 자리로 셉니다. */
  private readonly handRows: { hand: PokerHandKind; seen: boolean;
                               y: number; height: number }[] = []
  private handBand?: Graphics
  private handPreview?: Container
  private handHovered = -1
  /**
   * 덱에 남은 카드.
   *
   * **무엇이 남았는지를 모르면 버릴지 낼지를 정할 수 없습니다.** 스트레이트에 한 장이
   * 모자랄 때 그 랭크가 덱에 아직 있는지가 그 판의 판단 전부입니다.
   */
  private readonly deckView: ModalPanel = {
    view: new Container(),
    size: { width: 520, height: 300 },
  }
  /**
   * 블라인드 셋을 한 자리에 세운 판.
   *
   * **셋을 함께 보여야 건너뛸지를 판단할 수 있습니다.** 지금 것 하나만 보여 주면 다음이
   * 무엇인지 모르는 채로 넘길지를 정하게 되고, 그건 선택이 아니라 찍기입니다.
   */
  private readonly blindPick = new Container()
  /**
   * 블라인드 판이 들어오는 정도. 0 에서 1 로 갑니다.
   *
   * **떡하니 서 있으면 밋밋합니다.** 셋이 왼쪽부터 차례로 아래에서 올라와야 「고르는 자리에
   * 왔다」가 됩니다.
   */
  private blindEnter = 0
  /** 지금 그려진 판이 어느 블라인드의 것인가. 바뀌면 다시 들어옵니다. */
  private blindShown = -1
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
  /**
   * 화면이 지금 주장하고 있는 것.
   *
   * **코어는 액션 하나를 끝까지 처리하고 답을 돌려줍니다.** 그 답을 그대로 그리면 카드가
   * 아직 날아가는 중에 최종 점수가 떠 있고, 다음 패가 이미 깔려 있고, 격파 보상이 이미
   * 들어와 있습니다 — 연출이 도는 의미가 없어집니다.
   *
   * 그래서 화면은 **박자가 도달한 데까지만** 압니다. 연출이 끝나면 상태와 같아집니다.
   */
  private shown = { score: 0, money: 0, hand: [] as number[] }
  /**
   * 칩 칸과 배수 칸이 가운데로 모인 정도. 1 에서 0 으로 잦아듭니다.
   *
   * **곱하기가 화면에서 일어나야 합니다.** 두 숫자가 제자리에 있는 채로 점수만 튀어나오면,
   * 그 점수가 어디서 온 것인지 눈으로 이어지지 않습니다.
   */
  private merge = 0
  /** 배경이 지금 그리고 있는 열기. 목표로 천천히 따라갑니다. */
  private heatShown = 0.1
  private clock = 0
  private pointerAt = { x: 0, y: 0 }
  /** 지금 설명이 떠 있는 조커. 바뀔 때만 다시 그립니다. */
  private hoveredJoker?: JokerView

  constructor(private readonly app: Application, private readonly data: Data, seed: string) {
    this.feel = readFeel(data.feel)
    this.audio = new Audio(data.tables)
    this.state = newRun(data, seed, 'red_deck', 'White').state
    this.player = new TimelinePlayer(beat => this.showBeat(beat))
    this.title = new Title(seed, () => this.start(),
      () => this.modals.open(this.guide), () => this.modals.open(this.optionsPanel))
    this.optionsPanel = new OptionsPanel(this.settings, () => this.applyOptions(),
      () => this.modals.close(this.optionsPanel))

    // 배경은 흰 스프라이트 한 장에 셰이더를 얹은 것입니다.
    this.sheet.filters = [this.background]
    this.backdrop.addChild(this.sheet)

    // **배경은 세계 밖에 있습니다.** 판은 기준 해상도에 맞춰 가운데에 놓이므로 창의 비율이
    // 다르면 옆이나 아래가 남습니다 — 그 자리를 검정으로 두지 않고 배경이 창 전체를 덮습니다.
    app.stage.addChild(this.backdrop, this.world)

    // **타이틀은 독립된 화면입니다.** 시작을 누르기 전에는 판도 조각들도 그리지 않습니다 —
    // 가려 두는 것과 없는 것은 다르고, 반투명한 판 뒤로 카드가 비치면 시작 전인지가
    // 흐려집니다.
    this.board.visible = false
    this.overlay.visible = false
    // **타이틀은 판 바깥입니다.** 판과 조각들을 통째로 끄고 그 위에 홀로 섭니다.
    this.scene.addChild(this.board, this.particles, this.overlay,
      this.coins, this.toasts, this.screenFlash, this.title)
    this.world.addChild(this.scene, this.modals, this.tooltip)

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
    // **가운데 버튼이 곧 몇 장 골랐는가입니다.** 점 다섯을 따로 두면 같은 것을 두 곳에서
    // 세게 되고, 그 둘 사이를 눈이 오갑니다.
    this.clearButton = new Button('-', 60, 46, 0x4a5568, () => this.clearSelection())
    this.primaryButton = new Button('이 블라인드로', 210, 50, 0x2f6fb5, () => this.primary())
    this.skipButton = new Button('건너뛴다', 150, 38, 0x4a5568,
      () => this.act({ t: 'skip_blind' }))
    this.rerollButton = new Button('리롤', 128, 44, 0x3f5f8f, () => this.reroll())
    this.sortRankButton = new Button('랭크순', 92, 32, 0x333e4e, () => this.sortHand('rank'))
    this.sortSuitButton = new Button('무늬순', 92, 32, 0x333e4e, () => this.sortHand('suit'))
    this.infoButton = new Button('족보 목록', 118, 34, 0x3a4658, () => this.toggleHandList())
    this.deckButton = new Button('남은 카드', 118, 34, 0x3a4658, () => this.toggleDeckView())
    this.guideButton = new Button('게임 방법', 118, 34, 0x3a4658,
      () => this.modals.open(this.guide))
    this.optionButton = new Button('옵션', 118, 34, 0x3a4658,
      () => this.modals.open(this.optionsPanel))

    this.overlay.addChild(this.playButton, this.discardButton, this.primaryButton,
      this.clearButton, this.skipButton, this.rerollButton, this.shopLayer, this.packLayer,
      this.sortRankButton, this.sortSuitButton, this.infoButton, this.guideButton,
      this.optionButton, this.deckButton, this.preview, this.blindPick, this.gameOver)

    this.preview.addChild(this.previewPlate, this.previewHand, this.previewValue)
    this.preview.visible = false
    this.gameOver.visible = false
    // 낸다 · 취소 · 버린다. **취소가 가운데인 것이 맞습니다** — 둘 중 어느 쪽으로도
    // 가기 전에 되돌리는 것이기 때문입니다.
    this.playButton.position.set(BOARD_X - 176, BUTTON_Y)
    this.clearButton.position.set(BOARD_X - 30, BUTTON_Y)
    this.discardButton.position.set(BOARD_X + 48, BUTTON_Y)
    this.primaryButton.position.set(BOARD_X - 105, 520)
    this.skipButton.position.set(BOARD_X - 75, 586)
    this.rerollButton.position.set(BOARD_X - 64, 578)
    this.sortRankButton.position.set(LEFT + PANEL_W + 30, BUTTON_Y + 7)
    this.sortSuitButton.position.set(LEFT + PANEL_W + 130, BUTTON_Y + 7)
    this.infoButton.position.set(LEFT - 2, 662)
    this.guideButton.position.set(LEFT + 134, 662)
    this.deckButton.position.set(LEFT - 2, 700)
    this.optionButton.position.set(LEFT + 134, 700)

    app.canvas.addEventListener('pointerdown', () => this.audio.unlock())
    // **누르는 순간 툴팁이 닫힙니다.** 툴팁은 마우스가 그것에서 벗어날 때 닫히는데, 누른
    // 것이 사라지면(사거나 팔거나 쓰거나) 벗어나는 일이 영영 없어서 그 자리에 남습니다.
    app.stage.on('pointerdown', () => this.tooltip.hide())
    app.stage.eventMode = 'static'
    app.stage.hitArea = { contains: () => true }
    app.stage.on('globalpointermove', event => {
      this.pointerAt = this.world.toLocal(event.global)
      this.advanceDrag()
    })
    // **판 밖에서 떼어도 끝나야 합니다.** 카드 위에서만 받으면 손가락이 판 밖으로 나간
    // 채 떼었을 때 그 카드가 커서에 붙어 남습니다.
    app.stage.on('pointerup', () => this.endDrag())
    app.stage.on('pointerupoutside', () => this.endDrag())
    window.addEventListener('keydown', event => {
      this.audio.unlock()
      // 판이 떠 있으면 맨 위의 것을 닫습니다. **연출을 넘기는 것보다 앞섭니다** — 판을
      // 보고 있는 사람에게 아무 키나는 「닫기」입니다.
      if (this.modals.busy) {
        if (event.key === 'Escape') this.modals.closeTop()
        return
      }
      // 고른 조커를 놓습니다. **판이 없을 때의 ESC 는 「무르기」입니다.**
      if (event.key === 'Escape' && this.held) {
        this.held = undefined
        this.refresh()
        return
      }
      if (this.player.busy) this.player.hurry(this.feel)
    })

    // 그림이 새로 들어오면 다시 그립니다. 문양이 그림으로 바뀝니다.
    onArtReady(() => this.refresh())

    this.refresh()
    app.ticker.add(ticker => this.tick(ticker.deltaMS))

  }

  /**
   * 옵션이 정한 것을 화면에 겁니다.
   *
   * **여기 있는 것은 전부 실제로 무언가를 합니다.** 값만 저장하고 아무 데도 쓰지 않으면
   * 그것은 옵션이 아니라 장식입니다.
   */
  private applyOptions(): void {
    this.audio.muted = !this.settings.sound
    this.audio.volume = this.settings.volume / 100
    this.player.base = this.settings.speed
    this.particles.enabled = this.settings.particles
    saveOptions(this.settings)
    // 도움 표시는 켜고 끄는 그 자리에서 바로 사라져야 합니다.
    this.updateHints()
    this.syncCards()
  }

  /**
   * 타이틀에서 시작합니다.
   *
   * 게임 방법은 **처음 여는 사람에게만** 저절로 펼쳐집니다. 두 번째부터는 타이틀의 버튼과
   * 왼쪽 아래 버튼으로 엽니다.
   */
  private start(): void {
    if (this.started) return
    this.started = true
    this.title.visible = false
    this.board.visible = true
    this.overlay.visible = true
    this.audio.unlock()
    this.applyOptions()
    this.settleShown()
    this.refresh()
    // **처음 값은 굴러가지 않습니다.** 판에 들어서는 순간의 금액은 「바뀐 것」이 아니라
    // 「원래 그런 것」이고, 0에서 세어 올라가면 무언가를 벌어들인 것으로 보입니다.
    this.money.reset(this.state.money)
    this.score.reset(0)
    this.chips.reset(0)
    this.mult.reset(0)

    try {
      if (localStorage.getItem('clover.guide.seen') === null) {
        this.modals.open(this.guide)
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
    this.score.position.set(LEFT, 208)
    this.chips.position.set(LEFT, CHIPS_Y)
    this.mult.position.set(LEFT + 140, CHIPS_Y)
    this.hands.position.set(LEFT, 386)
    this.discards.position.set(LEFT + 134, 386)
    this.money.position.set(LEFT, 450)
    this.anteSlot.position.set(LEFT + 134, 450)

    const times = new Text({ text: '×', style: { fontSize: 28, fill: COLOR.ink } })
    times.anchor.set(0.5)
    times.position.set(LEFT + 132, CHIPS_Y + 42)

    // 불은 **칸 뒤**입니다. 앞에 두면 숫자를 가리고, 그러면 연출이 정보를 덮습니다.
    for (const fire of [this.chipsFire, this.multFire]) {
      fire.width = 124 + FIRE_PAD * 2
      fire.height = 78 + FIRE_PAD * 2 + FIRE_RISE
      fire.blendMode = 'add'
      fire.visible = false
    }
    this.chipsFire.filters = [this.chipsFlame]
    this.multFire.filters = [this.multFlame]

    this.board.addChild(this.badge, this.score,
      this.chipsFire, this.multFire, this.chips, times, this.mult,
      this.hands, this.discards, this.money, this.anteSlot)

    // 가운데에서 커집니다. 위쪽을 붙잡고 키우면 글씨가 아래로 자라 보입니다.
    this.headline.anchor.set(0.5, 0.5)
    this.headline.position.set(BOARD_X, 214)

    // **칸 아래입니다.** 위에 두면 줄과 화면 위쪽 사이가 좁아 글이 끼어 있는 것으로
    // 보이고, 아래에 두면 줄에 딸린 것으로 읽힙니다.
    this.jokerCount.anchor.set(0.5, 0)
    this.jokerCount.position.set(
      JOKER_X + (SIZE.jokerWidth + 12) * 2, JOKER_Y + SIZE.jokerHeight / 2 + 9)
    this.consumableCount.anchor.set(0.5, 0)
    this.consumableCount.position.set(
      CONSUMABLE_X + (SIZE.jokerWidth + 12) / 2, JOKER_Y + SIZE.jokerHeight / 2 + 9)

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
    this.hint.position.set(BOARD_X, BUTTON_Y - 36)

    // **덱은 판이 도는 동안만 화면에 있습니다.** 상점에서는 오른쪽으로 밀려 나가고,
    // 다음 블라인드로 가면 다시 들어옵니다 — 상점의 물건과 자리를 다투지 않습니다.
    this.deckLayer.addChild(pile, this.deckLabel)

    this.board.addChild(this.deckLayer, this.headline, this.gauge, this.jokerCount,
      this.consumableCount, this.consumableLayer, this.heldBar, this.hint, this.panelFlash)
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
    // 무엇이 일어나면 가리키던 것이 그대로 있으리라는 보장이 없습니다.
    this.tooltip.hide()
    const before = this.shown.hand
    const step = apply(this.data, this.state, action)
    this.rewind(step.events, before)
    this.announce(step.events)
    this.startTimeline(step.events)
    this.refresh()
  }

  /**
   * 이 액션이 낸 이벤트들을 되짚어, **연출이 아직 도달하지 않은 것을 화면에서 뺍니다.**
   *
   * 점수와 금액은 늘어난 만큼 되돌리고, 패는 뽑기 전의 모습으로 되돌립니다. 그다음은 박자가
   * 하나씩 도로 채웁니다.
   */
  private rewind(events: readonly GameEvent[], before: readonly number[]): void {
    let money = this.state.money
    let score = Number(this.state.score)
    const drawn = new Set<number>()
    const leaving = new Set<number>()

    for (const event of events) {
      switch (event.t) {
        case 'MoneyChanged': money -= event.delta; break
        case 'ScoreResolved': score -= event.score; break
        case 'HandDrawn': for (const uid of event.uids) drawn.add(uid); break
        case 'HandPlayed':
        case 'HandDiscarded': for (const uid of event.uids) leaving.add(uid); break
        default: break
      }
    }

    const held = new Set(this.state.hand)
    this.shown = {
      money,
      score,
      // 아직 패에 있는 것과, 이번에 패를 떠나지만 아직 떠나는 것이 보이지 않은 것.
      hand: before.filter(uid => (held.has(uid) && !drawn.has(uid)) || leaving.has(uid)),
    }
  }

  /** 연출이 끝났습니다. 화면이 주장하는 것을 상태와 맞춥니다. */
  private settleShown(): void {
    this.deals.length = 0
    this.shown = {
      money: this.state.money,
      score: Number(this.state.score),
      hand: this.state.hand.slice(),
    }
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

        // **무엇이 어떻게 바뀌었는가**가 두 줄입니다. 「규칙이 바뀌었습니다」와 식별자
        // 하나로는 무엇을 얻은 것인지 알 수 없습니다.
        case 'RuleChanged':
          this.toasts.push(this.ruleName(event.rule), ruleChange(event), COLOR.money, 2.8)
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
    // **카드를 올리는 것도 박자입니다.** 여기서 올리고 득점을 따로 세면 둘의 간격이 코드에
    // 고정되고, `Const_Feel` 을 고쳐도 화면이 바뀌지 않습니다.
    this.act({ t: 'play', cards })
  }

  private discard(): void {
    if (this.selected.size === 0 || this.player.busy) return
    const cards = this.orderedSelection()
    this.selected.clear()
    // 버리는 것도 한 장씩입니다. **한 덩어리로 사라지면 몇 장을 버렸는지가 남지 않습니다.**
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
    if (this.modals.has(this.handList)) {
      this.modals.close(this.handList)
      return
    }
    this.drawHandList()
    this.modals.open(this.handList)
  }

  /** 남은 카드를 열고 닫습니다. */
  private toggleDeckView(): void {
    if (this.modals.has(this.deckView)) {
      this.modals.close(this.deckView)
      return
    }
    this.drawDeckView()
    this.modals.open(this.deckView)
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
        view, x: startX + index * spacing,
        at: this.clock + index * (this.feel.playStaggerMs / 1000),
      })
    })
  }

  /**
   * 버린 카드를 한 장씩 내보냅니다.
   *
   * **곧바로 지우지 않습니다** — 사라지는 것이 보여야 몇 장을 버렸는지가 남습니다.
   */
  private throwAway(uids: readonly number[]): void {
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
      this.fades.push({
        view, at: this.clock + index * (this.feel.playStaggerMs / 1000), spark: true,
      })
    })
  }

  /** 예약해 둔 한 장씩의 깔기. */
  private advanceDeals(): void {
    let dealt = false
    while (this.deals.length > 0 && this.deals[0].at <= this.clock) {
      const next = this.deals.shift()
      if (!next) break
      this.shown.hand = [...this.shown.hand, next.uid]
      dealt = true
    }
    if (dealt) this.refresh()
  }

  /** 예약해 둔 한 장씩의 내보내기. */
  private advanceFades(): void {
    while (this.fades.length > 0 && this.fades[0].at <= this.clock) {
      const next = this.fades.shift()
      if (!next) break
      next.view.retire()
      // 버린 카드만 흩어집니다. **득점하고 물러나는 카드는 조용히 나갑니다** — 방금 빛이
      // 돌았던 카드가 다시 터지면 무엇이 끝난 것인지 흐려집니다.
      if (next.spark) this.particles.burst(next.view.x, next.view.y, 6, 0xff9d5c, 0.6)
      this.audio.play('card_destroy')
    }
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

  /**
   * 낸 카드를 물러나게 합니다. 화면 밖으로 나가면 그때 지웁니다.
   *
   * **한 장씩 나갑니다.** 다섯 장이 한꺼번에 미끄러지면 한 덩어리가 빠져나가는 것으로
   * 보이고, 낸 것이 다섯 장이었다는 것이 마지막에 지워집니다.
   */
  private clearPlayArea(): void {
    this.playedViews.forEach((view, index) => {
      if (view.retiring) return
      this.fades.push({
        view, at: this.clock + index * (this.feel.playStaggerMs / 1000), spark: false,
      })
    })
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
      this.settleShown()
      this.chips.reset(0)
      this.mult.reset(0)
      this.score.target = Number(this.state.score)
      return
    }

    this.chips.reset(0)
    this.mult.reset(0)
    // 옵션의 배속. **연출을 끄지는 못하고 빨리 넘길 수만 있습니다.**
    this.player.base = this.settings.speed
    this.player.play(beats)
  }

  private showBeat(beat: Beat): void {
    const event = beat.event
    const semitones = semitonesOf(beat.intensity, this.feel)
    const dust = particlesOf(beat.intensity, this.feel)

    switch (event.t) {
      // 낸 카드가 왼쪽부터 한 장씩 판으로 올라갑니다. **이 박자가 끝날 때까지 아무것도
      // 세지 않습니다.**
      case 'HandPlayed':
        this.shown.hand = this.shown.hand.filter(uid => !event.uids.includes(uid))
        this.liftToPlayArea(event.uids)
        this.refresh()
        break

      // 다음 패. **득점이 끝난 뒤에, 한 장씩** 덱에서 옵니다 — 여러 장이 한꺼번에 깔리면
      // 뽑았다는 느낌이 없고 그냥 패가 바뀌어 있습니다.
      case 'HandDrawn':
        event.uids.forEach((uid, index) => {
          this.deals.push({ uid, at: this.clock + index * (this.feel.drawStaggerMs / 1000) })
        })
        break

      case 'HandDiscarded':
        this.shown.hand = this.shown.hand.filter(uid => !event.uids.includes(uid))
        this.throwAway(event.uids)
        this.refresh()
        break

      case 'HandEvaluated':
        // **득점하지 않는 카드는 물러납니다.** 다섯 장을 냈는데 셋만 세는 것이 화면에
        // 보이지 않으면, 점수가 왜 그것뿐인지 알 수 없습니다.
        this.dimNonScoring(event.cards)
        this.say(`${this.handName(event.hand)}   레벨 ${event.level}`, COLOR.ink, 3, 0.35)
        this.audio.play('score_count', semitones)
        this.flashPanel(COLOR.ink, 0.5)
        break

      case 'CardScored': {
        const view = this.viewOf(event.uid)
        // 카드가 차례로 득점할수록 세집니다. **뒤로 갈수록 커지는 것이 기대를 만듭니다.**
        const step = Math.min(1, this.chain / 5)
        const mul = event.op === 'MulMult'
        const tint = event.source === 'rank' || event.chips !== 0 ? COLOR.chips
          : event.money !== 0 ? COLOR.money : COLOR.mult
        this.chain++
        if (view) {
          // **조각을 터뜨리지 않고 빛을 돌립니다.** 카드가 차례로 터지면 화면이 시끄러워지고,
          // 정작 카드 위에 뜬 숫자가 그 조각에 묻힙니다.
          view.pop(0.5 + beat.intensity * 0.4 + step * 0.25 + (mul ? 0.35 : 0))
          view.shine(rgbOf(tint), 1)
        }
        this.popAt(view, valueText(event.op, event.chips, event.mult, event.money),
          tint, beat.intensity + step * 0.4 + (mul ? 0.5 : 0))
        // 랭크의 칩과 강화·인장·에디션이 낸 것은 소리가 달라야 갈립니다.
        this.audio.play(event.source === 'rank' ? 'card_chip'
          : mul ? 'joker_mul' : 'joker_add', semitones + this.chain * 2)
        // **화면은 흔들지 않습니다.** 한 장이 점수를 내는 것은 다섯 번, 여덟 번 이어지는
        // 일이고, 그때마다 화면이 흔들리면 카드 위의 숫자를 읽을 수 없습니다 — 일어난 자리를
        // 가리키는 것은 그 카드에 도는 빛 하나로 충분합니다.
        this.flashPanel(tint, 0.4 + step * 0.3)
        this.stop(28 + step * 26 + (mul ? 60 : 0))
        if (event.money !== 0 && view) {
          this.coins.fly(event.money, { x: view.x, y: view.y }, this.moneySpot())
        }
        break
      }

      case 'JokerTriggered': {
        const view = this.jokers.get(this.jokerUidAt(event.slot))
        const mul = event.op === 'MulMult'
        const money = event.op === 'AddMoney'
        const grow = event.op === 'GrowSelf'
        const cue = mul ? 'joker_mul' : money ? 'joker_money' : 'joker_add'
        const text = valueText(event.op, event.chips, event.mult, event.money)
        const tint = grow ? COLOR.good
          : money ? COLOR.money
            : mul || event.chips === 0 ? COLOR.mult : COLOR.chips

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

      // 덱과 바우처와 보스가 낸 것. **조커가 아닌 것도 임자가 있습니다** — 판돈 딱지가
      // 그 자리입니다.
      case 'RunTriggered': {
        const mul = event.op === 'MulMult'
        const tint = event.money !== 0 ? COLOR.money
          : event.chips !== 0 ? COLOR.chips : COLOR.mult
        this.popAt(this.badge, valueText(event.op, event.chips, event.mult, event.money),
          tint, beat.intensity + (mul ? 0.5 : 0.1))
        this.audio.play(mul ? 'joker_mul' : 'joker_add', semitones)
        this.jolt(4 + beat.intensity * 5, 0.7 + beat.intensity, 0.2)
        this.flashPanel(tint, 0.6)
        this.stop(mul ? 90 : 40)
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
          view.pop(1)
          view.shine(rgbOf(COLOR.good), 0.9)
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
        this.shown.money += event.delta
        this.money.target = this.shown.money
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
        // **더해집니다.** 이 판의 점수가 아니라 라운드에 쌓인 점수가 칸에 뜹니다.
        this.shown.score += event.score
        this.score.target = this.shown.score
        this.audio.play('score_settle', semitones)

        // **칩 칸과 배수 칸이 가운데로 모여 하나가 됩니다.** 이 게임에서 가장 큰 장면이
        // 두 숫자가 곱해지는 순간이고, 그것이 화면에서 일어나야 합니다.
        this.merge = 1

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
        this.chain = 0
        this.fever = 0
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

    // **박자가 값을 들고 옵니다.** 화면이 이벤트마다 값을 다시 세지 않는 이유가 이것입니다 —
    // 누적값과 에디션처럼 세는 자리가 여럿이면 반드시 한쪽이 빠집니다.
    if (beat.chips !== undefined) this.chips.target = beat.chips
    if (beat.mult !== undefined) this.mult.target = Math.round(beat.mult / 10_000)
    this.chips.emphasize(scaleOf(beat.intensity, this.feel))
    this.mult.emphasize(scaleOf(beat.intensity, this.feel))
    // **오를 때는 즉시.** 배수가 커진 그 박자에서 불이 붙어야 그 조커가 한 일로 읽힙니다.
    this.fever = Math.max(this.fever, beat.intensity)
  }

  /**
   * 득점하지 않는 카드를 물러나게 합니다.
   *
   * **원작이 그렇습니다** — 다섯 장을 냈는데 족보에 드는 것이 둘뿐이면 나머지 셋은 회색이
   * 되고, 뜨지도 세지도 않습니다.
   */
  private dimNonScoring(scoring: readonly number[]): void {
    for (const view of this.playedViews) {
      const counts = scoring.includes(view.uid)
      view.setPick(counts ? 0 : -1, PICK_TINT)
      view.idle = counts ? 0.4 : 0.15
    }
  }

  /**
   * 두 칸이 가운데로 모였다 돌아옵니다.
   *
   * 잦아드는 시간은 `MultiplyMs` 입니다 — 점수가 굴러가는 시간과 같아야 둘이 한 동작으로
   * 보입니다.
   */
  private advanceMerge(deltaMs: number): void {
    const restChips = LEFT
    const restMult = LEFT + 140
    const middle = (restChips + restMult) / 2

    if (this.merge > 0) {
      this.merge = Math.max(0, this.merge - deltaMs / Math.max(1, this.feel.multiplyMs))
      // 모였다가 풀립니다. 가운데에 가장 가까운 것이 절반 지점입니다.
      const pull = Math.sin(Math.min(1, 1 - this.merge) * Math.PI)
      this.chips.position.set(restChips + (middle - restChips) * pull, CHIPS_Y - pull * 6)
      this.mult.position.set(restMult + (middle - restMult) * pull, CHIPS_Y - pull * 6)
      const swell = 1 + pull * 0.18
      this.chips.scale.set(swell)
      this.mult.scale.set(swell)
    } else if (this.chips.x !== restChips) {
      this.chips.position.set(restChips, CHIPS_Y)
      this.mult.position.set(restMult, CHIPS_Y)
      this.chips.scale.set(1)
      this.mult.scale.set(1)
    }

    this.advanceFire(deltaMs)
  }

  /**
   * 두 칸이 타오릅니다.
   *
   * **세기는 흔들림과 같은 값입니다** — `Const_Feel` 의 문턱을 넘은 배수부터 불이 붙고,
   * 상한에서 가장 높습니다. 채널마다 따로 재면 어느 하나만 사납게 반응하는 화면이 됩니다.
   *
   * 불은 곧바로 꺼지지 않습니다. 박자마다 한 번씩 붙었다 꺼지면 깜빡이는 것으로 보이므로,
   * 오를 때는 즉시 오르고 내릴 때만 잦아듭니다.
   */
  private advanceFire(deltaMs: number): void {
    const seconds = deltaMs / 1000
    this.fever = Math.max(0, this.fever - seconds * 0.85)

    const heat = this.fever
    this.chipsFlame.heat = heat * 0.72
    this.multFlame.heat = heat
    this.chipsFlame.advance(seconds)
    this.multFlame.advance(seconds)

    const lit = heat > 0.004
    this.chipsFire.visible = lit
    this.multFire.visible = lit
    if (!lit) return

    // 칸을 따라다닙니다. 합쳐지는 동안 칸이 움직이므로 자리를 매 프레임 맞춥니다.
    const follow = (fire: Sprite, slot: Slot) => {
      fire.position.set(slot.x - FIRE_PAD, slot.y - FIRE_PAD - FIRE_RISE)
      fire.scale.set(slot.scale.x, slot.scale.y)
    }
    follow(this.chipsFire, this.chips)
    follow(this.multFire, this.mult)
  }

  /**
   * 판 뒤를 흐립니다.
   *
   * **필요할 때만 겁니다.** 흐림은 화면 전체를 한 번 더 굽는 것이라, 판이 없는 동안에도
   * 걸어 두면 매 프레임 그 값을 냅니다.
   */
  private advanceBlur(seconds: number): void {
    const want = this.modals.busy ? 1 : 0
    this.blurShown += (want - this.blurShown) * Math.min(1, seconds * 11)

    const on = this.blurShown > 0.01
    const filtered = (this.scene.filters as unknown[] | null)?.length ?? 0
    if (on && filtered === 0) {
      this.scene.filters = [this.blur]
      this.backdrop.filters = [this.blurBack]
    } else if (!on && filtered > 0) {
      this.scene.filters = []
      this.backdrop.filters = []
    }
    // **약하게.** 뒤가 무엇인지는 알아볼 수 있어야 합니다 — 판을 닫고 어디로 돌아가는지가
    // 보이지 않으면 판이 화면을 갈아치운 것으로 보입니다.
    if (on) {
      this.blur.strength = this.blurShown * 3
      this.blurBack.strength = this.blurShown * 2
    }
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
    return this.started && !this.player.busy && this.playedViews.length === 0
      && this.deals.length === 0 && !this.coins.busy
  }

  private jolt(shake: number, chroma: number, pulse = 0): void {
    // 흔들림과 색수차는 **꺼 둘 수 있습니다.** 배경이 밝아지는 것은 남깁니다 — 그것이
    // 없으면 큰 값이 온 것을 알릴 채널이 하나도 없습니다.
    if (this.settings.shake) this.shake = Math.max(this.shake, shake)
    if (this.settings.chromatic) {
      this.punch.hit(Math.min(chroma, this.feel.chromaticMaxPx * 2))
    }
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
    // **배경의 빠르기는 천천히 따라갑니다.** 점수가 한 박자에 크게 뛰므로 그대로 먹이면
    // 블라인드가 그대로인데도 배경이 휘리릭 돕니다.
    this.heatShown += (this.heat() - this.heatShown) * Math.min(1, seconds * 0.9)
    this.background.setHeat(this.heatShown)
    this.particles.advance(seconds)

    for (const slot of [this.score, this.chips, this.mult, this.money]) slot.advance(deltaMs)
    this.advanceMerge(deltaMs)

    this.updateHover()
    this.updateHandHover()

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
    for (let i = this.burning.length - 1; i >= 0; i--) {
      const view = this.burning[i]
      view.advance(seconds, this.clock)
      if (!view.gone) continue
      view.destroy()
      this.burning.splice(i, 1)
    }

    // 연출이 끝난 순간에 한 번 다시 그립니다. **그때가 다음 국면의 화면을 띄울 때입니다.**
    const busyNow = !this.presented
    if (this.wasBusy && !busyNow) {
      this.settleShown()
      this.refresh()
    }
    this.wasBusy = busyNow

    this.advanceHeadline(seconds)
    this.advanceChimes()
    this.advanceSlams()
    this.advanceFades()
    this.advanceDeals()

    // 덱은 판이 도는 동안만 자리에 있습니다.
    // **판이 도는 동안만 자리에 있습니다.** 블라인드를 고르는 중에도 아직 없습니다 —
    // 시작을 누르면 오른쪽에서 들어옵니다.
    const away = this.state.phase !== 'round'
    this.deckSlide.target = away ? 300 : 0
    this.deckSlide.advance(seconds)
    this.deckLayer.x = this.deckSlide.value
    this.deckLayer.visible = this.deckSlide.value < 296
    this.advanceGameOver(seconds)
    this.title.advance(seconds)
    this.modals.advance(seconds)

    // 블라인드 판이 들어오는 동안 매 프레임 다시 그립니다.
    if (this.state.phase === 'blind-select' && this.blindEnter < 1) {
      this.blindEnter = Math.min(1, this.blindEnter + seconds * 1.5)
      this.drawBlindPick()
    } else if (this.state.phase !== 'blind-select') {
      this.blindEnter = 0
      this.blindShown = -1
    }
    this.advanceBlur(seconds)

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
    return Math.max(0.08, Math.min(1, this.shown.score / Number(this.state.target)))
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
    const blocked = this.modals.busy || this.state.pack !== null
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
      ? this.shown.score / Number(this.state.target) : 0

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
      // **자리가 규칙입니다.** 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터이므로,
      // 자리를 바꾸는 것이 되는지는 이 두 줄로만 확인할 수 있습니다.
      handOrder: state.hand.slice(),
      jokerOrder: state.jokers.map(joker => joker.uid),
      // **판을 끝까지 두는 도구를 위한 손잡이입니다.** 사람이 보라고 넣은 뜸이 도구에게는
      // 기다림일 뿐이고, 그 기다림이 실행 시간의 대부분입니다. 옵션의 속도와 같은 값입니다.
      hurry: (times: number) => { this.player.base = times },
      // **개발 서버에서만 있습니다.** 자리를 바꾸는 것이 되는지 보려면 조커가 둘 있어야
      // 하는데, 그것을 사려고 판을 열 판 두는 동안 확인하려던 것과 상관없는 곳에서 도구가
      // 멈춥니다. 구운 것에는 이 줄이 들어가지 않습니다.
      ...(import.meta.env.DEV ? {
        grantJoker: (count: number) => {
          const rows = this.data.tables.joker.records
          for (let i = 0; i < count && i < rows.length; i++) {
            this.state.jokers.push({
              uid: this.state.nextUid++,
              jokerId: rows[i].jokerId,
              edition: 0 as never,
              sticker: 0 as never,
              counters: newCounters(),
              age: 0,
              disabled: false,
            })
          }
          this.refresh()
        },
        grantConsumable: (count: number) => {
          const rows = this.data.tables.tarot.records
          for (let i = 0; i < count && i < rows.length; i++) {
            this.state.consumables.push({
              uid: this.state.nextUid++,
              kind: 1 as never,
              id: rows[i].tarotId,
              edition: 0 as never,
            })
          }
          this.refresh()
        },
      } : {}),
      busy: this.player.busy || !this.score.settled || this.coins.busy,
      // **화면이 주장하는 패입니다.** 도구가 눌러야 하는 것은 지금 그려져 있는 카드입니다.
      hand: this.shown.hand.map(uid => {
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

    this.money.target = this.shown.money
    this.score.target = this.shown.score
    this.hands.text = String(state.handsLeft)
    this.discards.text = String(state.discardsLeft)
    this.anteSlot.text = `${state.ante} / ${this.data.run.winAnte}`
    this.deckLabel.text = `덱  ${state.drawPile.length} / ${state.deck.length}`
    this.jokerCount.text = `${state.jokers.length} / ${state.rules.jokerSlots}`
    this.consumableCount.text =
      `${state.consumables.length} / ${state.rules.consumableSlots}`

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
    // 떠 있는 판만 다시 그립니다. **닫힌 판을 그리는 것은 낭비이고**, 남은 카드는 덱
    // 52장을 매번 만듭니다.
    if (this.modals.has(this.handList)) this.drawHandList()
    if (this.modals.has(this.deckView)) this.drawDeckView()
    this.drawBlindPick()
    // 다시 시작하면 판을 걷습니다. **띄우는 것은 `tick` 이 합니다** — 연출이 끝난 뒤여야
    // 하기 때문입니다.
    if (this.state.phase !== 'lost' && this.state.phase !== 'won') this.drawGameOver()
    // **국면을 말하는 글은 연출이 끝난 뒤에 바뀝니다.** 코어는 액션 하나를 끝까지 처리해
    // 두므로, 그대로 그리면 아직 득점을 보고 있는데 상점의 지시문이 떠 있습니다.
    // **지난 지시문을 붙잡아 두지 않습니다.** 이미 낸 뒤에 「5장 골랐습니다」가 남아 있으면
    // 무엇을 하라는 말인지가 아니라 무엇을 했었는지가 됩니다.
    this.drawHint(this.presented ? this.hintText() : '')
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
    if (!this.settings.hints) return
    // **핸드가 도는 동안에는 권하지 않습니다.** 득점을 보고 있는데 패의 카드들이 따로
    // 깜빡이면 눈이 둘로 갈리고, 그때는 고를 수도 없습니다.
    if (!this.presented) return

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
  /**
   * 몇 장 골랐는가.
   *
   * **가운데 버튼에 적습니다.** 고른 것을 푸는 자리와 몇 장 골랐는지가 같은 자리에 있으면
   * 눈이 한 번만 갑니다. 아무것도 고르지 않았으면 셀 것이 없으므로 `-` 입니다.
   */
  private drawPips(): void {
    const picked = this.selected.size
    // 켜고 끄는 것은 `syncButtons` 가 정합니다 — 여기는 적는 것만 합니다.
    this.clearButton.text = picked === 0
      ? '-'
      : `${picked} / ${this.data.run.maxPlayedCards}`
  }

  /**
   * 지시문 한 줄.
   *
   * **수와 이름은 다른 색입니다.** 「최대 5장」 · 「남은 핸드 3회」에서 사람이 찾는 것은 그
   * 수이고, 문장과 같은 색이면 문장을 처음부터 읽어야 찾습니다.
   */
  private drawHint(text: string): void {
    if (text === this.hintShown) return
    this.hintShown = text
    this.hint.removeChildren().forEach(child => child.destroy())
    if (text === '') return
    const line = richLine(text, {
      base: { fontSize: 13, fill: 0x9fb0c6, fontWeight: '700' },
      number: COLOR.accentNumber,
      term: COLOR.accentTerm,
    })
    line.position.set(-line.width / 2, 0)
    this.hint.addChild(line)
  }

  /** 지금 국면에서 다음에 할 것. */
  private hintText(): string {
    const state = this.state
    switch (state.phase) {
      // 블라인드 선택에는 지시문을 두지 않습니다. **판마다 자기 버튼에 적혀 있습니다.**
      case 'blind-select': return ''
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
        // 상점은 판 하나이고 그 안에 다 적혀 있습니다.
        return ''
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

  /**
   * 족보 목록.
   *
   * **줄에 마우스를 올리면 그 족보를 카드로 보여 줍니다.** 「투 페어」가 무엇인지는 낱말이
   * 아니라 카드 다섯 장의 모양이고, 그 모양을 본 적이 없으면 이름만으로는 배울 수 없습니다.
   */
  private drawHandList(): void {
    const layer = this.handList.view
    layer.removeChildren().forEach(child => child.destroy())
    this.handRows.length = 0

    const rows = this.data.tables.pokerHand.records
    const width = 540
    const rowH = 36
    const top = TITLE_BAR + 20
    const height = top + rows.length * rowH + 14 + FOOTER_BAR

    layer.addChild(panelFrame(width, height, '족보', () => this.toggleHandList()))

    const band = new Graphics()
    layer.addChild(band)

    rows.forEach((row, index) => {
      const key = PokerHandKind[row.hand]
      const level = this.state.handLevels[key] ?? 1
      const chips = row.baseChips + row.chipsPerLevel * (level - 1)
      const mult = row.baseMult + row.multPerLevel * (level - 1)
      const seen = row.visibleFromStart || (this.state.handPlayCounts[key] ?? 0) > 0
      const y = top + index * rowH

      const name = new Text({
        text: seen ? this.handName(row.hand) : '???',
        style: { fontSize: 15, fill: seen ? COLOR.ink : COLOR.inkDim, fontWeight: '700' },
      })
      name.position.set(28, y + 2)

      const lv = new Text({
        text: `Lv.${level}`,
        style: { fontSize: 13, fill: level > 1 ? COLOR.good : COLOR.inkDim, fontWeight: '700' },
      })
      lv.position.set(246, y + 3)

      const value = new Text({
        text: seen ? `${chips}  ×  ${mult}` : '—',
        style: { fontSize: 15, fill: seen ? COLOR.chips : COLOR.inkDim, fontWeight: '700' },
      })
      value.position.set(318, y + 2)

      const played = new Text({
        text: `${this.state.handPlayCounts[key] ?? 0}회`,
        style: { fontSize: 12, fill: COLOR.inkDim },
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
   * 어느 줄을 가리키고 있는가.
   *
   * **화면이 이미 재고 있는 커서 자리를 씁니다.** 줄마다 사건을 붙이는 것보다 자리 하나를
   * 견주는 편이 확실합니다 — 그리는 것이 없는 통은 저절로 잡히지 않습니다.
   */
  private updateHandHover(): void {
    if (!this.modals.has(this.handList) || this.handRows.length === 0) return

    const local = this.handList.view.toLocal(this.world.toGlobal(this.pointerAt))
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
        band.roundRect(12, row.y - 4, width - 24, row.height - 2, 6)
          .fill({ color: 0x4a6ea8, alpha: 0.42 })
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
    board.roundRect(0, 0, boxW, boxH, 10).fill({ color: 0x0c1320, alpha: 0.98 })
    board.roundRect(0.5, 0.5, boxW - 1, boxH - 1, 10)
      .stroke({ color: 0x6f7f9a, width: 1.5 })
    into.addChild(board)

    if (!shape) {
      const veiled = new Text({
        text: '아직 내 본 적이 없는 족보입니다',
        style: { fontSize: 12, fill: COLOR.inkDim },
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

  /**
   * 블라인드 셋을 한 자리에.
   *
   * **원작의 화면입니다** — 스몰·빅·보스가 나란히 서고, 지금 차례인 것 하나만 앞으로
   * 나옵니다. 이미 넘긴 것은 표시가 붙고, 아직 오지 않은 것은 물러나 있습니다.
   *
   * 건너뛰기가 뜻을 가지려면 **다음에 무엇이 오는지가 보여야 합니다.** 보스의 효과가
   * 그중에서도 가장 중요하고, 그래서 보스 칸에는 무엇을 하는 보스인지가 적힙니다.
   */
  private drawBlindPick(): void {
    this.blindPick.removeChildren().forEach(child => child.destroy())
    const state = this.state
    this.blindPick.visible = state.phase === 'blind-select' && this.presented
    if (!this.blindPick.visible) return

    // 블라인드가 바뀌면 처음부터 다시 들어옵니다.
    if (this.blindShown !== state.blind) {
      this.blindShown = state.blind
      this.blindEnter = 0
    }

    const order = [BlindKind.Small, BlindKind.Big, BlindKind.Boss]
    const cardW = 226
    const gap = 20
    const cardH = 322
    // **아래에 붙입니다.** 조커 줄과 판 사이가 비면 화면이 위로 쏠리고, 판이 서는 자리는
    // 카드를 내는 자리와 같아야 눈이 옮겨 다니지 않습니다.
    const bottom = 754
    const startX = BOARD_X - ((order.length - 1) * (cardW + gap)) / 2 - cardW / 2

    order.forEach((blind, index) => {
      const row = this.data.tables.blind.getByBlindOrThrow(blind)
      const boss = blind === BlindKind.Boss
      const bossRow = boss ? this.data.tables.bossBlind.findByBossId(state.bossId) : undefined
      const now = blind === state.blind
      const done = blind < state.blind
      const tint = boss ? 0x8e3a5c : blind === BlindKind.Big ? 0x8a6a2e : 0x2f6a52

      const group = new Container()
      // 지금 차례인 것만 앞으로 나옵니다. **아랫변을 맞춥니다** — 위로 자라면 줄이
      // 들쭉날쭉해 보입니다.
      const height = cardH + (now ? 26 : 0)

      // 왼쪽부터 차례로 아래에서 올라옵니다. 셋이 같이 나타나면 셋이 한 덩어리로 보입니다.
      const enter = Math.max(0, Math.min(1, (this.blindEnter - index * 0.16) / 0.44))
      const eased = 1 - Math.pow(1 - enter, 3)
      group.position.set(startX + index * (cardW + gap),
        bottom - height + (1 - eased) * 70)
      group.alpha = (now ? 1 : done ? 0.5 : 0.72) * eased

      const plate = new Graphics()
      plate.roundRect(0, 0, cardW, height, 14)
        .fill({ color: now ? 0x18202e : 0x141a24, alpha: 0.97 })
      plate.roundRect(0.5, 0.5, cardW - 1, height - 1, 14)
        .stroke({ color: now ? tint : COLOR.panelEdge, width: now ? 3 : 1.5 })
      // 머리 띠. 어느 블라인드인지가 색으로 먼저 읽힙니다.
      plate.roundRect(0, 0, cardW, 46, 14).fill({ color: tint, alpha: now ? 0.95 : 0.6 })
      plate.rect(0, 34, cardW, 12).fill({ color: tint, alpha: now ? 0.95 : 0.6 })
      group.addChild(plate)

      const label = (text: string, size: number, fill: number, weight = '700') =>
        new Text({ text, style: { fontSize: size, fill, fontWeight: weight as never } })

      const name = label(bossRow?.name ?? `${blindName(blind)} 블라인드`, 17, COLOR.ink, '800')
      name.anchor.set(0.5, 0.5)
      name.position.set(cardW / 2, 23)
      group.addChild(name)

      const need = label(String(targetOf(this.data, state, blind)), 34, COLOR.chips, '800')
      need.anchor.set(0.5, 0)
      need.position.set(cardW / 2, 72)
      group.addChild(need)

      const needCaption = label('요구 점수', 11, COLOR.inkDim)
      needCaption.anchor.set(0.5, 0)
      needCaption.position.set(cardW / 2, 114)
      group.addChild(needCaption)

      const reward = label(`격파 보상  $${row.reward}`, 13, COLOR.money, '800')
      reward.anchor.set(0.5, 0)
      reward.position.set(cardW / 2, 138)
      group.addChild(reward)

      // 보스의 효과. **건너뛸지를 정하는 것이 대부분 이 한 줄입니다.**
      const note = bossRow
        ? describe(this.data, this.data.bossEffects.get(state.bossId) ?? []).join(NEWLINE)
        : '아무 규칙도 걸리지 않습니다'
      // **수와 이름은 다른 색입니다.** 「패에서 2장을 버립니다」에서 판단을 가르는 것은
      // 그 2 입니다.
      const noteLines = note.split(NEWLINE)
      const noteText = new Container()
      noteLines.forEach((one, line) => {
        const drawn = richLine(one, {
          base: { fontSize: 12, fill: boss ? 0xffb4c8 : COLOR.inkDim },
          number: COLOR.accentNumber,
          term: COLOR.accentTerm,
        })
        drawn.position.set(-drawn.width / 2, line * 17)
        noteText.addChild(drawn)
      })
      noteText.position.set(cardW / 2, 172)
      group.addChild(noteText)

      if (done) {
        const mark = label('넘겼습니다', 14, COLOR.good, '800')
        mark.anchor.set(0.5, 0)
        mark.position.set(cardW / 2, height - 52)
        group.addChild(mark)
      } else if (!now) {
        const mark = label('다음 차례', 13, COLOR.inkDim, '700')
        mark.anchor.set(0.5, 0)
        mark.position.set(cardW / 2, height - 50)
        group.addChild(mark)
      } else {
        const pick = new Button('이 블라인드로', cardW - 36, 44, 0x2f6fb5,
          () => this.act({ t: 'select_blind' }))
        pick.position.set(18, height - 106)
        group.addChild(pick)

        // 보스는 건너뛸 수 없습니다.
        if (row.skippable && blind !== BlindKind.Boss) {
          const skip = new Button('건너뛴다 — 태그', cardW - 36, 36, 0x4a5568,
            () => this.act({ t: 'skip_blind' }))
          skip.position.set(18, height - 52)
          group.addChild(skip)
        }
      }

      this.blindPick.addChild(group)
    })
  }

  /**
   * 덱에 남은 카드.
   *
   * **덱을 그대로 펼칩니다.** 숫자로 세어 놓으면 「스페이드가 4장」은 읽히지만 그것이 어느
   * 4장인지는 읽히지 않고, 강화가 붙은 카드가 아직 남았는지는 아예 보이지 않습니다.
   *
   * 무늬마다 한 줄이고, 카드는 **옆으로 겹쳐** 놓입니다 — 겹치면 한 장을 크게 그리고도
   * 13장이 한 줄에 들어가고, 겹친 쪽이 손에 쥔 부챗살과 같은 모습입니다. 아직 뽑지 않은
   * 것만 밝게 두어 남은 것이 무엇인지가 한눈에 갈립니다.
   *
   * 카드를 누르면 그 한 장의 설명이 뜹니다. 강화와 인장과 에디션은 얼굴의 색과 점 하나로만
   * 구분되므로, **누르면 글로 읽을 수 있어야 합니다.**
   */
  private drawDeckView(): void {
    const layer = this.deckView.view
    layer.removeChildren().forEach(child => child.destroy())

    const state = this.state
    const suits = [...this.data.tables.suit.records].sort((a, b) => a.sortOrder - b.sortOrder)
    const alive = new Set(state.drawPile)
    const held = new Set(this.shown.hand)

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

    layer.addChild(panelFrame(width, height, '남은 카드', () => this.toggleDeckView()))

    const label = (text: string, size: number, fill: number, weight = '700') =>
      new Text({ text, style: { fontSize: size, fill, fontWeight: weight as never } })

    const total = label(`${state.drawPile.length} / ${state.deck.length}`, 15, COLOR.chips, '800')
    total.anchor.set(0.5, 0)
    total.position.set(width / 2, TITLE_BAR + 12)
    const legend = label(
      '밝은 것이 덱에 남은 카드입니다. 카드를 누르면 그 한 장의 설명이 뜹니다.',
      11, COLOR.inkDim, '600')
    legend.anchor.set(0.5, 0)
    legend.position.set(width / 2, TITLE_BAR + 34)
    layer.addChild(total, legend)

    rows.forEach((row, line) => {
      const y = gridTop + line * rowH
      const red = row.suit.suit === SuitKind.Heart || row.suit.suit === SuitKind.Diamond
      const leftIn = row.cards.filter(card => alive.has(card.uid)).length

      const mark = label(SUIT_PIP[row.suit.suit] ?? row.suit.letter, 26,
        red ? COLOR.red : COLOR.ink, '800')
      mark.anchor.set(0.5, 0)
      mark.position.set(34, y + 16)
      const count = label(`${leftIn}/${row.cards.length}`, 11, COLOR.inkDim, '700')
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
          this.showCardTip(card, alive.has(card.uid), held.has(card.uid), mini)
        })
        layer.addChild(mini)
      })
    })

    layer.sortableChildren = true

    const remaining = state.deck.filter(card => alive.has(card.uid))
    const faces = remaining.filter(
      card => this.data.tables.rank.findByRank(card.rank)?.isFace).length
    const aces = remaining.filter(card => card.rank === RankKind.Ace).length
    const enhanced = remaining.filter(card => card.enhancement !== EnhancementKind.None).length
    const sealed = remaining.filter(card => card.seal !== SealKind.None).length

    const foot = label(
      `남은 것 중  그림 ${faces}   ·   에이스 ${aces}   ·   강화 ${enhanced}   ·   인장 ${sealed}`,
      12, COLOR.inkDim)
    foot.anchor.set(0.5, 1)
    foot.position.set(width / 2, height - FOOTER_BAR - 10)
    layer.addChild(foot)

    this.deckView.size.width = width
    this.deckView.size.height = height
  }

  /**
   * 덱 판에 놓이는 카드 한 장.
   *
   * **손패의 카드와 같은 그림입니다.** 작다고 다른 얼굴을 쓰면 판을 보고 손패를 찾을 때
   * 한 번 더 옮겨 읽어야 합니다.
   */
  private miniCard(card: CardInstance, alive: boolean, w: number, h: number): Container {
    const node = new Container()
    const red = card.suit === SuitKind.Heart || card.suit === SuitKind.Diamond
    const paint = MINI_TINT[card.enhancement] ?? COLOR.cardFace

    // **나간 카드도 불투명합니다.** 반투명하면 뒤의 카드가 비쳐 겹친 자리가 지저분해지고,
    // 겹쳐 놓은 줄에서는 그 자리가 카드마다 다릅니다 — 어둡게만 두면 깔끔합니다.
    const body = new Graphics()
    body.roundRect(0, 0, w, h, 5).fill(alive ? COLOR.cardFace : 0x39414f)
    body.roundRect(0.5, 0.5, w - 1, h - 1, 5)
      .stroke({ color: alive ? COLOR.cardEdge : 0x2a3140, width: 1 })
    node.addChild(body)

    const ink = alive ? (red ? COLOR.red : COLOR.black) : 0x5d6879
    const texture = artFor('card', cardArtId(card.suit, card.rank))
    if (texture) {
      const picture = new Sprite(texture)
      picture.width = w
      picture.height = h
      picture.tint = alive ? paint : 0x4c5566
      node.addChild(picture)
    } else {
      const face = new Graphics()
      drawFace(face, card.suit, card.rank, w, h, ink)
      const rank = new Text({
        text: MINI_RANK[card.rank] ?? '?',
        style: { fontSize: 11, fill: ink, fontWeight: '800' },
      })
      rank.position.set(3, 1)
      node.addChild(face, rank)
    }

    if (card.seal !== SealKind.None) {
      const seal = new Graphics()
      seal.circle(w - 9, 9, 4.5)
        .fill({ color: MINI_SEAL[card.seal] ?? COLOR.ink, alpha: alive ? 1 : 0.4 })
      node.addChild(seal)
    }
    if (card.edition !== EditionKind.Base) {
      const spark = new Graphics()
      spark.roundRect(3, h - 8, w - 6, 4, 2)
        .fill({ color: COLOR.mult, alpha: alive ? 0.9 : 0.3 })
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
  private showCardTip(card: CardInstance, alive: boolean, inHand: boolean,
                      at: Container): void {
    const rank = this.data.tables.rank.findByRank(card.rank)
    const name = `${MINI_RANK[card.rank] ?? '?'} ${SUIT_PIP[card.suit] ?? ''}`

    const lines: string[] = [
      alive ? '덱에 남아 있습니다' : inHand ? '지금 손에 있습니다' : '이미 나갔습니다',
      `칩 ${(rank?.chips ?? 0) + card.bonusChips}`
        + (card.bonusChips > 0 ? ` — 기본 ${rank?.chips ?? 0} + 덤 ${card.bonusChips}` : ''),
    ]
    if (card.enhancement !== EnhancementKind.None) {
      lines.push(`강화 — ${this.enhancementName(card.enhancement)}`)
    }
    if (card.seal !== SealKind.None) lines.push(`인장 — ${this.sealName(card.seal)}`)
    if (card.edition !== EditionKind.Base) {
      lines.push(`에디션 — ${this.editionName(card.edition)}`)
    }
    if (card.debuffed) lines.push('이번 라운드에는 무력화되어 있습니다')

    const spot = this.world.toLocal(at.getGlobalPosition())
    this.tooltip.show(name, alive ? '덱' : inHand ? '손' : '나감', alive ? 3 : 1,
      lines, spot.x + 30, spot.y + 46, { width: SIZE.width, height: SIZE.height })
  }

  /**
   * 표시 이름 셋.
   *
   * **표의 `display` 를 거쳐 글 표로 갑니다** — 이름을 화면에 손으로 적으면 지역화가 그
   * 자리를 지나칩니다.
   */
  private enhancementName(kind: EnhancementKind): string {
    return this.localized(this.data.tables.enhancement.findByEnhancement(kind)?.display)
      ?? EnhancementKind[kind]
  }

  private sealName(kind: SealKind): string {
    return this.localized(this.data.tables.seal.findBySeal(kind)?.display) ?? SealKind[kind]
  }

  private editionName(kind: EditionKind): string {
    return this.localized(this.data.tables.edition.findByEdition(kind)?.display)
      ?? EditionKind[kind]
  }

  /**
   * 규칙 하나의 이름.
   *
   * **글 표에서 옵니다.** `RuleKind` 의 이름은 `AllCardsScore` 같은 식별자이고, 그것이
   * 화면에 그대로 뜨면 무엇이 바뀐 것인지 읽을 수 없습니다.
   */
  private ruleName(rule: string): string {
    return this.data.tables.stringTable.findByStringId(`rule.${snake(rule)}.name`)?.ko ?? rule
  }

  /** 글 표에 있으면 그 말, 없으면 적힌 그대로. */
  private localized(key: string | undefined): string | undefined {
    if (key === undefined || key === '') return undefined
    return this.data.tables.stringTable.findByStringId(key)?.ko ?? key
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
    // **판의 한가운데입니다.** 왼쪽 패널을 뺀 나머지의 가운데 — 화면의 가운데에 두면
    // 그동안 카드가 서 있던 자리에서 비껴 나타납니다.
    board.position.set(BOARD_X, SIZE.height / 2)
    this.gameOver.addChild(board)
    this.gameOverBoard = board
    this.gameOver.zIndex = 10_000

    // 럼블. **판이 그냥 나타나면 아무 무게가 없습니다.**
    this.audio.play(won ? 'run_win' : 'run_lose')
    this.jolt(won ? 22 : 16, won ? 3.4 : 2.6, 1)
    this.flashScreen(won ? COLOR.money : COLOR.bad, won ? 0.5 : 0.34)
    if (won) this.particles.burst(BOARD_X, SIZE.height / 2, 90, COLOR.money, 2.6)
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
      BOARD_X + (Math.random() - 0.5) * shiver,
      SIZE.height / 2 + (Math.random() - 0.5) * shiver)
    board.rotation = (Math.random() - 0.5) * shiver * 0.0022

    if (this.gameOverPop <= 0) {
      board.scale.set(1)
      board.position.set(BOARD_X, SIZE.height / 2)
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

    // **가운데 버튼이 없습니다.** 블라인드 선택은 판마다 자기 버튼을 가지고, 상점은 판의
    // 밑단에 자기 버튼을 가집니다 — 어느 쪽이든 누를 것이 그 판 안에 있습니다.
    this.primaryButton.visible = false
    this.skipButton.visible = false
    this.sortRankButton.visible = inRound
    this.sortSuitButton.visible = inRound
    const playing = state.phase !== 'lost' && state.phase !== 'won'
    this.infoButton.visible = playing
    this.guideButton.visible = playing
    this.optionButton.visible = playing
    // 남은 카드는 판이 도는 동안만 뜻이 있습니다.
    this.deckButton.visible = playing && this.state.phase === 'round'
    if (!this.deckButton.visible) this.modals.close(this.deckView)
    // 리롤도 상점 판의 밑단에 있습니다.
    this.rerollButton.visible = false
  }

  private syncCards(): void {
    // **화면이 주장하는 패입니다.** 다음 패는 득점 연출이 끝난 뒤에 깔립니다.
    const wanted = new Set(this.shown.hand)

    for (const [uid, view] of this.cards) {
      if (!wanted.has(uid)) {
        view.destroy()
        this.cards.delete(uid)
      }
    }

    const hand = this.shown.hand
      .map(uid => this.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    const spacing = Math.min(SIZE.cardWidth + 12, 720 / Math.max(1, hand.length))
    const startX = BOARD_X - ((hand.length - 1) * spacing) / 2
    this.handSpots = { startX, spacing }

    hand.forEach((card, index) => {
      let view = this.cards.get(card.uid)
      const fresh = view === undefined

      if (!view) {
        view = new CardView(card, this.editionLook(card.edition))
        view.eventMode = 'static'
        view.cursor = 'pointer'
        // **누르기와 끌기가 한 손가락에 얹힙니다.** 뗄 때까지 움직이지 않았으면 고른
        // 것이고, 움직였으면 자리를 옮긴 것입니다 — `pointertap` 은 이 둘을 갈라 주지
        // 않아서 끌고 나서도 골라 버립니다.
        view.on('pointerdown', () => this.beginDrag('hand', card.uid, view as CardView))
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
      const spotX = startX + index * spacing
      const spotY = HAND_Y + offset * offset * 1.1
      const tilt = offset * 2.2
      // 끌고 있는 카드는 손가락이 자리를 정합니다. 여기서 다시 놓으면 커서에서 떨어집니다.
      if (this.drag?.kind === 'hand' && this.drag.uid === card.uid && this.drag.moved) return
      // 갓 뽑힌 카드는 **절도 있게** 자리에 붙고, 나머지는 부드럽게 자리를 옮깁니다.
      if (fresh) view.deal(spotX, spotY, tilt)
      else view.place(spotX, spotY, tilt)
    })
  }

  private syncJokers(): void {
    const wanted = new Set(this.state.jokers.map(joker => joker.uid))

    for (const [uid, view] of this.jokers) {
      if (wanted.has(uid)) continue
      // **곧바로 지우지 않습니다.** 타서 사라지는 것이 보여야 무엇이 없어진 것인지
      // 눈이 따라갑니다. 다 타면 `tick` 이 치웁니다.
      view.ignite()
      this.jokers.delete(uid)
      this.burning.push(view)
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
        view.on('pointerdown', () => this.beginDrag('joker', joker.uid, view as JokerView))
        this.jokers.set(joker.uid, view)
        this.board.addChild(view)
        // 위에서 내려옵니다. **그리는 자리도 함께 옮깁니다** — 용수철만 옮기면 한
        // 프레임 동안 화면 왼쪽 위에 서 있습니다.
        const from = JOKER_X + index * (SIZE.jokerWidth + 12)
        view.motion.snap(from, JOKER_Y - 160)
        view.position.set(from, JOKER_Y - 160)
      } else {
        view.set(joker, look)
      }

      if (this.drag?.kind === 'joker' && this.drag.uid === joker.uid && this.drag.moved) return
      const lifted = this.held?.kind === 'joker' && this.held.uid === joker.uid ? 12 : 0
      view.place(JOKER_X + index * (SIZE.jokerWidth + 12), JOKER_Y - lifted)
    })

    this.syncHeldBar()
  }

  /**
   * 끌기를 시작합니다.
   *
   * 아직 끄는 것인지 누르는 것인지 모릅니다 — 손가락이 몇 px 움직이고 나서야 갈립니다.
   */
  private beginDrag(kind: 'hand' | 'joker', uid: number, view: Container): void {
    if (this.player.busy || this.modals.busy) return
    this.drag = {
      kind, uid, moved: false,
      startX: this.pointerAt.x, startY: this.pointerAt.y,
      grabX: this.pointerAt.x - view.x,
    }
  }

  /**
   * 끄는 동안.
   *
   * **자리를 바로바로 바꿉니다** — 손을 뗀 뒤에 한 번에 정리하면 어디에 놓이는 것인지
   * 모르는 채로 끌게 됩니다.
   */
  private advanceDrag(): void {
    const drag = this.drag
    if (!drag) return

    if (!drag.moved) {
      const far = Math.abs(this.pointerAt.x - drag.startX) > 6
        || Math.abs(this.pointerAt.y - drag.startY) > 6
      if (!far) return
      drag.moved = true
      this.audio.play('card_select', -4)
    }

    const x = this.pointerAt.x - drag.grabX
    const order = drag.kind === 'hand'
      ? this.state.hand
      : this.state.jokers.map(joker => joker.uid)
    const current = order.indexOf(drag.uid)
    if (current < 0) return

    const spacing = drag.kind === 'hand'
      ? this.handSpots.spacing : SIZE.jokerWidth + 12
    const startX = drag.kind === 'hand' ? this.handSpots.startX : JOKER_X
    const target = Math.max(0, Math.min(order.length - 1,
      Math.round((x - startX) / Math.max(1, spacing))))

    if (target !== current) {
      if (drag.kind === 'hand') {
        const next = this.state.hand.slice()
        next.splice(target, 0, ...next.splice(current, 1))
        this.state.hand = next
      } else {
        const next = this.state.jokers.slice()
        next.splice(target, 0, ...next.splice(current, 1))
        this.state.jokers = next
      }
      this.audio.play('card_select', target * 2)
      this.refresh()
    }

    // 끌리는 것은 커서를 따라오고 조금 들립니다. **다른 것들 위에 있어야** 어디로 가는지
    // 보입니다.
    const view = drag.kind === 'hand'
      ? this.cards.get(drag.uid) : this.jokers.get(drag.uid)
    if (view) {
      this.board.setChildIndex(view, this.board.children.length - 1)
      view.place(x, (drag.kind === 'hand' ? HAND_Y : JOKER_Y) - 22, 0)
    }
  }

  /** 손을 뗍니다. 움직이지 않았으면 끈 것이 아니라 누른 것입니다. */
  private endDrag(): void {
    const drag = this.drag
    this.drag = undefined
    if (!drag) return

    if (!drag.moved) {
      if (drag.kind === 'hand') this.toggle(drag.uid)
      else this.pick('joker', drag.uid)
      return
    }
    this.audio.play('card_place')
    this.refresh()

    // **겹치는 차례도 되돌립니다.** 끄는 동안 맨 위로 올렸으므로, 그대로 두면 놓은 카드가
    // 이웃을 계속 가려 부챗살이 한 장만 어긋나 보입니다.
    if (drag.kind === 'hand') {
      for (const uid of this.state.hand) {
        const view = this.cards.get(uid)
        if (view) this.board.addChild(view)
      }
    } else {
      for (const joker of this.state.jokers) {
        const view = this.jokers.get(joker.uid)
        if (view) this.board.addChild(view)
      }
    }
  }

  /** 조커나 소모품 하나를 고릅니다. 같은 것을 다시 누르면 놓습니다. */
  private pick(kind: 'joker' | 'consumable', uid: number): void {
    if (this.player.busy) return
    this.held = this.held?.kind === kind && this.held.uid === uid
      ? undefined : { kind, uid }
    this.audio.play('card_select')
    this.refresh()
  }

  /**
   * 고른 것 밑의 버튼들.
   *
   * **고른 자리 바로 밑입니다.** 화면 구석에 두면 무엇에 대한 버튼인지가 끊깁니다.
   */
  private syncHeldBar(): void {
    this.heldBar.removeChildren().forEach(child => child.destroy())
    const held = this.held
    if (!held) return

    let anchor = 0
    const buttons: Button[] = []

    if (held.kind === 'joker') {
      const index = this.state.jokers.findIndex(joker => joker.uid === held.uid)
      if (index < 0) {
        this.held = undefined
        return
      }
      anchor = JOKER_X + index * (SIZE.jokerWidth + 12)
      const price = sellValueOf(this.data, this.state, this.state.jokers[index])
      buttons.push(new Button(`판매 $${price}`, 92, 30, 0x7a3f4a, () => {
        this.held = undefined
        this.act({ t: 'sell_joker', index })
      }))
    } else {
      const index = this.state.consumables.findIndex(item => item.uid === held.uid)
      if (index < 0) {
        this.held = undefined
        return
      }
      anchor = CONSUMABLE_X + index * (SIZE.jokerWidth + 12)
      buttons.push(new Button('사용', 68, 30, 0x3f5f8a, () => {
        this.held = undefined
        this.act({ t: 'use_consumable', index, targets: this.orderedSelection() })
      }))
      buttons.push(new Button(`판매 $${this.data.economy.sellMin}`, 92, 30, 0x7a3f4a, () => {
        this.held = undefined
        this.act({ t: 'sell_consumable', index })
      }))
    }

    const gap = 8
    const span = buttons.reduce((sum, one) => sum + one.width, 0) + gap * (buttons.length - 1)
    let x = anchor - span / 2
    for (const button of buttons) {
      button.position.set(x, JOKER_Y + SIZE.jokerHeight / 2 + 10)
      x += button.width + gap
      this.heldBar.addChild(button)
    }
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

      // **조커와 같은 카드입니다.** 나란히 선 줄에서 하나만 다른 모양이면 갈래가 다른
      // 물건으로 보이고, 실제로는 둘 다 손에 든 카드입니다.
      const tile = new Container()
      tile.position.set(
        CONSUMABLE_X + index * (SIZE.jokerWidth + 12) - SIZE.jokerWidth / 2,
        JOKER_Y - SIZE.jokerHeight / 2)

      tile.addChild(this.faceCard({
        kind: (item.kind === 1 ? ShopItemKind.Tarot
          : item.kind === 2 ? ShopItemKind.Planet : ShopItemKind.Spectral) as ShopItemKind,
        id: item.id,
        cost: 0,
        edition: item.edition as never,
      } as ShopItem))
      tile.hitArea = new Rectangle(0, 0, SIZE.jokerWidth, SIZE.jokerHeight)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      // **누르면 고르는 것입니다.** 쓰는 것과 파는 것은 그 밑에 선 버튼이 합니다 —
      // 소모품 하나가 판을 바꾸므로, 실수로 눌러 써 버리면 되돌릴 수 없습니다.
      tile.on('pointertap', () => this.pick('consumable', item.uid))
      if (this.held?.kind === 'consumable' && this.held.uid === item.uid) tile.y -= 12
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

  /**
   * 상점.
   *
   * **판 하나입니다.** 물건이 화면 여기저기에 흩어져 있으면 무엇이 한 벌인지 · 무엇을 먼저
   * 보아야 하는지가 읽히지 않습니다. 다른 판들과 같은 머리와 밑단을 쓰고, 안쪽은 줄 셋으로
   * 나뉩니다 — 살 것 · 뜯을 것 · 런 내내 남을 것.
   *
   * **닫히지 않습니다.** 닫으면 갈 곳이 없으므로 밑단에는 닫기 대신 리롤과 다음 블라인드가
   * 놓입니다.
   */
  private syncShop(): void {
    this.shopLayer.removeChildren().forEach(child => child.destroy())
    this.shopLayer.visible = this.state.phase === 'shop' && this.presented
    if (!this.shopLayer.visible) return

    const state = this.state
    const width = 780

    // **자리를 세어 가며 쌓습니다.** 높이를 못박으면 물건이 하나 늘거나 줄 때마다 아래가
    // 넘치거나 비고, 그 둘은 눈에 곧바로 보입니다.
    const ITEM_H = SIZE.jokerHeight + 52
    const PACK_H = SIZE.jokerHeight + 34
    const VOUCHER_H = 68
    const HEAD = 26
    const GAP = 20

    const itemHead = TITLE_BAR + 14
    const itemsAt = itemHead + HEAD
    const packHead = itemsAt + ITEM_H + GAP
    const packsAt = packHead + HEAD
    const voucherHead = packsAt + PACK_H + GAP
    const voucherAt = voucherHead + HEAD
    const height = voucherAt + VOUCHER_H + 14 + FOOTER_BAR

    const x = BOARD_X - width / 2
    const y = Math.max(84, (SIZE.height - height) / 2 - 24)

    const foot = new Container()
    const reroll = new Button(`리롤  $${rerollCost(this.data, state, state.shop)}`,
      150, 40, 0x3f5f8f, () => this.reroll())
    reroll.enabled = state.money >= rerollCost(this.data, state, state.shop)
    const leave = new Button('다음 블라인드로', 190, 40, 0x2f6fb5, () => this.primary())
    leave.position.set(166, 0)
    foot.addChild(reroll, leave)

    const frame = panelFrame(width, height, '상점', undefined, foot)
    frame.position.set(x, y)
    this.shopLayer.addChild(frame)

    const money = new Text({
      text: `$${this.shown.money}`,
      style: { fontSize: 19, fill: COLOR.money, fontWeight: '800' },
    })
    money.anchor.set(1, 0.5)
    money.position.set(x + width - 26, y + TITLE_BAR / 2)
    this.shopLayer.addChild(money)

    this.shopSection(x, y + itemHead, width, '상품')
    this.drawShopItems(x, y + itemsAt, width, ITEM_H)

    this.shopSection(x, y + packHead, width, '팩')
    this.drawPackRow(x, y + packsAt, width, PACK_H)

    this.shopSection(x, y + voucherHead, width, '바우처')
    this.drawVoucher(x, y + voucherAt, width, VOUCHER_H)
  }

  /** 줄의 이름표. **셋을 가르는 것이 이 한 줄입니다.** */
  private shopSection(x: number, y: number, width: number, title: string): void {
    const label = new Text({
      text: title,
      style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '800', letterSpacing: 1 },
    })
    label.position.set(x + 26, y)

    const rule = new Graphics()
    rule.rect(x + 26 + label.width + 12, y + 8, width - 64 - label.width - 12, 1)
      .fill({ color: COLOR.panelEdge, alpha: 0.8 })

    this.shopLayer.addChild(rule, label)
  }

  /**
   * 살 것들.
   *
   * **줄에 서는 것은 카드입니다.** 아이콘을 얹은 딱지로 두면 살 때와 산 뒤의 모습이 달라
   * 같은 물건으로 보이지 않습니다 — 상점에 선 그 카드가 그대로 조커 줄에 섭니다.
   */
  private drawShopItems(left: number, top: number, width: number, tileH: number): void {
    const slots = this.state.shop.cards
    const tileW = 158
    const gap = 14
    const span = slots.length * tileW + Math.max(0, slots.length - 1) * gap
    const startX = left + (width - span) / 2

    slots.forEach((item, slot) => {
      const name = shopLabel(item.kind, item.id, this.data)
      const lines = this.shopLines(item)
      const rarity = item.kind === ShopItemKind.Joker
        ? this.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0
      const afford = this.shown.money >= item.cost
      const room = this.roomFor(item.kind)

      const tile = new Container()
      tile.position.set(startX + slot * (tileW + gap), top)

      const card = this.itemCard(item)
      card.position.set((tileW - SIZE.jokerWidth) / 2, 0)
      tile.addChild(card)

      const price = new Text({
        text: `$${item.cost}`,
        style: {
          fontSize: 20, fontWeight: '800',
          fill: afford ? COLOR.money : 0x7a6a45,
          stroke: { color: 0x0a0f18, width: 4 },
        },
      })
      price.anchor.set(0.5, 0)
      price.position.set(tileW / 2, SIZE.jokerHeight + 8)
      tile.addChild(price)

      // **자리가 없으면 그것이 값보다 먼저 읽혀야 합니다.** 눌러 보고 아무 일도 없는 것이
      // 가장 나쁩니다.
      if (!room) {
        const full = new Text({
          text: '자리 없음 — 눌러서 교체',
          style: { fontSize: 10, fill: 0xffb4c8, fontWeight: '800' },
        })
        full.anchor.set(0.5, 0)
        full.position.set(tileW / 2, SIZE.jokerHeight + 34)
        tile.addChild(full)
      }

      tile.alpha = afford ? 1 : 0.55
      tile.eventMode = 'static'
      tile.hitArea = new Rectangle(0, 0, tileW, tileH)
      tile.cursor = afford ? 'pointer' : 'default'
      tile.on('pointertap', () => this.buyFrom(slot, item, tile))
      tile.on('pointerover', () => {
        this.tooltip.show(name, kindName(item.kind), rarity, lines,
          tile.x + tileW / 2, tile.y + tileH, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.shopLayer.addChild(tile)
    })
  }

  /**
   * 상점에 선 물건 하나의 카드.
   *
   * 조커는 **줄에 서는 그 카드 그대로**입니다 — 같은 클래스를 씁니다. 소모품과 플레잉
   * 카드는 같은 크기와 모양의 카드로 그립니다.
   */
  private itemCard(item: ShopItem): Container {
    if (item.kind === ShopItemKind.Joker) {
      const row = this.data.tables.joker.findByJokerId(item.id)
      const view = new JokerView({
        uid: -1, jokerId: item.id, edition: item.edition as never,
        sticker: 0 as never, counters: newCounters(), age: 0, disabled: false,
      }, {
        name: row?.name ?? item.id,
        rarity: row?.rarity ?? 1,
        lines: describe(this.data, this.data.jokerEffects.get(item.id) ?? []),
        edition: this.editionLook(item.edition as EditionKind),
      })
      // 상점의 카드는 흔들리지 않습니다. 줄에 선 것과 달리 고를 것이지 도는 것이 아닙니다.
      view.pivot.set(0, 0)
      view.position.set(0, 0)
      return view
    }

    return this.faceCard(item)
  }

  /**
   * 소모품과 플레잉 카드의 얼굴.
   *
   * 조커와 **같은 크기, 같은 모서리, 같은 이름 띠**입니다 — 상점에 여러 갈래가 서므로
   * 모양이 어긋나면 줄이 흐트러져 보입니다.
   */
  private faceCard(item: ShopItem): Container {
    const w = SIZE.jokerWidth
    const h = SIZE.jokerHeight
    const node = new Container()

    const plate = new Graphics()
    plate.roundRect(3, 5, w, h, 9).fill({ color: 0x000000, alpha: 0.4 })
    plate.roundRect(0, 0, w, h, 9).fill(0x141b26)
    node.addChild(plate)

    const clip = new Graphics()
    clip.roundRect(0, 0, w, h, 9).fill(0xffffff)
    node.addChild(clip)

    if (item.kind === ShopItemKind.PlayingCard) {
      const row = this.data.tables.baseDeckCard.findByCardId(item.id)
      if (row) {
        const texture = artFor('card', cardArtId(row.suit, row.rank))
        if (texture) {
          const picture = new Sprite(texture)
          picture.width = w
          picture.height = h
          node.addChild(picture)
        } else {
          const face = new Graphics()
          face.roundRect(0, 0, w, h, 9).fill(COLOR.cardFace)
          drawFace(face, row.suit, row.rank, w, h,
            row.suit === SuitKind.Heart || row.suit === SuitKind.Diamond
              ? COLOR.red : COLOR.black)
          node.addChild(face)
        }
      }
    } else {
      const kind: ArtKind | undefined = item.kind === ShopItemKind.Tarot ? 'tarot'
        : item.kind === ShopItemKind.Planet ? 'planet'
          : item.kind === ShopItemKind.Spectral ? 'spectral' : undefined
      const texture = kind ? artFor(kind, item.id) : undefined
      if (texture) {
        const sprite = new Sprite(texture)
        const scale = Math.max(w / texture.width, h / texture.height)
        sprite.width = texture.width * scale
        sprite.height = texture.height * scale
        sprite.position.set((w - sprite.width) / 2, (h - sprite.height) / 2)
        sprite.mask = clip
        node.addChild(sprite)
      }
    }

    const tint = item.kind === ShopItemKind.PlayingCard ? COLOR.cardEdge : 0x9b8fd0
    const band = new Graphics()
    band.roundRect(0, h - 26, w, 26, 9).fill({ color: 0x0b1018, alpha: 0.88 })
    band.rect(0, h - 26, w, 17).fill({ color: 0x0b1018, alpha: 0.88 })
    band.rect(0, h - 26, w, 1.5).fill({ color: tint, alpha: 0.9 })
    node.addChild(band)

    const label = new Text({
      text: shopLabel(item.kind, item.id, this.data),
      style: {
        fontSize: 11, fill: COLOR.ink, fontWeight: '800', align: 'center',
        wordWrap: true, wordWrapWidth: w - 8, lineHeight: 12,
      },
    })
    label.anchor.set(0.5, 0.5)
    label.position.set(w / 2, h - 13)
    node.addChild(label)

    const frame = new Graphics()
    frame.roundRect(1.25, 1.25, w - 2.5, h - 2.5, 8).stroke({ color: tint, width: 2.5 })
    node.addChild(frame)

    return node
  }

  /** 그 갈래를 받을 자리가 있는가. */
  private roomFor(kind: ShopItemKind): boolean {
    const state = this.state
    if (kind === ShopItemKind.Joker) return state.jokers.length < state.rules.jokerSlots
    if (kind === ShopItemKind.Tarot || kind === ShopItemKind.Planet
      || kind === ShopItemKind.Spectral) {
      return state.consumables.length < state.rules.consumableSlots
    }
    return true
  }

  /**
   * 상점의 물건 하나를 삽니다.
   *
   * **자리가 없으면 무엇과 바꿀지를 묻습니다.** 그냥 눌리지 않게 두면 왜 안 되는지 알 수
   * 없고, 말없이 파는 것은 되돌릴 수 없는 일을 묻지 않고 하는 것입니다.
   */
  private buyFrom(slot: number, item: ShopItem, tile: Container): void {
    if (this.shown.money < item.cost) return
    if (!this.roomFor(item.kind)) {
      this.tooltip.hide()
      this.askSwap(slot, item)
      return
    }
    this.audio.play('shop_buy')
    this.particles.burst(tile.x + 79, tile.y + 84, 16, COLOR.money, 1)
    this.act({ t: 'buy', slot })
  }

  /**
   * 자리가 없습니다 — 무엇과 바꿀까요.
   *
   * **묻고 나서 팝니다.** 말없이 하나를 팔아 치우면 되돌릴 수 없는 일을 묻지 않고 한
   * 것이고, 그냥 눌리지 않게 두면 왜 안 되는지 알 수 없습니다.
   *
   * 파는 값이 줄마다 적혀 있습니다 — 그것이 무엇을 내놓을지를 정하는 값입니다.
   */
  private askSwap(slot: number, item: ShopItem): void {
    const joker = item.kind === ShopItemKind.Joker
    const rows = joker
      ? this.state.jokers.map((held, index) => {
        const row = this.data.tables.joker.findByJokerId(held.jokerId)
        return {
          index,
          name: row?.name ?? held.jokerId,
          note: describe(this.data, this.data.jokerEffects.get(held.jokerId) ?? [])[0] ?? '',
          price: sellValueOf(this.data, this.state, held),
          // `Eternal` 은 팔리지 않습니다.
          locked: held.sticker === 1,
        }
      })
      : this.state.consumables.map((held, index) => ({
        index,
        name: this.consumableName(held.kind, held.id),
        note: this.consumableLines(held.kind, held.id)[0] ?? '',
        price: this.data.economy.sellMin,
        locked: false,
      }))

    const width = 460
    const rowH = 58
    const top = TITLE_BAR + 52
    const height = top + rows.length * rowH + 14 + FOOTER_BAR

    const panel: ModalPanel = {
      view: new Container(),
      size: { width, height },
    }
    const layer = panel.view
    layer.addChild(panelFrame(width, height, '자리가 없습니다',
      () => this.modals.close(panel)))

    const lead = richLine(
      `「${shopLabel(item.kind, item.id, this.data)}」 를 놓을 자리를 비웁니다`, {
        base: { fontSize: 12, fill: COLOR.inkDim },
        number: COLOR.accentNumber,
        term: COLOR.accentTerm,
      })
    lead.position.set((width - lead.width) / 2, TITLE_BAR + 20)
    layer.addChild(lead)

    rows.forEach((held, line) => {
      const y = top + line * rowH
      const tile = new Panel(width - 48, rowH - 10, held.locked ? 0x241c26 : 0x1b2331)
      tile.position.set(24, y)

      const name = new Text({
        text: held.name,
        style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
      })
      name.position.set(14, 8)

      const note = new Text({
        text: held.locked ? '팔 수 없습니다' : held.note,
        style: {
          fontSize: 11, fill: held.locked ? 0xffb4c8 : COLOR.inkDim,
          wordWrap: true, wordWrapWidth: width - 200,
        },
      })
      note.position.set(14, 28)

      const price = new Text({
        text: held.locked ? '—' : `+$${held.price}`,
        style: { fontSize: 15, fill: held.locked ? 0x7a6a45 : COLOR.money, fontWeight: '800' },
      })
      price.anchor.set(1, 0.5)
      price.position.set(width - 62, (rowH - 10) / 2)

      tile.addChild(name, note, price)
      tile.alpha = held.locked ? 0.5 : 1
      if (!held.locked) {
        tile.eventMode = 'static'
        tile.cursor = 'pointer'
        tile.on('pointertap', () => {
          this.modals.close(panel)
          this.audio.play('shop_buy')
          this.act({ t: 'swap', slot, index: held.index })
        })
      }
      layer.addChild(tile)
    })

    this.modals.open(panel)
  }

  /**
   * 팩.
   *
   * **사는 것이 아니라 뜯는 것입니다** — 값을 내면 몇 장이 펼쳐지고 그중에서 고릅니다.
   * 그래서 카드가 아니라 **봉지**로 그립니다. 크기는 카드에 맞추되 위가 톱니로 뜯기게 되어
   * 있고, 그 톱니 하나가 「이건 여는 것이다」를 말합니다.
   */
  private drawPackRow(left: number, top: number, width: number, tileH: number): void {
    const packs = this.state.shop.packs
    const tileW = 104
    const gap = 26
    const span = packs.length * tileW + Math.max(0, packs.length - 1) * gap
    const startX = left + (width - span) / 2

    packs.forEach((packId, slot) => {
      const row = this.data.tables.boosterPack.findByPackId(packId)
      if (!row) return

      const ink = packInk(row.kind)
      const afford = this.shown.money >= row.cost
      const h = SIZE.jokerHeight
      const tile = new Container()
      tile.position.set(startX + slot * (tileW + gap), top)

      const bag = new Graphics()
      bag.roundRect(3, 5, tileW, h, 10).fill({ color: 0x000000, alpha: 0.4 })
      bag.roundRect(0, 0, tileW, h, 10).fill(shade(ink, 0.45))
      // 봉지의 몸통. 위쪽이 조금 밝아 빛을 받은 것으로 보입니다.
      bag.roundRect(0, 0, tileW, h * 0.55, 10).fill({ color: ink, alpha: 0.5 })

      // **뜯는 줄.** 톱니 하나가 봉지를 봉지로 만듭니다.
      const tearY = 26
      bag.rect(0, tearY - 7, tileW, 14).fill({ color: 0x0b1018, alpha: 0.35 })
      const teeth = 13
      for (let i = 0; i < teeth; i++) {
        const x = (tileW / teeth) * i
        bag.moveTo(x, tearY)
          .lineTo(x + tileW / teeth / 2, tearY - 4)
          .lineTo(x + tileW / teeth, tearY)
          .stroke({ color: shade(ink, 0.8), width: 1.4, alpha: 0.9 })
      }

      bag.roundRect(1.25, 1.25, tileW - 2.5, h - 2.5, 9)
        .stroke({ color: shade(ink, 0.9), width: 2.5 })
      tile.addChild(bag)

      const label = new Text({
        text: packName(row.kind, row.size),
        style: {
          fontSize: 12, fill: COLOR.ink, fontWeight: '800', align: 'center',
          wordWrap: true, wordWrapWidth: tileW - 14, lineHeight: 15,
        },
      })
      label.anchor.set(0.5, 0.5)
      label.position.set(tileW / 2, h * 0.52)

      const note = richLine(`${row.cards}장 중 ${row.picks}장`, {
        base: { fontSize: 11, fill: 0xdbe4f0 },
        number: COLOR.accentNumber,
        term: COLOR.accentTerm,
      })
      note.position.set((tileW - note.width) / 2, h - 30)
      tile.addChild(label, note)

      const price = new Text({
        text: `$${row.cost}`,
        style: {
          fontSize: 20, fontWeight: '800',
          fill: afford ? COLOR.money : 0x7a6a45,
          stroke: { color: 0x0a0f18, width: 4 },
        },
      })
      price.anchor.set(0.5, 0)
      price.position.set(tileW / 2, h + 8)
      tile.addChild(price)

      tile.alpha = afford ? 1 : 0.55
      tile.eventMode = 'static'
      tile.hitArea = new Rectangle(0, 0, tileW, tileH)
      tile.cursor = afford ? 'pointer' : 'default'
      tile.on('pointertap', () => {
        if (!afford) return
        this.audio.play('shop_buy')
        this.particles.burst(tile.x + tileW / 2, tile.y + h / 2, 20, ink, 1.2)
        this.jolt(5, 3)
        this.act({ t: 'buy_pack', slot })
      })
      tile.on('pointerover', () => {
        this.tooltip.show(packName(row.kind, row.size), '팩', 0,
          [packBlurb(row.kind), `${row.cards}장이 펼쳐지고 ${row.picks}장을 고릅니다`],
          tile.x + tileW / 2, tile.y + tileH, SIZE)
      })
      tile.on('pointerout', () => this.tooltip.hide())
      this.shopLayer.addChild(tile)
    })
  }

  /** 바우처. 한 안테에 하나이고 런이 끝날 때까지 남습니다. */
  private drawVoucher(left: number, top: number, width: number, tileH: number): void {
    const id = this.state.shop.voucher
    if (!id) {
      const none = new Text({
        text: '이번 안테의 바우처는 이미 샀습니다',
        style: { fontSize: 11, fill: COLOR.inkDim },
      })
      none.anchor.set(0.5, 0)
      none.position.set(left + width / 2, top + 16)
      this.shopLayer.addChild(none)
      return
    }

    const row = this.data.tables.voucher.findByVoucherId(id)
    const lines = describe(this.data, this.data.voucherEffects.get(id) ?? [])
    const cost = this.data.economy.voucherCost
    const afford = this.shown.money >= cost

    const tileW = 460
    const tile = new Panel(tileW, tileH, 0x1d3149)
    tile.position.set(left + (width - tileW) / 2, top)

    const label = new Text({
      text: row?.name ?? '',
      style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
    })
    label.position.set(16, 11)

    const note = richLine(lines[0] ?? '런 내내 남습니다', {
      base: { fontSize: 11, fill: 0x9fc4e8 },
      number: COLOR.accentNumber,
      term: COLOR.accentTerm,
    })
    note.position.set(16, 36)

    const price = new Text({
      text: `$${cost}`,
      style: {
        fontSize: 18, fill: afford ? COLOR.money : 0x7a6a45, fontWeight: '800',
      },
    })
    price.anchor.set(1, 0.5)
    price.position.set(tileW - 16, tileH / 2)

    tile.addChild(label, note, price)
    tile.alpha = afford ? 1 : 0.55
    tile.eventMode = 'static'
    tile.cursor = afford ? 'pointer' : 'default'
    tile.on('pointertap', () => {
      if (!afford) return
      this.audio.play('shop_buy')
      this.act({ t: 'buy_voucher' })
    })
    tile.on('pointerover', () => {
      this.tooltip.show(row?.name ?? '', '바우처', 0, lines,
        tile.x + tileW / 2, tile.y + tileH, SIZE)
    })
    tile.on('pointerout', () => this.tooltip.hide())
    this.shopLayer.addChild(tile)
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
/**
 * 규칙 하나가 어떻게 바뀌었는가.
 *
 * **읽는 법이 셋입니다** — 켜고 끄는 것, 수를 세는 것, 만분율로 적힌 배수. 셋을 한 가지로
 * 적으면 확률 배수가 `10000 → 20000` 으로 뜹니다.
 *
 * 값을 가지지 않는 규칙도 있습니다 — 덱을 다시 뽑거나 그림 카드를 빼는 것들이고, 그때는
 * 이름 한 줄이 전부입니다.
 */
function ruleChange(event: { before: number | null; after: number | null;
                             flag: boolean; rule: string }): string {
  if (event.after === null) return '이번 런 내내 적용됩니다'
  if (event.flag) return event.after !== 0 ? '켜졌습니다' : '꺼졌습니다'

  if (RULE_IS_SCALE.has(event.rule) || RULE_IS_MULTIPLIER.has(event.rule)) {
    const unit = RULE_IS_SCALE.has(event.rule) ? 10_000 : 1
    const to = (event.after / unit).toFixed(2)
    if (event.before === null || event.before === event.after) return `×${to}`
    return `×${(event.before / unit).toFixed(2)}  →  ×${to}`
  }

  // 할인은 만분율이고 **낮을수록 좋습니다.** 배수로 적으면 그 방향이 뒤집혀 읽힙니다.
  if (event.rule === 'ShopDiscount') {
    return `${(event.after / 100).toFixed(0)}% 싸게 삽니다`
  }

  if (event.before === null || event.before === event.after) return String(event.after)
  const delta = event.after - event.before
  return `${event.before}  →  ${event.after}   (${delta > 0 ? '+' : ''}${delta})`
}

/**
 * 만분율로 적힌 규칙들.
 *
 * 값의 단위는 규칙의 성질이지만 **읽는 법은 화면의 몫이라** 여기 있습니다 —
 * `moneyReason` · `valueText` 와 같은 자리입니다.
 */
const RULE_IS_SCALE = new Set([
  'BlindSizeScale', 'PlanetGivesMult',
])

/**
 * 백분율로 적힌 규칙들.
 *
 * 나머지 배수들은 만분율이 아니라 그냥 곱하는 수입니다 — `probabilityScale` 의 기본값이
 * 1이고 2가 되면 두 배라는 뜻입니다. 단위를 지레짐작하면 `1 → 2` 가 `×0.00` 으로 뜹니다.
 */
const RULE_IS_MULTIPLIER = new Set([
  'ShopWeightTarot', 'ShopWeightPlanet', 'ProbabilityScale', 'EditionWeightScale',
])

/**
 * 효과 하나가 낸 값을 글로.
 *
 * **연산마다 읽는 법이 다릅니다** — 곱은 `×`, 가산은 `+`, 돈은 `$` 입니다. 한 자리에서
 * 정하지 않으면 카드와 조커와 런이 각자 다르게 적게 됩니다.
 */
function valueText(op: string, chips: number, mult: number, money: number): string {
  if (op === 'MulMult') return `×${(mult / 10_000).toFixed(2)}`
  if (op === 'GrowSelf') return '늘었습니다'
  if (money !== 0) return `${money > 0 ? '+' : ''}$${money}`
  if (chips !== 0) return `+${chips}`
  if (mult !== 0) return `+${(mult / 10_000).toFixed(0)}`
  return '발동'
}

/** `AllCardsScore` 를 `all_cards_score` 로. 글 표의 식별자가 그 모양입니다. */
function snake(name: string): string {
  return name.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase()
}

/** 색 하나를 셰이더가 받는 0..1 셋으로. */
function rgbOf(color: number): [number, number, number] {
  return [
    ((color >> 16) & 0xff) / 255,
    ((color >> 8) & 0xff) / 255,
    (color & 0xff) / 255,
  ]
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
