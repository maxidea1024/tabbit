// 떠오른 글이 화면 안에 온전히 뜨는가.
//
// **눈으로는 확인되지 않습니다.** 글은 0.8초 뒤에 없어지므로 스크린샷은 그 순간을 잡지
// 못하고, 잘린 것은 사람이 그 프레임을 보고 있어야만 보입니다 — 화면이 뜬 자리와 그
// 번쩍임의 크기를 그대로 알리고(`pops`) 여기서 판정합니다.
//
// **자리를 넘겨주는 쪽이 판을 모릅니다.** 조커 줄은 화면 맨 위이고 블라인드 딱지는 왼쪽
// 위 모서리라, 「그 물건의 윗변」을 그대로 쓰면 글의 절반이 화면 밖입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import type { Page } from 'playwright'
import {
  STAGE_H, STAGE_W, clickPrimary, clickSpot, grantJoker, grantMoney, grantTag, heldButton,
  openRun, packSlot, pass, peek, settle, shopBuySpot, shopSlot, skipLogin, takePayout, winRound,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5241

interface Pop { when: string; text: string; x: number; y: number; w: number; h: number }

/** 이 글이 화면 밖으로 나간 변들. 비어 있으면 온전히 들어 있습니다. */
function clipped(one: Pop): string[] {
  const out: string[] = []
  if (one.x - one.w < 0) out.push('왼쪽')
  if (one.y - one.h < 0) out.push('위')
  if (one.x + one.w > STAGE_W) out.push('오른쪽')
  if (one.y + one.h > STAGE_H) out.push('아래')
  return out
}

/**
 * 화면이 알린 글 목록을 훑어 새로 뜬 것만 모읍니다.
 *
 * **글로 짚습니다.** 목록은 24개까지만 남으므로 번호로 짚으면 그 사이에 밀려나간 것을
 * 놓치고, 같은 글이 두 번 뜨는 것은 자리가 다르면 다른 표본입니다.
 */
class Watch {
  private seen = new Set<string>()
  readonly all: Pop[] = []
  constructor(private readonly page: Page) {}

  async take(when: string): Promise<void> {
    for (const [text, x, y, w, h] of (await peek(this.page)).pops ?? []) {
      const key = `${text}@${x},${y}`
      if (this.seen.has(key)) continue
      this.seen.add(key)
      this.all.push({ when, text, x, y, w, h })
    }
  }
}

async function main(): Promise<number> {
  // **다시 읽어 들이는 것을 끕니다.** 이 도구는 한 판을 끝까지 두므로, 그 사이에 누군가
  // 코드를 고치면 화면이 새로 열리고 도구는 「없어진 화면에게 물었다」 로 끝납니다.
  const server = await createServer({
    root: path.resolve(HERE, '..'), server: { port: PORT, hmr: false },
  })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: STAGE_W, height: STAGE_H } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-POP1&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  const watch = new Watch(page)

  // 1. 스몰과 빅을 넘어 보스까지 갑니다. 카드와 조커가 낸 값이 그동안 뜹니다.
  await grantJoker(page, 3)
  await settle(page)
  // **`openRun` 이 이미 스몰을 골라 두었습니다.** 그 판에서 시작합니다.
  for (const which of ['스몰', '빅'] as const) {
    await winRound(page)
    await watch.take(`${which}을 깹니다`)
    await grantMoney(page, 400)
    await settle(page)
    await pass(page, 600)
    if (which === '스몰') await shopErrands(page, watch)
    await clickSpot(page, 'nextBlind')
    await settle(page)
    await pass(page, 1200)
    if (which === '스몰') {
      await clickPrimary(page)
      await settle(page)
      await pass(page, 800)
    }
  }

  // 2. 보스를 깹니다.
  //
  // **덱 · 바우처 · 보스 · 태그가 낸 값은 블라인드 딱지에서 뜹니다.** 그 딱지는 화면의
  // 왼쪽 위 모서리라 이 도구의 요점이고, 보스를 깰 때 발동하는 `investment` 가 그 길을
  // 확실히 지납니다 — 건너뛰어 받는 태그는 무엇이 올지 시드가 정하므로 짚을 수 없습니다.
  await atBlindPick(page)
  await grantTag(page, 'investment')
  await clickSpot(page, 'pick')
  await settle(page)
  await pass(page, 800)
  await winRound(page)
  await watch.take('보스를 깹니다')

  let bad = 0
  for (const one of watch.all) {
    const out = clipped(one)
    if (out.length > 0) bad++
    console.log(`${out.length > 0 ? '←' : '  '} ${one.when} · ${one.text}`
      + ` · (${one.x}, ${one.y}) ±(${one.w}, ${one.h})`
      + (out.length > 0 ? ` · ${out.join('·')} 밖` : ''))
  }
  console.log('표본', watch.all.length, '· 잘린 것', bad)
  await browser.close()
  await server.close()
  return watch.all.length > 0 && bad === 0 ? 0 : 1
}

/**
 * 블라인드를 고르는 자리에 설 때까지 나아갑니다.
 *
 * **국면이 `shop` 인 것과 상점 판이 떠 있는 것과 고르는 자리에 놓인 것이 다 다릅니다.**
 * 셋 중 어디에 있는지를 보고 한 걸음씩 나아갑니다.
 */
async function atBlindPick(page: Page): Promise<void> {
  for (let step = 0; step < 20; step++) {
    const now = await peek(page)
    if (now.spots?.pick) return
    if (now.payout || now.cleared) { await takePayout(page); continue }
    if (now.spots?.nextBlind) {
      await clickSpot(page, 'nextBlind')
      await settle(page)
    }
    await pass(page, 400)
  }
  throw new Error('블라인드를 고르는 자리에 서지 못했습니다')
}

/** 상점에서 사고 팔고 뜯습니다. **값이 그 물건에서 뜨는 자리들입니다.** */
async function shopErrands(page: Page, watch: Watch): Promise<void> {
  for (const [what, want] of [['조커를 삽니다', 1], ['소모품을 삽니다', 2]] as const) {
    const kinds = (await peek(page)).shopKinds ?? []
    const slot = want === 1 ? kinds.indexOf(1) : kinds.findIndex(kind => kind >= 2)
    if (slot < 0) continue
    const tile = await shopSlot(page, slot)
    await page.mouse.click(tile.x, tile.y)
    await pass(page, 350)
    const buy = await shopBuySpot(page)
    await page.mouse.click(buy.x, buy.y)
    await pass(page, 2400)
    await watch.take(what)
  }

  // 바우처. **산 자리에서 이름이 뜨는 것이 그것을 얻었다는 유일한 표시입니다.**
  const voucherRow = (await peek(page)).shopRows?.voucher
  if (voucherRow !== undefined) {
    await page.mouse.click(STAGE_W / 2 + 254, voucherRow + 74)
    await pass(page, 1600)
    await watch.take('바우처를 삽니다')
  }

  // 조커 하나를 팝니다. **판 값은 내놓은 그 자리에서 뜹니다.**
  const tray = (await peek(page)).trayCards?.joker?.[0]
  if (tray) {
    await page.mouse.click(tray.x + tray.width / 2, tray.y + tray.height / 2)
    await pass(page, 400)
    const sell = await heldButton(page).catch(() => undefined)
    if (sell) {
      await page.mouse.click(sell.x, sell.y)
      await pass(page, 400)
      const yes = (await peek(page)).spots?.['confirm:yes']
      if (yes) await page.mouse.click(yes.x, yes.y)
      await pass(page, 2400)
      await watch.take('조커를 팝니다')
    }
  }

  // 팩 하나를 뜯고 한 장 집습니다.
  const packs = (await peek(page)).packIds ?? []
  if (packs.length === 0) return
  const tile = await packSlot(page, 0, packs.length)
  await page.mouse.click(tile.x, tile.y)
  await pass(page, 350)
  const buy = await shopBuySpot(page)
  await page.mouse.click(buy.x, buy.y)
  await pass(page, 3200)
  await watch.take('팩을 뜯습니다')
  if (((await peek(page)).packCards ?? 0) > 0) {
    await page.mouse.click(STAGE_W / 2, 360)
    await pass(page, 400)
    const take = await heldButton(page).catch(() => undefined)
    if (take) {
      await page.mouse.click(take.x, take.y)
      await pass(page, 3000)
      await watch.take('팩에서 집습니다')
    }
  }
  if ((await peek(page)).spots?.packSkip) {
    await clickSpot(page, 'packSkip')
    await pass(page, 1200)
  }
}

main().then(code => process.exit(code))
