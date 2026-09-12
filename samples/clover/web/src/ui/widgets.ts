// 화면의 조각들.
//
// 그리는 규칙은 `render/skin.ts` 에 있고 여기는 그것을 쓰는 자리입니다. **버튼과 패널이 같은
// 손으로 그려져야 화면이 한 벌로 보입니다.**

import { PAINT } from '../render/ink'
import { Container, Graphics, Rectangle, Sprite, Text } from 'pixi.js'

import { contrast } from '../render/color'
import type { Surface } from '../render/palette'
import { LIP, mix, panelStyle, plate, plateTint, pressable,
         type ButtonLook, type PlateStyle } from '../render/skin'
import { UI, TEXT, WEIGHT } from '../render/theme'
import { outlined, outlineOf, outlineWidth, strokeWidthOf } from './font'
import { iconFor, type IconName } from './icon'
import { piece, rungFor, rungOf } from './chrome'
import type { RungName } from './atlas'

/**
 * 판.
 *
 * **구워 둔 그림 한 장입니다.** 채움과 위 변의 빛과 오른쪽 아래의 잘린 귀가 그 안에 다
 * 있습니다 — 코드로 그린 채움과 테는 잘 만든 웹 화면의 문법이고, 이 게임의 그림 화풍과는
 * 어긋납니다.
 *
 * 그림이 아직 오지 않았으면 지금까지의 길로 그립니다. 첫 프레임에 판이 사라지는 것보다
 * 낫습니다.
 */
export class Panel extends Container {
  private readonly board = new Graphics()
  private skin?: Container

  constructor(width: number, height: number, tint?: number) {
    super()
    this.addChild(this.board)
    this.resize(width, height, tint)
  }

  resize(width: number, height: number, tint?: number): void {
    this.skin?.destroy()
    this.skin = piece('plate', width, height, plateTint(tint ?? panelStyle().top))
    this.board.clear()
    if (this.skin !== undefined) {
      this.addChildAt(this.skin, 0)
      this.alpha = panelStyle().alpha ?? 1
      return
    }

    const border = panelStyle().border
    const style: PlateStyle = tint === undefined
      ? { ...panelStyle(), border }
      : { ...panelStyle(), top: mix(tint, PAINT.sheen, 0.1), bottom: tint, border }
    plate(this.board, width, height, style)
  }
}

/**
 * 누른 자리에서 이만큼 움직이면 끈 것입니다. 화면 픽셀입니다.
 *
 * **굴리는 판 안의 단추를 위한 것입니다.** 목록은 줄로 가득하고 그 줄이 단추이므로,
 * 손가락으로 굴려 손을 떼는 자리는 언제나 어느 단추 위입니다 — 가리지 않으면 굴릴 때마다
 * 무언가가 눌립니다.
 */
const DRAG_SLOP = 12

/**
 * 단추가 무엇을 하는 단추인가.
 *
 * **색이 아니라 이것을 받습니다.** 색을 수로 받던 동안은 만들 때의 수가 앞 겉면의 색이라,
 * 그 수를 겉면의 색들과 견주어 어느 이름이었는지 되찾아야 했습니다 — 되찾기가 어긋나면 그
 * 단추만 옛 색으로 남고, 되찾는 목록에 없는 색은 겉면을 아예 따라가지 않았습니다.
 *
 * 이름을 받으면 그 일이 없어집니다. 그릴 때마다 지금 겉면에서 읽습니다.
 */
export type Intent =
  /** 그 밖의 단추. 닫기 · 메뉴 · 타이틀로 · 정렬입니다. */
  | 'neutral'
  /** 나아가는 단추. 시작 · 사기 · 다음 블라인드입니다. */
  | 'primary'
  /** 되돌릴 수 없는 것. 팔기 · 버리기 · 지우기입니다. */
  | 'danger'
  /** 걸어 보는 것. 블라인드를 건너뜁니다. */
  | 'dare'
  /** 묻는 판의 「그렇게 합니다」. 되돌릴 수 있는 쪽입니다. */
  | 'confirm'
  /** 되돌릴 수 없는 일의 첫 누름. 두 번째 누름에서 `danger` 로 갑니다. */
  | 'caution'
  /** 고른 탭 · 밝은 단추. */
  | 'select'
  /** 판 위에 조용히 놓이는 것. 곁들이는 단추입니다. */
  | 'quiet'

/**
 * 지금 화면에 붙어 있는 단추들.
 *
 * **이름으로 세어 두지 않습니다.** 겉면을 갈아입은 뒤 다시 그릴 단추 10개를 손으로 적어
 * 두었고, 붙박이 단추를 하나 더할 때 그 목록에 더하는 것을 잊으면 그 단추만 앞 겉면의 색으로
 * 남았습니다 — 그것을 확인하는 게이트가 없습니다.
 *
 * 무대에 붙고 떨어지는 것을 받아 두면 목록이 저절로 맞습니다.
 */
const LIVE = new Set<Restyleable>()

interface Restyleable {
  restyle(): void
}

/** 겉면을 갈아입은 뒤 화면에 남아 있는 단추를 전부 다시 그립니다. */
export function restyleButtons(): void {
  for (const one of LIVE) one.restyle()
}

/**
 * 갈래마다 쉴 때 · 가리켰을 때 · 눌렸을 때의 채움, 그리고 테.
 *
 * **판 계열의 갈래만 테를 가집니다.** 뜻이 있는 색은 그 색이 이미 모양을 잡으므로 테 대신
 * 두께를 가집니다 — `skin.ts` 의 `pressable`.
 */
interface Look {
  rest: keyof Surface
  hover: keyof Surface
  press: keyof Surface
  edge?: keyof Surface
  edgeHover?: keyof Surface
}

const INTENTS: Record<Intent, Look> = {
  neutral: { rest: 'btn', hover: 'btnHover', press: 'btnPress',
             edge: 'btnEdge', edgeHover: 'btnEdgeHover' },
  quiet: { rest: 'quiet', hover: 'quietHover', press: 'quietPress',
           edge: 'quietEdge', edgeHover: 'btnEdge' },
  primary: { rest: 'yellow', hover: 'yellowHover', press: 'yellowPress' },
  danger: { rest: 'red', hover: 'redHover', press: 'redPress' },
  dare: { rest: 'dare', hover: 'dareHover', press: 'darePress' },
  confirm: { rest: 'confirm', hover: 'confirmHover', press: 'confirmPress' },
  caution: { rest: 'caution', hover: 'cautionHover', press: 'cautionPress' },
  select: { rest: 'light', hover: 'lightHover', press: 'lightPress' },
}

/**
 * 단추 위의 글을 어느 색으로 적는가.
 *
 * **재어서 고릅니다.** 밝기 한 값으로 가르던 동안은 그 문턱에 걸친 단추 — 붉음과 초록이
 * 그렇습니다 — 가 겉면마다 다른 쪽으로 넘어갔습니다.
 *
 * **흰 쪽으로 기울여 둡니다.** 어두운 글이 15% 넘게 더 잘 읽힐 때에만 그쪽입니다 — 두 값이
 * 비슷하면 흰 글이 단추의 관례이고, 게임 안에서도 그 편이 한 벌로 보입니다.
 */
function captionInk(base: number): number {
  return contrast(UI.onLight, base) > contrast(UI.ink, base) * 1.15 ? UI.onLight : UI.ink
}

/**
 * 단추가 내려앉는 깊이.
 *
 * 구워 둔 그림의 아래 턱과 같은 값입니다. 누르면 얼굴이 이만큼 내려가 턱에 얹히므로
 * 실루엣의 아랫변은 제자리에 남습니다.
 */
const SINK = 3

/**
 * 상태가 건너가는 시간. 초입니다.
 *
 * **한 프레임에 바뀌면 눌린 것인지 잠긴 것인지가 갈립니다.** 활성과 비활성, 색이 바뀌는
 * 것, 글이 바뀌는 것이 전부 이 시간에 건너갑니다.
 */
const CROSS = 0.16

/** 지금 화면에 붙어 있는 단추들. 프레임마다 건너가는 것을 한 걸음 옮깁니다. */
const TICKING = new Set<Button>()

export class Button extends Container {
  private readonly board = new Graphics()
  private skin?: Container
  /** 그림이 지금 띤 색과 가려는 색. 건너가는 동안 그 사이입니다. */
  private tintNow?: number
  private tintWant?: number
  /** 물러나는 옛 글. 올라가며 옅어지고, 다 지면 지웁니다. */
  private fading?: { text: Text; left: number }
  /** 새 글이 드는 정도. 0 이면 아래에서 시작하고 1 이면 제자리입니다. */
  private rising = 1
  /** 이 단추가 서 있는 높이의 칸. 계단 넷 안에서만 고릅니다. */
  private readonly rung: RungName
  private readonly caption = new Text({
    text: '',
    style: {
      ...outlined(TEXT.base, UI.outline),
      fill: UI.ink, fontWeight: WEIGHT.bold,
    },
  })

  private enabledState = true
  /** 이 누름이 시작된 화면의 자리. 끌기와 누르기를 가르는 데 씁니다. */
  private downAt?: { x: number; y: number }
  /** 마지막으로 적은 글. 같은 글을 다시 적지 않기 위한 것입니다. */
  private captionShown?: string
  private lit = false

  /** 아무 버튼이나 눌렸을 때. 소리를 내는 쪽이 겁니다. */
  static onPressed?: () => void

  /**
   * **글자 크기를 받지 않습니다.** 높이의 칸이 글자를 정합니다 — 36은 12, 48과 60은 24,
   * 72는 36입니다. 부르는 자리마다 고르게 두었더니 한 화면에 15 · 16 · 18 · 19가 함께
   * 놓였고, 그 차이는 나란히 보아야만 보입니다.
   */
  constructor(text: string, private readonly boxWidth: number,
              private readonly boxHeight: number,
              private readonly intent: Intent, onPress: () => void) {
    super()
    this.rung = rungFor(boxHeight)
    this.textSize = rungOf(this.rung).font
    this.caption.style.fontSize = this.textSize
    this.addChild(this.board, this.caption)
    this.caption.anchor.set(0.5)
    this.caption.position.set(boxWidth / 2, this.captionY(false))
    this.text = text

    this.eventMode = 'static'
    this.cursor = 'pointer'
    // **버튼 소리는 여기 한 자리입니다.** 부르는 쪽마다 걸면 새로 만드는 버튼에서 반드시
    // 하나가 빠지고, 그 버튼만 소리 없이 눌립니다.
    this.on('pointertap', event => {
      if (!this.enabledState) return
      // 끌고 와서 이 단추 위에서 손을 뗀 것이면 누른 것이 아닙니다.
      const from = this.downAt
      this.downAt = undefined
      if (from) {
        const dx = event.global.x - from.x
        const dy = event.global.y - from.y
        if (dx * dx + dy * dy > DRAG_SLOP * DRAG_SLOP) return
      }
      Button.onPressed?.()
      onPress()
    })
    this.on('pointerover', () => { if (this.enabledState) this.setLit(true) })
    this.on('pointerout', () => this.setLit(this.held))
    this.on('pointerdown', event => {
      this.downAt = { x: event.global.x, y: event.global.y }
      if (!this.enabledState) return
      this.setPushed(true)
    })
    // **밖에서 손을 떼는 것도 받습니다.** 누른 채로 단추를 벗어나면 `pointerup` 이 오지
    // 않고, 그러면 그 단추만 눌린 색으로 남습니다.
    this.on('pointerup', () => this.release())
    this.on('pointerupoutside', () => this.release())
    this.on('added', () => { LIVE.add(this); TICKING.add(this) })
    this.on('removed', () => { LIVE.delete(this); TICKING.delete(this) })
    this.draw()
  }

  /**
   * 화면의 모든 단추를 한 걸음 옮깁니다. **게임의 시계가 프레임마다 부릅니다.**
   *
   * 색은 가려는 색으로 미끄러지고, 옛 글은 올라가며 옅어지고, 새 글은 아래에서 듭니다.
   */
  static advanceAll(seconds: number): void {
    for (const one of TICKING) one.step(seconds)
  }

  private step(seconds: number): void {
    const k = Math.min(1, seconds / CROSS)
    if (this.skin !== undefined && this.tintWant !== undefined && this.tintNow !== undefined
        && this.tintNow !== this.tintWant) {
      this.tintNow = mix(this.tintNow, this.tintWant, k)
      // 8비트로 반올림한 뒤 같으면 다 간 것입니다.
      if (mix(this.tintNow, this.tintWant, 0.5) === this.tintWant) this.tintNow = this.tintWant
      ;(this.skin as { tint: number }).tint = this.tintNow
    }
    if (this.fading !== undefined) {
      this.fading.left -= seconds
      const gone = 1 - Math.max(0, this.fading.left) / CROSS
      this.fading.text.alpha = 1 - gone
      this.fading.text.y = this.captionY(this.pushed) - gone * 10
      if (this.fading.left <= 0) {
        this.fading.text.destroy()
        this.fading = undefined
      }
    }
    if (this.rising < 1) {
      this.rising = Math.min(1, this.rising + seconds / CROSS)
      this.caption.alpha = this.enabledState ? this.rising : this.rising * 0.5
      this.caption.y = this.captionY(this.pushed) + (1 - this.rising) * 10
    }
  }

  /** 이 칸의 글자 크기. 글이 길어 줄였다가 되돌릴 때 씁니다. */
  private textSize = 24

  /**
   * 단추에 적히는 글.
   *
   * **칸을 넘치면 글자를 줄입니다.** 말마다 길이가 다르므로 한국어에 맞춘 칸이 독일어에서
   * 넘칩니다 — 「Plasma-Deck · Violetter Einsatz」 가 200픽셀 칸의 양쪽으로 삐져나와 있었고,
   * 넘친 글은 잘리지도 않고 옆의 단추 위에 그려집니다.
   *
   * 칸을 넓히는 것으로는 끝나지 않습니다. 어느 말이 가장 긴지는 데이터가 정하고, 덱 15종과
   * 스테이크 8종의 조합이므로 가장 긴 것을 미리 셀 수도 없습니다.
   */
  set text(value: string) {
    // **같은 글이면 손대지 않습니다.** 아래의 줄이기가 글자 크기를 바꿀 때마다 글을 다시
    // 굽고, 조커 풀은 쪽을 넘길 때마다 단추 7개에 같은 글을 다시 적습니다.
    if (value === this.captionShown) return
    // **옛 글이 올라가며 옅어지고 새 글이 아래에서 듭니다.** 처음 적는 글은 그냥 놓입니다.
    if (this.captionShown !== undefined && this.caption.text !== '') {
      this.fading?.text.destroy()
      const old = new Text({ text: this.caption.text, style: this.caption.style.clone() })
      old.anchor.set(0.5)
      old.position.set(this.caption.x, this.caption.y)
      old.alpha = this.caption.alpha
      this.addChild(old)
      this.fading = { text: old, left: CROSS }
      this.rising = 0
      this.caption.alpha = 0
    }
    this.captionShown = value
    this.caption.style.fontSize = this.textSize
    this.caption.text = value

    // 양쪽에 8픽셀씩 남깁니다. 글이 테두리에 닿으면 칸이 터진 것으로 보입니다.
    //
    // **줄일 때에도 계단을 밟습니다.** 한 픽셀씩 내리면 12의 배수를 벗어나 획이 격자에서
    // 어긋납니다 — 픽셀 서체는 그 사이 값에서 굵기가 자리마다 달라집니다.
    const room = this.boxWidth - 16
    let size = this.textSize
    while (size > 12 && this.caption.width > room) {
      size -= 12
      this.caption.style.fontSize = size
    }

    // **테두리를 여기서 다시 정합니다.** 굵기는 글자 크기와 고른 말에서 나오는 값이고 둘
    // 다 여기서 바뀝니다 — 위의 줄이기가 크기를 9까지 내리고, 말이 바뀌면 `relabel` 이
    // 이 자리로 새 글을 넣습니다.
    this.applyInk()
  }

  /**
   * 글의 색과 테두리.
   *
   * **단추의 밝기가 정합니다.** 노랑 · 하늘 · 크림 위에 흰 글을 검은 테로 두르면 읽히지
   * 않으므로, 밝은 단추의 글은 어둡고 테가 없습니다.
   *
   * **글자 크기도 봅니다.** 테두리의 굵기는 크기에서 나오는 값이고, 긴 글은 위의 줄이기가
   * 크기를 9까지 내립니다.
   */
  private applyInk(): void {
    // **되돌릴 수 없는 단추의 글은 밝은 붉음입니다.** 짙은 붉음 채움 위에 흰 글을 얹으면
    // 경고판이 됩니다. 잠긴 것은 잠긴 것의 글색입니다.
    const ink = this.intent === 'danger' && this.enabledState && !this.held
      ? UI.bad
      : captionInk(this.shownBase)
    this.caption.style.fill = ink
    const size = this.caption.style.fontSize as number
    const width = ink === UI.onLight ? 0 : outlineWidth(size)
    this.caption.style.stroke = outlineOf(width, UI.outline)
  }

  /** 지금 글에 걸려 있는 테두리의 굵기. **검증 도구가 읽습니다.** */
  get inkWidth(): number {
    return strokeWidthOf(this.caption)
  }

  set enabled(value: boolean) {
    if (this.enabledState === value) return
    this.enabledState = value
    // **잠긴 단추는 옅어지는 것이 아니라 회색이 됩니다.** 밝은 단추를 알파로 죽이면 뒤의
    // 배경이 그 색에 섞여 노랑이 흙색으로, 붉음이 자주색으로 보입니다 — 잠긴 것은 잠긴
    // 것의 색을 가져야 합니다.
    this.alpha = 1
    this.caption.alpha = value ? 1 : 0.5
    this.cursor = value ? 'pointer' : 'default'
    this.draw()
  }

  /**
   * 눌린 채로 두는 것.
   *
   * **탭에 씁니다** — 지금 보고 있는 탭이 어느 것인지가 보이지 않으면 그것은 탭이 아니라
   * 버튼 줄입니다. 마우스를 올렸을 때와 같은 모습이라 따로 배울 것이 없습니다.
   */
  set highlight(value: boolean) {
    this.held = value
    this.setLit(value)
  }

  private held = false

  private setLit(value: boolean): void {
    if (this.held && !value) return
    if (this.lit === value) return
    this.lit = value
    this.draw()
  }

  /**
   * 지금 그릴 색.
   *
   * **고른 탭은 크림입니다.** 참고의 탭이 그렇고, 그러면 어느 탭을 보고 있는지가 밝기
   * 하나로 갈립니다 — 같은 색을 조금 밝히는 것으로는 고른 것이 드러나지 않습니다.
   * 잠긴 것은 잠긴 색을 가집니다.
   */
  /**
   * 지금 그릴 모습.
   *
   * **색이 아니라 이름을 들고 있으므로 지금 겉면에서 읽습니다.**
   */
  private get shown(): ButtonLook {
    const look = INTENTS[this.intent]
    if (!this.enabledState) return { face: UI.locked, edge: UI.lockedEdge }
    if (this.held) return { face: UI.light }
    const face = this.pushed ? UI[look.press] : this.lit ? UI[look.hover] : UI[look.rest]
    if (look.edge === undefined) return { face }
    return { face, edge: this.lit ? UI[look.edgeHover ?? look.edge] : UI[look.edge] }
  }

  private get shownBase(): number {
    return this.shown.face
  }

  /** 지금 눌려 있는가. 손을 뗄 때까지입니다. */
  private pushed = false

  private setPushed(value: boolean): void {
    if (this.pushed === value) return
    this.pushed = value
    // 글도 얼굴을 따라 내려앉습니다. **얼굴만 내려가면 글이 턱 위에 떠 있습니다.**
    this.caption.y = this.captionY(value)
    this.draw()
  }

  private release(): void {
    this.setPushed(false)
  }

  /**
   * 글이 앉는 높이.
   *
   * **테가 있는 단추는 가운데입니다.** 두께가 없으므로 누를 때 1픽셀만 내려갑니다 —
   * 두꺼운 단추는 얼굴이 턱 안으로 내려앉는 만큼 함께 갑니다.
   */
  private captionY(pushed: boolean): number {
    if (this.skin !== undefined || piece === undefined) {
      const tall = rungOf(this.rung).height
      return tall / 2 + (pushed ? SINK / 2 : 0)
    }
    const flat = INTENTS[this.intent].edge !== undefined || !this.enabledState
    if (flat) return this.boxHeight / 2 + (pushed ? 1 : 0)
    return (this.boxHeight - LIP) / 2 + (pushed ? LIP : 0)
  }

  /**
   * 겉면이 바뀌었을 때 다시 그립니다.
   *
   * **판때기는 그릴 때의 색으로 삼각화되어 있습니다.** 그래서 색만 갈아 끼워도 이미 그려
   * 둔 단추는 앞 겉면의 색으로 남습니다 — 화면에 오래 남아 있는 단추들이 그렇습니다.
   */
  restyle(): void {
    this.draw()
  }

  private draw(): void {
    this.board.clear()

    // **회색조 그림 한 장에 갈래의 색을 물들입니다.** 갈래 8 × 상태 4 를 그림으로 두면
    // 32장이 되므로, 그림은 칸마다 한 장이고 색은 지금까지의 토큰에서 그대로 옵니다.
    //
    // 누르면 얼굴이 턱 위로 내려앉습니다 — 그만큼 낮게, 그만큼 아래에 놓습니다. 아랫변은
    // 제자리에 남으므로 단추가 자리를 옮기지 않습니다.
    this.skin?.destroy()
    const tall = rungOf(this.rung).height
    const sunk = this.pushed ? SINK : 0
    // **색은 건너갑니다.** 새 그림은 지금 띤 색으로 놓고, 가려는 색은 `step` 이 옮깁니다.
    this.tintWant = this.shown.face
    if (this.tintNow === undefined || !TICKING.has(this)) this.tintNow = this.tintWant
    this.skin = piece(this.rung, this.boxWidth, tall - sunk, this.tintNow)
    if (this.skin !== undefined) {
      // `piece()` 가 그림자 여백만큼 물러앉혀 두었으므로 그 위에 더합니다 — 덮어쓰면 얼굴이
      // 여백만큼 아래로 내려가 글이 얼굴의 윗변에 붙습니다.
      this.skin.y += sunk
      this.addChildAt(this.skin, 0)
      this.applyInk()
      return
    }

    pressable(this.board, this.boxWidth, this.boxHeight, this.shown, this.pushed)
    this.applyInk()
  }
}

/**
 * 아이콘 하나짜리 버튼.
 *
 * **글이 없습니다.** 화면 구석에 서는 것들이라, 글을 넣으면 그 글의 길이가 자리를 정하고
 * 말이 바뀌는 날마다 배치가 흔들립니다 — 물음표와 톱니는 어느 말에서나 같은 것을 뜻합니다.
 *
 * 아이콘은 **가져온 것**입니다(`ui/icon.ts`). 직접 그려 보았는데 톱니가 해처럼 보였습니다.
 */
/**
 * 아이콘 하나짜리 단추.
 *
 * **판때기가 없습니다.** 네모 칸에 담아 두었더니 글이 있는 단추들과 같은 무게로 서서, 판
 * 바깥의 일인 도움말과 옵션이 게임의 단추들과 한 벌로 보였습니다 — 아이콘만 두면 그 둘이
 * 화면의 구석에 놓인 표시가 됩니다.
 *
 * **누르는 자리는 그대로 넓습니다.** 보이는 것이 작아졌다고 맞혀야 하는 자리까지 작아지면
 * 손가락으로는 누르지 못합니다.
 */
export class IconButton extends Container {
  private readonly mark?: Sprite
  private lit = false

  constructor(private readonly box: number, icon: IconName, onPress: () => void) {
    super()

    const texture = iconFor(icon)
    if (texture) {
      // **칸이 없으므로 그만큼 큽니다.** 테두리에 붙어 답답할 자리가 없어졌습니다.
      const size = box * 0.66
      this.mark = new Sprite(texture)
      this.mark.width = size
      this.mark.height = size
      this.mark.position.set((box - size) / 2, (box - size) / 2)
      this.addChild(this.mark)
    }

    this.eventMode = 'static'
    this.cursor = 'pointer'
    this.hitArea = new Rectangle(0, 0, box, box)
    this.on('pointertap', () => {
      Button.onPressed?.()
      onPress()
    })
    this.on('pointerover', () => this.setLit(true))
    this.on('pointerout', () => this.setLit(false))
    this.on('pointerdown', () => { if (this.mark) this.mark.y += 2 })
    this.on('pointerup', () => this.place())
    this.on('added', () => LIVE.add(this))
    this.on('removed', () => LIVE.delete(this))
    this.draw()
  }

  private place(): void {
    if (!this.mark) return
    this.mark.y = (this.box - this.mark.height) / 2
  }

  private setLit(value: boolean): void {
    if (this.lit === value) return
    this.lit = value
    this.place()
    this.draw()
  }

  /** 겉면이 바뀌었을 때 다시 그립니다. 단추와 같은 이유입니다. */
  restyle(): void {
    this.draw()
  }

  private draw(): void {
    // 밝기 하나로만 답합니다. **가리킨 것이 밝아지는 것은 다른 단추들과 같습니다.**
    if (this.mark) this.mark.tint = this.lit ? UI.ink : UI.inkDim
  }
}
