// 색과 크기를 손으로 적은 자리.
//
// **색은 두 파일에만 있어야 합니다** — 겉면의 씨앗(`render/theme.ts`)과 겉면을 따라가지
// 않는 것들(`render/ink.ts`)입니다. 그리는 자리에 남은 리터럴은 어느 쪽에도 속하지 않은
// 것이고, 겉면을 갈아입어도 그 자리만 앞 색으로 남습니다.
//
// **이름이 붙은 상수는 통과입니다.** 그 자리에서만 쓰는 색이라도 이름이 붙어 있으면 고칠 때
// 찾을 수 있습니다 — 이 게이트가 막는 것은 그리는 줄 한가운데의 수입니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'
import { describe, expect, it } from 'vitest'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const SRC = path.resolve(HERE, '../src')

/** 색을 손으로 적어도 되는 파일. */
const HOME = ['render/ink.ts', 'render/theme.ts', 'render/palette.ts']
/** 색이 아닌 수. 해시의 씨앗과 비트를 뒤집는 자리입니다. */
const NOT_COLOR = ['core/hash.ts', 'core/rng.ts', 'render/card-back.ts']
/** 미리보기 도구. 화면이 아닙니다. */
const TOOLS = /-preview\.ts$/

const HEX = /0x[0-9a-fA-F]{6}\b/
/** 이름을 짓는 줄. `const X = 0x…` 과 표의 한 줄입니다. */
const NAMING = /^(export\s+)?const\s|^\[?[\w.[\]]+]?\s*:\s*0x[0-9a-fA-F]{6},?$/

/** 글자 크기를 손으로 적어도 되는 파일. 카드에 인쇄되는 글입니다. */
const PRINTED = ['render/faces.ts', 'render/card-face.ts', 'render/card-view.ts',
                 'render/joker-view.ts', 'render/glyph.ts', 'ui/collection.ts',
                 'ui/wordmark.ts']
const SIZE = /fontSize:\s*\d/

function walk(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) return entry.name === 'generated' ? [] : walk(full)
    return entry.name.endsWith('.ts') ? [full] : []
  })
}

function scan(skip: string[], hit: RegExp): string[] {
  const loose: string[] = []
  for (const full of walk(SRC)) {
    const rel = path.relative(SRC, full).split(path.sep).join('/')
    if (skip.includes(rel) || TOOLS.test(rel)) continue
    const lines = fs.readFileSync(full, 'utf8').split(/\r?\n/)
    lines.forEach((line, at) => {
      const body = line.trim()
      if (!hit.test(body) || NAMING.test(body)) return
      loose.push(`${rel}:${at + 1} ${body.slice(0, 64)}`)
    })
  }
  return loose
}

describe('손으로 적은 값', () => {
  it('그리는 줄 한가운데에 색이 적혀 있지 않습니다', () => {
    expect(scan([...HOME, ...NOT_COLOR], HEX)).toEqual([])
  })

  it('화면의 글자 크기가 전부 `TEXT` 에서 나옵니다', () => {
    expect(scan(PRINTED, SIZE)).toEqual([])
  })
})
