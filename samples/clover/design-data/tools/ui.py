# -*- coding: utf-8 -*-
"""화면의 부품을 그림으로 굽습니다.

**런타임에 도형을 그리지 않습니다.** 판·단추·칸·자리·키캡·게이지는 모양이 고정이므로
9분할 그림으로 굽고 스프라이트로 놓습니다. 실행 중에 남는 비용이 0 입니다.

**회색조로 한 벌만 굽습니다.** 갈래와 상태는 `tint` 로 만듭니다. 물들이기가 색을 곱하는
것이므로 어두운 턱은 어둡게 남고 밝은 변이 그 색을 받습니다 — 단추 갈래 다섯과 상태 넷이
그림 한 장에서 나옵니다.

**두 배로 굽고 절반으로 놓습니다.** 1대1로 구우면 2배 밀도 화면에서 늘려 쓰게 되어
가장자리가 흐려집니다. `web/src/ui/chrome.ts` 의 `SCALE` 이 이미 그 값입니다.

**손으로 다시 그리지 않습니다.** 규격은 [디자인 언어](../../doc/ui/language.md) 에 있고,
고정 부품과 타이틀 그림은 `design-data/art/ui` 의 원화에서 내립니다. CSS 정의는 원화가
아직 없는 개발용 부품의 대체 경로일 뿐이며 제품 화면의 그림 자리를 대신하지 않습니다.

    python samples/clover/design-data/tools/ui.py
    python samples/clover/design-data/tools/ui.py --check   # 다시 굽지 않고 대조만

나온 것은 `web/public/ui/` 로 가고, 규격표는 `web/src/ui/atlas.ts` 로 갑니다.
"""

import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)
OUT = os.path.join(SAMPLE, 'web', 'public', 'ui')
TABLE = os.path.join(SAMPLE, 'web', 'src', 'ui', 'atlas.ts')
SOURCE = os.path.join(DESIGN, 'art', 'ui')

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

# 굽는 배율입니다. 놓을 때 절반으로 줄입니다.
BAKE = 2

# 단추 높이의 계단 넷입니다. 그 사이 값은 없습니다 — 문서의 「높이의 계단 넷」 과 같습니다.
#
#   이름   높이  글자  컷
RUNGS = [('button-sm', 36, 12, 8),
         ('button', 48, 24, 10),
         ('button-lg', 60, 24, 12),
         ('button-xl', 72, 36, 14)]

# 판의 컷입니다. **하나로 못박습니다** — 판마다 다르면 같은 판으로 보이지 않습니다.
PLATE_CUT = 20


def px(v):
    return '%dpx' % (v * BAKE)


# ── 조각의 정의 ────────────────────────────────────────────────────────────
#
# 각 조각은 (이름, 가로, 세로, 9분할 여백, CSS) 입니다. 여백은 1배 기준이고
# 왼쪽·오른쪽·위·아래 순서입니다. 0 이면 그 축으로 늘리지 않습니다.

def plate():
    """판 — 세로 채움, 위 변의 빛, 오른쪽 아래의 잘린 귀.

    빛은 위에서 옵니다. 그것이 판·칸·단추의 모든 명암을 정합니다.
    """
    c = PLATE_CUT
    # **두 점짜리 선형입니다.** 꺾이는 점을 두면 9분할로 늘렸을 때 그 꺾임이 사라집니다.
    # **꼭대기가 흰색입니다.** 물들이는 색이 곧 판의 윗변이고 아래로 내려가며 조금 어두워집니다.
    return ('background: linear-gradient(180deg, #ffffff 0%%, #a0a0a0 100%%);'
            ' clip-path: polygon(0 0, 100%% 0, 100%% calc(100%% - %s), calc(100%% - %s) 100%%,'
            ' 0 100%%);' % (px(c), px(c)))


def well():
    """칸 — 판 안으로 눌린 자리. 위 안쪽의 그늘과 아래의 밝은 줄."""
    # **몸통이 흰색입니다.** 물들이는 색이 곧 칸의 색이고, 위 안쪽의 그늘만 어둡습니다.
    return ('background: linear-gradient(180deg, #6e6e6e 0%%, #ffffff 100%%);'
            ' box-shadow: inset 0 %s 0 #4a4a4a, inset 0 %s %s #5a5a5a;'
            % (px(1), px(2), px(5)))


def tray():
    """물건 자리 — 칸을 하나씩 그리지 않습니다. 고정된 영역 하나입니다."""
    # **테를 두르지 않습니다.** 윗변의 한 줄만 안쪽으로 있습니다.
    return ('background: linear-gradient(180deg, #ffffff 0%%, #b4b4b4 100%%);'
            ' box-shadow: inset 0 %s 0 #ffffff;' % px(1))


def head():
    """머리 판 — 판의 폭을 다 씁니다. 밑줄의 번짐까지 한 장에 있습니다."""
    return ('background: linear-gradient(180deg, #ffffff 0%%, #8c8c8c 100%%);'
            ' box-shadow: inset 0 -%s 0 #ffffff;' % px(2))


def button(h, cut):
    """단추 — 실루엣을 따라가는 테, 아주 옅은 얼굴의 빛, 아래의 턱.

    테를 `inset` 으로 그리지 않습니다. 잘린 모서리에서 그 테가 함께 잘려 나가
    컷의 대칭 부분만 선이 없어집니다. 실루엣을 따라가는 그림자 넷으로 그립니다.

    안쪽에 사각 테를 두르지 않습니다. 형태는 얼굴의 빛과 아래의 턱이 냅니다.
    얼굴의 빛을 세게 주면 가운데가 불룩한 알약이 됩니다.
    """
    return ('background: linear-gradient(180deg, #ffffff 0%%, #c4c4c4 100%%);'
            ' clip-path: polygon(%s 0, 100%% 0, 100%% calc(100%% - %s),'
            ' calc(100%% - %s) 100%%, 0 100%%, 0 %s);'
            % (px(cut), px(cut), px(cut), px(cut)))


def keycap():
    """ESC 키캡 — 닫기 단추를 따로 두지 않습니다. 이것이 그것입니다."""
    return button(36, 8)


def gauge_groove():
    return ('background: #ffffff; box-shadow: inset 0 %s %s #6e6e6e,'
            ' inset 0 -%s 0 #ffffff;' % (px(1), px(3), px(1)))


def gauge_fill():
    return 'background: linear-gradient(180deg, #ffffff 0%, #9b9b9b 100%);'


def glow_edge():
    """테두리의 빛 — 왼쪽에서 밝게 시작해 오른쪽으로 사라집니다.

    아무 변에나 두면 화면이 번들거립니다. 문서가 다섯 자리를 정해 두었습니다.
    """
    return ('background: linear-gradient(90deg, #ffffff 0%, #6e6e6e 62%, transparent 100%);')


PIECES = [
    # 왼쪽 HUD 전용 외피. 글과 값은 모두 런타임에 놓고 원화에는 재질과 큰 구획만 있습니다.
    ('hud-shell', 264, 756, (24, 24, 72, 64), plate()),
    # 블라인드 정보 전용 안쪽 판. 머리·점수·보상 자리가 한 원화에 이어져 있습니다.
    ('blind-badge', 264, 212, (28, 28, 52, 34), well()),
    ('plate', 96, 96, (24, 24, 6, PLATE_CUT + 4), plate()),
    ('well', 96, 48, (18, 18, 12, 12), well()),
    ('tray', 96, 96, (14, 14, 8, 8), tray()),
    ('head', 96, 64, (14, 14, 8, 10), head()),
    # 블라인드 카드의 제목 띠. 공용 머리 판의 중앙 장식은 짧은 카드 제목을 가리므로,
    # 같은 재질이되 글 뒤가 비어 있는 전용 조각을 둡니다.
    ('blind-head', 96, 64, (14, 14, 8, 10), head()),
    ('keycap', 64, 36, (12, 12, 10, 14), keycap()),
    ('gauge', 48, 12, (6, 6, 0, 0), gauge_groove()),
    ('gauge-fill', 48, 8, (6, 6, 0, 0), gauge_fill()),
    ('glow-edge', 64, 2, (0, 0, 0, 0), glow_edge()),
]
for name, h, _font, cut in RUNGS:
    # 버튼 원화는 실제 화면에서 가장 흔한 3:1 비율로 그립니다. 9분할의 가운데가 줄어들 수
    # 있으므로 좁은 단추도 그대로 쓸 수 있고, 넓은 단추에서는 재질의 결이 찌그러지지 않습니다.
    PIECES.append((name, h * 3, h, (cut + 12, cut + 12, 10, 14), button(h, cut)))


# 단추의 겉면 밖으로 나가는 그림자입니다. 클립 밖이라 여백을 둡니다.
PAD = 14


def sheet():
    """한 장의 HTML 로 모아 굽습니다. 조각마다 창을 여는 것보다 훨씬 빠릅니다."""
    out = ['<!doctype html><meta charset="utf-8"><style>',
           'html,body{margin:0;background:transparent}',
           '.p{position:absolute}',
           '</style>']
    y = 0
    spots = {}
    for name, w, h, _slice, css in PIECES:
        lip = PAD if name.startswith('button') or name == 'keycap' else 0
        fx, fy = PAD * BAKE, (y + PAD) * BAKE
        out.append('<div class="p" id="%s" style="left:%s;top:%s;width:%s;height:%s;%s%s"></div>'
                   % (name, px(PAD), px(y + PAD), px(w), px(h), css,
                      edges() if lip else ''))
        spots[name] = (fx, fy, w * BAKE, h * BAKE)
        y += h + PAD * 2
    return chr(10).join(out), spots


def edges():
    """잘린 실루엣을 따라가는 테와 아래의 턱입니다.

    위가 밝고 아래가 어둡습니다. 회색조로 굽되 테는 물들이기를 덜 받아야 하므로
    흰 쪽과 검은 쪽 양끝을 씁니다.
    """
    return (' filter:'
            ' drop-shadow(0 -%s 0 #ffffff)'
            ' drop-shadow(-%s 0 0 #ededed)'
            ' drop-shadow(%s 0 0 #3c3c3c)'
            ' drop-shadow(0 %s 0 #3c3c3c)'
            ' drop-shadow(0 %s 0 #2a2a2a);'
            % (px(1), px(1), px(1), px(1), px(3)))


# ── 원화에서 UI 스프라이트 굽기 ──────────────────────────────────────────
#
# CSS 도형은 구조를 세우는 동안의 대체물이었습니다. 최종 부품은 `design-data/art/ui` 의
# 원화에서 굽습니다. 원화는 높은 밝기의 회색조이고 실행 중 `tint` 를 받아 겉면을 따라갑니다.
# 배경의 순수한 마젠타는 생성 도구가 투명 알파 대신 남기는 크로마키이고, 아래 한 자리에서만
# 걷습니다. 화면에 보이는 모양을 여기서 새로 그리지 않습니다.

SOURCE_FILES = {
    'plate': 'plate-source.png',
    'well': 'well-source.png',
    'button': 'button-source.png',
    'hud-shell': 'hud-shell-v2-source.png',
    'blind-badge': 'blind-badge-source.png',
}

TITLE_FILES = {
    'title-start': 'title-start-source.png',
    'title-collection': 'title-collection-source.png',
    'title-leaderboard': 'title-leaderboard-source.png',
}

RUN_FILES = {
    'run-new': 'run-new-source.png',
    'run-resume': 'run-resume-source.png',
    'run-challenge': 'run-challenge-source.png',
}

ILLUSTRATION_FILES = {**TITLE_FILES, **RUN_FILES}

def source_image(name):
    """마젠타 바탕을 실제 알파로 바꾼 회색조 원화를 읽습니다.

    생성된 가장자리 픽셀은 전경 회색과 마젠타가 섞여 있습니다. 배경이 (255, 0, 255), 전경이
    회색이라고 두면 `alpha = 1 - (red - green) / 255` 이므로 가장자리의 반투명도와 본래
    회색을 함께 되찾을 수 있습니다. 단순 색상 문턱보다 분홍 테가 남지 않습니다.
    """
    from PIL import Image

    path = os.path.join(SOURCE, SOURCE_FILES[name])
    raw = Image.open(path).convert('RGBA')
    # 생성기가 실제 알파를 준 원화는 그 알파를 보존합니다. 마젠타 복원식에 다시 넣으면
    # 투명 픽셀의 검정 RGB가 불투명한 검정 바탕이 됩니다.
    if raw.getchannel('A').getextrema()[0] < 255:
        gray = raw.convert('L')
        keyed = Image.merge('RGBA', (gray, gray, gray, raw.getchannel('A')))
        bbox = keyed.getchannel('A').point(lambda a: 255 if a > 3 else 0).getbbox()
        if bbox is None:
            raise RuntimeError('UI 원화의 전경을 찾지 못했습니다: %s' % path)
        return keyed.crop(bbox)

    image = raw.convert('RGB')
    out = []
    for r, g, b in image.get_flattened_data():
        # R/B 둘 중 하나에 압축 오차가 있어도 한쪽만으로 가장자리가 들쭉날쭉하지 않게 합니다.
        spill = max(0, ((r + b) // 2) - g)
        alpha = max(0, min(255, 255 - spill))
        if alpha <= 3:
            out.append((255, 255, 255, 0))
            continue
        gray = max(0, min(255, round(g * 255 / alpha)))
        out.append((gray, gray, gray, alpha))
    keyed = Image.new('RGBA', image.size)
    keyed.putdata(out)
    alpha = keyed.getchannel('A')
    bbox = alpha.point(lambda a: 255 if a > 10 else 0).getbbox()
    if bbox is None:
        raise RuntimeError('UI 원화의 전경을 찾지 못했습니다: %s' % path)
    return keyed.crop(bbox)


def crop_ratio(image, area):
    """원화 안의 정규화된 구역을 자릅니다. 재질의 면을 빌릴 때만 씁니다."""
    x0, y0, x1, y1 = area
    return image.crop((round(image.width * x0), round(image.height * y0),
                       round(image.width * x1), round(image.height * y1)))


def fit_source(image, width, height, pad=0):
    """원화 한 장을 그 부품의 2배 크기로 맞추고 바깥 여백을 둡니다."""
    from PIL import Image, ImageOps

    target = (width * BAKE, height * BAKE)
    fitted = ImageOps.fit(image, target, method=Image.Resampling.LANCZOS,
                          centering=(0.5, 0.5))
    if pad <= 0:
        return fitted
    canvas = Image.new('RGBA', ((width + pad * 2) * BAKE, (height + pad * 2) * BAKE),
                       (255, 255, 255, 0))
    canvas.alpha_composite(fitted, (pad * BAKE, pad * BAKE))
    return canvas


def bake_sources():
    """그림 원화가 맡는 부품을 CSS 대체물 위에 덮어 씁니다."""
    if not os.path.isdir(SOURCE):
        return []

    made = []
    plate_art = source_image('plate')
    well_art = source_image('well')
    button_art = source_image('button')
    shell_art = source_image('hud-shell')
    badge_art = source_image('blind-badge')

    fit_source(shell_art, 264, 756).save(os.path.join(OUT, 'hud-shell.png'), optimize=True)
    made.append('hud-shell')
    fit_source(badge_art, 264, 212).save(os.path.join(OUT, 'blind-badge.png'), optimize=True)
    made.append('blind-badge')

    # 판과 값 칸은 원화의 전체 실루엣을 그대로 씁니다.
    fit_source(plate_art, 96, 96).save(os.path.join(OUT, 'plate.png'), optimize=True)
    fit_source(well_art, 96, 48).save(os.path.join(OUT, 'well.png'), optimize=True)
    made.extend(['plate', 'well'])

    # 물건 자리는 테가 없습니다. 판 원화의 조용한 가운데 재질만 빌려 전체 영역에 놓습니다.
    tray_art = crop_ratio(plate_art, (0.22, 0.24, 0.78, 0.76))
    fit_source(tray_art, 96, 96).save(os.path.join(OUT, 'tray.png'), optimize=True)
    made.append('tray')

    # 머리 판은 왼쪽 HUD 원화의 실제 머리 재질입니다. 내부의 글과 값은 런타임이 놓습니다.
    head_art = crop_ratio(shell_art, (0.03, 0.01, 0.97, 0.105))
    fit_source(head_art, 96, 64).save(os.path.join(OUT, 'head.png'), optimize=True)
    made.append('head')

    # 블라인드 카드에는 중앙 문양이 없는 눌린 판의 종이·금속 결을 씁니다. 이것은 보이는
    # 도형을 새로 그리는 대체물이 아니라 `well` 원화의 실제 재질을 전용 규격으로 자른
    # 것입니다. 글 뒤가 비어 있어 세 언어의 긴 제목도 장식과 충돌하지 않습니다.
    fit_source(well_art, 96, 64).save(os.path.join(OUT, 'blind-head.png'), optimize=True)
    made.append('blind-head')

    # 버튼 네 계단은 같은 원화에서 굽습니다. 모양과 재질은 같고 높이만 계단을 따릅니다.
    for name, h, _font, _cut in RUNGS:
        w = h * 3
        fit_source(button_art, w, h, PAD).save(os.path.join(OUT, name + '.png'), optimize=True)
        made.append(name)

    # 키캡도 같은 누름 재질을 쓰되 작은 단추의 비율로 접습니다.
    fit_source(button_art, 64, 36, PAD).save(os.path.join(OUT, 'keycap.png'), optimize=True)
    made.append('keycap')

    # 게이지는 값 칸 원화의 파인 면을 그대로 축소합니다. 채움은 판의 조용한 면입니다.
    groove = crop_ratio(well_art, (0.04, 0.12, 0.96, 0.88))
    fit_source(groove, 48, 12).save(os.path.join(OUT, 'gauge.png'), optimize=True)
    fill = crop_ratio(plate_art, (0.28, 0.28, 0.72, 0.72))
    fit_source(fill, 48, 8).save(os.path.join(OUT, 'gauge-fill.png'), optimize=True)
    made.extend(['gauge', 'gauge-fill'])

    # 머리의 밝은 아래 변을 경계의 빛으로 씁니다. 색과 가산 합성은 런타임이 정합니다.
    edge = crop_ratio(shell_art, (0.08, 0.095, 0.92, 0.103))
    fit_source(edge, 64, 2).save(os.path.join(OUT, 'glow-edge.png'), optimize=True)
    made.append('glow-edge')
    return made


def bake_illustrations():
    """타이틀과 런 시작 판의 실제 삽화를 화면용 WebP로 내립니다.

    원본은 보존하고 런타임에는 그림 자리의 정확한 2배 크기만 둡니다. 같은 도구를 다시
    돌려도 같은 결과가 나와야 원화를 고친 뒤 옛 그림이 남지 않습니다.
    """
    from PIL import Image, ImageOps

    made = []
    for name, filename in ILLUSTRATION_FILES.items():
        source = os.path.join(SOURCE, filename)
        if not os.path.exists(source):
            continue
        image = Image.open(source).convert('RGB')
        fitted = ImageOps.fit(image, (704, 472), method=Image.Resampling.LANCZOS,
                              centering=(0.5, 0.5))
        fitted.save(os.path.join(OUT, name + '.webp'), 'WEBP', quality=88, method=6)
        made.append(name)
    return made


# ── 카드의 뜯긴 가장자리 ──────────────────────────────────────────────────
#
# **둥근 모서리 대신 뜯긴 변입니다.** 가위로 자른 네모는 멋이 없고 둥근 모서리는 웹의
# 문법입니다 — 손으로 뜯은 종이가 이 화면의 카드꼴입니다.
#
# **변을 픽셀마다 흔들면 가시가 됩니다.** 13픽셀마다 한 점만 뽑아 코사인으로 잇고,
# 이빠짐 셋을 따로 냅니다. 넷을 돌려 쓰므로 카드가 몇 장이든 비용이 같습니다.
CARD_W, CARD_H = 88, 124
TORN_SCALE = 6          # 굽는 배수. 매끈하게 그려 놓고 2배로 줄입니다.
TORN_AMP = 2.6          # 변이 흔들리는 폭 (1배 픽셀)
TORN_STEP = 9           # 흔들림을 뽑는 사이 (1배 픽셀)
TORN_NICKS = 5          # 이빠짐
TORN_COUNT = 4


def torn_wave(length_px, seed):
    """TORN_STEP 마다 한 점씩 뽑아 코사인으로 잇습니다."""
    import math
    import random
    rnd = random.Random(seed)
    n = max(3, int(length_px / (TORN_STEP * TORN_SCALE)) + 2)
    keys = [rnd.uniform(-1, 1) for _ in range(n)]
    out = []
    for i in range(length_px):
        t = i / (length_px - 1) * (n - 1)
        a = int(t)
        b = min(a + 1, n - 1)
        f = t - a
        f = (1 - math.cos(f * math.pi)) / 2
        out.append(keys[a] * (1 - f) + keys[b] * f)
    return out


def bake_torn(index, path):
    import random
    from PIL import Image, ImageDraw, ImageFilter

    seed = index * 101
    rnd = random.Random(seed)
    s6 = TORN_SCALE
    bw, bh = CARD_W * s6, CARD_H * s6
    pad = int(TORN_AMP * s6) + 6
    img = Image.new('L', (bw + pad * 2, bh + pad * 2), 0)
    d = ImageDraw.Draw(img)
    amp = TORN_AMP * s6
    top, bot = torn_wave(bw, seed + 1), torn_wave(bw, seed + 2)
    lef, rig = torn_wave(bh, seed + 3), torn_wave(bh, seed + 4)
    pts = []
    for x in range(bw):
        pts.append((pad + x, pad + top[x] * amp))
    for y in range(bh):
        pts.append((pad + bw - 1 + rig[y] * amp, pad + y))
    for x in range(bw - 1, -1, -1):
        pts.append((pad + x, pad + bh - 1 + bot[x] * amp))
    for y in range(bh - 1, -1, -1):
        pts.append((pad + lef[y] * amp, pad + y))
    d.polygon(pts, fill=255)

    # 이빠짐 — 변에서 안쪽으로 얕게 파고듭니다.
    for _ in range(TORN_NICKS):
        side = rnd.randrange(4)
        depth = rnd.uniform(0.7, 1.5) * s6
        wide = rnd.uniform(3.0, 6.5) * s6
        if side in (0, 2):
            x = rnd.uniform(wide, bw - wide)
            y = 0 if side == 0 else bh - 1
            dy = depth if side == 0 else -depth
            d.polygon([(pad + x - wide / 2, pad + y), (pad + x + wide / 2, pad + y),
                       (pad + x, pad + y + dy)], fill=0)
        else:
            y = rnd.uniform(wide, bh - wide)
            x = 0 if side == 1 else bw - 1
            dx = depth if side == 1 else -depth
            d.polygon([(pad + x, pad + y - wide / 2), (pad + x, pad + y + wide / 2),
                       (pad + x + dx, pad + y)], fill=0)

    img = img.filter(ImageFilter.GaussianBlur(s6 * 0.3))
    # 여백을 잘라 내고 2배로 줄입니다 — 놓는 쪽은 카드 크기로 늘려 씁니다.
    img = img.crop((pad, pad, pad + bw, pad + bh))
    small = img.resize((CARD_W * BAKE, CARD_H * BAKE), Image.LANCZOS)
    rgba = Image.new('RGBA', small.size, (255, 255, 255, 0))
    rgba.putalpha(small)
    rgba.save(path, optimize=True)


def bake():
    from playwright.sync_api import sync_playwright

    html, spots = sheet()
    total_h = sum(h + PAD * 2 for _n, _w, h, _s, _c in PIECES)
    os.makedirs(OUT, exist_ok=True)
    made = []
    with sync_playwright() as p:
        b = p.chromium.launch()
        pg = b.new_page(viewport={'width': 400 * BAKE, 'height': total_h * BAKE},
                        device_scale_factor=1)
        pg.set_content(html)
        for name, w, h, _slice, _css in PIECES:
            x, yy, ww, hh = spots[name]
            pad = PAD * BAKE if name.startswith('button') or name == 'keycap' else 0
            box = {'x': x - pad, 'y': yy - pad, 'width': ww + pad * 2, 'height': hh + pad * 2}
            path = os.path.join(OUT, name + '.png')
            pg.screenshot(path=path, clip=box, omit_background=True)
            made.append(name)
        b.close()
    for index in range(1, TORN_COUNT + 1):
        bake_torn(index, os.path.join(OUT, 'card-torn-%d.png' % index))
        made.append('card-torn-%d' % index)
    # CSS 로 세운 대체물 가운데 원화가 준비된 것은 마지막에 실제 스프라이트로 갈아 끼웁니다.
    # 이 순서라야 원화가 없는 부품도 화면을 깨뜨리지 않고, 원화가 있는 부품은 도형이 남지
    # 않습니다.
    for name in bake_sources():
        if name not in made:
            made.append(name)
    made.extend(bake_illustrations())
    return made


def table():
    """규격표입니다. 코드가 여백을 다시 적지 않게 합니다."""
    rows = []
    for name, w, h, sl, _css in PIECES:
        pad = PAD if name.startswith('button') or name == 'keycap' else 0
        rows.append("  '%s': { w: %d, h: %d, pad: %d, left: %d, right: %d, top: %d, bottom: %d },"
                    % (name, w, h, pad, sl[0], sl[1], sl[2], sl[3]))
    for index in range(1, TORN_COUNT + 1):
        rows.append("  'card-torn-%d': { w: %d, h: %d, pad: 0, left: 0, right: 0, top: 0, bottom: 0 },"
                    % (index, CARD_W, CARD_H))
    rungs = ',\n'.join("  '%s': { height: %d, font: %d, cut: %d }" % r for r in RUNGS)
    return '''// 이 파일은 design-data/tools/ui.py 가 씁니다. 손으로 고치지 않습니다.
//
// 굽는 배율이 %d 이므로 놓을 때 %s 로 줄입니다. 여기의 값은 전부 1배 기준입니다.

export type Slice = {
  /** 1배 기준의 본디 크기입니다. */
  w: number
  h: number
  /** 겉면 밖으로 나가는 그림자의 여백입니다. 놓을 때 이만큼 물러앉습니다. */
  pad: number
  left: number
  right: number
  top: number
  bottom: number
}

export const BAKE_SCALE = %s

export const ATLAS: Record<string, Slice> = {
%s
}

/** 단추 높이의 계단 넷입니다. 그 사이 값은 쓰지 않습니다. */
export const RUNG = {
%s
} as const

export type RungName = keyof typeof RUNG

/** 카드의 뜯긴 가장자리 마스크의 수. 카드마다 하나를 돌려 씁니다. */
export const TORN_COUNT = %d
''' % (BAKE, '1 / %d' % BAKE, '1 / %d' % BAKE, chr(10).join(rows), rungs, TORN_COUNT)


def main():
    check = '--check' in sys.argv
    if check:
        pngs = [n for n, *_ in PIECES] + ['card-torn-%d' % i for i in range(1, TORN_COUNT + 1)]
        missing = [n + '.png' for n in pngs
                   if not os.path.exists(os.path.join(OUT, n + '.png'))]
        missing.extend(name + '.webp' for name in ILLUSTRATION_FILES
                       if not os.path.exists(os.path.join(OUT, name + '.webp')))
        missing.extend(filename for filename in [*SOURCE_FILES.values(), *ILLUSTRATION_FILES.values()]
                       if not os.path.exists(os.path.join(SOURCE, filename)))
        if missing:
            print('없는 조각: %s' % ' '.join(missing))
            return 1
        print('UI 조각과 원화가 모두 있습니다')
        return 0
    made = bake()
    io.open(TABLE, 'w', encoding='utf-8', newline='\n').write(table())
    print('구움 %d개 → %s' % (len(made), os.path.relpath(OUT, SAMPLE)))
    print('규격표 → %s' % os.path.relpath(TABLE, SAMPLE))
    return 0


if __name__ == '__main__':
    sys.exit(main())
