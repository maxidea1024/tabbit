# clover → HYPEPHORIA 리브랜딩 계획

> 작성 2026-09-06 · 계획 문서이고 아직 착수하지 않았습니다.

---

`samples/clover` 를 **HYPEPHORIA**(태그라인 `GET HYPED. GET LUCKY.`)로 옮깁니다.
이 문서는 **지금 저장소에 구현되어 있는 것을 기준으로** 이름이 놓인 자리를 전부 세고,
화면 중에 브랜드를 따라 다시 정해야 하는 것을 가립니다.

**유니티는 이 작업의 범위 밖입니다** — 2026-09-06 결정이고, 당분간 지원 계획이
없습니다. 아래 「유니티 — 범위 밖으로 두는 법」 한 절만 유니티를 다루고, 그 절이 정하는 것은
**무엇을 하지 않는가**입니다.

지금 규모는 **318개 파일 · 2,044곳**입니다(빌드 산출물 · `node_modules` · 캐시 제외).
이 중 상당수가 생성물이므로 손으로 고치는 것은 그보다 훨씬 적습니다 — 아래 2단계에서
가릅니다.

---

## 이름이 놓인 자리 — 갈래 일곱

치환을 한 번에 돌리면 안 되는 이유가 여기 있습니다. **갈래마다 바꾸는 값이 다르고, 바꾸는
비용도 다릅니다.**

|갈래|어디|무엇으로|비용|
|--|--|--|--|
|① 경로|`samples/clover/` · `web/src/generated/clover-data.ts`|`samples/hypephoria/` 등|낮음. 다만 recipe · csproj · 문서 링크가 함께|
|② 코드 식별자|`CloverData`(recipe `AccessorName`) · `Clover.Authoring`(`Program.cs` · `Authoring.csproj` 의 `RootNamespace`) · `dev.tabbit.clover`(안드로이드 패키지 · `MainActivity`)|`HypephoriaData` 등|중간. **생성물은 손으로 고치지 않고 recipe 를 고친 뒤 재생성합니다**|
|③ 저장 열쇠|`clover.options` · `.run` · `.session` · `.collection` · `.challenge` · `.pending` · `.guide.seen` · `.retry` — 8개|`hypephoria.*`|**결정이 필요합니다**. 바꾸면 이미 저장된 판 · 옵션 · 도감이 보이지 않습니다|
|④ 전역 훅|`window.__clover` · `window.__cloverGuest`|`__hype` 등|낮음. 다만 `web/tools/check-*.ts` 9개와 `harness.ts` 가 이것을 씁니다|
|⑤ 표시 이름|`index.html` 의 `<title>` · `title.ts` 의 로고 글 · `login-scene.ts` 의 제목 · `package.json` 셋(web · desktop · mobile)의 `name`/`description` · `capacitor.config.json` 의 `appName`/`appId` · electron 의 `appId`/`productName`/`artifactName` · 서버의 `POSTGRES_DB`/`USER`/`PASSWORD`/`DATABASE_URL`|`HYPEPHORIA`|낮음|
|⑥ 시드 접두어|`CLOVER-` — `title.ts` · `headless.ts` · `server/src/ranked.ts` · 테스트 · 문서|`HYPE-` 등|**높음. 아래 「위험한 것」 참조**|
|⑦ 데이터의 이름|`Tier` 표의 `Clover` 등급(상위 1%) · `TierKind` enum(`meta.tbs` 의 `value Clover = 6`) · 조커 `clover_bloom`(클로버꽃)|아래 5단계|중간. 워크북 · 웹 생성물 · 6개 언어 문자열이 함께|

**저장소 쪽 참조도 함께입니다** — `samples/readme.md` · `doc/architecture.md`(261 · 289행) ·
`spec/ops/lsp.md`(83행) · `spec/targets/csharp-record-name.md`(72행) ·
`tools/vscode/README.md`(100행), 그리고 **`.gitignore` 238~250행의 경로 6개**입니다.
[`.github/workflows/names.yml`](../.github/workflows/names.yml) 의 샘플 폴더 허용 목록에도
`clover` 가 적혀 있습니다 — 폴더를 옮기면 이 게이트가 실패합니다.

**문서 사이트는 따라옵니다.** `website/sync-docs.mjs` 가 `samples/` 를 폴더째 읽어 옮기므로
이름이 바뀌어도 손댈 것이 없습니다. 다만 `website/sidebars.mjs`(207~210행)는 샘플을 하나씩
적고 있고 **거기에 `clover` 가 지금도 없습니다** — 리브랜딩과 별개로 빠져 있는 것이므로,
이 작업에서 새 이름으로 한 줄 더하면 함께 해결됩니다.

---

## 로그인 · 타이틀 화면의 재조정

두 화면은 [`ui/title.ts`](../samples/clover/web/src/ui/title.ts) ·
[`ui/login-scene.ts`](../samples/clover/web/src/ui/login-scene.ts) 이고, 문서는
[`doc/ui/start.md`](../samples/clover/doc/ui/start.md) 입니다. **자리 배치는 그대로 두어도
되고, 다시 정해야 하는 것은 글자 · 그림 · 색 셋입니다.**

### 글자 — 6자에서 10자로

로고는 텍스트 하나입니다(`title.ts` 의 `logo`, 128픽셀 · 자간 10 · 획 12).
그 자리의 주석에 **「이 글은 `clover` 여섯 자로 고정이라 그 한계보다 굵어도 속이 막히지
않습니다」** 라고 적혀 있습니다 — 즉 지금 값은 6글자를 전제로 손으로 정한 값이고,
`HYPEPHORIA` 10글자에는 그대로 쓸 수 없습니다. 크기 · 자간 · 획 굵기 셋을 다시 정합니다.
로그인 화면의 제목(76픽셀)도 같습니다.

**글꼴이 지금은 `noto-sans` 700 입니다.** 브랜드 규격의 「heavy condensed sans」가 아닙니다.
선택지가 둘입니다 —

|방법|장점|단점|
|--|--|--|
|**워드마크를 SVG 로 구워 자산으로 넣기**|자간 · 굵기 · 기울임 · 왜곡을 자유롭게. 스토어 배너 · 아이콘과 같은 파일에서 나옴|글꼴 자산이 하나 늘고, 크기가 다른 자리마다 다시 굽거나 벡터로 얹어야 함|
|표시용 글꼴 하나 더 읽기|글자 하나로 끝남|브랜드가 요구하는 「controlled distortion」이 나오지 않음|

**저장소에 이미 `bungee-700.woff2` 가 있습니다** — 지금은 숫자 전용(`clover-num`)으로만
읽고 있습니다(`ui/font.ts`). 표시용 글꼴을 새로 들이기 전에 이것으로 될지 먼저 봅니다.

### 그림 — 네잎 걷어내기

`title.ts` 의 `drawLeaf()`(322~331행)와 `login-scene.ts`(457~462행)가 **같은 네잎 그림을
각자 그립니다.** 브랜드 규격이 「행운의 상투적 그림 금지」이므로 이것을 걷고 그 자리에
버스트 · 파편 · 호 같은 추상 도형을 둡니다. **두 곳이 각자 그리고 있으므로 이참에 한 자리로
모읍니다** — 지금 상태로 두면 브랜드 그림을 고칠 때마다 두 곳을 고치게 됩니다.

### 색 — `COLOR.good` 에서 떼어낸 브랜드 색

지금 로고 · 잎 · 로그인 제목이 전부 `COLOR.good`(`0x63d68f`, 초록)입니다.
그런데 [`doc/ui.md`](../samples/clover/doc/ui.md) 에 **「승리의 초록은 약속이므로
고정입니다」** 라고 적혀 있습니다 — 브랜드 색을 `COLOR.good` 위에서 바꾸면 게임 안의 승리
표시가 함께 바뀝니다.

그러므로 `theme.ts` 의 `COLOR` 에 **브랜드 색 토큰을 따로 둡니다**(주색 하나 · 대비색
한둘). 겉면 8개는 건드리지 않습니다 — 겉면이 정하는 것은 판 · 테 · 선 · 칸 · 단추이고,
브랜드 색은 겉면을 따라가면 안 되는 고정 색이기 때문입니다.

### 태그라인

지금 `ui.title.tagline` · `ui.title.note` 두 줄이 6개 언어로 적혀 있습니다.
`GET HYPED. GET LUCKY.` 는 영어 슬로건이므로 **6개 언어에 옮길지 영어 하나로 고정할지가
결정 사항입니다**(아래 「결정해야 하는 것」).

---

## 그 밖에 재조정이 필요한 곳

두 화면 외에 검토한 결과입니다.

### 1. 환희의 순간 — 브랜드의 본체

[`render/euphoria.ts`](../samples/clover/web/src/render/euphoria.ts) 가 **이미 있습니다.**
칩 × 배수가 문턱을 넘으면 겹이 오르고 정산에서 터지는 연출이고, 검증 도구
`check-euphoria.ts` 까지 있습니다.

브랜드의 감정 순서가 HYPE → LUCK → **EUPHORIA** 이므로, 이 연출이 브랜드의 이름과 같은
것을 가리키게 됩니다. **다만 지금 문턱 4단의 이름이 `ki_gather` · `ki_wave` 입니다** —
「기를 모으는 겹」이라는 무협풍 표현이고 브랜드와 어긋납니다. 이름과 영상 규격
([`doc/presentation.md`](../samples/clover/doc/presentation.md) 의 「환희의 순간」)을 다시
정할 자리입니다.

문턱 값 자체는 아직 확인용이고 실제 값이 아니라고 파일에 적혀 있으므로, **브랜드 작업과
문턱 확정을 함께 하는 것이 낫습니다.**

### 2. 씬이 갈릴 때 — 작업 중이므로 보류

미커밋 상태로 `render/transition.ts` · `shader/wipe.ts` · `doc/ui/transition.md` 가 있습니다.
브랜드 규격의 「burst · momentum · chain reaction」이 나타날 자리가 바로 여기입니다 —
**브랜드 색과 도형이 정해지기 전에 굳히면 두 번 하게 됩니다.**

### 3. 부팅 화면

`index.html` 안의 `#boot` 입니다. 글자 색이 `#cfe8d6`(연초록) 하나이고, 이 화면만 DOM
입니다(글꼴을 읽기 전에 보이는 화면이라 그렇습니다). 브랜드 색이 바뀌면 여기도 바뀝니다.
판 밖의 색 `#000000` 은 세 곳(`theme.ts` 의 `COLOR.crop` · `index.html` · 데스크탑 창)에
같은 값으로 있으므로, 손대면 셋을 함께 손댑니다.

### 4. 겉면 8개 — 그대로

브랜드 규격에 「지나치게 어두운 팔레트 회피」가 있지만, `doc/ui.md` 에 **「카드가 크림색
종이이므로 여덟 다 어둡습니다」** 라고 그 이유가 적혀 있습니다. 판의 밝기를 올리면 카드가
배경에서 떨어지지 않습니다. **브랜드의 높은 채도는 겉면이 아니라 브랜드 색 토큰과 연출
쪽에서 나오게 합니다.**

### 5. 등급 이름 — 꼭대기의 `Clover`

`Tier` 표 6단의 상위 1% 가 `Clover`(클로버, `#5fd67a`)입니다. 브랜드 이름에서 온 것이므로
함께 바뀝니다. `Euphoria` 가 자연스럽고, 그러면 `meta.tbs` 의 `TierKind` enum · `Tier.tsv` ·
`StringTable.tsv` 의 6개 언어 · 웹 생성물이 따라옵니다.

### 6. 조커 `clover_bloom`

이것은 **브랜드가 아니라 게임 안의 물건 하나**입니다(「클로버꽃」, Common, 5). 브랜드와
무관하므로 남겨도 됩니다. 다만 문자열 게이트로 `clover` 를 금지어로 세울 계획이라면 함께
갈아야 합니다 — 결정 사항입니다.

### 7. 없는 아이콘 · 스플래시

|무엇|지금|
|--|--|
|`web/public/icon/`|도움말 · 설정 아이콘 3개뿐. **파비콘도 앱 아이콘도 없습니다**|
|안드로이드|`ic_launcher` 세트와 `splash.png` 11장이 기본값 그대로|
|데스크탑|electron-builder 에 아이콘 지정이 없습니다|

브랜드 규격의 「런처 아이콘 · 스토어 배너」가 여기입니다. **1단계에서 워드마크를 만들 때
같은 파일에서 파생본을 뽑습니다.**

### 8. 글꼴 패밀리 이름 6개

`clover-kr` · `-jp` · `-sc` · `-tc` · `-latin` · `-num` 입니다(`ui/font.ts`).
갈래 ②에 속하고 비용은 낮습니다.

### 9. 서버 DB 이름

`docker-compose.yml` 의 `POSTGRES_DB`/`USER`/`PASSWORD` 가 전부 `clover` 이고
`DATABASE_URL` 두 곳이 그것을 씁니다. **바꾸면 이미 띄워 둔 볼륨과 어긋납니다** — 개발
기계에서는 볼륨을 버리고 다시 올리는 것이 간단합니다.

### 10. 문서 40여 개

`doc/` 아래 대부분이 머리에 「clover 문서 목록으로」 같은 줄을 답니다. 기계적 치환이지만
**`korean-report-style` 검사기를 그 뒤에 한 번 돌립니다.**

---

## 유니티 — 범위 밖으로 두는 법

**손대지 않는 것이 기본입니다.** 다만 그냥 두면 되는 것이 아니라서 이 절이 있습니다 —
recipe 가 유니티로 두 벌을 내보내고 있고, 폴더 이름이 바뀌면 그 경로가 어긋납니다.

|무엇|어디|
|--|--|
|C# 생성 코드|`recipe.jsonc` 73~78행. `unity/Assets/Clover/Generated` · `Clover.Data` · `CloverData`|
|바이너리 표|`recipe.jsonc` 82~85행. `unity/Assets/StreamingAssets/tables` 로 `.bytes`|
|재생성 확인 목록|`design-data/tools/verify.py` 186행이 `unity/Assets/Clover/Generated/CloverData.cs` 를 봅니다|
|문서|10개 파일에 25곳. `architecture.md` 6 · `readme.md` 4 · `presentation.md` 4 · `progress.md` 3 · `effect-vm/state.md` 3 · 나머지 다섯에 하나씩|

선택지가 셋이고, **어느 것을 골라도 recipe 의 경로 두 줄은 고칩니다**(폴더 이름이 바뀌므로).

|방법|무엇|남는 것|
|--|--|--|
|**걷습니다** (권합니다)|recipe 의 출력 둘을 지우고 `unity/` 트리와 `verify.py` 의 그 줄을 함께 지웁니다|없음. 재생성이 그만큼 빨라집니다|
|출력만 걷습니다|recipe 에서 빼되 트리는 남깁니다|`Assets/Clover/` 폴더와 낡은 생성 코드가 그대로. 다음에 볼 때 이것이 최신인지 알 수 없습니다|
|경로만 옮깁니다|새 폴더 아래로 따라가되 내용은 손대지 않습니다|`Assets/Clover` · `Clover.Data` · `CloverData` 가 새 폴더 안에서 옛 이름으로 남습니다|

**셋째를 고르면 이름이 살아남습니다.** 나머지를 전부 갈아 놓고 한 트리에만 옛 이름이 남으면,
나중에 그 폴더를 여는 사람은 그것이 남겨 둔 것인지 빠뜨린 것인지 가릴 수 없습니다.

### 문서에서 유니티를 뺄 때 함께 정해지는 것

`readme.md` 는 이 샘플의 목적을 **「같은 데이터셋이 서로 다른 두 런타임에서 같은 게임이
되는가」** 라고 적고 있고, `samples/readme.md` 의 표도 그 문장입니다. **유니티를 빼면 이
문장이 성립하지 않습니다.**

다만 그 문장은 이미 지난 것입니다 — 2026-09-01에 판단 기준이 「도구 시연」에서 「게임으로
좋은가」로 옮겨졌고, `readme.md` 만 따라오지 않았습니다. **리브랜딩이 그 문장을 다시 적을
자리입니다.** 문서 6단계에서 함께 합니다.

---

## 위험한 것 — 시드 접두어와 저장 열쇠

### 시드 접두어 — 바꾸면 판이 전부 달라지는 것

시드 글이 RNG 로 들어가므로 `CLOVER-0001` 과 `HYPE-0001` 은 **서로 다른 판**입니다.
따라오는 것이 셋입니다 —

|무엇|어디|
|--|--|
|리플레이 8개 재기록|`npm run bake:replays`. `check:replays` 가 그것을 견줍니다|
|시드와 안테의 표 재측정|`doc/progress.md` 96 · 126~129 · 431 · 450 · 458~460행. 「`CLOVER-0007` 은 여덟이 모두 안테 4를 넘습니다」 같은 문장이 그 시드에만 참입니다|
|테스트 기본값|`web/test/challenge.test.ts` · `server/test/l1.test.ts` · `headless.ts` 의 `--seed` 기본값|

**대안이 하나 있습니다** — 접두어를 브랜드와 무관한 것으로 두는 것입니다. 다만 지금 값에
`CLOVER` 가 들어 있으므로 어떤 값으로든 바뀌고, 바뀌는 순간 위의 셋이 따라옵니다.
**바꾸는 것은 한 번뿐이므로 이 단계에서 확정합니다.**

### 저장 열쇠 — 바꾸면 저장된 것이 보이지 않는 것

`clover.run`(도중에 그만둔 판) · `clover.collection`(도감) · `clover.options`(겉면 · 언어) ·
`clover.session`(로그인) 넷이 그렇습니다. 방법이 둘입니다 —

|방법|무엇|
|--|--|
|**옮깁니다**|처음 켤 때 옛 열쇠를 읽어 새 열쇠로 옮기고 지웁니다. 코드 20줄 안팎이고 한 판 지나면 걷을 수 있습니다|
|**버립니다**|샘플이므로 개발 기계의 저장이 없어져도 됩니다. 대신 도감 진행이 사라집니다|

---

## 단계 순서

**0단계 — 이름 규격 확정.** 아래 「결정해야 하는 것」을 먼저 정합니다. 여기서 정한 것이
1~7단계 전부의 입력입니다.

**1단계 — 브랜드 자산.** 워드마크 · 아이콘 · 색 토큰. **화면보다 먼저인 이유는 화면이
이것을 읽기 때문입니다.** 산출물은 워드마크 SVG · 파비콘 · 런처 아이콘 세트 · 스플래시 ·
`theme.ts` 에 더할 색 토큰 목록입니다.

**2단계 — 기계적 치환.** 갈래 ① · ② · ④ · ⑤. 생성물은 손으로 고치지 않습니다 —
`recipe.jsonc` 의 `AccessorName` · `Namespace` · 경로를 고친 뒤 `--full --force-output` 으로
재생성합니다(붙이지 않으면 캐시 때문에 아무것도 다시 쓰지 않습니다).

**3단계 — 저장 열쇠와 시드.** 0단계의 결정에 따라 옮기거나 버리고, 리플레이를 재기록하고,
`doc/progress.md` 의 시드 표를 재측정합니다.

**4단계 — 로그인 · 타이틀.** 글자 · 그림 · 색 셋. `check-*.ts` 도구가 좌표가 아니라 발행된
자리를 조회하므로 배치를 바꿔도 도구는 따라옵니다.

**5단계 — 데이터의 이름.** `Tier` 의 `Clover` 등급과 `TierKind` enum, 결정에 따라
`clover_bloom`.

**6단계 — 문서.** `doc/` 40여 개 + 저장소 쪽 참조 다섯. 검사기 한 번.

**7단계 — 게이트.** `names.yml` 의 샘플 폴더 목록, 그리고 전체 스위트 한 번.

**2단계와 4단계 사이에 씬 전환 작업을 재개합니다** — 색과 도형이 정해진 뒤라야 두 번 하지
않습니다.

---

## 결정해야 하는 것

|무엇|선택지|
|--|--|
|폴더 이름|`samples/hypephoria/` · `samples/hype/`|
|시드 접두어|`HYPE-` · 그 밖|
|저장 열쇠|옮깁니다 · 버립니다|
|태그라인|6개 언어로 옮깁니다 · 영어 하나로 고정합니다|
|조커 `clover_bloom`|남깁니다 · 갈아냅니다|
|`Tier` 꼭대기 이름|`Euphoria` · 그 밖|

유니티는 결정 목록에 없습니다 — 아래 절이 그 자리입니다.
