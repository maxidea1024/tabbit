using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That doc/troubleshooting.md quotes messages the tool actually prints.
///
/// A troubleshooting page is only useful if somebody can paste the message they got into a
/// search box and land on the right section. Which means the quoted text has to be the text -
/// and nothing about renaming a message would tell you the page had gone stale. It would just
/// stop matching, silently, for whoever needed it.
///
/// So the page's headings are checked against the source. Reword a message and this fails,
/// naming the heading that no longer describes anything.
/// </summary>
public class TroubleshootingDocTests
{
    private static string Page
        => File.ReadAllText(Path.Combine(RepoLayout.Root, "doc", "troubleshooting.md"));

    /// <summary>
    /// A heading that is entirely a code span is quoting a message. One that is prose - "갑자기
    /// 인증이 안 됨" - is describing a symptom and has nothing to check.
    /// </summary>
    private static readonly Regex QuotedHeading = new Regex(
        @"^### `(?<message>[^`]+)`(?: / `(?<second>[^`]+)`)?\s*$", RegexOptions.Multiline);

    /// <summary>
    /// The parts of a quoted message that are literal.
    /// </summary>
    /// <remarks>
    /// The page writes the message as a reader sees it, with real values where the source has
    /// interpolation - `Index field 'Item.Index' repeats the value '3'` against
    /// `Index field ``{table.Name}.{field.Name}`` repeats the value ``{cell.Value}```. So the
    /// quoted values are cut out and what is left between them has to be in the source.
    ///
    /// Short runs are dropped: "at" or "of" would match anything and prove nothing.
    /// </remarks>
    private static IEnumerable<string> LiteralRuns(string message)
        => Regex.Split(message, @"'[^']*'|\.\.\.")
                .Select(run => run.Trim())
                .Where(run => run.Length >= 12);

    /// <summary>
    /// Everywhere a message this tool prints can be written down, with adjacent string
    /// literals joined.
    /// </summary>
    /// <remarks>
    /// A message longer than a line is written as two literals and a `+`, which puts a seam
    /// in the middle of a sentence that is one sentence when printed. Searching the source as
    /// written would miss any quoted message that happens to span the seam - and the longer
    /// the message, the more likely that is, which is backwards.
    ///
    /// The message catalogs are read too, because a report that has been given an id no longer
    /// has its words in any `.cs` file - they are in `src/Messages/Catalog/`. This test found
    /// that the moment the first batch moved, which is what it is for: it asks whether the page
    /// quotes something the tool really says, and where the saying lives is not its business.
    /// </remarks>
    private static string Sources()
    {
        // `lib/` as well as `src/`, because the page documents what the runtime readers say
        // and not only what the converter says - and a reader's message is exactly the kind a
        // user meets without the converter anywhere in sight. Every extension, since the
        // readers are written in every language.
        var files = Directory
            .EnumerateFiles(Path.Combine(RepoLayout.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(RepoLayout.Root, "src", "Messages", "Catalog"), "*.json",
                SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoLayout.Root, "lib"), "*.*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}__pycache__{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText);

        string all = string.Join("\n", files);

        // A closing quote, a `+`, and the next literal's opening quote - the seam and nothing
        // else. Interpolated literals open with `$"`, so that form is matched too, and a
        // TypeScript template literal closes and reopens with a backtick.
        all = Regex.Replace(all, @"""\s*\+\s*\$?""", "");

        return Regex.Replace(all, @"`\s*\+\s*`", "");
    }

    [Fact]
    public void Every_quoted_message_is_one_the_tool_prints()
    {
        string source = Sources();

        var headings = QuotedHeading.Matches(Page)
            .SelectMany(m => new[] { m.Groups["message"].Value, m.Groups["second"].Value })
            .Where(value => !string.IsNullOrEmpty(value))
            .ToList();

        Assert.True(headings.Count >= 20,
            $"Only {headings.Count} quoted messages were found. The page's shape probably changed.");

        var missing = new List<string>();

        foreach (var heading in headings)
        {
            var runs = LiteralRuns(heading).ToList();

            if (runs.Count == 0)
            {
                missing.Add($"  `{heading}` has no literal run long enough to check");
                continue;
            }

            // One run is enough. A message split by two interpolations gives three fragments
            // and any of them identifies it; requiring all three would fail on the wrapping
            // that a long message gets in the source.
            if (!runs.Any(run => source.Contains(run, StringComparison.Ordinal)))
                missing.Add($"  `{heading}` — no source string contains: {string.Join(" | ", runs)}");
        }

        Assert.True(missing.Count == 0,
            "doc/troubleshooting.md quotes messages the tool does not print. Either the " +
            "message was reworded and the page needs updating, or the page invented one:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// And the page reaches every part of the tool a reader might be stuck in.
    /// </summary>
    /// <remarks>
    /// Not a count - a count grows on its own and says nothing. These are the stages a run
    /// goes through, and a page missing one sends the reader nowhere.
    /// </remarks>
    [Fact]
    public void The_page_covers_every_stage_of_a_run()
    {
        string page = Page;

        foreach (var section in new[]
                 {
                     "## 먼저 읽는 법",
                     "## 시트를 읽는 중",
                     "## 값을 해석하는 중",
                     "## 참조와 인덱스",
                     "## Recipe",
                     "## 구글 스프레드시트",
                     "## 데이터베이스",
                     "## `--serve`",
                     "## 생성 결과가 이상할 때",
                 })
        {
            Assert.Contains(section, page);
        }
    }
}
