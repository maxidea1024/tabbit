import { PAINT } from '../render/ink'
import { type Application, Container, Graphics, Rectangle, Text } from 'pixi.js'
import { type Data } from '../core/data'
import { type Action, apply, newRun } from '../core/run'
import { t, tf } from '../core/strings'
import { type RunSetup } from '../ui/setup'
import { type GameEvent, type RunState } from '../core/state'
import { BackgroundFilter } from '../shader/background'
import { payoutLevel } from '../shader/wave'
import { Audio } from '../feedback/audio'
import { type Feel, readFeel, TimelinePlayer } from '../render/juice'
import { fraction } from '../render/motion'
import { artTick, onArtReady, setArtDensity } from '../render/art'
import { backLookOf, bakeCardBacks, forgetCardBacks, setCardBack } from '../render/card-back'
import { useMotesLayer } from '../render/motes-layer'
import { cardBackMotif, setCardSet, setLookOf } from '../render/card-set'
import { bakeCardFaces, forgetCardFaces } from '../render/card-face'
import { SIZE, UI } from '../render/theme'
import { box, type Box, splitX } from '../ui/layout'
import { Button } from '../ui/widgets'
import { CollectionPanel } from '../ui/collection'
import { discover, saveCollection, sightings } from '../core/collection'
import { RunPanel } from '../ui/run-panel'
import { randomSeed, Title } from '../ui/title'
import { LeaderboardHub } from '../ui/hub'
import { NetStatus } from '../ui/net-status'
import { LoginScene } from '../ui/login-scene'
import { nativeShell, onAppState, onBackButton } from '../ui/shell'
import { observe } from '../core/metrics'
import { type Crossings, readCrossings, Transition } from '../render/transition'
import { type ToolSpot } from '../ui/layout'
import { OptionsPanel, saveOptions } from '../ui/options'
import {
  BOARD_X, BUTTON_GAP, BUTTON_Y, CLEAR_W, DECK_MEET, FOOT_BTN_H, HELD_RISE, ITEM_LINGER,
  ITEM_SETTLE, JOKER_Y, LEFT, PANEL_BTN_W, PANEL_FOOT_Y, PANEL_W, PLAY_H, PLAY_W, PLAY_Y,
  RIGHT_COL, SORT_H, SORT_W, STEP_MS,
} from './metrics'
import { blurResolution } from './helpers'
import {
  BlindPart, CardsPart, ChromePart, InputPart, PackPart, PanelsPart, PayoutPart, ProbePart,
  SessionPart, ShopPart, ShowPart, TrayPart,
} from './parts'
import * as account from '../net/session'
/** 그림이 들어온 뒤 이만큼 더 들어오지 않으면 다시 그립니다. 초입니다. */
const ART_QUIET = 0.1
/** 그림이 잇달아 들어와도 첫 도착에서 이만큼 지나면 다시 그립니다. 초입니다. */
const ART_WAIT = 0.3
/**
 * 그림이 놓인 뒤 이 안에는 다시 그립니다. 초입니다.
 *
 * **놓인 것은 `RETIRE_TICKS` 안에 다시 그려야 합니다** — 그 뒤에 버려지므로. 30틱은 120Hz
 * 에서 0.25초이고 이 값은 그보다 짧습니다. 그 자리에서 곧장 그리지 않는 것은, 상한에 걸려
 * 놓는 것이 도착마다 하나씩 이어지면 도착마다 판 전체를 다시 세우게 되기 때문입니다.
 */
const ART_DROP_WAIT = 0.08

export class Game {

  // 부분들. **판이 들고 있고, 부분끼리는 판을 거쳐 서로에게 닿습니다.**
  /** 연출. */
  readonly show = new ShowPart(this)
  /** 왼쪽 판과 단추. */
  readonly chrome = new ChromePart(this)
  /** 카드와 조커. */
  readonly cards = new CardsPart(this)
  /** 들고 있는 것. */
  readonly tray = new TrayPart(this)
  /** 블라인드와 태그. */
  readonly blind = new BlindPart(this)
  /** 상점. */
  readonly shop = new ShopPart(this)
  /** 팩. */
  readonly pack = new PackPart(this)
  /** 정산. */
  readonly payout = new PayoutPart(this)
  /** 펼쳐 보는 판. */
  readonly panels = new PanelsPart(this)
  /** 누름과 끌기. */
  readonly input = new InputPart(this)
  /** 판의 생애. */
  readonly session = new SessionPart(this)
  /** 도구가 조회하는 것. */
  readonly probe = new ProbePart(this)

  readonly world = new Container()

  /**
   * 화면 전부.
   *
   * **배경과 판이 한 통에 있습니다.** 씬이 갈릴 때 들어오는 화면에 필터를 거는데, 그
   * 대상이 판만이면 배경만 처리되지 않은 채로 남습니다 — 화면이 갈리는 것은 판에만
   * 일어나는 일이 아닙니다.
   */
  readonly screen = new Container()

  /**
   * 판 밖을 잘라 내는 사각형.
   *
   * **무대의 마스크입니다.** 판은 1280 × 800 하나이고 창의 비율은 기계마다 다릅니다 —
   * 남는 자리를 배경으로 채우면 판이 더 넓은 화면 가운데에 놓인 사각형으로 보이고, 비율
   * 마다 다른 화면이 됩니다.
   */
  readonly cropBox = new Graphics()

  /** 잘라 낸 사각형. 검증 도구가 읽습니다. */
  cropRect?: Box

  readonly board = new Container()

  readonly overlay = new Container()

  /**
   * 판의 상태.
   *
   * **코어가 제자리에서 고칩니다** — `apply` 는 이 객체를 받아 바꾸고 이벤트만 돌려줍니다.
   * 이 객체를 갈아 끼우는 곳은 **시작 전의 `useSeed` 하나뿐**입니다.
   */
  state: RunState

  readonly feel: Feel

  readonly audio: Audio

  readonly player: TimelinePlayer

  readonly background = new BackgroundFilter()

  /**
   * 화면과 화면 사이.
   *
   * **규격은 `doc/ui/transition.md` 입니다.** 씬을 바꾸는 자리는 「무엇을 할지」만 넘기고,
   * 언제 할지는 그쪽이 정합니다 — 완전히 덮인 프레임입니다.
   */
  readonly transition = new Transition({
    shoot: () => this.session.shoot(),
    play: cue => this.audio.play(cue),
    screen: this.screen,
    // **닫힘으로 넘깁니다.** 필드의 초기식이 도는 자리에는 `app` 이 아직 없습니다 — 생성자의
    // 매개변수 프로퍼티는 필드 다음에 놓입니다.
    renderer: () => this.app?.renderer,
  })

  /**
   * 자리마다 화면을 어떻게 지우는가. **시트가 정합니다.**
   *
   * 생성자에서 채웁니다 — 필드의 초기식은 생성자 본문보다 먼저 도는데 그 자리에는 아직
   * 표가 없습니다.
   */
  crossings!: Crossings

  /**
   * 눌러야 하는 것들의 자리.
   *
   * **자리를 계산하는 곳이 하나여야 합니다.** 판의 밑단은 글의 길이에 따라 자라므로,
   * 바깥에서 같은 계산을 다시 하면 말을 바꾼 날에 어긋납니다.
   */
  readonly spots: Record<string, { x: number; y: number }> = {}

  /**
   * 잠시 뒤에 한 번 할 것.
   *
   * **닿는 순간에 해야 하는 것들이 있습니다** — 산 조커가 줄에 꽂히는 것은 날아가는
   * 시간만큼 뒤입니다. 그때에 맞춰 소리와 조각을 냅니다.
   */
  readonly later: { at: number; run: () => void }[] = []

  /** 프레임 안에서 던진 것들. 새것이 뒤입니다. **소리 없이 멈춘 판의 까닭이 여기 적힙니다.** */
  readonly errors: string[] = []

  /** 한 자리가 던져도 프레임의 나머지가 돌게 합니다. 던진 것은 적어 둡니다. */
  private guard(where: string, run: () => void): void {
    try {
      run()
    } catch (error) {
      this.note_(`${where}: ${String(error).slice(0, 160)}`)
    }
  }

  private note_(line: string): void {
    if (this.errors[this.errors.length - 1] === line) return
    this.errors.push(line)
    if (this.errors.length > 8) this.errors.shift()
  }

  /**
   * 나중에 세는 자리들.
   *
   * **`spots` 와 갈립니다.** 그쪽은 그리는 그 자리에서 셈이 끝나는 것들이고, 이쪽은 판이
   * 화면의 어디에 서는지가 그 뒤에 정해지는 것들입니다 — 판을 세우는 중에 세면 아직
   * 자리가 정해지지 않은 판의 왼쪽 위가 나옵니다.
   */
  readonly spotNodes = new Map<string, ToolSpot>()

  /** 그림이 놓였는가. `tick` 이 그 프레임에 처리합니다. */
  private artDirty = false
  /** 마지막 그림이 들어온 뒤 이만큼 조용하면 다시 그립니다. */
  private artQuietAt = Infinity
  /** 그림이 계속 들어와도 이때는 다시 그립니다. 첫 도착에서 셉니다. */
  private artDueAt = Infinity

  /** 점수가 멈춘 뒤 낸 카드를 얼마나 붙잡아 두었는가. */
  holdAfterScore = 0

  /**
   * 고정 단계에 아직 쓰지 않은 시간. 밀리초.
   *
   * **매 프레임 난수를 새로 뽑는 것들은 고정 단계에서만 전진합니다.** 판의 흔들림 ·
   * 숫자 칸의 떨림 · 떠오르는 글자의 떨림이 그것입니다 — 프레임마다 뽑으면 떨림의
   * 주파수가 모니터 주사율이 되어 144Hz 에서는 잔떨림이고 30Hz 에서는 흔들거림입니다.
   * 용수철과 예약 큐는 여기 없습니다. 그것들은 이미 시간으로만 움직입니다.
   */
  private stepDebt = 0

  /**
   * 떠오른 글이 어디에서 떴는가. 뒤가 새것이고 16개까지 남습니다.
   *
   * **자리가 틀린 것은 값으로만 확인됩니다.** 글은 0.8초 뒤에 없어지므로 스크린샷은 그
   * 순간을 잡지 못하고, 판 밖이나 왼쪽 위에 뜬 것은 사람이 그 프레임을 보고 있어야만
   * 보입니다 — 뜬 자리를 그대로 알려 도구가 판 안인지 판정합니다.
   */
  readonly popLog:
    { text: string; x: number; y: number; w: number; h: number }[] = []

  /**
   * 화면이 지금 주장하고 있는 것.
   *
   * **코어는 액션 하나를 끝까지 처리하고 답을 돌려줍니다.** 그 답을 그대로 그리면 카드가
   * 아직 날아가는 중에 최종 점수가 떠 있고, 다음 패가 이미 깔려 있고, 격파 보상이 이미
   * 들어와 있습니다 — 연출이 도는 의미가 없어집니다.
   *
   * 그래서 화면은 **박자가 도달한 데까지만** 압니다. 연출이 끝나면 상태와 같아집니다.
   */
  /**
   * 화면이 주장하는 것. 점수·금액·손패와 함께 **국면**도 여기 있습니다.
   *
   * 판을 떠나는 것(격파·패배·승리)은 코어에서는 액션 한 번에 끝나지만, 화면에서는 득점이
   * 끝나고 카드가 걷혀 덱으로 돌아간 뒤의 일입니다. 덱이 물러나는 것·음악이 멎는 것·손패
   * 뷰를 거두는 것은 이 국면을 기준으로 합니다 — 코어의 국면을 보면 마지막 핸드를 낸 그 프레임에
   * 손패가 사라지고 덱이 빠지기 시작합니다.
   */
  shown = {
    score: 0, money: 0, hand: [] as number[], phase: 'blind-select' as RunState['phase'],
  }

  clock = 0

  constructor(readonly app: Application, readonly data: Data, seed: string,
              /** 시간이 `__clover.advance` 로만 흐릅니다. 검증 도구가 `?tick=manual` 로 켭니다. */
              readonly manualTick = false) {
    this.feel = readFeel(data.feel)
    // **씬이 갈리는 방법도 시트에 있습니다.** 화면을 세우기 전에 읽어 둡니다 — 첫 전환은
    // 부팅이 끝나는 그 자리입니다.
    this.crossings = readCrossings(data)
    this.session.bootSeed = seed
    this.audio = new Audio(data.tables)
    const first = this.session.setup()
    this.state = newRun(data, seed, first.deckId, first.stake).state
    this.player = new TimelinePlayer(beat => this.show.showBeat(beat))
    this.session.hub = new LeaderboardHub(data, this.panels.modals, this.input.toasts)
    this.session.netStatus = new NetStatus(this.input.toasts)
    this.session.title = new Title({
      onStart: () => this.session.openRunPanel(),
      onGuide: () => this.panels.modals.open(this.panels.guide),
      onOptions: () => this.session.openOptions(),
      onCollection: () => this.panels.modals.open(this.panels.collection),
      onLeaderboard: () => this.session.hub.openLeaderboard(),
      onAccount: () => this.session.openAccount(),
      onSignOut: () => this.session.signOut(),
      onQuit: () => this.session.askQuit(),
    })
    this.session.hub.onAccountChanged = () => this.session.syncAccount()
    this.session.hub.onNeedLogin = () => this.session.cross('title_login',
      () => this.session.enterLogin())
    this.session.hub.onSignOut = () => this.session.signOut()
    this.session.login = new LoginScene()
    // **로그인 화면에서 고른 말이 옵션에도 남습니다.** 그러지 않으면 다음에 켤 때
    // 되돌아가고, 사람은 같은 것을 두 번 고르게 됩니다.
    this.session.login.onLanguage = language => {
      this.session.settings.language = language
      saveOptions(this.session.settings)
      this.session.applyOptions()
    }
    this.session.login.onQuit = () => this.session.askQuit()
    this.session.login.onBusy = level => { this.show.frontHaze = level }
    this.session.login.onSingle = () => {
      account.playAsGuest()
      this.session.cross('login_title', () => this.session.enterTitle())
    }
    // 개발용 로그인은 제공자를 지난 것과 같은 자리입니다 — 내 것을 읽고 타이틀로 갑니다.
    this.session.login.onSignedIn = () =>
      void this.session.hub.refresh().then(() => this.session.cross('login_title',
        () => this.session.enterTitle()))
    // 도감. **보는 곳이고 고르는 곳이 아닙니다** — 다음 판의 풀은 판을 여는 자리에서
    // 고릅니다. 처음 열 때 조커 탭이 보여 주는 범위만 그 값을 따릅니다.
    // **칸을 굽는 렌더러와 글씨의 배율을 넘깁니다.** 배율은 창의 크기를 따라 바뀌므로 값이
    // 아니라 읽는 함수입니다.
    this.panels.collection = new CollectionPanel(data, this.session.collected,
      () => this.panels.modals.close(this.panels.collection),
      { renderer: this.app.renderer, density: () => this.textScale })
    this.panels.optionsPanel = new OptionsPanel(data, this.session.settings,
      () => this.session.applyOptions(),
      () => this.panels.modals.close(this.panels.optionsPanel))
    this.panels.optionsPanel.onSeed = next => this.session.useSeed(next)

    // 판을 여는 자리 하나. **탭 셋이 저마다 판을 엽니다.**
    this.panels.runPanel = new RunPanel(data, this.session.setup(), this.session.challenges, {
      onClose: () => this.panels.modals.close(this.panels.runPanel),
      // **고른 그 자리에서 저장합니다.** 판을 닫을 때 저장하면 판을 닫지 않고 시작한
      // 판이 다음 번에 다른 덱으로 열립니다.
      onPickSetup: (next: RunSetup) => {
        this.session.settings.deck = next.deckId
        this.session.settings.stake = next.stake
        saveOptions(this.session.settings)
      },
      // **고른 것으로 곧바로 판을 엽니다.** 판을 닫고 시작을 다시 누르게 하면 무엇으로
      // 시작하는지가 두 화면에 걸쳐 있게 됩니다.
      onStartNew: (next: RunSetup) => {
        this.session.askStartNew(next, () => {
          this.panels.modals.closeAll()
          this.session.challengeId = ''
          this.session.settings.deck = next.deckId
          this.session.settings.stake = next.stake
          saveOptions(this.session.settings)
          this.session.hub.clearRanked()
          this.session.cross('title_run', () => {
            this.session.layRun(randomSeed())
            this.session.enterRun()
          })
        })
      },
      onStartChallenge: (challengeId: string) => {
        this.panels.modals.close(this.panels.runPanel)
        this.session.challengeId = challengeId
        this.session.hub.clearRanked()
        this.session.cross('title_run', () => {
          this.session.layRun(randomSeed())
          this.session.enterRun()
        })
      },
      onStartRanked: () => {
        this.panels.modals.close(this.panels.runPanel)
        void this.session.startRanked()
      },
      onResume: () => {
        this.panels.modals.close(this.panels.runPanel)
        this.session.cross('title_run', () => this.session.continueRun())
      },
      onDiscard: () => this.session.askDiscardRun(),
    })

    /**
     * 흐림이 굽는 자리를 **못박아 둡니다.**
     *
     * 정하지 않으면 Pixi 가 이 통에 든 것들의 경계를 매 프레임 재고, 그 경계가 굽는 자리가
     * 됩니다 — 카드와 조각이 움직이므로 그 자리가 프레임마다 달라지고, 반 해상도의 텍셀
     * 격자에 맞추는 자리도 함께 달라집니다. 그러면 흐린 그림이 계속 미세하게 떱니다.
     *
     * 이 통의 좌표는 언제나 기준 해상도입니다 — 창에 맞추는 것은 바깥의 `world` 가
     * 합니다. 그래서 값 하나를 한 번 적어 두면 됩니다.
     */
    this.show.recede.filterArea = new Rectangle(0, 0, SIZE.width, SIZE.height)

    // 배경은 흰 스프라이트 한 장에 셰이더를 얹은 것입니다.
    this.show.sheet.filters = [this.background]
    this.show.frontSheet.filters = [this.show.front]
    this.show.backdrop.addChild(this.show.sheet, this.show.frontSheet, this.show.euphoria.view)
    this.show.syncBackdrop()
    // 기가 모이는 자리는 낸 카드가 놓인 자리입니다. **판의 좌표는 고정이므로 한 번 적습니다.**
    //
    // **배경의 고리도 같은 자리에서 퍼집니다.** 왼쪽 판이 280픽셀을 쓰므로 카드가 놓이는
    // 자리의 가운데는 화면의 가운데가 아니고, 화면 가운데에서 퍼지는 고리는 그 한 방이
    // 카드에서 난 것으로 읽히지 않습니다.
    this.show.euphoria.setCenter(BOARD_X / SIZE.width, PLAY_Y / SIZE.height)
    this.background.setCenter(BOARD_X / SIZE.width, PLAY_Y / SIZE.height)

    // **판 밖은 잘라 냅니다.** 판은 1280 × 800 하나에 맞춰 그려지고, 창의 비율이 다르면
    // 옆이나 아래가 남습니다 — 배경이 그 자리까지 덮고 있었고, 그러면 판이 더 넓은 화면
    // 가운데에 놓인 사각형 하나로 보입니다. 폰을 가로로 쥐면 좌우가 26%씩 그렇게 남습니다.
    //
    // **비율이 제각각인 것을 한 규칙으로 처리하려면 자르는 편이 낫습니다.** 갤럭시 폴드는
    // 접으면 2.56, 펴면 1.25 이고, 그 사이의 어느 값에서도 판의 자리는 그대로여야 합니다 —
    // 남는 자리를 화면의 일부로 두면 그 값마다 다른 화면이 됩니다.
    //
    // 마스크 하나로 무대 전체를 자릅니다. 판 밖으로 나가는 것이 배경만이 아니기
    // 때문입니다 — 번쩍임은 `-2000` 부터 그리고 모달의 막은 판의 3배입니다.
    this.screen.addChild(this.show.backdrop, this.world)
    // **전환은 화면 위입니다.** 나가는 화면의 사진이 그 화면 위에 놓여야 하고, 들어오는
    // 화면에 걸리는 필터가 이 층까지 처리하면 안 됩니다.
    app.stage.addChild(this.screen, this.transition.view, this.cropBox)
    app.stage.mask = this.cropBox

    // **타이틀은 독립된 화면입니다.** 시작을 누르기 전에는 판도 조각들도 그리지 않습니다 —
    // 가려 두는 것과 없는 것은 다르고, 반투명한 판 뒤로 카드가 비치면 시작 전인지가
    // 흐려집니다.
    this.board.visible = false
    this.overlay.visible = false
    // **켤 때 옵션을 겁니다.** 판을 열 때만 걸고 있어서, 타이틀과 로그인 화면의 소리는
    // 만들 때의 기본값으로 났습니다 — 옵션에 적힌 값과 실제로 나는 값이 달랐습니다.
    this.session.applyQuietOptions()
    // **타이틀은 판 바깥입니다.** 판과 조각들을 통째로 끄고 그 위에 홀로 뜹니다.
    // **알갱이는 조각보다 위, 떠 있는 판들보다 아래입니다.** 삭는 카드는 판 위의 일이고,
    // 그 위에 상점이나 옵션이 떠 있으면 알갱이가 그 뒤로 가야 합니다.
    this.show.recede.addChild(this.board, this.show.particles, this.show.motes, this.overlay,
      this.show.screenFlash, this.session.title)
    // **알림은 판 위입니다.** 흐려지는 층 안에 있어서 판이 열려 있는 동안의 알림이 그 판
    // 뒤에서 흐린 채로 떴습니다 — 순위표를 열었을 때의 「서버가 받지 않았습니다」가 정확히
    // 그 자리였고, 알림은 무엇이 열려 있든 읽혀야 하는 것입니다.
    // **동전은 모달 위입니다.** 정산 판에서 금액 칸으로 날아가므로 그 판보다 위여야 하고,
    // 모달이 열려 흐려지는 층 안에 있으면 그 동전도 함께 흐려집니다.
    //
    // **차례는 `zIndex` 가 정합니다.** Pixi 는 `zIndex` 가 0 이 아닌 자식이 들어오면 그 부모를
    // 정렬하는 것으로 바꾸므로(`depthOfChildModified`), 모달(9,500)이 들어온 순간부터 이 층의
    // `addChild` 순서는 그림의 차례가 아닙니다 — 동전도 값을 받아야 모달 위에 놓입니다. 통신
    // 표시(9,800)보다는 아래입니다.
    this.payout.coins.zIndex = 9_600
    this.world.addChild(this.show.recede, this.panels.modals, this.payout.coins,
      this.input.toasts, this.input.tooltip)

    // **내 카드가 계정 칩의 자리에 놓입니다.** 이름을 두 곳에 적으면 같은 것을 두 번 보게
    // 되고, 카드에는 순위까지 있으므로 칩이 남을 이유가 없습니다.
    this.session.title.accountSlot.addChild(this.session.hub.card)
    this.session.login.visible = false
    this.show.recede.addChild(this.session.login)

    // 통신 표시와 입력 막이. **판보다 위입니다.**
    this.world.addChild(this.session.netStatus)

    // 되돌아온 주소를 보고, 로그인되어 있으면 내 것을 읽습니다. 그다음에 어느 씬으로
    // 갈지가 정해집니다 — **로그인했거나 싱글플레이로 정했으면 타이틀입니다.**
    void this.session.hub.boot().then(() => this.session.openingScene())

    // **잔액은 동전이 닿는 그 순간에 그 몫만큼 바뀝니다.** 동전이 뜨는 순간에 바뀌면 동전은
    // 이미 끝난 일을 뒤따라가는 그림이고, 첫 동전에 끝값으로 뛰면 나머지 동전은 뜻이
    // 없습니다. 닿을 때마다 칸이 튀고 음이 하나 올라갑니다.
    this.payout.coins.onLand = (index, gain, share) => {
      this.shown.money += share
      this.chrome.money.target = this.shown.money
      this.audio.play(gain ? 'coin_land' : 'coin_lose', index * 2)
      this.show.flashPanel(gain ? UI.money : UI.bad, 0.5)
    }
    this.board.sortableChildren = true

    // **더미를 세우기 전에 뒷면을 정합니다.** `buildPanel` 이 덱 더미를 그리므로, 순서가
    // 거꾸로면 첫 화면의 더미만 첫 덱의 뒷면입니다.
    // **앞면을 먼저 정합니다.** 뒷면의 무늬를 세트가 정하므로 순서가 거꾸로면 첫 화면의
    // 더미만 덱의 무늬로 나옵니다.
    setCardSet(setLookOf(data, this.session.settings.cardSet))
    const back = data.tables.deck.findByDeckId(this.state.deckId)
    if (back) setCardBack({ ...backLookOf(back), motif: cardBackMotif() ?? back.back })

    this.show.buildPanel()

    this.chrome.playButton = new Button(t('ui.button.play'), PLAY_W, PLAY_H, 'primary',
      () => this.cards.play())
    this.chrome.discardButton = new Button(t('ui.button.discard'), PLAY_W, PLAY_H, 'danger',
      () => this.cards.discard())
    // **가운데 버튼이 곧 몇 장 골랐는가입니다.** 점 다섯을 따로 두면 같은 것을 두 곳에서
    // 세게 되고, 그 둘 사이를 눈이 오갑니다.
    this.chrome.clearButton = new Button('-', CLEAR_W, PLAY_H, 'neutral',
      () => this.cards.clearSelection())
    this.chrome.primaryButton = new Button(t('ui.button.select_blind'), 210, 50, 'primary',
      () => this.chrome.primary())
    this.chrome.skipButton = new Button(t('ui.button.skip'), 150, 38, 'dare',
      () => {
        this.audio.play('blind_skip')
        this.act({ t: 'skip_blind' })
      })
    this.chrome.rerollButton = new Button(t('ui.button.reroll'), 128, 44, 'select',
      () => this.shop.reroll())
    this.chrome.sortRankButton = new Button(t('ui.button.sort_rank'), SORT_W, SORT_H, 'neutral',
      () => this.cards.sortHand('rank'))
    this.chrome.sortSuitButton = new Button(t('ui.button.sort_suit'), SORT_W, SORT_H, 'neutral',
      () => this.cards.sortHand('suit'))
    // 위의 칸들과 같은 격자입니다 — 너비도 자리도.
    // **「족보 목록」 이 아니라 「런 정보」 입니다.** 족보는 그 안의 한 갈래가 되었습니다.
    this.chrome.infoButton = new Button(t('ui.run_info.title'), PANEL_BTN_W, FOOT_BTN_H,
      'neutral',
      () => this.cards.toggleHandList())
    this.chrome.menuButton = new Button(t('ui.button.menu'), PANEL_BTN_W, FOOT_BTN_H, 'neutral',
      () => this.panels.openMenu())
    // **자리는 화면이 알립니다.** 도구가 좌표를 베껴 적으면 판을 고칠 때 한쪽만 고쳐집니다.
    const footCx = PANEL_BTN_W / 2
    const footCy = FOOT_BTN_H / 2
    this.spotNodes.set('runInfo', { node: this.chrome.infoButton, cx: footCx, cy: footCy })
    this.spotNodes.set('menu', { node: this.chrome.menuButton, cx: footCx, cy: footCy })
    this.spotNodes.set('sort:rank',
                       { node: this.chrome.sortRankButton, cx: SORT_W / 2, cy: SORT_H / 2 })
    this.spotNodes.set('sort:suit',
                       { node: this.chrome.sortSuitButton, cx: SORT_W / 2, cy: SORT_H / 2 })

    // **상점은 판 안에 뜹니다.** 조커와 소모품 줄이 그 위로 지나가야 — 무엇을 가지고
    // 있는지를 보면서 사고, 산 것이 줄에 꽂히는 것도 보입니다.
    this.shop.shopLayer.zIndex = -1
    this.board.addChild(this.shop.shopLayer)

    // **알림 판은 손패 위입니다.** 판 위에서 일어나는 일이므로 팩과 자리 고르기보다는
    // 아래이고, 손패와 낸 카드보다는 위입니다.
    this.show.ruleBanner.zIndex = 480
    this.overlay.addChild(this.show.ruleBanner)
    this.overlay.addChild(this.chrome.playButton, this.chrome.discardButton,
      this.chrome.primaryButton,
      this.chrome.clearButton, this.chrome.skipButton, this.chrome.rerollButton,
      this.pack.packLayer, this.tray.focusLayer, this.chrome.sortRankButton,
      this.chrome.sortSuitButton, this.chrome.infoButton,
      this.chrome.menuButton, this.blind.blindPick, this.session.gameOver, this.chrome.heldBar)
    // **뜯은 팩은 판 위의 모든 것을 덮습니다.** 붙인 순서로만 두면 그 뒤에 붙는 버튼들이
    // 덮개 위로 올라옵니다 — 왼쪽 아래 버튼 둘이 팩을 뜯은 화면에 그대로 떠 있었습니다.
    this.overlay.sortableChildren = true
    // **자리를 비우는 글은 팩의 카드보다 위입니다.** 그동안 팩의 카드는 한 단 옅어집니다.
    this.pack.packLayer.zIndex = 500
    this.tray.focusLayer.zIndex = 560
    // **고른 것의 단추는 그보다 위입니다.** 팩에서 집는 단추가 그 팩의 카드들 뒤로
    // 들어가 있었습니다 — 무엇을 고르는지를 그 카드들이 보여 주고, 집는 것은 그 위에서
    // 눌러야 합니다.
    this.chrome.heldBar.zIndex = 600

    this.session.gameOver.visible = false
    // 낸다 · 취소 · 버린다. **취소가 가운데인 것이 맞습니다** — 둘 중 어느 쪽으로도
    // 가기 전에 되돌리는 것이기 때문입니다.
    // **줄을 세어 가운데에 놓습니다.** 자리를 하나하나 적어 두면 버튼 크기를 고친 날에
    // 가운데가 어긋납니다.
    const row = splitX(
      box(BOARD_X - (PLAY_W * 2 + CLEAR_W + BUTTON_GAP * 2) / 2, BUTTON_Y,
        PLAY_W * 2 + CLEAR_W + BUTTON_GAP * 2, PLAY_H),
      [PLAY_W, CLEAR_W, PLAY_W], BUTTON_GAP)
    this.chrome.playButton.position.set(row[0].x, row[0].y)
    this.chrome.clearButton.position.set(row[1].x, row[1].y)
    this.chrome.discardButton.position.set(row[2].x, row[2].y)
    this.chrome.primaryButton.position.set(BOARD_X - 105, 520)
    this.chrome.skipButton.position.set(BOARD_X - 75, 586)
    this.chrome.rerollButton.position.set(BOARD_X - 64, 578)
    // 정렬 둘은 그 줄의 세로 가운데에 놓입니다.
    //
    // **간격은 단추의 너비에서 셉니다.** 100픽셀을 적어 두었고, 손가락으로 누를 수 있게
    // 단추를 112픽셀로 키운 날부터 둘이 12픽셀 겹쳐 있었습니다.
    const sortY = BUTTON_Y + (PLAY_H - SORT_H) / 2
    this.chrome.sortRankButton.position.set(LEFT + PANEL_W + 30, sortY)
    this.chrome.sortSuitButton.position.set(LEFT + PANEL_W + 30 + SORT_W + 10, sortY)
    // **판의 밑단에 붙입니다.** 위에 두면 그 아래가 통째로 빈 자리로 남습니다 — 왼쪽 판은
    // 화면 아래 22픽셀까지 내려오고, 버튼은 그 안쪽에 있으면 됩니다.
    this.chrome.infoButton.position.set(LEFT, PANEL_FOOT_Y)
    this.chrome.menuButton.position.set(RIGHT_COL, PANEL_FOOT_Y)

    // **창 전체의 예외도 받아 둡니다.** F12 를 열지 않아도 `__clover.errors` 로 읽힙니다.
    window.addEventListener('error',
      event => this.note_(`window: ${String(event.message).slice(0, 160)}`))
    window.addEventListener('unhandledrejection', event =>
      this.note_(`promise: ${String((event as PromiseRejectionEvent).reason).slice(0, 160)}`))

    app.canvas.addEventListener('pointerdown', event => {
      this.audio.unlock()
      // **누름이 캔버스에 닿았는가.** Pixi 가 받기 전의 자리입니다 — 여기 적히고 아래의
      // 무대에 적히지 않으면 Pixi 의 길이 막힌 것이고, 여기에도 적히지 않으면 캔버스 위에
      // 무언가가 덮여 있는 것입니다. 원격 데스크탑에서 판 안의 카드만 눌리지 않아 넣었습니다.
      this.input.lastPointer = {
        dom: { type: event.pointerType, x: Math.round(event.clientX),
          y: Math.round(event.clientY),
               at: Math.round(performance.now()) },
        stage: this.input.lastPointer?.stage,
      }
    })
    // **껍데기 안에서는 기다리지 않습니다.** 소리 길이 사람의 조작 뒤에만 열리는 것은
    // 브라우저의 규칙이고, 앱의 WebView 와 일렉트론은 둘 다 그 규칙을 끄고 동작합니다 —
    // 그래서 타이틀의 음악이 첫 화면부터 납니다. 걸어 두지 않으면 첫 조작이 대개 판을
    // 여는 단추라, 타이틀 곡이 그 순간에 시작해서 다음 화면에서 곧바로 잦아들었습니다.
    //
    // **`inApp` 만 보고 있었습니다.** 그것은 커패시터의 표시이므로 데스크탑에서는 거짓이고,
    // 데스크탑은 캔버스를 한 번 눌러야 소리가 났습니다 — 창을 눌러 포커스를 준 것은 대개
    // 창 테두리라 그 누름이 캔버스에 닿지 않습니다.
    if (nativeShell()) this.audio.unlock()
    // **누르는 순간 툴팁이 닫힙니다.** 툴팁은 마우스가 그것에서 벗어날 때 닫히는데, 누른
    // 것이 사라지면(사거나 팔거나 쓰거나) 벗어나는 일이 영영 없어서 그 자리에 남습니다.
    //
    // **꾸욱 누르기는 여기서 시작하지 않습니다** — 누른 것이 무엇인지는 그 물건이 알고,
    // 이 자리는 화면 전체라 무엇을 눌렀는지 모릅니다. 여기서는 앞의 것을 걷을 뿐입니다.
    //
    // **잡는 단계에서 받습니다.** 이벤트는 눌린 것에서 시작해 화면까지 올라오므로, 여기서
    // 그냥 받으면 그 물건이 방금 걸어 둔 꾸욱 누르기를 이 줄이 곧바로 걷어냅니다 —
    // 잡는 단계는 그 반대로 화면에서 물건으로 내려가므로 여기가 먼저입니다.
    app.stage.addEventListener('pointerdown', event => {
      const hit = event.target as { label?: string; constructor: { name: string } } | null
      this.input.lastPointer = {
        dom: this.input.lastPointer?.dom,
        stage: { type: event.pointerType, at: Math.round(performance.now()),
                 target: hit === app.stage ? 'stage'
                   : `${hit?.constructor.name}${hit?.label ? ':' + hit.label : ''}`,
                 x: Math.round(event.global.x), y: Math.round(event.global.y) },
      }
      this.input.touching = event.pointerType !== 'mouse'
      // **떠 있는 쪽지가 있으면 이 누름은 그것을 닫는 누름입니다.**
      //
      // 꾸욱 눌러 세운 쪽지는 손을 떼도 남습니다(읽을 시간입니다). 그 상태에서 누른 것이
      // 고르기까지 되면 한 누름이 두 일을 하고, 조커를 읽고 나서 손을 떼는 그 자리에
      // 「판매」가 놓여 있습니다 — 닫는 것과 고르는 것을 한 누름에 겹치지 않습니다.
      //
      // **마우스에는 이것이 없습니다.** 마우스의 쪽지는 커서를 따라 뜨고 벗어나면 닫히므로
      // 떠 있는 쪽지가 없고(`pressShown` 은 꾸욱 누르기만 세웁니다), 올린 채로 누르는 것이
      // 곧 고르는 것입니다.
      this.input.hold.begin()
      this.input.tooltip.hide()
      // **이 누름이 고른 것의 밖이었는가**를 적어 둡니다. 놓는 것은 손을 뗄 때입니다 —
      // 여기서 놓으면 화면을 다시 그리게 되고, 다시 그리는 것은 지금 눌린 딱지를 통째로
      // 없애는 것이라 그 누름이 어디에도 닿지 못한 채 사라집니다.
      this.tray.heldAtPress = this.tray.held
      this.tray.pressOutsideHeld = this.tray.held !== undefined
        && !this.input.pressedHeld(event.target)
    }, { capture: true })
    app.stage.eventMode = 'static'
    app.stage.hitArea = { contains: () => true }
    // **아무것도 없는 곳을 눌렀는가.**
    //
    // 검증 도구가 화면을 누르는데, 누른 자리에 아무것도 없으면 브라우저도 게임도 아무 말을
    // 하지 않습니다 — 그래서 배치를 고친 날에 그 도구는 빈자리를 눌러 놓고 **통과합니다.**
    // 오늘만 그런 자리가 다섯 곳이었고, 그중 하나는 「소리 0번」을 타이틀 화면에서 재고
    // 있었습니다. 눌린 것이 무대 자신이면 그 누름은 아무것도 맞히지 못한 것입니다.
    app.stage.on('pointerdown', event => {
      if (event.target !== app.stage) return
      this.input.blankTaps++
      // **아무것도 없는 곳을 누르면 고른 것을 놓습니다.** 설명 쪽지가 사라지는 것과 같은
      // 처리입니다 — 조커를 눌러 「판다」가 놓여 있는 채로 다른 일을 하러 가면 그 단추가
      // 화면에 남고, 그것이 지금 누를 것으로 보입니다.
      this.input.dismissOnBlank()
    })
    app.stage.on('globalpointermove', event => {
      const at = this.world.toLocal(event.global)
      // **자리가 실제로 달라졌을 때만 세웁니다.** 같은 자리로 오는 이동 사건이 있고,
      // 그것을 움직인 것으로 세면 가만히 있어도 설명이 뜨는 길이 그대로 남습니다.
      if (at.x !== this.input.pointerAt.x
          || at.y !== this.input.pointerAt.y) this.input.pointerMoved = true
      this.input.pointerAt = at
      if (event.pointerType === 'mouse') this.input.touching = false
      this.input.advanceDrag()
      this.input.hold.moved(this.input.pointerAt)
    })
    // **판 밖에서 떼어도 끝나야 합니다.** 카드 위에서만 받으면 손가락이 판 밖으로 나간
    // 채 떼었을 때 그 카드가 커서에 붙어 남습니다.
    app.stage.on('pointerup', () => {
      this.input.endDrag()
      this.input.hold.cancel()
    })
    app.stage.on('pointerupoutside', () => {
      this.input.endDrag()
      this.input.hold.cancel()
    })
    // **고른 것 밖을 누르면 놓습니다.** 아무것도 없는 곳뿐이 아니라 아무 곳이나입니다 —
    // 조커를 눌러 「판매」가 놓여 있는 채로 손패를 고르러 가면 그 단추가 화면에 남고, 그것이
    // 지금 누를 것으로 보입니다.
    //
    // **손을 뗄 때 봅니다.** 누른 그 물건의 차례가 이미 지나갔으므로, 그 누름이 다른 것을
    // 골랐거나(다른 조커) 그것을 놓았으면(같은 조커를 다시) 여기서 할 일이 없습니다 —
    // 고른 것이 누를 때와 그대로일 때만 놓습니다.
    app.stage.on('pointertap', () => this.input.dismissAfterTap())
    window.addEventListener('keydown', event => {
      this.audio.unlock()
      if (event.key === 'Escape') {
        this.session.back()
        return
      }
      // 판이 떠 있으면 아무 키도 연출을 넘기지 않습니다. **판을 보고 있는 사람에게
      // 아무 키나는 「닫기」이고, 닫는 것은 위에서 `Escape` 가 이미 했습니다.**
      if (this.panels.modals.busy) return
      if (this.player.busy) this.player.hurry(this.feel)
    })
    // **안드로이드의 뒤로 가기.** 키 사건을 만들지 않으므로 위의 줄이 보지 못합니다.
    onBackButton(() => this.session.back())
    // **뒤로 물러나면 멈춥니다.** 안드로이드는 WebView 를 대신 멈춰 주지 않으므로, 걸어
    // 두지 않으면 화면이 없는 동안에도 그리고 소리를 냅니다.
    onAppState(active => this.session.setAwake(active))
    // **창을 떠날 때는 기다리지 않고 적습니다.** 탭을 닫는 것과 앱이 끝나는 것이 여기로
    // 옵니다 — `beforeunload` 는 핸드폰에서 오지 않는 자리가 있어 `pagehide` 를 씁니다.
    window.addEventListener('pagehide', () => this.session.flushRun(true))

    // **GPU 의 자리를 기계가 회수할 수 있습니다.** 오래 물러나 있으면 안드로이드가 그렇게
    // 하고, 돌아오면 Pixi 가 컨텍스트를 다시 세웁니다 — 그런데 **구워 둔 것은 그림이
    // 없는 채로 돌아옵니다.** 원본이 GPU 에만 있던 것들이라 다시 올릴 곳이 없습니다.
    // 놓아 두면 다음에 그릴 때 다시 굽습니다.
    app.canvas.addEventListener('webglcontextlost', () => {
      if (!this.manualTick) app.ticker.stop()
    })
    app.canvas.addEventListener('webglcontextrestored', () => {
      forgetCardFaces()
      forgetCardBacks()
      this.panels.collection.forgetBakes()
      if (!this.manualTick && this.session.awake) app.ticker.start()
      this.pack.repaintPack()
      this.refresh()
    })

    // 그림이 새로 들어오면 다시 그립니다. 문양이 그림으로 바뀝니다.
    // **그 자리에서 다시 그리지 않고 표시만 남깁니다.** 그림 하나마다 부르므로 조커 풀을
    // 열면 40번이 오고, 그때마다 화면 전체를 다시 세우면 한 번에 40번입니다 — `tick` 이
    // 모아서 한 번에 처리합니다.
    //
    // **들어온 것은 잠깐 모으고, 놓인 것은 더 짧게 모읍니다.** 도감을 한 줄 굴리면 그림 열
    // 장이 몇 프레임에 걸쳐 하나씩 들어오고, 프레임마다 판 전체를 다시 세우면 굴리는 동안
    // `refresh` 가 줄마다 열 번입니다 — 들어온 것은 조용해진 뒤 한 번으로 모읍니다. 놓인
    // 것은 버려지기 전에 다시 그려야 하므로 그보다 짧게 모읍니다.
    onArtReady((_key, gone) => {
      if (gone) {
        this.artDueAt = Math.min(this.artDueAt, this.clock + ART_DROP_WAIT)
        return
      }
      this.artQuietAt = this.clock + ART_QUIET
      this.artDueAt = Math.min(this.artDueAt, this.clock + ART_WAIT)
    })

    // **도구가 읽을 때만 셉니다.** 매 프레임 40개 키를 만들어 두던 것을, 읽는 쪽이 그 순간에
    // 만드는 것으로 바꿨습니다 — 값은 같고, 아무도 읽지 않는 프레임에는 아무 일도 없습니다.
    Object.defineProperty(window, '__clover', { configurable: true,
      get: () => this.probe.peek() })

    // **카드가 다 닿은 뒤에 셉니다.** 카드는 실제 시계로 날아가고 연출은 배속과 히트스톱을
    // 타므로, 시간으로만 맞추면 배속을 올린 순간 어긋납니다.
    this.player.blocked = () => this.cards.slams.length > 0 || this.clock < this.cards.playLanded

    // **버튼과 판의 소리는 여기 한 자리입니다.** 부르는 쪽마다 걸면 새로 만드는 것에서
    // 반드시 하나가 빠지고, 그것만 소리 없이 눌립니다.
    Button.onPressed = () => this.audio.play('button')
    this.panels.modals.onOpened = () => this.audio.play('panel_open')
    this.panels.modals.onClosed = () => this.audio.play('panel_close')

    this.refresh()
    // **수동 틱.** 검증 도구가 `?tick=manual` 로 열면 시간은 `advance` 로만 흐릅니다 — 실제
    // 시간을 기다리는 대신 틱 수를 정해 돌리므로 기계의 부하와 무관하게 같은 결과입니다.
    // 그리는 것은 틱커가 그대로 하므로 스크린샷은 언제든 찍힙니다.
    if (!this.manualTick) app.ticker.add(ticker => this.tick(ticker.deltaMS))

  }

  layout(width: number, height: number): void {
    const scale = Math.min(width / SIZE.width, height / SIZE.height)
    this.world.scale.set(scale)
    // **설명 쪽지는 판만큼 줄어들지 않습니다.** 폰에서 판이 0.6배로 들어가면 12픽셀
    // 설명글이 화면에서 7픽셀이 되고, 그 크기는 읽는 크기가 아닙니다.
    this.input.tooltip.setBoardScale(scale)
    // 자리를 정수로 맞춥니다. 반 픽셀이 남으면 글씨가 흐려집니다.
    const left = Math.round((width - SIZE.width * scale) / 2)
    const top = Math.round((height - SIZE.height * scale) / 2)
    this.world.position.set(left, top)

    // **자르는 자리는 판이 놓인 그 사각형입니다.** 올림으로 셉니다 — 내림하면 판의
    // 오른쪽과 아래에 배경색 한 줄이 남습니다.
    const boxW = Math.ceil(SIZE.width * scale)
    const boxH = Math.ceil(SIZE.height * scale)
    this.cropBox.clear()
    this.cropBox.rect(left, top, boxW, boxH).fill(PAINT.sheen)
    this.cropRect = box(left, top, boxW, boxH)

    // **흐림은 화면 해상도의 절반으로 굽습니다.** 픽셀 밀도는 창을 다른 화면으로 옮기면
    // 달라지므로 여기서 함께 정합니다.
    this.show.blurDensity = blurResolution(this.app.renderer.resolution ?? 1)
    this.show.blur.resolution = this.show.blurDensity
    this.show.blurBack.resolution = this.show.blurDensity

    // **배경도 판의 사각형입니다.** 창 전체를 덮으면 잘라 낸 자리에 그것만 남습니다.
    this.show.sheet.position.set(left, top)
    this.show.sheet.width = boxW
    this.show.sheet.height = boxH
    // 앞 배경도 같은 사각형입니다. 하나만 보이지만 자리는 둘 다 맞춰 둡니다.
    this.show.frontSheet.position.set(left, top)
    this.show.frontSheet.width = boxW
    this.show.frontSheet.height = boxH
    this.show.front.setAspect(SIZE.width / SIZE.height)
    // **비율이 고정입니다.** 배경이 판의 사각형에만 그려지므로 창의 비율과 상관이 없고,
    // 그래서 무늬가 기계마다 달라지지 않습니다.
    this.background.setAspect(SIZE.width / SIZE.height)
    // 씬이 갈리는 층도 같은 사각형입니다. 판 밖은 잘라 낸 자리이므로 건드리지 않습니다.
    this.transition.layout(left, top, boxW, boxH)
    // **필터가 굽는 자리를 못박아 둡니다.** 정하지 않으면 Pixi 가 이 통에 든 것들의 경계를
    // 매 프레임 재고, 그 경계는 카드와 조각이 움직일 때마다 달라집니다.
    this.screen.filterArea = new Rectangle(left, top, boxW, boxH)
    // 환희의 겹도 같은 사각형입니다. 배경과 어긋나면 넘어가는 동안 한쪽이 삐져나옵니다.
    this.show.euphoria.layout(left, top, boxW, boxH)
    this.show.euphoria.setAspect(SIZE.width / SIZE.height)
    this.sharpen(scale)
    // 앞면과 뒷면은 글씨와 같은 배율로 굽습니다. **그림도 그 배율로 풉니다** — 셋이 한
    // 배율이어야 한 화면에서 어느 하나만 흐리거나 또렷하지 않습니다.
    setArtDensity(this.textScale)
    bakeCardBacks(this.app.renderer, this.textScale)
    bakeCardFaces(this.app.renderer, this.textScale)
    // 삭는 판을 굽는 것도 같은 렌더러입니다. **알갱이를 그릴 수 있는지가 여기서 갈립니다.**
    useMotesLayer(this.show.motes, this.app.renderer, this.textScale)
  }

  /**
   * 글씨를 화면 배율에 맞춰 다시 굽습니다.
   *
   * **월드를 통째로 확대하므로 글씨가 그대로면 뿌옇습니다.** 글씨는 한 번 그림으로 구워서
   * 쓰는 것이라, 구울 때의 배율이 화면 배율보다 작으면 늘려 놓은 그림이 됩니다.
   */
  private sharpen(scale: number): void {
    // **화면에 실제로 놓이는 픽셀만큼입니다.** 배율과 밀도의 곱이 그것이고, 배율이
    // 1보다 작은 자리 — 핸드폰이 그렇습니다 — 에서 배율을 1로 올려 버리면 필요한 것의
    // 갑절이 넘게 굽습니다. 굽는 값은 픽셀 수만큼입니다.
    const want = Math.min(3, Math.max(1, scale * (this.app.renderer.resolution ?? 1)))
    const walk = (node: Container) => {
      if (node instanceof Text && node.resolution !== want) node.resolution = want
      for (const child of node.children) walk(child as Container)
    }
    walk(this.world)
    this.textScale = want
  }

  textScale = 1

  // ---------------------------------------------------------------- 액션

  act(action: Action): void {
    if (this.player.busy) return
    // **건너뛰기 연출 중에는 판이 낡은 것입니다.** 화면의 판은 건너뛴 그 블라인드인데 코어는
    // 다음 블라인드이므로, 그 판의 단추를 누르면 보이는 것과 다른 것에 답하게 됩니다.
    if (this.blind.skipping && action.t !== 'skip_blind') return
    // 무엇이 일어나면 가리키던 것이 그대로 있으리라는 보장이 없습니다.
    this.input.tooltip.hide()
    const before = this.shown.hand
    // **소모품이 오는 길을 누가 드는지는 액션마다 새로 셉니다.**
    this.tray.itemFlyOwned = false
    // **바뀌기 전의 카드를 붙들어 둡니다.** 상태는 이 줄 다음에 이미 바뀌어 있고, 바뀌는
    // 것을 보이는 박자는 그 뒤에 옵니다 — 붙들지 않으면 보일 것이 이미 없어진 뒤입니다.
    const wasDeck = new Map(this.state.deck.map(card => [card.uid, { ...card }]))
    // **조커도 같습니다.** 판이 갈리는 딱지는 그 박자가 올 때까지 이전 모습으로 섭니다.
    const wasJokers = new Map(this.state.jokers.map(one => [one.uid, { ...one }]))
    this.cards.pendingJokers.clear()
    const step = apply(this.data, this.state, action)
    for (const event of step.events) {
      if (event.t === 'JokerModified') {
        const one = wasJokers.get(event.uid)
        if (one) this.cards.pendingJokers.set(event.uid, one)
      }
      // **보스가 거는 것도 카드의 모습을 바꿉니다.** 죽는 것과 엎어지는 것 둘 다 얼굴이
      // 갈리므로, 붙들지 않으면 그 순간에 이미 죽어 있고 이미 엎어져 있습니다.
      const uids = event.t === 'CardModified' || event.t === 'CardDestroyed' ? [event.uid]
        : event.t === 'CardsDebuffed' || event.t === 'CardsHidden' ? event.uids
          : undefined
      if (!uids) continue
      for (const uid of uids) {
        const was = wasDeck.get(uid)
        if (was) this.cards.pendingCards.set(uid, was)
      }
    }
    this.input.hintCache = undefined
    // **코어를 지난 액션만 적습니다.** 화면이 막은 것은 런에 들어가지 않았습니다.
    this.session.actions.push(action)
    // **액션마다 적어 둡니다.** 판을 접는 자리에서만 적으면 창을 그냥 닫은 사람은
    // 이어서 할 것이 없습니다.
    this.session.rememberRun()
    observe(this.session.metrics, step.events)
    this.rewind(step.events, before)
    // **판을 떠나는 것만 미룹니다.** 블라인드를 고르고 상점을 나서는 것은 누른 그 자리에서
    // 바뀌어야 하고, 판이 끝나는 것은 연출이 끝난 뒤에 보여야 합니다 — 그것은 연출이
    // 끝나는 자리(`settleShown`)가 맞춥니다.
    const leaving = this.shown.phase === 'round' && this.state.phase !== 'round'
    if (!leaving) this.shown.phase = this.state.phase
    // **아무도 들지 않은 소모품은 박자가 듭니다.** 조커가 만들고 태그가 주는 것이 그것이고,
    // 그동안 칸에 툭 나타났습니다 — 박자가 올 때까지 세우지 않습니다.
    if (this.tray.arriveHold === undefined) {
      // **넉넉한 천장입니다.** 박자가 이것을 걷으므로 이 값에 닿는 것은 박자가 오지 않은
      // 때뿐이고, 그때는 붙든 채로 두는 것보다 그냥 세우는 것이 낫습니다.
      // **조커도 같습니다.** 효과·태그가 준 조커는 소모품과 같은 「아무도 들지 않은 획득」인데
      // 붙들지 않아 그 프레임에 줄 위에서 떨어졌습니다.
      if (step.events.some(event => event.t === 'JokerAdded')) {
        this.tray.arriveHold = { kind: 'joker', until: this.clock + 4, byBeat: true }
      } else if (!this.tray.itemFlyOwned
                 && step.events.some(event => event.t === 'ConsumableAdded')) {
        this.tray.arriveHold = { kind: 'item', until: this.clock + 4, byBeat: true }
      }
    }
    this.announce(step.events)
    this.show.startTimeline(step.events)
    this.note()
    this.refresh()
    // **연출이 도는 중이면 표시를 켭니다.** 다 끝난 첫 프레임에 `tick` 이 화면을 상태에
    // 맞추고 다시 그립니다 — 그때가 다음 국면의 판을 세울 때입니다.
    if (!this.presented) this.payout.settleOwed = true
  }

  /**
   * 지금 판에서 보이는 것을 도감에 적습니다.
   *
   * **액션마다 부릅니다.** 상태가 바뀌는 길이 `apply` 하나이므로, 그 뒤에 한 번 부르면
   * 놓치는 자리가 없습니다 — 오는 길마다 적으면 조커 하나가 상점 · 팩 · 태그 · 카드
   * 만들기 넷에서 저마다 적히고, 그중 하나를 빼먹은 것은 아무도 보지 못합니다.
   *
   * **늘었을 때만 저장합니다.** 액션마다 쓰면 한 판에 수백 번입니다.
   */
  note(): void {
    if (!discover(this.session.collected, sightings(this.state))) return
    saveCollection(this.session.collected)
    this.panels.collection.setProgress(this.session.collected)
  }

  /**
   * 이 액션이 낸 이벤트들을 되짚어, **연출이 아직 도달하지 않은 것을 화면에서 뺍니다.**
   *
   * 점수와 금액은 늘어난 만큼 되돌리고, 패는 뽑기 전의 모습으로 되돌립니다. 그다음은 박자가
   * 하나씩 도로 채웁니다.
   */
  private rewind(events: readonly GameEvent[], before: readonly number[]): void {
    // **아직 닿지 않은 동전의 몫도 뺍니다.** 앞 액션의 동전이 날고 있는 채로 다음 액션이
    // 들어오면 코어의 잔액에는 그 동전들의 돈이 이미 있습니다 — 빼지 않으면 닿을 때 한 번
    // 더 더해집니다.
    let money = this.state.money - this.chrome.moneyInFlight()
    let score = Number(this.state.score)
    const drawn = new Set<number>()

    for (const event of events) {
      switch (event.t) {
        case 'MoneyChanged': money -= event.delta; break
        case 'ScoreResolved': score -= event.score; break
        case 'HandDrawn': for (const uid of event.uids) drawn.add(uid); break
        default: break
      }
    }

    this.shown = {
      money,
      score,
      // **누르기 전의 패 그대로입니다.** 뽑은 것만 뺍니다 — 낸 것은 박자가 도달할 때
      // 물러나고, 남은 것은 계속 손에 있어야 합니다.
      //
      // 코어의 패를 보고 정하면 **마지막 핸드에서 남은 카드가 즉시 사라집니다.** 그 한
      // 판으로 격파하면 코어가 라운드를 끝내며 패를 비우는데, 화면은 아직 카드가 날아가는
      // 중이고 득점도 시작하지 않았습니다.
      hand: before.filter(uid => !drawn.has(uid)),
      phase: this.shown.phase,
    }
  }

  /**
   * 무엇이 일어났는지 글로 알립니다.
   *
   * **소모품은 결과가 화면 여러 곳에 흩어집니다** — 카드가 바뀌고 족보 레벨이 오르고 조커가
   * 사라지는데, 그것들이 각자의 자리에서 조용히 바뀌면 무엇을 쓴 것인지 남지 않습니다.
   *
   * 같은 갈래는 묶어서 한 줄로 냅니다. 카드 5장이 바뀌었다고 토스트가 5개 뜨면 읽을 수
   * 없습니다.
   */
  private announce(events: readonly GameEvent[]): void {
    let modified = 0
    let destroyed = 0
    let added = 0

    for (const event of events) {
      switch (event.t) {
        case 'ConsumableUsed': {
          const kind = this.state.consumables.find(item => item.id === event.id)?.kind
          const name = this.tray.consumableName(kind ?? 1, event.id)
          this.input.toasts.push(tf('ui.toast.used', { name }),
            this.tray.consumableLines(kind ?? 1, event.id).join(' · ') || t('ui.note.applied'),
            UI.legendary, 3)
          break
        }

        // **족보 레벨과 규칙은 토스트가 아닙니다.** 손패 줄 위의 판이 알립니다 —
        // `showRuleChange` 가 그 자리입니다.

        // **태그를 받은 것이 보여야 합니다.** 받은 것이 화면 어디에도 나타나지 않으면
        // 건너뛴 대가가 없는 것으로 보입니다.
        // **받은 태그는 토스트가 아니라 박자입니다.** 카드에 적혀 있던 칩이 머리띠로 날아가
        // 앉는 것이 받았다는 표시이고, 토스트는 눈이 있는 판 가운데에서 먼 구석에 떴습니다.
        // `showBeat` 의 `TagGained` 가 합니다.

        // **쓰인 태그도 보여야 합니다.** 태그는 둘로 갈립니다 — 상점에 들어갈 때 도는
        // 것은 들고 있다가 그때 돌지만, 그 자리에서 도는 것은 받자마자 쓰이고 사라집니다.
        // 그 사라짐을 알리지 않으니 둘을 건너뛰고 하나만 남은 것으로 보였습니다.
        case 'TagUsed': {
          // **쓴 것도 남깁니다.** 지우면 그 자리에서 쓰이는 태그는 아무것도 뜨지 않은 채로
          // 지나가고, 무엇을 받았는지가 화면에 남지 않습니다. 켜지는 것은 박자가 합니다 —
          // 칩이 머리띠에 앉은 뒤여야 켜질 자리가 있습니다.
          if (!this.blind.tagSpent.includes(event.tagId)) this.blind.tagSpent.push(event.tagId)
          break
        }

        // 부서진 조커는 박자가 알립니다 — 타는 그 자리에서. 토스트는 눈이 있는 판에서 먼
        // 구석입니다.
        case 'JokerDestroyed': break

        case 'CardModified': modified++; break
        case 'CardDestroyed': destroyed++; break
        case 'CardAdded': added++; break
        default: break
      }
    }

    if (modified > 0) {
      this.input.toasts.push(tf('ui.toast.cards_changed', { n: modified }), t('ui.deck.changed'),
        UI.good, 2.4)
    }
    if (destroyed > 0) {
      this.input.toasts.push(tf('ui.toast.cards_destroyed', { n: destroyed }),
        t('ui.deck.removed'), UI.bad, 2.4)
    }
    if (added > 0) {
      this.input.toasts.push(tf('ui.toast.cards_added', { n: added }), t('ui.deck.added'),
        UI.good, 2.4)
    }
  }

  /** 때가 된 것을 합니다. */
  private advanceLater(): void {
    for (let i = this.later.length - 1; i >= 0; i--) {
      if (this.later[i].at > this.clock) continue
      const one = this.later[i]
      this.later.splice(i, 1)
      one.run()
    }
  }

  get presented(): boolean {
    return this.cardsQuiet && !this.payout.coins.busy && this.blind.tagFly === undefined
  }

  /**
   * 카드가 다 물러났는가.
   *
   * **동전은 세지 않습니다.** 동전이 나는 동안에도 떠 있어야 하는 것들이 있고 — 상점이
   * 그렇습니다 — 카드가 아직 걷히는 중인 것과는 다른 일입니다.
   */
  /**
   * 상점이 서도 되는가.
   *
   * **카드가 다 걷혔는가만 봅니다.** 연출이 도는 중인지는 보지 않습니다 — 사는 것도 연출
   * 하나이므로 그것까지 세면 하나 살 때마다 큰 판이 사라졌다 다시 뜹니다. 카드가 걷히는
   * 동안 뜨지 않는 것은 그것과 다른 일입니다: 낸 카드가 아직 물러나는 중인데 판이 그 위에
   * 뜨면 그 둘이 겹칩니다.
   */
  get shopReady(): boolean {
    return this.session.scene === 'run' && this.cards.playedViews.length === 0
      && this.cards.deals.length === 0 && this.cards.fades.length === 0
  }

  private get cardsQuiet(): boolean {
    return this.session.scene === 'run' && !this.player.busy
      && this.cards.playedViews.length === 0
      && this.cards.deals.length === 0 && this.cards.fades.length === 0
        && this.cards.recalls.length === 0
  }

  /** 지금까지 그린 프레임 수. **검증 도구가 물러난 동안 멈추는지를 이것으로 봅니다.** */
  drawn = 0
  /** 지금까지 `refresh()` 를 부른 수. **검증 도구가 이것으로 다시 세우는 폭풍을 봅니다.** */
  refreshes = 0

  /** 적어 둘 것이 밀려 있는가. */
  saveDue = false

  /** 마지막으로 적은 때. */
  saveAt = -Infinity

  // ---------------------------------------------------------------- 매 프레임

  private tick(deltaMs: number): void {
    const seconds = deltaMs / 1000
    this.clock += seconds
    this.drawn++

    // **놓은 그림을 버리는 자리입니다.** 놓는 것은 `art.ts` 가 넘칠 때 스스로 하고,
    // 여기서는 그것이 몇 틱 지났는지만 세어 줍니다 — 놓자마자 버리면 그 그림을 쓰고 있던
    // 스프라이트가 다시 그려지기 전의 한 프레임에 없는 텍스처를 가리킵니다.
    artTick()
    this.session.flushRun()

    if (this.artDirty || this.clock >= this.artQuietAt || this.clock >= this.artDueAt) {
      this.artDirty = false
      this.artQuietAt = Infinity
      this.artDueAt = Infinity
      this.pack.repaintPack()
      this.session.repaintGameOver()
      this.refresh()
    }

    // **히트스톱.** 연출의 시계만 멈춥니다 — 용수철과 파티클은 계속 움직여야 화면이
    // 얼어붙은 것으로 보이지 않습니다.
    if (this.show.freeze > 0) this.show.freeze = Math.max(0, this.show.freeze - deltaMs)
    else this.player.advance(deltaMs)

    this.tray.advanceConsumableLift(seconds)
    this.tray.advanceItemArrive(seconds)
    this.input.hold.advance(seconds, () => this.audio.play('button'))
    this.shop.advanceReveals()
    this.pack.advancePack(seconds)
    this.tray.advanceFocus(seconds)
    this.tray.advanceDeckFlight(seconds)
    this.advanceLater()
    this.payout.advancePayout(seconds)
    this.show.advanceRatchet(seconds)
    this.tray.advanceBurningItems(seconds)
    this.payout.coins.advance(seconds)
    this.input.toasts.advance(seconds)
    this.show.decayFlashes(seconds)

    // **하나가 던져도 프레임의 나머지는 돕니다.** 이 셋은 겉모습이고, 그 뒤에 오는 것이
    // 카드가 판에 닿는 것 · 깔리는 것 · 소리입니다 — 겉모습 하나가 그것들을 통째로 막으면
    // 판이 소리 없이 멈추고, 왜인지는 아무 데도 적히지 않습니다. 던진 것은 `__clover.errors`
    // 에 적힙니다.
    this.guard('background', () => this.background.advance(seconds))
    if (this.show.frontSheet.visible) this.guard('front', () => this.show.front.advance(seconds))
    this.guard('euphoria', () => this.show.euphoria.advance(seconds))
    this.guard('punch', () => this.show.punch.advance(seconds))
    // **파형의 흐름은 실제 초로 잇습니다.** `step` 은 초당 60번 고정이므로 프레임이 그보다
    // 적게 나오는 화면에서는 그 안에서 여러 번 돌고, 위상을 거기서 올리면 화면에 보이는
    // 흐름이 프레임 수와 어긋납니다.
    this.guard('wave', () => this.chrome.scoreWave.advance(seconds))

    // **필터는 필요할 때만 겁니다.** 늘 걸어 두면 판이 매 프레임 그림으로 한 번 구워지고,
    // 그 그림이 화면 배율에 늘어나 글씨가 뿌옇게 됩니다.
    const punching = !this.show.punch.quiet
    const filtered = (this.board.filters as unknown[] | null)?.length ?? 0
    if (punching && filtered === 0) this.board.filters = [this.show.punch]
    else if (!punching && filtered > 0) this.board.filters = []
    // **배경의 빠르기는 천천히 따라갑니다.** 점수가 한 박자에 크게 뛰므로 그대로 먹이면
    // 블라인드가 그대로인데도 배경이 휘리릭 돕니다.
    this.show.heatShown += (this.show.heat() - this.show.heatShown) * fraction(seconds, 0.9)
    this.background.setHeat(this.show.heatShown)
    this.show.particles.advance(seconds)
    this.show.motes.advance(seconds)

    // **고정 단계.** 틱커가 한 프레임을 100밀리초로 자르므로 한 프레임에 많아야 6단계입니다.
    this.stepDebt += deltaMs
    while (this.stepDebt >= STEP_MS) {
      this.stepDebt -= STEP_MS
      this.step(STEP_MS)
    }
    this.input.updateHover()
    this.panels.updateHandHover()

    for (const view of this.cards.views.values()) {
      view.pointer = this.input.tiltFor(view)
      view.advance(seconds, this.clock)
    }
    for (const view of this.cards.playedViews) view.advance(seconds, this.clock)
    // **빌린 카드는 손패 줄의 표에도 있습니다.** 위에서 이미 한 번 옮겼으므로 여기서는
    // 빌려 오지 않은 것만 옮깁니다.
    this.cards.advanceCardShow(seconds)
    this.show.ruleBanner.advance(seconds)
    // 이어져 있는 줄은 숨 쉬듯 짙어졌다 옅어집니다. **다시 긋지 않습니다** — 그리는
    // 것은 자리가 바뀔 때뿐이고, 여기서는 짙기 하나만 옮깁니다.
    if (this.cards.borrowLink.visible) {
      this.cards.borrowLink.alpha = 0.5 + 0.25 * Math.sin(this.clock * 2.2)
    }
    const before = this.cards.playedViews.length
    this.cards.reapPlayArea()
    if (before > 0 && this.cards.playedViews.length === 0) {
      this.holdAfterScore = 0
      this.refresh()
    }
    for (const view of this.cards.jokers.values()) {
      view.pointer = this.input.tiltFor(view)
      // **자리를 비우는 동안.** 내놓을 수 있는 것은 조금 떠서 살짝 오르내리고, 다른
      // 갈래의 것과 `Eternal` 은 물러납니다 — 어느 것을 고르라는 것인지가 글보다 먼저
      // 보여야 합니다. 끝나면 `refresh` 가 제자리와 짙기를 되돌립니다.
      if (this.tray.focus) {
        const ok = this.tray.focus.kind === 'joker' && this.tray.focusEligible('joker', view.uid)
        view.alpha = ok ? 1 : 0.3
        if (ok && !(this.input.drag?.kind === 'joker' && this.input.drag.uid === view.uid)) {
          // **고른 것에는 오르내림을 얹지 않습니다.** 고른 것은 이미 `HELD_RISE` 만큼
          // 올라가 그 밑에 단추를 세운 것이고, 거기에 9픽셀이 더 얹히면 윗변이 화면 밖으로
          // 나갑니다 — 오르내리는 것은 「이 줄에서 고르십시오」의 몸짓이므로 아직 고르지
          // 않은 것들의 것입니다.
          const bob = this.tray.held?.kind === 'joker' && this.tray.held.uid === view.uid
            ? HELD_RISE : 6 + Math.sin(this.clock * 3 + view.motion.phase) * 3
          view.motion.y.target = JOKER_Y - bob
        }
      }
      view.advance(seconds, this.clock)
    }
    for (let i = this.cards.burning.length - 1; i >= 0; i--) {
      const view = this.cards.burning[i]
      view.advance(seconds, this.clock)
      if (!view.gone) continue
      view.destroy()
      this.cards.burning.splice(i, 1)
    }

    // 줄 밖에 선 것들. **겉면만 돌립니다.**
    this.input.advanceLooks()

    // **판이 끝났고 카드가 다 나갔으면 덱으로 돌아옵니다.** 한 판을 도는 동안 나간 카드
    // 전부가 한 번에 돌아옵니다 — 격파한 그 박자에 그때까지 나간 것만 돌려보내면, 낸 카드와
    // 손패는 다음 판의 격파에 가서야 돌아옵니다.
    //
    // **늦추지 않습니다.** 걷는 소리와 겹치는 것을 갈라 놓으려고 이 자리를 0.5초 늦춘
    // 적이 있는데, 그동안 정산 판과 끝난 판이 먼저 서므로 **카드와 덱이 그 판 위로 한 번
    // 들어왔다가 사라졌습니다.** 소리를 위해 화면을 늦추는 것은 값이 맞지 않습니다 —
    // 갈라 놓는 것은 소리 쪽에서 합니다(`advanceRecalls`).
    if (this.state.phase !== 'round' && this.cards.retired > 0 && !this.player.busy
        && this.cards.playedViews.length === 0 && this.cards.fades.length === 0) {
      this.cards.recallToDeck()
    }

    // 건너뛰기 연출이 끝났습니다. 이제 판이 다음 블라인드로 넘어가고 팩이 열립니다.
    // **`skipping` 자체가 걸쇠입니다** — 단추가 걸고, 연출이 다 끝난 첫 프레임에 풉니다.
    if (this.blind.skipping && this.presented) {
      this.blind.skipping = false
      this.blind.skipFrom = undefined
      this.blind.tagLanded = undefined
      this.payout.settleOwed = true
    }
    // 연출이 끝났으면 화면을 상태에 맞춥니다. **그때가 다음 국면의 화면을 띄울 때입니다.**
    if (this.payout.settleOwed && this.presented) {
      this.payout.settleOwed = false
      this.payout.settleShown()
      this.refresh()
    }
    // **블라인드 판은 떠야 하는가와 떠 있는가를 프레임마다 맞춥니다.** 정산 · 게임 오버와
    // 같은 규칙입니다 — `refresh` 가 어느 순간에 불렸는가에 판이 뜨는 것을 맡기지
    // 않습니다. 건너뛰는 동안은 떠 있는 판을 그대로 둡니다.
    if (!this.blind.skipping
        && this.blind.blindWanted !== this.blind.blindPick.visible) this.blind.drawBlindPick()

    this.show.advanceHeadline(seconds)
    this.show.advanceChimes()
    this.cards.advanceSlams()
    this.cards.advanceFades()
    this.cards.advanceDeals(seconds)

    // 덱은 판이 도는 동안만 자리에 있습니다.
    // **판이 도는 동안만 자리에 있습니다.** 블라인드를 고르는 중에도 아직 없습니다 —
    // 시작을 누르면 오른쪽에서 들어옵니다.
    // **화면의 국면입니다.** 코어의 국면을 보면 마지막 핸드를 내는 순간 덱이 빠지기
    // 시작하고, 걷힌 카드가 돌아갈 자리가 없습니다.
    //
    // **돌아온 카드가 쌓인 것을 보고 나서 물러납니다.** 마지막 한 장이 닿는 그 프레임에
    // 빠지기 시작하면 그 장이 덱에 들어간 것이 보이지 않습니다.
    //
    // **플레잉 카드가 든 팩이 펼쳐진 동안과 그 카드를 받는 동안도 나와 있습니다.** 집은
    // 카드가 덱으로 들어가는 것이 보여야 하기 때문입니다 — 물러나 있으면 화면 오른쪽 밖으로
    // 사라지는 것으로 보입니다. 타로·행성·조커 팩은 덱에 넣는 것이 없으므로 덱이 나올 이유가
    // 없고, 나와 있으면 그 팩과 상관없는 것이 하나 더 나와 있는 것입니다.
    const away = this.shown.phase !== 'round' && this.clock >= this.cards.deckHold
      && this.clock >= this.cards.deckPeekUntil
      && !(this.pack.packHoldsCards && this.clock >= this.shop.shopStayUntil)
    this.chrome.deckBump.advance(seconds)
    this.chrome.deckPile.y = this.chrome.deckBump.value
    // **거둘 때에는 덱이 마주 나옵니다.** 카드가 화면 오른쪽 끝까지 날아가 사라지는
    // 것으로 보였습니다 — 덱이 판 쪽으로 한 걸음 나와 받고, 다 받은 뒤에 그대로 오른쪽으로
    // 물러납니다. 나온 자리에서 물러나므로 제자리로 돌아가는 걸음이 없습니다.
    const meeting = this.cards.recalls.length > 0 || this.clock < this.cards.deckHold
    this.chrome.deckSlide.target = away ? 300 : meeting ? -DECK_MEET : 0
    this.chrome.deckSlide.advance(seconds)
    this.chrome.deckLayer.x = this.chrome.deckSlide.value
    this.chrome.deckLayer.visible = this.chrome.deckSlide.value < 296
    this.cards.advanceRecalls(seconds)
    this.show.advanceDeltas(seconds)
    this.shop.advanceShopLift(seconds)
    this.tray.advanceLeavingTiles(seconds)
    this.shop.advanceShopPanel(seconds)
    this.chrome.advanceCountPulse(seconds)
    this.chrome.advanceActiveGlow()
    this.chrome.advanceHandControls(seconds)
    this.blind.badge.advance(seconds)
    this.blind.advanceTagFlash(seconds)
    this.blind.advanceTagFly(seconds)
    this.session.advanceGameOver(seconds)
    this.session.title.advance(seconds)
    this.input.tooltip.advance(seconds)
    // 인사이트 갈래의 굴림통. 관성과 되돌아옴이 이 프레임을 받습니다.
    this.panels.insightScroll?.tick(seconds)
    this.panels.modals.advance(seconds)
    // 도감의 쪽지도 이 프레임을 받습니다.
    this.panels.collection.advance(seconds)
    // **씬을 갈아 끼우는 것이 이 안에서 일어납니다.** 화면의 시계로 도므로 수동 틱으로
    // 세운 도구가 그대로 지납니다.
    this.transition.tick(seconds)

    // 블라인드 판이 들어오는 동안 매 프레임 자리를 옮깁니다. **다시 만들지 않습니다.**
    // **상점이 물러나고 덮개가 걷힌 뒤에 올라옵니다.** 국면은 상점이 아직 미끄러지는 중에
    // 넘어가므로, 그 자리에서 올리면 판 셋이 상점 뒤에서 올라오고 상점이 걷힌 자리에는
    // 이미 다 떠 있습니다 — 올라오는 것을 아무도 보지 못합니다.
    const roomForBlind = !this.shop.shopLayer.visible && this.panels.modals.cover < 0.2
    if (this.state.phase === 'blind-select' && this.blind.blindEnter < 0.999 && roomForBlind) {
      this.blind.blindEnter += (1 - this.blind.blindEnter) * fraction(seconds, 9)
      for (const entry of this.blind.blindGroups) this.blind.placeBlindGroup(entry)
    } else if (this.state.phase !== 'blind-select') {
      this.blind.blindEnter = 0
      this.blind.blindShown = -1
    }
    this.show.advanceBlur(seconds)

    // **끝났다는 판은 연출이 다 끝난 뒤에 띄웁니다.** 마지막 카드의 결과를 보기 전에 덮이면
    // 무엇 때문에 끝난 것인지 알 수 없습니다.
    //
    // **카드는 걷지 않습니다.** 끝난 판의 손패와 낸 카드는 그 자리에 남고, 판이 그 위에
    // 뜹니다 — 진 판은 재가 되어 바람에 실려 가는데, 카드를 먼저 걷으면 사진에 카드가 없어
    // 빈 판때기만 부서집니다. 남는 것은 그 판의 마지막 모습이어야 합니다. 그래서 여기서
    // 기다리는 것은 **움직이는 것이 없는가**와 **결과를 읽을 시간이 지났는가**입니다.
    const finished = this.state.phase === 'lost' || this.state.phase === 'won'
    // **거둔 것은 세지 않습니다.** `retired` 는 딜러에게 간 카드가 덱으로 돌아오기를
    // 기다리는 수이고, 그 돌아오기(`recallToDeck`)는 **낸 카드가 다 걷힌 뒤에만 돕니다** —
    // 끝난 판은 낸 카드를 그대로 두므로 그 수가 영원히 0이 되지 않습니다. 조건에 넣어
    // 두었더니 패배 판이 뜨지 않고 그 자리에서 멈췄습니다.
    const still = this.cards.fades.length === 0 && this.cards.deals.length === 0
      && this.cards.recalls.length === 0
    const read = this.cards.playedViews.length === 0 || this.holdAfterScore > 1_100
    if (finished && still && read && !this.session.gameOverShown
        && !this.player.busy && this.chrome.score.settled && !this.payout.coins.busy) {
      this.session.drawGameOver()
    }

    if (!this.player.busy) {
      // **박자가 끝난 뒤에 크기를 되돌리지 않습니다.** 얹힌 크기는 칸에서 시간으로
      // 잦아드므로, 여기서 1 을 다시 앉히면 마지막 박자의 크기가 그대로 남아 있다가
      // 박자가 끝나는 프레임에 한 번에 줄어듭니다.

      // **결과를 읽을 시간을 둡니다.** 점수가 다 굴러간 뒤에도 잠깐 남아 있어야
      // 무엇을 냈고 얼마가 되었는지가 보입니다.
      if (this.cards.playedViews.length > 0 && this.chrome.score.settled) {
        this.holdAfterScore += deltaMs
        // **끝난 판은 걷지 않습니다.** 위의 「카드는 걷지 않습니다」.
        const over = this.state.phase === 'lost' || this.state.phase === 'won'
        if (this.holdAfterScore > 1_100 && !over
            && !this.cards.leaving(this.cards.playedViews[0])) {
          this.cards.clearPlayArea()
          // **판이 끝났으면 손패도 뒤따라 걷힙니다.** 낸 카드와 손패가 따로 나가면 걷는
          // 것이 두 번이고, 그 사이에 손에 카드가 남은 채로 결과만 보입니다.
          // 낸 카드의 절반쯤이 나갔을 때부터 뒤따릅니다 — 따로 나가면 걷는 데 1초가 넘고,
          // 그 시간은 볼 것이 아니라 셈이 맞는다는 표시일 뿐입니다.
          if (this.state.phase !== 'round') {
            this.cards.sweepHand(ITEM_SETTLE + ITEM_LINGER
              + this.cards.playedViews.length * (this.feel.playStaggerMs / 2000))
          }
        }
      } else {
        this.holdAfterScore = 0
      }
      if (this.shown.phase !== 'round') {
        this.chrome.chips.target = 0
        this.chrome.mult.target = 0
      }
    }
  }

  /**
   * 고정 단계. 초당 60번, 프레임과 무관합니다.
   *
   * **여기 있는 것은 전부 단계마다 난수를 뽑거나 단계당 비율로 줄어드는 것들입니다.**
   * 그래서 이것들은 단계의 길이가 늘 같아야 같은 모습입니다. 시간으로만 움직이는 것은
   * `tick` 에 있습니다.
   */
  private step(stepMs: number): void {
    // **왼쪽 판의 칸 전부입니다.** 굴러가는 넷만 여기 있었고, 핸드·버리기·안티는 값이
    // 글로 들어와 굴러갈 것이 없다는 이유로 빠져 있었습니다 — 그런데 ±N 이 뜨는 동안
    // 숫자가 물러나는 것도, 값이 바뀔 때 한 번 튀는 것도 이 단계에서 돕니다. 빠져 있는
    // 동안 그 셋은 `mute()` 를 받고도 그대로 남아 있었고, 그래서 같은 자리에 수가 둘
    // 겹쳤습니다.
    for (const slot of this.chrome.panelSlots) slot.advance(stepMs)
    this.chrome.paintScoreFlash()
    // **파형의 세기입니다.** 얹히는 것은 칸이 세고(더해질 때만), 바닥은 지금의 배당입니다 —
    // 바닥을 두 상자가 나누므로 배당이 크면 둘이 함께 요동치고, 얹히는 것은 칸마다 따로이므로
    // 어느 쪽이 지금 움직였는지가 그대로 남습니다.
    //
    // **여기서 읽는 것은 화면에 있는 수입니다.** 상태의 값을 쓰면 숫자가 아직 굴러가는 동안
    // 파형만 먼저 최대가 됩니다.
    this.chrome.scoreWave.setSurge(this.chrome.chips.surge, this.chrome.mult.surge,
      payoutLevel(this.chrome.chips.amount, this.chrome.mult.amount))
    // **0 인 칸은 사라집니다.** 쌓인 것이 없으면 그 줄이 무엇을 나타내는지도 없습니다 —
    // 판이 서기 전과 판이 끝난 뒤의 두 상자가 그 자리입니다.
    this.chrome.scoreWave.setLive(stepMs, this.chrome.chips.amount > 0,
      this.chrome.mult.amount > 0)
    this.show.advanceRisers(stepMs)

    // 흔들림은 줄어듭니다. **판만 흔들고 배경은 가만히 둡니다** — 둘 다 흔들면 무엇이
    // 맞은 것인지 읽히지 않습니다.
    if (this.show.shake > 0.08) {
      const angle = Math.random() * Math.PI * 2
      this.board.position.set(
        Math.cos(angle) * this.show.shake, Math.sin(angle) * this.show.shake)
      this.overlay.position.set(this.board.x * 0.5, this.board.y * 0.5)
      this.show.shake *= 0.84
    } else if (this.board.x !== 0 || this.board.y !== 0) {
      this.board.position.set(0, 0)
      this.overlay.position.set(0, 0)
      this.show.shake = 0
    }
  }

  /**
   * 정해진 시간만큼 틱을 돌립니다. 수동 틱에서만 부릅니다.
   *
   * **100밀리초어치마다 한 번 이벤트 루프에 자리를 내줍니다.** 그림과 소리는 비동기로
   * 오므로 한 번에 다 돌리면 그것들이 도착할 틈이 없습니다. 틱의 수는 그와 무관하게 같습니다.
   */
  async advanceManually(ms: number): Promise<void> {
    let left = Math.max(1, Math.round(ms / STEP_MS))
    while (left > 0) {
      const burst = Math.min(left, 6)
      for (let i = 0; i < burst; i++) this.tick(STEP_MS)
      left -= burst
      await new Promise<void>(done => setTimeout(done, 0))
    }
  }

  refresh(): void {
    this.refreshes++
    const state = this.state

    // **세울 것 목록은 여기서 비웁니다.** 상점과 팩이 같이 쓰므로, 어느 한쪽이 비우면
    // 다른 쪽이 이미 담아 둔 것을 지우게 됩니다.
    this.shop.reveals.length = 0

    // **오르내린 만큼이 그 칸에서 한 번 떠오릅니다.** 칸의 숫자는 언제나 지금 값이므로,
    // 눈을 그 칸에 두고 있지 않으면 무엇이 줄었는지 모른 채 지나갑니다.
    //
    // 돈은 여기서 세지 않습니다 — 동전이 날아가 꽂히는 것이 이미 그 일을 하고 있고,
    // 둘이 겹치면 같은 말이 한 자리에서 두 번입니다.
    this.show.slotDelta(this.chrome.hands, this.chrome.panelShown.hands, state.handsLeft, UI.good)
    this.show.slotDelta(this.chrome.discards, this.chrome.panelShown.discards,
      state.discardsLeft, UI.discard)
    this.show.slotDelta(this.chrome.anteSlot, this.chrome.panelShown.ante, state.ante, UI.ink)
    this.chrome.panelShown.hands = state.handsLeft
    this.chrome.panelShown.discards = state.discardsLeft
    this.chrome.panelShown.ante = state.ante

    this.chrome.money.target = this.shown.money
    this.chrome.score.target = this.shown.score
    this.chrome.hands.text = String(state.handsLeft)
    this.chrome.discards.text = String(state.discardsLeft)
    this.chrome.anteSlot.text = `${state.ante} / ${this.data.run.winAnte}`
    this.chrome.deckLabel.text = tf('ui.stat.deck', { left: state.drawPile.length,
      all: state.deck.length })
    this.chrome.jokerCount.text = `${state.jokers.length} / ${state.rules.jokerSlots}`
    this.chrome.consumableCount.text =
      `${state.consumables.length} / ${state.rules.consumableSlots}`

    this.input.updateHints()

    this.blind.syncBadge()
    this.cards.syncCards()
    this.cards.syncJokers()
    this.tray.syncConsumables()
    this.blind.syncTags()
    this.panels.syncActive()
    this.shop.syncShop()
    // 건너뛰기 연출 중에는 판과 팩이 그대로입니다. 연출이 끝나는 자리가 다시 세웁니다.
    if (!this.blind.skipping) this.pack.syncPack()
    // **줄과 상점과 팩이 다 선 뒤입니다.** 고른 것이 어느 통인지를 이 줄이 적어 두는데,
    // 그 통들은 다시 그릴 때마다 새로 만들어집니다 — 조커 줄 끝에서 부르고 있어서 상점과
    // 팩의 딱지는 이미 없어진 통이 적혔습니다.
    this.tray.syncHeldBar()
    this.chrome.syncButtons()
    this.show.syncMood()
    this.show.syncMusic()
    this.input.previewSlots()
    // 떠 있는 판만 다시 그립니다. **닫힌 판을 그리는 것은 낭비이고**, 남은 카드는 덱
    // 52장을 매번 만듭니다.
    if (this.panels.modals.has(this.panels.handList)) this.panels.drawHandList()
    if (this.panels.modals.has(this.cards.deckView)) this.panels.drawDeckView()
    if (!this.blind.skipping) this.blind.drawBlindPick()
    // 다시 시작하면 판을 걷습니다. **띄우는 것은 `tick` 이 합니다** — 연출이 끝난 뒤여야
    // 하기 때문입니다.
    if (this.state.phase !== 'lost' && this.state.phase !== 'won') this.session.drawGameOver()
    // **국면을 말하는 글은 연출이 끝난 뒤에 바뀝니다.** 코어는 액션 하나를 끝까지 처리해
    // 두므로, 그대로 그리면 아직 득점을 보고 있는데 상점의 지시문이 떠 있습니다.
    // **지난 지시문을 붙잡아 두지 않습니다.** 이미 낸 뒤에 「5장 골랐습니다」가 남아 있으면
    // 무엇을 하라는 말인지가 아니라 무엇을 했었는지가 됩니다.
    this.input.drawHint(this.presented ? this.input.hintText() : '')
    this.chrome.drawPips()
    // **기본 해상도면 걷지 않습니다.** 새로 만든 글은 렌더러의 해상도로 구워지므로, 화면
    // 배율이 1 이하일 때 원하는 값은 그 기본값과 같습니다 — 트리 전체를 걷는 것은 배율이
    // 1을 넘을 때만 필요하고, 그때도 `layout` 이 이미 한 번 걸었습니다.
    if (this.world.scale.x > 1) this.sharpen(this.world.scale.x)
  }

  /**
   * 손패를 다룰 수 있는 때인가 — 라운드 중이고 · 손패가 다 깔렸고 · 연출이 돌지 않습니다.
   *
   * **소모품을 쓰는 것도 이때뿐입니다.** 상점에서 쓰면 카드를 고르는 타로가 고른 것 없이
   * 그대로 사라지고, 그것은 쓴 것이 아니라 버린 것입니다 — 소모품은 손패를 앞에 두고 씁니다.
   */
  get handReady(): boolean {
    const dealt = this.cards.deals.length === 0 && this.clock >= this.cards.dealtUntil
      && this.shown.hand.length === this.state.hand.length
    return this.state.phase === 'round' && this.shown.phase === 'round'
      && this.shown.hand.length > 0 && dealt && !this.player.busy && !this.panels.modals.busy
  }

  /**
   * 손패를 만질 수 있는 때인가. **누르기 · 끌기 · 올려두기 · 소리가 이것을 확인합니다.**
   *
   * **`handReady` 와 갈라 둡니다.** 그쪽은 단추가 올라와 있을 때를 정하는 자리라 「전부
   * 가라앉았는가」를 묻고, 그중 `shown.hand.length === state.hand.length` 는 연출이 한 장이라도
   * 어긋나면 거짓이 됩니다 — 단추가 내려가 있는 것은 그때 눈에 보이지만, **그것을 조작과
   * 소리의 걸쇠로 쓰면 그 라운드가 통째로 죽습니다.** 카드가 눌리지도 않고 소리도 나지
   * 않고, 왜 그런지 화면에 아무것도 적히지 않습니다. 실제로 그렇게 되었습니다.
   *
   * 여기서 막는 것은 **지금 무언가가 도는 중인 것**뿐입니다 — 박자가 돌거나, 낸 카드가
   * 아직 판에 있거나, 나가는 중이거나, 깔리는 중입니다. 개수가 맞는지는 묻지 않습니다.
   */
  get handLive(): boolean {
    return this.state.phase === 'round' && this.shown.phase === 'round'
      && !this.player.busy && !this.panels.modals.busy
      && this.cards.playedViews.length === 0 && this.cards.fades.length === 0
      && this.cards.deals.length === 0 && this.clock >= this.cards.dealtUntil
  }
}
