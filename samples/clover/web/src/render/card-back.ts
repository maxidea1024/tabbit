// 카드의 뒷면.
//
// **뒷면은 바탕색 하나가 아닙니다.** 몇백 년째 같은 모양이 있습니다 — 크림 바탕에 한 가지
// 색으로 그린 선화이고, 굵은 테두리 · 점선 띠 · 가는 테두리가 겹겹이 두르고, 가운데에는
// 동심원 여럿이 겹쳐 만드는 상하좌우 대칭 문양이 있습니다. 그 겹침이 곧 「이것은 트럼프
// 뒷면이다」이고, 색만 칠한 사각형은 아무리 예뻐도 그렇게 보이지 않습니다.
//
// **덱마다 다릅니다.** 무늬와 색 두 개가 `Deck` 표에 있고, 덱을 고르는 것이 한 판 내내
// 손에 들고 있을 뒷면을 고르는 것이기도 합니다.
//
// 바깥의 겹겹은 어느 무늬나 같습니다 — 그것이 「트럼프 뒷면」이라는 낱말이고, 갈리는 것은
// 안쪽 판에 무엇이 있는가입니다. 그래서 이 파일은 **테두리 한 벌과 안쪽 열한 가지**입니다.
//
// 그림 파일이 아닌 이유는 앞면과 같습니다 — 크기가 여럿(손패 · 덱 더미 · 남은 카드 보기)이고,
// 선화라 어느 크기에서도 다시 그리는 편이 낫습니다.

import { Container, Graphics } from 'pixi.js'

import { CardBackKind } from '../generated/enums/card-back-kind'
import { COLOR } from './theme'

/**
 * 뒷면 하나가 정해지는 것. **무늬 하나와 색 두 개입니다** — 셋째 색이 들어가면 선화가
 * 그림이 됩니다.
 */
export interface BackLook {
  motif: CardBackKind
  ground: number
  ink: number
}

/**
 * 덱을 고르기 전의 뒷면.
 *
 * **판이 서기 전에도 뒷면이 필요합니다** — 타이틀의 카드와, 덱을 아직 모르는 자리가
 * 그렇습니다. 첫 덱의 뒷면이고, 판이 서면 그 판의 덱이 이것을 대신합니다.
 */
export const CARD_BACK: BackLook = {
  motif: CardBackKind.Classic, ground: COLOR.cardBack, ink: COLOR.cardBackEdge,
}

/**
 * 지금 판의 뒷면.
 *
 * **한 판에 하나입니다.** 손패도 · 덱 더미도 · 판이 끝나고 돌아오는 카드도 같은 뒷면이고,
 * 그것을 정하는 것은 판이 시작될 때 고른 덱 하나입니다. 그래서 그리는 자리마다 넘겨받는
 * 대신 여기 한 곳에 둡니다 — 넘겨받게 하면 뒷면을 그리는 다섯 자리가 저마다 「어느
 * 뒷면인가」를 알아야 하고, 그 다섯 중 하나만 놓쳐도 그 자리의 카드가 다른 덱이 됩니다.
 *
 * 판이 시작될 때 한 번 정해지고 그 판이 끝날 때까지 바뀌지 않습니다.
 */
let inPlay: BackLook = CARD_BACK

/** 이 판이 쓸 뒷면. 판이 시작될 때 부릅니다. */
export function setCardBack(look: BackLook): void {
  inPlay = look
}

/** 지금 그려야 할 뒷면. */
export function cardBack(): BackLook {
  return inPlay
}

/** `Deck` 표의 한 줄이 뒷면이 됩니다. 색은 시트에 `#rrggbb` 로 적혀 있습니다. */
export function backLookOf(row: { back: CardBackKind; backGround: string; backInk: string }): BackLook {
  return { motif: row.back, ground: hex(row.backGround), ink: hex(row.backInk) }
}

function hex(value: string): number {
  const parsed = Number.parseInt(value.replace('#', ''), 16)
  return Number.isNaN(parsed) ? COLOR.cardBack : parsed
}

/** 안쪽 판. 무늬가 그려지는 자리이고, 이 밖으로는 나가지 않습니다. */
interface Panel {
  x: number
  y: number
  w: number
  h: number
  cx: number
  cy: number
  /** 가는 선 하나의 굵기. 카드가 작아지면 이것도 작아집니다. */
  hair: number
}

/**
 * 뒷면 하나를 그립니다. `(0, 0)` 이 왼쪽 위입니다.
 */
export function drawCardBack(node: Container, width: number, height: number,
                             radius: number, look: BackLook): void {
  const cx = width / 2
  const cy = height / 2
  // **자기 Graphics 를 만들어 담습니다.** 부르는 쪽의 `Graphics` 에 자식을 붙이면 Pixi 가
  // 예고 폐기로 알리고, 콘솔이 그 경고로 덮이면 진짜 오류가 그 밑에 묻힙니다.
  const g = new Graphics()
  node.addChild(g)

  // 1. 바탕.
  g.roundRect(0, 0, width, height, radius).fill(look.ground)

  // 2. 굵은 바깥 테두리.
  const edge = Math.max(2, Math.round(width * 0.045))
  g.roundRect(edge / 2, edge / 2, width - edge, height - edge, radius - edge / 2)
    .stroke({ color: look.ink, width: edge })

  // 3. 점선 띠. **작은 네모가 줄지어 있는 것이 이 뒷면의 표식입니다.**
  const bandInset = edge * 1.9
  const dot = Math.max(1, Math.round(width * 0.018))
  const step = dot * 2.6
  const bandL = bandInset
  const bandR = width - bandInset
  const bandT = bandInset
  const bandB = height - bandInset

  for (let x = bandL; x <= bandR - dot; x += step) {
    g.rect(x, bandT, dot, dot).fill(look.ink)
    g.rect(x, bandB - dot, dot, dot).fill(look.ink)
  }
  for (let y = bandT + step; y <= bandB - dot - step; y += step) {
    g.rect(bandL, y, dot, dot).fill(look.ink)
    g.rect(bandR - dot, y, dot, dot).fill(look.ink)
  }

  // 4. 가는 안쪽 테두리와 네 변 가운데의 표식.
  const inset = bandInset + dot * 2.4
  const panelW = width - inset * 2
  const panelH = height - inset * 2
  const hair = Math.max(1, Math.round(width * 0.013))
  g.rect(inset, inset, panelW, panelH).stroke({ color: look.ink, width: hair })

  const mark = dot * 1.8
  for (const [mx, my] of [[cx, inset], [cx, inset + panelH],
                          [inset, cy], [inset + panelW, cy]] as const) {
    g.rect(mx - mark / 2, my - mark / 2, mark, mark).fill(look.ground)
    g.rect(mx - mark / 2, my - mark / 2, mark, mark).stroke({ color: look.ink, width: hair })
  }

  // 5. 안쪽 판. **여기서부터 덱마다 다릅니다.**
  const panel: Panel = { x: inset, y: inset, w: panelW, h: panelH, cx, cy, hair }
  const field = new Graphics()
  MOTIF[look.motif]?.(field, panel, look)

  // 무늬는 판 밖으로 나가지 않습니다. **자를 것이 있어야 문양을 판보다 크게 그릴 수
  // 있고**, 그래야 무늬가 판에 가득 찬 것으로 보입니다 — 판 안에 얌전히 들어가게 그리면
  // 무늬가 아니라 판 가운데 놓인 그림이 됩니다.
  const clip = new Graphics()
  clip.rect(inset + hair, inset + hair, panelW - hair * 2, panelH - hair * 2).fill(0xffffff)
  field.mask = clip
  node.addChild(clip, field)
}

/**
 * 그려 둔 것을 치웁니다.
 *
 * `Graphics.clear()` 는 그린 선만 지웁니다 — 문양을 담은 통은 자식이라 그대로 남고, 다시
 * 그릴 때마다 한 겹씩 쌓입니다.
 */
export function clearCardBack(node: Container): void {
  node.removeChildren().forEach(child => child.destroy({ children: true }))
}

// ------------------------------------------------------------------ 무늬

/**
 * 자리를 흩는 수.
 *
 * **난수가 아닙니다.** 카드는 크기가 바뀔 때마다 다시 그려지므로, `Math.random` 을 쓰면
 * 손패에 있던 카드와 덱 보기에 있는 같은 카드의 뒷면이 다른 그림이 됩니다 — 흩어져
 * 보이기만 하면 되고, 그것은 셈으로 충분합니다.
 */
function scatter(i: number, salt: number): number {
  const x = Math.sin(i * 12.9898 + salt * 78.233) * 43_758.545
  return x - Math.floor(x)
}

type Motif = (g: Graphics, p: Panel, look: BackLook) => void

/**
 * 겹친 동심원.
 *
 * **셋이면 뭉칩니다.** 88 × 124 에서 원 셋이 겹치면 가운데가 메워져 무늬가 아니라 얼룩으로
 * 보입니다 — 위아래 하나씩이면 가운데에 렌즈 하나가 서고 그것이 문양이 됩니다.
 */
const classic: Motif = (g, p, look) => {
  const ring = Math.max(6, p.w * 0.145)
  const reach = Math.hypot(p.w, p.h) / 2
  const offset = p.h * 0.3

  for (const center of [p.cy - offset, p.cy + offset]) {
    for (let r = ring; r <= reach; r += ring) {
      g.circle(p.cx, center, r).stroke({ color: look.ink, width: p.hair })
    }
  }

  // 위아래 끝의 마름모. **문양에 시작과 끝이 있어야** 겹친 원이 무늬로 읽힙니다.
  for (const center of [p.y + p.h * 0.13, p.y + p.h * 0.87]) {
    const size = p.w * 0.15
    diamond(g, p.cx, center, size).fill(look.ground)
    diamond(g, p.cx, center, size).stroke({ color: look.ink, width: p.hair })
  }
}

/** 비스듬한 체크무늬. 마름모를 한 칸 걸러 하나씩 채웁니다. */
const checker: Motif = (g, p, look) => {
  const cell = p.w / 4
  for (let row = -1; row <= p.h / cell + 1; row++) {
    for (let col = -1; col <= p.w / cell + 1; col++) {
      if ((row + col) % 2 !== 0) continue
      diamond(g, p.x + col * cell, p.y + row * cell, cell * 0.72).fill(look.ink)
    }
  }
}

/**
 * 별과 성운.
 *
 * 성운은 큰 원 몇 개를 옅게 겹친 것입니다 — **한 겹으로는 얼룩이고, 겹치면 성운입니다.**
 */
const starfield: Motif = (g, p, look) => {
  for (let i = 0; i < 5; i++) {
    const x = p.x + scatter(i, 3) * p.w
    const y = p.y + scatter(i, 7) * p.h
    g.circle(x, y, p.w * (0.2 + scatter(i, 11) * 0.3))
      .fill({ color: look.ink, alpha: 0.1 })
  }

  for (let i = 0; i < 46; i++) {
    const x = p.x + scatter(i, 17) * p.w
    const y = p.y + scatter(i, 23) * p.h
    const size = p.hair * (0.5 + scatter(i, 29) * 1.3)
    g.circle(x, y, size).fill({ color: look.ink, alpha: 0.4 + scatter(i, 31) * 0.6 })
  }

  // 네 갈래로 뻗는 큰 별 셋. **크기만 다른 점 마흔여섯 개는 모래이지 별이 아닙니다.**
  for (let i = 0; i < 3; i++) {
    const x = p.x + p.w * (0.24 + i * 0.26)
    const y = p.y + p.h * (0.2 + scatter(i, 37) * 0.6)
    const reach = p.w * 0.13
    g.moveTo(x - reach, y).lineTo(x + reach, y)
      .stroke({ color: look.ink, width: p.hair })
    g.moveTo(x, y - reach).lineTo(x, y + reach)
      .stroke({ color: look.ink, width: p.hair })
    g.circle(x, y, p.hair * 1.4).fill(look.ink)
  }
}

/** 달과 룬. 초승달 하나와 그 둘레의 짧은 획들입니다. */
const arcane: Motif = (g, p, look) => {
  const r = p.w * 0.26
  // 초승달은 원 둘의 차입니다 — 채운 원 위에 바탕색 원을 비껴 얹습니다.
  g.circle(p.cx, p.cy, r).fill(look.ink)
  g.circle(p.cx + r * 0.42, p.cy - r * 0.3, r * 0.92).fill(look.ground)

  const runes = 12
  for (let i = 0; i < runes; i++) {
    const angle = (i / runes) * Math.PI * 2 - Math.PI / 2
    const at = r * 1.75
    const x = p.cx + Math.cos(angle) * at
    const y = p.cy + Math.sin(angle) * at
    const long = p.w * 0.06
    // 획 둘이 엇갈립니다. 같은 획이 열둘이면 눈금이고, 엇갈리면 글자로 보입니다.
    g.moveTo(x - Math.cos(angle) * long, y - Math.sin(angle) * long)
      .lineTo(x + Math.cos(angle) * long, y + Math.sin(angle) * long)
      .stroke({ color: look.ink, width: p.hair })
    if (i % 2 === 0) continue
    g.moveTo(x - Math.sin(angle) * long * 0.6, y + Math.cos(angle) * long * 0.6)
      .lineTo(x + Math.sin(angle) * long * 0.6, y - Math.cos(angle) * long * 0.6)
      .stroke({ color: look.ink, width: p.hair })
  }
}

/** 황도의 눈금 원반. 열두 칸이고 넷째마다 눈금이 깁니다. */
const zodiac: Motif = (g, p, look) => {
  const outer = p.w * 0.42
  for (const r of [outer, outer * 0.82, outer * 0.34]) {
    g.circle(p.cx, p.cy, r).stroke({ color: look.ink, width: p.hair })
  }

  for (let i = 0; i < 12; i++) {
    const angle = (i / 12) * Math.PI * 2 - Math.PI / 2
    const long = i % 3 === 0 ? outer * 0.5 : outer * 0.24
    g.moveTo(p.cx + Math.cos(angle) * (outer * 0.82 - long),
             p.cy + Math.sin(angle) * (outer * 0.82 - long))
      .lineTo(p.cx + Math.cos(angle) * outer * 0.82,
              p.cy + Math.sin(angle) * outer * 0.82)
      .stroke({ color: look.ink, width: p.hair })
    g.circle(p.cx + Math.cos(angle) * outer, p.cy + Math.sin(angle) * outer, p.hair * 1.2)
      .fill(look.ink)
  }

  g.circle(p.cx, p.cy, outer * 0.12).fill(look.ink)
}

/**
 * 아른거리는 겹.
 *
 * **옅기만 하면 유령이 아니라 안 그린 것입니다.** 처음에 알파를 0.4로 두었더니 카드가 빈
 * 종이로 보였습니다 — 아른거림은 「흐리다」가 아니라 「같은 것이 조금씩 어긋나 여럿
 * 있다」이고, 그 어긋남이 보이려면 선 하나하나는 또렷해야 합니다.
 *
 * **그다음엔 실뭉치가 되었습니다.** 고리 위로 물결 둘을 지나가게 했더니 겹침이 어긋남이
 * 아니라 엉킴으로 읽혔습니다 — 어긋남이 보이려면 어긋나는 것이 **같은 모양**이어야 하고,
 * 다른 모양이 얹히는 순간 그것은 둘이 겹친 것이 아니라 하나의 뒤엉킨 무엇입니다.
 * 남은 것은 고리뿐입니다.
 */
const veil: Motif = (g, p, look) => {
  const groups = 3
  const step = p.w * 0.145
  for (let i = 0; i < groups; i++) {
    const drift = (i - (groups - 1) / 2) * p.w * 0.14
    // 가운데 겹이 가장 진합니다. **한 겹이 앞에 있어야 나머지가 그 뒤의 잔상이 됩니다.**
    const near = 1 - Math.abs(i - (groups - 1) / 2) / groups
    for (let r = step; r <= p.w * 0.5; r += step) {
      g.circle(p.cx + drift, p.cy, r)
        .stroke({ color: look.ink, width: p.hair * 1.4, alpha: 0.3 + near * 0.6 })
    }
  }
  g.circle(p.cx, p.cy, p.w * 0.045).fill({ color: look.ink, alpha: 0.85 })
}

/**
 * 닳은 것.
 *
 * **버려진 덱은 낡은 카드입니다.** 처음에 끊긴 격자를 그렸더니 격자가 아니라 벽돌 조각이
 * 되었습니다 — 낡음은 다른 무늬가 아니라 **같은 무늬가 닳은 것**이라, 고전 무늬를 그대로
 * 긋되 획을 토막 내고 그 토막을 군데군데 빠뜨립니다.
 */
const worn: Motif = (g, p, look) => {
  const ring = Math.max(6, p.w * 0.145)
  const reach = Math.hypot(p.w, p.h) / 2
  const offset = p.h * 0.3
  let seed = 0

  for (const center of [p.cy - offset, p.cy + offset]) {
    for (let r = ring; r <= reach; r += ring) {
      const arcs = 14
      for (let i = 0; i < arcs; i++) {
        seed++
        if (scatter(seed, 41) < 0.34) continue
        const from = (i / arcs) * Math.PI * 2
        const to = from + (Math.PI * 2) / arcs
        g.moveTo(p.cx + Math.cos(from) * r, center + Math.sin(from) * r)
        g.arc(p.cx, center, r, from, to)
        g.stroke({ color: look.ink, width: p.hair, alpha: 0.5 + scatter(seed, 43) * 0.5 })
      }
    }
  }

  // 긁힌 자국 셋. **닳은 것에는 결이 있습니다.**
  for (let i = 0; i < 3; i++) {
    const y = p.y + (0.2 + i * 0.3) * p.h
    g.moveTo(p.x, y + scatter(i, 47) * p.h * 0.1)
      .lineTo(p.x + p.w, y - scatter(i, 53) * p.h * 0.1)
      .stroke({ color: look.ink, width: p.hair * 0.9, alpha: 0.22 })
  }
}

/**
 * 붓으로 한 번에 그은 원.
 *
 * **꽃잎 여섯을 그렸다가 거미가 되었습니다.** 가운데에서 뻗는 획 여럿은 꽃이 아니라
 * 다리이고, 붓의 낱말은 「무엇을 그렸는가」가 아니라 「한 획으로 그었는가」입니다 —
 * 닫히지 않은 원 하나가 그 낱말입니다.
 *
 * 굵기가 변합니다. **한결같은 굵기는 붓이 아니라 관입니다** — 처음에 눌리고 끝에서
 * 들리므로, 토막마다 굵기를 다르게 그어 그 눌림을 냅니다.
 */
const brush: Motif = (g, p, look) => {
  const r = p.w * 0.34
  const steps = 26
  const gap = 0.34
  for (let i = 0; i < steps; i++) {
    const t0 = i / steps
    const t1 = (i + 1) / steps
    const a0 = -Math.PI * 0.62 + t0 * (Math.PI * 2 - gap)
    const a1 = -Math.PI * 0.62 + t1 * (Math.PI * 2 - gap)
    // 눌렸다가 들립니다. 처음이 가늘고 가운데가 굵고 끝이 다시 가늘어집니다.
    const press = Math.sin(Math.min(1, t0 * 1.15) * Math.PI) * 0.85 + 0.25
    g.moveTo(p.cx + Math.cos(a0) * r, p.cy + Math.sin(a0) * r)
      .lineTo(p.cx + Math.cos(a1) * r, p.cy + Math.sin(a1) * r)
      .stroke({ color: look.ink, width: p.hair * 3.4 * press, cap: 'round' })
  }

  // 튄 자국 셋. **붓이 지나갔으면 튄 것도 있습니다.**
  for (let i = 0; i < 3; i++) {
    g.circle(p.cx + (scatter(i, 73) - 0.5) * p.w * 0.8,
             p.cy + (scatter(i, 79) - 0.5) * p.h * 0.8,
             p.hair * (0.8 + scatter(i, 83) * 1.4))
      .fill({ color: look.ink, alpha: 0.75 })
  }
}

/**
 * 어긋나 겹친 두 색.
 *
 * **셋째 색이 아닙니다.** 적청 안경으로 보는 그림은 같은 그림 둘이 어긋난 것이고, 그
 * 둘째 색은 첫째의 보색이므로 시트에 따로 적을 것이 없습니다.
 *
 * **어긋남이 커야 어긋난 것으로 보입니다.** 처음에 카드 너비의 4.5%를 밀었더니 두 색이
 * 거의 겹쳐서 과녁 하나가 되었고, 겹쳐 그린 두 장이라는 것이 읽히지 않았습니다.
 */
const anaglyph: Motif = (g, p, look) => {
  const other = complement(look.ink)
  const shift = p.w * 0.1
  for (const [dx, color] of [[-shift, other], [shift, look.ink]] as const) {
    const ring = Math.max(5, p.w * 0.17)
    for (let r = ring; r <= p.w * 0.5; r += ring) {
      g.circle(p.cx + dx, p.cy, r).stroke({ color, width: p.hair * 1.5, alpha: 0.8 })
    }
    diamond(g, p.cx + dx, p.cy, p.w * 0.22)
      .stroke({ color, width: p.hair * 1.5, alpha: 0.8 })
    for (const at of [p.y + p.h * 0.16, p.y + p.h * 0.84]) {
      diamond(g, p.cx + dx, at, p.w * 0.12).stroke({ color, width: p.hair * 1.5, alpha: 0.8 })
    }
  }
}

/**
 * 안에서 밖으로 도는 소용돌이.
 *
 * **굵으면 달팽이입니다.** 팔 셋을 두껍게 그었더니 껍데기 하나가 되었습니다 — 소용돌이는
 * 도는 것이 보여야 하고, 도는 것은 **감긴 횟수**에서 나오지 굵기에서 나오지 않습니다.
 */
const plasma: Motif = (g, p, look) => {
  const arms = 4
  const turns = 2.5
  const reach = Math.hypot(p.w, p.h) * 0.46
  for (let arm = 0; arm < arms; arm++) {
    const base = (arm / arms) * Math.PI * 2
    let first = true
    for (let t = 0; t <= 1.001; t += 0.025) {
      // 안쪽이 촘촘하고 바깥이 성깁니다. 고르게 감으면 그것은 나사입니다.
      const angle = base + Math.pow(t, 0.72) * turns * Math.PI * 2
      const r = t * reach
      const x = p.cx + Math.cos(angle) * r
      const y = p.cy + Math.sin(angle) * r * 0.94
      if (first) {
        g.moveTo(x, y)
        first = false
      } else {
        g.lineTo(x, y)
      }
    }
    g.stroke({ color: look.ink, width: p.hair * 1.5, cap: 'round', alpha: 0.95 })
  }
  g.circle(p.cx, p.cy, p.w * 0.06).fill(look.ink)
  g.circle(p.cx, p.cy, p.w * 0.13).stroke({ color: look.ink, width: p.hair, alpha: 0.45 })
}

/**
 * 뒤섞인 조각들.
 *
 * **규칙이 없는 것이 규칙입니다.** 그 덱이 카드 52장을 무작위로 바꾸므로 뒷면도 아무것도
 * 맞아떨어지지 않습니다 — 다만 흩어진 자리는 셈으로 정하므로 늘 같은 그림입니다.
 *
 * **큰 조각 열넷은 흩어진 것이 아니라 늘어놓은 것이었습니다.** 조각이 크면 눈이 하나씩
 * 세고, 세어지는 것은 뒤섞인 것으로 보이지 않습니다 — 작게, 많이, 겹치게.
 */
const erratic: Motif = (g, p, look) => {
  for (let i = 0; i < 54; i++) {
    const x = p.x + (0.04 + scatter(i, 59) * 0.92) * p.w
    const y = p.y + (0.03 + scatter(i, 61) * 0.94) * p.h
    const size = p.w * (0.03 + scatter(i, 67) * 0.075)
    const alpha = 0.35 + scatter(i, 71) * 0.65
    const turn = scatter(i, 89) * Math.PI
    switch (i % 5) {
      case 0:
        g.circle(x, y, size).stroke({ color: look.ink, width: p.hair, alpha })
        break
      case 1:
        diamond(g, x, y, size).fill({ color: look.ink, alpha })
        break
      case 2:
        g.rect(x - size / 2, y - size / 2, size, size)
          .stroke({ color: look.ink, width: p.hair, alpha })
        break
      case 3:
        g.circle(x, y, size * 0.5).fill({ color: look.ink, alpha })
        break
      default:
        g.moveTo(x - Math.cos(turn) * size, y - Math.sin(turn) * size)
          .lineTo(x + Math.cos(turn) * size, y + Math.sin(turn) * size)
          .stroke({ color: look.ink, width: p.hair * 1.2, alpha, cap: 'round' })
        break
    }
  }
}

const MOTIF: Record<CardBackKind, Motif> = {
  [CardBackKind.Classic]: classic,
  [CardBackKind.Checker]: checker,
  [CardBackKind.Starfield]: starfield,
  [CardBackKind.Arcane]: arcane,
  [CardBackKind.Zodiac]: zodiac,
  [CardBackKind.Veil]: veil,
  [CardBackKind.Worn]: worn,
  [CardBackKind.Brush]: brush,
  [CardBackKind.Anaglyph]: anaglyph,
  [CardBackKind.Plasma]: plasma,
  [CardBackKind.Erratic]: erratic,
}

/** 마름모 하나. 채우든 긋든 부르는 쪽이 정합니다. */
function diamond(g: Graphics, x: number, y: number, size: number): Graphics {
  return g.moveTo(x, y - size).lineTo(x + size, y)
    .lineTo(x, y + size).lineTo(x - size, y).closePath()
}

/** 보색. 적청 겹침의 둘째 색이고, 그 하나뿐이라 여기 있습니다. */
function complement(color: number): number {
  return 0xffffff - color
}
