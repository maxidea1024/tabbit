using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the extension of the data files is a setting, and that the generated readers read it.
///
/// Every other recipe leaves it at `.tcb`, so nothing ever read the setting back out. The C#
/// accessor had a `".tcb"` literal where the recipe's value belonged: a recipe that set the
/// extension on both the export and the target got the right file names out of the exporter
/// and a reader that looked for the default anyway. Nothing failed - the generated code simply
/// did not find its data, in somebody else's project.
///
/// A golden comparison could not have caught it either. The literal was in the committed
/// output, so it was the correct answer.
/// </summary>
public class TableExtensionTests
{
    private const string Scenario = "table-extension";

    /// <summary>What that recipe sets it to, on the export and on every target.</summary>
    private const string Extension = ".bytes";

    /// <summary>
    /// One line per target: the file its accessor lands in, and how that language spells the
    /// default.
    /// </summary>
    /// <remarks>
    /// Spelled out per language rather than searched for, because "the string `.bytes` appears
    /// somewhere in the output" would pass on a comment. What is being checked is that it is
    /// the default of the parameter a caller does not pass.
    /// </remarks>
    public static TheoryData<string, string> Defaults => new TheoryData<string, string>
    {
        { "python/x/tables.py",      "def read_all(self, base_path, file_extension=\".bytes\")" },
        { "ruby/a.rb",               "def read_all(base_path, file_extension = '.bytes')" },
        { "php/A.php",               "string $fileExtension = '.bytes'" },
        { "dart/a.dart",             "[String fileExtension = '.bytes']" },
        { "kotlin/x/A.kt",           "fileExtension: String = \".bytes\"" },
        { "csharp/A.cs",             "string fileExtension = \".bytes\"" },
        { "cpp/A.h",                 "const std::string& file_extension = \".bytes\"" },
        { "unreal/X/Public/A.h",     "const FString& FileExtension = TEXT(\".bytes\")" },

        // The four with no default arguments, which delegate instead.
        { "java/x/A.java",           "readAll(basePath, \".bytes\");" },
        { "go/tables.go",     "return t.ReadAllWithExtension(basePath, \".bytes\")" },
        { "rust/src/tables.rs",      "self.read_all_with_extension(base_path, \".bytes\")" },
        { "c/A.c",                   "data, base_path, \".bytes\", error, error_size);" },
    };

    [Theory]
    [MemberData(nameof(Defaults))]
    public void The_accessor_defaults_to_the_extension_the_recipe_set(string file, string expected)
    {
        Convert();

        string path = Path.Combine(RepoLayout.OutputDir(Scenario), file.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path), $"{file} was not generated.");

        Assert.Contains(expected, File.ReadAllText(path));
    }

    /// <summary>
    /// And the exporter wrote the files under that name, so the two halves agree.
    /// </summary>
    [Fact]
    public void The_exporter_writes_that_extension()
    {
        Convert();

        var written = Directory
            .GetFiles(Path.Combine(RepoLayout.OutputDir(Scenario), "binary"))
            .Select(Path.GetFileName)
            .Where(name => !name.StartsWith("manifest", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(written);
        Assert.All(written, name => Assert.EndsWith(Extension, name));
    }

    /// <summary>
    /// And the parameter is wired through, not just present in the signature.
    /// </summary>
    /// <remarks>
    /// The two tests above read the generated text. A default of `.bytes` on a parameter the
    /// body then ignores would satisfy both - which is close to the shape of the bug they were
    /// written for, so it is worth one run.
    ///
    /// Python because it needs an interpreter and nothing else. The same wiring is the same
    /// three lines in every target, and they are checked as text.
    /// </remarks>
    [Fact]
    public void An_extension_passed_at_the_call_reads_files_with_that_extension()
    {
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why), why);

        Convert();

        // The same bytes under a name neither the recipe nor the exporter chose.
        string binary = Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

        foreach (var written in Directory.GetFiles(binary, "*" + Extension))
            File.Copy(written, Path.ChangeExtension(written, ".dat"), overwrite: true);

        string escaped = binary.Replace("\\", "\\\\");

        // The third call is the one that decides it. The first two read the same bytes under
        // two names, so a body that ignores the parameter passes both - which is the shape of
        // the bug, not a check on it. An extension nothing was written under has to fail, and
        // it can only fail if the parameter reached the path.
        var result = ConformanceHarness.RunPythonSnippet(Scenario,
            "import x\n" +
            "d = x.Tables()\n" +
            $"d.read_all(r'{escaped}')\n" +
            "assert len(d.template.records) > 0, 'the default extension read nothing'\n" +
            "n = len(d.template.records)\n" +
            "r = x.Tables()\n" +
            $"r.read_all(r'{escaped}', '.dat')\n" +
            "assert len(r.template.records) == n, 'the passed extension read a different table'\n" +
            "try:\n" +
            $"    x.Tables().read_all(r'{escaped}', '.nothing-was-written-here')\n" +
            "except OSError:\n" +
            "    pass\n" +
            "else:\n" +
            "    raise AssertionError('an extension nothing was written under read something')\n" +
            "print('ok', n)\n");

        Assert.True(result.Succeeded,
            $"Reading with a passed extension failed.{Environment.NewLine}{result.Output}");

        Assert.Contains("ok", result.StdOut);
    }

    /// <summary>
    /// No generated code still names the default.
    /// </summary>
    /// <remarks>
    /// The one that finds a literal somewhere new. The list above names the twelve accessors,
    /// and a thirteenth place hard-coding `.tcb` - a table's own read, say - would not be in
    /// it. Comments are excluded because the readers describe the format they read and say
    /// `.tcb` while doing it, which is prose about the tool rather than a path.
    /// </remarks>
    [Fact]
    public void Nothing_generated_still_names_the_default_extension()
    {
        Convert();

        var offenders = new List<string>();
        string root = RepoLayout.OutputDir(Scenario);

        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(path) == Extension || path.Contains("manifest"))
                continue;

            foreach (var (line, number) in File.ReadLines(path).Select((line, i) => (line, i + 1)))
            {
                if (!Literal.IsMatch(line) || IsComment(line))
                    continue;

                offenders.Add($"  {Path.GetRelativePath(root, path)}:{number}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Generated code names `.tcb` where the recipe set `{Extension}`:" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    /// <summary>
    /// `.tcb` where a file name ends, and not where one merely starts with it.
    /// </summary>
    /// <remarks>
    /// The boundary is what makes it an extension rather than the start of a longer word. It
    /// went in when the extension was `.table` and a plain substring search matched Python's
    /// `from .tables import Tables` - the module holding the accessor, nothing to do with the
    /// extension. Nothing in the output happens to start with `.tcb` today, which is luck
    /// rather than a reason to search for a bare prefix.
    /// </remarks>
    private static readonly System.Text.RegularExpressions.Regex Literal =
        new System.Text.RegularExpressions.Regex(@"\.tcb(?![A-Za-z0-9_])");

    /// <summary>
    /// A comment in any of the every language, near enough.
    /// </summary>
    /// <remarks>
    /// Line-leading only. A trailing comment after code would be missed, and a string holding
    /// `//` would be mistaken for one - neither matters here, because what this is looking for
    /// is a path built into generated code, and those are not written after a statement.
    ///
    /// A Python docstring counts: it opens the line and it is prose, which is the whole test.
    /// </remarks>
    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();

        return trimmed.StartsWith("\"\"\"", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("#", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("///", StringComparison.Ordinal)
            || trimmed.StartsWith("--", StringComparison.Ordinal);
    }

    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");
    }
}
