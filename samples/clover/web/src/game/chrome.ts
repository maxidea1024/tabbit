import { Container, Graphics, Text } from 'pixi.js'
import { t } from '../core/strings'
import { outlined } from '../ui/font'
import { piece } from '../ui/chrome'
import { ScoreWave } from '../shader/wave'
import { Slot } from '../render/hud'
import { Spring } from '../render/motion'
import { slotStyle, mix } from '../render/skin'
import { TEXT, UI, WEIGHT } from '../render/theme'
import { type Box } from '../ui/layout'
import { ProgressBar } from '../ui/parts'
import { Button, Panel } from '../ui/widgets'
import {
  BOARD_X, BUTTON_Y, CELL_SLOT_H, CELL_SLOT_W, CHIPS_GAP, CHIPS_H, CHIPS_R, CONSUMABLE_TRAY,
  COUNT_PULSE, HAND_HINT_SELECTED_RISE, HAND_INFO_Y, IN_W, JOKER_TRAY, PLAY_H, PLAY_Y,
  SCORE_H, SORT_H, SORT_HIDE,
} from './metrics'
import { boxInk } from './helpers'
import { type Game } from './game'
export class ChromePart {
  constructor(private readonly game: Game) {
    // 소지금은 값 앞에 `$` 가 붙습니다. 정산의 동전과 상점의 값이 같은 표기입니다.
    this.money.prefix = '$'
  }

  /** 고른 것 밑에 서는 버튼들. */
  readonly heldBar = new Container()

  /**
   * 블라인드 딱지 아래의 왼쪽 판.
   *
   * **통째로 내려갑니다.** 딱지가 들고 있는 태그만큼 자라므로 그 아래가 그만큼 밀립니다.
   */
  readonly panelStack = new Container()

  /**
   * 왼쪽 판의 무리를 가르는 줄들.
   *
   * **여섯 칸이 한 덩어리로 보이던 것을 가릅니다.** 사이가 12·30·12로 제각각이라 어느
   * 둘이 한 벌인지가 자리로 드러나지 않았습니다 — 사이를 26으로 맞추고 그 한가운데에 줄을
   * 하나씩 둡니다.
   *
   * 자리가 상수이므로 **한 번 그리고 그대로 둡니다.**
   */
  readonly panelGrooves = new Graphics()

  /**
   * 왼쪽 판의 판때기.
   *
   * **붙들어 둡니다.** 판이 도는 내내 한 번 그리고 마는 것이므로, 옵션에서 겉면을 갈아
   * 끼웠을 때 다시 그려 줄 곳이 필요합니다.
   */
  panelPlate?: Panel

  readonly score = new Slot(t('ui.slot.round_score'), IN_W, SCORE_H, UI.ink)
  /**
   * 라운드 점수 아래의 게이지. **눈금의 끝은 요구 점수가 아닙니다** — 넘긴 만큼이 금색으로
   * 보입니다.
   */
  readonly scoreBar = new ProgressBar(IN_W - 24)

  // **이 둘이 화면에서 가장 큰 두 숫자입니다.** 점수는 이 둘의 곱이고, 나머지 칸들은
  // 그것을 설명하는 것들입니다 — 크기가 그 서열을 그대로 보여야 합니다.
  // 칩은 오른쪽으로, 배수는 왼쪽으로 붙습니다 — 사이의 곱셈표와 함께 한 식으로 읽힙니다.
  /**
   * 고른 것이 무슨 족보인가.
   *
   * **칩과 배수 칸 바로 위입니다.** 그 두 수가 어디서 온 것인지가 바로 위에 적혀 있어야
   * 한 덩어리로 읽힙니다 — 판 가운데에만 띄우면 눈이 왼쪽과 가운데를 오갑니다.
   */
  readonly handLabel = new Text({
    text: '',
    style: {
      // **12픽셀은 작았습니다.** 지금 고른 것이 무슨 족보인가는 화면에서 점수 다음으로
      // 중요한 글이고, 칩과 배수가 어디서 나온 값인지를 잇는 유일한 줄입니다.
      ...outlined(TEXT.big, UI.outline),
      fill: UI.ink, fontWeight: WEIGHT.bold, letterSpacing: 0.5,
    },
  })

  /**
   * 칩 × 배수의 바탕.
   *
   * **판의 다른 칸과 같은 바탕입니다.** 파랑과 붉음 둘로 칠해 두었더니 왼쪽 판에서 이
   * 둘만 다른 문법으로 그려진 물건이었습니다 — 색은 조용할 때가 아니라 값이 바뀔 때
   * 듭니다(`scoreFlash`).
   */
  readonly scoreBox = new Graphics()

  /**
   * 칩 × 배수가 바뀔 때 그 위에 드는 색.
   *
   * **원래 그 상자의 색입니다.** 파랑과 붉음이 사라진 것이 아니라, 늘 켜져 있던 것이
   * 값이 움직이는 동안에만 켜집니다.
   */
  readonly scoreFlash = new Graphics()

  /**
   * 두 상자의 바탕에 흐르는 파형.
   *
   * **번쩍임 위, 숫자 아래입니다.** 위로 올리면 숫자를 덮고, 아래로 내리면 번쩍임이
   * 덮습니다 — 이 순서에서는 배치가 한 번 끊기고, 그것이 이 층의 값 전부입니다.
   *
   * 규격은 `doc/ui/wave.md` 입니다.
   */
  readonly scoreWave = new ScoreWave()

  /** 두 상자가 놓인 자리. 겉면을 갈아입을 때와 번쩍임이 다시 씁니다. */
  scoreBoxes?: { chips: Box; mult: Box }

  /** 마지막으로 그린 번쩍임의 세기 둘. 같으면 다시 그리지 않습니다. */
  private flashChips = -1

  private flashMult = -1

  /**
   * 칩과 배수.
   *
   * **이름이 없습니다.** 두 수 사이에 `×` 가 있으면 그것이 무엇인지 더 적을 것이 없습니다 —
   * 이름은 자리만 잡아먹고 숫자를 아래로 밀어냅니다.
   *
   * **숫자가 그 뜻의 색입니다** — 칩은 파랑, 배수는 붉음. 흰색으로 두었던 동안 두 상자는
   * 바탕의 색으로만 갈렸고, 값이 오를 때 글자마다 지나가는 흰빛(`Digits.advance`)도 흰
   * 글자 위에서는 아무것도 아니었습니다. 제 색에서 흰색으로 갔다가 제 색으로 돌아오는
   * 것이 그 물결이므로, 제 색이 있어야 물결이 보입니다.
   */
  readonly chips =
    new Slot('', (IN_W - CHIPS_GAP) / 2, CHIPS_H, UI.chips, 36, 1, true, true)

  readonly mult =
    new Slot('', (IN_W - CHIPS_GAP) / 2, CHIPS_H, UI.mult, 36, 0, true, true)

  /**
   * 왼쪽 판의 칸들이 마지막으로 보여 준 수.
   *
   * **차이를 적으려면 앞의 값을 들고 있어야 합니다.** 상태에는 지금 값만 있고 「얼마에서
   * 얼마가 되었는가」는 없으므로, 화면이 자기가 보여 준 것을 기억합니다 — 판을 새로 깔면
   * 이것도 함께 되돌립니다.
   *
   * `-1` 은 아직 아무것도 보여 주지 않았다는 뜻이고, 그때는 차이를 적지 않습니다.
   */
  panelShown: { hands: number; discards: number; ante: number; phase: string } =
    { hands: -1, discards: -1, ante: -1, phase: '' }

  readonly hands = new Slot(t('ui.slot.hands'), CELL_SLOT_W, CELL_SLOT_H, UI.good)

  readonly discards = new Slot(t('ui.slot.discards'), CELL_SLOT_W, CELL_SLOT_H, UI.discard)

  readonly money = new Slot(t('ui.slot.money'), CELL_SLOT_W, CELL_SLOT_H, UI.money)

  readonly anteSlot = new Slot(t('ui.slot.ante'), CELL_SLOT_W, CELL_SLOT_H, UI.ink)

  /**
   * 왼쪽 판의 값 칸 전부.
   *
   * **한 곳에 적어 둡니다.** 매 단계 나아가게 하는 곳과 겉면을 갈아입는 곳 둘이 각자
   * 칸을 세어 적고 있었고, 그중 한쪽에서 셋이 빠져 있었습니다 — 칸을 하나 더하는 날에
   * 두 곳을 다 고쳐야 하는 것이 그 원인입니다.
   */
  readonly panelSlots: readonly Slot[] = [
    this.score, this.chips, this.mult,
    this.hands, this.discards, this.money, this.anteSlot,
  ]

  readonly frames = new Graphics()

  /** 덱 더미. 상점에서는 화면 밖으로 밀려 나갑니다. */
  readonly deckLayer = new Container()

  /**
   * 덱 더미.
   *
   * **뒷면이 바뀌면 다시 그립니다.** 판마다 덱이 다르고 덱마다 뒷면이 다르므로, 한 번
   * 그려 놓고 두면 두 번째 판의 더미가 첫 판의 뒷면입니다.
   */
  readonly deckPile = new Container()

  readonly deckSlide = new Spring(0, 150, 20)

  /** 패널 위에 얹는 빛. `panelGlow` 가 세기입니다. */
  readonly panelFlash = new Graphics()

  /**
   * 조커 칸이 몇 칸 찼는가.
   *
   * **「조커」라고 적지 않습니다.** 칸에 조커가 서는 줄이고, 그 줄 아래의 `0 / 5` 는 그
   * 줄에 관한 것 말고 다른 것일 수 없습니다.
   */
  readonly jokerCount = new Text({
    text: '', style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.bold },
  })

  readonly consumableCount = new Text({
    text: '', style: { fontSize: TEXT.small, fill: UI.legendary, fontWeight: WEIGHT.bold },
  })

  readonly deckLabel = new Text({
    text: '', style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
  })

  playButton!: Button

  discardButton!: Button

  /** 고른 것을 한 번에 풉니다. **한 장씩 다시 누르는 것은 일입니다.** */
  clearButton!: Button

  primaryButton!: Button

  skipButton!: Button

  rerollButton!: Button

  sortRankButton!: Button

  sortSuitButton!: Button

  infoButton!: Button

  /**
   * 나머지를 모아 둔 자리.
   *
   * **판 아래의 버튼은 둘입니다.** 넷이 늘어서 있으면 그 자리가 화면에서 가장 복잡한
   * 자리가 되는데, 정작 판을 두는 동안에는 하나도 누르지 않습니다 — 자주 쓰는 족보 목록만
   * 남기고 나머지는 이 안으로 들어갑니다.
   */
  menuButton!: Button

  /**
   * 조커·카드·판돈이 낸 돈이 나오는 자리.
   *
   * **코어는 돈을 둘로 알립니다** — 누가 냈는지(`JokerTriggered`·`CardScored`·`RunTriggered`)와
   * 얼마가 들어왔는지(`MoneyChanged`). 동전은 뒤의 것이 날리고 자리는 앞의 것이 적어 둡니다.
   * 한 효과가 돈을 여러 번 내면 같은 자리에서 여러 번 나오므로 쓴 뒤에도 남겨 두고,
   * 타임라인이 시작할 때 비웁니다.
   */
  moneyFrom!: { x: number; y: number } | undefined

  /** 산 직후 강조되는 칸 수 글. 어느 글이고 언제까지인가. */
  countPulse?: { node: Text; base: number; until: number }

  readonly countScale = new Spring(1, 300, 18)

  /** 덱이 카드를 받을 때 한 번 눌리는 것. 덱 더미의 세로 어긋남입니다. */
  readonly deckBump = new Spring(0, 300, 18)

  /**
   * 이 판의 점수가 이미 정산되었는가. **환희의 문턱을 다시 보지 않기 위한 것입니다.**
   *
   * 정산(`ScoreResolved`) 뒤에도 득점하는 갈래의 박자가 옵니다 — 라운드가 끝날 때 도는
   * 조커가 그것입니다. 그 박자들도 그 판의 칩과 배수를 그대로 들고 있으므로, 문턱을 다시
   * 보면 **터진 뒤에 다시 모으기 시작합니다** — 그러면 겹이 12초(`HOLD_MOST`)를 채울 때까지
   * 남고, 그동안 정산과 상점이 그 위에 뜹니다. 박자의 갈래로만 가리고 있었고, 그 목록에
   * 있는 갈래가 정산 뒤에도 오는 것이 이 자리였습니다.
   */
  scoreSettled = false

  /**
   * 칩과 배수의 상자.
   *
   * **깔끔한 단색 둘입니다.** 숫자가 앉는 자리이므로 그 자리는 조용해야 합니다 —
   * 광택이나 그라디언트를 얹으면 그 위에 앉는 흰 숫자가 자리마다 다른 바탕을 만납니다.
   */
  paintScoreBox(chipsBox: Box, multBox: Box): void {
    this.scoreBoxes = { chips: chipsBox, mult: multBox }
    // **파형이 덮는 사각형은 두 상자와 그 사이를 합친 것입니다.** 상자에서 셈합니다 — 자리를
    // 베껴 적으면 상자의 크기나 사이를 고친 자리에서 이것만 낡습니다.
    this.scoreWave.layout(chipsBox.x, chipsBox.y, chipsBox.width, chipsBox.height,
      multBox.x + multBox.width - chipsBox.x, CHIPS_R)
    this.scoreWave.ink(UI.chips, UI.mult)
    const g = this.scoreBox
    g.clear()
    g.removeChildren().forEach(child => child.destroy())
    const style = slotStyle(UI.ink)
    for (const area of [chipsBox, multBox]) {
      // **칩은 파랑의 어두운 것, 배수는 붉음의 어두운 것입니다.** 값이 움직이지 않을 때도
      // 어느 쪽이 무엇인지가 바탕으로 읽힙니다.
      const tone = area === chipsBox ? UI.chips : UI.mult
      const skin = piece('well', area.width, area.height, mix(tone, UI.ground, 0.72))
      if (skin !== undefined) {
        skin.position.set(area.x, area.y)
        g.addChild(skin)
        continue
      }
      g.rect(area.x, area.y, area.width, area.height)
        .fill(style.top)
      g.rect(area.x + 0.5, area.y + 0.5, area.width - 1, area.height - 1)
        .stroke({ color: style.border, width: 1 })
    }
    this.flashChips = -1
    this.flashMult = -1
    this.paintScoreFlash()
  }

  /**
   * 칩과 배수가 움직이는 동안 그 바탕에 드는 색.
   *
   * **칸마다 따로입니다.** 칩만 오르는 대목과 배수만 곱해지는 대목이 갈려 있고, 둘을 함께
   * 밝히면 어느 쪽이 움직인 것인지가 사라집니다.
   *
   * **세기는 8단계로 끊습니다.** 값이 굴러가는 동안 매 단계 불리므로, 그대로 그리면 초당
   * 60번 다시 삼각화됩니다 — 눈에는 같습니다.
   */
  paintScoreFlash(): void {
    const boxes = this.scoreBoxes
    if (!boxes) return
    const step = (value: number) => Math.round(value * 8) / 8
    const level = { chips: step(this.chips.lit), mult: step(this.mult.lit) }
    // **열쇠는 수 둘입니다.** 문자열로 만들면 초당 60번 문자열 하나가 생깁니다.
    if (level.chips === this.flashChips && level.mult === this.flashMult) return
    this.flashChips = level.chips
    this.flashMult = level.mult

    const g = this.scoreFlash
    g.clear()
    for (const [area, tint, lit] of [
      [boxes.chips, UI.chips, level.chips],
      [boxes.mult, UI.mult, level.mult],
    ] as const) {
      if (lit <= 0) continue
      // 짙게 눌러 씁니다. **원색 그대로는 흰 숫자가 눌러앉지 못합니다.**
      g.rect(area.x, area.y, area.width, area.height)
        .fill({ color: boxInk(tint), alpha: lit })
      // **테는 건드리지 않습니다.** 색을 얹으면 밝은 동안 그 상자만 다른 문법으로 그려진
      // 것이 되고, 값이 굴러가는 내내 테 하나가 색을 바꾸며 굵어졌다 가늘어집니다 —
      // 알릴 것은 바탕 하나로 족합니다.
    }
  }

  /**
   * 조커와 소모품의 자리.
   *
   * **비어 있어도 자리가 보여야 무엇을 모으는 게임인지 압니다.** 그러나 칸을 하나씩
   * 그리지는 않습니다 — 칸 수는 규칙이 정하는 값이고, 자리는 그것과 무관하게 늘 같은
   * 사각형이어야 합니다.
   *
   * **한 번만 그립니다.** 규칙에 따라 달라지는 것이 없어졌으므로 `refresh` 마다 다시
   * 삼각화할 이유가 없습니다.
   */
  drawFrames(): void {
    const g = this.frames
    g.clear()

    // **바탕만 깔고 테는 두지 않습니다.** 이 자리에 서는 것은 카드이고 카드마다 자기 테가
    // 있으므로, 자리에도 테를 두르면 테가 두 겹으로 겹칩니다 — 비어 있는 자리를 알리는 데는
    // 한 단 밝은 바탕으로 족합니다.
    g.removeChildren().forEach(child => child.destroy())
    for (const tray of [JOKER_TRAY, CONSUMABLE_TRAY]) {
      // **칸을 하나씩 그리지 않습니다. 고정된 영역 하나입니다** — 칸 수를 덱·바우처·
      // 챌린지가 바꾸므로, 칸마다 그리면 줄의 너비가 규칙을 따라 달라집니다.
      // **반투명입니다.** 자리는 바탕이고, 그 뒤의 무늬가 비쳐야 판 위에 파인 자리로 읽힙니다.
      const skin = piece('tray', tray.width, tray.height, UI.inkDim)
      if (skin !== undefined) {
        skin.position.set(tray.x, tray.y)
        // 빈 칸은 전체 면적만 알립니다. 밝은 테나 슬롯 구획 없이 프랙탈이 비치는 재질 한
        // 겹이고, 카드가 놓이면 카드가 이 면을 자연스럽게 덮습니다.
        skin.alpha = 0.14
        g.addChild(skin)
        continue
      }
      g.rect(tray.x, tray.y, tray.width, tray.height).fill({ color: UI.panel, alpha: 0.5 })
    }
  }

  /**
   * 코어에는 들어갔지만 화면에는 아직 닿지 않은 돈.
   *
   * **날고 있는 동전의 몫과 정산 판에 올라 있는 줄입니다.** 화면의 잔액은 이것을 뺀 값에서
   * 시작해야 동전이 닿을 때마다 그만큼 오르고, 마지막 동전이 닿으면 코어와 같아집니다.
   */
  moneyInFlight(): number {
    return this.game.payout.coins.pending + this.game.payout.payoutRows.reduce((sum, row) => sum
        + row.amount, 0)
  }

  primary(): void {
    this.game.audio.play(this.game.state.phase === 'blind-select' ? 'blind_select' : 'shop_enter')
    if (this.game.state.phase === 'blind-select') this.game.act({ t: 'select_blind' })
    else if (this.game.state.phase === 'shop') this.game.act({ t: 'leave_shop' })
  }

  /** 한 방. 흔들림과 색수차를 함께 겁니다. */
  /**
   * 패널을 번쩍입니다.
   *
   * **왼쪽의 숫자들이 바뀌는 자리를 파티클로 알리면 숫자를 가립니다.** 패널 자체가 빛나면
   * 무엇이 바뀌었는지가 가려지지 않고 눈에 들어옵니다.
   */
  /** 금액이 왜 바뀌었는지를 띄울 자리. 판 가운데입니다. */
  moneyLabelAnchor(): { x: number; y: number } {
    // 낸 카드 아래입니다. **카드 위에 겹치면 흰 종이에 흰 글씨가 됩니다.**
    return { x: BOARD_X, y: PLAY_Y + 200 }
  }

  /** 금액 칸의 숫자 한가운데. 동전이 여기로 꽂힙니다. **동전 층의 좌표입니다.** */
  moneySpot(): { x: number; y: number } {
    return this.game.payout.coinSpot(this.money, this.money.valueMiddle)
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
  drawPips(): void {
    const picked = this.game.cards.selected.size
    // 켜고 끄는 것은 `syncButtons` 가 정합니다 — 여기는 적는 것만 합니다.
    this.clearButton.text = picked === 0
      ? '-'
      : `${picked} / ${this.game.data.run.maxPlayedCards}`
  }

  syncButtons(): void {
    const state = this.game.state
    const inRound = state.phase === 'round'

    // **낸다 · 취소 · 버린다는 자리로 숨습니다.** 정렬 단추와 한 무리이고, 오르내리는 것은
    // `advanceHandControls` 가 매 프레임 정합니다.
    this.playButton.visible = this.game.session.scene === 'run'
    this.discardButton.visible = this.game.session.scene === 'run'
    // **취소 단추는 두지 않습니다.** 고른 카드를 다시 누르면 놓이고, 낸다와 버린다 사이에
    // 세 번째 단추가 서면 나아가는 줄이 셋으로 갈립니다.
    this.clearButton.visible = false
    this.clearButton.enabled = inRound && this.game.cards.selected.size > 0
    this.playButton.enabled = inRound && this.game.cards.selected.size > 0 && state.handsLeft > 0
    this.discardButton.enabled = inRound && this.game.cards.selected.size > 0
      && state.discardsLeft > 0

    // **가운데 버튼이 없습니다.** 블라인드 선택은 판마다 자기 버튼을 가지고, 상점은 판의
    // 밑단에 자기 버튼을 가집니다 — 어느 쪽이든 누를 것이 그 판 안에 있습니다.
    this.primaryButton.visible = false
    this.skipButton.visible = false
    // **정렬 단추는 자리로 숨습니다.** 보이고 안 보이고가 아니라 올라와 있고 내려가 있는
    // 것이고, 그것은 `advanceSortButtons` 가 매 프레임 정합니다.
    this.sortRankButton.visible = this.game.session.scene === 'run'
    this.sortSuitButton.visible = this.game.session.scene === 'run'
    // **끝난 판에서는 걷는 것이 아니라 끕니다.** 없애 버리면 왼쪽 판의 밑단이 통째로 비어
    // 판이 그리다 만 것으로 보입니다 — 자리는 그대로 두고 눌리지 않게만 합니다. 그 자리에
    // 무엇이 있었는지가 남고, 지금 누를 것이 아니라는 것도 함께 읽힙니다.
    const playing = this.game.shown.phase !== 'lost' && this.game.shown.phase !== 'won'
    this.infoButton.enabled = playing
    this.menuButton.enabled = playing
    // 남은 카드는 판이 도는 동안만 뜻이 있습니다. 덱을 눌러 엽니다.
    if (this.game.state.phase !== 'round') this.game.panels.modals.close(this.game.cards.deckView)
    // 리롤도 상점 판의 밑단에 있습니다.
    this.rerollButton.visible = false
  }

  /**
   * 손패를 다루는 것들의 자리. 쓸 수 있을 때 올라와 있고, 아니면 화면 아래로 내려가 있습니다.
   *
   * **쓸 수 있다는 것은** 라운드 중이고 · 손패가 다 깔렸고 · 연출이 돌지 않는 것입니다.
   * 깔리는 중에 올라오면 아직 없는 카드를 고르라는 단추가 먼저 놓여 있고, 득점 연출이 도는
   * 동안은 눌러도 아무 일이 없습니다 — 둘 다 그동안은 내려가 있습니다.
   *
   * 낸다 · 취소 · 버린다 · 정렬 둘 · 지시문이 함께 움직입니다. **같은 맥락의 것입니다** —
   * 정렬만 내려가고 낸다가 남아 있으면 둘이 다른 물건으로 보입니다.
   */
  advanceHandControls(seconds: number): void {
    const usable = this.game.handReady
    this.game.cards.sortSlide.target = usable ? 0 : SORT_HIDE
    this.game.cards.sortSlide.advance(seconds)
    const off = this.game.cards.sortSlide.value
    const sortY = BUTTON_Y + (PLAY_H - SORT_H) / 2 + off
    this.sortRankButton.y = sortY
    this.sortSuitButton.y = sortY
    this.sortRankButton.enabled = usable && this.game.shown.hand.length > 1
    this.sortSuitButton.enabled = usable && this.game.shown.hand.length > 1
    this.playButton.y = BUTTON_Y + off
    this.clearButton.y = BUTTON_Y + off
    this.discardButton.y = BUTTON_Y + off
    // **지시문은 손패 위입니다.** 손패와 단추 줄 사이에 두었던 동안 그 줄은 카드 밑의
    // 점들과 겹쳤습니다. 카드를 고르면 큰 족보 이름과 함께 뜨므로 그때만 한 줄 위로
    // 물러나고, 조작 무리가 숨을 때는 둘이 함께 아래로 내려갑니다.
    const selectedRise = this.game.cards.selected.size > 0 ? HAND_HINT_SELECTED_RISE : 0
    this.game.input.hint.y = HAND_INFO_Y - selectedRise + off
  }

  /**
   * 칸 수(`5 / 5`)를 잠깐 강조합니다. 산 것이 닿는 자리에서 부릅니다.
   *
   * **커지고 값의 색이 되어 머물다가 돌아옵니다.** 한 번 깜박이는 것은 놓치기 쉽고, 이
   * 게임은 실시간 액션이 아니므로 읽을 만큼 머물러도 됩니다.
   */
  pulseCount(node: Text): void {
    if (this.countPulse && this.countPulse.node !== node) this.endCountPulse()
    if (!this.countPulse) {
      this.countPulse = { node, base: node.style.fill as number, until: this.game.clock
          + COUNT_PULSE }
    } else this.countPulse.until = this.game.clock + COUNT_PULSE
    node.style.fill = UI.money
    this.countScale.target = 1.28
    this.countScale.kick(4)
  }

  private endCountPulse(): void {
    const one = this.countPulse
    if (!one) return
    one.node.style.fill = one.base
    one.node.scale.set(1)
    this.countPulse = undefined
    this.countScale.snap(1)
  }

  advanceCountPulse(seconds: number): void {
    const one = this.countPulse
    if (!one) return
    // 마지막 0.3초에 제 크기로 돌아옵니다. 색은 그때까지 값의 색입니다.
    if (this.game.clock > one.until - 0.3) this.countScale.target = 1
    this.countScale.advance(seconds)
    one.node.scale.set(this.countScale.value)
    if (this.game.clock >= one.until && this.countScale.settled) this.endCountPulse()
  }

  /** 「적용 중」의 밝은 줄이 숨 쉬듯 오르내립니다. 때가 지나면 다음 그리기에 걷힙니다. */
  advanceActiveGlow(): void {
    const one = this.game.panels.activeGlow
    if (!one) return
    if (this.game.clock >= one.until) {
      this.game.panels.activeGlow = undefined
      this.game.panels.syncActive()
      return
    }
    if (one.plate && !one.plate.destroyed) {
      one.plate.alpha = 0.55 + 0.45 * Math.sin(this.game.clock * 4.2)
    }
  }
}
