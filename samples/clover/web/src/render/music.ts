// 배경음.
//
// **화면마다 한 곡이고, 바뀔 때는 겹쳐서 넘어갑니다.** 곡을 끊고 다음 곡을 시작하면 그
// 끊긴 자리가 「화면이 바뀌었다」보다 크게 들립니다 — 앞의 것이 잦아드는 동안 뒤의 것이
// 올라오면 넘어간 것을 알아채지 못한 채로 분위기만 바뀝니다.
//
// 효과음과 길이 다릅니다. 효과음은 신호 하나에 한 번이지만 배경음은 **계속 돌고**, 그래서
// 음량도 따로 둡니다 — 효과음이 잘 들리는 크기와 배경음이 방해되지 않는 크기는 다릅니다.
//
// **효과음처럼 통째로 풀지 않고 흘려 보냅니다.** 곡 하나가 60초에서 84초이고, 푼 것은
// 48kHz 스테레오라 초당 384KB 입니다 — 세 곡이면 81MB 가 판이 끝날 때까지 남습니다.
// 효과음은 되감고 여러 개를 겹쳐야 하므로 풀어 두는 것이 맞지만, 배경음은 한 번에 한 곡이
// 처음부터 끝까지 도는 것뿐이라 그럴 이유가 없습니다.
//
// 가져온 것은 CC0 이고 `public/music/readme.md` 에 적혀 있습니다.

/** 겹쳐서 넘어가는 데 걸리는 시간. */
const CROSS = 1.4

/**
 * 조용해지는 데 걸리는 시간.
 *
 * **넘어가는 것과 떠나는 것이 다른 일입니다.** 곡에서 곡으로 갈 때는 앞의 것이 천천히
 * 물러나야 넘어간 것을 알아채지 못하지만, 음악이 없어야 하는 화면으로 갈 때는 그 1.4초가
 * 그대로 그 화면에서 들립니다 — 블라인드를 고르는 자리가 조용해야 하는데 앞 라운드의
 * 곡이 거기서 잦아들고 있었습니다.
 *
 * **0 으로 두지는 않습니다.** 뚝 끊기면 그것은 넘어간 것이 아니라 무언가 잘못된 것으로
 * 들립니다. 0.2초는 끊긴 것으로 들리지 않으면서 다음 화면에 남지 않는 길이입니다.
 */
const LEAVE = 0.2

/** 잦아든 다음 실제로 멈추기까지의 여유. */
const SETTLE = 0.05

interface Track {
  element: HTMLAudioElement
  /** 소리 길이 열린 뒤에 생깁니다. **원소마다 한 번만 만들 수 있습니다.** */
  source?: MediaElementAudioSourceNode
  gain?: GainNode
  /** 잦아든 뒤에 멈추기로 걸어 둔 것. 다시 시작하면 걷습니다. */
  stopping?: ReturnType<typeof setTimeout>
}

export class Music {
  private context?: AudioContext
  private master?: GainNode
  private readonly tracks = new Map<string, Track>()
  /** 지금 나야 하는 곡. 소리 길이 열리기 전에 정해지면 열린 뒤에 시작합니다. */
  private wanted?: string

  private level = 0.5
  private off = false

  constructor(names: readonly string[]) {
    // **받는 것은 누르기를 기다리지 않습니다.** 소리 길은 사람이 무언가를 누른 뒤에만
    // 열리지만 파일을 받는 것은 그 전에 됩니다.
    for (const name of names) this.make(name)
  }

  /**
   * 곡 하나의 자리를 만듭니다.
   *
   * **원소는 소리 길보다 먼저 만듭니다.** `preload` 가 받는 것을 시작하므로, 소리 길이
   * 열리는 순간에는 이미 앞부분이 와 있습니다.
   */
  private make(name: string): Track | undefined {
    const seen = this.tracks.get(name)
    if (seen) return seen
    if (typeof document === 'undefined') return undefined

    const element = document.createElement('audio')
    element.src = `./music/${name}.ogg`
    element.loop = true
    element.preload = 'auto'

    const track: Track = { element }
    this.tracks.set(name, track)
    return track
  }

  /** 소리 길이 열렸습니다. 정해진 곡이 있으면 시작합니다. */
  open(context: AudioContext, destination: AudioNode): void {
    if (this.context) return
    this.context = context
    this.master = context.createGain()
    this.master.gain.value = this.off ? 0 : this.level
    this.master.connect(destination)

    if (this.wanted) this.swap(this.wanted)
  }

  /** 음량. 0 에서 1 입니다. */
  set volume(value: number) {
    this.level = Math.max(0, Math.min(1, value))
    if (this.master) this.master.gain.value = this.off ? 0 : this.level
  }

  get volume(): number {
    return this.level
  }

  /** 배경음을 끕니다. **효과음과 따로입니다.** */
  set muted(value: boolean) {
    this.off = value
    if (this.master) this.master.gain.value = value ? 0 : this.level
  }

  /**
   * 이 곡으로 넘어갑니다.
   *
   * 같은 곡이면 아무것도 하지 않습니다 — 화면이 다시 그려질 때마다 불리므로, 여기서
   * 다시 시작하면 곡이 매번 처음으로 돌아갑니다.
   */
  play(name: string | undefined): void {
    if (this.wanted === name) return
    const before = this.wanted
    this.wanted = name
    this.swap(name, before)
  }

  /** 앞의 것을 잦아들게 하고 뒤의 것을 올립니다. */
  private swap(name: string | undefined, before?: string): void {
    const context = this.context
    const master = this.master
    if (!context || !master) return

    // **떠나는 것이면 빨리 잦아듭니다.** 다음 곡이 그 자리를 채우는 것이 아니므로,
    // 겹치는 시간이 그대로 다음 화면에서 들립니다.
    if (before && before !== name) this.fadeOut(before, name ? CROSS : LEAVE)
    if (!name) return

    const track = this.make(name)
    if (!track) return

    // **원소마다 한 번만 만들 수 있습니다.** 두 번 만들면 브라우저가 거부하고, 그 곡은
    // 그 판이 끝날 때까지 조용해집니다.
    if (!track.source) {
      track.source = context.createMediaElementSource(track.element)
      track.gain = context.createGain()
      track.source.connect(track.gain).connect(master)
    }
    const gain = track.gain
    if (!gain) return

    if (track.stopping !== undefined) {
      clearTimeout(track.stopping)
      track.stopping = undefined
    }

    const now = context.currentTime
    gain.gain.cancelScheduledValues(now)
    gain.gain.setValueAtTime(gain.gain.value, now)
    gain.gain.linearRampToValueAtTime(1, now + CROSS)

    // **처음부터입니다.** 넘어간 화면에서 앞서 듣던 자리가 이어지면, 그 곡을 처음 듣는
    // 사람에게는 중간부터 시작한 것으로 들립니다.
    track.element.currentTime = 0
    void track.element.play().catch(() => undefined)
  }

  /**
   * 그 곡을 잦아들게 하고 멈춥니다.
   *
   * **0 에 닿은 다음에 멈춥니다.** 소리가 나는 중에 멈추면 파형이 잘린 자리에서 「퍽」
   * 소리가 납니다 — 잦아드는 시간이 아무리 짧아도 0 에 닿기만 하면 그것이 나지 않습니다.
   */
  private fadeOut(name: string, span: number): void {
    const context = this.context
    const track = this.tracks.get(name)
    if (!context || !track?.gain) return

    const now = context.currentTime
    track.gain.gain.cancelScheduledValues(now)
    track.gain.gain.setValueAtTime(track.gain.gain.value, now)
    track.gain.gain.linearRampToValueAtTime(0, now + span)

    if (track.stopping !== undefined) clearTimeout(track.stopping)
    track.stopping = setTimeout(() => {
      track.stopping = undefined
      track.element.pause()
    }, (span + SETTLE) * 1000)
  }

  /**
   * 지금 나고 있는 것을 멈춥니다. **다시 부르면 그 곡부터 다시 시작합니다.**
   *
   * 앱이 뒤로 물러날 때 씁니다 — 소리 길을 통째로 재우는 것과 함께 원소도 멈춰야
   * 기계가 그 파일을 계속 풀지 않습니다.
   */
  hold(): void {
    for (const track of this.tracks.values()) {
      if (track.stopping !== undefined) {
        clearTimeout(track.stopping)
        track.stopping = undefined
      }
      track.element.pause()
    }
  }

  /** 물러나 있는 동안 멈춘 것을 되돌립니다. */
  resume(): void {
    if (!this.wanted) return
    const track = this.tracks.get(this.wanted)
    if (track?.source) void track.element.play().catch(() => undefined)
  }

  /**
   * 지금 무엇이 어떻게 도는가. **검증 도구가 봅니다.**
   *
   * 원소는 문서에 붙이지 않으므로 밖에서 찾을 수 없습니다 — 소리 길에만 이어져 있으면
   * 되고, 문서에 붙이면 브라우저가 그 자리에 조작 막대를 그립니다.
   */
  report(): { wanted?: string; tracks: { name: string; playing: boolean; at: number }[] } {
    return {
      ...(this.wanted ? { wanted: this.wanted } : {}),
      tracks: [...this.tracks].map(([name, track]) => ({
        name,
        playing: !track.element.paused,
        at: track.element.currentTime,
      })),
    }
  }
}
