using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// How every generated text file ends.
///
/// Exactly one newline, which is what makes the last line a line. It used to be two: the
/// printer the templates replaced split on the final newline, which yields one empty
/// segment, and then appended a newline to every segment including that one. The accident
/// was kept on purpose while the generators moved onto templates, so that the golden trees
/// could prove the bytes had not moved - and then outlived the reason, leaving a blank
/// line at the end of every generated file for a formatter or a reviewer to remove.
///
/// Worth a gate rather than a fixed golden, because the golden trees would record two
/// newlines just as happily as one. They answer "did the output change"; this answers
/// "is the output right".
/// </summary>
public class GeneratedFileEndingTests
{
    /// <summary>
    /// Scenarios covering every generator between them, so no language's ending is taken
    /// on trust from another's.
    /// </summary>
    private static readonly string[] Scenarios = { "core", "conformance", "reserved-words" };

    /// <summary>
    /// Extensions worth checking: text a person or a compiler reads.
    ///
    /// A `.tcb` is binary and a `.map` is a generated artefact of a generated artefact.
    /// Everything else the tool writes is source or documentation.
    /// </summary>
    private static readonly HashSet<string> TextExtensions = new HashSet<string>(
        new[]
        {
            ".cs", ".ts", ".h", ".c", ".cpp", ".go", ".rs", ".py", ".java", ".kt", ".rb",
            ".php", ".dart", ".html", ".json", ".mod", ".toml",
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The three targets MSVC compiles carry a UTF-8 byte order mark; nothing else does.
    /// </summary>
    /// <remarks>
    /// A byte-level gate rather than a compile, because the compile gates pass `/utf-8`
    /// and so would answer yes either way. A consumer's project does not necessarily pass
    /// it, and without a mark MSVC reads the file in the system codepage - where a comment
    /// carried over from a Korean sheet can end in a byte that is a backslash, which
    /// continues the comment over the declaration below and reports a syntax error on a
    /// line that is correct. The rescue sample's C++ did exactly that.
    ///
    /// The other ten targets are checked for the absence of the mark, because it is not
    /// free: it shows up in diffs, and a shell that reads a generated file expecting it to
    /// begin with `#!` or `&lt;?php` gets three bytes it did not want.
    /// </remarks>
    [Fact]
    public void Only_the_msvc_targets_carry_a_byte_order_mark()
    {
        // The directory each target writes into, under a scenario's output root.
        var withMark = new HashSet<string>(new[] { "c", "cpp", "unreal" }, StringComparer.OrdinalIgnoreCase);

        var offenders = new List<string>();

        foreach (var scenario in Scenarios)
        {
            var conversion = TabbitRunner.Convert(scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

            string root = RepoLayout.OutputDir(scenario);

            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!TextExtensions.Contains(Path.GetExtension(path)))
                    continue;

                if (path.Contains($"{Path.DirectorySeparatorChar}target{Path.DirectorySeparatorChar}")
                    || path.Contains("__pycache__")
                    || path.Contains("node_modules")
                    || path.Contains($"{Path.DirectorySeparatorChar}classes{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(root, path);
                string target = relative.Split(Path.DirectorySeparatorChar)[0];

                // The Unreal module's .Build.cs is read by UnrealBuildTool, not by cl.
                bool expected = withMark.Contains(target)
                    && !path.EndsWith(".Build.cs", StringComparison.OrdinalIgnoreCase);

                var head = new byte[3];
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Read(head, 0, 3) < 3)
                        continue;
                }

                bool marked = head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;

                if (marked != expected)
                {
                    offenders.Add(
                        $"  {Path.GetRelativePath(RepoLayout.Root, path)}: " +
                        (marked ? "has a byte order mark and should not" : "has no byte order mark"));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Byte order marks are not where they should be:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Take(40)));
    }

    [Fact]
    public void Every_generated_text_file_ends_with_exactly_one_newline()
    {
        var offenders = new List<string>();

        foreach (var scenario in Scenarios)
        {
            var conversion = TabbitRunner.Convert(scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

            string root = RepoLayout.OutputDir(scenario);

            foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!TextExtensions.Contains(Path.GetExtension(path)))
                    continue;

                // Anything a language toolchain produced while the suite was checking the
                // output - a Cargo target directory, a Python __pycache__, node_modules -
                // is not this tool's writing.
                if (path.Contains($"{Path.DirectorySeparatorChar}target{Path.DirectorySeparatorChar}")
                    || path.Contains("__pycache__")
                    || path.Contains("node_modules")
                    || path.Contains($"{Path.DirectorySeparatorChar}classes{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                string text = File.ReadAllText(path);

                if (text.Length == 0)
                    continue;

                string relative = Path.GetRelativePath(RepoLayout.Root, path);

                if (!text.EndsWith("\n", StringComparison.Ordinal))
                {
                    offenders.Add($"  {relative}: no newline at the end");
                    continue;
                }

                if (text.EndsWith("\n\n", StringComparison.Ordinal))
                    offenders.Add($"  {relative}: ends with a blank line");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Generated files do not end with exactly one newline:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders.Take(40)));
    }
}
