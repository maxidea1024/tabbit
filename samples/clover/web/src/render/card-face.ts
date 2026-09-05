// 카드의 앞면.
//
// **한 번 그린 것은 그림으로 남깁니다.** 앞면 하나가 채우기·선 명령 수십 개에 글 둘입니다 —
// 10♣ 한 장이 약 48개이고, 그 위에 모서리의 랭크가 `Text` 둘로 얹힙니다. 그런데 앞면을
// 정하는 것은 「무늬 · 랭크 · 종이색 · 디버프」 넷뿐이고, 그 넷이 같으면 그림도 같습니다.
//
// 다시 그리는 자리가 잦습니다. `CardView.set()` 이 `refresh` 마다 손패 전부에 불리고,
// `refresh` 를 부르는 자리가 25곳입니다 — 카드 한 장을 고르는 것도 그중 하나입니다. 그때마다
// 여덟 장의 벡터를 다시 삼각화하고 글 16개를 다시 구울 이유가 없습니다.
//
// 뒷면(`card-back.ts`)과 같은 얼개입니다. 렌더러를 받기 전(타이틀 · 미리보기 도구)에는
// 선으로 그립니다.

import {
  Container, Graphics, Rectangle, Sprite, Text, type Renderer, type Texture,
} from 'pixi.js'

import type { SuitKind } from '../generated/enums/suit-kind'
import { artFor } from './art'
import { cardArtDir, drawsIndex } from './card-set'
import { cardArtId, cornerSize, drawFace, drawSuit } from './pips'
import { COLOR } from './theme'

/** 모서리에 적히는 랭크. */
const RANK_TEXT: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A',
}

/** 모서리 랭크의 기준 크기. 두 글자(`10`)는 `cornerSize` 가 줄입니다. */
const CORNER_SIZE = 19

/**
 * 앞면 하나가 정해지는 것.
 *
 * **이 넷이 열쇠입니다.** 강화는 종이색으로, 석재는 `stone` 으로 들어옵니다 — 강화의 이름
 * 딱지와 인장과 칩 딱지는 여기 없습니다. 그것들은 말과 상태를 타므로 그리는 쪽에 남습니다.
 */
export interface FaceLook {
  suit: SuitKind
  rank: number
  /** 종이의 색. 강화가 정하거나 세트의 것입니다. */
  paper: number
  /** 디버프. 종이를 덮고 무늬와 테두리를 회색으로 만듭니다. */
  debuffed: boolean
  /** 석재. **랭크도 무늬도 없습니다** — 돌 하나입니다. */
  stone: boolean
}

/** 디버프된 카드의 무늬 색. */
const DEBUFF_INK = 0x9a9a9a
/** 디버프된 카드의 테두리 색. */
const DEBUFF_EDGE = 0x6b6b6b

/**
 * 이 앞면의 그림.
 *
 * **없으면 `undefined` 입니다** — 렌더러를 받기 전(타이틀 · 미리보기 도구)이 그렇고, 그때는
 * 부르는 쪽이 `drawCardFaceVector` 로 선을 그립니다.
 *
 * `ink` 는 무늬의 색입니다. **부르는 쪽이 넘깁니다** — 색은 세트가 정하고, 세트를 아는 것은
 * 이 파일이 아니라 부르는 쪽입니다.
 */
export function cardFaceTexture(width: number, height: number, radius: number,
                                look: FaceLook, ink: number): Texture | undefined {
  return bakedFace(width, height, radius, look, ink)
}

/**
 * 앞면 하나를 통 하나에 그립니다. `(0, 0)` 이 왼쪽 위입니다.
 *
 * 구운 것이 있으면 스프라이트 하나이고, 없으면 선입니다. **한 장을 한 번 그리고 마는
 * 자리가 씁니다** — 손패처럼 같은 통을 계속 쓰는 쪽은 `cardFaceTexture` 로 그림만 받아
 * 자기 스프라이트에 겁니다.
 */
export function drawCardFace(node: Container, width: number, height: number, radius: number,
                             look: FaceLook, ink: number): void {
  const texture = bakedFace(width, height, radius, look, ink)
  if (!texture) {
    drawCardFaceVector(node, width, height, radius, look, ink)
    return
  }
  const sprite = new Sprite(texture)
  sprite.width = width
  sprite.height = height
  node.addChild(sprite)
}

/** 그려 둔 것을 지웁니다. */
export function clearCardFace(node: Container): void {
  node.removeChildren().forEach(child => child.destroy({ children: true }))
}

/** 구울 때 쓰는 렌더러와 화면 밀도. `bakeCardFaces` 가 넘깁니다. */
let baker: { renderer: Renderer; density: number } | undefined

/**
 * 구워 둔 앞면들. 열쇠가 같으면 같은 그림입니다.
 *
 * **넣은 차례가 남는 `Map` 입니다.** 다시 쓴 것을 지우고 다시 넣어 뒤로 보내므로, 맨 앞이
 * 가장 오래 쓰이지 않은 것입니다 — 넘칠 때 그것부터 버립니다.
 */
const BAKED = new Map<string, Texture>()

/** 몇 장을 구웠고 몇 번을 다시 썼고 몇 장을 버렸는가. **검증 도구가 읽습니다.** */
let bakedCount = 0
let reusedCount = 0
let droppedCount = 0

/**
 * 구운 그림에 쓰는 메모리의 한계.
 *
 * **열쇠의 조합이 한 벌보다 많습니다.** 강화 8종과 디버프와 그림 유무가 곱해지므로, 한 판
 * 내내 쌓이면 한 벌 52장이 수백 장이 됩니다 — 배율 3에서 한 장이 380KB 이고, 묶지 않으면
 * 100MB를 넘습니다.
 *
 * 48MB면 배율 2에서 260장, 배율 3에서 120장입니다. **한 화면에 보이는 카드는 많아도 52장**
 * (덱 보기)이므로, 버려지는 것은 지금 아무도 쓰지 않는 조합입니다.
 */
const BUDGET = 48 * 1024 * 1024

/** 지금 쥐고 있는 그림이 몇 바이트인가. */
let heldBytes = 0

/** 한 장이 차지하는 바이트. RGBA 이므로 픽셀당 4바이트입니다. */
function bytesOf(width: number, height: number, density: number): number {
  return Math.ceil(width * density) * Math.ceil(height * density) * 4
}

/**
 * 화면 배율보다 이만큼 높게 굽습니다.
 *
 * **카드는 늘 조금씩 기울고 흔들립니다.** 그래서 구운 그림이 화면의 픽셀과 어긋난 채로
 * 놓이고, 화면 배율에 딱 맞춰 구우면 그 어긋난 만큼 흐려집니다 — 모서리의 랭크와 무늬의
 * 뾰족한 끝에서 그것이 보였습니다. 뒷면은 그런 잔 것이 없어 배율 그대로입니다.
 *
 * 값이 1인 이유는 그 이상이 눈에 보이지 않고 메모리만 늘기 때문입니다. 한 벌 52장을
 * 배율 2로 구우면 9MB 안쪽입니다.
 */
const SHARPEN = 1

/**
 * 앞면을 그림으로 구울 렌더러를 받습니다.
 *
 * `density` 는 글씨와 같은 배율입니다 — 화면 배율 × 픽셀 밀도입니다. 이미 만들어진
 * 스프라이트는 그대로 두고 다음 것부터 새 밀도로 굽습니다.
 */
export function bakeCardFaces(renderer: Renderer, density: number): void {
  const density_ = Math.min(3, Math.max(1, density) + SHARPEN)
  // **배율이 바뀌면 쥐고 있던 것을 놓습니다.** 그 그림들은 다시 쓰이지 않는데 자리만
  // 차지하고, 그러면 새 배율의 것이 들어올 자리가 그만큼 좁아집니다. 아직 화면에 걸려
  // 있는 것은 다음에 다시 그릴 때 새 배율로 갈립니다.
  if (baker && baker.density !== density_) {
    droppedCount += BAKED.size
    BAKED.clear()
    heldBytes = 0
  }
  baker = { renderer, density: density_ }
}

/**
 * 구운 장수와 다시 쓴 횟수.
 *
 * **다시 쓰는 쪽이 크게 늘어야 맞습니다.** 카드를 고르고 무르는 동안 구운 장수가 함께
 * 늘면 열쇠에 매번 바뀌는 값이 섞인 것이고, 그러면 굽기가 낭비만 됩니다.
 */
export function cardFaceBakes():
    { baked: number; reused: number; held: number; dropped: number; bytes: number } {
  return {
    baked: bakedCount, reused: reusedCount, held: BAKED.size,
    dropped: droppedCount, bytes: heldBytes,
  }
}

/**
 * 넘치는 만큼 오래된 것부터 놓습니다.
 *
 * **`destroy` 하지 않습니다.** 버리는 것이 지금 화면의 어느 스프라이트에 걸려 있는지 여기서
 * 알 수 없고, 걸린 것을 지우면 그 카드가 빈 사각형이 됩니다 — 참조를 놓으면 아무도 쓰지
 * 않게 된 뒤에 Pixi 의 텍스처 수거가 GPU 쪽을 내립니다.
 */
function trim(): void {
  for (const [key, texture] of BAKED) {
    if (heldBytes <= BUDGET) return
    BAKED.delete(key)
    heldBytes -= bytesOf(texture.width, texture.height, texture.source.resolution)
    droppedCount++
  }
}

function bakedFace(width: number, height: number, radius: number,
                   look: FaceLook, ink: number): Texture | undefined {
  if (!baker) return undefined
  // **그림이 있는지도 열쇠입니다.** 그림은 늦게 옵니다(`onArtReady`) — 없을 때 구운 것이
  // 그림이 온 뒤에도 쓰이면 그 세트의 얼굴이 문양으로 남습니다.
  const dir = cardArtDir()
  const art = look.stone || dir === undefined
    ? undefined : artFor(dir, cardArtId(look.suit, look.rank))
  const key = `${width}|${height}|${radius}|${look.suit}|${look.rank}|${look.paper}`
    + `|${look.debuffed ? 1 : 0}|${look.stone ? 1 : 0}|${ink}`
    + `|${dir ?? ''}|${art ? 1 : 0}|${drawsIndex() ? 1 : 0}|${baker.density}`

  const found = BAKED.get(key)
  if (found) {
    reusedCount++
    // 다시 쓴 것을 맨 뒤로. 넘칠 때 버려지는 것이 가장 오래 쓰이지 않은 것이 됩니다.
    BAKED.delete(key)
    BAKED.set(key, found)
    return found
  }

  const node = new Container()
  drawCardFaceVector(node, width, height, radius, look, ink)
  const made = baker.renderer.generateTexture({
    target: node,
    resolution: baker.density,
    antialias: true,
    // 경계를 직접 줍니다. 그린 획이 삐져나온 만큼 사각형이 커지는 것을 막습니다.
    frame: new Rectangle(0, 0, width, height),
  })
  node.destroy({ children: true })
  BAKED.set(key, made)
  heldBytes += bytesOf(width, height, baker.density)
  bakedCount++
  trim()
  return made
}

/**
 * 앞면 하나를 선으로 그립니다. 굽는 쪽과 렌더러가 없는 쪽이 씁니다.
 *
 * 차례가 있습니다 — 종이 · 그림(또는 무늬) · 테두리 · 모서리입니다. **테두리가 그림 위**
 * 인 것은 그림이 카드를 덮기 때문이고, **모서리가 그 위**인 것은 정본 한 벌만 모서리까지
 * 그려져 있기 때문입니다.
 */
export function drawCardFaceVector(node: Container, width: number, height: number,
                                   radius: number, look: FaceLook, ink: number): void {
  // **자기 Graphics 를 만들어 담습니다.** 부르는 쪽의 `Graphics` 에 자식을 붙이면 Pixi 가
  // 예고 폐기로 알립니다.
  const g = new Graphics()
  node.addChild(g)

  // 1. 종이.
  g.roundRect(0, 0, width, height, radius).fill(look.paper)
  g.roundRect(3, 3, width - 6, height - 6, radius - 3)
    .stroke({ color: 0xffffff, width: 1, alpha: 0.5 })
  if (look.debuffed) {
    g.roundRect(0, 0, width, height, radius).fill({ color: 0x2a2a2a, alpha: 0.55 })
  }

  // 2. 얼굴.
  let index = true
  if (look.stone) {
    // 석재는 랭크도 무늬도 없습니다. **돌 하나입니다.**
    g.circle(width / 2, height / 2, 22).fill(0x6f6a60)
    g.circle(width / 2 - 5, height / 2 - 6, 7).fill({ color: 0x8b8578, alpha: 0.6 })
    index = false
  } else {
    const dir = cardArtDir()
    const texture = dir === undefined
      ? undefined : artFor(dir, cardArtId(look.suit, look.rank))
    if (texture) {
      const picture = new Sprite(texture)
      picture.width = width
      picture.height = height
      // 강화는 그림에 색을 입혀 알립니다 — 그림 위에 덧그리면 얼굴이 가려집니다.
      picture.tint = look.debuffed ? 0x8d8d8d : look.paper
      node.addChild(picture)
      // **정본 한 벌만 모서리까지 그려져 있습니다.** 우리가 굽는 세트는 그림 카드 12컷
      // 뿐이라 모서리를 그 위에 그립니다 — 그림에 넣게 하면 52컷이 되고, 랭크의 글자를
      // 그림 생성기가 틀립니다.
      index = drawsIndex()
    } else {
      drawFace(g, look.suit, look.rank, width, height, ink)
    }
  }

  // 3. 테두리. **얼굴 위에 그립니다** — 종이에 그으면 그림에 가려집니다.
  const line = new Graphics()
  node.addChild(line)
  line.roundRect(0.5, 0.5, width - 1, height - 1, radius)
    .stroke({ color: look.debuffed ? DEBUFF_EDGE : COLOR.cardEdge, width: 2 })

  // 4. 모서리. 랭크 하나와 그 아래의 작은 무늬 하나이고, 아래쪽은 거꾸로입니다 — 손에
  // 부챗살로 쥐었을 때 보이는 것이 그 둘뿐이기 때문입니다.
  if (!index) return
  const label = RANK_TEXT[look.rank] ?? '?'
  const size = cornerSize(label, CORNER_SIZE)

  const top = new Text({ text: label, style: { fontSize: size, fill: ink, fontWeight: '800' } })
  top.position.set(8, 5)
  node.addChild(top)

  // **아랫쪽 글자는 뒤집힙니다.** 트럼프는 어느 쪽에서 집어도 읽히도록 반대편 모서리의
  // 글자가 180도 돌아 있습니다 — 돌리지 않으면 그 카드는 한쪽에서만 읽히는 종이가 되고,
  // 무늬는 이미 뒤집어 그리고 있어서 글자만 바로 서 있었습니다.
  const bottom = new Text({
    text: label, style: { fontSize: size, fill: ink, fontWeight: '800' },
  })
  bottom.anchor.set(0, 0)
  bottom.rotation = Math.PI
  bottom.position.set(width - 8, height - 5)
  node.addChild(bottom)

  drawSuit(line, look.suit, 14, 33, 12, ink)
  drawSuit(line, look.suit, width - 14, height - 33, 12, ink, true)
}

/** 디버프가 무늬에 주는 색. 그리는 쪽이 세트의 색과 이것을 가릅니다. */
export function faceInk(debuffed: boolean, setInk: number): number {
  return debuffed ? DEBUFF_INK : setInk
}
