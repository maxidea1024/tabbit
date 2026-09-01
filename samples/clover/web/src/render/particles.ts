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

export class Particles extends Container {
  private readonly canvas = new Graphics()
  private readonly live: Particle[] = []

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
  }

  advance(seconds: number): void {
    this.canvas.clear()
    if (this.live.length === 0) return

    for (let i = this.live.length - 1; i >= 0; i--) {
      const p = this.live[i]
      p.life += seconds
      if (p.life >= p.span) {
        this.live.splice(i, 1)
        continue
      }

      p.vy += 900 * seconds
      p.vx *= 0.98
      p.x += p.vx * seconds
      p.y += p.vy * seconds

      const alpha = 1 - p.life / p.span
      this.canvas.circle(p.x, p.y, p.size * alpha).fill({ color: p.tint, alpha })
    }
  }
}
