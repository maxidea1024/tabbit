// 전환이 시작되는 자리에서 구운 화면이 살아 있는 화면과 같은가.
//
// **눈으로는 놓칩니다.** 지워짐이 5%인 한 프레임은 사람이 보기에 「아직 그대로」이고, 그
// 프레임에 구멍이 몇십 개 뚫려 있어도 다음 프레임이 곧 덮습니다 — 실제로 그렇게 지나갔고,
// 핸드폰에서 상점 카드가 검은 구멍으로 보인다는 말을 듣고서야 찾았습니다.
//
// 그래서 **두 그림을 픽셀로 견줍니다.** 지워짐이 얼마 안 된 프레임에서 구운 화면은 살아 있는
// 화면과 거의 같아야 하고, 크게 다른 픽셀이 많으면 그 자리에 무언가가 빠졌거나 뚫린 것입니다.
//
// **핸드폰 몫도 봅니다.** 손가락으로 짚는 화면이면 다른 셰이더가 돌고 그림도 다른 것을
// 읽으므로, 데스크탑에서 멀쩡한 것이 그쪽에서 뚫릴 수 있습니다 — 실제로 그랬습니다.
//
//     npx tsx tools/check-shot.ts
//
// **실제 GPU 로 띄웁니다.** 소프트웨어 그리기는 스텐실과 렌더 타깃을 다르게 다룹니다.

import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  clearBlind, crossed, grantJoker, openRun, pass, peek, settle, shopStanding, skipLogin,
  takePayout,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/shot')
const PORT = 5297
/** 얼마나 지워진 프레임을 견주는가. **이만큼은 눈에 「그대로」입니다.** */
const EARLY = 0.05
/** 두 그림에서 이만큼 다르면 다른 픽셀로 셉니다. 0..255 */
const APART = 30
/**
 * 크게 다른 픽셀이 몇 개까지 괜찮은가. 화면 픽셀의 몫입니다.
 *
 * **0이 아닙니다.** 지워짐이 5%면 재의 첫 알갱이가 이미 몇 개 떠 있고, 판이 그동안에도
 * 움직입니다. 잰 값은 멀쩡한 화면이 0.0~0.1%이고, 금이 뚫던 때의 핸드폰 몫이 1.6%였습니다.
 */
const ALLOW = 0.004

const problems: string[] = []

async function grab(page: Page, file: string): Promise<string> {
  const buffer = await page.screenshot({ path: file })
  return buffer.toString('base64')
}

/**
 * 크게 다른 자리의 몫. **덩어리만 셉니다.**
 *
 * 구운 그림은 배율 2로 굽고 다시 늘려 그리므로 **글자와 테두리의 경계가 1픽셀씩 다릅니다** —
 * 그것을 그대로 세면 멀쩡한 화면도 2~3%가 나오고, 찾으려는 구멍은 그 안에 묻힙니다. 그래서
 * 이웃까지 다른 자리만 셉니다: 구멍은 덩어리이고 경계는 선입니다.
 *
 * **브라우저가 셉니다.** PNG 를 풀어야 하고, 그것을 Node 에서 하려면 꾸러미가 하나
 * 늘어납니다 — 브라우저에는 그 해독기가 이미 있습니다.
 */
async function apart(page: Page, a: string, b: string): Promise<number> {
  return await page.evaluate(`(async () => {
    const load = async src => {
      const blob = await (await fetch('data:image/png;base64,' + src)).blob()
      return await createImageBitmap(blob)
    }
    const pull = async src => {
      const bitmap = await load(src)
      const board = new OffscreenCanvas(bitmap.width, bitmap.height)
      board.getContext('2d').drawImage(bitmap, 0, 0)
      return board.getContext('2d').getImageData(0, 0, bitmap.width, bitmap.height)
    }
    const one = await pull(${JSON.stringify(a)})
    const two = await pull(${JSON.stringify(b)})
    if (one.width !== two.width || one.height !== two.height) return 1
    const w = one.width, h = one.height
    const far = new Uint8Array(w * h)
    for (let p = 0; p < w * h; p++) {
      const i = p * 4
      const gap = (Math.abs(one.data[i] - two.data[i])
        + Math.abs(one.data[i + 1] - two.data[i + 1])
        + Math.abs(one.data[i + 2] - two.data[i + 2])) / 3
      far[p] = gap > ${APART} ? 1 : 0
    }
    // 이웃까지 다른 자리만. **선은 빠지고 덩어리만 남습니다.**
    const R = 3
    let many = 0
    for (let y = R; y < h - R; y++) {
      for (let x = R; x < w - R; x++) {
        const p = y * w + x
        if (!far[p]) continue
        if (far[p - R] && far[p + R] && far[p - R * w] && far[p + R * w]) many++
      }
    }
    return many / (w * h)
  })()`) as number
}

/**
 * 판을 상점까지 몰고 가서 두 그림을 찍습니다.
 *
 * **상점과 조커를 함께 세웁니다.** 둘 다 마스크로 잘리는 것이고, 굽는 자리에서 빠지던 것이
 * 그것들이었습니다.
 */
async function measure(browser: Browser, lite: boolean, url: string): Promise<void> {
  const tag = lite ? '핸드폰 몫' : '데스크탑'
  const page = await browser.newPage(lite
    ? { viewport: { width: 900, height: 420 }, hasTouch: true, isMobile: true,
        deviceScaleFactor: 2 }
    : { viewport: { width: 1280, height: 800 } })
  page.on('pageerror', one => problems.push(`${tag}: ${one.message}`))
  await skipLogin(page)
  await page.goto(url, { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await crossed(page)
  await openRun(page)
  await grantJoker(page, 3)
  await pass(page, 1200)
  await settle(page)
  await clearBlind(page)
  await takePayout(page)
  await shopStanding(page)
  // **진열이 끝나기를 기다립니다.** 물건은 알파 0에서 서서히 뜨므로, 진열 중에 찍으면
  // 구운 화면이 아니라 그 순간이 빈 것입니다.
  await pass(page, 3500)
  const seen = await peek(page)
  const items = (seen.shopKinds ?? []).length

  const live = await grab(page, path.join(OUT, `${lite ? 'lite' : 'desk'}-live.png`))
  await page.evaluate(`window.__clover.cross('run_lost')`)
  let shot: string | undefined
  let cover = 0
  for (let i = 0; i < 300; i++) {
    const now = (await peek(page)).transition
    if (now && now.stage === 'out' && now.cover >= EARLY) {
      cover = now.cover
      shot = await grab(page, path.join(OUT, `${lite ? 'lite' : 'desk'}-shot.png`))
      break
    }
    await pass(page, 16)
  }
  await crossed(page)

  if (!shot) {
    await page.close()
    problems.push(`${tag}: 전환이 ${Math.round(EARLY * 100)}% 를 지나지 않았습니다`)
    return
  }
  const share = await apart(page, live, shot)
  await page.close()
  const ok = share <= ALLOW
  console.log(`${tag} · 상점 ${items}칸 · 조커 ${seen.jokers}개 · 지워짐 `
    + `${Math.round(cover * 100)}% · 다른 픽셀 ${(share * 100).toFixed(2)}% ${ok ? '' : '←'}`)
  if (!ok) {
    problems.push(`${tag}: 구운 화면이 살아 있는 화면과 다릅니다 — `
      + `다른 픽셀 ${(share * 100).toFixed(2)}% (${(ALLOW * 100).toFixed(1)}% 까지)`)
  }
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  await fs.mkdir(OUT, { recursive: true })
  const browser = await chromium.launch({ headless: false })
  const url = `http://localhost:${PORT}/?seed=CLOVER-SHOT`
  for (const lite of [false, true]) await measure(browser, lite, url)
  await browser.close()
  await server.close()
  console.log(problems.length === 0
    ? '\n전환이 시작되는 자리에서 구운 화면이 살아 있는 화면과 같습니다'
    : '\n' + problems.join('\n'))
  return problems.length === 0 ? 0 : 1
}

void main().then(code => process.exit(code))
