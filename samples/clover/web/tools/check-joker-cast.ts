// 조커에 걸리는 것들이 화면에 나타나는가.
//
// **셋 다 이벤트조차 없던 것들입니다.** 조커의 판이 갈리고(`OpModifyJoker`) 능력을
// 빌리고(`OpCopyJoker`) 하나가 꺼지는 것(`OpDisableRandomJoker`)은 상태만 바꾸고 아무것도
// 내지 않아서, 화면이 어느새 달라져 있었습니다.
//
// **소리로 재지 않습니다.** 판이 갈리는 것과 능력을 빌리는 것은 같은 소리를 내므로, 소리로
// 재면 둘 중 어느 것이 돈 것인지 알 수 없습니다 — 화면이 실제로 그린 박자를 봅니다.
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import {
  grantConsumableId, grantJoker, heldButton, itemSpot, openRun, pass, peek, playHand,
  settle, skipLogin,
} from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const PORT = 5237

let bad = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '어긋남'}  ${what}`)
  if (!ok) bad++
}

/** 지금까지 그려진 박자에 이것이 있는가. */
async function drew(page: Page, name: string): Promise<boolean> {
  return ((await peek(page)).beats ?? []).includes(name)
}

/** 소모품 첫 칸을 골라 씁니다. */
async function useFirstItem(page: Page): Promise<void> {
  const tile = await itemSpot(page, 0)
  await page.mouse.move(tile.x, tile.y)
  await pass(page, 120)
  await page.mouse.down()
  await pass(page, 60)
  await page.mouse.up()
  await pass(page, 350)
  const use = await heldButton(page)
  await page.mouse.click(use.x, use.y)
}

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
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-JOKER1&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)

  await openRun(page)
  // 셋 다 대상이 있어야 도는 것들입니다. 딱지 둘을 놓습니다.
  await grantJoker(page, 'twig')
  await grantJoker(page, 'spinner')
  await pass(page, 400)

  // 1. 판이 갈립니다. `ectoplasm` 은 조커 하나에 `Negative` 를 붙이는 유령 카드입니다.
  //
  // **`hex` 로 재지 않습니다.** 그것은 판을 걸고 같은 액션에서 나머지 조커를 부수는데, 지금
  // 데이터에서는 host 가 조커가 아니라 「나머지」가 전부여서 방금 판이 걸린 딱지까지 탑니다 —
  // 뒤집을 딱지가 그 프레임에 이미 없습니다.
  await grantConsumableId(page, 'ectoplasm')
  await pass(page, 400)
  await useFirstItem(page)
  // **뒷면을 거칩니다.** 카드와 같은 몸짓입니다 — 판이 갈리는 것이 다음 그리기에 슬쩍
  // 달라져 있던 동안은 무엇이 걸린 것인지 화면에 남지 않았습니다. 뒷면이 보이는 그 프레임을
  // 한 장 굽습니다 — **조커의 뒷면은 눈으로만 판정됩니다.**
  let backs = 0
  let shot = false
  for (let i = 0; i < 120; i++) {
    const now = await peek(page)
    if ((now.jokersBack ?? 0) > 0) {
      backs++
      // **다 벌어진 프레임입니다.** 뒤집기의 한가운데는 딱지가 선 하나로 좁아져 있어,
      // 거기서 구우면 뒷면이 그려졌는지 보이지 않습니다.
      if (!shot && backs >= 5) {
        shot = true
        await page.screenshot({
          path: path.resolve(HERE, '../../design-data/out/check/joker-back.png'),
        })
      }
    }
    if (await drew(page, 'JokerModified') && shot) break
    await pass(page, 20)
  }
  check(await drew(page, 'JokerModified'), '조커의 판이 갈리는 것이 그려집니다')
  check(backs > 0, `뒷면을 거쳐 뒤집힙니다 (뒷면이 보인 표본 ${backs})`)
  await settle(page)

  // 2. **계속 빌리고 있는 것은 박자가 아닙니다.**
  //
  // `tracing` 은 오른쪽 조커의 능력을 계속 빌립니다 — 그 조커의 효과를 모을 때마다 다시
  // 도는 것이므로, 그때마다 알리면 한 라운드에 「빌렸다」가 수십 번이고 그중 어느 것도
  // 지금 일어난 일이 아닙니다. 빌리고 있다는 것은 딱지에 계속 나타나야 하는 것이고,
  // 그것은 박자가 아니라 그림입니다.
  await grantJoker(page, 'tracing')
  // **오른쪽에 빌려줄 딱지가 있어야 합니다.** 줄의 끝에 서면 빌릴 것이 없습니다.
  await grantJoker(page, 'twig')
  await pass(page, 400)
  // 빌리는 것은 순간이 아니라 상태이므로 딱지에 계속 나타납니다.
  check((await peek(page)).borrowLink === true, '빌리는 딱지와 빌려주는 딱지가 이어집니다')
  await playHand(page)
  await settle(page)
  const copies = ((await peek(page)).beats ?? []).filter(one => one === 'JokerCopied').length
  check(copies === 0, `계속 빌리는 것은 박자로 나지 않습니다 (${copies}번)`)

  // 3. 하나가 꺼집니다. `snake_pit` 은 패를 낼 때마다 조커 하나를 끄는 조커입니다.
  await grantJoker(page, 'snake_pit')
  await pass(page, 400)
  await playHand(page)
  let dark = 0
  for (let i = 0; i < 140; i++) {
    dark = Math.max(dark, (await peek(page)).withering ?? 0)
    if (await drew(page, 'JokerDisabled') && dark > 0) break
    await pass(page, 40)
  }
  check(await drew(page, 'JokerDisabled'), '조커가 꺼지는 것이 그려집니다')
  check(dark > 0, '꺼지는 딱지가 실제로 시듭니다')

  console.log((await peek(page)).beats?.slice(-12).join(' · ') ?? '없음')
  console.log(bad === 0 ? '모두 통과' : `${bad}건 어긋납니다`)

  await browser.close()
  await server.close()
  return bad === 0 ? 0 : 1
}

main().then(code => process.exit(code))
