using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tabbit;

/// <summary>
/// Which build of this tool is running.
/// </summary>
/// <remarks>
/// One place, because two things read it and they must agree. It is written at the top of
/// every run so a log somebody sends says which build produced it, and it is part of the
/// build cache's key - every generator's code and every embedded template is inside this
/// assembly, so a different build is a different answer to the same recipe.
///
/// Those two together are the reason this is not two expressions in two files. A run that
/// reports one version while the cache keys on another would produce the one message nobody
/// can act on: a full conversion blamed on a build that the banner says is the build being
/// run.
/// </remarks>
public static class ToolVersion
{
    /// <summary>
    /// The version, as the build stamped it.
    /// </summary>
    /// <remarks>
    /// The informational version first, because it carries the commit for a build made from
    /// one - which is what distinguishes two builds of the same released version, and those
    /// are exactly the two the cache has to tell apart.
    /// </remarks>
    public static string Current { get; } = Read();

    /// <summary>
    /// The line a run opens with: which build this is.
    /// </summary>
    public static string Banner => $"Tabbit {Current}";

    /// <summary>
    /// What this build carries, on a line of its own under <see cref="Banner"/>.
    /// </summary>
    /// <remarks>
    /// Split off rather than appended because the two answer different questions and both
    /// get read at a glance. One line holding a version, a format number, a runtime and a
    /// platform is a line nobody finds anything in.
    ///
    /// The format's version is here and not in the banner for the same reason it is here at
    /// all: it is the one number a reader can disagree with. Every generated reader carries
    /// its own copy of the format's constants, so a client refusing to load a file is a
    /// version mismatch before it is anything else - and the number the writer used is not
    /// otherwise anywhere a person can see.
    /// </remarks>
    public static string Runtime =>
        $".tcb v{Exporters.TcbFormat.Version} · {RuntimeInformation.FrameworkDescription} "
        + $"({RuntimeInformation.RuntimeIdentifier})";

    /// <summary>
    /// When this build was made, or null for a build that did not say.
    /// </summary>
    /// <remarks>
    /// Only the release workflow passes it, and that is the whole design rather than an
    /// omission. A timestamp evaluated at build time is a compile input that differs on
    /// every build, so stamping it locally would end deterministic builds and make every
    /// `dotnet build` a full recompile - in front of a test suite that already takes
    /// twenty-two minutes.
    ///
    /// Kept out of <see cref="Current"/> for a harder reason: that string is the build
    /// cache's key and the `toolVersion` written into every summary and report. A value
    /// that moved on each build would mean a full conversion every time, and a report
    /// golden that changes when nothing did.
    ///
    /// So a local build has no build time, and the absence is itself the answer: together
    /// with a version of 0.0.0 it says this binary is not a release. spec/cli-help.md §7.
    /// </remarks>
    public static string? Built { get; } = Metadata("BuildTimestamp");

    /// <summary>How much of a commit hash is kept. Enough to name one, short enough to read.</summary>
    private const int CommitLength = 12;

    /// <summary>
    /// One of the `AssemblyMetadata` values the build stamped in, or null.
    /// </summary>
    private static string? Metadata(string key)
    {
        string? value = typeof(ToolVersion).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Read()
    {
        var assembly = typeof(ToolVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return assembly.GetName().Version?.ToString() ?? "unknown";

        // The build stamps the whole commit hash after a `+`, which is forty characters of a
        // line somebody reads at a glance and of a message saying which build wrote a cache.
        // Shortened here rather than at each of those, so the two cannot disagree about what
        // this build is called.
        int plus = informational.IndexOf('+');

        if (plus < 0 || informational.Length - plus - 1 <= CommitLength)
            return informational;

        return informational[..(plus + 1 + CommitLength)];
    }
}
