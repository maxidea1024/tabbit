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
        Assert.Equal(new[] { 1, 2 }, Ints(rows[0], "slot"));
        Assert.Equal(new[] { 5, 6 }, Ints(rows[2], "slot"));
    }

    /// <summary>
    /// The two notations read the same way: both carry their length per row.
    /// </summary>
    /// <remarks>
    /// They did not until v107. A serial field's length was a constant known at generation
    /// time, which is what made adding a column to a group a code deploy rather than a data
    /// patch - a deployed reader read up to its constant and dropped the rest.
    /// spec/tcb-v107-dynamic-arrays.md.
    /// </remarks>
    [Fact]
    public void Both_array_notations_take_their_length_from_the_row()
    {
        TabbitRunner.Convert("core");

        // The table's own file. The C# target used to put every table in the accessor;
        // it now writes one file per table, as the TypeScript target below always has.
        string cs = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("core"), "csharp", "tables", "ArrayTypesTable.cs"));

        // Delimited. The count comes from the cursor rather than the reader, because where
        // it sits depends on how the block is laid out - a raw array states it in front of
        // each row's elements, an encoded one puts every length in a stream at the head of
        // the block. The cursor answers the same call either way, which is what keeps this
        // one line in the generated loop instead of a branch.
        Assert.Contains("\"ArrayTypes.Tags\", TcbTable.KindArray", cs);
        Assert.Contains("elementCount = cursor.NextLength();", cs);
        Assert.Contains("record._tags = new string[elementCount];", cs);

        // Serial, and the same three lines: one kind, one read, one allocation. No constant
        // in the page and no count in the check - the file states the length row by row, so
        // a sheet that grew a column is read by code generated before it.
        Assert.DoesNotContain("Slot_N", cs);
        Assert.DoesNotContain("column.Count", cs);
        Assert.Contains("record._slot = new int[elementCount];", cs);
        Assert.Contains("\"ArrayTypes.Slot\", TcbTable.KindArray", cs);

        string ts = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("core"), "typescript", "tables", "array-types.ts"));

        Assert.Contains("tags: string[]", ts);
        Assert.Contains("grades: Grade[]", ts);
        // Compact rows keep a delimited array as one entry and flatten a serial one.
        Assert.Contains("this._tags = dataRow[offset++]", ts);
        Assert.Contains("this._slot = dataRow.slice(offset, offset + 2)", ts);
    }

    /// <summary>
    /// An array of references written as one cell - the third way an array of references
    /// reaches the model, beside numbered columns and rows.
    /// </summary>
    /// <remarks>
    /// This was refused, on the grounds that the generated readers had no shape for a variable
    /// number of targets per row. They do, and had before the refusal was lifted: a folded
    /// group of numbered reference columns arrives at every generator as the same group, and
    /// [v107](../../spec/tcb-v107-dynamic-arrays.md) made every array carry its own length -
    /// so the reader already allocates the slots per row. spec/polymorphism.md section 4.
    /// </remarks>
    [Fact]
    public void An_array_of_references_written_in_one_cell_resolves_per_element()
    {
        var result = TabbitRunner.Convert("array-foreign");

        Assert.True(result.Succeeded, result.Describe());

        string cs = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("array-foreign"), "csharp", "tables", "HolderTable.cs"));

        // A row per element for the whole-row form, and one of that row's values for the
        // dotted one - the same two shapes a scalar reference takes. The column's name is
        // the key's either way; only the whole-row form has a second name for the rows.
        // spec/reference-surface-naming.md sections 4, 5 and 9.
        Assert.Contains("public int[] Targets => _targets_Target_index;", cs);
        Assert.Contains("public TargetTable.Record[] TargetByTargets => _targets;", cs);
        Assert.Contains("public string[] Notes => _notes;", cs);

        // Allocated from the row's own element count rather than a constant, which is what
        // separates a cell array from a folded group of columns.
        Assert.Contains("record._targets = new TargetTable.Record[elementCount];", cs);

        var rows = Rows("array-foreign", "Holder");

        Assert.Equal(new[] { 1, 2, 3 }, Ints(rows[0], "targets"));
        Assert.Equal(new[] { 2 }, Ints(rows[1], "targets"));
        Assert.Empty(Ints(rows[2], "targets"));
    }

    /// <summary>
    /// Several tables is refused whether or not it is an array, because it is a check and
    /// not a reference - spec/reference-surface-naming.md section 6.
    /// </summary>
    [Fact]
    public void An_array_reaching_several_tables_is_refused_with_what_is_available()
    {
        var result = TabbitRunner.Convert("array-foreign-multi");

        Assert.False(result.Succeeded, "`foreign A|B[]` was accepted.");
        Assert.Contains("has no single type to resolve to", result.StdOut);
        Assert.Contains("refs=", result.StdOut);
    }
}
