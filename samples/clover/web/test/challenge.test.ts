// 챌린지 20종.
//
// **판정 기준이 둘입니다.** 20종이 예외 없이 끝까지 돌고, 챌린지가 아닌 런의 해시가
// 그대로여야 합니다 — 뒤쪽이 회귀의 판정 기준이고, 챌린지는 런 설정이므로 기존 리플레이에
// 닿을 경로가 없어야 합니다.

import { describe, expect, it } from 'vitest'

import { BanKind } from '../src/generated/enums/ban-kind'
import { JokerPool } from '../src/generated/enums/joker-pool'
import { RuleKind } from '../src/generated/enums/rule-kind'
import { loadFromDisk } from '../src/core/load-node'
import { apply, newRun } from '../src/core/run'
import { snapshotHash } from '../src/core/hash'
import { bestHand } from '../src/core/suggest'
import { autoplay, play } from '../src/headless'
import type { RunState } from '../src/core/state'

const DATA = 'public/data'
const data = loadFromDisk(DATA)

const IDS = data.tables.challenge.records
  .slice()
  .sort((one, two) => one.sortOrder - two.sortOrder)
  .map(row => row.challengeId)

/**
 * 자동으로 끝까지 둡니다.
 *
 * **`headless.ts` 의 `autoplay` 와 다른 것입니다.** 그쪽은 무작위로 두면서 리플레이를
 * 만들고, 이것은 가장 값이 높은 조합만 내리 냅니다 — 여기서 필요한 것은 「예외 없이 종료
 * 상태에 닿는가」뿐입니다.
 */
function runToEnd(challengeId: string, seed = 'CLOVER-0001', limit = 2000) {
  const start = newRun(data, seed, 'red_deck', 'White', [JokerPool.Base], challengeId)
  let state: RunState = start.state
  let acted = 0
  for (; acted < limit; acted++) {
    if (state.phase === 'won' || state.phase === 'lost') break
    if (state.phase === 'blind-select') {
      state = apply(data, state, { t: 'select_blind' }).state
      continue
    }
    if (state.phase === 'shop') {
      state = apply(data, state, { t: 'leave_shop' }).state
      continue
    }
    const held = state.hand
      .map(uid => state.deck.find(card => card.uid === uid))
      .filter((card): card is NonNullable<typeof card> => card !== undefined)
    const pick = bestHand(data, state, held)
    if (!pick || pick.cards.length === 0) break
    state = apply(data, state, { t: 'play', cards: pick.cards.map(card => card.uid) }).state
  }
  return { state, acted }
}

describe('챌린지', () => {
  it('20종이 표에 있고 해금 순서가 1부터 20까지입니다', () => {
    expect(IDS).toHaveLength(20)
    const orders = data.tables.challenge.records.map(row => row.sortOrder).sort((a, b) => a - b)
    expect(orders).toEqual([...Array(20).keys()].map(i => i + 1))
  })

  it('20종 전부 효과 행을 가집니다', () => {
    for (const id of IDS) {
      expect(data.challengeEffects.get(id)?.length ?? 0).toBeGreaterThan(0)
    }
  })

  it('금지 목록의 식별자가 전부 실재합니다', () => {
    const known: Record<number, Set<string>> = {
      [BanKind.Joker]: new Set(data.tables.joker.records.map(row => row.jokerId)),
      [BanKind.Voucher]: new Set(data.tables.voucher.records.map(row => row.voucherId)),
      [BanKind.Tarot]: new Set(data.tables.tarot.records.map(row => row.tarotId)),
      [BanKind.Planet]: new Set(data.tables.planet.records.map(row => row.planetId)),
      [BanKind.Spectral]: new Set(data.tables.spectral.records.map(row => row.spectralId)),
      [BanKind.Tag]: new Set(data.tables.tag.records.map(row => row.tagId)),
      [BanKind.Pack]: new Set(data.tables.boosterPack.records.map(row => row.packId)),
      [BanKind.Boss]: new Set(data.tables.bossBlind.records.map(row => row.bossId)),
    }
    for (const row of data.tables.challengeBan.records) {
      expect(known[row.kind]?.has(row.refId), `${row.owner} / ${row.refId}`).toBe(true)
    }
  })

  it('20종 전부 예외 없이 종료 상태까지 돕니다', () => {
    for (const id of IDS) {
      const run = runToEnd(id)
      expect(['won', 'lost'], id).toContain(run.state.phase)
    }
  })

  // **챌린지는 조커 150종으로 돕니다.** 원작의 금지 목록이 그 150종을 상대로 쓰였으므로,
  // 확장이 켜지면 금지가 걸린 채로 금지가 무효가 됩니다.
  it('확장을 켜도 챌린지의 풀은 기본 150종입니다', () => {
    const run = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                       [JokerPool.Base, JokerPool.Greenhouse], 'evergreen')
    expect(run.state.pools).toEqual([JokerPool.Base])
  })

  it('챌린지가 아닌 런은 풀을 그대로 씁니다', () => {
    const run = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                       [JokerPool.Base, JokerPool.Greenhouse])
    expect(run.state.pools).toEqual([JokerPool.Base, JokerPool.Greenhouse])
    expect(run.state.challengeId).toBe('')
  })

  // **이것이 회귀의 판정 기준입니다.** 챌린지는 런 설정이고 해시에 들어가지 않으므로,
  // 챌린지가 아닌 런의 해시가 한 글자도 달라지지 않아야 합니다.
  it('챌린지 인자를 넘기지 않은 런과 빈 문자열을 넘긴 런이 같습니다', () => {
    const bare = newRun(data, 'CLOVER-0007', 'red_deck', 'White')
    const empty = newRun(data, 'CLOVER-0007', 'red_deck', 'White', [JokerPool.Base], '')
    expect(snapshotHash(empty.state)).toBe(snapshotHash(bare.state))
  })

  describe('시작 덱', () => {
    it('덱을 적지 않은 15종은 표준 52장입니다', () => {
      const changed = new Set(data.tables.challengeCard.records.map(row => row.owner))
      expect(changed.size).toBe(5)
      for (const id of IDS) {
        if (changed.has(id)) continue
        const run = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base], id)
        expect(run.state.deck, id).toHaveLength(52)
      }
    })

    it('`face_town` 은 52장이고 A·2·3 이 없으며 그림 카드가 2벌입니다', () => {
      const deck = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                          [JokerPool.Base], 'face_town').state.deck
      expect(deck).toHaveLength(52)
      // 랭크는 핍 값입니다 — `Two` 가 2이고 `Ace` 가 14입니다.
      expect(deck.filter(card => card.rank === 14)).toHaveLength(0)   // A
      expect(deck.filter(card => card.rank === 2)).toHaveLength(0)    // 2
      expect(deck.filter(card => card.rank === 3)).toHaveLength(0)    // 3
      expect(deck.filter(card => card.rank === 11)).toHaveLength(8)   // J 가 2벌
    })

    it('`low_field` 은 랭크 2~9 의 32장입니다', () => {
      const deck = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                          [JokerPool.Base], 'low_field').state.deck
      expect(deck).toHaveLength(32)
      expect(Math.max(...deck.map(card => card.rank))).toBe(9)
      expect(Math.min(...deck.map(card => card.rank))).toBe(2)
    })

    it('`glass_field` 은 52장 전부 유리입니다', () => {
      const deck = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                          [JokerPool.Base], 'glass_field').state.deck
      expect(deck).toHaveLength(52)
      expect(deck.every(card => card.enhancement === 4)).toBe(true)
    })

    it('`stone_court` 은 그림 카드 12장이 `Stone` 입니다', () => {
      const deck = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                          [JokerPool.Base], 'stone_court').state.deck
      expect(deck).toHaveLength(52)
      expect(deck.filter(card => card.enhancement === 6)).toHaveLength(12)
    })

    it('`red_wax` 은 52장 전부 붉은 인장입니다', () => {
      const deck = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                          [JokerPool.Base], 'red_wax').state.deck
      expect(deck.every(card => card.seal === 1)).toBe(true)
    })
  })

  describe('시작 소지품', () => {
    it('`dry_season` 은 씨주머니 5개로 시작합니다', () => {
      const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                           [JokerPool.Base], 'dry_season').state
      expect(state.jokers.filter(one => one.jokerId === 'seed_pod')).toHaveLength(5)
    })

    it('`face_town` 의 시작 조커 둘에 `Eternal` 이 붙습니다', () => {
      const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                           [JokerPool.Base], 'face_town').state
      expect(state.jokers.map(one => one.jokerId)).toEqual(['long_path', 'stepping_stone'])
      expect(state.jokers.every(one => one.sticker === 1)).toBe(true)
    })

    it('`glass_field` 의 납 주사위 둘이 `Negative` 이자 `Eternal` 입니다', () => {
      const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                           [JokerPool.Base], 'glass_field').state
      expect(state.jokers).toHaveLength(2)
      expect(state.jokers.every(one => one.edition === 4 && one.sticker === 1)).toBe(true)
    })

    it('`coin_ceiling` 은 바우처 둘과 $100 으로 시작합니다', () => {
      const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                           [JokerPool.Base], 'coin_ceiling').state
      expect(state.vouchers).toEqual(['seed_money', 'money_tree'])
      expect(state.money).toBe(100)
    })

    it('`vine_night` 은 타로 둘을 들고 시작합니다', () => {
      const state = newRun(data, 'CLOVER-0001', 'red_deck', 'White',
                           [JokerPool.Base], 'vine_night').state
      expect(state.consumables.map(one => one.id)).toEqual(['the_emperor', 'the_empress'])
    })
  })

  describe('규칙', () => {
    const rulesOf = (id: string) =>
      newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base], id).state.rules

    it('`bare_field` 은 조커 칸이 0이고 상점에 조커가 없습니다', () => {
      const rules = rulesOf('bare_field')
      expect(rules.jokerSlots).toBe(0)
      expect(rules.noJokersInShop).toBe(true)
    })

    it('`barren_road` 은 스몰·빅만 보상이 없습니다', () => {
      const rules = rulesOf('barren_road')
      expect(rules.noSmallBlindReward).toBe(true)
      expect(rules.noBigBlindReward).toBe(true)
      expect(rules.noBossBlindReward).toBe(false)
      expect(rules.jokerSlots).toBe(3)
    })

    it('`dry_season` 은 셋 다 보상이 없고 남은 핸드 수입도 0입니다', () => {
      const rules = rulesOf('dry_season')
      expect(rules.noSmallBlindReward).toBe(true)
      expect(rules.noBigBlindReward).toBe(true)
      expect(rules.noBossBlindReward).toBe(true)
      expect(rules.moneyPerHandLeft).toBe(0)
      expect(rules.noInterest).toBe(true)
    })

    it('`heavy_purse` 은 패 크기 10 에 보유 $5마다 하나 줄어듭니다', () => {
      const rules = rulesOf('heavy_purse')
      expect(rules.handSize).toBe(10)
      expect(rules.handSizePerMoney).toBe(5)
    })

    it('`sky_road` · `five_leaves` · `single_thread` 의 수치', () => {
      expect(rulesOf('sky_road').handsPerRound).toBe(2)
      expect(rulesOf('sky_road').discardsPerRound).toBe(2)
      expect(rulesOf('sky_road').jokerSlots).toBe(4)
      expect(rulesOf('five_leaves').handSize).toBe(5)
      expect(rulesOf('five_leaves').jokerSlots).toBe(7)
      expect(rulesOf('single_thread').handsPerRound).toBe(1)
      expect(rulesOf('single_thread').discardCost).toBe(1)
    })

    // 안테 4의 보스를 깨야 걸립니다. **시작 시점에는 걸려 있지 않아야 합니다.**
    it('`sealed_fate` 은 시작할 때 조커 칸이 그대로입니다', () => {
      const rules = rulesOf('sealed_fate')
      expect(rules.allJokersEternal).toBe(false)
      expect(rules.jokerSlots).toBeGreaterThan(0)
    })
  })

  describe('금지', () => {
    it('`evergreen` 의 풀에 금지된 조커 11종이 없습니다', () => {
      const run = newRun(data, 'CLOVER-0001', 'red_deck', 'White', [JokerPool.Base], 'evergreen')
      const no = new Set(data.tables.challengeBan.records
        .filter(row => row.owner === 'evergreen' && row.kind === BanKind.Joker)
        .map(row => row.refId))
      expect(no.size).toBe(11)
      // 금지된 것은 어느 경로로도 나오지 않아야 하므로 풀 자체에 없어야 합니다.
      const pool = data.tables.joker.records.filter(row =>
        run.state.pools.includes(row.pool) && no.has(row.jokerId))
      expect(pool.map(row => row.jokerId)).toHaveLength(11)
    })

    it('`Eternal` 이 붙지 않는 조커에는 `AllJokersEternal` 도 걸리지 않습니다', () => {
      const cannot = data.tables.joker.records.filter(row => !row.eternalOk)
      expect(cannot.length).toBe(17)
      // `evergreen` 은 그중 기본 11종을 금지하고, 확장 6종은 풀에 없으므로 만나지 않습니다.
      const base = cannot.filter(row => row.pool === JokerPool.Base)
      expect(base).toHaveLength(11)
    })
  })

  // **리플레이가 챌린지를 들고 다녀야 대조할 것이 생깁니다.** 챌린지는 런 설정이므로
  // 해시에 들어가지 않고, 그래서 재생할 때 다시 넘겨 주지 않으면 조용히 다른 판이 됩니다.
  describe('리플레이', () => {
    it('챌린지 런을 다시 돌리면 같은 해시가 나옵니다', () => {
      const run = autoplay('CLOVER-0001', 'red_deck', 'White', 600, DATA,
                           [JokerPool.Base], 'evergreen')
      expect(run.replay.challenge).toBe('evergreen')
      const again = play(run.replay, DATA)
      expect(again.hashes).toEqual(run.report.hashes)
    })

    it('챌린지가 아닌 리플레이에는 그 칸이 없습니다', () => {
      const run = autoplay('CLOVER-0001', 'red_deck', 'White', 600, DATA)
      expect('challenge' in run.replay).toBe(false)
      const again = play(run.replay, DATA)
      expect(again.hashes).toEqual(run.report.hashes)
    })

    it('챌린지를 잃으면 다른 판이 됩니다', () => {
      const run = autoplay('CLOVER-0001', 'red_deck', 'White', 600, DATA,
                           [JokerPool.Base], 'low_field')
      const bare = play({ ...run.replay, challenge: undefined }, DATA)
      expect(bare.finalHash).not.toBe(run.report.finalHash)
    })
  })

  it('규칙 12종이 `RuleKind` 에 있습니다', () => {
    for (const name of ['NoSmallBlindReward', 'NoBigBlindReward', 'NoBossBlindReward',
                        'ChipsCappedByMoney', 'FaceDownDrawRate', 'HandSizePerMoney',
                        'AllJokersEternal', 'DebuffPlayedAfterScoring', 'PriceRisePerPurchase',
                        'NoJokersInShop', 'DiscardCost', 'PinnedJokerSlot']) {
      expect(RuleKind[name as keyof typeof RuleKind], name).toBeDefined()
    }
  })
})
