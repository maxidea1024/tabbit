# -*- coding: utf-8 -*-
"""`data/*.tsv` 를 처음 한 번 만듭니다.

**정본은 `.tsv` 입니다.** 이 스크립트를 다시 돌리면 손으로 고친 값이 사라집니다. 값을 다시
계산해야 하는 경우에만 씁니다 — 격자를 처음 세울 때와, 컬럼 구성을 바꿀 때입니다.

원작의 어느 규칙이 어느 변종으로 갔는지는 `doc/parity/` 에 적혀 있고, 여기 있는 것은 그
판정을 격자로 옮긴 것입니다.

    python samples/clover/design-data/tools/seed.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from seedlib import cards, consts, consumables, jokers, progression, setup_, shop  # noqa: E402


def main():
    total = 0
    for module in (cards, jokers, consumables, progression, shop, setup_, consts):
        total += module.seed()
    print('-' * 34)
    print('격자 %d개' % total)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
