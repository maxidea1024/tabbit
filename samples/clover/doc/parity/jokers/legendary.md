# 전설 5종

> [조커 목록으로](../jokers.md)

---

**상점에 나오지 않습니다.** `The Soul` 한 장으로만 나오고, 그 카드는 아르카나 팩과 유령 팩에서
0.3% 확률로 나옵니다. 값이 없으므로 판매가는 별도 규칙입니다.

원작의 다섯은 실존하거나 전설에 남은 광대의 이름입니다. 우리 다섯은 **온실의 정령**입니다.

|원작|우리|`id`|효과|VM 변종|
|--|--|--|--|--|
|Canio|녹빛|`verdigris`|그림 카드가 파괴될 때마다 배수 ×1 누적|`GrowSelf(MulMult)`|
|Triboulet|작은 관|`coronet`|K와 Q 득점마다 배수 ×2|`MulMult` + `CardRankSet`|
|Yorick|무덤꽃|`gravebloom`|카드를 23장 버릴 때마다 배수 ×1 누적|`GrowSelf(MulMult)` + `CounterAtLeast(23)`|
|Chicot|잠잠종|`hushbell`|**모든 보스 블라인드의 효과를 끕니다**|`ChangeRule`|
|Perkeo|메아리씨|`echoseed`|상점을 나갈 때 보유 소모품 하나의 `Negative` 사본을 만듭니다|`CreateCard` + `OnShopExit`|

## 눈에 걸리는 것

|무엇|왜 걸리는가|
|--|--|
|`hushbell`|**보스 효과를 전부 끕니다.** 보스 효과가 조커와 같은 계열이므로, 계열 하나를 통째로 끄는 연산이 필요합니다|
|`gravebloom`|**23장이라는 누적 문턱**입니다. 문턱을 넘으면 초기화하고 다시 셉니다|
|판매가|값이 없으므로 「구입가의 절반」이 적용되지 않습니다. 원작은 $10을 기준으로 잡습니다 — **`Const_Economy` 에 상수로 둡니다**|

---

EOD
