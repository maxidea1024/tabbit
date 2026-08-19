using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Python generator's record groups and optional columns parse, and that reading a
/// file through them gives back what the sheet said.
/// </summary>
/// <remarks>
/// Compiling is the whole static check a Python target can be given, and here that is worth
/// less than it is for the nine targets before it: `record.slot[j].id = x` parses whether or
/// not `slot` holds anything. So these run the read as well, against the binary the same
/// conversion wrote, and assert the shapes only these two features produce - the length of a
/// trimmed record array, and a row that has no value for a column that carries one.
///
/// The expected values are the fixture's, and the `nested`, `record-trim` and `optional`
/// goldens hold the JSON they are read from.
/// </remarks>
public class PythonNestedAndOptionalTests
{
    /// <summary>
    /// An array whose elements may be absent: the per-element answer beside the value.
    /// </summary>
    /// <remarks>
    /// Read rather than compiled, which is what this gate is for: the bitmap is walked with a
    /// counter that steps once per element of every row, and a reader that stepped per row
    /// would still run. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void Optional_array_elements_read()
        => AssertReads("nullable-elements", "nullable_elements_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.listing.records
assert len(rows) == 5, rows
assert rows[1].has_holes_at == [True, False, True], rows[1].has_holes_at
assert rows[3].has_words_at == [True, True, True], rows[3].has_words_at
assert rows[3].words == ['a', '', 'c'], rows[3].words
");

    /// <summary>
    /// A record, an array of records whose members are of different types, and a scalar
    /// serial field beside them.
    /// </summary>
    [Fact]
    public void A_record_group_reads()
        => AssertReads("nested", "nested_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.loadout.records
assert len(rows) == 3, rows
assert [len(r.slot) for r in rows] == [2, 2, 2]
assert rows[0].pos.x == 1.5 and rows[0].pos.y == -2.5, rows[0].pos
assert rows[0].slot[0].id == 10 and rows[0].slot[0].label == 'sword', rows[0].slot[0]
assert rows[0].slot[1].id == 11 and rows[0].slot[1].label == 'shield', rows[0].slot[1]
assert rows[2].slot[0].label == '', rows[2].slot[0]
assert rows[0].tag_array == ['a', 'b'], rows[0].tag_array
");

    /// <summary>
    /// A record array whose length is each row's - including the row that filled in none of
    /// it, and the one whose gap is a value rather than an end.
    /// </summary>
    [Fact]
    public void A_trimmed_record_array_reads()
        => AssertReads("record-trim", "record_trim_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.loot.records
assert [len(r.slot) for r in rows] == [3, 2, 3, 0, 2], [len(r.slot) for r in rows]
assert rows[2].slot[1].id == 0 and rows[2].slot[1].count == 0, rows[2].slot[1]
assert rows[3].slot == [], rows[3].slot
assert rows[0].pos.x == 5 and rows[0].pos.y == 6, rows[0].pos
");

    /// <summary>
    /// A record whose members are arrays - one record, and each member holding all of its
    /// elements, out of the same columns an array of records would use.
    /// spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_reads()
        => AssertReads("member-array", "member_array_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.guide.records
assert rows[0].skill.step == [10, 11], rows[0].skill.step
assert rows[0].skill.order == ['a', 'b'], rows[0].skill.order
assert rows[1].skill.step == [20, 21], rows[1].skill.step
assert rows[0].pos.x == 1.5, rows[0].pos
assert rows[0].tag_array == ['t1', 't2'], rows[0].tag_array
assert rows[0].grid == [[1, 2, 3], [4, 5, 6]], rows[0].grid
assert rows[1].grid == [[7, 8, 9], [10, 11, 12]], rows[1].grid
");

    /// <summary>
    /// A record whose member is itself a record - a value and a record at the same level, read
    /// out of the binary. spec/nested-multi-level.md.
    /// </summary>
    /// <remarks>
    /// Reading rather than parsing, for the reason this whole class exists: `r.star[k].position.x`
    /// parses whatever `position` turns out to be, so only the values say the read reached the
    /// right column.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_reads()
        => AssertReads("nested-deep", "nested_deep_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.deep.records
assert len(rows) == 2, rows
assert [len(r.star) for r in rows] == [2, 2]
assert rows[0].star[0].id == 10, rows[0].star[0]
assert rows[0].star[0].position.x == 11 and rows[0].star[0].position.y == 12, rows[0].star[0].position
assert rows[0].star[1].position.y == 22, rows[0].star[1].position
assert rows[1].star[0].position.x == 31, rows[1].star[0].position
assert rows[1].star[1].id == 40 and rows[1].star[1].position.y == 42, rows[1].star[1]
");

    /// <summary>
    /// A row that has a value and two that do not, for every type that can be optional.
    /// </summary>
    /// <remarks>
    /// `label` and `hidden` are the two that matter most: a blank string and a blank bool
    /// have always read as `''` and `False`, so only the presence flag tells those rows
    /// apart from the ones that wrote those values.
    /// </remarks>
    [Fact]
    public void Optional_columns_read()
        => AssertReads("optional", "optional_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.drop.records
assert len(rows) == 3, rows
assert rows[0].has_bonus and rows[0].bonus == 5, rows[0]
assert rows[0].has_costs and rows[0].costs == [10, 20], rows[0].costs
assert rows[0].has_label and rows[0].label == 'first', rows[0].label
assert rows[0].has_hidden and rows[0].hidden is True, rows[0].hidden
for row in rows[1:]:
    assert not row.has_bonus and row.bonus == 0, row
    assert not row.has_costs and row.costs == [], row.costs
    assert not row.has_label and row.label == '', row.label
    assert not row.has_hidden and row.hidden is False, row.hidden
");


    /// <summary>
    /// A record whose member references another table: the row it resolved to beside the key
    /// that came off the wire, and a linking pass that walks the elements.
    /// </summary>
    /// <remarks>
    /// Reading rather than only compiling, because the failure this guards against is code
    /// that runs and leaves every element unresolved. Element 0 and element 1 point at
    /// different rows, so a loop that resolved the first and left the rest shows as the wrong
    /// value. spec/references-in-records.md.
    /// </remarks>
    [Fact]
    public void A_reference_inside_a_record_reads()
        => AssertReads("record-ref", "record_ref_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.loadout.records
assert rows[0].slot[0].item_id.name == 'sword', rows[0].slot[0]
assert rows[0].slot[1].item_id.name == 'shield', rows[0].slot[1]
assert rows[0].slot[0].swap_id.name == 'shield', rows[0].slot[0]
assert rows[1].slot[1].item_id is None, rows[1].slot[1]
assert t.holder.records[0].main.item_id.name == 'shield', t.holder.records[0].main
assert t.bag.records[0].slots.item_id[0].name == 'sword', t.bag.records[0].slots
assert t.mount.records[0].rig[0].core.item_id.name == 'sword', t.mount.records[0].rig[0]
assert t.pose.records[0].step[0].clip_id.index == 'Idle_01', t.pose.records[0].step[0]
assert [len(r.part) for r in t.kit.records] == [3, 2, 0], [len(r.part) for r in t.kit.records]
assert t.kit.records[1].part[0].item_id.name == 'shield', t.kit.records[1].part[0]
");

    /// <summary>
    /// An array of references: numbered reference columns folded into one array.
    /// </summary>
    /// <remarks>
    /// Reading rather than only compiling, because the failure this guards against is code that
    /// runs and resolves nothing: the keys arrive on the wire and the values are written by the
    /// linking pass, which walks the array it was given. Element 0 and element 1 point at
    /// different rows, so a loop that resolved the first and left the rest shows as the wrong
    /// value. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void An_array_of_references_reads()
        => AssertReads("serial-ref", "serial_ref_data", @"
t = Tables()
t.read_all(sys.argv[1])
rows = t.kit.records
assert [len(r.slot_array) for r in rows] == [2, 2, 2]
assert rows[0].slot_array[0].name == 'sword'
assert rows[0].slot_array[1].name == 'shield'
assert rows[1].slot_array[0].name == 'ring'
assert rows[2].slot_array[1] is None
assert rows[0].tier_array == [3, 5]
assert rows[2].tier_array[1] is None
");

    private static void AssertReads(string scenario, string package, string body)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated code. {why}");

        var compiled = ConformanceHarness.CompilePython(scenario);
        Assert.True(compiled.Succeeded,
            $"The generated Python for `{scenario}` does not compile.{Environment.NewLine}{compiled.Output}");

        string binaryDir = Path.Combine(RepoLayout.OutputDir(scenario), "binary");

        var result = ConformanceHarness.RunPythonSnippet(
            scenario,
            $"import sys\nfrom {package} import Tables\n{body}",
            binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{scenario}` through the generated Python failed.{Environment.NewLine}{result.Output}");
    }
}
