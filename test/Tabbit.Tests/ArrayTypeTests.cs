using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Delimited array cells: a field typed `int[]` holds several values in one cell,
/// with the length free to differ from row to row.
///
/// Tabbit already had a fixed-size notion of array - a serial field, where
/// consecutively numbered columns fold together - and the array ValueType members
/// had been declared from the start without anything ever producing one. The two
/// kinds coexist and are deliberately different on the wire, so these tests check
/// each of them and that they do not disturb each other.
/// </summary>
public class ArrayTypeTests
{
    private static JsonElement Rows(string scenario, string table)
    {
        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(scenario), "json-named", table + ".json"));

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static string[] Strings(JsonElement row, string field)
        => row.GetProperty(field).EnumerateArray().Select(e => e.GetString()).ToArray();

    private static int[] Ints(JsonElement row, string field)
        => row.GetProperty(field).EnumerateArray().Select(e => e.GetInt32()).ToArray();

    [Fact]
    public void Array_cells_split_on_the_delimiter()
    {
        var result = TabbitRunner.Convert("core");
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var rows = Rows("core", "ArrayTypes");

        Assert.Equal(new[] { "red", "green", "blue" }, Strings(rows[0], "tags"));
        Assert.Equal(new[] { 10, 20, 30 }, Ints(rows[0], "costs"));
    }

    /// <summary>
    /// The reason this exists rather than reusing serial fields: a serial field has
    /// as many elements as it has columns, the same for every row.
    /// </summary>
    [Fact]
    public void Length_may_differ_from_row_to_row()
    {
        TabbitRunner.Convert("core");
        var rows = Rows("core", "ArrayTypes");

        Assert.Equal(3, Strings(rows[0], "tags").Length);
        Assert.Single(Strings(rows[1], "tags"));
        Assert.Equal(4, rows[1].GetProperty("weights").GetArrayLength());
    }

    /// <summary>
    /// A row with nothing to say for a column is ordinary, so an empty cell is an
    /// empty array rather than an error that forces a placeholder value.
    /// </summary>
    [Fact]
    public void Empty_cell_becomes_an_empty_array()
    {
        TabbitRunner.Convert("core");
        var rows = Rows("core", "ArrayTypes");

        Assert.Empty(Strings(rows[2], "tags"));
        Assert.Empty(Ints(rows[2], "costs"));
    }

    [Fact]
    public void Whitespace_around_elements_is_trimmed()
    {
        TabbitRunner.Convert("core");
        var rows = Rows("core", "ArrayTypes");

        // Authored as "a; b ;c" and "1; 2".
        Assert.Equal(new[] { "a", "b", "c" }, Strings(rows[3], "tags"));
        Assert.Equal(new[] { 1, 2 }, Ints(rows[3], "costs"));
    }

    /// <summary>
    /// `enum[]` resolves each element against the enum declaration, so the stored
    /// values are label numbers rather than the text in the cell.
    /// </summary>
    [Fact]
    public void Enum_arrays_resolve_each_element_to_its_label_value()
    {
        TabbitRunner.Convert("core");
        var rows = Rows("core", "ArrayTypes");

        // Authored as "Common;Rare" against Grade { Common = 1, Rare = 2, Epic = 3 }.
        Assert.Equal(new[] { 1, 2 }, Ints(rows[0], "grades"));
        Assert.Equal(new[] { 3 }, Ints(rows[1], "grades"));
    }

    /// <summary>
    /// Both array kinds in one table. The delimited ones are self-describing on the
    /// wire while the serial one is not, so a mistake in either would show up as
    /// the other's values landing in the wrong field.
    /// </summary>
    [Fact]
    public void Serial_fields_and_delimited_arrays_coexist()
    {
        TabbitRunner.Convert("core");
        var rows = Rows("core", "ArrayTypes");

        // Slot1 and Slot2 fold into one serial field, independent of the
        // delimited columns beside them.
        Assert.Equal(new[] { 1, 2 }, Ints(rows[0], "slotArray"));
        Assert.Equal(new[] { 5, 6 }, Ints(rows[2], "slotArray"));
    }

    /// <summary>
    /// The two kinds need different generated readers: a delimited array carries
    /// its length, a serial field's length is a constant known at generation time.
    /// </summary>
    [Fact]
    public void Generated_readers_distinguish_the_two_array_kinds()
    {
        TabbitRunner.Convert("core");

        // The table's own file. The C# target used to put every table in the accessor;
        // it now writes one file per table, as the TypeScript target below always has.
        string cs = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("core"), "csharp", "tables", "ArrayTypesTable.cs"));

        // Delimited: the column declares no per-row count, so every row's own count is
        // read and the array is allocated to it.
        //
        // From the cursor rather than from the reader, because where that count sits
        // depends on how the block is laid out - a raw array states it in front of each
        // row's elements, an encoded one puts every length in a stream at the head of the
        // block. The cursor answers the same call either way, which is what keeps this one
        // line in the generated loop instead of a branch.
        Assert.Contains("\"ArrayTypes.Tags\", TcbTable.KindVarArray, 0", cs);
        Assert.Contains("elementCount = cursor.NextLength();", cs);
        Assert.Contains("record._tags = new string[elementCount];", cs);

        // Serial: the count is part of the column's shape and baked in as a constant,
        // so there is no counter on the wire to read.
        Assert.Contains("SlotArray_N", cs);
        Assert.Contains(
            "\"ArrayTypes.Slot_array\", TcbTable.KindFixedArray, 2", cs);

        string ts = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("core"), "typescript", "tables", "array-types.ts"));

        Assert.Contains("tags: string[]", ts);
        Assert.Contains("grades: Grade[]", ts);
        // Compact rows keep a delimited array as one entry and flatten a serial one.
        Assert.Contains("this._tags = dataRow[offset++]", ts);
        Assert.Contains("this._slotArray = dataRow.slice(offset, offset + 2)", ts);
    }

    /// <summary>
    /// An array of references would mean resolving a variable number of targets per
    /// row, which the generated readers have no shape for. Rejecting it outright
    /// beats emitting code that silently never resolves.
    /// </summary>
    [Fact]
    public void Arrays_of_foreign_references_are_rejected_with_an_explanation()
    {
        var result = TabbitRunner.Convert("array-foreign");

        Assert.False(result.Succeeded, "`foreign[]` was accepted.");
        Assert.Contains("`foreign[]` is not supported", result.StdOut);
        Assert.Contains("serial field", result.StdOut);
    }
}
