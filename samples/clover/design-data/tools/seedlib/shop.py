# -*- coding: utf-8 -*-
"""Shop.xlsx — 상점 구성 · 팩 15종 · 바우처 32종 · 리롤 비용.

수치의 출처는 `doc/parity/economy-and-shop.md` 와 `doc/parity/vouchers-and-tags.md` 입니다.
"""

from .grid import ALWAYS, E, O, RULE, effect_grid, table, write

# 카드 칸 추첨의 가중치. 합이 28이므로 조커가 71.4% 입니다.
SLOT_WEIGHT = [
    ('Joker', 20, '희귀도는 `JokerRarityWeight` 가 다시 뽑습니다'),
    ('Tarot', 4, '`Tarot Merchant` 계열이 배로 늘립니다'),
    ('Planet', 4, '`Planet Merchant` 계열이 배로 늘립니다'),
    ('PlayingCard', 0, '`Magic Trick` 이 켭니다'),
    ('Spectral', 0, '유령 덱이 켭니다'),
]

# 팩, 크기, 값, 장수, 고르는 수
PACK = [
    ('Arcana', 'Normal', 4, 3, 1), ('Arcana', 'Jumbo', 6, 5, 1), ('Arcana', 'Mega', 8, 5, 2),
    ('Celestial', 'Normal', 4, 3, 1), ('Celestial', 'Jumbo', 6, 5, 1),
    ('Celestial', 'Mega', 8, 5, 2),
    ('Standard', 'Normal', 4, 3, 1), ('Standard', 'Jumbo', 6, 5, 1),
    ('Standard', 'Mega', 8, 5, 2),
    ('Buffoon', 'Normal', 4, 2, 1), ('Buffoon', 'Jumbo', 6, 4, 1),
    ('Buffoon', 'Mega', 8, 4, 2),
    ('Spectral', 'Normal', 4, 2, 1), ('Spectral', 'Jumbo', 6, 4, 1),
    ('Spectral', 'Mega', 8, 4, 2),
]

# 식별자, 표시 이름, 영문 이름, 상위가 잇는 하위, 효과
VOUCHER = [
    ('overstock', '재고 과잉', 'Overstock', None,
     [E('Passive', ALWAYS, RULE('ShopCardSlots', 1))]),
    ('overstock_plus', '재고 과잉 플러스', 'Overstock Plus', 'overstock',
     [E('Passive', ALWAYS, RULE('ShopCardSlots', 1))]),
    ('clearance_sale', '떨이', 'Clearance Sale', None,
     [E('Passive', ALWAYS, RULE('ShopDiscount', 25, absolute=True))]),
    ('liquidation', '창고 정리', 'Liquidation', 'clearance_sale',
     [E('Passive', ALWAYS, RULE('ShopDiscount', 50, absolute=True))]),
    ('hone', '갈기', 'Hone', None,
     [E('Passive', ALWAYS, RULE('EditionWeightScale', 2, absolute=True))]),
    ('glow_up', '광내기', 'Glow Up', 'hone',
     [E('Passive', ALWAYS, RULE('EditionWeightScale', 4, absolute=True))]),
    ('reroll_surplus', '리롤 여유', 'Reroll Surplus', None,
     [E('Passive', ALWAYS, RULE('RerollCostDelta', -2))]),
    ('reroll_glut', '리롤 과잉', 'Reroll Glut', 'reroll_surplus',
     [E('Passive', ALWAYS, RULE('RerollCostDelta', -2))]),
    ('crystal_ball', '수정 구슬', 'Crystal Ball', None,
     [E('Passive', ALWAYS, RULE('ConsumableSlots', 1))]),
    ('omen_globe', '조짐 구슬', 'Omen Globe', 'crystal_ball',
     [E('Passive', ALWAYS, RULE('SpectralInArcanaPacks'))]),
    ('telescope', '망원경', 'Telescope', None,
     [E('Passive', ALWAYS, RULE('CelestialHasMostPlayed'))]),
    ('observatory', '천문대', 'Observatory', 'telescope',
     [E('Passive', ALWAYS, RULE('PlanetGivesMult', 15000, absolute=True))]),
    ('grabber', '집게', 'Grabber', None,
     [E('Passive', ALWAYS, RULE('HandsPerRound', 1))]),
    ('nacho_tong', '나초 집게', 'Nacho Tong', 'grabber',
     [E('Passive', ALWAYS, RULE('HandsPerRound', 1))]),
    ('wasteful', '낭비', 'Wasteful', None,
     [E('Passive', ALWAYS, RULE('DiscardsPerRound', 1))]),
    ('recyclomancy', '재활용술', 'Recyclomancy', 'wasteful',
     [E('Passive', ALWAYS, RULE('DiscardsPerRound', 1))]),
    ('tarot_merchant', '타로 상인', 'Tarot Merchant', None,
     [E('Passive', ALWAYS, RULE('ShopWeightTarot', 2, absolute=True))]),
    ('tarot_tycoon', '타로 거상', 'Tarot Tycoon', 'tarot_merchant',
     [E('Passive', ALWAYS, RULE('ShopWeightTarot', 4, absolute=True))]),
    ('planet_merchant', '행성 상인', 'Planet Merchant', None,
     [E('Passive', ALWAYS, RULE('ShopWeightPlanet', 2, absolute=True))]),
    ('planet_tycoon', '행성 거상', 'Planet Tycoon', 'planet_merchant',
     [E('Passive', ALWAYS, RULE('ShopWeightPlanet', 4, absolute=True))]),
    ('seed_money', '종잣돈', 'Seed Money', None,
     [E('Passive', ALWAYS, RULE('InterestCap', 10, absolute=True))]),
    ('money_tree', '돈나무', 'Money Tree', 'seed_money',
     [E('Passive', ALWAYS, RULE('InterestCap', 20, absolute=True))]),
    ('blank', '백지', 'Blank', None,
     [E('Passive', ALWAYS, O('Nothing'))]),
    ('antimatter', '반물질', 'Antimatter', 'blank',
     [E('Passive', ALWAYS, RULE('JokerSlots', 1))]),
    ('magic_trick', '요술', 'Magic Trick', None,
     [E('Passive', ALWAYS, RULE('ShopAllowsPlayingCards'))]),
    ('illusion', '착시', 'Illusion', 'magic_trick',
     [E('Passive', ALWAYS, RULE('ShopCardsHaveModifiers'))]),
    ('hieroglyph', '상형문자', 'Hieroglyph', None,
     [E('Passive', ALWAYS, RULE('AnteDelta', -1)),
      E('Passive', ALWAYS, RULE('HandsPerRound', -1))]),
    ('petroglyph', '암각화', 'Petroglyph', 'hieroglyph',
     [E('Passive', ALWAYS, RULE('AnteDelta', -1)),
      E('Passive', ALWAYS, RULE('DiscardsPerRound', -1))]),
    ('directors_cut', '감독판', "Director's Cut", None,
     [E('Passive', ALWAYS, RULE('BossRerollsPerAnte', 1, absolute=True))]),
    ('retcon', '설정 변경', 'Retcon', 'directors_cut',
     [E('Passive', ALWAYS, RULE('BossRerollsPerAnte', 99, absolute=True))]),
    ('paint_brush', '붓', 'Paint Brush', None,
     [E('Passive', ALWAYS, RULE('HandSize', 1))]),
    ('palette', '팔레트', 'Palette', 'paint_brush',
     [E('Passive', ALWAYS, RULE('HandSize', 1))]),
]

VOUCHER_COST = 10


def seed():
    assert len(VOUCHER) == 32, '바우처가 %d종입니다' % len(VOUCHER)

    write('ShopSlotWeight', table(
        'ShopSlotWeight(key=item)',
        '카드 칸 하나에 무엇이 오는가의 추첨입니다. 가중치의 합으로 나눈 것이 확률입니다.',
        ['item', 'weight', 'note'],
        ['ShopItemKind', 'int (min=0)', 'string'],
        ['무엇이', '가중치', '언제 바뀌는가'],
        [list(w) for w in SLOT_WEIGHT]))

    write('BoosterPack', table(
        'BoosterPack(key=pack_id)',
        '팩 15종입니다. 갈래 5종과 크기 3종의 조합입니다.',
        ['pack_id', 'kind', 'size', 'cost', 'cards', 'picks'],
        ['string (regex="^[a-z_]+$")', 'PackKind', 'PackSize', 'int (min=1)',
         'int (min=1, max=5)', 'int (min=1, max=2)'],
        ['식별자', '갈래', '크기', '값', '들어 있는 장수', '고르는 장수'],
        [['%s_%s' % (p[0].lower(), p[1].lower()), p[0], p[1], p[2], p[3], p[4]]
         for p in PACK]))

    write('Voucher', table(
        'Voucher(key=voucher_id)',
        '바우처 32종입니다. 16쌍이고, 상위는 자기 하위를 산 뒤에만 상점에 나옵니다.',
        ['voucher_id', 'name', 'cost', 'upgrades_from', 'sort_order'],
        ['string (regex="^[a-z][a-z0-9_]*$")', 'string (text=Voucher)', 'int (min=1)',
         'foreign Voucher?', 'int (min=1, max=32)'],
        ['식별자', '표시 이름', '값', '이것이 잇는 하위 바우처', '수집 목록에서의 순서'],
        [[v[0], v[1], VOUCHER_COST, v[3] or '-', i + 1] for i, v in enumerate(VOUCHER)]))

    write('RerollCost', table(
        'RerollCost(key=times)',
        '상점 하나 안에서 리롤한 횟수마다의 값입니다. 상점을 나가면 처음으로 돌아갑니다.',
        ['times', 'cost'],
        ['int (min=0, max=9)', 'int (min=0)'],
        ['이 상점에서 이미 리롤한 횟수', '다음 리롤의 값'],
        [[i, 5 + i] for i in range(10)]))

    effect_grid('VoucherEffect', 'foreign Voucher',
                '바우처가 바꾸는 규칙입니다. 런 전체에 남습니다.',
                [(v[0], v[4]) for v in VOUCHER])
    return 5
