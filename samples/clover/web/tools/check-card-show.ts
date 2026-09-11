// 바뀌는 카드가 판 위로 나오는가.
//
// **덱 안의 카드는 일어난 자리가 화면에 없었습니다.** 덱은 라운드 사이에 화면 오른쪽으로
// 물러나 있어서, 조커가 덱의 카드를 바꾸면 오른쪽 토스트 한 줄이 전부였습니다 — 「어, 뭐가
// 바뀐 거지」가 그것입니다.
//
// **그리고 뒷면을 거쳐 한 장씩 뒤집히는가.** 앞면에서 앞면으로 반 바퀴만 돌던 동안은
// 좁아졌다 벌어지는 62밀리초 안에 얼굴이 갈렸고, 장마다의 간격(90밀리초)이 반 바퀴보다
// 짧아 여러 장이 한 덩어리로 갈렸습니다 — 눈에는 「뭔가 찌그러졌다」까지만 보입니다.
//
// `hothouse` 로 잽니다. 라운드 끝에 확률 없이 덱의 카드 두 장을 바꾸는 조커입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { grantJoker, openRun, pass, peek, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5233

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  page.on('pageerror', error => console.log('  [터짐]', error.stack ?? error.message))
  page.on('console', one => {
    if (one.type() === 'error') console.log('  [콘솔]', one.text().slice(0, 200))
  })
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-SHOW1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  await grantJoker(page, 'hothouse')
  await pass(page, 400)

  const before = await peek(page)
  // **`clearBlind` 도 쓰지 않습니다.** 그것은 연출이 다 끝날 때까지 기다리므로 판이 서고
  // 걷히는 것이 그 기다림 안에서 지나갑니다 — 훅만 부르고 그 뒤를 30밀리초씩 봅니다.
  await page.evaluate(() => {
    const hook = (window as unknown as { __clover: { clearBlind?(): void } }).__clover
    hook.clearBlind?.()
  })

  // 라운드 끝의 박자들이 도는 동안을 봅니다.
  let mostOut = 0
  let peeked = 0
  let shows = 0
  const kinds = new Set<string>()
  const rows = new Set<number>()
  const heard = new Set<string>()
  // 장마다 뒷면이 처음 보인 표본 번호. **한 장씩 도는지는 이 값들의 간격입니다.**
  const backAt = new Map<number, number>()
  // 뒷면이 보인 표본 수. 장마다 셉니다 — 한 프레임만 스쳐서는 뒤집힌 것으로 읽히지 않습니다.
  const backFor = new Map<number, number>()
  const popsBefore = new Set(((await peek(page)).pops ?? []).map(one => one.join('|')))
  // **창이 뒤집기보다 길어야 합니다.** 뒷면을 거치게 되며 한 장이 도는 데 1초가 되었고,
  // 220 표본(6.6초)에서는 마지막 판이 걷히기 전에 재기가 끝났습니다.
  for (let i = 0; i < 300; i++) {
    const now = await peek(page)
    const out = now.changeCards ?? []
    mostOut = Math.max(mostOut, out.length)
    for (const one of out) {
      kinds.add(one.kind)
      // **안착한 표본만 셉니다.** 카드는 용수철로 오므로 오는 동안의 자리는 줄이 아닙니다.
      if (Math.abs(one.y - 486) <= 3) rows.add(one.y)
      if (one.back !== true) continue
      if (!backAt.has(one.uid)) backAt.set(one.uid, i)
      backFor.set(one.uid, (backFor.get(one.uid) ?? 0) + 1)
    }
    if (now.deckPeek === true) peeked++
    shows = Math.max(shows, now.cardShows ?? 0)
    for (const cue of now.sounds ?? []) heard.add(cue)
    await pass(page, 30)
  }
  const pops = ((await peek(page)).pops ?? []).filter(one => !popsBefore.has(one.join('|')))

  const after = await peek(page)
  console.log('판이 선 횟수', shows)
  console.log('판 위에 한꺼번에 나온 장수', mostOut, '· 갈래', [...kinds].join(' · ') || '없음')
  console.log('제 줄에 안착했는가', rows.size > 0, '· 그 줄', [...rows].join(' ') || '없음')
  console.log('덱이 나와 있던 표본', peeked, '/ 300')
  console.log('난 소리', [...heard].filter(one => one.startsWith('card_')).join(' · '))
  console.log('덱', before.deckSize, '→', after.deckSize, '· 판을 다 걷었는가',
              (after.changeCards ?? []).length === 0)

  // **뒷면을 거칩니다.** 뒤집기는 앞면 → 뒷면 → 앞면이고, 가운데가 없으면 카드가 잠깐
  // 찌그러졌다 펴지는 것으로만 보입니다.
  const turned = [...backFor.values()].filter(one => one >= 2).length
  console.log('뒷면을 거친 장수', turned, '/', backAt.size,
    '· 뒷면이 보인 표본', [...backFor.values()].join(' · ') || '없음')

  // **한 장씩 돕니다.** 장마다의 간격이 220밀리초이므로 표본(30밀리초)으로 다섯은 떨어집니다.
  const starts = [...backAt.values()].sort((a, b) => a - b)
  const gaps = starts.slice(1).map((one, i) => one - starts[i])
  const staggered = starts.length < 2 || gaps.every(one => one >= 4)
  console.log('뒤집기 시작 표본', starts.join(' · ') || '없음',
    '· 사이', gaps.join(' · ') || '한 장뿐')

  // **무엇이 무엇으로 바뀌었는지가 카드 위에 적힙니다.**
  console.log('뜬 글', pops.map(one => one[0]).join(' · ') || '없음')

  const good = shows >= 1 && mostOut >= 1 && kinds.has('modify') && rows.size > 0
    && turned >= 1 && staggered && pops.length > 0
    && peeked > 0 && heard.has('card_flip') && (after.changeCards ?? []).length === 0
    && before.deckSize === after.deckSize
  console.log(good ? '뒷면을 거쳐 한 장씩 바뀌고 그 자리에 적힙니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
