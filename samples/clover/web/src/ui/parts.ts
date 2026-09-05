// 판 안의 부품들.
//
// **모든 판이 같은 부품으로 나뉩니다.** 구획 머리 · 값 칸 · 진행 바 · 물건 칸 넷이고,
// 정산 · 상점 · 게임오버 · 왼쪽 판이 이것으로 그려집니다. 판마다 따로 그리면 선의 굵기와
// 여백이 저마다 달라져 한 벌로 보이지 않습니다.
//
// 그리는 것은 단색 채우기와 테와 글뿐입니다 — 그라디언트 · 그림자 · 두께가 없습니다.

import { Container, Graphics, Text } from 'pixi.js'

import { NUMERALS } from './font'
import { insetRadius } from '../render/skin'
import { COLOR, UI } from '../render/theme'

/** 구획 머리의 높이. 마름모 · 이름 · 아래 선 하나입니다. */
export const SECTION_H = 28

/**
 * 구획 머리. 「◈ 이름」 과 그 아래 선 하나.
 *
 * **마름모는 그린 것입니다.** 글자로 두면 글꼴마다 크기와 자리가 달라집니다 — 작은
 * 정사각형을 45도 돌린 테 하나입니다.
 *
 * @param note 이름 옆에 흐리게 붙는 짧은 글. 개수 따위입니다.
 * @param rule 아래에 선을 그을 것인가. **줄이 곧바로 이어지는 곳에서는 뺍니다** — 그 줄들이
 *   저마다 칸을 두르고 있으면 선과 첫 칸의 테가 두 줄로 겹칩니다.
 */
export function sectionHead(width: number, title: string, note?: string,
                            rule = true): Container {
  const node = new Container()
  const mark = new Graphics()
  mark.rect(-4.5, -4.5, 9, 9).stroke({ color: UI.mark, width: 1.5 })
  mark.rotation = Math.PI / 4
  mark.position.set(6, SECTION_H / 2 - 1)

  const name = new Text({
    text: title,
    style: { fontSize: 13, fill: COLOR.ink, fontWeight: '800' },
  })
  name.anchor.set(0, 0.5)
  name.position.set(20, SECTION_H / 2 - 1)

  node.addChild(mark, name)
  if (rule) {
    const line = new Graphics()
    line.rect(0, SECTION_H - 1.5, width, 1.5).fill(UI.rule)
    node.addChild(line)
  }

  if (note) {
    const side = new Text({
      text: note,
      style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
    })
    side.anchor.set(0, 0.5)
    side.position.set(20 + name.width + 8, SECTION_H / 2 - 1)
    node.addChild(side)
  }
  return node
}

/**
 * 값 칸. 이름은 왼쪽, 값은 오른쪽.
 *
 * **테의 색은 하나입니다.** 칸마다 다른 색 테를 두르면 화면에 색이 여덟 가지가 됩니다 —
 * 무엇의 값인지는 값의 색이 말합니다.
 */
export function valueCell(width: number, height: number, label: string,
                          value: string, ink: number = COLOR.ink, valueSize = 16): Container {
  const node = new Container()
  const box = new Graphics()
  box.roundRect(0, 0, width, height, 6).fill(UI.cell)
  box.roundRect(0.5, 0.5, width - 1, height - 1, insetRadius(6, 0.5))
    .stroke({ color: UI.hairline, width: 1 })

  const name = new Text({
    text: label,
    style: { fontSize: 12, fill: COLOR.inkDim, fontWeight: '700' },
  })
  name.anchor.set(0, 0.5)
  name.position.set(12, height / 2)

  const amount = new Text({
    text: value,
    style: { fontSize: valueSize, fill: ink, fontWeight: '800', fontFamily: NUMERALS },
  })
  amount.anchor.set(1, 0.5)
  amount.position.set(width - 12, height / 2)
  node.addChild(box, name, amount)
  return node
}

/**
 * 진행 바.
 *
 * `set(ratio)` 로 채움을 바꿉니다. **바탕은 한 번 그리고 채움만 다시 그립니다** —
 * 프레임마다 불리는 자리에 두 도형을 다 다시 만들 이유가 없습니다.
 */
export class ProgressBar extends Container {
  private readonly fill = new Graphics()
  private shown = -1

  constructor(private readonly boxWidth: number, private readonly boxHeight: number,
              private readonly color: number = UI.bar) {
    super()
    const back = new Graphics()
    back.roundRect(0, 0, boxWidth, boxHeight, boxHeight / 2).fill(UI.well)
    back.roundRect(0.5, 0.5, boxWidth - 1, boxHeight - 1, insetRadius(boxHeight / 2, 0.5))
      .stroke({ color: UI.hairline, width: 1 })
    this.addChild(back, this.fill)
    this.set(0)
  }

  set(ratio: number): void {
    const clamped = Math.max(0, Math.min(1, ratio))
    // 1/200 아래의 차이는 같은 그림입니다.
    const step = Math.round(clamped * 200) / 200
    if (step === this.shown) return
    this.shown = step
    this.fill.clear()
    const w = (this.boxWidth - 2) * step
    if (w < 1) return
    this.fill.roundRect(1, 1, w, this.boxHeight - 2, (this.boxHeight - 2) / 2)
      .fill(this.color)
  }
}

/**
 * 물건 칸의 바탕. 어두운 채우기에 테 하나.
 *
 * 테의 색이 그 물건의 희귀도이고, 고른 것은 파랑, 빈 것은 옅은 테입니다.
 */
export function cellPlate(width: number, height: number, border: number,
                          empty = false): Graphics {
  const g = new Graphics()
  g.roundRect(0, 0, width, height, 6).fill({ color: UI.cell, alpha: empty ? 0.6 : 1 })
  g.roundRect(0.75, 0.75, width - 1.5, height - 1.5, insetRadius(6, 0.75))
    .stroke({ color: border, width: 1.5, alpha: empty ? 0.45 : 1 })
  return g
}

/** 값 글자 하나. 살 수 있으면 노랑, 없으면 붉음. */
export function priceText(cost: number, afford: boolean, size = 15): Text {
  const text = new Text({
    text: `$${cost}`,
    style: {
      fontSize: size, fontWeight: '800', fontFamily: NUMERALS,
      fill: afford ? UI.yellow : UI.red,
    },
  })
  text.anchor.set(0.5, 0.5)
  return text
}

/** 얇은 가로선 하나. 줄과 줄을 가릅니다. */
export function hairline(width: number, color = UI.hairline): Graphics {
  const g = new Graphics()
  g.rect(0, 0, width, 1).fill(color)
  return g
}
