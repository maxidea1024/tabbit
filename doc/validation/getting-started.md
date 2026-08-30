# 시작하기와 폴더 규약

> [「정적 검증」으로 돌아가기](../validation.md)

---

## 3. 시작하기

### recipe에 세 줄

```jsonc
"Validation": {
  "Path": "./validation"
}
```

`Path`를 비우면 검증이 꺼집니다.

그것이 끄는 유일한 방법이고, diff에 남습니다.
명령줄로 끌 수 있는 게이트는 아무도 신뢰할 수 없기 때문입니다.

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

이 파일에 감춰진 것이 없습니다.

규칙이 쓰는 이름 둘이 `using` 두 줄로 파일 안에 있고, 진입점은 `Validate` 하나이고, 규칙이 보고에
쓰는 것은 그것이 받는 인자입니다.

호스트가 암묵적으로 제공하는 것에 기대는 줄이 하나도 없다는 뜻입니다.
그래서 편집기가 이 파일을 열자마자 전부 해석합니다 (§17).

### 확인

```
tabbit --recipe recipe.json --validate-only
```

검증까지만 돌고 산출물을 하나도 만들지 않습니다. PR 검사가 사용하는 방식입니다.

> **돌아가는 규칙 폴더가 저장소에 있습니다.**
>
> [test/reserved-words/validation/](../../test/reserved-words/validation)이고, `rules/` 아래 `pre`, `tables`,
> `global`, `shared` 각각 한 파일입니다.
>
> 같은 디렉터리에 그 시트를 모든 언어로 생성한 결과가 함께 커밋되어 있으므로, 규칙이 읽는
> `context.Tables.Package.Records`가 실제로 어떤 타입인지 옆에서 확인할 수 있습니다.

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

`rules/` 하나가 경계입니다. 그 아래는 사람이 쓴 것이고 나머지는 도구가 씁니다.

단계 폴더가 `lib/`, `.build/`, `.csproj`와 나란히 있던 때에는 목록만 보고 어느 쪽인지 알 수
없었습니다.

> 이전 배치(단계 폴더가 `validation/` 바로 아래)로 된 폴더는 **오류로 보고하고 옮기라고 안내합니다.**
> 그대로 지나가면 규칙이 하나도 돌지 않은 실행이 통과한 실행과 똑같이 보입니다.

|규칙|이유|
|--|--|
|네 폴더는 **평평합니다**. 하위 폴더의 파일은 실행되지 않습니다|폴더가 실행 시점과 받는 것을 정하므로, 한 단 아래의 파일은 단계가 없는 규칙이 됩니다. 정리하고 싶으면 `rules/shared/`를 쓰세요|
|`rules/tables/`의 파일은 `<테이블>Rules.cs`여야 하고, `X` 테이블이 없으면 **오류**입니다|테이블 이름이 바뀌면 규칙이 말없이 안 도는 것이 아니라 파일을 따라 옮기라고 안내합니다. 접미사는 이름이 곧 대상인 자리에서 그 대상을 나머지와 갈라주고, 생성된 `Item.cs`와 규칙 `ItemRules.cs`가 한 프로젝트에서 같은 이름이 되지 않게 합니다|
|`rules/` 아래에 이 다섯 외의 폴더가 있으면 **오류**입니다|`table/`은 규칙이 하나도 돌지 않는 폴더이고, 산출물의 어디에도 그 사실이 남지 않습니다. `#`로 시작하면 건너뜁니다|
|`Path`가 있는데 폴더가 없으면 **오류**입니다|오타 하나로 검증 전체가 아무 표시 없이 통과합니다|
|규칙 파일이 하나도 없으면 **경고**입니다|빈 폴더는 프로젝트의 시작일 수 있지만, 아무 표시 없이 지나가서는 안 됩니다|

### 폴더마다 있는 시작 파일

각 폴더에 `_....cs.template` 파일이 하나씩 있습니다.

복사해서 `.cs`로 이름을 바꾸면 그 폴더의 규칙이 됩니다.

머리말에 그 폴더의 규약이 적혀 있습니다. 파일 이름, 진입점, 그 단계에서 쓸 수 있는 것과 없는 것,
우선순위입니다. 이 문서를 다시 찾지 않아도 되도록 폴더 안에 둔 것입니다.

`.template` 확장자인 이유가 있습니다.

폴더의 `.cs`는 전부 규칙으로 컴파일되므로, 빈 시작 파일을 `.cs`로 두면 진입점이 없다는 오류로
실행이 멈춥니다. `.template`은 스캔 대상이 아니라서 안내문이 폴더 안에 있을 수 있습니다.

> 파일이 하나씩 들어 있으므로 **빈 폴더도 git에 남습니다.** 받은 사람이 폴더 구조를 그대로
> 봅니다.

## 5. 규칙 파일의 형태

평범한 `.cs` 파일이고, 프로젝트에 등재되지 않습니다.

클래스 하나에 `Validate` 하나이고, 파일 이름과 클래스 이름이 같습니다.
`ItemRules.cs`의 `ItemRules`입니다.

|정통 소스|규칙 파일|
|--|--|
|네임스페이스 · 클래스 · 진입점|클래스와 `Validate`. 인자 타입은 폴더가 정합니다(§5.1). 네임스페이스는 두지 않습니다|
|빌드가 컴파일|**변환기가 실행 시점에** 컴파일|
|프로젝트 참조|호스트가 줍니다 — 생성 액세서와 이 어셈블리|
|다른 파일과의 결합은 프로젝트가|`rules/shared/`에 두면 호스트가 같은 컴파일에 넣습니다|

`System`, `System.Linq`, `System.Collections.Generic`은 호스트가 열어 둡니다. 그 셋뿐입니다.

규칙이 실제로 쓰는 이름 둘은 파일에 적힌 `using` 두 줄입니다.
`Tabbit.Rules`가 `Tables`이고, `Tabbit.Validation`이 컨텍스트 타입들입니다.

그 둘을 파일에 두는 것이 편집기가 파일을 해석할 수 있게 하는 조건입니다(§17).
실행만 생각하면 없어도 되지만, 없으면 편집기에서 아무것도 되지 않습니다.

> `rules/pre/` 규칙만 `using Tabbit.Rules;`를 쓰지 않습니다 — 시트를 읽기 전이라 액세서가 아직 없고,
> 그 줄은 컴파일 오류가 됩니다. `--new-validator`가 써주는 것이 폴더에 맞는 머리말입니다.

### 5.1 폴더가 정하는 인자 타입

|폴더|`Validate`이 받는 것|그 단계에만 있는 것|
|--|--|--|
|`rules/pre/`|`IPreContext`|— (보고 · `Option` · `Files` · `Json`뿐)|
|`rules/tables/`|`ITableContext`|`context.Table` — 그 파일이 담당하는 테이블의 스키마|
|`rules/global/`|`IGlobalContext`|`context.Schema`와 행·컬럼을 짚는 보고|
|`rules/runtime/`|`IRuntimeContext`|`context.Db()` · `context.Redis()`|

타입이 곧 그 단계의 경계입니다.

넷은 겹쳐 있습니다. `IPreContext`가 가장 좁고, `IGlobalContext`가 데이터를 더하고,
`ITableContext`와 `IRuntimeContext`가 각각 그 위에 하나씩 더합니다.

그래서 `rules/shared/`의 헬퍼는 실제로 쓰는 것 중 가장 좁은 타입을 받으면 그 아래 단계 전부에서
호출됩니다.

전에는 넷이 같은 타입을 받았고, 폴더에 맞지 않는 것을 호출하면 컴파일된 뒤 실행 중에 「어느
폴더로 옮기라」는 메시지가 나왔습니다.

지금은 이름 자체가 없으므로 타이핑하는 동안 편집기가 표시합니다.
실행에서 만나더라도 보고에 그 이름을 가진 폴더가 함께 적힙니다.

```
[rules/tables/ItemRules.cs] 'ITableContext'에는 'Db'에 대한 정의가 포함되어 있지 않고 …
    `Db` is on the context `rules/runtime/` hands over, and this file is in `rules/tables/`.
```

> 호스트가 넘기는 객체 하나가 넷을 다 구현하므로 **캐스팅으로 우회할 수는 있습니다.** 그렇게 해도
> `Db()`는 단계를 확인하고 거부합니다 — `--skip-runtime-validation`이 의미를 가지려면 저장소를 여는
> 규칙이 그 폴더 안에만 있어야 하기 때문입니다.

컴파일 단위는 규칙 파일 하나입니다.

언어의 제약이 아니라 호스트가 그렇게 나눈 것이고, 얻는 것이 셋입니다.

- `rules/shared/`의 `static` 상태가 파일마다 독립이라 병렬 실행에 경합이 없습니다.
- 한 파일의 컴파일 오류가 다른 파일을 막지 않습니다.
- 파일 사이에 상태를 공유할 수 없습니다.

**아래 사례들은 `Validate`의 본문만 보여줍니다.** 감싸는 클래스는 위와 같고 매번 반복하지 않습니다.

> **검증 폴더를 다른 C# 프로젝트 트리 안에 두지 마세요.** 부모 프로젝트가 `**/*.cs`로 이 파일들을
> 걷어가면 `Tables`를 참조하지 못해 부모 빌드가 깨집니다. 시트 옆이 제자리이고, 편집기용 프로젝트가
> 필요하면 이 도구가 검증 폴더에 써줍니다 (§17).
