// 확장 350종.
//
// **한 종씩 실제로 돌립니다.** 헤드리스 완주는 한 런에서 조커 20종 남짓만 만나므로, 350종
// 가운데 어느 것이 예외를 내는지는 그 방법으로 드러나지 않습니다. 여기서는 500종 전부를
// 판에 세우고 트리거를 지나게 합니다.
//
// 값이 얼마인지는 보지 않습니다 — 그것은 `scoring.test.ts` 의 몫이고, 여기가 보는 것은
// **선언이 런타임에서 실행되는가**입니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import { EditionKind } from '../src/generated/enums/edition-kind'
import { EnhancementKind } from '../src/generated/enums/enhancement-kind'
import { JokerPool } from '../src/generated/enums/joker-pool'
import { ShopItemKind } from '../src/generated/enums/shop-item-kind'
import { RankKind } from '../src/generated/enums/rank-kind'
import { SealKind } from '../src/generated/enums/seal-kind'
import { SuitKind } from '../src/generated/enums/suit-kind'
import { Trigger } from '../src/generated/enums/trigger'
import type { Data } from '../src/core/data'
import { describe as describeEffects } from '../src/core/describe'
import { loadFromDisk } from '../src/core/load-node'
import { newRun } from '../src/core/run'
import { stock } from '../src/core/shop'
import { scoreHand } from '../src/core/scoring'
import { newCounters, type CardInstance, type RunState } from '../src/core/state'
import { newVm, runTrigger } from '../src/core/vm'

const DATA = path.resolve(__dirname, '..', 'public', 'data')
const BOTH = [JokerPool.Base, JokerPool.Greenhouse]

let data: Data

beforeAll(() => {
  data = loadFromDisk(DATA)
})

let uid = 50_000

function card(rank: number, suit: SuitKind, enhancement = EnhancementKind.None): CardInstance {
  return {
    uid: uid++,
    baseCardId: 'T',
    rank: rank as RankKind,
    suit,
    enhancement,
    seal: SealKind.None,
    edition: EditionKind.Base,
    bonusChips: 0,
    debuffed: false,
    faceDown: false,
  }
}

/** 조커 하나를 심은 상태. 덱은 그대로 두고 패만 손으로 세웁니다. */
function withJoker(jokerId: string): RunState {
  const state = newRun(data, 'EXP-0001', 'red_deck', 'White', BOTH).state
  state.jokers.push({
    uid: uid++,
    jokerId,
    edition: EditionKind.Base,
    sticker: 0 as never,
    counters: newCounters(),
    age: 0,
    disabled: false,
  })
  return state
}

/** 득점 한 번과, 카드를 보지 않는 트리거 전부. */
function exercise(jokerId: string): void {
  const state = withJoker(jokerId)
  const hand = [
    card(2, SuitKind.Spade), card(2, SuitKind.Heart),
    card(13, SuitKind.Club, EnhancementKind.Glass),
    card(14, SuitKind.Diamond, EnhancementKind.Gold),
    card(9, SuitKind.Spade, EnhancementKind.Steel),
  ]
  state.hand = hand.map(entry => entry.uid)
  for (const entry of hand) state.deck.push(entry)

  const vm = newVm(data, state)
  scoreHand(vm, hand)

  // 득점 밖의 트리거들. 카드를 보는 것(`OnCardScored` · `OnCardHeld`)은 위에서 지났습니다.
  const rest = [
    Trigger.Passive, Trigger.OnRoundStart, Trigger.OnRoundEnd, Trigger.OnBlindSelect,
    Trigger.OnBossDefeated, Trigger.OnShopEnter, Trigger.OnShopExit, Trigger.OnReroll,
    Trigger.OnPackSkipped, Trigger.OnPackOpened, Trigger.OnCardAdded,
    Trigger.OnCardDestroyed, Trigger.OnJokerSold, Trigger.OnConsumableUsed,
    Trigger.OnSell, Trigger.OnScoreResolved, Trigger.OnLuckyTriggered,
    Trigger.OnHandDiscarded, Trigger.OnCardDiscarded,
  ]
  for (const trigger of rest) runTrigger(newVm(data, state), trigger)
}

describe('데이터', () => {
  it('조커가 500종이고 풀이 150 · 350 입니다', () => {
    const rows = data.tables.joker.records
    expect(rows.length).toBe(500)
    expect(rows.filter(row => row.pool === JokerPool.Base).length).toBe(150)
    expect(rows.filter(row => row.pool === JokerPool.Greenhouse).length).toBe(350)
  })

  it('희귀도가 181 · 214 · 85 · 20 입니다', () => {
    const rows = data.tables.joker.records
    const count = (rarity: number) => rows.filter(row => row.rarity === rarity).length
    expect([count(1), count(2), count(3), count(4)]).toEqual([181, 214, 85, 20])
  })

  it('확장 조커 전부에 효과 행이 있습니다', () => {
    const owners = new Set(data.tables.jokerEffect.records.map(row => row.owner))
    const orphan = data.tables.joker.records
      .filter(row => row.pool === JokerPool.Greenhouse && !owners.has(row.jokerId))
      .map(row => row.jokerId)
    expect(orphan).toEqual([])
  })
})

/**
 * 상점의 조커 칸을 시드 여러 개로 채워 나온 식별자를 모읍니다.
 *
 * **`stock()` 을 직접 부릅니다.** 액션으로 상점까지 가려면 블라인드를 실제로 깨야 하는데,
 * 그 경로는 판의 실력에 의존하므로 시드에 따라 상점에 닿지 못합니다 — 그러면 검사가 아무것도
 * 보지 않은 채 통과합니다.
 */
function shopJokers(pools: JokerPool[], rounds: number): string[] {
  const out: string[] = []
  for (let n = 1; n <= rounds; n++) {
    const state = newRun(data, `POOL-${n}`, 'red_deck', 'White', pools).state
    const vm = newVm(data, state)
    for (let refill = 0; refill < 6; refill++) {
      stock(vm, state.shop)
      for (const item of state.shop.cards) {
        if (item.kind === ShopItemKind.Joker) out.push(item.id)
      }
    }
  }
  return out
}

describe('풀', () => {
  it('확장을 켜지 않으면 상점에 확장 조커가 나오지 않습니다', () => {
    const expansion = new Set(data.tables.joker.records
      .filter(row => row.pool === JokerPool.Greenhouse).map(row => row.jokerId))

    const seen = shopJokers([JokerPool.Base], 30)
    expect(seen.length).toBeGreaterThan(20)
    expect(seen.filter(id => expansion.has(id))).toEqual([])
  })

  it('확장을 켜면 두 풀이 다 나옵니다', () => {
    const expansion = new Set(data.tables.joker.records
      .filter(row => row.pool === JokerPool.Greenhouse).map(row => row.jokerId))

    const seen = shopJokers(BOTH, 30)
    expect(seen.filter(id => expansion.has(id)).length).toBeGreaterThan(0)
    expect(seen.filter(id => !expansion.has(id)).length).toBeGreaterThan(0)
  })
})

describe('500종 전부가 예외 없이 돕니다', () => {
  // 종마다 테스트 하나입니다. 하나가 터지면 어느 조커인지 이름이 그대로 보입니다.
  it.each(loadIds())('%s', jokerId => {
    expect(() => exercise(jokerId)).not.toThrow()
  })
})

/** `beforeAll` 보다 먼저 도는 `it.each` 를 위해 데이터를 한 번 더 읽습니다. */
function loadIds(): string[] {
  return loadFromDisk(DATA).tables.joker.records.map(row => row.jokerId)
}

describe('설명문', () => {
  it('500종 전부가 자리표 없는 설명문을 냅니다', () => {
    const broken: string[] = []
    for (const joker of data.tables.joker.records) {
      const lines = describeEffects(data, data.jokerEffects.get(joker.jokerId) ?? [])
      const bad = lines.length === 0
        || lines.some(line => line.trim() === ''
          || line.includes('{') || line.includes('}')
          || line.includes('undefined') || line.includes('NaN'))
      if (bad) broken.push(`${joker.jokerId}: ${JSON.stringify(lines)}`)
    }
    expect(broken).toEqual([])
  })
})
