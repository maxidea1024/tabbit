# -*- coding: utf-8 -*-
"""화면의 껍데기를 만든다 — 9분할 패널·버튼·테두리·빛무리.

아이콘과 같은 규칙입니다. **외부 애셋을 쓰지 않는 이유가 있습니다** — 애셋스토어 패키지는
로그인이 필요하고 재배포가 막혀 저장소에 담을 수 없어서, 클론한 사람이 빌드할 수 없게 됩니다.

9분할은 `.meta` 의 `spriteBorder` 가 정합니다. 그래서 버튼 하나를 어떤 크기로 늘려도 모서리가
망가지지 않습니다.
"""
import io
import math
import os

from raster import Canvas, mix, shade

UI = 64          # 9분할 조각의 한 변
BORDER = 20      # 늘어나지 않는 가장자리


def rounded_plate(canvas, top, bottom, edge, radius, inset=2.0, size=UI):
    """위아래로 색이 변하는 둥근 판이다. 가장자리에 선이 있다."""
    canvas.rect(inset - 2, inset - 2, size - inset + 2, size - inset + 2,
                edge, radius=radius + 2)
    canvas.gradient_rect(inset, inset, size - inset, size - inset,
                         top, bottom, radius=radius)
    # 위쪽 안쪽에 밝은 선 하나. 판이 서 있는 것처럼 보이게 합니다.
    canvas.gradient_rect(inset + 2, inset + 2, size - inset - 2, inset + 8,
                         shade(top, 0.30), top, radius=radius, alpha=0.55)


def build(art_dir, emit, folder_meta, unity_meta):
    """`Resources/art/ui/` 를 만든다. 낸 것은 만든 파일의 목록이다."""
    ui_dir = os.path.join(art_dir, "ui")
    os.makedirs(ui_dir, exist_ok=True)
    folder_meta(ui_dir, "Resources/art/ui")

    made = []

    def nine(name, draw):
        canvas = Canvas(UI, UI)
        draw(canvas)
        path = os.path.join(ui_dir, name + ".png")
        canvas.write_png(path)
        unity_meta(path, name, border=BORDER)
        made.append(path)

    def flat(name, width, height, draw):
        canvas = Canvas(width, height)
        draw(canvas)
        path = os.path.join(ui_dir, name + ".png")
        canvas.write_png(path)
        unity_meta(path, name)
        made.append(path)

    # ------------------------------------------------------------ 판
    nine("panel", lambda c: rounded_plate(
        c, (52, 55, 70), (34, 36, 47), (22, 23, 31), 14))
    nine("panel_soft", lambda c: rounded_plate(
        c, (66, 70, 88), (44, 47, 60), (26, 28, 37), 14))
    nine("panel_sunk", lambda c: rounded_plate(
        c, (24, 25, 33), (32, 34, 44), (18, 19, 25), 12))

    # ------------------------------------------------------------ 버튼
    nine("button", lambda c: rounded_plate(
        c, (76, 81, 101), (48, 51, 66), (28, 30, 39), 14))
    nine("button_accent", lambda c: rounded_plate(
        c, (126, 222, 178), (66, 168, 124), (30, 84, 62), 14))
    nine("button_warn", lambda c: rounded_plate(
        c, (232, 132, 118), (176, 78, 68), (86, 34, 30), 14))

    # ------------------------------------------------------------ 등급 테두리
    grades = {
        "common": (154, 165, 177),
        "rare": (76, 141, 224),
        "epic": (166, 92, 224),
        "legendary": (224, 166, 60),
        "mythic": (224, 92, 122),
    }
    for name, color in grades.items():
        def draw(c, color=color):
            # 안쪽이 비어 있는 테두리입니다. 아이콘 위에 얹습니다.
            c.rect(0, 0, UI, UI, shade(color, -0.35), radius=14)
            c.rect(3, 3, UI - 3, UI - 3, shade(color, 0.25), radius=12)
            c.rect(6, 6, UI - 6, UI - 6, (0, 0, 0), radius=10, alpha=0.0)
            # 가운데를 지웁니다 — 알파를 직접 0으로 씁니다.
            for y in range(6 * 3, (UI - 6) * 3):
                for x in range(6 * 3, (UI - 6) * 3):
                    i = (y * c.w + x) * 4
                    c.buf[i + 3] = 0
        nine("frame_" + name, draw)

    # ------------------------------------------------------------ 막대
    nine("bar_back", lambda c: rounded_plate(
        c, (26, 27, 36), (34, 36, 46), (18, 19, 25), 10, inset=0))
    nine("bar_fill", lambda c: rounded_plate(
        c, (150, 235, 190), (86, 190, 142), (40, 110, 82), 10, inset=0))

    # ------------------------------------------------------------ 빛무리
    def glow(c):
        cx = cy = 128
        for i in range(48, 0, -1):
            t = i / 48.0
            r = 126 * t
            c.ellipse(cx, cy, r, r, (255, 255, 255), alpha=(1.0 - t) ** 2 * 0.16)
    flat("glow", 256, 256, glow)

    # 네 갈래 별. 치명타와 새 발견에 씁니다.
    def spark(c):
        cx = cy = 64
        for arm in range(4):
            a = arm * math.pi / 2
            tip = 62
            wide = 9
            c.poly([(cx + math.cos(a) * tip, cy + math.sin(a) * tip),
                    (cx + math.cos(a + math.pi / 2) * wide,
                     cy + math.sin(a + math.pi / 2) * wide),
                    (cx - math.cos(a) * 6, cy - math.sin(a) * 6),
                    (cx + math.cos(a - math.pi / 2) * wide,
                     cy + math.sin(a - math.pi / 2) * wide)],
                   (255, 255, 255))
        c.ellipse(cx, cy, 13, 13, (255, 255, 255))
    flat("spark", 128, 128, spark)

    # ------------------------------------------------------------ 훑고 지나가는 빛
    def shine(c):
        for x in range(c.w):
            t = x / float(c.w)
            # 가운데가 밝고 양끝이 투명한 띠입니다.
            a = math.sin(t * math.pi) ** 3
            c.rect(x / 3.0, 0, (x + 1) / 3.0, c.height, (255, 255, 255), alpha=a * 0.85)
    flat("shine", 96, 8, shine)

    # ------------------------------------------------------------ 위쪽이 밝은 덮개
    def sheen(c):
        c.gradient_rect(0, 0, c.width, c.height, (255, 255, 255), (255, 255, 255),
                        radius=0, alpha=0.0)
        for y in range(c.h):
            t = y / float(c.h)
            c.rect(0, y / 3.0, c.width, (y + 1) / 3.0, (255, 255, 255),
                   alpha=max(0.0, 0.22 * (1.0 - t * 2.2)))
    flat("sheen", 8, 64, sheen)

    print("껍데기 %d장" % len(made))
    return made
