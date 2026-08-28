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
|**자동완성** — 줄을 여는 낱말 · 멤버의 타입 · `extends` 뒤의 이름 · 메타데이터 키 · enum 기본값|`tabbit lsp`|
|**시맨틱 토큰** — 이름을 실제 정체로 갈라서 칠하기|`tabbit lsp`|
|주석 토글 · 괄호 짝 · `///` 줄 잇기|언어 설정|

**판정은 확장이 하지 않습니다.** 서버가 변환과 같은 파서를 돌리므로, 여기서 빨간 줄이 그어지는
것과 빌드가 거부하는 것이 같은 답입니다. 워크북은 열지 않으므로 시트가 있어야 답할 수 있는 것 —
`foreign` 뒤의 테이블 이름이 그렇습니다 — 은 여기서 답하지 않습니다.

## 받아서 설치하기

만들지 않고 받아도 됩니다. [릴리즈](https://github.com/maxidea1024/tabbit/releases)에
`tbs-v` 로 시작하는 태그의 것이 이 확장이고, `.vsix` 하나와 그 해시가 붙어 있습니다.

```
code --install-extension tabbit-tbs-0.0.1.vsix
```

**확장은 도구와 따로 배포됩니다.** 하이라이팅 하나를 고치는 데 플랫폼 바이너리 6개를 다시
구울 이유가 없기 때문입니다. 다만 진단과 이동은 `tabbit lsp` 가 답하므로 도구도 있어야 합니다.

## 직접 만들기

```
cd tools/vscode
yarn install
yarn package
code --install-extension tabbit-tbs-0.0.1.vsix
```

지우려면 `code --uninstall-extension tabbit.tabbit-tbs` 입니다.

### 무시해도 되는 경고 둘

**`The engine "vscode" appears to be invalid`** — yarn이 적습니다. `engines.vscode` 는 VS Code가
요구하는 항목이고 yarn이 모르는 이름이라서 나옵니다.

**`[DEP0169] DeprecationWarning: url.parse()`** — 설치 뒤에 나옵니다. **이 확장에서 나오는 것이
아닙니다.** VS Code의 CLI가 설치를 마치고 그 확장의 메타데이터를 마켓플레이스에 조회하는데, 그
HTTP 요청 경로가 Node의 옛 API를 씁니다. `--trace-deprecation` 으로 찍어 본 스택이 전부
`cliProcessMain.js` 안입니다.

이 확장은 마켓플레이스에 없으므로 그 조회는 아무것도 찾지 못하고, 그래도 요청은 나갑니다.
설치는 이미 끝난 뒤이므로 결과에 영향이 없습니다. 보기 싫으면 그 실행에서만 끕니다.

```powershell
$env:NODE_OPTIONS="--no-deprecation"; code --install-extension tabbit-tbs-0.0.1.vsix
```

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
5. 새 줄에 `field x ` 까지 치고 Ctrl+Space — 내장 타입과 이 폴더가 선언한 타입이 나옵니다
6. `field x ` 뒤에 `Elemnt` 처럼 없는 이름을 적어 — **그 이름만 색을 잃습니다.** 잘못 적은
   타입 이름은 워크북이 있어야 검사되므로 빨간 줄이 아니라 색으로 드러납니다

`samples/clover/design-data/schemas/effect.tbs` 는 다형과 툼스톤이,
`test/fixtures/schemas/containers/bag.tbs` 는 `set` · `map` 이 들어 있어 함께 보면 좋습니다.

서버가 뜨지 않으면 출력 패널의 `Tabbit Language Server` 를 봅니다.
`tabbit.trace.server` 를 `verbose` 로 두면 주고받은 메시지가 그대로 적힙니다.

## 문법 파일을 고칠 때

내장 타입 이름이 [`syntaxes/tbs.tmLanguage.json`](syntaxes/tbs.tmLanguage.json)에 적혀 있습니다.
**타입을 추가하면 이 파일도 함께 고칩니다.** 정본은 `src/Models/ScalarType.cs` 와
`src/Models/CompositeType.cs` 이고, 서버는 그 두 표에 물으므로 따라올 필요가 없습니다 —
따라오지 않는 것은 이 문법 파일 하나뿐입니다.

문법을 고쳤으면 색이 실제로 어떻게 나오는지 토큰으로 확인할 수 있습니다. `vscode-textmate` 로
파일 하나를 토큰화해 보면 스코프가 붙지 않은 자리가 드러납니다 — 눈으로 보아서는
`(sep=",")` 한 줄이 뒤쪽 파일 전체를 문자열로 만드는 것을 알아채기 어렵습니다.
