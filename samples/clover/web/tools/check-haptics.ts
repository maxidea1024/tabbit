// 진동이 폰에서만 서고, 중요한 순간에만 나는가.
//
// **눌러 보고 판단할 수 없는 것입니다.** 진동은 손에 쥐고 있어야 알 수 있고 스크린샷에
// 남지 않으므로, `navigator.vibrate` 로 나가는 것을 받아 적어 판정합니다 — 앱에서는
// 다리를 지나가지만 파형은 같은 표에서 나오므로, 여기서 맞으면 앱에서도 그 순간에 납니다.
//
// 보는 것이 셋입니다.
//
// |무엇|어떻게|
// |--|--|
// |데스크탑에는 없습니다|손가락으로 짚는 화면이 아니면 옵션의 「입력」 탭이 서지 않습니다. **`navigator.vibrate` 는 데스크탑 크롬에도 있으므로** 그것만 보면 이 탭이 거기서도 섭니다|
// |폰에는 있습니다|`hasTouch` 로 열면 탭이 서고, 그 안의 줄이 켜져 있습니다|
// |중요한 순간에만 납니다|한 판을 내고 블라인드를 넘겨 무엇이 났는지 셉니다. 카드 다섯 장은 한 번이어야 합니다|
//
//     npx tsx tools/check-haptics.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type BrowserContext, type Page } from 'playwright'
import { createServer } from 'vite'

import {
  at, clearBlind, openRun, pass, peek, playHand, pressTitle, settle, skipLogin, swept,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5211

/** 받아 적은 파형이 어느 순간인가. `feedback/haptics.ts` 의 `BEATS` 와 같아야 합니다. */
const BEATS: { name: string; web: string }[] = [
  { name: 'play', web: '20' },
  { name: 'boss', web: '43' },
  { name: 'settle', web: '61' },
  { name: 'clear/win', web: '35,65,21' },
  { name: 'lose', web: '27,45,50' },
]

function nameOf(pattern: number[]): string {
  const key = pattern.join(',')
  return BEATS.find(one => one.web === key)?.name ?? `?(${key})`
}

/**
 * 나가는 진동을 받아 적습니다.
 *
 * **덮어쓰고 `true` 를 돌려줍니다** — 실제로 떨 기계가 아니므로 떨지 않는 것이 맞고,
 * 부르는 쪽은 돌아온 값을 보지 않습니다.
 */
const RECORDER = `
try {
  window.__buzz = []
  navigator.vibrate = function (pattern) {
    window.__buzz.push(Array.isArray(pattern) ? pattern : [pattern])
    return true
  }
} catch {}
`

async function buzzed(page: Page): Promise<number[][]> {
  return await page.evaluate(() => (window as unknown as { __buzz: number[][] }).__buzz)
}

async function clearBuzz(page: Page): Promise<void> {
  await page.evaluate(() => { (window as unknown as { __buzz: number[][] }).__buzz = [] })
}

/** 옵션 판이 알린 자리 하나. **좌표를 적어 두지 않습니다.** */
async function optionSpot(page: Page, name: string,
                          tries = 20): Promise<{ x: number; y: number } | undefined> {
  for (let wait = 0; wait < tries; wait++) {
    const found = (await peek(page)).spots?.[`option:${name}`]
    if (found) return await at(page, found.x, found.y)
    await pass(page, 100)
  }
  return undefined
}

/** 타이틀에서 옵션 판을 엽니다. **판이 다 떠오르기를 기다립니다.** */
async function openOptions(page: Page): Promise<void> {
  await pressTitle(page, 'options')
  await pass(page, 700)
}

async function fresh(browser: Browser, touch: boolean): Promise<[BrowserContext, Page]> {
  const context = await browser.newContext(touch ? { hasTouch: true } : {})
  const page = await context.newPage()
  await skipLogin(page)
  await page.addInitScript(RECORDER)
  await page.goto(`http://localhost:${PORT}/`, { waitUntil: 'networkidle' })
  await pass(page, 900)
  return [context, page]
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()

  let failed = 0
  const verdict = (good: boolean, line: string) => {
    if (!good) failed++
    console.log(`  ${good ? '✓' : '✗'} ${line}`)
  }

  console.log('데스크탑')
  {
    const [context, page] = await fresh(browser, false)
    const has = await page.evaluate(() => typeof navigator.vibrate === 'function')
    verdict(has, 'navigator.vibrate 가 여기에도 있습니다 — 그것만으로는 가를 수 없습니다')
    await openOptions(page)
    const tab = await optionSpot(page, 'tab:input', 4)
    verdict(tab === undefined, `「입력」 탭이 ${tab === undefined ? '없습니다' : '섰습니다'}`)
    await context.close()
  }

  // **판마다 새 문맥입니다.** 옵션은 저장소에 남으므로, 끈 채로 이어서 재면 그다음이
  // 무엇 때문에 조용한지 갈리지 않습니다.
  console.log('\n폰 — 켜진 채로')
  {
    const [context, page] = await fresh(browser, true)
    await openOptions(page)
    const tab = await optionSpot(page, 'tab:input')
    verdict(tab !== undefined, `「입력」 탭이 ${tab === undefined ? '없습니다' : '섰습니다'}`)
    if (tab) {
      await page.mouse.click(tab.x, tab.y)
      await pass(page, 400)
    }
    const value = await optionSpot(page, 'value:haptics')
    verdict(value !== undefined, '진동 줄이 자리를 알립니다')
    await page.keyboard.press('Escape')
    await pass(page, 500)

    console.log('  한 판을 내는 동안')
    await openRun(page)
    await clearBuzz(page)
    await playHand(page)
    await settle(page)
    await swept(page)
    const hand = (await buzzed(page)).map(nameOf)
    console.log(`    ${hand.length === 0 ? '(없음)' : hand.join(' · ')}`)
    // **카드는 다섯 장이지만 진동은 한 번입니다.** `PlayStaggerMs` 사이로 닿으므로 장마다
    // 떨면 그것은 다섯 번의 알림이 아니라 한 번의 긴 떨림입니다.
    const taps = hand.filter(one => one === 'play').length
    verdict(taps === 1, `낸 카드로 한 번 떱니다 — ${taps}번`)
    verdict(hand.includes('settle'), '점수가 확정될 때 떱니다')
    verdict(hand.every(one => !one.startsWith('?')), '표에 없는 파형이 나가지 않습니다')

    console.log('  블라인드를 넘길 때')
    await clearBuzz(page)
    await clearBlind(page)
    await pass(page, 1400)
    const cleared = (await buzzed(page)).map(nameOf)
    console.log(`    ${cleared.length === 0 ? '(없음)' : cleared.join(' · ')}`)
    verdict(cleared.includes('clear/win'), '블라인드를 넘길 때 떱니다')
    // **작은 것이 큰 것을 가리지 않습니다.** 득점 확정과 격파는 배속을 올리면 260ms 까지
    // 붙는데, 간격만으로 막으면 뒤에 오는 격파가 사라집니다.
    verdict(!cleared.includes('settle')
            || cleared.indexOf('clear/win') > cleared.indexOf('settle'),
            '득점 확정이 격파를 가리지 않습니다')
    await context.close()
  }

  console.log('\n폰 — 꺼 놓고')
  {
    const [context, page] = await fresh(browser, true)
    await openOptions(page)
    const tab = await optionSpot(page, 'tab:input')
    if (tab) {
      await page.mouse.click(tab.x, tab.y)
      await pass(page, 400)
    }
    const value = await optionSpot(page, 'value:haptics')
    if (!value) {
      verdict(false, '진동 줄의 단추가 자리를 알리지 않습니다')
    } else {
      await page.mouse.click(value.x, value.y)
      await pass(page, 400)
      await page.keyboard.press('Escape')
      await pass(page, 500)
      await openRun(page)
      await clearBuzz(page)
      await playHand(page)
      await settle(page)
      const quiet = (await buzzed(page)).map(nameOf)
      console.log(`  ${quiet.length === 0 ? '(없음)' : quiet.join(' · ')}`)
      verdict(quiet.length === 0, `끄면 나지 않습니다 — ${quiet.length}번`)
    }
    await context.close()
  }

  await browser.close()
  await server.close()

  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code)).catch((error: unknown) => {
  console.log(String(error))
  process.exit(1)
})
