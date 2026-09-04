// 환희의 순간.
//
// **칩 × 배수가 문턱을 넘으면 그 판이 특별한 판이 됩니다.** 그때는 배경의 프랙탈이
// 물러나고 기를 모으는 겹이 그 자리에 오르고, 점수가 정산되는 순간에 터집니다.
//
// 이 파일은 **언제 · 어느 연출**을 정합니다. 그림은 둘 중 하나입니다 —
//
// - `public/effect/<이름>.mp4` 가 있으면 그 영상
// - 없으면 [`shader/surge.ts`](../shader/surge.ts) 의 셰이더
//
// **영상이 없어도 화면이 돌아야 합니다.** 영상은 저작권이 있는 파일이라 저장소에 들어가지
// 않고, 그래서 받아 둔 기계에서만 있습니다 — 그림을 못 읽으면 문양을 그리는
// [`art.ts`](art.ts) 와 같은 규칙입니다. 파일의 규격은 `doc/presentation.md` 에 있습니다.
//
// 규격은 `doc/presentation.md` 의 「환희의 순간」입니다.
//
// **겹 하나를 따로 두는 이유**는 넘어가는 것이 보여야 하기 때문입니다. 배경 셰이더 안에
// 넣으면 유니폼 하나로 갈아치우는 것이 되어 한 프레임에 딴 화면이 됩니다.

import { Container, Sprite, Texture, VideoSource } from 'pixi.js'

import { SurgeFilter } from '../shader/surge'

/** 문턱 하나와 거기서 도는 연출. */
export interface EuphoriaTier {
  /** 이 값 이상이면 이 줄입니다. 칩 × 배수입니다. */
  atLeast: number
  /** 어느 연출인가. `public/effect/<이름>.mp4` 이고, 없으면 셰이더로 갑니다. */
  visual: string
}

/**
 * 문턱의 표.
 *
 * **`design-data` 로 옮길 자리입니다.** 문턱마다 다른 영상을 거는 표이므로 `Euphoria` 표가
 * 되고, 그때 이 배열이 없어집니다 — 유니티도 같은 문턱을 읽어야 하므로 값이 코드에 남아
 * 있을 자리가 아닙니다.
 *
 * 첫 줄이 낮은 것은 **확인용**입니다. 40만은 안티 3~4에서 나오는 값이라 연출이 제대로 도는지를
 * 눈으로 확인할 수 있고, 문턱을 정하는 것은 그다음 일입니다. 열 배씩 올라갑니다.
 */
const TIERS: readonly EuphoriaTier[] = [
  { atLeast: 400_000, visual: 'ki_gather' },
  { atLeast: 4_000_000, visual: 'ki_wave' },
  { atLeast: 40_000_000, visual: 'ki_burst' },
  { atLeast: 400_000_000, visual: 'ki_roar' },
]

/** 이 곱이 어느 줄에 드는가. 문턱을 넘지 않으면 아무 줄도 아닙니다. */
export function euphoriaTierOf(product: number): EuphoriaTier | undefined {
  let found: EuphoriaTier | undefined
  for (const tier of TIERS) {
    if (product < tier.atLeast) continue
    if (!found || tier.atLeast > found.atLeast) found = tier
  }
  return found
}

/** 영상이 놓인 곳. */
const REEL_DIR = './effect'

/** 겹이 오르는 시간. **빠르면 갈아치운 것으로 보입니다.** */
const FADE_IN = 0.42
/** 물러나는 시간. 오르는 것보다 느립니다 — 끝난 것은 서두를 이유가 없습니다. */
const FADE_OUT = 0.85
/** 모으는 정도가 0에서 1까지 오르는 시간. 득점 한 판이 도는 시간과 비슷하게 둡니다. */
const CHARGE_SPAN = 2.2
/** 터짐이 잦아드는 시간. */
const BURST_SPAN = 0.9
/** 터진 뒤 이 겹이 남아 있는 시간. 점수가 굴러가는 것을 이 배경 위에서 봅니다. */
const LINGER = 1.15
/**
 * 터지지 않은 채 버티는 가장 긴 시간.
 *
 * **정산이 오지 않는 길이 있습니다** — 판을 떠나거나 연출이 끊기는 경우입니다. 그때
 * 겹이 남아 있으면 상점 배경이 계속 기를 모으고 있게 됩니다.
 */
const HOLD_MOST = 12
/**
 * 영상의 밝기.
 *
 * **판이 읽혀야 합니다.** 영상은 배경으로 그려지는 것이지 화면의 주인이 아니고, 그대로
 * 두면 흰 카드의 글씨와 밝은 장면에서 그 위의 모든 글이 사라집니다.
 */
const REEL_TINT = 0xb0b4bc
/**
 * 영상의 가장 짙은 값.
 *
 * **1 로 두지 않습니다.** 아래의 프랙탈이 계속 흐르고 있고 그것이 어둡습니다 — 영상을
 * 반쯤 비쳐 두면 밝은 장면에서도 판이 읽히고, 배경이 갈아치워진 것이 아니라 그 위로
 * 겹친 것으로 보입니다.
 */
const REEL_MOST = 0.55

/**
 * 짙기를 주소에서 덮어씁니다. `?reel=1` 이면 영상이 그대로 보입니다.
 *
 * **시험용 손잡이입니다.** 0.55 가 판이 읽히는 값이지만 영상 자체를 보고 고르는 동안에는
 * 그대로 봐야 하고, 그때는 밝기도 누르지 않습니다 — 눌러 놓고 「어둡다」로 판단하면 영상을
 * 잘못 고릅니다.
 */
const ASKED = typeof location === 'undefined'
  ? null : new URLSearchParams(location.search).get('reel')
const REEL_ALPHA = ASKED !== null && Number.isFinite(Number(ASKED))
  ? Math.max(0, Math.min(1, Number(ASKED))) : REEL_MOST
/** 주소로 짙기를 정했으면 밝기는 그대로 둡니다. */
const REEL_SHADE = ASKED !== null ? 0xffffff : REEL_TINT

/** 지금 무엇을 하고 있는가. 검증 도구가 읽습니다. */
export type EuphoriaPhase = 'off' | 'charge' | 'burst' | 'fade'

/** 영상 한 편. 읽지 못한 것도 기록으로 남습니다 — 다시 시도하지 않기 위한 것입니다. */
interface Reel {
  video: HTMLVideoElement
  sprite: Sprite
  /** 첫 프레임이 왔는가. 오기 전에는 셰이더가 그 자리를 맡습니다. */
  ready: boolean
  /** 읽지 못했는가. 파일이 없는 기계에서 그렇습니다. */
  failed: boolean
}

export class Euphoria {
  /** 배경 위에 얹히는 겹. 셰이더와 영상이 이 안에 있습니다. */
  readonly view = new Container()

  private readonly shade = new Sprite(Texture.WHITE)
  private readonly filter = new SurgeFilter()
  /** 연출 이름마다 영상 하나. 읽는 것은 그 문턱을 처음 넘을 때입니다. */
  private readonly reels = new Map<string, Reel>()
  /** 겹이 놓이는 사각형. 영상을 이 안에 채웁니다. */
  private box = { x: 0, y: 0, width: 0, height: 0 }
  /** 모으는 중인가. 문턱을 넘으면 참이 되고 터진 뒤 남는 시간이 지나면 거짓입니다. */
  private holding = false
  private charge = 0
  private burst = 0
  private fade = 0
  private held = 0
  private linger = 0
  private tier?: EuphoriaTier

  constructor() {
    this.shade.filters = [this.filter]
    this.shade.visible = false
    this.view.addChild(this.shade)
  }

  /** 지금 어느 줄인가. 아무 줄도 아니면 비어 있습니다. */
  get current(): EuphoriaTier | undefined {
    return this.tier
  }

  get phase(): EuphoriaPhase {
    if (this.fade <= 0.002) return 'off'
    // **터진 뒤 남는 시간까지가 터짐입니다.** 번쩍임이 잦아드는 것은 0.9초이고 겹이 남는
    // 것은 1.15초이므로, 번쩍임만 보면 그 사이의 0.25초가 다시 모으는 것으로 읽힙니다.
    if (this.linger > 0 || this.burst > 0.002) return 'burst'
    return this.holding ? 'charge' : 'fade'
  }

  /** 검증 도구가 읽는 값들. */
  peek(): {
    phase: EuphoriaPhase; visual?: string; charge: number; fade: number; reel: boolean
  } {
    const reel = this.tier ? this.reels.get(this.tier.visual) : undefined
    return {
      phase: this.phase,
      visual: this.tier?.visual,
      charge: Math.round(this.charge * 1000) / 1000,
      fade: Math.round(this.fade * 1000) / 1000,
      // 지금 그려지는 것이 영상인가. 거짓이면 셰이더입니다.
      reel: reel?.ready === true && !reel.failed,
    }
  }

  /**
   * 첫 문턱의 영상을 미리 읽습니다. **판에 들어설 때 한 번 부릅니다.**
   *
   * 문턱을 넘는 순간에 읽기 시작하면 그 판의 앞부분이 셰이더로 지나갑니다 — 파일 하나가
   * 3MB 이므로 그때 읽으면 늦습니다. **넷을 한꺼번에 읽지는 않습니다**: 안티 1에서 마지막
   * 줄의 영상을 읽을 이유가 없고, 다음 줄은 이 줄이 나올 때 읽어 둡니다.
   */
  warm(): void {
    if (TIERS.length > 0) this.load(TIERS[0].visual)
  }

  /**
   * 이 곱이 문턱을 넘었는가.
   *
   * **박자마다 불립니다.** 조커가 배수를 올리는 도중에 넘어가므로, 정산까지 기다리면
   * 모으는 것 없이 터지는 것만 남습니다.
   */
  consider(product: number): void {
    const tier = euphoriaTierOf(product)
    if (!tier) return
    // **더 높은 줄로는 올라가고 내려가지는 않습니다.** 한 판의 곱은 오르기만 합니다.
    const climbed = !this.tier || tier.atLeast > this.tier.atLeast
    if (climbed) {
      this.tier = tier
      this.begin(tier)
      // 다음 줄을 미리 읽어 둡니다. 이 판에서 더 오를 수 있습니다.
      const next = TIERS.find(one => one.atLeast > tier.atLeast)
      if (next) this.load(next.visual)
    }
    this.holding = true
    this.held = 0
    this.linger = 0
  }

  /** 정산하는 순간. 모으던 것이 터집니다. */
  release(): void {
    if (!this.holding) return
    this.burst = 1
    this.linger = LINGER
  }

  /** 이 판은 끝났습니다. 남은 것은 물러나며 사라집니다. */
  done(): void {
    this.holding = false
    this.linger = 0
  }

  /** 씬이 바뀌었습니다. **남기지 않습니다** — 다른 화면에 기가 남아 있을 자리가 없습니다. */
  reset(): void {
    this.holding = false
    this.charge = 0
    this.burst = 0
    this.fade = 0
    this.held = 0
    this.linger = 0
    this.tier = undefined
    this.shade.visible = false
    for (const reel of this.reels.values()) this.stop(reel)
  }

  advance(seconds: number): void {
    if (this.holding) {
      // 터진 뒤에는 남는 시간만 세고, 그 시간이 지나면 물러납니다.
      if (this.linger > 0) {
        this.linger -= seconds
        if (this.linger <= 0) this.holding = false
      } else {
        this.held += seconds
        if (this.held > HOLD_MOST) this.holding = false
      }
    }

    const want = this.holding ? 1 : 0
    const rate = seconds / (this.holding ? FADE_IN : FADE_OUT)
    this.fade += Math.max(-rate, Math.min(rate, want - this.fade))
    if (this.holding) this.charge = Math.min(1, this.charge + seconds / CHARGE_SPAN)
    this.burst = Math.max(0, this.burst - seconds / BURST_SPAN)

    // **다 물러났으면 줄도 놓습니다.** 다음 판이 이 값을 물려받으면 문턱을 넘지 않은
    // 판에서 겹이 오릅니다.
    if (!this.holding && this.fade <= 0.002) {
      this.fade = 0
      this.charge = 0
      if (this.tier) {
        const reel = this.reels.get(this.tier.visual)
        if (reel) this.stop(reel)
      }
      this.tier = undefined
    }

    const reel = this.tier ? this.reels.get(this.tier.visual) : undefined
    // 영상이 있으면 영상이고, 없거나 아직 첫 프레임이 오지 않았으면 셰이더입니다.
    const playing = this.fade > 0.002 && reel !== undefined && reel.ready && !reel.failed

    for (const one of this.reels.values()) {
      const mine = one === reel && playing
      one.sprite.visible = mine
      if (mine) one.sprite.alpha = this.fade * REEL_ALPHA
    }
    if (playing && reel) this.fit(reel)

    // **보이지 않으면 필터도 돌지 않습니다.** 화면 한 장을 한 번 더 굽는 일이므로
    // 겹이 없는 동안에는 스프라이트를 끕니다.
    this.shade.visible = this.fade > 0.002 && !playing
    if (!this.shade.visible) return

    this.filter.advance(seconds)
    this.filter.setLevels(this.charge, this.burst, this.fade)
  }

  /** 자리와 크기. 배경과 같은 사각형입니다. */
  layout(x: number, y: number, width: number, height: number): void {
    this.box = { x, y, width, height }
    this.shade.position.set(x, y)
    this.shade.width = width
    this.shade.height = height
    for (const reel of this.reels.values()) this.fit(reel)
  }

  setMood(ink: [number, number, number], glow: [number, number, number]): void {
    this.filter.setMood(ink, glow)
  }

  setCenter(x: number, y: number): void {
    this.filter.setCenter(x, y)
  }

  setAspect(aspect: number): void {
    this.filter.setAspect(aspect)
  }

  // ---------------------------------------------------------------- 영상

  /**
   * 영상을 채워 넣습니다.
   *
   * **비율을 지키고 넘치는 쪽을 잘라 냅니다.** 판의 사각형으로 늘리면 4:3 영상이 옆으로
   * 퍼지고, 안쪽에 맞추면 위아래에 검은 띠가 남습니다 — 판 밖은 무대의 마스크가 이미
   * 잘라 내므로 넘치게 두면 됩니다.
   */
  private fit(reel: Reel): void {
    const vw = reel.video.videoWidth
    const vh = reel.video.videoHeight
    if (vw === 0 || vh === 0) return
    const scale = Math.max(this.box.width / vw, this.box.height / vh)
    reel.sprite.width = vw * scale
    reel.sprite.height = vh * scale
    reel.sprite.position.set(
      this.box.x + (this.box.width - vw * scale) / 2,
      this.box.y + (this.box.height - vh * scale) / 2)
  }

  /** 이 줄의 영상을 처음부터 돌립니다. 없으면 아무것도 하지 않습니다. */
  private begin(tier: EuphoriaTier): void {
    const reel = this.load(tier.visual)
    if (!reel || reel.failed) return
    reel.video.currentTime = 0
    // **막힌 것은 오류가 아닙니다.** 소리를 끈 영상은 어느 브라우저에서나 돌지만, 막히면
    // 셰이더가 그 자리를 맡습니다.
    void reel.video.play().catch(() => { reel.failed = true })
  }

  private stop(reel: Reel): void {
    reel.sprite.visible = false
    if (!reel.failed) reel.video.pause()
  }

  /**
   * 영상 하나를 읽습니다. 이미 읽었으면 그것을 돌려줍니다.
   *
   * **소리는 끕니다.** 게임에 이미 배경음과 효과음이 있고, 영상의 소리까지 나면 그 둘이
   * 겹칩니다 — 소리를 끈 영상만이 사람이 누르지 않아도 돌 수 있는 것이기도 합니다.
   */
  private load(name: string): Reel | undefined {
    const seen = this.reels.get(name)
    if (seen) return seen
    if (typeof document === 'undefined') return undefined

    const video = document.createElement('video')
    video.src = `${REEL_DIR}/${name}.mp4`
    video.loop = true
    video.muted = true
    video.playsInline = true
    video.preload = 'auto'

    // **읽는 것은 우리가 시킵니다.** `autoLoad` 를 두면 Pixi 가 `void load()` 로 부르고,
    // 파일이 없는 기계에서 그 약속이 아무도 받지 않는 거부가 되어 콘솔에 오류로 남습니다 —
    // 없는 파일은 오류가 아니므로 그 자리에서 받습니다.
    const source = new VideoSource({
      resource: video, autoPlay: false, autoLoad: false, loop: true, muted: true,
    })
    const sprite = new Sprite(new Texture({ source }))
    sprite.tint = REEL_SHADE
    sprite.visible = false
    this.view.addChild(sprite)

    const reel: Reel = { video, sprite, ready: false, failed: false }
    this.reels.set(name, reel)
    // **첫 프레임이 온 다음부터 그립니다.** 그 전에 그리면 빈 텍스처가 한 프레임 보이고,
    // 파일이 없으면 셰이더가 그 자리를 맡습니다 — **화면이 서지 않는 일은 없습니다.**
    void source.load().then(() => {
      reel.ready = true
      this.fit(reel)
      // 읽히기를 기다리는 동안 문턱을 넘었으면 그때부터 돌립니다.
      if (this.holding && this.tier?.visual === name) this.begin(this.tier)
    }).catch(() => { reel.failed = true })
    return reel
  }
}
