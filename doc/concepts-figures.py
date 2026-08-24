# -*- coding: utf-8 -*-
"""concepts.md 의 예시를 엑셀 격자 모습의 SVG로 생성한다.

실행: `python doc/concepts-figures.py`

같은 폴더에 concepts-*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고 다시 실행한 뒤,
PNG로 렌더해 눈으로 확인하고 커밋합니다.

**여기의 세 그림은 `core` 픽스처를 그대로 옮긴 것입니다** — 문서가 「지어낸 예제가 아니다」라고
적고 있으므로, 값 하나를 바꾸려면 test/fixtures/tools/FixtureGen/Program.cs 를 먼저 봅니다.

격자를 그리는 코드는 spec/primary-layout-figures.py 의 것을 그대로 씁니다 — 같은 격자를
여러 벌 그리면 문서마다 그림이 서로 달라 보이기 시작하는 자리가 되기 때문입니다."""
import importlib.util
import os

HERE = os.path.dirname(os.path.abspath(__file__))
BUILDER = os.path.join(os.path.dirname(HERE), "spec", "primary-layout-figures.py")

_spec = importlib.util.spec_from_file_location("layout_figures", BUILDER)
_figures = importlib.util.module_from_spec(_spec)

# The builder writes its own figures on import, so it is pointed here right after - nothing of
# the spec's is left rewritten by this run.
_spec.loader.exec_module(_figures)
_figures.OUT_DIR = HERE

build = _figures.build


build("concepts-item-category", [
    [":table ItemCategory", "Referenced by Item.CategoryId."],
    [":field", "index", "Name", "Description"],
    [":type", "int", "string", "string"],
    [":desc", "primary index", "category name", "human readable description"],
    [":target", "cs", "cs", "cs"],
    ["", "1", "Weapon", "things that hit"],
    ["", "2", "Armor", "things that absorb"],
    ["", "3", "Potion", "things that heal"],
], notes={
    0: "선언 셀과 그 오른쪽의 설명",
    1: "생략할 수 없는 유일한 헤더 행",
    5: "마커 열이 비면 데이터 행",
}, title="테이블 ItemCategory")

build("concepts-enum-grade", [
    [":enum Grade", "Item grade. Deliberately omits a zero entry."],
    [":field", "label", "value", "desc"],
    ["", "Common", "1", "common grade"],
    ["", "Rare", "2", "rare grade"],
    ["", "Epic", "3", "epic grade"],
], notes={
    1: "헤더 행은 이것 하나뿐",
    2: "0이 없으므로 None = 0이 붙습니다",
}, title="enum Grade")

build("concepts-item", [
    [":table Item", "References ItemCategory by record."],
    [":field", "index", "Name", "CategoryId", "GradeField", "Price"],
    [":type", "int", "string", "foreign ItemCategory", "Grade", "int"],
    [":desc", "primary index", "item name", "owning category", "item grade", "shop price"],
    [":target", "cs", "cs", "cs", "cs", "s"],
    ["", "1", "Short Sword", "1", "Common", "100"],
    ["", "2", "Leather Armor", "2", "Rare", "250"],
    ["", "3", "Small Potion", "3", "Epic", "50"],
], notes={
    2: "foreign과 enum은 이름을 함께 적습니다",
    4: "Price만 s — 서버 빌드에만",
}, title="테이블 Item")
