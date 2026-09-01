# 데이터 설계

> [clover 문서 목록으로](readme.md)

---

원작의 규칙을 [대조표](parity.md)가 적었고, 그것을 시트로 옮기는 방법이 여기 있습니다.

## 워크북 9개 · 테이블 40개

|워크북|테이블|행 수 대략|
|--|--|--|
|`Cards.xlsx`|`Rank` · `Suit` · `BaseDeckCard` · `PokerHand` · `Enhancement` · `EnhancementEffect` · `Seal` · `SealEffect` · `Edition`|13 · 4 · 52 · 12 · 9 · 12 · 5 · 4 · 5|
|`Jokers.xlsx`|`Joker` · `JokerRarityWeight` · `JokerEffect`|**500** · 4 · **588**|
|`Consumables.xlsx`|`Tarot` · `TarotEffect` · `Planet` · `Spectral` · `SpectralEffect`|22 · 22 · 12 · 18 · 27|
|`Progression.xlsx`|`Ante` · `Blind` · `BossBlind` · `BossEffect`|9 · 3 · 28 · 26|
|`Shop.xlsx`|`ShopSlotWeight` · `BoosterPack` · `Voucher` · `VoucherEffect` · `RerollCost`|5 · 15 · 32 · 34 · 10|
|`Setup.xlsx`|`Deck` · `DeckEffect` · `Stake` · `Tag` · `TagEffect`|15 · 25 · 8 · 24 · 24|
|`Const.xlsx`|`Const_Run` · `Const_Score` · `Const_Economy` · `RngStream`|상수셋 3개 · 10|
|`Feel.xlsx`|`Const_Feel` · `EditionVisual` · `SoundCue`|상수셋 1개 · 5 · 20|
|`Text.xlsx`|`StringTable` · `Achievement`|351 · 20|

워크북을 갈래로 나눈 이유는 **한 사람이 한 갈래를 고치는 동안 다른 갈래가 잠기지 않게** 하는
것입니다. 조커 밸런스를 만지는 사람과 상점 확률을 만지는 사람이 같은 파일을 열지 않습니다.

## 효과 테이블 9개의 공통 계열

`JokerEffect` · `TarotEffect` · `SpectralEffect` · `BossEffect` · `VoucherEffect` ·
`TagEffect` · `DeckEffect` · `EnhancementEffect` · `SealEffect` 가 **같은 컬럼 구성**입니다.
다른 것은 `owner` 가 무엇을 가리키는가뿐입니다.

```
:field  owner  order  trigger  chance_num  chance_den  first_only  ranks  suits
        scope  scope_count  condition.$type  condition.…  operation.$type  operation.…
:type   foreign Joker  int  Trigger  int?  int?  bool?  RankKind[]?  SuitKind[]?
        Scope  int?  Condition  …  Operation  …
```

컬럼이 60개 남짓입니다. 변종의 멤버가 전부 나란히 놓이고 한 행이 그중 서넛만 쓰기 때문이며,
**그것이 다형 컬럼의 모양입니다.**

이것이 [선언된 struct의 신원](../../../spec/types/declared-struct-identity.md)과 닿습니다 —
`Condition`이 테이블마다 따로 선언되던 것이 하나가 되었습니다. 형식은 무변경이므로 데이터는
그대로입니다.

## `.tbs` 3개

|파일|무엇|
|--|--|
|`card.tbs`|`SuitKind` · `RankKind` · `PokerHandKind` · `EnhancementKind` · `SealKind` · `EditionKind` enum|
|`effect.tbs`|`Trigger` · `Scope` · `RuleKind` · `UnitKind` 등 enum 15개, **조건 41종 · 연산 36종**|
|`run.tbs`|`Rarity` · `StickerKind` · `BlindKind` · `PackKind` · `PackSize` · `ConsumableKind` · `StakeKind` · `ShopItemKind` · `RngStreamKind` enum|

**enum 이 `.tbs` 에 있는 이유**는 규격입니다 — 선언된 struct 의 멤버 타입은 `.tbs` 안에서
찾을 수 있어야 하고, `Condition` 이 `SuitKind` 를 멤버로 가지므로 그것이 시트의 enum이면
안 됩니다.

그래서 무늬는 두 자리에 있습니다.

|어디|무엇|
|--|--|
|`card.tbs` 의 `enum SuitKind`|**값의 목록.** 효과가 이것을 가리킵니다|
|`Cards.xlsx` 의 `Suit` 테이블|무늬마다의 **데이터** — 표시 이름 · 색 · 정렬 순서 · 글자|

이름이 갈린 것은 그래야 하기 때문입니다 — 같은 이름이면 「타입 셀이 어느 쪽을 가리키는가」에
답이 없어서 변환이 거부합니다. **목록은 `.tbs` 에 있고 데이터는 시트에 있습니다.**

테이블의 키가 enum 이면 그 테이블을 `foreign` 으로 가리킬 수 없습니다 —
[도구 보고 §2](tool-findings.md#2-enum-이-키인-테이블에-대한-foreign-의-제약) 입니다.

## 조커 테이블

```
:field  joker_id  rarity  cost  name  description  art  blueprint_ok  eternal_ok  perishable_ok  unlock
:type   string    Rarity  int   string (text=Joker)  string (text=Joker)  string (asset=joker)  bool  bool  bool  string?
```

|컬럼|왜 있는가|
|--|--|
|`blueprint_ok`|`tracing` · `mirror_note` 가 복사할 수 있는가. 원작이 조커마다 정해 둡니다|
|`eternal_ok` · `perishable_ok`|그 스티커가 붙을 수 있는가. `old_bones` 는 `Eternal` 이 붙으면 성립하지 않습니다|
|`unlock`|해금 조건. 105종이 처음부터이고 45종이 조건입니다|
|`art`|`asset=joker` 검사. **자리표가 아니라 게임이 띄우는 파일**을 봅니다|

`name`과 `description`이 `text=Joker` 인 것은 지역화입니다 — 값이 `StringTable`의 키가 되고,
한국어와 영어가 그 테이블에 있습니다.

## 값의 단위

|무엇|단위|
|--|--|
|배수|**만분율 `int`**. `×1.5` 는 `15000`|
|칩|정수 그대로|
|금액|정수 달러|
|확률|`Fraction` — 분자와 분모|
|시간|밀리초 `int`|

`float`을 쓰지 않습니다. [효과 VM의 결정론](effect-vm.md#결정론)이 그 이유입니다.

## 검증 규칙

시트의 타입으로 표현되지 않는 것을 `validation/`에 둡니다.

|규칙|무엇을 보는가|
|--|--|
|희귀도 분포|`JokerRarityWeight` 의 `count` 가 실제 종수와 같은가. **숫자가 어긋나면 조커를 빠뜨린 것입니다** — 지금 커먼 181 · 언커먼 214 · 레어 85 · 전설 20|
|효과 행의 존재|조커 전부가 효과 행을 하나 이상 가지는가|
|`Custom` 목록|`handler_id` 가 [문서](effect-vm.md#custom-탈출구)에 적힌 것과 일치하는가|
|`PerUnit` 의 단위|`UnitKind` 가 그 트리거에서 의미가 있는가 — `DiscardsLeft` 를 `OnShopEnter` 에서 보면 규격 위반입니다|
|행성과 족보|행성 12종이 족보 12종과 일대일인가|
|바우처 쌍|32종이 16쌍을 이루고, 상위가 자기 하위를 가리키는가|
|지역화|`StringTable` 에 한국어와 영어가 모두 있는가|
|해금 조건|해금 조건이 가리키는 대상이 존재하는가|

## 워크북을 만드는 방법

`wildling`과 같습니다. **정본은 `.tsv`이고 워크북은 생성물입니다.**

```
design-data/data/*.tsv          격자. 사람이 고치는 정본
        ↓  tools/Authoring      서식만 얹습니다
design-data/xlsx/*.xlsx         워크북 9개
        ↓  recipe.jsonc
web/src/generated · web/public/data          typescript · binary
unity/Assets/Clover/Generated · StreamingAssets   csharp · binary
design-data/out/html                          사람이 읽는 문서
```

---

EOD
