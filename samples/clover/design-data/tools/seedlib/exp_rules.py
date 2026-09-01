# -*- coding: utf-8 -*-
"""확장 조커 — 판의 규칙을 다루는 계열 3개, 75종.

|계열|종수|무엇을 하는 편성|
|--|--|--|
|상점|25|사는 방식을 바꾸는 것|
|규칙|25|판의 규칙을 비트는 것|
|진행|25|보스와 안테와 블라인드를 다루는 것|

**`RuleKind` 51개가 이 계열의 재료입니다.** 기본 150종이 쓰는 것은 그중 20개 남짓이고,
나머지가 여기 들어옵니다.

값의 단위가 규칙마다 다릅니다 — `BlindSizeScale` 은 만분율이고(`-1000` 이 「-10%」),
`ShopDiscount` 는 금액이고, `FlushStraightCards` 는 장수입니다. **`absolute` 가 붙은 것은
더하지 않고 그 값으로 정합니다.**
"""

from .grid import (AC, ALWAYS, AM, C, E, FACE, GROW, MONEY, O, PER, RULE, XM, j)


# ---------------------------------------------------------------------------
# 상점 25종 — 수레 · 간판 · 손님
# ---------------------------------------------------------------------------
#
# 상점을 바꾸는 것은 **다음 상점에서야 값이 나옵니다.** 그래서 이 계열은 사는 시점의 판단이
# 다른 계열과 다릅니다 — 지금 점수가 부족하면 사지 못하는 것들입니다.

SHOP = [
    j('hand_cart', '손수레', 'Hand Cart', 'Common', 5,
      [E('Passive', ALWAYS, RULE('ShopCardSlots', 1))], blueprint=False),
    j('paint_sign', '그린 간판', 'Paint Sign', 'Common', 4,
      [E('Passive', ALWAYS, RULE('ShopDiscount', 1))], blueprint=False),
    j('free_wheel', '헛바퀴', 'Free Wheel', 'Common', 4,
      [E('Passive', ALWAYS, RULE('RerollStartsFree'))], blueprint=False),
    j('regular', '단골', 'Regular', 'Common', 5,
      [E('OnShopEnter', ALWAYS, GROW('Chips', 10), 'SelfTarget')]),
    j('barrow_boy', '수레 끄는 아이', 'Barrow Boy', 'Common', 4,
      [E('OnReroll', ALWAYS, GROW('Chips', 8), 'SelfTarget')]),
    j('chalk_board', '칠판', 'Chalk Board', 'Common', 5,
      [E('Passive', ALWAYS, RULE('ShopAllowsPlayingCards'))], blueprint=False),
    j('paper_bag', '종이 봉지', 'Paper Bag', 'Common', 4,
      [E('OnShopExit', ALWAYS, MONEY(3))]),
    j('window_shopper', '구경꾼', 'Window Shopper', 'Common', 5,
      [E('OnPackSkipped', ALWAYS, MONEY(3))]),
    j('till_bell', '계산대 종', 'Till Bell', 'Common', 5,
      [E('OnReroll', ALWAYS, MONEY(1))]),

    j('two_carts', '두 수레', 'Two Carts', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('ShopCardSlots', 2))], blueprint=False),
    j('coupon_book', '할인 책', 'Coupon Book', 'Uncommon', 7,
      [E('OnBossDefeated', ALWAYS, RULE('NextShopFree'))], blueprint=False),
    j('sale_board', '할인 간판', 'Sale Board', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('ShopDiscount', 2))], blueprint=False),
    j('modifier_case', '성질 상자', 'Modifier Case', 'Uncommon', 8,
      [E('Passive', ALWAYS, RULE('ShopCardsHaveModifiers'))], blueprint=False),
    j('crier', '외치는 이', 'Crier', 'Uncommon', 6,
      [E('OnShopEnter', ALWAYS, O('ShopGift', create='Tarot', count=1, free=True))],
      blueprint=False),
    j('porter', '짐꾼', 'Porter', 'Uncommon', 7,
      [E('OnShopEnter', ALWAYS,
         O('ShopGift', create='PlayingCard', count=1, free=True))], blueprint=False),
    j('dealer_hand', '딜러의 손', 'Dealer Hand', 'Uncommon', 8,
      [E('OnBossDefeated', ALWAYS,
         O('ShopGift', create='Joker', rarity='Rare', count=1))], blueprint=False),
    j('spendthrift', '낭비꾼', 'Spendthrift', 'Uncommon', 6,
      [E('OnShopExit', ALWAYS, GROW('MultMul', 800), 'SelfTarget')]),
    j('reroll_rig', '리롤 장치', 'Reroll Rig', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('FreeRerolls', 2))], blueprint=False),
    j('night_market', '밤 장터', 'Night Market', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('RerollCostDelta', -4))], blueprint=False),
    j('foil_stall', '박 좌판', 'Foil Stall', 'Uncommon', 8,
      [E('OnShopEnter', ALWAYS,
         O('ShopGift', create='Joker', edition='Foil', count=1))], blueprint=False),

    j('grand_bazaar', '큰 장', 'Grand Bazaar', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('ShopCardSlots', 3)),
       E('Passive', ALWAYS, RULE('ShopDiscount', 2))], blueprint=False),
    j('free_market', '자유 시장', 'Free Market', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('FreeRerolls', 4))], blueprint=False),
    j('patron', '후원자', 'Patron', 'Rare', 10,
      [E('OnShopEnter', ALWAYS,
         O('ShopGift', create='Voucher', count=1, free=True))], blueprint=False),
    j('gilt_cart', '금박 수레', 'Gilt Cart', 'Rare', 9,
      [E('OnBlindSelect', ALWAYS,
         O('ShopGift', create='Joker', edition='Negative', count=1))],
      blueprint=False),

    j('high_street', '큰 거리', 'High Street', 'Legendary', 10,
      [E('Passive', ALWAYS, RULE('ShopCardSlots', 2)),
       E('Passive', ALWAYS, RULE('FreeRerolls', 3)),
       E('OnShopEnter', ALWAYS,
         O('ShopGift', create='Joker', rarity='Rare', count=1, free=True))],
      blueprint=False),
]


# ---------------------------------------------------------------------------
# 규칙 25종 — 안개 · 꿈 · 뒤틀림
# ---------------------------------------------------------------------------
#
# 두 갈래입니다 — **판정의 문턱을 낮추는 것**(`FlushStraightCards` · `StraightGap` ·
# `SuitsMerged`)과 **라운드마다 바뀌는 지정 대상을 읽는 것**(`TargetMatch`)입니다.
#
# 문턱을 낮추는 것들은 값이 크고 희귀도가 높습니다. **2장으로 플러시가 되는 판은 다른
# 게임이 되므로** 그것이 레어의 자리입니다.

RULES = [
    j('fog_bank', '안개 띠', 'Fog Bank', 'Common', 5,
      [E('Passive', ALWAYS, RULE('AlwaysDrawThree'))], blueprint=False),
    j('even_scales', '고른 저울', 'Even Scales', 'Common', 5,
      [E('Passive', ALWAYS, RULE('BalanceChipsAndMult'))], blueprint=False),
    j('half_light', '반쪽 빛', 'Half Light', 'Common', 3,
      [E('Passive', ALWAYS, RULE('HalveBaseChipsAndMult')),
       E('OnHandPlayed', ALWAYS, XM(3))]),
    j('dream_pane', '꿈 유리', 'Dream Pane', 'Common', 5,
      [E('Passive', ALWAYS, RULE('ForceCardSelected'))], blueprint=False),
    j('gap_stone', '틈 돌', 'Gap Stone', 'Common', 5,
      [E('Passive', ALWAYS, RULE('StraightGap', 2, absolute=True))], blueprint=False),
    j('crooked_pane', '굽은 유리', 'Crooked Pane', 'Common', 5,
      [E('OnHandPlayed', C('CardCount', n=3, compare='Exactly'), XM(2))]),
    j('omen_slate', '조짐 서판', 'Omen Slate', 'Common', 4,
      [E('OnCardScored', C('TargetMatch', target='Card'), AC(80))]),
    j('sign_reader', '조짐 읽는 이', 'Sign Reader', 'Common', 5,
      [E('OnHandPlayed', C('TargetMatch', target='Suit'), AM(12))]),

    j('three_line', '셋 줄', 'Three Line', 'Uncommon', 8,
      [E('Passive', ALWAYS, RULE('FlushStraightCards', 3, absolute=True))],
      blueprint=False),
    j('weighted_air', '무게 실은 공기', 'Weighted Air', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('ProbabilityScale', 3, absolute=True))],
      blueprint=False),
    j('dream_ladder', '꿈 사다리', 'Dream Ladder', 'Uncommon', 7,
      [E('OnHandPlayed', C('TargetMatch', target='Hand'), XM(2.5))]),
    j('rank_omen', '랭크 조짐', 'Rank Omen', 'Uncommon', 6,
      [E('OnCardScored', C('TargetMatch', target='Rank'), AM(10))]),
    j('mist_gate', '안개 문', 'Mist Gate', 'Uncommon', 7,
      [E('OnBlindSelect', C('BlindKind', blind='Boss'), GROW('MultMul', 3000),
         'SelfTarget')]),
    j('small_hours', '이른 시간', 'Small Hours', 'Uncommon', 6,
      [E('OnBlindSelect', C('BlindKind', blind='Small'), MONEY(6))]),
    j('big_hours', '늦은 시간', 'Big Hours', 'Uncommon', 6,
      [E('OnHandPlayed', C('BlindKind', blind='Big'), XM(2))]),
    j('sixth_sense', '여섯째 감각', 'Sixth Sense', 'Uncommon', 7,
      [E('OnHandPlayed', C('EveryNHands', n=4),
         O('CreateCard', create='Spectral', count=1))]),
    j('bent_ladder', '굽은 사다리', 'Bent Ladder', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('StraightGap', 3, absolute=True))], blueprint=False),
    j('waking_dream', '깬 꿈', 'Waking Dream', 'Uncommon', 8,
      [E('OnRoundStart', ALWAYS, O('ModifyCard', modify='Edition', edition='Foil'),
         'RandomInHand')]),
    j('long_odds', '긴 승산', 'Long Odds', 'Uncommon', 8,
      [E('OnHandPlayed', ALWAYS, O('RandomRange', mode='AddChips', min=20, max=300))]),

    j('broken_rule', '부러진 자', 'Broken Rule', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('SuitsMerged')),
       E('Passive', ALWAYS, RULE('StraightGap', 1, absolute=True))],
      blueprint=False),
    j('dream_engine', '꿈 기관', 'Dream Engine', 'Rare', 10,
      [E('Passive', ALWAYS, RULE('HalveBaseChipsAndMult')),
       E('OnHandPlayed', ALWAYS, XM(8))]),
    j('all_face', '온 얼굴', 'All Face', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('AllCardsAreFace')),
       E('OnCardScored', FACE, AM(6))]),
    j('omen_crown', '조짐 화관', 'Omen Crown', 'Rare', 9,
      [E('OnCardScored', C('TargetMatch', target='Card'), XM(2))]),
    j('two_line', '둘 줄', 'Two Line', 'Rare', 10,
      [E('Passive', ALWAYS, RULE('FlushStraightCards', 2, absolute=True))],
      blueprint=False),

    j('waking_world', '깬 세계', 'Waking World', 'Legendary', 10,
      [E('Passive', ALWAYS, RULE('SuitsMerged')),
       E('Passive', ALWAYS, RULE('AllCardsAreFace')),
       E('Passive', ALWAYS, RULE('StraightGap', 2, absolute=True))],
      blueprint=False),
]


# ---------------------------------------------------------------------------
# 진행 25종 — 문 · 자물쇠 · 계절
# ---------------------------------------------------------------------------
#
# 이 계열만이 **판을 쉽게 만듭니다.** 점수를 늘리는 대신 요구를 줄이거나 보스를 다시 뽑거나
# 안테를 늦춥니다 — 다른 계열의 조커가 부족할 때 그것을 메우는 자리입니다.

PROGRESSION = [
    j('iron_key', '무쇠 열쇠', 'Iron Key', 'Common', 5,
      [E('OnBossDefeated', ALWAYS, MONEY(8))]),
    j('brass_lock', '놋 자물쇠', 'Brass Lock', 'Common', 5,
      [E('OnHandPlayed', C('BlindKind', blind='Boss'), AM(15))]),
    j('spring_gate', '봄 문', 'Spring Gate', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, PER('BlindsSkipped', 'AddChips', 25))]),
    j('autumn_gate', '가을 문', 'Autumn Gate', 'Common', 5,
      [E('OnPackSkipped', ALWAYS,
         O('CreateCard', create='Tag', count=1, ref_id='uncommon'))]),
    j('winter_gate', '겨울 문', 'Winter Gate', 'Common', 5,
      [E('OnBossDefeated', ALWAYS, GROW('Chips', 25), 'SelfTarget')]),
    j('summer_gate', '여름 문', 'Summer Gate', 'Common', 4,
      [E('OnRoundStart', C('BlindKind', blind='Small'), MONEY(4))]),
    j('ward_stone', '지킴돌', 'Ward Stone', 'Common', 5,
      [E('OnHandPlayed', C('BossTriggered'), AM(20))]),
    j('door_chime', '문 종', 'Door Chime', 'Common', 4,
      [E('OnBlindSelect', ALWAYS, GROW('Chips', 6), 'SelfTarget')]),

    j('boss_key', '보스 열쇠', 'Boss Key', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('BossRerollsPerAnte', 1))], blueprint=False),
    j('double_gate', '겹문', 'Double Gate', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('DoubleTagOnBossDefeat'))], blueprint=False),
    j('season_ring', '계절 고리', 'Season Ring', 'Uncommon', 7,
      [E('OnBossDefeated', ALWAYS, GROW('MultMul', 1500), 'SelfTarget')]),
    j('skeleton_key', '만능 열쇠', 'Skeleton Key', 'Uncommon', 8,
      [E('OnBlindSelect', C('BlindKind', blind='Boss'), O('RerollBoss'))],
      blueprint=False),
    j('toll_house', '통행 집', 'Toll House', 'Uncommon', 6,
      [E('OnBossDefeated', ALWAYS,
         O('CreateCard', create='Tag', count=1, ref_id='rare'))]),
    j('open_gate', '열린 문', 'Open Gate', 'Uncommon', 7,
      [E('OnPackSkipped', ALWAYS, GROW('MultMul', 1500), 'SelfTarget')]),
    j('lock_pick', '자물쇠 따개', 'Lock Pick', 'Uncommon', 7,
      [E('OnBossDefeated', ALWAYS, O('DuplicateNextTag'))], blueprint=False),
    j('harvest_gate', '추수 문', 'Harvest Gate', 'Uncommon', 8,
      [E('OnBossDefeated', ALWAYS,
         O('CreateCard', create='Joker', count=1, rarity='Uncommon'))]),
    j('small_door', '작은 문', 'Small Door', 'Uncommon', 6,
      [E('OnHandPlayed', C('BlindKind', blind='Small'), XM(2.5))]),
    j('ante_stone', '안테 돌', 'Ante Stone', 'Uncommon', 8,
      [E('Passive', ALWAYS, RULE('BlindSizeScale', -1000))], blueprint=False),

    j('great_key', '큰 열쇠', 'Great Key', 'Rare', 9,
      [E('OnBossDefeated', ALWAYS, GROW('MultMul', 4000), 'SelfTarget')]),
    j('boss_ward', '보스 지킴', 'Boss Ward', 'Rare', 9,
      [E('OnHandPlayed', C('BossTriggered'), XM(4))]),
    j('season_wheel', '계절 바퀴', 'Season Wheel', 'Rare', 10,
      [E('Passive', ALWAYS, RULE('AnteDelta', -1))], blueprint=False),
    j('iron_gate', '무쇠 문', 'Iron Gate', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('BlindSizeScale', -2000))], blueprint=False),
    j('warden', '문지기', 'Warden', 'Rare', 10,
      [E('Passive', ALWAYS, RULE('BossRerollsPerAnte', 3))], blueprint=False),

    j('year_gate', '해의 문', 'Year Gate', 'Legendary', 10,
      [E('OnBossDefeated', ALWAYS, GROW('MultMul', 10000), 'SelfTarget')]),
    j('last_door', '마지막 문', 'Last Door', 'Legendary', 10,
      [E('Passive', ALWAYS, RULE('AnteDelta', -1)),
       E('Passive', ALWAYS, RULE('BlindSizeScale', -1500))], blueprint=False),
]


FAMILIES = [
    ('상점', SHOP),
    ('규칙', RULES),
    ('진행', PROGRESSION),
]
