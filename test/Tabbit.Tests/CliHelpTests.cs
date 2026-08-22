using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CommandLine;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The help screen against the options it describes.
///
/// `HelpScreen` is written out by hand rather than generated, because usage forms, examples
/// and which options belong to which mode are what a generated list cannot say. The cost of
/// that decision is exactly one failure mode - an option is added and the screen is not
/// touched - and this closes it. Nobody adding an option has to remember these tests; they
/// remember for them.
///
/// spec/cli-help.md section 8.
/// </summary>
public class CliHelpTests
{
    /// <summary>Every long name declared on <see cref="Options"/>, with its dashes.</summary>
    private static IReadOnlyList<OptionAttribute> Declared { get; } =
        typeof(Options)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetCustomAttribute<OptionAttribute>())
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!)
            .ToList();

    /// <summary>
    /// The names the screen spells out, taken from the option column only.
    /// </summary>
    /// <remarks>
    /// The column rather than the whole text, because the prose mentions options too -
    /// the examples use `--full`, and `--template`'s description names `--new-recipe`. A
    /// scan of everything would call those declarations and pass whether or not the option
    /// has a row of its own.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> Documented { get; } = ReadOptionColumn();

    private static Dictionary<string, string> ReadOptionColumn()
    {
        // `  -r, --recipe=FILE` or `      --full`, at the start of a line and nowhere else.
        var row = new Regex(@"^  (?:(-[A-Za-z]), |    )(--[a-z][a-z-]*)", RegexOptions.Multiline);

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in row.Matches(HelpScreen.Body))
            found[match.Groups[2].Value] = match.Groups[1].Value;

        return found;
    }

    /// <summary>
    /// Every declared option has a row on the screen.
    /// </summary>
    /// <remarks>
    /// The failure this exists for: an option is added to `Options` and the screen, which is
    /// no longer generated from it, keeps describing the program as it was.
    /// </remarks>
    [Fact]
    public void EveryOptionIsOnTheHelpScreen()
    {
        var missing = Declared
            .Select(option => "--" + option.LongName)
            .Where(name => !Documented.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"Declared in Options but absent from HelpScreen.Body: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Every option the screen names exists.
    /// </summary>
    /// <remarks>
    /// The other direction, which catches the two things the first one cannot: an option that
    /// was deleted and left on the screen, and a typo. A misspelled name reads as perfectly
    /// good documentation right up to the moment somebody copies it.
    /// </remarks>
    [Fact]
    public void EveryOptionOnTheHelpScreenExists()
    {
        var declared = Declared.Select(option => "--" + option.LongName).ToHashSet(StringComparer.Ordinal);

        // The two the parser owns rather than `Options`. They are answered before parsing,
        // so they have no property to declare them - and they still belong on the screen.
        declared.Add("--help");
        declared.Add("--version");

        var unknown = Documented.Keys
            .Where(name => !declared.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            $"Named by HelpScreen.Body but not declared in Options: {string.Join(", ", unknown)}");
    }

    /// <summary>
    /// An option with a short name shows it, and one without does not invent one.
    /// </summary>
    /// <remarks>
    /// A short name that works but is undocumented is a feature nobody finds. One that is
    /// documented but does not work is worse: it is a command line that fails for the person
    /// who trusted the screen.
    /// </remarks>
    [Fact]
    public void ShortNamesAgreeWithWhatIsDeclared()
    {
        var wrong = new List<string>();

        foreach (var option in Declared)
        {
            string name = "--" + option.LongName;

            if (!Documented.TryGetValue(name, out string shown))
                continue;   // the first test reports this one

            string declared = string.IsNullOrEmpty(option.ShortName) ? "" : "-" + option.ShortName;

            if (shown != declared)
            {
                wrong.Add($"{name}: declared '{(declared == "" ? "none" : declared)}', "
                          + $"screen shows '{(shown == "" ? "none" : shown)}'");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// The option column is one column, and nothing runs off an eighty-column terminal.
    /// </summary>
    /// <remarks>
    /// Written by hand means aligned by hand. A row that is two spaces out is not a defect
    /// anybody files, so it stays - and the screen slowly stops reading as a table.
    ///
    /// Only the option rows and their continuations are measured. The usage forms, the
    /// examples and the epilogue are prose with their own shapes.
    /// </remarks>
    [Fact]
    public void TheOptionColumnIsAligned()
    {
        const int DescriptionColumn = 26;
        const int ContinuationColumn = 28;
        const int Width = 80;

        var optionRow = new Regex(@"^  (?:-[A-Za-z], |    )--[a-z][a-z-]*(?:=[A-Z]+)?");

        var problems = new List<string>();
        string[] lines = HelpScreen.Body.Replace("\r\n", "\n").Split('\n');

        // Whether the line above was an option row or one of its continuations. A blank
        // line ends the run, which is what separates the option list from the epilogue:
        // `Exit codes:` looks exactly like a group title, and the codes under it are
        // indented, so a rule that keyed on the colon would measure those too.
        bool underAnOptionRow = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.Length > Width)
                problems.Add($"line {i + 1} is {line.Length} columns wide: {line}");

            if (line.Trim().Length == 0)
            {
                underAnOptionRow = false;
                continue;
            }

            var match = optionRow.Match(line);

            if (match.Success)
            {
                underAnOptionRow = true;

                string rest = line[match.Length..];

                // A name too long for the column carries its description on the next line.
                if (rest.Trim().Length == 0)
                    continue;

                int column = match.Length + (rest.Length - rest.TrimStart().Length);

                if (column != DescriptionColumn)
                    problems.Add($"line {i + 1} starts its description at column {column}: {line}");

                continue;
            }

            if (underAnOptionRow && line[0] == ' ')
            {
                int column = line.Length - line.TrimStart().Length;

                if (column != ContinuationColumn && column != DescriptionColumn)
                    problems.Add($"line {i + 1} continues at column {column}: {line}");

                continue;
            }

            // A group title, or prose. Either way this is not part of a row.
            underAnOptionRow = false;
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The screen says what the exit codes are, and says the same three the program returns.
    /// </summary>
    [Fact]
    public void TheExitCodesOnTheScreenAreTheOnesReturned()
    {
        Assert.Contains($"  {ExitCode.Success}   the run did what it was asked to", HelpScreen.Body);
        Assert.Contains($"  {ExitCode.Failed}   the run failed, and said why", HelpScreen.Body);
        Assert.Contains($"  {ExitCode.NothingToDo}   nothing had changed", HelpScreen.Body);
    }

    /// <summary>
    /// Asking for the help screen succeeds.
    /// </summary>
    /// <remarks>
    /// It exited 1 before this, so `tabbit --help && next-thing` failed on the `&&`. All
    /// four spellings, because the two Windows ones are the ones nobody would think to check.
    /// </remarks>
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void AskingForHelpSucceeds(string argument)
    {
        var result = TabbitRunner.Invoke(argument);

        Assert.True(result.Succeeded, result.Describe());
        Assert.Contains("Usage: tabbit -r RECIPE", result.StdOut);
        Assert.Contains("Exit codes:", result.StdOut);
    }

    /// <summary>
    /// `--version` succeeds, and says which runtime this build is on.
    /// </summary>
    /// <remarks>
    /// The runtime is the value an issue report is asked for first, and it was the one thing
    /// this option did not print while the run's own first line did.
    /// </remarks>
    [Fact]
    public void VersionSaysWhichRuntime()
    {
        var result = TabbitRunner.Invoke("--version");

        Assert.True(result.Succeeded, result.Describe());
        Assert.Contains(".NET", result.StdOut);
        Assert.Contains(".tcb v", result.StdOut);
    }

    /// <summary>
    /// A misuse says what was wrong and stops - it does not print the whole screen.
    /// </summary>
    /// <remarks>
    /// The behaviour being locked in is the shortness. Ninety lines of options under an
    /// error message scroll the error off the top of the terminal, which is how the old
    /// screen answered every mistake.
    /// </remarks>
    [Theory]
    [InlineData(new[] { "--nope" }, "unrecognised option '--nope'")]
    [InlineData(new[] { "-Z" }, "unrecognised option '-Z'")]
    [InlineData(new[] { "--verbose" }, "no recipe given")]
    public void AMisuseSaysWhatWasWrongAndNothingElse(string[] arguments, string expected)
    {
        var result = TabbitRunner.Invoke(arguments);

        Assert.Equal(ExitCode.Failed, result.ExitCode);
        Assert.Contains(expected, result.StdErr);
        Assert.Contains("Try 'tabbit --help' for more information.", result.StdErr);

        // The option list is not in it.
        Assert.DoesNotContain("Exit codes:", result.StdErr);
        Assert.DoesNotContain("--force-output", result.StdErr);
    }

    /// <summary>
    /// Running it with nothing at all says so, rather than printing the screen.
    /// </summary>
    [Fact]
    public void NoArgumentsSaysNoOptionsWereGiven()
    {
        var result = TabbitRunner.Invoke();

        Assert.Equal(ExitCode.Failed, result.ExitCode);
        Assert.Contains("no options given", result.StdErr);

        int lines = result.StdErr
            .Split('\n')
            .Count(line => line.Trim().Length > 0);

        Assert.True(lines <= 3, $"expected three lines, got {lines}:{Environment.NewLine}{result.StdErr}");
    }
}
