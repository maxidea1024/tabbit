using System.Collections.Generic;
using System.Linq;
using Tabbit;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// How columns written in the `Group.Member` notation fold into a record.
///
/// Asked of the model rather than of a conversion, because what is being checked is the
/// folding itself: which columns end up in which member, in what order, and which
/// inconsistencies stop the conversion instead of generating a record with a value
/// nothing writes. A workbook could express these, but reviewing the test would then mean
/// opening Excel to see whether a column is numbered 2 or 3.
///
/// The notation and the reasons behind it are in spec/types/nested-fields.md.
/// </summary>
public class NestedFieldFoldingTests
{
    /// <summary>
    /// A table whose columns are described as `("Index", null, null, 0)` for a plain one
    /// and `("Slot1Id", "Slot", "Id", 1)` for a record member.
    /// </summary>
    private static Table TableOf(params (string Name, string Group, string Member, int Ordinal)[] columns)
    {
        var table = ModelFactory.Table(
            "T",
            columns.Select(c => (c.Name, ValueType.Int32)).ToList());

        // A group whose columns are all element zero is one record rather than an array of
        // one, so its level carries no number at all. That is the distinction the path draws
        // and the ordinals above do not: `Pos.X` against `Slot1.Id`.
        var numbered = columns
            .Where(c => c.Group is not null && c.Ordinal != 0)
            .Select(c => c.Group)
            .ToHashSet();

        for (int i = 0; i < columns.Length; i++)
        {
            if (columns[i].Group is null)
                continue;

            var path = new List<FieldPathStep>
            {
                new FieldPathStep
                {
                    Name = columns[i].Group,
                    Index = numbered.Contains(columns[i].Group) ? columns[i].Ordinal : null,
                },
            };

            // No member is an array of plain values: one level, and the elements are the
            // values themselves.
            if (columns[i].Member is not null)
                path.Add(new FieldPathStep { Name = columns[i].Member });

            table.Fields[i].NamePath = path;
        }

        return table;
    }

    [Fact]
    public void A_group_with_no_number_is_one_record_and_not_an_array()
    {
        // `Index`, `Pos.X`, `Pos.Y`
        var table = TableOf(
            ("Index", null, null, 0),
            ("PosX", "Pos", "X", 0),
            ("PosY", "Pos", "Y", 0));

        var groups = table.SerialFields;

        Assert.Equal(2, groups.Count);
        Assert.Equal("Index", groups[0].Name);
        Assert.False(groups[0].IsRecord);

        var pos = groups[1];
        Assert.True(pos.IsRecord);
        Assert.Equal("Pos", pos.Name);
        Assert.Equal(new[] { "X", "Y" }, pos.Members.Select(m => m.Name));
        Assert.Equal(1, pos.RecordElementCount);

        // One element, so a consumer sees a record rather than an array of one.
        Assert.False(pos.IsArray);
    }

    [Fact]
    public void A_numbered_group_is_an_array_of_records()
    {
        // `Index`, `Slot1.Id`, `Slot1.Count`, `Slot2.Id`, `Slot2.Count`
        var table = TableOf(
            ("Index", null, null, 0),
            ("Slot1Id", "Slot", "Id", 1),
            ("Slot1Count", "Slot", "Count", 1),
            ("Slot2Id", "Slot", "Id", 2),
            ("Slot2Count", "Slot", "Count", 2));

        var groups = table.SerialFields;

        Assert.Equal(2, groups.Count);

        var slot = groups[1];
        Assert.True(slot.IsRecord);
        Assert.True(slot.IsArray);
        Assert.Equal(2, slot.RecordElementCount);

        // Members in the order their columns first appear, so the generated record reads
        // down the sheet.
        Assert.Equal(new[] { "Id", "Count" }, slot.Members.Select(m => m.Name));

        // And each member holds one column per element.
        Assert.Equal(new[] { "Slot1Id", "Slot2Id" }, slot.Members[0].Fields.Select(f => f.Name));
        Assert.Equal(new[] { "Slot1Count", "Slot2Count" }, slot.Members[1].Fields.Select(f => f.Name));

        // Every underlying column reachable without knowing the shape, which is what the
        // tag assignment and the data walk use.
        Assert.Equal(4, slot.AllFields.Count());
    }

    [Fact]
    public void Elements_are_ordered_by_their_number_not_by_column_position()
    {
        // The sheet put element 2 to the left of element 1. The array still reads 1, 2 -
        // only the order matters, which is also why a sheet counting from 0 comes out
        // right.
        var table = TableOf(
            ("Index", null, null, 0),
            ("Slot2Id", "Slot", "Id", 2),
            ("Slot1Id", "Slot", "Id", 1));

        var slot = table.SerialFields[1];

        Assert.Equal(new[] { "Slot1Id", "Slot2Id" }, slot.Members[0].Fields.Select(f => f.Name));
    }

    [Fact]
    public void A_group_takes_its_own_columns_and_leaves_the_others_alone()
    {
        // Two groups and a plain column, interleaved. A group's columns need not be
        // adjacent, exactly as a serial field's need not be.
        var table = TableOf(
            ("Index", null, null, 0),
            ("Slot1Id", "Slot", "Id", 1),
            ("PosX", "Pos", "X", 0),
            ("Slot2Id", "Slot", "Id", 2),
            ("Name", null, null, 0),
            ("PosY", "Pos", "Y", 0));

        var groups = table.SerialFields;

        Assert.Equal(new[] { "Index", "Slot", "Pos", "Name" }, groups.Select(g => g.Name));
        Assert.Equal(2, groups[1].RecordElementCount);
        Assert.Equal(2, groups[2].Members.Count);
        Assert.False(groups[3].IsRecord);
    }

    [Fact]
    public void A_record_group_is_never_an_index()
    {
        // Even with Indexing set, which is what the first column of every table carries.
        var table = TableOf(
            ("Index", null, null, 0),
            ("PosX", "Pos", "X", 0));
        table.Fields[1].Indexing = true;

        Assert.False(table.SerialFields[1].IsIndexer);

        // A reference belongs to a member rather than to the record, so the group does not
        // claim to be one either.
        Assert.False(table.SerialFields[1].IsRef);
    }

    [Fact]
    public void An_element_missing_a_member_stops_the_conversion()
    {
        // `Slot2.Count` was never written, so element 2's Count would be a value nothing
        // fills in - indistinguishable from a deliberate zero.
        var table = TableOf(
            ("Index", null, null, 0),
            ("Slot1Id", "Slot", "Id", 1),
            ("Slot1Count", "Slot", "Count", 1),
            ("Slot2Id", "Slot", "Id", 2));

        var ex = Assert.Throws<TabbitException>(() => _ = table.SerialFields);
        Assert.Equal(Tabbit.Cooking.CookingMessages.RecordMemberElementCountsDiffer, ex.MessageId);
        Assert.Contains("Slot", ex.Message);
    }

    [Fact]
    public void Members_numbered_inconsistently_stop_the_conversion()
    {
        // Both members have two elements, so the count agrees - but they are numbered 1,2
        // and 1,3. Position 1 of the record would then mix element 2 of one member with
        // element 3 of the other.
        var table = TableOf(
            ("Index", null, null, 0),
            ("Slot1Id", "Slot", "Id", 1),
            ("Slot1Count", "Slot", "Count", 1),
            ("Slot2Id", "Slot", "Id", 2),
            ("Slot3Count", "Slot", "Count", 3));

        var ex = Assert.Throws<TabbitException>(() => _ = table.SerialFields);
        Assert.Equal(Tabbit.Cooking.CookingMessages.RecordNumberedInconsistently, ex.MessageId);
    }

    [Fact]
    public void Members_disagreeing_about_target_side_stop_the_conversion()
    {
        // Half a record in the client build is not a shape any generator has.
        var table = TableOf(
            ("Index", null, null, 0),
            ("PosX", "Pos", "X", 0),
            ("PosY", "Pos", "Y", 0));
        table.Fields[2].TargetSide = TargetSide.ServerOnly;

        var ex = Assert.Throws<TabbitException>(() => _ = table.SerialFields);
        Assert.Equal(Tabbit.Cooking.CookingMessages.RecordMixesTargetSides, ex.MessageId);
    }

    [Fact]
    public void The_target_side_of_a_record_group_is_the_one_its_members_agree_on()
    {
        var table = TableOf(
            ("Index", null, null, 0),
            ("PosX", "Pos", "X", 0),
            ("PosY", "Pos", "Y", 0));
        table.Fields[1].TargetSide = TargetSide.ClientOnly;
        table.Fields[2].TargetSide = TargetSide.ClientOnly;

        Assert.Equal(TargetSide.ClientOnly, table.SerialFields[1].TargetSide);

        // No single column speaks for a record, so FirstField says so rather than picking
        // an arbitrary member's.
        Assert.Null(table.SerialFields[1].FirstField);
        Assert.NotNull(table.SerialFields[1].AnyField);
    }
}
