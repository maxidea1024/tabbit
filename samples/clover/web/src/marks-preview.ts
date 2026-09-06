// 카드에 붙는 표시를 한 번에 봅니다.
//
// **판에서는 한 판에 한둘씩만 만납니다.** 강화 8종 · 인장 4종 · 덧붙은 칩 · 무력화가 서로
// 겹치는 자리에 앉는데, 그 조합을 판에서 만나려면 소모품을 몇 판어치 써야 합니다 — 표시를
// 고치는 동안 눈으로 볼 자리가 여기입니다.
//
//     npm run dev   →  /marks.html
//
// **판에서 쓰는 그 `CardView` 입니다.** 흉내 낸 그림을 세우면 여기서 맞아 보이는 것이 판에서
// 어긋나므로, 종이 · 얼굴 · 칩 · 인장이 같은 코드에서 나와야 합니다.

import { Application, Container, Text } from 'pixi.js'

import { loadFromUrl } from './core/load'
import { LANGUAGES, setLanguage, useStrings, type Language } from './core/strings'
import type { CardInstance } from './core/state'
import { EditionKind } from './generated/enums/edition-kind'
import { EnhancementKind } from './generated/enums/enhancement-kind'
import { RankKind } from './generated/enums/rank-kind'
import { SealKind } from './generated/enums/seal-kind'
import { SuitKind } from './generated/enums/suit-kind'
import { setCardSet, setLookOf } from './render/card-set'
import { CardView } from './render/card-view'
import { COLOR, SIZE } from './render/theme'

/** 강화 8종. `EnhancementKind` 의 차례입니다. */
const ENHANCEMENTS: [EnhancementKind, string][] = [
  [EnhancementKind.Bonus, 'Bonus'],
  [EnhancementKind.Mult, 'Mult'],
  [EnhancementKind.Wild, 'Wild'],
  [EnhancementKind.Glass, 'Glass'],
  [EnhancementKind.Steel, 'Steel'],
  [EnhancementKind.Stone, 'Stone'],
  [EnhancementKind.Gold, 'Gold'],
  [EnhancementKind.Lucky, 'Lucky'],
]

const SEALS: [SealKind, string][] = [
  [SealKind.Red, 'Red'],
  [SealKind.Blue, 'Blue'],
  [SealKind.Gold, 'Gold'],
  [SealKind.Purple, 'Purple'],
]

let uid = 0

function cardOf(over: Partial<CardInstance>): CardInstance {
  uid += 1
  return {
    uid,
    baseCardId: 'preview',
    rank: RankKind.King,
    suit: SuitKind.Spade,
    enhancement: EnhancementKind.None,
    seal: SealKind.None,
    edition: EditionKind.Base,
    bonusChips: 0,
    debuffed: false,
    faceDown: false,
    ...over,
  }
}

function heading(text: string, x: number, y: number): Text {
  const label = new Text({
    text, style: { fontSize: 13, fill: COLOR.inkDim, fontWeight: '700' },
  })
  label.position.set(x, y)
  return label
}

function view(card: CardInstance): CardView {
  const one = new CardView(card)
  one.set(card)
  return one
}

/**
 * 한 줄. 위에 무리의 이름을 적고 그 아래로 카드를 세웁니다.
 *
 * **`CardView` 의 자리는 가운데입니다** — 판에서 돌고 뒤집히는 것이라 피벗이 가운데이고,
 * 왼쪽 위로 알고 놓으면 반 장씩 왼쪽으로 밀립니다.
 */
function row(world: Container, y: number, title: string, cards: CardInstance[]): number {
  world.addChild(heading(title, 24, y))
  cards.forEach((card, index) => {
    const one = view(card)
    one.position.set(24 + SIZE.cardWidth / 2 + index * (SIZE.cardWidth + 12),
                     y + 22 + SIZE.cardHeight / 2)
    world.addChild(one)
  })
  return y + 22 + SIZE.cardHeight + 26
}

async function main(): Promise<void> {
  const data = await loadFromUrl('./data')
  useStrings(data)
  // **말을 바꿔 볼 수 있어야 합니다.** 칩의 넓이를 정하는 것이 글이고, 한국어의 「+칩」이
  // 가장 짧습니다 — `?lang=de` 가 가장 긴 쪽입니다.
  const asked = new URLSearchParams(location.search).get('lang') ?? 'ko'
  setLanguage((LANGUAGES as readonly string[]).includes(asked) ? asked as Language : 'ko')
  setCardSet(setLookOf(data, 'classic'))

  const app = new Application()
  await app.init({
    canvas: document.getElementById('stage') as HTMLCanvasElement,
    background: 0x0e1420,
    antialias: true,
    resolution: Math.min(3, window.devicePixelRatio || 1),
    autoDensity: true,
    resizeTo: window,
    preference: 'webgl',
  })

  const world = new Container()
  app.stage.addChild(world)

  let y = 24
  y = row(world, y, '강화 8종',
    ENHANCEMENTS.map(([kind]) => cardOf({ enhancement: kind })))
  y = row(world, y, '강화 + 인장',
    SEALS.map(([seal], at) => cardOf({
      enhancement: ENHANCEMENTS[at][0], seal,
    })))
  y = row(world, y, '덧붙은 칩 · 무력화 · 뒷면', [
    cardOf({ enhancement: EnhancementKind.Bonus, bonusChips: 30 }),
    cardOf({ enhancement: EnhancementKind.Gold, bonusChips: 30, seal: SealKind.Gold }),
    cardOf({ enhancement: EnhancementKind.Mult, debuffed: true }),
    cardOf({ enhancement: EnhancementKind.Steel, debuffed: true, bonusChips: 10 }),
    cardOf({ bonusChips: 30 }),
    cardOf({ enhancement: EnhancementKind.Glass, faceDown: true }),
  ])
  row(world, y, '강화 없음 · 인장만',
    SEALS.map(([seal]) => cardOf({ seal })))
}

void main()
