// 껍데기와 자원.
//
// 손전화에서 앱을 죽이거나 배터리를 쓰던 자리 넷을 봅니다. **화면을 보는 도구가 아니라
// 값을 보는 도구입니다** — 넷 다 눈에 보이지 않는 자리이고, 눈으로 보아 알 수 있었으면
// 진작 고쳤을 것입니다.
//
// |보는 것|왜|
// |--|--|
// |배경음이 원소로 납니다|풀어 두면 세 곡이 81MB 입니다. 원소로 바꾸었으므로 그 원소가 실제로 도는지를 봅니다|
// |그림이 상한 안에 있습니다|목록을 끝까지 굴려도 들고 있는 것이 상한을 넘지 않아야 합니다|
// |뒤로 가기가 판을 닫습니다|`ESC` 와 같은 자리로 보냈으므로 `ESC` 로 확인합니다|
// |물러나면 멈춥니다|`visibilitychange` 로 흉내 냅니다. 앱에서는 Capacitor 가 같은 자리를 부릅니다|
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { peek, pressTitle, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5213
/** `art.ts` 의 상한과 같은 값입니다. */
const BUDGET = 96 * 1024 * 1024

let failed = false

function ok(what: string, pass: boolean, note = ''): void {
  console.log(`  ${pass ? '✓' : '✗'} ${what}${note ? `  — ${note}` : ''}`)
  if (!pass) failed = true
}

async function main(): Promise<void> {
  const server = await createServer({ root: path.join(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })

  const errors: string[] = []
  page.on('pageerror', error => errors.push(String(error)))

  // **주소를 열기 전에 겁니다.** `addInitScript` 는 다음에 여는 쪽에만 걸립니다.
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=shell`)
  await page.waitForFunction(() => (window as { __clover?: unknown }).__clover !== undefined,
                             undefined, { timeout: 30_000 })

  // ---- 배경음. 타이틀의 빈 자리를 한 번 누르면 소리 길이 열립니다.
  await page.mouse.click(60, 60)
  await page.waitForTimeout(1500)

  const music = await report(page)
  ok('배경음이 원소 셋으로 있습니다', music.tracks.length === 3, `${music.tracks.length}개`)
  const playing = music.tracks.filter(one => one.playing)
  ok('그중 하나만 돕니다', playing.length === 1,
     playing.map(one => one.name).join(' · ') || '없음')
  ok('그 곡이 흐릅니다', playing.length === 1 && playing[0].at > 0,
     `${playing[0]?.name ?? '-'} ${playing[0]?.at.toFixed(2) ?? '-'}초`)

  // ---- 뒤로 가기. `ESC` 와 같은 자리이므로 `ESC` 로 봅니다.
  await pressTitle(page, 'options')
  await page.waitForTimeout(600)
  ok('판이 떴습니다', (await peek(page)).modalUp === true)
  await page.keyboard.press('Escape')
  await page.waitForTimeout(500)
  ok('뒤로가 판을 닫습니다', (await peek(page)).modalUp === false)

  // ---- 물러나면 멈춥니다.
  const before = await frames(page)
  await hide(page, true)
  await page.waitForTimeout(700)
  const paused = await frames(page)
  await page.waitForTimeout(700)
  ok('물러나면 그리지 않습니다', (await frames(page)) === paused, `${paused - before}프레임`)
  ok('물러나면 소리도 멈춥니다', (await report(page)).tracks.every(one => !one.playing))

  await hide(page, false)
  await page.waitForTimeout(700)
  ok('돌아오면 다시 그립니다', (await frames(page)) > paused)

  // ---- 그림. 도감을 끝까지 굴립니다. **여기가 그림을 가장 많이 부르는 자리입니다.**
  //
  // **밝힌 것으로 적어 두고 다시 엽니다.** 도감은 아직 만나지 않은 것에 그림을 부르지
  // 않으므로, 새 판으로 굴리면 부르는 것이 열 몇 장뿐입니다 — 오래 한 사람의 도감이
  // 이 도구가 보아야 하는 자리입니다.
  await page.evaluate(async () => {
    const list = await (await fetch('./art/index.json')).json() as string[]
    const jokers = list.filter(one => one.startsWith('joker/'))
      .map(one => one.slice('joker/'.length))
    localStorage.setItem('clover.collection', JSON.stringify({ joker: jokers }))
  })
  await page.reload()
  await page.waitForFunction(() => (window as { __clover?: unknown }).__clover !== undefined,
                             undefined, { timeout: 30_000 })
  await pressTitle(page, 'collection')
  await page.waitForTimeout(800)
  for (let i = 0; i < 80; i++) {
    await page.mouse.move(640, 420)
    await page.mouse.wheel(0, 1200)
    await page.waitForTimeout(70)
  }
  await page.waitForTimeout(1500)
  const bytes = await page.evaluate(() =>
    (window as { __clover?: { artBytes?: number } }).__clover?.artBytes ?? -1)
  ok('그림이 상한 안입니다', bytes >= 0 && bytes <= BUDGET,
     `${(bytes / 1024 / 1024).toFixed(1)}MB / ${BUDGET / 1024 / 1024}MB`)

  ok('오류가 없습니다', errors.length === 0, errors.join(' · '))

  await browser.close()
  await server.close()
  console.log(failed ? '\n덜 된 것이 있습니다' : '\n다 통과했습니다')
  process.exit(failed ? 1 : 0)
}

interface MusicReport {
  wanted?: string
  tracks: { name: string; playing: boolean; at: number }[]
}

/** 배경음이 무엇을 어떻게 내고 있는가. */
async function report(page: import('playwright').Page): Promise<MusicReport> {
  return page.evaluate(() =>
    (window as { __clover?: { music?: MusicReport } }).__clover?.music
      ?? { tracks: [] }) as Promise<MusicReport>
}

/** 지금까지 그린 프레임 수. */
async function frames(page: import('playwright').Page): Promise<number> {
  return page.evaluate(() =>
    (window as { __clover?: { drawn?: number } }).__clover?.drawn ?? 0)
}

/**
 * 물러났다 · 돌아왔다.
 *
 * **`document.hidden` 을 갈아 끼웁니다.** 크롬은 탭이 실제로 가려질 때만 그 값을 바꾸는데,
 * 도구는 탭 하나로 도므로 가릴 것이 없습니다.
 */
async function hide(page: import('playwright').Page, hidden: boolean): Promise<void> {
  await page.evaluate(value => {
    Object.defineProperty(document, 'hidden', { value, configurable: true })
    Object.defineProperty(document, 'visibilityState',
                          { value: value ? 'hidden' : 'visible', configurable: true })
    document.dispatchEvent(new Event('visibilitychange'))
  }, hidden)
}

void main()
