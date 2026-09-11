// 이름으로 찾는 표.
//
// **무엇이 어떤 갈래인가를 적어 둔 것들입니다.** 코어가 내보내는 이름을 받아 화면이
// 쓰는 값으로 옮기고, 그 옮김이 코드의 분기가 아니라 표의 한 줄입니다 — 갈래가
// 하나 늘면 줄이 하나 늘고 코드는 그대로입니다.

import { UI } from '../render/theme'
import { t, tf } from '../core/strings'
import { BlindKind } from '../generated/enums/blind-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { ShopItemKind } from '../generated/enums/shop-item-kind'
import { SuitKind } from '../generated/enums/suit-kind'
import type { InsightLevel } from '../core/insight'

/**
 * 칩과 배수가 뜻을 갖는 박자들.
 *
 * **정산 뒤의 박자도 그 값을 들고 있습니다.** 박자가 값을 나르는 것이 그 뜻이므로 — 다음
 * 패를 깔고 돈을 주는 동안에도 마지막 값이 그대로 붙어 있습니다 — 문턱을 보는 자리는
 * 득점하는 동안으로 한정합니다.
 */
export const SCORING_BEATS: ReadonlySet<string> = new Set([
  'HandEvaluated', 'CardScored', 'JokerTriggered', 'RunTriggered', 'JokerFizzled', 'Retriggered',
])

/**
 * 값을 바꾸는 연산의 이름.
 *
 * **이것이 아닌 이름으로 오는 것은 「무언가를 했다」입니다.** 코어는 값을 바꾸지 않는
 * 효과에도 「누가 했는가」를 내는데(`operations.ts` 의 `apply`), 그 이벤트는 칩·배수·돈이
 * 셋 다 0 이고 `op` 에 무엇을 한 것인지가 담깁니다.
 */
export const VALUE_OPS = new Set([
  'AddChips', 'AddMult', 'MulMult', 'AddMoney', 'SetMoney', 'PerUnit', 'RandomRange',
  'GrowSelf', 'MulMoney',
])

/**
 * 무언가를 한 것의 갈래.
 *
 * **갈래마다 색과 소리가 다릅니다.** 「칩 +4」와 「조커 하나를 부숩니다」가 같은 몸짓이면
 * 무엇이 일어난 것인지 남지 않습니다 — 값 연산의 몸짓이 세기만 다른 하나였던 것과 같은
 * 문제이고, 여기서 갈래를 넷으로 가릅니다.
 */
export const ACT_KINDS: Record<string, 'make' | 'break' | 'change' | 'rule' | 'boss'> = {
  CreateCard: 'make', AddCard: 'make', Grant: 'make', ShopGift: 'make',
  DestroyCard: 'break', DestroyJoker: 'break', ForceDiscard: 'break',
  ModifyCard: 'change', ModifyJoker: 'change', CopyJoker: 'change', CardTrait: 'change',
  ChangeRule: 'rule', ChangeRuleByCounter: 'rule', LevelUpHand: 'rule',
  DisableBoss: 'rule', PreventLoss: 'rule', DuplicateNextTag: 'rule', RerollBoss: 'rule',
  Debuff: 'boss', DrawFaceDown: 'boss', FlipJokers: 'boss', DisableRandomJoker: 'boss',
}

/**
 * 갈래마다의 색과 소리와 말.
 *
 * **말이 갈래를 나눕니다.** 다섯이 다 「발동」 하나였을 때는 소리와 색만 달랐고, 그 둘은
 * 무엇을 한 것인지까지는 말해 주지 않습니다 — 만든 것과 부순 것이 같은 글로 떴습니다.
 */
export const ACT_LOOK: Record<string, { tint: number; cue: string; say: string }> = {
  make: { tint: UI.good, cue: 'card_place', say: 'ui.act.make' },
  break: { tint: UI.bad, cue: 'card_destroy', say: 'ui.act.break' },
  change: { tint: UI.legendary, cue: 'card_flip', say: 'ui.act.change' },
  rule: { tint: UI.money, cue: 'voucher_buy', say: 'ui.act.rule' },
  boss: { tint: UI.bad, cue: 'boss_reveal', say: 'ui.act.boss' },
}

/**
 * 소모품 슬롯으로 가는 갈래인가.
 *
 * **플레잉 카드는 아닙니다.** 「조커가 아니면 소모품」으로 세고 있어서, 표준 팩에서 카드를
 * 집으면 아무 상관 없는 소모품 하나가 팩에서 날아오는 연출이 붙었습니다 — 카드는 덱으로
 * 들어가고 소모품 칸은 그대로인데 화면만 그렇게 보였습니다.
 */
export function isConsumable(kind: ShopItemKind): boolean {
  return kind === ShopItemKind.Tarot || kind === ShopItemKind.Planet
    || kind === ShopItemKind.Spectral
}

/**
 * 족보 하나가 어떤 모양인가.
 *
 * **규칙이 아니라 보기입니다.** 어느 카드로 예를 들지는 판정에 아무 영향이 없고, 그래서
 * 표가 아니라 여기 있습니다. `counts` 는 그 카드가 족보에 드는가입니다 — 들지 않는 카드가
 * 물러나 있어야 「다섯 장을 냈는데 둘만 센다」가 그림에 남습니다.
 */
export const HAND_SHAPE: Partial<Record<PokerHandKind, { rank: number; suit: SuitKind;
                                                  counts: boolean }[]>> = {
  [PokerHandKind.HighCard]: [
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 10, suit: SuitKind.Heart, counts: false },
    { rank: 7, suit: SuitKind.Club, counts: false },
    { rank: 5, suit: SuitKind.Diamond, counts: false },
    { rank: 3, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.Pair]: [
    { rank: 9, suit: SuitKind.Spade, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: false },
    { rank: 6, suit: SuitKind.Diamond, counts: false },
    { rank: 2, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.TwoPair]: [
    { rank: 9, suit: SuitKind.Spade, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 4, suit: SuitKind.Club, counts: true },
    { rank: 4, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.ThreeOfAKind]: [
    { rank: 7, suit: SuitKind.Spade, counts: true },
    { rank: 7, suit: SuitKind.Heart, counts: true },
    { rank: 7, suit: SuitKind.Club, counts: true },
    { rank: 11, suit: SuitKind.Diamond, counts: false },
    { rank: 3, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.Straight]: [
    { rank: 5, suit: SuitKind.Spade, counts: true },
    { rank: 6, suit: SuitKind.Heart, counts: true },
    { rank: 7, suit: SuitKind.Club, counts: true },
    { rank: 8, suit: SuitKind.Diamond, counts: true },
    { rank: 9, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.Flush]: [
    { rank: 2, suit: SuitKind.Heart, counts: true },
    { rank: 6, suit: SuitKind.Heart, counts: true },
    { rank: 9, suit: SuitKind.Heart, counts: true },
    { rank: 11, suit: SuitKind.Heart, counts: true },
    { rank: 13, suit: SuitKind.Heart, counts: true },
  ],
  [PokerHandKind.FullHouse]: [
    { rank: 8, suit: SuitKind.Spade, counts: true },
    { rank: 8, suit: SuitKind.Heart, counts: true },
    { rank: 8, suit: SuitKind.Club, counts: true },
    { rank: 3, suit: SuitKind.Diamond, counts: true },
    { rank: 3, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.FourOfAKind]: [
    { rank: 12, suit: SuitKind.Spade, counts: true },
    { rank: 12, suit: SuitKind.Heart, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: true },
    { rank: 12, suit: SuitKind.Diamond, counts: true },
    { rank: 5, suit: SuitKind.Spade, counts: false },
  ],
  [PokerHandKind.StraightFlush]: [
    { rank: 9, suit: SuitKind.Club, counts: true },
    { rank: 10, suit: SuitKind.Club, counts: true },
    { rank: 11, suit: SuitKind.Club, counts: true },
    { rank: 12, suit: SuitKind.Club, counts: true },
    { rank: 13, suit: SuitKind.Club, counts: true },
  ],
  [PokerHandKind.FiveOfAKind]: [
    { rank: 10, suit: SuitKind.Spade, counts: true },
    { rank: 10, suit: SuitKind.Heart, counts: true },
    { rank: 10, suit: SuitKind.Club, counts: true },
    { rank: 10, suit: SuitKind.Diamond, counts: true },
    { rank: 10, suit: SuitKind.Spade, counts: true },
  ],
  [PokerHandKind.FlushHouse]: [
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 6, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Diamond, counts: true },
    { rank: 13, suit: SuitKind.Diamond, counts: true },
  ],
  [PokerHandKind.FlushFive]: [
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
    { rank: 14, suit: SuitKind.Spade, counts: true },
  ],
}

/**
 * 인사이트 줄의 등급이 무슨 색인가.
 *
 * **셋뿐입니다** — 지금 손해를 보고 있는 것, 바꾸면 나아지는 것, 알아 두면 되는 것.
 * 넷째를 두면 색만으로는 갈리지 않고 사람이 범례를 찾게 됩니다.
 */
export const INSIGHT_COLOR: Record<InsightLevel, number> = {
  warn: UI.bad,
  advise: UI.good,
  info: UI.inkDim,
}

/** 돈이 왜 오갔는가. 표에 없는 갈래는 적지 않습니다. */
/** 바뀐 규칙의 이름. 표에 없는 것은 식별자를 그대로 적습니다. */
/**
 * 규칙 하나가 어떻게 바뀌었는가.
 *
 * **읽는 법이 셋입니다** — 켜고 끄는 것, 수를 세는 것, 만분율로 적힌 배수. 셋을 한 가지로
 * 적으면 확률 배수가 `10000 → 20000` 으로 뜹니다.
 *
 * 값을 가지지 않는 규칙도 있습니다 — 덱을 다시 뽑거나 그림 카드를 빼는 것들이고, 그때는
 * 이름 한 줄이 전부입니다.
 */
export function ruleChange(event: { before: number | null; after: number | null;
                             flag: boolean; rule: string }): string {
  if (event.after === null) return t('ui.note.applies_all_run')
  if (event.flag) return event.after !== 0 ? t('ui.note.turned_on') : t('ui.note.turned_off')

  if (RULE_IS_SCALE.has(event.rule) || RULE_IS_MULTIPLIER.has(event.rule)) {
    const unit = RULE_IS_SCALE.has(event.rule) ? 10_000 : 1
    const to = (event.after / unit).toFixed(2)
    if (event.before === null || event.before === event.after) return `×${to}`
    return `×${(event.before / unit).toFixed(2)}  →  ×${to}`
  }

  // 할인은 만분율이고 **낮을수록 좋습니다.** 배수로 적으면 그 방향이 뒤집혀 읽힙니다.
  if (event.rule === 'shopDiscount') {
    return tf('ui.rule.discount', { n: (event.after / 100).toFixed(0) })
  }

  if (event.before === null || event.before === event.after) return String(event.after)
  const delta = event.after - event.before
  return `${event.before}  →  ${event.after}   (${delta > 0 ? '+' : ''}${delta})`
}

/**
 * 규칙 값 하나를 읽을 수 있게.
 *
 * **단위는 규칙의 성질이고 읽는 법은 화면의 몫입니다** — `ruleChange` 와 같은 표를 씁니다.
 */
export function ruleValue(rule: string, value: number): string {
  if (RULE_IS_SCALE.has(rule)) return `×${(value / 10_000).toFixed(2)}`
  if (RULE_IS_MULTIPLIER.has(rule)) return `×${value.toFixed(2)}`
  if (rule === 'shopDiscount') return `${(value / 100).toFixed(0)}%`
  return String(value)
}

/**
 * 만분율로 적힌 규칙들.
 *
 * 값의 단위는 규칙의 성질이지만 **읽는 법은 화면의 몫이라** 여기 있습니다 —
 * `moneyReason` · `valueText` 와 같은 자리입니다.
 *
 * **`Rules` 의 필드 이름입니다.** `RuleKind` 의 이름(`BlindSizeScale`)이 아닙니다 —
 * 「적용 중」 목록이 `Rules` 를 훑으며 이 표를 보므로, 한쪽 이름을 적으면 아무것도 걸리지
 * 않고 배수가 날값으로 적힙니다. `ruleName` 의 열쇠도 같은 이름에서 나옵니다.
 */
export const RULE_IS_SCALE = new Set([
  'blindSizeScaleBp', 'planetGivesMultBp',
])

/**
 * 백분율로 적힌 규칙들.
 *
 * 나머지 배수들은 만분율이 아니라 그냥 곱하는 수입니다 — `probabilityScale` 의 기본값이
 * 1이고 2가 되면 두 배라는 뜻입니다. 단위를 지레짐작하면 `1 → 2` 가 `×0.00` 으로 뜹니다.
 */
export const RULE_IS_MULTIPLIER = new Set([
  'shopWeightTarotScale', 'shopWeightPlanetScale', 'probabilityScale', 'editionWeightScale',
])

/**
 * 효과 하나가 낸 값을 글로.
 *
 * **연산마다 읽는 법이 다릅니다** — 곱은 `×`, 가산은 `+`, 돈은 `$` 입니다. 한 자리에서
 * 정하지 않으면 카드와 조커와 런이 각자 다르게 적게 됩니다.
 */
/** `AllCardsScore` 를 `all_cards_score` 로. 글 표의 식별자가 그 모양입니다. */
export function snake(name: string): string {
  return name.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase()
}

export function moneyReason(reason: string): string {
  switch (reason) {
    case 'blind': return t('ui.label.reward')
    case 'interest': return t('ui.money.interest')
    case 'hands_left': return t('ui.label.hands_left')
    case 'discards_left': return t('ui.label.discards_left')
    // **파는 것과 사는 것도 적습니다.** 이 둘이 비어 있어서 금액만 조용히 바뀌었고,
    // 바꿀 때는 들어온 것과 나간 것이 한 프레임 안에 섞여 「알아서 들어갔네」가 되었습니다.
    case 'sell': return t('ui.money.sell')
    case 'shop': return t('ui.money.spent')
    case 'rental': return t('ui.money.rental')
    // **갈 곳 없는 지출도 적습니다.** 리롤과 버리기 비용은 이유가 없어 금액만 조용히
    // 줄었습니다 — 단추의 말이 곧 그 이유이므로 그 말을 그대로 씁니다.
    case 'reroll': return t('ui.button.reroll')
    case 'discard': return t('ui.button.discard')
    default: return ''
  }
}

export function blindName(blind: BlindKind): string {
  return blind === BlindKind.Small ? t('ui.blind.small') : blind === BlindKind.Big
    ? t('ui.blind.big') : t('ui.blind.boss')
}
