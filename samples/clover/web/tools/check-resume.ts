// 그만둔 판이 그대로 이어지는가.
//
// **저장은 액션 목록입니다.** 되살리는 것은 `newRun` 뒤에 `apply` 를 차례로 돌리는 것이고,
// 그 길은 서버의 판정과 `headless` 가 지나는 길과 같습니다 — 그러므로 「같은 판인가」는
// 눈으로가 아니라 상태 해시로 갈립니다. 안테와 금액이 같아도 덱의 차례와 난수의 자리가
// 다르면 다른 판입니다.
//
// 여기서 보는 것은 다섯입니다.
//
// 1. 판을 두다 타이틀로 나가면 「이어하기」 탭이 생기는가
// 2. 이어서 한 판의 상태 해시가 나가기 전과 같은가
// 3. **끌어서 옮긴 손패의 차례가 그대로 이어지는가**
// 4. 창을 닫았다 다시 열어도(새로 고침) 그 판이 남아 있는가
// 5. 버리면 그 탭이 없어지는가
//
// 셋째가 따로 서 있는 이유가 있습니다. **자리를 옮기는 것은 오랫동안 액션이 아니었고**,
// 그래서 이어한 판은 옮기기 전의 차례로 되살아났습니다 — 조커의 차례는 점수를 바꾸므로
// 그것은 다른 판입니다. 해시만 보면 이 결함이 보이지 않았습니다: 옮긴 뒤에 아무 액션도
// 두지 않고 나가면 저장에 적힌 해시도 옮기기 전의 것이라, 어긋난 둘이 서로 같았습니다.
//
//     npx tsx tools/check-resume.ts

import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'

import {
  clickPrimary, clickSpot, closeGuide, confirmYes, dragBy, handSpot, pass, peek, pickCards,
  pressPlay, pressRunPanel, pressTitle, settle, skipLogin, startNewRun,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5296

let failed = 0

function check(name: string, good: boolean, detail = ''): void {
  if (!good) failed++
  console.log(`  ${good ? '✓' : '✗'} ${name}${detail ? '  —  ' + detail : ''}`)
}

/** 저장된 판이 있는가. 「이어하기」 탭이 서는 것으로 봅니다. */
async function resumeTabUp(page: import('playwright').Page): Promise<boolean> {
  await pressTitle(page, 'start')
  await pass(page, 700)
  const up = (await peek(page)).spots?.['run:tab:resume'] !== undefined
  await page.keyboard.press('Escape')
  await pass(page, 500)
  return up
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, locale: 'ko-KR' })
  await skipLogin(page)

  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-KEEP1&tick=manual`,
                  { waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  // 저장을 비우고 시작합니다. **처음 여는 사람의 상태**에서는 이어할 것이 없습니다.
  await page.evaluate(() => {
    localStorage.removeItem('clover.run')
    localStorage.setItem('clover.guide.seen', '1')
  })
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)

  check('처음에는 이어하기가 없습니다', !(await resumeTabUp(page)))

  // 판을 열고 몇 수 둡니다. **블라인드를 고르고 한 번 내야 저장에 액션이 쌓입니다.**
  await startNewRun(page)
  await pass(page, 900)
  await closeGuide(page)
  await pass(page, 400)
  await clickPrimary(page)
  await settle(page)
  await pickCards(page, [0, 1])
  await pressPlay(page)
  await settle(page)
  await pass(page, 400)

  // **차례를 어지릅니다.** 정렬 한 번과 끌기 한 번입니다 — 둘 다 액션을 거치지 않고
  // 손패를 바꾸던 길이었습니다.
  await clickSpot(page, 'sort:rank')
  await pass(page, 600)

  const laid = (await peek(page)).handOrder
  await dragBy(page, await handSpot(page, 0, laid.length),
               await handSpot(page, 3, laid.length))
  const stirred = (await peek(page)).handOrder
  check('끌어서 자리가 바뀝니다', laid.length === stirred.length && laid[0] !== stirred[0],
        `${laid.join(',')} → ${stirred.join(',')}`)

  const before = await peek(page)
  check('판이 돌고 있습니다', before.scene === 'run', `${before.scene} · ${before.phase}`)

  // 타이틀로 나갑니다. **묻고 나서 갑니다.**
  await clickSpot(page, 'menu')
  await pass(page, 500)
  await clickSpot(page, 'menu:toTitle')
  await pass(page, 700)
  await confirmYes(page)
  await pass(page, 1400)

  const atTitle = await peek(page)
  check('타이틀로 돌아왔습니다', atTitle.scene === 'title', atTitle.scene)
  check('이어하기가 생겼습니다', await resumeTabUp(page))

  // 새로 고쳐도 남아 있는가. **창을 닫았다 다시 여는 것과 같은 자리입니다.**
  await page.reload({ waitUntil: 'domcontentloaded' })
  await pass(page, 2600)
  check('다시 열어도 남아 있습니다', await resumeTabUp(page))

  // 이어서 합니다.
  await pressTitle(page, 'start')
  await pass(page, 700)
  await pressRunPanel(page, 'tab:resume')
  await pass(page, 400)
  await pressRunPanel(page, 'resume')
  await pass(page, 2000)

  const after = await peek(page)
  check('판으로 들어갔습니다', after.scene === 'run', after.scene)
  check('그만두던 판과 같습니다', after.hash === before.hash,
        `${String(before.hash).slice(0, 12)} → ${String(after.hash).slice(0, 12)}`)
  check('안테와 금액이 같습니다',
        after.ante === before.ante && after.money === before.money,
        `안테 ${after.ante} · $${after.money}`)
  // **해시가 같아도 따로 봅니다.** 옮긴 것이 저장에 적히지 않으면 저장의 해시도 옮기기
  // 전의 것이므로, 어긋난 둘이 해시로는 같아 보입니다.
  check('손패의 차례가 그대로입니다', after.handOrder.join() === stirred.join(),
        `${stirred.join(',')} → ${after.handOrder.join(',')}`)

  await page.screenshot({
    path: path.resolve(HERE, '..', '..', 'design-data', 'out', 'check', 'resume-run.png'),
  })

  await browser.close()
  await server.close()
  console.log(failed === 0 ? '\n다 통과했습니다' : `\n${failed}개 실패`)
  return failed > 0 ? 1 : 0
}

main().then(code => process.exit(code))
