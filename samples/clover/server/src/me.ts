// 내 정보.
//
// **`/me` 와 `/profiles/{handle}` 이 같은 모양을 돌려줍니다.** 내 것에 관리 단추가 붙는
// 것 말고는 판이 하나이고, 판을 둘로 만들면 한쪽만 고쳐지는 날이 옵니다.

import { Router } from 'express'
import { deleteAccount, profileByHandle, profileOf, providersOf, setHandle } from './accounts'
import { fail, guard, requireLogin, type Context } from './http'
import { listSessions } from './auth/session'
import { ranksOf } from './boards'

export function meRouter(context: Context): Router {
  const router = Router()
  const { db } = context

  router.get('/me', requireLogin(context), guard(async (req, res) => {
    const accountId = req.accountId as number
    const profile = await profileOf(db, accountId)
    if (!profile) {
      fail(res, 404, 'not_found', '없는 계정입니다')
      return
    }
    res.json({
      handle: profile.handle,
      tier: profile.tier,
      lastSeasonTier: profile.lastSeasonTier,
      // **기계 목록이 여기 있습니다.** 한 계정이 여러 기계에서 동시에 로그인하므로,
      // 어디에 들어와 있는지를 사람이 볼 수 있어야 합니다.
      devices: await listSessions(db, accountId),
      // **무엇으로 로그인해 두었는가.** 계정 자리에 그 표시가 있어야 다음에 어느 단추를
      // 눌러야 하는지 압니다.
      providers: await providersOf(db, accountId),
      ranks: await ranksOf(context, accountId),
    })
  }))

  router.post('/me/handle', requireLogin(context), guard(async (req, res) => {
    const handle = String(req.body?.handle ?? '')
    const result = await setHandle(db, req.accountId as number, handle)
    if (result === 'ok') {
      res.json({ handle })
      return
    }
    const status = result === 'taken' ? 409 : result === 'cooldown' ? 429 : 400
    fail(res, status, result)
  }))

  router.delete('/me', requireLogin(context), guard(async (req, res) => {
    // **되돌리지 않습니다.** 순위표에서 그 자리가 빠지고 아래가 한 칸씩 올라옵니다.
    await deleteAccount(db, req.accountId as number)
    res.json({ deleted: true })
  }))

  router.get('/profiles/:handle', requireLogin(context), guard(async (req, res) => {
    const profile = await profileByHandle(db, String(req.params.handle))
    if (!profile) {
      fail(res, 404, 'not_found', '없는 이름입니다')
      return
    }
    res.json({
      handle: profile.handle,
      tier: profile.tier,
      lastSeasonTier: profile.lastSeasonTier,
      ranks: await ranksOf(context, profile.accountId),
    })
  }))

  return router
}
