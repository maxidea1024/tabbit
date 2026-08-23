# 트러블슈팅

빌드가 실패했을 때 어디를 볼 것인가입니다.

여기 있는 메시지는 전부 도구가 실제로 출력하는 문장입니다.

> [문서 목록으로](readme.md)

생성된 코드를 프로젝트에 적용하다 생기는 문제는 [언어별 가이드](languages/readme.md)의 각 문서
끝에 따로 있습니다.

---

## 먼저 읽는 법

오류는 한 번에 모아서 보고됩니다. 오류 하나당 한 번씩 재실행할 필요가 없습니다.

```
Fatal: Field `Item.CategoryId` references `ItemCategory` row `99`, which does not exist.
   at test/fixtures/xlsx/core/core.xlsx : Refs : J8

Details:
  [  1] Index field `Item.Index` repeats the value `3`, ...
        at test/fixtures/xlsx/core/core.xlsx : Refs : I10
  [  2] ...
```

- **첫 줄**이 무엇이 잘못됐는지.
- **`at`** 이 어느 파일의 어느 시트, 어느 셀인지. 구글 스프레드시트라면 URL이라 바로 열립니다.
- **`Details:`** 는 같이 발견된 나머지입니다.

`--debug`를 붙이면 콜스택도 나옵니다. 도구 자체의 버그를 의심할 때만 사용하면 됩니다.

**실패한 빌드는 아무것도 남기지 않습니다.**

파일은 스테이징 영역을 거쳐 마지막에 일괄 반영되고, 데이터베이스는 섀도 테이블에 적재한 뒤
원자적으로 교체합니다.

그러므로 실패했다면 이전 출력이 그대로 유지됩니다.

---

## 시트를 읽는 중

### `Unexpected entity-marker`

마커 문법이 어긋났습니다. `~~table:Item~~`, `~~enum:Grade~~`, `~~const:GameConfig~~` 형식이어야 하고, 서버/클라를 나눌 때는 `~~table:Item:c~~`처럼 뒤에 붙입니다.

임시로 빼려면 이름 앞에 `#`을 붙이세요 — [시트 작성](sheets.md)의 「작성중인 데이터 임시로 제외하기」.

### `Entity 'Table:Item' starts outside the sheet: its marker points at ...`

마커가 가리키는 지점이 시트 범위 밖입니다. 마커만 남기고 내용을 지웠거나, 시트를 잘라낸 뒤 마커가 남았을 때 나옵니다.

### `Entity 'Table:Item' must have cells of at least ... size`

엔티티가 최소 크기를 못 채웁니다. 테이블은 마커·주석·이름·타입·세부타입·설명 줄과 데이터 한 줄이 필요합니다.

### `Entity 'Table's name 'Item' is a duplicated`

같은 이름의 엔티티가 둘입니다. 시트가 달라도 이름은 전역입니다.

### `Field name 'Name' is a duplicated`

한 테이블 안에 같은 이름의 필드가 둘입니다.

### `'2nd Field' is not a valid identifier, so it cannot name a field or an entity`

식별자로 쓸 수 없는 이름입니다. 숫자로 시작하거나 공백·기호가 들어갔습니다. 생성되는 코드에서
멤버 이름이 되어야 하므로 문자나 `_`로 시작해야 하고, 문자·숫자·`_`만 쓸 수 있습니다.

> **컬럼 이름이 id인 표에서 이 메시지가 나옵니다.** 행과 열이 모두 id인 매트릭스 형태는
> 컬럼 이름이 식별자가 될 수 없어서 지금은 읽지 못합니다 — [앞으로 할 것](roadmap.md)의 5b.

### `Field name '**Foo**' has more than one leading '*'`

secondary index는 `*` **하나**입니다.

### `The primary index field cannot be omitted`

테이블의 첫 필드가 primary index여야 합니다. [시트 작성](sheets.md)의 「인덱스 필드」를 보세요.

### `The index field 'Index' is 'bool', 'and a table keyed by a bool can only hold two rows.' Use a whole-number, string, uuid or enum column as the index`

인덱스가 될 수 있는 것은 `int`·`bigint`·`string`·`uuid`·`enum`입니다. 거부되는 이유는 서로
다릅니다 — `bool`은 값이 둘뿐이어서 행이 두 개를 넘을 수 없고, `float`·`double`은 정확히
비교되지 않아 조회가 실패 없이 빗나가고, 배열은 한 셀에 값이 여럿입니다. `datetime`·`timespan`은
틱이라 비교는 정확하지만, **행을 시각으로 찾는 시트가 없어서** 받지 않습니다.
[인덱스 필드](sheets.md#인덱스-필드) 참고.

보조 인덱스(`*`)도 같은 문장으로 거부됩니다. 두 자리의 규칙이 하나이기 때문입니다.

### `Target 'html' does not support optional fields yet`

그 타깃이 「값이 없음」을 표현하지 못합니다. 없음을 잃은 채로 내보내면 「비었다」와 「0」이
같아 보이는데, 그게 바로 `?`가 없애려는 것이라 말없이 내보내는 대신 그 이름과 함께 멈춥니다.
recipe에서 그 타깃을 빼거나, 컬럼에서 `?`를 떼세요. **모든 언어와 `json`·`binary`가
지원하고**, 남은 것은 `html`·데이터베이스·`summary`·`history`입니다.

### `references 'X', whose index is an enum`

enum으로 키를 잡은 테이블을 `foreign`으로 가리켰습니다. **다른 키 타입은 전부
됩니다** — `int`·`bigint`·`string`·`uuid`로 키를 잡은 테이블은 가리킬 수 있고, 참조 컬럼이 그
키를 그대로 담습니다 ([설계](../spec/reference-key-types.md)).

enum만 남은 것은 규칙이 아니라 구멍입니다. enum 값은 고정 폭이 아니라 지그재그 인코딩으로
실리고, 그 읽기는 언어마다 자기 enum을 쓰기 때문에 공용 읽기 표에 항목이 없습니다. 대상을
enum의 바탕 `int`로 키를 잡거나, 값을 그 enum으로 들고 대상 테이블의 인덱스로 직접 찾으세요.

> 전에는 `int`가 아닌 키 전부가 거절이었고, 메시지가 「키를 직접 들고 찾아보라」고 했습니다.
> 그것은 형식이 못 해서가 아니라 `int32`가 6곳에 상수로 하드코딩되어 있어서였습니다.

### `The target-side of the index field must be set to CS`

index 필드를 서버나 클라 한쪽으로 보낼 수 없습니다. 양쪽 다 필요한 값입니다.

---

## 값을 해석하는 중

### `Cannot parse '...' as a value of type 'int'`

셀 값이 그 타입으로 읽히지 않습니다. 흔한 원인:

- 엑셀이 값을 텍스트로 저장해 앞뒤에 공백이 남음 (Tabbit은 앞뒤 공백을 다듬으므로 대개 문제되지 않습니다)
- 숫자 칸에 `1,024`처럼 천 단위 구분자 — **이건 허용됩니다**
- 날짜 칸에 엑셀 표시 형식만 바뀐 숫자

### `Cell contains the formula error '#REF!'`

수식이 오류를 냈습니다. 수식을 고치거나 리터럴 값으로 바꾸세요. 오류인 채로 내보내지 않습니다.

### `type 'foo' is an unrecognized type`

지원하지 않는 타입 이름입니다. 목록은 [시트 작성](sheets.md)의 「Supported Data Types」에 있습니다.

### `In case of enum type, enum name must be specified in detail-type`

`enum` 타입은 세부타입 칸에 enum 이름이 있어야 합니다.

### `In case of foreign type, 'RefTable[.RefFieldName]' must be specified in detail-type`

`foreign` 타입은 세부타입 칸에 가리킬 테이블(과 선택적으로 필드)이 있어야 합니다 — `Owners` 또는 `Owners.rank`.

### `type 'foreign' cannot be used as an array element`

`foreign[]`은 지원하지 않습니다. 개수가 고정이면 serial field(`Ref1`, `Ref2`, …)를, 하나면 `foreign`을 쓰세요.

### `Label 'X' is already defined in enum 'Y'` / `Constant 'X' is already defined in constant-set 'Y'`

같은 이름이 두 번 나왔습니다.

---

## 참조와 인덱스

### `Field 'Item.CategoryId' references 'ItemCategory' row '99', which does not exist`

가리키는 행이 없습니다. `0`은 "참조 없음"이라 허용되고, 그 외의 값은 실제로 있어야 합니다.

### `Index field 'Item.Index' repeats the value '3'`

primary index가 중복입니다. 행을 복사하고 인덱스를 안 고쳤을 때 나옵니다.

### `In a client build, field 'Item.CategoryId' references table ...`

클라이언트로 가는 필드가 서버 전용 테이블을 가리킵니다. 그쪽 빌드에는 그 테이블이 없으므로 참조가 성립하지 않습니다. 둘 중 하나의 `TargetSide`를 고치세요.

---

## 스키마가 바뀐 뒤

여기 모인 메시지는 대부분 **차단이 목적**입니다. 이미 배포된 클라이언트가 못 읽을 데이터를 쓰기 전에 멈추는 것이고, 규칙과 근거는 [바이너리 형식](binary-format.md)에 있습니다.

### `Field name 'Price@x' has an '@' where a wire tag goes`

`@` 뒤가 1 이상의 정수가 아닙니다. `Price@3`처럼 씁니다.

### `Table 'Item' tags some fields and not others: ...`

태그는 테이블 단위로 **전부 또는 전무**입니다. 반쯤 단 표는 어느 쪽 보장도 못 하기 때문입니다. 나열된 필드에 `@N`을 달거나, 전부에서 떼세요.

### `Field 'Item.Price' declares wire tag 3, which field 'Name' already holds`

같은 테이블에서 태그가 겹쳤습니다. `#`로 제외된 컬럼이 예약한 태그와 겹친 경우도 같은 메시지가 나옵니다 — 그 태그는 이미 데이터를 실었으므로 비어 있는 것이 아닙니다.

### `The schema changed in ... way(s) that a reader already built from the previous schema would not survive`

`SchemaBaseline` 검사가 차단했습니다. **아무것도 쓰이지 않았습니다.** 아래 항목들이 뒤따라 나오며, 각각이 무엇을 해야 하는지 함께 적습니다.

### `... is gone from the schema. Tombstone it in the sheet as ... so nothing takes its tag`

컬럼을 지웠는데 태그가 남지 않았습니다. 시트에 `#이름@N`을 남기세요 — 태그가 비면 다음에 추가하는 컬럼이 그것을 가져가고, 그러면 삭제 전에 만들어진 테이블 리더가 새 컬럼을 옛 필드로 읽습니다.

정말 태그까지 버릴 생각이라면 `AcceptSchemaChanges`에 `"테이블.컬럼"`을 적으세요. 그래도 베이스라인은 그 태그를 retired로 계속 기억합니다.

### `... refuses this column rather than reading it wrongly`

컬럼의 타입이나 구조가 바뀌었습니다. 넓히는 변경(int→bigint 등)이라도 **구 테이블 리더는 거절합니다** — 잘릴 수 있는 값을 읽지 않는 것이 설계이기 때문입니다. 그래서 이 변경은 재생성된 코드와 함께 나가야 하고, 그 사실을 `AcceptSchemaChanges: ["테이블.컬럼"]`로 적어 확인합니다. 한 번 통과하면 베이스라인이 갱신되니 목록에서 지워도 됩니다.

### `... has taken tag ..., which ... used and gave up`

한 번 쓰이고 버려진 태그를 다른 컬럼이 가져갔습니다. **승인 방법이 없습니다** — 다른 변경은 구 테이블 리더가 맞게 읽거나 거절하지만, 이것만은 틀린 컬럼을 성공적으로 읽습니다. 그 컬럼에 아무도 쓴 적 없는 새 태그를 주세요.

### `... has no explicit tags, and tag ... was ... and is now ...`

`@N`이 없는 테이블에서 컬럼이 밀렸습니다. 태그가 위치이므로 중간을 지우거나 삽입하면 그 뒤 전부의 태그가 바뀝니다. 그 테이블에 `@N`을 달거나(권장), 밀린 것이 의도라면 승인하세요.

### `The schema baseline ... could not be read`

베이스라인 파일이 깨졌습니다. 고치거나 지우세요 — 지우면 한 번은 검사를 건너뛰고 현재 스키마로 새로 씁니다. 도구가 자동으로 새로 쓰지 않는 이유는, 누가 파일을 깨뜨린 바로 그 순간이 검사를 건너뛸 때가 아니기 때문입니다.

### 생성된 테이블 리더가 컬럼을 거부함

런타임에 다음과 같은 메시지가 납니다.

```
Item.Price: the file carries element type 3, which this member cannot read
(accepts 2, 0). The column changed type incompatibly; regenerate the code
or rebuild the data.
```

파일의 컬럼과 그것을 읽는 멤버의 타입이 맞지 않습니다. 데이터가 코드보다 새롭다면(넓어진 컬럼) 코드를 재생성하고, 코드가 새롭다면 데이터를 다시 뽑으세요. **읽고 나서 틀린 값을 주는 것보다 여기서 멈추는 것이 낫다**는 판단이고, `SchemaBaseline`을 켜두면 이 상황이 배포 전에 잡힙니다.

구조가 바뀐 경우(스칼라 ↔ 배열, 고정배열 길이 변경)에는 `does not match the generated member`가 붙은 메시지가 같은 자리에서 납니다.

### `the file and the generated member disagree about whether this column is optional`

컬럼에 `?`가 붙거나 떨어진 뒤에 **한쪽만** 다시 만들었습니다. 옵셔널 컬럼은 블록 앞에 presence 비트맵을 달고 있어서, 그것을 기다리지 않는 코드는 비트맵을 값으로 읽습니다. 그래서 이것도 구조 변경으로 취급해 거부합니다 — 코드를 다시 생성하거나 데이터를 다시 내보내세요.

**모든 리더가 비트 6을 nullability로 읽으므로**, 옛 데이터든 옛 코드든 위의 메시지로 나옵니다. 롤아웃 중에는 아직 지원하지 않는 리더들이 비트 6을 kind 쪽에 함께 읽어서 **kind가 맞지 않는다**는 메시지를 냈는데 — 비트맵을 값으로 읽는 것보다 낫기 때문이었고 — 그 리더들이 전부 지원하고 있으므로, 현재는 그 경로가 없습니다.

### 테이블 리더가 파일 버전을 거부함

```
table format version 103 is not supported (expected 104)
```

이 빌드가 모르는 형식의 파일입니다. 호환 경로는 없으므로 **데이터를 다시 뽑으세요** — 형식 버전은 하나뿐이고, 모르는 버전을 추측해서 읽지 않는 것이 설계입니다.

103은 컬럼이 로우마다 값의 유무를 담을 수 있게 되면서, 104는 인코딩이 4종 늘면서 올라갔습니다. 새 인코딩이 하나도 이기지 않은 파일은 **버전 4바이트만** 달라지지만, 그 4바이트가 다른 파일을 읽지 않는 것이 이 형식의 규칙입니다.

### `the table is encrypted and was not decrypted - pass the key through Open first`

암호화된 파일의 바이트를 `envelope`을 열지 않고 리더에 그대로 넘겼습니다. 로드 경로에서 `envelope`을 여는 호출을 먼저 거치세요 — **암호화되지 않은 파일은 그 호출에서 그대로 돌아오므로**, 키를 쓰는지 여부로 경로를 나눌 필요가 없습니다. 「[파일 암호화](binary-format.md#파일-암호화)」

### `the file did not decrypt to a table - the key is not the one it was written with`

파일이 쓰인 키와 지금 쓰는 키가 다릅니다. 암호문 머리의 `keyCheck` 4바이트가 이것을 「파일이 손상됐다」와 구분해 주고, 값을 하나도 읽기 전에 멈춥니다. 키를 바꿨다면 **그 키로 내보낸 데이터를 함께 배포해야 합니다** — 데이터와 클라이언트가 따로 갱신되는 구조라면 두 쪽이 같은 시점에 바뀌어야 합니다.

> 이 메시지를 사용자에게 그대로 보이지 말고 「데이터를 다시 받으십시오」 정도로 바꾸는 편이 낫습니다.

### `the file does not match its MAC - it was altered after it was exported`

파일의 바이트가 내보낸 시점과 다릅니다. **정상 경로에서는 나오지 않는 메시지**이므로, 나왔다면 둘 중 하나입니다.

- **누군가 파일을 고쳤습니다.** MAC이 검출하려고 있는 것이 이것입니다.
- **MAC 키가 데이터와 안 맞습니다.** 키를 바꾸고 데이터를 다시 내보내지 않았거나, 그 반대입니다.

전송 중 손상은 보통 이쪽이 아니라 업데이터의 매니페스트 해시에서 먼저 걸립니다.

### `the file carries no MAC and this build expects one`

클라이언트에는 MAC 키가 있는데 데이터에는 MAC이 없습니다. **켜는 순서가 뒤집힌 경우**가 대부분입니다 — recipe에 `MacKeyVariable`을 먼저 넣고 데이터를 다시 내보낸 다음, 클라이언트에 키를 넣습니다.

거절하는 이유는 그렇지 않으면 **16바이트를 0으로 덮는 것만으로 검사가 없어지기** 때문입니다. 「[변조 검출](binary-format.md#변조-검출--mac)」

### `the file does not begin with the table file signature`

`.tcb`가 아닌 파일을 리더에 넘겼습니다. 모든 테이블 파일은 암호화 여부와 무관하게 `54 43 42 00`(`TCB\0`)으로 시작합니다 — 경로가 잘못됐거나, 빌드 단계에서 파일이 다른 것으로 덮였는지 보세요.

---

## Recipe

### `Recipe 'Targets[2]' has no 'Type', so there is nothing to say which target it configures`

`Targets`의 각 항목은 무엇을 만들지 나타내는 `Type`이 있어야 합니다.

### `Recipe 'Targets[2]' names target 'csharpp', which does not exist`

오타이거나 없는 타깃입니다. 쓸 수 있는 이름을 메시지가 함께 적어줍니다.

### `Recipe '...' sets up target 'csharp', but could not be read`

그 타깃에 없는 설정을 적었습니다. `FileExtention` 같은 오타가 **말없이 기본값으로 넘어가지 않도록** 오류입니다 — 그냥 넘어가면 증상이 "설정이 안 먹는다"로만 보입니다.

### `Recipe section '...' has TargetSide 'client-only', which is not recognized`

`client`, `server`, `both` 중 하나입니다.

### `Recipe setting 'ArrayDelimiter' is '...', but it must be exactly one character`

배열 구분자는 한 글자입니다.

### `Recipe '...' reads workbooks from '...', which does not exist`

`Sources`의 경로가 없습니다. 경로는 **CLI를 실행한 위치 기준**입니다.

---

## 검증 규칙

전체 사용법은 「[검증](validation.md)」에 있고, 여기 있는 것은 규칙 파일을 쓰다 만나는 메시지입니다.

### `The recipe's 'Validation.Path' is '...', and there is no folder there`

지정했는데 없는 폴더입니다. **오류인 이유**는 오타 하나로 검증 전체가 걸리는 것 없이 통과하기 때문입니다. 검증 없이 돌리려면 `Path`를 비웁니다.

### `The validation folder '...' has a subfolder 'X', which is not one this layout runs`

`pre` · `tables` · `global` · `runtime` · `shared` 다섯뿐입니다. `table/`처럼 이름이 어긋난 폴더는 규칙이 하나도 돌지 않고, 산출물의 어디에도 그 사실이 남지 않습니다. 작업 중인 폴더는 `#`로 시작하면 건너뜁니다.

### `'rules/tables/X.cs' is a rule for table 'X', which this model does not have`

테이블 이름이 바뀌었거나 파일 이름에 오타가 있습니다. **오류인 이유**는 규칙이 말없이 안 도는 것이 더 나쁘기 때문입니다 — 비슷한 이름이 있으면 메시지가 함께 적어줍니다. 한 테이블에 대한 규칙이 아니라면 `rules/global/`로 옮깁니다.

### `'...' has nothing to run`

`public static void Validate(<그 단계의 컨텍스트> context)`가 없는 파일입니다. 규칙 파일은 클래스 하나에 `Validate` 하나이고, 진입점이 없는 헬퍼는 `rules/shared/`에 둡니다 — 거기 있는 것은 모든 규칙과 함께 컴파일되고 그 자체로는 실행되지 않습니다.

### `This rule reads the validation option 'X', which the recipe does not set`

recipe의 `Validation.Options`에 넣거나, 없어도 되는 값이면 `Option("X", 기본값)`을 씁니다. **빈 문자열로 말없이 대신하지 않는 이유**는 로케일 비교가 아무것과도 맞지 않아 **아무것도 검사하지 않는 규칙**이 되기 때문입니다.

### `This rule opens an external store, which only the 'rules/runtime/' rules may do`

`Db()`·`Redis()`를 `rules/runtime/` 밖에서 불렀습니다. 그 폴더가 `--skip-runtime-validation`이 건너뛰는 단위이므로, 밖에 있는 연결은 접근 권한이 없는 기계에서 무엇을 건너뛰든 실패합니다.

### `'...' made '50' more report(s) than the '100' shown`

한 규칙이 상한을 넘겨 보고했습니다. **규칙 자체가 틀린 경우가 대부분입니다** — 실제로 상점 규칙을 이식할 때 대상 테이블 하나를 빠뜨려 4,400건이 나왔습니다. 조건을 먼저 의심하세요.

### 규칙 파일의 컴파일 오류

검증 오류와 같은 경로로, 파일·줄·열과 함께 보고합니다. 한 파일이 깨져도 나머지는 전부 컴파일하므로 한 번에 전부 나옵니다. **이것이 타입을 쓰는 이유입니다** — 없는 컬럼이나 없는 enum 값은 실행 중의 드러나지 않는 미스가 아니라 여기서 걸립니다.

---

## 구글 스프레드시트

### 브라우저가 열리고 인증을 요구함

첫 실행에는 OAuth 동의가 필요합니다. 토큰은 홈 디렉터리 아래 `.credentials/sheets.googleapis.com-tabbit`에 저장되므로 다음부터는 묻지 않습니다.

### `Recipe '...' names client secret file ...`

클라이언트 시크릿 파일 경로가 잘못됐거나 파일이 없습니다. 발급 절차는 [시트 작성](sheets.md)의 「Google Spread Sheets」에 스크린샷과 함께 있습니다.

> 시크릿 파일은 **커밋하지 마세요.** 저장소 히스토리에 한 번 들어가면 지워도 이미 복제된 사본에는 남습니다.

### 갑자기 인증이 안 됨

홈 디렉터리의 `.credentials/sheets.googleapis.com-tabbit`을 지우고 다시 실행하면 재인증합니다.

---

## 데이터베이스

### `Recipe section '...' has no ConnectionString`

연결 문자열이 없습니다.

### 연결 문자열의 비밀번호 취급

**적지 마세요.** `${VAR}` 형식으로 쓰면 환경 변수에서 채웁니다.

```
Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}
```

변수가 설정되어 있지 않으면 **오류이고, 어느 변수인지 이름으로 출력합니다.** 빈 문자열로 말없이 치환하지 않습니다 — 그러면 인증 실패가 "비밀번호가 틀렸다"로 보이고, 진짜 원인인 "변수를 안 넣었다"는 어디에도 안 나옵니다.

### `MySQL exporter cannot map type '...' of column '...'`

그 엔진으로 옮길 수 없는 타입입니다.

### `Could not clean up MySQL shadow tables` / `Redis refused the swap transaction`

적재는 섀도 테이블에 한 뒤 원자적으로 교체합니다. 교체가 거부되면 **기존 데이터는 그대로**입니다. 정리 실패는 경고이고 남은 섀도 테이블은 다음 실행이 덮어씁니다.

---

## `--serve`

### 400과 함께 메시지가 옴

요청이 잘못됐습니다. 메시지에 무엇이 잘못됐는지 나옵니다.

### 500과 사건 ID만 옴

도구 쪽 문제입니다. 상세는 서버 로그에 그 ID로 남습니다.

### `--bind ... is not an address`

IP, `localhost`, 또는 모든 인터페이스를 뜻하는 `0.0.0.0`이어야 합니다.

### 외부 바인딩을 거부함

`127.0.0.1` 밖으로 열려면 토큰이 필요합니다. 히스토리에는 시트 내용과 그것을 건드린 사람들의 이름이 들어 있기 때문입니다.

환경 변수로 토큰을 주고 `Authorization: Bearer <token>`으로 보내세요. 브라우저로 열 때는 `?token=<token>`을 한 번 붙이면 HttpOnly 쿠키로 바뀌고 URL에서 사라집니다.

### `The history holds no project called '...'`

프로젝트 키가 다릅니다. 기록할 때 쓴 것과 같아야 합니다.

### `No working copy was found, so a range can only be asked for by commit hash`

`HEAD~3` 같은 상대 표기는 워킹카피가 있어야 풉니다. 없으면 커밋 해시로 지정하세요.

---

## 생성 결과가 이상할 때

### 지운 테이블의 코드가 남아 있음

스윕이 꺼져 있습니다(`"Sweep": false`). 켜면 헤더에 `Generated by Tabbit`이 적힌 파일 중 이번 실행이 쓰지 않은 것을 지웁니다.

### 남의 파일이 지워질까 걱정됨

지워지지 않습니다. 마커가 허가증이고, 그것을 쓰는 것은 이 도구뿐입니다.

### `Two different files were generated for '...'`

두 타입의 이름이 같은 파일 이름으로 줄어들었습니다 — `Item` 테이블과 `Item` enum, 또는 snake_case로 바꾸면 같아지는 `ItemType`과 `Item_Type`. 시트에서 하나를 다른 이름으로 바꾸세요.

예전에는 나중에 쓴 쪽이 앞의 것을 덮고 다른 타입은 출력에 아예 없었습니다. 그러면 소비자가 자기 컴파일러에게서, 이 도구가 생성하였다고 보고한 타입의 이름과 함께, 근거는 어디에도 없이 확인하게 됩니다.

### 데이터 파일을 못 찾음

확장자가 어긋났을 가능성이 큽니다. 익스포터의 `FileExtension`과 코드 생성 타깃의 `BinaryTableFileExtension`이 같아야 합니다. 패키징 과정에서 이름이 바뀌었다면 `readAll`에 확장자를 인자로 넘기세요 — [언어별 가이드](languages/readme.md)의 「데이터 파일 확장자」.

### `Embedded resource ... is missing from the build` / `Embedded template ...`

빌드가 깨졌습니다. 테이블 리더와 템플릿은 임베디드 리소스라 정상 빌드에는 반드시 있습니다. `dotnet build --no-incremental`로 다시 빌드해 보세요 — 템플릿을 고친 뒤 증분 빌드는 리소스를 다시 임베드하지 않습니다.

### `The ... generator cannot read type '...'` / `cannot render type '...'`

값 타입을 새로 추가했는데 그 언어에 가르치지 않았습니다. 추가해야 할 곳은 `LanguageProfile`의 표와, 필요하면 그 제너레이터의 enum·참조 처리입니다. `LanguageProfileTests`가 어느 언어의 무엇이 빠졌는지 이름으로 알려줍니다.

---

## 파일 이동 단계에서 실패

```
While moving the artifact file to the actual target path, We got the below error.
This would have caused problems with the final result.
Please return to the previous state with version control such as git or svn.
```

스테이징에서 실제 경로로 옮기는 **마지막 단계**에서 실패했습니다. 이 지점은 되돌릴 수 없는 유일한 구간입니다 — 파일 일부는 옮겨졌고 일부는 아닐 수 있습니다.

흔한 원인은 대상 파일이 열려 있거나(에디터, 백신), 권한이 없거나, 디스크가 찼을 때입니다. 원인을 없앤 뒤 버전 관리로 되돌리고 다시 실행하세요.
