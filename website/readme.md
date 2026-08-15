# 문서 사이트

[Docusaurus](https://docusaurus.io/)로 만들고 GitHub Pages에서 서빙합니다.
문서의 **원본은 여기 있지 않습니다** — `doc/` · `spec/` · `samples/*/doc/`가 원본이고,
이 폴더는 그것을 사이트로 만드는 껍데기입니다.

---

## 돌리기

```
cd website
npm install
npm start           # 로컬 개발 서버
npm run build       # 정적 파일 생성 (build/)
npm run serve       # 생성된 결과를 확인
```

`start`와 `build`는 **매번 `sync`를 먼저 돌립니다.** 저장소의 문서를 고치고 새로고침만 해도
반영되게 하기 위함입니다.

## 문서를 어디서 가져오는가

`sync-docs.mjs`가 복사합니다. `docs/`는 그 산출물이라 **git이 무시합니다.**

|원본|사이트 안|URL|
|--|--|--|
|`doc/`|`docs/guide/`|`/docs/guide/…`|
|`spec/`|`docs/spec/`|`/docs/spec/…`|
|`samples/*/doc/`|`docs/samples/*/doc/`|`/docs/samples/…`|

**원본을 옮기지 않는 이유**는 문서 60여 편이 상대 경로로 엮여 있고, GitHub에서 그대로 읽히는
것이 지금의 사용 방식이기 때문입니다. 디렉터리 깊이를 보존해서 복사하므로 문서끼리의 링크는
대부분 그대로 맞고, 실제로 고쳐지는 것은 두 가지뿐입니다.

- `doc/` 이름이 `guide/`로 바뀌면서 어긋나는 링크
- 복사 대상 밖을 가리키는 링크(`src/` · `lib/` · 워크북 · 샘플 readme) — 사이트에 그 파일이
  없으므로 **GitHub 주소로** 바꿉니다

## 이 빌드가 게이트입니다

문서에는 지금까지 게이트가 없었습니다. 이 빌드가 그 자리를 맡습니다 — 셋 다 `throw`입니다.

|무엇|잡는 것|
|--|--|
|`onBrokenLinks`|없는 페이지를 가리키는 링크|
|`onBrokenMarkdownLinks`|없는 파일을 가리키는 마크다운 링크|
|`onBrokenAnchors`|**이름이 바뀐 섹션을 가리키는 앵커** — 실제로 두 개를 검출했습니다|

## 알아둘 것

- **`.md`는 MDX가 아니라 보통 마크다운으로 읽습니다**(`markdown.format: 'md'`). 문서에
  `<Field>`·`{}` 같은 표기가 그대로 들어 있어서, MDX로 파싱하면 전부 문법 오류가 됩니다.
- **`package.json`에 `"type": "module"`을 넣지 마세요.** 넣으면 서버 번들 평가가
  `require.resolveWeak is not a function`으로 실패합니다. 설정 파일은 그래서 `.mjs`입니다.
- 사이드바는 `sidebars.mjs`에 손으로 적습니다. 자동 생성은 파일 이름 순서라 읽는 순서가
  나오지 않습니다. 구성은 [doc/readme.md](../doc/readme.md)와 같게 유지합니다.

## 검색

빌드할 때 색인을 만들어 사이트 안에 넣습니다(`@easyops-cn/docusaurus-search-local`). Algolia
DocSearch 는 신청과 승인이 필요하고 크롤러가 사이트에 닿아야 하는데, 배포 전에는 그 조건이
성립하지 않습니다.

**`language` 에 `ko` 가 들어가는 것이 이 문서 묶음에서는 필수입니다.** 한국어를 띄어쓰기만으로
자르면 조사가 붙은 낱말이 서로 다른 토큰이 되어, 「시트를」로 찾을 때 「시트」가 안 나옵니다.

## 브랜드

파비콘·navbar 로고·OG 카드는 [brand/](../brand/readme.md)가 만들어 `static/img/` 로 넣습니다.
사이트 쪽에서 이미지를 손으로 두지 않습니다 — 로고가 바뀌면 그쪽 스크립트만 다시 돌립니다.

## 아직 안 한 것

- 커스텀 도메인 (지금은 `maxidea1024.github.io/tabbit`)
- 영문 문서 — i18n 뼈대를 아직 깔지 않았습니다
