// 득점.
//
// **순서가 규칙입니다.** `doc/effect-vm.md` 의 「평가 순서」가 규격이고 이 파일이 그
// 구현입니다. 순서가 바뀌면 같은 패에서 다른 점수가 나오므로, 여기의 줄 순서가 곧 규격의
// 줄 순서입니다.

import { EditionKind } from '../generated/enums/edition-kind'
import { PokerHandKind } from '../generated/enums/poker-hand-kind'
import { Trigger } from '../generated/enums/trigger'
import { EnhancementKind } from '../generated/enums/enhancement-kind'
import { evaluate } from './hand'
import { mulBp, MULT_ONE } from './units'
import type { CardInstance, JokerInstance } from './state'
import { collect, rowsForJoker, runCardEffects, runRow, runTrigger, type Vm } from './vm'

export interface ScoreResult {
  hand: PokerHandKind
  level: number
  chips: number
  mult: number
  score: number
}

/** 무력화된 카드는 칩값과 강화와 인장과 에디션을 전부 잃습니다. 족보에는 그대로 참여합니다. */
function isSilenced(card: CardInstance): boolean {
  return card.debuffed
}

function rankChips(vm: Vm, card: CardInstance): number {
  if (isSilenced(card)) return 0
  if (card.enhancement === EnhancementKind.Stone) return 0
  return (vm.data.tables.rank.findByRank(card.rank)?.chips ?? 0) + card.bonusChips
}

/**
 * 에디션이 카드에 주는 것. 조커에도 같은 값이 붙습니다.
 *
 * **에디션도 이름을 남깁니다.** 값만 조용히 들어가면 화면에는 「어디선가 배수가 올랐다」만
 * 남고, 그 카드에 왜 무늬가 도는지가 설명되지 않습니다.
 */
function applyEdition(vm: Vm, edition: EditionKind, tell: (chips: number, mult: number,
                                                          op: string) => void): void {
  const row = vm.data.tables.edition.findByEdition(edition)
  if (!row || !vm.scoring) return

  if (row.chips !== 0) {
    vm.scoring.chips += row.chips
    tell(row.chips, 0, 'AddChips')
    emitTotals(vm)
  }
  if (row.multAdd !== 0) {
    vm.scoring.mult += row.multAdd
    tell(0, row.multAdd, 'AddMult')
    emitTotals(vm)
  }
  if (row.multMul !== MULT_ONE) {
    vm.scoring.mult = mulBp(vm.scoring.mult, row.multMul)
    tell(0, row.multMul, 'MulMult')
    emitTotals(vm)
  }
}

/** 카드의 에디션. 그 카드 위에 뜹니다. */
function applyCardEdition(vm: Vm, card: CardInstance): void {
  applyEdition(vm, card.edition, (chips, mult, op) => {
    vm.events.push({
      t: 'CardScored', uid: card.uid, op, chips, mult, money: 0, source: 'edition',
    })
  })
}

/** 조커의 에디션. 그 조커 위에 뜹니다. */
function applyJokerEdition(vm: Vm, joker: JokerInstance, slot: number): void {
  applyEdition(vm, joker.edition, (chips, mult, op) => {
    vm.events.push({
      t: 'JokerTriggered', slot, jokerId: joker.jokerId, op, chips, mult, money: 0,
    })
  })
}

/**
 * 조커가 누적한 값. **득점할 때 자동으로 들어갑니다** — 조커마다 더하는 효과 행을 두면
 * 같은 것을 150번 적게 됩니다.
 *
 * **자동으로 들어가는 것과 조용히 들어가는 것은 다릅니다.** 늘어나는 조커는 누적값이 그
 * 조커의 전부이고, 그것이 이벤트를 내지 않는 동안 화면에서는 아무 일도 하지 않는 조커였습니다.
 */
function applyCounters(vm: Vm, joker: JokerInstance, slot: number): void {
  const scoring = vm.scoring
  if (!scoring) return
  const { chips, multAdd, multMul } = joker.counters

  const tell = (op: string, addChips: number, addMult: number) => {
    vm.events.push({
      t: 'JokerTriggered', slot, jokerId: joker.jokerId, op,
      chips: addChips, mult: addMult, money: 0,
    })
    emitTotals(vm)
  }

  if (chips !== 0) {
    scoring.chips += chips
    tell('AddChips', chips, 0)
  }
  if (multAdd !== 0) {
    scoring.mult += multAdd
    tell('AddMult', 0, multAdd)
  }
  if (multMul !== MULT_ONE) {
    scoring.mult = mulBp(scoring.mult, multMul)
    tell('MulMult', 0, multMul)
  }
}

/** 지금의 칩과 배수. 값을 바꾼 것 바로 뒤에 붙습니다. */
function emitTotals(vm: Vm): void {
  if (!vm.scoring) return
  vm.events.push({ t: 'ChipsMultChanged', chips: vm.scoring.chips, mult: vm.scoring.mult })
}

/** 카드 한 장을 한 번 처리합니다. 재발동이면 이것이 다시 불립니다. */
function scoreCardOnce(vm: Vm, card: CardInstance): void {
  const scoring = vm.scoring
  if (!scoring) return

  const chips = rankChips(vm, card)
  if (chips !== 0) {
    scoring.chips += chips
    vm.events.push({
      t: 'CardScored', uid: card.uid, op: 'AddChips',
      chips, mult: 0, money: 0, source: 'rank',
    })
    emitTotals(vm)
  }

  if (!isSilenced(card)) {
    runCardEffects(vm, Trigger.OnCardScored, card)
    applyCardEdition(vm, card)
  }

  // 조커를 매번 다시 모으는 것은 낭비가 아니라 규격입니다 — 효과가 조커를 파괴할 수 있고,
  // 그러면 다음 카드에서 줄이 달라져 있어야 합니다.
  for (const [row, host] of collect(vm, Trigger.OnCardScored)) runRow(vm, row, host)
}

/**
 * 핸드 하나의 점수.
 *
 * 여기서 상태를 바꾸는 것은 효과들이고, 이 함수는 순서만 정합니다.
 */
export function scoreHand(vm: Vm, played: CardInstance[]): ScoreResult {
  const state = vm.state
  const rules = state.rules

  // 1. 족보를 판정합니다. 판정 규칙을 바꾸는 조커들은 이미 `rules` 에 반영되어 있습니다.
  const { hand, scoring: scoringCards } = evaluate(played, rules)
  const handName = PokerHandKind[hand]
  const level = state.handLevels[handName] ?? 1
  const row = vm.data.tables.pokerHand.getByHandOrThrow(hand)

  // 2. 레벨로 기본값을 냅니다. `The Flint` 가 여기서 절반으로 만듭니다.
  let chips = row.baseChips + row.chipsPerLevel * (level - 1)
  let mult = (row.baseMult + row.multPerLevel * (level - 1)) * MULT_ONE
  if (rules.halveBaseChipsAndMult) {
    chips = Math.floor(chips / 2)
    mult = Math.floor(mult / 2)
  }

  vm.scoring = {
    hand, level, chips, mult,
    played, scoringCards,
    matched: new Set(),
    depth: 0,
  }

  vm.events.push({
    t: 'HandEvaluated', hand, level, chips, mult,
    cards: scoringCards.map(card => card.uid),
  })

  // 3. 득점 카드를 **왼쪽에서 오른쪽으로.**
  for (const card of scoringCards) {
    vm.scoring.card = card
    vm.pendingRetrigger = 0
    scoreCardOnce(vm, card)

    // 재발동은 **그 자리에서 즉시**입니다. 카드를 다 처리한 뒤에 하면 누적형 조커의 값이
    // 달라집니다.
    //
    // **재발동 중에는 재발동을 받지 않습니다.** `Red Seal` 이 그 카드를 한 번 더 발동하는데,
    // 그 한 번에서 인장이 또 요청하면 자기 자신을 끝없이 부릅니다. 깊이 상한은 그 위의
    // 안전장치이지 규칙이 아닙니다.
    const repeats = Math.min(vm.pendingRetrigger ?? 0, vm.data.score.maxRetriggerDepth)
    vm.retriggering = true
    for (let again = 0; again < repeats; again++) scoreCardOnce(vm, card)
    vm.retriggering = false
    vm.pendingRetrigger = 0
  }

  // 4. 패에 든 카드를 왼쪽에서 오른쪽으로.
  for (const uid of state.hand) {
    const card = state.deck.find(entry => entry.uid === uid)
    if (!card || isSilenced(card)) continue
    vm.scoring.card = card
    runCardEffects(vm, Trigger.OnCardHeld, card)
    runTrigger(vm, Trigger.OnCardHeld)
  }

  // 5. 패 전체에 반응하는 것들. 덱과 바우처와 보스가 먼저이고, 그다음 조커입니다.
  vm.scoring.card = undefined
  for (const [effect, host] of collect(vm, Trigger.OnHandPlayed)) {
    if (host.kind === 'joker') continue
    runRow(vm, effect, host)
  }

  // 조커는 **자리 순서로** 돕니다. 누적값과 에디션이 그 조커의 자리에서 함께 들어갑니다 —
  // 따로 더하는 효과 행을 두지 않는 이유가 이것입니다.
  for (let slot = 0; slot < state.jokers.length; slot++) {
    const joker = state.jokers[slot]
    if (joker.disabled) continue

    applyCounters(vm, joker, slot)
    for (const effect of rowsForJoker(vm, joker, slot)) {
      if (effect.trigger === Trigger.OnHandPlayed) {
        runRow(vm, effect, { kind: 'joker', joker, slot })
      }
    }
    applyJokerEdition(vm, joker, slot)
  }

  // 6. 곱합니다. 여기서 한 번뿐입니다.
  let finalChips = vm.scoring.chips
  let finalMult = vm.scoring.mult

  // `plasma_deck` 은 칩과 배수를 평균으로 맞춥니다. **내림 방향이 규격입니다.**
  if (rules.balanceChipsAndMult) {
    const average = Math.floor((finalChips + finalMult / MULT_ONE) / 2)
    finalChips = average
    finalMult = average * MULT_ONE
  }

  // **칩이 보유액을 넘지 못합니다.** 배수가 아니라 칩입니다 — 곱하기 직전에 자릅니다.
  if (rules.chipsCappedByMoney) finalChips = Math.min(finalChips, Math.max(0, state.money))

  const score = Math.floor((finalChips * finalMult) / MULT_ONE)

  vm.events.push({ t: 'ScoreResolved', score, target: state.target })
  return { hand, level, chips: finalChips, mult: finalMult, score }
}
