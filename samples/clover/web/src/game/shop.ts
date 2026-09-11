import { COLOR } from '../render/ink'
import { Container, Rectangle, Text } from 'pixi.js'
import { EditionKind } from '../generated/enums/edition-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { describe } from '../core/describe'
import { type Action } from '../core/run'
import { nameOf, t, tf } from '../core/strings'
import { NUMERALS } from '../ui/font'
import { rerollCost, type ShopItem } from '../core/shop'
import { newCounters } from '../core/state'
import { JokerView } from '../render/joker-view'
import { fraction, Spring } from '../render/motion'
import {
  giftChip, itemFace, kindName, packBlurb, packFace, packName, shopLabel, voucherFace,
} from '../render/faces'
import { popupLeft, rarityColor, SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { Button } from '../ui/widgets'
import { panelFrame, TITLE_BAR } from '../ui/modal'
import { cellPlate, hairline, priceText, SECTION_H, sectionHead } from '../ui/parts'
import {
  BUY_LINGER, CELL_GAP, CELL_H, CELL_W, DECK_PEEK, FOOT_BTN_H, GIFT_CHIP, GROUP_GAP, LAND_AT,
  PAY_BEAT, POPUP_X, REVEAL_RISE, REVEAL_SPAN, REVEAL_STEP, SHOP_BOTTOM, SHOP_LIFT, SHOP_RISE,
  STOCK_DROP,
} from './metrics'
import { type LookTick } from './types'
import { type Game } from './game'
export class ShopPart {
  constructor(private readonly game: Game) {}

  /** 선물 칸의 칩과 값. **겹치지 않는지를 도구가 봅니다.** */
  giftMark?: { chip: Container; price: Container }

  readonly shopTiles =
    new Map<number, { tile: Container; baseX: number; baseY: number; price: Container;
                     mid: number; key: string; slide: number
                     /** 단추가 서는 자리. 올라간 물건의 아랫변 바로 밑입니다. */
                     holdY: number
                     /**
                      * 고르면 올라가는 것.
                      *
                      * **칸의 테두리는 그대로 있습니다.** 칸은 상점의 자리이고 올라가는 것은
                      * 그 자리에 놓인 물건이므로, 통째로 올리면 진열대가 함께 들립니다.
                      */
                     lift: Container
                     /**
                      * 이 칸에 선 물건의 카드.
                      *
                      * **덱으로 가는 것이 이 카드 자체입니다.** 플레잉 카드를 사면 이것이
                      * 딱지에서 빠져나와 덱까지 날아갑니다 — 새로 만들어 띄우면 딱지에 남은
                      * 것과 둘이 되어, 같은 카드가 옮겨 간 것으로 읽히지 않습니다.
                      */
                     card: Container
                     /** 이 칸에 선 물건의 겉면. 판이 걸린 것만 있습니다. */
                     look?: LookTick }>()

  /**
   * 다시 세우기 전에 딱지들이 놓여 있던 자리. **칸의 차례대로 한 줄입니다.**
   *
   * **남은 것이 미끄러져 빈자리를 메웁니다.** 상점은 다시 세울 때마다 딱지를 통째로 버리고
   * 새로 만들므로, 그대로 두면 남은 물건이 새 자리에 툭 나타납니다 — 어느 것이 어디로 간
   * 것인지가 없고, 산 것의 자리가 메워진 것으로도 읽히지 않습니다.
   *
   * **물건마다 하나인 표가 아닙니다.** 같은 물건이 같은 값으로 둘 놓여 있으면 열쇠가 같아서,
   * 표 하나에는 뒤엣것의 자리만 남습니다 — 그 자리를 앞엣것이 받아 오른쪽에서 미끄러져
   * 들어왔습니다. `shopSpotWas` 가 차례를 지키며 하나씩 짚습니다.
   */
  private shopWas: { key: string; x: number }[] = []

  /** 위 줄에서 어디까지 짚었는가. 다시 세울 때마다 0 입니다. */
  private shopWasAt = 0

  /**
   * 상점의 칸과 팩의 칸마다 들리는 높이. 열쇠가 `card:<칸>` · `pack:<칸>` 입니다.
   *
   * **누른 것이 올라와야 골랐다는 것이 됩니다.** 단추가 그 밑에 서는 것만으로는 어느 칸을
   * 고른 것인지가 단추의 자리로만 읽히고, 칸 자체는 아무 일도 없었던 것처럼 남습니다 —
   * 조커와 소모품이 들리는 것과 같은 몸짓이고 같은 용수철입니다.
   *
   * **칸마다 하나입니다.** 용수철 하나를 모든 칸이 나눠 쓰고 있었고, 놓는 순간 「고른 칸」이
   * 아니게 되므로 그 칸의 높이가 한 프레임에 0 이 되었습니다 — 용수철은 그 뒤에 혼자
   * 잦아들었고 그것을 그리는 칸이 없었습니다. 그래서 놓을 때만 카드가 툭 내려앉았습니다.
   * 다른 칸으로 옮겨 고를 때도 같습니다 — 앞의 칸이 툭 내려가고 새 칸이 툭 올라옵니다.
   *
   * **칸은 다시 그릴 때마다 새로 만들어지므로 이 표는 그 밖에 있습니다**(`consumableLift`
   * 와 같은 이유입니다). 그 프레임에 만지지 않은 열쇠는 지웁니다 — 없어진 칸의 용수철은
   * 다시 도는 자리가 없어 그 값에 멈춰 있습니다.
   */
  private readonly shopLifts = new Map<string, Spring>()

  /**
   * 상점 판이 화면 아래에서 올라오는 동안의 세로 어긋남. 0 이면 제자리입니다.
   *
   * **판이 먼저 오고 줄은 그 뒤에 채워집니다.** 놓여 있던 자리에 갑자기 나타나면 무엇이
   * 열린 것인지가 남지 않습니다.
   */
  readonly shopSlide = new Spring(0, 200, 24)

  /**
   * 상점 판의 지금 높이.
   *
   * **줄이 없어지면 목표만 바뀌고 높이가 따라갑니다.** 바닥이 고정이므로 윗변이 내려오는
   * 것으로 보이고, 한 프레임에 줄어들면 판이 바뀐 것이 아니라 다른 판이 선 것으로 보입니다.
   */
  private readonly shopHeight = new Spring(0, 180, 22)

  /** 지금 그려진 상점 판의 틀. 높이가 움직이는 동안 매 프레임 다시 그립니다. */
  /** 상점 밑단의 단추 둘과 그것이 열리는 시각. */
  shopFoot?: { reroll: Button; leave: Button; afford: boolean; readyAt: number }

  /**
   * 바우처 칸. **상점의 카드 칸과 같은 규칙으로 고릅니다** — 누르면 들리고, 사는 것은 그
   * 밑에 서는 단추입니다. 한 번 누르면 곧바로 사지던 유일한 칸이었습니다.
   */
  voucherTile?: {
    tile: Container; lift: Container; price: Text; mid: number; holdY: number
    cost: number; title: string
  }

  shopFrame?: {
    node?: Container; foot: Container; body: Container
    x: number; width: number; height: number; drawn: number
  }

  readonly shopLayer = new Container()

  /**
   * 상점의 것들이 하나씩 서는 것.
   *
   * **한꺼번에 그려 놓으면 무엇이 놓여 있는지 훑어야 합니다.** 정산 판이 줄을 하나씩
   * 쌓았던 것과 같은 이유입니다 — 하나씩 서면 눈이 그 하나를 따라가고, 다 서고 나면
   * 그때가 고르는 때입니다.
   */
  readonly reveals: { node: Container; at: number; from: number; rise?: number }[] = []

  /** 지금 무엇을 세우고 있는가. 판이 그리기 전에 정합니다. */
  private revealing = { layer: this.shopLayer, base: 0, slot: 0, sound: false, note: 0 }

  /**
   * 지금 세우는 것이 **물건인가.**
   *
   * 소리를 여기서 가릅니다. 칸과 값에는 소리를 두지 않습니다 — 한동안 세우는 것마다
   * 하나씩 냈는데, 진열 한 번에 24번이었습니다(칸 7 · 물건 7 · 값 7 · 구획 머리 3).
   * 게다가 그 음원이 0.689초라 여섯이 함께 울렸습니다.
   */
  private stocking = false

  /**
   * 산 값이 빠져나가는 자리.
   *
   * **산 그 물건의 가운데입니다.** 판 가운데 아래에 뜨면 상점 판의 구석에 걸리고, 무엇을
   * 사서 나간 돈인지가 남지 않습니다. `sellFrom` 과 같이 액션 앞에서 적고 한 번 쓰면
   * 비웁니다 — 액션이 상점을 다시 그리며 딱지를 없애므로, 그 뒤에는 자리를 셀 수 없습니다.
   */
  boughtFrom!: { x: number; y: number } | undefined

  /**
   * 지금 떠 있는 상점 판의 자리.
   *
   * 딱지가 이미 없어졌을 때의 예비 자리이고, 도구가 줄의 자리를 읽는 곳입니다 — 바닥이
   * 고정이라 윗변은 줄 수에 따라 움직이므로 상수로는 셀 수 없습니다.
   */
  shopBox!: { x: number; y: number; width: number; height: number } | undefined

  /** 상점의 줄마다 몸통이 시작하는 `y`. 없는 줄은 없습니다. */
  shopRows: { items?: number; packs?: number; voucher?: number } = {}

  /**
   * 상점이 열릴 때의 칸 수. **팔린 자리는 비어 남습니다** — 물건 수로 칸을 세면 하나 살
   * 때마다 칸이 없어지고 판이 다시 짜입니다.
   */
  private shopCardCells = 0

  private shopPackCells = 0

  /** 상점이 뜬 시각. 그것을 기준으로 각자의 차례가 정해집니다. */
  shopRevealAt = 0

  /** 지금 상점이 떠 있는가. 떠 있지 않다가 뜨는 그 한 번만 차례를 다시 셉니다. */
  shopStanding = false

  /** 지난 프레임에 상점 판이 보였는가. 숨었다 다시 보이는 순간을 잡습니다. */
  private shopWasVisible = false

  /** 이번에 새로 선 것인가. 그때만 소리가 하나씩 납니다. */
  shopOpening = false

  /**
   * 상점이 이 시각까지는 내려가 있습니다.
   *
   * **산 것이 자리에 닿는 것을 보고 나서 올라옵니다.** 팩이 닫히는 것과 자리 비우기가
   * 끝나는 것은 물건이 아직 날아가는 중인 순간이고, 그때 판이 올라오면 어디에 들어간
   * 것인지를 볼 틈이 없습니다.
   */
  shopHoldUntil = 0

  /**
   * 상점이 이 시각까지는 올라와 있습니다. 팩을 뜯은 값을 치르는 동안입니다.
   *
   * **값을 치르는 것을 보고 나서 내려갑니다.** 뜯는 그 프레임에 내려가기 시작하면 값이 뜨는
   * 딱지가 판과 함께 내려가고, 얼마를 냈는지가 화면에 없습니다.
   */
  shopStayUntil = 0

  /**
   * 리롤한 직후.
   *
   * **있던 물건이 걷히고 새것이 내려와 앉습니다.** 리롤은 상점을 통째로 다시 그리므로 새
   * 물건이 그 자리에 툭 나타났고, 무엇이 바뀐 것인지가 남지 않았습니다 — 팩과 바우처는
   * 그대로이므로 그것들은 움직이지 않습니다.
   */
  rerolled = false

  reroll(): void {
    this.game.audio.play('shop_reroll')
    // **있던 물건이 먼저 걷힙니다.** 딱지들을 그 자리에 남겨 곧바로 옅어지게 하고, 다
    // 사라진 자리에서 상점이 다시 세워지며 새것이 하나씩 내려와 앉습니다 — 그 사이 팩과
    // 바우처는 그대로여서, 바뀐 것이 카드 줄이라는 것이 움직임으로 남습니다.
    for (const slot of [...this.shopTiles.keys()]) this.game.tray.lingerTile(slot, 0)
    this.rerolled = true
    this.game.act({ t: 'reroll' })
  }

  /**
   * 고른 상점 칸이 들리는 것.
   *
   * **누른 것이 올라와야 골랐다는 것이 됩니다.** 단추가 그 밑에 서는 것만으로는 어느 칸을
   * 고른 것인지가 단추의 자리로만 읽히고, 칸 자체는 아무 일도 없었던 것처럼 남습니다 —
   * 조커와 소모품을 고를 때와 같은 몸짓이고, 같은 용수철입니다.
   */
  advanceShopLift(seconds: number): void {
    this.game.session.hub.advance(seconds)
    this.game.session.login.advance(seconds)
    this.game.session.netStatus.advance(seconds)
    this.game.session.rollRank(seconds)

    // **딱지가 없으면 여기서 끝입니다.**
    if (this.shopTiles.size === 0 && this.game.pack.packSlotTiles.size === 0
        && !this.voucherTile) {
      if (this.shopLifts.size > 0) this.shopLifts.clear()
      return
    }

    // 지금 올라와 있어야 하는 칸 하나. 없으면 전부 제자리로 내려옵니다.
    // **자리를 비우는 동안은 사려던 그 딱지입니다.** 고른 것은 놓았지만 무엇을 위해 자리를
    // 비우는지가 그 딱지에 남아야 하고, 새 물건은 거기서 떠납니다.
    const up = this.game.tray.held?.kind === 'shop' ? `card:${this.game.tray.held.uid}`
      : this.game.tray.held?.kind === 'pack_slot' ? `pack:${this.game.tray.held.uid}`
      : this.game.tray.held?.kind === 'voucher' ? 'voucher'
      : this.game.tray.focus?.slot !== undefined ? `card:${this.game.tray.focus.slot}` : undefined
    const seen = new Set<string>()
    /**
     * 이 칸이 지금 얼마나 올라와 있는가. **한 프레임에 칸마다 한 번씩 돕니다.**
     *
     * **단추가 설 자리만큼 밀어 올립니다.** 단추는 그 칸이 서던 자리의 바닥에 서므로,
     * 물건이 그 위로 비켜서지 않으면 단추가 그림 위에 얹힙니다.
     */
    const liftOf = (key: string): number => {
      let spring = this.shopLifts.get(key)
      if (!spring) {
        spring = new Spring()
        this.shopLifts.set(key, spring)
      }
      spring.target = key === up ? SHOP_LIFT : 0
      spring.advance(seconds)
      seen.add(key)
      return spring.value
    }

    // 표를 지우는 자리와 여기가 갈라져 있으므로 한 겹 더 막습니다 — 지워진 것의 자리를
    // 만지면 그 프레임의 나머지가 통째로 죽습니다.
    // **단추가 값을 대신합니다.** 고른 칸에는 그 밑에 「산다」 가 서는데, 값이 그대로
    // 남아 있으면 단추 위로 그 값이 삐죽 보입니다 — 둘은 같은 자리의 것이고, 값을 보고
    // 고른 다음에 필요한 것은 살지 말지뿐입니다.
    for (const [slot, one] of this.shopTiles) {
      if (one.tile.destroyed) continue
      const key = `card:${slot}`
      // **칸이 아니라 그 안의 물건이 올라갑니다.** 칸은 상점의 자리이므로 그대로 있습니다.
      one.lift.y = -liftOf(key)
      one.price.visible = key !== up
      // 지난 자리에서 제자리로. **자리를 묻는 쪽에는 제자리를 답합니다** — 미끄러지는 것은
      // 눈에 보이는 것뿐이고, 단추가 서는 자리와 동전이 나오는 자리는 닿을 자리입니다.
      if (one.slide === 0) continue
      one.slide -= one.slide * fraction(seconds, 14)
      if (Math.abs(one.slide) < 0.5) one.slide = 0
      one.tile.x = one.baseX + one.slide
    }
    for (const [slot, one] of this.game.pack.packSlotTiles) {
      if (one.tile.destroyed) continue
      const key = `pack:${slot}`
      one.lift.y = -liftOf(key)
      one.price.visible = key !== up
    }
    const voucher = this.voucherTile
    if (voucher && !voucher.tile.destroyed) {
      voucher.lift.y = -liftOf('voucher')
      voucher.price.visible = up !== 'voucher'
    }

    // 이 프레임에 만지지 않은 것은 없어진 칸입니다. 그 높이는 버립니다.
    for (const key of [...this.shopLifts.keys()]) {
      if (!seen.has(key)) this.shopLifts.delete(key)
    }
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
  syncShop(): void {
    // **산 딱지가 아직 그 자리에 있는 동안은 다시 세우지 않습니다.** 다시 세우는 것은 남은
    // 것들을 당겨 빈자리를 메우는 것인데, 그 자리의 물건은 아직 사라지지 않았습니다 — 산
    // 것이 그대로 보이는 채로 그 옆이 먼저 메워집니다. 딱지가 사라질 때 `advanceLeavingTiles`
    // 가 다시 부릅니다.
    //
    // **상점을 떠났으면 그대로 세웁니다.** 판이 없어져야 하는데 이 길로 돌아가면 떠난 뒤에도
    // 상점이 0.6초 더 떠 있습니다.
    if (this.game.state.phase === 'shop' && this.shopStanding
        && this.game.cards.leavingTiles.length > 0) return

    // **지우는 그 자리에서 함께 비웁니다.** 딱지를 들고 있는 표가 둘 있는데, 그리는 쪽에서
    // 비우면 상점이 뜨지 않는 프레임에는 그 그리는 쪽에 닿지 않습니다 — 지워진 딱지가
    // 표에 남고, 매 프레임 그것의 자리를 만지는 곳이 그 자리에서 터집니다. 예외는 조용히
    // 삼켜지므로 화면은 멀쩡하고 그 뒤가 통째로 죽습니다.
    // **버리기 전에 지금 자리를 적어 둡니다.** 새로 만든 딱지가 이 자리에서 출발합니다.
    // 상점이 떠 있지 않았으면 적을 것이 없고, 적어 두면 다음 상점의 첫 딱지가 지난 판의
    // 자리에서 미끄러져 들어옵니다.
    this.shopWas = []
    this.shopWasAt = 0
    if (this.shopStanding) {
      for (const [, one] of this.shopTiles) {
        if (!one.tile.destroyed) this.shopWas.push({ key: one.key, x: one.tile.x })
      }
    }
    this.shopLayer.removeChildren().forEach(child => child.destroy())
    this.shopTiles.clear()
    this.game.pack.packSlotTiles.clear()
    this.shopFrame = undefined
    // **정산이 끝난 뒤에 뜹니다.** 돈이 들어오는 것을 보는 동안 상점이 이미 뒤에 떠 있으면
    // 그 판이 무엇을 막고 있는 것으로 보이고, 순서가 뒤집힙니다.
    // **차례는 새로 설 때만 다시 셉니다.** 하나 사면 다시 그리는데, 그때마다 처음부터
    // 세우면 산 다음에 남은 것들이 또 한 번 나타납니다.
    this.shopOpening = false

    // **동전이 나는 동안에도 떠 있습니다.** 하나 샀다고 판이 통째로 사라지면 무엇을 샀는지
    // 보다 판이 없어진 것이 먼저 보입니다. **카드가 걷히는 동안에는 뜨지 않습니다** — 낸
    // 카드가 아직 물러나는 중인데 판이 그 위에 뜨면 그 둘이 겹칩니다.
    // **정산을 기다리는 동안에도 뜨지 않습니다.** 카드가 걷힌 그 프레임에 상점이 먼저
    // 그려지고 정산은 그다음 프레임에 열리므로, 판이 떠 있는지만 보면 그 사이에 상점이
    // 한 번 번쩍입니다.
    const visible = this.game.state.phase === 'shop' && this.game.shopReady
      && !this.game.payout.payoutWanted && !this.game.panels.modals.has(this.game.payout.panel)
    this.shopLayer.visible = visible
    this.shopBox = undefined
    this.shopRows = {}
    // **국면으로 판정합니다.** 눈에 보이는지로 보면 연출이 한 박자 도는 동안 — 조커를 살 때
    // 동전이 날아가는 그 동안 — 상점이 잠깐 물러났다가 처음부터 다시 뜹니다.
    if (this.game.state.phase !== 'shop') this.shopStanding = false
    // **숨었다 다시 보이면 새로 서는 것입니다.** 카드가 걷히는 그 프레임에 판이 한 번 서고
    // 정산 판 뒤에 숨는데, 그때 잡아 둔 진열의 시각은 정산이 끝났을 때 이미 지난 것이라
    // 물건이 한꺼번에 나타났습니다 — 진열은 판이 보이는 순간부터 셉니다.
    if (visible && !this.shopWasVisible) this.shopStanding = false
    this.shopWasVisible = visible
    if (!visible) return
    if (!this.shopStanding) {
      this.shopStanding = true
      this.shopOpening = true
      // 판이 올라와 선 다음부터 줄이 채워집니다.
      this.shopRevealAt = this.game.clock + SHOP_RISE
    }

    const state = this.game.state
    // **왼쪽 패널을 비껴야 합니다.** 화면 한가운데에 서므로, 이보다 넓으면 판돈과 금액이
    // 적힌 칸을 덮습니다 — 왼쪽 판은 `x` 292 에서 끝나고, 그것을 비껴야 합니다.
    const width = 660

    // **칸 수는 열릴 때 셉니다.** 팔린 자리는 빈 칸으로 남고 판은 움직이지 않습니다 — 판이
    // 줄어들면 눈이 판을 따라가고 남은 물건을 놓칩니다.
    if (this.shopOpening) {
      this.shopCardCells = state.shop.cards.length
      this.shopPackCells = state.shop.packs.length
    }
    const cardCells = Math.max(this.shopCardCells, state.shop.cards.length, 1)
    const packCells = Math.max(this.shopPackCells, state.shop.packs.length, 1)
    const groups: { key: keyof ShopPart['shopRows']; title: string; cells: number }[] = [
      { key: 'items', title: t('ui.shop.wares'), cells: cardCells },
      { key: 'packs', title: t('ui.kind.pack'), cells: packCells },
    ]
    // **바우처 구획은 있을 때만 놓입니다.** 이번 안테에 바우처가 없고 산 것도 없으면 — 살
      // 것이 다 떨어진 안테가 그렇습니다 — 그 자리에 빈 칸 하나와 이름만 남습니다.
    if (state.shop.voucher || state.shop.voucherBought) {
      groups.push({ key: 'voucher', title: t('ui.kind.voucher'), cells: 1 })
    }
    const spanOf = (cells: number) => cells * CELL_W + (cells - 1) * CELL_GAP
    const full = groups.reduce((sum, group) => sum + spanOf(group.cells), 0)
      + GROUP_GAP * (groups.length - 1)
    // **칸이 늘어나도 한 줄은 판 안에 있어야 합니다.** 상점 칸을 늘리는 조커가 있으면
    // 3칸도 5칸도 됩니다 — 넘치면 줄 전체를 줄입니다.
    const room = width - 48
    const fit = full > room ? room / full : 1

    const headY = TITLE_BAR + 16
    const cellY = headY + SECTION_H + 10
    const footY = cellY + CELL_H * fit + 14
    const height = footY + 12 + FOOT_BTN_H + 16

    // **바닥에 맞춰 놓입니다.** 높이가 고정이므로 윗변도 고정입니다.
    const x = popupLeft(width)
    const y = SHOP_BOTTOM - height
    this.shopBox = { x, y, width, height }

    // 밑단 · 선 하나와 단추 둘. **둘은 색도 크기도 다릅니다** — 나아가는 것이 노랑입니다.
    const foot = new Container()
    const cost = rerollCost(this.game.data, state, state.shop)
    // **높이는 판 밑단의 단추와 같습니다.** 왼쪽 판의 런 정보·메뉴와 판 아래의
    // 낸다·버린다가 같은 높이이므로, 상점의 이 둘만 낮으면 아래 변에 세 가지 높이가
    // 생깁니다.
    const rerollW = 140
    const leaveW = 190
    const reroll = new Button(tf('ui.shop.reroll_cost', { n: cost }), rerollW, FOOT_BTN_H,
      'select', () => this.reroll())
    const leave = new Button(t('ui.button.next_blind'), leaveW, FOOT_BTN_H, 'primary',
      () => this.game.chrome.primary())
    const rule = hairline(width - 48)
    rule.position.set(24, footY)
    reroll.position.set(24, footY + 12)
    leave.position.set(width - 24 - leaveW, footY + 12)
    foot.addChild(rule, reroll, leave)
    this.game.spotNodes.set('reroll', { node: reroll, cx: rerollW / 2, cy: FOOT_BTN_H / 2 })
    this.game.spotNodes.set('nextBlind', { node: leave, cx: leaveW / 2, cy: FOOT_BTN_H / 2 })

    // **틀과 몸통이 갈립니다.** 틀은 판이 올라오는 동안 자리만 따라가고, 몸통은 한 번 그립니다.
    const inner = new Container()
    this.shopFrame = { foot, body: inner, x, width, height, drawn: -1 }
    if (this.shopOpening) {
      // 새로 서는 것은 화면 아래에서 올라옵니다.
      this.shopHeight.snap(height)
      this.shopSlide.snap(SIZE.height - y)
      this.shopSlide.target = 0
      // **그리는 자리도 함께 옮깁니다.** 용수철만 옮기면 그 값이 다음 틱에야 판에 닿습니다.
      this.placeShopLayer()
    } else {
      this.shopHeight.target = height
    }
    this.redrawShopFrame()
    this.shopLayer.addChild(inner)
    this.beginReveal(inner, this.shopRevealAt, this.shopOpening)

    // 지갑. 머리의 오른쪽에 어두운 칸 하나.
    const wallet = new Container()
    wallet.addChild(cellPlate(80, 30, UI.rule))
    const money = new Text({
      text: `$${this.game.shown.money}`,
      style: { fontSize: TEXT.big, fill: UI.ink, fontWeight: WEIGHT.bold, fontFamily: NUMERALS },
    })
    money.anchor.set(0.5, 0.5)
    money.position.set(40, 15)
    wallet.addChild(money)
    wallet.position.set(x + width - 24 - 80, y + (TITLE_BAR - 30) / 2)
    inner.addChild(wallet)

    // **진열은 두 번에 나눕니다.** 구획 머리와 빈 칸이 먼저 다 서고, 그다음 물건이 왼쪽부터
    // 하나씩 칸에 내려와 앉습니다 — 칸 하나마다 물건을 얹으면 진열이 아니라 나열입니다.
    const stock: { key: keyof ShopPart['shopRows']; put: () => void }[] = []
    let gx = x + (width - full * fit) / 2
    for (const group of groups) {
      const span = spanOf(group.cells) * fit
      const head = sectionHead(span, group.title)
      head.position.set(gx, y + headY)
      this.reveal(head)
      // 도구가 이 값으로 칸을 짚습니다.
      this.shopRows[group.key] = y + cellY
      for (let i = 0; i < group.cells; i++) {
        const cx = gx + i * (CELL_W + CELL_GAP) * fit
        const put = group.key === 'items' ? this.shopCardCell(i, cx, y + cellY, fit)
          : group.key === 'packs' ? this.shopPackCell(i, cx, y + cellY, fit)
          : this.shopVoucherCell(cx, y + cellY, fit)
        stock.push({ key: group.key, put })
      }
      gx += span + GROUP_GAP * fit
    }
    // **리롤한 직후에는 카드만 새로 내려와 앉습니다.** 팩과 바우처는 그대로이므로 지난
    // 차례(이미 지난 시각)로 두어 제자리에 그대로 서고, 카드는 지금부터 하나씩 소리와
    // 함께 내려옵니다 — 리롤이 바꾼 것이 무엇인지가 움직임으로 남습니다.
    const rerolled = this.rerolled
    this.rerolled = false
    let cardsEnd = this.shopRevealAt
    for (const one of stock) {
      if (rerolled && one.key === 'items') this.beginReveal(inner, this.game.clock + 0.1, true)
      one.put()
      if (rerolled && one.key === 'items') {
        cardsEnd = Math.max(cardsEnd,
          this.revealing.base + this.revealing.slot * REVEAL_STEP + REVEAL_SPAN)
        this.beginReveal(inner, this.shopRevealAt, false)
      }
    }

    // **진열이 끝나기 전에는 밑단의 둘이 잠깁니다.** 물건이 내려오는 중에 「다음
    // 블라인드로」 가 눌리면 무엇을 팔고 있었는지 보지 못한 채 판을 떠나고, 리롤은 아직
    // 서지도 않은 것을 다시 굴립니다 — 마지막 것이 다 선 시각이 그 시각입니다.
    this.shopFoot = {
      // **코어와 같은 문턱입니다**(`canPay`). 빚 한도를 세지 않아, 살 수 있는데 단추가
      // 눌리지 않는 구간이 있었습니다.
      reroll, leave, afford: state.money - cost >= state.rules.debtLimit,
      // 리롤로 새 카드가 내려오는 동안도 잠깁니다. 다 앉기 전에 다시 굴리면 무엇이 왔는지 못 봅니다.
      readyAt: Math.max(cardsEnd,
        this.shopRevealAt + this.revealing.slot * REVEAL_STEP + REVEAL_SPAN),
    }
    this.gateShopFoot()
  }

  /** 밑단의 둘을 지금 열 것인가. 진열이 끝나고, 리롤은 돈이 있을 때입니다. */
  private gateShopFoot(): void {
    const foot = this.shopFoot
    if (!foot || foot.leave.destroyed) return
    // **자리를 비우는 동안은 잠깁니다.** 상점은 그대로 떠 있고 고르는 것은 위 줄인데,
    // 그동안 리롤이 눌리면 사려던 딱지가 없어집니다.
    const ready = this.game.clock >= foot.readyAt && !this.game.tray.focus
    foot.leave.enabled = ready
    foot.reroll.enabled = ready && foot.afford
  }

  /**
   * 상점 판의 틀을 지금 자리로 그립니다.
   *
   * 높이는 고정이지만 판이 올라오는 동안 자리가 움직이므로, 용수철이 목표에 닿을 때까지만
   * 다시 그립니다. 밑단의 단추는 같은 것을 새 틀로 옮겨 붙이므로 누르던 채로 남습니다.
   */
  private redrawShopFrame(): void {
    const one = this.shopFrame
    if (!one) return
    const spring = this.shopHeight
    const shown = Math.abs(spring.value - spring.target) < 0.5 ? spring.target : spring.value
    if (Math.abs(shown - one.drawn) < 0.5) return

    one.foot.parent?.removeChild(one.foot)
    one.node?.destroy()
    const node = panelFrame(one.width, shown, t('ui.guide.shop.head'), undefined, undefined,
      false)
    node.position.set(one.x, SHOP_BOTTOM - shown)
    one.foot.position.set(one.x, SHOP_BOTTOM - shown)
    this.shopLayer.addChildAt(node, 0)
    this.shopLayer.addChild(one.foot)
    one.node = node
    one.drawn = shown
    one.body.y = one.height - shown
  }

  /**
   * 상점 판이 자리를 비켜 내려가 있어야 하는가. 팩을 뜯었거나 산 것이 닿는 것을 보는
   * 중입니다.
   *
   * **팩은 값을 치르는 것을 보고 나서입니다.** 그 전에는 뜯은 딱지 위에 값이 떠 있고
   * 동전이 나가는 중이라, 판이 그것을 들고 내려가면 얼마를 냈는지가 화면에 없습니다.
   *
   * **자리를 비우는 동안은 내려가지 않습니다.** 바꿔 사는 것도 사는 것이라, 값은 산 딱지
   * 위에 뜨고 새 물건은 그 딱지에서 떠나야 합니다 — 판이 내려가 있으면 그 둘이 다 없어서,
   * 값은 고르는 글 판의 가운데에 뜨고 물건은 그 옆에 세운 작은 카드에서 날아갔습니다.
   * 고르는 줄은 화면 위이고 상점은 아래이므로 겹치는 것도 없습니다.
   */
  get shopParked(): boolean {
    const packBusy = (this.game.state.pack !== null || this.game.pack.packPending)
      && this.game.clock >= this.shopStayUntil
    return packBusy || this.game.clock < this.shopHoldUntil
  }

  /** 상점을 이만큼 더 내려가 있게 합니다. 산 것이 닿는 것을 보는 동안입니다. */
  holdShop(seconds: number): void {
    this.shopHoldUntil = Math.max(this.shopHoldUntil, this.game.clock + seconds)
  }

  /** 상점 판이 내려가면 서는 자리. 화면 아래로 다 나간 자리입니다. */
  private get shopParkY(): number {
    return this.shopBox ? SIZE.height - this.shopBox.y : SIZE.height
  }

  /** 상점 판이 화면 아래로 물러나 있는가. 팩을 펴는 것이 이것을 기다립니다. */
  get shopAway(): boolean {
    return !this.shopLayer.visible || this.shopSlide.value > this.shopParkY - 40
  }

  /** 상점 판이 올라오는 것과 높이가 따라가는 것을 한 단계 진행합니다. */
  advanceShopPanel(seconds: number): void {
    if (!this.shopLayer.visible) return
    // **팩을 다루는 동안 상점은 자리를 비켜 줍니다.** 뜯은 동안은 화면 아래로 내려가
    // 있고, 끝나면 올라온 그 길로 다시 올라옵니다 — 판이 서 있는 채로 그 위에 카드를
    // 펼치면 두 화면이 한 자리에 겹칩니다. 자리를 비우는 동안은 내려가지 않습니다
    // (`shopParked`).
    this.shopSlide.target = this.shopParked ? this.shopParkY : 0
    this.shopSlide.advance(seconds)
    this.placeShopLayer()
    this.shopHeight.advance(seconds)
    this.redrawShopFrame()
    this.gateShopFoot()
  }

  /**
   * 용수철이 든 값을 판에 옮깁니다.
   *
   * **한 자리에서 옮깁니다.** 세우는 곳과 프레임마다 진행하는 곳 둘이 각자 옮기면 그중
   * 한쪽이 빠지고, 빠진 쪽은 한 프레임짜리 어긋남이라 눈에는 「한 번 튄다」로만 보입니다.
   *
   * 0.3 아래는 0 으로 봅니다 — 다 선 판이 반 픽셀 어긋난 자리에 있으면 글씨가 흐려집니다.
   */
  private placeShopLayer(): void {
    this.shopLayer.y = Math.abs(this.shopSlide.value) < 0.3 ? 0 : this.shopSlide.value
  }

  /**
   * 이제부터 세우는 것은 이 판의 것입니다.
   *
   * **소리는 새로 설 때만 냅니다.** 다시 그릴 때마다 내면 하나 살 때 남은 것들의 소리가
   * 한꺼번에 다시 납니다.
   */
  private beginReveal(layer: Container, base: number, sound: boolean): void {
    this.revealing = { layer, base, slot: 0, sound, note: 0 }
  }

  /**
   * 하나를 세웁니다.
   *
   * 한 번에 세우는 것들은 한 차례를 나눠 씁니다 — 칸 이름의 선과 글자가 그렇습니다.
   * 판이 뜬 지 오래되었으면 계산 결과가 이미 1이므로 그 자리에 그대로 놓입니다.
   */
  private reveal(...nodes: Container[]): void {
    this.revealInto(this.revealing.layer, 0, REVEAL_RISE, ...nodes)
  }

  /**
   * 하나를 세우되 어디에 · 몇 박자 쉬고 · 어느 쪽에서 올지를 정합니다.
   *
   * **상점의 진열이 씁니다.** 칸이 먼저 서고, 물건은 한 박자 쉬고 위에서 내려와 칸에
   * 앉고, 값은 그 뒤에 적힙니다 — 물건을 하나씩 놓는 손이 보이는 것이 진열입니다.
   * `rise` 가 양수면 아래에서 올라오고 음수면 위에서 내려옵니다.
   */
  private revealInto(parent: Container, pause: number, rise: number,
    ...nodes: Container[]): void {
    this.revealing.slot += pause
    const at = this.revealing.base + this.revealing.slot * REVEAL_STEP
    this.revealing.slot += 1
    // **물건이 앉을 때만 냅니다.** 그리고 앉는 차례마다 음이 한 계단 오릅니다 — 같은
    // 음을 일곱 번 내면 그것은 진열이 아니라 같은 소리 일곱 번이고, 오르면 진열 전체가
    // 한 소절로 들립니다.
    if (this.revealing.sound && this.stocking) {
      // **왼쪽부터 앉으므로 소리도 왼쪽부터입니다.** 진열이 어느 쪽까지 왔는지가 화면을
      // 보지 않아도 들립니다.
      const step = this.revealing.note
      this.game.show.notes.push({
        at, name: 'marimba', step, strength: 0.7, gap: REVEAL_STEP,
        pan: -0.35 + Math.min(1, step / 6) * 0.7,
      })
      this.revealing.note++
    }

    for (const node of nodes) {
      parent.addChild(node)
      const one = { node, at, from: node.y, rise }
      this.reveals.push(one)
      this.advanceOne(one)
    }
  }

  /**
   * 물건 하나가 칸에 앉습니다. **소리가 나는 것은 이 자리 하나입니다.**
   *
   * 값이 적히는 것은 이것 바로 뒤라, 둘 다 소리를 내면 0.13초 사이로 두 번 납니다 —
   * 그것이 「두 번씩 나는 느낌」이었습니다.
   */
  private revealStock(parent: Container, rise: number, ...nodes: Container[]): void {
    this.stocking = true
    this.revealInto(parent, 1, rise, ...nodes)
    this.stocking = false
  }

  /** 하나가 지금 어디까지 섰는가. */
  advanceOne(one: { node: Container; at: number; from: number; rise?: number }): void {
    const step = Math.max(0, Math.min(1, (this.game.clock - one.at) / REVEAL_SPAN))
    const eased = 1 - (1 - step) * (1 - step) * (1 - step)
    one.node.alpha = eased
    one.node.y = one.from + (1 - eased) * (one.rise ?? REVEAL_RISE)
  }

  advanceReveals(): void {
    for (const one of this.reveals) {
      if (one.node.alpha < 1) this.advanceOne(one)
    }
  }

  /**
   * 이 물건이 다시 세우기 전에 놓여 있던 자리. 없으면 `undefined` 입니다.
   *
   * **왼쪽 칸부터 차례로 짚고, 한 번 짚은 자리는 지나갑니다.** 열쇠는 갈래·이름·값·판이라
   * 같은 물건이 같은 값으로 둘 놓여 있으면 두 칸의 열쇠가 같습니다 — 표 하나에 자리 하나만
   * 담으면 그 열쇠에는 오른쪽 칸의 자리만 남고, 그것을 왼쪽 칸이 받아 오른쪽에서
   * 미끄러져 들어옵니다. **줄의 조커를 누르기만 해도 그렇게 보였습니다** — 누름이 상점을
   * 다시 세우고, 다시 세울 때마다 그 한 칸이 오른쪽으로 갔다가 제자리로 왔습니다.
   *
   * 차례를 지키므로 산 자리를 메우는 것은 그대로입니다 — 앞의 것을 사면 뒤엣것의 열쇠가
   * 커서보다 뒤에 있고, 그 자리에서 왼쪽으로 미끄러집니다.
   */
  private shopSpotWas(key: string): number | undefined {
    for (let i = this.shopWasAt; i < this.shopWas.length; i++) {
      if (this.shopWas[i].key !== key) continue
      this.shopWasAt = i + 1
      return this.shopWas[i].x
    }
    return undefined
  }

  /**
   * 상품 칸 하나.
   *
   * **줄에 놓이는 것은 카드입니다.** 아이콘을 얹은 딱지로 두면 살 때와 산 뒤의 모습이 달라
   * 같은 물건으로 보이지 않습니다 — 상점에 놓인 그 카드가 그대로 조커 줄에 놓입니다. 칸의
   * 테는 그 물건의 희귀도입니다.
   *
   * 칸은 지금 서고, 물건을 놓는 것은 돌려주는 함수가 합니다 — 칸이 다 선 뒤에 물건을 놓는
   * 순서를 부르는 쪽이 정합니다.
   */
  private shopCardCell(slot: number, cx: number, cy: number, fit: number): () => void {
    const item = this.game.state.shop.cards[slot]
    const tile = new Container()
    tile.position.set(cx, cy)
    tile.scale.set(fit)
    if (!item) {
      tile.addChild(cellPlate(CELL_W, CELL_H, UI.hairline, true))
      this.reveal(tile)
      return () => {}
    }

    const name = shopLabel(item.kind, item.id, this.game.data)
    const lines = this.shopLines(item)
    const rarity = item.kind === ShopItemKind.Joker
      ? this.game.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0
    const afford = this.game.shown.money >= item.cost
    const border = item.kind === ShopItemKind.Joker ? rarityColor(rarity)
      : item.kind === ShopItemKind.PlayingCard ? COLOR.cardEdge : UI.legendary
    tile.addChild(cellPlate(CELL_W, CELL_H, border))
    // **올라가는 것만 담습니다.** 테두리는 이 통 밖에 있으므로 제자리에 남습니다.
    const lift = new Container()
    tile.addChild(lift)

    const card = this.itemCard(item)
    card.position.set((CELL_W - SIZE.jokerWidth) / 2, 8)
    const price = priceText(item.cost, afford)
    price.position.set(CELL_W / 2, CELL_H - 20)

    // **누가 놓아 둔 것인지는 칩 하나입니다. 글이 아닙니다.**
    //
    // 칸은 104 × 166 이고 그 안에 88 × 124 카드와 값이 들어갑니다 — 이름이 들어갈 자리가
    // 없어서, 카드의 아랫변과 값 사이에 적었더니 값과 겹쳤습니다. 말은 쪽지에 두고 칸에는
    // 놓은 것의 칩을 얹습니다. 태그의 칩은 이미 머리띠에 쓰는 그림이고, 조커는 그 조커의
    // 그림입니다.
    if (item.gift !== undefined && item.gift !== '') {
      const chip = giftChip(item.gift, GIFT_CHIP)
      // 카드의 오른쪽 위 모서리에 걸칩니다. **카드 위입니다** — 칸의 여백에 두면 무엇에
      // 딸린 것인지가 끊깁니다.
      chip.position.set(CELL_W - (CELL_W - SIZE.jokerWidth) / 2 - GIFT_CHIP + 3, 5)
      lift.addChild(chip)
      // **값과 겹치지 않는지를 도구가 숫자로 봅니다.** 눈으로만 보던 자리이고, 글로 적었을
      // 때 실제로 값과 겹쳐 있었습니다.
      this.giftMark = { chip, price }
    }

    // **자리가 없다는 것은 적지 않습니다.** 사면 무엇과 바꿀지 고르는 화면이 서고 그것이
    // 이미 그 말입니다 — 칸마다 붉은 글 한 줄을 더 두면 값보다 그것이 먼저 읽힙니다.

    tile.alpha = afford ? 1 : 0.55
    tile.eventMode = 'static'
    tile.hitArea = new Rectangle(0, 0, CELL_W, CELL_H)
    tile.cursor = afford ? 'pointer' : 'default'
    const key = `${item.kind}:${item.id}:${item.cost}:${item.edition}`
    // **지난 자리에서 미끄러져 옵니다.** 상점은 다시 세울 때마다 딱지를 통째로 버리고 새로
    // 만들므로, 그대로 두면 산 것의 빈자리를 메우는 남은 물건이 새 자리에 툭 나타납니다 —
    // 어느 것이 어디로 간 것인지가 없고, 산 것의 자리가 메워진 것으로도 읽히지 않습니다.
    //
    // **한 번 쓴 자리는 다시 쓰지 않습니다.** 같은 물건이 같은 값으로 둘 놓여 있으면 열쇠가
    // 같으므로, 짝을 하나씩 지어 주지 않으면 둘이 같은 자리에서 출발합니다.
    const baseX = tile.x
    const was = this.shopSpotWas(key)
    // 처음 서는 물건은 미끄러지지 않습니다 — 그것은 진열이고, `reveal` 이 합니다.
    const slide = was === undefined ? 0 : was - baseX
    // **첫 프레임부터 지난 자리에 둡니다.** `advanceShopTiles` 는 다음 프레임에 도므로,
    // 여기서 옮기지 않으면 새 자리에 한 프레임 보이고 나서 지난 자리로 뛰었다 돌아옵니다.
    tile.x = baseX + slide
    this.shopTiles.set(slot, { tile, baseX, baseY: tile.y, price, key, slide, lift, card,
                               look: this.game.input.lookOf(card),
                               mid: baseX + CELL_W * fit / 2,
                               holdY: tile.y + (8 + SIZE.jokerHeight - SHOP_LIFT + 4) * fit })
    // **누르면 고르기만 합니다.** 사는 것은 그 밑에 서는 단추가 합니다.
    tile.on('pointertap', () => {
      if (this.game.input.ate()) return
      // **밝힐 금액은 지금 금액이 아니라 보이는 금액입니다.** 누를 때 다시 봅니다.
      if (this.cannotPay(item.cost)) return
      this.game.tray.pick('shop', slot)
    })
    this.game.input.tipOn(tile, at => {
      this.game.input.tooltip.show(name, kindName(item.kind), rarity,
        [...lines, ...this.giftLine(item.gift)], at, SIZE, item.cost)
    })
    this.reveal(tile)
    return () => {
      this.revealStock(lift, -STOCK_DROP, card)
      this.revealInto(tile, 0, 0, price)
    }
  }

  /**
   * 상점에 선 물건 하나의 카드.
   *
   * 조커는 **줄에 서는 그 카드 그대로**입니다 — 같은 클래스를 씁니다. 소모품과 플레잉
   * 카드는 같은 크기와 모양의 카드로 그립니다.
   */
  itemCard(item: ShopItem): Container {
    if (item.kind === ShopItemKind.Joker) {
      const row = this.game.data.tables.joker.findByJokerId(item.id)
      const view = new JokerView({
        uid: -1, jokerId: item.id, edition: item.edition as never,
        sticker: 0 as never, counters: newCounters(), age: 0, disabled: false,
      }, {
        name: row?.name ?? item.id,
        rarity: row?.rarity ?? 1,
        lines: describe(this.game.data, this.game.data.jokerEffects.get(item.id) ?? []),
        edition: this.game.cards.editionLook(item.edition as EditionKind),
      })
      // 상점의 카드는 흔들리지 않습니다. 줄에 선 것과 달리 고를 것이지 도는 것이 아닙니다.
      view.pivot.set(0, 0)
      view.position.set(0, 0)
      return view
    }

    return itemFace(this.game.data, item)
  }

  /**
   * 카드에서 얼굴만. **그림자는 뺍니다.**
   *
   * `faceCard` 가 만든 것은 그림자 하나와 얼굴 하나입니다. 셰이더를 통째로 걸면 그림자에도
   * 걸려, 카드 옆에 빛나는 얼룩 하나가 따로 남습니다.
   */
  /**
   * 상점 딱지 하나가 선 자리. 산 것이 여기에서 날아갑니다.
   *
   * **한 곳에서만 셉니다.** 같은 계산이 세 군데에 적혀 있었고, 그중 둘은 액션 뒤에 있어
   * 이미 없어진 딱지에게 물었습니다.
   */
  private shopSpot(slot: number): { x: number; y: number } {
    const one = this.shopTiles.get(slot)
    const tile = one?.tile
    // **없으면 상점 한가운데입니다.** 딱지는 상점을 다시 그릴 때마다 새로 만들어지므로,
    // 붙들고 있던 것이 이미 지워졌을 수 있습니다 — 그때 그 딱지에게 자리를 물으면 예외가
    // 나고, 누르는 자리의 예외는 조용히 삼켜져 그 뒤가 통째로 죽습니다.
    if (!tile) return this.shopMiddle()
    // **카드의 가운데입니다.** 조커 뷰의 피벗이 가운데이고, 소모품이 오는 길은 `itemFlying`
    // 이 가운데를 받아 제 셈으로 옮깁니다. 딱지에 배율이 붙어 있으면 그만큼 줄어든 카드의
    // 가운데입니다.
    return this.fromShop({ x: one.mid, y: tile.y + SIZE.jokerHeight / 2 * tile.scale.x })
  }

  /**
   * 상점 판 안의 자리를 판의 자리로 옮깁니다.
   *
   * **딱지의 `x`·`y` 는 상점 판 안의 값입니다.** 상점 판은 산 것을 다루는 동안 화면 아래로
   * 내려가고 그 몸통도 판이 자라는 동안 판 안에서 밀리므로, 딱지에게 물은 자리를 그대로
   * 넘기면 그만큼 어긋난 곳에서 값이 뜨고 동전이 나갑니다 — 받는 쪽은 전부 판의 좌표를
   * 씁니다(조커 줄 · 낸 카드 · 덱).
   */
  fromShop(at: { x: number; y: number }): { x: number; y: number } {
    const body = this.shopFrame?.body
    if (!body) return at
    return this.game.board.toLocal(body.toGlobal(at))
  }

  /** 상점 판의 한가운데. 딱지가 이미 없어졌을 때의 예비 자리입니다. */
  shopMiddle(): { x: number; y: number } {
    const box = this.shopBox
    if (!box) return { x: POPUP_X, y: SIZE.height / 2 }
    return { x: box.x + box.width / 2, y: box.y + box.height / 2 }
  }

  /** 그 갈래를 받을 자리가 있는가. */
  /**
   * 이 값을 낼 수 있는가.
   *
   * **코어와 같은 판정입니다.** 빚 한도까지는 낼 수 있으므로 금액이 값보다 적어도 살 수
   * 있고, `shown.money` 는 동전이 날아가는 동안의 값이라 이 판정에 쓰지 않습니다.
   */
  private canPay(cost: number): boolean {
    return this.game.state.money - cost >= this.game.state.rules.debtLimit
  }

  /**
   * 낼 수 없으면 소리로 거절합니다.
   *
   * **거절에는 소리가 있습니다.** 자리가 없어 집지 못하는 것과 자리를 비우는 동안 다른 갈래를
   * 누르는 것은 `joker_fizzle` 로 거절하는데, 돈이 모자란 것만 조용히 지나갔습니다 — 눌렀는데
   * 아무 일이 없으면 눌린 것인지도 모릅니다.
   */
  private cannotPay(cost: number): boolean {
    if (this.canPay(cost)) return false
    this.game.audio.play('joker_fizzle')
    return true
  }

  /**
   * 상점의 물건 하나를 삽니다.
   *
   * **자리가 없으면 무엇과 바꿀지를 묻습니다.** 그냥 눌리지 않게 두면 왜 안 되는지 알 수
   * 없고, 말없이 파는 것은 되돌릴 수 없는 일을 묻지 않고 하는 것입니다.
   *
   * **바꿔 사는 것도 사는 것입니다.** 내놓을 것을 고르는 한 단계가 앞에 놓일 뿐이고, 고른
   * 뒤는 그냥 사는 것과 같은 길입니다(`purchase`) — 상점은 그대로 떠 있고, 값은 산 딱지
   * 위에 뜨고, 새 물건은 그 딱지에서 날아갑니다. 상점을 내려 두고 글 옆에 세운 작은
   * 카드에서 날려 보내던 동안은 값이 뜨는 자리도 물건이 오는 길도 그냥 살 때와 달랐고,
   * 그 길에서만 조커가 출발 자리를 받지 못해 줄 위에서 떨어졌습니다.
   *
   * **딱지를 받지 않습니다.** 받아 두면 그것을 붙든 채로 상점이 다시 그려지고, 다시
   * 그려지는 것은 딱지를 통째로 없애는 것입니다 — 자리는 부르는 그때 칸 번호로 찾습니다.
   */
  buyFrom(slot: number, item: ShopItem): void {
    if (this.cannotPay(item.cost)) return
    this.game.input.tooltip.hide()
    if (!this.game.tray.roomFor(item.kind)) {
      this.game.tray.enterFocus(item, held => {
        // 내놓는 것이 먼저이고, 그다음은 그냥 사는 것입니다.
        this.game.session.giveUp(item, held)
        this.purchase(slot, item, { t: 'swap', slot, index: held })
      }, slot)
      return
    }
    this.purchase(slot, item, { t: 'buy', slot })
  }

  /**
   * 상점의 딱지 하나가 팔리는 것을 보입니다. 그냥 사는 것과 바꿔 사는 것이 같은 길입니다.
   *
   * 값이 딱지 위에 뜨고 동전이 나가는 동안(`BUY_LINGER`) 딱지가 그 자리에 남고, 그다음
   * 물건이 딱지에서 떠나 제 줄에 닿습니다(`LAND_AT`). 조커와 소모품이 같은 시각에 같은
   * 용수철로 옵니다 — `holdArrival` 이 그 둘을 한 길로 보냅니다.
   */
  private purchase(slot: number, item: ShopItem, action: Action): void {
    // **산 것이 그 자리에서 튀어 오릅니다.** 값을 치른 자리가 밝아지고, 그 자리에서
    // 조커가 날아가 줄에 꽂힙니다 — 조각 몇 개만으로는 눌린 것인지 산 것인지 모릅니다.
    // 조커를 사는 것과 소모품을 사는 것은 소리가 갈립니다.
    this.game.audio.play(item.kind === ShopItemKind.Joker ? 'joker_buy' : 'shop_buy')
    // **조각을 터뜨리지 않습니다.** 조각은 산 물건 뒤에서 흩어질 뿐이라 무엇을 산 것인지가
    // 남지 않습니다 — 산 그 물건이 울렁이며 날아가 자리에서 번쩍이는 것이 「샀다」입니다.
    this.game.show.flashPanel(UI.money, 0.35)

    // **산 자리를 액션보다 먼저 적어 둡니다.**
    //
    // `act` 는 상점을 다시 그리고, 다시 그리는 것은 딱지를 통째로 없애고 새로 만드는
    // 것입니다 — 그 뒤에 `tile.x` 를 읽으면 없어진 것에게 자리를 묻는 것이라 그 자리에서
    // 예외가 납니다. 예외는 누르는 자리에서 조용히 삼켜지므로 화면은 그대로 돌고, **산
    // 소모품만 오는 길 없이 제 칸에 툭 나타났습니다.** 조커는 이 값을 액션 앞에서 한 번만
    // 읽으므로 멀쩡했고, 팩에서 집는 것은 딱지가 없어지지 않으므로 멀쩡했습니다.
    const from = this.shopSpot(slot)
    // **딱지가 든 카드를 액션보다 먼저 붙듭니다.** `act` 는 상점을 다시 그리며 딱지를
    // 통째로 버리므로, 그 뒤에 물으면 이미 없습니다.
    const one = this.shopTiles.get(slot)
    // **딱지는 그 자리에 남습니다.** 값이 그 위에 뜨고 동전이 나가는 것을 본 다음에 물건이
    // 떠납니다 — 같은 프레임에 딱지가 없어지고 물건이 날아가면 값이 뜨는 자리가 빈자리입니다.
    this.game.tray.lingerTile(slot)
    this.boughtFrom = from

    // **플레잉 카드는 덱으로 갑니다.**
    //
    // 조커 줄에도 소모품 칸에도 자리가 없으므로, 값을 치른 그 카드가 딱지에서 빠져나와
    // 덱까지 날아가고 덱이 나와 받습니다 — 상점은 그동안 물러나 있다가 덱이 받는 것을 보고
    // 나서 올라옵니다. 팩에서 집는 것과 같은 길이고 같은 시간입니다.
    //
    // **이 갈래가 없어서 산 카드가 소모품 칸으로 갔습니다.** 코어는 덱에 넣는데 `landed`
    // 는 「조커가 아니면 소모품」으로 세고 있어서, 산 카드의 이름이 아무 상관 없는 소모품
    // 칸 위에 뜨고 그 칸 수가 강조되었습니다.
    if (item.kind === ShopItemKind.PlayingCard) {
      this.holdShop(BUY_LINGER + DECK_PEEK)
      this.game.act(action)
      // **값을 치르고 나서 떠납니다.** 딱지가 남아 있는 동안 그 위에서 값이 뜨고 동전이
      // 나가고, 그 박자가 끝나는 프레임에 카드가 딱지에서 빠져 덱으로 갑니다.
      this.game.later.push({
        at: this.game.clock + BUY_LINGER,
        run: () => {
          const card = one?.card
          if (!card || card.destroyed) return
          // **지금 놓여 있는 자리와 배율입니다.** 딱지는 판에서 떼어져 판 위에 남아 있고,
          // 고른 것이 올라가 있으면 그만큼 위입니다.
          this.game.tray.flyToDeck(card, this.game.overlay.toLocal(card.getGlobalPosition()),
                         one.tile.destroyed ? 1 : one.tile.scale.x)
        },
      })
      return
    }

    // **액션보다 먼저입니다.** 물건은 딱지가 사라질 때 떠나고, 그때까지 제 칸에 서지
    // 않습니다 — 액션이 지나며 화면을 한 번 그리므로 그 뒤에 붙들면 늦습니다.
    this.game.tray.holdArrival(item, from)
    this.game.act(action)

    // 날아가 닿는 데까지가 한 박자입니다. 닿는 자리에서 이름과 소리가 납니다.
    this.game.later.push({ at: this.game.clock + BUY_LINGER + LAND_AT,
      run: () => this.game.tray.landed(item) })
  }

  /**
   * 팩.
   *
   * **사는 것이 아니라 뜯는 것입니다** — 값을 내면 몇 장이 펼쳐지고 그중에서 고릅니다.
   * 그래서 카드가 아니라 **봉지**로 그립니다. 크기는 카드에 맞추되 위가 톱니로 뜯기게 되어
   * 있고, 그 톱니 하나가 「이건 여는 것이다」를 말합니다.
   */
  /**
   * 팩 칸 하나. 봉지 하나가 카드 크기로 서고 아래에 값입니다.
   */
  private shopPackCell(slot: number, cx: number, cy: number, fit: number): () => void {
    const packId = this.game.state.shop.packs[slot]
    const row = packId === undefined ? undefined
      : this.game.data.tables.boosterPack.findByPackId(packId)
    const tile = new Container()
    tile.position.set(cx, cy)
    tile.scale.set(fit)
    if (packId === undefined || !row) {
      tile.addChild(cellPlate(CELL_W, CELL_H, UI.hairline, true))
      this.reveal(tile)
      return () => {}
    }

    const afford = this.game.shown.money >= row.cost
    const w = SIZE.jokerWidth
    tile.addChild(cellPlate(CELL_W, CELL_H, UI.hairline))
    // **올라가는 것만 담습니다.** 테두리는 이 통 밖에 있으므로 제자리에 남습니다.
    const lift = new Container()
    tile.addChild(lift)

    // **포장지는 도감과 같은 것입니다.** 값과 누름만 여기서 얹습니다.
    const bag = packFace(row)
    bag.position.set((CELL_W - w) / 2, 8)

    const price = priceText(row.cost, afford)
    price.position.set(CELL_W / 2, CELL_H - 20)

    tile.alpha = afford ? 1 : 0.55
    tile.eventMode = 'static'
    tile.hitArea = new Rectangle(0, 0, CELL_W, CELL_H)
    tile.cursor = afford ? 'pointer' : 'default'
    // **누르면 고르기만 합니다.** 뜯는 것은 그 밑에 서는 단추가 합니다 — 뜯은 팩은 무르지
    // 못합니다.
    this.game.pack.packSlotTiles.set(slot, { tile, height: CELL_H, baseY: tile.y, price, lift,
                                   mid: tile.x + CELL_W * fit / 2,
                                   holdY: tile.y + (8 + SIZE.jokerHeight - SHOP_LIFT + 4) * fit })
    tile.on('pointertap', () => {
      if (this.game.input.ate()) return
      if (this.cannotPay(row.cost)) return
      this.game.tray.pick('pack_slot', slot)
    })
    this.game.input.tipOn(tile, at => {
      this.game.input.tooltip.show(packName(row.kind, row.size), t('ui.kind.pack'), 0,
        [packBlurb(row.kind), tf('ui.pack.spread', { cards: row.cards, picks: row.picks })],
        at, SIZE, row.cost)
    })
    this.reveal(tile)
    return () => {
      this.revealStock(lift, -STOCK_DROP, bag)
      this.revealInto(tile, 0, 0, price)
    }
  }

  /**
   * 바우처를 삽니다.
   *
   * **바우처는 들어갈 칸이 없습니다.** 규칙으로 들어가므로, 산 자리에서 이름이 뜨는 것이
   * 그것을 얻었다는 유일한 표시입니다.
   *
   * **소리와 판 번쩍임은 규칙 판이 냅니다.** 바뀐 규칙이 판으로 뜰 때(`showRuleChange`)
   * `voucher_buy` 와 번쩍임과 「적용 중」의 밝아짐이 나므로, 여기서도 내면 한 번 누름에 같은
   * 소리와 번쩍임이 둘입니다. 조각은 없습니다 — 사는 것은 조각을 터뜨리지 않습니다
   * (`purchase`).
   */
  buyVoucher(): void {
    const one = this.voucherTile
    if (!one || one.tile.destroyed || !this.canPay(one.cost)) return
    this.game.input.tooltip.hide()
    // **값이 뜨는 자리는 카드의 가운데입니다.** 상점 카드와 같습니다 — 칸의 가운데는 그보다
    // 13픽셀 아래입니다.
    const at = this.fromShop({
      x: one.mid, y: one.tile.y + (8 + SIZE.jokerHeight / 2) * one.tile.scale.x,
    })
    this.game.show.popAt(at, one.title, UI.money, 0.5)
    this.boughtFrom = at
    // 딱지가 남아 값과 이름이 그 위에 뜹니다. 다 보고 난 뒤에 빈 칸이 됩니다.
    this.voucherTile = undefined
    this.game.tray.lingerNode(one.tile, PAY_BEAT)
    this.game.act({ t: 'buy_voucher' })
  }

  /**
   * 바우처 칸. **바우처도 카드입니다** — 크림색 얼굴에 이름과 한 줄. 상점의 물건이 전부
   * 카드여야 한 줄에 놓입니다. 한 안테에 하나이고 런이 끝날 때까지 남습니다.
   */
  private shopVoucherCell(cx: number, cy: number, fit: number): () => void {
    const id = this.game.state.shop.voucher
    const tile = new Container()
    tile.position.set(cx, cy)
    tile.scale.set(fit)
    if (!id) {
      tile.addChild(cellPlate(CELL_W, CELL_H, UI.hairline, true))
      // 산 것은 빈자리가 아니라 적힌 사실입니다.
      if (this.game.state.shop.voucherBought) {
        const none = new Text({
          text: t('ui.shop.voucher_taken'),
          style: {
            fontSize: TEXT.micro, fill: UI.inkDim, fontWeight: WEIGHT.normal, align: 'center',
            wordWrap: true, wordWrapWidth: CELL_W - 16, breakWords: true, lineHeight: 13,
          },
        })
        none.anchor.set(0.5, 0.5)
        none.position.set(CELL_W / 2, CELL_H / 2)
        tile.addChild(none)
      }
      this.reveal(tile)
      return () => {}
    }

    const row = this.game.data.tables.voucher.findByVoucherId(id)
    const lines = describe(this.game.data, this.game.data.voucherEffects.get(id) ?? [])
    const cost = this.game.data.economy.voucherCost
    const afford = this.game.shown.money >= cost
    const title = nameOf(this.game.data, 'voucher', id, row?.name ?? '')
    const w = SIZE.jokerWidth
    tile.addChild(cellPlate(CELL_W, CELL_H, UI.hairline))
    // **올라가는 것만 담습니다.** 카드 칸·팩 칸과 같습니다 — 테두리는 이 통 밖에 있으므로
    // 제자리에 남습니다. 딱지 통째로 올리던 동안은 진열될 때 테두리까지 함께 움직였습니다.
    const lift = new Container()
    tile.addChild(lift)

    // **얼굴은 도감과 같은 것입니다.** 값과 누름만 여기서 얹습니다.
    const face = voucherFace(this.game.data, id, lines[0] ?? t('ui.note.rest_of_run'))
    face.position.set((CELL_W - w) / 2, 8)

    const price = priceText(cost, afford)
    price.position.set(CELL_W / 2, CELL_H - 20)

    tile.alpha = afford ? 1 : 0.55
    tile.eventMode = 'static'
    tile.hitArea = new Rectangle(0, 0, CELL_W, CELL_H)
    tile.cursor = afford ? 'pointer' : 'default'
    // **자리는 화면이 알립니다.** 바우처는 규칙으로 들어가는 유일한 물건이므로, 규칙이
    // 바뀌는 것을 재는 도구가 눌러야 하는 자리입니다 — 카드 칸만 알리고 있었습니다.
    this.game.spotNodes.set('voucher', { node: tile, cx: CELL_W * fit / 2, cy: CELL_H * fit / 2 })
    this.voucherTile = { tile, lift, price, cost, title,
                         mid: tile.x + CELL_W * fit / 2,
                         holdY: tile.y + (8 + SIZE.jokerHeight - SHOP_LIFT + 4) * fit }
    // **누르면 고르기만 합니다.** 사는 것은 그 밑에 서는 단추가 합니다 — 카드 칸·팩 칸과 같은
    // 규칙이고, 바우처만 한 번에 사지던 것을 걷었습니다.
    tile.on('pointertap', () => {
      if (this.game.input.ate()) return
      if (this.cannotPay(cost)) return
      this.game.tray.pick('voucher', 0)
    })
    // 값 칩도 카드 칸과 같이 붙습니다.
    this.game.input.tipOn(tile, at => {
      this.game.input.tooltip.show(title, t('ui.kind.voucher'), 0, lines, at, SIZE, cost)
    })
    this.reveal(tile)
    return () => {
      this.revealStock(lift, -STOCK_DROP, face)
      this.revealInto(tile, 0, 0, price)
    }
  }

  shopLines(item: { kind: ShopItemKind; id: string }): string[] {
    switch (item.kind) {
      case ShopItemKind.Joker:
        return describe(this.game.data, this.game.data.jokerEffects.get(item.id) ?? [])
      case ShopItemKind.Tarot: return this.game.tray.consumableLines(1, item.id)
      case ShopItemKind.Planet: return this.game.tray.consumableLines(2, item.id)
      case ShopItemKind.Spectral: return this.game.tray.consumableLines(3, item.id)
      default: return []
    }
  }

  /**
   * 놓아 둔 것의 이름 한 줄. **쪽지에 들어갑니다.**
   *
   * 칸에는 칩만 얹습니다 — 그 칸에 이름이 들어갈 자리가 없습니다.
   */
  private giftLine(gift: string | undefined): string[] {
    const name = gift === undefined || gift === '' ? undefined
      : this.game.panels.keyName(`tag.${gift}`) ?? this.game.panels.keyName(`joker.${gift}`)
    return name === undefined ? [] : [tf('ui.shop.gift', { name })]
  }
}
