# Summary와 히스토리

누가 언제 무엇을 바꿨는지 셀 단위로 추적하고 브라우저로 확인합니다.

> [문서 목록으로](readme.md)

---

## 무엇을 확인할 수 있나

세 가지입니다.

| 무엇 | 답하는 질문 |
| --- | --- |
| 통계 | 지금 이 커밋의 데이터가 어떻게 생겼나 |
| 히스토리 | A와 B 커밋 사이에 누가 언제 무엇을 바꿨나. 셀 단위로 |
| 배포 판정 | 그 구간을 라이브에 내보내려면 데이터 패치와 코드 배포 중 무엇이 나가야 하나 |

배포 판정은 [아래](#배포-판정--이-구간을-내보내려면-무엇이-나가야-하나)에서 자세히 설명합니다.

## recipe 설정

```json
"Targets": [
  { "Type": "summary", "Path": "./out/summary" },

  { "Type": "history",
    "ConnectionString": "Server=db;Database=tabbit_history;Uid=tabbit;Pwd=${TABBIT_HISTORY_PASSWORD}",
    "ProjectKey": "my-game" }
]
```

`summary`는 빌드마다 `summary.json`을 씁니다.

테이블, 행, 컬럼, 셀 수와 타입 분포, 테이블별과 컬럼별 통계가 들어갑니다.
모든 화면이 이 문서에서 그려집니다.

`run` 블록에는 그 빌드가 무엇이었는지가 들어갑니다.

시각, 도구 버전(`toolVersion`), recipe 이름, `--target-side`, 커밋, 그리고 `environment`입니다.
`environment`는 `--env`로 적은 값이고, 적지 않았으면 `null`입니다.

산출물 폴더를 `Path`로 지정하면 데이터 옆에 함께 나갑니다.
배포된 데이터에서 「어느 환경의, 어느 버전 도구가 만든 것인가」를 되짚을 수 있는 자리가
이것입니다.

`history`는 MySQL에 스냅샷 하나와 거기 이르기까지의 셀 단위 변경을 기록합니다.

비밀번호는 `${NAME}`으로 환경변수에서 받습니다.
recipe는 커밋되므로 직접 적으면 히스토리에 영구히 남습니다.

| 설정 | 기본값 | 의미 |
| --- | --- | --- |
| `ProjectKey` | — | 필수입니다. 한 DB가 여러 프로젝트를 담을 수 있고 이 값으로 구분합니다. 바꾸면 새 히스토리가 시작됩니다 |
| `RecordDirty` | `false` | 커밋되지 않은 변경이 있는 워킹카피의 빌드도 기록할지 |
| `AllowOutOfOrder` | `false` | 브랜치 head보다 뒤진 커밋도 기록할지 |
| `OnFailure` | `warn` | DB에 닿지 못할 때. `warn`이면 빌드는 성공하고 ERROR 로그가 남습니다. `fail`이면 빌드가 멈춥니다 |

## CI에 붙이기

귀속을 정확하게 만드는 것은 워크북이 바뀐 커밋마다 빌드를 한 번씩 돌리는 것입니다.

그렇지 않으면 건너뛴 커밋들의 변경이 다음 스냅샷에 뭉쳐서 그 커밋 작성자에게 귀속됩니다.

```yaml
# .github/workflows/data.yml
on:
  push:
    paths: [ 'design-data/**' ]      # 워크북이 바뀐 커밋에서만

jobs:
  convert:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 2             # 부모 커밋이 있어야 "구멍"을 판정할 수 있습니다

      - run: |
          tabbit --recipe recipe.json             --commit "$GITHUB_SHA"             --branch "${GITHUB_REF_NAME}"
        env:
          TABBIT_HISTORY_PASSWORD: ${{ secrets.TABBIT_HISTORY_PASSWORD }}
```

`--commit`과 `--branch`를 명시하는 이유는 CI 체크아웃이 대개 detached HEAD이기 때문입니다.

그 상태에서는 브랜치를 알 수 없고, 주지 않으면 스냅샷이 브랜치 없이 기록됩니다.

작성자는 커밋에서 자동으로 읽습니다.
git이 없는 빌드 시스템이라면 `--commit-author "이름 <메일>"`로 넘기면 됩니다.

## 기록되지 않는 세 가지

기록하면 그럴듯하지만 틀린 답이 히스토리에 남기 때문에 거부합니다.

각각 로그로 이유를 남깁니다.

**1. 식별되지 않은 빌드**

`--commit`도 없고 git 워킹카피도 아니면 기록할 대상이 없습니다.

**2. dirty 워킹카피의 빌드**

커밋이 설명하지 않는 작업이라 마지막 커밋 작성자에게 잘못 귀속됩니다.
게다가 한 번 넣으면 그 커밋의 깨끗한 빌드는 영영 기록할 수 없습니다.

**3. head보다 뒤진 커밋**

스냅샷은 사슬이고 각각 직전 것과 비교되므로, 새 커밋 뒤에 옛 커밋을 넣으면 새 커밋의 작업이
되돌려진 것으로 기록됩니다.

조상 관계는 타임스탬프가 아니라 git에서 가져옵니다.

## 조회

```bash
# 두 릴리스 사이에 누가 무엇을 바꿨나
tabbit --recipe recipe.json --history --from v1.2.0 --to v1.3.0

# 커밋 해시로도 (앞부분만 써도 됩니다)
tabbit --recipe recipe.json --history --from 4f2a9c1 --to HEAD

# 한 테이블만, 한 사람만
tabbit --recipe recipe.json --history --from v1.2.0 --table Item --author kim

# 터미널에서 읽기 / 자족적 HTML 한 장으로
tabbit --recipe recipe.json --history --from v1.2.0 --format text
tabbit --recipe recipe.json --history --from v1.2.0 --format html --out report.html

# 한 커밋의 통계
tabbit --recipe recipe.json --stats --at v1.2.0
```

`--from`은 제외, `--to`는 포함입니다.

`--from`은 비교의 기준 상태이고, 그 커밋 자신의 변경은 그 앞 구간에 속합니다.

커밋은 앞부분만 써도 됩니다. 애매하면 추측하지 않고 거부합니다.

### 태그와 리비전 표현식

저장된 커밋 해시가 아니면 git에게 물어서 해석합니다.

`v1.2.0`, 브랜치 이름, `HEAD~3`, `abc123^` 전부 가능합니다.

릴리스 사이의 변경을 보는 것이 가장 흔한 용도이고, 버전을 자른 커밋 해시를 외우고 있는 사람은
없습니다.

```bash
tabbit --recipe recipe.json --history --from v1.2.0 --to v1.3.0
```

태그는 빌드를 돌리지 않은 커밋을 가리키는 경우가 흔합니다.
버전 올리는 커밋은 시트를 건드리지 않기 때문입니다.

그럴 때는 그 뒤에 있는 가장 가까운 스냅샷으로 대체하고, 대체했다는 사실을 리포트에 기재합니다.

```
v1.2.0 is 4f2a9c1b8e33, which no conversion ever ran on. Using 8b1d7e40a2f5, the last snapshot before it.
```

오류로 처리하면 해시를 손으로 찾아야 하고, 말없이 대체하면 물어본 것과 다른 질문에 답하게
됩니다.

해석에 필요한 워킹카피는 빌드할 때와 같은 순서로 찾습니다.
`--repository`, 그다음 시트의 소스 디렉터리, 그다음 현재 디렉터리입니다.

체크아웃이 없는 머신에서는 태그를 해석할 수 없고, 「스냅샷이 없다」가 아니라 그 이유를
출력합니다.

`--serve`도 같은 규칙이며, 기동 로그에 어느 워킹카피를 쓰는지 또는 없다는 것을 적습니다.

### 조회 옵션

| 옵션 | 의미 |
| --- | --- |
| `--from` / `--to` | 범위. 커밋 해시(앞부분 가능), 태그, 리비전 표현식. 생략하면 브랜치 처음과 head |
| `--at` | `--stats`가 볼 커밋. `--from`과 같은 형식을 받습니다. 생략하면 head |
| `--table` / `--field` / `--author` | 좁히기 |
| `--project <이름>` | 어느 프로젝트의 기록을 볼지. 생략하면 recipe에 적힌 것을 사용합니다 |
| `--format` | `json`(기본) / `text` / `html` |
| `--out <파일>` | 파일로 출력합니다. 생략하면 표준출력 |
| `--limit <n>` | 최대 변경 건수. 잘린 만큼은 잘렸다고 보고합니다 |

### `--format text` 출력

```
demo-atlas / main
  c001 .. (head)

c002  2026-07-22 14:03  박밸런스 <park@example.com>
    ~ Item[1].Price  100 -> 120    sheets/core.xlsx : Refs : O9

c003  2026-07-24 09:41  박밸런스 <park@example.com>
    ~ Item[2].Price  250 -> 230    sheets/core.xlsx : Refs : O10
    ~ Item[3].Description  Restores 10 HP <or> 5 MP -> HP 10 또는 MP 5 회복    sheets/core.xlsx : Refs : N11
    ~ Item[3].Price  50 -> 40    sheets/core.xlsx : Refs : O11

c004  2026-07-28 16:20  이시스템 <lee@example.com>
    ~ field      Item.Price -> ShopPrice  (renamed, 3 row(s) carried over)

c005  2026-07-30 11:05  김기획 <kim@example.com>
    - Item[3].Name  Small Potion -> (blank)
    - Item[3].ShopPrice  40 -> (blank)
    ...

4 snapshot(s), 1 schema, 8 row and 20 cell change(s).
```

`~`는 수정, `+`는 추가, `-`는 삭제입니다.

오른쪽은 원본 셀 위치이고, 구글 시트라면 그 셀로 가는 URL이 나옵니다.

`c004`가 컬럼 이름 변경입니다.

셀 6건이 아니라 한 줄로 접히고, 값이 그대로 옮겨간 행 수를 적습니다.
값까지 같이 고쳤다면 접지 않고 삭제와 추가로 남습니다.
옮겨지지 않은 값을 옮겨졌다고 기록하지 않기 위해서입니다.

### `--format json` 출력

CLI와 API가 같은 문서를 같은 직렬화기로 내보냅니다.

아래는 위 `c004` 구간이고, 셀 변경은 지면상 생략했습니다.

```json
{
  "schemaVersion": 1,
  "query": {
    "project": "demo-atlas", "branch": "main", "from": "c003", "to": "c004",
    "table": null, "field": null, "author": null,
    "limit": 5000, "truncated": false, "omitted": 0,
    "notes": []
  },
  "snapshots": [
    {
      "commit": "c004", "shortCommit": "c004",
      "authorName": "이시스템", "authorEmail": "lee@example.com",
      "committedAt": "2026-07-28T07:20:00.0000000Z", "subject": null,
      "followsParent": true, "previousCommit": "c003",
      "attributable": true, "pruned": false,
      "counts": { "schema": 1, "rows": 3, "cells": 6 },
      "schema": [
        {
          "entityKind": "Field", "entity": "Item", "member": "ShopPrice",
          "kind": "Modified", "renamedFrom": "Price",
          "before": "{\"comment\":\"shop price\",\"side\":\"s\",\"type\":\"int\"}",
          "after": "{\"comment\":\"shop price\",\"side\":\"s\",\"type\":\"int\"}",
          "location": { "file": "sheets/core.xlsx", "sheet": "Refs", "cell": "O4", "url": null }
        }
      ]
    }
  ],
  "totals": { "snapshots": 1, "schema": 1, "rows": 3, "cells": 6, "gaps": 0, "pruned": 0 }
}
```

읽을 때 놓치기 쉬운 필드들입니다.

| 필드 | 의미 |
| --- | --- |
| `query.notes` | 요청하지 않았는데 수행된 일입니다. 태그를 커밋으로 해석했거나 스냅샷 없는 커밋을 뒤의 것으로 대체했을 때 문장이 들어옵니다. 숫자가 무엇을 뜻하는지가 달라지므로 비어 있지 않으면 읽어야 합니다 |
| `query.truncated` / `omitted` | `--limit`에 걸려 잘렸는지, 몇 건이 빠졌는지. 잘렸는데 표시하지 않으면 「더 이상 변경 없음」으로 읽힙니다 |
| `attributable` | 이 변경을 이 커밋 작성자에게 돌려도 되는지. dirty 워킹카피에서 기록된 스냅샷은 `false`입니다 |
| `followsParent` / `previousCommit` | 직전 스냅샷의 커밋이 이 커밋의 부모인지. `false`면 그 사이 커밋들이 빌드되지 않아 이 변경이 한 사람의 것이 아닙니다 |
| `pruned` | `--prune`으로 상세가 정리된 스냅샷. 변경 목록이 비어 있는 것이 「안 바뀜」이 아니라 「기록이 지워짐」입니다 |
| `renamedFrom` | 컬럼 이름 변경. 있으면 그 컬럼의 셀 변경은 값이 옮겨간 것뿐입니다 |
| `location.url` | 구글 시트일 때 그 셀로 가는 링크. 엑셀이면 `null`이고 `file`, `sheet`, `cell`로 찾습니다 |
| `deployment` | 배포 판정. 스냅샷마다 하나, 문서 전체에 하나입니다. 바로 아래에서 설명합니다 |

## 배포 판정 — 이 구간을 내보내려면 무엇이 나가야 하나

히스토리의 모든 항목이 말없이 제기하는 질문이 하나 있습니다.

「그래서 무엇을 배포해야 하지?」입니다.

셀 수정은 데이터 패치로 나가지만, 상수는 빌드 말고는 어디에도 실리지 않고, enum은 이름이 코드에
숫자가 데이터에 나뉘어 있습니다.

이 답은
[무엇이 코드로 나가고 무엇이 데이터로 나가는지](languages/readme.md#데이터만-나가도-되는-변경과-코드가-함께-나가야-하는-변경)를
이미 아는 사람만 변경 목록에서 읽어낼 수 있습니다.

그래서 히스토리가 대신 판정합니다. 스냅샷마다, 그리고 범위 전체에 대해 판정합니다.

```
c006  2026-08-02 10:15  김기획 <kim@example.com>
    => ship: data + code
    ! enum Grade: labels added while data changed in the same conversion. If rows
      already use the new values, deploy this code before that data reaches builds
      that lack the labels.
    + enumlabel   Grade.Mythic  5
    ~ Item[3].Grade  4 -> 5    sheets/core.xlsx : Refs : F11
...

To ship this range: data + code
  - enum Grade: 1 label(s) added
  - column Item.SellPrice added
  ! enum Grade: labels added while data changed in the same conversion. ...
```

웹 페이지에서는 커밋마다 `DATA`와 `CODE` 칩이 붙고 이유는 툴팁으로 나옵니다.
Changes 카드 위쪽에 범위 전체의 판정이 나옵니다.

JSON에서는 스냅샷마다 `deployment`가 있고 문서 전체에 `deployment`가 하나 더 있습니다.

범위 판정은 스냅샷 판정들의 합집합입니다.
중간의 한 스냅샷이 코드 배포를 요구하면 그 범위 전체가 요구하는 것이기 때문입니다.

```json
"deployment": {
  "data": true,
  "code": true,
  "reasons": [ "enum Grade: 1 label(s) added", "column Item.SellPrice added" ],
  "warnings": [ "enum Grade: labels added while data changed in the same conversion. ..." ]
}
```

### 판정 규칙

[언어별 가이드의 표](languages/readme.md#데이터만-나가도-되는-변경과-코드가-함께-나가야-하는-변경)
그대로입니다.

| 변경 | 판정 |
| --- | --- |
| 행과 셀 값 | `data` |
| 컬럼 추가, 삭제, 이름 변경 | `data` |
| 컬럼 타입, side, 참조 변경 | `data + code` |
| 테이블 추가, 삭제 | `data + code` |
| enum 레이블 (컬럼이 그 enum을 쓸 때) | `code`. 값이 밀렸다면 `data + code` |
| enum 레이블 (어느 컬럼도 쓰지 않을 때) | `code`. 데이터에 값이 실린 적이 없으므로 경고 없음 |
| 상수 세트 | `code`. 데이터 패치는 이 변경을 아무것도 실어 나르지 못합니다 |

### 경고가 붙는 변경

`warnings`는 그중에서도 아무것도 실패하지 않는 변경만 모읍니다.

빌드도 성공하고 로딩도 성공하는데 결과가 틀리는 것들입니다.
파이프라인의 다른 어떤 도구도 이것을 보고할 위치에 있지 않아서 여기서 보고합니다.

| 경고가 붙는 변경 | 왜 |
| --- | --- |
| enum 레이블 값이 밀림 | 이미 내보낸 데이터의 숫자가 다른 레이블을 가리키게 됩니다. 전체 재내보내기와 코드를 함께 배포해야 합니다 |
| enum 레이블 삭제 | 데이터만 롤백하면 이 빌드가 이름을 모르는 값이 다시 나타납니다 |
| 레이블 추가와 같은 빌드에서 데이터 변경 | 새 값을 쓰는 행이 있다면, 그 데이터가 구버전 빌드에 닿기 전에 코드부터 배포해야 합니다 |
| 테이블 삭제 | 그 전에 생성된 빌드는 로드할 때 여전히 그 파일을 찾습니다 |

### 알아둘 것 셋

**판정은 저장되지 않고 읽을 때 계산됩니다.**

규칙이 좋아지면 과거 스냅샷의 판정도 함께 좋아집니다.
이 기능이 생기기 전에 기록된 스냅샷에도 판정이 붙는 이유입니다.

**어느 컬럼도 쓰지 않는 enum은 데이터 쪽 판정에서 빠집니다.**

값이 어느 행에도 실린 적이 없으니, 값을 다시 매겨도 코드 수정일 뿐입니다.

다만 쓰이는지는 브랜치의 현재 컬럼으로 판단합니다.
판정은 「지금 이것을 내보내면」에 대한 답이기 때문입니다.

**정리(prune)된 스냅샷은 판정하지 않습니다.**

증거가 지워진 스냅샷에 「내보낼 것 없음」으로 표시하면 그것은 다른 질문에 대한 답이 됩니다.
`deployment`가 `null`이면 판정 불가이지 「없음」이 아닙니다.

## 웹서버

```bash
tabbit --recipe recipe.json --serve --port 8080
```

`http://127.0.0.1:8080/` 에 대시보드가 뜹니다.

통계 타일, 행 수 추이, 스냅샷별 변경량, 커밋별 변경 목록(원본 셀 딥링크 포함), 작성자별
집계입니다.

API는 `/api/v1` 아래에 있고 전부 GET, 전부 읽기 전용입니다.

```
/api/v1/projects            /api/v1/branches      /api/v1/tables
/api/v1/snapshots           /api/v1/stats         /api/v1/trend
/api/v1/diff                /api/v1/authors       /api/v1/cell
/api/v1/dashboard           /api/v1/healthz
```

쿼리 파라미터는 CLI 옵션과 이름이 같습니다.

`project`, `branch`, `from`, `to`, `at`, `table`, `field`, `author`, `limit`, `metric`, `row`
입니다.

`from`, `to`, `at`은 CLI와 똑같이 태그와 리비전 표현식을 받습니다.

```
/api/v1/diff?project=atlas&from=v1.2.0&to=v1.3.0&table=Item
/api/v1/dashboard?project=atlas&from=v1.2.0
```

응답은 `--format json`과 같은 문서를 같은 직렬화기로 내보냅니다.

`/diff`는 회귀 테스트가 API 응답과 CLI 출력을 바이트 단위로 비교합니다. 답변 생성 시각만
제외합니다.
웹 페이지의 숫자와 터미널의 숫자가 어긋날 수 없는 이유입니다.

`query.notes`는 웹 페이지에도 그대로 표시됩니다.
태그를 대체했다는 안내를 터미널에서만 보고 페이지에서는 못 보는 상황이 생기지 않습니다.

### 서버를 열 때 알아둘 것

- **읽기 전용입니다.** 쓰는 것은 빌드뿐이므로 접속 계정도 읽기 전용을 권장합니다.
- **기본은 127.0.0.1입니다.** `--bind`로 밖에 열려면 `TABBIT_SERVE_TOKEN`이 반드시 있어야 하고,
  없으면 시작을 거부합니다. 열어 놓고 인증을 잊는 것이 이런 도구가 새는 흔한 경로이고, 새면 기획
  데이터 전부와 손댄 사람 전원의 이름이 함께 나갑니다. 요청은 `Authorization: Bearer <token>`
  입니다.
- 스냅샷은 불변이라 모든 응답에 ETag가 붙고 재요청은 304입니다.
- `--serve`는 ASP.NET Core 런타임이 필요합니다. 기본 .NET 런타임만 있는 머신에 배포한다면
  self-contained로 퍼블리시하세요.

## 정리 (Prune)

무한히 자라는 것은 변경 로그입니다.

커밋마다 수정된 셀 하나당 한 행씩 영원히 쌓입니다.
값 풀은 content-addressed라 서로 다른 값의 개수로 묶이고, 어휘가 포화되면 더 자라지 않습니다.

```bash
tabbit --recipe recipe.json --prune --before 90d --keep 200
```

`--before`는 ISO 8601 날짜 또는 `90d` 같은 기간입니다.

기간 쪽이 스케줄 잡에 맞습니다.
날짜는 잡이 매번 다시 계산해 주어야 하고, 그러지 않으면 첫 실행 이후로는 아무것도 정리하지
않습니다.

`--keep`은 컷오프의 하한이지 대안이 아닙니다. 기본값은 100입니다.

1년간 아무도 건드리지 않은 브랜치가 컷오프만으로는 전부 사라져 「히스토리 없는 히스토리」가 되기
때문입니다.

정리된 스냅샷은 행, 통계, 저장된 summary를 그대로 유지합니다.

사라지는 것은 셀 단위 변경 로그뿐이고, 그 구간을 조회하면 「상세가 정리됨」으로 표시합니다.
빈 변경 목록을 「이 커밋은 아무것도 안 바꿈」으로 읽히게 두지 않습니다.

상세가 사라지고 나면 아무도 참조하지 않게 된 값들도 함께 수거됩니다.

> 스냅샷 기록과 prune은 브랜치별 이름 락을 공유합니다.
>
> 같은 브랜치의 두 커밋을 동시에 빌드하면 둘 다 같은 순번을 주장하게 되고(커밋이 다르니 유니크
> 키로는 걸리지 않습니다), 그 이후의 모든 diff가 임의의 한쪽을 기준으로 측정됩니다.
> 락이 그것도 막습니다.

## 현시점에서 남는 한계

**백필하지 않습니다.**

빌드를 돌리지 않은 커밋 구간의 변경은 다음에 성공한 스냅샷에 뭉쳐서 그 커밋 작성자에게
귀속됩니다.

다만 그 구간은 표시됩니다.
스냅샷은 자기 커밋이 직전 스냅샷 커밋의 직계 자식인지를 기록하고, 아니면 리포트와 웹 페이지가
「이 변경은 이 커밋 것만이 아니다」를 함께 표시합니다.

정확한 귀속을 원하면 워크북이 바뀐 커밋마다 CI가 빌드를 돌리면 됩니다.

**한 커밋에 두 사람의 수정이 섞이면 커밋 작성자 한 명으로 기록됩니다.**

xlsx는 바이너리라 git blame이 되지 않으므로 커밋 단위가 천장입니다.

**행은 primary index 값으로 추적합니다.**

키가 바뀐 수정은 삭제와 추가로 보입니다.

**컬럼 이름 변경은 자동으로 인식해서 rename 한 줄로 보고합니다.**

값이 전부 그대로일 때만 그렇습니다.
이름을 바꾸면서 값도 고쳤다면 삭제와 추가로 남습니다.

덜 깔끔하지만, 옮겨지지 않은 값을 옮겨졌다고 기록하지는 않습니다.
