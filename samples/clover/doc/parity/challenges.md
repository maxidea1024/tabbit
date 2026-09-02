# 챌린지 20종

> [대조표로](../parity.md)

---

원작의 챌린지 20종입니다. **규칙과 수치는 그대로 재현하고 이름은 갈아냅니다** —
[대조 원칙](../parity.md#그대로-쓰는-것과-자작하는-것)이고, 조커 150종과 같은 처리입니다.

설계는 [챌린지](../challenge.md)에 있습니다. 여기 있는 것은 「원작이 무엇을 정하는가」뿐입니다.

**20종 전부 개별 문서로 확인하였습니다.** 갈래를 묶어 놓은 문서는 금지 목록을 덜 싣고
있었습니다 — `bare_field` 에서 타로 2종과 유령 3종이 빠져 있었고, 개별 문서에는 있었습니다.

## 우리 이름 20종

원작 이름이 창작 표현이므로 갈아냅니다. **규칙을 가리키는 이름으로 지었고, 데이터에 적기
전까지는 바꿀 수 있습니다.**

|#|원작|우리|`id`|
|--|--|--|--|
|1|The Omelette|마른 철|`dry_season`|
|2|15 Minute City|그림 마을|`face_town`|
|3|Rich get Richer|동전 천장|`coin_ceiling`|
|4|On a Knife's Edge|가위날|`shear_edge`|
|5|X-ray Vision|엎어진 패|`blind_draw`|
|6|Mad World|낮은 들|`low_field`|
|7|Luxury Tax|무거운 지갑|`heavy_purse`|
|8|Non-Perishable|상록|`evergreen`|
|9|Medusa|돌이 된 얼굴|`stone_court`|
|10|Double or Nothing|붉은 밀랍|`red_wax`|
|11|Typecast|굳는 편성|`sealed_fate`|
|12|Inflation|오르는 값|`rising_price`|
|13|Bram Poker|덩굴의 밤|`vine_night`|
|14|Fragile|유리밭|`glass_field`|
|15|Monolith|돌의 줄|`stone_row`|
|16|Blast Off|하늘길|`sky_road`|
|17|Five-Card Draw|다섯 잎|`five_leaves`|
|18|Golden Needle|한 올|`single_thread`|
|19|Cruelty|메마른 길|`barren_road`|
|20|Jokerless|맨 밭|`bare_field`|

## 공통 규칙

|무엇|값|
|--|--|
|해금|덱 15종 중 **5종으로 이기면** 처음 5개가 열립니다. 하나를 깨면 다음 하나가 열립니다|
|스테이크|흰색 고정|
|승리 조건|안테 8. 일반 런과 같습니다|
|해금·도전과제|**오르지 않습니다.** 챌린지의 조건이 그것들을 너무 쉽게 만듭니다|

---

## 규칙과 시작 소지품

**시작 덱 칸이 비면 표준 52장입니다.** 15종이 그렇습니다.
`E` 는 `Eternal`, `N` 은 `Negative`, `P` 는 자리 고정입니다.

|#|`id`|시작 덱|시작 소지품|규칙|
|--|--|--|--|--|
|1|`dry_season`|—|`seed_pod` ×5|모든 블라인드 보상 0 · 남은 핸드 수입 0 · 이자 없음|
|2|`face_town`|**52장** — J·Q·K 각 8장(24) + 4~10 각 무늬 1장(28). A·2·3 없음|`long_path` `E` · `stepping_stone` `E`|—|
|3|`coin_ceiling`|—|바우처 `seed_money` · `money_tree`|시작 금액 $100 · **칩이 보유액을 넘지 못함**|
|4|`shear_edge`|—|`pruning_shears` `E` `P`|—|
|5|`blind_draw`|—|—|**4장에 1장이 엎어진 채로 뽑힘**|
|6|`low_field`|**32장** — 랭크 2~9만|`face_pattern` `E` `N` · `trade_card` `E`|남은 핸드 수입 0 · 이자 없음|
|7|`heavy_purse`|—|—|패 크기 10 · **보유 $5마다 패 크기 -1**|
|8|`evergreen`|—|—|**모든 조커가 `Eternal`**|
|9|`stone_court`|**52장** — 그림 카드 12장이 `Stone` 으로 바뀜|`pebble_jar` `E`|—|
|10|`red_wax`|**52장** — 전부 붉은 인장|—|**낸 카드가 득점 뒤에 무력화됨**|
|11|`sealed_fate`|—|—|**안테 4 보스 격파 시** 모든 조커가 `Eternal` · 조커 슬롯 0|
|12|`rising_price`|—|`ledger_note`|**살 때마다 가격이 영구히 $1 오름**|
|13|`vine_night`|—|`leech_vine` `E` · 타로 `the_emperor` `the_empress` · 바우처 `magic_trick` `illusion`|**상점에 조커가 나오지 않음**|
|14|`glass_field`|**52장** — 전부 유리|`loaded_dice` `E` `N` ×2|—|
|15|`stone_row`|—|`standing_stone` `E` · `pebble_jar` `E` `N`|—|
|16|`sky_road`|—|`star_chart` `E` · `sky_rocket` `E` · 바우처 `planet_merchant` `planet_tycoon`|핸드 2 · 버리기 2 · 조커 슬롯 4|
|17|`five_leaves`|—|`sharper` · `twig`|패 크기 5 · 버리기 6 · 조커 슬롯 7|
|18|`single_thread`|—|`ledger_note`|시작 금액 $10 · 핸드 1 · 버리기 6 · **버릴 때마다 $1**|
|19|`barren_road`|—|—|조커 슬롯 3 · 스몰·빅 블라인드 보상 0|
|20|`bare_field`|—|—|조커 슬롯 0 · **상점에 조커가 나오지 않음**|

바우처가 짝으로 붙는 셋(`coin_ceiling` · `vine_night` · `sky_road`)은 **기본과 상위가
함께**입니다 — `money_tree` 는 `seed_money` 의 상위이고, `illusion` 은 `magic_trick` 의,
`planet_tycoon` 은 `planet_merchant` 의 상위입니다.

---

## 금지 목록

**금지가 있는 것이 10종이고, 나머지 10종은 금지가 없습니다.** 아래에 없는 `id` 가 그
10종입니다.

|#|`id`|금지|
|--|--|--|
|1|`dry_season`|**조커** `moon_ladder` `sky_rocket` `gilt_coin` `orbit_dish` · **바우처** `seed_money` `money_tree`|
|6|`low_field`|**보스** `the_plant`|
|8|`evergreen`|**조커** `windfall_pear` `orchard_pear` `frost_pane` `broad_bean` `noodle_pot` `soda_cap` `fizz_bottle` `puffball` `old_bones` `faint_outline` `ring_fighter` · **보스** `verdant_leaf`|
|11|`sealed_fate`|**보스** `verdant_leaf`|
|12|`rising_price`|**바우처** `clearance_sale` `liquidation`|
|14|`glass_field`|**조커** `pebble_jar` `leech_vine` `gilt_mask` `deed_stamp` · **타로** `the_magician` `the_empress` `the_hierophant` `the_chariot` `the_devil` `the_tower` `the_lovers` · **유령** `incantation` `grim` `familiar` · **바우처** `magic_trick` `illusion` · **팩** `standard_normal` `standard_jumbo` `standard_mega` · **태그** `standard`|
|16|`sky_road`|**조커** `night_thief` · **바우처** `grabber` `nacho_tong`|
|17|`five_leaves`|**조커** `spinner` `fiddler` `broad_bean`|
|18|`single_thread`|**조커** `night_thief` · **바우처** `grabber` `nacho_tong`|
|20|`bare_field`|**타로** `judgement` `the_wheel_of_fortune` `temperance` · **유령** `wraith` `the_soul` `ectoplasm` `ankh` `hex` · **태그** `uncommon` `rare` `negative` `foil` `holographic` `polychrome` `buffoon` `topup` · **보스** `crimson_heart` `verdant_leaf` `amber_acorn` · **바우처** `antimatter` · **팩** `buffoon_normal` `buffoon_jumbo` `buffoon_mega`|

원작은 팩을 갈래마다 4종으로 세고 **우리 데이터는 3종입니다**(`normal` · `jumbo` · `mega`).
[경제와 상점](economy-and-shop.md)의 팩 대조가 그렇게 되어 있으므로 여기서도 3종을 적습니다 —
어긋난 것이면 그쪽이 먼저입니다.

### 금지가 규칙과 겹치는 자리

`bare_field` 는 「조커 슬롯 0」과 「상점에 조커 없음」이 규칙이고, **조커를 얻는 나머지 길을
금지 목록이 막습니다** — 타로·유령·태그·보스·바우처·팩입니다. 규칙 하나로 되지 않는 것을
원작이 목록으로 처리하고, 그것을 그대로 옮깁니다.

`vine_night` 는 상점만 막고 나머지 길은 열어 둡니다. **같은 규칙에 목록이 다른 것이
이 둘의 차이 전부입니다.**

`evergreen` 이 금지하는 조커 11종은 **원작에서 `Eternal` 이 붙지 않는 그 11종입니다.**
「모든 조커가 `Eternal`」이 그것들에는 걸리지 않아 팔 수 있는 조커가 남으므로 목록으로
막습니다. `sealed_fate` 는 같은 11종을 금지하지 않습니다 — 안테 4까지는 그것들이 정상으로
동작해야 하기 때문입니다.

---

## 조커에 붙지 않는 `Eternal` 11종

**우리 데이터에 이 성질이 없습니다.** `Joker` 표에 `blueprint_ok` 는 있고 그 짝이 되는
칸이 없습니다 — [규칙 문서](../challenge/rules.md#먼저-고쳐야-하는-것-3건)에 적었습니다.

|원작|우리 `id`|`Eternal` 이 붙지 않는 이유|
|--|--|--|
|Gros Michel|`windfall_pear`|스스로 파괴됩니다|
|Cavendish|`orchard_pear`|스스로 파괴됩니다|
|Ice Cream|`frost_pane`|값이 0이 되면 사라집니다|
|Turtle Bean|`broad_bean`|값이 0이 되면 사라집니다|
|Ramen|`noodle_pot`|값이 0이 되면 사라집니다|
|Popcorn|`puffball`|값이 0이 되면 사라집니다|
|Seltzer|`fizz_bottle`|세기가 끝나면 사라집니다|
|Diet Cola|`soda_cap`|팔아야 효과가 납니다|
|Luchador|`ring_fighter`|팔아야 효과가 납니다|
|Invisible Joker|`faint_outline`|팔아야 효과가 납니다|
|Mr. Bones|`old_bones`|막고 나서 스스로 파괴됩니다|

**챌린지만의 문제가 아닙니다.** 검은 스테이크가 30% 확률로 `Eternal` 을 붙이므로, 지금은
이 11종에도 붙습니다.

---

## 조커 이름 대조 34종

챌린지가 이름으로 지목하는 조커입니다. 전부 [조커 150종](jokers.md)에 있습니다.

|원작|우리 `id`|원작|우리 `id`|
|--|--|--|--|
|Egg|`seed_pod`|To the Moon|`moon_ladder`|
|Ride the Bus|`long_path`|Rocket|`sky_rocket`|
|Shortcut|`stepping_stone`|Golden Joker|`gilt_coin`|
|Ceremonial Dagger|`pruning_shears`|Satellite|`orbit_dish`|
|Pareidolia|`face_pattern`|Gros Michel|`windfall_pear`|
|Business Card|`trade_card`|Cavendish|`orchard_pear`|
|Marble Joker|`pebble_jar`|Ice Cream|`frost_pane`|
|Credit Card|`ledger_note`|Turtle Bean|`broad_bean`|
|Vampire|`leech_vine`|Ramen|`noodle_pot`|
|Oops! All 6s|`loaded_dice`|Diet Cola|`soda_cap`|
|Obelisk|`standing_stone`|Seltzer|`fizz_bottle`|
|Constellation|`star_chart`|Popcorn|`puffball`|
|Card Sharp|`sharper`|Mr. Bones|`old_bones`|
|Joker|`twig`|Invisible Joker|`faint_outline`|
|Burglar|`night_thief`|Luchador|`ring_fighter`|
|Juggler|`spinner`|Midas Mask|`gilt_mask`|
|Troubadour|`fiddler`|Certificate|`deed_stamp`|

바우처·타로·유령·태그·보스·팩의 이름은 갈아내지 않았으므로 원작의 것이 그대로
식별자입니다.

---

EOD
