# 돌리기와 사례

> [「정적 검증」으로 돌아가기](../validation.md)

---

## 12. 실행

|옵션|무엇|
|--|--|
|`--validate-only`|검증까지만. 산출물을 만들지 않습니다. PR 검사용|
|`--skip-runtime-validation`|`rules/runtime/`만 건너뜁니다. 건너뛴 규칙 수가 기록에 남습니다|
|`--list-validators`|규칙을 실행 순서대로 출력하고 종료. 시트를 읽지 않습니다|

검증을 끄는 옵션은 없습니다.

recipe에서 `Path`를 비우는 것이 유일한 방법이고, 그것은 diff에 남습니다.

### 우선순위 — 티어와 차단점

기반 규칙이 실패했는데 그것을 전제하는 규칙 수십 개가 파생 오류를 쏟아내는 상황을 방지합니다.

클래스에 `[RulePriority(n)]`를 답니다.

```csharp
[RulePriority(-10)]
internal static class ReferenceIntegrityRules
{
    public static void Validate(IGlobalContext context) { ... }
}
```

|성질|내용|
|--|--|
|**순서가 아니라 차단점입니다**|규칙 파일은 원래 이름 순으로 모이므로 순서는 이미 정해져 있었습니다. 없던 것은 「앞의 것이 실패하면 뒤를 실행하지 않는다」입니다|
|같은 값이 한 티어|티어 안의 순서는 지금까지와 같이 파일 이름 순입니다. 작은 값이 먼저입니다|
|어트리뷰트가 없으면 기본 티어(0)|따라서 **아무것도 달지 않은 폴더의 동작은 지금과 같습니다** — 티어가 하나뿐이면 차단점도 없습니다. 음수를 주면 달지 않은 것들보다 앞에, 양수를 주면 뒤에 놓입니다|
|차단 기준은 실행을 세우는 보고|`TreatWarningsAsErrors`를 켜면 경고도 차단점이 됩니다. 별도 설정이 없습니다|
|건너뛴 것은 보고에 남습니다|몇 개가 실행되지 않았고 어느 파일들인지 적힙니다. 절반이 돌지 않은 폴더가 통과한 폴더와 똑같이 읽혀서는 안 되기 때문입니다|
|`pre` · `global` · `runtime`만|`rules/tables/`는 병렬이고 규칙이 테이블 단위라 선언할 순서가 없습니다. 여기에 달면 **무시가 아니라 오류**입니다 — 그냥 넘어가면 단 사람은 적용된 줄로 읽습니다|
|값은 평범한 숫자여야 합니다|티어는 어느 규칙도 컴파일되기 전에 정해지므로, 이름 붙인 상수는 읽을 수 없습니다. 그 경우도 오류로 알립니다|

전체 순서는 `--list-validators`로 봅니다.

```
Global - 3 rule(s):
  tier -10
    rules/global/ReferenceIntegrityRules.cs
  tier 0 (default)
    rules/global/ConventionsRules.cs
    rules/global/SurveyRules.cs
```

|성질|내용|
|--|--|
|병렬|`rules/tables/`의 규칙은 서로 독립이라 병렬로 돕니다. `pre` · `global` · `runtime`은 순차입니다|
|보고 순서|파일 · 시트 · 로우 · 컬럼으로 정렬해서 냅니다. 병렬인데 정렬하지 않으면 실행마다 순서가 달라져 CI 로그 diff가 매번 바뀝니다|
|한 번에 전부|하나 고치고 다시 돌리는 대신, 규칙 141개가 깨져 있으면 141개를 한 번에 보고합니다|
|컴파일 오류|검증 오류와 같은 경로로, 파일·줄·열과 함께 보고합니다. 한 파일이 깨져도 나머지는 전부 컴파일합니다|

## 13. 사례

### 조건부 필수

```csharp
// isNotSell이 false면 sellPrice가 있어야 합니다.
foreach (var row in Tables.Item.Records)
{
    if (!row.IsNotSell && row.SellPrice == 0)
        context.Error(row, nameof(row.SellPrice), "판매 가능한 아이템에는 판매가가 필요합니다.");
}
```

시트의 `:required`로는 적을 수 없습니다. 다른 컬럼의 값에 달려 있기 때문입니다.

### 다른 테이블의 값에 걸린 조건

```csharp
foreach (var row in Tables.Item.Records)
{
    if (row.Type != ItemType.BuffConsumable)
        continue;

    var buff = Tables.WorldBuff.FindByIndex(row.BuffId);

    if (buff is null || buff.BuffTargetType is not (1 or 2))
        context.Error(row, nameof(row.BuffId), "WorldBuff의 대상은 선단 또는 함대만 지원합니다.");
}
```

### 로우를 가로지르는 유일성

```csharp
// 같은 detailType의 아이템은 같은 WorldBuff.groupNo를 가리켜야 합니다.
var groupByDetail = new Dictionary<int, int>();

foreach (var row in Tables.Item.Records)
{
    if (row.Type != ItemType.BuffConsumable)
        continue;

    int group = Tables.WorldBuff.FindByIndex(row.BuffId)?.GroupNo ?? 0;

    if (!groupByDetail.TryAdd(row.DetailType, group) && groupByDetail[row.DetailType] != group)
        context.Error(row, nameof(row.DetailType), "같은 detailType은 같은 groupNo를 가리켜야 합니다.");
}
```

파일 안에서 행을 가로질러 상태를 모으는 것은 그대로 됩니다. 파일 사이는 아닙니다.

### 역참조 — 아무도 가리키지 않는 데이터

```csharp
// validation/rules/global/Orphans.cs
var referenced = new HashSet<int>();

foreach (var row in Tables.Shop.Records) referenced.Add(row.ItemId);
foreach (var row in Tables.RewardFixed.Records)
    foreach (var slot in row.Reward) referenced.Add(slot.Id);

var orphans = Tables.Item.Records.Where(item => !referenced.Contains(item.Index)).ToList();

// 쓰이지 않는 데이터는 잘못이 아니라 정리 대상이므로 경고입니다.
if (orphans.Count > 0)
    context.Warn($"어느 상점·보상도 가리키지 않는 아이템 {orphans.Count}개.");
```

### 합계

```csharp
foreach (var pool in Tables.LootPool.Records.GroupBy(row => row.PoolId))
{
    int total = pool.Sum(row => row.Rate);

    if (total != 10000)
        context.Error(pool.First(), nameof(pool.First().Rate), $"풀 {pool.Key}의 확률 합이 {total}입니다.");
}
```

### 규약 — 모든 테이블에

```csharp
// validation/rules/global/ConventionsRules.cs
foreach (var field in context.Schema.Fields)
{
    // 서버 전용 컬럼이 클라 전용 테이블에 있으면 어느 빌드에도 들어가지 않습니다.
    if (field.TargetSide != field.Table.TargetSide && field.Table.TargetSide != "Both")
        context.Warn(field, $"`{field}`의 사이드가 테이블과 다릅니다.");
}
```

> **이름 표기는 여기에 쓰지 않아도 됩니다.** 「필드는 camelCase」 같은 규약과, 한 이름이
> 여러 표기로 적힌 것의 검출은 recipe의
> [`Naming` 섹션](../recipe/settings.md#naming--이름의-표기-규약)이 합니다 — 프로젝트마다 같은
> 규칙을 다시 쓰지 않기 위해 코어로 옮긴 것입니다. 규칙 파일은 표기로 표현할 수 없는 것을
> 위한 자리로 남습니다.

### 사전 검증 — 시트를 읽기 전

```csharp
// validation/rules/pre/Naming.cs
context.Info($"로케일 {context.Option("Locale", "KR")}로 검증합니다.");

if (!context.HasOption("ContentRoot"))
    context.Error("`ContentRoot` 옵션이 없으면 에셋 검사를 할 수 없습니다.");
```

## 14. 자주 만나는 메시지

|메시지|뜻|
|--|--|
|`is a rule for table X, which this model does not have`|`rules/tables/XRules.cs`인데 `X` 테이블이 없습니다. 테이블 이름이 바뀌었거나 파일 이름에 오타가 있습니다|
|`has a subfolder Y, which is not one this layout runs`|`rules/` 아래에 `pre` `tables` `global` `runtime` `shared` 외의 폴더가 있습니다. `#`를 붙이면 건너뜁니다|
|`at its root, which is where the stages were before they moved under rules/`|이전 배치입니다. 단계 폴더를 `rules/` 아래로 옮기세요 — 규칙 자체는 고칠 것이 없습니다|
|`is on the context ... hands over, and this file is in ...`|그 단계의 컨텍스트에 없는 것을 불렀습니다. 파일을 옮기거나 그 단계가 가진 것을 확인하세요 (§5.1)|
|`has nothing to run`|`public static void Validate(<그 단계의 컨텍스트> context)`가 없는 파일입니다. 헬퍼라면 `rules/shared/`로 옮기세요|
|`This rule reads the validation option X, which the recipe does not set`|`Option(key)`가 없는 키입니다. recipe에 넣거나 `Option(key, 기본값)`을 쓰세요|
|`only the rules/runtime/ rules may do`|`Db()`/`Redis()`를 `rules/runtime/` 밖에서 불렀습니다. 평범하게 적으면 컴파일부터 되지 않으므로, 이 메시지는 캐스팅으로 타입을 우회한 경우입니다|
|`This build of Tabbit carries no ...`|참조 어셈블리가 실행 파일에 들어가지 않은 빌드입니다. 정상 빌드에서는 나오지 않습니다|

## 15. 한계

|한계|내용|
|--|--|
|규칙이 쓸 수 있는 것은 **정해진 세트**입니다|프레임워크(`Microsoft.NETCore.App`)와 `Tabbit.Validation`, 그리고 `Newtonsoft.Json`입니다. 도구 자신의 의존(Roslyn·Serilog·DB 드라이버)은 규칙에서 쓸 수 없습니다 — 도구가 무엇을 참조하는지가 규칙이 쓸 수 있는 것을 정하지 않도록, 그 목록을 도구가 실행 파일에 담고 다닙니다|
|**샌드박스가 없습니다**|규칙은 변환기 권한으로 도는 C#입니다. 파일을 쓸 수 있고 네트워크에 나갈 수 있습니다. **검증 폴더는 코드로 취급하세요** — 같은 저장소, 같은 리뷰, 같은 승인 절차. CI가 PR의 규칙을 도는 것은 PR 작성자가 빌드 머신에서 코드를 도는 것입니다|
|**메모리**|같은 데이터를 세 벌 유지합니다 — Model, `.tcb` 바이트, 리더의 객체 그래프. 큰 워크북에서 가장 먼저 한계에 닿을 곳입니다|
|**셀 위치의 근사**|접힌 배열·레코드 그룹·매트릭스 표는 그룹의 첫 컬럼을 가리킵니다. 원소를 알면 넘기세요|
|**IDE 자동 완성에는 준비가 필요합니다**|프로젝트 생성이 옵션(기본 꺼짐)이고, 검증 폴더를 편집기에 따로 열어야 합니다. §17을 보세요|
|**컴파일 캐시가 없습니다**|실행마다 전부 컴파일합니다. 규칙 수가 많아져 문제가 되면 그때 만듭니다 — 짐작이 아니라 실측으로 판단할 일입니다|
