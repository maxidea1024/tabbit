using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Tabbit.Tests;

/// <summary>
/// Compares a conversion's output tree against its committed golden tree.
///
/// Every mismatch in the tree is collected before reporting, because a change in
/// the converter usually ripples across many generated files at once and seeing
/// them together is what makes the diff interpretable.
/// </summary>
internal static class GoldenComparer
{
    /// <summary>
    /// Set TABBIT_UPDATE_GOLDEN=1 to overwrite the golden tree with the current
    /// output instead of asserting against it. Intended for the deliberate
    /// behaviour changes in the bug-fix and feature phases, where the review step
    /// is reading the resulting git diff.
    /// </summary>
    public static bool UpdateRequested
        => Environment.GetEnvironmentVariable("TABBIT_UPDATE_GOLDEN") == "1";

    public static void Verify(string scenario) => Verify(scenario, scenario);

    /// <summary>
    /// Compares one scenario's output against another scenario's golden tree.
    ///
    /// For the cases where two recipes are meant to produce the same thing by
    /// different means - the same outputs written once through the named recipe
    /// sections and once through the `Targets` list. Byte equality across every
    /// artifact is a stronger statement than any assertion about a few field names.
    /// </summary>
    public static void Verify(string scenario, string goldenScenario)
    {
        string outputDir = RepoLayout.OutputDir(scenario);
        string goldenDir = RepoLayout.GoldenDir(goldenScenario);

        if (UpdateRequested)
        {
            // Recording would overwrite the other scenario's golden tree with this
            // one's output, which is how an equivalence check quietly stops checking
            // anything. The scenario that owns the tree is the one that records it.
            if (scenario != goldenScenario)
            {
                throw new InvalidOperationException(
                    $"Scenario `{scenario}` is compared against `{goldenScenario}`'s golden tree and " +
                    $"cannot record it. Update `{goldenScenario}` instead.");
            }

            Update(outputDir, goldenDir);
            return;
        }

        if (!Directory.Exists(goldenDir))
        {
            throw new InvalidOperationException(
                $"No golden tree for scenario `{goldenScenario}` at {goldenDir}. " +
                $"Run the suite once with TABBIT_UPDATE_GOLDEN=1 to record one.");
        }

        var expected = Enumerate(goldenDir);
        var actual = Enumerate(outputDir);

        var failures = new List<string>();

        foreach (var missing in expected.Except(actual).OrderBy(x => x))
            failures.Add($"MISSING  {missing}  (in golden, not produced)");

        foreach (var unexpected in actual.Except(expected).OrderBy(x => x))
            failures.Add($"NEW      {unexpected}  (produced, not in golden)");

        foreach (var relative in expected.Intersect(actual).OrderBy(x => x))
        {
            string goldenFile = Path.Combine(goldenDir, relative);
            string outputFile = Path.Combine(outputDir, relative);

            if (OutputNormalizer.IsBinary(relative))
            {
                var a = File.ReadAllBytes(goldenFile);
                var b = File.ReadAllBytes(outputFile);

                if (!a.SequenceEqual(b))
                    failures.Add($"DIFFERS  {relative}  (binary: {a.Length} bytes golden vs {b.Length} bytes produced)");

                continue;
            }

            string expectedText = OutputNormalizer.Normalize(relative, File.ReadAllText(goldenFile));
            string actualText = OutputNormalizer.Normalize(relative, File.ReadAllText(outputFile));

            if (expectedText != actualText)
                failures.Add($"DIFFERS  {relative}{Environment.NewLine}{FirstDifference(expectedText, actualText)}");
        }

        if (failures.Count > 0)
        {
            throw new GoldenMismatchException(
                $"Scenario `{scenario}` does not match its golden tree ({failures.Count} problem(s)):" +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, failures) +
                Environment.NewLine + Environment.NewLine +
                "If the change is intended, re-run with TABBIT_UPDATE_GOLDEN=1 and review the git diff.");
        }
    }

    /// <summary>
    /// Returns a short excerpt around the first differing line. Whole-file diffs
    /// of generated code are unreadable in test output; the first divergence is
    /// almost always the informative one.
    /// </summary>
    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');

        int line = 0;
        while (line < e.Length && line < a.Length && e[line] == a[line])
            line++;

        var sb = new StringBuilder();
        sb.AppendLine($"         first difference at line {line + 1}:");
        sb.AppendLine($"           golden:   {(line < e.Length ? e[line] : "<end of file>")}");
        sb.AppendLine($"           produced: {(line < a.Length ? a[line] : "<end of file>")}");
        return sb.ToString();
    }

    private static void Update(string outputDir, string goldenDir)
    {
        if (Directory.Exists(goldenDir))
            Directory.Delete(goldenDir, recursive: true);

        foreach (var relative in Enumerate(outputDir))
        {
            string target = Path.Combine(goldenDir, relative);
            string source = Path.Combine(outputDir, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target));

            if (OutputNormalizer.IsBinary(relative))
            {
                File.Copy(source, target, overwrite: true);
                continue;
            }

            // Recorded masked, so what is committed is what is actually compared. The
            // alternative freezes one machine's clock, user name and checked-out commit
            // into the repository, where they read as if they mattered.
            File.WriteAllText(target, OutputNormalizer.Normalize(relative, File.ReadAllText(source)));
        }
    }

    /// <summary>Relative paths of every file under <paramref name="root"/>, slash-normalized.</summary>
    private static HashSet<string> Enumerate(string root)
    {
        if (!Directory.Exists(root))
            return new HashSet<string>(StringComparer.Ordinal);

        return new HashSet<string>(
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')),
            StringComparer.Ordinal);
    }
}

internal sealed class GoldenMismatchException : Exception
{
    public GoldenMismatchException(string message) : base(message) { }
}
