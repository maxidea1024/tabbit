// 인사이트가 무엇을 보고 무엇을 보지 않는가.
//
// **가장 중요한 것은 건식 실행이 원본을 바꾸지 않는 것입니다.** 득점은 조커의 누적값을
// 늘리고 카드를 부수고 돈을 옮기므로, 복제가 얕으면 **판을 열어 보는 것만으로 런이
// 달라집니다.** 그것은 화면에서 보이지 않고 리플레이에서만 드러납니다.
//
// 규격은 `doc/insight.md` 이고 줄의 목록은 `doc/insight/rules.md` 입니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import { BlindKind } from '../src/generated/enums/blind-kind'
import { ConsumableKind } from '../src/generated/enums/consumable-kind'
import { EditionKind } from '../src/generated/enums/edition-kind'
import { EnhancementKind } from '../src/generated/enums/enhancement-kind'
import { JokerPool } from '../src/generated/enums/joker-pool'
import { SealKind } from '../src/generated/enums/seal-kind'
import { SuitKind } from '../src/generated/enums/suit-kind'
import { Trigger } from '../src/generated/enums/trigger'
import { RankKind } from '../src/generated/enums/rank-kind'

import type { Data } from '../src/core/data'
import { cloneState, dryScore } from '../src/core/dry-run'
import { snapshotHash } from '../src/core/hash'
import { INSIGHT_GROUPS, insights, type Insight } from '../src/core/insight'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun } from '../src/core/run'
import { newCounters, type CardInstance, type RunState } from '../src/core/state'
import { newVm, runRow } from '../src/core/vm'

const DATA = path.resolve(__dirname, '..', 'public', 'data')
const BOTH = [JokerPool.Base, JokerPool.Greenhouse]

let data: Data
let uid = 70_000

beforeAll(() => {
  data = loadFromDisk(DATA)
})

function fresh(seed = 'TEST-INSIGHT'): RunState {
  return newRun(data, seed, 'red_deck', 'White', BOTH).state
}

/** 라운드가 도는 상태. 패가 깔려 있습니다. */
function inRound(seed = 'TEST-INSIGHT'): RunState {
  const state = fresh(seed)
  apply(data, state, { t: 'select_blind' })
  return state
}

function hold(state: RunState, jokerId: string, sticker = 0): void {
  state.jokers.push({
    uid: uid++, jokerId, edition: EditionKind.Base, sticker: sticker as never,
    counters: newCounters(), age: 0, disabled: false,
  })
}

/** 그 열쇠의 줄. 없으면 `undefined` 입니다. */
function found(rows: readonly Insight[], key: string): Insight | undefined {
  return rows.find(one => one.key === key)
}

/** 시트에 있는 열쇠 전부. */
function keys(): Set<string> {
  return new Set(data.tables.stringTable.records.map(row => row.stringId))
}

describe('건식 실행', () => {
  it('원본의 해시가 바뀌지 않습니다', () => {
    const state = inRound()
    // 값을 내는 조커를 여럿 들려 놓습니다. 누적형이 하나라도 있어야 뜻이 있습니다.
    for (const row of data.tables.joker.records.slice(0, 5)) hold(state, row.jokerId)

    const before = snapshotHash(state)
    const rngBefore = JSON.stringify(
      Object.keys(state.rng).sort().map(name => [name, state.rng[name].save()]))

    const dry = dryScore(data, state, state.hand.slice(0, 5))
    expect(dry).toBeDefined()

    expect(snapshotHash(state)).toBe(before)
    expect(JSON.stringify(
      Object.keys(state.rng).sort().map(name => [name, state.rng[name].save()])))
      .toBe(rngBefore)
  })

  it('복제본을 바꾸어도 원본이 그대로입니다', () => {
    const state = inRound()
    hold(state, data.tables.joker.records[0].jokerId)

    const copy = cloneState(state)
    copy.money = 999
    copy.deck[0].debuffed = true
    copy.jokers[0].counters.chips = 42
    copy.rng.Boss.next()

    expect(state.money).not.toBe(999)
    expect(state.deck[0].debuffed).toBe(false)
    expect(state.jokers[0].counters.chips).toBe(0)
    expect(copy.rng.Boss.save()).not.toEqual(state.rng.Boss.save())
  })

  it('낼 수 없는 장수는 세지 않습니다', () => {
    const state = inRound()
    expect(dryScore(data, state, [])).toBeUndefined()
    expect(dryScore(data, state, state.hand.slice(0, 6))).toBeUndefined()
  })

  it('없는 조커를 넣은 차례로는 세지 않습니다', () => {
    const state = inRound()
    hold(state, data.tables.joker.records[0].jokerId)
    expect(dryScore(data, state, state.hand.slice(0, 2), [-1])).toBeUndefined()
  })
})

describe('확률', () => {
  /** 확률이 걸린 효과 행 하나. 조커 500종 가운데 실제로 있는 것을 씁니다. */
  function chanceRow() {
    for (const [owner, rows] of data.jokerEffects) {
      for (const row of rows) {
        if (row.chanceNum !== null && row.chanceDen !== null && row.chanceDen > 1) {
          return { owner, row }
        }
      }
    }
    return undefined
  }

  it("`chanceMode: 'never'` 는 굴리지 않고 적어 둡니다", () => {
    const one = chanceRow()
    expect(one).toBeDefined()
    if (one === undefined) return

    const state = inRound()
    const vm = newVm(data, state)
    vm.chanceMode = 'never'
    const before = state.rng.JokerProc.save()

    runRow(vm, one.row, { kind: 'run' })

    // **난수가 소비되지 않았습니다.** 이것이 이 갈래의 이유입니다.
    expect(state.rng.JokerProc.save()).toEqual(before)
    expect(vm.chanceSkipped?.length).toBe(1)
    expect(vm.chanceSkipped?.[0].den).toBe(one.row.chanceDen)
  })

  it('기본은 굴립니다', () => {
    const one = chanceRow()
    if (one === undefined) return

    const state = inRound()
    const vm = newVm(data, state)
    const before = state.rng.JokerProc.save()

    runRow(vm, one.row, { kind: 'run' })

    expect(state.rng.JokerProc.save()).not.toEqual(before)
  })
})

describe('줄', () => {
  it('핸드당 필요 점수는 남은 핸드로 나눈 값입니다', () => {
    const state = inRound()
    state.target = 1_000
    state.score = 400
    state.handsLeft = 2

    const row = found(insights(data, state), 'round.per_hand')
    expect(row?.values).toMatchObject({ hands: 2, need: 300 })
  })

  it('나누어 떨어지지 않으면 올립니다', () => {
    const state = inRound()
    state.target = 1_000
    state.score = 0
    state.handsLeft = 3

    expect(found(insights(data, state), 'round.per_hand')?.values.need).toBe(334)
  })

  it('보스가 무력화하는 무늬를 패와 남은 것에서 셉니다', () => {
    const state = inRound()
    state.blind = BlindKind.Boss
    state.bossId = 'the_club'

    const row = found(insights(data, state), 'blind.debuff_suit')
    expect(row).toBeDefined()

    const held = state.hand
      .map(target => state.deck.find(card => card.uid === target))
      .filter((card): card is CardInstance => card !== undefined)
      .filter(card => card.suit === SuitKind.Club).length
    const left = state.drawPile
      .map(target => state.deck.find(card => card.uid === target))
      .filter((card): card is CardInstance => card !== undefined)
      .filter(card => card.suit === SuitKind.Club).length

    expect(row?.values).toMatchObject({ held, left })
    // **패와 남은 것을 합치면 덱의 클럽 전부입니다.** 어느 한쪽만 세던 결함이 이 자리입니다.
    expect(held + left).toBe(state.deck.filter(card => card.suit === SuitKind.Club).length)
  })

  it('덱 줄은 아직 뽑지 않은 것과 패를 함께 셉니다', () => {
    const state = inRound()
    const row = found(insights(data, state), 'deck.left')
    expect(row?.values.total).toBe(state.deck.length)
    expect(row?.values.left).toBe(state.drawPile.length)
    expect(row?.values.held).toBe(state.hand.length)
  })

  it('라운드 밖에서는 덱 전부를 남은 것으로 봅니다', () => {
    const state = fresh()
    const row = found(insights(data, state), 'deck.left')
    // 블라인드를 고르는 자리에는 뽑을 무더기가 아직 없습니다. 0장이라고 적으면 사실과
    // 다릅니다.
    expect(row?.values.left).toBe(state.deck.length)
  })

  it('꺼진 조커를 셉니다', () => {
    const state = inRound()
    hold(state, data.tables.joker.records[0].jokerId)
    state.jokers[0].disabled = true

    expect(found(insights(data, state), 'joker.disabled')?.values.n).toBe(1)
  })

  it('소멸이 가까운 조커를 알립니다', () => {
    const state = inRound()
    hold(state, data.tables.joker.records[0].jokerId, 2)
    state.jokers[0].age = 4

    expect(found(insights(data, state), 'joker.perish')?.values.n).toBe(1)
  })

  it('카드를 골라야 쓰는 소모품에 지금 고른 수를 견줍니다', () => {
    const state = inRound()
    const want = pickyTarot()
    expect(want).toBeDefined()
    if (want === undefined) return

    state.consumables = [{
      uid: uid++, kind: ConsumableKind.Tarot, id: want.id, edition: EditionKind.Base,
    }]

    const row = found(insights(data, state), 'consumable.needs_cards')
    expect(row?.values).toMatchObject({ want: want.count, have: 0 })
  })

  it('빚 한도가 없으면 그 줄이 없습니다', () => {
    const state = inRound()
    expect(state.rules.debtLimit).toBe(0)
    expect(found(insights(data, state), 'economy.debt')).toBeUndefined()
  })

  /** 카드를 골라야 쓰는 타로 하나. */
  function pickyTarot(): { id: string; count: number } | undefined {
    for (const [id, rows] of data.tarotEffects) {
      for (const row of rows) {
        // `Scope.Selected` 가 5 입니다.
        if (row.scope === 5 && (row.scopeCount ?? 1) >= 2) {
          return { id, count: row.scopeCount ?? 1 }
        }
      }
    }
    return undefined
  }
})

describe('국면', () => {
  it('끝난 판에는 줄이 없습니다', () => {
    for (const phase of ['won', 'lost'] as const) {
      const state = inRound()
      state.phase = phase
      expect(insights(data, state)).toEqual([])
    }
  })

  it('블라인드를 고르는 자리에는 고른 카드 갈래가 없습니다', () => {
    const state = fresh()
    const groups = new Set(insights(data, state).map(one => one.group))
    expect(groups.has('pick')).toBe(false)
    expect(groups.has('option')).toBe(false)
  })

  it('상점에는 재굴림 값이 적힙니다', () => {
    const state = fresh()
    state.phase = 'shop'
    expect(found(insights(data, state), 'economy.reroll')).toBeDefined()
  })
})

describe('상한', () => {
  it('판 전체가 14줄을 넘지 않습니다', () => {
    const state = inRound()
    for (const row of data.tables.joker.records.slice(0, 5)) hold(state, row.jokerId)
    for (const uidOf of state.hand.slice(0, 3)) {
      const card = state.deck.find(one => one.uid === uidOf)
      if (card) card.debuffed = true
    }
    state.rules.noRepeatHandTypes = true
    state.handTypesThisRound = ['Pair', 'TwoPair']
    state.blind = BlindKind.Boss

    const rows = insights(data, state, state.hand.slice(0, 5))
    expect(rows.length).toBeLessThanOrEqual(14)
  })

  it('갈래 하나가 3줄을 넘지 않습니다', () => {
    const state = inRound()
    for (const row of data.tables.joker.records.slice(0, 5)) hold(state, row.jokerId)

    const rows = insights(data, state, state.hand.slice(0, 5))
    for (const group of INSIGHT_GROUPS) {
      expect(rows.filter(one => one.group === group).length).toBeLessThanOrEqual(3)
    }
  })
})

describe('문구', () => {
  /**
   * 종류마다 문구가 시트에 있는가.
   *
   * **열쇠를 세는 것만으로는 부족합니다** — 판을 여럿 만들어 실제로 나온 줄의 열쇠를
   * 봅니다. 나오지 않는 종류는 아래의 목록이 잡습니다.
   */
  it('나온 줄의 열쇠가 전부 시트에 있습니다', () => {
    const sheet = keys()
    const seen = new Set<string>()

    for (const state of manyStates()) {
      for (const row of insights(data, state, state.hand.slice(0, 5))) {
        seen.add(row.key)
        expect(sheet.has(`ui.insight.${row.key}`)).toBe(true)
      }
    }

    // 판을 여럿 돌렸으므로 갈래는 전부 한 번씩 나와야 합니다.
    expect(seen.size).toBeGreaterThan(10)
  })

  it('목록의 열쇠 34개가 전부 시트에 있습니다', () => {
    const sheet = keys()
    const missing = ALL_KEYS.filter(key => !sheet.has(`ui.insight.${key}`))
    expect(missing).toEqual([])
  })

  it('갈래 머리와 갈래 단추의 열쇠가 있습니다', () => {
    const sheet = keys()
    expect(sheet.has('ui.tab.insight')).toBe(true)
    expect(sheet.has('ui.insight.none')).toBe(true)
    for (const group of INSIGHT_GROUPS) {
      expect(sheet.has(`ui.insight.group.${group}`)).toBe(true)
    }
  })

  /** 여러 판. 국면과 소지품을 바꿔 갈래를 두루 지납니다. */
  function manyStates(): RunState[] {
    const out: RunState[] = []

    for (const seed of ['TEST-A', 'TEST-B', 'TEST-C']) {
      out.push(fresh(seed))

      const round = inRound(seed)
      for (const row of data.tables.joker.records.slice(0, 4)) hold(round, row.jokerId)
      out.push(round)

      const boss = inRound(seed)
      boss.blind = BlindKind.Boss
      boss.bossId = 'the_club'
      out.push(boss)

      const shop = fresh(seed)
      shop.phase = 'shop'
      shop.money = 23
      out.push(shop)

      const rich = inRound(seed)
      rich.money = 100
      rich.rules.debtLimit = -20
      rich.consumables = [{
        uid: uid++, kind: ConsumableKind.Planet,
        id: data.tables.planet.records[0].planetId, edition: EditionKind.Base,
      }]
      rich.deck[0].enhancement = EnhancementKind.Bonus
      rich.deck[1].seal = SealKind.Red
      out.push(rich)
    }

    return out
  }
})

/** `doc/insight/rules.md` 의 목록입니다. **여기와 그 문서가 함께 늘어납니다.** */
const ALL_KEYS = [
  'round.per_hand', 'round.clears', 'round.unreachable', 'round.discards',
  'pick.score', 'pick.jokers', 'pick.chance', 'pick.debuffed', 'pick.count',
  'option.best', 'option.alt', 'option.same',
  'deck.left', 'deck.suit', 'deck.rank_gap', 'deck.enhanced',
  'joker.suit_focus', 'joker.order', 'joker.disabled', 'joker.perish', 'joker.idle',
  'joker.slots',
  'consumable.needs_cards', 'consumable.planet', 'consumable.slots',
  'blind.rule', 'blind.debuff_suit', 'blind.hand_used', 'blind.skip', 'blind.target',
  'economy.interest', 'economy.interest_cap', 'economy.reroll', 'economy.debt',
]

describe('목록', () => {
  it('34종입니다', () => {
    expect(ALL_KEYS.length).toBe(34)
    expect(new Set(ALL_KEYS).size).toBe(34)
  })

  it('갈래 이름이 전부 아는 것입니다', () => {
    for (const key of ALL_KEYS) {
      expect(INSIGHT_GROUPS).toContain(key.split('.')[0])
    }
  })
})

// 쓰지 않는 임포트를 남기지 않습니다. 랭크와 트리거는 아래에서 씁니다.
describe('스트레이트에 한 장', () => {
  it('네 장이 이어지면 모자란 랭크를 알립니다', () => {
    const state = inRound()

    // 패를 손으로 세웁니다. **2·3·4·5 를 들고 6 이나 A 를 기다리는 자리입니다.**
    const wanted: RankKind[] = [RankKind.Two, RankKind.Three, RankKind.Four, RankKind.Five]
    const hand: number[] = []
    for (const rank of wanted) {
      const card = state.deck.find(one => one.rank === rank && !hand.includes(one.uid))
      if (card) hand.push(card.uid)
    }
    expect(hand.length).toBe(4)

    state.hand = hand
    state.drawPile = state.deck
      .filter(card => !hand.includes(card.uid))
      .map(card => card.uid)

    const row = found(insights(data, state), 'deck.rank_gap')
    expect(row).toBeDefined()
    expect(Number(row?.values.n)).toBeGreaterThan(0)
  })

  it('트리거가 버리기인 조커를 버리기 줄이 셉니다', () => {
    const state = inRound()
    const watcher = data.tables.joker.records.find(row =>
      (data.jokerEffects.get(row.jokerId) ?? []).some(one =>
        one.trigger === Trigger.OnHandDiscarded || one.trigger === Trigger.OnCardDiscarded))
    expect(watcher).toBeDefined()
    if (watcher === undefined) return

    hold(state, watcher.jokerId)
    expect(found(insights(data, state), 'round.discards')?.values.jokers).toBe(1)
  })
})
