# 규칙 쓰기

> [「정적 검증」으로 돌아가기](../validation.md)

---

## 6. 데이터 읽기 — 루트가 둘

|루트|무엇|언제|
|--|--|--|
|**`context.Tables`**|시트에서 생성된 C# 액세서. 프로젝트의 게임 코드가 쓰는 그 타입입니다|대상 테이블을 **아는** 규칙|
|**`context.Schema`**|테이블·컬럼을 열거하는 메타 뷰|대상이 이름으로 특정되지 **않는** 규약|

> **`Tables`를 전역으로 써도 됩니다.** `context.Tables`가 이번 실행이 읽은 인스턴스이고, 정적
> `Tables`는 같은 데이터를 전역으로 가리킵니다 — 둘은 같은 것이고 선택입니다. 어느 쪽이어도
> 되는 자리라면 `context.Tables` 쪽이 낫습니다. 규칙이 자기가 받은 것만 읽으므로 한 프로세스가
> 두 데이터 세트를 여는 경우에 섞이지 않기 때문입니다.
>
> 그 이름이 어떻게 성립하는지는 §17에 있습니다 — 계약 어셈블리는 액세서보다 먼저 만들어지므로
> 그 타입을 선언할 수 없고, 생성된 어셈블리가 확장 속성으로 잇습니다.

둘이 다 필요한 이유는 성질이 반대라는 것입니다.

타입 액세서는 이름을 알 때 강력합니다. 필드가 실제 타입이고 오타가 컴파일 오류입니다.

반면 프로퍼티는 누군가 적은 이름에만 존재하므로 「모든 테이블」을 다룰 수 없습니다.

### `context.Tables` — 타입으로

```csharp
foreach (var row in context.Tables.Item.Records)
{
    // 참조는 해석된 상태로 옵니다. 저장된 키가 아니라 레코드입니다.
    if (row.ItemCategoryByCategoryId is null)
        context.Error(row, nameof(row.ItemCategoryByCategoryId), "모든 아이템은 카테고리에 속해야 합니다.");

    // enum 컬럼은 생성된 enum이므로 숫자가 아니라 라벨로 비교합니다.
    if (row.GradeField == Grade.Epic && row.Price <= 0)
        context.Error(row, nameof(row.Price), "에픽 등급에는 가격이 있어야 합니다.");
}

// 다른 테이블도 같은 방법으로 봅니다.
var buff = context.Tables.WorldBuff.FindByIndex(row.BuffId);
```

|얻는 것|
|--|
|`context.Tables.Itme`이나 `row.MaxStak`은 실행 중의 드러나지 않는 미스가 아니라 **파일·줄 번호가 찍힌 컴파일 오류**입니다|
|`row.Price`는 `int`, `row.GradeField`는 생성된 enum입니다|
|**참조가 해석되어 있습니다** — 리더가 연결 단계를 수행하므로 `row.ItemCategoryByCategoryId.Name`처럼 따라갑니다|
|자동 완성이 무엇이 있는지 알려줍니다. 테이블 275개의 컬럼을 기억할 필요가 없습니다|

이것이 가능한 이유는 검증이 프로젝트의 C#을 실제로 생성해서 컴파일하기 때문입니다.

recipe에 csharp 타깃이 있든 없든 생성하고, 데이터는 바이너리를 메모리에서 만들어 생성된 리더에
그대로 넘깁니다. 파일은 쓰지 않습니다.

그래서 규칙이 보는 데이터는 게임이 읽을 데이터와 같은 코드로 읽힌 것입니다.

### `Schema` — 열거로

```csharp
foreach (var table in context.Schema.Tables)
{
    if (string.IsNullOrWhiteSpace(table.Comment))
        context.Warn(table, $"테이블 `{table.Name}`에 설명이 없습니다.");

    foreach (var field in table.Fields)
    {
        // 이름이 참조를 뜻하는 컬럼은 실제로 참조여야 합니다.
        if (field.Name.EndsWith("ItemId") && field.References?.Name != "Item")
            context.Error(field, $"`{field}`는 이름이 Item 참조를 뜻하는데 참조가 아닙니다.");
    }
}
```

|`TableSchema`|`FieldSchema`|
|--|--|
|`Name` · `RawName` · `Comment` · `RowCount` · `TargetSide`|`Name` · `TypeName` · `Comment` · `TargetSide`|
|`Fields` · `Index` · `Field("이름")`|`IsRequired` · `IsIndex` · `IsArray` · `IsReference`|
||`References` — 가리키는 `TableSchema`, 없으면 `null`|

`Schema`는 `rules/pre/`에서 쓸 수 없습니다 — 아직 시트를 읽지 않았기 때문이고, 그렇게 쓰면 어느 폴더로
옮기라는 메시지가 나옵니다.

## 7. 보고 — 심각도 셋

|심각도|커밋|위치|무엇을 적나|
|--|--|--|--|
|`Error`|**차단합니다**|셀|데이터가 규칙을 위반한 것|
|`Warn`|막지 않습니다. `TreatWarningsAsErrors`로 승격 가능|셀|잘못이라 단정할 수 없지만 봐야 하는 것|
|`Info`|**막지 않고 승격도 없습니다**|선택|검증이 무엇을 했는지|

승격되지 않는 것이 `Info`를 경고와 가릅니다.

오류가 될 수 있는 보고는 판정이고, `Info`는 기록입니다.

그래서 「이 규칙은 이 로케일에 해당하지 않아 건너뜁니다」는 `Info`입니다.
조용히 `return`하는 것과 다릅니다.

```csharp
if (context.Option("Locale") != "KR")
{
    context.Info("현금 판매 규칙은 KR에서만 적용됩니다. 이 실행은 건너뜁니다.");
    return;
}
```

### 오버로드 — 무엇을 가리킬지

```csharp
context.Error(row, nameof(row.Price), "...");      // 그 로우 그 컬럼의 셀
context.Error(row, nameof(row.Reward), "...", 2);  // 배열·레코드 그룹의 원소 2
context.ErrorAtRow(row, "...");                    // 로우의 기본 인덱스 셀 (여러 컬럼이 함께 잘못일 때)
context.Error(field, "...");                       // 컬럼 헤더 셀 (Schema 뷰)
context.Error(table, "...");                       // 테이블 선언 셀 (Schema 뷰)
context.Error("...");                              // 규칙 파일 자신
```

`Warn`과 `Info`도 같은 집합입니다.

필드 이름을 `nameof`로 넘기는 이유는 컴파일러가 확인하게 하려는 것입니다.

문자열 리터럴이면 컬럼 이름이 바뀌어도 걸리는 것 없이 통과합니다.

> 셀 위치는 되찾은 것입니다.
>
> 생성된 레코드는 데이터만 들고 있으므로(게임이 쓰는 타입이고 검증 때문에 바꾸지 않습니다),
> 컨텍스트가 레코드의 타입에서 테이블을, 기본 인덱스에서 행을, 필드 이름에서 컬럼을 조회합니다.
>
> 접힌 배열과 레코드 그룹과 매트릭스 표는 이 역이 정확하지 않아 그룹의 첫 컬럼을 가리킵니다.
> 원소를 알면 네 번째 인자로 넘기세요.

## 8. 설정 — recipe에서 규칙으로

```jsonc
"Validation": {
  "Path": "./validation",

  // 코어가 키를 모르는 자유 키/값. 규칙이 읽고 의미를 정합니다.
  "Options": {
    "Locale": "KR",
    "ContentRoot": "../game/content"
  },

  // rules/runtime/ 규칙이 이름으로 여는 연결. ${NAME}은 환경 변수에서.
  "Connections": {
    "Live": "mysql:Server=db;Database=game;Uid=ro_validator;Pwd=${DB_PASSWORD}",
    "Cache": "redis://cache:6379/0"
  },

  // 경고를 오류로. CI에서 켭니다. Info는 이것으로도 승격되지 않습니다.
  "TreatWarningsAsErrors": false
}
```

```csharp
context.Option("Locale")            // 없으면 오류 — 있는 키 목록과 함께 보고합니다
context.Option("Locale", "KR")      // 없으면 기본값
context.HasOption("Locale")         // 있는지만
```

`Option(key)`는 없는 키에 대해 오류를 냅니다.

빈 문자열로 말없이 답하면 로케일 비교가 아무것과도 맞지 않아, 아무것도 검사하지 않는 규칙이 되기
때문입니다.

## 9. 시트 밖의 파일

```csharp
// 폴더 하나를 이름으로 조회. 확장자와 대소문자는 무시합니다.
var icons = context.Files(context.Option("ContentRoot"), "*.uasset");

if (!icons.Has(row.IconName))
    context.Error(row, nameof(row.IconName), $"`{row.IconName}` 에셋이 없습니다.");

// 테이블이 아닌 일반 JSON.
var policy = context.Json(context.Option("ContentRoot") + "/policy.json");
```

`FileMap`은 `Has(name)`, `PathOf(name)`, `Names`, `Count`를 냅니다.

폴더와 패턴마다 한 번 스캔해서 실행 내내 공유하므로, 행마다 물어도 스캔은 한 번입니다.

에셋이 있는지는 이 도구가 판정하는 것이 아닙니다. 코어는 `.uasset`이 무엇인지 모릅니다.

폴더를 건네주고, 무엇을 확인할지는 프로젝트의 규칙이 정합니다.

## 10. 런타임 검증 — 저장소와 대조

`rules/runtime/` 폴더의 규칙만 외부 저장소를 열 수 있습니다.

```csharp
// validation/rules/runtime/Offer.cs
var live = context.Db("Live").Set<int>("SELECT product_id FROM live_products");

context.Info($"라이브 상품 {live.Count}건과 대조합니다.");

foreach (var row in Tables.Offer.Records)
{
    if (row.OnSale == 1 && !live.Contains(row.ProductId))
        context.Error(row, nameof(row.ProductId), "판매 중인데 라이브 상품 테이블에 없습니다.");
}

// 캐시도 이름으로 엽니다.
if (!context.Redis("Cache").Exists($"event:{row.EventId}"))
    context.Warn(row, nameof(row.EventId), "이벤트 키가 캐시에 없습니다.");
```

|`Db(name)` — `SqlStore`|`Redis(name)` — `RedisStore`|
|--|--|
|`Column<T>(sql)` · `Set<T>(sql)` · `Scalar<T>(sql)` · `Rows(sql)`|`Exists(key)` · `Get(key)` · `Field(key, field)` · `Members(key)`|

연결 문자열의 스킴이 종류를 나타냅니다. `mysql:`, `postgres:`, `redis://`입니다.

ADO 연결 문자열과 Redis 설정 문자열은 형식으로 구별되지 않으므로 추측하지 않습니다.

|정책|내용|
|--|--|
|**연결 실패 = 검증 실패**|결과를 얻지 못한 게이트는 통과가 아닙니다. 접근할 수 없는 환경은 `--skip-runtime-validation`으로 **명시하고**, 건너뛴 규칙 수가 기록에 남습니다|
|`rules/runtime/` 밖에서는 열 수 없습니다|어느 폴더로 옮기라는 메시지가 나옵니다. 그러지 않으면 `--skip-runtime-validation`이 아무 의미가 없습니다|
|읽기 전용은 **편의이지 보증이 아닙니다**|규칙은 임의의 C#이고 자기 연결을 직접 열 수 있습니다. 쿼리가 아닌 문장은 거부하지만 그것은 사고를 막는 두 번째 잠금이고, 첫 번째는 **읽기 전용 계정**입니다|
|순차 실행|연결을 든 규칙 20개를 동시에 돌리면 검증이 부하 시험이 됩니다|

## 11. 공용 코드

`rules/shared/`의 파일은 모든 규칙 파일의 컴파일에 함께 들어갑니다. 지시자를 적지 않습니다.

```csharp
// validation/rules/shared/Rewards.cs — `Validate`가 없으므로 실행되지 않고, 모든 규칙에 보입니다.
internal static class Rewards
{
    internal const int Item = 2;
    internal const int Ship = 6;

    internal static bool Exists(int type, int id) => type switch
    {
        Item => Tables.Item.ContainsIndex(id),
        Ship => Tables.Ship.ContainsIndex(id),
        _ => true,
    };
}
```

```csharp
// validation/rules/global/RewardIntegrity.cs — 규칙 하나가 한 파일에.
foreach (var row in Tables.RewardFixed.Records)
{
    foreach (var slot in row.Reward)
    {
        if (slot.Type != 0 && !Rewards.Exists(slot.Type, slot.Id))
            context.Error(row, nameof(row.Reward), $"보상 타입 {slot.Type}의 Id {slot.Id}가 대상 테이블에 없습니다.");
    }
}
```

`rules/shared/`는 하위 폴더를 가질 수 있습니다.

아무것도 스스로 실행되지 않으므로, 하위 폴더는 단계가 없는 규칙이 아니라 그냥 정리입니다.

> **`static` 상태를 `rules/shared/`에 두지 마세요.**
>
> 컴파일이 파일마다 독립이므로 파일 사이에 쌓이지 않습니다.
> 여러 테이블을 가로지르는 누적은 `rules/global/`의 한 파일에 두세요.
