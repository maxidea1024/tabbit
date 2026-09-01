// 강조가 붙은 한 줄.
//
// **숫자와 이름은 글의 나머지와 다른 색입니다.** 「안테 8까지 블라인드를 차례로 격파합니다」
// 에서 사람이 찾는 것은 `8` 이고, 그것이 문장과 같은 색이면 문장을 처음부터 읽어야 찾습니다.
//
// 글 하나에 색을 여럿 쓰는 방법이 둘입니다 — 브라우저에 HTML 을 그리게 하거나, 조각을 여럿
// 만들어 나란히 놓거나. **뒤쪽을 씁니다.** 앞쪽은 글꼴이 화면의 나머지와 미묘하게 달라지고,
// 이 화면의 글은 이미 한 벌로 맞춰져 있습니다.
//
// **넓이를 주면 접습니다.** 주지 않으면 한 줄이 한 줄로 나갑니다 — 부르는 쪽이 이미 줄을
// 나누어 둔 자리가 있기 때문입니다.

import { CanvasTextMetrics, Container, Text, TextStyle, type TextStyleOptions } from 'pixi.js'

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

/** 같은 색으로 이어지는 글 한 토막. */
interface Run {
  text: string
  fill?: number
}

/** 글을 색이 같은 토막들로 나눕니다. */
function runsOf(text: string, style: RichStyle): Run[] {
  const runs: Run[] = []
  let cursor = 0

  const put = (part: string, fill?: number) => {
    if (part !== '') runs.push({ text: part, fill })
  }

  for (const found of text.matchAll(MARK)) {
    const at = found.index ?? 0
    put(text.slice(cursor, at))
    put(found[0], found[1] !== undefined ? style.term : style.number)
    cursor = at + found[0].length
  }
  put(text.slice(cursor))

  return runs
}

/**
 * 글을 접을 수 있는 조각으로.
 *
 * 로마자와 한글 낱말은 빈칸 뒤에서, 한중일 글자는 글자마다 끊습니다.
 */
function splitWords(text: string): string[] {
  const out: string[] = []
  let buffer = ''

  const flush = () => {
    if (buffer !== '') out.push(buffer)
    buffer = ''
  }

  const letters = [...text]
  for (let i = 0; i < letters.length; i++) {
    const letter = letters[i]
    buffer += letter
    if (/\s/.test(letter)) {
      flush()
      continue
    }
    if (!CJK.test(letter)) continue
    // 다음 글자가 줄머리에 설 수 없으면 여기서 끊지 않습니다.
    const next = letters[i + 1]
    if (next !== undefined && NO_HEAD.test(next)) continue
    if (NO_TAIL.test(letter)) continue
    flush()
  }
  flush()
  return out
}

/** 그 토막의 모습. 강조는 굵게 갑니다. */
function styleOf(style: RichStyle, fill?: number): TextStyleOptions {
  return fill === undefined ? style.base : { ...style.base, fill, fontWeight: '800' }
}

/**
 * 글자를 세지 않고 넓이만 잽니다.
 *
 * **표시 객체를 만들지 않습니다** — 접을 자리를 찾느라 낱말마다 `Text` 를 만들면 마우스를
 * 올릴 때마다 수십 개를 만들었다 버립니다.
 */
function widthOf(text: string, style: TextStyleOptions): number {
  return CanvasTextMetrics.measureText(text, new TextStyle(style)).width
}

/**
 * 한중일 글자.
 *
 * **이 글자들은 낱말 사이에 빈칸이 없습니다.** 빈칸에서만 끊으면 문장 하나가 통째로 한
 * 덩어리가 되어, 접을 자리를 못 찾고 한 줄이 넘칩니다.
 */
const CJK = /[぀-ヿ㐀-䶿一-鿿가-힣＀-ﾟ]/

/**
 * 그 글자 앞에서는 끊지 않습니다. 줄머리에 설 수 없는 것들입니다.
 *
 * **반각도 넣습니다.** 한국어는 온각 쉼표가 아니라 반각을 쓰므로, 온각만 막으면 「있고」
 * 다음에서 끊겨 쉼표가 다음 줄의 첫 글자가 됩니다.
 */
const NO_HEAD = /[。、，．・：；？！）」』】〉》〕｝々,.!?:;）\)\]}ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮー]/

/** 그 글자 뒤에서는 끊지 않습니다. 줄끝에 설 수 없는 것들입니다. */
const NO_TAIL = /[（「『【〈《〔｛]/

/**
 * 접을 수 있는 자리로 나눕니다.
 *
 * 빈칸 뒤에서 나눕니다. **한중일 글자는 글자마다 나눕니다** — 그 말들은 낱말 사이에 빈칸이
 * 없어서, 빈칸만 보면 문장 하나가 통째로 한 덩어리가 됩니다. 다만 줄머리에 설 수 없는
 * 글자와 줄끝에 설 수 없는 글자는 앞 조각에 붙입니다.
 *
 * **한 낱말이 통보다 넓으면 글자로 나눕니다** — 그러지 않으면 그 낱말 하나가 통 밖으로
 * 나갑니다.
 */
function piecesOf(text: string, style: TextStyleOptions, maxWidth: number): string[] {
  const words = splitWords(text)
  const out: string[] = []

  for (const word of words) {
    if (word === '') continue
    if (widthOf(word, style) <= maxWidth) {
      out.push(word)
      continue
    }
    let piece = ''
    for (const letter of word) {
      if (piece !== '' && widthOf(piece + letter, style) > maxWidth) {
        out.push(piece)
        piece = ''
      }
      piece += letter
    }
    if (piece !== '') out.push(piece)
  }

  return out
}

/**
 * 토막들을 통에 놓습니다.
 *
 * `maxWidth` 가 없으면 한 줄로 갑니다. 있으면 넘칠 때마다 다음 줄로 내립니다. 돌려주는 것은
 * **몇 줄이 되었는가** 입니다 — 부르는 쪽이 다음 글을 어디에 놓을지 그것으로 정합니다.
 */
function place(runs: Run[], style: RichStyle, into: Container,
               top: number, lineHeight: number, maxWidth?: number,
               align: Align = 'left'): number {
  let x = 0
  let row = 0

  // **줄마다 무엇이 놓였는지 들고 있습니다.** 가운데로 모으려면 그 줄이 얼마나 넓은지를
  // 알아야 하는데, 그것은 그 줄을 다 놓아 본 뒤에야 알 수 있습니다 — 조각을 놓으면서
  // 가운데로 밀 수는 없습니다.
  const rows: { nodes: Text[]; width: number }[] = []

  const put = (part: string, fill?: number) => {
    if (part === '') return
    const node = new Text({ text: part, style: styleOf(style, fill) })
    node.position.set(x, top + row * lineHeight)
    into.addChild(node)
    x += node.width
    const line = rows[row] ?? (rows[row] = { nodes: [], width: 0 })
    line.nodes.push(node)
    line.width = x
  }

  for (const run of runs) {
    if (maxWidth === undefined) {
      put(run.text, run.fill)
      continue
    }

    const shape = styleOf(style, run.fill)
    // **「」 로 묶은 이름은 쪼개지 않습니다.** 「리롤」 이 「리 / 롤」 로 끊기면 그것이 한
    // 낱말이라는 것이 사라집니다 — 통보다 넓을 때만 어쩔 수 없이 나눕니다.
    const whole = run.fill === style.term && widthOf(run.text, shape) <= maxWidth
    let buffer = ''
    for (const piece of whole ? [run.text] : piecesOf(run.text, shape, maxWidth)) {
      const grown = buffer + piece
      if (x + widthOf(grown, shape) > maxWidth && (buffer !== '' || x > 0)) {
        // **줄 끝의 빈칸은 버립니다.** 남겨 두면 다음 줄이 한 칸 밀려 시작합니다.
        put(buffer.replace(/\s+$/, ''), run.fill)
        buffer = ''
        x = 0
        row++
      }
      buffer += piece
    }
    put(buffer, run.fill)
  }

  // 가운데로 모읍니다. **줄마다 따로입니다** — 접혀서 둘이 된 글은 두 줄의 너비가 다르고,
  // 한 덩어리로 밀면 짧은 줄이 긴 줄에 딸려가 어느 쪽으로도 맞지 않습니다.
  if (align === 'center' && maxWidth !== undefined) {
    for (const line of rows) {
      // 빈 칸이 있을 수 있습니다 — 아무것도 놓이지 않은 줄은 통을 만들지 않습니다.
      if (!line) continue
      const shift = Math.round((maxWidth - line.width) / 2)
      if (shift <= 0) continue
      for (const node of line.nodes) node.x += shift
    }
  }

  return row + 1
}

/**
 * 강조가 붙은 한 줄을 만듭니다.
 *
 * `maxWidth` 를 주면 그 넓이에서 접습니다. 통의 넓이가 곧 그 줄의 넓이이므로, 부르는 쪽이
 * 가운데 맞춤을 하려면 `container.width` 를 보면 됩니다.
 */
export function richLine(text: string, style: RichStyle,
                         maxWidth?: number, lineHeight = 17): Container {
  const line = new Container()
  place(runsOf(text, style), style, line, 0, lineHeight, maxWidth)
  return line
}

/**
 * 여러 줄을 위에서 아래로.
 *
 * **접힌 줄만큼 아래가 밀립니다.** 줄 수를 미리 세어 두고 자리를 잡으면, 긴 줄 하나가
 * 다음 줄 위에 겹쳐 그려집니다.
 */
/** 줄을 어느 쪽으로 모으는가. 가운데로 모으려면 접는 폭이 있어야 합니다. */
export type Align = 'left' | 'center'

export function richBlock(lines: readonly string[], style: RichStyle,
                          lineHeight: number, maxWidth?: number,
                          align: Align = 'left'): Container {
  const block = new Container()
  let row = 0
  for (const text of lines) {
    row += place(runsOf(text, style), style, block, row * lineHeight, lineHeight,
      maxWidth, align)
  }
  return block
}
