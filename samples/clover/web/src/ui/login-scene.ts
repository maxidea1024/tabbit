// 로그인 씬.
//
// **판이 아니라 화면입니다.** 계정을 정하는 것은 게임 위에 잠깐 뜨는 일이 아니라 게임에
// 들어가기 전에 지나는 자리이고, 판으로 두면 뒤에 타이틀이 비쳐 이미 시작한 것처럼
// 보입니다. 타이틀이 같은 이유로 화면인 것과 같습니다.
//
// **「계정 없이 시작하기」가 제공자와 같은 크기입니다.** 작게 두면 권유가 되고, 이 게임은
// 로그인 없이도 온전합니다 — 권유할 것이 없습니다.
//
// **실행할 때마다 지납니다.** 로그인해 두었으면 세션이 남아 있으므로 곧바로 타이틀이고,
// 그렇지 않으면 켤 때마다 여기입니다 — 계정 없이 하기로 한 것은 그 실행에만 적용됩니다.
// 「한 번 물어보고 그다음부터 건너뛰던」 것을 걷었습니다.
//
// **되돌아가는 단추가 따로 없습니다.** 「계정 없이 시작하기」가 나가는 길이므로, 구석에
// 「뒤로」를 하나 더 두면 같은 일을 하는 것이 둘입니다.

import { BlurFilter, Container, FillGradient, Graphics, Text } from 'pixi.js'

import { language as nowLanguage, LANGUAGE_NAMES, LANGUAGES, t, tf,
         type Language } from '../core/strings'
import * as account from '../net/session'
import type { Provider } from '../net/session'
import { UI, SIZE, TEXT, WEIGHT } from '../render/theme'
import { providerTint } from './provider'
import { Button } from './widgets'
import { sceneArt } from './scene-art'
import { Wordmark } from './wordmark'

/** 이름과 그 아래 한 줄. **타이틀보다 위입니다** — 아래에 단추가 더 놓입니다. */
const LOGO_Y = 132
const WHY_Y = 276

/** 제공자 단추의 크기. */
const BUTTON_W = 320
const BUTTON_H = 52
const GAP = 12

/**
 * 진행 띠가 머무는 가장 짧은 시간.
 *
 * **떴다 곧 사라지면 알림이 아니라 깜빡임입니다.** 개발용 로그인은 20ms 안에 끝나므로
 * 이것이 없으면 눌렀는데 아무 일도 없었던 것처럼 보입니다.
 */
const BAND_LEAST_MS = 1_500

/**
 * 띠가 떴을 때 그 뒤를 흐리는 정도. 화면 픽셀입니다.
 *
 * **뒤가 무엇인지는 알아볼 수 있어야 합니다.** 띠는 이 화면이 지금 무엇을 하는 중인가를
 * 알리는 것이지 다른 화면이 아니고, 뒤가 통째로 사라지면 화면이 갈린 것으로 보입니다.
 */
const BAND_BLUR_PX = 5

/** 띠가 뜨고 지는 데 걸리는 시간. */
const BAND_FADE = 0.18

/** 띠의 높이와, 그 안을 지나는 빛의 폭. */
const BAND_H = 92
const SWEEP_W = 300

export class LoginScene extends Container {
  /**
   * 띠 뒤에서 흐려지는 것들.
   *
   * **띠는 이 통 밖입니다.** 흐림을 화면 전체에 걸면 알리는 글까지 흐려지고, 그러면 무엇을
   * 하는 중인지가 적혀 있지 않은 것과 같습니다.
   */
  private readonly under = new Container()
  private readonly body = new Container()
  /** 무언가 진행 중일 때 화면을 가로지르는 띠. */
  private readonly band = new Container()
  private readonly haze = new BlurFilter({ strength: 0, quality: 3, resolution: 0.5 })
  /**
   * 띠가 얼마나 떠 있는가. 0에서 1입니다.
   *
   * **뚝 뜨고 뚝 지지 않습니다.** 흐림이 한 프레임에 걸리고 한 프레임에 풀리면 그것은
   * 흐려지는 것이 아니라 화면이 두 번 갈리는 것으로 보입니다.
   */
  private bandLevel = 0
  private bandWanted = false
  /** 띠 안에서 도는 것들. `advance` 가 움직입니다. */
  private bandText?: Text
  private bandSweep?: Graphics
  private bandWord = ''
  private bandClock = 0
  /** 말 고르기가 펼쳐져 있는가. */
  private langOpen = false
  /**
   * 다시 그려야 하는가.
   *
   * **누른 그 자리에서 다시 그리지 않습니다.** 눌린 것을 지우는 일이 그 눌림을 처리하는
   * 중에 일어나면, 그다음 차례를 기다리던 것이 없어진 객체를 만납니다 — 화면이 거기서
   * 멈춥니다. 다음 프레임에 그립니다.
   */
  private dirty = false
  private list: Provider[] = []
  private dev = false
  private note = t('ui.lb.loading')
  private time = 0
  /**
   * 이름.
   *
   * **타이틀과 같은 것입니다**([`wordmark.ts`](wordmark.ts)) — 크기만 다릅니다. 이름 위에
   * 얹어 두었던 네 잎은 둘 다에서 걷었습니다.
   *
   * **다시 그리는 것 밖에 있습니다.** `redraw` 는 판을 통째로 버리고 다시 만드는데,
   * 이름은 말이 바뀌어도 그대로이고 떠오르는 것도 이어져야 합니다.
   */
  private readonly mark = new Wordmark(84, 3)

  /**
   * 띠 뒤의 배경을 흐릴 만큼.
   *
   * **배경은 이 화면 밖입니다.** 셰이더가 그리는 판 한 장이고 씬들이 함께 쓰는 것이므로,
   * 이 화면은 얼마나 흐릴지만 알리고 거는 것은 `game.ts` 가 합니다.
   */
  onBusy?: (level: number) => void
  /** 로그인 없이 하겠다고 했습니다. */
  onSingle?: () => void
  /** 게임을 나갑니다. **묻는 것은 부르는 쪽이 합니다.** */
  onQuit?: () => void
  /** 개발용 로그인으로 들어왔습니다. 제공자를 지난 것과 같은 자리입니다. */
  onSignedIn?: () => void
  /**
   * 말을 바꿨습니다.
   *
   * **여기서도 바꿀 수 있어야 합니다.** 이 화면이 게임의 첫 화면이므로, 읽지 못하는 말로
   * 적혀 있으면 무엇을 고르는 자리인지부터 알 수 없습니다 — 옵션은 그 뒤에 있습니다.
   */
  onLanguage?: (language: Language) => void

  constructor() {
    super()
    // **배경 그림이 맨 아래입니다.** 타이틀과 같은 그림 한 장을 씁니다(`ui/scene-art.ts`).
    // 이 통은 띠 뒤에서 흐려지므로 그림도 함께 흐려집니다 — 로그인 띠에 초점이 갑니다.
    // **0.34로 눌러 깝니다.** 글이 여섯 줄이므로 타이틀보다 더 낮춥니다.
    const art = sceneArt(0.34)
    if (art !== undefined) this.under.addChild(art)
    this.under.addChild(this.mark, this.body)
    this.addChild(this.under, this.band)
    this.mark.position.set(SIZE.width / 2, LOGO_Y)
    this.redraw()
    void this.load()
  }

  /**
   * 무엇이 진행 중인지를 화면 가운데에 알립니다.
   *
   * **가로로 펼친 띠 하나입니다.** 판을 띄우면 로그인 화면 위에 또 하나의 화면이 되고,
   * 이것은 그 화면이 지금 무엇을 하는 중인가이므로 화면 자신이 알립니다.
   */
  private showBand(message: string): void {
    this.band.removeChildren().forEach(child => child.destroy({ children: true }))

    const y = SIZE.height / 2 - BAND_H / 2

    // **누르는 것을 막습니다.** 진행 중에 제공자를 또 누르면 요청이 둘이 됩니다.
    //
    // **뒤가 흐려져 있습니다**(`stepBand`). 덮개는 그 위에 한 겹 더 얹는 어두움이고,
    // 흐림만으로는 띠 위의 글이 뒤의 밝은 자리와 겹칠 때 읽히지 않습니다.
    const block = new Graphics()
    block.rect(0, 0, SIZE.width, SIZE.height).fill({ color: UI.scrim, alpha: 0.52 })
    block.eventMode = 'static'
    block.on('pointertap', () => undefined)

    const strip = new Graphics()
    strip.rect(0, y, SIZE.width, BAND_H).fill({ color: UI.ground, alpha: 0.94 })
    strip.rect(0, y, SIZE.width, 1).fill({ color: UI.yellow, alpha: 0.34 })
    strip.rect(0, y + BAND_H - 1, SIZE.width, 1).fill({ color: UI.yellow, alpha: 0.34 })

    // **띠 안을 빛 한 줄이 지나갑니다.** 글만 있으면 멈춘 화면과 구분되지 않습니다 —
    // 무언가 도는 중이라는 것은 움직이는 것으로만 읽힙니다.
    //
    // **한 번 그리고 자리만 옮깁니다.** 조각 열둘로 나눠 조각마다 알파를 달리해 매 프레임
    // 다시 그리고 있었고, 알파가 조각 안에서 한 값이라 빛 하나가 아니라 막대 열둘로
    // 보였습니다 — 그라디언트 하나면 값이 이어집니다.
    const sweep = new Graphics()
    sweep.rect(0, 0, SWEEP_W, BAND_H - 2).fill(new FillGradient({
      start: { x: 0, y: 0 },
      end: { x: 1, y: 0 },
      colorStops: [
        { offset: 0, color: 'rgba(118, 239, 169, 0)' },
        { offset: 0.5, color: 'rgba(118, 239, 169, 0.14)' },
        { offset: 1, color: 'rgba(118, 239, 169, 0)' },
      ],
      textureSpace: 'local',
    }))
    sweep.position.set(-SWEEP_W, y + 1)
    this.bandSweep = sweep

    const text = new Text({
      text: message,
      style: { fontSize: TEXT.lead, fill: UI.ink, fontWeight: WEIGHT.bold, letterSpacing: 3 },
    })
    text.anchor.set(0.5)
    text.position.set(SIZE.width / 2, SIZE.height / 2)
    this.bandText = text
    this.bandWord = message
    this.bandClock = 0

    this.band.addChild(block, strip, sweep, text)
    this.band.visible = true
    this.band.interactiveChildren = true
    this.bandWanted = true
    this.spinBand(0)
  }

  /**
   * 띠가 뜨고 지는 것을 한 걸음.
   *
   * **보이지 않을 때도 돕니다.** 띠를 띄운 채로 씬이 갈리므로, 여기서 멈추면 흐림이 그
   * 값에 머물러 다음 화면에 남습니다.
   */
  private stepBand(seconds: number): void {
    if (!this.visible) this.bandWanted = false
    const want = this.bandWanted ? 1 : 0
    if (this.bandLevel === want) {
      this.onBusy?.(this.bandLevel)
      return
    }

    const step = seconds / BAND_FADE
    this.bandLevel = want > this.bandLevel
      ? Math.min(1, this.bandLevel + step)
      : Math.max(0, this.bandLevel - step)

    this.band.alpha = this.bandLevel
    // **지는 동안에는 누름을 받지 않습니다.** 아직 보이지만 이미 끝난 것이고, 그 위를
    // 누르면 그 아래의 단추가 눌리지 않습니다.
    this.band.interactiveChildren = this.bandWanted

    const on = this.bandLevel > 0.004
    const filtered = (this.under.filters as unknown[] | null)?.length ?? 0
    if (on && filtered === 0) this.under.filters = [this.haze]
    else if (!on && filtered > 0) this.under.filters = []
    if (on) this.haze.strength = this.bandLevel * BAND_BLUR_PX * 0.5

    // 다 졌으면 띠를 걷습니다.
    if (!on && !this.bandWanted && this.band.visible) {
      this.band.visible = false
      this.bandText = undefined
      this.bandSweep = undefined
      this.band.removeChildren().forEach(child => child.destroy({ children: true }))
    }
    this.onBusy?.(this.bandLevel)
  }

  /** 띠 안의 글과 빛을 한 걸음 움직입니다. */
  private spinBand(seconds: number): void {
    if (!this.band.visible) return
    this.bandClock += seconds

    // 점 셋이 차례로 붙습니다.
    const dots = Math.floor(this.bandClock * 2.6) % 4
    if (this.bandText) this.bandText.text = this.bandWord + '.'.repeat(dots)

    const sweep = this.bandSweep
    if (!sweep) return

    // 한 바퀴에 1.6초. 화면을 다 지나면 왼쪽에서 다시 들어옵니다.
    sweep.x = ((this.bandClock / 1.6) % 1) * (SIZE.width + SWEEP_W) - SWEEP_W
  }

  /** 걷습니다. **그 자리에서 지우지 않습니다** — `stepBand` 가 잦아든 뒤에 지웁니다. */
  private hideBand(): void {
    this.bandWanted = false
  }

  /** 띠를 띄운 채로 하나를 합니다. **적어도 얼마간은 머뭅니다.** */
  private async withBand<T>(message: string, work: () => Promise<T>): Promise<T> {
    this.showBand(message)
    const started = Date.now()
    try {
      return await work()
    } finally {
      const left = BAND_LEAST_MS - (Date.now() - started)
      if (left > 0) await new Promise(done => setTimeout(done, left))
      this.hideBand()
    }
  }

  private async load(): Promise<void> {
    try {
      const found = await account.providers()
      // **서버가 켠 것과 이 빌드에 단추가 있는 것이 겹치는 것만 그립니다.** GitHub 단추는
      // `import.meta.env.DEV` 안에 있으므로 배포 빌드에는 그 코드 자체가 없습니다.
      this.list = found.providers.filter(one => one.id !== 'github' || import.meta.env.DEV)
      this.dev = found.dev && import.meta.env.DEV
      this.note = this.list.length === 0 && !this.dev ? t('ui.lb.login.none') : ''
    } catch {
      // **서버가 없어도 게임은 합니다.** 그 사실을 적고 싱글플레이만 남깁니다.
      this.list = []
      this.note = t('ui.lb.fail.offline')
    }
    this.redraw()
  }

  private redraw(): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))

    const why = new Text({
      text: t('ui.account.why'),
      style: { fontSize: TEXT.base, fill: UI.light, fontWeight: WEIGHT.normal },
    })
    why.anchor.set(0.5, 0)
    why.position.set(SIZE.width / 2, WHY_Y)
    this.body.addChild(why)

    let y = WHY_Y + 44
    for (const provider of this.list) {
      const button = new Button(tf('ui.account.continueWith', { name: provider.label }),
                                BUTTON_W, BUTTON_H, 'quiet', () => {
        // **넘어가기 전에 띠를 띄웁니다.** 제공자로 가는 데 한두 박자가 걸리는데, 그동안
        // 아무 표시가 없으면 눌리지 않은 것으로 보입니다.
        this.showBand(t('ui.account.signingIn'))
        account.goToProvider(provider.id)
      }, 18)
      button.position.set(SIZE.width / 2 - BUTTON_W / 2, y)
      // **제공자의 색은 작은 네모 하나에만 듭니다.** 단추 넷을 저마다의 색으로 칠하면
      // 어느 것을 고르라는 화면인지가 색으로 정해지지 않고, 화면에 채도가 넷 늘어납니다.
      const chip = new Graphics()
      chip.roundRect(0, 0, 16, 16, 4).fill(providerTint(provider.id))
      chip.position.set(16, (BUTTON_H - 16) / 2)
      button.addChild(chip)
      this.body.addChild(button)
      y += BUTTON_H + GAP
    }

    // **개발용 로그인.** OAuth 를 지나지 않고 계정 하나로 들어갑니다 — 화면을 고치는
    // 동안 매번 제공자를 지나지 않기 위한 것이고, `import.meta.env.DEV` 안에 있으므로
    // 배포 빌드에는 이 코드가 없습니다.
    if (import.meta.env.DEV && this.dev) {
      const fake = new Button(t('ui.account.devLogin'), BUTTON_W, BUTTON_H - 6, 'neutral',
                              () => void this.signInAsDev(), 16)
      fake.position.set(SIZE.width / 2 - BUTTON_W / 2, y)
      this.body.addChild(fake)
      y += BUTTON_H - 6 + GAP
    }

    // **싱글플레이는 자리가 고정입니다.** 제공자가 몇이든 같은 자리에 있어야 합니다 —
    // 제공자 하나가 늘고 줄 때마다 이 단추가 오르내리면, 늘 같은 것을 누르는 사람이
    // 매번 찾아야 합니다.
    const singleY = SIZE.height - 214

    if (this.note !== '') {
      const note = new Text({
        text: this.note,
        style: {
          fontSize: TEXT.body, fill: UI.inkDim, wordWrap: true,
          wordWrapWidth: BUTTON_W + 80, align: 'center',
        },
      })
      // **위가 비었으면 아래에 붙습니다.** 서버가 없으면 제공자 단추가 하나도 서지
      // 않는데, 그때 이 글이 소개 글 바로 밑에 남으면 그 아래로 화면의 3분의 1이 빈
      // 채로 남습니다 — 이 글이 말하는 것은 「위에 아무것도 없는 까닭」이므로 그 빈자리가
      // 아니라 다음에 누를 것 위에 있어야 합니다.
      const alone = this.list.length === 0 && !(import.meta.env.DEV && this.dev)
      note.anchor.set(0.5, alone ? 1 : 0)
      note.position.set(SIZE.width / 2, alone ? singleY - 26 : y + 2)
      this.body.addChild(note)
      y += note.height + 14
    }

    // **가르는 줄 하나.** 위는 계정을 만드는 길이고 아래는 만들지 않는 길입니다.
    //
    // **가를 것이 없으면 두지 않습니다.** 서버가 없으면 위가 비는데, 그때도 「또는」이
    // 남아 있으면 무엇과 무엇을 가르는 줄인지 알 수 없습니다.
    if (this.list.length > 0 || (import.meta.env.DEV && this.dev)) {
      const ruleY = singleY - 26
      const half = (BUTTON_W - 46) / 2
      const rule = new Graphics()
      rule.rect(SIZE.width / 2 - BUTTON_W / 2, ruleY, half, 1).fill(UI.hairline)
      rule.rect(SIZE.width / 2 + BUTTON_W / 2 - half, ruleY, half, 1)
        .fill(UI.hairline)
      const or = new Text({
        text: t('ui.account.or'),
        style: { fontSize: TEXT.small, fill: UI.inkFaint },
      })
      or.anchor.set(0.5)
      or.position.set(SIZE.width / 2, ruleY)
      this.body.addChild(rule, or)
    }
    void y

    const single = new Button(t('ui.account.guestStart'), BUTTON_W, BUTTON_H, 'select',
                              () => void this.startWithoutAccount(), 18)
    single.position.set(SIZE.width / 2 - BUTTON_W / 2, singleY)
    this.body.addChild(single)

    const singleNote = new Text({
      text: t('ui.account.singleNote'),
      style: {
        fontSize: TEXT.small, fill: UI.inkDim, wordWrap: true,
        wordWrapWidth: BUTTON_W + 120, align: 'center',
      },
    })
    singleNote.anchor.set(0.5, 0)
    singleNote.position.set(SIZE.width / 2, singleY + BUTTON_H + 10)
    this.body.addChild(singleNote)

    // 나가기. **이 화면의 마지막 줄입니다** — 로그인도 하지 않고 게임도 하지 않겠다는
    // 것이므로 목록의 끝입니다.
    const quitW = 132
    const quit = new Button(t('ui.button.quit'), quitW, 38, 'neutral',
                            () => this.onQuit?.(), 14)
    quit.position.set(SIZE.width / 2 - quitW / 2, singleY + BUTTON_H + 44)
    this.body.addChild(quit)

    // 판 번호. **왼쪽 아래 구석입니다.**
    const version = new Text({
      text: `v${__APP_VERSION__}`,
      style: { fontSize: TEXT.small, fill: UI.inkFaint, fontWeight: WEIGHT.normal },
    })
    version.anchor.set(0, 1)
    version.position.set(30, SIZE.height - 20)
    this.body.addChild(version)

    this.drawLanguage()

    // 들고 있는 것. **무엇을 주는지 모른 채로 누르게 하지 않습니다.**
    const legal = new Text({
      text: t('ui.account.legal'),
      style: {
        fontSize: TEXT.mini, fill: UI.inkFaint, wordWrap: true,
        wordWrapWidth: 620, align: 'center',
      },
    })
    legal.anchor.set(0.5, 1)
    legal.position.set(SIZE.width / 2, SIZE.height - 44)
    this.body.addChild(legal)

    const keep = new Text({
      text: t('ui.lb.login.keep'),
      style: {
        fontSize: TEXT.mini, fill: UI.inkFaint, wordWrap: true,
        wordWrapWidth: 560, align: 'center',
      },
    })
    keep.anchor.set(0.5, 1)
    keep.position.set(SIZE.width / 2, SIZE.height - 26)
    this.body.addChild(keep)
  }

  /**
   * 말 고르기. **오른쪽 위 구석입니다** — 발라트로가 같은 자리에 둡니다.
   *
   * 눌러야 펼쳐집니다. 여섯 개를 늘 펼쳐 두면 로그인 화면의 절반이 말 목록이 됩니다.
   */
  private drawLanguage(): void {
    const now = nowLanguage()
    const width = 132
    const height = 34
    const x = SIZE.width - 30 - width
    const y = 30

    const chip = new Container()
    const plate = new Graphics()
    plate.roundRect(0, 0, width, height, 8)
      .fill({ color: UI.cell, alpha: 0.92 })
      .stroke({ color: this.langOpen ? UI.pick : UI.hairline, width: 1.5 })
    const label = new Text({
      text: LANGUAGE_NAMES[now],
      style: { fontSize: TEXT.body, fill: UI.ink, fontWeight: WEIGHT.normal },
    })
    label.anchor.set(0.5)
    label.position.set(width / 2, height / 2)
    chip.addChild(plate, label)
    chip.position.set(x, y)
    chip.eventMode = 'static'
    chip.cursor = 'pointer'
    chip.on('pointertap', () => {
      this.langOpen = !this.langOpen
      this.dirty = true
    })
    this.body.addChild(chip)

    if (!this.langOpen) return

    const list = new Container()
    for (let at = 0; at < LANGUAGES.length; at++) {
      const code = LANGUAGES[at]
      const on = code === now
      const rowY = (height + 6) + at * (height - 2)

      const row = new Container()
      const back = new Graphics()
      back.roundRect(0, rowY, width, height - 4, 7)
        .fill({ color: on ? UI.quiet : UI.cell, alpha: 0.96 })
        .stroke({ color: on ? UI.pick : UI.hairline, width: 1 })
      const text = new Text({
        text: LANGUAGE_NAMES[code],
        style: {
          fontSize: TEXT.body, fill: on ? UI.ink : UI.inkDim,
          fontWeight: on ? '700' : '400',
        },
      })
      text.anchor.set(0.5)
      text.position.set(width / 2, rowY + (height - 4) / 2)
      row.addChild(back, text)
      row.eventMode = 'static'
      row.cursor = 'pointer'
      row.on('pointertap', () => {
        this.langOpen = false
        this.dirty = true
        if (code !== now) {
          // **여기서 말을 바꾸지 않습니다.** 부르는 쪽이 「고른 말이 지금 말과 다른가」로
          // 화면 전체를 다시 그릴지 정하는데, 여기서 미리 바꿔 두면 그 판정이 항상 거짓이
          // 되어 타이틀과 판이 앞의 말로 남습니다 — 글꼴도 그때 바뀝니다.
          this.onLanguage?.(code)
        }
      })
      list.addChild(row)
    }
    list.position.set(x, y)
    this.body.addChild(list)
  }

  /**
   * 로그인 없이 들어갑니다.
   *
   * **제공자로 들어가는 것과 같은 띠가 뜹니다.** 하는 일이 없어서 곧바로 넘어가는데, 그러면
   * 이 단추만 「누르자마자 화면이 바뀌는 것」이 되어 다른 단추와 다른 갈래로 보입니다 —
   * 어느 쪽으로 들어가도 지나는 자리가 같아야 합니다.
   */
  private async startWithoutAccount(): Promise<void> {
    await this.withBand(t('ui.account.startingSingle'), async () => undefined)
    this.onSingle?.()
  }

  /**
   * 개발용으로 들어갑니다.
   *
   * **이름을 그때그때 짓습니다.** 같은 이름으로 다시 부르면 같은 계정이므로, 여러
   * 사람을 흉내 내려면 이름이 달라야 합니다.
   */
  private async signInAsDev(): Promise<void> {
    try {
      await this.withBand(t('ui.account.signingIn'),
                          () => account.devSignIn(`dev_${Math.floor(Math.random() * 1e5)}`))
      this.onSignedIn?.()
    } catch {
      // 알림은 `NetStatus` 가 띄웁니다.
    }
  }

  relabel(): void {
    this.dirty = true
  }

  advance(seconds: number): void {
    // **다시 그리는 것은 여기 한 자리입니다.** 보이지 않을 때도 그려야 합니다 — 말이
    // 바뀐 것을 이 화면이 다음에 뜰 때까지 모르고 있으면 안 됩니다.
    if (this.dirty) {
      this.dirty = false
      this.redraw()
    }
    // **띠는 보이지 않을 때도 잦아듭니다.** 띠를 띄운 채로 씬이 갈리므로, 여기 아래에
    // 두면 흐림이 그 값에 머물러 다음 화면에 남습니다.
    this.stepBand(seconds)
    if (!this.visible) return
    this.time += seconds
    this.mark.advance(seconds)
    this.spinBand(seconds)
  }
}
