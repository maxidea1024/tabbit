# wildling — 가상 게임으로 만든 기능 전수 샘플

[`sprout`](../sprout)과 [`canopy`](../canopy)는 **남의 규칙으로 쓰인 시트를 읽어낸다**의
증거입니다. 그래서 시트 표현력은 그쪽 시트가 쓴 만큼만 나옵니다.

`wildling`은 **우리 규칙으로 쓰면 무엇을 적을 수 있나**의 증거입니다. 게임을 하나 기획하고,
그 기획이 요구하는 데이터를 이 도구의 표기로 적었습니다.

```
dotnet run --project src/Tabbit.csproj -- --recipe samples/wildling/design-data/recipe.jsonc
```

## 여기 있는 것

|무엇|어디|
|--|--|
|기획 데이터 전부|[design-data/](design-data) — 워크북 9개 · 격자 40개 · `.tbs` 3개 · 검증 규칙 8개 · recipe|
|**플레이되는 게임**|[unity/](unity) — 생성 C#, `.bytes`, 그림 201장, 그리고 그것을 읽어 도는 게임|
|읽는 산출물|[design-data/out/](design-data/out) — HTML 문서 · 인코딩 보고서 · 스키마 기준선. **유니티 애셋이 아닌 것들**|
|문서|[doc/](doc)|

기획 데이터를 폴더 하나에 모은 것은 **그것이 게임의 한 부분**이기 때문입니다. 그림은 기획
데이터가 아니라 게임 애셋이므로 유니티 안에 있고, 그래서 `asset` 검사가 자리표가 아니라 실제로
게임이 로드할 파일을 봅니다.

## 문서

|문서|내용|
|--|--|
|[기획서](doc/game-design.md)|시스템 기획서 20절. **데이터가 왜 그 형태여야 하는지의 근거**입니다|
|[데이터 설계](doc/data-design.md)|테이블 40개 · `.tbs` 선언 · **기능 배치표**|
|[대조표](doc/coverage.md)|**기능 하나가 어디에 어떻게 쓰였고 무엇이 나왔는가.** 이 폴더의 목적입니다|
|[도구 보고](doc/tool-findings.md)|변환에서 찾은 것 **11건**과 그 결말. **전부 닫혔습니다**|
|[적용 기록](doc/applied.md)|데이터 쪽에서 고친 9건. 전부 규격을 잘못 읽은 것입니다|
|[유니티 게임](doc/unity-game.md)|**화면 · 규칙 · 그림 · 확인 절차.** 게임 쪽의 규격입니다|
|[게임 보고](doc/game-findings.md)|**게임을 돌렸을 때 나온 것 6건.** 변환도 검증도 잡지 못한 갈래입니다|

## 이 샘플의 목적

**표기를 대조하는 것과, 그 표로 실제로 게임을 돌리는 것 둘입니다.**

처음 끝까지 변환하면서 도구 쪽 결함 11건이 나왔습니다. 그중 둘은 **생성 C#이 컴파일되지 않는
것**이었고, `csharp` 타깃은 파일을 쓰기만 하므로 그 자리에서는 성공으로 보였습니다. 그래서
[대조표](doc/coverage.md)에 빈 칸이 없는 것이 이 폴더의 첫째 값입니다.

**둘째 값은 그 다음 질문의 답입니다** — 생성된 조회 표면이 쓸 만한가. 표를 읽어 게임을
만들었더니 6건이 더 나왔고, **그중 하나는 지역 2가 열리지 않아 게임이 첫 지역에서 끝나는
것**이었습니다. 변환은 끝까지 돌았고 값도 시트대로였으므로 어느 검사에도 걸리지 않았습니다.
[게임 보고](doc/game-findings.md)에 전부 있습니다.

## 데이터의 흐름

```
design-data/data/*.tsv      격자. 사람이 고치는 정본
        ↓  design-data/tools/Authoring     서식만 얹습니다
design-data/*.xlsx           워크북 9개 · 탭 39개
        ↓  recipe.jsonc
unity/Assets/Tabbit/Generated       C#
unity/Assets/StreamingAssets/tables `.bytes`
design-data/out/html                  사람이 읽는 문서
```

`.tsv` 하나가 **시트 하나의 격자 그대로**입니다. 저작기는 값을 계산하지 않으므로 밸런스 수정이
`.tsv` 하나의 diff로 남습니다. 형식은 [design-data/data/readme.md](design-data/data/readme.md)에
있습니다.

## 다시 만들기

```
python samples/wildling/design-data/tools/seed.py        # 격자를 처음 만들 때만
dotnet run --project samples/wildling/design-data/tools/Authoring   # 격자 → 워크북
dotnet run --project src/Tabbit.csproj -- --recipe samples/wildling/design-data/recipe.jsonc
```

**`seed.py`는 처음 한 번만입니다.** 다시 돌리면 손으로 고친 값이 사라집니다 — 정본은 `.tsv`
입니다.

## 유니티에서 확인

```
Unity.exe -batchmode -quit -projectPath samples/wildling/unity \ 
          -executeMethod Wildling.Check.WildlingDataCheck.RunFromCommandLine -logFile -
```

에디터에서는 `Wildling ▸ 데이터 확인` 입니다. **보고는 파일로 나갑니다** —
[design-data/out/unity-check.txt](design-data/out/unity-check.txt) 이고 첫 줄이 `OK` 또는
`FAIL` 입니다. 종료 코드에 맡기지 않는 이유는 그 스크립트에 적어 두었습니다.

|보는 것|무엇이 확인되는가|
|--|--|
|행 수 9개 테이블|`.bytes` 를 읽었는가|
|`hp` · `element` · `grade`|**맞게** 읽었는가|
|`habitat`|`bitset` 이 값 하나로 왔는가|
|`tags`|셀 배열이 원소 3개로 왔는가|
|각성 후 행|참조가 링킹으로 **행**이 되었는가|
|보상 변종 3종|판별자가 변종 타입으로 왔는가 — `is` 로 좁혀지는가|
|`ItemByItemId`|**변종의 참조**가 행으로 연결되었는가|
|`FindByRegionIdAndHourBand`|복합 키 조회가 나왔는가|
|출현 28종|멀티 로우가 원소로 쌓였는가|
|상수셋|코드로 나갔는가|

### 규칙의 확인

값이 맞는 것과 그 값으로 게임이 도는 것은 다릅니다. 그래서 검사가 하나 더 있습니다.

```
Unity.exe -batchmode -quit -nographics -projectPath samples/wildling/unity           -executeMethod Wildling.Check.WildlingPlayCheck.RunFromCommandLine -logFile -
```

에디터에서는 `Wildling ▸ 자동 플레이 검사` 이고, 보고는
[design-data/out/unity-play.txt](design-data/out/unity-play.txt) 입니다.

**사람 없이 핵심 루프를 한 바퀴 돕니다** — 새 판을 만들고, 파티를 세우고, 키우고, 탐사를
8시간 정산하고, 각성하고, **막히면 다시 키워서** 스테이지 18개와 수호자를 넘고, 다음 지역을
열고, 세이브를 되읽습니다. 기획서 2.1 의 한 바퀴가 그대로 검사입니다.

### 화면의 확인

빌드가 스스로 화면을 돌며 그림을 남깁니다. **마우스를 쓰지 않습니다.**

```
wildling.exe -screen-width 540 -screen-height 960 -screen-fullscreen 0 -shots <폴더>
```

## 게임 만들기

```
Unity.exe -batchmode -quit -nographics -projectPath samples/wildling/unity           -executeMethod Wildling.Check.WildlingBuild.BuildFromCommandLine -logFile -
```

씬도 이 진입점이 만듭니다 — 씬 파일에는 부트 오브젝트 하나만 들어 있고 화면은 실행 중에
코드가 조립합니다. 자세한 것은 [유니티 게임](doc/unity-game.md)에 있습니다.

## 재검증 절차

```
python samples/wildling/design-data/tools/verify.py
```

**되는지가 아니라 맞는지를 봅니다.** 변환이 성공으로 끝나도 확인되지 않는 것이 있습니다 —
`csharp` 타깃은 파일을 쓰기만 하고 컴파일하지 않고, 리더가 와이어를 무엇으로 검사하는지는
언어마다 따로 적힙니다.

|갈래|검사|
|--|--|
|데이터 자체|참조가 대상을 찾는가 · 복합 키의 조합이 유일한가|
|변환|끝까지 도는가 · 검증 규칙이 도는가|
|생성 코드|**C#이 컴파일되는가** · C# 리더가 배열을 배열로 보는가|
|우회|§2 · §1 · §4 · 5단계의 우회가 남아 있는가|

**우회는 결함의 자리표입니다.** 지워지면 그 검사가 통과로 바뀌고 [도구 보고](doc/tool-findings.md)의
그 항목이 닫힙니다.

### 지금의 기준선 — 2026-08-26

|검사|결과|
|--|--|
|`verify.py`|통과 **9** · 실패 **0** · 건너뜀 1|
|`unity-check.txt`|**OK**|
|`unity-play.txt`|**OK**|
|`unity-build.txt`|**OK** — 96 MB · 오류 0|

**우회가 하나도 남지 않았습니다.** [도구 보고](doc/tool-findings.md)의 **11건이 전부 닫혔고**,
[게임 보고](doc/game-findings.md)의 6건 중 넷을 고쳤습니다. 남은 둘은 읽는 법을 적은 것과
밸런스 결정을 기다리는 것입니다.

---

EOD
