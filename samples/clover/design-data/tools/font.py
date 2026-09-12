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

# 물마루 (OFL). https://github.com/mushsooni/mulmaru
#
# **픽셀 서체입니다.** 1em 이 192유닛이고 1픽셀이 16유닛이라 12의 배수에서만 획이 격자에
# 맞습니다 — 화면의 글자 계단이 그래서 12·24·36·48·72 입니다.
#
# 한글 11,172자와 라틴 전부와 가나를 덮습니다. **한자는 없습니다** — 일본어와 중국어는
# 노토로 남습니다.
MULMARU = 'https://github.com/mushsooni/mulmaru/releases/download/v1.0/Mulmaru.zip'

# 숫자는 고정폭 벌입니다.
#
# **구르는 수가 흔들리지 않아야 합니다.** 칩과 배수와 점수는 한 자리씩 바뀌는데, 폭이
# 다른 숫자로 적으면 값이 오를 때마다 줄 전체가 좌우로 떨립니다.
MULMARU_MONO = ('https://github.com/mushsooni/mulmaru/releases/download/v1.0/MulmaruMono.zip')

# 어느 말이 어느 원본을 쓰는가. **라틴은 한 벌로 족합니다** — 영어와 독일어가 같은 글자를
# 씁니다.
SOURCE = {
    'ko': ('mulmaru', MULMARU),
    'ja': ('noto-sans-jp', CJK + 'NotoSansJP-VF.otf'),
    'zh-Hans': ('noto-sans-sc', CJK + 'NotoSansSC-VF.otf'),
    'zh-Hant': ('noto-sans-tc', CJK + 'NotoSansTC-VF.otf'),
    'en': ('mulmaru', MULMARU),
    'de': ('mulmaru', MULMARU),
}

# 굵기 둘입니다. 화면이 700 과 800 을 쓰는데, 800 은 700 으로 그려도 눈에 띄지 않습니다.
WEIGHTS = [400, 700]

# **픽셀 서체에는 굵기가 하나뿐입니다.** 가변 축이 없고 굵은 벌도 없습니다. 두 벌을 내면
# 같은 파일이 두 번 담기고, 브라우저가 굵게 흉내 내면 획이 격자를 벗어나 뭉갭니다.
ONE_WEIGHT = {'mulmaru'}

# **숫자는 고정폭 벌로 적습니다.** 칩과 배수와 점수는 한 자리씩 바뀌는데, 폭이 다른 숫자로
# 적으면 값이 오를 때마다 줄 전체가 좌우로 떨립니다. 같은 서체의 고정폭 벌이므로 본문과
# 획이 같습니다.
#
# 숫자와 그 사이에 끼는 기호만 남깁니다. 나머지 글자는 본문 글꼴이 그립니다.
NUMERALS = ('mulmaru-mono', MULMARU_MONO)
NUMERAL_LETTERS = '0123456789,./$+-x*eE ()%'
NUMERAL_WEIGHT = 400


def fetch(url: str, into: str) -> str:
    """원본을 받아 둡니다. 이미 있으면 다시 받지 않습니다 — 하나가 15MB 입니다.

    **zip 으로 배포되는 것이 있습니다.** 그런 것은 풀어서 안의 `.ttf` 를 씁니다.
    """
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, into)
    if os.path.exists(path) and os.path.getsize(path) > 20_000:
        return path
    request = urllib.request.Request(url, headers={'User-Agent': AGENT})
    with urllib.request.urlopen(request) as response:
        data = response.read()
    if url.endswith('.zip'):
        import zipfile
        with zipfile.ZipFile(io.BytesIO(data)) as zf:
            inner = [n for n in zf.namelist() if n.lower().endswith('.ttf')]
            if not inner:
                raise SystemExit('%s 안에 ttf 가 없습니다' % url)
            data = zf.read(sorted(inner, key=len)[0])
    io.open(path, 'wb').write(data)
    print('받음  %-28s %6.1f MB' % (into, len(data) / 1024 / 1024))
    return path


def kind(url: str) -> str:
    """받아 둘 파일의 확장자입니다. zip 은 풀고 나면 ttf 입니다."""
    if url.endswith('.zip'):
        return '.ttf'
    return os.path.splitext(url)[1].split('%')[0]


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
        source = fetch(entry['url'], name + kind(entry['url']))
        letters = ''.join(sorted(entry['letters']))
        for weight in ([400] if name in ONE_WEIGHT else WEIGHTS):
            target = os.path.join(OUT, '%s-%d.woff2' % (name, weight))
            size = cut(source, letters, weight, target)
            total += size
            print('%-20s %7.1f KB  글자 %d' % (os.path.basename(target), size / 1024,
                                              len(letters)))

    # 숫자 글꼴. **가변 축이 없는 글꼴이라 굵기를 고정할 것이 없습니다.**
    #
    # 물마루를 쓰는 말에서는 걸리지 않습니다 — 픽셀 서체 옆에 간판용 글꼴의 숫자를 두면
    # 같은 줄 안에서 격자가 어긋납니다. 한자를 쓰는 말에만 남습니다.
    name, url = NUMERALS
    source = fetch(url, name + kind(url))
    target = os.path.join(OUT, '%s-%d.woff2' % (name, NUMERAL_WEIGHT))
    size = cut(source, NUMERAL_LETTERS, NUMERAL_WEIGHT, target)
    total += size
    print('%-20s %7.1f KB  글자 %d' % (os.path.basename(target), size / 1024,
                                      len(NUMERAL_LETTERS)))

    print('합 %.1f KB' % (total / 1024))


if __name__ == '__main__':
    main()
