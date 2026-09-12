// 판 안의 부품들.
//
// **모든 판이 같은 부품으로 나뉩니다.** 구획 머리 · 값 칸 · 게이지 · 물건 칸 넷이고,
// 정산 · 상점 · 게임오버 · 왼쪽 판이 이것으로 그려집니다. 판마다 따로 그리면 선의 굵기와
// 여백이 저마다 달라져 한 벌로 보이지 않습니다.
//
// **런타임에 도형을 그리지 않습니다.** 칸과 게이지는 `design-data/tools/ui.py` 가 구운
// 9분할 그림이고, 여기는 그것을 놓고 물들이는 자리입니다. 그림이 아직 오지 않았을 때만
// 지금까지의 길로 그립니다.

import { Container, Graphics, Text } from 'pixi.js'

import { NUMERALS } from './font'
import { glowEdge, piece } from './chrome'
import { wellTint } from '../render/skin'
import { SPACE, TEXT, UI, WEIGHT } from '../render/theme'

/** 구획 머리의 높이. 이름과 그 아래 빛 한 줄입니다. */
export const SECTION_H = 28

/**
 * 구획 머리. 이름 한 줄과 그 아래의 빛 한 줄.
 *
 * **라벨은 12픽셀의 흐린 글입니다.** 자간을 벌려 본문과 갈립니다 — 마름모 같은 표시를
 * 앞에 두지 않습니다. 표시가 자리마다 다르면 그것은 표시가 아니라 장식입니다.
 *
 * **아래 줄은 테두리의 빛입니다.** 머리 판이 끝나는 밑줄은 빛을 두는 다섯 자리 중
 * 하나입니다 — 왼쪽에서 밝게 시작해 오른쪽으로 사라집니다.
 *
 * @param note 이름 옆에 흐리게 붙는 짧은 글. 개수 따위입니다.
 * @param rule 아래에 줄을 둘 것인가. **줄이 곧바로 이어지는 곳에서는 뺍니다.**
 */
export function sectionHead(width: number, title: string, note?: string,
                            rule = true): Container {
  const node = new Container()

  const name = new Text({
    text: title,
    style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal, letterSpacing: 1 },
  })
  name.anchor.set(0, 0.5)
  name.position.set(SPACE.tight, SECTION_H / 2 - 1)
  node.addChild(name)

  if (rule) {
    const glow = glowEdge(width, UI.rule)
    if (glow !== undefined) {
      glow.position.set(0, SECTION_H - 2)
      node.addChild(glow)
    } else {
      const line = new Graphics()
      line.rect(0, SECTION_H - 2, width, 1).fill(UI.rule)
      node.addChild(line)
    }
  }

  if (note) {
    const side = new Text({
      text: note,
      style: { fontSize: TEXT.small, fill: UI.inkFaint, fontWeight: WEIGHT.normal },
    })
    side.anchor.set(0, 0.5)
    side.position.set(SPACE.tight + name.width + SPACE.base, SECTION_H / 2 - 1)
    node.addChild(side)
  }
  return node
}

/**
 * 판 안으로 눌린 칸의 바탕. 없으면 `undefined` 입니다.
 *
 * **위 안쪽의 그늘과 아래의 밝은 줄이 그림 안에 있습니다.** 빛이 위에서 오므로 파인
 * 것은 위가 어둡습니다.
 */
function well(width: number, height: number, alpha = 1): Container {
  const skin = piece('well', width, height, wellTint(UI.cell))
  if (skin !== undefined) {
    skin.alpha = alpha
    return skin
  }
  const g = new Graphics()
  g.rect(0, 0, width, height).fill({ color: UI.cell, alpha })
  return g
}

/**
 * 값 칸. 이름은 왼쪽, 값은 오른쪽 — **한 줄입니다.** 두 단으로 쌓으면 세로를 두 배
 * 차지합니다.
 *
 * **테의 색은 하나입니다.** 칸마다 다른 색 테를 두르면 화면에 색이 여덟 가지가 됩니다 —
 * 무엇의 값인지는 값의 색이 말합니다.
 */
export function valueCell(width: number, height: number, label: string,
                          value: string, ink: number = UI.ink, valueSize = TEXT.base): Container {
  const node = new Container()
  node.addChild(well(width, height))

  const name = new Text({
    text: label,
    style: { fontSize: TEXT.small, fill: UI.inkDim, fontWeight: WEIGHT.normal },
  })
  name.anchor.set(0, 0.5)
  name.position.set(SPACE.wide, height / 2)

  const amount = new Text({
    text: value,
    style: { fontSize: valueSize, fill: ink, fontWeight: WEIGHT.bold, fontFamily: NUMERALS },
  })
  amount.anchor.set(1, 0.5)
  amount.position.set(width - SPACE.wide, height / 2)
  node.addChild(name, amount)
  return node
}

/** 게이지의 높이. 구운 홈의 높이와 같습니다. */
export const GAUGE_H = 12

/**
 * 게이지.
 *
 * **눈금의 끝은 요구 점수가 아닙니다.** 요구 점수를 100% 로 두면 넘긴 만큼이 보이지
 * 않습니다 — 눈금의 끝은 요구의 1.28배와 점수의 1.06배 중 큰 쪽이고, 요구 점수는 그 안의
 * 붉은 눈금 하나입니다. 요구까지는 파랑으로 차고 넘긴 만큼은 금색입니다.
 *
 * `set(score, target)` 으로 채움을 바꿉니다. **홈은 한 번 놓고 채움만 다시 놓습니다.**
 * 비율 하나로 부르던 자리는 `ratio(r)` 로 부릅니다 — 요구가 1 인 눈금입니다.
 */
export class ProgressBar extends Container {
  private readonly fill = new Container()
  private readonly over = new Container()
  private readonly mark = new Graphics()
  private shown = ''

  constructor(private readonly boxWidth: number, boxHeight: number = GAUGE_H,
              private readonly color: number = UI.bar) {
    super()
    const back = piece('gauge', boxWidth, GAUGE_H, wellTint(UI.well))
    if (back !== undefined) this.addChild(back)
    else {
      const g = new Graphics()
      g.rect(0, 0, boxWidth, GAUGE_H).fill(UI.well)
      this.addChild(g)
    }
    void boxHeight
    this.addChild(this.fill, this.over, this.mark)
    this.set(0, 1)
  }

  /** 비율 하나로 부르는 자리. 요구가 1 인 눈금입니다. */
  ratio(value: number): void {
    this.set(Math.max(0, value), 1)
  }

  set(score: number, target: number): void {
    const top = Math.max(target * 1.28, score * 1.06, 1e-9)
    const markAt = target / top
    const filled = Math.min(score, target) / top
    const overBy = Math.max(0, score - target) / top
    // 1/200 아래의 차이는 같은 그림입니다.
    const key = `${Math.round(markAt * 200)}|${Math.round(filled * 200)}|${Math.round(overBy * 200)}`
    if (key === this.shown) return
    this.shown = key

    this.fill.removeChildren().forEach(child => child.destroy())
    this.over.removeChildren().forEach(child => child.destroy())
    this.mark.clear()

    const inner = this.boxWidth - 4
    const fillW = Math.round(inner * filled)
    if (fillW >= 1) this.fill.addChild(this.bar(fillW, this.color, 2))
    const overW = Math.round(inner * overBy)
    if (overW >= 1) this.over.addChild(this.bar(overW, UI.money, 2 + Math.round(inner * markAt)))
    // 요구 점수의 눈금. **붉음입니다** — 넘어야 하는 선이고, 넘긴 뒤에도 남습니다.
    const x = 2 + Math.round(inner * markAt)
    this.mark.rect(x - 1, -2, 2, GAUGE_H + 4).fill(UI.red)
  }

  private bar(width: number, tint: number, x: number): Container {
    const skin = piece('gauge-fill', width, GAUGE_H - 4, tint)
    if (skin !== undefined) {
      skin.position.set(x, 2)
      return skin
    }
    const g = new Graphics()
    g.rect(x, 2, width, GAUGE_H - 4).fill(tint)
    return g
  }
}

/**
 * 물건 칸의 바탕. 눌린 자리에 테 하나.
 *
 * 테의 색이 그 물건의 희귀도이고, 고른 것은 파랑, 빈 것은 옅은 테입니다. **테는 실루엣을
 * 두르는 1픽셀입니다** — 자리는 판을 파낸 것이므로 비어 있을 때 그 파임이 보입니다.
 */
export function cellPlate(width: number, height: number, border: number,
                          empty = false): Container {
  const node = new Container()
  node.addChild(well(width, height, empty ? 0.6 : 1))
  const edge = new Graphics()
  edge.rect(0.5, 0.5, width - 1, height - 1)
    .stroke({ color: border, width: 1, alpha: empty ? 0.45 : 1 })
  node.addChild(edge)
  return node
}

/** 값 글자 하나. 살 수 있으면 노랑, 없으면 붉음. */
export function priceText(cost: number, afford: boolean, size = TEXT.base): Text {
  const text = new Text({
    text: `$${cost}`,
    style: {
      fontSize: size, fontWeight: WEIGHT.bold, fontFamily: NUMERALS,
      fill: afford ? UI.yellow : UI.red,
    },
  })
  text.anchor.set(0.5, 0.5)
  return text
}

/** 얇은 줄 하나. 판 안에서 위아래를 가릅니다. */
export function hairline(width: number, color = UI.hairline): Graphics {
  const g = new Graphics()
  g.rect(0, 0, width, 1).fill(color)
  return g
}
