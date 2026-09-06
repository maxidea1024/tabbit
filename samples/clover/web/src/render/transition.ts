// 씬이 갈릴 때.
//
// **규격은 `doc/ui/transition.md` 입니다.** 걸음은 셋입니다 — 나가는 화면을 지우고(`out`),
// 아무것도 보이지 않는 그 프레임에 갈아 끼우고(`hold`), 들어오는 화면을 되돌립니다(`in`).
//
// **덮개를 그리지 않습니다.** 색을 칠한 판이 앞을 지나가면 그것은 화면 위에 놓인 다른
// 물건입니다. 여기서 하는 것은 **화면 자체를 처리하는 것**이고, 그래서 나가는 쪽은 앞
// 화면을 구운 사진에 걸고 들어오는 쪽은 살아 있는 화면에 그대로 겁니다 — 같은 식에 값만
// 거꾸로 넣습니다.
//
// **갈아 끼우는 함수는 이 파일이 부릅니다.** 씬을 바꾸는 쪽은 「무엇을 할지」를 함수 하나로
// 넘기고, 「언제 할지」만 여기서 정합니다. 그러지 않으면 뷰 수십 개를 만드는 그 프레임이
// 멈춘 화면으로 보입니다.

import { Container, Rectangle, Sprite, Texture } from 'pixi.js'

import type { Data } from '../core/data'
import { TransitionKind as Kind } from '../generated/enums/transition-kind'
import { CROSS, CrossFilter } from '../shader/cross'

/** 화면을 지우는 방법. */
export type TransitionKind = 'fade' | 'blocks' | 'push' | 'burn' | 'slide' | 'ash'

/** 지금 어느 걸음인가. */
export type TransitionStage = 'off' | 'out' | 'hold' | 'in'

/** 갈리는 자리. **이름은 코드가 정하고 무엇을 할지는 아래 표가 정합니다.** */
export type TransitionId =
  'title_run' | 'run_title' | 'run_lost' | 'run_won' | 'run_restart'
  | 'login_title' | 'title_login' | 'boot_first'

export interface TransitionSpec {
  kind: TransitionKind
  /** 나가는 화면을 지우는 시간. 밀리초 */
  outMs: number
  /** 아무것도 보이지 않는 채로 머무는 시간. **갈아 끼우기가 길어지면 그만큼 깎입니다.** */
  holdMs: number
  /** 들어오는 화면을 되돌리는 시간 */
  inMs: number
  /** 다 지워진 자리에 남는 색 */
  ink: number
  /** 다가오는가. 밀림과 옆으로가 이 값으로 방향을 정합니다 */
  toward: boolean
  /** 시작할 때 나는 소리. 빈 값이면 나지 않습니다 */
  cue: string
}

/** 시트가 정하지 않은 자리에 쓰는 것. **표를 읽지 못해도 화면은 갈립니다.** */
const FALLBACK: TransitionSpec = {
  kind: 'fade', outMs: 160, holdMs: 0, inMs: 160, ink: 0x05070d, toward: true, cue: '',
}

/** 시트의 갈래를 화면의 이름으로. */
const KINDS: Record<number, TransitionKind> = {
  [Kind.Fade]: 'fade',
  [Kind.Blocks]: 'blocks',
  [Kind.Push]: 'push',
  [Kind.Burn]: 'burn',
  [Kind.Slide]: 'slide',
  [Kind.Ash]: 'ash',
}

/**
 * 자리마다의 전환을 시트에서 읽습니다.
 *
 * **방법도 길이도 데이터입니다.** 연출의 길이가 `Const_Feel` 인 것과 같은 규칙이고, 자리마다
 * 어느 방법인지를 고르는 것이므로 상수가 아니라 표입니다 — 시트에서 `kind` 를 바꾸면 화면이
 * 바뀝니다.
 *
 * **없는 자리는 짧은 잦아듦입니다.** 표에 줄이 빠져도 씬은 갈려야 하고, 갈아 끼우는 프레임은
 * 어느 경우에도 보이면 안 됩니다.
 */
export function readCrossings(data: Data): Crossings {
  const out = new Map<string, TransitionSpec>()
  for (const row of data.tables.transition.records) {
    out.set(row.transitionId, {
      kind: KINDS[row.kind] ?? 'fade',
      outMs: row.outMs,
      holdMs: row.holdMs,
      inMs: row.inMs,
      ink: colorOf(row.ink),
      toward: row.toward,
      // 시트의 빈 칸은 `-` 입니다. 소리가 없다는 뜻입니다.
      cue: row.cue === '-' ? '' : row.cue,
    })
  }
  return {
    of: (id: string) => out.get(id) ?? FALLBACK,
    quiet: out.get('quiet') ?? FALLBACK,
  }
}

/** 시트가 정한 전환들. */
export interface Crossings {
  /** 이 자리의 전환. 표에 없으면 짧은 잦아듦입니다. */
  of(id: TransitionId | string): TransitionSpec
  /** 전환을 줄였을 때 쓰는 것. **0이 아닙니다** — 갈아 끼우는 프레임은 보이면 안 됩니다. */
  quiet: TransitionSpec
}

/** `#rrggbb` 를 수로. */
function colorOf(text: string): number {
  const value = Number.parseInt(text.replace('#', ''), 16)
  return Number.isFinite(value) ? value : 0x05070d
}

export interface TransitionPeek {
  id: string
  stage: TransitionStage
  /** 얼마나 지워졌는가. 0 에서 1 */
  cover: number
  /** 앞 화면을 몇 번 구웠는가. **한 전환에 많아야 한 번입니다.** */
  shots: number
}

/**
 * 화면과 화면 사이.
 *
 * 층 하나에 사진 한 장과 바탕 한 장이 있고, 들어오는 쪽의 필터는 살아 있는 화면에
 * 걸립니다 — 그래서 **되돌아오는 동안에도 판은 움직입니다.** 사진을 한 장 더 구워 그것을
 * 되돌리면 카드가 깔리는 첫 몇백 밀리초가 멈춘 그림이 됩니다.
 */
export class Transition {
  /** 무대의 맨 위. 나가는 화면의 사진과 남는 바탕이 여기 있습니다. */
  readonly view = new Container()

  /**
   * 다 지워진 자리.
   *
   * **아무것도 보이지 않는다는 것을 이 한 장이 보증합니다.** 사진을 굽지 못하는 기계도
   * 있고, 갈아 끼우는 프레임은 어느 경우에도 보이면 안 됩니다.
   */
  private readonly backdrop = new Sprite(Texture.WHITE)
  /** 나가는 화면의 사진. 지워지는 것은 이 그림입니다. */
  private shot?: Sprite
  private readonly leaving = new CrossFilter()
  /** 들어오는 화면에 거는 것. **살아 있는 화면에 그대로 걸립니다.** */
  private readonly coming = new CrossFilter()

  private stage: TransitionStage = 'off'
  private spec: TransitionSpec = FALLBACK
  private id = ''
  /** 이 걸음에 흐른 시간. 밀리초 */
  private elapsed = 0
  private swap?: () => void
  private shots = 0
  /** 화면이 놓인 사각형. 무대의 좌표입니다. */
  private box = new Rectangle(0, 0, 1, 1)

  constructor(private readonly hooks: {
    /** 앞 화면을 그림 한 장으로 굽습니다. 못 구우면 비어 있습니다. */
    shoot: () => Texture | undefined
    /** 소리 하나. */
    play: (cue: string) => void
    /** 들어오는 화면. **여기에 필터가 걸립니다.** */
    screen: Container
  }) {
    this.view.addChild(this.backdrop)
    // **도는 동안에는 눌림을 삼킵니다.** 보이지 않는 화면 뒤의 단추가 눌리면 사람은 자기가
    // 무엇을 눌렀는지 볼 수 없습니다.
    this.view.eventMode = 'static'
    this.view.visible = false
  }

  /** 화면이 놓인 사각형을 받습니다. **판 밖은 잘라 낸 자리이므로 건드리지 않습니다.** */
  layout(x: number, y: number, width: number, height: number): void {
    this.box = new Rectangle(x, y, width, height)
    this.view.hitArea = this.box
    this.backdrop.position.set(x, y)
    this.backdrop.width = width
    this.backdrop.height = height
    if (this.shot) {
      this.shot.position.set(x, y)
      this.shot.width = width
      this.shot.height = height
    }
    const aspect = width / Math.max(1, height)
    this.leaving.setAspect(aspect)
    this.coming.setAspect(aspect)
  }

  get busy(): boolean {
    return this.stage !== 'off'
  }

  /** 지금 아무것도 보이지 않는가. */
  get covered(): boolean {
    return this.stage === 'hold'
  }

  peek(): TransitionPeek {
    return { id: this.id, stage: this.stage, cover: this.amount(), shots: this.shots }
  }

  /**
   * 지우고 · 갈고 · 되돌립니다.
   *
   * **도는 중에 다시 부르면 앞의 것을 끝냅니다.** 도는 동안에는 입력을 받지 않으므로
   * 사람이 겹쳐 부를 길이 없고, 코드가 겹쳐 부르는 자리에서는 앞의 갈아 끼우기가 빠지면
   * 안 됩니다.
   */
  play(id: TransitionId | string, spec: TransitionSpec, swap: () => void): void {
    if (this.busy) this.finish()
    this.id = id
    this.spec = spec
    this.swap = swap
    this.elapsed = 0
    this.shots = 0
    this.stage = 'out'
    this.view.visible = true
    this.prepare()
    if (spec.cue) this.hooks.play(spec.cue)
    this.paint()
    if (spec.outMs <= 0) this.crossOver()
  }

  /**
   * 되돌리기만 합니다.
   *
   * 로딩에서 넘어오는 자리입니다 — **지울 앞 화면이 없습니다.** 화면은 이미 없는 채이고,
   * 되돌리면 첫 화면이 드러납니다.
   */
  open(id: TransitionId | string, spec: TransitionSpec): void {
    if (this.busy) this.finish()
    this.id = id
    this.spec = spec
    this.swap = undefined
    this.shots = 0
    this.elapsed = 0
    this.stage = 'in'
    this.view.visible = true
    this.prepare()
    this.attach()
    this.backdrop.visible = true
    if (spec.cue) this.hooks.play(spec.cue)
    this.paint()
  }

  /** 화면의 시계를 받습니다. **손 시계로 돌면 수동 틱으로 세운 도구가 지나가지 못합니다.** */
  tick(seconds: number): void {
    if (this.stage === 'off') return
    this.elapsed += seconds * 1000

    if (this.stage === 'out' && this.elapsed >= this.spec.outMs) {
      this.crossOver()
      return
    }
    if (this.stage === 'hold' && this.elapsed >= this.spec.holdMs) {
      this.stage = 'in'
      this.elapsed = 0
      // 사진은 지워지는 동안에만 뜻이 있습니다. 되돌리는 것은 살아 있는 화면입니다.
      this.dropShot()
      this.backdrop.visible = false
    }
    if (this.stage === 'in' && this.elapsed >= this.spec.inMs) {
      this.finish()
      return
    }
    this.paint()
  }

  /**
   * 지금 자리에서 곧바로 끝냅니다.
   *
   * **갈아 끼우기는 빠지지 않습니다.** 아직 하지 않았으면 여기서 합니다 — 끝내는 것과
   * 하지 않는 것은 다릅니다.
   */
  finish(): void {
    if (this.stage === 'off') return
    const swap = this.swap
    this.swap = undefined
    if (swap) swap()
    this.stage = 'off'
    this.elapsed = 0
    this.view.visible = false
    this.backdrop.visible = false
    this.dropShot()
    this.detach()
  }

  // ------------------------------------------------------------------ 안쪽

  /** 아무것도 보이지 않는 자리. **갈아 끼우기는 여기서 일어납니다.** */
  private crossOver(): void {
    this.stage = 'hold'
    this.elapsed = 0
    // 들어오는 화면에 필터를 먼저 겁니다 — 다 지워진 값으로 걸리므로 이 프레임도 비어
    // 있습니다.
    this.attach()
    this.backdrop.visible = true
    this.paint()
    const swap = this.swap
    this.swap = undefined
    if (swap) swap()
  }

  /** 0 이면 그대로이고 1 이면 아무것도 보이지 않습니다. */
  private amount(): number {
    if (this.stage === 'off') return 0
    if (this.stage === 'hold') return 1
    if (this.stage === 'out') {
      return ease(Math.min(1, this.elapsed / Math.max(1, this.spec.outMs)))
    }
    return 1 - ease(Math.min(1, this.elapsed / Math.max(1, this.spec.inMs)))
  }

  private prepare(): void {
    const kind = CROSS[this.spec.kind]
    for (const filter of [this.leaving, this.coming]) {
      filter.kind = kind
      filter.ink = this.spec.ink
      filter.toward = this.spec.toward
      filter.amount = 0
    }
    this.backdrop.tint = this.spec.ink
    this.backdrop.alpha = 1
    this.backdrop.visible = false
    this.dropShot()
    if (this.stage === 'out') this.takeShot()
  }

  /**
   * 앞 화면을 그림 한 장으로.
   *
   * **한 전환에 한 번뿐입니다.** 매 프레임 구우면 화면 전체를 프레임마다 한 벌 더 그리는
   * 것이고, 그것은 지워지는 동안 내내입니다.
   *
   * **굽지 못하면 바탕만 남습니다.** 화면이 갈리는 것 자체는 그대로 됩니다.
   */
  private takeShot(): void {
    const texture = this.hooks.shoot()
    if (!texture) {
      this.backdrop.visible = true
      this.backdrop.alpha = 0
      return
    }
    this.shots++
    const sprite = new Sprite(texture)
    sprite.position.set(this.box.x, this.box.y)
    sprite.width = this.box.width
    sprite.height = this.box.height
    sprite.filters = [this.leaving]
    this.shot = sprite
    this.view.addChild(sprite)
  }

  private dropShot(): void {
    if (!this.shot) return
    this.view.removeChild(this.shot)
    this.shot.destroy({ texture: true })
    this.shot = undefined
  }

  /** 들어오는 화면에 필터를 겁니다. **걸린 동안에는 화면 전체가 한 번 더 그려집니다.** */
  private attach(): void {
    this.coming.amount = 1
    this.hooks.screen.filters = [this.coming]
  }

  private detach(): void {
    this.hooks.screen.filters = []
  }

  /** 이번 프레임의 값. **여기서 만드는 것이 없습니다.** */
  private paint(): void {
    const amount = this.amount()
    if (this.stage === 'out') {
      this.leaving.amount = amount
      // 사진을 굽지 못한 판에서는 바탕이 그 자리를 대신합니다.
      if (!this.shot) this.backdrop.alpha = amount
      return
    }
    this.backdrop.alpha = 1
    this.coming.amount = amount
  }
}

/** 가다 서다가 없는 곡선. 시작과 끝이 둘 다 잦아듭니다. */
function ease(t: number): number {
  return t * t * (3 - 2 * t)
}
