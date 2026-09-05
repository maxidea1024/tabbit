// 계정과 세션.
//
// **로그인은 리더보드의 것이 아닙니다.** 지금은 순위표가 그것을 쓰는 유일한 자리지만,
// 저장 동기화도 도전과제도 같은 계정 위에 올라갑니다 — 그래서 이 파일이 리더보드를
// 모르고, 리더보드가 이 파일을 압니다.
//
// **쿠키가 아니라 token 입니다.** 같은 웹 빌드가 `file://` 과 Capacitor 안에서도 도는데
// 거기에서 쿠키는 각각 다르게 동작합니다. 우리가 들고 헤더로 보내면 셋에서 같습니다.

/** 서버의 앞부분. 개발에서는 vite 가 8787 로 넘깁니다. */
const BASE = '/api'

/** 세션을 두는 자리. 옵션과 같은 자리이고 같은 방어입니다. */
const KEY = 'clover.session'

export interface Session {
  access: string
  refresh: string
}

export interface Device {
  id: number
  label: string
  createdAt: string
  usedAt: string
}


export interface Me {
  handle: string
  tier: string
  lastSeasonTier: string
  devices: Device[]
  /**
   * 무엇으로 로그인해 두었는가.
   *
   * **내 것에만 있습니다.** 남의 프로필에는 오지 않습니다 — 어느 계정으로 들어왔는지는
   * 순위표에 필요한 값이 아닙니다.
   */
  providers?: string[]
  ranks: {
    boardId: string; name: string; group: string; metric: string
    rank: number; value: number
  }[]
}

export type FailKind =
  | 'offline' | 'unauthorized' | 'bad_seed' | 'bad_fingerprint'
  | 'too_many' | 'taken' | 'cooldown' | 'shape' | 'unknown'

export class ApiError extends Error {
  constructor(readonly kind: FailKind, readonly status: number, message = '') {
    super(message)
  }
}

/**
 * 부른 자리에서 글을 내는 갈래.
 *
 * **여기 있는 것은 전역 알림을 띄우지 않습니다.** 이름이 겹친 것과 시드가 어긋난 것은
 * 그것을 물어본 판이 그 자리에서 적어야 하고, 화면 구석에 한 번 더 뜨면 같은 말이
 * 두 번입니다. **나머지는 전부 뜹니다** — 조용히 실패하는 것이 가장 나쁩니다.
 */
const HANDLED_INLINE: readonly FailKind[] = [
  'unauthorized', 'taken', 'cooldown', 'shape', 'bad_seed',
]

/** 실패한 갈래의 글 열쇠. `StringTable` 에 있습니다. */
export function failKey(error: unknown): string {
  const kind = error instanceof ApiError ? error.kind : 'unknown'
  return `ui.lb.fail.${kind}`
}

// ---------------------------------------------------------------------------
// 세션
// ---------------------------------------------------------------------------

let session: Session | undefined = load()

function load(): Session | undefined {
  try {
    const raw = localStorage.getItem(KEY)
    if (raw === null) return undefined
    const saved = JSON.parse(raw) as Partial<Session>
    if (typeof saved.access !== 'string' || typeof saved.refresh !== 'string') return undefined
    return { access: saved.access, refresh: saved.refresh }
  } catch {
    // 저장소가 막혀 있으면 이번 판에만 로그인입니다. 오류가 아닙니다.
    return undefined
  }
}

function save(next: Session | undefined): void {
  session = next
  try {
    if (next) localStorage.setItem(KEY, JSON.stringify(next))
    else localStorage.removeItem(KEY)
  } catch {
    // 저장하지 못하는 것은 이번 판에만 적용된다는 뜻입니다.
  }
}

export function loggedIn(): boolean {
  return session !== undefined
}

export function forget(): void {
  save(undefined)
}

// ---------------------------------------------------------------------------
// 첫 화면
// ---------------------------------------------------------------------------

/**
 * 켤 때 어느 화면인가.
 *
 * **세션이 있으면 타이틀이고 없으면 로그인 화면입니다.** 「계정 없이 하겠다」를 적어 두고
 * 그다음부터 건너뛰던 것을 걷었습니다 — 온라인 게임의 첫 화면은 로그인 화면이고, 계정
 * 없이 하는 것은 그 화면에서 매번 고르는 것입니다.
 *
 * 계정 없이 시작한 것은 **이번 실행에만** 적용됩니다. 그래서 저장소에 적지 않습니다.
 */
export function needsLogin(): boolean {
  return session === undefined && !guestNow
}

let guestNow = false

/** 이번 실행은 계정 없이 합니다. **적어 두지 않습니다** — 다음에 켜면 다시 묻습니다. */
export function playAsGuest(): void {
  guestNow = true
}

/** 로그아웃했습니다. 다음 화면이 로그인 화면이어야 하므로 손님 표시도 함께 걷습니다. */
export function leaveGuest(): void {
  guestNow = false
}

// ---------------------------------------------------------------------------
// 부르기
// ---------------------------------------------------------------------------

function kindOf(status: number, error: string): FailKind {
  if (status === 401) return 'unauthorized'
  if (error === 'taken') return 'taken'
  if (error === 'cooldown') return 'cooldown'
  if (error === 'shape') return 'shape'
  if (error === 'bad_seed') return 'bad_seed'
  if (error === 'bad_fingerprint') return 'bad_fingerprint'
  if (status === 429) return 'too_many'
  return 'unknown'
}

// ---------------------------------------------------------------------------
// 통신 중
// ---------------------------------------------------------------------------

/**
 * 지금 오가는 요청 수.
 *
 * **화면이 이것을 봅니다.** 통신이 도는 동안 아무 표시가 없으면 사람이 다시 누르고, 다시
 * 누른 것이 두 번째 요청이 됩니다.
 */
let inFlight = 0
const watchers = new Set<(working: boolean) => void>()
const failWatchers = new Set<(error: ApiError) => void>()

/** 통신이 오가는지 알려 달라고 겁니다. 거는 즉시 지금 상태를 한 번 받습니다. */
export function onBusy(watcher: (working: boolean) => void): () => void {
  watchers.add(watcher)
  watcher(inFlight > 0)
  return () => { watchers.delete(watcher) }
}

export function busy(): boolean {
  return inFlight > 0
}

/**
 * 실패를 알려 달라고 겁니다.
 *
 * **부른 자리가 글을 내는 갈래는 여기로 오지 않습니다** — `HANDLED_INLINE` 이 그것들입니다.
 */
export function onFail(watcher: (error: ApiError) => void): () => void {
  failWatchers.add(watcher)
  return () => { failWatchers.delete(watcher) }
}

function reportFail(error: ApiError): void {
  if (HANDLED_INLINE.includes(error.kind)) return
  for (const watcher of failWatchers) watcher(error)
}

function enter(): void {
  inFlight++
  if (inFlight === 1) for (const watcher of watchers) watcher(true)
}

function leave(): void {
  inFlight = Math.max(0, inFlight - 1)
  if (inFlight === 0) for (const watcher of watchers) watcher(false)
}

/**
 * 한 번 부릅니다.
 *
 * `quiet` 를 켜면 실패해도 전역 알림이 뜨지 않습니다. **부른 자리가 그 실패를 화면에
 * 적는 길입니다** — `HANDLED_INLINE` 과 같은 규칙이고, 다만 갈래가 아니라 부르는 자리로
 * 가릅니다. 제공자 목록 조회가 그렇습니다: 서버가 없으면 로그인 화면이 그 사실을 화면
 * 안에 적으므로, 알림까지 뜨면 같은 말이 두 번입니다.
 */
export async function once<T>(path: string, init: RequestInit, token?: string,
                              quiet = false): Promise<T> {
  enter()
  try {
    return await send<T>(path, init, token)
  } catch (error) {
    if (error instanceof ApiError && !quiet) reportFail(error)
    throw error
  } finally {
    leave()
  }
}

async function send<T>(path: string, init: RequestInit, token?: string): Promise<T> {
  let response: Response
  try {
    response = await fetch(`${BASE}${path}`, {
      ...init,
      headers: {
        ...(init.body === undefined ? {} : { 'content-type': 'application/json' }),
        ...(token === undefined ? {} : { authorization: `Bearer ${token}` }),
        ...init.headers,
      },
    })
  } catch {
    // **서버가 없는 것은 오류가 아닙니다.** 게임은 그대로 돌아야 합니다.
    throw new ApiError('offline', 0)
  }

  if (response.ok) return await response.json() as T

  let error = ''
  let message = ''
  try {
    const body = await response.json() as { error?: string; message?: string }
    error = body.error ?? ''
    message = body.message ?? ''
  } catch {
    // 몸통이 JSON 이 아닌 것은 서버 앞단이 낸 응답입니다.
  }
  throw new ApiError(kindOf(response.status, error), response.status, message || error)
}

/**
 * 로그인이 필요한 길.
 *
 * **401 이면 한 번 새로 받아 다시 부릅니다.** access token 이 15분이므로 게임을 켜 두면
 * 반드시 지나가는 자리이고, 여기서 다시 부르지 않으면 사람이 이유 없이 로그아웃됩니다.
 */
export async function call<T>(path: string, init: RequestInit = {}): Promise<T> {
  // **계정이 없어도 지나는 길이 있습니다.** 순위표를 보는 것이 그것입니다 — 그때는
  // 서명 없이 보내고, 서버가 「내 자리」만 빼고 돌려줍니다.
  if (!session) return await once<T>(path, init)

  try {
    return await once<T>(path, init, session.access)
  } catch (error) {
    if (!(error instanceof ApiError) || error.kind !== 'unauthorized') throw error
  }

  let renewed: Session
  try {
    renewed = await once<Session>('/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refresh: session.refresh }),
    })
  } catch (error) {
    // 새로 받지도 못하면 다시 로그인해야 합니다.
    save(undefined)
    throw error
  }

  save(renewed)
  return await once<T>(path, init, renewed.access)
}

// ---------------------------------------------------------------------------
// 로그인
// ---------------------------------------------------------------------------

export interface Provider {
  id: string
  label: string
}

/** 서버가 켜 둔 것들. `dev` 는 개발 서버에서만 참입니다. */
export interface Offered {
  providers: Provider[]
  dev: boolean
}

/**
 * 서버가 켜 둔 제공자. **빌드에 단추가 있는 것만 그립니다.**
 *
 * **실패해도 알림이 뜨지 않습니다.** 로그인 화면이 열릴 때마다 지나는 길이고, 서버가 없는
 * 자리에서는 켤 때마다 「서버에 연결할 수 없습니다」가 떴습니다 — 그 사실은 이 화면 안에
 * 이미 적혀 있고, 계정 없이 하는 사람에게는 알릴 일도 아닙니다.
 */
export async function providers(): Promise<Offered> {
  const body = await once<{ providers?: Provider[]; dev?: boolean }>(
    '/auth/providers', {}, undefined, true)
  return { providers: body.providers ?? [], dev: body.dev === true }
}

/**
 * 개발용 로그인.
 *
 * **배포에서는 이 길이 서버에 없습니다.** 그리고 이것을 부르는 코드는
 * `import.meta.env.DEV` 안에만 있으므로 배포 빌드에 들어가지도 않습니다 — 막는 자리가
 * 둘이고, 하나가 잘못되어도 다른 하나가 남습니다.
 */
export async function devSignIn(handle: string): Promise<void> {
  if (!import.meta.env.DEV) return
  save(await once<Session>('/auth/dev', {
    method: 'POST',
    body: JSON.stringify({ handle }),
  }))
}

/** 제공자로 갑니다. **게임이 다시 뜹니다** — 팝업은 안드로이드에서 막힙니다. */
export function goToProvider(id: string): void {
  location.href = `${BASE}/auth/${id}?return=${encodeURIComponent(returnUrl())}`
}

/**
 * 어디로 돌아올 것인가.
 *
 * **웹과 앱이 다릅니다.** 웹은 지금 보고 있는 주소로 그대로 돌아오지만, 앱에는 그 주소가
 * 없습니다 — 제공자는 시스템 브라우저에서 열리고, 그 브라우저가 우리 앱을 다시 깨울 길이
 * 커스텀 스킴 하나뿐입니다.
 *
 * 서버의 `RETURN_ALLOWLIST` 에 이 값이 들어 있어야 합니다.
 */
export function returnUrl(): string {
  return inApp() ? APP_RETURN : `${location.origin}${location.pathname}`
}

/** 앱 안에서 돌고 있는가. Capacitor 가 자기 표시를 남깁니다. */
function inApp(): boolean {
  const flag = (globalThis as { Capacitor?: { isNativePlatform?: () => boolean } }).Capacitor
  return flag?.isNativePlatform?.() === true
}

/**
 * 되돌아온 주소에서 세션을 받습니다.
 *
 * **주소에 남은 code 를 지웁니다.** 한 번만 쓸 수 있는 값이지만, 남겨 두면 새로 고칠 때마다
 * 실패한 교환이 한 번씩 일어납니다.
 */
export async function claimFromUrl(): Promise<boolean> {
  const hash = location.hash
  const at = hash.indexOf('session=')
  if (at < 0) return false

  const code = hash.slice(at + 'session='.length).split('&')[0]
  history.replaceState(null, '', `${location.pathname}${location.search}`)
  return await claimCode(code)
}

/**
 * 앱이 스킴으로 깨어났을 때.
 *
 * **주소가 아니라 사건으로 옵니다.** 앱은 새로 뜨는 것이 아니라 돌던 것이 앞으로 나오는
 * 것이므로(`launchMode="singleTask"`), 읽을 주소가 없고 대신 열린 주소가 알림으로
 * 옵니다 — 그 안의 code 는 웹에서 오는 것과 같습니다.
 *
 * 걸어 두는 것은 부팅에서 한 번이고, Capacitor 가 없는 곳에서는 아무 일도 하지 않습니다.
 */
export function listenForAppLink(onArrived: () => void): void {
  const app = (globalThis as {
    Capacitor?: { Plugins?: { App?: { addListener?: (
      name: string, run: (event: { url: string }) => void) => void } } }
  }).Capacitor?.Plugins?.App
  if (!app?.addListener) return

  app.addListener('appUrlOpen', event => {
    const at = event.url.indexOf('session=')
    if (at < 0) return
    void claimCode(event.url.slice(at + 'session='.length).split('&')[0])
      .then(got => { if (got) onArrived() })
  })
}

/** code 하나를 세션으로 바꿉니다. 웹과 앱이 같은 길을 지납니다. */
async function claimCode(code: string): Promise<boolean> {
  if (code === '') return false
  try {
    save(await once<Session>('/auth/exchange', {
      method: 'POST',
      body: JSON.stringify({ code, label: deviceLabel() }),
    }))
    return true
  } catch {
    return false
  }
}

/**
 * 앱이 돌아오는 주소.
 *
 * **`AndroidManifest.xml` 의 `intent-filter` 와 같아야 합니다.** 한쪽만 고치면 제공자가
 * 보낸 사람이 아무 데도 도착하지 않습니다.
 */
const APP_RETURN = 'clover://auth'

/** 사람이 자기 기계를 알아보는 표시. **신뢰하지 않습니다** — 표시일 뿐입니다. */
function deviceLabel(): string {
  const agent = navigator.userAgent
  if (/Android/i.test(agent)) return 'Android'
  if (/iPhone|iPad/i.test(agent)) return 'iOS'
  if (/Electron/i.test(agent)) return 'Desktop'
  if (/Mac OS X/i.test(agent)) return 'Mac'
  if (/Windows/i.test(agent)) return 'Windows'
  return 'Web'
}

export async function logout(everywhere = false): Promise<void> {
  try {
    await call('/auth/logout', { method: 'POST', body: JSON.stringify({ everywhere }) })
  } catch {
    // 서버가 없어도 이 기계에서는 로그아웃합니다.
  }
  save(undefined)
}

// ---------------------------------------------------------------------------
// 내 정보 · 보드
// ---------------------------------------------------------------------------

export function me(): Promise<Me> {
  return call<Me>('/me')
}

export function setHandle(handle: string): Promise<{ handle: string }> {
  return call('/me/handle', { method: 'POST', body: JSON.stringify({ handle }) })
}

export function deleteAccount(): Promise<{ deleted: boolean }> {
  return call('/me', { method: 'DELETE' })
}

export function profile(handle: string): Promise<Me> {
  return call<Me>(`/profiles/${encodeURIComponent(handle)}`)
}
