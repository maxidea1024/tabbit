# 값이 여러 개일 때

<!-- 이 파일은 `doc/figures/showcase.py` 가 생성합니다. 손으로 고치지 마십시오 - 다음 실행이 덮어씁니다. -->

> [생성되는 코드로](../generated-code.md)

---

배열이 오는 자리는 둘입니다.

- **셀 하나 안에 여러 값** — 타입을 `string[]` 으로 적고 셀에 `potion;cheap` 처럼 적습니다
- **컬럼 여러 개가 한 배열** — `Weight[0]` · `Weight[1]` 처럼 번호를 붙입니다

시트에서 쓰기 편한 쪽을 고르면 됩니다.

<!-- tabbit:pair -->

![테이블 Loot](../figures/showcase-loot.svg)

<!-- tabbit:tabs lang -->
<details data-lang="csharp" open>
<summary>C#</summary>

```csharp
[System.Serializable]
public partial class LootRecord
{
    #region Values
    /// <summary>
    /// primary index
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// search tags
    /// </summary>
    public string[] Tags => _tags;

    /// <summary>
    /// weight 1
    /// </summary>
    public int[] Weight => _weight;
    #endregion

    #region Storage
    internal int _index;
    internal string[] _tags = System.Array.Empty<string>();
    internal int[] _weight = System.Array.Empty<int>();
    #endregion

    #region ToString
    public override string ToString()
    {
        var sb = new StringBuilder("{");
        sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
        sb.Append(",\"Tags\":"); ToStringHelper.ToString(Tags, sb);
        sb.Append(",\"Weight\":"); ToStringHelper.ToString(Weight, sb);
        sb.Append("}");
        return sb.ToString();
    }
    #endregion
}
```

[LootTable.cs](../../test/fixtures/golden/doc-showcase/csharp/tables/LootTable.cs)

</details>
<details data-lang="typescript">
<summary>TypeScript</summary>

```typescript
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/** Two ways to write an array. */
export class LootRecord {
  /** Default constructor */
  constructor() {
  }

  /** primary index */
  public get index(): number { return this._index }

  /** search tags */
  public get tags(): string[] { return this._tags }

  /** weight 1 */
  public get weight(): number[] { return this._weight }

  public _index: number = 0
  public _tags: string[] = []
  public _weight: number[] = []

  /** Populate field values. */
  public populateFieldValues(dataRow: IDataRow): void {
    this._index = dataRow.index
    this._tags = dataRow.tags
    this._weight = dataRow.weight
  }

  /** Populate field values. */
  public populateFieldValuesCompact(dataRow: any[]): void {
    let offset = 0
    this._index = dataRow[offset++]
    this._tags = dataRow[offset++]
    this._weight = dataRow.slice(offset, offset + 2)
    offset += 2
  }
}
```

[loot.ts](../../test/fixtures/golden/doc-showcase/typescript/tables/loot.ts)

</details>
<details data-lang="cpp">
<summary>C++</summary>

```cpp
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/// Two ways to write an array.
struct LootRecord {
  /// primary index
  std::int32_t index = 0;
  /// search tags
  std::vector<std::string> tags;
  /// weight 1
  std::vector<std::int32_t> weight;
};
```

[DocShowcaseAccessor_loot.h](../../test/fixtures/golden/doc-showcase/cpp/tables/DocShowcaseAccessor_loot.h)

</details>
<details data-lang="python">
<summary>Python</summary>

```python
class LootRecord:
    """Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2.

    Two ways to write an array.
    """

    __slots__ = ("index", "tags", "weight")

    def __init__(self):
        self.index = 0
        self.tags = []
        self.weight = []

    def __repr__(self):
        return "LootRecord(index=%r, tags=%r, weight=%r)" % (self.index, self.tags, self.weight)
```

[loot_table.py](../../test/fixtures/golden/doc-showcase/python/doc_showcase_data/loot_table.py)

</details>
<details data-lang="c">
<summary>C</summary>

```c
/* Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
 *
 * Two ways to write an array.
 */
struct DocShowcase_LootRecord_t {
  /* primary index */
  int32_t index;
  /* search tags */
  const char** tags;
  int32_t tags_count;
  /* weight 1 */
  int32_t* weight;
  int32_t weight_count;
};
```

[DocShowcase_Loot.h](../../test/fixtures/golden/doc-showcase/c/tables/DocShowcase_Loot.h)

</details>
<details data-lang="dart">
<summary>Dart</summary>

```dart
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/// Two ways to write an array.
class LootRecord {
  /// primary index
  int index = 0;
  /// search tags
  List<String> tags = [];
  /// weight 1
  List<int> weight = [];

}
```

[loot_table.dart](../../test/fixtures/golden/doc-showcase/dart/tables/loot_table.dart)

</details>
<details data-lang="go">
<summary>Go</summary>

```go
// LootRecord was generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2.
// Two ways to write an array.
type LootRecord struct {
	// primary index
	Index int32
	// search tags
	Tags []string
	// weight 1
	Weight []int32
}
```

[loot_table.go](../../test/fixtures/golden/doc-showcase/go/loot_table.go)

</details>
<details data-lang="java">
<summary>Java</summary>

```java
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/** Two ways to write an array. */
public final class LootRecord {
    /** primary index */
    public int index;
    /** search tags */
    public String[] tags = new String[0];
    /** weight 1 */
    public int[] weight = new int[0];

}
```

[LootRecord.java](../../test/fixtures/golden/doc-showcase/java/tabbit/fixtures/docshowcase/LootRecord.java)

</details>
<details data-lang="kotlin">
<summary>Kotlin</summary>

```kotlin
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/** Two ways to write an array. */
class LootRecord {
    /** primary index */
    var index: Int = 0
    /** search tags */
    var tags: MutableList<String> = ArrayList()
    /** weight 1 */
    var weight: MutableList<Int> = ArrayList()

}
```

[LootTable.kt](../../test/fixtures/golden/doc-showcase/kotlin/tabbit/fixtures/docshowcase/tables/LootTable.kt)

</details>
<details data-lang="lua">
<summary>Lua</summary>

```lua
-- Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2.
-- Two ways to write an array.
---@class LootRecord
---@field index integer
---@field tags string[]
---@field weight integer[]
local LootRecordMeta = tcb.strictType("a `Loot` row", { "index", "tags", "weight" })
```

[loot_table.lua](../../test/fixtures/golden/doc-showcase/lua/tables/loot_table.lua)

</details>
<details data-lang="php">
<summary>PHP</summary>

```php
/**
 * Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
 *
 * Two ways to write an array.
 */
final class LootRecord
{
    /** primary index */
    public int $index = 0;
    /** search tags */
    /** @var list<string> */
    public array $tags = [];
    /** weight 1 */
    /** @var list<int> */
    public array $weight = [];
}
```

[LootTable.php](../../test/fixtures/golden/doc-showcase/php/tables/LootTable.php)

</details>
<details data-lang="ruby">
<summary>Ruby</summary>

```ruby
# Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
# Two ways to write an array.
class LootRecord
  attr_accessor :index, :tags, :weight

  def initialize
    @index = 0
    @tags = []
    @weight = []
  end
```

[loot_table.rb](../../test/fixtures/golden/doc-showcase/ruby/tables/loot_table.rb)

</details>
<details data-lang="rust">
<summary>Rust</summary>

```rust
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/// Two ways to write an array.
#[derive(Clone, Debug, Default)]
pub struct LootRecord {
    /// primary index
    pub index: i32,
    /// search tags
    pub tags: Vec<String>,
    /// weight 1
    pub weight: Vec<i32>,
}
```

[loot_table.rs](../../test/fixtures/golden/doc-showcase/rust/src/loot_table.rs)

</details>
<details data-lang="swift">
<summary>Swift</summary>

```swift
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/// Two ways to write an array.
public final class LootRecord {

    public init() {}

    /// primary index
    public var index: Int32 = 0

    /// search tags
    public var tags: [String] = []

    /// weight 1
    public var weight: [Int32] = []
}
```

[LootTable.swift](../../test/fixtures/golden/doc-showcase/swift/tables/LootTable.swift)

</details>
<details data-lang="unreal">
<summary>Unreal</summary>

```cpp
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Loot : B2
/** Two ways to write an array. */
USTRUCT(BlueprintType)
struct DOCSHOWCASE_API FLootRow
{
    GENERATED_BODY()

    /** primary index */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Loot")
    int32 Index = 0;

    /** search tags */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Loot")
    TArray<FString> Tags;

    /** weight 1 */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Loot")
    TArray<int32> Weight;

};
```

[FDocShowcase.h](../../test/fixtures/golden/doc-showcase/unreal/Source/DocShowcase/Public/FDocShowcase.h)

</details>
<!-- /tabbit:tabs -->

<!-- /tabbit:pair -->

**생성된 코드에서는 둘이 구별되지 않습니다.** 둘 다 그냥 배열입니다 — 어느
쪽으로 적었는지는 시트의 사정이고, 코드는 그것을 알 필요가 없습니다.

`Weight` 는 컬럼이 둘이므로 길이가 언제나 2이고, `Tags` 는 행마다 다릅니다.
