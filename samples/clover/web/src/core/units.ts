// 단위와 셈법.
//
// **배수는 만분율 정수입니다.** `×1.5` 는 `15000` 이고 `+0.25` 는 `2500` 입니다. 부동소수를
// 쓰지 않는 이유는 하나입니다 — `×0.1` 을 30번 곱하는 조커가 있고, 그 누적 오차가 언어와
// 최적화 수준에 따라 달라집니다. 만분율 정수는 어디서도 같습니다.
//
// 규격은 `doc/effect-vm.md` 의 「결정론」입니다.

/** 배수의 단위. 이 값이 ×1 입니다. */
export const MULT_SCALE = 10_000

/** 곱 누적값의 시작값. */
export const MULT_ONE = MULT_SCALE

/**
 * 만분율끼리의 곱.
 *
 * **내림은 음의 무한 방향입니다.** 음수 배수가 생기는 조커는 없지만, 규격에 방향이 없으면
 * 두 언어가 다른 답을 낼 자리가 남습니다.
 */
export function mulBp(a: number, b: number): number {
  return Math.floor((a * b) / MULT_SCALE)
}

/** 만분율을 정수 배수로 적용합니다. 점수와 칩이 이것을 지납니다. */
export function applyBp(value: number, bp: number): number {
  return Math.floor((value * bp) / MULT_SCALE)
}

/** 사람이 읽는 배수. 표시에만 씁니다 — 셈에는 쓰지 않습니다. */
export function bpToText(bp: number): string {
  const whole = Math.floor(bp / MULT_SCALE)
  const frac = bp % MULT_SCALE
  if (frac === 0) return String(whole)
  return (bp / MULT_SCALE).toFixed(2).replace(/0+$/, '').replace(/\.$/, '')
}

/** 판매가. 구입가의 절반을 내리고 최소 1 입니다. */
export function sellValue(cost: number, divisor: number, min: number): number {
  return Math.max(min, Math.floor(cost / divisor))
}
