// 에디션 셰이더를 나란히 봅니다.
//
// **게임 안에서는 확인이 되지 않습니다** — 에디션은 상점 추첨으로만 붙으므로, 홀로그래픽
// 하나를 눈으로 보려고 여러 판을 돌려야 합니다. 이 페이지는 다섯 가지를 한 줄에 세웁니다.
//
//     npm run dev   →  /editions.html
//
// 파라미터는 게임과 같은 `EditionVisual` 을 읽으므로, 여기서 맞으면 게임에서도 맞습니다.

import { Application, Container, Sprite, Text, Texture } from 'pixi.js'

import { EditionKind } from './generated/enums/edition-kind'
import { loadFromUrl } from './core/load'
import { BackgroundFilter } from './shader/background'
import { loadArtIndex, onArtReady } from './render/art'
import { JokerView } from './render/joker-view'
import { CardView, type EditionLook } from './render/card-view'
import { COLOR } from './render/theme'
import type { CardInstance, JokerInstance } from './core/state'

const NAMES: Record<number, string> = {
  [EditionKind.Base]: '기본',
  [EditionKind.Foil]: '포일',
  [EditionKind.Holographic]: '홀로그래픽',
  [EditionKind.Polychrome]: '폴리크롬',
  [EditionKind.Negative]: '네거티브',
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

  const data = await loadFromUrl('./data')
  // **그림을 먼저 읽습니다.** 없으면 조커가 문양 하나로 그려지고, 에디션 셰이더가 무엇에
  // 걸리는지가 이 그림의 요점인데 그 무엇이 빈 자리가 됩니다.
  await loadArtIndex()

  const background = new BackgroundFilter()
  const sheet = new Sprite(Texture.WHITE)
  sheet.filters = [background]
  app.stage.addChild(sheet)

  const world = new Container()
  app.stage.addChild(world)

  // 게임의 기본 색조와 같게 둡니다.
  background.setMood([0.042, 0.052, 0.086], [0.30, 0.52, 0.98])

  const look = (edition: EditionKind): EditionLook | undefined => {
    const row = data.tables.editionVisual.findByEdition(edition)
    if (!row || row.shader === 'none') return undefined
    return {
      shader: row.shader as EditionLook['shader'],
      strength: row.strength, flowSpeed: row.flowSpeed, noise: row.noise,
    }
  }

  const jokerRow = data.tables.joker.records[0]
  const cardRow = data.tables.baseDeckCard.records[12]
  const editions = [
    EditionKind.Base, EditionKind.Foil, EditionKind.Holographic,
    EditionKind.Polychrome, EditionKind.Negative,
  ]

  const jokers: JokerView[] = []
  const cards: CardView[] = []

  editions.forEach((edition, index) => {
    const x = 150 + index * 200

    const joker: JokerInstance = {
      uid: index, jokerId: jokerRow.jokerId, edition: edition as never,
      sticker: 0 as never,
      counters: { chips: 0, multAdd: 0, multMul: 10_000, money: 0, sellValue: 0, charge: 0, tick: 0 },
      age: 0, disabled: false,
    }
    const dress = {
      name: jokerRow.name, rarity: jokerRow.rarity, lines: [], edition: look(edition),
    }
    const view = new JokerView(joker, dress)
    // **그림은 목록을 읽은 뒤에도 한 박자 늦게 옵니다.** 그때 다시 그리지 않으면 카드가
    // 빈 채로 남습니다.
    onArtReady(() => view.set(joker, dress))
    view.motion.snap(x, 190)
    world.addChild(view)
    jokers.push(view)

    const card: CardInstance = {
      uid: 100 + index,
      baseCardId: cardRow.cardId, rank: cardRow.rank, suit: cardRow.suit,
      enhancement: 0 as never, seal: 0 as never, edition: edition as never,
      bonusChips: 0, debuffed: false, faceDown: false,
    }
    const cardView = new CardView(card, look(edition))
    cardView.placeNow(x, 400)
    cardView.idle = 0
    world.addChild(cardView)
    cards.push(cardView)

    const label = new Text({
      text: NAMES[edition] ?? String(edition),
      style: { fontSize: 15, fill: COLOR.ink, fontWeight: '800' },
    })
    label.anchor.set(0.5, 0)
    label.position.set(x, 500)
    world.addChild(label)
  })

  const heading = new Text({
    text: '에디션 셰이더 — 조커와 카드',
    style: { fontSize: 20, fill: COLOR.ink, fontWeight: '800' },
  })
  heading.anchor.set(0.5, 0)
  heading.position.set(560, 50)
  world.addChild(heading)

  const layout = () => {
    const width = app.renderer.screen.width
    const height = app.renderer.screen.height
    const scale = Math.min(width / 1120, height / 580)
    world.scale.set(scale)
    world.position.set(
      Math.round((width - 1120 * scale) / 2), Math.round((height - 580 * scale) / 2))
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
    for (const view of jokers) view.advance(seconds, clock)
    for (const view of cards) view.advance(seconds, clock)
  })
}

main().catch((error: unknown) => {
  console.error(error)
})
