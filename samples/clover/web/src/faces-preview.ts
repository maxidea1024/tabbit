// 그림 없이 그린 얼굴 52장을 한 번에 봅니다.
//
// **게임 안에서는 한 번에 넉 장쯤만 보입니다.** 손패가 여덟이고 그중 절반은 겹쳐 있으므로,
// 랭크 13종이 한 결로 보이는지는 판에서 확인이 되지 않습니다 — 그린 얼굴을 고치는 동안
// 눈으로 볼 자리가 여기입니다.
//
//     npm run dev   →  /faces.html
//
// **큰 것과 작은 것을 함께 세웁니다.** 손패의 크기에서 뭉개지는 것은 큰 그림에서 보이지
// 않고, 큰 그림에서 엉성한 것은 작은 그림에서 보이지 않습니다.

import { Application, Container, Graphics, Text } from 'pixi.js'

import { SuitKind } from './generated/enums/suit-kind'
import { cornerSize, drawFace, drawSuit } from './render/pips'
import { COLOR, SIZE } from './render/theme'

/** 왼쪽에서 오른쪽으로. 랭크의 순서입니다. */
const RANKS = [2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14]
const RANK_TEXT: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A',
}

/** 두 벌의 색. 시트의 `CardSetSuit` 와 같은 값입니다. */
const INK: Record<string, Record<number, number>> = {
  line: {
    [SuitKind.Spade]: 0x1f2024,
    [SuitKind.Heart]: 0xd7343f,
    [SuitKind.Club]: 0x1f2024,
    [SuitKind.Diamond]: 0xd7343f,
  },
  four_color: {
    [SuitKind.Spade]: 0x1f2024,
    [SuitKind.Heart]: 0xd7343f,
    [SuitKind.Club]: 0x2f8b52,
    [SuitKind.Diamond]: 0x2f6fc0,
  },
}

/** 카드 한 장. 판에서 그리는 것과 같은 순서입니다 — 종이 · 얼굴 · 모서리. */
function card(suit: SuitKind, rank: number, ink: number, scale: number): Container {
  const node = new Container()
  const w = SIZE.cardWidth
  const h = SIZE.cardHeight

  const paper = new Graphics()
  paper.roundRect(0, 0, w, h, SIZE.cardRadius).fill(COLOR.cardFace)
  paper.roundRect(3, 3, w - 6, h - 6, SIZE.cardRadius - 3)
    .stroke({ color: 0xffffff, width: 1, alpha: 0.5 })
  paper.roundRect(0.5, 0.5, w - 1, h - 1, SIZE.cardRadius)
    .stroke({ color: COLOR.cardEdge, width: 2 })
  node.addChild(paper)

  const face = new Graphics()
  drawFace(face, suit, rank, w, h, ink)
  drawSuit(face, suit, 14, 33, 12, ink)
  drawSuit(face, suit, w - 14, h - 33, 12, ink, true)
  node.addChild(face)

  const label = RANK_TEXT[rank] ?? '?'
  const top = new Text({
    text: label,
    style: { fontSize: cornerSize(label, 19), fill: ink, fontWeight: '800' },
  })
  top.position.set(8, 5)
  node.addChild(top)

  const bottom = new Text({
    text: label,
    style: { fontSize: cornerSize(label, 19), fill: ink, fontWeight: '800' },
  })
  bottom.anchor.set(1, 1)
  bottom.position.set(w - 8, h - 5)
  node.addChild(bottom)

  node.scale.set(scale)
  return node
}

function heading(text: string, x: number, y: number): Text {
  const node = new Text({
    text,
    style: { fontSize: 13, fill: COLOR.good, fontWeight: '800', letterSpacing: 1 },
  })
  node.position.set(x, y)
  return node
}

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

  const world = new Container()
  app.stage.addChild(world)

  const small = 0.78
  const stepX = SIZE.cardWidth * small + 6
  const stepY = SIZE.cardHeight * small + 24

  // 1. 손패의 크기로 52장. **한 결로 보이는지를 여기서 봅니다.**
  let y = 34
  world.addChild(heading('그린 얼굴 52장 — 손패 크기', 30, 14))
  for (const suit of [SuitKind.Spade, SuitKind.Heart, SuitKind.Club, SuitKind.Diamond]) {
    RANKS.forEach((rank, index) => {
      const one = card(suit, rank, INK.line[suit], small)
      one.position.set(30 + index * stepX, y)
      world.addChild(one)
    })
    y += stepY
  }

  // 2. 그림 카드와 에이스를 크게. **작은 그림에서 보이지 않는 엉성함이 여기서 보입니다.**
  y += 10
  world.addChild(heading('그림 카드와 에이스 — 2.1배', 30, y - 20))
  const big = 2.1
  let x = 30
  for (const rank of [11, 12, 13, 14]) {
    const one = card(SuitKind.Spade, rank, INK.line[SuitKind.Spade], big)
    one.position.set(x, y)
    world.addChild(one)
    x += SIZE.cardWidth * big + 12
  }

  // 3. 4색. **클럽과 다이아만 색이 다릅니다** — 그 둘이 검정·빨강과 갈리는지를 봅니다.
  x += 30
  world.addChild(heading('4색 — 클럽과 다이아', x, y - 20))
  for (const suit of [SuitKind.Club, SuitKind.Diamond]) {
    for (const rank of [7, 13]) {
      const one = card(suit, rank, INK.four_color[suit], big)
      one.position.set(x, y)
      world.addChild(one)
      x += SIZE.cardWidth * big + 12
    }
  }

  // 창이 작으면 통째로 줄입니다. 늘리지는 않습니다 — 손패 크기가 손패 크기여야 합니다.
  const fit = () => {
    const wide = 30 + 13 * stepX + 30
    const tall = y + SIZE.cardHeight * big + 40
    const ratio = Math.min(1, window.innerWidth / wide, window.innerHeight / tall)
    world.scale.set(ratio)
  }
  fit()
  window.addEventListener('resize', fit)
}

void main()
