// 뒤로 갔다 곧바로 돌아온 자리에서 소리가 남는가.
//
// **내려놓는 것과 재우는 것이 두 단계입니다.** 나는 중에 재우면 파형이 잘린 자리에서
// 「퍽」 소리가 나므로, 이득을 0 으로 내린 뒤 0.05초 뒤에 재웁니다 — 그 사이에 돌아오면
// 그 예약이 깨운 뒤에 도착해서, 방금 깨운 길을 다시 재웁니다.
//
// **데스크탑에서는 그것이 그 판의 끝까지 갑니다.** 창을 내렸다 올릴 때까지
// `visibilitychange` 가 다시 오지 않으므로, 소리가 돌아올 자리가 없습니다. 손전화는 앱을
// 자주 오가므로 다음 왕복에 저절로 돌아오고, 그래서 이 결함은 데스크탑에서만 남습니다.
//
// 셋을 봅니다. 곧바로 돌아오면 도는가, 뜸하게 돌아와도 도는가, 그리고 물러난 동안에는
// 실제로 재워지는가.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { openRun, pass, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5232

/**
 * 봐도 되는 콘솔 오류.
 *
 * **계정 서버가 없습니다.** 이 도구는 개발 서버만 띄우므로 타이틀이 `/auth/providers` 를
 * 조회할 때 500 이 돌아옵니다.
 */
function noise(line: string): boolean {
  return line.includes('500 (Internal Server Error)') || line.includes('/auth/')
}

/**
 * 화면이 보이는가를 바꾸고 그 사실을 알립니다.
 *
 * **글로 넘깁니다.** 함수로 넘기면 esbuild 가 이름을 남기려고 `__name` 을 붙이고, 그
 * 도우미는 페이지에 없으므로 `__name is not defined` 로 끝납니다.
 */
async function show(page: Page, visible: boolean): Promise<void> {
  const want = visible ? 'true' : 'false'
  await page.evaluate(`(() => {
    var want = ${want};
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: function () { return want ? 'visible' : 'hidden' },
    });
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: function () { return !want },
    });
    document.dispatchEvent(new Event('visibilitychange'));
  })()`)
}

/** 지금 소리 길이 어떤 상태인가. **소리 마디 하나를 만들어 그것에 물어봅니다.** */
async function stateOf(page: Page): Promise<string> {
  return page.evaluate('window.__ctx ? window.__ctx.state : "none"') as Promise<string>
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)

  const problems: string[] = []
  page.on('console', message => {
    if (message.type() === 'error' && !noise(message.text())) problems.push(message.text())
  })
  page.on('pageerror', error => { if (!noise(String(error))) problems.push(String(error)) })

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-WAKE1&tick=manual`,
    { waitUntil: 'networkidle' })
  await pass(page, 1200)

  // **만들어지는 소리 길을 붙잡아 둡니다.** 게임이 그것을 내놓지 않으므로 여기서 잡습니다.
  await page.evaluate(`(() => {
    var Made = window.AudioContext;
    window.AudioContext = function () {
      var made = new Made();
      window.__ctx = made;
      return made;
    };
    window.AudioContext.prototype = Made.prototype;
  })()`)

  // **소리 길은 사람이 누른 뒤에 열립니다.**
  await openRun(page)
  await pass(page, 600)

  const opened = await stateOf(page)
  if (opened !== 'running') {
    console.log(`소리 길이 열리지 않았습니다 — ${opened}`)
    await browser.close()
    await server.close()
    return 1
  }
  console.log('열린 뒤 ' + opened)

  // ------------------------------------------------------------ 곧바로 돌아옵니다
  //
  // **재우기로 걸어 둔 것이 도착하기 전입니다.** 내려놓는 데 0.04초이고, 여기서 돌아오는
  // 것은 그보다 짧습니다.
  await show(page, false)
  await page.waitForTimeout(20)
  await show(page, true)
  await page.waitForTimeout(400)
  const quick = await stateOf(page)
  console.log(`곧바로 돌아온 뒤 ${quick}`)

  // ------------------------------------------------------------ 물러난 동안
  await show(page, false)
  await page.waitForTimeout(400)
  const away = await stateOf(page)
  console.log(`물러난 동안 ${away}`)

  // ------------------------------------------------------------ 뜸하게 돌아옵니다
  await show(page, true)
  await page.waitForTimeout(400)
  const slow = await stateOf(page)
  console.log(`뜸하게 돌아온 뒤 ${slow}`)

  // **출력에 값이 흐르는가.** 소리 길이 돌고 있다는 것과 들린다는 것이 다른 일이라,
  // 소리를 한 번 내 보고 잰 값을 봅니다.
  const heard = await page.evaluate(`(() => {
    var c = window.__clover || {};
    return c.audio ? c.audio.peak : -1;
  })()`) as number
  console.log(`출력에 흐른 봉우리 ${heard}`)
  if (heard <= 0) problems.push(`소리를 냈는데 출력에 흐른 값이 ${heard} 입니다`)

  if (quick !== 'running') problems.push(`곧바로 돌아왔는데 ${quick} 입니다`)
  if (away !== 'suspended') problems.push(`물러났는데 ${away} 입니다`)
  if (slow !== 'running') problems.push(`돌아왔는데 ${slow} 입니다`)

  if (problems.length > 0) {
    console.log('어긋납니다:')
    for (const one of problems.slice(0, 6)) console.log('  ' + one)
  } else {
    console.log('오갔다 돌아와도 소리가 남습니다')
  }

  await browser.close()
  await server.close()
  return problems.length === 0 ? 0 : 1
}

main().then(code => process.exit(code))
