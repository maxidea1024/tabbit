# 도구 보고

> [clover 문서 목록으로](readme.md)

---

데이터를 처음 끝까지 변환하면서 찾은 것입니다. **이 샘플의 값이 여기 있습니다** — 되게
만드는 것이 아니라, 실전 크기의 데이터가 도구의 어디에 닿는지를 보는 것입니다.

|  |수|
|--|--|
|찾은 것|**6**|
|닫힌 것|0|
|우회한 것|**6**|

앞의 셋은 [데이터 저작](progress.md#p2--데이터-저작-끝)에서, 뒤의 셋은
[코어](progress.md#p3--코어-typescript-끝)에서 나왔습니다. **뒤의 셋은 생성 코드를 실제로
읽고 돌려 보아야 나오는 것들입니다** — 변환은 셋 다 성공으로 끝납니다.

## 1. 배열인 변종 멤버에서의 C# 생성 예외

**결함입니다.** 좁혀 두었습니다.

다형 변종(`struct X extends Y @n`)의 멤버를 배열로 선언하고 **그 컬럼에 값이 하나라도 들어가면**
C# 생성이 예외를 던집니다.

```
[F] [Tabbit] One or more errors occurred.
             (Unable to cast object of type 'System.Int32[]' to type 'System.Int32'.)
```

|조건|결과|
|--|--|
|변종 멤버가 배열이고 **모든 행이 비어 있음**|**통과합니다**|
|변종 멤버가 배열이고 **한 행이라도 값이 있음**|**예외**|
|배열이 변종 멤버가 아니라 보통 컬럼|통과합니다|

원소 타입은 상관이 없습니다 — `int[]?` 로도 `RankKind[]?` 로도 같습니다. 값이 있는 행이
하나면 충분합니다.

**닿는 자리는 C# 생성입니다.** 쿠킹은 0 error 로 끝나고, 예외는
`Generating codes for CSharp into …tabbit-accessor` 다음에 나옵니다. 검증을 켜 두면 그
액세서 생성에서 먼저 걸리므로, 다른 타깃만 쓰는 프로젝트는 더 늦게 만납니다.

[테스트 픽스처](../../../test/fixtures/schemas/polymorphism/effect.tbs)의 변종에는 배열 멤버가
없습니다. **덮이지 않은 경로입니다.**

### 우회

`ranks` 와 `suits` 를 변종의 멤버가 아니라 **효과 행의 컬럼**으로 올렸습니다. 조건이든
연산이든 「어느 랭크인가」를 묻는 것은 한 행에 하나뿐이므로 한 칸을 나눠 씁니다 —
[생성기](../design-data/tools/seedlib/grid.py)의 `SHARED_FIELDS` 가 그것이고, 둘이 겹치면
거기서 멈춥니다.

**우회가 지워지면 이 항목이 닫힙니다.**

## 2. enum 이 키인 테이블에 대한 `foreign` 의 제약

**설계된 제약이고, 메시지가 그렇게 말합니다.**

```
`Planet.Hand` references `PokerHand`, whose index is an enum. Every other key type can be
referenced; this one cannot yet, because an enum travels in an encoding of its own and the
generated readers have no call for it here.
```

세 자리에서 만났습니다 — `Planet.hand → PokerHand`, `EnhancementEffect.owner → Enhancement`,
`SealEffect.owner → Seal` 입니다.

**목록이 `.tbs` 에 있고 데이터가 시트에 있는 구조에서 자연스럽게 나옵니다.** 효과가 `Suit` 를
가리켜야 하므로 `Suit` 는 `.tbs` 의 enum 이어야 하고, 무늬마다의 표시 이름은 테이블에 있어야
하므로 그 테이블의 키가 enum 이 됩니다. 그러면 그 테이블을 `foreign` 으로 가리킬 수 없습니다.

### 우회

메시지가 적어 둔 둘째 방법을 썼습니다 — 값을 enum 으로 들고 있고, 읽는 쪽이 그 테이블의
자기 인덱스로 행을 찾습니다. 참조 검사가 없어지지만 **enum 이므로 값 자체가 검사됩니다.**

## 3. 변종이 공유하는 컬럼의 타입과 제약

**설계의 결과이고, 메시지가 그렇게 말합니다.** 다만 **처음 만나는 자리가 늦습니다.**

```
Column `Operation.Value` of `JokerEffect` is declared `int?` by `OpPerUnit` and `int` by
`OpMulMoney`, both variants of `Operation`.
```

두 갈래로 만났습니다.

|무엇|무엇이 일어났는가|
|--|--|
|**옵셔널이 다름**|`value` 를 `OpPerUnit` 이 `int` 로, `OpModifyCard` 가 `int?` 로 선언했습니다. **선언 시점에는 오류가 아니고**, 그 계열을 쓰는 테이블이 생길 때 나옵니다|
|**제약이 다름**|`n` 을 `CondCardCount` 가 `int (min=1, max=5)` 로, `CondDiscardsLeft` 가 `int (min=0)` 으로 선언했습니다. **컬럼은 첫 제약 하나를 가지므로**, 데이터의 `0` 과 `23` 이 값 오류로 보고됩니다|

둘째가 특히 늦습니다 — 오류가 `JokerEffect.ConditionN is 23, above the maximum 5` 이고,
그 자리에서 보이는 것은 **데이터가 틀렸다**입니다. 실제로 틀린 것은 선언이고, 두 변종이
같은 이름을 다른 제약으로 쓴 것입니다.

### 우회

공유하는 칸의 제약을 지웠습니다. `n` 은 이제 제약 없는 `int` 이고, 변종마다의 범위는
[검증 규칙](../design-data/validation/rules/global/EffectRules.cs)이 볼 자리로 남습니다.

**변종별 제약을 컬럼이 담을 수 없다는 것 자체는 옳습니다.** 다만 선언을 읽을 때 그 사실이
보이면 데이터를 만들기 전에 알 수 있습니다.

## 4. 다형 변종의 옵셔널 멤버에서 사라지는 「비었음」

시트의 옵셔널 컬럼은 생성 레코드에 `hasChanceNum` 같은 짝을 가집니다. **변종의 멤버에는 그
짝이 없습니다.**

```ts
export interface OpAddMoney extends OperationBase {
  readonly kind: 'OpAddMoney'
  money: number
  /** 상한. 없으면 비웁니다. */
  cap: number          // ← 비어 있던 행도 0 으로 옵니다
}
```

`cap` 이 `int?` 로 선언되어 있고 시트에서 비어 있어도, 읽는 쪽에는 `0` 이 옵니다. **「상한이
없다」와 「상한이 0 이다」가 같은 값입니다.**

대부분의 칸에서는 문제가 되지 않습니다 — enum 의 0 은 `None` 이고, `count` 의 0 은 뜻이
없기 때문입니다. 문제가 되는 것은 **0 이 정당한 값인 칸**이고, 지금 그런 칸이 셋입니다.

|칸|0 이 뜻할 수 있는 것|우리가 정한 것|
|--|--|--|
|`cap`|상한 없음 · 상한 0|**0 은 상한 없음.** 0 으로 막는 효과가 없습니다|
|`floor`|하한 없음 · 하한 0|**감소하는 효과에서만 봅니다.** 늘기만 하는 것은 하한을 보지 않습니다|
|`baseValue`|기본값 없음 · 기본값 0|규격이 이미 「비우면 0」이므로 같습니다|

### 우회

위의 표가 우회입니다 — **0 을 무엇으로 읽을지를 정하고 코드와 문서에 적었습니다.**
`operations.ts` 의 `op.cap || null` 이 그것입니다.

**닫히는 방법은 변종에도 `hasX` 를 내는 것**입니다. 그러면 이 표가 없어집니다.

## 5. `strict` 에서 컴파일되지 않는 `typescript` 산출물

**결함입니다.** wildling 이 C# 에서 찾은 것과 같은 자리입니다 — 타깃이 파일을 쓰기만 하고
컴파일하지 않으므로, 변환은 성공으로 끝나고 코드는 컴파일되지 않습니다.

`strict: true` 로 검사하면 열 곳이 걸립니다. 두 갈래입니다.

```
src/generated/tables/joker-effect.ts(687,10): error TS2564:
  Property '_owner' has no initializer and is not definitely assigned in the constructor.

src/generated/tables/voucher.ts(241,15): error TS2322:
  Type 'undefined' is not assignable to type 'VoucherRecord'.
```

|갈래|무엇|
|--|--|
|`foreign` 필드의 초기화|`_owner` 가 선언만 되고 대입이 없습니다. `strictPropertyInitialization` 이 잡습니다|
|**옵셔널 `foreign` 의 타입**|`foreign Voucher?` 가 `_upgradesFrom: VoucherRecord` 로 선언되고, 값이 없는 행에서 `undefined` 가 대입됩니다. **타입이 사실과 다릅니다** — `strict` 를 꺼도 이 자리는 남습니다|

둘째가 더 무겁습니다. 컴파일이 통과하더라도 **읽는 쪽이 `undefined` 를 받을 수 있는 자리를
타입이 가려 줍니다.**

### 우회

프로젝트를 둘로 나눴습니다. `src/generated/tsconfig.json` 이 생성 코드만 `strict: false` 로
검사하고, 우리 코드는 `strict: true` 로 남습니다. `tsc -b` 가 둘을 함께 봅니다.

**우회가 지워지면**(그 `tsconfig.json` 이 없어지면) 이 항목이 닫힙니다.

## 6. ESM 에서 돌지 않는 `readAllBytes`

**결함입니다.** 생성된 `tcb-reader.ts` 가 파일을 이렇게 엽니다.

```ts
declare function require(moduleName: string): any

export function readAllBytes(filename: string): Uint8Array {
  const fs = require('fs')
  return new Uint8Array(fs.readFileSync(filename))
}
```

주석이 적어 둔 의도는 **「브라우저에서도 모듈이 로드되도록 지연 해석한다」입니다.** 그 의도는
맞습니다. 다만 `require` 는 CommonJS 의 것이고 **ESM 모듈에는 존재하지 않습니다.**

```
ReferenceError: require is not defined in ES module scope
    at Module.readAllBytes (src/generated/tabbit/tcb-reader.ts:1490:14)
    at RankTable.readBinarySync (src/generated/tables/rank.ts:159:14)
    at CloverData.readAllBinarySync (src/generated/clover-data.ts:416:10)
```

`package.json` 에 `"type": "module"` 을 적은 프로젝트에서는 `readBinarySync` 와
`readAllBinarySync` 가 **전부** 이 자리에서 멈춥니다. 요즘 만드는 TypeScript 프로젝트의
기본값이 그것입니다.

### 우회

Node 전용 로더(`src/core/load-node.ts`)에서 `require` 를 하나 놓아 줍니다.

```ts
import { createRequire } from 'module'
const shim = globalThis as unknown as { require?: unknown }
if (typeof shim.require === 'undefined') shim.require = createRequire(import.meta.url)
```

**브라우저 쪽과 파일이 갈린 이유가 이것입니다** — 이 한 줄을 공용 로더에 두면 브라우저
번들이 `module` 을 끌어옵니다. 결함이 닫히면 파일 둘이 하나가 됩니다.

**닫는 방법**은 `import { readFileSync } from 'node:fs'` 를 쓰거나, 파일에서 읽는 것을
생성 코드에서 빼고 호출자가 바이트를 넘기게 하는 것입니다. 후자는 이미 `readBinaryFrom`
으로 있습니다.

---

EOD
