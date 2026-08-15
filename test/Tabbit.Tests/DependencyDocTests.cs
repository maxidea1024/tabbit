using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// `doc/dependencies.md` lists the external packages and what each one is for. A list kept by
/// hand rots: the README carried one that named NPOI long after the streaming reader replaced
/// it, and misspelled another. This makes the drift fail a build instead of waiting to be
/// noticed.
///
/// What is checked is the name and the version, not the prose - the point of the document is
/// the prose, and a test cannot tell whether it is still true.
/// </summary>
public class DependencyDocTests
{
    private static readonly string Doc = Path.Combine(RepoLayout.Root, "doc", "dependencies.md");

    /// <summary>Every `PackageReference` in the repository, by project.</summary>
    private static IEnumerable<(string Project, string Name, string Version)> References()
    {
        foreach (var project in Directory.EnumerateFiles(RepoLayout.Root, "*.csproj", SearchOption.AllDirectories))
        {
            // `obj` carries generated copies of nothing we own, and the fixture validation
            // projects are inputs to the suite rather than parts of the tool.
            var relative = Path.GetRelativePath(RepoLayout.Root, project).Replace('\\', '/');
            if (relative.Contains("/obj/") || relative.Contains("/bin/")) continue;

            foreach (Match match in Regex.Matches(
                         File.ReadAllText(project),
                         @"<PackageReference\s+Include=""(?<name>[^""]+)""\s+Version=""(?<version>[^""]+)"""))
            {
                yield return (relative, match.Groups["name"].Value, match.Groups["version"].Value);
            }
        }
    }

    [Fact]
    public void Every_referenced_package_is_documented_with_its_version()
    {
        var text = File.ReadAllText(Doc);

        // Without this the test passes when the projects are not read at all, which is the
        // one way a gate like this fails silently.
        Assert.True(References().Count() > 10, "No PackageReference was read from the repository's projects.");

        var missing = References()
            .Where(r => !text.Contains(r.Name, StringComparison.Ordinal))
            .Select(r => $"{r.Name} ({r.Project})")
            .Distinct()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"doc/dependencies.md does not name these referenced packages:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Select(m => "  " + m)));

        var wrongVersion = References()
            .Where(r => text.Contains(r.Name, StringComparison.Ordinal))
            .Where(r => !text.Contains(r.Version, StringComparison.Ordinal))
            .Select(r => $"{r.Name} is referenced at {r.Version} ({r.Project}), which the document does not state")
            .Distinct()
            .ToList();

        Assert.True(
            wrongVersion.Count == 0,
            string.Join(Environment.NewLine, wrongVersion));
    }

    [Fact]
    public void The_document_names_no_package_the_repository_does_not_reference()
    {
        var referenced = References().Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        // Only the rows of the tables are read. Prose names packages that were dropped -
        // saying what a dependency used to be is the useful part of such a line.
        var named = Regex.Matches(
                File.ReadAllText(Doc),
                @"^\|\[(?<name>[^\]]+)\]\([^)]*\)\|", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToList();

        Assert.NotEmpty(named);

        var stale = named.Where(n => !referenced.Contains(n)).ToList();

        Assert.True(
            stale.Count == 0,
            $"doc/dependencies.md lists packages nothing references any more:{Environment.NewLine}"
            + string.Join(Environment.NewLine, stale.Select(s => "  " + s)));
    }
}
