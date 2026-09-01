// 동전.
//
// **돈이 숫자만 바뀌면 벌었다는 느낌이 없습니다.** 번 자리에서 금액 칸으로 동전이 날아가
// 하나씩 꽂히고, 꽂히는 소리의 음이 하나씩 올라갑니다 — 사람이 「받았다」로 읽는 것이
// 그 셋입니다.
//
// 잃을 때는 반대로 금액 칸에서 튀어나가 떨어집니다. 색도 다릅니다.

import { Container, Graphics } from 'pixi.js'

interface Coin {
  from: { x: number; y: number }
  to: { x: number; y: number }
  /** 곡선의 가운데. 여기가 있어야 직선으로 날지 않습니다. */
  bend: { x: number; y: number }
  delay: number
  life: number
  span: number
  spin: number
  gain: boolean
  /** 몇 번째 동전인가. 소리의 음이 이것으로 올라갑니다. */
  index: number
  landed: boolean
}

const GOLD = 0xffcf4a
const GOLD_EDGE = 0xa8781a
const LOSS = 0xff7a7a
const LOSS_EDGE = 0x7a2a2a

export class Coins extends Container {
  private readonly canvas = new Graphics()
  private readonly live: Coin[] = []

  /** 동전 하나가 꽂힐 때 부릅니다. 인자는 그 동전의 순번입니다. */
  onLand?: (index: number, gain: boolean) => void

  constructor() {
    super()
    this.addChild(this.canvas)
    this.eventMode = 'none'
  }

  /**
   * 동전을 날립니다.
   *
   * **개수는 금액이 아니라 금액의 눈금입니다** — $30을 30개로 날리면 화면이 동전으로 덮이고
   * 하나씩 꽂히는 소리도 뜻을 잃습니다.
   */
  fly(amount: number, from: { x: number; y: number }, to: { x: number; y: number }): void {
    const gain = amount > 0
    const count = Math.max(1, Math.min(12, Math.round(Math.abs(amount) / 2) + 1))
    const start = gain ? from : to
    const end = gain ? to : { x: to.x + (Math.random() - 0.5) * 260, y: to.y + 320 }

    for (let i = 0; i < count; i++) {
      this.live.push({
        from: { x: start.x + (Math.random() - 0.5) * 40, y: start.y + (Math.random() - 0.5) * 30 },
        to: gain ? { x: end.x + (Math.random() - 0.5) * 18, y: end.y + (Math.random() - 0.5) * 14 } : end,
        bend: {
          x: (start.x + end.x) / 2 + (Math.random() - 0.5) * 220,
          y: Math.min(start.y, end.y) - 90 - Math.random() * 130,
        },
        delay: i * 0.055,
        life: 0,
        span: 0.42 + Math.random() * 0.18,
        spin: 6 + Math.random() * 6,
        gain,
        index: i,
        landed: false,
      })
    }
  }

  get busy(): boolean {
    return this.live.length > 0
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
      const coin = this.live[i]
      coin.life += seconds

      const t = (coin.life - coin.delay) / coin.span
      if (t < 0) continue

      if (t >= 1) {
        if (!coin.landed) {
          coin.landed = true
          this.onLand?.(coin.index, coin.gain)
        }
        this.live.splice(i, 1)
        continue
      }

      // 이차 베지에. 위로 솟았다가 목표로 떨어집니다.
      const u = 1 - t
      const x = u * u * coin.from.x + 2 * u * t * coin.bend.x + t * t * coin.to.x
      const y = u * u * coin.from.y + 2 * u * t * coin.bend.y + t * t * coin.to.y

      // 앞뒤로 돌아가는 것처럼 가로만 눌립니다. **원판이 도는 것으로 읽힙니다.**
      const squash = Math.abs(Math.cos(coin.life * coin.spin))
      const radius = 7 * (coin.gain ? 1 : 0.85)
      const width = Math.max(1.2, radius * squash)

      const face = coin.gain ? GOLD : LOSS
      const edge = coin.gain ? GOLD_EDGE : LOSS_EDGE
      const fade = coin.gain ? 1 : 1 - Math.max(0, t - 0.6) / 0.4

      this.canvas.ellipse(x, y + 3, width, radius).fill({ color: 0x000000, alpha: 0.25 * fade })
      this.canvas.ellipse(x, y, width, radius).fill({ color: face, alpha: fade })
      this.canvas.ellipse(x, y, width, radius).stroke({ color: edge, width: 1.5, alpha: fade })
      if (squash > 0.55) {
        this.canvas.ellipse(x, y, width * 0.45, radius * 0.45)
          .stroke({ color: edge, width: 1.2, alpha: 0.7 * fade })
      }
    }
  }
}
