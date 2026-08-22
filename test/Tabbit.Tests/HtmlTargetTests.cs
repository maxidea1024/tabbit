using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The generated documentation, checked as a set of pages rather than as text.
///
/// The golden comparison already pins every byte of this output, and it could not see
/// any of what these tests check - because none of it was a change. Every enum link
/// pointed at `enums.html`, which this target has never written; the source-sheet list
/// carried `href=""`; and the enum pages showed no names at all for an Excel-sourced
/// model, because the caption was returned only when there was a url to wrap it in. All
/// three were recorded in the goldens as the correct answer.
///
/// A golden comparison answers "did the output change". These answer "does the output
/// work", which is a different question and needs asking separately.
/// </summary>
public class HtmlTargetTests
{
    private const string Scenario = "core";

    private static string HtmlDir => Path.Combine(RepoLayout.OutputDir(Scenario), "html");

    private static readonly Regex HrefPattern =
        new Regex("href\\s*=\\s*\"(?<href>[^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex IdPattern =
        new Regex("id\\s*=\\s*\"(?<id>[^\"]*)\"", RegexOptions.Compiled);

    /// <summary>A `data:` url and its payload, so a check can look at the rest of the line.</summary>
    private static readonly Regex DataUrl =
        new Regex("data:[^\"']*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A reported line, short enough to read. The favicon's payload is 1.2 KB on one line,
    /// and a failure message carrying it in full is a failure message nobody reads.
    /// </summary>
    private static string Trim(string line)
    {
        string collapsed = DataUrl.Replace(line.Trim(), "data:…");
        return collapsed.Length <= 200 ? collapsed : collapsed.Substring(0, 200) + "…";
    }

    private static IReadOnlyList<string> Pages()
    {
        var result = TabbitRunner.Convert(Scenario);

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        var pages = Directory.GetFiles(HtmlDir, "*.html", SearchOption.AllDirectories);

        Assert.NotEmpty(pages);

        return pages;
    }

    // ---------------------------------------------------------------- tests

    /// <summary>
    /// Every link inside the documentation reaches a page that exists, and an anchor on
    /// it that exists.
    /// </summary>
    [Fact]
    public void Every_internal_link_resolves()
    {
        var broken = new List<string>();

        foreach (var page in Pages())
        {
            string text = File.ReadAllText(page);
            string pageDir = Path.GetDirectoryName(page);

            foreach (Match match in HrefPattern.Matches(text))
            {
                string href = match.Groups["href"].Value;

                // Somewhere else entirely - a Google Sheets url. Not this test's business.
                if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // The favicon is carried in the page rather than named beside it, so there is
                // no file for it to resolve to - the bytes are the href. That is the point of
                // it: a page stays one file somebody can mail.
                if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (href.Length == 0)
                {
                    broken.Add($"  {Path.GetFileName(page)}: href=\"\" - a link back to the page it is on");
                    continue;
                }

                int hash = href.IndexOf('#');

                string relative = hash < 0 ? href : href.Substring(0, hash);
                string fragment = hash < 0 ? null : href.Substring(hash + 1);

                // A bare fragment points into the current page.
                string target = relative.Length == 0
                    ? page
                    : Path.Combine(pageDir, relative.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(target))
                {
                    broken.Add($"  {Path.GetFileName(page)}: `{href}` names no file");
                    continue;
                }

                if (fragment == null)
                    continue;

                var ids = IdPattern.Matches(File.ReadAllText(target))
                                   .Select(m => m.Groups["id"].Value)
                                   .ToHashSet(StringComparer.Ordinal);

                if (!ids.Contains(fragment))
                    broken.Add($"  {Path.GetFileName(page)}: `{href}` names no anchor in {relative}");
            }
        }

        Assert.True(broken.Count == 0,
            $"The generated documentation has links that go nowhere:{Environment.NewLine}" +
            string.Join(Environment.NewLine, broken.Distinct()));
    }

    /// <summary>
    /// The pages reach nothing over the network.
    ///
    /// The tool is expected to run on closed networks - which is the reason the history
    /// page states for reaching no CDN - and these pages reached a CDN that has since
    /// shut down, so they had been rendering unstyled for some time. A stylesheet nobody
    /// can load is worse than none, because the markup assumes it arrived.
    ///
    /// A Google Sheets link in a page's body is a different thing: it is content, and
    /// following it is the reader's choice. What is refused here is a page that cannot
    /// render itself without the network.
    /// </summary>
    [Fact]
    public void No_page_loads_anything_over_the_network()
    {
        var offenders = new List<string>();

        foreach (var page in Pages())
        {
            var lines = File.ReadAllLines(page);

            for (int i = 0; i < lines.Length; i++)
            {
                bool loads = lines[i].Contains("<link", StringComparison.OrdinalIgnoreCase)
                             || lines[i].Contains("<script", StringComparison.OrdinalIgnoreCase)
                             || lines[i].Contains("@import", StringComparison.OrdinalIgnoreCase)
                             || lines[i].Contains("<img", StringComparison.OrdinalIgnoreCase);

                if (!loads)
                    continue;

                // A `data:` url is the opposite of what this test is about - the bytes are in
                // the page. Its base64 also contains `//` wherever the payload happens to, so
                // leaving it in would make every page an offender for a reason that is not one.
                string line = DataUrl.Replace(lines[i], "data:");

                // `//` after a scheme, or as the whole of one. Not any `//` anywhere: that is
                // what read the favicon's payload as a url.
                if (line.Contains("://", StringComparison.Ordinal)
                    || line.Contains("=\"//", StringComparison.Ordinal)
                    || line.Contains("='//", StringComparison.Ordinal))
                {
                    offenders.Add($"  {Path.GetFileName(page)}:{i + 1}  {Trim(lines[i])}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"A page fetches something to render itself:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Nothing a page is meant to name comes out nameless.
    ///
    /// The enum pages did: the heading read `Enumeration:` and every label cell was
    /// empty, for any model whose source is a workbook on disk rather than Google
    /// Sheets - which is every fixture, and most projects.
    /// </summary>
    [Fact]
    public void Nothing_the_pages_name_is_left_blank()
    {
        Pages();

        foreach (var page in Directory.GetFiles(
                     Path.Combine(HtmlDir, "enums"), "*.html", SearchOption.TopDirectoryOnly))
        {
            string text = File.ReadAllText(page);

            // The heading names the enum, so it has to hold more than its own label.
            var heading = Regex.Match(text, "<h1[^>]*>(?<body>.*?)</h1>", RegexOptions.Singleline);

            Assert.True(heading.Success, $"{Path.GetFileName(page)} has no heading.");

            string headingText = Regex.Replace(heading.Groups["body"].Value, "<[^>]+>", "").Trim();

            Assert.False(headingText.Length == 0, $"{Path.GetFileName(page)} heading names nothing.");

            // And the line under it, which is where the label the source link carries is.
            var lead = Regex.Match(text, "<p class=\"lead\"[^>]*>(?<body>.*?)</p>", RegexOptions.Singleline);

            Assert.True(lead.Success, $"{Path.GetFileName(page)} has no summary line.");

            string leadText = Regex.Replace(lead.Groups["body"].Value, "<[^>]+>", "").Trim();

            Assert.DoesNotContain("Enumeration: &middot;", leadText);
            Assert.False(leadText.StartsWith("Enumeration: ·", StringComparison.Ordinal),
                $"{Path.GetFileName(page)} names no enum: `{leadText}`");

            // And every label row names its label.
            foreach (Match cell in Regex.Matches(
                         text, "<td id=\"const_(?<id>[^\"]*)\">(?<body>.*?)</td>"))
            {
                string body = Regex.Replace(cell.Groups["body"].Value, "<[^>]+>", "").Trim();

                Assert.False(body.Length == 0,
                    $"{Path.GetFileName(page)}: the row for `{cell.Groups["id"].Value}` shows no name.");
            }
        }
    }

    /// <summary>
    /// A section with nothing in it says so, rather than rendering an empty list.
    /// </summary>
    /// <remarks>
    /// The index has a column per kind of entity, and a model that declares no enum is
    /// ordinary - most do not. It rendered `Enumerations` over an empty `&lt;ul&gt;`, which
    /// reads as a page that failed to fill itself in rather than as an answer.
    ///
    /// `core` has an enum and a constant set, so every column there is full - which is why
    /// this scenario alone would prove nothing, and why `reserved-words` is here too.
    ///
    /// Three golden trees turned out to have been recording the empty `&lt;ul&gt;` all along:
    /// `excel-typed`, `foreign-field` and `layout-edge` all generate documentation for a model
    /// with no enum. Which is the usual shape of this - a golden comparison answers "did the
    /// output change" and had happily pinned the wrong answer, three times over.
    ///
    /// Stated as "no page contains an empty list" rather than naming the columns, so a column
    /// added later is covered without anyone remembering to add it.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    [InlineData("reserved-words")]
    public void No_page_shows_an_empty_list(string scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        string root = Path.Combine(RepoLayout.OutputDir(scenario), "html");

        Assert.True(Directory.Exists(root), $"`{scenario}` generated no documentation at {root}.");

        var pages = Directory.GetFiles(root, "*.html", SearchOption.AllDirectories);

        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            string text = File.ReadAllText(page).Replace("\r\n", "\n");

            Assert.DoesNotContain("<ul>\n</ul>", text);
            Assert.DoesNotContain("<ul></ul>", text);
        }
    }

    /// <summary>
    /// And the empty case really is reached, so the test above is not passing by never
    /// meeting one.
    /// </summary>
    /// <remarks>
    /// On the enumerations page rather than the overview: the lists moved off the overview
    /// when each kind got a page of its own, and the page still has to say that a model
    /// declaring no enum declares none rather than rendering an empty table.
    /// </remarks>
    [Fact]
    public void A_list_page_says_so_when_a_model_declares_nothing_of_its_kind()
    {
        // `excel-typed` declares no enum. `reserved-words` used to and has one now, which
        // is how this test came to be checking a message it could no longer meet.
        var conversion = TabbitRunner.Convert("excel-typed");

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string root = Path.Combine(RepoLayout.OutputDir("excel-typed"), "html");

        Assert.Contains("선언된 것이 없습니다.", File.ReadAllText(Path.Combine(root, "enums.html")));

        // And the overview is still the way to that page.
        Assert.Contains("enums.html", File.ReadAllText(Path.Combine(root, "index.html")));
    }

    /// <summary>
    /// No page carries any of the template language it was rendered from.
    /// </summary>
    /// <remarks>
    /// The footer template's own commentary was in every page of every project: a Scriban
    /// comment block ends at the first closing brace pair, one appeared inside the comment
    /// (in a backtick, which the parser does not care about), and the six lines after it
    /// were page text from then on. It rendered directly under each table, which is where
    /// the timestamp is, so what a reader saw was the timestamp preceded by garbage.
    ///
    /// The golden trees recorded it as correct - all six of them - because it had been
    /// that way since the templates were written, and a golden comparison answers "did
    /// the output change".
    /// </remarks>
    [Fact]
    public void No_page_carries_template_markup()
    {
        string[] residue = { "##~}}", "##}}", "{{~##", "{{~ ", "{{ include", "{{ if", "{{ end" };

        var offenders = new List<string>();

        foreach (var page in Pages())
        {
            var lines = File.ReadAllLines(page);

            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var marker in residue)
                {
                    if (lines[i].Contains(marker, StringComparison.Ordinal))
                        offenders.Add($"  {Path.GetFileName(page)}:{i + 1}  {Trim(lines[i])}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"A page carries the template it was rendered from:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Distinct()));
    }

    /// <summary>
    /// Every page is a document that ends.
    /// </summary>
    /// <remarks>
    /// The enum pages were not: their template did not include the shared footer, so an
    /// enum page stopped after its last row with nothing closed. Browsers repair that
    /// silently, which is why it survived - and a repaired document is not a promise about
    /// the next reader's browser.
    /// </remarks>
    [Fact]
    public void Every_page_is_a_complete_document()
    {
        var offenders = new List<string>();

        foreach (var page in Pages())
        {
            string text = File.ReadAllText(page).TrimEnd();

            if (!text.EndsWith("</html>", StringComparison.Ordinal))
                offenders.Add($"  {Path.GetFileName(page)} does not end with a closing html tag");

            if (!text.Contains("</body>", StringComparison.Ordinal))
                offenders.Add($"  {Path.GetFileName(page)} never closes its body");
        }

        Assert.True(offenders.Count == 0,
            $"A page is not a complete document:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Every page has a way out of itself.
    /// </summary>
    /// <remarks>
    /// The only route between pages used to be the index, and the only route back from one
    /// was the browser's history: no generated page linked to any other except the one the
    /// index pointed at. That is navigation nobody can describe to a colleague, and it is
    /// what a reader meets first.
    /// </remarks>
    [Fact]
    public void Every_page_can_reach_the_overview()
    {
        var offenders = new List<string>();

        foreach (var page in Pages())
        {
            string text = File.ReadAllText(page);

            bool reachesHome = HrefPattern.Matches(text)
                                          .Select(match => match.Groups["href"].Value)
                                          .Any(href => href.EndsWith("index.html", StringComparison.Ordinal)
                                                       || href.Contains("index.html#", StringComparison.Ordinal));

            if (!reachesHome)
                offenders.Add($"  {Path.GetFileName(page)} links to no overview");
        }

        Assert.True(offenders.Count == 0,
            $"A page cannot be navigated away from:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The counters on the overview are the pages they claim to count.
    /// </summary>
    /// <remarks>
    /// A statistic nobody checks is worse than no statistic: a reader who counts rows by
    /// eye and disagrees with the page has learned nothing about which of them is wrong.
    /// Checked against the other pages rather than against the model, because that is the
    /// pair a reader can compare - and a counter that has drifted from its own pages is
    /// the failure this guards.
    /// </remarks>
    [Fact]
    public void The_overview_counts_agree_with_the_pages()
    {
        Pages();

        // The label holds an icon beside its text, so the key is what the label says with
        // the markup taken out rather than everything up to the first tag.
        var cards = Regex.Matches(
                            File.ReadAllText(Path.Combine(HtmlDir, "index.html")),
                            "<div class=\"n\">(?<n>[0-9,]+)</div><div class=\"k\">(?<k>.*?)</div>")
                         .ToDictionary(
                             m => Regex.Replace(m.Groups["k"].Value, "<[^>]+>", "").Trim(),
                             m => Number(m.Groups["n"].Value));

        var tablePages = Directory.GetFiles(Path.Combine(HtmlDir, "tables"), "*.html");

        Assert.Equal(cards["테이블"], tablePages.Length);

        int rows = 0;
        int columns = 0;

        foreach (var page in tablePages)
        {
            string text = File.ReadAllText(page);

            rows += Stated(text, "행 (?<n>[0-9,]+)개");
            columns += Stated(text, "컬럼 (?<n>[0-9,]+)개");
        }

        Assert.Equal(cards["행"], rows);
        Assert.Equal(cards["컬럼"], columns);

        // And the column index lists every column of every table, once each.
        string fields = File.ReadAllText(Path.Combine(HtmlDir, "fields.html"));

        Assert.Equal(columns, Regex.Matches(fields, "<tr><td>").Count);

        static int Stated(string text, string pattern)
        {
            var match = Regex.Match(text, pattern);

            Assert.True(match.Success, $"A table page states no count for `{pattern}`.");

            return Number(match.Groups["n"].Value);
        }
    }

    /// <summary>
    /// A page showing part of a table says which part.
    /// </summary>
    /// <remarks>
    /// The row cap is the answer to a 37 MB page, and the danger of any cap is that the
    /// page looks complete. `html-row-cap` sets it to two rows, so the notice is reachable
    /// from a fixture that is deliberately small.
    /// </remarks>
    [Fact]
    public void A_page_showing_part_of_a_table_says_so()
    {
        var conversion = TabbitRunner.Convert("html-row-cap");

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string root = Path.Combine(RepoLayout.OutputDir("html-row-cap"), "html", "tables");

        var capped = new List<string>();

        foreach (var page in Directory.GetFiles(root, "*.html"))
        {
            string text = File.ReadAllText(page);

            int total = Number(Regex.Match(text, "행 (?<n>[0-9,]+)개").Groups["n"].Value);

            // The first tbody, which is the table's. A page also carries a copy of every
            // enum it mentions, and those are rows too - counting them said a two-row page
            // was showing five.
            int start = text.IndexOf("<tbody>", StringComparison.Ordinal);
            int stop = text.IndexOf("</tbody>", start, StringComparison.Ordinal);

            int shown = Regex.Matches(text.Substring(start, stop - start), "<tr>").Count;

            Assert.True(shown <= 2, $"{Path.GetFileName(page)} shows {shown} rows against a cap of 2.");

            if (total <= 2)
                continue;

            capped.Add(Path.GetFileName(page));

            Assert.Contains($"{Num(total)}행 중 처음 {Num(shown)}행", text);
        }

        // And a table long enough to cap really is in this fixture, so the assertion above
        // is not passing by never meeting one.
        Assert.NotEmpty(capped);
    }

    /// <summary>
    /// A cell offering an enum preview carries the enum it previews.
    /// </summary>
    /// <remarks>
    /// Reading an enum-valued cell used to mean leaving the row: the value was a link to
    /// the enum's page, and that page was the only place the labels were written down. The
    /// card that replaces the trip is built from a copy of the enum carried once per page,
    /// so a cell naming an enum the page does not carry shows nothing at all - and nothing
    /// at all is indistinguishable from a card that has not opened yet.
    /// </remarks>
    [Fact]
    public void An_enum_valued_cell_carries_the_enum_it_previews()
    {
        var offenders = new List<string>();
        int previews = 0;

        foreach (var page in Pages())
        {
            string text = File.ReadAllText(page);

            var carried = Regex.Matches(text, "data-enum-def=\"(?<name>[^\"]+)\"")
                               .Select(m => m.Groups["name"].Value)
                               .ToHashSet(StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(text, "data-enum=\"(?<name>[^\"]+)\""))
            {
                previews++;

                if (!carried.Contains(match.Groups["name"].Value))
                {
                    offenders.Add(
                        $"  {Path.GetFileName(page)} offers `{match.Groups["name"].Value}` and does not carry it");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"A cell offers a preview the page cannot build:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Distinct()));

        Assert.True(previews > 0, "No page offers an enum preview, so this test proves nothing.");
    }

    private static int Number(string formatted)
        => int.Parse(formatted, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>The same formatting the pages use, so a count can be looked for as text.</summary>
    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
