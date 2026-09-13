import { Container, Graphics, Rectangle, Text } from 'pixi.js'
import { BlindKind } from '../generated/enums/blind-kind'
import { describe } from '../core/describe'
import { rewardOf, tagFor, targetOf } from '../core/run'
import { nameOf, t, tf } from '../core/strings'
import { piece } from '../ui/chrome'
import { BlindBadge } from '../render/hud'
import { Motion } from '../render/motion'
import { artFor } from '../render/art'
import { blindFace, packInk, packName, shopLabel, tagFace } from '../render/faces'
import { mix, plateTint, wellTint } from '../render/skin'
import { popupLeft, SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { Button } from '../ui/widgets'
import { PANEL_BOTTOM } from '../ui/modal'
import { richBlock, richLeading, richStyle } from '../ui/rich'
import {
  BLIND_ENTER_GAP, BLIND_ENTER_TIME, BLIND_ENTER_TOTAL, BLIND_RISE, BOARD_X, PANEL_W,
  PLAY_Y, TAG_FIRE, TAG_FIRE_WAIT, TAG_FLASH, TAG_POP,
} from './metrics'
import { blindName } from './tables'
import { glare, NEWLINE } from './helpers'
import { type BlindGroup, type TagCell } from './types'
import { type Game } from './game'
/** 고르기 판의 머리 판 높이. 이름(24)이 앉는 띠입니다. */
const BAND_H = 40

/** 고르기 판 아래에 쌓이는 것들의 사이. 단추의 턱과 그림자가 들어갑니다. */
const STACK_GAP = 12

export class BlindPart {
  constructor(private readonly game: Game) {}

  readonly badge = new BlindBadge(PANEL_W)

  /**
   * 새로 선 태그가 번쩍이는 것. 태그 하나에 0에서 1로 갑니다.
   *
   * **칩이 아니라 태그 이름으로 셉니다.** 칩은 화면을 다시 그릴 때마다 새로 만들어지므로
   * 그것에 붙여 두면 다음 프레임에 없어집니다 — 번쩍이는 것은 그 태그이지 그 통이
   * 아닙니다.
   */
  /**
   * 번쩍이는 것은 **마지막에 받은 하나뿐입니다.**
   *
   * 표로 두었더니 둘을 받았을 때 둘 다 번쩍였습니다 — 번쩍임은 「새로 생겼다」는 말이고,
   * 이미 떠 있던 것이 함께 번쩍이면 그 말이 둘을 가리키게 됩니다.
   */
  tagFlashId = ''

  tagFlashLife = 1

  /**
   * 건너뛰기 연출.
   *
   * **누른 그 자리에서 시작합니다.** 건너뛰기 한 번에 네 가지가 한 프레임에 겹쳤습니다 —
   * 머리띠 끝의 칩이 번쩍이고, 화면 구석에 토스트가 뜨고, 그 자리에서 도는 태그는 동전이나
   * 팩을 내고, 동시에 블라인드 판이 다음으로 넘어갔습니다. 눈은 판 가운데에 있는데 알림은
   * 구석 둘에 있었습니다. 지금은 카드에 적혀 있던 태그 칩이 커져서 머리띠로 날아가 앉고,
   * 앉은 뒤에 발동하고, 그것이 끝난 뒤에 판이 넘어갑니다. **그동안 판은 그대로 떠 있습니다**
   * — `refresh` 가 블라인드 판과 팩을 다시 세우지 않습니다.
   */
  skipping = false

  /** 누른 카드에 적힌 태그 얼굴의 자리. 액션이 판을 바꾸기 전에 적어 둡니다. */
  skipFrom?: { x: number; y: number }

  tagFly?: {
    node: Container; motion: Motion; tagId: string; at: number; sent: boolean
    to: { x: number; y: number }
  }

  /** 날아간 칩이 앉은 자리. 그 태그가 내는 동전이 여기서 나옵니다. */
  tagLanded?: { x: number; y: number }

  /**
   * 지금 발동하는 태그들. 태그 하나에 `-지연`에서 1로 갑니다.
   *
   * **쓰였다는 것은 사라지는 것이 아니라 켜지는 것입니다.** 쓰인 태그를 곧바로 흐리게
   * 두었더니 「무엇이 없어졌다」로 보였습니다 — 그 태그가 한 일은 그 순간에 발동한
   * 것이고, 발동은 켜졌다가 잦아드는 것입니다. 흐려지는 것은 그 뒤에 남는 상태입니다.
   *
   * **음수에서 시작합니다.** 그 자리에서 쓰이는 태그는 받는 순간과 쓰이는 순간이 같아서,
   * 나오는 번쩍임과 발동하는 번쩍임이 한 프레임에 겹칩니다 — 한 박자 뒤에 켜야 둘이
   * 갈립니다.
   */
  readonly tagFire = new Map<string, number>()

  /**
   * 이번 안테에 이미 쓰인 태그들. 받은 순서입니다.
   *
   * **쓰였다고 지우지 않습니다.** 태그 24종 중 14종은 건너뛰는 그 순간에 쓰이고 목록에서
   * 빠지므로, 들고 있는 것만 그리면 그 열넷은 화면에 한 프레임도 서지 못합니다 — 무엇을
   * 받았는지가 남지 않는 것이고, 그러면 건너뛴 대가가 없었던 것으로 보입니다.
   *
   * **흐리게 남깁니다.** 아직 들고 있는 것과 이미 쓴 것은 다음에 할 일이 다르므로 같은
   * 밝기로 남아 있으면 안 됩니다.
   *
   * 안테가 바뀌면 비웁니다 — 그 안테에 무엇을 받았는가가 이 줄이 답하는 것이고, 판이
   * 끝날 때까지 쌓으면 띠가 그것만으로 찹니다.
   */
  tagSpent: string[] = []

  /** 이 줄이 어느 안테의 것인가. 바뀌면 비웁니다. */
  tagSpentAnte = 0

  /** 지금 머리띠에 달린 칩들. `tagChips` 가 채우고 `advanceTagFlash` 가 만집니다. */
  private tagCells: TagCell[] = []

  /**
   * 블라인드 셋을 한 자리에 세운 판.
   *
   * **셋을 함께 보여야 건너뛸지를 판단할 수 있습니다.** 지금 것 하나만 보여 주면 다음이
   * 무엇인지 모르는 채로 넘길지를 정하게 되고, 그건 선택이 아니라 찍기입니다.
   */
  readonly blindPick = new Container()

  /**
   * 블라인드 판이 들어오는 정도. 0 에서 1 로 갑니다.
   *
   * **떡하니 떠 있으면 밋밋합니다.** 셋이 왼쪽부터 차례로 아래에서 올라와야 「고르는 자리에
   * 왔다」가 됩니다.
   */
  blindEnter = 0

  /** 지금 그려진 판이 어느 블라인드의 것인가. 바뀌면 다시 들어옵니다. */
  blindShown = -1

  /** 블라인드 고르기 판의 카드 셋. 들어오는 동안 자리만 옮깁니다. */
  blindGroups: BlindGroup[] = []

  /**
   * 건너뛰어 받은 태그 칩을 띄웁니다.
   *
   * 카드에 적혀 있던 자리에서 커지고(`TAG_POP`), 머리띠에 이미 놓여 있는 그 칩의 자리로
   * 날아갑니다. **머리띠의 칩은 그동안 비어 있습니다** — 둘이 같이 보이면 태그가 둘입니다.
   */
  launchTag(tagId: string): void {
    const from = this.skipFrom ?? { x: BOARD_X, y: PLAY_Y }
    const node = new Container()
    node.addChild(tagFace(tagId, 40))
    node.position.set(from.x, from.y)
    node.zIndex = 9_000
    this.game.overlay.addChild(node)

    const motion = new Motion()
    motion.snap(from.x, from.y)
    motion.scale.snap(1)
    motion.scale.target = 1.3

    // 앉을 자리. 머리띠는 넷까지만 보이므로, 밀려나 자리가 없으면 머리띠의 가운데로 갑니다.
    const cell = this.tagCells.find(one => one.tagId === tagId && !one.cell.destroyed)
    const to = cell
      ? this.game.overlay.toLocal(cell.cell.getGlobalPosition())
      : { x: SIZE.width / 2, y: 40 }

    this.tagFly?.node.destroy()
    this.tagFly = { node, motion, tagId, at: this.game.clock, sent: false, to }
    this.game.audio.play('joker_add', 4)
  }

  /** 날아가는 태그 칩. 앉으면 그 자리의 칩이 번쩍입니다. */
  advanceTagFly(seconds: number): void {
    const fly = this.tagFly
    if (!fly) return
    const age = this.game.clock - fly.at
    if (!fly.sent && age >= TAG_POP) {
      fly.sent = true
      // **날아오는 것은 전부 같은 용수철입니다**(`Motion.drift`). 조커·소모품·덱으로 가는
      // 카드가 그것이고, 태그만 `hard` 로 날아가 셋 중 하나가 다른 빠르기였습니다.
      fly.motion.drift()
      fly.motion.to(fly.to.x, fly.to.y, 0)
      fly.motion.scale.target = 26 / 40
    }
    fly.motion.advance(seconds)
    fly.node.position.set(fly.motion.x.value, fly.motion.y.value)
    fly.node.scale.set(fly.motion.scale.value)
    for (const one of this.tagCells) {
      if (one.tagId === fly.tagId && !one.cell.destroyed) one.cell.alpha = 0
    }

    const landed = fly.sent && fly.motion.x.settled && fly.motion.y.settled
    if (!landed && age < 1.6) return
    fly.node.destroy()
    this.tagFly = undefined
    this.tagLanded = fly.to
    // **앉은 칩이 하얗게 한 번 번쩍입니다.** 칩을 만드는 쪽이 이 값을 읽어 붙이므로 한 번
    // 다시 세웁니다. 받자마자 쓰이는 태그면 번쩍임이 잦아든 뒤에 켜집니다.
    this.tagFlashId = fly.tagId
    this.tagFlashLife = 0
    if (this.tagSpent.includes(fly.tagId)) this.tagFire.set(fly.tagId, -TAG_FIRE_WAIT * 0.4)
    this.game.audio.play('joker_add', 4)
    this.game.refresh()
  }

  /**
   * 새로 선 태그가 번쩍이는 것.
   *
   * **매 프레임 딱지를 다시 그립니다.** 칩은 통이 매번 새로 만들어지므로 셰이더를 그 통에
   * 붙여 두고 잦아들게 할 수가 없습니다 — 세기를 여기서 세고, 칩을 만드는 쪽이 그 값을
   * 읽어 붙입니다.
   */
  advanceTagFlash(seconds: number): void {
    if (this.tagFlashLife >= 1 && this.tagFire.size === 0) return

    if (this.tagFlashLife < 1) {
      this.tagFlashLife = Math.min(1, this.tagFlashLife + seconds / TAG_FLASH)
    }
    for (const [tagId, life] of this.tagFire) {
      const next = life + seconds / TAG_FIRE
      if (next >= 1) this.tagFire.delete(tagId)
      else this.tagFire.set(tagId, next)
    }
    // **칩은 그대로 두고 밝기·크기·필터만 만집니다.** 전에는 이 1초 동안 매 프레임 칩 전부를
    // 버리고 다시 만들었고, 발동하는 칩마다 필터를 새로 걸었습니다.
    for (const one of this.tagCells) {
      if (one.cell.destroyed) continue
      const fire = this.tagFire.get(one.tagId)
      if (fire !== undefined && fire >= 0) {
        const wave = Math.sin(fire * Math.PI)
        one.cell.alpha = 0.42 + wave * 0.58
        one.cell.scale.set(1 + wave * 0.3)
        if (!one.lit) {
          one.lit = glare(one.size, 1)
          one.cell.addChild(one.lit)
        }
        one.lit.alpha = wave * 0.9
      } else if (one.lit) {
        one.lit.destroy()
        one.lit = undefined
        one.cell.alpha = one.used ? 0.42 : 1
        one.cell.scale.set(1)
      }
      if (one.shine) {
        const life = one.tagId === this.tagFlashId ? this.tagFlashLife : 1
        if (life < 1) {
          one.shine.alpha = Math.sin(life * Math.PI) * 0.8
        } else {
          one.shine.destroy()
          one.shine = undefined
        }
      }
    }
  }

  /**
   * 블라인드 딱지의 가운데.
   *
   * **딱지의 자리는 그것의 왼쪽 위입니다.** 그래서 딱지를 그대로 넘기면 거기서 나오는
   * 것이 화면의 왼쪽 위 구석에서 납니다 — 덱 · 바우처 · 보스 · 태그가 낸 값과 돈이 전부
   * 이 딱지에서 나오므로, 그 한 자리를 여기서 셉니다.
   */
  badgeMiddle(): { x: number; y: number } {
    const bounds = this.badge.getLocalBounds()
    return {
      x: this.badge.x + bounds.x + bounds.width / 2,
      y: this.badge.y + bounds.y + bounds.height / 2,
    }
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
  /**
   * 카드 하나를 들어오는 정도에 맞춰 놓습니다.
   *
   * **떠 있는 판들과 같은 거리로 올라옵니다** — 같은 58픽셀입니다. 다만 셋이 함께 있는
   * 화면이므로 50ms 간격을 두고 차례로 서며, 마지막 칸까지 0.66초에 정확히 끝납니다.
   * 도구가 누르는 자리도 카드를 따라갑니다.
   *
   * **적어 둔 것과 코드가 어긋나 있었습니다.** 170픽셀을 감쇠 7로 올리고 있었고, 그것은
   * 떠 있는 판의 세 배 거리를 더 느린 곡선으로 지나는 것입니다 — 고를 것이 다 설 때까지
   * 기다리는 자리가 되었습니다.
   */
  placeBlindGroup(entry: BlindGroup): void {
    // **셋을 한꺼번에 밀지 않습니다.** 왼쪽에서 오른쪽으로 50ms씩 따라오게 해야
    // 스몰→빅→보스의 순서가 보이고, 세 판이 한 덩어리로 튀는 느낌도 사라집니다.
    const elapsed = this.blindEnter * BLIND_ENTER_TOTAL
    const raw = Math.max(0, Math.min(1,
      (elapsed - entry.index * BLIND_ENTER_GAP) / BLIND_ENTER_TIME))
    // 첫 움직임은 또렷하고 끝은 부드럽게 붙되, 감쇠처럼 꼬리가 남지는 않습니다.
    const enter = 1 - (1 - raw) ** 3
    entry.group.position.set(entry.x, entry.bottom - entry.height + (1 - enter) * BLIND_RISE)
    entry.group.alpha = (entry.now ? 1 : entry.done ? 0.5 : 0.72) * Math.min(1, enter * 1.6)
    if (entry.skipY !== undefined) {
      this.game.spots.skip = { x: entry.x + entry.width / 2, y: entry.group.y + entry.skipY }
    }
    if (entry.pickY !== undefined) {
      this.game.spots.pick = { x: entry.x + entry.width / 2, y: entry.group.y + entry.pickY }
    }
  }

  /** 블라인드 판이 떠 있어야 하는가. 고르는 국면이고, 연출이 다 끝났고, 건너뛰는 중이 아닐 때입니다. */
  get blindWanted(): boolean {
    return this.game.state.phase === 'blind-select' && this.game.presented && !this.skipping
  }

  drawBlindPick(): void {
    this.blindPick.removeChildren().forEach(child => child.destroy())
    this.blindGroups = []
    delete this.game.spots.pick
    delete this.game.spots.skip
    const state = this.game.state
    this.blindPick.visible = this.blindWanted
    if (!this.blindPick.visible) return

    // 블라인드가 바뀌면 처음부터 다시 들어옵니다.
    if (this.blindShown !== state.blind) {
      this.blindShown = state.blind
      this.blindEnter = 0
      // **보스 차례가 되면 그것이 들립니다.** 판 셋 중 붉은 것 하나가 앞으로 나오는 것을
      // 눈으로만 알리면, 안테의 마지막이라는 것이 지나가 버립니다.
      if (state.blind === BlindKind.Boss) {
        this.game.audio.play('boss_reveal')
        this.game.audio.music.duck(0.5, 1.1)
        this.game.show.haptics.play('boss')
      }
    }

    const order = [BlindKind.Small, BlindKind.Big, BlindKind.Boss]
    // 설명과 두 갈래 행동을 담는 카드입니다. 226픽셀에서는 태그 이름과 한국어 설명이
    // 같은 줄에서 서로 밀어내므로, 세 장이 화면 안에 남는 범위에서 244픽셀로 넓힙니다.
    const cardW = 244
    const gap = 18
    const cardH = 322
    // **아래에 붙입니다.** 조커 줄과 판 사이가 비면 화면이 위로 쏠리고, 판이 서는 자리는
    // 카드를 내는 자리와 같아야 눈이 옮겨 다니지 않습니다.
    //
    // **아래 변은 떠 있는 판들과 같은 자리입니다.** 이 판 셋만 조금 위에 떠 있었고, 정산과
    // 상점이 그 자리에서 나오므로 판이 갈릴 때 아래 변이 한 번 튑니다.
    const bottom = PANEL_BOTTOM
    // **가로는 다른 판들과 같은 규칙입니다** — 화면의 가운데이고, 왼쪽 판을 침범하면
    // 그만큼 오른쪽입니다. 판이 도는 자리의 가운데에 두었더니 이 셋만 오른쪽에 쏠려 있었고,
    // 정산과 상점이 그 자리에서 나옵니다.
    const spread = order.length * cardW + (order.length - 1) * gap
    const startX = popupLeft(spread) + cardW / 2

    order.forEach((blind, index) => {
      const row = this.game.data.tables.blind.getByBlindOrThrow(blind)
      const boss = blind === BlindKind.Boss
      const bossRow = boss ? this.game.data.tables.bossBlind.findByBossId(state.bossId)
        : undefined
      const now = blind === state.blind
      const done = blind < state.blind

      // 건너뛸 수 있으면 태그 딱지가 들어갑니다. 보스는 건너뛸 수 없고, 이미 지난 것도
      // 건너뛸 것이 없습니다.
      const skippable = row.skippable && blind !== BlindKind.Boss && !done
      // **건너뛰면 무엇을 받는가.** 스몰과 빅이 나란히 서므로 둘 다 적혀 있어야 지금 것을
      // 건너뛸지 다음 것을 건너뛸지를 견줄 수 있습니다.
      const offer = skippable ? tagFor(state, blind) : undefined
      const tag = offer ? this.tagPlate(offer, now ? cardW - 40 : cardW - 36, !now) : undefined

      // **건너뛰기와 그 보상은 한 덩어리입니다.** 단추·태그·주동작을 같은 간격으로
      // 세워 두면 태그가 어느 행동의 결과인지 모호합니다. 건너뛰기와 태그를 눌린 판 안에
      // 묶고, 블라인드 시작은 그 밖의 금색 단추 하나로 둡니다.
      const actionW = cardW - 24
      let skipChoice: { node: Container; height: number; buttonY: number } | undefined
      if (now && skippable) {
        const node = new Container()
        const innerW = actionW - 16
        const skip = new Button(t('ui.button.skip'), innerW, 36, 'dare', () => {
          if (this.skipping) return
          this.game.audio.play('blind_skip')
          if (tag) {
            this.skipFrom = this.game.overlay.toLocal(tag.face.getGlobalPosition())
            this.skipping = true
          }
          this.game.act({ t: 'skip_blind' })
        })
        skip.position.set(8, 8)
        let choiceH = 8 + 36
        if (tag) {
          tag.node.position.set(8, choiceH + 8)
          choiceH += 8 + tag.height
        }
        choiceH += 8
        const choiceSkin = piece('well', actionW, choiceH, wellTint(UI.cell))
        if (choiceSkin !== undefined) node.addChild(choiceSkin)
        else {
          const fallback = new Graphics().rect(0, 0, actionW, choiceH)
            .fill({ color: UI.cell, alpha: 0.95 })
          node.addChild(fallback)
        }
        if (tag) node.addChild(tag.node)
        node.addChild(skip)
        skipChoice = { node, height: choiceH, buttonY: skip.y + 18 }
      }

      // 밑단에 쌓이는 것들의 높이. 아래에서 위로 쌓습니다.
      //
      // 지금 차례인 칸에는 **하는 일 둘이 들어갑니다** — 이 블라인드로 가는 것과 건너뛰는
      // 것이고, 그 사이에 구분선 하나가 놓입니다.
      // **가르는 줄은 없습니다.** 무리는 사이의 넓이가 가릅니다.
      const stack: number[] = now
        ? [...(skipChoice ? [skipChoice.height] : []), 48]
        : [20, ...(tag ? [tag.height] : [])]
      // **사이는 12 입니다.** 단추의 턱(3)과 그림자가 아래로 내려오므로 8 이면 그 아래의
      // 것이 단추에 붙어 보입니다.
      const stackH = stack.reduce((sum, one) => sum + one + STACK_GAP, 0)

      const group = new Container()
      // 지금 차례인 것만 앞으로 나옵니다. **아랫변을 맞춥니다** — 위로 자라면 줄이
      // 들쭉날쭉해 보입니다. 밑단에 쌓인 만큼은 반드시 자랍니다.
      const height = Math.max(cardH + (now ? 26 : 0), 222 + stackH + 12)

      const entry: BlindGroup = {
        group, index, x: startX + index * (cardW + gap), bottom, width: cardW, height, now, done,
      }
      this.blindGroups.push(entry)
      this.placeBlindGroup(entry)

      // **셋이 같은 판입니다.** 머리띠를 저마다의 색으로 칠하면 판 셋이 서로 다른 물건이
      // 되고, 어느 것을 지금 고르는지는 색이 아니라 자리와 밝기가 말합니다 — 고를 것은
      // 위로 서고 다음 차례는 옅습니다. 색은 이름 앞의 문양 하나에만 듭니다.
      const plate = new Graphics()

      group.addChild(plate)

      // **구워 둔 판 한 장입니다.** 채움과 잘린 귀가 그 안에 있습니다.
      const rim = piece('plate', cardW, height, plateTint(UI.panel))
      if (rim !== undefined) {
        // **고를 차례가 아닌 판은 옅습니다.** 판만 옅고 테가 또렷하면 그 판이 앞으로 나온
        // 것으로 보입니다.
        rim.alpha = now ? 1 : 0.55
        group.addChildAt(rim, 0)
      } else {
        plate.rect(0, 0, cardW, height).fill({ color: UI.panel, alpha: UI.panelAlpha })
      }
      // **머리 판은 외피 안쪽에 들어갑니다.** 원화의 양끝 장식이 판 밖으로 튀어나오면
      // 세 카드가 서로 침범합니다. 외피에서 8픽셀 물려 제목의 소속을 분명히 합니다.
      const tone = boss ? UI.red : blind === BlindKind.Big ? UI.legendary : UI.bar
      const band = piece('blind-head', cardW - 16, BAND_H, mix(tone, UI.panel, 0.62))
      if (band !== undefined) {
        band.alpha = now ? 1 : 0.55
        band.x = 8
        group.addChildAt(band, rim !== undefined ? 1 : 0)
      }

      const label = (text: string, size: number, fill: number, weight = '700') =>
        new Text({ text, style: { fontSize: size, fill, fontWeight: weight as never } })

      const name = label(bossRow
        ? nameOf(this.game.data, 'boss', state.bossId, bossRow.name)
        : tf('ui.blind.named', { name: blindName(blind) }), TEXT.base, mix(tone, UI.ink, 0.35), '800')
      // **이름은 칸의 가운데입니다.** 셋이 나란히 서는 판이고, 이름이 왼쪽에 붙으면
      // 보스의 긴 이름과 「스몰 블라인드」가 저마다 다른 자리에서 끝납니다 — 문양은 띠의
      // 왼쪽 끝에 얹히는 것이지 이름과 한 줄로 서는 것이 아닙니다.
      name.anchor.set(0.5, 0.5)
      // 문양을 밀지 않는 만큼 줄입니다. 보스의 이름은 말에 따라 두 배로 길어집니다.
      const nameRoom = cardW - 46 * 2
      if (name.width > nameRoom) name.scale.set(nameRoom / name.width)
      name.position.set(cardW / 2, BAND_H / 2)
      group.addChild(name)

      // **보스에는 인장이 붙습니다.** 스물여덟이 이름 하나로만 갈리면 어느 것이 나왔는지가
      // 판마다 남지 않습니다. 이름 왼쪽이고, 이름은 그만큼 오른쪽으로 비켜섭니다.
      // **이름은 언제나 가운데입니다.** 인장이 붙는 보스만 이름을 오른쪽으로 비켜세웠고,
      // 그러면 셋이 나란히 섰을 때 보스의 이름만 다른 자리에 있습니다 — 인장은 띠의 왼쪽
      // 끝에 얹히는 것이지 이름과 한 줄로 서는 것이 아닙니다.
      const seal = blindFace(blind, 24, this.game.state.bossId)
      seal.position.set(28, BAND_H / 2)
      group.addChild(seal)

      // **세 자리마다 쉼표를 찍습니다.** 요구 점수는 안테가 오르면 네 자리 다섯 자리가
      // 되고, 쉼표가 없으면 30000 과 300000 을 한눈에 가릴 수 없습니다.
      const need = label(
        targetOf(this.game.data, state, blind).toLocaleString('en-US'), TEXT.display, UI.bar, '800')
      need.anchor.set(0.5, 0)
      need.position.set(cardW / 2, BAND_H + 30)
      group.addChild(need)

      const needCaption = label(t('ui.label.target'), TEXT.small, UI.inkDim)
      needCaption.anchor.set(0.5, 0)
      needCaption.position.set(cardW / 2, BAND_H + 14)
      group.addChild(needCaption)

      const reward = label(tf('ui.blind.reward',
        { n: rewardOf(this.game.data, this.game.state, row.blind) }), TEXT.body, UI.money, '800')
      reward.anchor.set(0.5, 0)
      reward.position.set(cardW / 2, BAND_H + 76)
      group.addChild(reward)

      // 보스의 효과. **건너뛸지를 정하는 것이 대부분 이 한 줄입니다.**
      const note = bossRow
        ? describe(this.game.data, this.game.data.bossEffects.get(state.bossId)
            ?? []).join(NEWLINE)
        : t('ui.note.no_rules')
      // **수와 이름은 다른 색입니다.** 「패에서 2장을 버립니다」에서 판단을 가르는 것은
      // 그 2 입니다.
      // **접습니다.** 접는 폭을 주지 않으면 한 줄로 뻗어 카드 밖으로 나갑니다 — 보스의
      // 효과는 「패에서 무늬가 같은 카드를 2장 버립니다」 처럼 깁니다.
      //
      // 그리고 **`richBlock` 으로 쌓습니다.** 줄마다 따로 그려 17픽셀씩 내리면, 접혀서 두
      // 줄이 된 것이 다음 줄 위에 겹칩니다.
      const noteWidth = cardW - 36
      const noteText = richBlock(note.split(NEWLINE),
                                 richStyle('note', boss ? { fill: UI.red } : undefined),
                                 richLeading('note'), noteWidth, 'center')
      noteText.position.set((cardW - noteWidth) / 2, BAND_H + 104)
      group.addChild(noteText)

      // 아래에서 위로 쌓습니다. **아랫변이 맞아야 셋이 한 줄로 보입니다.**
      let at = height - 12
      const place = (node: Container, w: number, h: number): void => {
        at -= h
        node.position.set((cardW - w) / 2, at)
        at -= STACK_GAP
        group.addChild(node)
      }

      if (done) {
        const mark = label(t('ui.label.cleared'), TEXT.body, UI.green, '800')
        mark.anchor.set(0.5, 0)
        mark.position.set(cardW / 2, height - 40)
        group.addChild(mark)
      } else if (!now) {
        const mark = label(t('ui.label.next_up'), TEXT.body, UI.inkDim, '700')
        mark.anchor.set(0.5, 0)
        mark.position.set(cardW / 2, height - 32)
        group.addChild(mark)
        at = height - 40
        if (tag) place(tag.node, cardW - 36, tag.height)
      } else {
        // **이 블라인드로 가는 것이 맨 아래입니다.** 셋 중 지금 차례인 칸에서만 뜨는
        // 단추이고, 밑단에 붙어 있어야 다음 안테에서도 같은 자리입니다.
        const pick = new Button(t('ui.button.select_blind'), actionW, 48, 'primary',
          () => this.game.act({ t: 'select_blind' }))
        place(pick, actionW, 48)
        entry.pickY = pick.y + 24
        this.game.spots.pick = { x: group.x + cardW / 2, y: group.y + entry.pickY }

        if (skipChoice) {
          place(skipChoice.node, actionW, skipChoice.height)
          entry.skipY = skipChoice.node.y + skipChoice.buttonY
          this.game.spots.skip = { x: group.x + cardW / 2, y: group.y + entry.skipY }
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
  /**
   * 태그 딱지 하나.
   *
   * **이름과 하는 일이 함께 적혀 있어야 합니다.** 이름만 있으면 「저글 태그」 가 무엇인지
   * 모르는 채로 건너뛸지를 정하게 됩니다.
   */
  private tagPlate(tagId: string, width: number, framed = true):
      { node: Container; height: number; face: Container } {
    const lines = describe(this.game.data, this.game.data.tagEffects.get(tagId) ?? [])

    // **그림이 읽힐 만큼은 되어야 합니다.** 22픽셀에서는 색깔 있는 점 하나이고, 그러면
    // 태그마다 그림이 다르다는 것 자체가 보이지 않습니다.
    const FACE = 40
    const textLeft = 12 + FACE

    // **글을 먼저 만들고 딱지의 높이를 그것에 맞춥니다.** 못박으면 두 줄인 태그에서 아랫줄이
    // 딱지 밖으로 나갑니다 — 어느 태그의 설명이 긴지는 데이터가 정합니다.
    //
    // **접는 폭은 실제로 놓일 폭입니다.** 넓게 잡아 높이를 재고 나서 좁게 다시 접었고,
    // 좁으면 줄이 늘어나므로 잰 높이보다 커집니다 — 딱지 밖으로 나가던 것이 그것입니다.
    //
    // 그리고 **낱말 사이를 찾지 못하면 글자에서 끊습니다.** 일본어와 중국어는 띄어쓰기가
    // 없어서, 낱말 경계만 찾는 접기로는 한 줄이 그대로 뻗습니다.
    const note = new Text({
      text: lines.join(' · '),
      style: {
        fontSize: TEXT.micro, fill: UI.inkDim,
        wordWrap: true, wordWrapWidth: width - textLeft - 8, breakWords: true,
      },
    })
    const height = Math.max(FACE + 12, 20 + note.height + 8)

    const node = new Container()
    // **눌린 칸입니다.** 구운 `well` 자체가 재질과 가장자리를 가지고 있습니다. 그 위에
    // 직사각형 선을 한 번 더 그리면 태그만 웹 카드처럼 둘러싸이므로, 선은 그림을 못 읽은
    // 비상 채움에서만 씁니다. 태그의 색은 얼굴과 이름이 냅니다.
    if (framed) {
      const skin = piece('well', width, height, wellTint(UI.cell))
      if (skin !== undefined) node.addChild(skin)
      else {
        const fallback = new Graphics()
          .rect(0, 0, width, height).fill({ color: UI.cell, alpha: 0.95 })
          .rect(0.5, 0.5, width - 1, height - 1)
          .stroke({ color: UI.accentTerm, width: 1, alpha: 0.7 })
        node.addChild(fallback)
      }
    }

    const face = tagFace(tagId, FACE)
    face.position.set(6 + FACE / 2, height / 2)
    node.addChild(face)

    const name = new Text({
      text: nameOf(this.game.data, 'tag', tagId, tagId),
      style: { fontSize: TEXT.small, fill: UI.ink, fontWeight: WEIGHT.bold },
    })
    name.position.set(textLeft, 6)
    const nameRoom = width - textLeft - 8
    if (name.width > nameRoom) name.scale.set(nameRoom / name.width)
    node.addChild(name)

    note.position.set(textLeft, 22)
    node.addChild(note)

    node.eventMode = 'static'
    node.hitArea = new Rectangle(0, 0, width, height)
    this.game.input.tipOn(node, at => {
      this.game.input.tooltip.show(nameOf(this.game.data, 'tag', tagId, tagId), t('ui.kind.tag'),
        0, lines,
        at, SIZE)
    })
    return { node, height, face }
  }

  /**
   * 태그의 얼굴.
   *
   * 그림이 있으면 그림, 없으면 문양입니다 — **그림이 오기 전에도 종류가 갈려 보여야
   * 합니다.**
   */
  /**
   * 들고 있는 태그.
   *
   * **상점에 들어갈 때까지 들고 있는 것입니다.** 「적용 중」 목록의 한 줄로만 두면 그것이
   * 지금 들고 있는 물건이라는 것이 읽히지 않습니다 — 조커와 소모품 줄 옆에 딱지로 놓입니다.
   */
  /**
   * 들고 있는 태그.
   *
   * **이제 블라인드 딱지 안에 놓입니다**(`tagChips`). 화면 오른쪽 위에 따로 두었는데, 그쪽
   * 끝은 덱과 소모품 칸이 이미 쓰고 있어서 태그가 그 둘 사이에 낀 셋째 줄처럼 보였고,
   * 무엇에 딸린 것인지도 끊겼습니다 — 태그는 다음 상점까지 들고 있는 것이므로 지금
   * 무엇과 붙고 있는지를 적은 그 딱지가 그 자리입니다.
   */
  syncTags(): void {
    this.game.tray.tagLayer.visible = false
  }

  syncBadge(): void {
    const state = this.game.state

    // **태그는 연출과 상관없이 지금 것입니다.** 딱지 전체는 연출이 끝난 뒤에 바꾸지만,
    // 태그는 그 연출 안에서 들어오므로 함께 묶으면 딱지가 한 번씩 뒤처집니다 — 첫 스킵의
    // 태그가 보이지 않고 다음 스킵에서야 그 앞의 것이 뜨던 것이 그것입니다.
    const chips = this.tagChips()
    this.badge.setTags(chips)

    // 연출이 도는 중에는 앞 국면의 딱지를 그대로 둡니다.
    if (!this.game.presented) return

    // **자리를 비우는 중.** 무엇을 놓을 자리인지와 어디서 고르는지가 여기에도 적힙니다.
    if (this.game.tray.focus) {
      const item = this.game.tray.focus.item
      this.badge.setInfo(t('ui.swap.title'),
        tf('ui.swap.lead', { name: shopLabel(item.kind, item.id, this.game.data) }),
        [t(this.game.tray.focus.kind === 'joker' ? 'ui.focus.pick_joker' : 'ui.focus.pick_item'),
          t('ui.swap.paid')],
        UI.yellow, undefined, chips)
      return
    }

    // **뜯은 팩.** 무엇을 뜯었고 몇 장을 고르는지입니다.
    if (state.pack) {
      const row = this.game.data.tables.boosterPack.findByPackId(state.pack.packId)
      this.badge.setInfo(row ? packName(row.kind, row.size) : t('ui.kind.pack'),
        tf('ui.pack.pick_from', { n: state.pack.picksLeft }),
        [tf('ui.pack.of', { cards: state.pack.options.length, picks: row?.picks ?? 1 }),
          t('ui.badge.pack_note')],
        packInk(state.pack.kind), undefined, chips)
      return
    }

    // **상점.** 다음에 붙을 블라인드와 그 요구 점수가 여기 있어야 무엇을 준비하는 것인지
    // 알 수 있습니다 — 「요구 점수 0 · 격파 보상 $0」 은 상점에서 뜻이 없는 값이었습니다.
    if (state.phase === 'shop') {
      const next = state.blind === BlindKind.Small ? BlindKind.Big
        : state.blind === BlindKind.Big ? BlindKind.Boss : BlindKind.Small
      // 보스를 넘긴 뒤의 다음은 다음 안테의 스몰 블라인드입니다.
      const ahead = next === BlindKind.Small ? { ...state, ante: state.ante + 1 } : state
      const name = tf('ui.blind.named', { name: blindName(next) })
      this.badge.setNext(t('ui.guide.shop.head'), tf('ui.badge.next', { name }),
        targetOf(this.game.data, ahead, next), rewardOf(this.game.data, ahead, next),
        UI.bar, undefined, chips)
      return
    }

    const boss = state.blind === BlindKind.Boss
    const bossRow = boss ? this.game.data.tables.bossBlind.findByBossId(state.bossId) : undefined

    // 고르는 중이면 무엇을 하라는 것인지가 여기에도 적힙니다. 보스의 규칙이 있으면 그것이 먼저입니다.
    // **고르는 판의 안내는 적지 않습니다.** 그 화면에 이미 딱지 셋과 「이 블라인드로 ·
    // 건너뛴다」가 놓여 있어서 같은 말이 두 번이고, 딱지 안에서는 그 줄이 넷째 줄이라
    // 수를 한 계단 내려앉혀 판의 주인공을 지웁니다.
    const note = bossRow
      ? describe(this.game.data, this.game.data.bossEffects.get(state.bossId) ?? []).join(' · ')
      : ''

    this.badge.set(
      bossRow
        ? nameOf(this.game.data, 'boss', state.bossId, bossRow.name)
        : tf('ui.blind.named', { name: blindName(state.blind) }),
      Number(state.target), rewardOf(this.game.data, state, state.blind), note, boss,
      state.blind === BlindKind.Big,
      // **판이 도는 내내 보이는 자리입니다.** 고르는 판은 한 번 지나가지만 이 딱지는
      // 남습니다 — 어느 보스와 붙고 있는지가 여기 있어야 합니다.
      blindFace(state.blind, 22, this.game.state.bossId),
      // 들고 있는 태그. **딱지 안 아래에 가운데로 놓입니다** — 화면 구석에 따로 두었더니
      // 무엇에 딸린 것인지가 끊겼고, 조커 줄과 덱 사이에 낀 셋째 줄처럼 보였습니다.
      // **위에서 만든 그 칩들입니다.** 다시 만들면 위의 것을 그 자리에서 버립니다.
      chips)
  }

  /**
   * 들고 있는 태그의 칩들.
   *
   * **셋까지입니다.** 그보다 많이 들고 있는 일은 드물고, 넷째부터는 딱지가 그만큼 자라서
   * 그 아래의 점수 칸을 밀어냅니다.
   */
  private tagChips(): Container[] {
    this.tagCells = []
    if (this.game.session.scene !== 'run') return []
    // 머리띠 안에 앉으므로 띠보다 작아야 합니다. 띠가 32이고 그 안에 26입니다.
    const size = 26

    // 안테가 바뀌면 쓴 것의 줄을 비웁니다.
    if (this.tagSpentAnte !== this.game.state.ante) {
      this.tagSpentAnte = this.game.state.ante
      this.tagSpent = []
    }

    // **받은 순서 그대로입니다.** 쓴 것이 먼저이고 들고 있는 것이 뒤입니다 — 새로 받은
    // 것이 바깥쪽에 서야 방금 무엇이 생겼는지가 자리로도 읽힙니다.
    const held = this.game.state.tagsPending
    const spent = this.tagSpent.filter(one => !held.includes(one))
    return [...spent, ...held].slice(-4).map(tagId => {
      const used = !held.includes(tagId)
      const lines = describe(this.game.data, this.game.data.tagEffects.get(tagId) ?? [])
      const cell = new Container()
      // **피벗은 늘 가운데입니다.** 발동할 때만 옮기면 그 순간에 자리가 한 번 바뀌고,
      // 세우는 쪽이 그것을 되돌려도 두 프레임에 걸쳐 흔들립니다 — 처음부터 가운데면
      // 부풀리는 것이 자리를 건드리지 않습니다.
      cell.pivot.set(size / 2, size / 2)

      // **그림이 이미 칩입니다.** 그 뒤에 또 네모 딱지를 깔면 칩이 액자에 든 것으로
      // 보입니다 — 그림이 없을 때만 딱지를 깝니다.
      const texture = artFor('tag', tagId)
      if (!texture) {
        const plate = new Graphics()
        plate.rect(0, 0, size, size).fill({ color: UI.cell, alpha: 0.95 })
        plate.rect(0.5, 0.5, size - 1, size - 1)
          .stroke({ color: UI.accentTerm, width: 1.5, alpha: 0.7 })
        cell.addChild(plate)
      }
      const face = tagFace(tagId, texture ? size : size - 10)
      face.position.set(size / 2, size / 2)
      cell.addChild(face)

      // **쓴 것은 흐리게 남습니다.** 지우면 그 자리에서 쓰이는 태그가 화면에 한 프레임도
      // 서지 못하고, 같은 밝기로 두면 아직 들고 있는 것과 갈리지 않습니다.
      if (used) cell.alpha = 0.42

      // **다만 발동하는 그 순간에는 켜집니다.** 그 태그가 한 일이 그 순간이고, 흐려지는
      // 것은 그 뒤에 남는 상태입니다.
      const record: TagCell = { cell, tagId, used, size }
      this.tagCells.push(record)
      const fire = this.tagFire.get(tagId)
      if (fire !== undefined && fire >= 0) {
        const wave = Math.sin(fire * Math.PI)
        cell.alpha = 0.42 + wave * 0.58
        cell.scale.set(1 + wave * 0.3)
        record.lit = glare(size, 1)
        record.lit.alpha = wave * 0.9
        cell.addChild(record.lit)
      }

      // 새로 선 칩 하나만 한 번 하얗게 번쩍이며 나옵니다.
      //
      // **셰이더를 걸지 않습니다.** `ArriveFilter` 는 카드 한 장의 크기에 맞춰 여백을 잡아
      // 두었고, 26픽셀짜리 칩에서는 그 여백이 차지하는 비율이 딴판이라 그림이 왼쪽 위로
      // 밀립니다 — 「안착했다가 한 번 튄다」가 그것이었습니다. 작은 것에 필요한 것은
      // 왜곡이 아니라 밝아짐 하나입니다.
      //
      // 켜지는 것도 꺼지는 것도 사인 한 마디입니다. **꼭대기가 0.8입니다** — 1이면 그
      // 순간 칩이 통째로 하얘져서 무엇이 생겼는지가 도리어 안 보입니다.
      const life = tagId === this.tagFlashId ? this.tagFlashLife : 1
      if (life < 1) {
        record.shine = glare(size, 1)
        record.shine.alpha = Math.sin(life * Math.PI) * 0.8
        cell.addChild(record.shine)
      }

      cell.eventMode = 'static'
      cell.hitArea = new Rectangle(0, 0, size, size)
      this.game.input.tipOn(cell, at => {
        this.game.input.tooltip.show(nameOf(this.game.data, 'tag', tagId, tagId),
          t('ui.kind.tag'), 0, lines,
          at, SIZE)
      })
      return cell
    })
  }
}
