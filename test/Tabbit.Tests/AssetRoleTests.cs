using System;
using System.IO;
using System.Text.Json;
using Tabbit.Cooking;
using Tabbit.Models;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// Columns typed `asset`: a string naming a file that has to exist.
/// </summary>
/// <remarks>
/// No golden tree, unlike `text`. This role produces no output of its own - the value goes
/// out through the ordinary exports and always did - so what there is to pin is what the run
/// says and whether it stops, and a tree of files cannot state either.
///
/// The four recipes over one fixture are the feature: the same sheets and the same folders,
/// differing only in what a value naming nothing amounts to. That is the arrangement the
/// roadmap's earlier refusal was missing - "the game build knows whether an asset exists" was
/// right about the tool not knowing, and wrong that a recipe cannot tell it.
/// </remarks>
public class AssetRoleTests
{
    /// <summary>
    /// The default: a value naming nothing is a warning, and the conversion finishes.
    /// </summary>
    /// <remarks>
    /// The case the setting exists for. A designer fills in the row today and the icon lands
    /// next week, and a converter that refuses until every asset is drawn stops work for a
    /// reason that is not the data's.
    /// </remarks>
    [Fact]
    public void A_missing_asset_is_a_warning_by_default()
    {
        var result = TabbitRunner.Convert("asset");

        Assert.True(result.Succeeded, result.Describe());

        // And the data really was written, rather than the run merely not failing.
        Assert.True(File.Exists(Path.Combine(
            RepoLayout.OutputDir("asset"), "json-named", "Item.json")));

        Assert.Contains("Icon_Missing", result.Describe());
    }

    /// <summary>
    /// The report names the cell, which is the point of checking here rather than after.
    /// </summary>
    /// <remarks>
    /// A script over the exported JSON can say the value is wrong. It cannot say which cell of
    /// which sheet to open, and that is most of the work of fixing one.
    /// </remarks>
    [Fact]
    public void A_report_names_the_cell()
    {
        var result = TabbitRunner.Convert("asset");

        Assert.Contains("asset.xlsx : Asset : F8", result.Describe());
    }

    /// <summary>
    /// The kind decides which folders a value is looked for in.
    /// </summary>
    /// <remarks>
    /// The whole reason a kind exists. `Icon_Sword` is a real file and resolves in the icon
    /// column; the same name in the sound column resolves nowhere, because sounds are
    /// somewhere else. Without the kind it would be a valid sound.
    /// </remarks>
    [Fact]
    public void A_kind_decides_which_folder_answers()
    {
        var result = TabbitRunner.Convert("asset");

        Assert.Contains("`Item.Sound` names `Icon_Sword`", result.Describe());

        // The same value in the icon column is not reported.
        Assert.DoesNotContain("`Item.Icon` names `Icon_Sword`", result.Describe());
    }

    /// <summary>
    /// Every element of a list cell is checked, not the delimited cell as one name.
    /// </summary>
    [Fact]
    public void Every_element_of_a_list_cell_is_checked()
    {
        var result = TabbitRunner.Convert("asset");

        Assert.Contains("`Item.Extras` names `Icon_Nope`", result.Describe());

        // Its neighbour in the same cell resolves, so the cell is not reported as a whole.
        Assert.DoesNotContain("Icon_Sword;Icon_Nope", result.Describe());
    }

    /// <summary>
    /// A blank cell is not a missing file.
    /// </summary>
    [Fact]
    public void A_blank_cell_is_not_a_missing_asset()
    {
        var result = TabbitRunner.Convert("asset");

        // Row 2 leaves `Sound` blank. Three values do not resolve and none of them is that.
        Assert.Equal(3, Occurrences(result.Describe(), "and no file of that name is in"));
    }

    /// <summary>
    /// The kind is read from the detail-type cell as well as from the brackets.
    /// </summary>
    /// <remarks>
    /// `Item.Portrait` is typed `asset` with `icon` in the detail-type row, which is where
    /// this layout puts an enum's name and a reference's target. Its values resolve, so the
    /// proof is that they are not reported - a kind that had not been read would have been no
    /// kind at all, and that has no folder configured.
    /// </remarks>
    [Fact]
    public void The_kind_may_be_written_in_the_detail_type_cell()
    {
        var result = TabbitRunner.Convert("asset");

        Assert.True(result.Succeeded, result.Describe());
        Assert.DoesNotContain("Item.Portrait", result.Describe());
    }

    /// <summary>
    /// The pattern narrows what counts as a file, so the tree's other contents do not answer.
    /// </summary>
    /// <remarks>
    /// `notes.txt` sits in the icon folder. A root scanned with `*` would let a value naming
    /// it pass, which is a check that means nothing.
    /// </remarks>
    [Fact]
    public void The_pattern_decides_what_counts_as_a_file()
    {
        Assert.True(File.Exists(
            Path.Combine(RepoLayout.Root, "test", "fixtures", "assets", "icon", "notes.txt")),
            "The fixture's icon folder is supposed to hold a file the pattern excludes.");

        var result = TabbitRunner.Convert("asset");

        Assert.True(result.Succeeded, result.Describe());
    }


    // ------------------------------------------------------------------ saying "not this time"

    /// <summary>
    /// `OnMissing: error` stops the run and names every cell at once.
    /// </summary>
    [Fact]
    public void OnMissing_error_stops_the_run()
    {
        var result = TabbitRunner.Convert("asset-strict");

        Assert.False(result.Succeeded, result.Describe());
        Assert.Contains("did not pass validation", result.Describe());

        // All three, not the first: fixing a sheet one report per run is the thing the
        // collector exists to avoid.
        Assert.Equal(3, Occurrences(result.Describe(), "and no file of that name is in"));
    }

    /// <summary>
    /// `TreatWarningsAsErrors` promotes them, so one recipe serves both audiences.
    /// </summary>
    /// <remarks>
    /// The arrangement this was shaped around, and the reason no new flag was added: the
    /// switch was already there for the validation rules. The people writing data get the
    /// warning; the build that ships does not get to ignore it. The recipe is otherwise
    /// `asset.json` exactly - `OnMissing` is still `warn`.
    /// </remarks>
    [Fact]
    public void Warnings_are_promoted_for_the_build_that_ships()
    {
        var lenient = TabbitRunner.Convert("asset");
        var ci = TabbitRunner.Convert("asset-ci");

        Assert.True(lenient.Succeeded, lenient.Describe());
        Assert.False(ci.Succeeded, ci.Describe());

        Assert.Equal(3, Occurrences(ci.Describe(), "and no file of that name is in"));
    }

    /// <summary>
    /// No `Assets` section switches the check off, and says so.
    /// </summary>
    /// <remarks>
    /// Both halves matter. Off, because an `asset` column says what it says whether or not
    /// anybody has wired up a content tree, and a project adopting this should be able to type
    /// its columns first. Said, because silence reads exactly like a check that ran and found
    /// nothing - which is the reading that ships broken references.
    /// </remarks>
    [Fact]
    public void No_configured_roots_switches_the_check_off_and_says_so()
    {
        var result = TabbitRunner.Convert("asset-unconfigured");

        Assert.True(result.Succeeded, result.Describe());

        Assert.Contains("4 column(s) are typed `asset`", result.Describe());
        Assert.Contains("no folders are configured", result.Describe());

        // And nothing was reported about the values themselves.
        Assert.DoesNotContain("no file of that name is in", result.Describe());
    }


    // ------------------------------------------------------------------ and nothing else changed

    /// <summary>
    /// An `asset` column reaches the output as the string it is.
    /// </summary>
    /// <remarks>
    /// The same claim `text` makes, and for the same reason: the role is not a `ValueType`, so
    /// nothing downstream can treat it as one.
    /// </remarks>
    [Fact]
    public void Asset_columns_export_as_ordinary_strings()
    {
        TabbitRunner.Convert("asset");

        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("asset"), "json-named", "Item.json"));

        var rows = JsonDocument.Parse(json).RootElement;

        Assert.Equal("Icon_Sword", rows[0].GetProperty("icon").GetString());

        // The unresolved one is exported unchanged. Reporting it is not the same as
        // withholding it - the row is what the sheet says, and a warning is a warning.
        Assert.Equal("Icon_Missing", rows[2].GetProperty("icon").GetString());
    }


    // ------------------------------------------------------------------ the notation

    /// <summary>
    /// `asset` is a type a sheet may declare, with or without a kind.
    /// </summary>
    [Fact]
    public void Asset_is_a_declarable_type()
    {
        var context = Context();

        Assert.True(context.IsValidTypeName("asset"));
        Assert.True(context.IsValidTypeName("asset(icon)"));
        Assert.True(context.IsValidTypeName("asset(icon)[]"));
        Assert.True(context.IsValidTypeName("asset(icon)[]?"));
    }

    /// <summary>
    /// The role comes off the name, so what follows resolves an ordinary `string`.
    /// </summary>
    [Fact]
    public void The_role_leaves_the_type_a_string()
    {
        var context = Context();

        Assert.Equal("string", context.SplitStringRole(
            "asset(icon)", Somewhere(), out var role, out string kind, out _));

        Assert.Equal(StringRole.Asset, role);
        Assert.Equal("icon", kind);

        Assert.Equal(ValueType.String, context.ParseValueType("asset", Somewhere()));
        Assert.Equal(ValueType.StringArray, context.ParseValueType("asset[]", Somewhere()));
    }

    /// <summary>
    /// A namespace is a `text` thing, so `asset` refuses a second name.
    /// </summary>
    /// <remarks>
    /// Where an asset is looked for comes from the recipe, keyed by the kind - there is
    /// nothing for a second name to mean, and accepting one silently would be accepting a
    /// sheet that says something this tool does not do.
    /// </remarks>
    [Fact]
    public void Asset_takes_no_second_name()
    {
        var failure = Assert.Throws<TabbitException>(
            () => Context().SplitStringRole("asset(icon,ui)", Somewhere(), out _, out _, out _));

        Assert.Equal(Tabbit.Cooking.CookingMessages.RoleSpaceNotText, failure.MessageId);

        // The wording and the id, together, only while the move is in progress. Once every
        // report is named the wording assertions go and this one stays - that is what stops
        // a message being frozen by the tests that read it. spec/validation/message-ids.md §7.
        Assert.Equal(Tabbit.Cooking.CookingMessages.RoleSpaceNotText, failure.MessageId);
    }

    /// <summary>
    /// Brackets opened and left empty are a typo, and the message says `kind` rather than
    /// `group` because that is what this role puts there.
    /// </summary>
    [Fact]
    public void An_empty_kind_is_refused_in_the_roles_own_words()
    {
        var failure = Assert.Throws<TabbitException>(
            () => Context().SplitStringRole("asset()", Somewhere(), out _, out _, out _));

        Assert.Equal(Tabbit.Cooking.CookingMessages.RoleGroupEmpty, failure.MessageId);
        Assert.Contains("names no kind", failure.Message);
        Assert.Contains("asset(icon)", failure.Message);
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0;

        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static CookingContext Context()
        => new CookingContext(new Model(), new Tabbit.Recipe.RecipeModel(), new Diagnostics());

    private static Location Somewhere()
        => new Location { Filename = "test", Sheet = "Sheet1", Column = 0, Row = 0 };
}
