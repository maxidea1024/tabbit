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
  /**
   * 지폐인가. **점과 지폐의 물리가 다릅니다** — 점은 그냥 떨어지고, 지폐는 천천히 떨어지며
   * 돌고 펄럭입니다.
   */
  bill: boolean
  /** 지금 기울어진 각도와 그 빠르기(도 · 도/초). 지폐만 씁니다. */
  angle: number
  turn: number
  /**
   * 펄럭이는 위상과 그 빠르기.
   *
   * **가로 폭이 이것으로 줄었다 늘어납니다.** 종이가 옆으로 돌아 얇아지는 것이고, 그
   * 하나가 「종이」와 「판때기」를 가릅니다 — 도는 것만으로는 도는 카드입니다.
   */
  flap: number
  flapAt: number
}

/**
 * 한꺼번에 살아 있을 수 있는 조각의 수.
 *
 * **한 번의 상한은 부르는 쪽이 정하지만, 여러 번이 겹치면 그 합에는 상한이 없었습니다.**
 * 블라인드를 깨는 순간에 500개 가까이가 2.5초 동안 살아 있었고, 조각 하나가 매 프레임
 * 채우기 명령 하나입니다. 넘치면 가장 오래된 것부터 놓습니다 — 어차피 먼저 꺼질 것들입니다.
 */
const MAX_LIVE = 400

/**
 * 지폐가 떨어지는 가장 빠른 속도. 픽셀/초입니다.
 *
 * **종이는 종점 속도가 낮습니다.** 점과 같이 떨어뜨리면 무게가 있는 것이 되고, 뿌린 돈으로
 * 읽히지 않습니다 — 화면 높이가 800이므로 이 값이면 위에서 아래까지 3초 남짓입니다.
 */
const BILL_FALL = 210
/** 지폐에 걸리는 중력. 점의 900보다 작습니다 — 종점 속도까지 천천히 갑니다. */
const BILL_GRAVITY = 460
/** 지폐의 가로 감속. 점(1.2)보다 셉니다 — **바람이 없어야 하므로 곧 멈춥니다.** */
const BILL_DRAG = 1.5
/** 세로가 가로의 몇 배인가. **세로로 약간 긴 네모입니다.** */
const BILL_TALL = 1.5

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
        bill: false, angle: 0, turn: 0, flap: 0, flapAt: 0,
      })
    }
  }

  /**
   * 돈을 한 자리에서 뿌립니다. **지폐가 흩날려 떨어집니다.**
   *
   * 점이 떨어지는 것과 다른 것 셋입니다.
   *
   * - **천천히 떨어집니다.** 종이는 종점 속도가 낮습니다(`BILL_FALL`) — 점과 같은 중력으로
   *   떨어뜨리면 그것은 무게가 있는 것이고, 뿌린 돈으로 읽히지 않습니다
   * - **돌면서 펄럭입니다.** 도는 것만으로는 도는 카드이고, 가로 폭이 줄었다 늘어나야
   *   종이가 옆으로 돌아 얇아지는 것으로 보입니다
   * - **바람이 없습니다.** 가로 속도는 곧 잦아들고(`BILL_DRAG`), 남는 좌우 움직임은
   *   펄럭임에 딸린 것뿐입니다 — 한쪽으로 미는 바람을 넣으면 그것은 돈이 아니라 날씨입니다
   */
  bills(x: number, y: number, count: number, tint: number, power = 1, linger = 1): void {
    if (!this.enabled) return

    const overflow = this.live.length + count - MAX_LIVE
    if (overflow > 0) this.live.splice(0, overflow)

    for (let i = 0; i < count; i++) {
      const angle = Math.random() * Math.PI * 2
      // **점보다 느리게 던집니다.** 지폐는 오래 남으므로 같은 속도로 던지면 첫 0.3초에
      // 화면 밖으로 나갑니다.
      const speed = (70 + Math.random() * 220) * power
      this.live.push({
        x, y,
        vx: Math.cos(angle) * speed,
        // **위로 한 번 솟습니다.** 뿌린 돈은 던진 자리에서 위로 갔다가 떨어집니다.
        vy: Math.sin(angle) * speed - 210 * power,
        life: 0,
        // 오래 남습니다 — 흩날려 떨어지는 것을 보는 것이 이 연출의 전부입니다.
        span: (1.5 + Math.random() * 0.9) * linger,
        size: 5 + Math.random() * 3,
        tint,
        bill: true,
        angle: Math.random() * 360,
        turn: (Math.random() - 0.5) * 300,
        flap: Math.random() * Math.PI * 2,
        flapAt: 3.4 + Math.random() * 3.6,
      })
    }
  }

  /** 날고 있는 것을 전부 지웁니다. 판이 없어질 때뿐입니다. */
  clear(): void {
    this.live.length = 0
    this.canvas.clear()
    this.drawn = false
  }

  /**
   * 지폐 한 장.
   *
   * **네모 하나와 안쪽 테 하나입니다.** `$` 를 적지 않습니다 — 8픽셀 폭에서 그 글자는
   * 알아볼 수 없는 얼룩이고, 「돈」은 색(값의 노랑)과 모양이 이미 말합니다. 안쪽 테는
   * 지폐의 테두리이고, 그 한 줄이 종이 조각과 지폐를 가릅니다.
   *
   * **손으로 돌립니다.** `Graphics` 의 네모는 돌지 않으므로 네 귀퉁이를 직접 셉니다.
   */
  private bill(p: Particle, alpha: number): void {
    // 펄럭임 — 옆으로 돌면 가로 폭이 줄어듭니다. 0 까지 가지 않습니다(선 하나가 되면
    // 그 프레임에 없어진 것으로 보입니다).
    const half = p.size * (0.22 + 0.78 * Math.abs(Math.cos(p.flap)))
    const tall = p.size * BILL_TALL
    const rad = p.angle * (Math.PI / 180)
    const c = Math.cos(rad)
    const s = Math.sin(rad)
    // **네모 하나가 배열 하나입니다.** 귀퉁이마다 배열을 만들어 펼치면 지폐 한 장에
    // 배열 15개이고, 상한 400장이면 프레임마다 6,000개입니다 — 셋으로 줍니다.
    //
    // **통 하나를 돌려 쓰지는 않습니다.** `Graphics.poly` 는 넘긴 배열을 베끼지 않고
    // 그대로 들고 있다가 그릴 때 읽으므로, 돌려 쓰면 그 프레임의 지폐 전부가 마지막
    // 네모의 자리에 겹쳐 그려집니다 — Pixi 의 `Polygon` 이 `this.points = flat` 입니다.
    const quad = (w: number, h: number): number[] => [
      p.x - w * c + h * s, p.y - w * s - h * c,
      p.x + w * c + h * s, p.y + w * s - h * c,
      p.x + w * c - h * s, p.y + w * s + h * c,
      p.x - w * c - h * s, p.y - w * s + h * c,
    ]
    this.canvas.poly(quad(half, tall)).fill({ color: p.tint, alpha })
    // 안쪽 테와 가운데의 점. **지폐가 좁게 돌아 있을 때는 그리지 않습니다** — 두 선이
    // 겹쳐 한 줄이 되고, 점은 그 줄을 덮습니다.
    //
    // **이 둘이 종이 조각과 지폐를 가릅니다.** 채움만으로는 금색 네모이고, 테 한 줄과
    // 가운데의 무엇 하나가 있으면 지폐의 얼개가 됩니다 — `$` 를 적지 않는 것은 8픽셀에서
    // 그 글자가 알아볼 수 없는 얼룩이기 때문입니다.
    if (half < 2.4) return
    const inx = half - 1.4
    const iny = tall - 2
    this.canvas.poly(quad(inx, iny))
      .stroke({ color: 0x000000, alpha: alpha * 0.45, width: 1 })
    this.canvas.poly(quad(inx * 0.5, iny * 0.34))
      .fill({ color: 0x000000, alpha: alpha * 0.22 })
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

    // **감속은 프레임마다 한 번 셉니다.** `seconds` 가 그 프레임의 모든 조각에 같으므로
    // 조각마다 `Math.exp` 를 부르면 상한 400개에 400번입니다.
    const billDrag = Math.exp(-BILL_DRAG * seconds)
    const dotDrag = Math.exp(-1.2 * seconds)

    // **남는 것을 앞으로 당겨 씁니다.** 죽은 것마다 `splice` 하면 한 번에 수백 개가 함께
    // 꺼질 때 n² 입니다. 차례는 그대로여야 겹치는 색이 흔들리지 않습니다.
    let keep = 0
    for (let i = 0; i < this.live.length; i++) {
      const p = this.live[i]
      p.life += seconds
      if (p.life >= p.span) continue

      // 가로 감속. **초 단위입니다** — 프레임당 0.98 로 적으면 144Hz 에서 조각이 절반도
      // 퍼지지 못합니다. 60Hz 에서 프레임당 0.98 이던 것과 같은 값입니다.
      if (p.bill) {
        p.vy = Math.min(BILL_FALL, p.vy + BILL_GRAVITY * seconds)
        p.vx *= billDrag
        p.angle += p.turn * seconds
        p.flap += p.flapAt * seconds
        // **펄럭임에 딸린 좌우 흔들림.** 한쪽으로 미는 것이 아니라 오가는 것이므로 옮겨
        // 가는 거리가 0 입니다 — 종이가 팔랑이는 것이 이것이고, 바람이 아닙니다.
        p.x += Math.sin(p.flap) * 34 * seconds
      } else {
        p.vy += 900 * seconds
        p.vx *= dotDrag
      }
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
      // **지폐는 끝에서만 옅어집니다.** 점은 뜨는 순간부터 옅어지지만(그것이 불티입니다)
      // 지폐는 종이이므로, 마지막 0.4초에만 사라집니다.
      const left = 1 - p.life / p.span
      if (!p.bill) {
        this.canvas.circle(p.x, p.y, p.size * left).fill({ color: p.tint, alpha: left })
        continue
      }
      const alpha = Math.min(1, left * p.span / 0.4)
      this.bill(p, alpha)
    }
  }
}
