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
// `wanderer` 로 잽니다. 낸 카드마다 확률 없이 바꾸는 조커입니다 — 한 번에 여러 장이
// 바뀌므로 「한 장씩 도는가」 도 이 하나로 재집니다.
//
// **`hothouse` 로 재던 것을 옮겼습니다.** 그 조커가 없어진 뒤로 이 도구는 아무것도 걸지
// 못한 채 「판이 선 횟수 0」 으로 끝나고 있었습니다 — 지금 데이터에 덱 안의 카드를 바꾸는
// 조커는 없고, 있는 것은 낸 카드를 바꾸는 것들입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  chooseFive, grantJoker, openRun, pass, peek, pickCards, pressPlay, skipLogin,
} from './harness'

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
  await grantJoker(page, 'wanderer')
  await pass(page, 400)

  const before = await peek(page)
  // **다섯 장을 냅니다.** 한 장이면 「한 장씩 도는가」 를 잴 수 없습니다 — 재려는 것이
  // 장마다의 간격이므로 여러 장이 한 번에 바뀌어야 합니다.
  //
  // **기다리지 않습니다.** 연출이 다 끝날 때까지 기다리면 판이 서고 걷히는 것이 그 기다림
  // 안에서 지나갑니다 — 누르고 그 뒤를 30밀리초씩 봅니다.
  await pickCards(page, chooseFive(before.hand))
  await pass(page, 200)
  await pressPlay(page)

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
  // **연출이 끝나고 판이 다 걷힐 때까지입니다.** 표본 수를 세어 두면 그 수는 그때의 박자
  // 길이에 매인 값입니다 — 220 에서 300 으로, 다시 420 으로 올린 자리이고, 연출을 느리게
  // 한 날 또 어긋납니다. 재는 것은 「걷혔는가」이지 「몇 표본 안에 걷혔는가」가 아닙니다.
  let samples = 0
  // 바뀌는 카드의 윗변과 낸 카드의 아랫변이 가장 가까웠던 거리. **음수면 겹친 것입니다.**
  let closest = Number.POSITIVE_INFINITY
  // **조용한 것이 이어져야 끝입니다.** 판과 판 사이에도 한 표본쯤은 비어 있어서, 한 번
  // 조용한 것으로 끊으면 뒤에 올 판들을 보지 못한 채 끝납니다.
  let quiet = 0
  for (let i = 0; i < 1_200; i++) {
    const now = await peek(page)
    const out = now.changeCards ?? []
    mostOut = Math.max(mostOut, out.length)
    // **줄의 자리는 화면이 알립니다.** 손패는 판이 도는 동안 물러나 있고 이 줄은 그것을
    // 따라가므로, 도구에 적어 둔 486 은 물러난 판에서 낡은 값입니다.
    const rowY = now.boardRows?.show ?? 0
    for (const one of out) {
      kinds.add(one.kind)
      // **안착한 표본만 셉니다.** 카드는 용수철로 오므로 오는 동안의 자리는 줄이 아닙니다.
      if (Math.abs(one.y - rowY) <= 3) rows.add(one.y)
      // **낸 카드와 겹치지 않아야 합니다.** 이 줄과 낸 카드의 줄 사이가 카드 하나보다
      // 좁았고, 들어오며 부푸는 그 순간에 윗부분이 낸 카드에 걸쳤습니다. 오는 도중도
      // 셉니다 — 걸치는 것이 가장 심한 자리가 거기입니다.
      const bottom = now.boardRows?.playBottom ?? 0
      if (bottom > 0 && one.top !== undefined) closest = Math.min(closest, one.top - bottom)
      if (one.back !== true) continue
      if (!backAt.has(one.uid)) backAt.set(one.uid, i)
      backFor.set(one.uid, (backFor.get(one.uid) ?? 0) + 1)
    }
    if (now.deckPeek === true) peeked++
    shows = Math.max(shows, now.cardShows ?? 0)
    for (const cue of now.sounds ?? []) heard.add(cue)
    samples = i + 1
    // **판이 한 번은 섰어야 끝입니다.** 누른 직후에는 아직 아무것도 서지 않았고, 그때의
    // 「떠 있는 것이 없다」 는 걷힌 것이 아닙니다.
    quiet = shows >= 1 && !now.busy && out.length === 0 ? quiet + 1 : 0
    if (quiet >= 30) break
    await pass(page, 30)
  }
  const pops = ((await peek(page)).pops ?? []).filter(one => !popsBefore.has(one.join('|')))

  const after = await peek(page)
  console.log('판이 선 횟수', shows)
  console.log('판 위에 한꺼번에 나온 장수', mostOut, '· 갈래', [...kinds].join(' · ') || '없음')
  console.log('제 줄에 안착했는가', rows.size > 0, '· 그 줄', [...rows].join(' ') || '없음')
  const apart = Number.isFinite(closest) ? closest : 0
  console.log('낸 카드의 아랫변과 가장 가까웠던 거리', Math.round(apart), 'px',
              '· 손패가 물러난 거리', after.boardRows?.drop ?? 0, 'px')
  console.log('덱이 나와 있던 표본', peeked, '/', samples)
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
    && turned >= 1 && staggered && pops.length > 0 && apart > 0
    && peeked > 0 && heard.has('card_flip') && (after.changeCards ?? []).length === 0
    && before.deckSize === after.deckSize
  console.log(good ? '뒷면을 거쳐 한 장씩 바뀌고 그 자리에 적힙니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
