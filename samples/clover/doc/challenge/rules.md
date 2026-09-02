# 챌린지가 더하는 규칙 12종

> [챌린지로](../challenge.md)

---

`RuleKind` 는 지금 51종입니다. 챌린지 20종이 쓰는 규칙 중 **기존 51종으로 덮이지 않는 것이
12종**이고, 그 규격이 여기 있습니다.

12종 전부 `OpChangeRule` 로 걸립니다. **연산을 새로 만들지 않습니다** — `Rules` 에 칸이
늘고 `defaultRules()` 에 기본값이 붙는 것이 전부입니다.

## 먼저 고쳐야 하는 것 4건 (끝)

**챌린지와 무관하게 우리 데이터가 원작과 어긋나 있었습니다.** 챌린지가 바로 그 값을
건드리므로, 고치지 않으면 규칙을 걸어도 결과가 달라지지 않습니다.

**2026-09-02에 넷 다 고쳤습니다.**

|무엇|원작|우리|
|--|--|--|
|**남은 핸드당 수입**|라운드 종료 시 남은 핸드 하나마다 $1|`moneyPerHandLeft` 의 기본값이 **0**. 초록 덱만 2로 올립니다|
|**최종 보스 보상**|안테 8의 최종 보스는 $8|`Blind.tsv` 의 보스 보상 $5 하나뿐. `BossBlind` 에 보상 컬럼이 없습니다|
|**`Eternal` 이 붙지 않는 조커**|스스로 파괴되거나 팔아야 효과가 나는 조커에는 붙지 않습니다|`Joker` 표에 그 칸이 없었습니다|
|**`noodle_pot` 의 배수**|×1 에서 멈추고 파괴됩니다|**×0 까지 내려가고 파괴 행이 없었습니다** — 그 조커를 들고 있으면 점수가 0이 됩니다|

첫째는 `Const_Economy` 에 `MoneyPerHandLeft` 와 `MoneyPerDiscardLeft` 를 더했습니다.
**코드에 1을 적지 않습니다** — 이 표의 규율이 「기본값이 데이터에 있고 변경분도 데이터에
있다」이기 때문입니다. 초록 덱의 `OpChangeRule` 이 절대값 2이므로 덱 쪽은 그대로입니다.
**[대조표](../parity/economy-and-shop.md)의 「라운드 종료 시의 수입」에도 줄이 빠져
있었습니다** — 표에 없었으므로 데이터에도 없었습니다.

둘째는 `BossBlind` 에 `reward` 컬럼을 더하고 최종 보스 5종에 8을 적습니다.

셋째는 `Joker` 에 `eternal_ok` 컬럼을 더하고 **17종**에 `false` 를 적었습니다.
**`blueprint_ok` 가 이미 같은 모양이므로 새로운 갈래가 아닙니다** — 어느 조커에 무엇이
붙지 않는가를 표에 적는 자리입니다. 기본 150종의
[11종](../parity/challenges.md#조커에-붙지-않는-eternal-11종)이 원작 대조이고, 확장
350종에서 같은 성질인 6종(`crow_bait` · `hollow_seed` · `nightshade_cup` · `thorn_ring` ·
`estate_note` · `stagehand`)을 함께 적었습니다 — **150종만 고치면 나머지가 남습니다.**

넷째는 `noodle_pot` 의 `floor` 를 `0` 에서 `10000`(×1)으로 올리고 파괴 행을
`frost_pane` 과 같은 모양으로 더했습니다. **감소하는 `GrowSelf` 를 전수로 훑어 찾은
것이고, `MultMul` 이 0까지 내려가는 것은 이 하나뿐이었습니다** — 나머지는 `MultAdd` 나
`Chips` 라 0이 되어도 점수를 지우지 않습니다.

> **컬럼은 표의 끝에 붙입니다.** 가운데에 끼우면 뒤의 와이어 태그가 전부 밀리고, 그것을
> Tabbit 의 스키마 드리프트 게이트가 막습니다. 처음에 `blueprint_ok` 옆에 끼웠다가
> 거부당했습니다.

---

## 12종

|`RuleKind`|값|무엇|쓰는 챌린지|
|--|--|--|--|
|`NoSmallBlindReward`|플래그|스몰 블라인드 격파 보상이 0|`dry_season` · `barren_road`|
|`NoBigBlindReward`|플래그|빅 블라인드 격파 보상이 0|`dry_season` · `barren_road`|
|`NoBossBlindReward`|플래그|보스 블라인드 격파 보상이 0|`dry_season`|
|`NoMoneyPerHandLeft`|플래그|남은 핸드의 수입이 0|`dry_season` · `low_field`|
|`ChipsCappedByMoney`|플래그|칩이 지금 보유액을 넘지 못함|`coin_ceiling`|
|`FaceDownDrawRate`|값 `n`|뽑는 카드의 `1/n` 이 엎어짐. 0이면 없음|`blind_draw`|
|`HandSizePerMoney`|값 `n`|보유 $`n` 마다 패 크기 -1. 0이면 없음|`heavy_purse`|
|`AllJokersEternal`|플래그|모든 조커에 `Eternal`|`evergreen` · `sealed_fate`|
|`DebuffPlayedAfterScoring`|플래그|낸 카드가 득점 뒤에 무력화됨|`red_wax`|
|`PriceRisePerPurchase`|값 `$`|살 때마다 상점 가격이 영구히 `$n` 오름|`rising_price`|
|`NoJokersInShop`|플래그|상점 카드 칸에 조커가 나오지 않음|`vine_night` · `bare_field`|
|`DiscardCost`|값 `$`|버릴 때마다 `$n`|`single_thread`|

## 갈래별 규격

### 보상을 끄는 셋

`Blind.tsv` 의 `reward` 를 읽는 자리가 [run.ts](../../web/src/core/run.ts) 한 곳이므로
거기서 봅니다. **플래그 셋으로 나눈 것은 원작이 그렇게 갈라 쓰기 때문입니다** —
`dry_season` 은 셋 다이고 `barren_road` 는 스몰과 빅만입니다.

값 하나짜리 `BlindRewardScale` 로 두면 `barren_road` 를 적을 수 없습니다. `RuleDelta` 는
`(rule, value, absolute)` 이고 **어느 블라인드인가를 담는 칸이 없기** 때문입니다.

### `ChipsCappedByMoney`

득점이 끝난 칩에 상한을 겁니다. **배수가 아니라 칩입니다.** `scoring.ts` 가 칩과 배수를
곱하기 직전에 봅니다.

보유액이 음수이면 상한이 0입니다.

### `FaceDownDrawRate`

`CardInstance.faceDown` 이 이미 있습니다. 카드를 뽑는 자리에서 `Shuffle` 흐름이 아니라
**`CardProc` 흐름을 씁니다** — 뽑는 장수는 규칙이 바꾸므로, 같은 흐름을 쓰면 패 크기가
달라질 때 덱 섞기까지 갈라집니다.

원작은 「4장에 1장」이고 이것은 확률이 아니라 세는 것입니다. **`1/n` 을 뽑을 때마다 굴리는
것으로 적습니다** — 세는 것으로 적으면 뽑는 장수가 라운드마다 달라질 때 어디서부터 세는지가
정해지지 않습니다.

### `HandSizePerMoney`

패 크기는 규칙이고 보유액은 상태이므로, **`rebuildRules` 만으로는 따라오지 않습니다.**
돈이 바뀔 때마다 다시 세워야 합니다 — `MoneyChanged` 를 내는 자리에서 `rebuildRules` 를
부릅니다.

이것 하나 때문에 규칙을 다시 세우는 횟수가 늘어납니다. **그래도 상태를 규칙에 섞지
않습니다** — 규칙은 매번 다시 세우는 것이라는 성질이 유일한 방어선이기 때문입니다.

### `AllJokersEternal`

조커를 얻는 모든 자리에서 `sticker` 를 `Eternal` 로 정합니다. `JokerInstance.sticker` 가
이미 있으므로 새 칸이 없습니다.

**`eternal_ok` 가 `FALSE` 인 11종에는 붙지 않습니다.** `evergreen` 은 그 11종을 금지
목록으로 막고, `sealed_fate` 는 막지 않습니다 — 안테 4까지는 정상으로 동작해야 하기
때문입니다. **두 챌린지의 차이가 여기 하나이므로 규칙이 아니라 목록으로 갈립니다.**

`sealed_fate` 는 안테 4의 보스를 격파할 때 이 규칙과 `JokerSlots = 0` 이 함께 걸립니다 —
`Trigger.OnBossDefeated` 에 안테 조건입니다. **한 번 걸리고 남아야 하므로 `ruleDeltas`
쪽입니다.**

### `DebuffPlayedAfterScoring`

`ScoreResolved` 뒤에 `played` 의 카드에 `debuffed` 를 세웁니다. **덱의 카드에 남습니다** —
라운드가 끝나도 풀리지 않습니다.

### `PriceRisePerPurchase`

상점의 값에 얹는 누적값이 필요합니다. `Rules` 는 매번 다시 세우므로 여기 둘 수 없고,
**`RunState` 에 칸 하나가 늘어납니다** — `priceRise` 입니다. 규칙은 「한 번에 얼마씩
오르는가」이고 지금까지 오른 값은 상태입니다.

### `NoJokersInShop`

상점의 카드 칸 뽑기에서 조커를 뺍니다. **`jokerPool()` 을 비우는 것이 아닙니다** — 팩과
태그의 조커는 그대로 두는 챌린지가 있습니다(`vine_night`). 상점 칸의 가중치 쪽에서 뺍니다.

`bare_field` 는 이 규칙에 더해 팩과 태그와 소모품까지 금지 목록에 올립니다. **규칙과 금지
목록이 겹쳐 걸리는 것이 원작의 방식이고, 그것을 그대로 옮깁니다.**

### `DiscardCost`

버리기를 누를 때 돈이 나갑니다. **돈이 모자라면 버릴 수 없습니다** — 빚 한도가 있으면
그만큼까지 됩니다.

---

## 스티커 `Pinned`

`StickerKind` 에 넷째를 더합니다. **그 조커가 항상 맨 왼쪽이고 자리를 옮길 수 없습니다.**

`shear_edge` 하나만 씁니다. 조커의 발동이 왼쪽에서 오른쪽이므로 자리가 곧 순서이고,
원작이 이 챌린지에서 그것을 고정합니다.

스티커는 `JokerInstance.sticker` 하나뿐이므로 **`Eternal` 과 `Pinned` 가 함께 붙지
않습니다.** `shear_edge` 의 조커는 원작에서 둘 다입니다 — 스티커 칸을 배열로 바꾸거나,
`Pinned` 를 스티커가 아니라 규칙(`PinnedSlot`, 값이 조커의 자리)으로 두는 두 갈래입니다.
**후자를 권합니다** — 세이브의 모양이 바뀌지 않고, 챌린지 하나 때문에 모든 조커의 스티커
칸이 배열이 되지 않습니다.

---

EOD
