// 로그인 · 이름 · 프로필.
//
// 판이 셋이고 크기가 같습니다 — [`options.ts`](options.ts) 의 판과 같은 폭입니다. 셋 다
// 계정에 관한 것이므로 한 파일에 둡니다.
//
// **`/me` 와 `/profiles/{handle}` 이 같은 모양을 돌려줍니다.** 그래서 프로필 판도 하나이고,
// 내 것일 때만 아래에 관리 단추가 붙습니다 — 판을 둘로 만들면 한쪽만 고쳐지는 날이 옵니다.

import { Container, Graphics, Text } from 'pixi.js'

import type { Data } from '../core/data'
import { t, tf } from '../core/strings'
import * as api from '../net/api'
import type { Me, Provider } from '../net/api'
import { COLOR } from '../render/theme'
import { valueLabel } from './leaderboard'
import type { ModalPanel } from './modal'
import { panelFrame } from './modal'
import { Button } from './widgets'

const WIDTH = 520

// ---------------------------------------------------------------------------
// 로그인
// ---------------------------------------------------------------------------

export class LoginPanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: 380 }

  private readonly body = new Container()
  private list: Provider[] = []
  private note = t('ui.lb.loading')

  constructor(private readonly onClose: () => void) {
    this.view.addChild(this.body)
    this.redraw()
    void this.load()
  }

  private async load(): Promise<void> {
    try {
      this.list = await api.providers()
      // **서버가 켠 것과 이 빌드에 단추가 있는 것이 겹치는 것만 그립니다.** GitHub 단추는
      // `import.meta.env.DEV` 안에 있으므로 배포 빌드에는 그 코드 자체가 없습니다.
      this.list = this.list.filter(one => one.id !== 'github' || import.meta.env.DEV)
      this.note = this.list.length === 0 ? t('ui.lb.login.none') : ''
    } catch {
      this.note = t('ui.lb.fail.offline')
    }
    this.redraw()
  }

  private redraw(): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))

    const height = Math.max(300, 208 + this.list.length * 58)
    this.size.height = height
    this.body.addChild(panelFrame(WIDTH, height, t('ui.button.login'), this.onClose,
                                  undefined, false))

    // **왜 로그인해야 하는지가 먼저입니다.**
    const why = new Text({
      text: t('ui.lb.login.why'),
      style: {
        fontSize: 15, fill: COLOR.ink, fontWeight: '700',
        wordWrap: true, wordWrapWidth: WIDTH - 72, align: 'center',
      },
    })
    why.anchor.set(0.5, 0)
    why.position.set(WIDTH / 2, 76)
    this.body.addChild(why)

    let y = 124
    for (const provider of this.list) {
      const button = new Button(provider.label, WIDTH - 100, 46, 0x2f5f8f,
                                () => api.goToProvider(provider.id), 17)
      button.position.set(50, y)
      this.body.addChild(button)
      y += 58
    }

    if (this.note !== '') {
      const note = new Text({
        text: this.note,
        style: { fontSize: 13, fill: COLOR.inkDim, wordWrap: true,
                 wordWrapWidth: WIDTH - 72, align: 'center' },
      })
      note.anchor.set(0.5, 0)
      note.position.set(WIDTH / 2, y + 4)
      this.body.addChild(note)
      y += note.height + 12
    }

    // **들고 있는 것을 적습니다.** 무엇을 주는지 모른 채로 누르게 하지 않습니다.
    const keep = new Text({
      text: t('ui.lb.login.keep'),
      style: { fontSize: 11, fill: 0x7d8ca0, wordWrap: true,
               wordWrapWidth: WIDTH - 72, align: 'center' },
    })
    keep.anchor.set(0.5, 0)
    keep.position.set(WIDTH / 2, height - 62)
    this.body.addChild(keep)
  }
}

// ---------------------------------------------------------------------------
// 이름
// ---------------------------------------------------------------------------

/**
 * 첫 로그인 직후 한 번.
 *
 * **닫을 수 없습니다.** 이름 없는 계정은 순위표에 놓을 자리가 없습니다.
 */
export class HandlePanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH, height: 300 }
  readonly dismissable = false

  private readonly body = new Container()
  private typed = ''
  private problem = ''
  private caret = 0
  private detach?: () => void

  /** 이름이 정해졌습니다. */
  onDone?: (handle: string) => void

  constructor(private readonly first: boolean, private readonly onClose: () => void) {
    this.view.addChild(this.body)
    this.redraw()
    this.listen()
  }

  /**
   * 글쇠를 받습니다.
   *
   * **Pixi 에 입력 칸이 없습니다.** 그래서 창의 글쇠를 직접 받고, 판이 닫힐 때 떼어
   * 냅니다 — 떼지 않으면 게임 화면에서 누른 글쇠가 여기로 들어옵니다.
   */
  private listen(): void {
    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Backspace') {
        this.typed = this.typed.slice(0, -1)
      } else if (event.key === 'Enter') {
        void this.submit()
        return
      } else if (event.key.length === 1 && /[A-Za-z0-9_]/.test(event.key)) {
        if (this.typed.length < 16) this.typed += event.key
      } else {
        return
      }
      event.preventDefault()
      this.problem = ''
      this.redraw()
    }
    window.addEventListener('keydown', onKey)
    this.detach = () => window.removeEventListener('keydown', onKey)
  }

  onClosed(): void {
    this.detach?.()
  }

  private async submit(): Promise<void> {
    if (this.typed.length < 3) {
      this.problem = t('ui.lb.fail.shape')
      this.redraw()
      return
    }
    try {
      await api.setHandle(this.typed)
      this.detach?.()
      this.onDone?.(this.typed)
      this.onClose()
    } catch (error) {
      // **이 갈래는 물어본 판이 그 자리에서 적습니다.** 화면 구석에 한 번 더 뜨면 같은
      // 말이 두 번입니다.
      this.problem = t(api.failKey(error))
      this.shake = 1
      this.redraw()
    }
  }

  /** 붉게 한 번 흔들리는 정도. 0 으로 잦아듭니다. */
  private shake = 0

  advance(seconds: number): void {
    this.caret = (this.caret + seconds) % 1
    if (this.shake > 0) this.shake = Math.max(0, this.shake - seconds * 3.4)
    this.redraw()
  }

  private redraw(): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))
    const height = this.size.height
    this.body.addChild(panelFrame(WIDTH, height, t('ui.lb.handle.ask'),
                                  this.first ? undefined : this.onClose, undefined, false))

    const rule = new Text({
      text: t('ui.lb.handle.rule'),
      style: { fontSize: 12, fill: COLOR.inkDim, wordWrap: true,
               wordWrapWidth: WIDTH - 72, align: 'center' },
    })
    rule.anchor.set(0.5, 0)
    rule.position.set(WIDTH / 2, 78)
    this.body.addChild(rule)

    // 입력 칸.
    const wobble = Math.sin(this.shake * 24) * this.shake * 6
    const box = new Graphics()
    box.roundRect(60 + wobble, 122, WIDTH - 120, 46, 8)
      .fill({ color: 0x111823 })
      .stroke({ color: this.shake > 0 ? COLOR.bad : 0x2c3849, width: 2 })
    this.body.addChild(box)

    const shown = this.typed + (this.caret < 0.5 ? '|' : ' ')
    const value = new Text({
      text: shown,
      style: { fontSize: 20, fill: COLOR.ink, fontWeight: '700' },
    })
    value.anchor.set(0.5, 0.5)
    value.position.set(WIDTH / 2 + wobble, 145)
    this.body.addChild(value)

    if (this.problem !== '') {
      const problem = new Text({
        text: this.problem,
        style: { fontSize: 12, fill: COLOR.bad, wordWrap: true,
                 wordWrapWidth: WIDTH - 72, align: 'center' },
      })
      problem.anchor.set(0.5, 0)
      problem.position.set(WIDTH / 2, 176)
      this.body.addChild(problem)
    }

    const done = new Button(t('ui.button.confirmName'), 200, 44, 0x2f8f52,
                            () => void this.submit(), 16)
    done.position.set(WIDTH / 2 - 100, height - 68)
    this.body.addChild(done)
  }
}

// ---------------------------------------------------------------------------
// 프로필
// ---------------------------------------------------------------------------

export class ProfilePanel implements ModalPanel {
  readonly view = new Container()
  readonly size = { width: WIDTH + 120, height: 620 }

  private readonly body = new Container()
  private shown?: Me
  private note = t('ui.lb.loading')
  private confirming = false

  /** 이름을 바꾸겠다고 했습니다. */
  onRename?: () => void
  /** 로그아웃했습니다. */
  onSignedOut?: () => void

  constructor(private readonly data: Data, private readonly handle: string | undefined,
              private readonly onClose: () => void) {
    this.view.addChild(this.body)
    this.redraw()
    void this.load()
  }

  private get mine(): boolean {
    return this.handle === undefined
  }

  private async load(): Promise<void> {
    try {
      this.shown = this.mine ? await api.me() : await api.profile(this.handle as string)
      this.note = ''
    } catch {
      this.note = t('ui.lb.fail.title')
    }
    this.redraw()
  }

  private redraw(): void {
    this.body.removeChildren().forEach(child => child.destroy({ children: true }))
    const { width, height } = this.size
    this.body.addChild(panelFrame(width, height, t('ui.lb.title'), this.onClose,
                                  undefined, false))

    const shown = this.shown
    if (!shown) {
      const note = new Text({
        text: this.note,
        style: { fontSize: 14, fill: COLOR.inkDim },
      })
      note.anchor.set(0.5, 0)
      note.position.set(width / 2, 160)
      this.body.addChild(note)
      return
    }

    // 이름과 등급.
    const name = new Text({
      text: shown.handle,
      style: { fontSize: 30, fill: COLOR.ink, fontWeight: '800' },
    })
    name.position.set(38, 68)
    this.body.addChild(name)

    const tier = new Text({
      text: shown.tier === '' || shown.tier === 'None'
        ? t('ui.lb.card.noTier') : this.tierName(shown.tier),
      style: {
        fontSize: 15, fill: this.tierColor(shown.tier), fontWeight: '700',
      },
    })
    tier.position.set(40, 106)
    this.body.addChild(tier)

    if (shown.lastSeasonTier !== '' && shown.lastSeasonTier !== 'None') {
      const last = new Text({
        text: tf('ui.lb.profile.lastSeason', { tier: this.tierName(shown.lastSeasonTier) }),
        style: { fontSize: 12, fill: COLOR.inkDim },
      })
      last.position.set(40, 128)
      this.body.addChild(last)
    }

    // 보드별 자리. **기록이 있는 것만입니다.**
    const top = 158
    const line = new Graphics()
    line.rect(30, top - 10, width - 60, 1).fill({ color: 0x2c3849 })
    this.body.addChild(line)

    if (shown.ranks.length === 0) {
      const none = new Text({
        text: t('ui.lb.noRecord'),
        style: { fontSize: 13, fill: COLOR.inkDim },
      })
      none.position.set(40, top + 6)
      this.body.addChild(none)
    }

    const rows = shown.ranks.slice(0, 14)
    for (let at = 0; at < rows.length; at++) {
      const rank = rows[at]
      const y = top + at * 24

      const label = new Text({
        text: rank.name,
        style: { fontSize: 13, fill: 0x9fb0c4 },
      })
      label.position.set(40, y)

      const place = new Text({
        text: `#${rank.rank}`,
        style: { fontSize: 13, fill: COLOR.good, fontWeight: '700' },
      })
      place.anchor.set(1, 0)
      place.position.set(width - 150, y)

      const value = new Text({
        text: valueLabel(this.data, rank.metric, rank.value),
        style: { fontSize: 13, fill: COLOR.money },
      })
      value.anchor.set(1, 0)
      value.position.set(width - 40, y)

      this.body.addChild(label, place, value)
    }

    this.drawFoot(width, height, shown)
  }

  private drawFoot(width: number, height: number, shown: Me): void {
    if (!this.mine) {
      const report = new Button(t('ui.button.report'), 150, 40, 0x8f3f3f, () => undefined, 15)
      report.position.set(width / 2 - 75, height - 62)
      this.body.addChild(report)
      return
    }

    if (shown.devices.length > 0) {
      const devices = new Text({
        text: tf('ui.lb.profile.devices', { n: shown.devices.length }),
        style: { fontSize: 11, fill: 0x7d8ca0 },
      })
      devices.position.set(40, height - 92)
      this.body.addChild(devices)
    }

    if (this.confirming) {
      const warn = new Text({
        text: t('ui.lb.profile.deleteWarn'),
        style: { fontSize: 12, fill: COLOR.bad, wordWrap: true, wordWrapWidth: width - 80 },
      })
      warn.position.set(40, height - 108)
      this.body.addChild(warn)
    }

    const gap = 12
    const bw = (width - 80 - gap * 2) / 3
    const rename = new Button(t('ui.button.confirmName'), bw, 40, 0x2f5f8f,
                              () => this.onRename?.(), 14)
    rename.position.set(40, height - 62)

    const out = new Button(t('ui.button.logout'), bw, 40, 0x4a5568, () => {
      void api.logout().then(() => {
        this.onSignedOut?.()
        this.onClose()
      })
    }, 14)
    out.position.set(40 + bw + gap, height - 62)

    // **두 번 누릅니다.** 되돌리지 않는 것이므로 한 번에 지워지지 않아야 합니다.
    const remove = new Button(t('ui.button.deleteAccount'), bw, 40,
                              this.confirming ? 0xa63f3f : 0x8f3f3f, () => {
      if (!this.confirming) {
        this.confirming = true
        this.redraw()
        return
      }
      void api.deleteAccount().then(() => {
        api.forget()
        this.onSignedOut?.()
        this.onClose()
      })
    }, 14)
    remove.position.set(40 + (bw + gap) * 2, height - 62)

    this.body.addChild(rename, out, remove)
  }

  private tierName(tier: string): string {
    const row = this.data.tables.tier.records.find(one => String(one.tier) === tier
      || one.name === tier)
    return row ? row.name : tier
  }

  private tierColor(tier: string): number {
    const row = this.data.tables.tier.records.find(one => String(one.tier) === tier
      || one.name === tier)
    return row ? Number.parseInt(row.color.slice(1), 16) : 0x6f7d90
  }
}
