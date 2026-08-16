using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace Mabbit.Tests;

/// <summary>
/// Mabbit as a version control merge driver, run by a real git against a real repository.
///
/// Everything else in this suite checks a decision. This checks the wiring: that git hands
/// the three sides over under the names it uses, that the result goes back where git expects
/// it, and that the exit code means what git takes it to mean. None of those can be got right
/// by reading the documentation - the temporary files have no extension, the result path is
/// the same path as one of the inputs, and a wrong exit code turns a clean merge into a
/// conflict or, worse, the other way round.
/// </summary>
public class MabbitDriverTests : IDisposable
{
    private const string Fixture = "fixtures/workbook.xlsx";

    private readonly string _repository = Path.Combine(
        Path.GetTempPath(), "mabbit-repo-" + Path.GetRandomFileName());

    private static string? GitPath =>
        Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, OperatingSystem.IsWindows() ? "git.exe" : "git"))
            .FirstOrDefault(File.Exists);

    /// <summary>
    /// The program under test, beside this assembly rather than at a written-down path.
    /// </summary>
    /// <remarks>
    /// Worked out from where this assembly is, so the gate runs under whatever configuration
    /// built it. A path with `Debug` in it passes locally and fails the moment CI builds
    /// Release - by declining to run, which is worse than failing.
    /// </remarks>
    private static string Mabbit
    {
        get
        {
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            string configuration = framework.Parent!.Name;
            string program = framework.Parent!.Parent!.Parent!.Parent!.FullName;

            return Path.Combine(program, "src", "bin", configuration, framework.Name,
                OperatingSystem.IsWindows() ? "mabbit.exe" : "mabbit");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_repository))
        {
            // Git leaves its object files read only, which stops a plain recursive delete.
            foreach (string file in Directory.EnumerateFiles(_repository, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);

            Directory.Delete(_repository, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private (int Code, string Output) Git(params string[] arguments)
    {
        var start = new ProcessStartInfo(GitPath!)
        {
            WorkingDirectory = _repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    private void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    private string Workbook => Path.Combine(_repository, "data", "Items.xlsx");

    /// <summary>A repository holding the fixture, with mabbit registered as its merge driver.</summary>
    private bool Prepare()
    {
        if (GitPath is null || !File.Exists(Mabbit))
            return false;

        Directory.CreateDirectory(Path.Combine(_repository, "data"));

        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "gate@example.invalid");
        Git("config", "user.name", "Gate");
        Git("config", "merge.mabbit.name", "Mabbit workbook merge");
        Git("config", "merge.mabbit.driver",
            $"\"{Mabbit}\" --merge --base %O --mine %A --theirs %B --result %A --path %P");

        File.WriteAllText(Path.Combine(_repository, ".gitattributes"), "*.xlsx merge=mabbit\n");
        File.Copy(Fixture, Workbook);

        Commit("the workbook");

        return true;
    }

    /// <summary>Writes one cell of the tracked workbook, as a person editing it would.</summary>
    private void Edit(int row, int column, string value)
    {
        var sheet = WorkbookGrid.Read(Workbook).Sheets.First(s => !s.IsEmpty);

        XlsxPatcher.Apply(Workbook, Workbook, [new CellEdit(sheet.Name, row, column, value)]);
    }

    private (int Row, int Column) TwoRows()
    {
        var schema = new HeuristicSchema();
        var view = TableViews.Of(WorkbookGrid.Read(Workbook), schema)
            .First(t => t.Rows.Count > 1 && t.Columns.Count > 1);

        return (view.Rows[0].RowIndex, view.Region.FirstColumn + 1);
    }

    [Fact]
    public void GitMergesTwoPeopleEditingDifferentRowsOfOneWorkbook()
    {
        if (!Prepare())
            return;

        var schema = new HeuristicSchema();
        var view = TableViews.Of(WorkbookGrid.Read(Workbook), schema)
            .First(t => t.Rows.Count > 1 && t.Columns.Count > 1);

        int column = view.Region.FirstColumn + 1;
        int first = view.Rows[0].RowIndex;
        int second = view.Rows[1].RowIndex;

        Git("switch", "-q", "-c", "theirs");
        Edit(second, column, "THEIRS");
        Commit("their row");

        Git("switch", "-q", "main");
        Edit(first, column, "MINE");
        Commit("my row");

        var (code, output) = Git("merge", "theirs", "-m", "merged");

        Assert.True(code == 0, output);

        // Both edits are in the file git left behind, and it is a workbook rather than a
        // file with conflict markers in it.
        var merged = WorkbookGrid.Read(Workbook).Sheets.First(s => !s.IsEmpty);

        Assert.Equal("MINE", merged.Cell(first, column));
        Assert.Equal("THEIRS", merged.Cell(second, column));
    }

    [Fact]
    public void GitReportsAConflictWhenBothSidesChangedTheSameCell()
    {
        if (!Prepare())
            return;

        var (row, column) = TwoRows();

        Git("switch", "-q", "-c", "theirs");
        Edit(row, column, "THEIRS");
        Commit("their edit");

        Git("switch", "-q", "main");
        Edit(row, column, "MINE");
        Commit("my edit");

        var (code, _) = Git("merge", "theirs", "-m", "merged");

        // Non-zero from the driver is what git reads as "this one needs a person".
        Assert.NotEqual(0, code);

        var (_, status) = Git("status", "--porcelain");
        Assert.Contains("data/Items.xlsx", status);

        // And this side's file is left as it was, rather than half merged.
        var left = WorkbookGrid.Read(Workbook).Sheets.First(s => !s.IsEmpty);
        Assert.Equal("MINE", left.Cell(row, column));
    }

    [Fact]
    public void TheDriverReadsTheTemporaryFilesGitHandsItWhichHaveNoExtension()
    {
        if (!Prepare())
            return;

        // Git names the ancestor and the other side `.merge_file_XXXXXX`. Without `--path`
        // naming the tracked file, nothing in those names says what format they are - and
        // the merge above could not have run at all.
        var temporary = Path.Combine(Path.GetTempPath(), "merge_file_" + Path.GetRandomFileName());

        try
        {
            File.Copy(Fixture, temporary);

            Assert.Throws<MabbitException>(() => WorkbookGrid.Read(temporary));
            Assert.NotEmpty(WorkbookGrid.Read(temporary, formatFrom: "data/Items.xlsx").Sheets);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    [Fact]
    public void AWorkbookCanBeWrittenOverItself()
    {
        // What a merge driver is told to do: the result path is the same path as this side.
        string copy = Path.Combine(Path.GetTempPath(), "mabbit-" + Path.GetRandomFileName() + ".xlsx");

        try
        {
            File.Copy(Fixture, copy);

            var sheet = WorkbookGrid.Read(copy).Sheets.First(s => !s.IsEmpty);

            XlsxPatcher.Apply(copy, copy,
                [new CellEdit(sheet.Name, sheet.FirstRow + 1, sheet.FirstColumn, "IN PLACE")]);

            Assert.Equal("IN PLACE",
                WorkbookGrid.Read(copy).Sheets.First(s => !s.IsEmpty)
                    .Cell(sheet.FirstRow + 1, sheet.FirstColumn));
        }
        finally
        {
            File.Delete(copy);
        }
    }
}
