import { COLOR } from '../render/ink'
import { Container, Rectangle, Text } from 'pixi.js'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { describe } from '../core/describe'
import { type Action } from '../core/run'
import { nameOf, t, tf } from '../core/strings'
import { sellValueOf, type ShopItem } from '../core/shop'
import { ArriveFilter } from '../shader/arrive'
import { ERODE_SWEEP, ErodeFilter } from '../shader/erode'
import { startMotes } from '../render/motes-layer'
import { JokerView } from '../render/joker-view'
import { fraction, Motion, Spring, sway } from '../render/motion'
import { faceOf, itemFace, shopLabel } from '../render/faces'
import { SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { box, type Box } from '../ui/layout'
import { Button, Panel } from '../ui/widgets'
import { richLine, richStyle } from '../ui/rich'
import { TIP_GROW, TIP_RISE } from '../ui/tip'
import {
  BOARD_X, BUY_LINGER, CONSUMABLE_TRAY, DECK_PEEK, DECK_X, DECK_Y, FOCUS_H, FOCUS_W, FOCUS_Y,
  HELD_EDGE, HELD_H, HELD_RISE, HOVER_Z, ITEM_ARRIVE, ITEM_FLASH, ITEM_HOLD, ITEM_SHAKE,
  ITEM_SHAKE_TILT, ITEM_WARP, JOKER_TRAY, JOKER_Y, LAND_AT, PACK_CARD_H, PACK_CARDS_Y,
  PACK_SCALE, PACK_X, PICK_Z, PLAY_Y, RISER_ON_CARD, ROW_Z, SHOP_RETURN_REST, trayRow,
} from './metrics'
import { isConsumable } from './tables'
import { newest } from './helpers'
import { type LookTick } from './types'
import { type Game } from './game'
/**
 * 소모품 한 장이 지금 얼마나 기울어 있는가. 라디안입니다.
 *
 * **만드는 자리와 옮기는 자리가 같은 식을 씁니다.** 둘이 갈리면 새로 만든 칸이 한 프레임
 * 동안 다른 기울기로 놓입니다.
 */
function itemTilt(uid: number, clock: number): number {
  return sway(clock, uid * 1.7, 1.1, 1.1) * (Math.PI / 180)
}

export class TrayPart {
  constructor(private readonly game: Game) {}

  /**
   * 고른 조커나 소모품 하나.
   *
   * **누르는 것만으로는 아무것도 팔리지 않습니다.** 조커가 판의 전부인 게임에서 한 번
   * 잘못 누른 것이 판을 끝내면 안 됩니다 — 고르면 그 밑에 무엇을 할지가 버튼으로 서고,
   * 그 버튼을 눌러야 일어납니다.
   */
  /**
   * 지금 고른 것 하나.
   *
   * **누르는 것과 하는 것을 가릅니다.** 조커와 소모품은 처음부터 그랬고 — 눌러 고르면 그
   * 밑에 `사용`·`판매` 가 놓입니다 — 상점과 팩만 누르는 그 자리에서 곧바로 되었습니다.
   * 사는 것도 집는 것도 되돌릴 수 없는 일이므로, 한 번 더 눌러야 합니다.
   *
   * 상점과 팩은 `uid` 가 아니라 **칸의 번호**입니다. 그 둘은 개체가 아니라 자리이고,
   * 자리는 상점이 다시 그려질 때마다 새로 만들어지므로 개체로는 가리킬 것이 없습니다.
   */
  held?: { kind: 'joker' | 'consumable' | 'shop' | 'pack' | 'pack_slot' | 'voucher'; uid: number }

  /**
   * 지금 고른 그것이 화면에서 어느 통인가. `syncHeldBar` 이 세울 때 적습니다.
   *
   * **누름이 그것의 안이었는지를 이것으로 봅니다.** 갈래마다 통을 찾는 길이 다르므로
   * (조커는 뷰, 소모품과 상점의 칸은 다시 그릴 때마다 새로 만드는 딱지입니다) 누를 때마다
   * 다시 찾으면 그 다섯 갈래를 누름을 다루는 자리에 한 번 더 적게 됩니다.
   */
  heldNode?: Container

  /** 누르기 시작할 때 고른 것이 무엇이었는가. 손을 뗄 때 그대로인지 봅니다. */
  heldAtPress?: { kind: string; uid: number }

  /** 그 누름이 고른 것과 그 단추 줄의 밖이었는가. */
  pressOutsideHeld = false

  /**
   * 소모품이 들린 높이.
   *
   * **조커와 같은 용수철을 탑니다.** 조커는 `place` 로 목표만 정하고 용수철이 데려가는데,
   * 소모품은 자리를 매번 새로 그리므로 그럴 것이 없습니다 — 높이 하나를 여기 두고 화면이
   * 그것을 따라갑니다. 값이 다르면 나란히 선 둘이 다른 물건처럼 움직입니다.
   */
  readonly consumableLift = new Map<number, Spring>()

  /**
   * 소모품 칸이 지난 자리에서 제자리로 미끄러지는 용수철. 번호로 듭니다.
   *
   * **칸은 다시 그릴 때마다 새로 만들어지므로 그대로 두면 순간이동합니다.** 하나가 타는 동안
   * 남은 칸들이 그 프레임에 새 자리로 뛰어 붙었습니다 — 조커는 용수철로 미끄러지고 상점
   * 딱지는 지난 자리에서 오는데(`shopSpotWas`) 이 줄만 그랬습니다.
   */
  readonly consumableSlide = new Map<number, Spring>()

  /**
   * 소모품 칸이 가리켜져 커지는 용수철. 번호로 듭니다.
   *
   * **조커 딱지와 같은 몸짓입니다**(`TIP_GROW` · `TIP_RISE`). 나란히 놓인 두 줄에서 조커는
   * 커서를 따라 들리고 커지는데 소모품은 쪽지만 떴습니다 — 같은 줄에 두 가지 규칙이
   * 있었습니다.
   */
  readonly consumableGrow = new Map<number, Spring>()

  /** 지금 커서가 올라가 있는 소모품. 없으면 `undefined`. */
  hoveredItem?: number

  /** 지금 그려져 있는 소모품 칸들. 매 프레임 높이를 다시 얹습니다. */
  consumableTiles: {
    uid: number
    tile: Container
    /** 이 칸의 겉면. 판이 걸린 것만 있습니다. */
    look?: LookTick
    /** 이 칸의 제자리. 들리는 것과 오는 것이 이 자리를 기준으로 얹힙니다. */
    baseX: number
    baseY: number
    /** 이 칸이 줄에서 갖는 그리기 차례. 가리키던 커서를 떼면 돌아갈 값입니다. */
    rowZ: number
  }[] = []

  /**
   * 지금 걸려 있는 것들.
   *
   * **토스트는 스치고 지나갑니다.** 무엇이 왜 그런지는 판이 도는 내내 볼 수 있어야 합니다 —
   * 손패가 왜 11장인지, 이번 보스가 무엇을 막고 있는지, 들고 있는 태그가 언제 터지는지.
   */
  readonly activeLayer = new Container()

  /** 고른 것 아래에 선 단추 줄이 차지한 사각형. 검증 도구가 읽습니다. */
  heldBox?: Box

  readonly consumableLayer = new Container()

  /** 들고 있는 태그의 딱지들. */
  readonly tagLayer = new Container()

  arriveFrom!: { x: number; y: number } | undefined

  /**
   * 산 물건이 제 자리에 나타나는 것을 미루는 것. 산 딱지가 남아 있는 동안입니다.
   *
   * **번호가 아니라 갈래입니다.** 산 물건의 번호는 코어를 지나야 알 수 있는데, 코어를 지나는
   * 그 자리에서 화면이 이미 한 번 그려집니다 — 번호를 알고 나서 붙들면 그 물건은 이미 줄에
   * 놓여 있고, 다시 그려도 있는 것을 지우지는 않습니다. **사는 것은 줄의 끝에 붙으므로** 갈래와
   * 시각만 있으면 되고, 그 표시는 액션보다 먼저 세울 수 있습니다.
   */
  /**
   * 산 물건이 제 칸에 서는 것을 미루는 것.
   *
   * **누가 붙들었는지가 함께 적힙니다.** 상점과 팩은 자기가 붙들고 자기가 날리는데, 박자도
   * 그 붙듦을 보고 「내 것」으로 알아서 한 번 더 날렸습니다 — 소모품이 왼쪽에서 미끄러져
   * 자리를 잡다가 사라지고, 그다음에 산 것이 다시 날아왔습니다.
   */
  arriveHold?: { kind: 'joker' | 'item'; until: number; byBeat?: boolean }

  /**
   * 소모품이 오는 길을 누가 드는가.
   *
   * **상점과 팩은 자기가 듭니다** — 산 자리와 집은 자리를 그 둘만 알기 때문입니다. 그
   * 밖에서 생긴 것(조커가 만들고 태그가 주는 것)은 아무도 들지 않아서 칸에 툭 나타났고,
   * 그것을 박자가 듭니다 — 이 표시가 그 둘을 가릅니다.
   */
  itemFlyOwned = false

  /**
   * 판 돈이 나오는 자리.
   *
   * **내놓은 그 물건의 자리입니다.** 판 가운데에서 동전이 솟으면 어느 것을 내놓아 들어온
   * 돈인지가 남지 않습니다 — 특히 바꿀 때는 들어온 것과 나간 것이 잇달아 일어나므로,
   * 둘이 서로 다른 자리에서 시작해야 갈립니다. 한 번 쓰면 비웁니다.
   */
  sellFrom!: { x: number; y: number } | undefined

  /**
   * 사서 오는 소모품 하나.
   *
   * **조커와 달리 뷰가 자기 것을 들고 있지 못합니다** — 소모품 칸은 화면을 다시 그릴
   * 때마다 새로 만들어지므로, 걸어 둔 셰이더가 그 자리에서 없어집니다. 그래서 화면이
   * 들고 있다가 그릴 때마다 다시 겁니다.
   */
  /** 소모품이 올 자리를 잡아 준 횟수와, 잡을 것이 없어 그냥 돌아온 횟수. */
  flyAsked = 0

  flyMissed = 0

  itemArrive?: {
    uid: number
    warp: number
    glow: number
    filter: ArriveFilter
    /**
     * 산 자리.
     *
     * **소모품도 산 자리에서 옵니다.** 조커는 뷰가 용수철을 들고 있어서 날아오는데,
     * 소모품 칸은 화면을 다시 그릴 때마다 새로 만들어지므로 그럴 것이 없습니다 — 그래서
     * 제 칸에 툭 나타났습니다. 오는 동안의 어긋남을 화면이 들고 있다가 매 프레임 얹습니다.
     */
    from: { x: number; y: number }
    /**
     * 오는 길의 용수철. 카드의 가운데를 끌고 갑니다.
     *
     * **조커와 같은 용수철입니다**(`Motion.drift`). 3차 곡선으로 미끄러지게 두었을 때는
     * 나란히 놓이는 두 줄이 다른 법으로 왔습니다 — 조커는 용수철로 닿아 한 번 튀고, 소모품은
     * 곧게 와서 멈췄습니다. 목표는 칸이 그려질 때마다 그 칸의 가운데로 다시 잡습니다
     * (`placeArriving`) — 칸의 자리는 든 개수로 정해지므로 날아오는 동안에도 바뀝니다.
     */
    motion: Motion
  }

  /**
   * 방금 「쓴다」를 누른 소모품.
   *
   * **쓴 것과 판 것을 가릅니다.** 화면은 소모품이 목록에서 없어진 것만 보므로 어느 쪽인지
   * 알 수 없습니다 — 쓴 것은 판 가운데로 나와 번쩍이고, 판 것은 제자리에서 탑니다.
   */
  usedItem?: number

  /**
   * 자리를 비우는 중.
   *
   * **묻는 판이 아니라 줄 자체가 고르는 자리입니다.** 조커와 소모품은 이미 위 줄에 서
   * 있고, 그중 무엇을 내놓을지는 그 줄에서 누르는 것이 가장 짧습니다 — 이름을 적은 목록을
   * 따로 띄우면 같은 것을 두 번 그리는 것이고, 어느 것이 어느 것인지 다시 맞춰 보게 됩니다.
   * 그동안 다른 갈래의 것은 옅어지고, 무엇을 하라는 글이 그 줄 아래에 놓입니다.
   *
   * `slot` 은 상점에서 사려던 칸입니다. 상점은 그동안 그대로 떠 있고 그 딱지가 들려
   * 있습니다 — 무엇을 위해 자리를 비우는지가 거기에 남고, 새 물건은 거기서 떠납니다.
   * 팩에서 왔으면 없습니다. 펼친 그 카드가 그 몫입니다.
   */
  focus?: {
    item: ShopItem
    kind: 'joker' | 'consumable'
    panel: Container
    commit: (held: number) => void
    slot?: number
  }

  /** 자리를 비우는 화면이 든 정도. 0 에서 1 로 갑니다. */
  focusEnter = 0

  readonly focusLayer = new Container()

  /** 조커나 소모품 하나를 고릅니다. 같은 것을 다시 누르면 놓습니다. */
  pick(kind: 'joker' | 'consumable' | 'shop' | 'pack' | 'pack_slot' | 'voucher',
               uid: number): void {
    if (this.game.player.busy) return
    // **끝난 판에서는 고를 수 없습니다.** 지고 나서도 소모품의 「쓴다」 가 눌렸고, 그것을
    // 쓰면 아무 일도 남지 않습니다 — 버리는 것과 같습니다.
    if (this.game.state.phase === 'lost' || this.game.state.phase === 'won') return
    // **고르면 쪽지는 걷힙니다.** 팩의 카드만 걷고 있어서, 상점의 칸을 고르면 쪽지가 뜬 채로
    // 그 밑에 단추가 섰습니다 — 어디서 고르든 같습니다.
    this.game.input.tooltip.hide()
    // **자리를 비우는 동안은 그 줄의 것만 고릅니다.** 상점과 팩의 카드는 막 뒤에 있고,
    // 줄에서도 내놓을 수 있는 것만입니다 — 다른 갈래의 것과 `Eternal` 은 소리로만 거절합니다.
    if (this.focus && (kind !== this.focus.kind || !this.focusEligible(kind, uid))) {
      if (kind === 'joker' || kind === 'consumable') this.game.audio.play('joker_fizzle')
      return
    }

    // **자리를 비우는 동안에는 누르는 것이 곧 고르는 것입니다.**
    //
    // 그 판이 이미 「내놓을 것을 고르십시오」이고, 내놓을 수 있는 것만 들려 있고 나머지는
    // 물러나 있습니다 — 그 위에서 하나를 누르는 것은 묻고 있는 것에 대한 답이므로, 그 밑에
    // 단추를 한 번 더 세우고 그것을 누르게 하는 것은 같은 답을 두 번 받는 것입니다.
    //
    // **그만두는 길은 그대로 있습니다** — 판의 「그만둔다」이고, 누르기 전이면 언제든
    // 물러납니다.
    if (this.focus) {
      const index = kind === 'joker'
        ? this.game.state.jokers.findIndex(one => one.uid === uid)
        : this.game.state.consumables.findIndex(one => one.uid === uid)
      if (index < 0) return
      this.game.audio.play('card_select')
      this.commitFocus(index)
      return
    }

    this.held = this.held?.kind === kind && this.held.uid === uid
      ? undefined : { kind, uid }
    this.game.audio.play('card_select')
    this.game.refresh()
  }

  /** 자리를 비우는 동안 이것을 내놓을 수 있는가. */
  focusEligible(kind: 'joker' | 'consumable', uid: number): boolean {
    if (kind === 'joker') {
      const held = this.game.state.jokers.find(one => one.uid === uid)
      // `Eternal` 은 팔리지 않습니다.
      return held !== undefined && held.sticker !== 1
    }
    return this.game.state.consumables.some(one => one.uid === uid)
  }

  /**
   * 고른 것 밑의 버튼들.
   *
   * **고른 자리 바로 밑입니다.** 화면 구석에 두면 무엇에 대한 버튼인지가 끊깁니다.
   */
  syncHeldBar(): void {
    this.game.chrome.heldBar.removeChildren().forEach(child => child.destroy())
    delete this.game.spots.held
    this.heldBox = undefined
    this.heldNode = undefined
    const held = this.held
    if (!held) return
    // 끝난 판에서는 단추를 세우지 않습니다. 고른 것이 남아 있어도 누를 것이 없습니다.
    if (this.game.state.phase === 'lost' || this.game.state.phase === 'won') {
      this.held = undefined
      return
    }

    let anchor = 0
    // **버튼이 서는 높이가 갈립니다.** 조커와 소모품은 자기 줄 밑이고, 상점의 칸과 팩의
    // 카드는 화면 가운데에 있으므로 그 밑입니다 — 한 높이로 두면 무엇에 대한 버튼인지가
    // 끊깁니다.
    //
    // **줄에서는 단추가 딱지가 서던 자리 안에 들어갑니다.** 아랫변이 딱지의 아랫변이고,
    // 딱지가 `HELD_RISE` 만큼 위로 비켜섭니다 — 아래로 내려가는 것이 하나도 없습니다.
    // 어느 것을 고르든 이 높이는 같으므로 두 번째 누름은 늘 같은 자리입니다.
    //
    // **높이는 어디서나 같습니다.** 줄만 24였고 상점과 팩이 32였습니다 — 한 화면 안에서
    // 두 가지 높이의 단추가 서면 같은 일을 하는 것으로 읽히지 않습니다.
    let baseline = JOKER_Y + SIZE.jokerHeight / 2 - HELD_H
    let height = HELD_H
    const buttons: Button[] = []

    if (held.kind === 'shop') {
      const item = this.game.state.shop.cards[held.uid]
      const one = this.game.shop.shopTiles.get(held.uid)
      if (!item || !one) {
        this.held = undefined
        return
      }
      anchor = one.mid
      this.heldNode = one.tile
      // **물건 바로 밑입니다.** 값이 있던 자리를 단추가 그대로 대신하고, 단추가 값보다
      // 높은 만큼만 물건이 밀려 올라갑니다 — 값이 있던 줄에 맞추었더니 단추가 그림 위에
      // 얹혔고, 칸의 바닥에 맞추었더니 물건과 단추 사이가 벌어졌습니다.
      //
      // **쉬는 자리로 셉니다.** 고른 딱지는 들려 있고, 들린 만큼 단추도 따라 올라가면
      // 단추가 딱지 안으로 파고듭니다.
      baseline = one.holdY
      height = HELD_H
      // **자리가 찼는지는 단추에 나타내지 않습니다.** 말도 색도 하나입니다 — 자리가 없으면
      // 누른 다음에 무엇과 바꿀지 고르는 화면이 서고, 그 화면이 이미 그 말을 합니다.
      // 단추에 미리 적어 두면 같은 것을 두 번 알리는 것이 됩니다.
      buttons.push(new Button(
        // **값은 적지 않습니다.** 딱지에 이미 크게 적혀 있고, 그 바로 밑의 단추가 같은
        // 값을 한 번 더 적으면 그 둘 중 어느 것이 값인지 잠깐 헷갈립니다.
        t('ui.button.buy'), 92, HELD_H, 'primary', () => {
          this.held = undefined
          this.game.shop.buyFrom(held.uid, item)
        }))
    } else if (held.kind === 'pack_slot') {
      const row = this.game.state.shop.packs[held.uid]
      const spot = this.game.pack.packSlotTiles.get(held.uid)
      if (row === undefined || !spot) {
        this.held = undefined
        return
      }
      // **가운데를 딱지가 들고 있습니다.** 상점 카드의 158 을 쓰고 있어서 단추가 27px
      // 오른쪽으로 밀려 옆 팩의 값에 걸쳤습니다.
      anchor = spot.mid
      this.heldNode = spot.tile
      // 카드 딱지와 같은 규칙입니다 — 봉지 바로 밑.
      baseline = spot.holdY
      height = HELD_H
      buttons.push(new Button(t('ui.button.buy'), 92, HELD_H, 'primary', () => {
        this.held = undefined
        this.game.pack.openPackSlot(held.uid)
      }))
    } else if (held.kind === 'voucher') {
      const one = this.game.shop.voucherTile
      if (!one || one.tile.destroyed) {
        this.held = undefined
        return
      }
      // 카드 딱지와 같은 규칙입니다 — 얼굴 바로 밑.
      anchor = one.mid
      this.heldNode = one.tile
      baseline = one.holdY
      height = HELD_H
      buttons.push(new Button(t('ui.button.buy'), 92, HELD_H, 'primary', () => {
        this.held = undefined
        this.game.shop.buyVoucher()
      }))
    } else if (held.kind === 'pack') {
      const open = this.game.state.pack
      const view = this.game.pack.packViews.get(held.uid)
      if (!open || !view) {
        this.held = undefined
        return
      }
      // **부챗살 아래의 한 줄입니다.** 고른 그 카드 바로 밑에 세웠더니 옆 카드에 걸쳤고,
      // 고른 카드는 올라오므로 그 단추도 함께 올라와 자리가 카드마다 달랐습니다 — 어느
      // 카드를 고르든 단추는 같은 줄에 놓입니다.
      //
      // **간격은 상점과 같은 4px 입니다.** 66px 였고, 그만큼 떨어지면 카드와 단추가 한
      // 덩이로 읽히지 않습니다 — 상점의 칸은 값이 있던 자리(카드 밑 4px)에 단추가 놓입니다.
      anchor = view.face.node.x
      this.heldNode = view.face.node
      baseline = PACK_CARDS_Y + PACK_CARD_H / 2 + 4
      height = HELD_H
      // 상점의 칸과 같은 규칙입니다 — 자리가 찼는지는 단추가 아니라 그 다음 화면이 적습니다.
      buttons.push(new Button(t('ui.button.take'), 92, HELD_H, 'primary', () => {
        this.held = undefined
        this.game.pack.takeFromPack(held.uid)
      }))
    } else if (held.kind === 'joker') {
      const index = this.game.state.jokers.findIndex(joker => joker.uid === held.uid)
      if (index < 0) {
        this.held = undefined
        return
      }
      anchor = this.jokerSpot(index).x
      this.heldNode = this.game.cards.jokers.get(held.uid)
      const price = sellValueOf(this.game.data, this.game.state, this.game.state.jokers[index])
      // **자리를 비우는 중이면 단추가 하나입니다.** 파는 것과 같은 값이 들어오지만 하는
      // 일은 「이것을 내놓고 그것을 받는다」이므로, 판다가 아니라 그 말로 적습니다.
      // **자리를 비우는 동안에는 단추가 없습니다.** 누르는 것이 곧 고르는 것이므로
      // (`pick`), 여기까지 오는 일이 없습니다.
      if (this.focus) {
        buttons.length = 0
      } else {
        buttons.push(new Button(tf('ui.button.sell', { n: price }), 92, HELD_H, 'danger', () => {
          this.held = undefined
          this.game.audio.play('joker_sell')
          this.sellFrom = this.jokerSpot(index)
          this.game.act({ t: 'sell_joker', index })
        }))
      }
    } else {
      const index = this.game.state.consumables.findIndex(item => item.uid === held.uid)
      if (index < 0) {
        this.held = undefined
        return
      }
      anchor = this.itemSpot(index).x
      this.heldNode = this.consumableTiles.find(one => one.uid === held.uid)?.tile
      if (this.focus) {
        buttons.length = 0
      // **「사용」은 손패를 앞에 두었을 때만 놓입니다.** 상점과 블라인드 고르기에서는 팔 수만
      // 있습니다 — 쓸 수 없는 때에 단추가 놓여 있으면 눌러서 카드를 버리게 됩니다.
      //
      // **나아가는 단추의 노랑입니다.** 판의 색(`UI.light`)이었고, 그 색은 겉면을 따라가므로
      // 무채색 겉면에서는 회색 단추 하나였습니다 — 하는 일은 「낸다」와 같은 갈래이고,
      // 그 옆의 「판매」가 붉음이므로 둘이 색으로 갈립니다.
      } else if (this.game.handReady) buttons.push(new Button(t('ui.button.use'), 68, HELD_H,
        'primary', () => {
        this.held = undefined
        // **쓴 것과 판 것은 없어지는 모습이 다릅니다.** 쓴 것은 판 가운데로 나와 번쩍이고,
        // 판 것은 제자리에서 탑니다 — 화면은 어느 쪽인지 모르므로 여기서 적어 둡니다.
        this.usedItem = held.uid
        this.game.act({ t: 'use_consumable', index, targets: this.game.cards.orderedSelection() })
      }))
      if (!this.focus) {
        buttons.push(new Button(tf('ui.button.sell', { n: this.game.data.economy.sellMin }), 92,
          HELD_H, 'danger', () => {
          this.held = undefined
          this.game.audio.play('joker_sell')
          this.sellFrom = this.itemSpot(index)
          this.game.act({ t: 'sell_consumable', index })
        }))
      }
    }

    const gap = 8
    // **얼굴의 넓이로 셉니다.** `width` 에는 그림자 여백이 들어 있어서, 그것으로 세면 줄이
    // 여백만큼 넓어지고 가운데가 왼쪽으로 밀립니다 — 상점의 「산다」가 물건의 왼쪽에
    // 비켜서 있던 것이 그것입니다.
    const span = buttons.reduce((sum, one) => sum + one.boxW, 0) + gap * (buttons.length - 1)
    // **화면 안으로 당깁니다.** 고른 것이 자기 줄의 끝에 놓여 있으면 그 아래에 가운데를
    // 맞춘 단추 줄이 화면 밖으로 나갑니다 — 소모품 줄은 화면 오른쪽에 붙어 있어서
    // 마지막 칸의 「쓴다 · 판다」가 30픽셀쯤 잘렸습니다.
    // **픽셀에 맞춥니다.** 줄의 간격은 칸 수로 나눈 값이라 소수입니다 — 소모품 줄의 단추가
    // `x` 1002.29 에 놓여 있었고, 그 반 픽셀이 조커 줄과 상점의 단추와 견주었을 때
    // 「자리가 미묘하게 다르다」로 보입니다.
    let x = Math.round(Math.max(HELD_EDGE,
      Math.min(SIZE.width - HELD_EDGE - span, anchor - span / 2)))
    // **첫 단추의 자리를 알립니다.** 이제 사는 것도 집는 것도 두 번 눌러야 하므로, 도구가
    // 두 번째 누를 자리를 알아야 합니다 — 계산을 도구가 베껴 적으면 배치를 고칠 때
    // 한쪽만 고쳐지고 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도 하지 않습니다.
    this.game.spots.held = { x: x + (buttons[0]?.boxW ?? 0) / 2, y: baseline + height / 2 }
    // **단추 줄이 화면 안에 있는지는 이 사각형으로만 확인됩니다.** 첫 단추의 가운데만
    // 알리면 줄이 얼마나 긴지 알 수 없고, 잘린 것은 줄의 오른쪽 끝입니다.
    this.heldBox = box(x, baseline, span, height)
    for (const button of buttons) {
      button.position.set(x, baseline)
      x += button.boxW + gap
      this.game.chrome.heldBar.addChild(button)
    }
  }

  /**
   * 소모품이 들리는 것.
   *
   * **조커와 같은 용수철입니다** — `Motion` 의 `y` 와 같은 강성과 감쇠이므로, 나란히 선
   * 조커와 소모품이 같은 빠르기로 올라갑니다.
   */
  /**
   * 사서 오는 소모품이 울렁이다가 번쩍입니다.
   *
   * **조커의 `advance` 가 하는 일과 같습니다.** 다만 소모품 칸은 화면을 다시 그릴 때마다
   * 새로 만들어지므로 그 몫을 화면이 대신 듭니다.
   */
  /**
   * 방금 들어온 소모품이 그 자리에서 옵니다.
   *
   * **조커는 뷰가 용수철을 들고 있어서 날아옵니다.** 소모품 칸은 화면을 다시 그릴 때마다
   * 새로 만들어지므로 그럴 것이 없어서 제 칸에 툭 나타났습니다 — 오는 동안의 어긋남을
   * 화면이 들고 있다가 매 프레임 얹습니다.
   *
   * 상점에서 사는 것과 팩에서 집는 것 둘이 부릅니다. **자리만 다르고 나머지는 같습니다.**
   */
  itemFlying(from: { x: number; y: number }): void {
    this.itemFlyOwned = true
    this.flyAsked++
    const last = newest(this.game.state.consumables)
    if (!last) {
      this.flyMissed++
      return
    }
    // **받는 것도 끄는 것도 카드의 가운데입니다.** 조커 뷰와 같은 자리를 받아야 부르는 쪽이
    // 둘을 달리 셀 필요가 없고, 칸의 왼쪽 위로 옮기는 것은 `placeArriving` 이 합니다.
    const motion = new Motion()
    motion.snap(from.x, from.y)
    // 오는 길이 보이도록 느리게 갑니다. 조커의 `buying` 과 같은 값입니다.
    motion.drift()
    this.itemArrive = { uid: last.uid, warp: 1, glow: 0, filter: new ArriveFilter(), from,
      motion }
    // 새로 만든 칸이 첫 프레임부터 산 자리에 서는 것은 `syncConsumables` 가 합니다.
    this.syncConsumables()
  }

  /**
   * 오는 중인 소모품을 지금 자리에 둡니다. 산 자리에서 제 칸으로 미끄러지는 중입니다.
   *
   * **매 프레임 얹는 것이라 `tile.y` 는 이미 들린 높이가 들어 있어야 합니다.** 부르는 쪽이
   * 제자리(또는 들린 자리)를 먼저 두고 그 위에 이것을 얹습니다.
   */
  private placeArriving(one: { uid: number; tile: Container; baseX: number; baseY: number }): void {
    const coming = this.itemArrive
    if (!coming || coming.uid !== one.uid) return
    // 목표는 이 칸의 가운데입니다. 칸의 자리가 바뀌면 용수철의 목표도 따라갑니다.
    const toX = one.baseX + SIZE.jokerWidth / 2
    const toY = one.baseY + SIZE.jokerHeight / 2
    coming.motion.x.target = toX
    coming.motion.y.target = toY
    one.tile.x = one.baseX + (coming.motion.x.value - toX)
    one.tile.y += coming.motion.y.value - toY
  }

  /** 오는 중인 소모품의 지금 자리와 어디에서 오는 중인지. 없으면 `null`. */
  flyPeek(): {
    x: number; y: number; fromX: number; fromY: number; travel: number
  } | null {
    const coming = this.itemArrive
    if (!coming) return null
    const one = this.consumableTiles.find(tile => tile.uid === coming.uid)
    if (!one) return null
    // 온 몫은 남은 거리로 셉니다. 용수철이라 시간으로는 알 수 없습니다.
    const whole = Math.hypot(coming.motion.x.target - coming.from.x,
                             coming.motion.y.target - coming.from.y)
    const left = Math.hypot(coming.motion.x.target - coming.motion.x.value,
                            coming.motion.y.target - coming.motion.y.value)
    const travel = whole < 1 ? 1 : Math.max(0, Math.min(1, 1 - left / whole))
    return {
      x: Math.round(one.tile.x), y: Math.round(one.tile.y),
      fromX: Math.round(coming.from.x), fromY: Math.round(coming.from.y),
      travel: Math.round(travel * 100) / 100,
    }
  }

  advanceItemArrive(seconds: number): void {
    const one = this.itemArrive
    if (!one) return

    one.warp = Math.max(0, one.warp - seconds * 1.6)
    one.glow = Math.max(0, one.glow - seconds * 2.2)
    one.motion.advance(seconds)
    one.filter.at(this.game.clock)
    one.filter.warp = one.warp
    one.filter.flash = one.glow * one.glow

    if (one.warp > 0 || one.glow > 0 || !one.motion.x.settled || !one.motion.y.settled) return
    // 다 썼으면 놓습니다. **판이 도는 내내 물결을 굽고 있을 이유가 없습니다.**
    this.itemArrive = undefined
    for (const tile of this.consumableTiles) {
      const first = tile.tile.children[0]
      if (first instanceof Container) faceOf(first).filters = []
    }
  }

  advanceConsumableLift(seconds: number): void {
    for (const one of this.consumableTiles) {
      let spring = this.consumableLift.get(one.uid)
      if (!spring) {
        spring = new Spring()
        this.consumableLift.set(one.uid, spring)
      }
      // **고른 것이 먼저입니다.** 고른 칸은 이미 단추가 설 만큼 올라가 있으므로 가리키는
      // 10픽셀을 거기에 더 얹으면 윗변이 화면 밖으로 나갑니다 — 조커 줄과 같은 규칙입니다.
      const picked = this.held?.kind === 'consumable' && this.held.uid === one.uid
      const over = !picked && this.hoveredItem === one.uid
      spring.target = picked ? HELD_RISE : over ? TIP_RISE : 0
      spring.advance(seconds)

      // 가리키면 커집니다. 얼굴에 얹는 것은 아래의 기울기와 한 자리입니다.
      let grow = this.consumableGrow.get(one.uid)
      if (!grow) {
        grow = new Spring(1)
        this.consumableGrow.set(one.uid, grow)
      }
      grow.target = over ? TIP_GROW : 1
      grow.advance(seconds)
      // 지난 자리에서 제자리로. 자리를 묻는 쪽(`itemSpot`)에는 제자리를 답합니다.
      const slide = this.consumableSlide.get(one.uid)
      if (slide) {
        slide.target = one.baseX
        slide.advance(seconds)
        one.tile.x = Math.abs(slide.value - one.baseX) < 0.3 ? one.baseX : slide.value
      }

      // **조커와 같이 늘 조금씩 흔들립니다.** 나란히 선 줄에서 한쪽만 멈춰 있으면 그것이
      // 그림이 아니라 화면에 붙은 딱지로 보입니다 — 값과 빠르기가 `JokerView.advance` 와
      // 같고, 위상만 카드마다 다릅니다(그래서 줄이 한 몸으로 출렁이지 않습니다).
      //
      // **위상은 번호에서 옵니다.** 칸은 다시 그릴 때마다 새로 만들어지므로, 만들 때 뽑은
      // 무작위 값은 다시 그릴 때마다 흔들림을 처음으로 되돌립니다.
      const phase = one.uid * 1.7
      const face = one.tile.children[0]
      if (face) {
        face.rotation = itemTilt(one.uid, this.game.clock)
        // **얼굴만 키웁니다.** 칸의 자리는 줄 밖의 여럿이 읽으므로(`spotOf` · `itemSpot` ·
        // 태우기) 칸을 키우면 그 값들의 뜻이 함께 바뀝니다.
        face.scale.set(grow.value)
      }
      one.tile.y = one.baseY - spring.value + sway(this.game.clock, phase * 1.3, 1.8, 0.7)

      // **자리를 비우는 동안.** 내놓을 수 있는 것은 조금 떠서 살짝 오르내리고, 그 갈래가
      // 아니면 물러납니다 — 어느 줄에서 고르라는 것인지가 글보다 먼저 보여야 합니다.
      if (this.focus) {
        const ok = this.focus.kind === 'consumable'
        one.tile.alpha = ok ? 1 : 0.3
        // **고른 것에는 얹지 않습니다.** 조커 줄과 같은 이유입니다 — 용수철이 이미
        // `HELD_RISE` 만큼 들어 올렸고 그 밑에 단추가 놓였습니다.
        const picked = this.held?.kind === 'consumable' && this.held.uid === one.uid
        if (ok && !picked) one.tile.y -= 6 + Math.sin(this.game.clock * 3 + one.uid) * 3
      } else one.tile.alpha = 1

      // 사서 오는 중인 한 장은 산 자리에서 제 칸으로 미끄러집니다.
      this.placeArriving(one)
    }
  }

  showTooltip(view: JokerView): void {
    const rarityName = ['', t('ui.rarity.common'), t('ui.rarity.uncommon'), t('ui.rarity.rare'),
      t('ui.rarity.legendary')][view.look.rarity] ?? ''
    this.game.input.tooltip.show(view.look.name, rarityName, view.look.rarity, view.look.lines,
      this.game.input.tipBox(view), SIZE)
  }

  syncConsumables(): void {
    const alive = new Set(this.game.state.consumables.map(item => item.uid))
    for (const uid of [...this.consumableSlide.keys()]) {
      if (!alive.has(uid)) this.consumableSlide.delete(uid)
    }

    // **없어진 것은 곧바로 지우지 않습니다.** 판 밖으로 옮겨 태우고, 다 타면 그때 지웁니다.
    for (const one of this.consumableTiles) {
      if (alive.has(one.uid)) continue
      const used = this.usedItem === one.uid
      if (used) this.usedItem = undefined
      this.igniteItem(one.tile, used)
    }

    this.consumableLayer.removeChildren().forEach(child => child.destroy())
    // **겹치는 차례를 이 층이 정합니다.** 붙인 순서로만 두면 가리킨 칸을 올릴 수 없습니다.
    this.consumableLayer.sortableChildren = true
    this.consumableTiles.length = 0
    // 없어진 것의 높이는 버립니다.
    for (const uid of [...this.consumableLift.keys()]) {
      if (!alive.has(uid)) this.consumableLift.delete(uid)
    }

    // 조커와 같습니다 — 자리 안에서 가운데로 모이고, 넘칠 만큼 많으면 좁게 놓입니다.
    const spots = trayRow(CONSUMABLE_TRAY, this.game.state.consumables.length)
    this.publishRowSpots('item', spots, this.game.state.consumables.length)

    // 방금 들어온 것. **줄의 끝이 아닙니다** — 자리를 갈아 끼우는 것은 판 그 자리에
    // 들어옵니다. 소모품 칸은 다시 그릴 때마다 새로 만들어져 조커처럼 「뷰가 없는 것」으로
    // 가릴 수 없으므로, 가장 나중에 받은 번호로 봅니다.
    const arriving = newest(this.game.state.consumables)?.uid
    this.game.state.consumables.forEach((item, index) => {
      // 산 딱지가 아직 그 자리에 있는 동안은 세우지 않습니다.
      if (this.arriveHold?.kind === 'item' && this.game.clock < this.arriveHold.until
          && item.uid === arriving) return
      const name = this.consumableName(item.kind, item.id)
      const lines = this.consumableLines(item.kind, item.id)

      // **조커와 같은 카드입니다.** 나란히 선 줄에서 하나만 다른 모양이면 갈래가 다른
      // 물건으로 보이고, 실제로는 둘 다 손에 든 카드입니다.
      const tile = new Container()
      tile.position.set(
        spots.startX + index * spots.spacing - SIZE.jokerWidth / 2,
        JOKER_Y - SIZE.jokerHeight / 2)

      const face = itemFace(this.game.data, {
        kind: (item.kind === 1 ? ShopItemKind.Tarot
          : item.kind === 2 ? ShopItemKind.Planet : ShopItemKind.Spectral) as ShopItemKind,
        id: item.id,
        cost: 0,
        edition: item.edition as never,
      } as ShopItem)
      // **가운데를 축으로 돕니다.** 조커와 같이 살짝 기울며 오가야 하고(`advanceConsumableLift`),
      // 왼쪽 위를 축으로 돌면 그 기울기가 카드를 옆으로 밀어 버립니다.
      //
      // **칸이 아니라 얼굴을 돌립니다.** 칸의 자리는 이 줄 밖의 여럿이 읽고 쓰므로
      // (`spotOf` · `placeArriving` · 태우기) 축을 옮기면 그 값들의 뜻이 함께 바뀝니다.
      face.pivot.set(SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
      face.position.set(SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
      // **만들 때 이미 기울여 둡니다.** 이 줄은 득점이 도는 동안 `refresh` 마다 새로
      // 만들어지는데, 새 얼굴의 기울기가 0 이면 다음 틱까지 한 프레임 동안 줄이 반듯하게
      // 펴집니다 — 판이 끝날 때마다 소모품이 한 번씩 움찔하던 것이 그것입니다.
      face.rotation = itemTilt(item.uid, this.game.clock)

      tile.addChild(face)
      tile.hitArea = new Rectangle(0, 0, SIZE.jokerWidth, SIZE.jokerHeight)
      tile.eventMode = 'static'
      tile.cursor = 'pointer'
      // **누르면 고르는 것입니다.** 쓰는 것과 파는 것은 그 밑에 선 버튼이 합니다 —
      // 소모품 하나가 판을 바꾸므로, 실수로 눌러 써 버리면 되돌릴 수 없습니다.
      tile.on('pointertap', () => {
        if (this.game.input.ate()) return
        this.pick('consumable', item.uid)
      })
      // **겹치는 차례도 커서를 따라갑니다.** 조커 줄과 같은 원리입니다 — 자리가 좁아지면
      // 칸이 겹치므로, 가리킨 것은 가리키는 동안 올라오고 떼면 줄의 차례로 돌아갑니다.
      //
      // **칸은 다시 그릴 때마다 새로 만들어집니다.** 그래서 조커처럼 매 프레임 셈하지 않고
      // 그 칸이 스스로 듣습니다 — 되돌릴 값은 아래에서 적어 둡니다.
      tile.on('pointerover', event => {
        if (tile.destroyed) return
        // **손가락에는 「올려 둔다」가 없습니다.** 손가락으로 스친 칸이 올라간 채로 남습니다.
        if (event.pointerType === 'mouse') this.hoveredItem = item.uid
        tile.zIndex = HOVER_Z
        this.consumableLayer.sortChildren()
      })
      tile.on('pointerout', () => {
        if (tile.destroyed) return
        if (this.hoveredItem === item.uid) this.hoveredItem = undefined
        const one = this.consumableTiles.find(row => row.uid === item.uid)
        tile.zIndex = this.held?.kind === 'consumable' && this.held.uid === item.uid
          ? PICK_Z : one?.rowZ ?? ROW_Z
        this.consumableLayer.sortChildren()
      })
      // **줄에 놓이는 것은 기울기도 따라갑니다.** 옆에 놓인 조커가 커서를 따라 기우는데
      // 소모품만 굳어 있으면 한 줄에 두 가지 규칙이 생깁니다. 칸은 다시 그릴 때마다 새로
      // 만들어지므로 자리는 값 하나로 잡아 둡니다.
      const anchorX = tile.x + SIZE.jokerWidth / 2
      // **고른 것은 올라온 채로 남습니다.** 되돌릴 값도 함께 적어 둡니다.
      const rowZ = ROW_Z + index
      tile.zIndex = this.held?.kind === 'consumable' && this.held.uid === item.uid
        ? PICK_Z : rowZ
      const entry = { uid: item.uid, tile, baseX: tile.x, baseY: tile.y, rowZ,
                      look: this.game.input.lookOf(face, () => this.game.input.tiltAt(anchorX)) }
      // **지난 자리에서 미끄러져 옵니다.** 처음 선 것은 제자리입니다. 첫 프레임부터 지난
      // 자리에 둡니다 — 다음 틱에 옮기면 새 자리에 한 프레임 보이고 나서 뛰었다 돌아옵니다.
      let slide = this.consumableSlide.get(item.uid)
      if (!slide) {
        slide = new Spring(entry.baseX)
        this.consumableSlide.set(item.uid, slide)
      }
      slide.target = entry.baseX
      tile.x = slide.value
      this.consumableTiles.push(entry)
      // **오는 중인 것은 첫 프레임부터 오는 자리에 둡니다.** 칸은 다시 그릴 때마다 새로
      // 만들어지고 오는 길을 얹는 것은 다음 틱이라, 그 사이 한 프레임 동안 제 칸에 보입니다 —
      // 산 딱지가 다 사라지는 자리에서 다시 그리는 것이 그 한 번이었습니다.
      this.placeArriving(entry)
      this.game.input.tipOn(tile, at => {
        this.game.input.tooltip.show(name, t('ui.kind.consumable'), 0, lines, at, SIZE)
      })
      // 사서 오는 중인 한 장에만 겁니다. **얼굴에만입니다** — 그림자까지 걸면 카드 옆에
      // 빛나는 얼룩 하나가 따로 남습니다.
      if (this.itemArrive?.uid === item.uid) {
        const first = tile.children[0]
        if (first instanceof Container) faceOf(first).filters = [this.itemArrive.filter]
      }
      this.consumableLayer.addChild(tile)
    })
  }

  /** 한 장을 삭게 합니다. 자기 자리에서 그대로 삭아야 하므로 자리를 옮겨 담습니다. */
  private igniteItem(tile: Container, used: boolean): void {
    const spot = this.game.probe.spotOf(tile)
    tile.removeFromParent()
    tile.position.set(spot.x, spot.y)
    tile.eventMode = 'none'

    // 얼굴만 골라 냅니다. 셰이더가 걸리는 자리입니다.
    const first = tile.children[0]
    const face = first instanceof Container ? faceOf(first) : tile

    const arrive = new ArriveFilter()
    const erode = new ErodeFilter()
    // **격자를 판의 크기로 세웁니다.** 넘기지 않으면 판 하나가 한 칸입니다.
    erode.fit(SIZE.jokerWidth, SIZE.jokerHeight)
    // **얼굴 하나에 겁니다.** 그림자까지 걸면 그 아래에 얼룩 하나가 따로 남고, 이 얼굴이
    // 판의 크기로 고정되어 있으므로 소멸선의 격자가 그 사각형과 딱 맞습니다.
    //
    // **번쩍임은 쓴 것에만 겁니다.** 판 것은 첫 프레임부터 왜곡도 번쩍임도 0 이라, 걸어 두면
    // 아무 그림 없는 렌더 텍스처 하나를 삭는 동안 들고 있습니다. 삭는 것이 위입니다 —
    // 울렁이는 판 위에서 표면이 풀려야 합니다.
    face.filters = used ? [arrive, erode] : [erode]
    // **판 위의 버튼들보다 위, 떠오르는 글 아래입니다.** 판 가운데로 나오는 길에 버튼들을
    // 지나가므로 0보다 높아야 하고, 파는 값이 이 딱지 위에 얹혀야 하므로 그 글(`popAt` 의
    // 2)보다는 낮아야 합니다 — 남긴 딱지(`lingerNode`)와 같은 규칙이고 같은 값입니다.
    //
    // **400 이었습니다.** 그 값에는 버튼을 지난다는 것 말고 다른 뜻이 없었고, 바꿔 집을 때
    // 파는 값이 그 카드 뒤로 들어갔습니다.
    tile.zIndex = 1
    this.game.overlay.addChild(tile)
    this.game.cards.burningItems.push({
      tile, face, arrive, erode,
      from: { x: spot.x, y: spot.y },
      // **판 가운데로 갑니다.** 카드가 놓이는 자리이므로, 쓴 것이 무엇에 걸리는지가 그
      // 자리에서 보입니다 — 오른쪽 칸에서 그대로 타 없어지면 화면 구석의 일이 됩니다.
      // **판 것은 제자리에서 탑니다.** 나와서 번쩍이는 것은 「썼다」의 몸짓이고, 파는 것은
      // 그 자리에서 없애는 것입니다.
      to: used
        ? { x: BOARD_X - SIZE.jokerWidth / 2, y: PLAY_Y - SIZE.jokerHeight / 2 - 24 }
        : { x: spot.x, y: spot.y },
      life: used ? 0 : ITEM_HOLD,
      age: 0, flashed: !used, grows: used,
    })

    if (used) this.game.audio.play('consumable_use')
  }

  /**
   * 쓴 것이 없어지는 네 마디.
   *
   * **울렁 → 이동 → 번쩍 → 모래로 풀림**입니다. 제자리에서 그냥 삭으면 무엇을 쓴 것인지가
   * 오른쪽 구석의 일로 남고, 그냥 없어지면 정말 쓰인 것인지 눈이 따라가지 못합니다 —
   * 사는 것이 「울렁 · 이동 · 안착」인 것과 짝이고, 다만 마지막이 안착이 아니라 사라짐입니다.
   */
  advanceBurningItems(seconds: number): void {
    for (let i = this.game.cards.burningItems.length - 1; i >= 0; i--) {
      const one = this.game.cards.burningItems[i]
      one.life += seconds

      // 첫 마디. 제자리에서 울렁입니다.
      const warp = Math.max(0, 1 - one.life / ITEM_WARP)
      one.arrive.at(this.game.clock)
      one.arrive.warp = warp

      // 둘째 마디. 판 가운데로 갑니다.
      const travel = Math.max(0, Math.min(1,
        (one.life - ITEM_WARP) / (ITEM_ARRIVE - ITEM_WARP)))
      const eased = 1 - Math.pow(1 - travel, 3)
      one.tile.position.set(
        one.from.x + (one.to.x - one.from.x) * eased,
        one.from.y + (one.to.y - one.from.y) * eased)
      // 가는 동안 조금 커집니다. **쓰는 것은 그 판에서 가장 큰 한 수입니다.** 판 것은
      // 나오지 않으므로 커지지도 않습니다.
      if (one.grows) one.tile.scale.set(1 + 0.22 * eased)

      // 셋째 마디. 닿는 그 한 번만 번쩍입니다.
      if (travel >= 1 && !one.flashed) {
        one.flashed = true
        this.game.audio.play('card_slam')
        this.game.show.jolt(7, 1.4, 0.3)
        this.game.show.flashPanel(UI.legendary, 0.6)
      }
      const since = one.life - ITEM_ARRIVE
      const glow = one.flashed ? Math.max(0, 1 - since / ITEM_FLASH) : 0
      one.arrive.flash = glow * glow

      // 넷째 마디. **닿은 자리에서 한 번 떱니다.** 잦아드는 흔들림이고, 파는 것은 나오지
      // 않으므로 떨지도 않습니다.
      if (one.grows && since >= 0 && since < ITEM_SHAKE) {
        const left = 1 - since / ITEM_SHAKE
        const tilt = Math.sin(since * 46) * ITEM_SHAKE_TILT * left * left
        one.tile.rotation = tilt * (Math.PI / 180)
      } else if (one.grows && one.flashed && one.age <= 0) {
        one.tile.rotation = 0
      }

      // 다섯째 마디. 잠시 머물렀다가 모래로 풀립니다.
      if (one.life < ITEM_HOLD) continue
      if (one.age <= 0) {
        this.game.audio.play('joker_burn')
        // **삭기 시작하는 그 프레임에 한 번 굽습니다.** 울렁임이 끝난 그 판이 구워져야
        // 알갱이의 색이 눈에 보이던 것과 같습니다.
        one.motes = startMotes(one.face, SIZE.jokerWidth, SIZE.jokerHeight)
      }
      one.age += seconds
      one.erode.erode = one.age / ERODE_SWEEP
      // **제자리에서 조금 내려앉습니다.** 떠오르는 것은 타서 가벼워진 종이입니다.
      one.tile.y += seconds * 7
      one.motes?.place(one.face)
      if (one.age < ERODE_SWEEP) continue
      this.game.cards.burningItems.splice(i, 1)
      one.tile.destroy({ children: true })
    }
  }

  consumableName(kind: number, id: string): string {
    const group = kind === 1 ? 'tarot' : kind === 2 ? 'planet' : 'spectral'
    return nameOf(this.game.data, group, id, id)
  }

  consumableLines(kind: number, id: string): string[] {
    if (kind === 1) return describe(this.game.data, this.game.data.tarotEffects.get(id) ?? [])
    if (kind === 3) return describe(this.game.data, this.game.data.spectralEffects.get(id) ?? [])
    const planet = this.game.data.tables.planet.findByPlanetId(id)
    return planet ? [tf('ui.hand.level_up', { name: this.game.panels.handName(planet.hand) })]
      : []
  }

  roomFor(kind: ShopItemKind): boolean {
    const state = this.game.state
    if (kind === ShopItemKind.Joker) return state.jokers.length < state.rules.jokerSlots
    if (kind === ShopItemKind.Tarot || kind === ShopItemKind.Planet
      || kind === ShopItemKind.Spectral) {
      return state.consumables.length < state.rules.consumableSlots
    }
    return true
  }

  /**
   * 산 딱지를 그 자리에 남깁니다.
   *
   * `act` 가 상점을 다시 그리며 딱지를 통째로 없애므로, 그 프레임에 물건은 이미 없고
   * 빈자리에서 값이 뜨고 동전이 나갔습니다 — 무엇에 얼마를 낸 것인지가 한 화면에 없었습니다.
   * 딱지를 상점 층에서 떼어 같은 자리에 두고, 때가 되면 사라집니다.
   */
  lingerTile(slot: number, hold = BUY_LINGER): void {
    const one = this.game.shop.shopTiles.get(slot)
    if (!one || one.tile.destroyed) return
    this.game.shop.shopTiles.delete(slot)
    this.lingerNode(one.tile, hold)
  }

  /**
   * 딱지 하나를 상점 층에서 떼어 같은 자리에 남깁니다. 카드 · 팩 · 바우처가 같은 길입니다.
   *
   * **값을 치르는 동안 그 물건이 거기 있어야 합니다.** 값이 그 위에 뜨고 동전이 거기서
   * 나가는데, 상점을 다시 그리는 것은 딱지를 통째로 없애는 것입니다.
   */
  lingerNode(tile: Container, hold: number): void {
    const at = this.game.overlay.toLocal(tile.getGlobalPosition())
    tile.removeFromParent()
    tile.position.copyFrom(at)
    tile.eventMode = 'none'
    // **상점 판 위, 떠오르는 글 아래입니다.** 상점 층이 `-1` 이므로 0 이상이면 판을 덮고,
    // 값이 뜨는 글보다 높으면 그 글이 이 딱지 뒤로 들어갑니다 — 딱지를 남기는 것은 값을
    // 그 물건 위에 얹기 위해서이므로 그 둘의 차례가 뒤집히면 남긴 뜻이 없어집니다.
    tile.zIndex = 1
    this.game.overlay.addChild(tile)
    this.game.cards.leavingTiles.push({ node: tile, at: this.game.clock + hold })
  }

  /** 남아 있던 딱지들. 때가 되면 사라집니다. */
  advanceLeavingTiles(seconds: number): void {
    if (this.game.cards.leavingTiles.length === 0) return
    // **상점을 떠나면 그 자리에서 걷습니다.** 판이 없어지는데 그 위에 딱지 하나가 남습니다.
    const gone = this.game.state.phase !== 'shop'
    for (let i = this.game.cards.leavingTiles.length - 1; i >= 0; i--) {
      const one = this.game.cards.leavingTiles[i]
      if (one.node.destroyed) {
        this.game.cards.leavingTiles.splice(i, 1)
        continue
      }
      if (!gone) {
        if (this.game.clock < one.at) continue
        one.node.alpha -= seconds / 0.16
        if (one.node.alpha > 0) continue
      }
      one.node.destroy()
      this.game.cards.leavingTiles.splice(i, 1)
    }
    // 다 사라졌습니다. 이제 남은 것들이 당겨져 빈자리를 메웁니다.
    if (this.game.cards.leavingTiles.length === 0) this.game.refresh()
  }

  /**
   * 산 물건이 `wait` 뒤에 `from` 에서 제 줄로 날아옵니다. 조커와 소모품이 같은 길입니다.
   *
   * **그동안은 줄에 세우지 않습니다.** 조커 줄과 소모품 칸은 `refresh` 마다 상태를 그대로
   * 그리므로, 붙들지 않으면 산 물건이 딱지가 아직 놓여 있는 동안 이미 줄에 놓여 있습니다 —
   * 같은 물건이 둘입니다. 상점에서는 딱지가 남는 동안(`BUY_LINGER`)이고, 팩에서는 집은
   * 그 프레임입니다.
   *
   * **출발 자리는 여기서 넘깁니다.** 조커는 뷰가 만들어질 때 `arriveFrom` 을 받아
   * 용수철로 오고, 소모품은 칸이 다시 그릴 때마다 새로 만들어지므로 화면이 그 용수철을
   * 들고 있습니다(`itemFlying`) — 둘이 같은 강성(`Motion.drift`)이고 같은 시각(`LAND_AT`)에
   * 닿습니다. 부르는 쪽이 갈래마다 달리 적던 동안 한 길에서 조커의 출발 자리가 빠져, 산
   * 조커가 줄 위에서 떨어졌습니다.
   */
  holdArrival(item: ShopItem, from: { x: number; y: number },
                      wait = BUY_LINGER): void {
    // **줄에 놓이지 않는 것은 붙들지 않습니다.** 상점의 플레잉 카드는 덱으로 들어가므로 조커
    // 줄에도 소모품 칸에도 자리가 없고, 그것을 소모품으로 세면 엉뚱한 한 칸이 비어 있습니다.
    const kind = item.kind === ShopItemKind.Joker ? 'joker' as const
      : isConsumable(item.kind) ? 'item' as const : undefined
    if (!kind) return
    // **부르는 쪽이 이 몫을 듭니다.** 박자가 겹쳐 들면 같은 물건이 두 번 날아옵니다.
    if (kind === 'item') this.itemFlyOwned = true
    // **천장은 닿는 시각입니다.** 아래의 예약이 그 전에 놓습니다 — `wait` 가 0 이어도 액션이
    // 지나며 한 번 그리는 그 프레임은 붙들려 있어야 하고, 예약은 다음 틱에 돕니다.
    this.arriveHold = { kind, until: this.game.clock + wait + LAND_AT }
    this.game.later.push({
      at: this.game.clock + wait,
      run: () => {
        this.arriveHold = undefined
        if (kind === 'item') {
          this.itemFlying(from)
          return
        }
        // 뷰가 만들어지는 그 `refresh` 가 이 자리를 받습니다.
        this.arriveFrom = from
        this.game.refresh()
      },
    })
  }

  /**
   * 산 것이 제자리에 닿았습니다.
   *
   * **닿은 자리에 이름이 뜹니다.** 어디로 들어간 것인지가 그 한 번으로 남습니다 —
   * 조커는 조커 줄로, 소모품은 소모품 칸으로 들어갑니다.
   */
  landed(item: ShopItem): void {
    const joker = item.kind === ShopItemKind.Joker
    // **줄에도 칸에도 놓이지 않는 것은 여기로 오지 않습니다.** 플레잉 카드는 덱으로
    // 들어가고 덱이 받았다는 글을 띄웁니다 — 아래는 「조커가 아니면 소모품」으로 세므로, 그대로
    // 두면 방금 산 카드의 이름이 아무 상관 없는 소모품 칸 위에 뜹니다.
    if (!joker && !isConsumable(item.kind)) return
    let spot: { x: number; y: number } | undefined

    if (joker) {
      // **방금 들어온 것입니다. 줄의 끝이 아닙니다** — 자리를 갈아 끼우면 판 그 자리에
      // 들어오고, 끝으로 보면 이름이 엉뚱한 카드 위에 뜹니다.
      const last = newest(this.game.state.jokers)
      const view = last ? this.game.cards.jokers.get(last.uid) : undefined
      if (last && view) {
        // **발동이 아니라 도착입니다.** 흔들리면 아무 이유 없이 난리치는 것으로 보입니다.
        view.bounce(1.2)
        view.landing()
        // **뷰가 지금 있는 자리가 아니라 그 카드가 설 자리입니다.**
        //
        // 조커는 용수철로 날아오므로 `LAND_AT` 이 지난 뒤에도 아직 오는 중이고, 뷰의
        // 자리를 그대로 읽으면 이름이 그 카드가 지나가던 중간에 뜹니다 — 산 것의 이름이
        // 판 한가운데에 한 번 흘리고 가던 것이 그것입니다.
        spot = this.jokerSpot(this.game.state.jokers.indexOf(last))
      }
    } else {
      const uid = newest(this.game.state.consumables)?.uid
      const last = this.consumableTiles.find(one => one.uid === uid)
      if (last) spot = this.game.probe.spotOf(last.tile, SIZE.jokerWidth / 2,
        SIZE.jokerHeight / 2)
      if (this.itemArrive) {
        this.itemArrive.glow = 1
        this.itemArrive.warp = 0
        // **조커와 같이 한 번 튑니다**(`JokerView.bounce`). 원래 용수철로 돌아가므로 그 튐이
        // 지나쳤다 돌아옵니다. 기울기와 크기는 얹지 않습니다 — 칸의 축이 왼쪽 위라 그 둘은
        // 카드를 옆으로 밀어 냅니다.
        this.itemArrive.motion.soft()
        this.itemArrive.motion.y.kick(-300 * 1.2)
      }
    }
    if (!spot) return

    this.game.audio.play('joker_add')
    // **여기서도 조각이 없습니다.** 자리에 닿은 것은 카드 전체가 한 번 번쩍이는 것으로
    // 알립니다 — 그것이 그 카드에 관한 일이라는 것이 조각보다 분명합니다.
    this.game.show.popAt({ x: spot.x, y: spot.y - RISER_ON_CARD },
      shopLabel(item.kind, item.id, this.game.data), UI.money, 0.5)
    // **칸 수도 알립니다.** 자리가 몇 남았는지는 물건이 닿는 순간에 눈이 가지 않습니다.
    this.game.chrome.pulseCount(joker ? this.game.chrome.jokerCount
        : this.game.chrome.consumableCount)
  }

  /** 줄에 선 카드 한 장이 차지하는 사각형. 가운데의 `x` 하나로 정해집니다. */
  cardRect(x: number): Box {
    return box(x - SIZE.jokerWidth / 2, JOKER_Y - SIZE.jokerHeight / 2,
      SIZE.jokerWidth, SIZE.jokerHeight)
  }

  /**
   * 줄에 선 것들을 누를 자리를 알립니다.
   *
   * **도구가 셈하지 못합니다.** 카드가 자리 안에서 가운데로 모이므로 자리는 개수마다
   * 달라지고, 좌표를 적어 둔 도구는 아무것도 없는 곳을 눌러 놓고 그다음 줄로 넘어갑니다.
   */
  publishRowSpots(prefix: string,
                          row: { startX: number; spacing: number },
                          count: number): void {
    for (const key of Object.keys(this.game.spots)) {
      if (key.startsWith(`${prefix}:`)) delete this.game.spots[key]
    }
    for (let i = 0; i < count; i++) {
      this.game.spots[`${prefix}:${i}`] = { x: row.startX + i * row.spacing, y: JOKER_Y }
    }
  }

  /**
   * 조커와 소모품이 지금 서는 자리의 가운데.
   *
   * **한 자리에서 셉니다.** 파는 자리에서 동전이 솟아야 하고 그 아래에 단추가 서야 하는데,
   * 부르는 쪽마다 다시 세면 줄의 자리를 고친 날에 한쪽만 고쳐집니다.
   *
   * **지금 든 개수로 셉니다.** 자리 안에서 가운데로 모이므로 하나를 사면 앞의 것도
   * 함께 옮겨 놓입니다 — 칸 번호만으로는 자리가 정해지지 않습니다.
   */
  jokerSpot(index: number): { x: number; y: number } {
    const row = trayRow(JOKER_TRAY, this.game.state.jokers.length)
    return { x: row.startX + index * row.spacing, y: JOKER_Y }
  }

  itemSpot(index: number): { x: number; y: number } {
    const row = trayRow(CONSUMABLE_TRAY, this.game.state.consumables.length)
    return { x: row.startX + index * row.spacing, y: JOKER_Y }
  }

  /**
   * 자리가 없습니다 — 무엇과 바꿀까요.
   *
   * **묻고 나서 팝니다.** 말없이 하나를 팔아 치우면 되돌릴 수 없는 일을 묻지 않고 한
   * 것이고, 그냥 눌리지 않게 두면 왜 안 되는지 알 수 없습니다.
   *
   * 파는 값이 줄마다 적혀 있습니다 — 그것이 무엇을 내놓을지를 정하는 값입니다.
   */
  canSwap(item: ShopItem): boolean {
    return item.kind === ShopItemKind.Joker
      // `Eternal` 은 팔리지 않습니다. 그것만 들고 있으면 내놓을 것이 없습니다.
      ? this.game.state.jokers.some(held => held.sticker !== 1)
      : this.game.state.consumables.length > 0
  }

  /**
   * 자리를 비우는 화면으로 들어갑니다.
   *
   * **묻는 판이 아니라 줄 자체가 고르는 자리입니다.** 조커와 소모품은 이미 위 줄에 서
   * 있으므로, 이름을 적은 목록을 따로 띄우면 같은 것을 두 번 그리는 것이고 어느 것이 어느
   * 것인지 다시 맞춰 보게 됩니다 — 내놓을 수 있는 것은 살짝 떠 있고 다른 것은 옅어지며,
   * 무엇을 하라는 글이 그 줄 바로 아래에 놓입니다. 상점은 그대로 떠 있고, 사려던 딱지가
   * 들려 있습니다 — 새 물건은 거기서 떠납니다.
   *
   * 고르는 것은 줄의 그 물건을 누르는 것이고, 내놓는 것은 그 밑에 서는 단추입니다 —
   * 되돌릴 수 없는 일이 손이 미끄러진 한 번으로 일어나지 않습니다.
   */
  enterFocus(item: ShopItem, commit: (held: number) => void, slot?: number): void {
    const kind = item.kind === ShopItemKind.Joker ? 'joker' as const : 'consumable' as const
    this.held = undefined
    this.game.input.tooltip.hide()
    this.focusLayer.removeChildren().forEach(child => child.destroy())

    // **줄 바로 아래입니다.** 화면 가운데에 두면 어느 줄에서 고르라는 것인지가 글로만 남고,
    // 줄 밑에 붙어 있으면 그 줄에 대한 글로 읽힙니다.
    const width = FOCUS_W
    const height = FOCUS_H
    const x = PACK_X - width / 2
    const y = FOCUS_Y
    const panel = new Panel(width, height)
    panel.position.set(x, y)

    // **얻을 것을 옆에 세우지 않습니다.** 그 물건은 화면에 있습니다 — 상점의 딱지는 들려
    // 있고 팩의 카드는 펼쳐진 채이고, 새 물건은 거기서 날아갑니다. 여기 한 장 더 세우면
    // 같은 물건이 둘이고, 어느 쪽에서 오는지가 갈립니다.
    const left = 24
    const lead = richLine(
      tf('ui.swap.lead', { name: shopLabel(item.kind, item.id, this.game.data) }),
      richStyle('title', { fontWeight: WEIGHT.bold }), width - left - 140)
    lead.position.set(left, 14)

    const how = new Text({
      text: t(kind === 'joker' ? 'ui.focus.pick_joker' : 'ui.focus.pick_item'),
      style: { fontSize: TEXT.body, fill: UI.ink, fontWeight: WEIGHT.normal,
        wordWrap: true, wordWrapWidth: width - left - 140 },
    })
    how.position.set(left, 40)

    // **값이 들어온다는 것을 적어 둡니다.** 내놓는 것마다 `+$N` 이 단추에 적혀도, 그것이
    // 「이만큼 받는다」인지 「이만큼 버린다」인지는 적혀 있지 않으면 알 수 없습니다.
    const paid = new Text({
      text: t('ui.swap.paid'),
      style: { fontSize: TEXT.mini, fill: UI.money, fontWeight: WEIGHT.normal },
    })
    paid.position.set(left, 66)

    const cancel = new Button(t('ui.focus.cancel'), 100, 36, 'neutral', () => {
      if (this.game.input.ate()) return
      this.leaveFocus()
    })
    cancel.position.set(width - 24 - 100, (height - 36) / 2)
    this.game.spotNodes.set('focus:cancel', { node: cancel, cx: 50, cy: 18 })

    panel.addChild(lead, how, paid, cancel)
    this.focusLayer.addChild(panel)
    this.focusLayer.alpha = 0

    this.focus = { item, kind, panel, commit, slot }
    this.game.audio.play('card_select')
    this.game.refresh()
  }

  /** 자리를 비우던 것을 그만둡니다. 글은 막이 걷히는 동안 함께 옅어집니다. */
  leaveFocus(): void {
    if (!this.focus) return
    this.focus = undefined
    this.held = undefined
    this.game.spotNodes.delete('focus:cancel')
    this.game.refresh()
  }

  /**
   * 줄의 하나를 내놓고 그 자리에 새것을 받습니다.
   *
   * **판은 고른 그 프레임에 걷힙니다.** 값이 뜨고 물건이 떠나는 자리는 상점의 딱지나 팩의
   * 카드이고 이 판에는 없으므로, 판이 더 남아 있을 까닭이 없습니다.
   */
  private commitFocus(index: number): void {
    const focus = this.focus
    if (!focus) return
    this.leaveFocus()
    focus.commit(index)
  }

  /** 자리를 비우는 화면이 드는 것과 걷히는 것. 글이 아래에서 조금 올라오며 짙어집니다. */
  advanceFocus(seconds: number): void {
    const on = this.focus ? 1 : 0
    this.focusEnter += (on - this.focusEnter) * fraction(seconds, 12)
    if (!this.focus && this.focusEnter < 0.03) {
      this.focusEnter = 0
      if (this.focusLayer.children.length > 0) {
        this.focusLayer.removeChildren().forEach(child => child.destroy())
      }
      return
    }
    this.focusLayer.alpha = this.focusEnter
    this.focusLayer.y = (1 - this.focusEnter) * 14
  }

  /**
   * 집거나 산 플레잉 카드가 덱으로 들어갑니다.
   *
   * **조커는 줄에 꽂히고 소모품은 칸에 서는데, 덱은 상점에서 오른쪽으로 물러나 있었습니다.**
   * 그래서 집은 카드가 화면 밖으로 사라졌고, 덱에 들어갔다는 것은 알림 한 줄로만 남았습니다 —
   * 덱은 팩이 펼쳐진 동안 나와 있고, 카드가 그 위에 닿아 덱이 한 번 눌리고, 받은 것을
   * 본 다음 물러납니다.
   *
   * 날아가는 것은 펼쳐 있던 그 카드 자체입니다. 새로 만들면 같은 카드로 보이지 않습니다.
   *
   * **출발 배율을 받습니다.** 팩의 카드는 제 크기로 펼쳐져 있지만 상점의 딱지는 칸이 좁으면
   * 줄어들어 서 있고, 그것을 1 로 두면 카드가 떠나는 순간 한 번 커집니다.
   */
  flyToDeck(node: Container, from: { x: number; y: number },
                    scale = PACK_SCALE): void {
    node.removeFromParent()
    node.eventMode = 'none'
    node.filters = []
    node.alpha = 1
    node.position.set(from.x, from.y)
    // **팩의 카드 위, 고른 것의 단추 아래입니다.** 팩의 남은 카드 뒤로 들어가면 그 사이를
    // 지나며 가려집니다.
    node.zIndex = 555
    this.game.overlay.addChild(node)

    const motion = new Motion()
    motion.snap(from.x, from.y)
    motion.scale.snap(scale)
    motion.rotation.snap(0)
    // 오는 길이 보이도록 느리게 갑니다.
    motion.drift()
    motion.to(DECK_X, DECK_Y, 6)
    motion.scale.target = 1

    this.game.cards.deckPeekUntil = this.game.clock + DECK_PEEK
    this.game.cards.deckFlight = { node, motion, at: this.game.clock + LAND_AT }
  }

  /** 덱으로 날아가는 카드를 한 단계 옮기고, 닿으면 덱이 받습니다. */
  advanceDeckFlight(seconds: number): void {
    const one = this.game.cards.deckFlight
    if (!one) return
    one.motion.advance(seconds)
    one.node.position.set(one.motion.x.value, one.motion.y.value)
    one.node.scale.set(one.motion.scale.value)
    one.node.rotation = one.motion.rotation.value * (Math.PI / 180)
    if (this.game.clock < one.at) return

    // 닿았습니다. **덱이 한 번 눌리고 그 자리에 이름이 뜹니다.** 카드는 덱 더미에 들어간
    // 것이므로 더 그리지 않습니다.
    one.node.destroy({ children: true })
    this.game.cards.deckFlight = undefined
    this.game.chrome.deckBump.kick(240)
    this.game.audio.play('card_place')
    this.game.show.particles.burst(DECK_X, DECK_Y, 14, COLOR.cardEdge, 0.9, 0.7)
    this.game.show.popAt({ x: DECK_X - 36, y: DECK_Y - RISER_ON_CARD }, t('ui.deck.added'),
      UI.good, 0.5)
  }

  /**
   * 팩에서 집은 한 장이 제 자리로 갑니다. 그냥 집는 것과 바꿔 집는 것이 같은 길입니다.
   *
   * **조각과 흔들림은 없습니다.** 사는 것과 같은 규칙입니다(`purchase`) — 집은 그 카드가
   * 울렁이며 날아가 자리에서 번쩍이는 것이 「집었다」이고, 조각은 그 뒤에서 흩어질 뿐이라
   * 무엇을 집은 것인지가 남지 않습니다. 그냥 집을 때만 조각 둘과 흔들림이 있었고 바꿔 집을
   * 때는 없어서, 같은 일이 두 몸짓이었습니다.
   */
  pickFromPack(index: number, item: ShopItem, node: Container, action: Action,
                       wait: number): void {
    // 집은 카드도 산 것과 같이 제자리에서 옵니다. **카드의 가운데입니다** — 조커 뷰의
    // 피벗이 가운데이고, 소모품 쪽은 `itemFlying` 이 제 셈으로 옮깁니다.
    const from = { x: node.x, y: node.y }
    this.game.audio.play('pack_pick')

    // **플레잉 카드는 덱으로 갑니다.** 줄에 꽂히는 자리가 없으므로, 펼쳐 있던 그 카드가
    // 덱까지 날아가고 덱이 나와 받습니다. 판에서 물러나는 몫은 그 비행이 대신합니다 — 지금
    // 빼 두지 않으면 `layoutPack` 이 집은 카드로 보고 옅어지며 커지게 합니다.
    if (item.kind === ShopItemKind.PlayingCard) {
      this.game.pack.packViews.delete(index)
      this.flyToDeck(node, from)
      // 덱이 받고 물러나는 것까지 보고 나서 상점이 올라옵니다.
      this.game.shop.holdShop(DECK_PEEK)
      this.game.act(action)
      return
    }

    // 상점은 새것이 닿는 것을 보고 나서 올라옵니다.
    this.game.shop.holdShop(wait + LAND_AT + SHOP_RETURN_REST)
    // **액션보다 먼저입니다.** 액션이 지나며 「아무도 들지 않았으면 박자가 든다」를 셈하므로,
    // 뒤에 적으면 박자가 겹쳐 들고 그동안 소모품이 칸에 서지 못합니다.
    this.holdArrival(item, from, wait)
    this.game.act(action)
    this.game.later.push({ at: this.game.clock + wait + LAND_AT, run: () => this.landed(item) })
  }
}
