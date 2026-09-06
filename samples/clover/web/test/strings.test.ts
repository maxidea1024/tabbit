// 화면이 찾는 글이 시트에 다 있는가.
//
// **빠진 열쇠는 화면에 열쇠가 그대로 나옵니다.** `«표시»` 를 다는 `phrase.*` 와 달리
// `ui.*` 와 `rule.*` 는 아무 표시 없이 지나가고, 그 자리를 실제로 지나간 사람만 봅니다 —
// `rule.blind_size_scale_bp.name` 이 상점 판에 그대로 떠 있었습니다.
//
// **번역이 빈 칸도 봅니다.** 그것은 한국어로 떨어지므로 열쇠가 보이지 않고, 다른 말로 켠
// 사람에게만 보입니다 — 로그인 화면 전체가 그렇게 한국어였습니다.
//
// 문구(`phrase.*`)는 [`phrase.test.ts`](phrase.test.ts) 가 봅니다.

import * as fs from 'fs'
import * as path from 'path'
import { fileURLToPath } from 'url'

import { describe as group, expect, it } from 'vitest'

import { loadFromDisk } from '../src/core/load-node'
import { defaultRules } from '../src/core/run'
import { stakeSlug } from '../src/core/stake'
import { tierSlug } from '../src/core/tier'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const DATA = path.resolve(HERE, '../public/data')
const SRC = path.resolve(HERE, '../src')
const data = loadFromDisk(DATA)

const keys = new Set(data.tables.stringTable.records.map(row => row.stringId))

/** `src/` 아래의 `.ts` 전부. 생성 코드는 글을 찾지 않으므로 뺍니다. */
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
 * 코드에 적힌 열쇠 전부.
 *
 * **작은따옴표로 적힌 것만 봅니다.** 역따옴표로 조립하는 열쇠는 값이 무엇인지 여기서 알 수
 * 없고, 그런 것들은 아래에서 갈래마다 따로 셉니다.
 */
const CALL = /(?<![\w.])(?:t|tf|text|textWith)\s*\(\s*(?:this\.data|data|table)?\s*,?\s*'([^'\n]+)'/g

function literalKeys(): Map<string, string> {
  const found = new Map<string, string>()
  for (const file of sources(SRC)) {
    const body = fs.readFileSync(file, 'utf8')
    for (const match of body.matchAll(CALL)) {
      const key = match[1]
      if (!key.includes('.')) continue
      if (!found.has(key)) found.set(key, path.relative(SRC, file))
    }
  }
  return found
}

group('글 표', () => {
  it('코드에 적힌 열쇠가 시트에 있습니다', () => {
    const missing: string[] = []
    for (const [key, where] of literalKeys()) {
      if (!keys.has(key)) missing.push(`${key} (${where})`)
    }
    expect(missing, missing.join(' · ')).toEqual([])
  })

  it('규칙마다 이름이 있습니다', () => {
    // **`Rules` 의 필드 이름입니다.** 「적용 중」 목록과 쪽지가 그 이름으로 글을 찾습니다 —
    // `RuleKind` 의 이름으로 적어 두면 이름이 다른 넷만 열쇠가 그대로 나옵니다.
    const missing = Object.keys(defaultRules(data))
      .map(field => `rule.${field.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase()}.name`)
      .filter(key => !keys.has(key))
    expect(missing, missing.join(' · ')).toEqual([])
  })

  it('이름을 가진 것마다 이름이 있습니다', () => {
    // **시트의 `name` 칸으로 떨어지면 그 자리만 한국어로 남습니다.** 열쇠가 없어도 화면이
    // 서기 때문에 눈에 띄지 않고, 그래서 등급과 카드 세트가 오래 한국어였습니다.
    const groups: [string, string[]][] = [
      ['joker', data.tables.joker.records.map(row => row.jokerId)],
      ['tarot', data.tables.tarot.records.map(row => row.tarotId)],
      ['spectral', data.tables.spectral.records.map(row => row.spectralId)],
      ['planet', data.tables.planet.records.map(row => row.planetId)],
      ['voucher', data.tables.voucher.records.map(row => row.voucherId)],
      ['tag', data.tables.tag.records.map(row => row.tagId)],
      ['boss', data.tables.bossBlind.records.map(row => row.bossId)],
      ['deck', data.tables.deck.records.map(row => row.deckId)],
      ['challenge', data.tables.challenge.records.map(row => row.challengeId)],
      ['cardset', data.tables.cardSet.records.map(row => row.setId)],
      ['stake', data.tables.stake.records.map(row => stakeSlug(row.stake))],
      ['tier', data.tables.tier.records
        .filter(row => row.tier !== 0)
        .map(row => tierSlug(row.tier))],
      ['hand', data.tables.pokerHand.records
        .map(row => data.enumNames.PokerHandKind?.[row.hand] ?? '')],
    ]

    const missing: string[] = []
    for (const [what, ids] of groups) {
      for (const id of ids) {
        if (!keys.has(`${what}.${id}.name`)) missing.push(`${what}.${id}.name`)
      }
    }
    expect(missing, missing.slice(0, 8).join(' · ')).toEqual([])
  })

  it('심판이 거절한 사유마다 글이 있습니다', () => {
    // 서버의 `RejectReason` 입니다. **여기서 가져올 수 없어 적어 둡니다** — 서버를 웹이
    // 참조하지 않고, 그 갈래가 늘면 이 줄도 늘어야 합니다.
    const reasons = ['invalid_action', 'unfinished', 'too_long']
    const missing = reasons.map(one => `ui.lb.fail.${one}`).filter(key => !keys.has(key))
    expect(missing, missing.join(' · ')).toEqual([])
  })

  it('번역에 빈 칸이 없습니다', () => {
    // 한국어가 원본입니다 — 그것이 비어 있으면 열쇠가 남은 줄이고, 나머지가 비어 있으면
    // 그 말로 켠 사람에게 한국어가 보입니다.
    const empty: string[] = []
    for (const row of data.tables.stringTable.records) {
      if (row.ko === '') {
        // 조건이 없는 효과의 조건문은 일부러 빈 칸입니다.
        if (row.en === '' && row.ja === '' && row.de === '') continue
        empty.push(`${row.stringId}: ko`)
        continue
      }
      for (const lang of ['en', 'ja', 'zhHans', 'zhHant', 'de'] as const) {
        if (row[lang] === '') empty.push(`${row.stringId}: ${lang}`)
      }
    }
    expect(empty, empty.slice(0, 8).join(' · ')).toEqual([])
  })
})
