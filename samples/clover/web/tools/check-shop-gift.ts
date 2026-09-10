// 상점에 놓인 선물이 누가 놓은 것인지 나타나는가.
//
// **다음 상점에서야 뜻을 가지는 것입니다.** 태그와 조커가 놓아 두는 물건은 값이 0 인
// 채로 상점에 서 있고, 왜 거기 있는지는 화면 어디에도 없었습니다 — 공짜 조커 하나가
// 이유 없이 놓여 있는 것으로 보입니다.
//
// `uncommon` 태그로 잽니다. 상점에 들 때 조커 하나를 공짜로 놓아 두는 태그입니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { grantTag, openRun, pass, peek, settle, skipLogin, winRound } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5238

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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-GIFT1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  // **상점에 들 때 도는 태그입니다.** 들고 있기만 하면 됩니다.
  await grantTag(page, 'uncommon')
  await pass(page, 400)

  await winRound(page)
  await settle(page)
  await pass(page, 900)

  const now = await peek(page)
  console.log('국면', now.phase, '· 상점 칸', (now.shopKinds ?? []).join(' '))

  // **놓아 둔 것은 줄의 맨 앞입니다.** 값이 0 이고 그 칸에 놓은 것의 이름이 적힙니다.
  const gift = now.shopGift
  console.log('선물 칸이 알린 것', JSON.stringify(gift ?? null))

  const good = now.phase === 'shop' && gift !== undefined && gift.slot === 0
    && gift.from !== '' && gift.cost === 0
  console.log(good ? '선물에 놓은 것의 이름이 적힙니다' : '어긋납니다')

  await browser.close()
  await server.close()
  return good ? 0 : 1
}

main().then(code => process.exit(code))
