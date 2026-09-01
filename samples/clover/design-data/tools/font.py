# -*- coding: utf-8 -*-
"""글꼴을 쓰는 글자만큼만 잘라 담습니다.

**여섯 말이 한 벌로 나와야 합니다.** 기계의 글꼴에 맡기면 없는 기계에서 네모가 보이고,
있는 기계에서도 저마다 다른 글꼴로 보입니다.

통째로 담으면 말마다 10~15MB 입니다. 이 게임의 글은 시트에 다 있으므로 **쓰는 글자를 셀 수
있고**, 그만큼만 담으면 수십 KB 로 끝납니다.

한자는 일본어·간체·번체의 자형이 다릅니다. 한 벌로 합치면 일본어 화면에 중국 자형이
나오므로 **말마다 따로 담습니다.**

    python design-data/tools/seed.py            # 먼저 쓰는 글자를 셉니다
    python design-data/tools/font.py

받아 온 원본은 `design-data/out/font-src/` 에 남습니다 — 저장소에 넣지 않습니다.
"""
import io
import json
import os
import urllib.request

from fontTools import subset
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
CHARS = os.path.join(ROOT, 'design-data', 'out', 'font-chars.json')
CACHE = os.path.join(ROOT, 'design-data', 'out', 'font-src')
OUT = os.path.join(ROOT, 'web', 'public', 'font')

AGENT = ('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 '
         '(KHTML, like Gecko) Chrome/120.0 Safari/537.36')

CJK = 'https://cdn.jsdelivr.net/gh/googlefonts/noto-cjk@main/Sans/Variable/OTF/Subset/'
LATIN = 'https://cdn.jsdelivr.net/gh/notofonts/notofonts.github.io@main/fonts/NotoSans/'

# 어느 말이 어느 원본을 쓰는가. **라틴은 한 벌로 족합니다** — 영어와 독일어가 같은 글자를
# 씁니다.
SOURCE = {
    'ko': ('noto-sans-kr', CJK + 'NotoSansKR-VF.otf'),
    'ja': ('noto-sans-jp', CJK + 'NotoSansJP-VF.otf'),
    'zh-Hans': ('noto-sans-sc', CJK + 'NotoSansSC-VF.otf'),
    'zh-Hant': ('noto-sans-tc', CJK + 'NotoSansTC-VF.otf'),
    'en': ('noto-sans', LATIN + 'unhinted/variable-ttf/NotoSans%5Bwdth,wght%5D.ttf'),
    'de': ('noto-sans', LATIN + 'unhinted/variable-ttf/NotoSans%5Bwdth,wght%5D.ttf'),
}

# 굵기 둘입니다. 화면이 700 과 800 을 쓰는데, 800 은 700 으로 그려도 눈에 띄지 않습니다.
WEIGHTS = [400, 700]

# **숫자는 다른 글꼴입니다.** 판을 읽는 사람이 보는 것은 수이고, 본문용 글꼴의 숫자는 어느
# 화면에서나 같은 모습이라 이 게임의 것으로 보이지 않습니다 — 간판용 글꼴은 획이 굵고 각져서
# 작은 칸에서도 각이 살아 있습니다.
#
# 숫자와 그 사이에 끼는 기호만 남깁니다. 나머지 글자는 본문 글꼴이 그립니다.
NUMERALS = ('bungee',
            'https://raw.githubusercontent.com/google/fonts/main/ofl/bungee/'
            'Bungee-Regular.ttf')
NUMERAL_LETTERS = '0123456789,./$+-x*eE ()%'
NUMERAL_WEIGHT = 700


def fetch(url: str, into: str) -> str:
    """원본을 받아 둡니다. 이미 있으면 다시 받지 않습니다 — 하나가 15MB 입니다."""
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, into)
    if os.path.exists(path) and os.path.getsize(path) > 100_000:
        return path
    request = urllib.request.Request(url, headers={'User-Agent': AGENT})
    with urllib.request.urlopen(request) as response:
        data = response.read()
    io.open(path, 'wb').write(data)
    print('받음  %-28s %6.1f MB' % (into, len(data) / 1024 / 1024))
    return path


def cut(source: str, letters: str, weight: int, target: str) -> int:
    """
    그 굵기로 고정하고, 쓰는 글자만 남깁니다.

    **가변 글꼴은 굵기를 고정해야 합니다.** 그대로 두면 굵기 축의 자료가 다 따라오고, 그것이
    잘라 낸 글자보다 큽니다.
    """
    font = TTFont(source, fontNumber=0, lazy=True)
    if 'fvar' in font:
        from fontTools.varLib import instancer
        font = instancer.instantiateVariableFont(font, {'wght': weight}, inplace=False)

    options = subset.Options()
    options.flavor = 'woff2'
    options.desubroutinize = True
    options.layout_features = ['kern', 'liga', 'calt', 'ccmp']
    options.name_IDs = ['*']
    options.notdef_outline = True
    options.drop_tables += ['DSIG']

    subsetter = subset.Subsetter(options=options)
    subsetter.populate(text=letters)
    subsetter.subset(font)
    font.flavor = 'woff2'
    font.save(target)
    font.close()
    return os.path.getsize(target)


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    text = json.load(io.open(CHARS, encoding='utf-8'))

    # 같은 원본을 쓰는 말들의 글자를 모읍니다.
    wanted: dict = {}
    for lang, (name, url) in SOURCE.items():
        entry = wanted.setdefault(name, {'url': url, 'letters': set()})
        entry['letters'] |= set(text[lang])

    total = 0
    for name, entry in wanted.items():
        source = fetch(entry['url'], name + os.path.splitext(entry['url'])[1].split('%')[0])
        letters = ''.join(sorted(entry['letters']))
        for weight in WEIGHTS:
            target = os.path.join(OUT, '%s-%d.woff2' % (name, weight))
            size = cut(source, letters, weight, target)
            total += size
            print('%-20s %7.1f KB  글자 %d' % (os.path.basename(target), size / 1024,
                                              len(letters)))

    # 숫자 글꼴. **가변 축이 없는 글꼴이라 굵기를 고정할 것이 없습니다.**
    name, url = NUMERALS
    source = fetch(url, name + '.ttf')
    target = os.path.join(OUT, '%s-%d.woff2' % (name, NUMERAL_WEIGHT))
    size = cut(source, NUMERAL_LETTERS, NUMERAL_WEIGHT, target)
    total += size
    print('%-20s %7.1f KB  글자 %d' % (os.path.basename(target), size / 1024,
                                      len(NUMERAL_LETTERS)))

    print('합 %.1f KB' % (total / 1024))


if __name__ == '__main__':
    main()
