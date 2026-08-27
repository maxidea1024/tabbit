# -*- coding: utf-8 -*-
"""Cards.xlsx — 카드를 이루는 것들과 족보.

수치의 출처는 `doc/parity/hands-and-cards.md` 입니다.
"""

from .grid import ALWAYS, C, E, O, effect_grid, table, write

RANKS = [
    # 랭크, 칩, 그림 카드인가, 표시
    ('Two', 2, False, '2'), ('Three', 3, False, '3'), ('Four', 4, False, '4'),
    ('Five', 5, False, '5'), ('Six', 6, False, '6'), ('Seven', 7, False, '7'),
    ('Eight', 8, False, '8'), ('Nine', 9, False, '9'), ('Ten', 10, False, '10'),
    ('Jack', 10, True, 'J'), ('Queen', 10, True, 'Q'), ('King', 10, True, 'K'),
    ('Ace', 11, False, 'A'),
]

SUITS = [
    ('Spade', '검정', 1, 'S'), ('Heart', '빨강', 2, 'H'),
    ('Club', '검정', 3, 'C'), ('Diamond', '빨강', 4, 'D'),
]

HANDS = [
    # 족보, 기본 칩, 기본 배수, 레벨당 칩, 레벨당 배수, 처음부터 보이는가
    ('HighCard', 5, 1, 10, 1, True),
    ('Pair', 10, 2, 15, 1, True),
    ('TwoPair', 20, 2, 20, 1, True),
    ('ThreeOfAKind', 30, 3, 20, 2, True),
    ('Straight', 30, 4, 30, 3, True),
    ('Flush', 35, 4, 15, 2, True),
    ('FullHouse', 40, 4, 25, 2, True),
    ('FourOfAKind', 60, 7, 30, 3, True),
    ('StraightFlush', 100, 8, 40, 4, True),
    ('FiveOfAKind', 120, 12, 35, 3, False),
    ('FlushHouse', 140, 14, 40, 4, False),
    ('FlushFive', 160, 16, 50, 3, False),
]

ENHANCEMENTS = [
    ('None', '없음'), ('Bonus', '보너스'), ('Mult', '배수'), ('Wild', '와일드'),
    ('Glass', '유리'), ('Steel', '강철'), ('Stone', '석재'), ('Gold', '황금'),
    ('Lucky', '행운'),
]

ENHANCEMENT_EFFECTS = [
    ('Bonus', [E('OnCardScored', ALWAYS, O('AddChips', chips=30))]),
    ('Mult', [E('OnCardScored', ALWAYS, O('AddMult', mult=40000))]),
    ('Wild', [E('Passive', ALWAYS, O('CardTrait', trait='AnySuit'), 'SelfTarget')]),
    ('Glass', [E('OnCardScored', ALWAYS, O('MulMult', mult=20000)),
               E('OnCardScored', ALWAYS, O('DestroyCard', count=1), 'ScoredCard',
                 chance=(1, 4))]),
    ('Steel', [E('OnCardHeld', ALWAYS, O('MulMult', mult=15000))]),
    ('Stone', [E('OnCardScored', ALWAYS, O('AddChips', chips=50)),
               E('Passive', ALWAYS, O('CardTrait', trait='NoRankSuit'), 'SelfTarget'),
               E('Passive', ALWAYS, O('CardTrait', trait='AlwaysScores'), 'SelfTarget')]),
    ('Gold', [E('OnRoundEnd', ALWAYS, O('AddMoney', money=3))]),
    ('Lucky', [E('OnCardScored', ALWAYS, O('AddMult', mult=200000), chance=(1, 5)),
               E('OnCardScored', ALWAYS, O('AddMoney', money=20), chance=(1, 15))]),
]

SEALS = [('None', '없음'), ('Red', '붉은'), ('Blue', '파란'), ('Gold', '금색'), ('Purple', '보라')]

SEAL_EFFECTS = [
    ('Red', [E('OnCardScored', ALWAYS, O('Retrigger', times=1), 'ScoredCard')]),
    ('Blue', [E('OnRoundEnd', ALWAYS,
                O('CreateCard', create='Planet', count=1, hand_pick='Played'))]),
    ('Gold', [E('OnCardScored', ALWAYS, O('AddMoney', money=3))]),
    ('Purple', [E('OnCardDiscarded', ALWAYS, O('CreateCard', create='Tarot', count=1))]),
]

EDITIONS = [
    # 에디션, 표시, 칩, 배수 가산, 배수 곱, 조커 슬롯, 상점 등장 가중치
    ('Base', '기본', 0, 0, 10000, 0, 100),
    ('Foil', '포일', 50, 0, 10000, 0, 20),
    ('Holographic', '홀로그래픽', 0, 100000, 10000, 0, 14),
    ('Polychrome', '폴리크롬', 0, 0, 15000, 0, 3),
    ('Negative', '네거티브', 0, 0, 10000, 1, 3),
]


def seed():
    write('Rank', table(
        'Rank(key=rank)', '랭크 하나의 값입니다. 크기 순서는 enum 이 정하고 칩값이 여기 있습니다.',
        ['rank', 'chips', 'is_face', 'display'],
        ['RankKind', 'int (min=2, max=11)', 'bool', 'string'],
        ['랭크', '득점할 때의 칩값', '그림 카드인가', '카드에 적히는 글자'],
        [list(r) for r in RANKS]))

    write('Suit', table(
        'Suit(key=suit)', '무늬 하나의 표시입니다.',
        ['suit', 'color', 'sort_order', 'letter'],
        ['SuitKind', 'string', 'int (min=1, max=4)', 'string (regex="^[A-Z]$")'],
        ['무늬', '카드에 쓰는 색', '정렬 순서', '식별자에 쓰는 글자'],
        [list(s) for s in SUITS]))

    write('BaseDeckCard', table(
        'BaseDeckCard(key=card_id)',
        '표준 52장입니다. 덱이 이 목록을 걸러 시작 덱을 만듭니다.',
        ['card_id', 'rank', 'suit', 'is_face'],
        ['string (regex="^[SHCD](10|[2-9JQKA])$")', 'RankKind', 'SuitKind', 'bool'],
        ['식별자', '랭크', '무늬', '그림 카드인가'],
        [['%s%s' % (s[3], r[3]), r[0], s[0], r[2]] for s in SUITS for r in RANKS]))

    write('PokerHand', table(
        'PokerHand(key=hand)',
        '족보 하나의 값 넷입니다. 레벨 N 의 값은 기본값에 증분을 N-1 번 더한 것입니다.',
        ['hand', 'base_chips', 'base_mult', 'chips_per_level', 'mult_per_level',
         'visible_from_start', 'sort_order'],
        ['PokerHandKind', 'int (min=1)', 'int (min=1)', 'int (min=1)', 'int (min=1)',
         'bool', 'int (min=1, max=12)'],
        ['족보', '레벨 1의 칩', '레벨 1의 배수', '레벨당 칩 증분', '레벨당 배수 증분',
         '처음부터 목록에 보이는가', '표시 순서'],
        [list(h) + [i + 1] for i, h in enumerate(HANDS)]))

    write('Enhancement', table(
        'Enhancement(key=enhancement)', '카드 한 장에 붙는 강화입니다. 한 장에 하나뿐입니다.',
        ['enhancement', 'display'], ['EnhancementKind', 'string (text=Enhancement)'],
        ['강화', '표시 이름'], [list(e) for e in ENHANCEMENTS]))

    write('Seal', table(
        'Seal(key=seal)', '인장입니다. 강화와 별개의 축이고 한 장에 하나뿐입니다.',
        ['seal', 'display'], ['SealKind', 'string (text=Seal)'],
        ['인장', '표시 이름'], [list(s) for s in SEALS]))

    write('Edition', table(
        'Edition(key=edition)',
        '에디션입니다. 카드와 조커 양쪽에 붙고 값이 같습니다. 배수 곱은 만분율입니다.',
        ['edition', 'display', 'chips', 'mult_add', 'mult_mul', 'joker_slots', 'weight'],
        ['EditionKind', 'string (text=Edition)', 'int', 'int', 'int (min=1)', 'int',
         'int (min=1)'],
        ['에디션', '표시 이름', '칩 가산', '배수 가산', '배수 곱', '조커 슬롯 증가',
         '상점 등장 가중치'],
        [list(e) for e in EDITIONS]))

    # 두 테이블의 키가 enum 이므로 `foreign` 으로 가리킬 수 없습니다 — 도구의 제약이고
    # `doc/tool-findings.md` 에 적어 두었습니다. 값을 enum 으로 들고 있으면 읽는 쪽이 그
    # 테이블의 인덱스로 행을 찾습니다.
    effect_grid('EnhancementEffect', 'EnhancementKind', '강화가 하는 일입니다.',
                ENHANCEMENT_EFFECTS)
    effect_grid('SealEffect', 'SealKind', '인장이 하는 일입니다.', SEAL_EFFECTS)
    return 9
