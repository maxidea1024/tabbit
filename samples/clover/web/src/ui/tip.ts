// 쪽지가 뜨는 법. **한 자리입니다.**
//
// 마우스는 올리면 뜨고 벗어나면 닫힙니다. 손가락에는 「올려 둔다」가 없으므로 꾸욱 눌러야
// 뜨고, 그 사이에 손가락이 움직였으면 끈 것이지 누른 것이 아닙니다.
//
// **화면마다 따로 적으면 언젠가 한쪽이 빠집니다.** 도감이 그랬습니다 — 누르면 곧바로 뜨게
// 해 두어 손가락으로는 꾸욱 누를 것도 없이 한 번 스치면 떴고, 굴리려고 짚은 손가락에도
// 떴습니다. 판 위의 것들은 그 규약을 지켰으므로 같은 게임 안에서 쪽지가 두 가지로 돌았습니다.

import type { Container, FederatedPointerEvent } from 'pixi.js'

/** 손가락으로 이만큼 누르고 있으면 쪽지가 뜹니다. */
export const HOLD_TIP = 0.45
/** 그 사이에 손가락이 이만큼 움직이면 누른 것이 아니라 끈 것입니다. */
export const HOLD_SLACK = 16

/** 가리킨 것이 이만큼 커집니다. **조커 딱지가 그 값이고, 나머지가 그것을 따릅니다.** */
export const TIP_GROW = 1.1
/** 가리킨 것이 이만큼 들립니다. */
export const TIP_RISE = 10

/**
 * 꾸욱 누르기를 재는 것. **화면마다 하나씩 듭니다.**
 *
 * `place` 는 누른 자리를 어느 좌표계로 옮길지입니다 — 움직임을 재는 자리와 같은 좌표계여야
 * 하고, 그 둘이 어긋나면 손가락이 가만히 있어도 끈 것으로 셉니다.
 */
export class TipHold {
  private one?: { at: number; x: number; y: number; show: () => void }
  /**
   * 꾸욱 눌러 띄운 쪽지가 지금 떠 있는가.
   *
   * **떼어도 남아 있어야 합니다.** 손가락을 떼면 Pixi 가 「벗어났다」를 내는데, 그것으로
   * 닫으면 누르고 있는 동안에만 보입니다 — 읽을 시간이 없습니다.
   */
  shown = false
  /**
   * 이 누름이 쪽지를 띄운 것이었는가.
   *
   * **그 손가락이 떼어질 때의 누름을 먹습니다.** 그러지 않으면 쪽지를 보려고 누른 것이
   * 그대로 고르기·사기·쓰기가 됩니다.
   */
  private eaten = false
  private clock = 0

  constructor(
    private readonly place: (event: FederatedPointerEvent) => { x: number; y: number }
      = event => ({ x: event.global.x, y: event.global.y }),
  ) {}

  /** 손가락이 눌렸습니다. **마우스면 아무것도 하지 않습니다** — 마우스는 올리면 뜹니다. */
  arm(event: FederatedPointerEvent, show: () => void): void {
    if (event.pointerType === 'mouse') return
    const at = this.place(event)
    this.one = { at: this.clock, x: at.x, y: at.y, show }
  }

  /**
   * 새 누름이 시작됐습니다.
   *
   * **떠 있는 쪽지가 있으면 이 누름은 그것을 닫는 누름입니다.** 꾸욱 눌러 세운 쪽지는 손을
   * 떼도 남으므로(읽을 시간입니다), 그 상태에서 누른 것이 고르기까지 되면 한 누름이 두 일을
   * 합니다 — 조커를 읽고 나서 손을 떼는 그 자리에 「판매」가 놓여 있게 됩니다.
   */
  begin(): void {
    this.eaten = this.shown
    this.shown = false
    this.one = undefined
  }

  /** 손가락이 여기까지 왔습니다. 많이 움직였으면 누른 것이 아니라 끈 것입니다. */
  moved(at: { x: number; y: number }): void {
    const one = this.one
    if (!one) return
    const dx = at.x - one.x
    const dy = at.y - one.y
    if (dx * dx + dy * dy > HOLD_SLACK * HOLD_SLACK) this.one = undefined
  }

  /** 누르던 것을 놓습니다. 떠 있는 쪽지는 그대로 둡니다. */
  cancel(): void {
    this.one = undefined
  }

  /** 한 틱. 오래 눌렀으면 띄웁니다. **한 번만입니다.** */
  advance(seconds: number, onShow?: () => void): void {
    this.clock += seconds
    const one = this.one
    if (!one || this.clock - one.at < HOLD_TIP) return
    this.one = undefined
    this.eaten = true
    this.shown = true
    onShow?.()
    one.show()
  }

  /** 이 누름이 쪽지를 띄운 것이었는가. **읽으면 내려갑니다.** */
  ate(): boolean {
    if (!this.eaten) return false
    this.eaten = false
    return true
  }

  reset(): void {
    this.one = undefined
    this.shown = false
    this.eaten = false
  }
}

/**
 * 물건 하나에 쪽지를 답니다.
 *
 * **부르는 자리마다 손과 마우스를 따로 적지 않습니다.** 넷을 손으로 달던 동안 한 화면이
 * 그중 둘만 달았고, 그 화면에서만 쪽지가 다르게 돌았습니다.
 */
export function attachTip(node: Container, hold: TipHold,
                          show: () => void, hide: () => void): void {
  node.on('pointerover', (event: FederatedPointerEvent) => {
    if (event.pointerType === 'mouse') show()
  })
  node.on('pointerdown', (event: FederatedPointerEvent) => hold.arm(event, show))
  node.on('pointerout', () => {
    if (!hold.shown) hide()
  })
}
