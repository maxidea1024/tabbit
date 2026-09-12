# -*- coding: utf-8 -*-
"""화면의 부품을 그림으로 굽습니다.

**런타임에 도형을 그리지 않습니다.** 판·단추·칸·자리·키캡·게이지는 모양이 고정이므로
9분할 그림으로 굽고 스프라이트로 놓습니다. 실행 중에 남는 비용이 0 입니다.

**회색조로 한 벌만 굽습니다.** 갈래와 상태는 `tint` 로 만듭니다. 물들이기가 색을 곱하는
것이므로 어두운 턱은 어둡게 남고 밝은 변이 그 색을 받습니다 — 단추 갈래 다섯과 상태 넷이
그림 한 장에서 나옵니다.

**두 배로 굽고 절반으로 놓습니다.** 1대1로 구우면 2배 밀도 화면에서 늘려 쓰게 되어
가장자리가 흐려집니다. `web/src/ui/chrome.ts` 의 `SCALE` 이 이미 그 값입니다.

**손으로 다시 그리지 않습니다.** 규격은 [디자인 언어](../../doc/ui/language.md) 에 있고
이 파일은 그 값을 CSS 로 적어 브라우저에서 내립니다. 그래야 문서와 애셋이 어긋나지
않습니다.

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
    return ('background: linear-gradient(180deg, #ffffff 0%%, #5a5a5a 100%%);'
            ' clip-path: polygon(0 0, 100%% 0, 100%% calc(100%% - %s), calc(100%% - %s) 100%%,'
            ' 0 100%%);' % (px(c), px(c)))


def well():
    """칸 — 판 안으로 눌린 자리. 위 안쪽의 그늘과 아래의 밝은 줄."""
    return ('background: #3c3c3c;'
            ' box-shadow: inset 0 %s 0 #202020, inset 0 %s %s #141414,'
            ' inset 0 -%s 0 #787878;'
            % (px(1), px(2), px(5), px(1)))


def tray():
    """물건 자리 — 칸을 하나씩 그리지 않습니다. 고정된 영역 하나입니다."""
    return ('background: linear-gradient(180deg, #3a3a3a 0%%, #242424 100%%);'
            ' box-shadow: inset 0 %s 0 #8c8c8c, inset 0 0 0 %s #1e1e1e;'
            % (px(1), px(1)))


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
    return ('background: #1a1a1a; box-shadow: inset 0 %s %s #000000,'
            ' inset 0 -%s 0 #565656;' % (px(1), px(3), px(1)))


def gauge_fill():
    return 'background: linear-gradient(180deg, #ffffff 0%, #9b9b9b 100%);'


def glow_edge():
    """테두리의 빛 — 왼쪽에서 밝게 시작해 오른쪽으로 사라집니다.

    아무 변에나 두면 화면이 번들거립니다. 문서가 다섯 자리를 정해 두었습니다.
    """
    return ('background: linear-gradient(90deg, #ffffff 0%, #6e6e6e 62%, transparent 100%);')


PIECES = [
    ('plate', 96, 96, (24, 24, 6, PLATE_CUT + 4), plate()),
    ('well', 64, 64, (12, 12, 8, 8), well()),
    ('tray', 96, 96, (14, 14, 8, 8), tray()),
    ('head', 96, 64, (14, 14, 8, 10), head()),
    ('keycap', 64, 36, (18, 18, 10, 14), keycap()),
    ('gauge', 48, 12, (6, 6, 0, 0), gauge_groove()),
    ('gauge-fill', 48, 8, (6, 6, 0, 0), gauge_fill()),
    ('glow-edge', 64, 2, (0, 0, 0, 0), glow_edge()),
]
for name, h, _font, cut in RUNGS:
    PIECES.append((name, cut * 4 + 16, h, (cut + 6, cut + 6, 0, 0), button(h, cut)))


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
    return made


def table():
    """규격표입니다. 코드가 여백을 다시 적지 않게 합니다."""
    rows = []
    for name, w, h, sl, _css in PIECES:
        pad = PAD if name.startswith('button') or name == 'keycap' else 0
        rows.append("  '%s': { w: %d, h: %d, pad: %d, left: %d, right: %d, top: %d, bottom: %d },"
                    % (name, w, h, pad, sl[0], sl[1], sl[2], sl[3]))
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
''' % (BAKE, '1 / %d' % BAKE, '1 / %d' % BAKE, chr(10).join(rows), rungs)


def main():
    check = '--check' in sys.argv
    if check:
        missing = [n for n, *_ in PIECES if not os.path.exists(os.path.join(OUT, n + '.png'))]
        if missing:
            print('없는 조각: %s' % ' '.join(missing))
            return 1
        print('조각 %d개가 모두 있습니다' % len(PIECES))
        return 0
    made = bake()
    io.open(TABLE, 'w', encoding='utf-8', newline='\n').write(table())
    print('구움 %d개 → %s' % (len(made), os.path.relpath(OUT, SAMPLE)))
    print('규격표 → %s' % os.path.relpath(TABLE, SAMPLE))
    return 0


if __name__ == '__main__':
    sys.exit(main())
