// 상태의 해시.
//
// **대조의 도구입니다.** 액션 하나마다 해시를 내면, 두 구현이 갈라진 지점을 이분해서
// 찾습니다. 그래서 이 함수는 「무엇을 담는가」가 규격입니다 — 담기지 않은 것이 달라도
// 해시는 같습니다.
//
// 담지 않는 것이 둘 있습니다. 이벤트는 연출의 몫이므로 담지 않고, 난수의 내부 상태는
// 담습니다 — 그것이 다르면 다음 액션에서 갈라지기 때문입니다.

import type { RunState } from './state'

/** FNV-1a 32비트. 언어의 문자열 해시를 쓰지 않는 이유는 `rng.ts` 와 같습니다. */
export function fnv1a32(text: string): number {
  let hash = 0x811c9dc5
  for (let i = 0; i < text.length; i++) {
    hash ^= text.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return hash >>> 0
}

/**
 * 해시에 들어가는 것들을 한 줄로 적습니다.
 *
 * **순서가 고정입니다.** 필드를 더할 때는 끝에 붙입니다 — 가운데에 끼우면 예전 리플레이의
 * 해시가 전부 달라지고, 그러면 무엇이 바뀌었는지 알 수 없게 됩니다.
 */
export function canonical(state: RunState): string {
  const parts: string[] = []

  parts.push(state.phase, String(state.ante), String(state.blind))
  parts.push(String(state.money), String(state.score), String(state.target))
  parts.push(String(state.handsLeft), String(state.discardsLeft))
  parts.push(state.bossId)

  parts.push(state.deck.map(card =>
    `${card.uid}.${card.rank}.${card.suit}.${card.enhancement}.${card.seal}` +
    `.${card.edition}.${card.bonusChips}${card.debuffed ? 'd' : ''}`).join(','))

  parts.push(state.hand.join(','))
  parts.push(state.drawPile.join(','))

  parts.push(state.jokers.map(joker => {
    const c = joker.counters
    return `${joker.uid}.${joker.jokerId}.${joker.edition}.${joker.sticker}` +
      `.${c.chips}.${c.multAdd}.${c.multMul}.${c.money}.${c.sellValue}.${c.charge}.${c.tick}`
  }).join(','))

  parts.push(state.consumables.map(item => `${item.id}.${item.edition}`).join(','))
  parts.push(state.vouchers.join(','))
  parts.push(state.tagsPending.join(','))

  parts.push(Object.keys(state.handLevels).sort()
    .map(name => `${name}=${state.handLevels[name]}`).join(','))
  parts.push(Object.keys(state.handPlayCounts).sort()
    .map(name => `${name}=${state.handPlayCounts[name]}`).join(','))

  parts.push(Object.keys(state.rng).sort()
    .map(name => `${name}=${state.rng[name].save().join('/')}`).join(','))

  // **뜯어 놓은 팩.** 끝에 붙입니다 — 가운데에 끼우면 예전 리플레이의 해시가 전부
  // 달라집니다. 펼쳐진 것이 무엇인지까지 담아야 두 구현이 여기서 갈라진 것을 이 자리에서
  // 알 수 있습니다.
  parts.push(state.pack
    ? `${state.pack.packId}:${state.pack.picksLeft}:` + state.pack.options
        .map((item, at) => `${item.kind}.${item.id}.${item.enhancement ?? 0}` +
          `.${item.seal ?? 0}.${item.edition}${state.pack!.taken[at] ? 'x' : ''}`).join(',')
    : '')

  // **이번 안테의 태그 둘.** 끝에 붙입니다 — 가운데에 끼우면 예전 리플레이의 해시가 전부
  // 달라집니다.
  parts.push(state.tagOffer.join(','))

  return parts.join('|')
}

/** 상태 하나를 8자리 16진수로. */
export function snapshotHash(state: RunState): string {
  return fnv1a32(canonical(state)).toString(16).padStart(8, '0')
}
