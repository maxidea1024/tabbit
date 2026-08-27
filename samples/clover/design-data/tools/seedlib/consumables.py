# -*- coding: utf-8 -*-
"""Consumables.xlsx — 타로 22종 · 행성 12종 · 유령 18종.

이름은 대아르카나와 천체의 이름이므로 그대로 씁니다. 원작이 만든 이름이 아닙니다.
수치의 출처는 `doc/parity/consumables.md` 입니다.
"""

from .grid import ALWAYS, C, E, MONEY, O, PER, RULE, effect_grid, table, write


def use(op, scope='Run', count=None, chance=None):
    """소모품의 효과는 전부 `OnUse` 입니다."""
    return E('OnUse', ALWAYS, op, scope, count, chance)


def enhance(kind, count):
    return use(O('ModifyCard', modify='Enhancement', enhancement=kind), 'Selected', count)


def to_suit(suit):
    return use(O('ModifyCard', modify='Suit', suit=suit), 'Selected', 3)


def seal(kind):
    return use(O('ModifyCard', modify='Seal', seal=kind), 'Selected', 1)


TAROT = [
    # 식별자, 표시 이름, 영문 이름, 효과
    ('the_fool', '바보', 'The Fool',
     [use(O('CreateCard', create='LastUsed', count=1))]),
    ('the_magician', '마법사', 'The Magician', [enhance('Lucky', 2)]),
    ('the_high_priestess', '고위 여사제', 'The High Priestess',
     [use(O('CreateCard', create='Planet', count=2, random=True))]),
    ('the_empress', '여제', 'The Empress', [enhance('Mult', 2)]),
    ('the_emperor', '황제', 'The Emperor',
     [use(O('CreateCard', create='Tarot', count=2, random=True))]),
    ('the_hierophant', '교황', 'The Hierophant', [enhance('Bonus', 2)]),
    ('the_lovers', '연인', 'The Lovers', [enhance('Wild', 1)]),
    ('the_chariot', '전차', 'The Chariot', [enhance('Steel', 1)]),
    ('justice', '정의', 'Justice', [enhance('Glass', 1)]),
    ('the_hermit', '은둔자', 'The Hermit',
     [use(O('MulMoney', value=20000, cap=20))]),
    ('the_wheel_of_fortune', '운명의 수레바퀴', 'The Wheel of Fortune',
     [use(O('ModifyJoker', random=True), 'RandomJoker', chance=(1, 4))]),
    ('strength', '힘', 'Strength',
     [use(O('ModifyCard', modify='RankStep', value=1), 'Selected', 2)]),
    ('the_hanged_man', '매달린 사람', 'The Hanged Man',
     [use(O('DestroyCard', count=2), 'Selected', 2)]),
    ('death', '죽음', 'Death',
     [use(O('ModifyCard', modify='CopyRight'), 'Selected', 2)]),
    ('temperance', '절제', 'Temperance',
     [use(PER('OtherJokerSellValue', 'AddMoney', 1, cap=50))]),
    ('the_devil', '악마', 'The Devil', [enhance('Gold', 1)]),
    ('the_tower', '탑', 'The Tower', [enhance('Stone', 1)]),
    ('the_star', '별', 'The Star', [to_suit('Diamond')]),
    ('the_moon', '달', 'The Moon', [to_suit('Club')]),
    ('the_sun', '태양', 'The Sun', [to_suit('Heart')]),
    ('judgement', '심판', 'Judgement',
     [use(O('CreateCard', create='Joker', count=1, random=True))]),
    ('the_world', '세계', 'The World', [to_suit('Spade')]),
]

PLANET = [
    # 식별자, 표시 이름, 영문 이름, 올리는 족보
    ('pluto', '명왕성', 'Pluto', 'HighCard'),
    ('mercury', '수성', 'Mercury', 'Pair'),
    ('uranus', '천왕성', 'Uranus', 'TwoPair'),
    ('venus', '금성', 'Venus', 'ThreeOfAKind'),
    ('saturn', '토성', 'Saturn', 'Straight'),
    ('jupiter', '목성', 'Jupiter', 'Flush'),
    ('earth', '지구', 'Earth', 'FullHouse'),
    ('mars', '화성', 'Mars', 'FourOfAKind'),
    ('neptune', '해왕성', 'Neptune', 'StraightFlush'),
    ('planet_x', '플래닛 X', 'Planet X', 'FiveOfAKind'),
    ('ceres', '세레스', 'Ceres', 'FlushHouse'),
    ('eris', '에리스', 'Eris', 'FlushFive'),
]

SPECTRAL = [
    ('familiar', '사역마', 'Familiar',
     [use(O('DestroyCard', count=1), 'RandomInHand', 1),
      use(O('AddCard', create='PlayingCard', count=3, card_class='Face', random=True),
          'AllInHand')]),
    ('grim', '암울', 'Grim',
     [use(O('DestroyCard', count=1), 'RandomInHand', 1),
      use(O('AddCard', create='PlayingCard', count=2, card_class='Ace', random=True),
          'AllInHand')]),
    ('incantation', '주문', 'Incantation',
     [use(O('DestroyCard', count=1), 'RandomInHand', 1),
      use(O('AddCard', create='PlayingCard', count=4, card_class='Numbered', random=True),
          'AllInHand')]),
    ('talisman', '부적', 'Talisman', [seal('Gold')]),
    ('aura', '아우라', 'Aura',
     [use(O('ModifyCard', modify='Edition', random=True), 'Selected', 1)]),
    ('wraith', '망령', 'Wraith',
     [use(O('CreateCard', create='Joker', count=1, rarity='Rare', random=True)),
      use(O('SetMoney', money=0))]),
    ('sigil', '상징', 'Sigil',
     [use(O('ModifyCard', modify='Suit', random=True), 'AllInHand')]),
    ('ouija', '점판', 'Ouija',
     [use(O('ModifyCard', modify='RankTo', random=True), 'AllInHand'),
      use(RULE('HandSize', -1))]),
    ('ectoplasm', '심령체', 'Ectoplasm',
     [use(O('ModifyJoker', edition='Negative'), 'RandomJoker'),
      use(RULE('HandSize', -1))]),
    ('immolate', '화형', 'Immolate',
     [use(O('DestroyCard', count=5), 'RandomInHand', 5),
      use(MONEY(20))]),
    ('ankh', '앙크', 'Ankh',
     [use(O('CopyJoker', pick='Random'), 'RandomJoker'),
      use(O('DestroyJoker', pick='AllOther'), 'AllOtherJokers')]),
    ('deja_vu', '데자뷰', 'Deja Vu', [seal('Red')]),
    ('hex', '주술', 'Hex',
     [use(O('ModifyJoker', edition='Polychrome'), 'RandomJoker'),
      use(O('DestroyJoker', pick='AllOther'), 'AllOtherJokers')]),
    ('trance', '최면', 'Trance', [seal('Blue')]),
    ('medium', '영매', 'Medium', [seal('Purple')]),
    ('cryptid', '괴물', 'Cryptid',
     [use(O('AddCard', create='CopyOfSelected', count=2), 'Selected', 1)]),
    ('the_soul', '영혼', 'The Soul',
     [use(O('CreateCard', create='Joker', count=1, rarity='Legendary', random=True))]),
    ('black_hole', '블랙홀', 'Black Hole',
     [use(O('LevelUpHand', hand_pick='All', levels=1))]),
]


def seed():
    write('Tarot', table(
        'Tarot(key=tarot_id)',
        '타로 22종입니다. 대아르카나 그대로이고 상점 값은 전부 같습니다.',
        ['tarot_id', 'name', 'sort_order'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Tarot)', 'int (min=1, max=22)'],
        ['식별자', '표시 이름', '수집 목록에서의 순서'],
        [[t[0], t[1], i + 1] for i, t in enumerate(TAROT)]))

    write('Planet', table(
        'Planet(key=planet_id)',
        '행성 12종입니다. **증분은 여기 없습니다** — 어느 족보를 올리는지만 있고 값은 '
        '`PokerHand` 에 있습니다.',
        ['planet_id', 'name', 'hand', 'sort_order'],
        # 족보 테이블의 키가 enum 이므로 `foreign` 으로 가리킬 수 없습니다 — 도구의 제약이고
        # `doc/tool-findings.md` 에 적어 두었습니다. 값을 enum 으로 들고 있으면 읽는 쪽이
        # `PokerHand` 의 인덱스로 행을 찾습니다.
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Planet)',
         'PokerHandKind', 'int (min=1, max=12)'],
        ['식별자', '표시 이름', '올리는 족보', '수집 목록에서의 순서'],
        [[p[0], p[1], p[3], i + 1] for i, p in enumerate(PLANET)]))

    write('Spectral', table(
        'Spectral(key=spectral_id)',
        '유령 18종입니다. 상점에는 기본적으로 나오지 않습니다.',
        ['spectral_id', 'name', 'sort_order'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Spectral)',
         'int (min=1, max=18)'],
        ['식별자', '표시 이름', '수집 목록에서의 순서'],
        [[s[0], s[1], i + 1] for i, s in enumerate(SPECTRAL)]))

    effect_grid('TarotEffect', 'foreign Tarot', '타로가 하는 일입니다.',
                [(t[0], t[3]) for t in TAROT])
    effect_grid('SpectralEffect', 'foreign Spectral', '유령 카드가 하는 일입니다.',
                [(s[0], s[3]) for s in SPECTRAL])
    return 5
