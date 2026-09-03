// 칩 × 배수의 불. **바탕 위에 붙어 타는지, 자기 반을 넘지 않는지 봅니다.**
import * as path from 'path'
import { fileURLToPath } from 'url'
import { chromium } from 'playwright'
import { createServer } from 'vite'
import { chooseFive, openRun, peek, pickCards, skipLogin } from './harness'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const OUT = path.resolve(HERE, '../../design-data/out/check')
const PORT = 5216

async function main(): Promise<number> {
  const server = await createServer({ root: path.resolve(HERE, '..'), server: { port: PORT } })
  await server.listen()
  const browser = await chromium.launch()
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } })
  await skipLogin(page)
  await page.goto(`http://localhost:${PORT}/?seed=CLOVER-FIRE1`, { waitUntil: 'networkidle' })
  await page.waitForTimeout(1500)

  await openRun(page)

  // **실제 값으로 봅니다.** 0 으로는 자릿수가 늘 때 어떻게 보이는지 알 수 없습니다.
  const held = await peek(page)
  await pickCards(page, chooseFive(held.hand))
  await page.waitForTimeout(400)

  for (const [index, heat] of [0.35, 0.7, 1].entries()) {
    await page.evaluate(hot => {
      const hook = (window as unknown as {
        __clover: { setFever?(v: number): void }
      }).__clover
      hook.setFever?.(hot)
    }, heat)
    await page.waitForTimeout(220)
    await page.screenshot({ path: path.join(OUT, `fire-${index + 1}.png`) })
  }

  await browser.close()
  await server.close()
  return 0
}

main().then(code => process.exit(code))
