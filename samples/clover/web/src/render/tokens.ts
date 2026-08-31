// 칩.
//
// **칩이 숫자로만 오르면 무엇이 얼마를 낸 것인지 남지 않습니다.** 카드가 낸 칩은 그 카드에서
// 칩 칸으로 날아가 꽂혀야 하고, 그 개수와 색이 곧 「얼마짜리인가」입니다 — 실제 포커 칩이
// 액면마다 색이 다른 것과 같은 이유입니다.
//
// 동전(`coins.ts`)과 나는 방식은 같고 다른 것이 둘입니다. 칩은 **액면으로 쪼갭니다** — 30이면
// 25 하나와 5 하나이고, 300이면 100 셋입니다. 그리고 칩은 판의 왼쪽으로 갑니다.

import { Container, Graphics } from 'pixi.js'

/**
 * 칩의 액면.
 *
 * **큰 것부터 적습니다.** 쪼갤 때 큰 것을 먼저 집으므로 순서가 뒤바뀌면 1원짜리 300개가
 * 됩니다.
 *
 * 색은 실제 칩의 관례를 따릅니다 — 흰색·빨강·초록·검정 순으로 커집니다. 그것을 아는 사람은
 * 색만 보고 크기를 알고, 모르는 사람도 **색이 다른 것 셋이 날아온 것**은 봅니다.
 */
const FACES: { value: number; face: number; edge: number }[] = [
  { value: 1000, face: 0x6b4a9c, edge: 0xc9a8ff },
  { value: 500, face: 0x2c3550, edge: 0x8fa4d8 },
  { value: 100, face: 0x1b2230, edge: 0xcfd8e8 },
  { value: 50, face: 0xc8a03c, edge: 0xffe08a },
  { value: 25, face: 0x2f7a52, edge: 0x8fe3b4 },
  { value: 10, face: 0x2f6a9c, edge: 0x9fd4ff },
  { value: 5, face: 0x9c3341, edge: 0xffa8b4 },
  { value: 1, face: 0xdfe6f2, edge: 0x8c96a8 },
]

/**
 * 금액을 칩으로 쪼갭니다.
 *
 * **몇 개까지만 냅니다.** 3000을 1000짜리 셋으로 두면 좋지만, 큰 액면이 없는 값이 오면
 * 개수가 끝없이 늘어나므로 상한을 둡니다 — 화면을 칩으로 덮는 것은 「많이 벌었다」가
 * 아니라 「무엇인지 모르겠다」입니다.
 */
export function chipStack(amount: number, most = 3): number[] {
  const out: number[] = []
  let left = Math.max(0, Math.round(amount))

  for (const one of FACES) {
    while (left >= one.value && out.length < most) {
      out.push(one.value)
      left -= one.value
    }
    if (out.length >= most) break
  }
  // 다 쪼개지 못했으면 가장 작은 것 하나로 남은 것을 나타냅니다.
  if (out.length === 0 && amount > 0) out.push(1)
  return out
}

interface Chip {
  value: number
  from: { x: number; y: number }
  to: { x: number; y: number }
  /** 곡선의 가운데. 여기가 있어야 직선으로 날지 않습니다. */
  bend: { x: number; y: number }
  delay: number
  life: number
  span: number
  spin: number
  index: number
  landed: boolean
}

export class Tokens extends Container {
  private readonly canvas = new Graphics()
  private readonly live: Chip[] = []

  /** 칩 하나가 꽂힐 때 부릅니다. 인자는 순번과 액면입니다. */
  onLand?: (index: number, value: number) => void

  constructor() {
    super()
    this.addChild(this.canvas)
    this.eventMode = 'none'
  }

  /** 그 금액만큼의 칩을 날립니다. */
  fly(amount: number, from: { x: number; y: number }, to: { x: number; y: number }): void {
    const stack = chipStack(amount)
    stack.forEach((value, i) => {
      this.live.push({
        value,
        from: { x: from.x + (Math.random() - 0.5) * 26, y: from.y + (Math.random() - 0.5) * 20 },
        to: { x: to.x + (Math.random() - 0.5) * 16, y: to.y + (Math.random() - 0.5) * 12 },
        bend: {
          x: (from.x + to.x) / 2 + (Math.random() - 0.5) * 180,
          y: Math.min(from.y, to.y) - 70 - Math.random() * 90,
        },
        delay: i * 0.05,
        life: 0,
        span: 0.34 + Math.random() * 0.14,
        spin: 7 + Math.random() * 5,
        index: i,
        landed: false,
      })
    })
  }

  get busy(): boolean {
    return this.live.length > 0
  }

  advance(seconds: number): void {
    this.canvas.clear()
    if (this.live.length === 0) return

    for (let i = this.live.length - 1; i >= 0; i--) {
      const chip = this.live[i]
      chip.life += seconds

      const t = (chip.life - chip.delay) / chip.span
      if (t < 0) continue

      if (t >= 1) {
        if (!chip.landed) {
          chip.landed = true
          this.onLand?.(chip.index, chip.value)
        }
        this.live.splice(i, 1)
        continue
      }

      // 이차 베지에. 위로 솟았다가 목표로 떨어집니다.
      const u = 1 - t
      const x = u * u * chip.from.x + 2 * u * t * chip.bend.x + t * t * chip.to.x
      const y = u * u * chip.from.y + 2 * u * t * chip.bend.y + t * t * chip.to.y

      // 앞뒤로 돌아가는 것처럼 가로만 눌립니다. **원판이 도는 것으로 읽힙니다.**
      const squash = Math.abs(Math.cos(chip.life * chip.spin))
      const radius = 9
      const width = Math.max(1.4, radius * squash)

      const look = FACES.find(one => one.value === chip.value) ?? FACES[FACES.length - 1]
      this.canvas.ellipse(x, y + 2, width, radius).fill({ color: 0x000000, alpha: 0.28 })
      this.canvas.ellipse(x, y, width, radius).fill({ color: look.face })
      this.canvas.ellipse(x, y, width, radius)
        .stroke({ color: look.edge, width: 1.6, alpha: 0.95 })
      // 테두리의 눈금 하나. **이것이 있어야 동전이 아니라 칩으로 읽힙니다.**
      this.canvas.ellipse(x, y, Math.max(0.8, width * 0.52), radius * 0.52)
        .stroke({ color: look.edge, width: 1, alpha: 0.7 })
    }
  }
}
