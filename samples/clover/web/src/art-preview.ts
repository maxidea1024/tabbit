// 그림이 들어온 것을 눈으로 봅니다.
//
// **202장을 한 번에 만들지 않습니다.** 그러므로 있는 것과 없는 것이 섞인 상태가 정상이고,
// 그 상태에서 화면이 어떻게 보이는지를 여기서 확인합니다 — 그림이 있는 것은 그림이고 없는
// 것은 문양입니다.
//
//     npm run dev   →  /artcheck.html

import { Application, Container, Sprite, Text, Texture } from 'pixi.js'

import { loadFromUrl } from './core/load'
import { loadArtIndex, onArtReady } from './render/art'
import { BackgroundFilter } from './shader/background'
import { JokerView } from './render/joker-view'
import { COLOR, SIZE } from './render/theme'
import type { JokerInstance } from './core/state'

const COLUMNS = 10
const ROWS = 4

async function main(): Promise<void> {
  const app = new Application()
  await app.init({
    canvas: document.getElementById('stage') as HTMLCanvasElement,
    background: COLOR.ground,
    antialias: true,
    resolution: Math.min(3, window.devicePixelRatio || 1),
    autoDensity: true,
    resizeTo: window,
    preference: 'webgl',
  })

  const data = await loadFromUrl('./data')
  const have = await loadArtIndex('./art')

  const background = new BackgroundFilter()
  background.setMood([0.042, 0.052, 0.086], [0.30, 0.52, 0.98])
  const sheet = new Sprite(Texture.WHITE)
  sheet.filters = [background]
  app.stage.addChild(sheet)

  const world = new Container()
  app.stage.addChild(world)

  const heading = new Text({
    text: `조커 ${data.tables.joker.records.length}종 중 ${COLUMNS * ROWS}종`
      + `   ·   그림이 있는 것 ${have}개`,
    style: { fontSize: 18, fill: COLOR.ink, fontWeight: '800' },
  })
  heading.position.set(24, 20)
  world.addChild(heading)

  // 그림이 있는 것을 앞에 세워 한 화면에서 견줄 수 있게 합니다.
  const rows = [...data.tables.joker.records]
  const views: JokerView[] = []

  const build = () => {
    for (const view of views) view.destroy()
    views.length = 0

    rows.slice(0, COLUMNS * ROWS).forEach((row, index) => {
      const joker: JokerInstance = {
        uid: index, jokerId: row.jokerId, edition: 0 as never, sticker: 0 as never,
        counters: {
          chips: 0, multAdd: 0, multMul: 10_000, money: 0,
          sellValue: 0, charge: 0, tick: 0,
        },
        age: 0, disabled: false,
      }
      const view = new JokerView(joker, {
        name: row.name, rarity: row.rarity, lines: [],
      })
      view.motion.snap(
        70 + (index % COLUMNS) * 108,
        110 + Math.floor(index / COLUMNS) * 148)
      world.addChild(view)
      views.push(view)
    })
  }

  build()
  onArtReady(() => build())

  const layout = () => {
    const width = app.renderer.screen.width
    const height = app.renderer.screen.height
    const scale = Math.min(width / 1140, height / 700)
    world.scale.set(scale)
    world.position.set(
      Math.round((width - 1140 * scale) / 2), Math.round((height - 700 * scale) / 2))
    sheet.width = width
    sheet.height = height
    background.setAspect(width / Math.max(1, height))
  }
  layout()
  window.addEventListener('resize', layout)

  let clock = 0
  app.ticker.add(ticker => {
    const seconds = ticker.deltaMS / 1000
    clock += seconds
    background.advance(seconds)
    for (const view of views) view.advance(seconds, clock)
  })

  void SIZE
}

main().catch((error: unknown) => {
  console.error(error)
})
