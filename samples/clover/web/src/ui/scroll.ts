// 넘치는 것을 굴려 보는 자리.
//
// **Pixi 에 스크롤이 없습니다.** 그래서 목록이 판보다 길어지는 순간 남는 것이 그냥 판
// 밖으로 나가고, 그것은 화면에서 잘린 것으로 보이지 않습니다 — 없는 것으로 보입니다.
// 조커 500종의 판이 쪽 넘김으로 그것을 피했고, 여기는 굴립니다.
//
// **쪽 넘김이 아니라 굴림인 이유**는 목록이 고르는 자리이기 때문입니다. 쪽을 넘기면 지금
// 고른 것이 어느 쪽에 있었는지를 사람이 기억해야 합니다.

import { Container, Graphics, type FederatedWheelEvent, type FederatedPointerEvent }
  from 'pixi.js'

/** 막대의 굵기. */
const BAR_W = 5

/** 한 번 굴릴 때 움직이는 거리. */
const STEP = 52

/** 손가락이나 마우스로 끌 때, 이만큼 움직이면 누른 것이 아니라 끈 것입니다. */
const DRAG_SLOP = 6

/** 끝을 넘어 끌 수 있는 거리. 넘긴 만큼은 손가락보다 덜 따라옵니다. */
const OVER_PULL = 72
/** 끝을 넘긴 자리에서 손가락이 따라오는 비율. */
const OVER_GRIP = 0.4
/** 손을 뗀 뒤 관성이 잦아드는 빠르기. */
const GLIDE_DECAY = 5.5
/** 끝을 넘긴 것이 되돌아오는 빠르기. */
const SNAP_BACK = 16
/** 이보다 느리면 멈춘 것으로 봅니다. 초당 픽셀. */
const GLIDE_STOP = 12

/**
 * 끌기와 관성과 되돌아옴.
 *
 * **굴리는 자리가 둘입니다** — `ScrollView` 와 옵션 판입니다. 손끝의 느낌이 그 둘에서
 * 다르면 같은 게임의 다른 판이 서로 다른 물건으로 보이므로, 셈은 여기 하나입니다.
 *
 * **손가락에는 바퀴가 없습니다.** 끌어서 굴리고, 놓으면 미끄러지고, 끝을 넘기면 되돌아오는
 * 것 셋이 있어야 손가락으로 굴리는 것이 됩니다.
 */
export class Fling {
  /** 지금 자리. 0 이 맨 위이고 아래로 갈수록 음수입니다. */
  offset = 0
  /** 넘치는 양. 내용이 바뀔 때 부르는 쪽이 갱신합니다. */
  over = 0
  /** 이 누름이 얼마나 움직였는가. 고르는 자리마다 이것을 봅니다. */
  moved = 0

  private velocity = 0
  private dragging = false
  private grabbedAt = 0
  private grabbedOffset = 0
  private lastY = 0
  private lastAt = 0

  get holding(): boolean {
    return this.dragging
  }

  grab(y: number): void {
    this.dragging = true
    this.grabbedAt = y
    this.grabbedOffset = this.offset
    this.moved = 0
    this.velocity = 0
    this.lastY = y
    this.lastAt = performance.now()
  }

  /** 끄는 중의 자리. **끝을 넘긴 만큼은 덜 따라옵니다.** */
  drag(y: number): void {
    if (!this.dragging) return
    const delta = y - this.grabbedAt
    this.moved = Math.max(this.moved, Math.abs(delta))
    this.offset = this.pulled(this.grabbedOffset + delta)

    // 손끝의 빠르기. 뗄 때 이만큼으로 미끄러집니다.
    const now = performance.now()
    const span = now - this.lastAt
    if (span > 8) {
      this.velocity = ((y - this.lastY) / span) * 1000
      this.lastY = y
      this.lastAt = now
    }
  }

  release(): void {
    this.dragging = false
  }

  /** 바퀴 한 칸. 관성은 없습니다. */
  wheel(delta: number): void {
    this.velocity = 0
    this.offset = Math.min(0, Math.max(-this.over, this.offset + delta))
  }

  /** 내용이 바뀌었을 때. 넘치는 양이 줄면 지금 자리가 그 밖일 수 있습니다. */
  clamp(): void {
    this.offset = Math.min(0, Math.max(-this.over, this.offset))
    this.velocity = 0
  }

  /**
   * 한 단계 흐릅니다. **자리가 바뀌었으면 참입니다.**
   *
   * 끌고 있는 동안에는 아무것도 하지 않습니다 — 그때의 자리는 손가락이 정합니다.
   */
  tick(seconds: number): boolean {
    if (this.dragging) return false
    const was = this.offset

    // 끝을 넘겨 있으면 먼저 되돌아옵니다.
    const home = Math.min(0, Math.max(-this.over, this.offset))
    if (home !== this.offset) {
      this.velocity = 0
      this.offset += (home - this.offset) * Math.min(1, seconds * SNAP_BACK)
      if (Math.abs(home - this.offset) < 0.5) this.offset = home
      return this.offset !== was
    }

    if (Math.abs(this.velocity) < GLIDE_STOP) {
      this.velocity = 0
      return false
    }
    this.offset = this.pulled(this.offset + this.velocity * seconds)
    this.velocity -= this.velocity * Math.min(1, seconds * GLIDE_DECAY)
    return this.offset !== was
  }

  /** 끝을 넘긴 자리를 덜 따라오게 접습니다. */
  private pulled(next: number): number {
    if (next > 0) return Math.min(OVER_PULL, next * OVER_GRIP)
    const floor = -this.over
    if (next < floor) return floor - Math.min(OVER_PULL, (floor - next) * OVER_GRIP)
    return next
  }
}

export class ScrollView extends Container {
  /** 여기에 넣습니다. 이 컨테이너가 위아래로 움직입니다. */
  readonly content = new Container()

  private readonly window = new Container()
  private readonly shade = new Graphics()
  private readonly bar = new Graphics()
  private readonly roll = new Fling()

  /**
   * @param width  보이는 폭
   * @param height 보이는 높이
   */
  constructor(private readonly width_: number, private readonly height_: number) {
    super()

    // **자르는 것은 마스크입니다.** 넘친 것을 지우면 굴렸을 때 다시 만들어야 하고, 그러면
    // 굴리는 동안 매 프레임 목록을 다시 짓게 됩니다.
    const mask = new Graphics()
    mask.rect(0, 0, width_, height_).fill(0xffffff)
    this.window.mask = mask
    this.window.addChild(this.content)

    // 눌리는 자리. **투명해도 자리는 있어야 합니다** — 없으면 빈 곳에서 굴리는 것이
    // 뒤로 지나갑니다.
    const hit = new Graphics()
    hit.rect(0, 0, width_, height_).fill({ color: 0x000000, alpha: 0 })
    hit.eventMode = 'static'

    this.addChild(hit, mask, this.window, this.shade, this.bar)

    // **듣는 것은 이 컨테이너입니다.** 빈 자리 위에서 시작한 것은 `hit` 에서, 줄 위에서
    // 시작한 것은 그 줄에서 올라옵니다 — 둘 다 여기를 지납니다. `hit` 에서만 들으면 줄은
    // `hit` 의 형제라 줄 위에서 누른 손가락이 여기에 닿지 않고, 목록은 줄로 가득하므로
    // 손가락으로는 굴릴 자리가 없었습니다. 휠도 같습니다 — 줄 위에서는 그 줄이 받습니다.
    this.eventMode = 'static'

    this.on('wheel', (event: FederatedWheelEvent) => {
      this.roll.wheel(-Math.sign(event.deltaY) * STEP)
      this.place(false)
      event.preventDefault()
    })

    this.on('pointerdown', (event: FederatedPointerEvent) => {
      this.roll.grab(this.localY(event))
    })
    const release = (): void => this.roll.release()
    this.on('pointerup', release)
    this.on('pointerupoutside', release)
    this.on('globalpointermove', (event: FederatedPointerEvent) => {
      if (!this.roll.holding) return
      this.roll.drag(this.localY(event))
      this.place(false)
    })
  }

  /**
   * 관성과 되돌아옴을 흘립니다.
   *
   * **부르는 쪽이 프레임을 넘겨줍니다.** 자기 틱커를 걸면 손 시계로 세운 도구에서 화면이
   * 멈춰 있는데도 목록만 계속 미끄러집니다.
   */
  tick(seconds: number): void {
    if (this.roll.tick(seconds)) this.place(false)
  }

  /**
   * 손가락의 세로 자리. **이 컨테이너의 좌표입니다.**
   *
   * 화면 좌표로 재면 판이 화면에 맞춰 줄어든 만큼 손가락보다 덜 움직입니다 — 작은 화면에서
   * 목록이 손가락을 따라오지 않던 것이 그것입니다.
   */
  private localY(event: FederatedPointerEvent): number {
    return this.toLocal(event.global).y
  }

  /**
   * 방금 끈 것인가.
   *
   * **줄을 누르는 쪽이 이것을 봅니다.** 끌고 손을 떼는 자리가 어느 줄 위이므로, 보지
   * 않으면 굴릴 때마다 무언가가 골라집니다.
   */
  get dragged(): boolean {
    return this.roll.moved > DRAG_SLOP
  }

  /** 내용이 바뀌었으면 부릅니다. 넘치는 양이 달라지므로 막대를 다시 그립니다. */
  refresh(): void {
    this.place(true)
  }

  /** 맨 위로. 탭을 바꿀 때 부릅니다 — 다른 목록의 자리를 물려받지 않습니다. */
  toTop(): void {
    this.roll.offset = 0
    this.place(true)
  }

  /** 그 자리가 보이도록 굴립니다. 고른 것이 화면 밖에 있으면 안 됩니다. */
  reveal(top: number, height: number): void {
    const at = this.roll.offset
    if (top + at < 0) this.roll.offset = -top
    else if (top + height + at > this.height_) {
      this.roll.offset = this.height_ - top - height
    } else return
    this.place(true)
  }

  private place(measure: boolean): void {
    // **굴리는 동안은 재지 않습니다.** `content.height` 는 자식 전부의 경계를 다시 세는
    // 것이라 휠 한 칸마다 순위표 125개 노드를 걷습니다 — 내용이 바뀌는 길(`refresh` ·
    // `toTop` · `reveal`)에서만 재고, 휠과 끌기는 그 값을 씁니다.
    if (measure) {
      this.contentH = this.content.height
      this.roll.over = Math.max(0, this.contentH - this.height_)
      this.roll.clamp()
    }
    const was = this.content.y
    this.content.y = Math.round(this.roll.offset)
    if (!measure && this.content.y === was) return
    this.drawBar(this.roll.over)
  }

  /** 마지막으로 잰 내용의 높이. */
  private contentH = 0

  /**
   * 막대와 가장자리의 그늘.
   *
   * **그늘이 「더 있다」를 알립니다.** 막대만으로는 눈이 가지 않고, 잘린 줄이 흐려지면
   * 아래에 더 있다는 것이 그 자리에서 읽힙니다.
   */
  private drawBar(over: number): void {
    const bar = this.bar
    bar.clear()
    const shade = this.shade
    shade.clear()

    if (over <= 0) return

    const ratio = this.height_ / this.contentH
    const barH = Math.max(28, this.height_ * ratio)
    // 끝을 넘긴 자리에서도 막대는 끝에 붙어 있습니다.
    const held = Math.min(over, Math.max(0, -this.roll.offset))
    const at = (held / over) * (this.height_ - barH)

    bar.roundRect(this.width_ - BAR_W - 2, 0, BAR_W, this.height_, BAR_W / 2)
      .fill({ color: 0x1b2431 })
    bar.roundRect(this.width_ - BAR_W - 2, at, BAR_W, barH, BAR_W / 2)
      .fill({ color: 0x46566d })

    const fade = 22
    if (this.roll.offset < 0) {
      shade.rect(0, 0, this.width_, fade).fill({ color: 0x0c121b, alpha: 0.55 })
    }
    if (-this.roll.offset < over) {
      shade.rect(0, this.height_ - fade, this.width_, fade)
        .fill({ color: 0x0c121b, alpha: 0.55 })
    }
  }
}
