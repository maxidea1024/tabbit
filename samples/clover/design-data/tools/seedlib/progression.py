# -*- coding: utf-8 -*-
"""Progression.xlsx — 안테 · 블라인드 · 보스 28종.

수치의 출처는 `doc/parity/progression.md` 와 `doc/parity/blinds.md` 입니다.
"""

from .grid import ALWAYS, C, E, O, PER, RULE, effect_grid, table, write

# 안테, 흰~검은, 초록~파란, 보라~황금
ANTE = [
    (0, 100, 100, 100),
    (1, 300, 300, 300),
    (2, 800, 900, 1000),
    (3, 2000, 2600, 3200),
    (4, 5000, 8000, 9000),
    (5, 11000, 20000, 25000),
    (6, 20000, 36000, 60000),
    (7, 35000, 60000, 110000),
    (8, 50000, 100000, 200000),
]

# 블라인드, 표시 이름, 요구 점수 배율(만분율), 보상, 스킵 가능
BLIND = [
    ('Small', '스몰 블라인드', 10000, 3, True),
    ('Big', '빅 블라인드', 15000, 4, True),
    ('Boss', '보스 블라인드', 20000, 5, False),
]

# 식별자, 표시 이름, 영문 이름, 최소 안테, 배율(만분율), 최종 보스인가, 효과
BOSS = [
    ('the_hook', '갈고리', 'The Hook', 1, 20000, False,
     [E('OnHandPlayed', ALWAYS, O('ForceDiscard', count=2), 'AllInHand')]),
    ('the_club', '클럽', 'The Club', 1, 20000, False,
     [E('Passive', ALWAYS, O('Debuff', debuff='BySuit', suit='Club'), 'AllInDeck')]),
    ('the_psychic', '심령술사', 'The Psychic', 1, 20000, False,
     [E('Passive', ALWAYS, RULE('MustPlayFiveCards'))]),
    ('the_goad', '자극', 'The Goad', 1, 20000, False,
     [E('Passive', ALWAYS, O('Debuff', debuff='BySuit', suit='Spade'), 'AllInDeck')]),
    ('the_window', '창문', 'The Window', 1, 20000, False,
     [E('Passive', ALWAYS, O('Debuff', debuff='BySuit', suit='Diamond'), 'AllInDeck')]),
    ('the_manacle', '수갑', 'The Manacle', 1, 20000, False,
     [E('Passive', ALWAYS, RULE('HandSize', -1))]),
    ('the_pillar', '기둥', 'The Pillar', 1, 20000, False,
     [E('Passive', ALWAYS, O('Debuff', debuff='PlayedThisAnte'), 'AllInDeck')]),
    ('the_head', '머리', 'The Head', 1, 20000, False,
     [E('Passive', ALWAYS, O('Debuff', debuff='BySuit', suit='Heart'), 'AllInDeck')]),
    ('the_house', '집', 'The House', 2, 20000, False,
     [E('OnRoundStart', C('FirstHand'), O('DrawFaceDown'), 'AllInHand')]),
    ('the_wall', '벽', 'The Wall', 2, 40000, False, []),
    ('the_wheel', '바퀴', 'The Wheel', 2, 20000, False,
     [E('Passive', ALWAYS, O('DrawFaceDown'), 'AllInHand', chance=(1, 7))]),
    ('the_arm', '팔', 'The Arm', 2, 20000, False,
     [E('OnHandPlayed', ALWAYS, O('LevelUpHand', hand_pick='Played', levels=-1))]),
    ('the_fish', '물고기', 'The Fish', 2, 20000, False,
     [E('OnHandPlayed', ALWAYS, O('DrawFaceDown'), 'AllInHand')]),
    ('the_water', '물', 'The Water', 2, 20000, False,
     [E('OnRoundStart', ALWAYS, RULE('SetDiscardsZero', 1, duration='ThisRound'))]),
    ('the_mouth', '입', 'The Mouth', 2, 20000, False,
     [E('Passive', ALWAYS, RULE('SingleHandTypeOnly'))]),
    ('the_needle', '바늘', 'The Needle', 2, 10000, False,
     [E('Passive', ALWAYS, RULE('HandsPerRound', 1, absolute=True))]),
    ('the_flint', '부싯돌', 'The Flint', 2, 20000, False,
     [E('Passive', ALWAYS, RULE('HalveBaseChipsAndMult'))]),
    ('the_mark', '표적', 'The Mark', 2, 20000, False,
     [E('Passive', ALWAYS, O('DrawFaceDown', card_class='Face'), 'AllInHand')]),
    ('the_eye', '눈', 'The Eye', 3, 20000, False,
     [E('Passive', ALWAYS, RULE('NoRepeatHandTypes'))]),
    ('the_tooth', '이빨', 'The Tooth', 3, 20000, False,
     [E('OnHandPlayed', ALWAYS, PER('CardsPlayed', 'AddMoney', -1))]),
    ('the_plant', '초목', 'The Plant', 4, 20000, False,
     [E('Passive', ALWAYS, O('Debuff', debuff='FaceCards'), 'AllInDeck')]),
    ('the_serpent', '뱀', 'The Serpent', 5, 20000, False,
     [E('Passive', ALWAYS, RULE('AlwaysDrawThree'))]),
    ('the_ox', '황소', 'The Ox', 6, 20000, False,
     [E('OnHandPlayed', C('IsMostPlayedHand'), O('SetMoney', money=0))]),

    ('amber_acorn', '호박 도토리', 'Amber Acorn', 8, 20000, True,
     [E('OnRoundStart', ALWAYS, O('FlipJokers'), 'AllJokers')]),
    ('verdant_leaf', '푸른 잎', 'Verdant Leaf', 8, 20000, True,
     [E('Passive', ALWAYS, RULE('DebuffUntilJokerSold'))]),
    ('violet_vessel', '자주색 그릇', 'Violet Vessel', 8, 60000, True, []),
    ('crimson_heart', '진홍 심장', 'Crimson Heart', 8, 20000, True,
     [E('OnHandPlayed', ALWAYS, O('DisableRandomJoker'), 'RandomJoker')]),
    ('cerulean_bell', '하늘빛 종', 'Cerulean Bell', 8, 20000, True,
     [E('Passive', ALWAYS, RULE('ForceCardSelected'))]),
]


def seed():
    assert len(BOSS) == 28, '보스가 %d종입니다' % len(BOSS)

    write('Ante', table(
        'Ante(key=ante)',
        '안테별 기준 점수입니다. 스테이크가 어느 열을 읽는지는 `Stake` 가 정합니다. '
        '안테 9 이상은 `Const_Run` 의 식으로 계산합니다.',
        ['ante', 'base_white', 'base_green', 'base_purple'],
        ['int (min=0, max=8)', 'int (min=1)', 'int (min=1)', 'int (min=1)'],
        ['안테', '흰색~검은 스테이크', '초록~파란 스테이크', '보라~황금 스테이크'],
        [list(a) for a in ANTE]))

    write('Blind', table(
        'Blind(key=blind)',
        '안테 하나의 세 라운드입니다. 보스는 배율을 `BossBlind` 가 덮어씁니다.',
        ['blind', 'name', 'score_mul', 'reward', 'skippable'],
        ['BlindKind', 'string (text=Blind)', 'int (min=1)', 'int (min=0)', 'bool'],
        ['블라인드', '표시 이름', '기준 점수의 배율. 만분율', '격파 보상', '스킵할 수 있는가'],
        [list(b) for b in BLIND]))

    write('BossBlind', table(
        'BossBlind(key=boss_id)',
        '보스 28종입니다. 최종 보스 5종은 안테 8의 배수에서만 나옵니다.',
        ['boss_id', 'name', 'min_ante', 'score_mul', 'is_showdown', 'sort_order'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Boss)', 'int (min=1, max=8)',
         'int (min=1)', 'bool', 'int (min=1, max=28)'],
        ['식별자', '표시 이름', '나올 수 있는 가장 이른 안테', '기준 점수의 배율. 만분율',
         '최종 보스인가', '수집 목록에서의 순서'],
        [[b[0], b[1], b[3], b[4], b[5], i + 1] for i, b in enumerate(BOSS)]))

    effect_grid('BossEffect', 'foreign BossBlind',
                '보스가 바꾸는 규칙입니다. 규칙만 바꾸고 점수는 배율이 정합니다.',
                [(b[0], b[6]) for b in BOSS])
    return 4
