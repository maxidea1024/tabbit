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
//
// **필터가 둘 벌입니다.** 다섯은 `CrossFilter` 하나가 `uKind` 로 가르고, 재는 `AshFilter` 가
// 따로입니다 — 셰이더가 둘(데스크탑·핸드폰)이고 그 위에 파티클이 얹힐 수 있어서, 하나의
// 유니폼으로는 갈 수 없습니다. 어느 것인지는 그래픽 품질이 정합니다.

import { Container, Rectangle, Sprite, Texture } from 'pixi.js'
import type { Filter, Renderer } from 'pixi.js'

import type { Data } from '../core/data'
import { TransitionKind as Kind } from '../generated/enums/transition-kind'
import { AshFilter } from '../shader/ash'
import type { AshParams } from '../shader/ash'
import { CROSS, CrossFilter } from '../shader/cross'
import { coarsePointer } from '../shader/device'
import { AshEmbers, embersSupported } from '../shader/embers'

/**
 * 알갱이가 있을 때 셰이더가 물러나는 몫.
 *
 * **둘이 같은 것을 그리면 안 됩니다.** 알갱이 8만 개가 고운 재를 그리는데 셰이더가 제
 * 굵은 재를 그 위에 겹치면, 고운 것 위에 굵은 것이 얹혀 굵은 쪽만 보입니다 — 셰이더는
 * 성한 판과 삭아 뚫리는 앞만 맡고 날아가는 것은 알갱이에게 넘깁니다.
 */
const WITH_EMBERS: Partial<AshParams> = {
  fragmentAmount: 0,
  ashAmount: 0.10,
  smokeAmount: 0.35,
}

/** 화면을 지우는 방법. */
export type TransitionKind = 'fade' | 'blocks' | 'push' | 'burn' | 'slide' | 'ash'

/** 지금 어느 걸음인가. */
export type TransitionStage = 'off' | 'out' | 'hold' | 'in'

/**
 * 그래픽 품질. **옵션의 `auto` 를 푼 뒤의 값입니다.**
 *
 * |값|재|
 * |--|--|
 * |`high`|데스크탑 셰이더 + 파티클. 기계가 파티클을 못 하면 파티클만 빠집니다|
 * |`medium`|데스크탑 셰이더|
 * |`low`|핸드폰 셰이더. 되풀이가 없고 그림이 둘입니다|
 */
export type QualityLevel = 'high' | 'medium' | 'low'

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

/** 갈아 끼우는 동안 덮는 색. **거의 검정입니다** — 화면이 지워졌다 돌아옵니다. */
const FADE_INK = 0x05070d

/** 시트가 정하지 않은 자리에 쓰는 것. **표를 읽지 못해도 화면은 갈립니다.** */
const FALLBACK: TransitionSpec = {
  kind: 'fade', outMs: 160, holdMs: 0, inMs: 160, ink: FADE_INK, toward: true, cue: '',
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
  return Number.isFinite(value) ? value : FADE_INK
}

export interface TransitionPeek {
  id: string
  stage: TransitionStage
  /** 얼마나 지워졌는가. 0 에서 1 */
  cover: number
  /** 앞 화면을 몇 번 구웠는가. **한 전환에 많아야 한 번입니다.** */
  shots: number
  /** 지금의 그래픽 품질 */
  quality: QualityLevel
  /** 파티클이 실제로 있는가. `high` 인데 거짓이면 기계가 못 하는 것입니다 */
  particles: boolean
}

/**
 * 화면을 처리하는 필터가 갖추어야 하는 것. `CrossFilter` 와 `AshFilter` 가 둘 다 이것입니다.
 */
interface Wipe extends Filter {
  amount: number
  ink: number
  toward: boolean
  setAspect(value: number): void
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
  /** 재. 품질이 바뀌면 새로 만듭니다 — 셰이더가 둘이라 유니폼으로 오갈 수 없습니다. */
  private ashOut!: AshFilter
  private ashIn!: AshFilter
  /** 재의 파티클. `high` 이고 기계가 되는 때에만 있습니다. */
  private embers?: AshEmbers
  private level: QualityLevel = coarsePointer() ? 'low' : 'high'
  private tuned: Partial<AshParams> = {}
  /** 렌더러가 있는 채로 파티클을 물어봤는가. **처음 만들어질 때는 렌더러가 아직 없습니다.** */
  private probed = false

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
    /** 렌더러. 파티클을 그릴 수 있는 기계인지를 이것으로 봅니다. 없으면 파티클이 없습니다. */
    renderer?: () => Renderer | undefined
  }) {
    this.view.addChild(this.backdrop)
    // **도는 동안에는 눌림을 삼킵니다.** 보이지 않는 화면 뒤의 단추가 눌리면 사람은 자기가
    // 무엇을 눌렀는지 볼 수 없습니다.
    this.view.eventMode = 'static'
    this.view.visible = false
    this.rebuildAsh()
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
    for (const filter of this.wipes()) filter.setAspect(aspect)
    this.embers?.layout(x, y, width, height)
  }

  /**
   * 그래픽 품질.
   *
   * **옵션이 정하고, 도구가 뒤집습니다.** 핸드폰의 셰이더는 그 기계에서만 도는 길이라, 여기서
   * 켜 보지 않으면 그 모습을 아무도 보지 않은 채로 나갑니다.
   */
  set quality(level: QualityLevel) {
    // 같은 값이어도 「높음」인데 아직 렌더러 없이 물어본 채라면 다시 만듭니다.
    if (level === this.level && (level !== 'high' || this.probed)) return
    this.level = level
    this.rebuildAsh()
  }

  get quality(): QualityLevel {
    return this.level
  }

  /** 재의 파라미터를 바꿉니다. 고르는 동안 쓰는 자리이고, 품질이 바뀌어도 남습니다. */
  tuneAsh(params: Partial<AshParams>): void {
    this.tuned = { ...this.tuned, ...params }
    this.ashOut.tune(params)
    this.ashIn.tune(params)
    this.embers?.tune(params)
  }

  get busy(): boolean {
    return this.stage !== 'off'
  }

  /** 지금 아무것도 보이지 않는가. */
  get covered(): boolean {
    return this.stage === 'hold'
  }

  peek(): TransitionPeek {
    return {
      id: this.id, stage: this.stage, cover: this.amount(), shots: this.shots,
      quality: this.level, particles: this.embers !== undefined,
    }
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

  /** 재의 필터 둘과 파티클을 품질에 맞게 다시 만듭니다. */
  private rebuildAsh(): void {
    const aspect = this.box.width / Math.max(1, this.box.height)
    const busyOnAsh = this.busy && this.spec.kind === 'ash'
    // **도는 중이면 끝내고 바꿉니다.** 걸려 있는 필터를 버리면 그 프레임이 비어 보입니다.
    if (busyOnAsh) this.finish()
    if (this.ashOut) this.ashOut.destroy()
    if (this.ashIn) this.ashIn.destroy()
    if (this.embers) {
      this.embers.view.removeFromParent()
      this.embers.destroy()
      this.embers = undefined
    }
    // **파티클은 「높음」이고 기계가 되는 때에만입니다.** 못 하는 기계에서는 셰이더만으로
    // 갑니다 — 그것만으로도 화면은 같은 모습으로 부서지고, 파티클은 그 위에 얹는 것입니다.
    const renderer = this.hooks.renderer?.()
    this.probed = renderer !== undefined
    if (this.level === 'high' && embersSupported(renderer)) {
      try {
        this.embers = new AshEmbers(undefined, this.tuned)
        this.embers.layout(this.box.x, this.box.y, this.box.width, this.box.height)
      } catch {
        this.embers = undefined
      }
    }

    // **알갱이가 있으면 나가는 쪽의 셰이더가 물러납니다.** 위의 `WITH_EMBERS`.
    //
    // **되돌아오는 쪽은 물러나지 않습니다.** 알갱이는 사진 위에 얹히는 것이고 되돌아오는
    // 걸음에는 사진이 없습니다(`dropShot`) — 알갱이가 색을 읽을 자리가 없으므로 그 걸음은
    // 셰이더 혼자입니다. 그런데 나가는 쪽과 같은 값을 주고 있어서, 고운 재와 조각이 0 인
    // 채로 굵은 덮개만 남았습니다 — **사라질 때는 자연스럽고 다시 나올 때만 투박한** 것이
    // 이것입니다.
    const shaped = this.embers ? { ...this.tuned, ...WITH_EMBERS } : this.tuned
    this.ashOut = new AshFilter(this.level === 'low', shaped)
    this.ashIn = new AshFilter(this.level === 'low', this.tuned)
    this.ashOut.setAspect(aspect)
    this.ashIn.setAspect(aspect)
  }

  private wipes(): Wipe[] {
    return [this.leaving, this.coming, this.ashOut, this.ashIn]
  }

  /** 이 전환의 나가는 쪽 필터. 재면 재의 것입니다. */
  private outFilter(): Wipe {
    return this.spec.kind === 'ash' ? this.ashOut : this.leaving
  }

  private inFilter(): Wipe {
    return this.spec.kind === 'ash' ? this.ashIn : this.coming
  }

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
    const kind = this.spec.kind
    if (kind !== 'ash') {
      this.leaving.kind = CROSS[kind]
      this.coming.kind = CROSS[kind]
    }
    for (const filter of [this.outFilter(), this.inFilter()]) {
      filter.ink = this.spec.ink
      filter.toward = this.spec.toward
      filter.amount = 0
    }
    if (this.embers) {
      this.embers.ink = this.spec.ink
      this.embers.toward = this.spec.toward
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
   *
   * **재의 파티클은 사진 위에 얹힙니다.** 파티클의 색이 그 사진에서 오므로 사진이 없으면
   * 파티클도 없습니다.
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
    sprite.filters = [this.outFilter()]
    this.shot = sprite
    this.view.addChild(sprite)
    if (this.spec.kind === 'ash' && this.embers) {
      this.embers.shot = texture
      this.embers.amount = 0
      this.view.addChild(this.embers.view)
    }
  }

  private dropShot(): void {
    if (this.embers) {
      // **사진을 버리기 전에 놓습니다.** 버려진 그림을 가리키는 채로 그리면 안 됩니다.
      this.embers.shot = undefined
      this.embers.view.removeFromParent()
    }
    if (!this.shot) return
    this.view.removeChild(this.shot)
    // **바탕까지 버립니다.** `texture` 만 참이면 Pixi 는 `Texture` 만 버리고 그 바탕
    // (`TextureSource`)은 그대로 둡니다 — 그 바탕이 곧 GPU 의 그림 한 장과 그것을 그리는
    // 틀이고, 그것을 버려야 스텐실 버퍼까지 함께 풀립니다. 두지 않았더니 전환 한 번마다
    // 화면 한 장이 GPU 에 남았습니다: 배율 1에서 4MB, 2에서 16MB이고, 렌더 텍스처는
    // Pixi 의 그림 수거 대상이 아니므로(`autoGarbageCollect` 가 거짓) 판을 접었다 펼
    // 때마다 그만큼 쌓이기만 했습니다.
    this.shot.destroy({ texture: true, textureSource: true })
    this.shot = undefined
  }

  /** 들어오는 화면에 필터를 겁니다. **걸린 동안에는 화면 전체가 한 번 더 그려집니다.** */
  private attach(): void {
    const filter = this.inFilter()
    filter.amount = 1
    this.hooks.screen.filters = [filter]
  }

  private detach(): void {
    this.hooks.screen.filters = []
  }

  /** 이번 프레임의 값. **여기서 만드는 것이 없습니다.** */
  private paint(): void {
    const amount = this.amount()
    if (this.stage === 'out') {
      this.outFilter().amount = amount
      if (this.embers && this.spec.kind === 'ash') this.embers.amount = amount
      // 사진을 굽지 못한 판에서는 바탕이 그 자리를 대신합니다.
      if (!this.shot) this.backdrop.alpha = amount
      return
    }
    this.backdrop.alpha = 1
    this.inFilter().amount = amount
  }
}

/** 가다 서다가 없는 곡선. 시작과 끝이 둘 다 잦아듭니다. */
function ease(t: number): number {
  return t * t * (3 - 2 * t)
}
