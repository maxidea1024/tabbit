# -*- coding: utf-8 -*-
"""조커 격자만 다시 씁니다.

**`seed.py` 를 쓰지 않는 이유가 있습니다.** 그것은 격자 40개를 통째로 다시 만들고, 그러면
손으로 채운 것이 사라집니다 — `StringTable` 의 `ui.*` 열쇠와 6개 언어 번역, `SoundCue` 와
`TagEffect` 와 `Const_Feel` 의 조정값이 그런 것들입니다. 조커를 고칠 때 그것들까지 잃을
이유가 없습니다.

    python samples/clover/design-data/tools/seed_jokers.py

다시 쓰는 것은 넷입니다.

|격자|어떻게|
|--|--|
|`Joker`|`jokers.seed()` 가 통째로 씁니다|
|`JokerRarityWeight`|같음|
|`JokerEffect`|같음|
|`StringTable`|**통째로 쓰지 않습니다.** `joker.*.name` 행만 갈아 끼우고 나머지 행과 컬럼은 그대로 둡니다|

`StringTable` 의 언어는 `ko` 와 `en` 만 채웁니다. 나머지는 비웁니다 — 검증이 요구하는 것이
그 둘이고, **번역되지 않은 것은 번역되지 않은 것으로 남아야** 나중에 무엇을 채워야 하는지
알 수 있습니다.
"""

import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
sys.path.insert(0, HERE)

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

from seedlib import jokers  # noqa: E402

STRINGS = os.path.join(DESIGN, 'data', 'StringTable.tsv')
PREFIX = '\tjoker.'


def rewrite_strings():
    """`joker.*.name` 행을 지금의 조커 목록으로 갈아 끼웁니다."""
    lines = io.open(STRINGS, encoding='utf-8').read().split('\n')
    fields = lines[1].split('\t')[1:]
    width = len(fields)

    first = next(i for i, line in enumerate(lines) if line.startswith(PREFIX))
    last = max(i for i, line in enumerate(lines) if line.startswith(PREFIX))
    keep = {}
    for line in lines[first:last + 1]:
        cells = line.split('\t')[1:]
        keep[cells[0]] = cells

    fresh = []
    for entry in jokers.JOKERS:
        key = 'joker.%s.name' % entry[0]
        # 이미 있던 행은 그대로 둡니다 — 다른 언어의 번역이 거기 있습니다.
        if key in keep:
            fresh.append('\t' + '\t'.join(keep[key]))
            continue
        cells = [key, entry[1], entry[2]] + [''] * (width - 3)
        fresh.append('\t' + '\t'.join(cells))

    added = len(fresh) - len(keep)
    lines[first:last + 1] = fresh
    with io.open(STRINGS, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines))
    print('%-24s %5d행  (조커 %d줄, 새로 %d줄)'
          % ('StringTable.tsv', sum(1 for L in lines if L.startswith('\t')),
             len(fresh), added))


def main():
    jokers.seed()
    rewrite_strings()
    print('-' * 34)
    print('조커 %d종' % len(jokers.JOKERS))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
