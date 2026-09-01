# 5단계 — clone 직후 자동 완성

> [「검증 사용성과 어셈블리 출력」으로 돌아가기](../validation-usability-and-assembly-output.md)

---

## 5. 검증 IDE 체계 — clone 직후 자동완성 (**5a 구현 완료**)

### 현재 구조

clone 직후 자동완성을 차단하는 요인은 세 겹입니다.

1. `Validation.csproj`가 gitignore 대상입니다 — clone에 프로젝트 파일이 없습니다.
2. 참조가 머신 종속입니다 — HintPath는 `typeof(Context).Assembly.Location`, 즉 그 실행이
   돌았던 위치의 기록입니다(`RuleScaffold.cs:131`). 규칙 API가 도구 본체 `tabbit.dll` 안에
   있어 참조가 설치 위치에 결속됩니다. 단일 파일 배포본에서는 `Location`이 빈 문자열이라
   `<Reference>` 블록 자체가 생략됩니다.
3. `Tables.` 자동완성의 원천인 `.generated/`도 gitignore 대상입니다.

### 5a. 규칙 API의 계약 어셈블리 분리

|비교|(가) 계약 어셈블리 물리 분리|(나) 본체의 참조 어셈블리 동봉|
|--|--|--|
|방법|`Context`·`SchemaView`·`FileMap`·`SqlStore`·`RedisStore` 표면을 별도 프로젝트로. 본체가 프로젝트 참조|본체 빌드의 `ProduceReferenceAssembly` 산출물을 내장 후 실행 시 기록|
|API 계약|명시됩니다 — 규칙 작성자 표면이 어셈블리 경계|본체 public 전체가 그대로 노출(현행과 동일)|
|구조 변경|중규모 — `Context` 구현이 본체 내부(셀 역참조·모델)와 결합되어 있어 구현 주입으로 역전 필요|코드 변경 최소|
|빌드 복잡성|없음|2단계 빌드 — 자기 ref asm을 자기 리소스로 넣는 순환을 빌드 스크립트로 풀어야 합니다|

**(가)로 확정합니다.** 규칙 작성자에게 계약을 주는 것이 피드백의 취지와 일치하고, 빌드
체계를 건드리지 않으며, 4단계의 임베드 대상에 이 dll이 합류합니다. 타입 동일성은 본체가
같은 어셈블리를 참조하므로 자동으로 성립합니다.

### 5a-1. 의존 실측 (2026-08-14) — `src/Validation` 11파일 2,962줄 전수

**판정: 중간 규모 리팩터링. 설계 변경은 아닙니다.**

**공개 시그니처는 이미 거의 닫혀 있습니다.** `Context`의 public 멤버 22개와 협력 타입 6개(공개
멤버 60개)의 시그니처에 나타나는 본체 타입은 **`Newtonsoft.Json.Linq.JToken` 하나뿐**입니다.
`Model` · `Table` · `Field` · `Location` · `Diagnostics` · `Severity` · `TabbitException` ·
`RuleFile` · `CellLocator` · `RuleScope`는 **단 한 곳도 공개 시그니처에 나타나지 않습니다** —
전부 private 필드·private 메서드·internal 생성자에만 있습니다. 무엇을 계약으로 할지는 이미
정해져 있고, 정할 것이 남아 있지 않습니다.

이 상태가 우연이 아니라는 근거가 둘 있습니다. `TargetSide`는 모델의 enum이 아니라
`.ToString()`을 거친 `string`으로 노출되고(`SchemaView.cs:79,145`), 행을 짚는 보고는 생성
레코드를 `object`로 받아 `CellLocator`가 리플렉션으로 역산합니다(`Context.cs:167`,
`CellLocator.cs:78-96`). 둘 다 모델 타입이 규칙 표면에 새지 않게 미리 막아 둔 자리입니다.

**리팩터링인 이유는 구현이 그 타입들 안에 들어 있기 때문입니다.** 7개 공개 타입이 전부
`sealed class` + `internal ctor`로 본체 타입을 생성자에서 직접 받습니다 —
`SchemaView(Model)` · `TableSchema(Table)` · `FieldSchema(TableSchema, Field)` ·
`Context(RuleScope)`. 계약으로 옮기려면 「인터페이스(계약) + 구현(본체)」로 쪼개야 하고, 그러지
않으면 계약이 `Model` · `Diagnostics` · MySqlConnector · Npgsql · StackExchange.Redis를 전부
끌고 갑니다.

|타입|파일|공개 시그니처가 끌고 오는 것|옮길 수 있나|
|--|--|--|--|
|`Context` (22 멤버)|`Context.cs:115-352`|`JToken` · 나머지는 계약 내부 타입과 BCL|인터페이스화 필요. **시그니처는 무손실**|
|`SchemaView` · `TableSchema` · `FieldSchema`|`SchemaView.cs:24-164`|계약 내부 타입뿐|가능|
|`FileMap`|`ExternalFiles.cs:22-64`|없음|**결합 제로.** 그대로 옮길 수 있습니다|
|`SqlStore` · `RedisStore`|`RuntimeStores.cs:21-187`|없음 — BCL 컬렉션만 반환|인터페이스로만. 클래스째 옮기면 계약이 DB 드라이버 2개를 참조합니다|
|`RuleStage` · `RulePriorityAttribute`|`RuleFolders.cs:15-28` · `RulePriorityAttribute.cs`|없음|순수. **규칙 작성자가 직접 쓰므로 계약에 필수**|

**규모.** 계약으로 옮길 것이 7타입 약 633줄(인터페이스와 POCO만 두면 250~300줄), 본체에 남을
것이 약 2,190줄입니다.

### 5a-3. 구현 (2026-08-14)

**인터페이스만 옮겼습니다.** 그 결정 하나가 실측이 꼽은 어려운 지점 둘을 없앴습니다 — 구현
클래스가 전부 제자리에 남으므로 `AssetRoots`가 `FileMap`의 internal 생성자를 부르는 것이
그대로이고, DB 드라이버 3종과 `TabbitException`은 계약에 나타나지 않습니다.

|무엇|어디|
|--|--|
|계약 어셈블리|`src/Contract/` — 어셈블리 이름 `Tabbit.Validation`, **네임스페이스도 `Tabbit.Validation` 그대로**|
|계약의 내용|`IContext` · `ISchemaView` · `ITableSchema` · `IFieldSchema` · `IFileMap` · `ISqlStore` · `IRedisStore` + `RuleStage` + `RulePriorityAttribute` — **이후 `IContext`가 단계별 넷으로 갈렸습니다**(`IPreContext` · `IGlobalContext` · `ITableContext` · `IRuntimeContext`, [파이프라인 스펙](../validation-pipeline.md)의 「단계마다 다른 컨텍스트」)|
|계약의 의존|`Newtonsoft.Json` 하나. 로깅도, 모델도, 드라이버도 없습니다|
|호스트 쪽|기존 클래스가 인터페이스를 구현합니다. 계약 타입을 돌려주는 8개 멤버만 명시적 구현이 필요했습니다|
|`Context`|`internal sealed class RuleContext : IContext`가 되었습니다|

**네임스페이스를 그대로 둔 것이 규칙 파일의 변경을 한 줄로 줄였습니다.** `using
Tabbit.Validation;`이 이제 계약 어셈블리를 가리키므로, 규칙이 바꿀 것은 엔트리 인자 타입
(`Context` → `IContext`) 하나뿐입니다.

**셀 위치는 호스트가 되찾습니다.** 계약이 인터페이스를 돌려주므로 스키마 항목의 `Location`이
표면에서 사라지는데, 그 객체를 만드는 것이 호스트뿐이므로 구현 안에서 구체 타입으로 되돌려
읽습니다(`Context.cs`의 `LocationOf(IFieldSchema)` · `LocationOf(ITableSchema)`). 규칙은 무엇을
가리킬지만 정하고 어디인지는 계산하지 않는다는 성질이 그대로입니다.

**보고 상한 카운터**는 `RuleScope`에 남았습니다 — 계약이 인터페이스만 두므로 상태를 가질 자리가
없고, 카운터는 호스트의 것이 맞습니다.

### 5a-4. 함께 드러난 테스트 하네스의 결함

프로젝트가 하나 늘자 웹서버 테스트가 90초 타임아웃으로 실패했습니다. 원인은 계약 분리가 아니라
**하네스가 CLI를 두 가지 방법으로 실행하고 있던 것**입니다.

|어디|어떻게|
|--|--|
|`TabbitRunner`|`dotnet build -o <전용 폴더>`로 한 번 빌드하고 그 실행 파일을 직접 부릅니다|
|`HistoryServerTests.Serve`|`dotnet run --project`|

두 빌드가 **서로 다른 출력 경로**를 쓰므로 번갈아 실행할 때마다 상대의 증분 상태를 무효화합니다.
그래서 변환 직후에 서버를 띄우는 테스트는 매번 전체 재빌드를 기다렸고, 프로젝트가 하나 늘면서
그 시간이 90초를 넘겼습니다. `dotnet run`을 버린 이유가 `TabbitRunner`의 주석에 이미 적혀
있었는데 — 「프로젝트를 평가하고 빌드를 확인하는 데 2~4초」 — 서버 경로만 남아 있었습니다.

**고친 방법은 같은 실행 파일을 쓰게 한 것입니다.** 부수 효과로 서버 테스트 전부가 전부 빨라졌고
(2초 → 1초 이하, 첫 테스트는 95초 → 4초), 전체 스위트에서 재빌드 왕복이 사라집니다.

### 5a-2. 끊어야 할 지점

|지점|위치|방법|
|--|--|--|
|**본체가 계약 타입의 internal 생성자를 부릅니다**|`src/Cooking/AssetRoots.cs:101` — `new FileMap(...)`|`InternalsVisibleTo` · 공개 팩토리 · 인터페이스 중 하나. **한 곳뿐이고 놓치기 쉬운 자리입니다**|
|스키마 타입의 internal `Location`|`SchemaView.cs:92,151` → `Context.Report`(`:257-265`)|셀 위치를 되찾는 책임을 호스트 쪽으로 옮깁니다. **인터페이스화에서 손이 가장 많이 가는 지점**|
|`Context` 생성 지점|`ValidationPipeline.cs:240-245`|`RuleScope`(7필드 묶음)가 이미 완벽한 주입 지점입니다. 본체 변경은 이 2줄과 생성자 시그니처뿐|
|보고 상한 카운터|`Context.cs:69,83-90` ↔ `ValidationPipeline.cs:263-268`|계약이 구현을 갖는지 인터페이스만 두는지에 따라 자리가 갈립니다 — **결정 필요**|
|`typeof(Context)`로 어셈블리 식별|`RuleCompiler.cs:437-448,285` · `RuleScaffold.cs:42-43,184-195`|분리의 이득이 나오는 자리입니다. 4단계의 순환과 5b의 머신 종속 경로가 전부 이 네 곳에서 옵니다|

**`TabbitException`은 인터페이스 방식이면 자동으로 빠집니다** — throw는 시그니처가 아니므로
계약에 나타나지 않습니다. 클래스째 옮기면 `Models.Location`까지 따라오므로, 이것이
인터페이스 방식을 택할 실무적 이유이기도 합니다.

**`JToken`은 그대로 둡니다.** 자체 타입으로 바꾸면 인덱서·LINQ·명시적 변환을 다시 만들어야
하고 기존 규칙이 깨집니다(`test/fixtures/validation/pass/tables/TestFieldTypesRules.cs:13`이
셋을 동시에 씁니다). 계약이 Newtonsoft를 참조하는 것은 §4가 이미 수용한 결정입니다.

어셈블리·네임스페이스 이름은 미결입니다. 생성 접근자가 이미 `Tabbit.Rules` 네임스페이스와
`Tabbit.Rules.Data` 어셈블리 이름을 쓰고 있으므로 충돌하지 않는 이름이어야 합니다.

### 5b. csproj의 커밋 가능화 — **구현 완료**

|무엇|어떻게|
|--|--|
|계약 동봉|`<검증폴더>/lib/`에 `Tabbit.Validation.dll`과 `.xml`. **내장본에서 꺼내 씁니다** — 단일 파일 배포본에는 디스크 사본이 없고, 디스크에서 복사하면 그 경로가 다시 머신 종속이 됩니다|
|HintPath|`lib/Tabbit.Validation.dll` — 상대경로입니다|
|쓰는 조건|**바이트가 다를 때만.** 커밋되는 파일이므로, 아무것도 바뀌지 않은 빌드가 남의 저장소에서 변경으로 보여서는 안 됩니다. 계약을 결정적으로 빌드하는 이유도 이것입니다|
|gitignore|`**/Validation.csproj`를 풀었습니다. `lib/`도 커밋 대상입니다|

**결정성 실측.** 재실행해도, 계약을 리빌드한 뒤에도 해시가 같고 **파일이 다시 쓰이지도
않습니다**(수정 시각 불변).

**단, 구성이 같을 때까지였습니다 — 2026-09-01.** `Deterministic`은 같은 종류의 빌드 둘을
맞추고 Debug와 Release는 맞추지 않습니다. 세 가지가 갈랐습니다 — 최적화 플래그, 디버그
디렉터리, 그리고 SDK가 넣는 `[assembly: AssemblyConfiguration]`의 「Debug」·「Release」라는
낱말입니다. 그래서 **샘플을 마지막에 재생성한 사람이 어느 쪽이 커밋될지 정하였고**, 그 파일이
기여자 사이에서 앞뒤로 바뀌었습니다. `Optimize` · `DebugType` · `GenerateAssemblyConfigurationAttribute`
셋을 구성과 무관하게 고정해서 두 빌드의 바이트를 같게 했습니다. 잃는 것은 없습니다 —
인터페이스와 enum과 어트리뷰트뿐이라 최적화할 메서드 본문이 없고, 이름만 읽는 표면의
심볼 파일은 읽는 쪽이 없습니다.

**게이트.** `Validation.csproj`가 상대 HintPath를 갖고 **드라이브 문자를 한 글자도 담지
않는지**, 그리고 `lib/`의 두 파일이 실재하는지 검사합니다.

**도구가 만든 폴더를 도구가 거부하던 문제가 여기서 또 나왔습니다** — `lib`이 「모르는 단계」로
거부되었습니다. `.vs` 때와 같은 종류이고, `RuleFolders.ContractFolder`로 등록해 면제합니다.

### 5c. `.generated/`의 커밋 — **구현 완료**

`lib/`만으로는 절반이었습니다. clone 상태로 편집기 프로젝트를 빌드해 보니 계약은 해결되고
**생성 액세서(`Tabbit.Rules`)가 없었습니다.** 그래서 `.generated/`를 커밋 대상으로
돌렸습니다 — 미결로 두었던 「중간 정책」의 답이 여기서 나옵니다.

파생물을 커밋하는 것이지만, 근거는 lockfile과 같습니다: 파생할 수 있다는 것과 파생하지 않고도
가질 가치가 있다는 것은 별개입니다. **스키마가 바뀌면 여기에 diff가 나는 것이 정직한 신호**이고
— 컬럼 이름이 바뀌면 규칙이 할 수 있는 말이 실제로 달라집니다 — 바뀌지 않으면 같은 모델이 같은
텍스트를 내므로 diff가 없습니다.

**결과.** 생성물을 지운 clone 상태에서 편집기 프로젝트가 **오류 0·경고 0**으로 빌드됩니다.
2번 피드백의 목표 「clone 직후, 아무것도 실행하지 않고 IDE를 열었을 때 자동완성이 되는 것」이
성립합니다.

7단계로 액세서를 dll로 내게 되면 `.generated/`의 소스가 그 dll로 바뀌는 것이고, 커밋 대상이라는
점은 그대로입니다.

### 5b의 원래 설계

- 도구가 계약 dll(과 XML 문서)을 `validation/lib/`(가칭)에 기록하고, HintPath를 상대경로로
  바꿉니다.
- gitignore에서 `**/Validation.csproj`를 제거하고, 생성 헤더의 「커밋하지 말라」 문구를
  폐기합니다.
- **dll 갱신은 내용 비교 후 쓰기입니다.** 무조건 덮어쓰면 빌드마다 바이트가 달라져(MVID)
  영구 dirty가 됩니다. 결정적 빌드 + 해시 비교가 성립 조건이며, 이것이 깨지면 이 체계 전체가
  dirty diff 발생기가 됩니다.

### 5c. `Tables.` 자동완성

`.generated/` 접근자를 dll로 내리고 커밋 대상으로 바꾸면 마지막 겹이 해소됩니다. 접근자
dll의 emit은 7단계의 출력 경로를 공유하므로 7단계 완료 시 얻어집니다. 그 전까지의 중간
정책(「`.generated/` 소스를 커밋 대상으로 전환」)을 둘지는 미결입니다.

**게이트.** clone 상태 재현(생성물 삭제) 후 IDE 프로젝트가 컴파일 가능한지 확인하는 테스트 —
기존 하네스가 픽스처에 `dotnet build`를 쓰는 선례(`CsToolchain.cs`)와 같은 방식.

---
