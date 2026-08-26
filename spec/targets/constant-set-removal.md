# 상수 세트 제거

- 상태: **초안.** 결정 대기
- 날짜: 2026-08-23
- 관련: [개념](../../doc/concepts.md) · [언어별 가이드](../../doc/languages/readme.md) · [이름 규약](naming-conventions.md)

---

## 1. 결정

**엔티티 종류에서 상수 세트(`~~const:이름~~`)를 없앱니다.** 시트가 낼 수 있는 것은
**테이블과 enum 둘**이 됩니다.

상수 세트가 하던 일은 **한 행 테이블**이 맡습니다. 이미 문서가 그렇게 안내하고
있습니다 — [언어별 가이드 §3](../../doc/languages/readme.md)은 「라이브 중에 조정할
가능성이 있는 수치라면 처음부터 상수 세트가 아니라 테이블 한 행으로 두세요」라고
적고, [시트 문서](../../doc/sheets.md)의 엔티티 표에도 같은 주의가 붙어 있습니다.

### 근거

|근거|내용|
|--|--|
|**테이블도 불변입니다**|읽는 쪽에 쓰기 경로가 없습니다. 상수 세트가 주는 「고쳐지지 않는다」는 성질을 한 행 테이블이 이미 줍니다|
|**한 행 테이블이 더 유리합니다**|같은 값을 **데이터 패치로 바꿀 수 있습니다.** 상수는 코드 배포만이 유일한 경로입니다|
|**가장 많이 틀리는 자리입니다**|상수만 고치면 변환은 성공하고, `.tcb`는 한 바이트도 안 바뀌고, 매니페스트 해시도 그대로입니다. 「배포했다」고 믿기 가장 쉬우면서 신호가 어디에도 없습니다. 이 함정을 설명하려고 문서 한 절, 배포 판정 규칙 하나, 테스트 네 개가 있습니다|
|**엔티티 하나가 모든 언어로 번집니다**|생성기와 View 가 언어마다 하나씩 · 템플릿 16개(355행) · 메시지 5개 × 5개 로케일. 상수 세트를 위한 코드입니다|
|**이미 한 타깃은 내지 않습니다**|`UnrealCodeGenerator`에 상수 처리가 **없습니다.** Unreal 타깃을 쓰는 프로젝트의 상수 세트는 지금 조용히 사라집니다. 언어 하나가 이미 없이 지내고 있습니다|
|**실사용이 없습니다**|`samples/rescue`의 상수 세트는 **0개**입니다. 상수 세트를 선언하는 것은 픽스처 워크북 3개(`core` · `conformance` · `conformance-skew`)뿐이고, 그중 둘은 **「상수 파일이 컴파일되는지」를 확인하려고 만든 것**입니다([conformance README](../../test/fixtures/tools/conformance/README.md))|

### 잃는 것

**컴파일 타임 상수입니다.** 상수 세트는 선언으로 나가므로 배열 크기 · `switch`
레이블 · C++ 템플릿 인수 · enum 초기값 자리에 쓸 수 있었습니다. 한 행 테이블의
값은 런타임 데이터라 그 자리에 못 들어갑니다.

**이 손실을 받아들입니다.** 그런 값 — 프로토콜 번호, 배열 크기 — 은 애초에
스프레드시트가 아니라 코드에 적히는 것이 맞고, 시트에 적혀 있으면 시트를 고친
사람이 코드 배포를 잊는 쪽으로 기울기 때문입니다.

---

## 2. 열린 결정

|#|결정할 것|선택지|기울어짐|
|--|--|--|--|
|D1|**이미 `~~const:~~`가 적힌 시트**|(a) 오류로 거부 (b) 경고 후 무시 (c) 조용히 무시|**(a)**. 무시는 「값을 고쳤는데 아무것도 안 나갔다」를 한 단계 더 조용하게 만듭니다|
|D2|**레시피의 `Naming.Constant` 키**|(a) 지우고 방치 (b) 지우고 발견 시 경고|`RecipeModel.LoadFromFile`은 `ToObject<RecipeModel>()`이므로 **미지 키를 조용히 무시합니다.** (a)를 고르면 기존 레시피는 계속 돌지만 그 줄이 죽은 것을 아무도 모릅니다|
|D3|**`snapshot_stat.constant_sets` · `constants` 컬럼**|(a) 남기고 0을 쓴다 (b) 마이그레이션 6으로 DROP|두 컬럼 모두 `NOT NULL`입니다. 히스토리 마이그레이션은 **가산적**이라는 규약이 `HistorySchema`에 적혀 있으므로 (a)가 규약대로입니다|
|D4|**`EntityKind.Constants` · `Constant`**|(a) 남기되 쓰지 않는다 (b) 제거|`HistoryStore.cs:274`가 저장된 문자열을 `Enum.Parse`로 되읽습니다. 제거하면 **기존 DB의 옛 스냅샷을 읽을 수 없습니다.** (a)가 안전합니다|
|D5|**`DeploymentAdvice.JudgeConstants`**|(a) 함께 제거 (b) 옛 스냅샷을 위해 남긴다|D4를 (a)로 하면 이쪽도 남기는 것이 일관됩니다|

---

## 3. 바뀌는 곳

### 3.1 코어

|위치|무엇|
|--|--|
|`src/Models/ConstantSet.cs`|**파일 삭제**(93행)|
|`src/Models/Model.cs`|`ConstantSets` · `Reset()` · `ProjectTo` 측 필터 · `ContainsConstantSet` · `FindConstantSet`. 뒤의 둘은 `private`이고 **호출자가 없습니다**|
|`src/Cooking/Layouts/TabbitLayoutParser.cs`|`ParseConstantSet`(약 100행)과 `def.type == "const"` 분기|
|`src/Cooking/ModelCooker.Composites.cs`|`RefuseCompositeConstants`|
|`src/Cooking/ModelCooker.Naming.cs`|상수 세트 · 상수 이름 수집|
|`src/Cooking/NamingRules.cs`|`NameKind.Constant`, `NameKind.Entity`의 설명 문구|
|`src/Recipe/NamingRecipe.cs`|`Constant` 프로퍼티(→ D2)|
|`src/Schema/SchemaDeclarations.cs`|`RefuseNamesTheSheetsAlreadyGave`의 상수 세트 충돌 검사|
|`src/CodeGeneration/TypeDependencies.cs`|`EnumsNamedBy(ConstantSet)`|
|`src/Cooking/CookingMessages.cs` · `Layouts/TabbitLayoutMessages.cs` · `Exporters/ExportMessages.cs`|id 5개. `cook.constant-not-found`는 **이미 호출자가 없습니다**|
|`src/Messages/Catalog/`|id 5개 × 로케일 5개 = **25개 항목**|

### 3.2 코드 생성

|위치|무엇|
|--|--|
|생성기 15개|`BuildConstantSet` · `RenderConstantValue` · 상수 파일 쓰기 · 임포트/`using` 계산. 언어당 대략 60~120행|
|View 15개|`*ConstantSetView` · `*ConstantView` · `Sets` 프로퍼티|
|템플릿 16개|`*-constants*.sbn` **355행**. C는 헤더/소스 둘, HTML은 `html-constantsets.sbn`|
|`HtmlCodeGenerator.cs`|`constantsets.html` 페이지 하나, 상단 바 항목, `index.html` 통계 2개, `HtmlLinks.ConstantSet`|
|`UnrealCodeGenerator.cs`|**변경 없음** — 애초에 없습니다|

### 3.3 히스토리

`ModelFingerprint`(상수 세트 지문) · `SnapshotDiff` · `SummaryBuilder` ·
`SummaryDocument`(`SummaryConstantSet` · `SummaryConstant` · Totals 2필드) ·
`HistoryText`(요약 한 줄) · `HistoryStore`(INSERT 파라미터 2개, live 엔티티) ·
`HistorySchema`(컬럼 2개 → D3) · `SnapshotChanges`(EntityKind 2값 → D4) ·
`DeploymentAdvice.JudgeConstants`(→ D5).

### 3.4 픽스처와 게이트

**픽스처 워크북은 코드가 만듭니다** — `test/fixtures/tools/FixtureGen/Program.cs`.
손으로 xlsx를 고칠 일은 없습니다.

|위치|무엇|
|--|--|
|`FixtureGen/Program.cs`|`ConstSpec` · `SheetBuilder.Const` · `core`의 `GameConfig`(상수 9개) · `conformance`의 `Limits`(상수 10개)|
|워크북|`core.xlsx` · `conformance.xlsx` · `conformance-skew.xlsx` 재생성. **나머지는 상수 세트를 선언하지 않습니다**|
|골든|`constantset`을 담은 파일 **90개**, `constants/` 아래 **14개**. `constantsets.html` 7개는 삭제, 나머지는 `TABBIT_UPDATE_GOLDEN=1`로 재기록|
|커밋된 산출물|`side-by-side/`(9개 파일) · `test/generated/`(2개) · `samples/rescue/out/`(상단 바 때문에 **115개**)|

**테스트**

|파일|무엇|
|--|--|
|`DeploymentAdviceTests.cs`|상수 관련 단정 4곳(`A_constant_change_is_code_only_and_says_why` 포함)|
|`KnownBugTests.cs`|A13/A14 — 상수 세트의 TypeScript 재수출|
|`NamingConventionTests.cs`|`NameKind.Constant` 단정 3곳과 그것을 세우는 헬퍼·레시피 JSON|
|`InMemoryHistoryState.cs`|상수 세트 엔티티 열거|
|`HeaderIncludeTests.cs` · `HtmlTargetTests.cs` · `SweepTests.cs` · `StagingCollisionTests.cs` · `GeneratedTypescriptTests.cs` · `LuaNestedAndOptionalTests.cs`|상수 파일·페이지를 전제한 단정과 주석|
|`ConformanceHarness.cs` · `ConversionGoldenTests.cs`|주석만|

> **conformance 게이트 하나가 없어집니다.** `Limits`는 「모든 언어의 상수 파일이
> 생성되고 컴파일되는가」를 확인하려고 있었습니다. 상수 파일 자체가 없어지므로
> 확인할 것도 없어지고, `test/fixtures/tools/conformance/README.md`의 `## The
> constant set` 절도 함께 사라집니다.

### 3.5 문서

|파일|무엇|
|--|--|
|`doc/sheets.md`|엔티티 마커 표 · `entity-type` 설명 · 엔티티 표 · 주의 박스|
|`doc/concepts.md`|엔티티 표 · 상수 세트 단락 · 배포 판정 표|
|`doc/glossary.md`|「엔티티」 항목(「셋뿐」 → 「둘뿐」) · 「상수셋」 항목|
|`doc/languages/readme.md`|배포 판정 표 · **§3 「상수만 고쳤는데 아무것도 안 바뀝니다」 절 전체** · 앵커를 가리키는 다른 문서들|
|`doc/languages/*.md` 15개|각 파일의 출력 트리 목록에서 `constants/` 줄|
|`doc/features.md` · `doc/history.md` · `doc/binary-format.md` · `doc/exports.md` · `doc/recipe.md` · `doc/troubleshooting.md` · `doc/readme.md` · `README.md`|한두 줄씩|
|`spec/targets/lua-language-support.md` · `spec/targets/html-documentation.md` · `spec/targets/naming-conventions.md`|출력 트리와 이름 규약|
|`doc/roadmap.md` · `CHANGELOG.md` · 나머지 `spec/`|**이력이므로 그대로 둡니다.** CHANGELOG에는 항목을 새로 답니다|

> `doc/languages/readme.md`의 앵커
> `#데이터만-나가도-되는-변경과-코드가-함께-나가야-하는-변경`은 여섯 문서가
> 가리킵니다. 절 제목은 유지하고 내용에서 상수 행만 뺍니다.

---

## 4. 게이트

|순서|게이트|비용|
|--|--|--|
|1|`FixtureGen` 재실행 후 워크북 3개의 diff 확인|즉시|
|2|골든 재기록 → **diff 전량 리뷰**|33초 + 리뷰|
|3|`--filter` 로 히스토리 · 이름 규약 · 배포 판정 · HTML|수 분|
|4|전 언어 비교본(`side-by-side/`) 재생성|수 분|
|5|**샘플 재생성**(`samples/rescue/out/`)|수 분|
|6|기록 없이 전체 스위트|**13분 21초**|

골든 diff에서 확인할 것은 **상수가 사라진 것 말고는 아무것도 안 바뀌었는지**입니다.
`constants/` 파일과 `constantsets.html`의 삭제, 상단 바에서 항목 하나가 빠진 것,
`index.html`과 `summary.json`의 통계 두 개 — 그 밖의 diff는 실수입니다.

---

## 5. 구현 순서

|단계|내용|
|--|--|
|1|**D1~D5 확정.** 특히 D4는 되돌리기 어렵습니다|
|2|파서에서 `const` 마커 거부(D1). 이 단계만으로 픽스처 3개가 실패하므로 다음 단계와 붙여서|
|3|`FixtureGen`에서 상수 세트 제거, 워크북 재생성|
|4|모델 · 쿠커 · 스키마 · 이름 규약에서 제거|
|5|생성기 15개 · View 15개 · 템플릿 16개 제거|
|6|HTML 타깃의 페이지와 상단 바|
|7|히스토리(D3~D5의 결정대로)|
|8|메시지 id 5개와 카탈로그 25개 항목|
|9|테스트 정리|
|10|문서|
|11|§4의 게이트를 순서대로|

2~5를 한 커밋으로 묶습니다 — 중간 상태는 빌드가 되지 않습니다.
