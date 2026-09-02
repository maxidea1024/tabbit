// 환경변수.
//
// **확인이 부팅에 있습니다.** 값 하나가 잘못되어 새는 것을 요청 처리 중에 발견하면, 그때는
// 이미 새어 있습니다 — 특히 `AUTH_PROVIDERS` 가 그렇습니다.

/** 배포에 나가서는 안 되는 제공자. 시험용이므로 아무나 계정을 만듭니다. */
const DEV_ONLY_PROVIDERS = ['github']

/** 우리가 아는 제공자 전부. */
export const KNOWN_PROVIDERS = ['google', 'discord', 'apple', 'github'] as const

export type Provider = (typeof KNOWN_PROVIDERS)[number]

export interface Env {
  port: number
  production: boolean
  databaseUrl: string
  redisUrl: string
  publicUrl: string
  /** 로그인을 마치고 돌아갈 수 있는 주소. 여기 없는 곳으로는 보내지 않습니다. */
  returnAllowlist: string[]
  providers: Provider[]
  /** access token 을 서명하는 열쇠. */
  jwtSecret: string
  /** 데이터가 있는 곳. 웹의 것을 그대로 읽습니다. */
  dataPath: string
  /** 구워 둔 리플레이가 있는 곳. 지문을 여기서 냅니다. */
  replayPath: string
}

export function readEnv(source: NodeJS.ProcessEnv = process.env): Env {
  const production = source.NODE_ENV === 'production'
  const providers = (source.AUTH_PROVIDERS ?? 'github')
    .split(',').map(name => name.trim().toLowerCase()).filter(name => name !== '')

  for (const name of providers) {
    if (!KNOWN_PROVIDERS.includes(name as Provider)) {
      throw new Error(`AUTH_PROVIDERS 에 모르는 제공자가 있습니다: ${name}`)
    }
    // **배포에서 개발용 제공자를 켜면 부팅하지 않습니다.** 환경변수 하나로 새는 것이므로
    // 요청이 오기 전에 멈춥니다.
    if (production && DEV_ONLY_PROVIDERS.includes(name)) {
      throw new Error(`${name} 은 개발 환경 전용입니다. NODE_ENV=production 에서는 켤 수 없습니다`)
    }
  }
  if (providers.length === 0) throw new Error('AUTH_PROVIDERS 가 비어 있습니다')

  const secret = source.JWT_SECRET ?? (production ? '' : 'clover-dev-secret')
  if (secret === '') throw new Error('JWT_SECRET 이 없습니다')

  return {
    port: Number(source.PORT ?? 8787),
    production,
    databaseUrl: source.DATABASE_URL ?? 'postgres://clover:clover@localhost:55432/clover',
    redisUrl: source.REDIS_URL ?? 'redis://localhost:56379',
    publicUrl: source.PUBLIC_URL ?? 'http://localhost:8787',
    returnAllowlist: (source.RETURN_ALLOWLIST ?? 'http://localhost:5173')
      .split(',').map(one => one.trim()).filter(one => one !== ''),
    providers: providers as Provider[],
    jwtSecret: secret,
    dataPath: source.DATA_PATH ?? '../web/public/data',
    replayPath: source.REPLAY_PATH ?? '../design-data/out/replay',
  }
}
