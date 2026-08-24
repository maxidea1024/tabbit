# -*- coding: utf-8 -*-
"""primary-layout.md 8절의 예시를 엑셀 격자 모습의 SVG로 생성한다.

같은 폴더에 primary-layout-*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고
다시 실행한 뒤, PNG로 렌더해 눈으로 확인하고 커밋합니다."""
import html
import os

OUT_DIR = os.path.dirname(os.path.abspath(__file__))

FONT = "Consolas, 'Cascadia Mono', 'Malgun Gothic', monospace"
CELL_H = 24
ROWHDR_W = 30
COLHDR_H = 22
FS = 12.5          # cell font size
FS_HDR = 10.5      # row/col header font size
PAD = 7

C_BAND = "#E9ECEF"        # column/row header band
C_BAND_LINE = "#BFC6CC"
C_BAND_TEXT = "#5B6570"
C_GRID = "#D8DDE2"
C_TEXT = "#1F2328"
C_MARKER = "#1A5FA8"      # ':' layout words
C_DECL_BG = "#E3EDF8"     # declaration row tint
C_HDR_BG = "#F1F6FB"      # header rows tint
C_EXCL = "#9AA1A8"        # excluded row text
C_HASH = "#C0392B"
C_NOTE = "#8A929B"

def text_w(s):
    w = 0.0
    for ch in s:
        o = ord(ch)
        if o >= 0x2E80:      # CJK
            w += FS * 1.0
        elif ch in "iIl.,:;'|![]() ":
            w += FS * 0.46
        else:
            w += FS * 0.585
    return w

def is_num(s):
    if "," in s:      # sep 셀(`101,2`)은 텍스트
        return False
    t = s.lstrip("-").replace(".", "").replace(":", "")
    return t.isdigit() and s != ""

C_ERR_BG = "#FBEAE8"
C_ERR_LINE = "#D64541"
C_ERR_TEXT = "#B03A2E"

def build(name, rows, notes=None, title=None, errors=None):
    """rows: list[list[str]]. row[0] = marker column. notes: {row_idx(0-based): text}.
    errors: {(row_idx, col_idx)} — 붉게 강조할 셀."""
    errors = errors or set()
    ncols = max(len(r) for r in rows)
    rows = [r + [""] * (ncols - len(r)) for r in rows]

    # column widths: skip row 0 (declaration/description spill on purpose)
    widths = []
    for c in range(ncols):
        w = 52.0
        for ri, r in enumerate(rows):
            if ri == 0 and c >= 1:
                continue  # description spills
            w = max(w, text_w(r[c]) + PAD * 2)
        widths.append(min(w, 280.0))

    # 설명(B1)이 시트 테두리 밖으로 나가지 않게 마지막 컬럼을 넓힌다
    if ncols > 1 and rows[0][1]:
        need = text_w(rows[0][1]) + PAD * 2 - sum(widths[1:])
        if need > 0:
            widths[-1] += need

    note_w = 0
    if notes:
        note_w = max(text_w("◀ " + t) for t in notes.values()) + 16

    W = ROWHDR_W + sum(widths) + 1 + note_w + 8
    H = COLHDR_H + CELL_H * len(rows) + 8

    xs = [ROWHDR_W]
    for w in widths:
        xs.append(xs[-1] + w)

    p = []
    p.append(f'<svg xmlns="http://www.w3.org/2000/svg" width="{W:.0f}" height="{H:.0f}" '
             f'viewBox="0 0 {W:.0f} {H:.0f}" font-family="{FONT}">')
    if title:
        p.append(f"<title>{html.escape(title)}</title>")
    # sheet background (white regardless of theme, like a spreadsheet)
    p.append(f'<rect x="0" y="0" width="{xs[-1]+1}" height="{H-8}" fill="#FFFFFF"/>')

    # header bands
    p.append(f'<rect x="0" y="0" width="{xs[-1]}" height="{COLHDR_H}" fill="{C_BAND}"/>')
    p.append(f'<rect x="0" y="0" width="{ROWHDR_W}" height="{COLHDR_H + CELL_H*len(rows)}" fill="{C_BAND}"/>')

    # row tints (before grid lines)
    for ri, r in enumerate(rows):
        y = COLHDR_H + ri * CELL_H
        m = r[0]
        if ri == 0 and m.startswith(":"):
            p.append(f'<rect x="{ROWHDR_W}" y="{y}" width="{xs[-1]-ROWHDR_W}" height="{CELL_H}" fill="{C_DECL_BG}"/>')
        elif m.startswith(":"):
            p.append(f'<rect x="{ROWHDR_W}" y="{y}" width="{xs[-1]-ROWHDR_W}" height="{CELL_H}" fill="{C_HDR_BG}"/>')

    # error cells (fill now, border after grid lines)
    for (eri, eci) in errors:
        ex, ey = xs[eci], COLHDR_H + eri * CELL_H
        p.append(f'<rect x="{ex}" y="{ey}" width="{xs[eci+1]-ex}" height="{CELL_H}" fill="{C_ERR_BG}"/>')

    # grid lines
    y0, y1 = COLHDR_H, COLHDR_H + CELL_H * len(rows)
    for x in xs:
        p.append(f'<line x1="{x}" y1="{y0}" x2="{x}" y2="{y1}" stroke="{C_GRID}" stroke-width="1"/>')
    for ri in range(len(rows) + 1):
        y = COLHDR_H + ri * CELL_H
        p.append(f'<line x1="{ROWHDR_W}" y1="{y}" x2="{xs[-1]}" y2="{y}" stroke="{C_GRID}" stroke-width="1"/>')
    # band borders
    p.append(f'<line x1="0" y1="{COLHDR_H}" x2="{xs[-1]}" y2="{COLHDR_H}" stroke="{C_BAND_LINE}" stroke-width="1"/>')
    p.append(f'<line x1="{ROWHDR_W}" y1="0" x2="{ROWHDR_W}" y2="{y1}" stroke="{C_BAND_LINE}" stroke-width="1"/>')
    for ci in range(ncols):
        p.append(f'<line x1="{xs[ci+1]}" y1="0" x2="{xs[ci+1]}" y2="{COLHDR_H}" stroke="{C_BAND_LINE}" stroke-width="1"/>')
    for ri in range(len(rows)):
        y = COLHDR_H + (ri + 1) * CELL_H
        p.append(f'<line x1="0" y1="{y}" x2="{ROWHDR_W}" y2="{y}" stroke="{C_BAND_LINE}" stroke-width="1"/>')
    p.append(f'<rect x="0.5" y="0.5" width="{xs[-1]-0.5}" height="{y1-0.5}" fill="none" stroke="{C_BAND_LINE}" stroke-width="1"/>')

    # error cell borders (over grid lines)
    for (eri, eci) in errors:
        ex, ey = xs[eci], COLHDR_H + eri * CELL_H
        p.append(f'<rect x="{ex+0.75}" y="{ey+0.75}" width="{xs[eci+1]-ex-1.5}" height="{CELL_H-1.5}" '
                 f'fill="none" stroke="{C_ERR_LINE}" stroke-width="1.5"/>')

    # column letters / row numbers
    for ci in range(ncols):
        cx = (xs[ci] + xs[ci + 1]) / 2
        p.append(f'<text x="{cx:.1f}" y="{COLHDR_H/2 + FS_HDR*0.36:.1f}" font-size="{FS_HDR}" '
                 f'fill="{C_BAND_TEXT}" text-anchor="middle">{chr(65+ci)}</text>')
    for ri in range(len(rows)):
        cy = COLHDR_H + ri * CELL_H + CELL_H / 2 + FS_HDR * 0.36
        p.append(f'<text x="{ROWHDR_W/2:.1f}" y="{cy:.1f}" font-size="{FS_HDR}" '
                 f'fill="{C_BAND_TEXT}" text-anchor="middle">{ri+1}</text>')

    # cell text (last, so spills overlay grid lines like Excel)
    for ri, r in enumerate(rows):
        y = COLHDR_H + ri * CELL_H + CELL_H / 2 + FS * 0.36
        excluded = r[0].strip() == "#"
        for ci, val in enumerate(r):
            if val == "":
                continue
            esc = html.escape(val)
            if ci == 0:  # marker column
                color = C_HASH if val.strip() == "#" else C_MARKER
                weight = ' font-weight="bold"' if ri == 0 or val.strip() == "#" else ""
                p.append(f'<text x="{xs[0]+PAD}" y="{y:.1f}" font-size="{FS}" fill="{color}"{weight}>{esc}</text>')
                continue
            if val.strip() == "#":   # :field 행의 메모 컬럼 표시
                p.append(f'<text x="{xs[ci]+PAD}" y="{y:.1f}" font-size="{FS}" '
                         f'fill="{C_HASH}" font-weight="bold">#</text>')
                continue
            color = C_EXCL if excluded else C_TEXT
            if (ri, ci) in errors:
                color = C_ERR_TEXT
            # 모든 셀 좌측 정렬
            p.append(f'<text x="{xs[ci]+PAD}" y="{y:.1f}" font-size="{FS}" fill="{color}">{esc}</text>')

    # margin notes
    if notes:
        for ri, note in notes.items():
            y = COLHDR_H + ri * CELL_H + CELL_H / 2 + 11 * 0.36
            color = C_ERR_TEXT if note.startswith("✗") else C_NOTE
            p.append(f'<text x="{xs[-1]+10}" y="{y:.1f}" font-size="11" fill="{color}" '
                     f'font-style="italic">◀ {html.escape(note)}</text>')

    p.append("</svg>")
    path = os.path.join(OUT_DIR, f"{name}.svg")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(p))
    print(path)


build("primary-layout-table", [
    [":table Item", "상점에서 파는 아이템입니다."],
    [":field", "id@1", "name@2", "*code@3", "grade@4", "tags@5"],
    [":type", "int", "string", "string", "Grade", "string[]"],
    [":desc", "아이템 ID", "표시 이름", "고유 코드", "등급", "검색 태그"],
    [":target", "c,s", "c", "c,s", "c,s", "s"],
    ["", "1001", "금화", "gold", "Common", "돈;화폐"],
    ["#", "1002", "(작업중)", "wip", "Common", ""],
    ["", "1003", "물약", "potion", "Rare", "회복"],
], title="테이블 기본형의 시트 배치")

build("primary-layout-struct", [
    [":table Quest", "퀘스트와 보상입니다."],
    [":field", "id", "title", "reward", "slots[0].id", "slots[1].id", "pay"],
    [":type", "int", "string", "Reward", "foreign Item", "", "Reward"],
    ["", "1", "길잃은 화물", "101,2", "5001", "5002", "102,1"],
    ["", "2", "해적 소탕", "205,1", "5003", "", "103,3"],
], title="struct와 배열 표기의 시트 배치")

build("primary-layout-multirow", [
    [":table Quest", "퀘스트와 보상입니다."],
    [":field", "id", "title", "rewards[].itemId", "rewards[].count"],
    [":type", "int", "string", "foreign Item", "int (min=1)"],
    ["", "1", "길잃은 화물", "1001", "2"],
    ["", "", "", "1002", "1"],
    ["", "2", "해적 소탕", "2001", "5"],
    ["#", "", "", "2002", "1"],
], notes={
    3: "레코드 1의 첫 행이자 첫 원소",
    4: "연장 행 — 인덱스 칸이 비어 있음",
    5: "인덱스에 값 → 새 레코드",
    6: "제외 — 이 원소만 빠짐",
}, title="멀티 로우의 시트 배치")

build("primary-layout-enum", [
    [":enum Grade", "아이템 등급입니다."],
    [":field", "label", "value", "alias", "desc"],
    ["", "common", "1", "일반", "기본 등급"],
    ["", "rare", "2", "희귀", ""],
    ["", "epic", "3", "영웅", "시즌 한정"],
], title="enum의 시트 배치")

build("primary-layout-const", [
    [":const GameConfig(side=s)", "서버 전역 설정입니다."],
    [":field", "name", "type", "value", "desc"],
    ["", "maxPartySize", "int", "5", "파티 최대 인원"],
    ["", "baseSpeed", "float", "1.25", ""],
    ["", "resetAt", "timespan", "06:00:00", "일일 리셋 시각"],
    ["", "defaultGrade", "Grade", "Common", "기본 등급"],
], title="상수셋의 시트 배치")

build("primary-layout-arrays", [
    [":table Wave", "웨이브 보상입니다."],
    [":field", "id", "costs", "slots[0]", "slots[1]", "drops[]"],
    [":type", "int", "int[]", "int", "", "int"],
    ["", "1", "10;20;30", "5", "7", "100"],
    ["", "", "", "", "", "101"],
    ["", "2", "5", "9", "4", "200"],
], notes={
    3: "레코드 1 — costs는 셀 안 3개, slots는 칸 2개, drops는 행",
    4: "연장 행 — drops의 2번째 원소",
    5: "레코드 2",
}, title="배열의 세 자리")

build("primary-layout-optional", [
    [":table Npc", "마을 주민입니다."],
    [":field", "id", "name", "shopId", "greeting", "#"],
    [":type", "int", "string", "foreign Shop?", "string?", ""],
    ["", "1", "상인", "2001", "어서 오세요", "할인 담당"],
    ["", "2", "경비", "-", "-", ""],
    ["", "3", "아이", "2002", "", "순찰 5구역"],
], notes={
    3: "맨 오른쪽은 메모 컬럼 — :field의 # 가 그 표시",
    4: "- 는 값 없음(null)",
    5: "✗ 빈 칸 — 기본 정책(OnBlankCell)은 오류. - 로 적습니다",
}, errors={(5, 4)}, title="옵셔널과 값 없음, 메타 컬럼")

build("primary-layout-nested", [
    [":table Spawn", "몬스터 배치입니다."],
    [":field", "id", "pos.x", "pos.y", "pos.z", "home", "waypoints[].x", "waypoints[].y"],
    [":type", "int", "float", "float", "float", "vec3f", "float", "float"],
    ["", "1", "1.5", "0", "-2.5", "(0, 1, 0)", "10", "20"],
    ["", "", "", "", "", "", "11", "21"],
], notes={
    3: "pos.* 컬럼 셋 = 레코드 하나. home 한 칸 = 같은 형태(합성 값)",
    4: "연장 행 — 인라인 그룹의 멀티 로우",
}, title="인라인 그룹과 합성 값 타입, 중첩")

build("primary-layout-multiref", [
    [":table Shop", "보상 상점입니다."],
    [":field", "id", "itemId", "count"],
    [":type", "int", "foreign Item|CEquip", "int"],
    ["", "1", "1001", "1"],
    ["", "2", "9001", "2"],
], notes={
    3: "1001은 Item의 행",
    4: "9001은 CEquip의 행 — 어느 쪽인지는 값이 정합니다",
}, title="다중 대상 참조")

build("primary-layout-pairing", [
    [":table Stage", "보상과 비용입니다."],
    [":field", "id", "rewards[].id", "rewards[].n", "costs[]"],
    [":type", "int", "foreign Item", "int", "int"],
    ["", "1", "1001", "2", "10"],
    ["", "", "1002", "1", ""],
], notes={
    3: "rewards 1번째 · costs 1번째",
    4: "rewards 2번째 — costs는 이 행에 원소 없음",
}, title="나란한 멀티 로우 그룹의 독립 축적")

build("primary-layout-caution-multirow", [
    [":table Quest", "잘못 적은 예입니다 — 그대로 쓰지 마십시오."],
    [":field", "id", "title", "rewards[].itemId", "rewards[].count"],
    [":type", "int", "string", "foreign Item", "int"],
    ["", "1", "길잃은 화물", "1001", "2"],
    ["", "", "보너스", "1002", "1"],
    ["", "2", "", "2001", "5"],
], notes={
    4: "✗ 연장 행의 스칼라 칸 — 이 값은 레코드의 첫 행에",
    5: "✗ 인덱스를 적으면 새 레코드 — 빈 title은 기본 정책이 검출",
}, errors={(4, 2), (5, 2)}, title="멀티 로우에서 잘못 적기 쉬운 2가지")

build("primary-layout-caution-header", [
    [":table Item", "잘못 적은 헤더입니다 — 그대로 쓰지 마십시오."],
    [":field", "id", "slots[1].id", "*drops[].id", "name"],
    [":type", "int", "int", "foreign Item", "text(Common)"],
    ["", "1", "5001", "1001", "검"],
], notes={
    1: "✗ C — [0] 없이 [1]부터 · D — 배열 컬럼의 *",
    2: "✗ E — 옛 표기. string (text=Common)으로",
}, errors={(1, 2), (1, 3), (2, 4)}, title="헤더에서 잘못 적기 쉬운 3가지")

build("primary-layout-key", [
    [':table Score(key="stage,slot")', "스테이지 · 슬롯별 점수입니다."],
    [":field", "stage", "slot", "score"],
    [":type", "int", "int", "int"],
    ["", "1", "1", "1200"],
    ["", "1", "2", "800"],
    ["", "2", "1", "1500"],
], notes={
    3: "조합 (1,1) — 조합이 유일하면 됩니다",
    4: "조합 (1,2) — 성분 값은 겹쳐도 됩니다",
}, title="복합 키의 선언")
