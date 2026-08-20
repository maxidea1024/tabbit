using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Targeted regression tests for defects that were reproducible before the fixes
/// landed.
///
/// The golden trees already lock the full output down, but a golden diff shows
/// only that bytes moved. These pin the specific behaviour each fix was about, so
/// a future change that reintroduces one fails with a message naming the defect
/// rather than a wall of generated code.
/// </summary>
public class KnownBugTests
{
    private static string OutputFile(string scenario, params string[] parts)
        => File.ReadAllText(Path.Combine(RepoLayout.OutputDir(scenario), Path.Combine(parts)));

    /// <summary>
    /// A7 - Excel date cells arrived as raw serial numbers.
    ///
    /// XlsxImporter treated every numeric cell as a number, but Excel stores dates
    /// as numbers carrying a date format, so a cell showing 2022-01-24 10:30:00
    /// reached the cooker as "44585.4375" and failed to parse as a datetime.
    /// </summary>
    [Fact]
    public void A7_excel_date_cells_import_as_dates()
    {
        var result = TabbitRunner.Convert("excel-typed");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string json = OutputFile("excel-typed", "json-named", "ExcelTyped.json");

        Assert.Contains("2022-01-24T10:30:00", json);
        Assert.DoesNotContain("44585", json);

        // The same path formats plain numbers round-trip and invariant. Without
        // that, 1e16 reached the parser in scientific notation, which no integer
        // parse accepts.
        Assert.Contains("10000000000000000", json);
    }

    /// <summary>
    /// A6 / A10 - ragged sheets crashed the column scan.
    ///
    /// RawSheet.Optimize looked for leading blank columns before padding rows to a
    /// common width, so IsWholeEmptyColumn indexed past the end of any row shorter
    /// than the blank run. An interior blank row survives the top/bottom trim and
    /// supplied exactly that.
    /// </summary>
    [Fact]
    public void A6_ragged_sheet_with_leading_blank_columns_converts()
    {
        var result = TabbitRunner.Convert("layout-edge");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        // Both entities survive: the one above the ragged row and the one below.
        Assert.Contains("alpha", OutputFile("layout-edge", "json-compact", "OffsetTable.json"));
        Assert.Contains("gamma", OutputFile("layout-edge", "json-compact", "SecondTable.json"));
    }

    /// <summary>
    /// A1 - the documented `RefTable.RefFieldName` foreign form could not resolve.
    ///
    /// Two defects sat on this path:
    ///
    ///   1. ModelCooker sliced the table name with `Substring(0, dot - 1)`, so
    ///      `ItemCategory.Name` yielded the table name `ItemCategor`.
    ///   2. The dotted branch never assigned a parseable ValueType. The bare-table
    ///      branch quietly set Int32 because a record reference is stored as the
    ///      target's index; the dotted branch left it `Unresolved`.
    ///
    /// Defect 2 fired first, because table data is parsed before
    /// SolveTableCrossReferencings runs, so fixing only the Substring would have
    /// moved the failure rather than removed it.
    /// </summary>
    [Fact]
    public void A1_foreign_reference_to_a_named_field_resolves_to_the_target_field_type()
    {
        var result = TabbitRunner.Convert("foreign-field");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string item = OutputFile("foreign-field", "typescript", "tables", "item.ts");

        // Exposed as the referenced field's type...
        Assert.Contains("public get categoryName(): string", item);
        // ...while the stored cell value stays the target table's index.
        Assert.Contains("_categoryName_ItemCategory_index: number", item);
    }

    /// <summary>
    /// A12 - generated TypeScript reference setters omitted `this.`.
    ///
    /// All four setReference_*_INTERNAL overloads emitted `_field = value` rather
    /// than `this._field = value`, which does not compile. It went unnoticed
    /// because the previous sample sheet had no field-level foreign references, so
    /// the broken lines were never emitted.
    /// </summary>
    [Fact]
    public void A12_generated_reference_setters_assign_through_this()
    {
        TabbitRunner.Convert("foreign-field");

        string item = OutputFile("foreign-field", "typescript", "tables", "item.ts");

        Assert.Contains("setReference_categoryName_INTERNAL", item);
        Assert.Contains("{ this._categoryName = value }", item);
    }

    /// <summary>
    /// A13 / A14 - constant sets produced unusable TypeScript.
    ///
    /// index.ts re-exported `./constants/{Name}`, but the export was written inside
    /// a C# interpolated string as `export { Name } from ...` with single braces,
    /// so the braces were read as an interpolation hole and the emitted line became
    /// `export GameConfig from ...`. The module it pointed at was never generated
    /// at all. Any sheet defining a `const` entity therefore produced TypeScript
    /// that could not resolve its own imports.
    /// </summary>
    [Fact]
    public void A13_and_A14_constant_sets_are_exported_and_generated()
    {
        TabbitRunner.Convert("core");

        string index = OutputFile("core", "typescript", "index.ts");
        Assert.Contains("export { GameConfig } from './constants/game-config'", index);

        string constants = OutputFile("core", "typescript", "constants", "game-config.ts");
        Assert.Contains("export class GameConfig", constants);
        Assert.Contains("public static readonly maxLevel: number = 100", constants);
        // Enum-typed constants resolve to a label and import their enum.
        Assert.Contains("import { Grade } from '../enums/grade'", constants);
        Assert.Contains("public static readonly defaultGrade: Grade = Grade.Rare", constants);
    }

    /// <summary>
    /// A15 - a table holding a record reference never imported the target's Record
    /// class, so the emitted module referred to a type it had not pulled in.
    /// </summary>
    [Fact]
    public void A15_referenced_record_types_are_imported()
    {
        TabbitRunner.Convert("core");

        string item = OutputFile("core", "typescript", "tables", "item.ts");

        Assert.Contains("import { ItemCategoryRecord } from './item-category'", item);
    }
    /// <summary>
    /// A3 - the named JSON row format lost serial-field data.
    ///
    /// JsonExporter walked a counter over the serial-field groups while indexing
    /// the row by that same counter. A serial field collapses N columns into one
    /// group, so there are fewer groups than columns: the counter took only the
    /// first column of each group and then ran ahead, so later values appeared
    /// under the wrong name and the remaining columns were dropped. A six-column
    /// Localization row came out as four scalars, two of them mislabelled.
    /// </summary>
    [Fact]
    public void A3_named_json_emits_serial_fields_as_complete_arrays()
    {
        TabbitRunner.Convert("core");

        string json = OutputFile("core", "json-named", "Localization.json");

        Assert.Contains("\"textEnArray\"", json);
        Assert.Contains("\"Hello\"", json);
        Assert.Contains("\"Hi\"", json);
        // The Korean columns used to vanish entirely.
        Assert.Contains("안녕하세요", json);
        Assert.Contains("안녕", json);
    }

    /// <summary>
    /// A16 - the compact JSON consumer mis-read serial fields.
    ///
    /// The compact row is flat, one entry per column, matching how the binary
    /// exporter writes them. The generated populateFieldValuesCompact read a
    /// single entry for a whole group, so an array field received its first
    /// element and every field after it read a neighbour's value.
    /// </summary>
    [Fact]
    public void A16_compact_consumer_slices_serial_fields()
    {
        TabbitRunner.Convert("core");

        string ts = OutputFile("core", "typescript", "tables", "localization.ts");

        Assert.Contains("this._textEnArray = dataRow.slice(offset, offset + 2)", ts);
        Assert.Contains("this._textKoArray = dataRow.slice(offset, offset + 2)", ts);
    }

    /// <summary>
    /// A9 - enum cells had to be spelled differently from the enum declaration.
    ///
    /// Labels are stored Pascal-cased, and lookup compared only against that, so
    /// an enum declared `fire_ball` could not be referenced as `fire_ball` from a
    /// data cell - only as `FireBall`.
    /// </summary>
    [Fact]
    public void A9_enum_cells_may_use_the_declared_spelling()
    {
        var result = TabbitRunner.Convert("core");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string json = OutputFile("core", "json-named", "Item.json");

        // fire_ball -> 1, ice_shard -> 2, none -> 0
        Assert.Contains("\"skillField\": 1", json);
        Assert.Contains("\"skillField\": 2", json);
        Assert.Contains("\"skillField\": 0", json);
    }

    /// <summary>
    /// B3 - sheet text reached the generated HTML unescaped.
    ///
    /// Comments and string cells are ordinary prose written by designers. An
    /// ampersand or an angle bracket in a description corrupted the documentation
    /// because the text was interpolated into the markup raw.
    /// </summary>
    [Fact]
    public void B3_sheet_text_is_escaped_in_generated_html()
    {
        TabbitRunner.Convert("core");

        // The table's own page. Every table used to be on one `tables.html`, which is why
        // this named that file; the strings under test are two cells of `Item`.
        string html = OutputFile("core", "html", "tables", "item.html");

        Assert.Contains("Sharp &amp; quick; deals &lt;b&gt;bonus&lt;/b&gt; damage", html);
        Assert.Contains("Restores 10 HP &lt;or&gt; 5 MP", html);
        // The raw form must not survive anywhere on the page.
        Assert.DoesNotContain("deals <b>bonus</b> damage", html);
    }
}
