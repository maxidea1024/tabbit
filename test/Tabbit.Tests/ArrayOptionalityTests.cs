using System.Collections.Generic;
using System.Linq;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// An array's first element answers for the whole array, about being optional as about type.
/// </summary>
/// <remarks>
/// `Name[0]`/`Name[1]`/`Name[2]` are one field seen three times, so there is one answer to
/// "may this be blank" and the first column carries it - the same rule the group already
/// followed for its type. A sheet that marks only the first is not saying anything about the
/// others.
///
/// This used to be an error, and briefly it was read as a minimum length. Both were wrong:
/// the marks on the later columns mean nothing, and a planner making them consistent would be
/// tidying rather than fixing.
///
/// A record's members each answer for themselves, because a member is its own column with its
/// own type.
/// </remarks>
public class ArrayOptionalityTests
{
    /// <param name="required">One entry per element of the group, in element order.</param>
    private static Table GroupOf(bool trims, params bool[] required)
    {
        var columns = new List<(string, ValueType)> { ("Index", ValueType.Int32) };

        for (int at = 0; at < required.Length; at++)
            columns.Add(("Name" + at, ValueType.String));

        var table = ModelFactory.Table("T", columns);
        table.TrimTrailingArrayElements = trims;

        // Grouped by the model rather than by hand, so the grouping under test is the one the
        // converter does. The path is what says an array: each column is element `at` of one
        // level called `Name`, which is what a sheet writes as `name[0]`.
        for (int at = 0; at < required.Length; at++)
        {
            table.Fields[at + 1].NamePath =
                [new FieldPathStep { Name = "Name", Index = at }];
        }

        for (int at = 0; at < required.Length; at++)
            table.Fields[at + 1].IsRequired = required[at];

        return table;
    }

    /// <summary>
    /// Every element takes the first one's answer, so no disagreement survives grouping.
    /// </summary>
    /// <remarks>
    /// The shape the real sheets carry is the first case: the first column marked and the
    /// rest left alone. The second is the same rule the other way round, which is what makes
    /// it a rule rather than "required wins".
    /// </remarks>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public void The_first_element_answers_for_the_whole_array(bool first, bool second, bool third)
    {
        var table = GroupOf(trims: true, first, second, third);

        // Touching the groups is what applies it, and everything downstream reads the model
        // through them.
        _ = table.SerialFields;

        Assert.Equal(first, table.Fields[1].IsRequired);
        Assert.Equal(first, table.Fields[2].IsRequired);
        Assert.Equal(first, table.Fields[3].IsRequired);
    }

    /// <summary>And the wire says the same thing, because it reads the same answer.</summary>
    /// <remarks>
    /// What the answer is *about* is the element, not the array. A folded group's type cells
    /// each declare one element and none of them declares the array, so a `?` there says an
    /// element may be absent - and the array itself has no marker, because its columns exist
    /// in every row. spec/types/nullable-array-elements.md.
    /// </remarks>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public void The_wire_column_agrees_with_the_first_element(bool first, bool second, bool third)
    {
        var table = GroupOf(trims: true, first, second, third);

        var name = WireColumn.Of(table).Single(wire => wire.Cells.Count > 1);

        Assert.Equal(!first, name.HasOptionalElements);

        // And nothing says the array as a whole is absent. The reading that did say it
        // answered from element 0's cell, which took the values after it down with it.
        Assert.False(name.IsNullable);
    }

    /// <summary>
    /// A record's members keep their own answers.
    /// </summary>
    /// <remarks>
    /// A member is a column of its own with a type of its own, so "is this one optional" is
    /// its own question - which is the distinction the sheets this came from draw between
    /// marking a field and marking the fields inside an object.
    /// </remarks>
    [Fact]
    public void A_record_s_members_answer_for_themselves()
    {
        var table = ModelFactory.Table("T", new List<(string, ValueType)>
        {
            ("Index", ValueType.Int32),
            ("Slot1.Id", ValueType.Int32),
            ("Slot1.Label", ValueType.String),
            ("Slot2.Id", ValueType.Int32),
            ("Slot2.Label", ValueType.String),
        });

        // `Id` required and `Label` not, in both elements: two members, two answers.
        table.Fields[1].IsRequired = true;
        table.Fields[2].IsRequired = false;
        table.Fields[3].IsRequired = true;
        table.Fields[4].IsRequired = false;

        table.SerialFields.Add(new SerialField
        {
            Name = "Index",
            Fields = new List<Field> { table.Fields[0] },
        });

        table.SerialFields.Add(new SerialField
        {
            Kind = SerialFieldKind.Record,
            Name = "Slot",
            Members = new List<RecordMember>
            {
                new RecordMember { Name = "Id", Fields = new List<Field> { table.Fields[1], table.Fields[3] } },
                new RecordMember { Name = "Label", Fields = new List<Field> { table.Fields[2], table.Fields[4] } },
            },
        });

        _ = table.SerialFields;

        Assert.True(table.Fields[1].IsRequired);
        Assert.False(table.Fields[2].IsRequired);
        Assert.True(table.Fields[3].IsRequired);
        Assert.False(table.Fields[4].IsRequired);
    }

    /// <summary>
    /// A table that does not trim keeps its arrays a fixed length.
    /// </summary>
    [Fact]
    public void Without_trimming_the_group_stays_a_fixed_array()
    {
        var name = WireColumn.Of(GroupOf(trims: false, true, false, false))
                             .Single(wire => wire.Cells.Count > 1);

        Assert.True(name.IsFixedArray);
        Assert.False(name.IsVariableLengthArray);
    }

    /// <summary>
    /// And with trimming it is a variable one, because the row decides how long it is.
    /// </summary>
    /// <remarks>
    /// Trimming used to reach record arrays only, which left a scalar one three columns long
    /// in a table whose sheets end an array where the values end.
    /// </remarks>
    [Fact]
    public void With_trimming_a_scalar_array_becomes_variable_too()
    {
        var name = WireColumn.Of(GroupOf(trims: true, true, false, false))
                             .Single(wire => wire.Cells.Count > 1);

        Assert.True(name.IsVariableLengthArray);
        Assert.False(name.IsFixedArray);
    }
}
