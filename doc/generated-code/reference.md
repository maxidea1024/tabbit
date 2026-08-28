# 다른 테이블 가리키기

<!-- 이 파일은 `doc/figures/showcase.py` 가 생성합니다. 손으로 고치지 마십시오 - 다음 실행이 덮어씁니다. -->

> [생성되는 코드로](../generated-code.md)

---

`:type` 칸에 `foreign <테이블>` 이라고 적으면 그 컬럼은 숫자가 아니라
**저 테이블의 행**입니다. 셀에는 대상의 키를 적습니다.

<!-- tabbit:pair -->

![테이블 Shop](../figures/showcase-shop.svg)

![테이블 ShopEntry](../figures/showcase-shop-entry.svg)

<!-- tabbit:tabs lang -->
<details data-lang="csharp" open>
<summary>C#</summary>

```csharp
[System.Serializable]
public partial class ShopEntryRecord
{
    #region Values
    /// <summary>
    /// primary index
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// which shop
    /// </summary>
    public int ShopId => _shopId_Shop_index;
    public ShopRecord ShopByShopId => _shopId;

    /// <summary>
    /// which potion
    /// </summary>
    public int PotionId => _potionId_Potion_index;
    public PotionRecord PotionByPotionId => _potionId;

    /// <summary>
    /// how many
    /// </summary>
    public int Stock => _stock;
    #endregion

    #region Reference wiring
    public void SetReference_ShopId_INTERNAL(ShopRecord value) => _shopId = value;
    public void SetReference_PotionId_INTERNAL(PotionRecord value) => _potionId = value;
    #endregion

    #region Storage
    internal int _index;
    internal ShopRecord _shopId;
    internal int _shopId_Shop_index;
    internal PotionRecord _potionId;
    internal int _potionId_Potion_index;
    internal int _stock;
    #endregion

    #region ToString
    public override string ToString()
    {
        var sb = new StringBuilder("{");
        sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
        sb.Append(",\"ShopId\":"); ToStringHelper.ToString(ShopId, sb);
        sb.Append(",\"PotionId\":"); ToStringHelper.ToString(PotionId, sb);
        sb.Append(",\"Stock\":"); ToStringHelper.ToString(Stock, sb);
        sb.Append("}");
        return sb.ToString();
    }
    #endregion
}
```

[ShopEntryTable.cs](../../test/fixtures/golden/doc-showcase/csharp/tables/ShopEntryTable.cs)

</details>
<details data-lang="typescript">
<summary>TypeScript</summary>

```typescript
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/** What each shop sells. */
export class ShopEntryRecord {
  /** Default constructor */
  constructor() {
  }

  /** primary index */
  public get index(): number { return this._index }

  /** which shop */
  public get shopId(): number { return this._shopId_Shop_index }
  public get shopByShopId(): ShopRecord { return this._shopId }

  /** which potion */
  public get potionId(): number { return this._potionId_Potion_index }
  public get potionByPotionId(): PotionRecord { return this._potionId }

  /** how many */
  public get stock(): number { return this._stock }

  public setReference_shopId_INTERNAL(value: ShopRecord) { this._shopId = value; }
  public setReference_potionId_INTERNAL(value: PotionRecord) { this._potionId = value; }

  public _index: number = 0
  public _shopId: ShopRecord
  public _shopId_Shop_index: number = 0
  public _potionId: PotionRecord
  public _potionId_Potion_index: number = 0
  public _stock: number = 0

  /** Populate field values. */
  public populateFieldValues(dataRow: IDataRow): void {
    this._index = dataRow.index
    this._shopId_Shop_index = dataRow.shopId
    this._potionId_Potion_index = dataRow.potionId
    this._stock = dataRow.stock
  }

  /** Populate field values. */
  public populateFieldValuesCompact(dataRow: any[]): void {
    let offset = 0
    this._index = dataRow[offset++]
    this._shopId_Shop_index = dataRow[offset++]
    this._potionId_Potion_index = dataRow[offset++]
    this._stock = dataRow[offset++]
  }
}
```

[shop-entry.ts](../../test/fixtures/golden/doc-showcase/typescript/tables/shop-entry.ts)

</details>
<details data-lang="cpp">
<summary>C++</summary>

```cpp
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/// What each shop sells.
struct ShopEntryRecord {
  /// primary index
  std::int32_t index = 0;
  /// which shop
  std::int32_t shop_id = 0;
  const ShopRecord* shop_by_shop_id = nullptr;
  /// which potion
  std::int32_t potion_id = 0;
  const PotionRecord* potion_by_potion_id = nullptr;
  /// how many
  std::int32_t stock = 0;
};
```

[DocShowcaseAccessor_shop_entry.h](../../test/fixtures/golden/doc-showcase/cpp/tables/DocShowcaseAccessor_shop_entry.h)

</details>
<details data-lang="python">
<summary>Python</summary>

```python
class ShopEntryRecord:
    """Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2.

    What each shop sells.
    """

    __slots__ = ("index", "shop_id", "shop_by_shop_id", "potion_id", "potion_by_potion_id", "stock")

    def __init__(self):
        self.index = 0
        self.shop_id = 0
        self.shop_by_shop_id = None
        self.potion_id = 0
        self.potion_by_potion_id = None
        self.stock = 0

    def __repr__(self):
        return "ShopEntryRecord(index=%r, shop_id=%r, potion_id=%r, stock=%r)" % (self.index, self.shop_id, self.potion_id, self.stock)
```

[shop_entry_table.py](../../test/fixtures/golden/doc-showcase/python/doc_showcase_data/shop_entry_table.py)

</details>
<details data-lang="c">
<summary>C</summary>

```c
/* Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
 *
 * What each shop sells.
 */
struct DocShowcase_ShopEntryRecord_t {
  /* primary index */
  int32_t index;
  /* which shop */
  int32_t shop_id;
  const DocShowcase_ShopRecord_t* shop_by_shop_id;
  /* which potion */
  int32_t potion_id;
  const DocShowcase_PotionRecord_t* potion_by_potion_id;
  /* how many */
  int32_t stock;
};
```

[DocShowcase_ShopEntry.h](../../test/fixtures/golden/doc-showcase/c/tables/DocShowcase_ShopEntry.h)

</details>
<details data-lang="dart">
<summary>Dart</summary>

```dart
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/// What each shop sells.
class ShopEntryRecord {
  /// primary index
  int index = 0;
  /// which shop
  int shopId = 0;
  ShopRecord? shopByShopId;
  /// which potion
  int potionId = 0;
  PotionRecord? potionByPotionId;
  /// how many
  int stock = 0;

}
```

[shop_entry_table.dart](../../test/fixtures/golden/doc-showcase/dart/tables/shop_entry_table.dart)

</details>
<details data-lang="go">
<summary>Go</summary>

```go
// ShopEntryRecord was generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2.
// What each shop sells.
type ShopEntryRecord struct {
	// primary index
	Index int32
	// which shop
	ShopId       int32
	ShopByShopId *ShopRecord
	// which potion
	PotionId         int32
	PotionByPotionId *PotionRecord
	// how many
	Stock int32
}
```

[shop_entry_table.go](../../test/fixtures/golden/doc-showcase/go/shop_entry_table.go)

</details>
<details data-lang="java">
<summary>Java</summary>

```java
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/** What each shop sells. */
public final class ShopEntryRecord {
    /** primary index */
    public int index;
    /** which shop */
    public int shopId;
    public ShopRecord shopByShopId;
    /** which potion */
    public int potionId;
    public PotionRecord potionByPotionId;
    /** how many */
    public int stock;

}
```

[ShopEntryRecord.java](../../test/fixtures/golden/doc-showcase/java/tabbit/fixtures/docshowcase/ShopEntryRecord.java)

</details>
<details data-lang="kotlin">
<summary>Kotlin</summary>

```kotlin
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/** What each shop sells. */
class ShopEntryRecord {
    /** primary index */
    var index: Int = 0
    /** which shop */
    var shopId: Int = 0
    var shopByShopId: ShopRecord? = null
    /** which potion */
    var potionId: Int = 0
    var potionByPotionId: PotionRecord? = null
    /** how many */
    var stock: Int = 0

}
```

[ShopEntryTable.kt](../../test/fixtures/golden/doc-showcase/kotlin/tabbit/fixtures/docshowcase/tables/ShopEntryTable.kt)

</details>
<details data-lang="lua">
<summary>Lua</summary>

```lua
-- Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2.
-- What each shop sells.
---@class ShopEntryRecord
---@field index integer
---@field shopId integer
---@field shopByShopId ShopRecord|nil
---@field potionId integer
---@field potionByPotionId PotionRecord|nil
---@field stock integer
local ShopEntryRecordMeta = tcb.strictType("a `ShopEntry` row", { "index", "shopId", "shopByShopId", "potionId", "potionByPotionId", "stock" })
```

[shop_entry_table.lua](../../test/fixtures/golden/doc-showcase/lua/tables/shop_entry_table.lua)

</details>
<details data-lang="php">
<summary>PHP</summary>

```php
/**
 * Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
 *
 * What each shop sells.
 */
final class ShopEntryRecord
{
    /** primary index */
    public int $index = 0;
    /** which shop */
    public int $shopId = 0;

    public ?ShopRecord $shopByShopId = null;
    /** which potion */
    public int $potionId = 0;

    public ?PotionRecord $potionByPotionId = null;
    /** how many */
    public int $stock = 0;
}
```

[ShopEntryTable.php](../../test/fixtures/golden/doc-showcase/php/tables/ShopEntryTable.php)

</details>
<details data-lang="ruby">
<summary>Ruby</summary>

```ruby
# Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
# What each shop sells.
class ShopEntryRecord
  attr_accessor :index, :shop_id, :shop_by_shop_id, :potion_id, :potion_by_potion_id, :stock

  def initialize
    @index = 0
    @shop_id = 0
    @shop_by_shop_id = nil
    @potion_id = 0
    @potion_by_potion_id = nil
    @stock = 0
  end
```

[shop_entry_table.rb](../../test/fixtures/golden/doc-showcase/ruby/tables/shop_entry_table.rb)

</details>
<details data-lang="rust">
<summary>Rust</summary>

```rust
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/// What each shop sells.
#[derive(Clone, Debug, Default)]
pub struct ShopEntryRecord {
    /// primary index
    pub index: i32,
    /// which shop
    pub shop_id: i32,
    /// which potion
    pub potion_id: i32,
    /// how many
    pub stock: i32,
}
```

[shop_entry_table.rs](../../test/fixtures/golden/doc-showcase/rust/src/shop_entry_table.rs)

</details>
<details data-lang="swift">
<summary>Swift</summary>

```swift
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/// What each shop sells.
public final class ShopEntryRecord {

    public init() {}

    /// primary index
    public var index: Int32 = 0

    /// which shop
    public var shopId: Int32 = 0
    public var shopByShopId: ShopRecord? = nil

    /// which potion
    public var potionId: Int32 = 0
    public var potionByPotionId: PotionRecord? = nil

    /// how many
    public var stock: Int32 = 0
}
```

[ShopEntryTable.swift](../../test/fixtures/golden/doc-showcase/swift/tables/ShopEntryTable.swift)

</details>
<details data-lang="unreal">
<summary>Unreal</summary>

```cpp
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Shop : F2
/** What each shop sells. */
USTRUCT(BlueprintType)
struct DOCSHOWCASE_API FShopEntryRow
{
    GENERATED_BODY()

    /** primary index */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "ShopEntry")
    int32 Index = 0;

    /** which shop */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "ShopEntry")
    int32 ShopId = 0;

    /** which potion */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "ShopEntry")
    int32 PotionId = 0;

    /** how many */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "ShopEntry")
    int32 Stock = 0;

};
```

[FDocShowcase.h](../../test/fixtures/golden/doc-showcase/unreal/Source/DocShowcase/Public/FDocShowcase.h)

</details>
<!-- /tabbit:tabs -->

<!-- /tabbit:pair -->

**컬럼 하나가 멤버 둘이 됩니다.**

- `ShopId` — 셀에 적힌 키 그대로입니다
- `ShopByShopId` — 그 키가 가리키는 행입니다

이름은 `<대상>By<컬럼>` 으로 만들어집니다
([참조가 내는 이름](../../spec/references/reference-surface-naming.md)).

파일에는 키로 저장되고, 모든 테이블을 읽은 뒤에 실제 레코드로 연결됩니다. **그래서 가리킨
행의 값을 쓸 때 조회를 한 번 더 하지 않습니다** — `entry.ShopByShopId.Name` 이 곧 상점
이름입니다.
