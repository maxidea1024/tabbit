// 흩어지는 알갱이가 그려지는 층.
//
// **알갱이는 판보다 오래 삽니다.** 판이 다 삭으면 그 카드는 그 프레임에 지워지고, 알갱이는
// 그 뒤로 1초 남짓 더 흩어집니다 — 그래서 알갱이를 카드에 달아 둘 수 없고, 무대에 층 하나가
// 따로 있어야 합니다. 카드가 없어지는 프레임에 마지막 자리가 그대로 굳고, 그 자리에서
// 이어서 흩어집니다.
//
// **넷을 미리 만들어 돌려 씁니다.** 매 프레임 만들지 않는 규약이고(`doc/performance.md`),
// 한 판에 동시에 삭는 것이 넷을 넘는 일은 드뭅니다 — 넘으면 가장 오래된 것을 놓습니다.
//
// **이 층이 없으면 판만 삭습니다.** 알갱이를 그릴 수 없는 기계에서도 연출은 성립합니다 —
// 판이 위에서 아래로 제대로 풀리고, 풀린 것이 날아가지 않을 뿐입니다.

import { Container, Matrix, Rectangle, Renderer, Texture } from 'pixi.js'

import { coarsePointer } from '../shader/device'
import { CardMotes, MOTES_HOLD, motesSupported } from '../shader/motes'

/** 한꺼번에 흩어질 수 있는 판의 수. */
const POOL = 4

/** 판 하나가 흩어지는 동안 그것을 가리키는 것. */
export interface MotesHandle {
  /**
   * 알갱이가 놓이는 자리.
   *
   * **판을 그리는 그 노드를 넘깁니다.** 알갱이의 좌표는 그 노드의 안쪽 좌표와 같으므로,
   * 노드가 기울고 떠오르면 알갱이도 그대로 따라갑니다.
   */
  place(source: Container): void
}

interface Live {
  motes: CardMotes
  age: number
  /** 구운 판. **끝에서 버립니다** — 판 하나가 그림 하나이고, 그대로 두면 쌓입니다. */
  shot: Texture
}

/** 자리를 옮겨 담는 데 쓰는 것. **매 프레임 만들지 않습니다.** */
const SCRATCH = new Matrix()

export class MotesLayer extends Container {
  private readonly free: CardMotes[] = []
  private readonly live: Live[] = []
  /** 이 기계가 알갱이를 그릴 수 있는가. `setup` 이 정합니다. */
  private ready = false

  constructor() {
    super()
    this.eventMode = 'none'
  }

  /**
   * 쓸 수 있는지 확인하고 넷을 만듭니다. **화면을 세울 때 한 번입니다.**
   *
   * **손가락으로 쓰는 기계에서는 켜지 않습니다.** 그쪽은 노이즈 그림 석 장만 읽으므로
   * 바람의 결이 없고, 결이 없으면 알갱이가 아래로 고르게 내려 밋밋합니다 — 판만 삭는 것이
   * 그보다 낫습니다.
   */
  setup(renderer: Renderer): void {
    if (this.ready || coarsePointer() || !motesSupported(renderer)) return
    this.ready = true
    for (let i = 0; i < POOL; i++) {
      const motes = new CardMotes()
      this.free.push(motes)
      this.addChild(motes.view)
    }
  }

  /**
   * 이 판을 흩기 시작합니다. 쓸 수 없으면 아무것도 돌려주지 않습니다.
   *
   * **거는 것보다 먼저 부릅니다.** 판을 굽는 것이 여기서 일어나므로, 필터를 이미 걸어 둔
   * 뒤에 부르면 삭기 시작한 판이 구워집니다.
   */
  take(shot: Texture | undefined, width: number, height: number, down = true): MotesHandle | undefined {
    if (!this.ready || !shot) return undefined
    const motes = this.free.pop() ?? this.recycle()
    if (!motes) return undefined
    motes.begin(shot, width, height, down)
    this.live.push({ motes, age: 0, shot })
    return {
      place: (source: Container) => {
        SCRATCH.copyFrom(this.worldTransform).invert().append(source.worldTransform)
        motes.view.setFromMatrix(SCRATCH)
      },
    }
  }

  /** 가장 오래된 것을 놓습니다. **넷이 다 흩어지고 있을 때입니다.** */
  private recycle(): CardMotes | undefined {
    const oldest = this.live.shift()
    if (!oldest) return undefined
    this.drop(oldest)
    return oldest.motes
  }

  /** 하나를 놓습니다. 구운 판도 함께 버립니다. */
  private drop(one: Live): void {
    one.motes.end()
    one.shot.destroy(true)
  }

  advance(seconds: number): void {
    for (let i = this.live.length - 1; i >= 0; i--) {
      const one = this.live[i]
      one.age += seconds
      one.motes.age = one.age
      if (one.age < MOTES_HOLD) continue
      this.drop(one)
      this.free.push(one.motes)
      this.live.splice(i, 1)
    }
  }
}

/**
 * 지금 무대에 있는 층과 판을 구울 렌더러.
 *
 * **뷰가 층을 모르게 하는 것입니다.** 카드와 딱지는 그리는 것만 하는 것들이고, 무대의
 * 어느 층에 알갱이가 놓이는지는 그것들이 알 일이 아닙니다 — `card-back.ts` 가 굽는
 * 렌더러를 들고 있는 것과 같은 꼴입니다.
 */
let stage: { layer: MotesLayer; renderer: Renderer; density: number } | undefined

export function useMotesLayer(layer: MotesLayer, renderer: Renderer, density: number): void {
  layer.setup(renderer)
  stage = { layer, renderer, density: Math.min(2, Math.max(1, density)) }
}

/**
 * 이 노드가 그리는 판을 흩기 시작합니다.
 *
 * **한 번 굽습니다.** 삭는 내내 매 프레임 판을 읽는 것이 아니라, 시작하는 그 프레임에
 * 그림 하나로 굽고 알갱이가 그 그림에서 색을 읽습니다.
 */
export function startMotes(source: Container, width: number, height: number): MotesHandle | undefined {
  if (!stage) return undefined
  let shot: Texture | undefined
  try {
    shot = stage.renderer.generateTexture({
      target: source,
      resolution: stage.density,
      antialias: true,
      // 경계를 직접 줍니다. 마스크가 있는 노드의 경계를 세는 데 기대지 않습니다 —
      // `card-back.ts` 가 구울 때와 같은 이유입니다.
      frame: new Rectangle(0, 0, width, height),
    })
  } catch {
    // 굽지 못하면 판만 삭습니다.
    return undefined
  }
  const handle = stage.layer.take(shot, width, height)
  if (!handle) shot.destroy(true)
  return handle
}
