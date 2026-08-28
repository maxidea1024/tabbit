# VS Code 확장 — `.tbs` 편집

`.tbs`(STRUCT DSL) 파일의 문법 강조와 편집기 지원입니다. 설계와 그 근거는
[편집기 지원](../../spec/ops/lsp.md)에 있습니다.

## 되는 것

|무엇|어디서 나오는가|
|--|--|
|**문법 강조** — 키워드 · 내장 타입 · 선언 이름 · 와이어 태그 · 메타데이터 · 주석|확장의 TextMate 문법|
|**진단** — 문법 오류, 중복 선언, `extends` 와 판별자|`tabbit lsp`|
|**정의로 이동**(F12) — 타입 이름 · `extends` 뒤의 이름|`tabbit lsp`|
|**호버** — 선언 한 줄과 `///` 문서|`tabbit lsp`|
|주석 토글 · 괄호 짝 · `///` 줄 잇기|언어 설정|

**판정은 확장이 하지 않습니다.** 서버가 변환과 같은 파서를 돌리므로, 여기서 빨간 줄이 그어지는
것과 빌드가 거부하는 것이 같은 답입니다. 워크북은 열지 않으므로 시트가 있어야 답할 수 있는 것 —
`foreign` 뒤의 테이블 이름이 그렇습니다 — 은 여기서 답하지 않습니다.

## 만들고 설치하기

```
cd tools/vscode
npm install
npm run package
code --install-extension tabbit-tbs-0.0.1.vsix
```

지우려면 `code --uninstall-extension tabbit.tabbit-tbs` 입니다.

## 서버를 찾는 순서

1. `tabbit.path` 설정
2. `PATH` 의 `tabbit`
3. 연 폴더가 이 저장소이면 `src/bin/Debug/net10.0/` 과 `Release`

셋 다 없으면 설정하는 방법을 알리고 멈춥니다. 이 저장소를 열어 놓고 `dotnet build src` 를 한 번
돌렸다면 설정 없이 3번으로 잡힙니다.

설정 셋이 있습니다 — `tabbit.path` · `tabbit.messages`(보고의 언어) ·
`tabbit.trace.server`(주고받는 메시지를 출력 패널에 적기).

## 눈으로 확인하기

설치한 다음 `samples/wildling/design-data/schemas/battle.tbs` 를 엽니다.

**색**부터 봅니다. 여섯 가지가 서로 달라야 합니다.

|무엇|예|
|--|--|
|키워드|`struct` · `abstract` · `extends` · `field` · `enum` · `value` · `foreign`|
|내장 타입|`int` · `string?` · `float[]` · `map<int,int>` 의 `map`|
|선언 이름|`struct` 다음의 이름, `extends` 다음의 이름|
|와이어 태그|`@1`|
|메타데이터|`(min=1, max=9999)` 의 `min` · `max`|
|주석|`//` 와 `///` 가 서로 다릅니다|

**그다음 서버**입니다.

1. 필드의 타입 이름에 F12 — 그 타입을 선언한 자리로, 다른 파일이면 그 파일로 갑니다
2. 같은 이름에 마우스를 올려 — 선언 한 줄과 `///` 문서가 뜹니다
3. `struct` 를 `strcut` 으로 고쳐 — 그 낱말에만 빨간 줄이 그어지고, 문제 패널에
   `schema.unknown-keyword` 가 뜹니다
4. 되돌려 — 빨간 줄이 사라집니다

`samples/clover/design-data/schemas/effect.tbs` 는 다형과 툼스톤이,
`test/fixtures/schemas/containers/bag.tbs` 는 `set` · `map` 이 들어 있어 함께 보면 좋습니다.

서버가 뜨지 않으면 출력 패널의 `Tabbit Language Server` 를 봅니다.
`tabbit.trace.server` 를 `verbose` 로 두면 주고받은 메시지가 그대로 적힙니다.

## 문법 파일을 고칠 때

내장 타입 이름이 [`syntaxes/tbs.tmLanguage.json`](syntaxes/tbs.tmLanguage.json)에 적혀 있습니다.
**타입을 추가하면 이 파일도 함께 고칩니다.** 정본은 `src/Cooking/CookingContext.cs` 의
`IsValidTypeName` 과 `src/Models/CompositeType.cs` 의 목록입니다.

문법을 고쳤으면 색이 실제로 어떻게 나오는지 토큰으로 확인할 수 있습니다. `vscode-textmate` 로
파일 하나를 토큰화해 보면 스코프가 붙지 않은 자리가 드러납니다 — 눈으로 보아서는
`(sep=",")` 한 줄이 뒤쪽 파일 전체를 문자열로 만드는 것을 알아채기 어렵습니다.
