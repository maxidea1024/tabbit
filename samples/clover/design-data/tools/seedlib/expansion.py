# -*- coding: utf-8 -*-
"""확장 조커 350종의 색인.

계열 14개가 모듈 3개에 나뉘어 있고 여기서 하나로 모읍니다. 규격은
`doc/expansion.md` 에 있습니다.

|모듈|계열|종수|
|--|--|--|
|`exp_board`|무늬 · 랭크 · 족보 · 버리기|100|
|`exp_engine`|경제 · 성장 · 위험 · 강화|100|
|`exp_meta`|소모품 · 조커 · 덱|75|
|`exp_rules`|상점 · 규칙 · 진행|75|

**계열마다 25종이고 희귀도 배분이 정해져 있습니다.** `seed()` 가 그것을 확인하므로, 한
계열에 하나를 더하면 다른 하나를 빼야 합니다 — 총계가 규격의 숫자와 어긋나면 멈춥니다.
"""

from . import exp_board, exp_engine, exp_meta, exp_rules

POOL = 'Greenhouse'

# 계열마다 (커먼, 언커먼, 레어, 전설). 합계는 120 · 150 · 65 · 15 입니다.
SPLIT = {
    '무늬': (9, 11, 4, 1),
    '랭크': (9, 11, 4, 1),
    '족보': (9, 10, 5, 1),
    '버리기': (9, 11, 4, 1),
    '경제': (9, 10, 5, 1),
    '성장': (8, 11, 5, 1),
    '위험': (8, 11, 5, 1),
    '강화': (9, 11, 4, 1),
    '소모품': (8, 11, 5, 1),
    '조커': (8, 10, 6, 1),
    '덱': (9, 11, 4, 1),
    '상점': (9, 11, 4, 1),
    '규칙': (8, 11, 5, 1),
    '진행': (8, 10, 5, 2),
}

RARITIES = ('Common', 'Uncommon', 'Rare', 'Legendary')


def _families():
    out = []
    for module in (exp_board, exp_engine, exp_meta, exp_rules):
        out.extend(module.FAMILIES)
    return out


FAMILIES = _families()


def _tagged():
    """확장 풀 값을 붙인 조커 목록. 계열의 순서가 곧 수집 목록의 순서입니다."""
    out = []
    for _, entries in FAMILIES:
        for e in entries:
            out.append(e[:8] + (POOL,))
    return out


JOKERS = _tagged()

# 계열 이름을 조커마다 붙여 둔 것. `verify.py` 가 계열별 종수를 셀 때 씁니다.
FAMILY_OF = {}
for _name, _entries in FAMILIES:
    for _e in _entries:
        FAMILY_OF[_e[0]] = _name


def check():
    """규격과 어긋나면 멈춥니다. `jokers.seed()` 가 부릅니다."""
    assert len(FAMILIES) == 14, '계열이 %d개입니다' % len(FAMILIES)

    for name, entries in FAMILIES:
        assert len(entries) == 25, '%s 계열이 %d종입니다' % (name, len(entries))
        counts = tuple(sum(1 for e in entries if e[3] == r) for r in RARITIES)
        assert counts == SPLIT[name], \
            '%s 계열의 희귀도 배분이 %s 입니다 — 규격은 %s' % (name, counts, SPLIT[name])

    assert len(JOKERS) == 350, '확장이 %d종입니다' % len(JOKERS)

    ids = [e[0] for e in JOKERS]
    assert len(set(ids)) == len(ids), \
        '확장 안에서 식별자가 겹칩니다: %s' % _duplicates(ids)
    for lang, index in (('한국어', 1), ('영어', 2)):
        names = [e[index] for e in JOKERS]
        assert len(set(names)) == len(names), \
            '확장 안에서 %s 이름이 겹칩니다: %s' % (lang, _duplicates(names))

    for e in JOKERS:
        assert e[7], '%s 에 효과가 없습니다' % e[0]
        bare = (len(e[7]) == 1 and e[7][0][1][0] == 'CondAlways'
                and e[7][0][2][0] == 'OpAddMult')
        assert not bare, \
            '%s 가 판정 기준에 걸립니다 — 조건 없는 배수 가산 하나뿐입니다' % e[0]


def _duplicates(values):
    seen, dup = set(), []
    for v in values:
        if v in seen and v not in dup:
            dup.append(v)
        seen.add(v)
    return ', '.join(dup)
