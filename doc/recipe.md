# Recipe 파일

무엇을 어디서 읽어 어디로 내보낼지 적는 파일.

> [문서 목록으로](readme.md)

---

## Recipe 파일 작성

`recipe` 파일은 입력 소스와 출력 대상을 지정하는 `.json` 파일입니다. `//` 주석을 사용할 수 있습니다.

`tabbit --new-recipe myrecipe.json` 으로 시작용 recipe를 만들 수 있습니다. 모든 목록에 기본값이 채워진 항목 하나가 들어 있고, 파일 앞부분에 사용 가능한 소스/타깃 이름이 적혀 나옵니다. 그대로 실행해도 아무것도 만들지 않고 정상 종료합니다 — 경로가 비어 있으면 꺼진 것으로 취급되기 때문입니다.

### 환경 변수 — `${NAME}`

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
  ([CLI](cli.md#env--이-실행이-어느-환경의-것인가)).
- 치환은 **값**에만 적용됩니다. 키 이름은 그대로입니다.
- 값에 따옴표나 역슬래시가 들어 있어도 됩니다. 치환이 텍스트가 아니라 **파싱된 문서**에
  적용되기 때문입니다.

> **연결 문자열은 예외입니다.** `ConnectionString`과 `Validation.Connections`의 값은 그 타깃이
> **실제로 실행될 때** 해석됩니다. recipe에 있지만 이번에 돌리지 않는 데이터베이스 타깃 때문에
> 검증만 하는 실행이 멈추지 않게 하기 위한 것입니다 — 라이브 DB로도 내보내는 recipe를
> `--validate-only`로 검사하는 사람은 그 비밀번호를 갖고 있지 않은 것이 정상입니다.

### 공통 설정

|키|기본값|설명|
|--|--|--|
|`ArrayDelimiter`|`";"`|배열 셀의 요소 구분자. 정확히 한 글자여야 합니다.|

### `Sources` — 무엇을 읽을지

읽을 곳은 두 가지이고, 여러 개를 함께 둘 수 있습니다. 전부 합쳐서 하나의 모델이 됩니다.

```jsonc
"Sources": {
  "Xlsx": [
    { "Path": "./sheets", "FileExtensionPatterns": ".xls;.xlsx" }
  ],
  "GoogleSheets": [
    { "ClientSecretFilename": "./client-secret.json", "SheetsId": "10NXZ..." }
  ]
}
```

|키|어디에|기본값|설명|
|--|--|--|--|
|`Path`|Xlsx|—|워크북을 찾을 폴더. 하위 폴더까지 봅니다. 이름이 `#`으로 시작하는 파일·폴더는 건너뜁니다.|
|`FileExtensionPatterns`|Xlsx|`.xls;.xlsx`|주워올 확장자. `;`로 구분합니다.|
|`ClientSecretFilename`|GoogleSheets|—|OAuth 클라이언트 비밀 파일 경로. **커밋하지 마세요.** [아래](#구글-시트에-무엇으로-접속하는가) 참고.|
|`ServiceAccountKeyFile`|GoogleSheets|—|서비스 계정 키 파일 경로. **커밋하지 마세요.**|
|`ServiceAccountKeyVariable`|GoogleSheets|—|서비스 계정 키가 든 **환경 변수의 이름**. 키가 아니라 이름입니다.|
|`SheetsId`|GoogleSheets|—|워크북(스프레드시트 문서) URL에 들어 있는 긴 식별자.|

#### 구글 시트에 무엇으로 접속하는가

**두 가지이고, 무엇을 고르는지는 누가 변환을 돌리는지에서 결정됩니다.**

|설정|누구로 접속하나|어디에 맞나|
|--|--|--|
|`ClientSecretFilename`|**변환을 돌리는 사람**|개발자의 기계. 첫 실행이 브라우저로 동의를 받고 그 계정의 프로필 아래에 토큰을 캐시하므로, 두 번째 실행부터는 대화형이 아닙니다|
|`ServiceAccountKeyFile` · `ServiceAccountKeyVariable`|**그 잡 자신**|빌드 서버. 대화형 단계가 없고, 문서를 사람에게 공유하듯 서비스 계정의 주소에 공유합니다|

**CI에 클라이언트 비밀을 쓰면 파이프라인의 문서 접근 권한이 한 사람의 계정에 종속됩니다.**
그 사람이 조직을 떠나거나 권한이 회수되면 빌드가 중단되고, 그 잡이 읽는 모든 것은 그
사람으로 읽힙니다. 서비스 계정은 그것을 잡의 신원으로 바꿉니다.

```jsonc
// 개발자의 기계
{ "ClientSecretFilename": "./secrets/googlesheets-client-secret.json", "SheetsId": "10NXZ..." }

// CI — 키는 시크릿 저장소에 두고 이름만 적습니다
{ "ServiceAccountKeyVariable": "TABBIT_SHEETS_KEY", "SheetsId": "10NXZ..." }
```

- **둘을 함께 적으면 거절합니다.** 서로 다른 신원이므로 하나를 말없이 고르면 그 잡이 자기가
  아닌 사람으로 문서를 읽게 되고, 산출물의 어디에도 그 사실이 남지 않습니다.
  `ServiceAccountKeyFile`과 `ServiceAccountKeyVariable`을 함께 적는 것도 같습니다.
- **키 파일 자리에 클라이언트 비밀을 적으면 그 자리에서 거절합니다.** 구글이 내려주는 두 JSON은
  서로 바꿔 넣기 쉬운데, 그대로 API에 보내면 권한 오류로 돌아와 문서 공유 문제처럼 읽힙니다.
- 서비스 계정에는 그 문서의 **뷰어** 권한만 있으면 됩니다. 키에 적힌 `client_email`이 공유할
  주소입니다.
- 셋 중 아무것도 적지 않은 항목은 `SheetsId`가 빈 것과 같게 **꺼진 것으로 취급**됩니다.

아래는 **두 소스 모두** 같습니다.

|키|기본값|설명|
|--|--|--|
|`Layout`|`"tabbit"`|시트를 읽는 방식. [아래](#layout--시트를-읽는-방식) 참고.|
|`IncludeWorkbooks`|`[]`(전부)|읽을 워크북. 배열 또는 `;`로 이은 문자열. `*` `?` 와일드카드.|
|`ExcludeWorkbooks`|`[]`|제외할 워크북. `IncludeWorkbooks` 다음에 적용됩니다.|
|`IncludeSheets`|`[]`(전부)|읽을 시트 이름. `[워크북]시트`로 워크북을 지정할 수 있습니다.|
|`ExcludeSheets`|`[]`|제외할 시트. `IncludeSheets` 다음에 적용됩니다.|
|`ArrayDelimiter`|(전체 설정)|이 항목의 배열 셀 구분자. **적으면 recipe 전체 설정보다 우선합니다.**|
|`OnDuplicateIndex`|`"error"`|인덱스 값이 겹칠 때. 겹치는 것을 허용하는 레이아웃에서만 동작합니다.|
|`OnFormulaError`|`"error"`|`#REF!` 같은 수식 오류 셀을 만났을 때. [아래](#onformulaerror--수식이-오류일-때) 참고.|
|`FoldSerialFields`|`false`|`Text1`/`Text2`를 배열 하나로 접습니다. [아래](#foldserialfields--연번-컬럼을-접기) 참고.|
|`TrimTrailingArrayElements`|`false`|배열에서 값이 없는 뒤쪽 원소를 버립니다. [아래](#trimtrailingarrayelements--배열의-빈-꼬리-자르기) 참고.|
|`LayoutOptions`|`{}`|그 레이아웃만 아는 설정. [아래](#layoutoptions--레이아웃-전용-설정) 참고.|

#### `Layout` — 시트를 읽는 방식

셀 격자를 **어떻게 해석할지**를 고르는 설정입니다. 어디서 읽어오는지(엑셀이냐 구글시트냐)와는 무관하므로, 두 소스 모두에서 쓸 수 있습니다.

|값|무엇인가|
|--|--|
|`tabbit`|**기본값.** `~~table:Item~~` 같은 마커로 엔티티를 선언합니다. 한 시트에 여러 개를 아무 데나 놓을 수 있습니다.|
|`rescue`|마커 없이 **시트 탭 하나가 곧 테이블 하나**이고 머리 3줄이 헤더인 형태. 다른 규칙으로 작성된 시트를 그대로 읽기 위한 것입니다.|

**소스 항목마다 따로 지정하므로 한 번에 섞어 읽을 수 있습니다.** 한쪽 워크북의 테이블이 다른 쪽에서 선언한 enum을 타입으로 써도 됩니다.

```jsonc
"Xlsx": [
  { "Path": "./sheets",       "Layout": "tabbit" },
  { "Path": "./other-sheets", "Layout": "rescue"   }
]
```

`rescue`가 시트를 어떻게 읽는지와 실제로 적용한 기록은 [다른 규칙으로 쓰인 시트 읽기](../samples/rescue/doc/적용-기록.md)에 있습니다.

#### 읽을 워크북과 시트 골라내기

**아무것도 적지 않으면 `Path` 아래의 워크북 전부, 그 안의 시트 전부입니다.** 골라내는 것은 두 단계이고, 워크북이 먼저입니다 — 제외된 워크북은 열지도 않습니다.

|키|무엇을 고르나|
|--|--|
|`IncludeWorkbooks` / `ExcludeWorkbooks`|워크북 자체|
|`IncludeSheets` / `ExcludeSheets`|시트. `[워크북]시트`로 특정 워크북에만 적용할 수 있습니다|

```jsonc
{
  "Path": "./xls",

  // 폴더에 입력이 아닌 파일이 섞여 있을 때. 보통 쓰게 되는 것은 제외 쪽입니다 —
  // 입력인 워크북을 전부 적는 것보다 짧고, 하나 늘어날 때마다 손볼 필요가 없습니다.
  "ExcludeWorkbooks": ["백업/*", "*_참고용*"],

  // 모든 워크북에 적용
  "ExcludeSheets": [
    "*_메모",

    // 그 워크북에만 적용. 양쪽 다 글롭이므로 한 줄로 여러 워크북을 덮을 수 있습니다.
    "[UWO_테이블.xlsb]Define",
    "[UWO_테이블.xlsb]List*Shape",
    "[UWO_*.xlsb]RewardPath"
  ]
}
```

목록이 길면 배열이, 하나면 문자열이 읽기 좋습니다. `*` `?`는 파일 글롭과 같고 대소문자는 구분하지 않습니다.

**워크북 이름은 세 가지로 적을 수 있습니다** — `Path` 기준 상대 경로(`shared/Items.xlsx`), 파일명(`Items.xlsx`), 확장자를 뗀 이름(`Items`). 3개 다 같은 워크북을 가리키므로 `백업/*`은 폴더 하나를, `*.xlsb`는 형식 하나를 지정합니다. 경로로 적으면 그 경로만이고, 파일명으로 적으면 하위 폴더에 있는 같은 이름도 걸립니다.

**시트 이름에 워크북을 붙일 수 있는 이유는 시트 이름이 워크북마다 겹치기 때문입니다.** 한쪽의 `Define`은 테이블이고 다른 쪽의 `Define`은 작업용 탭인 상황을 시트 이름만으로는 구분할 수 없고, 구분하지 못하면 둘 다 빠집니다. 대괄호를 쓰는 것은 시트 이름에 `!`이나 `.`은 들어갈 수 있지만 엑셀이 `[` `]`는 금지하기 때문입니다 — 이름의 일부로 오해될 여지가 없습니다.

**`IncludeWorkbooks`·`IncludeSheets`에 적었는데 없는 것은 오류입니다** — 적어놓고 표시 없이 빠지면 산출물에서 테이블 하나가 사라진 걸 아무도 모르기 때문입니다. 오류 메시지는 실제로 있는 목록을 같이 보여주고, 워크북을 지정한 패턴이 아무것도 못 맞혔을 때는 각 시트가 어느 워크북에 있었는지와 **이 항목이 건너뛴 워크북**까지 적습니다 — 시트를 못 찾은 이유가 그 워크북을 제외해 둔 것일 때가 많기 때문입니다.

> `GoogleSheets` 소스는 문서 하나를 가리키므로 워크북 목록은 **문서 제목**과 맞춰봅니다. `[제목]시트`도 같게 동작합니다.

#### `OnDuplicateIndex` — 인덱스가 겹칠 때

|값|무엇을 하나|
|--|--|
|`error`|**기본값.** 겹친 값을 전부 모아 보고하고 멈춥니다. 인덱스가 존재하는 이유 자체입니다.|
|`keep-first`|먼저 나온 행을 남기고 뒤를 버립니다.|
|`keep-last`|나중 행이 앞을 덮어씁니다.|

뒤의 둘은 원본을 고칠 수 없는 동안 나머지를 변환하기 위한 것이고, 인덱스가 겹치는 것을 허용하는 레이아웃에서만 동작합니다. 버린 행은 전부 로그에 남습니다.

#### `OnFormulaError` — 수식이 오류일 때

|값|무엇을 하나|
|--|--|
|`error`|**기본값.** 셀 위치와 함께 멈춥니다.|
|`empty`|빈 값으로 읽고, 셀마다 경고하고, 끝에 총계를 냅니다.|

`empty`는 **우리가 관리하지 않는 워크북**을 위한 것입니다. 아무도 읽지 않는 컬럼의 깨진 수식 하나 때문에 그 파일의 테이블 전부를 거부하지 않기 위한 것이고, 우리 워크북이라면 기본값이 맞습니다 — 수식 오류는 고칠 수 있는 문제입니다.

#### `FoldSerialFields` — 연번 컬럼을 접기

`Text1`·`Text2`를 `Text_array` 하나로 접습니다. 기본은 **끔**입니다.

**이름의 숫자가 배열을 뜻하는지는 이름으로는 판정할 수 없는 문제이기 때문입니다.** `Text1`/`Text2`는 대개 길이 2인 배열이지만, 실제 프로젝트의 `Condition_1`·`Condition_2`·`Condition_3`은 서로 다른 enum 셋이었습니다. 접으면 더 나은 API가 아니라 **틀린 API**가 되고, 틀려도 아무 신호가 없습니다 — 세 필드가 하나가 되고 **시트에 없던 이름**이 붙습니다.

연번 규칙이 없는 시트를 읽는 레이아웃은 이 설정을 **아예 읽지 않습니다.** 거기서는 숫자가 이름의 일부일 뿐입니다. 자세한 것은 [시트 작성](sheets.md#serial-field--옵트인)에 있습니다.

#### `TrimTrailingArrayElements` — 배열의 빈 꼬리 자르기

`Slot1.*`·`Slot2.*`·`Slot3.*`에서 셋째를 비운 로우가 **길이 2**를 냅니다. 기본은 **끔**입니다.

**레코드 배열과 스칼라 배열 둘 다입니다.** `Tag1`·`Tag2`·`Tag3`도 같은 규칙으로 잘립니다 —
자르기는 「원소가 어디서 끝나는가」에 대한 답이고, 그 질문에 원소의 생김새는 상관이 없습니다.

> 이 키는 `TrimTrailingRecordElements`였습니다. 레코드 배열에만 걸리던 때의 이름이고,
> 릴리즈된 적이 없어 이름을 맞췄습니다.

고정 길이는 채우지 않은 로우를 빈 값으로 메우고, 그 메움은 값과 구별되지 않습니다 — `{Id:0, Count:0}`이 「0개를 주는 슬롯」인지 「슬롯이 없음」인지 알 수 없습니다. 자르면 데이터로 구별됩니다.

**가운데는 지우지 않습니다.** 뒤에서만 잘라야 인덱스 `k`가 언제나 `Slot{k+1}`입니다. 「값이 없다」는 타입에 `?`가 붙은 컬럼의 빈 칸이고, `0`을 적은 셀은 값입니다. 규칙 전체는 [시트 작성](sheets.md#로우마다-길이가-다른-레코드-배열--옵트인)에 있습니다.

**배열의 컬럼들이 필수 여부를 다르게 적어도 됩니다.** **첫 원소의 표시가 배열 전체의 표시**이고, 뒤 컬럼의 표시는 보지 않습니다 — 타입을 첫 원소에서 가져오는 것과 같습니다. [설계](../spec/array-optionality.md)

#### `LayoutOptions` — 레이아웃 전용 설정

어떤 레이아웃만 아는 설정을 자유 키/값으로 넘깁니다. **코어는 키를 모르고 검증하지 않습니다** — 레이아웃이 읽고, 인식하지 못하는 키는 레이아웃이 그 이름과 함께 보고합니다.

```jsonc
{
  "Path": "./other-sheets",
  "Layout": "some-layout",
  "LayoutOptions": { "NumberType": "narrow" }
}
```

모든 레이아웃에 해당하는 설정은 위의 정식 키로 들어갑니다. 특정 프로젝트의 설정이 이름째로 recipe 스키마에 들어가면 그 레이아웃을 지울 때 스키마와 문서에 흔적이 남기 때문입니다 — [설계 원칙](architecture.md#설계-원칙--코어에-프로젝트-이름-금지).

### `Targets` — 이 변환이 내는 것 전부

내보내기든 코드 생성이든 기록이든, 출력은 전부 `Targets`의 항목 하나입니다. `Type`이 타깃을 지목하고 나머지가 그 타깃의 설정입니다.

```json
"Targets": [
  { "Type": "binary", "Path": "./out/data", "FileExtension": ".tcb" },
  { "Type": "csharp", "Path": "./out/cs", "Namespace": "MyGame.Data", "AccessorName": "GameData" }
]
```

|`Type`|종류|
|--|--|
|`binary`, `json`|파일 내보내기|
|`text`|`text` 컬럼의 값을 그룹마다 파일 하나로 수집 — 「[수집된 텍스트](exports.md#수집된-텍스트--text-타깃)」|
|`mysql`, `postgresql`, `mongodb`, `redis`|데이터베이스 내보내기|
|`cpp`, `csharp`, `typescript`, `html`, `c`, `go`, `rust`, `python`, `java`, `kotlin`, `ruby`, `php`, `dart`|코드 생성 — 설정은 [언어별 가이드](languages/readme.md)|
|`unreal`|Unreal 모듈 생성|
|`summary`, `history`|변환 자체를 기록 — 「[Summary와 히스토리](history.md)」|

- 없는 `Type`은 **오류**입니다. 출력을 요청했는데 말없이 아무것도 안 나오면, 있어야 할 파일이 빠진 채 빌드가 나갑니다.
- 그 타깃에 없는 필드도 **오류**입니다. `FileExtention`처럼 오타를 내면 기본값으로 그냥 넘어가고, 증상은 "설정이 안 먹는다"로만 보입니다.

타깃마다 전용 섹션을 두지 않는 것은 타깃을 추가할 때 recipe 스키마를 고치지 않아도 되게 하기 위함입니다. **타깃 하나를 지우는 일이 파일 하나를 지우는 일**이어야 하기 때문이기도 합니다.

> 예전에는 10개 타깃이 `Exports`·`CodeGenerations` 아래에 전용 섹션을 갖고 나머지는 `Targets`에 있었습니다. 그 10개를 가르는 것은 기능이 아니라 도입 시점이었고, recipe를 읽는 사람이 그 배치에서 읽어낼 수 있는 규칙은 없었습니다.

#### 출력 항목 공통 설정

|키|기본값|설명|
|--|--|--|
|`TargetSide`|`"cs"`|이 출력이 어느 쪽을 위한 것인지. `"c"`(클라), `"s"`(서버), `"cs"`(양쪽). 반대쪽으로 지정된 엔티티와 필드가 제외됩니다.|

> 익스포터와 그 파일을 읽는 코드 제너레이터는 **같은 `TargetSide`로 맞춰야** 합니다. 컬럼 집합이 어긋나면 생성된 테이블 리더가 데이터와 맞지 않습니다.

서버/클라 각각을 뽑으려면 항목을 두 개 두고 각기 다른 `TargetSide`와 경로를 지정하면 됩니다.

### `Assets` — 애셋 폴더

[`asset` 타입](sheets.md#asset--파일이-있어야-하는-문자열) 컬럼의 값을 어느 폴더에서 찾을지.
**이 섹션이 없으면 검사가 꺼집니다.**

```jsonc
"Assets": {
  "Roots": [
    { "Kind": "icon", "Path": "./content/ui/icon", "Pattern": "*.uasset" },
    { "Kind": "sfx",  "Path": "./content/audio",   "Pattern": "*.uasset" },

    // 같은 종류를 여러 폴더에 둘 수 있습니다. 어느 하나에 있으면 통과입니다
    { "Kind": "icon", "Path": "./content/dlc/icon", "Pattern": "*.uasset" }
  ],
  "OnMissing": "warn"
}
```

|설정|기본값|무엇|
|--|--|--|
|`Kind`|`""`|`asset(icon)`의 괄호 안. 비우면 종류를 안 적은 컬럼의 폴더입니다. 대소문자 구분 없음|
|`Path`|—|하위 폴더까지 훑습니다. **없는 폴더는 오류**입니다 — 거기서 찾는 값이 전부 「없음」으로 보고되기 때문입니다|
|`Pattern`|`*`|**좁히는 편이 낫습니다.** 전부 훑으면 애셋 폴더에 있는 메모 파일까지 맞아버려서, 통과해도 아무 의미가 없습니다|
|`OnMissing`|`warn`|`warn` · `error` · `ignore`|

`OnMissing`이 `warn`인 것과 `Validation.TreatWarningsAsErrors`의 조합이 이 기능의 요점입니다 —
자세한 것은 [시트 작성](sheets.md#없는-파일--기본은-경고)에 있습니다.

- **폴더 훑기는 루트당 한 번**입니다. 셀마다 묻지 않습니다.
- 확장자와 대소문자는 무시하고 이름만 봅니다. 시트가 `Ship_Galleon`이라고 적기 때문입니다.
- 같은 이름의 파일이 여러 폴더에 있으면 **먼저 찾은 것**입니다. 어느 쪽인지가 문제라면 그건 프로젝트가 정할 일입니다.

### `Validation` — 시트에 적을 수 없는 규칙

시트에 적을 수 있는 제약(필수·범위·허용값·참조)은 이미 변환 단계에서 검사합니다. 그것으로 표현할 수 없는 규칙은 폴더 하나에 `.cs` 파일로 적습니다 — 자세한 것은 「[검증](validation.md)」에 있습니다.

```jsonc
"Validation": {
  // pre/ tables/ global/ runtime/ shared/ 로 된 폴더. 비우면 검증이 꺼집니다.
  "Path": "./validation",

  // 규칙만 읽는 자유 키/값. 코어는 키를 모릅니다.
  "Options": {
    "Locale": "KR",
    "ContentRoot": "../game/content"
  },

  // runtime/ 규칙이 이름으로 여는 연결. 스킴이 종류를 나타내고, ${NAME}은 환경 변수에서.
  "Connections": {
    "Live": "mysql:Server=db;Database=game;Uid=ro_validator;Pwd=${DB_PASSWORD}",
    "Cache": "redis://cache:6379/0"
  },

  // 편집기가 `Tables`를 해석할 프로젝트를 생성할지. 기본은 켜짐입니다 — 「검증」 §17.
  "EmitIdeProject": true,

  // 경고를 오류로 취급할지. CI에서 켜는 용도이고, Info는 이것으로도 승격되지 않습니다.
  "TreatWarningsAsErrors": false
}
```

|설정|무엇|
|--|--|
|`Path`|규칙 폴더. **비우는 것이 검증을 끄는 유일한 방법**이고, 그것은 diff에 남습니다. 지정했는데 폴더가 없으면 오류입니다 — 오타 하나로 검증 전체가 그냥 통과하지 않도록|
|`Options`|규칙만 읽는 자유 키/값. 로케일·콘텐츠 경로처럼 **코어가 몰라야 하는 것**이 지나가는 자리입니다|
|`Connections`|`rules/runtime/` 규칙이 여는 읽기 전용 연결. `mysql:` · `postgres:` · `redis://` 중 하나로 시작해야 합니다 — ADO 연결 문자열과 Redis 설정 문자열은 모양으로 구별되지 않아 추측하지 않습니다|
|`EmitIdeProject`|편집기가 `Tables`를 해석할 프로젝트를 검증 폴더 루트에 씁니다. **기본은 켜짐** — 액세서 소스는 어차피 `.generated/`에 쓰이고, 이 파일은 그것을 편집기가 읽을 수 있게 하는 것뿐입니다. 프레임워크보다 오래된 Visual Studio는 이 프로젝트를 열지 못하므로 그때 끕니다 (「[검증](validation.md)」 §17)|
|`TreatWarningsAsErrors`|경고를 오류로. 기본은 꺼짐이고 CI에서 켭니다|

검증은 **모든 타깃보다 앞에서** 돌고, 실패하면 파일에도 데이터베이스에도 흔적이 남지 않습니다.

---

## 시작점 고르기

백지에서 시작할 필요가 없습니다. `--template`이 상황에 맞는 recipe를 내놓고, **설정마다 무엇을 위한 것이고 언제 바꾸는지 주석이 붙어 있습니다.**

```
tabbit --new-recipe my-recipe.json --template unity
```

|템플릿|무엇을 위한 것|들어 있는 것|
|--|--|--|
|`unity`|유니티 클라이언트|엑셀 → StreamingAssets(`.bytes`) + C# + HTML 문서|
|`client-server`|같은 시트에서 두 벌|`TargetSide`로 가른 바이너리 두 개, C#(클라)과 Go(서버)|
|`web`|브라우저|구글 스프레드시트 → JSON + 바이너리 + TypeScript + HTML|
|`server`|게임 서버|바이너리 + MySQL 적재 + C++|
|`unreal`|언리얼|바이너리 + 모듈 하나. 패키징 주의사항이 주석에 있습니다|
|`ci`|빌드 파이프라인|바이너리 + summary + 셀 단위 히스토리|

`--template`을 **생략하면** 모든 섹션이 기본값 항목 하나씩을 담은 파일이 나옵니다. 무엇을 쓸 수 있는지 훑어보기에는 그쪽이 낫습니다 — 다만 마흔 개의 기본값이 늘어선 파일도 그 나름의 백지라, 실제로 시작할 때는 템플릿 쪽이 빠릅니다.

> 템플릿은 회귀 스위트가 **실제로 변환해봅니다.** 설정 이름이 바뀌면 변환이 거부하므로, 낡은 템플릿은 테스트가 깨져서 드러납니다.

---

## 설정 하나하나

### 모든 타깃에 공통인 것

|키|기본값|무엇인가|
|--|--|--|
|`Path`|`""`|**출력이 나갈 디렉터리.** 없으면 만듭니다. 상대 경로는 **CLI를 실행한 위치** 기준입니다 — recipe 파일 위치가 아닙니다. **비워두면 그 항목은 꺼진 것으로 취급**되어 아무것도 만들지 않습니다. recipe에서 항목을 지우지 않고 잠시 끌 때 쓰면 됩니다.|
|`TargetSide`|`"cs"`|**이 출력이 어느 쪽 빌드를 위한 것인가.** `"c"`는 클라이언트, `"s"`는 서버, `"cs"`(또는 빈 값)는 양쪽. 반대쪽으로 표시된 엔티티와 필드가 이 출력에서 빠집니다. 클라이언트 빌드에 서버 전용 테이블을 보내지 않기 위한 것입니다.|
|`Sweep`|`true`|**지난 실행의 잔재를 지울 것인가.** 시트에서 테이블을 지우면 그 파일이 남는데, 남은 파일은 없는 타입을 이름 부르므로 지저분하거나 컴파일을 깨뜨립니다. 지워지는 것은 **헤더에 `Generated by Tabbit`이 적힌 파일 중 이번 실행이 쓰지 않은 것**뿐이라, 남의 소스가 든 폴더를 가리켜도 안전합니다. 생성물을 손으로 고쳐 쓴다면 `false`로 두세요.|
|`BinaryTableFileExtension`|`".tcb"`|**생성된 테이블 리더가 찾을 데이터 파일의 확장자.** 익스포터의 `FileExtension`과 **반드시 같아야** 합니다 — 다르면 테이블 리더가 파일을 못 찾습니다. 유니티에 넣는다면 `.bytes`가 필요할 수 있습니다.|

> `Path`가 비면 꺼짐, `Sweep`은 마커가 있는 파일만, 확장자는 익스포터와 짝. 이 셋이 실제로 가장 많이 어긋나는 지점입니다.

### 이름과 관련된 것

**`AccessorName`은 13개 언어 전부에서 「전부 담고 있는 진입점」의 이름입니다.** 기본값은 어디서나
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
|`WriteUpdater`|전부|`false`|데이터 갱신기를 테이블 리더 옆에 함께 낼 것인가. CDN에서 바뀐 파일만 받아 로컬 사본을 최신으로 유지합니다. 유일하게 네트워크를 쓰는 생성물이라 기본값이 `false`이고, **의존성이 생기는 유일한 자리**이기도 합니다 — 언리얼은 `Build.cs`에 `HTTP` 모듈이, Rust는 `Cargo.toml`에 `ureq`가 함께 들어갑니다. 나머지 언어는 표준 라이브러리만 씁니다. 「[C#](languages/csharp.md#데이터만-갱신하기-writeupdater)」·「[언리얼](languages/unreal.md#데이터만-갱신하기-writeupdater)」·「[Rust](languages/rust.md#데이터만-갱신하기-writeupdater)」·「[Ruby](languages/ruby.md#데이터만-갱신하기-writeupdater)」|

### 내보내기

|키|해당|기본값|무엇인가|
|--|--|--|--|
|`FileExtension`|Binary|`".tcb"`|각 테이블 파일의 확장자. 코드 생성 쪽 `BinaryTableFileExtension`과 짝을 맞추세요|
|`Compress`|Binary|`false`|**예약. 구현되어 있지 않습니다.** 형식이 압축 플래그 자리를 비워두고 있을 뿐, 아무것도 읽거나 쓰지 않습니다|
|`SchemaBaseline`|Binary|`""`|지난 스키마의 기록을 둘 경로. **커밋하세요.** 매 실행이 스키마를 그것과 비교해서, 이미 배포된 테이블 리더가 읽지 못할 변경이면 **아무것도 쓰기 전에** 컬럼 이름과 함께 멈춥니다. 비워두면 검사하지 않습니다|
|`AcceptSchemaChanges`|Binary|`[]`|의도한 변경을 `"테이블.컬럼"`으로 승인. 타입 변경은 재생성된 코드와 함께 나가야 하므로 자동 통과가 아닙니다. 한 번 통과하면 베이스라인이 갱신되니 다시 지워도 됩니다|
|`EncodingReport`|Binary|`""`|컬럼마다 무엇을 재서 그 인코딩을 골랐는지 적을 경로. 후보별 크기는 추정이 아니라 선택이 근거한 실측입니다. 형식에 **없는** 레이아웃까지 재느라 큰 익스포트에서는 시간이 들므로, 경로를 적었을 때만 합니다. 「[내보내기](exports.md#바이너리-익스포트의-recipe-옵션)」|
|`EncryptionKeyVariable`|Binary|`""`|암호화 키가 든 **환경 변수의 이름**. 키가 아니라 이름입니다 — recipe는 커밋되는 파일입니다. 키는 64자리 16진수이고 `tabbit --new-encryption-key`가 만듭니다. 비워두면 파일은 평문입니다|
|`EncryptionKeyFile`|Binary|`""`|암호화 키가 든 **파일의 경로**. `EncryptionKeyVariable`의 대안이고, **둘을 함께 적으면 거절합니다.** 키가 없거나 형식이 틀리면 첫 테이블을 쓰기 전에 멈춥니다|
|`MacKeyVariable`|Binary|`""`|MAC 키가 든 **환경 변수의 이름**. 켜면 파일이 변조되었는지 리더가 검출합니다 — 암호화만으로는 검출되지 않습니다([근거](binary-format.md#변조-검출--mac)). 암호화 키와 **다른 값**이어야 하고, 모양은 같으므로 같은 명령으로 만듭니다. 비워두면 `mac` 필드가 0으로 남습니다|
|`MacKeyFile`|Binary|`""`|MAC 키가 든 **파일의 경로**. `MacKeyVariable`의 대안이고, **둘을 함께 적으면 거절합니다.** 켜는 순서는 데이터가 먼저, 클라이언트의 키가 나중입니다|
|`UseCompactRowFormat`|Json|`false`|각 행을 필드 이름 있는 객체 대신 **값만 담은 배열**로. 작아지지만 사람이 보기 어렵습니다|
|`Indented`|Json|`false`|들여쓰기. 사람이 들여다볼 때만 켜세요|
|`ConnectionString`|DB 4종|`""`|연결 문자열. **`${NAME}`으로 환경 변수를 채웁니다** — 비밀번호를 recipe에 적지 마세요. 변수가 없으면 오류이고 어느 변수인지 출력합니다. recipe의 다른 설정과 달리 **이 타깃이 실행될 때** 해석됩니다([위](#환경-변수--name))|
|`NamePrefix`|DB 4종|`""`|기록되는 모든 테이블·컬렉션·키 이름의 접두사. 데이터베이스 하나에 독립된 데이터 세트를 여럿 둘 때|

### 기록

|키|해당|기본값|무엇인가|
|--|--|--|--|
|`FileName`|Summary|`"summary.json"`|문서의 파일 이름|
|`Author`|Summary|`"full"`|파일에 커밋 작성자를 얼마나 싣는가. `full`은 이름·이메일을 커밋 그대로, `masked`는 각각 첫 글자만 남기고(`서*`, `m*@gmail.com`), `none`은 두 필드를 뺍니다. summary는 산출물 옆에 커밋되거나 다른 팀에 전달되기 쉬운 파일이라, 개인정보를 내보내고 싶지 않으면 낮추세요. 히스토리에는 영향이 없습니다 — 귀속이 목적인 기록이라 전체 작성자를 유지합니다|
|`ConnectionString`|History|`""`|히스토리가 사는 곳. `${NAME}` 지원|
|`ProjectKey`|History|`""`|어느 프로젝트의 히스토리인가. 데이터베이스 하나가 여럿을 담을 수 있고, **이 값을 바꾸면 이어지는 게 아니라 새 히스토리가 시작됩니다**|
|`RecordDirty`|History|`false`|커밋되지 않은 변경이 있는 워킹카피의 변환도 기록할 것인가. 꺼져 있는 이유는 그런 변환이 어느 커밋에도 없는 작업을 담고 있기 때문입니다|
|`AllowOutOfOrder`|History|`false`|브랜치가 이미 도달한 것보다 오래된 커밋도 기록할 것인가|
|`OnFailure`|History|`"warn"`|히스토리에 닿지 못했을 때 `warn`할지 `fail`할지. 기본이 `warn`인 이유는 빌드의 본업이 게임 데이터를 만드는 것이고, 기록용 데이터베이스가 잠깐 안 된다고 그것을 멈출 이유가 없기 때문입니다|


---

## 예제

상황별로 하나씩. 그대로 두고 경로만 바꾸면 됩니다.

### 1. 가장 작은 것 — 엑셀 하나에서 C#으로

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./generated/data" },
    { "Type": "csharp", "Path": "./generated/cs", "Namespace": "MyGame.Data", "AccessorName": "GameData" }
  ]
}
```

`sheets/`의 워크북을 읽어 `generated/data/<테이블>.tcb`와 `generated/cs/`의 C# 코드를 냅니다.

### 2. 유니티 클라이언트

확장자가 `.bytes`인 것에 주의하세요 — 유니티는 그것만 TextAsset으로 포함합니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    {
      "Type": "binary",
      // StreamingAssets는 모든 플랫폼에 배포됩니다.
      "Path": "./Assets/StreamingAssets/Data",
      "FileExtension": ".bytes",
      "TargetSide": "c"
    },
    {
      "Type": "csharp",
      "Path": "./Assets/Scripts/Generated",
      "Namespace": "MyGame.Data",
      "AccessorName": "GameData",
      "BinaryTableFileExtension": ".bytes",   // 익스포터와 짝
      "TargetSide": "c"                        // 서버 전용 데이터는 클라 빌드에 넣지 않습니다
    }
  ]
}
```

### 3. 서버와 클라이언트를 함께

같은 시트에서 두 벌을 뽑습니다. **`TargetSide`가 익스포터와 코드 생성 양쪽에서 맞아야** 합니다 — 어긋나면 컬럼 집합이 달라져 테이블 리더가 데이터와 맞지 않습니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./build/client/data", "FileExtension": ".bytes", "TargetSide": "c" },
    { "Type": "binary", "Path": "./build/server/data", "TargetSide": "s" },
    {
      "Type": "csharp",
      "Path": "./client/Assets/Scripts/Generated",
      "Namespace": "MyGame.Data", "AccessorName": "GameData",
      "BinaryTableFileExtension": ".bytes", "TargetSide": "c"
    },
    {
      "Type": "go",
      "Path": "./server/internal/gamedata",
      "PackageName": "gamedata", "ModulePath": "myserver/internal/gamedata",
      "WriteGoMod": false,                      // 이미 서버 모듈 안입니다
      "TargetSide": "s"
    }
  ]
}
```

### 4. 웹 — 구글 스프레드시트에서 TypeScript로

TypeScript는 JSON과 바이너리 양쪽을 읽으므로 둘 다 내보냅니다.

```jsonc
{
  "Sources": {
    "GoogleSheets": [
      {
        // 커밋하지 마세요.
        "ClientSecretFilename": "./secrets/googlesheets-client-secret.json",
        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"
      }
    ]
  },

  "Targets": [
    { "Type": "json", "Path": "./public/data", "Indented": false },
    { "Type": "binary", "Path": "./public/data" },
    { "Type": "typescript", "Path": "./src/generated", "AccessorName": "Tables" },
    { "Type": "html", "Path": "./docs/data" }
  ]
}
```

### 5. 게임 서버 — 데이터베이스로 직접

비밀번호는 recipe에 적지 않습니다. `${NAME}`이 환경 변수를 채우고, 변수가 없으면 오류입니다.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    {
      "Type": "mysql",
      "ConnectionString": "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}",
      "NamePrefix": "tb_",     // 한 데이터베이스에 여러 세트를 둘 때
      "TargetSide": "s"
    },
    {
      "Type": "redis",
      "ConnectionString": "${REDIS_HOST}:6379,password=${REDIS_PASSWORD}",
      "TargetSide": "s"
    },
    { "Type": "cpp", "Path": "./src/generated", "Namespace": "game::data",
      "AccessorName": "GameData", "TargetSide": "s" }
  ]
}
```

### 6. 언리얼

모듈이 `Source/GameData/`에 생성됩니다. 데이터를 어디에 두고 패키징에 어떻게 포함시키는지는 [Unreal 가이드](languages/unreal.md#패키징--빌드-포함-여부)를 보세요.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./Sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    { "Type": "binary", "Path": "./Content/Data", "TargetSide": "c" },
    {
      "Type": "unreal",
      "Path": "./Source",
      "ModuleName": "GameData",
      "AccessorName": "FGameData",
      "TargetSide": "c"
    }
  ]
}
```

### 7. CI — 누가 무엇을 바꿨는지 기록하며

`history`는 변환마다 셀 단위 스냅샷을 남깁니다. `OnFailure`가 `warn`이라, 기록용 데이터베이스가 잠깐 안 되어도 빌드는 계속됩니다.

`SchemaBaseline`은 CI에서 특히 값을 합니다 — 이미 배포된 클라이언트가 못 읽을 스키마 변경이면 **데이터를 쓰기 전에** 빌드가 멈춥니다. 베이스라인 파일은 커밋하세요.

```jsonc
{
  "Sources": {
    "Xlsx": [ { "Path": "./sheets", "FileExtensionPatterns": ".xlsx" } ]
  },

  "Targets": [
    {
      "Type": "binary",
      "Path": "./build/data",
      "SchemaBaseline": "./schema-baseline.json",
      "AcceptSchemaChanges": []
    },
    { "Type": "summary", "Path": "./build/summary" },
    {
      "Type": "history",
      "ConnectionString": "Server=${HISTORY_HOST};Database=tabbit;Uid=ci;Pwd=${HISTORY_PASSWORD}",
      "ProjectKey": "mygame",
      "OnFailure": "warn"
    }
  ]
}
```

```
tabbit --recipe ci-recipe.json --commit $GITHUB_SHA
```

### 8. 전부 — 13개 언어를 한 번에

`side-by-side/side-by-side.json`이 저장소에 있고, 실제로 매번 실행되어 [side-by-side/](../side-by-side/)에 결과가 커밋됩니다. 언어별 출력이 어떻게 생겼는지 나란히 볼 수 있습니다.

```
dotnet run --project src/Tabbit.csproj -- --recipe side-by-side/side-by-side.json
```

### 실제로 돌아가는 recipe들

[test/fixtures/recipes/](../test/fixtures/recipes/)에 회귀 스위트가 매번 실행하는 recipe가 서른 개 가까이 있습니다. 문서의 예제와 달리 **반드시 최신**입니다 — 낡으면 테스트가 깨지기 때문입니다.

|파일|내용|
|--|--|
|`core.json`|엑셀 하나에서 바이너리·JSON·C#·C++·HTML까지|
|`core-client.json` / `core-server.json`|`TargetSide`로 나눠 뽑기|
|`conformance.json`|13개 타깃 전부를 한 recipe에|
|`table-extension.json`|`.tcb`가 아닌 확장자로 맞추기|
|`databases.json`|MySQL / PostgreSQL / MongoDB / Redis|
|`history.json`|히스토리 기록|
|`core-dynamic.json`|`Targets` 목록만으로 전부 지정하기|

### 전체 예제 (모든 설정)

<details>
<summary>펼쳐보기</summary>

```json
{
  // 배열 셀의 구분자. 쉼표가 기본이 아닌 이유는 문장과 숫자 표기에 너무 흔하기 때문입니다.
  "ArrayDelimiter": ";",

  // 0번 라벨이 없는 enum에 `None = 0`을 넣어줍니다.
  // 켜두는 쪽이 기본인 이유: enum 타입의 필드는 값이 대입되기 전에도 뭔가를 들고 있어야 하는데,
  // 그게 이름 없는 0이면 디버거에서도 로그에서도 읽을 수 없기 때문입니다.
  // 시트에 적은 것만 정확히 나오길 원한다면 끄세요.
  "AutoInsertEnumNoneLabel": true,

  "Sources": {
    "Xlsx": [
      {
        "Path": "./sheets",
        "FileExtensionPatterns": ".xls;.xlsx",

        // 시트를 읽는 방식. 기본은 `tabbit` — 마커로 엔티티를 선언하는 방식입니다.
        // 다른 규칙으로 작성된 시트를 그대로 읽으려면 `rescue`. 자세한 건 sheets.md 참고.
        "Layout": "tabbit",

        // 읽을 워크북 목록. 비우면 Path 아래 전부. 상대 경로·파일명·확장자를 뗀 이름
        // 중 무엇으로 적어도 되고, 여기 적었는데 없는 워크북은 오류로 알려줍니다.
        "IncludeWorkbooks": [],

        // 제외할 워크북. IncludeWorkbooks 다음에 적용되고, 제외된 워크북은 열지도
        // 않습니다. 보통 쓰게 되는 것은 이쪽입니다.
        "ExcludeWorkbooks": ["백업/*"],

        // 읽을 시트 목록. 비우면 전부. 배열로도, `;`로 이은 문자열로도 쓸 수 있습니다.
        // `*` `?` 와일드카드가 파일 글롭과 같게 동작하고, 여기 적었는데 없는 시트는
        // 말없이 빠지는 대신 오류로 알려줍니다.
        "IncludeSheets": [],

        // 제외할 시트. IncludeSheets 다음에 적용됩니다. `[워크북]시트`로 적으면 그
        // 워크북에만 적용됩니다 — 시트 이름은 워크북마다 겹칩니다.
        "ExcludeSheets": ["*참고용*", "[Items.xlsx]Define"],

        // 인덱스 값이 겹칠 때: `error`(기본) / `keep-first` / `keep-last`.
        // 뒤의 둘은 겹치는 것을 허용하는 레이아웃 전용이며, 버린 행을 로그에 남깁니다.
        "OnDuplicateIndex": "error",

        // `#REF!` 같은 수식 오류 셀: `error`(기본) / `empty`.
        // `empty`는 남이 관리하는 워크북을 위한 것입니다. 삼킨 셀은 하나하나 경고하고
        // 끝에 총계를 냅니다.
        "OnFormulaError": "error",

        // `Text1`/`Text2`를 배열 하나로 접을지. 기본은 끔 — 이름의 숫자가 배열을 뜻하는지는
        // 이름으로는 판정할 수 없는 문제입니다.
        "FoldSerialFields": false,

        // 레코드 배열에서 값이 없는 뒤쪽 원소를 버릴지. 기본은 끔 — 배열이 짧아지는 것은
        // 아무 말도 하지 않습니다. 가운데는 지우지 않습니다.
        "TrimTrailingArrayElements": false,

        // 이 레이아웃만 아는 설정. 코어는 키를 모르고, 레이아웃이 오타를 보고합니다.
        "LayoutOptions": {}
      }
    ],
    "GoogleSheets": [
      {
        // 이 파일은 커밋하지 마세요. .gitignore에 등록되어 있습니다.
        // 변환을 돌리는 *사람*으로 접속합니다.
        "ClientSecretFilename": "./googlesheets-client-secret.json",

        // CI라면 이쪽입니다 — 잡 자신으로 접속하므로 개인 계정에 종속되지 않습니다.
        // 위의 것과 함께 적으면 거절합니다. 셋 다 비우면 이 항목은 꺼집니다.
        "ServiceAccountKeyFile": "",
        "ServiceAccountKeyVariable": "",

        "SheetsId": "10NXZAeyFaxRFsC8BPVTS9A6DzsM57Z1tizpJMCokJwU"

        // 위의 소스 항목 공통 설정은 여기서도 같습니다.
      }
    ]
  },

  "Targets": [
    {
      "Type": "binary",
      "Path": "./generated/binary",
      "FileExtension": ".tcb"
    },
    {
      "Type": "json",
      "Path": "./generated/json",
      // true면 이름 없이 값만 배열로 담습니다. 파일이 작아집니다.
      "UseCompactRowFormat": false,
      "Indented": false
    },

    // 데이터베이스 적재. 비밀값은 ${환경변수}로 빼세요.
    {
      "Type": "mysql",
      "ConnectionString": "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}",
      "NamePrefix": "tb_"
    },
    {
      "Type": "postgresql",
      "ConnectionString": "Host=db;Database=game;Username=tabbit;Password=${DB_PASSWORD}",
      "Schema": "public",
      "NamePrefix": "tb_"
    },
    {
      "Type": "mongodb",
      // 데이터베이스 이름을 반드시 포함해야 합니다.
      "ConnectionString": "mongodb://db:27017/game",
      "NamePrefix": "tb_"
    },
    {
      "Type": "redis",
      "ConnectionString": "db:6379,password=${REDIS_PASSWORD}",
      "Database": 0,
      "NamePrefix": "tb_"
    },
    {
      "Type": "csharp",
      // 출력 타겟 폴더입니다. 없으면 자동으로 만듭니다.
      "Path": "./generated/cs",
      "Namespace": "StaticData",
      "AccessorName": "SheetAccessor"
    },
    {
      "Type": "typescript",
      "Path": "./generated/ts",
      // true면 enum을 숫자 대신 문자열로 생성합니다.
      "UseStringEnum": false
    },
    {
      "Type": "cpp",
      "Path": "./generated/cpp",
      // `.`이나 `::`로 중첩 네임스페이스를 지정할 수 있습니다.
      "Namespace": "game::data",
      "AccessorName": "SheetAccessor"
    },
    {
      "Type": "html",
      "Path": "./generated/html"
    }
  ]
}
```

</details>
