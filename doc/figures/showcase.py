# -*- coding: utf-8 -*-
"""시트와 그 시트가 만드는 코드를 나란히 놓은 문서를 생성한다.

    python doc/figures/showcase.py

`doc/generated-code.md` 와 `doc/generated-code/*.md`, 그리고 `doc/figures/showcase-*.svg` 를
다시 씁니다. **그 파일들은 손으로 고치지 않습니다** - 다음 실행이 덮어씁니다.

왼쪽(시트 그림)은 `grid_dump.py` 가 워크북에서 뽑아 둔 격자에서 나오고, 오른쪽(코드)은
회귀 테스트가 매번 비교하는 골든 트리에서 오려 옵니다. 그래서 이 문서에 적힌 것은 전부 어딘가의
게이트가 보고 있는 것이고, 생성기를 고치면 골든이 움직이고 골든이 움직이면 이 문서를 다시
생성해야 합니다.

여기가 정하는 것은 셋뿐입니다 - 어느 엔티티를 보일지, 화살표로 무엇을 가리킬지, 그리고 그
옆에 적을 설명입니다."""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))

# 어디에 쓸지. 기본은 저장소의 자리이고, 환경변수는 게이트가 씁니다 - 임시 폴더에 한 벌 만들어
# 커밋된 것과 비교하고 나면 저장소에는 아무것도 쓰이지 않습니다.
FIGURES = os.environ.get("TABBIT_DOC_FIGURES", HERE)
DOC = os.environ.get("TABBIT_DOC_DIR", os.path.join(REPO, "doc"))
OUT_DIR = os.path.join(DOC, "generated-code")

sys.path.insert(0, HERE)
import grid_dump  # noqa: E402
import showcase_code  # noqa: E402

_spec = importlib.util.spec_from_file_location(
    "layout_figures", os.path.join(REPO, "spec", "layout", "primary-layout-figures.py"))
_figures = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_figures)
_figures.OUT_DIR = FIGURES
build = _figures.build

BANNER = ("<!-- 이 파일은 `doc/figures/showcase.py` 가 생성합니다. 손으로 고치지 마십시오 - "
          "다음 실행이 덮어씁니다. -->")


# ---------------------------------------------------------------- 문서에 실리는 것

# 한 편이 정하는 것:
#
#   slug     파일 이름          title    제목          summary  색인에 적히는 한 줄
#   intro    머리말             tail     꼬리말(선택)
#   blocks   그림과 코드의 짝. 한 편에 여럿을 둘 수 있습니다
#
# 짝 하나가 정하는 것:
#
#   grids    [(격자 이름, 그림 설명, 화살표)]
#   code     (종류, 엔티티) - 종류는 record · enum · const
#   lead     그림 앞에 오는 설명       after  코드 뒤에 오는 설명
#   file     (저장소의 파일, 그 앞에 오는 설명) - 그 파일을 그대로 싣습니다 (선택)
#   extra    (골든의 언어 폴더, 타입 이름, 설명) - 같은 시트의 다른 빌드 (선택)
SECTIONS = [
    # ------------------------------------------------------------------ 기본
    {
        "slug": "table",
        "title": "테이블 하나",
        "summary": "선언 셀 · 헤더 행 · 데이터 행, 그리고 컬럼 하나가 되는 멤버 하나",
        "intro": """가장 작은 테이블부터 봅니다. 여기서 읽는 것이 나머지 전부의 바탕입니다.""",
        "blocks": [{
            "grids": [("showcase-potion", "테이블 Potion", {
                1: "컬럼의 이름이 되는 줄",
                2: "타입. enum은 이름을 그대로 적습니다",
                5: "마커 열이 비면 데이터 행",
            })],
            "code": ("record", "Potion"),
            "lead": """컬럼 넷짜리 테이블입니다. `:field` 가 이름을, `:type` 이 타입을,
`:desc` 가 설명을 정하고, 마커 열이 빈 행부터가 데이터입니다.

**첫 필드 컬럼이 기본 인덱스입니다.** 여기서는 `index` 가 그것입니다.""",
            "after": """읽을 것이 셋 있습니다.

- **컬럼 하나가 멤버 하나입니다.** 이름은 각 언어의 관례를 따라 바뀌지만 순서는 시트의 순서
  그대로입니다.
- **`:desc` 에 적은 설명이 doc comment로 나갑니다.** 시트에 적어 두면 IDE의 툴팁까지 갑니다.
- **`Rarity` 컬럼의 타입이 enum 타입입니다.** 정수가 아닙니다 — 시트에 `Common` 이라고 적고
  코드에서도 `Rarity` 로 받습니다.

조회 함수는 여기 없습니다. 레코드는 값이고, 찾는 일은 테이블이 합니다 —
[행을 찾는 방법](keys.md)에 있습니다.""",
        }],
    },

    # ------------------------------------------------------------------ enum
    {
        "slug": "enum",
        "title": "enum",
        "summary": "`:field` 줄 하나로 끝나는 선언과, 0을 두었을 때와 두지 않았을 때",
        "intro": """enum과 상수셋은 컬럼이 정해져 있으므로 **`:field` 줄 하나**로 끝납니다.
`label` 이 이름, `value` 가 값, `desc` 가 설명입니다.""",
        "blocks": [
            {
                "grids": [("showcase-rarity", "enum Rarity", {
                    1: "헤더 행은 이것 하나뿐",
                    2: "0이 없으므로 None = 0이 붙습니다",
                })],
                "code": ("enum", "Rarity"),
                "lead": """0 항목이 없는 enum입니다.""",
                "after": """**시트에 없던 `None = 0` 이 붙어 있습니다.**

enum 필드는 값이 대입되기 전에도 무언가를 들고 있는데, 그것이 이름 없는 0이면 디버거에서도
로그에서도 읽을 수 없기 때문입니다. `AutoInsertEnumNoneLabel` 로 끌 수 있습니다.

여기는 선언 파일을 통째로 옮겼습니다. enum은 그 정도로 짧습니다.""",
            },
            {
                "grids": [("showcase-element", "enum Element", {
                    2: "0을 직접 두면 손대지 않습니다",
                    3: "선언은 snake_case, 데이터 셀도 그 철자",
                })],
                "code": ("enum", "Element"),
                "lead": """시트가 0에 이미 값을 두었다면 아무것도 더하지 않습니다. 그리고
**라벨을 어떤 철자로 선언하든 데이터 셀에는 그 철자 그대로 적습니다.**""",
                "after": """생성된 타입의 이름은 각 언어의 관례를 따르지만, **시트는 시트의
표기를 지킵니다** — `fire_ball` 이라고 선언했으면 셀에도 `fire_ball` 입니다.""",
            },
        ],
    },

    # ------------------------------------------------------------------ 상수셋
    {
        "slug": "const",
        "title": "상수셋",
        "summary": "행이 없는 값들. 데이터 파일에 흔적이 남지 않습니다",
        "intro": """행이 아니라 이름과 타입과 값의 목록입니다. 한 줄짜리 설정값들이 테이블
흉내를 내지 않아도 되는 자리입니다.""",
        "blocks": [{
            "grids": [("showcase-balance", "상수셋 Balance", {
                1: "이름·타입·값·설명 네 컬럼",
            })],
            "code": ("const", "Balance"),
            "lead": """`:field` 줄 하나에 `name` · `type` · `value` · `desc` 입니다.""",
            "after": """**상수셋은 데이터 파일에 흔적이 전혀 없습니다.** 값을 고쳐도 코드를
다시 배포하기 전에는 아무것도 달라지지 않습니다 —
[엔티티에 따라 갈리는 배포 경로](../concepts.md#엔티티에-따라-갈리는-배포-경로).

언어마다 내는 것이 다릅니다. 클래스의 정적 멤버로 내는 언어가 있고, 모듈 상수로 내는 언어가
있고, C는 매크로로 냅니다 — 그 언어에서 상수를 쓰는 방법이 그것이기 때문입니다.""",
        }],
    },

    # ------------------------------------------------------------------ 타입
    {
        "slug": "types",
        "title": "적을 수 있는 타입",
        "summary": "스칼라 9종이 언어마다 무엇이 되는지",
        "intro": """`:type` 칸에 적을 수 있는 스칼라를 한 테이블에 모았습니다. **타입은 한 칸에
하나씩 적습니다** — 타입과 세부 타입을 두 줄에 나눠 적던 자리가 없습니다.""",
        "blocks": [{
            "grids": [("showcase-sample", "테이블 Sample", {
                2: "여기 적는 이름이 타입입니다",
                5: "bool은 Y·N·빈 칸을 받습니다",
                6: "지수 표기도 그대로 읽습니다",
            })],
            "code": ("record", "Sample"),
            "lead": """`int` · `bigint` · `float` · `double` · `bool` · `string` ·
`datetime` · `timespan` · `uuid` 아홉입니다.""",
            "after": """**언어마다 폭을 다르게 부릅니다.** `bigint` 하나가 C#에서는 `long`,
C++에서는 `int64_t`, Rust에서는 `i64` 입니다 — 값은 같고 부르는 이름만 다릅니다.

`datetime` · `timespan` · `uuid` 는 그 언어에 해당하는 타입이 있으면 그것으로, 없으면
그 언어가 쓰는 표현으로 나옵니다.

빈 칸의 뜻은 타입마다 다릅니다 —
[빈 칸의 뜻](../sheets/rules-and-pitfalls.md#빈-칸의-뜻--자리별-총정리)에 자리별로
정리되어 있습니다.""",
        }],
    },

    # ------------------------------------------------------------ 합성 값과 비트셋
    {
        "slug": "composite",
        "title": "한 칸에 여러 값 — 합성 값과 비트셋",
        "summary": "vec3f·color32가 레코드가 되는 것, 그리고 bitset이 정수가 되는 것",
        "intro": """셀 하나에 적지만 값이 여럿인 타입이 둘 있습니다. **하나는 레코드가 되고,
하나는 정수가 됩니다.**""",
        "blocks": [
            {
                "grids": [("showcase-marker", "테이블 Marker", {
                    2: "성분 수는 타입이 정합니다",
                    5: "쉼표로 끊어 적습니다",
                })],
                "code": ("record", "Marker"),
                "lead": """벡터 · 회전 · 색입니다. 컬럼 하나에 적고, 코드에서는 성분을 가진
타입으로 받습니다.""",
                "after": """**파일에는 성분마다 컬럼 하나로 저장됩니다.** `Pos.X` · `Pos.Y` ·
`Pos.Z` 를 따로 적었을 때와 바이트가 같습니다 — 달라지는 것은 시트에 적는 품과 코드에서
읽는 모습뿐입니다.""",
            },
            {
                "grids": [("showcase-access", "테이블 Access", {
                    2: "bitset과 bigint는 같은 타입이 됩니다",
                    5: "10진수·0x·0b, 그리고 끊어 적는 _",
                })],
                "code": ("record", "Access"),
                "lead": """`bitset` 은 플래그 최대 64개입니다. **생성된 코드에서 `bigint` 와
구별되지 않습니다** — 다른 것은 담는 값이 아니라 셀에서 받아들이는 표기입니다.""",
                "after": """`Flags` 와 `Same` 이 같은 타입입니다. 시트에서 `0b1011` 이라고 적을
수 있는 쪽이 `bitset` 이고, **빈 칸은 오류입니다** — 비트 패턴에 빈 칸이 뜻할 것이 없기
때문입니다.""",
            },
        ],
    },

    # ------------------------------------------------------------------ 역할
    {
        "slug": "roles",
        "title": "문자열의 역할 — text와 asset",
        "summary": "번역을 위해 수집되는 문자열과, 파일이 있어야 하는 문자열",
        "intro": """둘 다 코드에서는 그냥 문자열입니다. **달라지는 것은 빌드가 그 컬럼에
무엇을 더 하느냐입니다.**""",
        "blocks": [{
            "grids": [("showcase-line", "테이블 Line", {
                2: "역할은 타입 뒤 괄호에",
                5: "asset은 실제 파일 이름",
            })],
            "code": ("record", "Line"),
            "lead": """`text` 는 번역을 위해 따로 모이는 문자열이고, `asset` 은 그 이름의
파일이 실제로 있어야 하는 문자열입니다.""",
            "after": """**생성된 코드에는 역할이 남지 않습니다.** 둘 다 문자열 멤버 하나입니다 —
역할은 빌드 시점에 끝납니다.

- `text` — recipe에 번역 파일 타깃을 더하면 그 값들이 거기로 모입니다 ([내보내기](../exports.md))
- `asset` — recipe가 가리킨 폴더에 그 파일이 있는지 확인하고, 없으면 보고합니다

역할이 산출물의 바이트를 바꾸지 않는다는 것이 이 설계의 요점입니다.""",
        }],
    },

    # ------------------------------------------------------------------ 인덱스
    {
        "slug": "keys",
        "title": "행을 찾는 방법",
        "summary": "기본 인덱스 · 보조 인덱스 · 문자열 키 · 컬럼 둘이 키인 것",
        "intro": """조회 함수는 인덱스마다 셋이 생성되고, **이름은 그 인덱스의 컬럼에서
만들어집니다.**

| 함수 | 없을 때 |
| --- | --- |
| `FindBy…` | 널을 반환합니다 |
| `GetBy…OrThrow` | 예외를 발생시킵니다 |
| `Contains…` | 존재 여부만 확인합니다 |

이름이 동작을 설명하므로, 검사를 빠뜨린 자리가 코드를 읽는 것만으로 드러납니다.""",
        "blocks": [
            {
                "grids": [("showcase-animation", "테이블 Animation", {
                    1: "*가 붙으면 보조 인덱스",
                    2: "기본 인덱스가 문자열이어도 됩니다",
                })],
                "code": ("record", "Animation"),
                "lead": """**첫 필드 컬럼이 기본 인덱스**이고, 이름 앞에 `*` 를 붙이면 보조
인덱스가 하나 더 생깁니다.""",
                "after": """조회 함수는 레코드가 아니라 테이블에 생깁니다 — 여기 보이는 것은
멤버뿐입니다. `index` 가 문자열이므로 그 테이블의 조회는 문자열을 받고, `*Slot` 때문에
정수로 찾는 조회가 하나 더 생깁니다.""",
            },
            {
                "grids": [("showcase-stage-reward", "테이블 StageReward", {
                    0: "선언 셀의 괄호에 키를 적습니다",
                    5: "Stage가 겹쳐도 Rank가 다르면 다른 행",
                })],
                "code": ("record", "StageReward"),
                "lead": """어느 컬럼 하나로도 행을 가릴 수 없을 때 **선언 셀의 괄호에 키를
적습니다** — `:table StageReward(key="Stage,Rank")` 입니다.""",
                "after": """키가 둘이면 조회 함수의 인자도 둘입니다 —
`FindByStageAndRank(stage, rank)` 처럼 성분마다 하나씩 생깁니다. 이름이 그 인덱스의 컬럼에서
만들어지므로, **키를 바꾸면 함수 이름이 함께 바뀌고 옛 이름을 부르던 자리가 컴파일에서
드러납니다.**""",
            },
        ],
    },

    # ------------------------------------------------------------------ 와이어 태그
    {
        "slug": "wire",
        "title": "컬럼에 번호 달기",
        "summary": "`@N` — 컬럼을 위치가 아니라 번호로 가리키게 하는 것",
        "intro": """이름 뒤에 `@N` 을 달면 바이너리 파일이 컬럼을 **위치가 아니라 번호로**
가리킵니다. 이미 배포된 클라이언트가 컬럼 순서 변경을 견디게 하는 장치입니다.""",
        "blocks": [{
            "grids": [("showcase-wire", "테이블 Wire", {
                1: "전부 달거나 전부 안 답니다",
                2: "#로 지운 컬럼도 번호를 예약합니다",
            })],
            "code": ("record", "Wire"),
            "lead": """한 테이블 안에서 **전부 달거나 전부 안 답니다.** 지운 컬럼은
`#이름@N` 으로 남겨 그 번호를 예약합니다.""",
            "after": """**생성된 코드에 번호는 없습니다.** `@N` 은 파일이 컬럼을 가리키는
방법이고, 코드가 보는 것은 멤버 이름뿐입니다 — `OldColour` 는 아예 없습니다.

한 번 데이터를 실은 번호는 다시 쓸 수 없습니다. 그래서 지울 때 `#이름@N` 을 남깁니다 —
자세한 것은 [바이너리 형식](../binary-format.md)에 있습니다.""",
        }],
    },

    # ------------------------------------------------------------------ 참조
    {
        "slug": "reference",
        "title": "다른 테이블 가리키기",
        "summary": "`foreign` 이 컬럼 하나를 멤버 둘로 만드는 것, 그리고 참조의 네 가지 꼴",
        "intro": """`:type` 칸에 `foreign <테이블>` 이라고 적으면 그 컬럼은 숫자가 아니라
**저 테이블의 행**입니다. 셀에는 대상의 키를 적습니다.""",
        "blocks": [
            {
                "grids": [
                    ("showcase-shop", "테이블 Shop", {0: "가리켜지는 쪽"}),
                    ("showcase-shop-entry", "테이블 ShopEntry", {
                        2: "foreign 뒤에 대상 테이블의 이름",
                    }),
                ],
                "code": ("record", "ShopEntry"),
                "lead": """가장 단순한 꼴입니다 — 컬럼 하나가 저쪽 테이블의 행 하나를
가리킵니다.""",
                "after": """**컬럼 하나가 멤버 둘이 됩니다.**

- `ShopId` — 셀에 적힌 키 그대로입니다
- `ShopByShopId` — 그 키가 가리키는 행입니다

이름은 `<대상>By<컬럼>` 으로 만들어집니다
([참조가 내는 이름](../../spec/references/reference-surface-naming.md)).

파일에는 키로 저장되고, 모든 테이블을 읽은 뒤에 실제 레코드로 연결됩니다. **그래서 가리킨
행의 값을 쓸 때 조회를 한 번 더 하지 않습니다** — `entry.ShopByShopId.Name` 이 곧 상점
이름입니다.""",
            },
            {
                "grids": [("showcase-craft", "테이블 Craft", {
                    2: "행 · 그 행의 값 · 여러 행 · 없어도 되는 행",
                    5: "여러 개는 한 칸에 ;로",
                })],
                "code": ("record", "Craft"),
                "lead": """참조가 취하는 꼴은 넷입니다.

| 적는 법 | 뜻 |
| --- | --- |
| `foreign Potion` | 그 테이블의 행 하나 |
| `foreign Potion.Name` | 그 행의 값 하나 — 컬럼의 타입은 저쪽 컬럼의 타입이 됩니다 |
| `foreign Potion[]` | 행 여럿 |
| `foreign Potion?` | 행 하나, 또는 없음 |""",
                "after": """**`ResultName` 이 문자열인 것이 읽을 자리입니다.** 셀에는 키를
적었지만 타입은 저쪽 컬럼을 따라갑니다 — 가리키는 것이 행이 아니라 그 행의 값이기
때문입니다.

배열도 옵셔널도 같은 규칙으로 이름이 둘씩 생깁니다.""",
            },
        ],
    },

    # ------------------------------------------------------------------ 배열
    {
        "slug": "array",
        "title": "값이 여러 개일 때",
        "summary": "셀 안에서 나누는 것과 컬럼으로 나누는 것, 그리고 코드에서 같아지는 것",
        "intro": """배열이 오는 자리는 셋입니다 — **셀 하나 안**, **컬럼 여럿**, 그리고
**행 여럿**입니다. 앞의 둘이 여기 있고, 셋째는 [행으로 쌓는 배열](multirow.md)에 있습니다.""",
        "blocks": [
            {
                "grids": [("showcase-loot", "테이블 Loot", {
                    1: "Weight[0]과 Weight[1]은 한 배열입니다",
                    2: "셀 안에서 나누는 것은 타입 뒤의 []",
                })],
                "code": ("record", "Loot"),
                "lead": """- **셀 하나 안에 여러 값** — 타입을 `string[]` 으로 적고 셀에
  `potion;cheap` 처럼 적습니다
- **컬럼 여러 개가 한 배열** — `Weight[0]` · `Weight[1]` 처럼 번호를 붙입니다

시트에서 쓰기 편한 쪽을 고르면 됩니다.""",
                "after": """**생성된 코드에서는 둘이 구별되지 않습니다.** 둘 다 그냥
배열입니다 — 어느 쪽으로 적었는지는 시트의 사정이고, 코드는 그것을 알 필요가 없습니다.

`Weight` 는 컬럼이 둘이므로 길이가 언제나 2이고, `Tags` 는 행마다 다릅니다.""",
            },
            {
                "grids": [("showcase-drop", "테이블 Drop", {
                    1: "#만 적은 컬럼은 읽지 않습니다",
                    2: "원소가 없을 수 있으면 int?[]",
                    7: "-는 그 원소가 없다는 뜻",
                })],
                "code": ("record", "Drop"),
                "lead": """enum도 배열이 됩니다. 그리고 **`?` 를 어디에 붙이느냐로 「배열이
없는 것」과 「원소가 없는 것」이 갈립니다** — `int?[]` 는 원소가 없을 수 있고,
`int[]?` 는 배열 자체가 없을 수 있습니다.

오른쪽 끝의 컬럼은 `:field` 칸에 `#` 만 적은 **메모 컬럼**입니다.""",
                "after": """**메모 컬럼은 생성된 코드에 없습니다.** 시트를 쓰는 사람의 자리이고,
무엇을 적어도 모델에 들어가지 않습니다.

`Counts` 의 원소 타입이 「없을 수 있는 정수」인 것도 코드에 그대로 나타납니다 — 그 언어에
옵셔널이 있으면 그것으로, 없으면 그 언어가 없음을 말하는 방법으로 나옵니다.""",
            },
        ],
    },

    # ------------------------------------------------------------------ 레코드
    {
        "slug": "record",
        "title": "컬럼 묶음과 빈 칸",
        "summary": "점 앞이 같은 컬럼이 레코드가 되는 것, 두 단계 중첩, 그리고 비워도 되는 칸",
        "intro": """`At.X` · `At.Y` 처럼 **점 앞이 같은 컬럼들은 한 레코드**가 됩니다. 시트에서는
여전히 컬럼 여럿이고, 코드에서는 멤버를 가진 타입 하나입니다.""",
        "blocks": [
            {
                "grids": [("showcase-spawn", "테이블 Spawn", {
                    1: "점 앞이 같으면 한 덩어리",
                    2: "?가 붙으면 비워도 됩니다",
                    6: "-는 값이 없다는 뜻입니다",
                })],
                "code": ("record", "Spawn"),
                "lead": """레코드 하나와, 비워도 되는 값 하나입니다. 타입 뒤의 `?` 가 그 칸을
비워도 된다는 뜻이고, 비우는 방법은 `-` 입니다.""",
                "after": """**중첩은 파일에 아무 값도 더하지 않습니다.** 레코드는 멤버마다 컬럼
하나로 저장되므로, `At.X` 와 `At.Y` 를 따로 적었을 때와 파일의 바이트가 같습니다. 달라지는
것은 코드를 읽는 쪽의 모습뿐입니다.

`?` 가 붙은 컬럼은 언어마다 그 언어의 「없음」으로 나옵니다 — 옵셔널 타입이 있는 언어는 그것을
쓰고, 없는 언어는 값이 있는지 묻는 방법을 따로 냅니다.""",
            },
            {
                "grids": [("showcase-deck", "테이블 Deck", {
                    1: "번호가 붙으면 레코드의 배열",
                    2: "점이 둘이면 두 단계",
                })],
                "code": ("record", "Deck"),
                "lead": """레코드도 배열이 되고, 레코드 안에 레코드가 옵니다.

- `Slot[0].Id` · `Slot[1].Id` — 레코드의 배열
- `Home.At.X` — 레코드 안의 레코드""",
                "after": """**깊이는 파일에 값을 더하지 않습니다.** 몇 단계를 내려가든 저장되는
것은 잎마다 컬럼 하나입니다 — 생성된 코드에 단계마다 타입이 하나씩 생길 뿐입니다.""",
            },
        ],
    },

    # ------------------------------------------------------------------ 멀티 로우
    {
        "slug": "multirow",
        "title": "행으로 쌓는 배열",
        "summary": "레코드 하나가 여러 행에 걸치는 것 — 원소 수가 행마다 다를 때",
        "intro": """원소 수가 행마다 크게 다르면 컬럼을 최대치만큼 늘어놓는 것이 힘듭니다.
**그때는 아래로 쌓습니다.**""",
        "blocks": [{
            "grids": [("showcase-quest", "테이블 Quest", {
                1: "이름의 []가 「아래로 쌓는다」는 표시",
                5: "기본 인덱스에 값이 있으면 새 레코드",
                6: "비어 있으면 위 레코드의 연장 행",
            })],
            "code": ("record", "Quest"),
            "lead": """이름에 `[]` 를 적으면 그 그룹의 원소는 **옆 컬럼이 아니라 아래 행**에서
옵니다.

- **새 레코드의 시작은 기본 인덱스 칸에 값이 있는 행**입니다
- 그 칸이 빈 행은 **직전 레코드의 연장 행**이고, 거기서 값을 담는 것은 `[]` 컬럼뿐입니다
- **완전히 빈 행은 엔티티를 끝냅니다** — 레코드 사이에 빈 행을 둘 수 없습니다""",
            "after": """**생성된 코드는 컬럼으로 적었을 때와 같습니다.** 배열 하나이고, 길이는
그 레코드가 실제로 가진 만큼입니다 — 시트에서 아래로 쌓았다는 사실은 코드에 남지 않습니다.

파일도 같습니다. 같은 데이터를 컬럼으로 적은 시트와 행으로 적은 시트가 **바이트 단위로 같은
파일**을 만듭니다.""",
        }],
    },

    # ------------------------------------------------------------------ 서버/클라
    {
        "slug": "sides",
        "title": "서버와 클라이언트에 다른 것 주기",
        "summary": "컬럼 하나만 빼는 것과 테이블 전체를 빼는 것",
        "intro": """`c` 는 클라이언트, `s` 는 서버, `cs` 는 양쪽(기본)입니다. **적는 자리가
둘인데, 어느 쪽이든 받지 않는 빌드에는 흔적이 남지 않습니다.**""",
        "blocks": [
            {
                "grids": [("showcase-stage-reward", "테이블 StageReward", {
                    4: "DropTable만 s — 서버 빌드에만",
                })],
                "code": ("record", "StageReward"),
                "lead": """`:target` 줄에 적으면 **그 컬럼 하나**입니다. 위는 서버 빌드에서
생성된 코드입니다.""",
                "after": """같은 시트를 클라이언트로 빌드하면 이렇게 됩니다.""",
                "extra": ("csharp-client", "StageRewardRecord",
                          """**컬럼이 없습니다.** 값이 비어 있거나 0으로 채워지는 것이 아니라,
그 컬럼을 읽는 방법 자체가 생성되지 않습니다 — 데이터 파일에도 없습니다."""),
            },
            {
                "grids": [("showcase-server-tuning", "테이블 ServerTuning", {
                    0: "선언 셀의 괄호에 side=s",
                })],
                "code": ("record", "ServerTuning"),
                "lead": """**선언 셀의 괄호**에 적으면 테이블 전체입니다.""",
                "after": """클라이언트 빌드에는 **이 타입이 아예 없습니다.** 컬럼이 빠지는 것과
달리 파일도 코드도 생성되지 않으므로, 클라이언트가 실수로 읽을 방법이 없습니다.

자세한 것은 [Target Side](../sheets/rules-and-pitfalls.md#target-side-서버클라-분리)에
있습니다.""",
            },
        ],
    },

    # ------------------------------------------------------------------ 변형
    {
        "slug": "variant",
        "title": "같은 필드를 여러 벌 적기",
        "summary": "지역별 가격처럼 컬럼 하나만 갈리는 데이터. 빌드가 하나를 고릅니다",
        "intro": """컬럼 하나만 갈리고 나머지가 공유되는 데이터가 있습니다. 테이블을 여러 벌
만들 것도, 컬럼 이름에 지역을 붙일 것도 없습니다 — **같은 이름으로 여러 벌 적고 빌드가 하나를
고릅니다.**""",
        "blocks": [{
            "grids": [("showcase-price", "테이블 Price", {
                1: "이름은 컬럼마다 되풀이합니다",
                2: "타입·설명은 기본 변형에 한 번",
                5: "빈 칸이 기본 변형",
            })],
            "code": ("record", "Price"),
            "lead": """같은 필드 이름을 컬럼 여러 개에 적고 `:variant` 행이 구분합니다.
**빈 칸이 기본 변형**이고, 타입과 설명은 그 컬럼에 한 번만 적습니다.

고르는 것은 recipe의 `"Variants": { "Price.Amount": "kr" }` 또는 CLI의
`--variant Price.Amount=kr` 이고, 명령줄이 recipe를 덮습니다.""",
            "after": """**산출물은 변형을 모릅니다.** `Amount` 멤버 하나뿐이고, 이름에도
타입에도 어느 변형이었는지가 남지 않습니다 — 고른 컬럼 하나가 그 필드가 되고 나머지는 그
빌드에 없습니다.

위는 변형을 지정하지 않은 빌드이므로 기본 변형이 실렸습니다. `--variant Price.Amount=kr` 로
빌드하면 **같은 코드**에 값만 달라집니다.

키 컬럼과 그룹 컬럼에는 변형을 둘 수 없습니다.""",
        }],
    },

    # ------------------------------------------------------------------ 폴리모피즘
    {
        "slug": "polymorphism",
        "title": "행마다 모양이 다른 묶음",
        "summary": "`$type` 이 그 행의 모양을 고르는 것. 선언은 `.tbs` 에",
        "intro": """「보상은 아이템이거나 화폐이거나 몬스터이다」 같은 데이터입니다. 컬럼 묶음
하나가 **행마다 다른 모양**을 가집니다.

모양의 목록은 시트가 아니라 **선언 파일**에 적습니다.""",
        "blocks": [{
            "grids": [("showcase-skill", "테이블 Skill", {
                1: "$type 이 그 행의 모양을 고릅니다",
                2: "모양의 이름을 적고, 나머지 칸은 비웁니다",
                5: "자기 모양이 아닌 칸은 빈 칸",
            })],
            "file": ("test/fixtures/schemas/doc-showcase/effect.tbs",
                     """선언 파일이 먼저입니다. `abstract struct` 가 공통이고, `extends` 가
변형이며, `@1` 같은 번호가 그 변형을 파일에서 가리킵니다."""),
            "code": ("record", "Skill"),
            "lead": """시트에는 **모든 변형의 멤버를 나란히** 둡니다. `$type` 칸이 그 행이 어느
모양인지 말하고, **그 행의 모양이 아닌 칸은 빈 칸**입니다 — `-` 가 아닙니다. 없는 값이 아니라
그 변형이 가지지 않은 멤버이기 때문입니다.""",
            "after": """**생성된 코드는 변형마다 타입 하나를 냅니다.** 어느 모양인지 묻는 방법과
그 모양으로 받는 방법이 함께 나오고, 그 방법은 언어마다 다릅니다 — 상속이 있는 언어는 상속으로,
합 타입이 있는 언어는 그것으로 냅니다.

파일은 움직이지 않았습니다. 모든 변형의 멤버가 컬럼으로 있고 각 행이 자기 것만 채우는 것은
이미 있던 저장 방식이므로, **이 기능이 형식에 더한 것은 없습니다.**""",
        }],
    },
]


# ---------------------------------------------------------------- 만들기

# 시트 그림과 코드를 좌우로 놓을 수 있는 폭. 이보다 넓은 격자는 반쪽에 들어가느라 글자가
# 읽을 수 없게 줄어들므로, 그런 것은 위아래로 둡니다 - 좌우 배치는 읽으라고 있는 것입니다.
PAIRABLE = 800


def figure(name, title, notes):
    """격자 JSON 하나를 SVG로. 그려 놓은 그림의 폭을 돌려줍니다."""
    build(name, grid_dump.load(name)["rows"], notes=notes, title=title)

    with open(os.path.join(FIGURES, name + ".svg"), encoding="utf-8") as f:
        head = f.read(300)

    at = head.index('width="') + 7
    return int(head[at:head.index('"', at)])


def tabs(kind, entity):
    """언어마다 코드를 오려 탭 묶음 하나로.

    테이블은 레코드 타입의 선언만 오려 옵니다 - 파일의 나머지는 파일을 읽고 배열을 채우는
    일이고, 시트에 무엇을 적었느냐로 달라지지 않기 때문입니다. enum과 상수셋은 파일이 짧으므로
    통째로 옮깁니다."""
    out = ["<!-- tabbit:tabs lang -->"]

    for i, (lang, label, fence, shapes) in enumerate(showcase_code.LANGUAGES):
        name = shapes[kind].format(entity=entity)
        if kind == "record":
            code, path = showcase_code.declaration(lang, name)
        else:
            code, path = showcase_code.whole_file(lang, entity, name)

        # `data-lang` 은 사이트가 탭을 기억할 때 쓰는 열쇠입니다. 라벨에서 만들면 `C#` 과
        # `C++` 과 `C` 가 모두 `c` 로 접혀 서로를 덮어씁니다.
        out.append('<details data-lang="%s"%s>' % (lang, " open" if i == 0 else ""))
        out.append("<summary>%s</summary>" % label)
        out.append("")
        out.append("```%s" % fence)
        out.append(code)
        out.append("```")
        out.append("")
        out.append("[%s](../../%s)" % (os.path.basename(path), path))
        out.append("")
        out.append("</details>")

    out.append("<!-- /tabbit:tabs -->")
    return "\n".join(out)


def block(spec):
    parts = []

    # 선언 파일이 있으면 그것이 먼저입니다 - 시트가 그 파일에 기대어 적히므로, 읽는 순서도
    # 그쪽이 앞입니다.
    if "file" in spec:
        path, prose = spec["file"]
        with open(os.path.join(REPO, path), encoding="utf-8") as f:
            parts += [prose, "", "```", f.read().rstrip(), "```", "",
                      "[%s](../../%s)" % (os.path.basename(path), path), ""]

    parts += [spec["lead"], ""]

    drawn = []
    for name, title, notes in spec["grids"]:
        drawn.append((name, title, figure(name, title, notes)))

    # 시트 그림과 코드를 한 덩어리로 묶어 둡니다. GitHub 은 주석 둘을 무시하고 위아래로
    # 읽고, 사이트는 이 표시를 보고 화면이 넓을 때 좌우로 놓습니다.
    pairable = all(width <= PAIRABLE for _, _, width in drawn)

    if pairable:
        parts += ["<!-- tabbit:pair -->", ""]

    for name, title, _ in drawn:
        parts += ["![%s](../figures/%s.svg)" % (title, name), ""]

    parts += [tabs(*spec["code"]), ""]

    if pairable:
        parts += ["<!-- /tabbit:pair -->", ""]
    parts += [spec["after"], ""]

    if "extra" in spec:
        lang, type_name, prose = spec["extra"]
        code, path = showcase_code.declaration(lang, type_name)
        parts += ["```csharp", code, "```", "",
                  "[%s](../../%s)" % (os.path.basename(path), path), "",
                  prose, ""]

    return parts


def section(spec):
    parts = ["# %s" % spec["title"], "", BANNER, "",
             "> [시트가 코드가 되는 모습으로](../generated-code.md)", "", "---", "",
             spec["intro"], ""]

    for i, one in enumerate(spec["blocks"]):
        if i > 0:
            parts += ["---", ""]
        parts += block(one)

    if "tail" in spec:
        parts += [spec["tail"], ""]

    return "\n".join(parts).rstrip() + "\n"


INDEX_HEAD = """# 시트가 코드가 되는 모습

시트에 적은 것과 거기서 생성된 코드를 나란히 놓았습니다. 언어는 탭에서 고릅니다.

{banner}

> [문서 목록으로](readme.md)

---

**여기 실린 것은 전부 생성물입니다.** 왼쪽의 시트 그림은
[`doc-showcase` 워크북](../test/fixtures/xlsx/doc-showcase)에서 셀을 그대로 읽어 그린 것이고,
오른쪽의 코드는 회귀 테스트가 매번 비교하는
[골든 트리](../test/fixtures/golden/doc-showcase)에서 오려 온 것입니다. 지어낸 예제가 아니고,
사람이 옮겨 적은 것도 아닙니다.

같은 워크북 하나가 모든 언어로 생성됩니다. 탭을 바꾸면 **같은 시트의 같은 자리**가 그 언어에서
어떻게 되는지 보입니다.

## 문서

| 무엇 | 어디 |
| --- | --- |
"""

INDEX_TAIL = """
## 여기 없는 것

시트에 적을 수 있는 것 전부는 [시트 작성](sheets.md)에 있습니다. 이 문서가 담는 것은 **생성된
코드가 함께 보여야 뜻이 통하는 것들**입니다.

`set`과 `map`은 아직 없습니다 — 모델과 파일에는 도달하지만 코드 타깃이 아직 이름을 대고
거부하므로, 나란히 놓을 코드가 없습니다 ([`set`과 `map`](sheets/containers.md)).

## 이 문서를 다시 만드는 방법

```bash
python doc/figures/grid_dump.py     # 워크북 -> 격자
python doc/figures/showcase.py      # 격자와 골든 -> 이 문서
```

생성기나 템플릿을 고쳤으면 **골든을 먼저 다시 기록하고** 이것을 돌립니다. 순서가 반대이면 옛
코드가 문서에 남습니다.

## 다음

| 무엇이 궁금한가 | 어디 |
| --- | --- |
| 시트에 적을 수 있는 것 전부 | [시트 작성](sheets.md) |
| 생성된 코드를 프로젝트에 적용하는 방법 | [언어별 가이드](languages/readme.md) |
| 세 가지 엔티티가 각각 무엇이 되는가 | [시트에 무엇을 적을 수 있나](concepts.md) |
"""


def index():
    rows = []
    for spec in SECTIONS:
        rows.append("| [%s](generated-code/%s.md) | %s |"
                    % (spec["title"], spec["slug"], spec["summary"]))
    return (INDEX_HEAD.format(banner=BANNER) + "\n".join(rows) + "\n" + INDEX_TAIL)


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(FIGURES, exist_ok=True)

    for spec in SECTIONS:
        path = os.path.join(OUT_DIR, spec["slug"] + ".md")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(section(spec))
        print("generated-code/%s.md" % spec["slug"])

    with open(os.path.join(DOC, "generated-code.md"), "w",
              encoding="utf-8", newline="\n") as f:
        f.write(index())
    print("generated-code.md")


if __name__ == "__main__":
    main()
