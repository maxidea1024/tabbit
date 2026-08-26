# Recipe 파일

무엇을 어디서 읽어 어디로 내보낼지 적는 파일.

> [문서 목록으로](readme.md)

---

## 한눈에

recipe 하나가 **무엇을 읽어**(`Sources`) **무엇을 낼지**(`Targets`) 적습니다. 그 둘이면
돌아갑니다 — 나머지는 전부 선택입니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./generated/data" },
    { "Type": "csharp", "Path": "./generated/cs", "Namespace": "MyGame.Data" }
  ]
}
```

`sheets/`의 워크북을 읽어 `.tcb`와 C# 접근자를 냅니다. `tabbit --new-recipe myrecipe.json`이
이런 뼈대를 만들어 주고, 경로가 빈 항목은 꺼진 것으로 취급되므로 그대로 실행해도 안전합니다.

|무엇을 하려는가|어디를 볼까|
|--|--|
|내 상황에 맞는 것을 베끼고 싶다|[예제](recipe/examples.md) — 유니티 · 언리얼 · 서버/클라 분리 · 구글 시트 · CI|
|어디서 읽을지 정한다|[`Sources`](recipe/sources.md)|
|무엇을 낼지 정한다|[`Targets`](recipe/targets.md#targets--이-변환이-내는-것-전부)|
|설정 하나의 뜻을 찾는다|[설정 하나하나](recipe/settings.md)|
|사람마다 경로가 다르다|[환경 변수 `${NAME}`](recipe/settings.md#환경-변수--name)|
|시트에 못 적는 규칙을 검사하고 싶다|[`Validation`](recipe/checks.md#validation--시트에-적을-수-없는-규칙)|
|찾은 문제를 남에게 보여야 한다|[`Report`](recipe/checks.md#report--찾은-문제를-고칠-사람에게-보이기)|

## recipe 파일이란

`recipe` 파일은 입력 소스와 출력 대상을 지정하는 `.json` 파일입니다. `//` 주석을 사용할 수 있습니다.

`tabbit --new-recipe myrecipe.json`으로 시작용 recipe를 만들 수 있습니다.

모든 목록에 기본값이 채워진 항목 하나가 들어 있고, 파일 앞부분에 사용 가능한 소스와 타깃 이름이
적혀 나옵니다.

그대로 실행해도 아무것도 만들지 않고 정상 종료합니다.
경로가 비어 있으면 꺼진 것으로 취급되기 때문입니다.

## 시작점 고르기

백지에서 시작할 필요가 없습니다. `--template`이 상황에 맞는 recipe를 내놓고, **설정마다 무엇을
위한 것이고 언제 바꾸는지 주석이 붙어 있습니다.**

```
tabbit --new-recipe my-recipe.json --template unity
```

|템플릿|무엇을 위한 것|들어 있는 것|
|--|--|--|
|`unity`|유니티 클라이언트|엑셀 → StreamingAssets(`.bytes`) + C# + HTML 문서|
|`client-server`|같은 시트에서 두 벌|`TargetSide`로 가른 바이너리 두 개, C#(클라)과 Go(서버)|
|`web`|브라우저|구글 스프레드시트 → JSON + 바이너리 + TypeScript + HTML|
|`server`|게임 서버|바이너리 + MySQL 적재 + C++|
|`unreal`|언리얼|바이너리 + 모듈 하나. 패키징 주의사항이 주석에 있습니다|
|`ci`|빌드 파이프라인|바이너리 + summary + 셀 단위 히스토리|

`--template`을 **생략하면** 모든 섹션이 기본값 항목 하나씩을 담은 파일이 나옵니다. 무엇을 쓸 수
있는지 훑어보기에는 그쪽이 낫습니다 — 다만 마흔 개의 기본값이 늘어선 파일도 그 나름의 백지라,
실제로 시작할 때는 템플릿 쪽이 빠릅니다.

> 템플릿은 회귀 스위트가 **실제로 변환해봅니다.** 설정 이름이 바뀌면 변환이 거부하므로, 낡은
> 템플릿은 테스트가 깨져서 드러납니다.

---

## 이 문서의 나머지

|무엇|어디|
|--|--|
|[`Sources` — 무엇을 읽을지](recipe/sources.md)|엑셀 · 구글 스프레드시트 · `.tbs` 선언. 어떤 워크북과 시트를 고를지|
|[`Targets` — 이 변환이 내는 것](recipe/targets.md)|바이너리 · 언어별 코드 · JSON · 데이터베이스 · 문서, 그리고 애셋 폴더|
|[검사와 보고](recipe/checks.md)|시트에 못 적는 규칙을 프로젝트 코드로, 그리고 찾은 문제를 남에게 보이기|
|[설정 하나하나](recipe/settings.md)|모든 타깃에 공통인 것 · 이름 · 언어별 · 내보내기 · 기록, 그리고 `${NAME}`|
|[예제](recipe/examples.md)|가장 작은 것부터 유니티 · 언리얼 · 서버/클라 분리 · 구글 시트 · CI 까지|
