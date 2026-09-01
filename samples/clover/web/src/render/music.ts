// 배경음.
//
// **화면마다 한 곡이고, 바뀔 때는 겹쳐서 넘어갑니다.** 곡을 끊고 다음 곡을 시작하면 그
// 끊긴 자리가 「화면이 바뀌었다」보다 크게 들립니다 — 앞의 것이 잦아드는 동안 뒤의 것이
// 올라오면 넘어간 것을 알아채지 못한 채로 분위기만 바뀝니다.
//
// 효과음과 길이 다릅니다. 효과음은 신호 하나에 한 번이지만 배경음은 **계속 돌고**, 그래서
// 음량도 따로 둡니다 — 효과음이 잘 들리는 크기와 배경음이 방해되지 않는 크기는 다릅니다.
//
// 가져온 것은 CC0 이고 `public/music/readme.md` 에 적혀 있습니다.

/** 겹쳐서 넘어가는 데 걸리는 시간. */
const CROSS = 1.4

interface Playing {
  name: string
  source: AudioBufferSourceNode
  gain: GainNode
}

export class Music {
  private context?: AudioContext
  private master?: GainNode
  private readonly buffers = new Map<string, AudioBuffer>()
  private readonly bytes = new Map<string, Promise<ArrayBuffer | undefined>>()
  private playing?: Playing
  /** 지금 나야 하는 곡. 읽히기 전에 정해지면 읽힌 뒤에 시작합니다. */
  private wanted?: string

  private level = 0.5
  private off = false

  constructor(private readonly names: readonly string[]) {
    // **받는 것은 누르기를 기다리지 않습니다.** 소리 길은 사람이 무언가를 누른 뒤에만
    // 열리지만 파일을 받는 것은 그 전에 됩니다.
    for (const name of names) this.bytes.set(name, this.grab(name))
  }

  private async grab(name: string): Promise<ArrayBuffer | undefined> {
    try {
      const answer = await fetch(`./music/${name}.ogg`)
      return answer.ok ? await answer.arrayBuffer() : undefined
    } catch {
      return undefined
    }
  }

  /** 소리 길이 열렸습니다. 받아 둔 것을 풀고, 정해진 곡이 있으면 시작합니다. */
  open(context: AudioContext, destination: AudioNode): void {
    if (this.context) return
    this.context = context
    this.master = context.createGain()
    this.master.gain.value = this.off ? 0 : this.level
    this.master.connect(destination)
    void this.load()
  }

  private async load(): Promise<void> {
    const context = this.context
    if (!context) return

    await Promise.all(this.names.map(async name => {
      const raw = await this.bytes.get(name)
      if (!raw) return
      try {
        this.buffers.set(name, await context.decodeAudioData(raw.slice(0)))
      } catch {
        // 풀지 못한 것은 그 화면에서 조용합니다.
      }
    }))

    // 읽는 동안 정해진 것이 있으면 그때 시작합니다. **`play` 를 거치지 않습니다** —
    // 이미 그 곡으로 정해져 있으므로 거기서 그대로 돌아갑니다.
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
    this.wanted = name
    this.swap(name)
  }

  /** 앞의 것을 잦아들게 하고 뒤의 것을 올립니다. */
  private swap(name: string | undefined): void {
    const context = this.context
    const master = this.master
    if (!context || !master) return

    const now = context.currentTime
    if (this.playing) {
      // 앞의 것은 잦아들고 스스로 끝납니다.
      const going = this.playing
      going.gain.gain.setValueAtTime(going.gain.gain.value, now)
      going.gain.gain.linearRampToValueAtTime(0, now + CROSS)
      going.source.stop(now + CROSS + 0.05)
      this.playing = undefined
    }

    if (!name) return
    const buffer = this.buffers.get(name)
    if (!buffer) return

    const source = context.createBufferSource()
    source.buffer = buffer
    source.loop = true

    const gain = context.createGain()
    gain.gain.setValueAtTime(0, now)
    gain.gain.linearRampToValueAtTime(1, now + CROSS)

    source.connect(gain).connect(master)
    source.start(now)
    this.playing = { name, source, gain }
  }
}
