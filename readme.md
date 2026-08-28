<a href="https://maxidea1024.github.io/tabbit/">
  <img src="brand/dist/readme-header.jpg" alt="Tabbit — 스프레드시트의 게임 데이터를 검증하고 런타임 데이터로 빌드합니다" width="100%">
</a>

# Tabbit

**Game Data Compiler** — 엑셀과 구글 스프레드시트에 적은 게임 데이터를 검증하고, 런타임이 그대로
싣는 데이터와 그것을 읽는 코드로 빌드합니다.

[![.NET](https://github.com/maxidea1024/tabbit/actions/workflows/dotnet.yml/badge.svg)](https://github.com/maxidea1024/tabbit/actions/workflows/dotnet.yml)
[![Names](https://github.com/maxidea1024/tabbit/actions/workflows/names.yml/badge.svg)](https://github.com/maxidea1024/tabbit/actions/workflows/names.yml)
[![Docs](https://github.com/maxidea1024/tabbit/actions/workflows/docs.yml/badge.svg)](https://maxidea1024.github.io/tabbit/)
[![release](https://img.shields.io/github/v/release/maxidea1024/tabbit?sort=semver)](https://github.com/maxidea1024/tabbit/releases)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

시트에서 런타임까지 사이에 있는 것은 직접 만들 수 있습니다. 대부분의 팀이 이미 만들었습니다.

**이 도구가 대신하는 것은 만드는 일이 아니라, 언어가 둘로 늘고 라이브가 시작된 뒤에도 그것을
유지하는 일입니다.** 내보내기, 변환 스크립트, 타입 맞추기, 검증, 참조 확인, 직렬화, 그리고
언어마다 손으로 유지하던 리더입니다.

**시트 작성 방식은 그대로 둡니다.** 기획자는 계속 엑셀과 구글 스프레드시트를 쓰고, 전용 에디터를
새로 배우지 않습니다.

---

## 시트 한 장에서 나오는 것

셀에 `:table`을 적은 자리가 테이블입니다. 그 아래 `:field`와 `:type` 줄이 컬럼의 이름과 타입이고,
마커 열이 빈 행부터 데이터입니다.

![테이블 Item](doc/figures/concepts-item.svg)

이 시트가 저장소의 [`core` 픽스처](test/fixtures/xlsx/core)이고, 아래 코드는 회귀 테스트가 매번
비교하는 [골든 트리](test/fixtures/golden/core/csharp/tables/ItemTable.cs)에서 가져온 것입니다.
지어낸 예제가 아닙니다 — 폭 때문에 컬럼 둘과 네임스페이스 수식만 뺐습니다.

```bash
tabbit --recipe recipe.json
```

```csharp
public partial class ItemRecord
{
    public int Index => ...;                     // primary index   ← :desc 가 그대로 실립니다
    public string Name => ...;                   // item name
    public int CategoryId => ...;                // 셀에 적힌 키
    public ItemCategoryRecord ItemCategoryByCategoryId => ...;   // 그 키가 가리키는 행
    public Grade GradeField => ...;              // 시트에는 Common 이라고 적습니다
    public int Price => ...;                     // :target 이 s 이므로 서버 빌드에만
}
```

```csharp
await GameData.ReadAllAsync("./data");

var sword = GameData.Item.FindByIndex(1);

sword.Name;                              // Short Sword
sword.ItemCategoryByCategoryId.Name;     // Weapon   ← 조회를 한 번 더 하지 않습니다
sword.GradeField;                        // Grade.Common
```

레코드 타입과 함께 나오는 것은 인덱스별 조회 함수 셋(`FindByIndex` · `GetByIndexOrThrow` ·
`ContainsIndex`), 런타임 데이터 파일, 그리고 그 파일을 읽는 테이블 리더입니다.

<details>
<summary><b>같은 시트에서 다른 언어로</b></summary>

<br>

C#(.NET과 유니티), TypeScript, C++, C, Go, Rust, Python, Java, Kotlin, Swift, Lua, Ruby, PHP,
Dart와 언리얼 모듈이 같은 시트에서 나옵니다. 표기는 각 언어의 관례를 따릅니다 — TypeScript는
`tables.item.findByIndex(1)`, Python은 `tables.item.find_by_index(1)`입니다.

**[시트가 코드가 되는 모습](doc/generated-code.md)** 이 같은 시트를 언어마다 나란히 놓습니다.
그 문서도 생성물이라 사람이 옮겨 적은 자리가 없습니다.

리더는 언어마다 별도 구현이므로 어긋날 수 있고, 그래서 경계값을 담은
[적합성 코퍼스](doc/languages/readme.md#테이블-리더가-어긋나지-않는다는-근거)가 있습니다. CI가 그
테이블을 모든 언어로 컴파일해 실행하고, 읽은 값을 익스포터의 출력과 필드 단위로 대조합니다.

적용 방법은 [언어별 가이드](doc/languages/readme.md)에 언어마다 한 장씩 있습니다.

</details>

## 시트와 런타임 사이에 있는 것

스프레드시트에서 런타임까지 대개 이런 단계가 있습니다.

```
스프레드시트
  ↓  내보내기          CSV·JSON 으로 빼내기
  ↓  변환 스크립트      팀마다 하나씩 있는 그 스크립트
  ↓  타입 맞추기        문자열을 숫자·enum·배열로
  ↓  검증              범위 · 필수 · 중복 · 참조
  ↓  직렬화            런타임이 읽을 형식으로
  ↓  언어별 리더        클라이언트와 서버가 다른 언어라면 두 벌
런타임
```

Tabbit을 넣으면 그 자리가 명령 한 줄과 recipe 한 장이 됩니다.

```
스프레드시트
  ↓  tabbit --recipe   검증 → 인코딩 선택 → 빌드
런타임
```

| 매번 하던 것 | Tabbit이 하는 것 |
| --- | --- |
| 내보내기와 변환 스크립트 | `.xlsx`와 구글 스프레드시트를 직접 읽습니다 |
| 타입 맞추기 | 시트의 `:type` 줄이 타입입니다 — 정수, 문자열, enum, 배열, [중첩 레코드](spec/types/nested-fields.md), [벡터와 색](doc/sheets/types.md#합성-값-타입--벡터--회전--색) |
| 참조 확인 | `foreign`으로 적으면 없는 키를 빌드가 거부하고, 로드한 뒤에는 실제 레코드 참조로 연결됩니다 |
| 검증 코드 | 범위·허용값·필수는 [시트에서](spec/layout/column-constraints.md), 그 밖의 규칙은 [C#으로](doc/validation.md) |
| 직렬화 형식 관리 | [컬럼 지향 바이너리](doc/binary-format.md) 하나입니다. 스키마가 달라졌을 때 조용히 잘못 읽지 않습니다 |
| 언어별 리더 | 생성 코드에 딸려 나옵니다. 추가로 설치할 것이 없고, Go는 `go.mod`, Rust는 `Cargo.toml`, 언리얼은 `Build.cs`까지 함께 나옵니다 |

## 엑셀은 그대로

### 시트에 데이터를 적는 분

알아야 하는 특수문자는 두 개입니다 — `:`가 붙은 셀은 데이터가 아니라 표의 뼈대이고, `#`가 붙은
것은 변환에서 빠집니다.

- 행과 열은 지금 쓰는 그대로입니다. 표의 위치도 자유롭고, 한 시트에 표 여러 개를 놓아도 됩니다.
- **Tabbit의 규칙으로 쓰이지 않은 시트도 읽습니다.** 레이아웃을 소스마다 지정하므로 시트를 먼저
  고칠 필요가 없습니다.
- 문제가 있으면 **어느 파일, 어느 시트, 어느 셀인지** 함께 보고합니다. 구글 스프레드시트라면
  링크를 눌러 그 셀로 이동합니다.
- 여러 문제를 한 번에 모아서 보고합니다. 하나 고치고 다시 실행하기를 반복하지 않습니다.

[기획자용 빠른 시작](doc/quickstart-designer.md)이 이 한 장으로 끝납니다.

### 도구를 붙이는 분

- 파서와 직렬화기와 검증 코드를 손으로 쓰지 않습니다.
- 서버와 클라이언트가 같은 시트를 씁니다. 컬럼에 `:target`을 적으면 그쪽 빌드에만 들어가므로,
  클라이언트가 받는 파일에는 서버 전용 컬럼이 **아예 없습니다.**
- 변경되지 않은 데이터는 다시 만들지 않고, `--validate-only`는 산출물 없이 검사만 합니다.
  PR 검사가 쓰는 방식입니다.
- 빌드가 중간에 실패해도 이전 결과가 그대로 남습니다. 파일은 스테이징 영역에 모았다가 마지막에
  옮기고, 데이터베이스는 섀도 테이블을 채운 뒤 교체합니다.

[개발자용 빠른 시작](doc/quickstart-developer.md) — 설치부터 코드에서 읽기까지.

## 데이터만 올렸을 때 생기는 일

등급 하나를 추가한다고 하겠습니다. enum `Grade`에 `Mythic = 5`를 더하고 아이템 몇 개를 그 등급으로
바꿔 CDN에 데이터만 올립니다.

- **읽기는 실패하지 않습니다.** 값 `5`는 그대로 도착하고 로딩도 정상적으로 끝납니다.
- 아직 업데이트하지 않은 클라이언트에는 그 이름이 없습니다. `switch`의 어느 가지에도 걸리지 않아
  등급 테두리가 그려지지 않거나, 엉뚱한 등급으로 보입니다.
- **로그에는 아무것도 남지 않습니다.** 문의가 들어와야 알게 됩니다.

이 일이 벌어지는 이유는 enum과 상수셋이 데이터가 아니라 **코드로 나가기** 때문입니다. 테이블의
행과 컬럼은 데이터만 올려도 반영되는데, 그 둘은 그렇지 않습니다.

| 무엇이 지키나 | 무엇을 |
| --- | --- |
| `SchemaBaseline` | recipe에 켜 두면, 이미 배포된 빌드가 읽지 못할 컬럼 변경은 데이터를 쓰기 전에 멈춥니다 |
| [히스토리](doc/history.md) | 베이스라인이 보지 않는 enum과 상수셋을 **커밋마다 판정합니다** — 이번 변경이 데이터만 올려도 되는 것인지, 코드 배포가 함께 필요한 것인지 |

상수만 고쳐 놓고 데이터를 올리는 헛배포도 같은 목록에 올라옵니다. 상수셋은 데이터 파일에 흔적이
전혀 없어서 빌드도 성공하고 출력도 정상이고 매니페스트 해시까지 그대로이므로, 알려주는 신호가
어디에도 없는 유일한 경우입니다.

> 이런 경우 6가지가
> [라이브 서비스에서 실제로 겪는 모습](doc/languages/readme.md#라이브-서비스에서-실제로-겪는-모습)에
> 증상과 대처까지 적혀 있습니다. 스토어 심사 사흘 동안 무엇이 견디고 무엇이 견디지 못하는지도
> 거기 있습니다.

## 데이터를 보고 고르는 인코딩

**컬럼마다 후보를 전부 인코딩해 보고 가장 작은 것을 고릅니다.** 사전, RLE, 델타, 비트폭
패킹입니다. 어느 것도 특정 테이블을 노린 것이 아니고, 기획 데이터의 컬럼이 대체로 그렇게 생겼기
때문에 그 선택이 이런 결과를 냅니다 — id는 증가하고, 분류는 몇 가지뿐이고, 수치는 구간마다
등차입니다.

서로 다른 정수 95,490개가 든 `Id` 컬럼이 담고 있는 정보는 「1,401,000에서 1씩 95,489번」이고,
형식에는 그렇게 적힙니다.

| 71개 테이블 · 109,218행 | 바이너리 `.tcb` | JSON |
| --- | --: | --: |
| 파일 크기 | **0.51 MB** | 14.08 MB (27.5배) |
| 전체 로드, 소요시간 | **12.6 ms** | 45.3 ms (3.6배) |
| 로드 중 총 할당 | **12.2 MB** | 30.6 MB (2.5배) |
| 로드 후 상주 메모리 | **11.4 MB** | 15.1 MB |

압축하지 않은 `.tcb`(0.51 MB)가 gzip한 JSON(0.65 MB)보다 작습니다. 압축 라이브러리를 모든 언어에
다는 대신 형식 안에서 같은 반복을 지운 결과입니다.

> 측정 조건과 컬럼별 내역, compact JSON까지 포함한 표는 [벤치마크](doc/benchmark.md)에 있습니다.

## 믿을 근거

**이 도구는 한동안 실제 상용 프로젝트의 워크북으로 검증했습니다.** 그 데이터는 회사 자산이므로
공개할 수 없고, 이름만 바꾸는 것으로는 부족합니다 — 컬럼 구성과 밸런스 곡선 자체가 노하우일 수
있기 때문입니다. 그래서 저장소의 샘플은 전부 합성이고, 생성기가 재현하는 것은 값이 아니라
**분포**입니다([경위](samples/readme.md#합성인-이유)).

나머지는 저장소를 열어 확인할 수 있는 것들입니다.

| 무엇 | 어떻게 확인하나 |
| --- | --- |
| 생성 결과 | 골든 트리와 바이트 단위로 비교합니다. 동작을 보존하는 변경이라면 한 바이트도 바뀌지 않아야 합니다 |
| 언어별 리더 | 경계값을 담은 적합성 코퍼스를 모든 언어로 컴파일해 실행하고, 익스포터의 출력과 필드 단위로 대조합니다 |
| 그 방식의 성과 | `long`을 32비트로 잘라내던 writer 버그를 실제로 그렇게 찾아냈습니다 |
| 세 형식이 같은 값인지 | 벤치마크 하네스의 `verify`가 109,213행을 형식마다 필드 단위로 대조합니다 |

## 테이블 하나부터

프로젝트 전체를 한 번에 옮기지 않아도 됩니다.

- **선언 셀을 적은 자리만 읽습니다.** 같은 워크북의 다른 시트는 대상이 아니고, 폴더나 파일 이름
  앞의 `#`이 그것을 제외합니다.
- **레이아웃은 소스마다 지정합니다.** 기존 규칙으로 쓰인 시트와 Tabbit 규칙으로 쓴 시트를 한
  recipe에서 섞어 읽고, 한쪽에서 선언한 enum을 다른 쪽 테이블이 타입으로 사용해도 됩니다.
- 출력 대상도 recipe에 적힌 대로입니다. 테이블 하나를 JSON으로 내보내 지금 쓰는 파일과 비교해
  보는 것이 가장 짧은 첫걸음입니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",       "Layout": "tabbit" },
  { "Path": "./other-sheets", "Layout": "sheet-per-table" }
]
```

**빼는 것도 폴더 하나입니다.** 생성된 코드는 그 자체로 완결된 패키지이고, 기본값에서는 새로
설치할 라이브러리가 없습니다. 그만 쓰기로 하면 생성 폴더와 recipe를 지우면 됩니다 — 스윕이 헤더에
`Generated by Tabbit`이 적힌 파일만 지우므로, 남의 소스가 든 폴더를 가리켜도 안전합니다.

코드와 문서는 MIT이고, 시트에서 생성된 코드와 데이터의 소유권은 그것을 만든 프로젝트에 있습니다.
텍스트로 가져가야 하면 JSON으로도 나갑니다.

규모가 있는 시트 두 벌을 손대지 않고 한 모델로 읽습니다 — [`sprout`](samples/sprout)이 워크북
17개·테이블 71개·109,218행이고, [`canopy`](samples/canopy)가 워크북 42개·정의된 이름 549개·셀
873만입니다.

## 시작하기

내려받아 압축을 푸는 것으로 끝납니다. .NET 런타임을 따로 설치하지 않아도 됩니다 — 런타임이 실행
파일 안에 들어 있습니다. [릴리즈](https://github.com/maxidea1024/tabbit/releases)에 Windows,
Linux, macOS의 x64와 arm64가 올라갑니다([설치](doc/install.md)).

```bash
tabbit --new-recipe my-recipe.json --template unity
tabbit --recipe my-recipe.json
```

`--template`이 상황에 맞는 recipe를 내놓고, 설정마다 무엇을 위한 것이고 언제 바꾸는지 주석이
붙어 있습니다.

| 템플릿 | 무엇을 위한 것 |
| --- | --- |
| `unity` | 엑셀 → StreamingAssets(`.bytes`) + C# + HTML 문서 |
| `client-server` | 같은 시트에서 두 벌 — `TargetSide`로 가른 바이너리 둘, C#(클라)과 Go(서버) |
| `web` | 구글 스프레드시트 → JSON + 바이너리 + TypeScript + HTML |
| `server` · `unreal` · `ci` | 게임 서버, 언리얼 모듈, 빌드 파이프라인 |

[시트 한 장으로 시작하는 예제](doc/concepts.md)가 그다음입니다.

## 한 번의 실행이 하는 일

| 단계 | 하는 일 |
| --- | --- |
| **Author** | 스프레드시트에 테이블, enum, 상수셋을 작성합니다 |
| **Schema** | 시트에서 타입, 인덱스, 참조, 제약을 읽습니다 |
| **Validate** | 타입으로 표현할 수 없는 규칙까지 검사하고, 문제가 발생한 셀을 알려줍니다 |
| **Analyze** | 생성된 데이터를 HTML로 확인하고, 커밋 사이의 변경을 셀 단위로 추적합니다 |
| **Optimize** | 컬럼의 데이터 특성에 맞는 인코딩을 선택합니다 |
| **Build** | 런타임 바이너리와 이를 읽는 코드를 언어별로 생성합니다 |

파일 대신 데이터베이스로 바로 적재할 수도 있습니다 — MySQL, PostgreSQL, MongoDB, Redis입니다.
[기능 전체 목록](doc/features.md)에 하는 일이 한자리에 있습니다.

## 문서

[문서 사이트](https://maxidea1024.github.io/tabbit/)에 전부 있습니다.
저장소에서는 [문서 목록](doc/readme.md)이 같은 자리입니다.

| 문서 | 내용 |
| --- | --- |
| [시트 작성](doc/sheets.md) | 데이터 배치, 엔티티 마커, 이름 규칙, 지원 타입 |
| [CLI](doc/cli.md) · [Recipe 파일](doc/recipe.md) | 실행 방법과, 어디서 읽고 어디로 출력할지 |
| [언어별 가이드](doc/languages/readme.md) | 생성된 코드를 프로젝트에 적용하는 방법 |
| [검증](doc/validation.md) | 시트로 표현할 수 없는 규칙을 C#으로 |
| [내보내기](doc/exports.md) · [바이너리 형식](doc/binary-format.md) | 출력 대상과 런타임 데이터 형식 |
| [벤치마크](doc/benchmark.md) | JSON, compact JSON, 바이너리의 크기와 로드 비용 |
| [트러블슈팅](doc/troubleshooting.md) | 빌드 실패 시 실제 출력 메시지를 기준으로 |
| [설계 노트](doc/readme.md#설계-노트) | 기능과 형식이 현재 구조가 된 이유 |

## 기여하기

버그와 제안은 [이슈](https://github.com/maxidea1024/tabbit/issues)로 알려주세요.

- 개발과 테스트 방법은 [아키텍처와 개발](doc/architecture.md)에 있습니다.
- 생성기나 템플릿을 수정하였다면 골든 데이터를 다시 기록하고 diff를 확인해 주세요.
- 보안 문제는 공개 이슈가 아닌 [SECURITY.md](SECURITY.md)의 절차를 따라주세요.

변경 내역은 [CHANGELOG.md](CHANGELOG.md), 사용하는 외부 패키지는
[의존 패키지](doc/dependencies.md)에 있습니다.

## 라이선스

코드와 문서는 [MIT](LICENSE)입니다. 생성된 코드와 함께 제공되는 테이블 리더 및 업데이터도
동일합니다. 생성물에 이 저장소의 라이선스를 표시할 의무는 없으며, 시트에서 생성된 코드와
데이터의 소유권은 이를 만든 프로젝트에 있습니다.

**이름과 로고는 다릅니다** — [브랜드 자산의 이용 조건](brand/license.md). 가리키는 데는
마음껏 쓰시고, 후원이나 제휴로 읽힐 자리에는 두지 말아 주십시오.
