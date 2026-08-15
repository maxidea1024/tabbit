# 출력 항목을 `Targets` 하나로

> 상태: **구현 완료** (2026-08-15) · 관련: [recipe 문서](../doc/recipe.md#targets--이-변환이-내는-것-전부)

`RecipeModel`의 `Exports`·`CodeGenerations` 섹션을 **삭제하고**, 모든 출력 항목을 `Targets`
목록으로 지정합니다. 외부 사용자가 없고 아직 릴리스 전이므로 **호환 장치를 두지 않습니다** —
섹션을 읽는 경로 자체를 없애고, 남은 레시피를 전부 이관합니다.

---

## 1. 현재 상태 — 실측

등록된 타깃 23개 중 10개가 전용 섹션을 가지고 있고, 13개는 `Targets`로만 지정됩니다.

|섹션이 있는 타깃|섹션|
|--|--|
|`binary` `json` `mysql` `postgresql` `mongodb` `redis`|`Exports.*`|
|`cpp` `csharp` `typescript` `html`|`CodeGenerations.*`|

|섹션이 없는 타깃|
|--|
|`c` `go` `rust` `python` `java` `kotlin` `ruby` `php` `dart` `unreal` `text` `summary` `history`|

**이 경계는 기능이 아니라 시점입니다.** `TargetRegistry`가 생기기 전에 있던 타깃이 섹션을
가지고 있고, 그 뒤에 추가된 것은 전부 `Targets`로만 갑니다. `RecipeModel.cs:696`이 이미 그렇게
기재하고 있습니다 — 「섹션들은 `Targets`보다 먼저 있었고 기존 recipe를 위해 남아 있다」.

그 사정이 recipe를 읽는 사람에게는 보이지 않습니다. `cpp`·`csharp`·`typescript`·`html`이
`CodeGenerations`에 있고 `go`·`rust`·`python`은 `Targets`에 있는 배치에는 **읽어낼 수 있는
규칙이 없습니다.** 같은 종류의 산출물이 두 자리로 나뉘어 있는 것이 유일한 사실입니다.

---

## 2. 통일의 근거

### 2.1 섹션 항목의 오타 미검출

`Targets` 항목은 `MissingMemberHandling.Error`로 역직렬화되므로 그 타깃에 없는 필드가 오류로
보고됩니다(`TargetRegistry.cs:129`). 섹션 항목은 `LoadFromFile`의 기본 `JsonConvert`를 그대로
지나므로(`RecipeModel.cs:743`) **같은 오타가 조용히 무시됩니다.**

`FileExtention`처럼 한 글자를 틀리면 섹션 쪽에서는 기본값으로 넘어가고, 증상은 「설정이 적용되지
않는다」로만 나타납니다. 이것은 통일의 부수 효과가 아니라 **주된 이득**입니다 — 두 경로의 엄격함이
다른 상태가 유지될 이유가 없습니다.

### 2.2 `--new-recipe`의 표시 범위 — 23개 중 10개

`RecipeSkeleton`은 모델을 반사로 순회해 리스트마다 기본 항목 하나를 채웁니다. `Targets`는 원소
타입이 `JObject`라 채울 것이 없어 건너뜁니다(`RecipeSkeleton.cs:162`). 따라서 지금
`--new-recipe`가 설정을 보여주는 타깃은 **섹션이 있는 10개뿐이고, 13개는 헤더의 주석 한 줄로만
언급됩니다.**

통일하면 이 골격을 레지스트리 순회로 바꿔야 하고, 그 결과 **23개 전부가 각자의 설정과 함께**
나옵니다. 지금보다 나아지는 것이지 유지 비용이 아닙니다.

### 2.3 스키마와 타깃의 결합 제거

타깃을 추가할 때 `RecipeModel`을 고치지 않아도 되는 것이 `Targets`의 도입 목적이었습니다. 섹션
10개가 남아 있는 한 그 목적은 절반만 달성된 상태입니다. entry 클래스가 `RecipeModel` 안에
중첩되어 있는 것도 같은 결합입니다 — 신규 타깃 13개는 이미 자기 파일에 entry 클래스를 두고
있습니다(`GoRecipe`·`DartRecipe`·`TextRecipe` 등).

---

## 3. 바뀌는 것

### 3.1 코드

|무엇|어떻게|
|--|--|
|`TabbitTargetAttribute`의 `Section`|10개 타깃에서 제거. 어트리뷰트의 `Section` 프로퍼티와 `TargetDescriptor.Section`·`SectionEntries` 경로도 함께 삭제|
|`RecipeSectionReader`|**타깃 쪽 용도 소멸.** 소스 레지스트리가 계속 사용하므로 파일은 존치|
|entry 클래스 10개 + `DatabaseRecipe`|`RecipeModel` 밖으로, 각자의 타깃 파일 옆으로. 신규 13개와 같은 배치|
|`RecipeModel.Exports`·`CodeGenerations`|그룹 클래스째 삭제|
|`RecipeSkeleton.FillLists`|모델 반사 대신 **레지스트리 순회**로. 타깃마다 entry를 기본 생성해 `"Type": id`를 앞에 붙여 `Targets` 배열로 직렬화|

참조는 14개 파일 40곳뿐이고 전부 **타입 이름 참조**입니다 — 섹션을 직접 순회하는 코드는 없습니다.

### 3.2 레시피와 문서

|대상|개수|
|--|--|
|`test/fixtures/recipes/*.json`|**61개** (전체 67개 중)|
|`test/fixtures/output/_baseline/*/recipe.json`|11개|
|`src/recipes/*.jsonc` (`--template` 원본)|6개 전부|
|`samples/rescue/` · `samples/named-range/`|6개|
|`side-by-side/side-by-side.json`|1개|
|`doc/recipe.md` · `doc/exports.md` · `doc/languages/*`|섹션 표와 예제 전부|

레시피 **85개**입니다. `samples/rescue/doc/`의 분석 문서 1개에도 예제가 있습니다.

> **`test/fixtures/output/_templates/*.json`을 재기록 대상으로 적었던 것은 틀렸습니다.** 그
> 12개는 커밋되는 골든이 아니라 `.gitignore`된 실행 산출물입니다. 템플릿의 게이트는
> `src/recipes/*.jsonc` 자신을 CLI 출력과 대조하는 쪽이고, 그 6개는 3.2에서 이미 이관합니다.

---

## 4. 바뀌지 않는 것

**산출물은 한 바이트도 움직이지 않아야 합니다.** 근거는 세 가지입니다.

1. `Targets` 항목은 섹션 항목과 **같은 entry 클래스**로 역직렬화됩니다.
2. 실행 순서는 `TargetRegistry.Discover`가 종류 → `Order` → id로 정렬한 결과이고, 항목이 어디에서
   왔는지는 정렬에 관여하지 않습니다.
3. `TargetSide` 판정과 모델 투영은 `Plan`·`RunAll`의 같은 코드를 지납니다.

따라서 **골든이 바뀌면 그 diff가 결함입니다.**

> **실측 결과 — 무변경.** 골든 픽스처가 한 바이트도 움직이지 않았습니다. `samples/rescue/out/`을
> 재생성한 결과도 67개 테이블 · 13개 언어 · 바이너리 · JSON 전부 동일하고, 달라진 것은 HTML의
> 생성 시각 줄과 summary의 커밋 해시뿐이었습니다.

---

## 5. 확인이 필요한 지점

### 5.1 엄격 역직렬화로 새로 검출되는 필드

2.1의 이득이 이관 과정에서는 위험으로 나타납니다. 기존 섹션 항목에 **해당 타깃이 갖지 않는 필드가
적혀 있어도 지금까지 무시되어 왔으므로**, 이관 직후 그런 항목이 오류로 보고될 수 있습니다.

이관 스크립트가 아니라 **게이트 실패로 검출합니다** — 85개 레시피를 옮긴 뒤 픽스처 게이트를 돌리면
해당 항목이 파일과 함께 보고됩니다. 검출되면 그것은 원래 적용되지 않고 있던 설정이므로, 오타면
고치고 폐기된 키면 삭제합니다. **개수를 기록에 남깁니다** — 0건과 「확인하지 않음」이 구별되어야
합니다.

> **실측 결과 — 0건.** 85개 레시피의 **396개 항목 전부**가 자기 타깃에 있는 필드만 쓰고
> 있었습니다. 대조 기준은 `--new-recipe` 골격이 내는 타깃별 설정 목록이므로, 판정한 것은 도구
> 자신입니다. 게이트 999개도 전부 통과합니다.

### 5.2 진단 메시지의 섹션 표기

`PlannedTarget.Section`이 진단에 인용됩니다. `CodeGenerations.CSharp[0]`이
`Targets[3]`으로 바뀝니다. 골든 산출물 중 이 문자열을 담은 것은 **없음을 확인하였습니다.**
`TargetSideTests.cs:101`이 `"Exports.Json"`을 문자열 리터럴로 쓰고 있으나, 그것은
`RecipeTargetSide.Of`에 넘기는 임의의 위치 표기이므로 값만 교체합니다.

---

## 6. 절차

생성기·템플릿을 건드리지 않으므로 [아키텍처 문서](../doc/architecture.md#개발--테스트)의 전체
재기록 절차는 해당하지 않습니다. 순서는 다음과 같습니다.

1. entry 클래스 이동과 `Section` 제거 — 이 단계에서 레시피는 아직 옛 형식이므로 **빌드는 되고
   테스트는 전부 실패합니다.** 정상입니다.
2. `RecipeSkeleton`을 레지스트리 순회로 전환.
3. 레시피 85개 이관. 기계적 변환이고, 5.1의 검출은 이 다음 단계에서 나옵니다.
4. `--filter`로 관련 게이트부터 — 골든이 움직이지 않는 것을 먼저 확인합니다.
5. `samples/rescue/out/` 재생성. 게이트가 없으므로 이 단계를 빠뜨려도 스위트는 통과합니다.
   **산출물이 같으면 되돌립니다** — 커밋되는 HTML과 summary가 실행 시각과 커밋 해시를 담고 있어,
   내용이 같은 재생성이 diff로는 변경으로 보입니다.
6. 문서 개정 — `doc/recipe.md`의 섹션 표와 예제, `doc/exports.md`, `doc/languages/*`.
7. 전체 스위트 1회.

날짜가 붙은 분석 기록(`samples/*/doc/*-YYYYMMDD.md`)의 예제는 **고치지 않습니다.** 그날의 판단을
적어 둔 기록이고, 이미 없어진 옵션이 함께 적혀 있어 고치면 기록이 아니게 됩니다.
