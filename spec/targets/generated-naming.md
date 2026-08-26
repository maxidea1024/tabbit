# 생성 코드의 이름 체계

> 상태: **구현 완료** (2026-08-14) · 근거 조사 2026-08-14 (모든 언어 전수 실측) · 상위 계획:
> [검증 사용성과 C# 어셈블리 산출](../validation/validation-usability-and-assembly-output.md)의 9단계

**세 가지를 바꿉니다.**

1. **`AccessorName`이 실제로 액세서 타입의 이름을 정하게 합니다.** 지금은 모든 언어 중 5개
   에서만 그렇게 동작하고, 7개는 파일 이름만 바꾸며, TypeScript는 아무 일도 하지 않습니다.
2. **기본값을 하나의 정본에서 파생시킵니다.** 지금은 액세서 기본값이 4갈래, 네임스페이스류
   기본값이 4갈래입니다.
3. **사실과 다른 문서를 고칩니다.** 옵션 설명이 틀렸고, C# 안내서의 예제 코드는 컴파일되지
   않습니다.

옵션 키 이름이 언어마다 다른 것(`Namespace` · `PackageName` · `CrateName` · `ModuleName`)은
**바꾸지 않습니다** — 각 언어의 고유 용어를 따른 결과이고 `doc/recipe.md`가 이미 표로
정리해 인정하고 있습니다. 통일의 대상은 키 이름이 아니라 **의미와 기본값**입니다.

---

## 1. 현재 상태 — 실측

### 1.1 `AccessorName`이 지키지 않는 약속

같은 키가 언어마다 네 가지 서로 다른 일을 합니다.

|`AccessorName`의 실제 역할|언어|
|--|--|
|액세서 **타입**의 이름|Java · Kotlin · PHP · Unreal · C(전 식별자 접두사)|
|**파일 이름만**. 타입은 `Tables`로 템플릿에 고정|C# · C++ · Go · Ruby · Dart|
|**아무 역할 없음**|TypeScript|
|옵션 자체가 없음|Rust · Python(모듈 이름이 대신) · HTML|

여러 언어의 템플릿에 `Tables`가 리터럴로 고정되어 있습니다(`csharp-accessor.sbn:15` ·
`cpp-accessor.sbn:4` · `ts-tables-set.sbn:10` · `go-accessor.sbn:4` ·
`rust-accessor.sbn:53` · `python-accessor.sbn:3` · `ruby-accessor.sbn:4` ·
`dart-accessor.sbn:7`).

**실측 증거.** `side-by-side.json`이 C#에 `AccessorName: "A"`를 지정하였으나 산출물은 파일
`csharp/A.cs`에 `public partial class Tables`입니다 — 파일 이름과 타입 이름이 어긋난 채
커밋되어 있습니다. 골든 픽스처도 같습니다(`golden/key-types/csharp/KeyTypesAccessor.cs`에
`class Tables`).

**사용자가 이미 우회하고 있습니다.** 어떤 샘플 recipe는 java · kotlin · php ·
csharp · cpp에 `AccessorName: "Tables"`를, ruby · dart에 `"tables"`를 명시합니다. 하드코딩된
타입 이름에 파일 이름을 손으로 맞춘 것입니다.

**검증 경로도 우회하고 있습니다.** `RuleAccessor.cs`가 `AccessorName = "Tables"`를 주는
이유가 이것이고, 같은 파일의 주석은 그 `Tables`가 사용자의 csharp 타깃 `Tables`와 충돌할 수
있어 네임스페이스를 분리하였다고 적고 있습니다 — 이름 고정이 실제로 충돌을 발생시키고
있음을 코드가 자인하는 지점입니다.

### 1.2 기본값의 분산

|축|갈래|
|--|--|
|액세서 기본값|`TabbitAccessor`(C#·C++·TS) · `TabbitData`(C·Java·Kotlin·PHP) · `tabbit_data`(Go·Ruby·Dart) · `FTabbitData`(Unreal) · 없음(Rust·Python)|
|네임스페이스류 기본값|`""`(C#·C++·TS) · `gamedata`(Go·Java·Kotlin·Python·Rust) · `GameData`(PHP·Ruby) · `TabbitData`(Unreal)|

한 픽스처(`golden/key-types/`)의 14개 산출물에서 액세서 이름이 5가지로 갈립니다 —
`KeyTypesAccessor` · `KeyTypes` · `FKeyTypes` · `tabbit_data` · `tables`.

### 1.3 사문화된 옵션

`TypescriptRecipe.AccessorName`은 선언되어 있으나 생성기가 한 번도 읽지 않습니다. 그럼에도
스캐폴딩 스타터 레시피(`src/recipes/web.jsonc`)가 이 값을 설정하고, `doc/recipe.md`와
`doc/languages/typescript.md`가 안내합니다.

### 1.4 사실과 다른 문서

|위치|내용|실제|
|--|--|--|
|`doc/recipe.md`|「`AccessorName` — 접근자 클래스와 그 파일」|C#·C++·TS에서 클래스 이름은 바뀌지 않습니다. TS는 파일 이름도 아닙니다|
|`doc/languages/csharp.md`|`AccessorName: "GameData"` 설정 후 `await GameData.ReadAllAsync(...)` 예제|실제 타입은 `Tables`이므로 **이 예제는 컴파일되지 않습니다.** C++·Go·Ruby·Dart 안내서는 올바릅니다|
|`doc/languages/typescript.md`|`Tables.ts`를 생성|실제는 `tables.ts`입니다|

## 2. 설계

### 2.1 하나의 정본과 언어별 표기

레시피는 **정본 이름 하나**를 받고, 각 생성기가 자기 언어의 표기 규칙을 적용합니다. 같은
문자열을 강제하는 것이 아니라 **같은 곳에서 파생되게** 하는 것입니다 — 언어 관용과 통일감이
충돌하지 않는 유일한 지점입니다.

|층|규칙|
|--|--|
|액세서 타입|정본 그대로. 언어가 요구하는 장식만 생성기가 붙입니다 — Unreal은 `F` 접두, C는 식별자 접두사|
|액세서 파일|그 언어의 파일 명명 관례로 정본을 변환합니다 — `Tables.cs` · `tables.go` · `tables.rb`|
|네임스페이스류|정본 하나를 받아 언어별 표기로 변환합니다 — `GameData`(C#·PHP·Ruby) · `gamedata`(Go·Rust·Python·Java·Kotlin)|

정본 액세서 이름의 기본값은 **`Tables`** 로 합니다. 여러 언어가 오늘 실제로 생성하는 이름이고,
샘플이 손으로 맞추고 있는 값이며, 사용 지점이 `Tables.Item.Records`로 읽힙니다.

### 2.2 `AccessorName`의 약속 이행

여러 언어의 템플릿에서 `Tables` 리터럴을 파라미터로 바꿉니다. TypeScript는 사문 상태를 해소해
파일 이름과 타입 이름 모두에 반영합니다. Rust와 Python은 옵션이 없으므로 신설할지, 모듈 이름
옵션이 그 역할을 계속할지 정합니다(§5).

이 변경으로 `RuleAccessor`의 우회가 필요 없어집니다 — 검증 액세서가 자기 이름을 가질 수 있게
되므로, 충돌 회피가 네임스페이스 분리에만 의존하지 않습니다.

### 2.3 6단계와의 동일 주기

[접근자 객체화](accessor-instances.md)가 액세서의 타입 표면을 다시 씁니다. 이름 고정을 푸는
작업도 같은 템플릿의 같은 자리를 만지므로, 두 변경을 한 골든 주기에 묶어 재기록을 한 번으로
만듭니다. 그리고 7단계의 dll이 이 이름들을 어셈블리 경계로 굳히므로 그보다 앞서야 합니다.

## 3. 다른 스펙의 미결 해소

이 체계가 정해지면 흩어져 있던 이름 결정 3건이 함께 정해집니다.

|미결|위치|이 체계에서|
|--|--|--|
|계약 어셈블리 이름|[검증 사용성 §5a](../validation/validation-usability-and-assembly-output.md)|규칙 작성자에게 열리는 표면의 이름. 생성 액세서의 네임스페이스와 충돌하지 않는 자리|
|인스턴스 타입 이름|[접근자 객체화 §5](accessor-instances.md)|정본에서 파생. 정적 파사드와 다른 타입이어야 하므로 파생 규칙이 필요합니다|
|코드 생성 dll의 어셈블리 이름|[검증 사용성 §7](../validation/validation-usability-and-assembly-output.md)|네임스페이스류 정본에서 파생|

## 4. 파급

- **골든·샘플·비교본** — 액세서 파일 이름과 타입 이름이 바뀌므로 전 언어 재기록입니다.
  기본값을 쓰던 산출물이 특히 많이 바뀝니다.
- **샘플 레시피의 정리** — 출시 전 소규모 프로젝트가 손으로 맞추던 `AccessorName` 지정이 기본값과
  같아지므로 제거 대상입니다.
- **문서** — §1.4의 오류 3건 수정, 옵션 설명 갱신.
- **소비자** — Java·Kotlin·PHP·C·Unreal은 기본값을 쓰던 경우 타입 이름이 바뀝니다. 외부
  사용자가 없으므로 일괄 전환합니다.

## 5. 구현에서 정한 것과 남은 것

|항목|정한 것|
|--|--|
|Rust·Python의 액세서 옵션|**신설했습니다.** 두 언어 모두 `AccessorName` 기본값 `Tables`입니다. Rust는 모듈 이름이 여기서 파생되고(`AccessorModule`은 snake_case), Python은 `ModuleName`이 파일을 따로 정합니다|
|표기 변환|**타입은 `ToPascalCase()`, 파일은 언어 관례**입니다. 생성기마다 `AccessorType`·`AccessorFile`로 갈라 두었습니다. Go·Ruby·Dart·Rust·TypeScript는 snake/kebab 파일, 나머지는 타입과 같은 이름의 파일입니다|
|C의 접두사|정본을 그대로 접두사로 씁니다. 기본값이 `Tables`이므로 `Tables_ItemRecord_t` 형태가 되고, 전역 식별자 공간이 걱정되는 프로젝트는 이름을 바꿔 지정합니다|
|Unreal의 장식|기본값을 `FTables`로 두었습니다 — 엔진 관례가 접두사를 강제하므로 정본에 담습니다|
|레코드 타입 이름|**현행 유지**입니다. 언어 관용 축이므로 이번 범위 밖입니다|

|남은 것|왜 미루었나|
|--|--|
|네임스페이스류 기본값 통일|`Namespace`·`PackageName`·`CrateName`의 기본값이 `""`·`gamedata`·`GameData`로 갈려 있습니다. C#·C++·TS의 `""`는 「비우면 전역」이라는 **의도된 선택**이라, 이름 축과는 다른 판단이 필요합니다|
|Java·Kotlin·PHP의 파일 이름|타입과 같은 이름을 그대로 쓰고 있어 이번 변환의 영향을 받지 않았습니다. 언어 관례상 맞으므로 그대로 둡니다|

## 6. 범위 밖 — 함께 발견된 것

- **레시피 구조의 분산.** 14개 코드생성 타깃 중 4개(cpp · csharp · typescript · html)만
  `CodeGenerations.*` 전용 섹션을 갖고, 나머지 10개는 `Targets[]` 리스트로만 도달합니다.
  같은 성격의 옵션이 레시피 안에서 두 군데에 있습니다.
  → **해소되었습니다.** 전용 섹션을 없애고 `Targets` 하나로 통일하였습니다
  ([설계](../ops/target-section-unification.md)).
- **`Namespace` 키의 과적재.** 코드 네임스페이스 외에 텍스트 익스포트의 그룹 토큰도
  `Namespace`이고, 데이터베이스 익스포터는 같은 개념을 `NamePrefix`로 부릅니다.
- **파일 배치의 갈래.** `tables/`·`enums/` 하위 폴더를 쓰는 언어와 평평한 언어가 갈리는데,
  같은 JVM인 Java는 평평하고 Kotlin은 하위 폴더입니다.

## 7. 게이트

- 전 언어에서 `AccessorName`을 지정했을 때 **타입 이름과 파일 이름이 함께** 그 값을 따르는지.
- 아무것도 지정하지 않았을 때 전 언어의 액세서 이름이 하나의 정본에서 파생되는지.
- 문서의 예제 코드가 실제 산출물과 맞는지 — C# 안내서의 예제가 컴파일되는지.
- 기존 게이트(전 언어 비교본 · 골든)가 재기록 후 통과하는지.

EOD
