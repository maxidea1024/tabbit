// 상점.
//
// **무엇이 오는가는 전부 데이터입니다** — 칸의 개수는 `Const_Economy`, 무엇이 올지는
// `ShopSlotWeight`, 조커의 희귀도는 `JokerRarityWeight`, 리롤 비용은 `RerollCost` 입니다.
// 여기 있는 것은 그 표들을 읽는 순서뿐입니다.

import { EditionKind } from '../generated/enums/edition-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { Rarity } from '../generated/enums/rarity'
import type { Data } from './data'
import type { RunState } from './state'
import type { Vm } from './vm'

/** 상점 칸 하나. */
export interface ShopItem {
  kind: ShopItemKind
  id: string
  cost: number
  edition: EditionKind
}

export interface ShopState {
  cards: ShopItem[]
  packs: string[]
  voucher: string | null
  rerollsUsed: number
  /** 이 안테에서 바우처를 이미 샀는가. 바우처 칸은 안테마다 한 번입니다. */
  voucherBought: boolean
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
    return base
  }

  return vm.state.rng.ShopSlot.pickWeighted(rows, row => weight(row.item, row.weight))?.item
}

function discounted(vm: Vm, cost: number): number {
  const off = vm.state.rules.shopDiscount
  if (off <= 0) return cost
  return Math.max(1, Math.floor((cost * (100 - off)) / 100))
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
      const pool = data.tables.joker.records.filter(row => row.rarity === rarity)
      if (pool.length === 0) return undefined
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      return {
        kind, id: row.jokerId,
        cost: discounted(vm, row.cost),
        edition: rollEdition(vm),
      }
    }

    case ShopItemKind.Tarot: {
      const pool = data.tables.tarot.records
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      return { kind, id: row.tarotId, cost: discounted(vm, data.economy.tarotCost), edition: EditionKind.Base }
    }

    case ShopItemKind.Planet: {
      const pool = data.tables.planet.records
      const row = pool[vm.state.rng.ShopSlot.below(pool.length)]
      const cost = vm.state.rules.freePlanets ? 0 : discounted(vm, data.economy.planetCost)
      return { kind, id: row.planetId, cost, edition: EditionKind.Base }
    }

    case ShopItemKind.Spectral: {
      const pool = data.tables.spectral.records
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
  const packs = data.tables.boosterPack.records
  for (let slot = 0; slot < data.economy.shopPackSlots; slot++) {
    shop.packs.push(packs[state.rng.Pack.below(packs.length)].packId)
  }

  // 바우처는 안테마다 하나입니다. 상위는 자기 하위를 산 뒤에만 나옵니다.
  if (!shop.voucherBought) {
    const owned = new Set(state.vouchers)
    const pool = data.tables.voucher.records.filter(row =>
      !owned.has(row.voucherId)
      && (row.upgradesFrom === '' || owned.has(row.upgradesFrom)))
    shop.voucher = pool.length > 0
      ? pool[state.rng.ShopVoucher.below(pool.length)].voucherId
      : null
  }

  // 태그가 남긴 선물. 무료 조커가 이렇게 옵니다.
  for (const gift of vm.shopGifts) {
    const pool = data.tables.joker.records.filter(
      row => gift.rarity === undefined || row.rarity === gift.rarity)
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
