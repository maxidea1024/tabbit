// 효과를 실제로 도는 곳.
//
// **순서가 규칙입니다.** 조커는 슬롯 왼쪽에서 오른쪽으로 돌고, 조건 앞에 확률이 있고,
// 「첫 대상만」은 그 트리거 안에서 한 번입니다. 순서가 바뀌면 같은 패에서 다른 점수가
// 나옵니다.

import { Trigger } from '../../generated/enums/trigger'
import type { EffectRow } from '../data'
import type { JokerInstance } from '../state'
import { holds } from './conditions'
import { apply } from './operations'
import { effectKey, RUN_HOST, type EffectHost, type Vm } from './context'

export * from './context'
export { changeRule, sellPrice } from './operations'
export { newVm } from './context'

/** 조커 하나가 이번에 쓸 효과 행. 복사 조커는 남의 것을 씁니다. */
export function rowsForJoker(vm: Vm, joker: JokerInstance, slot: number): readonly EffectRow[] {
  const own = vm.data.jokerEffects.get(joker.jokerId) ?? []

  // 복사는 **한 단계입니다.** 복사가 복사를 가리키면 원작도 거기서 끊습니다.
  const copy = own.find(row => row.operation.kind === 'OpCopyJoker')
  if (!copy) return own

  // `apply` 가 `copyTarget` 에 남깁니다. 읽고 바로 비웁니다 — 다음 조커가 남의 것을 보면
  // 안 됩니다.
  vm.copyTarget = undefined
  apply(vm, copy, { kind: 'joker', joker, slot })
  const target = takeCopyTarget(vm)

  if (!target || target === joker) return []
  const borrowed = vm.data.jokerEffects.get(target.jokerId) ?? []
  return borrowed.filter(row => row.operation.kind !== 'OpCopyJoker')
}

/** 복사 조커가 가리킨 것을 꺼내고 비웁니다. */
function takeCopyTarget(vm: Vm): JokerInstance | undefined {
  const found = vm.copyTarget
  vm.copyTarget = undefined
  return found
}

/** 그 트리거에 반응하는 효과들을, 도는 순서 그대로. */
export function collect(vm: Vm, trigger: Trigger): Array<[EffectRow, EffectHost]> {
  const out: Array<[EffectRow, EffectHost]> = []
  const state = vm.state

  // 1. 덱과 바우처. 런의 규칙을 정하는 것들이 먼저입니다.
  for (const row of vm.data.deckEffects.get(state.deckId) ?? []) {
    if (row.trigger === trigger) out.push([row, RUN_HOST])
  }
  for (const voucher of state.vouchers) {
    for (const row of vm.data.voucherEffects.get(voucher) ?? []) {
      if (row.trigger === trigger) out.push([row, RUN_HOST])
    }
  }

  // 2. 보스. 규칙을 바꾸므로 조커보다 앞입니다.
  //
  // **판을 두는 동안에만입니다.** 블라인드만 보면 라운드가 끝나고 상점에 있는 동안에도
  // 보스가 규칙에 남습니다 — 다음 블라인드를 고를 때까지 `state.blind` 는 그대로이기
  // 때문입니다.
  if (!state.bossDisabled && state.blind === 3 && state.phase === 'round') {
    for (const row of vm.data.bossEffects.get(state.bossId) ?? []) {
      if (row.trigger === trigger) out.push([row, RUN_HOST])
    }
  }

  // 3. 들고 있는 태그. **가진 것이므로 매번 함께 봅니다** — 뽑을 때 한 번 돌리고 마는 것이
  // 아닙니다. 대부분은 상점에 들어갈 때나 다음 라운드에 뜻을 가집니다.
  for (const tag of state.tagsPending) {
    for (const row of vm.data.tagEffects.get(tag) ?? []) {
      if (row.trigger === trigger) out.push([row, RUN_HOST])
    }
  }

  // 4. 조커. **왼쪽에서 오른쪽으로.**
  for (let slot = 0; slot < state.jokers.length; slot++) {
    const joker = state.jokers[slot]
    if (joker.disabled) continue
    for (const row of rowsForJoker(vm, joker, slot)) {
      if (row.trigger === trigger) out.push([row, { kind: 'joker', joker, slot }])
    }
  }

  return out
}

/**
 * 효과 행 하나를 봅니다.
 *
 * 조건이 성립해도 확률에 걸리면 아무 일도 일어나지 않고, **그 사실이 이벤트로 나갑니다** —
 * 보여주지 않으면 플레이어가 그 조커가 무엇을 하는지 배우지 못합니다.
 */
export function runRow(vm: Vm, row: EffectRow, host: EffectHost): void {
  if (host.joker?.disabled) return

  const key = effectKey(row, host)
  if (row.firstOnly && vm.scoring?.matched.has(key)) return

  if (!holds(vm, row, host)) return
  if (row.firstOnly) vm.scoring?.matched.add(key)

  if (row.chanceNum !== null && row.chanceDen !== null) {
    const scale = vm.state.rules.probabilityScale
    if (!vm.state.rng.JokerProc.chance(row.chanceNum, row.chanceDen, scale)) {
      if (host.joker) {
        vm.events.push({
          t: 'JokerFizzled',
          slot: host.slot ?? 0,
          jokerId: host.joker.jokerId,
          num: row.chanceNum * scale,
          den: row.chanceDen,
        })
      }
      return
    }
  }

  apply(vm, row, host)
}

/** 그 트리거를 전부 돕니다. */
export function runTrigger(vm: Vm, trigger: Trigger): void {
  for (const [row, host] of collect(vm, trigger)) runRow(vm, row, host)
}

/** 카드 하나에 붙은 강화와 인장의 효과. */
export function runCardEffects(vm: Vm, trigger: Trigger, card: { enhancement: number; seal: number }): void {
  const host: EffectHost = { kind: 'card', card: card as never }

  for (const row of vm.data.enhancementEffects.get(String(card.enhancement)) ?? []) {
    if (row.trigger === trigger) runRow(vm, row, host)
  }
  for (const row of vm.data.sealEffects.get(String(card.seal)) ?? []) {
    if (row.trigger === trigger) runRow(vm, row, host)
  }
}

export { Trigger }
