using System.Collections.Generic;
using Tabbit.Recipe;
using Tabbit.Sources;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Which workbooks a source entry reads, and which of their sheets.
/// </summary>
/// <remarks>
/// Written against the filter rather than against workbooks on disk. Every question here is
/// about what a pattern covers - a name, a path, a workbook-qualified sheet - and as fixtures
/// each would be another .xlsx that has to be opened in Excel to review, for a decision that
/// is made before a file is opened at all.
///
/// The reason the workbook level exists at all is in the last group: sheet names repeat across
/// workbooks. A directory where two files both hold a `Define` tab cannot be described by sheet
/// name alone, and the unqualified list quietly drops both.
/// </remarks>
public class SheetFilterTests
{
    private const string Section = "Sources.Xlsx[0]";

    private static SheetFilter Filter(
        IEnumerable<string> includeWorkbooks = null,
        IEnumerable<string> excludeWorkbooks = null,
        IEnumerable<string> includeSheets = null,
        IEnumerable<string> excludeSheets = null)
    {
        var recipe = new RecipeModel.SourceRecipeGroup.XlsxRecipe();

        if (includeWorkbooks is not null)
            recipe.IncludeWorkbooks = new List<string>(includeWorkbooks);

        if (excludeWorkbooks is not null)
            recipe.ExcludeWorkbooks = new List<string>(excludeWorkbooks);

        if (includeSheets is not null)
            recipe.IncludeSheets = new List<string>(includeSheets);

        if (excludeSheets is not null)
            recipe.ExcludeSheets = new List<string>(excludeSheets);

        return SheetFilter.From(recipe, Section);
    }

    #region Nothing named

    /// <summary>
    /// An entry naming no list reads the whole directory, which is what every recipe written
    /// before the workbook lists existed holds.
    /// </summary>
    [Fact]
    public void An_entry_naming_nothing_takes_every_workbook_and_sheet()
    {
        var filter = Filter();

        Assert.True(filter.IncludesWorkbook("Items.xlsx"));
        Assert.True(filter.IncludesWorkbook("backup/Items 사본.xlsx"));
        Assert.True(filter.Includes("Items.xlsx", "ItemTable"));
        Assert.True(filter.Includes("backup/Items 사본.xlsx", "Define"));

        // And nothing to report, so a run over any directory succeeds.
        filter.ReportUnmatchedIncludes(Section, ["Items.xlsx"], [("Items.xlsx", "ItemTable")]);
    }

    #endregion

    #region Workbooks

    [Fact]
    public void A_workbook_is_excluded_by_name_by_bare_name_or_by_path()
    {
        Assert.False(Filter(excludeWorkbooks: ["Items.xlsx"]).IncludesWorkbook("Items.xlsx"));
        Assert.False(Filter(excludeWorkbooks: ["Items"]).IncludesWorkbook("Items.xlsx"));
        Assert.False(Filter(excludeWorkbooks: ["shared/Items.xlsx"]).IncludesWorkbook("shared/Items.xlsx"));

        // The name alone reaches a workbook in a subdirectory, because that is what somebody
        // naming one workbook means.
        Assert.False(Filter(excludeWorkbooks: ["Items.xlsx"]).IncludesWorkbook("shared/Items.xlsx"));

        // A path does not reach a workbook of the same name elsewhere, because that is what
        // writing the path is for.
        Assert.True(Filter(excludeWorkbooks: ["shared/Items.xlsx"]).IncludesWorkbook("Items.xlsx"));
    }

    /// <summary>
    /// The two the requirement is about: a directory holds workbooks kept for reference and
    /// workbooks whose contents were never tabular, and those are named by where they sit or
    /// by what they are called.
    /// </summary>
    [Fact]
    public void A_directory_and_a_format_can_be_excluded_at_once()
    {
        var filter = Filter(excludeWorkbooks: ["백업/*", "*_참고용*"]);

        Assert.False(filter.IncludesWorkbook("백업/Items.xlsx"));
        Assert.False(filter.IncludesWorkbook("백업/2026/Items.xlsx"));
        Assert.False(filter.IncludesWorkbook("Items_참고용.xlsx"));
        Assert.True(filter.IncludesWorkbook("Items.xlsx"));
        Assert.True(filter.IncludesWorkbook("shared/Items.xlsx"));
    }

    [Fact]
    public void An_include_list_narrows_to_what_it_names_and_exclude_runs_after_it()
    {
        var filter = Filter(includeWorkbooks: ["UWO_*"], excludeWorkbooks: ["*.xlsb"]);

        Assert.True(filter.IncludesWorkbook("UWO_TownNpc.xlsx"));
        Assert.False(filter.IncludesWorkbook("UWO_보상.xlsb"));
        Assert.False(filter.IncludesWorkbook("Items.xlsx"));
    }

    /// <summary>
    /// A workbook is the same workbook however its name is cased, because Windows says so and
    /// because the name is typed by hand in two places.
    /// </summary>
    [Fact]
    public void Workbook_names_are_matched_without_case()
    {
        Assert.False(Filter(excludeWorkbooks: ["items.XLSX"]).IncludesWorkbook("Items.xlsx"));
    }

    #endregion

    #region The same sheet name in two workbooks

    /// <summary>
    /// The case the qualifier exists for: `Define` is a table in one workbook and a scratch
    /// tab in another.
    /// </summary>
    [Fact]
    public void A_qualified_pattern_reaches_one_workbooks_sheet_only()
    {
        var filter = Filter(excludeSheets: ["[UWO_테이블.xlsb]Define"]);

        Assert.False(filter.Includes("UWO_테이블.xlsb", "Define"));
        Assert.True(filter.Includes("UWO_TownNpc.xlsx", "Define"));
    }

    /// <summary>
    /// A pattern with no workbook keeps meaning every workbook, which is what recipes written
    /// before the qualifier existed rely on.
    /// </summary>
    [Fact]
    public void An_unqualified_pattern_still_applies_to_every_workbook()
    {
        var filter = Filter(excludeSheets: ["Define"]);

        Assert.False(filter.Includes("UWO_테이블.xlsb", "Define"));
        Assert.False(filter.Includes("UWO_TownNpc.xlsx", "Define"));
    }

    /// <summary>
    /// Both halves are globs, so one line covers the sheet that repeats across a family of
    /// workbooks - which is what a map keyed by workbook has to spell out once per workbook.
    /// </summary>
    [Fact]
    public void Both_halves_of_a_qualified_pattern_are_globs()
    {
        var filter = Filter(excludeSheets: ["[UWO_*.xlsb]Ref*"]);

        Assert.False(filter.Includes("UWO_보상.xlsb", "RefCharacter"));
        Assert.False(filter.Includes("UWO_퀘스트.xlsb", "RefTownDialogue"));
        Assert.True(filter.Includes("UWO_TownNpc.xlsx", "RefCharacter"));
        Assert.True(filter.Includes("UWO_보상.xlsb", "RewardPath"));
    }

    [Fact]
    public void A_qualified_include_takes_that_workbooks_sheet_and_no_other()
    {
        var filter = Filter(includeSheets: ["[Items.xlsx]ItemTable"]);

        Assert.True(filter.Includes("Items.xlsx", "ItemTable"));
        Assert.False(filter.Includes("Items.xlsx", "Define"));

        // A whitelist is a whitelist: another workbook was not named, so nothing in it is
        // asked for.
        Assert.False(filter.Includes("Monsters.xlsx", "ItemTable"));
    }

    /// <summary>
    /// The workbook half is matched the way the workbook lists match, so one workbook can be
    /// named three ways here as well.
    /// </summary>
    [Fact]
    public void The_workbook_half_takes_a_bare_name_or_a_path()
    {
        Assert.False(Filter(excludeSheets: ["[Items]Define"]).Includes("Items.xlsx", "Define"));
        Assert.False(Filter(excludeSheets: ["[shared/Items.xlsx]Define"])
            .Includes("shared/Items.xlsx", "Define"));
        Assert.True(Filter(excludeSheets: ["[shared/Items.xlsx]Define"])
            .Includes("Items.xlsx", "Define"));
    }

    #endregion

    #region What a name that matched nothing gets

    [Fact]
    public void An_include_naming_a_workbook_that_is_not_there_is_reported()
    {
        var filter = Filter(includeWorkbooks: ["Items.xlsx", "Monsters.xlsx"]);

        Assert.True(filter.IncludesWorkbook("Items.xlsx"));

        var thrown = Assert.Throws<TabbitException>(() =>
            filter.ReportUnmatchedIncludes(Section, ["Items.xlsx", "Npc.xlsx"], []));

        Assert.Contains("Monsters.xlsx", thrown.Message);

        // With what is there, so the answer to a typo is in the message.
        Assert.Contains("Npc.xlsx", thrown.Message);
        Assert.DoesNotContain("sheet(s)", thrown.Message);
    }

    /// <summary>
    /// A qualified include that matched nothing is answered with qualified names, because the
    /// sheet usually does exist - in a workbook the pattern did not name.
    /// </summary>
    [Fact]
    public void An_unmatched_qualified_include_is_answered_with_the_workbook_each_sheet_was_in()
    {
        var filter = Filter(includeSheets: ["[Items.xlsx]ItemTable"]);

        var thrown = Assert.Throws<TabbitException>(() =>
            filter.ReportUnmatchedIncludes(
                Section,
                ["Items.xlsx", "Monsters.xlsx"],
                [("Monsters.xlsx", "ItemTable"), ("Items.xlsx", "Define")]));

        Assert.Contains("[Monsters.xlsx]ItemTable", thrown.Message);
        Assert.Contains("[Items.xlsx]Define", thrown.Message);
    }

    /// <summary>
    /// An unqualified list gets the message it always got, so a recipe that does not use the
    /// qualifier reads the same error it read before.
    /// </summary>
    [Fact]
    public void An_unmatched_plain_include_is_answered_with_plain_names()
    {
        var filter = Filter(includeSheets: ["StageTable"]);

        var thrown = Assert.Throws<TabbitException>(() =>
            filter.ReportUnmatchedIncludes(Section, ["Items.xlsx"], [("Items.xlsx", "ItemTable")]));

        Assert.Contains("StageTable", thrown.Message);
        Assert.Contains("Sheets that are there: ItemTable", thrown.Message);
    }

    /// <summary>
    /// A sheet the recipe asks for in a workbook the recipe skips: the sheet list cannot
    /// explain that, so the skipped workbooks are named too.
    /// </summary>
    [Fact]
    public void A_sheet_asked_for_in_a_skipped_workbook_says_which_workbook_was_skipped()
    {
        var filter = Filter(
            excludeWorkbooks: ["백업/*"],
            includeSheets: ["[백업/Items.xlsx]ItemTable"]);

        Assert.False(filter.IncludesWorkbook("백업/Items.xlsx"));

        var thrown = Assert.Throws<TabbitException>(() =>
            filter.ReportUnmatchedIncludes(Section, ["백업/Items.xlsx"], []));

        Assert.Contains("Workbooks this entry skips: 백업/Items.xlsx", thrown.Message);
    }

    /// <summary>
    /// A sheet named by both lists still counts as found. It is there; the recipe just does
    /// not want it, and that is not a typo to report.
    /// </summary>
    [Fact]
    public void A_sheet_that_both_lists_name_is_not_reported_as_missing()
    {
        var filter = Filter(includeSheets: ["Item*"], excludeSheets: ["[Items.xlsx]ItemNotes"]);

        Assert.True(filter.Includes("Items.xlsx", "ItemTable"));
        Assert.False(filter.Includes("Items.xlsx", "ItemNotes"));

        filter.ReportUnmatchedIncludes(
            Section, ["Items.xlsx"], [("Items.xlsx", "ItemTable"), ("Items.xlsx", "ItemNotes")]);
    }

    #endregion

    #region Patterns that are not patterns

    /// <summary>
    /// A bracket that never closes is a typo, and reading it as a sheet whose name starts with
    /// `[` would drop a table with nothing said.
    /// </summary>
    [Fact]
    public void A_workbook_name_that_is_never_closed_is_refused()
    {
        var thrown = Assert.Throws<TabbitException>(() => Filter(excludeSheets: ["[Items.xlsx"]));

        Assert.Contains("`[workbook]sheet`", thrown.Message);
        Assert.Contains(Section, thrown.Message);
    }

    [Fact]
    public void A_qualifier_naming_no_workbook_is_refused()
    {
        Assert.Throws<TabbitException>(() => Filter(excludeSheets: ["[]Define"]));
    }

    /// <summary>
    /// A whole workbook is what the workbook lists are for, and having two spellings of it
    /// would mean two places to look when a workbook is unexpectedly missing.
    /// </summary>
    [Fact]
    public void A_qualifier_naming_no_sheet_is_pointed_at_the_workbook_list()
    {
        var thrown = Assert.Throws<TabbitException>(() => Filter(excludeSheets: ["[Items.xlsx]"]));

        Assert.Contains("ExcludeWorkbooks", thrown.Message);
    }

    [Fact]
    public void A_qualified_pattern_in_the_workbook_list_is_refused()
    {
        var thrown = Assert.Throws<TabbitException>(
            () => Filter(excludeWorkbooks: ["[Items.xlsx]Define"]));

        Assert.Contains("ExcludeSheets", thrown.Message);
    }

    /// <summary>
    /// Blank entries are dropped rather than matching everything, which is what a trailing
    /// comma or an emptied-out line leaves behind.
    /// </summary>
    [Fact]
    public void Blank_entries_are_dropped()
    {
        var filter = Filter(excludeWorkbooks: ["", "  "], excludeSheets: ["", " "]);

        Assert.True(filter.IncludesWorkbook("Items.xlsx"));
        Assert.True(filter.Includes("Items.xlsx", "ItemTable"));
    }

    #endregion
}
