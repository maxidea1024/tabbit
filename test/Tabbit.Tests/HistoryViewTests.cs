using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Tabbit.History;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The page `--history --format html` writes.
///
/// Two things have to hold. It must work with no network, because the tool is expected
/// to run where there is none - a page that fetches a stylesheet from a CDN is a page
/// that is blank on the machine that needed it most. And a cell value must not be able
/// to break out of the data block: the values are whatever a designer typed into a
/// spreadsheet, which is to say anything at all.
/// </summary>
public class HistoryViewTests
{
    private static DashboardDocument Dashboard(params CellChangeView[] cells)
        => new DashboardDocument
        {
            Project = "p",
            Branch = "main",
            Branches = new[] { "main" },
            Snapshots = Array.Empty<SnapshotListing>(),
            Rows = Array.Empty<TrendPoint>(),
            Churn = Array.Empty<TrendPoint>(),
            Authors = Array.Empty<AuthorSummary>(),
            Stats = new SummaryDocument
            {
                // The dashboard test does not read these; they are here because the
                // document says every summary has them, and a fixture that skipped them
                // would be a shape production never produces.
                Run = new SummaryRun
                {
                    GeneratedAt = "", ToolVersion = "", Recipe = "",
                    Commit = new SummaryCommit { Origin = "none" }, RequestedTargetSide = "cs",
                },
                Data = new SummaryData { Hash = "", Totals = new SummaryTotals() },
            },

            History = new HistoryDocument
            {
                Query = new HistoryQueryInfo { Project = "p", Branch = "main", GeneratedAt = "" },
                Totals = new HistoryTotals(),
                Snapshots = new[]
                {
                    new HistorySnapshotView
                    {
                        Commit = "abc", ShortCommit = "abc", Branch = "main", AuthorName = "Kim",
                        FollowsParent = true, Attributable = true,
                        Counts = new HistoryChangeCounts { Cells = cells.Length },
                        Schema = Array.Empty<SchemaChangeView>(),
                        Rows = Array.Empty<RowChangeView>(),
                        Cells = cells,
                    },
                },
            },
        };

    private static CellChangeView Cell(string before, string after) => new CellChangeView
    {
        Table = "Item", RowKey = "1", Field = "name", Kind = "Modified",
        Before = before, After = after,
    };

    [Fact]
    public void The_page_carries_its_own_stylesheet_and_script()
    {
        string html = HistoryView.SelfContained(Dashboard());

        Assert.Contains("<style>", html);
        Assert.Contains("--surface", html);
        Assert.Contains("<script>", html);
        Assert.Contains("function lineChart", html);
    }

    /// <summary>
    /// Nothing is fetched. Checked by pattern rather than by reading the assets,
    /// because the thing that would break this is a URL somebody adds later.
    /// </summary>
    [Fact]
    public void The_page_asks_the_network_for_nothing()
    {
        string html = HistoryView.SelfContained(Dashboard());

        foreach (Match match in Regex.Matches(html, @"(?:src|href)\s*=\s*""([^""]*)""",
                                              RegexOptions.IgnoreCase))
        {
            string target = match.Groups[1].Value;

            Assert.False(
                target.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("//", StringComparison.Ordinal),
                $"The page references `{target}`, which it cannot reach on a closed network.");
        }

        // The live page fetches; the self-contained one must not have inherited that.
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
    }

    /// <summary>
    /// An HTML parser ends a script element at the first `&lt;/script`, whatever the
    /// element's type says. A cell holding that text would cut the page in half and
    /// render the rest of the data as markup.
    /// </summary>
    [Fact]
    public void A_cell_holding_a_closing_script_tag_does_not_end_the_data()
    {
        string html = HistoryView.SelfContained(Dashboard(Cell("safe", "</script><h1>owned</h1>")));

        Assert.DoesNotContain("</script><h1>owned</h1>", html);
        Assert.Contains("<\\/script>", html);

        // Exactly two script elements: the data block and the app.
        Assert.Equal(2, Regex.Matches(html, "</script>").Count);
    }

    /// <summary>
    /// A comment opener would end the data block just as effectively in some parsers,
    /// and costs nothing to escape.
    /// </summary>
    [Fact]
    public void A_cell_holding_a_comment_opener_is_escaped_too()
    {
        string html = HistoryView.SelfContained(Dashboard(Cell("safe", "<!--x")));

        Assert.Contains("<\\!--x", html);
    }

    [Fact]
    public void The_data_survives_the_escaping_as_valid_json()
    {
        string html = HistoryView.SelfContained(Dashboard(Cell("a", "</script>")));

        var match = Regex.Match(html,
            "<script type=\"application/json\" id=\"data\">(.*?)</script>", RegexOptions.Singleline);

        Assert.True(match.Success, "The data block is not where the page expects it.");

        // `<\/` is a valid escape in a JSON string and reads back as `</`.
        using var document = System.Text.Json.JsonDocument.Parse(match.Groups[1].Value);

        string after = document.RootElement
            .GetProperty("history").GetProperty("snapshots")[0]
            .GetProperty("cells")[0].GetProperty("after").GetString();

        Assert.Equal("</script>", after);
    }

    [Fact]
    public void Non_ascii_values_are_carried_through()
    {
        string html = HistoryView.SelfContained(Dashboard(Cell("검", "한글 이름")));

        Assert.Contains("한글 이름", html);
    }

    /// <summary>
    /// The served page is the same shell with the assets by URL, so the two cannot
    /// drift into different layouts.
    /// </summary>
    [Fact]
    public void The_served_page_links_its_assets_instead_of_inlining_them()
    {
        string html = HistoryView.Live();

        Assert.Contains("<link rel=\"stylesheet\" href=\"history.css\">", html);
        Assert.Contains("<script src=\"history.js\"></script>", html);

        Assert.DoesNotContain("<style>", html);
        Assert.DoesNotContain("id=\"data\"", html);
    }

    [Fact]
    public void Assets_are_served_as_what_they_are()
    {
        Assert.Equal("text/css; charset=utf-8", HistoryView.ContentTypeOf("history.css"));
        Assert.Equal("text/javascript; charset=utf-8", HistoryView.ContentTypeOf("history.js"));
        Assert.Equal("text/html; charset=utf-8", HistoryView.ContentTypeOf("history.html"));
    }
}
