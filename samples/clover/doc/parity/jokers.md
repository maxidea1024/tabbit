# 조커 150종

> [대조표로](../parity.md)

---

**여기가 이 샘플에서 유일하게 이름을 갈아내는 자리입니다.** 원작 조커의 이름과 설명문과 그림은
원작의 창작이므로, 규칙과 수치만 가져오고 이름은 새로 지었습니다.
[대조 원칙](../parity.md#그대로-쓰는-것과-자작하는-것)이 그것입니다.

## 목록

|희귀도|종수|문서|
|--|--|--|
|`Common`|61|[커먼 61종](jokers/common.md)|
|`Uncommon`|64|[언커먼 64종](jokers/uncommon.md)|
|`Rare`|20|[레어 20종](jokers/rare.md)|
|`Legendary`|5|[전설 5종](jokers/legendary.md)|

## 공통 성질

|무엇|값|
|--|--|
|슬롯|기본 5. `Black Deck` · `Antimatter` · `Negative` 가 늘리고 `Painted Deck` 이 줄입니다|
|값|$1 ~ $10. 조커마다 정해져 있습니다|
|판매가|구입가의 절반을 내림. 최소 $1|
|에디션|`Foil` 칩 +50 · `Holographic` 배수 +10 · `Polychrome` 배수 ×1.5 · `Negative` 슬롯 +1|
|스티커|`Eternal` · `Perishable` · `Rental`. [스테이크](decks-and-stakes.md#스티커-3종)가 붙입니다|
|순서|**슬롯의 왼쪽에서 오른쪽으로 발동합니다.** 순서를 바꿀 수 있습니다|

## 효과 VM 변종의 분포

150종을 효과 VM의 어느 변종으로 적었는지의 합계입니다. `Custom`이 적을수록 데이터에 담긴
비율이 높고, **`Custom`의 개수가 이 샘플의 지표입니다.**

|변종|종수|
|--|--|
|`AddMult` · `AddChips`|집계 대기|
|`MulMult`|집계 대기|
|`AddMoney` · `SetMoney`|집계 대기|
|`Retrigger`|집계 대기|
|`GrowSelf`|집계 대기|
|`CreateCard`|집계 대기|
|`ModifyCard` · `DestroyCard`|집계 대기|
|`ChangeRule`|집계 대기|
|`CopyJoker`|집계 대기|
|`Custom`|집계 대기|

집계는 [데이터 저작](../../doc/data-design.md)이 끝나면 `verify.py`가 냅니다 — 손으로 세면
데이터와 어긋나므로 세지 않습니다.

## 이름을 지은 방식

원작의 이름은 음식·물건·직업·과학·사람 이름이 섞여 있어서 한 계열이 아닙니다. 그 성질을
따라가되 우리 계열을 하나 두었습니다 — **온실과 정원, 그 안의 도구와 작은 것들**입니다.

|원작의 묶음|우리의 묶음|
|--|--|
|무늬 4종 (`Greedy` 계열)|꽃 4종 — `Quartz Bloom` · `Poppy Bloom` · `Nettle Bloom` · `Clover Bloom`|
|족보별 배수 5종 (`Jolly` 계열)|새 5종 — `Warbler` · `Magpie` · `Starling` · `Swallow` · `Finch`|
|족보별 칩 5종 (`Sly` 계열)|벌레 5종 — `Cricket` · `Beetle` · `Mantis` · `Centipede` · `Firefly`|
|보석 4종 (`Rough Gem` 계열)|돌 4종 — `Raw Gem` · `Heartstone` · `Arrow Flint` · `Onyx Leaf`|
|족보 배수 5종 (`The Duo` 계열)|무리 5종 — `The Bond` · `The Braid` · `The Brood` · `The March` · `The Flock`|
|전설 5종 (실존·전설의 광대)|온실의 정령 5종 — `Verdigris` · `Coronet` · `Gravebloom` · `Hushbell` · `Echoseed`|

**한 묶음이 한 묶음으로 대응하는 것이 요점입니다.** 원작에서 다섯이 한 계열이면 우리도 다섯이
한 계열이어야, 플레이어가 상점에서 같은 판단을 합니다.

---

EOD
