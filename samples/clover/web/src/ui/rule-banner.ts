// 규칙이 바뀐 것을 알리는 판.
//
// **토스트로 보내지 않습니다.** 조커가 걸고 소모품이 걸고 보스가 거는 규칙은 그 판의
// 셈법을 통째로 바꾸는 것인데, 화면 오른쪽 구석의 작은 글 두 줄로는 지나가는 알림으로
// 읽힙니다 — 손패 줄 바로 위 가운데에 판 하나로 뜹니다.
//
// **한 번에 하나입니다.** 규칙은 한 액션에 여럿 걸릴 수 있고(챌린지 · 보스 · 바우처),
// 그것들이 판 여럿으로 뜨면 어느 것이 방금 온 것인지 알 수 없습니다 — 한 판에 담고
// 넘치면 몇 개가 더 있는지만 적습니다.
//
// **왼쪽 판의 「적용 중」 목록과 하는 일이 다릅니다.** 목록은 지금 걸려 있는 것이고 이
// 판은 그것이 방금 얼마에서 얼마로 달라졌는가입니다 — 목록은 그 델타를 담을 수 없습니다.
// 그래서 **바뀐 값이 크고 규칙의 이름이 그 위에 작게** 놓입니다. 이름을 크게 두면 목록의
// 한 줄과 같은 모양이 되고, 그러면 같은 글이 두 번 적힌 것으로 읽힙니다.

import { Container, Graphics, Text } from 'pixi.js'

import { SETTLE_SECONDS, settle } from '../render/motion'
import { plate, panelStyle, plateTint } from '../render/skin'
import { TEXT, UI, WEIGHT, SIZE } from '../render/theme'
import { outlined } from './font'
import { glowEdge, piece } from './chrome'
import { richLine, richStyle } from './rich'

/** 규칙 하나가 어떻게 바뀌었는가. */
export interface RuleNote {
  /** 규칙의 이름. 글 표의 `rule.<이름>.name` 입니다. */
  title: string
  /** 무엇이 어떻게 바뀌었는가. `8  →  10` 처럼 적힙니다. */
  change: string
  /** 좋아진 것인가. 색이 갈립니다. */
  good: boolean
}

/** 판의 넓이. **손패보다 좁습니다** — 판 위에 얹힌 것이지 판을 덮는 것이 아닙니다. */
/**
 * 배너의 폭. **화면보다 넓습니다** — 양 끝이 화면 밖으로 나갑니다. 화면 안에 갇힌 띠는
 * 판으로 읽힙니다.
 */
const WIDTH = SIZE.width + 240
const PAD = 16
/** 줄 하나의 높이. 이름이 작게 위에, 바뀐 값이 크게 아래에 놓입니다. */
const ROW = 60
/** 한 판에 적는 규칙의 수. 넘치면 몇 개가 더 있는지만 적습니다. */
const ROWS = 4

/** 뜨고 · 머물고 · 걷히는 세 마디. 초입니다. */
const RISE = 0.22
const HOLD = 2.6
const FALL = 0.4

/**
 * 손패 줄 위에 뜨는 알림 판.
 *
 * 자리는 부르는 쪽이 정합니다 — 손패의 윗변이 판의 크기에 따라 달라지므로, 화면이 그
 * 값을 알고 있습니다.
 */
export class RuleBanner extends Container {
  private readonly board = new Graphics()
  private readonly body = new Container()
  /** 왼쪽에서 오른쪽으로 차오르는 띠. **무엇이 걸렸는지가 아니라 언제 걷히는지입니다.** */
  private readonly timer = new Graphics()

  /** 0 에서 1 로 들었다가 0 으로 돌아갑니다. */
  private enter = 0
  private life = 0
  /** 판의 세로 길이. `Container.height` 와 겹치지 않게 따로 셉니다. */
  private tall = 0
  /** 머리글에 적은 것. 검증 도구가 묻는 값입니다. */
  private headText = ''
  private showing = false

  constructor() {
    super()
    this.addChild(this.board, this.timer, this.body)
    this.visible = false
    this.eventMode = 'none'
  }

  /**
   * 판을 세웁니다. **이미 서 있으면 갈아 끼웁니다** — 앞의 것을 기다리게 하면 방금 걸린
   * 규칙이 몇 초 뒤에 뜹니다.
   *
   * @param from 무엇이 걸었는가. 조커 · 소모품 · 보스의 이름입니다.
   */
  show(notes: readonly RuleNote[], from?: string): void {
    if (notes.length === 0) return
    this.body.removeChildren().forEach(child => child.destroy())

    const style = richStyle('body')
    const shown = notes.slice(0, ROWS)
    let y = PAD

    // 머리글 — 무엇이 걸었는가. **없으면 두지 않습니다.**
    this.headText = from ?? ''
    if (from !== undefined && from !== '') {
      const head = new Text({
        text: from,
        style: { ...outlined(TEXT.mini, UI.outline), fill: UI.inkDim, fontWeight: WEIGHT.bold },
      })
      head.anchor.set(0.5, 0)
      head.position.set(WIDTH / 2, y)
      this.body.addChild(head)
      y += TEXT.mini + 8
    }

    for (const note of shown) {
      // 규칙의 이름은 작게 위에. **이것은 왼쪽 목록에도 있는 것입니다** — 여기서 크게
      // 두면 목록의 한 줄과 같은 모양이 되고, 그러면 되풀이로 읽힙니다.
      const name = new Text({
        text: note.title,
        style: { ...outlined(TEXT.mini, UI.outline), fill: UI.inkDim, fontWeight: WEIGHT.bold },
      })
      name.anchor.set(0.5, 0)
      name.position.set(WIDTH / 2, y)

      // 바뀐 값이 주인공입니다. **목록이 담을 수 없는 것이 이것입니다.**
      // **수까지 그 색입니다.** 강조의 규칙은 수를 칩의 파랑으로 두는 것인데, 여기서는
      // 좋아졌는지 나빠졌는지가 그 색이므로 파랑으로 두면 그 뜻이 사라집니다.
      const tint = note.good ? UI.good : UI.bad
      const value = richLine(note.change, {
        ...style,
        number: tint,
        term: tint,
        base: { ...style.base, fontSize: TEXT.base, fill: tint, fontWeight: WEIGHT.bold },
      }, undefined, 36)
      value.position.set((WIDTH - value.width) / 2, y + TEXT.mini + 6)

      this.body.addChild(name, value)
      y += ROW
    }

    if (notes.length > shown.length) {
      // **말이 아니라 수입니다.** 「외 2개」를 적으려면 글 표에 줄이 하나 더 있어야 하고,
      // `+2` 는 어느 말로도 같습니다.
      const more = new Text({
        text: `+${notes.length - shown.length}`,
        style: { ...outlined(TEXT.mini, UI.outline), fill: UI.inkDim, fontWeight: WEIGHT.bold },
      })
      more.anchor.set(0.5, 0)
      more.position.set(WIDTH / 2, y + 2)
      this.body.addChild(more)
      y += TEXT.mini + 6
    }

    this.tall = y + PAD
    this.redraw()

    this.enter = 0
    this.life = 0
    this.showing = true
    this.visible = true
  }

  /**
   * 서둘러 걷습니다.
   *
   * **그 자리를 다른 것이 쓸 때입니다.** 바뀌는 카드가 판 위로 나오는 자리가 여기와
   * 겹치므로, 겹치는 동안 카드가 판 뒤로 들어갑니다.
   */
  dismiss(): void {
    this.showing = false
  }

  /** 머리글에 적힌 것. */
  get head(): string {
    return this.headText
  }

  /** 서 있는가. 다른 것이 그 자리를 쓰려면 물어야 합니다. */
  get up(): boolean {
    return this.showing || this.enter > 0.01
  }

  private redraw(): void {
    this.board.clear()
    this.board.removeChildren().forEach(child => child.destroy())
    // **구운 판 한 장이고 위 변은 테두리의 빛입니다.** 색이 곧 갈래입니다 — 규칙은 값의
    // 색을 씁니다.
    const skin = piece('plate', WIDTH, this.tall, plateTint(UI.panel))
    if (skin !== undefined) this.board.addChild(skin)
    else plate(this.board, WIDTH, this.tall, panelStyle())
    const glow = glowEdge(WIDTH, UI.money)
    if (glow !== undefined) this.board.addChild(glow)
    else this.board.rect(0, 0, WIDTH, 3).fill({ color: UI.money, alpha: 0.9 })
  }

  advance(seconds: number): void {
    if (!this.visible) return

    // 들었다가 머물다가 걷힙니다. **드는 것이 빠르고 걷히는 것이 느립니다** — 방금 걸린
    // 것은 곧바로 보여야 하고, 사라지는 것은 눈이 따라갈 만해야 합니다.
    if (this.showing) {
      // **가운데에서 양쪽으로 펴집니다.** 곡선 하나, 0.56초.
      this.enter = Math.min(1, this.enter + seconds / SETTLE_SECONDS)
      this.life += seconds
      if (this.life >= RISE + HOLD) this.showing = false
    } else {
      this.enter -= seconds / FALL
      if (this.enter <= 0) {
        this.enter = 0
        this.visible = false
        return
      }
    }

    // 가운데에서 양쪽으로 펴지며 짙어집니다. 걷힐 때는 그 길로 돌아갑니다.
    const open = settle(this.enter)
    this.alpha = Math.min(1, open * 1.6)
    this.pivot.set(WIDTH / 2, this.tall)
    this.scale.set(Math.max(0.001, open), 1)
    this.body.y = 0

    // 남은 시간의 띠. **왼쪽에서 오른쪽으로 줄어듭니다.**
    const left = Math.max(0, 1 - Math.max(0, this.life - RISE) / HOLD)
    this.timer.clear()
    if (left > 0) {
      this.timer.rect(PAD, this.tall - 5, (WIDTH - PAD * 2) * left, 2)
        .fill({ color: UI.money, alpha: 0.5 })
    }
  }

  /** 판이 서는 자리. 넓이의 절반이 피벗이므로 가운데 `x` 와 아랫변 `y` 입니다. */
  place(x: number, bottom: number): void {
    this.position.set(x, bottom)
  }
}

/** 판이 화면에서 차지하는 사각형. 도구가 자리를 묻는 값입니다. */
export function bannerBox(banner: RuleBanner): { x: number; y: number
                                                 width: number; height: number } | undefined {
  if (!banner.visible) return undefined
  const bounds = banner.getBounds()
  return {
    x: Math.round(bounds.x), y: Math.round(bounds.y),
    width: Math.round(bounds.width), height: Math.round(bounds.height),
  }
}
