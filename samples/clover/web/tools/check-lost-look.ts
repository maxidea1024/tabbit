// 진 판의 판이 선 채로 **구운 화면이 살아 있는 화면과 같은가.**
//
// `check-shot.ts` 는 상점에서 지워짐 5%에 견주므로 재의 첫 알갱이가 이미 섞여 있고, 그래서
// 문턱이 필요합니다. **여기는 전환을 돌리지 않고 시계를 멈춘 채로 견줍니다** — 두 그림이
// 같은 시각이므로 다른 픽셀은 곧 빠진 것입니다.
//
// **창의 비율마다 봅니다.** 판은 1280 × 800 하나에 맞춰 그려지고 창 가운데에 놓이므로, 창의
// 비율이 그 비율이 아니면 판의 왼쪽 위 모서리가 (0, 0) 에서 떨어집니다. 굽는 것은 그 떨어진
// 만큼을 옮기며 굽는 일이고, **옮기며 굽는 자리에서만 드러나는 결함이 있습니다** — 규격은
// `doc/ui/transition.md` 의 「구울 그림의 필터 사각형」 입니다. 기준 비율 하나로는 그 자리를
// 한 번도 지나지 않습니다.
//
// **판이 걸린 딱지를 세웁니다.** 맨 딱지는 셰이더가 걸리지 않으므로 필터의 사각형을 재는
// 길을 지나지 않습니다 — 이 둘(비율과 에디션)이 함께여야 재현됩니다.
//
// **그림을 놓아 보는 것도 여기서 합니다.** 그림은 상한(96MB)이 넘치면 오래된 것부터 놓이고
// 두 틱 뒤에 버려집니다 — 그 사이에 다시 그리지 않은 쪽은 버려진 그림을 가리킨 채로 남고,
// 그리는 그 자리에서 프레임이 예외로 죽습니다. **예외는 조용히 삼켜지므로 화면에는 카드가
// 갑자기 사라진 것으로 보입니다.** 진 판의 판이 그런 자리였습니다 — 한 번 세우고 다시
// 세우지 않으므로 `refresh` 가 닿지 않습니다. 그것은 기준 비율에서 한 번만 봅니다.
//
//     npx tsx tools/check-lost-look.ts
import * as fs from 'fs/promises'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium, type Browser, type Page } from 'playwright'
import { createServer } from 'vite'
import { openRun, pass, peek, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check/lost')
const PORT = 5296
/** 두 그림에서 이만큼 다르면 다른 픽셀로 셉니다. 0..255 */
const APART = 30
/** 크게 다른 덩어리가 몇 몫까지 괜찮은가. **시계가 멈춰 있으므로 0에 가깝습니다.** */
const ALLOW = 0.0005

/**
 * 재는 비율들.
 *
 * **기준 비율은 판의 모서리가 (0, 0) 인 유일한 창입니다.** 나머지 넷은 좌우가 남는 것과
 * 위아래가 남는 것을 함께 봅니다 — 갤럭시 폴드는 접으면 2.58, 펴면 1.25 이고 폰을 가로로
 * 쥐면 2.17 입니다. `check-crop.ts` 와 같은 목록입니다.
 *
 * **픽셀 밀도는 1로 둡니다.** 이 도구가 재는 것은 필터의 사각형이고 그것은 밀도와 무관합니다 —
 * 밀도 2의 길은 `check-shot.ts` 의 핸드폰 몫이 지납니다. 밀도를 올리면 구운 그림이 잘라 낸
 * 자리의 두 배 크기가 되어 두 그림의 크기가 어긋납니다.
 */
const SIZES = [
  { name: '기준 16:10', width: 1280, height: 800 },
  { name: '넓은 2:1', width: 1600, height: 800 },
  { name: '폰 가로 2.17', width: 1170, height: 540 },
  { name: '폴드 접음 2.58', width: 1240, height: 480 },
  { name: '세로 9:19.5', width: 540, height: 1170 },
]

let failed = 0
function check(ok: boolean, what: string): void {
  console.log(`  ${ok ? '통과' : '실패'}  ${what}`)
  if (!ok) failed++
}

/** 크게 다른 자리의 몫. **브라우저가 셉니다** — PNG 해독기가 거기 있습니다. */
async function apart(page: Page, a: string, b: string): Promise<number> {
  return await page.evaluate(`(async () => {
    const pull = async src => {
      const blob = await (await fetch('data:image/png;base64,' + src)).blob()
      const bitmap = await createImageBitmap(blob)
      const board = new OffscreenCanvas(bitmap.width, bitmap.height)
      board.getContext('2d').drawImage(bitmap, 0, 0)
      return board.getContext('2d').getImageData(0, 0, bitmap.width, bitmap.height)
    }
    const one = await pull(${JSON.stringify(a)})
    const two = await pull(${JSON.stringify(b)})
    if (one.width !== two.width || one.height !== two.height) return 1
    const w = one.width, h = one.height
    const far = new Uint8Array(w * h)
    for (let p = 0; p < w * h; p++) {
      const i = p * 4
      const gap = (Math.abs(one.data[i] - two.data[i])
        + Math.abs(one.data[i + 1] - two.data[i + 1])
        + Math.abs(one.data[i + 2] - two.data[i + 2])) / 3
      far[p] = gap > ${APART} ? 1 : 0
    }
    // 이웃까지 다른 자리만. **선은 빠지고 덩어리만 남습니다** — 구운 그림은 다중 표본을
    // 쓰지 않으므로 획의 가장자리가 1픽셀씩 다르고, 그것을 세면 멀쩡한 화면도 0.3%입니다.
    const R = 3
    let many = 0
    for (let y = R; y < h - R; y++) {
      for (let x = R; x < w - R; x++) {
        const p = y * w + x
        if (!far[p]) continue
        if (far[p - R] && far[p + R] && far[p - R * w] && far[p + R * w]) many++
      }
    }
    return many / (w * h)
  })()`) as number
}

/** 잘라 낸 자리. 살아 있는 화면을 이 사각형만 찍어야 구운 그림과 크기가 같습니다. */
interface Crop {
  box?: [number, number, number, number]
}

/**
 * 창 하나에서 판을 세우고 두 그림을 견줍니다.
 *
 * `deep` 이면 그림을 놓아 보는 것까지 합니다 — 기준 비율에서 한 번입니다.
 */
async function measure(browser: Browser, size: typeof SIZES[number],
                       deep: boolean): Promise<void> {
  const tag = `${size.name} ${size.width}x${size.height}`
  const slug = `${size.width}x${size.height}`
  const page = await browser.newPage({ viewport: { width: size.width, height: size.height } })
  await skipLogin(page)
  const blew: string[] = []
  page.on('pageerror', error => blew.push(error.message))
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-ASH&tick=manual`,
                  { waitUntil: 'networkidle' })
  await pass(page, 1500)
  await openRun(page)
  // 판이 걸린 것 셋. **맨 딱지만으로는 에디션 셰이더의 길을 지나지 않습니다.**
  const many = Number(process.argv[2] ?? 3)
  await page.evaluate(`window.__clover.grantJoker(${many}, 1)`)
  await pass(page, 900)
  await page.evaluate(`window.__clover.loseRound()`)
  for (let i = 0; i < 200; i++) {
    if ((await peek(page)).gameOver) break
    await pass(page, 60)
  }
  await pass(page, 1800)

  const now = await peek(page)
  check(now.gameOver === true,
    `${tag} · 진 판의 판이 섰습니다 · 조커 ${now.jokers}개 · 국면 ${now.phase}`)

  // **시계를 멈춘 채로 굽습니다.** `?tick=manual` 이므로 `pass` 를 부르지 않는 동안
  // 화면은 한 프레임에 멈춰 있고, 두 그림이 같은 시각의 것입니다.
  const holes = await page.evaluate(`window.__clover.shotHoles()`) as
    { holes: number; total: number }
  check(holes.holes === 0,
    `${tag} · 구운 화면에 빈 자리가 없습니다 (${holes.holes} / ${holes.total})`)

  if (deep) {
    // **그림을 다 놓아 봅니다.** 상한이 넘치면 실제로 이 길입니다 — 놓인 그림을 쓰고 있던
    // 쪽이 다시 그리지 않으면 그 카드는 빈 채로 남습니다.
    await page.screenshot({ path: path.join(OUT, 'before-drop.png') })
    const dropped = await page.evaluate(`window.__clover.dropArt()`) as number
    // **틱 자체가 죽을 수 있습니다.** 버려진 그림을 그리는 그 자리에서 예외가 나므로, 여기서
    // 받아 두지 않으면 도구가 판정 대신 스택을 뱉고 끝납니다.
    for (let i = 0; i < 12; i++) {
      try {
        await pass(page, 200)
      } catch (error) {
        blew.push(String(error).split(/\r?\n/)[0])
        break
      }
    }
    await page.screenshot({ path: path.join(OUT, 'after-drop.png') })
    check(blew.length === 0,
      `${tag} · 그림 ${dropped}장을 놓아도 판이 성합니다`
      + (blew.length > 0 ? ` — ${blew[0]}` : ''))
  }

  // **살아 있는 화면은 잘라 낸 자리만 찍습니다.** 구운 그림이 그 사각형이므로, 창 전체를
  // 찍으면 크기가 달라 견줄 것이 없습니다.
  const crop = await page.evaluate(`window.__clover.cropRegion()`) as Crop
  const box = crop.box
  check(box !== undefined, `${tag} · 잘라 낸 자리를 알립니다`)
  if (!box) {
    await page.close()
    return
  }
  const live = (await page.screenshot({
    path: path.join(OUT, `live-${slug}.png`),
    clip: { x: box[0], y: box[1], width: box[2], height: box[3] },
  })).toString('base64')
  const shot = await page.evaluate(`window.__clover.shotDump()`) as string
  await fs.writeFile(path.join(OUT, `shot-${slug}.png`),
                     Buffer.from(shot.split(',')[1], 'base64'))
  const share = await apart(page, live, shot.split(',')[1])
  check(share <= ALLOW,
    `${tag} · 구운 화면이 살아 있는 화면과 같습니다`
    + ` (다른 픽셀 ${(share * 100).toFixed(2)}% · 잘라 낸 자리 ${box.join(',')})`)

  await page.close()
}

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  await fs.mkdir(OUT, { recursive: true })
  const browser = await chromium.launch(process.argv.includes('--gpu') ? { headless: false } : {})
  for (const [at, size] of SIZES.entries()) await measure(browser, size, at === 0)
  await browser.close()
  await server.close()
  return failed === 0 ? 0 : 1
}

main().then(code => process.exit(code))
