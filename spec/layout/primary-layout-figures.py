# -*- coding: utf-8 -*-
"""primary-layout.md 8절과 matrix-declaration.md 의 예시를 엑셀 격자 모습의 SVG로 생성한다.

같은 폴더에 primary-layout-*.svg 와 matrix-declaration*.svg 를 다시 씁니다. 예시를 고치면 이 파일을 고치고
다시 실행한 뒤, PNG로 렌더해 눈으로 확인하고 커밋합니다."""
import html
import os

# 이 파일을 불러다 쓰는 쪽이 자기 자리에 그리게 하는 통로입니다. 불러오기만 해도 그림을
# 쓰기 때문에, 부르는 쪽이 나중에 자리를 옮겨서는 이 파일의 그림이 먼저 덮어써집니다.
OUT_DIR = os.environ.get(
    "TABBIT_LAYOUT_FIGURES", os.path.dirname(os.path.abspath(__file__)))

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

    def spill_end(ri, ci):
        """왼쪽 정렬한 글이 넘칠 수 있는 데까지 — 오른쪽의 첫 빈칸 아닌 셀 앞까지입니다."""
        j = ci + 1
        while j < ncols and rows[ri][j] == "":
            j += 1
        return j

    # 컬럼 폭. 선언 행의 설명은 재지 않습니다 — 시트에서 그렇듯 옆 빈칸으로 흘러넘치는
    # 것이 제 모습이기 때문입니다. **선언 셀은 잽니다.** `:table Hero`처럼 오른쪽에 자기
    # 설명이 붙어 있는 것은 넘칠 자리가 없고, 재지 않으면 그 설명 위에 겹쳐 그려집니다.
    widths = []
    for c in range(ncols):
        w = 52.0
        for ri, r in enumerate(rows):
            if ri == 0 and c >= 1 and not r[c].startswith(":"):
                continue
            w = max(w, text_w(r[c]) + PAD * 2)
        widths.append(min(w, 280.0))

    # 넘쳐도 되는 설명이 시트 테두리 밖까지 나가지 않게 마지막 컬럼을 넓힙니다. 오른쪽에
    # 다른 셀이 있어 잘리는 설명은 넓힐 이유가 없으므로 세지 않습니다.
    for ci in range(1, ncols):
        text = rows[0][ci]

        if text == "" or text.startswith(":") or spill_end(0, ci) < ncols:
            continue

        need = text_w(text) + PAD * 2 - sum(widths[ci:])
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
    defs_at = len(p)
    defs = []

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

            # **넘치는 글은 옆 셀 앞에서 잘립니다** - 시트가 그렇게 보여 주기 때문입니다.
            # 자를 자리가 없으면 옆 셀의 글 위에 두 겹으로 그려지고, 그림을 읽는 사람은
            # 어느 쪽이 어느 셀의 값인지 알 수 없습니다.
            end = spill_end(ri, ci)
            room = xs[end] - xs[ci] - PAD

            clip = ""
            if text_w(val) > room:
                cid = f"{name}-clip-{ri}-{ci}"
                top = COLHDR_H + ri * CELL_H
                defs.append(
                    f'<clipPath id="{cid}"><rect x="{xs[ci]:.1f}" y="{top:.1f}" '
                    f'width="{xs[end]-xs[ci]:.1f}" height="{CELL_H}"/></clipPath>')
                clip = f' clip-path="url(#{cid})"'

            # 모든 셀 좌측 정렬
            p.append(f'<text x="{xs[ci]+PAD}" y="{y:.1f}" font-size="{FS}" '
                     f'fill="{color}"{clip}>{esc}</text>')

    # margin notes
    if notes:
        for ri, note in notes.items():
            y = COLHDR_H + ri * CELL_H + CELL_H / 2 + 11 * 0.36
            color = C_ERR_TEXT if note.startswith("✗") else C_NOTE
            p.append(f'<text x="{xs[-1]+10}" y="{y:.1f}" font-size="11" fill="{color}" '
                     f'font-style="italic">◀ {html.escape(note)}</text>')

    if defs:
        p.insert(defs_at, "<defs>" + "".join(defs) + "</defs>")

    p.append("</svg>")
    path = os.path.join(OUT_DIR, f"{name}.svg")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(p))
    print(path)


# 이 파일의 그림들. **불러다 쓰는 쪽에서는 그리지 않습니다** - 격자를 그리는 코드만 가져다
# 쓰는 문서가 둘 있고, 그 둘을 돌릴 때마다 spec 의 그림까지 다시 쓰이면 어느 실행이 무엇을
# 바꾸었는지 알 수 없게 됩니다.
if __name__ == "__main__":
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
        [":type", "int", "int (refs=Item;CharGear)", "int"],
        ["", "1", "1001", "1"],
        ["", "2", "9001", "2"],
    ], notes={
        3: "1001은 Item에 있습니다",
        4: "9001은 CharGear에 — 둘 중 어디에도 없으면 그 셀을 가리켜 보고합니다",
    }, title="여러 테이블 중 하나여도 되는 값")

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

    build("primary-layout-variant", [
        [":table Item", "지역별 가격입니다."],
        [":field", "id", "name", "price", "price", "price"],
        [":variant", "", "", "", "kr", "jp"],
        [":type", "int", "string", "int", "", ""],
        ["", "1", "금화", "100", "120", "110"],
        ["", "2", "물약", "50", "60", "55"],
    ], notes={
        2: "빈 칸이 기본 변형 — kr · jp 가 변형 이름",
        4: "빌드가 price 하나를 고릅니다 — 나머지 컬럼은 그 빌드에 없음",
    }, title="필드 변형의 시트 배치")


    build("matrix-declaration", [
        [":matrix TownPrice", "지역별 교역품 보정치입니다."],
        [":field", "town", "value"],
        [":type", "foreign Town", "int? (min=-100)"],
        [":col", "goods foreign Goods", "11000001", "11000002", "11000003"],
        ["", "21000001", "0", "-25", "-125"],
        ["", "21000002", "10", "0", "-40"],
    ], notes={
        0: "선언 셀은 A1 한 칸 — B1이 설명",
        1: "B는 행 축의 이름, C 한 칸이 격자 전체의 이름",
        2: "타입도 같은 자리 — D 이후는 비어 있어야 합니다",
        3: "B는 열 축의 이름과 타입, C부터가 열 축의 키",
        4: "B는 행 축의 키, C부터가 값",
    }, title="매트릭스 선언의 시트 배치")

    build("matrix-declaration-enum", [
        [":matrix ElementChart", "속성 상성표입니다."],
        [":field", "attacker", "rate"],
        [":type", "Element", "float"],
        [":col", "defender Element", "Fire", "Water", "Wind", "Earth"],
        ["", "Fire", "1.0", "0.5", "2.0", "1.0"],
        ["", "Water", "2.0", "1.0", "1.0", "0.5"],
        ["", "Wind", "0.5", "1.0", "1.0", "2.0"],
    ], notes={
        3: "열 축의 키가 enum 라벨입니다",
        4: "두 축이 같은 enum이어도 축은 둘입니다",
    }, title="축이 enum인 격자")
