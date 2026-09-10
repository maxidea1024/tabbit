// 값을 바꾸지 않는 조커가 발동하는 것이 화면에 나타나는가.
//
// **439종 중 113종이 그랬습니다.** `report` 를 부르는 자리가 값을 바꾸는 연산 7가지뿐이라,
// 카드를 만들고 부수고 바꾸는 조커는 발동해도 이벤트를 하나도 내지 않았습니다 — 화면은
// 받을 것이 없으므로 딱지가 흔들리지도 글이 뜨지도 않았습니다.
//
// `hothouse` 로 잽니다. 라운드 끝에 확률 없이 두 줄이 도는 조커라, 이기면 반드시 두 번
// 발동합니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { grantJoker, openRun, pass, peek, skipLogin, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5232

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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-ACT1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  // **덱 안의 카드를 바꾸는 조커입니다.** 값은 하나도 내지 않습니다.
  await grantJoker(page, 'hothouse')
  await pass(page, 400)

  const before = await peek(page)
  await winRound(page)

  // 라운드 끝의 박자들이 도는 동안을 봅니다.
  const said = new Set<string>()
  const heard = new Set<string>()
  for (let i = 0; i < 90; i++) {
    const now = await peek(page)
    for (const [text] of now.pops ?? []) said.add(text)
    for (const cue of now.sounds ?? []) heard.add(cue)
    await pass(page, 30)
  }

  const after = await peek(page)
  console.log('뜬 글', [...said].join(' · ') || '없음')
  console.log('난 소리', [...heard].join(' · ') || '없음')

  // **바뀐 것이 실제로 있어야 합니다.** 몸짓만 확인하면 아무 일도 없는데 나는 것을
  // 지나칩니다 — 코어가 「아무것도 바꾸지 못한 것은 발동이 아니다」로 거르는 자리입니다.
  // **갈래의 말이 떠야 합니다.** 다섯 갈래가 다 「발동」 하나였을 때는 만든 것과 부순
  // 것이 같은 글이었습니다 — `hothouse` 는 덱의 카드를 바꾸는 조커입니다.
  const acted = said.has('바꿉니다')
  const cue = heard.has('card_flip')
  console.log('바꾸는 갈래의 말이 떴는가', acted, '· 그 갈래의 소리가 났는가', cue)
  console.log('덱', before.deckSize, '→', after.deckSize, '(장수는 그대로여야 합니다)')

  const good = acted && cue && before.deckSize === after.deckSize
  console.log(good ? '값을 내지 않는 조커도 무엇을 한 것인지 보입니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
