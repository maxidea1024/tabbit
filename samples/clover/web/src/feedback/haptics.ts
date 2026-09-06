// 진동.
//
// **손에 쥔 기계에만 있는 채널입니다.** 데스크탑에는 진동자가 없습니다 — 그런데 크롬
// 데스크탑의 `navigator.vibrate` 는 있고, `true` 를 돌려주면서 아무 일도 하지 않습니다. 그래서 여는 조건이 「함수가 있는가」가 아니라 「진동자가 있는
// 기계인가」입니다. 그러지 않으면 옵션에 아무것도 하지 않는 줄이 하나 생깁니다.
//
// **중요한 순간에만 냅니다.** 조커가 배수를 붙일 때마다 울리게 하면 한 판에 열몇 번이
// 2초 안에 몰리고, 그만큼 자주 오는 것은 알림이 아니라 잡음입니다 — 소리가 그것을 이미
// 하고 있고, 진동은 소리보다 거칠어서 같은 빈도를 견디지 못합니다.
//
// 길은 셋이고 되는 첫 번째를 씁니다.
//
// |길|어디서|
// |--|--|
// |`Capacitor.Plugins.Haptics`|웹 빌드가 그 꾸러미를 담았을 때. 지금은 담지 않습니다|
// |`Capacitor.nativePromise('Haptics', …)`|안드로이드 앱. **다리가 꾸러미 없이도 열려 있는 자리입니다** — 네이티브 플러그인만 있으면 됩니다|
// |`navigator.vibrate`|폰의 브라우저. 세기를 정할 수 없어 길이로만 흉냅니다|

/**
 * 진동으로 알리는 순간들.
 *
 * **여섯뿐이고 늘리지 않는 것이 규약입니다.** 무엇이 중요한 순간인지는 이 목록이 정하고,
 * 그 목록이 길어지면 「중요한 순간」이라는 말의 뜻이 없어집니다.
 */
export type HapticBeat = 'play' | 'settle' | 'clear' | 'boss' | 'win' | 'lose'

/** 네이티브가 아는 세기. 플러그인의 이름 그대로입니다. */
type Native =
  | { call: 'impact'; style: 'LIGHT' | 'MEDIUM' | 'HEAVY' }
  | { call: 'notification'; type: 'SUCCESS' | 'WARNING' | 'ERROR' }

/**
 * 순간마다의 진동.
 *
 * **`web` 은 네이티브의 그 세기가 쓰는 파형입니다.** 플러그인의 안드로이드 구현이
 * `VibrationEffect` 로 내는 것과 같은 시간표이고, 다른 것은 세기를 정할 수 없어 진폭이
 * 빠지는 것뿐입니다 — 그래서 두 길의 느낌이 어긋나지 않습니다.
 *
 * 안드로이드의 시간표는 맨 앞이 「기다리는 시간」이고 `navigator.vibrate` 는 맨 앞이
 * 「떠는 시간」이므로, 앞의 0 하나가 빠진 모양입니다.
 */
const BEATS: Record<HapticBeat,
                    { native: Native; web: number | number[]; rank: number }> = {
  // 낸 카드가 판에 닿는 순간. **가장 약합니다** — 그다음에 올 득점이 이것보다 세야
  // 순서가 읽힙니다.
  play: { native: { call: 'impact', style: 'LIGHT' }, web: 20, rank: 1 },
  // 보스가 드러난 것. **좋은 일도 나쁜 일도 아니라 「무엇이 온다」입니다** — 그래서
  // 알림이 아니라 한 방이고, 득점보다 약합니다.
  boss: { native: { call: 'impact', style: 'MEDIUM' }, web: 43, rank: 2 },
  // 그 판의 점수가 확정되는 순간. **이 게임에서 사람이 기다리는 자리입니다.**
  settle: { native: { call: 'impact', style: 'HEAVY' }, web: 61, rank: 3 },
  // 블라인드를 넘긴 것. 한 방이 아니라 두 번 끊어지므로 「좋은 일」로 읽힙니다.
  clear: { native: { call: 'notification', type: 'SUCCESS' }, web: [35, 65, 21], rank: 4 },
  win: { native: { call: 'notification', type: 'SUCCESS' }, web: [35, 65, 21], rank: 5 },
  lose: { native: { call: 'notification', type: 'ERROR' }, web: [27, 45, 50], rank: 5 },
}

/**
 * 두 진동 사이의 가장 짧은 간격.
 *
 * **가장 긴 파형(`lose`)이 122ms 입니다.** 그보다 짧은 간격으로 다음 것이 오면 앞의 것을
 * 자르고 겹쳐서, 둘 다 무엇이었는지 알 수 없는 한 덩어리가 됩니다.
 *
 * **더 중한 순간은 이 간격을 지나갑니다** — `rank` 가 그 차례입니다. 간격만으로 막으면
 * 앞선 작은 것이 뒤에 오는 큰 것을 가릴 수 있고, 그 방향은 거꾸로여야 합니다. 배속을
 * 4로 두면 득점 확정과 블라인드 격파의 사이가 260ms 까지 줄어들므로 여유가 많지 않습니다.
 */
const GAP_MS = 180

/** 다리가 남긴 것. 없는 곳에서는 전부 `undefined` 입니다. */
interface Bridge {
  isNativePlatform?: () => boolean
  nativePromise?: (plugin: string, method: string,
                   options: Record<string, string>) => Promise<unknown>
  Plugins?: {
    Haptics?: {
      impact?: (options: { style: string }) => Promise<void>
      notification?: (options: { type: string }) => Promise<void>
    }
  }
}

function bridge(): Bridge | undefined {
  return (globalThis as { Capacitor?: Bridge }).Capacitor
}

/** 앱 안에서 돌고 있는가. */
function inApp(): boolean {
  return bridge()?.isNativePlatform?.() === true
}

/**
 * 진동자가 있는 기계인가.
 *
 * **거친 손가락으로 짚는 화면인지를 봅니다.** `navigator.vibrate` 가 있는지만 보면
 * 데스크탑 크롬이 통과합니다 — 거기서는 그 함수가 `true` 를 돌려주고 아무 일도 하지
 * 않으므로, 옵션에 아무것도 하지 않는 줄이 하나 생깁니다.
 */
function canVibrate(): boolean {
  if (typeof navigator === 'undefined' || typeof navigator.vibrate !== 'function') return false
  if (typeof matchMedia !== 'function') return false
  return matchMedia('(pointer: coarse)').matches
}

/**
 * 이 기계에서 진동을 낼 수 있는가. **옵션의 「입력」 탭이 이것으로 서고 없어집니다.**
 *
 * 앱이면 그 다리가 하고, 앱이 아니면 폰의 브라우저입니다. 데스크탑과 일렉트론은 둘 다
 * 아니므로 그 탭이 아예 없습니다 — 켤 수 없는 것을 꺼진 채로 늘어놓지 않습니다.
 */
export function hapticsAvailable(): boolean {
  return inApp() || canVibrate()
}

/**
 * 진동.
 *
 * **문을 하나로 둡니다** — 부르는 자리가 여섯 곳이므로, 저마다 옵션과 기계를 보게 하면
 * 언젠가 하나가 빠집니다.
 */
export class Haptics {
  /** 진동을 내는가. 옵션이 정합니다. */
  enabled = true

  /** 이 기계가 진동을 낼 수 있는가. **판이 도는 동안 바뀌지 않으므로 한 번 잽니다.** */
  private readonly possible = hapticsAvailable()

  /** 마지막으로 낸 시각. 너무 붙은 것은 내지 않습니다. */
  private last = -Infinity
  /** 마지막으로 낸 것이 얼마나 중한 순간이었는가. */
  private lastRank = 0

  get available(): boolean {
    return this.possible
  }

  /**
   * 한 순간을 알립니다.
   *
   * **던지지 않습니다.** 진동은 게임의 곁가지이므로, 어느 길이 막혀 있더라도 그것이
   * 판을 멈출 이유가 되지 않습니다 — 권한이 없는 기계와 절전 중인 기계가 실제로 거부합니다.
   */
  play(beat: HapticBeat): void {
    if (!this.enabled || !this.possible) return

    const one = BEATS[beat]
    const now = typeof performance === 'undefined' ? Date.now() : performance.now()
    if (now - this.last < GAP_MS && one.rank <= this.lastRank) return
    this.last = now
    this.lastRank = one.rank

    const plugin = bridge()?.Plugins?.Haptics
    try {
      if (one.native.call === 'impact' && plugin?.impact) {
        void plugin.impact({ style: one.native.style }).catch(() => undefined)
        return
      }
      if (one.native.call === 'notification' && plugin?.notification) {
        void plugin.notification({ type: one.native.type }).catch(() => undefined)
        return
      }

      const native = bridge()?.nativePromise
      if (inApp() && native) {
        const options: Record<string, string> = one.native.call === 'impact'
          ? { style: one.native.style } : { type: one.native.type }
        void native('Haptics', one.native.call, options).catch(() => undefined)
        return
      }

      navigator.vibrate(one.web)
    } catch {
      // 진동 하나 때문에 판이 멈추지 않습니다.
    }
  }
}
