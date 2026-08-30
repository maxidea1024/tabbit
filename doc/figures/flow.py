# -*- coding: utf-8 -*-
"""위에서 아래로 흐르는 단계 그림을 SVG로 그린다.

문서에 박스 문자(`┌ │ └ ▼`)로 그려 두었던 그림을 대신하는 자리입니다. **그 그림들은 한글이
섞이면 어긋납니다** — 고정폭 글꼴에서 한글은 두 칸이고 라틴 문자는 한 칸인데, 브라우저가 고른
글꼴이 그 비를 정확히 지키지 않으면 세로선이 줄마다 다른 자리에 섭니다. 어긋난 정도는 읽는
사람의 글꼴 설정에 달려 있어서, 적은 사람의 화면에서 맞아 보여도 다른 화면에서 어긋납니다.

쓰는 쪽은 단계를 목록으로 적고, 자리와 선은 여기가 정합니다.

    import flow
    flow.build("validation-pipeline", [
        flow.step("recipe 로드"),
        flow.stage("① 사전 검증", "파일 이름 · 설정 · 환경",
                   aside=[("rules/pre/*.cs", "시트를 읽기 전에 확인할 수 있는 것")]),
        flow.edge("실패 → 종료. 아무것도 읽지 않았습니다"),
        ...
    ], out_dir=HERE)

`step` 은 이름만 있는 단계, `stage` 는 테두리가 있는 단계, `edge` 는 두 단계 사이의 화살표에
붙는 글입니다. `aside` 는 오른쪽에 나란히 놓는 줄들이고, 각 줄은 `(코드, 설명)` 입니다.
"""
import html
import os

# 격자 그림과 같은 글꼴·같은 색입니다. 한 문서 안에서 두 그림이 서로 다른 손으로 그린 것처럼
# 보이지 않게 하는 것이 전부입니다.
FONT = "Consolas, 'Cascadia Mono', 'Malgun Gothic', monospace"
FS = 12.5
FS_SMALL = 11.5

# 색은 클래스로 나가고 어두운 테마의 값을 함께 둡니다. 그림은 이미지로 서비스되므로 질의가
# 읽는 사람의 시스템 테마를 따르고, 문서 사이트의 어두운 테마에서도 글이 읽힙니다 —
# 여기서 색을 인라인으로 적으면 배경이 어두워질 때 글자가 배경과 같은 어둠이 됩니다.
STYLE = """
  .bg  { fill: #FFFFFF; }
  .box { fill: #F1F6FB; stroke: #BFC6CC; }
  .hd  { fill: #1F2328; }
  .mut { fill: #5B6570; }
  .cod { fill: #1A5FA8; }
  .nte { fill: #8A929B; }
  .flw { stroke: #BFC6CC; fill: none; }
  .arw { fill: #BFC6CC; }

  @media (prefers-color-scheme: dark) {
    .bg  { fill: #0D1117; }
    .box { fill: #151B23; stroke: #3D444D; }
    .hd  { fill: #F0F6FC; }
    .mut { fill: #9198A1; }
    .cod { fill: #6CB6FF; }
    .nte { fill: #6A7381; }
    .flw { stroke: #6A7381; }
    .arw { fill: #6A7381; }
  }
"""

ROW_H = 26          # 한 줄의 높이
BOX_PAD_Y = 9       # 테두리 안쪽 위아래 여백
GAP = 16            # 단계 사이의 화살표 길이
PAD = 10            # 글과 테두리 사이 좌우 여백
COL_GAP = 26        # 본체와 오른쪽 설명 사이
MARGIN = 16


def text_w(s, fs=FS):
    """고정폭 글꼴에서의 대략적인 폭. 격자 그림의 것과 같은 계산입니다."""
    w = 0.0
    for ch in s:
        if ord(ch) >= 0x2E80:      # CJK
            w += fs * 1.0
        elif ch in "iIl.,:;'|![]() ":
            w += fs * 0.46
        else:
            w += fs * 0.585
    return w


def step(label, aside=None):
    """이름만 있는 단계."""
    return {"kind": "step", "label": label, "aside": aside or []}


def stage(title, subtitle=None, aside=None):
    """테두리가 있는 단계. 여러 줄이면 `subtitle` 에 리스트를 줍니다."""
    if subtitle is None:
        lines = []
    elif isinstance(subtitle, str):
        lines = [subtitle]
    else:
        lines = list(subtitle)
    return {"kind": "stage", "title": title, "lines": lines, "aside": aside or []}


def edge(label):
    """두 단계 사이의 화살표에 붙는 글."""
    return {"kind": "edge", "label": label}


def _esc(s):
    return html.escape(s, quote=False)


# 코드 칸과 설명 칸 사이. 설명이 자기 코드에서 멀어지면 어느 줄의 설명인지 눈으로 다시
# 이어야 하므로, 붙는 쪽으로 좁게 잡습니다.
CODE_GAP = 16


def _row_w(code, note):
    """오른쪽 설명 한 줄의 폭. 코드가 없는 줄은 설명이 코드 칸에서 시작합니다."""
    if not code:
        return text_w(note, FS_SMALL)
    w = text_w(code, FS_SMALL)
    if note:
        w += CODE_GAP + text_w(note, FS_SMALL)
    return w


def build(name, items, out_dir, title=None):
    """items 를 위에서 아래로 배치한 SVG 하나를 `<out_dir>/<name>.svg` 로 씁니다."""
    # ---------------------------------------------------------------- 폭
    body_w = 0.0
    aside_w = 0.0
    code_w = 0.0        # 오른쪽 설명의 코드 칸 폭 - 설명 글이 세로로 맞도록
    for it in items:
        if it["kind"] == "stage":
            inner = max([text_w(it["title"])] + [text_w(l) for l in it["lines"]])
            body_w = max(body_w, inner + PAD * 2)
        elif it["kind"] == "step":
            body_w = max(body_w, text_w(it["label"]))
        else:
            body_w = max(body_w, text_w(it["label"], FS_SMALL) + 22)
        # 코드 칸의 폭은 설명이 붙는 줄만 정합니다 - 설명 없는 긴 코드 한 줄 때문에 다른
        # 줄의 설명이 오른쪽으로 밀려나지 않게 합니다.
        for code, note in it.get("aside", []):
            if code and note:
                code_w = max(code_w, text_w(code, FS_SMALL))

    for it in items:
        for code, note in it.get("aside", []):
            aside_w = max(aside_w, _row_w(code, note)
                          if not (code and note) else code_w + CODE_GAP + text_w(note, FS_SMALL))

    body_x = MARGIN
    aside_x = body_x + body_w + COL_GAP
    width = aside_x + aside_w + MARGIN if aside_w else body_x + body_w + MARGIN

    # ---------------------------------------------------------------- 높이
    y = MARGIN
    if title:
        y += ROW_H
    placed = []
    for i, it in enumerate(items):
        if it["kind"] == "stage":
            h = BOX_PAD_Y * 2 + ROW_H * (1 + len(it["lines"]))
        elif it["kind"] == "step":
            h = ROW_H
        else:
            h = ROW_H
        placed.append((it, y, h))
        y += h
        # 마지막이 아니면 화살표 자리.
        if i + 1 < len(items) and items[i + 1]["kind"] != "edge" and it["kind"] != "edge":
            y += GAP
        elif i + 1 < len(items):
            y += 4
    height = y + MARGIN

    cx = body_x + body_w / 2.0

    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="%.0f" height="%.0f" '
           'viewBox="0 0 %.0f %.0f" font-family="%s">'
           % (width, height, width, height, FONT)]
    out.append('<style>%s</style>' % STYLE)
    out.append('<rect class="bg" x="0" y="0" width="%.0f" height="%.0f"/>' % (width, height))
    out.append('<defs><marker id="a" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="6" '
               'markerHeight="6" orient="auto"><path d="M0 0 L8 4 L0 8 z" class="arw"/>'
               '</marker></defs>')

    if title:
        out.append('<text class="mut" x="%.1f" y="%.1f" font-size="%.1f">%s</text>'
                   % (body_x, MARGIN + FS, FS, _esc(title)))

    # ---------------------------------------------------------------- 연결선
    # 단계에서 다음 단계로. `edge` 는 선 옆에 글만 놓으므로 선은 그 위아래를 잇습니다.
    anchors = []
    for it, top, h in placed:
        if it["kind"] != "edge":
            anchors.append((top, top + h))

    for i in range(len(anchors) - 1):
        y0 = anchors[i][1]
        y1 = anchors[i + 1][0]
        out.append('<path class="flw" d="M%.1f %.1f L%.1f %.1f" stroke-width="1.2" '
                   'marker-end="url(#a)"/>' % (cx, y0, cx, y1 - 1))

    # ---------------------------------------------------------------- 내용
    for it, top, h in placed:
        if it["kind"] == "stage":
            out.append('<rect class="box" x="%.1f" y="%.1f" width="%.1f" height="%.1f" '
                       'rx="4"/>' % (body_x, top, body_w, h))
            ty = top + BOX_PAD_Y + FS + 2
            out.append('<text class="hd" x="%.1f" y="%.1f" font-size="%.1f" '
                       'font-weight="600">%s</text>'
                       % (body_x + PAD, ty, FS, _esc(it["title"])))
            for line in it["lines"]:
                ty += ROW_H
                out.append('<text class="mut" x="%.1f" y="%.1f" font-size="%.1f">%s</text>'
                           % (body_x + PAD, ty, FS, _esc(line)))

        elif it["kind"] == "step":
            out.append('<text class="hd" x="%.1f" y="%.1f" font-size="%.1f" '
                       'text-anchor="middle">%s</text>'
                       % (cx, top + FS + 4, FS, _esc(it["label"])))

        else:
            out.append('<text class="nte" x="%.1f" y="%.1f" font-size="%.1f">%s</text>'
                       % (cx + 12, top + FS_SMALL + 4, FS_SMALL, _esc(it["label"])))

        ay = top + BOX_PAD_Y + FS + 2 if it["kind"] == "stage" else top + FS + 4
        for code, note in it.get("aside", []):
            if code:
                out.append('<text class="cod" x="%.1f" y="%.1f" font-size="%.1f">%s</text>'
                           % (aside_x, ay, FS_SMALL, _esc(code)))
            if note:
                # 코드가 없는 줄은 설명이 코드 칸에서 시작합니다 - 앞 줄의 코드 아래에
                # 놓여야 그 코드의 설명으로 읽힙니다.
                nx = aside_x + code_w + CODE_GAP if code else aside_x
                out.append('<text class="mut" x="%.1f" y="%.1f" font-size="%.1f">%s</text>'
                           % (nx, ay, FS_SMALL, _esc(note)))
            ay += ROW_H

    out.append('</svg>')

    path = os.path.join(out_dir, name + ".svg")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")
    print(os.path.abspath(path))
    return path
