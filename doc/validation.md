# 검증 — 시트로 나타낼 수 없는 규칙을 C#으로

> [문서 목록으로](../readme.md) · 설계 근거는 [검증 파이프라인](../spec/validation-pipeline.md)에 있습니다

시트에 적을 수 있는 제약은 변환할 때 이미 검사합니다 — 필수 여부, 숫자 범위, 허용값 목록,
참조가 실재하는지, 인덱스가 유니크한지. 이 문서는 **그것으로 표현할 수 없는 규칙**을 적는 방법을
설명합니다.

「type이 36인 아이템의 `buffId`가 가리키는 `WorldBuff`는 대상이 선단이나 함대여야 한다」처럼
여러 컬럼과 여러 테이블에 걸치는 규칙, 「`*ItemId`로 끝나는 컬럼은 전부 `Item`을 가리켜야 한다」
처럼 모든 테이블에 일률적으로 적용되는 규약, 그리고 「판매 중으로 표시된 상품이 운영 DB에 실재
하는지」처럼 시트 밖까지 봐야 하는 확인입니다.

---

## 1. 개요

|무엇|어디에 적나|누가 검사하나|
|--|--|--|
|타입 · 필수 · 범위 · 허용값 · 참조 · 인덱스 유니크|**시트**|변환기가 내장으로. [컬럼 제약](../spec/column-constraints.md)|
|그 밖의 모든 규칙|**`.cs` 규칙 파일**|이 문서가 설명하는 검증 파이프라인|

규칙 파일은 프로젝트의 폴더에 있고, 변환기가 실행할 때 컴파일해서 돕니다. 이 저장소는 그 규칙이
무엇인지 알지 못합니다 — recipe가 폴더를 가리키고, 그 안에 무엇이 있든 코어는 알지 못합니다.

**보고에 셀 위치가 나옵니다.** 산출물을 나중에 훑는 방식과 이것이 갈리는 지점입니다.

```
[rules/tables/ItemRules.cs] 의뢰서 아이템의 maxStack은 1이어야 합니다.
    at UWO_테이블.xlsb : Item : AF12
```

구글 시트라면 그 셀로 가는 링크가 나옵니다.

## 2. 파이프라인에서의 위치

```
recipe 로드
    │
    ├─ ① rules/pre/       시트를 읽기 전 — 파일 이름 · 설정 · 환경
    ▼
임포트 → 쿠킹 → Model
    │
    ├─ ② 정적 검증  타입 · 인덱스 · 컬럼 제약 · 참조 (코어 내장)
    │
    ├─ ③ rules/tables/    테이블별
    │   rules/global/     테이블 사이 · 규약
    │   rules/runtime/    데이터베이스 · 캐시 교차 확인
    ▼
타깃 실행 → 커밋
```

**③이 타깃 실행보다 앞입니다.** 파일 타깃은 스테이징에 모았다가 마지막에 옮기지만 데이터베이스
타깃은 **실행 중에 섀도를 교체**하므로, 타깃 뒤에서 검증하면 실패 시점에 이미 데이터가 바뀌어
있습니다. 모든 산출물은 Model의 결정적 사영이므로 Model을 검증하는 것이 곧 산출물을 검증하는
것이고, 그래서 **실패한 실행은 파일에도 데이터베이스에도 흔적을 남기지 않습니다.**

## 3. 시작하기

### recipe에 세 줄

```jsonc
"Validation": {
  "Path": "./validation"
}
```

`Path`를 비우면 검증이 꺼집니다. **그것이 끄는 유일한 방법이고**, diff에 남습니다 — 명령줄로 끌 수
있는 게이트는 아무도 신뢰할 수 없기 때문입니다.

### 첫 규칙 파일

```
tabbit --recipe recipe.json --new-validator Item
```

`validation/rules/tables/ItemRules.cs`가 생깁니다. 열면 이렇게 되어 있습니다.

```csharp
using Tabbit.Rules;
using Tabbit.Validation;

internal static class ItemRules
{
    public static void Validate(ITableContext context)
    {
        foreach (var row in context.Tables.Item.Records)
        {
            // if (row.Something < 0)
            //     context.Error(row, nameof(row.Something), "Something cannot be negative.");
        }
    }
}
```

**이 파일에 감춰진 것이 없습니다.** 규칙이 쓰는 이름 둘이 `using` 두 줄로 파일 안에 있고, 진입점은
`Validate` 하나이고, 규칙이 보고에 쓰는 것은 그것이 받는 인자입니다. 호스트가 암묵적으로 제공하는 것에 기대는
줄이 하나도 없다는 뜻이고, 그래서 **편집기가 이 파일을 열자마자 전부 해석합니다** (§17).

### 확인

```
tabbit --recipe recipe.json --validate-only
```

검증까지만 돌고 산출물을 하나도 만들지 않습니다. PR 검사가 쓰는 형태입니다.

> **돌아가는 규칙 폴더가 저장소에 있습니다** — [side-by-side/validation/](../side-by-side/validation/)이고, `rules/` 아래 `pre`·`tables`·`global`·`shared` 각각 한 파일입니다. 같은 디렉터리에 그 시트를 13개 언어로 뽑은 결과가 함께 커밋되어 있으므로, 규칙이 읽는 `context.Tables.Package.Records`가 실제로 어떤 타입인지 옆에서 확인할 수 있습니다.

## 4. 폴더 규약

```
validation/
  rules/              손으로 쓰는 것은 전부 이 아래에 있습니다
    pre/              시트를 읽기 전 — 파일 이름 · 설정 · 환경
    tables/           테이블별 — 파일명이 `<테이블>Rules.cs` (ItemRules.cs → Item 테이블)
    global/           전역 — 테이블 사이, 그리고 모든 테이블에 걸친 규약
    runtime/          외부 저장소 교차 확인 — 이 폴더만 건너뛸 수 있습니다
    shared/           공용 코드 — 모든 규칙과 함께 컴파일되고, 그 자체로는 실행되지 않습니다
  lib/                도구가 쓰는 것 — 계약 어셈블리와 생성된 액세서. **커밋합니다**
  Validation.csproj   도구가 쓰는 것 — 편집기가 읽는 프로젝트. **커밋합니다**
  .build/             그 프로젝트를 빌드한 산출물. 버전 관리에서 제외합니다
```

**`rules/` 하나가 경계입니다.** 그 아래는 사람이 쓴 것이고 나머지는 도구가 씁니다. 단계 폴더가
`lib/`·`.build/`·`.csproj`와 나란히 있던 때에는 목록만 보고 어느 쪽인지 알 수 없었습니다.

> 이전 배치(단계 폴더가 `validation/` 바로 아래)로 된 폴더는 **오류로 보고하고 옮기라고 안내합니다.**
> 조용히 지나가면 규칙이 하나도 돌지 않은 실행이 통과한 실행과 똑같이 보입니다.

|규칙|이유|
|--|--|
|네 폴더는 **평평합니다**. 하위 폴더의 파일은 실행되지 않습니다|폴더가 실행 시점과 받는 것을 정하므로, 한 단 아래의 파일은 단계가 없는 규칙이 됩니다. 정리하고 싶으면 `rules/shared/`를 쓰세요|
|`rules/tables/`의 파일은 `<테이블>Rules.cs`여야 하고, `X` 테이블이 없으면 **오류**입니다|테이블 이름이 바뀌면 규칙이 조용히 안 도는 것이 아니라 파일을 따라 옮기라고 안내합니다. 접미사는 이름이 곧 대상인 자리에서 그 대상을 나머지와 갈라주고, 생성된 `Item.cs`와 규칙 `ItemRules.cs`가 한 프로젝트에서 같은 이름이 되지 않게 합니다|
|`rules/` 아래에 이 다섯 외의 폴더가 있으면 **오류**입니다|`table/`은 규칙이 하나도 돌지 않는 폴더이고, 산출물의 어디에도 그 사실이 남지 않습니다. `#`로 시작하면 건너뜁니다|
|`Path`가 있는데 폴더가 없으면 **오류**입니다|오타 하나로 검증 전체가 조용히 통과합니다|
|규칙 파일이 하나도 없으면 **경고**입니다|빈 폴더는 프로젝트의 시작일 수 있지만, 아무 표시 없이 지나가서는 안 됩니다|

### 폴더마다 있는 시작 파일

각 폴더에 `_....cs.template` 파일이 하나씩 있습니다. **복사해서 `.cs`로 이름을 바꾸면 그 폴더의
규칙이 됩니다.** 머리말에 그 폴더의 규약이 적혀 있습니다 — 파일 이름, 진입점, 그 단계에서 쓸 수
있는 것과 없는 것, 우선순위. 이 문서를 다시 찾지 않아도 되도록 폴더 안에 둔 것입니다.

`.template` 확장자인 이유가 있습니다. 폴더의 `.cs`는 **전부** 규칙으로 컴파일되므로, 빈 시작
파일을 `.cs`로 두면 진입점이 없다는 오류로 실행이 멈춥니다. `.template`은 스캔 대상이 아니라서
안내문이 폴더 안에 있을 수 있습니다.

> 파일이 하나씩 들어 있으므로 **빈 폴더도 git에 남습니다.** 받은 사람이 폴더 구조를 그대로
> 봅니다.

## 5. 규칙 파일의 모양

**평범한 `.cs` 파일이고, 프로젝트에 등재되지 않습니다.** 클래스 하나에 `Validate` 하나이고,
파일 이름과 클래스 이름이 같습니다 — `ItemRules.cs`의 `ItemRules`입니다.

|정통 소스|규칙 파일|
|--|--|
|네임스페이스 · 클래스 · 진입점|클래스와 `Validate`. 인자 타입은 폴더가 정합니다(§5.1). 네임스페이스는 두지 않습니다|
|빌드가 컴파일|**변환기가 실행 시점에** 컴파일|
|프로젝트 참조|호스트가 줍니다 — 생성 액세서와 이 어셈블리|
|다른 파일과의 결합은 프로젝트가|`rules/shared/`에 두면 호스트가 같은 컴파일에 넣습니다|

`System` · `System.Linq` · `System.Collections.Generic`은 호스트가 열어둡니다. 그 셋뿐이고,
**규칙이 실제로 쓰는 이름 둘은 파일에 적힌 `using` 두 줄입니다** — `Tabbit.Rules`가 `Tables`이고,
`Tabbit.Validation`이 컨텍스트 타입들입니다. 그 둘을 파일에 두는 것이 편집기가 파일을 해석할 수
있게 하는 조건입니다(§17). 실행만 생각하면 없어도 되지만, 없으면 편집기에서 아무것도 되지 않습니다.

> `rules/pre/` 규칙만 `using Tabbit.Rules;`를 쓰지 않습니다 — 시트를 읽기 전이라 액세서가 아직 없고,
> 그 줄은 컴파일 오류가 됩니다. `--new-validator`가 써주는 것이 폴더에 맞는 머리말입니다.

### 5.1 폴더가 정하는 인자 타입

|폴더|`Validate`이 받는 것|그 단계에만 있는 것|
|--|--|--|
|`rules/pre/`|`IPreContext`|— (보고 · `Option` · `Files` · `Json`뿐)|
|`rules/tables/`|`ITableContext`|`context.Table` — 그 파일이 담당하는 테이블의 스키마|
|`rules/global/`|`IGlobalContext`|`context.Schema`와 행·컬럼을 짚는 보고|
|`rules/runtime/`|`IRuntimeContext`|`context.Db()` · `context.Redis()`|

**타입이 곧 그 단계의 경계입니다.** 넷은 겹쳐 있습니다 — `IPreContext`가 가장 좁고, `IGlobalContext`가
데이터를 더하고, `ITableContext`와 `IRuntimeContext`가 각각 그 위에 하나씩 더합니다. 그래서
`rules/shared/`의 헬퍼는 **실제로 쓰는 것 중 가장 좁은 타입**을 받으면 그 아래 단계 전부에서
불립니다.

전에는 넷이 같은 타입을 받았고, 폴더에 맞지 않는 것을 부르면 컴파일된 뒤 실행 중에 「어느 폴더로
옮기라」는 메시지가 나왔습니다. 지금은 이름 자체가 없으므로 **타이핑하는 동안 편집기가 말합니다.**
실행에서 만나더라도 보고가 그 이름을 가진 폴더를 함께 알려줍니다.

```
[rules/tables/ItemRules.cs] 'ITableContext'에는 'Db'에 대한 정의가 포함되어 있지 않고 …
    `Db` is on the context `rules/runtime/` hands over, and this file is in `rules/tables/`.
```

> 호스트가 넘기는 객체 하나가 넷을 다 구현하므로 **캐스팅으로 우회할 수는 있습니다.** 그렇게 해도
> `Db()`는 단계를 확인하고 거부합니다 — `--skip-runtime-validation`이 의미를 가지려면 저장소를 여는
> 규칙이 그 폴더 안에만 있어야 하기 때문입니다.

**컴파일 단위는 규칙 파일 하나입니다.** 언어의 제약이 아니라 호스트가 그렇게 나눈 것이고, 결과
셋을 위해 그렇게 합니다 — `rules/shared/`의 `static` 상태가 파일마다 독립이라 병렬 실행에 경합이 없고,
한 파일의 컴파일 오류가 다른 파일을 막지 않으며, 파일 사이에 상태를 공유할 수 없습니다.

**아래 사례들은 `Validate`의 본문만 보여줍니다.** 감싸는 클래스는 위와 같고 매번 반복하지 않습니다.

> **검증 폴더를 다른 C# 프로젝트 트리 안에 두지 마세요.** 부모 프로젝트가 `**/*.cs`로 이 파일들을
> 걷어가면 `Tables`를 참조하지 못해 부모 빌드가 깨집니다. 시트 옆이 제자리이고, 편집기용 프로젝트가
> 필요하면 이 도구가 검증 폴더에 써줍니다 (§17).

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

둘이 다 필요한 이유는 성질이 반대라는 것입니다. 타입 액세서는 이름을 알 때 강력하지만 —
필드가 실제 타입이고 오타가 컴파일 오류입니다 — 프로퍼티는 누군가 적은 이름에만 존재하므로
「모든 테이블」을 다룰 수 없습니다.

### `context.Tables` — 타입으로

```csharp
foreach (var row in context.Tables.Item.Records)
{
    // 참조는 해석된 상태로 옵니다. 저장된 키가 아니라 레코드입니다.
    if (row.CategoryId is null)
        context.Error(row, nameof(row.CategoryId), "모든 아이템은 카테고리에 속해야 합니다.");

    // enum 컬럼은 생성된 enum이므로 숫자가 아니라 라벨로 비교합니다.
    if (row.GradeField == Grade.Epic && row.Price <= 0)
        context.Error(row, nameof(row.Price), "에픽 등급에는 가격이 있어야 합니다.");
}

// 다른 테이블도 같은 방법으로 봅니다.
var buff = context.Tables.WorldBuff.FindByIndex(row.BuffId);
```

|얻는 것|
|--|
|`context.Tables.Itme`이나 `row.MaxStak`은 실행 중의 조용한 미스가 아니라 **파일·줄 번호가 찍힌 컴파일 오류**입니다|
|`row.Price`는 `int`, `row.GradeField`는 생성된 enum입니다|
|**참조가 해석되어 있습니다** — 리더가 연결 단계를 수행하므로 `row.CategoryId.Name`처럼 따라갑니다|
|자동 완성이 무엇이 있는지 알려줍니다. 테이블 275개의 컬럼을 기억할 필요가 없습니다|

이것이 가능한 이유는 **검증이 프로젝트의 C#을 실제로 생성해서 컴파일하기 때문**입니다. recipe에
csharp 타깃이 있든 없든 생성하고, 데이터는 바이너리를 메모리에서 만들어 생성된 리더에 그대로
넘깁니다. 파일은 쓰지 않습니다. 그래서 **규칙이 보는 데이터는 게임이 읽을 데이터와 같은 코드로
읽힌 것**입니다.

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

**승격되지 않는 것이 `Info`를 경고와 가릅니다.** 오류가 될 수 있는 보고는 판정이고, `Info`는
기록입니다. 그래서 「이 규칙은 이 로케일에 해당하지 않아 건너뜁니다」는 `Info`입니다 — 조용히
`return`하는 것과 다릅니다.

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
context.Error(table, "...");                       // 테이블 마커 셀 (Schema 뷰)
context.Error("...");                              // 규칙 파일 자신
```

`Warn`과 `Info`도 같은 집합입니다.

**필드 이름을 `nameof`로 넘기는 이유**는 컴파일러가 확인하게 하려는 것입니다. 문자열 리터럴이면
컬럼 이름이 바뀌어도 조용히 통과합니다.

> 셀 위치는 **되찾은 것**입니다. 생성된 레코드는 데이터만 들고 있으므로(게임이 쓰는 타입이고
> 검증 때문에 바꾸지 않습니다), 컨텍스트가 레코드의 타입 → 테이블, 기본 인덱스 → 로우,
> 필드 이름 → 컬럼으로 조회합니다. 접힌 배열·레코드 그룹·매트릭스 표는 이 역이 정확하지 않아
> **그룹의 첫 컬럼**을 가리킵니다 — 원소를 알면 네 번째 인자로 넘기세요.

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

`Option(key)`가 없는 키에 대해 **오류를 내는 이유**는, 빈 문자열로 조용히 답하면 로케일 비교가
아무것과도 맞지 않아 **아무것도 검사하지 않는 규칙**이 되기 때문입니다.

## 9. 시트 밖의 파일

```csharp
// 폴더 하나를 이름으로 조회. 확장자와 대소문자는 무시합니다.
var icons = context.Files(context.Option("ContentRoot"), "*.uasset");

if (!icons.Has(row.IconName))
    context.Error(row, nameof(row.IconName), $"`{row.IconName}` 에셋이 없습니다.");

// 테이블이 아닌 일반 JSON.
var policy = context.Json(context.Option("ContentRoot") + "/policy.json");
```

`FileMap`은 `Has(name)` · `PathOf(name)` · `Names` · `Count`를 냅니다. 폴더와 패턴마다 한 번
스캔해서 실행 내내 공유하므로, 로우마다 물어도 스캔은 한 번입니다.

**에셋이 있는지는 이 도구가 판정하는 것이 아닙니다** — 코어는 `.uasset`이 무엇인지 모릅니다.
폴더를 건네주고, 무엇을 확인할지는 프로젝트의 규칙이 정합니다.

## 10. 런타임 검증 — 저장소와 대조

`rules/runtime/` 폴더의 규칙만 외부 저장소를 열 수 있습니다.

```csharp
// validation/rules/runtime/CashShop.cs
var live = context.Db("Live").Set<int>("SELECT product_id FROM live_products");

context.Info($"라이브 상품 {live.Count}건과 대조합니다.");

foreach (var row in Tables.CashShop.Records)
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

연결 문자열의 **스킴이 종류를 나타냅니다** — `mysql:` · `postgres:` · `redis://`. ADO 연결 문자열과
Redis 설정 문자열은 모양으로 구별되지 않으므로 추측하지 않습니다.

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

`rules/shared/`는 하위 폴더를 가질 수 있습니다 — 아무것도 스스로 실행되지 않으므로, 하위 폴더는 단계가
없는 규칙이 아니라 그냥 정리입니다.

> **`static` 상태를 `rules/shared/`에 두지 마세요.** 컴파일이 파일마다 독립이므로 파일 사이에 쌓이지
> 않습니다. 여러 테이블을 가로지르는 누적은 `rules/global/`의 한 파일에 두세요.

## 12. 실행

|옵션|무엇|
|--|--|
|`--validate-only`|검증까지만. 산출물을 만들지 않습니다. PR 검사용|
|`--skip-runtime-validation`|`rules/runtime/`만 건너뜁니다. 건너뛴 규칙 수가 기록에 남습니다|
|`--list-validators`|규칙을 실행 순서대로 출력하고 종료. 시트를 읽지 않습니다|

검증을 끄는 옵션은 **없습니다.** recipe에서 `Path`를 비우는 것이 유일한 방법이고, 그것은 diff에
남습니다.

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
|`pre` · `global` · `runtime`만|`rules/tables/`는 병렬이고 규칙이 테이블 단위라 선언할 순서가 없습니다. 여기에 달면 **무시가 아니라 오류**입니다 — 조용히 넘어가면 단 사람은 적용된 줄로 읽습니다|
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

시트의 `:required`로는 적을 수 없습니다 — 다른 컬럼의 값에 달려 있기 때문입니다.

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

파일 안에서 로우를 가로질러 상태를 모으는 것은 그대로 됩니다. 파일 **사이**는 아닙니다.

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
foreach (var pool in Tables.RewardDropPool.Records.GroupBy(row => row.PoolId))
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

## 16. 이식할 때 — 원본을 옆에 두세요

기존 검증을 옮기는 것이라면, **원본을 규칙 파일의 주석으로 붙여두세요.** 이식은 번역이 아니라
대조이고, 대조는 원본이 옆에 있을 때만 됩니다.

[samples/named-range/validation/](../samples/named-range/validation/)이 그 형태입니다 — 규칙마다 원본 Lua가 위에
그대로 있고, 달라진 곳에는 왜 달라졌는지가 붙어 있습니다.

```csharp
// 판매 가능한 아이템에는 판매가가 있어야 합니다.
//
//   local function CheckSellPrice(row)
//     -- isNotSell이 false일 때 sellPrice가 없으면 오류
//     if row.isNotSell == false and not row.sellPrice then
//       ...
//
// 원본의 `not row.sellPrice`는 「값이 없다」와 「0이다」를 구별하지 못합니다.
// `HasSellPrice`가 그 구별입니다 — 옵셔널 컬럼의 존재 여부가 와이어에 담겨 있기 때문입니다.

if (row.HasIsNotSell && !row.IsNotSell && !row.HasSellPrice)
    context.Error(row, nameof(row.SellPrice), "판매 가능한 아이템에는 sellPrice가 필요합니다.");
```

이 주석이 실제로 값을 낸 자리가 셋 있었습니다.

|무엇|
|--|
|**규칙이 나아진 곳을 눈에 보이게 합니다.** 위의 옵셔널 구별이 그렇습니다 — 주석이 없으면 「그냥 그렇게 썼다」로 읽힙니다|
|**원본의 버그를 그대로 드러냅니다.** `custom`이 없을 때 오류를 낸 뒤 그대로 내려가 `nil`을 키로 쓰던 것이 원본에 있었습니다. 고친 것이지 옮긴 것이 아니라는 사실이 남아 있어야 합니다|
|**옮기지 않은 것을 옮기지 않았다고 적습니다.** 상수 38개 중 규칙이 쓰는 넷만 옮겼습니다. 원본이 주석에 있으니 필요할 때 꺼내면 됩니다|

## 17. 편집기 자동 완성

**됩니다. 그리고 clone 직후부터 됩니다** — 받은 사람이 변환을 한 번 돌릴 필요가 없습니다.
손으로 할 것은 검증 폴더를 편집기에서 여는 것뿐이고, 그것도 편집기 쪽 사정입니다.

### 검증 폴더에 커밋되는 것

|무엇|왜|
|--|--|
|`Validation.csproj`|규칙을 액세서에 대고 컴파일하는 프로젝트. `Tables.Item.MaxStack`이 타이핑 중에 해석되는 이유입니다|
|`lib/Tabbit.Validation.dll` · `.xml`|`context.`이 해석되는 이유. 요약 문서가 함께 있어 툴팁에 설명이 나옵니다|
|`lib/Tabbit.Rules.Data.dll` · `.xml`|생성 액세서. `Tables.`와 `context.Tables.`가 해석되는 이유입니다|
|`lib/Newtonsoft.Json.dll`|계약의 `Json()`이 `JToken`을 내므로 그 타입이 있는 어셈블리도 필요합니다. 패키지 참조가 아니라 파일로 두는 이유는 위와 같습니다 — 받은 사람이 아무것도 복원하지 않아도 성립해야 합니다|

액세서는 **어셈블리로** 들어갑니다. 그 소스는 실행할 때 임시 폴더에서 컴파일되고 지워집니다 —
아무도 편집하지 않고 컴파일러만 읽던 파일이라, 테이블마다 한 장씩 프로젝트에 둘 이유가
없었습니다.

**`context.Tables`가 성립하는 방식도 여기 있습니다.** 계약 어셈블리는 이 도구와 함께 만들어지고
액세서는 남의 시트에서 실행 중에 만들어지므로, 계약이 액세서의 타입을 선언할 방법이 없습니다.
그래서 계약에는 형 없는 슬롯 하나만 두고, **생성된 어셈블리가 그 위에 확장 속성을 얹습니다** —
`context.Tables`는 그 확장이고, 돌려주는 것은 생성된 스냅숏 타입이라 필드가 전부 typed입니다.
슬롯 쪽은 자동 완성에서 숨겨 둡니다. 규칙이 쓸 이름은 그 옆의 것 하나입니다.

확장 속성은 C# 14의 것이고, 이 어셈블리는 호스트에서만 컴파일되고 게임에 실리지 않으므로
유니티 하한과 무관합니다.

### 빌드가 곧 검증

편집기에서 이 프로젝트를 빌드하면(대개 `Ctrl+Shift+B`) **두 가지가 한 번에** 일어납니다.

1. 컴파일러가 규칙을 검사합니다 — 없는 컬럼, 없는 enum 라벨은 여기서 걸립니다.
2. 그 다음 도구가 규칙을 **실제 데이터에 대고** 돌립니다. 보고가 빌드 출력에 그대로 나오고,
   검증이 실패하면 **빌드가 실패합니다.**

터미널로 옮겨가지 않고 규칙을 고치고 바로 돌려볼 수 있습니다.

|알아둘 것|내용|
|--|--|
|`tabbit`이 **PATH에 있어야** 합니다|없으면 규칙 컴파일까지만 하고 「PATH에 없다」고 경고합니다. 검증이 통과한 것과 구분됩니다|
|끄려면|`dotnet build -p:TabbitValidate=false`. 규칙을 쓰는 중이라 데이터 쪽 보고가 뻔할 때 씁니다|
|경로|프로젝트가 적는 것은 전부 상대경로입니다 — 그래서 이 파일이 커밋 가능합니다|

셋 다 이 도구가 쓰지만 **커밋 대상**입니다. 프로젝트가 가리키는 것이 전부 자기 폴더 안에 있으므로
받은 사람의 기계에서 그대로 성립합니다 — 전에는 도구가 있던 머신의 절대경로를 가리켜서 그럴 수
없었습니다.

**바이트가 달라졌을 때만 다시 씁니다.** 아무것도 바뀌지 않은 실행이 저장소에서 변경으로 보이지
않게 하기 위해서입니다. 스키마가 바뀌면 `lib/`의 액세서에 diff가 나는데, 그것은 규칙이 할 수 있는
말이 실제로 달라졌다는 신호입니다.

빌드 산출물은 `.build/` 한 곳으로 갑니다. `bin/`·`obj/`가 규칙 폴더 옆에 생기면 열어보는 사람이
가장 먼저 보는 것이 그 둘이 되기 때문이고, 프로젝트가 스스로 그렇게 지정하므로 **무시 규칙이
필요한 것이 아니라 애초에 생기지 않습니다.**

끄려면 이렇게 합니다. 아래 각주의 오래된 Visual Studio가 그 경우입니다.

```jsonc
"Validation": {
  "Path": "./validation",
  "EmitIdeProject": false
}
```

프로젝트가 **검증 폴더의 루트**에 있고 점으로 시작하는 폴더 안이 아닌 것도 이유가 있습니다 —
편집기는 프로젝트를 찾을 때 그런 폴더를 건너뜁니다. 숨긴 폴더에 둔 프로젝트는 어느 편집기도 찾지
못하는 프로젝트입니다.

### 편집기에서 여는 폴더

**VS Code에서 `validation/` 폴더를 열거나 워크스페이스 폴더로 추가하세요.**

저장소 루트를 열면 되지 않습니다. C# Dev Kit은 워크스페이스에서 솔루션·프로젝트를 하나 골라
로드하고, 루트에는 `Tabbit.slnx`가 있어 그것이 우선합니다. 검증 폴더를 열면 그 안의
`Validation.csproj`가 유일한 후보가 됩니다.

|확인|무엇이 보이면 되는 것인가|
|--|--|
|솔루션 탐색기|`Validation` 프로젝트와 그 아래 `Dependencies`에 `tabbit`|
|`Tables.` 을 타이핑|테이블 목록. 이어서 `.` 을 치면 그 테이블의 컬럼|
|`context.` 을 타이핑|`Error` · `Warn` · `Info` · `Option` · `Files` · `Db` · `Redis`|
|경로 표시(breadcrumb)|`ItemRules.cs > ItemRules > Validate() : void` — 의미 분석이 되고 있다는 표시|

> **Visual Studio 2022 이하는 대상이 아닙니다.** 이 저장소가 쓰는 프레임워크보다 오래된 버전이라
> 프로젝트를 열지 못합니다 — `Microsoft.NET.Sdk`를 찾을 수 없다는 오류가 나고, `src/Tabbit.csproj`
> 도 같은 이유로 열리지 않습니다.

### 규칙 파일이 이 모양인 이유

**자동 완성이 이 모양을 정했습니다.** 처음 형태는 클래스 없이 최상위 문장을 쓰는 파일이었고,
그것이 자동 완성을 얻지 못한다는 것을 실측으로 확인했습니다 — 규칙 파일 셋에 존재하지 않는
심볼을 하나씩 넣고 컴파일러가 그것을 검출하는지 봤습니다. 검출한다면 의미 분석이 되고 있다는 뜻입니다.

|규칙 파일 형태|보고된 오류|해석|
|--|--|--|
|최상위 문장|`CS8802` 4개. **`CS0103` 0개**|최상위 문장이 컴파일 단위 하나에만 허용되므로 나머지 파일은 **본문이 바인딩되지 않습니다**|
|클래스로 감싼 형태 (지금)|**`CS0103` 6개** — 세 심볼 전부|전부 분석됩니다|

`#:project`로 프로젝트를 가리키는 파일 기반 프로그램 방식도 시도하였고 되지 않았습니다 — 그것은
C# 문법이 아니라 SDK가 컴파일 전에 걷어내는 지시자여서, 편집기의 Roslyn은 1행의 구문 오류로 보고
그 파일의 컴파일을 중단합니다. 남는 것은 **평범한 클래스와 평범한 `using` 두 줄**이고, 그것이 지금 모양입니다.

잃은 것은 「파일을 열면 바로 검사문」이라는 성질이고, 얻은 것은 컬럼 378개를 기억하지 않아도 되는
것입니다. 그 교환은 할 만합니다.

### 준비 없이도 검출되는 오타

위의 둘을 하지 않아도 **컴파일러가 검출합니다.** 편집기가 아니라 실행에서 나옵니다.

```
[rules/tables/ItemRules.cs] 'Grade'에는 'Legend'에 대한 정의가 포함되어 있지 않습니다.
    at .../validation/rules/tables/ItemRules.cs(17,33)
```

없는 컬럼·없는 enum 값·타입이 맞지 않는 비교가 **데이터를 한 줄도 읽기 전에** 파일과 줄 번호로
보고됩니다. 자동 완성은 타이핑 중에 알려주는 편의이고, 잘못을 검출하는 것은 어느 쪽이든 됩니다.

## 18. 검증 자체를 검증하기

규칙이 늘면 **그것들이 옳은지 누가 보는가**가 남습니다. 잘못된 검증은 통과시키거나, 더 나쁘게는
옳은 데이터를 차단합니다.

이 저장소가 쓰는 방법은 픽스처 워크북과 기대 진단을 짝지어 게이트로 두는 것입니다 —
[test/fixtures/validation/](../test/fixtures/validation/)에 그 예가 있고,
[ValidationPipelineTests](../test/Tabbit.Tests/ValidationPipelineTests.cs)와
[ValidationRuntimeTests](../test/Tabbit.Tests/ValidationRuntimeTests.cs)가 심각도별 동작과
**산출물이 실제로 없는 것**을 확인합니다.

규칙별 억제 목록은 **두지 않습니다.** 조용한 통과를 recipe에 적어두는 장치이고, 억제가 필요한
규칙은 규칙이 틀린 것입니다.
