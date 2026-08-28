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

# 각 절이 정하는 것:
#
#   slug     파일 이름
#   title    제목
#   grids    [(격자 이름, 그림 설명, 화살표)] - 여럿이면 위아래로 놓입니다
#   code     ("record", 엔티티) 또는 ("file", 파일 이름 조각)
#   lead     그림 앞에 오는 설명
#   after    코드 뒤에 오는 설명
SECTIONS = [
    {
        "slug": "table",
        "summary": "선언 셀 · 헤더 행 · 데이터 행, 그리고 컬럼 하나가 되는 멤버 하나",
        "title": "테이블 하나",
        "grids": [("showcase-potion", "테이블 Potion", {
            1: "컬럼의 이름이 되는 줄",
            2: "타입. enum은 이름을 그대로 적습니다",
            5: "마커 열이 비면 데이터 행",
        })],
        "code": ("record", "Potion"),
        "lead": """컬럼 넷짜리 테이블입니다. `:field` 가 이름을, `:type` 이 타입을, `:desc` 가
설명을 정하고, 마커 열이 빈 행부터가 데이터입니다.

**첫 필드 컬럼이 기본 인덱스입니다.** 여기서는 `index` 가 그것입니다.""",
        "after": """읽을 것이 셋 있습니다.

- **컬럼 하나가 멤버 하나입니다.** 이름은 각 언어의 관례를 따라 바뀌지만 순서는 시트의 순서
  그대로입니다.
- **`:desc` 에 적은 설명이 doc comment로 나갑니다.** 시트에 적어 두면 IDE의 툴팁까지 갑니다.
- **`Rarity` 컬럼의 타입이 enum 타입입니다.** 정수가 아닙니다 - 시트에 `Common` 이라고 적고
  코드에서도 `Rarity` 로 받습니다.

조회 함수는 여기 없습니다. 레코드는 값이고, 찾는 일은 테이블이 합니다 —
[레코드 조회](../languages/readme.md#레코드-조회)에 언어별로 정리되어 있습니다.""",
    },
    {
        "slug": "enum",
        "summary": "`:field` 줄 하나로 끝나는 선언과, 시트에 없던 `None = 0`",
        "title": "enum",
        "grids": [("showcase-rarity", "enum Rarity", {
            1: "enum은 컬럼이 정해져 있어 이 줄 하나뿐",
            2: "0이 없으므로 None = 0이 붙습니다",
        })],
        "code": ("enum", "Rarity"),
        "lead": """enum과 상수셋은 컬럼이 정해져 있으므로 `:field` 줄 하나로 끝납니다.

`label` 이 이름, `value` 가 값, `desc` 가 설명입니다.""",
        "after": """**시트에 없던 `None = 0` 이 붙어 있습니다.**

enum 필드는 값이 대입되기 전에도 무언가를 들고 있는데, 그것이 이름 없는 0이면 디버거에서도
로그에서도 읽을 수 없기 때문입니다. 시트가 0에 이미 값을 두었다면 손대지 않으며,
`AutoInsertEnumNoneLabel` 로 끌 수도 있습니다.

여기는 선언 파일을 통째로 옮겼습니다. enum은 그 정도로 짧습니다.""",
    },
    {
        "slug": "const",
        "summary": "행이 없는 값들. 데이터 파일에 흔적이 남지 않습니다",
        "title": "상수셋",
        "grids": [("showcase-balance", "상수셋 Balance", {
            1: "이름·타입·값·설명 네 컬럼",
        })],
        "code": ("const", "Balance"),
        "lead": """행이 아니라 이름과 타입과 값의 목록입니다. 한 줄짜리 설정값들이 테이블 흉내를
내지 않아도 되는 자리입니다.""",
        "after": """**상수셋은 데이터 파일에 흔적이 전혀 없습니다.** 값을 고쳐도 코드를 다시
배포하기 전에는 아무것도 달라지지 않습니다 —
[엔티티에 따라 갈리는 배포 경로](../concepts.md#엔티티에-따라-갈리는-배포-경로).

언어마다 내는 것이 다릅니다. 클래스의 정적 멤버로 내는 언어가 있고, 모듈 상수로 내는 언어가
있고, C는 매크로로 냅니다 — 그 언어에서 상수를 쓰는 방법이 그것이기 때문입니다.""",
    },
    {
        "slug": "reference",
        "summary": "`foreign` 이 컬럼 하나를 멤버 둘로 만드는 것",
        "title": "다른 테이블 가리키기",
        "grids": [
            ("showcase-shop", "테이블 Shop", {0: "가리켜지는 쪽"}),
            ("showcase-shop-entry", "테이블 ShopEntry", {
                2: "foreign 뒤에 대상 테이블의 이름",
            }),
        ],
        "code": ("record", "ShopEntry"),
        "lead": """`:type` 칸에 `foreign <테이블>` 이라고 적으면 그 컬럼은 숫자가 아니라
**저 테이블의 행**입니다. 셀에는 대상의 키를 적습니다.""",
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
        "slug": "array",
        "summary": "셀 안에서 나누는 것과 컬럼으로 나누는 것, 그리고 코드에서 같아지는 것",
        "title": "값이 여러 개일 때",
        "grids": [("showcase-loot", "테이블 Loot", {
            1: "Weight[0]과 Weight[1]은 한 배열입니다",
            2: "셀 안에서 나누는 것은 타입 뒤의 []",
        })],
        "code": ("record", "Loot"),
        "lead": """배열이 오는 자리는 둘입니다.

- **셀 하나 안에 여러 값** — 타입을 `string[]` 으로 적고 셀에 `potion;cheap` 처럼 적습니다
- **컬럼 여러 개가 한 배열** — `Weight[0]` · `Weight[1]` 처럼 번호를 붙입니다

시트에서 쓰기 편한 쪽을 고르면 됩니다.""",
        "after": """**생성된 코드에서는 둘이 구별되지 않습니다.** 둘 다 그냥 배열입니다 — 어느
쪽으로 적었는지는 시트의 사정이고, 코드는 그것을 알 필요가 없습니다.

`Weight` 는 컬럼이 둘이므로 길이가 언제나 2이고, `Tags` 는 행마다 다릅니다.""",
    },
    {
        "slug": "record",
        "summary": "점 앞이 같은 컬럼이 레코드가 되는 것과, 비워도 되는 칸",
        "title": "컬럼 묶음과 빈 칸",
        "grids": [("showcase-spawn", "테이블 Spawn", {
            1: "점 앞이 같으면 한 덩어리",
            2: "?가 붙으면 비워도 됩니다",
            6: "-는 값이 없다는 뜻입니다",
        })],
        "code": ("record", "Spawn"),
        "lead": """`At.X` · `At.Y` 처럼 **점 앞이 같은 컬럼들은 한 레코드**가 됩니다. 시트에서는
여전히 컬럼 둘이고, 코드에서는 멤버 둘을 가진 타입 하나입니다.

타입 뒤의 `?` 는 그 칸을 비워도 된다는 뜻이고, 비우는 방법은 `-` 입니다.""",
        "after": """**중첩은 파일에 아무 값도 더하지 않습니다.** 레코드는 멤버마다 컬럼 하나로
저장되므로, `At.X` 와 `At.Y` 를 따로 적었을 때와 파일의 바이트가 같습니다. 달라지는 것은
코드를 읽는 쪽의 모습뿐입니다.

`?` 가 붙은 컬럼은 언어마다 그 언어의 「없음」으로 나옵니다 — 옵셔널 타입이 있는 언어는 그것을
쓰고, 없는 언어는 값이 있는지 묻는 방법을 따로 냅니다.""",
    },
    {
        "slug": "key",
        "summary": "컬럼 둘이 키인 테이블과, 클라이언트가 받지 않는 컬럼",
        "title": "키가 여럿인 테이블과 서버 전용 컬럼",
        "grids": [("showcase-stage-reward", "테이블 StageReward", {
            0: "선언 셀의 괄호에 키를 적습니다",
            4: "DropTable만 s — 서버 빌드에만",
            5: "Stage가 겹쳐도 Rank가 다르면 다른 행",
        })],
        "code": ("record", "StageReward"),
        "lead": """어느 컬럼 하나로도 행을 가릴 수 없을 때, **선언 셀의 괄호에 키를 적습니다** —
`:table StageReward(key="Stage,Rank")` 입니다.

`:target` 줄의 `s` 는 그 컬럼을 서버 빌드에만 포함한다는 뜻입니다.""",
        "after": """키가 둘이면 조회 함수의 인자도 둘입니다 — `FindByStageAndRank(stage, rank)`
처럼 성분마다 하나씩 생깁니다. 이름은 그 인덱스의 컬럼에서 만들어지므로, 키를 바꾸면 함수
이름이 함께 바뀌고 옛 이름을 부르던 자리가 컴파일에서 드러납니다.

`DropTable` 은 위의 코드에 있습니다. 같은 시트를 클라이언트로 빌드하면 이렇게 됩니다.""",
        "extra": ("csharp-client", "StageRewardRecord",
                  """**컬럼이 없습니다.** 값이 비어 있거나 0으로 채워지는 것이 아니라, 그
컬럼을 읽는 방법 자체가 생성되지 않습니다 — 데이터 파일에도 없습니다."""),
    },
]


# ---------------------------------------------------------------- 만들기

def figure(name, title, notes):
    """격자 JSON 하나를 SVG로."""
    build(name, grid_dump.load(name)["rows"], notes=notes, title=title)


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


def section(spec):
    parts = ["# %s" % spec["title"], "", BANNER, "",
             "> [생성되는 코드로](../generated-code.md)", "", "---", ""]

    parts += [spec["lead"], ""]

    # 시트 그림과 코드를 한 덩어리로 묶어 둡니다. GitHub 은 주석 둘을 무시하고 위아래로
    # 읽고, 사이트는 이 표시를 보고 화면이 넓을 때 좌우로 놓습니다.
    parts += ["<!-- tabbit:pair -->", ""]

    for name, title, notes in spec["grids"]:
        figure(name, title, notes)
        parts += ["![%s](../figures/%s.svg)" % (title, name), ""]

    parts += [tabs(*spec["code"]), "", "<!-- /tabbit:pair -->", ""]
    parts += [spec["after"], ""]

    if "extra" in spec:
        lang, type_name, prose = spec["extra"]
        code, path = showcase_code.declaration(lang, type_name)
        parts += ["```csharp", code, "```", "",
                  "[%s](../../%s)" % (os.path.basename(path), path), "",
                  prose, ""]

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
