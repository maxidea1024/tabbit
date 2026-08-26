using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The edges of the `Group.Member` notation: a grouping that cannot be made into one shape,
/// and a grouping that goes further in than one level.
///
/// The first is a refusal, and it is here because the alternative is worse than a failure: a
/// sheet with a hole in a group would generate a record carrying a value nothing writes,
/// which reads as a deliberate default.
///
/// The second used to be a refusal too, on the grounds that a member could not itself be a
/// group. It is not one any more - depth is a property of the columns rather than a limit of
/// the model - so the same fixture now pins the shape it produces. spec/types/nested-multi-level.md
/// has why the wire needed nothing for it.
///
/// A third refusal used to live here - a target that did not understand a record - and it is
/// gone because all thirteen now do. The check itself stays in `ITarget`, which is what a
/// fourteenth target would meet before it had learned.
///
/// spec/types/nested-fields.md has the rules. The shapes one level deep are pinned by the `nested`
/// golden.
/// </summary>
public class NestedTargetSupportTests
{
    /// <summary>
    /// An element that does not declare every member stops the conversion.
    /// </summary>
    [Fact]
    public void An_element_missing_a_member_is_refused()
    {
        var result = TabbitRunner.Convert("nested-hole");

        Assert.False(result.Succeeded, $"Expected a refusal.\n{result.Describe()}");

        string output = result.StdOut + result.StdErr;

        Assert.Contains("Holed", output);
        Assert.Contains("Label", output);
        Assert.Contains("every member", output);
    }

    /// <summary>
    /// A member that is itself a record comes out as one, at the depth the columns wrote.
    /// </summary>
    /// <remarks>
    /// Checked against the exported JSON rather than against the model, because what is in
    /// question is the shape a consumer receives: `star[i].position.x` has to be an object
    /// inside an object inside an array, and a model assertion would pass on a tree that
    /// serialized flat.
    ///
    /// The columns are `Star1.Id` beside `Star1.Position.X`, so one level holds a value and
    /// a record at once - which is what proves the folding walks the path rather than
    /// counting levels.
    /// </remarks>
    [Fact]
    public void A_member_that_is_itself_a_record_comes_out_nested()
    {
        var result = TabbitRunner.Convert("nested-deep");

        Assert.True(result.Succeeded, $"Conversion failed.\n{result.Describe()}");

        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir("nested-deep"), "json-named", "Deep.json"));

        var rows = JsonDocument.Parse(json).RootElement.Clone();
        var stars = rows[0].GetProperty("star");

        Assert.Equal(2, stars.GetArrayLength());

        // The value beside the record, and the record's own members - the whole point being
        // that both live at the same level.
        Assert.Equal(10, stars[0].GetProperty("id").GetInt32());
        Assert.Equal(11, stars[0].GetProperty("position").GetProperty("x").GetInt32());
        Assert.Equal(12, stars[0].GetProperty("position").GetProperty("y").GetInt32());

        Assert.Equal(20, stars[1].GetProperty("id").GetInt32());
        Assert.Equal(21, stars[1].GetProperty("position").GetProperty("x").GetInt32());
        Assert.Equal(22, stars[1].GetProperty("position").GetProperty("y").GetInt32());

        // The second row, so the depth is read per row rather than once.
        var second = rows[1].GetProperty("star");
        Assert.Equal(31, second[0].GetProperty("position").GetProperty("x").GetInt32());
        Assert.Equal(42, second[1].GetProperty("position").GetProperty("y").GetInt32());
    }

    /// <summary>
    /// And the generated C# reads the same values back out of the binary.
    /// </summary>
    /// <remarks>
    /// The question neither the JSON nor a compile answers. The declaration says
    /// `Star[j].Position.X` and the file says a fixed-array column named
    /// `Deep.Star.Position.X`; whether those are the same column is settled by reading, and
    /// the writer and the reader are two halves of the tool that share no code.
    ///
    /// A hard failure rather than a skip if the toolchain is missing - a gate that quietly
    /// turns itself off is worse than no gate.
    /// </remarks>
    [Fact]
    public void The_generated_reader_reads_a_record_inside_a_record()
    {
        var conversion = TabbitRunner.Convert("nested-deep");
        Assert.True(conversion.Succeeded, $"Conversion failed.\n{conversion.Describe()}");

        var result = CsToolchain.ReadBack("nested-deep", "cs-check-nested-deep");

        Assert.True(result.Succeeded,
            $"The generated C# did not read the exported binary.\n{result.Output}");

        var rows = JsonDocument.Parse(result.StdOut.Trim()).RootElement.Clone();

        Assert.Equal(2, rows.GetArrayLength());

        var stars = rows[0].GetProperty("star");
        Assert.Equal(2, stars.GetArrayLength());

        Assert.Equal(10, stars[0].GetProperty("id").GetInt32());
        Assert.Equal(11, stars[0].GetProperty("x").GetInt32());
        Assert.Equal(12, stars[0].GetProperty("y").GetInt32());
        Assert.Equal(22, stars[1].GetProperty("y").GetInt32());

        // The second row, read from the same two columns at the next offset - which is what
        // says the element index and the member path did not get crossed.
        var second = rows[1].GetProperty("star");
        Assert.Equal(31, second[0].GetProperty("x").GetInt32());
        Assert.Equal(40, second[1].GetProperty("id").GetInt32());
        Assert.Equal(42, second[1].GetProperty("y").GetInt32());
    }
}
