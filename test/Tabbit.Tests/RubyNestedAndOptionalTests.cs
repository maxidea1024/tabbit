using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Ruby generator's record groups and optional columns parse, and that reading a file
/// through them gives back what the sheet said.
/// </summary>
/// <remarks>
/// `ruby -c` is a parse, and a parse says nothing about whether a constant the generated code
/// names exists - which is exactly what a record group adds. So these syntax-check every
/// emitted file and then read the binary the same conversion wrote, asserting the shapes only
/// these two features produce: the length of a trimmed record array, and a row that has no
/// value for a column that carries one.
///
/// The expected values are the fixture's, and the `nested`, `record-trim` and `optional`
/// goldens hold the JSON they are read from.
/// </remarks>
public class RubyNestedAndOptionalTests
{
    /// <summary>
    /// A record, an array of records whose members are of different types, and a scalar
    /// serial field beside them.
    /// </summary>
    [Fact]
    public void A_record_group_reads()
        => AssertReads("nested", "Nested", @"
rows = accessor.loadout.records
raise 'row count' unless rows.length == 3
raise 'lengths' unless rows.map { |r| r.slot.length } == [2, 2, 2]
raise 'pos' unless rows[0].pos.x == 1.5 && rows[0].pos.y == -2.5
raise 'slot 0' unless rows[0].slot[0].id == 10 && rows[0].slot[0].label == 'sword'
raise 'slot 1' unless rows[0].slot[1].id == 11 && rows[0].slot[1].label == 'shield'
raise 'blank label' unless rows[2].slot[0].label == ''
raise 'tags' unless rows[0].tag_array == %w[a b]
");

    /// <summary>
    /// A record array whose length is each row's - including the row that filled in none of
    /// it, and the one whose gap is a value rather than an end.
    /// </summary>
    [Fact]
    public void A_trimmed_record_array_reads()
        => AssertReads("record-trim", "RecordTrim", @"
rows = accessor.loot.records
raise 'lengths' unless rows.map { |r| r.slot.length } == [3, 2, 3, 0, 2]
raise 'gap' unless rows[2].slot[1].id == 0 && rows[2].slot[1].count == 0
raise 'empty' unless rows[3].slot == []
raise 'pos' unless rows[0].pos.x == 5 && rows[0].pos.y == 6
");

    /// <summary>
    /// A record whose members are arrays - one record, and each member holding all of its
    /// elements, out of the same columns an array of records would use.
    /// spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_reads()
        => AssertReads("member-array", "MemberArray", @"
rows = accessor.guide.records
raise 'step' unless rows[0].skill.step == [10, 11]
raise 'order' unless rows[0].skill.order == ['a', 'b']
raise 'second row' unless rows[1].skill.step == [20, 21]
raise 'pos is one record still' unless rows[0].pos.x == 1.5
raise 'tag' unless rows[0].tag_array == ['t1', 't2']
raise 'grid' unless rows[0].grid == [[1, 2, 3], [4, 5, 6]]
raise 'grid 2' unless rows[1].grid == [[7, 8, 9], [10, 11, 12]]
");

    /// <summary>
    /// A record whose member is itself a record - a value and a record at the same level, read
    /// out of the binary. spec/nested-multi-level.md.
    /// </summary>
    /// <remarks>
    /// Reading rather than parsing, for the reason this whole class exists:
    /// `r.star[k].position.x` parses whatever `position` turns out to be, so only the values say
    /// the read reached the right column.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_reads()
        => AssertReads("nested-deep", "NestedDeep", @"
rows = accessor.deep.records
raise 'row count' unless rows.length == 2
raise 'element count' unless rows.map { |r| r.star.length } == [2, 2]
raise 'value beside record' unless rows[0].star[0].id == 10
raise 'level below' unless rows[0].star[0].position.x == 11 && rows[0].star[0].position.y == 12
raise 'second element' unless rows[0].star[1].position.y == 22
raise 'second row' unless rows[1].star[0].position.x == 31
raise 'second row second element' unless rows[1].star[1].id == 40 && rows[1].star[1].position.y == 42
");

    /// <summary>
    /// A row that has a value and two that do not, for every type that can be optional.
    /// </summary>
    /// <remarks>
    /// `label` and `hidden` are the two that matter most: a blank string and a blank bool
    /// have always read as `''` and `false`, so only the presence flag tells those rows apart
    /// from the ones that wrote those values.
    /// </remarks>
    [Fact]
    public void Optional_columns_read()
        => AssertReads("optional", "Optional", @"
rows = accessor.drop.records
raise 'row count' unless rows.length == 3
raise 'bonus' unless rows[0].has_bonus && rows[0].bonus == 5
raise 'costs' unless rows[0].has_costs && rows[0].costs == [10, 20]
raise 'label' unless rows[0].has_label && rows[0].label == 'first'
raise 'hidden' unless rows[0].has_hidden && rows[0].hidden == true
rows[1..].each do |row|
  raise 'absent bonus' unless !row.has_bonus && row.bonus == 0
  raise 'absent costs' unless !row.has_costs && row.costs == []
  raise 'absent label' unless !row.has_label && row.label == ''
  raise 'absent hidden' unless !row.has_hidden && row.hidden == false
end
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
        => AssertReads("record-ref", "RecordRef", @"
rows = accessor.loadout.records
raise unless rows[0].slot[0].item_id.name == 'sword'
raise unless rows[0].slot[1].item_id.name == 'shield'
raise unless rows[0].slot[0].swap_id.name == 'shield'
raise unless rows[1].slot[1].item_id.nil?
raise unless accessor.holder.records[0].main.item_id.name == 'shield'
raise unless accessor.bag.records[0].slots.item_id[0].name == 'sword'
raise unless accessor.mount.records[0].rig[0].core.item_id.name == 'sword'
raise unless accessor.pose.records[0].step[0].clip_id.index == 'Idle_01'
raise unless accessor.kit.records.map { |r| r.part.length } == [3, 2, 0]
raise unless accessor.kit.records[1].part[0].item_id.name == 'shield'
");

    private static void AssertReads(string scenario, string module, string body)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"A Ruby interpreter is required to check the generated code. {why}");

        var parsed = ConformanceHarness.CompileRuby(scenario);
        Assert.True(parsed.Succeeded,
            $"The generated Ruby for `{scenario}` does not parse.{Environment.NewLine}{parsed.Output}");

        string binaryDir = Path.Combine(RepoLayout.OutputDir(scenario), "binary");

        var result = ConformanceHarness.RunRubySnippet(
            scenario,
            "require_relative 'tables'\n"
            + $"accessor = {module}::Tables.new\n"
            + "accessor.read_all(ARGV[0])\n"
            + body,
            binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{scenario}` through the generated Ruby failed.{Environment.NewLine}{result.Output}");
    }
}
