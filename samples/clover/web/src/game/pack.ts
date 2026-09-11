import { Container, Rectangle, Text } from 'pixi.js'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { t, tf } from '../core/strings'
import { type ShopItem } from '../core/shop'
import { ArriveFilter } from '../shader/arrive'
import { fraction } from '../render/motion'
import { kindName, shopLabel } from '../render/faces'
import { SIZE, UI } from '../render/theme'
import { Button } from '../ui/widgets'
import { attachTip } from '../ui/tip'
import {
  BUY_LINGER, PACK_CARD_W, PACK_CARDS_Y, PACK_HOLD_RISE, PACK_SCALE, PACK_X, PAY_BEAT,
} from './metrics'
import { type PackFace, type PackView } from './types'
import { type Game } from './game'
export class PackPart {
  constructor(private readonly game: Game) {}

  /** 상점의 팩 딱지들. 카드 딱지와 높이가 달라 그것도 함께 들고 있습니다. */
  readonly packSlotTiles =
    new Map<number, { tile: Container; height: number; baseY: number;
                     price: Container; mid: number
                     /** 단추가 서는 자리. 올라간 봉지의 아랫변 바로 밑입니다. */
                     holdY: number
                     /** 고르면 올라가는 것. 칸의 테두리는 그대로 있습니다. */
                     lift: Container }>()

  /** 뜯어 놓은 팩. 상점 위를 덮습니다. */
  readonly packLayer = new Container()

  /**
   * 펼친 팩의 카드들.
   *
   * **낱장이 자기 용수철을 가집니다.** 한 장을 집으면 남은 것들이 새 자리로 미끄러져야
   * 하고, 매번 다시 만들면 그 자리에 순간이동합니다.
   */
  readonly packViews = new Map<number, PackView>()

  /** 물러나는 중인 카드들. 집은 그 한 장이 옅어지며 커집니다. */
  readonly packGone: { node: Container; life: number }[] = []

  /** 지금 그려진 팩. 바뀌면 처음부터 다시 폅니다. */
  packShown = ''

  /** 덮개가 짙어진 정도. 0 에서 1 로 갑니다. */
  packEnter = 0

  /**
   * 뜯었는데 아직 펴지 않은 팩.
   *
   * **상점이 물러난 뒤에 폅니다.** 뜯는 그 자리에서 펴면 상점 판이 아직 떠 있는 위로
   * 카드가 나오고, 판이 내려가는 것과 카드가 나오는 것이 한 화면에 겹칩니다 — 산 것을
   * 다루는 동안 상점은 자리를 비켜 줍니다.
   */
  packPending = false

  packNote?: Text

  /** 뜯은 팩의 이름. 덮개와 함께 들고 납니다. */
  packTitle?: Text

  packSkip?: Button

  /** 뜯어 놓은 팩에 플레잉 카드가 있는가. 덱은 그 팩에서만 나와 받습니다. */
  get packHoldsCards(): boolean {
    const open = this.game.state.pack
    return open !== null && open.options.some(item => item.kind === ShopItemKind.PlayingCard)
  }

  /**
   * 상점의 팩 하나를 뜯습니다.
   *
   * **누름과 갈라 두었습니다.** 뜯은 팩은 무르지 못하므로 한 번 더 눌러야 합니다.
   */
  openPackSlot(slot: number): void {
    const spot = this.packSlotTiles.get(slot)
    if (this.game.state.shop.packs[slot] === undefined) return

    this.game.input.tooltip.hide()
    this.game.audio.play('pack_open')
    // **값이 이 딱지에서 납니다.** 어느 것을 뜯었는지가 거기에 남습니다. 딱지가 이미
    // 지워졌으면 상점 한가운데입니다. **봉지의 가운데입니다** — 상점 카드의 값이 카드의
    // 가운데에서 뜨는 것과 같은 자리이고, 칸의 가운데는 그보다 13픽셀 아래입니다.
    const from = spot
      ? this.game.shop.fromShop({
        x: spot.mid, y: spot.baseY + (8 + SIZE.jokerHeight / 2) * spot.tile.scale.x,
      })
      : this.game.shop.shopMiddle()
    // **값을 치른 자리가 밝아집니다. 조각과 흔들림은 없습니다** — 사는 것과 같은 규칙입니다
    // (`purchase`). 팩만 조각을 내고 판은 밝히지 않아, 같은 「값을 치렀다」가 둘이었습니다.
    this.game.show.flashPanel(UI.money, 0.35)
    this.game.shop.boughtFrom = from
    // **값을 치르는 것이 먼저입니다.** 뜯은 딱지가 그 자리에 남아 값이 그 위에 뜨고 동전이
    // 나가는 것을 보고 난 뒤에, 딱지가 사라지고 상점이 내려가고 카드가 깔립니다.
    if (spot) {
      this.packSlotTiles.delete(slot)
      this.game.tray.lingerNode(spot.tile, PAY_BEAT)
    }
    this.game.shop.shopStayUntil = this.game.clock + PAY_BEAT
    this.game.act({ t: 'buy_pack', slot })
  }

  /**
   * 뜯어 놓은 팩.
   *
   * **펼쳐 놓고 하나를 집습니다.** 딱지에 설명을 적어 나란히 세우면 읽고 나서 고르는 일이
   * 되고, 그것은 카드 게임이 아니라 목록입니다.
   *
   * **판을 매 프레임 다시 만들지 않습니다.** 다시 만들면 한 장을 집었을 때 남은 카드가
   * 새 자리에 순간이동합니다 — 카드는 미끄러져 가야 하고, 그러려면 그 카드가 같은 카드로
   * 남아 있어야 합니다. 그래서 뜯을 때 한 번 짓고, 그다음은 자리만 다시 정합니다.
   */
  syncPack(): void {
    const open = this.game.state.pack
    // 어느 팩을 뜯었는가. 바뀌면 처음부터 다시 폅니다.
    const key = open ? open.packId + ':' + open.options.length : ''

    if (key !== this.packShown) {
      // **상점이 물러난 뒤에 폅니다.** 뜯는 그 자리에서 펴면 상점 판이 아직 떠 있는 위로
      // 카드가 나옵니다 — 판이 내려가는 동안 막이 짙어지고, 다 내려가면 `advancePack` 이
      // 이곳을 다시 부릅니다.
      if (open && !this.game.shop.shopAway) {
        this.packPending = true
        return
      }
      this.packPending = false
      this.packShown = key
      if (open) this.game.show.buildPack()
    }
    if (open) this.layoutPack()
  }

  /**
   * 자리를 다시 정합니다.
   *
   * **남은 것만 다시 가운데로 모읍니다** — 집어 간 자리를 비워 두면 부챗살에 이가 빠지고,
   * 순간이동하면 어느 카드가 어디로 갔는지가 남지 않습니다.
   */
  private layoutPack(): void {
    const open = this.game.state.pack
    if (!open) return

    if (this.packNote) this.packNote.text = this.packLine(open.picksLeft)
    if (this.packSkip) {
      const untouched = open.taken.every(one => !one)
      this.packSkip.text = untouched ? t('ui.button.skip') : t('ui.button.clear')
    }

    // 집어 간 것은 판에서 물러납니다.
    for (const [index, one] of [...this.packViews]) {
      if (!open.taken[index]) continue
      this.packViews.delete(index)
      this.packGone.push({ node: one.face.node, life: 0 })
    }

    const left = [...this.packViews.values()].sort((a, b) => a.index - b.index)
    // 손패와 같은 간격입니다.
    const spacing = PACK_CARD_W + 12
    const startX = PACK_X - ((left.length - 1) * spacing) / 2

    // 지난번에 펼쳐져 있던 자리는 버립니다. 뜯을 때마다 장수가 다릅니다.
    for (const key of Object.keys(this.game.spots)) {
      if (key.startsWith('pack:')) delete this.game.spots[key]
    }

    left.forEach((one, slot) => {
      // **한 줄로 폅니다.** 부챗살이 보기에는 좋았는데, 집는 단추가 그 곡선 아래 어디에
      // 서든 어느 카드와는 붙고 어느 카드와는 벌어집니다 — 카드가 한 높이에 있어야 그
      // 아래의 한 줄이 셋 모두의 것이 됩니다.
      one.motion.to(startX + slot * spacing, PACK_CARDS_Y, 0)
      // **펼쳐진 낱장의 자리를 알립니다.** 몇 장이 펼쳐지는지는 팩의 갈래가 정하므로
      // 도구가 가운데 한 장만 짚어 왔고, 그것은 장수가 짝수인 팩에서는 두 장 사이입니다.
      this.game.spots[`pack:${slot}`] = { x: startX + slot * spacing, y: PACK_CARDS_Y }
    })
  }

  /**
   * 펼친 카드의 얼굴을 다시 그립니다.
   *
   * **판을 다시 짓지 않습니다.** 그림은 처음 물어볼 때부터 읽히기 시작하므로 팩을 뜯는
   * 순간에는 아직 없을 수 있고, 그때 판을 다시 지으면 부챗살이 처음부터 다시 펴집니다 —
   * 자리와 용수철은 그대로 두고 얼굴만 바꿉니다.
   */
  repaintPack(): void {
    for (const one of this.packViews.values()) {
      // **통을 두고 카드만 갈아 끼웁니다.** 자리와 배율과 반짝임은 그 통에 걸려 있어서,
      // 통째로 비우면 그림이 들어오는 순간에 그것들이 처음으로 돌아갑니다.
      const fresh = this.game.shop.itemCard(one.item)
      one.face.node.removeChild(one.face.card)
      one.face.card.destroy({ children: true })
      one.face.card = fresh
      one.face.node.addChildAt(fresh, 0)
    }
  }

  /** 몇 장을 더 고르는가. 건너뛸 수 있다는 것도 함께 적습니다. */
  packLine(left: number): string {
    return tf('ui.pack.pick_from', { n: left }) + t('ui.hint.skip_if_unwanted')
  }

  /** 펼쳐 놓는 카드 한 장. */
  /**
   * 어느 자리가 찼는가.
   *
   * **「자리가 없습니다」로는 모자랍니다.** 조커 칸이 찬 것과 소모품 칸이 찬 것은 다음에
   * 할 일이 다릅니다 — 하나는 조커를 팔아야 하고 하나는 소모품을 써야 합니다.
   */
  private fullNote(kind: ShopItemKind): string {
    return t(kind === ShopItemKind.Joker ? 'ui.pack.jokers_full' : 'ui.pack.consumables_full')
  }

  /**
   * 팩에 펼쳐 놓는 카드 한 장.
   *
   * **팩의 색은 이제 여기서 쓰지 않습니다.** 집을 때 터지는 조각의 색이었는데, 집는 일이
   * `takeFromPack` 으로 옮겨 가면서 그 색도 그쪽에서 셉니다.
   */
  packCard(item: ShopItem, index: number): PackFace {
    const name = shopLabel(item.kind, item.id, this.game.data)
    const lines = this.game.shop.shopLines(item)
    const rarity = item.kind === ShopItemKind.Joker
      ? this.game.data.tables.joker.findByJokerId(item.id)?.rarity ?? 1 : 0

    const node = new Container()
    node.pivot.set(SIZE.jokerWidth / 2, SIZE.jokerHeight / 2)
    node.scale.set(PACK_SCALE)
    const card = this.game.shop.itemCard(item)
    node.addChild(card)

    // **자리가 없다는 것은 카드에 적지도 카드를 옅게 하지도 않습니다.** 집으면 무엇과
    // 바꿀지 고르는 화면이 서고 그것이 이미 그 말입니다 — 카드 한가운데를 띠가 가로지르면
    // 무엇을 고르는 자리인지가 그 띠에 덮이고, 옅게 하면 펼친 것이 전부 옅어집니다.

    node.eventMode = 'static'
    node.cursor = 'pointer'
    node.hitArea = new Rectangle(0, 0, SIZE.jokerWidth, SIZE.jokerHeight)
    // 카드가 들리는 것과 설명이 뜨는 것이 함께 있어서 `tipOn` 을 쓰지 않습니다 —
    // **손가락으로는 들리는 것만 먼저 일어나고, 설명은 꾸욱 눌러야 뜹니다.**
    const tip = (): void => {
      // 다 서지 않은 카드는 설명을 띄우지 않습니다. `tipOn` 과 같은 문턱입니다.
      if (node.alpha < 0.99) return
      this.game.input.tooltip.show(name, kindName(item.kind), rarity, lines,
        this.game.input.tipBox(node), SIZE)
    }
    // **마우스를 올리는 것은 설명까지입니다.** 올리기만 해도 카드가 들리면 상점의 칸과
    // 몸짓이 갈립니다 — 상점은 눌러서 고른 것만 들리고, 팩도 그래야 어느 것을 집으려는
    // 중인지가 한 가지로 읽힙니다.
    attachTip(node, this.game.input.hold, tip, () => this.game.input.tooltip.hide())
    // **누르면 고르기만 합니다.** 집는 것은 그 밑에 서는 단추가 합니다 — 집는 것은
    // 되돌릴 수 없고, 팩은 열려 있는 동안 무엇을 집을지 견주어 보는 자리입니다.
    node.on('pointertap', () => {
      if (this.game.input.ate()) return
      this.game.tray.pick('pack', index)
    })
    return { node, card }
  }

  /**
   * 팩에서 한 장을 집습니다.
   *
   * **누름과 갈라 두었습니다.** 카드를 누르는 것은 고르는 것이고, 집는 것은 그 밑에 선
   * 단추입니다 — 되돌릴 수 없는 일이 손이 미끄러진 한 번으로 일어나지 않습니다.
   */
  takeFromPack(index: number): void {
    const open = this.game.state.pack
    const view = this.packViews.get(index)
    if (!open || !view) return

    const item = view.item
    const node = view.face.node

    // **자리가 없으면 무엇과 바꿀지를 묻습니다.** 코어는 자리가 없으면 아무것도 하지
    // 않는데, 화면이 그것을 모른 채 소리와 조각을 내고 있었습니다.
    if (!this.game.tray.roomFor(item.kind)) {
      if (!this.game.tray.canSwap(item)) {
        // 내놓을 것도 없습니다. **왜 안 되는지는 적혀야 합니다.**
        this.game.audio.play('joker_fizzle')
        // **줄로 알립니다.** 머리글은 팩의 제목과 같은 자리라 둘이 겹칩니다.
        this.game.input.toasts.push(t('ui.swap.title'), this.fullNote(item.kind), UI.bad, 3)
        return
      }
      // **줄에서 고릅니다.** 펼친 카드는 막 뒤로 물러나 있고, 집은 것은 그 자리에서 옵니다.
      // 고른 뒤는 그냥 집는 것과 같은 길입니다 — 내놓는 것 하나만 앞에 놓입니다.
      this.game.tray.enterFocus(item, held => {
        this.game.session.giveUp(item, held)
        // **파는 것이 먼저 보입니다.** 새것은 `BUY_LINGER` 만큼 기다렸다 옵니다 — 붙들지
        // 않으면 내놓은 것이 타는 것과 새것이 오는 것이 한 프레임에 겹칩니다.
        this.game.tray.pickFromPack(index, item, node, { t: 'swap_pack', index, held },
          BUY_LINGER)
      })
      return
    }
    this.game.tray.pickFromPack(index, item, node, { t: 'pick_pack', index }, 0)
  }

  /**
   * 나오는 한 장이 반짝이는 것.
   *
   * **팩에서 나오는 그 순간의 한 장에만 붙습니다.** 카드가 자기 자리로 미끄러지는 것은
   * 이미 있었고, 없던 것은 「지금 이 한 장이 나왔다」입니다 — 다섯 장이 0.11초 간격으로
   * 나오므로 그 사이를 채우는 것이 없으면 다섯 장이 한꺼번에 놓인 것으로 읽힙니다.
   *
   * **켜지는 것도 꺼지는 것도 사인 한 마디입니다.** 1 에서 시작해 잦아드는 쪽은 켜지는
   * 순간이 계단이 되고, 그것은 부드럽게 반짝이는 것이 아니라 한 번 터지는 것입니다.
   * 조커가 자리에 닿을 때의 번쩍임이 그쪽이고, 그것은 「닿았다」라서 그렇습니다.
   *
   * 필터는 반짝이는 동안에만 붙입니다 — 필터 하나가 곧 렌더 텍스처 하나이고, 다 반짝인
   * 카드가 그것을 계속 들고 있을 이유가 없습니다.
   */
  private advancePackGlow(one: PackView, seconds: number): void {
    if (one.glow < 0 || one.glow >= 1) return

    one.glow = Math.min(1, one.glow + seconds / 0.46)
    const wave = Math.sin(one.glow * Math.PI)

    if (one.glow >= 1) {
      one.face.node.filters = []
      one.arrive = undefined
      return
    }

    if (!one.arrive) {
      one.arrive = new ArriveFilter()
      one.face.node.filters = [one.arrive]
    }
    one.arrive.at(this.game.clock)
    // 빛은 물결 그대로, 울렁임은 그 절반보다 작게. **둘이 같은 세기면 반짝이는 것이
    // 아니라 카드가 녹습니다.**
    one.arrive.flash = wave * 0.85
    one.arrive.warp = wave * 0.3
  }

  /**
   * 펼친 팩이 도는 것.
   *
   * 덮개가 짙어지고 · 카드가 자리로 미끄러지고 · 마우스를 올린 한 장이 올라오고 · 집어 간
   * 것이 물러납니다. 다 닫혔으면 판을 걷습니다.
   */
  advancePack(seconds: number): void {
    const open = this.game.state.pack !== null
    // 이름과 지시문과 단추가 드는 정도입니다. 한 프레임에 서면 툭 나타난 것으로 보입니다.
    this.packEnter += ((open ? 1 : 0) - this.packEnter) * fraction(seconds, 11)

    // 뜯어 놓고 상점이 내려가기를 기다리던 팩. 다 내려갔으면 지금 폅니다.
    if (this.packPending && open && this.game.shop.shopAway) this.syncPack()

    if (!this.packLayer.visible) return

    if (!open && this.packEnter < 0.03) {
      this.packLayer.removeChildren().forEach(child => child.destroy())
      this.packViews.clear()
      this.packGone.length = 0
      this.game.spotNodes.delete('packSkip')
      this.packNote = undefined
      this.packSkip = undefined
      this.packTitle = undefined
      this.packLayer.visible = false
      return
    }

    // 글과 버튼은 카드와 함께 들고 납니다. **자리를 비우는 동안은 한 단 물러납니다** —
    // 그동안 고르는 자리는 위의 줄이고, 이 판은 기다리는 것입니다.
    const shown = this.packEnter * (1 - 0.55 * this.game.tray.focusEnter)
    if (this.packNote) this.packNote.alpha = shown
    if (this.packSkip) {
      this.packSkip.alpha = shown
      this.packSkip.enabled = !this.game.tray.focus
    }
    if (this.packTitle) this.packTitle.alpha = shown

    for (const one of this.packViews.values()) {
      one.motion.advance(seconds)
      const node = one.face.node
      // **자리를 비우는 동안은 누르지 못합니다.** 고르는 자리가 위의 줄로 옮겨 갔습니다.
      node.eventMode = this.game.tray.focus ? 'none' : 'static'
      // 올라오는 것은 **고른 것 하나**입니다. 상점의 칸과 같은 규칙이고, 고른 카드는 손을
      // 떼어도 올라와 있어야 무엇을 집으려는 중인지가 남습니다.
      const up = this.game.tray.held?.kind === 'pack' && this.game.tray.held.uid === one.index
      // **용수철로 오르내립니다.** 고른 것인지로 자리를 바로 정하면 놓는 순간 카드가 툭
      // 내려앉습니다 — 줄의 조커와 상점의 칸이 같은 몸짓입니다.
      one.lift.target = up ? PACK_HOLD_RISE : 0
      one.lift.advance(seconds)
      const risen = one.lift.value / PACK_HOLD_RISE

      // **펼쳐 놓은 카드는 가만히 있지 않습니다.** 자리에 닿은 뒤로 아무것도 움직이지
      // 않으면 고르는 화면이 그림 한 장이 됩니다. 살짝 갸웃거리고 아주 조금 떠 있습니다 —
      // 눈에 띄면 그것은 이미 큰 것이라, 각도는 1.6도이고 높이는 2픽셀입니다.
      //
      // **올린 한 장은 잦아듭니다.** 들여다보는 중인 카드가 계속 흔들리면 읽기 어렵고,
      // 멈추는 것 자체가 「이것을 보고 있다」가 됩니다. 올라오는 만큼 잦아듭니다 — 켜고
      // 끄면 그 순간에 흔들림의 폭이 한 번 튑니다.
      const alive = 1 - 0.78 * risen
      const tilt = Math.sin(this.game.clock * 1.15 + one.sway) * 1.6 * alive
      const bob = Math.sin(this.game.clock * 0.83 + one.sway * 1.6) * 2 * alive

      one.motion.scale.target = up ? PACK_SCALE * 1.07 : PACK_SCALE
      node.position.set(one.motion.x.value, one.motion.y.value - one.lift.value + bob)
      node.rotation = (one.motion.rotation.value + tilt) * (Math.PI / 180)
      node.scale.set(one.motion.scale.value)
      node.zIndex = up ? 10 : 0
      // **자리가 차 있어도 옅게 두지 않습니다.** 소모품 칸이 찬 팩은 펼친 것이 전부 옅어지고,
      // 전부 옅으면 옅음이 무엇도 구별하지 않습니다 — 남는 것은 「무엇이 잘못되었다」
      // 하나이고, 자리가 없다는 것은 고른 다음에 서는 화면이 적습니다.
      this.advancePackGlow(one, seconds)
      // 닫히는 동안에는 카드도 함께 물러납니다. 자리를 비우는 동안은 한 단 옅습니다 —
      // 아직 나오지 않은 카드(0)는 그대로 둡니다.
      if (!open) node.alpha = this.packEnter
      else if (node.alpha > 0) node.alpha = 1 - 0.6 * this.game.tray.focusEnter
    }

    for (let i = this.packGone.length - 1; i >= 0; i--) {
      const gone = this.packGone[i]
      gone.life += seconds / 0.18
      gone.node.alpha = Math.max(0, 1 - gone.life)
      gone.node.scale.set(PACK_SCALE * (1 + gone.life * 0.3))
      if (gone.life < 1) continue
      this.packGone.splice(i, 1)
      gone.node.destroy({ children: true })
    }
  }
}
