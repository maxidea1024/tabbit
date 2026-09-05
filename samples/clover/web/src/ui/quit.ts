// 게임을 나갑니다.
//
// **한 벌이 세 곳에서 돕니다.** 같은 빌드가 브라우저 · Electron · Capacitor 안에서 도는데
// 「나가기」의 뜻이 셋에서 다릅니다 — 앱은 스스로 끝나고, Electron 은 창을 닫으면 끝나고,
// 브라우저에서는 스크립트가 자기 탭을 닫을 수 없습니다.
//
// **브라우저에서는 빈 쪽으로 갑니다.** `window.close()` 는 스크립트가 열지 않은 탭에서
// 조용히 아무 일도 하지 않으므로, 그것만 부르면 누른 사람에게는 고장으로 보입니다.

/** 앱 안에서 도는가. Capacitor 가 자기 표시를 남깁니다. */
function inApp(): boolean {
  const flag = (globalThis as { Capacitor?: { isNativePlatform?: () => boolean } }).Capacitor
  return flag?.isNativePlatform?.() === true
}

function exitApp(): boolean {
  const app = (globalThis as {
    Capacitor?: { Plugins?: { App?: { exitApp?: () => void } } }
  }).Capacitor?.Plugins?.App
  if (!app?.exitApp) return false
  app.exitApp()
  return true
}

/**
 * 게임을 나갑니다.
 *
 * **묻는 것은 부르는 쪽이 합니다.** 이 함수가 부르는 순간 돌아올 자리가 없습니다.
 */
export function quitGame(): void {
  if (inApp() && exitApp()) return

  // Electron 에서는 창을 닫으면 앱이 끝납니다 — 렌더러가 자기 창을 닫는 것은 허용됩니다.
  // 브라우저에서는 조용히 아무 일도 하지 않으므로, 그 뒤에 빈 쪽으로 갑니다.
  try {
    window.close()
  } catch {
    // 닫을 수 없는 자리입니다. 아래로 갑니다.
  }
  // **닫혔으면 이 줄에 닿지 않습니다.** 닿았다면 닫히지 않은 것입니다.
  setTimeout(() => { location.href = 'about:blank' }, 120)
}
