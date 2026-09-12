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

import { plate, plateTint, floatingStyle } from '../render/skin'
import { UI, SIZE, popupLeft, TEXT, WEIGHT } from '../render/theme'
import { fraction } from '../render/motion'
import { Button } from './widgets'
import { glowEdge, piece } from './chrome'

/** 쌓을 수 있는 판 하나. */
export interface ModalPanel {
  /** 판의 몸통. **자리는 이쪽이 정하지 않습니다** — 쌓는 쪽이 가운데에 놓고 움직입니다. */
  readonly view: Container
  /** 판의 넓이. 가운데에 놓는 데 씁니다. */
  readonly size: { width: number; height: number }
  /** 뒤를 눌러 닫히는가. 적지 않으면 닫힙니다. */
  readonly dismissable?: boolean
  /**
   * 가로로 화면의 가운데에 놓는가. 적지 않으면 왼쪽 판을 비껴 놓입니다.
   *
   * **비껴 놓이는 것은 판이 도는 동안의 규칙입니다.** 왼쪽에 지금 몇 점인지가 놓여 있으므로
   * 그것을 가리지 않는 것이 완전한 가운데보다 먼저인데, 타이틀에서 열리는 판에는 비껴 설
   * 대상이 없습니다 — 그 판이 이것을 켭니다.
   */
  readonly centered?: boolean
  /**
   * 뒤를 덮는가. 적지 않으면 덮습니다.
   *
   * **판이 다른 자리로 데려가는 것일 때만 덮습니다.** 정산은 판이 도는 그 자리의 한 걸음이고
   * 뒤에서 카드가 걷히는 것을 보는 중이므로, 덮으면 그 걸음이 끊깁니다 — 덮개가 없으면
   * 흐림도 없습니다(흐림은 덮개의 짙기를 그대로 씁니다).
   */
  readonly covers?: boolean
  /** 닫힌 뒤에 부릅니다. 판이 자기 상태를 되돌릴 자리입니다. */
  onClosed?(): void
  /**
   * 프레임마다 부릅니다. 판 안에 움직이는 것이 있으면 여기서 흘립니다.
   *
   * **`advance` 가 아닙니다.** 판 스스로 프레임을 받는 것들이 이미 그 이름을 다른 인자로
   * 쓰고 있어서, 같은 이름이면 그것들이 두 번 흐릅니다.
   */
  tick?(seconds: number): void
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

/**
 * 뒤를 덮는 정도.
 *
 * **덮는 것이지 지우는 것이 아닙니다.** 0.82 였고, 그 값에서는 뒤의 판이 검은 벽이 되어
 * 판 하나가 뜰 때마다 게임이 사라졌습니다. 뒤가 무엇이었는지 알아볼 만큼은 남겨 두어야
 * 판이 그 위에 얹힌 것으로 보입니다 — 판 하나가 얹힐 때마다 뒤가 물러나는
 * `BACK_FADE` 도 뒤가 보여야 값을 합니다.
 */
const VEIL = 0.62
/** 판 하나가 얹힐 때마다 아래의 것이 물러나는 정도. */
/**
 * 아래에 깔린 판을 얼마나 옅게 하는가.
 *
 * **옅어지기만 합니다.** 뒤로 물러난 것을 크기와 자리로도 알리고 있었는데, 그러면 묻는 판이
 * 열리고 닫힐 때마다 아래의 판이 작아졌다 돌아옵니다 — 덱을 고르고 「이 덱으로 시작」을
 * 누르면 판을 여는 자리가 한 번 줄었다가 커졌습니다. 무엇이 위인지는 막과 밝기가 이미
 * 알리므로, 자리와 크기는 건드리지 않습니다.
 */
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
      .fill({ color: UI.scrim, alpha: 1 })
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

  /**
   * 맨 위 판이 화면에서 차지한 사각형.
   *
   * **판을 누르는 도구가 이것을 씁니다.** 판은 가운데에 놓이고 높이는 내용이 정하므로,
   * 도구가 그 값을 다시 세면 판이 자란 날에 엉뚱한 곳을 누르고 아무 말도 하지 않습니다.
   */
  get box(): { x: number; y: number; width: number; height: number } | undefined {
    const top = this.entries[this.entries.length - 1]
    if (!top || top.t < 0.99) return undefined
    const view = top.panel.view
    const size = top.panel.size
    return {
      x: view.x, y: view.y,
      width: size.width * view.scale.x, height: size.height * view.scale.y,
    }
  }

  get busy(): boolean {
    return this.entries.length > 0
  }

  /**
   * 판이 화면을 얼마나 덮고 있는가. 0..1.
   *
   * **덮개의 짙기와 같은 값입니다.** 뒤를 흐리는 쪽이 「판이 떠 있는가」 만 보고 있었고,
   * 그것은 닫는 움직임이 다 끝난 다음에야 거짓이 됩니다 — 판이 줄어들며 사라지는 동안
   * 흐림은 그대로 있다가, 판이 없어진 뒤에 혼자 잦아들었습니다. 덮개가 0 이 되는 순간에
   * 흐림이 아직 남아 있으므로 그 나머지가 뚝 끊기는 것으로 보입니다.
   */
  get cover(): number {
    return this.coverShown
  }

  /** 마지막으로 셈한 덮개의 짙기. `sync` 가 채웁니다. */
  private coverShown = 0

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

    const step = fraction(seconds, 9)

    for (let i = this.entries.length - 1; i >= 0; i--) {
      const entry = this.entries[i]
      entry.panel.tick?.(seconds)
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
      if (entry.panel.covers !== false) cover = Math.max(cover, entry.t)
      this.place(entry)
    }

    this.coverShown = cover
    this.veil.alpha = VEIL * cover
    this.veil.visible = cover > 0.01
    this.visible = this.entries.length > 0
  }

  private place(entry: Entry): void {
    const { view, size } = entry.panel

    // 넘쳤다가 자리에 앉습니다. `t` 가 1에 가까워질수록 넘침이 잦아듭니다.
    const overshoot = Math.sin(Math.min(1, entry.t) * Math.PI) * 0.06
    const back = entry.depth
    const scale = 0.9 + 0.1 * entry.t + overshoot

    // 들어올 때의 떨림. **짧게, 그리고 잦아듭니다** — 오래 떨면 흔들리는 판이 됩니다.
    const shake = entry.rumble * entry.rumble * 5
    const jitterX = shake === 0 ? 0 : (Math.random() - 0.5) * shake
    const jitterY = shake === 0 ? 0 : (Math.random() - 0.5) * shake

    view.scale.set(scale)
    // **자리는 다 나온 크기로 셈합니다.** 커지는 중의 크기로 셈하면 판이 자라는 동안 자리도
    // 함께 움직이고, `popupLeft` 가 왼쪽 변을 한 자리에 붙여 두는 넓은 판에서는 그 움직임이
    // 통째로 옆으로 흐르는 것이 됩니다 — 아래에서 올라오는 것이 아니라 비스듬히 들어오는
    // 것으로 보였습니다.
    //
    // **가로는 화면의 가운데입니다.** 왼쪽 판을 침범하면 그만큼 오른쪽으로 밀립니다 —
    // 규칙은 `popupLeft` 하나이고, 떠 있지 않은 판들(상점 · 끝난 판 · 고르기)도 같은
    // 것을 씁니다.
    const left = entry.panel.centered
      ? Math.round(SIZE.width / 2 - size.width / 2)
      : popupLeft(size.width)
    // **밑변을 맞춥니다.** 판마다 높이가 다르므로 가운데에 놓으면 밑변이 판마다 다른 자리에
    // 있고, 판을 잇달아 열면 그 밑변이 위아래로 움직입니다 — 상점은 바닥에 맞춰 서므로
    // 그것과도 어긋났습니다. 아주 높은 판은 위가 넘치지 않게 그 자리에서 멈춥니다.
    const top = Math.max(8, PANEL_BOTTOM - size.height)
    // **커지는 것은 판의 가운데를 축으로 합니다.** 축이 왼쪽 위 모서리이므로 그만큼을
    // 되돌려 놓습니다 — 그래야 넘침이 좌우로도 위아래로도 고르게 퍼집니다.
    const grow = (1 - scale) / 2
    view.position.set(
      left + size.width * grow + jitterX,
      top + size.height * grow + (1 - entry.t) * 58 + jitterY)
    view.alpha = entry.t * (1 - BACK_FADE * back)
    view.visible = entry.t > 0.01
  }
}

/**
 * 떠 있는 판의 밑변.
 *
 * **모든 판이 이 자리에서 끝납니다.** 상점도 이 자리에 뜹니다 — 값이 두 곳에 적혀 있으면
 * 한쪽을 고칠 때 다른 쪽이 남고, 그러면 판을 바꿀 때마다 밑변이 한두 픽셀 튑니다.
 */
export const PANEL_BOTTOM = SIZE.height - 14

/** 판 머리의 높이. **모든 판이 같습니다** — 제목이 판마다 다른 자리에 있으면 한 벌로 보이지 않습니다. */
export const TITLE_BAR = 56
/** 판 밑단의 높이. 나아가는 줄(`lg`, 60)이 여기 앉습니다. */
export const FOOTER_BAR = 84

/** ESC 키캡의 크기. 구운 그림과 같습니다. */
const KEY_W = 64
const KEY_H = 36
/** 키캡에 적히는 글. 어느 말에서나 같은 키 이름이므로 고정입니다. */
const ESC_KEY = 'ESC'

/**
 * ESC 키캡.
 *
 * **닫기 단추를 따로 두지 않습니다.** 오른쪽 위의 키캡이 그것이고, 무엇을 누르면 닫히는지를
 * 글자가 직접 알립니다.
 */
export function escKey(onClose: () => void): Container {
  const node = new Container()
  const cap = piece('keycap', KEY_W, KEY_H, plateTint(UI.btn))
  if (cap !== undefined) node.addChild(cap)
  else {
    const g = new Graphics()
    g.rect(0, 0, KEY_W, KEY_H).fill(UI.btn)
    node.addChild(g)
  }
  const label = new Text({
    text: ESC_KEY,
    style: { fontSize: TEXT.small, fill: UI.ink, fontWeight: WEIGHT.bold, letterSpacing: 1 },
  })
  label.anchor.set(0.5)
  label.position.set(KEY_W / 2, KEY_H / 2)
  node.addChild(label)
  node.eventMode = 'static'
  node.hitArea = new Rectangle(-8, -8, KEY_W + 16, KEY_H + 16)
  node.cursor = 'pointer'
  node.on('pointerover', () => { if (cap !== undefined) cap.tint = plateTint(UI.btnHover) })
  node.on('pointerout', () => { if (cap !== undefined) cap.tint = plateTint(UI.btn) })
  node.on('pointertap', () => onClose())
  return node
}

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
  // **구워 둔 판 한 장입니다.** 채움과 위 변의 빛과 오른쪽 아래의 잘린 귀가 그 안에 다
  // 있습니다. 그림이 아직 오지 않았으면 지금까지의 길로 그립니다.
  const style = floatingStyle()
  const frame = piece('plate', width, height, plateTint(style.top))
  if (frame === undefined) plate(board, width, height, style)

  // 머리. **제목은 왼쪽 위이고 그 아래가 테두리의 빛입니다** — 왼쪽에서 밝게 시작해
  // 오른쪽으로 사라집니다. 밑단은 단추가 있을 때만 그 위에 선 하나가 놓입니다.
  const bars = new Graphics()
  const headGlow = glowEdge(width - 48, UI.rule)
  if (headGlow !== undefined) headGlow.position.set(24, TITLE_BAR - 2)
  else bars.rect(24, TITLE_BAR - 2, width - 48, 1).fill(UI.rule)

  // **밑단이 없는 판도 있습니다.** 누를 것이 그 판의 내용뿐이면 밑단은 빈 띠일 뿐입니다 —
  // 오른쪽 위의 ESC 와 바깥 누르기로 닫히므로 닫기를 또 둘 이유가 없습니다.
  const footTop = height - FOOTER_BAR
  if (foot) {
    bars.rect(24, footTop, width - 48, 1).fill(UI.hairline)
  }

  const heading = new Text({
    text: title,
    style: { fontSize: TEXT.big, fill: UI.ink, fontWeight: WEIGHT.bold, letterSpacing: 1 },
  })
  heading.anchor.set(0, 0.5)
  heading.position.set(24, TITLE_BAR / 2 - 2)

  node.addChild(board)
  if (frame !== undefined) node.addChild(frame)
  if (headGlow !== undefined) node.addChild(headGlow)
  node.addChild(bars, heading)

  // **닫을 수 없는 판도 있습니다.** 상점이 그렇습니다 — 닫으면 갈 곳이 없으므로 닫기가
  // 없고, 밑단에는 그 판이 할 일이 대신 놓입니다.
  if (onClose === undefined) {
    if (extra) {
      extra.position.set((width - extra.width) / 2, footTop + (FOOTER_BAR - 60) / 2)
      node.addChild(extra)
    }
    node.eventMode = 'static'
    return node
  }

  // 오른쪽 위의 ESC 키캡. **밑단에 닫기가 있으면 둘 다 둡니다** — 창을 닫는 두 손버릇이
  // 다르고, 둘 다 같은 자리에 있으면 어느 쪽으로도 닫힙니다.
  const shutMark = escKey(onClose)
  shutMark.position.set(width - 24 - KEY_W, (TITLE_BAR - KEY_H) / 2 - 2)
  node.addChild(shutMark)

  if (!foot) {
    node.eventMode = 'static'
    return node
  }

  // 밑단의 버튼들. **닫기는 판마다 같은 자리에 같은 모습입니다.**
  //
  // 부르는 쪽은 닫기를 만들지 않습니다 — 여기서 답니다. `extra` 는 그 판이 할 일이고,
  // 닫는 것이 아닙니다.
  // **나아가는 줄입니다 — 전부 `lg`.** 갈래가 달라도 그 줄의 높이는 하나입니다.
  const shut = new Button(t('ui.button.close'), 144, 60, 'neutral', onClose)
  const extraWidth = extra ? extra.width + 12 : 0
  const row = 144 + extraWidth
  // **판보다 넓어지지 않게 잡습니다.** 넘치면 버튼이 판의 좌우로 삐져나가고, 그것은
  // 판이 아니라 부서진 것으로 보입니다.
  const left = Math.max(14, (width - row) / 2)
  if (extra) {
    extra.position.set(left, footTop + (FOOTER_BAR - 60) / 2)
    node.addChild(extra)
  }
  shut.position.set(left + extraWidth, footTop + (FOOTER_BAR - 60) / 2)
  node.addChild(shut)

  node.eventMode = 'static'
  return node
}
