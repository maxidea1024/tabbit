import { Container, Graphics, Sprite, Text, Texture } from 'pixi.js'
import { BlindKind } from '../generated/enums/blind-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { valueText } from '../core/describe'
import { nameOf, t, tf } from '../core/strings'
import { NUMERALS, outlined } from '../ui/font'
import { type GameEvent } from '../core/state'
import { FrontFilter } from '../shader/front'
import { PunchFilter } from '../shader/punch'
import { ladder, type ToneName } from '../feedback/audio'
import { Slot } from '../render/hud'
import {
  type Beat, buildTimeline, particlesOf, scaleOf, semitonesOf, shakeOf, TURN_BACK_MS,
} from '../render/juice'
import { Euphoria } from '../render/euphoria'
import { Haptics } from '../feedback/haptics'
import { Motion, Spring } from '../render/motion'
import { Particles } from '../render/particles'
import { MotesLayer } from '../render/motes-layer'
import { packInk, packInkLit, packName } from '../render/faces'
import { SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { box, CENTER, pointOf, putText, splitX } from '../ui/layout'
import { Button, Panel } from '../ui/widgets'
import { RuleBanner, type RuleNote } from '../ui/rule-banner'
import {
  ACTIVE_GLOW, BLIND_MUSIC_DIM, BLUR_BACK_PX, BLUR_PX, BOARD_X, BUTTON_Y, CHIPS_GAP, CHIPS_H, CHIPS_Y, CONSUMABLE_TRAY, DEALER, DECK_X, DECK_Y, DELTA_LIFE, DELTA_POOL, EMBER, HAND_Y, JOKER_TRAY, JOKER_Y, LAND_AT, LEFT, PACK_TITLE_Y, PACK_X, PANEL_ROWS, PANEL_W, PLAY_H, PLAY_W, PLAY_Y, RIGHT_COL, RISER_HOLD, RISER_LIFT, RISER_ON_CARD, RISER_SPAN, SELL_WAIT, TRAY_PAD_X, within, IN_X, IN_W, SCORE_H,
} from './metrics'
import { ACT_KINDS, ACT_LOOK, moneyReason, ruleChange, SCORING_BEATS, VALUE_OPS } from './tables'
import { edgeBlur, rgbOf } from './helpers'
import { type Riser } from './types'
import { type Game } from './game'
/** 곱셈표. 어느 말에서나 같은 기호입니다. */
const TIMES_SIGN = String.fromCharCode(0xd7)

export class ShowPart {
  constructor(private readonly game: Game) {}

  readonly backdrop = new Container()

  /** 배경을 칠하는 흰 판. 창 크기를 그대로 받습니다. */
  readonly sheet = new Sprite(Texture.WHITE)

  /**
   * 환희의 순간에 배경을 대신하는 겹.
   *
   * **배경 위에 얹힙니다.** 프랙탈을 갈아치우는 것이 아니라 그 위로 올라오므로 넘어가는
   * 것이 보이고, 겹이 없는 동안에는 스프라이트가 꺼져 있어 필터가 돌지 않습니다.
   */
  readonly euphoria = new Euphoria()

  /**
   * 판이 떠 있을 때 뒤로 물러나는 것들.
   *
   * **판 뒤가 흐려져야 판이 앞에 있는 것으로 보입니다.** 어둡게만 덮으면 뒤의 글자가 읽히는
   * 채로 어두워질 뿐이고, 눈이 자꾸 뒤로 갑니다. 흐림은 이 통 하나에 걸립니다 — 판과 설명
   * 쪽지는 이 통 밖이라 또렷하게 남습니다.
   */
  readonly recede = new Container()

  /**
   * 판 뒤를 흐리는 필터 둘.
   *
   * **반 해상도로 굽습니다.** 흐림은 화면 전체를 그림으로 한 번 구워 여섯 번 지나가는 것이고,
   * 흐린 그림은 해상도를 낮춰도 흐린 그림입니다 — 텍셀이 4분의 1이면 그 여섯 번이 4분의 1
   * 값입니다. 반지름은 텍셀 단위라 반으로 적어야 화면에서 같은 크기입니다.
   *
   * **가장자리 픽셀을 늘려 씁니다.** 이것이 판이 열리고 닫힐 때 화면이 한 번씩 어긋나던
   * 까닭입니다 — `repeatEdgePixels` 가 꺼져 있으면 Pixi 가 흐림의 여백을 반지름의 두 배로
   * 잡고, 그 여백이 정수로 잘려 들어갑니다(`padding | 0`). 반지름이 0에서 1.5로 오르는
   * 동안 여백이 0 · 1 · 2 · 3 으로 뚝뚝 넘어가고, 굽는 자리가 그때마다 커집니다.
   *
   * 게다가 그 자리는 **반 해상도의 텍셀 격자에 맞춰 잘립니다**(`scale(0.5).ceil()`). 자리가
   * 바뀌면 격자에 맞추는 자리도 바뀌므로 화면 전체가 최대 2픽셀 옮겨 그려집니다 — 뜰 때
   * 한 번, 사라질 때 한 번 어긋나던 것이 그것입니다.
   *
   * 여백을 0으로 두면 흐림이 자리 밖에서 투명한 검정을 끌어오는데, 여기서 흐리는 것은
   * 화면 전체이므로 **밖에서 끌어올 것이 애초에 없습니다.** 가장자리 픽셀을 늘려 쓰는 것이
   * 맞는 답입니다.
   */
  readonly blur = edgeBlur()

  /** 배경도 함께 흐립니다. **필터 하나를 둘에 걸지 않습니다** — 같은 프레임에 두 번 쓰입니다. */
  readonly blurBack = edgeBlur()

  /** 지금 흐린 정도. 판이 열리고 닫힐 때 잦아듭니다. */
  private blurShown = 0

  /**
   * 로그인 화면의 진행 띠가 배경을 흐리는 정도.
   *
   * **그 화면이 정하고 여기가 겁니다.** 배경은 씬들이 함께 쓰는 판 한 장이라 로그인
   * 화면 안에서 만질 수 있는 것이 아닙니다.
   */
  frontHaze = 0

  /**
   * 흐림을 굽는 해상도. `layout` 이 화면의 픽셀 밀도에서 냅니다.
   *
   * **반지름을 셈할 때 씁니다.** 필터의 `resolution` 은 `'inherit'` 일 수도 있는 값이라
   * 그것으로 곱셈을 할 수 없습니다.
   */
  blurDensity = 0.5

  /**
   * 판에 들어가기 전의 배경.
   *
   * **로그인 화면과 타이틀에서만 돕니다.** 프랙탈은 카드 뒤에 깔릴 것이라 대비가 낮고
   * 어두운데, 카드가 없는 화면에서는 그 어두움이 화면 전체가 됩니다 — 그 두 화면은
   * 이름과 단추 몇 개가 전부이므로 배경이 곧 화면입니다.
   *
   * **둘 중 하나만 그립니다.** 이것이 보이는 동안 프랙탈의 스프라이트는 꺼져 있습니다 —
   * 덮여서 보이지 않는 화면 한 장을 매 프레임 셰이더로 굽는 것이므로.
   */
  readonly front = new FrontFilter()

  readonly frontSheet = new Sprite(Texture.WHITE)

  readonly particles = new Particles()

  /**
   * 삭아 없어지는 판에서 풀려 나간 모래알.
   *
   * **판보다 오래 살아야 하므로 층이 따로 있습니다.** 판이 다 삭으면 그 카드는 그 프레임에
   * 지워지고, 알갱이는 그 뒤로 1초 남짓 더 흩어집니다.
   */
  readonly motes = new MotesLayer()

  /**
   * 진동.
   *
   * **폰에만 있는 채널이고, 소리와 같은 자리에서 나지 않습니다** — 소리는 무엇이
   * 일어났는지 낱낱이 알리지만 진동은 중요한 순간 여섯에만 납니다. 진동자가 없는
   * 기계에서는 이 객체가 아무것도 하지 않고, 옵션의 「입력」 탭도 나오지 않습니다.
   */
  readonly haptics = new Haptics()

  /**
   * 날아가는 칩.
   *
   * **칩이 숫자로만 오르면 무엇이 얼마를 낸 것인지 남지 않습니다.** 카드가 낸 칩은 그
   * 카드에서 칩 칸으로 날아가고, 액면마다 색이 다르므로 개수와 색이 곧 얼마인지입니다.
   */
  readonly punch = new PunchFilter(SIZE.width, SIZE.height)

  /**
   * 판 위로 나와 바뀌는 것을 보이는 중인 카드들.
   *
   * 규격은 [가진 것이 바뀌는 것의 연출](../../doc/presentation/state-change.md) 입니다.
   */
  /**
   * 규칙이 바뀐 것을 알리는 판. **손패 줄 바로 위 가운데입니다.**
   *
   * 조커가 걸고 소모품이 걸고 보스가 거는 규칙은 그 판의 셈법을 통째로 바꾸는 것이므로,
   * 화면 오른쪽 구석의 토스트로 보내지 않습니다.
   */
  readonly ruleBanner = new RuleBanner()

  /**
   * 화면이 그린 박자들. **새것이 뒤입니다.**
   *
   * **검증 도구가 무엇이 그려졌는지를 묻는 자리입니다.** 소리와 떠오른 글로는 갈리지
   * 않는 것이 있습니다 — 조커의 판이 갈리는 것과 능력을 빌리는 것은 같은 소리를 내므로,
   * 소리로 재면 둘 중 어느 것이 돈 것인지 알 수 없습니다.
   */
  readonly beatLog: string[] = []

  /**
   * 떠오르는 차이 글의 풀.
   *
   * 안 보이는 것이 노는 것입니다 — 따로 표를 두면 그 표와 화면이 어긋날 수 있고, 어긋나면
   * 보이는 글을 다시 쓰거나 노는 글이 영영 노는 채로 남습니다.
   */
  readonly deltas: { node: Text; life: number; homeY: number }[] = []

  readonly headline = new Text({
    text: '',
    style: {
      ...outlined(TEXT.hero, UI.outline),
      fill: UI.ink, fontWeight: WEIGHT.bold,
    },
  })

  /** 화면 전체에 얹는 빛. */
  readonly screenFlash = new Graphics()

  /** 머리글이 얼마나 남았는가. **계속 떠 있으면 지난 일이 지금 일처럼 보입니다.** */
  headlineLife = 0

  private headlineSpan = 1

  /**
   * 점수가 굴러가는 동안 나는 소리의 시계.
   *
   * **숫자가 굴러가는 1초가 무음이었습니다.** 재어 보면 낸 뒤 8초 동안 소리가 14번이고
   * 그 사이에 1089밀리초가 비었는데, 비는 그 자리가 칩과 배수가 곱해져 점수가 올라가는
   * 바로 그 순간입니다.
   */
  ratchet = 0

  /** 곱해지기를 기다리는 동안 얼마나 조여들었는가. 0 에서 1 로 갑니다. */
  build = 0

  /** 예약해 둔 소리. 음이 하나씩 올라가는 아르페지오를 이것으로 냅니다. */
  readonly chimes: { at: number; cue: string; semitones: number }[] = []

  /**
   * 예약해 둔 음. **음원 없이 음 하나씩입니다.**
   *
   * 상점의 진열이 여기 쓰던 `card_place` 는 0.689초라 꼬리가 다음 물건까지 남았고, 일곱
   * 번이 같은 음이었습니다. 진열은 물건이 하나씩 놓이는 것이므로 소리도 하나씩 올라가야
   * 합니다.
   *
   * **오르는 것을 음원으로 내지 않습니다.** 음원의 음높이는 재생 속도이고 그것은 3반음이
   * 상한이라(`SAMPLE_TILT_MOST`), 여섯 계단을 음원으로 내면 여섯이 거의 같은 음입니다 —
   * 격파의 여섯 음이 실제로 그랬고, 0.35초 안에 같은 음원 여섯이 겹쳐 겹침 계수기가 그중
   * 둘을 물러나게 했습니다.
   */
  notes: {
    at: number; name: ToneName; step: number; strength: number; pan: number
    /** 다음 음까지의 간격. **밀린 소절을 다시 벌리는 데 씁니다.** */
    gap: number
  }[] = []

  shake = 0

  /**
   * 히트스톱. **때린 순간 시간이 잠깐 멈추면 그 한 방이 무거워집니다.**
   *
   * 멈추는 것은 연출의 시계뿐입니다 — 용수철과 파티클은 계속 움직여야 화면이 얼어붙은 것처럼
   * 보이지 않습니다.
   */
  freeze = 0

  /** 이번 득점에서 몇 번째 사건인가. 화면의 세기가 이것으로 올라갑니다. */
  chain = 0

  /**
   * 이번 득점에서 소리가 오른 칸 수. **`chain` 과 따로 셉니다.**
   *
   * 사건 수를 그대로 음높이로 쓰던 것을 나눴습니다 — 큰 사건은 두 칸을 오르고 작은 것은
   * 한 칸을 오르므로, 세는 것과 오르는 것이 같은 수가 아닙니다. **되돌아가지 않습니다**:
   * 한 번 오른 칸이 다음 사건에서 내려가면 오르는 것으로 들리지 않습니다.
   */
  rung = 0

  /** 왼쪽 패널의 번쩍임. 숫자가 바뀌는 자리를 파티클 대신 이것이 알립니다. */
  panelGlow = 0

  private panelTint: number = UI.ink

  /** 화면 전체의 번쩍임. 큰 것에만 씁니다. */
  screenGlow = 0

  private panelDrawn = false

  /** 마지막으로 그린 패널 번쩍임의 색. 밝기는 알파로 따로 갑니다. */
  private panelKey = -1

  /** 마지막으로 그린 화면 번쩍임의 색. */
  private screenKey = -1

  private screenDrawn = false

  private screenTint: number = UI.ink

  /** 떠오르는 글자들. `popAt` 이 만들고 고정 단계가 올립니다. */
  private readonly risers: Riser[] = []

  /** 배경이 지금 그리고 있는 열기. 목표로 천천히 따라갑니다. */
  heatShown = 0.1

  buildPanel(): void {
    // **판은 16 · 32 에서 시작하고 물건 자리의 윗변과 같습니다.**
    const panel = new Panel(PANEL_W, SIZE.height - 32 - 12)
    this.game.chrome.panelPlate = panel
    panel.position.set(LEFT, 32)
    // **조커와 소모품의 자리는 상점 아래에 그립니다.** 상점이 판 안에 서므로, 이 사각형이
    // 위에 있으면 상점의 머리띠를 가로질러 자리가 그려집니다.
    this.game.chrome.frames.zIndex = -2
    this.game.board.addChild(panel, this.game.chrome.frames)
    // **한 번만 그립니다.** 규칙에 따라 달라지는 것이 없으므로 `refresh` 가 다시 부를
    // 이유가 없습니다.
    this.game.chrome.drawFrames()

    this.game.blind.badge.position.set(LEFT, 32)
    this.game.chrome.score.position.set(IN_X, PANEL_ROWS.score)
    // 게이지는 칸의 아랫변 안쪽입니다.
    this.game.chrome.scoreBar.position.set(IN_X + 12, PANEL_ROWS.score + SCORE_H - 14)
    // **자원 넷은 오르내림이 바탕색에 드러납니다.** 라운드 득점과 칩·배수는 오르기만 하므로
    // 그 색이 아무것도 가르지 않습니다.
    //
    // **넷 다 바탕이 물들지 않습니다.** 늘고 주는 것이 ±N 글로 이미 적히고, 그 위에
    // 바탕까지 물들면 판이 도는 동안 왼쪽 판의 칸 넷이 번갈아 밝습니다 — 그러면 밝은
    // 것이 무엇도 가리지 않습니다.
    //
    // **`signed` 를 걷는 것만으로는 모자랐습니다.** 그 값은 오르내림의 색을 정할 뿐이고,
    // 판때기가 밝아지는 것은 숫자가 굴러가는 동안의 열기가 정합니다 — 색만 빠지고 밝기는
    // 그대로였습니다.
    for (const slot of [this.game.chrome.hands, this.game.chrome.discards,
      this.game.chrome.money, this.game.chrome.anteSlot]) {
      slot.plateGlow = false
    }
    // **칩과 배수는 오를 때만 들뜹니다.** 판이 끝나 0으로 되돌아가는 것은 알릴 일이
    // 아닌데도 바탕이 밝고 숫자가 떨었습니다.
    this.game.chrome.chips.quietOnDrop = true
    this.game.chrome.mult.quietOnDrop = true
    // **네 무리이고 사이가 26입니다.** 이 넷은 판이 도는 동안 가끔 보는 것이고 칩과 배수는
    // 매 순간 보는 것인데, 사이가 12·30·12로 제각각이면 여섯 칸이 한 덩어리로 보여서
    // 그중 어느 둘이 지금 중요한지가 자리로 드러나지 않습니다.
    this.game.chrome.hands.position.set(IN_X, PANEL_ROWS.hands)
    this.game.chrome.discards.position.set(RIGHT_COL, PANEL_ROWS.hands)
    this.game.chrome.money.position.set(IN_X, PANEL_ROWS.money)
    this.game.chrome.anteSlot.position.set(RIGHT_COL, PANEL_ROWS.money)

    // **무리를 가르는 줄을 두지 않습니다.** 무리는 사이의 넓이가 가릅니다 — 줄까지 두면
    // 판 안에 선이 셋 늘고, 그 선들이 웹 화면의 인상을 만듭니다.

    // **상자 둘과 그 사이의 곱셈표입니다.** 원작의 배치이고, 붙여 놓는 것보다 이 편이
    // 「칩 곱하기 배수」 라는 식으로 읽힙니다.
    const block = box(IN_X, CHIPS_Y, IN_W, CHIPS_H)
    const [chipsBox, gapBox, multBox] =
      splitX(block, [1, CHIPS_GAP / (IN_W - CHIPS_GAP) * 2, 1])
    this.game.chrome.paintScoreBox(chipsBox, multBox)
    this.game.chrome.chips.position.set(chipsBox.x, chipsBox.y)
    this.game.chrome.mult.position.set(multBox.x, multBox.y)

    // **곱셈표는 글자입니다.** 물마루가 `×` 를 들고 있고, 말마다 글꼴이 갈리던 자리에서
    // 한글·라틴은 한 글꼴이 되었습니다 — 그림으로 그리면 두 칸의 숫자와 획의 굵기가
    // 다른 물건이 됩니다.
    //
    // **두 상자 사이의 한가운데입니다.** 식의 연산자이므로 어느 상자에도 속하지 않는
    // 것이 맞습니다.
    const times = new Text({
      text: TIMES_SIGN, style: { fontSize: TEXT.head, fill: UI.ink, fontWeight: WEIGHT.bold },
    })
    times.anchor.set(0.5)
    const seam = pointOf(gapBox, CENTER)
    times.position.set(seam.x, seam.y)

    // 족보 이름. **칩 × 배수 바로 위입니다.**
    //
    // 위에서 아래로 라운드 점수 · 족보 이름 · 칩 × 배수 순서이고, 눈이 한 번 내려오면서
    // 「이 판은 무슨 족보이고, 값은 이것이다」 로 읽힙니다.
    //
    // **두 무리의 한가운데가 아니라 아래 무리의 머리입니다.** 사이의 한가운데에 두었더니
    // 위의 점수와 아래의 두 수 어느 쪽에도 붙지 않은 글 한 줄이 되었습니다 — 이 글이
    // 설명하는 것은 아래의 두 수이므로, 그 상자와 8픽셀을 두고 붙습니다.
    // **족보 이름은 판 안이 아니라 손패 위에 뜹니다.** 고른 카드 바로 위에서 무엇을 만들었는지가
    // 읽혀야 하고, 판 안에 두면 눈이 왼쪽으로 한 번 가야 합니다.
    putText(this.game.chrome.handLabel,
            box(BOARD_X - 200, HAND_Y - SIZE.cardHeight / 2 - 52, 400, 24), CENTER)

    // **딱지 아래의 것들은 한 통에 담습니다.** 블라인드 딱지는 들고 있는 태그만큼 자라고,
    // 그러면 그 아래가 통째로 내려가야 합니다 — 낱개로 자리를 다시 세면 여섯 곳을 고쳐야
    // 하고 그중 하나를 빠뜨리면 그것만 겹칩니다.
    //
    this.game.chrome.panelStack.addChild(this.game.chrome.panelGrooves, this.game.chrome.score,
      this.game.chrome.scoreBar, this.game.chrome.scoreBox,
      this.game.chrome.scoreFlash, this.game.chrome.scoreWave.view, this.game.chrome.chips,
      this.game.chrome.mult, times, this.game.chrome.handLabel,
      this.game.chrome.hands, this.game.chrome.discards, this.game.chrome.money,
      this.game.chrome.anteSlot)
    this.game.board.addChild(this.game.blind.badge, this.game.chrome.panelStack)

    // 가운데에서 커집니다. 위쪽을 붙잡고 키우면 글씨가 아래로 자라 보입니다.
    this.headline.anchor.set(0.5, 0.5)
    this.headline.position.set(BOARD_X, 214)

    // **자리의 바깥쪽 끝에 붙입니다** — 조커는 왼쪽, 소모품은 오른쪽입니다. 가운데에
    // 두면 카드가 가운데로 모이므로 글과 카드가 한 줄에 겹쳐 읽힙니다.
    //
    // **자리 위입니다.** 아래에 두었더니 고른 것 밑에 서는 「쓴다 · 판다」가 그 글을
    // 덮었습니다 — 그 단추는 카드 아래 가운데에 서고 화면 안으로 당겨지므로, 소모품
    // 줄의 끝 칸을 고르면 단추 줄의 오른쪽 끝이 바로 그 글의 자리입니다.
    // **개수는 자리 아래입니다.** 위에 두면 그 글이 자리의 머리처럼 붙어 판의 윗변보다
    // 위로 올라가고, 그러면 왼쪽 판과 윗변을 맞춘 뜻이 없어집니다.
    const countY = JOKER_TRAY.y + JOKER_TRAY.height + 6
    this.game.chrome.jokerCount.anchor.set(0, 0)
    this.game.chrome.jokerCount.position.set(JOKER_TRAY.x + TRAY_PAD_X, countY)
    this.game.chrome.consumableCount.anchor.set(1, 0)
    this.game.chrome.consumableCount.position.set(
      CONSUMABLE_TRAY.x + CONSUMABLE_TRAY.width - TRAY_PAD_X, countY)

    this.game.chrome.deckLabel.anchor.set(0.5, 0)
    this.game.chrome.deckLabel.position.set(DECK_X, DECK_Y + 76)

    const pile = this.game.chrome.deckPile
    this.game.cards.drawDeckPile()

    // **지시문은 누를 버튼 바로 위입니다.** 패널 아래에 두면 눈이 화면 왼쪽 끝까지 갔다
    // 와야 하고, 정작 누를 것은 가운데에 있습니다.
    this.game.input.hint.position.set(BOARD_X, BUTTON_Y - 30)

    // **덱은 판이 도는 동안만 화면에 있습니다.** 상점에서는 오른쪽으로 밀려 나가고,
    // 다음 블라인드로 가면 다시 들어옵니다 — 상점의 물건과 자리를 다투지 않습니다.
    // **덱을 누르면 남은 카드가 보입니다.** 그것을 여는 버튼을 따로 두면 판 아래가
    // 복잡해지고, 정작 눌러야 할 것은 화면에 이미 그려져 있습니다.
    pile.eventMode = 'static'
    pile.cursor = 'pointer'
    // 꾸욱 눌러 설명을 본 것이면 열지 않습니다. 누름을 다루는 자리마다 `ate` 를 먼저
    // 물어봅니다 — 그러지 않으면 설명을 보려던 손가락이 그대로 판을 엽니다.
    pile.on('pointertap', () => {
      if (this.game.input.ate()) return
      this.game.cards.toggleDeckView()
    })
    this.game.input.tipOn(pile, at => {
      this.game.input.tooltip.show(t('ui.button.deck_view'), '', 0, [t('ui.deck.tip')], at, SIZE)
    })

    this.game.chrome.deckLayer.addChild(pile, this.game.chrome.deckLabel)

    // **고른 것의 단추는 판이 아니라 그 위의 층입니다.** 판에 두면 뜯은 팩이 판 전체를
    // 덮으므로 그 팩에서 집는 단추가 자기가 덮은 것 뒤로 들어갑니다.
    // **딱지 아래입니다.** 줄이 딱지 위로 지나가면 그림을 가리고, 그러면 이어져 있다는
    // 것보다 무엇이 그어져 있다는 것이 먼저 읽힙니다.
    this.game.cards.borrowLink.zIndex = -2
    this.game.board.addChild(this.game.cards.borrowLink)
    this.game.board.addChild(this.game.chrome.deckLayer, this.headline,
      this.game.chrome.jokerCount,
      this.game.chrome.consumableCount, this.game.tray.consumableLayer, this.game.tray.tagLayer,
      this.game.tray.activeLayer,
      this.game.input.hint, this.game.chrome.panelFlash)
  }

  // ---------------------------------------------------------------- 연출

  startTimeline(events: GameEvent[]): void {
    this.game.chrome.moneyFrom = undefined
    // **발동한 자리도 박자마다 새로입니다.** 지우지 않아 앞 액션에서 발동한 조커의 자리가
    // 남고, 태그가 만든 소모품이 그 자리에서 날아왔습니다.
    this.game.cards.actorAt = undefined
    const beats = buildTimeline(events, this.game.feel)
    if (beats.length === 0) {
      this.game.payout.settleShown()
      this.game.chrome.chips.reset(0)
      this.game.chrome.mult.reset(0)
      this.game.chrome.score.target = Number(this.game.state.score)
      return
    }

    this.game.chrome.chips.reset(0)
    this.game.chrome.mult.reset(0)
    // 옵션의 배속. **연출을 끄지는 못하고 빨리 넘길 수만 있습니다.**
    this.game.player.base = this.game.session.settings.speed
    this.game.player.play(beats)
  }

  showBeat(beat: Beat): void {
    const event = beat.event
    // **그린 것만 적습니다.** 코어가 낸 이벤트가 아니라 화면이 실제로 그린 박자입니다.
    this.beatLog.push(event.t)
    if (this.beatLog.length > 40) this.beatLog.shift()
    const semitones = semitonesOf(beat.intensity, this.game.feel)
    const dust = particlesOf(beat.intensity, this.game.feel)

    switch (event.t) {
      // 낸 카드가 왼쪽부터 한 장씩 판으로 올라갑니다. **이 박자가 끝날 때까지 아무것도
      // 세지 않습니다.**
      case 'HandPlayed':
        // **지난 판의 겹은 여기서 물러납니다.** 남은 시간으로 저절로 사라지게 두면 다음
        // 판의 카드가 그 배경 위로 올라옵니다.
        this.euphoria.done()
        this.game.chrome.scoreSettled = false
        this.game.shown.hand = this.game.shown.hand.filter(uid => !event.uids.includes(uid))
        this.game.cards.liftToPlayArea(event.uids)
        this.game.refresh()
        break

      // 다음 패. **득점이 끝난 뒤에** 덱에서 옵니다. **나오기와 까기가 두 단계입니다** —
      // 뒷면으로 우르르 자리에 붙고, 마지막 장이 붙은 뒤에 왼쪽부터 파도로 뒤집힙니다.
      // 한 장씩 나와 한 장씩 뒤집었더니 여덟 장에 1.3초였고, 그 시간 동안 할 수 있는 것이
      // 없었습니다. 한꺼번에 깔리면 뽑았다는 것이 없어지므로, 간격은 짧게 두되 둡니다.
      case 'HandDrawn': {
        const draw = this.game.feel.drawStaggerMs / 1000
        const flip = this.game.feel.flipStaggerMs / 1000
        const landed = this.game.clock + (event.uids.length - 1) * draw
          + this.game.feel.drawLandMs / 1000
        event.uids.forEach((uid, index) => {
          this.game.cards.deals.push({ uid, at: this.game.clock + index * draw, flipAt: landed
              + index * flip })
        })
        break
      }

      case 'HandDiscarded':
        this.game.shown.hand = this.game.shown.hand.filter(uid => !event.uids.includes(uid))
        this.game.cards.throwAway(event.uids)
        this.game.refresh()
        break

      case 'HandEvaluated':
        // **득점하지 않는 카드는 물러납니다.** 다섯 장을 냈는데 셋만 세는 것이 화면에
        // 보이지 않으면, 점수가 왜 그것뿐인지 알 수 없습니다.
        this.game.cards.dimNonScoring(event.cards)
        this.say(tf('ui.hand.level', { name: this.game.panels.handName(event.hand),
          level: event.level }), UI.ink, 3, 0.35)
        this.game.audio.play('score_count', semitones)
        this.flashPanel(UI.ink, 0.5)
        break

      case 'CardScored': {
        const view = this.game.cards.viewOf(event.uid)
        // 카드가 차례로 득점할수록 세집니다. **뒤로 갈수록 커지는 것이 기대를 만듭니다.**
        const step = Math.min(1, this.chain / 5)
        const mul = event.op === 'MulMult'
        const tint = event.source === 'rank' || event.chips !== 0 ? UI.chips
          : event.money !== 0 ? UI.money : UI.mult
        this.chain++
        if (view) {
          // **조각을 터뜨리지 않고 빛을 돌립니다.** 카드가 차례로 터지면 화면이 시끄러워지고,
          // 정작 카드 위에 뜬 숫자가 그 조각에 묻힙니다.
          view.pop(0.5 + beat.intensity * 0.4 + step * 0.25 + (mul ? 0.35 : 0))
          view.shine(rgbOf(tint), 1)
        }
        // **다섯 장이 한 높이에서 뜁니다.** 낸 카드는 부챗살로 놓여 저마다 높이가 다르고,
        // 카드마다 그 높이에서 띄우면 오른쪽으로 갈수록 글이 아래에서 나옵니다 — 값이
        // 차례로 오르는 것을 읽는 자리이므로 그 줄이 흔들리면 안 됩니다.
        this.popAt(view && { x: view.x, y: PLAY_Y - RISER_ON_CARD },
          valueText(event.op, event.chips, event.mult, event.money),
          tint, beat.intensity + step * 0.4 + (mul ? 0.5 : 0))
        // 랭크의 칩과 강화·인장·에디션이 낸 것은 소리가 달라야 갈립니다.
        // **카드가 낸 것과 조커가 낸 것은 소리가 갈립니다.** 같은 배수라도 어디서 온
        // 것인지가 들려야 무엇을 세는 중인지 따라갈 수 있습니다.
        // **음원과 가락이 두 층입니다.** 음원은 칩이 놓이는 질감이고 — 음높이를 따라
        // 크게 움직이면 짧아져 없어집니다 — 오르는 가락은 그 위에 겹치는 음 하나가
        // 냅니다. 그 둘을 갈라 놓고 나서야 사슬을 끝까지 올려도 소리가 남습니다.
        // **소리가 난 자리가 카드가 있는 자리입니다.** 다섯 장이 한 자리에서 나면 서로를
        // 덮는데, 저마다의 자리에서 나면 겹쳐도 각각이 들리고 사슬이 왼쪽에서 오른쪽으로
        // 지나가는 것으로도 들립니다.
        const side = this.panOf(view?.x)
        const rise = this.stepUp(beat.intensity + (mul ? 0.5 : 0))
        this.game.audio.play(event.source === 'rank' ? 'card_chip'
          : event.chips !== 0 ? 'card_chip'
            : mul ? 'card_mult' : 'joker_add', rise, side)
        this.game.audio.tone('chime', rise, 0.55 + beat.intensity * 0.5, side)
        // **화면은 흔들지 않습니다.** 한 장이 점수를 내는 것은 다섯 번, 여덟 번 이어지는
        // 일이고, 그때마다 화면이 흔들리면 카드 위의 숫자를 읽을 수 없습니다 — 일어난 자리를
        // 가리키는 것은 그 카드에 도는 빛 하나로 충분합니다.
        this.flashPanel(tint, 0.4 + step * 0.3)
        this.stop(28 + step * 26 + (mul ? 60 : 0))
        // **동전은 뒤따르는 `MoneyChanged` 가 날립니다.** 코어는 돈을 둘로 알립니다 — 누가
        // 냈는지(이 이벤트)와 얼마가 실제로 들어왔는지(`MoneyChanged`, 빚 한도가 깎은 뒤의
        // 값). 여기서도 날리면 같은 돈에 동전이 두 무리이고, 그중 하나는 판 가운데에서
        // 나옵니다. 이 자리는 그 동전이 어디서 나올지만 적어 둡니다.
        if (event.money !== 0 && view) this.game.chrome.moneyFrom = { x: view.x, y: view.y }
        break
      }

      case 'JokerTriggered': {
        const view = this.game.cards.jokers.get(this.game.cards.jokerUidAt(event.slot))
        // **값을 낸 것과 무언가를 한 것이 갈립니다.** 값이 아닌 것은 사슬에 얹지 않습니다 —
        // 사슬은 값이 오르는 가락이고, 카드를 만드는 것은 그 가락의 한 음이 아닙니다.
        if (!VALUE_OPS.has(event.op)) {
          this.showAct(event.op, view && { x: view.x, y: view.y }, beat.intensity,
            nameOf(this.game.data, 'joker', event.jokerId, event.jokerId))
          if (view) view.pop(1.1)
          break
        }
        const mul = event.op === 'MulMult'
        const money = event.op === 'AddMoney'
        const grow = event.op === 'GrowSelf'
        const cue = mul ? 'joker_mul' : money ? 'joker_money' : 'joker_add'
        const text = valueText(event.op, event.chips, event.mult, event.money)
        const tint = grow ? UI.good
          : money ? UI.money
            : mul || event.chips === 0 ? UI.mult : UI.chips

        this.chain++
        // **조각을 터뜨리지 않습니다.** 조커는 한 판에 열 번도 발동하고, 그때마다 조각이
        // 터지면 화면이 시끄러워집니다 — 좌우로 흔들리는 것 하나로 충분합니다.
        if (view) view.pop(mul ? 1.6 : 1.1)
        this.popAt(view && { x: view.x, y: view.y - RISER_ON_CARD }, text, tint,
          beat.intensity + (mul ? 0.6 : 0.2))
        const side = this.panOf(view?.x)
        const rise = this.stepUp(beat.intensity + (mul ? 0.6 : 0))
        this.game.audio.play(cue, rise, side)
        // **조커마다 악기가 하나입니다.** 값이 오르는 소리만으로는 그것이 누가 낸 값인지가
        // 남지 않습니다 — 음색은 조커마다 고정이라, 같은 조커가 두 번 발동하면 같은
        // 악기로 두 번 냅니다.
        //
        // 이어질수록 잦아듭니다. 한 판에 열 번 발동하는 것이라, 매번 같은 크기로 나면
        // 조커가 카드의 득점 소리를 덮습니다.
        if (view) {
          this.game.audio.jokerVoice(view.uid, rise, Math.max(0.45, 1 - this.chain * 0.07), side)
        }

        // **배수를 곱하는 것이 이 게임에서 가장 큰 사건입니다.** 그 하나만 크게 다룹니다.
        if (mul) {
          this.jolt(12 + beat.intensity * 10, 2 + beat.intensity * 2, 0.62)
          this.flashScreen(UI.mult, 0.2 + beat.intensity * 0.16)
          this.flashPanel(UI.mult, 1)
          this.stop(120)
        } else {
          this.jolt(5 + beat.intensity * 6, 0.8 + beat.intensity, 0.24)
          this.flashPanel(tint, 0.6)
          this.stop(48)
        }

        // 동전은 뒤따르는 `MoneyChanged` 가 이 자리에서 날립니다. `CardScored` 와 같습니다.
        if (money && event.money !== 0 && view) this.game.chrome.moneyFrom = { x: view.x,
          y: view.y }
        break
      }

      // 덱과 바우처와 보스가 낸 것. **조커가 아닌 것도 임자가 있습니다** — 판돈 딱지가
      // 그 자리입니다.
      case 'RunTriggered': {
        // 덱·바우처·보스가 한 것. **임자가 판돈 딱지입니다.**
        if (!VALUE_OPS.has(event.op)) {
          // **표 이름이 열쇠의 앞 토막입니다.** `blind.` 하나로 짐작해 두었더니 보스와
          // 바우처와 덱이 다 그 앞 토막을 가지지 않아, 없는 열쇠가 화면에 그대로 떴습니다.
          this.showAct(event.op, this.game.blind.badgeMiddle(), beat.intensity,
            this.game.panels.ownerName(event.source, event.owner))
          break
        }
        const mul = event.op === 'MulMult'
        const tint = event.money !== 0 ? UI.money
          : event.chips !== 0 ? UI.chips : UI.mult
        // **딱지의 가운데입니다.** 딱지 자체를 넘기면 그것의 자리는 왼쪽 위 모서리이므로,
        // 값이 화면의 왼쪽 위 구석에 뜹니다 — 딱지가 낸 돈은 이미 가운데를 셈해 쓰고
        // 있었고 값 쪽만 빠져 있었습니다.
        this.popAt(this.game.blind.badgeMiddle(), valueText(event.op, event.chips, event.mult,
          event.money),
          tint, beat.intensity + (mul ? 0.5 : 0.1))
        // **조커가 아닌 것도 사슬에 얹힙니다.** 덱과 바우처와 보스가 낸 값이고, 그것도
        // 값이 오르는 그 가락의 한 음입니다.
        const from = this.stepUp(beat.intensity + (mul ? 0.5 : 0))
        this.game.audio.play(mul ? 'joker_mul' : 'joker_add', from)
        this.game.audio.tone('pluck', from, 0.5 + beat.intensity * 0.3)
        this.jolt(4 + beat.intensity * 5, 0.7 + beat.intensity, 0.2)
        this.flashPanel(tint, 0.6)
        this.stop(mul ? 90 : 40)
        // 판돈 딱지가 낸 돈은 그 딱지의 가운데에서 나옵니다.
        if (event.money !== 0) this.game.chrome.moneyFrom = this.game.blind.badgeMiddle()
        break
      }

      case 'JokerFizzled': {
        const view = this.game.cards.jokers.get(this.game.cards.jokerUidAt(event.slot))
        this.popAt(view && { x: view.x, y: view.y - RISER_ON_CARD },
          `${event.num}/${event.den}`, UI.inkDim, 0)
        this.game.audio.play('joker_fizzle')
        break
      }

      case 'Retriggered': {
        const view = this.game.cards.viewOf(event.uid)
        this.chain++
        if (view) {
          view.pop(1)
          view.shine(rgbOf(UI.good), 0.9)
        }
        this.popAt(view && { x: view.x, y: view.y - RISER_ON_CARD },
          t('ui.button.again'), UI.good, beat.intensity + 0.3)
        // **재발동은 같은 카드가 한 번 더 세는 것입니다.** 사슬을 이어 올리는 것이 맞고,
        // 음색은 카드의 것과 같아야 「같은 카드가 또」 로 들립니다.
        const again = this.stepUp(beat.intensity)
        const where = this.panOf(view?.x)
        this.game.audio.play('retrigger', again, where)
        this.game.audio.tone('chime', again, 0.5 + beat.intensity * 0.4, where)
        this.jolt(5, 0.9, 0.2)
        this.flashPanel(UI.good, 0.5)
        this.stop(40)
        break
      }

      case 'MoneyChanged': {
        if (event.delta === 0) break
        // **잔액은 여기서 바뀌지 않습니다.** 동전이 닿는 순간에 그 몫만큼 바뀝니다(`onLand`).
        // 상점에서 낸 값도 같습니다 — 누른 자리에서 즉시 빼고 동전을 뒤따르게 하던 것은
        // 동전이 하는 일이 없는 그림이었습니다.
        //
        // **정산에 오를 돈은 지금 날지 않습니다.** 카드가 걷히는 판 가운데에는 나올 자리가
        // 없고, 그 돈이 어디서 온 것인지는 정산 판의 줄이 적습니다 — 「받는다」 를 누르면
        // 그 판에서 금액 칸으로 날아가고, 그때 잔액이 셉니다.
        if (this.game.payout.payoutWanted) {
          this.game.payout.payoutRows.push({ reason: event.reason, amount: event.delta })
          if (this.game.panels.modals.has(this.game.payout.panel)) this.game.payout.drawPayout()
          break
        }
        // **판 돈은 내놓은 그 자리에서 나옵니다.** 그것이 어느 것을 내놓아 들어온 돈인지를
        // 가리키는 유일한 표시입니다.
        const sold = event.reason === 'sell' ? this.game.tray.sellFrom : undefined
        if (event.reason === 'sell') this.game.tray.sellFrom = undefined
        // **산 값이 뜨는 자리는 산 물건 위입니다.** 동전은 거기서 나오지 않습니다 — 아래를
        // 보십시오.
        const bought = event.reason === 'shop' ? this.game.shop.boughtFrom : undefined
        if (event.reason === 'shop') this.game.shop.boughtFrom = undefined
        // **조커·카드·판돈이 낸 돈은 그것에서 나옵니다.** 그 이벤트가 적어 둔 자리입니다.
        const hosted = this.game.chrome.moneyFrom
        if (event.delta < 0) {
          // **나가는 돈은 곳간에서 사라집니다.** 물건 쪽과 동전을 오가게 하면 색은 「잃었다」
          // 인데 움직임은 「들어왔다」이고, 남은 돈을 보러 간 눈이 물건까지 따라가야 합니다.
          // 리롤·임대료처럼 갈 곳이 없는 지출도 이 하나로 같은 그림입니다.
          this.game.payout.coins.spend(event.delta, this.game.chrome.moneySpot())
        } else {
          // **들어오는 돈은 온 곳에서 날아듭니다.** 건너뛰어 받은 태그의 돈은 그 칩이 앉은
          // 자리에서 — 판 가운데에서 나오면 어느 것이 낸 돈인지가 없습니다.
          const tag = this.game.blind.skipping ? this.game.blind.tagLanded
            ?? this.game.blind.skipFrom : undefined
          const from = sold ?? hosted ?? tag
            ?? (this.game.state.phase === 'shop' ? this.game.shop.shopMiddle() : { x: BOARD_X,
              y: PLAY_Y })
          // 태그의 자리만 덧층의 좌표이고 나머지는 판의 좌표입니다.
          const layer = from === tag ? this.game.overlay : this.game.board
          this.game.payout.coins.fly(event.delta, this.game.payout.coinSpot(layer, from),
            this.game.chrome.moneySpot())
        }
        // **소리는 낸 쪽이 이미 냈습니다.** 조커·카드가 낸 돈은 그 이벤트가 소리를 냈고,
        // 여기서 또 내면 같은 돈에 소리가 둘입니다. 닿는 소리는 동전마다 따로 납니다.
        // **판 것과 산 것도 낸 쪽이 냈습니다** — 단추가 `joker_sell` · `joker_buy` 를 냅니다.
        // 리롤의 소리도 단추가 냅니다. 여기서 또 내면 한 번 누름에 소리가 둘입니다.
        const voiced = hosted !== undefined || sold !== undefined || bought !== undefined
          || event.reason === 'reroll'
        if (!voiced) this.game.audio.play(event.delta > 0 ? 'joker_money' : 'shop_reroll')

        // **무엇으로 번 돈인가**를 적습니다. 합계만 굴러가면 이유를 알 수 없습니다.
        // **판 돈은 내놓은 자리에 뜹니다.** 금액 칸 옆에 뜨면 사는 것과 파는 것이 같은
        // 자리에서 잇달아 떠서 뒤의 것이 앞의 것을 덮습니다.
        const why = moneyReason(event.reason)
        if (why) {
          // **부호는 달러 앞입니다.** 값 그대로 이어 적어서 나가는 돈이 `$-2` 로 났습니다.
          const line = `${why}  ${event.delta > 0 ? '+' : '-'}$${Math.abs(event.delta)}`
          const tint = event.delta > 0 ? UI.money : UI.bad
          // 카드의 윗변에 걸쳐 뜹니다. 다른 값들과 같은 규칙입니다.
          //
          // **뜯은 팩 뒤에서는 기다립니다.** 바꿔 집는 것은 파는 것과 집는 것이 한
          // 누름에 일어나는데, 파는 값이 그 자리에서 뜨면 아직 덮여 있는 팩 뒤에
          // 가려집니다 — 팩이 걷힌 뒤에 뜨고, 새 물건의 이름은 그다음입니다.
          if (sold && this.game.pack.packLayer.visible) {
            const at = { x: sold.x, y: sold.y - RISER_ON_CARD }
            this.game.later.push({ at: this.game.clock + SELL_WAIT, run: () => this.popAt(at,
              line, tint, 0.5) })
          }
          // **판 값과 산 값은 같은 세기입니다.** 바꿔 살 때 둘이 잇달아 뜨는데, 한쪽만 크면
          // 내놓는 것이 사는 것보다 무거운 일로 읽힙니다.
          else if (sold) this.popAt({ x: sold.x, y: sold.y - RISER_ON_CARD }, line, tint, 0.5)
          else if (bought) {
            this.popAt({ x: bought.x, y: bought.y - RISER_ON_CARD }, line, tint, 0.5)
          }
          else this.popAt(this.game.chrome.moneyLabelAnchor(), line, tint, 0.3)
        }
        break
      }

      case 'ScoreResolved':
        // **모으던 것이 여기서 터집니다.** 문턱을 넘지 않은 판에서는 아무것도 하지 않습니다.
        this.euphoria.release()
        // **이 판의 문턱은 여기까지입니다.** 뒤에 오는 박자로 다시 모으지 않습니다.
        this.game.chrome.scoreSettled = true
        // **더해집니다.** 이 판의 점수가 아니라 라운드에 쌓인 점수가 칸에 뜹니다.
        this.game.shown.score += event.score
        this.game.chrome.score.target = this.game.shown.score
        this.game.audio.play('score_settle', semitones)
        // **곡이 잠깐 물러납니다.** 이 한 방이 앞의 것들보다 확실히 커야 하는데, 소리를
        // 키우는 것보다 자리를 내는 편이 낫습니다.
        this.game.audio.music.duck(0.45, 0.9)
        this.haptics.play('settle')

        // **마지막 한 방이 앞의 것들보다 확실히 커야 합니다.** 그것이 없으면 득점이
        // 어디서 끝났는지 읽히지 않습니다.
        this.jolt(14 + shakeOf(beat.intensity, this.game.feel), 2.4 + beat.intensity * 2, 0.9)
        this.flashScreen(UI.ink, 0.26 + beat.intensity * 0.2)
        this.flashPanel(UI.ink, 1)
        this.stop(150)

        // 낸 카드가 멈춘 자리에서 크게 터집니다.
        this.burstAcrossPlayArea(26 + dust * 4, UI.mult, 1.8 + beat.intensity)
        this.chain = 0
        this.rung = 0
        break

      case 'BlindCleared':
        // **보스를 격파하면 챌린지가 열립니다.** 안테 8을 넘기는 것이 조건이었고, 그것은
        // 한 판을 끝까지 이기는 것이라 대개 열리지 않은 채로 남습니다 — 챌린지는 다르게
        // 한 판 더 하는 것이므로, 이 게임이 무엇인지를 아는 자리에서 열리면 됩니다.
        if (this.game.state.blind === BlindKind.Boss) this.game.session.unlockChallenges()
        // **정산 판이 상점보다 먼저 뜹니다.** 돈이 들어오는 것을 보고 나서 쓰는 것이
        // 순서이고, 상점이 먼저 열리면 그 돈이 어디서 왔는지가 지나가 버립니다.
        this.game.payout.payoutRows.length = 0
        this.game.payout.payoutWanted = true
        // **이 게임에서 사람이 기다리는 순간입니다.** 채널을 전부 씁니다 — 화면이
        // 번쩍이고, 판이 흔들리고, 배경이 밝아지고, 음이 여섯 번 올라갑니다.
        //
        // **글은 적지 않습니다.** 곧 정산 판이 서서 무엇을 얼마나 받는지가 적히므로,
        // 「넘겼습니다」는 그 판이 할 말을 한 번 미리 하는 것일 뿐입니다.
        this.game.audio.play('blind_clear')
        this.game.audio.music.duck(0.55, 1.3)
        this.haptics.play('clear')
        // **격파의 소리가 지나간 뒤에 오릅니다.** 같은 순간에 시작하면 첫 음이 그 소리
        // 밑에 묻히고, 그러면 오르는 것이 다섯 계단으로 들립니다.
        this.flourish('glass', 6, { gap: 0.11, after: 0.18, strength: 0.85 })
        this.burstAcrossPlayArea(46, UI.good, 2.4, 2.6)
        // **돈은 지폐로 뿌립니다.** 격파의 보상이 이 자리에서 들어오므로, 그 한 방이
        // 불티가 아니라 뿌린 돈으로 보여야 합니다 — 점보다 크므로 개수는 절반입니다.
        this.particles.bills(BOARD_X, PLAY_Y - 60, 34, UI.money, 1.2, 1)
        this.particles.burst(BOARD_X, 210, 44, UI.good, 2.2, 2.4)
        // **국면이 넘어가는 자리입니다.** 흔들림은 판 전체를 움직이므로, 여기서 큰 값을
        // 쓰면 격파한 것이 아니라 땅이 흔들린 것으로 읽힙니다 — 알릴 것은 이미 터지는
        // 것과 번쩍이는 것과 소리 셋이 하고 있습니다.
        this.jolt(9, 4.2, 1)
        this.flashScreen(UI.good, 0.46)
        this.stop(280)
        this.chain = 0
        this.rung = 0
        break

      // 건너뛰어 받은 태그. **카드에 적혀 있던 칩이 커져서 머리띠로 날아가 앉습니다.**
      // 앉은 자리의 칩이 하얗게 한 번 번쩍이고, 그 자리에서 쓰이는 태그는 앉은 뒤에 켜집니다.
      case 'TagGained':
        this.game.blind.launchTag(event.tagId)
        break

      // 받자마자 쓰인 태그. 켜지는 것은 칩이 앉을 때 시작했고, 여기서는 소리만 냅니다 —
      // 코어는 효과를 다 낸 뒤에 이 이벤트를 내므로, 여기서 켜면 동전이 나간 뒤에 켜집니다.
      case 'TagUsed':
        this.game.audio.play('joker_add', 8)
        break

      case 'RunLost':
        // **여기서 걷지 않습니다.** 낸 카드가 결과를 보이고 물러날 때 손패가 뒤따르고,
        // 그 뒤에 전부 덱으로 돌아가고, 그 뒤에 끝났다는 판이 뜹니다 — 격파와 같은 순서입니다.
        // **글은 적지 않습니다.** 끝났다는 판이 곧 뜨고 거기에 몇 점이 모자랐는지까지
        // 적히므로, 머리글은 그 판이 할 말을 미리 하는 것입니다.
        this.game.audio.play('blind_fail')
        this.game.audio.music.duck(0.5, 1.2)
        this.jolt(5, 1.6, 0.5)
        this.flashScreen(UI.bad, 0.2)
        this.stop(160)
        break

      case 'RunWon':
        // **이긴 것을 저장에 남깁니다.** 챌린지를 열어 주는 것도 여기입니다 — 원작은 덱
        // 여러 종으로 이겨야 열리지만 우리에게는 덱을 고르는 화면이 아직 없으므로, 한 번
        // 이기는 것을 조건으로 둡니다.
        this.game.session.recordWin()
        this.say(t('ui.label.all_cleared'), UI.money, 2.8)
        this.game.audio.play('blind_clear')
        this.flourish('bell', 10, { gap: 0.13, after: 0.18, strength: 0.8 })
        this.particles.bills(BOARD_X, SIZE.height / 2, 54, UI.money, 1.3, 1.1)
        this.jolt(8, 3.4, 1)
        this.flashScreen(UI.money, 0.44)
        this.stop(220)
        break

      // 소모품 하나가 생겼습니다. **누가 만들었는지의 자리에서 옵니다** — 상점과 팩은
      // 자기가 들므로 여기 오지 않습니다.
      case 'ConsumableAdded': {
        // **박자가 붙든 것만 박자가 놓습니다.** 상점과 팩이 붙든 것을 여기서 놓으면 같은
        // 물건이 두 번 날아옵니다.
        if (this.game.tray.arriveHold?.kind !== 'item'
            || this.game.tray.arriveHold.byBeat !== true) break
        this.game.tray.arriveHold = undefined
        this.game.tray.itemFlying(this.game.cards.actorAt ?? { x: BOARD_X, y: JOKER_Y })
        // **얻는 소리입니다.** 쓰는 소리(`consumable_use`)로 알리고 있었습니다 — 생긴 것과
        // 쓴 것이 같은 소리면 귀로 갈리지 않습니다. 팩에서 집는 것과 같은 소리이고,
        // 조커가 생기는 것도 같습니다.
        this.game.audio.play('pack_pick')
        // **갈래를 상태에서 읽습니다.** 이름을 찾는 표가 갈래마다 다르므로, 타로로 고정하면
        // 행성과 유령의 이름이 식별자 그대로 뜹니다.
        const made = this.game.state.consumables.find(one => one.uid === event.uid)
        const kind = made?.kind === 2 ? ShopItemKind.Planet
          : made?.kind === 3 ? ShopItemKind.Spectral : ShopItemKind.Tarot
        this.game.later.push({
          at: this.game.clock + LAND_AT,
          run: () => this.game.tray.landed({ kind, id: event.id, cost: 0,
            edition: EditionKind.Base }),
        })
        break
      }

      // 조커 하나가 생겼습니다. **소모품과 같은 길입니다** — 만든 것의 자리에서 날아와
      // 닿고, 닿은 자리에 이름이 뜨고 칸 수가 강조됩니다. 이 이벤트를 화면이 받지 않아
      // 효과가 준 조커만 줄 위에서 떨어지고 이름도 없었습니다.
      case 'JokerAdded': {
        if (this.game.tray.arriveHold?.kind !== 'joker'
            || this.game.tray.arriveHold.byBeat !== true) break
        this.game.tray.arriveHold = undefined
        this.game.tray.arriveFrom = this.game.cards.actorAt ?? { x: BOARD_X, y: JOKER_Y }
        this.game.refresh()
        this.game.audio.play('pack_pick')
        this.game.later.push({
          at: this.game.clock + LAND_AT,
          run: () => this.game.tray.landed({
            kind: ShopItemKind.Joker, id: event.jokerId, cost: 0, edition: EditionKind.Base,
          }),
        })
        break
      }

      // 보스나 효과가 조커 하나를 부쉈습니다. **카드가 부서지는 것과 같은 몸짓입니다** —
      // 타는 그 자리에서 조각이 튀고 이름이 뜨고 판이 흔들립니다. 구석의 토스트 한 줄로만
      // 알리고 있었고, 판 것과 부서진 것이 같은 불로 타서 갈리지 않았습니다.
      case 'JokerDestroyed': {
        const view = this.game.cards.burning.find(one => one.uid === event.uid)
        const at = view ? { x: view.x, y: view.y } : undefined
        if (at) this.particles.burst(at.x, at.y, 26, EMBER, 1.2, 1)
        this.popAt(at && { x: at.x, y: at.y - RISER_ON_CARD },
          tf('ui.toast.destroyed',
             { name: nameOf(this.game.data, 'joker', event.jokerId, event.jokerId) }),
          UI.bad, 0.5)
        this.game.audio.play('card_destroy')
        this.jolt(7, 1.6, 0.35)
        break
      }

      // 규칙이 바뀌었습니다. **판 하나로 뜹니다** — 오른쪽 구석의 토스트가 아닙니다.
      case 'RuleChanged':
      case 'HandLevelled':
        this.showRuleChange(beat)
        break

      // 카드가 바뀌고 없어지고 더해집니다. **셋이 한 박자입니다** — 연달아 오는 것을
      // `buildTimeline` 이 묶어 두었고, 화면이 하는 일은 한 몸짓입니다.
      case 'CardModified':
      case 'CardDestroyed':
      case 'CardAdded':
        this.game.cards.showCardChange(beat)
        break

      // 보스가 카드를 무력하게 만들었습니다. **어느 장인지가 보여야 합니다** — 판이
      // 시작할 때 한 번 크게 개입하는 것인데, 그동안 화면이 어느새 회색이 되어 있었습니다.
      case 'CardsDebuffed':
        this.game.cards.witherCards(event.uids)
        break

      // 보스가 손패를 엎었습니다. **그 자리에서 뒤집힙니다** — 이미 엎어진 채로 그려지면
      // 무엇이 일어난 것인지 화면에 없습니다.
      case 'CardsHidden':
        this.game.cards.hideCards(event.uids)
        break

      // 보스가 조커 하나를 껐습니다.
      case 'JokerDisabled': {
        const view = this.game.cards.jokers.get(event.uid)
        if (view) {
          view.wither()
          view.pop(1.4)
          // 카드가 시드는 것과 같은 조각입니다(`witherOne`). 조커는 하나이므로 이름 글이
          // 함께 뜨고, 카드는 여럿이라 조각만 튑니다.
          this.particles.burst(view.x, view.y, 10, UI.bad, 0.7, 0.7)
        }
        this.popAt(view && { x: view.x, y: view.y - RISER_ON_CARD },
          t('ui.note.turned_off'), UI.bad, 0.5)
        this.game.audio.play('boss_reveal')
        this.game.audio.tone('pluck', -9, 0.7)
        this.jolt(7, 1.6, 0.35)
        this.flashPanel(UI.bad, 0.7)
        break
      }

      // 보스가 조커의 차례를 섞었습니다. 딱지가 새 자리로 미끄러지는 것은 `syncJokers` 가
      // 합니다 — **여기서는 그 하나하나가 한 번씩 튀어오릅니다.** 자리만 바뀌면 무엇이
      // 일어난 것인지 알 수 없습니다.
      case 'JokersShuffled': {
        event.uids.forEach((uid, index) => {
          const view = this.game.cards.jokers.get(uid)
          if (!view) return
          this.game.later.push({
            at: this.game.clock + index * 0.05,
            run: () => view.pop(1.1),
          })
        })
        this.game.audio.play('joker_move')
        this.game.audio.tone('pluck', 2, 0.5)
        this.jolt(6, 1.4, 0.3)
        break
      }

      // 조커의 판이 갈렸습니다. **카드와 같은 몸짓입니다** — 뒷면을 거쳐 다른 딱지가 되어
      // 돌아오고, 새 얼굴이 보이는 자리에 걸린 판의 이름이 뜹니다. 딱지가 한 번 튀고
      // 「효과가 적용되었습니다」가 뜨던 동안은 무엇이 걸린 것인지가 없었습니다.
      case 'JokerModified': {
        const view = this.game.cards.jokers.get(event.uid)
        const joker = this.game.state.jokers.find(one => one.uid === event.uid)
        this.game.cards.pendingJokers.delete(event.uid)
        if (!view) break
        // **이름은 이벤트가 들고 옵니다.** 상태에서 찾으면 같은 액션에서 부서진 딱지의
        // 이름을 찾을 수 없습니다.
        const name = this.game.panels.editionName(event.edition as EditionKind)
          || t('ui.note.applied')
        // **없어진 딱지는 뒤집지 않습니다.** 판이 걸리고 같은 액션에서 부서지는 것이 있고
        // (`hex`), 그때 상태에는 이미 그 조커가 없습니다 — 타는 딱지를 뒤집으면 두 몸짓이
        // 한 자리에서 겹칩니다.
        if (!joker) {
          this.popAt({ x: view.x, y: view.y - RISER_ON_CARD }, name, UI.legendary, 0.5)
          this.game.audio.play('card_flip')
          break
        }
        view.onFlipped = () => {
          view.onFlipped = undefined
          view.pop(1.2)
          this.popAt({ x: view.x, y: view.y - RISER_ON_CARD }, name, UI.legendary, 0.5)
          this.game.audio.play('card_flip')
        }
        this.game.audio.play('card_flip')
        view.turnOver(joker, this.game.cards.jokerLookOf(joker), TURN_BACK_MS / 1000)
        break
      }

      // 조커 하나가 다른 조커의 능력을 빌립니다. **둘 다 흔들립니다** — 어디에서
      // 어디로인지가 한쪽만으로는 남지 않습니다.
      case 'JokerCopied': {
        const from = this.game.cards.jokers.get(event.fromUid)
        const to = this.game.cards.jokers.get(event.uid)
        if (from) from.pop(1.1)
        if (to) to.pop(1.3)
        // **누구의 것을 빌렸는지가 글입니다.** 「효과가 적용되었습니다」는 어느 조커의
        // 능력인지가 적히지 않고, 그것이 이 박자에서 유일하게 새로운 것입니다.
        this.popAt(to && { x: to.x, y: to.y - RISER_ON_CARD },
          from?.look.name ?? t('ui.note.applied'), UI.legendary, 0.5)
        this.game.audio.play('card_flip')
        break
      }

      default:
        break
    }

    // **박자가 값을 들고 옵니다.** 화면이 이벤트마다 값을 다시 세지 않는 이유가 이것입니다 —
    // 누적값과 에디션처럼 세는 자리가 여럿이면 반드시 한쪽이 빠집니다.
    if (beat.chips !== undefined) this.game.chrome.chips.target = beat.chips
    if (beat.mult !== undefined) this.game.chrome.mult.target = Math.round(beat.mult / 10_000)
    this.game.chrome.chips.emphasize(scaleOf(beat.intensity, this.game.feel))
    this.game.chrome.mult.emphasize(scaleOf(beat.intensity, this.game.feel))

    // **환희의 문턱은 득점하는 박자마다 봅니다.** 조커가 배수를 올리는 도중에 넘어가므로
    // 정산에서 한 번만 보면 모으는 것 없이 터지는 것만 남고, 반대로 **아무 박자에서나 보면
    // 정산한 다음에 다시 모으기 시작합니다** — 정산 뒤의 박자들(다음 패 · 돈 · 격파)도 그
    // 판의 칩과 배수를 그대로 들고 있기 때문입니다. 배수는 만 배로 적힌 값입니다.
    if (!this.game.chrome.scoreSettled && SCORING_BEATS.has(event.t)
        && beat.chips !== undefined && beat.mult !== undefined) {
      this.euphoria.consider(beat.chips * beat.mult / 10_000)
    }
  }

  /**
   * 값을 바꾸지 않은 효과 하나가 발동했습니다.
   *
   * **값을 내는 것과 갈라 둡니다.** 값의 몸짓은 사슬에 얹혀 음이 오르고 판이 그 색으로
   * 번쩍이는 것인데, 카드를 만들고 부수는 것은 그 가락의 한 음이 아닙니다 — 얹으면 값이
   * 오르지 않았는데 음만 올라갑니다.
   *
   * **갈래마다 색과 소리와 말이 다릅니다.** 무엇을 만들고 무엇을 부순 것인지 — 그 이름은
   * 뒤따르는 박자가 냅니다. `ConsumableAdded` 가 만든 것의 이름을, `JokerDestroyed` 가
   * 부순 것의 이름을 들고 옵니다.
   */
  private showAct(op: string, at: { x: number; y: number } | undefined,
                  intensity: number, who?: string): void {
    this.game.cards.actorName = who
    this.game.cards.actorAt = at
    const look = ACT_LOOK[ACT_KINDS[op] ?? 'change']
    this.popAt(at && { x: at.x, y: at.y - RISER_ON_CARD },
      t(look.say), look.tint, 0.2 + intensity * 0.3)
    this.game.audio.play(look.cue, 0, this.panOf(at?.x))
    this.jolt(4 + intensity * 4, 0.8 + intensity * 0.6, 0.2)
    this.flashPanel(look.tint, 0.5)
    this.stop(40)
  }

  /**
   * 규칙이 바뀐 것을 판 하나로 알립니다.
   *
   * **오른쪽 구석의 토스트가 아닙니다.** 조커가 걸고 소모품이 걸고 보스가 거는 규칙은 그
   * 판의 셈법을 통째로 바꾸는 것이라, 지나가는 알림으로 두면 무엇이 달라진 판인지 모르는
   * 채로 계속하게 됩니다 — 손패 줄 바로 위 가운데입니다.
   *
   * **한 판에 담습니다.** 한 액션에 규칙이 여럿 걸리므로(챌린지 · 보스 · 바우처) 판을
   * 여럿 세우면 어느 것이 방금 온 것인지 알 수 없습니다.
   */
  private showRuleChange(beat: Beat): void {
    const events = beat.rules ?? [beat.event]
    const notes: RuleNote[] = []

    // **머리글은 이벤트가 들고 옵니다.** 앞 박자에서 짐작하면 규칙이 견주어 나오는 자리
    // — 조커나 바우처를 산 자리 — 에서는 그 앞에 발동 이벤트가 없으므로, 아무 상관 없는
    // 앞의 이름이 그대로 뜹니다.
    let from: string | undefined

    for (const one of events) {
      if (one.t === 'RuleChanged') {
        from = from ?? this.game.panels.keyName(one.from)
        notes.push({
          title: this.game.panels.ruleName(one.rule),
          change: ruleChange(one),
          // **켜고 끄는 것은 켜지는 쪽이 좋은 것입니다.** 수는 오르는 쪽입니다 — 어느
          // 쪽이 이로운지는 규칙마다 다르지만, 걸리는 것은 대개 이로우려고 거는 것입니다.
          good: one.after === null || one.before === null || one.after >= one.before,
        })
      } else if (one.t === 'HandLevelled') {
        notes.push({
          title: this.game.panels.handName(one.hand),
          // **얼마에서 얼마로입니다.** 뒤만 적으면 몇 단 오른 것인지가 없고, 규칙이 바뀌는
          // 줄(`RuleChanged`)과 읽는 법도 달라집니다.
          // **말이 코드에 고정되어 있었습니다.** `Lv.` 는 어느 말에서나 같지 않습니다.
          change: one.before === one.level
            ? tf('ui.hand.level_short', { level: one.level })
            : `${tf('ui.hand.level_short', { level: one.before })}`
              + ` → ${tf('ui.hand.level_short', { level: one.level })}`,
          good: true,
        })
      }
    }
    if (notes.length === 0) return

    this.ruleBanner.show(notes, from ?? this.game.cards.actorName)
    this.placeRuleBanner()
    // **왼쪽 판의 그 줄이 함께 밝아집니다.**
    //
    // 두 자리가 하는 일이 다릅니다 — 「적용 중」 목록은 지금 걸려 있는 것이고, 가운데
    // 판은 그것이 방금 얼마에서 얼마로 달라졌는가입니다. 목록은 그 델타를 담을 수 없고,
    // 판은 목록을 대신할 수 없습니다. 둘을 잇지 않으면 같은 글이 두 번 적힌 것으로
    // 읽히므로, 판이 뜨는 그 순간에 목록의 그 줄이 밝아져 어디로 들어갔는지를 말합니다.
    this.game.panels.activeGlow = { label: notes[0].title, until: this.game.clock + ACTIVE_GLOW }
    this.game.panels.syncActive()
    this.game.audio.play('voucher_buy')
    // **판이 서는 소리가 값의 소리와 갈립니다.** 규칙은 값이 아니라 셈법이 바뀌는 것이고,
    // 그 둘이 같은 소리면 무엇이 일어난 것인지 귀로 갈리지 않습니다.
    this.game.audio.tone('bell', 4, 0.55)
    this.flashPanel(UI.money, 0.7)
  }

  /**
   * 알림 띠가 놓이는 자리. **손패의 윗변 바로 위, 화면의 가운데입니다.**
   *
   * **판의 가운데가 아니라 화면의 가운데입니다.** 띠는 화면보다 넓어 양 끝이 화면 밖으로
   * 나가므로, 판의 가운데(`BOARD_X`)에 놓으면 왼쪽 끝이 화면 안에서 끊깁니다. 손패가 몇
   * 장인지와 무관하게 줄의 높이는 같으므로 자리는 고정이고, 띠의 세로 길이는 규칙 수마다
   * 다르므로 아랫변을 기준으로 놓습니다.
   */
  private placeRuleBanner(): void {
    this.ruleBanner.place(SIZE.width / 2, HAND_Y - SIZE.cardHeight / 2 - 14)
  }

  /**
   * 판 뒤를 흐립니다.
   *
   * **필요할 때만 겁니다.** 흐림은 화면 전체를 한 번 더 굽는 것이라, 판이 없는 동안에도
   * 걸어 두면 매 프레임 그 값을 냅니다.
   */
  advanceBlur(seconds: number): void {
    void seconds
    /**
     * **덮개의 짙기를 그대로 씁니다.**
     *
     * 「판이 떠 있는가」 만 보고 따로 잦아들게 했더니 둘의 때가 어긋났습니다 — 그 값은
     * 닫는 움직임이 다 끝난 다음에야 거짓이 되므로, 판이 줄어들며 사라지는 내내 흐림은
     * 그대로 있다가 판이 없어진 뒤에 혼자 잦아들었습니다. 덮개가 0 이 되는 순간에 흐림이
     * 아직 남아 있고, 그 나머지가 뚝 끊기는 것으로 보입니다.
     *
     * 판의 `t` 가 이미 눌린 값이므로 여기서 다시 눌 것이 없습니다.
     */
    this.blurShown = this.game.panels.modals.cover

    // 덮개가 보이지 않는 자리와 같은 문턱입니다. 둘이 같은 프레임에 서고 같은 프레임에
    // 없어져야 한 가지 일로 보입니다.
    const on = this.blurShown > 0.01
    const filtered = (this.recede.filters as unknown[] | null)?.length ?? 0
    if (on && filtered === 0) this.recede.filters = [this.blur]
    else if (!on && filtered > 0) this.recede.filters = []
    // **약하게.** 뒤가 무엇인지는 알아볼 수 있어야 합니다 — 판을 닫고 어디로 돌아가는지가
    // 보이지 않으면 판이 화면을 갈아치운 것으로 보입니다.
    if (on) this.blur.strength = this.blurShown * BLUR_PX * this.blurDensity

    // **배경은 판 말고도 흐릴 일이 있습니다.** 로그인 화면의 진행 띠가 그것입니다 — 그
    // 화면에는 떠 있는 판이 없으므로 위의 값은 0이고, 배경만 그대로 또렷하면 띠가 배경
    // 위에 놓인 막대 하나로 보입니다. 흐리는 층이 둘로 갈린 까닭입니다.
    const back = Math.max(this.blurShown, this.frontHaze)
    const backOn = back > 0.01
    const backFiltered = (this.backdrop.filters as unknown[] | null)?.length ?? 0
    if (backOn && backFiltered === 0) this.backdrop.filters = [this.blurBack]
    else if (!backOn && backFiltered > 0) this.backdrop.filters = []
    if (backOn) this.blurBack.strength = back * BLUR_BACK_PX * this.blurDensity
  }

  /** 낸 카드가 늘어선 폭 전체에서 터뜨립니다. 한 점에서 터지면 찔끔 나온 것으로 보입니다. */
  private burstAcrossPlayArea(perCard: number, tint: number, power: number,
                              linger = 1.9): void {
    if (this.game.cards.playedViews.length === 0) {
      this.particles.burst(BOARD_X, PLAY_Y, perCard * 3, tint, power, linger)
      return
    }
    for (const view of this.game.cards.playedViews) {
      this.particles.burst(view.x, view.y, perCard, tint, power, linger)
      this.particles.burst(view.x, view.y - 40, Math.round(perCard * 0.6),
        UI.chips, power * 0.8, linger)
    }
  }

  flashPanel(tint: number, strength: number): void {
    this.panelTint = tint
    this.panelGlow = Math.min(1, Math.max(this.panelGlow, strength))
  }

  /** 화면 전체를 번쩍입니다. **큰 것에만 씁니다** — 잦으면 눈이 아픕니다. */
  flashScreen(tint: number, strength: number): void {
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

  advanceHeadline(seconds: number): void {
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

  /**
   * 화면의 가로 자리를 좌우로.
   *
   * **판의 가운데가 0 입니다.** 화면의 가운데가 아닙니다 — 왼쪽에 패널이 있어서 카드가
   * 노는 자리는 화면의 오른쪽으로 치우쳐 있고, 화면의 가운데를 기준으로 잡으면 다섯 장이
   * 전부 오른쪽에서만 납니다.
   */
  private panOf(x: number | undefined): number {
    if (x === undefined) return 0
    return Math.max(-1, Math.min(1, (x - BOARD_X) / (SIZE.width - BOARD_X)))
  }

  /**
   * 사슬을 한 칸(큰 사건이면 두 칸) 올리고 그 음높이를 냅니다.
   *
   * **반음이 아니라 5음 음계의 계단입니다.** 사건마다 2반음씩 올리던 것은 「올라간다」로만
   * 들리고 가락으로는 들리지 않았습니다 — 어느 두 음도 협화음이 아니기 때문입니다. 그리고
   * 상한이 없어서 사슬이 길면 32반음까지 갔고, 그 높이에서 음원은 재생 속도 6배라
   * 0.027초짜리 딱 소리가 되었습니다. `ladder` 가 그 둘을 함께 답합니다.
   */
  private stepUp(intensity: number): number {
    this.rung += intensity > 0.55 ? 2 : 1
    return ladder(this.rung)
  }

  /**
   * 음이 하나씩 올라가는 한 소절. **오르는 음이 「해냈다」로 읽힙니다.**
   *
   * **음원이 아니라 음입니다.** 음원으로 내면 재생 속도가 3반음에서 멈추므로 여섯 계단이
   * 여섯 번 같은 소리이고, 짧은 음원 여섯이 0.35초 안에 겹쳐 겹침 계수기에 걸립니다 —
   * 질감은 그 자리의 큰 신호 하나가 이미 내고 있고, 여기서 낼 것은 가락입니다.
   *
   * `after` 는 첫 음까지 기다리는 시간입니다. **큰 신호의 앞머리를 비켜 갑니다** —
   * 격파의 소리와 첫 음이 같은 순간에 나면 그 음은 들리지 않습니다.
   */
  private flourish(name: ToneName, count: number,
                   { gap = 0.1, after = 0, strength = 0.8, step = 1 } = {}): void {
    for (let i = 0; i < count; i++) {
      this.notes.push({
        at: this.game.clock + after + i * gap,
        name, step: i * step, strength, gap,
        // 왼쪽에서 오른쪽으로 지나갑니다. 오르는 것이 자리로도 읽힙니다.
        pan: count > 1 ? -0.3 + (i / (count - 1)) * 0.6 : 0,
      })
    }
  }

  advanceChimes(): void {
    while (this.chimes.length > 0 && this.chimes[0].at <= this.game.clock) {
      const next = this.chimes.shift()
      if (next) this.game.audio.play(next.cue, next.semitones)
    }
    // **한 프레임에 한 음이고, 밀렸으면 다시 벌립니다.**
    //
    // 오르는 소절은 음 사이의 간격이 곧 그 소절이므로, 밀린 것이 한 번에 나오면 소절이
    // 화음 하나가 됩니다 — 여섯 음이 0.11초 간격으로 예약되어 있는데 그 사이에 프레임이
    // 길어지면(그림을 굽느나 멈추거나 히트스톱이 이어지면) 전부 지난 시각이 되고, 한
    // 프레임에 하나씩 내보내도 0.017초 간격입니다.
    //
    // **남은 것을 통째로 밀어 둡니다.** 그러면 소절이 늦게 시작하더라도 간격은 그대로이고,
    // 늦게 시작한 것은 들리지 않습니다.
    if (this.notes.length > 0 && this.notes[0].at <= this.game.clock) {
      const next = this.notes.shift()
      if (next) {
        this.game.audio.tone(next.name, ladder(next.step), next.strength, next.pan)
        const ahead = this.notes[0]
        if (ahead && ahead.at <= this.game.clock) {
          const push = this.game.clock + next.gap - ahead.at
          for (const one of this.notes) one.at += push
        }
      }
    }
  }

  /**
   * 굴러가는 숫자에 소리를 붙입니다.
   *
   * **간격이 좁아지고 음이 오릅니다.** 남은 거리가 줄면 빨라지므로, 끝으로 갈수록 촘촘해지고
   * 높아집니다 — 그 조여드는 것이 「쌓이고 있다」입니다.
   */
  advanceRatchet(seconds: number): void {
    if (!this.game.session.settings.sound) return

    // **곱해지기를 기다리는 동안.** 마지막 카드가 득점하고 두 숫자가 곱해질 때까지가
    // 이 게임에서 사람이 가장 크게 기다리는 자리인데, 재어 보니 그 1초가 무음이었습니다.
    // 조여들며 올라가는 소리로 채웁니다.
    if (this.game.player.coming === 'ScoreResolved') {
      this.ratchet -= seconds
      if (this.ratchet > 0) return
      this.build = Math.min(1, this.build + 0.12)
      this.ratchet = 0.10 - this.build * 0.055
      // **여기도 같은 음계입니다.** 조여드는 자리만 반음으로 오르면 그 구간에서 조성이
      // 바뀌고, 이어지는 득점 소리와 어긋납니다.
      this.game.audio.tone('chime', ladder(Math.round(this.build * 9)), 0.32)
      return
    }
    this.build = 0

    // **숫자가 굴러가는 동안.** 남은 거리가 줄면 촘촘해지고 음이 오릅니다.
    const rolling = this.game.chrome.score.rolling
    if (rolling <= 0) {
      this.ratchet = 0
      return
    }

    this.ratchet -= seconds
    if (this.ratchet > 0) return
    this.ratchet = 0.05 + rolling * 0.06
    this.game.audio.tone('chime', ladder(Math.round((1 - rolling) * 8)), 0.28)
  }

  jolt(shake: number, chroma: number, pulse = 0): void {
    // 흔들림과 색수차는 **꺼 둘 수 있습니다.** 배경이 밝아지는 것은 남깁니다 — 그것이
    // 없으면 큰 값이 온 것을 알릴 채널이 하나도 없습니다.
    if (this.game.session.settings.shake) this.shake = Math.max(this.shake, shake)
    if (this.game.session.settings.chromatic) {
      this.punch.hit(Math.min(chroma, this.game.feel.chromaticMaxPx * 2))
    }
    if (pulse > 0) this.game.background.pulse(pulse)
  }

  popAt(target: { x: number; y: number } | undefined, text: string, tint: number,
                intensity: number): void {
    // **크기는 계단에서 고릅니다.** 세기가 크면 한 칸 큽니다 — 24 또는 36.
    const label = new Text({
      text,
      style: {
        ...outlined(intensity > 0.5 ? TEXT.display : TEXT.base, UI.outline),
        fill: tint, fontWeight: WEIGHT.bold,
        // **뒤에 어두운 번짐 하나.** 판 위에는 카드와 그림이 깔려 있어서 글자만으로는
        // 읽히지 않습니다 — 만화의 번쩍임을 걷고 번짐으로 띄웁니다.
        dropShadow: { color: UI.outline, alpha: 0.9, blur: 6 + intensity * 6, distance: 0 },
      },
    })
    label.anchor.set(0.5, 0.5)
    label.resolution = this.game.textScale

    const flare = new Graphics()

    const node = new Container()
    node.addChild(flare, label)
    // **그 물건에서 나옵니다.** 부르는 쪽이 넘겨주는 자리가 곧 뜨는 자리이고, 카드에서
    // 나오는 것은 그 카드의 윗변에 살짝 걸치는 자리입니다(`RISER_ON_CARD`) — 값을 낸 것이
    // 무엇인지는 자리로만 읽히므로, 옆이나 아래에 띄우면 어느 것이 낸 값인지 끊깁니다.
    //
    // **떠오르는 거리는 위에 남은 자리만큼입니다.** 조커 줄은 화면의 맨 위라, 늘 46픽셀을
    // 올리면 글이 화면 밖으로 나가고 남는 것은 잘린 획 몇 개입니다.
    //
    // **판 안으로 들여 세웁니다.** 넘겨받은 자리를 그대로 쓰면 화면 변에 붙어 선 것에서
    // 나오는 글이 절반쯤 화면 밖입니다 — 그 물건에서 나온다는 것이 남으려면 글이 온전히
    // 보여야 하므로, 번쩍임까지 들어가는 가장 가까운 자리로 옮깁니다.
    const halfW = (flare.width || label.width) / 2
    const half = (flare.height || label.height) / 2
    const x = within(target ? target.x : BOARD_X, halfW, SIZE.width)
    const y = within(target ? target.y : SIZE.height / 2, half, SIZE.height)
    node.position.set(x, y)
    this.game.popLog.push({
      text, x: Math.round(x), y: Math.round(y),
      w: Math.round(halfW), h: Math.round(half),
    })
    if (this.game.popLog.length > 24) this.game.popLog.shift()
    const lift = Math.max(10, Math.min(RISER_LIFT, y - half - 10))
    // **떠오르는 글은 남아 있는 딱지 위입니다.** 산 물건이 그 자리에 잠깐 남으므로, 차례를
    // 적어 두지 않으면 낸 값이 그 물건 뒤로 들어갑니다.
    node.zIndex = 2
    this.game.overlay.addChild(node)

    // **그냥 뜨면 심심합니다.** 튀어나왔다가 부르르 떨며 올라갑니다.
    // **`tick` 을 거칩니다.** 틱커에 자기 콜백을 따로 걸면 히트스톱도 고정 단계도 타지
    // 않는 유일한 자리가 됩니다.
    node.scale.set(0.4)
    this.risers.push({
      node, life: 0, lift,
      homeX: node.x, homeY: node.y,
      drift: (Math.random() - 0.5) * 34,
      rumble: 3 + intensity * 7,
    })
  }

  /** 떠오르는 글자를 한 단계 올립니다. */
  advanceRisers(stepMs: number): void {
    for (let i = this.risers.length - 1; i >= 0; i--) {
      const one = this.risers[i]
      one.life += stepMs
      const t = Math.min(1, one.life / RISER_SPAN)

      // 처음 120밀리초는 튀어나오는 구간입니다. 1을 넘겼다가 돌아옵니다.
      const grow = one.life < 120
        ? 0.4 + 0.85 * (one.life / 120)
        : 1.25 - 0.25 * Math.min(1, (one.life - 120) / 220)
      one.node.scale.set(grow + t * 0.18)

      // **먼저 떠 있고 그다음에 옅어집니다.** 뜨는 순간부터 옅어지면 읽을 시간이 없습니다.
      const fade = t < RISER_HOLD ? 1 : 1 - (t - RISER_HOLD) / (1 - RISER_HOLD)
      const shiver = one.rumble * (1 - t) * (1 - t)
      one.node.x = one.homeX + one.drift * t + (Math.random() - 0.5) * shiver
      one.node.y = one.homeY - t * one.lift + (Math.random() - 0.5) * shiver
      one.node.rotation = (Math.random() - 0.5) * 0.05 * (1 - t)
      one.node.alpha = fade

      if (one.life < RISER_SPAN) continue
      one.node.destroy()
      this.risers.splice(i, 1)
    }
  }

  /**
   * 왼쪽 판의 수가 오르내린 것을 그 자리에 적습니다.
   *
   * **바뀐 것을 보여 주는 것과 지금 값을 보여 주는 것은 다른 일입니다.** 칸의 숫자는
   * 언제나 지금 값이라 「4」 가 「3」 이 되는 것은 눈을 그 칸에 두고 있어야만 보이고,
   * 화면 한가운데를 보고 있으면 그 사이에 무엇이 줄었는지 모른 채 지나갑니다 — 그래서
   * 줄어든 만큼이 그 칸에서 한 번 떠오릅니다.
   *
   * **글을 만들지 않고 돌려 씁니다.** 이것이 뜨는 자리는 사람이 누르는 자리가 아니라
   * 상태가 바뀌는 자리라, 조커 하나가 라운드마다 버리기를 주고 태그가 핸드를 주고 하는
   * 판에서는 한 프레임에 여럿이 겹칠 수 있습니다 — 그때마다 `Text` 하나와 티커 콜백
   * 하나를 만들면 만드는 값이 보여 주는 값보다 커집니다.
   *
   * **여덟이 넘으면 가장 오래된 것을 빼앗습니다.** 아홉째를 그리지 않고 버리는 쪽은
   * 그 순간 무엇이 바뀌었는지를 통째로 잃는 것이고, 가장 오래된 것은 이미 옅어져
   * 사라지는 중이므로 잃는 것이 적습니다.
   */
  slotDelta(slot: Slot, before: number, after: number, tint: number): void {
    if (before < 0 || after === before) return

    const delta = after - before
    // **숫자가 앉은 그 자리, 그 크기입니다.** 모서리에 작게 띄우면 그것은 곁에 적어 둔
    // 주석이고, 눈이 그 칸을 보고 있지 않으면 지나갑니다 — 바뀐 것이 값 자체의 자리를
    // 차지해야 화면 가운데를 보고 있어도 그것이 보입니다.
    const spot = slot.valueSpot
    const one = this.freeDelta()
    one.life = 0
    one.homeY = slot.y + spot.y
    one.node.text = `${delta > 0 ? '+' : ''}${delta}`
    one.node.style.fill = delta > 0 ? tint : UI.bad
    one.node.style.fontSize = spot.size
    one.node.anchor.set(spot.pull, 0.5)
    one.node.position.set(slot.x + spot.x, one.homeY)
    one.node.scale.set(1.5)
    one.node.alpha = 1
    one.node.visible = true
    // 같은 자리에 같은 크기의 수가 둘이면 어느 것도 읽히지 않습니다. 칸의 숫자가 그동안
    // 물러납니다.
    slot.mute()
    // **바탕도 그 방향으로 밝습니다.** 떠오르는 글은 0.8초 뒤에 없어지므로 그것을 놓치면
    // 무엇이 줄었는지가 남지 않습니다.
    slot.flash(delta > 0)
  }

  /**
   * 쓸 수 있는 글 하나.
   *
   * 노는 것이 있으면 그것이고, 없으면 만들고, 다 찼으면 가장 오래된 것입니다.
   */
  private freeDelta(): { node: Text; life: number; homeY: number } {
    const idle = this.deltas.find(one => !one.node.visible)
    if (idle) return idle

    if (this.deltas.length < DELTA_POOL) {
      const node = new Text({
        text: '', style: { ...outlined(TEXT.head, UI.outline, true), fontWeight: WEIGHT.bold,
                           fill: UI.ink, fontFamily: NUMERALS },
      })
      // 크기와 기준은 뜰 때마다 그 칸의 숫자에서 받습니다. **칸마다 글자 크기가 다르므로**
      // 여기서 정해 두면 어느 칸에서는 그 칸의 수보다 크거나 작게 뜹니다.
      node.anchor.set(0.5, 0.5)
      node.resolution = this.game.textScale
      node.visible = false
      this.game.board.addChild(node)
      const made = { node, life: 0, homeY: 0 }
      this.deltas.push(made)
      return made
    }

    return this.deltas.reduce((oldest, one) => one.life > oldest.life ? one : oldest)
  }

  /** 떠오르는 차이 글들. 다 떠오른 것은 다시 풀로 돌아갑니다. */
  advanceDeltas(seconds: number): void {
    for (const one of this.deltas) {
      if (!one.node.visible) continue
      one.life += seconds
      const t = Math.min(1, one.life / DELTA_LIFE)
      // **튀어나와 제 크기로 앉습니다.** 앞의 0.12초가 그 구간이고, 그 뒤로는 칸의 숫자와
      // 같은 크기입니다.
      one.node.scale.set(one.life < 0.12 ? 1.5 - 0.5 * (one.life / 0.12) : 1)
      // **앉아 있다가 떠오릅니다.** 곧바로 올라가기 시작하면 읽기 전에 자리를 떠나고,
      // 그러면 크게 띄운 뜻이 없어집니다.
      const rise = Math.max(0, (t - 0.4) / 0.6)
      one.node.y = one.homeY - rise * rise * 34
      one.node.alpha = 1 - rise * rise
      if (t >= 1) one.node.visible = false
    }
  }

  /** 번쩍임은 줄어듭니다. 패널은 빠르게, 화면은 더 빠르게 — 오래 남으면 눈이 아픕니다. */
  decayFlashes(seconds: number): void {
    // **모양은 색이 바뀔 때만, 밝기는 매 프레임.** 지오메트리를 다시 만드는 것이 비싼
    // 쪽이고 알파는 값 하나입니다 — 잦아드는 것은 알파로 합니다. 득점 중에는 카드마다
    // 번쩍이므로 이것이 사실상 매 프레임 돌던 것입니다.
    //
    // **테는 두르지 않습니다.** 굵기와 색이 함께 움직이는 테는 카드가 하나씩 득점하는
    // 동안 판의 윤곽이 내내 자랐다 줄어드는 것이 되고, 그 움직임이 정작 읽어야 하는
    // 숫자보다 큽니다 — 그것을 걷고 나서 모양은 색 하나에만 달립니다.
    if (this.panelGlow > 0.002) {
      this.panelGlow = Math.max(0, this.panelGlow - seconds * 3.6)
      const ease = this.panelGlow * this.panelGlow
      if (this.panelTint !== this.panelKey) {
        this.panelKey = this.panelTint
        this.game.chrome.panelFlash.clear()
        this.game.chrome.panelFlash.rect(LEFT - 12, 22, PANEL_W + 24, SIZE.height - 44)
          .fill({ color: this.panelTint, alpha: 0.3 })
      }
      this.game.chrome.panelFlash.alpha = ease
      this.panelDrawn = true
    } else if (this.panelDrawn) {
      this.game.chrome.panelFlash.clear()
      this.panelKey = -1
      this.panelDrawn = false
    }

    if (this.screenGlow > 0.002) {
      this.screenGlow = Math.max(0, this.screenGlow - seconds * 4.4)
      if (this.screenTint !== this.screenKey) {
        this.screenKey = this.screenTint
        this.screenFlash.clear()
        this.screenFlash.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
          .fill({ color: this.screenTint, alpha: 1 })
      }
      this.screenFlash.alpha = this.screenGlow * this.screenGlow
      this.screenDrawn = true
    } else if (this.screenDrawn) {
      this.screenFlash.clear()
      this.screenKey = -1
      this.screenDrawn = false
    }
  }

  /** 배경이 얼마나 뜨거운가. 점수가 요구에 가까울수록 올라갑니다. */
  heat(): number {
    if (this.game.state.phase === 'shop') return 0.15
    if (this.game.state.target <= 0) return 0.1
    return Math.max(0.08, Math.min(1, this.game.shown.score / Number(this.game.state.target)))
  }

  /**
   * 국면이 배경의 색을 정합니다.
   *
   * **어디에 있는지가 배경만 보고도 읽혀야 합니다.** 스몰은 초록, 빅은 호박, 보스는 붉고,
   * 상점은 푸르고, 끝났으면 색이 빠집니다.
   */
  /**
   * 어느 곡이 흐르는가.
   *
   * **화면마다 하나입니다.** 타이틀과 판과 상점은 하는 일이 다르므로 분위기도 달라야 하고,
   * 넘어갈 때는 겹쳐서 넘어가므로 끊긴 자리가 들리지 않습니다.
   */
  syncMusic(): void {
    if (this.game.session.scene !== 'run') {
      this.game.audio.music.dim = 1
      this.game.audio.music.play('title')
      return
    }
    // 끝난 판에서는 조용합니다. **끝난 것 위로 음악이 계속 흐르면 끝난 것이 아닙니다.**
    // **화면의 국면입니다.** 마지막 핸드의 득점이 도는 동안 음악이 멎으면 끝난 것이
    // 아직 보이기도 전에 끝난 것으로 들립니다.
    const phase = this.game.shown.phase
    if (phase === 'lost' || phase === 'won') {
      this.game.audio.music.play(undefined)
      return
    }
    // **블라인드를 고르는 동안은 반만 냅니다.** 무엇과 붙을지 정하는 자리이므로 판이 도는
    // 중이 아니고, 고르고 나서 곡이 제 크기로 드는 것이 판이 시작된 것입니다.
    //
    // 한동안 여기서 곡을 끊었는데, 그러면 고르는 자리마다 분위기가 한 번 없어지고 판이
    // 여러 화면으로 토막납니다 — 같은 곡이 낮게 흐르고 있으면 고르는 것이 그 판의 앞부분이
    // 됩니다. **곡을 바꾸지 않고 이득만 옮깁니다** — 갈아 끼우면 고른 그 순간에 곡이
    // 처음으로 돌아갑니다.
    //
    // **낮춤을 먼저 정하고 나서 켭니다.** 판이 열리는 첫 프레임이 곧 이 자리이므로,
    // 순서가 바뀌면 그 한 프레임에 제 크기가 나고 그것이 「퍽」으로 들립니다.
    this.game.audio.music.dim = phase === 'blind-select' ? BLIND_MUSIC_DIM : 1
    this.game.audio.music.play(phase === 'shop' ? 'shop' : 'round')
  }

  /**
   * 배경의 색.
   *
   * **환희의 겹도 같은 값을 받습니다.** 겹이 라운드의 색을 모르면 보스 라운드에서 배경만
   * 붉고 그 위의 기는 초록인 화면이 됩니다.
   */
  private setMood(ink: [number, number, number], glow: [number, number, number]): void {
    this.game.background.setMood(ink, glow)
    this.euphoria.setMood(ink, glow)
  }

  /**
   * 어느 배경을 그릴 것인가.
   *
   * **씬이 갈릴 때마다 부릅니다.** 판 밖의 두 화면은 [앞 배경](../shader/front.ts)이고
   * 판은 프랙탈입니다 — 덮여서 보이지 않는 쪽은 스프라이트를 끕니다. 스프라이트 하나가
   * 곧 화면 한 장을 셰이더로 굽는 일이므로, 켜 둔 채로 가리면 그 값이 그대로 나갑니다.
   */
  syncBackdrop(): void {
    const front = this.game.session.scene !== 'run'
    this.frontSheet.visible = front
    this.sheet.visible = !front
    // **빛이 나오는 자리는 이름 뒤입니다.** 두 화면의 이름이 서로 다른 높이에 있으므로,
    // 한 자리로 두면 한쪽에서는 이름 아래에서 빛이 퍼집니다.
    if (front) this.front.setOrigin(0.5, this.game.session.scene === 'login' ? 0.24 : 0.33)
  }

  syncMood(): void {
    const state = this.game.state

    // **판 밖의 두 화면은 프랙탈이 아닙니다.** 색을 정할 것이 없습니다 — `syncBackdrop`
    // 이 앞 배경으로 갈아 끼웁니다. 다만 판으로 들어갈 때 프랙탈이 앞 국면의 색으로
    // 남아 있지 않도록 값은 그대로 넣어 둡니다.
    if (this.game.session.scene !== 'run') {
      this.setMood([0.012, 0.030, 0.020], [0.10, 0.34, 0.20])
      return
    }

    // 배경도 연출이 끝난 뒤에 갑니다. 득점 중에 색이 바뀌면 무엇이 끝난 것인지 흐려집니다.
    if (!this.game.presented) return

    if (state.phase === 'lost') {
      this.setMood([0.05, 0.05, 0.058], [0.55, 0.5, 0.55])
      return
    }
    if (state.phase === 'won') {
      this.setMood([0.075, 0.062, 0.026], [1, 0.82, 0.34])
      return
    }
    if (state.phase === 'shop') {
      this.setMood([0.032, 0.062, 0.072], [0.32, 0.86, 0.82])
      return
    }

    switch (state.blind) {
      case BlindKind.Boss:
        this.setMood([0.082, 0.024, 0.04], [1, 0.26, 0.33])
        break
      case BlindKind.Big:
        this.setMood([0.062, 0.042, 0.082], [0.72, 0.42, 0.98])
        break
      default:
        this.setMood([0.042, 0.052, 0.086], [0.30, 0.52, 0.98])
        break
    }
  }

  /** 판을 짓습니다. 뜯을 때 한 번입니다. */
  buildPack(): void {
    this.game.pack.packLayer.removeChildren().forEach(child => child.destroy())
    this.game.pack.packViews.clear()
    this.game.pack.packGone.length = 0

    const open = this.game.state.pack
    if (!open) return

    this.game.pack.packLayer.sortableChildren = true
    this.game.pack.packLayer.visible = true
    this.game.pack.packEnter = 0

    // 팩 딱지에 떠 있던 설명을 걷습니다. 뜯은 판 뒤에 남으면 지저분합니다.
    this.game.input.tooltip.hide()

    const row = this.game.data.tables.boosterPack.findByPackId(open.packId)
    const ink = packInk(open.kind)

    // **뒤를 덮는 막이 없습니다.** 상점이 내려간 판 위에 그대로 깔립니다 — 팩에서 고르는
    // 것은 손패를 한 번 더 치는 것이고, 덮개를 치면 그것이 다른 화면이 됩니다.

    // **뜯은 것의 이름입니다.** 26픽셀에 자간 없이 두었더니 그 아래의 지시문과 굵기만
    // 다른 두 줄이 되어서, 무엇을 뜯었는지가 읽히지 않고 지나갔습니다 — 이름은 크게,
    // 자간을 벌려서, 그리고 지시문과 사이를 두어야 이름으로 읽힙니다.
    //
    // **색은 밝힌 쪽입니다.** 팩의 색을 그대로 쓰면 어두운 판과 밝기가 거의 같아서, 크게
    // 굵게 적어도 이름으로 읽히지 않습니다 — `packInkLit` 이 색조를 그대로 두고 밝힙니다.
    const title = new Text({
      text: row ? packName(row.kind, row.size) : t('ui.kind.pack'),
      style: {
        ...outlined(TEXT.hero, UI.scrim),
        fill: packInkLit(open.kind), fontWeight: WEIGHT.bold, letterSpacing: 2,
      },
    })
    title.anchor.set(0.5, 0)
    title.position.set(PACK_X, PACK_TITLE_Y)

    // **지시문은 여기 한 곳입니다.** 화면 아래에도 같은 말을 두면 덮개 뒤에서 흐릿하게
    // 읽히고, 그것은 남은 글자로 보입니다.
    const note = new Text({
      text: this.game.pack.packLine(open.picksLeft),
      style: { fontSize: TEXT.copy, fill: UI.ink, fontWeight: WEIGHT.normal },
    })
    note.anchor.set(0.5, 0)
    note.position.set(PACK_X, PACK_TITLE_Y + 46)
    this.game.pack.packNote = note

    // **판 아래 단추 줄입니다.** 라운드에서 낸다·취소·버린다가 놓이는 그 줄이고, 그 크기입니다 —
    // 팩에서 고르는 것은 손패를 한 번 더 치는 것이므로 단추도 그 자리에 놓입니다.
    // 블라인드를 건너뛰는 것과 같은 소리입니다. 소리 없이 판이 걷히던 유일한 자리였습니다.
    // **건너뛰기는 `lg` 입니다.** 판을 움직이는 낸다·버린다만 `xl` 이고, 이것은 그 줄의
    // 자리를 잠깐 빌려 쓰는 나아가는 단추입니다 — 줄의 세로 가운데에 앉습니다.
    const skip = new Button(t('ui.button.skip'), PLAY_W, 60, 'neutral', () => {
      this.game.audio.play('blind_skip')
      this.game.act({ t: 'skip_pack' })
    })
    skip.position.set(PACK_X - PLAY_W / 2, BUTTON_Y + (PLAY_H - 60) / 2)
    this.game.pack.packSkip = skip
    // 도구가 팩을 건너뛰는 자리입니다. 걷을 때 함께 지웁니다.
    this.game.spotNodes.set('packSkip', { node: skip, cx: PLAY_W / 2, cy: 30 })

    this.game.pack.packTitle = title

    this.game.pack.packLayer.addChild(title, note, skip)

    // **카드는 손패가 깔리는 자리에서 옵니다.** 라운드의 카드가 오는 딜러의 자리이고,
    // 그래서 팩에서 고르는 것이 손패를 한 번 더 받는 것으로 읽힙니다. 뜯은 딱지는 상점과
    // 함께 화면 아래로 내려가 있어 그 자리에서 낼 것이 없습니다.
    const from = { x: DEALER.x, y: DEALER.y }

    open.options.forEach((item, index) => {
      if (open.taken[index]) return

      const face = this.game.pack.packCard(item, index)
      const node = face.node
      const motion = new Motion()
      motion.snap(from.x, from.y)
      motion.rotation.snap(0)
      node.position.set(from.x, from.y)
      node.alpha = 0

      this.game.pack.packViews.set(index, {
        face, motion, index, item, lift: new Spring(),
        look: this.game.input.lookOf(face.card),
        // 황금비만큼씩 벌려 둡니다. 정수 배로 벌리면 장수가 짝수일 때 두 장씩 같은 자리가
        // 됩니다.
        sway: index * 2.399_96,
        glow: -1,
      })
      this.game.pack.packLayer.addChild(node)

      // 하나씩 나옵니다. 나오는 순간에 소리가 하나.
      this.game.later.push({
        at: this.game.clock + 0.12 + index * 0.11,
        run: () => {
          node.alpha = 1
          // **나오는 그 한 장이 반짝입니다.** 소리만 나고 그림은 그냥 있으면 다섯 장이
          // 한꺼번에 놓인 것으로 보입니다 — 하나씩 나온다는 것은 하나씩 눈에 띈다는
          // 것이고, 눈에 띄게 하는 것은 그 순간의 빛입니다.
          const one = this.game.pack.packViews.get(index)
          if (one) one.glow = 0
          // **뜯은 팩에서 나오는 것은 뒤집히는 소리입니다.** 손에 깔리는 것과 갈립니다.
          this.game.audio.play('card_flip', index * 2)
          // 조각은 그 카드가 설 자리에서 납니다. 나오는 자리는 화면 밖입니다.
          if (one) this.particles.burst(one.motion.x.target, one.motion.y.target, 8, ink, 0.7,
            0.6)
        },
      })
    })
  }
}
