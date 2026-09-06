// 부스터 팩.
//
// **팩은 사는 것이 아니라 뜯는 것입니다.** 값을 내면 몇 장이 펼쳐지고 그중에서 고릅니다.
// 그 사이의 상태(`state.pack`)가 있는 동안은 상점을 나가지 못하고, 다 고르거나 건너뛰면
// 사라집니다 — 이 세 가지가 여기서 확인하는 것입니다.

import { describe, expect, it } from 'vitest'

import { PackKind } from '../src/generated/enums/pack-kind'
import { ShopItemKind } from '../src/generated/enums/shop-item-kind'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun, type Action } from '../src/core/run'
import type { RunState } from '../src/core/state'
import { autoplay, play } from '../src/headless'

const DATA = 'public/data'

/** 상점에 닿을 때까지 자동으로 둡니다. */
function toShop(seed: string): { data: ReturnType<typeof loadFromDisk>; state: RunState } {
  const run = autoplay(seed, 'red_deck', 'White', 400, DATA)
  const data = loadFromDisk(DATA)

  let state = newRun(data, seed, 'red_deck', 'White').state
  for (const action of run.replay.actions) {
    if (state.phase === 'shop' && state.shop.packs.length > 0 && !state.pack) break
    state = apply(data, state, action).state
  }
  return { data, state }
}

describe('팩', () => {
  it('표가 15종을 정합니다', () => {
    const data = loadFromDisk(DATA)
    expect(data.tables.boosterPack.records.length).toBe(15)

    // 갈래 5종 × 크기 3종입니다. 하나라도 비면 상점에 안 나오는 팩이 생깁니다.
    const kinds = new Set(data.tables.boosterPack.records.map(row => row.kind))
    expect(kinds.size).toBe(5)
    for (const row of data.tables.boosterPack.records) {
      expect(row.picks).toBeLessThanOrEqual(row.cards)
    }
  })

  it('사면 뜯어지고 값을 냅니다', () => {
    const { data, state } = toShop('CLOVER-0001')
    expect(state.phase).toBe('shop')
    expect(state.shop.packs.length).toBeGreaterThan(0)

    const row = data.tables.boosterPack.findByPackId(state.shop.packs[0])!
    const before = { money: state.money, packs: state.shop.packs.length }

    const next = apply(data, state, { t: 'buy_pack', slot: 0 }).state
    expect(next.pack).not.toBeNull()
    expect(next.pack!.options.length).toBe(row.cards)
    expect(next.pack!.picksLeft).toBe(row.picks)
    expect(next.money).toBe(before.money - row.cost)
    expect(next.shop.packs.length).toBe(before.packs - 1)
  })

  it('갈래가 무엇이 들어 있는지를 정합니다', () => {
    const { data, state } = toShop('CLOVER-0001')

    const expected: Record<number, ShopItemKind> = {
      [PackKind.Arcana]: ShopItemKind.Tarot,
      [PackKind.Celestial]: ShopItemKind.Planet,
      [PackKind.Spectral]: ShopItemKind.Spectral,
      [PackKind.Buffoon]: ShopItemKind.Joker,
      [PackKind.Standard]: ShopItemKind.PlayingCard,
    }

    const opened = apply(data, state, { t: 'buy_pack', slot: 0 }).state
    const open = opened.pack!
    for (const item of open.options) {
      expect(item.kind).toBe(expected[open.kind])
      // 팩에서 나온 것은 값이 없습니다 — 팩을 살 때 이미 냈습니다.
      expect(item.cost).toBe(0)
    }
  })

  it('고르면 손에 들어오고 다 고르면 닫힙니다', () => {
    const { data, state } = toShop('CLOVER-0001')
    const opened = apply(data, state, { t: 'buy_pack', slot: 0 }).state
    const open = opened.pack!
    const picks = open.picksLeft

    let next = opened
    for (let i = 0; i < picks; i++) {
      const before = next.jokers.length + next.consumables.length + next.deck.length
      next = apply(data, next, { t: 'pick_pack', index: i }).state
      expect(next.jokers.length + next.consumables.length + next.deck.length).toBe(before + 1)
    }

    expect(next.pack).toBeNull()
  })

  it('건너뛰면 아무것도 받지 않고 닫힙니다', () => {
    const { data, state } = toShop('CLOVER-0001')
    const opened = apply(data, state, { t: 'buy_pack', slot: 0 }).state
    const before = opened.jokers.length + opened.consumables.length + opened.deck.length

    const closed = apply(data, opened, { t: 'skip_pack' }).state
    expect(closed.pack).toBeNull()
    expect(closed.jokers.length + closed.consumables.length + closed.deck.length).toBe(before)
  })

  it('뜯어 놓은 채로는 상점을 나가지 못합니다', () => {
    const { data, state } = toShop('CLOVER-0001')
    const opened = apply(data, state, { t: 'buy_pack', slot: 0 }).state

    const tried = apply(data, opened, { t: 'leave_shop' }).state
    expect(tried.phase).toBe('shop')
    expect(tried.pack).not.toBeNull()
  })

  it('같은 시드는 같은 것을 펼칩니다', () => {
    const one = toShop('CLOVER-0001')
    const two = toShop('CLOVER-0001')

    const left = apply(one.data, one.state, { t: 'buy_pack', slot: 0 }).state.pack!
    const right = apply(two.data, two.state, { t: 'buy_pack', slot: 0 }).state.pack!
    expect(left.options.map(item => item.id)).toEqual(right.options.map(item => item.id))
  })

  it('리플레이에 팩이 들어가도 해시가 같습니다', () => {
    // **자동으로 두는 쪽이 팩을 사므로** 리플레이에 `buy_pack` 과 `pick_pack` 이 들어갑니다.
    // 그 둘이 결정론을 깨면 구워 둔 리플레이가 전부 무효가 됩니다.
    //
    // **시드가 고정인 것은 이 전제 때문입니다.** 경제가 달라지면 사는 것도 달라지므로,
    // 팩을 사지 않게 된 시드는 해시가 아니라 전제에서 걸립니다 — `CLOVER-0002` 가
    // 그렇게 되어 여기를 옮겼습니다.
    const run = autoplay('CLOVER-0003', 'red_deck', 'White', 400, DATA)
    const kinds = new Set(run.replay.actions.map((action: Action) => action.t))
    expect(kinds.has('buy_pack')).toBe(true)

    const again = play(run.replay, DATA)
    expect(again.hashes).toEqual(run.report.hashes)
  })
})
