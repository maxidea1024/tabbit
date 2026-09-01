# 값을 만드는 기관

> [확장 조커로](../expansion.md)

---

돈과 누적과 대가로 값을 만드는 계열입니다. 라운드를 넘겨야 값이 커집니다.

**이 파일은 생성물입니다.** 값을 고치려면 `design-data/tools/seedlib/` 의
해당 모듈을 고치고 `expansion_doc.py` 를 다시 돌립니다.

## 경제 25종

커먼 9 · 언커먼 10 · 레어 5 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|구리 저울|Copper Scale|`copper_scale`|커먼|$4|`OnHandPlayed` Always → PerUnit|
|양철 금고|Tin Till|`tin_till`|커먼|$5|`OnRoundEnd` Always → PerUnit|
|빚 서판|Debt Slate|`debt_slate`|커먼|$2|`Passive` Always → ChangeRule · `OnHandPlayed` Always → AddMult|
|돈주머니|Coin Purse|`coin_purse`|커먼|$5|`OnBlindSelect` Always → AddMoney|
|장터 좌판|Market Stall|`market_stall`|커먼|$5|`OnShopEnter` Always → AddMoney|
|값 깎는 이|Haggler|`haggler`|커먼|$4|`Passive` Always → ChangeRule|
|놋 분동|Brass Weight|`brass_weight`|커먼|$5|`OnHandPlayed` Money → AddMult|
|빈 주머니|Empty Purse|`empty_purse`|커먼|$4|`OnHandPlayed` Money → MulMult|
|통행 문|Toll Gate|`toll_gate`|커먼|$4|`OnRoundStart` Always → AddMoney · `OnHandPlayed` Always → AddMult|
|이자 책|Interest Book|`interest_book`|언커먼|$7|`Passive` Always → ChangeRule|
|동전 압착기|Coin Press|`coin_press`|언커먼|$7|`OnRoundEnd` Always → MulMoney|
|금고|Strongbox|`strongbox`|언커먼|$7|`OnRoundEnd` Always → GrowSelf · `OnRoundEnd` Always → PerUnit|
|금박 장부|Gilt Ledger|`gilt_ledger`|언커먼|$8|`OnHandPlayed` Always → PerUnit|
|돈놀이꾼|Usurer|`usurer`|언커먼|$6|`Passive` Always → ChangeRule|
|쓴 어음|Spent Note|`spent_note`|언커먼|$6|`OnShopExit` Always → GrowSelf|
|구휼 그릇|Alms Bowl|`alms_bowl`|언커먼|$6|`OnRoundEnd` Money → AddMoney|
|계량대|Weighbridge|`weighbridge`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|거래인|Broker|`broker`|언커먼|$7|`OnJokerSold` Always → AddMoney|
|봉한 상자|Sealed Chest|`sealed_chest`|언커먼|$8|`Passive` Always → ChangeRule · `OnRoundEnd` Always → AddMoney|
|왕실 주조소|Royal Mint|`royal_mint`|레어|$10|`OnHandPlayed` Always → PerUnit|
|빚의 나선|Debt Spiral|`debt_spiral`|레어|$8|`Passive` Always → ChangeRule · `OnHandPlayed` Money → MulMult|
|십일조 곳간|Tithe Barn|`tithe_barn`|레어|$9|`OnBossDefeated` Always → MulMoney|
|셈방|Counting House|`counting_house`|레어|$9|`OnHandPlayed` Always → PerUnit|
|무쇠 준비금|Iron Reserve|`iron_reserve`|레어|$8|`Passive` Always → ChangeRule|
|황금 무더기|Golden Hoard|`golden_hoard`|전설|$10|`OnHandPlayed` Always → PerUnit|

## 성장 25종

커먼 8 · 언커먼 11 · 레어 5 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|곧은 뿌리|Tap Root|`tap_root`|커먼|$5|`OnRoundEnd` Always → GrowSelf|
|나이테|Year Ring|`year_ring`|커먼|$5|`OnBossDefeated` Always → GrowSelf|
|기는 덩굴|Runner Vine|`runner_vine`|커먼|$4|`OnCardAdded` Always → GrowSelf|
|눈 비늘|Bud Scale|`bud_scale`|커먼|$4|`OnBlindSelect` Always → GrowSelf|
|수액 오름|Sap Rise|`sap_rise`|커먼|$5|`OnRoundStart` Always → GrowSelf|
|이끼 계단|Moss Stair|`moss_stair`|커먼|$4|`OnScoreResolved` CardCount → GrowSelf|
|껍질 주름|Bark Fold|`bark_fold`|커먼|$5|`OnScoreResolved` LastHand → GrowSelf|
|첫 잎|First Leaf|`first_leaf`|커먼|$4|`OnScoreResolved` FirstHand → GrowSelf|
|심재|Heartwood|`heartwood`|언커먼|$7|`OnRoundEnd` Always → GrowSelf|
|두름 띠|Girdle Band|`girdle_band`|언커먼|$7|`OnCardDestroyed` Always → GrowSelf|
|잘린 그루|Pollard Stump|`pollard_stump`|언커먼|$7|`OnJokerSold` Always → GrowSelf|
|접합부|Graft Union|`graft_union`|언커먼|$8|`OnConsumableUsed` ConsumableKind → GrowSelf|
|홀씨 구름|Spore Cloud|`spore_cloud`|언커먼|$7|`OnConsumableUsed` ConsumableKind → GrowSelf|
|깊은 뿌리|Deep Root|`deep_root`|언커먼|$8|`OnRoundEnd` Always → GrowSelf · `Passive` Always → ChangeRule|
|오르는 장미|Climbing Rose|`climbing_rose`|언커먼|$7|`OnScoreResolved` HandContains → GrowSelf|
|고리버들 띠|Withy Band|`withy_band`|언커먼|$6|`OnScoreResolved` Always → GrowSelf|
|옹이|Knot Wood|`knot_wood`|언커먼|$7|`OnCardScored` CardEnhanced → GrowSelf|
|그늘잎|Shade Leaf|`shade_leaf`|언커먼|$6|`OnScoreResolved` NoFaceScored → GrowSelf|
|메꽃|Bind Weed|`bind_weed`|언커먼|$8|`OnScoreResolved` Always → GrowSelf · `OnRoundEnd` Always → GrowSelf|
|늙은 참나무|Old Oak|`old_oak`|레어|$9|`OnRoundEnd` Always → GrowSelf|
|뿌리혹|Crown Gall|`crown_gall`|레어|$8|`OnCardDestroyed` Always → GrowSelf|
|대목|Wild Stock|`wild_stock`|레어|$9|`OnCardAdded` Always → GrowSelf|
|선 숲|Standing Grove|`standing_grove`|레어|$9|`OnHandPlayed` Always → PerUnit|
|사철 열림|Everbearing|`everbearing`|레어|$10|`OnScoreResolved` Always → GrowSelf|
|세계나무|World Tree|`world_tree`|전설|$10|`OnRoundEnd` Always → GrowSelf · `Passive` Always → ChangeRule|

## 위험 25종

커먼 8 · 언커먼 11 · 레어 5 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|가시 고리|Thorn Ring|`thorn_ring`|커먼|$4|`OnHandPlayed` Always → MulMult · `OnRoundEnd` Always → DestroyJoker (1/8)|
|까마중 잔|Nightshade Cup|`nightshade_cup`|커먼|$5|`OnHandPlayed` Always → AddMult · `OnRoundEnd` Always → GrowSelf · `OnRoundEnd` CounterAtMost → DestroyJoker|
|서릿발|Frost Bite|`frost_bite`|커먼|$5|`OnScoreResolved` Always → GrowSelf|
|벌 침|Bee Sting|`bee_sting`|커먼|$4|`OnCardScored` CardIsFace → AddMult · `OnCardScored` CardIsFace → DestroyCard (1/10)|
|독당근 잎|Hemlock Leaf|`hemlock_leaf`|커먼|$5|`OnHandPlayed` Always → AddChips · `Passive` Always → ChangeRule|
|갈라진 화분|Cracked Pot|`cracked_pot`|커먼|$4|`OnHandPlayed` Always → MulMult · `OnHandPlayed` Always → DestroyCard (1/6)|
|말벌집|Wasp Nest|`wasp_nest`|커먼|$5|`OnBlindSelect` Always → AddMoney · `OnBlindSelect` Always → DestroyCard|
|여린 유리|Brittle Glass|`brittle_glass`|커먼|$4|`OnCardScored` CardEnhancement → AddMult|
|피뿌리|Bloodroot|`bloodroot`|언커먼|$7|`OnRoundStart` Always → DestroyCard · `OnHandPlayed` Always → MulMult|
|살무사 고리|Viper Coil|`viper_coil`|언커먼|$8|`OnHandPlayed` HandsLeft → MulMult|
|죽은 가지|Dead Wood|`dead_wood`|언커먼|$7|`OnHandPlayed` Always → MulMult · `Passive` Always → ChangeRule|
|제물 그릇|Sacrifice Bowl|`sacrifice_bowl`|언커먼|$7|`OnBlindSelect` Always → DestroyJoker · `OnBlindSelect` Always → GrowSelf|
|빈 씨|Hollow Seed|`hollow_seed`|언커먼|$6|`OnScoreResolved` Always → GrowSelf · `OnBossDefeated` Always → DestroyJoker|
|무쇠 가시|Iron Thorn|`iron_thorn`|언커먼|$7|`OnScoreResolved` ScoreRatioAtLeast → PreventLoss|
|마름병 자리|Blight Spot|`blight_spot`|언커먼|$6|`OnRoundEnd` Always → DestroyCard · `OnHandPlayed` Always → PerUnit|
|녹슨 핀|Rust Pin|`rust_pin`|언커먼|$6|`OnRoundEnd` Always → GrowSelf|
|뱀 구덩이|Snake Pit|`snake_pit`|언커먼|$8|`OnHandPlayed` Always → DisableRandomJoker · `OnHandPlayed` Always → MulMult|
|덩굴옻나무|Poison Ivy|`poison_ivy`|언커먼|$7|`OnCardScored` Always → ModifyCard (1/4)|
|까마귀 밥|Crow Bait|`crow_bait`|언커먼|$6|`OnRoundEnd` Always → GrowSelf · `OnRoundEnd` Always → DestroyJoker (1/5)|
|독당근 화관|Hemlock Crown|`hemlock_crown`|레어|$9|`OnHandPlayed` Always → MulMult · `Passive` Always → ChangeRule|
|이빨 덫|Wolf Trap|`wolf_trap`|레어|$9|`Passive` Always → ChangeRule · `OnHandPlayed` CardCount → MulMult|
|검은 서리|Black Frost|`black_frost`|레어|$8|`OnScoreResolved` Always → GrowSelf|
|마지막 버팀|Last Stand|`last_stand`|레어|$10|`OnScoreResolved` ScoreRatioAtLeast → PreventLoss · `Passive` Always → ChangeRule|
|순교의 돌|Martyr Stone|`martyr_stone`|레어|$9|`Passive` Always → ChangeRule · `OnHandPlayed` Always → MulMult|
|겨울의 왕|Winter King|`winter_king`|전설|$10|`OnHandPlayed` Always → MulMult · `Passive` Always → ChangeRule · `Passive` Always → ChangeRule|

## 강화 25종

커먼 9 · 언커먼 11 · 레어 4 · 전설 1

|이름|영어|`id`|희귀도|값|효과|
|--|--|--|--|--|--|
|밀랍 인장|Wax Seal|`wax_seal`|커먼|$5|`OnCardScored` CardSeal → AddMult|
|파란 밀랍|Blue Wax|`blue_wax`|커먼|$5|`OnCardScored` CardSeal → AddChips|
|금 밀랍|Gold Wax|`gold_wax`|커먼|$5|`OnCardScored` CardSeal → AddMoney|
|자주 밀랍|Purple Wax|`purple_wax`|커먼|$5|`OnCardDiscarded` CardSeal → GrowSelf|
|박 띠|Foil Strip|`foil_strip`|커먼|$4|`OnCardScored` CardEdition → AddChips|
|홀로 띠|Holo Strip|`holo_strip`|커먼|$4|`OnCardScored` CardEdition → AddMult|
|덧칩 핀|Bonus Pin|`bonus_pin`|커먼|$4|`OnCardScored` CardEnhancement → AddChips|
|배수 핀|Mult Pin|`mult_pin`|커먼|$4|`OnCardScored` CardEnhancement → AddMult|
|들 카드 핀|Wild Pin|`wild_pin`|커먼|$5|`OnCardScored` CardEnhancement → AddMult|
|유리장이|Glazier|`glazier`|언커먼|$7|`OnRoundStart` Always → ModifyCard|
|대장 집게|Smith Tongs|`smith_tongs`|언커먼|$7|`OnRoundStart` Always → ModifyCard|
|인장 압착기|Seal Press|`seal_press`|언커먼|$7|`OnRoundStart` Always → ModifyCard|
|금박장이|Gilder|`gilder`|언커먼|$8|`OnCardScored` CardEnhancement → MulMult|
|밀랍 셈|Wax Tally|`wax_tally`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|유리 셈|Glass Tally|`glass_tally`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|행운 셈|Lucky Tally|`lucky_tally`|언커먼|$7|`OnHandPlayed` Always → PerUnit|
|돌 놓는 이|Stone Setter|`stone_setter`|언커먼|$6|`OnBlindSelect` Always → AddCard|
|판본 상자|Edition Case|`edition_case`|언커먼|$8|`Passive` Always → ChangeRule|
|에나멜 가마|Enamel Kiln|`enamel_kiln`|언커먼|$8|`OnCardScored` CardEdition → MulMult|
|봉인된 덱|Sealed Deck|`sealed_deck`|언커먼|$7|`OnBlindSelect` Always → ModifyCard|
|유리 장인|Master Glazier|`master_glazier`|레어|$9|`OnCardScored` CardEnhancement → MulMult|
|인장 반지|Seal Ring|`seal_ring`|레어|$9|`OnCardScored` Always → ModifyCard (1/4)|
|박 대장간|Foil Forge|`foil_forge`|레어|$10|`OnBlindSelect` Always → ModifyCard|
|합금 화단|Alloy Bed|`alloy_bed`|레어|$9|`OnCardScored` CardEnhanced → Retrigger|
|왕관 유리|Crown Glass|`crown_glass`|전설|$10|`OnCardScored` Always → ModifyCard · `OnCardScored` CardEnhancement → MulMult|

---

EOD
