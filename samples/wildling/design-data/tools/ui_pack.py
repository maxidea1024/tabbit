# -*- coding: utf-8 -*-
"""무료 UI 팩을 받아 `unity/Assets/Resources/art/ui/` 에 넣는다.

**애셋스토어가 아니라 CC0 입니다.** 애셋스토어 패키지는 로그인이 필요하고 재배포가 막혀
저장소에 담을 수 없습니다 — 클론한 사람이 빌드하지 못하게 됩니다. Kenney 의 UI Pack 은
**Creative Commons Zero** 라서 저장소에 넣어도 되고 상업 사용도 자유롭습니다.

    python samples/wildling/design-data/tools/ui_pack.py

받은 파일은 커밋되므로 **이 스크립트는 다시 돌릴 일이 없습니다.** 네트워크가 없는 자리에서도
클론하면 그대로 빌드됩니다. 그림을 다시 받아야 할 때만 씁니다.
"""
import io
import os
import sys
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from art import unity_meta  # noqa: E402

ART = os.path.normpath(os.path.join(
    HERE, "..", "..", "unity", "Assets", "Resources", "art", "ui"))

URL = ("https://kenney.nl/media/pages/assets/ui-pack/"
       "f651646eab-1718203990/kenney_ui-pack.zip")

# (압축 안의 경로, 쓸 이름, 9분할 테두리)
#
# 테두리는 늘어나지 않는 가장자리입니다. 아래쪽이 두꺼운 것은 버튼의 두께를 남기기
# 위해서입니다 — 그 부분이 늘어나면 단추가 납작해집니다.
WANT = [
    # **밝은 화면용 키트입니다.** 흰 판에 색 테두리, 색 버튼에 흰 글자 — 요즘 모바일 게임의
    # 기본형입니다. 어둡게 물들이면 광택이 죽으므로 색 그대로 씁니다.
    ("PNG/Grey/Double/button_rectangle_depth_border.png", "panel", (28, 34, 28, 28)),
    ("PNG/Blue/Double/button_rectangle_depth_border.png", "panel_soft", (28, 34, 28, 28)),
    ("PNG/Grey/Double/button_rectangle_border.png", "panel_sunk", (28, 28, 28, 28)),
    ("PNG/Blue/Double/button_rectangle_depth_gloss.png", "button", (28, 34, 28, 28)),
    ("PNG/Green/Double/button_rectangle_depth_gloss.png", "button_accent", (28, 34, 28, 28)),
    ("PNG/Red/Double/button_rectangle_depth_gloss.png", "button_warn", (28, 34, 28, 28)),
    ("PNG/Grey/Double/button_rectangle_depth_gloss.png", "button_plain", (28, 34, 28, 28)),
    ("PNG/Yellow/Double/star.png", "star", (0, 0, 0, 0)),
    ("PNG/Extra/Double/divider.png", "divider", (16, 0, 16, 0)),
]

NOTICE = """이 폴더의 아래 파일들은 Kenney 의 UI Pack 2.0 에서 가져온 것입니다.

    panel · panel_soft · panel_sunk · button · button_accent · button_warn ·
    button_plain · star · divider

라이선스는 **Creative Commons Zero (CC0)** 입니다 — 개인·교육·상업 사용이 모두 자유롭고
표기 의무가 없습니다. 그래서 저장소에 담을 수 있습니다.

    출처   https://kenney.nl/assets/ui-pack
    라이선스 http://creativecommons.org/publicdomain/zero/1.0/

**애셋스토어 패키지는 여기 들어올 수 없습니다.** 로그인이 필요하고 재배포가 막혀 있어,
클론한 사람이 빌드하지 못하게 됩니다.

나머지 파일(`bar_*` · `glow` · `spark` · `shine` · `sheen` · `frame_*`)은
`art_ui.py` 가 만듭니다.
다시 받으려면 `python design-data/tools/ui_pack.py` 입니다.
"""


def main():
    os.makedirs(ART, exist_ok=True)

    cache = os.path.join(HERE, "..", "out", "kenney_ui-pack.zip")
    cache = os.path.normpath(cache)

    if not os.path.exists(cache):
        print("받는 중: %s" % URL)
        with urllib.request.urlopen(URL, timeout=180) as response:
            io.open(cache, "wb").write(response.read())
    print("%s (%.1f MB)" % (cache, os.path.getsize(cache) / 1048576.0))

    with zipfile.ZipFile(cache) as archive:
        for source, name, border in WANT:
            data = archive.read(source)
            path = os.path.join(ART, name + ".png")
            io.open(path, "wb").write(data)
            unity_meta(path, name, border=border)
            print("  %-14s %6d바이트  테두리 %s" % (name, len(data), border))

    io.open(os.path.join(ART, "readme.md"), "w", encoding="utf-8",
            newline="\n").write(NOTICE)

    os.remove(cache)
    print("%d장을 넣었습니다." % len(WANT))


if __name__ == "__main__":
    main()
