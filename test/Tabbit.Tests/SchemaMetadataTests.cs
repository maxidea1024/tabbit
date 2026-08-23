using System.Linq;
using Tabbit.Models;
using Tabbit.Schema;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A declaration's constraints meeting a column's own.
/// </summary>
/// <remarks>
/// **Both, and never one instead of the other.** A declaration says what is true of the type
/// wherever it is used and a sheet's rows say what is true of one column, so a value has to
/// satisfy each - which means a column may tighten what the type promises and never loosen
/// it. That is what lets somebody read a schema file and know the floor; if a sheet could
/// widen a bound, the bound would guarantee nothing and the declaration would be a comment.
///
/// Driven directly rather than through a workbook. What is being checked is the arithmetic of
/// meeting two statements, and a fixture pair per case would say the same thing at the cost
/// of a workbook each. The end of the road - a bound reaching the cell-level check - is a
/// fixture, in `SchemaBindingTests`.
///
/// notes/struct-dsl-design.md section 6.3.
/// </remarks>
public class SchemaMetadataTests
{
    private static SchemaField Member(string declaration)
    {
        var diagnostics = new Diagnostics();

        var file = SchemaParser.Parse(
            $"struct S\n    field x {declaration}\n", "s.tbs", diagnostics);

        Assert.True(diagnostics.Count == 0,
            string.Join("\n", diagnostics.Entries.Select(entry => entry.Detail.Message)));

        return file.Structs[0].Fields[0];
    }

    private static Field Column(ValueType type = ValueType.Int32)
    {
        var where = new Location { Filename = "book.xlsx", Sheet = "T" };

        var table = new Table
        {
            Location = where,
            RawName = "Items",
            Name = "Items",
            Comment = "",
        };

        return new Field
        {
            NameLocation = where,
            TypeLocation = where,
            DetailTypeLocation = where,
            TargetSideLocation = where,
            OwnerTable = table,
            RawName = "X",
            Name = "X",
            Comment = "",
            Type = type,
            TypeName = type == ValueType.String ? "string" : "int",
        };
    }

    private static Diagnostics Apply(Field field, string declaration)
    {
        var diagnostics = new Diagnostics();
        SchemaMetadata.Apply(field.OwnerTable, field, Member(declaration), diagnostics);
        return diagnostics;
    }

    private static string Reported(Diagnostics diagnostics)
        => string.Join("\n", diagnostics.Entries.Select(entry => entry.Detail.Message));

    // -------------------------------------------------------------------- bounds

    [Fact]
    public void A_declared_bound_is_taken_where_the_column_set_none()
    {
        var field = Column();
        Apply(field, "int (min=1, max=99)");

        Assert.Equal(1, field.Constraints.Minimum);
        Assert.Equal(99, field.Constraints.Maximum);
    }

    /// <summary>
    /// The tighter of the two survives, whichever side wrote it.
    /// </summary>
    [Theory]
    [InlineData(5.0, "int (min=1)", 5.0)]
    [InlineData(1.0, "int (min=5)", 5.0)]
    public void The_higher_minimum_is_the_one_that_stands(
        double sheet, string declaration, double expected)
    {
        var field = Column();
        field.Constraints.Minimum = sheet;

        Apply(field, declaration);

        Assert.Equal(expected, field.Constraints.Minimum);
    }

    [Theory]
    [InlineData(10.0, "int (max=99)", 10.0)]
    [InlineData(99.0, "int (max=10)", 10.0)]
    public void The_lower_maximum_is_the_one_that_stands(
        double sheet, string declaration, double expected)
    {
        var field = Column();
        field.Constraints.Maximum = sheet;

        Apply(field, declaration);

        Assert.Equal(expected, field.Constraints.Maximum);
    }

    [Fact]
    public void A_bound_that_is_not_a_number_is_refused()
        => Assert.Contains("a bound is a number", Reported(Apply(Column(), "int (min=low)")));

    // ------------------------------------------------------------------- allowed

    [Fact]
    public void A_declared_whitelist_is_taken_where_the_column_set_none()
    {
        var field = Column(ValueType.String);
        Apply(field, "string (allowed=a;b;c)");

        Assert.Equal(["a", "b", "c"], field.Constraints.AllowedValues);
    }

    /// <summary>
    /// Two whitelists meet at what they have in common, which is the same rule the bounds
    /// follow said about a set.
    /// </summary>
    [Fact]
    public void Two_whitelists_meet_at_what_they_share()
    {
        var field = Column(ValueType.String);
        field.Constraints.AllowedValues = ["a", "b", "c"];

        Apply(field, "string (allowed=b;c;d)");

        Assert.Equal(["b", "c"], field.Constraints.AllowedValues);
    }

    /// <summary>
    /// And two that share nothing are refused rather than stored.
    /// </summary>
    /// <remarks>
    /// Stored, it would be a column no value can satisfy - every row would fail against it,
    /// and not one of those reports would name the two lists that cannot both be met.
    /// </remarks>
    [Fact]
    public void Two_whitelists_with_nothing_in_common_are_refused()
    {
        var field = Column(ValueType.String);
        field.Constraints.AllowedValues = ["a", "b"];

        string reported = Reported(Apply(field, "string (allowed=c;d)"));

        Assert.Contains("nothing in common", reported);
        Assert.Contains("can hold nothing at all", reported);

        // Left as the sheet had it, so the run's other reports are about values rather than
        // about a whitelist this decided to invent.
        Assert.Equal(["a", "b"], field.Constraints.AllowedValues);
    }

    [Fact]
    public void An_empty_whitelist_is_refused()
        => Assert.Contains("lists no values", Reported(Apply(Column(ValueType.String), "string (allowed=;)")));

    // --------------------------------------------------------------------- roles

    [Fact]
    public void A_declared_role_reaches_the_column()
    {
        var field = Column(ValueType.String);
        Apply(field, "string (asset=icon)");

        Assert.Equal(StringRole.Asset, field.Role);
        Assert.Equal("icon", field.RoleGroup);
    }

    [Fact]
    public void The_text_role_reaches_the_column()
    {
        var field = Column(ValueType.String);
        Apply(field, "string (text)");

        Assert.Equal(StringRole.Text, field.Role);
    }

    /// <summary>
    /// A column that already said what its strings are for said it about that column, which
    /// is the more particular statement of the two.
    /// </summary>
    [Fact]
    public void A_role_the_column_already_has_is_left_alone()
    {
        var field = Column(ValueType.String);
        field.Role = StringRole.Text;

        Apply(field, "string (asset=icon)");

        Assert.Equal(StringRole.Text, field.Role);
    }

    [Fact]
    public void A_role_on_something_that_is_not_a_string_is_refused()
        => Assert.Contains(
            "says what a string is for",
            Reported(Apply(Column(), "int (text)")));

    [Fact]
    public void Both_roles_at_once_are_refused()
        => Assert.Contains(
            "has one thing it is for",
            Reported(Apply(Column(ValueType.String), "string (text, asset=icon)")));
}
