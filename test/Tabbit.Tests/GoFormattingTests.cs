using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The generated Go has to be what `gofmt` would have written.
/// </summary>
/// <remarks>
/// Go is the one target whose formatting is settled rather than preferred. `gofmt` takes no
/// options, editors run it on save, and repositories check it in CI - so a generated file
/// that is one space short of aligned is a file the consumer's tooling rewrites, in a tree
/// whose every header says not to edit it. It stayed that way for a long time because
/// nothing here looked: `go build` accepts the output either way, and that was the only
/// question being asked. In one sample it was 93 files out of 93.
///
/// Compiling is checked elsewhere. This asks the other question, and asks it of the output
/// tree rather than a copy, because the copy is not what anyone receives.
/// </remarks>
public class GoFormattingTests
{
    [Theory]
    [InlineData("composite-key")]
    [InlineData("containers-target")]
    [InlineData("key-types")]
    [InlineData("member-array")]
    [InlineData("nested")]
    [InlineData("nested-deep")]
    [InlineData("nullable-elements")]
    [InlineData("optional")]
    [InlineData("polymorphism")]
    [InlineData("record-ref")]
    [InlineData("record-trim")]
    [InlineData("serial-ref")]
    public void Generated_Go_is_already_formatted(string scenario)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.GoIsAvailable(out string why),
            $"A Go toolchain is required to check the generated code. {why}");

        string module = Path.Combine(RepoLayout.OutputDir(scenario), "go");

        Assert.True(Directory.Exists(module),
            $"`{scenario}` was expected to emit Go into `{module}`, and did not.");

        // `-l` names the files that differ rather than rewriting them, so this reads the
        // output tree without touching it.
        var result = ConformanceHarness.Execute("gofmt", module, "-l", ".");

        Assert.True(result.Succeeded,
            $"`gofmt` could not read the generated Go for `{scenario}`."
            + $"{Environment.NewLine}{result.Output}");

        string[] generated = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => Path.Combine(module, line))
            .Where(WrittenByTabbit)
            .ToArray();

        Assert.True(generated.Length == 0,
            $"`gofmt` would rewrite the generated Go for `{scenario}`. "
            + "The generator has to emit what it would have written - run `gofmt -d` in "
            + $"`{module}` to see what differs.{Environment.NewLine}"
            + string.Join(Environment.NewLine, generated));
    }

    /// <summary>
    /// Whether this tool wrote the file, rather than a test driver put it there.
    /// </summary>
    /// <remarks>
    /// Asked because the harnesses copy their driver into the module - `go run ./harness`
    /// has to be inside the module for the generated package to be importable at all - and
    /// a driver is somebody's hand-written source that this gate has no business judging.
    /// The marker separates the two without a list of filenames to keep current.
    /// </remarks>
    private static bool WrittenByTabbit(string path)
    {
        using var stream = File.OpenRead(path);

        var head = new byte[GeneratedFileMarker.Window];
        int read = stream.Read(head, 0, head.Length);

        return read > 0
            && GeneratedFileMarker.IsMarked(System.Text.Encoding.UTF8.GetString(head, 0, read));
    }
}
