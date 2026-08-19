using System;
using System.IO;
using NPOI.XSSF.UserModel;

namespace Tabbit.FixtureGen;

/// <summary>
/// Generates the .xlsx fixtures used by the regression tests.
///
/// The fixtures are committed to the repo; this generator exists so they can be
/// reviewed as code and regenerated deterministically instead of being opaque
/// binaries nobody dares to touch.
///
///     dotnet run --project test/fixtures/tools/FixtureGen
///
/// Fixtures are split by intent:
///
///   core.xlsx         Everything that works today. Its generated output is the
///                     golden baseline that the port must not change.
///   excel-typed.xlsx  Cells that carry real Excel types (numeric, date) rather
///                     than strings. Exercises importer behaviour that string
///                     cells hide.
///   layout-edge.xlsx  Sheets with leading blank rows/columns, ragged rows and
///                     interior blank rows, which drive RawSheet.Optimize.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string outputDir = args.Length > 0
            ? args[0]
            : Path.Combine(FindRepoRoot(), "test", "fixtures", "xlsx");

        // One directory per scenario: XlsxImporter scans its source path
        // recursively, so fixtures sharing a directory would bleed into each
        // other's runs.
        WriteCore(Prepare(outputDir, "core", "core.xlsx"));
        WriteExcelTyped(Prepare(outputDir, "excel-typed", "excel-typed.xlsx"));
        WriteLayoutEdge(Prepare(outputDir, "layout-edge", "layout-edge.xlsx"));
        WriteForeignFieldRef(Prepare(outputDir, "foreign-field", "foreign-field.xlsx"));
        WriteInvalid(Prepare(outputDir, "invalid", "invalid.xlsx"));
        WriteSideDangling(Prepare(outputDir, "side-dangling", "side-dangling.xlsx"));
        WriteArrayForeign(Prepare(outputDir, "array-foreign", "array-foreign.xlsx"));
        WriteStrictValues(Prepare(outputDir, "strict-values", "strict-values.xlsx"));
        WriteDoubleStar(Prepare(outputDir, "double-star", "double-star.xlsx"));
        WriteNested(Prepare(outputDir, "nested", "nested.xlsx"));
        WriteNestedHole(Prepare(outputDir, "nested-hole", "nested-hole.xlsx"));
        WriteNestedDeep(Prepare(outputDir, "nested-deep", "nested-deep.xlsx"));
        WriteRecordTrim(Prepare(outputDir, "record-trim", "record-trim.xlsx"));
        WriteStringIndex(Prepare(outputDir, "string-index", "string-index.xlsx"));
        WriteReferenceKeys(Prepare(outputDir, "reference-keys", "reference-keys.xlsx"));
        WriteRecordRef(Prepare(outputDir, "record-ref", "record-ref.xlsx"));
        WriteRecordRefTrim(Prepare(outputDir, "record-ref-trim", "record-ref-trim.xlsx"));
        WriteKeyTypes(Prepare(outputDir, "key-types", "key-types.xlsx"));
        WriteOptional(Prepare(outputDir, "optional", "optional.xlsx"));
        WriteBlankAndNull(Prepare(outputDir, "blank-and-null", "blank-and-null.xlsx"));
        WriteBlankCell(Prepare(outputDir, "blank-cell", "blank-cell.xlsx"));
        WriteNoValueRefused(Prepare(outputDir, "no-value-refused", "no-value-refused.xlsx"));
        WriteNoValueElement(Prepare(outputDir, "no-value-element", "no-value-element.xlsx"));
        WriteOptionalIndex(Prepare(outputDir, "optional-index", "optional-index.xlsx"));
        WriteFormulaError(Prepare(outputDir, "formula-error", "formula-error.xlsx"));
        WriteEnumByValue(Prepare(outputDir, "enum-by-value", "enum-by-value.xlsx"));
        WriteReservedWords(Prepare(outputDir, "reserved-words", "reserved-words.xlsx"));
        WriteText(Prepare(outputDir, "text", "text.xlsx"));
        WriteBitset(Prepare(outputDir, "bitset", "bitset.xlsx"));
        WriteReferenceOptional(
            Prepare(outputDir, "reference-optional", "reference-optional.xlsx"));
        WriteReferenceRequiredBlank(
            Prepare(outputDir, "reference-required-blank", "reference-required-blank.xlsx"));
        WriteAsset(Prepare(outputDir, "asset", "asset.xlsx"));
        WriteRowSets(Prepare(outputDir, "row-sets", "row-sets.xlsx"));
        // The corpus and the corpus one generation later, from one description. The skew
        // scenario is the same tables with a column appended, and the only thing the gate
        // asks of it is that nothing else differs - so nothing else may be maintained
        // separately.
        WriteConformance(Prepare(outputDir, "conformance", "conformance.xlsx"), skewed: false);
        WriteConformance(
            Prepare(outputDir, "conformance-skew", "conformance-skew.xlsx"), skewed: true);

        // The same three tables at two points in their history. Read across the pair -
        // one generation's code against the other's data - and every kind of schema
        // change gets an answer: the right value, the default, or a refusal by name.
        WriteEvolution(Prepare(outputDir, "evolution-v1", "evolution-v1.xlsx"), second: false);
        WriteEvolution(Prepare(outputDir, "evolution-v2", "evolution-v2.xlsx"), second: true);

        Console.WriteLine($"Fixtures written to {outputDir}");
        return 0;
    }

    private static string Prepare(string outputDir, string scenario, string filename)
    {
        string dir = Path.Combine(outputDir, scenario);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, filename);
    }

    // ---------------------------------------------------------------- core

    private static void WriteCore(string path)
    {
        var workbook = new XSSFWorkbook();

        // --- Enums -------------------------------------------------------

        var enums = new SheetBuilder(workbook.CreateSheet("Enums"));

        // Declares its own 0 entry, so Tabbit leaves the label list alone.
        enums.Enum(1, 1, new EnumSpec { Name = "ValueType", Comment = "Value types used by the test tables." }
            .Label("None", "0", "no value")
            .Label("Int32", "1", "32 bit integer")
            .Label("Int64", "2", "64 bit integer")
            .Label("Float", "3", "single precision float"));

        // No `None` and no 0 entry, so Tabbit auto-inserts one. Covers
        // ModelCooker.ParseEnum's implicit-label path.
        enums.Enum(6, 1, new EnumSpec { Name = "Grade", Comment = "Item grade. Deliberately omits a zero entry." }
            .Label("Common", "1", "common grade")
            .Label("Rare", "2", "rare grade")
            .Label("Epic", "3", "epic grade"));

        // Labels declared in snake_case. They are stored Pascal-cased, so a data
        // cell repeating the declared spelling has to resolve back to them.
        enums.Enum(11, 1, new EnumSpec { Name = "SkillType", Comment = "Declared in snake_case on purpose." }
            .Label("none", "0", "no skill")
            .Label("fire_ball", "1", "throws a fireball")
            .Label("ice_shard", "2", "throws an ice shard"));

        // --- All primitive field types -----------------------------------

        var types = new SheetBuilder(workbook.CreateSheet("Types"));

        var testFieldTypes = new TableSpec
        {
            Name = "TestFieldTypes",
            Comment = "One column per supported primitive type.",
        };
        testFieldTypes
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("StringField", "string", "utf8 text"))
            .Field(FieldSpec.Of("BoolField", "bool", "logical flag", targetSide: "c"))
            .Field(FieldSpec.Of("IntField", "int", "32 bit integer", targetSide: "s"))
            .Field(FieldSpec.Of("BigIntField", "bigint", "64 bit integer"))
            .Field(FieldSpec.Of("FloatField", "float", "single precision"))
            .Field(FieldSpec.Of("DoubleField", "double", "double precision"))
            .Field(FieldSpec.Of("DatetimeField", "datetime", "date and time"))
            .Field(FieldSpec.Of("TimespanField", "timespan", "time interval"))
            .Field(FieldSpec.Of("UuidField", "uuid", "globally unique id"))
            .Field(FieldSpec.Of("ValueTypeField", "enum", "enum reference", detailType: "ValueType"))
            // A field commented out with `#` keeps its column but is dropped from
            // the model, so the data cells below it are never parsed.
            .Field(FieldSpec.Of("#IgnoredField", "string", "should not appear in output"));

        testFieldTypes
            .Row("1", "first", "Y", "1,024", "9007199254740993", "1.5", "2.25", "2022-01-24 10:30:00", "1.02:03:04", "7b7d9f6a-1e2c-4c1a-9a5f-2b6d0c3e4f51", "Int32", "junk")
            .Row("2", "second", "N", "-20", "-9007199254740993", "-0.5", "1e-8", "1999-12-31 23:59:59", "00:00:01", "0f8fad5b-d9cb-469f-a165-70867728950e", "Float", "junk")
            // Empty string and empty bool are both legal: bool treats blank as false.
            .Row("3", "", "", "0", "0", "0", "0", "2000-01-01 00:00:00", "00:00:00", "00000000-0000-0000-0000-000000000000", "None", "junk");

        types.Table(1, 1, testFieldTypes);

        // --- Cross-table references --------------------------------------

        var refs = new SheetBuilder(workbook.CreateSheet("Refs"));

        var itemCategory = new TableSpec
        {
            Name = "ItemCategory",
            Comment = "Referenced by Item.CategoryId.",
        };
        itemCategory
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "category name"))
            .Field(FieldSpec.Of("Description", "string", "human readable description"));
        itemCategory
            .Row("1", "Weapon", "things that hit")
            .Row("2", "Armor", "things that absorb")
            .Row("3", "Potion", "things that heal");

        refs.Table(1, 1, itemCategory);

        var item = new TableSpec
        {
            Name = "Item",
            Comment = "References ItemCategory by record.",
        };
        item
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "item name"))
            // `foreign` with a bare table name resolves to that table's record.
            .Field(FieldSpec.Of("CategoryId", "foreign", "owning category", detailType: "ItemCategory"))
            .Field(FieldSpec.Of("GradeField", "enum", "item grade", detailType: "Grade"))
            // Cells below spell these the way the enum was declared, in snake_case.
            .Field(FieldSpec.Of("SkillField", "enum", "granted skill", detailType: "SkillType"))
            // Free text as a designer would actually write it, including the
            // characters that have to be escaped before reaching the HTML docs.
            .Field(FieldSpec.Of("Description", "string", "shop blurb"))
            .Field(FieldSpec.Of("Price", "int", "shop price", targetSide: "s"));
        item
            .Row("1", "Short Sword", "1", "Common", "fire_ball", "Sharp & quick; deals <b>bonus</b> damage", "100")
            .Row("2", "Leather Armor", "2", "Rare", "ice_shard", "Blocks 5% of \"physical\" hits", "250")
            .Row("3", "Small Potion", "3", "Epic", "none", "Restores 10 HP <or> 5 MP", "50");

        // Placed well clear of ItemCategory: the rect scanner grows rightward
        // through non-empty cells, so neighbours need a blank gutter.
        refs.Table(8, 1, item);

        // --- Serial fields -------------------------------------------------

        var serial = new SheetBuilder(workbook.CreateSheet("Serial"));

        var localization = new TableSpec
        {
            Name = "Localization",
            Comment = "Trailing-number columns collapse into arrays.",
        };
        localization
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Key", "string", "lookup key"))
            .Field(FieldSpec.Of("TextEn1", "string", "english text 1"))
            .Field(FieldSpec.Of("TextEn2", "string", "english text 2"))
            .Field(FieldSpec.Of("TextKo1", "string", "korean text 1"))
            .Field(FieldSpec.Of("TextKo2", "string", "korean text 2"));
        localization
            .Row("1", "greeting", "Hello", "Hi", "안녕하세요", "안녕")
            .Row("2", "farewell", "Goodbye", "Bye", "안녕히가세요", "잘가");

        serial.Table(1, 1, localization);

        // --- Delimited array cells -------------------------------------------

        var arrays = new SheetBuilder(workbook.CreateSheet("Arrays"));

        var arrayTable = new TableSpec
        {
            Name = "ArrayTypes",
            Comment = "One cell holding several delimited values, length varying per row.",
        };
        arrayTable
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Tags", "string[]", "free-form tags"))
            .Field(FieldSpec.Of("Costs", "int[]", "cost per level"))
            .Field(FieldSpec.Of("Weights", "float[]", "drop weights"))
            .Field(FieldSpec.Of("Grades", "enum[]", "allowed grades", detailType: "Grade"))
            // A serial field alongside the delimited ones: the two array kinds use
            // different wire formats and must not disturb each other.
            .Field(FieldSpec.Of("Slot1", "int", "fixed slot 1"))
            .Field(FieldSpec.Of("Slot2", "int", "fixed slot 2"));
        arrayTable
            .Row("1", "red;green;blue", "10;20;30", "0.5;0.25", "Common;Rare", "1", "2")
            // A different length in every row, which is the point of the feature.
            .Row("2", "solo", "5", "1.0;2.0;3.0;4.0", "Epic", "3", "4")
            // Empty cells are empty arrays, not errors: a row with nothing to say
            // for the column is ordinary.
            .Row("3", "", "", "", "", "5", "6")
            // Whitespace around elements is trimmed.
            .Row("4", "a; b ;c", "1; 2", "0.1", "Common; Epic", "7", "8");

        arrays.Table(1, 1, arrayTable);

        // --- Entity-level target sides ---------------------------------------

        var sides = new SheetBuilder(workbook.CreateSheet("Sides"));

        // Whole entities marked for one side. These disappear entirely from output
        // built for the other side, while the per-field markers on TestFieldTypes
        // and Item exercise column-level filtering.
        var serverOnly = new TableSpec
        {
            Name = "ServerTuning",
            Comment = "Server-only table. Must not appear in client output.",
            TargetSide = "s",
        };
        serverOnly
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Key", "string", "tuning key"))
            .Field(FieldSpec.Of("Amount", "int", "tuning amount"));
        serverOnly
            .Row("1", "spawn_rate", "35")
            .Row("2", "loot_bias", "12");

        sides.Table(1, 1, serverOnly);

        var clientOnly = new TableSpec
        {
            Name = "ClientStrings",
            Comment = "Client-only table. Must not appear in server output.",
            TargetSide = "c",
        };
        clientOnly
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Key", "string", "string key"))
            .Field(FieldSpec.Of("Text", "string", "display text"));
        clientOnly
            .Row("1", "ui.ok", "OK")
            .Row("2", "ui.cancel", "Cancel");

        sides.Table(6, 1, clientOnly);

        // --- Constants -----------------------------------------------------

        var consts = new SheetBuilder(workbook.CreateSheet("Consts"));

        consts.Const(1, 1, new ConstSpec { Name = "GameConfig", Comment = "Assorted tuning constants." }
            .Constant("MaxLevel", "int", "100", "level cap")
            .Constant("StartGold", "bigint", "1000", "gold granted to new accounts")
            .Constant("DropRate", "float", "0.25", "base drop rate")
            .Constant("DebugMode", "bool", "N", "whether debug hooks are active")
            .Constant("DefaultGrade", "enum", "Rare", "grade assigned when unspecified", detailType: "Grade")
            // The three types a constant could not previously be written in: the C#
            // generator emitted their default ToString, which is not a literal, so a
            // sheet declaring one of these produced a file that would not compile.
            .Constant("SeasonStart", "datetime", "2022-03-01 09:00:00", "when the season opens")
            .Constant("RoundLength", "timespan", "0.00:05:00", "length of one round")
            .Constant("BuildId", "uuid", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "identifies this data build"));

        Save(workbook, path);
    }

    // ---------------------------------------------------- excel-typed cells

    private static void WriteExcelTyped(string path)
    {
        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Typed");
        var b = new SheetBuilder(sheet);

        var spec = new TableSpec
        {
            Name = "ExcelTyped",
            Comment = "Values entered as real Excel types rather than text.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("IntFromNumeric", "int", "numeric cell holding an integer"))
            .Field(FieldSpec.Of("FloatFromNumeric", "float", "numeric cell holding a fraction"))
            .Field(FieldSpec.Of("WhenFromDateCell", "datetime", "genuine Excel date cell"))
            .Field(FieldSpec.Of("BigFromNumeric", "bigint", "numeric cell beyond double precision"));

        b.Table(1, 1, spec);

        // Header block occupies rows 1..7 (marker, comment, 5 header rows), so the
        // first data row is row 8. Written cell by cell because these must carry
        // real Excel types, which the string-based TableSpec.Row cannot express.
        int row = 8;

        b.SetNumeric(1, row, 1);
        b.SetNumeric(2, row, 42);
        b.SetNumeric(3, row, 1.5);
        b.SetDate(4, row, new DateTime(2022, 1, 24, 10, 30, 0));
        b.SetNumeric(5, row, 9007199254740993d);
        row++;

        b.SetNumeric(1, row, 2);
        b.SetNumeric(2, row, -7);
        b.SetNumeric(3, row, 0.1);
        b.SetDate(4, row, new DateTime(1999, 12, 31, 23, 59, 59));
        b.SetNumeric(5, row, 1e16);

        Save(workbook, path);
    }

    // ------------------------------------------------------- layout edges

    private static void WriteLayoutEdge(string path)
    {
        var workbook = new XSSFWorkbook();

        // Entity pushed down and right, so RawSheet.Optimize has leading blank
        // rows and columns to trim before anything else can run.
        var offset = new SheetBuilder(workbook.CreateSheet("Offset"));

        var spec = new TableSpec
        {
            Name = "OffsetTable",
            Comment = "Starts at F9 rather than the top-left corner.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "name"))
            .Field(FieldSpec.Of("Value", "int", "value"));
        spec
            .Row("1", "alpha", "10")
            .Row("2", "beta", "20");

        int afterFirst = offset.Table(5, 8, spec);

        // A blank row that is genuinely ragged, sitting *between* two entities.
        //
        // RawSheet.Optimize trims blank rows only at the top and bottom, so an
        // interior one survives into the column scan. Its cells stop at column 2
        // while the entity rows reach column 7, and every column left of the
        // entity is empty. IsWholeEmptyColumn therefore walks to column 4 looking
        // for content and indexes past the end of this row - it runs before the
        // padding pass that would have squared the sheet off.
        //
        // Written as explicit empty cells because a row with no cells at all is
        // not emitted to the .xlsx, and NPOI would simply skip it on import.
        int raggedRow = afterFirst + 1;
        offset.Set(0, raggedRow, "");
        offset.Set(1, raggedRow, "");
        offset.Set(2, raggedRow, "");

        // A second entity below keeps the ragged row interior rather than trailing.
        var second = new TableSpec
        {
            Name = "SecondTable",
            Comment = "Keeps the ragged row from being trimmed as a trailing blank.",
        };
        second
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Label", "string", "label"))
            .Field(FieldSpec.Of("Amount", "int", "amount"));
        second
            .Row("1", "gamma", "30");

        offset.Table(5, raggedRow + 2, second);

        Save(workbook, path);
    }

    // ------------------------------------------- foreign `Table.Field` form

    /// <summary>
    /// The documented `RefTable.RefFieldName` form of a foreign detail type.
    ///
    /// ModelCooker slices the table name with `Substring(0, dot - 1)`, dropping its
    /// last character, so this shape cannot resolve today. Kept in its own fixture
    /// so the rest of the suite still has a workbook that converts.
    /// </summary>
    private static void WriteForeignFieldRef(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Refs"));

        var category = new TableSpec
        {
            Name = "ItemCategory",
            Comment = "Target of the field-level reference below.",
        };
        category
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "category name"))
            .Field(FieldSpec.Of("Description", "string", "description"));
        category
            .Row("1", "Weapon", "things that hit")
            .Row("2", "Armor", "things that absorb");

        b.Table(1, 1, category);

        var item = new TableSpec
        {
            Name = "Item",
            Comment = "References a specific field rather than the whole record.",
        };
        item
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "item name"))
            // Resolves to ItemCategory.Name, so the field's effective type
            // becomes `string` rather than a record reference.
            .Field(FieldSpec.Of("CategoryName", "foreign", "category name by reference", detailType: "ItemCategory.Name"));
        item
            .Row("1", "Short Sword", "1")
            .Row("2", "Leather Armor", "2");

        b.Table(8, 1, item);

        Save(workbook, path);
    }

    // ------------------------------------------------------ invalid workbooks

    /// <summary>
    /// A workbook with several independent mistakes in it.
    ///
    /// Deliberately more than one, and of more than one kind: validation is
    /// supposed to report the lot in a single run rather than stopping at the
    /// first, so a fixture with a single error could not tell the difference.
    /// </summary>
    private static void WriteInvalid(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var catalog = new TableSpec
        {
            Name = "Catalog",
            Comment = "Repeats a primary index value and a secondary index value.",
        };
        catalog
            .Field(FieldSpec.Of("index", "int", "primary index"))
            // A `*` prefix opts a further column into index treatment, so its
            // values must be unique too.
            .Field(FieldSpec.Of("*Code", "string", "secondary index"))
            .Field(FieldSpec.Of("Name", "string", "display name"));
        catalog
            .Row("1", "X", "first")
            // Duplicate primary index, and "X" duplicates the secondary index.
            .Row("1", "X", "second")
            .Row("3", "Z", "third");

        b.Table(1, 1, catalog);

        var orders = new TableSpec
        {
            Name = "Orders",
            Comment = "Points at a Catalog row that does not exist.",
        };
        orders
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Item", "foreign", "ordered item", detailType: "Catalog"))
            .Field(FieldSpec.Of("Qty", "int", "quantity"));
        orders
            .Row("1", "3", "2")
            // Catalog has no row 99.
            .Row("2", "99", "1");

        b.Table(6, 1, orders);

        // A reference whose target table is absent altogether, and one naming a
        // field the target does not have. Both are resolution failures rather
        // than validation failures, and used to abort the run on the spot - so
        // they never appeared alongside the problems above.
        var shipments = new TableSpec
        {
            Name = "Shipments",
            Comment = "References a table that does not exist, and a field that does not exist.",
        };
        shipments
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Warehouse", "foreign", "no such table", detailType: "NoSuchTable"))
            .Field(FieldSpec.Of("CatalogLabel", "foreign", "no such field", detailType: "Catalog.NoSuchField"));
        shipments
            .Row("1", "1", "1");

        b.Table(11, 1, shipments);

        Save(workbook, path);
    }

    /// <summary>
    /// A client-visible table referencing a server-only one.
    ///
    /// Valid as a whole, but a client build drops the target, leaving the
    /// reference pointing at a type that was never emitted.
    /// </summary>
    private static void WriteSideDangling(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Sides"));

        var serverOnly = new TableSpec
        {
            Name = "ServerOnlyTarget",
            Comment = "Excluded from client builds.",
            TargetSide = "s",
        };
        serverOnly
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "name"))
            .Field(FieldSpec.Of("Note", "string", "note"));
        serverOnly
            .Row("1", "alpha", "first")
            .Row("2", "beta", "second");

        b.Table(1, 1, serverOnly);

        var clientVisible = new TableSpec
        {
            Name = "ClientVisible",
            Comment = "Survives a client build, but its reference does not.",
        };
        clientVisible
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Target", "foreign", "dangles in a client build", detailType: "ServerOnlyTarget"))
            .Field(FieldSpec.Of("Label", "string", "label"));
        clientVisible
            .Row("1", "1", "one")
            .Row("2", "2", "two");

        b.Table(6, 1, clientVisible);

        Save(workbook, path);
    }

    /// <summary>
    /// `foreign[]`, which is deliberately unsupported.
    /// </summary>
    private static void WriteArrayForeign(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var target = new TableSpec { Name = "Target", Comment = "Reference target." };
        target
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "name"))
            .Field(FieldSpec.Of("Note", "string", "note"));
        target.Row("1", "one", "first");

        b.Table(1, 1, target);

        var holder = new TableSpec { Name = "Holder", Comment = "Declares an array of references." };
        holder
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Targets", "foreign[]", "unsupported", detailType: "Target"))
            .Field(FieldSpec.Of("Label", "string", "label"));
        holder.Row("1", "1", "a");

        b.Table(9, 1, holder);

        Save(workbook, path);
    }

    /// <summary>
    /// Cell values that used to be accepted silently and now are not.
    ///
    /// A misspelled boolean became false rather than an error, which is the class of
    /// human mistake this tool exists to catch turned into wrong data.
    /// </summary>
    /// <summary>
    /// A `bitset` column beside a `bigint` one holding the same values.
    /// </summary>
    /// <remarks>
    /// The pairing is the point. `bitset` is a type only for as long as parsing lasts - the
    /// cooker folds it to a 64-bit integer once every cell has been read - so the two columns
    /// have to arrive at every artifact as the same column. A fold that did not happen shows
    /// up here twice: as a difference between the two columns, and as a generator refusing to
    /// render a type it was never told about.
    ///
    /// Each row writes the same number a different way, ending with the one decimal cannot
    /// reach - every bit set, which a signed 64-bit value spells as -1.
    ///
    /// spec/bitset.md has the notation table.
    /// </remarks>
    private static void WriteBitset(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bitset"));

        var spec = new TableSpec
        {
            Name = "Flags",
            Comment = "A flag set written four ways, beside the same value as a plain integer.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Mask", "bitset", "the flags"))
            .Field(FieldSpec.Of("Same", "bigint", "the same value, written in decimal"))
            .Field(FieldSpec.Of("Set", "bitset[]", "several flag sets in one cell"))
            .Field(FieldSpec.Of("Label", "string", "which notation the row is written in"));
        spec
            .Row("1", "0x1f", "31", "0x1;0b10;3", "hexadecimal")
            .Row("2", "0b1011", "11", "0;0", "binary")
            .Row("3", "123", "123", "0xff", "decimal")
            // Every bit. Decimal cannot reach bit 63, which is the whole reason the pattern
            // notations exist for this type.
            .Row("4", "0xFFFFFFFFFFFFFFFF", "-1", "", "every bit");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// Reference cells left empty: refused where the column is required, absence where it
    /// says it may be.
    /// </summary>
    /// <remarks>
    /// A blank parses to zero and zero is the convention for "points at nothing", so the
    /// value alone cannot tell a cell nobody filled in from one somebody wrote a zero into.
    /// Both spellings of "nothing" are here, in both kinds of column, because the rule this
    /// pins is that they are not the same fact: a blank is the column's answer to give and
    /// a zero is the author's.
    ///
    /// `reference-required-blank` is the other half - the same blank in a column that does
    /// not allow it.
    ///
    /// spec/reference-optionality.md.
    /// </remarks>
    private static void WriteReferenceOptional(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Refs"));

        var target = new TableSpec
        {
            Name = "Target",
            Comment = "What the references below point at.",
        };
        target
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "so the row has something in it"))
            .Field(FieldSpec.Of("Note", "string", "a third column, so both tables are the same width"));
        target
            .Row("1", "first", "a")
            .Row("2", "second", "b");

        b.Table(1, 1, target);

        var holder = new TableSpec
        {
            Name = "Holder",
            Comment = "One reference per rule the spec states.",
        };
        holder
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Maybe", "foreign?", "optional - `-` is no target",
                                detailType: "Target"))
            .Field(FieldSpec.Of("Zero", "foreign", "required, and a written zero is a value",
                                detailType: "Target"));
        holder
            // Both filled, which is the ordinary row.
            .Row("1", "1", "1")
            // The optional one saying it points at none, and a written zero beside it.
            // Neither is a finding: one says absence where absence is allowed and the other
            // is a value.
            .Row("2", "-", "0")
            // A written zero in the optional column too, so that "a zero passes" is pinned
            // on both kinds of column and not only on the required one. It has to keep
            // meaning what it means beside a column that can also say absence.
            .Row("3", "0", "2");

        b.Table(9, 1, holder);

        Save(workbook, path);
    }

    /// <summary>
    /// The same shape with the cells a required reference may not hold.
    /// </summary>
    /// <remarks>
    /// A separate workbook rather than a second recipe over the first, because what has to
    /// differ is the column's own declaration and that lives in the sheet.
    ///
    /// Two rows, because a required reference has two ways to say nothing and they are not
    /// the same finding: a blank cell is one nobody filled in, refused whatever the column
    /// declared, and `-` is a row saying it points at none, refused because this column says
    /// every row points at something. spec/blank-and-null-cells.md.
    /// </remarks>
    private static void WriteReferenceRequiredBlank(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Refs"));

        var target = new TableSpec
        {
            Name = "Target",
            Comment = "What the reference below points at.",
        };
        target
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "so the row has something in it"))
            .Field(FieldSpec.Of("Note", "string", "a third column, so both tables are the same width"));
        target.Row("1", "first", "a");

        b.Table(1, 1, target);

        var holder = new TableSpec
        {
            Name = "Holder",
            Comment = "A required reference this sheet leaves empty.",
        };
        holder
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Must", "foreign", "required, and the rows below say nothing",
                                detailType: "Target"))
            .Field(FieldSpec.Of("Label", "string", "a third column, so both tables are the same width"));
        holder
            .Row("1", "1", "x")
            // Nobody filled this one in.
            .Row("2", "", "y")
            // And this one says it points at none, which this column does not allow either.
            .Row("3", "-", "z");

        b.Table(8, 1, holder);

        Save(workbook, path);
    }

    private static void WriteStrictValues(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var spec = new TableSpec
        {
            Name = "Flags",
            Comment = "Holds a boolean that is neither a recognized word nor a number.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Enabled", "bool", "misspelled below"))
            .Field(FieldSpec.Of("Label", "string", "label"));
        spec
            .Row("1", "Y", "fine")
            // A typo for TRUE. Used to read as false.
            .Row("2", "Ture", "typo");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// The three nesting shapes the `Group.Member` notation produces, and a plain column
    /// beside each one so the folding has to tell them apart.
    /// </summary>
    /// <remarks>
    /// Shapes, in the order they appear: a record with no number (one record), a numbered
    /// record (an array of them), and a scalar serial field - which is what a numbered
    /// column without a member has always meant and must keep meaning.
    ///
    /// The members deliberately disagree about type. That is the whole reason a record
    /// exists rather than an array: `Slot1.Id` is an int and `Slot1.Label` is a string,
    /// so folding them into one array would have to pick one and be wrong about the other.
    ///
    /// spec/nested-fields.md has the notation.
    /// </remarks>
    private static void WriteNested(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Nested"));

        var spec = new TableSpec
        {
            Name = "Loadout",
            Comment = "Columns folded into records by the Group.Member notation.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "plain column, between two groups"))

            // One record, because the group carries no number.
            .Field(FieldSpec.Of("Pos.X", "float", "record with no number"))
            .Field(FieldSpec.Of("Pos.Y", "float", "second member of the same record"))

            // An array of records. Members of different types, and the columns of the two
            // elements deliberately not adjacent - a group's columns need not be, exactly
            // as a serial field's need not be.
            .Field(FieldSpec.Of("Slot1.Id", "int", "element 1, first member"))
            .Field(FieldSpec.Of("Slot1.Label", "string", "element 1, second member"))
            .Field(FieldSpec.Of("Note", "string", "plain column inside the group's span"))
            .Field(FieldSpec.Of("Slot2.Id", "int", "element 2, first member"))
            .Field(FieldSpec.Of("Slot2.Label", "string", "element 2, second member"))

            // A scalar serial field, which the notation must not have changed.
            .Field(FieldSpec.Of("Tag1", "string", "scalar serial field"))
            .Field(FieldSpec.Of("Tag2", "string", "second element of it"));

        spec
            .Row("1", "first",  "1.5", "-2.5", "10", "sword",  "n1", "11", "shield", "a", "b")
            .Row("2", "second", "0",   "0",    "20", "bow",    "n2", "21", "arrow",  "c", "d")
            // Empty strings and zeroes, so a run of equal values exists in every column -
            // which is what the column encodings and the run decode read.
            .Row("3", "third",  "0",   "0",    "20", "",       "",   "21", "",       "",  "");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A table whose primary index is a string.
    /// </summary>
    /// <remarks>
    /// The lookup a generator emits is a dictionary over the index field's own type, so this
    /// needs nothing an `int` index did not already have - the secondary indexes have accepted
    /// strings all along. Being pointed at is the other half, and `reference-keys` covers it.
    /// </remarks>
    private static void WriteStringIndex(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Keys"));

        var spec = new TableSpec
        {
            Name = "Animation",
            Comment = "Keyed by name rather than by number.",
        };
        spec
            .Field(FieldSpec.Of("index", "string", "primary index, a string"))
            .Field(FieldSpec.Of("Blend", "float", "anything"))

            // A second index, to show the two kinds of key sit side by side.
            .Field(FieldSpec.Of("*Slot", "int", "secondary index, still a number"));

        spec
            .Row("Combat_Run_01", "0.2", "1")
            .Row("Combat_Walk_01", "0.35", "2")
            .Row("Idle_01", "0", "3");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A table whose rows are given twice, and the shapes the fold has to answer for.
    /// </summary>
    /// <remarks>
    /// The tail `_alt` rather than anything a real project uses: the pattern that recognizes
    /// it is the recipe's, so a fixture written around one project's spelling would be
    /// testing that spelling instead of the mechanism.
    ///
    /// Four tables, and each is one of the questions in spec/table-row-sets.md:
    ///
    ///   Colour, Colour_alt      the base case - one type, two files
    ///   Paint,  Paint_alt       a reference; the `_alt` rows point at ids only `Colour_alt`
    ///                           has, so a set resolving against the base would fail
    ///   Brush,  (none)          a table with one set, referenced from `Paint_alt` - the
    ///                           fallback, which is the common case rather than a leniency
    ///   Narrow, Narrow_alt      the set that holds fewer columns, which is allowed: the
    ///                           cells it does not have read as absent
    ///
    /// A set holding a column the table does not is refused, and that is a unit test rather
    /// than a fixture - it is about the message, and a workbook that cannot be converted has
    /// no golden to compare.
    /// </remarks>
    private static void WriteRowSets(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Tables"));

        // The ids differ between the two sets on purpose: 1..2 in one and 11..12 in the
        // other, so a reference resolved against the wrong set finds nothing.
        // The ids differ between the two sets on purpose: 1..2 in one and 11..12 in the
        // other, so a reference resolved against the wrong set finds nothing.
        var colour = new TableSpec { Name = "Colour", Comment = "Given its rows twice." };
        colour
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "what it is called"))
            .Field(FieldSpec.Of("Ordinal", "int", "anything"));
        colour.Row("1", "red", "1").Row("2", "green", "2");

        var colourAlt = new TableSpec { Name = "Colour_alt", Comment = "Given its rows twice." };
        colourAlt
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "what it is called"))
            .Field(FieldSpec.Of("Ordinal", "int", "anything"));
        colourAlt.Row("11", "crimson", "1").Row("12", "jade", "2");

        var brush = new TableSpec { Name = "Brush", Comment = "One set of rows only." };
        brush
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Width", "int", "anything"))
            .Field(FieldSpec.Of("Bristles", "int", "anything"));
        brush.Row("1", "4", "40").Row("2", "8", "80");

        var paint = new TableSpec { Name = "Paint", Comment = "Points at both kinds." };
        paint
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("ColourId", "foreign", "the same set first", detailType: "Colour"))
            .Field(FieldSpec.Of("BrushId", "foreign", "falls back to the one set", detailType: "Brush"));
        paint.Row("1", "1", "1").Row("2", "2", "2");

        var paintAlt = new TableSpec { Name = "Paint_alt", Comment = "Points at both kinds." };
        paintAlt
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("ColourId", "foreign", "the same set first", detailType: "Colour"))
            .Field(FieldSpec.Of("BrushId", "foreign", "falls back to the one set", detailType: "Brush"));

        // 11 and 12 exist only in `Colour_alt`, and 1 and 2 only in `Brush`.
        paintAlt.Row("1", "11", "1").Row("2", "12", "2");

        var narrow = new TableSpec { Name = "Narrow", Comment = "One set holds fewer columns." };
        narrow
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Kept", "int", "in both sets"))
            .Field(FieldSpec.Of("Also", "int", "in both sets"))
            .Field(FieldSpec.Of("Dropped", "int?", "in this set only"));
        narrow.Row("1", "10", "1", "100").Row("2", "20", "2", "200");

        var narrowAlt = new TableSpec { Name = "Narrow_alt", Comment = "One set holds fewer columns." };
        narrowAlt
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Kept", "int", "in both sets"))
            .Field(FieldSpec.Of("Also", "int", "in both sets"));
        narrowAlt.Row("1", "11", "1").Row("2", "21", "2");

        // Side by side rather than stacked, which is how the other multi-table fixtures do
        // it: these tables are not all the same width, and a narrower one above a wider one
        // leaves the grid holding a blank cell where its field row ends.
        int column = 1;
        // The widest table last: a table at the right edge needs room for the marker row
        // beside its columns, and a two-column one there does not have it.
        foreach (var spec in new[] { colour, colourAlt, brush, narrowAlt, narrow, paint, paintAlt })
            column = b.Table(column, 1, spec) is var _ ? column + spec.Fields.Count + 1 : column;

        Save(workbook, path);
    }

    /// <summary>
    /// Every type a key may be, in both index positions.
    /// </summary>
    /// <remarks>
    /// `int` and `string` were the only two the primary index accepted, and a `*` column
    /// accepted anything that was not a float - two rules for one question. Now there is one,
    /// and this is what stands behind it: `bigint`, `uuid` and `enum` keying a table, and each
    /// of them also sitting as a secondary index beside a different primary.
    ///
    /// Both positions on purpose. A generator reads the primary through one path and the
    /// secondary through another - `PrimaryLookup` off the referenced table against the
    /// `IsIndexer` loop - so a key type can work in one and not the other.
    ///
    /// None of these tables is referenced by anything, which keeps this about keys alone.
    /// Being pointed at is `reference-keys`, where a `string`, a `bigint` and a `uuid` key
    /// are each the target of a reference.
    /// </remarks>
    private static void WriteKeyTypes(string path)
    {
        var workbook = new XSSFWorkbook();

        var enums = new SheetBuilder(workbook.CreateSheet("Enums"));

        enums.Enum(1, 1, new EnumSpec { Name = "Slot", Comment = "Where a piece of equipment goes." }
            .Label("None", "0", "no slot")
            .Label("Head", "1", "worn on the head")
            .Label("Body", "2", "worn on the body")
            .Label("Feet", "3", "worn on the feet"));

        // --- uuid key, enum secondary ------------------------------------

        var assets = new SheetBuilder(workbook.CreateSheet("Asset"));

        var asset = new TableSpec
        {
            Name = "Asset",
            Comment = "Keyed by a uuid, which is what a pipeline that mints ids hands over.",
        };
        asset
            .Field(FieldSpec.Of("index", "uuid", "primary index, a uuid"))
            .Field(FieldSpec.Of("Path", "string", "anything"))
            .Field(FieldSpec.Of("*Slot", "enum", "secondary index, an enum label", detailType: "Slot"));

        asset
            .Row("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "art/helm.fbx", "Head")
            .Row("6f9619ff-8b86-d011-b42d-00c04fc964ff", "art/plate.fbx", "Body")
            .Row("0f8fad5b-d9cb-469f-a165-70867728950e", "art/boots.fbx", "Feet");

        assets.Table(1, 1, asset);

        // --- bigint key, uuid secondary ----------------------------------

        var ledgers = new SheetBuilder(workbook.CreateSheet("Ledger"));

        var ledger = new TableSpec
        {
            Name = "Ledger",
            Comment = "Keyed by a bigint, for ids that outgrew 32 bits.",
        };
        ledger
            .Field(FieldSpec.Of("index", "bigint", "primary index, past 32 bits"))
            .Field(FieldSpec.Of("Amount", "int", "anything"))
            .Field(FieldSpec.Of("*Batch", "uuid", "secondary index, a uuid"));

        ledger
            .Row("9007199254740993", "10", "7b7d9f6a-1e2c-4c1a-9a5f-2b6d0c3e4f51")
            .Row("-9007199254740993", "-10", "1f2504e0-4f89-11d3-9a0c-0305e82c3302")
            .Row("1", "0", "2f2504e0-4f89-11d3-9a0c-0305e82c3303");

        ledgers.Table(1, 1, ledger);

        // --- enum key, bigint secondary ----------------------------------

        var slots = new SheetBuilder(workbook.CreateSheet("Slotting"));

        var slotting = new TableSpec
        {
            Name = "Slotting",
            Comment = "Keyed by an enum: one row per label, which is a shape sheets have.",
        };
        slotting
            .Field(FieldSpec.Of("index", "enum", "primary index, an enum label", detailType: "Slot"))
            .Field(FieldSpec.Of("Capacity", "int", "anything"))
            .Field(FieldSpec.Of("*Serial", "bigint", "secondary index, past 32 bits"));

        slotting
            .Row("Head", "1", "9007199254740994")
            .Row("Body", "3", "9007199254740995")
            .Row("Feet", "2", "9007199254740996");

        slots.Table(1, 1, slotting);

        Save(workbook, path);
    }

    /// <summary>
    /// A reference that is a member of a record group.
    /// </summary>
    /// <remarks>
    /// Refused until now, and the refusal threw rather than reported - so a workbook with one
    /// of these did not convert at all. What was missing is generated code: resolution makes
    /// a stored key and a setter per field, and neither reached `[j].Member`.
    ///
    /// All three shapes a record group has, because the element index is the whole of what
    /// was missing and each shape puts it somewhere else: an array of records indexes the
    /// group, a single record indexes nothing, and a record of arrays indexes the member.
    /// A generator that handles one of them handles neither of the others by accident.
    ///
    /// One member beside the reference in each, so the shape being pinned is a record that
    /// holds both and not a record that is only a reference.
    ///
    /// spec/references-in-records.md.
    /// </remarks>
    private static void WriteRecordRef(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Records"));

        var item = new TableSpec { Name = "Item", Comment = "What the members point at." };
        item
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "so the row has something in it"))
            .Field(FieldSpec.Of("Pad", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad3", "int", "padding, so every table is one width"));
        item
            .Row("1", "sword", "0", "0", "0")
            .Row("2", "shield", "0", "0", "0");

        b.Table(1, 1, item);

        var loadout = new TableSpec
        {
            Name = "Loadout",
            Comment = "A record array whose first member is a reference.",
        };
        loadout
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Slot1.ItemId", "foreign", "element 1, the reference",
                                detailType: "Item"))
            // A second reference in the same group, at the same table. This is what decides
            // where the stored key lives: a name built from the group and the target would be
            // `_slot_Item_index` for both of them, and the second would overwrite the first.
            .Field(FieldSpec.Of("Slot1.SwapId", "foreign", "element 1, a second reference to "
                                + "the same table", detailType: "Item"))
            .Field(FieldSpec.Of("Slot1.Count", "int", "element 1, an ordinary member"))
            .Field(FieldSpec.Of("Slot2.ItemId", "foreign", "element 2, the reference",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Slot2.SwapId", "foreign", "element 2, the second reference",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Slot2.Count", "int", "element 2, an ordinary member"));
        loadout
            // Both elements pointing at different rows, so a wrong element index shows - and
            // the two members of one element pointing at different rows as well, so a second
            // reference landing in the first one's key shows too.
            .Row("1", "1", "2", "10", "2", "1", "20")
            // A written zero in the second element's reference, which is the convention for
            // "points at nothing" and has to stay that beside a resolved one.
            .Row("2", "2", "1", "30", "0", "0", "0");

        b.Table(9, 1, loadout);

        // One record rather than an array of them: the group carries no number, so there is
        // no element index at all. A linking pass written around `[j]` compiles for the
        // shape above and not for this one.
        var holder = new TableSpec
        {
            Name = "Holder",
            Comment = "A single record whose first member is a reference.",
        };
        holder
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Main.ItemId", "foreign", "the reference, in a record of one",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Main.Count", "int", "an ordinary member beside it"))
            .Field(FieldSpec.Of("Pad", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"));
        holder
            .Row("1", "2", "5", "0", "0")
            // Points at nothing, which has to stay that way beside the resolved one above.
            .Row("2", "0", "0", "0", "0");

        b.Table(17, 1, holder);

        // The number is on the member rather than on the group, so the group is one record
        // and the array is inside it. The third place an element index can sit, and the
        // reference member is an array of keys rather than one.
        var bag = new TableSpec
        {
            Name = "Bag",
            Comment = "One record whose members are arrays, one of them references.",
        };
        bag
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Slots.ItemId1", "foreign", "element 1 of the member",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Slots.ItemId2", "foreign", "element 2 of the member",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Slots.Count1", "int", "element 1 of the member beside it"))
            .Field(FieldSpec.Of("Slots.Count2", "int", "element 2 of the member beside it"));
        bag
            // Two elements at different rows, so a wrong element index shows here too.
            .Row("1", "1", "2", "10", "20")
            .Row("2", "2", "0", "30", "0");

        b.Table(25, 1, bag);

        // A reference two levels in. The read and the linking both name the member by its
        // whole path, so a generator that took only the last part - or only the first - puts
        // the key somewhere nothing declared.
        var mount = new TableSpec
        {
            Name = "Mount",
            Comment = "A reference inside a record inside a record.",
        };
        mount
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Rig1.Core.ItemId", "foreign", "element 1, two levels in",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Rig1.Core.Count", "int", "its sibling at that level"))
            .Field(FieldSpec.Of("Rig2.Core.ItemId", "foreign", "element 2", detailType: "Item"))
            .Field(FieldSpec.Of("Rig2.Core.Count", "int", "its sibling"));
        mount
            .Row("1", "1", "10", "2", "20")
            .Row("2", "2", "30", "0", "0");

        b.Table(33, 1, mount);

        // A table keyed by a string, and a record member pointing at it. The key a reference
        // carries is the target's to decide, and inside an element it is declared, read and
        // compared against zero in three separate places - none of which may assume `int`.
        var clip = new TableSpec { Name = "Clip", Comment = "Keyed by name rather than number." };
        clip
            .Field(FieldSpec.Of("index", "string", "primary index, a string"))
            .Field(FieldSpec.Of("Length", "int", "so the row has something in it"))
            .Field(FieldSpec.Of("Pad", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad3", "int", "padding, so every table is one width"));
        clip
            .Row("Idle_01", "12", "0", "0", "0")
            .Row("Run_01", "24", "0", "0", "0");

        b.Table(41, 1, clip);

        // Keyed by a uuid, which is a class in most of the thirteen rather than a number - so
        // the key a record member declares cannot be initialized with `0` there.
        var seal = new TableSpec { Name = "Seal", Comment = "Keyed by a uuid." };
        seal
            .Field(FieldSpec.Of("index", "uuid", "primary index, a uuid"))
            .Field(FieldSpec.Of("Label", "string", "so the row has something in it"))
            .Field(FieldSpec.Of("Pad", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad3", "int", "padding, so every table is one width"));
        seal
            .Row("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "alpha", "0", "0", "0")
            .Row("3f2504e0-4f89-11d3-9a0c-0305e82c3302", "beta", "0", "0", "0");

        b.Table(57, 1, seal);

        // A record of one whose members reference a string key and a uuid key. This is the
        // shape that reaches the run-length path - a run is one value for many rows, which an
        // array column has none of - so it is the only place a non-numeric key meets the run
        // decode. spec/reference-key-types.md.
        var badge = new TableSpec
        {
            Name = "Badge",
            Comment = "A record of one whose members' keys are not numbers.",
        };
        badge
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Mark.ClipId", "foreign", "a string key, on the run path",
                                detailType: "Clip"))
            .Field(FieldSpec.Of("Mark.SealId", "foreign", "a uuid key, on the run path",
                                detailType: "Seal"))
            .Field(FieldSpec.Of("Mark.Rank", "int", "an ordinary member beside them"))
            .Field(FieldSpec.Of("Pad", "int", "padding, so every table is one width"));
        badge
            // The same value on both rows, so the column encodes as a run rather than
            // per row - which is the path being pinned.
            .Row("1", "Idle_01", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "7", "0")
            .Row("2", "Idle_01", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "8", "0");

        b.Table(65, 1, badge);

        var rig = new TableSpec
        {
            Name = "Pose",
            Comment = "A record member pointing at a table keyed by a string.",
        };
        rig
            .Field(FieldSpec.Of("index", "int", "primary index"))
            // Optional on element 1, because element 1 is what decides for the member - the
            // elements of one member share an answer about being optional, and element 2
            // points at nothing. spec/reference-optionality.md · spec/array-optionality.md.
            .Field(FieldSpec.Of("Step1.ClipId", "foreign?", "element 1, a string key",
                                detailType: "Clip"))
            .Field(FieldSpec.Of("Step1.Weight", "int", "element 1, an ordinary member"))
            .Field(FieldSpec.Of("Step2.ClipId", "foreign?", "element 2, a string key",
                                detailType: "Clip"))
            .Field(FieldSpec.Of("Step2.Weight", "int", "element 2, an ordinary member"));
        rig
            .Row("1", "Idle_01", "1", "Run_01", "2")
            // `-` rather than a zero: a string key has no zero, so this is what "points at
            // nothing" looks like for one. spec/reference-optionality.md.
            .Row("2", "Run_01", "3", "-", "0");

        b.Table(49, 1, rig);

        Save(workbook, path);
    }

    /// <summary>
    /// A reference that is a member of a record array whose length is each row's.
    /// </summary>
    /// <remarks>
    /// The case the design decision was made for. A trimming group allocates its elements per
    /// row, so a key kept in an array beside the group would have to be allocated with them
    /// and at the same length; inside the element it is free. That is only worth anything if
    /// the rows differ in length, which is what this fixture is.
    ///
    /// A workbook of its own because trimming is a property of the source entry rather than of
    /// a table, and the fixed-length shapes are worth keeping beside it.
    ///
    /// spec/references-in-records.md · spec/variable-length-record-arrays.md.
    /// </remarks>
    private static void WriteRecordRefTrim(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Records"));

        var kit = new TableSpec
        {
            Name = "Kit",
            Comment = "A trimmed record array whose first member is a reference.",
        };
        kit
            .Field(FieldSpec.Of("index", "int", "primary index"))

            // Optional, because that is how a cell says it has no value - which is what the
            // trim reads.
            .Field(FieldSpec.Of("Part1.ItemId", "foreign?", "element 1, the reference",
                                detailType: "Item"))
            .Field(FieldSpec.Of("Part1.Count", "int?", "element 1, an ordinary member"))
            .Field(FieldSpec.Of("Part2.ItemId", "foreign?", "element 2", detailType: "Item"))
            .Field(FieldSpec.Of("Part2.Count", "int?", "element 2"))
            .Field(FieldSpec.Of("Part3.ItemId", "foreign?", "element 3", detailType: "Item"))
            .Field(FieldSpec.Of("Part3.Count", "int?", "element 3"));
        kit
            // Three, two and none: the lengths a linking loop taking the sheet's column count
            // rather than the row's would walk past.
            .Row("1", "1", "10", "2", "20", "1", "30")
            .Row("2", "2", "40", "1", "50", "-", "-")
            .Row("3", "-", "-", "-", "-", "-", "-");

        b.Table(1, 1, kit);

        Save(workbook, path);
    }

    /// <summary>
    /// References to tables keyed by something other than an `int`.
    /// </summary>
    /// <remarks>
    /// A reference carries the target's primary index, and its type is the target's to
    /// decide. `int32` used to be written into the exporters, the format's element mapping
    /// and thirteen read switches, so a table keyed by `string`, `bigint` or `uuid` could be
    /// read and generated but not pointed at - and the refusal told the author to carry the
    /// key by hand.
    ///
    /// Three keys and three references in one workbook, because a generator picks each key's
    /// spelling from its own type table: one of them getting `string` right while `uuid`
    /// names a byte array is exactly the kind of disagreement thirteen goldens are for. The
    /// referencing table holds all three at once, so a mixture is pinned as well as each on
    /// its own.
    ///
    /// The cells hold the keys as written - `Idle_01`, not a number standing in for it. That
    /// is what the deferred conversion buys: the cell is kept as text until the target is
    /// resolved, which is the only point at which its type is known.
    ///
    /// spec/reference-key-types.md.
    /// </remarks>
    private static void WriteReferenceKeys(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Keys"));

        // Every table on one sheet is the same width, which is what keeps a narrower one
        // from reading the next table's empty columns as fields of its own.
        var animation = new TableSpec { Name = "Animation", Comment = "Keyed by name." };
        animation
            .Field(FieldSpec.Of("index", "string", "primary index, a string"))
            .Field(FieldSpec.Of("Blend", "float", "anything"))
            .Field(FieldSpec.Of("Pad1", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"));
        animation
            .Row("Idle_01", "0", "0", "0")
            .Row("Combat_Run_01", "0.2", "0", "0");

        b.Table(1, 1, animation);

        var ledger = new TableSpec { Name = "Ledger", Comment = "Keyed by a 64-bit id." };
        ledger
            .Field(FieldSpec.Of("index", "bigint", "primary index, wider than an int"))
            .Field(FieldSpec.Of("Note", "string", "anything"))
            .Field(FieldSpec.Of("Pad1", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"));
        ledger
            // Past int32, so a key that only fits in 64 bits is what the reference carries.
            .Row("9007199254740993", "first", "0", "0")
            .Row("9007199254740994", "second", "0", "0");

        b.Table(9, 1, ledger);

        var art = new TableSpec { Name = "Art", Comment = "Keyed by a uuid." };
        art
            .Field(FieldSpec.Of("index", "uuid", "primary index, a uuid"))
            .Field(FieldSpec.Of("Path", "string", "anything"))
            .Field(FieldSpec.Of("Pad1", "int", "padding, so every table is one width"))
            .Field(FieldSpec.Of("Pad2", "int", "padding, so every table is one width"));
        art
            .Row("3f2504e0-4f89-11d3-9a0c-0305e82c3301", "a.png", "0", "0")
            .Row("3f2504e0-4f89-11d3-9a0c-0305e82c3302", "b.png", "0", "0");

        b.Table(17, 1, art);

        var clip = new TableSpec
        {
            Name = "Clip",
            Comment = "Points at all three at once.",
        };
        clip
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Anim", "foreign", "a string-keyed target", detailType: "Animation"))
            .Field(FieldSpec.Of("Entry", "foreign", "a bigint-keyed target", detailType: "Ledger"))
            .Field(FieldSpec.Of("Cover", "foreign", "a uuid-keyed target", detailType: "Art"));
        clip
            .Row("1", "Idle_01", "9007199254740993", "3f2504e0-4f89-11d3-9a0c-0305e82c3301")
            .Row("2", "Combat_Run_01", "9007199254740994", "3f2504e0-4f89-11d3-9a0c-0305e82c3302");

        b.Table(25, 1, clip);

        Save(workbook, path);
    }

    private static void WriteRecordTrim(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Trim"));

        var spec = new TableSpec
        {
            Name = "Loot",
            Comment = "A record array trimmed at the last element the row filled in.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "plain column, must not move"))

            // Optional, because that is how a cell says it has no value - which is what the
            // trim reads. A required member would refuse the `-` before it got this far.
            .Field(FieldSpec.Of("Slot1.Id", "int?", "element 1"))
            .Field(FieldSpec.Of("Slot1.Count", "int?", "element 1"))
            .Field(FieldSpec.Of("Slot1.Label", "string?",
                                "element 1 - a string, so the element factory has to fill it"))
            .Field(FieldSpec.Of("Slot2.Id", "int?", "element 2"))
            .Field(FieldSpec.Of("Slot2.Count", "int?", "element 2"))
            .Field(FieldSpec.Of("Slot2.Label", "string?", "element 2"))
            .Field(FieldSpec.Of("Slot3.Id", "int?", "element 3"))
            .Field(FieldSpec.Of("Slot3.Count", "int?", "element 3"))
            .Field(FieldSpec.Of("Slot3.Label", "string?", "element 3"))

            // A record that is not an array: one element, nothing to trim. Here to show the
            // trim leaves it as a plain object.
            .Field(FieldSpec.Of("Pos.X", "int?", "single record, not an array"))
            .Field(FieldSpec.Of("Pos.Y", "int?", "second member of it"))

            // The other array kind. The recipe folds these three into one `Tag` array, so the
            // trim has to answer for a scalar array as well as for a record group - and the
            // first element is required, which is what keeps the array from ever being empty.
            .Field(FieldSpec.Of("Tag1", "string",
                                "scalar serial array, element 1 - required, so the array is never empty"))
            .Field(FieldSpec.Of("Tag2", "string?", "element 2"))
            .Field(FieldSpec.Of("Tag3", "string?", "element 3"));

        spec
            // All three filled.
            .Row("1", "full",    "10", "1", "sword", "20", "2", "shield", "30", "3", "bow", "5", "6", "a", "b", "c")
            // The last one saying it has no value: two elements. `-` and not a blank, because
            // a blank `string?` is the empty string - a value, which would keep the element.
            // The `Tag` columns beside them stay blank on purpose: that group's first element
            // is required, so every element of it is, and a blank there is the empty string
            // it has always been. spec/blank-and-null-cells.md · spec/array-optionality.md.
            .Row("2", "two",     "10", "1", "sword", "20", "2", "shield", "-",  "-", "-",   "5", "6", "a", "b", "")
            // A gap in the middle, so this row keeps all three - element 2 stays at index 1
            // holding nothing, because moving it would make the index mean something else on
            // this row than on the row above. The `Tag` array has the same shape of hole.
            .Row("3", "gap",     "10", "1", "sword", "-",  "-", "-",      "30", "3", "bow", "5", "6", "a", "",  "c")
            // Nothing at all: an empty record array, which is the case a fixed length cannot
            // say. `Tag` still has its one required element, which is the difference between
            // the two array kinds.
            .Row("4", "none",    "-",  "-", "-",     "-",  "-", "-",      "-",  "-", "-",   "-", "-", "a", "",  "")
            // A zero the author wrote, which must survive - it is a value, and only `-` is
            // absence. Without HasValue this row would trim to one element.
            .Row("5", "zeroes",  "10", "1", "sword", "0",  "0", "-",      "-",  "-", "-",   "0", "0", "a", "z", "");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// Columns marked optional with a trailing `?`, beside the same types left required.
    /// </summary>
    /// <remarks>
    /// The point of the pairs: a blank in the `?` column passes and reads as the type's
    /// empty value, and the column next to it would have refused the same cell. Every type
    /// that refuses a blank today gets a pair, because that is the whole set the marker
    /// changes anything for.
    /// </remarks>
    private static void WriteOptional(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Optional"));

        b.Enum(1, 1, new EnumSpec { Name = "Rarity", Comment = "Rarity of a drop." }
            .Label("None", "0", "no value")
            .Label("Common", "1", "common")
            .Label("Rare", "2", "rare"));

        var spec = new TableSpec
        {
            Name = "Drop",
            Comment = "Optional columns, each beside the same type left required.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index, never optional"))
            .Field(FieldSpec.Of("Hp", "int", "required, so a blank here would be an error"))
            .Field(FieldSpec.Of("Bonus", "int?", "optional int"))
            .Field(FieldSpec.Of("Weight", "double?", "optional double"))
            .Field(FieldSpec.Of("Ratio", "float?", "optional float"))
            .Field(FieldSpec.Of("Count", "bigint?", "optional bigint"))
            .Field(FieldSpec.Of("OpenAt", "datetime?", "optional datetime"))
            .Field(FieldSpec.Of("Cooldown", "timespan?", "optional timespan"))
            .Field(FieldSpec.Of("Batch", "uuid?", "optional uuid"))
            .Field(FieldSpec.Of("Grade", "enum?", "optional enum label", detailType: "Rarity"))

            // After the brackets, so the array is optional rather than its elements. A blank
            // was already an empty array here, so this one states intent rather than changing
            // behaviour - which is worth a golden of its own.
            .Field(FieldSpec.Of("Costs", "int[]?", "optional array"))

            // These two have always read a blank as `""` and false. The marker does not
            // change that today; it is here so the goldens show it does not.
            .Field(FieldSpec.Of("Label", "string?", "optional string"))
            .Field(FieldSpec.Of("Hidden", "bool?", "optional bool"));

        spec
            .Row("1", "100", "5", "1.5", "0.25", "9000000000", "2026-01-02 03:04:05",
                 "01:02:03", "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "Rare", "10;20", "first", "true")
            // Every optional column saying it has no value at once, which is the row the
            // marker exists for. `-` and not a blank cell: a blank is the type's empty value
            // and this row is about the absence of one. spec/blank-and-null-cells.md.
            .Row("2", "100", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-")
            // And a second such row, so each column has a run of equal values for the
            // encodings to find.
            .Row("3", "100", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-", "-");

        b.Table(5, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// What a cell says about having nothing: a blank, a `-`, and the escape for a literal one.
    /// </summary>
    /// <remarks>
    /// Three answers that used to be two. A blank cell is whatever the column's type reads a
    /// blank as - the empty string, false, an array of no elements - and `-` is the row saying
    /// it has no value at all. `\-` writes the one character `-` where a string column needs
    /// it.
    ///
    /// The rows holding `-5`, `A-1` and `--` are the other half of the claim: `-` is special
    /// as a whole cell and nowhere else, so a column of ranges or negative numbers reads the
    /// way it always did.
    ///
    /// spec/blank-and-null-cells.md.
    /// </remarks>
    private static void WriteBlankAndNull(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Cells"));

        var spec = new TableSpec
        {
            Name = "Cell",
            Comment = "A blank, a `-` and a `\\-` in each type that can hold them.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "what the row is about"))

            // The type the whole distinction was invented for: before this, an optional
            // string column had no way to hold the empty string.
            .Field(FieldSpec.Of("Text", "string?", "blank is `\"\"`, `-` is no value"))
            .Field(FieldSpec.Of("Count", "int?", "no reading for a blank, so `-` or a number"))
            .Field(FieldSpec.Of("Flag", "bool?", "blank is false, `-` is no value"))

            // Both array kinds, because the element rule differs from the cell rule: the cell
            // as a whole can say `-`, and an element cannot.
            .Field(FieldSpec.Of("Tags", "string[]?", "blank is no elements, `-` is no array"))
            .Field(FieldSpec.Of("Costs", "int[]?", "the same for a numeric array"));

        spec
            // Ordinary values, so the rows below are read against something.
            .Row("1", "values", "hello", "7", "true", "a;b", "10;20")
            // Every column saying it has no value.
            .Row("2", "no value", "-", "-", "-", "-", "-")
            // Every column that has a reading for a blank, blank. `Count` cannot be one -
            // there is no number a blank could be - so it holds the zero this row is here to
            // be told apart from.
            .Row("3", "blank", "", "0", "", "", "")
            // The escape, in the cell and in an element. `-5` beside it: a sign is not the
            // mark, and never was.
            .Row("4", "escaped", "\\-", "-5", "false", "a;\\-;b", "-1;-2")
            // Text that merely contains the character, which nothing here touches.
            .Row("5", "literal", "A-1", "5", "true", "--;-x", "3");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A blank cell where the column's type has no reading for one.
    /// </summary>
    /// <remarks>
    /// Converted twice: once with the strict default, where it is refused and names the cell,
    /// and once with `OnBlankCell: "empty"`, where it becomes the type's empty value and is
    /// warned about. One workbook, because what differs is the recipe rather than the sheet -
    /// which is the whole of what that setting says.
    ///
    /// The column is required on purpose. Optional would answer a different question: `-`
    /// is how a row says it has none, and this setting is about a cell nobody filled in.
    /// </remarks>
    private static void WriteBlankCell(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Sparse"));

        var spec = new TableSpec
        {
            Name = "Reading",
            Comment = "A number column with a cell nobody filled in.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Value", "int", "required, and row 2 is blank"))
            .Field(FieldSpec.Of("Name", "string", "so the row holds something either way"));
        spec
            .Row("1", "10", "first")
            .Row("2", "", "unfinished");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// `-` in the two places a column does not allow it.
    /// </summary>
    /// <remarks>
    /// Both are reported by validation rather than by the reader, so one workbook holds both
    /// and one run says both things: a required column has no absence to express, and an
    /// index has none either - it identifies the row.
    /// </remarks>
    private static void WriteNoValueRefused(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var required = new TableSpec
        {
            Name = "Needed",
            Comment = "A required column with a row saying it has no value.",
        };
        required
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Hp", "int", "required, and row 2 says it has none"))
            .Field(FieldSpec.Of("Name", "string", "a third column, which an entity needs"));
        required
            .Row("1", "100", "first")
            .Row("2", "-", "second");

        b.Table(1, 1, required);

        var keyless = new TableSpec
        {
            Name = "Keyless",
            Comment = "An index saying it has no value.",
        };
        keyless
            .Field(FieldSpec.Of("index", "int", "primary index, and row 2 says it has none"))
            .Field(FieldSpec.Of("Name", "string", "anything"))
            .Field(FieldSpec.Of("Note", "string", "a third column, which an entity needs"));
        keyless
            .Row("1", "first", "a")
            .Row("-", "second", "b");

        b.Table(6, 1, keyless);

        Save(workbook, path);
    }

    /// <summary>
    /// `-` as one element of an array cell.
    /// </summary>
    /// <remarks>
    /// A workbook of its own because this one is refused while the cell is being read, and a
    /// refusal there stops the sheet - so a second table beside it would never be reached.
    ///
    /// The elements of one cell are all there or all not, which is why `?` goes after the
    /// brackets: the array is what can be absent, not an element of it.
    /// spec/optional-fields.md · spec/blank-and-null-cells.md.
    /// </remarks>
    private static void WriteNoValueElement(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var spec = new TableSpec
        {
            Name = "Listing",
            Comment = "An array cell with `-` between two values.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Tags", "string[]?", "and one element says it has no value"))
            .Field(FieldSpec.Of("Name", "string", "a third column, which an entity needs"));
        spec
            .Row("1", "a;b", "first")
            .Row("2", "a;-;b", "second");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// An index column marked optional, which has nothing to mean.
    /// </summary>
    /// <remarks>
    /// Left alone it would hand every blank row the same index 0, and the failure would
    /// surface as duplicate keys - or, in a table with one such row, not at all.
    /// </remarks>
    private static void WriteOptionalIndex(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var spec = new TableSpec { Name = "Keyless", Comment = "Its index is marked optional." };
        spec
            .Field(FieldSpec.Of("index", "int?", "an index that need not be there"))
            .Field(FieldSpec.Of("Name", "string", "anything"))
            .Field(FieldSpec.Of("Hp", "int", "anything, to reach the minimum table size"));
        spec.Row("1", "first", "100");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A record group whose second element never declares one of the members, so the
    /// record built for it would carry a value nothing writes.
    /// </summary>
    private static void WriteNestedHole(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var spec = new TableSpec { Name = "Holed", Comment = "Element 2 is missing its Label." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Slot1.Id", "int", "element 1"))
            .Field(FieldSpec.Of("Slot1.Label", "string", "element 1"))
            .Field(FieldSpec.Of("Slot2.Id", "int", "element 2, and nothing else"));
        spec.Row("1", "10", "sword", "11");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A record whose member is itself a record: an array of records, each holding a value
    /// and a record of its own.
    /// </summary>
    /// <remarks>
    /// The shape measured in a real project's workbooks - `array > record > record`, with the
    /// element number on the outermost level only. Written with two members at the middle
    /// level, one a value and one a record, because a level that holds both is what proves
    /// the folding walks the path rather than counting it.
    ///
    /// spec/nested-multi-level.md.
    /// </remarks>
    private static void WriteNestedDeep(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Deep"));

        var spec = new TableSpec { Name = "Deep", Comment = "A record whose member is a record." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Star1.Id", "int", "element 1, a value beside a record"))
            .Field(FieldSpec.Of("Star1.Position.X", "int", "element 1, one level further in"))
            .Field(FieldSpec.Of("Star1.Position.Y", "int", "its sibling"))
            .Field(FieldSpec.Of("Star2.Id", "int", "element 2"))
            .Field(FieldSpec.Of("Star2.Position.X", "int", ""))
            .Field(FieldSpec.Of("Star2.Position.Y", "int", ""));
        spec.Row("1", "10", "11", "12", "20", "21", "22");
        spec.Row("2", "30", "31", "32", "40", "41", "42");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A field name carrying two `*` markers, which is a typo for one.
    /// </summary>
    private static void WriteDoubleStar(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Bad"));

        var spec = new TableSpec { Name = "Doubled", Comment = "Field name carries two index markers." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("**Code", "string", "typo for *Code"))
            .Field(FieldSpec.Of("Label", "string", "label"));
        spec.Row("1", "A", "first");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// A formula whose cached result is an error, as a division by zero leaves behind.
    /// </summary>
    private static void WriteFormulaError(string path)
    {
        var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Bad");
        var b = new SheetBuilder(sheet);

        var spec = new TableSpec { Name = "Broken", Comment = "Holds a formula that does not evaluate." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Ratio", "float", "computed by a formula"))
            .Field(FieldSpec.Of("Label", "string", "label"));
        spec.Row("1", "0", "placeholder");

        b.Table(1, 1, spec);

        // Header occupies rows 1..7, so the single data row is row 8. The formula is
        // written with its cached result already set to the error, because Tabbit
        // reads cached results rather than evaluating anything itself.
        b.SetFormulaError(2, 8, "1/0", NPOI.SS.UserModel.FormulaError.DIV0);

        Save(workbook, path);
    }

    /// <summary>
    /// An enum column where one cell names the label and another gives its number.
    /// </summary>
    private static void WriteEnumByValue(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Data"));

        b.Enum(1, 1, new EnumSpec { Name = "Grade", Comment = "Item grade." }
            .Label("None", "0", "unset")
            .Label("Common", "1", "common")
            .Label("Rare", "2", "rare"));

        var spec = new TableSpec { Name = "Items", Comment = "Refers to Grade both ways." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Grade", "enum", "by name or by number", detailType: "Grade"))
            .Field(FieldSpec.Of("Label", "string", "label"));
        spec
            .Row("1", "Rare", "written by name")
            // The same label, written as its value.
            .Row("2", "2", "written by number");

        b.Table(6, 1, spec);

        Save(workbook, path);
    }

    /// <summary>
    /// Names that collide with a keyword in one of the output languages.
    ///
    /// Whether this matters depends on how each generator cases an identifier, and the
    /// three differ: C# renders members PascalCase, which lifts every all-lowercase
    /// keyword out of the way; TypeScript renders them camelCase; C++ renders them
    /// snake_case, so `Int` becomes `int` and `Class` becomes `class`.
    ///
    /// The table name matters too - the C++ accessor exposes each table through a
    /// snake_cased method - hence a table called Template.
    ///
    /// The point of the fixture is that the toolchain gates answer the question rather
    /// than anybody reasoning about it: the thirteen generated languages are compiled,
    /// linted or type-checked, and the side-by-side comparison reads this same workbook.
    ///
    /// A table name, an enum and its labels are all identifiers too, so all three are in
    /// here: `Package` names a table and is a Java keyword and a strict-mode binding
    /// reserved word, and the `Keyword` enum carries a `Self` that is PascalCase already
    /// and a `Type` that is a keyword in two languages. The sheet is not called `Data`
    /// because a second table and an enum share it.
    ///
    /// Two of the comments are Korean, which is not decoration: a generator that writes
    /// its output without a byte order mark leaves MSVC reading those bytes in the machine's
    /// codepage, and the C++ gate is the only thing that would ever notice.
    /// </summary>
    /// <summary>
    /// Columns typed `text`, which is a string that is also gathered for translation.
    /// </summary>
    /// <remarks>
    /// What the scenario has to show is that the role changes what is gathered and nothing
    /// else - so the same values also go out through the ordinary exports, and the golden tree
    /// is what proves a `text` column and a `string` column are the same column downstream.
    ///
    /// Three groupings, because the grouping is the part with a decision in it:
    ///
    ///   * `text` with no group, which lands in a file named after the table.
    ///   * `text(Common)` on two different tables, which collects across both - the case a
    ///     shared file exists for.
    ///   * the group written in the detail-type row instead, which is where this layout puts
    ///     the rest of a type and therefore where somebody will write it.
    ///
    /// The duplicate value, the blank cell and the quotation mark are each a line in the
    /// gathered file that would be wrong in a different way: a repeat, an empty entry, and a
    /// string that ends early.
    /// </remarks>
    private static void WriteText(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Text"));

        var quest = new TableSpec
        {
            Name = "Quest",
            Comment = "Strings shown to a player, beside strings that are not.",
        };
        quest
            .Field(FieldSpec.Of("index", "int", "primary index"))

            // No group, so these gather under the table's own name.
            .Field(FieldSpec.Of("Title", "text", "shown to a player"))

            // A group named on the type, with a namespace after it. `Common` also appears
            // on the other table, and the namespace is the column's own rather than the
            // recipe's - which is the distinction the second part exists to make.
            .Field(FieldSpec.Of("Category", "text(Common,Shared)", "shared, and its own namespace"))

            // Not gathered. Same type on the wire, and the gathered files must not hold it.
            .Field(FieldSpec.Of("ScriptId", "string", "an identifier, not prose"))

            // An optional text column, holding both of the things a cell can say about
            // having nothing: `-` for no value at all, and a blank cell for the empty string.
            // Neither contributes an entry to the gathered file.
            .Field(FieldSpec.Of("Hint", "text?", "`-` or blank"))

            // A list of gathered strings. Every element is gathered, not the joined cell.
            .Field(FieldSpec.Of("Lines", "text[]", "several strings in one cell"));

        quest
            // Row 2 repeats row 1's title on purpose: a gathered file lists it once.
            .Row("1", "Lost Cargo", "Delivery", "quest_lost_cargo", "Ask at the docks.", "Hello;Goodbye")
            .Row("2", "Lost Cargo", "Delivery", "quest_lost_cargo_2", "-", "Hello;Farewell")
            .Row("3", "The \"Blue\" Whale", "Hunt", "quest_blue_whale", "Head south.", "Onward")

            // Braces in the value. Real display strings are full of them - `{0} 애호가` is an
            // ordinary sentence with a number in it - and a format filled in by replacing
            // `{text}` in the result would go on to substitute into this.
            .Row("4", "{0} Enthusiast", "Hunt", "quest_enthusiast", "", "{0} of {1}");

        int next = b.Table(1, 1, quest);

        var item = new TableSpec
        {
            Name = "Item",
            Comment = "A second table gathering into the same shared group.",
        };
        item
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "text", "gathers under this table's name"))

            // The same group and the same namespace the other table names, so one file
            // holds both tables' values under one key.
            .Field(FieldSpec.Of("Category", "text(Common,Shared)", "shared with the other table"))

            // The group in the detail-type cell rather than on the type. The same column,
            // written the way this layout writes an enum's name and a reference's target.
            // Deliberately without a namespace, so this column takes the recipe's while the
            // two beside it keep their own - one file showing both answers.
            .Field(FieldSpec.Of("Flavour", "text", "group named in detail-type", detailType: "Common"));

        item
            .Row("1", "Rope", "Delivery", "Coarse but strong.")
            .Row("2", "Lantern", "Tools", "It burns whale oil.");

        b.Table(1, next + 2, item);

        Save(workbook, path);
    }

    /// <summary>
    /// Columns typed `asset`, whose values name files that have to exist.
    /// </summary>
    /// <remarks>
    /// The check is the recipe's - it names the folders - so the fixture's job is to give it
    /// every answer to produce: a value that resolves, one that does not, and a kind pointed
    /// at a different folder so that the same name is right in one column and wrong in the
    /// next. That last one is why the kind exists at all; without it `Icon_Sword` would be a
    /// valid sound.
    ///
    /// Two rows also leave the optional column blank, because a blank is not a missing file.
    /// </remarks>
    private static void WriteAsset(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Asset"));

        var spec = new TableSpec
        {
            Name = "Item",
            Comment = "Every column here holds a string; two of them name files.",
        };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Name", "string", "not an asset"))

            // Two kinds, looked for in two folders. `Icon_Sword` exists as an icon and not
            // as a sound, which is the distinction the kind is for.
            .Field(FieldSpec.Of("Icon", "asset(icon)", "a file under the icon root"))
            .Field(FieldSpec.Of("Sound", "asset(sfx)?", "a file under the sfx root, may be blank"))

            // The kind written in the detail-type cell instead, which is where this layout
            // puts the rest of a type.
            .Field(FieldSpec.Of("Portrait", "asset", "kind named in detail-type", detailType: "icon"))

            // A list of them. Every element is checked, not the joined cell.
            .Field(FieldSpec.Of("Extras", "asset(icon)[]", "several files in one cell"));

        spec
            .Row("1", "Sword", "Icon_Sword", "Sfx_Hit", "Icon_Portrait_A", "Icon_Sword;Icon_Shield")

            // Everything here resolves too, and the sound column is blank rather than absent.
            .Row("2", "Shield", "Icon_Shield", "", "Icon_Portrait_A", "Icon_Shield")

            // `Icon_Missing` is in no folder, and `Icon_Sword` is not a sound - the same name
            // that resolved in the icon column above.
            .Row("3", "Ghost", "Icon_Missing", "Icon_Sword", "Icon_Portrait_A", "Icon_Sword;Icon_Nope");

        b.Table(1, 1, spec);

        Save(workbook, path);
    }

    private static void WriteReservedWords(string path)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("ReservedWords"));

        var spec = new TableSpec { Name = "Template", Comment = "Named after a C++ keyword." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Class", "string", "class: keyword in C++ and C#"))
            .Field(FieldSpec.Of("Int", "int", "int: keyword in C++ and C#"))
            .Field(FieldSpec.Of("Delete", "bool", "delete: keyword in C++"))
            // A secondary index, and a string-keyed one. The generated lookups take
            // their key type from the field, so a table whose only index is the int
            // primary never compiles the other half of that - and every language but
            // C# and TypeScript went years without emitting a secondary index at all.
            .Field(FieldSpec.Of("*Operator", "string", "operator: keyword in C++, and a secondary index"))
            .Field(FieldSpec.Of("Namespace", "string", "namespace: keyword in C++ and C#"))
            .Field(FieldSpec.Of("Constructor", "string", "constructor: special member in TypeScript"))
            .Field(FieldSpec.Of("Function", "string", "function: keyword in TypeScript"));
        spec
            .Row("1", "first", "10", "Y", "plus", "alpha", "ctor-a", "fn-a")
            .Row("2", "second", "20", "N", "minus", "beta", "ctor-b", "fn-b");

        b.Table(1, 1, spec);

        // A second table whose own name is the reserved word. `const package = ...` is not a
        // binding TypeScript will take, and nothing about the first table asks that question:
        // `Template` is only awkward in C++.
        var package = new TableSpec
        {
            Name = "Package",
            Comment = "이름이 TypeScript의 바인딩 예약어인 테이블. "
                    + "주석은 한글이라 MSVC 코드페이지도 함께 확인합니다.",
        };
        package
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("Label", "string", "라벨 — 한글 주석"))
            .Field(FieldSpec.Of("Kind", "enum", "예약어 라벨을 쓰는 enum 필드", detailType: "Keyword"));
        package
            .Row("1", "one", "Self")
            .Row("2", "two", "Package");

        b.Table(10, 1, package);

        // Labels are identifiers in every target, and each generator escapes them its own
        // way - or does not. Referenced from `Package.Kind` rather than left unused, so the
        // name has to survive being written into a read as well as into a declaration.
        b.Enum(14, 1, new EnumSpec
        {
            Name = "Keyword",
            Comment = "Labels that collide with a keyword somewhere.",
        }
            .Label("None", "0", "unset")
            .Label("Self", "1", "Self is a Rust keyword, and the one that is PascalCase")
            .Label("Package", "2", "package is a Java keyword and reserved in strict-mode JavaScript")
            .Label("Type", "3", "type is a keyword in Rust and TypeScript"));

        Save(workbook, path);
    }

    /// <summary>
    /// The conformance corpus: every type, at the values that break a reader.
    ///
    /// This exists so that adding an output language costs a small harness rather than a
    /// gate of its own. A reader in any language reads this one table and prints what it
    /// found; the suite compares that against what the JSON exporter wrote from the same
    /// cells. Nothing about the comparison is language-specific.
    ///
    /// The values are chosen from where readers have actually gone wrong:
    ///
    ///   2^53 + 1 and its negative, because a language that carries a 64-bit integer in
    ///   a double returns them changed rather than failing - which is how the binary
    ///   writer's truncation of `long` survived for years, and how a JSON reader that
    ///   parses int64 as a number still loses them.
    ///
    ///   0.1 as a float, because the shortest decimal that round-trips a 32-bit value
    ///   widens to a different double, so a reader without a narrowing step disagrees
    ///   with the binary by a hair.
    ///
    ///   varint lengths one through five, and negative values either side of zero, since
    ///   the encoding is zig-zag and a reader that shifts instead of dividing gets the
    ///   sign wrong only for some magnitudes.
    ///
    ///   an empty string, an empty array and non-ASCII text, because a length-prefixed
    ///   format makes each of those a separate path.
    ///
    /// And it carries two references into a second table, which is not about values at all.
    /// Splitting each target's output into a file per table gave every language a question
    /// it did not have before - how does one table's file reach another's - and nothing here
    /// crossed a table, so the answer went unchecked in every language but C#. A missing
    /// import or require does not compile or does not load, which is what this catches.
    ///
    /// Both kinds, because the generators treat them differently: `owner` points at a whole
    /// row, so a table's file names the other table's record type, while `tier` points at
    /// one of that row's fields and names only its type.
    ///
    /// <para>
    /// Written twice. `skewed` appends one column to `Vectors` and changes nothing else,
    /// which is the corpus as a later schema would have written it; the skew gate points
    /// the readers this scenario generated at that file and asks for the same values back.
    /// The two workbooks come from one description because the gate's whole assertion is
    /// that they are otherwise identical - a twin maintained by hand answers that question
    /// with whatever it was last edited to say, and a column added here would silently take
    /// the tag the appended one holds there.
    /// </para>
    /// </summary>
    private static void WriteConformance(string path, bool skewed)
    {
        var workbook = new XSSFWorkbook();
        var b = new SheetBuilder(workbook.CreateSheet("Vectors"));

        b.Enum(1, 1, new EnumSpec { Name = "Flag", Comment = "Enum values travel zig-zag encoded." }
            .Label("None", "0", "zero")
            .Label("One", "1", "one byte")
            .Label("Large", "1048576", "three bytes")
            .Label("Negative", "-7", "negative, so the sign is folded into the low bit"));

        var spec = new TableSpec { Name = "Vectors", Comment = "Every type at the values that break a reader." };
        spec
            .Field(FieldSpec.Of("index", "int", "primary index"))
            // None of these may end in a digit: a numbered name is folded into a
            // serial field, so `i32` and `i64` became one array called `i`.
            .Field(FieldSpec.Of("intVal", "int", "varint boundaries and both extremes"))
            .Field(FieldSpec.Of("bigVal", "bigint", "past what a double carries exactly"))
            .Field(FieldSpec.Of("floatVal", "float", "single precision"))
            .Field(FieldSpec.Of("doubleVal", "double", "double precision"))
            .Field(FieldSpec.Of("text", "string", "empty, ascii and beyond"))
            .Field(FieldSpec.Of("flag", "bool", "both values"))
            .Field(FieldSpec.Of("when", "datetime", "as ticks on the wire"))
            .Field(FieldSpec.Of("span", "timespan", "as ticks on the wire"))
            .Field(FieldSpec.Of("uid", "uuid", "sixteen bytes in .NET order"))
            .Field(FieldSpec.Of("label", "enum", "zig-zag encoded", detailType: "Flag"))
            .Field(FieldSpec.Of("ints", "int[]", "length-prefixed, including empty"))
            .Field(FieldSpec.Of("strs", "string[]", "length-prefixed strings"))

            // The two array forms whose element read is not the scalar one in a loop.
            //
            // An enum element goes through a cast in most targets and through a scratch
            // variable in C, because the reader fills an int and the member is the enum. A
            // uuid element is sixteen bytes rather than a value, so the targets that own
            // their memory allocate per element rather than assigning.
            //
            // The other six array forms - bool, bigint, float, double, datetime, timespan -
            // are the same loop with a different scalar call, and each of those calls is
            // already read as a scalar column above. Left out on purpose, and the reason is
            // in CorpusCoverageTests so that leaving them out stays a decision.
            .Field(FieldSpec.Of("labels", "enum[]", "an element that needs a cast",
                                detailType: "Flag"))
            .Field(FieldSpec.Of("uids", "uuid[]", "an element that is sixteen bytes"))

            // A whole-row reference, so a table's own file names the other table's record
            // type - which is the dependency a per-table output has to carry.
            .Field(FieldSpec.Of("owner", "foreign", "a whole row of another table",
                                detailType: "Owners"))

            // And a field reference, which resolves to that row's value and so names only
            // its type. The generators take a different path for each.
            .Field(FieldSpec.Of("tier", "foreign", "one field of another table's row",
                                detailType: "Owners.rank"))

            // The three columns the v104 encodings need somewhere to win.
            //
            // A spreadsheet has one kind of number, so a column of counts arrives as
            // floating point; `count` is that column, and stated as the integers its values
            // are it costs a delta apiece instead of eight bytes.
            .Field(FieldSpec.Of("count", "double", "whole numbers carried as integers"))

            // Values built from a small vocabulary of pieces, arranged so the piece that
            // varies most is the first one. Front coding can only share what neighbours
            // have in common at the front, which here is almost nothing; the pieces repeat
            // everywhere else.
            .Field(FieldSpec.Of("route", "string", "built from shared pieces, no runs"))
            .Field(FieldSpec.Of("zone", "string", "the same, in runs"));

        spec
            // Zero and empty everywhere: one varint byte, and the length-prefixed
            // paths at length zero.
            .Row("1", "0", "0", "0", "0", "", "N",
                 "0001-01-01 00:00:00", "00:00:00", "00000000-0000-0000-0000-000000000000",
                 "None", "", "", "", "", "0", "0", "0", "", "")

            // The value a double cannot hold, and one varint byte short of two.
            .Row("2", "63", "9007199254740993", "0.1", "0.1", "ascii", "Y",
                 "2022-03-01 09:00:00", "0.00:05:00", "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                 "One", "0;1;-1", "a;b", "One", "6f9619ff-8b86-d011-b42d-00c04fc964ff", "1", "1",
                 "1", "north_gate_small_leg_north_gate", "north_gate_small_leg_north_gate")

            // Its negative, and the zig-zag boundary either side of zero.
            .Row("3", "-64", "-9007199254740993", "-0.1", "-0.1", "é한Ａ", "N",
                 "9999-12-31 23:59:59", "-0.00:05:00", "ffffffff-ffff-ffff-ffff-ffffffffffff",
                 "Negative", "-2147483648;2147483647", "", "Negative;Large", "00000000-0000-0000-0000-000000000000;ffffffff-ffff-ffff-ffff-ffffffffffff", "2", "2",
                 "-1", "south_wall_large_leg_south_wall", "north_gate_small_leg_north_gate")

            // Three varint bytes, and both 32-bit extremes.
            .Row("4", "1048576", "-1", "3.4028235E+38", "1.7976931348623157E+308", "  spaced  ", "Y",
                 "1970-01-01 00:00:00", "10675199.02:48:05", "01020304-0506-0708-090a-0b0c0d0e0f10",
                 "Large", "1048576", "one;;three", "None;One;Large", "01020304-0506-0708-090a-0b0c0d0e0f10", "3", "3",
                 "2", "east_tower_small_leg_east_tower", "north_gate_small_leg_north_gate")

            // Five varint bytes each way.
            .Row("5", "2147483647", "9223372036854775807", "1.4E-45", "5E-324", "tail", "N",
                 "2038-01-19 03:14:07", "00:00:00.0000001", "ffffffff-0000-ffff-0000-ffffffffffff",
                 "None", "134217728;-134217729", "z", "Large", "ffffffff-0000-ffff-0000-ffffffffffff", "1", "3",
                 "3", "west_gate_large_leg_west_gate", "south_wall_large_leg_south_wall")

            // Negative zero is deliberately not here: JSON has no such value, so the
            // harness contract cannot carry it and a disagreement would say nothing
            // about the reader. A negative denormal exercises the same code path and
            // does survive the round trip.
            .Row("6", "-2147483648", "-9223372036854775808", "-1.4E-45", "-5E-324", "", "Y",
                 "2000-02-29 12:00:00", "1.00:00:00", "80000000-0000-0000-0000-000000000001",
                 "One", "", "é", "", "80000000-0000-0000-0000-000000000001", "3", "1",
                 "-2", "upper_wall_small_leg_upper_wall", "south_wall_large_leg_south_wall");

        // The encoding rows: enough of each shape that every column encoding of
        // spec/tcb-v102-column-encoding.md wins somewhere in this corpus, so all
        // thirteen readers decode all seven layouts - not just the ones their own
        // fixture data happened to trigger. What wins where is pinned by the
        // encoding-selection test in BinaryFormatTests, so a drift in the writer's
        // choices is a failure with a column name rather than a quiet coverage hole.
        //
        //   index     steps by one           -> delta-RLE (one run of +1 deltas)
        //   intVal    large values, small    -> delta (varying deltas, so the RLE of
        //             varying steps             them buys nothing) - and rows 5 and 6
        //                                       above already sit at the int32
        //                                       extremes, so the delta between them
        //                                       exercises the wrapping rule
        //   bigVal    three values, in runs  -> dict-RLE over i64 entries
        //   floatVal  four values, no runs   -> dict over f32 entries
        //   doubleVal two long runs          -> dict-RLE over f64 entries
        //   text      names sharing a prefix -> dict-front-RLE
        //   flag      two long runs          -> RLE over bool
        //   label     two long runs          -> RLE
        //   owner,    small values in an     -> varint (no runs to speak of, deltas
        //   tier      irregular pattern         no cheaper than the values)
        //   when/span, uid, the four arrays  -> raw, which is the floor and worth
        //                                       having something sit on
        //
        // The pattern arrays are what keep the irregular columns irregular: no run
        // long enough for RLE, no delta stream cheaper than the values themselves.
        int[] pattern = { 1, 3, 2, 3, 1, 2 };

        // The vocabulary the segment dictionary is built out of. Indexed by three different
        // strides so the combinations do not fall into lockstep, which is what keeps the
        // values distinct while the pieces stay few.
        string[] heads = { "north", "south", "east", "west", "upper", "lower" };
        string[] parts = { "gate", "wall", "tower" };
        string[] sizes = { "small", "large" };
        int[] steps = { 5, 2, 9, 3, 7, 1, 8, 4, 6, 2 };
        string[] bigs = { "70368744177664", "-70368744177664", "8796093022208" };
        string[] floats = { "0.5", "0.25", "-0.5", "1.5" };
        int walk = 1073741824;

        // Pieces reused in a different order in the tail, so the same table row holds the
        // same piece twice and no two rows share a front.
        static string Route(string[] heads, string[] parts, string[] sizes, int at)
            => heads[at % heads.Length] + "_" + parts[(at / 2) % parts.Length] + "_"
                + sizes[(at / 3) % sizes.Length] + "_leg_"
                + heads[at % heads.Length] + "_" + parts[(at / 2) % parts.Length];

        for (int at = 0; at < 30; at++)
        {
            walk += steps[at % steps.Length];

            spec.Row(
                (7 + at).ToString(),
                walk.ToString(),
                bigs[at / 10],
                floats[at % floats.Length],
                at < 15 ? "0.125" : "-0.125",

                // Distinct values that share almost all of their bytes, three rows
                // apart, which is what the front-coded dictionary is for: the entries
                // cannot be deduplicated and are nearly free to store anyway.
                "Stat_Attack_Level" + (at / 3).ToString("00"),
                at < 15 ? "Y" : "N",
                "2022-03-01 09:00:00", "0.00:05:00",
                "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                at < 15 ? "One" : "Large",
                "", "", "", "",
                // One reference reaches a row whose index is nowhere near the others, which
                // is what keeps varint the smallest layout for this column: a bit width has
                // to cover the whole span, so a single distant id costs every row four more
                // bits while a varint charges only the row that is large. Ids in real sheets
                // are not dense, so this is the ordinary case rather than a contrived one.
                at == 7 ? "1000" : pattern[at % pattern.Length].ToString(),
                pattern[(at + 3) % pattern.Length].ToString(),

                // Whole numbers stepping by one, which the integer encodings flatten to a
                // run of deltas once the column says its values are integers.
                (1000 + at).ToString(),

                Route(heads, parts, sizes, at),

                // The same values, five rows at a time, so the index stream runs.
                Route(heads, parts, sizes, at / 5));
        }

        // The one difference between the two workbooks.
        //
        // Appended rather than inserted, so every column above keeps the tag its position
        // gave it and the reader meets the unknown one last. Its values are a plain
        // counter: the gate never reads them, and something that steps by one is the
        // cheapest thing to eyeball in a diff of the sheet.
        if (skewed)
        {
            spec.Field(FieldSpec.Of(
                "afterwards", "int",
                "a column added after the readers under test were generated"));

            for (int at = 0; at < spec.Data.Count; at++)
            {
                string[] widened = new string[spec.Fields.Count];
                spec.Data[at].CopyTo(widened, 0);
                widened[^1] = (1000 + at).ToString();
                spec.Data[at] = widened;
            }
        }

        b.Table(8, 1, spec);

        // The table the two references point into.
        //
        // Small on purpose: it is not here to test values - Vectors does that - but to give
        // the references somewhere real to land. Row 1 of Vectors points at 0, which is how
        // a sheet says "no reference", so the unresolved path is exercised too.
        var owners = new TableSpec
        {
            Name = "Owners",
            Comment = "Referenced by Vectors.owner and Vectors.tier.",
        };

        owners
            .Field(FieldSpec.Of("index", "int", "primary index"))
            .Field(FieldSpec.Of("name", "string", "what the referring row points at"))
            .Field(FieldSpec.Of("rank", "int", "what the field reference resolves to"));

        owners
            .Row("1", "first", "10")
            .Row("2", "second", "20")
            .Row("3", "third", "30");

        // Names that share a prefix and never repeat, which is what the front-coded
        // dictionary wins on with a plain index stream beside it: no two rows are
        // equal, so there is nothing for a run to cover. rank keeps stepping by ten,
        // which hands delta-RLE a second, smaller table to win in.
        for (int at = 4; at <= 15; at++)
            owners.Row(at.ToString(), "Owner_Region_" + at.ToString("00"), (at * 10).ToString());

        // The row one reference reaches across a gap. Sheets number their rows with gaps in
        // them, and the referring column's encoding is decided by the widest gap it spans.
        owners.Row("1000", "Owner_Region_1000", "10000");

        // Below Vectors, which the encoding rows made forty-odd rows tall. Entities may
        // not overlap, and these two share a column band.
        b.Table(8, 46, owners);

        // A constant set, so every language's constants file is generated, compiled and
        // read by its harness.
        //
        // Nothing gated one before. The corpus had no constant set, and neither did
        // reserved-words - the only other scenario generating for every language - so
        // splitting the output into a file per table produced a constants file per set in
        // twelve languages that nothing ever built. Rust proved the point: a constant typed
        // with an enum names that enum, the dependency graph did not say so, and the crate
        // did not compile. It took building an unrelated corpus by hand to find out.
        //
        // The enum-typed and uuid-typed constants are the two that make a constants file
        // depend on something outside itself, which is what makes them worth the place here.
        var limits = new ConstSpec
        {
            Name = "Limits",
            Comment = "Constants whose types make a constants file depend on something else.",
        };

        limits
            .Constant("MaxOwners", "int", "15", "how many rows Owners has")
            .Constant("Huge", "bigint", "9223372036854775807", "past what a double carries exactly")
            .Constant("Ratio", "float", "0.25", "single precision")
            .Constant("Precise", "double", "5E-324", "the smallest denormal")
            .Constant("Title", "string", "é한Ａ", "beyond ascii")
            .Constant("Enabled", "bool", "Y", "logical flag")
            .Constant("Epoch", "datetime", "1970-01-01 00:00:00", "as ticks on the wire")
            .Constant("Round", "timespan", "0.00:05:00", "as ticks on the wire")

            // The two that reach outside the file: an enum label, and a value the reader's
            // own type carries.
            .Constant("DefaultFlag", "enum", "Large", "names the Flag enum", detailType: "Flag")
            .Constant("BuildId", "uuid", "6f9619ff-8b86-d011-b42d-00c04fc964ff",
                      "names the reader's uuid type");

        // Column 1, well below the Flag enum: the two tables are at column 8.
        b.Const(1, 20, limits);

        Save(workbook, path);
    }

    // --------------------------------------------------------- evolution

    /// <summary>
    /// One workbook at two points in its history, written by the same code so the
    /// difference between the two is the whole of what the fixture is about.
    ///
    /// Columns carry explicit `@N` tags, which is what makes them the same columns
    /// across the pair. What changes between v1 and v2:
    ///
    ///   Evolution  A column renamed, the order shuffled, one column deleted and its
    ///              tag tombstoned, one column added. Nothing changes type, so the
    ///              code of either generation reads the other's data.
    ///   Promoted   Two columns widened - int to bigint, float to double. v2's code
    ///              reads v1's data because both widenings are lossless. v1's code
    ///              refuses v2's data, because narrowing is not.
    ///   Refused    A column that went from string to int, which is no conversion at
    ///              all. Refused in both directions, by name.
    ///
    /// The values are identical across the pair wherever a column survives, so a test
    /// asserting what came back does not have to keep two sets of numbers straight.
    /// </summary>
    private static void WriteEvolution(string path, bool second)
    {
        var workbook = new XSSFWorkbook();

        // A sheet each, because the three tables are of different widths and a rect
        // scanner reads a sheet as one rectangle.
        var b = new SheetBuilder(workbook.CreateSheet("Evolution"));

        // --- Evolution: everything that changes without changing a type ---

        var evolution = new TableSpec
        {
            Name = "Evolution",
            Comment = "Columns added, deleted, renamed and reordered.",
        };

        if (!second)
        {
            evolution
                .Field(FieldSpec.Of("index@1", "int", "primary index"))
                .Field(FieldSpec.Of("Label@2", "string", "renamed in v2"))
                .Field(FieldSpec.Of("Amount@3", "int", "unchanged"))
                .Field(FieldSpec.Of("Doomed@4", "string", "deleted in v2"));

            evolution
                .Row("1", "first", "10", "gone")
                .Row("2", "second", "-20", "also gone");
        }
        else
        {
            // Reordered as well as renamed: position carries no meaning any more, so
            // moving a column has to be a change nothing notices.
            evolution
                .Field(FieldSpec.Of("index@1", "int", "primary index"))
                .Field(FieldSpec.Of("Amount@3", "int", "unchanged, and now second"))
                .Field(FieldSpec.Of("Renamed@2", "string", "was Label in v1"))
                // A tombstone: the column is gone but its tag is not free. Reusing 4
                // for something else would make v1's code read the new column as
                // `Doomed`, which is the one way a tag scheme can still go wrong.
                .Field(FieldSpec.Of("#Doomed@4", "string", "deleted, tag reserved"))
                .Field(FieldSpec.Of("Added@5", "int", "new in v2"));

            evolution
                .Row("1", "10", "first", "", "100")
                .Row("2", "-20", "second", "", "-200");
        }

        b.Table(1, 1, evolution);

        // --- Promoted: widened types ---------------------------------------

        b = new SheetBuilder(workbook.CreateSheet("Promoted"));

        var promoted = new TableSpec
        {
            Name = "Promoted",
            Comment = "Columns widened to a type that holds every old value.",
        };

        promoted.Field(FieldSpec.Of("index@1", "int", "primary index"));

        if (!second)
        {
            promoted
                .Field(FieldSpec.Of("Amount@2", "int", "widened to bigint in v2"))
                .Field(FieldSpec.Of("Ratio@3", "float", "widened to double in v2"));
        }
        else
        {
            promoted
                .Field(FieldSpec.Of("Amount@2", "bigint", "was int in v1"))
                .Field(FieldSpec.Of("Ratio@3", "double", "was float in v1"));
        }

        // Values an int and a float both carry exactly, so what comes back after a
        // promotion is comparable without a tolerance.
        promoted
            .Row("1", "1024", "1.5")
            .Row("2", "-1024", "-0.25");

        b.Table(1, 1, promoted);

        // --- Refused: a change that is no conversion -----------------------

        b = new SheetBuilder(workbook.CreateSheet("Refused"));

        var refused = new TableSpec
        {
            Name = "Refused",
            Comment = "A column whose type changed incompatibly.",
        };

        refused.Field(FieldSpec.Of("index@1", "int", "primary index"));

        refused.Field(!second
            ? FieldSpec.Of("Code@2", "string", "an int in v2, which is not a conversion")
            : FieldSpec.Of("Code@2", "int", "a string in v1, which is not a conversion"));

        // A third column because a table is at least three wide, and an untouched one
        // because the refusal has to be about `Code` and nothing else.
        refused.Field(FieldSpec.Of("Note@3", "string", "unchanged"));

        refused
            .Row("1", second ? "7" : "seven", "first")
            .Row("2", second ? "8" : "eight", "second");

        b.Table(1, 1, refused);

        Save(workbook, path);
    }

    // ------------------------------------------------------------- helpers

    private static void Save(XSSFWorkbook workbook, string path)
    {
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            workbook.Write(fs);

        Console.WriteLine($"  wrote {Path.GetFileName(path)}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;

        if (dir == null)
            throw new InvalidOperationException("Could not locate the repository root.");

        return dir.FullName;
    }
}
