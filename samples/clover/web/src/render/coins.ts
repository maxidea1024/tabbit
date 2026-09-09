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
// **나가는 돈은 어디에도 닿지 않습니다.** 금액 숫자에서 붉은 동전이 던져져 솟았다가 떨어지며
// 사라지고, 나오는 그 순간에 그 몫만큼 줄어듭니다(`spend`). 물건 쪽으로 날리면 색은
// 「잃었다」인데 움직임은 「들어왔다」의 문법이고, 남은 돈을 보러 간 눈이 물건까지 따라가야
// 합니다 — 무엇에 낸 돈인지는 그 물건 위에 뜨는 값이 적습니다. 리롤·임대료처럼 갈 곳이
// 없는 지출도 이 하나로 같은 그림입니다. 규칙은 하나입니다 — **들어오는 돈은 온 곳에서
// 날아들고, 나가는 돈은 곳간에서 사라집니다.**

import { Container, Graphics, Text } from 'pixi.js'
import { NUMERALS } from '../ui/font'

interface Coin {
  /** 날아드는 것인가, 곳간에서 빠져나가는 것인가. */
  kind: 'fly' | 'spend'
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
/** 빠져나가는 동전 하나가 던져져 사라지는 시간(초). */
const SPEND = 0.55
/**
 * 동전 원판의 반지름.
 *
 * **그림자와 테와 `$` 가 여기에 딸립니다.** 원판만 키우면 테가 가늘어지고 글이 원판
 * 가운데의 작은 점으로 남아, 커진 것이 아니라 흐려진 것으로 보입니다.
 */
const RADIUS = 11
/** 동전 위의 `$` 크기. */
const MARK_SIZE = Math.round(RADIUS * 1.6)

export class Coins extends Container {
  private readonly canvas = new Graphics()
  /** 캔버스에 무엇이 그려져 있는가. 비어 있으면 손대지 않기 위한 것입니다. */
  private drawn = false
  private readonly live: Coin[] = []
  /**
   * 동전 위의 `$`.
   *
   * **동전마다 글 하나입니다.** 원판만 날면 칩인지 동전인지가 없습니다. 글은 굽는 비용이
   * 있으므로 만들어 둔 것을 돌려 쓰고, 이 프레임에 나는 동전 수만큼만 보입니다.
   */
  private readonly marks: Text[] = []

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

  /** `n` 번째 표시. 없으면 만듭니다. 흰 글에 색을 입혀 쓰므로 하나로 두 색을 냅니다. */
  private mark(n: number): Text {
    while (this.marks.length <= n) {
      const one = new Text({
        text: '$',
        style: {
          fontSize: MARK_SIZE, fontWeight: '900', fontFamily: NUMERALS, fill: 0xffffff,
        },
      })
      one.anchor.set(0.5, 0.52)
      one.visible = false
      this.marks.push(one)
      this.addChild(one)
    }
    return this.marks[n]
  }

  /**
   * 동전을 `from` 에서 `to` 로 날립니다. 들어오는 돈입니다 — 온 곳에서 금액 칸으로.
   */
  fly(amount: number, from: { x: number; y: number }, to: { x: number; y: number }): void {
    if (amount === 0) return
    const gain = amount > 0
    for (const [i, share] of this.shares(amount).entries()) {
      this.live.push({
        kind: 'fly',
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

  /**
   * 동전 한 개를 그 자리에서 날립니다.
   *
   * **부르는 쪽이 자리를 정합니다.** `fly` 는 한 자리에서 여러 개를 흩어 날리는 것이고,
   * 이것은 이미 여러 자리에 놓인 것들이 저마다 하나씩 뜨는 자리입니다 — 정산의 `$` 낱개가
   * 그렇습니다. 자리를 흩지 않습니다(이미 흩어져 있습니다).
   *
   * `index` 는 소리의 음을 올리는 순번이므로 부르는 쪽이 셉니다.
   */
  one(share: number, index: number, from: { x: number; y: number },
      to: { x: number; y: number }): void {
    if (share === 0) return
    this.live.push({
      kind: 'fly',
      from: { x: from.x, y: from.y },
      to: { x: to.x + (Math.random() - 0.5) * 18, y: to.y + (Math.random() - 0.5) * 12 },
      // 뜨는 자리가 저마다 다르므로 곡선은 그 자리와 금액 칸 사이에서 셉니다.
      bend: {
        x: (from.x + to.x) / 2 + (Math.random() - 0.5) * 90,
        y: Math.min(from.y, to.y) - 70 - Math.random() * 70,
      },
      delay: 0,
      life: 0,
      span: 0.42 + Math.random() * 0.18,
      spin: 6 + Math.random() * 6,
      gain: share > 0,
      share,
      index,
      landed: false,
    })
  }

  /**
   * 곳간에서 돈이 빠져나갑니다. `at` 은 금액 숫자의 자리입니다.
   *
   * **동전이 숫자에서 던져져 솟았다가 떨어지면서 사라집니다.** 위로 80~120px 솟아 좌우로
   * 흩어지며 돌아가고, 꼭대기를 지나 떨어지는 동안 옅어져 바닥에 닿기 전에 없어집니다.
   * 짧게 튀는 것으로 해 보았더니 연기 한 줌이 나다 마는 것으로 보였습니다 — 던져져 떨어지는
   * 것이어야 「나갔다」입니다. 몫은 **나오는 그 순간에** 넘깁니다 — 숫자와 동전이 같은
   * 자리에 있으므로 「빠져나간 만큼 줄었다」가 한눈에 읽힙니다.
   */
  spend(amount: number, at: { x: number; y: number }): void {
    if (amount >= 0) return
    for (const [i, share] of this.shares(amount).entries()) {
      // 좌우로 골고루. 한쪽으로만 쏠리면 무엇이 밀어낸 것으로 보입니다.
      const side = (i % 2 === 0 ? 1 : -1) * (0.35 + Math.random() * 0.65)
      const dx = side * 90
      this.live.push({
        kind: 'spend',
        from: { x: at.x + (Math.random() - 0.5) * 20, y: at.y },
        to: { x: at.x + dx, y: at.y + 90 + Math.random() * 30 },
        bend: { x: at.x + dx * 0.55, y: at.y - 170 - Math.random() * 80 },
        delay: i * 0.05,
        life: 0,
        span: SPEND,
        spin: 6 + Math.random() * 6,
        gain: false,
        share,
        index: i,
        landed: false,
      })
    }
  }

  /**
   * 금액을 동전들에 나눕니다.
   *
   * **개수는 금액이 아니라 금액의 눈금입니다** — $30을 30개로 날리면 화면이 동전으로 덮이고
   * 하나씩 꽂히는 소리도 뜻을 잃습니다. 나누어지지 않는 나머지는 앞의 동전부터 하나씩 더
   * 듭니다 — 몫의 합이 금액과 같아야 마지막 동전이 닿은 잔액이 코어와 같습니다.
   */
  private shares(amount: number): number[] {
    const size = Math.abs(amount)
    const sign = amount > 0 ? 1 : -1
    const count = Math.max(1, Math.min(12, Math.round(size / 2) + 1))
    const base = Math.floor(size / count)
    const extra = size - base * count
    const out: number[] = []
    for (let i = 0; i < count; i++) out.push((base + (i < extra ? 1 : 0)) * sign)
    return out
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
    this.drawn = false
    for (const mark of this.marks) mark.visible = false
  }

  advance(seconds: number): void {
    if (this.live.length === 0) {
      // **비어 있으면 손대지 않습니다.** `clear()` 는 지오메트리를 더럽혀 매 프레임 빈 것을
      // 다시 만들게 하고, 판이 도는 시간의 대부분이 동전 하나 없는 상태입니다.
      // `particles.ts` 와 같은 걸쇠입니다.
      if (this.drawn) {
        this.canvas.clear()
        for (const mark of this.marks) mark.visible = false
        this.drawn = false
      }
      return
    }
    this.canvas.clear()
    this.drawn = true
    let shown = 0
    for (const mark of this.marks) mark.visible = false

    for (let i = this.live.length - 1; i >= 0; i--) {
      const coin = this.live[i]
      coin.life += seconds

      const t = (coin.life - coin.delay) / coin.span
      if (t < 0) continue

      const face = coin.gain ? GOLD : LOSS
      const edge = coin.gain ? GOLD_EDGE : LOSS_EDGE

      // **빠져나가는 동전.** 나오는 첫 프레임에 몫을 넘기고, 포물선의 꼭대기를 지나 내려오기
      // 시작하면서 옅어져 없어집니다. 닿는 자리가 없으므로 착지도 없습니다.
      const spending = coin.kind === 'spend'
      if (spending && !coin.landed) {
        coin.landed = true
        this.onLand?.(coin.index, false, coin.share)
      }
      if (spending && t >= 1) {
        this.live.splice(i, 1)
        continue
      }
      // 떨어지는 동안 옅어집니다. 꼭대기는 t 0.5 근처입니다.
      const fade = spending ? 1 - Math.max(0, (t - 0.5) / 0.5) ** 1.3 : 1

      // **닿은 자리에서 커지며 사라집니다.** 닿는 프레임에 그냥 지우면 닿았다는 것이 없고,
      // 잔액이 오른 것과 동전이 없어진 것이 같은 일로 읽히지 않습니다.
      if (!spending && t >= 1) {
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
          .stroke({ color: face, width: RADIUS * 0.21, alpha: fade * 0.6 })
        continue
      }

      // 이차 베지에. 위로 솟았다가 목표로 떨어집니다.
      const u = 1 - t
      const x = u * u * coin.from.x + 2 * u * t * coin.bend.x + t * t * coin.to.x
      const y = u * u * coin.from.y + 2 * u * t * coin.bend.y + t * t * coin.to.y

      // 앞뒤로 돌아가는 것처럼 가로가 조금 눌립니다. **눌림은 조금뿐입니다.** 옆면까지
      // 돌리면 이만한 원판에서는 도는 동전이 아니라 세로로 긴 알로 읽힙니다 — 둥근 것이
      // 먼저이고 도는 것은 그 위에 얹히는 흔들림입니다.
      const squash = 0.82 + 0.18 * Math.abs(Math.cos(coin.life * coin.spin))
      // 나가는 동전은 조금 큽니다. 어디에도 닿지 않으므로 공중에서 읽혀야 합니다.
      const radius = RADIUS * (coin.gain ? 1 : 1.15)
      const width = radius * squash

      // 그림자는 아래로 조금 비켜 같은 크기로. 더 내리면 둘이 겹쳐 세로로 긴 덩어리가 됩니다.
      // **비끼는 거리와 테의 굵기가 반지름을 따릅니다** — 고정된 픽셀로 두면 원판을 키울
      // 때마다 그림자가 원판 뒤로 숨고 테만 가늘어집니다.
      this.canvas.ellipse(x + RADIUS * 0.14, y + RADIUS * 0.29, width, radius)
        .fill({ color: 0x000000, alpha: 0.22 * fade })
      this.canvas.ellipse(x, y, width, radius).fill({ color: face, alpha: fade })
      this.canvas.ellipse(x, y, width, radius)
        .stroke({ color: edge, width: RADIUS * 0.21, alpha: fade })
      // 눌림이 조금뿐이므로 `$` 는 내내 보입니다. 가로만 그만큼 함께 눌립니다.
      const mark = this.mark(shown++)
      mark.visible = true
      mark.position.set(x, y)
      mark.scale.set(squash * radius / RADIUS, radius / RADIUS)
      mark.alpha = fade
      mark.tint = edge
    }
  }
}
