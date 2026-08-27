using System;
using System.Diagnostics;
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


    /// <summary>
    /// A build directory that belongs to the test class that asked for it.
    /// </summary>
    /// <remarks>
    /// **The build directories were keyed by scenario, and a scenario is not one class's.**
    /// A dozen classes compile the `core` output with different harnesses beside it, and each
    /// of them cleared and rebuilt `_cscheck/core`. Serial that is a reused folder; in
    /// parallel it is one class deleting the tree another is compiling into, which is most of
    /// what failed the first time the suite ran in parallel.
    ///
    /// **The class rather than the test**, because a class's own tests do not run at once -
    /// xUnit's unit of parallelism is the collection and a class is one by default. Naming
    /// the test as well would only make the paths longer, and these are Windows paths with a
    /// compiler's intermediate files under them.
    ///
    /// Taken off the stack rather than from a parameter: the harnesses that call the
    /// toolchains sit between them and the test, and one of the entry points ends in a
    /// `params` array, which cannot have a caller argument after it.
    ///
    /// doc/roadmap.md, the suite-parallelism entry.
    /// </remarks>
    public static string WorkDir(string bucket, string name)
        => Path.Combine(OutputDir(bucket), name + "-" + CallingTestClass());

    /// <summary>
    /// The nearest test class below this call, or a shared name if there is none.
    /// </summary>
    private static string CallingTestClass()
    {
        foreach (var frame in new StackTrace().GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;

            if (type != null
                && type.Namespace == "Tabbit.Tests"
                && type.Name.EndsWith("Tests", StringComparison.Ordinal))
            {
                return type.Name;
            }
        }

        return "shared";
    }

    /// <summary>Where a scenario's build cache goes.</summary>
    /// <remarks>
    /// Beside the output tree rather than inside it. The golden comparison walks the whole
    /// output tree and reports anything it does not recognise, so a cache file in there
    /// would fail every golden test as a new artifact.
    ///
    /// And not the default location either, which is `.tabbit/` under the working directory -
    /// that is the repository root here, so the suite would leave its cache in the checkout.
    /// </remarks>
    public static string CacheDir(string scenario)
        => Path.Combine(Root, "test", "fixtures", "output", "_cache", scenario);

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
