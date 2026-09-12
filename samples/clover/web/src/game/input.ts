import { Container, type FederatedPointerEvent } from 'pixi.js'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { evaluate } from '../core/hand'
import { t, tf } from '../core/strings'
import { outlined } from '../ui/font'
import { bestHand, valueOf } from '../core/suggest'
import { type CardInstance } from '../core/state'
import { CardView } from '../render/card-view'
import { JokerView } from '../render/joker-view'
import { faceEdition } from '../render/faces'
import { SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { richLine, richStyle } from '../ui/rich'
import { Toasts } from '../ui/toast'
import { type TipBox, Tooltip } from '../ui/tooltip'
import { attachTip, TipHold } from '../ui/tip'
import { DRAG_Z, HAND_Y, JOKER_TRAY, JOKER_Y, TILT_REACH, trayRow } from './metrics'
import { near } from './helpers'
import { type LookTick } from './types'
import { type Game } from './game'
export class InputPart {
  constructor(private readonly game: Game) {}

  readonly tooltip = new Tooltip()

  /**
   * 무엇이 일어났는지 알리는 줄들.
   *
   * **판의 오른쪽에 놓입니다.** 가운데에 세우면 낸 카드를 덮고, 그러면 읽는 것이 아니라
   * 사라지기를 기다리게 됩니다.
   */
  // **처음 서는 자리는 판 밖입니다.** 게임이 뜨는 곳이 로그인 화면이나 타이틀이므로,
  // 판 안의 자리로 시작하면 첫 알림 하나가 구석에 납니다.
  readonly toasts = new Toasts(Toasts.OUT_RUN)

  /**
   * 끌고 있는 것.
   *
   * **자리가 규칙입니다.** 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터이므로,
   * 무엇을 어디에 두느냐가 최종 점수를 바꿉니다 — 그것을 정하지 못하면 판을 짜는 일의
   * 절반이 없습니다.
   */
  drag?: {
    kind: 'hand' | 'joker'
    uid: number
    startX: number
    startY: number
    grabX: number
    moved: boolean
    /** 끌기 시작할 때의 줄. **되돌아왔으면 적지 않습니다** — 한 수가 헛되이 쌓입니다. */
    order: number[]
    /** 누른 것과 끈 것을 가르는 거리. **손가락은 마우스보다 넉넉해야 합니다.** */
    slack: number
  }

  /**
   * 지금 무엇을 하면 되는가.
   *
   * **국면마다 한 줄입니다.** 화면에 버튼이 여럿 있어도 다음에 누를 것이 무엇인지가
   * 적혀 있지 않으면 처음 여는 사람은 움직이지 못합니다.
   */
  readonly hint = new Container()

  /** 지금 적혀 있는 지시문. 같은 글이면 다시 만들지 않습니다. */
  hintShown = ''

  /**
   * 새 조커가 날아오는 자리.
   *
   * **산 것은 산 자리에서 옵니다.** 위에서 떨어지면 어느 칸을 눌러서 얻은 것인지가
   * 남지 않습니다. 한 장에 한 번만 쓰이므로 쓰고 나면 비웁니다.
   */
  /**
   * 아무것도 없는 곳을 누른 횟수.
   *
   * **도구가 자기 좌표가 맞는지 물을 수 있는 유일한 창구입니다.** 사람은 눌러 보면 아무
   * 일도 없는 것을 곧 알아채지만, 도구는 그다음 줄로 그냥 넘어갑니다.
   */
  blankTaps = 0

  /** 마지막 누름이 어디까지 닿았는가. 캔버스(DOM)와 무대(Pixi) 둘입니다. */
  lastPointer?: {
    dom?: { type: string; x: number; y: number; at: number }
    stage?: { type: string; target: string; x: number; y: number; at: number }
  }

  /** 마지막으로 센 최선의 조합. 패가 같으면 다시 세지 않습니다. `act` 가 비웁니다. */
  hintCache?: { key: string; best: ReturnType<typeof bestHand> }

  pointerAt = { x: 0, y: 0 }

  /**
   * 지난 프레임 이후 포인터가 실제로 자리를 옮겼는가.
   *
   * **설명이 뜨는 조건은 「밑에 있는 것이 달라졌다」가 아니라 「커서가 옮겨 가서 들어왔다」
   * 입니다.** 조커 줄은 사고 팔고 순서를 바꿀 때마다 다시 배치되므로, 달라진 것만 보면
   * 커서가 한 픽셀도 움직이지 않았는데 지나가는 조커마다 차례로 설명이 뜹니다.
   *
   * 같은 자리로 오는 이동 사건이 있으므로 좌표를 견주어서 세웁니다.
   */
  pointerMoved = false

  /**
   * 설명 쪽지가 지금 설명하고 있는 조커.
   *
   * **커서 밑에 있는 것과 따로 셉니다.** 밑에 있는 것은 `JokerView.hovered` 가 이미
   * 나타내고, 그것은 계속되는 상태입니다 — 설명은 들어온 그 한 번의 사건이므로 무엇을
   * 띄웠는지를 따로 기억해야 합니다.
   */
  /** 지금 커서 밑에 있어 설명이 뜬 것. 조커 딱지이거나 손패의 카드입니다. */
  tipUnder?: Container

  /** 그때 띄운 쪽지가 몇 번째 것이었는가. **닫을 때 남의 쪽지를 걷지 않으려고 들고 있습니다.** */
  tipOpen = -1

  /**
   * 꾸욱 누르고 있는 것.
   *
   * **손가락으로 설명을 보는 길입니다.** 마우스는 올리면 뜨지만 손가락에는 「올린다」가
   * 없습니다 — 누른 자리와 시각을 적어 두고, 그 자리에서 오래 있으면 설명을 띄웁니다.
   */
  readonly hold = new TipHold(event => this.game.world.toLocal(event.global))

  /**
   * 마지막으로 쓴 것이 손가락인가.
   *
   * **조커와 손패의 설명은 마우스의 자리를 보고 뜁니다.** 손가락은 뗀 뒤에도 그 자리가
   * 그대로 남으므로, 그 길로는 설명이 떼고 나서도 붙어 있습니다.
   */
  touching = false

  /**
   * 뒤로.
   *
   * **`ESC` 와 안드로이드의 뒤로 가기가 같은 자리입니다.** 둘은 같은 뜻이므로 하는 일도
   * 같아야 하고, 두 곳에 따로 적으면 언젠가 한쪽만 고쳐집니다.
   *
   * 차례가 셋입니다 — 떠 있는 판을 닫고, 없으면 들고 있는 것을 놓고, 그것도 없으면
   * 나갈지 묻습니다.
   *
   * **나갈 수 없는 자리에서는 마지막 줄이 아무 일도 하지 않습니다.** 브라우저의 탭은
   * 스크립트가 닫지 못하므로, 거기서 `ESC` 를 누를 때마다 「나갈 수 없습니다」가 뜨면
   * 그것은 알림이 아니라 방해입니다.
   */
  /**
   * 이 누름이 고른 것이나 그 단추 줄의 안이었는가.
   *
   * **그 둘은 놓는 자리가 아닙니다.** 고른 딱지를 다시 누르는 것은 놓는 것이고(`pick` 이
   * 합니다), 단추는 그 단추가 하는 일이 곧 놓는 것입니다 — 여기서 먼저 놓아 버리면 같은
   * 딱지를 다시 눌러도 놓이지 않고 다시 골라집니다.
   */
  pressedHeld(target: unknown): boolean {
    const node = target as Container | null
    if (!node) return false
    for (let at: Container | null = node; at !== null; at = at.parent) {
      if (at === this.game.chrome.heldBar) return true
      if (this.game.tray.heldNode && !this.game.tray.heldNode.destroyed
          && at === this.game.tray.heldNode) return true
    }
    return false
  }

  /**
   * 손을 뗐습니다. **고른 것 밖을 누른 것이면 놓습니다.**
   *
   * 고른 것이 누를 때와 달라졌으면 그 누름이 이미 다른 것을 골랐거나 놓은 것입니다.
   */
  dismissAfterTap(): void {
    const was = this.game.tray.heldAtPress
    const outside = this.game.tray.pressOutsideHeld
    this.game.tray.heldAtPress = undefined
    this.game.tray.pressOutsideHeld = false
    // **판이 떠 있어도 놓습니다.** 그 판을 연 누름이 바로 이 누름일 수 있고, 판 뒤에
    // 남은 단추 줄은 판을 닫는 순간 다시 눌러야 할 것으로 보입니다.
    if (!was || !outside) return
    const now = this.game.tray.held
    if (!now || now.kind !== was.kind || now.uid !== was.uid) return
    this.game.tray.held = undefined
    this.game.audio.play('card_select', 0, 0, -8)
    this.game.refresh()
  }

  /**
   * 아무것도 없는 곳을 눌렀습니다. **한 단계만 놓습니다.**
   *
   * `back` 과 같은 사다리를 쓰되 맨 아래 한 칸이 없습니다 — 그쪽은 사람이 「나가기」를
   * 뜻하고 누른 것이므로 판을 접을지 묻지만, 빈자리를 누른 것은 그 뜻이 아닙니다.
   *
   * **고른 것 · 자리를 비우는 판이 모두 같은 규칙입니다.** 상점의 칸도 팩의 카드도 조커
   * 줄도 한 번 누르면 그 밑에 단추가 서는 같은 문법이므로, 놓는 길도 하나여야 합니다.
   *
   * **고른 것을 놓는 것은 여기만이 아닙니다.** 빈자리가 아닌 곳을 눌러도 놓이므로
   * (`dismissAfterTap`) 이 줄에 남은 몫은 자리를 비우던 것을 그만두는 쪽입니다.
   */
  dismissOnBlank(): void {
    if (this.game.panels.modals.busy) return
    if (this.game.tray.held) {
      this.game.tray.held = undefined
      this.game.audio.play('card_select', 0, 0, -8)
      this.game.refresh()
      return
    }
    if (this.game.tray.focus) this.game.tray.leaveFocus()
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
  /**
   * 이 누름이 설명을 띄운 것이었는가.
   *
   * **한 번만 참입니다.** 누름을 다루는 자리마다 맨 앞에서 물어봅니다 — 참이면 그 누름은
   * 설명을 본 것이므로 아무것도 하지 않습니다.
   */
  ate(): boolean {
    return this.hold.ate()
  }

  /**
   * 설명이 뜨는 자리 하나를 답니다.
   *
   * **마우스와 손가락의 길이 다릅니다.** 마우스는 올리면 뜨고 벗어나면 닫히고, 손가락은
   * 꾸욱 누르면 뜹니다 — 부르는 자리마다 그 둘을 따로 적으면 언젠가 한쪽이 빠집니다.
   */
  tipOn(node: Container, show: (at: TipBox) => void): void {
    // **자리는 여기서 셉니다.** 부르는 쪽마다 자기 지역 좌표를 적고 있었고, 그 물건이
    // 층 안에 있으면(상점 판은 올라오는 중에 층이 움직입니다) 그만큼 어긋났습니다 —
    // 물건이 화면에서 차지한 자리를 그 물건에게 물으면 어느 층에 있든 맞습니다.
    // **다 선 물건에만 뜹니다.** 진열이 도는 동안의 칸은 알파가 0이어도 눌리는 자리는
    // 그대로 있어서, 아직 물건이 놓이지도 않은 빈 칸이 설명을 띄우고 있었습니다.
    //
    // **`eventMode` 로 막지 않습니다.** 세울 것 목록은 `refresh` 가 비우므로, 다 서기
    // 전에 그 목록에서 빠진 것은 되돌릴 자리를 잃고 영원히 손이 닿지 않습니다.
    const spot = () => {
      if (node.alpha < 0.99) return
      show(this.tipBox(node))
    }
    attachTip(node, this.hold, spot, () => this.tooltip.hide())
  }

  /**
   * 그 물건이 쪽지의 좌표계에서 차지한 자리.
   *
   * **테두리를 물어봅니다.** 피벗과 배율과 층의 옮김이 저마다 다르므로, 좌표를 손으로
   * 더하면 어느 하나에서 어긋납니다.
   */
  tipBox(node: Container): TipBox {
    const box = node.getBounds()
    const from = this.game.world.toLocal({ x: box.x, y: box.y })
    const to = this.game.world.toLocal({ x: box.x + box.width, y: box.y + box.height })
    return { x: (from.x + to.x) / 2, top: from.y, bottom: to.y }
  }

  updateHover(): void {
    // **손가락에는 「올려 둔다」 가 없습니다.** 손가락은 떼고 나서도 그 자리가 마지막으로
    // 지나간 자리로 남으므로, 누른 조커가 계속 올려진 것으로 셉니다 — 골랐다가 다시 눌러
    // 놓아도 그 카드만 조금 들린 채로 있었고, 다른 카드를 눌러 그 자리가 옮겨질 때에야
    // 내려왔습니다.
    const blocked = this.touching || this.game.panels.modals.busy
      || this.game.shown.phase === 'lost' || this.game.shown.phase === 'won'
    // **손패를 만질 수 있는 때만 카드에 올려집니다.**
    //
    // 뽑는 동안이 그 하나였습니다 — 마우스가 나오는 길목에 있으면 지나가는 카드마다 차례로
    // 들려 올라가고, 그것은 고르는 것으로도 지나가는 것으로도 읽히지 않습니다. **득점이
    // 도는 동안도 같습니다** — 그때 보는 것은 판에 올라간 카드이고, 밑에 남은 손패가
    // 커서를 따라 들리면 눈이 그쪽으로 끌립니다.
    //
    // **조커 줄은 막지 않습니다.** 득점이 도는 동안 어느 조커가 무엇을 내는지는 그 설명으로
    // 읽으므로, 그쪽은 그때가 오히려 볼 때입니다.
    const quiet = !this.game.handLive
    // **뜯은 팩은 판 위에 펼쳐집니다.** 그 아래의 손패까지 커서를 받으면 펼친 카드 뒤에서
    // 카드가 들립니다 — 조커 줄은 팩 위쪽에 그대로 놓여 있으므로 그쪽은 막지 않습니다.
    // 조커의 설명이 이 길로만 뜨고, 무엇을 집을지는 지금 든 조커를 읽고 정합니다.
    const overlaid = this.game.state.pack !== null

    let card: CardView | undefined
    let joker: JokerView | undefined

    if (!blocked) {
      for (const view of this.game.cards.views.values()) {
        if (quiet || overlaid) break
        if (!near(this.pointerAt, view.motion, SIZE.cardWidth, SIZE.cardHeight)) continue
        if (!card || view.motion.x.target > card.motion.x.target) card = view
      }
      for (const view of this.game.cards.jokers.values()) {
        if (!near(this.pointerAt, view.motion, SIZE.jokerWidth, SIZE.jokerHeight)) continue
        if (!joker || view.motion.x.target > joker.motion.x.target) joker = view
      }
    }

    for (const view of this.game.cards.views.values()) {
      view.hovered = view === card
      // **겹치는 차례도 커서를 따라갑니다.** 고른 것은 올라온 채로 남고, 가리킨 것은
      // 가리키는 동안만 올라옵니다 — 떼면 줄의 차례로 돌아갑니다.
      this.game.cards.restackRow(view, view.hovered, this.game.cards.selected.has(view.uid))
    }
    for (const view of this.game.cards.jokers.values()) {
      view.hovered = view === joker
      this.game.cards.restackRow(view, view.hovered,
        this.game.tray.held?.kind === 'joker' && this.game.tray.held.uid === view.uid)
    }

    // **여는 쪽만 커서의 움직임을 묻습니다.** 밑에 아무것도 없으면 커서가 가만히 있어도
    // 닫습니다 — 팔려 없어진 조커의 설명이 화면에 남으면 안 됩니다.
    //
    // **손가락으로는 이 길을 쓰지 않습니다.** 손가락은 뗀 뒤에도 그 자리가 그대로 남아서,
    // 설명이 떼고 나서도 붙어 있습니다 — 손가락은 꾸욱 눌러서 봅니다.
    //
    // **손패의 카드도 이 길을 씁니다.** 손가락으로는 꾸욱 눌러 볼 수 있는 것이 마우스로는
    // 볼 수 없었습니다 — 강화와 인장이 붙은 카드가 무슨 값을 내는지는 카드의 얼굴만으로
    // 알 수 없습니다. 겹쳐 있으면 조커가 먼저입니다(조커 자리가 위에 있습니다).
    const under: Container | undefined = joker ?? card
    if (under !== this.tipUnder) {
      // 밑에 있는 것이 달라졌으면 앞의 설명은 더 이상 그 자리의 것이 아닙니다. 팔려
      // 없어진 조커의 설명이 남지 않게, 닫는 것은 커서의 움직임을 묻지 않습니다.
      if (this.tipUnder) {
        // **내가 띄운 그 쪽지일 때만 닫습니다.** 조커에서 커서를 떼어 펼친 팩의 카드나
        // 상점의 칸으로 옮기면, 그것이 자기 노드로 띄운 쪽지가 이 프레임 끝의 닫음에
        // 함께 걷혔습니다 — 옮겨 간 그 자리의 설명은 남아 있어야 합니다.
        if (this.tooltip.opens === this.tipOpen) this.tooltip.hide()
        this.tipUnder = undefined
      }
      if (under && this.pointerMoved && !this.touching) {
        if (joker) this.game.tray.showTooltip(joker)
        else if (card) {
          const held = this.game.state.deck.find(one => one.uid === card.uid)
          if (held) this.game.panels.showCardTip(held, false, true, card)
        }
        this.tipUnder = under
        this.tipOpen = this.tooltip.opens
      }
    }
    // 이 프레임의 움직임은 여기서 다 쓰였습니다. **`updateHover` 가 프레임마다 한 번
    // 불리는 유일한 자리이므로** 여기서 내립니다.
    this.pointerMoved = false
  }

  /**
   * 이 통의 겉면을 시계에 붙일 것. 붙일 것이 없으면 `undefined` 입니다.
   *
   * 딱지(`JokerView`)이면 그것이 알아서 돌고, 얼굴이면 거기 걸린 판의 셰이더 하나입니다 —
   * `itemCard` 가 둘 중 하나를 돌려주므로 부르는 쪽은 가리지 않습니다.
   */
  lookOf(node: Container, tilt?: () => number): LookTick | undefined {
    if (node instanceof JokerView) {
      return { at: time => node.lookAt(time), seen: () => node.editionAt }
    }
    const one = faceEdition(node)
    if (!one) return undefined
    // **기울기가 한 프레임에 뛰지 않습니다.** 줄에 선 딱지는 뷰가 스스로 따라가는데
    // (`JokerView.easePointer`) 이쪽은 뷰가 아니라 얼굴이므로 그 몫을 여기서 듭니다 —
    // 받은 값을 그대로 위상에 더하면 커서가 뛴 만큼 무늬가 순간 이동합니다.
    //
    // **한 프레임의 몫이 고정입니다.** 이 표들은 `advanceLooks` 가 프레임마다 한 번
    // 돌리므로 초를 받지 않고, 60Hz 에서 뷰와 같은 빠르기가 되는 값입니다.
    let eased = tilt?.() ?? 0
    return {
      at: time => {
        eased += ((tilt?.() ?? 0) - eased) * 0.14
        one.at(time, eased)
      },
      seen: () => one.seen,
    }
  }

  /** 줄 밖에 선 것들의 겉면을 한 틱. **표 넷을 그대로 걷습니다.** */
  advanceLooks(): void {
    for (const [, one] of this.game.shop.shopTiles) one.look?.at(this.game.clock)
    for (const one of this.game.pack.packViews.values()) one.look?.at(this.game.clock)
    for (const one of this.game.tray.consumableTiles) one.look?.at(this.game.clock)
    for (const one of this.game.cards.gameOverJokers) {
      if (!one.view.destroyed) one.view.lookAt(this.game.clock)
    }
  }

  /** 이 가로 자리에서의 기울기. 커서가 가까울수록 0 에 가깝습니다. */
  tiltAt(x: number): number {
    // 카드 한 장 너비를 1 로 셉니다.
    const away = (this.pointerAt.x - x) / 90
    // **멀면 0 입니다.**
    //
    // 잘라 두기만 했더니 한 장 너비를 넘어선 것이 전부 ±1 이었습니다 — 커서가 화면
    // 반대쪽에 있으면 줄에 선 것이 다 최대로 기운 채이고, 커서가 줄을 지나가면 그 값이
    // +1 에서 −1 로 한꺼번에 뒤집혀 무늬의 위상이 1.2 라디안 뜁니다. 그것이 마우스를
    // 움직일 때 무늬가 밀리는 것으로 보였습니다.
    //
    // **닿는 거리는 세 장입니다.** 그보다 멀면 그 딱지는 커서와 상관없이 제 위상으로
    // 흐르고, 가까울수록 커서 쪽으로 기웁니다.
    const reach = Math.max(0, 1 - Math.abs(away) / TILT_REACH)
    return Math.max(-1, Math.min(1, away)) * reach
  }

  tiltFor(view: Container): number {
    return this.tiltAt(view.x)
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
  updateHints(): void {
    this.game.cards.hinted.clear()
    if (this.game.state.phase !== 'round') return
    if (!this.game.session.settings.hints) return
    // **핸드가 도는 동안에는 권하지 않습니다.** 득점을 보고 있는데 패의 카드들이 따로
    // 깜빡이면 눈이 둘로 갈리고, 그때는 고를 수도 없습니다.
    if (!this.game.presented) return

    const held = this.game.state.hand
      .map(uid => this.game.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)
    if (held.length === 0) return

    // **같은 패면 다시 세지 않습니다.** 부분집합 전수라 패가 8장이면 218번, 12장이면
    // 1,585번 족보를 세고, 카드를 고르고 푸는 것마다 `refresh` 가 여기를 지납니다 — 패와
    // 규칙은 액션으로만 바뀌므로 `act` 와 새 판이 이것을 비웁니다.
    const key = this.game.state.hand.join(',')
    if (this.hintCache?.key !== key) {
      this.hintCache = { key, best: bestHand(this.game.data, this.game.state, held) }
    }
    const best = this.hintCache.best
    if (!best) return

    const picked = held.filter(card => this.game.cards.selected.has(card.uid))
    const now = picked.length > 0 ? valueOf(this.game.data, this.game.state, picked) : undefined
    if (now && now.value >= best.value) return

    for (const card of best.cards) {
      if (!this.game.cards.selected.has(card.uid)) this.game.cards.hinted.add(card.uid)
    }
    // 고른 것 전부가 최선의 조합에 들어 있지 않으면 권할 것이 없습니다 — 지금 고른 것을
    // 풀어야 하는 상황이므로 카드 표시로는 알릴 수 없습니다.
    const inBest = new Set(best.cards.map(card => card.uid))
    if (picked.some(card => !inBest.has(card.uid))) this.game.cards.hinted.clear()
  }

  /**
   * 지시문 한 줄.
   *
   * **수와 이름은 다른 색입니다.** 「최대 5장」 · 「남은 핸드 3회」에서 사람이 찾는 것은 그
   * 수이고, 문장과 같은 색이면 문장을 처음부터 읽어야 찾습니다.
   */
  drawHint(text: string): void {
    if (text === this.hintShown) return
    this.hintShown = text
    this.hint.removeChildren().forEach(child => child.destroy())
    if (text === '') return
    // **배경 위에 그대로 놓이는 글입니다.** 판때기가 없으므로 배경의 무늬가 밝은 자리에서
    // 회색 글이 반투명한 것처럼 보였습니다 — 테를 두르고 밝기를 한 칸 올립니다. 작은
    // 화면에서 특히 그랬으므로 크기도 두 칸 키웁니다.
    const line = richLine(text, richStyle('body', {
      ...outlined(TEXT.body, UI.outline), fontWeight: WEIGHT.normal,
    }))
    line.position.set(-line.width / 2, 0)
    this.hint.addChild(line)
  }

  /** 지금 국면에서 다음에 할 것. */
  hintText(): string {
    const state = this.game.state
    switch (state.phase) {
      // 블라인드 선택에는 지시문을 두지 않습니다. **판마다 자기 버튼에 적혀 있습니다.**
      case 'blind-select': return ''
      case 'round':
        if (this.game.cards.selected.size === 0) {
          return this.game.cards.hinted.size > 0
            ? tf('ui.hint.best_hand', { n: this.game.cards.hinted.size })
              + tf('ui.hint.hands_left', { n: state.handsLeft })
            : tf('ui.hint.pick_cards', { n: this.game.data.run.maxPlayedCards })
              + tf('ui.hint.hands_left', { n: state.handsLeft })
        }
        return tf('ui.hint.selected', { n: this.game.cards.selected.size })
          + t('ui.hint.discard_to_swap')
      case 'shop':
        // **뜯은 팩에는 그 판이 적습니다.** 여기에도 적으면 덮개 뒤에서 흐릿하게 읽히고,
        // 그것은 남은 글자로 보입니다. 상점도 판 하나이고 그 안에 다 적혀 있습니다.
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
  /**
   * 고른 것의 칩과 배수를 먼저 보입니다.
   *
   * **내기 전에 보여야 고를 수 있습니다.** 두 수가 득점할 때에야 나타나면, 무엇을 고를지는
   * 판 가운데의 작은 상자를 읽어서 정하게 됩니다 — 값이 나오는 자리에 값이 미리 있어야
   * 합니다.
   *
   * 연출이 도는 동안에는 손대지 않습니다. 그때의 두 칸은 지금 세고 있는 값입니다.
   */
  previewSlots(): void {
    // 라운드가 아니면 지웁니다. **그대로 두면 상점에 든 뒤에도 족보 이름이 남습니다.**
    if (this.game.state.phase !== 'round') {
      this.game.chrome.handLabel.text = ''
      return
    }
    if (!this.game.presented) return

    const picked = this.game.cards.orderedSelection()
      .map(uid => this.game.state.deck.find(card => card.uid === uid))
      .filter((card): card is CardInstance => card !== undefined)

    if (picked.length === 0) {
      this.game.chrome.handLabel.text = ''
      this.game.chrome.chips.target = 0
      this.game.chrome.mult.target = 0
      return
    }

    const { hand } = evaluate(picked, this.game.state.rules)
    const row = this.game.data.tables.pokerHand.findByHand(hand)
    const level = this.game.state.handLevels[PokerHandKind[hand]] ?? 1
    this.game.chrome.handLabel.text = tf('ui.hand.level',
      { name: this.game.panels.handName(hand), level })
    this.game.chrome.chips.target = (row?.baseChips ?? 0) + (row?.chipsPerLevel
        ?? 0) * (level - 1)
    this.game.chrome.mult.target = (row?.baseMult ?? 0) + (row?.multPerLevel ?? 0) * (level - 1)
  }

  /**
   * 끌기를 시작합니다.
   *
   * 아직 끄는 것인지 누르는 것인지 모릅니다 — 손가락이 몇 px 움직이고 나서야 갈립니다.
   */
  beginDrag(kind: 'hand' | 'joker', uid: number, view: Container,
                    event?: FederatedPointerEvent): void {
    if (this.game.player.busy || this.game.panels.modals.busy) return
    // **득점이 도는 동안 손패는 만질 수 없습니다.** 그때는 낸 카드가 하나씩 세어지는
    // 것을 보는 자리이고, 밑에 남은 손패는 아직 그 판에 쓸 것이 아닙니다 — `player.busy`
    // 만으로는 박자가 다 지난 뒤부터 다음 패가 깔리기까지가 열려 있어서, 그 사이에 고른
    // 것이 새 패에 그대로 남았습니다.
    if (kind === 'hand' && !this.game.handLive) return

    // **누른 그 자리에서 시작합니다.** 마우스는 누르기 전에 움직이므로 마지막으로 지나간
    // 자리가 곧 누른 자리이지만, **손가락은 누르는 그 순간에 처음 나타납니다** — 그때의
    // 자리를 쓰지 않으면 지난 자리와의 차이만큼 카드가 튀고, 그 튐이 「끌었다」로 읽혀
    // 손을 떼도 고르는 것이 되지 않습니다.
    if (event) this.pointerAt = this.game.world.toLocal(event.global)

    this.drag = {
      kind, uid, moved: false,
      order: kind === 'hand'
        ? this.game.state.hand.slice() : this.game.state.jokers.map(joker => joker.uid),
      startX: this.pointerAt.x, startY: this.pointerAt.y,
      grabX: this.pointerAt.x - view.x,
      // **손가락은 가만히 있어도 흔들립니다.** 마우스와 같은 문턱을 두면 누르려던 것이
      // 끄는 것으로 읽힙니다.
      slack: event?.pointerType === 'touch' ? 18 : 6,
    }

    // **손가락으로 설명을 보는 길입니다.** 마우스는 올리면 뜨지만 손가락에는 그것이
    // 없으므로, 누른 채로 기다리면 뜹니다 — 그러면 그 누름은 고르는 것이 아닙니다.
    if (!event) return
    if (kind === 'joker') {
      const view = this.game.cards.jokers.get(uid)
      if (view) this.hold.arm(event, () => this.game.tray.showTooltip(view))
      return
    }
    const card = this.game.state.deck.find(one => one.uid === uid)
    // 손에 있는 것이므로 「덱에 남았다」가 아니라 「손에 있다」입니다.
    if (card) this.hold.arm(event, () => this.game.panels.showCardTip(card, false, true, view))
  }

  /**
   * 끄는 동안.
   *
   * **자리를 바로바로 바꿉니다** — 손을 뗀 뒤에 한 번에 정리하면 어디에 놓이는 것인지
   * 모르는 채로 끌게 됩니다.
   */
  advanceDrag(): void {
    const drag = this.drag
    if (!drag) return

    if (!drag.moved) {
      const far = Math.abs(this.pointerAt.x - drag.startX) > drag.slack
        || Math.abs(this.pointerAt.y - drag.startY) > drag.slack
      if (!far) return
      drag.moved = true
      this.game.audio.play(drag.kind === 'hand' ? 'card_select' : 'joker_move', -4)
    }

    const x = this.pointerAt.x - drag.grabX
    const order = drag.kind === 'hand'
      ? this.game.state.hand
      : this.game.state.jokers.map(joker => joker.uid)
    const current = order.indexOf(drag.uid)
    if (current < 0) return

    // **조커도 손패처럼 개수에 따라 자리가 달라집니다.** 간격을 고정으로 세면 좁게 선
    // 줄에서 손가락이 있는 칸과 계산한 칸이 어긋납니다.
    const row = drag.kind === 'hand'
      ? this.game.cards.handSpots : trayRow(JOKER_TRAY, order.length)
    const target = Math.max(0, Math.min(order.length - 1,
      Math.round((x - row.startX) / Math.max(1, row.spacing))))

    if (target !== current) {
      if (drag.kind === 'hand') {
        const next = this.game.state.hand.slice()
        next.splice(target, 0, ...next.splice(current, 1))
        this.game.state.hand = next
        // **화면이 그리는 것은 `shown.hand` 입니다.** 이것을 함께 바꾸지 않으면 끌어다
        // 놓아도 자리가 하나도 움직이지 않고 제자리로 돌아갑니다 — 정렬과 같습니다.
        const seen = new Set(this.game.shown.hand)
        this.game.shown.hand = next.filter(uid => seen.has(uid))
      } else {
        const next = this.game.state.jokers.slice()
        next.splice(target, 0, ...next.splice(current, 1))
        this.game.state.jokers = next
      }
      this.game.audio.play(drag.kind === 'hand' ? 'card_select' : 'joker_move', target * 2)
      this.game.refresh()
    }

    // 끌리는 것은 커서를 따라오고 조금 들립니다. **다른 것들 위에 있어야** 어디로 가는지
    // 보입니다.
    //
    // **`zIndex` 로 올립니다.** 판은 `zIndex` 로 정렬하므로 자식 순서를 옮기는 것은 값이
    // 같은 것들 사이에서만 듣고, 줄에 선 것들은 이제 저마다 값을 가집니다. 놓으면 다시
    // 그리기가 색인대로 되돌립니다.
    const view = drag.kind === 'hand'
      ? this.game.cards.views.get(drag.uid) : this.game.cards.jokers.get(drag.uid)
    if (view) {
      view.zIndex = DRAG_Z
      view.place(x, (drag.kind === 'hand' ? HAND_Y : JOKER_Y) - 22, 0)
    }
  }

  /**
   * 바뀐 차례를 런에 적습니다.
   *
   * **자리를 옮기는 것도 액션입니다.** 화면에서만 옮기면 이어서 하는 판이 옮기기 전의
   * 차례로 되살아나고, 조커의 차례는 점수를 바꾸므로 그것은 다른 판입니다. 적어 두지
   * 않은 채로 다음 액션이 저장되면 해시가 어긋나 저장이 통째로 버려집니다.
   *
   * **잇달아 옮긴 것은 하나로 묶습니다.** 사이에 아무 액션도 없는 두 수는 뒤의 것만
   * 남겨도 같은 판이고, 묶지 않으면 제출의 상한이 자리를 옮긴 횟수로 찹니다.
   */
  recordOrder(what: 'hand' | 'joker', before: number[]): void {
    if (this.game.session.scene !== 'run') return
    const order = what === 'hand'
      ? this.game.state.hand.slice() : this.game.state.jokers.map(joker => joker.uid)
    if (order.join(',') === before.join(',')) return
    const last = this.game.session.actions[this.game.session.actions.length - 1]
    if (last?.t === 'reorder' && last.what === what) this.game.session.actions.pop()
    this.game.session.actions.push({ t: 'reorder', what, order })
    this.game.session.rememberRun()
  }

  /** 손을 뗍니다. 움직이지 않았으면 끈 것이 아니라 누른 것입니다. */
  endDrag(): void {
    const drag = this.drag
    this.drag = undefined
    if (!drag) return

    if (!drag.moved) {
      // 꾸욱 눌러 설명을 본 것이면 고르지 않습니다.
      if (this.ate()) return
      if (drag.kind === 'hand') this.game.cards.toggle(drag.uid)
      else this.game.tray.pick('joker', drag.uid)
      return
    }
    this.game.audio.play(drag.kind === 'hand' ? 'card_place' : 'joker_move')
    this.recordOrder(drag.kind, drag.order)
    // **겹치는 차례는 다시 그리기가 되돌립니다.** 끄는 동안 `DRAG_Z` 로 올렸고, 다시
    // 그리면 `ROW_Z + 색인` 이 다시 걸립니다 — 여기서 자식 순서를 손으로 되돌리던 것이
    // 겹침을 정하는 두 번째 자리였고, 정렬 단추는 그 자리를 지나지 않았습니다.
    this.game.refresh()
  }
}
