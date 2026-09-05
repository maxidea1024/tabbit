// 게임을 나갑니다.
//
// **한 벌이 세 곳에서 돕니다.** 같은 빌드가 브라우저 · Electron · 안드로이드 앱 안에서
// 도는데 「나가기」의 뜻이 셋에서 다릅니다 — 앱은 스스로 끝나고, Electron 은 창을 닫으면
// 끝나고, **브라우저에서는 스크립트가 자기 탭을 닫을 수 없습니다.**
//
// **빈 쪽으로 보내지 않습니다.** `about:blank` 로 옮겨 가게 두었더니 안드로이드에서 그
// 주소를 시스템이 받아 「어느 앱으로 열까요」를 띄웠습니다 — 나가기를 눌렀는데 앱을 고르는
// 화면이 뜨던 것이 그것입니다. 브라우저에서 할 수 있는 것이 없으면 **아무 데도 가지 않고**
// 그 사실을 부르는 쪽에 알립니다.

/** Capacitor 가 열어 두는 다리. **꾸러미를 담지 않아도 열려 있습니다.** */
interface Bridge {
  isNativePlatform?: () => boolean
  nativePromise?: (plugin: string, method: string,
                   options: Record<string, never>) => Promise<unknown>
  Plugins?: { App?: { exitApp?: () => void | Promise<void> } }
}

function bridge(): Bridge | undefined {
  return (globalThis as { Capacitor?: Bridge }).Capacitor
}

/** 앱 안에서 도는가. Capacitor 가 자기 표시를 남깁니다. */
function inApp(): boolean {
  return bridge()?.isNativePlatform?.() === true
}

/**
 * 앱을 끝냅니다. **끝낼 길을 찾았으면 참입니다.**
 *
 * 길이 둘이고 되는 첫 번째를 씁니다 — 웹 빌드가 `@capacitor/app` 을 담았으면 그 꾸러미이고,
 * 담지 않았으면 다리를 직접 지납니다. 진동이 지나는 그 다리입니다.
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
 * 게임을 나갑니다. **나갈 수 있었으면 참입니다.**
 *
 * **묻는 것은 부르는 쪽이 합니다.** 참을 돌려주는 순간 돌아올 자리가 없고, 거짓이면
 * 아무 일도 일어나지 않았으므로 부르는 쪽이 그 사실을 사람에게 알립니다.
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
