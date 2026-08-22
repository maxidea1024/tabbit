# 시트에 무엇을 적을 수 있나

세 가지 엔티티와, 그것이 각각 무엇이 되어 나오는지 — **실제 시트 하나를 끝까지 따라가면서**.

> [문서 목록으로](readme.md)

---

여기 나오는 시트·명령·생성된 코드는 전부 이 저장소의
[`core` 픽스처](../test/fixtures/tools/FixtureGen)에서 그대로 가져온 것입니다. 지어낸 예제가
아니라, 회귀 스위트가 매번 변환해서 [골든 트리](../test/fixtures/golden/core)와 바이트 단위로
대조하는 그 시트입니다.

## 1. 시트

셀에 마커를 적으면 그 자리가 엔티티가 됩니다. 어느 시트, 어느 위치든 상관없고 한 시트에 여러
개를 놓아도 됩니다.

마커 아래로 **다섯 줄이 컬럼을 설명하고**, 그다음부터 데이터입니다 — 이름 · 주석 · 타입 ·
세부 타입 · 대상. 빈 줄은 비워 두면 됩니다.

**테이블 `ItemCategory`** — 아이템이 가리킬 분류입니다.

```
~~table:ItemCategory~~
Referenced by Item.CategoryId.
index           Name            Description                   ← 이름
primary index   category name   human readable description    ← 주석
int             string          string                        ← 타입
                                                              ← 세부 타입 (없음)
                                                              ← 대상 (양쪽)
1               Weapon          things that hit
2               Armor           things that absorb
3               Potion          things that heal
```

**enum `Grade`** — 이름 붙은 정수 값입니다. 테이블 컬럼의 타입으로 씁니다.

```
~~enum:Grade~~
Item grade. Deliberately omits a zero entry.
name     value   description
Common   1       common grade
Rare     2       rare grade
Epic     3       epic grade
```

> 0 항목이 없습니다. 그러면 **`None = 0`이 자동으로 들어갑니다** — enum 필드는 값이 대입되기
> 전에도 뭔가를 들고 있는데, 그게 이름 없는 0이면 디버거에서도 로그에서도 읽을 수 없기
> 때문입니다. 시트가 0에 이미 뭔가를 두었다면 손대지 않고, `AutoInsertEnumNoneLabel`로 끌 수도
> 있습니다.

**테이블 `Item`** — 위의 둘을 다 씁니다. 폭 때문에 컬럼 넷만 옮겼습니다(실제로는 일곱입니다).

```
~~table:Item~~
References ItemCategory by record.
index           Name          CategoryId        GradeField    Price
primary index   item name     owning category   item grade    shop price
int             string        foreign           enum          int
                              ItemCategory      Grade
                                                              s
1               Short Sword   1                 Common        100
2               Leather Armor 2                 Rare          250
3               Small Potion  3                 Epic          50
```

여기서 셋을 봐 두면 나머지는 [시트 작성](sheets.md)이 자세히 말합니다.

|적은 것|뜻|
|--|--|
|타입 `foreign`, 세부 타입 `ItemCategory`|**이 값은 저 테이블의 행이다.** 숫자가 아니라 행입니다 — 아래에서 그 차이가 나옵니다|
|타입 `enum`, 세부 타입 `Grade`|셀에 `Common`이라고 적습니다. 숫자를 외울 일이 없고, 오타는 변환에서 잡힙니다|
|`Price`의 대상 줄에 적은 `s`|**서버 빌드에만** 넣습니다. 클라이언트가 받는 파일에는 이 컬럼이 아예 없습니다|

여기서 뺀 컬럼 중 하나가 `SkillField`이고, 그 셀에는 `fire_ball`처럼 **선언된 철자 그대로**
적습니다. 생성되는 타입 이름은 언어 관례를 따르지만, 시트는 시트의 표기를 지킵니다.

## 2. recipe와 실행

무엇을 어디서 읽어 어디로 내보낼지는 recipe에 적습니다. 백지에서 시작할 필요는 없습니다.

```
tabbit --new-recipe my-recipe.json --template unity
tabbit --recipe my-recipe.json
```

## 3. 나온 것

`Item` 하나에서 나오는 C#입니다. **골든에 있는 그대로입니다.**

```csharp
public string Name => _name;
public ItemCategoryTable.Record CategoryId => _categoryId;   // int 가 아닙니다
public Grade GradeField => _gradeField;
public string Description => _description;
public int Price => _price;                                  // 서버 빌드에만
```

**`CategoryId`가 `int`가 아니라 `ItemCategoryTable.Record`입니다.** 파일에는 인덱스로 실리고,
`ReadAllAsync`가 모든 테이블을 읽은 뒤 실제 레코드로 연결합니다. 그래서 이렇게 씁니다.

```csharp
await GameData.ReadAllAsync("./data");

var sword = GameData.Item.FindByIndex(1);
Console.WriteLine(sword.Name);                  // Short Sword
Console.WriteLine(sword.CategoryId.Name);       // Weapon   ← 조회를 한 번 더 하지 않습니다
Console.WriteLine(sword.GradeField);            // Common
```

조회는 인덱싱된 필드마다 셋이 나옵니다 — `FindByIndex`(없으면 널) ·
`GetByIndexOrThrow`(없으면 예외) · `ContainsIndex`. **이름이 동작을 말합니다**, 그래서 검사를
빼먹은 자리가 읽는 것만으로 보입니다.

같은 시트에서 지원하는 모든 언어가 나오고, 표기는 각 언어의 관례를 따릅니다 — TypeScript는
`tables.item.findByIndex(1)`, Python은 `tables.item.find_by_index(1)`.
자세한 것은 [언어별 가이드](languages/readme.md)에 있습니다.

## 세 가지 정리

|엔티티|마커|나오는 것|
|--|--|--|
|**테이블**|`~~table:Item~~`|레코드 타입, 인덱스별 조회, **데이터 파일**|
|**enum**|`~~enum:Grade~~`|언어별 열거형 타입|
|**상수셋**|`~~const:Limits~~`|언어별 상수 선언|

상수셋은 행이 아니라 이름·타입·값의 목록입니다 — 한 줄짜리 설정값들이 테이블 흉내를 내지
않아도 되는 자리입니다.

**나오는 것이 다르니 배포도 다릅니다.**

|엔티티|배포|
|--|--|
|테이블|데이터 파일로 나갑니다. 대개 **데이터만 올려도** 반영됩니다|
|enum · 상수셋|코드로 나갑니다. **코드 배포가 함께 필요합니다**|

특히 상수셋은 데이터 파일에 흔적이 아예 없어서, 값을 고쳐도 코드를 다시 배포하기 전에는
아무것도 달라지지 않습니다. 이 판정은 눈으로 하지 않아도 됩니다 — 히스토리가 커밋마다
[어느 쪽이 나가야 하는지](languages/readme.md#데이터만-나가도-되는-변경과-코드가-함께-나가야-하는-변경)
보고합니다.

## 다른 규칙으로 쓰인 시트 읽기

위 마커 방식이 기본(`tabbit` 레이아웃)이지만, **다른 규칙으로 작성된 시트도 그대로 읽을 수
있습니다.** 시트를 먼저 고치지 않아도 됩니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",       "Layout": "tabbit" },
  { "Path": "./other-sheets", "Layout": "rescue" }
]
```

레이아웃은 **소스 항목마다** 지정하므로 한 recipe에서 섞어 읽을 수 있고, 한쪽에서 선언한 enum을
다른 쪽 테이블이 타입으로 써도 됩니다. 실제로 라이브 서비스 중인 프로젝트의 워크북 20개·테이블
275개·269,870행을 손대지 않고 한 모델로 읽습니다.

- recipe 설정: [Recipe 파일 — Layout](recipe.md#layout--시트를-읽는-방식)
- 적용 기록: [다른 규칙으로 쓰인 시트 읽기](../samples/rescue/doc/적용-기록.md)
- 레이아웃을 새로 만드는 법: [아키텍처와 개발](architecture.md#설계-원칙--코어에-프로젝트-이름-금지)

## 다음

|무엇이 궁금한가|어디|
|--|--|
|시트에 적을 수 있는 것 전부 — 타입·인덱스·배열·중첩·제약|[시트 작성](sheets.md)|
|recipe에 적을 수 있는 것 전부|[Recipe 파일](recipe.md)|
|생성된 코드를 프로젝트에 넣는 법|[언어별 가이드](languages/readme.md)|
|시트로 표현할 수 없는 규칙 검사|[검증](validation.md)|
