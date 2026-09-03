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

export class ScrollView extends Container {
  /** 여기에 넣습니다. 이 컨테이너가 위아래로 움직입니다. */
  readonly content = new Container()

  private readonly window = new Container()
  private readonly shade = new Graphics()
  private readonly bar = new Graphics()
  private offset = 0
  private dragging = false
  private grabbedAt = 0
  private grabbedOffset = 0
  private moved = 0

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
      this.scrollBy(Math.sign(event.deltaY) * STEP)
      event.preventDefault()
    })

    this.on('pointerdown', (event: FederatedPointerEvent) => {
      this.dragging = true
      this.grabbedAt = this.localY(event)
      this.grabbedOffset = this.offset
      this.moved = 0
    })
    const release = (): void => { this.dragging = false }
    this.on('pointerup', release)
    this.on('pointerupoutside', release)
    this.on('globalpointermove', (event: FederatedPointerEvent) => {
      if (!this.dragging) return
      const delta = this.localY(event) - this.grabbedAt
      this.moved = Math.max(this.moved, Math.abs(delta))
      this.setOffset(this.grabbedOffset + delta, false)
    })
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
    return this.moved > DRAG_SLOP
  }

  /** 내용이 바뀌었으면 부릅니다. 넘치는 양이 달라지므로 막대를 다시 그립니다. */
  refresh(): void {
    this.setOffset(this.offset)
  }

  /** 맨 위로. 탭을 바꿀 때 부릅니다 — 다른 목록의 자리를 물려받지 않습니다. */
  toTop(): void {
    this.setOffset(0)
  }

  /** 그 자리가 보이도록 굴립니다. 고른 것이 화면 밖에 있으면 안 됩니다. */
  reveal(top: number, height: number): void {
    if (top + this.offset < 0) this.setOffset(-top)
    else if (top + height + this.offset > this.height_) {
      this.setOffset(this.height_ - top - height)
    }
  }

  private scrollBy(delta: number): void {
    this.setOffset(this.offset - delta, false)
  }

  private setOffset(next: number, measure = true): void {
    // **굴리는 동안은 재지 않습니다.** `content.height` 는 자식 전부의 경계를 다시 세는
    // 것이라 휠 한 칸마다 순위표 125개 노드를 걷습니다 — 내용이 바뀌는 길(`refresh` ·
    // `toTop` · `reveal`)에서만 재고, 휠과 끌기는 그 값을 씁니다.
    if (measure) this.contentH = this.content.height
    const over = Math.max(0, this.contentH - this.height_)
    const was = this.content.y
    this.offset = Math.min(0, Math.max(-over, next))
    this.content.y = Math.round(this.offset)
    if (!measure && this.content.y === was) return
    this.drawBar(over)
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
    const at = (-this.offset / over) * (this.height_ - barH)

    bar.roundRect(this.width_ - BAR_W - 2, 0, BAR_W, this.height_, BAR_W / 2)
      .fill({ color: 0x1b2431 })
    bar.roundRect(this.width_ - BAR_W - 2, at, BAR_W, barH, BAR_W / 2)
      .fill({ color: 0x46566d })

    const fade = 22
    if (this.offset < 0) {
      shade.rect(0, 0, this.width_, fade).fill({ color: 0x0c121b, alpha: 0.55 })
    }
    if (-this.offset < over) {
      shade.rect(0, this.height_ - fade, this.width_, fade)
        .fill({ color: 0x0c121b, alpha: 0.55 })
    }
  }
}
