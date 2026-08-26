# -*- coding: utf-8 -*-
"""`data/*.tsv` 를 읽어 `unity/Assets/Resources/art/` 의 그림을 만든다.

**이름을 표가 정한다.** 손으로 적은 목록이 없고, `asset=` 컬럼에 적힌 이름 그대로 파일이
나온다. 종이 늘면 그림도 늘고, 이름을 고치면 그림 이름도 따라간다.

**색과 형태도 표가 정한다.** 와일드링은 `element` 로 색, `grade` 로 테두리, `role` 로 실루엣,
`stage` 로 장식이 갈린다. 그래서 아이콘이 데이터와 어긋나면 화면에서 보인다 — 잎새 속성인데
붉은 아이콘이 나오면 그 자리가 틀린 것이다.

    python samples/wildling/design-data/tools/art.py

의존성은 없다. `raster.py` 가 PNG 를 직접 쓴다.
"""
import io
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from raster import Canvas, Rng, hex_color, mix, shade  # noqa: E402

DATA = os.path.normpath(os.path.join(HERE, "..", "data"))
ART = os.path.normpath(os.path.join(
    HERE, "..", "..", "unity", "Assets", "Resources", "art"))

ICON = 128
BG_W, BG_H = 640, 360


# ---------------------------------------------------------------- 격자 읽기

def read_grid(filename):
    """`.tsv` 하나에서 엔티티마다 `(선언, 헤더맵, 데이터행들)` 을 낸다."""
    path = os.path.join(DATA, filename)
    rows = [line.rstrip("\n").split("\t")
            for line in io.open(path, encoding="utf-8").read().split("\n")]

    entities, decl, header, body = [], None, {}, []
    for row in rows:
        marker = row[0] if row else ""
        if not any(cell.strip() for cell in row):
            if decl:
                entities.append((decl, header, body))
            decl, header, body = None, {}, []
        elif marker.startswith(":") and not marker.startswith(":field") \
                and not marker.startswith(":type") and not marker.startswith(":desc") \
                and not marker.startswith(":target") and not marker.startswith(":variant"):
            if decl:
                entities.append((decl, header, body))
            decl, header, body = marker, {}, []
        elif marker.startswith(":"):
            header[marker[1:]] = row[1:]
        elif marker == "":
            if any(cell.strip() for cell in row[1:]):
                body.append(row[1:])
    if decl:
        entities.append((decl, header, body))
    return entities


def records(filename, entity=0):
    """데이터 행을 `{컬럼: 값}` 목록으로 낸다. 멀티 로우의 연장 행은 앞 행을 잇는다."""
    decl, header, body = read_grid(filename)[entity]
    fields = [f.lstrip("*").split("@")[0] for f in header["field"]]
    out = []
    for row in body:
        cells = [(row[i] if i < len(row) else "").strip() for i in range(len(fields))]
        row_map = dict(zip(fields, cells))
        if not cells[0] and out:
            out[-1].setdefault("__more", []).append(row_map)
        else:
            out.append(row_map)
    return out


# ---------------------------------------------------------------- 색

ELEMENT = {
    "Flame": dict(base=(226, 84, 44), deep=(112, 28, 20), light=(255, 190, 120)),
    "Tide": dict(base=(46, 134, 201), deep=(16, 52, 94), light=(158, 226, 245)),
    "Leaf": dict(base=(79, 168, 69), deep=(28, 74, 36), light=(200, 232, 138)),
    "Arc": dict(base=(228, 190, 46), deep=(104, 74, 16), light=(255, 240, 158)),
    "Umbra": dict(base=(139, 84, 194), deep=(50, 26, 82), light=(212, 176, 246)),
}
NEUTRAL = dict(base=(150, 152, 162), deep=(58, 60, 70), light=(226, 228, 236))

GRADE = {
    "Common": (154, 165, 177),
    "Rare": (76, 141, 224),
    "Epic": (166, 92, 224),
    "Legendary": (224, 166, 60),
    "Mythic": (224, 92, 122),
}
GRADE_PIPS = {"Common": 1, "Rare": 2, "Epic": 3, "Legendary": 4, "Mythic": 5}

INK = (26, 24, 34)


def palette(element):
    return ELEMENT.get(element, NEUTRAL)


# ---------------------------------------------------------------- 바탕

def plate(canvas, pal, grade, size=ICON):
    """아이콘의 바탕이다 — 색은 속성이, 테두리와 표식은 등급이 정한다."""
    pad = size * 0.031
    frame = GRADE.get(grade, GRADE["Common"])

    canvas.rect(pad - 1, pad - 1, size - pad + 1, size - pad + 1,
                shade(frame, -0.55), radius=size * 0.16)
    canvas.gradient_rect(pad + 2, pad + 2, size - pad - 2, size - pad - 2,
                         shade(pal["deep"], 0.22), pal["deep"], radius=size * 0.14)
    canvas.gradient_rect(pad + 2, pad + 2, size - pad - 2, size * 0.52,
                         shade(pal["base"], 0.12), pal["base"],
                         radius=size * 0.14, alpha=0.35)

    # 등급 표식. 왼쪽 아래에 등급 수만큼이다.
    pips = GRADE_PIPS.get(grade, 1)
    for i in range(pips):
        cx = size * 0.13 + i * size * 0.062
        canvas.ellipse(cx, size * 0.895, size * 0.021, size * 0.021, shade(frame, 0.35))


def vignette(canvas, size=ICON):
    """가장자리를 어둡게 해 아이콘끼리 구별되게 한다."""
    canvas.rect(0, size * 0.72, size, size, INK, alpha=0.16, radius=size * 0.1)


# ---------------------------------------------------------------- 와일드링

EAR_KINDS = ("horn", "round", "fin", "antenna", "crest")
TAIL_KINDS = ("none", "curl", "fan", "orb")
PATTERN_KINDS = ("plain", "belly", "stripe", "spot")

# 역할이 갈래를 좁힌다. 선봉은 무겁고 조율은 가볍다.
BUILD_BY_ROLE = {
    "Vanguard": ("bulk", "beast", "bulk"),
    "Breaker": ("beast", "bird", "beast"),
    "Warden": ("bulk", "serpent", "bulk"),
    "Tuner": ("bird", "serpent", "bird"),
}


class Body(object):
    """몸의 자리이다. 갈래마다 값이 다르고 나머지 부위가 이것을 기준으로 붙는다."""

    __slots__ = ("cx", "cy", "rx", "ry", "head_x", "head_y", "head_r", "ground")


def _rim_ellipse(canvas, cx, cy, rx, ry, fill, rim, width, rot=0.0):
    """윤곽을 두른 타원이다. 바탕과 겹쳐도 형태가 보이게 한다."""
    canvas.ellipse(cx, cy, rx + width, ry + width, rim, rot=rot)
    canvas.ellipse(cx, cy, rx, ry, fill, rot=rot)


def creature(canvas, seed, pal, role, stage, size=ICON):
    """와일드링 하나이다. 씨앗은 종 식별자이므로 같은 종의 세 단계가 닮는다."""
    rng = Rng(seed)
    scale = (0.88, 1.0, 1.10)[max(0, min(2, stage - 1))]

    build = BUILD_BY_ROLE.get(role, ("beast", "bird", "bulk"))[rng.int(0, 2)]
    ear = EAR_KINDS[rng.int(0, 4)]
    tail = TAIL_KINDS[rng.int(0, 3)]
    pattern = PATTERN_KINDS[rng.int(0, 3)]

    # **같은 속성 안에서도 종마다 색이 조금씩 다르다.** 속성은 알아볼 수 있어야 하므로
    # 원래 색에서 크게 벗어나지 않는 범위이다.
    tint = rng.float(-0.16, 0.16)
    pal = dict(base=shade(pal["base"], tint),
               deep=shade(pal["deep"], tint * 0.5),
               light=shade(pal["light"], tint * 0.6))

    body_color = pal["base"]
    belly = shade(pal["light"], -0.06)
    dark = shade(pal["deep"], 0.06)
    rim = shade(pal["deep"], -0.30)
    rim_w = size * 0.016
    ground = size * 0.845

    canvas.ellipse(size * 0.5, ground + size * 0.020,
                   size * 0.24 * scale, size * 0.026, INK, alpha=0.32)

    b = Body()
    b.ground = ground

    # ------------------------------------------------------------ 갈래별 자리
    if build == "beast":
        b.rx, b.ry = size * 0.205 * scale, size * 0.128 * scale
        b.cx, b.cy = size * 0.455, size * 0.560
        b.head_r = size * 0.112 * scale * rng.float(0.94, 1.10)
        b.head_x, b.head_y = size * 0.715, size * 0.395
        leg_top, leg_w, leg_n = b.cy + b.ry * 0.35, size * 0.046 * scale, 4
    elif build == "bulk":
        b.rx, b.ry = size * 0.240 * scale, size * 0.165 * scale
        b.cx, b.cy = size * 0.470, size * 0.545
        b.head_r = size * 0.132 * scale * rng.float(0.94, 1.08)
        b.head_x, b.head_y = size * 0.715, size * 0.470
        leg_top, leg_w, leg_n = b.cy + b.ry * 0.45, size * 0.062 * scale, 4
    elif build == "bird":
        b.rx, b.ry = size * 0.150 * scale, size * 0.190 * scale
        b.cx, b.cy = size * 0.475, size * 0.520
        b.head_r = size * 0.104 * scale * rng.float(0.94, 1.10)
        b.head_x, b.head_y = size * 0.555, size * 0.300
        leg_top, leg_w, leg_n = b.cy + b.ry * 0.55, size * 0.028 * scale, 2
    else:  # serpent
        b.rx, b.ry = size * 0.185 * scale, size * 0.115 * scale
        b.cx, b.cy = size * 0.450, size * 0.640
        b.head_r = size * 0.100 * scale * rng.float(0.94, 1.10)
        b.head_x, b.head_y = size * 0.660, size * 0.330
        leg_top, leg_w, leg_n = 0.0, 0.0, 0

    # ------------------------------------------------------------ 3단의 울림
    if stage >= 3:
        canvas.ring(size * 0.5, size * 0.50, size * 0.395, size * 0.014,
                    pal["light"], alpha=0.34)

    # ------------------------------------------------------------ 꼬리
    tx, ty = b.cx - b.rx * 0.80, b.cy - b.ry * 0.10
    if tail == "curl":
        canvas.capsule(tx, ty, tx - size * 0.115, ty - size * 0.115,
                       size * 0.048 * scale, rim)
        canvas.capsule(tx, ty, tx - size * 0.115, ty - size * 0.115,
                       size * 0.048 * scale - rim_w * 1.4, dark)
        _rim_ellipse(canvas, tx - size * 0.125, ty - size * 0.125,
                     size * 0.040 * scale, size * 0.040 * scale, pal["light"], rim, rim_w)
    elif tail == "fan":
        canvas.poly([(tx + size * 0.02, ty + size * 0.04),
                     (tx - size * 0.175, ty - size * 0.130),
                     (tx - size * 0.150, ty + size * 0.010),
                     (tx - size * 0.180, ty + size * 0.135)], rim)
        canvas.poly([(tx, ty + size * 0.028),
                     (tx - size * 0.150, ty - size * 0.105),
                     (tx - size * 0.128, ty + size * 0.008),
                     (tx - size * 0.155, ty + size * 0.110)], shade(pal["light"], -0.14))
    elif tail == "orb":
        canvas.capsule(tx, ty + size * 0.03, tx - size * 0.150, ty - size * 0.02,
                       size * 0.030 * scale, rim)
        canvas.capsule(tx, ty + size * 0.03, tx - size * 0.150, ty - size * 0.02,
                       size * 0.030 * scale - rim_w * 1.2, dark)
        _rim_ellipse(canvas, tx - size * 0.160, ty - size * 0.025,
                     size * 0.046 * scale, size * 0.046 * scale, pal["light"], rim, rim_w)

    # ------------------------------------------------------------ 다리
    for i in range(leg_n):
        t = (i / float(max(1, leg_n - 1))) * 2.0 - 1.0
        lx = b.cx + t * b.rx * (0.66 if leg_n == 4 else 0.44)
        canvas.capsule(lx, leg_top, lx, ground - leg_w * 0.4, leg_w + rim_w * 1.6, rim)
        canvas.capsule(lx, leg_top, lx, ground - leg_w * 0.4, leg_w, dark)
        canvas.ellipse(lx + leg_w * 0.25, ground - leg_w * 0.35,
                       leg_w * 0.78, leg_w * 0.46, shade(dark, 0.16))

    # ------------------------------------------------------------ 몸
    if build == "serpent":
        # 사려 놓은 몸이다. 아래에서 위로 좁아지는 고리 셋이다.
        for i in range(3):
            t = i / 2.0
            _rim_ellipse(canvas, b.cx + (0.5 - t) * size * 0.10,
                         b.cy - t * size * 0.115,
                         b.rx * (1.0 - t * 0.34), b.ry * (1.0 - t * 0.30),
                         mix(body_color, belly, t * 0.35), rim, rim_w)
    else:
        _rim_ellipse(canvas, b.cx, b.cy, b.rx, b.ry, body_color, rim, rim_w)
        canvas.ellipse(b.cx + b.rx * 0.10, b.cy + b.ry * 0.30,
                       b.rx * 0.72, b.ry * 0.56, belly, alpha=0.80)

    if build == "bird":
        # 날개 하나가 몸 앞을 덮는다.
        _rim_ellipse(canvas, b.cx - b.rx * 0.30, b.cy + b.ry * 0.02,
                     b.rx * 0.62, b.ry * 0.66, shade(body_color, -0.16), rim,
                     rim_w * 0.8, rot=-0.35)

    if pattern == "stripe":
        for i in range(3):
            sx = b.cx - b.rx * 0.34 + i * b.rx * 0.40
            canvas.capsule(sx, b.cy - b.ry * 0.56, sx - b.rx * 0.14, b.cy + b.ry * 0.16,
                           size * 0.020 * scale, dark, alpha=0.66)
    elif pattern == "spot":
        for i in range(4):
            canvas.ellipse(b.cx + rng.float(-0.55, 0.55) * b.rx,
                           b.cy + rng.float(-0.45, 0.35) * b.ry,
                           size * 0.024 * scale, size * 0.021 * scale, dark, alpha=0.60)

    # 등의 돌기. 단계마다 하나씩 늘어난다.
    if build != "bird":
        for i in range(stage + 1):
            t = (i / float(stage + 1)) - 0.34
            sx = b.cx + t * b.rx * 1.6
            sy = b.cy - b.ry * math.sqrt(max(0.0, 1.0 - min(1.0, (t * 1.6) ** 2))) - rim_w
            canvas.poly([(sx - size * 0.028 * scale, sy + size * 0.014),
                         (sx, sy - size * 0.062 * scale),
                         (sx + size * 0.028 * scale, sy + size * 0.014)], rim)
            canvas.poly([(sx - size * 0.020 * scale, sy + size * 0.014),
                         (sx, sy - size * 0.050 * scale),
                         (sx + size * 0.020 * scale, sy + size * 0.014)], pal["light"])

    # ------------------------------------------------------------ 목
    hx, hy, hr = b.head_x, b.head_y, b.head_r
    canvas.capsule(b.cx + b.rx * 0.46, b.cy - b.ry * 0.34, hx - hr * 0.36, hy + hr * 0.52,
                   hr * 0.44 + rim_w * 1.4, rim)
    canvas.capsule(b.cx + b.rx * 0.46, b.cy - b.ry * 0.34, hx - hr * 0.36, hy + hr * 0.52,
                   hr * 0.44, shade(body_color, -0.10))

    # ------------------------------------------------------------ 귀와 뿔
    horn_h = hr * (0.85 + 0.40 * (stage - 1))
    for side in (-1, 1):
        ex = hx + side * hr * 0.56
        ey = hy - hr * 0.62
        if ear == "horn":
            canvas.poly([(ex - hr * 0.26, ey + hr * 0.30),
                         (ex + side * hr * 0.18, ey - horn_h),
                         (ex + hr * 0.26, ey + hr * 0.30)], rim)
            canvas.poly([(ex - hr * 0.17, ey + hr * 0.26),
                         (ex + side * hr * 0.16, ey - horn_h * 0.90),
                         (ex + hr * 0.17, ey + hr * 0.26)], pal["light"])
        elif ear == "round":
            _rim_ellipse(canvas, ex, ey, hr * 0.40, hr * 0.46, body_color, rim, rim_w)
            canvas.ellipse(ex, ey, hr * 0.20, hr * 0.26, belly)
        elif ear == "fin":
            canvas.poly([(ex, ey + hr * 0.38),
                         (ex + side * hr * 1.00, ey - hr * 0.34),
                         (ex + side * hr * 0.34, ey - hr * 1.00)], rim)
            canvas.poly([(ex, ey + hr * 0.28),
                         (ex + side * hr * 0.86, ey - hr * 0.30),
                         (ex + side * hr * 0.30, ey - hr * 0.86)],
                        shade(pal["light"], -0.10))
        elif ear == "antenna":
            canvas.capsule(ex, ey + hr * 0.24, ex + side * hr * 0.50, ey - horn_h,
                           hr * 0.14 + rim_w, rim)
            canvas.capsule(ex, ey + hr * 0.24, ex + side * hr * 0.50, ey - horn_h,
                           hr * 0.14, dark)
            _rim_ellipse(canvas, ex + side * hr * 0.50, ey - horn_h,
                         hr * 0.19, hr * 0.19, pal["light"], rim, rim_w * 0.8)
        elif side < 0:  # crest — 머리 위로 한 줄이다
            for i in range(stage + 2):
                t = i / float(stage + 2)
                bx = hx - hr * (0.85 - t * 1.15)
                canvas.poly([(bx - hr * 0.22, hy - hr * 0.66),
                             (bx, hy - hr * (1.22 + t * 0.55)),
                             (bx + hr * 0.22, hy - hr * 0.66)], rim)
                canvas.poly([(bx - hr * 0.15, hy - hr * 0.70),
                             (bx, hy - hr * (1.14 + t * 0.55)),
                             (bx + hr * 0.15, hy - hr * 0.70)], pal["light"])

    # ------------------------------------------------------------ 머리
    _rim_ellipse(canvas, hx, hy, hr, hr * rng.float(0.90, 1.02), body_color, rim, rim_w)

    if build == "bird":
        canvas.poly([(hx + hr * 0.55, hy - hr * 0.10),
                     (hx + hr * 1.42, hy + hr * 0.14),
                     (hx + hr * 0.55, hy + hr * 0.42)], shade(pal["light"], -0.20))
    else:
        _rim_ellipse(canvas, hx + hr * 0.58, hy + hr * 0.30, hr * 0.50, hr * 0.36,
                     belly, rim, rim_w * 0.7)
        canvas.ellipse(hx + hr * 0.92, hy + hr * 0.20, hr * 0.12, hr * 0.10, INK)

    eye_y = hy - hr * 0.16
    for ex in (hx - hr * 0.20, hx + hr * 0.38):
        canvas.ellipse(ex, eye_y, hr * 0.17, hr * 0.22, INK)
        canvas.ellipse(ex + hr * 0.06, eye_y - hr * 0.08, hr * 0.06, hr * 0.07,
                       (255, 255, 255))


# ---------------------------------------------------------------- 스킬

def skill_glyph(canvas, pal, scope, kinds, size=ICON):
    """스킬 하나이다. 배치는 대상 범위가, 기호는 효과 변종이 정한다."""
    cx, cy = size * 0.5, size * 0.45
    # 원반은 어둡게 둔다. 기호가 밝은 색이므로 바탕이 밝으면 묻힌다.
    canvas.radial(cx, cy, size * 0.33, pal["base"], shade(pal["deep"], -0.15), alpha=0.85)
    canvas.ring(cx, cy, size * 0.33, size * 0.020, shade(pal["light"], -0.10), alpha=0.55)

    kind = kinds[0] if kinds else "DamageEffect"
    light = shade(pal["light"], 0.35)
    rim = shade(pal["deep"], -0.45)

    def mark(mx, my, r):
        if kind == "DamageEffect":
            # 발톱 자국 셋이다.
            for i in range(3):
                a = -1.15 + i * 0.34
                x0, y0 = mx - math.cos(a) * r * 0.95, my - math.sin(a) * r * 1.20
                x1, y1 = mx + math.cos(a) * r * 0.95, my + math.sin(a) * r * 1.20
                canvas.capsule(x0, y0, x1, y1, r * 0.34, rim)
                canvas.capsule(x0, y0, x1, y1, r * 0.20, light)
        elif kind == "HealEffect":
            canvas.capsule(mx, my - r * 0.92, mx, my + r * 0.92, r * 0.60, rim)
            canvas.capsule(mx - r * 0.72, my, mx + r * 0.72, my, r * 0.60, rim)
            canvas.capsule(mx, my - r * 0.92, mx, my + r * 0.92, r * 0.40, light)
            canvas.capsule(mx - r * 0.72, my, mx + r * 0.72, my, r * 0.40, light)
        elif kind == "BuffEffect":
            for i in range(2):
                oy = my + r * (0.52 - i * 0.66)
                pts = [(mx - r * 0.92, oy + r * 0.40), (mx, oy - r * 0.44),
                       (mx + r * 0.92, oy + r * 0.40), (mx, oy + r * 0.06)]
                canvas.poly([(x, y + r * 0.10) for x, y in pts], rim)
                canvas.poly(pts, light)
        else:  # StatusEffect — 감아 도는 점이다
            for i in range(7):
                t = i / 6.0
                a = t * 4.6
                rr = r * (0.16 + t * 0.86)
                px, py = mx + math.cos(a) * rr, my + math.sin(a) * rr
                dot = r * (0.13 + t * 0.15)
                canvas.ellipse(px, py, dot + r * 0.07, dot + r * 0.07, rim)
                canvas.ellipse(px, py, dot, dot, light)

    if scope in ("AllEnemy", "AllAlly"):
        for i in range(3):
            t = i - 1
            mark(cx + t * size * 0.185, cy + abs(t) * size * 0.050, size * 0.098)
    else:
        mark(cx, cy, size * 0.185)

    # 아군 대상은 방패가 붙는다.
    if scope in ("OneAlly", "AllAlly"):
        sx, sy = size * 0.50, size * 0.775
        shield = [(sx - size * 0.115, sy - size * 0.085), (sx + size * 0.115, sy - size * 0.085),
                  (sx + size * 0.105, sy + size * 0.030), (sx, sy + size * 0.098),
                  (sx - size * 0.105, sy + size * 0.030)]
        canvas.poly([(x, y + size * 0.010) for x, y in shield], rim)
        canvas.poly(shield, shade(pal["light"], 0.15))
        canvas.capsule(sx, sy - size * 0.048, sx, sy + size * 0.040, size * 0.022,
                       shade(pal["deep"], 0.10))

    # 둘째 효과가 있으면 오른쪽 위에 점으로 표시한다.
    for i, extra in enumerate(kinds[1:3]):
        dx, dy = size * 0.855, size * 0.145 + i * size * 0.076
        canvas.ellipse(dx, dy, size * 0.038, size * 0.038, rim)
        canvas.ellipse(dx, dy, size * 0.028, size * 0.028,
                       {"DamageEffect": (236, 104, 84), "HealEffect": (118, 220, 138),
                        "BuffEffect": (244, 214, 104),
                        "StatusEffect": (194, 138, 238)}.get(extra, (222, 222, 222)))


# ---------------------------------------------------------------- 아이템

def item_glyph(canvas, pal, kind, variant, size=ICON):
    """아이템 하나이다. 형태는 분류가, 색은 지역이나 속성이 정한다."""
    cx, cy = size * 0.5, size * 0.50
    base, light, deep = pal["base"], pal["light"], shade(pal["deep"], 0.06)

    if kind == "core":
        pts = [(cx + math.cos(a) * size * 0.26, cy + math.sin(a) * size * 0.29)
               for a in [math.pi * (0.5 + i / 3.0) for i in range(6)]]
        canvas.poly(pts, base)
        canvas.poly([pts[0], pts[1], (cx, cy)], light, alpha=0.75)
        canvas.poly([pts[3], pts[4], (cx, cy)], deep, alpha=0.55)
    elif kind == "dust":
        rng = Rng(variant)
        for i in range(9):
            r = size * rng.float(0.030, 0.062)
            canvas.ellipse(cx + rng.float(-0.22, 0.22) * size,
                           cy + rng.float(-0.16, 0.22) * size, r, r,
                           mix(base, light, rng.float()))
    elif kind == "resin":
        canvas.poly([(cx, cy - size * 0.30), (cx + size * 0.21, cy + size * 0.10),
                     (cx, cy + size * 0.28), (cx - size * 0.21, cy + size * 0.10)], base)
        canvas.ellipse(cx - size * 0.07, cy + size * 0.03,
                       size * 0.055, size * 0.075, light, alpha=0.8)
    elif kind == "relic":
        canvas.rect(cx - size * 0.20, cy - size * 0.27, cx + size * 0.20, cy + size * 0.27,
                    base, radius=size * 0.05)
        for i in range(3):
            y = cy - size * 0.14 + i * size * 0.14
            canvas.capsule(cx - size * 0.11, y, cx + size * 0.11, y, size * 0.030, deep)
    elif kind == "sigil":
        pts = []
        for i in range(10):
            a = math.pi * (-0.5 + i / 5.0)
            r = size * (0.29 if i % 2 == 0 else 0.13)
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
        canvas.poly(pts, base)
        canvas.ellipse(cx, cy, size * 0.075, size * 0.075, light)
    elif kind == "awaken":
        count = max(1, min(3, variant))
        for i in range(count):
            t = (i - (count - 1) * 0.5)
            hx = cx + t * size * 0.15
            hy = cy + abs(t) * size * 0.07
            h = size * (0.27 - abs(t) * 0.05)
            canvas.poly([(hx, hy - h), (hx + size * 0.085, hy),
                         (hx, hy + h * 0.72), (hx - size * 0.085, hy)], base)
            canvas.poly([(hx, hy - h), (hx + size * 0.085, hy), (hx, hy)], light, alpha=0.7)
    elif kind == "food":
        # 먹이는 24종이므로 그릇의 모양과 담긴 것을 씨앗으로 가른다.
        rng = Rng(variant)
        bowl = rng.int(0, 2)
        if bowl == 0:  # 그릇
            canvas.poly([(cx - size * 0.26, cy - size * 0.02),
                         (cx + size * 0.26, cy - size * 0.02),
                         (cx + size * 0.17, cy + size * 0.24),
                         (cx - size * 0.17, cy + size * 0.24)], base)
            canvas.ellipse(cx, cy - size * 0.02, size * 0.26, size * 0.09, light)
        elif bowl == 1:  # 자루
            canvas.rect(cx - size * 0.20, cy - size * 0.10, cx + size * 0.20, cy + size * 0.26,
                        base, radius=size * 0.06)
            canvas.capsule(cx - size * 0.13, cy - size * 0.10, cx + size * 0.13,
                           cy - size * 0.10, size * 0.055, deep)
        else:  # 잎에 싼 것
            canvas.ellipse(cx, cy + size * 0.08, size * 0.25, size * 0.17, base)
            canvas.poly([(cx - size * 0.25, cy + size * 0.06), (cx, cy - size * 0.24),
                         (cx + size * 0.25, cy + size * 0.06)], shade(base, -0.18))

        count = rng.int(2, 4)
        for i in range(count):
            t = (i - (count - 1) * 0.5) / max(1.0, count - 1.0)
            r = size * rng.float(0.045, 0.070)
            canvas.ellipse(cx + t * size * 0.26, cy - size * rng.float(0.02, 0.09), r, r * 0.9,
                           mix(light, deep, rng.float(0.15, 0.85)))
    elif kind == "boost":
        # 강화제는 16종이므로 병의 모양과 내용물의 높이를 가른다.
        rng = Rng(variant)
        neck = size * rng.float(0.16, 0.24)
        fill_top = cy + size * rng.float(-0.06, 0.10)
        flask = rng.int(0, 2)
        canvas.capsule(cx, cy - neck - size * 0.06, cx, cy - neck, size * 0.085, deep)
        if flask == 0:  # 삼각 플라스크
            glass = [(cx - size * 0.06, cy - neck), (cx + size * 0.06, cy - neck),
                     (cx + size * 0.20, cy + size * 0.25), (cx - size * 0.20, cy + size * 0.25)]
        elif flask == 1:  # 둥근 병
            glass = [(cx - size * 0.07, cy - neck), (cx + size * 0.07, cy - neck),
                     (cx + size * 0.19, cy + size * 0.06), (cx + size * 0.15, cy + size * 0.25),
                     (cx - size * 0.15, cy + size * 0.25), (cx - size * 0.19, cy + size * 0.06)]
        else:  # 각진 병
            glass = [(cx - size * 0.13, cy - neck), (cx + size * 0.13, cy - neck),
                     (cx + size * 0.17, cy + size * 0.25), (cx - size * 0.17, cy + size * 0.25)]
        canvas.poly(glass, shade(light, 0.40), alpha=0.50)
        canvas.poly([(x, max(y, fill_top)) for x, y in glass], base)
        for i in range(rng.int(1, 3)):
            canvas.ellipse(cx + rng.float(-0.09, 0.09) * size,
                           fill_top + size * rng.float(0.04, 0.16),
                           size * 0.026, size * 0.026, light, alpha=0.75)
    else:  # ticket
        # 입장권은 12종이므로 가운데 표식을 가른다.
        rng = Rng(variant)
        canvas.rect(cx - size * 0.28, cy - size * 0.16, cx + size * 0.28, cy + size * 0.16,
                    base, radius=size * 0.035)
        canvas.ellipse(cx - size * 0.28, cy, size * 0.055, size * 0.055, pal["deep"])
        canvas.ellipse(cx + size * 0.28, cy, size * 0.055, size * 0.055, pal["deep"])
        canvas.capsule(cx - size * 0.15, cy - size * 0.08, cx - size * 0.15, cy + size * 0.08,
                       size * 0.022, shade(base, -0.30))
        pips = rng.int(1, 3)
        mark = rng.int(0, 2)
        for i in range(pips):
            mx = cx + size * 0.04 + (i - (pips - 1) * 0.5) * size * 0.115
            if mark == 0:
                canvas.ellipse(mx, cy, size * 0.055, size * 0.055, light)
            elif mark == 1:
                canvas.poly([(mx, cy - size * 0.075), (mx + size * 0.062, cy),
                             (mx, cy + size * 0.075), (mx - size * 0.062, cy)], light)
            else:
                canvas.rect(mx - size * 0.048, cy - size * 0.055,
                            mx + size * 0.048, cy + size * 0.055, light, radius=size * 0.014)


def currency_glyph(canvas, currency_id, size=ICON):
    cx, cy = size * 0.5, size * 0.49
    if currency_id == "gold":
        canvas.ellipse(cx, cy, size * 0.28, size * 0.28, (214, 168, 62))
        canvas.ellipse(cx, cy - size * 0.02, size * 0.23, size * 0.23, (246, 214, 116))
        canvas.capsule(cx, cy - size * 0.13, cx, cy + size * 0.13, size * 0.055, (176, 128, 40))
    elif currency_id == "gem":
        canvas.poly([(cx, cy - size * 0.29), (cx + size * 0.26, cy - size * 0.04),
                     (cx, cy + size * 0.30), (cx - size * 0.26, cy - size * 0.04)],
                    (86, 186, 214))
        canvas.poly([(cx, cy - size * 0.29), (cx + size * 0.26, cy - size * 0.04), (cx, cy)],
                    (168, 232, 246))
    elif currency_id == "food":
        for dx, dy, r in ((-0.10, 0.04, 0.13), (0.10, 0.04, 0.13), (0.0, -0.10, 0.14)):
            canvas.ellipse(cx + dx * size, cy + dy * size, size * r, size * r, (206, 128, 78))
            canvas.ellipse(cx + dx * size - size * 0.03, cy + dy * size - size * 0.04,
                           size * r * 0.4, size * r * 0.35, (240, 186, 138))
    else:  # shard
        for dx, dy, s in ((-0.09, 0.06, 0.9), (0.10, 0.02, 1.0), (0.0, -0.12, 0.7)):
            h = size * 0.22 * s
            canvas.poly([(cx + dx * size, cy + dy * size - h),
                         (cx + dx * size + size * 0.09 * s, cy + dy * size),
                         (cx + dx * size, cy + dy * size + h * 0.6),
                         (cx + dx * size - size * 0.09 * s, cy + dy * size)],
                        (168, 132, 224))


# ---------------------------------------------------------------- 배경

def background(canvas, pal, fog, w=BG_W, h=BG_H):
    """지역 배경이다. 안개 색은 표의 `fog_color` 를 그대로 쓴다."""
    horizon = h * 0.62
    canvas.gradient_rect(0, 0, w, horizon, shade(pal["deep"], 0.10), fog)
    canvas.gradient_rect(0, horizon, w, h, shade(pal["base"], -0.35),
                         shade(pal["deep"], -0.20))

    canvas.ellipse(w * 0.74, h * 0.22, h * 0.11, h * 0.11, shade(pal["light"], 0.45), alpha=0.85)

    rng = Rng(fog[0] * 65536 + fog[1] * 256 + fog[2])
    for layer in range(3):
        depth = layer / 2.0
        color = mix(mix(pal["deep"], fog, 0.55 - depth * 0.45), pal["base"], depth * 0.35)
        top = horizon - h * (0.20 - layer * 0.05)
        step = w / 7.0
        pts = [(0.0, h)]
        for i in range(8):
            pts.append((i * step, top + rng.float(-0.06, 0.06) * h + layer * h * 0.05))
        pts.append((w, h))
        canvas.poly(pts, color)

    # 앞쪽 실루엣. 속성이 형태를 정한다 — 잎새와 불꽃은 나무, 물결과 어둠은 바위,
    # 번개는 뾰족한 기둥이다.
    ink = shade(pal["deep"], -0.55)
    shape = "tree" if pal["base"][1] > pal["base"][2] else "rock"
    for i in range(7):
        x = rng.float(-0.04, 1.04) * w
        th = rng.float(0.12, 0.26) * h
        if shape == "tree":
            canvas.capsule(x, h, x, h - th * 0.62, rng.float(0.008, 0.014) * w, ink)
            crown = rng.float(0.048, 0.082) * w
            for k in range(3):
                cy = h - th * (0.52 + k * 0.24)
                canvas.poly([(x - crown * (1.0 - k * 0.22), cy),
                             (x, cy - th * 0.34),
                             (x + crown * (1.0 - k * 0.22), cy)], ink)
        else:
            base_w = rng.float(0.045, 0.085) * w
            canvas.poly([(x - base_w, h), (x - base_w * 0.55, h - th * 0.86),
                         (x + base_w * 0.12, h - th), (x + base_w * 0.78, h - th * 0.50),
                         (x + base_w, h)], ink)


# ---------------------------------------------------------------- 만들기

def unity_meta(path, guid_seed, is_sprite=True):
    """유니티가 애셋마다 요구하는 `.meta` 이다.

    **직접 쓴다.** 에디터가 없는 자리에서 그림을 만들고 커밋해야 하는데, `.meta` 가 없으면
    유니티가 열릴 때마다 새 `guid` 를 붙여 diff 가 매번 달라진다. `guid` 를 파일 이름에서
    정하면 다시 만들어도 같은 값이 나온다.
    """
    rng = Rng("wildling-meta:" + guid_seed)
    guid = "".join("%08x" % rng.next() for _ in range(4))
    filter_mode = 1
    text = (
        "fileFormatVersion: 2\n"
        "guid: %s\n"
        "TextureImporter:\n"
        "  internalIDToNameTable: []\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 13\n"
        "  mipmaps:\n"
        "    mipMapMode: 0\n"
        "    enableMipMap: 0\n"
        "  bumpmap:\n"
        "    convertToNormalMap: 0\n"
        "  isReadable: 0\n"
        "  streamingMipmaps: 0\n"
        "  alphaTestReferenceValue: 0.5\n"
        "  alphaIsTransparency: 1\n"
        "  spriteMode: %d\n"
        "  spritePixelsToUnits: 100\n"
        "  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n"
        "  spritePivot: {x: 0.5, y: 0.5}\n"
        "  spriteGenerateFallbackPhysicsShape: 0\n"
        "  textureType: %d\n"
        "  textureShape: 1\n"
        "  filterMode: %d\n"
        "  wrapU: 1\n"
        "  wrapV: 1\n"
        "  sRGBTexture: 1\n"
        "  textureCompression: 0\n"
        "  maxTextureSize: 2048\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    ) % (guid, 1 if is_sprite else 0, 8 if is_sprite else 0, filter_mode)
    io.open(path + ".meta", "w", encoding="utf-8", newline="\n").write(text)


def folder_meta(path, guid_seed):
    """폴더에도 `.meta` 가 필요하다. 없으면 유니티가 열릴 때마다 새로 만든다."""
    rng = Rng("wildling-folder:" + guid_seed)
    guid = "".join("%08x" % rng.next() for _ in range(4))
    io.open(path + ".meta", "w", encoding="utf-8", newline="\n").write(
        "fileFormatVersion: 2\n"
        "guid: %s\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n" % guid)


def emit(canvas, folder, name):
    path = os.path.join(folder, name + ".png")
    canvas.write_png(path)
    unity_meta(path, name)
    return path


def build_monsters(icon_dir, made):
    for row in records("Monster.tsv"):
        canvas = Canvas(ICON, ICON)
        pal = palette(row["element"])
        plate(canvas, pal, row["grade"])
        creature(canvas, row["species_id"], pal, row["role"], int(row["stage"]))
        vignette(canvas)
        made.append(emit(canvas, icon_dir, row["icon"]))


def build_skills(icon_dir, made):
    effects = {}
    for row in records("SkillEffect.tsv"):
        effects.setdefault(row["skill_id"], []).append(row["effect.$type"])
        for more in row.get("__more", []):
            effects.setdefault(row["skill_id"], []).append(more["effect.$type"])

    for row in records("Skill.tsv"):
        canvas = Canvas(ICON, ICON)
        pal = palette(row["element"])
        # **스킬에는 등급 컬럼이 없다.** 테두리를 아무렇게나 정하는 대신 재사용 대기로
        # 정한다 — 무거운 스킬일수록 테두리가 높다. 표에 있는 값이므로 어긋나면 보인다.
        cooldown = int(row["cooldown"] or 0)
        grade = ("Common", "Rare", "Rare", "Epic", "Epic",
                 "Legendary", "Legendary", "Mythic", "Mythic", "Mythic")[min(9, cooldown)]
        plate(canvas, pal, grade)
        skill_glyph(canvas, pal, row["target_scope"], effects.get(row["skill_id"], []))
        vignette(canvas)
        made.append(emit(canvas, icon_dir, row["icon"]))


def build_items(icon_dir, made, region_element):
    for row in records("Item.tsv"):
        icon = row["icon"]
        pal, kind, variant = NEUTRAL, "ticket", 0
        stem = icon[len("it_"):]

        for suffix in ("core", "dust", "resin", "relic", "sigil"):
            if stem.endswith("_" + suffix):
                kind = suffix
                pal = palette(region_element.get(stem[:-len(suffix) - 1], ""))
                break
        else:
            if stem.startswith("awaken_"):
                rest = stem[len("awaken_"):]
                element, _, tier = rest.rpartition("_")
                kind, variant = "awaken", int(tier) if tier.isdigit() else 1
                pal = palette(element.capitalize())
            elif stem.startswith(("food_", "boost_", "ticket_")):
                kind, variant = stem.split("_", 1)[0], stem
                # **같은 분류가 수십 종인데 속성 컬럼이 없다.** 이름으로 색을 가른다 —
                # 표에서 나온 값은 아니지만 이름이 바뀌면 색도 따라 바뀐다.
                order = ("Leaf", "Tide", "Arc", "Flame", "Umbra")
                pal = ELEMENT[order[Rng("item:" + stem).next() % len(order)]]

        canvas = Canvas(ICON, ICON)
        plate(canvas, pal, row["grade"])
        item_glyph(canvas, pal, kind, variant)
        vignette(canvas)
        made.append(emit(canvas, icon_dir, icon))


def build_currencies(icon_dir, made):
    for row in records("Currency.tsv"):
        canvas = Canvas(ICON, ICON)
        pal = {"gold": ELEMENT["Arc"], "gem": ELEMENT["Tide"],
               "food": ELEMENT["Leaf"], "shard": ELEMENT["Umbra"]}[row["currency_id"]]
        plate(canvas, pal, "Rare")
        currency_glyph(canvas, row["currency_id"])
        vignette(canvas)
        made.append(emit(canvas, icon_dir, row["icon"]))


def build_backgrounds(model_dir, made, regions):
    for row in regions:
        canvas = Canvas(BG_W, BG_H)
        background(canvas, palette(row["theme_element"]), hex_color(row["fog_color"]))
        made.append(emit(canvas, model_dir, row["background"]))


def main():
    icon_dir = os.path.join(ART, "icon")
    model_dir = os.path.join(ART, "model")
    os.makedirs(icon_dir, exist_ok=True)
    os.makedirs(model_dir, exist_ok=True)

    folder_meta(os.path.dirname(ART), "Resources")
    folder_meta(ART, "Resources/art")
    folder_meta(icon_dir, "Resources/art/icon")
    folder_meta(model_dir, "Resources/art/model")

    regions = records("Region.tsv")
    region_element = {r["region_id"]: r["theme_element"] for r in regions}

    made = []
    build_monsters(icon_dir, made)
    print("와일드링 %d장" % len(made))

    mark = len(made)
    build_skills(icon_dir, made)
    print("스킬 %d장" % (len(made) - mark))

    mark = len(made)
    build_items(icon_dir, made, region_element)
    print("아이템 %d장" % (len(made) - mark))

    mark = len(made)
    build_currencies(icon_dir, made)
    print("재화 %d장" % (len(made) - mark))

    mark = len(made)
    build_backgrounds(model_dir, made, regions)
    print("배경 %d장" % (len(made) - mark))

    # 표가 더는 가리키지 않는 그림을 지운다. 이름이 표에서 나오므로 여기서만 알 수 있다.
    kept = set(os.path.abspath(p) for p in made)
    removed = 0
    for folder in (icon_dir, model_dir):
        for name in os.listdir(folder):
            if not name.endswith(".png"):
                continue
            path = os.path.abspath(os.path.join(folder, name))
            if path not in kept:
                os.remove(path)
                if os.path.exists(path + ".meta"):
                    os.remove(path + ".meta")
                removed += 1

    print("모두 %d장. 표가 가리키지 않아 지운 것 %d장." % (len(made), removed))


if __name__ == "__main__":
    main()
