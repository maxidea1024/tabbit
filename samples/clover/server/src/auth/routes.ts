// 로그인의 길.
//
// **팝업을 쓰지 않습니다.** 안드로이드에서 팝업이 막히고 데스크탑에서 팝업이 다른 창입니다.
// redirect 는 셋에서 같고, 게임의 부팅은 2초 안입니다.
//
// 되돌아올 때 붙는 것이 token 이 아니라 **한 번 쓰는 code** 인 것은 주소가 기록에 남기
// 때문입니다.

import * as crypto from 'crypto'
import { Router } from 'express'
import { accountFor } from '../accounts'
import { KEY } from '../redis'
import { fail, guard, requireLogin, type Context } from '../http'
import { PROVIDERS, authorizeUrl, secretsOf, subjectOf } from './providers'
import { endAllSessions, endSession, rotate, startSession } from './session'
import type { Provider } from '../env'

/** 로그인을 시작하고 마칠 때까지의 시간. */
const STATE_SECONDS = 600

/** 되돌아온 뒤 code 를 바꿀 수 있는 시간. */
const CODE_SECONDS = 60

export function authRouter(context: Context): Router {
  const router = Router()
  const { env, db, cache } = context

  const enabled = (name: string): name is Provider =>
    (env.providers as string[]).includes(name)

  const redirectUri = (provider: string) => `${env.publicUrl}/auth/${provider}/callback`

  router.get('/auth/providers', (_req, res) => {
    // **켜져 있는 것만 알립니다.** 클라이언트는 이것과 자기 빌드의 목록이 겹치는 것만
    // 그립니다 — GitHub 단추는 배포 빌드에 아예 없습니다.
    res.json({
      providers: env.providers.map(name => ({ id: name, label: PROVIDERS[name].label })),
    })
  })

  router.get('/auth/:provider', guard(async (req, res) => {
    const provider = String(req.params.provider)
    if (!enabled(provider)) {
      fail(res, 404, 'unknown_provider', '켜져 있지 않은 제공자입니다')
      return
    }

    const back = String(req.query.return ?? env.returnAllowlist[0] ?? '')
    if (!env.returnAllowlist.some(one => back === one || back.startsWith(`${one}/`))) {
      // **허용 목록 밖으로는 보내지 않습니다.** 여기가 열려 있으면 남의 주소로 code 를
      // 실어 보내는 길이 됩니다.
      fail(res, 400, 'bad_return', '돌아갈 주소가 허용 목록에 없습니다')
      return
    }

    const state = crypto.randomBytes(16).toString('base64url')
    await cache.set(KEY.code(`state:${state}`), JSON.stringify({ provider, back }),
                    'EX', STATE_SECONDS)

    res.redirect(authorizeUrl(provider, secretsOf(provider), redirectUri(provider), state))
  }))

  const callback = guard(async (req, res) => {
    const provider = String(req.params.provider)
    const source = req.method === 'POST' ? req.body : req.query
    const code = String(source?.code ?? '')
    const state = String(source?.state ?? '')

    if (!enabled(provider) || code === '' || state === '') {
      fail(res, 400, 'bad_callback', '되돌아온 값이 모자랍니다')
      return
    }

    const raw = await cache.getdel(KEY.code(`state:${state}`))
    if (!raw) {
      fail(res, 400, 'bad_state', '시작한 기록이 없거나 시간이 지났습니다')
      return
    }
    const started = JSON.parse(raw) as { provider: string; back: string }
    if (started.provider !== provider) {
      fail(res, 400, 'bad_state', '시작한 제공자와 다릅니다')
      return
    }

    const subject = await subjectOf(provider, secretsOf(provider), redirectUri(provider), code)
    const accountId = await accountFor(db, provider, subject)

    // **한 번 쓰는 code 로 바꿔 보냅니다.** 주소는 기록에 남으므로 token 을 실어 보내지
    // 않습니다.
    const handoff = crypto.randomBytes(24).toString('base64url')
    await cache.set(KEY.code(handoff), String(accountId), 'EX', CODE_SECONDS)

    res.redirect(`${started.back}#session=${handoff}`)
  })

  router.get('/auth/:provider/callback', callback)
  // Apple 은 `form_post` 로 돌려줍니다.
  router.post('/auth/:provider/callback', callback)

  router.post('/auth/exchange', guard(async (req, res) => {
    const handoff = String(req.body?.code ?? '')
    const label = String(req.body?.label ?? '').slice(0, 40)

    const accountId = handoff === '' ? null : await cache.getdel(KEY.code(handoff))
    if (!accountId) {
      fail(res, 400, 'bad_code', '쓸 수 없는 code 입니다')
      return
    }

    res.json(await startSession(db, env.jwtSecret, Number(accountId), label))
  }))

  router.post('/auth/refresh', guard(async (req, res) => {
    const given = String(req.body?.refresh ?? '')
    const tokens = given === '' ? undefined : await rotate(db, env.jwtSecret, given)
    if (!tokens) {
      fail(res, 401, 'bad_refresh', '다시 로그인해야 합니다')
      return
    }
    res.json(tokens)
  }))

  router.post('/auth/logout', requireLogin(context), guard(async (req, res) => {
    // **이 기계만 로그아웃합니다.** 다른 기계의 세션은 그대로입니다.
    if (req.body?.everywhere === true) {
      const count = await endAllSessions(db, req.accountId as number)
      res.json({ ended: count })
      return
    }
    await endSession(db, req.sessionId as number)
    res.json({ ended: 1 })
  }))

  return router
}
