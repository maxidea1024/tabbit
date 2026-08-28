# C# 레코드 이름 — 중첩 `Record`에서 `{테이블}Record`로

> 상태: **구현 완료** (2026-08-28) — 관련: [생성 코드의 이름 체계](generated-naming.md) ·
> [참조 표면의 이름](../references/reference-surface-naming.md) ·
> [테이블의 컬렉션 표면](table-collection-surface.md)

C#만 레코드를 테이블 클래스 안에 중첩해 `ItemTable.Record`로 냈습니다. 다른 언어는 모두
네임스페이스 바로 아래의 `ItemRecord`입니다. **C#을 나머지에 맞춥니다.**

---

## 1. 어긋나 있던 것

|레코드 이름|언어|
|--|--|
|`{테이블}Record`|C++ · Go · Java · Kotlin · Swift · TypeScript · Dart · Rust · Python · PHP · Ruby · Lua|
|`{접두사}_{테이블}Record_t`|C — 네임스페이스가 없어 접두사가 그 역할을 합니다|
|`F{테이블}Row`|Unreal — 엔진의 DataTable 관례이므로 **별개 축입니다**|
|중첩한 `Record`|**C# 하나**|

[Java 생성기](../../src/CodeGeneration/JavaCodeGenerator.cs)에는 그때의 판단이 적혀 있습니다 —
중첩해 `ItemTable.Record`로 부르는 대안이 있었고, 파일 하나를 줄이는 값으로 이름이 나빠지는
것을 택하지 않았습니다. C#은 한 파일에 타입 둘을 둘 수 있어 그 값조차 없습니다.

## 2. 정한 것

|무엇|어떻게|
|--|--|
|레코드 타입|`{테이블}Record`를 **네임스페이스 바로 아래**에 선언합니다|
|파일|`tables/{테이블}Table.cs` 하나 그대로입니다. 타입 둘이 한 파일에 있고 레코드가 위에 옵니다|
|레코드 안의 중첩 타입|그 자리에 둡니다 — 그룹의 원소 타입은 `ItemRecord.RewardEntry`입니다|
|`#region Record`|**없앱니다.** 네임스페이스 아래의 타입 하나에는 지역으로 감쌀 것이 없습니다|

```csharp
// 전
public partial class ItemTable : IEnumerable<ItemTable.Record>
{
    public partial class Record { ... }
    public List<Record> Records => _records;
}

// 후
public partial class ItemRecord { ... }

public partial class ItemTable : IEnumerable<ItemRecord>
{
    public List<ItemRecord> Records => _records;
}
```

## 3. 함께 바뀌는 것 — 규칙이 쓰는 역방향 조회

검증 규칙은 행 하나만 들고 `Error(row, ...)`를 부르고, 그 행이 어느 테이블의 것인지는
[`CellLocator`](../../src/Validation/CellLocator.cs)가 타입에서 읽습니다. 중첩이던 동안은
`DeclaringType`의 이름에서 `Table`을 떼는 것이 그 방법이었습니다. 이제 타입 이름에서
`Record`를 뗍니다.

게이트가 있습니다 — `ValidationPipelineTests`가 규칙이 낸 오류의 위치를 셀 하나까지
확인하므로, 이 조회가 어긋나면 그 게이트가 실패합니다.

## 4. 담지 않는 것

|무엇|근거|
|--|--|
|Unreal의 `F{테이블}Row`|엔진 관례가 이름을 정합니다. 다른 언어와 맞추는 축이 아닙니다|
|레코드를 자기 파일로 옮기는 것|Java가 그렇게 하는 것은 **언어가 강제하기 때문**입니다. C#은 강제하지 않으므로 파일을 늘릴 이유가 없습니다|
|`Records`·`FindBy…`의 이름|이 문서는 타입 이름만 정합니다|

## 5. 파급

- **커밋된 C# 생성 트리 전부 재기록입니다.** 골든 · 전 언어 비교본 · sprout · wildling ·
  clover. 레코드 본문이 4칸 내어써지므로 그 행이 전부 diff에 오릅니다.
- **파괴적 변경입니다.** `partial class Record`로 생성 레코드를 확장하던 코드는 선언을
  `partial class {테이블}Record`로 옮겨야 합니다.
- **샘플의 손으로 쓴 소비 코드**가 함께 바뀝니다 — wildling의 유니티 스크립트와 두 샘플의
  검증 규칙입니다. 유니티 스크립트에는 컴파일 게이트가 없어 사람이 확인합니다.
