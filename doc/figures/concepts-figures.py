# -*- coding: utf-8 -*-
"""concepts.md 의 예시를 엑셀 격자 모습의 SVG로 생성한다.

실행: `python doc/figures/concepts-figures.py`

같은 폴더에 concepts-*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고 다시 실행한 뒤,
PNG로 렌더해 눈으로 확인하고 커밋합니다.

**격자는 `core` 픽스처 워크북에서 그대로 읽습니다** - `grid_dump.py` 가 뽑아 둔
`grids/core-*.json` 이 그것이고, 여기가 정하는 것은 어느 엔티티를 어느 컬럼까지 보일지와
화살표로 가리킬 줄뿐입니다. 값 하나를 바꾸려면 test/fixtures/tools/FixtureGen/Program.cs 를
고치고 `grid_dump.py` 를 다시 돌립니다.

격자를 그리는 코드는 spec/layout/primary-layout-figures.py 의 것을 그대로 씁니다 - 같은 격자를
여러 벌 그리면 문서마다 그림이 서로 달라 보이기 시작하는 자리가 되기 때문입니다. 그 파일은
직접 돌릴 때만 자기 그림을 쓰므로, 여기서 불러도 spec 의 그림은 그대로입니다."""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
BUILDER = os.path.join(REPO, "spec", "layout", "primary-layout-figures.py")

sys.path.insert(0, HERE)
import grid_dump  # noqa: E402

_spec = importlib.util.spec_from_file_location("layout_figures", BUILDER)
_figures = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_figures)
_figures.OUT_DIR = HERE

build = _figures.build


build("concepts-item-category", grid_dump.load("core-item-category")["rows"], notes={
    0: "선언 셀과 그 오른쪽의 설명",
    1: "생략할 수 없는 유일한 헤더 행",
    5: "마커 열이 비면 데이터 행",
}, title="테이블 ItemCategory")

build("concepts-enum-grade", grid_dump.load("core-grade")["rows"], notes={
    1: "헤더 행은 이것 하나뿐",
    2: "0이 없으므로 None = 0이 붙습니다",
}, title="enum Grade")

# 폭 때문에 일곱 중 다섯만 옮깁니다. 뺀 것은 `SkillField` 와 `Description` 이고,
# concepts.md 가 그 둘을 본문에서 따로 설명합니다.
build("concepts-item", grid_dump.select(
    grid_dump.load("core-item"),
    ["index", "Name", "CategoryId", "GradeField", "Price"]), notes={
    2: "foreign과 enum은 이름을 함께 적습니다",
    4: "Price만 s — 서버 빌드에만",
}, title="테이블 Item")
