# -*- coding: utf-8 -*-
"""픽스처 워크북에서 엔티티의 셀 격자를 그대로 뽑는다.

    python doc/figures/grid_dump.py                 # doc/figures/grids/*.json 을 다시 씁니다
    python doc/figures/grid_dump.py --list <경로>   # 그 워크북에 무엇이 있는지만 봅니다

**그림의 출처를 워크북 하나로 모으는 것이 이 파일의 목적입니다.** 그림에 적힌 격자를 사람이
따로 옮겨 적으면, 워크북이 바뀌어도 그림은 그대로 남고 아무 게이트도 그것을 보지 않습니다.

읽는 방법은 zip 과 XML 뿐입니다 - 외부 패키지를 쓰지 않습니다. 이 도구가 필요로 하는 것은
셀의 문자열 값이 전부이고, 서식과 수식과 차트는 보지 않습니다."""
import argparse
import json
import os
import re
import zipfile
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT_DIR = os.environ.get("TABBIT_DOC_GRIDS", os.path.join(HERE, "grids"))

NS_MAIN = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
NS_REL_DOC = "{http://schemas.openxmlformats.org/officeDocument/2006/relationships}"
NS_REL_PKG = "{http://schemas.openxmlformats.org/package/2006/relationships}"

KINDS = ("table", "enum", "const")


# -- 워크북 읽기.

def _col_index(ref):
    """`B12` 의 열 번호를 0부터 세어 돌려줍니다."""
    letters = re.match(r"[A-Z]+", ref).group(0)
    n = 0
    for ch in letters:
        n = n * 26 + (ord(ch) - ord("A") + 1)
    return n - 1


def _row_index(ref):
    return int(re.search(r"\d+", ref).group(0)) - 1


def _shared_strings(zf):
    if "xl/sharedStrings.xml" not in zf.namelist():
        return []

    root = ET.fromstring(zf.read("xl/sharedStrings.xml"))
    out = []
    for si in root.findall(NS_MAIN + "si"):
        # 서식이 나뉜 문자열은 `t` 가 여럿이므로 이어 붙입니다. 후리가나(`rPh`)는 값이
        # 아니므로 세지 않습니다.
        parts = []
        furigana = {id(t) for ph in si.findall(NS_MAIN + "rPh")
                    for t in ph.iter(NS_MAIN + "t")}
        for t in si.iter(NS_MAIN + "t"):
            if id(t) not in furigana and t.text is not None:
                parts.append(t.text)
        out.append("".join(parts))
    return out


def _number(text):
    """엑셀이 수로 적어 둔 셀. 정수로 떨어지면 소수점을 붙이지 않습니다."""
    try:
        v = float(text)
    except ValueError:
        return text
    return str(int(v)) if v == int(v) else repr(v)


def _sheet_paths(zf):
    """{시트 이름: zip 안의 경로} 를 워크북에 적힌 순서대로 돌려줍니다."""
    rels = ET.fromstring(zf.read("xl/_rels/workbook.xml.rels"))
    target = {}
    for rel in rels.findall(NS_REL_PKG + "Relationship"):
        path = rel.get("Target")
        if path.startswith("/"):
            path = path[1:]
        elif not path.startswith("xl/"):
            path = "xl/" + path
        target[rel.get("Id")] = path

    book = ET.fromstring(zf.read("xl/workbook.xml"))
    out = {}
    for sheet in book.find(NS_MAIN + "sheets"):
        out[sheet.get("name")] = target[sheet.get(NS_REL_DOC + "id")]
    return out


def read_workbook(path):
    """{시트 이름: {(행, 열): 값}}. 행과 열은 0부터 세고, 빈 셀은 키가 없습니다."""
    with zipfile.ZipFile(path) as zf:
        strings = _shared_strings(zf)
        sheets = {}

        for name, member in _sheet_paths(zf).items():
            grid = {}
            root = ET.fromstring(zf.read(member))
            data = root.find(NS_MAIN + "sheetData")
            if data is None:
                sheets[name] = grid
                continue

            for row in data.findall(NS_MAIN + "row"):
                for cell in row.findall(NS_MAIN + "c"):
                    ref = cell.get("r")
                    kind = cell.get("t")

                    if kind == "s":
                        v = cell.find(NS_MAIN + "v")
                        text = strings[int(v.text)] if v is not None else ""
                    elif kind == "inlineStr":
                        node = cell.find(NS_MAIN + "is")
                        text = "".join(t.text or "" for t in node.iter(NS_MAIN + "t")) \
                            if node is not None else ""
                    elif kind == "e":
                        v = cell.find(NS_MAIN + "v")
                        text = v.text if v is not None else ""
                    else:
                        v = cell.find(NS_MAIN + "v")
                        if v is None or v.text is None:
                            text = ""
                        else:
                            text = v.text if kind == "str" else _number(v.text)

                    if text != "":
                        grid[(_row_index(ref), _col_index(ref))] = text

            sheets[name] = grid

    return sheets


# -- 엔티티의 사각형.

def declared_kind(value):
    """그 셀이 선언하는 종류, 아니면 None. 판정은 TabbitLayoutParser.DeclaredKindOf 와 같습니다."""
    text = (value or "").strip()
    for kind in KINDS:
        keyword = ":" + kind
        if not text.lower().startswith(keyword):
            continue
        if len(text) == len(keyword):
            return kind
        if text[len(keyword)] in " \t(":
            return kind
    return None


def declared_name(value):
    text = (value or "").strip()
    kind = declared_kind(text)
    rest = text[len(kind) + 1:].strip()
    return re.split(r"[(\s]", rest, maxsplit=1)[0] if rest else ""


def find_entities(sheets):
    """[(시트, 행, 열, 종류, 이름, 선언 셀)] 을 시트 순과 행 순으로."""
    found = []
    for sheet, grid in sheets.items():
        for (row, col), value in sorted(grid.items()):
            kind = declared_kind(value)
            if kind:
                found.append((sheet, row, col, kind, declared_name(value), value.strip()))
    return found


def extract(grid, row, col):
    """선언 셀 하나가 차지하는 사각형을 [[문자열]] 로.

    오른쪽 끝은 `:field` 줄이 정합니다 - 마커 열에서 오른쪽으로 걸어가 빈칸이나 다른 선언의
    `:` 낱말을 만나면 거기까지입니다. 옆에 붙은 이웃 테이블이 딸려오지 않는 자리입니다.

    아래 끝은 그 폭 안이 전부 빈 행입니다. 선언 셀 오른쪽의 설명은 넘쳐도 그대로 둡니다 -
    시트에서 실제로 그렇게 보이기 때문입니다."""
    header_rows = []
    r = row + 1
    while (r, col) in grid and grid[(r, col)].strip().startswith(":"):
        header_rows.append(r)
        r += 1

    field_row = next(
        (h for h in header_rows if grid[(h, col)].strip().lower() == ":field"), None)
    if field_row is None:
        raise ValueError("(%d, %d) 의 선언 아래에 `:field` 줄이 없습니다." % (row, col))

    right = col
    c = col + 1
    while (field_row, c) in grid and not grid[(field_row, c)].strip().startswith(":"):
        right = c
        c += 1

    bottom = header_rows[-1] if header_rows else row
    r = bottom + 1
    while any((r, c) in grid for c in range(col, right + 1)):
        bottom = r
        r += 1

    return [[grid.get((rr, cc), "") for cc in range(col, right + 1)]
            for rr in range(row, bottom + 1)]


def dump(workbook, sheet=None, name=None):
    """워크북에서 엔티티 하나를 찾아 격자와 그 출처를 함께 돌려줍니다."""
    sheets = read_workbook(workbook)
    for s, row, col, kind, entity, declaration in find_entities(sheets):
        if sheet is not None and s != sheet:
            continue
        if name is not None and entity != name:
            continue
        return {
            "source": os.path.relpath(workbook, REPO).replace("\\", "/"),
            "sheet": s,
            "kind": kind,
            "name": entity,
            "declaration": declaration,
            "origin": {"row": row, "column": col},
            "rows": extract(sheets[s], row, col),
        }

    raise LookupError("%s 에 %s 의 %s 가 없습니다."
                      % (workbook, sheet or "어느 시트", name or "어느 엔티티"))


# -- 그림 생성기가 쓰는 것.

def load(name):
    """`grids/<name>.json` 을 읽어 돌려줍니다."""
    with open(os.path.join(OUT_DIR, name + ".json"), encoding="utf-8") as f:
        return json.load(f)


def select(data, fields):
    """격자에서 컬럼 몇 개만 남깁니다. 폭 때문에 문서가 일부만 옮길 때 쓰는 것입니다.

    마커 열은 언제나 남고, 선언 셀 오른쪽의 설명도 남습니다 - 어느 컬럼을 빼든 시트에서는
    그 자리에 그대로 있기 때문입니다."""
    rows = data["rows"] if isinstance(data, dict) else data
    field_row = next(r for r in rows if r[0].strip().lower() == ":field")

    keep = [0]
    for want in fields:
        try:
            keep.append(field_row.index(want))
        except ValueError:
            raise LookupError("`%s` 컬럼이 없습니다. 있는 것: %s"
                              % (want, ", ".join(field_row[1:])))

    out = [[r[i] for i in keep] for r in rows]
    out[0] = [rows[0][0], rows[0][1]] + [""] * (len(keep) - 2)
    return out


# -- 문서가 쓰는 격자들.

SHOWCASE = "test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx"

WANTED = [
    # (내보낼 이름, 워크북, 시트, 엔티티)
    ("core-item-category", "test/fixtures/xlsx/core/core.xlsx", "Refs", "ItemCategory"),
    ("core-item", "test/fixtures/xlsx/core/core.xlsx", "Refs", "Item"),
    ("core-grade", "test/fixtures/xlsx/core/core.xlsx", None, "Grade"),

    # 생성 코드와 나란히 놓는 것들. `generated-code.md` 의 왼쪽입니다.
    ("showcase-rarity", SHOWCASE, "Basics", "Rarity"),
    ("showcase-balance", SHOWCASE, "Basics", "Balance"),
    ("showcase-potion", SHOWCASE, "Potion", "Potion"),
    ("showcase-shop", SHOWCASE, "Shop", "Shop"),
    ("showcase-shop-entry", SHOWCASE, "Shop", "ShopEntry"),
    ("showcase-loot", SHOWCASE, "Loot", "Loot"),
    ("showcase-spawn", SHOWCASE, "Spawn", "Spawn"),
    ("showcase-stage-reward", SHOWCASE, "StageReward", "StageReward"),
]


def main():
    ap = argparse.ArgumentParser(description="픽스처 워크북에서 엔티티의 셀 격자를 뽑습니다.")
    ap.add_argument("--list", metavar="워크북", help="그 워크북의 엔티티 목록만 출력합니다.")
    args = ap.parse_args()

    if args.list:
        path = args.list if os.path.isabs(args.list) else os.path.join(REPO, args.list)
        for sheet, row, col, kind, name, _ in find_entities(read_workbook(path)):
            print("%-20s (%3d, %3d)  :%s %s" % (sheet, row, col, kind, name))
        return

    os.makedirs(OUT_DIR, exist_ok=True)
    for out, workbook, sheet, name in WANTED:
        data = dump(os.path.join(REPO, workbook), sheet, name)
        dest = os.path.join(OUT_DIR, out + ".json")
        with open(dest, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print("%s.json  %d행 x %d열  <- %s / %s"
              % (out, len(data["rows"]), len(data["rows"][0]), data["source"], data["sheet"]))


if __name__ == "__main__":
    main()
