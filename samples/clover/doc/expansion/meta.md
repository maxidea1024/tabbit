# 판 밖의 것

> [확장 조커로](../expansion.md)

---

소모품과 조커와 덱을 다루는 계열입니다. 사는 순서가 값을 정합니다.

**이 파일은 생성물입니다.** 값을 고치려면 `design-data/tools/seedlib/` 의
해당 모듈을 고치고 `expansion_doc.py` 를 다시 돌립니다.

## 소모품 25종

커먼 8 · 언커먼 11 · 레어 5 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|별가루|Star Dust|`star_dust`|커먼|$4|`OnRoundEnd` Always → CreateCard (1/3)|
|달 접시|Moon Dish|`moon_dish`|커먼|$5|`OnBossDefeated` Always → CreateCard|
|증기 구멍|Steam Vent|`steam_vent`|커먼|$5|`OnHandPlayed` EveryNHands → CreateCard|
|우량계|Rain Gauge|`rain_gauge`|커먼|$4|`OnConsumableUsed` Always → AddMoney|
|이슬 유리|Dew Glass|`dew_glass`|커먼|$4|`OnHandPlayed` Always → PerUnit|
|혜성 재|Comet Ash|`comet_ash`|커먼|$5|`OnPackOpened` Always → CreateCard (1/2)|
|하늘 렌즈|Sky Lens|`sky_lens`|커먼|$5|`Passive` Always → ChangeRule|
|카드 갑|Card Case|`card_case`|커먼|$4|`Passive` Always → ChangeRule|
|성운 항아리|Nebula Jar|`nebula_jar`|언커먼|$7|`OnConsumableUsed` ConsumableKind → AddMoney|
|물웅덩이|Tide Pool|`tide_pool`|언커먼|$7|`OnConsumableUsed` Always → GrowSelf|
|안개 종|Mist Bell|`mist_bell`|언커먼|$6|`OnShopExit` Always → CreateCard (1/2)|
|넋 주전자|Spirit Kettle|`spirit_kettle`|언커먼|$8|`OnBossDefeated` Always → CreateCard|
|아스트롤라베|Astrolabe|`astrolabe`|언커먼|$8|`Passive` Always → ChangeRule|
|비법 상자|Arcana Case|`arcana_case`|언커먼|$7|`Passive` Always → ChangeRule|
|행성 다이얼|Planet Dial|`planet_dial`|언커먼|$7|`OnRoundEnd` Always → LevelUpHand (1/3)|
|타로 압착기|Tarot Press|`tarot_press`|언커먼|$8|`OnConsumableUsed` ConsumableKind → CreateCard (1/4)|
|소금 그릇|Salt Bowl|`salt_bowl`|언커먼|$6|`OnConsumableUsed` ConsumableKind → AddMoney|
|구름 사다리|Cloud Ladder|`cloud_ladder`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|천구의|Orrery|`orrery`|언커먼|$8|`OnPackSkipped` Always → CreateCard|
|큰 합|Great Conjunction|`great_conjunction`|레어|$9|`OnRoundEnd` Always → LevelUpHand (1/6)|
|넋의 문|Spirit Gate|`spirit_gate`|레어|$9|`Passive` Always → ChangeRule|
|별 대장간|Star Forge|`star_forge`|레어|$10|`OnConsumableUsed` Always → GrowSelf|
|달 거울|Moon Mirror|`moon_mirror`|레어|$9|`OnBossDefeated` Always → CreateCard|
|빈 렌즈|Void Lens|`void_lens`|레어|$9|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|천체 기관|Celestial Engine|`celestial_engine`|전설|$10|`OnRoundEnd` Always → LevelUpHand · `Passive` Always → ChangeRule|

## 조커 25종

커먼 8 · 언커먼 10 · 레어 6 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|짝 유리|Paired Glass|`paired_glass`|커먼|$5|`OnHandPlayed` Always → PerUnit|
|그림자 상자|Shadow Box|`shadow_box`|커먼|$4|`OnHandPlayed` Always → PerUnit|
|쌍 화분|Twin Pot|`twin_pot`|커먼|$5|`OnBlindSelect` Always → CreateCard (1/3)|
|값표|Sale Tag|`sale_tag`|커먼|$4|`OnJokerSold` Always → GrowSelf|
|여벌 액자|Spare Frame|`spare_frame`|커먼|$5|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|덮개천|Dust Sheet|`dust_sheet`|커먼|$4|`OnHandPlayed` Always → PerUnit|
|가격표|Price Card|`price_card`|커먼|$5|`OnHandPlayed` Always → PerUnit|
|흐린 거울|Dim Mirror|`dim_mirror`|커먼|$5|`OnRoundEnd` Always → GrowOthers|
|액자 가게|Frame Shop|`frame_shop`|언커먼|$7|`OnShopEnter` Always → CreateCard (1/6)|
|희귀 대장|Rarity Ledger|`rarity_ledger`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|가득한 선반|Full Shelf|`full_shelf`|언커먼|$8|`OnHandPlayed` Always → PerUnit|
|물림|Hand Me Down|`hand_me_down`|언커먼|$6|`OnJokerSold` Always → GrowOthers|
|바꿈 액자|Swap Frame|`swap_frame`|언커먼|$7|`OnBlindSelect` Always → ModifyJoker|
|유산 쪽지|Estate Note|`estate_note`|언커먼|$6|`OnSell` Always → CreateCard|
|자리 핀|Slot Pin|`slot_pin`|언커먼|$7|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|경매 종|Auction Bell|`auction_bell`|언커먼|$7|`OnJokerSold` Always → MulMoney|
|거울 가루|Mirror Dust|`mirror_dust`|언커먼|$8|`OnHandPlayed` Always → PerUnit|
|대역|Understudy|`understudy`|언커먼|$7|`OnRoundStart` Always → GrowOthers|
|거울의 방|Hall of Mirrors|`hall_of_mirrors`|레어|$10|`OnHandPlayed` Always → PerUnit|
|검은 액자|Black Frame|`black_frame`|레어|$9|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|대본|Prompt Book|`prompt_book`|레어|$10|`OnBlindSelect` Always → GrowOthers|
|전당포|Pawnbroker|`pawnbroker`|레어|$8|`OnHandPlayed` Always → PerUnit|
|쌍씨|Twin Seed|`twin_seed`|레어|$9|`OnBlindSelect` Always → CreateCard|
|무대 일꾼|Stagehand|`stagehand`|레어|$9|`OnSell` Always → GrowOthers|
|큰 화랑|Grand Gallery|`grand_gallery`|전설|$10|`Passive` Always → ChangeRule · `OnHandPlayed` Always → PerUnit|

## 덱 25종

커먼 9 · 언커먼 11 · 레어 4 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|삽날|Spade Head|`spade_head`|커먼|$5|`OnRoundStart` Always → AddCard|
|모판|Seed Tray|`seed_tray`|커먼|$5|`OnBlindSelect` Always → AddCard|
|고르는 체|Sorting Sieve|`sorting_sieve`|커먼|$4|`OnHandPlayed` Always → PerUnit|
|성긴 줄|Thin Rows|`thin_rows`|커먼|$5|`OnHandPlayed` Always → PerUnit|
|그림 화단|Face Bed|`face_bed`|커먼|$4|`OnBlindSelect` Always → AddCard|
|으뜸 화단|Ace Bed|`ace_bed`|커먼|$5|`OnBlindSelect` Always → AddCard|
|손삽|Hand Spade|`hand_spade`|커먼|$4|`OnRoundStart` Always → ModifyCard|
|정원 줄|Garden Line|`garden_line`|커먼|$5|`OnHandPlayed` DeckEnhancedAtLeast → AddMult|
|잡초 뽑기|Weed Pull|`weed_pull`|커먼|$4|`OnHandDiscarded` Always → DestroyCard (1/3)|
|접칼|Graft Knife|`graft_knife`|언커먼|$7|`OnCardScored` Always → AddCard (1/8)|
|온상|Nursery Bed|`nursery_bed`|언커먼|$7|`OnRoundEnd` Always → AddCard|
|흙 검사|Soil Test|`soil_test`|언커먼|$6|`OnHandPlayed` Always → PerUnit|
|깊은 화단|Deep Bed|`deep_bed`|언커먼|$8|`OnHandPlayed` Always → PerUnit|
|돌려짓기|Crop Rotation|`crop_rotation`|언커먼|$7|`OnRoundEnd` Always → ModifyCard|
|백묵 줄|Chalk Line|`chalk_line`|언커먼|$6|`OnHandPlayed` DeckEnhancedAtLeast → MulMult|
|돌 줄|Stone Row|`stone_row`|언커먼|$6|`OnHandPlayed` Always → PerUnit|
|솎는 갈퀴|Culling Fork|`culling_fork`|언커먼|$7|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|넓은 화단|Wide Bed|`wide_bed`|언커먼|$6|`Passive` Always → ChangeRule · `Passive` Always → ChangeRule|
|씨 뿌리개|Seed Drill|`seed_drill`|언커먼|$8|`OnRoundStart` Always → AddCard|
|금 이랑|Gold Furrow|`gold_furrow`|언커먼|$7|`OnBlindSelect` Always → ModifyCard|
|큰 접붙임|Great Graft|`great_graft`|레어|$9|`OnCardScored` Always → AddCard (1/3)|
|계단 화단|Terraced Bed|`terraced_bed`|레어|$9|`OnHandPlayed` Always → PerUnit|
|유리 온실|Hothouse|`hothouse`|레어|$10|`OnRoundEnd` Always → ModifyCard · `OnRoundEnd` Always → ModifyCard|
|돌 정원|Stone Garden|`stone_garden`|레어|$9|`Passive` Always → ChangeRule · `OnHandPlayed` Always → AddMult|
|첫 정원|First Garden|`first_garden`|전설|$10|`OnRoundStart` Always → AddCard · `OnHandPlayed` Always → PerUnit|

---

EOD
