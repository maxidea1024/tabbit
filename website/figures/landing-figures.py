# -*- coding: utf-8 -*-
"""첫 화면의 시트 그림을 엑셀 격자 모습의 SVG로 생성한다.

실행: `python website/figures/landing-figures.py`

`website/static/img/` 에 `landing-*.svg` 를 다시 씁니다. 고쳤으면 다시 실행한 뒤, PNG로
렌더해 눈으로 확인하고 커밋합니다.

**격자는 `core` 픽스처 워크북에서 그대로 읽습니다** — 문서의 그림들과 같은 자리입니다.
첫 화면이 손으로 적은 표를 들고 있으면, 시트를 고쳤을 때 첫 화면만 옛 값을 보이게 됩니다.

격자를 그리는 코드도 spec/layout/primary-layout-figures.py 의 것을 그대로 씁니다 — 첫 화면의
시트가 문서의 시트와 달라 보이면, 문서로 들어온 사람이 같은 것을 두 번 배우게 됩니다.

**첫 화면이 글자 블록이 아니라 그림인 이유입니다.** 시트는 격자이고, 격자를 `<pre>` 로 적으면
좁은 화면에서 가로로 밀리는 글자 덩어리가 됩니다 — 셀의 경계가 없으니 어느 칸이 어느 컬럼인지
읽는 사람이 세어야 합니다.
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
BUILDER = os.path.join(REPO, "spec", "layout", "primary-layout-figures.py")
FIGURES = os.path.join(REPO, "doc", "figures")

sys.path.insert(0, FIGURES)
import grid_dump  # noqa: E402

_spec = importlib.util.spec_from_file_location("layout_figures", BUILDER)
_figures = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_figures)
_figures.OUT_DIR = os.path.abspath(os.path.join(HERE, "..", "static", "img"))

build = _figures.build


# concepts.md 의 `concepts-item` 과 같은 표입니다. 화살표 주석과 제목은 없습니다 — 첫 화면은
# 패널이 「엑셀 · 구글 스프레드시트」라고 이미 말하고, 짚을 것은 그림 아래 글이 말합니다.
build("landing-sheet", grid_dump.select(
    grid_dump.load("core-item"),
    ["index", "Name", "CategoryId", "GradeField", "Price"]))
