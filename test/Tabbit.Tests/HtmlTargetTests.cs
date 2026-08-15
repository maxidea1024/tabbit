using System;
using System.Collections.Generic;
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

                if (loads && lines[i].Contains("//", StringComparison.Ordinal))
                    offenders.Add($"  {Path.GetFileName(page)}:{i + 1}  {lines[i].Trim()}");
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
            var heading = Regex.Match(text, "<h3[^>]*>(?<body>.*?)</h3>", RegexOptions.Singleline);

            Assert.True(heading.Success, $"{Path.GetFileName(page)} has no heading.");

            string headingText = Regex.Replace(heading.Groups["body"].Value, "<[^>]+>", "").Trim();

            Assert.False(headingText.EndsWith(":", StringComparison.Ordinal),
                $"{Path.GetFileName(page)} heading names nothing: `{headingText}`");

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
    [Fact]
    public void The_index_says_so_when_a_model_declares_no_enum()
    {
        var conversion = TabbitRunner.Convert("reserved-words");

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string index = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("reserved-words"), "html", "index.html"));

        // The heading stays, because the navigation links to it by id.
        Assert.Contains("id=\"enums\"", index);
        Assert.Contains("None declared.", index);
    }
}
