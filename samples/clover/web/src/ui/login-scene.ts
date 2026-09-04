// 로그인 씬.
//
// **판이 아니라 화면입니다.** 계정을 정하는 것은 게임 위에 잠깐 뜨는 일이 아니라 게임에
// 들어가기 전에 지나는 자리이고, 판으로 두면 뒤에 타이틀이 비쳐 이미 시작한 것처럼
// 보입니다. 타이틀이 같은 이유로 화면인 것과 같습니다.
//
// **「싱글플레이로 시작」이 제공자와 같은 크기입니다.** 작게 두면 권유가 되고, 이 게임은
// 로그인 없이도 온전합니다 — 권유할 것이 없습니다.
//
// **한 번만 지납니다.** 로그인했거나 싱글플레이로 정했으면 다음부터는 곧바로 타이틀이고,
// 타이틀의 계정 단추가 여기로 되돌립니다.
//
// **되돌아가는 단추가 따로 없습니다.** 「계정 없이 시작하기」가 나가는 길이므로, 구석에
// 「뒤로」를 하나 더 두면 같은 일을 하는 것이 둘입니다.

import { Container, Graphics, Text } from 'pixi.js'

import { language as nowLanguage, LANGUAGE_NAMES, LANGUAGES, setLanguage, t, tf,
         type Language } from '../core/strings'
import * as account from '../net/session'
import type { Provider } from '../net/session'
import { COLOR, SIZE, UI } from '../render/theme'
import { Button } from './widgets'

/** 제공자 단추의 크기. */
const BUTTON_W = 320
const BUTTON_H = 52
const GAP = 12

/** 제공자마다의 색. **알아볼 수 있는 색이어야 고르는 것이 빨라집니다.** */
/**
 * 진행 띠가 머무는 가장 짧은 시간.
 *
 * **떴다 곧 사라지면 알림이 아니라 깜빡임입니다.** 개발용 로그인은 20ms 안에 끝나므로
 * 이것이 없으면 눌렀는데 아무 일도 없었던 것처럼 보입니다.
 */
const BAND_LEAST_MS = 1_500

const TINT: Record<string, number> = {
  google: 0x3a6ea5,
  discord: 0x4f5fc4,
  apple: 0x4a5568,
  github: 0x3f4a5a,
}

export class LoginScene extends Container {
  private readonly body = new Container()
  /** 무언가 진행 중일 때 화면을 가로지르는 띠. */
  private readonly band = new Container()
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
  private readonly leaf = new Graphics()

  /** 싱글플레이로 가겠다고 했습니다. */
  onSingle?: () => void
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
    this.addChild(this.leaf, this.body, this.band)
    this.drawLeaf()
    this.leaf.position.set(SIZE.width / 2, 150)
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

    const height = 92
    const y = SIZE.height / 2 - height / 2

    // **누르는 것을 막습니다.** 진행 중에 제공자를 또 누르면 요청이 둘이 됩니다.
    const block = new Graphics()
    block.rect(0, 0, SIZE.width, SIZE.height).fill({ color: 0x05080e, alpha: 0.42 })
    block.eventMode = 'static'
    block.on('pointertap', () => undefined)

    const strip = new Graphics()
    strip.rect(0, y, SIZE.width, height).fill({ color: 0x0a1018, alpha: 0.95 })
    strip.rect(0, y, SIZE.width, 1).fill({ color: 0x2c3849 })
    strip.rect(0, y + height - 1, SIZE.width, 1).fill({ color: 0x2c3849 })

    // **띠 안을 빛 한 줄이 지나갑니다.** 글만 있으면 멈춘 화면과 구분되지 않습니다 —
    // 무언가 도는 중이라는 것은 움직이는 것으로만 읽힙니다.
    const sweep = new Graphics()
    this.bandSweep = sweep

    const text = new Text({
      text: message,
      style: { fontSize: 20, fill: COLOR.ink, fontWeight: '800', letterSpacing: 3 },
    })
    text.anchor.set(0.5)
    text.position.set(SIZE.width / 2, SIZE.height / 2)
    this.bandText = text
    this.bandWord = message
    this.bandClock = 0

    this.band.addChild(block, strip, sweep, text)
    this.band.visible = true
    this.spinBand(0)
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

    const height = 92
    const y = SIZE.height / 2 - height / 2
    const band = 260
    // 한 바퀴에 1.6초. 화면을 다 지나면 왼쪽에서 다시 들어옵니다.
    const at = ((this.bandClock / 1.6) % 1) * (SIZE.width + band) - band

    sweep.clear()
    for (let step = 0; step < 12; step++) {
      const part = step / 11
      // 가운데가 밝고 양끝이 잦아드는 띠 하나를 조각으로 그립니다.
      const alpha = Math.sin(part * Math.PI) * 0.09
      sweep.rect(at + part * band, y + 1, band / 11 + 1, height - 2)
        .fill({ color: COLOR.good, alpha })
    }
  }

  private hideBand(): void {
    this.band.visible = false
    this.bandText = undefined
    this.bandSweep = undefined
    this.band.removeChildren().forEach(child => child.destroy({ children: true }))
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

    const title = new Text({
      text: 'clover',
      style: {
        fontSize: 76, fill: COLOR.good, fontWeight: '800',
        stroke: { color: 0x07130b, width: 8 }, letterSpacing: 6,
      },
    })
    title.anchor.set(0.5, 0)
    title.position.set(SIZE.width / 2, 186)
    this.body.addChild(title)

    const why = new Text({
      text: t('ui.account.why'),
      style: { fontSize: 15, fill: COLOR.ink, fontWeight: '700' },
    })
    why.anchor.set(0.5, 0)
    why.position.set(SIZE.width / 2, 292)
    this.body.addChild(why)

    let y = 336
    for (const provider of this.list) {
      const button = new Button(tf('ui.account.continueWith', { name: provider.label }),
                                BUTTON_W, BUTTON_H, UI.cell, () => {
        // **넘어가기 전에 띠를 띄웁니다.** 제공자로 가는 데 한두 박자가 걸리는데, 그동안
        // 아무 표시가 없으면 눌리지 않은 것으로 보입니다.
        this.showBand(t('ui.account.signingIn'))
        account.goToProvider(provider.id)
      }, 18)
      button.position.set(SIZE.width / 2 - BUTTON_W / 2, y)
      // **제공자의 색은 작은 네모 하나에만 듭니다.** 단추 넷을 저마다의 색으로 칠하면
      // 어느 것을 고르라는 화면인지가 색으로 정해지지 않고, 화면에 채도가 넷 늘어납니다.
      const chip = new Graphics()
      chip.roundRect(0, 0, 16, 16, 4).fill(TINT[provider.id] ?? UI.sky)
      chip.position.set(16, (BUTTON_H - 16) / 2)
      button.addChild(chip)
      this.body.addChild(button)
      y += BUTTON_H + GAP
    }

    // **개발용 로그인.** OAuth 를 지나지 않고 계정 하나로 들어갑니다 — 화면을 고치는
    // 동안 매번 제공자를 지나지 않기 위한 것이고, `import.meta.env.DEV` 안에 있으므로
    // 배포 빌드에는 이 코드가 없습니다.
    if (import.meta.env.DEV && this.dev) {
      const fake = new Button(t('ui.account.devLogin'), BUTTON_W, BUTTON_H - 6, UI.slate,
                              () => void this.signInAsDev(), 16)
      fake.position.set(SIZE.width / 2 - BUTTON_W / 2, y)
      this.body.addChild(fake)
      y += BUTTON_H - 6 + GAP
    }

    if (this.note !== '') {
      const note = new Text({
        text: this.note,
        style: {
          fontSize: 13, fill: COLOR.inkDim, wordWrap: true,
          wordWrapWidth: BUTTON_W + 80, align: 'center',
        },
      })
      note.anchor.set(0.5, 0)
      note.position.set(SIZE.width / 2, y + 2)
      this.body.addChild(note)
      y += note.height + 14
    }

    // **싱글플레이는 자리가 고정입니다.** 제공자가 몇이든 같은 자리에 있어야 합니다 —
    // 제공자 하나가 늘고 줄 때마다 이 단추가 오르내리면, 늘 같은 것을 누르는 사람이
    // 매번 찾아야 합니다.
    const singleY = SIZE.height - 214

    // **가르는 줄 하나.** 위는 계정을 만드는 길이고 아래는 만들지 않는 길입니다.
    const ruleY = singleY - 26
    const half = (BUTTON_W - 46) / 2
    const rule = new Graphics()
    rule.rect(SIZE.width / 2 - BUTTON_W / 2, ruleY, half, 1).fill(UI.hairline)
    rule.rect(SIZE.width / 2 + BUTTON_W / 2 - half, ruleY, half, 1)
      .fill(UI.hairline)
    const or = new Text({
      text: t('ui.account.or'),
      style: { fontSize: 12, fill: 0x66748a },
    })
    or.anchor.set(0.5)
    or.position.set(SIZE.width / 2, ruleY)
    this.body.addChild(rule, or)
    void y

    const single = new Button(t('ui.account.guestStart'), BUTTON_W, BUTTON_H, UI.cream,
                              () => this.onSingle?.(), 18)
    single.position.set(SIZE.width / 2 - BUTTON_W / 2, singleY)
    this.body.addChild(single)

    const singleNote = new Text({
      text: t('ui.account.singleNote'),
      style: {
        fontSize: 12, fill: 0x7d8ca0, wordWrap: true,
        wordWrapWidth: BUTTON_W + 120, align: 'center',
      },
    })
    singleNote.anchor.set(0.5, 0)
    singleNote.position.set(SIZE.width / 2, singleY + BUTTON_H + 10)
    this.body.addChild(singleNote)

    // 판 번호. **왼쪽 아래 구석입니다.**
    const version = new Text({
      text: `v${__APP_VERSION__}`,
      style: { fontSize: 12, fill: 0x5c6a7d, fontWeight: '700' },
    })
    version.anchor.set(0, 1)
    version.position.set(30, SIZE.height - 20)
    this.body.addChild(version)

    this.drawLanguage()

    // 들고 있는 것. **무엇을 주는지 모른 채로 누르게 하지 않습니다.**
    const legal = new Text({
      text: t('ui.account.legal'),
      style: {
        fontSize: 11, fill: 0x5c6a7d, wordWrap: true,
        wordWrapWidth: 620, align: 'center',
      },
    })
    legal.anchor.set(0.5, 1)
    legal.position.set(SIZE.width / 2, SIZE.height - 44)
    this.body.addChild(legal)

    const keep = new Text({
      text: t('ui.lb.login.keep'),
      style: {
        fontSize: 11, fill: 0x66748a, wordWrap: true,
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
      .fill({ color: 0x151d2a, alpha: 0.92 })
      .stroke({ color: this.langOpen ? UI.pick : UI.hairline, width: 1.5 })
    const label = new Text({
      text: LANGUAGE_NAMES[now],
      style: { fontSize: 13, fill: COLOR.ink, fontWeight: '700' },
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
        .fill({ color: on ? 0x24354a : 0x151d2a, alpha: 0.96 })
        .stroke({ color: on ? UI.pick : UI.hairline, width: 1 })
      const text = new Text({
        text: LANGUAGE_NAMES[code],
        style: {
          fontSize: 13, fill: on ? COLOR.ink : 0x9fb0c4,
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
          setLanguage(code)
          // **부르는 쪽이 화면 전체를 다시 그립니다.** 그 안에 이 화면도 들어 있으므로
          // 여기서 또 그리지 않습니다.
          this.onLanguage?.(code)
        }
      })
      list.addChild(row)
    }
    list.position.set(x, y)
    this.body.addChild(list)
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

  /** 네 잎. 타이틀의 것과 같은 모양입니다 — 같은 게임의 화면입니다. */
  private drawLeaf(): void {
    const g = this.leaf
    g.clear()
    for (let i = 0; i < 4; i++) {
      const angle = (Math.PI / 2) * i + Math.PI / 4
      g.circle(Math.cos(angle) * 15, Math.sin(angle) * 15, 13)
        .fill({ color: COLOR.good, alpha: 0.92 })
    }
    g.rect(-2, 13, 4, 22).fill({ color: 0x2f8f52 })
  }

  advance(seconds: number): void {
    // **다시 그리는 것은 여기 한 자리입니다.** 보이지 않을 때도 그려야 합니다 — 말이
    // 바뀐 것을 이 화면이 다음에 뜰 때까지 모르고 있으면 안 됩니다.
    if (this.dirty) {
      this.dirty = false
      this.redraw()
    }
    if (!this.visible) return
    this.time += seconds
    this.leaf.rotation = Math.sin(this.time * 0.8) * 0.16
    this.spinBand(seconds)
  }
}
