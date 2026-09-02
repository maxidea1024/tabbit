// 통신 중과 실패.
//
// **통신은 눈에 보여야 합니다.** 도는 동안 아무 표시가 없으면 사람이 다시 누르고, 실패가
// 조용하면 눌렀는데 아무 일도 일어나지 않은 것이 됩니다 — 둘 다 「고장인가」로 읽힙니다.
//
// 표시는 화면 오른쪽 위 구석 하나입니다. 판마다 두면 판이 겹칠 때 표시도 겹칩니다.
//
// **도는 동안 입력을 막습니다.** 응답이 늦으면 사람이 계속 누르고, 누른 만큼이 요청이
// 됩니다 — 시드를 스무 개 받거나 같은 판을 여러 번 제출하는 길이 그것입니다. 막는 것은
// **곧바로**이고 표시만 늦게 나타납니다. 표시를 기다렸다 막으면 그 사이의 연타가 지나갑니다.

import { Container, Graphics } from 'pixi.js'

import { t } from '../core/strings'
import { failKey, onBusy, onFail, type ApiError } from '../net/api'
import { COLOR, SIZE } from '../render/theme'
import type { Toasts } from './toast'

/**
 * 표시가 나타나기까지 기다리는 시간.
 *
 * **빠른 요청에는 나타나지 않습니다.** 순위 조회가 대개 10ms 안에 끝나는데 그때마다
 * 표시가 깜빡이면 화면이 소란스럽습니다 — 기다림이 느껴질 만큼 걸린 것에만 붙습니다.
 */
const DELAY = 0.22

/** 사라질 때 잦아드는 시간. */
const FADE = 0.18

const RADIUS = 11
const THICKNESS = 3

/** 막는 동안 화면이 어두워지는 정도. 표시와 함께 나타납니다. */
const DIM = 0.18

export class NetStatus extends Container {
  /**
   * 입력을 받아 버리는 겹.
   *
   * **화면 전체를 덮습니다.** 판 위에도 덮여야 하므로 판보다 위에 있고, 그래서 이
   * 컨테이너의 `zIndex` 가 판의 것보다 큽니다.
   */
  private readonly blocker = new Graphics()
  private readonly ring = new Graphics()
  /** 0 이 없는 것, 1 이 다 나온 것. */
  private shown = 0
  private waited = 0
  private working = false
  private spin = 0
  private readonly unwatch: (() => void)[] = []

  /**
   * @param toasts 실패를 적을 자리. **판이 그 자리에서 적는 갈래는 오지 않습니다.**
   */
  constructor(private readonly toasts: Toasts) {
    super()
    // 판이 9_500 이므로 그보다 위입니다. 판 위의 단추도 막혀야 합니다.
    this.zIndex = 9_800
    this.sortableChildren = true

    // **넓게 그립니다.** 화면이 늘어나도 가장자리가 남지 않아야 합니다.
    this.blocker.rect(-4_000, -4_000, SIZE.width + 8_000, SIZE.height + 8_000)
      .fill({ color: 0x05080e })
    this.blocker.eventMode = 'static'
    // 위에 아무것도 지나가지 않게 합니다. 눌러도 아무 일도 하지 않습니다.
    this.blocker.on('pointertap', () => undefined)
    this.blocker.visible = false
    this.blocker.alpha = 0
    this.blocker.zIndex = 0

    this.ring.position.set(SIZE.width - 34, 34)
    this.ring.eventMode = 'none'
    this.ring.alpha = 0
    this.ring.zIndex = 1

    this.addChild(this.blocker, this.ring)

    // **프레임을 기다리지 않고 막습니다.** `advance` 에서만 켜면 요청이 시작된 프레임의
    // 입력이 지나가고, 연타는 바로 그 몇 밀리초에 들어옵니다.
    this.unwatch.push(onBusy(working => {
      this.working = working
      if (working) this.blocker.visible = true
    }))
    this.unwatch.push(onFail(error => this.report(error)))
  }

  /** 화면을 접을 때 겁니다. 걸어 둔 것을 풀지 않으면 판이 사라져도 남습니다. */
  destroyWatchers(): void {
    for (const off of this.unwatch) off()
    this.unwatch.length = 0
  }

  /**
   * 실패 하나를 적습니다.
   *
   * **글은 갈래에서 나옵니다.** 서버가 준 문장을 그대로 적으면 6개 언어 중 하나로만
   * 나오고, 그 하나가 한국어도 아닙니다.
   */
  private report(error: ApiError): void {
    this.toasts.push(t('ui.lb.fail.title'), t(failKey(error)), COLOR.bad, 3.4)
  }

  advance(seconds: number): void {
    // 켜는 것은 알림이 하고 여기는 끄기만 합니다.
    if (!this.working && this.shown <= 0) this.blocker.visible = false

    if (this.working) {
      this.waited += seconds
      if (this.waited >= DELAY) this.shown = Math.min(1, this.shown + seconds / FADE)
    } else {
      this.waited = 0
      this.shown = Math.max(0, this.shown - seconds / FADE)
    }

    this.blocker.alpha = this.shown * DIM
    this.ring.alpha = this.shown
    if (this.shown <= 0) return

    this.spin += seconds * 4.4
    this.draw()
  }

  /** 한 바퀴에서 한 조각이 비어 있는 고리. 그 빈 자리가 도는 것으로 보입니다. */
  private draw(): void {
    const g = this.ring
    g.clear()
    g.circle(0, 0, RADIUS)
      .stroke({ color: 0x27324a, width: THICKNESS, alpha: 0.9 })
    g.arc(0, 0, RADIUS, this.spin, this.spin + Math.PI * 0.6)
      .stroke({ color: COLOR.good, width: THICKNESS, cap: 'round' })
  }
}
