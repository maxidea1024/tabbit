// 난수.
//
// **언어 표준 난수를 쓰지 않습니다.** 구현이 다르면 리플레이가 갈라지고, 그러면 이 샘플이
// 답하려는 질문에 답할 수 없습니다. PCG32 를 양쪽에 같은 수식으로 구현합니다.
//
// 스트림을 나누는 이유도 같습니다 — 상점 추첨과 카드 섞기와 조커의 확률 발동이 한 스트림을
// 쓰면, 상점에서 리롤 한 번이 득점의 확률을 바꿉니다. 스트림 목록은 `RngStream` 테이블에
// 있습니다.
//
// `BigInt` 를 쓰는 것은 64비트 상태를 정확히 다루기 위해서입니다. C# 의 `ulong` 과 같은
// 값이 나와야 하고, `number` 는 53비트까지만 정확합니다.

const MASK64 = (1n << 64n) - 1n
const MASK32 = (1n << 32n) - 1n

// PCG32 의 상수. 원 논문의 `PCG_DEFAULT_MULTIPLIER_64` 입니다.
const MULTIPLIER = 6364136223846793005n

/** 스트림 하나의 난수. 상태 둘이 전부이므로 세이브에 그대로 들어갑니다. */
export class Pcg32 {
  private state: bigint
  private readonly inc: bigint

  constructor(seed: bigint, sequence: bigint) {
    this.inc = ((sequence << 1n) | 1n) & MASK64
    this.state = 0n
    this.next()
    this.state = (this.state + (seed & MASK64)) & MASK64
    this.next()
  }

  /** 32비트 난수 하나. */
  next(): number {
    const old = this.state
    this.state = (old * MULTIPLIER + this.inc) & MASK64
    const xorshifted = (((old >> 18n) ^ old) >> 27n) & MASK32
    const rot = Number((old >> 59n) & 31n)
    const result = ((xorshifted >> BigInt(rot)) | (xorshifted << BigInt((32 - rot) & 31))) & MASK32
    return Number(result)
  }

  /**
   * `0` 이상 `bound` 미만.
   *
   * **나머지 편향을 버립니다.** `next() % bound` 는 `bound` 가 2의 거듭제곱이 아니면 작은
   * 값이 조금 더 자주 나오고, 그 편향이 두 구현에서 같더라도 옳지 않습니다.
   */
  below(bound: number): number {
    if (bound <= 0) throw new RangeError(`난수의 상한이 ${bound} 입니다`)
    const threshold = (0x1_0000_0000 - bound) % bound
    for (;;) {
      const r = this.next()
      if (r >= threshold) return r % bound
    }
  }

  /** 분자와 분모로 적은 확률. `scale` 은 분자에 곱하는 전역 배율입니다. */
  chance(num: number, den: number, scale = 1): boolean {
    return this.below(den) < num * scale
  }

  /** 목록에서 하나. 비어 있으면 `undefined` 입니다. */
  pick<T>(items: readonly T[]): T | undefined {
    if (items.length === 0) return undefined
    return items[this.below(items.length)]
  }

  /** 가중치 추첨. 가중치의 합이 0 이면 `undefined` 입니다. */
  pickWeighted<T>(items: readonly T[], weight: (item: T) => number): T | undefined {
    let total = 0
    for (const item of items) total += Math.max(0, weight(item))
    if (total <= 0) return undefined

    let roll = this.below(total)
    for (const item of items) {
      roll -= Math.max(0, weight(item))
      if (roll < 0) return item
    }
    return items[items.length - 1]
  }

  /**
   * 제자리 섞기. **아래에서 위로 도는 Fisher-Yates 입니다** — 방향이 바뀌면 같은 난수에서
   * 다른 순서가 나옵니다.
   */
  shuffle<T>(items: T[]): void {
    for (let i = items.length - 1; i > 0; i--) {
      const j = this.below(i + 1)
      const tmp = items[i]
      items[i] = items[j]
      items[j] = tmp
    }
  }

  /** 세이브에 넣는 상태. */
  save(): [string, string] {
    return [this.state.toString(16), this.inc.toString(16)]
  }

  /** 세이브에서 되돌립니다. */
  static restore(saved: [string, string]): Pcg32 {
    const rng = Object.create(Pcg32.prototype) as Pcg32
    ;(rng as unknown as { state: bigint }).state = BigInt('0x' + saved[0])
    ;(rng as unknown as { inc: bigint }).inc = BigInt('0x' + saved[1])
    return rng
  }
}

/**
 * 시드 문자열과 스트림 이름에서 64비트를 만듭니다. FNV-1a 입니다.
 *
 * **언어의 문자열 해시를 쓰지 않습니다.** `String.hashCode` 는 언어마다 다르고, 그 하나가
 * 리플레이를 갈라놓습니다.
 */
export function fnv1a64(text: string): bigint {
  let hash = 0xcbf29ce484222325n
  const prime = 0x100000001b3n
  const bytes = new TextEncoder().encode(text)
  for (const byte of bytes) {
    hash = (hash ^ BigInt(byte)) & MASK64
    hash = (hash * prime) & MASK64
  }
  return hash
}

/** 시드 하나에서 스트림마다 다른 난수를 파생합니다. */
export function streamRng(seed: string, stream: string): Pcg32 {
  return new Pcg32(fnv1a64(`${seed}:${stream}`), fnv1a64(`${stream}:${seed}`))
}
