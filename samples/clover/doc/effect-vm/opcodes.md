# 명령 목록

> [효과 VM으로](../effect-vm.md)

---

**선언은 [`effect.tbs`](../../design-data/schemas/effect.tbs) 이고 여기는 그것을 읽는
법입니다.** 이 목록과 그 파일이 어긋나면 `verify.py` 가 검출합니다 — 문서가 데이터를 따라오지
못하는 것이 이 종류 문서의 흔한 결말이므로, 게이트를 하나 두었습니다.

|  |수|
|--|--|
|조건|**41**|
|연산|**36**|

## 효과 행의 칸

조건·연산과 **직교하는 것**은 조건 안이 아니라 행에 있습니다.

|칸|무엇|
|--|--|
|`chance_num` · `chance_den`|확률. 「8 을 낼 때마다 1/4 확률로」는 조건이 랭크이고 확률이 그 위에 얹힌 것입니다|
|`first_only`|조건을 만족한 **첫 대상만**. `tintype` 과 `torn_tab` 이 씁니다|
|`ranks` · `suits`|랭크 집합과 무늬 집합. 조건이든 연산이든 한 행에 하나뿐이므로 한 칸을 나눠 씁니다|
|`scope` · `scope_count`|누구에게, 몇 개|

`ranks` 와 `suits` 가 변종의 멤버가 아닌 것은
[도구 보고 §1](../tool-findings.md#1-배열인-변종-멤버에서의-c-생성-예외) 때문
입니다.

## `trigger` — 발동 시점

24종입니다. 득점 뒤의 누적이 `OnScoreResolved` 인 것이 요점입니다 — 득점 중에 늘리면 그
핸드의 점수가 달라집니다.

|묶음|값|
|--|--|
|상시|`Passive`|
|득점|`OnCardScored` · `OnCardHeld` · `OnHandPlayed` · `OnScoreResolved`|
|버리기|`OnHandDiscarded` · `OnCardDiscarded`|
|라운드|`OnRoundStart` · `OnRoundEnd` · `OnBlindSelect` · `OnBossDefeated` · `OnRunStart`|
|상점|`OnShopEnter` · `OnShopExit` · `OnReroll` · `OnPackSkipped` · `OnPackOpened`|
|덱|`OnCardAdded` · `OnCardDestroyed` · `OnLuckyTriggered`|
|자기 자신|`OnSell` · `OnUse`|
|기타|`OnJokerSold` · `OnConsumableUsed`|

## `condition` — 성립 조건 41종

|묶음|변종|
|--|--|
|없음|`Always`|
|족보|`HandContains` · `HandIs` · `HandRepeated` · `IsMostPlayedHand` · `NotMostPlayedHand` · `HandContainsRankAndHand`|
|카드|`CardSuit` · `CardRankSet` · `CardIsFace` · `CardEnhancement` · `CardEnhanced` · `CardSeal` · `CardEdition`|
|낸 카드|`CardCount` · `AllSuitsPresent` · `SuitPair` · `AllHeldSuit` · `NoFaceScored` · `FaceScored`|
|자원|`Money` · `DiscardsLeft` · `HandsLeft` · `DiscardsUnused`|
|시점|`FirstHand` · `LastHand` · `FirstDiscard` · `EveryNHands`|
|한 장|`FirstHandSingleCard` · `FirstHandSingleRank` · `FirstDiscardSingleCard`|
|누적|`CounterAtLeast` · `CounterAtMost` · `ChargeLeft`|
|기타|`BlindKind` · `TargetMatch` · `DeckEnhancedAtLeast` · `DiscardedFaceAtLeast` · `BossTriggered` · `ScoreRatioAtLeast` · `ConsumableKind`|

**조건은 하나만 적습니다.** 둘이 필요하면 그 조합에 이름을 주어 변종 하나로 만듭니다 —
`SuitPair` 와 `HandContainsRankAndHand` 와 `FirstHandSingleRank` 가 그렇게 생긴 것입니다.
논리곱을 시트에서 조립하게 하면 시트가 프로그램이 됩니다.

## `operation` — 연산 36종

|묶음|변종|
|--|--|
|득점|`AddChips` · `AddMult` · `MulMult` · `PerUnit` · `RandomRange` · `Retrigger`|
|금액|`AddMoney` · `SetMoney` · `MulMoney`|
|누적|`GrowSelf` · `ResetSelf` · `GrowOthers`|
|족보|`LevelUpHand`|
|카드|`CreateCard` · `AddCard` · `DestroyCard` · `ModifyCard` · `CardTrait`|
|조커|`ModifyJoker` · `DestroyJoker` · `CopyJoker` · `FlipJokers` · `DisableRandomJoker`|
|규칙|`ChangeRule` · `ChangeRuleByCounter` · `Debuff` · `DisableBoss` · `PreventLoss`|
|상점·태그|`ShopGift` · `DuplicateNextTag` · `RerollBoss` · `Grant`|
|보스|`ForceDiscard` · `DrawFaceDown`|
|나머지|`Nothing` · **`Custom`**|

### 셋의 셈법

|변종|계산|
|--|--|
|`PerUnit` 의 `MulMult`|`base_value + value × 개수`. `base_value` 를 비우면 0 입니다 — 빈 슬롯마다 ×1 은 0 이고 강철 카드마다 ×0.2 는 10000 입니다|
|`PerUnit` 의 `MulEach`|개수만큼 **되풀이해서 곱합니다.** `rookie_card` 하나입니다|
|`GrowSelf` 의 `MultMul`|시작값이 10000(×1)입니다. 나머지 카운터는 0 에서 시작합니다|

`Chips` · `MultAdd` · `MultMul` 카운터는 **득점할 때 자동으로 적용됩니다.** 따로 더하는
효과 행을 두지 않습니다.

## `scope` — 대상 16종

|묶음|값|
|--|--|
|런|`Run` · `SelfTarget`|
|카드|`ScoredCard` · `HeldCard` · `Selected` · `RandomInHand` · `AllInHand` · `RandomInDeck` · `AllInDeck`|
|조커|`RandomJoker` · `JokerRight` · `JokerLeftmost` · `AllOtherJokers` · `AllJokers`|
|소모품|`RandomConsumable` · `AllConsumables`|

## `Custom` 의 지금 개수

**1건**입니다 — `pruning_shears` 하나이고, 오른쪽 조커를 파괴하면서 **그 조커의 판매가 2배**를
자기 배수로 가져오는 것입니다. 값이 다른 개체의 상태에서 오므로 선언으로 적히지 않습니다.

효과 행 400개 남짓 중 하나이므로, **효과의 99% 이상이 데이터에 있습니다.**

## 판정 하나

**시트에 적을 수 없는 것이 나오면 변종을 하나 더합니다. 표현식을 넣지 않습니다.**

변종이 하나 늘면 두 구현에 각각 한 곳이 늘고 그것으로 끝입니다. 표현식을 넣으면 시트가 타입
검사를 잃고, 이 도구가 증명하려는 것과 어긋납니다.

---

EOD
