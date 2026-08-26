# 언커먼 64종

> [조커 목록으로](../jokers.md)

---

상점 조커의 25%가 여기서 나옵니다. 값은 $4 ~ $8입니다.

|원작|우리|`id`|값|효과|VM 변종|
|--|--|--|--|--|--|
|Joker Stencil|빈 액자|`empty_frame`|$8|빈 조커 슬롯마다 배수 ×1|`MulMult` × `EmptySlots`|
|Four Fingers|네 마디|`four_knuckles`|$7|플러시와 스트레이트를 4장으로 이룹니다|`ChangeRule`|
|Mime|무언극배우|`mummer`|$5|패에 든 카드의 효과를 재발동합니다|`Retrigger` + `Held`|
|Ceremonial Dagger|전정 가위|`pruning_shears`|$6|블라인드 선택 시 오른쪽 조커를 파괴하고 판매가의 2배를 배수로 얻습니다|`DestroyJoker` + `GrowSelf(AddMult)`|
|Marble Joker|자갈 항아리|`pebble_jar`|$6|블라인드 선택 시 덱에 `Stone Card` 1장을 더합니다|`AddCard` + `OnBlindSelect`|
|Loyalty Card|도장 카드|`punch_card`|$5|6핸드마다 배수 ×4|`MulMult` + `EveryNHands`|
|Dusk|해거름|`twilight`|$5|라운드 마지막 핸드의 득점 카드 전부를 재발동합니다|`Retrigger` + `LastHand`|
|Fibonacci|나선 껍질|`spiral_shell`|$8|A · 2 · 3 · 5 · 8 득점마다 배수 +8|`AddMult` + `CardRankSet`|
|Steel Joker|강철 격자|`steel_trellis`|$7|덱의 `Steel Card` 하나마다 배수 ×0.2|`MulMult` × `DeckEnhancementCount`|
|Hack|눈금|`notch`|$6|2 · 3 · 4 · 5 를 재발동합니다|`Retrigger` + `CardRankSet`|
|Pareidolia|얼굴 무늬|`face_pattern`|$5|모든 카드를 그림 카드로 봅니다|`ChangeRule`|
|Space Joker|혜성 꼬리|`comet_tail`|$5|1/4 확률로 낸 족보의 레벨을 올립니다|`LevelUpHand` + `Probability`|
|Burglar|밤손님|`night_thief`|$6|블라인드 선택 시 핸드 +3, 버리기 전부 상실|`ChangeRule`|
|Blackboard|점판암 판|`slate_board`|$6|패의 모든 카드가 스페이드 또는 클럽이면 배수 ×3|`MulMult` + `AllHeldSuit`|
|Sixth Sense|예감|`hunch`|$6|라운드 첫 핸드가 6 한 장이면 파괴하고 유령 카드를 만듭니다|`DestroyCard` + `CreateCard`|
|Constellation|별자리표|`star_chart`|$6|행성 카드를 쓸 때마다 배수 ×0.1 누적|`GrowSelf(MulMult)`|
|Hiker|나그네|`wanderer`|$5|득점한 카드가 영구히 칩 +5를 얻습니다|`ModifyCard` + `OnCardScored`|
|Card Sharp|타짜|`sharper`|$6|이번 라운드에 이미 낸 족보면 배수 ×3|`MulMult` + `HandRepeated`|
|Madness|열병|`fever`|$7|스몰·빅 블라인드 선택 시 배수 ×0.5 누적, 무작위 조커 하나 파괴|`GrowSelf(MulMult)` + `DestroyJoker`|
|Séance|밤샘|`vigil`|$6|족보가 스트레이트 플러시면 무작위 유령 카드 생성|`CreateCard` + `HandContains`|
|Vampire|거머리 덩굴|`leech_vine`|$7|강화 카드 득점마다 배수 ×0.1 누적, 그 강화를 제거합니다|`GrowSelf(MulMult)` + `ModifyCard`|
|Shortcut|디딤돌|`stepping_stone`|$7|스트레이트에 랭크 1칸의 빈틈을 허용합니다|`ChangeRule`|
|Hologram|유리 그림자|`glass_ghost`|$7|덱에 카드가 더해질 때마다 배수 ×0.25 누적|`GrowSelf(MulMult)`|
|Cloud 9|아홉째 구름|`ninth_cloud`|$7|라운드 종료 시 덱의 9 하나마다 $1|`AddMoney` × `DeckRankCount`|
|Rocket|불화살|`sky_rocket`|$6|라운드 종료 시 $1. 보스 격파마다 +$2 누적|`AddMoney` + `GrowSelf(Money)`|
|Midas Mask|금박 가면|`gilt_mask`|$7|득점한 그림 카드가 `Gold Card`가 됩니다|`ModifyCard`|
|Luchador|링의 투사|`ring_fighter`|$5|팔면 지금 보스 블라인드를 무력화합니다|`OnSell` + `DisableBoss`|
|Gift Card|선물 꼬리표|`gift_tag`|$6|라운드 종료 시 모든 조커와 소모품의 판매가 +$1|`GrowOthers(SellValue)`|
|Turtle Bean|넓은 콩|`broad_bean`|$6|패 크기 +5. 라운드마다 -1|`ChangeRule` + 감소|
|Erosion|씻김|`washout`|$6|덱이 시작 장수보다 부족한 장수마다 배수 +4|`AddMult` × `DeckDeficit`|
|To the Moon|달 사다리|`moon_ladder`|$5|이자를 보유 $5마다 $1 더 받습니다|`ChangeRule`|
|Stone Joker|돌무지|`cairn`|$6|덱의 `Stone Card` 하나마다 칩 +25|`AddChips` × `DeckEnhancementCount`|
|Lucky Cat|복 두꺼비|`lucky_toad`|$6|`Lucky Card`가 발동할 때마다 배수 ×0.25 누적|`GrowSelf(MulMult)`|
|Diet Cola|음료 뚜껑|`soda_cap`|$6|팔면 무료 `Double Tag` 하나|`OnSell` + `CreateCard`|
|Trading Card|교환 쪽지|`swap_note`|$6|라운드 첫 버리기가 1장이면 파괴하고 $3|`DestroyCard` + `AddMoney`|
|Flash Card|섬광 쪽지|`flash_note`|$5|상점 리롤마다 배수 +2 누적|`GrowSelf(AddMult)`|
|Spare Trousers|여벌 장갑|`spare_gloves`|$6|투페어 포함 시 배수 +2 누적|`GrowSelf(AddMult)`|
|Ramen|국수 냄비|`noodle_pot`|$6|배수 ×2. 버린 카드 한 장마다 ×0.01 감소|`GrowSelf(MulMult)` 감소|
|Seltzer|탄산병|`fizz_bottle`|$6|다음 10핸드 동안 득점 카드 전부를 재발동합니다|`Retrigger` + `ChargeCounter(10)`|
|Castle|돌 성채|`stone_keep`|$6|지정된 무늬를 버릴 때마다 칩 +3 누적. 무늬는 라운드마다 바뀝니다|`GrowSelf(AddChips)` + `TargetSuit`|
|Acrobat|곡예사|`tumbler`|$6|라운드 마지막 핸드에 배수 ×3|`MulMult` + `LastHand`|
|Sock and Buskin|두 가면|`two_masks`|$6|득점한 그림 카드 전부를 재발동합니다|`Retrigger` + `CardIsFace`|
|Troubadour|악사|`fiddler`|$6|패 크기 +2. 라운드당 핸드 -1|`ChangeRule`|
|Certificate|증서 도장|`deed_stamp`|$6|라운드 시작 시 무작위 인장이 달린 카드 1장을 패에 더합니다|`AddCard` + `OnRoundStart`|
|Smeared Joker|번진 유리|`smudged_pane`|$7|하트와 다이아를 한 무늬로, 스페이드와 클럽을 한 무늬로 봅니다|`ChangeRule`|
|Throwback|옛 길|`old_route`|$6|이번 런에 스킵한 블라인드마다 배수 ×0.25|`MulMult` × `BlindsSkipped`|
|Rough Gem|원석|`raw_gem`|$7|다이아 카드 득점마다 $1|`AddMoney` + `CardSuit`|
|Bloodstone|피돌|`heartstone`|$7|하트 카드 득점마다 1/2 확률로 배수 ×1.5|`MulMult` + `Probability` + `CardSuit`|
|Arrowhead|화살 부싯돌|`arrow_flint`|$7|스페이드 카드 득점마다 칩 +50|`AddChips` + `CardSuit`|
|Onyx Agate|검은 잎|`onyx_leaf`|$7|클럽 카드 득점마다 배수 +7|`AddMult` + `CardSuit`|
|Glass Joker|파편 항아리|`shard_jar`|$6|`Glass Card`가 파괴될 때마다 배수 ×0.75 누적|`GrowSelf(MulMult)`|
|Showman|호객꾼|`barker`|$5|조커·타로·행성·유령이 중복해서 나올 수 있습니다|`ChangeRule`|
|Flower Pot|네 화단|`four_beds`|$6|득점 카드에 네 무늬가 모두 있으면 배수 ×3|`MulMult` + `AllSuitsPresent`|
|Merry Andy|즐거운 광대|`glad_fool`|$7|라운드마다 버리기 +3. 패 크기 -1|`ChangeRule`|
|Oops! All 6s|납 주사위|`loaded_dice`|$4|**모든 확률을 2배로 합니다**|`ChangeRule`|
|The Idol|목각 인형|`the_effigy`|$6|지정된 랭크와 무늬의 카드 득점마다 배수 ×2. 라운드마다 바뀝니다|`MulMult` + `TargetCard`|
|Seeing Double|겹보임|`double_sight`|$6|득점 카드에 클럽 하나와 다른 무늬 하나가 있으면 배수 ×2|`MulMult` + `SuitPair`|
|Matador|투우사|`bullfighter`|$7|낸 핸드가 보스 능력을 발동시키면 $8|`AddMoney` + `BossTriggered`|
|Satellite|접시 안테나|`orbit_dish`|$6|라운드 종료 시 이번 런에 쓴 고유 행성 카드마다 $1|`AddMoney` × `UniquePlanetUsed`|
|Cartomancer|카드 읽는 이|`card_reader`|$6|블라인드 선택 시 타로 카드 생성|`CreateCard` + `OnBlindSelect`|
|Astronomer|별 보는 이|`stargazer`|$8|상점의 행성 카드와 천체 팩이 무료입니다|`ChangeRule`|
|Bootstraps|구두끈|`bootlace`|$7|보유 $5마다 배수 +2|`AddMult` × `Money`|
|Bull|황소 멍에|`ox_yoke`|$6|보유 $1마다 칩 +2|`AddChips` × `Money`|
|Mr. Bones|낡은 뼈|`old_bones`|$5|점수가 요구의 25% 이상이면 패배를 막고 스스로 파괴됩니다|`PreventLoss` + `SelfDestruct`|

## 눈에 걸리는 것

|무엇|왜 걸리는가|
|--|--|
|`loaded_dice`|**다른 효과의 확률을 바꿉니다.** 효과 VM이 자기 자신의 확률 계산에 전역 배율을 받아야 합니다|
|`pruning_shears` · `fever`|**다른 조커를 파괴합니다.** 슬롯 순서가 발동 중에 바뀌므로 순회 방식이 규격에 적혀야 합니다|
|`fizz_bottle`|**남은 횟수가 있는 상태**입니다. 조커의 런 상태가 값 하나가 아닙니다|
|`old_bones`|**패배 판정에 개입합니다.** 득점이 아니라 라운드 결과를 바꿉니다|
|`wanderer` · `leech_vine` · `gilt_mask`|**덱의 카드를 영구히 바꿉니다.** 런 상태가 데이터의 사본을 들고 있어야 합니다|
|`gift_tag`|**다른 조커의 판매가를 올립니다.** `GrowSelf` 가 아니라 `GrowOthers` 입니다|

---

EOD
