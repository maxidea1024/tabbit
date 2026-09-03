// 파티클.
//
// **값이 클수록 많이 나옵니다.** 개수의 상한은 `Const_Feel` 의 `ParticleMax` 이므로
// 데이터입니다.
//
// 스프라이트를 쓰지 않고 `Graphics` 하나에 매 프레임 다시 그립니다 — 수십 개짜리에는
// 그것이 더 싸고, 그림 파일이 없다는 이 샘플의 성질과도 맞습니다.

import { Container, Graphics } from 'pixi.js'

interface Particle {
  x: number
  y: number
  vx: number
  vy: number
  life: number
  span: number
  size: number
  tint: number
}

/**
 * 한꺼번에 살아 있을 수 있는 조각의 수.
 *
 * **한 번의 상한은 부르는 쪽이 정하지만, 여러 번이 겹치면 그 합에는 상한이 없었습니다.**
 * 블라인드를 깨는 순간에 500개 가까이가 2.5초 동안 살아 있었고, 조각 하나가 매 프레임
 * 채우기 명령 하나입니다. 넘치면 가장 오래된 것부터 놓습니다 — 어차피 먼저 꺼질 것들입니다.
 */
const MAX_LIVE = 400

export class Particles extends Container {
  private readonly canvas = new Graphics()
  private readonly live: Particle[] = []
  /** 캔버스에 무엇이 그려져 있는가. 비어 있으면 손대지 않기 위한 것입니다. */
  private drawn = false

  constructor() {
    super()
    this.addChild(this.canvas)
    this.eventMode = 'none'
  }

  /** 한 자리에서 터뜨립니다. */
  /**
   * 한 자리에서 터뜨립니다.
   *
   * `linger` 는 오래 남는 정도입니다 — **마지막 한 방은 오래 남아야 「끝났다」로 읽힙니다.**
   */
  /**
   * 조각을 낼 것인가. 옵션이 정합니다.
   *
   * **문을 하나로 둡니다** — 부르는 자리가 열몇 곳이라, 저마다 옵션을 보게 하면 언젠가
   * 하나가 빠집니다.
   */
  enabled = true

  burst(x: number, y: number, count: number, tint: number, power = 1, linger = 1): void {
    if (!this.enabled) return

    const overflow = this.live.length + count - MAX_LIVE
    if (overflow > 0) this.live.splice(0, overflow)

    for (let i = 0; i < count; i++) {
      const angle = Math.random() * Math.PI * 2
      const speed = (60 + Math.random() * 240) * power
      this.live.push({
        x, y,
        vx: Math.cos(angle) * speed,
        vy: Math.sin(angle) * speed - 60 * power,
        life: 0,
        span: (0.45 + Math.random() * 0.5) * linger,
        size: 2 + Math.random() * 4 * power,
        tint,
      })
    }
  }

  /** 날고 있는 것을 전부 지웁니다. 판이 없어질 때뿐입니다. */
  clear(): void {
    this.live.length = 0
    this.canvas.clear()
    this.drawn = false
  }

  advance(seconds: number): void {
    if (this.live.length === 0) {
      // **비어 있으면 손대지 않습니다.** `clear()` 는 지오메트리를 더럽혀 매 프레임 빈 것을
      // 다시 만들게 합니다.
      if (this.drawn) {
        this.canvas.clear()
        this.drawn = false
      }
      return
    }

    // **남는 것을 앞으로 당겨 씁니다.** 죽은 것마다 `splice` 하면 한 번에 수백 개가 함께
    // 꺼질 때 n² 입니다. 차례는 그대로여야 겹치는 색이 흔들리지 않습니다.
    let keep = 0
    for (let i = 0; i < this.live.length; i++) {
      const p = this.live[i]
      p.life += seconds
      if (p.life >= p.span) continue

      p.vy += 900 * seconds
      // 가로 감속. **초 단위입니다** — 프레임당 0.98 로 적으면 144Hz 에서 조각이 절반도
      // 퍼지지 못합니다. 60Hz 에서 프레임당 0.98 이던 것과 같은 값입니다.
      p.vx *= Math.exp(-1.2 * seconds)
      p.x += p.vx * seconds
      p.y += p.vy * seconds
      this.live[keep++] = p
    }
    this.live.length = keep

    this.canvas.clear()
    this.drawn = keep > 0
    // 새것이 아래, 오래된 것이 위입니다 — 전부터 그 차례였습니다.
    for (let i = keep - 1; i >= 0; i--) {
      const p = this.live[i]
      const alpha = 1 - p.life / p.span
      this.canvas.circle(p.x, p.y, p.size * alpha).fill({ color: p.tint, alpha })
    }
  }
}
