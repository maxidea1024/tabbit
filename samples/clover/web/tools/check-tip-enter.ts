// 설명 쪽지가 커서가 들어올 때에만 뜨는가.
//
// **뜨는 조건은 「밑에 있는 것이 달라졌다」가 아니라 「커서가 옮겨 가서 들어왔다」입니다.**
// 조커 줄은 사고 팔고 순서를 바꿀 때마다 다시 배치되므로, 달라진 것만 보면 사람이 커서를
// 한 픽셀도 움직이지 않았는데 지나가는 조커마다 차례로 설명이 뜹니다.
//
// 재는 순서는 셋입니다.
//
// 1. 조커 위에 커서를 올립니다 — 떠야 합니다. 이것이 없으면 나머지 둘은 아무 말도 아닙니다
// 2. 빈 칸으로 커서를 옮겨 두고, **커서를 건드리지 않은 채로** 그 칸에 조커를 놓습니다 —
//    뜨면 안 됩니다
// 3. 커서를 한 픽셀 움직입니다 — 그때 떠야 합니다. 들어왔다가 영영 못 뜨는 상태가 되면
//    2번을 막은 것이 아니라 기능을 지운 것입니다
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { at, grantJoker, jokerSpot, openRun, pass, peek, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5241

/** 커서가 조커에 닿았다가 떨어질 만큼. 칸 하나보다 넓습니다. */
const AWAY = { x: 372 + 100 * 3, y: 108 }

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-TIP1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  await pass(page, 1200)

  // 조커 둘. 왼쪽부터 0번·1번 칸에 섭니다.
  await grantJoker(page, 2)
  await pass(page, 2000)

  // 1. 올리면 뜹니다.
  const first = await at(page, jokerSpot(0).x, jokerSpot(0).y)
  await page.mouse.move(first.x, first.y)
  await pass(page, 300)
  const onJoker = (await peek(page)).tip
  console.log('조커에 올렸을 때      ', onJoker ? '뜹니다' : '안 뜹니다')

  // 빈 칸으로 옮겨 둡니다. **2번 칸입니다** — 조커 둘은 0번과 1번에 있습니다.
  const empty = await at(page, jokerSpot(2).x, jokerSpot(2).y)
  await page.mouse.move(AWAY.x, AWAY.y)
  await page.mouse.move(empty.x, empty.y)
  await pass(page, 600)
  const onEmpty = (await peek(page)).tip
  console.log('빈 칸에 올렸을 때     ', onEmpty ? '뜹니다' : '안 뜹니다')

  // 2. 커서를 건드리지 않고 그 칸에 조커가 옵니다. **여기서부터 마우스를 부르지 않습니다.**
  await grantJoker(page, 2)
  await pass(page, 2500)
  const arrived = (await peek(page)).tip
  console.log('조커가 커서 밑에 왔을 때', arrived ? '뜹니다' : '안 뜹니다')

  // 3. 한 픽셀 움직입니다.
  await page.mouse.move(empty.x + 1, empty.y)
  await pass(page, 300)
  const nudged = (await peek(page)).tip
  console.log('그 뒤 한 픽셀 움직이면  ', nudged ? '뜹니다' : '안 뜹니다')

  await browser.close()
  await server.close()

  const bad: string[] = []
  if (!onJoker) bad.push('조커에 올려도 뜨지 않습니다')
  if (onEmpty) bad.push('빈 칸에서 뜹니다')
  if (arrived) bad.push('커서가 가만히 있는데 조커가 와서 뜹니다')
  if (!nudged) bad.push('들어온 뒤 커서를 움직여도 뜨지 않습니다')
  console.log(bad.length === 0 ? '설명은 커서가 들어올 때에만 뜹니다' : bad.join(' · '))
  return bad.length === 0 ? 0 : 1
}

main().then(code => process.exit(code)).catch(error => {
  console.error(error)
  process.exit(1)
})
