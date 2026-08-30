// 강조가 붙은 한 줄.
//
// **숫자와 이름은 글의 나머지와 다른 색입니다.** 「안테 8까지 블라인드를 차례로 격파합니다」
// 에서 사람이 찾는 것은 `8` 이고, 그것이 문장과 같은 색이면 문장을 처음부터 읽어야 찾습니다.
//
// 글 하나에 색을 여럿 쓰는 방법이 둘입니다 — 브라우저에 HTML 을 그리게 하거나, 조각을 여럿
// 만들어 나란히 놓거나. **뒤쪽을 씁니다.** 앞쪽은 글꼴이 화면의 나머지와 미묘하게 달라지고,
// 이 화면의 글은 이미 한 벌로 맞춰져 있습니다.
//
// 대신 **줄바꿈을 하지 않습니다.** 한 줄이 들어오면 한 줄이 나갑니다 — 부르는 쪽이 이미
// 줄을 나누어 두었기 때문입니다.

import { Container, Text, type TextStyleOptions } from 'pixi.js'

/**
 * 강조할 자리를 찾는 규칙.
 *
 * **셋입니다** — 수(`8` · `1/4` · `2.5`), 값에 붙는 기호가 있는 수(`$3` · `×2` · `+40` ·
 * `Lv.3` · `8회` · `10%`), 그리고 「」 로 묶은 이름. 그 밖의 것은 강조하지 않습니다 —
 * 전부 강조하면 아무것도 강조되지 않습니다.
 */
const MARK = /(「[^」]*」)|([$×+\-]?\d+(?:[./]\d+)*\s*(?:회|장|개|%|배)?)|(Lv\.\d+)/g

export interface RichStyle {
  /** 글의 바탕 모습. */
  base: TextStyleOptions
  /** 수의 색. */
  number: number
  /** 「」 로 묶은 이름의 색. */
  term: number
}

/**
 * 강조가 붙은 한 줄을 만듭니다.
 *
 * 조각들을 왼쪽부터 나란히 붙입니다. 통의 넓이가 곧 그 줄의 넓이이므로, 부르는 쪽이 가운데
 * 맞춤을 하려면 `container.width` 를 보면 됩니다.
 */
export function richLine(text: string, style: RichStyle): Container {
  const line = new Container()
  let cursor = 0
  let x = 0

  const put = (part: string, fill?: number) => {
    if (part === '') return
    const node = new Text({
      text: part,
      style: fill === undefined ? style.base : { ...style.base, fill, fontWeight: '800' },
    })
    node.position.set(x, 0)
    line.addChild(node)
    x += node.width
  }

  for (const found of text.matchAll(MARK)) {
    const at = found.index ?? 0
    put(text.slice(cursor, at))
    put(found[0], found[1] !== undefined ? style.term : style.number)
    cursor = at + found[0].length
  }
  put(text.slice(cursor))

  return line
}

/**
 * 여러 줄을 위에서 아래로.
 *
 * **줄은 이미 나뉘어 들어옵니다.** 여기서 나누면 조각마다 넓이를 재어 가며 접어야 하고,
 * 그것은 글자 한 벌을 두 번 굽는 일입니다.
 */
export function richBlock(lines: readonly string[], style: RichStyle,
                          lineHeight: number): Container {
  const block = new Container()
  lines.forEach((text, index) => {
    const line = richLine(text, style)
    line.position.set(0, index * lineHeight)
    block.addChild(line)
  })
  return block
}
