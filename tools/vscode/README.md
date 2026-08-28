# VS Code 확장 — `.tbs` 편집

`.tbs`(STRUCT DSL) 파일의 문법 강조와 편집기 지원입니다. 설계와 그 근거는
[편집기 지원](../../spec/ops/lsp.md)에 있습니다.

## 지금 되는 것

- **문법 강조** — 키워드 · 내장 타입 · 선언 이름 · 와이어 태그 · 메타데이터 · 주석
- **주석 토글**과 괄호 짝 맞추기
- `///` 줄에서 엔터를 치면 다음 줄에 `///` 가 붙습니다

진단 · 정의로 이동 · 호버는 `tabbit lsp` 가 들어온 뒤입니다.

## 만들고 설치하기

`vsce` 는 내려받아 쓰므로 미리 설치할 것이 없습니다.

```
cd tools/vscode
npx --yes @vscode/vsce package
code --install-extension tabbit-tbs-0.0.1.vsix
```

지우려면 `code --uninstall-extension tabbit.tabbit-tbs` 입니다.

## 눈으로 확인하기

설치한 다음 `samples/wildling/design-data/schemas/battle.tbs` 를 엽니다. 여섯 가지가 서로 다른
색이어야 합니다.

|무엇|예|
|--|--|
|키워드|`struct` · `abstract` · `extends` · `field` · `enum` · `value` · `foreign`|
|내장 타입|`int` · `string?` · `float[]` · `map<int,int>` 의 `map`|
|선언 이름|`struct` 다음의 이름, `extends` 다음의 이름|
|와이어 태그|`@1`|
|메타데이터|`(min=1, max=9999)` 의 `min` · `max`|
|주석|`//` 와 `///` 가 서로 다릅니다|

`samples/clover/design-data/schemas/effect.tbs` 는 다형과 툼스톤이,
`test/fixtures/schemas/containers/bag.tbs` 는 `set` · `map` 이 들어 있어 함께 보면 좋습니다.

## 문법 파일을 고칠 때

내장 타입 이름이 [`syntaxes/tbs.tmLanguage.json`](syntaxes/tbs.tmLanguage.json)에 적혀 있습니다.
**타입을 추가하면 이 파일도 함께 고칩니다.** 정본은 `src/Cooking/CookingContext.cs` 의
`IsValidTypeName` 과 `src/Models/CompositeType.cs` 의 목록입니다.
