using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A record group whose rows are each one of an abstract type's variants.
/// </summary>
/// <remarks>
/// **What has to be true here is that the format did not move.** The discriminator is an
/// integer column, the abstract type's own field is an ordinary one, and a variant's member is
/// an optional column - three shapes the encoder already wrote. So this reads the produced
/// files and asserts what is in them rather than asserting on any new wire concept, because
/// there is no new wire concept to assert on. spec/polymorphism.md section 6.
/// </remarks>
public class PolymorphicRecordTests
{
    private const string Scenario = "polymorphism";

    private static JsonElement[] Rows()
    {
        var result = TabbitRunner.Convert(Scenario);

        Assert.True(result.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{result.Describe()}");

        string json = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "json-named", "Skill.json"));

        return JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();
    }

    /// <summary>
    /// The `$type` cell arrives as the variant's number, not as its name.
    /// </summary>
    /// <remarks>
    /// The numbers are the `@N` the declaration wrote - 1, 2, 3 - and nothing in the file
    /// carries the variant's name. That is what lets a variant be renamed without touching a
    /// deployed reader. spec/polymorphism.md section 5.1.1.
    /// </remarks>
    [Fact]
    public void The_discriminator_carries_the_variants_number()
    {
        var byName = Rows().ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("effect").GetProperty("type").GetInt32());

        Assert.Equal(1, byName["Slash"]);
        Assert.Equal(1, byName["Cleave"]);
        Assert.Equal(2, byName["Mend"]);
        Assert.Equal(3, byName["Feint"]);
    }

    /// <summary>
    /// The abstract type's own field is on every row, whatever the variant.
    /// </summary>
    /// <remarks>
    /// One column rather than one per variant, which is what section 5.1 decided and the
    /// reason it decided it: copying the base fields into every variant would multiply the
    /// columns by the number of variants and fill one of them per row.
    /// </remarks>
    [Fact]
    public void The_base_field_is_on_every_row()
    {
        var byName = Rows().ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("effect").GetProperty("chance").GetInt32());

        Assert.Equal(30, byName["Slash"]);
        Assert.Equal(100, byName["Mend"]);

        // Including the variant that declares nothing of its own. Its rows carry the
        // discriminator and the base field and that is all.
        Assert.Equal(10, byName["Feint"]);
    }

    /// <summary>
    /// A variant's member holds its value on that variant's rows.
    /// </summary>
    [Fact]
    public void A_variant_member_reads_on_its_own_rows()
    {
        var byName = Rows().ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("effect"));

        Assert.Equal(50, byName["Slash"].GetProperty("damage").GetInt32());
        Assert.True(byName["Slash"].GetProperty("pierces").GetBoolean());
        Assert.False(byName["Cleave"].GetProperty("pierces").GetBoolean());
        Assert.Equal(20, byName["Mend"].GetProperty("amount").GetInt32());
    }

    /// <summary>
    /// And the rows come out in discriminator order, whatever order the sheet wrote them.
    /// </summary>
    /// <remarks>
    /// **The fixture is deliberately written out of order** - 1, 2, 3, 4, 5 with the variants
    /// interleaved - so this asserts the sorting happened rather than that the sheet happened
    /// to be sorted. Stable, which is what the second half of the check says: `Slash` before
    /// `Cleave` and `Mend` before `Mend2` is the author's order kept inside each variant.
    ///
    /// In the cooking rather than in an exporter, so the JSON and the binary agree row for
    /// row. spec/polymorphism.md section 6.3.
    /// </remarks>
    [Fact]
    public void The_rows_come_out_in_discriminator_order()
    {
        string[] order = Rows()
            .Select(row => row.GetProperty("name").GetString()!)
            .ToArray();

        Assert.Equal(["Slash", "Cleave", "Mend", "Mend2", "Feint"], order);
    }

    /// <summary>
    /// The generated C# compiles, and `is` narrows to the variant each row is.
    /// </summary>
    /// <remarks>
    /// **The compile is most of this gate.** Variant types only mean something if pattern
    /// matching reaches them, and that is a claim about generated code which has to be built
    /// to be tested - a generator emitting the union flat would produce something this harness
    /// cannot compile against.
    ///
    /// The read adds what a compile cannot: that the discriminator picked the right variant
    /// per row, and that a member of another variant is not on the object at all.
    /// spec/polymorphism.md section 7.
    /// </remarks>
    [Fact]
    public void The_generated_csharp_narrows_to_each_rows_variant()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.ReadBack(Scenario, "cs-check-polymorphism");

        Assert.True(result.Succeeded,
            $"Reading `{Scenario}` back through the generated C# failed."
            + $"{System.Environment.NewLine}{result.Output}");

        var rows = JsonDocument.Parse(result.Output).RootElement
            .GetProperty("Skill").EnumerateArray().ToArray();

        var kind = rows.ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("kind").GetString());

        Assert.Equal("DamageEffect", kind["Slash"]);
        Assert.Equal("HealEffect", kind["Mend"]);
        Assert.Equal("NoEffect", kind["Feint"]);

        var own = rows.ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("own").GetString());

        Assert.Equal("damage=50,pierces=True", own["Slash"]);
        Assert.Equal("damage=70,pierces=False", own["Cleave"]);
        Assert.Equal("amount=20", own["Mend"]);
        Assert.Equal("none", own["Feint"]);
    }

    /// <summary>
    /// The generated TypeScript type-checks, and `kind` narrows to each row's variant.
    /// </summary>
    /// <remarks>
    /// **The type check is most of this gate.** A discriminated union only means something if
    /// narrowing reaches each variant's own members, and the compiler is what settles that - a
    /// generator emitting the union flat would produce something the harness cannot check
    /// against. spec/polymorphism.md section 7.
    /// </remarks>
    [Fact]
    public void The_generated_typescript_narrows_to_each_rows_variant()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        var result = TypescriptRoundTrip.Run(Scenario, driver: "ts-check-polymorphism");

        Assert.True(result.Succeeded,
            $"Reading `{Scenario}` back through the generated TypeScript failed."
            + $"{System.Environment.NewLine}{result.Output}");

        var rows = JsonDocument.Parse(result.Output).RootElement
            .GetProperty("Skill").EnumerateArray().ToArray();

        var kind = rows.ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("kind").GetString());

        Assert.Equal("DamageEffect", kind["Slash"]);
        Assert.Equal("HealEffect", kind["Mend"]);
        Assert.Equal("NoEffect", kind["Feint"]);

        var own = rows.ToDictionary(
            row => row.GetProperty("name").GetString()!,
            row => row.GetProperty("own").GetString());

        Assert.Equal("damage=50,pierces=true", own["Slash"]);
        Assert.Equal("amount=20", own["Mend"]);
        Assert.Equal("none", own["Feint"]);
    }
}
