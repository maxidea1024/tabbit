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

    /// <summary>
    /// Every scenario whose sheets use a type or a shape the pages have to say something
    /// particular about.
    /// </summary>
    /// <remarks>
    /// The checks below ran on `core` alone, which declares no composite, no container, no
    /// struct, no bit pattern and no key of several columns - so a page could report any of
    /// them wrongly, or not at all, and nothing here would notice. Every defect these
    /// scenarios were added for had been in the goldens from the day the page was written,
    /// recorded as the right answer, because a golden answers "did this change".
    /// </remarks>
    public static TheoryData<string> FeatureScenarios => new TheoryData<string>
    {
        "composite",
        "polymorphism",
        "bitset",
        "composite-key",
        "nullable-elements",
        "containers",
        "packed",
    };

    private static IReadOnlyList<string> PagesOf(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{result.Describe()}");

        string root = Path.Combine(RepoLayout.OutputDir(scenario), "html");

        Assert.True(Directory.Exists(root), $"`{scenario}` generated no documentation at {root}.");

        var pages = Directory.GetFiles(root, "*.html", SearchOption.AllDirectories);

        Assert.NotEmpty(pages);

        return pages;
    }

    /// <summary>
    /// No page uses one `id` twice.
    /// </summary>
    /// <remarks>
    /// A duplicate `id` is not a rendering fault a reader sees; it is a link that lands on the
    /// wrong row and gives no sign of having done so. The pages promise that a reference names
    /// a row, and the row anchors were built from the first column of the table whether the
    /// rows were addressed by it or not - so a table keyed by `X`, `Y` and `Z` held
    /// `row_Grid.0` three times over and `#row_Grid.0` reached whichever came first.
    ///
    /// <see cref="Every_internal_link_resolves"/> could not see it: it collects a page's ids
    /// into a set and asks whether a fragment is in it, and a set of three identical ids is a
    /// set of one.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    [MemberData(nameof(FeatureScenarios))]
    public void No_page_uses_one_id_twice(string scenario)
    {
        var offenders = new List<string>();

        foreach (var page in PagesOf(scenario))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in IdPattern.Matches(File.ReadAllText(page)))
            {
                string id = match.Groups["id"].Value;

                if (id.Length > 0 && !seen.Add(id))
                    offenders.Add($"  {Path.GetFileName(page)}: `{id}`");
            }
        }

        Assert.True(offenders.Count == 0,
            $"`{scenario}` has pages that use one id several times:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Distinct()));
    }

    /// <summary>
    /// No page shows the name of a runtime type where a value belongs.
    /// </summary>
    /// <remarks>
    /// `object.ToString()` on an array is the name of its CLR type, and the constant set page
    /// reached it for every constant that was not a scalar enum - so a `string[]` constant read
    /// `System.String[]` and a `Grade[]` read `System.Int32[]`. Four of them were in the
    /// `core` golden from the first commit of that page.
    ///
    /// A blanket check rather than one about arrays: any `System.` in a rendered value is this
    /// same mistake, and the next one will be a type nobody predicted here.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    [MemberData(nameof(FeatureScenarios))]
    public void No_page_shows_a_runtime_type_name(string scenario)
    {
        var offenders = new List<string>();

        foreach (var page in PagesOf(scenario))
        {
            foreach (string line in File.ReadAllLines(page))
            {
                if (line.Contains("System.", StringComparison.Ordinal))
                    offenders.Add($"  {Path.GetFileName(page)}: {Trim(line)}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"`{scenario}` renders a runtime type name where a value belongs:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Distinct()));
    }

    /// <summary>
    /// No column heading names one part of the group it stands over.
    /// </summary>
    /// <remarks>
    /// A group's columns are written `Pos.X`, `Bag.Tags`, `statBonus[0]["Id"]` - the sheet has
    /// one column per part and nowhere else to say which - and the page draws the whole group
    /// as one column. The bracket was being trimmed from the heading and the dot was not, so
    /// every group a sheet wrote in dot notation was headed by its first member: a `vec3f`
    /// column read `Pos.X` over a cell holding all three components.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    [MemberData(nameof(FeatureScenarios))]
    public void No_column_heading_names_a_part_of_its_group(string scenario)
    {
        var offenders = new List<string>();

        foreach (var page in PagesOf(scenario))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(page), "<th[^>]*>(?<caption>[^<]*)</th>"))
            {
                string caption = match.Groups["caption"].Value.Trim();

                // A description cell is prose from the sheet and may hold anything.
                if (caption.Length == 0 || caption.Contains(' '))
                    continue;

                if (caption.Contains('.') || caption.Contains('['))
                    offenders.Add($"  {Path.GetFileName(page)}: `{caption}`");
            }
        }

        Assert.True(offenders.Count == 0,
            $"`{scenario}` heads a column with the name of one part of it:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Distinct()));
    }

    /// <summary>
    /// The row anchors of a page are built from the columns the sheet says address the rows.
    /// </summary>
    /// <remarks>
    /// Checked against the conversion's own answer rather than against a list written here, so
    /// that the assertion is about the two agreeing. A page whose anchors come from somewhere
    /// else is a page whose links are decoration.
    /// </remarks>
    [Theory]
    [InlineData("composite-key")]
    public void Row_anchors_carry_every_column_of_the_key(string scenario)
    {
        var pages = PagesOf(scenario)
                    .Where(page => Path.GetFileName(Path.GetDirectoryName(page)) == "tables")
                    .ToList();

        Assert.NotEmpty(pages);

        int composite = 0;

        foreach (var page in pages)
        {
            string text = File.ReadAllText(page);

            var anchors = Regex.Matches(text, "id=\"row_(?<name>[^.\"]+)\\.(?<key>[^\"]*)\"")
                               .Select(m => m.Groups["key"].Value)
                               .ToList();

            Assert.NotEmpty(anchors);

            // Every anchor of one page has the same number of parts, because every row of one
            // table is addressed the same way. A page mixing widths means the anchor is being
            // built from something other than the key.
            var widths = anchors.Select(key => key.Split('|').Length).Distinct().ToList();

            Assert.True(widths.Count == 1,
                $"{Path.GetFileName(page)} has anchors of {widths.Count} different widths.");

            if (widths[0] > 1)
                composite++;
        }

        // And a table keyed by several columns really is in this fixture.
        Assert.True(composite > 0, $"`{scenario}` has no table keyed by several columns.");
    }

    /// <summary>
    /// Every variant a `$type` cell names is one the struct pages declare.
    /// </summary>
    /// <remarks>
    /// The cell used to print the number the file carries, which is the one thing about a
    /// variant that no reader of a sheet has ever typed. Now it prints the name and links to
    /// the declaration, and this is the check that the two are the same set - a name with no
    /// declaration behind it is the enum-preview fault in another place.
    /// </remarks>
    [Theory]
    [InlineData("polymorphism")]
    public void Every_variant_a_cell_names_is_declared(string scenario)
    {
        var pages = PagesOf(scenario);

        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var page in pages.Where(p => Path.GetFileName(Path.GetDirectoryName(p)) == "structs"))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(page), "id=\"variant_(?<id>[^\"]+)\""))
                declared.Add(match.Groups["id"].Value);
        }

        Assert.NotEmpty(declared);

        var offenders = new List<string>();
        int named = 0;

        foreach (var page in pages)
        {
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(page), "class=\"variant\" href=\"[^\"]*#variant_(?<id>[^\"]+)\""))
            {
                named++;

                if (!declared.Contains(match.Groups["id"].Value))
                    offenders.Add($"  {Path.GetFileName(page)}: `{match.Groups["id"].Value}`");
            }
        }

        Assert.True(offenders.Count == 0,
            $"A cell names a variant no page declares:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Distinct()));

        Assert.True(named > 0, "No cell names a variant, so this test proves nothing.");
    }

    /// <summary>
    /// The types a page reports are the ones the sheets wrote.
    /// </summary>
    /// <remarks>
    /// The one check here that is about content rather than about structure, and it is here
    /// because the absences it covers were each invisible: a `vec3f` column was reported as a
    /// record of three floats, a `set` as an array, a `map` as a record of two, and a `bitset`
    /// as the `bigint` it is folded to. Every one of those is a true statement about the
    /// encoding and the wrong answer to "what does this sheet say".
    /// </remarks>
    [Theory]
    [InlineData("composite", "vec3f", "vec2i", "quat", "color32", "color")]
    [InlineData("containers", "set<", "map<")]
    [InlineData("bitset", "bitset", "0x")]
    [InlineData("nullable-elements", "?[]")]
    [InlineData("polymorphism", "struct.Effect", "$type")]
    public void A_page_reports_the_types_the_sheets_declared(string scenario, params string[] wanted)
    {
        // What a reader sees, not what the file holds. A type spelling is assembled out of
        // several elements - the name, the angle brackets, the element's own type - so looking
        // for it in the markup is looking for the markup rather than for the answer.
        string all = string.Join("\n", PagesOf(scenario).Select(page => Readable(File.ReadAllText(page))));

        foreach (string spelling in wanted)
        {
            Assert.True(all.Contains(spelling, StringComparison.Ordinal),
                $"`{scenario}` never reports `{spelling}`, which its sheets declare.");
        }
    }

    /// <summary>
    /// The drawing the script hides really is hidden.
    /// </summary>
    /// <remarks>
    /// **`hidden` is an HTML content attribute and the browser's own stylesheet does not apply
    /// it to an SVG element.** The script sets it on the whole graph when a reader picks one
    /// table, and it did nothing: the whole drawing stayed on screen while the neighbourhood
    /// was drawn over it, from its own origin and at its own scale. What a reader saw was two
    /// pictures on top of each other with the table they had chosen apparently somewhere of
    /// its own.
    ///
    /// Nothing else here could see it. It is not a link, not a count, not a missing page - the
    /// markup was right and one declaration was missing, so the goldens recorded it as correct
    /// from the day the drawing was written. This asks for the declaration by name, which is
    /// the only part of it a file can be read for.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    [InlineData("polymorphism")]
    public void The_graph_can_hide_a_drawing(string scenario)
    {
        var pages = PagesOf(scenario)
                    .Where(page => Path.GetFileName(page) == "references.html")
                    .ToList();

        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            string text = File.ReadAllText(page);

            Assert.Contains("id=\"focus\"", text);

            Assert.True(
                text.Contains(".graph [hidden]", StringComparison.Ordinal),
                $"{Path.GetFileName(page)} sets `hidden` on an SVG group and carries no rule " +
                "that hides one, so both drawings render at once.");
        }
    }

    /// <summary>
    /// A description's emphasis is rendered rather than printed.
    /// </summary>
    /// <remarks>
    /// Descriptions come from sheets and people write emphasis in them. The pages printed the
    /// asterisks: a constant whose description ended `**두 구현이 같아야 하는 값들입니다.**`
    /// read with the four asterisks in it, in the one place its author was being emphatic.
    ///
    /// The other half of this is the check below, that no page carries markup a cell wrote.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    public void A_description_carries_the_emphasis_its_author_wrote(string scenario)
    {
        string all = string.Join("\n", PagesOf(scenario).Select(File.ReadAllText));

        // The fixture writes one, so the assertion cannot pass by never meeting one.
        Assert.Contains("<b>", all, StringComparison.Ordinal);

        // And what it renders is not what it printed. A `**` left in a description means the
        // pair was not read.
        foreach (var page in PagesOf(scenario))
        {
            string readable = Readable(File.ReadAllText(page));

            Assert.DoesNotContain("**", readable, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The frozen first column keeps its paint as well as its place.
    /// </summary>
    /// <remarks>
    /// A positioned element left at `z-index: auto` is painted in document order, so every
    /// cell to the right of the sticky one went over it: scrolling a wide table sideways drew
    /// the second column's values across the key and the two texts sat on top of each other.
    /// The cell held its position the whole time, which is why nothing about the layout looked
    /// wrong until it was scrolled.
    /// </remarks>
    [Theory]
    [InlineData("core")]
    public void The_frozen_column_is_painted_over_the_cells_it_passes(string scenario)
    {
        foreach (var page in PagesOf(scenario))
        {
            string text = File.ReadAllText(page);

            if (!text.Contains("tbody td:first-child { position: sticky", StringComparison.Ordinal)
                && !text.Contains("td:first-child { position: sticky", StringComparison.Ordinal))
            {
                Assert.Contains(".scroll tbody td:first-child {", text);
            }

            int at = text.IndexOf(".scroll tbody td:first-child {", StringComparison.Ordinal);

            Assert.True(at >= 0, $"{Path.GetFileName(page)} has no rule for the sticky cell.");

            string rule = text.Substring(at, Math.Min(220, text.Length - at));

            Assert.True(rule.Contains("z-index", StringComparison.Ordinal),
                $"{Path.GetFileName(page)} makes the first column sticky and gives it no " +
                "z-index, so the cells it passes are painted over it.");
        }
    }

    /// <summary>A page as its text, with the markup taken out and the entities resolved.</summary>
    private static string Readable(string page)
    {
        string body = Regex.Replace(page, "<(script|style)[\\s\\S]*?</\\1>", " ");

        return System.Net.WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]*>", ""));
    }

    private static int Number(string formatted)
        => int.Parse(formatted, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>The same formatting the pages use, so a count can be looked for as text.</summary>
    private static string Num(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
