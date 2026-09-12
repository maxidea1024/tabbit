import { Container, Graphics, Matrix, RenderTexture, Text, Texture } from 'pixi.js'
import { EditionKind } from '../generated/enums/edition-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { describe } from '../core/describe'
import { StakeKind } from '../generated/enums/stake-kind'
import { snapshotHash } from '../core/hash'
import { type Action, apply, newRun } from '../core/run'
import { language, nameOf, setLanguage, t, tf } from '../core/strings'
import { type RunSetup, setupLabel, validSetup } from '../ui/setup'
import { NUMERALS, outline, useFont } from '../ui/font'
import { type ShopItem } from '../core/shop'
import { type JokerInstance } from '../core/state'
import { ladder } from '../feedback/audio'
import { type JokerLook, JokerView } from '../render/joker-view'
import { fraction } from '../render/motion'
import { setCardSet, setLookOf } from '../render/card-set'
import { groove } from '../render/skin'
import { popupCenter, setUiTheme, SIZE, TEXT, UI, WEIGHT } from '../render/theme'
import { Button, restyleButtons } from '../ui/widgets'
import { type ChallengeProgress, loadProgress, saveProgress } from '../ui/challenge'
import { type CollectionProgress, loadCollection } from '../core/collection'
import { randomSeed, Title } from '../ui/title'
import { type EndLine, LeaderboardHub } from '../ui/hub'
import { NetStatus } from '../ui/net-status'
import { LoginScene } from '../ui/login-scene'
import { ConfirmPanel } from '../ui/confirm'
import { canQuit, keepAwake, quitGame } from '../ui/shell'
import { type MetricsAcc, newMetrics, observe } from '../core/metrics'
import { clearRun, loadRun, type SavedRun, saveRun } from '../core/save-run'
import { type Scene } from '../render/scene'
import { type TransitionId } from '../render/transition'
import { PANEL_BOTTOM } from '../ui/modal'
import { cellPlate, hairline, ProgressBar, SECTION_H, sectionHead, valueCell } from '../ui/parts'
import {
  chosen, graphicsLevel, loadOptions, type Options, saveOptions, transitionWanted,
} from '../ui/options'
import { Toasts } from '../ui/toast'
import { LEFT, PANEL_GROOVES, PANEL_W, POPUP_X, RANK_TICK, SAVE_GAP, SORT_HIDE } from './metrics'
import { blindName } from './tables'
import { guestBoot, RANK_MARK } from './helpers'
import { type Game } from './game'
import * as account from '../net/session'
export class SessionPart {
  constructor(private readonly game: Game) {}

  /** 깬 챌린지의 목록. 저장에 남습니다. */
  readonly challenges: ChallengeProgress = loadProgress()

  /**
   * 무엇을 만나 보았는가.
   *
   * **판이 아니라 저장이 가지는 값입니다.** 챌린지의 깬 목록과 같은 자리이고, 코어에
   * 닿지 않으므로 구워 둔 리플레이의 해시가 그대로입니다.
   */
  readonly collected: CollectionProgress = loadCollection()

  /** 지금 도는 판의 챌린지. 빈 문자열이면 챌린지가 아닙니다. */
  challengeId = ''

  /** 타이틀. **시작을 누르기 전에는 판이 없습니다.** */
  title!: Title

  /**
   * 지금 어느 씬인가.
   *
   * **이 클래스가 아는 것은 둘입니다** — `loading` 은 이 클래스가 서기 전이라 진입점의
   * 몫이고, 여기서는 타이틀과 판 사이를 오갑니다.
   */
  scene: Scene = 'title'

  /** 도구가 손으로 정한 그래픽 품질. **옵션을 다시 걸어도 남습니다** — 없으면 옵션이 정합니다. */
  qualityOverride?: 'high' | 'medium' | 'low'

  /** 옵션. 타이틀이 고치고 화면이 읽습니다. */
  readonly settings: Options = loadOptions()

  /**
   * 다음 판을 무엇으로 시작하는가.
   *
   * **표에 있는 것으로 걸러 읽습니다.** 저장된 값이 표에 없는 덱을 가리키면 시작 조건이
   * 하나도 걸리지 않은 판이 조용히 돌아갑니다.
   */
  setup(): RunSetup {
    return validSetup(this.game.data, {
      deckId: this.settings.deck, stake: this.settings.stake,
    })
  }

  /** 끝났을 때 덮는 판. */
  readonly gameOver = new Container()

  /**
   * 주소에 적혀 온 시드. **아직 쓰지 않은 동안만 값이 있습니다.**
   *
   * 타이틀에 서면 새 시드로 판을 미리 깔고, 부팅도 그 길을 지납니다 — 그래서 이것이 없으면
   * `?seed=` 는 화면이 처음 뜨는 그 순간에 버려집니다.
   */
  bootSeed?: string

  /** 게임오버 판의 득점 바. */
  private overBar?: { bar: ProgressBar; begin: number; ratio: number }

  gameOverShown = false

  private gameOverPop = 0

  private gameOverBoard?: Container

  /** 끝난 판의 단추 둘. **다 선 뒤에 자리를 알리려고 들고 있습니다.** */
  private gameOverAgain?: Container

  private gameOverHome?: Container

  /**
   * 게임오버 판이 앉는 자리.
   *
   * **다른 판들과 아래가 같습니다.** 이 판만 화면 가운데에 앉아 있었고, 판의 높이는 랭크
   * 런인지에 따라 달라지므로 아래 변의 자리는 그때 셈해 둡니다 — 이 판의 아이들은 origin
   * 을 가운데로 두고 놓이므로 절반을 올린 자리입니다.
   */
  private gameOverY = SIZE.height / 2

  /** 그 판의 가로 가운데. 넓이를 알아야 왼쪽 판을 침범하는지가 정해집니다. */
  private gameOverX = POPUP_X

  /**
   * 이 런의 액션.
   *
   * **랭크 런을 올리려면 처음부터 끝까지가 필요합니다.** 이어하기도 같은 것을 쓰므로,
   * 상태를 저장하는 것보다 이것을 저장하는 편이 상태 구조가 바뀌어도 살아남습니다.
   */
  actions: Action[] = []

  /** 이 런의 지표. 이벤트를 지나가며 쌓입니다. */
  metrics: MetricsAcc = newMetrics()

  hub!: LeaderboardHub

  login!: LoginScene

  netStatus!: NetStatus

  /** 끝난 판에 얹을 순위 한 줄. 판정이 오기 전에는 `undefined` 입니다. */
  private rankLine?: EndLine

  /** 그 줄을 그린 자리. 숫자가 굴러 내려가는 동안 여기에 다시 그립니다. */
  private rankNode?: Container

  /** 굴러 내려가는 중의 값. */
  private rankRoll = 0

  /** 순위의 세는 소리가 마지막으로 난 시각. */
  private rankTickAt = -1

  /**
   * 옵션이 정한 것을 화면에 겁니다.
   *
   * **여기 있는 것은 전부 실제로 무언가를 합니다.** 값만 저장하고 아무 데도 쓰지 않으면
   * 그것은 옵션이 아니라 장식입니다.
   */
  /**
   * 옵션이 정한 것 중 **화면을 세우지 않아도 걸리는 것들.**
   *
   * **켤 때 한 번 걸어야 합니다.** `applyOptions` 는 옵션을 만졌을 때와 판을 열 때만
   * 불리므로, 그 전까지 소리는 만들 때의 기본값으로 났습니다 — 효과음 0.35 · 배경음
   * 0.5 이고, 옵션에는 60 · 60 이 적혀 있습니다. **음악만 들리고 효과음은 거의 들리지
   * 않는 것이 그 차이입니다**(4.7dB). 소리를 꺼 두었어도 판을 열기 전까지 났습니다.
   *
   * 나머지 절반(말 · 글꼴 · 카드 세트 · 겉면)은 화면이 선 뒤에야 걸 수 있으므로 갈라
   * 둡니다 — 여기 있는 것은 값 하나를 옮기는 것뿐이라 언제 불러도 됩니다. 소리 길이
   * 열리기 전에 정해 두면 열 때 그 값으로 시작합니다.
   */
  applyQuietOptions(): void {
    this.game.audio.muted = !this.settings.sound
    this.game.audio.volume = this.settings.volume / 100
    this.game.audio.music.muted = !this.settings.music
    this.game.audio.music.volume = this.settings.musicVolume / 100
    this.game.player.base = this.settings.speed
    this.game.show.particles.enabled = this.settings.particles
    this.game.show.haptics.enabled = this.settings.haptics
    // **초당 몇 프레임까지 그리는가.** 0 은 화면이 정하는 대로입니다 — 티커에 0 을 넣으면
    // 문턱이 없어집니다.
    this.game.app.ticker.maxFPS = this.settings.frameCap
    // **그래픽 품질.** 지금 갈리는 것은 재가 되는 전환 하나입니다 — 셰이더 둘 가운데 어느
    // 것인지와 파티클을 얹는지가 여기서 정해집니다.
    this.game.transition.quality = this.qualityOverride ?? graphicsLevel(this.settings)
    // **칩과 배수의 파형은 화질을 보지 않습니다.** 그 둘은 판이 도는 내내 눈이 머무는
    // 자리이고, 파형은 그 상자가 살아 있다는 표시입니다 — 화질을 낮춘 화면에서 그 자리만
    // 죽은 상자가 되면 그것은 연출이 덜 보이는 것이 아니라 다른 게임입니다.
    //
    // **값이 그것을 허락합니다.** 판의 1.50%에 그림 읽기 1회이므로 배경 셰이더 하나의 1%
    // 아래이고, 화질로 아낄 것이 있는 자리가 아닙니다.
  }

  applyOptions(): void {
    this.applyQuietOptions()

    // **말이 바뀌면 화면을 다시 그립니다.** 글은 그릴 때 한 번 읽히므로, 다시 그리지 않으면
    // 고른 그 순간에는 아무것도 바뀌지 않고 다음 판부터 바뀝니다.
    const want = chosen(this.settings)
    const changed = want !== language()
    setLanguage(want)
    if (changed) useFont(want)

    saveOptions(this.settings)
    // **고른 그 자리에서 갈아입습니다.** 다음 판까지 기다릴 이유가 없습니다 — 겉모습이므로
    // 도는 판의 규칙에 닿지 않습니다.
    setCardSet(setLookOf(this.game.data, this.settings.cardSet))
    // **뒷면도 함께 갈아입습니다.** 무늬를 세트가 정하므로, 여기서 다시 정하지 않으면
    // 앞면만 바뀌고 뒷면은 앞 세트의 무늬로 남습니다.
    this.game.cards.syncCardBack()
    // 도움 표시는 켜고 끄는 그 자리에서 바로 사라져야 합니다.
    this.game.input.updateHints()
    this.game.cards.syncCards()
    if (changed) this.relabel()

    // **판의 겉면.** 고른 그 자리에서 갈아입습니다 — 겉모습이므로 도는 판의 규칙에 닿지
    // 않습니다.
    if (this.settings.uiTheme !== this.themeShown) {
      this.themeShown = this.settings.uiTheme
      setUiTheme(this.settings.uiTheme)
      this.restyle()
    }
  }

  /**
   * 겉면을 갈아 끼운 뒤 그려 둔 것을 다시 그립니다.
   *
   * **한 번 그리고 마는 것만 여기 있습니다.** 판때기는 그릴 때의 색으로 삼각화되어 있어서
   * 색을 바꿨다고 저절로 바뀌지 않습니다 — 떠 있는 판들은 열 때마다 다시 그리므로 여기서
   * 손댈 것이 없고, 남는 것이 왼쪽 판과 그 안의 칸들입니다.
   */
  private restyle(): void {
    // **떠 있는 판들도 다시 세웁니다.** 열 때마다 다시 그리므로 대개는 손댈 것이 없지만,
    // 테마를 고르는 그 판은 **지금 열려 있습니다** — 고른 사람이 보고 있는 것이 그 판이라,
    // 그것만 옛 색으로 남으면 「고쳤는데 아무 일도 없다」로 읽힙니다. 말을 바꿀 때와 같은
    // 자리에서 같은 일을 합니다.
    this.game.panels.optionsPanel.relabel()
    this.game.panels.guide.relabel()
    this.game.panels.collection.relabel()
    this.game.panels.runPanel.relabel()
    // 정산 판이 열려 있으면 그것도 새 겉면으로 다시 그립니다.
    if (this.game.panels.modals.has(this.game.payout.panel)) this.game.payout.drawPayout()
    // **화면에 오래 남아 있는 단추들.** 판 안의 단추는 판을 열 때 새로 만들어지지만 이것들은
    // 처음 한 번 그려지고 그대로 남습니다 — 겉면을 갈아 끼운 뒤에도 앞 겉면의 색이었고,
    // 타이틀에 다녀와야 바뀌는 것으로 보였습니다. **이름으로 세지 않습니다** — 단추가 스스로
    // 무대에 붙을 때 등록합니다(`ui/widgets.ts` 의 `restyleButtons`).
    restyleButtons()
    this.title.restyle()
    this.login.relabel()
    this.game.chrome.panelPlate?.resize(PANEL_W + 24, SIZE.height - 44)
    this.game.chrome.drawFrames()
    this.game.chrome.panelGrooves.clear()
    for (const at of PANEL_GROOVES) groove(this.game.chrome.panelGrooves, LEFT, at, PANEL_W)
    for (const slot of this.game.chrome.panelSlots) slot.restyle()
    // **칩 × 배수의 바탕도 겉면을 따릅니다.** 파랑·붉음 단색이던 동안은 겉면과 무관해서
    // 여기서 다시 그릴 것이 없었고, 판의 칸 색을 쓰기 시작하면 이 둘만 앞 겉면으로 남습니다.
    const boxes = this.game.chrome.scoreBoxes
    if (boxes) this.game.chrome.paintScoreBox(boxes.chips, boxes.mult)
    this.game.refresh()
  }

  /** 마지막으로 갈아입은 겉면. 같으면 다시 그리지 않습니다. */
  private themeShown = loadOptions().uiTheme

  /**
   * 말이 바뀌었을 때 글을 다시 읽습니다.
   *
   * **`refresh` 로는 모자랍니다.** 그것은 매번 다시 그리는 것들을 그리고, 여기 있는 것들은
   * 만들 때 한 번 읽은 글을 그대로 들고 있습니다 — 칸의 이름, 왼쪽 아래 버튼, 타이틀.
   */
  private relabel(): void {
    this.game.chrome.score.caption = t('ui.slot.round_score')
    this.game.chrome.chips.caption = t('ui.slot.chips')
    this.game.chrome.mult.caption = t('ui.slot.mult')
    this.game.chrome.hands.caption = t('ui.slot.hands')
    this.game.chrome.discards.caption = t('ui.slot.discards')
    this.game.chrome.money.caption = t('ui.slot.money')
    this.game.chrome.anteSlot.caption = t('ui.slot.ante')

    this.game.chrome.playButton.text = t('ui.button.play')
    this.game.chrome.discardButton.text = t('ui.button.discard')
    this.game.chrome.primaryButton.text = t('ui.button.select_blind')
    this.game.chrome.skipButton.text = t('ui.button.skip')
    this.game.chrome.rerollButton.text = t('ui.button.reroll')
    this.game.chrome.sortRankButton.text = t('ui.button.sort_rank')
    this.game.chrome.sortSuitButton.text = t('ui.button.sort_suit')
    this.game.chrome.infoButton.text = t('ui.run_info.title')
    this.game.chrome.menuButton.text = t('ui.button.menu')

    // **테두리의 굵기도 말을 탑니다.** 굵기는 그 글자의 획 사이 틈에서 나오는 값이고
    // 한자의 틈이 한글의 절반이므로, 만들 때의 말로 정해 둔 굵기는 말을 바꾸면 어긋납니다.
    // 단추는 글을 적는 자리에서 스스로 다시 정하고, 한 번 만들고 마는 것이 이 둘입니다.
    this.game.chrome.handLabel.style.stroke = outline(TEXT.big, UI.outline)
    this.game.show.headline.style.stroke = outline(TEXT.hero, UI.outline)

    this.title.relabel()
    this.login.relabel()
    this.hub.relabel()
    this.syncAccount()
    this.game.panels.optionsPanel.relabel()
    this.game.panels.guide.relabel()
    this.game.panels.collection.relabel()
    this.game.panels.runPanel.relabel()
    // **정산 판도 지금의 말로 다시 적습니다.** 열려 있는 채로 말을 바꾸면 그 판만 앞의
    // 말로 남았습니다 — 줄의 이유를 열쇠로 들고 있으므로 다시 그리면 됩니다.
    if (this.game.panels.modals.has(this.game.payout.panel)) this.game.payout.drawPayout()
    this.game.refresh()
  }

  /**
   * 타이틀에서 시작합니다.
   *
   * 게임 방법은 **처음 여는 사람에게만** 저절로 펼쳐집니다. 두 번째부터는 타이틀의 버튼과
   * 왼쪽 아래 버튼으로 엽니다.
   */
  /**
   * 옵션을 엽니다.
   *
   * **시드는 판 밖에서만 고칩니다.** 판이 돌기 시작하면 그 판의 시드이고, 도는 중에
   * 바꾸면 보고 있는 패와 적힌 시드가 어긋납니다.
   */
  openOptions(): void {
    this.game.panels.optionsPanel.setSeed(this.game.state.seed, this.scene === 'title')
    this.game.panels.modals.open(this.game.panels.optionsPanel)
  }

  /**
   * 이긴 것을 저장에 남깁니다.
   *
   * **챌린지 런이면 그 챌린지가 깬 것으로 들어갑니다.** 다음 하나가 열리는 것이 그
   * 목록의 길이로 정해지므로, 여기 적히지 않으면 20종이 다섯에서 멈춥니다.
   */
  /**
   * 챌린지 목록을 엽니다. **한 번만 저장합니다.**
   *
   * 보스를 격파한 자리와 판을 이긴 자리 둘이 이것을 부릅니다 — 대개는 앞쪽이고, 뒤쪽은
   * 챌린지 런으로 이긴 것을 적으러 지나는 길에 함께 엽니다.
   */
  unlockChallenges(): void {
    if (this.challenges.unlocked) return
    this.challenges.unlocked = true
    saveProgress(this.challenges)
  }

  recordWin(): void {
    let changed = false
    if (!this.challenges.unlocked) {
      this.challenges.unlocked = true
      changed = true
    }
    if (this.challengeId !== '' && !this.challenges.beaten.includes(this.challengeId)) {
      this.challenges.beaten.push(this.challengeId)
      changed = true
    }
    // **판이 그 목록을 그대로 들고 있습니다.** 챌린지 탭이 열렸는지는 판을 열 때
    // 다시 세므로 여기서 알릴 자리가 없습니다.
    if (changed) saveProgress(this.challenges)
  }

  /**
   * 챌린지 판.
   *
   * **아직 안 열렸어도 판을 엽니다.** 처음에는 쪽지로 알렸는데, 쪽지가 서는 자리는 판
   * 안의 덱 옆이라 타이틀에서는 누른 곳과 먼 빈 구석에 떴습니다 — 알릴 것은 판 안에
   * 적히고, 20칸이 잠긴 채로 보이는 것이 무엇이 남았는지를 함께 알립니다.
   */
  /**
   * 판을 여는 자리를 엽니다.
   *
   * **지금 저장된 것에 표시를 맞춰 엽니다** — 판을 한 번 세워 두고 다시 여는 것이므로,
   * 맞추지 않으면 처음 열 때의 자리에 표시가 남습니다. 저장된 판도 그때 다시 읽습니다.
   */
  openRunPanel(): void {
    this.game.panels.runPanel.relabel()
    this.game.panels.runPanel.setSetup(this.setup())
    this.game.panels.runPanel.setSignedIn(this.hub.signedIn)
    this.game.panels.runPanel.setSaved(loadRun())
    this.game.panels.runPanel.open()
    this.game.panels.modals.open(this.game.panels.runPanel)
  }

  /**
   * 저장된 판을 이어서 합니다.
   *
   * **되살리지 못하면 그 사실을 적습니다.** 저장이 손상되었거나 규칙이 바뀌어 같은 판이
   * 다시 만들어지지 않는 경우이고, 그때 아무 일도 일어나지 않으면 눌린 것으로 보이지
   * 않습니다.
   */
  continueRun(): void {
    const saved = loadRun()
    if (saved && this.resumeRun(saved)) return
    this.game.panels.runPanel.setSaved(undefined)
    this.game.input.toasts.push(t('ui.run.resumeFailed'), t('ui.run.resumeFailedBody'), UI.bad,
      3.4)
  }

  /**
   * 새 판을 열지 묻습니다.
   *
   * **저장된 판이 있으면 그것이 사라집니다.** 판 하나만 적어 두므로 새 판을 열면 앞의
   * 것이 덮입니다 — 묻는 글이 그것을 적고, 그때는 되돌릴 수 없는 것이므로 붉습니다.
   */
  askStartNew(next: RunSetup, run: () => void): void {
    const saved = this.game.panels.runPanel.hasSaved
    // **무엇이 걸리는지 함께 적힙니다.** 덱과 스테이크의 이름만으로는 그 판이 무엇이
    // 다른지 알 수 없고, 누르기 직전의 자리가 그것을 읽는 마지막 자리입니다.
    this.ask(t('ui.run.startAsk'),
             tf(saved ? 'ui.run.startBodySaved' : 'ui.run.startBody',
                { what: setupLabel(this.game.data, next) }),
             t('ui.button.start'), saved, run, this.setupNotes(next))
  }

  /**
   * 이 설정으로 시작하면 무엇이 걸리는가.
   *
   * **문장은 데이터에서 나옵니다.** 덱의 시작 조건은 `describe()` 가 효과 행을 읽어
   * 만들고, 스테이크의 규칙은 런 정보 판과 같은 한 문장입니다 — 여기서 새로 적으면
   * 같은 것을 두 문장으로 적게 됩니다.
   */
  private setupNotes(setup: RunSetup): string[] {
    const lines = describe(this.game.data, this.game.data.deckEffects.get(setup.deckId) ?? [])
    if (lines.length === 0) lines.push(t('ui.note.no_rules'))
    const row = this.game.data.tables.stake.records
      .find(one => StakeKind[one.stake] === setup.stake)
    if (row) {
      const record = this.game.data.tables.stake.findByStake(row.stake)
      if (record) {
        lines.push(tf('ui.stake.note', {
          column: record.anteColumn,
          reward: record.smallBlindReward,
          discards: record.discardsDelta,
        }))
      }
    }
    return lines
  }

  back(): void {
    if (this.game.panels.modals.busy) {
      this.game.panels.modals.closeTop()
      return
    }
    // 자리를 비우던 것을 그만둡니다. **고른 것이 있으면 그것을 먼저 놓습니다.**
    if (this.game.tray.focus) {
      if (this.game.tray.held) {
        this.game.tray.held = undefined
        this.game.refresh()
        return
      }
      this.game.tray.leaveFocus()
      return
    }
    if (this.game.tray.held) {
      this.game.tray.held = undefined
      this.game.refresh()
      return
    }
    if (canQuit()) this.askQuit()
  }

  /**
   * 게임을 나갈지 묻습니다. **되돌릴 수 없으므로 반드시 묻습니다.**
   *
   * **글이 씬마다 다릅니다.** 판이 도는 중이면 그 판이 저장된다는 것이 알아야 하는 것이고,
   * 타이틀과 로그인 화면에서는 저장할 판이 없으므로 그 말이 뜻을 갖지 않습니다.
   */
  askQuit(): void {
    // **나갈 수 없는 자리에서는 묻지 않습니다.** 브라우저의 탭은 스크립트가 닫지 못하므로,
    // 물어 놓고 「예」를 눌렀을 때 아무 일도 일어나지 않으면 그것은 고장으로 보입니다.
    if (!canQuit()) {
      this.game.input.toasts.push(t('ui.quit.browser'), t('ui.quit.browserBody'), UI.inkDim, 3.6)
      return
    }
    const inRun = this.scene === 'run'
    this.ask(t('ui.quit.ask'), t(inRun ? 'ui.quit.bodyRun' : 'ui.quit.body'),
             t('ui.button.quit'), true, () => {
               if (quitGame()) return
               this.game.input.toasts.push(t('ui.quit.browser'), t('ui.quit.browserBody'),
                                UI.inkDim, 3.6)
             })
  }

  /** 저장된 판을 버립니다. **묻고 나서 합니다** — 되돌릴 수 없습니다. */
  askDiscardRun(): void {
    this.ask(t('ui.run.discardAsk'), t('ui.run.discardBody'), t('ui.run.discard'), true,
             () => {
               clearRun()
               this.game.panels.runPanel.setSaved(undefined)
             })
  }

  /**
   * 시드를 갈아 끼웁니다.
   *
   * **시드는 판 하나를 정하는 문자열입니다.** 덱 섞기 · 상점 · 팩 · 확률 발동이 저마다
   * 다른 난수 흐름을 쓰지만 그 흐름 전부가 이 문자열에서 갈라져 나오므로, 같은 시드는
   * 같은 판입니다.
   *
   * 시작하기 전에만 됩니다 — 판이 돌기 시작하면 그 판의 시드입니다.
   */
  useSeed(seed: string): void {
    if (this.scene !== 'title') return
    this.layRun(seed)
  }

  /**
   * 시드 하나로 판을 새로 깝니다.
   *
   * **화면이 주장하던 것도 함께 맞춥니다** — 상태만 갈아 끼우면 화면은 앞 판의 점수와
   * 패를 그대로 들고 있습니다.
   */
  layRun(seed: string): void {
    const setup = this.setup()
    this.game.input.hintCache = undefined
    this.game.state = newRun(this.game.data, seed, setup.deckId, setup.stake,
                        undefined, this.challengeId).state
    this.actions = []
    this.metrics = newMetrics()
    this.rankLine = undefined
    this.rankNode = undefined
    // **뒷면부터입니다.** 손패를 다시 그리기 전에 정해야, 새로 깔리는 카드가 이 판의
    // 뒷면으로 깔립니다.
    this.game.cards.syncCardBack()
    this.game.payout.settleShown()
    this.game.refresh()
    this.writeSeedUrl(seed)
  }

  /**
   * 주소에 이 판의 시드를 적습니다.
   *
   * **그 주소를 열면 같은 판입니다** — 지금 페이지를 다시 읽지는 않으므로 보고 있는
   * 화면은 그대로입니다.
   *
   * **시드만 갈아 끼웁니다.** 물음표 뒤를 통째로 새로 쓰면 함께 실려 있던 것이 지워지고,
   * `?tick=manual` 로 연 판이 그 자리에서 그것을 잃습니다.
   */
  private writeSeedUrl(seed: string): void {
    try {
      const url = new URL(location.href)
      url.searchParams.set('seed', seed)
      history.replaceState(null, '', url.toString())
      document.title = `clover — ${seed}`
    } catch {
      // 주소를 바꿀 수 없는 자리에서는 판만 바뀝니다.
    }
  }

  /**
   * 지금 판을 적어 둡니다.
   *
   * **액션 목록을 적습니다.** 되살리는 것은 그것을 `apply` 로 다시 돌리는 것이고, 그
   * 길은 서버의 판정과 `headless` 가 지나는 길과 같습니다 — 이어서 한 판을 랭크에 올려도
   * 서버가 세는 판과 어긋나지 않습니다.
   */
  rememberRun(): void {
    if (this.scene !== 'run') return
    this.game.saveDue = true
  }

  /**
   * 적어 둘 것이 있으면 적습니다.
   *
   * **액션마다 적지 않습니다.** 적는 것은 판의 액션 기록을 통째로 글로 만들어 저장소에
   * 넣는 일이고, 저장소는 디스크에 있습니다 — 안드로이드에서는 액션마다 그 기다림이
   * 눌린 자리에서 그대로 보입니다. 기록은 판이 길어질수록 길어지므로 뒤로 갈수록 커집니다.
   *
   * **묶어도 잃는 것이 없습니다.** 뒤로 물러날 때와 판을 떠날 때는 기다리지 않고 바로
   * 적으므로(`force`), 묶여 있다가 사라지는 것은 앱이 그 사이에 죽었을 때의 액션 하나
   * 뿐입니다.
   */
  flushRun(force = false): void {
    if (!this.game.saveDue || this.scene !== 'run') return
    const now = typeof performance === 'undefined' ? Date.now() : performance.now()
    if (!force && now - this.game.saveAt < SAVE_GAP) return
    this.game.saveDue = false
    this.game.saveAt = now
    saveRun({
      seed: this.game.state.seed,
      deckId: this.game.state.deckId,
      stake: this.game.state.stake,
      challengeId: this.game.state.challengeId,
      actions: this.actions.slice(),
      hash: snapshotHash(this.game.state),
      ...(this.hub.rankedRun ? { ranked: this.hub.rankedRun } : {}),
    }, this.game.state)
  }

  /**
   * 저장된 판을 이어서 합니다. 되살리지 못하면 거짓입니다.
   *
   * **되살린 것이 적어 둔 것과 같은지 봅니다.** `apply` 는 받을 수 없는 액션을 조용히
   * 넘기므로 손상된 저장으로도 판 하나가 만들어지고, 그 판은 그만두던 자리와 다릅니다 —
   * 해시가 어긋나면 저장을 버립니다.
   */
  private resumeRun(saved: SavedRun): boolean {
    this.game.input.hintCache = undefined
    const start = newRun(this.game.data, saved.seed, saved.deckId, saved.stake,
                         undefined, saved.challengeId)
    const state = start.state
    const acc = newMetrics()
    observe(acc, start.events)
    for (const action of saved.actions) {
      observe(acc, apply(this.game.data, state, action).events)
      if (state.phase === 'lost' || state.phase === 'won') break
    }

    if (state.phase === 'lost' || state.phase === 'won'
        || snapshotHash(state) !== saved.hash) {
      clearRun()
      return false
    }

    this.dropRun()
    this.challengeId = saved.challengeId
    this.game.state = state
    this.actions = saved.actions.slice()
    this.metrics = acc
    this.rankLine = undefined
    this.rankNode = undefined
    // **랭크였으면 랭크로 돌아옵니다.** 그 사실은 허브에만 있으므로, 되돌리지 않으면
    // 이어서 끝낸 판이 올라가지 않습니다.
    if (saved.ranked) this.hub.restoreRanked(saved.ranked)
    else this.hub.clearRanked()
    this.game.cards.syncCardBack()
    this.game.payout.settleShown()
    this.game.refresh()
    this.writeSeedUrl(saved.seed)
    this.enterRun()
    return true
  }

  /**
   * 랭크 런을 시작합니다.
   *
   * **서버가 준 시드로만 시작합니다.** 시드를 고르게 두면 좋은 시드를 오프라인에서 찾아
   * 오는 것이 가능하고, 그것은 실력이 아니라 계산입니다.
   *
   * 받지 못하면 시작하지 않습니다 — 그냥 시작은 옆의 단추가 그대로 합니다.
   */
  async startRanked(): Promise<void> {
    const seed = await this.hub.requestRanked({
      deck: 'red_deck',
      stake: 'White',
      // 순위표의 축입니다. 새로 여는 판은 전부 이 풀입니다 — `core/pool.ts`.
      pool: 'base',
    })
    if (seed === undefined) return

    this.challengeId = ''
    this.cross('title_run', () => {
      this.layRun(seed)
      this.enterRun()
    })
    this.game.input.toasts.push(t('ui.lb.ranked'), t('ui.lb.ranked.on'), UI.good, 2.6)
  }

  /**
   * 부팅이 끝나고 처음 서는 자리.
   *
   * **로그인하지 않았으면 로그인 화면입니다.** 실행할 때마다 그렇습니다 — 계정 없이
   * 하기로 한 것은 그 실행에만 적용되고, 로그아웃한 뒤나 처음 켠 자리도 여기입니다.
   *
   * **도구는 `?guest=1` 로 건너뜁니다.** 화면을 눌러 판을 두는 도구 50여 개가 저마다 이
   * 화면을 지나야 할 이유가 없습니다.
   */
  openingScene(): void {
    this.syncAccount()
    if (guestBoot()) account.playAsGuest()
    if (account.needsLogin()) this.enterLogin()
    else this.enterTitle()
    // **덮을 앞 화면이 없으므로 걷기만 합니다.** 로딩은 DOM 한 줄이고 무대 밖입니다 —
    // 그 줄이 걷히는 자리에서 첫 화면이 덮개 밑에서 드러납니다.
    this.game.transition.open('boot_first', transitionWanted(this.settings)
      ? this.game.crossings.of('boot_first') : this.game.crossings.quiet)
  }

  /**
   * 씬을 갈아 끼웁니다. **그 사이를 덮습니다.**
   *
   * 넘긴 것이 실제로 씬을 바꾸는 일이고, 그것이 불리는 자리는 화면이 완전히 덮인
   * 프레임입니다 — `enterRun` 은 뷰 수십 개를 만들고 앞면을 굽느라 한 프레임을 넘기므로,
   * 덮지 않으면 그 프레임이 멈춘 화면으로 보입니다.
   *
   * **줄여 두었으면 짧은 덮개입니다.** 0이 아닙니다 — 갈아 끼우는 프레임은 어느 설정에서도
   * 보이면 안 됩니다.
   */
  cross(id: TransitionId, swap: () => void): void {
    this.game.transition.play(
      id, transitionWanted(this.settings) ? this.game.crossings.of(id)
        : this.game.crossings.quiet, swap)
  }

  /**
   * 앞 화면을 그림 한 장으로 굽습니다.
   *
   * **타서 사라지는 전환 하나만 씁니다.** 그것도 덮기가 시작되는 그 프레임에 한 번뿐이고,
   * 걷는 자리에서 버립니다.
   *
   * **전환 층은 이 통 밖입니다.** 그래서 사진에 자기 자신이 찍히지 않습니다.
   */
  shoot(): Texture | undefined {
    const crop = this.game.cropRect
    if (!crop) return undefined
    try {
      // **그림 한 장을 손으로 만듭니다.** `extract.texture` 가 만드는 것과 같은 것이지만,
      // 그리기 전에 스텐실을 붙일 자리가 그 안에는 없습니다.
      const texture = RenderTexture.create({
        width: crop.width,
        height: crop.height,
        // **화면 배율보다 촘촘하게 굽지 않습니다.** 한 장이 그대로 메모리이고, 이 그림은
        // 타는 동안에만 있습니다.
        resolution: Math.min(2, this.game.app.renderer.resolution ?? 1),
        // **다중 표본을 쓰지 않습니다.** 아래에서 붙이는 스텐실은 표본 하나짜리이고, 표본
        // 수가 다른 것을 한 틀에 붙이면 그 틀이 성립하지 않습니다. 이 그림은 재로 삭는
        // 동안에만 있으므로 가장자리의 계단은 알갱이와 연기에 묻힙니다.
        antialias: false,
      })
      // **그리기 전에 스텐실을 붙입니다.** 이유는 이렇습니다.
      //
      // 마스크를 쓰는 것(상점 딱지의 컷아웃 · 조커 아트의 클립 · 태그와 보스의 원형)은
      // 스텐실 버퍼로 잘립니다. 화면에는 그 버퍼가 처음부터 있고 프레임마다 지워지지만,
      // **구울 그림에는 없습니다** — Pixi 는 그림을 색 텍스처 하나로만 만들고, 마스크가
      // 처음 쓰이는 순간에 스텐실을 붙입니다(`ensureDepthStencil`). 그 자리에서 패스를
      // 다시 여는데 **지우지 않고** 열므로, 갓 붙은 스텐실의 값은 정해져 있지 않습니다.
      //
      // 그 값이 0으로 오는 기계에서는 마스크가 맞고, 그렇지 않은 기계에서는 마스크가 통째로
      // 어긋나 **그 그림이 아예 그려지지 않습니다.** 구운 그림의 그 자리는 알파가 0이고,
      // 지우는 셰이더는 알파 0을 「없는 자리」로 읽어 남는 색으로 칠합니다(`ash.ts` 의
      // `plain`) — 카드가 검은 구멍으로 남던 것이 이것입니다.
      //
      // 여기서 먼저 붙이면 패스를 열 때의 지움이 스텐실도 함께 지우므로, 첫 마스크부터
      // 값이 0입니다. **화면 한 장을 두 번 그려 뒤엣것만 쓰던 것을 이 한 줄이 대신합니다.**
      this.game.app.renderer.renderTarget.getRenderTarget(texture).ensureDepthStencilTexture()
      this.game.app.renderer.render({
        container: this.game.screen,
        // `generateTexture` 가 쓰는 것과 같은 옮김입니다 — 잘라 낸 자리를 원점으로.
        transform: new Matrix().translate(-crop.x, -crop.y),
        target: texture,
        // **투명으로 지웁니다.** 넘기지 않으면 렌더러의 배경색으로 지워지고, 그것은 판
        // 밖에 보이는 색입니다.
        clearColor: [0, 0, 0, 0],
      })
      return texture
    } catch {
      // 굽지 못하면 남는 색만 보입니다. 화면이 갈리는 것 자체는 그대로 됩니다.
      return undefined
    }
  }

  /** 계정 상태를 화면에 알립니다. 로그인·로그아웃·이름 바꾸기 뒤에 부릅니다. */
  syncAccount(): void {
    this.title.setAccount(this.hub.signedIn)
  }

  /**
   * 물어보는 판 하나를 엽니다.
   *
   * **여는 자리가 하나입니다.** 다섯 곳이 저마다 판을 만들어 열고 있었고, 그러면 도구가
   * 짚을 자리를 알리는 코드도 다섯 곳이 됩니다 — 지금 떠 있는 물음이 무엇인지도 여기서만
   * 압니다.
   */
  private ask(title: string, body: string, yes: string, danger: boolean,
              onYes: () => void, notes: readonly string[] = []): void {
    const panel = new ConfirmPanel(title, body, yes, danger, onYes,
                                   () => this.game.panels.modals.close(panel), notes)
    this.game.panels.confirmUp = panel
    this.game.panels.modals.open(panel)
  }

  /** 계정 칩을 눌렀습니다. */
  openAccount(): void {
    if (this.hub.signedIn) this.hub.openProfile()
    else this.cross('title_login', () => this.enterLogin())
  }

  /**
   * 로그아웃합니다.
   *
   * **묻고 나서 합니다.** 한 번 눌러서 일어나면 잘못 누른 사람에게는 사고입니다.
   *
   * **끝나면 로그인 화면입니다.** 로그아웃은 「계정을 쓰지 않겠다」가 아니라 「이 계정에서
   * 나가겠다」이므로, 다음에 무엇으로 할지를 다시 정하는 자리로 갑니다 — 다시 켰을 때도
   * 로그인 화면인 것이 그 때문입니다.
   */
  signOut(): void {
    this.ask(t('ui.account.signOutAsk'), t('ui.account.signOutBody'),
             t('ui.button.logout'), false, () => void this.doSignOut())
  }

  /**
   * 타이틀로 가도 되는지 묻습니다.
   *
   * **런은 적혀 있습니다.** 나가면 사라지던 것이 이제 저장되고 타이틀의 「이어하기」로
   * 돌아옵니다 — 그래도 묻는 것은 판을 접는 것이 그 자리에서 되돌아오는 일이 아니기
   * 때문이고, 무엇이 일어나는지는 묻는 글이 적습니다.
   */
  askLeaveRun(): void {
    this.ask(t('ui.title.leaveAsk'), t('ui.title.leaveBody'), t('ui.button.toTitle'),
             false, () => this.cross('run_title', () => this.enterTitle()))
  }

  private async doSignOut(): Promise<void> {
    await this.hub.signOut()
    // **손님 표시도 걷습니다.** 로그아웃한 다음 화면은 로그인 화면입니다.
    account.leaveGuest()
    this.syncAccount()
    this.cross('title_login', () => this.enterLogin())
  }

  /** 로그인 화면으로. **나가는 길은 「계정 없이 시작하기」 하나입니다.** */
  enterLogin(): void {
    this.scene = 'login'
    this.game.show.syncBackdrop()
    keepAwake(false)
    // **알림이 서는 자리가 씬마다 다릅니다.** 판 안에서는 낸 카드를 덮지 않으려고
    // 오른쪽에 붙지만, 카드가 없는 화면에서는 그냥 구석에 붙은 것이 됩니다.
    this.game.input.toasts.setCenter(Toasts.OUT_RUN)
    this.login.visible = true
    this.title.visible = false
    this.game.board.visible = false
    this.game.overlay.visible = false
  }

  /**
   * 판으로 들어갑니다. **타이틀에서만 갑니다.**
   */
  enterRun(): void {
    if (this.scene === 'run') return
    this.scene = 'run'
    this.game.show.syncBackdrop()
    this.game.input.toasts.setCenter(Toasts.IN_RUN)
    this.login.visible = false
    this.title.visible = false
    this.game.board.visible = true
    this.game.overlay.visible = true
    this.game.audio.unlock()
    // **판이 도는 동안만 화면을 켜 둡니다.** 낼 카드를 고르는 사이는 손이 화면에 닿지
    // 않는 시간이고, 이 게임에는 그 시간을 재는 것이 없습니다.
    keepAwake(true)
    // **환희의 첫 영상을 미리 읽습니다.** 문턱을 넘는 순간에 읽기 시작하면 그 판의
    // 앞부분이 셰이더로 지나갑니다. 타이틀에서는 읽지 않습니다 — 판을 열지 않는 사람에게
    // 3MB 를 읽힐 이유가 없습니다.
    this.game.show.euphoria.warm()
    this.applyOptions()
    this.game.payout.settleShown()
    this.game.refresh()
    // **처음 값은 굴러가지 않습니다.** 판에 들어서는 순간의 금액은 「바뀐 것」이 아니라
    // 「원래 그런 것」이고, 0에서 세어 올라가면 무언가를 벌어들인 것으로 보입니다.
    //
    // **화면이 주장하는 것에서 시작합니다.** 이어서 하는 판은 점수가 이미 쌓여 있고,
    // 0을 적어 두면 들어서는 순간 그 점수까지 세어 올라갑니다.
    this.game.chrome.money.reset(this.game.shown.money)
    this.game.chrome.score.reset(this.game.shown.score)
    this.game.chrome.chips.reset(0)
    this.game.chrome.mult.reset(0)

    // 들어선 판을 적어 둡니다. **첫 액션을 기다리지 않습니다** — 기다리면 새 판을 열고
    // 아무것도 두지 않은 채로 껐을 때 지난 판이 이어하기에 남습니다.
    this.rememberRun()
    // 덱과 스테이크와 이 안테의 블라인드는 들어서는 그 자리에서 보입니다.
    //
    // **판을 깔 때가 아니라 들어설 때입니다.** 상태는 타이틀에 머무는 동안에도 하나
    // 있고 시드를 바꿀 때마다 새로 깔립니다 — 그것을 적으면 아무 판도 열지 않은 사람의
    // 도감에 덱과 보스와 태그가 앞면으로 남게 됩니다.
    this.game.note()

    try {
      if (localStorage.getItem('clover.guide.seen') === null) {
        this.game.panels.modals.open(this.game.panels.guide)
        localStorage.setItem('clover.guide.seen', '1')
      }
    } catch {
      // 저장소가 막힌 브라우저에서는 그냥 열지 않습니다.
    }
  }

  /**
   * 타이틀로 돌아갑니다.
   *
   * **페이지를 다시 읽지 않습니다.** 다시 읽으면 데이터·글꼴·그림을 처음부터 읽으므로
   * 로딩 씬이 한 번 더 보이는데, 판을 접는 것과 데이터를 읽는 것은 아무 관계가 없습니다 —
   * 접는 것은 `dropRun` 이 하고, 읽어 둔 것은 그대로 둡니다.
   */
  enterTitle(): void {
    // **판을 접기 전에 적어 둡니다.** 적는 것을 묶어 두었으므로 밀려 있는 것이 있을 수
    // 있고, 씬이 바뀌고 나면 적을 자리가 아닙니다.
    this.flushRun(true)
    this.dropRun()
    this.scene = 'title'
    this.game.show.syncBackdrop()
    keepAwake(false)
    this.game.input.toasts.setCenter(Toasts.OUT_RUN)
    this.login.visible = false
    this.title.visible = true
    this.game.board.visible = false
    this.game.overlay.visible = false

    // **챌린지는 타이틀로 돌아갈 때 놓습니다.** 들고 있으면 타이틀의 시작 단추가 조용히
    // 챌린지를 여는 것이 됩니다.
    this.challengeId = ''

    // 새 판을 새 시드로 미리 깔아 둡니다. 타이틀에서 옵션을 열면 이 시드가 적혀 있고,
    // 시작을 누르면 이 판이 펼쳐집니다.
    //
    // **처음 한 번은 주소에 적혀 온 시드를 그대로 씁니다.** 부팅도 이 길을 지나므로 새
    // 시드를 여기서 만들면 `?seed=` 로 연 판이 타이틀에 서는 그 순간 버려졌습니다 —
    // 주소는 새 시드로 다시 적히고, 그래서 **그 주소를 남에게 보내도 다른 판이 열렸습니다.**
    const boot = this.bootSeed
    this.bootSeed = undefined
    this.useSeed(boot ?? randomSeed())
  }

  /**
   * 판을 새로 시작합니다.
   *
   * **접고 나서 폅니다.** 끝난 판의 카드 한 장이 남아 있으면 그것이 새 판에 섞입니다 —
   * 접는 길은 타이틀로 가는 것과 같은 길이고, 다른 것은 곧바로 다시 편다는 것뿐입니다.
   */
  private restartRun(): void {
    this.cross('run_restart', () => {
      this.enterTitle()
      this.enterRun()
    })
  }

  /**
   * 판에 딸린 것을 전부 걷습니다.
   *
   * **상태를 새로 만드는 것으로는 모자랍니다.** 카드 뷰·조커 뷰·상점 딱지·날고 있는 칩은
   * 저마다 자기 목록에 있고, 상태를 갈아 끼워도 그 자리에 그대로 남습니다 — 여기서 빠뜨린
   * 것 하나가 곧 타이틀 위에 떠 있는 카드 한 장입니다.
   */
  private dropRun(): void {
    // 남은 박자를 버립니다. **`finish` 가 아닙니다** — 보여 줄 판이 이미 없습니다.
    this.game.player.drop()
    this.game.panels.modals.closeAll()
    this.game.input.tooltip.hide()

    // **판 위로 나와 있던 카드를 먼저 걷습니다.** 손패에서 빌려 온 것이 있으므로, 손패를
    // 지우기 전에 돌려주지 않으면 이미 지워진 뷰를 붙들고 있게 됩니다.
    this.game.show.beatLog.length = 0
    this.game.cards.borrowLink.clear()
    this.game.cards.borrowLink.visible = false
    this.game.cards.endCardShow()
    this.game.cards.borrowed.clear()
    this.game.cards.pendingCards.clear()
    this.game.cards.pendingJokers.clear()
    this.game.cards.castSoon.clear()
    this.game.show.ruleBanner.visible = false

    // 카드와 조커. 뷰는 `board` 의 자식이라 지워야 사라집니다.
    for (const view of this.game.cards.views.values()) view.destroy()
    this.game.cards.views.clear()
    for (const view of this.game.cards.playedViews) view.destroy()
    this.game.cards.playedViews.length = 0
    for (const view of this.game.cards.jokers.values()) view.destroy()
    this.game.cards.jokers.clear()
    for (const view of this.game.cards.burning) view.destroy()
    this.game.cards.burning.length = 0
    this.game.cards.slams.length = 0
    this.game.cards.fades.length = 0
    this.game.cards.deals.length = 0
    // 돌아오는 중이던 카드들. **판을 접으면 갈 곳이 없습니다** — 덱째로 사라지므로,
    // 남겨 두면 타이틀 화면 오른쪽에 뒷면 몇 장이 떠 있습니다.
    for (const one of this.game.cards.recalls) one.node.destroy()
    this.game.cards.recalls.length = 0
    this.game.cards.retired = 0
    this.game.cards.fadeUntil = 0
    // 떠오르던 차이 글. **글은 두고 상태만 되돌립니다** — 풀이므로 다시 쓰입니다.
    for (const one of this.game.show.deltas) one.node.visible = false
    this.game.chrome.panelShown = { hands: -1, discards: -1, ante: -1 }
    this.game.blind.tagFlashId = ''
    this.game.blind.tagFlashLife = 1
    this.game.blind.tagSpent = []
    this.game.blind.tagSpentAnte = 0
    this.game.blind.tagFire.clear()

    // 판 위에 그려 둔 겹들. 매번 다시 그리는 것들이므로 비우면 됩니다.
    for (const layer of [this.game.shop.shopLayer, this.game.pack.packLayer,
      this.game.tray.consumableLayer, this.game.tray.tagLayer,
                         this.game.tray.activeLayer, this.game.blind.blindPick, this.gameOver,
                         this.game.chrome.heldBar,
                         this.game.input.hint, this.game.payout.panel.view,
                         this.game.panels.activePanel.view, this.game.cards.deckView.view,
                         this.game.panels.handList.view, this.game.panels.menu.view]) {
      layer.removeChildren().forEach(child => child.destroy())
    }
    this.gameOver.visible = false
    delete this.game.spots.again
    delete this.game.spots.home

    // 날고 있는 것들.
    this.game.show.particles.clear()
    this.game.payout.coins.clear()
    this.game.input.toasts.clear()

    // 고른 것 · 끄는 것 · 가리키는 것.
    this.game.cards.selected.clear()
    this.game.cards.hinted.clear()
    this.game.tray.held = undefined
    this.game.input.drag = undefined
    this.game.input.tipUnder = undefined
    this.game.input.tipOpen = -1
    this.game.panels.handHovered = -1
    this.game.panels.handBand = undefined
    this.game.panels.handPreview = undefined
    this.game.panels.handRows.length = 0

    // 상점과 팩.
    this.game.pack.packViews.clear()
    this.game.pack.packGone.length = 0
    this.game.pack.packShown = ''
    this.game.pack.packEnter = 0
    this.game.pack.packPending = false
    this.game.tray.focus = undefined
    this.game.tray.focusEnter = 0
    this.game.shop.shopHoldUntil = 0
    this.game.shop.shopStayUntil = 0
    this.game.cards.sortSlide.snap(SORT_HIDE)
    this.game.chrome.countPulse = undefined
    this.game.chrome.countScale.snap(1)
    this.game.panels.activeGlow = undefined
    this.game.shop.rerolled = false
    this.game.tray.focusLayer.removeChildren().forEach(child => child.destroy())
    this.game.cards.deckPeekUntil = 0
    this.game.cards.deckFlight?.node.destroy({ children: true })
    this.game.cards.deckFlight = undefined
    this.game.chrome.deckBump.snap(0)
    this.game.pack.packNote = undefined
    this.game.pack.packSkip = undefined
    this.game.pack.packTitle = undefined
    this.game.shop.reveals.length = 0
    this.game.later.length = 0
    this.game.show.chimes.length = 0
    this.game.show.notes.length = 0
    this.game.tray.arriveFrom = undefined
    this.game.tray.sellFrom = undefined
    this.game.shop.boughtFrom = undefined
    this.game.chrome.moneyFrom = undefined
    this.game.shop.shopBox = undefined
    this.game.shop.shopRows = {}
    this.game.shop.shopFrame = undefined
    this.game.shop.shopFoot = undefined
    this.game.shop.voucherTile = undefined
    this.game.shop.shopSlide.snap(0)
    this.game.shop.shopLayer.y = 0
    delete this.game.spots.take
    this.game.shop.shopRevealAt = 0
    this.game.shop.shopStanding = false
    this.game.shop.shopOpening = false

    // 소모품.
    this.game.tray.itemArrive = undefined
    this.game.tray.arriveHold = undefined
    for (const one of this.game.cards.leavingTiles) one.node.destroy()
    this.game.cards.leavingTiles.length = 0
    this.game.tray.usedItem = undefined
    this.game.tray.consumableLift.clear()
    this.game.tray.consumableSlide.clear()
    this.game.tray.consumableGrow.clear()
    this.game.tray.hoveredItem = undefined
    this.game.tray.consumableTiles.length = 0
    this.game.cards.burningItems.length = 0

    // 정산.
    this.game.payout.payoutRows.length = 0
    this.game.payout.payoutNodes.length = 0
    this.game.payout.payoutWait = undefined
    this.game.payout.payoutWanted = false
    this.game.payout.payoutOpen = false
    this.game.payout.payoutTaking = false
    this.game.payout.sweptAt = -1

    // 세고 있던 것들.
    this.game.cards.playLanded = 0
    this.game.cards.dealtUntil = 0
    this.game.cards.flipAt.clear()
    this.game.cards.deckHold = 0
    this.game.blind.blindEnter = 0
    this.game.blind.blindShown = -1
    this.game.blind.skipping = false
    this.game.blind.skipFrom = undefined
    this.game.blind.tagLanded = undefined
    this.game.blind.tagFly?.node.destroy()
    this.game.blind.tagFly = undefined
    this.game.show.headlineLife = 0
    this.game.show.ratchet = 0
    this.game.show.build = 0
    this.game.show.chain = 0
    this.game.show.rung = 0
    this.game.holdAfterScore = 0
    this.game.payout.settleOwed = false
    this.game.input.hintShown = ''

    // 번쩍임과 흔들림. **남겨 두면 타이틀이 흔들린 채로 뜹니다.** 환희의 겹도 같습니다 —
    // 판을 접은 뒤에도 남아 있으면 타이틀에서 기를 모으고 있게 됩니다.
    this.game.show.euphoria.reset()
    this.game.chrome.scoreSettled = false
    // **읽어 둔 영상도 놓습니다.** 타이틀과 도감에는 나올 자리가 없고, 도감이 그림을
    // 가장 많이 올리는 화면입니다.
    this.game.show.euphoria.forget()
    this.game.show.shake = 0
    this.game.show.freeze = 0
    this.game.show.panelGlow = 0
    this.game.show.screenGlow = 0
    this.gameOverShown = false
    this.gameOverPop = 0
    this.gameOverBoard = undefined
    this.gameOverAgain = undefined
    this.gameOverHome = undefined
  }

  // ---------------------------------------------------------------- 앞뒤

  /**
   * 앞으로 나왔는가 · 뒤로 물러났는가.
   *
   * **물러난 동안에는 아무것도 하지 않습니다.** 그리는 것 · 소리 · 영상 셋이 저마다
   * 스스로 도는 것이라 하나만 멈추면 나머지가 남습니다 — 화면이 없는 동안의 그 셋이
   * 그대로 배터리입니다.
   *
   * **검증 도구의 수동 틱에서는 티커를 만지지 않습니다.** 거기서는 시간이 도구의 부름으로만
   * 흐르므로, 티커를 세우고 다시 세우면 그 판이 두 번 흐릅니다.
   */
  setAwake(active: boolean): void {
    if (this.awake === active) return
    this.awake = active

    if (active) {
      if (!this.game.manualTick) this.game.app.ticker.start()
      this.game.audio.wake()
      this.game.show.euphoria.wake()
      // **물러나면 기계가 스스로 놓습니다.** 돌아온 자리에서 다시 잡습니다.
      keepAwake(this.scene === 'run')
      return
    }
    if (!this.game.manualTick) this.game.app.ticker.stop()
    this.game.audio.hold()
    this.game.show.euphoria.hold()
    keepAwake(false)
    // **물러나기 전에 적어 둡니다.** 물러난 앱은 기계가 언제든 끝낼 수 있고, 그때
    // 밀려 있던 것은 사라집니다.
    this.flushRun(true)
  }

  /** 지금 앞에 나와 있는가. */
  awake = true

  /**
   * 끝났을 때 덮는 판.
   *
   * **지고 나서 아무것도 없는 것이 가장 나쁩니다.** 어디까지 갔는지 보여주고 다시 시작할
   * 자리를 둡니다.
   */
  drawGameOver(): void {
    const done = this.game.state.phase === 'lost' || this.game.state.phase === 'won'
    if (!done) {
      this.gameOver.removeChildren().forEach(child => child.destroy())
      this.game.cards.gameOverJokers.length = 0
      this.gameOver.visible = false
      this.gameOverShown = false
      this.overBar = undefined
      delete this.game.spots.again
      delete this.game.spots.home
      return
    }

    // **연출이 끝나기 전에는 띄우지 않습니다.** 마지막 카드를 낸 결과를 보기도 전에 판이
    // 덮이면 무엇 때문에 끝난 것인지 알 수 없습니다. `tick` 이 조건을 보고 부릅니다.
    if (this.gameOverShown) return
    this.gameOverShown = true
    this.gameOverPop = 1

    const won = this.game.state.phase === 'won'
    this.gameOver.removeChildren().forEach(child => child.destroy())
    this.game.cards.gameOverJokers.length = 0
    this.gameOver.visible = true

    // 판 하나가 뜨는 것과 같은 정도로 덮습니다.
    const veil = new Graphics()
    veil.rect(-2000, -2000, SIZE.width + 4000, SIZE.height + 4000)
      .fill({ color: UI.scrim, alpha: 0.66 })
    this.gameOver.addChild(veil)

    const board = new Container()
    const width = 520
    const pad = 24
    const inner = width - pad * 2
    const state = this.game.state
    const ranked = this.hub.isRanked(state.seed)
    const tone = won ? UI.green : UI.red

    // 위에서부터 — 머리 · 어디서(바) · 이번 런(칸 넷) · 조커(카드 한 줄) · [순위] · 밑단.
    // **끝난 런을 돌아보는 판입니다.** 「패배」 와 수 넷으로 끝내면 무엇으로 싸웠는지가 남지
    // 않습니다.
    const headH = 56
    const barBlock = SECTION_H + 46 + 22
    const statBlock = SECTION_H + 10 + 40 * 2 + 8
    const jokerBlock = SECTION_H + 10 + 84
    const rankBlock = ranked ? SECTION_H + 40 + 14 : 0
    const height = headH + 14 + barBlock + 6 + statBlock + 14 + jokerBlock + rankBlock
      + 16 + 1 + 14 + 40 + 20
    const top = -height / 2
    const left = -width / 2 + pad

    const plate = new Graphics()
    plate.roundRect(-width / 2, top, width, height, 8).fill({ color: UI.panel,
      alpha: UI.panelAlpha })
    plate.roundRect(-width / 2 + 0.75, top + 0.75, width - 1.5, height - 1.5, 8)
      .stroke({ color: UI.panelEdge, width: 1.5 })
    plate.rect(-width / 2 + 1.5, top + headH, width - 3, 1.5).fill(UI.rule)
    board.addChild(plate)

    // 결과 한 낱말. **색은 여기와 바에만 듭니다** — 판 전체를 붉게 물들이지 않습니다.
    const title = new Text({
      text: won ? t('ui.label.won') : t('ui.label.lost'),
      style: { fontSize: TEXT.head, fill: tone, fontWeight: WEIGHT.heavy, letterSpacing: 4 },
    })
    title.anchor.set(0.5)
    title.position.set(0, top + headH / 2)
    board.addChild(title)

    let yy = top + headH + 14

    // 어디서 · 얼마나. 득점 / 요구 바 하나와 한 줄.
    const where = won
      ? tf('ui.over.where', { ante: this.game.data.run.winAnte, blind: blindName(state.blind) })
      : tf('ui.over.where', { ante: state.ante, blind: blindName(state.blind) })
    const whereHead = sectionHead(inner, where)
    whereHead.position.set(left, yy)
    yy += SECTION_H
    const score = Number(state.score)
    const target = Number(state.target)
    const barY = yy + 23
    const scored = new Text({
      text: `${t('ui.stat.score')}  ${score.toLocaleString('en-US')}`,
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    scored.anchor.set(0, 0.5)
    scored.position.set(left, barY)
    const wanted = new Text({
      text: `${t('ui.label.target')}  ${target.toLocaleString('en-US')}`,
      style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    wanted.anchor.set(1, 0.5)
    wanted.position.set(left + inner, barY)
    const bar = new ProgressBar(220, 8, won ? UI.green : UI.bar)
    bar.position.set(-110, barY - 4)
    this.overBar = { bar, begin: this.game.clock + 0.3, ratio: target > 0 ? Math.min(1,
      score / target) : 1 }
    yy += 46
    const lead = new Text({
      text: this.endLine(won),
      style: {
        fontSize: TEXT.body, fill: tone, fontWeight: WEIGHT.normal,
        wordWrap: true, wordWrapWidth: inner, breakWords: true, align: 'center',
      },
    })
    lead.anchor.set(0.5, 0.5)
    lead.position.set(0, yy + 8)
    yy += 22 + 6
    board.addChild(whereHead, scored, wanted, bar, lead)

    // 이번 런. 안테 · 낸 핸드 · 최고 핸드 · 소지금.
    const runHead = sectionHead(inner, t('ui.over.run'))
    runHead.position.set(left, yy)
    board.addChild(runHead)
    yy += SECTION_H + 10
    const cellW = (inner - 8) / 2
    const cells: [string, string, number][] = [
      [t('ui.slot.ante'), `${state.ante} / ${this.game.data.run.winAnte}`, UI.ink],
      [tf('ui.stat.hands_played', { n: '' }).trim(), `${state.handsPlayedThisRun}`, UI.ink],
      [t('ui.over.best_hand'), this.metrics.bestHand.toLocaleString('en-US'), UI.bar],
      [t('ui.over.money'), `$${state.money}`, UI.yellow],
    ]
    cells.forEach(([label, value, ink], index) => {
      const cell = valueCell(cellW, 40, label, value, ink)
      cell.position.set(left + (index % 2) * (cellW + 8), yy + Math.floor(index / 2) * 48)
      board.addChild(cell)
    })
    yy += 40 * 2 + 8 + 14

    // 조커. **들고 끝난 것을 카드로.** 빈 자리는 빈 칸으로 남아 몇을 채웠는지가 보입니다.
    const slots = Math.max(state.jokers.length, state.rules.jokerSlots)
    const jokerHead = sectionHead(inner, t('ui.button.jokers'),
      `${state.jokers.length} / ${state.rules.jokerSlots}`)
    jokerHead.position.set(left, yy)
    board.addChild(jokerHead)
    yy += SECTION_H + 10
    const small = 60 / SIZE.jokerWidth
    for (let i = 0; i < slots; i++) {
      const jx = left + i * 70
      const joker = state.jokers[i]
      if (!joker) {
        const empty = cellPlate(60, 84, UI.hairline, true)
        empty.position.set(jx, yy)
        board.addChild(empty)
        continue
      }
      const view = new JokerView(joker, this.gameOverLook(joker))
      view.pivot.set(0, 0)
      view.position.set(jx, yy)
      view.scale.set(small)
      board.addChild(view)
      this.game.cards.gameOverJokers.push({ view, joker })
    }
    yy += 84 + 14

    // 순위. **판정이 온 뒤에 적힙니다.** 랭크 런이 아니면 이 구획이 없습니다.
    if (ranked) {
      const rankHead = sectionHead(inner, t('ui.button.leaderboard'))
      rankHead.position.set(left, yy)
      board.addChild(rankHead)
      this.rankNode = new Container()
      this.rankNode.position.set(0, yy + SECTION_H + 20)
      board.addChild(this.rankNode)
      yy += rankBlock
    }

    // 밑단. 시드와 복사, 단추 둘.
    const foot = hairline(inner)
    foot.position.set(left, yy + 16)
    board.addChild(foot)
    yy += 16 + 1 + 14
    const seedLabel = new Text({
      text: t('ui.title.seed'),
      style: { fontSize: TEXT.mini, fill: UI.inkDim, fontWeight: WEIGHT.normal },
    })
    seedLabel.anchor.set(0, 0.5)
    seedLabel.position.set(left, yy + 20)
    const seed = new Text({
      text: state.seed,
      style: { fontSize: TEXT.body, fill: UI.mark, fontWeight: WEIGHT.normal,
        fontFamily: NUMERALS, letterSpacing: 1 },
    })
    seed.anchor.set(0, 0.5)
    seed.position.set(left + seedLabel.width + 8, yy + 20)
    // **시드는 다시 돌리려고 적는 것입니다.** 손으로 옮겨 적게 두지 않습니다.
    const copy = new Button(t('ui.over.copy'), 52, 24, 'quiet', () => {
      const clip = globalThis.navigator?.clipboard
      if (!clip) return
      void clip.writeText(state.seed).then(() => { copy.text = t('ui.over.copied') })
    })
    copy.position.set(seed.x + seed.width + 8, yy + 8)
    board.addChild(seedLabel, seed, copy)

    // **둘 다 페이지를 다시 읽지 않습니다.** 판을 접는 것은 화면이 하는 일입니다.
    const again = new Button(t('ui.button.restart'), 140, 40, 'primary', () => this.restartRun())
    again.position.set(width / 2 - pad - 140, yy)
    const home = new Button(t('ui.button.to_title'), 96, 40, 'neutral',
      () => this.cross(won ? 'run_won' : 'run_lost', () => this.enterTitle()))
    home.position.set(again.x - 8 - 96, yy)
    board.addChild(home, again)

    this.gameOverX = popupCenter(width)
    this.gameOverY = PANEL_BOTTOM - height / 2
    // **아래에서 시작합니다.** 제자리에 놓고 다음 프레임에 내리면 그 한 프레임 동안 판이
    // 다 선 자리에 있습니다 — 상점 판에서 같은 것이 한 번 튀는 것으로 보였습니다.
    board.position.set(this.gameOverX, this.gameOverY + 58)
    this.gameOver.addChild(board)
    this.gameOverBoard = board
    // **단추 둘의 자리는 다 선 뒤에 알립니다.** 여기서 세면 판이 아직 58픽셀 아래에
    // 있으므로 그만큼 낮은 자리가 발행되고, 그 자리는 화면 밖입니다 — 도구는 아무것도
    // 맞히지 못한 채로 눌렀다고 넘어갔습니다. `advanceGameOver` 가 잦아든 자리에서 셉니다.
    this.gameOverAgain = again
    this.gameOverHome = home
    delete this.game.spots.again
    delete this.game.spots.home

    if (ranked) void this.judgeRun()
    this.gameOver.zIndex = 10_000

    // 럼블. **판이 그냥 나타나면 아무 무게가 없습니다.**
    this.game.audio.play(won ? 'run_win' : 'run_lose')
    this.game.audio.music.duck(0.6, 1.6)
    this.game.show.haptics.play(won ? 'win' : 'lose')
    this.game.show.jolt(won ? 8 : 6, won ? 3.4 : 2.6, 1)
    this.game.show.flashScreen(won ? UI.money : UI.bad, won ? 0.5 : 0.34)
    if (won) this.game.show.particles.bills(POPUP_X, SIZE.height / 2, 44, UI.money, 1.3, 1.1)
  }

  /** 진 판의 판에 선 조커 하나의 모습. 세울 때와 다시 그릴 때가 같아야 합니다. */
  private gameOverLook(joker: JokerInstance): JokerLook {
    const row = this.game.data.tables.joker.findByJokerId(joker.jokerId)
    return {
      name: nameOf(this.game.data, 'joker', joker.jokerId, row?.name ?? joker.jokerId),
      rarity: row?.rarity ?? 1,
      lines: describe(this.game.data, this.game.data.jokerEffects.get(joker.jokerId) ?? []),
      edition: this.game.cards.editionLook(joker.edition as EditionKind),
    }
  }

  /**
   * 진 판의 판에 선 조커들을 다시 그립니다.
   *
   * **그림이 오갈 때 이 판만 남습니다.** 줄과 상점과 팩은 `refresh` 가 다시 세우지만 이
   * 판은 한 번 세우고 다시 세우지 않으므로(`gameOverShown`), 놓인 그림을 가리킨 채로
   * 남습니다 — 그림이 실제로 버려지는 두 틱 뒤에 그 프레임이 예외로 죽고, 예외는 조용히
   * 삼켜지므로 **카드가 갑자기 사라진 것처럼 보입니다.**
   *
   * 다시 그리면 그림을 다시 부탁하므로 차례도 최근이 됩니다 — 화면에 놓여 있는 동안에는
   * 상한에 걸려 놓이지 않습니다.
   */
  repaintGameOver(): void {
    for (const one of this.game.cards.gameOverJokers) {
      if (one.view.destroyed) continue
      one.view.set(one.joker, this.gameOverLook(one.joker))
    }
  }

  /** 게임오버 판의 득점 바를 한 단계 진행합니다. 0.6초에 걸쳐 득점까지 찹니다. */
  private advanceOverBar(): void {
    const one = this.overBar
    if (!one || one.bar.destroyed) return
    const step = Math.max(0, Math.min(1, (this.game.clock - one.begin) / 0.6))
    one.bar.set(one.ratio * (1 - (1 - step) * (1 - step)))
  }

  /**
   * 끝난 런을 올리고 그 결과를 판에 적습니다.
   *
   * **랭크 런이 아니면 아무것도 하지 않습니다.**
   */
  private async judgeRun(): Promise<void> {
    if (!this.hub.isRanked(this.game.state.seed)) return

    this.rankLine = { text: t('ui.lb.end.judging'), tone: UI.inkDim }
    this.drawRankLine()

    const line = await this.hub.finishRun(this.game.state, this.actions, this.metrics)
    this.rankLine = line
    this.rankRoll = line?.from ?? line?.to ?? 0
    this.drawRankLine()
    if (!line) return

    // **순위가 오른 것은 연출이 있어야 무게가 있습니다.** 그냥 적혀 있으면 아무 일도
    // 아닙니다.
    if (line.moved !== undefined && line.moved > 0) {
      this.game.audio.play('blind_clear')
      this.game.show.jolt(4, 2.2, 1)
      if (line.moved >= 25) {
        this.game.show.particles.bills(POPUP_X, SIZE.height / 2, 22, UI.money, 1.1, 1)
      }
    }
    if (line.tier !== undefined) {
      this.game.audio.play('run_win')
      this.game.show.flashScreen(UI.money, 0.3)
      this.game.input.toasts.push(t('ui.lb.title'), tf('ui.lb.end.tierUp', { tier: line.tier }),
                       UI.money, 3.4)
    }
  }

  /**
   * 순위 숫자를 새 자리까지 굴립니다.
   *
   * **내려가는 동안 소리가 한 음씩 오릅니다.** 득점 연출의 카운터와 같은 규칙이고, 그
   * 규칙이 같아야 이 판의 것으로 읽힙니다.
   */
  rollRank(seconds: number): void {
    const line = this.rankLine
    if (!line || line.to === undefined || line.from === undefined) return
    if (Math.round(this.rankRoll) === line.to) return

    const span = Math.max(1, Math.abs(line.from - line.to))
    const before = Math.round(this.rankRoll)
    // **폭이 넓어도 곧 끝납니다.** 자리마다 같은 속도로 굴리면 100자리가 오른 판이
    // 한참 동안 숫자만 굴립니다.
    this.rankRoll += (line.to - this.rankRoll) * fraction(seconds, 5)
    if (Math.abs(this.rankRoll - line.to) < 0.6) this.rankRoll = line.to

    const now = Math.round(this.rankRoll)
    if (now !== before) {
      // **숫자가 바뀔 때마다 내지 않습니다.** 용수철이 수렴하는 동안 값은 프레임마다
      // 바뀌므로 초당 예순 번이고, 그것은 세는 소리가 아니라 잡음입니다 — 세는 소리로
      // 들리는 간격이 있고 그것이 문턱입니다.
      if (this.game.clock - this.rankTickAt >= RANK_TICK) {
        this.rankTickAt = this.game.clock
        const step = 1 - Math.abs(now - line.to) / span
        this.game.audio.play('coin_land', ladder(Math.floor(step * 6)))
      }
      this.drawRankLine()
    }
  }

  /**
   * 순위 한 줄을 그립니다.
   *
   * **숫자가 굴러 내려갑니다.** 예전 순위에서 새 순위로 내려가는 동안 그 값을 글에
   * 끼워 넣으므로, 그 사이에는 매 프레임 다시 그립니다.
   */
  private drawRankLine(): void {
    const node = this.rankNode
    if (!node) return
    node.removeChildren().forEach(child => child.destroy({ children: true }))

    const line = this.rankLine
    if (!line) return

    const rolling = line.to !== undefined && Math.round(this.rankRoll) !== line.to
    const shown = rolling ? line.text.replace(RANK_MARK, '#' + String(Math.round(this.rankRoll)))
      : line.text

    const text = new Text({
      text: shown,
      style: {
        fontSize: TEXT.copy, fill: line.tone, fontWeight: WEIGHT.normal,
        wordWrap: true, wordWrapWidth: 420, align: 'center',
      },
    })
    text.anchor.set(0.5, 0)
    node.addChild(text)

    // 나머지 보드는 작은 글로. **하나씩 다 연출하면 끝난 판이 30초가 됩니다.**
    const others = this.hub.otherRanks()
    if (others !== '' && line.moved !== undefined) {
      const small = new Text({
        text: others,
        style: {
          fontSize: TEXT.mini, fill: UI.inkDim, wordWrap: true, wordWrapWidth: 420,
          align: 'center',
        },
      })
      small.anchor.set(0.5, 0)
      small.position.set(0, text.height + 4)
      node.addChild(small)
    }
  }

  /** 왜 끝났는가. **숫자가 있어야 다음 판에 무엇을 다르게 할지 압니다.** */
  private endLine(won: boolean): string {
    if (won) return tf('ui.over.won', { n: this.game.data.run.winAnte })
    const short = Number(this.game.state.target) - Number(this.game.state.score)
    const where = tf('ui.over.where', { ante: this.game.state.ante,
      blind: blindName(this.game.state.blind) })
    return short > 0
      ? tf('ui.over.short', { where, n: short.toLocaleString('en-US') })
      : tf('ui.over.stopped', { where })
  }

  /**
   * 끝난 판이 들어오는 동안.
   *
   * **떠 있는 판들과 같은 법으로 아래에서 올라옵니다** — 같은 58픽셀이고 같은 감쇠입니다.
   * 크기가 넘쳤다가 잦아드는 럼블이었는데, 판마다 들어오는 방식이 다르면 이 판만 다른
   * 갈래의 것으로 보입니다. 들어오는 동안 조금 떠는 것은 남겼습니다 — 판이 선 그 순간의
   * 무게이고, 떠 있는 판들도 열릴 때 같은 것을 합니다.
   */
  advanceGameOver(seconds: number): void {
    this.advanceOverBar()
    const board = this.gameOverBoard
    if (!board || this.gameOverPop <= 0) return

    this.gameOverPop -= this.gameOverPop * fraction(seconds, 9)
    if (this.gameOverPop < 0.004) this.gameOverPop = 0
    const shiver = this.gameOverPop * this.gameOverPop * 10

    board.position.set(
      this.gameOverX + (Math.random() - 0.5) * shiver,
      this.gameOverY + this.gameOverPop * 58 + (Math.random() - 0.5) * shiver)
    board.rotation = (Math.random() - 0.5) * shiver * 0.0022

    if (this.gameOverPop <= 0) {
      board.scale.set(1)
      board.position.set(this.gameOverX, this.gameOverY)
      board.rotation = 0
      // **다 선 자리에서 셉니다.** 떨리는 동안의 자리를 발행하면 도구가 그 프레임의
      // 흔들린 자리를 짚습니다.
      if (this.gameOverAgain) this.game.spots.again = this.game.probe.spotOf(this.gameOverAgain,
        70, 20)
      if (this.gameOverHome) this.game.spots.home = this.game.probe.spotOf(this.gameOverHome, 48,
        20)
    }
  }

  /**
   * 자리를 비우며 하나를 내놓습니다.
   *
   * **파는 것과 같은 소리이고 같은 자리입니다.** 단추로 팔 때는 `joker_sell` 이 나는데 내놓을
   * 때는 고르기 소리 뒤에 타는 소리만 났습니다 — 판 값은 내놓은 그 자리에서 나옵니다.
   */
  giveUp(item: ShopItem, held: number): void {
    this.game.audio.play('joker_sell')
    this.game.tray.sellFrom = item.kind === ShopItemKind.Joker ? this.game.tray.jokerSpot(held)
      : this.game.tray.itemSpot(held)
  }
}
