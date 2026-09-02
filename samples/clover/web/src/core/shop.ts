// 상점.
//
// **무엇이 오는가는 전부 데이터입니다** — 칸의 개수는 `Const_Economy`, 무엇이 올지는
// `ShopSlotWeight`, 조커의 희귀도는 `JokerRarityWeight`, 리롤 비용은 `RerollCost` 입니다.
// 여기 있는 것은 그 표들을 읽는 순서뿐입니다.

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { PackKind } from '../generated/enums/pack-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { Rarity } from '../generated/enums/rarity'
import {
  jokerPool, packPool, planetPool, spectralPool, tarotPool, voucherPool,
} from './pool'
import type { Data } from './data'
import type { JokerInstance, RunState } from './state'
import { newVm, sellPrice, type Vm } from './vm'

/** 상점 칸 하나. */
export interface ShopItem {
  kind: ShopItemKind
  id: string
  cost: number
  edition: EditionKind
  /** 플레잉 카드에만 붙습니다. 표준 팩에서 나온 카드가 강화와 인장을 달고 옵니다. */
  enhancement?: EnhancementKind
  seal?: SealKind
}

/**
 * 뜯은 팩.
 *
 * **산 순간 물건이 손에 들어오지 않습니다** — 몇 장 중에서 고르는 것이 팩이고, 그 고르는
 * 동안의 상태가 이것입니다. 고를 것을 다 골랐거나 건너뛰면 사라집니다.
 */
export interface PackOpen {
  packId: string
  kind: PackKind
  /** 앞으로 몇 장 더 고를 수 있는가. */
  picksLeft: number
  options: ShopItem[]
  /** 이미 가져간 자리. 화면이 이것을 보고 비워 그립니다. */
  taken: boolean[]
}

export interface ShopState {
  cards: ShopItem[]
  packs: string[]
  voucher: string | null
  rerollsUsed: number
  /** 이 안테에서 바우처를 이미 샀는가. 바우처 칸은 안테마다 한 번입니다. */
  voucherBought: boolean
}

/**
 * 팩 하나를 뜯습니다.
 *
 * **갈래가 무엇이 들어 있는지를 정합니다** — 표는 장수와 고르는 수만 정하고, 어느 표에서
 * 뽑을지는 갈래가 정합니다. 확률은 `Const_Economy` 이므로 여기 숫자가 없습니다.
 */
export function openPack(vm: Vm, packId: string): PackOpen | undefined {
  const data = vm.data
  const row = data.tables.boosterPack.findByPackId(packId)
  if (!row) return undefined

  const options: ShopItem[] = []
  for (let i = 0; i < row.cards; i++) {
    const item = rollPackCard(vm, row.kind)
    if (item) options.push(item)
  }
  if (options.length === 0) return undefined

  return {
    packId,
    kind: row.kind,
    picksLeft: Math.min(row.picks, options.length),
    options,
    taken: options.map(() => false),
  }
}

/** 팩 한 장. 값은 0입니다 — 팩을 살 때 이미 냈습니다. */
function rollPackCard(vm: Vm, kind: PackKind): ShopItem | undefined {
  const data = vm.data
  const rng = vm.state.rng.Pack

  switch (kind) {
    case PackKind.Arcana: {
      const pool = tarotPool(data, vm.state)
      return { kind: ShopItemKind.Tarot, id: pool[rng.below(pool.length)].tarotId, cost: 0, edition: EditionKind.Base }
    }

    case PackKind.Celestial: {
      const pool = planetPool(data, vm.state)
      return { kind: ShopItemKind.Planet, id: pool[rng.below(pool.length)].planetId, cost: 0, edition: EditionKind.Base }
    }

    case PackKind.Spectral: {
      const pool = spectralPool(data, vm.state)
      return { kind: ShopItemKind.Spectral, id: pool[rng.below(pool.length)].spectralId, cost: 0, edition: EditionKind.Base }
    }

    case PackKind.Buffoon: {
      const rarity = rng.pickWeighted(
        data.tables.jokerRarityWeight.records, row => row.weight)?.rarity ?? Rarity.Common
      const pool = jokerPool(data, vm.state, rarity)
      if (pool.length === 0) return undefined
      return { kind: ShopItemKind.Joker, id: pool[rng.below(pool.length)].jokerId, cost: 0, edition: rollEdition(vm) }
    }

    case PackKind.Standard: {
      const pool = data.tables.baseDeckCard.records
      const base = pool[rng.below(pool.length)]
      const economy = data.economy

      const enhancements = data.tables.enhancement.records
        .filter(row => row.enhancement !== EnhancementKind.None)
      const seals = data.tables.seal.records.filter(row => row.seal !== SealKind.None)

      return {
        kind: ShopItemKind.PlayingCard,
        id: base.cardId,
        cost: 0,
        edition: rng.below(10_000) < economy.packEditionChanceBp ? rollEdition(vm) : EditionKind.Base,
        enhancement: rng.below(10_000) < economy.packEnhanceChanceBp
          ? enhancements[rng.below(enhancements.length)].enhancement
          : EnhancementKind.None,
        seal: rng.below(10_000) < economy.packSealChanceBp
          ? seals[rng.below(seals.length)].seal
          : SealKind.None,
      }
    }

    default:
      return undefined
  }
}

export function emptyShop(): ShopState {
  return { cards: [], packs: [], voucher: null, rerollsUsed: 0, voucherBought: false }
}

/** 카드 칸 하나에 무엇이 오는가. 가중치의 합으로 나눈 것이 확률입니다. */
function rollKind(vm: Vm): ShopItemKind | undefined {
  const rules = vm.state.rules
  const rows = vm.data.tables.shopSlotWeight.records

  const weight = (kind: ShopItemKind, base: number): number => {
    if (kind === ShopItemKind.Tarot) return base * rules.shopWeightTarotScale
    if (kind === ShopItemKind.Planet) return base * rules.shopWeightPlanetScale
    if (kind === ShopItemKind.PlayingCard) return rules.shopAllowsPlayingCards ? Math.max(base, 4) : 0
    if (kind === ShopItemKind.Spectral) return rules.shopAllowsSpectral ? Math.max(base, 2) : 0
    // **상점 칸에서만 뺍니다.** 팩과 태그로는 그대로 나와야 하는 챌린지가 있습니다.
    if (kind === ShopItemKind.Joker && rules.noJokersInShop) return 0
    return base
  }

  return vm.state.rng.ShopSlot.pickWeighted(rows, row => weight(row.item, row.weight))?.item
}

/**
 * 상점의 값 하나.
 *
 * **오른 값을 먼저 얹고 할인을 뒤에 적용합니다** — 순서를 뒤집으면 할인이 오른 값에 걸리지
 * 않아 값이 오르는 뜻이 없어집니다.
 */
function discounted(vm: Vm, cost: number): number {
  const raised = cost + vm.state.priceRise
  const off = vm.state.rules.shopDiscount
  if (off <= 0) return raised
  return Math.max(1, Math.floor((raised * (100 - off)) / 100))
}

/** 에디션 추첨. `Hone` 계열이 배율을 올립니다. */
function rollEdition(vm: Vm): EditionKind {
  const scale = vm.state.rules.editionWeightScale
  const rows = vm.data.tables.edition.records.filter(row => row.edition !== EditionKind.Negative)
  const pick = vm.state.rng.ShopSlot.pickWeighted(
    rows, row => (row.edition === EditionKind.Base ? row.weight : row.weight * scale))
  return pick?.edition ?? EditionKind.Base
}

/** 카드 칸 하나를 채웁니다. */
function rollCard(vm: Vm): ShopItem | undefined {
  const data = vm.data
  const kind = rollKind(vm)
  if (kind === undefined) return undefined

  switch (kind) {
    case ShopItemKind.Joker: {
      const rarity = vm.state.rng.ShopRarity.pickWeighted(
        data.tables.jokerRarityWeight.records, row => row.weight)?.rarity ?? Rarity.Common
      const pool = jokerPool(data, vm.state, rarity)
      if (pool.length === 0) return undefined
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      return {
        kind, id: row.jokerId,
        cost: discounted(vm, row.cost),
        edition: rollEdition(vm),
      }
    }

    case ShopItemKind.Tarot: {
      const pool = tarotPool(data, vm.state)
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      return { kind, id: row.tarotId, cost: discounted(vm, data.economy.tarotCost), edition: EditionKind.Base }
    }

    case ShopItemKind.Planet: {
      const pool = planetPool(data, vm.state)
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      const cost = vm.state.rules.freePlanets ? 0 : discounted(vm, data.economy.planetCost)
      return { kind, id: row.planetId, cost, edition: EditionKind.Base }
    }

    case ShopItemKind.Spectral: {
      const pool = spectralPool(data, vm.state)
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      return { kind, id: row.spectralId, cost: discounted(vm, data.economy.spectralCost), edition: EditionKind.Base }
    }

    case ShopItemKind.PlayingCard: {
      const pool = data.tables.baseDeckCard.records
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      return { kind, id: row.cardId, cost: discounted(vm, data.economy.playingCardCost), edition: EditionKind.Base }
    }

    default:
      return undefined
  }
}

/** 상점을 채웁니다. 안테마다 바우처가 하나 걸립니다. */
export function stock(vm: Vm, shop: ShopState): void {
  const data = vm.data
  const state = vm.state

  shop.cards = []
  for (let slot = 0; slot < state.rules.shopCardSlots; slot++) {
    const item = rollCard(vm)
    if (item) shop.cards.push(item)
  }

  shop.packs = []
  const packs = packPool(data, vm.state)
  for (let slot = 0; slot < data.economy.shopPackSlots; slot++) {
    shop.packs.push(packs[state.rng.Pack.below(packs.length)].packId)
  }

  // 바우처는 안테마다 하나입니다. 상위는 자기 하위를 산 뒤에만 나옵니다.
  if (!shop.voucherBought) {
    const owned = new Set(state.vouchers)
    const pool = voucherPool(data, state).filter(row =>
      !owned.has(row.voucherId)
      && (row.upgradesFrom === '' || owned.has(row.upgradesFrom)))
    shop.voucher = pool.length > 0
      ? pool[state.rng.ShopVoucher.below(pool.length)].voucherId
      : null
  }

  // 태그가 남긴 선물. 무료 조커가 이렇게 옵니다.
  for (const gift of vm.shopGifts) {
    const pool = jokerPool(data, state, gift.rarity)
    if (pool.length === 0) continue
    const row = pool[state.rng.ShopSlot.below(pool.length)]
    shop.cards.unshift({
      kind: ShopItemKind.Joker,
      id: row.jokerId,
      cost: gift.free ? 0 : discounted(vm, row.cost),
      edition: (gift.edition ?? EditionKind.Base) as EditionKind,
    })
  }
  vm.shopGifts = []

  if (state.rules.nextShopFree) {
    for (const card of shop.cards) card.cost = 0
  }
}

/** 다음 리롤의 값. */
export function rerollCost(data: Data, state: RunState, shop: ShopState): number {
  if (state.rules.rerollStartsFree && shop.rerollsUsed === 0) return 0
  if (shop.rerollsUsed < state.rules.freeRerolls) return 0

  const row = data.tables.rerollCost.findByTimes(Math.min(shop.rerollsUsed, 9))
  return Math.max(0, (row?.cost ?? 5) + state.rules.rerollCostDelta)
}

/**
 * 조커 하나를 팔면 얼마인가.
 *
 * **화면도 이 값을 물어야 합니다** — 무엇을 내놓을지 정하는 값이고, 그 값이 판마다 다르게
 * 계산되면 화면에 적힌 값과 실제로 들어오는 값이 갈라집니다.
 */
export function sellValueOf(data: Data, state: RunState, joker: JokerInstance): number {
  return sellPrice(newVm(data, state), joker)
}
