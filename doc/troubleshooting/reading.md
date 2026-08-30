# 시트를 읽는 중에 나는 것

> [「트러블슈팅」으로 돌아가기](../troubleshooting.md)

---

## 시트를 읽는 중

### `The marker column of ... holds ..., which this layout does not define`

마커 열에 이 레이아웃이 모르는 값이 있습니다. 마커 열이 담는 것은 선언(`:table` · `:enum` ·
`:const`), 헤더 행 키(`:field` · `:type` · `:desc` · `:target` · `:variant`), 그 행을
제외하는 `#`, 그리고 데이터 행의 빈 칸뿐입니다. 대개 행 키의 오타입니다.

### `... and names none. Write the name after the keyword`

`:table` 다음에 이름이 없습니다. 키워드 뒤에 적습니다 — `:table Item`.

### `... has no ':field' row. It is the one header row that cannot be left out`

생략할 수 없는 유일한 헤더 행입니다. 컬럼이 무엇인지 적히는 자리가 그 행뿐입니다.

### `... has no ':type' row. Every field column states its type`

테이블의 필드 컬럼은 모두 타입을 적습니다. enum과 상수셋은 컬럼이 이름으로 정해져 있어 이
행이 아예 없습니다.

### `... has a ':type' row below a data row`

**헤더 행까지 선택 범위에 넣고 시트를 정렬하면 이렇게 됩니다.** 헤더 행은 데이터보다 위에
있어야 하고, 보고는 옮겨진 그 행을 가리킵니다.

### `... is declared twice - as ... here, and already above`

같은 이름의 엔티티가 둘입니다. 종류가 달라도, 시트가 달라도 이름은 전역입니다 — 생성 코드에서
그 이름이 하나이기 때문입니다.

### `... has two columns named ...`

한 테이블 안에 같은 이름의 컬럼이 둘입니다. 이름은 Pascal 표기로 정규화되므로 `item_id`와
`ItemId`도 같은 이름입니다.

### `Column ... has no name in ':field', and ... is written below it`

이름 없는 컬럼에 데이터가 있습니다. 필드라면 이름을 적고, 시트 작성자의 자유 공간이라면
`:field` 셀에 `#`를 적으세요 — 그렇게 표시한 컬럼은 아무것도 읽지 않습니다.

### `'2nd Field' is not a valid identifier, so it cannot name a field or an entity`

식별자로 쓸 수 없는 이름입니다. 숫자로 시작하거나 공백·기호가 들어갔습니다. 생성되는 코드에서
멤버 이름이 되어야 하므로 문자나 `_`로 시작해야 하고, 문자·숫자·`_`만 쓸 수 있습니다.

> **컬럼 이름이 id인 표에서 이 메시지가 나옵니다.** 행과 열이 모두 id인 매트릭스 형태는
> 컬럼 이름이 식별자가 될 수 없어서 지금은 읽지 못합니다 — [앞으로 할 것](../roadmap.md)의 5b.

### `Column ... begins with more than one '*'`

보조 인덱스는 `*` **하나**입니다.

### `Column ... is marked '*', and it holds an array`

배열은 인덱스가 될 수 없습니다. `*`를 **멀티 로우** 표시로 적으셨다면, 이 레이아웃의 멀티
로우는 이름에 붙는 `[]`이고 `*`는 보조 인덱스입니다.

### `... Element numbers count from zero and run without a gap`

원소 번호는 0부터 시작해 빠짐없이 이어집니다. 엑셀의 행 번호가 1부터라 걸리기 쉬운 자리입니다.

### `... is written in column ... on a row that extends the record above it`

멀티 로우 표에서 **연장 행에 값을 담는 것은 `[]` 컬럼뿐**입니다. 이 값은 레코드의 첫 행에
적거나, 새 레코드를 뜻한 것이라면 그 행의 인덱스를 적으세요.

### `The index field 'Index' is 'bool', 'and a table keyed by a bool can only hold two rows.' Use a whole-number, string, uuid or enum column as the index`

인덱스가 될 수 있는 것은 `int`, `bigint`, `string`, `uuid`, `enum`입니다.

거부되는 이유는 서로 다릅니다.

- `bool`은 값이 둘뿐이어서 행이 두 개를 넘을 수 없습니다.
- `float`와 `double`은 정확히 비교되지 않아 조회가 실패 없이 빗나갑니다.
- 배열은 한 셀에 값이 여럿입니다.
- `datetime`과 `timespan`은 틱이라 비교는 정확하지만, 행을 시각으로 찾는 시트가 없어서 받지
  않습니다.

[인덱스 필드](../sheets/naming.md#인덱스-필드)를 참고하세요.

보조 인덱스(`*`)도 같은 문장으로 거부됩니다. 두 자리의 규칙이 하나이기 때문입니다.

### `Target 'html' does not support optional fields yet`

그 타깃이 「값이 없음」을 표현하지 못합니다.

없음을 잃은 채로 내보내면 「비었다」와 「0」이 같아 보이는데, 그것이 바로 `?`가 없애려는
것입니다. 그래서 말없이 내보내는 대신 그 이름과 함께 멈춥니다.

recipe에서 그 타깃을 빼거나, 컬럼에서 `?`를 떼세요.

모든 언어와 `json`, `binary`가 지원합니다.
남은 것은 `html`, 데이터베이스, `summary`, `history`입니다.

### `references 'X', whose index is an enum`

enum으로 키를 잡은 테이블을 `foreign`으로 가리켰습니다. **다른 키 타입은 전부
됩니다** — `int`·`bigint`·`string`·`uuid`로 키를 잡은 테이블은 가리킬 수 있고, 참조 컬럼이 그
키를 그대로 담습니다 ([설계](../../spec/references/reference-key-types.md)).

enum만 남은 것은 규칙이 아니라 구멍입니다. enum 값은 고정 폭이 아니라 지그재그 인코딩으로
실리고, 그 읽기는 언어마다 자기 enum을 쓰기 때문에 공용 읽기 표에 항목이 없습니다. 대상을
enum의 바탕 `int`로 키를 잡거나, 값을 그 enum으로 들고 대상 테이블의 인덱스로 직접 찾으세요.

> 전에는 `int`가 아닌 키 전부가 거부이었고, 메시지가 「키를 직접 들고 찾아보라」고 했습니다.
> 그것은 형식이 못 해서가 아니라 `int32`가 6곳에 상수로 하드코딩되어 있어서였습니다.

### `The target-side of the index field must be set to CS`

index 필드를 서버나 클라 한쪽으로 보낼 수 없습니다. 양쪽 다 필요한 값입니다.

---

## 값을 해석하는 중

### `Cannot parse '...' as a value of type 'int'`

셀 값이 그 타입으로 읽히지 않습니다. 흔한 원인:

- 엑셀이 값을 텍스트로 저장해 앞뒤에 공백이 남음 (Tabbit은 앞뒤 공백을 다듬으므로 대개 문제되지 않습니다)
- 숫자 칸에 `1,024`처럼 천 단위 구분자 — **이건 허용됩니다**
- 자릿수 구분자 `1_024`도 **허용됩니다**. `0x`·`0b`와 지수 표기도 마찬가지입니다
  ([숫자 표기](../sheets/layout.md#숫자-표기))
- 날짜 칸에 엑셀 표시 형식만 바뀐 숫자

### `'...' has a '_' that is not between two digits`

자릿수 구분자는 숫자와 숫자 사이에만 놓습니다. `_1000`·`1000_`·`1_.0`·`1e_5`가 오류이고,
`0x`·`0b` 바로 뒤만 예외입니다(`0b_1010`). C#의 규칙과 같습니다.

### `'...' is written with both ',' and '_'`

한 셀에 천 단위 구분자와 자릿수 구분자가 함께 적혀 있습니다. 값은 갈리지 않지만 적은 사람의
의도가 갈리므로 오류입니다. 둘 중 하나로만 끊으세요.

### `'...' names a value with a fractional part, and 'int' holds whole numbers`

정수 컬럼에 소수가 적혀 있습니다. `1e3`이나 `1.5e3`처럼 **정수가 되는** 지수 표기는
받지만, `1.5`나 `1e-3`은 받지 않습니다. 소수를 담을 컬럼이라면 타입이 `float`이나
`double`입니다.

### `'...' is not a boolean`

메시지가 무엇을 참으로, 무엇을 거짓으로 읽는지 그 자리에 나열합니다. `Ture` 같은 오타가
대부분입니다.

**시트에 `예`·`아니오`처럼 적고 싶다면** recipe에 낱말을 더합니다 —
[`TrueWords` · `FalseWords`](../recipe/settings.md#truewords--falsewords--참과-거짓의-낱말).

### `Cell contains the formula error '#REF!'`

수식이 오류를 냈습니다. 수식을 고치거나 리터럴 값으로 바꾸세요. 오류인 채로 내보내지 않습니다.

### `type 'foo' is an unrecognized type`

지원하지 않는 타입 이름입니다. 목록은 [시트 작성](../sheets.md)의 「Supported Data Types」에 있습니다.

### `... is typed 'foreign' and names no table`

`foreign` 뒤에 가리킬 테이블을 적습니다 — `foreign Item`, 또는 그 테이블의 값 하나를
가리키려면 `foreign Owners.rank`입니다. 여러 테이블 중 하나여도 되는 값은 참조가
아니라 검사이고 `int (refs=Item;CharGear)`으로 적습니다.

**세부타입 칸은 없습니다.** 타입 하나가 셀 하나에 들어가므로 enum도 이름을 바로 적습니다 —
`Grade`.

### `... has no single type to resolve to`

`foreign A|B` — 테이블을 여럿 지목했습니다. 참조는 테이블 하나를 지목해 해석하는 것이라,
여러 테이블 중 하나의 id일 수 있는 값에는 해석할 타입이 하나가 없습니다. **그것은 검사이고**
`int (refs=A;B)`로 적습니다. 행에 닿아야 하면 테이블마다 컬럼을 두세요.

### `Enum ... has two labels named ...` / `Constant set ... has two constants named ...`

같은 이름이 두 번 나왔습니다.

---

## 참조와 인덱스

### `Field 'Item.CategoryId' references 'ItemCategory' row '99', which does not exist`

가리키는 행이 없습니다. `0`은 "참조 없음"이라 허용되고, 그 외의 값은 실제로 있어야 합니다.

### `Index field 'Item.Index' repeats the value '3'`

primary index가 중복입니다. 행을 복사하고 인덱스를 안 고쳤을 때 나옵니다.

### `In a client build, field 'Item.CategoryId' references table ...`

클라이언트로 가는 필드가 서버 전용 테이블을 가리킵니다. 그쪽 빌드에는 그 테이블이 없으므로
참조가 성립하지 않습니다. 둘 중 하나의 `TargetSide`를 고치세요.

---
