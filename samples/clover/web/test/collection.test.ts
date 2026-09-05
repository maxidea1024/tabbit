// 콜렉션 — 발견의 판정.
//
// **판정 기준이 둘입니다.** 발견이 갈래를 섞지 않고 같은 것을 두 번 적지 않아야 하고,
// 한 판을 돌린 뒤 손에 든 것과 진열된 것이 전부 적혀 있어야 합니다.
//
// 해시가 그대로인지는 `determinism.test.ts` 가 봅니다 — 이 파일이 코어에 닿지 않았다는
// 판정은 거기입니다.

import { describe, expect, it } from 'vitest'

import { JokerPool } from '../src/generated/enums/joker-pool'
import { EditionKind } from '../src/generated/enums/edition-kind'
import { EnhancementKind } from '../src/generated/enums/enhancement-kind'
import { SealKind } from '../src/generated/enums/seal-kind'
import { ShopItemKind } from '../src/generated/enums/shop-item-kind'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun } from '../src/core/run'
import { autoplay } from '../src/headless'
import {
  COLLECTION_GROUPS, countOf, discover, emptyCollection, seen, sightings,
} from '../src/core/collection'
import type { RunState } from '../src/core/state'

const DATA = 'public/data'
const data = loadFromDisk(DATA)

/**
 * 한 판을 자동으로 두고 마지막 상태를 냅니다.
 *
 * **`autoplay` 는 상태를 내지 않습니다** — 리플레이를 굽는 것이 그 함수의 일이므로,
 * 그것이 낸 액션을 여기서 다시 돌려 상태를 얻습니다.
 */
function runOf(seed: string, steps: number): RunState {
  const run = autoplay(seed, 'red_deck', 'White', steps, DATA)
  let state = newRun(data, seed, 'red_deck', 'White', [JokerPool.Base]).state
  for (const action of run.replay.actions) state = apply(data, state, action).state
  return state
}

describe('적는 규칙', () => {
  it('빈 저장은 갈래마다 빈 목록이다', () => {
    const progress = emptyCollection()
    for (const group of COLLECTION_GROUPS) {
      expect(progress[group]).toEqual([])
    }
  })

  it('같은 것을 두 번 적지 않는다', () => {
    const progress = emptyCollection()
    expect(discover(progress, [{ group: 'joker', id: 'a' }])).toBe(true)
    expect(discover(progress, [{ group: 'joker', id: 'a' }])).toBe(false)
    expect(progress.joker).toEqual(['a'])
  })

  it('갈래를 섞지 않는다', () => {
    const progress = emptyCollection()
    discover(progress, [{ group: 'joker', id: 'a' }, { group: 'tarot', id: 'a' }])
    expect(progress.joker).toEqual(['a'])
    expect(progress.tarot).toEqual(['a'])
    expect(seen(progress, 'planet', 'a')).toBe(false)
  })

  it('빈 식별자는 적지 않는다', () => {
    const progress = emptyCollection()
    expect(discover(progress, [{ group: 'boss', id: '' }])).toBe(false)
    expect(countOf(progress, 'boss')).toBe(0)
  })

  it('한 목록에 든 같은 것도 한 번만 적는다', () => {
    const progress = emptyCollection()
    expect(discover(progress, [
      { group: 'tag', id: 'x' }, { group: 'tag', id: 'x' },
    ])).toBe(true)
    expect(progress.tag).toEqual(['x'])
  })
})

describe('보이는 것', () => {
  it('판을 열면 덱과 스테이크와 블라인드 셋이 보인다', () => {
    const { state } = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base])
    const progress = emptyCollection()
    discover(progress, sightings(state))
    expect(progress.deck).toEqual(['red_deck'])
    expect(progress.stake).toEqual(['White'])
    expect(progress.blind.sort()).toEqual(['Big', 'Boss', 'Small'])
    // 이번 안테의 보스는 고르는 판에 적혀 있습니다.
    expect(countOf(progress, 'boss')).toBeGreaterThan(0)
  })

  it('아무것도 붙지 않은 카드는 강화도 인장도 에디션도 아니다', () => {
    const { state } = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base])
    const progress = emptyCollection()
    discover(progress, sightings(state))
    // 붉은 덱의 52장은 전부 맨 카드입니다.
    expect(progress.enhancement).toEqual([])
    expect(progress.seal).toEqual([])
    expect(progress.edition).toEqual([])
  })

  it('상점에 진열된 것은 사지 않아도 보인다', () => {
    const state = runOf('CLOVER-0001', 400)
    const progress = emptyCollection()
    discover(progress, sightings(state))
    for (const item of state.shop.cards) {
      if (item.kind === ShopItemKind.Joker) expect(seen(progress, 'joker', item.id)).toBe(true)
      if (item.kind === ShopItemKind.Tarot) expect(seen(progress, 'tarot', item.id)).toBe(true)
      if (item.kind === ShopItemKind.Planet) expect(seen(progress, 'planet', item.id)).toBe(true)
      if (item.kind === ShopItemKind.Spectral) expect(seen(progress, 'spectral', item.id)).toBe(true)
    }
    for (const id of state.shop.packs) expect(seen(progress, 'pack', id)).toBe(true)
    if (state.shop.voucher) expect(seen(progress, 'voucher', state.shop.voucher)).toBe(true)
  })

  it('손에 든 것이 전부 보인다', () => {
    const state = runOf('CLOVER-0003', 600)
    const progress = emptyCollection()
    discover(progress, sightings(state))
    for (const joker of state.jokers) expect(seen(progress, 'joker', joker.jokerId)).toBe(true)
    for (const id of state.vouchers) expect(seen(progress, 'voucher', id)).toBe(true)
    for (const card of state.deck) {
      if (card.enhancement !== EnhancementKind.None) {
        expect(seen(progress, 'enhancement', EnhancementKind[card.enhancement])).toBe(true)
      }
      if (card.seal !== SealKind.None) {
        expect(seen(progress, 'seal', SealKind[card.seal])).toBe(true)
      }
      if (card.edition !== EditionKind.Base) {
        expect(seen(progress, 'edition', EditionKind[card.edition])).toBe(true)
      }
    }
  })

  it('적힌 식별자가 전부 표에 있다', () => {
    const progress = emptyCollection()
    for (const seed of ['CLOVER-0001', 'CLOVER-0003', 'CLOVER-0005']) {
      discover(progress, sightings(runOf(seed, 600)))
    }
    const has = {
      joker: (id: string) => data.tables.joker.findByJokerId(id) !== undefined,
      tarot: (id: string) => data.tables.tarot.findByTarotId(id) !== undefined,
      planet: (id: string) => data.tables.planet.findByPlanetId(id) !== undefined,
      spectral: (id: string) => data.tables.spectral.findBySpectralId(id) !== undefined,
      voucher: (id: string) => data.tables.voucher.findByVoucherId(id) !== undefined,
      pack: (id: string) => data.tables.boosterPack.findByPackId(id) !== undefined,
      tag: (id: string) => data.tables.tag.findByTagId(id) !== undefined,
      boss: (id: string) => data.tables.bossBlind.findByBossId(id) !== undefined,
      deck: (id: string) => data.tables.deck.findByDeckId(id) !== undefined,
    }
    for (const [group, check] of Object.entries(has)) {
      for (const id of progress[group as keyof typeof has]) {
        expect(check(id), `${group}.${id}`).toBe(true)
      }
    }
  })

  it('여러 판을 돌리면 발견이 쌓인다', () => {
    const progress = emptyCollection()
    discover(progress, sightings(runOf('CLOVER-0001', 600)))
    const first = countOf(progress, 'joker')
    discover(progress, sightings(runOf('CLOVER-0005', 600)))
    expect(countOf(progress, 'joker')).toBeGreaterThanOrEqual(first)
  })
})
