// 족보 도움이 카드를 밝힐 때 소리가 나는가.
//
// **밝히는 것은 알림이지 사건이 아닙니다.** 사람이 아무것도 하지 않았는데 소리가 나면 무슨
// 일이 일어난 것으로 들리고, 패를 볼 때마다 그 소리가 납니다.
//
// 아무것도 누르지 않은 채로 도움이 켜지는 순간을 기다려, 그 사이에 난 소리를 적습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import {
  at, chooseFive, clickPrimary, peek, playHand, settle, STAGE_W, skipLogin, swept, TITLE_START,
  pass,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5232

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-HINT1&tick=manual`, { waitUntil: 'networkidle' })
  await pass(page, 1500)

  const start = await at(page, TITLE_START.x, TITLE_START.y)
  await page.mouse.click(start.x, start.y)
  await pass(page, 900)
  await page.mouse.click(20, 20)
  await pass(page, 400)
  await clickPrimary(page)
  await settle(page)
  await pass(page, 1200)

  // 여기서부터가 재는 구간입니다. **패는 이미 다 깔렸고 아무것도 누르지 않습니다** —
  // 이 사이에 나는 소리는 전부 사람이 시키지 않은 것입니다.
  const before = ((await peek(page)).sounds ?? []).length
  await pass(page, 2500)
  const after = (await peek(page)).sounds ?? []
  const during = after.slice(before)

  console.log('가만히 두는 동안', during.length === 0 ? '없음' : during.join(' · '))

  // 한 장을 고릅니다. **고르는 소리 하나 말고는 없어야 합니다** — 그 한 번에 도움이 다시
  // 계산되어 밝히는 카드가 바뀌는데, 그것은 알림이지 사건이 아닙니다.
  const spot = await at(page, STAGE_W / 2 - 120, 608)
  const mark = ((await peek(page)).sounds ?? []).length
  await page.mouse.click(spot.x, spot.y)
  await pass(page, 900)
  const picked = ((await peek(page)).sounds ?? []).slice(mark)
  console.log('한 장 고를 때  ', picked.length === 0 ? '없음' : picked.join(' · '))

  // 물립니다. 같은 소리 하나여야 합니다.
  const mark2 = ((await peek(page)).sounds ?? []).length
  await page.mouse.click(spot.x, spot.y)
  await pass(page, 900)
  const off = ((await peek(page)).sounds ?? []).slice(mark2)
  console.log('물릴 때      ', off.length === 0 ? '없음' : off.join(' · '))

  // 한 판을 내고 새 패가 깔린 뒤. **패가 다 깔린 다음부터가 도움이 켜지는 자리입니다** —
  // 깔리는 동안의 소리는 깔리는 소리이지 도움의 소리가 아닙니다.
  const held = await peek(page)
  await playHand(page, chooseFive(held.hand))
  await settle(page)
  // **낸 카드가 다 나간 뒤부터입니다.** 연출이 끝나도 낸 카드는 1.1초 더 남았다가 한 장씩
  // 나가며 소리를 내고, 정해진 시간만 기다리면 그 꼬리가 재는 구간에 들어옵니다.
  await swept(page)
  await pass(page, 1600)
  const mark3 = ((await peek(page)).sounds ?? []).length
  await pass(page, 2500)
  const later = ((await peek(page)).sounds ?? []).slice(mark3)
  console.log('낸 뒤 가만히 ', later.length === 0 ? '없음' : later.join(' · '))

  await browser.close()
  await server.close()
  const extra = [...picked, ...off, ...later].filter(one => one !== 'card_select')
  const good = during.length === 0 && later.length === 0 && extra.length === 0
  console.log(good ? '도움은 조용합니다' : `도움 말고 난 소리: ${extra.join(' · ')}`)
  return good ? 0 : 1
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
