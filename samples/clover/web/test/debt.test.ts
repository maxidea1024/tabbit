// 빚 한도가 돈이 나가는 모든 곳에서 같은 바닥인가.
//
// **`debtLimit` 은 잔액이 내려갈 수 있는 바닥이고 0 이하의 값입니다.** 상점은 `-debtLimit`
// 으로, 버리기는 `debtLimit` 으로 검사하던 때가 있었고, 그때 `Credit Card` 를 들면 잔액이
// $20 아래로 내려가는 구매가 전부 막혔습니다. 임대료와 음수 `OpAddMoney` 는 검사 없이 빼서
// 황금 스테이크와 확장 풀에서 잔액이 음수가 되었습니다. 그 넷이 여기 있습니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import { EditionKind } from '../src/generated/enums/edition-kind'
import { JokerPool } from '../src/generated/enums/joker-pool'
import { ShopItemKind } from '../src/generated/enums/shop-item-kind'
import type { Data } from '../src/core/data'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun } from '../src/core/run'
import { newCounters, type GameEvent, type RunState } from '../src/core/state'

const DATA = path.resolve(__dirname, '..', 'public', 'data')
const BOTH = [JokerPool.Base, JokerPool.Greenhouse]

let data: Data
let uid = 90_000

beforeAll(() => {
  data = loadFromDisk(DATA)
})

function fresh(): RunState {
  return newRun(data, 'TEST-DEBT', 'red_deck', 'White', BOTH).state
}

function hold(state: RunState, jokerId: string, sticker = 0): void {
  state.jokers.push({
    uid: uid++, jokerId, edition: EditionKind.Base, sticker: sticker as never,
    counters: newCounters(), age: 0, disabled: false,
  })
}

function offer(state: RunState, jokerId: string, cost: number): void {
  state.phase = 'shop'
  state.shop.cards = [{ kind: ShopItemKind.Joker, id: jokerId, cost, edition: EditionKind.Base } as never]
}

function moneyEvents(events: readonly GameEvent[]): { delta: number; reason: string }[] {
  return events.flatMap(event => event.t === 'MoneyChanged' ? [{ delta: event.delta, reason: event.reason }] : [])
}

describe('빚 한도', () => {
  it('Credit Card 를 들면 상점에서 -20 까지 빚을 냅니다', () => {
    const state = fresh()
    state.money = 50
    offer(state, 'ledger_note', 1)
    apply(data, state, { t: 'buy', slot: 0 })
    expect(state.rules.debtLimit).toBe(-20)

    state.money = 5
    offer(state, 'spinner', 10)
    apply(data, state, { t: 'buy', slot: 0 })
    expect(state.jokers.map(joker => joker.jokerId)).toContain('spinner')
    expect(state.money).toBe(-5)

    // 바닥을 넘는 것은 막힙니다.
    offer(state, 'spinner', 20)
    apply(data, state, { t: 'buy', slot: 0 })
    expect(state.shop.cards).toHaveLength(1)
    expect(state.money).toBe(-5)
  })

  it('버리기 비용도 같은 바닥입니다', () => {
    for (const [debtLimit, allowed] of [[0, false], [-20, true]] as const) {
      const state = fresh()
      state.phase = 'round'
      state.rules.discardCost = 5
      state.rules.debtLimit = debtLimit
      state.discardsLeft = 1
      state.hand = state.deck.slice(0, 8).map(card => card.uid)
      state.money = 1
      apply(data, state, { t: 'discard', cards: [state.hand[0]] })
      expect(state.discardsLeft).toBe(allowed ? 0 : 1)
      expect(state.money).toBe(allowed ? -4 : 1)
    }
  })

  it('임대료는 바닥에서 멈추고 나간 만큼 알립니다', () => {
    const state = fresh()
    hold(state, 'spinner', 3)
    hold(state, 'spinner', 3)
    state.phase = 'round'
    state.rules.noSmallBlindReward = true
    state.handsLeft = 1
    state.target = 1
    state.hand = state.deck.slice(0, 8).map(card => card.uid)
    state.money = 1

    const { events } = apply(data, state, { t: 'play', cards: state.hand.slice(0, 5) })
    expect(state.phase).toBe('shop')
    expect(state.money).toBe(0)
    expect(moneyEvents(events).filter(event => event.reason === 'rental')).toEqual([{ delta: -1, reason: 'rental' }])
  })

  it('음수 OpAddMoney 는 바닥에서 멈춥니다', () => {
    const state = fresh()
    hold(state, 'toll_gate')
    state.phase = 'blind-select'
    state.money = 1
    apply(data, state, { t: 'select_blind' })
    expect(state.phase).toBe('round')
    expect(state.money).toBe(0)
  })

  it('교체는 팔기 전에 셈합니다', () => {
    const state = fresh()
    hold(state, 'spinner')
    state.money = 0
    offer(state, 'ledger_note', 10)
    apply(data, state, { t: 'swap', slot: 0, index: 0 })
    expect(state.jokers.map(joker => joker.jokerId)).toEqual(['spinner'])
    expect(state.shop.cards).toHaveLength(1)
    expect(state.money).toBe(0)
  })
})
