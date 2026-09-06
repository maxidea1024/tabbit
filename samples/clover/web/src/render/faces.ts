// 물건의 얼굴.
//
// **판에서 보이는 것과 도감에서 보이는 것이 같아야 합니다.** 태그의 칩도 보스의 인장도
// 소모품의 카드도 판을 그리는 파일 안의 private 메서드였고, 그것을 도감에서 다시 그리면
// 같은 물건이 두 곳에서 따로 그려집니다 — 한쪽만 고친 날부터 둘이 어긋나고, 어긋난 것을
// 알려 주는 게이트가 없습니다.
//
// **여기 있는 것은 얼굴뿐입니다.** 값도 누름도 진열 움직임도 상점의 일이므로 상점에
// 남습니다 — 이 파일의 함수는 상태를 읽지 않고 받은 것만 그립니다.

import { Container, Graphics, Sprite, Text, Texture } from 'pixi.js'

import type { Data } from '../core/data'
import { nameOf, t, text, tf } from '../core/strings'
import { BlindKind } from '../generated/enums/blind-kind'
import { PackKind } from '../generated/enums/pack-kind'
import { PackSize } from '../generated/enums/pack-size'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import { richLine } from '../ui/rich'
import { artFor, type ArtKind } from './art'
import { cardArtDir, suitInk } from './card-set'
import { drawGlyph, glyphFor, hashOf, hsl, shade } from './glyph'
import { cardArtId, drawFace } from './pips'
import { insetRadius, mix } from './skin'
import { COLOR, SIZE, UI } from './theme'

/** 카드 한 장을 작게 적을 때의 글자. */
export const MINI_RANK: Record<number, string> = {
  2: '2', 3: '3', 4: '4', 5: '5', 6: '6', 7: '7', 8: '8', 9: '9', 10: '10',
  11: 'J', 12: 'Q', 13: 'K', 14: 'A',
}

/** 무늬 하나의 글자. 작은 카드 얼굴이 씁니다. */
export const SUIT_PIP: Record<number, string> = {
  [SuitKind.Spade]: '♠',
  [SuitKind.Heart]: '♥',
  [SuitKind.Club]: '♣',
  [SuitKind.Diamond]: '♦',
}

/** 팩 이름. 표가 갈래와 크기만 정하므로 이름은 여기서 짓습니다. */
export function packName(kind: PackKind, size: PackSize): string {
  const body = kind === PackKind.Arcana ? t('ui.pack.arcana')
    : kind === PackKind.Celestial ? t('ui.pack.celestial')
    : kind === PackKind.Spectral ? t('ui.kind.spectral')
    : kind === PackKind.Buffoon ? t('ui.enhancement.mult')
    : t('ui.pack.standard')
  const scale = size === PackSize.Jumbo ? t('ui.pack.size.jumbo') : size === PackSize.Mega ? t('ui.pack.size.mega') : ''
  return tf('ui.pack.named', { scale, body })
}

export function packBlurb(kind: PackKind): string {
  switch (kind) {
    case PackKind.Arcana: return t('ui.pack.note.arcana')
    case PackKind.Celestial: return t('ui.pack.note.celestial')
    case PackKind.Spectral: return t('ui.pack.note.spectral')
    case PackKind.Buffoon: return t('ui.pack.note.buffoon')
    default: return t('ui.pack.note.standard')
  }
}

/**
 * 팩 갈래의 그림 파일 이름.
 *
 * **크기로는 갈리지 않습니다.** 보통 · 점보 · 메가가 같은 것을 담으므로 포장지도 같고,
 * 몇 장 중 몇 장인지는 화면이 글로 적습니다 — `art.py` 의 `pack_kinds` 와 같은 규칙입니다.
 */
export function packKindArt(kind: PackKind): string {
  return PackKind[kind].toLowerCase()
}

export function packInk(kind: PackKind): number {
  switch (kind) {
    case PackKind.Arcana: return 0x4a3a6b
    case PackKind.Celestial: return 0x264a6b
    case PackKind.Spectral: return 0x3a2a52
    case PackKind.Buffoon: return 0x6b3a3a
    default: return 0x2f5c42
  }
}

export function kindName(kind: ShopItemKind): string {
  switch (kind) {
    case ShopItemKind.Joker: return t('ui.guide.joker.head')
    case ShopItemKind.Tarot: return t('ui.kind.tarot')
    case ShopItemKind.Planet: return t('ui.kind.planet')
    case ShopItemKind.Spectral: return t('ui.kind.spectral')
    default: return t('ui.kind.card')
  }
}

export function shopLabel(kind: ShopItemKind, id: string, data: Data): string {
  switch (kind) {
    case ShopItemKind.Joker: return nameOf(data, 'joker', id, id)
    case ShopItemKind.Tarot: return nameOf(data, 'tarot', id, id)
    case ShopItemKind.Planet: return nameOf(data, 'planet', id, id)
    case ShopItemKind.Spectral: return nameOf(data, 'spectral', id, id)

    // **플레잉 카드는 이름이 표에 없습니다.** 이름을 52개 적어 둘 것이 아니라 무늬와
    // 랭크로 짓는 것이고, 그 둘은 이미 말마다 번역되어 있습니다 — 적어 두지 않아서
    // 식별자 `D2` 가 그대로 딱지에 떴습니다.
    case ShopItemKind.PlayingCard: {
      const row = data.tables.baseDeckCard.findByCardId(id)
      if (!row) return id
      const suit = text(data, `phrase.suit.${SuitKind[row.suit]}`)
      return `${suit} ${MINI_RANK[row.rank] ?? row.rank}`
    }

    default: return id
  }
}

/**
 * 태그의 칩.
 *
 * 그림이 있으면 그림, 없으면 문양입니다 — 색이 식별자에서 나오므로 태그마다 다릅니다.
 */
/**
 * 동그라미 안에 그림 하나.
 *
 * **비율을 지켜 덮습니다.** 너비와 높이에 같은 값을 넣으면 정사각이 아닌 그림이 눌립니다 —
 * 보스의 그림이 세로로 길게 구워져 있었고, 화면에서 그 동그라미가 전부 타원이었습니다.
 * 덮어서 넘치는 쪽은 동그라미가 오려 냅니다.
 */
function roundArt(texture: Texture, size: number): Container {
  const face = new Container()
  // 그림의 여백이 저마다 조금씩 달라서, 조금 키워 넣으면 그 차이가 덜 보입니다.
  const grown = size * 1.16
  const scale = Math.max(grown / texture.width, grown / texture.height)
  const sprite = new Sprite(texture)
  sprite.width = texture.width * scale
  sprite.height = texture.height * scale
  sprite.position.set(-sprite.width / 2, -sprite.height / 2)

  const round = new Graphics()
  round.circle(0, 0, size / 2).fill(0xffffff)
  sprite.mask = round

  face.addChild(round, sprite)
  return face
}

export function tagFace(tagId: string, size: number): Container {
  const face = new Container()
  const texture = artFor('tag', tagId)
  // **동그라미로 오려 냅니다.** 그림마다 바탕의 여백이 조금씩 달라서 그대로 넣으면
  // 어떤 것은 네모 액자에 든 칩으로 보입니다 — 칩만 남기면 그 차이가 없어집니다.
  if (texture) return roundArt(texture, size)

  // 문양 하나와 그 태그의 색. 색은 이름에서 나오므로 태그마다 다릅니다.
  const hue = hashOf(tagId) % 360
  const art = new Graphics()
  art.circle(0, 0, size / 2).fill({ color: hsl(hue, 0.5, 0.32) })
  art.circle(0, 0, size / 2).stroke({ color: hsl(hue, 0.6, 0.6), width: 1.5 })
  drawGlyph(art, glyphFor(tagId), 0, 0, size * 0.3, {
    fill: hsl(hue, 0.7, 0.78), line: hsl(hue, 0.4, 0.22), weight: 1.4,
  })
  face.addChild(art)
  return face
}

/**
 * 보스의 인장.
 *
 * **보스마다 다른 표시가 있어야 합니다.** 이름과 효과만 적혀 있으면 28종이 한 갈래로
 * 보이고, 어느 것이 나왔는지가 판마다 남지 않습니다 — 원작에서도 보스는 저마다 다른
 * 표시를 답니다.
 *
 * 그림이 있으면 그림, 없으면 문양입니다 — 문양은 식별자에서 나오므로 보스마다 다릅니다.
 */
export function bossFace(bossId: string, size: number): Container {
  const face = new Container()
  const texture = artFor('boss', bossId)
  if (texture) return roundArt(texture, size)

  // 붉은 돌 하나에 새긴 표시. **색은 식별자에서 나오므로 보스마다 다릅니다.**
  const hue = 320 + (hashOf(bossId) % 60)
  const art = new Graphics()
  art.circle(0, 0, size / 2).fill({ color: hsl(hue % 360, 0.42, 0.20) })
  art.circle(0, 0, size / 2).stroke({ color: hsl(hue % 360, 0.55, 0.52), width: 2 })
  // 테두리의 눈금. 태그의 칩과 갈리는 것이 이것입니다.
  for (let i = 0; i < 12; i++) {
    const angle = (i / 12) * Math.PI * 2
    const inner = size / 2 - 4
    const outer = size / 2 - 1
    art.moveTo(Math.cos(angle) * inner, Math.sin(angle) * inner)
      .lineTo(Math.cos(angle) * outer, Math.sin(angle) * outer)
      .stroke({ color: hsl(hue % 360, 0.5, 0.62), width: 1.2, alpha: 0.8 })
  }
  drawGlyph(art, glyphFor(bossId), 0, 0, size * 0.28, {
    fill: hsl(hue % 360, 0.7, 0.8), line: hsl(hue % 360, 0.4, 0.14), weight: 1.6,
  })
  face.addChild(art)
  return face
}

/**
 * 블라인드의 표시.
 *
 * **셋 다 답니다.** 보스에만 인장이 붙어 있었고, 그러면 나란히 선 셋 중 하나만 표시를
 * 가진 것이 되어 그 셋이 같은 갈래로 보이지 않습니다 — 스몰과 빅은 같은 모양의 딱지이고
 * 크기와 색으로 갈립니다. 어느 라운드인지가 이름을 읽지 않아도 표시로 먼저 읽힙니다.
 *
 * **어느 보스인지는 받습니다.** 판이 도는 중에는 그 안테의 보스이고 도감에서는 고른
 * 보스입니다 — 그리는 쪽이 판의 상태를 조회하면 도감에서 쓸 수 없습니다.
 */
export function blindFace(blind: BlindKind, size: number, bossId: string): Container {
  if (blind === BlindKind.Boss) return bossFace(bossId, size)

  const big = blind === BlindKind.Big
  const tint = big ? 0xa279e0 : 0x5d92d6
  const face = new Container()
  const art = new Graphics()

  // 딱지 하나. **가운데의 원이 크기로 갈립니다** — 빅이 스몰보다 큽니다.
  art.circle(0, 0, size / 2).fill({ color: mix(tint, 0x000000, 0.55) })
  art.circle(0, 0, size / 2).stroke({ color: tint, width: 2 })
  art.circle(0, 0, size * (big ? 0.28 : 0.19)).fill({ color: tint, alpha: 0.9 })

  // 가장자리의 눈금 여덟. 딱지가 그냥 동그라미가 아니라 표식으로 보입니다.
  for (let i = 0; i < 8; i++) {
    const angle = (i / 8) * Math.PI * 2
    const from = size * 0.37
    const to = size * 0.45
    art.moveTo(Math.cos(angle) * from, Math.sin(angle) * from)
      .lineTo(Math.cos(angle) * to, Math.sin(angle) * to)
      .stroke({ color: tint, width: 1.4, alpha: 0.85 })
  }

  face.addChild(art)
  return face
}

/** `itemFace` 가 그리는 것. 상점의 딱지에서는 `ShopItem` 이 그대로 들어옵니다. */
export interface ItemFace {
  kind: ShopItemKind
  id: string
}

/**
 * 소모품과 플레잉 카드의 얼굴.
 *
 * 조커와 **같은 크기, 같은 모서리, 같은 이름 띠**입니다 — 상점에 여러 갈래가 서므로
 * 모양이 어긋나면 줄이 흐트러져 보입니다.
 */
export function itemFace(data: Data, item: ItemFace): Container {
  const w = SIZE.jokerWidth
  const h = SIZE.jokerHeight
  const node = new Container()

  // **그림자와 얼굴을 가릅니다.** 셰이더는 얼굴에만 걸립니다 — 통째로 걸면 그림자도
  // 함께 번쩍여서, 카드 옆에 빛나는 얼룩 하나가 따로 남습니다. `joker-view.ts` 와 같은
  // 이유이고, `faceOf` 가 이 둘째 아이를 찾아 씁니다.
  const shadow = new Graphics()
  shadow.roundRect(3, 5, w, h, 9).fill({ color: 0x000000, alpha: 0.4 })
  const paper = new Container()
  node.addChild(shadow, paper)

  const plate = new Graphics()
  plate.roundRect(0, 0, w, h, 9).fill(0x141b26)
  paper.addChild(plate)

  /**
   * 그림을 카드 모양으로 자르는 것.
   *
   * **쓸 때 만듭니다.** 미리 만들어 붙여 두면 그림이 아직 오지 않은 동안 이 흰 사각형이
   * 그대로 카드 얼굴로 그려집니다 — 카드가 하얗게 나오던 것이 그것입니다.
   */
  const cutout = (): Graphics => {
    const clip = new Graphics()
    clip.roundRect(0, 0, w, h, 9).fill(0xffffff)
    paper.addChild(clip)
    return clip
  }

  if (item.kind === ShopItemKind.PlayingCard) {
    const row = data.tables.baseDeckCard.findByCardId(item.id)
    if (row) {
      const setDir = cardArtDir()
      const texture = setDir === undefined
        ? undefined : artFor(setDir, cardArtId(row.suit, row.rank))
      if (texture) {
        const picture = new Sprite(texture)
        picture.width = w
        picture.height = h
        paper.addChild(picture)
      } else {
        const face = new Graphics()
        face.roundRect(0, 0, w, h, 9).fill(COLOR.cardFace)
        drawFace(face, row.suit, row.rank, w, h, suitInk(row.suit))
        paper.addChild(face)
      }
    }
  } else {
    const kind: ArtKind | undefined = item.kind === ShopItemKind.Tarot ? 'tarot'
      : item.kind === ShopItemKind.Planet ? 'planet'
        : item.kind === ShopItemKind.Spectral ? 'spectral' : undefined
    const texture = kind ? artFor(kind, item.id) : undefined
    if (texture) {
      const sprite = new Sprite(texture)
      const scale = Math.max(w / texture.width, h / texture.height)
      sprite.width = texture.width * scale
      sprite.height = texture.height * scale
      sprite.position.set((w - sprite.width) / 2, (h - sprite.height) / 2)
      sprite.mask = cutout()
      paper.addChild(sprite)
    }
  }

  const tint = item.kind === ShopItemKind.PlayingCard ? COLOR.cardEdge : 0x9b8fd0
  const band = new Graphics()
  band.roundRect(0, h - 26, w, 26, 9).fill({ color: 0x0b1018, alpha: 0.88 })
  band.rect(0, h - 26, w, 17).fill({ color: 0x0b1018, alpha: 0.88 })
  band.rect(0, h - 26, w, 1.5).fill({ color: tint, alpha: 0.9 })
  paper.addChild(band)

  const label = new Text({
    text: shopLabel(item.kind, item.id, data),
    style: {
      fontSize: 11, fill: COLOR.ink, fontWeight: '800', align: 'center',
      wordWrap: true, wordWrapWidth: w - 8, breakWords: true, lineHeight: 12,
    },
  })
  label.anchor.set(0.5, 0.5)
  label.position.set(w / 2, h - 13)
  paper.addChild(label)

  const frame = new Graphics()
  frame.roundRect(1.25, 1.25, w - 2.5, h - 2.5, insetRadius(9, 1.25))
    .stroke({ color: tint, width: 2.5 })
  paper.addChild(frame)

  return node
}

/**
 * 카드 하나의 얼굴을 그림자와 갈라 냅니다.
 *
 * **둘째 아이가 얼굴입니다.** 셰이더를 통째로 걸면 그림자도 함께 번쩍이므로, 거는 쪽은
 * 이 함수로 얼굴만 찾습니다.
 */
export function faceOf(node: Container): Container {
  return (node.children[1] as Container | undefined) ?? node
}

/**
 * 바우처의 얼굴.
 *
 * **바우처도 카드입니다** — 크림색 얼굴에 이름과 한 줄. 상점의 물건이 전부 카드여야 한
 * 줄에 놓입니다.
 */
export function voucherFace(data: Data, voucherId: string, note: string): Container {
  const w = SIZE.jokerWidth
  const h = SIZE.jokerHeight
  const row = data.tables.voucher.findByVoucherId(voucherId)
  const title = nameOf(data, 'voucher', voucherId, row?.name ?? '')

  const face = new Container()
  const paper = new Graphics()
  paper.roundRect(0, 0, w, h, 9).fill(0xefe6d3)
  paper.roundRect(1, 1, w - 2, h - 2, insetRadius(9, 1)).stroke({ color: UI.ink, width: 2 })
  const label = new Text({
    text: title,
    style: {
      fontSize: 13, fill: 0x2a2420, fontWeight: '900', align: 'center',
      wordWrap: true, wordWrapWidth: w - 12, breakWords: true, lineHeight: 15,
    },
  })
  label.anchor.set(0.5, 0)
  label.position.set(w / 2, 12)
  const line = new Text({
    text: note,
    style: {
      fontSize: 9, fill: 0x6b6255, fontWeight: '700', align: 'center',
      wordWrap: true, wordWrapWidth: w - 12, breakWords: true, lineHeight: 12,
    },
  })
  line.anchor.set(0.5, 0)
  line.position.set(w / 2, 12 + label.height + 6)
  face.addChild(paper, label, line)
  return face
}

/** `packFace` 가 그리는 것. 표의 한 행이 그대로 들어옵니다. */
export interface PackFaceRow {
  kind: PackKind
  size: PackSize
  cards: number
  picks: number
}

/**
 * 팩의 포장지.
 *
 * **포장지는 그림입니다.** 봉지 몸통과 톱니 한 줄을 그어 두고 있었고, 그것은 색칠한
 * 네모였습니다 — 상점 한 줄에 카드와 나란히 서는데 그 줄에서 팩만 그림이 없었습니다.
 * 그림이 아직 오지 않은 기계에서는 그 봉지를 그대로 그립니다.
 */
export function packFace(row: PackFaceRow): Container {
  const w = SIZE.jokerWidth
  const h = SIZE.jokerHeight
  const ink = packInk(row.kind)
  const bag = new Container()

  const wrap = artFor('pack', packKindArt(row.kind))
  const body = new Graphics()
  if (wrap) {
    body.roundRect(0, 0, w, h, 9).fill(shade(ink, 0.35))
  } else {
    body.roundRect(0, 0, w, h, 9).fill(shade(ink, 0.45))
    body.roundRect(0, 0, w, h * 0.55, 9).fill({ color: ink, alpha: 0.5 })
    // **뜯는 줄.** 톱니 하나가 봉지를 봉지로 만듭니다.
    const tearY = 22
    body.rect(0, tearY - 6, w, 12).fill({ color: 0x0b1018, alpha: 0.35 })
    const teeth = 11
    for (let i = 0; i < teeth; i++) {
      const tx = (w / teeth) * i
      body.moveTo(tx, tearY).lineTo(tx + w / teeth / 2, tearY - 4).lineTo(tx + w / teeth, tearY)
        .stroke({ color: shade(ink, 0.8), width: 1.2, alpha: 0.9 })
    }
  }
  bag.addChild(body)
  if (wrap) {
    // **칸을 덮도록 키워 가운데를 씁니다.** 그림의 비율이 칸과 다르면 남는 자리가 생기고
    // 그 자리는 그림의 바탕색입니다 — 넘치는 쪽을 잘라 냅니다.
    const sprite = new Sprite(wrap)
    const scale = Math.max(w / wrap.width, h / wrap.height)
    sprite.width = wrap.width * scale
    sprite.height = wrap.height * scale
    sprite.position.set((w - sprite.width) / 2, (h - sprite.height) / 2)
    const clip = new Graphics()
    clip.roundRect(0, 0, w, h, 9).fill(0xffffff)
    sprite.mask = clip
    bag.addChild(sprite, clip)
  }
  const edge = new Graphics()
  edge.roundRect(1, 1, w - 2, h - 2, insetRadius(9, 1)).stroke({ color: UI.ink, width: 2 })
  bag.addChild(edge)

  const label = new Text({
    text: packName(row.kind, row.size),
    style: {
      fontSize: 11, fill: COLOR.ink, fontWeight: '800', align: 'center',
      wordWrap: true, wordWrapWidth: w - 10, breakWords: true, lineHeight: 14,
    },
  })
  label.anchor.set(0.5, 0.5)
  label.position.set(w / 2, h - 32)
  const note = richLine(tf('ui.pack.of', { cards: row.cards, picks: row.picks }), {
    base: { fontSize: 10, fill: COLOR.ink },
    number: COLOR.accentNumber,
    term: COLOR.accentTerm,
  })
  note.position.set((w - note.width) / 2, h - 18)
  // **글이 앉는 자리를 어둡게 깔아 둡니다.** 포장지의 색이 무엇이든 그 위의 글이 읽혀야
  // 합니다 — 카드의 이름 띠와 같은 규칙입니다.
  const band = new Graphics()
  band.roundRect(0, h - 42, w, 42, 9).fill({ color: 0x0b1018, alpha: 0.86 })
  band.rect(0, h - 42, w, 30).fill({ color: 0x0b1018, alpha: 0.86 })
  bag.addChild(band, label, note)

  return bag
}
