# -*- coding: utf-8 -*-
"""확장 조커 — 값을 만드는 기관 계열 4개, 100종.

|계열|종수|무엇을 하는 편성|
|--|--|--|
|경제|25|돈이 점수가 되고 점수가 돈이 되는 것|
|성장|25|라운드를 넘겨 값을 쌓는 것|
|위험|25|잃을 것을 걸고 크게 얻는 것|
|강화|25|강화 · 인장 · 에디션에 값을 붙이는 것|

**인장과 에디션을 보는 조건은 이 계열이 처음 씁니다.** `CondCardSeal` 과 `CondCardEdition`
은 선언만 있고 기본 150종이 쓰지 않던 것들입니다.
"""

from .grid import (AC, ALWAYS, AM, C, E, FACE, GROW, HC, MONEY, O, PER, RANKS, RULE, XM, j)


# ---------------------------------------------------------------------------
# 경제 25종 — 동전 · 저울 · 장부 · 시장
# ---------------------------------------------------------------------------
#
# 금액을 점수로 바꾸는 단계가 셋입니다 — 커먼은 칩, 레어는 배수 가산, 전설은 배수 곱입니다.
# **같은 자원을 같은 값으로 세 번 팔지 않기 위해** 희귀도마다 환율을 다르게 둡니다.

ECONOMY = [
    j('copper_scale', '구리 저울', 'Copper Scale', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, PER('MoneyPer5', 'AddChips', 12))]),
    j('tin_till', '양철 금고', 'Tin Till', 'Common', 5,
      [E('OnRoundEnd', ALWAYS, PER('HandsLeft', 'AddMoney', 1))]),
    j('debt_slate', '빚 서판', 'Debt Slate', 'Common', 2,
      [E('Passive', ALWAYS, RULE('DebtLimit', -10)),
       E('OnHandPlayed', ALWAYS, AM(6))]),
    j('coin_purse', '돈주머니', 'Coin Purse', 'Common', 5,
      [E('OnBlindSelect', ALWAYS, MONEY(3))]),
    j('market_stall', '장터 좌판', 'Market Stall', 'Common', 5,
      [E('OnShopEnter', ALWAYS, MONEY(2))]),
    j('haggler', '값 깎는 이', 'Haggler', 'Common', 4,
      [E('Passive', ALWAYS, RULE('RerollCostDelta', -2))], blueprint=False),
    j('brass_weight', '놋 분동', 'Brass Weight', 'Common', 5,
      [E('OnHandPlayed', C('Money', n=20, compare='AtLeast'), AM(10))]),
    j('empty_purse', '빈 주머니', 'Empty Purse', 'Common', 4,
      [E('OnHandPlayed', C('Money', n=0, compare='AtMost'), XM(2.5))]),
    j('toll_gate', '통행 문', 'Toll Gate', 'Common', 4,
      [E('OnRoundStart', ALWAYS, MONEY(-2)),
       E('OnHandPlayed', ALWAYS, AM(8))]),

    j('interest_book', '이자 책', 'Interest Book', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('InterestCap', 10))], blueprint=False),
    j('coin_press', '동전 압착기', 'Coin Press', 'Uncommon', 7,
      [E('OnRoundEnd', ALWAYS, O('MulMoney', value=11000))]),
    j('strongbox', '금고', 'Strongbox', 'Uncommon', 7,
      [E('OnRoundEnd', ALWAYS, GROW('Money', 1, init=2), 'SelfTarget'),
       E('OnRoundEnd', ALWAYS, PER('SelfCounterMoney', 'AddMoney', 1))]),
    j('gilt_ledger', '금박 장부', 'Gilt Ledger', 'Uncommon', 8,
      [E('OnHandPlayed', ALWAYS, PER('Money', 'MulMult', 100, base_value=10000))]),
    j('usurer', '돈놀이꾼', 'Usurer', 'Uncommon', 6,
      [E('Passive', ALWAYS, RULE('InterestPer5', 2))], blueprint=False),
    j('spent_note', '쓴 어음', 'Spent Note', 'Uncommon', 6,
      [E('OnShopExit', ALWAYS, GROW('MultAdd', 10000), 'SelfTarget')]),
    j('alms_bowl', '구휼 그릇', 'Alms Bowl', 'Uncommon', 6,
      [E('OnRoundEnd', C('Money', n=4, compare='AtMost'), MONEY(8))]),
    j('weighbridge', '계량대', 'Weighbridge', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS, PER('MoneyPer5', 'MulMult', 1000, base_value=10000))]),
    j('broker', '거래인', 'Broker', 'Uncommon', 7,
      [E('OnJokerSold', ALWAYS, MONEY(5))]),
    j('sealed_chest', '봉한 상자', 'Sealed Chest', 'Uncommon', 8,
      [E('Passive', ALWAYS, RULE('NoInterest')),
       E('OnRoundEnd', ALWAYS, MONEY(12))]),

    j('royal_mint', '왕실 주조소', 'Royal Mint', 'Rare', 10,
      [E('OnHandPlayed', ALWAYS, PER('Money', 'AddMult', 500))]),
    j('debt_spiral', '빚의 나선', 'Debt Spiral', 'Rare', 8,
      [E('Passive', ALWAYS, RULE('DebtLimit', -40)),
       E('OnHandPlayed', C('Money', n=0, compare='AtMost'), XM(5))]),
    j('tithe_barn', '십일조 곳간', 'Tithe Barn', 'Rare', 9,
      [E('OnBossDefeated', ALWAYS, O('MulMoney', value=15000))]),
    j('counting_house', '셈방', 'Counting House', 'Rare', 9,
      [E('OnHandPlayed', ALWAYS, PER('Money', 'AddChips', 5))]),
    j('iron_reserve', '무쇠 준비금', 'Iron Reserve', 'Rare', 8,
      [E('Passive', ALWAYS, RULE('MoneyPerHandLeft', 2))], blueprint=False),

    j('golden_hoard', '황금 무더기', 'Golden Hoard', 'Legendary', 10,
      [E('OnHandPlayed', ALWAYS, PER('Money', 'MulMult', 300, base_value=10000))]),
]


# ---------------------------------------------------------------------------
# 성장 25종 — 뿌리 · 덩굴 · 나이테
# ---------------------------------------------------------------------------
#
# 누적의 연료가 무엇인가로 갈립니다 — 라운드 · 보스 · 덱에 더해지는 카드 · 파괴되는 카드 ·
# 소모품 · 판 것. **연료가 같으면 값이 겹치므로 같은 연료를 두 번 쓰지 않습니다.**
#
# `reset` 이 붙은 것들은 한 라운드짜리이고 그만큼 값이 큽니다.

SCALING = [
    j('tap_root', '곧은 뿌리', 'Tap Root', 'Common', 5,
      [E('OnRoundEnd', ALWAYS, GROW('Chips', 12), 'SelfTarget')]),
    j('year_ring', '나이테', 'Year Ring', 'Common', 5,
      [E('OnBossDefeated', ALWAYS, GROW('MultAdd', 20000), 'SelfTarget')]),
    j('runner_vine', '기는 덩굴', 'Runner Vine', 'Common', 4,
      [E('OnCardAdded', ALWAYS, GROW('Chips', 6), 'SelfTarget')]),
    j('bud_scale', '눈 비늘', 'Bud Scale', 'Common', 4,
      [E('OnBlindSelect', ALWAYS, GROW('MultAdd', 4000), 'SelfTarget')]),
    j('sap_rise', '수액 오름', 'Sap Rise', 'Common', 5,
      [E('OnRoundStart', ALWAYS, GROW('Chips', 8, cap=200), 'SelfTarget')]),
    j('moss_stair', '이끼 계단', 'Moss Stair', 'Common', 4,
      [E('OnScoreResolved', C('CardCount', n=2, compare='AtMost'),
         GROW('MultAdd', 6000), 'SelfTarget')]),
    j('bark_fold', '껍질 주름', 'Bark Fold', 'Common', 5,
      [E('OnScoreResolved', C('LastHand'), GROW('Chips', 15), 'SelfTarget')]),
    j('first_leaf', '첫 잎', 'First Leaf', 'Common', 4,
      [E('OnScoreResolved', C('FirstHand'), GROW('MultAdd', 8000), 'SelfTarget')]),

    j('heartwood', '심재', 'Heartwood', 'Uncommon', 7,
      [E('OnRoundEnd', ALWAYS, GROW('MultMul', 1000), 'SelfTarget')]),
    j('girdle_band', '두름 띠', 'Girdle Band', 'Uncommon', 7,
      [E('OnCardDestroyed', ALWAYS, GROW('MultAdd', 12000), 'SelfTarget')]),
    j('pollard_stump', '잘린 그루', 'Pollard Stump', 'Uncommon', 7,
      [E('OnJokerSold', ALWAYS, GROW('Chips', 30), 'SelfTarget')]),
    j('graft_union', '접합부', 'Graft Union', 'Uncommon', 8,
      [E('OnConsumableUsed', C('ConsumableKind', consumable='Tarot'),
         GROW('MultMul', 800), 'SelfTarget')]),
    j('spore_cloud', '홀씨 구름', 'Spore Cloud', 'Uncommon', 7,
      [E('OnConsumableUsed', C('ConsumableKind', consumable='Spectral'),
         GROW('MultMul', 2500), 'SelfTarget')]),
    j('deep_root', '깊은 뿌리', 'Deep Root', 'Uncommon', 8,
      [E('OnRoundEnd', ALWAYS, GROW('MultAdd', 25000), 'SelfTarget'),
       E('Passive', ALWAYS, RULE('HandSize', -1))]),
    j('climbing_rose', '오르는 장미', 'Climbing Rose', 'Uncommon', 7,
      [E('OnScoreResolved', HC('Straight'), GROW('MultMul', 1200), 'SelfTarget')]),
    j('withy_band', '고리버들 띠', 'Withy Band', 'Uncommon', 6,
      [E('OnScoreResolved', ALWAYS, GROW('MultAdd', 6000, reset='Boss'), 'SelfTarget')]),
    j('knot_wood', '옹이', 'Knot Wood', 'Uncommon', 7,
      [E('OnCardScored', C('CardEnhanced'), GROW('Chips', 6), 'SelfTarget')]),
    j('shade_leaf', '그늘잎', 'Shade Leaf', 'Uncommon', 6,
      [E('OnScoreResolved', C('NoFaceScored'), GROW('Chips', 20), 'SelfTarget')]),
    j('bind_weed', '메꽃', 'Bind Weed', 'Uncommon', 8,
      [E('OnScoreResolved', ALWAYS, GROW('MultMul', 400), 'SelfTarget'),
       E('OnRoundEnd', ALWAYS, GROW('MultMul', -800, floor=10000), 'SelfTarget')]),

    j('old_oak', '늙은 참나무', 'Old Oak', 'Rare', 9,
      [E('OnRoundEnd', ALWAYS, GROW('MultMul', 2500), 'SelfTarget')]),
    j('crown_gall', '뿌리혹', 'Crown Gall', 'Rare', 8,
      [E('OnCardDestroyed', ALWAYS, GROW('MultMul', 2000), 'SelfTarget')]),
    j('wild_stock', '대목', 'Wild Stock', 'Rare', 9,
      [E('OnCardAdded', ALWAYS, GROW('MultAdd', 30000), 'SelfTarget')]),
    j('standing_grove', '선 숲', 'Standing Grove', 'Rare', 9,
      [E('OnHandPlayed', ALWAYS,
         PER('HandsPlayedThisRun', 'MulMult', 100, base_value=10000))]),
    j('everbearing', '사철 열림', 'Everbearing', 'Rare', 10,
      [E('OnScoreResolved', ALWAYS, GROW('MultAdd', 12000, reset='Round'),
         'SelfTarget')]),

    j('world_tree', '세계나무', 'World Tree', 'Legendary', 10,
      [E('OnRoundEnd', ALWAYS, GROW('MultMul', 5000), 'SelfTarget'),
       E('Passive', ALWAYS, RULE('JokerSlots', -1))]),
]


# ---------------------------------------------------------------------------
# 위험 25종 — 가시 · 독 · 서리
# ---------------------------------------------------------------------------
#
# 대가의 종류가 넷입니다 — **사라지는 것** · **줄어드는 것** · **판이 좁아지는 것** ·
# **다른 것을 잃는 것**입니다. 하나에 대가가 둘 붙으면 사지 않으므로 하나씩만 둡니다.

RISK = [
    j('thorn_ring', '가시 고리', 'Thorn Ring', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, XM(2.5)),
       E('OnRoundEnd', ALWAYS, O('DestroyJoker', pick='SelfPick'), 'SelfTarget',
         chance=(1, 8))]),
    j('nightshade_cup', '까마중 잔', 'Nightshade Cup', 'Common', 5,
      [E('OnHandPlayed', ALWAYS, AM(20)),
       E('OnRoundEnd', ALWAYS, GROW('Charge', -1, init=6, floor=0), 'SelfTarget'),
       E('OnRoundEnd', C('CounterAtMost', counter='Charge', n=0),
         O('DestroyJoker', pick='SelfPick'), 'SelfTarget')]),
    j('frost_bite', '서릿발', 'Frost Bite', 'Common', 5,
      [E('OnScoreResolved', ALWAYS, GROW('MultAdd', -5000, init=150000, floor=0),
         'SelfTarget')]),
    j('bee_sting', '벌 침', 'Bee Sting', 'Common', 4,
      [E('OnCardScored', FACE, AM(8)),
       E('OnCardScored', FACE, O('DestroyCard', count=1), 'ScoredCard',
         chance=(1, 10))]),
    j('hemlock_leaf', '독당근 잎', 'Hemlock Leaf', 'Common', 5,
      [E('OnHandPlayed', ALWAYS, AC(200)),
       E('Passive', ALWAYS, RULE('HandsPerRound', -1))]),
    j('cracked_pot', '갈라진 화분', 'Cracked Pot', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, XM(2)),
       E('OnHandPlayed', ALWAYS, O('DestroyCard', count=1), 'RandomInDeck',
         chance=(1, 6))]),
    j('wasp_nest', '말벌집', 'Wasp Nest', 'Common', 5,
      [E('OnBlindSelect', ALWAYS, MONEY(6)),
       E('OnBlindSelect', ALWAYS, O('DestroyCard', count=1), 'RandomInDeck')]),
    j('brittle_glass', '여린 유리', 'Brittle Glass', 'Common', 4,
      [E('OnCardScored', C('CardEnhancement', enhancement='Glass'), AM(10))]),

    j('bloodroot', '피뿌리', 'Bloodroot', 'Uncommon', 7,
      [E('OnRoundStart', ALWAYS, O('DestroyCard', count=1), 'RandomInHand'),
       E('OnHandPlayed', ALWAYS, XM(2.5))]),
    j('viper_coil', '살무사 고리', 'Viper Coil', 'Uncommon', 8,
      [E('OnHandPlayed', C('HandsLeft', n=1, compare='AtMost'), XM(5))]),
    j('dead_wood', '죽은 가지', 'Dead Wood', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS, XM(3)),
       E('Passive', ALWAYS, RULE('HandSize', -2))]),
    j('sacrifice_bowl', '제물 그릇', 'Sacrifice Bowl', 'Uncommon', 7,
      [E('OnBlindSelect', ALWAYS, O('DestroyJoker', pick='Right'), 'JokerRight'),
       E('OnBlindSelect', ALWAYS, GROW('MultMul', 5000), 'SelfTarget')]),
    j('hollow_seed', '빈 씨', 'Hollow Seed', 'Uncommon', 6,
      [E('OnScoreResolved', ALWAYS, GROW('MultMul', 1000), 'SelfTarget'),
       E('OnBossDefeated', ALWAYS, O('DestroyJoker', pick='SelfPick'), 'SelfTarget')]),
    j('iron_thorn', '무쇠 가시', 'Iron Thorn', 'Uncommon', 7,
      [E('OnScoreResolved', C('ScoreRatioAtLeast', num=1, den=2), O('PreventLoss'))]),
    j('blight_spot', '마름병 자리', 'Blight Spot', 'Uncommon', 6,
      [E('OnRoundEnd', ALWAYS, O('DestroyCard', count=1), 'RandomInDeck'),
       E('OnHandPlayed', ALWAYS, PER('DeckDeficit', 'AddMult', 20000))]),
    j('rust_pin', '녹슨 핀', 'Rust Pin', 'Uncommon', 6,
      [E('OnRoundEnd', ALWAYS, GROW('Chips', -15, init=200, floor=0), 'SelfTarget')]),
    j('snake_pit', '뱀 구덩이', 'Snake Pit', 'Uncommon', 8,
      [E('OnHandPlayed', ALWAYS, O('DisableRandomJoker'), 'RandomJoker'),
       E('OnHandPlayed', ALWAYS, XM(3.5))]),
    j('poison_ivy', '덩굴옻나무', 'Poison Ivy', 'Uncommon', 7,
      [E('OnCardScored', ALWAYS,
         O('ModifyCard', modify='Enhancement', enhancement='Glass'), 'ScoredCard',
         chance=(1, 4))]),
    j('crow_bait', '까마귀 밥', 'Crow Bait', 'Uncommon', 6,
      [E('OnRoundEnd', ALWAYS, GROW('MultAdd', 30000), 'SelfTarget'),
       E('OnRoundEnd', ALWAYS, O('DestroyJoker', pick='SelfPick'), 'SelfTarget',
         chance=(1, 5))]),

    j('hemlock_crown', '독당근 화관', 'Hemlock Crown', 'Rare', 9,
      [E('OnHandPlayed', ALWAYS, XM(6)),
       E('Passive', ALWAYS, RULE('HandsPerRound', -2))]),
    j('wolf_trap', '이빨 덫', 'Wolf Trap', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('MustPlayFiveCards')),
       E('OnHandPlayed', C('CardCount', n=5, compare='Exactly'), XM(5))]),
    j('black_frost', '검은 서리', 'Black Frost', 'Rare', 8,
      [E('OnScoreResolved', ALWAYS,
         GROW('MultMul', -500, init=60000, floor=10000), 'SelfTarget')]),
    j('last_stand', '마지막 버팀', 'Last Stand', 'Rare', 10,
      [E('OnScoreResolved', C('ScoreRatioAtLeast', num=1, den=10), O('PreventLoss')),
       E('Passive', ALWAYS, RULE('HandsPerRound', -1))]),
    j('martyr_stone', '순교의 돌', 'Martyr Stone', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('JokerSlots', -3)),
       E('OnHandPlayed', ALWAYS, XM(8))]),

    j('winter_king', '겨울의 왕', 'Winter King', 'Legendary', 10,
      [E('OnHandPlayed', ALWAYS, XM(12)),
       E('Passive', ALWAYS, RULE('HandsPerRound', -2)),
       E('Passive', ALWAYS, RULE('DiscardsPerRound', -2))]),
]


# ---------------------------------------------------------------------------
# 강화 25종 — 유리 · 강철 · 밀랍 · 금박
# ---------------------------------------------------------------------------
#
# 셋으로 나뉩니다 — **보는 것**(그 인장이면 값을 줍니다) · **붙이는 것**(카드를 그렇게
# 만듭니다) · **세는 것**(덱에 몇 장인가). 붙이는 것과 세는 것이 함께 있을 때 편성이 됩니다.

MODIFIER = [
    j('wax_seal', '밀랍 인장', 'Wax Seal', 'Common', 5,
      [E('OnCardScored', C('CardSeal', seal='Red'), AM(8))]),
    j('blue_wax', '파란 밀랍', 'Blue Wax', 'Common', 5,
      [E('OnCardScored', C('CardSeal', seal='Blue'), AC(70))]),
    j('gold_wax', '금 밀랍', 'Gold Wax', 'Common', 5,
      [E('OnCardScored', C('CardSeal', seal='Gold'), MONEY(2))]),
    j('purple_wax', '자주 밀랍', 'Purple Wax', 'Common', 5,
      [E('OnCardDiscarded', C('CardSeal', seal='Purple'), GROW('MultAdd', 4000),
         'SelfTarget')]),
    j('foil_strip', '박 띠', 'Foil Strip', 'Common', 4,
      [E('OnCardScored', C('CardEdition', edition='Foil'), AC(60))]),
    j('holo_strip', '홀로 띠', 'Holo Strip', 'Common', 4,
      [E('OnCardScored', C('CardEdition', edition='Holographic'), AM(9))]),
    j('bonus_pin', '덧칩 핀', 'Bonus Pin', 'Common', 4,
      [E('OnCardScored', C('CardEnhancement', enhancement='Bonus'), AC(50))]),
    j('mult_pin', '배수 핀', 'Mult Pin', 'Common', 4,
      [E('OnCardScored', C('CardEnhancement', enhancement='Mult'), AM(7))]),
    j('wild_pin', '들 카드 핀', 'Wild Pin', 'Common', 5,
      [E('OnCardScored', C('CardEnhancement', enhancement='Wild'), AM(10))]),

    j('glazier', '유리장이', 'Glazier', 'Uncommon', 7,
      [E('OnRoundStart', ALWAYS,
         O('ModifyCard', modify='Enhancement', enhancement='Glass'), 'RandomInHand')]),
    j('smith_tongs', '대장 집게', 'Smith Tongs', 'Uncommon', 7,
      [E('OnRoundStart', ALWAYS,
         O('ModifyCard', modify='Enhancement', enhancement='Steel'), 'RandomInHand')]),
    j('seal_press', '인장 압착기', 'Seal Press', 'Uncommon', 7,
      [E('OnRoundStart', ALWAYS, O('ModifyCard', modify='Seal', seal='Red'),
         'RandomInHand')]),
    j('gilder', '금박장이', 'Gilder', 'Uncommon', 8,
      [E('OnCardScored', C('CardEnhancement', enhancement='Steel'), XM(1.3))]),
    j('wax_tally', '밀랍 셈', 'Wax Tally', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckEnhancementCount', 'AddMult', 3000, enhancement='Gold'))]),
    j('glass_tally', '유리 셈', 'Glass Tally', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckEnhancementCount', 'MulMult', 1500, base_value=10000,
             enhancement='Glass'))]),
    j('lucky_tally', '행운 셈', 'Lucky Tally', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckEnhancementCount', 'AddChips', 20, enhancement='Lucky'))]),
    j('stone_setter', '돌 놓는 이', 'Stone Setter', 'Uncommon', 6,
      [E('OnBlindSelect', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, enhancement='Gold', random=True),
         'AllInDeck')]),
    j('edition_case', '판본 상자', 'Edition Case', 'Uncommon', 8,
      [E('Passive', ALWAYS, RULE('EditionWeightScale', 2))], blueprint=False),
    j('enamel_kiln', '에나멜 가마', 'Enamel Kiln', 'Uncommon', 8,
      [E('OnCardScored', C('CardEdition', edition='Polychrome'), XM(1.4))]),
    j('sealed_deck', '봉인된 덱', 'Sealed Deck', 'Uncommon', 7,
      [E('OnBlindSelect', ALWAYS, O('ModifyCard', modify='Seal', seal='Blue'),
         'RandomInDeck')]),

    j('master_glazier', '유리 장인', 'Master Glazier', 'Rare', 9,
      [E('OnCardScored', C('CardEnhancement', enhancement='Glass'), XM(1.6))]),
    j('seal_ring', '인장 반지', 'Seal Ring', 'Rare', 9,
      [E('OnCardScored', ALWAYS, O('ModifyCard', modify='Seal', random=True),
         'ScoredCard', chance=(1, 4))]),
    j('foil_forge', '박 대장간', 'Foil Forge', 'Rare', 10,
      [E('OnBlindSelect', ALWAYS, O('ModifyCard', modify='Edition', random=True),
         'RandomInDeck')]),
    j('alloy_bed', '합금 화단', 'Alloy Bed', 'Rare', 9,
      [E('OnCardScored', C('CardEnhanced'), O('Retrigger', times=1), 'ScoredCard')]),

    j('crown_glass', '왕관 유리', 'Crown Glass', 'Legendary', 10,
      [E('OnCardScored', ALWAYS,
         O('ModifyCard', modify='Enhancement', enhancement='Glass'), 'ScoredCard'),
       E('OnCardScored', C('CardEnhancement', enhancement='Glass'), XM(1.3))]),
]


FAMILIES = [
    ('경제', ECONOMY),
    ('성장', SCALING),
    ('위험', RISK),
    ('강화', MODIFIER),
]
