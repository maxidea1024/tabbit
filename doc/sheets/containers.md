# `set`과 `map`

> [문서 목록으로](../readme.md)

`.tbs` 선언에서 멤버의 타입으로 씁니다.

```
struct Bag
    /// 이 행이 무엇으로 분류되는지. 서로 달라야 합니다.
    field tags   set<string>
    /// 아이템 키마다의 가격.
    field prices map<int,int(min=1)>
```

**파일 형식은 그대로입니다.** `set`은 배열 컬럼 하나이고 `map`은 길이가 같은 컬럼 둘입니다 —
같은 데이터를 배열 컬럼으로 적은 것과 **바이트가 같은 파일**이 나옵니다.

---

## `.tbs` 없이 map 하나만 쓰기

**시트의 타입 칸에 그대로 적을 수 있습니다.** 선언 파일도, 컬럼을 감싸는 그룹도 필요 없습니다.

|컬럼|타입 칸|셀|
|--|--|--|
|`Prices`|`map<int,int>`|`10:100;11:120`|

**`set`은 타입 칸에 적을 수 없습니다.** 이름을 대고 거절하며, 이유는 조회가 앉을 자리입니다 —
타입 칸의 `map`은 컬럼 둘이 되면서 레코드가 생기고 그 레코드에 조회가 붙는데, `set`은 컬럼
하나여서 그런 레코드가 없습니다. 배열만 나오고 물을 것이 없으면 그것은 반쪽입니다. `.tbs`에
멤버로 선언하거나, 그냥 `string[]`으로 두십시오.

---

## 시트에 적는 법

|무엇|어떻게|
|--|--|
|`set`|배열과 같습니다 — `new;sale`|
|`map` — 컬럼 둘|`Prices.Key`에 `10;11`, `Prices.Value`에 `100;120`|
|`map` — 한 셀|`Prices`에 `10:100;11:120`. 항목 사이가 배열 구분자, 키와 값 사이가 `:`|

값에 `:`가 들어가야 하면 `\:`로 적습니다.

**값이 struct인 map은 컬럼 둘로만 적습니다** — `Drops.Key`와 `Drops.Value.ItemId`처럼
`Value` 아래로 한 겹 더 내려갑니다.

---

## 거부되는 것

|무엇|이유|
|--|--|
|`set`의 원소 중복 · `map`의 키 중복|**그것이 이 타입의 뜻입니다.** 어느 셀의 몇 번째 원소인지 함께 보고합니다|
|키와 값의 개수가 다른 행|map은 쌍입니다|
|키가 `float` · `double` · `datetime` · `timespan` · `bitset`|동등성이 값 자체에 있지 않습니다. 키가 될 수 있는 것은 `int` · `bigint` · `string` · `bool` · `uuid` · enum입니다|
|`set<T>[]` · `map<K,V>[]`, 번호 붙은 그룹 안의 컨테이너|컬럼 하나가 목록의 목록을 담아야 합니다|
|`map<K,V?>` · `map<int,set<int>>` · `foreign` 키|1차에서 담지 않습니다|

---

## 생성 코드 — 두 겹의 표면

**배열과 조회를 둘 다 냅니다.**

```csharp
bag.Tags                                     // string[] — 시트에 적힌 순서
bag.ContainsTags("sale")                     // 있는지 묻기

bag.Prices.Key                               // int[]
bag.Prices.Value                             // int[]
bag.Prices.TryGetValue(11, out int price)    // 값 찾기
```

값이 struct인 map은 항목 하나에 해당하는 객체가 없으므로 **자리**를 돌려줍니다. 이름이 다르므로
헷갈리지 않습니다 — `TryGetIndex` · `indexByKey`.

```csharp
bag.Drops.TryGetIndex(2, out int at);
bag.Drops.Value.ItemId[at];
```

철자는 언어의 관례를 따릅니다 — TypeScript는 인터페이스의 프로퍼티(`bag.prices.byKey`), Go는
구조체의 맵 필드(`bag.Prices.ByKey`), C는 배열 위를 훑는 함수입니다.

---

## 순서 — 반드시 알아야 하는 한 가지

**적은 순서가 필요하면 배열을 돕니다. 조회는 찾을 때만 씁니다.**

|무엇|보장|
|--|--|
|**배열**|**언제나 시트에 적힌 순서입니다.** 이 도구는 정렬하지 않습니다|
|**조회를 순회한 순서**|**보장하지 않습니다.** 언어의 컨테이너가 정합니다|

절반의 언어에 순서를 지키는 해시 컨테이너가 표준으로 없기 때문입니다.

|조회를 순회하면 파일 순서가 나오는가|언어|
|--|--|
|**나옵니다**|Java · Kotlin · TypeScript · Ruby · PHP · Dart, 그리고 Python의 `map`|
|**나오지 않습니다**|C# · Go · Rust · C++ · Swift · Lua · Unreal, 그리고 Python의 `set`|

> Python이 갈리는 것을 보십시오 — 같은 언어 안에서도 `dict`는 지키고 `set`은 지키지 않습니다.
> 「이 언어는 지킨다」로 외우면 틀립니다. **배열을 돈다**가 어디서나 맞는 답입니다.

```csharp
// 순서대로 훑는 법. 모든 언어에서 같은 답을 냅니다.
for (int j = 0; j < bag.Prices.Key.Length; j++)
    Use(bag.Prices.Key[j], bag.Prices.Value[j]);
```

**순서 있는 조회를 모든 언어에 두지 않은 이유**는 셋입니다 — 있는 언어에서는 이미 순서 있는
컨테이너를 씁니다. 없는 언어에 외부 패키지를 들이면 생성 코드가 표준 라이브러리 밖의 것을
요구하게 되고, 직접 만들면 런타임이 일곱 벌 늘어납니다. 배열이 이미 답하고 있는 것에 지불할
값이 아닙니다.

> 설계와 근거는 [`set`과 `map`](../../spec/types/set-and-map.md)에 있습니다.
