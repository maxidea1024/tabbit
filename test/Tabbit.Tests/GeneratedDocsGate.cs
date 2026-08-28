using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The documentation that shows a sheet beside the code it generates is a build product, and
/// this is what says it is current.
/// </summary>
/// <remarks>
/// Three things feed those pages and none of them is written by hand:
///
///   the sheet pictures   read out of `doc-showcase.xlsx` cell by cell
///   the code beside them cut out of `test/fixtures/golden/doc-showcase`
///   the pages themselves assembled from those two
///
/// So a generator change moves the golden, and a moved golden has to reach the pages. Nothing
/// else would notice: the pages are `.md` in a folder no test reads, and a stale excerpt looks
/// exactly like a current one. That is the failure this gate exists for - it is the same
/// argument the golden trees themselves rest on, one step further along.
///
/// The generators write into a temporary tree here rather than into the repository, so a
/// failing run reports a difference instead of quietly fixing it. Regenerate with:
///
///     python doc/figures/grid_dump.py
///     python doc/figures/showcase.py
/// </remarks>
public class GeneratedDocsGate
{
    private static string Doc => Path.Combine(RepoLayout.Root, "doc");
    private static string Figures => Path.Combine(Doc, "figures");

    [Fact]
    public void Generated_documentation_matches_its_sources()
    {
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated documentation. {why}");

        string work = Path.Combine(Path.GetTempPath(), "tabbit-doc-gate-" + Guid.NewGuid().ToString("N"));
        string grids = Path.Combine(work, "grids");
        string figures = Path.Combine(work, "figures");
        string doc = Path.Combine(work, "doc");

        try
        {
            Directory.CreateDirectory(grids);
            Directory.CreateDirectory(figures);
            Directory.CreateDirectory(doc);

            var environment = new Dictionary<string, string>
            {
                { "TABBIT_DOC_GRIDS", grids },
                { "TABBIT_DOC_FIGURES", figures },
                { "TABBIT_DOC_DIR", doc },
                { "PYTHONIOENCODING", "utf-8" },
            };

            Run(environment, Path.Combine("doc", "figures", "grid_dump.py"));
            Run(environment, Path.Combine("doc", "figures", "showcase.py"));

            var stale = new List<string>();

            // The grids and the pictures live beside the generators; the pages live in `doc/`.
            Compare(grids, Path.Combine(Figures, "grids"), "doc/figures/grids", stale);
            Compare(figures, Figures, "doc/figures", stale, only: "showcase-");
            Compare(doc, Doc, "doc", stale, only: "generated-code");

            Assert.True(stale.Count == 0,
                "The generated documentation is behind its sources. Run"
                + $"{Environment.NewLine}    python doc/figures/grid_dump.py"
                + $"{Environment.NewLine}    python doc/figures/showcase.py"
                + $"{Environment.NewLine}{Environment.NewLine}"
                + string.Join(Environment.NewLine, stale));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    private static void Run(Dictionary<string, string> environment, string script)
    {
        var result = ConformanceHarness.Execute(
            ConformanceHarness.Python, RepoLayout.Root, environment, script);

        Assert.True(result.Succeeded,
            $"`{script}` failed.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// Every file the generators just wrote, against the one committed beside it.
    /// </summary>
    /// <remarks>
    /// One direction only, and deliberately: a file in the repository that the generators no
    /// longer write is a leftover, and deleting one is not what this gate is for. What it has
    /// to catch is a committed file that no longer matches what the sources say.
    /// </remarks>
    private static void Compare(
        string fresh, string committed, string label, List<string> stale, string only = null)
    {
        foreach (string produced in Directory.GetFiles(fresh, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(fresh, produced);
            if (only != null && !relative.Replace('\\', '/').StartsWith(only, StringComparison.Ordinal))
                continue;

            string beside = Path.Combine(committed, relative);
            string shown = $"{label}/{relative.Replace('\\', '/')}";

            if (!File.Exists(beside))
            {
                stale.Add($"  {shown} - not committed");
                continue;
            }

            // Byte for byte. The generators write LF and UTF-8 without a mark, so a file that
            // differs only in line endings is a file somebody edited by hand in an editor that
            // rewrote them - which is exactly the edit this gate is asking about.
            if (!File.ReadAllBytes(produced).SequenceEqual(File.ReadAllBytes(beside)))
                stale.Add($"  {shown} - differs");
        }
    }
}
