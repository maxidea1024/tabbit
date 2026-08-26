# `Targets` — 이 변환이 내는 것

> [「Recipe 파일」로 돌아가기](../recipe.md)

---

## `Targets` — 이 변환이 내는 것 전부

내보내기든 코드 생성이든 기록이든, 출력은 전부 `Targets`의 항목 하나입니다. `Type`이 타깃을
지목하고 나머지가 그 타깃의 설정입니다.

```json
"Targets": [
  { "Type": "binary", "Path": "./out/data", "FileExtension": ".tcb" },
  { "Type": "csharp", "Path": "./out/cs", "Namespace": "MyGame.Data", "AccessorName": "GameData" }
]
```

|`Type`|종류|
|--|--|
|`binary`, `json`|파일 내보내기|
|`text`|`text` 컬럼의 값을 그룹마다 파일 하나로 수집 — 「[수집된 텍스트](../exports/text.md#수집된-텍스트--text-타깃)」|
|`mysql`, `postgresql`, `mongodb`, `redis`|데이터베이스 내보내기|
|`cpp`, `csharp`, `typescript`, `html`, `c`, `go`, `rust`, `python`, `java`, `kotlin`, `swift`, `lua`, `ruby`, `php`, `dart`|코드 생성 — 설정은 [언어별 가이드](../languages/readme.md)|
|`unreal`|Unreal 모듈 생성|
|`summary`, `history`|변환 자체를 기록 — 「[Summary와 히스토리](../history.md)」|

- 없는 `Type`은 **오류**입니다. 출력을 요청했는데 말없이 아무것도 안 나오면, 있어야 할 파일이
  빠진 채 빌드가 나갑니다.
- 그 타깃에 없는 필드도 **오류**입니다. `FileExtention`처럼 오타를 내면 기본값으로 그냥
  넘어가고, 증상은 "설정이 안 먹는다"로만 보입니다.

타깃마다 전용 섹션을 두지 않는 것은 타깃을 추가할 때 recipe 스키마를 고치지 않아도 되게 하기
위함입니다. **타깃 하나를 지우는 일이 파일 하나를 지우는 일**이어야 하기 때문이기도 합니다.

> 예전에는 일부 타깃이 `Exports`·`CodeGenerations` 아래에 전용 섹션을 갖고 나머지는 `Targets`에
> 있었습니다. 그 10개를 가르는 것은 기능이 아니라 도입 시점이었고, recipe를 읽는 사람이 그
> 배치에서 읽어낼 수 있는 규칙은 없었습니다.

### 출력 항목 공통 설정

|키|기본값|설명|
|--|--|--|
|`TargetSide`|`"cs"`|이 출력이 어느 쪽을 위한 것인지. `"c"`(클라), `"s"`(서버), `"cs"`(양쪽). 반대쪽으로 지정된 엔티티와 필드가 제외됩니다.|

> 익스포터와 그 파일을 읽는 코드 제너레이터는 **같은 `TargetSide`로 맞춰야** 합니다. 컬럼
> 집합이 어긋나면 생성된 테이블 리더가 데이터와 맞지 않습니다.

서버/클라 각각을 뽑으려면 항목을 두 개 두고 각기 다른 `TargetSide`와 경로를 지정하면 됩니다.

## `Assets` — 애셋 폴더

[`asset` 타입](../sheets/types.md#asset--파일이-있어야-하는-문자열) 컬럼의 값을 어느 폴더에서 찾을지.
**이 섹션이 없으면 검사가 꺼집니다.**

```jsonc
"Assets": {
  "Roots": [
    { "Kind": "icon", "Path": "./content/ui/icon", "Pattern": "*.uasset" },
    { "Kind": "sfx",  "Path": "./content/audio",   "Pattern": "*.uasset" },

    // 같은 종류를 여러 폴더에 둘 수 있습니다. 어느 하나에 있으면 통과입니다
    { "Kind": "icon", "Path": "./content/dlc/icon", "Pattern": "*.uasset" }
  ],
  "OnMissing": "warn"
}
```

|설정|기본값|무엇|
|--|--|--|
|`Kind`|`""`|`asset(icon)`의 괄호 안. 비우면 종류를 안 적은 컬럼의 폴더입니다. 대소문자 구분 없음|
|`Path`|—|하위 폴더까지 훑습니다. **없는 폴더는 오류**입니다 — 거기서 찾는 값이 전부 「없음」으로 보고되기 때문입니다|
|`Pattern`|`*`|**좁히는 편이 낫습니다.** 전부 훑으면 애셋 폴더에 있는 메모 파일까지 맞아버려서, 통과해도 아무 의미가 없습니다|
|`OnMissing`|`warn`|`warn` · `error` · `ignore`|

`OnMissing`이 `warn`인 것과 `Validation.TreatWarningsAsErrors`의 조합이 이 기능의 요점입니다 —
자세한 것은 [시트 작성](../sheets/types.md#없는-파일--기본은-경고)에 있습니다.

- **폴더 훑기는 루트당 한 번**입니다. 셀마다 묻지 않습니다.
- 확장자와 대소문자는 무시하고 이름만 봅니다. 시트가 `Ship_Galleon`이라고 적기 때문입니다.
- 같은 이름의 파일이 여러 폴더에 있으면 **먼저 찾은 것**입니다. 어느 쪽인지가 문제라면 그건
  프로젝트가 정할 일입니다.
