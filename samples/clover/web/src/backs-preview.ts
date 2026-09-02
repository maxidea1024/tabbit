// 덱 15종의 뒷면을 나란히 봅니다.
//
// **게임 안에서는 확인이 되지 않습니다** — 뒷면은 덱이 정하고 덱은 판을 시작할 때 하나만
// 고르므로, 열다섯을 보려면 판을 열다섯 번 시작해야 합니다.
//
//     npm run dev   →  /backs.html
//
// 색과 무늬는 게임과 같은 `Deck` 표를 읽으므로, 여기서 맞으면 게임에서도 맞습니다.
// **큰 것과 작은 것을 함께 세웁니다** — 선화는 작아질 때 뭉개지고, 뭉개지는 것은 큰
// 그림에서 보이지 않습니다.

import { Application, Container, Text } from 'pixi.js'

import { loadFromUrl } from './core/load'
import { backLookOf, drawCardBack } from './render/card-back'
import { COLOR, SIZE } from './render/theme'

const BIG = 2.1
const COLS = 5

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
  const world = new Container()
  app.stage.addChild(world)

  // 큰 것 옆에 작은 것이 서므로 칸의 너비는 둘을 합친 것입니다. 하나만 세면 작은 쪽이
  // 옆 칸을 덮습니다.
  const cellW = SIZE.cardWidth * BIG + SIZE.cardWidth + 46
  const cellH = SIZE.cardHeight * BIG + 74

  data.tables.deck.records.forEach((row, index) => {
    const col = index % COLS
    const line = Math.floor(index / COLS)
    const x = 40 + col * cellW
    const y = 30 + line * cellH

    const look = backLookOf(row)

    const big = new Container()
    big.position.set(x, y)
    big.scale.set(BIG)
    drawCardBack(big, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius, look)
    world.addChild(big)

    // 손패의 크기 그대로. **여기서 뭉개지면 게임에서 뭉개집니다.**
    const small = new Container()
    small.position.set(x + SIZE.cardWidth * BIG + 16, y + SIZE.cardHeight * (BIG - 1))
    drawCardBack(small, SIZE.cardWidth, SIZE.cardHeight, SIZE.cardRadius, look)
    world.addChild(small)

    const label = new Text({
      text: `${row.name}  ·  ${row.back}`,
      style: { fontSize: 14, fill: COLOR.ink, fontWeight: '800' },
    })
    label.position.set(x, y + SIZE.cardHeight * BIG + 10)
    world.addChild(label)
  })

  const fit = (): void => {
    const wide = 40 + COLS * cellW
    const tall = 30 + Math.ceil(data.tables.deck.records.length / COLS) * cellH
    const scale = Math.min(app.screen.width / wide, app.screen.height / tall, 1)
    world.scale.set(scale)
    world.position.set((app.screen.width - wide * scale) / 2,
                       (app.screen.height - tall * scale) / 2)
  }
  fit()
  app.renderer.on('resize', fit)
}

void main()
