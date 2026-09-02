// 소셜 로그인 제공자.
//
// **넷 다 OAuth 2.0 authorization code 입니다.** 다른 것은 어디에 물어보고 무엇을 신원으로
// 삼는가뿐이므로, 그 셋만 표로 두고 흐름은 하나입니다.
//
// Apple 만 `client_secret` 이 고정 문자열이 아니라 **우리가 서명하는 JWT** 입니다.

import jwt from 'jsonwebtoken'
import type { Provider } from '../env'

export interface ProviderConfig {
  authorizeUrl: string
  tokenUrl: string
  scope: string
  /** 신원을 어디에서 얻습니까. `id_token` 이면 token 응답 안에 있습니다. */
  subjectFrom: 'userinfo' | 'id_token'
  userinfoUrl?: string
  /** `userinfo` 응답에서 신원이 되는 칸. */
  userinfoField?: string
  /** 사람이 창에서 보는 이름. */
  label: string
}

export const PROVIDERS: Record<Provider, ProviderConfig> = {
  github: {
    authorizeUrl: 'https://github.com/login/oauth/authorize',
    tokenUrl: 'https://github.com/login/oauth/access_token',
    scope: 'read:user',
    subjectFrom: 'userinfo',
    userinfoUrl: 'https://api.github.com/user',
    userinfoField: 'id',
    label: 'GitHub',
  },
  google: {
    authorizeUrl: 'https://accounts.google.com/o/oauth2/v2/auth',
    tokenUrl: 'https://oauth2.googleapis.com/token',
    scope: 'openid',
    subjectFrom: 'id_token',
    label: 'Google',
  },
  discord: {
    authorizeUrl: 'https://discord.com/oauth2/authorize',
    tokenUrl: 'https://discord.com/api/oauth2/token',
    scope: 'identify',
    subjectFrom: 'userinfo',
    userinfoUrl: 'https://discord.com/api/users/@me',
    userinfoField: 'id',
    label: 'Discord',
  },
  apple: {
    authorizeUrl: 'https://appleid.apple.com/auth/authorize',
    tokenUrl: 'https://appleid.apple.com/auth/token',
    scope: 'openid',
    subjectFrom: 'id_token',
    label: 'Apple',
  },
}

export interface Secrets {
  clientId: string
  clientSecret: string
}

/**
 * 이 제공자의 열쇠.
 *
 * **Apple 의 `client_secret` 은 그때그때 서명합니다.** 고정 문자열이 아니라 팀과 열쇠로
 * 만든 짧은 JWT 이고, 최대 6개월이지만 요청마다 새로 만드는 편이 만료를 관리하지 않아도
 * 되므로 간단합니다.
 */
export function secretsOf(provider: Provider,
                          source: NodeJS.ProcessEnv = process.env): Secrets {
  const upper = provider.toUpperCase()
  const clientId = source[`${upper}_CLIENT_ID`] ?? ''
  if (clientId === '') throw new Error(`${upper}_CLIENT_ID 가 없습니다`)

  if (provider !== 'apple') {
    const clientSecret = source[`${upper}_CLIENT_SECRET`] ?? ''
    if (clientSecret === '') throw new Error(`${upper}_CLIENT_SECRET 이 없습니다`)
    return { clientId, clientSecret }
  }

  const teamId = source.APPLE_TEAM_ID ?? ''
  const keyId = source.APPLE_KEY_ID ?? ''
  const privateKey = (source.APPLE_PRIVATE_KEY ?? '').replace(/\n/g, '\n')
  if (teamId === '' || keyId === '' || privateKey === '') {
    throw new Error('APPLE_TEAM_ID · APPLE_KEY_ID · APPLE_PRIVATE_KEY 가 있어야 합니다')
  }

  const clientSecret = jwt.sign({}, privateKey, {
    algorithm: 'ES256',
    keyid: keyId,
    issuer: teamId,
    audience: 'https://appleid.apple.com',
    subject: clientId,
    expiresIn: '10m',
  })
  return { clientId, clientSecret }
}

export function authorizeUrl(provider: Provider, secrets: Secrets,
                             redirectUri: string, state: string): string {
  const config = PROVIDERS[provider]
  const query = new URLSearchParams({
    client_id: secrets.clientId,
    redirect_uri: redirectUri,
    response_type: 'code',
    scope: config.scope,
    state,
  })
  // **Apple 은 `response_mode=form_post` 를 요구합니다** — `openid` 를 요청하면 그렇습니다.
  if (provider === 'apple') query.set('response_mode', 'form_post')
  return `${config.authorizeUrl}?${query.toString()}`
}

/**
 * code 를 신원으로 바꿉니다.
 *
 * **돌려주는 것은 제공자 안에서의 식별자 하나입니다.** 이름도 이메일도 받지 않습니다.
 */
export async function subjectOf(provider: Provider, secrets: Secrets,
                                redirectUri: string, code: string): Promise<string> {
  const config = PROVIDERS[provider]

  const response = await fetch(config.tokenUrl, {
    method: 'POST',
    headers: { 'content-type': 'application/x-www-form-urlencoded', accept: 'application/json' },
    body: new URLSearchParams({
      client_id: secrets.clientId,
      client_secret: secrets.clientSecret,
      code,
      grant_type: 'authorization_code',
      redirect_uri: redirectUri,
    }),
  })
  if (!response.ok) throw new Error(`${provider} 의 token 요청이 ${response.status} 입니다`)

  const token = await response.json() as { access_token?: string; id_token?: string }

  if (config.subjectFrom === 'id_token') {
    // **서명을 확인하지 않습니다.** 우리가 방금 제공자에게 직접 물어 받은 응답이고, 그
    // 연결이 TLS 입니다 — 남이 끼어들 자리가 없습니다.
    const claims = jwt.decode(token.id_token ?? '') as { sub?: string } | null
    if (!claims?.sub) throw new Error(`${provider} 가 id_token 을 주지 않았습니다`)
    return claims.sub
  }

  const info = await fetch(config.userinfoUrl as string, {
    headers: {
      authorization: `Bearer ${token.access_token ?? ''}`,
      accept: 'application/json',
      // GitHub 이 요구합니다.
      'user-agent': 'clover-leaderboard',
    },
  })
  if (!info.ok) throw new Error(`${provider} 의 신원 조회가 ${info.status} 입니다`)

  const body = await info.json() as Record<string, unknown>
  const subject = body[config.userinfoField as string]
  if (subject === undefined || subject === null) {
    throw new Error(`${provider} 의 응답에 ${config.userinfoField} 가 없습니다`)
  }
  return String(subject)
}
