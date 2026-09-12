import { Container, Graphics, Text } from 'pixi.js'
import { t, tf } from '../core/strings'
import { NUMERALS } from '../ui/font'
import { Coins } from '../render/coins'
import { popupCenter, TEXT, UI, WEIGHT } from '../render/theme'
import { Button } from '../ui/widgets'
import { type ModalPanel, PANEL_BOTTOM, panelFrame, TITLE_BAR } from '../ui/modal'
import { hairline, ProgressBar, SECTION_H, sectionHead } from '../ui/parts'
import {
  COIN_FLIGHTS, COIN_LAUNCH, COIN_MAX, COIN_MERGE, COIN_SPREAD, COIN_STEP, PAYOUT_STEP,
  PAYOUT_WAIT, SWEEP_REST,
} from './metrics'
import { blindName, moneyReason } from './tables'
import { type Game } from './game'
export class PayoutPart {
  constructor(private readonly game: Game) {}

  readonly coins = new Coins()

  /** 「받는다」 의 자리. **눌릴 수 있게 되면** `spots.take` 로 알립니다. */
  private takeSpot?: { x: number; y: number }

  /**
   * 정산 판.
   *
   * **돈이 어디서 나왔는지가 한자리에 모여야 합니다.** 동전이 날아가는 것만으로는 격파
   * 보상과 남긴 핸드와 이자가 한 덩어리로 보이고, 다음 판에 무엇을 아껴야 하는지가
   * 남지 않습니다.
   */
  readonly panel: ModalPanel = {
    view: new Container(),
    size: { width: 380, height: 240 },
    // **뒤를 덮지 않습니다.** 정산은 판이 도는 그 자리의 한 걸음입니다 — 뒤에서 카드가
    // 걷히고 다음 패가 깔리는 것을 보는 중인데 그것을 덮으면 걸음이 끊깁니다.
    covers: false,
    // **다 닫힌 뒤에 상점이 뜹니다.** 닫기를 누른 그 순간에 그리면 판이 아직 물러나는
    // 중이라 상점이 비어 보입니다.
    onClosed: () => {
      this.payoutOpen = false
      this.payoutWanted = false
      this.payoutTaking = false
      // **받지 않고 닫힌 판의 돈은 그 자리에서 잔액에 들어갑니다.** 줄이 남아 있으면 화면의
      // 잔액이 그만큼 코어보다 뒤에 머문 채로 남습니다.
      let left = this.payoutRows.reduce((sum, row) => sum + row.amount, 0)
      this.payoutRows.length = 0
      // **뜨지 못한 낱개의 몫도 같습니다.** 받는 중에 판이 걷히면(`Esc`) 남은 낱개는 뜰
      // 자리가 없어졌으므로 그 자리에서 잔액에 들어갑니다. **뜬 것으로 셈해 둡니다** —
      // 판을 닫는 자리가 「아직 뜰 것이 남았는가」를 이 값으로 보므로, 그대로 두면 다음
      // 판을 받을 때 그 자리가 영영 오지 않습니다.
      const taking = this.payoutBar?.taking
      if (taking) {
        for (let i = taking.launched; i < taking.share.length; i++) left += taking.share[i]
        taking.launched = taking.share.length
      }
      if (left !== 0) {
        this.game.shown.money += left
        this.game.chrome.money.target = this.game.shown.money
      }
      this.game.refresh()
    },
  }

  /** 정산에 오른 줄들. 이벤트가 하나씩 더합니다. */
  /** 정산 판의 줄들. **이유는 열쇠로 들고 있습니다** — 글은 그릴 때 그때의 말로 적습니다. */
  readonly payoutRows: { reason: string; amount: number }[] = []

  /**
   * 정산 판을 세울 차례인가.
   *
   * **카드가 다 걷힌 뒤에 뜹니다.** 판이 떠 있는 채로 그 밑에서 카드가 물러나면, 끝난
   * 것과 끝나는 중인 것이 한 화면에 겹칩니다 — 격파는 이미 정해졌지만 그것을 보는 순서는
   * 카드가 물러나고 나서입니다.
   */
  payoutWanted = false

  /**
   * 「받는다」 를 눌러 동전이 금액 칸으로 날아가는 중인가.
   *
   * **판은 동전이 다 닿은 뒤에 닫힙니다.** 누른 자리에서 닫으면 동전은 아무 데서도 나오지
   * 않은 것이 되고, 그동안 단추는 「받는 중」으로 잠깁니다.
   */
  payoutTaking = false

  /** 카드가 다 걷힌 시각. **-1 은 아직 걷히는 중입니다.** */
  sweptAt = -1

  /**
   * 정산 판이 지금 떠 있는가.
   *
   * **`modals.has` 를 쓰지 않습니다.** 그것은 닫히는 중인 판을 곧바로 없는 것으로 세므로,
   * 「받는다」 를 누른 다음 프레임에 이 코드가 판을 다시 엽니다 — 창이 닫히지 않고 계속
   * 눌리던 것이 그것입니다.
   */
  payoutOpen = false

  /**
   * 정산의 줄이 하나씩 서는 것.
   *
   * 판이 열릴 때 이미 줄이 다 모여 있으므로, 쌓이는 것은 **그리는 쪽에서** 만듭니다 —
   * 상점이 하나씩 서는 것과 같은 계산이고, 다만 목록을 따로 둡니다: `reveals` 는
   * `refresh` 가 비우므로 판이 열리는 그 프레임에 지워집니다.
   */
  readonly payoutNodes: { node: Container; at: number; from: number }[] = []

  /**
   * 줄이 서기 전에 뜨는 「정산 중」.
   *
   * **판은 먼저 열리고 줄은 하나씩 쌓입니다.** 그 사이가 빈 상자라, 무엇을 기다리는
   * 중인지가 적혀 있지 않으면 판이 잘못 열린 것으로 보입니다.
   */
  payoutWait?: {
    head: Text
    /** 뼈대 줄. 매 프레임 다시 그립니다 — 줄마다 옅어지는 정도가 다릅니다. */
    bones: Graphics
    width: number
    rows: number
    top: number
    rowH: number
    /** 첫 줄이 서는 시각. 그 뒤로 `PAYOUT_STEP` 마다 하나씩입니다. */
    begin: number
  }

  /** 정산 판의 득점 바와 합계. 줄이 설 때마다 합계가 그만큼 셉니다. */
  private payoutBar?: {
    bar: ProgressBar; begin: number; ratio: number
    sum: Text; shown: number; poppedAt: number; rowAt: number[]; amounts: number[]
    /**
     * 합계의 `$` 낱개들.
     *
     * **줄이 설 때마다 그만큼 보입니다.** `coinRest` 는 저마다 서는 자리이고, 뭉칠 때
     * 오른쪽 끝으로 모입니다 — `mergeAt` 가 0 이면 받을 것이 없어 낱개가 없습니다.
     */
    coins: Text[]; coinRest: number[]; coinTo: number
    mergeAt: number; merged: boolean
    /**
     * 「받는다」 를 누른 뒤.
     *
     * **뭉쳐 있던 낱개가 다시 펼쳐지고, 하나씩 사라지면서 그 자리에서 동전이 뜹니다.**
     * 한 자리에서 열두 개가 함께 뜨면 그것은 곧게 그은 선 하나이고, 낱개가 이미 줄로
     * 펼쳐져 있으므로 그 자리를 쓰면 열두 갈래가 됩니다.
     *
     * `share` 가 0 인 낱개는 동전 없이 사라집니다 — 동전의 수는 열둘까지이고(그보다 많이
     * 날면 하나씩 꽂히는 소리가 뜻을 잃습니다) 낱개는 28까지입니다.
     */
    taking?: { at: number; share: number[]; launched: number; flights: number }
    /**
     * 「받는다」 와 그것이 열리는 시각.
     *
     * **줄이 다 서기 전에는 잠깁니다.** 열려 있으면 셈이 도는 중에 눌리고, 그러면 얼마를
     * 받은 것인지 보지 못한 채 판이 닫힙니다 — 합계가 다 센 뒤가 그 시각입니다.
     */
    take: Button; readyAt: number
  }

  /**
   * 액션의 연출이 끝나면 화면을 상태에 맞춰야 한다는 표시.
   *
   * **걸쇠입니다.** 연출이 도는 중에 액션이 들어오면 켜지고, 연출이 다 끝난(`presented`)
   * 첫 프레임에 `settleShown` 과 `refresh` 를 하면서 꺼집니다 — 정산의 `payoutWanted` 와
   * 같은 꼴입니다. 끄기 전에는 켜진 채로 남으므로, 그 사이가 몇 프레임이든 0 프레임이든
   * 놓치지 않습니다.
   *
   * **프레임마다 표본을 떠서 「바쁨→안 바쁨」 전환을 잡던 것을 대신합니다.** 그 방식은 한
   * 프레임 안에서 시작해 끝나는 연출을 보지 못했습니다 — 상점을 나설 때 발동하는 조커의
   * 박자 하나가 그랬고, 그때 블라인드 판은 조커나 소모품을 눌러 `refresh` 가 불릴 때까지
   * 뜨지 않았습니다.
   */
  settleOwed = false

  /** 연출이 끝났습니다. 화면이 주장하는 것을 상태와 맞춥니다. */
  settleShown(): void {
    this.game.cards.deals.length = 0
    this.game.cards.dealtUntil = 0
    this.game.shown = {
      // **정산 판에 올라 있는 돈은 아직 화면의 것이 아닙니다.** 「받는다」 를 누를 때
      // 날아가 닿으면서 더해집니다.
      money: this.game.state.money - this.game.chrome.moneyInFlight(),
      score: Number(this.game.state.score),
      hand: this.game.state.hand.slice(),
      phase: this.game.state.phase,
    }
  }

  /**
   * 어느 층 안의 자리를 동전 층의 자리로 옮깁니다.
   *
   * **동전은 판 위, 모달 위에 있습니다.** 그래서 판 안의 좌표를 그대로 넘길 수 없고, 판이
   * 흔들리는 동안의 어긋남도 이것이 맞춥니다.
   */
  coinSpot(layer: Container, at: { x: number; y: number }): { x: number; y: number } {
    return this.coins.toLocal(layer.toGlobal(at))
  }

  /**
   * 정산 판이 서는 때.
   *
   * **카드가 다 걷혀야 뜹니다** — 낸 카드가 물러났고, 타서 사라지는 것도 끝났고, 손패도
   * 걷혔을 때입니다. 그러고 나서 줄이 하나씩 쌓입니다.
   */
  advancePayout(seconds: number): void {
    void seconds

    // 「받는다」 의 동전이 다 닿았으면 판을 닫습니다.
    // **닫은 자리에서 다시 그립니다.** 상점은 정산 판이 없어야 서므로, 닫힌 것을 그리는
    // 쪽이 알아야 합니다.
    // **낱개가 아직 남아 있으면 닫지 않습니다.** 펼치는 0.22초 동안에는 뜬 동전이 하나도
    // 없으므로, 동전만 보고 닫으면 그 자리에서 판이 사라집니다.
    const taking = this.payoutBar?.taking
    const launching = taking !== undefined
      && taking.launched < (this.payoutBar?.coins.length ?? 0)
    if (this.payoutTaking && !launching && !this.coins.busy) {
      this.payoutTaking = false
      this.game.panels.modals.close(this.panel)
      this.game.refresh()
    }

    if (this.payoutWanted && !this.payoutOpen) {
      const swept = this.game.cards.playedViews.length === 0 && this.game.cards.fades.length === 0
        && this.game.shown.hand.length === 0 && this.game.cards.deals.length === 0
        && this.game.cards.recalls.length === 0 && this.game.cards.retired === 0
      // **다 거둔 그 프레임에 판이 뜨지 않습니다.** 마지막 한 장이 덱에 닿는 것과 정산이
      // 올라오는 것이 겹치면 라운드가 끝난 것을 볼 틈이 없습니다 — 한 박자 둡니다.
      if (!swept || this.game.player.busy) this.sweptAt = -1
      else if (this.sweptAt < 0) this.sweptAt = this.game.clock
      if (swept && !this.game.player.busy && this.game.clock - this.sweptAt >= SWEEP_REST) {
        this.payoutOpen = true
        // **정산이 서는 것이 그 판의 끝입니다.** 환희의 겹이 남아 있으면 정산과 상점을 보는
        // 동안에도 그 판의 기가 화면에 깔려 있습니다 — 터지는 것은 이미 한참 전에 지났고
        // (정산은 카드를 다 걷은 뒤에 뜹니다), 여기서 남은 것은 물러납니다.
        this.game.show.euphoria.done()
        this.drawPayout()
        this.game.panels.modals.open(this.panel)
        // **상점은 정산 뒤입니다.** 판이 열린 것을 상점이 알아야 물러납니다 — 카드가
        // 걷히는 그 프레임에 상점이 이미 그려져 있습니다.
        this.game.refresh()
      }
    }

    this.advancePayoutBones()
    this.advancePayoutBar()

    for (const one of this.payoutNodes) {
      if (one.node.alpha < 1) this.game.shop.advanceOne(one)
    }
  }

  /**
   * 뼈대 줄.
   *
   * **줄마다 자기 차례에 걷힙니다.** 한꺼번에 걷으면 빈 상자가 한 번 보이고, 그러면
   * 뼈대를 깔아 둔 뜻이 없어집니다 — 실제 줄이 그 자리에 서는 그때 그 자리의 뼈대만
   * 사라집니다.
   */
  private advancePayoutBones(): void {
    const wait = this.payoutWait
    if (!wait) return

    wait.bones.clear()
    let left = 0
    for (let i = 0; i < wait.rows; i++) {
      const at = wait.begin + i * PAYOUT_STEP
      const fade = Math.max(0, Math.min(1, (at - this.game.clock) / 0.18))
      if (fade <= 0) continue
      left++

      // 물결. **가만히 있는 회색 막대는 멈춘 화면으로 보입니다.**
      const wave = 0.42 + 0.26 * Math.sin(this.game.clock * 5 - i * 0.9)
      const alpha = fade * wave
      const y = wait.top + i * wait.rowH + 8
      // 왼쪽이 이유, 오른쪽이 금액. 실제 줄과 같은 자리입니다.
      wait.bones.roundRect(24, y, 132, 15, 7).fill({ color: UI.inkFaint, alpha })
      wait.bones.roundRect(wait.width - 24 - 62, y, 62, 15, 7)
        .fill({ color: UI.inkFaint, alpha: alpha * 0.86 })
    }

    wait.head.alpha = Math.max(0, Math.min(1, (wait.begin - this.game.clock) / 0.2))
    if (left > 0) return
    wait.bones.destroy()
    wait.head.destroy()
    this.payoutWait = undefined
  }

  /**
   * 정산 판.
   *
   * 줄이 하나씩 쌓입니다 — 이벤트가 하나씩 오므로 그리는 것도 하나씩이고, 그 쌓이는 것이
   * 곧 「어디서 얼마가 들어왔는가」입니다.
   */
  drawPayout(): void {
    const layer = this.panel.view
    layer.removeChildren().forEach(child => child.destroy())
    this.payoutNodes.length = 0
    this.payoutBar = undefined

    const width = 420
    const pad = 24
    const inner = width - pad * 2
    const rowH = 42
    const rows = Math.max(1, this.payoutRows.length)
    // 위에서부터 — 머리 · 블라인드 구획(득점 / 요구 바) · 받는 돈 구획(줄들과 합계) · 단추.
    // **판의 높이는 줄 수를 따릅니다.** 줄이 하나씩 서는 동안은 뼈대 줄이 그 자리를 잡습니다.
    const barTop = TITLE_BAR + 16
    const listTop = barTop + SECTION_H + 44 + 8
    const rowsTop = listTop + SECTION_H + 4
    const sumTop = rowsTop + rows * rowH + 6
    const buttonTop = sumTop + 56 + 14
    const height = buttonTop + 48 + 22
    ;(this.panel.size as { width: number; height: number }).height = height
    // **「받는다」 의 자리를 도구에 알립니다.** 판은 화면 가운데에 서고 높이는 줄 수를
    // 따르므로, 도구가 줄 수를 짐작해 셈하면 줄이 하나 늘 때마다 빈자리를 누릅니다.
    //
    // **눌릴 수 있게 된 뒤에 알립니다.** 줄이 다 서기 전에는 잠겨 있고, 그때 알리면 도구는
    // 잠긴 단추를 한 번 누르고 눌렀다고 넘어갑니다 — 그 뒤로 아무것도 진행되지 않습니다.
    this.takeSpot = { x: popupCenter(width), y: PANEL_BOTTOM - height + buttonTop + 24 }
    delete this.game.spots.take

    const sum = this.payoutRows.reduce((total, row) => total + row.amount, 0)
    // **받을 것이 없으면 없다고 적습니다.** 단추도 「다음」 입니다 — 0원을 받는 것은 받는
    // 것이 아닙니다.
    const empty = this.payoutRows.length === 0
    layer.addChild(panelFrame(width, height, t('ui.payout.title'), undefined, undefined, false))

    // 어디를 넘겼는가. **득점 / 요구 바 하나입니다** — 얼마나 넘겼는지가 수 둘이 아니라
    // 바의 채움으로 읽힙니다.
    const score = Number(this.game.state.score)
    const target = Number(this.game.state.target)
    const where = tf('ui.over.where', { ante: this.game.state.ante,
      blind: blindName(this.game.state.blind) })
    const head = sectionHead(inner, where)
    head.position.set(pad, barTop)
    const barY = barTop + SECTION_H + 22
    const scored = new Text({
      text: `${t('ui.stat.score')}  ${score.toLocaleString('en-US')}`,
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    scored.anchor.set(0, 0.5)
    scored.position.set(pad, barY)
    const wanted = new Text({
      text: `${t('ui.label.target')}  ${target.toLocaleString('en-US')}`,
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    wanted.anchor.set(1, 0.5)
    wanted.position.set(width - pad, barY)
    const bar = new ProgressBar(180, 8)
    bar.position.set(width / 2 - 90, barY - 4)
    layer.addChild(head, scored, wanted, bar)

    // 받는 돈. 줄마다 어디서 얼마가 왔는가이고, 아래에 합계 하나입니다.
    const earned = sectionHead(inner, t('ui.payout.earned'))
    earned.position.set(pad, listTop)
    layer.addChild(earned)

    // **줄이 하나씩 쌓입니다.** 판이 열릴 때 줄은 이미 다 모여 있으므로, 쌓이는 것은
    // 그리는 쪽에서 만듭니다 — 한꺼번에 그려 놓으면 어디서 얼마가 들어왔는지를 훑어야
    // 합니다. 설 때마다 동전 소리가 하나 나고 음이 올라갑니다.
    const rowAt: number[] = []
    const amounts: number[] = []
    this.payoutRows.forEach((row, index) => {
      const y = rowsTop + index * rowH
      const at = this.game.clock + PAYOUT_WAIT + index * PAYOUT_STEP
      rowAt.push(at)
      amounts.push(row.amount)

      const label = new Text({
        text: moneyReason(row.reason),
        style: { fontSize: TEXT.copy, fill: UI.ink, fontWeight: WEIGHT.normal },
      })
      label.anchor.set(0, 0.5)
      label.position.set(pad + 4, y + rowH / 2)

      const amount = new Text({
        text: `${row.amount > 0 ? '+' : ''}$${row.amount}`,
        style: {
          fontSize: TEXT.big, fill: row.amount > 0 ? UI.yellow : UI.red, fontWeight: WEIGHT.bold,
          fontFamily: NUMERALS,
        },
      })
      amount.anchor.set(1, 0.5)
      amount.position.set(width - pad - 4, y + rowH / 2)

      const line = hairline(inner)
      line.position.set(pad, y + rowH - 1)

      layer.addChild(label, amount, line)
      for (const node of [label, amount, line]) {
        const one = { node: node as Container, at, from: node.y }
        this.payoutNodes.push(one)
        this.game.shop.advanceOne(one)
      }
      this.game.show.chimes.push({ at, cue: 'coin_land', semitones: index * 3 })
    })

    // 줄이 없을 때의 한 줄.
    if (empty) {
      const none = new Text({
        text: t('ui.payout.nothing'),
        style: { fontSize: TEXT.copy, fill: UI.inkDim, fontWeight: WEIGHT.normal },
      })
      none.anchor.set(0.5, 0.5)
      none.position.set(width / 2, rowsTop + rowH / 2)
      layer.addChild(none)
      const one = { node: none as Container, at: this.game.clock + PAYOUT_WAIT, from: none.y }
      this.payoutNodes.push(one)
      this.game.shop.advanceOne(one)
    }

    // 뼈대 줄. **줄이 서기 전의 판이 휑했습니다.** 줄이 서는 그 자리의 뼈대가 그때 걷힙니다.
    const bones = new Graphics()
    const headless = new Text({ text: '', style: { fontSize: 1 } })
    layer.addChild(bones, headless)
    this.payoutWait = {
      head: headless, bones, width,
      rows, top: rowsTop + (rowH - 15) / 2 - 8, rowH,
      begin: this.game.clock + PAYOUT_WAIT,
    }

    // 합계. **줄이 설 때마다 그만큼 셉니다.** 큰 수 하나가 이 판의 무게입니다.
    const sumLabel = new Text({
      text: t('ui.payout.sum'),
      style: { fontSize: TEXT.body, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    sumLabel.anchor.set(0, 0.5)
    sumLabel.position.set(pad + 4, sumTop + 28)
    const sumText = new Text({
      text: '$0',
      style: { fontSize: TEXT.giant, fill: UI.yellow, fontWeight: WEIGHT.bold,
        fontFamily: NUMERALS },
    })
    sumText.anchor.set(1, 0.5)
    sumText.position.set(width - pad - 4, sumTop + 28)
    // **받을 것이 없으면 합계도 없습니다.** 「받을 것이 없습니다」 한 줄 아래에 「합계 $0」
    // 이 또 서면, 없다는 것을 두 번 적고 그중 하나는 수입니다.
    if (!empty) layer.addChild(sumLabel, sumText)

    // **낱개가 먼저 쌓입니다.** 줄이 설 때마다 그만큼 `$` 가 오른쪽으로 늘어서고, 다 서면
    // 오른쪽 끝으로 뭉치면서 그 자리에 수 하나가 남습니다.
    const coinRight = width - pad - 4
    const many = Math.min(Math.max(0, sum), COIN_MAX)
    const coinRoom = coinRight - (pad + 4 + Math.ceil(sumLabel.width) + 16)
    const coinStep = many > 1 ? Math.min(COIN_STEP, coinRoom / (many - 1)) : 0
    const coins: Text[] = []
    const coinRest: number[] = []
    for (let i = 0; i < many; i++) {
      const one = new Text({
        text: '$',
        style: { fontSize: TEXT.head, fill: UI.yellow, fontWeight: WEIGHT.bold,
          fontFamily: NUMERALS },
      })
      one.anchor.set(0.5, 0.5)
      const restX = coinRight - (many - 1 - i) * coinStep
      one.position.set(restX, sumTop + 28)
      one.visible = false
      coins.push(one)
      coinRest.push(restX)
      layer.addChild(one)
    }
    // 낱개가 서는 동안 수는 없습니다. **둘이 같이 있으면 뭉치는 것이 아무 뜻도 아닙니다.**
    sumText.alpha = many > 0 ? 0 : 1
    const lastRow = rowAt[rowAt.length - 1] ?? this.game.clock + PAYOUT_WAIT
    const mergeAt = many > 0 ? lastRow + 0.3 : 0
    const readyAt = many > 0 ? mergeAt + COIN_MERGE + 0.14 : lastRow + 0.22

    // **닫기 단추가 없습니다.** 받는 것이 이 판의 전부이고, 그것을 누르는 것이 닫는 것입니다.
    const label = empty ? t('ui.payout.next') : tf('ui.payout.take', { n: sum })
    const take = new Button(label, 240, 48, empty ? 'neutral' : 'primary', () => {
      // **누른 그 자리에서 차례를 지웁니다.** 닫히는 것을 기다리면 그 사이에 다시 뜹니다.
      this.payoutWanted = false
      delete this.game.spots.take
      this.takeSpot = undefined
      this.payoutRows.length = 0
      // **받는 순간에 돈이 단추에서 금액 칸으로 날아가고, 다 닿은 뒤에 판이 닫힙니다.**
      // 판이 먼저 닫히면 동전은 아무 데서도 나오지 않은 것이 되고, 그동안 단추는 「받는 중」
      // 으로 잠깁니다 — 두 번 눌리지 않고, 무엇을 기다리는지가 적힙니다. 닫는 것은
      // `advancePayout` 이 동전이 다 닿은 것을 보고 합니다.
      if (sum !== 0) {
        // **낱개가 있으면 그 자리에서 하나씩 뜹니다.** 뭉쳐 둔 것을 다시 펼치고, 하나씩
        // 사라지면서 그 자리에서 동전이 날아갑니다 — 단추 한 자리에서 열두 개가 함께
        // 뜨는 것은 곧게 그은 선 하나였습니다. 낱개가 없는 판(빚)에서는 앞의 길입니다.
        const bar = this.payoutBar
        if (sum > 0 && bar && bar.coins.length > 0) {
          bar.taking = { at: this.game.clock, share: this.launchShares(sum, bar.coins.length),
                         launched: 0, flights: 0 }
        } else if (sum > 0) {
          const at = layer.toGlobal({ x: take.x + 120, y: take.y + 24 })
          this.coins.fly(sum, this.coins.toLocal(at), this.game.chrome.moneySpot())
        } else this.coins.spend(sum, this.game.chrome.moneySpot())
        take.enabled = false
        take.text = t('ui.payout.taking')
        this.payoutTaking = true
      } else {
        this.game.panels.modals.close(this.panel)
        this.game.refresh()
      }
    })
    take.position.set((width - 240) / 2, buttonTop)
    take.enabled = this.game.clock >= readyAt
    layer.addChild(take)
    this.payoutBar = {
      bar, begin: this.game.clock + PAYOUT_WAIT * 0.5, ratio: target > 0 ? Math.min(1,
        score / target) : 1,
      sum: sumText, shown: 0, poppedAt: -1, rowAt, amounts, take, readyAt,
      coins, coinRest, coinTo: coinRight, mergeAt, merged: many === 0,
    }
  }

  /**
   * 정산 판의 바와 합계를 한 단계 진행합니다.
   *
   * 바는 판이 선 뒤 0.42초에 걸쳐 득점까지 차고, 합계는 줄이 서는 그 순간 그만큼 셉니다 —
   * 셀 때 한 번 커졌다 돌아옵니다.
   */
  private advancePayoutBar(): void {
    const one = this.payoutBar
    if (!one || one.sum.destroyed) return

    // **받는 중.** 뭉쳐 있던 낱개가 다시 펼쳐지고, 하나씩 사라지면서 그 자리에서 동전이
    // 뜹니다 — 바와 합계는 이미 다 셌으므로 여기서 할 일이 없습니다.
    const take = one.taking
    if (take) {
      const back = Math.max(0, Math.min(1, (this.game.clock - take.at) / COIN_SPREAD))
      const eased = back * back * (3 - 2 * back)
      one.sum.alpha = 1 - eased
      one.coins.forEach((coin, i) => {
        if (coin.destroyed) return
        // 이미 뜬 것은 없습니다.
        if (i < take.launched) {
          coin.visible = false
          return
        }
        coin.visible = true
        const rest = one.coinRest[i]
        coin.position.x = one.coinTo + (rest - one.coinTo) * eased
        coin.alpha = eased
        coin.scale.set(0.7 + 0.3 * eased)
      })
      // 다 펼친 뒤에 하나씩 뜹니다. **한 프레임에 여럿이 밀려 있으면 그만큼 함께 띄웁니다** —
      // 프레임이 늦은 기계에서 마지막 동전만 남지 않게 합니다.
      if (back >= 1) {
        const due = Math.floor((this.game.clock - take.at - COIN_SPREAD) / COIN_LAUNCH) + 1
        while (take.launched < Math.min(due, one.coins.length)) {
          const i = take.launched++
          const coin = one.coins[i]
          if (coin.destroyed) continue
          coin.visible = false
          const share = take.share[i]
          if (share === 0) continue
          // **낱개가 놓여 있던 자리입니다.** 지금 그린 자리가 아니라 쉬는 자리입니다 —
          // 펼치는 중에 눌리는 일은 없지만, 자리를 묻는 쪽에는 늘 닿을 자리를 답합니다.
          const layer = coin.parent
          if (!layer) continue
          const at = layer.toGlobal({ x: one.coinRest[i], y: coin.y })
          this.coins.one(share, take.flights++, this.coins.toLocal(at),
            this.game.chrome.moneySpot())
        }
      }
      return
    }

    const step = Math.max(0, Math.min(1, (this.game.clock - one.begin) / 0.42))
    one.bar.set(one.ratio * (1 - (1 - step) * (1 - step)))

    let total = 0
    for (let i = 0; i < one.amounts.length; i++) {
      if (this.game.clock >= one.rowAt[i]) total += one.amounts[i]
    }
    if (total !== one.shown) {
      one.shown = total
      one.sum.text = `$${total}`
      // 낱개가 있는 판에서는 뭉치는 그 순간에 한 번 커집니다. 여기서는 셈만 합니다.
      if (one.merged) one.poppedAt = this.game.clock
    }

    // 낱개 — 지금까지 선 줄만큼 보이고, 다 서면 오른쪽 끝으로 뭉칩니다.
    if (one.coins.length > 0) {
      const shown = Math.max(0, Math.min(one.coins.length, total))
      const merge = one.mergeAt <= 0
        ? 0
        : Math.max(0, Math.min(1, (this.game.clock - one.mergeAt) / COIN_MERGE))
      const eased = merge * merge * (3 - 2 * merge)
      one.coins.forEach((coin, i) => {
        coin.visible = i < shown && merge < 1
        if (!coin.visible) return
        const rest = one.coinRest[i]
        coin.position.x = rest + (one.coinTo - rest) * eased
        coin.alpha = 1 - eased * 0.9
        coin.scale.set(1 - 0.3 * eased)
      })
      one.sum.alpha = eased
      // 뭉친 그 순간에 수가 한 번 커집니다. **낱개가 하나로 모인 것이 그 수입니다.**
      if (merge >= 1 && !one.merged) {
        one.merged = true
        one.poppedAt = this.game.clock
        this.game.audio.play('coin_land', 6)
      }
    }

    const pop = one.poppedAt < 0 ? 0 : Math.max(0, 1 - (this.game.clock - one.poppedAt) / 0.18)
    one.sum.scale.set(1 + 0.14 * pop)
    if (one.take.destroyed) return
    const ready = this.game.clock >= one.readyAt
    one.take.enabled = ready
    if (ready && this.takeSpot) this.game.spots.take = this.takeSpot
    else delete this.game.spots.take
  }

  /**
   * 낱개마다 실을 금액. **동전이 뜨지 않는 낱개는 0 입니다.**
   *
   * 동전은 `COIN_FLIGHTS` 까지이고 낱개는 `COIN_MAX` 까지이므로, 많이 받는 판에서는 낱개
   * 몇 개가 동전 없이 사라집니다 — 뜰 것을 줄에 고르게 흩습니다. 앞의 열둘만 쓰면 줄의
   * 왼쪽에서만 동전이 뜨고 오른쪽은 그냥 사라집니다.
   *
   * **몫의 합이 금액과 같습니다.** 나누어지지 않는 나머지는 앞의 것부터 하나씩 더 듭니다 —
   * 마지막 동전이 닿은 잔액이 코어와 같아야 합니다.
   */
  private launchShares(sum: number, many: number): number[] {
    const count = Math.max(1, Math.min(many, COIN_FLIGHTS))
    const base = Math.floor(sum / count)
    const extra = sum - base * count
    const out = new Array<number>(many).fill(0)
    for (let j = 0; j < count; j++) {
      const at = count === 1 ? 0 : Math.round(j * (many - 1) / (count - 1))
      out[at] = base + (j < extra ? 1 : 0)
    }
    return out
  }
}
