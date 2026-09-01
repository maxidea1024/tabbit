# -*- coding: utf-8 -*-
"""확장 조커 — 판 위에서 결정되는 계열 4개, 100종.

|계열|종수|무엇을 하는 편성|
|--|--|--|
|무늬|25|무늬 하나로 몰아 얻는 것|
|랭크|25|특정 랭크를 남기고 세는 것|
|족보|25|족보를 정하고 그것만 내는 것|
|버리기|25|버리는 것이 자원이 되는 것|

판정 기준과 계열의 뜻은 `doc/expansion.md` 에 있습니다. **여기 있는 것은 그 판정을 통과한
것만입니다** — 조건이 `Always` 이고 연산이 `AddMult` 하나뿐인 것은 들어오지 않습니다.
"""

from .grid import (AC, ALWAYS, AM, C, E, FACE, GROW, HC, MONEY, O, PER, RANKS, RULE, XM, j)


# ---------------------------------------------------------------------------
# 무늬 25종 — 꽃 · 색 · 물감
# ---------------------------------------------------------------------------
#
# 기본 150종의 무늬 조커는 **득점하는 카드**를 봅니다. 이 계열은 그 옆의 세 자리를 채웁니다 —
# 패에 들고만 있는 카드, 무늬 둘의 조합, 무늬를 바꾸는 것입니다.

SUIT = [
    j('ash_rose', '잿빛 장미', 'Ash Rose', 'Common', 4,
      [E('OnCardHeld', C('CardSuit', suit='Spade'), AC(15))]),
    j('dusk_iris', '어스름 붓꽃', 'Dusk Iris', 'Common', 4,
      [E('OnCardHeld', C('CardSuit', suit='Heart'), AM(2))]),
    j('paint_pot', '물감통', 'Paint Pot', 'Common', 5,
      [E('OnHandPlayed', C('SuitPair', suit='Spade'), AC(60))]),
    j('dye_vat', '염료 통', 'Dye Vat', 'Common', 5,
      [E('OnHandPlayed', C('SuitPair', suit='Heart'), AM(9))]),
    j('pollen_veil', '꽃가루 너울', 'Pollen Veil', 'Common', 4,
      [E('OnCardScored', C('CardSuit', suit='Diamond'), AC(40), chance=(2, 3))]),
    j('petal_drift', '꽃잎 흘림', 'Petal Drift', 'Common', 5,
      [E('OnCardDiscarded', C('CardSuit', suit='Club'), MONEY(1))]),
    j('glass_petal', '유리 꽃잎', 'Glass Petal', 'Common', 6,
      [E('OnHandPlayed', C('AllSuitsPresent'), AC(120))]),
    j('ochre_brush', '황토 붓', 'Ochre Brush', 'Common', 5,
      [E('OnRoundStart', ALWAYS, O('ModifyCard', modify='Suit', suit='Spade'),
         'RandomInHand')]),
    j('woad_brush', '쪽빛 붓', 'Woad Brush', 'Common', 5,
      [E('OnRoundStart', ALWAYS, O('ModifyCard', modify='Suit', suit='Heart'),
         'RandomInHand')]),

    j('slate_ring', '슬레이트 고리', 'Slate Ring', 'Uncommon', 7,
      [E('OnCardScored', C('CardSuit', suit='Spade'), GROW('Chips', 5), 'SelfTarget')]),
    j('crimson_ring', '붉은 고리', 'Crimson Ring', 'Uncommon', 7,
      [E('OnCardScored', C('CardSuit', suit='Heart'), GROW('MultAdd', 5000),
         'SelfTarget')]),
    j('moss_ring', '이끼 고리', 'Moss Ring', 'Uncommon', 7,
      [E('OnCardScored', C('CardSuit', suit='Club'), GROW('Money', 1), 'SelfTarget'),
       E('OnRoundEnd', ALWAYS, PER('SelfCounterMoney', 'AddMoney', 1))]),
    j('amber_ring', '호박 고리', 'Amber Ring', 'Uncommon', 8,
      [E('OnCardScored', C('CardSuit', suit='Diamond'), GROW('MultMul', 500),
         'SelfTarget')]),
    j('night_veil', '밤 너울', 'Night Veil', 'Uncommon', 8,
      [E('OnHandPlayed', C('AllHeldSuit', suits=['Spade']), XM(4))]),
    j('dawn_veil', '새벽 너울', 'Dawn Veil', 'Uncommon', 8,
      [E('OnHandPlayed', C('AllHeldSuit', suits=['Heart']), XM(4))]),
    j('tinted_pane', '물든 유리', 'Tinted Pane', 'Uncommon', 6,
      [E('OnCardScored', C('CardSuit', suit='Club'),
         O('ModifyCard', modify='BonusChips', value=8), 'ScoredCard')]),
    j('bleach_jar', '탈색 항아리', 'Bleach Jar', 'Uncommon', 6,
      [E('OnHandDiscarded', ALWAYS, O('ModifyCard', modify='Suit', suit='Diamond'),
         'RandomInHand')]),
    j('pigment_mill', '안료 방앗간', 'Pigment Mill', 'Uncommon', 8,
      [E('OnRoundStart', ALWAYS, O('ModifyCard', modify='Suit', random=True),
         'AllInHand')]),
    j('veiled_lamp', '가린 등', 'Veiled Lamp', 'Uncommon', 6,
      [E('OnCardScored', C('CardSuit', suit='Spade'), O('Retrigger', times=1),
         'ScoredCard', first=True)]),
    j('coral_ring', '산호 고리', 'Coral Ring', 'Uncommon', 7,
      [E('OnCardHeld', C('CardSuit', suit='Diamond'), MONEY(1), chance=(1, 3))]),

    j('black_coronet', '검은 화관', 'Black Coronet', 'Rare', 8,
      [E('OnCardScored', C('CardSuit', suit='Spade'), O('Retrigger', times=1),
         'ScoredCard')]),
    j('red_coronet', '붉은 화관', 'Red Coronet', 'Rare', 9,
      [E('OnCardHeld', C('CardSuit', suit='Heart'), XM(1.2))]),
    j('dye_press', '염료 압착기', 'Dye Press', 'Rare', 8,
      [E('OnHandPlayed', C('AllHeldSuit', suits=['Club', 'Diamond']), XM(4))]),
    j('sunfast_glaze', '볕에 굳은 유약', 'Sunfast Glaze', 'Rare', 9,
      [E('OnCardScored', C('CardSuit', suit='Club'),
         O('ModifyCard', modify='Enhancement', enhancement='Steel'), 'ScoredCard')]),

    j('full_palette', '온 팔레트', 'Full Palette', 'Legendary', 10,
      [E('OnCardScored', ALWAYS, O('Retrigger', times=1), 'ScoredCard'),
       E('Passive', ALWAYS, RULE('HandSize', -2))]),
]


# ---------------------------------------------------------------------------
# 랭크 25종 — 열매 · 씨앗
# ---------------------------------------------------------------------------
#
# 랭크를 세는 자리가 둘입니다 — **득점하는 카드의 랭크**와 **덱에 그 랭크가 몇 장 있는가**
# 입니다. 뒤쪽이 덱을 고치게 만드므로 이 계열의 값은 거기 있습니다.

RANK = [
    j('plum_stone', '자두씨', 'Plum Stone', 'Common', 4,
      [E('OnCardScored', RANKS('Three', 'Six', 'Nine'), AM(5))]),
    j('acorn_cup', '도토리 깍정이', 'Acorn Cup', 'Common', 4,
      [E('OnCardScored', RANKS('Seven'), AC(45))]),
    j('rose_hip', '장미 열매', 'Rose Hip', 'Common', 4,
      [E('OnCardHeld', RANKS('Jack'), AM(6))]),
    j('hazel_shell', '개암 껍질', 'Hazel Shell', 'Common', 5,
      [E('OnCardScored', RANKS('Ten'), MONEY(1))]),
    j('bean_row', '콩 줄', 'Bean Row', 'Common', 5,
      [E('OnHandPlayed', ALWAYS, PER('DeckRankCount', 'AddChips', 6, ranks=['Ace']))]),
    j('millet_ear', '조 이삭', 'Millet Ear', 'Common', 4,
      [E('OnCardScored', RANKS('Two', 'Three', 'Four'), AC(20)),
       E('OnCardScored', RANKS('Two', 'Three', 'Four'), AM(2))]),
    j('sloe_berry', '야생 자두', 'Sloe Berry', 'Common', 5,
      [E('OnCardDiscarded', RANKS('King'), MONEY(2))]),
    j('pip_pouch', '씨앗 주머니', 'Pip Pouch', 'Common', 5,
      [E('OnCardScored', RANKS('Five'), GROW('MultAdd', 2000), 'SelfTarget')]),
    j('chaff_sieve', '쭉정이 체', 'Chaff Sieve', 'Common', 4,
      [E('OnHandPlayed', C('FirstHandSingleRank', ranks=['Ten']), MONEY(6))]),

    j('quince_jar', '마르멜로 항아리', 'Quince Jar', 'Uncommon', 7,
      [E('OnCardScored', RANKS('Queen'), O('Retrigger', times=1), 'ScoredCard')]),
    j('king_pod', '왕 꼬투리', 'King Pod', 'Uncommon', 7,
      [E('OnCardHeld', RANKS('King'), AC(40))]),
    j('ace_husk', '으뜸 껍질', 'Ace Husk', 'Uncommon', 8,
      [E('OnCardScored', RANKS('Ace'), XM(1.3))]),
    j('seed_ledger', '씨앗 대장', 'Seed Ledger', 'Uncommon', 6,
      [E('OnRoundEnd', ALWAYS, PER('DeckRankCount', 'AddMoney', 1, ranks=['Jack']))]),
    j('bitter_pip', '쓴 씨', 'Bitter Pip', 'Uncommon', 6,
      [E('OnCardScored', RANKS('Two', 'Four', 'Six', 'Eight', 'Ten'),
         GROW('MultAdd', 1000), 'SelfTarget')]),
    j('sweet_pip', '단 씨', 'Sweet Pip', 'Uncommon', 6,
      [E('OnCardScored', RANKS('Three', 'Five', 'Seven', 'Nine', 'Ace'),
         GROW('Chips', 4), 'SelfTarget')]),
    j('walnut_press', '호두 압착기', 'Walnut Press', 'Uncommon', 7,
      [E('OnCardScored', RANKS('Nine'), O('ModifyCard', modify='RankStep', value=1),
         'ScoredCard')]),
    j('husk_lamp', '껍질 등', 'Husk Lamp', 'Uncommon', 7,
      [E('OnHandPlayed', C('FirstHandSingleRank', ranks=['Ace']), XM(4))]),
    j('chestnut_burr', '밤송이', 'Chestnut Burr', 'Uncommon', 7,
      [E('OnCardHeld', RANKS('Ace'), MONEY(2), chance=(1, 2))]),
    j('date_stone', '대추씨', 'Date Stone', 'Uncommon', 8,
      [E('OnCardScored', RANKS('Seven'), GROW('MultMul', 1000), 'SelfTarget')]),
    j('pomace_cask', '사과 찌끼 통', 'Pomace Cask', 'Uncommon', 6,
      [E('OnCardDiscarded', RANKS('Ace'), GROW('MultAdd', 15000), 'SelfTarget')]),

    j('thirteen_seeds', '열세 씨앗', 'Thirteen Seeds', 'Rare', 9,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckRankCount', 'MulMult', 300, base_value=10000, ranks=['Ace', 'King']))]),
    j('stone_fruit', '씨 굳은 열매', 'Stone Fruit', 'Rare', 8,
      [E('OnCardScored', RANKS('Eight'),
         O('ModifyCard', modify='Enhancement', random=True), 'ScoredCard')]),
    j('ripe_year', '익은 해', 'Ripe Year', 'Rare', 10,
      [E('OnRoundEnd', ALWAYS, O('ModifyCard', modify='RankStep', value=1), 'AllInDeck')]),
    j('regal_pair', '왕과 으뜸', 'Regal Pair', 'Rare', 8,
      [E('OnHandPlayed', C('HandContainsRankAndHand', ranks=['King'], hand='Pair'),
         XM(3))]),

    j('granary', '곳간', 'Granary', 'Legendary', 10,
      [E('OnHandPlayed', ALWAYS,
         PER('DeckRankCount', 'MulEach', 11000, ranks=['Ace']))]),
]


# ---------------------------------------------------------------------------
# 족보 25종 — 새 · 무리 · 떼
# ---------------------------------------------------------------------------
#
# 기본 150종은 **포함하는가**(`HandContains`)를 주로 봅니다. 이 계열은 **정확히 그것인가**
# (`HandIs`)와 **족보의 레벨**과 **낼 수 있는 족보를 제한하는 것**을 씁니다. 제한이 붙은 것이
# 이 계열에서 값이 가장 큰 것들입니다.

HAND = [
    j('wren', '굴뚝새', 'Wren', 'Common', 3,
      [E('OnHandPlayed', HC('HighCard'), AM(10))]),
    j('dipper', '물까마귀', 'Dipper', 'Common', 4,
      [E('OnHandPlayed', HC('FullHouse'), AM(14))]),
    j('crake', '뜸부기', 'Crake', 'Common', 4,
      [E('OnHandPlayed', HC('FourOfAKind'), AM(18))]),
    j('plover', '물떼새', 'Plover', 'Common', 4,
      [E('OnHandPlayed', HC('StraightFlush'), AM(24))]),
    j('shrike', '때까치', 'Shrike', 'Common', 5,
      [E('OnHandPlayed', HC('FullHouse'), AC(110))]),
    j('nuthatch', '동고비', 'Nuthatch', 'Common', 5,
      [E('OnHandPlayed', HC('FourOfAKind'), AC(140))]),
    j('flock_call', '떼 부름', 'Flock Call', 'Common', 5,
      [E('OnHandPlayed', C('HandIs', hand='Pair'), MONEY(3))]),
    j('heron_watch', '왜가리 지킴', 'Heron Watch', 'Common', 5,
      [E('OnHandPlayed', C('HandIs', hand='HighCard'), XM(2))]),
    j('swift_line', '칼새 줄', 'Swift Line', 'Common', 4,
      [E('OnHandPlayed', C('EveryNHands', n=3), AC(80))]),

    j('godwit', '마도요', 'Godwit', 'Uncommon', 7,
      [E('OnScoreResolved', HC('Flush'), GROW('MultAdd', 15000), 'SelfTarget')]),
    j('curlew', '알락꼬리', 'Curlew', 'Uncommon', 7,
      [E('OnScoreResolved', HC('FullHouse'), GROW('Chips', 20), 'SelfTarget')]),
    j('lapwing', '댕기물떼새', 'Lapwing', 'Uncommon', 8,
      [E('OnHandPlayed', C('NotMostPlayedHand'), XM(2.5))]),
    j('rookery', '새 둥지터', 'Rookery', 'Uncommon', 7,
      [E('OnHandPlayed', C('IsMostPlayedHand'), XM(2))]),
    j('kestrel', '황조롱이', 'Kestrel', 'Uncommon', 6,
      [E('OnHandPlayed', C('HandRepeated'), MONEY(4))]),
    j('migrant_line', '철새 줄', 'Migrant Line', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS, O('LevelUpHand', hand_pick='MostPlayed', levels=1),
         chance=(1, 5))]),
    j('wing_beat', '날갯짓', 'Wing Beat', 'Uncommon', 7,
      [E('OnHandPlayed', C('HandIs', hand='Flush'), XM(3))]),
    j('pinion_case', '깃 상자', 'Pinion Case', 'Uncommon', 6,
      [E('OnHandPlayed', HC('TwoPair'), O('LevelUpHand', hand_pick='Played', levels=1),
         chance=(1, 6))]),
    j('covey', '메추라기 떼', 'Covey', 'Uncommon', 7,
      [E('OnHandPlayed', C('CardCount', n=5, compare='Exactly'), AM(20))]),
    j('lone_gull', '외로운 갈매기', 'Lone Gull', 'Uncommon', 6,
      [E('OnHandPlayed', C('CardCount', n=1, compare='Exactly'), XM(4))]),

    j('great_bustard', '느시', 'Great Bustard', 'Rare', 9,
      [E('OnHandPlayed', C('HandIs', hand='FiveOfAKind'), XM(8))]),
    j('firecrest', '상모솔새', 'Firecrest', 'Rare', 8,
      [E('OnHandPlayed', HC('FlushHouse'), XM(5))]),
    j('albatross', '알바트로스', 'Albatross', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('NoRepeatHandTypes')),
       E('OnHandPlayed', ALWAYS, XM(3))]),
    j('swan_song', '백조의 노래', 'Swan Song', 'Rare', 9,
      [E('Passive', ALWAYS, RULE('SingleHandTypeOnly')),
       E('OnHandPlayed', ALWAYS, XM(4))]),
    j('sandpiper', '삑삑도요', 'Sandpiper', 'Rare', 8,
      [E('OnHandPlayed', ALWAYS, O('LevelUpHand', hand_pick='Played', levels=1))]),

    j('great_flock', '큰 무리', 'Great Flock', 'Legendary', 10,
      [E('OnHandPlayed', ALWAYS, O('LevelUpHand', hand_pick='All', levels=1),
         chance=(1, 3))]),
]


# ---------------------------------------------------------------------------
# 버리기 25종 — 낙엽 · 퇴비 · 가위
# ---------------------------------------------------------------------------
#
# **버리기 트리거에 칩과 배수를 주지 않습니다.** 그때는 득점이 없으므로 값이 사라집니다.
# 이 계열이 버리기에서 받는 것은 누적값 · 돈 · 카드 · 규칙 넷입니다.

DISCARD = [
    j('leaf_fall', '낙엽', 'Leaf Fall', 'Common', 4,
      [E('OnCardDiscarded', ALWAYS, GROW('Chips', 2), 'SelfTarget')]),
    j('compost_bin', '퇴비통', 'Compost Bin', 'Common', 5,
      [E('OnHandDiscarded', ALWAYS, GROW('MultAdd', 5000), 'SelfTarget')]),
    j('rake_head', '갈퀴', 'Rake Head', 'Common', 4,
      [E('OnHandPlayed', C('DiscardsLeft', n=3, compare='AtLeast'), AC(70))]),
    j('dry_twine', '마른 끈', 'Dry Twine', 'Common', 4,
      [E('OnHandPlayed', C('DiscardsLeft', n=1, compare='AtMost'), AM(12))]),
    j('weed_fork', '잡초 갈퀴', 'Weed Fork', 'Common', 5,
      [E('OnCardDiscarded', FACE, MONEY(1))]),
    j('chaff_pile', '쭉정이 더미', 'Chaff Pile', 'Common', 5,
      [E('OnHandDiscarded', C('DiscardedFaceAtLeast', n=2), GROW('Chips', 12),
         'SelfTarget')]),
    j('hedge_snips', '산울타리 가위', 'Hedge Snips', 'Common', 4,
      [E('OnHandDiscarded', C('FirstDiscard'), MONEY(4))]),
    j('windrow', '마른 풀 줄', 'Windrow', 'Common', 5,
      [E('OnRoundEnd', C('DiscardsUnused'), GROW('MultAdd', 10000), 'SelfTarget')]),
    j('sap_pail', '수액 통', 'Sap Pail', 'Common', 4,
      [E('OnCardDiscarded', RANKS('Queen'), GROW('MultAdd', 3000), 'SelfTarget')]),

    j('mulch_bed', '뿌리덮개', 'Mulch Bed', 'Uncommon', 7,
      [E('OnCardDiscarded', ALWAYS, GROW('MultMul', 300), 'SelfTarget')]),
    j('sieve_frame', '체 틀', 'Sieve Frame', 'Uncommon', 6,
      [E('OnHandDiscarded', ALWAYS,
         O('AddCard', create='PlayingCard', count=1, random=True), 'AllInDeck')]),
    j('long_shears', '긴 가위', 'Long Shears', 'Uncommon', 6,
      [E('Passive', ALWAYS, RULE('DiscardsPerRound', 2)),
       E('Passive', ALWAYS, RULE('HandsPerRound', -1))], blueprint=False),
    j('thinning_hook', '솎음 낫', 'Thinning Hook', 'Uncommon', 7,
      [E('OnHandDiscarded', ALWAYS, O('DestroyCard', count=1), 'RandomInDeck'),
       E('OnHandDiscarded', ALWAYS, GROW('MultMul', 1000), 'SelfTarget')]),
    j('bramble_gate', '가시 문', 'Bramble Gate', 'Uncommon', 7,
      [E('OnHandPlayed', C('DiscardsLeft', n=0, compare='Exactly'), XM(3))]),
    j('full_basket', '가득 찬 바구니', 'Full Basket', 'Uncommon', 7,
      [E('OnHandPlayed', C('DiscardsUnused'), XM(2.5))]),
    j('wither_line', '시든 줄', 'Wither Line', 'Uncommon', 6,
      [E('OnCardDiscarded', C('CardEnhanced'), GROW('MultAdd', 8000), 'SelfTarget')]),
    j('dross_jar', '찌끼 항아리', 'Dross Jar', 'Uncommon', 7,
      [E('OnHandDiscarded', C('FirstDiscardSingleCard'),
         O('CreateCard', create='Spectral', count=1))]),
    j('stubble_field', '그루터기 밭', 'Stubble Field', 'Uncommon', 7,
      [E('OnHandPlayed', ALWAYS, PER('DiscardsUnusedThisRun', 'AddMult', 3000))]),
    j('smoke_ring', '연기 고리', 'Smoke Ring', 'Uncommon', 6,
      [E('OnHandDiscarded', C('DiscardedFaceAtLeast', n=3),
         O('CreateCard', create='Tarot', count=1))]),
    j('cut_flowers', '꺾은 꽃', 'Cut Flowers', 'Uncommon', 8,
      [E('OnHandDiscarded', ALWAYS, O('ModifyCard', modify='BonusChips', value=3),
         'AllInHand')]),

    j('scythe_moon', '낫 같은 달', 'Scythe Moon', 'Rare', 8,
      [E('Passive', ALWAYS, RULE('DiscardsPerRound', 3)),
       E('OnHandPlayed', ALWAYS,
         PER('DiscardsLeft', 'MulMult', 2000, base_value=10000))]),
    j('great_compost', '큰 퇴비', 'Great Compost', 'Rare', 9,
      [E('OnCardDiscarded', ALWAYS, GROW('MultAdd', 4000, reset='Round'), 'SelfTarget')]),
    j('bare_bed', '맨 화단', 'Bare Bed', 'Rare', 8,
      [E('Passive', ALWAYS, RULE('SetDiscardsZero')),
       E('OnHandPlayed', ALWAYS, XM(4))]),
    j('winnow_wind', '키질 바람', 'Winnow Wind', 'Rare', 9,
      [E('OnHandDiscarded', ALWAYS, O('ModifyCard', modify='Suit', random=True),
         'AllInHand')]),

    j('endless_autumn', '끝없는 가을', 'Endless Autumn', 'Legendary', 10,
      [E('Passive', ALWAYS, RULE('DiscardsPerRound', 2)),
       E('OnCardDiscarded', ALWAYS, GROW('MultMul', 500), 'SelfTarget')]),
]


FAMILIES = [
    ('무늬', SUIT),
    ('랭크', RANK),
    ('족보', HAND),
    ('버리기', DISCARD),
]
