# 설정 하나하나

> [「Recipe 파일」로 돌아가기](../recipe.md)

---

## 공통 설정

|키|기본값|설명|
|--|--|--|
|`DefaultDelimiter`|`";"`|한 셀 안에 여러 값을 적을 때의 구분자 — 배열의 원소, `set`·`map` 셀의 항목. 정확히 한 글자여야 합니다. [아래](#defaultdelimiter--한-셀-안의-값-구분자) 참고.|
|`TimeZone`|`""`|`datetime` 셀에 적힌 시각을 어느 시간대의 것으로 읽을지. [아래](#timezone--시트의-날짜를-어느-시간대로-읽을지) 참고.|
|`Naming`|(아래 참조)|시트에 적은 이름의 표기 규약. 소스별이 아니라 전역입니다 — 이름은 모델의 것이고, 워크북마다 다르게 적힌 한 이름을 찾는 것이 이 검사의 목적입니다|
|`DataFileCase`|`""`|내보낸 데이터 파일 이름의 표기. `pascal` · `camel` · `snake` · `upper-snake`. 비우면 테이블 이름 그대로입니다|
|`TrueWords` · `FalseWords`|`[]`|`bool` 셀을 참·거짓으로 읽을 낱말. 내장 낱말에 더해집니다. [아래](#truewords--falsewords--참과-거짓의-낱말) 참고.|
|`BuiltinBoolWords`|`true`|`Y`·`YES`·`TRUE`·`N`·`NO`·`FALSE`를 함께 읽을지|
|`ExcludeTags`|`[]`|**이 빌드에서 뺄 행 태그.** 시트의 마커 열에 적힌 태그를 부릅니다. [아래](#excludetags--이-빌드에서-뺄-행) 참고.|

### `TimeZone` — 시트의 날짜를 어느 시간대로 읽을지

시트에 적힌 `2022-01-24 10:30:00`은 **어느 시간대의 10시 30분인지 말하지 않습니다.** 이 설정이
그것을 정하고, 저장되는 값은 언제나 UTC입니다.

```jsonc
"TimeZone": "Asia/Seoul",     // 또는 "+09:00"
```

|적는 형식|예|비고|
|--|--|--|
|지역 이름|`Asia/Seoul` · `America/New_York` · `Korea Standard Time`|서머타임을 포함한 그 지역의 역사를 따릅니다. 기계에 시간대 데이터가 있어야 합니다|
|고정 오프셋|`+09:00` · `-05:30` · `+0900` · `+09` · `Z`|일 년 내내 같은 오프셋입니다. 시간대 데이터가 필요 없습니다|

**비워 두면 셀이 이미 UTC로 적힌 것으로 봅니다.** 값을 건드리지 않으므로, 이 설정을 적지 않은
recipe의 산출물은 이 설정이 없던 때와 같습니다.

|셀에 적힌 것|`TimeZone` 없음|`"Asia/Seoul"`|
|--|--|--|
|`2022-01-24 10:30:00`|10:30|**01:30** — KST 10:30을 UTC로|
|`2022-01-24T10:30:00Z`|10:30|10:30 — **셀이 우선합니다**|
|`2022-01-24T10:30:00+09:00`|01:30|01:30 — **셀이 우선합니다**|

셀이 오프셋을 직접 적었으면 그것이 우선합니다. 이미 한 순간을 가리키는 값을 다시 지역 시간으로
읽을 여지가 없기 때문입니다.

**지역 이름을 쓰면 서머타임의 두 자리가 생깁니다.** 시계가 건너뛴 시각(예: `America/New_York`의
2022-03-13 02:30)은 그 지역에 존재하지 않았으므로 **셀 위치와 함께 거부합니다** — 앞과 뒤 어느
쪽으로 읽어도 한 시간이 틀리고, 그것을 도구가 정할 수는 없습니다. 시계가 되돌아가 두 번
지나간 시각(2022-11-06 01:30)은 **표준시 쪽으로 읽고** 그런 셀이 몇 개였는지 한 줄로
보고합니다. 다른 쪽을 원하면 셀에 오프셋을 적습니다.

고정 오프셋에는 두 자리가 모두 없습니다 — 그것이 고정 오프셋의 뜻입니다.

> 데이터는 UTC이고, 지역 시간으로 보여 주는 것은 읽는 쪽의 일입니다. 이 설정은 **시트를 읽는
> 방법**이지 내보내는 형식이 아닙니다. 자세한 것은
> [시트의 datetime과 시간대](../../spec/types/datetime-timezone.md).

### `DefaultDelimiter` — 한 셀 안의 값 구분자

한 셀에 값을 여러 개 적을 때 무엇으로 끊는지입니다. 배열의 원소와 `set`·`map` 셀의 항목이
그것입니다.

```json
"DefaultDelimiter": ";"
```

기본값이 쉼표가 아니라 세미콜론인 것은, 쉼표가 평범한 문장에도 사람이 읽는 숫자 표기에도
끊임없이 나오기 때문입니다. 원소의 앞뒤 공백은 제거되므로 `1; 2 ;3`은 `1;2;3`과 같습니다.

**이름에 `Default`가 붙은 이유가 있습니다.** 구분자를 정하는 자리가 이것 말고도 있고, 좁은
쪽이 우선합니다.

|정하는 자리|적용 범위|
|--|--|
|구조체 선언의 `:sep`|그 타입의 셀. [STRUCT DSL](../sheets/types.md)|
|합성 값 타입|성분 사이는 언제나 쉼표입니다. 정할 수 없습니다|
|소스 항목의 `DefaultDelimiter`|그 항목이 읽는 시트|
|**recipe의 `DefaultDelimiter`**|**타입도 항목도 말하지 않은 나머지 전부**|

> **예전 이름은 `ArrayDelimiter`였습니다.** 그대로 적혀 있어도 읽히고, 지금 이름으로 고치라는
> 경고가 함께 나옵니다. 둘을 함께 적으면 오류입니다.

### `TrueWords` · `FalseWords` — 참과 거짓의 낱말

시트는 디자이너가 읽는 문서입니다. 한국어로 채우는 시트에 `Y`·`N`이라고 적으면 읽는 사람이
매번 옮겨 읽어야 하고, 수식으로 `예`를 `Y`로 바꾸어 두면 컬럼이 두 배가 되고 수식 하나가
깨지면 값이 조용히 거짓이 됩니다. 낱말을 적으면 둘 다 없어집니다.

```json
"TrueWords":  ["예", "참", "켜짐", "O"],
"FalseWords": ["아니오", "거짓", "꺼짐", "X"],
"BuiltinBoolWords": true
```

|키|기본값|하는 일|
|--|--|--|
|`TrueWords`|`[]`|참으로 읽을 낱말|
|`FalseWords`|`[]`|거짓으로 읽을 낱말|
|`BuiltinBoolWords`|`true`|`Y`·`YES`·`TRUE`·`N`·`NO`·`FALSE`를 함께 읽을지|

- **내장 낱말에 더해집니다.** 위의 recipe는 `예`도 `TRUE`도 읽습니다. 대체가 기본이면 낱말
  하나를 더하려던 recipe가 `TRUE`를 잃고, 그것을 적어 둔 시트가 그날 오류를 냅니다.
- **적은 낱말만 읽으려면 `"BuiltinBoolWords": false`.** 시트에 정확히 무엇만 허용할지 정하려는
  프로젝트를 위한 것이고, 이미 적혀 있는 `TRUE`가 전부 오류가 되는 것을 감수합니다.
- **대소문자를 가리지 않습니다.** `O`를 적으면 `o`도 읽힙니다.
- **한쪽만 적어도 됩니다.** 빈 셀이 이미 거짓이므로, `켜짐`만 적고 나머지를 비워 두는 시트는
  할 말을 다 한 것입니다.
- **배열 대신 `;`로 이어 적어도 됩니다.** `"TrueWords": "예;참;켜짐"`.
- **숫자는 낱말이 아닙니다.** `0`이나 `1`을 적으면 오류입니다 — 「숫자는 0이 아니면 참」이
  이미 답하는 것이라, 낱말로 적으면 두 규칙이 같은 셀을 두고 갈립니다.
- **양쪽에 같은 낱말을 적으면 오류입니다.** 내장 낱말과 반대 뜻으로 겹치는 것도 같습니다 —
  `TRUE`를 `FalseWords`에 적으려면 `BuiltinBoolWords`를 먼저 꺼야 합니다.

전부 **워크북을 하나도 열기 전에** 보고합니다.

> 설계와 근거는 [불리언의 낱말](../../spec/types/boolean-words.md)에 있습니다.

### `ExcludeTags` — 이 빌드에서 뺄 행

시트의 마커 열에 적은 태그를 부르면 그 행이 이 빌드에서 빠집니다. 적는 법은
[행 태그](../sheets/layout.md#행-태그--빌드마다-빼는-행)에 있습니다.

```jsonc
"ExcludeTags": [ "wip", "stage=test" ]
```

|적은 것|무엇이 빠지나|
|--|--|
|`wip`|`wip` 태그가 붙은 행 전부|
|`stage`|`stage`가 붙은 행 전부, 값이 무엇이든|
|`stage=test`|`stage`가 `test`인 행만|

- **대소문자를 가리지 않습니다.**
- **소스별이 아니라 전역입니다.** 한 워크북에서는 빼고 다른 워크북에서는 넣는 빌드는 아무도
  요구하지 않는 빌드입니다.
- **환경마다 다르게 적으려면 `${TABBIT_ENV}`를 씁니다.** 레시피에 있으므로 빌드 캐시가
  이 값의 변화를 봅니다 — 태그를 바꾸면 다시 변환합니다.
- **빠진 행은 시트에 없던 행입니다.** 그 행을 가리키는 참조는 없는 키를 가리키는 참조로
  보고됩니다.
- 무엇이 빠졌는지는 변환 요약의 `run.rowTags`에 태그마다 적힙니다.

### `DataFileCase` — 데이터 파일 이름의 표기

`ItemDrop` 테이블을 `snake`로 두면 `item_drop.tcb`가 됩니다. 행 세트가 있으면 **표기를 적용한
뒤** 세트 이름을 원문 그대로 붙입니다 — 세트는 구분자까지 사용자가 적는 규칙이므로
`item_drop_alt.tcb`입니다.

**타깃이 아니라 전역인 이유가 있습니다.** 다른 표기 설정은 그것을 읽는 쪽의 것이지만, 이
이름은 **프로그램 사이의 계약**입니다 — 익스포터가 파일을 쓰고, 각 언어로 생성된 리더가 그
파일을 엽니다. 둘이 따로 계산하면 자기 리더가 찾을 수 없는 데이터를 내보내는 빌드가 됩니다.
레시피 하나가 정하면 그 어긋남이 불가능해집니다.

> 실제로 어긋나 있었습니다. 17곳이 각자 계산하고 있었고 C# 액세서만 시트의 원문 표기를
> 근거로 삼아서, `:table item_drop`으로 적은 테이블은 `ItemDrop.tcb`로 내보내지고
> `item_drop.tcb`를 찾았습니다. 모든 픽스처의 테이블 이름이 이미 Pascal이라 어느 게이트도
> 짚을 수 없었고, 이 옵션을 넣는 과정에서 드러났습니다.

### `Naming` — 이름의 표기 규약

시트에 적은 이름의 표기를 recipe가 선언하고 코어가 검사합니다. **섹션을 빼도 검사 두 가지는
계속 돕니다** — 규약이 무엇이든 한 이름을 두 가지로 적은 것은 잘못이기 때문입니다.

```jsonc
"Naming": {
  "Field": "camel",              // 필드 이름
  "Entity": "pascal",            // 테이블·enum·상수셋 이름
  "Label": "pascal",             // enum 라벨
  "Constant": "upper-snake",     // 상수 이름
  "OnViolation": "error",
  "Exempt": [ "Art_Path" ]      // 아직 못 고친 기존 이름
}
```

|키|기본값|무엇을 하나|
|--|--|--|
|`Field` · `Entity` · `Label` · `Constant`|`""`|각 종류가 따라야 할 표기. `pascal` · `camel` · `snake` · `upper-snake`. **비우면 그 종류는 검사하지 않습니다**|
|`OnViolation`|`error`|규약을 벗어난 이름의 무게. `error` 또는 `warn`. 검사를 끄려면 표기를 비웁니다|
|`OnSpellingConflict`|`warn`|한 이름이 여러 표기로 적힌 것의 무게. `error` · `warn` · `ignore`|
|`OnConsecutiveUnderscores`|`warn`|이름 안쪽에 `__`가 있는 것의 무게. `error` · `warn` · `ignore`|
|`Exempt`|`[]`|검사에서 빼는 이름 목록. 시트에 적힌 그대로 적습니다|

**표기 판정은 왕복입니다.** 선언한 표기로 이름을 다시 적어 보고 원본과 같으면 통과입니다.
그래서 두문자어를 판정하는 규칙과 변환하는 규칙이 어긋날 수 없습니다 — `HTTPServer`는
양쪽 모두에게 `HTTP` + `Server`입니다.

**보고는 고쳐 쓸 이름으로 끝납니다.** 무엇이 틀렸는지가 아니라 무엇을 적어야 하는지가 마지막
문장입니다.

```
Field `MaxHitPoints` of table `Item` is not spelled in `camel` case, which this
recipe declares for field names. Write it as `maxHitPoints`.

Field `a__b` of table `Item` holds two or more underscores in a row. A run of them
reads as one word boundary, so this name and `a_b` both reach the generated code as
`AB` - the difference is not carried anywhere. Write it as `a_b`.
```

**한 이름을 여러 표기로 적으면** 그 이름의 모든 표기와 위치를 한 건으로 묶어 보고하고, 어느
표기로 통일할지 함께 제시합니다. 필드는 테이블 경계를 넘어 묶습니다 — 여러 테이블이 같은
컬럼을 다르게 적는 것이 소비 코드가 가장 크게 비용을 치르는 경우이기 때문입니다.
무게는 생성 코드가 갈라지는지에 따라 다릅니다.

|`OnSpellingConflict`|생성물이 갈라지는 충돌|갈라지지 않는 충돌|
|--|--|--|
|`warn` (기본)|경고|노트|
|`error`|오류|경고|

`Item.maxHitPoints`와 `Ship.maxhitpoints`는 각각 `MaxHitPoints`와 `Maxhitpoints`로 정규화되어
생성 코드에 멤버가 두 개 생깁니다.

`my_flag`와 `myFlag`는 둘 다 `MyFlag`가 되므로 생성물은 같고 시트만 어긋난 것이라 노트입니다.

```
One field name is written 2 ways: `Id` (66 places, first at Collection.xlsx : GroupTable : A2),
`ID` (1 place, first at Master.xlsx : HotbarTable : A2). These do not normalize to one
name, so the generated code carries a separate member for each spelling and every consumer
has to know which one it is reading. Settle on `Id` and rewrite the other 1 place.
```

가리키는 셀은 **고쳐야 할 표기의 첫 자리**입니다 — 위 예에서는 `ID`를 쓴 셀입니다.

**이름 안쪽의 연속 밑줄**(`a__b`)은 따로 봅니다. 밑줄이 몇 개든 한 번의 단어 경계로 읽히므로
`a_b`와 `a__b`는 같은 이름이 되고, 그 차이는 어디에도 전달되지 않습니다. 선행·후행 밑줄
(`_reserved`)은 생성 코드에 그대로 남으므로 대상이 아닙니다.

**기존 프로젝트에 규약을 도입할 때**는 `Exempt`를 씁니다. 규약을 선언해 위반 전체를 받아
목록에 옮기고 `OnViolation`을 `error`로 두면, 그 시점부터 **새 이름만** 규약을 지켜야 합니다.
기존 이름은 계열 단위로 개명하며 목록에서 지웁니다 — 목록은 줄어드는 방향으로만 관리합니다.

> 설계 배경과 실측은 [이름 표기 규약](../../spec/targets/naming-conventions.md)에 있습니다.

## 설정 하나하나

### 모든 타깃에 공통인 것

|키|기본값|무엇인가|
|--|--|--|
|`Path`|`""`|**출력이 나갈 디렉터리.** 없으면 만듭니다. 상대 경로는 **CLI를 실행한 위치** 기준입니다 — recipe 파일 위치가 아닙니다. **비워두면 그 항목은 꺼진 것으로 취급**되어 아무것도 만들지 않습니다. recipe에서 항목을 지우지 않고 잠시 끌 때 쓰면 됩니다.|
|`TargetSide`|`"cs"`|**이 출력이 어느 쪽 빌드를 위한 것인가.** `"c"`는 클라이언트, `"s"`는 서버, `"cs"`(또는 빈 값)는 양쪽. 반대쪽으로 표시된 엔티티와 필드가 이 출력에서 빠집니다. 클라이언트 빌드에 서버 전용 테이블을 보내지 않기 위한 것입니다.|
|`Sweep`|`true`|**지난 실행의 잔재를 지울 것인가.** 시트에서 테이블을 지우면 그 파일이 남는데, 남은 파일은 없는 타입을 이름 부르므로 지저분하거나 컴파일을 깨뜨립니다. 지워지는 것은 **헤더에 `Generated by Tabbit`이 적힌 파일 중 이번 실행이 쓰지 않은 것**뿐이라, 남의 소스가 든 폴더를 가리켜도 안전합니다. 생성물을 손으로 고쳐 쓴다면 `false`로 두세요.|
|`BinaryTableFileExtension`|`".tcb"`|**생성된 테이블 리더가 찾을 데이터 파일의 확장자.** 익스포터의 `FileExtension`과 **반드시 같아야** 합니다 — 다르면 테이블 리더가 파일을 못 찾습니다. 유니티에 넣는다면 `.bytes`가 필요할 수 있습니다.|

> `Path`가 비면 꺼짐, `Sweep`은 마커가 있는 파일만, 확장자는 익스포터와 짝. 이 셋이 실제로 가장
> 많이 어긋나는 지점입니다.

### 이름과 관련된 것

#### `MemberCase` — 생성 멤버의 표기

레코드 멤버(프로퍼티·필드·게터)를 어떤 표기로 낼지 정합니다. **비우면 그 언어가 늘 쓰던
표기**이므로, 적지 않은 레시피의 산출물은 한 바이트도 달라지지 않습니다.

|값|예 — 시트의 `OpenAt`|
|--|--|
|`pascal`|`OpenAt`|
|`camel`|`openAt`|
|`snake`|`open_at`|
|`upper-snake`|`OPEN_AT`|

|언어|기본값|
|--|--|
|C#|`pascal`|
|Dart · Java · Kotlin · Lua · PHP · Swift · TypeScript|`camel`|
|C · C++ · Python · Ruby · Rust|`snake`|
|Go|**제공하지 않습니다** — 첫 글자의 대소문자가 export 여부를 정하므로, 표기를 바꾸면 소비자가 멤버를 읽을 수 없게 됩니다|
|Unreal|**제공하지 않습니다** — 멤버 이름이 표기가 아닙니다. bool UPROPERTY는 `bIsOpen`처럼 소문자 `b` + Pascal이고, 그 형태에 대응하는 snake·camel이 없습니다(`bis_open`은 어느 관례도 아닙니다). UHT가 이 선언을 읽는 것도 함께 걸립니다|

**멤버만 움직이고 나머지는 그대로입니다.** 타입 이름, 조회 메서드(`FindByIndex`),
원소 개수 상수(`Index_N`), 데이터 파일은 멤버 표기와 무관합니다 — 그것들은 멤버 이름을 한
단어로 품은 합성 이름이라, 그 단어가 대문자로 남아야 읽힙니다. 전부 함께 움직이면 표기를
정하는 것이 아니라 산출물을 개명하는 것이 됩니다.

**존재 여부 접근자는 멤버입니다.** 시트의 `OpenAt`이 옵셔널이면 `snake`에서 `has_open_at`,
`camel`에서 `hasOpenAt`이 됩니다 — 접두어를 붙인 뒤 표기하는 것이 아니라 합성한 이름을
표기하므로 네 표기 모두에서 어긋나지 않습니다.

**예약어는 그 표기에서 다시 판정됩니다.** C#은 멤버가 PascalCase인 동안 키워드와 겹칠 수
없었지만(모든 C# 키워드가 소문자입니다), `camel`로 두면 `Class` 컬럼이 `class`로 도착하므로
`@class`로 회피합니다. 표기를 바꾸는 것은 어느 이름이 위험해지는지를 바꾸는 것입니다.

---

**`AccessorName`은 모든 언어에서 「전부 담고 있는 진입점」의 이름입니다.** 기본값은 어디서나
`Tables`이고, 각 생성기가 자기 언어의 표기로 바꿔 씁니다 — **타입은 PascalCase, 파일은 그 언어의
파일 명명 관례**입니다. 그래서 `Tables`라고 한 번 적으면 C#은 `Tables.cs`의 `Tables`, Go는
`tables.go`의 `Tables`, Ruby는 `a.rb`의 `A`가 됩니다.

|키|해당 언어|무엇의 이름인가|기본값|
|--|--|--|--|
|`AccessorName`|C#, C++, Java, Kotlin, PHP, TypeScript, Go, Rust, Python, Ruby, Dart|접근자 타입(Kotlin은 `object`)과 그것이 들어갈 파일. 나머지 타입은 자기 이름의 파일로 옆에 놓입니다|`Tables`|
|`AccessorName`|C|접근자이자 **모든 타입·함수 이름의 접두사**. C에는 네임스페이스가 없어 이것이 충돌 회피의 전부입니다 — `GameData`면 `GameData_ItemRecord_t`, `GameData_ItemLoad`|`Tables`|
|`AccessorName`|Unreal|접근자 클래스와 헤더·`.cpp`의 이름. 관례상 `F`로 시작합니다|`FTables`|
|`Namespace`|C#, C++, TypeScript|생성 코드를 감쌀 네임스페이스. **비우면 전역**이라 다른 코드와 이름이 부딪힐 수 있습니다|`""`|
|`Namespace`|PHP|생성 파일이 선언할 네임스페이스|`GameData`|
|`PackageName`|Go|생성 파일이 선언할 Go 패키지|`gamedata`|
|`PackageName`|Java, Kotlin|생성 코드의 패키지. `Path` **아래에 폴더로 펼쳐집니다** (`com.a.b` → `com/a/b/`)|`gamedata`|
|`PackageName`|Python|생성 패키지의 이름이자 폴더 이름이자 `import`할 이름|`gamedata`|
|`ModuleName`|Python|접근자가 들어갈 모듈 (`tables.py`). `PackageName`과 **다르게** 두세요|`tables`|
|`AccessorModule`|Rust|없습니다 — 액세서가 들어갈 모듈은 `AccessorName`을 snake_case로 바꾼 것입니다 (`Tables` → `tables::Tables`)|—|
|`ModuleName`|Ruby|생성 타입 전부를 감쌀 모듈|`GameData`|
|`ModuleName`|Unreal|모듈 이름. 디렉터리·`Build.cs`·export 매크로의 이름이고, 다른 모듈이 의존성으로 적을 이름입니다|`TabbitData`|
|`CrateName`|Rust|`Cargo.toml`이 선언할 크레이트 이름. 소비자가 타입을 부를 때 쓰는 이름이기도 합니다|`gamedata`|
|`ModulePath`|Go|`go.mod`가 선언할 모듈 경로이자, 생성 파일이 테이블 리더를 import할 접두사. Go에는 상대 import가 없어 필요합니다|`gamedata`|

### 언어별로만 있는 것

|키|해당 언어|기본값|무엇인가|
|--|--|--|--|
|`WriteGoMod`|Go|`true`|`go.mod`를 함께 쓸 것인가. 이미 있는 모듈 안에 넣는다면 `false`|
|`GoVersion`|Go|`"1.21"`|생성되는 `go.mod`가 요구할 Go 버전|
|`WriteCargoToml`|Rust|`true`|`Cargo.toml`을 함께 쓸 것인가. 이미 있는 크레이트 안에 넣는다면 `false`|
|`Edition`|Rust|`"2021"`|생성되는 `Cargo.toml`이 선언할 edition|
|`WriteBuildFile`|Unreal|`true`|모듈의 `Build.cs`를 쓸 것인가. 의존성을 직접 관리한다면 `false`|
|`UseStringEnum`|TypeScript|`false`|enum을 숫자 대신 문자열 유니온으로. 디버거와 로그에서 읽히지만 파일에 저장된 정수와는 어긋납니다|
|`WriteUpdater`|전부|`false`|데이터 갱신기를 테이블 리더 옆에 함께 낼 것인가. CDN에서 바뀐 파일만 받아 로컬 사본을 최신으로 유지합니다. 유일하게 네트워크를 쓰는 생성물이라 기본값이 `false`이고, **의존성이 생기는 유일한 자리**이기도 합니다 — 언리얼은 `Build.cs`에 `HTTP` 모듈이, Rust는 `Cargo.toml`에 `ureq`가 함께 들어갑니다. 나머지 언어는 표준 라이브러리만 씁니다. 「[C#](../languages/csharp.md#데이터만-갱신하기-writeupdater)」·「[언리얼](../languages/unreal.md#데이터만-갱신하기-writeupdater)」·「[Rust](../languages/rust.md#데이터만-갱신하기-writeupdater)」·「[Ruby](../languages/ruby.md#데이터만-갱신하기-writeupdater)」|

### 내보내기

|키|해당|기본값|무엇인가|
|--|--|--|--|
|`FileExtension`|Binary|`".tcb"`|각 테이블 파일의 확장자. 코드 생성 쪽 `BinaryTableFileExtension`과 짝을 맞추세요|
|`Compress`|Binary|`false`|**예약. 구현되어 있지 않습니다.** 형식이 압축 플래그 자리를 비워두고 있을 뿐, 아무것도 읽거나 쓰지 않습니다|
|`SchemaBaseline`|Binary|`""`|지난 스키마의 기록을 둘 경로. **커밋하세요.** 매 실행이 스키마를 그것과 비교해서, 이미 배포된 테이블 리더가 읽지 못할 변경이면 **아무것도 쓰기 전에** 컬럼 이름과 함께 멈춥니다. 비워두면 검사하지 않습니다|
|`AcceptSchemaChanges`|Binary|`[]`|의도한 변경을 `"테이블.컬럼"`으로 승인. 타입 변경은 재생성된 코드와 함께 나가야 하므로 자동 통과가 아닙니다. 한 번 통과하면 베이스라인이 갱신되니 다시 지워도 됩니다|
|`EncodingReport`|Binary|`""`|컬럼마다 무엇을 측정해 그 인코딩을 골랐는지 적을 경로. 후보별 크기는 추정이 아니라 선택이 근거한 실측입니다. 형식에 **없는** 레이아웃까지 재느라 큰 익스포트에서는 시간이 들므로, 경로를 적었을 때만 합니다. 「[내보내기](../exports/binary.md#바이너리-익스포트의-recipe-옵션)」|
|`EncryptionKeyVariable`|Binary|`""`|암호화 키가 든 **환경 변수의 이름**. 키가 아니라 이름입니다 — recipe는 커밋되는 파일입니다. 키는 64자리 16진수이고 `tabbit --new-encryption-key`가 만듭니다. 비워두면 파일은 평문입니다|
|`EncryptionKeyFile`|Binary|`""`|암호화 키가 든 **파일의 경로**. `EncryptionKeyVariable`의 대안이고, **둘을 함께 적으면 거부합니다.** 키가 없거나 형식이 틀리면 첫 테이블을 쓰기 전에 멈춥니다|
|`MacKeyVariable`|Binary|`""`|MAC 키가 든 **환경 변수의 이름**. 켜면 파일이 변조되었는지 리더가 검출합니다 — 암호화만으로는 검출되지 않습니다([근거](../binary-format/security.md#변조-검출--mac)). 암호화 키와 **다른 값**이어야 하고, 형식은 같으므로 같은 명령으로 만듭니다. 비워두면 `mac` 필드가 0으로 남습니다|
|`MacKeyFile`|Binary|`""`|MAC 키가 든 **파일의 경로**. `MacKeyVariable`의 대안이고, **둘을 함께 적으면 거부합니다.** 켜는 순서는 데이터가 먼저, 클라이언트의 키가 나중입니다|
|`UseCompactRowFormat`|Json|`false`|각 행을 필드 이름 있는 객체 대신 **값만 담은 배열**로. 작아지지만 사람이 보기 어렵습니다|
|`Indented`|Json|`false`|들여쓰기. 사람이 들여다볼 때만 켜세요|
|`ConnectionString`|DB 4종|`""`|연결 문자열. **`${NAME}`으로 환경 변수를 채웁니다** — 비밀번호를 recipe에 적지 마세요. 변수가 없으면 오류이고 어느 변수인지 출력합니다. recipe의 다른 설정과 달리 **이 타깃이 실행될 때** 해석됩니다([위](#환경-변수--name))|
|`NamePrefix`|DB 4종|`""`|기록되는 모든 테이블·컬렉션·키 이름의 접두사. 데이터베이스 하나에 독립된 데이터 세트를 여럿 둘 때|

### 기록

|키|해당|기본값|무엇인가|
|--|--|--|--|
|`FileName`|Summary|`"summary.json"`|문서의 파일 이름|
|`Author`|Summary|`"full"`|파일에 커밋 작성자를 얼마나 싣는가. `full`은 이름·이메일을 커밋 그대로, `masked`는 각각 첫 글자만 남기고(`서*`, `m*@gmail.com`), `none`은 두 필드를 뺍니다. summary는 산출물 옆에 커밋되거나 다른 팀에 전달되기 쉬운 파일이라, 개인정보를 내보내고 싶지 않으면 낮추세요. 히스토리에는 영향이 없습니다 — 귀속이 목적인 기록이라 전체 작성자를 유지합니다|
|`ConnectionString`|History|`""`|히스토리가 저장되는 곳. `${NAME}` 지원|
|`ProjectKey`|History|`""`|어느 프로젝트의 히스토리인가. 데이터베이스 하나가 여럿을 담을 수 있고, **이 값을 바꾸면 이어지는 게 아니라 새 히스토리가 시작됩니다**|
|`RecordDirty`|History|`false`|커밋되지 않은 변경이 있는 워킹카피의 변환도 기록할 것인가. 꺼져 있는 이유는 그런 변환이 어느 커밋에도 없는 작업을 담고 있기 때문입니다|
|`AllowOutOfOrder`|History|`false`|브랜치가 이미 도달한 것보다 오래된 커밋도 기록할 것인가|
|`OnFailure`|History|`"warn"`|히스토리에 닿지 못했을 때 `warn`할지 `fail`할지. 기본이 `warn`인 이유는 빌드의 본업이 게임 데이터를 만드는 것이고, 기록용 데이터베이스가 잠깐 안 된다고 그것을 멈출 이유가 없기 때문입니다|


---

## 환경 변수 — `${NAME}`

> **혼자 쓴다면 이 절은 건너뛰어도 됩니다.** 경로를 그대로 적은 recipe는 아무것도 달라지지
> 않습니다 — `${NAME}`을 쓰지 않으면 치환도 일어나지 않고, 설정할 변수도 없습니다.
> 이 절은 **같은 recipe를 여러 사람이 여러 환경에서 돌릴 때** 필요해지는 것입니다.

**recipe의 어느 문자열에서나 `${NAME}`이 환경 변수로 채워집니다.** recipe는 커밋되는 파일이므로
기계마다 달라지는 것은 적을 수 없고, 그것은 비밀번호만이 아닙니다 — 어느 문서를 읽는지,
어디로 내보내는지가 환경을 가르는 설정입니다.

```jsonc
{
  "Sources": { "Xlsx": [ { "Path": "./sheets/${TABBIT_ENV}" } ] },
  "Targets": [ { "Type": "binary", "Path": "./build/${TABBIT_ENV}/data" } ]
}
```

**recipe 하나와 변수 두 벌이 환경 두 개를 나타냅니다.** 환경마다 recipe 파일을 따로 두면
`Targets` 목록 전체가 두 파일에 중복되고, 한쪽만 수정된 상태가 생깁니다.

- **변수가 없으면 오류입니다.** 빈 값으로 치환하면 그 실패가 적힌 자리가 아니라 「폴더가
  없습니다」나 「테이블이 0개입니다」로 나중에 나타납니다.
- **없는 변수는 전부 모아 한 번에 보고합니다**, 각각 recipe 안의 어느 자리인지와 함께.
  기계를 새로 세팅하는 사람은 변수를 전부 설정해야 하고, 하나씩 알려주면 변수 수만큼 실행하게
  됩니다.
- **`TABBIT_ENV`는 `--env`가 채웁니다.** 그 낱말 하나가 경로를 정하는 동시에 summary에 기록되므로,
  「`live`라고 적혀 있는데 개발 시트로 만든 산출물」이 나올 수 없습니다
  ([CLI](../cli.md#--env--이-실행이-어느-환경의-것인가)).
- 치환은 **값**에만 적용됩니다. 키 이름은 그대로입니다.
- 값에 따옴표나 역슬래시가 들어 있어도 됩니다. 치환이 텍스트가 아니라 **파싱된 문서**에
  적용되기 때문입니다.

> **연결 문자열은 예외입니다.** `ConnectionString`과 `Validation.Connections`의 값은 그 타깃이
> **실제로 실행될 때** 해석됩니다. recipe에 있지만 이번에 돌리지 않는 데이터베이스 타깃 때문에
> 검증만 하는 실행이 멈추지 않게 하기 위한 것입니다 — 라이브 DB로도 내보내는 recipe를
> `--validate-only`로 검사하는 사람은 그 비밀번호를 갖고 있지 않은 것이 정상입니다.
