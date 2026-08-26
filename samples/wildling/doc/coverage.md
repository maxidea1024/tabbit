# 대조표 — 기능 하나가 어디에 어떻게 쓰였나

**이 폴더의 목적이 이 표입니다.** 기능 목록은 [기능](../../../doc/features.md)에 있고, 여기 있는
것은 그 기능 하나하나가 **실전 데이터의 어느 자리에서 어떻게 쓰였고 무엇이 나왔는가**입니다.

각 항목은 셋으로 적습니다.

|  |무엇|
|--|--|
|**시트**|어느 워크북 어느 탭의 어느 컬럼이고, 타입 칸에 무엇이 적혀 있는가|
|**왜 거기**|그 기획이 왜 그 표현을 요구하는가. 억지로 넣은 자리가 아니라는 근거|
|**나온 것**|생성 C#·`.bytes`·유니티 확인에서 실제로 확인되는 것|

인용은 전부 실물입니다 — `design-data/data/*.tsv` 와 `unity/Assets/Tabbit/Generated/` 에서
그대로 옮겼습니다.

---

## 1. 시트에 무엇을 적을 수 있나

### 1.1 선언 셀과 키

|시트|나온 것|
|--|--|
|`Monster` — `:table Monster`|기본 인덱스가 첫 필드 컬럼입니다. `FindByMonsterId(string)`|
|`Item` — `:table Item(key=item_id)`|첫 컬럼은 `category` 이고 인덱스가 아닙니다. **`key=` 가 인덱스를 옮깁니다**|
|`Stage` — `:table Stage(key="stage_id; region_id,index")`|`FindByStageId(string)` 과 `FindByRegionIdAndIndex(string, int)` **둘 다** 나옵니다|
|`RegionYield` — `:table RegionYield(key="region_id,hour_band")`|`FindByRegionIdAndHourBand(string, int)`. 복합 기본 인덱스이므로 이 테이블은 `foreign` 의 대상이 되지 않습니다|
|`MonsterSkill` — `:table MonsterSkill(key="monster_id,skill_id")`|**성분이 둘 다 참조**인 연결 테이블입니다. 그 조합이 [도구 보고](tool-findings.md) §7이 나온 자리입니다|
|`EncounterTable` — `:table EncounterTable(side=s)`|출현 확률은 클라이언트가 알 필요가 없고, 알면 조작 대상이 됩니다|

**왜 거기.** 스테이지는 `(지역, 순번)` 으로도 찾고 id로도 가리켜야 합니다 — 각성 조건이
`StageRequirement` 로 그것을 가리키기 때문입니다. 첫 키가 단일이어야 참조 대상이 되므로
그 순서로 적었습니다.

### 1.2 값의 표현

|기능|시트|나온 것|
|--|--|--|
|`bitset`|`Monster.habitat` — `bitset`, 값은 `0b00011`|`public long Habitat`. 유니티 확인에서 `0b00011`|
|셀 배열|`Monster.tags` — `string[]`, 값은 `잎새;사슴;초기형`|`public string[] Tags`. **원소 3개**|
|합성 값 타입|`Monster.model_offset` — `vec3f?`, 값은 `(0,0.2,0)`|`public ModelOffsetEntry ModelOffset` — 성분마다 컬럼입니다|
|합성 값 타입|`Region.fog_color` — `color`, 값은 `#C8DCC0`|`public FogColorEntry FogColor`|
|참조 배열|`Stage.wave_monster_ids` — `foreign Monster[]`|`public MonsterTable.Record[] MonsterByWaveMonsterIds` — 키 배열이 **행 배열**로|
|셀 배열 + 크기 제약|`Stage.wave_levels` — `int[] (size=1..5)`|`public int[] WaveLevels`|
|검사 `refs=`|`Mission.target_id` — `string (refs="Monster;Region;Item")`|**생성 코드가 없습니다.** 값이 셋 중 하나의 id인지만 봅니다|
|보조 인덱스 + `regex`|`Monster.*display_code` — `string (regex="^[a-z]{2}[0-9]{3}$")`|`RecordsByDisplayCode` 와 `FindByDisplayCode`|
|`text` + 네임스페이스|`Monster.description` — `string (text=Monster, namespace=Codex)`|값은 `string` 그대로입니다 — 역할만 붙으므로 **와이어가 달라지지 않습니다**|
|`asset`|`Monster.icon` — `string (asset=icon)`|`unity/Assets/Art/icon/` 에 그 이름의 파일이 있는지 검사합니다. **유니티 애셋이므로 자리표가 아닙니다**|

**왜 거기.** 서식 지역은 종마다 여러 곳이고 기록부가 그것을 플래그로만 씁니다 — 행을 조회하지
않으므로 `foreign Region[]` 이 아니라 `bitset` 입니다. 반대로 스테이지의 등장 목록은 그 행의
능력치를 읽어야 하므로 참조 배열입니다. **같은 「여럿」이 두 표현으로 갈리는 자리**입니다.

### 1.3 배열이 오는 세 자리

|자리|시트|
|--|--|
|셀 안|`Monster.tags` — `string[]`, 한 칸에 `잎새;사슴;초기형`|
|칸|`SkillGrowth.costs[0].currency_id` · `costs[1].currency_id` — 원소 번호가 컬럼|
|행|`EncounterTable.entries[].monster_id` — 원소가 **행**에서 옵니다|

세 표기 모두 와이어는 같은 배열 컬럼 하나입니다. 다른 것은 시트에서 읽는 방법뿐입니다.

### 1.4 같은 타입, 세 표기 — `Cost`

|시트|셀에 적히는 것|
|--|--|
|`MonsterAwakening.costs[]` — `Cost`|`shard,30` — 멀티 로우 + `sep`|
|`SkillGrowth.costs[0].currency_id` `[0].amount`|`gold` / `1200` — 원소 번호 칸에 멤버를 펼침|
|`GrowthCurve.costs[0].currency_id` `[0].amount`|같음|

`.tbs` 의 `struct Cost (sep=",")` 하나가 셋을 답합니다. **`sep` 이 있으면 한 셀에 적을 수 있고,
없으면 멤버를 컬럼으로 펼칩니다** — 와이어는 같습니다.

### 1.5 다형

|시트|나온 것|
|--|--|
|`RewardEntry.reward.$type` — `Reward`|`abstract class Reward` 와 변종 4개. 유니티에서 `is ItemReward` 로 좁혀집니다|
|`SkillEffect.effect.$type` — `Effect`|변종 4개. `power` 와 `duration` 은 **컬럼을 공유**합니다|
|`Boss.effects[].$type` — `Effect`|**멀티 로우 다형 배열.** 원소마다 형태가 다릅니다|

**왜 거기.** 보상은 「아이템이거나 재화이거나 와일드링이거나 조각」이고, 참조는 컬럼당 대상
하나입니다. 변종마다 자기 카탈로그로의 참조를 두는 것이 그 자리의 답이고, 그것이
[도구 보고](tool-findings.md) §1 · §8이 나온 자리입니다 — **가장 자연스러운 형태가 가장 덜
다녀진 길이었습니다.**

수호자의 효과만 멀티 로우인 이유는 그것이 그 수호자에만 종속되어 묶음 id를 줄 가치가 없기
때문입니다. 보상과 조건은 여러 테이블이 공유하므로 항목 테이블로 내렸습니다.

### 1.6 선언 — `.tbs`

|선언|쓰는 곳|
|--|--|
|`struct StatBlock`|`Monster.base` · `Boss.stat_factor`|
|`struct Cost (sep=",")`|3곳, 세 표기로|
|`struct EncounterEntry`|`EncounterTable.entries[]`|
|`abstract struct Reward` + 변종 4개|`RewardEntry`|
|`abstract struct Requirement` + 변종 4개|`RequirementEntry`|
|`abstract struct Effect` + 변종 4개|`SkillEffect` · `Boss`|
|enum 6개|`Element` · `Grade` · `CodexState` · `StatKind` · `StatusKind` · `EncounterSlot`|

**타입도 설명도 선언이 답합니다.** 그룹의 `:type` 과 `:desc` 를 비우면 `///` 가 컬럼 설명이
됩니다 — 첫 멤버에만 적으면 그것이 정본이 되어 선언이 답하지 않으므로, **그룹 전체를 비우는
것**이 이 표기의 뜻입니다.

enum을 `.tbs` 와 시트에 반씩 둔 것은 **두 표기가 다 검증되어야** 하기 때문입니다. `.tbs` 쪽은
`Effect` · `Requirement` 변종이 멤버 타입으로 쓰는 것들입니다 — 선언된 멤버의 타입은 그
파일들 안에서 찾을 수 있어야 하고, 그래서 시트의 enum을 가리킬 수 없습니다.

### 1.7 시트 운영

|기능|시트|
|--|--|
|와이어 태그 `@N`|`Item` 의 모든 컬럼|
|묘비|`Item.#old_price@5` — 5번을 예약합니다|
|메모 컬럼|`Monster.#` · `GrowthCurve.#` — 모델에 흔적이 없습니다|
|행 제외|`Monster` · `Skill` · `Item` 의 마커 열 `#`|
|필드 변형 `:variant`|`Package.price_display` — `kr`(기본) · `us` · `jp`|
|행 벌|`ShopSlot` · `ShopSlot_Season` · `ShopSlot_Package` → `.bytes` 3개|
|`:target`|`Monster.icon`(c) · `RewardEntry.server_weight`(s)|
|한 시트에 엔티티 여럿|`Const_Battle`(상수셋 + 표) · `Const_Growth`(상수셋 둘)|

**태그와 묘비가 `Item` 에 있는 이유**는 `Monster` 에 `vec3f` 가 있기 때문입니다. 합성 값
타입은 성분마다 컬럼이 되므로 성분마다 태그가 필요하고, 한 테이블에서 둘을 함께 쓸 수는
없습니다. 결함이 아니라 형식의 사정이고, 그 진단이 제자리를 가리키는지가
[도구 보고](tool-findings.md) §3이었습니다.

---

## 2. 검증

|규칙|무엇을 보는가|
|--|--|
|`MonsterAwakeningRules`|**각성 후 능력치가 직전 단계보다 낮지 않은가.** 각성이 손실이면 수집의 목표가 성립하지 않습니다|
|`ProgressionRules`|**각성 재료가 2개 지역 이상에서 나오는가.** 한 곳뿐이면 그 지역을 반복하는 것 외에 할 일이 없어집니다|
|`MonsterRules`|종과 단계의 조합, 단계가 이어지는가, 서식 지역이 하나라도 있는가|
|`StageRules`|짝을 이루는 두 배열의 길이가 같은가|
|`EncounterTableRules`|지역마다 고유 종 4종 이상, 은둔 슬롯 하나|
|`RewardEntryRules`|아무것도 나오지 않을 수 있는 묶음, 순서가 겹치는 항목|
|`ConventionsRules`|설명 없는 컬럼, 아이콘 이름의 접두어|
|`InputsRules`|recipe가 넘긴 자유 키 — 코어는 그 키를 모릅니다|

**앞의 둘이 요점입니다.** 기획서가 정한 것이고 타입으로는 표현되지 않으며, 어긋나면 게임이
성립하지 않습니다. 나머지는 그 둘이 혼자 서지 않도록 받치는 것들입니다.

규칙을 쓰면서 하나 배웠습니다 — 처음에 확률의 합을 검사했더니 99건이 울렸고, 도구가
「이만큼 보고하는 규칙은 보통 규칙이 틀린 것」이라고 말했습니다. **그 말이 맞았습니다.** 이
데이터의 묶음은 항목마다 독립 확률이라 합이 10000을 넘는 것이 정상입니다. 규칙을 바꾸자
재료 묶음 5개가 **확정 항목 없이 전부 확률**이라는 진짜 문제를 짚었습니다.

---

## 3. 산출

|타깃|어디|
|--|--|
|`csharp`|`unity/Assets/Tabbit/Generated/` — 유니티가 그대로 컴파일합니다|
|`binary`|`unity/Assets/StreamingAssets/tables/*.bytes` — 유니티는 이 확장자만 TextAsset으로 포함합니다|
|`html`|`design-data/out/html/` — **유니티 애셋이 아니므로 프로젝트 밖입니다**|
|인코딩 보고서|`design-data/out/encoding-report.txt` — 컬럼마다 이긴 인코딩과 후보별 실측 크기|
|스키마 기준선|`design-data/out/schema-baseline.json` — `Monster.Tags` 가 문자열에서 배열이 되었을 때 그것을 잡은 것이 이 파일입니다|
|업데이터|`WriteUpdater` — 데이터만 따로 패치하는 경로|

---

## 4. 유니티에서 확인되는 것

[unity-check.txt](../design-data/out/unity-check.txt) 의 내용입니다. **행 수만 보면 「읽었다」
까지이고, 값을 보면 「맞게 읽었다」까지입니다.**

|보는 것|확인되는 것|
|--|--|
|`hp=420 atk=38 element=Leaf grade=Common`|값이 시트대로 왔는가|
|`태그 3개 — 잎새, 사슴, 초기형`|셀 배열이 원소로 갈렸는가|
|`0b00011`|`bitset` 이 값 하나로 왔는가|
|`sprout_deer_1 -> sprout_deer_2 (잎뿔사슴)`|참조가 링킹으로 **행**이 되었는가|
|`ItemReward 125, CurrencyReward 239, MonsterReward 5`|판별자가 변종 타입으로 왔는가|
|`여울숲 수액(mat_weir_forest_resin) × 3`|**변종의 참조**가 행으로 연결되었는가|
|`FindByRegionIdAndHourBand("weir_forest", 0)`|복합 키 조회가 나왔는가|
|`여울숲 출현 28종`|멀티 로우가 원소로 쌓였는가|
|`방치 상한 8시간 · 파티 3마리`|상수셋이 코드로 나갔는가|

---

## 5. 이 데이터가 쓰지 않는 것

|기능|왜|
|--|--|
|구글 스프레드시트 소스|자격증명이 필요해 저장소에 담을 수 없습니다|
|데이터베이스 타깃 4종|서버가 필요합니다. 회귀 스위트가 컨테이너로 봅니다|
|히스토리 · `--serve`|실행이 필요합니다|
|`sheet-per-table` · `named-range` 레이아웃|`sprout`과 `canopy`의 몫입니다|
|매트릭스 표|**이름 기반 레이아웃의 기능**입니다. 속성 상성은 복합 키 표로 적었습니다|
|전 언어 타깃|`samples/sprout/out/` 이 이미 그 답입니다. 이 샘플은 **무엇을 적을 수 있나** 쪽입니다|
|`set` · `map`|이름과 함께 거절됩니다|

---

EOD
