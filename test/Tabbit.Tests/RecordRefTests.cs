using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A reference that is a member of a record group.
/// </summary>
/// <remarks>
/// Refused until now, and the refusal threw rather than reported - so a workbook holding one
/// did not convert at all. What was missing was generated code: resolution made a stored key
/// and a setter per field, and neither reached `[j].Member`.
///
/// The fixture holds every shape a record group has, because each puts the element number
/// somewhere else and a generator that handles one handles neither of the others by accident:
///
///   Loadout  an array of records      the group is indexed   `slot[j].itemId`
///   Holder   one record               nothing is indexed     `main.itemId`
///   Bag      one record of arrays     the member is indexed  `slots.itemId[j]`
///   Mount    a reference two levels in, so the member is named by its whole path
///   Pose     a target keyed by a string, so nothing on the path may assume `int`
///   Kit      a trimmed array, whose length is the row's rather than the sheet's
///
/// `Loadout` also holds two references in one element, both at the same table. That is what
/// decided where the key lives: a name built from the group and the target would be one name
/// for both, and the second would land in the first one's.
///
/// spec/references/references-in-records.md.
/// </remarks>
public class RecordRefTests
{
    private const string Scenario = "record-ref";

    private static JsonElement Rows(string table)
        => JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(
            RepoLayout.OutputDir(Scenario), "json-named", table + ".json"))).RootElement;

    /// <summary>
    /// A workbook whose record members reference other tables converts.
    /// </summary>
    /// <remarks>
    /// The refusal this replaces was a `throw`, so the whole conversion stopped rather than
    /// one column being reported. That is why this is worth its own fact: it is the thing that
    /// used to be impossible.
    /// </remarks>
    [Fact]
    public void A_reference_may_be_a_member_of_a_record()
    {
        var result = TabbitRunner.Convert(Scenario);

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// The exported key is the target's, under the member's own name.
    /// </summary>
    /// <remarks>
    /// The format did not change for this - a record is stored one column per member, and a
    /// reference member is a column carrying its target's key like any other. So the JSON
    /// holds a number where the member is, and not an object.
    /// </remarks>
    [Fact]
    public void The_export_carries_the_key_under_the_members_own_name()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, result.Describe());

        var first = Rows("Loadout")[0].GetProperty("slot");

        Assert.Equal(1, first[0].GetProperty("itemId").GetInt32());
        Assert.Equal(2, first[1].GetProperty("itemId").GetInt32());

        // The second reference of the same element, which points at the other row.
        Assert.Equal(2, first[0].GetProperty("swapId").GetInt32());
        Assert.Equal(1, first[1].GetProperty("swapId").GetInt32());
    }

    /// <summary>
    /// The generated C# compiles, and reading the binary back resolves each element's own row.
    /// </summary>
    /// <remarks>
    /// A compile is not enough. The read writes into the element's own key and the linking
    /// pass walks the array and resolves each one; both were missing, and code that builds and
    /// leaves every element unresolved is exactly what the lifted refusal's comment warned
    /// about.
    ///
    /// Element 0 and element 1 point at different rows, so a loop that resolved the first and
    /// left the rest - or used the wrong index - shows as the wrong name rather than as a
    /// crash.
    /// </remarks>
    [Fact]
    public void Reading_the_binary_back_resolves_every_element()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        var result = CsToolchain.ReadBack(Scenario, "cs-check-record-ref");

        Assert.True(result.Succeeded,
            $"The generated C# does not build or does not read back.{Environment.NewLine}{result.Output}");

        var report = JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement;

        // An array of records: each element resolves to its own row, and a written zero stays
        // pointing at nothing beside a resolved one.
        var loadout = report.GetProperty("Loadout");
        Assert.Equal("sword", Resolved(loadout[0], "slots", 0));
        Assert.Equal("shield", Resolved(loadout[0], "slots", 1));
        Assert.Equal("shield", Resolved(loadout[1], "slots", 0));
        Assert.Equal("<unresolved>", Resolved(loadout[1], "slots", 1));

        // Two references in one element, at the same table, pointing at different rows.
        Assert.Equal("shield", loadout[0].GetProperty("slots")[0].GetProperty("swap").GetString());
        Assert.Equal("sword", loadout[0].GetProperty("slots")[1].GetProperty("swap").GetString());

        // A record of one: no element number anywhere.
        Assert.Equal("shield", report.GetProperty("Holder")[0].GetProperty("resolved").GetString());
        Assert.Equal("<unresolved>", report.GetProperty("Holder")[1].GetProperty("resolved").GetString());

        // A record of arrays: the number is on the member.
        var bag = report.GetProperty("Bag");
        Assert.Equal("sword", Resolved(bag[0], "slots", 0));
        Assert.Equal("shield", Resolved(bag[0], "slots", 1));

        // Two levels in.
        var mount = report.GetProperty("Mount");
        Assert.Equal("sword", Resolved(mount[0], "rigs", 0));
        Assert.Equal("shield", Resolved(mount[0], "rigs", 1));

        // A key that is not a number, and the empty one that points at nothing.
        var pose = report.GetProperty("Pose");
        Assert.Equal("Idle_01", Resolved(pose[0], "steps", 0));
        Assert.Equal("<unresolved>", Resolved(pose[1], "steps", 1));
    }

    /// <summary>
    /// A trimmed record array resolves the elements the row has, and no others.
    /// </summary>
    /// <remarks>
    /// The case the design decision was made for. A trimming group allocates its elements per
    /// row, so a key kept beside the group would have to be allocated with them and at the
    /// same length; inside the element that is free. A linking loop taking the sheet's column
    /// count rather than the row's walks past the end here, and the three rows are three, two
    /// and none so that it does.
    /// </remarks>
    [Fact]
    public void A_trimmed_record_array_resolves_the_elements_the_row_has()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        var result = CsToolchain.ReadBack(Scenario, "cs-check-record-ref");
        Assert.True(result.Succeeded, result.Output);

        var kit = JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement.GetProperty("Kit");

        Assert.Equal(3, kit[0].GetProperty("length").GetInt32());
        Assert.Equal(2, kit[1].GetProperty("length").GetInt32());
        Assert.Equal(0, kit[2].GetProperty("length").GetInt32());

        Assert.Equal("sword", Resolved(kit[0], "parts", 0));
        Assert.Equal("shield", Resolved(kit[0], "parts", 1));
        Assert.Equal("sword", Resolved(kit[0], "parts", 2));
        Assert.Equal("shield", Resolved(kit[1], "parts", 0));
    }

    /// <summary>
    /// The three read paths agree, and the values are the sheet's.
    /// </summary>
    /// <remarks>
    /// TypeScript is the only language that reads JSON as well as binary, so it is the only
    /// one that can be asked this. The compact JSON is the route most likely to be wrong: it
    /// is positional over the wire columns, and a reference member is one - reading it as the
    /// row rather than as the key would put an entry meant for the key where the linking pass
    /// writes.
    /// </remarks>
    [Fact]
    public void The_three_read_paths_agree()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to run the TypeScript round trip. {why}");

        var result = TypescriptRoundTrip.Run(Scenario, driver: "ts-check-record-ref");

        Assert.True(result.Succeeded,
            $"The read paths disagree.{Environment.NewLine}{result.Output}");

        var report = JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement;
        Assert.Equal(0, report.GetProperty("mismatches").GetArrayLength());
    }

    /// <summary>
    /// And what TypeScript ends up holding is the sheet's, so the three routes agreeing is
    /// evidence rather than three copies of one mistake.
    /// </summary>
    [Fact]
    public void Each_element_holds_the_row_the_sheet_named()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded, conversion.Describe());

        Assert.True(TypescriptToolchain.IsAvailable(out string why), why);

        var result = TypescriptRoundTrip.Run(Scenario, driver: "ts-check-record-ref");
        Assert.True(result.Succeeded, result.Output);

        var values = JsonObjects(result.StdOut).First(o => o.TryGetProperty("loadout", out _));

        Assert.Equal(
            new[] { "sword+shield/shield+sword", "shield+sword/<unresolved>+<unresolved>" },
            values.GetProperty("loadout").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(
            new[] { "shield", "<unresolved>" },
            values.GetProperty("holder").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(
            new[] { "sword/shield", "shield/<unresolved>" },
            values.GetProperty("bag").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(
            new[] { "sword/shield", "shield/<unresolved>" },
            values.GetProperty("mount").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(
            new[] { "Idle_01/Run_01", "Run_01/<unresolved>" },
            values.GetProperty("pose").EnumerateArray().Select(x => x.GetString()).ToArray());

        // Three, two and none - the trimmed lengths, seen from the other read path.
        Assert.Equal(
            new[] { "sword/shield/sword", "shield/sword", "" },
            values.GetProperty("kit").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    private static string Resolved(JsonElement row, string list, int at)
        => row.GetProperty(list)[at].GetProperty("resolved").GetString();

    /// <summary>The last line of stdout that parses as a JSON object.</summary>
    private static string LastJsonLine(string stdout)
        => stdout.Split('\n')
                 .Select(line => line.Trim())
                 .Last(line => line.StartsWith("{", StringComparison.Ordinal));

    private static IEnumerable<JsonElement> JsonObjects(string stdout)
    {
        foreach (var line in stdout.Split('\n').Select(l => l.Trim()))
        {
            if (!line.StartsWith("{", StringComparison.Ordinal))
                continue;

            yield return JsonDocument.Parse(line).RootElement;
        }
    }
}
