# 판 위의 것

> [확장 조커로](../expansion.md)

---

한 판 안에서 값이 결정되는 계열입니다. 무늬와 랭크와 족보를 고르는 방식이 바뀝니다.

**이 파일은 생성물입니다.** 값을 고치려면 `design-data/tools/seedlib/` 의
해당 모듈을 고치고 `expansion_doc.py` 를 다시 돌립니다.

## 무늬 25종

커먼 9 · 언커먼 11 · 레어 4 · 전설 1

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|잿빛 장미|Ash Rose|`ash_rose`|커먼|$4|`OnCardHeld` CardSuit → AddChips|
|어스름 붓꽃|Dusk Iris|`dusk_iris`|커먼|$4|`OnCardHeld` CardSuit → AddMult|
|물감통|Paint Pot|`paint_pot`|커먼|$5|`OnHandPlayed` SuitPair → AddChips|
|염료 통|Dye Vat|`dye_vat`|커먼|$5|`OnHandPlayed` SuitPair → AddMult|
|꽃가루 너울|Pollen Veil|`pollen_veil`|커먼|$4|`OnCardScored` CardSuit → AddChips (2/3)|
|꽃잎 흘림|Petal Drift|`petal_drift`|커먼|$5|`OnCardDiscarded` CardSuit → AddMoney|
|유리 꽃잎|Glass Petal|`glass_petal`|커먼|$6|`OnHandPlayed` AllSuitsPresent → AddChips|
|황토 붓|Ochre Brush|`ochre_brush`|커먼|$5|`OnRoundStart` Always → ModifyCard|
|쪽빛 붓|Woad Brush|`woad_brush`|커먼|$5|`OnRoundStart` Always → ModifyCard|
|슬레이트 고리|Slate Ring|`slate_ring`|언커먼|$7|`OnCardScored` CardSuit → GrowSelf|
|붉은 고리|Crimson Ring|`crimson_ring`|언커먼|$7|`OnCardScored` CardSuit → GrowSelf|
|이끼 고리|Moss Ring|`moss_ring`|언커먼|$7|`OnCardScored` CardSuit → GrowSelf · `OnRoundEnd` Always → PerUnit|
|호박 고리|Amber Ring|`amber_ring`|언커먼|$8|`OnCardScored` CardSuit → GrowSelf|
|밤 너울|Night Veil|`night_veil`|언커먼|$8|`OnHandPlayed` AllHeldSuit → MulMult|
|새벽 너울|Dawn Veil|`dawn_veil`|언커먼|$8|`OnHandPlayed` AllHeldSuit → MulMult|
|물든 유리|Tinted Pane|`tinted_pane`|언커먼|$6|`OnCardScored` CardSuit → ModifyCard|
|탈색 항아리|Bleach Jar|`bleach_jar`|언커먼|$6|`OnHandDiscarded` Always → ModifyCard|
|안료 방앗간|Pigment Mill|`pigment_mill`|언커먼|$8|`OnRoundStart` Always → ModifyCard|
|가린 등|Veiled Lamp|`veiled_lamp`|언커먼|$6|`OnCardScored` CardSuit → Retrigger|
|산호 고리|Coral Ring|`coral_ring`|언커먼|$7|`OnCardHeld` CardSuit → AddMoney (1/3)|
|검은 화관|Black Coronet|`black_coronet`|레어|$8|`OnCardScored` CardSuit → Retrigger|
|붉은 화관|Red Coronet|`red_coronet`|레어|$9|`OnCardHeld` CardSuit → MulMult|
|염료 압착기|Dye Press|`dye_press`|레어|$8|`OnHandPlayed` AllHeldSuit → MulMult|
|볕에 굳은 유약|Sunfast Glaze|`sunfast_glaze`|레어|$9|`OnCardScored` CardSuit → ModifyCard|
|온 팔레트|Full Palette|`full_palette`|전설|$10|`OnCardScored` Always → Retrigger · `Passive` Always → ChangeRule|

## 랭크 25종

커먼 9 · 언커먼 11 · 레어 4 · 전설 1

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|자두씨|Plum Stone|`plum_stone`|커먼|$4|`OnCardScored` CardRankSet → AddMult|
|도토리 깍정이|Acorn Cup|`acorn_cup`|커먼|$4|`OnCardScored` CardRankSet → AddChips|
|장미 열매|Rose Hip|`rose_hip`|커먼|$4|`OnCardHeld` CardRankSet → AddMult|
|개암 껍질|Hazel Shell|`hazel_shell`|커먼|$5|`OnCardScored` CardRankSet → AddMoney|
|콩 줄|Bean Row|`bean_row`|커먼|$5|`OnHandPlayed` Always → PerUnit|
|조 이삭|Millet Ear|`millet_ear`|커먼|$4|`OnCardScored` CardRankSet → AddChips · `OnCardScored` CardRankSet → AddMult|
|야생 자두|Sloe Berry|`sloe_berry`|커먼|$5|`OnCardDiscarded` CardRankSet → AddMoney|
|씨앗 주머니|Pip Pouch|`pip_pouch`|커먼|$5|`OnCardScored` CardRankSet → GrowSelf|
|쭉정이 체|Chaff Sieve|`chaff_sieve`|커먼|$4|`OnHandPlayed` FirstHandSingleRank → AddMoney|
|마르멜로 항아리|Quince Jar|`quince_jar`|언커먼|$7|`OnCardScored` CardRankSet → Retrigger|
|왕 꼬투리|King Pod|`king_pod`|언커먼|$7|`OnCardHeld` CardRankSet → AddChips|
|으뜸 껍질|Ace Husk|`ace_husk`|언커먼|$8|`OnCardScored` CardRankSet → MulMult|
|씨앗 대장|Seed Ledger|`seed_ledger`|언커먼|$6|`OnRoundEnd` Always → PerUnit|
|쓴 씨|Bitter Pip|`bitter_pip`|언커먼|$6|`OnCardScored` CardRankSet → GrowSelf|
|단 씨|Sweet Pip|`sweet_pip`|언커먼|$6|`OnCardScored` CardRankSet → GrowSelf|
|호두 압착기|Walnut Press|`walnut_press`|언커먼|$7|`OnCardScored` CardRankSet → ModifyCard|
|껍질 등|Husk Lamp|`husk_lamp`|언커먼|$7|`OnHandPlayed` FirstHandSingleRank → MulMult|
|밤송이|Chestnut Burr|`chestnut_burr`|언커먼|$7|`OnCardHeld` CardRankSet → AddMoney (1/2)|
|대추씨|Date Stone|`date_stone`|언커먼|$8|`OnCardScored` CardRankSet → GrowSelf|
|사과 찌끼 통|Pomace Cask|`pomace_cask`|언커먼|$6|`OnCardDiscarded` CardRankSet → GrowSelf|
|열세 씨앗|Thirteen Seeds|`thirteen_seeds`|레어|$9|`OnHandPlayed` Always → PerUnit|
|씨 굳은 열매|Stone Fruit|`stone_fruit`|레어|$8|`OnCardScored` CardRankSet → ModifyCard|
|익은 해|Ripe Year|`ripe_year`|레어|$10|`OnRoundEnd` Always → ModifyCard|
|왕과 으뜸|Regal Pair|`regal_pair`|레어|$8|`OnHandPlayed` HandContainsRankAndHand → MulMult|
|곳간|Granary|`granary`|전설|$10|`OnHandPlayed` Always → PerUnit|

## 족보 25종

커먼 9 · 언커먼 10 · 레어 5 · 전설 1

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|굴뚝새|Wren|`wren`|커먼|$3|`OnHandPlayed` HandContains → AddMult|
|물까마귀|Dipper|`dipper`|커먼|$4|`OnHandPlayed` HandContains → AddMult|
|뜸부기|Crake|`crake`|커먼|$4|`OnHandPlayed` HandContains → AddMult|
|물떼새|Plover|`plover`|커먼|$4|`OnHandPlayed` HandContains → AddMult|
|때까치|Shrike|`shrike`|커먼|$5|`OnHandPlayed` HandContains → AddChips|
|동고비|Nuthatch|`nuthatch`|커먼|$5|`OnHandPlayed` HandContains → AddChips|
|떼 부름|Flock Call|`flock_call`|커먼|$5|`OnHandPlayed` HandIs → AddMoney|
|왜가리 지킴|Heron Watch|`heron_watch`|커먼|$5|`OnHandPlayed` HandIs → MulMult|
|칼새 줄|Swift Line|`swift_line`|커먼|$4|`OnHandPlayed` EveryNHands → AddChips|
|마도요|Godwit|`godwit`|언커먼|$7|`OnScoreResolved` HandContains → GrowSelf|
|알락꼬리|Curlew|`curlew`|언커먼|$7|`OnScoreResolved` HandContains → GrowSelf|
|댕기물떼새|Lapwing|`lapwing`|언커먼|$8|`OnHandPlayed` NotMostPlayedHand → MulMult|
|새 둥지터|Rookery|`rookery`|언커먼|$7|`OnHandPlayed` IsMostPlayedHand → MulMult|
|황조롱이|Kestrel|`kestrel`|언커먼|$6|`OnHandPlayed` HandRepeated → AddMoney|
|철새 줄|Migrant Line|`migrant_line`|언커먼|$7|`OnHandPlayed` Always → LevelUpHand (1/5)|
|날갯짓|Wing Beat|`wing_beat`|언커먼|$7|`OnHandPlayed` HandIs → MulMult|
|깃 상자|Pinion Case|`pinion_case`|언커먼|$6|`OnHandPlayed` HandContains → LevelUpHand (1/6)|
|메추라기 떼|Covey|`covey`|언커먼|$7|`OnHandPlayed` CardCount → AddMult|
|외로운 갈매기|Lone Gull|`lone_gull`|언커먼|$6|`OnHandPlayed` CardCount → MulMult|
|느시|Great Bustard|`great_bustard`|레어|$9|`OnHandPlayed` HandIs → MulMult|
|상모솔새|Firecrest|`firecrest`|레어|$8|`OnHandPlayed` HandContains → MulMult|
|알바트로스|Albatross|`albatross`|레어|$9|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|백조의 노래|Swan Song|`swan_song`|레어|$9|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|삑삑도요|Sandpiper|`sandpiper`|레어|$8|`OnHandPlayed` Always → LevelUpHand|
|큰 무리|Great Flock|`great_flock`|전설|$10|`OnHandPlayed` Always → LevelUpHand (1/3)|

## 버리기 25종

커먼 9 · 언커먼 11 · 레어 4 · 전설 1

|이름|영어|`id`|희귀도|가격|효과|
|--|--|--|--|--|--|
|낙엽|Leaf Fall|`leaf_fall`|커먼|$4|`OnCardDiscarded` Always → GrowSelf|
|퇴비통|Compost Bin|`compost_bin`|커먼|$5|`OnHandDiscarded` Always → GrowSelf|
|갈퀴|Rake Head|`rake_head`|커먼|$4|`OnHandPlayed` DiscardsLeft → AddChips|
|마른 끈|Dry Twine|`dry_twine`|커먼|$4|`OnHandPlayed` DiscardsLeft → AddMult|
|잡초 갈퀴|Weed Fork|`weed_fork`|커먼|$5|`OnCardDiscarded` CardIsFace → AddMoney|
|쭉정이 더미|Chaff Pile|`chaff_pile`|커먼|$5|`OnHandDiscarded` DiscardedFaceAtLeast → GrowSelf|
|산울타리 가위|Hedge Snips|`hedge_snips`|커먼|$4|`OnHandDiscarded` FirstDiscard → AddMoney|
|마른 풀 줄|Windrow|`windrow`|커먼|$5|`OnRoundEnd` DiscardsUnused → GrowSelf|
|수액 통|Sap Pail|`sap_pail`|커먼|$4|`OnCardDiscarded` CardRankSet → GrowSelf|
|뿌리덮개|Mulch Bed|`mulch_bed`|언커먼|$7|`OnCardDiscarded` Always → GrowSelf|
|체 틀|Sieve Frame|`sieve_frame`|언커먼|$6|`OnHandDiscarded` Always → AddCard|
|긴 가위|Long Shears|`long_shears`|언커먼|$6|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|솎음 낫|Thinning Hook|`thinning_hook`|언커먼|$7|`OnHandDiscarded` Always → DestroyCard · `OnHandDiscarded` Always → GrowSelf|
|가시 문|Bramble Gate|`bramble_gate`|언커먼|$7|`OnHandPlayed` DiscardsLeft → MulMult|
|가득 찬 바구니|Full Basket|`full_basket`|언커먼|$7|`OnHandPlayed` DiscardsUnused → MulMult|
|시든 줄|Wither Line|`wither_line`|언커먼|$6|`OnCardDiscarded` CardEnhanced → GrowSelf|
|찌끼 항아리|Dross Jar|`dross_jar`|언커먼|$7|`OnHandDiscarded` FirstDiscardSingleCard → CreateCard|
|그루터기 밭|Stubble Field|`stubble_field`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|연기 고리|Smoke Ring|`smoke_ring`|언커먼|$6|`OnHandDiscarded` DiscardedFaceAtLeast → CreateCard|
|꺾은 꽃|Cut Flowers|`cut_flowers`|언커먼|$8|`OnHandDiscarded` Always → ModifyCard|
|낫 같은 달|Scythe Moon|`scythe_moon`|레어|$8|`Passive` Always → ChangeRule · `OnHandPlayed` Always → PerUnit|
|큰 퇴비|Great Compost|`great_compost`|레어|$9|`OnCardDiscarded` Always → GrowSelf|
|맨 화단|Bare Bed|`bare_bed`|레어|$8|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|키질 바람|Winnow Wind|`winnow_wind`|레어|$9|`OnHandDiscarded` Always → ModifyCard|
|끝없는 가을|Endless Autumn|`endless_autumn`|전설|$10|`Passive` Always → ChangeRule · `OnCardDiscarded` Always → GrowSelf|

---

EOD
