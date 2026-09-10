// 화면의 글이 코드에 고정되어 있는가.
//
// **열쇠가 빠진 것보다 찾기 어렵습니다.** 빠진 열쇠는 화면에 열쇠가 그대로 나오지만
// (`strings.test.ts` 가 그것을 봅니다), 코드에 고정된 글은 한국어로 잘 보입니다 — 다른
// 말로 켠 사람에게만 그 한 자리가 한국어입니다.
//
// 규칙 알림 판의 `Lv.3` 이 그랬고, 족보 목록에도 같은 것이 있었습니다. 둘 다 눈으로
// 찾았고, 그때까지 아무 게이트도 그것을 보지 않았습니다.
//
// **글자가 있는지로 판정합니다.** 끼워 넣는 자리를 뺀 나머지에 글자가 있으면 그것은
// 말입니다 — 수와 기호(`+2` · `—` · `1 / 3`)는 어느 말에서나 같으므로 고정해도 됩니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { describe as group, expect, it } from 'vitest'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SRC = path.resolve(HERE, '../src')

/** `src/` 아래의 `.ts` 전부. 생성 코드는 화면의 글을 만들지 않으므로 뺍니다. */
function sources(dir: string): string[] {
  const out: string[] = []
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'generated') continue
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) out.push(...sources(full))
    else if (entry.name.endsWith('.ts')) out.push(full)
  }
  return out
}

/**
 * 글을 그대로 넣는 자리.
 *
 * **`text:` 와 `.text =` 둘입니다.** 단추와 떠오르는 글은 글을 인자로 넘기므로 이 규칙으로
 * 잡히지 않고, 인자의 자리로 가리려면 `'neutral'` 같은 갈래 이름과 갈라야 합니다 — 그
 * 자리들은 `t()` · `tf()` 를 쓰는 것으로 지킵니다.
 */
const VISIBLE = /(?:text:\s*|\.text\s*=\s*)(?:'([^'\n]*)'|`([^`\n]*)`)/g

/** 라틴 문자 · 한글 · 가나 · 한자. 이 가운데 하나라도 있으면 말입니다. */
const LETTERS = /[A-Za-z가-힣぀-ヿ一-鿿]/

/** 끼워 넣는 자리. 값이므로 판정에서 뺍니다. */
const HOLE = /\$\{[^}]*\}/g

/**
 * 판정하지 않는 자리.
 *
 * |무엇|왜|
 * |--|--|
 * |`*-preview.ts`|개발용 미리보기 화면입니다. 말을 갈아입지 않습니다|
 * |판 번호|`v` 하나와 값뿐이고, 어느 말에서나 같습니다|
 */
function exempt(where: string, literal: string): boolean {
  if (where.endsWith('-preview.ts')) return true
  return /^v\$\{[^}]*\}$/.test(literal.trim())
}

group('코드에 고정된 글', () => {
  it('화면의 글이 코드에 고정되어 있지 않습니다', () => {
    const found: string[] = []

    for (const file of sources(SRC)) {
      const where = path.relative(SRC, file).split(path.sep).join('/')
      const lines = fs.readFileSync(file, 'utf8').split('\n')

      lines.forEach((line, at) => {
        for (const match of line.matchAll(VISIBLE)) {
          const literal = match[1] ?? match[2] ?? ''
          if (!LETTERS.test(literal.replace(HOLE, ''))) continue
          if (exempt(where, literal)) continue
          found.push(`${where}:${at + 1} ${literal.slice(0, 40)}`)
        }
      })
    }

    expect(found, found.join(' · ')).toEqual([])
  })
})
