using System;
using System.IO;
using Tabbit;
using Tabbit.Helpers;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What happens when two generated types want the same file.
///
/// It became possible when the targets started writing a file per table: a file name now comes
/// from a table, an enum or a constant set name, and two of those can reduce to one file name.
/// `Item` the table and `Item` the enum in a target that lower-cases; `ItemType` and
/// `Item_Type` in one that snake-cases.
///
/// The old behaviour was that whichever ran last was the file and the other type was simply
/// not in the output. Which the consumer finds out about from their own compiler, naming a
/// type this tool reported generating, with nothing anywhere saying why.
/// </summary>
public class StagingCollisionTests
{
    [Fact]
    public void Two_different_files_for_one_path_is_an_error()
    {
        string path = Unique();

        StagingFiles.WriteAllTextToFile(path, "one");

        var thrown = Assert.Throws<TabbitException>(
            () => StagingFiles.WriteAllTextToFile(path, "two"));

        Assert.Contains(path, thrown.Message);
        Assert.Equal(Tabbit.Exporters.ExportMessages.GeneratedFileNameClash, thrown.MessageId);
    }

    /// <summary>
    /// Writing the same text twice is allowed, because a run legitimately does it: two targets
    /// sharing an output directory each write the reader runtime, and neither knows about the
    /// other.
    /// </summary>
    [Fact]
    public void The_same_file_twice_is_not()
    {
        string path = Unique();

        StagingFiles.WriteAllTextToFile(path, "same");
        StagingFiles.WriteAllTextToFile(path, "same");
    }

    /// <summary>
    /// A path nothing has staged, staged under a directory of its own so the shared static
    /// registry cannot make one test's writes another's.
    /// </summary>
    private static string Unique()
        => Path.Combine(Path.GetTempPath(), "tabbit-collision", Guid.NewGuid().ToString("N"), "x.txt");
}
