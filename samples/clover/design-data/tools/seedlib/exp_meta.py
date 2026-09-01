# -*- coding: utf-8 -*-
"""확장 조커 — 판 밖에서 결정되는 계열 3개, 75종.

|계열|종수|무엇을 하는 편성|
|--|--|--|
|소모품|25|타로 · 행성 · 유령을 돌리는 것|
|조커|25|조커의 수와 자리와 판매가를 쓰는 것|
|덱|25|덱의 구성을 바꾸는 것|

**한 판에서 값이 결정되지 않는 계열입니다.** 라운드를 넘겨야 값이 나오므로, 커먼도 즉시
점수를 주지 않는 것이 많습니다.
"""

from .grid import (AC, ALWAYS, AM, C, E, GROW, MONEY, O, PER, RULE, XM, j)


# ---------------------------------------------------------------------------
# 소모품 25종 — 별 · 달 · 물 · 증기
# ---------------------------------------------------------------------------
#
# 셋으로 나뉩니다 — **만드는 것** · **쓸 때 받는 것** · **상점에서 나오는 것을 바꾸는 것**
# 입니다. 셋이 함께 있어야 소모품 편성이 성립하므로 계열 안에 셋을 다 둡니다.

CONSUMABLE = [
    j('star_dust', '별가루', 'Star Dust', 'Common', 4,
      [E('OnRoundEnd', ALWAYS, O('CreateCard', create='Planet', count=1),
         chance=(1, 3))]),
    j('moon_dish', '달 접시', 'Moon Dish', 'Common', 5,
      [E('OnBossDefeated', ALWAYS, O('CreateCard', create='Planet', count=1))]),
    j('steam_vent', '증기 구멍', 'Steam Vent', 'Common', 5,
      [E('OnHandPlayed', C('EveryNHands', n=5),
         O('CreateCard', create='Tarot', count=1))]),
    j('rain_gauge', '우량계', 'Rain Gauge', 'Common', 4,
      [E('OnConsumableUsed', ALWAYS, MONEY(2))]),
    j('dew_glass', '이슬 유리', 'Dew Glass', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, PER('UniquePlanetUsed', 'AddMult', 8000))]),
    j('comet_ash', '혜성 재', 'Comet Ash', 'Common', 5,
      [E('OnPackOpened', ALWAYS, O('CreateCard', create='Planet', count=1),
         chance=(1, 2))]),
    j('sky_lens', '하늘 렌즈', 'Sky Lens', 'Common', 5,
      [E('Passive', ALWAYS, RULE('ShopWeightPlanet', 2))], blueprint=False),
    j('card_case', '카드 갑', 'Card Case', 'Common', 4,
      [E('Passive', ALWAYS, RULE('ConsumableSlots', 1))], blueprint=False),

    j('nebula_jar', '성운 항아리', 'Nebula Jar', 'Uncommon', 7,
      [E('OnConsumableUsed', C('ConsumableKind', consumable='Planet'), MONEY(3))]),
    j('tide_pool', '물웅덩이', 'Tide Pool', 'Uncommon', 7,
      [E('OnConsumableUsed', ALWAYS, GROW('Chips', 12), 'SelfTarget')]),
    j('mist_bell', '안개 종', 'Mist Bell', 'Uncommon', 6,
      [E('OnShopExit', ALWAYS, O('CreateCard', create='Tarot', count=1),
         chance=(1, 2))]),
    j('spirit_kettle', '넋 주전자', 'Spirit Kettle', 'Uncommon', 8,
      [E('OnBossDefeated', ALWAYS, O('CreateCard', create='Spectral', count=1))]),
    j('astrolabe', '아스트롤라베', 'Astrolabe', 'Uncommon', 8,
      [E('Passive', ALWAYS, RULE('CelestialHasMostPlayed'))], blueprint=False),
    j('arcana_case', '비법 상자', 'Arcana Case', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('SpectralInArcanaPacks'))], blueprint=False),
    j('planet_dial', '행성 다이얼', 'Planet Dial', 'Uncommon', 7,
      [E('OnRoundEnd', ALWAYS, O('LevelUpHand', hand_pick='MostPlayed', levels=1),
         chance=(1, 3))]),
    j('tarot_press', '타로 압착기', 'Tarot Press', 'Uncommon', 8,
      [E('OnConsumableUsed', C('ConsumableKind', consumable='Tarot'),
         O('CreateCard', create='Tarot', count=1), chance=(1, 4))]),
    j('salt_bowl', '소금 그릇', 'Salt Bowl', 'Uncommon', 6,
      [E('OnConsumableUsed', C('ConsumableKind', consumable='Spectral'), MONEY(8))]),
    j('cloud_ladder', '구름 사다리', 'Cloud Ladder', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS, PER('TarotUsed', 'MulMult', 300, base_value=10000))]),
    j('orrery', '천구의', 'Orrery', 'Uncommon', 8,
      [E('OnPackSkipped', ALWAYS, O('CreateCard', create='Planet', count=1))]),

    j('great_conjunction', '큰 합', 'Great Conjunction', 'Rare', 9,
      [E('OnRoundEnd', ALWAYS, O('LevelUpHand', hand_pick='All', levels=1),
         chance=(1, 6))]),
    j('spirit_gate', '넋의 문', 'Spirit Gate', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('ShopAllowsSpectral'))], blueprint=False),
    j('star_forge', '별 대장간', 'Star Forge', 'Rare', 10,
      [E('OnConsumableUsed', ALWAYS, GROW('MultMul', 1500), 'SelfTarget')]),
    j('moon_mirror', '달 거울', 'Moon Mirror', 'Rare', 9,
      [E('OnBossDefeated', ALWAYS,
         O('CreateCard', create='Spectral', count=1, edition='Negative'))]),
    j('void_lens', '빈 렌즈', 'Void Lens', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('ShopWeightTarot', 3)),
       E('Passive', ALWAYS, RULE('ShopWeightPlanet', 3))], blueprint=False),

    j('celestial_engine', '천체 기관', 'Celestial Engine', 'Legendary', 10,
      [E('OnRoundEnd', ALWAYS, O('LevelUpHand', hand_pick='MostPlayed', levels=2)),
       E('Passive', ALWAYS, RULE('ConsumableSlots', 1))]),
]


# ---------------------------------------------------------------------------
# 조커 25종 — 거울 · 그림자 · 쌍둥이
# ---------------------------------------------------------------------------
#
# **복사는 늘리지 않았습니다.** 기본 150종에 `tracing` 과 `mirror_note` 와 `faint_outline`
# 셋이 있고, 복사가 넷을 넘으면 어느 것이 무엇을 복사하는지 판이 읽히지 않습니다. 대신
# **다른 조커를 키우는 것**(`GrowOthers`)을 이 계열의 값으로 두었습니다.

JOKER_META = [
    j('paired_glass', '짝 유리', 'Paired Glass', 'Common', 5,
      [E('OnHandPlayed', ALWAYS, PER('JokerCount', 'AddChips', 20))]),
    j('shadow_box', '그림자 상자', 'Shadow Box', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, PER('EmptyJokerSlots', 'AddMult', 10000))]),
    j('twin_pot', '쌍 화분', 'Twin Pot', 'Common', 5,
      [E('OnBlindSelect', ALWAYS,
         O('CreateCard', create='Joker', count=1, rarity='Common'), chance=(1, 3))]),
    j('sale_tag', '값표', 'Sale Tag', 'Common', 4,
      [E('OnJokerSold', ALWAYS, GROW('MultAdd', 6000), 'SelfTarget')]),
    j('spare_frame', '여벌 액자', 'Spare Frame', 'Common', 5,
      [E('Passive', ALWAYS, RULE('JokerSlots', 1)),
       E('Passive', ALWAYS, RULE('HandSize', -1))], blueprint=False),
    j('dust_sheet', '덮개천', 'Dust Sheet', 'Common', 4,
      [E('OnHandPlayed', ALWAYS,
         PER('JokerRarityCount', 'AddMult', 6000, rarity='Common'))]),
    j('price_card', '가격표', 'Price Card', 'Common', 5,
      [E('OnHandPlayed', ALWAYS, PER('OtherJokerSellValue', 'AddChips', 8))]),
    j('dim_mirror', '흐린 거울', 'Dim Mirror', 'Common', 5,
      [E('OnRoundEnd', ALWAYS, O('GrowOthers', counter='MultAdd', step=2000),
         'AllOtherJokers')]),

    j('frame_shop', '액자 가게', 'Frame Shop', 'Uncommon', 7,
      [E('OnShopEnter', ALWAYS,
         O('CreateCard', create='Joker', count=1, rarity='Uncommon'),
         chance=(1, 6))]),
    j('rarity_ledger', '희귀 대장', 'Rarity Ledger', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS,
         PER('JokerRarityCount', 'AddMult', 15000, rarity='Rare'))]),
    j('full_shelf', '가득한 선반', 'Full Shelf', 'Uncommon', 8,
      [E('OnHandPlayed', ALWAYS, PER('JokerCount', 'MulMult', 1000,
                                     base_value=10000))]),
    j('hand_me_down', '물림', 'Hand Me Down', 'Uncommon', 6,
      [E('OnJokerSold', ALWAYS, O('GrowOthers', counter='MultAdd', step=5000),
         'AllOtherJokers')]),
    j('swap_frame', '바꿈 액자', 'Swap Frame', 'Uncommon', 7,
      [E('OnBlindSelect', ALWAYS, O('ModifyJoker', random=True), 'RandomJoker')]),
    j('estate_note', '유산 쪽지', 'Estate Note', 'Uncommon', 6,
      [E('OnSell', ALWAYS, O('CreateCard', create='Joker', count=1, rarity='Rare'))],
      blueprint=False),
    j('slot_pin', '자리 핀', 'Slot Pin', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('JokerSlots', 2)),
       E('Passive', ALWAYS, RULE('DiscardsPerRound', -1))], blueprint=False),
    j('auction_bell', '경매 종', 'Auction Bell', 'Uncommon', 7,
      [E('OnJokerSold', ALWAYS, O('MulMoney', value=11500))]),
    j('mirror_dust', '거울 가루', 'Mirror Dust', 'Uncommon', 8,
      [E('OnHandPlayed', ALWAYS,
         PER('JokerRarityCount', 'MulEach', 10800, rarity='Common'))]),
    j('understudy', '대역', 'Understudy', 'Uncommon', 7,
      [E('OnRoundStart', ALWAYS, O('GrowOthers', counter='Chips', step=5),
         'AllOtherJokers')]),

    j('hall_of_mirrors', '거울의 방', 'Hall of Mirrors', 'Rare', 10,
      [E('OnHandPlayed', ALWAYS, PER('JokerCount', 'MulMult', 2500,
                                     base_value=10000))]),
    j('black_frame', '검은 액자', 'Black Frame', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('JokerSlots', -2)),
       E('OnHandPlayed', ALWAYS, XM(4))]),
    j('prompt_book', '대본', 'Prompt Book', 'Rare', 10,
      [E('OnBlindSelect', ALWAYS, O('GrowOthers', counter='MultMul', step=1000),
         'AllOtherJokers')]),
    j('pawnbroker', '전당포', 'Pawnbroker', 'Rare', 8,
      [E('OnHandPlayed', ALWAYS,
         PER('OtherJokerSellValue', 'MulMult', 300, base_value=10000))]),
    j('twin_seed', '쌍씨', 'Twin Seed', 'Rare', 9,
      [E('OnBlindSelect', ALWAYS,
         O('CreateCard', create='Joker', count=1, rarity='Uncommon'))]),
    j('stagehand', '무대 일꾼', 'Stagehand', 'Rare', 9,
      [E('OnSell', ALWAYS, O('GrowOthers', counter='MultMul', step=5000),
         'AllOtherJokers')], blueprint=False),

    j('grand_gallery', '큰 화랑', 'Grand Gallery', 'Legendary', 10,
      [E('Passive', ALWAYS, RULE('JokerSlots', 3)),
       E('OnHandPlayed', ALWAYS, PER('JokerCount', 'AddMult', 10000))]),
]


# ---------------------------------------------------------------------------
# 덱 25종 — 삽 · 모종 · 체 · 접붙임
# ---------------------------------------------------------------------------
#
# 덱을 **늘리는 것**과 **줄이는 것**이 반대 방향의 편성입니다. 늘리면 `DeckRemaining` 이
# 커지고 줄이면 `DeckDeficit` 이 커지므로, 값을 읽는 단위가 갈립니다 — 한 런에서 둘을 함께
# 가져가는 것은 손해입니다. **그 판단이 이 계열의 값입니다.**

DECK = [
    j('spade_head', '삽날', 'Spade Head', 'Common', 5,
      [E('OnRoundStart', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, random=True), 'AllInDeck')]),
    j('seed_tray', '모판', 'Seed Tray', 'Common', 5,
      [E('OnBlindSelect', ALWAYS,
         O('AddCard', create='PlayingCard', count=2, card_class='Numbered',
           random=True), 'AllInDeck')]),
    j('sorting_sieve', '고르는 체', 'Sorting Sieve', 'Common', 4,
      [E('OnHandPlayed', ALWAYS, PER('DeckRemaining', 'AddMult', 400))]),
    j('thin_rows', '성긴 줄', 'Thin Rows', 'Common', 5,
      [E('OnHandPlayed', ALWAYS, PER('DeckDeficit', 'AddChips', 12))]),
    j('face_bed', '그림 화단', 'Face Bed', 'Common', 4,
      [E('OnBlindSelect', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, card_class='Face', random=True),
         'AllInDeck')]),
    j('ace_bed', '으뜸 화단', 'Ace Bed', 'Common', 5,
      [E('OnBlindSelect', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, card_class='Ace', random=True),
         'AllInDeck')]),
    j('hand_spade', '손삽', 'Hand Spade', 'Common', 4,
      [E('OnRoundStart', ALWAYS, O('ModifyCard', modify='BonusChips', value=4),
         'AllInHand')]),
    j('garden_line', '정원 줄', 'Garden Line', 'Common', 5,
      [E('OnHandPlayed', C('DeckEnhancedAtLeast', n=6), AM(12))]),
    j('weed_pull', '잡초 뽑기', 'Weed Pull', 'Common', 4,
      [E('OnHandDiscarded', ALWAYS, O('DestroyCard', count=1), 'RandomInDeck',
         chance=(1, 3))]),

    j('graft_knife', '접칼', 'Graft Knife', 'Uncommon', 7,
      [E('OnCardScored', ALWAYS, O('AddCard', create='CopyOfScored', count=1),
         'AllInDeck', chance=(1, 8))]),
    j('nursery_bed', '온상', 'Nursery Bed', 'Uncommon', 7,
      [E('OnRoundEnd', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, enhancement='Bonus', random=True),
         'AllInDeck')]),
    j('soil_test', '흙 검사', 'Soil Test', 'Uncommon', 6,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckEnhancementCount', 'AddMult', 2000, enhancement='Bonus'))]),
    j('deep_bed', '깊은 화단', 'Deep Bed', 'Uncommon', 8,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckRemaining', 'MulMult', 60, base_value=10000))]),
    j('crop_rotation', '돌려짓기', 'Crop Rotation', 'Uncommon', 7,
      [E('OnRoundEnd', ALWAYS, O('ModifyCard', modify='Suit', random=True),
         'RandomInDeck')]),
    j('chalk_line', '백묵 줄', 'Chalk Line', 'Uncommon', 6,
      [E('OnHandPlayed', C('DeckEnhancedAtLeast', n=10), XM(2))]),
    j('stone_row', '돌 줄', 'Stone Row', 'Uncommon', 6,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckEnhancementCount', 'AddMult', 2500, enhancement='Stone'))]),
    j('culling_fork', '솎는 갈퀴', 'Culling Fork', 'Uncommon', 7,
      [E('Passive', ALWAYS, RULE('RemoveFaceCards')),
       E('OnHandPlayed', ALWAYS, XM(2))]),
    j('wide_bed', '넓은 화단', 'Wide Bed', 'Uncommon', 6,
      [E('Passive', ALWAYS, RULE('HandSize', 3)),
       E('Passive', ALWAYS, RULE('HandsPerRound', -1))], blueprint=False),
    j('seed_drill', '씨 뿌리개', 'Seed Drill', 'Uncommon', 8,
      [E('OnRoundStart', ALWAYS,
         O('AddCard', create='PlayingCard', count=2, random=True), 'AllInDeck')]),
    j('gold_furrow', '금 이랑', 'Gold Furrow', 'Uncommon', 7,
      [E('OnBlindSelect', ALWAYS,
         O('ModifyCard', modify='Enhancement', enhancement='Gold'), 'RandomInDeck')]),

    j('great_graft', '큰 접붙임', 'Great Graft', 'Rare', 9,
      [E('OnCardScored', ALWAYS, O('AddCard', create='CopyOfScored', count=1),
         'AllInDeck', chance=(1, 3))]),
    j('terraced_bed', '계단 화단', 'Terraced Bed', 'Rare', 9,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckDeficit', 'MulMult', 400, base_value=10000))]),
    j('hothouse', '유리 온실', 'Hothouse', 'Rare', 10,
      [E('OnRoundEnd', ALWAYS, O('ModifyCard', modify='Enhancement', random=True),
         'RandomInDeck'),
       E('OnRoundEnd', ALWAYS, O('ModifyCard', modify='Seal', random=True),
         'RandomInDeck')]),
    j('stone_garden', '돌 정원', 'Stone Garden', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('AllCardsScore')),
       E('OnHandPlayed', ALWAYS, AM(15))]),

    j('first_garden', '첫 정원', 'First Garden', 'Legendary', 10,
      [E('OnRoundStart', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, enhancement='Steel', random=True),
         'AllInDeck'),
       E('OnHandPlayed', ALWAYS,
         PER('DeckEnhancementCount', 'MulMult', 800, base_value=10000,
             enhancement='Steel'))]),
]


FAMILIES = [
    ('소모품', CONSUMABLE),
    ('조커', JOKER_META),
    ('덱', DECK),
]
