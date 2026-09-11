// 판이 모래로 삭는 몇 프레임을 굽습니다.
//
// **판에서는 0.85초에 지나갑니다.** 소멸선의 톱니가 굵은지 · 칸이 꺼지는 프레임과 알갱이가
// 뜨는 프레임이 같은지 · 알갱이가 아래로 흩어지는지는 세워 두고 봐야 갈립니다 —
// `/erode.html` 이 손으로 끄는 자리이고, 이 도구는 그 자리를 정해진 시각에 굽습니다.
import * as path from 'path'
import { fileURLToPath } from 'url'

import { chromium } from 'playwright'
import { createServer } from 'vite'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = process.env.SHOT_OUT ?? path.resolve(HERE, '../../design-data/out/check')
const PORT = 5231

/** 초 단위로 굽는 자리. */
const MARKS = [0.1, 0.3, 0.5, 0.7, 0.85, 1.2, 1.8, 2.4]
const STEP = 0.02

async function main(): Promise<void> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({
    viewport: { width: 1280, height: 820 }, deviceScaleFactor: 2,
  })
  page.on('console', one => {
    if (one.type() === 'error' || one.text().includes('motes')) console.log('[page]', one.text())
  })
  page.on('pageerror', one => console.log('[error]', one.message))
  await page.goto(`http://localhost:${PORT}/erode.html`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1800)

  await page.keyboard.press('Space')
  await page.keyboard.press('KeyR')
  let at = 0
  for (const mark of MARKS) {
    const steps = Math.round((mark - at) / STEP)
    for (let i = 0; i < steps; i++) await page.keyboard.press('ArrowRight')
    at = mark
    await page.waitForTimeout(120)
    await page.screenshot({ path: path.join(OUT, `erode-${mark.toFixed(2)}.png`) })
    console.log('구움', mark.toFixed(2))
  }

  await browser.close()
  await server.close()
}

void main()
