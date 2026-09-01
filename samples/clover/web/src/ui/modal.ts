// 판을 띄우고 걷는 자리.
//
// **판이 저마다 자기를 띄우면 규칙이 저마다 다릅니다.** 어떤 것은 뒤를 덮고 어떤 것은 덮지
// 않으며, 어느 것이 위인지가 붙이는 순서로 정해지고, 하나를 열면 다른 하나가 조용히 닫혀
// 있었습니다 — 게임 방법이 타이틀에서 열리지 않던 것도 그 판이 판 안쪽 층에 있었기
// 때문입니다.
//
// 그래서 **쌓는 것을 한 곳이 맡습니다.** 여는 순서가 곧 위아래이고, 뒤를 덮는 것도 · 뒤로
// 물러나는 것도 · 들어오고 나가는 움직임도 여기 한 벌만 있습니다.

import { Container, Graphics, Rectangle, Text } from 'pixi.js'
import { t } from '../core/strings'

import { plate, FLOATING } from '../render/skin'
import { Button } from './widgets'
import { COLOR, SIZE } from '../render/theme'

/** 쌓을 수 있는 판 하나. */
export interface ModalPanel {
  /** 판의 몸통. **자리는 이쪽이 정하지 않습니다** — 쌓는 쪽이 가운데에 놓고 움직입니다. */
  readonly view: Container
  /** 판의 넓이. 가운데에 놓는 데 씁니다. */
  readonly size: { width: number; height: number }
  /** 뒤를 눌러 닫히는가. 적지 않으면 닫힙니다. */
  readonly dismissable?: boolean
  /** 닫힌 뒤에 부릅니다. 판이 자기 상태를 되돌릴 자리입니다. */
  onClosed?(): void
}

interface Entry {
  panel: ModalPanel
  /** 0 이 없는 것, 1 이 다 나온 것. */
  t: number
  /** 닫히는 중인가. */
  leaving: boolean
  /** 들어올 때의 떨림. 0 으로 잦아듭니다. */
  rumble: number
  /** 지금 그려지는 깊이. 위에 몇 장이 얹혀 있는가입니다. */
  depth: number
}

/** 뒤를 덮는 정도. */
const VEIL = 0.82
/** 판 하나가 얹힐 때마다 아래의 것이 물러나는 정도. */
const BACK_SCALE = 0.055
const BACK_LIFT = 16
const BACK_FADE = 0.34

export class Modals extends Container {
  private readonly veil = new Graphics()
  private readonly entries: Entry[] = []

  constructor() {
    super()
    this.zIndex = 9_500
    this.sortableChildren = true

    // **뒤를 덮지 않으면 뒤의 카드가 눌립니다.** 기준 넓이 밖까지 덮어야 창이 넓을 때
    // 옆이 뚫리지 않습니다.
    this.veil.rect(-SIZE.width, -SIZE.height, SIZE.width * 3, SIZE.height * 3)
      .fill({ color: 0x070a10, alpha: 1 })
    this.veil.eventMode = 'static'
    this.veil.cursor = 'pointer'
    this.veil.zIndex = 0
    this.veil.on('pointertap', () => this.closeTop())
    this.addChild(this.veil)
    this.sync()
  }

  /** 지금 몇 장이 떠 있는가. 닫히는 중인 것은 세지 않습니다. */
  get depth(): number {
    return this.entries.filter(entry => !entry.leaving).length
  }

  get busy(): boolean {
    return this.entries.length > 0
  }

  has(panel: ModalPanel): boolean {
    return this.entries.some(entry => entry.panel === panel && !entry.leaving)
  }

  /**
   * 판 하나를 얹습니다.
   *
   * 이미 떠 있으면 다시 얹지 않고 **맨 위로 올립니다** — 같은 판이 둘 쌓이면 하나를 닫아도
   * 남아 있습니다.
   */
  /** 판이 뜨고 닫힐 때. 소리를 내는 쪽이 겁니다. */
  onOpened?: () => void
  onClosed?: () => void

  open(panel: ModalPanel): void {
    const found = this.entries.find(entry => entry.panel === panel)
    if (found) {
      found.leaving = false
      this.entries.splice(this.entries.indexOf(found), 1)
      this.entries.push(found)
      this.sync()
      return
    }

    this.addChild(panel.view)
    this.entries.push({ panel, t: 0, leaving: false, rumble: 1, depth: 0 })
    this.onOpened?.()
    this.sync()
  }

  /** 맨 위의 것을 닫습니다. 뒤를 눌러 닫히지 않는 판이면 아무 일도 없습니다. */
  closeTop(): void {
    const top = this.topEntry()
    if (!top) return
    if (top.panel.dismissable === false) return
    this.close(top.panel)
  }

  close(panel: ModalPanel): void {
    const found = this.entries.find(entry => entry.panel === panel && !entry.leaving)
    if (!found) return
    this.onClosed?.()
    found.leaving = true
    this.sync()
  }

  closeAll(): void {
    for (const entry of this.entries) entry.leaving = true
    this.sync()
  }

  private topEntry(): Entry | undefined {
    for (let i = this.entries.length - 1; i >= 0; i--) {
      if (!this.entries[i].leaving) return this.entries[i]
    }
    return undefined
  }

  /**
   * 위의 것만 눌립니다.
   *
   * **아래의 판이 눌리면 안 됩니다** — 물러나 있는 것을 누를 수 있으면 그것이 아직 열려
   * 있다는 뜻이 되고, 쌓은 것이 쌓은 것으로 보이지 않습니다.
   */
  private sync(): void {
    const top = this.topEntry()
    this.entries.forEach((entry, index) => {
      entry.panel.view.zIndex = 10 + index
      entry.panel.view.eventMode = entry === top ? 'static' : 'none'
    })
    this.veil.eventMode = this.entries.length > 0 ? 'static' : 'none'
    this.visible = this.entries.length > 0
  }

  /**
   * 들어오고 나가는 움직임.
   *
   * **아래에서 밀려 올라와 한 번 넘칩니다.** 곧바로 자리에 있는 판은 화면에 붙여 놓은
   * 그림으로 보이고, 나갈 때 그대로 사라지면 닫은 것인지 화면이 멈춘 것인지 갈리지 않습니다.
   */
  advance(seconds: number): void {
    if (this.entries.length === 0) return

    const step = Math.min(1, seconds * 9)

    for (let i = this.entries.length - 1; i >= 0; i--) {
      const entry = this.entries[i]
      entry.t += ((entry.leaving ? 0 : 1) - entry.t) * step
      entry.rumble = Math.max(0, entry.rumble - seconds * 5.5)

      if (entry.leaving && entry.t < 0.02) {
        this.removeChild(entry.panel.view)
        this.entries.splice(i, 1)
        entry.panel.onClosed?.()
        this.sync()
      }
    }

    // 위에 몇 장이 얹혀 있는가. 닫히는 중인 것은 세지 않습니다.
    let above = 0
    for (let i = this.entries.length - 1; i >= 0; i--) {
      const entry = this.entries[i]
      entry.depth = above
      if (!entry.leaving) above++
    }

    let cover = 0
    for (const entry of this.entries) {
      cover = Math.max(cover, entry.t)
      this.place(entry)
    }

    this.veil.alpha = VEIL * cover
    this.veil.visible = cover > 0.01
    this.visible = this.entries.length > 0
  }

  private place(entry: Entry): void {
    const { view, size } = entry.panel

    // 넘쳤다가 자리에 앉습니다. `t` 가 1에 가까워질수록 넘침이 잦아듭니다.
    const overshoot = Math.sin(Math.min(1, entry.t) * Math.PI) * 0.06
    const back = entry.depth
    const scale = (0.9 + 0.1 * entry.t + overshoot) * (1 - BACK_SCALE * back)

    // 들어올 때의 떨림. **짧게, 그리고 잦아듭니다** — 오래 떨면 흔들리는 판이 됩니다.
    const shake = entry.rumble * entry.rumble * 5
    const jitterX = shake === 0 ? 0 : (Math.random() - 0.5) * shake
    const jitterY = shake === 0 ? 0 : (Math.random() - 0.5) * shake

    view.scale.set(scale)
    view.position.set(
      SIZE.width / 2 - (size.width / 2) * scale + jitterX,
      SIZE.height / 2 - (size.height / 2) * scale
        + (1 - entry.t) * 58 - BACK_LIFT * back + jitterY)
    view.alpha = entry.t * (1 - BACK_FADE * back)
    view.visible = entry.t > 0.01
  }
}

/** 판 머리의 높이. **모든 판이 같습니다** — 제목이 판마다 다른 자리에 있으면 한 벌로 보이지 않습니다. */
export const TITLE_BAR = 46
/** 판 밑단의 높이. 머리와 같은 띠이고, 닫기가 여기 있습니다. */
export const FOOTER_BAR = 56

/**
 * 판 하나의 껍데기.
 *
 * **모든 판이 같은 머리를 씁니다** — 제목은 가운데, 닫기는 오른쪽 끝의 `✕` 하나. 판마다
 * 제목의 자리와 닫는 방법이 다르면 한 벌로 보이지 않습니다.
 *
 * 바깥을 누르거나 `Esc` 로도 닫힙니다. `✕` 는 그 둘을 모르는 사람을 위한 자리입니다.
 */
export function panelFrame(width: number, height: number, title: string,
                           onClose?: () => void, extra?: Container,
                           foot = true): Container {
  const node = new Container()

  const board = new Graphics()
  // 광택 띠를 거의 없앱니다 — 판이 크면 띠의 끝이 가로줄로 보입니다.
  plate(board, width, height, { ...FLOATING, radius: 16, weight: 2.5, gloss: 0.06 })

  // 머리와 밑단. **같은 띠 둘이 위아래를 맞물어야 판이 끝난 것으로 보입니다.**
  const bars = new Graphics()
  bars.roundRect(1.5, 1.5, width - 3, TITLE_BAR, 14).fill({ color: 0x232e40, alpha: 0.95 })
  bars.rect(1.5, TITLE_BAR - 12, width - 3, 12).fill({ color: 0x232e40, alpha: 0.95 })
  bars.rect(1.5, TITLE_BAR, width - 3, 1.5).fill({ color: COLOR.panelEdge, alpha: 0.9 })

  // **밑단이 없는 판도 있습니다.** 누를 것이 그 판의 내용뿐이면 밑단은 빈 띠일 뿐입니다 —
  // 머리의 `✕` 와 바깥 누르기와 `Esc` 로 닫히므로 닫기를 또 둘 이유가 없습니다.
  const footTop = height - FOOTER_BAR - 1.5
  if (foot) {
    bars.roundRect(1.5, footTop, width - 3, FOOTER_BAR, 14)
      .fill({ color: 0x232e40, alpha: 0.95 })
    bars.rect(1.5, footTop, width - 3, 12).fill({ color: 0x232e40, alpha: 0.95 })
    bars.rect(1.5, footTop - 1.5, width - 3, 1.5).fill({ color: COLOR.panelEdge, alpha: 0.9 })
  }

  const heading = new Text({
    text: title,
    style: { fontSize: 18, fill: COLOR.ink, fontWeight: '800', letterSpacing: 1 },
  })
  heading.anchor.set(0.5, 0.5)
  heading.position.set(width / 2, TITLE_BAR / 2)

  node.addChild(board, bars, heading)

  // **닫을 수 없는 판도 있습니다.** 상점이 그렇습니다 — 닫으면 갈 곳이 없으므로 닫기가
  // 없고, 밑단에는 그 판이 할 일이 대신 놓입니다.
  if (onClose === undefined) {
    if (extra) {
      extra.position.set((width - extra.width) / 2, footTop + (FOOTER_BAR - 40) / 2)
      node.addChild(extra)
    }
    node.eventMode = 'static'
    return node
  }

  // 머리의 `✕`. **밑단에 닫기가 있으면 둘 다 둡니다** — 창을 닫는 두 손버릇이 다르고,
  // 둘 다 같은 자리에 있으면 어느 쪽으로도 닫힙니다.
  const shutMark = new Container()
  const mark = new Graphics()
  const paint = (lit: boolean) => {
    mark.clear()
    mark.roundRect(0, 0, 28, 28, 8)
      .fill({ color: lit ? 0x3d4a60 : 0x2a3446, alpha: lit ? 1 : 0.9 })
    mark.roundRect(0.5, 0.5, 27, 27, 8)
      .stroke({ color: lit ? 0x7d8ba4 : 0x46536a, width: 1.2 })
    const ink = lit ? COLOR.ink : COLOR.inkDim
    mark.moveTo(9.5, 9.5).lineTo(18.5, 18.5).stroke({ color: ink, width: 2 })
    mark.moveTo(18.5, 9.5).lineTo(9.5, 18.5).stroke({ color: ink, width: 2 })
  }
  paint(false)
  shutMark.addChild(mark)
  shutMark.position.set(width - 40, TITLE_BAR / 2 - 14)
  shutMark.eventMode = 'static'
  shutMark.hitArea = new Rectangle(0, 0, 28, 28)
  shutMark.cursor = 'pointer'
  shutMark.on('pointerover', () => paint(true))
  shutMark.on('pointerout', () => paint(false))
  shutMark.on('pointertap', () => onClose())

  node.addChild(shutMark)

  if (!foot) {
    node.eventMode = 'static'
    return node
  }

  // 밑단의 버튼들. **닫기는 판마다 같은 자리에 같은 모습입니다.**
  //
  // 부르는 쪽은 닫기를 만들지 않습니다 — 여기서 답니다. `extra` 는 그 판이 할 일이고,
  // 닫는 것이 아닙니다.
  const shut = new Button(t('ui.button.close'), 132, 34, 0x3a4658, onClose)
  const extraWidth = extra ? extra.width + 12 : 0
  const row = 132 + extraWidth
  // **판보다 넓어지지 않게 잡습니다.** 넘치면 버튼이 판의 좌우로 삐져나가고, 그것은
  // 판이 아니라 부서진 것으로 보입니다.
  const left = Math.max(14, (width - row) / 2)
  if (extra) {
    extra.position.set(left, footTop + (FOOTER_BAR - 34) / 2)
    node.addChild(extra)
  }
  shut.position.set(left + extraWidth, footTop + (FOOTER_BAR - 34) / 2)
  node.addChild(shut)

  node.eventMode = 'static'
  return node
}
