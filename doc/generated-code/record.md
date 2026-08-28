# 컬럼 묶음과 빈 칸

<!-- 이 파일은 `doc/figures/showcase.py` 가 생성합니다. 손으로 고치지 마십시오 - 다음 실행이 덮어씁니다. -->

> [생성되는 코드로](../generated-code.md)

---

`At.X` · `At.Y` 처럼 **점 앞이 같은 컬럼들은 한 레코드**가 됩니다. 시트에서는
여전히 컬럼 둘이고, 코드에서는 멤버 둘을 가진 타입 하나입니다.

타입 뒤의 `?` 는 그 칸을 비워도 된다는 뜻이고, 비우는 방법은 `-` 입니다.

<!-- tabbit:pair -->

![테이블 Spawn](../figures/showcase-spawn.svg)

<!-- tabbit:tabs lang -->
<details data-lang="csharp" open>
<summary>C#</summary>

```csharp
[System.Serializable]
public partial class SpawnRecord
{
    #region Values
    /// <summary>
    /// primary index
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// x position
    /// </summary>
    public AtEntry At => _at;

    /// <summary>
    /// radius, or blank
    /// </summary>
    public float Radius => _radius;
    /// <summary>Whether this row has a value for <see cref="Radius"/>.</summary>
    public bool HasRadius => _radiusHasValue;
    #endregion

    /// <summary>One element of <see cref="At"/>.</summary>
    [System.Serializable]
    public struct AtEntry
    {
        /// x position
        public float X;
        /// y position
        public float Y;

        public override string ToString()
        {
            var sb = new StringBuilder("{");
            sb.Append("\"X\":"); ToStringHelper.ToString(X, sb);
            sb.Append(",\"Y\":"); ToStringHelper.ToString(Y, sb);
            sb.Append("}");
            return sb.ToString();
        }
    }

    #region Storage
    internal int _index;
    internal AtEntry _at;
    internal float _radius;
    internal bool _radiusHasValue;
    #endregion

    #region ToString
    public override string ToString()
    {
        var sb = new StringBuilder("{");
        sb.Append("\"Index\":"); ToStringHelper.ToString(Index, sb);
        sb.Append(",\"At\":"); ToStringHelper.ToString(At, sb);
        sb.Append(",\"Radius\":"); ToStringHelper.ToString(Radius, sb);
        sb.Append("}");
        return sb.ToString();
    }
    #endregion
}
```

[SpawnTable.cs](../../test/fixtures/golden/doc-showcase/csharp/tables/SpawnTable.cs)

</details>
<details data-lang="typescript">
<summary>TypeScript</summary>

```typescript
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
/** Two columns as one record. */
export class SpawnRecord {
  /** Default constructor */
  constructor() {
  }

  /** primary index */
  public get index(): number { return this._index }

  /** x position */
  public get at(): AtEntry { return this._at }

  /** radius, or blank */
  public get radius(): number { return this._radius }
  /** Whether this row has a value for `radius`. */
  public get hasRadius(): boolean { return this._radiusHasValue }

  public _index: number = 0
  public _at: AtEntry = { x: 0, y: 0 }
  public _radius: number = 0
  public _radiusHasValue: boolean = false

  /** Populate field values. */
  public populateFieldValues(dataRow: IDataRow): void {
    this._index = dataRow.index
    this._at = ((e: any) => ({ x: Math.fround(e.x), y: Math.fround(e.y) }))(dataRow.at)
    this._radiusHasValue = dataRow.radius !== null && dataRow.radius !== undefined; if (this._radiusHasValue) this._radius = Math.fround(dataRow.radius)
  }

  /** Populate field values. */
  public populateFieldValuesCompact(dataRow: any[]): void {
    let offset = 0
    this._index = dataRow[offset++]
    this._at = { x: Math.fround(dataRow[offset++]), y: Math.fround(dataRow[offset++]) }
    const _radius_raw = dataRow[offset++]
    this._radiusHasValue = _radius_raw !== null && _radius_raw !== undefined
    if (this._radiusHasValue) this._radius = Math.fround(_radius_raw)
  }
}
```

[spawn.ts](../../test/fixtures/golden/doc-showcase/typescript/tables/spawn.ts)

</details>
<details data-lang="cpp">
<summary>C++</summary>

```cpp
/// Two columns as one record.
struct SpawnRecord {
  /// primary index
  std::int32_t index = 0;
  /// x position
  SpawnRecord_at_entry at;
  /// radius, or blank
  float radius = 0.0f;
  /// Whether this row has a value for `radius`.
  bool has_radius = false;
};
```

[DocShowcaseAccessor_spawn.h](../../test/fixtures/golden/doc-showcase/cpp/tables/DocShowcaseAccessor_spawn.h)

</details>
<details data-lang="python">
<summary>Python</summary>

```python
class SpawnRecord:
    """Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2.

    Two columns as one record.
    """

    __slots__ = ("index", "at", "radius", "has_radius")

    def __init__(self):
        self.index = 0
        self.at = SpawnAtEntry()
        self.radius = 0.0
        self.has_radius = False

    def __repr__(self):
        return "SpawnRecord(index=%r, at=%r, radius=%r)" % (self.index, self.at, self.radius)
```

[spawn_table.py](../../test/fixtures/golden/doc-showcase/python/doc_showcase_data/spawn_table.py)

</details>
<details data-lang="c">
<summary>C</summary>

```c
struct DocShowcase_SpawnRecord_t {
  /* primary index */
  int32_t index;
  /* x position */
  struct DocShowcase_SpawnRecord_t_at_entry at;
  /* radius, or blank */
  float radius;
  /* Whether this row has a value for radius. The value member keeps its type and
   * holds the type's empty value when the row had none; this says which it was. */
  bool has_radius;
};
```

[DocShowcase_Spawn.h](../../test/fixtures/golden/doc-showcase/c/tables/DocShowcase_Spawn.h)

</details>
<details data-lang="dart">
<summary>Dart</summary>

```dart
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
/// Two columns as one record.
class SpawnRecord {
  /// primary index
  int index = 0;
  /// x position
  SpawnAtEntry at = SpawnAtEntry();
  /// radius, or blank
  double radius = 0.0;
  bool hasRadius = false;

}
```

[spawn_table.dart](../../test/fixtures/golden/doc-showcase/dart/tables/spawn_table.dart)

</details>
<details data-lang="go">
<summary>Go</summary>

```go
// SpawnRecord was generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2.
// Two columns as one record.
type SpawnRecord struct {
	// primary index
	Index int32
	// x position
	At SpawnAtEntry
	// radius, or blank
	Radius float32
	// Whether this row has a value for Radius. The value member keeps its type
	// and holds the type's empty value when the row had none; this says which it was.
	HasRadius bool
}
```

[spawn_table.go](../../test/fixtures/golden/doc-showcase/go/spawn_table.go)

</details>
<details data-lang="java">
<summary>Java</summary>

```java
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
/** Two columns as one record. */
public final class SpawnRecord {
    /** primary index */
    public int index;
    /** x position */
    public AtEntry at = new AtEntry();
    /** radius, or blank */
    public float radius;
    /**
     * Whether this row has a value for radius. The value field keeps its type and
     * holds the type's empty value when the row had none; this says which it was.
     */
    public boolean hasRadius;

    /** One element of at. */
    public static final class AtEntry {
        /** x position */
        public float x;
        /** y position */
        public float y;
    }

}
```

[SpawnRecord.java](../../test/fixtures/golden/doc-showcase/java/tabbit/fixtures/docshowcase/SpawnRecord.java)

</details>
<details data-lang="kotlin">
<summary>Kotlin</summary>

```kotlin
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
/** Two columns as one record. */
class SpawnRecord {
    /** primary index */
    var index: Int = 0
    /** x position */
    var at: AtEntry = AtEntry()
    /** radius, or blank */
    var radius: Float = 0.0f
    /**
     * Whether this row has a value for radius. The value property keeps its type
     * and holds the type's empty value when the row had none; this says which it was.
     */
    var hasRadius: Boolean = false

    /** One element of at. */
    class AtEntry {
        /** x position */
        var x: Float = 0.0f
        /** y position */
        var y: Float = 0.0f
    }

}
```

[SpawnTable.kt](../../test/fixtures/golden/doc-showcase/kotlin/tabbit/fixtures/docshowcase/tables/SpawnTable.kt)

</details>
<details data-lang="lua">
<summary>Lua</summary>

```lua
-- Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2.
-- Two columns as one record.
---@class SpawnRecord
---@field index integer
---@field at SpawnAtEntry
---@field radius number
---@field hasRadius boolean
local SpawnRecordMeta = tcb.strictType("a `Spawn` row", { "index", "at", "radius", "hasRadius" })
```

[spawn_table.lua](../../test/fixtures/golden/doc-showcase/lua/tables/spawn_table.lua)

</details>
<details data-lang="php">
<summary>PHP</summary>

```php
/**
 * Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
 *
 * Two columns as one record.
 */
final class SpawnRecord
{
    /** primary index */
    public int $index = 0;
    /** x position */
    public SpawnAtEntry $at;
    /** radius, or blank */
    public float $radius = 0.0;

    public bool $hasRadius = false;


    /**
     * A row with its record groups built.
     *
     * They cannot be built at the declaration: a PHP property initializer has to be a
     * constant expression, and `new SlotEntry()` is not one.
     */
    public function __construct()
    {
        $this->at = new SpawnAtEntry();
    }
}
```

[SpawnTable.php](../../test/fixtures/golden/doc-showcase/php/tables/SpawnTable.php)

</details>
<details data-lang="ruby">
<summary>Ruby</summary>

```ruby
# Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
# Two columns as one record.
class SpawnRecord
  attr_accessor :index, :at, :radius, :has_radius

  def initialize
    @index = 0
    @at = SpawnAtEntry.new
    @radius = 0.0
    @has_radius = false
  end
```

[spawn_table.rb](../../test/fixtures/golden/doc-showcase/ruby/tables/spawn_table.rb)

</details>
<details data-lang="rust">
<summary>Rust</summary>

```rust
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
/// Two columns as one record.
#[derive(Clone, Debug, Default)]
pub struct SpawnRecord {
    /// primary index
    pub index: i32,
    /// x position
    pub at: SpawnAtEntry,
    /// radius, or blank
    pub radius: f32,
    /// Whether this row has a value for `radius`. The value member keeps its type
    /// and holds the type's empty value when the row had none; this says which it was.
    pub has_radius: bool,
}
```

[spawn_table.rs](../../test/fixtures/golden/doc-showcase/rust/src/spawn_table.rs)

</details>
<details data-lang="swift">
<summary>Swift</summary>

```swift
// Generated from test/fixtures/xlsx/doc-showcase/doc-showcase.xlsx : Spawn : B2
/// Two columns as one record.
public final class SpawnRecord {

    public init() {}

    /// primary index
    public var index: Int32 = 0

    /// x position
    public var at: AtEntry = AtEntry()

    /// radius, or blank
    public var radius: Float = 0
    /// Whether this row has a value for radius. The value property keeps its type
    /// and holds the type's empty value when the row had none; this says which it was.
    public var hasRadius: Bool = false

    /// One element of at.
    public struct AtEntry {

        public init() {}

        /// x position
        public var x: Float = 0

        /// y position
        public var y: Float = 0
    }
}
```

[SpawnTable.swift](../../test/fixtures/golden/doc-showcase/swift/tables/SpawnTable.swift)

</details>
<details data-lang="unreal">
<summary>Unreal</summary>

```cpp
/** Two columns as one record. */
USTRUCT(BlueprintType)
struct DOCSHOWCASE_API FSpawnRow
{
    GENERATED_BODY()

    /** primary index */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Spawn")
    int32 Index = 0;

    /** x position */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Spawn")
    FSpawnAtEntry At;

    /** radius, or blank */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Spawn")
    float Radius = 0.0f;

    /** Whether this row has a value for Radius. */
    UPROPERTY(EditAnywhere, BlueprintReadOnly, Category = "Spawn")
    bool bHasRadius = false;
};
```

[FDocShowcase.h](../../test/fixtures/golden/doc-showcase/unreal/Source/DocShowcase/Public/FDocShowcase.h)

</details>
<!-- /tabbit:tabs -->

<!-- /tabbit:pair -->

**중첩은 파일에 아무 값도 더하지 않습니다.** 레코드는 멤버마다 컬럼 하나로
저장되므로, `At.X` 와 `At.Y` 를 따로 적었을 때와 파일의 바이트가 같습니다. 달라지는 것은
코드를 읽는 쪽의 모습뿐입니다.

`?` 가 붙은 컬럼은 언어마다 그 언어의 「없음」으로 나옵니다 — 옵셔널 타입이 있는 언어는 그것을
쓰고, 없는 언어는 값이 있는지 묻는 방법을 따로 냅니다.
