// 동전.
//
// **돈이 숫자만 바뀌면 벌었다는 느낌이 없습니다.** 번 자리에서 금액 칸으로 동전이 날아가
// 하나씩 꽂히고, 꽂히는 소리의 음이 하나씩 올라갑니다 — 사람이 「받았다」로 읽는 것이
// 그 셋입니다.
//
// **잔액은 동전이 닿는 순간에 바뀝니다.** 동전마다 금액의 몫을 들고 있고, 닿을 때 그 몫을
// 넘깁니다(`onLand`). 동전이 뜨는 순간에 잔액이 먼저 바뀌면 동전은 이미 끝난 일을 뒤따라
// 가는 그림이고, 그 그림은 아무것도 알리지 않습니다.
//
// **잃는 돈도 같은 길입니다.** 낸 자리에서 금액 칸으로 붉은 동전이 날아가 닿고, 닿는 만큼
// 줄어듭니다. 금액 칸에서 튀어나가 화면 밖으로 떨어지던 것은 「닿았다가 튕겨 나갔다」로
// 읽혔고, 어디서 나간 돈인지도 남지 않았습니다.

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
  /** 이 동전이 들고 있는 금액. 닿을 때 넘깁니다. 합이 곧 금액입니다. */
  share: number
  /** 몇 번째 동전인가. 소리의 음이 이것으로 올라갑니다. */
  index: number
  landed: boolean
}

const GOLD = 0xffcf4a
const GOLD_EDGE = 0xa8781a
const LOSS = 0xff7a7a
const LOSS_EDGE = 0x7a2a2a

/** 닿은 뒤 커지며 사라지는 데 걸리는 시간(초). */
const POP = 0.16
const RADIUS = 7

export class Coins extends Container {
  private readonly canvas = new Graphics()
  private readonly live: Coin[] = []

  /**
   * 동전 하나가 꽂힐 때 부릅니다.
   *
   * `index` 는 그 동전의 순번, `share` 는 그 동전이 들고 온 금액입니다 — 잃는 동전이면
   * 음수입니다. 받는 쪽은 이것을 잔액에 더하기만 하면 됩니다.
   */
  onLand?: (index: number, gain: boolean, share: number) => void

  constructor() {
    super()
    this.addChild(this.canvas)
    this.eventMode = 'none'
  }

  /**
   * 동전을 날립니다. 얻든 잃든 `from` 에서 `to` 로입니다.
   *
   * **개수는 금액이 아니라 금액의 눈금입니다** — $30을 30개로 날리면 화면이 동전으로 덮이고
   * 하나씩 꽂히는 소리도 뜻을 잃습니다. 금액은 동전들에 나누어 싣고, 나누어지지 않는
   * 나머지는 앞의 동전부터 하나씩 더 듭니다 — 몫의 합이 금액과 같아야 마지막 동전이 닿은
   * 잔액이 코어와 같습니다.
   */
  fly(amount: number, from: { x: number; y: number }, to: { x: number; y: number }): void {
    if (amount === 0) return
    const gain = amount > 0
    const size = Math.abs(amount)
    const count = Math.max(1, Math.min(12, Math.round(size / 2) + 1))
    const base = Math.floor(size / count)
    const extra = size - base * count

    for (let i = 0; i < count; i++) {
      const share = (base + (i < extra ? 1 : 0)) * (gain ? 1 : -1)
      this.live.push({
        from: { x: from.x + (Math.random() - 0.5) * 36, y: from.y + (Math.random() - 0.5) * 26 },
        to: { x: to.x + (Math.random() - 0.5) * 18, y: to.y + (Math.random() - 0.5) * 12 },
        // **좌우로 많이 벌리지 않습니다.** 열두 개가 제각각의 길로 날면 어디서 어디로 가는
        // 것인지가 흩어집니다 — 한 다발로 보여야 「저기서 여기로」 가 읽힙니다.
        bend: {
          x: (from.x + to.x) / 2 + (Math.random() - 0.5) * 120,
          y: Math.min(from.y, to.y) - 60 - Math.random() * 90,
        },
        delay: i * 0.055,
        life: 0,
        span: 0.42 + Math.random() * 0.18,
        spin: 6 + Math.random() * 6,
        gain,
        share,
        index: i,
        landed: false,
      })
    }
  }

  get busy(): boolean {
    return this.live.length > 0
  }

  /**
   * 아직 닿지 않은 동전들이 들고 있는 금액의 합.
   *
   * **코어에는 들어갔지만 화면에는 아직 없는 돈입니다.** 화면의 잔액을 코어에서 다시 셀 때
   * 이만큼을 빼야, 그 동전들이 닿을 때 더한 값이 코어와 같아집니다.
   */
  get pending(): number {
    let sum = 0
    for (const coin of this.live) if (!coin.landed) sum += coin.share
    return sum
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

      const face = coin.gain ? GOLD : LOSS
      const edge = coin.gain ? GOLD_EDGE : LOSS_EDGE

      // **닿은 자리에서 커지며 사라집니다.** 닿는 프레임에 그냥 지우면 닿았다는 것이 없고,
      // 잔액이 오른 것과 동전이 없어진 것이 같은 일로 읽히지 않습니다.
      if (t >= 1) {
        if (!coin.landed) {
          coin.landed = true
          this.onLand?.(coin.index, coin.gain, coin.share)
        }
        const pop = (coin.life - coin.delay - coin.span) / POP
        if (pop >= 1) {
          this.live.splice(i, 1)
          continue
        }
        const fade = 1 - pop
        const grow = RADIUS * (1 + pop * 0.9)
        this.canvas.circle(coin.to.x, coin.to.y, grow).fill({ color: face, alpha: fade * 0.9 })
        this.canvas.circle(coin.to.x, coin.to.y, RADIUS * (1 + pop * 2.2))
          .stroke({ color: face, width: 1.5, alpha: fade * 0.6 })
        continue
      }

      // 이차 베지에. 위로 솟았다가 목표로 떨어집니다.
      const u = 1 - t
      const x = u * u * coin.from.x + 2 * u * t * coin.bend.x + t * t * coin.to.x
      const y = u * u * coin.from.y + 2 * u * t * coin.bend.y + t * t * coin.to.y

      // 앞뒤로 돌아가는 것처럼 가로만 눌립니다. **원판이 도는 것으로 읽힙니다.**
      const squash = Math.abs(Math.cos(coin.life * coin.spin))
      const radius = RADIUS * (coin.gain ? 1 : 0.9)
      const width = Math.max(1.2, radius * squash)

      this.canvas.ellipse(x, y + 3, width, radius).fill({ color: 0x000000, alpha: 0.25 })
      this.canvas.ellipse(x, y, width, radius).fill({ color: face })
      this.canvas.ellipse(x, y, width, radius).stroke({ color: edge, width: 1.5 })
      if (squash > 0.55) {
        this.canvas.ellipse(x, y, width * 0.45, radius * 0.45)
          .stroke({ color: edge, width: 1.2, alpha: 0.7 })
      }
    }
  }
}
