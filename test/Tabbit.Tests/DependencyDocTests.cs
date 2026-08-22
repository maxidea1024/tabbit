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

    /// <summary>
    /// Every third-party dependency a generated manifest declares is named in the document.
    /// </summary>
    /// <remarks>
    /// The check above reads this repository's own `PackageReference`s, which is a different
    /// list: a dependency the generated code carries appears in nobody's csproj. That list
    /// had drifted - Python's reader needs `cryptography` for an encrypted file and the
    /// document said the readers use only their standard libraries - and nothing noticed,
    /// because nothing was looking.
    ///
    /// The manifests under side-by-side/ are what is read. They are committed and reviewed
    /// as output, so a new dependency arrives in a diff either way; this makes it arrive in
    /// the document as well. A missing manifest fails rather than skips - a gate that turns
    /// itself off when a path moves is worse than no gate.
    /// </remarks>
    [Fact]
    public void Every_dependency_a_generated_manifest_declares_is_documented()
    {
        string text = File.ReadAllText(Doc);
        var declared = new List<(string Manifest, string Name)>();

        // Cargo.toml: the names under [dependencies], up to the next section.
        string cargo = Path.Combine(RepoLayout.Root, "side-by-side", "rust", "Cargo.toml");

        Assert.True(File.Exists(cargo), $"{cargo} is not there any more; this gate needs it.");

        bool inDependencies = false;

        foreach (string line in File.ReadAllLines(cargo))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                inDependencies = trimmed == "[dependencies]";
                continue;
            }

            if (!inDependencies || trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;

            int equals = trimmed.IndexOf('=');

            if (equals > 0)
                declared.Add(("Cargo.toml", trimmed.Substring(0, equals).Trim()));
        }

        // Package.swift: the repository name of each package URL.
        string manifest = Path.Combine(RepoLayout.Root, "side-by-side", "swift", "Package.swift");

        Assert.True(File.Exists(manifest), $"{manifest} is not there any more; this gate needs it.");

        foreach (Match match in Regex.Matches(
                     File.ReadAllText(manifest), @"\.package\(\s*url:\s*""(?<url>[^""]+)"""))
        {
            string url = match.Groups["url"].Value;
            string name = url.Split('/')[^1];

            if (name.EndsWith(".git", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 4);

            declared.Add(("Package.swift", name));
        }

        // Without this the test passes when neither manifest declares anything, which is the
        // one way a gate like this fails silently.
        Assert.NotEmpty(declared);

        var missing = declared
            .Where(d => !text.Contains(d.Name, StringComparison.Ordinal))
            .Select(d => $"{d.Name} (declared by {d.Manifest})")
            .Distinct()
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"doc/dependencies.md does not name these generated-code dependencies:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Select(m => "  " + m)));
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
