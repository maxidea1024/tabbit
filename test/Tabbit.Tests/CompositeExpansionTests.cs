using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// What a composite column becomes: the record a sheet could have written by hand.
/// </summary>
/// <remarks>
/// **The comparison is the gate.** `CompositeTypeTests` checks the notation, and everything
/// below the notation rests on one claim - that `Pos: vec3f` and `Pos.X`/`Pos.Y`/`Pos.Z`
/// arrive as the same columns. Two workbooks hold the same table under the same name, one
/// written each way, and the produced files must be identical byte for byte.
///
/// A fold that did not happen fails here, and so does a notation read as the wrong number.
/// Neither would need an assertion written for it.
///
/// spec/types/composite-value-types.md section 6.
/// </remarks>
public class CompositeExpansionTests
{
    private static void Convert(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Converting `{scenario}` failed.{System.Environment.NewLine}{result.Describe()}");
    }

    /// <summary>
    /// The two ways of writing the table produce the same file.
    /// </summary>
    [Fact]
    public void A_composite_column_and_its_components_reach_the_same_file()
    {
        Convert("composite");
        Convert("composite-expanded");

        byte[] fromComposite = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("composite"), "binary", "Vectors.tcb"));

        byte[] fromComponents = File.ReadAllBytes(Path.Combine(
            RepoLayout.OutputDir("composite-expanded"), "binary", "Vectors.tcb"));

        Assert.Equal(fromComponents, fromComposite);
    }

    /// <summary>
    /// And the same JSON, which is where a difference is readable.
    /// </summary>
    /// <remarks>
    /// The byte comparison above is the stronger claim and the worse report - two files that
    /// differ say so in an offset. This one says which member of which row, so a failing run
    /// names the notation that was read wrongly.
    /// </remarks>
    [Fact]
    public void The_two_spellings_produce_the_same_json()
    {
        Convert("composite");
        Convert("composite-expanded");

        Assert.Equal(
            Json("composite-expanded"),
            Json("composite"));
    }

    private static string Json(string scenario)
        => File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(scenario), "json-named", "Vectors.json"));

    /// <summary>
    /// Each notation reaches the number it names.
    /// </summary>
    /// <remarks>
    /// The equivalence gate above would pass if both sides were wrong in the same way, which
    /// they cannot be - one side writes plain numbers - but nothing in it says what the
    /// numbers are. Reading a few out is what makes a failure legible without opening a
    /// workbook.
    /// </remarks>
    [Fact]
    public void The_notations_read_as_the_values_they_name()
    {
        Convert("composite");

        var rows = JsonDocument.Parse(Json("composite")).RootElement;

        Assert.Equal(3, rows.GetArrayLength());

        // `identity` in a quaternion column, and a hex colour in an 8-bit one.
        Assert.Equal(1, rows[0].GetProperty("rot").GetProperty("w").GetDouble());
        Assert.Equal(51, rows[0].GetProperty("tint").GetProperty("r").GetInt32());
        Assert.Equal(204, rows[0].GetProperty("tint").GetProperty("b").GetInt32());

        // A CSS name, and `one` and `zero` in vector columns.
        Assert.Equal(1, rows[1].GetProperty("pos").GetProperty("x").GetDouble());
        Assert.Equal(0, rows[1].GetProperty("cell").GetProperty("y").GetInt32());
        Assert.Equal(100, rows[1].GetProperty("tint").GetProperty("r").GetInt32());
        Assert.Equal(237, rows[1].GetProperty("tint").GetProperty("b").GetInt32());

        // `transparent`, which is the one colour keyword whose alpha is not 255.
        Assert.Equal(0, rows[1].GetProperty("glow").GetProperty("a").GetDouble());

        // The engine's spelling of a qualified literal.
        Assert.Equal(1, rows[2].GetProperty("cell").GetProperty("x").GetInt32());
        Assert.Equal(128, rows[2].GetProperty("tint").GetProperty("a").GetInt32());
    }

    /// <summary>
    /// A composite column arrives as a record, not as a name with the components glued on.
    /// </summary>
    [Fact]
    public void The_components_are_members_rather_than_columns()
    {
        Convert("composite");

        var first = JsonDocument.Parse(Json("composite")).RootElement[0];

        Assert.Equal(JsonValueKind.Object, first.GetProperty("pos").ValueKind);
        Assert.True(first.GetProperty("pos").TryGetProperty("z", out _));

        // And nothing kept the flattened spelling beside it.
        Assert.False(first.TryGetProperty("posX", out _));
        Assert.False(first.TryGetProperty("pos_x", out _));
    }
}
