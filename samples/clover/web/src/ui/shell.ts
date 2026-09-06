// 게임을 담고 있는 껍데기.
//
// **한 벌이 세 곳에서 돕니다** — 브라우저 · Electron · 안드로이드 앱. 게임의 코드는 셋에서
// 같고, 「나간다」·「뒤로 간다」·「뒤로 물러났다」의 뜻만 다릅니다. 그 셋을 이 파일이 맡고,
// 부르는 쪽은 어디서 도는지 모릅니다.
//
// **꾸러미를 담지 않습니다.** Capacitor 는 앱의 WebView 에 다리를 미리 넣어 두고, 그 다리는
// 자바스크립트 쪽 꾸러미가 없어도 열려 있습니다 — 네이티브에 그 플러그인만 있으면 됩니다.
// 진동이 지나는 그 다리이고, 여기서도 같은 길을 씁니다. 그래서 웹 빌드에 안드로이드 전용
// 의존이 하나도 늘지 않습니다.

/** Capacitor 가 열어 두는 다리. */
interface Bridge {
  isNativePlatform?: () => boolean
  nativePromise?: (plugin: string, method: string,
                   options: Record<string, never>) => Promise<unknown>
  addListener?: (plugin: string, event: string,
                 callback: (data: unknown) => void) => { remove: () => Promise<void> }
  Plugins?: { App?: { exitApp?: () => void | Promise<void> } }
}

function bridge(): Bridge | undefined {
  return (globalThis as { Capacitor?: Bridge }).Capacitor
}

/** 앱 안에서 도는가. Capacitor 가 자기 표시를 남깁니다. */
export function inApp(): boolean {
  return bridge()?.isNativePlatform?.() === true
}

/**
 * 나갈 수 있는 자리인가.
 *
 * **묻기 전에 봅니다.** 할 수 없는 것을 물어 놓고 「예」를 눌렀을 때 아무 일도 일어나지
 * 않으면, 누른 사람에게는 그것이 고장입니다.
 */
export function canQuit(): boolean {
  if (inApp()) return true
  // Electron 은 자기 표시를 `userAgent` 에 남깁니다.
  return navigator.userAgent.includes('Electron')
}

/**
 * 게임을 나갑니다. **나갈 수 있었으면 참입니다.**
 *
 * **묻는 것은 부르는 쪽이 합니다.** 참을 돌려주는 순간 돌아올 자리가 없고, 거짓이면
 * 아무 일도 일어나지 않았으므로 부르는 쪽이 그 사실을 사람에게 알립니다.
 *
 * **빈 쪽으로 보내지 않습니다.** `about:blank` 로 옮겨 가게 두었더니 안드로이드에서 그
 * 주소를 시스템이 받아 「어느 앱으로 열까요」를 띄웠습니다 — 나가기를 눌렀는데 앱을 고르는
 * 화면이 뜨던 것이 그것입니다. 브라우저에서 할 수 있는 것이 없으면 **아무 데도 가지 않고**
 * 그 사실을 알립니다.
 */
export function quitGame(): boolean {
  if (inApp()) return exitApp()

  // Electron 에서는 창을 닫으면 앱이 끝납니다 — 렌더러가 자기 창을 닫는 것은 허용됩니다.
  // **브라우저에서는 조용히 아무 일도 하지 않습니다.**
  const before = Date.now()
  try {
    window.close()
  } catch {
    // 닫을 수 없는 자리입니다.
  }
  // **닫혔으면 이 줄에 닿지 않습니다.** 닿았다면 닫히지 않은 것이고, 그때 할 수 있는 것이
  // 없습니다 — 빈 쪽으로 보내는 것은 나가는 것이 아니라 다른 쪽을 여는 것입니다.
  return Date.now() - before > 1000
}

/**
 * 앱을 끝냅니다. **끝낼 길을 찾았으면 참입니다.**
 *
 * 길이 둘이고 되는 첫 번째를 씁니다 — 웹 빌드가 꾸러미를 담았으면 그 꾸러미이고, 담지
 * 않았으면 다리를 직접 지납니다.
 */
function exitApp(): boolean {
  const found = bridge()
  if (!found) return false

  const plugin = found.Plugins?.App
  if (plugin?.exitApp) {
    void plugin.exitApp()
    return true
  }
  if (found.nativePromise) {
    void found.nativePromise('App', 'exitApp', {} as Record<string, never>)
      .catch(() => undefined)
    return true
  }
  return false
}

/**
 * 뒤로 가기.
 *
 * **안드로이드에만 있는 단추이고, 아무도 받지 않으면 시스템이 가져갑니다.** 받지 않는
 * 동안에는 누를 때마다 앱이 뒤로 물러났습니다 — 판이 떠 있어도 그것을 닫지 않고 통째로
 * 물러났으므로, 눌렀을 때 무엇이 일어날지 알 수 없는 단추였습니다.
 *
 * **`ESC` 와 같은 자리로 보냅니다.** 안드로이드의 뒤로 가기는 키 사건을 만들지 않으므로
 * 화면의 `keydown` 은 이것을 보지 못합니다 — 두 곳에서 같은 일을 하려면 여기서 보내야
 * 합니다.
 */
export function onBackButton(handler: () => void): void {
  const found = bridge()
  if (!found?.addListener) return
  found.addListener('App', 'backButton', () => handler())
}

/**
 * 앞으로 나왔는가 · 뒤로 물러났는가.
 *
 * **두 길을 하나로 모읍니다.** 앱에서는 Capacitor 가 알리고 그 밖에서는 문서가 알리는데,
 * 앱에서도 문서의 것이 함께 오므로 어느 하나만 두면 한쪽이 빠지거나 두 번 옵니다 —
 * 마지막으로 알린 것과 같으면 알리지 않습니다.
 *
 * **뒤로 물러난 동안에는 그리지도 소리 내지도 않아야 합니다.** 안드로이드는 WebView 를
 * 대신 멈춰 주지 않습니다 — 화면이 없는 동안에도 초당 예순 번을 그리고 그것이 그대로
 * 배터리입니다.
 */
export function onAppState(handler: (active: boolean) => void): void {
  let last: boolean | undefined

  const tell = (active: boolean) => {
    if (active === last) return
    last = active
    handler(active)
  }

  const found = bridge()
  if (found?.addListener) {
    found.addListener('App', 'appStateChange', data => {
      tell((data as { isActive?: boolean } | undefined)?.isActive === true)
    })
  }

  if (typeof document !== 'undefined') {
    document.addEventListener('visibilitychange', () => tell(!document.hidden))
  }
}

/**
 * 화면을 켜 둡니다.
 *
 * **판이 도는 동안만입니다.** 이 게임에는 시간 제한이 없어서 어느 카드를 낼지 고르는 동안
 * 손이 화면에 닿지 않고, 그러면 기계가 정해 둔 시간이 지나 화면이 어두워집니다 — 고르는
 * 중에 어두워지는 것은 기다리라는 뜻이 아닙니다.
 *
 * **꾸러미를 쓰지 않습니다.** 브라우저의 `WakeLock` 이 안드로이드의 WebView 에도 있고,
 * 권한도 필요하지 않습니다. 없는 자리에서는 아무 일도 하지 않습니다.
 *
 * **뒤로 물러나면 기계가 스스로 놓습니다.** 돌아왔을 때 다시 잡는 것은 부르는 쪽의
 * 일이고, `onAppState` 가 그 자리입니다.
 */
export function keepAwake(on: boolean): void {
  const api = (navigator as Navigator & {
    wakeLock?: { request: (kind: 'screen') => Promise<WakeSentinel> }
  }).wakeLock
  if (!api) return
  wanted = on

  if (!on) {
    const held = sentinel
    sentinel = undefined
    void held?.release().catch(() => undefined)
    return
  }
  if (sentinel || asking) return
  asking = true
  void api.request('screen').then(got => {
    asking = false
    // 잡는 사이에 놓으라고 한 것이면 바로 놓습니다.
    if (!wanted) {
      void got.release().catch(() => undefined)
      return
    }
    sentinel = got
  }).catch(() => {
    asking = false
  })
}

interface WakeSentinel {
  release: () => Promise<void>
}

let sentinel: WakeSentinel | undefined
let asking = false
let wanted = false
