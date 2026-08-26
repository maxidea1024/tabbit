# 와일드링 — 유니티 게임 구현 스펙

이 문서는 `samples/wildling/unity/` 를 **플레이되는 게임**으로 만드는 작업의 규격입니다.
[기획서](game-design.md)가 「무엇을 만드는가」이고, 이 문서는 「어느 코드가 어느 표를 읽어
그것을 하는가」입니다.

## 목차

|절|내용|
|--|--|
|[1. 목적](#1-목적)|이 게임이 증명하는 것|
|[2. 범위](#2-범위)|넣는 것과 화면만 두는 것|
|[3. 판정](#3-판정)|무엇이 되면 완료인가|
|[4. 화면](#4-화면)|화면 8개와 전이|
|[5. 상태](#5-상태)|세이브에 남는 것|
|[6. 규칙](#6-규칙)|계산이 어느 표를 읽는가|
|[7. 그림](#7-그림)|아이콘 196개를 만드는 방법|
|[8. 파일 배치](#8-파일-배치)|어디에 무엇을 두는가|
|[9. 확인 절차](#9-확인-절차)|되돌려 보는 방법과 지금의 기준선|

---

## 1. 목적

[`WildlingDataCheck.cs`](../unity/Assets/Scripts/Editor/WildlingDataCheck.cs) 가 확인하는 것은
**생성 코드가 `.bytes` 를 읽어 값을 낸다**까지입니다. 그 다음 질문에는 답이 없었습니다 —
**생성된 조회 표면이 실제로 쓸 만한가.**

|확인되는 것|`WildlingDataCheck` 만 있던 때|지금|
|--|--|--|
|`.bytes` 를 읽는가|확인됨|동일|
|값이 시트대로인가|확인됨|동일|
|**조회 표면으로 게임 규칙을 쓸 수 있는가**|확인되지 않음|**핵심 루프가 돕니다**|
|**참조가 낸 행으로 화면을 그릴 수 있는가**|확인되지 않음|**화면 9개**|
|**`asset=` 컬럼이 실제 파일을 가리키는가**|이름만 대조|**로드되어 화면에 나옵니다**|

[도구 보고](tool-findings.md)의 11건 중 셋이 「참조가 내는 두 이름 중 틀린 쪽을 골랐다」였습니다.
그 종류의 결함은 **표를 읽어 쓰는 코드를 실제로 쓸 때** 드러납니다. 이 작업이 그 자리입니다.

용도는 하나 더 있습니다. 이 폴더는 **적용 튜토리얼**입니다 — Tabbit을 처음 쓰는 사람이
「시트에서 게임 화면까지」의 전 구간을 하나의 저장소에서 볼 수 있어야 합니다.

## 2. 범위

### 2.1 넣는 것

핵심 루프 한 바퀴가 끝까지 돌아야 합니다.

```
탐사 파견 → (시간 경과) → 정산 → 신규 발견 · 조각 획득
    → 육성 → 각성 → 파티 재구성 → 수호자 도전 → 지역 해금 → 탐사 파견
```

|기획서|시스템|넣는 것|
|--|--|--|
|4절|와일드링|동행 개체 · 능력치 · 단계|
|5절|수집|발견 · 중복 · 조각 전환|
|6절|생태 기록부|관측 누적 · 상태 전이 · 완성 보상|
|7절|각성|조건 확인 · 재료 소모 · 단계 상승|
|8절|파티|3마리 편성 · 열 제약|
|9절|자동 전투|턴제 · 슬롯 순환 · 다형 효과 · 배속|
|10절|탐사|파견 · 누적 · 정산|
|11절|지역|해금 조건 · 지역 전환|
|12절|스테이지|일반 · 관측 · 수호자|
|13절|성장|레벨 · 스킬 레벨 · 공명 등급|
|14절|재화|4종 수지|
|15절|방치 보상|8시간 상한 · 시간대별 체감|

### 2.2 화면만 두는 것

|기획서|시스템|이유|
|--|--|--|
|16절|일일 의뢰|`Mission` 을 목록으로 그리고 진척은 세지 않습니다|
|17절|광고와 과금|결제 연동이 이 샘플의 증명 대상이 아닙니다|
|17.3|광고 보상|버튼은 두되 즉시 지급합니다|

**표를 읽어 화면에 그리는 것까지는 합니다.** `Shop`·`ShopSlot` 3벌·`Package`·`PassBenefit`·
`AdReward`·`Mission` 이 전부 화면에 나옵니다 — 그래야 테이블 40개에 닿지 않는 자리가 없습니다.
하지 않는 것은 그 뒤의 결제와 광고 SDK입니다.

### 2.3 하지 않는 것

- 서버. `EncounterTable` 의 `side=s` 는 원래 서버가 굴리는 것이지만, 이 게임은 단독 실행이므로
  클라이언트가 굴립니다. **그 사실을 코드 주석에 적습니다**
- 연출. 화면 전환과 전투 표시는 값이 읽히는 것을 보이는 수준까지입니다
- 현지화. `StringTable` 2,305행이 한국어 한 벌이므로 그대로 씁니다

## 3. 판정

**Windows standalone 빌드가 실행되고 루프 한 바퀴가 도는 것**입니다.

|단계|판정|
|--|--|
|생성 C# 컴파일|유니티가 `Assets/Tabbit/Generated` 를 오류 없이 컴파일합니다|
|테이블 로드|standalone 에서 `StreamingAssets` 를 읽습니다. 에디터와 경로 처리가 다릅니다|
|그림 로드|`asset=icon` · `asset=model` 이 가리키는 파일이 전부 존재하고 로드됩니다|
|루프|자동 플레이 검사가 한 바퀴를 돌고 보고를 씁니다|

마지막 항목이 이 작업의 게이트입니다. `WildlingDataCheck` 와 같은 방식으로 —
**보고는 파일로 나가고 첫 줄이 `OK` 또는 `FAIL`** 입니다. 종료 코드에 맡기지 않는 이유는
그 파일에 이미 적혀 있습니다.

```
design-data/out/unity-play.txt
```

## 4. 화면

### 4.1 목록

|화면|무엇을 읽는가|
|--|--|
|`Home`|`Currency` · `Region` · `IdleConst`. 재화 4종과 탐사 누적 시간|
|`Expedition`|`Region` · `RegionYield` · `EncounterTable` · `DropTable`. 파견과 정산|
|`Codex`|`Monster` · `CodexReward` · `CodexConst`. 종 54개의 관측 상태|
|`Party`|`Monster` · `PartyConst`. 3마리 편성과 열 제약|
|`Monster`|`GrowthCurve` · `ResonanceRank` · `MonsterSkill` · `MonsterAwakening` · `Skill`|
|`Region`|`Region` · `Stage` · `StageReward` · `RequirementGroup`. 스테이지 목록|
|`Battle`|`Skill` · `SkillEffect` · `SkillGrowth` · `ElementAffinity` · `Boss` · `BattleConst` · `BattleSpeed`|
|`Shop`|`Shop` · `ShopSlot` 3벌 · `Package` · `PassBenefit` · `AdReward` · `Mission`|

### 4.2 전이

```
        Home ─┬─ Expedition ─── (정산) ─── Home
              ├─ Codex ──────── Monster
              ├─ Party ──────── Monster
              ├─ Region ─────── Battle ─── (결과) ─── Region
              └─ Shop
```

`Monster` 화면은 `Codex` 와 `Party` 양쪽에서 열리고, 돌아갈 곳을 기억합니다.

### 4.3 구성 방법

**uGUI를 코드에서 조립합니다.** 씬은 `Main.unity` 하나이고 그 안에는 카메라 · `Canvas` ·
`EventSystem` · 부트 오브젝트만 둡니다.

이유는 둘입니다.

|이유|
|--|
|**diff가 읽힙니다.** 프리팹 YAML의 변경은 리뷰되지 않지만 화면 조립 코드의 변경은 리뷰됩니다|
|**에디터 없이 재현됩니다.** 이 저장소의 다른 산출물과 같은 성질입니다|

튜토리얼 용도에도 이쪽이 낫습니다 — 「이 표의 이 컬럼이 이 텍스트가 됩니다」가 코드 한 줄로
보입니다.

## 5. 상태

세이브는 JSON 한 벌이고 `Application.persistentDataPath/wildling-save.json` 입니다.

|묶음|내용|
|--|--|
|재화|`gold` · `food` · `gem` · `shard` 잔량|
|동행|개체 목록 — `monster_id` · 레벨 · 공명 등급 · 스킬 슬롯 구성|
|기록부|종별 관측 누적치와 상태|
|조각|종별 울림 조각 보유량|
|지역|해금된 지역과 각 지역의 클리어한 스테이지 번호|
|파티|저장 슬롯 3개|
|탐사|파견 중인 지역과 파견 시각|
|아이템|재료 보유량|

**시각은 UTC 초로 남깁니다.** 방치 정산이 시각 차이만 쓰므로 타임존이 개입할 자리가 없습니다.

**세이브에 표의 값을 복사해 두지 않습니다.** 레벨과 공명 등급만 남기고 능력치는 매번
`GrowthCurve` 에서 계산합니다 — 밸런스를 고치고 다시 변환하면 기존 세이브에 그대로 반영되는
것이 이 도구를 쓰는 이유이기 때문입니다.

## 6. 규칙

### 6.1 능력치

```
능력치 = 기본치 × 레벨 배수 × 공명 배수
```

|항|출처|
|--|--|
|기본치|`Monster.base.*`|
|레벨 배수|`GrowthCurve(grade, level).hp_factor` 등. 만분율|
|공명 배수|`ResonanceRank(grade, rank).stat_factor`. 만분율|

**만분율은 정수 연산으로 유지합니다.** `long` 으로 곱하고 마지막에 나눕니다 — 부동소수를
거치면 같은 세이브가 기계마다 다른 값을 낼 수 있습니다.

레벨 상한은 단계별로 `GrowthConst.LevelCapStage1..3` 입니다.

### 6.2 전투

기획서 9.3 그대로입니다.

|규칙|출처|
|--|--|
|행동 순서|`speed` 내림차순, 동률이면 배치 순서(`BattleConst.SpeedTiebreak`)|
|스킬 선택|슬롯 순서대로 순환. 재사용 대기 중이면 건너뜁니다|
|재사용 대기|`Skill.cooldown`|
|결착|`BattleConst.MaxTurn` 턴에 결착이 없으면 남은 체력 비율이 높은 쪽|
|배속|`BattleSpeed` 3단계|

**피해**

```
피해 = 공격 × 스킬 배수 × (1 - min(방어 × DefenseFactor, 80%)) × 상성 배수 × 치명 배수
```

`DefenseFactor` 는 `BattleConst`, 상성 배수는 `ElementAffinity(attacker, defender)` 이고
스킬에 속성이 없으면 `BattleConst.NeutralAffinity` 입니다.

**기획서 9.3 의 식은 방어를 빼는 형태입니다.** 그대로 빼면 방어가 피해를 0.19 깎으므로
비율로 읽었습니다 — [게임 보고 §3](game-findings.md#3-뺄셈으로-읽은-defensefactor-와-무력해지는-방어).

**효과**

`SkillEffect.effect` 가 다형 레코드입니다. 판별자로 좁혀서 처리합니다.

|변종|하는 것|
|--|--|
|`DamageEffect`|`power` 를 배수로 피해|
|`HealEffect`|`power` 를 배수로 회복|
|`BuffEffect`|`stat` 을 `ratio` 만큼 `duration` 턴|
|`StatusEffect`|`status` 를 `chance` 확률로 `duration` 턴|

`chance` 는 만분율입니다. **난수는 시드를 세이브에 두지 않습니다** — 전투 결과가 재현되어야
하는 요구가 기획에 없습니다.

스킬 레벨은 `SkillGrowth` 가 배수를 올립니다.

### 6.3 탐사와 방치

|단계|출처|
|--|--|
|누적 시간|현재 시각 - 파견 시각. `IdleConst.CapHours` 시간에서 멈춥니다|
|시간대별 산출|`RegionYield(region_id, hour_band)`. 시간대가 뒤로 갈수록 줄어듭니다|
|재료|`RegionYield.reward_group_id` → `RewardGroup` · `RewardEntry`|
|발견|`EncounterTable.entries` 의 가중치 추첨. 미기록 종은 `CollectionConst.UnrecordedBoost` 로 가중|
|은둔 슬롯|`EncounterTable.requirement_group_id` 를 만족해야 후보에 들어갑니다|

### 6.4 수집과 기록부

|규칙|출처|
|--|--|
|중복 발견|울림 조각으로 전환. 상한은 `CollectionConst.ShardCap`|
|공명 등급 상승|`ResonanceRank.shard_cost` 만큼 조각 소모|
|관측 상한|등급별 `CodexConst.ObserveCap*`|
|전투 관측|승리 시 `CodexConst.BattleObserveFactor` 만큼|
|완성 보상|`CodexReward`|

### 6.5 각성

`MonsterAwakening` 이 관계이고 **소모 재화가 원소마다 행 하나인 멀티 로우**입니다.

|단계|
|--|
|조건을 `RequirementGroup` · `RequirementEntry` 로 확인|
|`costs` 의 모든 원소를 소모|
|`to_monster_id` 가 가리키는 행으로 교체하고 레벨은 1로|
|슬롯 수가 `AwakeningConst` 대로 늘어납니다|

### 6.6 보상

`RewardGroup` · `RewardEntry` 가 8개 테이블이 공유하는 묶음입니다. `RewardEntry.reward` 가
다형이므로 지급도 변종별로 갈립니다.

|변종|지급|
|--|--|
|`ItemReward`|`item_id` 의 보유량 증가|
|`CurrencyReward`|`currency_id` 의 잔량 증가|
|`MonsterReward`|동행 추가. 이미 있으면 조각으로|
|`ShardReward`|해당 종의 조각 증가|

## 7. 그림

### 7.1 방법

**코드로 만듭니다.** 파이썬이 도형을 그려 PNG 를 직접 씁니다 — `design-data/tools/raster.py`
가 3배 해상도에 그린 뒤 줄이고, `zlib` 말고는 가져오는 것이 없습니다.

이유는 셋입니다.

|이유|
|--|
|**재현됩니다.** 데이터가 바뀌면 다시 만들어 같은 결과가 나옵니다|
|**저장소가 가볍습니다.** 201장을 합쳐 0.9 MB입니다. 설치할 것도 없습니다|
|**표에서 나옵니다.** 색과 형태가 `element` · `grade` · `category` 에서 결정되므로, 그림 자체가 데이터가 읽혔다는 증거입니다|

마지막 항목이 요점입니다. 아이콘의 색이 속성과 다르면 그 자리에서 보입니다.

### 7.2 규칙

|묶음|개수|무엇으로 결정되는가|
|--|--|--|
|`wl_*` 와일드링|54|`element` 가 색상 · `grade` 가 테두리 · `role` 이 실루엣 · `stage` 가 장식 수|
|`sk_*` 스킬|46|`element` 가 색상 · `target_scope` 가 배치 · 효과 변종이 기호|
|`it_*` 아이템|92|`category` 가 형태 · 지역이 색상 · 등급이 테두리|
|`cur_*` 재화|4|고정 4색|
|`bg_*` 배경|5|`Region.fog_color` 를 그대로 씁니다|

**이름은 표가 정합니다.** 생성기가 `Monster.tsv` 의 `icon` 컬럼을 읽어 그 이름으로 씁니다 —
손으로 적은 목록을 두지 않습니다. 종이 늘면 그림도 늘어납니다.

### 7.3 배경

`Region.background` 는 `asset=model` 이고 프리팹이었습니다. 프리팹 YAML을 손으로 쓰는 것보다
**스프라이트 5장과 그것을 배치하는 코드**가 낫습니다. 컬럼의 `asset=model` 규격은 그대로 두고
로더가 `Resources/art/model/<이름>.png` 를 찾습니다. `recipe.jsonc` 의 애셋 뿌리도 `*.png`
로 바뀌었습니다.

## 8. 파일 배치

```
unity/Assets/
  Scenes/Main.unity                    씬 하나. 부트 오브젝트만 들어 있습니다
  Scripts/
    Runtime/
      Boot.cs                          부팅 순서. 카메라와 Canvas 도 여기서 만듭니다
      Save/SaveStore.cs                세이브와 시각
      State/                           진행 상태 — SaveData · GameState
      Sim/                             계산 — Numbers · Stats · Battle · Rewards ·
                                       Requirements · Expedition · Korean
      Art/ArtLibrary.cs                asset= 이름으로 스프라이트 조회
      UI/                              Theme · Ui · App · 화면 8개
      Play/AutoPlay.cs                 자동 플레이 검사
      Play/ScreenTour.cs               화면을 그림으로 남기는 순회
    Editor/
      WildlingDataCheck.cs             값이 맞는가. 그대로 둡니다
      WildlingPlayCheck.cs             규칙이 도는가
      WildlingBuild.cs                 씬 만들기와 standalone 빌드
  Resources/art/icon/*.png             196장. 생성물
  Resources/art/model/*.png            5장. 생성물

design-data/tools/raster.py            의존성 없는 PNG 래스터라이저
design-data/tools/art.py               그림 생성기
design-data/out/unity-play.txt         자동 플레이 보고
design-data/out/unity-build.txt        빌드 보고
```

**`Sim/` 이 유니티에 의존하지 않습니다.** `UnityEngine` 을 참조하지 않는 순수 C#이므로 규칙을
그 자체로 읽을 수 있고, 튜토리얼에서 「생성 코드를 어떻게 쓰는가」를 보이는 자리도 거기입니다.

**그림이 `Resources/` 아래인 이유.** 게임이 실행 중에 `asset=` 컬럼이 적은 이름으로 찾습니다.
그 폴더가 아니면 빌드에 들어가지 않으므로, `recipe.jsonc` 의 애셋 경로도 그쪽을 봅니다.

## 9. 확인 절차

```
python samples/wildling/design-data/tools/art.py
dotnet run --project samples/wildling/design-data/tools/Authoring
dotnet run --project src/Tabbit.csproj -- --recipe samples/wildling/design-data/recipe.jsonc
python samples/wildling/design-data/tools/verify.py
```

유니티는 셋을 돌립니다. **판정은 종료 코드가 아니라 보고 파일의 첫 줄**입니다.

|무엇|진입점|보고|
|--|--|--|
|데이터 확인|`Wildling.Check.WildlingDataCheck.RunFromCommandLine`|`out/unity-check.txt`|
|자동 플레이|`Wildling.Check.WildlingPlayCheck.RunFromCommandLine`|`out/unity-play.txt`|
|빌드|`Wildling.Check.WildlingBuild.BuildFromCommandLine`|`out/unity-build.txt`|

```
Unity.exe -batchmode -quit -nographics -projectPath samples/wildling/unity           -executeMethod Wildling.Check.WildlingPlayCheck.RunFromCommandLine -logFile -
```

### 9.1 자동 플레이가 도는 것

|순서|확인되는 것|
|--|--|
|표 로드|`.bytes` 30개|
|그림 로드|`asset=` 이 가리키는 201개가 전부 열립니다|
|새 판|시작 동행과 첫 지역|
|파티|`PartyConst` 의 열 제약|
|능력치|1레벨이 기본치와 같은가|
|육성|`GrowthCurve` 로 상한까지, 능력치가 실제로 오르는가|
|공명|`ResonanceRank` 로 상한까지|
|탐사|8시간 정산 · `RegionYield` 8구간 · 시간대별 체감 · `EncounterTable` 추첨|
|각성|`MonsterAwakening` 멀티 로우 · 조건 · 슬롯 증가 · 공명 유지|
|루프|**막히면 키우고 다시 도전합니다** — 18개 스테이지와 수호자를 넘습니다|
|지역 해금|`RequirementGroup` 을 다시 확인해 다음 지역이 열리는가|
|보상 변종|`RewardEntry` 4종 · `SkillEffect` 4종|
|세이브|되읽어 같은 값이 나오는가|

### 9.2 눈으로 보는 것

값이 맞아도 글자가 잘리거나 카드가 겹치는 것은 검사로 잡히지 않습니다. 그래서 빌드가 스스로
화면을 돌며 그림을 남깁니다.

```
wildling.exe -screen-width 540 -screen-height 960 -screen-fullscreen 0 -shots <폴더>
```

화면 9장을 남기고 스스로 종료합니다. **마우스를 쓰지 않으므로** 사람이 쓰고 있는 기계에서
돌려도 입력이 창 밖으로 새지 않습니다.

### 9.3 지금의 기준선

|검사|결과|
|--|--|
|`verify.py`|통과 9 · 실패 0 · 건너뜀 1|
|`unity-check.txt`|`OK`|
|`unity-play.txt`|`OK`|
|`unity-build.txt`|`OK` — 96 MB · 오류 0|

게임을 만들면서 나온 것 6건은 [게임 보고](game-findings.md)에 있습니다. **넷을 고쳤고 하나는
읽는 법을 적었으며 하나는 밸런스 결정으로 열어 두었습니다.**

---

EOD
