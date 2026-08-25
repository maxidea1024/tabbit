using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A column group typed by a schema file, and the same table typed by its own cells.
/// </summary>
/// <remarks>
/// **The comparison is the gate.** `SchemaParserTests` checks the notation and
/// `SchemaDeclarationsTests` checks what a set of files means; everything below those rests
/// on one claim - that a group whose type cell names a struct arrives as the columns a sheet
/// could have typed by hand. Two workbooks hold the same table under the same name, one
/// written each way, and the produced files must be identical byte for byte.
///
/// A binding that did not happen fails here, and so does one that read a member as the wrong
/// type or in the wrong order. Neither would need an assertion written for it.
///
/// notes/struct-dsl-design.md section 7.2.
/// </remarks>
public class SchemaBindingTests
{
    private static void Convert(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Converting `{scenario}` failed.{System.Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// The two ways of writing the table produce the same file.
    /// </summary>
    [Fact]
    public void A_declared_group_and_its_written_members_reach_the_same_file()
    {
        Convert("declared");
        Convert("declared-expanded");

        byte[] fromSchema = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("declared"), "binary", "Loadout.tcb"));

        byte[] fromCells = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("declared-expanded"), "binary", "Loadout.tcb"));

        Assert.Equal(fromCells, fromSchema);
    }

    /// <summary>
    /// And the same JSON, which is where a difference is readable.
    /// </summary>
    /// <remarks>
    /// The byte comparison above is the stronger claim and the worse report - two files that
    /// differ say so in an offset. This one says which member of which row, so a failing run
    /// names the column that was read wrongly.
    /// </remarks>
    [Fact]
    public void The_two_ways_of_typing_the_group_produce_the_same_json()
    {
        Convert("declared");
        Convert("declared-expanded");

        Assert.Equal(Json("declared-expanded"), Json("declared"));
    }

    private static string Json(string scenario)
        => File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(scenario), "json-named", "Loadout.json"));

    /// <summary>
    /// The members reach the values they name.
    /// </summary>
    /// <remarks>
    /// The equivalence gate would pass if both sides were wrong in the same way, which they
    /// cannot be - one side writes its types out - but nothing in it says what the values
    /// are. Reading a few out is what makes a failure legible without opening a workbook.
    /// </remarks>
    [Fact]
    public void A_declared_member_reads_as_the_type_it_was_declared()
    {
        Convert("declared");

        var rows = JsonDocument.Parse(Json("declared")).RootElement;
        var first = rows[0];

        // Numbers rather than the text of numbers: a column left for the declaration to type
        // is carried as text while the sheet is read, and this is what says it was read again
        // once there was a type to read it as.
        Assert.Equal(10, first.GetProperty("slot")[0].GetProperty("itemId").GetInt32());
        Assert.Equal(1, first.GetProperty("slot")[0].GetProperty("count").GetInt32());
        Assert.Equal("icon_a", first.GetProperty("slot")[0].GetProperty("icon").GetString());

        // The enum the schema file declares, named by an ordinary type row.
        Assert.Equal(1, first.GetProperty("grade").GetInt32());
    }

    /// <summary>
    /// The description a declaration carries reaches the generated code.
    /// </summary>
    /// <remarks>
    /// **The one thing a declaration supplies that the wire does not carry**, so the two
    /// comparisons above cannot see it. A sheet moving to this notation empties its
    /// description cells along with its type cells, and what would be lost by that is
    /// exactly this.
    ///
    /// Read out of the generated file rather than held to a golden tree: what is being
    /// checked is that the sentence arrived, not how C# spells a doc comment.
    /// </remarks>
    [Fact]
    public void A_members_description_reaches_the_generated_code()
    {
        Convert("declared");

        string emitted = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir("declared"), "csharp", "tables", "LoadoutTable.cs"));

        Assert.Contains("Which item, as that table's key.", emitted);
        Assert.Contains("Shown beside the count. Blank where there is nothing to show.", emitted);

        // And the sheet's own wins where it wrote one - `Slot1.Count` is typed and described
        // by both sides, and the sheet is the one that answers.
        Assert.Contains("How many of it.", emitted);
    }

    // ------------------------------------------------------------------ refusals

    /// <summary>
    /// A sheet and a declaration that disagree say so, for every column rather than the
    /// first, and name which of the two disagrees with which.
    /// </summary>
    /// <remarks>
    /// The same workbook the two gates above convert, against declarations that are wrong
    /// three ways. Correcting a sheet that has moved to this notation is a matter of reading
    /// a list, and a run that stopped at the first mistake would make it one run per mistake.
    /// </remarks>
    [Fact]
    public void A_declaration_that_disagrees_with_the_sheet_says_so_everywhere()
    {
        var result = TabbitRunner.Convert("declared-mismatch");

        Assert.False(result.Succeeded, "Declarations that disagree with the sheet were accepted.");

        // A column that wrote its own type is checked rather than overwritten, and the report
        // quotes both sides so it is clear which is being changed.
        Assert.Contains(
            "`Loadout.Slot[1].Count` is typed `int` and `Reward.count` is declared `string`",
            result.StdOut);

        // Every element of the member, not the first: they are separate cells and each is
        // somebody's to fix.
        Assert.Contains("`Loadout.Slot[1].Count` is typed `int`", result.StdOut);

        // A column the struct has no member for.
        Assert.Contains(
            "`Loadout.Slot[1].Icon` is in a group typed `Reward`, and `Reward` has no member",
            result.StdOut);

        // And a member the sheet gave no column, reported against the declaration - which is
        // a line of a text file, so the report points into it the way a compiler's does.
        Assert.Contains("has no column for its member `bonus`", result.StdOut);
        Assert.Contains("loadout.tbs(", result.StdOut);
    }

    /// <summary>
    /// A column already reported against its declaration is not reported again for having no
    /// type.
    /// </summary>
    /// <remarks>
    /// The empty type cell is allowed only on the promise that a group will fill it in, and
    /// the check that the promise was kept is what makes allowing it safe. But a column whose
    /// group tried and failed has been accounted for - saying it twice would put a second
    /// report between the reader and the cause of the first.
    /// </remarks>
    [Fact]
    public void A_column_reported_once_is_not_reported_twice()
    {
        var result = TabbitRunner.Convert("declared-mismatch");

        Assert.DoesNotContain("has an empty type cell and is in no group", result.StdOut);
    }

    // ------------------------------------------------------------------ metadata

    /// <summary>
    /// A key nothing reads is reported, and the report says which kind of nothing it is.
    /// </summary>
    /// <remarks>
    /// The parser carries every key without checking one, which is what lets a project write
    /// its own. The other half of that policy is this: once every declaration is in, a key
    /// nobody claimed is named - and a key the notation defines is told apart from one
    /// nothing defines at all, because the two send the reader somewhere different.
    /// </remarks>
    [Fact]
    public void A_key_nothing_reads_says_which_kind_of_nothing_it_is()
    {
        var result = TabbitRunner.Convert("declared-mismatch");

        // Nobody's key. A misspelling, almost always.
        Assert.Contains("`mn` on `Reward.count` is not a key anything reads", result.StdOut);

        // Defined by the notation, not acted on by this build. Refused rather than ignored,
        // because ignoring it leaves somebody believing a check is running.
        Assert.Contains(
            "`uniqueBy` on `Reward.bonus` is a key this notation defines and this build does not act on",
            result.StdOut);
    }

    /// <summary>
    /// A bound written in a declaration reaches the check a bound written in a sheet reaches.
    /// </summary>
    /// <remarks>
    /// The whole of what makes a declared constraint worth writing. Reported at the cell that
    /// breaks it, not at the declaration: the declaration is right and the value is not.
    /// </remarks>
    [Fact]
    public void A_declared_bound_refuses_the_cell_that_breaks_it()
    {
        var result = TabbitRunner.Convert("declared-constraints");

        Assert.False(result.Succeeded, "A value below the declared minimum was accepted.");

        Assert.Contains("is 1, below the minimum 2 the column declares", result.StdOut);
        Assert.Contains("declared.xlsx", result.StdOut);
    }

    /// <summary>
    /// The constraints that had no home in the model until now reach the cell as well.
    /// </summary>
    /// <remarks>
    /// `notDefault` and `regex` on the same member, and both fire on the same blank cells -
    /// which is the point of `notDefault`. A `string` reads a blank as an empty string, so a
    /// column where the empty string means nothing has no other way to refuse one: to
    /// everything downstream a blank cell and a written empty string are one value.
    /// </remarks>
    [Fact]
    public void The_declared_pattern_and_default_refusal_reach_the_cell()
    {
        var result = TabbitRunner.Convert("declared-constraints");

        Assert.False(result.Succeeded, "A value the declaration refuses was accepted.");

        Assert.Contains("the column refuses the type's own empty value", result.StdOut);
        Assert.Contains("does not match the pattern `^icon_[a-z]$`", result.StdOut);
    }

    /// <summary>
    /// A declared role reaches the column, and the run says so.
    /// </summary>
    /// <remarks>
    /// `icon` is declared `(asset=icon)` and the recipe configures no folders, so the run
    /// says two columns went unchecked - which is the report saying the role arrived. Both
    /// elements of the group, from one declaration.
    ///
    /// A role does not reach the wire, so the equivalence gate above passes with the keys
    /// written on one side only. That is the other half of what this checks.
    /// </remarks>
    [Fact]
    public void A_declared_role_reaches_every_column_of_the_group()
    {
        var result = TabbitRunner.Convert("declared");

        Assert.True(result.Succeeded, result.Describe());
        Assert.Contains("2 column(s) are typed `asset`", result.StdOut);
    }
}
