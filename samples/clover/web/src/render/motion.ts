// 움직임.
//
// **카드가 목표 자리로 곧바로 가지 않습니다.** 용수철로 따라가고, 지나쳤다가 돌아옵니다 —
// 그 한 번의 넘침이 카드가 살아 있다는 느낌의 거의 전부입니다.
//
// 화면 밖에서도 뜻이 있는 순수한 것이라 따로 두었고, 그래서 테스트가 값을 확인합니다.

/** 값 하나를 목표로 끌고 가는 용수철. */
export class Spring {
  value: number
  target: number
  private velocity = 0

  constructor(value = 0, private stiffness = 260, private damping = 22) {
    this.value = value
    this.target = value
    this.homeStiffness = stiffness
    this.homeDamping = damping
  }

  private readonly homeStiffness: number
  private readonly homeDamping: number

  /**
   * 이번 한 번만 세게.
   *
   * **자리에 「짝」 달라붙는 느낌은 강성에서 나옵니다** — 부드러운 용수철은 미끄러져 들어오고,
   * 강성이 높고 감쇠가 큰 용수철은 빠르게 가서 멈춥니다. 자리에 닿으면 원래 값으로 돌아옵니다.
   */
  hard(stiffness: number, damping: number): void {
    this.stiffness = stiffness
    this.damping = damping
  }

  soft(): void {
    this.stiffness = this.homeStiffness
    this.damping = this.homeDamping
  }

  /** 곧바로 그 값이 됩니다. 판이 바뀔 때 씁니다. */
  snap(value: number): void {
    this.value = value
    this.target = value
    this.velocity = 0
  }

  /** 한 번 밀어 줍니다. 카드가 튀어오르는 것이 이것입니다. */
  kick(amount: number): void {
    this.velocity += amount
  }

  advance(seconds: number): void {
    // 큰 프레임 간격에서 용수철이 폭주하지 않게 나눠서 적분합니다.
    let left = Math.min(seconds, 0.1)
    while (left > 0) {
      const step = Math.min(left, 1 / 120)
      left -= step

      const force = (this.target - this.value) * this.stiffness
      this.velocity += (force - this.velocity * this.damping) * step
      this.value += this.velocity * step
    }
  }

  get settled(): boolean {
    return Math.abs(this.target - this.value) < 0.05 && Math.abs(this.velocity) < 0.05
  }
}

/** 카드 하나의 움직임 전체. 자리와 기울기와 크기입니다. */
export class Motion {
  readonly x = new Spring()
  readonly y = new Spring()
  readonly rotation = new Spring(0, 220, 18)
  readonly scale = new Spring(1, 300, 20)

  /** 늘 흔들리게 하는 위상. 카드마다 다르므로 줄이 살아 있어 보입니다. */
  readonly phase = Math.random() * Math.PI * 2

  snap(x: number, y: number): void {
    this.x.snap(x)
    this.y.snap(y)
  }

  to(x: number, y: number, rotation: number): void {
    this.x.target = x
    this.y.target = y
    this.rotation.target = rotation
  }

  /** 이번 이동만 세게. 낸 카드가 자리에 달라붙는 것이 이것입니다. */
  hard(): void {
    this.x.hard(1_400, 62)
    this.y.hard(1_400, 62)
    this.rotation.hard(900, 44)
  }

  soft(): void {
    this.x.soft()
    this.y.soft()
    this.rotation.soft()
  }

  advance(seconds: number): void {
    this.x.advance(seconds)
    this.y.advance(seconds)
    this.rotation.advance(seconds)
    this.scale.advance(seconds)
  }
}

/** 늘 조금씩 흔들리는 값. 멈춘 화면이 죽어 보이는 것을 막습니다. */
export function sway(time: number, phase: number, amount: number, speed = 1): number {
  return Math.sin(time * speed + phase) * amount
}
