# 명령 목록

> [효과 VM으로](../effect-vm.md)

---

축이 4개입니다 — `trigger` · `condition` · `operation` · `scope`. 축마다 값이 정해져 있고,
**코어가 모르는 자유 문자열은 없습니다.**

## `trigger` — 발동 시점

|값|시점|
|--|--|
|`Passive`|상시. 값을 더하지 않고 규칙을 바꾸는 것들|
|`OnCardScored`|득점 카드 한 장마다|
|`OnCardHeld`|패에 든 카드 한 장마다|
|`OnHandPlayed`|핸드 하나가 판정된 뒤, 곱하기 전|
|`OnHandDiscarded`|버릴 때|
|`OnCardDiscarded`|버린 카드 한 장마다|
|`OnRoundStart`|라운드가 시작할 때|
|`OnRoundEnd`|라운드가 끝날 때|
|`OnBlindSelect`|블라인드를 고를 때|
|`OnBossDefeated`|보스를 격파할 때|
|`OnShopEnter` · `OnShopExit`|상점에 들어갈 때 · 나갈 때|
|`OnReroll`|상점에서 리롤할 때|
|`OnPackSkipped` · `OnPackOpened`|팩을 넘길 때 · 열 때|
|`OnCardAdded` · `OnCardDestroyed`|덱의 카드가 늘 때 · 줄 때|
|`OnJokerSold` · `OnConsumableUsed`|조커를 팔 때 · 소모품을 쓸 때|
|`OnSell`|**이것 자신**을 팔 때|
|`OnUse`|**이것 자신**을 쓸 때. 소모품이 전부 이것입니다|
|`OnScoreResolved`|점수가 확정된 뒤. 패배 판정 전 — `old_bones` 가 여기 있습니다|

## `condition` — 성립 조건

|변종|필드|뜻|
|--|--|--|
|`Always`|—|항상|
|`HandContains`|`hand`|낸 족보가 그것을 포함하는가|
|`HandIs`|`hand`|낸 족보가 정확히 그것인가|
|`CardSuit`|`suit`|이 카드의 무늬|
|`CardRankSet`|`ranks[]`|이 카드의 랭크가 집합에 있는가|
|`CardIsFace`|—|그림 카드인가. `face_pattern` 이 이 판정을 바꿉니다|
|`CardEnhancement` · `CardSeal` · `CardEdition`|각 종류|이 카드가 그것을 가졌는가|
|`CardCountAtMost` · `CardCountIs`|`n`|낸 카드의 장수|
|`AllSuitsPresent`|—|득점 카드에 네 무늬가 다 있는가|
|`SuitPair`|`suit`|그 무늬 하나와 다른 무늬 하나가 있는가|
|`AllHeldSuit`|`suits[]`|패의 모든 카드가 그 무늬들에 드는가|
|`Probability`|`num` · `den`|`rng.below(den) < num × 확률배율`|
|`MoneyAtMost` · `MoneyAtLeast`|`amount`|보유 금액|
|`DiscardsLeft` · `HandsLeft`|`n` · `compare`|남은 버리기·핸드|
|`DiscardsUnused`|—|이번 라운드에 버리기를 쓰지 않았는가|
|`HandRepeated`|—|이번 라운드에 이미 낸 족보인가|
|`FirstHand` · `LastHand`|—|라운드의 첫 핸드 · 마지막 핸드|
|`FirstMatch`|—|이 트리거에서 조건을 만족한 첫 대상인가|
|`FirstDiscard`|—|라운드의 첫 버리기인가|
|`EveryNHands`|`n`|`n`핸드마다|
|`CounterAtLeast`|`counter` · `n`|상태 카운터가 문턱을 넘었는가. 넘으면 뺍니다|
|`ChargeLeft`|—|남은 횟수가 있는가|
|`TargetHand` · `TargetRank` · `TargetSuit` · `TargetCard`|—|라운드마다 바뀌는 지정 대상과 맞는가|
|`DeckEnhancedAtLeast`|`n`|덱의 강화 카드 수|
|`BossTriggered`|—|이번 핸드가 보스 능력을 발동시켰는가|
|`ScoreRatioAtLeast`|`num` · `den`|점수가 요구의 비율을 넘었는가|

조건은 **하나만** 적습니다. 둘이 필요한 효과는 효과 행을 둘로 나누지 않고, 그 조합을 변종
하나로 만듭니다 — `SuitPair`가 그렇게 생긴 것입니다. **조건의 논리곱을 시트에서 조립하게 하면
시트가 프로그램이 됩니다.**

## `operation` — 연산

|변종|필드|뜻|
|--|--|--|
|`AddChips`|`chips`|칩 가산|
|`AddMult`|`mult`|배수 가산. 만분율|
|`MulMult`|`mult`|배수 곱. 만분율|
|`AddMoney`|`amount` · `cap?`|금액 가산. 상한이 있으면 `cap`|
|`SetMoney`|`amount`|금액 대입. `Wraith` 가 유일합니다|
|`PerUnit`|`unit` · `chips?` · `mult?` · `money?`|**단위마다 반복합니다.** `unit` 이 `JokerCount` · `DeckRemaining` · `Money` · `MoneyPer5` · `DiscardsLeft` · `BlindsSkipped` · `TarotUsed` · `UniquePlanetUsed` · `DeckDeficit` · `EmptySlots` · `DeckRankCount` · `DeckEnhancementCount` · `JokerRarityCount` · `HandPlayCount` · `OtherJokerSellValue` · `LowestHeldRank`|
|`RandomRange`|`min` · `max` · `field`|난수를 그 칸에 더합니다. `smudge` 가 유일합니다|
|`Retrigger`|`times`|대상을 그 자리에서 다시 발동합니다|
|`GrowSelf`|`field` · `step` · `cap?` · `reset?`|자기 상태를 늘립니다. `field` 는 `chips` · `mult_add` · `mult_mul` · `money` · `sell_value`. `step` 은 음수 가능. `reset` 은 `Round` · `Boss` · `Never`|
|`GrowOthers`|`field` · `step` · `target`|다른 것의 값을 늘립니다. `gift_tag` 하나입니다|
|`LevelUpHand`|`which` · `levels`|족보 레벨. `which` 는 `Played` · `FirstDiscarded` · `All` · `Random` · `MostPlayed`|
|`CreateCard`|`kind` · `count` · `edition?` · `pool?`|소모품·조커·플레잉 카드 생성|
|`AddCard`|`spec`|덱 또는 패에 카드를 더합니다|
|`DestroyCard`|`count`|카드를 파괴합니다|
|`ModifyCard`|`what` · `value`|강화·인장·에디션·무늬·랭크·영구 칩을 바꿉니다|
|`DestroyJoker`|`which`|조커를 파괴합니다. `Right` · `Random` · `AllOther` · `Self`|
|`CopyJoker`|`which`|조커의 능력을 복사합니다. `Right` · `Leftmost`|
|`Debuff`|`what`|무력화합니다. 보스가 씁니다|
|`DisableBoss`|—|보스 효과를 끕니다|
|`PreventLoss`|—|패배를 막습니다|
|`ChangeRule`|`rule` · `value`|규칙 하나를 바꿉니다. 목록이 아래에 있습니다|
|`Nothing`|—|아무것도 하지 않습니다. `Blank` 바우처가 씁니다|
|`Custom`|`handler_id`|선언으로 적히지 않는 것. **개수가 지표입니다**|

### `ChangeRule`의 `rule` 목록

|`rule`|뜻|
|--|--|
|`HandSize` · `HandsPerRound` · `DiscardsPerRound` · `JokerSlots` · `ConsumableSlots`|자원의 증감|
|`DebtLimit`|빚 한도|
|`FreeRerolls` · `RerollCostDelta` · `RerollStartsFree`|리롤|
|`InterestPer5` · `InterestCap`|이자|
|`ShopCardSlots` · `ShopWeight` · `ShopDiscount` · `ShopAllowsPlayingCards` · `ShopAllowsSpectral` · `FreePlanets`|상점|
|`AllCardsScore` · `AllCardsAreFace` · `FlushStraightCards` · `StraightGap` · `SuitsMerged`|족보 판정|
|`ProbabilityScale`|확률 전역 배율. `loaded_dice`|
|`AllowDuplicates`|중복 등장|
|`BalanceChipsAndMult`|`Plasma Deck` 의 평준화|
|`BossRerollsPerAnte`|보스 리롤 횟수|
|`AnteDelta`|안테 증감. `Hieroglyph` 계열|
|`EditionWeightScale`|에디션 등장 배율. `Hone` 계열|
|`PlanetGivesMult`|`Observatory`|

## `scope` — 누구에게

|값|뜻|
|--|--|
|`Run`|런 전체. 자원과 규칙|
|`Self`|이 효과의 소유자|
|`ScoredCard`|지금 처리 중인 득점 카드|
|`HeldCard`|지금 처리 중인 패의 카드|
|`Selected`|플레이어가 고른 카드. `count` 를 함께 적습니다|
|`RandomInHand`|패의 무작위 카드. `count`|
|`AllInHand`|패의 모든 카드|
|`RandomInDeck` · `AllInDeck`|덱 기준|
|`RandomJoker` · `JokerRight` · `JokerLeftmost` · `AllOtherJokers`|조커 기준|

## 판정 하나

**시트에 적을 수 없는 것이 나오면 변종을 하나 더합니다. 표현식을 넣지 않습니다.**

변종이 하나 늘면 두 구현에 각각 한 곳이 늘고 그것으로 끝입니다. 표현식을 넣으면 시트가 타입
검사를 잃고, 이 도구가 증명하려는 것과 어긋납니다.

---

EOD
