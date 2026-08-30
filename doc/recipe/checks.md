# 검사와 보고

> [「Recipe 파일」로 돌아가기](../recipe.md)

---

## `Validation` — 시트에 적을 수 없는 규칙

시트에 적을 수 있는 제약(필수·범위·허용값·참조)은 이미 변환 단계에서 검사합니다. 그것으로
표현할 수 없는 규칙은 폴더 하나에 `.cs` 파일로 적습니다 — 자세한 것은
「[검증](../validation.md)」에 있습니다.

```jsonc
"Validation": {
  // pre/ tables/ global/ runtime/ shared/ 로 된 폴더. 비우면 검증이 꺼집니다.
  "Path": "./validation",

  // 규칙만 읽는 자유 키/값. 코어는 키를 모릅니다.
  "Options": {
    "Locale": "KR",
    "ContentRoot": "../game/content"
  },

  // runtime/ 규칙이 이름으로 여는 연결. 스킴이 종류를 나타내고, ${NAME}은 환경 변수에서.
  "Connections": {
    "Live": "mysql:Server=db;Database=game;Uid=ro_validator;Pwd=${DB_PASSWORD}",
    "Cache": "redis://cache:6379/0"
  },

  // 편집기가 `Tables`를 해석할 프로젝트를 생성할지. 기본은 켜짐입니다 — 「검증」 §17.
  "EmitIdeProject": true,

  // 경고를 오류로 취급할지. CI에서 켜는 용도이고, Info는 이것으로도 승격되지 않습니다.
  "TreatWarningsAsErrors": false
}
```

|설정|무엇|
|--|--|
|`Path`|규칙 폴더. **비우는 것이 검증을 끄는 유일한 방법**이고, 그것은 diff에 남습니다. 지정했는데 폴더가 없으면 오류입니다 — 오타 하나로 검증 전체가 그냥 통과하지 않도록|
|`Options`|규칙만 읽는 자유 키/값. 로케일·콘텐츠 경로처럼 **코어가 몰라야 하는 것**이 지나가는 자리입니다|
|`Connections`|`rules/runtime/` 규칙이 여는 읽기 전용 연결. `mysql:` · `postgres:` · `redis://` 중 하나로 시작해야 합니다 — ADO 연결 문자열과 Redis 설정 문자열은 형식으로 구별되지 않아 추측하지 않습니다|
|`EmitIdeProject`|편집기가 `Tables`를 해석할 프로젝트를 검증 폴더 루트에 씁니다. **기본은 켜짐** — 액세서 소스는 어차피 `.generated/`에 쓰이고, 이 파일은 그것을 편집기가 읽을 수 있게 하는 것뿐입니다. 프레임워크보다 오래된 Visual Studio는 이 프로젝트를 열지 못하므로 그때 끕니다 (「[검증](../validation.md)」 §17)|
|`TreatWarningsAsErrors`|경고를 오류로. 기본은 꺼짐이고 CI에서 켭니다|

검증은 **모든 타깃보다 앞에서** 돌고, 실패하면 파일에도 데이터베이스에도 흔적이 남지 않습니다.

## `Report` — 찾은 문제를 고칠 사람에게 보이기

실행이 찾은 것을 HTML 한 장과 JSON 한 장으로 냅니다. **성공한 실행만이 아니라 멈춘 실행도
씁니다** — 멈춘 실행의 보고가 본론이기 때문입니다. 설계는 「[빌드 리포트](../../spec/ops/build-report.md)」에
있습니다.

```jsonc
"Report": {
  // 기본은 켜짐. 끄는 쪽이 명시적입니다.
  "Enabled": true,

  // 비우면 빌드 캐시 옆(.tabbit/)에. CI가 아티팩트를 걷어가는 자리를 지정하는 용도입니다.
  "Path": "",

  // never · problems · always. 기본은 problems — 경고 이상이거나 실행이 실패했을 때.
  "OpenInBrowser": "problems",

  // 페이지에 실을 최대 건수. 0은 무제한이고, 잘린 것은 페이지에 적힙니다. JSON은 언제나 전량입니다.
  "MaxHtmlEntries": 5000
}
```

|설정|무엇|
|--|--|
|`Enabled`|기본 켜짐. **켜야 보이는 것이면 로그와 같습니다** — 로그를 읽지 않는 사람에게 닿는 것이 이 산출물의 목적입니다|
|`Path`|비우면 `.tabbit/<레시피 이름>-<해시>.report.html` · `.report.json`. 빌드 씰과 같은 이름 규칙이라 한 실행의 파일들이 같은 자리에 모입니다|
|`OpenInBrowser`|`problems`면 **경고 이상이거나 실행이 실패했을 때** 기본 브라우저로 엽니다. 알려진 문제로 강등된 것은 세지 않습니다 — 이미 아는 것으로 매번 열리면 여는 것 자체가 무시하는 습관이 됩니다|
|`MaxHtmlEntries`|한 페이지가 열릴 수 있는 크기의 상한이지 기록의 상한이 아닙니다. 잘렸으면 페이지에 그렇게 적히고, 전량은 JSON에 있습니다|

**보는 사람이 없는 실행에서는 열지 않습니다.** `CI` 환경 변수가 설정되어 있거나, 출력이
터미널이 아니거나(리다이렉트·파이프), `--silent`이면 설정과 무관하게 열지 않습니다.

구글 시트에서 온 위치는 **그 셀로 바로 가는 링크**가 됩니다. xlsx는 셀을 여는 이식 가능한
URL이 없어서 위치 텍스트와 복사 버튼입니다.

마지막 리포트는 `tabbit --recipe <레시피> --show-report` 로 다시 엽니다.

---
