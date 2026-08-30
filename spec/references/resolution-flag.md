# 참조의 해석 여부 — `_F` 플래그 제거

> [문서 목록으로](../../doc/readme.md)
>
> 상태: **됨** — C#·TypeScript, 형식 무변경

참조 컬럼 하나가 C#과 TypeScript에서 **셋**을 냈습니다 — 해석된 행, 파일에 실린 키, 그리고
「해석되었는가」를 담은 `bool` 하나입니다. 마지막 것의 이름이 `_F`였고, 나머지 11개 언어에는
없었습니다.

---

## 1. 있던 것

```csharp
internal ItemCategoryRecord _categoryId;              // 해석된 행
internal int _categoryId_ItemCategory_index;          // 파일에 실린 키
public bool _categoryId_F = false;                    // 해석 여부
```

쓰는 자리가 둘이었습니다. 테이블 읽기가 키를 담으면서 `false`로 두고, 링킹이 조회에 성공하면
`true`로 바꿉니다.

```csharp
if (record._categoryId_ItemCategory_index > 0)
{
    record.SetReference_CategoryId_INTERNAL(...);
    record._categoryId_F = true;
}
```

## 2. 없앤 이유

**세 가지가 겹쳤습니다.**

|무엇|
|--|
|**같은 답이 이미 두 곳에 있습니다.** 링킹이 `_F = true`를 쓰는 조건이 곧 키가 채워져 있는 것이므로, 로드가 끝난 뒤 `_F`는 예외 없이 「키가 채워져 있는가」와 같은 값입니다|
|**나머지 언어가 이미 없이 합니다.** 널이 그 답이고, 그것이 [레코드 안의 참조](references-in-records.md#언어별-분기--원소-안에-두는-것)에 언어별 분기로 적혀 있었습니다|
|**새로 설계한 자리에서는 이미 두지 않기로 했습니다.** [다중 대상 참조의 접근자](multi-target-accessors.md#6-언어별로-무엇을-두는가)가 식별자로 답하면서 플래그를 두지 않았습니다|

**「키가 없으면 널도 없다」가 성립하지 않는 경우가 있는지 확인했습니다.** 대상의 값 하나를
가리키는 참조(`foreign Piece.Tier`)는 해석 결과가 `int`이므로 널로 판정할 수 없습니다. 이 경우가
플래그를 정당화할 뻔한 유일한 자리인데, **키가 공개 표면에 있습니다** — 값 참조의 키는
`public int[] _tier_Piece_index`이고, 행 참조의 키는 `public int[] Slot => _slot_Piece_index`
같은 프로퍼티이며, 레코드 멤버의 키는 원소 타입의 공개 필드입니다. 그래서 판정 수단이 없는
자리가 하나도 없습니다.

## 3. 대신 쓰는 판정

|참조의 형태|해석되었는지 확인하는 방법|
|--|--|
|행을 가리키는 것|해석된 행이 널이 아닌지. 나머지 11개 언어와 같습니다|
|대상의 값 하나를 가리키는 것|키가 채워져 있는지. `> 0` · `is { Length: > 0 }` · `!= Guid.Empty` — 키 타입이 정합니다|

두 번째 줄의 조건은 링킹이 값을 쓰기 전에 이미 보는 그 조건입니다.

## 4. 형식의 무변경

**와이어에 플래그는 없었습니다.** 파일은 키만 싣고 나머지는 로드 뒤에 계산되므로, 이 제거는
`.tcb`도 JSON도 건드리지 않습니다. 골든에서 달라지는 것은 C#과 TypeScript의 생성 코드뿐입니다.

## 5. 게이트

|게이트|확인하는 것|
|--|--|
|골든|C#·TypeScript에서 선언·초기화·링킹의 대입이 사라지고, **나머지 11개 언어는 한 바이트도 바뀌지 않아야 합니다**|
|적합성 하네스|`cs-check-record-ref` · `cs-check-serial-ref` · `ts-check-record-ref` · `ts-check-serial-ref` · `ts-check`. 플래그를 읽던 자리가 널과 키로 바뀌고 **보고의 내용은 같아야 합니다**|
|`SerialReferenceTests`|읽기가 파일의 수로 할당하는 배열이 셋에서 **둘**로|
|샘플 재생성|`samples/*/out/`과 유니티 생성 코드에서 `_F`가 사라지는지|
