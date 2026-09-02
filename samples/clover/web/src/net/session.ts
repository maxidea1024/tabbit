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
// 계정을 만들지 말지의 결정
// ---------------------------------------------------------------------------

/**
 * 사람이 무엇을 골랐는가.
 *
 * **묻지 않은 것과 「하지 않겠다」는 다릅니다.** 그 둘을 가르지 않으면 로그인 화면이
 * 켤 때마다 뜨거나, 아니면 한 번도 뜨지 않습니다.
 */
export type Mode = 'undecided' | 'single' | 'social'

const MODE_KEY = 'clover.account.mode'

export function mode(): Mode {
  if (session !== undefined) return 'social'
  try {
    return localStorage.getItem(MODE_KEY) === 'single' ? 'single' : 'undecided'
  } catch {
    // 저장소가 막혀 있으면 켤 때마다 묻게 됩니다. 그 브라우저에서는 그것이 맞습니다.
    return 'undecided'
  }
}

/**
 * 로그인 없이 하기로 정했습니다.
 *
 * **되돌릴 수 있습니다.** 타이틀의 계정 단추가 다시 로그인 화면으로 갑니다.
 */
export function chooseSingle(): void {
  try {
    localStorage.setItem(MODE_KEY, 'single')
  } catch {
    // 적어 두지 못하면 다음에 다시 묻습니다.
  }
}

/** 다시 묻습니다. 로그아웃할 때 이것을 함께 부르지 않습니다 — 로그아웃은 싱글플레이입니다. */
export function askAgain(): void {
  try {
    localStorage.removeItem(MODE_KEY)
  } catch {
    // 지우지 못해도 화면은 지금 상태로 갑니다.
  }
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

export async function once<T>(path: string, init: RequestInit, token?: string): Promise<T> {
  enter()
  try {
    return await send<T>(path, init, token)
  } catch (error) {
    if (error instanceof ApiError) reportFail(error)
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
  if (!session) throw new ApiError('unauthorized', 401)

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

/** 서버가 켜 둔 제공자. **빌드에 단추가 있는 것만 그립니다.** */
export async function providers(): Promise<Offered> {
  const body = await once<{ providers?: Provider[]; dev?: boolean }>('/auth/providers', {})
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
  const back = `${location.origin}${location.pathname}`
  location.href = `${BASE}/auth/${id}?return=${encodeURIComponent(back)}`
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
