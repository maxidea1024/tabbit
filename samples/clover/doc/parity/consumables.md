# 소모품

> [대조표로](../parity.md)

---

소모품은 슬롯 2개를 쓰고, 쓰면 사라집니다. 세 갈래이고 이름은 타로와 천체의 이름이므로 그대로
씁니다.

## 타로 22종

대아르카나 그대로입니다. 상점 값은 $3입니다.

|카드|효과|
|--|--|
|`The Fool`|이번 런에서 마지막으로 쓴 타로 또는 행성 카드의 사본을 만듭니다. 자기 자신은 제외입니다|
|`The Magician`|고른 카드 2장을 `Lucky Card`로|
|`The High Priestess`|무작위 행성 카드를 최대 2장 만듭니다|
|`The Empress`|고른 카드 2장을 `Mult Card`로|
|`The Emperor`|무작위 타로 카드를 최대 2장 만듭니다|
|`The Hierophant`|고른 카드 2장을 `Bonus Card`로|
|`The Lovers`|고른 카드 1장을 `Wild Card`로|
|`The Chariot`|고른 카드 1장을 `Steel Card`로|
|`Justice`|고른 카드 1장을 `Glass Card`로|
|`The Hermit`|보유 금액을 2배로. **상한 $20**|
|`The Wheel of Fortune`|1/4 확률로 무작위 조커에 무작위 에디션을 붙입니다|
|`Strength`|고른 카드 최대 2장의 랭크를 1 올립니다|
|`The Hanged Man`|고른 카드 최대 2장을 파괴합니다|
|`Death`|카드 2장을 고르면 왼쪽이 오른쪽과 같아집니다|
|`Temperance`|보유한 조커 전부의 판매가 합계를 받습니다. **상한 $50**|
|`The Devil`|고른 카드 1장을 `Gold Card`로|
|`The Tower`|고른 카드 1장을 `Stone Card`로|
|`The Star`|고른 카드 최대 3장을 다이아로|
|`The Moon`|고른 카드 최대 3장을 클럽으로|
|`The Sun`|고른 카드 최대 3장을 하트로|
|`Judgement`|무작위 조커를 만듭니다|
|`The World`|고른 카드 최대 3장을 스페이드로|

## 행성 12종

행성 카드 하나가 족보 하나를 1레벨 올립니다. **증분은 여기 없습니다** —
[족보 표](hands-and-cards.md#족보-15종)에 있고, 행성 테이블은 어느 족보인지만 가리킵니다.

|카드|족보|
|--|--|
|`Pluto`|High Card|
|`Mercury`|Pair|
|`Uranus`|Two Pair|
|`Venus`|Three of a Kind|
|`Saturn`|Straight|
|`Jupiter`|Flush|
|`Earth`|Full House|
|`Mars`|Four of a Kind|
|`Neptune`|Straight Flush|
|`Planet X`|Five of a Kind|
|`Ceres`|Flush House|
|`Eris`|Flush Five|

히든 족보 3종에 해당하는 `Planet X` · `Ceres` · `Eris`는 그 족보를 한 번 낸 뒤에야 나옵니다.

`Observatory` 바우처가 있으면 행성 카드가 해당 족보에 배수 ×1.5를 더합니다 — 소모품이 아니라
**보유한 행성 카드가 상시 효과를 가지는 유일한 경우**입니다.

## 유령 18종

상점에는 기본적으로 나오지 않습니다. 유령 팩과 유령 덱으로만 옵니다. 값은 $4입니다.

|카드|효과|
|--|--|
|`Familiar`|패의 무작위 카드 1장을 파괴하고, 강화된 그림 카드 3장을 더합니다|
|`Grim`|패의 무작위 카드 1장을 파괴하고, 강화된 A 2장을 더합니다|
|`Incantation`|패의 무작위 카드 1장을 파괴하고, 강화된 숫자 카드 4장을 더합니다|
|`Talisman`|고른 카드 1장에 `Gold Seal`|
|`Aura`|고른 카드 1장에 포일·홀로·폴리크롬 중 하나|
|`Wraith`|무작위 레어 조커를 만들고 **금액을 $0으로**|
|`Sigil`|패의 모든 카드를 무작위 한 무늬로|
|`Ouija`|패의 모든 카드를 무작위 한 랭크로. **패 크기 -1**|
|`Ectoplasm`|무작위 조커에 `Negative`. **패 크기 -1**|
|`Immolate`|패의 무작위 카드 5장을 파괴하고 $20|
|`Ankh`|무작위 조커의 사본을 만들고 **나머지 조커를 전부 파괴합니다**|
|`Deja Vu`|고른 카드 1장에 `Red Seal`|
|`Hex`|무작위 조커에 `Polychrome`을 붙이고 **나머지 조커를 전부 파괴합니다**|
|`Trance`|고른 카드 1장에 `Blue Seal`|
|`Medium`|고른 카드 1장에 `Purple Seal`|
|`Cryptid`|고른 카드 1장의 사본을 2장 만듭니다|
|`The Soul`|**전설 조커 1장.** 아르카나 팩과 유령 팩에서 0.3% 확률로 나옵니다|
|`Black Hole`|모든 족보를 1레벨 올립니다|

`Ectoplasm`의 패 크기 -1은 쓸 때마다 누적됩니다.

## 효과 VM에 요구되는 것

소모품 52종이 조커와 **같은 효과 계열을 공유**합니다. 조커에 없고 소모품에만 있는 연산이
셋입니다.

|연산|누가 쓰는가|
|--|--|
|`ModifyCard`|강화·인장·에디션·무늬·랭크의 변경. 타로 대부분과 유령 절반|
|`AddCard` · `DestroyCard`|`Familiar` 계열 · `The Hanged Man` · `Cryptid` · `Immolate`|
|`SetMoney`|`Wraith` 가 금액을 $0으로. 가산이 아니라 대입입니다|

**대상 선택이 효과의 일부입니다.** 「고른 카드 2장」과 「패의 무작위 1장」과 「모든 카드」가
다른 값이므로, `Scope`에 `Selected(n)` · `RandomInHand(n)` · `AllInHand`가 있어야 합니다.

## 데이터의 자리

|무엇|테이블|
|--|--|
|타로 22종|`Tarot`|
|행성 12종|`Planet` — 족보를 `PokerHandKind` 로 들고 있습니다|
|유령 18종|`Spectral`|
|타로와 유령의 효과|`TarotEffect` · `SpectralEffect` — 조커와 같은 계열|

---

EOD
