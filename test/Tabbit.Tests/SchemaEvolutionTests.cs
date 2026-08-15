using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A schema at two points in its history, read across the pair.
///
/// The conformance corpus answers "does every reader read what the writer wrote", with
/// both sides generated from one schema. That is the easy case, and it is not the one
/// that breaks a live service: there, the data was written by the schema that shipped
/// last week and the code was generated from the one that shipped today. Nothing in a
/// single-schema corpus can tell you what happens then.
///
/// So there are two fixtures, `evolution-v1` and `evolution-v2`, differing only in the
/// ways a schema changes. Every test below builds one generation's code and points it
/// at the other generation's data. What the format promises is that each kind of change
/// has exactly one outcome, and these are those outcomes:
///
///   added since        skipped, by the column's declared length
///   removed since      the member keeps its default
///   renamed            read anyway, because the tag identifies the column
///   reordered          read anyway, for the same reason
///   widened losslessly promoted, values intact
///   narrowed           refused, naming the field
///   changed outright   refused, naming the field
/// </summary>
public class SchemaEvolutionTests
{
    private const string V1 = "evolution-v1";
    private const string V2 = "evolution-v2";

    // ------------------------------------------------------------ add, delete, rename

    /// <summary>
    /// v1's code against v2's data: `Renamed` is v1's `Label` under the same tag, the
    /// reordering is invisible, `Added` is skipped, and `Doomed` - which v2 deleted -
    /// keeps its default.
    /// </summary>
    [Fact]
    public void Old_code_reads_new_data_across_added_deleted_and_renamed_columns()
    {
        var rows = Rows(code: V1, data: V2, table: "Evolution");

        Assert.Equal(2, rows.Count);

        Assert.Equal(1, rows[0].GetProperty("Index").GetInt32());
        Assert.Equal("first", rows[0].GetProperty("Label").GetString());
        Assert.Equal(10, rows[0].GetProperty("Amount").GetInt32());

        // v2 does not write this column at all, and its tag is tombstoned so nothing
        // else can take it. An empty string is the only honest answer, and the read does
        // not fail for wanting one - nor does it hand back a null for a consumer to
        // dereference.
        Assert.Equal("", rows[0].GetProperty("Doomed").GetString());
        Assert.Equal("", rows[1].GetProperty("Doomed").GetString());

        Assert.Equal("second", rows[1].GetProperty("Label").GetString());
        Assert.Equal(-20, rows[1].GetProperty("Amount").GetInt32());

        // `Added` is v2's, and v1's record has no member for it. What proves it was
        // skipped rather than misread is that everything else came back right: its
        // block sits between two the old code does read.
        Assert.False(rows[0].TryGetProperty("Added", out _));
    }

    /// <summary>
    /// v2's code against v1's data, which is the direction a rollout actually takes:
    /// new code, files written by the build before it.
    /// </summary>
    [Fact]
    public void New_code_reads_old_data_across_added_deleted_and_renamed_columns()
    {
        var rows = Rows(code: V2, data: V1, table: "Evolution");

        Assert.Equal(2, rows.Count);

        // Tag 2 was `Label` when the file was written and is `Renamed` in this build.
        // The column is the tag, not the name.
        Assert.Equal("first", rows[0].GetProperty("Renamed").GetString());
        Assert.Equal(10, rows[0].GetProperty("Amount").GetInt32());

        // v1 knew nothing about `Added`, so the file has no column for it and the
        // member keeps the zero it was declared with.
        Assert.Equal(0, rows[0].GetProperty("Added").GetInt32());
        Assert.Equal(0, rows[1].GetProperty("Added").GetInt32());

        Assert.Equal("second", rows[1].GetProperty("Renamed").GetString());
    }

    // ---------------------------------------------------------------- promotions

    /// <summary>
    /// int to bigint and float to double, read by the widened members.
    ///
    /// Both widenings are exact for every value the narrower type holds, which is the
    /// only reason they are allowed: a promotion that could round is a promotion that
    /// silently changes data, and the point of the check is to not do that.
    /// </summary>
    [Fact]
    public void New_code_promotes_the_widened_columns_of_old_data()
    {
        var rows = Rows(code: V2, data: V1, table: "Promoted");

        Assert.Equal(2, rows.Count);

        // Written as an i32, read into an int64 member.
        Assert.Equal(1024L, rows[0].GetProperty("Amount").GetInt64());
        Assert.Equal(-1024L, rows[1].GetProperty("Amount").GetInt64());

        // Written as an f32, read into a double member. Exact, so no tolerance.
        Assert.Equal(1.5, rows[0].GetProperty("Ratio").GetDouble());
        Assert.Equal(-0.25, rows[1].GetProperty("Ratio").GetDouble());
    }

    /// <summary>
    /// The same pair the other way round, which is narrowing, and is refused.
    ///
    /// An int64 written to a file and read into an int32 member is the case where being
    /// permissive costs a value: 2^40 truncated is a number nobody wrote. The read stops
    /// instead, naming the field, so a rollback that would corrupt data fails to start.
    /// </summary>
    [Fact]
    public void Old_code_refuses_the_widened_columns_of_new_data()
    {
        string error = Error(code: V1, data: V2, table: "Promoted");

        Assert.Contains("Promoted.Amount", error);
        Assert.Contains("regenerate the code or rebuild the data", error);
    }

    // ----------------------------------------------------------------- refusals

    /// <summary>
    /// string to int, which is no conversion in either direction.
    /// </summary>
    [Fact]
    public void A_type_change_that_is_not_a_promotion_is_refused_both_ways()
    {
        string forward = Error(code: V1, data: V2, table: "Refused");
        Assert.Contains("Refused.Code", forward);

        string backward = Error(code: V2, data: V1, table: "Refused");
        Assert.Contains("Refused.Code", backward);
    }

    /// <summary>
    /// Every generation reads its own data, which is the check that keeps the rest
    /// meaningful: a corpus where nothing reads is a corpus where nothing is refused
    /// for the right reason either.
    /// </summary>
    [Theory]
    [InlineData(V1)]
    [InlineData(V2)]
    public void Each_generation_reads_its_own_data(string generation)
    {
        foreach (string table in new[] { "Evolution", "Promoted", "Refused" })
        {
            var rows = Rows(code: generation, data: generation, table);
            Assert.Equal(2, rows.Count);
        }
    }

    // ------------------------------------------------------------------ refresh

    /// <summary>
    /// Reading a loaded table again, which is what a data patch or a hot reload does.
    ///
    /// The table used to be cleared and then filled, so a second read passed through a
    /// state where it held some of the new rows and none of the old ones - and a consumer
    /// iterating at that moment saw it. Now a read builds its own storage and publishes it
    /// at the end, so the rows are one whole load or the other.
    /// </summary>
    [Fact]
    public void A_table_can_be_read_again_over_itself()
    {
        var report = Refresh(code: V1, first: V1, second: V1, table: "Evolution");

        Assert.False(report.TryGetProperty("error", out _),
            "Reading the same data twice was refused.");

        // Not doubled, and not empty. The index map is rebuilt too - adding the same keys
        // to the map that was already there is what used to throw.
        var second = Rows(report, "second");

        Assert.Equal(2, second.Count);
        Assert.Equal("first", second[0].GetProperty("Label").GetString());
    }

    /// <summary>
    /// A refresh that is refused leaves the rows that were already there.
    ///
    /// This is the case the atomicity is for. A service reads a patched file, the file has
    /// a column this build cannot read, and the answer has to be the data it already had
    /// plus a reason - not an empty table, and not half a table.
    /// </summary>
    [Fact]
    public void A_refused_refresh_leaves_the_previous_rows_in_place()
    {
        // v2 widened `Amount` to a 64-bit integer, which v1's code refuses rather than
        // truncating - so the second read fails partway through the columns.
        var report = Refresh(code: V1, first: V1, second: V2, table: "Promoted");

        Assert.True(report.TryGetProperty("error", out var error),
            "The refresh was expected to be refused, and was not.");

        Assert.Contains("Promoted.Amount", error.GetString());

        var before = Rows(report, "first");
        var after = Rows(report, "after");

        Assert.Equal(2, after.Count);
        Assert.Equal(before[0].GetProperty("Amount").GetInt32(), after[0].GetProperty("Amount").GetInt32());
        Assert.Equal(before[1].GetProperty("Ratio").GetDouble(), after[1].GetProperty("Ratio").GetDouble());
    }

    /// <summary>
    /// And a refresh that goes through replaces every row.
    /// </summary>
    [Fact]
    public void An_accepted_refresh_replaces_the_rows()
    {
        var report = Refresh(code: V2, first: V2, second: V1, table: "Promoted");

        Assert.False(report.TryGetProperty("error", out _),
            "v2's code was expected to read v1's data by promotion.");

        var second = Rows(report, "second");

        Assert.Equal(2, second.Count);
        Assert.Equal(1024L, second[0].GetProperty("Amount").GetInt64());
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// Builds one generation's generated code and runs it against the other's data.
    /// </summary>
    private static JsonElement Run(string code, string data, string table)
    {
        foreach (string scenario in new[] { code, data })
        {
            var conversion = TabbitRunner.Convert(scenario);
            Assert.True(conversion.Succeeded,
                $"Converting `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
        }

        var result = EvolutionHarness.RunCsharp(code, Path.Combine(RepoLayout.OutputDir(data), "binary"), table);

        Assert.True(result.Succeeded,
            $"The harness built from `{code}` failed on `{data}`'s {table}." +
            $"{Environment.NewLine}{result.Output}");

        return JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement.Clone();
    }

    /// <summary>
    /// Builds one generation's code, loads one directory's data, then loads another's over
    /// the top. The report says what the table held before and after.
    /// </summary>
    private static JsonElement Refresh(string code, string first, string second, string table)
    {
        foreach (string scenario in new[] { code, first, second })
        {
            var conversion = TabbitRunner.Convert(scenario);
            Assert.True(conversion.Succeeded,
                $"Converting `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
        }

        var result = EvolutionHarness.RefreshCsharp(
            code,
            Path.Combine(RepoLayout.OutputDir(first), "binary"),
            Path.Combine(RepoLayout.OutputDir(second), "binary"),
            table);

        Assert.True(result.Succeeded,
            $"The harness built from `{code}` failed refreshing {table} from `{first}` to " +
            $"`{second}`.{Environment.NewLine}{result.Output}");

        return JsonDocument.Parse(LastJsonLine(result.StdOut)).RootElement.Clone();
    }

    /// <summary>One named array of rows out of a refresh report.</summary>
    private static List<JsonElement> Rows(JsonElement report, string name)
    {
        var rows = new List<JsonElement>();

        foreach (var row in report.GetProperty(name).EnumerateArray())
            rows.Add(row.Clone());

        return rows;
    }

    private static List<JsonElement> Rows(string code, string data, string table)
    {
        var report = Run(code, data, table);

        Assert.False(report.TryGetProperty("error", out var error),
            $"Reading `{data}`'s {table} with `{code}`'s code was refused: " +
            (error.ValueKind == JsonValueKind.String ? error.GetString() : "<no message>"));

        var rows = new List<JsonElement>();

        foreach (var row in report.GetProperty("rows").EnumerateArray())
            rows.Add(row.Clone());

        return rows;
    }

    private static string Error(string code, string data, string table)
    {
        var report = Run(code, data, table);

        Assert.True(report.TryGetProperty("error", out var error),
            $"Reading `{data}`'s {table} with `{code}`'s code was expected to be refused, " +
            "and was not.");

        return error.GetString();
    }

    /// <summary>
    /// The harness prints one JSON object, but a build can print ahead of it.
    /// </summary>
    private static string LastJsonLine(string output)
    {
        string found = null;

        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
                found = trimmed;
        }

        Assert.NotNull(found);
        return found;
    }
}
