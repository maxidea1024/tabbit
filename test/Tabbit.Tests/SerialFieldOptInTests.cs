using System.Linq;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// Folding consecutively numbered columns into one array is off unless asked for.
/// </summary>
/// <remarks>
/// It used to be on for every table of the layout that has the convention, and being wrong
/// about it is quiet: three columns become one field under a name the sheet never used, and a
/// consumer reads an array where the author wrote three separate things. `Text1`/`Text2`
/// usually is one array of two; `Condition_1`/`Condition_2`/`Condition_3` of one real workbook
/// are three different enums.
///
/// A name cannot answer which, so the author does. These tests are what keeps the default from
/// drifting back - the goldens cannot, because every fixture that covers folding now asks for
/// it and would look the same either way.
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

    [Fact]
    public void Numbered_columns_stay_separate_by_default()
    {
        var table = NumberedColumns();

        Assert.False(table.FoldSerialFields);

        var groups = table.SerialFields;

        Assert.Equal(new[] { "Index", "Text1", "Text2" }, groups.Select(g => g.Name));
        Assert.All(groups, g => Assert.False(g.IsArray));
    }

    [Fact]
    public void Numbered_columns_fold_when_the_table_asks()
    {
        var table = NumberedColumns();
        table.FoldSerialFields = true;

        var groups = table.SerialFields;

        // The folded group takes a name the sheet never used, which is one of the reasons
        // this is the author's call rather than the default.
        Assert.Equal(new[] { "Index", "Text_array" }, groups.Select(g => g.Name));
        Assert.True(groups[1].IsArray);
        Assert.Equal(2, groups[1].Fields.Count);
    }

    /// <summary>
    /// A recipe entry that does not mention it gets it off.
    /// </summary>
    /// <remarks>
    /// The default lives in two places that have to agree - the recipe property and the model
    /// - and a mismatch would mean sheets folding because nothing set the flag either way.
    /// </remarks>
    [Fact]
    public void The_recipe_default_is_off()
    {
        var recipe = new Tabbit.Recipe.RecipeModel.SourceRecipeGroup.XlsxRecipe();

        Assert.False(recipe.FoldSerialFields);
    }
}
