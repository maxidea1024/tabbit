import { Container, Graphics } from 'pixi.js'
import { EditionKind } from '../generated/enums/edition-kind'
import { describe } from '../core/describe'
import { nameOf } from '../core/strings'
import { type CardInstance, type JokerInstance } from '../core/state'
import { ArriveFilter } from '../shader/arrive'
import { DissolveFilter } from '../shader/dissolve'
import { CardView, type EditionLook } from '../render/card-view'
import { type JokerLook, JokerView } from '../render/joker-view'
import { type Beat, TURN_BACK_MS, TURN_STEP_MS } from '../render/juice'
import { Motion, Spring } from '../render/motion'
import { backLookOf, cardBack, drawCardBack, setCardBack } from '../render/card-back'
import { cardBackMotif } from '../render/card-set'
import { editionLookOf } from '../render/faces'
import { SIZE, UI } from '../render/theme'
import { borrowedFrom } from '../core/vm'
import { type ModalPanel } from '../ui/modal'
import {
  BOARD_X, DEALER, DECK_LINGER, DECK_PEEK, DECK_X, DECK_Y, DRAG_Z, EMBER, HAND_Y, HELD_RISE,
  HOVER_Z, ITEM_LINGER, ITEM_SETTLE, JOKER_TRAY, JOKER_Y, PICK_TINT, PICK_Z, PLAY_Y, RECALL_STEP,
  RETIRE_TAIL, RISER_ON_CARD, ROW_Z, SHOW_CLEAR, SHOW_Y, SHOW_Z, trayRow,
} from './metrics'
import { type CardShow } from './types'
import { type Game } from './game'
export class CardsPart {
  constructor(private readonly game: Game) {}

  readonly views = new Map<number, CardView>()

  readonly playedViews: CardView[] = []

  /** 아직 날아가지 않은 카드들. 왼쪽부터 한 장씩 차례로 갑니다. */
  readonly slams: { view: CardView; x: number; at: number }[] = []

  /**
   * 이번에 낸 카드에서 진동이 이미 났는가.
   *
   * **한 벌이 한 번입니다.** 카드마다 세면 다섯 번이고, 그것은 한 번의 알림이 아닙니다.
   */
  private slamTapped = false

  /** 마지막 카드가 자리에 닿는 시각. **그때까지 득점을 세지 않습니다.** */
  playLanded = 0

  /** 아직 나가지 않은 버린 카드들. 이것도 왼쪽부터 한 장씩입니다. */
  readonly fades: { view: CardView; at: number }[] = []

  /**
   * 이번 블라인드에서 화면 밖으로 나간 카드가 몇 장인가.
   *
   * **세는 것은 돌려보내기 위해서입니다.** 낸 것도 버린 것도 오른쪽으로 빠져나가는데,
   * 그것으로 끝나면 한 판을 도는 동안 덱이 계속 줄기만 하고 아무것도 돌아오지 않습니다 —
   * 카드는 없어지는 것이 아니라 다음 판에 다시 나오는 것입니다.
   */
  retired = 0

  /**
   * 덱으로 돌아오는 중인 카드들.
   *
   * **되돌아오는 것은 낱장이 아니라 장수입니다.** 어느 카드가 어느 자리로 돌아가는지는
   * 아무도 세지 않으므로, 돌아오는 것은 뒷면 한 장씩이면 됩니다.
   */
  readonly recalls: { node: Container; motion: Motion; at: number; sent: boolean }[] = []

  /** 아직 깔리지 않은 뽑은 카드들. **덱에서 한 장씩 옵니다.** */
  readonly deals: { uid: number; at: number; flipAt: number }[] = []

  /** 깔린 카드가 뒤집힐 시각. `syncCards` 가 새 뷰를 만들 때 가져갑니다. */
  readonly flipAt = new Map<number, number>()

  /** 카드 소리를 마지막으로 낸 시각. 여덟 장이 저마다 내면 소리가 아니라 잡음입니다. */
  /**
   * 여럿이 움직이는 세 자리가 지금 도는가.
   *
   * **맺음을 한 번만 내기 위한 것입니다.** 지속 보이스는 잦아들 뿐 끝을 알리지 않으므로,
   * 마지막 한 장이 닿은 자리에 원샷 하나가 있어야 「끝났다」로 읽힙니다 — 그 하나를
   * 프레임마다 내지 않으려면 돌고 있었는지를 기억해야 합니다.
   */
  private dealing = false

  private sweepingOut = false

  private recalling = false

  /**
   * 마지막 장이 자리를 잡을 때까지.
   *
   * **놓은 것과 앉은 것은 다릅니다** — 예약이 다 빠져도 카드는 아직 용수철로 날아가는
   * 중이고, 그 동안에도 마우스가 닿으면 지나가는 카드가 들려 올라갑니다.
   */
  dealtUntil = 0

  /**
   * 마지막으로 내보낸 카드가 화면을 떠날 때까지.
   *
   * **예약이 빈 것과 나가는 것이 끝난 것은 다릅니다.** 나가기는 예약을 꺼내는 그 프레임에
   * 시작되므로, 한 장을 버리면 예약이 그 프레임에 비고 남은 것이 하나도 없습니다 —
   * 「남은 것이 있는 동안 지속 보이스를 낸다」로만 보고 있어서, **한 장을 버릴 때는
   * 나가는 소리도 맺음도 나지 않았습니다.** 깔기 쪽은 `dealtUntil` 이 그것을 막고
   * 있었고 여기만 없었습니다.
   */
  fadeUntil = 0

  /** 이 시각까지는 덱이 자리에 남습니다. 마지막 카드가 덱에 닿을 때 정해집니다. */
  deckHold = 0

  readonly jokers = new Map<number, JokerView>()

  /** 타는 중인 조커들. 다 타면 치웁니다. */
  readonly burning: JokerView[] = []

  /**
   * 진 판의 판에 선 조커들. **그림이 오갈 때 다시 그리려고 들고 있습니다.**
   *
   * 이 판은 한 번 세우고 다시 세우지 않으므로(`gameOverShown`), 다른 통들과 달리
   * `refresh` 가 닿지 않습니다 — 그림이 놓이면 그것을 가리킨 채로 남고, 놓인 그림이
   * 실제로 버려지는 두 틱 뒤에 그 프레임이 예외로 죽습니다.
   */
  readonly gameOverJokers: { view: JokerView; joker: JokerInstance }[] = []

  /**
   * 겉면만 시각을 받는 것. **딱지 하나이거나 얼굴에 걸린 셰이더 하나입니다.**
   *
   * 줄에 선 딱지는 `advance` 가 자리와 겉면을 함께 돌리지만, 상점의 칸 · 팩에 펼친 카드 ·
   * 소모품 칸 · 진 판의 판에 선 것은 자리를 부르는 쪽이 정합니다 — 그것들에까지 `advance`
   * 를 부르면 용수철이 딱지를 제 목표(0, 0)로 끌어갑니다. 그렇다고 아무것도 부르지 않으면
   * 판의 셰이더가 시각을 받지 못해 `uTime` 이 0 에 굳고, 무늬가 흐르지 않습니다.
   *
   * **이 넷은 저마다 자기 표에 담아 둡니다.** 따로 목록을 두고 「지워진 것」으로 걷어내려
   * 했는데, Pixi 의 `destroy()` 는 자식까지 지우지 않으므로 그 표시가 성립하지 않습니다 —
   * 통을 버려도 그 안의 딱지는 `destroyed` 가 거짓인 채로 남습니다.
   */
  readonly selected = new Set<number>()

  /**
   * 상점 칸의 딱지들.
   *
   * **자리를 물을 곳이 있어야 합니다.** 고른 칸 밑에 단추를 세우려면 그 칸이 어디에 있는지
   * 알아야 하고, 산 것이 날아가는 자리도 그 칸입니다 — 상점은 다시 그릴 때마다 딱지를
   * 새로 만드므로 그때마다 여기도 새로 채웁니다.
   */
  /**
   * 아직 화면이 닿지 않은 카드의 이전 모습.
   *
   * **상태는 액션이 끝난 그 프레임에 이미 바뀌어 있습니다.** 손패의 카드가 그것을 그대로
   * 읽으면 얼굴이 그 프레임에 갈리고, 바뀌는 것을 보이는 박자는 그 뒤에 옵니다 — 보일
   * 것이 이미 없어진 뒤입니다. 박자가 올 때까지 이전 모습을 들고 있는 자리입니다.
   *
   * `rewind` 가 점수와 금액에 하는 일과 같습니다.
   */
  readonly pendingCards = new Map<number, CardInstance>()

  /**
   * 아직 박자가 닿지 않은 조커의 이전 모습. **카드의 `pendingCards` 와 같은 몫입니다.**
   *
   * 조커 줄은 `refresh` 마다 상태를 그대로 그리므로, 붙들지 않으면 판이 갈리는 박자가 오기
   * 전에 이미 새 판으로 그려져 있습니다 — 뒤집어도 앞과 뒤가 같습니다.
   */
  readonly pendingJokers = new Map<number, JokerInstance>()

  cardShow!: CardShow | undefined

  /** 판이 몇 번 섰는가. 검증 도구가 「한 번도 서지 않았다」를 가르는 값입니다. */
  cardShowCount = 0

  /**
   * 방금 무언가를 한 것의 이름.
   *
   * **규칙을 건 것이 누구인지는 앞 박자에 있습니다.** `RuleChanged` 는 무엇이 바뀌었는지만
   * 담고, 누가 걸었는지는 그 앞의 발동 이벤트가 들고 옵니다 — 판의 머리글이 그 이름입니다.
   */
  actorName!: string | undefined

  /**
   * 판 위로 빌려 간 손패의 카드들.
   *
   * **빌린 동안에는 손패 줄이 자리를 정하지 않습니다.** 그러지 않으면 매 프레임 손패 줄이
   * 그 카드를 제자리로 도로 당겨, 나온 카드가 줄과 판 위 사이에서 떨립니다.
   */
  readonly borrowed = new Set<number>()

  /**
   * 능력을 빌리는 딱지와 빌려주는 딱지를 잇는 표시.
   *
   * **순간이 아니라 상태입니다.** 빌리는 것은 그 조커가 있는 내내 이어지는 일이므로
   * 박자로 낼 것이 아니라 계속 보여야 합니다 — 그러지 않으면 왼쪽 조커가 왜 오른쪽
   * 것과 같은 값을 내는지 화면 어디에도 없습니다.
   *
   * 두 딱지의 아래를 잇는 줄 하나입니다. 딱지 위를 지나가면 그림을 가립니다.
   */
  readonly borrowLink = new Graphics()

  /**
   * 아직 화면에 없는 카드에 걸린 것. **그 카드가 깔릴 때 걸립니다.**
   *
   * 보스는 판이 시작할 때 덱 전체에 겁니다 — 그때 손패는 아직 깔리기 전이라 화면에 있는
   * 카드가 하나도 없고, 그 자리에서 걸면 아무 데도 나타나지 않습니다. 깔리는 카드가 그
   * 자리에서 시드는 것이 「이 보스가 내 클럽을 죽였다」입니다.
   */
  readonly castSoon = new Map<number, 'wither' | 'hide'>()

  /**
   * 타고 있는 소모품.
   *
   * **쓴 것은 타서 사라집니다.** 그냥 없어지면 무엇이 없어진 것인지 · 정말 쓰인 것인지
   * 눈이 따라가지 못합니다. 조커를 팔 때와 같은 불이고 같은 빠르기입니다.
   */
  burningItems: {
    tile: Container
    /** 얼굴. **그림자는 뺍니다** — 울렁임과 번쩍임이 그림자에도 걸리면 얼룩이 따로 남습니다. */
    face: Container
    arrive: ArriveFilter
    dissolve: DissolveFilter
    from: { x: number; y: number }
    to: { x: number; y: number }
    /** 쓰기 시작한 뒤 지난 시간. 이것 하나로 네 마디가 갈립니다. */
    life: number
    burn: number
    /** 번쩍임을 한 번 냈는가. 자리에 닿는 그 한 프레임입니다. */
    flashed: boolean
    /** 나오면서 커지는가. 쓴 것만 그렇습니다. */
    grows: boolean
  }[] = []

  /** 손패가 놓인 자리. 끌 때 어느 칸으로 가는지를 이것으로 셉니다. */
  handSpots = { startX: 0, spacing: 0 }

  /** 도움. 이것도 고르면 더 높은 족보가 되는 카드들입니다. */
  readonly hinted = new Set<number>()

  /**
   * 덱에 남은 카드.
   *
   * **무엇이 남았는지를 모르면 버릴지 낼지를 정할 수 없습니다.** 스트레이트에 한 장이
   * 모자랄 때 그 랭크가 덱에 아직 있는지가 그 판의 판단 전부입니다.
   */
  readonly deckView: ModalPanel = {
    view: new Container(),
    size: { width: 520, height: 300 },
  }

  /** 산 뒤에도 그 자리에 남아 있는 딱지들. 때가 되면 사라집니다. */
  readonly leavingTiles: { node: Container; at: number }[] = []

  /** 방금 무언가를 한 것이 놓인 자리. 거기에서 물건이 옵니다. */
  actorAt!: { x: number; y: number } | undefined

  /**
   * 손패를 다루는 것들(낸다 · 취소 · 버린다 · 정렬 둘 · 지시문)이 아래로 물러난 만큼.
   * 0 이면 제자리입니다.
   *
   * **손패를 다룰 수 있을 때만 올라와 있습니다.** 손패가 다 깔리고 연출이 돌지 않는 동안이고,
   * 그 밖에는 화면 아래로 내려가 있습니다 — 눌러도 아무 일이 없는 단추가 놓여 있으면 고장으로
   * 보입니다. 다섯은 같은 맥락의 것이라 함께 움직입니다.
   */
  readonly sortSlide = new Spring(0, 200, 22)

  /**
   * 덱이 이 시각까지 나와 있습니다.
   *
   * **팩에서 집은 플레잉 카드는 덱으로 들어갑니다.** 덱은 팩이 펼쳐진 동안 나와 있고, 팩이
   * 닫힌 뒤에도 마지막 카드가 닿아 눌리는 것을 보고 나서 물러납니다.
   */
  deckPeekUntil = 0

  /** 덱으로 날아가는 카드. 팩에서 집은 그 카드 자체입니다. */
  deckFlight?: { node: Container; motion: Motion; at: number }

  // ---------------------------------------------------------------- 뼈대

  /**
   * 덱 더미를 그립니다.
   *
   * **다섯 장이 다 진짜 뒷면입니다.** 맨 위 한 장만 무늬를 그리고 아래 넉 장은 색만 칠한
   * 네모였습니다 — 옆구리만 보이니 무늬가 보이지 않는다는 이유였는데, 옆구리가 보인다는
   * 것은 그 옆구리에 테두리와 점선 띠가 있다는 뜻입니다. 색만 칠한 네모는 카드가 아니라
   * 카드 두께를 흉내 낸 무엇이고, 덱에서 나가는 카드와 덱으로 돌아오는 카드는 진짜 뒷면을
   * 들고 다니므로 더미만 다른 것을 쓰면 그 둘이 같은 카드로 보이지 않습니다.
   *
   * 매 프레임이 아니라 뒷면이 바뀔 때만 부릅니다.
   */
  drawDeckPile(): void {
    this.game.chrome.deckPile.removeChildren().forEach(child => child.destroy())
    const look = cardBack()
    for (let i = 4; i >= 0; i--) {
      const sheet = new Container()
      sheet.position.set(DECK_X - SIZE.cardWidth / 2 + i * 2,
                         DECK_Y - SIZE.cardHeight / 2 - i * 3)
      drawCardBack(sheet, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius, look)
      this.game.chrome.deckPile.addChild(sheet)
    }
  }

  /**
   * 이 판의 뒷면을 정합니다.
   *
   * **판이 시작될 때 한 번입니다.** 덱이 뒷면을 정하고 덱은 판이 도는 동안 바뀌지 않으므로,
   * 매 프레임 표를 뒤질 이유가 없습니다. 표에 없는 덱이면 첫 덱의 뒷면 그대로입니다 —
   * 뒷면이 없다고 판이 서지 못할 이유는 없습니다.
   */
  syncCardBack(): void {
    const row = this.game.data.tables.deck.findByDeckId(this.game.state.deckId)
    // **무늬는 세트가, 색 두 개는 덱이 정합니다.** 덱이 정하는 것 중 한 판 내내 보이는
    // 것이 뒷면이므로, 세트가 그것을 통째로 가져가면 어느 덱으로 하고 있는지가 화면에서
    // 사라집니다 — 「붉은 덱 + 뼈의 궁정」은 붉은 룬 뒷면입니다.
    if (row) setCardBack({ ...backLookOf(row), motif: cardBackMotif() ?? row.back })
    this.drawDeckPile()
  }

  play(): void {
    if (this.selected.size === 0 || !this.game.handLive) return
    const cards = this.orderedSelection()
    this.selected.clear()
    // **카드를 올리는 것도 박자입니다.** 여기서 올리고 득점을 따로 세면 둘의 간격이 코드에
    // 고정되고, `Const_Feel` 을 고쳐도 화면이 바뀌지 않습니다.
    this.game.act({ t: 'play', cards })
  }

  discard(): void {
    if (this.selected.size === 0 || !this.game.handLive) return
    const cards = this.orderedSelection()
    this.selected.clear()
    // 버리는 것도 한 장씩입니다. **한 덩어리로 사라지면 몇 장을 버렸는지가 남지 않습니다.**
    this.game.act({ t: 'discard', cards })
  }

  /** 고른 카드를 패의 순서대로. **낸 순서가 득점 순서입니다.** */
  orderedSelection(): number[] {
    return this.game.state.hand.filter(uid => this.selected.has(uid))
  }

  /** 패를 정렬합니다. **낼 것을 고르는 일이 훨씬 쉬워집니다.** */
  clearSelection(): void {
    if (this.selected.size === 0 || !this.game.handLive) return
    this.selected.clear()
    this.game.audio.play('card_select', 0, 0, -6)
    this.game.refresh()
  }

  sortHand(by: 'rank' | 'suit'): void {
    if (!this.game.handLive) return
    const before = this.game.state.hand.slice()
    const cards = this.game.state.hand
      .map(uid => this.game.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    cards.sort((a, b) => by === 'rank'
      ? b.rank - a.rank || a.suit - b.suit
      : a.suit - b.suit || b.rank - a.rank)

    this.game.state.hand = cards.map(card => card.uid)
    // **화면이 그리는 것은 `shown.hand` 입니다.** 연출이 도달한 것만 그리기 위한 것이라,
    // 정렬이 그것을 함께 바꾸지 않으면 자리가 하나도 움직이지 않습니다.
    //
    // 화면에 이미 있는 것만 그 차례로 다시 세웁니다 — 아직 날아오는 중인 카드를 여기서
    // 끌어오면 뽑는 연출이 끊깁니다.
    const seen = new Set(this.game.shown.hand)
    this.game.shown.hand = this.game.state.hand.filter(uid => seen.has(uid))

    this.game.input.recordOrder('hand', before)
    this.game.audio.play('card_select')
    this.game.refresh()
  }

  /** 족보 목록을 열고 닫습니다. */
  toggleHandList(): void {
    if (this.game.panels.modals.has(this.game.panels.handList)) {
      this.game.panels.modals.close(this.game.panels.handList)
      return
    }
    this.game.panels.drawHandList()
    this.game.panels.modals.open(this.game.panels.handList)
  }

  /** 남은 카드를 열고 닫습니다. */
  toggleDeckView(): void {
    if (this.game.panels.modals.has(this.deckView)) {
      this.game.panels.modals.close(this.deckView)
      return
    }
    this.game.panels.drawDeckView()
    this.game.panels.modals.open(this.deckView)
  }

  /**
   * 손패 한 장을 집거나 놓습니다.
   *
   * **소리는 하나입니다.** 한동안 여기에 음계 사다리를 얹었는데, 그것은 잘못 놓인
   * 것이었습니다 — 음계가 「값이 쌓이고 있다」를 뜻하는 자리는 낸 카드가 하나씩 득점하는
   * 그곳이고, 무엇을 낼지 고르는 것은 값이 쌓이는 일이 아닙니다. 고르는 자리에까지 음이
   * 오르면 그 뜻이 묽어지고, 정작 득점의 사다리가 특별하지 않게 됩니다.
   */
  toggle(uid: number): void {
    if (!this.game.handLive) return
    if (this.selected.has(uid)) this.selected.delete(uid)
    else if (this.selected.size < this.game.data.run.maxPlayedCards) this.selected.add(uid)
    this.game.audio.play('card_select')
    this.game.refresh()
  }

  /**
   * 낸 카드를 판으로 올립니다.
   *
   * **한꺼번에 움직이지 않습니다.** 왼쪽부터 한 장씩 차례로, 빠르게 가서 자리에 달라붙습니다 —
   * 다섯 장이 같이 미끄러지면 무엇을 냈는지가 한 덩어리로 보이고, 하나씩 「짝」 붙으면
   * 다섯 번의 사건이 됩니다.
   */
  liftToPlayArea(uids: number[]): void {
    const spacing = SIZE.cardWidth + 16
    const startX = BOARD_X - ((uids.length - 1) * spacing) / 2

    uids.forEach((uid, index) => {
      const view = this.views.get(uid)
      if (!view) return
      this.views.delete(uid)
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
        at: this.game.clock + index * (this.game.feel.playStaggerMs / 1000),
      })
    })
    this.slamTapped = false
  }

  /**
   * 버린 카드를 한 장씩 내보냅니다.
   *
   * **곧바로 지우지 않습니다** — 사라지는 것이 보여야 몇 장을 버렸는지가 남습니다.
   *
   * **낸 카드가 물러나는 것과 같은 몸짓입니다.** 버리는 것과 득점하고 물러나는 것은 다음에
   * 일어나는 일이 다르지만 화면에서 하는 일은 하나입니다 — 그 카드가 이 판에서 없어지는
   * 것입니다. 나가는 자리도, 나가는 길도, 조각을 흩는지도 같습니다.
   */
  throwAway(uids: readonly number[], after = 0): void {
    uids.forEach((uid, index) => {
      const view = this.views.get(uid)
      if (!view) return
      this.views.delete(uid)
      this.playedViews.push(view)
      view.eventMode = 'none'
      view.hovered = false
      view.selected = false
      view.setPick(0, PICK_TINT)
      view.hint = false
      // **제자리에서 곧바로 나갑니다.** 판 가운데로 한 번 올려 보냈는데, 그러면 버린 카드가
      // 낸 카드처럼 판에 올라섰다가 없어지는 것으로 보입니다 — 버리는 것은 그 자리에서
      // 화면 밖으로 치우는 것입니다.
      this.fades.push({
        view, at: this.game.clock + after + index * (this.game.feel.playStaggerMs / 1000),
      })
    })
  }

  /** 예약해 둔 깔기. */
  advanceDeals(seconds: number): void {
    // **끝난 판에는 깔지 않습니다.** 다음 패는 득점 연출이 끝난 뒤에 한 장씩 깔리는데,
    // 그 예약이 이미 잡혀 있는 채로 판이 끝날 수 있습니다 — 그러면 「패배」 판이 선 뒤에
    // 그 밑으로 새 패가 마저 깔립니다. 코어는 진 판의 손패를 비우지 않으므로 화면이
    // 걷어야 하고, 걷은 다음에 깔리면 걷은 것이 헛일이 됩니다.
    //
    // **손패는 그대로 둡니다.** 끝난 판의 카드는 그 자리에 남아 판의 마지막 모습이 됩니다 —
    // 예약된 깔기만 버립니다.
    if (this.game.shown.phase === 'lost' || this.game.shown.phase === 'won') {
      this.deals.length = 0
      return
    }

    // **딜러가 먼저 걷고 나서 채웁니다.** 낸 카드가 아직 판에 있는데 덱에서 새 카드가
    // 깔리면, 한 판에 지난 손과 다음 손이 함께 놓입니다 — 실제 판에서는 걷는 것이 먼저이고
    // 채우는 것이 그다음입니다.
    //
    // **예약을 통째로 미룹니다.** 시각만 견주어 막으면 걷힌 그 프레임에 밀린 것이 한꺼번에
    // 쏟아지고, 한 장씩 깔리는 것이 없어집니다.
    if (this.playedViews.length > 0 || this.fades.length > 0) {
      for (const one of this.deals) {
        one.at += seconds
        one.flipAt += seconds
      }
      return
    }

    let dealt = false
    while (this.deals.length > 0 && this.deals[0].at <= this.game.clock) {
      const next = this.deals.shift()
      if (!next) break
      this.game.shown.hand = [...this.game.shown.hand, next.uid]
      this.flipAt.set(next.uid, next.flipAt)
      // 뒤집히는 동안까지 뽑는 중입니다.
      this.dealtUntil = Math.max(this.dealtUntil, next.flipAt + 0.14)
      dealt = true
    }

    // **깔리는 동안 하나만 냅니다.** 여덟 장이 35ms 간격으로 나오고 25ms 간격으로
    // 뒤집히므로, 낱장마다 내면 0.28초에 16개입니다 — 같은 소리를 60ms 에 한 번으로
    // 줄여 두었어도 0.6초짜리 음원 다섯이 겹치는 것은 그대로였습니다.
    if (this.deals.length > 0 || this.game.clock < this.dealtUntil) {
      this.game.audio.sweep('deal', 0.14)
      this.dealing = true
    } else if (this.dealing) {
      // **다 깔린 자리에 맺음 하나.** 지속 보이스는 끝을 알리지 않습니다.
      this.dealing = false
      this.game.audio.play('card_place', 0, 0, -3)
    }

    if (!dealt) return
    this.game.refresh()
  }

  /**
   * 나갔던 카드들이 덱으로 돌아옵니다.
   *
   * **한 판을 도는 동안 카드는 나가기만 했습니다.** 낸 것도 버린 것도 오른쪽 화면 밖으로
   * 빠지고 그것으로 끝이라, 덱은 줄기만 하고 다음 블라인드의 첫 패가 어디에서 오는지가
   * 화면에 없었습니다 — 카드는 없어진 것이 아니라 덱으로 돌아간 것입니다.
   *
   * **아주 빠릅니다.** 이것은 볼 것이 아니라 셈이 맞는다는 표시입니다: 눈이 따라갈 만큼
   * 느리면 격파한 뒤의 그 한숨이 카드 세는 시간이 되고, 그 자리에 서야 할 것은 정산입니다.
   * 스무 장이 0.4초 안에 다 들어옵니다.
   *
   * 돌아오는 것은 뒷면입니다 — 어느 카드가 어느 자리로 가는지는 아무도 세지 않으므로,
   * 얼굴을 그리는 것은 그리는 값만 치르고 아무것도 알리지 않습니다.
   */
  recallToDeck(): void {
    const many = this.retired
    this.retired = 0
    if (many === 0) return

    for (let i = 0; i < many; i++) {
      const sheet = new Container()
      drawCardBack(sheet, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius, cardBack())
      sheet.pivot.set(SIZE.cardWidth / 2, SIZE.cardHeight / 2)

      const motion = new Motion()
      // 딜러의 자리에서 돌아옵니다. **한 줄로 오면 한 장이 길어진 것으로 보입니다** —
      // 조금씩 흩어 둡니다.
      //
      // **덱 층의 좌표입니다.** 이 층은 덱이 나온 만큼 통째로 옮겨져 있으므로, 화면의
      // 자리를 그대로 적으면 그만큼 어긋난 자리에서 출발합니다.
      motion.snap(DEALER.x - this.game.chrome.deckSlide.value + ((i % 5) - 2) * 6,
                  DEALER.y + ((i % 5) - 2) * 11)
      motion.rotation.snap(((i % 3) - 1) * 7)
      motion.hard()
      sheet.position.set(motion.x.value, motion.y.value)
      sheet.zIndex = 60 + i

      // 덱과 같은 층입니다. 덱이 물러나기 시작해도 돌아오는 카드가 그것을 따라갑니다 —
      // 판이 끝나면 덱은 오른쪽으로 빠지는데, 층이 다르면 카드만 빈자리로 들어갑니다.
      this.game.chrome.deckLayer.addChild(sheet)
      this.recalls.push({ node: sheet, motion, at: this.game.clock + i * RECALL_STEP,
        sent: false })
    }
  }

  /** 돌아오는 카드들. 덱에 닿은 것부터 지웁니다. */
  advanceRecalls(seconds: number): void {
    for (let i = this.recalls.length - 1; i >= 0; i--) {
      const one = this.recalls[i]
      if (one.at > this.game.clock) continue

      if (!one.sent) {
        one.sent = true
        one.motion.scale.target = 0.92
      }
      // **덱 층의 좌표입니다.** 카드가 덱과 같은 층에 있으므로 덱이 나온 만큼은 층이
      // 이미 옮겨 놓았습니다 — 그 값을 여기서 한 번 더 더하고 있었고, 카드는 덱이 나온
      // 거리의 두 배만큼 왼쪽에서 사라졌습니다.
      one.motion.to(DECK_X, DECK_Y, 0)
      one.motion.advance(seconds)
      one.node.position.set(one.motion.x.value, one.motion.y.value)
      one.node.rotation = one.motion.rotation.value * (Math.PI / 180)
      one.node.scale.set(one.motion.scale.value)

      // 덱에 닿았습니다. **닿는 소리를 장마다 내지 않습니다** — 그것은 아래의 한
      // 보이스가 통째로 냅니다.
      if (one.motion.x.value > one.motion.x.target + 6) continue
      this.recalls.splice(i, 1)
      one.node.destroy()
      // 닿은 마지막 한 장이 이 값을 정합니다. 그만큼 덱이 자리에 남습니다.
      this.deckHold = this.game.clock + DECK_LINGER
    }

    // **도는 동안 하나만 냅니다.** 프레임마다 부르되 `sweep` 이 끝나는 시각만 미루므로
    // 보이스는 하나이고, 장수가 스물이든 쉰이든 크기가 같습니다.
    //
    // 낱장마다 냈을 때가 문제였습니다: 스무 장이 0.4초 안에 들어오므로 보이스가 스물이고
    // 합은 13dB 위입니다. 몇 장에 한 번으로 줄여도 0.6초짜리 음원 다섯이 겹쳐 남는 것은
    // 「드르르륵」이었고, 그것은 카드가 쌓이는 소리가 아닙니다.
    //
    // **걷는 소리가 잦아든 뒤에 냅니다.** 카드는 이미 오고 있고 소리만 한 박자 늦습니다 —
    // 둘이 같은 순간에 시작하면 두 몸짓이 하나로 들립니다. 걷는 소리가 아직 도는 동안
    // 회수가 끝나면 이 소리는 나지 않고, 그때는 걷는 소리가 그 자리를 채웁니다.
    if (this.recalls.length > 0) {
      if (!this.sweepingOut) {
        this.game.audio.sweep('recall', 0.14, 1,
          Math.max(0.14, this.recalls.length * RECALL_STEP + 0.14))
        this.recalling = true
      }
    } else if (this.recalling) {
      // **마지막 한 장이 닿은 자리에 맺음 하나.** 지속 보이스는 끝을 알리지 않으므로,
      // 그것만으로는 잦아든 것이 「끝났다」로 읽히지 않습니다.
      this.recalling = false
      this.game.audio.play('card_place', 0, 0, -5)
    }
  }

  /** 예약해 둔 한 장씩의 내보내기. */
  advanceFades(): void {
    while (this.fades.length > 0 && this.fades[0].at <= this.game.clock) {
      const next = this.fades.shift()
      if (!next) break
      // **딜러에게 갑니다.** 버린 것도 낸 것도 오른쪽 위 밖의 한 점으로 물러납니다 — 태워
      // 없애는 것은 조커와 소모품의 것이고, 카드는 거두는 것입니다. 저마다 자기 자리의
      // 높이로 나가면 손에서 나가는 카드가 덱의 높이로 빠져 덱으로 되돌아가는 것으로
      // 보였습니다.
      next.view.retire(DEALER.x, DEALER.y)
      this.retired++
      // 이 한 장이 화면을 떠날 때까지가 아직 「나가는 중」입니다.
      this.fadeUntil = Math.max(this.fadeUntil, this.game.clock + RETIRE_TAIL)
      // **조용히 나갑니다.** 버린 카드에만 조각을 흩뿌렸는데, 그러면 버리는 것과 득점하고
      // 물러나는 것이 화면에서 다른 일로 보입니다 — 둘 다 그 카드가 이 판에서 없어지는
      // 것이고, 무엇이 없어졌는지는 카드가 나가는 것으로 이미 보입니다.
    }

    // **나가는 동안 하나만 냅니다.** 장마다 `card_destroy` 를 냈고 그 음원이 0.693초라,
    // 여덟 장이 나가면 여덟이 겹쳤습니다 — 회수가 그 뒤에 이어지므로 둘이 함께 「드르르륵」
    // 이 되던 자리입니다.
    //
    // **예약이 빈 뒤에도 나가는 중입니다.** 한 장만 버리면 예약이 꺼내는 그 프레임에
    // 비므로, 남은 개수만 보면 그 한 장은 소리 없이 나갔습니다.
    if (this.fades.length > 0 || this.game.clock < this.fadeUntil) {
      // 대역은 남은 장수만큼 걸려 옮겨 갑니다. **몸짓의 길이와 소리의 길이가 같아야
      // 어디까지 왔는지가 들립니다.**
      this.game.audio.sweep('retire', 0.16, 1,
        Math.max(0.16, this.fades.length * (this.game.feel.playStaggerMs / 1000) + RETIRE_TAIL))
      this.sweepingOut = true
    } else if (this.sweepingOut) {
      this.sweepingOut = false
      this.game.audio.play('card_destroy', 0, 0, -7)
    }
  }

  /**
   * 이 카드가 나가는 중이거나 나가기로 잡혀 있는가.
   *
   * **잡혀 있는 것도 셉니다.** `retiring` 은 물러남이 실제로 시작될 때 서는데, 예약과 시작
   * 사이의 0.3초 동안 `retiring` 만 보면 매 틱 다시 예약합니다 — 한 판에 같은 카드가 큐에
   * 99번까지 들어가 있었고, 그동안 `fades` 가 비지 않아 상점이 서지 못했습니다.
   */
  leaving(view: CardView): boolean {
    return view.retiring || this.fades.some(one => one.view === view)
  }

  /** 예약해 둔 한 장씩의 이동. */
  advanceSlams(): void {
    while (this.slams.length > 0 && this.slams[0].at <= this.game.clock) {
      const next = this.slams.shift()
      if (!next) break
      next.view.slam(next.x, PLAY_Y)
      this.game.audio.play('card_slam')
      // **한 판에 한 번입니다.** 다섯 장이 `PlayStaggerMs` 사이로 닿으므로, 장마다 떨면
      // 그것은 다섯 번의 알림이 아니라 한 번의 긴 떨림입니다 — 진동의 간격에 맡기면
      // 그 값과 스태거의 비에 따라 세 번이 되기도 하므로 여기서 셉니다.
      if (!this.slamTapped) {
        this.slamTapped = true
        this.game.show.haptics.play('play')
      }
      this.game.show.jolt(2.2, 0.35)
      // **마지막 카드가 닿을 때까지 세지 않습니다.** 날아가는 중인 카드 위에 숫자가 뜨면
      // 다섯 장이 한 덩어리로 보입니다.
      this.playLanded = this.game.clock + this.game.feel.playLandMs / 1000
    }
  }

  /**
   * 낸 카드를 물러나게 합니다. 화면 밖으로 나가면 그때 지웁니다.
   *
   * **한 장씩 나갑니다.** 다섯 장이 한꺼번에 미끄러지면 한 덩어리가 빠져나가는 것으로
   * 보이고, 낸 것이 다섯 장이었다는 것이 마지막에 지워집니다.
   */
  clearPlayArea(): void {
    // **들린 카드는 먼저 내려옵니다.** 득점한 카드는 8픽셀 들려 있는데, 그 채로 나가면
    // 매칭된 것과 아닌 것이 어긋난 줄로 물러나고 들렸던 것이 도로 내려오는 것을 보지
    // 못합니다 — 올라간 것은 내려와서 없어져야 한 몸짓으로 읽힙니다.
    for (const view of this.playedViews) view.scoring = false
    this.playedViews.forEach((view, index) => {
      if (this.leaving(view)) return
      this.fades.push({
        view,
        at: this.game.clock + ITEM_SETTLE + ITEM_LINGER
          + index * (this.game.feel.playStaggerMs / 1000),
      })
    })
  }

  /**
   * 라운드가 끝났습니다. 손에 남은 카드를 걷습니다.
   *
   * **정산 판은 빈 자리 위에 뜹니다.** 손에 카드가 그대로 있는데 그 위에 판이 덮이면, 끝난
   * 것과 아직 쥐고 있는 것이 한 화면에 겹칩니다. **끝난 판(패배·승리)에서는 부르지
   * 않습니다** — 그쪽은 카드가 그 자리에 남아 판의 마지막 모습이 됩니다.
   *
   * 태우지 않고 물러나게 합니다. 버리는 것은 없애는 것이고, 이것은 치우는 것입니다.
   */
  sweepHand(after = 0): void {
    const left = [...this.views.keys()]
    if (left.length === 0) return
    this.throwAway(left, after)
    this.game.shown.hand = []
  }

  /** 물러난 카드를 치웁니다. */
  reapPlayArea(): void {
    for (let i = this.playedViews.length - 1; i >= 0; i--) {
      if (!this.playedViews[i].gone) continue
      this.playedViews[i].destroy()
      this.playedViews.splice(i, 1)
    }
  }

  /**
   * 바뀌는 카드가 판 위로 나옵니다.
   *
   * **깜깜이로 바꾸지 않습니다.** 카드가 바뀌고 없어지고 더해지는 것은 그동안 오른쪽
   * 토스트 한 줄이 전부였고, 덱 안의 카드는 일어난 자리가 화면에 없었습니다 — 대상만
   * 덱과 손패에서 나와 한 줄로 서고, 거기서 바뀌는 것을 보이고, 돌아갑니다.
   *
   * **막을 씌우지 않습니다.** 판 위에서 그대로 일어납니다.
   *
   * 규격은 [가진 것이 바뀌는 것의 연출](../../doc/presentation/state-change.md) 입니다.
   */
  showCardChange(beat: Beat): void {
    const group = beat.cards
    if (!group) return
    // **이전 판은 곧바로 걷습니다.** 카드가 연달아 바뀌는 판에서 앞의 것이 아직 나와
    // 있으면 두 줄이 겹칩니다.
    this.endCardShow()

    const wanted: { uid: number; kind: 'modify' | 'destroy' | 'add' }[] = [
      ...group.modified.map(uid => ({ uid, kind: 'modify' as const })),
      ...group.destroyed.map(uid => ({ uid, kind: 'destroy' as const })),
      ...group.added.map(uid => ({ uid, kind: 'add' as const })),
    ]
    if (wanted.length === 0) return

    const hold = beat.hold / 1000
    // 덱이 나와서 보내고 받습니다. **이미 있는 몸짓입니다** — 팩에서 집은 카드가 덱으로
    // 들어갈 때와 같은 자리입니다.
    this.deckPeekUntil = Math.max(this.deckPeekUntil, this.game.clock + hold + SHOW_CLEAR)
    // 상점에서 일어난 것이면 상점이 물러납니다. 판 위에서 일어나는 일이 판에 가려집니다.
    if (this.game.state.phase === 'shop') this.game.shop.holdShop(hold + SHOW_CLEAR)
    // **알림 판이 그 자리를 씁니다.** 둘이 겹치면 카드가 판 뒤로 들어가므로, 카드가
    // 나오는 동안은 알림 판이 먼저 걷힙니다 — 알림 판은 이미 그 몫을 읽혔습니다.
    this.game.show.ruleBanner.dismiss()

    const spacing = Math.min(SIZE.cardWidth + 16, 640 / Math.max(1, wanted.length))
    const startX = BOARD_X - ((wanted.length - 1) * spacing) / 2
    const show: CardShow = {
      cards: [], until: this.game.clock + hold, clear: this.game.clock + hold + SHOW_CLEAR,
      closed: false,
    }

    wanted.forEach((one, index) => {
      // **바뀌기 전의 모습으로 나옵니다.** 없으면 지금의 모습입니다 — 더해진 카드는
      // 이전이 없습니다.
      const now = this.game.state.deck.find(card => card.uid === one.uid)
      const was = this.pendingCards.get(one.uid) ?? now
      if (!was) return

      // 손패에 있으면 그 카드가 그대로 올라옵니다. **덱에서 꺼내 오면 손에 든 카드가
      // 덱에서 나오는 것으로 보입니다.**
      const held = this.views.get(one.uid)
      const view = held ?? new CardView(was, this.editionLook(was.edition))
      if (held) {
        this.borrowed.add(one.uid)
      } else {
        this.game.board.addChild(view)
        view.placeNow(DECK_X, DECK_Y)
      }
      view.eventMode = 'none'
      view.selected = false
      view.hint = false
      view.zIndex = SHOW_Z + index
      // **더해진 카드는 뒷면으로 나옵니다.** 뒤집혀 앞면이 되는 것이 「새로 왔다」이고,
      // 바뀌는 카드가 뒷면을 거쳐 돌아오는 것과 같은 몸짓입니다.
      if (one.kind === 'add') view.faceBack()
      view.slam(startX + index * spacing, SHOW_Y)
      show.cards.push({ view, uid: one.uid, kind: one.kind, borrowed: held !== undefined })

      // **한 장씩 차례로 바뀝니다.** 다섯 장이 한 프레임에 갈리면 한 덩어리가 바뀐 것으로
      // 보이고, 어느 장이 무엇이 되었는지가 남지 않습니다.
      //
      // **간격이 반 바퀴보다 길어야 합니다**(`TURN_STEP_MS`). 낸 카드가 올라가는 간격을
      // 빌려 쓰던 동안은 그것이 90밀리초라 장마다 뒤집힘이 겹쳤고, 세 장부터 한 덩어리가
      // 통째로 갈리는 것으로 보였습니다.
      const at = this.game.clock + this.game.feel.drawLandMs / 1000
        + index * (TURN_STEP_MS / 1000)
      this.game.later.push({ at, run: () => this.turnShownCard(one.uid, one.kind) })
    })

    if (show.cards.length === 0) return
    this.cardShow = show
    this.cardShowCount++
    this.game.audio.play('card_draw')
  }

  /** 나와 있는 카드 한 장이 제 차례에 바뀝니다. */
  private turnShownCard(uid: number, kind: 'modify' | 'destroy' | 'add'): void {
    const one = this.cardShow?.cards.find(card => card.uid === uid)
    if (!one || one.view.destroyed) return
    const view = one.view

    const now = this.game.state.deck.find(card => card.uid === uid)

    if (kind === 'destroy') {
      // **탑니다.** 옅어지며 지워지는 것은 「치웠다」이지 「없앴다」가 아닙니다 — 조커가
      // 없어지는 것과 같은 몸짓이고 같은 셰이더입니다.
      const was = this.pendingCards.get(uid) ?? now
      view.ignite()
      this.game.show.particles.burst(view.x, view.y + 30, 26, EMBER, 1.3, 1.1)
      // **어느 장이 없어졌는지가 그 자리에 적힙니다.** 타는 것은 「없앴다」이고, 글이
      // 「무엇을」입니다.
      if (was) {
        this.game.show.popAt({ x: view.x, y: view.y - RISER_ON_CARD },
          this.game.panels.cardLabel(was), UI.bad, 0.5)
      }
      this.game.audio.play('card_destroy')
      this.game.audio.tone('pluck', -6, 0.6)
      this.game.show.jolt(7, 1.6, 0.35)
      return
    }

    if (kind === 'add') {
      // 새로 온 것. **뒷면으로 서 있다가 뒤집혀 앞면이 됩니다** — 무엇이 들어왔는지가
      // 그 순간에 보입니다.
      view.onFlipped = () => {
        view.onFlipped = undefined
        view.pop(1.2)
        this.game.show.particles.burst(view.x, view.y, 18, UI.good, 0.95, 0.85)
        if (now) {
          this.game.show.popAt({ x: view.x, y: view.y - RISER_ON_CARD },
            this.game.panels.cardLabel(now), UI.good, 0.5)
        }
        this.game.audio.play('card_place')
        this.game.audio.tone('chime', 5, 0.5)
      }
      this.game.audio.play('card_flip')
      view.turnUp()
      return
    }

    // 바뀌는 것. **뒷면을 거쳐 다른 카드가 되어 돌아옵니다.**
    if (!now) return
    const was = this.pendingCards.get(uid)
    const text = was ? this.game.panels.changeText(was, now) : this.game.panels.cardLabel(now)
    this.pendingCards.delete(uid)
    // **글은 새 얼굴이 보이는 그 순간입니다.** 앞서 뜨면 아직 뒷면인 카드 위에 뜨고,
    // 늦으면 이미 다 본 카드에 뒤늦게 얹힙니다.
    view.onFlipped = () => {
      view.onFlipped = undefined
      view.pop(0.8)
      this.game.show.particles.burst(view.x, view.y, 16, UI.legendary, 0.95, 0.85)
      this.game.show.popAt({ x: view.x, y: view.y - RISER_ON_CARD }, text, UI.legendary, 0.5)
      this.game.audio.play('card_flip')
      this.game.audio.tone('glass', 7, 0.55)
    }
    // 닫히는 소리와 열리는 소리가 둘입니다 — 반 바퀴가 둘이기 때문입니다.
    this.game.audio.play('card_flip')
    view.turnOver(now, this.editionLook(now.edition), TURN_BACK_MS / 1000)
  }

  /**
   * 나와 있던 카드들이 돌아갑니다.
   *
   * 빌린 것은 손패가 도로 가져가고, 덱에서 나온 것은 덱으로 돌아가 지워집니다. 없어진
   * 것은 그 자리에서 옅어집니다.
   */
  private closeCardShow(): void {
    const show = this.cardShow
    if (!show || show.closed) return
    show.closed = true

    for (const one of show.cards) {
      if (one.view.destroyed) continue
      if (one.borrowed) {
        // 손패가 제자리를 다시 정합니다.
        this.borrowed.delete(one.uid)
        one.view.eventMode = 'static'
        one.view.zIndex = ROW_Z
        continue
      }
      if (one.kind === 'destroy') continue
      one.view.place(DECK_X, DECK_Y, 0)
    }
    // 빌린 것이 제자리로 가는 것은 손패 줄이 합니다.
    this.game.refresh()
    if (show.cards.some(one => !one.borrowed && one.kind !== 'destroy')) {
      this.game.audio.play('card_place')
      this.game.chrome.deckBump.kick(180)
    }
  }

  /** 판을 걷습니다. 빌린 것은 돌려주고 나머지는 지웁니다. */
  endCardShow(): void {
    const show = this.cardShow
    if (!show) return
    this.closeCardShow()
    for (const one of show.cards) {
      if (one.borrowed || one.view.destroyed) continue
      one.view.destroy()
    }
    this.cardShow = undefined
    this.pendingCards.clear()
  }

  /** 나와 있는 카드들을 한 단계 옮깁니다. 때가 되면 돌려보내고 지웁니다. */
  advanceCardShow(seconds: number): void {
    const show = this.cardShow
    if (!show) return
    for (const one of show.cards) {
      if (one.view.destroyed) continue
      one.view.advance(seconds, this.game.clock)
    }
    if (!show.closed && this.game.clock >= show.until) this.closeCardShow()
    if (this.game.clock >= show.clear) this.endCardShow()
  }

  /**
   * 카드 몇 장이 무력해집니다. **보스가 거는 것입니다.**
   *
   * 금이 가운데에서 바깥으로 번지고 색이 빠집니다 — 죽어 있는 모습은 카드의 얼굴이 이미
   * 들고 있으므로, 여기서 보이는 것은 그 사이의 한 몸짓입니다.
   *
   * **한 장씩 차례로 걸립니다.** 손패 여덟 장이 한 프레임에 회색이 되면 한 덩어리가 죽은
   * 것으로 보이고, 어느 장이 걸린 것인지 눈이 따라가지 못합니다.
   *
   * 손패에 없는 카드는 건너뜁니다 — 덱 전체에 거는 보스가 있고, 그 순간 화면에 있는 것은
   * 손에 든 몇 장뿐입니다.
   */
  witherCards(uids: readonly number[]): void {
    const shown = this.stagger(uids, 'wither', (view, uid) => this.witherOne(view, uid))
    // **화면에 한 장도 없으면 덱이 알립니다.** 덱 전체에 거는 보스가 그렇습니다 — 무늬
    // 하나면 13장이고, 그 13장은 아직 덱 안에 있습니다.
    if (shown === 0) {
      this.tellDeck(uids.length, UI.bad, 'boss_reveal')
      return
    }
    this.game.audio.play('boss_reveal')
    this.game.show.jolt(6 + Math.min(shown, 6), 1.7, 0.4)
    this.game.show.flashPanel(UI.bad, 0.75)
  }

  /**
   * 덱 안의 카드 몇 장에 무엇이 걸렸다는 것을 덱이 알립니다.
   *
   * **덱은 라운드 사이에 화면 오른쪽으로 물러나 있습니다.** 그래서 덱 안의 카드를
   * 건드리는 것은 일어난 자리가 화면에 없습니다 — 덱이 나와 한 번 눌리고, 그 위에 몇
   * 장인지가 뜹니다. 어느 장인지는 그 카드가 깔릴 때 그 자리에서 보입니다.
   */
  private tellDeck(count: number, tint: number, cue: string): void {
    if (count <= 0) return
    this.deckPeekUntil = Math.max(this.deckPeekUntil, this.game.clock + DECK_PEEK)
    this.game.chrome.deckBump.kick(220)
    this.game.audio.play(cue)
    this.game.show.particles.burst(DECK_X, DECK_Y, 16, tint, 1, 0.9)
    this.game.show.popAt({ x: DECK_X - 36, y: DECK_Y - RISER_ON_CARD }, `${count}`, tint, 0.5)
    this.game.show.jolt(6, 1.5, 0.35)
    this.game.show.flashPanel(tint, 0.7)
  }

  /**
   * 손패의 카드들이 엎어집니다. **보스가 거는 것입니다.**
   *
   * 그 자리에서 한 번 뒤집혀 뒷면으로 돌아옵니다 — 이미 엎어진 채로 그려지면 카드가 처음
   * 부터 그랬던 것으로 보입니다.
   */
  hideCards(uids: readonly number[]): void {
    const shown = this.stagger(uids, 'hide', (view, uid) => this.hideOne(view, uid))
    if (shown === 0) {
      this.tellDeck(uids.length, UI.inkDim, 'card_flip')
      return
    }
    this.game.audio.play('card_flip')
    this.game.audio.tone('pluck', -4, 0.6)
    this.game.show.jolt(5 + Math.min(shown, 5), 1.4, 0.3)
    this.game.show.flashPanel(UI.inkDim, 0.6)
  }

  /**
   * 손패의 카드들에 차례로 무엇을 겁니다.
   *
   * **간격은 낸 카드가 올라갈 때의 것과 같습니다.** 새 상수를 두지 않는 것은 이것도 카드
   * 여럿이 차례로 무엇을 하는 일이기 때문입니다.
   */
  private stagger(uids: readonly number[], kind: 'wither' | 'hide',
                  run: (view: CardView, uid: number) => void): number {
    let shown = 0
    for (const uid of uids) {
      const view = this.views.get(uid)
      if (!view) {
        // **아직 깔리기 전입니다.** 깔릴 때 걸립니다 — 지금 지나가면 그 카드에는 아무
        // 일도 일어나지 않은 것이 됩니다.
        this.castSoon.set(uid, kind)
        continue
      }
      const at = this.game.clock + shown * (this.game.feel.playStaggerMs / 1000)
      this.game.later.push({
        at,
        run: () => {
          if (view.destroyed) return
          run(view, uid)
        },
      })
      shown++
    }
    return shown
  }

  /** 카드 한 장이 시듭니다. */
  private witherOne(view: CardView, uid: number): void {
    const now = this.game.state.deck.find(card => card.uid === uid)
    this.pendingCards.delete(uid)
    view.wither(now, now && this.editionLook(now.edition))
    this.game.show.particles.burst(view.x, view.y, 10, UI.bad, 0.7, 0.7)
  }

  /** 카드 한 장이 엎어집니다. */
  private hideOne(view: CardView, uid: number): void {
    const now = this.game.state.deck.find(card => card.uid === uid)
    if (!now) return
    this.pendingCards.delete(uid)
    view.turnInto(now, this.editionLook(now.edition))
  }

  /**
   * 방금 깔린 카드에 미뤄 둔 것이 있으면 겁니다.
   *
   * **뒤집히고 나서입니다.** 뒤집히는 중에 걸면 뒷면이 시드는 것으로 보이고, 그것은 그
   * 카드에 일어난 일로 읽히지 않습니다.
   */
  private castOnDealt(uid: number, view: CardView, flipAt: number): void {
    const kind = this.castSoon.get(uid)
    if (kind === undefined) return
    this.castSoon.delete(uid)
    this.game.later.push({
      at: flipAt + 0.14,
      run: () => {
        if (view.destroyed) return
        if (kind === 'wither') {
          this.witherOne(view, uid)
          this.game.audio.play('boss_reveal', -3)
        } else {
          this.hideOne(view, uid)
          this.game.audio.play('card_flip', -2)
        }
      },
    })
  }

  /**
   * 득점하지 않는 카드를 물러나게 합니다.
   *
   * **원작이 그렇습니다** — 다섯 장을 냈는데 족보에 드는 것이 둘뿐이면 나머지 셋은 회색이
   * 되고, 뜨지도 세지도 않습니다.
   */
  dimNonScoring(scoring: readonly number[]): void {
    for (const view of this.playedViews) {
      const counts = scoring.includes(view.uid)
      view.setPick(counts ? 0 : -1, PICK_TINT)
      view.idle = counts ? 0.4 : 0.15
      // **안착한 다음에 살며시 올라갑니다.** 이 박자는 카드가 다 닿은 뒤에 오므로, 여기서
      // 올리면 날아가는 중에 들리는 일이 없습니다.
      view.scoring = counts
    }
  }

  viewOf(uid: number): CardView | undefined {
    return this.views.get(uid) ?? this.playedViews.find(view => view.uid === uid)
  }

  jokerUidAt(slot: number): number {
    return this.game.state.jokers[slot]?.uid ?? -1
  }

  /**
   * 줄에 선 것 하나의 겹치는 차례.
   *
   * **끌고 있는 것은 건드리지 않습니다** — 그것은 이미 맨 위(`DRAG_Z`)이고, 그 값을
   * 여기서 되돌리면 끌고 있는 카드가 줄 밑으로 들어갑니다.
   */
  restackRow(view: { zIndex: number; rowZ: number }, hovered: boolean,
                     picked: boolean): void {
    if (view.zIndex === DRAG_Z) return
    view.zIndex = hovered ? HOVER_Z : picked ? PICK_Z : view.rowZ
  }

  // ---------------------------------------------------------------- 다시 그리기

  editionLook(edition: EditionKind): EditionLook | undefined {
    return editionLookOf(this.game.data, edition)
  }

  /** 조커 딱지 하나의 겉모습. 줄이 그리는 것과 뒤집어 갈아 끼우는 것이 같은 것을 씁니다. */
  jokerLookOf(joker: JokerInstance): JokerLook {
    const row = this.game.data.tables.joker.findByJokerId(joker.jokerId)
    return {
      name: nameOf(this.game.data, 'joker', joker.jokerId, joker.jokerId),
      rarity: row?.rarity ?? 1,
      lines: describe(this.game.data, this.game.data.jokerEffects.get(joker.jokerId) ?? []),
      edition: this.editionLook(joker.edition),
    }
  }

  syncCards(): void {
    // **화면이 주장하는 패입니다.** 다음 패는 득점 연출이 끝난 뒤에 깔립니다.
    //
    // **끝난 판에서는 손대지 않습니다.** 있는 카드는 그 자리에 남고 새로 깔리는 것도
    // 없습니다 — 그 카드들이 판의 마지막 모습이고, 진 판은 그 모습 그대로 재가 됩니다.
    const over = this.game.shown.phase === 'lost' || this.game.shown.phase === 'won'
    if (over) return
    const wanted = new Set(this.game.shown.hand)

    for (const [uid, view] of this.views) {
      if (!wanted.has(uid)) {
        view.destroy()
        this.views.delete(uid)
      }
    }

    const hand = this.game.shown.hand
      .map(uid => this.game.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    const spacing = Math.min(SIZE.cardWidth + 12, 720 / Math.max(1, hand.length))
    const startX = BOARD_X - ((hand.length - 1) * spacing) / 2
    this.handSpots = { startX, spacing }

    hand.forEach((card, index) => {
      let view = this.views.get(card.uid)
      const fresh = view === undefined
      // **아직 박자가 닿지 않았으면 이전 모습입니다.** 바뀌는 것을 보이는 박자가 그 자리에서
      // 뒤집어 갈아 끼웁니다.
      const face = this.pendingCards.get(card.uid) ?? card

      if (!view) {
        view = new CardView(face, this.editionLook(face.edition))
        view.eventMode = 'static'
        view.cursor = 'pointer'
        // **누르기와 끌기가 한 손가락에 얹힙니다.** 뗄 때까지 움직이지 않았으면 고른
        // 것이고, 움직였으면 자리를 옮긴 것입니다 — `pointertap` 은 이 둘을 갈라 주지
        // 않아서 끌고 나서도 골라 버립니다.
        view.on('pointerdown', event =>
          this.game.input.beginDrag('hand', card.uid, view as CardView, event))
        this.views.set(card.uid, view)
        this.game.board.addChild(view)
        // 덱에서 날아옵니다. **곧바로 자리에 있으면 뽑았다는 느낌이 없습니다.**
        view.placeNow(DECK_X, DECK_Y)
        // **낱장마다 소리를 내지 않습니다.** 깔리는 동안은 `advanceDeals` 의 지속
        // 보이스 하나가 통째로 냅니다 — 장마다 내면 개수가 곧 보이스 수입니다.
      } else if (!this.borrowed.has(card.uid)) {
        view.set(face, this.editionLook(face.edition))
      }

      // **여기서 `eventMode` 를 끄지 않습니다.** 한동안 `handLive` 가 거짓이면 `'none'` 으로
      // 두었는데, 이 값은 `refresh()` 가 불린 그 순간에 굳습니다 — 깔기의 마지막 `refresh()`
      // 는 카드가 아직 뒤집히는 중에 오므로 `'none'` 이 찍히고, 그 뒤로 다시 그릴 일이 없으면
      // **카드가 영영 눌리지 않습니다.** 판을 열고 닫아야 살아났고, 30fps 로 도는 데스크탑에서
      // 그렇게 되었습니다. 만질 수 있는지는 누르는 그 순간에 `beginDrag` 가 봅니다.

      // **판 위로 빌려 간 카드는 손패 줄이 자리를 정하지 않습니다.** 매 프레임 제자리로
      // 당기면 나온 카드가 줄과 판 위 사이에서 떨립니다.
      if (this.borrowed.has(card.uid)) return

      const chosen = this.selected.has(card.uid)
      view.selected = chosen
      // 고른 것이 하나도 없으면 아무것도 물러나지 않습니다 — 고르기 전에 화면이 어두워지면
      // 무엇이 잘못된 것처럼 보입니다.
      // 도움을 받는 카드는 물러나지 않습니다 — 어두워진 카드를 권할 수는 없습니다.
      const hint = !chosen && this.hinted.has(card.uid)
      view.hint = hint
      view.setPick(chosen ? 1 : this.selected.size === 0 || hint ? 0 : -1, PICK_TINT)

      // **한 줄로 폅니다.** 가운데를 높이고 양끝을 기울여 부채꼴로 폈는데, 여덟 장이
      // 늘어서면 그 곡선이 카드마다 다른 높이와 기울기가 되어 줄이 고르지 않게 보입니다 —
      // 손패는 늘어놓은 것이지 쥐고 있는 것이 아닙니다.
      const spotX = startX + index * spacing
      const spotY = HAND_Y
      const tilt = 0
      // 끌고 있는 카드는 손가락이 자리를 정합니다. 여기서 다시 놓으면 커서에서 떨어집니다.
      if (this.game.input.drag?.kind === 'hand' && this.game.input.drag.uid === card.uid
          && this.game.input.drag.moved) return
      // **겹치는 차례도 여기서 정합니다.** 손패는 9장부터 서로 겹치므로(간격이 카드보다
      // 좁아집니다) 이 값이 없으면 정렬한 뒤의 겹침이 깔린 순서로 남습니다.
      view.rowZ = ROW_Z + index
      view.zIndex = view.hovered ? HOVER_Z
        : this.selected.has(card.uid) ? PICK_Z : view.rowZ
      // 갓 뽑힌 카드는 **절도 있게** 자리에 붙고, 나머지는 부드럽게 자리를 옮깁니다.
      // 뒤집는 시각은 깔기가 예약한 것입니다. 예약 없이 온 카드(판을 이어서 열 때)는
      // 닿을 즈음에 뒤집힙니다.
      if (fresh) {
        const flipAt = this.flipAt.get(card.uid) ?? this.game.clock
          + this.game.feel.drawLandMs / 1000
        this.flipAt.delete(card.uid)
        view.deal(spotX, spotY, tilt, flipAt)
        // 보스가 걸어 둔 것이 있으면 이 카드가 뒤집힌 뒤에 걸립니다.
        this.castOnDealt(card.uid, view, flipAt)
      } else {
        view.place(spotX, spotY, tilt)
      }
    })
  }

  /**
   * 능력을 빌리는 줄을 다시 긋습니다.
   *
   * **매 프레임 다시 긋지 않습니다.** 줄이 바뀌는 것은 자리가 바뀔 때뿐이고, 숨 쉬는
   * 것은 짙기 하나로 충분합니다 — 그리는 것은 다시 세울 때만 합니다.
   */
  private syncBorrow(spots: { startX: number; spacing: number }): void {
    const g = this.borrowLink
    g.clear()
    g.visible = false
    if (this.game.session.scene !== 'run') return

    const jokers = this.game.state.jokers
    for (let slot = 0; slot < jokers.length; slot++) {
      const lender = borrowedFrom(this.game.data, jokers, slot)
      if (!lender) continue
      const to = jokers.indexOf(lender)
      if (to < 0) continue

      const from = spots.startX + slot * spots.spacing
      const at = spots.startX + to * spots.spacing
      // 딱지의 아랫변 바로 밑입니다. 둘을 잇고 양 끝에서 딱지 쪽으로 짧게 올립니다.
      const base = JOKER_Y + SIZE.jokerHeight / 2 + 5
      const lift = 6
      g.moveTo(from, base - lift).lineTo(from, base)
        .lineTo(at, base).lineTo(at, base - lift)
        .stroke({ color: UI.legendary, width: 2, alpha: 0.9 })
      // 빌려오는 쪽 끝에 점 하나. **어느 쪽이 빌리는 것인지가 줄만으로는 없습니다.**
      g.circle(from, base, 3).fill({ color: UI.legendary })
      g.visible = true
    }
  }

  syncJokers(): void {
    const wanted = new Set(this.game.state.jokers.map(joker => joker.uid))

    for (const [uid, view] of this.jokers) {
      if (wanted.has(uid)) continue
      // **곧바로 지우지 않습니다.** 타서 사라지는 것이 보여야 무엇이 없어진 것인지
      // 눈이 따라갑니다. 다 타면 `tick` 이 치웁니다.
      view.ignite()
      this.game.audio.play('joker_burn')
      this.jokers.delete(uid)
      this.burning.push(view)
    }

    // 자리 안에서 몇 개가 어디에 서는가. **개수마다 달라지므로 한 번 세어 돌려 씁니다.**
    const spots = trayRow(JOKER_TRAY, this.game.state.jokers.length)
    this.game.tray.publishRowSpots('joker', spots, this.game.state.jokers.length)
    this.syncBorrow(spots)

    this.game.state.jokers.forEach((joker, index) => {
      // **아직 박자가 닿지 않았으면 이전 모습입니다.** 판이 갈리는 박자가 그 자리에서
      // 뒤집어 갈아 끼웁니다 — 카드의 `pendingCards` 와 같습니다.
      const shown = this.pendingJokers.get(joker.uid) ?? joker
      const look = this.jokerLookOf(shown)

      // 산 딱지가 아직 그 자리에 있는 동안은 세우지 않습니다.
      //
      // **아직 뷰가 없는 것이 방금 들어온 것입니다.** 줄의 끝인지로 보았는데, 자리를
      // 갈아 끼우는 것은 판 그 자리에 들어오므로 끝이 아닙니다 — 그러면 그 하나가
      // 딱지보다 먼저 줄에 놓이고, 딱지는 이미 놓여 있는 것 위로 날아갑니다.
      if (this.game.tray.arriveHold?.kind === 'joker'
          && this.game.clock < this.game.tray.arriveHold.until
          && !this.jokers.has(joker.uid)) return

      let view = this.jokers.get(joker.uid)
      if (!view) {
        view = new JokerView(shown, look)
        view.eventMode = 'static'
        view.cursor = 'pointer'
        view.on('pointerdown', event =>
          this.game.input.beginDrag('joker', joker.uid, view as JokerView, event))
        this.jokers.set(joker.uid, view)
        this.game.board.addChild(view)
        // 위에서 내려옵니다 — **산 것이라면 산 자리에서 옵니다.** 그리는 자리도 함께
        // 옮깁니다: 용수철만 옮기면 한 프레임 동안 화면 왼쪽 위에 놓입니다.
        const home = spots.startX + index * spots.spacing
        const bought = this.game.tray.arriveFrom !== undefined
        const from = this.game.tray.arriveFrom ?? { x: home, y: JOKER_Y - 160 }
        this.game.tray.arriveFrom = undefined
        view.motion.snap(from.x, from.y)
        view.position.set(from.x, from.y)
        // **산 것만 울렁입니다.** 판이 시작될 때 딸려 오는 조커까지 울렁이면 그것이
        // 「샀다」의 표시가 되지 못합니다.
        if (bought) view.buying()
      } else {
        view.set(shown, look)
      }

      if (this.game.input.drag?.kind === 'joker' && this.game.input.drag.uid === joker.uid
          && this.game.input.drag.moved) return
      // **고른 딱지는 커서에 반응하지 않습니다.** 까닭은 `HELD_RISE` 에 있습니다.
      view.held = this.game.tray.held?.kind === 'joker' && this.game.tray.held.uid === joker.uid
      const lifted = view.held ? HELD_RISE : 0
      // 손패와 같습니다 — 줄이 자리를 넘칠 만큼 차면 겹치므로, 겹치는 차례가 발동하는
      // 차례와 같아야 합니다.
      view.rowZ = ROW_Z + index
      view.zIndex = view.hovered ? HOVER_Z : view.held ? PICK_Z : view.rowZ
      view.place(spots.startX + index * spots.spacing, JOKER_Y - lifted)
    })
  }

  /** 「적용 중」 판을 열고 닫습니다. */
  toggleActive(): void {
    if (this.game.panels.modals.has(this.game.panels.activePanel)) {
      this.game.panels.modals.close(this.game.panels.activePanel)
      return
    }
    this.game.panels.drawActivePanel()
    this.game.panels.modals.open(this.game.panels.activePanel)
  }
}
