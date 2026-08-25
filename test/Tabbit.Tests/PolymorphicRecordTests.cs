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

    /// <summary>
    /// The generated Go compiles: a sealed interface and one struct per variant.
    /// </summary>
    /// <remarks>
    /// **Compiling is the whole of this one.** There is no inheritance in this language, so the
    /// abstract type is an interface sealed by an unexported method and every variant embeds the
    /// base struct - and all of that is a claim the compiler settles. A variant that did not
    /// satisfy the interface, or a base field the embedding did not promote, fails here.
    /// spec/polymorphism.md section 7.
    /// </remarks>
    /// <summary>
    /// The generated code of every language that has to declare the variants compiles.
    /// </summary>
    /// <remarks>
    /// **Compiling is the whole of these.** Whichever shape a language takes - classes and
    /// `instanceof`, a sealed interface, a sum type - it is a claim about generated code that
    /// only a compiler settles: a variant that does not belong to the set, or a base field the
    /// inheritance does not carry, fails here and nowhere else. spec/polymorphism.md section 7.
    /// </remarks>
    [Theory]
    [InlineData("Go")]
    [InlineData("Java")]
    [InlineData("Kotlin")]
    [InlineData("Swift")]
    [InlineData("Dart")]
    [InlineData("Rust")]
    [InlineData("C")]
    [InlineData("Cpp")]
    public void The_generated_code_compiles(string language)
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        var (available, compile) = Toolchain(language);

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(available(out string why),
            $"A {language} toolchain is required to check the generated code. {why}");

        var result = compile(Scenario);

        Assert.True(result.Succeeded,
            $"Generated {language} for a polymorphic group does not compile."
            + $"{System.Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// The generated PHP reads each row back as the variant it is.
    /// </summary>
    /// <remarks>
    /// **A dynamic language, so parsing settles almost nothing here** - `instanceof` against a
    /// class that was never declared is `false` rather than an error. That is why this one reads
    /// the file and names the class per row: a build that emitted the union flat would report
    /// every row as the base type and nothing else would say so.
    /// spec/polymorphism.md section 7.
    /// </remarks>
    [Fact]
    public void The_generated_php_reads_each_rows_variant()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to check the generated code. {why}");

        var linted = ConformanceHarness.CompilePhp(Scenario, "PolymorphismAccessor");

        Assert.True(linted.Succeeded,
            $"The generated PHP does not parse.{System.Environment.NewLine}{linted.Output}");

        string binaryDir = System.IO.Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

        // zend.assertions=1 makes `assert` evaluate; the default for a non-development ini is
        // to compile it away, which would make this pass by not running.
        var result = ConformanceHarness.RunPhpSnippet(
            Scenario,
            "require_once __DIR__ . '/PolymorphismAccessor.php'; "
            + "$accessor = new \\Tabbit\\Fixtures\\Polymorphism\\PolymorphismAccessor(); "
            + "$accessor->readAll($argv[1]); "
            + "$rows = $accessor->skill->records; "
            + "$named = []; foreach ($rows as $r) { $named[$r->name] = $r; } "

            // The class per row, which is what the discriminator picked. A build that emitted
            // the union flat would report every row as the base type.
            + "$of = static function ($row) { "
            + "    return (new \\ReflectionClass($row->effectOf()))->getShortName(); "
            + "}; "
            + "assert($of($named['Slash']) === 'DamageEffect'); "
            + "assert($of($named['Mend']) === 'HealEffect'); "
            + "assert($of($named['Feint']) === 'NoEffect'); "

            // The base field, read through the abstract type.
            + "assert($named['Slash']->effectOf()->chance === 30); "
            + "assert($named['Feint']->effectOf()->chance === 10); "

            // A member only one variant has, and the other variant not having it.
            + "assert($named['Slash']->effectOf()->damage === 50); "
            + "assert($named['Slash']->effectOf()->pierces === true); "
            + "assert($named['Mend']->effectOf()->amount === 20); "
            + "assert(!property_exists($named['Mend']->effectOf(), 'damage'));",
            binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{Scenario}` back through the generated PHP failed."
            + $"{System.Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// The generated Python reads each row back as the variant it is.
    /// </summary>
    /// <remarks>
    /// Another dynamic language, so this reads the file rather than compiling: the class per row
    /// is what the discriminator picked, and a build that emitted the union flat would report
    /// every row as the base type. spec/polymorphism.md section 7.
    /// </remarks>
    [Fact]
    public void The_generated_python_reads_each_rows_variant()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated code. {why}");

        var compiled = ConformanceHarness.CompilePython(Scenario);

        Assert.True(compiled.Succeeded,
            $"The generated Python does not compile.{System.Environment.NewLine}{compiled.Output}");

        string binaryDir = System.IO.Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

        var result = ConformanceHarness.RunPythonSnippet(
            Scenario,
            "import sys\n"
            + "from gamedata import Tables\n"
            + "from gamedata.struct_effect import Effect\n"
            + @"
t = Tables()
t.read_all(sys.argv[1])
rows = {r.name: r for r in t.skill.records}

# The class per row, which is what the discriminator picked.
assert type(rows['Slash'].effect_of()).__name__ == 'DamageEffect'
assert type(rows['Mend'].effect_of()).__name__ == 'HealEffect'
assert type(rows['Feint'].effect_of()).__name__ == 'NoEffect'

# Every variant is the base type, which is what makes the shared field reachable.
assert isinstance(rows['Slash'].effect_of(), Effect)
assert rows['Slash'].effect_of().chance == 30
assert rows['Feint'].effect_of().chance == 10

# A member only one variant has, and the other variant not having it at all.
assert rows['Slash'].effect_of().damage == 50
assert rows['Slash'].effect_of().pierces is True
assert rows['Mend'].effect_of().amount == 20
assert not hasattr(rows['Mend'].effect_of(), 'damage')

# Built once: the second call hands back the same object.
assert rows['Slash'].effect_of() is rows['Slash'].effect_of()
",
            binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{Scenario}` back through the generated Python failed."
            + $"{System.Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// The generated Ruby reads each row back as the variant it is.
    /// </summary>
    /// <remarks>
    /// A dynamic language, so this reads the file: the class per row is what the discriminator
    /// picked, and a build that emitted the union flat would report every row as the base type.
    /// spec/polymorphism.md section 7.
    /// </remarks>
    [Fact]
    public void The_generated_ruby_reads_each_rows_variant()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"A Ruby interpreter is required to check the generated code. {why}");

        var parsed = ConformanceHarness.CompileRuby(Scenario);

        Assert.True(parsed.Succeeded,
            $"The generated Ruby does not parse.{System.Environment.NewLine}{parsed.Output}");

        string binaryDir = System.IO.Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

        var result = ConformanceHarness.RunRubySnippet(
            Scenario,
            "require_relative 'tables'\n"
            + "accessor = GameData::Tables.new\n"
            + "accessor.read_all(ARGV[0])\n"
            + "rows = accessor.skill.records.to_h { |r| [r.name, r] }\n"
            // The class per row, which is what the discriminator picked.
            + "raise unless rows['Slash'].effect_of.class.name.end_with?('DamageEffect')\n"
            + "raise unless rows['Mend'].effect_of.class.name.end_with?('HealEffect')\n"
            + "raise unless rows['Feint'].effect_of.class.name.end_with?('NoEffect')\n"
            // Every variant is the base type, which is what makes the shared field reachable.
            + "raise unless rows['Slash'].effect_of.is_a?(GameData::Effect)\n"
            + "raise unless rows['Slash'].effect_of.chance == 30\n"
            + "raise unless rows['Feint'].effect_of.chance == 10\n"
            // A member only one variant has, and the other not having it at all.
            + "raise unless rows['Slash'].effect_of.damage == 50\n"
            + "raise unless rows['Slash'].effect_of.pierces == true\n"
            + "raise unless rows['Mend'].effect_of.amount == 20\n"
            + "raise if rows['Mend'].effect_of.respond_to?(:damage)\n"
            // Built once: the second call hands back the same object.
            + "raise unless rows['Slash'].effect_of.equal?(rows['Slash'].effect_of)\n",
            binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{Scenario}` back through the generated Ruby failed."
            + $"{System.Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// The generated Lua reads each row back as the variant it is, and refuses a typo.
    /// </summary>
    /// <remarks>
    /// **The language with the least to check and the most to lose.** There is no compile step
    /// and a misspelled member is a nil that compares false with everything, so the variants get
    /// strict metatables like every other generated table here - and the last two assertions are
    /// what say those are on. spec/polymorphism.md section 7 and spec/lua-language-support.md.
    /// </remarks>
    [Fact]
    public void The_generated_lua_reads_each_rows_variant()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Converting `{Scenario}` failed.{System.Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.LuaIsAvailable(out string why),
            $"A C toolchain is required to build the Lua host. {why}");

        string binaryDir = System.IO.Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

        var result = ConformanceHarness.RunLuaSnippet(Scenario, @"
local t = require('tables').new()
t:readAll(arg[1])

local rows = {}
for _, r in ipairs(t.skill.records) do rows[r.name] = r end

local skill = require('tables.skill_table')

-- The variant, named by its own `kind` - which is what the discriminator picked.
assert(skill.effectOf(rows['Slash']).kind == 'DamageEffect')
assert(skill.effectOf(rows['Mend']).kind == 'HealEffect')
assert(skill.effectOf(rows['Feint']).kind == 'NoEffect')

-- The base field, on every variant.
assert(skill.effectOf(rows['Slash']).chance == 30)
assert(skill.effectOf(rows['Feint']).chance == 10)

-- A member only one variant has.
assert(skill.effectOf(rows['Slash']).damage == 50)
assert(skill.effectOf(rows['Slash']).pierces == true)
assert(skill.effectOf(rows['Mend']).amount == 20)

-- And the strict metatable: a member of another variant is an error to read, not a nil.
local ok, err = pcall(function() return skill.effectOf(rows['Mend']).damage end)
assert(not ok and tostring(err):find('no field'), tostring(err))

-- A misspelling too, which is the same guard the rows themselves get.
ok, err = pcall(function() return skill.effectOf(rows['Slash']).chnace end)
assert(not ok and tostring(err):find('no field'), tostring(err))
", binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{Scenario}` back through the generated Lua failed."
            + $"{System.Environment.NewLine}{result.Output}");
    }

    /// <summary>Whether a toolchain is on this machine, and why not when it is missing.</summary>
    private delegate bool Availability(out string reason);

    /// <summary>How to ask for one language's toolchain and how to run it.</summary>
    private static (Availability Available, System.Func<string, ToolResult> Compile)
        Toolchain(string language)
        => language switch
        {
            "Go" => (ConformanceHarness.GoIsAvailable, ConformanceHarness.CompileGo),
            "Java" => (ConformanceHarness.JavaIsAvailable, ConformanceHarness.CompileJava),
            "Kotlin" => (ConformanceHarness.KotlinIsAvailable, ConformanceHarness.CompileKotlin),
            "Swift" => (ConformanceHarness.SwiftIsAvailable, ConformanceHarness.CompileSwift),
            "Dart" => (ConformanceHarness.DartIsAvailable, ConformanceHarness.CompileDart),
            "Rust" => (ConformanceHarness.RustIsAvailable, ConformanceHarness.CompileRust),
            // This one takes the accessor name too: every C identifier carries it as a prefix,
            // so the harness has to be told which one the recipe set.
            // Both of these take the accessor name too: their identifiers carry it.
            "Cpp" => (CppToolchain.IsAvailable,
                      scenario => CppToolchain.Compile(scenario, "PolyAccessor")),
            "C" => (ConformanceHarness.CIsAvailable,
                    scenario => ConformanceHarness.CompileC(scenario, "PolyData")),
            _ => throw new System.ArgumentOutOfRangeException(nameof(language), language, null),
        };

}
