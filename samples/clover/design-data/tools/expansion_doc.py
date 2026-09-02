# -*- coding: utf-8 -*-
"""확장 350종의 목록 문서를 데이터에서 만듭니다.

**손으로 적지 않습니다.** 350종의 표를 사람이 관리하면 값을 고칠 때마다 문서가 실제와
어긋나고, 어긋난 것은 아무 게이트도 보지 않습니다. 여기서 만들면 격자가 정본입니다.

    python samples/clover/design-data/tools/expansion_doc.py

`doc/expansion/` 아래에 계열 묶음마다 한 파일이 나옵니다. 색인은 `doc/expansion.md` 이고
그것은 손으로 적는 문서입니다 — 규격과 판정 기준이 거기 있습니다.
"""

import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)
SAMPLE = os.path.dirname(DESIGN)
sys.path.insert(0, HERE)

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

from seedlib import exp_board, exp_engine, exp_meta, exp_rules  # noqa: E402

# 파일 하나가 계열 몇 개를 담는가. 모듈의 경계와 같습니다.
GROUPS = [
    ('board', '판 위의 것', exp_board,
     '한 판 안에서 값이 결정되는 계열입니다. 무늬와 랭크와 족보를 고르는 방식이 바뀝니다.'),
    ('engine', '값을 만드는 기관', exp_engine,
     '돈과 누적과 대가로 값을 만드는 계열입니다. 라운드를 넘겨야 값이 커집니다.'),
    ('meta', '판 밖의 것', exp_meta,
     '소모품과 조커와 덱을 다루는 계열입니다. 사는 순서가 값을 정합니다.'),
    ('rules', '규칙', exp_rules,
     '상점과 판정과 진행을 바꾸는 계열입니다. 값이 점수가 아니라 규칙으로 옵니다.'),
]

RARITY_KO = {
    'Common': '커먼', 'Uncommon': '언커먼', 'Rare': '레어', 'Legendary': '전설',
}


def rows_of(entries):
    """조커 하나를 표의 한 줄로. 효과는 트리거와 변종만 적습니다."""
    out = []
    for e in entries:
        jid, ko, en, rarity, cost = e[0], e[1], e[2], e[3], e[4]
        parts = []
        for trigger, cond, op, _scope, _count, chance, _first in e[7]:
            piece = '`%s` %s → %s' % (trigger, cond[0][4:], op[0][2:])
            if chance:
                piece += ' (%d/%d)' % chance
            parts.append(piece)
        out.append('|%s|%s|`%s`|%s|$%d|%s|' % (
            ko, en, jid, RARITY_KO[rarity], cost, ' · '.join(parts)))
    return out


def write_group(slug, title, module, lead):
    lines = []
    lines.append('# %s' % title)
    lines.append('')
    lines.append('> [확장 조커로](../expansion.md)')
    lines.append('')
    lines.append('---')
    lines.append('')
    lines.append(lead)
    lines.append('')
    lines.append('**이 파일은 생성물입니다.** 값을 고치려면 `design-data/tools/seedlib/` 의')
    lines.append('해당 모듈을 고치고 `expansion_doc.py` 를 다시 돌립니다.')
    lines.append('')

    for family, entries in module.FAMILIES:
        counts = {}
        for e in entries:
            counts[e[3]] = counts.get(e[3], 0) + 1
        summary = ' · '.join('%s %d' % (RARITY_KO[r], counts[r])
                             for r in ('Common', 'Uncommon', 'Rare', 'Legendary')
                             if r in counts)
        lines.append('## %s %d종' % (family, len(entries)))
        lines.append('')
        lines.append(summary)
        lines.append('')
        lines.append('|이름|영어|`id`|희귀도|가격|효과|')
        lines.append('|--|--|--|--|--|--|')
        lines.extend(rows_of(entries))
        lines.append('')

    lines.append('---')
    lines.append('')
    lines.append('EOD')

    folder = os.path.join(SAMPLE, 'doc', 'expansion')
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, slug + '.md')
    with io.open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')
    total = sum(len(entries) for _, entries in module.FAMILIES)
    print('%-14s %3d종  %s' % (slug + '.md', total, path))
    return total


def main():
    total = 0
    for slug, title, module, lead in GROUPS:
        total += write_group(slug, title, module, lead)
    print('-' * 34)
    print('확장 %d종' % total)
    assert total == 350, '%d종입니다' % total
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
