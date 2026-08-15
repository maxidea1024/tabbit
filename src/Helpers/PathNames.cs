using System;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Helpers;

/// <summary>
/// How two paths are compared for being the same path.
/// </summary>
/// <remarks>
/// The answer is the filesystem's, not this tool's, and it differs by platform: NTFS and
/// APFS resolve two spellings that differ only in case to one file, and ext4 resolves them
/// to two. So the comparison has to differ too, and comparing case-insensitively everywhere
/// - which is what this used to do - is wrong on Linux in the direction that hides work.
///
/// The sweep is where it shows. A run that writes `Item.cs` records having written it, and
/// then removes every generated file under the output that it did not write. On Linux a
/// stale `item.cs` from an earlier run is a different file, but a case-insensitive lookup
/// says it was written - so it survives, still declaring types nothing generates any more.
/// That is the exact failure the sweep exists to prevent, and it would have been reported
/// as "the sweep does not work on Linux" with nothing in the code admitting to a platform.
///
/// The split follows the one the runtime makes for itself when it compares paths, so a
/// question this answers and a question `Path` answers cannot disagree.
///
/// Two spellings are still two paths on a case-insensitive Linux mount or a case-sensitive
/// APFS volume. Those exist and this will be wrong on them; the alternative is asking the
/// filesystem per path, which costs a syscall on every comparison to be right about a
/// configuration nobody here runs.
/// </remarks>
public static class PathNames
{
    /// <summary>How to compare two paths on this platform.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>The same, for the collections that need a comparer rather than an enum.</summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>Whether two paths name the same file as far as this platform is concerned.</summary>
    /// <remarks>
    /// Callers pass full paths. This compares spellings and does not resolve links or
    /// `..`, so `Path.GetFullPath` is the caller's job - as it already was.
    /// </remarks>
    public static bool Same(string left, string right)
        => string.Equals(left, right, Comparison);

    /// <summary>
    /// Paths in an order that is the same on every platform.
    /// </summary>
    /// <remarks>
    /// `Directory.GetFiles` does not promise an order and does not give the same one twice
    /// across filesystems: NTFS hands back its index, which reads as alphabetical, and ext4
    /// hands back hash order, which reads as nothing. Anything that turns a directory into
    /// output - the order workbooks are imported in, the order sources are handed to the
    /// compiler, which of two files of one name is the one a rule sees - therefore produced
    /// different output on Linux than on Windows from the same input.
    ///
    /// That failure has no gate behind it, and could not have: every fixture directory here
    /// holds exactly one workbook, so the goldens agree on every platform whatever the order
    /// is. A project with two would have found it, and would have found it as "the output
    /// differs depending on who ran the conversion" rather than as a failure.
    ///
    /// Sorted on the path with `/` separators so the comparison does not sort on the
    /// separator itself, and ordinally so a machine's culture is not in the answer either.
    /// </remarks>
    public static IEnumerable<string> InOrder(IEnumerable<string> paths)
        => paths.OrderBy(path => path.Replace('\\', '/'), StringComparer.Ordinal);
}
