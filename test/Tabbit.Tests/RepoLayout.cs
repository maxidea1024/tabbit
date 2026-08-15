using System;
using System.IO;

namespace Tabbit.Tests;

/// <summary>
/// Resolves the handful of repository paths the regression tests need.
///
/// Recipe files use repo-root-relative paths, so the CLI has to be invoked with
/// the repository root as its working directory. Everything here hangs off that.
/// </summary>
internal static class RepoLayout
{
    private static readonly Lazy<string> _root = new Lazy<string>(Locate);

    public static string Root => _root.Value;

    public static string CliProject => Path.Combine(Root, "src", "Tabbit.csproj");

    public static string Recipe(string scenario)
        => Path.Combine(Root, "test", "fixtures", "recipes", scenario + ".json");

    public static string GoldenDir(string scenario)
        => Path.Combine(Root, "test", "fixtures", "golden", scenario);

    public static string OutputDir(string scenario)
        => Path.Combine(Root, "test", "fixtures", "output", scenario);

    private static string Locate()
    {
        // A file as well as a directory: a linked worktree's `.git` is a file pointing at
        // the shared one. Looking only for the directory walked straight past the worktree
        // and found the checkout it was made from, so a run inside a worktree converted that
        // other tree's fixtures and wrote into that other tree's output - which is not
        // isolation, and is worse than none because it looks like isolation.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null
            && !Directory.Exists(Path.Combine(dir.FullName, ".git"))
            && !File.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException(
                $"Could not locate the repository root walking up from {AppContext.BaseDirectory}.");
        }

        return dir.FullName;
    }
}
