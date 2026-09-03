// 효과를 사람이 읽는 문장으로.
//
// **조커 150종의 설명문이 데이터에 없습니다.** 효과가 데이터에 있으므로 설명도 거기서
// 나옵니다 — 그러면 값을 고쳤을 때 설명이 어긋날 수 없습니다.
//
// 문구는 `StringTable` 의 `phrase.*` 이고, 여기 있는 것은 그것을 채우는 규칙뿐입니다.

import type { Condition } from '../generated/structs/condition'
import type { Operation } from '../generated/structs/operation'
import { PerUnitMode } from '../generated/enums/per-unit-mode'
import type { Data, EffectRow } from './data'
import { nameOf, t, text, tf } from './strings'
import { MULT_ONE } from './units'

/** 문구 하나를 꺼냅니다. 없으면 열쇠를 그대로 돌려주어 빠진 것이 눈에 띄게 합니다. */
function phrase(data: Data, key: string): string {
  const found = text(data, `phrase.${key}`)
  // **없는 것은 눈에 띄어야 합니다.** 열쇠를 그대로 두면 문장에 섞여 지나가므로 표시를 답니다.
  return found === `phrase.${key}` ? `«${key}»` : found
}

/**
 * 문구를 가진 갈래와 그 enum 의 이름.
 *
 * **여기 없는 갈래는 문장에 열쇠가 그대로 나옵니다.** `phrase.<갈래>.<이름>` 이 시트에
 * 있어야 하고, 그것을 세는 게이트가 이 표를 읽습니다 — `phrase.scope.RandomInDeck` 이
 * 없어서 조커의 설명에 열쇠가 그대로 적혀 있던 것이 그렇게 지나갔습니다.
 */
export const PHRASE_FAMILIES: Record<string, string> = {
  blind: 'BlindKind',
  compare: 'Compare',
  counter: 'CounterField',
  create: 'CreateKind',
  debuff: 'DebuffKind',
  handpick: 'HandPick',
  modify: 'ModifyKind',
  pick: 'JokerPick',
  rarity: 'Rarity',
  rule: 'RuleKind',
  scope: 'Scope',
  suit: 'SuitKind',
  target: 'TargetKind',
  trait: 'CardTrait',
  trigger: 'Trigger',
  unit: 'UnitKind',
}

function named(data: Data, kind: string, value: number, enumName: Record<number, string>): string {
  return phrase(data, `${kind}.${enumName[value] ?? value}`)
}

/** 만분율을 사람이 읽는 수로. `15000` 이 `1.5` 입니다. */
function bp(value: number): string {
  if (value % MULT_ONE === 0) return String(value / MULT_ONE)
  return (value / MULT_ONE).toFixed(2).replace(/0+$/, '').replace(/\.$/, '')
}

function fill(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? values[name] : whole)
}

/** 한 줄짜리 설명 하나. 효과 행 하나가 문장 하나입니다. */
export function describeRow(data: Data, row: EffectRow): string {
  const enums = data.enumNames

  const cond = describeCondition(data, row)
  const op = describeOperation(data, row)
  const chance = row.chanceNum !== null && row.chanceDen !== null
    ? tf('phrase.chance', { num: row.chanceNum, den: row.chanceDen })
    : ''

  const template = phrase(data, `trigger.${enums.Trigger[row.trigger] ?? row.trigger}`)
  const sentence = fill(template, { cond, op: chance + op })
  return sentence.replace(/\s+/g, ' ').trim()
}

/** 여러 행을 가진 것의 설명 전체. */
export function describe(data: Data, rows: readonly EffectRow[]): string[] {
  const lines: string[] = []
  for (const row of rows) {
    const line = describeRow(data, row)
    if (line.length > 0 && !lines.includes(line)) lines.push(line)
  }
  return lines
}

function describeCondition(data: Data, row: EffectRow): string {
  const cond: Condition = row.condition
  const enums = data.enumNames
  const template = phrase(data, `cond.${cond.kind}`)

  const values: Record<string, string> = {
    ranks: row.ranks.map(rank => rankText(data, rank)).join(' · '),
    suits: row.suits.map(suit => named(data, 'suit', suit, enums.SuitKind)).join(' · '),
  }

  if ('hand' in cond) values.hand = handName(data, cond.hand)
  if ('suit' in cond) values.suit = named(data, 'suit', cond.suit, enums.SuitKind)
  if ('n' in cond) values.n = String(cond.n)
  if ('num' in cond) values.num = String(cond.num)
  if ('den' in cond) values.den = String(cond.den)
  if ('compare' in cond) values.compare = named(data, 'compare', cond.compare, enums.Compare)
  if ('counter' in cond) values.counter = named(data, 'counter', cond.counter, enums.CounterField)
  if ('target' in cond) values.target = named(data, 'target', cond.target, enums.TargetKind)
  if ('blind' in cond) values.blind = named(data, 'blind', cond.blind, enums.BlindKind)
  if ('enhancement' in cond) {
    values.enhancement = data.tables.enhancement.findByEnhancement(cond.enhancement)?.display ?? ''
  }
  if ('seal' in cond) values.seal = data.tables.seal.findBySeal(cond.seal)?.display ?? ''
  if ('edition' in cond) values.edition = data.tables.edition.findByEdition(cond.edition)?.display ?? ''
  if ('consumable' in cond) {
    values.consumable = named(data, 'create', cond.consumable, enums.ConsumableKind)
  }

  return fill(template, values)
}

function describeOperation(data: Data, row: EffectRow): string {
  const op: Operation = row.operation
  const enums = data.enumNames
  const template = phrase(data, `op.${op.kind}`)

  const values: Record<string, string> = {
    scope: named(data, 'scope', row.scope, enums.Scope),
  }

  switch (op.kind) {
    case 'OpAddChips':
      values.chips = String(op.chips)
      break
    case 'OpAddMult':
    case 'OpMulMult':
      values.mult = bp(op.mult)
      break
    case 'OpAddMoney':
    case 'OpSetMoney':
      values.money = String(op.money)
      break
    case 'OpPerUnit': {
      values.unit = fill(named(data, 'unit', op.unit, enums.UnitKind), {
        ranks: row.ranks.map(rank => rankText(data, rank)).join(' · '),
        enhancement: data.tables.enhancement.findByEnhancement(op.enhancement)?.display ?? '',
        rarity: named(data, 'rarity', op.rarity, enums.Rarity),
      })
      values.per = perUnit(data, op.mode, op.value, op.baseValue)
      break
    }
    case 'OpRandomRange':
      values.per = op.mode === PerUnitMode.AddChips ? t('ui.slot.chips') : t('ui.slot.mult')
      values.min = String(op.mode === PerUnitMode.AddChips ? op.min : op.min / MULT_ONE)
      values.max = String(op.mode === PerUnitMode.AddChips ? op.max : op.max / MULT_ONE)
      break
    case 'OpRetrigger':
      values.times = String(op.times)
      break
    case 'OpGrowSelf':
    case 'OpGrowOthers':
      values.counter = named(data, 'counter', op.counter, enums.CounterField)
      values.step = op.counter === 3 || op.counter === 2 ? bp(Math.abs(op.step)) : String(Math.abs(op.step))
      if (op.step < 0) values.step = `-${values.step}`
      break
    case 'OpResetSelf':
      values.counter = named(data, 'counter', op.counter, enums.CounterField)
      break
    case 'OpLevelUpHand':
      values.hand_pick = named(data, 'handpick', op.handPick, enums.HandPick)
      values.levels = op.levels >= 0 ? `+${op.levels}` : String(op.levels)
      break
    case 'OpCreateCard':
    case 'OpAddCard':
    case 'OpShopGift':
    case 'OpGrant':
      values.create = named(data, 'create', op.create, enums.CreateKind)
      values.count = String('count' in op ? op.count || 1 : 1)
      // **무엇을 주는지가 이름입니다.** 「바우처를 1개」로만 적으면 어느 바우처인지 알 수
      // 없고, 그러면 이 줄이 챌린지를 고르는 데 쓰이지 않습니다.
      values.name = grantName(data, op.create, 'refId' in op ? op.refId : '')
      break
    case 'OpDestroyCard':
    case 'OpForceDiscard':
      values.count = String(op.count)
      break
    case 'OpModifyCard':
      values.modify = named(data, 'modify', op.modify, enums.ModifyKind)
      break
    case 'OpModifyJoker':
      values.edition = data.tables.edition.findByEdition(op.edition)?.display ?? t('ui.edition.random')
      break
    case 'OpDestroyJoker':
    case 'OpCopyJoker':
      values.pick = named(data, 'pick', op.pick, enums.JokerPick)
      break
    case 'OpDebuff':
      values.debuff = named(data, 'debuff', op.debuff, enums.DebuffKind)
      break
    case 'OpChangeRule':
    case 'OpChangeRuleByCounter': {
      const rule = phrase(data, `rule.${enums.RuleKind[op.rule] ?? op.rule}`)
      const raw = 'value' in op ? op.value : 1
      values.rule = fill(rule, {
        value: signed(enums.RuleKind[op.rule], raw,
                      'absolute' in op ? op.absolute : false),
        suits: row.suits.map(suit => named(data, 'suit', suit, enums.SuitKind)).join(' · '),
      })
      break
    }
    case 'OpCardTrait':
      values.trait = named(data, 'trait', op.trait, enums.CardTrait)
      break
    case 'OpMulMoney':
      values.value = bp(op.value || MULT_ONE)
      break
    case 'OpCustom':
      return phrase(data, `handler.${op.handler}`)
    default:
      break
  }

  return fill(template, values)
}

/**
 * 규칙의 값. 늘고 주는 것은 부호를 붙이고, 정하는 것은 그대로 적습니다.
 *
 * **행의 `absolute` 가 먼저입니다.** 규칙 이름으로만 판정하면 같은 규칙이 증감으로 걸릴
 * 때와 절대값으로 걸릴 때가 구분되지 않습니다 — 「$100 으로 시작」이 「$+100 을 더
 * 가지고」로 적혀 있었습니다.
 */
function signed(rule: string | undefined, value: number, absoluteRow = false): string {
  const absolute = new Set([
    'ShopDiscount', 'ShopWeightTarot', 'ShopWeightPlanet', 'EditionWeightScale',
    'ProbabilityScale', 'BlindSizeScale', 'FlushStraightCards', 'StraightGap',
    'BossRerollsPerAnte', 'InterestCap', 'PlanetGivesMult',
  ])
  if (rule === 'PlanetGivesMult' || rule === 'BlindSizeScale') return bp(value)
  // 빚 한도는 바닥의 값이 음수로 적혀 있습니다. 「-20까지 빚을」이 아니라 「20까지 빚을」입니다.
  if (rule === 'DebtLimit') return String(Math.abs(value))
  if (absoluteRow) return String(value)
  if (rule !== undefined && absolute.has(rule)) return String(value)
  return value >= 0 ? `+${value}` : String(value)
}

/**
 * `OpGrant` 가 주는 것의 표시 이름.
 *
 * **갈래마다 이름이 다른 표에 있습니다.** `CreateKind` 가 어느 표인지를 정하고, 못 찾으면
 * 식별자를 그대로 돌려주어 빠진 것이 눈에 띄게 합니다.
 */
function grantName(data: Data, create: number, refId: string): string {
  if (refId === '') return ''
  const kind = GRANT_KINDS[create]
  if (kind === undefined) return refId
  return nameOf(data, kind, refId, refId)
}

/** `CreateKind` 의 값마다 이름이 어느 표에 있는가. */
const GRANT_KINDS: Record<number, string> = {
  1: 'tarot', 2: 'planet', 3: 'spectral', 4: 'joker', 6: 'tag', 11: 'voucher',
}

function perUnit(data: Data, mode: PerUnitMode, value: number, base: number): string {
  switch (mode) {
    case PerUnitMode.AddChips: return tf('phrase.op.add_chips_value', { n: value })
    case PerUnitMode.AddMult: return tf('phrase.op.add_mult_value', { n: bp(value) })
    case PerUnitMode.AddMoney: return `$${value}`
    case PerUnitMode.MulEach: return tf('phrase.op.mul_mult_value', { n: bp(value) })
    case PerUnitMode.MulMult:
      return base === 0 ? tf('phrase.op.mul_mult_value', { n: bp(value) }) : tf('phrase.op.mul_mult_each', { n: bp(value) })
    default: return ''
  }
  void data
}

function handName(data: Data, hand: number): string {
  const key = data.enumNames.PokerHandKind[hand]
  return text(data, `hand.${key}.name`)
}

function rankText(data: Data, rank: number): string {
  return data.tables.rank.findByRank(rank)?.display ?? String(rank)
}
