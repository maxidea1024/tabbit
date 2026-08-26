# 검증의 범위와 실행

> [「검증 파이프라인」으로 돌아가기](../validation-pipeline.md)

---

## 4. 전역 검증 — 모델 전체에 대한 룰셋

테이블별 스크립트는 「이 테이블의 로우가 옳은가」만 확인할 수 있습니다. `global/`은 그것으로
확인할 수 없는 것을 위한 자리입니다.

**기존 검증에는 이 자리가 없었습니다.** `Perform(tables)`가 JSON 파일 단위이고 워커가 파일을
나눠 가지므로, 스크립트 사이에 상태를 둘 방법이 없었습니다. 그래서 교차 확인은 「내 테이블
스크립트에서 남의 JSON을 읽는」 방향으로만 성립하였고, **규칙 하나가 여러 파일에 흩어졌습니다** —
`RewardTypeValidator`를 7개 스크립트가 각자 부르던 것이 그 형태입니다. 규칙은 한 군데
있어야 하고, 그것이 이 폴더입니다.

### 두 가지 뷰 — 이름을 알 때와 모를 때

|뷰|무엇|쓰는 곳|
|--|--|--|
|`Tables.Item` (타입)|이름을 아는 접근. 필드가 실제 타입이고 오타가 컴파일 오류|대상 테이블을 아는 규칙|
|`Schema` (열거)|`Schema.Tables` → 테이블·필드의 메타 뷰. 이름을 모르는 채 돕니다|**모든** 테이블 또는 조건에 맞는 테이블에 적용하는 규약|

둘은 같은 Model을 가리키고 서로 건너갑니다 — `Tables.Item.Schema`로 메타 뷰에, 반대로 메타
뷰의 테이블에서 로우 값에 닿습니다. 타입 액세서만으로는 「모든 테이블을 돌면서」가 되지
않으므로(이름을 알아야 프로퍼티가 있습니다) 둘이 다 필요합니다.

### 교차 불변식 — 규칙 하나가 한 파일에

```csharp
// validation/rules/global/RewardIntegrity.cs
// 보상 슬롯의 (Type, Id)는 Type이 지정하는 테이블에 실재해야 합니다.
bool Exists(int type, int id) => type switch
{
    2 => Tables.Item.Contains(id),
    6 => Tables.Ship.Contains(id),
    7 => Tables.Mate.Contains(id),
    _ => true,                       // 대상 테이블이 없는 보상 타입
};

foreach (var row in Tables.RewardFixed)
{
    foreach (var slot in row.Reward)
    {
        if (slot.Type != 0 && !Exists(slot.Type, slot.Id))
            context.Error(row, nameof(row.Reward), $"보상 타입 {slot.Type}의 Id {slot.Id}가 대상 테이블에 없습니다.");
    }
}
```

### 규약 룰셋 — 대상이 이름으로 특정되지 않는 규칙

```csharp
// validation/rules/global/ConventionsRules.cs
foreach (var table in context.Schema.Tables)
{
    // 이름이 Item 참조를 뜻하는 컬럼은 실제로 참조여야 합니다.
    foreach (var field in table.Fields)
    {
        if (field.Name.EndsWith("ItemId") && field.RefTable?.Name != "Item")
            context.Error(field, $"`{table.Name}.{field.Name}`은 이름이 Item 참조를 뜻하는데 참조가 아닙니다.");
    }

    // 설명 없는 테이블은 차단할 일은 아니지만 남겨둘 일도 아닙니다.
    if (string.IsNullOrWhiteSpace(table.Comment))
        context.Warn(table, "테이블에 설명이 없습니다.");
}
```

### 전역 규칙의 형태

|형태|예|
|--|--|
|교차 불변식|보상 `(Type, Id)`가 대상 테이블에 실재 · 상점 가격이 아이템 등급의 대역 안|
|합계·분포|드롭 풀의 확률 합이 100 · 등급별 개수의 하한|
|역참조(고아)|어느 상점·보상·퀘스트도 가리키지 않는 아이템. `Warn`이 맞습니다 — 쓰이지 않는 데이터는 잘못이 아니라 정리 대상입니다|
|전역 이름 공간|여러 테이블이 한 id 공간을 나눠 쓸 때의 대역 충돌|
|규약|컬럼 명명 · 첫 컬럼 이름 · 설명 유무 · 대상 사이드의 일관성|
|존재|있어야 하는 테이블·enum이 실재하는가. 소스 하나가 빠져 테이블 무리가 사라진 것을 `tables/`의 이름 대조가 검출하지만, **스크립트가 없는 테이블**은 그것으로 잡히지 않습니다|

전역 스크립트는 테이블별 스크립트가 전부 끝난 뒤 **순차로** 돕니다 — 모으는 일을 하는 쪽이라
병렬로 두면 누적이 스레드 문제가 됩니다(§7).

> **선언형 DSL을 두지 않았습니다.** `Rule("…").ForEachTable().Require(…)` 같은 문법도
> 가능하지만, `foreach`와 `if`로 적은 규칙은 C#을 아는 사람에게 설명이 필요 없고 DSL은
> **표현할 수 있는 것의 경계를 새로 만듭니다** — 그 경계를 넘는 규칙이 나오면 결국
> `foreach`로 돌아옵니다. 대신 **파일 하나 = 규칙 하나**를 관례로 두어 파일 목록이 곧 룰셋
> 목록이 되게 합니다. 그것이 `tables/`에서 이미 효과가 확인된 방법입니다.

## 5. recipe — 검증 섹션

```jsonc
"Validation": {
  // 폴더 규약(§3)의 루트. 비우면 검증 파이프라인 자체가 꺼진 것입니다.
  "Path": "./validation",

  // 스크립트만 읽는 자유 키/값. 코어는 키를 모르고 검증하지 않습니다 — LayoutOptions와 같은 패턴.
  "Options": {
    "ContentRoot": "../game/content",
    "Locale": "KR"
  },

  // rules/runtime/ 스크립트가 이름으로 여는 연결. ${NAME}은 환경 변수에서 — 기존 익스포터와 같은 규칙.
  "Connections": {
    "GameDb": "Server=db;Database=game;Uid=ro_validator;Pwd=${DB_PASSWORD}",
    "Cache": "redis://cache:6379/0"
  },

  // 경고를 오류로 취급할지. CI에서 켜는 용도이고, Info는 이것으로도 승격되지 않습니다.
  "TreatWarningsAsErrors": false
}
```

CLI는 둘이 늘어납니다.

|옵션|무엇|
|--|--|
|`--validate-only`|①②③까지만 돌고 타깃을 실행하지 않습니다. PR 검사가 쓰는 형태입니다|
|`--skip-runtime-validation`|`runtime/` 폴더만 건너뜁니다. 외부 저장소가 없는 로컬 실행용 — 건너뛴 사실은 요약에 남습니다|

`tables/` · `global/` · `pre/`를 건너뛰는 옵션은 **없습니다.** 스스로 꺼지는 게이트는 없는
게이트보다 나쁘다는 이 저장소의 원칙 그대로이고, 정말 끄려면 recipe에서 `Path`를 비우는 —
diff에 남는 — 행동이어야 합니다.

## 6. 런타임 검증 — 시트 밖의 것과 대조

시트가 가리키는 것이 시트 밖에 있을 때가 있습니다 — 운영 데이터베이스에 이미 발급된 쿠폰
코드, Redis에 올라가 있는 이벤트 키. `runtime/` 스크립트는 recipe `Connections`의 이름으로
게이트웨이를 열어 교차 확인합니다.

```csharp
// validation/rules/runtime/CashShop.cs
var live = context.Db("GameDb").Column<int>("SELECT product_id FROM live_products");
context.Info($"라이브 상품 {live.Count}건과 대조합니다.");

foreach (var row in Tables.CashShop)
{
    if (row.OnSale == 1 && !live.Contains(row.ProductId))
        context.Error(row, nameof(row.ProductId), "판매 중으로 표시되었지만 라이브 상품 테이블에 없습니다.");
}
```

|정책|내용|
|--|--|
|읽기 전용|게이트웨이는 조회 API만 냅니다. 검증이 저장소를 바꿀 수 있는 형태 자체를 만들지 않고, 계정도 읽기 권한만 권장합니다|
|연결 실패 = 검증 실패|결과를 얻지 못한 게이트는 통과가 아닙니다. 외부 저장소 없이 돌아야 하는 환경은 `--skip-runtime-validation`으로 **명시하고**, 건너뛴 사실이 요약에 남습니다|
|타임아웃|기본 수 초. 검증이 죽은 저장소를 기다리며 변환 전체를 세우지 않도록|
|구현 재사용|MySQL·PostgreSQL·MongoDB·Redis 연결은 익스포터가 이미 들고 있는 코드입니다. 게이트웨이는 그 위의 얇은 읽기 전용 뷰입니다|

## 7. 실행 — 컴파일과 병렬

|무엇|어떻게|
|--|--|
|준비|C# 액세서를 생성해 컴파일하고, 바이너리를 메모리에서 만들어 리더에 넣습니다(§3). 그 뒤 폴더를 스캔해 스크립트 전부를 액세서를 참조해 컴파일합니다|
|컴파일 오류|검증 오류와 같은 경로로 — 파일·줄 번호와 함께 — 보고하고, **하나가 깨져도 나머지는 전부 컴파일해** 한 번에 보고합니다|
|캐시|**아직 없습니다.** 한 실행 안에는 캐시할 것이 없고, 실행 사이에 캐시하려면 어셈블리를 콘텐츠 해시로 디스크에 남겨야 합니다 — 실측에서 필요가 확인될 때 만드는 것이 맞습니다. 그 실측은 이식(§9-6)에서 나옵니다|
|병렬|테이블별 스크립트는 서로 독립이므로 병렬로 돕니다. `Diagnostics` 수집만 스레드 안전하게. 전역·런타임 스크립트는 그 뒤 순차입니다|
|순서 보장|스크립트 사이의 실행 순서에 기대지 않습니다 — Lua의 `_buffConsumableGroupCheck`처럼 스크립트 **안에서** 로우를 가로질러 상태를 모으는 것은 그대로 되고, 스크립트 **사이의** 상태 공유는 제공하지 않습니다. 여러 테이블을 함께 봐야 하는 규칙은 전역 스크립트 한 파일에 둡니다(§4)|

## 8. 코어와 플러그인의 경계

[코어에 프로젝트 이름 금지](../../../CLAUDE.md) 규칙이 이 기능의 형태를 정합니다.

|코어 (`src/Validation/`)|프로젝트 (recipe가 가리키는 폴더)|
|--|--|
|폴더 규약 · 액세서 생성 · 규칙 파일 컴파일 · 컨텍스트 · 기본 헬퍼 · 게이트 배선|모든 검증 규칙 — `ItemRules.cs`의 아이템 타입 상수와 `ConventionsRules.cs`의 명명 규약까지 전부|
|`Schema` 열거 뷰 — 어떤 규약이 있어야 하는지는 모릅니다|`*ItemId`가 무엇을 뜻하는지|
|`Option(key)` 통로 — 키를 모릅니다|`Locale` · `ContentRoot` 같은 키의 의미|
|`Files(pattern)` — 확장자를 모릅니다|`.uasset`을 볼지 `.br`을 볼지|
|`Db(name)` — 스키마를 모릅니다|어느 테이블을 왜 조회하는지|

판단 기준도 같습니다: **validation 폴더를 통째로 지웠을 때** 빌드가 깨지거나 코어에 흔적이
남으면 설계가 잘못된 것입니다. named-range의 검증 141개를 이식해도 이 저장소의 diff는
`samples/named-range/validation/` 아래에만 생겨야 합니다.
