import { Container, Sprite } from 'pixi.js'
import { EditionKind } from '../generated/enums/edition-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { snapshotHash } from '../core/hash'
import { strokeWidthOf } from '../ui/font'
import { newCounters } from '../core/state'
import { artBudget, artBytes, artDecodeHeight, artTallest, dropAllArt } from '../render/art'
import { cardFaceBakes } from '../render/card-face'
import { bannerBox } from '../ui/rule-banner'
import { CELL_H, CONSUMABLE_TRAY, HAND_Y, JOKER_TRAY } from './metrics'
import { type Game } from './game'
import { busy as netBusy } from '../net/session'
export class ProbePart {
  constructor(private readonly game: Game) {}

  /**
   * 버려진 그림을 가리키고 있는 스프라이트의 수.
   *
   * **`art.ts` 의 규약이 지켜지는지를 재는 값입니다.** 상한을 넘으면 오래된 것부터 놓고
   * 두 틱 뒤에 버리는데, 그 사이에 알림을 받은 쪽이 다시 그려 그 그림을 놓아야 합니다 —
   * 놓지 않으면 버려진 그림을 가리킨 채로 그리게 되고, 그 자리는 기계에 따라 빈 칸이
   * 되거나 그리기가 통째로 죽습니다.
   *
   * **눈으로는 갈리지 않습니다.** 데스크탑에서는 빈 칸조차 나오지 않았고 도감은 멀쩡해
   * 보였습니다. 수로 세지 않으면 알 길이 없는 자리입니다.
   *
   * 값을 읽는 순간에만 걷습니다 — `peek` 이 getter 입니다.
   */
  private deadArt(): [number, number] {
    let dead = 0
    let all = 0
    const walk = (node: Container): void => {
      if (node instanceof Sprite) {
        all++
        // **버려진 텍스처는 바탕이 `null` 입니다.** `destroyed` 만 보면 여기서 던지고, 그
        // 예외가 도구를 세워 정작 세려던 것을 세지 못합니다.
        const source = (node.texture as { source?: { destroyed: boolean } | null }).source
        if (!source || source.destroyed || node.texture.destroyed) dead++
      }
      for (const child of node.children) walk(child as Container)
    }
    walk(this.game.app.stage)
    return [dead, all]
  }

  peek(): unknown {
    const state = this.game.state
    return {
      // 어느 씬인가. **판을 접고 타이틀로 갔는지를 이것으로 봅니다.**
      scene: this.game.session.scene,
      // 지금 들고 있는 그림의 크기. **상한이 실제로 도는지를 이것으로 봅니다.**
      artBytes: artBytes(),
      // 이 기계의 상한과 푸는 세로 상한과 실제로 푼 것 가운데 가장 큰 세로. **셋을 함께 알려야**
      // 도구가 「상한 안인가」와 「화면 크기로 풀렸는가」를 상수 없이 봅니다.
      artBudget: artBudget(),
      artDecodeHeight: artDecodeHeight(),
      artTallest: artTallest(),
      // 버려진 그림을 가리키고 있는 스프라이트의 수와 전체 수. **앞엣것이 늘 0 이어야 합니다.**
      deadArt: this.deadArt(),
      // 배경음이 무엇을 어떻게 내고 있는가.
      music: this.game.audio.music.report(),
      // **소리가 안 나는 그 순간에 읽을 자리입니다.** 원인이 넷이고 서로 구별됩니다 —
      // 소리 길이 잠들었는가 · 마스터가 내려가 있는가 · 꺼져 있는가 · 겹침이 막는가.
      audio: this.game.audio.report(),
      // 지금까지 그린 프레임 수. 물러나면 더 늘지 않아야 합니다.
      drawn: this.game.drawn,
      // 지금까지 판 전체를 다시 세운 수. 그림 한 장마다 세우고 있는지를 이것으로 봅니다.
      refreshes: this.game.refreshes,
      // 설명 쪽지가 지금 떠 있는가. 꾸욱 누르기를 재는 도구가 씁니다.
      tip: this.game.input.tooltip.visible,
      // 지금 몇 장 골라 두었는가. 꾸욱 눌렀을 때 골라지지 않는지 확인합니다.
      picked: this.game.cards.selected.size,
      // 화면에 카드 뷰가 몇 장 살아 있는가. 접었으면 0 이어야 합니다.
      views: this.game.cards.views.size + this.game.cards.playedViews.length
        + this.game.cards.jokers.size,
      // 어느 통에 남아 있는가. **「카드가 남아 있다」만으로는 손패인지 낸 것인지 걷는
      // 중인 것인지 갈리지 않고**, 갈리지 않으면 어디를 고쳐야 할지 알 수 없습니다.
      bins: {
        hand: this.game.cards.views.size,
        played: this.game.cards.playedViews.length,
        fades: this.game.cards.fades.length,
        deals: this.game.cards.deals.length,
        recalls: this.game.cards.recalls.length,
        retired: this.game.cards.retired,
        shown: this.game.shown.hand.length,
      },
      seed: state.seed,
      // 무엇으로 시작한 판인가. **고른 것이 실제로 걸렸는지는 이 둘로만 확인됩니다** —
      // 화면에는 뒷면과 시작 조건으로만 나타나므로 그림으로는 갈리지 않습니다.
      deck: state.deckId,
      stake: state.stake,
      // 지금 상태의 해시. **이어서 한 판이 그만두던 판과 같은지는 이것으로만 갈립니다** —
      // 안테와 금액이 같아도 덱의 차례와 난수의 자리가 다르면 다른 판입니다.
      hash: snapshotHash(state),
      // 상점 판이 지금 떠 있는가. 하나 사는 동안 접히지 않는지 확인합니다.
      shopUp: this.game.shop.shopLayer.visible,
      shownPhase: this.game.shown.phase,
      skipping: this.game.blind.skipping,
      tagFly: this.game.blind.tagFly ? [Math.round(this.game.blind.tagFly.node.x),
        Math.round(this.game.blind.tagFly.node.y)] : null,
      blindBoard: this.game.blind.blindPick.visible
        ? `${this.game.blind.blindShown}:${this.game.blind.blindGroups.length}` : 'hidden',
      dealing: this.game.cards.deals.length > 0 || this.game.clock < this.game.cards.dealtUntil,
      deckX: Math.round(this.game.chrome.deckLayer.x),
      leaving: this.game.cards.leavingTiles.length,
      lingering: this.game.cards.leavingTiles.filter(one => this.game.clock < one.at).length,
      drawnItems: this.game.tray.consumableTiles.length,
      drawnJokers: this.game.cards.jokers.size,
      shopAt: [...this.game.shop.shopTiles.entries()].map(([slot, one]) =>
        [slot, Math.round(one.tile.x), Math.round(one.baseX), Math.round(one.mid),
          Math.round(one.baseY + CELL_H * one.tile.scale.x / 2)]),
      // 팩 칸의 가운데. 도구가 상수로 셈하던 것입니다.
      packAt: [...this.game.pack.packSlotTiles.entries()].map(([slot, one]) =>
        [slot, Math.round(one.mid), Math.round(one.baseY + one.height * one.tile.scale.x / 2)]),
      // 상점 판이 지금 떠 있는 높이. **0 이면 다 뜬 것이고, 클수록 아래에 있습니다.**
      // 뜨기 시작하는 프레임에 이 값이 0 이면 다 뜬 모습이 한 번 그려진 것입니다.
      shopY: Math.round(this.game.shop.shopLayer.y),
      // 상점 몸통이 판 안에서 얼마나 밀려 있는가. 딱지의 자리가 이만큼 어긋납니다.
      shopBodyY: Math.round(this.game.shop.shopFrame?.body.y ?? 0),
      // 떠오른 글이 뜬 자리들. 뒤가 새것입니다.
      pops: this.game.popLog.map(one =>
        [one.text, one.x, one.y, one.w, one.h] as [string, number, number, number, number]),
      // 상점이 자리를 비켜 내려가 있어야 하는가. 팩을 뜯었거나 산 것이 닿는 것을 보는 중입니다.
      shopParked: this.game.shop.shopParked,
      // 자리를 비우는 중인가 · 그 화면이 든 정도.
      focus: this.game.tray.focus !== undefined,
      focusEnter: Math.round(this.game.tray.focusEnter * 100) / 100,
      // 펼쳐 놓은 팩의 카드 수. **상점이 물러난 뒤에야 0 이 아닙니다.**
      packCards: this.game.pack.packViews.size,
      // 덱이 팩의 카드를 받으려고 나와 있는가.
      deckPeek: this.game.clock < this.game.cards.deckPeekUntil,
      // 지금 시드는 중인 카드와 딱지의 수. **보스가 건 것이 실제로 도는지의 값입니다.**
      withering: [...this.game.cards.views.values()].filter(view => view.blighted).length
        + [...this.game.cards.jokers.values()].filter(view => view.blighted).length,
      // 화면이 그린 박자들. 새것이 뒤입니다.
      beats: this.game.show.beatLog.slice(),
      // 능력을 빌리는 줄이 그어져 있는가.
      borrowLink: this.game.cards.borrowLink.visible,
      // 지금 뒷면이 보이는 조커 딱지의 수. 판이 갈리는 것이 뒤집기로 도는지의 값입니다.
      jokersBack: [...this.game.cards.jokers.values()].filter(view => view.facingBack).length,
      // **줄에 선 것들의 겹치는 차례입니다.** 가리킨 것과 고른 것이 위로 올라오는지를
      // 도구가 이 값으로 봅니다 — 겹침은 그림으로 판정할 수 없습니다.
      // **줄에 선 차례 그대로입니다.** 표에 담긴 차례로 내면 정렬한 뒤에 배열의 자리와
      // 화면의 자리가 어긋나, 도구가 「왼쪽부터 오르는가」를 물을 수 없습니다.
      stack: {
        hand: this.game.shown.hand
          .map(uid => this.game.cards.views.get(uid)?.zIndex)
          .filter((one): one is number => one !== undefined),
        joker: this.game.state.jokers
          .map(one => this.game.cards.jokers.get(one.uid)?.zIndex)
          .filter((one): one is number => one !== undefined),
        item: this.game.state.consumables
          .map(one => this.game.tray.consumableTiles.find(row => row.uid === one.uid)?.tile.zIndex)
          .filter((one): one is number => one !== undefined),
      },
      // 상점에 놓인 선물. 몇째 칸이고 누가 놓았고 값이 얼마인가.
      shopGift: ((): {
        slot: number; from: string; cost: number
        chip?: { x: number; y: number; width: number; height: number }
        price?: { x: number; y: number; width: number; height: number }
      } | undefined => {
        const at = this.game.state.shop.cards.findIndex(one => one.gift !== undefined)
        const one = at < 0 ? undefined : this.game.state.shop.cards[at]
        if (!one) return undefined
        const mark = this.game.shop.giftMark
        const box = (node: Container | undefined) => {
          if (!node || node.destroyed) return undefined
          const b = node.getBounds()
          return {
            x: Math.round(b.x), y: Math.round(b.y),
            width: Math.round(b.width), height: Math.round(b.height),
          }
        }
        return {
          slot: at, from: one.gift ?? '', cost: one.cost,
          chip: box(mark?.chip), price: box(mark?.price),
        }
      })(),
      // 판이 몇 번 섰는가. 도구가 「한 번도 서지 않았다」와 「서고 걷혔다」를 가릅니다.
      cardShows: this.game.cards.cardShowCount,
      // 규칙 알림 판이 차지한 사각형. 떠 있지 않으면 없습니다.
      ruleBanner: bannerBox(this.game.show.ruleBanner),
      // 그 판의 머리글. **없는 열쇠가 그대로 떠 있는지를 도구가 이 값으로 봅니다.**
      ruleBannerHead: this.game.show.ruleBanner.visible ? this.game.show.ruleBanner.head
        : undefined,
      // **판 위로 나와 바뀌는 중인 카드들.** 자리를 도구가 베껴 적지 않도록 화면이 알립니다.
      changeCards: (this.game.cards.cardShow?.cards ?? [])
        .filter(one => !one.view.destroyed)
        .map(one => ({
          uid: one.uid, kind: one.kind, borrowed: one.borrowed,
          // 지금 뒷면인가와 얼마나 좁아져 있는가. **뒤집기는 이 둘로만 재집니다.**
          back: one.view.facingBack,
          squeeze: Math.round(one.view.scale.x * 100),
          x: Math.round(one.view.x), y: Math.round(one.view.y),
          // 제자리로 가는 중인지 돌아가는 중인지. **도구가 그 둘을 가릅니다.**
          to: Math.round(one.view.motion.y.target),
        })),
      // 연출의 시계. 스크린샷 사이의 시간을 재는 데 씁니다.
      clock: this.game.clock,
      phase: state.phase, ante: state.ante, blind: state.blind,
      money: state.money, score: Number(state.score), target: Number(state.target),
      // **화면에 적힌 잔액입니다.** 위의 `money` 는 코어의 값이라 연출이 어디까지
      // 왜는지와 무관하게 항상 맞습니다 — 「잔액이 언제 바뀌는가」를 재려면 이쪽입니다.
      shownMoney: this.game.shown.money,
      jokers: state.jokers.length, discards: state.discardsLeft,
      hands: state.handsLeft,
      packOpen: state.pack !== null, packs: state.shop.packs.length,
      // 뽑을 패와 덱의 크기. **라운드 사이에는 둘이 같아야 합니다.**
      drawLeft: state.drawPile.length, deckSize: state.deck.length,
      // 칸 수 글이 강조되어 있는가. 산 것이 닿은 직후입니다.
      countPulse: this.game.chrome.countPulse !== undefined,
      // 팩 칸마다 어느 팩인가. 덱으로 가는 연출을 보려는 도구가 표준 팩을 짚어야 합니다.
      packIds: [...state.shop.packs],
      // 상점 칸마다 무엇이 놓여 있는가. **도구가 칸을 짚어야 합니다** — 소모품이 오는 길을
      // 재는 도구가 네 칸을 차례로 눌러 보고 있었고, 조커만 놓여 있는 상점에서는 아무것도
      // 사지 못한 채 「사지 못했습니다」 로 끝났습니다.
      shopKinds: state.shop.cards.map(card => card.kind),
      // 상점의 줄마다 몸통이 시작하는 `y`. **판이 바닥에 맞춰 서므로 도구가 상수로 셀 수
      // 없습니다** — 줄이 하나 없어지면 나머지 줄이 그만큼 내려옵니다.
      shopRows: this.game.shop.shopRows,
      // **들고 있는 태그와, 딱지에 실제로 그린 칩 수.** 둘이 갈라져야 어디가 틀렸는지
      // 나옵니다 — 상태에 없으면 규칙이고, 있는데 안 그렸으면 화면입니다.
      // 리더보드. **로그아웃 상태의 게임이 지금과 같은지**를 도구가 이것으로 봅니다.
      signedIn: this.game.session.hub.signedIn,
      // 판이 하나라도 떠 있는가. 리더보드와 로그인 판이 떴는지 확인합니다.
      modalUp: this.game.panels.modals.busy,
      // 맨 위 판이 화면에서 차지한 사각형. **판을 누르는 도구가 자리를 다시 세지 않습니다.**
      modalBox: this.game.panels.modals.box,
      // 통신이 지금 오가는가. 도는 동안 입력이 막힙니다.
      netBusy: netBusy(),
      // 랭크 런인가.
      ranked: this.game.session.hub.isRanked(state.seed),
      // 끝난 판이 떴는가. **제출은 그 판이 뜰 때 나갑니다** — 카드가 다 걷힌 뒤입니다.
      gameOver: this.game.session.gameOverShown,
      tags: state.tagsPending.slice(),
      tagChips: this.game.blind.badge.chipCount,
      // **칩이 실제로 어디에 그려졌는가.** 「잠깐 왼쪽 위로 튄다」는 프레임 몇 개짜리라
      // 눈으로는 어느 프레임에 어디였는지를 말할 수 없습니다.
      tagAt: this.game.blind.badge.chipSpots,
      played: this.game.cards.playedViews.length, coins: this.game.payout.coins.busy,
      // **머리글이 아니라 국면으로 봅니다.** 「넘겼습니다」 라는 글은 걷어냈습니다 —
      // 곧 정산 판이 서서 무엇을 얼마나 받는지가 적히므로, 그 글은 그 판이 할 말을 한 번
      // 미리 하는 것이었습니다.
      cleared: this.game.payout.payoutWanted || this.game.payout.payoutOpen,
      consumables: state.consumables.length,
      // **자리가 규칙입니다.** 득점은 낸 카드의 왼쪽부터이고 조커는 슬롯의 왼쪽부터이므로,
      // 자리를 바꾸는 것이 되는지는 이 두 줄로만 확인할 수 있습니다.
      //
      // 패는 **화면이 그리는 차례**를 알립니다. 코어의 차례를 알리면 끌어다 놓아도 화면은
      // 제자리로 돌아가는데 도구는 통과합니다 — 실제로 그런 결함이 있었습니다.
      // **버튼의 자리도 알립니다.** 도구가 같은 계산을 베껴 적으면 배치를 고칠 때 한쪽만
      // 고쳐지고, 그 도구는 엉뚱한 곳을 눌러 놓고 아무 말도 하지 않습니다.
      // **전환이 도는 동안에는 누를 자리를 알리지 않습니다.** 층이 눌림을 삼키므로 그
      // 자리는 눌리지 않고, 눌리지 않는 자리를 알리면 도구는 한 번 누르고 넘어갑니다.
      spots: this.game.transition.busy ? {} : {
        ...this.game.spots, ...this.lateSpots(), ...this.optionSpots(),
        ...this.handRowSpots(), ...this.runSpots(), ...this.game.panels.confirmSpots(),
        ...this.game.panels.collectionSpots(),
      },
      // 화면과 화면 사이. **어느 걸음에서 씬이 갈렸는지를 도구가 이것으로 봅니다.**
      transition: this.game.transition.peek(),
      // **도감이 지금 무엇을 몇 개 세우고 있는가.** 판이 떠 있을 때만 값이 있습니다 —
      // 갈래마다 칸의 수가 표의 행 수와 같은지를 도구가 이것으로 봅니다.
      collection: this.game.panels.modals.has(this.game.panels.collection)
        ? this.game.panels.collection.census : undefined,
      // 아무것도 없는 곳을 누른 횟수. **도구가 자기 좌표를 검사하는 자리입니다.**
      blankTaps: this.game.input.blankTaps,
      // **인사이트 판에 지금 무엇이 놓여 있는가.** 판이 떠 있을 때만 값이 있습니다.
      //
      // 갈래와 열쇠까지 알립니다 — 줄 수만 알리면 「몇 줄 있다」까지이고, 그것은 문장이
      // 열쇠 그대로 적혀 있어도 같은 답입니다. 열쇠를 알리면 도구가 그것을 시트와 견줄 수
      // 있고, **그 줄이 어느 국면에 나오지 않아야 하는지도 볼 수 있습니다.**
      insight: this.game.panels.modals.has(this.game.panels.handList)
        && this.game.panels.runInfoTab === 'insight'
        ? { keys: this.game.panels.insightRows().map(one => one.key) }
        : undefined,
      // 소리가 비는 자리를 찾을 때 씁니다 — 시계로 재면 배속과 히트스톱에서 어긋납니다.
      coming: this.game.player.coming ?? '',
      // 최근에 난 소리들. **「이 순간에 왜 이 소리가 나느냐」를 도구가 물을 자리입니다.**
      sounds: this.game.audio.played.slice(),
      payout: this.game.payout.payoutOpen,
      // **사서 오는 중인 소모품의 가로 자리.** 조커는 뷰가 용수철을 들고 있어 오는 길이
      // 있지만 소모품 칸은 매번 새로 만들어지므로, 오는 길이 있는지는 이 값이 프레임마다
      // 달라지는지로만 확인됩니다 — 눈으로는 0.5초짜리를 놓칩니다.
      //
      // **이 줄이 없어져 있었습니다.** `check-item-fly.ts` 가 이것을 읽는데 없으면 `-1` 을
      // 받고, 그 도구는 「사지 못했습니다」 로 끝나 무엇이 틀렸는지 말하지 않았습니다.
      // 사서 오는 중인 소모품 한 장. **함수가 아니라 값입니다** — 이 손잡이는 프레임마다
      // 다시 놓이므로 값이면 충분하고, 함수로 두면 도구가 부르는 그 순간의 것이라
      // 「없다」와 「부르지 못했다」가 같은 답으로 돌아옵니다.
      //
      // **가로만 재면 반만 봅니다.** 상점의 칸과 소모품 칸이 세로로는 멀고 가로로는 가까울
      // 수 있습니다.
      fly: this.game.tray.flyPeek(),
      // 소모품이 올 자리를 잡아 준 횟수와, 잡지 못한 횟수. **오는 길이 없을 때 그것이
      // 「부르지 않았다」인지 「불렀는데 잡을 것이 없었다」인지 갈립니다.**
      flyAsked: this.game.tray.flyAsked,
      flyMissed: this.game.tray.flyMissed,
      // **글에 두른 테두리의 굵기.** 굵기는 그 말의 획 사이 틈에서 나오는 값이라 말마다
      // 다르고, 한 번 만들고 글만 갈아 끼우는 것들은 말이 바뀔 때 여기서 다시 정합니다 —
      // 그 길을 지났는지는 눈으로 보이지 않습니다. 굵기 차이가 1픽셀 아래입니다.
      // **카드 앞면을 몇 장 굽고 몇 번 다시 썼는가.** 앞면은 「무늬 · 랭크 · 종이색 ·
      // 디버프」가 같으면 같은 그림이라 한 번만 굽습니다 — 다시 쓰는 쪽만 늘어야 맞고,
      // 구운 장수가 함께 늘면 열쇠에 매번 바뀌는 값이 섞인 것입니다.
      faceBakes: cardFaceBakes(),
      inkWidth: {
        hand: strokeWidthOf(this.game.chrome.handLabel),
        headline: strokeWidthOf(this.game.show.headline),
        button: this.game.chrome.menuButton.inkWidth,
      },
      // **판의 셰이더가 지금 몇 초를 보고 있는가.** 통마다 하나씩입니다.
      //
      // **눈으로도 그림으로도 잡히지 않습니다.** 무늬가 멈춘 것과 흐르는 것은 컷 한 장에서
      // 같아 보이고, 「누를 때마다 처음으로 돌아간다」는 컷 두 장을 나란히 놓아도 그 사이에
      // 무엇이 있었는지가 없습니다 — 값이 판의 시계를 따라가는지로 봅니다.
      editionAt: {
        tray: [...this.game.cards.jokers.values()].map(one => one.editionAt)
          .filter(one => one !== undefined),
        look: [
          ...[...this.game.shop.shopTiles.values()].map(one => one.look?.seen()),
          ...[...this.game.pack.packViews.values()].map(one => one.look?.seen()),
          ...this.game.tray.consumableTiles.map(one => one.look?.seen()),
          ...this.game.cards.gameOverJokers.map(one => one.view.editionAt),
        ].filter(one => one !== undefined),
      },
      // 조커와 소모품의 자리, 그리고 카드가 실제로 그려진 사각형들.
      //
      // **넘어가지 않는다는 것은 이 둘을 견주어야만 확인됩니다.** 눈으로는 몇 개까지
      // 담기는지 세어 볼 수 없고, 자리를 넘어간 한 장은 옆 줄이나 화면 밖에 놓입니다.
      trays: {
        joker: { ...JOKER_TRAY },
        item: { ...CONSUMABLE_TRAY },
      },
      // 고른 것 아래에 놓이는 단추 줄. **화면 밖으로 나가지 않는지 확인합니다.**
      heldBox: this.game.tray.heldBox ? { ...this.game.tray.heldBox } : undefined,
      trayCards: {
        joker: state.jokers.map((_, i) => this.game.tray.cardRect(this.game.tray.jokerSpot(i).x)),
        item: state.consumables.map((_,
          i) => this.game.tray.cardRect(this.game.tray.itemSpot(i).x)),
      },
      handOrder: this.game.shown.hand.slice(),
      jokerOrder: state.jokers.map(joker => joker.uid),
      // **판을 끝까지 두는 도구를 위한 손잡이입니다.** 사람이 보라고 넣은 뜸이 도구에게는
      // 기다림일 뿐이고, 그 기다림이 실행 시간의 대부분입니다. 옵션의 속도와 같은 값입니다.
      hurry: (times: number) => { this.game.player.base = times },
      // **프레임 상한을 재는 도구의 손잡이입니다.** 옵션을 거치지 않고 값만 갈아 끼웁니다.
      frameCap: (value: number) => { this.game.app.ticker.maxFPS = value },
      // **틱을 정해진 수만큼 돌립니다.** `?tick=manual` 로 열었을 때만 있습니다 — 하네스의
      // `pass` 가 이것이 있으면 틱을 돌리고 없으면 실제로 기다립니다.
      ...(this.game.manualTick ? { advance: (ms: number) => this.game.advanceManually(ms) } : {}),
      // **개발 서버에서만 있습니다.** 자리를 바꾸는 것이 되는지 보려면 조커가 둘 있어야
      // 하는데, 그것을 사려고 판을 열 판 두는 동안 확인하려던 것과 상관없는 곳에서 도구가
      // 멈춥니다. 구운 것에는 이 줄이 들어가지 않습니다.
      ...(import.meta.env.DEV ? {
        // 수를 주면 표의 앞에서 그만큼이고, **id 를 주면 그 조커 하나입니다** — 상점을
        // 나설 때 발동하는 것처럼 특정 조커가 있어야만 지나는 길을 보려면 지목해야 합니다.
        grantJoker: (want: number | string, edition = 0) => {
          const rows = this.game.data.tables.joker.records
          const picked = typeof want === 'string'
            ? rows.filter(row => row.jokerId === want) : rows.slice(0, want)
          for (const [at, row] of picked.entries()) {
            this.game.state.jokers.push({
              uid: this.game.state.nextUid++,
              jokerId: row.jokerId,
              // **판을 돌려 가며 겁니다.** 0 이면 전부 맨 것입니다 — 맨 것만으로는 판이
              // 걸린 딱지를 굽는 길을 한 번도 지나지 않습니다.
              edition: (edition === 0 ? 0 : 1 + (at + edition - 1) % 3) as never,
              sticker: 0 as never,
              counters: newCounters(),
              age: 0,
              disabled: false,
            })
          }
          this.game.refresh()
        },
        // **상점의 첫 팩 칸을 그 팩으로 바꿉니다.** 플레잉 카드가 덱으로 가는 연출은 표준
        // 팩에서만 보이는데, 어느 팩이 서는지는 시드가 정하므로 도구가 고를 수 없습니다.
        stockPack: (packId: string) => {
          if (this.game.state.phase !== 'shop' || this.game.state.shop.packs.length === 0) return
          this.game.state.shop.packs[0] = packId
          this.game.refresh()
        },
        /**
         * 상점의 첫 카드 칸을 플레잉 카드 하나로 바꿉니다.
         *
         * **그 칸은 `ShopAllowsPlayingCards` 를 켜는 것을 들고 있어야 나옵니다.** 무엇이
         * 그것을 켜는지도 언제 나오는지도 시드가 정하므로 도구가 고를 수 없고, 그러면 산
         * 카드가 덱으로 가는 길은 아무 도구도 지나지 않습니다. `stockPack` 과 같은 자리이고
         * 같은 까닭입니다.
         */
        stockPlayingCard: (cardId?: string) => {
          if (this.game.state.phase !== 'shop' || this.game.state.shop.cards.length === 0) return
          const rows = this.game.data.tables.baseDeckCard.records
          const row = (cardId === undefined ? undefined
            : this.game.data.tables.baseDeckCard.findByCardId(cardId)) ?? rows[0]
          this.game.state.shop.cards[0] = {
            kind: ShopItemKind.PlayingCard,
            id: row.cardId,
            cost: this.game.data.economy.playingCardCost,
            edition: EditionKind.Base,
          }
          this.game.refresh()
        },
        /**
         * 이번 안테의 보스를 지목합니다.
         *
         * **어느 보스가 오는지는 시드가 정합니다.** 보스가 거는 것을 확인하려면 그 보스
         * 하나를 세워야 하고, 판을 여러 판 두며 원하는 보스가 나오기를 기다리는 것은
         * 확인하려는 것과 무관한 일입니다. `stockPack` 과 같은 자리이고 같은 까닭입니다.
         */
        forceBoss: (bossId: string) => {
          if (!this.game.data.tables.bossBlind.findByBossId(bossId)) return
          this.game.state.bossId = bossId
          this.game.refresh()
        },
        // **태그 하나를 들고 있는 것으로 칩니다.** 태그는 블라인드를 건너뛰어야 들어오고,
        // 무엇이 들어오는지는 시드가 정하므로 도구가 고를 수 없습니다 — 돈을 내놓는 태그가
        // 그 값을 어디에 띄우는지를 보려면 그 태그 하나를 지목해야 합니다.
        grantTag: (tagId: string) => {
          this.game.state.tagsPending.push(tagId)
          this.game.refresh()
        },
        // **블라인드를 넘긴 것으로 칩니다.** 상점에서 도는 코드를 재려면 상점에 닿아야
        // 하는데, 도구의 자동 진행은 안테 1을 넘기지 못하고 집니다 — 그래서 「터진 것
        // 0건」 이 상점에 대해서는 아무 말도 아니었고, 실제로 상점에서 매 프레임 터지는
        // 것을 이 도구가 지나쳤습니다.
        clearBlind: () => {
          this.game.state.score = Number(this.game.state.target)
          this.game.shown.score = this.game.state.score
          this.game.act({ t: 'play', cards: this.game.state.hand.slice(0, 1) })
        },
        /**
         * 마지막 핸드 한 장을 내어 그 자리에서 집니다. 끝나는 순서를 보는 도구가 씁니다.
         *
         * **요구 점수를 닿지 못할 자리에 둡니다.** 한 장이면 진다고 여겼는데, 판이 걸린
         * 조커 셋이면 한 장으로도 1,462점이 나옵니다 — 지는 자리를 보려던 도구가 상점에
         * 머물렀고 그 도구는 「판이 뜨지 않았습니다」로만 끝났습니다.
         */
        loseRound: () => {
          this.game.state.handsLeft = 1
          this.game.state.target = this.game.state.score + 1_000_000
          this.game.act({ t: 'play', cards: this.game.state.hand.slice(0, 1) })
        },
        grantActive: () => {
          this.game.state.tagsPending = ['voucher', 'juggle']
          this.game.state.vouchers = this.game.data.tables.voucher.records.slice(0, 2)
            .map(row => row.voucherId)
          this.game.refresh()
        },
        /**
         * 흐림이 굽는 자리.
         *
         * **눈으로는 한 프레임짜리 어긋남을 잡을 수 없습니다.** 판이 열리고 닫힐 때 화면이
         * 한 번씩 옮겨 그려지던 결함이고, 원인은 흐림의 여백이 반지름에 따라 0 · 1 · 2 · 3
         * 으로 넘어가며 굽는 자리를 바꾼 것이었습니다 — 그 자리가 안 바뀐다는 것을 재는 쪽이
         * 확인할 수 있어야 합니다.
         */
        /**
         * 잘라 내는 자리와, 실제로 잘리고 있는가.
         *
         * **마스크가 걸려 있는지를 함께 알립니다.** 사각형만 알리면 그것이 옳은 자리에
         * 그려져 있어도 무대에 걸리지 않은 채일 수 있고, 그러면 판 밖으로 배경과 번쩍임과
         * 모달의 막이 그대로 새어 나갑니다 — 화면은 그것을 아무 말도 하지 않습니다.
         */
        cropRegion: () => ({
          box: this.game.cropRect
            ? [this.game.cropRect.x, this.game.cropRect.y, this.game.cropRect.width,
              this.game.cropRect.height]
            : undefined,
          masked: this.game.app.stage.mask === this.game.cropBox,
          // 배경이 덮은 자리. **판의 사각형과 같아야 합니다.**
          sheet: [this.game.show.sheet.x, this.game.show.sheet.y, this.game.show.sheet.width,
            this.game.show.sheet.height],
        }),
        /**
         * 칩과 배수의 파형이 지금 얼마나 요동치는가.
         *
         * **화면을 굽지 않고 확인하는 자리입니다.** 세기는 값이므로 여기서 확인하고, 모습은
         * `tools/shoot-wave.ts` 가 굽습니다.
         */
        scoreWave: () => {
          const now = this.game.chrome.scoreWave.surge
          // **자리도 함께 냅니다.** 그림을 오려 보는 도구가 좌표를 베껴 적으면 판의 자리를
          // 고친 그날 그 도구만 낡습니다. **판의 좌표이므로 도구가 `at` 으로 환산합니다** —
          // 판이 창의 가운데에 놓이고 남는 자리는 배경이 덮으므로 그 환산이 있어야 합니다.
          return {
            chips: Math.round(now.chips * 1000) / 1000,
            mult: Math.round(now.mult * 1000) / 1000,
            level: Math.round(now.level * 1000) / 1000,
            shown: this.game.chrome.scoreWave.view.visible,
            box: this.game.chrome.scoreWave.box,
            // **빠르기는 위상의 차이로 봅니다.** 그림으로는 확인되지 않습니다.
            phase: Math.round(now.phase * 1000) / 1000,
            // 칸마다 얼마나 나타나 있는가. 0 이면 그 상자에 아무것도 없습니다.
            live: [Math.round(now.live[0] * 1000) / 1000,
              Math.round(now.live[1] * 1000) / 1000],
            // **바탕의 번쩍임입니다.** 파형과 다른 층이고, 0 으로 되돌아가는 동안에는 둘 다
            // 0 이어야 합니다 — 그 대목에 두 상자가 파랑과 붉음으로 한 번 빛나고 있었습니다.
            lit: [Math.round(this.game.chrome.chips.lit * 1000) / 1000,
              Math.round(this.game.chrome.mult.lit * 1000) / 1000],
          }
        },
        blurRegion: () => ({
          padding: this.game.show.blur.padding,
          backPadding: this.game.show.blurBack.padding,
          strength: Math.round(this.game.show.blur.strength * 100) / 100,
          area: this.game.show.recede.filterArea
            ? [this.game.show.recede.filterArea.x, this.game.show.recede.filterArea.y,
               this.game.show.recede.filterArea.width, this.game.show.recede.filterArea.height]
            : undefined,
          filtered: ((this.game.show.recede.filters as unknown[] | null)?.length ?? 0) > 0,
          // 굽는 해상도와 화면의 해상도. **핸드폰에서 흐림이 뭉개지던 것을 재는 자리입니다.**
          density: this.game.show.blurDensity,
          rendered: this.game.app.renderer.resolution ?? 1,
          // 덮개의 짙기. **흐림과 같은 값으로 서고 같은 값으로 없어져야 합니다.**
          cover: Math.round(this.game.panels.modals.cover * 1000) / 1000,
        }),
        // 환희의 겹. **문턱을 넘은 판에서만 값이 있습니다.**
        euphoria: () => this.game.show.euphoria.peek(),
        // 소모품 첫 칸이 지금 그려진 자리. 사서 오는 길을 재는 도구가 씁니다.
        itemX: () => this.game.tray.consumableTiles[this.game.tray.consumableTiles.length - 1]?.tile.x,
        jokerX: () => {
          const first = this.game.state.jokers[0]
          return first ? this.game.cards.jokers.get(first.uid)?.x : undefined
        },
        // **돈이 없어서 못 사는 것과 자리가 없어서 못 넣는 것은 다른 일입니다.** 자리
        // 쪽을 보려면 돈은 걸림돌이 아니어야 합니다.
        // **소리는 조용히 실패합니다.** WebAudio 는 잘못된 값에 예외를 내는데 그것을 받는
        // 곳이 없어서, 웅얼거림이 안 나는 것과 예외로 죽은 것을 화면에서 가릴 수 없습니다.
        jokerVoice: (uid: number) => this.game.audio.jokerVoice(uid, 0),
        /**
         * 들고 있는 그림을 전부 놓습니다. **상한이 넘쳤을 때와 같은 길입니다.**
         *
         * 놓인 그림을 쓰고 있던 쪽이 `onArtReady` 로 다시 그리지 않으면 그 카드는 그대로
         * 빈 채로 남습니다 — 그 자리를 도구가 만들 수 있어야 합니다.
         */
        dropArt: () => dropAllArt().length,
        /**
         * GPU 에 올라와 있는 그림의 수와 몫.
         *
         * **전환이 화면 한 장을 남기고 가는지를 이것으로 봅니다.** 구운 사진은 렌더
         * 텍스처라 Pixi 의 그림 수거 대상이 아니고, 놓지 않으면 전환마다 그만큼 쌓이기만
         * 합니다 — 눈으로는 보이지 않고 오래 켜 둔 판에서만 드러납니다.
         */
        gpuTextures: () => {
          const kept = (this.game.app.renderer as unknown as {
            texture: { managedTextures: ({ pixelWidth: number; pixelHeight: number } | null)[] }
          }).texture.managedTextures
          let bytes = 0
          let count = 0
          for (const one of kept) {
            if (!one) continue
            count++
            bytes += one.pixelWidth * one.pixelHeight * 4
          }
          return { count, mb: Math.round(bytes / 1048576) }
        },
        /**
         * 지금 화면을 굽고 **빈 자리가 몇 픽셀인지**를 셉니다.
         *
         * 화면의 바탕이 잘라 낸 자리를 다 덮으므로 **성한 사진에는 빈 자리가 없습니다.**
         * 마스크가 어긋나 그려지지 않은 것이 있으면 그 자리의 알파가 0 이고, 지우는
         * 셰이더는 그것을 남는 색으로 칠합니다 — 눈으로는 「검은 구멍」입니다.
         */
        shotHoles: async () => {
          const texture = this.game.session.shoot()
          if (!texture) return { holes: -1, total: 0 }
          const got = await this.game.app.renderer.extract.pixels({ target: texture })
          let holes = 0
          for (let i = 3; i < got.pixels.length; i += 4) {
            if (got.pixels[i] === 0) holes++
          }
          texture.destroy(true)
          return { holes, total: got.pixels.length / 4 }
        },
        /** 구운 사진 한 장을 그대로. **눈으로 보는 자리입니다.** */
        shotDump: async () => {
          const texture = this.game.session.shoot()
          if (!texture) return ''
          const out = await this.game.app.renderer.extract.base64({ target: texture })
          texture.destroy(true)
          return out
        },
        /**
         * 전환 하나를 그냥 돌립니다. **씬은 그대로입니다.**
         *
         * 여덟 자리를 눈으로 보려면 저마다 그 자리까지 판을 두어야 하고, 이긴 판은 안테
         * 8입니다 — 그림을 찍는 도구가 확인하려는 것은 덮개의 모습이지 거기까지 가는 길이
         * 아닙니다. **갈아 끼우는 것이 없으므로 화면은 그 자리에 남습니다.**
         */
        cross: (id: string) => {
          this.game.transition.play(id, this.game.crossings.of(id), () => {})
        },
        /**
         * 그래픽 품질을 손으로 돌립니다. **옵션이 정하는 것을 도구가 뒤집는 자리입니다.**
         *
         * 재의 핸드폰 셰이더는 그 기계에서만 도는 길이라, 데스크탑에서 켜 보지 않으면 그
         * 모습을 아무도 보지 않은 채로 나갑니다. 「높음」의 파티클도 같은 자리에서 봅니다.
         */
        crossQuality: (level: 'high' | 'medium' | 'low') => {
          this.game.session.qualityOverride = level
          this.game.transition.quality = level
        },
        /** 재의 손잡이를 돌립니다. 고르는 동안 쓰는 자리입니다. */
        tuneAsh: (params: Record<string, number | [number, number]>) => {
          this.game.transition.tuneAsh(params)
        },
        grantMoney: (amount: number) => {
          this.game.state.money += amount
          this.game.chrome.money.reset(this.game.state.money)
          this.game.payout.settleShown()
          this.game.refresh()
        },
        /**
         * 환희의 겹을 그냥 켭니다.
         *
         * **문턱을 넘는 곱은 안티 3~4에서 나옵니다.** 그 판을 두는 동안 도구가 확인하려던
         * 것과 상관없는 곳에서 멈추므로, 곱만 건네고 겹이 그것을 어떻게 다루는지 확인합니다.
         * `release` 가 참이면 정산까지 갑니다.
         */
        forceEuphoria: (product: number, release = false) => {
          this.game.show.euphoria.consider(product)
          if (release) this.game.show.euphoria.release()
        },
        /**
         * 칩과 배수의 칸에 수를 그냥 넣습니다.
         *
         * **파형의 바닥이 배당을 따라가는 것을 보는 자리입니다.** 그 배당은 안티 5~6에서
         * 나오고, 거기까지 판을 굴리는 것은 그 연출을 보려는 도구가 할 일이 아닙니다.
         *
         * 칸에 넣을 뿐이므로 상태의 점수는 그대로입니다 — 다음 `refresh` 가 칸을 다시
         * 맞추므로 이 값은 그 사이에만 있습니다.
         */
        forceScore: (chips: number, mult: number) => {
          this.game.chrome.chips.target = chips
          this.game.chrome.mult.target = mult
        },
        /**
         * 파형의 위상을 이 값에 세우고 붙잡습니다.
         *
         * **흐르는 쪽을 확인하는 도구가 쓰는 자리입니다.** 그림 한 장을 굽는 데 1초쯤 들어서
         * 컷 사이의 위상 차이를 시간으로는 정할 수 없습니다 — 잡아 두고 값을 손으로 옮기면
         * 두 컷의 차이가 정확히 그만큼입니다. **한 번 잡으면 이 판에서는 놓지 않습니다.**
         */
        holdWave: (phase: number) => this.game.chrome.scoreWave.hold(phase),
        /**
         * 소모품 하나를 지목해 놓습니다.
         *
         * **어느 소모품이 오는지는 시드가 정합니다.** 유령 카드 하나가 하는 일을 보려면
         * 그것을 손에 들어야 하고, 나오기를 기다리며 판을 여러 판 두는 것은 확인하려는
         * 것과 무관합니다. `grantJoker` 에 이름을 넘기는 것과 같은 자리입니다.
         */
        grantConsumableId: (id: string) => {
          const kind = this.game.data.tables.tarot.findByTarotId(id) ? 1
            : this.game.data.tables.planet.findByPlanetId(id) ? 2
              : this.game.data.tables.spectral.findBySpectralId(id) ? 3 : 0
          if (kind === 0) return
          this.game.state.consumables.push({
            uid: this.game.state.nextUid++, kind: kind as never, id, edition: 0 as never,
          })
          this.game.refresh()
        },
        grantConsumable: (count: number, edition = 0) => {
          const rows = this.game.data.tables.tarot.records
          for (let i = 0; i < count && i < rows.length; i++) {
            this.game.state.consumables.push({
              uid: this.game.state.nextUid++,
              kind: 1 as never,
              id: rows[i].tarotId,
              // 판을 돌려 가며 겁니다. `grantJoker` 와 같은 규칙입니다.
              edition: (edition === 0 ? 0 : 1 + (i + edition - 1) % 4) as never,
            })
          }
          this.game.refresh()
        },
      } : {}),
      // **깔리는 중도 바쁜 것입니다.** 카드가 뒷면으로 붙고 뒤집히기까지는 고를 수 없으므로,
      // 도구가 이 값을 보고 기다리는 것이 맞습니다 — 박자는 첫 장이 나올 때 이미 끝났습니다.
      busy: this.game.player.busy || !this.game.chrome.score.settled
        || this.game.payout.coins.busy
        || this.game.cards.deals.length > 0 || this.game.clock < this.game.cards.dealtUntil,
      /**
       * 손패의 걸쇠와 그것이 거짓인 까닫.
       *
       * **보이지 않는 채로 죽는 자리였습니다.** 카드가 눌리지 않고 소리도 나지 않는데
       * 화면에는 아무것도 적히지 않아서, 어느 조건이 거짓인지 코드에서 눈으로 찾아야
       * 했습니다 — 그 조건이 여섯입니다.
       */
      handLive: this.game.handLive,
      lastPointer: this.game.input.lastPointer ?? null,
      errors: this.game.errors.slice(),
      /** 소리 길의 세 지점에 삐 소리를 냅니다 — `'out'` · `'master'` · `'music'`. */
      beep: (where?: 'out' | 'master' | 'music') => this.game.audio.beep(where),
      /** 출력 장치를 다시 잡습니다. **스트림은 건강한데 소리가 안 나는 자리를 고칩니다.** */
      rebind: () => this.game.audio.rebind(),
      /**
       * 손패의 자리를 하나씩 짚어 **그 자리에서 무엇이 잡히는가**를 돌려줍니다.
       *
       * **사람이 조준할 필요가 없습니다.** 「카드를 눌렀는데 안 된다」를 확인하려면 카드가
       * 정확히 어디에 있는지 알아야 하는데, 그것을 아는 것은 화면입니다 — 화면이 자기
       * 자리를 짚어 보고 잡히는 것의 이름을 적습니다. `CardView` 가 아니면 그 이름이
       * 무엇을 덮고 있는지입니다.
       */
      probeHand: () => {
        const found = this.game.app.renderer.events?.rootBoundary
        const row = this.game.cards.handSpots
        return this.game.shown.hand.map((uid, index) => {
          const view = this.game.cards.views.get(uid)
          const world = { x: row.startX + index * row.spacing, y: HAND_Y }
          const at = this.game.world.toGlobal(world)
          let hit = 'no-boundary'
          if (found) {
            const target = found.hitTest(at.x, at.y) as
              { constructor: { name: string } } | null
            hit = target === null ? 'none'
              : target === this.game.app.stage ? 'stage' : target.constructor.name
          }
          return {
            uid, 자리: [Math.round(world.x), Math.round(world.y)],
            화면: [Math.round(at.x), Math.round(at.y)],
            뷰: view
              ? `${view.constructor.name} eventMode=${String(view.eventMode)} z=${view.zIndex}`
              + ` 보임=${view.visible} 알파=${Math.round(view.alpha * 100) / 100}` : '없음',
            잡힌것: hit,
          }
        })
      },
      // 캔버스 위에 DOM 이 덮여 있는가. 가운데 점에서 맨 위에 있는 원소입니다.
      topAtCenter: (() => {
        const el = document.elementFromPoint(window.innerWidth / 2, window.innerHeight * 0.76)
        return el ? `${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}` : 'none'
      })(),
      handWhy: [
        state.phase !== 'round' ? 'phase:' + state.phase : '',
        this.game.shown.phase !== 'round' ? 'shown:' + this.game.shown.phase : '',
        this.game.player.busy ? 'timeline' : '',
        this.game.panels.modals.busy ? 'modal' : '',
        this.game.cards.playedViews.length > 0 ? 'played:' + this.game.cards.playedViews.length
          : '',
        this.game.cards.fades.length > 0 ? 'fades:' + this.game.cards.fades.length : '',
        this.game.cards.deals.length > 0 ? 'deals:' + this.game.cards.deals.length : '',
        this.game.clock < this.game.cards.dealtUntil ? 'dealing' : '',
      ].filter(one => one !== '').join(' '),
      // **화면이 주장하는 패입니다.** 도구가 눌러야 하는 것은 지금 그려져 있는 카드입니다.
      hand: this.game.shown.hand.map(uid => {
        const card = state.deck.find(entry => entry.uid === uid)
        return { rank: card?.rank ?? 0, suit: card?.suit ?? 0 }
      }),
    }
  }

  /** 그 자리를 판 위의 자리로 옮깁니다. 왼쪽 판 안의 것들은 자기 판 기준입니다. */
  spotOf(node: Container, dx = 0, dy = 0): { x: number; y: number } {
    return this.game.overlay.toLocal(node.toGlobal({ x: dx, y: dy }))
  }

  /**
   * 이것이 지금 화면에 붙어 있는가.
   *
   * **어버이가 있는 것만으로는 모자랍니다.** 판이 닫히면 그 판의 통 하나가 무대에서
   * 떼어지고 그 안의 단추들은 그대로 남으므로, 어버이가 있는지만 보면 닫힌 판의 단추 자리가
   * 계속 알려집니다 — 그 자리는 판이 사라지던 그 프레임의 자리이고, 도구는 화면 가운데의
   * 빈 곳을 눌러 놓고 눌렀다고 봅니다.
   */
  private onStage(node: Container): boolean {
    for (let at: Container | null = node; at !== null; at = at.parent) {
      if (at === this.game.world) return true
    }
    return false
  }

  /**
   * 나중에 세는 자리들이 지금 어디에 있는가.
   *
   * **화면에 붙어 있는 것만 셉니다.** 판이 닫히면 그 판은 무대에서 떼어지므로, 떼어진 것의
   * 자리를 알리면 도구가 아무것도 없는 곳을 누르고도 눌렀다고 봅니다.
   */
  private lateSpots(): Record<string, { x: number; y: number }> {
    const out: Record<string, { x: number; y: number }> = {}
    for (const [key, one] of this.game.spotNodes) {
      if (one.node.destroyed || !this.onStage(one.node)) continue
      out[key] = this.spotOf(one.node, one.cx, one.cy)
    }
    // 타이틀의 단추들. **그 화면이 보일 때만입니다.**
    if (this.game.session.title.visible) {
      for (const [key, one] of this.game.session.title.toolSpots) {
        if (one.node.destroyed) continue
        out[`title:${key}`] = this.spotOf(one.node, one.cx, one.cy)
      }
    }
    return out
  }

  /**
   * 족보 목록의 줄들이 지금 어디에 있는가.
   *
   * **판이 떠 있고 그 갈래일 때만 값이 있습니다.** 줄의 자리는 판의 높이와 들어오는 중의
   * 배율을 따르므로 도구가 셈할 수 없습니다 — 상수를 베껴 적어 둔 도구가 있었고, 판이
   * 자라고 단추가 옮겨진 뒤로 그 도구는 판을 열지도 못한 채 빈 화면을 찍고 있었습니다.
   */
  private handRowSpots(): Record<string, { x: number; y: number }> {
    if (!this.game.panels.modals.has(this.game.panels.handList)
        || this.game.panels.runInfoTab !== 'hands') return {}
    const out: Record<string, { x: number; y: number }> = {}
    const width = this.game.panels.handList.size.width
    this.game.panels.handRows.forEach((row, index) => {
      out[`handRow:${index}`] =
        this.spotOf(this.game.panels.handList.view, width / 2, row.y + row.height / 2 - 4)
    })
    return out
  }

  /**
   * 판을 여는 자리의 탭과 단추들이 지금 어디에 있는가.
   *
   * **판이 떠 있을 때만 값이 있습니다.** 탭은 몇 개가 서는지가 저장된 판과 챌린지의
   * 해금에 따라 달라지므로, 도구가 그 셈을 베껴 적으면 이어할 것이 있는 날과 없는 날에
   * 다른 곳을 누릅니다.
   */
  private runSpots(): Record<string, { x: number; y: number }> {
    if (!this.game.panels.modals.has(this.game.panels.runPanel)) return {}
    const out: Record<string, { x: number; y: number }> = {}
    for (const [key, one] of this.game.panels.runPanel.toolSpots) {
      if (one.node.destroyed) continue
      out[`run:${key}`] = this.spotOf(one.node, one.cx, one.cy)
    }
    return out
  }

  /**
   * 옵션 판 안의 칸들이 지금 어디에 있는가.
   *
   * **판이 떠 있을 때만 값이 있습니다.** 자리는 탭과 글 길이와 굴린 만큼에 따라 달라지므로
   * 도구가 셈할 수 없고, 셈하려 든 도구는 좌표를 못박아 두고 빈자리를 눌러 놓고 통과했습니다 —
   * 말을 바꾸면 화면이 멈추는 결함이 그 사이로 지나갔습니다.
   */
  private optionSpots(): Record<string, { x: number; y: number }> {
    if (!this.game.panels.modals.has(this.game.panels.optionsPanel)) return {}
    const out: Record<string, { x: number; y: number }> = {}
    for (const [key, one] of this.game.panels.optionsPanel.toolSpots) {
      // 그리는 사이의 한 프레임에는 이미 지워진 칸이 남아 있을 수 있습니다.
      if (one.node.destroyed) continue
      out[`option:${key}`] = this.spotOf(one.node, one.cx, one.cy)
    }
    return out
  }
}
