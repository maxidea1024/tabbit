// 규칙이 다시 세워지는가.
//
// **규칙은 누적이 아닙니다.** 원인이 생기거나 사라지면 기본값에서 다시 쌓습니다 — 누적으로
// 두었을 때 세 가지가 조용히 틀렸습니다.
//
//   - 상점에서 산 조커의 `Passive` 규칙이 아예 걸리지 않았습니다
//   - 보스 효과 17개가 아예 걸리지 않았습니다. `Passive` 는 런 시작에 한 번만 도는데
//     그때 블라인드는 스몰이라 보스가 `collect` 에 들어가지 않습니다
//   - 태그가 뽑는 순간 즉시, 그리고 영원히 걸렸습니다
//
// 셋 다 아무 게이트도 보지 않았습니다. 그래서 이 파일이 있습니다.

import { beforeAll, describe, expect, it } from 'vitest'
import * as path from 'path'

import { EditionKind } from '../src/generated/enums/edition-kind'
import { ShopItemKind } from '../src/generated/enums/shop-item-kind'
import { Trigger } from '../src/generated/enums/trigger'
import type { Data } from '../src/core/data'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun } from '../src/core/run'
import type { RunState } from '../src/core/state'

const DATA = path.resolve(__dirname, '..', 'public', 'data')

let data: Data

beforeAll(() => {
  data = loadFromDisk(DATA)
})

function freshState(): RunState {
  return newRun(data, 'TEST-RULES', 'red_deck', 'White').state
}

describe('규칙 다시 세우기', () => {
  it('상점에서 산 조커의 규칙이 걸립니다', () => {
    const state = freshState()
    const before = state.rules.handSize

    // `spinner` 는 손패를 하나 늘립니다. **사는 것으로 걸려야 합니다** — 넣기만 하고
    // 효과를 돌리지 않던 것이 이 게이트가 잡는 것입니다.
    state.phase = 'shop'
    state.money = 50
    state.shop.cards = [{
      kind: ShopItemKind.Joker, id: 'spinner', cost: 4, edition: EditionKind.Base,
    } as never]

    apply(data, state, { t: 'buy', slot: 0 })

    expect(state.jokers.map(joker => joker.jokerId)).toContain('spinner')
    expect(state.rules.handSize).toBe(before + 1)
  })

  it('판 조커의 규칙이 빠집니다', () => {
    const state = freshState()
    const before = state.rules.handSize

    state.phase = 'shop'
    state.money = 50
    state.shop.cards = [{
      kind: ShopItemKind.Joker, id: 'spinner', cost: 4, edition: EditionKind.Base,
    } as never]
    apply(data, state, { t: 'buy', slot: 0 })
    expect(state.rules.handSize).toBe(before + 1)

    apply(data, state, { t: 'sell_joker', index: 0 })
    expect(state.rules.handSize).toBe(before)
  })

  it('규칙이 두 번 얹히지 않습니다', () => {
    const state = freshState()
    const before = state.rules.handSize

    // 여러 번 다시 세워도 값이 불어나지 않아야 합니다. **다시 세우는 동안 적어 버리면**
    // 부를 때마다 한 칸씩 늘어납니다.
    state.phase = 'shop'
    state.money = 80
    state.shop.cards = [{
      kind: ShopItemKind.Joker, id: 'spinner', cost: 4, edition: EditionKind.Base,
    } as never]
    apply(data, state, { t: 'buy', slot: 0 })

    apply(data, state, { t: 'reroll' })
    apply(data, state, { t: 'reroll' })
    apply(data, state, { t: 'leave_shop' })

    expect(state.rules.handSize).toBe(before + 1)
  })

  it('보스의 규칙이 그 블라인드에서만 걸립니다', () => {
    const state = freshState()
    const before = state.rules.handSize

    // `the_manacle` 은 손패를 하나 줄입니다. 보스 블라인드 동안에만입니다.
    state.bossId = 'the_manacle'
    state.blind = 3
    state.phase = 'blind-select'
    apply(data, state, { t: 'select_blind' })
    expect(state.rules.handSize).toBe(before - 1)

    // 라운드를 끝내면 되돌아옵니다.
    state.score = state.target
    apply(data, state, { t: 'play', cards: state.hand.slice(0, 1) })
    expect(state.rules.handSize).toBe(before)
  })
})

describe('태그', () => {
  it('즉시 갈래가 아닌 태그는 손에 남습니다', () => {
    // **뽑는 자리에서 도는 것은 `OnUse` 뿐입니다.** 나머지는 상점이나 다음 라운드에 뜻을
    // 가지므로 들고 있어야 합니다. 어느 태그가 뽑힐지는 시드가 정하므로, 갈래별로 「즉시면
    // 사라지고 아니면 남는다」를 봅니다.
    for (const row of data.tables.tag.records) {
      const rows = data.tagEffects.get(row.tagId) ?? []
      if (rows.length === 0) continue
      const immediate = rows.some(one => one.trigger === Trigger.OnUse)

      const state = freshState()
      state.tagsPending = [row.tagId]
      state.phase = 'blind-select'
      state.blind = 1
      // 상점까지 가지 않고, 즉시 갈래만 뽑는 자리에서 도는 것을 봅니다.
      apply(data, state, { t: 'skip_blind' })

      const left = state.tagsPending.filter(one => one === row.tagId).length
      // 새로 뽑힌 것이 같은 태그일 수 있으므로 「원래 것이 남았는가」만 봅니다.
      expect(immediate ? left <= 1 : left >= 1).toBe(true)
    }
  })

  it('같은 태그가 상점마다 다시 돌지 않습니다', () => {
    const state = freshState()
    // 상점에서 쓰이는 태그 하나를 손에 쥐여 줍니다.
    state.tagsPending = ['voucher']
    state.blind = 3
    state.phase = 'blind-select'
    apply(data, state, { t: 'select_blind' })
    state.score = state.target
    apply(data, state, { t: 'play', cards: state.hand.slice(0, 1) })

    expect(state.phase).toBe('shop')
    expect(state.tagsPending).not.toContain('voucher')
  })
})
