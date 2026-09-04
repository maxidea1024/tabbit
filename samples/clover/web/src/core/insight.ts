// 판을 읽어 다음 한 수에 필요한 것만 줄로.
//
// **게임이 이미 아는 것을 사람이 손으로 세지 않게 하는 것**이 이 파일의 몫입니다. 남은
// 핸드로 나눈 핸드당 필요 점수, 덱에 남은 무늬의 수, 보스가 무력화하는 카드가 패에 몇
// 장인지가 그런 것들입니다 — 재료는 전부 화면에 있고 사람이 세고 있었습니다.
//
// **문장을 만들지 않습니다.** 열쇠와 채울 값을 내고 화면이 `tf(key, values)` 로 문장을
// 만듭니다. 여기서 문장을 만들면 게이트가 번역된 글을 견주게 되고, 그러면 말을 바꿀 때
// 게이트가 함께 깨집니다.
//
// **숨긴 정보를 쓰지 않습니다.** 덱 보기 화면에서 볼 수 있는 것만 봅니다 — 남은 카드의
// 구성은 보고 순서는 보지 않으며, 확률은 굴리지 않고 분자와 분모로 적습니다.
//
// 규격과 줄의 목록은 `doc/insight.md` 와 `doc/insight/rules.md` 입니다.

import { BlindKind } from '../generated/enums/blind-kind'
import { ConsumableKind } from '../generated/enums/consumable-kind'
import { DebuffKind } from '../generated/enums/debuff-kind'
import { EditionKind } from '../generated/enums/edition-kind'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { PerUnitMode } from '../generated/enums/per-unit-mode'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { Scope } from '../generated/enums/scope'
import { SealKind } from '../generated/enums/seal-kind'
import { StickerKind } from '../generated/enums/sticker-kind'
import { Trigger } from '../generated/enums/trigger'
import type { RankKind } from '../generated/enums/rank-kind'
import type { SuitKind } from '../generated/enums/suit-kind'

import type { Data, EffectRow } from './data'
import {
  describe, describeRow, handDisplay, rankDisplay, suitDisplay, valueText,
} from './describe'
import { dryScore, type DryRun } from './dry-run'
import { evaluate } from './hand'
import { PERISH_ROUNDS, rewardOf, tagFor, targetOf } from './run'
import { rerollCost } from './shop'
import { nameOf } from './strings'
import { subsets, valueOf } from './suggest'
import { MULT_ONE } from './units'
import type { CardInstance, GameEvent, JokerInstance, Phase, RunState } from './state'
import type { EffectHost } from './vm'

export type InsightGroup =
  | 'round' | 'pick' | 'option' | 'deck' | 'joker' | 'consumable' | 'blind' | 'economy'

/** 판에 세우는 차례입니다. 국면마다 이 가운데 몇 개가 보입니다. */
export const INSIGHT_GROUPS: readonly InsightGroup[] =
  ['round', 'pick', 'option', 'deck', 'joker', 'consumable', 'blind', 'economy']

export type InsightLevel = 'warn' | 'advise' | 'info'

/** 줄 하나. */
export interface Insight {
  group: InsightGroup
  level: InsightLevel
  /** `ui.insight.` 뒤에 붙는 이름. 시트의 열쇠입니다. */
  key: string
  /** 문장에 채우는 값. */
  values: Record<string, string | number>
  /** 쪽지에 적히는 줄들. */
  lines: string[]
}

/** 갈래 하나가 낼 수 있는 줄. */
const PER_GROUP = 3
/** 판 전체의 줄. */
const TOTAL = 14

/**
 * 국면마다 보이는 갈래.
 *
 * **끝난 판에는 아무것도 없습니다** — 다음 수가 없으므로 조언할 것이 없습니다.
 */
const BY_PHASE: Record<Phase, readonly InsightGroup[]> = {
  'round': INSIGHT_GROUPS,
  'blind-select': ['blind', 'joker', 'consumable', 'deck', 'economy'],
  'shop': ['economy', 'joker', 'consumable', 'deck'],
  'won': [],
  'lost': [],
}

/** 등급이 차례를 정합니다. 작은 것이 먼저입니다. */
const ORDER: Record<InsightLevel, number> = { warn: 0, advise: 1, info: 2 }

/**
 * 지금 판의 줄 전부.
 *
 * `selected` 는 화면이 골라 둔 카드의 `uid` 입니다. **코어가 고름 상태를 들고 있지 않으므로**
 * 화면이 넘깁니다 — 고르는 것은 아직 액션이 아니고, 액션이 아닌 것은 상태에 없습니다.
 */
export function insights(data: Data, state: RunState,
                         selected: readonly number[] = []): Insight[] {
  const groups = BY_PHASE[state.phase]
  if (groups.length === 0) return []

  const view = look(data, state, selected)
  const out: Insight[] = []

  for (const group of groups) {
    const rows = MAKERS[group](view)
    rows.sort((a, b) => ORDER[a.level] - ORDER[b.level])
    out.push(...rows.slice(0, PER_GROUP))
  }

  if (out.length <= TOTAL) return out

  // **상한을 넘으면 등급이 낮은 것부터 빠집니다.** 남는 것의 차례는 갈래 순서 그대로입니다 —
  // 등급으로 다시 세우면 같은 갈래의 줄이 판에서 흩어집니다.
  const keep = new Set([...out].sort((a, b) => ORDER[a.level] - ORDER[b.level]).slice(0, TOTAL))
  return out.filter(one => keep.has(one))
}

function line(group: InsightGroup, level: InsightLevel, key: string,
              values: Record<string, string | number> = {}, lines: string[] = []): Insight {
  return { group, level, key, values, lines }
}

// ── 한 번만 세는 것들 ─────────────────────────────────────────────────────────

/** 후보 조합 하나. */
interface Option {
  cards: CardInstance[]
  hand: PokerHandKind
  dry: DryRun
}

/**
 * 갈래들이 함께 보는 것.
 *
 * **여기서 한 번만 셉니다.** 갈래마다 따로 세면 건식 실행이 갈래 수만큼 돌고, 그것이 이
 * 기능에서 가장 비싼 계산입니다.
 */
interface View {
  data: Data
  state: RunState
  /** 패에 든 카드. */
  held: CardInstance[]
  /** 지금 고른 카드. 패에 있는 것만입니다. */
  picked: CardInstance[]
  /**
   * 아직 뽑지 않은 카드. **순서는 보지 않습니다.**
   *
   * 라운드 밖에서는 덱 전부입니다 — 그때는 뽑을 무더기가 비어 있고, 「남은 것이 0장」은
   * 사실이 아니라 아직 깔지 않았다는 뜻입니다.
   */
  left: CardInstance[]
  /** 지금 고른 것을 낸다면. */
  pick?: DryRun
  /** 족보 종류마다 하나. 예상 점수가 높은 것이 먼저입니다. */
  options: Option[]
}

function look(data: Data, state: RunState, selected: readonly number[]): View {
  const byUid = new Map(state.deck.map(card => [card.uid, card]))
  const alive = (uid: number): CardInstance | undefined => byUid.get(uid)

  // **득점을 세는 것은 라운드 안에서만입니다.** 다른 국면에는 낼 패가 없습니다.
  const scoring = state.phase === 'round'

  const held = state.hand.map(alive).filter(has)
  const picked = held.filter(card => selected.includes(card.uid))
  const left = scoring ? state.drawPile.map(alive).filter(has) : state.deck.slice()

  return {
    data, state, held, picked, left,
    pick: scoring && picked.length > 0
      ? dryScore(data, state, picked.map(card => card.uid))
      : undefined,
    options: scoring ? candidates(data, state, held) : [],
  }
}

function has<T>(value: T | undefined): value is T {
  return value !== undefined
}

/**
 * 족보 종류마다 값이 가장 높은 조합 하나.
 *
 * **전수를 건식 실행하지 않습니다.** 패가 8장이면 부분집합이 218개이고 득점 한 번이
 * 0.3~0.6밀리초이므로 전수는 한 프레임을 훨씬 넘습니다. 조커를 세지 않은 값으로 후보를 먼저
 * 좁히고 — `suggest.ts` 가 이미 그 계산입니다 — 좁힌 것만 실제 득점으로 셉니다. 족보가
 * 12종이므로 후보도 12개 아래입니다.
 */
function candidates(data: Data, state: RunState, held: CardInstance[]): Option[] {
  const best = new Map<PokerHandKind, { cards: CardInstance[]; value: number }>()

  for (const subset of subsets(held, Math.min(5, data.run.maxPlayedCards))) {
    const value = valueOf(data, state, subset)
    if (!value) continue
    const found = best.get(value.hand)
    // 같은 값이면 장수가 적은 쪽입니다. 남기는 카드가 많은 쪽이 다음 핸드에 낫습니다.
    if (found !== undefined && (found.value > value.value
      || (found.value === value.value && found.cards.length <= subset.length))) continue
    best.set(value.hand, { cards: subset, value: value.value })
  }

  const out: Option[] = []
  for (const [hand, one] of best) {
    const dry = dryScore(data, state, one.cards.map(card => card.uid))
    if (dry !== undefined) out.push({ cards: one.cards, hand, dry })
  }
  return out.sort((a, b) => b.dry.result.score - a.dry.result.score)
}

// ── 효과 행을 읽는 것들 ───────────────────────────────────────────────────────

function jokerRows(data: Data, joker: JokerInstance): readonly EffectRow[] {
  return data.jokerEffects.get(joker.jokerId) ?? []
}

/** 버리기를 보는 효과인가. */
function watchesDiscard(row: EffectRow): boolean {
  if (row.trigger === Trigger.OnHandDiscarded) return true
  if (row.trigger === Trigger.OnCardDiscarded) return true
  switch (row.condition.kind) {
    case 'CondDiscardsLeft':
    case 'CondDiscardsUnused':
    case 'CondFirstDiscard':
    case 'CondFirstDiscardSingleCard':
    case 'CondDiscardedFaceAtLeast':
      return true
    default:
      return false
  }
}

/** 이 효과가 보는 무늬들. 컬럼의 무늬 목록과 카드 무늬 조건 둘을 봅니다. */
function suitsWatched(row: EffectRow): SuitKind[] {
  const out = [...row.suits]
  if (row.condition.kind === 'CondCardSuit') out.push(row.condition.suit)
  return out
}

/** 득점 중에 값을 내려 하는 효과인가. */
function scoresValue(row: EffectRow): boolean {
  if (row.trigger !== Trigger.OnCardScored && row.trigger !== Trigger.OnCardHeld
    && row.trigger !== Trigger.OnHandPlayed) return false
  switch (row.operation.kind) {
    case 'OpAddChips':
    case 'OpAddMult':
    case 'OpMulMult':
    case 'OpRetrigger':
      return true
    case 'OpPerUnit':
      return row.operation.mode !== PerUnitMode.AddMoney
    default:
      return false
  }
}

/** 배수를 곱하는 효과를 가졌는가. */
function multiplies(rows: readonly EffectRow[]): boolean {
  return rows.some(row => row.operation.kind === 'OpMulMult'
    || (row.operation.kind === 'OpPerUnit'
      && (row.operation.mode === PerUnitMode.MulMult
        || row.operation.mode === PerUnitMode.MulEach)))
}

/** 칩이나 배수를 더하는 효과를 가졌는가. */
function adds(rows: readonly EffectRow[]): boolean {
  return rows.some(row => row.operation.kind === 'OpAddChips'
    || row.operation.kind === 'OpAddMult'
    || (row.operation.kind === 'OpPerUnit'
      && (row.operation.mode === PerUnitMode.AddChips
        || row.operation.mode === PerUnitMode.AddMult)))
}

/** 그 소모품이 카드를 몇 장 골라야 하는가. 0이면 고르지 않고 씁니다. */
function wantsCards(data: Data, kind: ConsumableKind, id: string): number {
  const rows = kind === ConsumableKind.Tarot ? data.tarotEffects.get(id)
    : kind === ConsumableKind.Spectral ? data.spectralEffects.get(id) : undefined
  let want = 0
  for (const row of rows ?? []) {
    if (row.scope === Scope.Selected) want = Math.max(want, row.scopeCount ?? 1)
  }
  return want
}

/** 효과의 임자를 사람이 읽는 이름으로. */
function ownerName(data: Data, host: EffectHost, row: EffectRow): string {
  if (host.joker !== undefined) {
    return nameOf(data, 'joker', host.joker.jokerId, host.joker.jokerId)
  }
  return nameOf(data, row.source, row.owner, row.owner)
}

// ── 갈래마다 ─────────────────────────────────────────────────────────────────

const MAKERS: Record<InsightGroup, (view: View) => Insight[]> = {
  round: roundLines,
  pick: pickLines,
  option: optionLines,
  deck: deckLines,
  joker: jokerLines,
  consumable: consumableLines,
  blind: blindLines,
  economy: economyLines,
}

function roundLines(view: View): Insight[] {
  const { data, state } = view
  const out: Insight[] = []
  const gap = state.target - state.score

  if (state.handsLeft > 0 && gap > 0) {
    out.push(line('round', 'info', 'round.per_hand',
                  { hands: state.handsLeft, need: Math.ceil(gap / state.handsLeft) }))
  }

  const picked = view.pick?.result.score ?? 0
  if (picked > 0 && state.score + picked >= state.target) {
    out.push(line('round', 'advise', 'round.clears'))
  }

  // **최선은 건식 실행한 값입니다.** 조커를 세지 않은 값으로 계산하면 조커가 붙은 판에서
  // 늘 모자란다고 적힙니다.
  const best = view.options[0]?.dry.result.score ?? 0
  if (gap > 0 && state.handsLeft > 0 && best > 0 && best * state.handsLeft < gap) {
    out.push(line('round', 'warn', 'round.unreachable',
                  { short: gap - best * state.handsLeft }))
  }

  if (state.discardsLeft > 0) {
    const watchers = state.jokers
      .filter(joker => jokerRows(data, joker).some(watchesDiscard)).length
    if (watchers > 0) {
      out.push(line('round', 'info', 'round.discards',
                    { left: state.discardsLeft, jokers: watchers }))
    }
  }

  return out
}

function pickLines(view: View): Insight[] {
  const { data, state, picked, pick } = view
  const out: Insight[] = []

  // **낼 수 없는 장수가 예상 점수보다 먼저입니다** — 낼 수 없으면 점수를 읽을 이유가 없습니다.
  if (state.rules.mustPlayFiveCards && picked.length !== data.run.maxPlayedCards) {
    out.push(line('pick', 'warn', 'pick.count',
                  { want: data.run.maxPlayedCards, have: picked.length }))
  }

  const dead = picked.filter(card => card.debuffed).length
  if (dead > 0) out.push(line('pick', 'warn', 'pick.debuffed', { n: dead }))

  if (pick === undefined) return out

  out.push(line('pick', 'info', 'pick.score', {
    score: pick.result.score,
    hand: handDisplay(data, pick.result.hand),
    chips: pick.result.chips,
    mult: multText(pick.result.mult),
  }))

  const fired = triggered(data, pick.events)
  if (fired.length > 0) {
    out.push(line('pick', 'info', 'pick.jokers', { n: fired.length },
                  fired.map(one => one.text)))
  }

  // **확률은 예상 점수에 들어가지 않고 따로 적힙니다.** 기대값으로 섞으면 실제로는 나올 수
  // 없는 점수가 적히고, 그 숫자를 보고 낸 사람은 그 점수가 나오지 않은 이유를 찾을 수 없습니다.
  if (pick.chanced.length > 0) {
    const first = pick.chanced[0]
    out.push(line('pick', 'info', 'pick.chance',
                  { num: first.num, den: first.den, n: pick.chanced.length },
                  pick.chanced.map(one =>
                    `${ownerName(data, one.host, one.row)}  ${describeRow(data, one.row)}`)))
  }

  return out
}

/** 이 조합에서 발동한 조커들. 자리 순서이고 조커 하나가 한 줄입니다. */
function triggered(data: Data,
                   events: readonly GameEvent[]): { slot: number; text: string }[] {
  const bySlot = new Map<number, { id: string; parts: string[] }>()
  for (const event of events) {
    if (event.t !== 'JokerTriggered') continue
    const found = bySlot.get(event.slot) ?? { id: event.jokerId, parts: [] }
    found.parts.push(valueText(event.op, event.chips, event.mult, event.money))
    bySlot.set(event.slot, found)
  }
  return [...bySlot.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([slot, one]) => ({
      slot,
      text: `${nameOf(data, 'joker', one.id, one.id)}  ${one.parts.join(' · ')}`,
    }))
}

/**
 * 후보 조합의 줄들.
 *
 * **고른 것이 없으면 견줄 것이 없습니다.** 그때는 후보를 값과 함께 세 줄로 적고, 고른 것이
 * 있으면 첫 줄이 그것과의 차이입니다.
 */
function optionLines(view: View): Insight[] {
  const { data, options, pick } = view
  if (options.length === 0) return []

  const out: Insight[] = []
  const now = pick?.result.score ?? 0
  const best = options[0]

  if (now > 0) {
    if (best.dry.result.score > now) {
      out.push(line('option', 'advise', 'option.best', {
        hand: handDisplay(data, best.hand),
        score: best.dry.result.score,
        delta: best.dry.result.score - now,
      }))
    } else {
      out.push(line('option', 'info', 'option.same'))
    }
  }

  const from = now > 0 ? 1 : 0
  for (const one of options.slice(from, from + PER_GROUP)) {
    out.push(line('option', 'info', 'option.alt',
                  { hand: handDisplay(data, one.hand), score: one.dry.result.score }))
  }

  return out
}

function deckLines(view: View): Insight[] {
  const { data, state, held, left } = view
  const out: Insight[] = []

  out.push(line('deck', 'info', 'deck.left',
                { total: state.deck.length, left: left.length, held: held.length }))

  if (left.length > 0) {
    const counts = new Map<SuitKind, number>()
    for (const card of left) counts.set(card.suit, (counts.get(card.suit) ?? 0) + 1)
    const [suit, n] = [...counts.entries()].sort((a, b) => b[1] - a[1])[0]
    out.push(line('deck', 'info', 'deck.suit', {
      suit: suitDisplay(data, suit), n,
      pct: Math.round((n / left.length) * 100),
    }))
  }

  const gap = straightGap(view)
  if (gap !== undefined) {
    out.push(line('deck', 'advise', 'deck.rank_gap',
                  { rank: rankDisplay(data, gap.rank), n: gap.left }))
  }

  const enhanced = state.deck.filter(card => card.enhancement !== EnhancementKind.None).length
  if (enhanced > 0) out.push(line('deck', 'info', 'deck.enhanced', { n: enhanced }))

  return out
}

/**
 * 한 장이면 스트레이트가 되는 랭크.
 *
 * **판정을 다시 구현하지 않습니다** — 패의 카드 넷에 그 랭크의 카드 한 장을 얹어 `evaluate`
 * 에 넣습니다. `straightGap` 과 `flushStraightCards` 규칙이 그대로 걸립니다.
 *
 * 이미 스트레이트가 되는 패에서는 세지 않습니다. 여럿이면 **남은 것에 가장 많이 있는 랭크**
 * 하나를 냅니다 — 조언은 뽑힐 가능성이 큰 쪽이어야 합니다.
 */
function straightGap(view: View): { rank: RankKind; left: number } | undefined {
  const { state, held, left, options } = view
  const need = state.rules.flushStraightCards
  if (held.length < need - 1) return undefined
  if (options.some(one => one.hand === PokerHandKind.Straight
    || one.hand === PokerHandKind.StraightFlush)) return undefined

  const remaining = new Map<RankKind, number>()
  for (const card of left) remaining.set(card.rank, (remaining.get(card.rank) ?? 0) + 1)
  if (remaining.size === 0) return undefined

  let found: { rank: RankKind; left: number } | undefined
  for (const [rank, count] of remaining) {
    if (found !== undefined && count <= found.left) continue
    if (!oneAwayFromStraight(view, rank)) continue
    found = { rank, left: count }
  }
  return found
}

/** 이 랭크의 카드 한 장을 더하면 스트레이트가 되는가. */
function oneAwayFromStraight(view: View, rank: RankKind): boolean {
  const { state, held } = view
  const need = state.rules.flushStraightCards

  // **깨끗한 카드를 얹습니다.** 패의 카드를 베끼면 그 카드의 강화가 함께 따라오고, `Stone`
  // 은 족보를 이루지 않으므로 답이 달라집니다.
  const extra: CardInstance = {
    uid: -1, baseCardId: '', rank, suit: held[0].suit,
    enhancement: EnhancementKind.None, seal: SealKind.None, edition: EditionKind.Base,
    bonusChips: 0, debuffed: false, faceDown: false,
  }

  for (const subset of subsets(held, need - 1)) {
    if (subset.length !== need - 1) continue
    const { hand } = evaluate([...subset, extra], state.rules)
    if (hand === PokerHandKind.Straight || hand === PokerHandKind.StraightFlush) return true
  }
  return false
}

function jokerLines(view: View): Insight[] {
  const { data, state, left, pick } = view
  const out: Insight[] = []

  // 같은 무늬를 보는 조커가 둘 이상이면 그 무늬가 이 판의 축입니다.
  const watching = new Map<SuitKind, number>()
  for (const joker of state.jokers) {
    const suits = new Set(jokerRows(data, joker).flatMap(suitsWatched))
    for (const suit of suits) watching.set(suit, (watching.get(suit) ?? 0) + 1)
  }
  const focus = [...watching.entries()].filter(([, n]) => n >= 2).sort((a, b) => b[1] - a[1])[0]
  if (focus !== undefined) {
    out.push(line('joker', 'info', 'joker.suit_focus', {
      n: focus[1], suit: suitDisplay(data, focus[0]),
      left: left.filter(card => card.suit === focus[0]).length,
    }))
  }

  const swap = betterOrder(view)
  if (swap !== undefined) out.push(line('joker', 'advise', 'joker.order', swap))

  const off = state.jokers.filter(joker => joker.disabled).length
  if (off > 0) out.push(line('joker', 'warn', 'joker.disabled', { n: off }))

  for (const joker of state.jokers) {
    if (joker.sticker !== StickerKind.Perishable || joker.disabled) continue
    const rounds = PERISH_ROUNDS - joker.age
    if (rounds > 2) continue
    out.push(line('joker', 'warn', 'joker.perish',
                  { name: nameOf(data, 'joker', joker.jokerId, joker.jokerId), n: rounds }))
  }

  // **득점하려 하는데 못 한 조커만 셉니다.** 라운드 끝이나 상점에서 도는 조커까지 세면
  // 「아무것도 하지 않습니다」가 늘 적히고, 그것은 사실이 아닙니다.
  if (pick !== undefined) {
    const fired = new Set(triggered(data, pick.events).map(one => one.slot))
    const idle = state.jokers.filter((joker, slot) =>
      !joker.disabled && !fired.has(slot) && jokerRows(data, joker).some(scoresValue)).length
    if (idle > 0) out.push(line('joker', 'info', 'joker.idle', { n: idle }))
  }

  if (state.phase !== 'round' && state.jokers.length >= state.rules.jokerSlots) {
    out.push(line('joker', 'info', 'joker.slots', { slots: state.rules.jokerSlots }))
  }

  return out
}

/**
 * 인접한 조커 둘의 자리를 바꾸면 점수가 오르는가.
 *
 * **후보는 곱하기가 더하기의 왼쪽인 쌍뿐입니다.** 곱하기가 먼저 들어가면 그 뒤에 더해지는
 * 값에 곱이 걸리지 않습니다. 그 쌍마다 바꾼 차례로 실제 득점을 한 번 더 돌리고 **실제로
 * 오른 것만** 냅니다 — 조건과 누적값이 자리를 함께 보므로 언제나 오르지는 않습니다.
 */
function betterOrder(view: View): Record<string, string | number> | undefined {
  const { data, state, picked, pick } = view
  if (pick === undefined || picked.length === 0) return undefined
  if (state.jokers.length < 2) return undefined

  const uids = state.jokers.map(joker => joker.uid)
  const cards = picked.map(card => card.uid)
  let best: { delta: number; left: JokerInstance; right: JokerInstance } | undefined

  for (let slot = 0; slot + 1 < state.jokers.length; slot++) {
    const left = state.jokers[slot]
    const right = state.jokers[slot + 1]
    if (!multiplies(jokerRows(data, left))) continue
    if (!adds(jokerRows(data, right))) continue

    const order = uids.slice()
    order[slot] = uids[slot + 1]
    order[slot + 1] = uids[slot]
    const swapped = dryScore(data, state, cards, order)
    if (swapped === undefined) continue

    const delta = swapped.result.score - pick.result.score
    if (delta <= 0) continue
    if (best === undefined || delta > best.delta) best = { delta, left, right }
  }

  if (best === undefined) return undefined
  return {
    left: nameOf(data, 'joker', best.left.jokerId, best.left.jokerId),
    right: nameOf(data, 'joker', best.right.jokerId, best.right.jokerId),
    delta: best.delta,
  }
}

function consumableLines(view: View): Insight[] {
  const { data, state, picked } = view
  const out: Insight[] = []

  for (const item of state.consumables) {
    const name = nameOf(data, kindGroup(item.kind), item.id, item.id)

    if (item.kind === ConsumableKind.Planet) {
      const planet = data.tables.planet.findByPlanetId(item.id)
      if (planet === undefined) continue
      out.push(line('consumable', 'advise', 'consumable.planet', {
        name, hand: handDisplay(data, planet.hand),
        n: state.handPlayCounts[PokerHandKind[planet.hand]] ?? 0,
      }))
      continue
    }

    const want = wantsCards(data, item.kind, item.id)
    if (want > 0 && picked.length !== want) {
      out.push(line('consumable', 'warn', 'consumable.needs_cards',
                    { name, want, have: picked.length }))
    }
  }

  if (state.consumables.length >= state.rules.consumableSlots) {
    out.push(line('consumable', 'info', 'consumable.slots',
                  { slots: state.rules.consumableSlots }))
  }

  return out
}

/** 소모품의 갈래가 글 표의 어느 무리인가. */
function kindGroup(kind: ConsumableKind): string {
  return kind === ConsumableKind.Tarot ? 'tarot'
    : kind === ConsumableKind.Planet ? 'planet' : 'spectral'
}

function blindLines(view: View): Insight[] {
  const { data, state, held, left } = view
  const out: Insight[] = []
  const rows = data.bossEffects.get(state.bossId) ?? []

  if (state.blind === BlindKind.Boss && rows.length > 0) {
    const row = data.tables.bossBlind.findByBossId(state.bossId)
    out.push(line('blind', 'warn', 'blind.rule',
                  { name: nameOf(data, 'boss', state.bossId, row?.name ?? state.bossId) },
                  describe(data, rows)))

    // **보스가 걸기 전에도 적힙니다** — 블라인드를 고르는 자리에서 알아야 건너뛸지를 정할
    // 수 있습니다.
    for (const one of rows) {
      if (one.operation.kind !== 'OpDebuff') continue
      if (one.operation.debuff !== DebuffKind.BySuit) continue
      const suit = one.operation.suit
      out.push(line('blind', 'warn', 'blind.debuff_suit', {
        suit: suitDisplay(data, suit),
        held: held.filter(card => card.suit === suit).length,
        left: left.filter(card => card.suit === suit).length,
      }))
      break
    }
  }

  if (state.rules.noRepeatHandTypes && state.handTypesThisRound.length > 0) {
    const names = [...new Set(state.handTypesThisRound)]
      .map(name => handDisplay(data, PokerHandKind[name as keyof typeof PokerHandKind]))
    out.push(line('blind', 'warn', 'blind.hand_used', { hands: names.join(' · ') }))
  }

  if (state.phase === 'blind-select') {
    out.push(line('blind', 'info', 'blind.target', {
      target: targetOf(data, state, state.blind),
      reward: rewardOf(data, state, state.blind),
    }))

    const tag = tagFor(state, state.blind)
    if (tag !== undefined && (data.tables.blind.findByBlind(state.blind)?.skippable ?? false)) {
      out.push(line('blind', 'info', 'blind.skip',
                    { tag: nameOf(data, 'tag', tag, tag) },
                    describe(data, data.tagEffects.get(tag) ?? [])))
    }
  }

  return out
}

function economyLines(view: View): Insight[] {
  const { data, state } = view
  const out: Insight[] = []
  const rules = state.rules

  if (rules.interestPer5 > 0 && !rules.noInterest && rules.interestCap > 0) {
    const money = Math.max(0, state.money)
    const now = Math.min(rules.interestCap, Math.floor(money / 5) * rules.interestPer5)
    if (now >= rules.interestCap) {
      out.push(line('economy', 'info', 'economy.interest_cap', { cap: rules.interestCap }))
    } else {
      out.push(line('economy', 'advise', 'economy.interest',
                    { need: 5 - (money % 5), gain: rules.interestPer5 }))
    }
  }

  if (state.phase === 'shop') {
    out.push(line('economy', 'info', 'economy.reroll',
                  { cost: rerollCost(data, state, state.shop) }))
  }

  if (rules.debtLimit < 0) {
    out.push(line('economy', 'info', 'economy.debt', { limit: rules.debtLimit }))
  }

  return out
}

/** 만분율 배수를 사람이 읽는 수로. `15000` 이 `1.5` 입니다. */
function multText(mult: number): string {
  const value = mult / MULT_ONE
  return Number.isInteger(value) ? String(value) : value.toFixed(2)
}
