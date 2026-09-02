# 커먼 61종

> [조커 목록으로](../jokers.md)

---

상점 조커의 70%가 여기서 나옵니다. 값은 $1 ~ $6입니다.

|원작|우리|`id`|가격|효과|VM 변종|
|--|--|--|--|--|--|
|Joker|둥근 잔가지|`twig`|$2|배수 +4|`AddMult`|
|Greedy Joker|석영꽃|`quartz_bloom`|$5|다이아 카드 득점마다 배수 +3|`AddMult` + `CardSuit`|
|Lusty Joker|양귀비꽃|`poppy_bloom`|$5|하트 카드 득점마다 배수 +3|`AddMult` + `CardSuit`|
|Wrathful Joker|쐐기풀꽃|`nettle_bloom`|$5|스페이드 카드 득점마다 배수 +3|`AddMult` + `CardSuit`|
|Gluttonous Joker|클로버꽃|`clover_bloom`|$5|클럽 카드 득점마다 배수 +3|`AddMult` + `CardSuit`|
|Jolly Joker|휘파람새|`warbler`|$3|페어 포함 시 배수 +8|`AddMult` + `HandContains`|
|Zany Joker|찌르레기|`starling`|$4|트리플 포함 시 배수 +12|`AddMult` + `HandContains`|
|Mad Joker|까치|`magpie`|$4|투페어 포함 시 배수 +10|`AddMult` + `HandContains`|
|Crazy Joker|제비|`swallow`|$4|스트레이트 포함 시 배수 +12|`AddMult` + `HandContains`|
|Droll Joker|되새|`finch`|$4|플러시 포함 시 배수 +10|`AddMult` + `HandContains`|
|Sly Joker|귀뚜라미|`cricket`|$3|페어 포함 시 칩 +50|`AddChips` + `HandContains`|
|Wily Joker|사마귀|`mantis`|$4|트리플 포함 시 칩 +100|`AddChips` + `HandContains`|
|Clever Joker|딱정벌레|`beetle`|$4|투페어 포함 시 칩 +80|`AddChips` + `HandContains`|
|Devious Joker|그리마|`centipede`|$4|스트레이트 포함 시 칩 +100|`AddChips` + `HandContains`|
|Crafty Joker|개똥벌레|`firefly`|$4|플러시 포함 시 칩 +80|`AddChips` + `HandContains`|
|Half Joker|반쪽 화분|`half_pot`|$5|낸 카드가 3장 이하면 배수 +20|`AddMult` + `CardCountAtMost`|
|Credit Card|외상 장부|`ledger_note`|$1|-$20까지 빚을 집니다|`ChangeRule`|
|Banner|깃발천|`bunting`|$5|남은 버리기마다 칩 +30|`AddChips` × `DiscardsLeft`|
|Mystic Summit|마지막 능선|`last_ridge`|$5|남은 버리기가 0이면 배수 +15|`AddMult` + `DiscardsLeft`|
|8 Ball|여덟째 종|`eight_bell`|$5|8 득점마다 1/4 확률로 타로 카드 생성|`CreateCard` + `Probability`|
|Misprint|번짐|`smudge`|$4|배수 +0 ~ +23|`AddMult` + `RandomRange`|
|Raised Fist|낮은 가지|`low_branch`|$5|패의 최저 랭크 카드의 칩값 2배를 배수로|`AddMult` + `LowestHeldRank`|
|Chaos the Clown|헝클림|`muddle`|$4|상점마다 무료 리롤 1회|`ChangeRule`|
|Scary Face|무서운 가면|`grim_mask`|$4|그림 카드 득점마다 칩 +30|`AddChips` + `CardIsFace`|
|Abstract Joker|엉킴|`tangle`|$4|조커 하나마다 배수 +3|`AddMult` × `JokerCount`|
|Delayed Gratification|더딘 항아리|`slow_pot`|$4|버리기를 하나도 쓰지 않으면 라운드 종료 시 버리기당 $2|`AddMoney` + `DiscardsUnused`|
|Gros Michel|바람 맞은 배|`windfall_pear`|$5|배수 +15. 라운드 종료 시 1/6 확률로 파괴|`AddMult` + `SelfDestruct`|
|Even Steven|짝수 담쟁이|`even_ivy`|$4|짝수 랭크 득점마다 배수 +4|`AddMult` + `CardRankSet`|
|Odd Todd|홀수 오리나무|`odd_alder`|$4|홀수 랭크 득점마다 칩 +31|`AddChips` + `CardRankSet`|
|Scholar|낡은 연감|`almanac`|$4|A 득점마다 칩 +20, 배수 +4|`AddChips`+`AddMult` + `CardRankSet`|
|Business Card|거래 명함|`trade_card`|$4|그림 카드 득점마다 1/2 확률로 $2|`AddMoney` + `Probability`|
|Supernova|새로 뜬 별|`nova_bud`|$5|이번 런에 이 족보를 낸 횟수를 배수로|`AddMult` × `HandPlayCount`|
|Ride the Bus|긴 길|`long_path`|$6|그림 카드가 득점하지 않은 연속 핸드마다 배수 +1 누적|`GrowSelf(AddMult)`|
|Egg|씨주머니|`seed_pod`|$4|라운드 종료 시 판매가 +$3|`GrowSelf(SellValue)`|
|Runner|덩굴손|`creeper`|$5|스트레이트 포함 시 칩 +15 누적|`GrowSelf(AddChips)`|
|Ice Cream|서린 유리|`frost_pane`|$5|칩 +100. 핸드마다 칩 -5|`GrowSelf(AddChips)` 감소|
|Splash|소나기|`downpour`|$3|낸 카드 전부가 득점합니다|`ChangeRule`|
|Blue Joker|파란 등|`blue_lantern`|$5|덱에 남은 카드마다 칩 +2|`AddChips` × `DeckRemaining`|
|Faceless Joker|빈 가면|`blank_mask`|$4|그림 카드 3장 이상을 한 번에 버리면 $5|`AddMoney` + `DiscardContains`|
|Green Joker|초록 싹|`green_shoot`|$4|핸드마다 배수 +1, 버리기마다 배수 -1|`GrowSelf(AddMult)` 양방향|
|Superposition|겹친 상태|`twin_state`|$4|A와 스트레이트가 함께 있으면 타로 카드 생성|`CreateCard` + `HandContains`|
|To Do List|할 일 목록|`chore_list`|$4|지정된 족보를 내면 $4. 족보는 라운드마다 바뀝니다|`AddMoney` + `TargetHand`|
|Cavendish|과수원 배|`orchard_pear`|$4|배수 ×3. 라운드 종료 시 1/1000 확률로 파괴|`MulMult` + `SelfDestruct`|
|Red Card|붉은 표|`red_ticket`|$5|팩을 스킵할 때마다 배수 +3 누적|`GrowSelf(AddMult)`|
|Square Joker|네모 격자|`square_trellis`|$4|낸 카드가 정확히 4장이면 칩 +4 누적|`GrowSelf(AddChips)`|
|Riff-Raff|잡것들|`rabble`|$6|블라인드 선택 시 커먼 조커 2장 생성|`CreateCard` + `OnBlindSelect`|
|Photograph|은판 사진|`tintype`|$5|첫 그림 카드 득점 시 배수 ×2|`MulMult` + `FirstMatch`|
|Reserved Parking|정원 벤치|`garden_bench`|$6|패의 그림 카드마다 1/2 확률로 $1|`AddMoney` + `Probability` + `Held`|
|Mail-In Rebate|환급 전표|`rebate_slip`|$4|지정된 랭크를 버릴 때마다 $5. 랭크는 라운드마다 바뀝니다|`AddMoney` + `TargetRank`|
|Hallucination|백일몽|`daydream`|$4|팩을 열 때 1/2 확률로 타로 카드 생성|`CreateCard` + `Probability`|
|Fortune Teller|손금 읽는 이|`palm_reader`|$6|이번 런에 쓴 타로 카드 수만큼 배수 +1|`AddMult` × `TarotUsed`|
|Juggler|던지는 이|`spinner`|$4|패 크기 +1|`ChangeRule`|
|Drunkard|한잔하는 이|`tippler`|$4|라운드마다 버리기 +1|`ChangeRule`|
|Golden Joker|금박 동전|`gilt_coin`|$6|라운드 종료 시 $4|`AddMoney` + `OnRoundEnd`|
|Popcorn|먼지버섯|`puffball`|$5|배수 +20. 라운드마다 배수 -4|`GrowSelf(AddMult)` 감소|
|Walkie Talkie|양철 나팔|`tin_horn`|$4|10 또는 4 득점마다 칩 +10, 배수 +4|`AddChips`+`AddMult` + `CardRankSet`|
|Smiley Face|웃는 가면|`glad_mask`|$4|그림 카드 득점마다 배수 +5|`AddMult` + `CardIsFace`|
|Golden Ticket|금박 표|`gilt_stub`|$5|`Gold Card` 득점마다 $4|`AddMoney` + `CardEnhancement`|
|Swashbuckler|가시 칼|`bramble_blade`|$4|다른 조커 전부의 판매가 합계를 배수로|`AddMult` × `OtherJokerSellValue`|
|Shoot the Moon|달 겨눔|`moonshot`|$5|패의 Q마다 배수 +13|`AddMult` + `Held` + `CardRankSet`|
|Hanging Chad|찢긴 쪽지|`torn_tab`|$4|득점 첫 카드를 2회 추가 재발동|`Retrigger(2)` + `FirstMatch`|

## 눈에 걸리는 것

|무엇|왜 걸리는가|
|--|--|
|`smudge` 의 `+0 ~ +23`|**난수가 득점 안에 있습니다.** 리플레이 대조가 여기서 갈라지면 PRNG 스트림 분리가 잘못된 것입니다|
|`low_branch`|패의 최저 랭크를 봅니다 — 득점 카드가 아니라 **패에 남은 카드**입니다|
|`frost_pane` · `puffball` · `green_shoot`|**감소하는 누적**입니다. `GrowSelf`가 음수 증분을 받아야 합니다|
|`chore_list` · `rebate_slip`|**라운드마다 대상이 바뀝니다.** 그 대상이 런 상태이고 세이브에 들어갑니다|
|`downpour` · `spinner` · `tippler` · `ledger_note` · `muddle`|`ChangeRule` 입니다 — 득점에 값을 더하지 않고 **규칙 자체를 바꿉니다**|

---

EOD
