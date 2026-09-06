// 소리.
//
// **녹음된 것을 쓰고, 합성은 바탕으로 남습니다.** 종이가 스치는 소리와 칩이 부딪히는 소리는
// 물리적으로 복잡해서 오실레이터 하나로는 되지 않습니다 — `public/sound/<신호>.ogg` 가 있으면
// 그것을 내고, 없거나 읽지 못하면 아래의 파형으로 냅니다. 소리가 아예 안 나는 것보다는
// 전자음이라도 나는 편이 낫습니다.
//
// 음높이가 값을 따라 오르는 것은 **재생 속도**로 합니다. 칩이 하나씩 더해질 때마다 음이
// 오르는 그 소리가 원작의 그것이고, 녹음된 소리로도 됩니다.
//
// **크기는 스스로 맞춥니다.** 꾸러미에서 온 파일은 저마다 음량이 달라서, 그대로 두면 어떤
// 것은 들리지 않고 어떤 것은 귀를 찌릅니다 — 읽을 때 실효값을 재서 한 크기로 맞추고, 뜻으로
// 키우거나 줄이는 것만 `SoundCue.gain` 에 둡니다.
//
// 원작의 음원을 쓰지 않습니다. 가져온 것은 CC0 이고 `public/sound/readme.md` 에 적혀 있습니다.

import type { CloverData } from '../generated/clover-data'
import { glide, GLIDE } from './gain'
import { Music } from './music'

/**
 * 배경음의 곡들.
 *
 * **파일 이름이 곧 그 화면입니다** — 효과음이 신호의 이름을 쓰는 것과 같습니다.
 */
const MUSIC = ['title', 'round', 'shop'] as const

const BASE_HZ = 220

/**
 * 맞추는 크기. 실효값이 이것이 되도록 곱합니다.
 *
 * **꾸러미의 파일은 저마다 음량이 다릅니다.** 카드가 스치는 소리는 작고 확인음은 큽니다 —
 * 그것을 그대로 두면 어떤 신호는 안 들리고 어떤 신호는 찌릅니다.
 */
const TARGET_RMS = 0.09
/** 맞추는 정도의 상한과 하한. 거의 빈 파일이 폭발하지 않게 합니다. */
const GAIN_RANGE = [0.05, 6] as const

/**
 * 겹친 것으로 세는 시간을 **음원 길이에서 정합니다.**
 *
 * 한동안 0.18초 하나로 두었는데, 재어 보니 음원 38개 중 19개가 0.4초를 넘습니다 —
 * `card_place` 는 0.689초이고 `card_select` 는 0.601초입니다. 0.13초 간격으로 나는 상점
 * 진열에서 실제로는 6개가 함께 울리는데 계수기는 2개로 세고 있었고, 그래서 줄이는 계산이
 * 걸리지 않았습니다.
 *
 * 꼬리는 앞머리보다 작으므로 길이를 다 세지 않습니다. 60%가 「아직 들린다」의 자리입니다.
 */
const CROWD_SHARE = 0.6
/** 그 시간의 하한과 상한. 아주 긴 음원 하나가 자기 신호를 오래 막지 않게 합니다. */
const CROWD_SPAN = [0.10, 0.45] as const
/** 한 신호가 동시에 낼 수 있는 수. */
const CROWD_MOST = 4
/** 뺏긴 소리가 잦아드는 시간. **끊지 않고 물러나게 합니다.** */
const STEAL_FADE = 0.05

/**
 * 마스터의 압축기.
 *
 * **사슬이 길면 합이 1을 넘습니다.** 카드 다섯 장과 조커 넷이 잇달아 득점할 때 소리는
 * 힘으로 더해지고, 넘긴 만큼은 기계가 깎아 냅니다 — 그 깎인 자리가 찌그러진 소리입니다.
 * 겹침 계수기가 신호 하나 안에서 줄이는 것과 달리, 이것은 서로 다른 신호가 함께 날 때를
 * 봅니다.
 */
const SQUEEZE = {
  threshold: -14, knee: 6, ratio: 4, attack: 0.003, release: 0.12,
} as const

/**
 * 사슬이 오르는 음계. 5음 음계의 계단입니다.
 *
 * **반음을 그대로 쌓지 않습니다.** 한동안 사건마다 2반음씩 올렸는데, 반음 사다리는
 * 「올라간다」로만 들리고 선율로는 들리지 않습니다 — 어느 두 음도 협화음이 아니기
 * 때문입니다. 5음 음계는 어느 두 음을 집어도 어긋나지 않아, 순서가 어떻든 가락이 됩니다.
 */
const LADDER = [0, 2, 4, 7, 9] as const
/** 사다리가 오르는 옥타브 수. 그 위는 맨 위 옥타브 안에서 돕니다. */
const LADDER_OCTAVES = 2

/**
 * 사슬의 몇 번째인가를 반음 몇 개로.
 *
 * **끝이 있습니다.** 상한 없이 올리던 것이 문제였습니다 — 세기 12에 사슬 10이면 32반음이고,
 * 재생 속도로 6.3배입니다. 0.172초인 칩 소리가 0.027초가 되어, 칩이 아니라 딱 소리로
 * 납니다. 두 옥타브를 다 오르면 위 옥타브 안에서만 계속 돕니다.
 */
export function ladder(step: number): number {
  const wide = LADDER.length * LADDER_OCTAVES
  const at = step < wide ? step
    : LADDER.length + ((step - wide) % LADDER.length)
  return LADDER[at % LADDER.length] + 12 * Math.floor(at / LADDER.length)
}

/**
 * 음원이 음높이를 따라 움직이는 정도.
 *
 * **음원은 재생 속도로만 음을 올릴 수 있고, 그러면 짧아집니다.** 그래서 음원에는 밝아지는
 * 정도만 맡기고 — 상한이 3반음입니다 — 가락은 위에 겹치는 음 하나가 맡습니다. 그 둘이
 * 갈리고 나서야 사슬을 끝까지 올려도 소리가 남습니다.
 */
const SAMPLE_TILT = 6
const SAMPLE_TILT_MOST = 3

/**
 * 같은 소리를 두 번 낼 때 흔드는 정도.
 *
 * **녹음은 반복이 들립니다.** 같은 파일을 같은 음높이와 같은 크기로 열 번 내면 열 번째에는
 * 그 파일이 들립니다 — 실제로 두드린 것은 매번 조금씩 다르기 때문입니다. 반음의 5분의 1과
 * 크기의 7%면 무엇이 달라졌는지는 알아채지 못하면서 같은 것으로는 들리지 않습니다.
 */
const WOBBLE_PITCH = 0.2
const WOBBLE_GAIN = 0.07

/** 좌우로 벌리는 끝. **끝까지 밀면 한쪽 귀에서만 납니다.** */
const PAN_MOST = 0.45

/**
 * 다 난 뒤에 마디를 끊기까지의 여유.
 *
 * **끊지 않으면 쌓입니다.** 소리 하나가 이득 마디를 만들어 마스터에 붙이는데, 그것을
 * 끊지 않으면 소리가 끝난 뒤에도 그래프에 남습니다 — 마스터에서 출력까지 이어져 있는 한
 * 기계는 그 마디를 살아 있는 것으로 보고 렌더 블록마다 계산합니다.
 *
 * 재어 보니 손 하나에 58개씩 늘어 넷째 손에서 272개였습니다. 판을 오래 돌리면 오디오
 * 스레드가 블록을 못 맞추고, 그때 나는 것은 「소리가 작아진다」가 아니라 **끊김과 무음**
 * 입니다.
 *
 * 마스터에서 끊으면 그 위의 것들이 통째로 도달 불가가 되어 함께 회수됩니다.
 */
const REAP = 0.25

export class Audio {
  private context?: AudioContext
  private master?: GainNode
  /**
   * 마스터의 압축기.
   *
   * **들고 있어야 합니다.** WebAudio 의 수명 규칙에서 마디는 출력에 이어져 있다는 것만으로는
   * 살아 있지 않습니다 — JS 참조가 없고 입력이 흐르지 않는 동안은 회수 대상입니다. 지역
   * 변수로 두었더니 `master → 출력` 길이 끊겨 **소리가 통째로 없어졌습니다.**
   *
   * 소리 마디는 만들어지고 소리 길은 `running` 이고 마스터의 이득도 살아 있는데 아무것도
   * 들리지 않는 것이 그 모습입니다 — 끊긴 자리가 그 둘 사이라 어느 쪽을 보아도 멀쩡합니다.
   */
  private squeeze?: DynamicsCompressorNode
  /**
   * 출력에 실제로 흐르는 값을 재는 것.
   *
   * **소리 마디가 뜨는 것과 들리는 것이 다른 일입니다.** 소리 길이 `running` 이고 마스터의
   * 이득이 살아 있고 마디가 만들어져도, 들리지 않는 일이 있습니다 — 그때 「게임이 소리를
   * 내지 않는 것」과 「기계가 소리를 내지 않는 것」을 가리는 것이 이것입니다. 여기 값이
   * 0 이 아니면 게임은 내고 있는 것이고, 그다음은 기계 쪽입니다.
   */
  private look?: AnalyserNode
  /** 그 봉우리. 잰 값이 서서히 내려갑니다 — 누른 그 순간을 지나쳐도 읽힙니다. */
  private loudest = 0
  private looking?: ReturnType<typeof setInterval>
  private readonly follows = new Map<string, boolean>()
  /** 신호마다의 크기. 데이터가 정합니다. */
  private readonly wanted = new Map<string, number>()
  /**
   * 읽어 둔 소리.
   *
   * 없는 신호는 합성으로 갑니다. **읽기를 기다리지 않습니다** — 읽히기 전에 난 소리는
   * 합성으로 나고, 읽힌 다음부터 녹음된 것으로 바뀝니다.
   */
  private readonly samples = new Map<string,
    { buffer: AudioBuffer; gain: number; lead: number; span: number }>()
  /**
   * 아직 풀지 않은 소리의 바이트.
   *
   * **받는 것은 누르기를 기다리지 않습니다.** 소리 길은 사람이 무언가를 누른 뒤에만 열리지만
   * 파일을 받는 것은 그 전에 됩니다 — 미리 받아 두지 않으면 첫 두어 소리가 합성으로 나고,
   * 그 둘이 첫인상입니다.
   */
  private bytes?: Promise<Map<string, ArrayBuffer>>
  /**
   * 잡음 한 토막.
   *
   * **카드와 종이와 동전은 음이 아니라 잡음입니다.** 사인파로 만든 「탁」은 어떤 값을 줘도
   * 전자음이고, 카드가 놓이는 소리로 들리지 않습니다.
   *
   * 한 번 만들어 두고 돌려 씁니다 — 소리마다 만들면 그 만드는 값이 소리보다 큽니다.
   */
  private hiss?: AudioBuffer
  /**
   * 신호마다 지금 울리고 있는 것들.
   *
   * **같은 소리가 겹치면 커집니다.** 카드 다섯 장이 잇달아 사라질 때 그 소리가 다섯 번
   * 나는데, 소리는 힘으로 더해지므로 다섯이면 하나보다 곱절 넘게 큽니다 — 그것이 「볼륨
   * 게이지가 올라가는」 느낌이고, 그 순간만 화면의 다른 소리를 다 덮습니다.
   *
   * 시각만 적던 것을 **이득 마디까지 들고 있는 것**으로 바꿨습니다. 넘칠 때 새것을 버리는
   * 대신 가장 오래된 것을 물러나게 하려면 그것을 붙잡고 있어야 합니다.
   */
  private readonly voices = new Map<string, { until: number; gain: GainNode }[]>()
  /**
   * 지금 도는 지속 보이스들. **이름마다 하나입니다.**
   *
   * **다시 부르면 늘립니다.** 새로 시작하면 그 순간 둘이 겹치고, 프레임마다 부르는
   * 벌크 오퍼레이션에서 그것은 곧 프레임 수만큼의 보이스입니다.
   */
  private readonly running = new Map<string, {
    source: AudioBufferSourceNode
    level: GainNode
    until: number
  }>()

  /**
   * 재우기로 걸어 둔 것.
   *
   * **걷을 수 있어야 합니다.** 내려놓는 데 걸리는 0.05초 안에 돌아오는 일이 있고, 그때
   * 이 예약이 남아 있으면 깨운 길을 다시 재웁니다.
   */
  private holding?: ReturnType<typeof setTimeout>

  /** 소리를 끄는가. 옵션이 정합니다. */
  muted = false

  /** 배경음. 효과음과 길은 같고 음량은 따로입니다. */
  readonly music = new Music(MUSIC)

  private level = 0.35

  /**
   * 음량. 0 에서 1 입니다.
   *
   * **이미 열려 있는 소리 길에도 바로 걸립니다** — 값만 두고 다음 소리부터 적용하면, 옵션을
   * 만지는 동안에는 무엇이 바뀌었는지 들리지 않습니다.
   *
   * 대입하지 않고 옮깁니다. 눈금이 20%씩 뛰므로 소리가 나는 중에 그만큼의 불연속이 생깁니다.
   */
  set volume(value: number) {
    this.level = Math.max(0, Math.min(1, value))
    if (this.master && this.context) glide(this.master.gain, this.level, this.context.currentTime)
  }

  get volume(): number {
    return this.level
  }

  constructor(private readonly tables: CloverData) {
    for (const cue of tables.soundCue.records) {
      this.follows.set(cue.cueId, cue.pitchFollowsValue)
      this.wanted.set(cue.cueId, cue.gain)
    }
    this.bytes = this.grab()
  }

  /** 파일을 받아 둡니다. 푸는 것은 소리 길이 열린 뒤입니다. */
  private async grab(): Promise<Map<string, ArrayBuffer>> {
    const out = new Map<string, ArrayBuffer>()
    await Promise.all(this.tables.soundCue.records.map(async cue => {
      try {
        const answer = await fetch(`./sound/${cue.cueId}.ogg`)
        if (answer.ok) out.set(cue.cueId, await answer.arrayBuffer())
      } catch {
        // 없는 것은 합성으로 갑니다.
      }
    }))
    return out
  }

  /**
   * 소리 길을 엽니다.
   *
   * **여는 것과 깨우는 것이 다른 일입니다.** 브라우저는 사람이 누르기 전에도 소리 길을
   * 만들게 해 주지만, 그렇게 만든 것은 잠든 채(`suspended`)로 나옵니다 — 만들었다는 것과
   * 소리가 난다는 것이 같지 않습니다.
   *
   * **그래서 이미 있어도 그냥 돌아가지 않습니다.** 돌아가게 두었더니 앱에서는 켤 때 잠든
   * 길이 하나 만들어지고, 그 뒤의 누름은 전부 첫 줄에서 되돌아가 아무것도 깨우지 못했습니다.
   * 부르는 자리가 여럿인 것은 「그중 하나는 사람이 누른 순간이다」를 노린 것이므로, 그때마다
   * 깨울 것이 있는지 보아야 합니다.
   */
  unlock(): void {
    if (this.context) {
      this.wake()
      return
    }
    const Ctor = (window.AudioContext
      ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)
    this.context = new Ctor()
    this.master = this.context.createGain()
    this.master.gain.value = this.level
    // **효과음만 압축기를 지납니다.** 배경음까지 함께 넣으면 득점이 길어질 때마다 곡이
    // 눌렸다 돌아오고, 그 오르내림이 득점보다 크게 들립니다.
    //
    // **필드에 둡니다.** 지역 변수로 두면 회수되고, 회수되면 마스터에서 출력까지의 길이
    // 끊깁니다 — 그 끊긴 자리는 소리 길의 상태에도 마스터의 이득에도 나타나지 않습니다.
    this.squeeze = this.context.createDynamicsCompressor()
    this.squeeze.threshold.value = SQUEEZE.threshold
    this.squeeze.knee.value = SQUEEZE.knee
    this.squeeze.ratio.value = SQUEEZE.ratio
    this.squeeze.attack.value = SQUEEZE.attack
    this.squeeze.release.value = SQUEEZE.release
    this.master.connect(this.squeeze).connect(this.context.destination)
    // **배경음도 같은 길을 씁니다.** 소리 길은 하나이고, 음량만 따로입니다.
    this.music.open(this.context, this.context.destination)

    // **출력에 흐르는 값을 나란히 잽니다.** 길에 끼우지 않고 갈라 받으므로 소리에 닿지
    // 않습니다 — 효과음과 배경음 둘 다 여기로 함께 옵니다.
    this.look = this.context.createAnalyser()
    this.look.fftSize = 2048
    this.squeeze.connect(this.look)
    this.music.tap(this.look)
    this.watch()

    const seconds = 0.5
    const frames = Math.floor(this.context.sampleRate * seconds)
    this.hiss = this.context.createBuffer(1, frames, this.context.sampleRate)
    const wave = this.hiss.getChannelData(0)
    for (let i = 0; i < frames; i++) wave[i] = Math.random() * 2 - 1

    void this.load()
    // **만든 그 자리에서 깨웁니다.** 사람이 누른 뒤라면 이미 깨어 있고, 그 전이라면
    // 이 부름이 되든 안 되든 다음 누름이 다시 옵니다.
    this.wake()
  }

  /**
   * 출력에 흐르는 값을 재기 시작합니다.
   *
   * 잰 값은 서서히 내려갑니다 — 누른 그 순간을 지나쳐도 읽을 수 있어야 합니다.
   */
  private watch(): void {
    const look = this.look
    if (!look || this.looking !== undefined) return
    const frame = new Float32Array(look.fftSize)
    this.looking = setInterval(() => {
      look.getFloatTimeDomainData(frame)
      let top = 0
      for (let i = 0; i < frame.length; i++) {
        const size = Math.abs(frame[i])
        if (size > top) top = size
      }
      this.loudest = Math.max(top, this.loudest * 0.92)
    }, 50)
  }

  /**
   * 뒤로 물러났습니다.
   *
   * **소리 길을 재웁니다.** 안드로이드는 화면이 없는 동안에도 소리 길을 그대로 두므로,
   * 재우지 않으면 배경음이 계속 나고 그것을 끄는 길이 앱을 끝내는 것뿐입니다.
   */
  hold(): void {
    const context = this.context
    if (!context) return
    // **재는 것도 멈춥니다.** 물러난 동안 흐르는 값이 없으므로 재도 0 이고, 그 0 이
    // 「소리를 내지 않는다」로 읽힙니다.
    if (this.looking !== undefined) {
      clearInterval(this.looking)
      this.looking = undefined
    }
    // **내려놓고 재웁니다.** 나는 중에 재우면 파형이 잘린 자리에서 「퍽」 소리가 나고,
    // 그것이 앱을 뒤로 보낸 사람이 마지막으로 듣는 소리가 됩니다.
    if (this.master) glide(this.master.gain, 0, context.currentTime)
    if (this.holding !== undefined) clearTimeout(this.holding)
    this.holding = setTimeout(() => {
      this.holding = undefined
      this.music.hold()
      void context.suspend().catch(() => undefined)
      // **다시 올려 둡니다.** 재운 뒤이므로 들리지 않고, 깨울 때 음량이 0 인 채로
      // 시작하지 않습니다.
      if (this.master) this.master.gain.value = this.level
    }, (GLIDE + 0.01) * 1000)
  }

  /**
   * 잠든 소리 길을 깨웁니다.
   *
   * **뒤로 갔다 돌아온 자리와 켤 때가 같은 일입니다** — 둘 다 「길은 있는데 잠들어 있다」
   * 입니다. 깨어 있으면 `resume` 은 아무 일도 하지 않습니다.
   */
  wake(): void {
    const context = this.context
    if (!context) return
    if (this.looking === undefined && this.look) this.watch()
    // **재우기로 걸어 둔 것을 먼저 걷습니다.** 내려놓는 0.05초 안에 돌아오면 그 예약이
    // 깨운 뒤에 도착해서, 방금 깨운 길을 다시 재웁니다 — 데스크탑에서는 창을 내렸다 올릴
    // 때까지 `visibilitychange` 가 다시 오지 않으므로, 그 판이 끝날 때까지 소리가
    // 없어집니다.
    if (this.holding !== undefined) {
      clearTimeout(this.holding)
      this.holding = undefined
      // 예약이 하려던 것 중 이것만 지금 합니다. 재우지 않으므로 음량은 되돌립니다.
      if (this.master) glide(this.master.gain, this.level, context.currentTime)
    }
    if (context.state === 'running') {
      this.music.resume()
      return
    }
    void context.resume().then(() => this.music.resume()).catch(() => undefined)
  }

  /**
   * 소리를 읽어 둡니다.
   *
   * **하나가 없어도 나머지는 읽습니다.** 파일 하나가 빠졌다고 소리가 통째로 없어지면,
   * 빠진 것을 찾는 것이 더 어려워집니다.
   */
  private async load(): Promise<void> {
    const context = this.context
    if (!context || !this.bytes) return

    const bytes = await this.bytes
    await Promise.all([...bytes].map(async ([cue, raw]) => {
      try {
        // **한 번만 풉니다.** `decodeAudioData` 는 넘긴 버퍼를 비우므로 베껴 넘깁니다.
        const buffer = await context.decodeAudioData(raw.slice(0))
        const lead = this.leadOf(buffer)
        this.samples.set(cue, {
          buffer,
          gain: this.levelFor(buffer) * (this.wanted.get(cue) ?? 1),
          lead,
          // **앞의 묵음을 뺀 길이입니다.** 재생은 그 자리부터 시작하므로, 실제로 소리가
          // 나는 시간이 그만큼입니다.
          span: buffer.duration - lead,
        })
      } catch {
        // 풀지 못한 것은 합성으로 갑니다.
      }
    }))
  }

  /**
   * 앞의 묵음이 몇 초인가.
   *
   * **꾸러미의 파일은 앞이 비어 있습니다.** 재어 보면 38개 중 18개가 8밀리초를 넘고
   * 가장 긴 것은 97밀리초입니다 — 그만큼 늦게 들리므로 카드가 놓이는 그림과 어긋납니다.
   * 파일을 고치지 않고 **그 자리부터 재생합니다.**
   */
  private leadOf(buffer: AudioBuffer): number {
    const wave = buffer.getChannelData(0)
    // 앞의 0.3초 안에서만 찾습니다. 그보다 뒤라면 묵음이 아니라 뜸입니다.
    const span = Math.min(wave.length, Math.floor(buffer.sampleRate * 0.3))
    for (let i = 0; i < span; i++) {
      if (Math.abs(wave[i]) > 0.01) return i / buffer.sampleRate
    }
    return 0
  }

  /** 그 소리를 한 크기로 맞추는 배수. 실효값을 재서 정합니다. */
  private levelFor(buffer: AudioBuffer): number {
    const wave = buffer.getChannelData(0)
    // **묵음 다음부터 0.4초를 봅니다.** 앞의 빈 자리까지 세면 그만큼 작게 재어져,
    // 묵음이 긴 파일이 더 크게 나옵니다.
    const from = Math.floor(this.leadOf(buffer) * buffer.sampleRate)
    const span = Math.min(wave.length - from, Math.floor(buffer.sampleRate * 0.4))
    let sum = 0
    for (let i = 0; i < span; i++) sum += wave[from + i] * wave[from + i]
    const rms = Math.sqrt(sum / Math.max(1, span))
    if (rms < 1e-5) return 1
    return Math.max(GAIN_RANGE[0], Math.min(GAIN_RANGE[1], TARGET_RMS / rms))
  }

  /**
   * 소리 하나.
   *
   * `semitones` 는 값의 크기에서 옵니다 — `SoundCue.pitch_follows_value` 가 참인 것만
   * 그것을 씁니다.
   */
  /**
   * 최근에 난 소리들. 새것이 뒤입니다.
   *
   * **「이 순간에 왜 이 소리가 나느냐」는 물음에 답할 자리입니다.** 소리는 화면과 달리
   * 굽어 볼 수가 없어서, 어느 자리가 냈는지를 코드에서 눈으로 찾아야 했습니다 — 부르는
   * 자리가 쉰 곳이 넘습니다.
   */
  readonly played: string[] = []

  play(cueId: string, semitones = 0, pan = 0): void {
    const context = this.context
    const master = this.master
    // **꺼져 있어도 적습니다.** 무엇이 부르려 했는지가 물음의 답이고, 실제로 울렸는지는
    // 그다음입니다.
    this.played.push(cueId)
    if (this.played.length > 48) this.played.shift()
    if (!context || !master || this.muted) return

    const now = context.currentTime
    const sample = this.samples.get(cueId)
    const shape = SHAPE[cueId] ?? DEFAULT_SHAPE
    const span = sample ? sample.span : shape.length

    const follows = this.follows.get(cueId) ?? false
    const shift = follows ? semitones : 0
    // **음원은 조금만 따라 올라갑니다.** 재생 속도로 올리면 그만큼 짧아지므로, 그대로
    // 따라가게 두면 사슬의 끝에서 음원이 없어집니다 — 밝아지는 정도까지만 맡기고 가락은
    // 위에 겹치는 음이 냅니다.
    const tilt = Math.max(-SAMPLE_TILT_MOST, Math.min(SAMPLE_TILT_MOST, shift / SAMPLE_TILT))
    // **같은 자리에서 두 번 내지 않습니다.** 반음의 5분의 1 안에서 흔듭니다.
    const wobble = tilt + (Math.random() - 0.5) * 2 * WOBBLE_PITCH
    const rate = Math.pow(2, wobble / 12)
    // 이 소리가 실제로 나는 시간. **그만큼 뒤에 마디를 끊습니다.**
    const heard = sample ? sample.span / rate
      : Math.max(shape.length, shape.noise?.length ?? 0)

    // **소리 하나가 이득 마디 하나를 갖습니다.** 겹치는 만큼 줄이는 것과, 넘칠 때 앞의
    // 것을 물러나게 하는 것이 그 마디 하나에서 됩니다 — 음원이든 파형이든 같습니다.
    const voice = this.lane(pan, heard)
    if (!voice) return
    voice.gain.value = this.room(cueId, now, span, voice)
      * (1 + (Math.random() - 0.5) * 2 * WOBBLE_GAIN)

    // **녹음된 것이 있으면 그것입니다.**
    if (sample) {
      const source = context.createBufferSource()
      source.buffer = sample.buffer
      source.playbackRate.value = rate

      const gain = context.createGain()
      gain.gain.value = sample.gain
      source.connect(gain).connect(voice)
      // **묵음을 건너뛰고 시작합니다.** 그것이 곧 「소리가 그림과 같이 난다」입니다.
      source.start(now, sample.lead)
      return
    }

    const hz = BASE_HZ * Math.pow(2, (shift + shape.offset) / 12)

    if (shape.gain > 0) {
      const osc = context.createOscillator()
      osc.type = shape.wave
      osc.frequency.setValueAtTime(hz, now)
      if (shape.glide !== 0) {
        osc.frequency.exponentialRampToValueAtTime(
          hz * Math.pow(2, shape.glide / 12), now + shape.length)
      }

      const gain = context.createGain()
      gain.gain.setValueAtTime(0, now)
      gain.gain.linearRampToValueAtTime(shape.gain, now + 0.006)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + shape.length)

      osc.connect(gain).connect(voice)
      osc.start(now)
      osc.stop(now + shape.length + 0.02)
    }

    if (shape.noise !== undefined) this.hissAt(shape.noise, now, voice)
  }

  /**
   * 소리 하나를 마스터에 붙입니다. 좌우로 벌릴 것이 있으면 벌립니다.
   *
   * **카드는 왼쪽부터 차례로 득점합니다.** 그것이 전부 가운데에서 나면 다섯 번이 한 자리에
   * 쌓이고, 그 자리에서 서로를 덮습니다 — 소리가 난 자리가 카드가 있는 자리이면 다섯이
   * 겹쳐도 각각이 들리고, 사슬이 왼쪽에서 오른쪽으로 지나가는 것으로도 들립니다.
   *
   * **끝까지 밀지 않습니다.** 한쪽 귀에서만 나는 소리는 이어폰에서 어긋난 것으로 들리고,
   * 한쪽 귀로만 듣는 사람에게는 아예 없는 소리가 됩니다.
   */
  private lane(pan: number, seconds: number): GainNode | undefined {
    const context = this.context
    const master = this.master
    if (!context || !master) return undefined

    const voice = context.createGain()
    let tail: AudioNode = voice
    if (pan !== 0 && typeof context.createStereoPanner === 'function') {
      const side = context.createStereoPanner()
      side.pan.value = Math.max(-PAN_MOST, Math.min(PAN_MOST, pan))
      voice.connect(side)
      tail = side
    }
    tail.connect(master)
    // **소리가 끝나면 끊습니다.** 마스터에서 끊긴 마디는 출력에 닿지 않으므로 그 위의
    // 것들과 함께 회수됩니다 — 끊지 않으면 판을 도는 동안 계속 쌓입니다.
    setTimeout(() => tail.disconnect(), (seconds + REAP) * 1000)
    return voice
  }

  /**
   * 이 신호를 지금 얼마나 크게 낼 것인가. 그리고 넘치면 자리를 냅니다.
   *
   * **소리는 힘으로 더해집니다.** 같은 소리 넷이 겹치면 하나보다 두 배쯤 큰데, 각자를
   * 겹친 수의 제곱근으로 나누면 합이 하나만큼으로 남습니다 — 개수로 나누면 도리어 작아지고,
   * 그러면 한 장씩 사라지는 소리가 들리지 않습니다.
   *
   * **넘칠 때 새것을 버리지 않습니다.** 다섯째를 내지 않던 것을 가장 오래된 것이 물러나는
   * 것으로 바꿨습니다 — 버리면 그 순간에 누른 것이 소리 없이 지나가고, 그것은 「눌렀는데
   * 아무 일도 없다」로 들립니다. 오래된 것은 이미 꼬리이므로 물러나도 알아채지 못합니다.
   */
  private room(cueId: string, now: number, span: number, mine: GainNode): number {
    // 이 음원이 얼마 동안 들리는가. 음원마다 다릅니다.
    const heard = Math.min(CROWD_SPAN[1], Math.max(CROWD_SPAN[0], span * CROWD_SHARE))
    const live = (this.voices.get(cueId) ?? []).filter(one => one.until > now)

    while (live.length >= CROWD_MOST) {
      const oldest = live.shift()
      if (oldest) glide(oldest.gain.gain, 0, now, STEAL_FADE)
    }

    live.push({ until: now + heard, gain: mine })
    this.voices.set(cueId, live)
    return 1 / Math.sqrt(live.length)
  }

  /**
   * 음 하나.
   *
   * **음원이 못 하는 것을 합니다.** 음원의 음높이는 재생 속도이므로 높이 갈수록 짧아지고,
   * 사슬의 끝에서는 소리가 아니라 딱 소리가 됩니다 — 그래서 질감은 음원이, 가락은 이것이
   * 맡습니다. 겹쳐 나는 둘이지 갈아 끼우는 둘이 아닙니다.
   *
   * `semitones` 는 `ladder` 가 낸 값입니다.
   */
  tone(name: ToneName, semitones: number, strength = 1, pan = 0): void {
    const context = this.context
    const master = this.master
    if (!context || !master || this.muted) return

    const timbre: Timbre = TIMBRE[name]
    const now = context.currentTime
    const hz = BASE_HZ * Math.pow(2, (timbre.offset + semitones) / 12)

    // 부분음 중 가장 오래 남는 것이 이 음의 길이입니다.
    const heard = timbre.decay * Math.max(...timbre.parts.map(part => part[2]))

    // **음색마다 따로 셉니다.** 같은 음색이 잇달아 나면 그것이 겹치는 것이고, 다른
    // 음색이 함께 나는 것은 화음이라 겹치는 것이 아닙니다.
    const voice = this.lane(pan, heard)
    if (!voice) return
    voice.gain.value = strength * timbre.gain
      * this.room(`tone:${name}`, now, timbre.decay, voice)

    let out: AudioNode = voice
    if (timbre.cut > 0) {
      const lid = context.createBiquadFilter()
      lid.type = 'lowpass'
      lid.frequency.setValueAtTime(timbre.cut * 2.4, now)
      // **앞이 밝고 곧 둥글어집니다.** 뜯은 줄이 그렇습니다.
      lid.frequency.exponentialRampToValueAtTime(timbre.cut * 0.5, now + timbre.decay)
      lid.connect(voice)
      out = lid
    }

    for (const [ratio, level, span] of timbre.parts) {
      // **들리는 데까지만 만듭니다.** 사람이 듣는 위쪽 끝을 넘긴 부분음은 값만 쓰고
      // 아무것도 더하지 않습니다.
      if (hz * ratio > 16_000) continue
      const osc = context.createOscillator()
      osc.type = timbre.wave
      osc.frequency.value = hz * ratio

      const decay = timbre.decay * span
      const gain = context.createGain()
      gain.gain.setValueAtTime(0, now)
      gain.gain.linearRampToValueAtTime(level, now + 0.004)
      gain.gain.exponentialRampToValueAtTime(0.0001, now + decay)

      osc.connect(gain).connect(out)
      osc.start(now)
      osc.stop(now + decay + 0.02)
    }
  }

  /**
   * 여럿이 움직이는 동안 하나만 냅니다.
   *
   * **프레임마다 불러도 됩니다.** 이미 도는 것이 있으면 끝나는 시각만 뒤로 미루므로,
   * 부르는 쪽은 「지금도 움직이는 중」만 알리면 됩니다 — 시작과 끝을 세는 자리를 따로
   * 두면 그중 하나가 반드시 빠지고, 빠진 쪽은 소리가 영영 남거나 아예 안 납니다.
   *
   * `seconds` 는 **지금부터 얼마나 더** 도는가입니다.
   */
  sweep(name: SweepName, seconds: number, strength = 1): void {
    const context = this.context
    const master = this.master
    if (!context || !master || !this.hiss || this.muted) return

    const shape = SWEEP[name]
    const now = context.currentTime
    const live = this.running.get(name)
    const level = shape.gain * strength

    // **도는 것이 있으면 늘리고 끝냅니다.**
    if (live && live.until > now) {
      live.until = Math.max(live.until, now + seconds)
      this.closeAt(live, level, shape, now)
      return
    }

    const source = context.createBufferSource()
    source.buffer = this.hiss
    source.loop = true

    const band = context.createBiquadFilter()
    band.type = 'bandpass'
    band.Q.value = shape.q
    band.frequency.setValueAtTime(shape.from, now)
    // 대역이 이 시간에 걸쳐 옮겨 갑니다. 늘어나면 옮겨 간 자리에 머무릅니다 — 결이
    // 잦아드는 것이 「거의 다 들어왔다」로 들립니다.
    band.frequency.exponentialRampToValueAtTime(shape.to, now + Math.max(0.08, seconds))

    // 낱장이 지나가는 결. **대역만 남긴 잡음은 바람이고, 끊으면 카드가 됩니다.**
    const grain = context.createGain()
    grain.gain.value = 1 - shape.depth / 2
    const flick = context.createOscillator()
    flick.type = 'sawtooth'
    flick.frequency.value = shape.rate
    const depth = context.createGain()
    depth.gain.value = shape.depth / 2
    flick.connect(depth).connect(grain.gain)
    flick.start(now)

    const gate = context.createGain()
    gate.gain.setValueAtTime(0, now)
    gate.gain.linearRampToValueAtTime(level, now + shape.attack)

    source.connect(band).connect(grain).connect(gate).connect(master)
    source.start(now)

    const one = { source, level: gate, until: now + seconds }
    this.running.set(name, one)
    this.closeAt(one, level, shape, now)
    // **결을 내는 것도 함께 멈추고 마디를 끊습니다.** 두고 가면 그 오실레이터 하나가
    // 판이 끝날 때까지 돌고, 끊지 않은 마디는 그래프에 남아 렌더 블록마다 계산됩니다.
    source.onended = () => {
      flick.stop()
      gate.disconnect()
      if (this.running.get(name) === one) this.running.delete(name)
    }
  }

  /**
   * 그 지속 보이스가 언제 어떻게 끝나는가를 다시 예약합니다.
   *
   * **예약된 것을 걷고 새로 답니다.** 늘릴 때마다 앞의 잦아듦이 남아 있으면 그것이 먼저
   * 도착해서, 아직 움직이는 중에 소리가 사라집니다.
   */
  private closeAt(
    one: { source: AudioBufferSourceNode; level: GainNode; until: number },
    level: number, shape: Sweep, now: number,
  ): void {
    const gain = one.level.gain
    gain.cancelScheduledValues(now)
    gain.setValueAtTime(gain.value, now)
    gain.linearRampToValueAtTime(level, now + shape.attack)
    gain.setValueAtTime(level, one.until)
    gain.linearRampToValueAtTime(0, one.until + shape.release)
    // **0 에 닿은 다음에 멈춥니다.** 나는 중에 멈추면 잘린 자리에서 소리가 납니다.
    one.source.stop(one.until + shape.release + 0.02)
  }

  /**
   * 지금 소리가 어떤 상태인가.
   *
   * **소리는 조용히 실패합니다.** 안 나는 자리를 화면에서 굽어 볼 수가 없어서, 안 나는
   * 그 순간에 무엇이 어긋났는지를 한 자리에서 읽어야 합니다 — 원인이 넷이고 고치는 자리가
   * 저마다 다릅니다.
   *
   * |읽히는 것|뜻|
   * |--|--|
   * |`state` 가 `suspended`|소리 길이 잠들었습니다. 뒤로 물러난 뒤 깨우지 못한 것입니다|
   * |`master` 가 0 에 가까움|내려놓은 것이 돌아오지 않았습니다|
   * |`muted` 가 참|옵션이 꺼 놓았습니다|
   * |`voices` 가 크고 `played` 는 도는데 소리가 없음|겹침 계수기가 막고 있습니다|
   *
   * 판이 도는 중에 `__clover.audio` 로 읽습니다.
   */
  report(): {
    open: boolean; state: string; time: number; master: number; level: number
    muted: boolean; holding: boolean; voices: number; sweeps: string[]
    samples: number; played: string[]; squeeze: number; peak: number
  } {
    const context = this.context
    const now = context?.currentTime ?? 0
    let held = 0
    for (const list of this.voices.values()) {
      held += list.filter(one => one.until > now).length
    }
    return {
      open: context !== undefined,
      state: context?.state ?? 'none',
      time: Number(now.toFixed(2)),
      master: this.master ? Number(this.master.gain.value.toFixed(3)) : -1,
      level: this.level,
      muted: this.muted,
      // 재우기로 걸어 두고 아직 도착하지 않은 것이 있는가.
      holding: this.holding !== undefined,
      voices: held,
      sweeps: [...this.running.keys()],
      samples: this.samples.size,
      played: this.played.slice(-6),
      // **압축기가 살아 있는가.** 지금 깎고 있는 정도(dB)이고, 마디가 없어졌으면 -1 입니다.
      squeeze: this.squeeze ? Number(this.squeeze.reduction.toFixed(2)) : -1,
      // **출력에 실제로 흐르는 값.** 0 이 아니면 게임은 소리를 내고 있습니다.
      peak: Number(this.loudest.toFixed(4)),
    }
  }

  /**
   * 조커 하나가 내는 음.
   *
   * **목소리를 걷고 악기로 바꿨습니다.** 한동안 웅얼거리는 소리를 냈는데, 그것이 카지노
   * 소리 위에 얹혀 어긋나 들렸습니다. 원인이 다듬기의 문제가 아니라 갈래의 문제입니다 —
   * 이 게임의 득점 소리는 값이 오르는 가락이고, 웅얼거림은 음높이가 정해지지 않아 그
   * 가락의 한 음이 될 수 없습니다.
   *
   * **음색이 그 조커를 가리킵니다.** 누가 낸 값인지는 음색으로 남고 값이 얼마나 큰지는
   * 음높이로 남으므로, 둘이 함께 들려도 서로를 덮지 않습니다. 목소리가 하던 역할을
   * 그대로 받되 가락 위에 있습니다.
   */
  jokerVoice(uid: number, semitones: number, strength = 1, pan = 0): void {
    const at = Math.abs(uid * 2_654_435_761) % JOKER_TIMBRES.length
    this.tone(JOKER_TIMBRES[at], semitones, strength, pan)
  }

  /**
   * 잡음 한 번.
   *
   * 좁은 대역만 남깁니다 — 그 대역이 어디냐가 「종이」와 「금속」과 「바람」을 가릅니다.
   * 대역이 움직이면 쓸리는 소리가 됩니다.
   */
  private hissAt(noise: Noise, now: number, into: AudioNode): void {
    const context = this.context
    if (!context || !this.hiss) return

    const source = context.createBufferSource()
    source.buffer = this.hiss
    source.loop = true

    const band = context.createBiquadFilter()
    band.type = 'bandpass'
    band.Q.value = noise.q
    band.frequency.setValueAtTime(noise.hz, now)
    if (noise.sweep !== 0) {
      band.frequency.exponentialRampToValueAtTime(
        Math.max(80, noise.hz * noise.sweep), now + noise.length)
    }

    const gain = context.createGain()
    gain.gain.setValueAtTime(0, now)
    gain.gain.linearRampToValueAtTime(noise.gain, now + 0.004)
    gain.gain.exponentialRampToValueAtTime(0.0001, now + noise.length)

    source.connect(band).connect(gain).connect(into)
    source.start(now)
    source.stop(now + noise.length + 0.02)
  }
}

/**
 * 음 하나의 음색.
 *
 * **부분음을 쌓아 만듭니다.** 어느 배수의 음이 얼마나 크고 얼마나 오래 남는가가 마림바와
 * 종을 가릅니다 — 마림바는 4배음이 잠깐 있다 사라지고, 종은 정수배가 아닌 부분음이 오래
 * 남습니다. 그 표가 이것입니다.
 */
export interface Timbre {
  /** 기준음에서 반음 몇 개 위인가. */
  offset: number
  /**
   * 부분음들. `[기본음의 몇 배, 크기, 감쇠가 기본의 몇 배]` 입니다.
   *
   * **높은 부분음일수록 빨리 사라집니다.** 그것이 「때린 것」과 「분 것」을 가릅니다.
   */
  parts: readonly (readonly [number, number, number])[]
  wave: OscillatorType
  /** 기본음이 잦아드는 데 걸리는 시간. */
  decay: number
  gain: number
  /** 위쪽을 깎는 자리. 0 이면 깎지 않습니다. */
  cut: number
}

/**
 * 음색들.
 *
 * **조커마다 하나씩 돌아갑니다.** 어느 조커가 낸 값인지가 음색으로 남고, 값이 얼마나
 * 큰지는 음높이로 남습니다 — 둘이 다른 채널이라 함께 들려도 서로를 덮지 않습니다.
 */
export const TIMBRE = {
  /** 나무 채. 짧고 둥급니다. */
  marimba: {
    offset: 24, wave: 'sine', decay: 0.42, gain: 0.30, cut: 0,
    parts: [[1, 1, 1], [4, 0.26, 0.42], [9.2, 0.07, 0.22]],
  },
  /** 쇠 판. 밝고 오래 남습니다. */
  glass: {
    offset: 31, wave: 'sine', decay: 0.85, gain: 0.22, cut: 0,
    parts: [[1, 1, 1], [2.76, 0.38, 0.7], [5.4, 0.16, 0.42]],
  },
  /** 종. 정수배가 아닌 부분음이 섞여 웅웅거립니다. */
  bell: {
    offset: 12, wave: 'sine', decay: 1.2, gain: 0.24, cut: 0,
    parts: [[1, 1, 1], [2.01, 0.45, 0.82], [2.98, 0.28, 0.6], [4.14, 0.13, 0.4]],
  },
  /** 뜯은 줄. 앞이 거칠고 곧 둥글어집니다. */
  pluck: {
    offset: 19, wave: 'sawtooth', decay: 0.34, gain: 0.20, cut: 2400,
    parts: [[1, 1, 1]],
  },
  /** 나무 토막. 거의 타점만 남습니다. */
  wood: {
    offset: 26, wave: 'triangle', decay: 0.15, gain: 0.26, cut: 0,
    parts: [[1, 1, 1], [3.1, 0.44, 0.32]],
  },
  /** 득점 사슬이 오르는 소리. 음원 위에 겹치는 것이라 얇습니다. */
  chime: {
    offset: 28, wave: 'triangle', decay: 0.26, gain: 0.16, cut: 0,
    parts: [[1, 1, 1], [3, 0.2, 0.5]],
  },
} as const satisfies Record<string, Timbre>

export type ToneName = keyof typeof TIMBRE

/** 조커에 돌아가는 음색들. **`chime` 은 사슬의 것이므로 빠집니다.** */
export const JOKER_TIMBRES: readonly ToneName[] = ['marimba', 'glass', 'bell', 'pluck', 'wood']

/**
 * 여럿이 한꺼번에 움직이는 동안의 소리 하나.
 *
 * **개수만큼 트리거하지 않습니다.** 스무 장이 0.4초 안에 덱으로 들어올 때 장마다 원샷을
 * 내면 보이스가 스물이고, 소리는 힘으로 더해지므로 합이 한 장의 √20배 — 13dB 위입니다.
 * 몇 장에 한 번으로 줄여도 남는 것은 같습니다: 0.6초짜리 음원 다섯이 겹쳐 「드르르륵」이
 * 되고, 그것은 카드가 쌓이는 소리가 아닙니다.
 *
 * **그래서 지속 보이스 하나로 냅니다.** 장수와 무관하게 하나이고, 크기가 개수를 따라
 * 오르지 않습니다. 알릴 것은 「지금 돌려받는 중이다」 하나이므로 그것으로 충분합니다.
 */
interface Sweep {
  gain: number
  /** 대역의 처음과 끝. 올라가면 펼치는 쪽이고 내려가면 쌓이는 쪽입니다. */
  from: number
  to: number
  q: number
  /**
   * 스치는 결. 초당 몇 번인가와 그 깊이입니다.
   *
   * **이것이 잡음과 카드를 가릅니다.** 대역만 남긴 잡음은 바람이고, 그것을 초당 40~60번
   * 끊으면 낱장이 지나가는 소리가 됩니다.
   */
  rate: number
  depth: number
  attack: number
  release: number
}

const SWEEP = {
  /** 패가 깔립니다. 펼치는 쪽이라 대역이 올라갑니다. */
  deal: {
    gain: 0.15, from: 1500, to: 2600, q: 0.9, rate: 40, depth: 0.55,
    attack: 0.02, release: 0.10,
  },
  /** 카드가 덱으로 돌아옵니다. 쌓이는 쪽이라 대역이 내려갑니다. */
  recall: {
    gain: 0.17, from: 2400, to: 1100, q: 0.8, rate: 58, depth: 0.6,
    attack: 0.015, release: 0.12,
  },
  /** 판의 카드가 딜러에게 쓸려 나갑니다. 회수보다 앞이고 더 가볍습니다. */
  retire: {
    gain: 0.14, from: 1900, to: 950, q: 0.85, rate: 46, depth: 0.5,
    attack: 0.02, release: 0.14,
  },
} as const satisfies Record<string, Sweep>

export type SweepName = keyof typeof SWEEP

/** 잡음 층 하나. `sweep` 은 끝날 때 대역이 몇 배가 되는가입니다. */
interface Noise {
  gain: number
  length: number
  hz: number
  q: number
  sweep: number
}

interface Shape {
  wave: OscillatorType
  /** 기준음에서 반음 몇 개 위인가. */
  offset: number
  length: number
  /** 음의 크기. **0 이면 음 없이 잡음만 납니다** — 카드와 종이가 그렇습니다. */
  gain: number
  /** 소리가 나는 동안 음이 얼마나 움직이는가. */
  glide: number
  noise?: Noise
}

const DEFAULT_SHAPE: Shape = { wave: 'triangle', offset: 12, length: 0.09, gain: 0.35, glide: 0 }

/**
 * 소리마다의 파형. **연산마다 다르고 조커마다 다르지 않습니다.**
 *
 * 카드와 종이와 동전은 `gain` 이 0 이거나 작고 잡음이 본체입니다 — 사인파로 만든 「탁」은
 * 어떤 값을 줘도 전자음이고, 카드가 놓이는 소리로 들리지 않습니다.
 */
const SHAPE: Record<string, Shape> = {
  // ---------------------------------------------------------------- 득점
  card_chip: { wave: 'triangle', offset: 19, length: 0.07, gain: 0.30, glide: 0 },
  card_mult: { wave: 'sawtooth', offset: 14, length: 0.08, gain: 0.26, glide: 0 },
  joker_add: { wave: 'square', offset: 17, length: 0.08, gain: 0.22, glide: 0 },
  joker_mul: {
    wave: 'sawtooth', offset: 21, length: 0.16, gain: 0.30, glide: 5,
    noise: { gain: 0.10, length: 0.14, hz: 900, q: 1.2, sweep: 3 },
  },
  joker_money: { wave: 'triangle', offset: 24, length: 0.10, gain: 0.26, glide: 3 },
  joker_fizzle: {
    wave: 'sine', offset: 5, length: 0.10, gain: 0.10, glide: -4,
    noise: { gain: 0.12, length: 0.16, hz: 1600, q: 0.8, sweep: 0.3 },
  },
  retrigger: { wave: 'square', offset: 22, length: 0.05, gain: 0.20, glide: 2 },
  score_count: { wave: 'triangle', offset: 16, length: 0.05, gain: 0.16, glide: 0 },
  score_settle: { wave: 'sine', offset: 12, length: 0.30, gain: 0.34, glide: 7 },
  blind_clear: { wave: 'triangle', offset: 24, length: 0.45, gain: 0.38, glide: 12 },
  blind_fail: { wave: 'sawtooth', offset: 3, length: 0.55, gain: 0.30, glide: -12 },

  // ---------------------------------------------------------------- 카드
  //
  // **전부 잡음입니다.** 종이가 스치고 닿는 소리이지 음이 아닙니다. 대역이 어디냐가 「스침」
  // 과 「닿음」을 가르고, 대역이 움직이면 쓸리는 소리가 됩니다.
  card_draw: {
    wave: 'sine', offset: 20, length: 0.04, gain: 0,
    glide: 0, noise: { gain: 0.16, length: 0.055, hz: 2600, q: 0.7, sweep: 0.35 },
  },
  card_select: {
    wave: 'sine', offset: 26, length: 0.03, gain: 0.05,
    glide: 0, noise: { gain: 0.11, length: 0.035, hz: 3400, q: 1.4, sweep: 1 },
  },
  /** 손패의 자리에 놓입니다. 짧고 마른 소리. */
  card_place: {
    wave: 'sine', offset: 10, length: 0.03, gain: 0.04,
    glide: 0, noise: { gain: 0.18, length: 0.05, hz: 1500, q: 0.9, sweep: 0.4 },
  },
  /** 낸 카드가 판에 「짝」 붙습니다. 더 낮고 더 세게. */
  card_slam: {
    wave: 'square', offset: 4, length: 0.04, gain: 0.10, glide: -6,
    noise: { gain: 0.26, length: 0.07, hz: 900, q: 0.7, sweep: 0.28 },
  },
  /** 뒷면이 앞면으로 뒤집힙니다. */
  card_flip: {
    wave: 'sine', offset: 14, length: 0.03, gain: 0.05,
    glide: 0, noise: { gain: 0.14, length: 0.06, hz: 2000, q: 0.6, sweep: 2.2 },
  },
  card_destroy: {
    wave: 'sawtooth', offset: 8, length: 0.16, gain: 0.16, glide: -8,
    noise: { gain: 0.20, length: 0.22, hz: 1800, q: 0.5, sweep: 0.2 },
  },

  // ---------------------------------------------------------------- 돈
  //
  // **음이 하나씩 올라가는 것이 이 연출의 절반입니다.**
  coin_land: {
    wave: 'triangle', offset: 31, length: 0.06, gain: 0.18, glide: 2,
    noise: { gain: 0.10, length: 0.05, hz: 5200, q: 3, sweep: 0.7 },
  },
  coin_lose: { wave: 'sine', offset: 9, length: 0.09, gain: 0.18, glide: -6 },

  // ---------------------------------------------------------------- 조커
  joker_buy: {
    wave: 'triangle', offset: 22, length: 0.12, gain: 0.26, glide: 5,
    noise: { gain: 0.10, length: 0.06, hz: 2400, q: 1.2, sweep: 1.6 },
  },
  joker_sell: {
    wave: 'triangle', offset: 18, length: 0.12, gain: 0.22, glide: -5,
    noise: { gain: 0.10, length: 0.07, hz: 3000, q: 2, sweep: 0.5 },
  },
  /** 타서 사라집니다. 불이 붙는 소리라 대역이 넓고 길게 꺼집니다. */
  joker_burn: {
    wave: 'sawtooth', offset: 2, length: 0.24, gain: 0.10, glide: -10,
    noise: { gain: 0.22, length: 0.42, hz: 2400, q: 0.4, sweep: 0.16 },
  },
  /** 조커의 자리를 바꿉니다. 카드보다 무겁게. */
  joker_move: {
    wave: 'sine', offset: 6, length: 0.03, gain: 0.06,
    glide: 0, noise: { gain: 0.14, length: 0.05, hz: 1100, q: 0.8, sweep: 0.5 },
  },

  // ---------------------------------------------------------------- 소모품과 팩
  consumable_use: {
    wave: 'sine', offset: 27, length: 0.26, gain: 0.24, glide: 9,
    noise: { gain: 0.09, length: 0.20, hz: 3600, q: 1.4, sweep: 2.4 },
  },
  /** 봉지를 뜯습니다. **길게 쓸리는 잡음 하나가 전부입니다.** */
  pack_open: {
    wave: 'sine', offset: 8, length: 0.05, gain: 0.06, glide: 0,
    noise: { gain: 0.24, length: 0.30, hz: 3200, q: 0.5, sweep: 0.22 },
  },
  pack_pick: {
    wave: 'triangle', offset: 24, length: 0.09, gain: 0.22, glide: 4,
    noise: { gain: 0.10, length: 0.05, hz: 2800, q: 1.2, sweep: 1 },
  },

  // ---------------------------------------------------------------- 상점과 판
  shop_enter: { wave: 'triangle', offset: 14, length: 0.22, gain: 0.26, glide: 4 },
  shop_buy: { wave: 'triangle', offset: 22, length: 0.10, gain: 0.28, glide: 3 },
  shop_reroll: {
    wave: 'square', offset: 12, length: 0.09, gain: 0.18, glide: -2,
    noise: { gain: 0.14, length: 0.12, hz: 2200, q: 0.6, sweep: 0.4 },
  },
  voucher_buy: { wave: 'triangle', offset: 19, length: 0.20, gain: 0.28, glide: 7 },
  blind_select: { wave: 'triangle', offset: 16, length: 0.16, gain: 0.26, glide: 5 },
  blind_skip: { wave: 'sine', offset: 11, length: 0.14, gain: 0.20, glide: -3 },
  boss_reveal: {
    wave: 'sawtooth', offset: -5, length: 0.60, gain: 0.32, glide: -3,
    noise: { gain: 0.10, length: 0.50, hz: 400, q: 0.5, sweep: 0.5 },
  },

  // ---------------------------------------------------------------- 화면
  //
  // **작게 냅니다.** 버튼과 판은 자주 눌리므로, 득점만큼 들리면 그 소리가 화면을 덮습니다.
  button: {
    wave: 'sine', offset: 24, length: 0.025, gain: 0.07,
    glide: 0, noise: { gain: 0.06, length: 0.03, hz: 3800, q: 2, sweep: 1 },
  },
  panel_open: { wave: 'sine', offset: 18, length: 0.10, gain: 0.12, glide: 5 },
  panel_close: { wave: 'sine', offset: 18, length: 0.09, gain: 0.10, glide: -5 },

  run_win: { wave: 'triangle', offset: 24, length: 0.9, gain: 0.40, glide: 12 },
  run_lose: { wave: 'sine', offset: 0, length: 1.1, gain: 0.34, glide: -12 },
}

