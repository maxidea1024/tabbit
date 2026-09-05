// 콜렉션 — 무엇을 만나 보았는가.
//
// **판이 아니라 저장이 가지는 값입니다.** 챌린지의 깬 목록과 같은 자리이고, `RunState` 에
// 들어가지 않습니다 — 코어는 결정론이고 해시가 그것을 봅니다. 발견은 진행이지 규칙이
// 아니므로, 여기에 두면 구워 둔 리플레이가 그대로 같은 해시를 냅니다.
//
// **손에 들어왔거나 진열된 것이 발견입니다.** 상점에서 본 것도 발견이고, 사지 않아도
// 남습니다 — 원작과 같습니다.
//
// **판정이 이 파일 안에 있습니다.** 화면은 `sightings(state)` 가 낸 목록을 `discover` 로
// 넘기기만 합니다. 어느 자리에서 무엇이 보이는가를 화면 코드에 흩어 두면, 자리 하나가
// 늘 때마다 그 규칙이 함께 늘고 게이트가 그것을 볼 수 없습니다.

import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { SealKind } from '../generated/enums/seal-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { BlindKind } from '../generated/enums/blind-kind'
import { ConsumableKind } from '../generated/enums/consumable-kind'
import type { ShopItem } from './shop'
import type { RunState } from './state'

/**
 * 발견을 적어 두는 묶음.
 *
 * **표 하나가 묶음 하나입니다.** 화면의 탭은 이보다 적습니다 — 소모품 탭 하나가 타로와
 * 행성과 유령 셋을 세우고, 카드 탭 하나가 강화와 인장과 에디션 셋을 세웁니다. 저장이
 * 탭을 따라가면 탭을 나누거나 합칠 때 저장이 못 쓰게 됩니다.
 */
export type CollectionGroup =
  | 'joker' | 'tarot' | 'planet' | 'spectral' | 'voucher'
  | 'enhancement' | 'seal' | 'edition' | 'pack' | 'tag'
  | 'blind' | 'boss' | 'stake' | 'deck'

export const COLLECTION_GROUPS: readonly CollectionGroup[] = [
  'joker', 'tarot', 'planet', 'spectral', 'voucher',
  'enhancement', 'seal', 'edition', 'pack', 'tag',
  'blind', 'boss', 'stake', 'deck',
]

/** 묶음마다 만나 본 것들의 식별자. */
export type CollectionProgress = Record<CollectionGroup, string[]>

/** 한 번 본 것 하나. */
export interface Sighting {
  group: CollectionGroup
  id: string
}

export function emptyCollection(): CollectionProgress {
  const out = {} as CollectionProgress
  for (const group of COLLECTION_GROUPS) out[group] = []
  return out
}

export function seen(progress: CollectionProgress, group: CollectionGroup,
                     id: string): boolean {
  return progress[group]?.includes(id) === true
}

export function countOf(progress: CollectionProgress, group: CollectionGroup): number {
  return progress[group]?.length ?? 0
}

/**
 * 본 것들을 적습니다. **무언가 늘었으면 알립니다** — 부르는 쪽은 그때만 저장합니다.
 *
 * 같은 것을 두 번 적지 않고, 묶음을 섞지 않습니다. 빈 식별자는 적지 않습니다 — 상점의
 * 빈 칸과 정해지지 않은 보스가 그렇게 들어옵니다.
 */
export function discover(progress: CollectionProgress,
                         found: readonly Sighting[]): boolean {
  let changed = false
  for (const one of found) {
    if (one.id === '') continue
    const list = progress[one.group]
    if (!list || list.includes(one.id)) continue
    list.push(one.id)
    changed = true
  }
  return changed
}

/**
 * 지금 이 판에서 보이는 것 전부.
 *
 * **상태만 봅니다.** 이벤트를 받아 갈래마다 적으면 같은 물건이 오는 길마다 규칙이 하나씩
 * 생깁니다 — 조커는 상점 · 팩 · 태그 · `CreateCard` 넷으로 오는데, 넷 다 들어오고 나면
 * `state.jokers` 에 있습니다.
 *
 * **액션마다 부릅니다.** 상태가 바뀌는 길이 `apply` 하나이므로 그 뒤에 한 번 부르면
 * 놓치는 자리가 없습니다.
 */
export function sightings(state: RunState): Sighting[] {
  const out: Sighting[] = []
  const add = (group: CollectionGroup, id: string): void => {
    if (id !== '') out.push({ group, id })
  }

  add('deck', state.deckId)
  add('stake', state.stake)

  // **블라인드 셋은 함께 보입니다.** 고르는 판에 스몰과 빅과 보스가 한 화면에 섭니다.
  add('blind', BlindKind[BlindKind.Small])
  add('blind', BlindKind[BlindKind.Big])
  add('blind', BlindKind[BlindKind.Boss])

  // 이번 안테의 보스는 고르는 판에 이름이 적혀 있습니다.
  add('boss', state.bossId)
  for (const id of state.bossesSeen) add('boss', id)

  for (const joker of state.jokers) {
    add('joker', joker.jokerId)
    addEdition(out, joker.edition)
  }
  for (const item of state.consumables) {
    add(consumableGroup(item.kind), item.id)
    addEdition(out, item.edition)
  }
  for (const id of state.vouchers) add('voucher', id)
  for (const id of state.tagsPending) add('tag', id)
  for (const id of state.tagOffer) add('tag', id)

  // 덱의 카드 52장. **판을 도는 내내 손패와 남은 카드 판에서 보입니다.**
  for (const card of state.deck) {
    addEnhancement(out, card.enhancement)
    addSeal(out, card.seal)
    addEdition(out, card.edition)
  }

  // 상점에 진열된 것. **사지 않아도 본 것입니다.**
  for (const item of state.shop.cards) addShopItem(out, item)
  for (const id of state.shop.packs) add('pack', id)
  if (state.shop.voucher) add('voucher', state.shop.voucher)

  // 뜯어 놓은 팩. 펼쳐진 카드가 전부 보입니다.
  if (state.pack) {
    add('pack', state.pack.packId)
    for (const item of state.pack.options) addShopItem(out, item)
  }

  return out
}

function addShopItem(out: Sighting[], item: ShopItem): void {
  switch (item.kind) {
    case ShopItemKind.Joker: out.push({ group: 'joker', id: item.id }); break
    case ShopItemKind.Tarot: out.push({ group: 'tarot', id: item.id }); break
    case ShopItemKind.Planet: out.push({ group: 'planet', id: item.id }); break
    case ShopItemKind.Spectral: out.push({ group: 'spectral', id: item.id }); break
    default: break
  }
  addEdition(out, item.edition)
  if (item.enhancement !== undefined) addEnhancement(out, item.enhancement)
  if (item.seal !== undefined) addSeal(out, item.seal)
}

/**
 * 「없음」은 발견이 아닙니다.
 *
 * 강화 · 인장 · 에디션의 표에는 아무것도 붙지 않은 줄이 하나씩 있습니다 — 그것은 붙일 수
 * 있는 것이 아니라 붙지 않은 상태이므로, 도감의 칸도 되지 않고 발견도 되지 않습니다.
 */
function addEnhancement(out: Sighting[], value: EnhancementKind): void {
  if (value === EnhancementKind.None) return
  out.push({ group: 'enhancement', id: EnhancementKind[value] })
}

function addSeal(out: Sighting[], value: SealKind): void {
  if (value === SealKind.None) return
  out.push({ group: 'seal', id: SealKind[value] })
}

function addEdition(out: Sighting[], value: EditionKind): void {
  if (value === EditionKind.Base) return
  out.push({ group: 'edition', id: EditionKind[value] })
}

function consumableGroup(kind: ConsumableKind): CollectionGroup {
  return kind === ConsumableKind.Tarot ? 'tarot'
    : kind === ConsumableKind.Planet ? 'planet' : 'spectral'
}

const KEY = 'clover.collection'

export function loadCollection(): CollectionProgress {
  const out = emptyCollection()
  try {
    const raw = localStorage.getItem(KEY)
    if (raw === null) return out
    const found = JSON.parse(raw) as Partial<Record<string, unknown>>
    for (const group of COLLECTION_GROUPS) {
      const list = found[group]
      if (!Array.isArray(list)) continue
      out[group] = list.filter((one): one is string => typeof one === 'string')
    }
    return out
  } catch {
    // 저장을 읽지 못하는 곳이 있습니다 — 사생활 보호 창이나 저장을 막은 브라우저입니다.
    return out
  }
}

export function saveCollection(progress: CollectionProgress): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(progress))
  } catch {
    // 저장하지 못해도 판은 돌아야 합니다.
  }
}
