// 효과를 문장으로 만드는 데 쓰는 문구가 전부 시트에 있는가.
//
// **빠진 것은 화면에 열쇠가 그대로 나옵니다.** `describe()` 가 없는 문구에 «표시» 를 달아
// 눈에 띄게 하지만, 눈에 띄는 것은 그 조커를 실제로 만난 사람뿐입니다 — 조커 500종 가운데
// 그 하나를 만나기까지 판을 몇 번 돌아야 하는지가 그 게이트의 성능이었습니다.
//
// 실제로 `phrase.scope.RandomInDeck` 과 여섯이 빠진 채로 지나갔습니다.

import * as path from 'path'
import { fileURLToPath } from 'url'

import { describe as group, expect, it } from 'vitest'

import { describe as sentences, PHRASE_FAMILIES } from '../src/core/describe'
import { loadFromDisk } from '../src/core/load-node'
import type { EffectIndex } from '../src/core/data'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const DATA = path.resolve(HERE, '../public/data')
const data = loadFromDisk(DATA)

/** 시트에 있는 열쇠 전부. */
const keys = new Set(data.tables.stringTable.records.map(row => row.stringId))

/** 효과를 가진 갈래 전부. **하나를 빼면 그 갈래의 문구만 빠진 채로 지나갑니다.** */
const FAMILIES: [string, EffectIndex][] = [
  ['joker', data.jokerEffects],
  ['tarot', data.tarotEffects],
  ['spectral', data.spectralEffects],
  ['boss', data.bossEffects],
  ['voucher', data.voucherEffects],
  ['tag', data.tagEffects],
  ['deck', data.deckEffects],
  ['challenge', data.challengeEffects],
  ['enhancement', data.enhancementEffects],
  ['seal', data.sealEffects],
]

group('문구', () => {
  it('갈래마다의 enum 이름이 실재합니다', () => {
    for (const [family, enumName] of Object.entries(PHRASE_FAMILIES)) {
      expect(data.enumNames[enumName], `${family} → ${enumName}`).toBeDefined()
    }
  })

  it('enum 값마다 문구가 있습니다', () => {
    const missing: string[] = []
    for (const [family, enumName] of Object.entries(PHRASE_FAMILIES)) {
      for (const name of Object.values(data.enumNames[enumName] ?? {})) {
        const want = `phrase.${family}.${name}`
        if (!keys.has(want)) missing.push(want)
      }
    }
    expect(missing, missing.join(' · ')).toEqual([])
  })

  it('설명에 «표시» 가 남지 않습니다', () => {
    // **문장을 실제로 만들어 봅니다.** 열쇠를 세는 것만으로는 조건과 연산의 변종이 빠진
    // 것을 볼 수 없고, 화면에 나오는 것은 이 문장입니다.
    const found: string[] = []
    for (const [family, index] of FAMILIES) {
      for (const [owner, rows] of index) {
        for (const line of sentences(data, rows)) {
          if (line.includes('«')) found.push(`${family}/${owner}: ${line}`)
        }
      }
    }
    expect(found, found.slice(0, 5).join('\n')).toEqual([])
  })
})
