# -*- coding: utf-8 -*-
"""Setup.xlsx — 덱 15종 · 스테이크 8종 · 태그 24종.

수치의 출처는 `doc/parity/decks-and-stakes.md` 와 `doc/parity/vouchers-and-tags.md` 입니다.
"""

from .grid import ALWAYS, E, MONEY, O, PER, RULE, effect_grid, table, write


def passive(rule, value=1, **kw):
    return E('Passive', ALWAYS, RULE(rule, value, **kw))


def grant(create, ref_id, count=1):
    return E('OnRunStart', ALWAYS, O('Grant', create=create, count=count, ref_id=ref_id))


# 뒷면. 무늬 하나와 색 두 개입니다.
#
# **덱을 고르는 것이 뒷면을 고르는 것이기도 합니다.** 시작 조건은 첫 판에만 눈에 띄지만
# 뒷면은 한 판 내내 손에 들려 있으므로, 무엇을 골랐는지가 화면에 남는 자리는 여기입니다.
#
# 색 이름이 있는 덱 다섯은 무늬를 함께 씁니다 — 붉은 덱과 파란 덱이 서로 다른 무늬를 쓰면
# 그 둘을 가르는 것이 색이라는 것이 흐려집니다. 나머지 열은 저마다의 무늬입니다.
BACK = {
    'red_deck':       ('Classic',   '#f2ece0', '#c0392f'),
    'blue_deck':      ('Classic',   '#f2ece0', '#2f6fc0'),
    'yellow_deck':    ('Classic',   '#f7f0d8', '#c99a2e'),
    'green_deck':     ('Classic',   '#eef2e4', '#3d8b52'),
    'black_deck':     ('Classic',   '#26262c', '#d8d2c4'),
    'magic_deck':     ('Arcane',    '#1e1830', '#d8b45a'),
    'nebula_deck':    ('Starfield', '#141b2e', '#8ea2f5'),
    'ghost_deck':     ('Veil',      '#e6ecf0', '#7f93a8'),
    'abandoned_deck': ('Worn',      '#e2d6bd', '#9a5b3d'),
    'checkered_deck': ('Checker',   '#f2ece0', '#c0392f'),
    'zodiac_deck':    ('Zodiac',    '#1c1830', '#e0b53b'),
    'painted_deck':   ('Brush',     '#f4efe2', '#2f8b8b'),
    'anaglyph_deck':  ('Anaglyph',  '#f2ece0', '#d02f3a'),
    'plasma_deck':    ('Plasma',    '#1a0f18', '#ff6b9d'),
    'erratic_deck':   ('Erratic',   '#f2ece0', '#7a4fd0'),
}

# 식별자, 표시 이름, 영문 이름, 해금 조건, 효과
DECK = [
    ('red_deck', '붉은 덱', 'Red Deck', '처음부터',
     [passive('DiscardsPerRound', 1)]),
    ('blue_deck', '파란 덱', 'Blue Deck', '수집 20종',
     [passive('HandsPerRound', 1)]),
    ('yellow_deck', '노란 덱', 'Yellow Deck', '수집 50종',
     [passive('StartingMoney', 10)]),
    ('green_deck', '초록 덱', 'Green Deck', '수집 75종',
     [passive('NoInterest'), passive('MoneyPerHandLeft', 2),
      passive('MoneyPerDiscardLeft', 1)]),
    ('black_deck', '검은 덱', 'Black Deck', '수집 100종',
     [passive('JokerSlots', 1), passive('HandsPerRound', -1)]),
    ('magic_deck', '마법 덱', 'Magic Deck', '붉은 덱 승리',
     [grant('Voucher', 'crystal_ball'), grant('Tarot', 'the_fool', 2)]),
    ('nebula_deck', '성운 덱', 'Nebula Deck', '파란 덱 승리',
     [grant('Voucher', 'telescope'), passive('ConsumableSlots', -1)]),
    ('ghost_deck', '유령 덱', 'Ghost Deck', '노란 덱 승리',
     [passive('ShopAllowsSpectral'), grant('Spectral', 'hex')]),
    ('abandoned_deck', '버려진 덱', 'Abandoned Deck', '초록 덱 승리',
     [passive('RemoveFaceCards')]),
    ('checkered_deck', '체크무늬 덱', 'Checkered Deck', '검은 덱 승리',
     [E('Passive', ALWAYS, RULE('SuitsOnly', 1, suits=['Spade', 'Heart']))]),
    ('zodiac_deck', '황도대 덱', 'Zodiac Deck', '붉은 스테이크 승리',
     [grant('Voucher', 'tarot_merchant'), grant('Voucher', 'planet_merchant'),
      grant('Voucher', 'overstock')]),
    ('painted_deck', '물감 덱', 'Painted Deck', '초록 스테이크 승리',
     [passive('HandSize', 2), passive('JokerSlots', -1)]),
    ('anaglyph_deck', '입체사진 덱', 'Anaglyph Deck', '검은 스테이크 승리',
     [passive('DoubleTagOnBossDefeat')]),
    ('plasma_deck', '플라스마 덱', 'Plasma Deck', '파란 스테이크 승리',
     [passive('BalanceChipsAndMult'), passive('BlindSizeScale', 20000, absolute=True)]),
    ('erratic_deck', '불규칙한 덱', 'Erratic Deck', '주황 스테이크 승리',
     [passive('RandomizeDeck')]),
]

# 스테이크, 표시 이름, 읽는 안테 열, 스몰 보상, 버리기 증감, 스티커, 확률(백분율)
STAKE = [
    ('White', '흰색', 1, 3, 0, 'None', 0),
    ('Red', '붉은색', 1, 0, 0, 'None', 0),
    ('Green', '초록색', 2, 0, 0, 'None', 0),
    ('Black', '검은색', 2, 0, 0, 'Eternal', 30),
    ('Blue', '파란색', 2, 0, -1, 'Eternal', 30),
    ('Purple', '보라색', 3, 0, -1, 'Eternal', 30),
    ('Orange', '주황색', 3, 0, -1, 'Perishable', 30),
    ('Gold', '황금색', 3, 0, -1, 'Rental', 30),
]

# 식별자, 표시 이름, 영문 이름, 최소 안테, 효과
TAG = [
    ('uncommon', '언커먼 태그', 'Uncommon Tag', 1,
     [E('OnUse', ALWAYS, O('ShopGift', create='Joker', rarity='Uncommon', free=True))]),
    ('rare', '레어 태그', 'Rare Tag', 1,
     [E('OnUse', ALWAYS, O('ShopGift', create='Joker', rarity='Rare', free=True))]),
    ('negative', '네거티브 태그', 'Negative Tag', 2,
     [E('OnUse', ALWAYS,
        O('ShopGift', create='Joker', edition='Negative', free=True))]),
    ('foil', '포일 태그', 'Foil Tag', 1,
     [E('OnUse', ALWAYS, O('ShopGift', create='Joker', edition='Foil', free=True))]),
    ('holographic', '홀로그래픽 태그', 'Holographic Tag', 1,
     [E('OnUse', ALWAYS,
        O('ShopGift', create='Joker', edition='Holographic', free=True))]),
    ('polychrome', '폴리크롬 태그', 'Polychrome Tag', 1,
     [E('OnUse', ALWAYS,
        O('ShopGift', create='Joker', edition='Polychrome', free=True))]),
    ('investment', '투자 태그', 'Investment Tag', 1,
     [E('OnBossDefeated', ALWAYS, MONEY(25))]),
    ('voucher', '바우처 태그', 'Voucher Tag', 1,
     [E('OnUse', ALWAYS, O('ShopGift', create='Voucher', count=1))]),
    ('boss', '보스 태그', 'Boss Tag', 1,
     [E('OnUse', ALWAYS, O('RerollBoss'))]),
    ('standard', '스탠다드 태그', 'Standard Tag', 2,
     [E('OnUse', ALWAYS,
        O('CreateCard', create='Pack', count=1, ref_id='standard_mega'))]),
    ('charm', '부적 태그', 'Charm Tag', 1,
     [E('OnUse', ALWAYS, O('CreateCard', create='Pack', count=1, ref_id='arcana_mega'))]),
    ('meteor', '유성 태그', 'Meteor Tag', 2,
     [E('OnUse', ALWAYS,
        O('CreateCard', create='Pack', count=1, ref_id='celestial_mega'))]),
    ('buffoon', '어릿광대 태그', 'Buffoon Tag', 2,
     [E('OnUse', ALWAYS,
        O('CreateCard', create='Pack', count=1, ref_id='buffoon_mega'))]),
    ('ethereal', '무형 태그', 'Ethereal Tag', 2,
     [E('OnUse', ALWAYS,
        O('CreateCard', create='Pack', count=1, ref_id='spectral_normal'))]),
    ('handy', '손재주 태그', 'Handy Tag', 2,
     [E('OnUse', ALWAYS, PER('HandsPlayedThisRun', 'AddMoney', 1))]),
    ('garbage', '쓰레기 태그', 'Garbage Tag', 2,
     [E('OnUse', ALWAYS, PER('DiscardsUnusedThisRun', 'AddMoney', 1))]),
    ('coupon', '쿠폰 태그', 'Coupon Tag', 1,
     [E('OnUse', ALWAYS, RULE('NextShopFree', 1, duration='NextRound'))]),
    ('d6', 'D6 태그', 'D6 Tag', 1,
     [E('OnUse', ALWAYS, RULE('RerollStartsFree', 1, duration='NextRound'))]),
    ('double', '더블 태그', 'Double Tag', 1,
     [E('OnUse', ALWAYS, O('DuplicateNextTag'))]),
    ('juggle', '저글 태그', 'Juggle Tag', 1,
     [E('OnUse', ALWAYS, RULE('HandSize', 3, duration='NextRound'))]),
    ('economy', '경제 태그', 'Economy Tag', 1,
     [E('OnUse', ALWAYS, O('MulMoney', value=20000, cap=40))]),
    ('speed', '속도 태그', 'Speed Tag', 1,
     [E('OnUse', ALWAYS, PER('BlindsSkipped', 'AddMoney', 5))]),
    ('orbital', '궤도 태그', 'Orbital Tag', 2,
     [E('OnUse', ALWAYS, O('LevelUpHand', hand_pick='Random', levels=3))]),
    ('topup', '보충 태그', 'Top-up Tag', 2,
     [E('OnUse', ALWAYS, O('CreateCard', create='Joker', count=2, rarity='Common'))]),
]


def seed():
    assert len(DECK) == 15, '덱이 %d종입니다' % len(DECK)
    assert len(TAG) == 24, '태그가 %d종입니다' % len(TAG)
    missing = [d[0] for d in DECK if d[0] not in BACK]
    assert not missing, '뒷면이 없는 덱: %s' % ', '.join(missing)

    write('Deck', table(
        'Deck(key=deck_id)',
        '런의 시작 조건입니다. 카드 52장 자체를 바꾸는 덱과 자원을 바꾸는 덱이 갈립니다. '
        '뒷면도 여기 있습니다 — 덱이 정하는 것 중 한 판 내내 보이는 유일한 것입니다.',
        ['deck_id', 'name', 'unlock', 'sort_order', 'back', 'back_ground', 'back_ink'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Deck)', 'string',
         'int (min=1, max=15)', 'CardBackKind',
         'string (regex="^#[0-9a-f]{6}$")', 'string (regex="^#[0-9a-f]{6}$")'],
        ['식별자', '표시 이름', '해금 조건', '고르는 화면에서의 순서', '뒷면의 무늬',
         '뒷면의 바탕색', '뒷면의 선 색'],
        [[d[0], d[1], d[3], i + 1, *BACK[d[0]]] for i, d in enumerate(DECK)]))

    write('Stake', table(
        'Stake(key=stake)',
        '난이도입니다. **누적이므로 뒤의 것은 앞의 것을 전부 포함합니다** — 이 표의 값은 '
        '그 스테이크에서의 최종값입니다.',
        ['stake', 'name', 'ante_column', 'small_blind_reward', 'discards_delta',
         'sticker', 'sticker_percent'],
        ['StakeKind', 'string (text=Stake)', 'int (min=1, max=3)', 'int (min=0)', 'int',
         'StickerKind', 'int (min=0, max=100)'],
        ['스테이크', '표시 이름', '`Ante` 의 어느 열을 읽는가', '스몰 블라인드 격파 보상',
         '버리기 증감', '조커에 붙는 스티커', '스티커가 붙을 확률'],
        [list(s) for s in STAKE]))

    write('Tag', table(
        'Tag(key=tag_id)',
        '블라인드를 스킵하면 받는 것입니다. 즉시 발동하거나 다음 상점까지 기다립니다.',
        ['tag_id', 'name', 'min_ante', 'sort_order'],
        ['string (regex="^[a-z][a-z0-9]*$")', 'string (text=Tag)', 'int (min=1, max=8)',
         'int (min=1, max=24)'],
        ['식별자', '표시 이름', '나올 수 있는 가장 이른 안테', '수집 목록에서의 순서'],
        [[t[0], t[1], t[3], i + 1] for i, t in enumerate(TAG)]))

    effect_grid('DeckEffect', 'foreign Deck', '덱이 정하는 시작 조건입니다.',
                [(d[0], d[4]) for d in DECK])
    effect_grid('TagEffect', 'foreign Tag', '태그가 하는 일입니다.',
                [(t[0], t[4]) for t in TAG])
    return 5
