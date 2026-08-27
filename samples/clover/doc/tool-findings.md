# 도구 보고

> [clover 문서 목록으로](readme.md)

---

데이터를 처음 끝까지 변환하면서 찾은 것입니다. **이 샘플의 값이 여기 있습니다** — 되게
만드는 것이 아니라, 실전 크기의 데이터가 도구의 어디에 닿는지를 보는 것입니다.

|  |수|
|--|--|
|찾은 것|**3**|
|닫힌 것|0|
|우회한 것|**3**|

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

---

EOD
