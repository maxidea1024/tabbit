# 규칙

> [확장 조커로](../expansion.md)

---

상점과 판정과 진행을 바꾸는 계열입니다. 값이 점수가 아니라 규칙으로 옵니다.

**이 파일은 생성물입니다.** 값을 고치려면 `design-data/tools/seedlib/` 의
해당 모듈을 고치고 `expansion_doc.py` 를 다시 돌립니다.

## 상점 25종

커먼 9 · 언커먼 11 · 레어 4 · 전설 1

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|손수레|Hand Cart|`hand_cart`|커먼|$5|`Passive` Always → ChangeRule|
|그린 간판|Paint Sign|`paint_sign`|커먼|$4|`Passive` Always → ChangeRule|
|헛바퀴|Free Wheel|`free_wheel`|커먼|$4|`Passive` Always → ChangeRule|
|단골|Regular|`regular`|커먼|$5|`OnShopEnter` Always → GrowSelf|
|수레 끄는 아이|Barrow Boy|`barrow_boy`|커먼|$4|`OnReroll` Always → GrowSelf|
|칠판|Chalk Board|`chalk_board`|커먼|$5|`Passive` Always → ChangeRule|
|종이 봉지|Paper Bag|`paper_bag`|커먼|$4|`OnShopExit` Always → AddMoney|
|구경꾼|Window Shopper|`window_shopper`|커먼|$5|`OnPackSkipped` Always → AddMoney|
|계산대 종|Till Bell|`till_bell`|커먼|$5|`OnReroll` Always → AddMoney|
|두 수레|Two Carts|`two_carts`|언커먼|$7|`Passive` Always → ChangeRule|
|할인 책|Coupon Book|`coupon_book`|언커먼|$7|`OnBossDefeated` Always → ChangeRule|
|할인 간판|Sale Board|`sale_board`|언커먼|$7|`Passive` Always → ChangeRule|
|성질 상자|Modifier Case|`modifier_case`|언커먼|$8|`Passive` Always → ChangeRule|
|외치는 이|Crier|`crier`|언커먼|$6|`OnShopEnter` Always → ShopGift|
|짐꾼|Porter|`porter`|언커먼|$7|`OnShopEnter` Always → ShopGift|
|딜러의 손|Dealer Hand|`dealer_hand`|언커먼|$8|`OnBossDefeated` Always → ShopGift|
|낭비꾼|Spendthrift|`spendthrift`|언커먼|$6|`OnShopExit` Always → GrowSelf|
|리롤 장치|Reroll Rig|`reroll_rig`|언커먼|$7|`Passive` Always → ChangeRule|
|밤 장터|Night Market|`night_market`|언커먼|$7|`Passive` Always → ChangeRule|
|박 좌판|Foil Stall|`foil_stall`|언커먼|$8|`OnShopEnter` Always → ShopGift|
|큰 장|Grand Bazaar|`grand_bazaar`|레어|$9|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|자유 시장|Free Market|`free_market`|레어|$9|`Passive` Always → ChangeRule|
|후원자|Patron|`patron`|레어|$10|`OnShopEnter` Always → ShopGift|
|금박 수레|Gilt Cart|`gilt_cart`|레어|$9|`OnBlindSelect` Always → ShopGift|
|큰 거리|High Street|`high_street`|전설|$10|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule · `OnShopEnter` Always → ShopGift|

## 규칙 25종

커먼 8 · 언커먼 11 · 레어 5 · 전설 1

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|안개 띠|Fog Bank|`fog_bank`|커먼|$5|`Passive` Always → ChangeRule|
|고른 저울|Even Scales|`even_scales`|커먼|$5|`Passive` Always → ChangeRule|
|반쪽 빛|Half Light|`half_light`|커먼|$3|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|꿈 유리|Dream Pane|`dream_pane`|커먼|$5|`Passive` Always → ChangeRule|
|틈 돌|Gap Stone|`gap_stone`|커먼|$5|`Passive` Always → ChangeRule|
|굽은 유리|Crooked Pane|`crooked_pane`|커먼|$5|`OnHandPlayed` CardCount → MulMult|
|조짐 서판|Omen Slate|`omen_slate`|커먼|$4|`OnCardScored` TargetMatch → AddChips|
|조짐 읽는 이|Sign Reader|`sign_reader`|커먼|$5|`OnHandPlayed` TargetMatch → AddMult|
|셋 줄|Three Line|`three_line`|언커먼|$8|`Passive` Always → ChangeRule|
|무게 실은 공기|Weighted Air|`weighted_air`|언커먼|$7|`Passive` Always → ChangeRule|
|꿈 사다리|Dream Ladder|`dream_ladder`|언커먼|$7|`OnHandPlayed` TargetMatch → MulMult|
|랭크 조짐|Rank Omen|`rank_omen`|언커먼|$6|`OnCardScored` TargetMatch → AddMult|
|안개 문|Mist Gate|`mist_gate`|언커먼|$7|`OnBlindSelect` BlindKind → GrowSelf|
|이른 시간|Small Hours|`small_hours`|언커먼|$6|`OnBlindSelect` BlindKind → AddMoney|
|늦은 시간|Big Hours|`big_hours`|언커먼|$6|`OnHandPlayed` BlindKind → MulMult|
|여섯째 감각|Sixth Sense|`sixth_sense`|언커먼|$7|`OnHandPlayed` EveryNHands → CreateCard|
|굽은 사다리|Bent Ladder|`bent_ladder`|언커먼|$7|`Passive` Always → ChangeRule|
|깬 꿈|Waking Dream|`waking_dream`|언커먼|$8|`OnRoundStart` Always → ModifyCard|
|긴 승산|Long Odds|`long_odds`|언커먼|$8|`OnHandPlayed` Always → RandomRange|
|부러진 자|Broken Rule|`broken_rule`|레어|$9|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|꿈 기관|Dream Engine|`dream_engine`|레어|$10|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|온 얼굴|All Face|`all_face`|레어|$9|`Passive` Always → ChangeRule · `OnCardScored` CardIsFace → AddMult|
|조짐 화관|Omen Crown|`omen_crown`|레어|$9|`OnCardScored` TargetMatch → MulMult|
|둘 줄|Two Line|`two_line`|레어|$10|`Passive` Always → ChangeRule|
|깬 세계|Waking World|`waking_world`|전설|$10|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule · `Passive` Always → ChangeRule|

## 진행 25종

커먼 8 · 언커먼 10 · 레어 5 · 전설 2

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|무쇠 열쇠|Iron Key|`iron_key`|커먼|$5|`OnBossDefeated` Always → AddMoney|
|놋 자물쇠|Brass Lock|`brass_lock`|커먼|$5|`OnHandPlayed` BlindKind → AddMult|
|봄 문|Spring Gate|`spring_gate`|커먼|$4|`OnHandPlayed` Always → PerUnit|
|가을 문|Autumn Gate|`autumn_gate`|커먼|$5|`OnPackSkipped` Always → CreateCard|
|겨울 문|Winter Gate|`winter_gate`|커먼|$5|`OnBossDefeated` Always → GrowSelf|
|여름 문|Summer Gate|`summer_gate`|커먼|$4|`OnRoundStart` BlindKind → AddMoney|
|지킴돌|Ward Stone|`ward_stone`|커먼|$5|`OnHandPlayed` BossTriggered → AddMult|
|문 종|Door Chime|`door_chime`|커먼|$4|`OnBlindSelect` Always → GrowSelf|
|보스 열쇠|Boss Key|`boss_key`|언커먼|$7|`Passive` Always → ChangeRule|
|겹문|Double Gate|`double_gate`|언커먼|$7|`Passive` Always → ChangeRule|
|계절 고리|Season Ring|`season_ring`|언커먼|$7|`OnBossDefeated` Always → GrowSelf|
|만능 열쇠|Skeleton Key|`skeleton_key`|언커먼|$8|`OnBlindSelect` BlindKind → RerollBoss|
|통행 집|Toll House|`toll_house`|언커먼|$6|`OnBossDefeated` Always → CreateCard|
|열린 문|Open Gate|`open_gate`|언커먼|$7|`OnPackSkipped` Always → GrowSelf|
|자물쇠 따개|Lock Pick|`lock_pick`|언커먼|$7|`OnBossDefeated` Always → DuplicateNextTag|
|추수 문|Harvest Gate|`harvest_gate`|언커먼|$8|`OnBossDefeated` Always → CreateCard|
|작은 문|Small Door|`small_door`|언커먼|$6|`OnHandPlayed` BlindKind → MulMult|
|안테 돌|Ante Stone|`ante_stone`|언커먼|$8|`Passive` Always → ChangeRule|
|큰 열쇠|Great Key|`great_key`|레어|$9|`OnBossDefeated` Always → GrowSelf|
|보스 지킴|Boss Ward|`boss_ward`|레어|$9|`OnHandPlayed` BossTriggered → MulMult|
|계절 바퀴|Season Wheel|`season_wheel`|레어|$10|`Passive` Always → ChangeRule|
|무쇠 문|Iron Gate|`iron_gate`|레어|$9|`Passive` Always → ChangeRule|
|문지기|Warden|`warden`|레어|$10|`Passive` Always → ChangeRule|
|해의 문|Year Gate|`year_gate`|전설|$10|`OnBossDefeated` Always → GrowSelf|
|마지막 문|Last Door|`last_door`|전설|$10|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|

---

EOD
