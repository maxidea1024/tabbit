using System.Linq;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// A number in a column's name never makes it an array.
/// </summary>
/// <remarks>
/// **This used to be a setting, and the setting is gone.** Folding consecutively numbered
/// columns into one array was on for every table of the layout that had the convention, then
/// opt-in, and now nothing: a name cannot say whether its number means an array -
/// `Text1`/`Text2` usually is one array of two, and `Condition_1`/`Condition_2`/`Condition_3`
/// of one real workbook are three different enums - so the notation says it instead, with
/// brackets. `spec/layout/primary-layout.md` section 5.
///
/// What is left to check is that no numbering rule survived the removal. The goldens cannot
/// check it: every fixture that meant an array now writes brackets, so a fold that came back
/// would produce the same output there and a different one for a sheet that meant two fields.
/// </remarks>
public class SerialFieldOptInTests
{
    private static Table NumberedColumns() => ModelFactory.Table(
        "T",
        new[]
        {
            ("Index", ValueType.Int32),
            ("Text1", ValueType.String),
            ("Text2", ValueType.String),
        });

    /// <summary>
    /// Numbered columns stay separate fields, whatever their names look like.
    /// </summary>
    [Fact]
    public void Numbered_columns_are_separate_fields()
    {
        var table = NumberedColumns();

        Assert.Equal(["Index", "Text1", "Text2"], table.SerialFields.Select(group => group.Name));
        Assert.All(table.SerialFields, group => Assert.False(group.IsArray));
    }

    /// <summary>
    /// And a path is what makes an array, which is what brackets write.
    /// </summary>
    [Fact]
    public void A_path_is_what_makes_an_array()
    {
        var table = NumberedColumns();

        table.Fields[1].NamePath = [new FieldPathStep { Name = "Text", Index = 0 }];
        table.Fields[2].NamePath = [new FieldPathStep { Name = "Text", Index = 1 }];

        var group = Assert.Single(table.SerialFields, serial => serial.IsArray);

        // No suffix invented for it either: the brackets said it was an array, so the name does
        // not have to say it a second time.
        Assert.Equal("Text", group.Name);
        Assert.Equal(2, group.Fields.Count);
    }
}
