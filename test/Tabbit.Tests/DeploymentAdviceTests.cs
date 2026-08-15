using System.Linq;
using Tabbit.History;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The ship verdict: given what changed, what has to go out.
///
/// Every case here is a deployment decision somebody makes during live operations, and
/// the costly mistakes are asymmetric. Calling a data-only change "data + code" wastes
/// a build; calling a constant change "data only" ships nothing at all while looking
/// shipped. The tests lean on the second kind.
/// </summary>
public class DeploymentAdviceTests
{
    private static SchemaChangeView Change(
        string entityKind, string entity, string member = null, string kind = "Modified",
        string before = null, string after = null, string renamedFrom = null)
    {
        return new SchemaChangeView
        {
            EntityKind = entityKind,
            Entity = entity,
            Member = member,
            Kind = kind,
            Before = before,
            After = after,
            RenamedFrom = renamedFrom,
        };
    }

    /// <summary>The enums some current column is typed with.</summary>
    private static System.Collections.Generic.ISet<string> InUse(params string[] enums)
        => new System.Collections.Generic.HashSet<string>(enums, System.StringComparer.Ordinal);

    // ------------------------------------------------------------ data only

    [Fact]
    public void Cell_edits_alone_are_a_data_patch()
    {
        var advice = DeploymentAdvice.Compute([], dataMoved: true, InUse());

        Assert.True(advice.Data);
        Assert.False(advice.Code);
    }

    [Fact]
    public void A_column_added_is_a_data_patch()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Field", "Item", "Color", "Added")], dataMoved: true, InUse());

        Assert.True(advice.Data);
        Assert.False(advice.Code);
    }

    [Fact]
    public void A_column_renamed_is_a_data_patch()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Field", "Item", "Cost", renamedFrom: "Price")], dataMoved: true, InUse());

        Assert.True(advice.Data);
        Assert.False(advice.Code);
    }

    // ------------------------------------------------------------ code only

    [Fact]
    public void A_constant_change_is_code_only_and_says_why()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Constant", "Limits", "MaxPartySize", before: "4", after: "5")],
            dataMoved: false, InUse());

        Assert.False(advice.Data);
        Assert.True(advice.Code);

        // The reason carries the trap: nothing about a constant reaches a data file.
        Assert.Contains(advice.Reasons, r => r.Contains("data patch carries none"));
    }

    [Fact]
    public void A_label_added_without_data_is_code_only()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("EnumLabel", "Grade", "Mythic", "Added", after: "5")], dataMoved: false, InUse("Grade"));

        Assert.False(advice.Data);
        Assert.True(advice.Code);
        Assert.Empty(advice.Warnings);
    }

    // ------------------------------------------------------------------ both

    [Fact]
    public void A_type_change_needs_both()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Field", "Item", "Gold", before: "{\"type\":\"int\",\"side\":\"cs\"}",
                    after: "{\"type\":\"bigint\",\"side\":\"cs\"}")],
            dataMoved: false, InUse());

        Assert.True(advice.Data);
        Assert.True(advice.Code);
        Assert.Contains(advice.Reasons, r => r.Contains("int -> bigint"));
    }

    [Fact]
    public void A_side_change_needs_both()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Field", "Item", "Secret", before: "{\"type\":\"int\",\"side\":\"cs\"}",
                    after: "{\"type\":\"int\",\"side\":\"s\"}")],
            dataMoved: false, InUse());

        Assert.True(advice.Data);
        Assert.True(advice.Code);
    }

    [Fact]
    public void A_table_added_needs_both()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Table", "Quest", kind: "Added")], dataMoved: true, InUse());

        Assert.True(advice.Data);
        Assert.True(advice.Code);
    }

    [Fact]
    public void A_table_removed_warns_about_builds_that_still_ask_for_it()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Table", "Quest", kind: "Removed")], dataMoved: false, InUse());

        Assert.True(advice.Code);
        Assert.Contains(advice.Warnings, w => w.Contains("still ask for its file"));
    }

    // -------------------------------------------------------- the quiet ones

    [Fact]
    public void Renumbered_labels_demand_a_full_reexport()
    {
        // The one change nothing rejects: every shifted number is still a declared
        // value, so old data reads cleanly into the wrong labels.
        var advice = DeploymentAdvice.Compute(
            [Change("EnumLabel", "Grade", "Rare", before: "2", after: "3"),
             Change("EnumLabel", "Grade", "Epic", before: "3", after: "4")],
            dataMoved: false, InUse("Grade"));

        Assert.True(advice.Data);
        Assert.True(advice.Code);
        Assert.Contains(advice.Warnings, w => w.Contains("re-export"));
    }

    [Fact]
    public void A_removed_label_warns_about_rollback()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("EnumLabel", "Grade", "Beta", "Removed", before: "9")], dataMoved: false, InUse("Grade"));

        Assert.True(advice.Code);
        Assert.Contains(advice.Warnings, w => w.Contains("Rolling data back"));
    }

    [Fact]
    public void A_label_added_alongside_data_warns_about_ordering()
    {
        // Scenario one of the deployment guide: the value can reach builds that have
        // no name for it, and nothing fails on the way.
        var advice = DeploymentAdvice.Compute(
            [Change("EnumLabel", "Grade", "Mythic", "Added", after: "5")], dataMoved: true, InUse("Grade"));

        Assert.True(advice.Data);
        Assert.True(advice.Code);
        Assert.Contains(advice.Warnings, w => w.Contains("deploy this code before"));
    }

    // ------------------------------------------------------- unused enums

    [Fact]
    public void Renumbering_an_unused_enum_is_a_code_edit_and_nothing_more()
    {
        // No column is typed with it, so no exported row holds its values. The
        // re-export alarm would be crying wolf.
        var advice = DeploymentAdvice.Compute(
            [Change("EnumLabel", "Draft", "Rare", before: "2", after: "3")],
            dataMoved: false, InUse("Grade"));

        Assert.False(advice.Data);
        Assert.True(advice.Code);
        Assert.Empty(advice.Warnings);
        Assert.Contains(advice.Reasons, r => r.Contains("no column uses it"));
    }

    [Fact]
    public void An_unused_enums_label_added_next_to_data_raises_no_ordering_warning()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("EnumLabel", "Draft", "Mythic", "Added", after: "5")],
            dataMoved: true, InUse("Grade"));

        Assert.Empty(advice.Warnings);
    }

    // -------------------------------------------------------------- neither

    [Fact]
    public void Nothing_changed_is_no_verdict()
    {
        Assert.Null(DeploymentAdvice.Compute([], dataMoved: false, InUse()));
    }

    [Fact]
    public void A_comment_only_column_change_ships_nothing()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Field", "Item", "Gold",
                    before: "{\"type\":\"int\",\"side\":\"cs\",\"comment\":\"old\"}",
                    after: "{\"type\":\"int\",\"side\":\"cs\",\"comment\":\"new\"}")],
            dataMoved: false, InUse());

        Assert.False(advice.Data);
        Assert.False(advice.Code);
    }

    [Fact]
    public void A_cosmetic_column_change_does_not_erase_another_columns_verdict()
    {
        var advice = DeploymentAdvice.Compute(
            [Change("Field", "Item", "Color", "Added"),
             Change("Field", "Item", "Gold",
                    before: "{\"type\":\"int\",\"side\":\"cs\",\"comment\":\"old\"}",
                    after: "{\"type\":\"int\",\"side\":\"cs\",\"comment\":\"new\"}")],
            dataMoved: false, InUse());

        Assert.True(advice.Data);
    }

    // ---------------------------------------------------------------- merging

    [Fact]
    public void A_range_needs_whatever_any_snapshot_in_it_needs()
    {
        var dataOnly = DeploymentAdvice.Compute([], dataMoved: true, InUse());

        var codeOnly = DeploymentAdvice.Compute(
            [Change("Constant", "Limits", "MaxPartySize", before: "4", after: "5")],
            dataMoved: false, InUse());

        var merged = DeploymentAdvice.Merge([dataOnly, null, codeOnly]);

        Assert.True(merged.Data);
        Assert.True(merged.Code);
    }

    [Fact]
    public void Merging_nothing_is_no_verdict()
    {
        Assert.Null(DeploymentAdvice.Merge([null, null]));
    }

    [Fact]
    public void Merged_reasons_are_not_repeated()
    {
        var first = DeploymentAdvice.Compute(
            [Change("Constant", "Limits", "A", before: "1", after: "2")], dataMoved: false, InUse());

        var second = DeploymentAdvice.Compute(
            [Change("Constant", "Limits", "B", before: "3", after: "4")], dataMoved: false, InUse());

        var merged = DeploymentAdvice.Merge([first, second]);

        Assert.Single(merged.Reasons, r => r.Contains("Limits"));
    }

    // ------------------------------------------------------------------ caps

    [Fact]
    public void A_pile_of_reasons_is_folded_rather_than_listed()
    {
        var changes = Enumerable.Range(0, 40)
            .Select(i => Change("Field", "Item", "Col" + i, "Added"))
            .ToList();

        var advice = DeploymentAdvice.Compute(changes, dataMoved: true, InUse());

        Assert.True(advice.Reasons.Count <= 13);
        Assert.Contains(advice.Reasons, r => r.Contains("more"));
    }
}
