# 레어 20종

> [조커 목록으로](../jokers.md)

---

상점 조커의 5%가 여기서 나옵니다. 값은 $7 ~ $10입니다.

|원작|우리|`id`|가격|효과|VM 변종|
|--|--|--|--|--|--|
|DNA|접붙임|`graft`|$8|라운드 첫 핸드가 1장이면 그 카드의 영구 사본을 덱에 더하고 패로 뽑습니다|`AddCard` + `FirstHand`|
|Vagabond|떠돌이|`drifter`|$8|보유 $4 이하로 핸드를 내면 타로 카드 생성|`CreateCard` + `MoneyAtMost`|
|Baron|집사|`steward`|$8|패의 K마다 배수 ×1.5|`MulMult` + `Held` + `CardRankSet`|
|Obelisk|선돌|`standing_stone`|$8|최다 사용 족보를 피한 연속 핸드마다 배수 ×0.2 누적|`GrowSelf(MulMult)`|
|Baseball Card|신인 카드|`rookie_card`|$8|언커먼 조커 하나마다 배수 ×1.5|`MulMult` × `JokerRarityCount`|
|Ancient Joker|옛 인장|`old_sigil`|$8|지정된 무늬 카드 득점마다 배수 ×1.5. 무늬는 라운드마다 바뀝니다|`MulMult` + `TargetSuit`|
|Campfire|모닥불|`bonfire`|$9|카드를 팔 때마다 배수 ×0.25 누적. 보스 격파 시 초기화|`GrowSelf(MulMult)` + `ResetOn`|
|Blueprint|덧그림|`tracing`|$10|오른쪽 조커의 능력을 복사합니다|`CopyJoker(Right)`|
|Wee Joker|꼬맹이|`tiny_tot`|$8|2 득점마다 칩 +8 누적|`GrowSelf(AddChips)`|
|Hit the Road|열린 길|`open_road`|$8|이번 라운드에 버린 J마다 배수 ×0.5 누적|`GrowSelf(MulMult)` + 라운드 초기화|
|The Duo|맺음|`the_bond`|$8|페어 포함 시 배수 ×2|`MulMult` + `HandContains`|
|The Trio|세 갈래|`the_braid`|$8|트리플 포함 시 배수 ×3|`MulMult` + `HandContains`|
|The Family|한 배|`the_brood`|$8|포카드 포함 시 배수 ×4|`MulMult` + `HandContains`|
|The Order|행렬|`the_march`|$8|스트레이트 포함 시 배수 ×3|`MulMult` + `HandContains`|
|The Tribe|무리|`the_flock`|$8|플러시 포함 시 배수 ×2|`MulMult` + `HandContains`|
|Stuntman|짐덩이|`deadweight`|$7|칩 +250. 패 크기 -2|`AddChips` + `ChangeRule`|
|Invisible Joker|희미한 윤곽|`faint_outline`|$8|2라운드 뒤에 팔면 무작위 조커를 복제합니다. 사본의 `Negative`는 사라집니다|`OnSell` + `CopyJoker` + `ChargeCounter(2)`|
|Brainstorm|거울 쪽지|`mirror_note`|$10|맨 왼쪽 조커의 능력을 복사합니다|`CopyJoker(Leftmost)`|
|Driver's License|통행 허가|`road_permit`|$7|덱에 강화 카드가 16장 이상이면 배수 ×3|`MulMult` + `DeckEnhancedAtLeast`|
|Burnt Joker|그을음|`scorch_mark`|$8|라운드에서 처음 버린 족보의 레벨을 올립니다|`LevelUpHand` + `FirstDiscard`|

## 눈에 걸리는 것

|무엇|왜 걸리는가|
|--|--|
|`tracing` · `mirror_note`|**다른 조커의 효과를 그 자리에서 실행합니다.** 효과 VM에 간접 참조가 있어야 하고, 두 개가 서로를 가리키면 순환입니다 — 원작은 사슬을 한 단계로 끊습니다|
|`the_bond` 계열 5종|커먼의 `warbler` 계열과 **조건이 같고 연산만 다릅니다.** 데이터가 조건을 공유할 수 있는지의 시험대입니다|
|`bonfire` · `open_road`|**초기화 시점이 다릅니다** — 보스 격파와 라운드 종료. `ResetOn` 이 필요합니다|
|`faint_outline`|**팔았을 때 발동하고 그전에 2라운드를 셉니다.** 조건이 시간입니다|

---

EOD
