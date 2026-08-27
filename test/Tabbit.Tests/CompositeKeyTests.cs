using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That a key made of several columns generates a lookup every language can build and run.
/// </summary>
/// <remarks>
/// The `composite-key` golden records what each generator emits. What a golden cannot answer
/// is whether the page it holds is a program - and this feature adds, per language, a private
/// map keyed by something the language has to accept, a function that builds that key, and a
/// call site passing several arguments where there was one. Each of those is a place a
/// language says no at compile time and a text comparison says nothing.
///
/// It also cannot answer the question the fixture exists for. `Route` holds `("a b", "c")`
/// beside `("a", "b c")`: joined by a separator alone those make one string, so one of the two
/// rows is lost and the other answers for both. The generated text is identical either way -
/// what differs is what `key_of_...` writes - so only running it says which happened. The
/// languages that can be run from here do run it.
///
/// spec/layout/primary-layout.md section 3.5.
/// </remarks>
public class CompositeKeyTests
{
    private const string Scenario = "composite-key";

    // ------------------------------------------------------------------ run

    /// <summary>The C# read back, which is where the two colliding pairs are told apart.</summary>
    [Fact]
    public void Generated_cs_reads_both_halves_of_a_key()
    {
        Converted();

        var result = CsToolchain.ReadBack(Scenario, "cs-check-composite-key");

        Assert.True(result.Succeeded,
            $"The C# harness failed.{Environment.NewLine}{result.Output}");

        // Two rows whose columns join to the same text under a naive separator. Both have to
        // come back, and each has to come back as itself.
        Assert.Contains("\"spaced\": \"R1\"", result.Output);
        Assert.Contains("\"shifted\": \"R2\"", result.Output);

        // A combination neither row holds, though both of its halves appear in the table.
        Assert.Contains("\"absent\": \"\\u003Cnone\\u003E\"", result.Output);

        // The single-column secondary key beside the composite primary one.
        Assert.Contains("\"secondary\": \"north-\\u003Esouth\"", result.Output);

        Assert.Contains("\"loadout\": \"field boots\"", result.Output);
        Assert.Contains("\"grid\": \"above origin\"", result.Output);
        Assert.Contains("\"containsGrid\": true", result.Output);
        Assert.Contains("\"containsAbsent\": false", result.Output);

        // A key made of two references. The harness calling it at all is most of this: a
        // lookup typed with the targets' rows does not compile against these arguments.
        Assert.Contains("\"link\": \"20\"", result.Output);
        Assert.Contains("\"linkAbsent\": true", result.Output);
        Assert.Contains("\"linkRow\": \"Deer/Charge\"", result.Output);
    }

    /// <summary>Python, read back through the generated lookups.</summary>
    [Fact]
    public void Generated_python_reads_both_halves_of_a_key()
        => AssertPythonReads(@"
from composite_key_data import Slot
t = Tables()
t.read_all(sys.argv[1])
assert t.route.find_by_from_and_to('a b', 'c').code == 'R1'
assert t.route.find_by_from_and_to('a', 'b c').code == 'R2'
assert t.route.find_by_from_and_to('a', 'c') is None
assert t.route.find_by_code('R3').to == 'south'
assert t.loadout.find_by_stage_and_slot(2, Slot.feet).label == 'field boots'
assert t.grid.find_by_x_and_y_and_z(0, 0, 'roof').name == 'above origin'
assert t.grid.contains_x_and_y_and_z(1, 0, 'floor')
assert not t.grid.contains_x_and_y_and_z(9, 9, 'floor')

# The link table, whose key is two references. Each parameter is the target's key - a
# string for one and a number for the other - and not the target's row.
assert t.beast_move.find_by_beast_id_and_move_id('deer', 2).power == 20
assert t.beast_move.find_by_beast_id_and_move_id('wolf', 1).power == 30
assert t.beast_move.find_by_beast_id_and_move_id('wolf', 2) is None
assert t.beast_move.find_by_beast_id_and_move_id('deer', 1).beast_by_beast_id.name == 'Deer'
");

    /// <summary>Ruby, the same.</summary>
    [Fact]
    public void Generated_ruby_reads_both_halves_of_a_key()
        => AssertRubyReads(@"
raise 'spaced' unless accessor.route.find_by_from_and_to('a b', 'c').code == 'R1'
raise 'shifted' unless accessor.route.find_by_from_and_to('a', 'b c').code == 'R2'
raise 'absent' unless accessor.route.find_by_from_and_to('a', 'c').nil?
raise 'secondary' unless accessor.route.find_by_code('R3').to == 'south'
raise 'loadout' unless accessor.loadout.find_by_stage_and_slot(2, CompositeKey::Slot::FEET).label == 'field boots'
raise 'grid' unless accessor.grid.find_by_x_and_y_and_z(0, 0, 'roof').name == 'above origin'
raise 'contains' unless accessor.grid.contains_x_and_y_and_z?(1, 0, 'floor')
raise 'absent grid' if accessor.grid.contains_x_and_y_and_z?(9, 9, 'floor')
raise 'link' unless accessor.beast_move.find_by_beast_id_and_move_id('deer', 2).power == 20
raise 'link 2' unless accessor.beast_move.find_by_beast_id_and_move_id('wolf', 1).power == 30
raise 'link absent' unless accessor.beast_move.find_by_beast_id_and_move_id('wolf', 2).nil?
raise 'link row' unless accessor.beast_move.find_by_beast_id_and_move_id('deer', 1).beast_by_beast_id.name == 'Deer'
");

    /// <summary>
    /// PHP, whose enum is an object rather than an int.
    /// </summary>
    /// <remarks>
    /// Recording this fixture found the generator casting the case to an int, which PHP warns
    /// about and answers with zero - so every row of `Loadout` had the same slot in its key
    /// and three of the four were lost. It is the same class of mistake `key-types` found in
    /// PHP, at the second width.
    /// </remarks>
    [Fact]
    public void Generated_php_reads_both_halves_of_a_key()
        => AssertPhpReads(
            "assert($accessor->route->findByFromAndTo('a b', 'c')->code === 'R1'); "
            + "assert($accessor->route->findByFromAndTo('a', 'b c')->code === 'R2'); "
            + "assert($accessor->route->findByFromAndTo('a', 'c') === null); "
            + "assert($accessor->route->findByCode('R3')->to === 'south'); "
            + "assert($accessor->loadout->findByStageAndSlot(2, "
            + "\\Tabbit\\Fixtures\\CompositeKey\\Slot::Feet)->label === 'field boots'); "
            + "assert($accessor->grid->findByXAndYAndZ(0, 0, 'roof')->name === 'above origin'); "
            + "assert($accessor->grid->containsXAndYAndZ(1, 0, 'floor')); "
            + "assert(!$accessor->grid->containsXAndYAndZ(9, 9, 'floor')); "

            // The link table, whose key is two references: the string one has to stay a
            // string, which is what a property typed `int` refused outright.
            + "assert($accessor->beastMove->findByBeastIdAndMoveId('deer', 2)->power === 20); "
            + "assert($accessor->beastMove->findByBeastIdAndMoveId('wolf', 1)->power === 30); "
            + "assert($accessor->beastMove->findByBeastIdAndMoveId('wolf', 2) === null); "
            + "assert($accessor->beastMove->findByBeastIdAndMoveId('deer', 1)"
            + "->beastByBeastId->name === 'Deer');");

    /// <summary>
    /// Lua, whose composite key skips the int64 normalization a single key uses.
    /// </summary>
    [Fact]
    public void Generated_lua_reads_both_halves_of_a_key()
        => AssertLuaReads(@"
local t = require('tables').new()
t:readAll(arg[1])
assert(t.route:findByFromAndTo('a b', 'c').code == 'R1')
assert(t.route:findByFromAndTo('a', 'b c').code == 'R2')
assert(t.route:findByFromAndTo('a', 'c') == nil)
assert(t.route:findByCode('R3').to == 'south')
assert(t.grid:findByXAndYAndZ(0, 0, 'roof').name == 'above origin')
assert(t.grid:containsXAndYAndZ(1, 0, 'floor'))
assert(not t.grid:containsXAndYAndZ(9, 9, 'floor'))
assert(t.beastMove:findByBeastIdAndMoveId('deer', 2).power == 20)
assert(t.beastMove:findByBeastIdAndMoveId('wolf', 1).power == 30)
assert(t.beastMove:findByBeastIdAndMoveId('wolf', 2) == nil)
assert(t.beastMove:findByBeastIdAndMoveId('deer', 1).beastByBeastId.name == 'Deer')
");

    // ------------------------------------------------------------------ compile

    /// <summary>The C++ map, whose key type needs a `std::hash` that exists.</summary>
    [Fact]
    public void Generated_cpp_compiles()
    {
        Converted();

        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++ compiler is required to check the generated code. {why}");

        var result = CppToolchain.Compile(Scenario, "CompositeKeyAccessor");

        Assert.True(result.Succeeded,
            $"Generated C++ for a composite key does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// C, which is the one language with nowhere free to put the key's text.
    /// </summary>
    /// <remarks>
    /// Its index is a sorted array searched by `strcmp`, so the text has to outlive the call
    /// that built it: the build path takes it from the table's arena and the lookup path
    /// builds into a stack buffer, falling back to the heap. Both are ordinary C and both are
    /// places a compiler has an opinion.
    /// </remarks>
    [Fact]
    public void Generated_c_compiles()
    {
        Converted();

        Assert.True(CToolchain.IsAvailable(out string why),
            $"A C compiler is required to check the generated code. {why}");

        string includeDir = Path.Combine(RepoLayout.OutputDir(Scenario), "c");
        string workDir = RepoLayout.WorkDir("_ccheck", Scenario);

        var sources = Directory
            .EnumerateFiles(includeDir, "*.c", SearchOption.AllDirectories)
            .ToList();

        var result = CToolchain.CompileOnly(workDir, includeDir, sources, "CompositeKey.h");

        Assert.True(result.Succeeded,
            $"Generated C for a composite key does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>Go, whose map key is the built text and whose import list is per file.</summary>
    /// <remarks>
    /// The import is the part worth a gate: `strconv` reaches a table's file only where a
    /// composite key is present, and an import Go does not use is a compile error - so both
    /// halves of that condition are wrong in a way only a build finds.
    /// </remarks>
    [Fact]
    public void Generated_go_compiles()
        => AssertCompiles(ConformanceHarness.GoIsAvailable, ConformanceHarness.CompileGo, "Go");

    /// <summary>Rust, where the built key is borrowed into the map's `get`.</summary>
    [Fact]
    public void Generated_rust_compiles()
        => AssertCompiles(ConformanceHarness.RustIsAvailable, ConformanceHarness.CompileRust, "Rust");

    /// <summary>Java, whose enum component is written as its declared value.</summary>
    [Fact]
    public void Generated_java_compiles()
        => AssertCompiles(ConformanceHarness.JavaIsAvailable, ConformanceHarness.CompileJava, "Java");

    /// <summary>Kotlin, the same.</summary>
    [Fact]
    public void Generated_kotlin_compiles()
        => AssertCompiles(ConformanceHarness.KotlinIsAvailable, ConformanceHarness.CompileKotlin, "Kotlin");

    /// <summary>Swift, whose tuples are not `Hashable` - which is why the key is text.</summary>
    [Fact]
    public void Generated_swift_compiles()
        => AssertCompiles(ConformanceHarness.SwiftIsAvailable, ConformanceHarness.CompileSwift, "Swift");

    /// <summary>Dart.</summary>
    [Fact]
    public void Generated_dart_compiles()
        => AssertCompiles(ConformanceHarness.DartIsAvailable, ConformanceHarness.CompileDart, "Dart");

    /// <summary>
    /// Unreal, whose `TMap` needs a key with a `GetTypeHash` - which `FString` has.
    /// </summary>
    /// <remarks>
    /// Opt-in like the other Unreal gate: set `TABBIT_UE_ROOT` to an engine and it runs the
    /// header tool over the generated module. Without one it returns having checked nothing,
    /// which is what the rest of the suite does with this toolchain.
    /// </remarks>
    [Fact]
    public void Generated_unreal_passes_the_header_tool()
    {
        string engineRoot = Environment.GetEnvironmentVariable("TABBIT_UE_ROOT");

        if (string.IsNullOrEmpty(engineRoot))
            return;

        Converted();

        var result = UnrealToolchain.RunHeaderTool(
            engineRoot,
            Path.Combine(RepoLayout.OutputDir(Scenario), "unreal", "Source", "CompositeKey"),
            moduleName: "CompositeKey",
            headerName: "FCompositeKey.h");

        Assert.True(result.Succeeded,
            $"Unreal Header Tool rejected the generated module."
            + $"{Environment.NewLine}{result.Output}");
    }

    // ------------------------------------------------------------------ helpers

    private delegate bool Availability(out string reason);

    private static void AssertCompiles(
        Availability available, Func<string, ToolResult> compile, string language)
    {
        Converted();

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(available(out string why),
            $"A {language} toolchain is required to check the generated code. {why}");

        var result = compile(Scenario);

        Assert.True(result.Succeeded,
            $"Generated {language} for a composite key does not compile."
            + $"{Environment.NewLine}{result.Output}");
    }

    private static void AssertPythonReads(string body)
    {
        Converted();

        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated code. {why}");

        var compiled = ConformanceHarness.CompilePython(Scenario);
        Assert.True(compiled.Succeeded,
            $"The generated Python does not compile.{Environment.NewLine}{compiled.Output}");

        var result = ConformanceHarness.RunPythonSnippet(
            Scenario,
            "import sys\nfrom composite_key_data import Tables\n" + body,
            BinaryDir());

        Assert.True(result.Succeeded,
            $"Reading through the generated Python failed.{Environment.NewLine}{result.Output}");
    }

    private static void AssertRubyReads(string body)
    {
        Converted();

        Assert.True(ConformanceHarness.RubyIsAvailable(out string why),
            $"A Ruby interpreter is required to check the generated code. {why}");

        var parsed = ConformanceHarness.CompileRuby(Scenario);
        Assert.True(parsed.Succeeded,
            $"The generated Ruby does not parse.{Environment.NewLine}{parsed.Output}");

        var result = ConformanceHarness.RunRubySnippet(
            Scenario,
            "require_relative 'tables'\n"
            + "accessor = CompositeKey::Tables.new\n"
            + "accessor.read_all(ARGV[0])\n"
            + body,
            BinaryDir());

        Assert.True(result.Succeeded,
            $"Reading through the generated Ruby failed.{Environment.NewLine}{result.Output}");
    }

    private static void AssertPhpReads(string body)
    {
        Converted();

        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to check the generated code. {why}");

        var linted = ConformanceHarness.CompilePhp(Scenario, "CompositeKeyAccessor");
        Assert.True(linted.Succeeded,
            $"The generated PHP does not parse.{Environment.NewLine}{linted.Output}");

        var result = ConformanceHarness.RunPhpSnippet(
            Scenario,
            "require_once __DIR__ . '/CompositeKeyAccessor.php'; "
            + "$accessor = new \\Tabbit\\Fixtures\\CompositeKey\\CompositeKeyAccessor(); "
            + "$accessor->readAll($argv[1]); "
            + body,
            BinaryDir());

        Assert.True(result.Succeeded,
            $"Reading through the generated PHP failed.{Environment.NewLine}{result.Output}");
    }

    private static void AssertLuaReads(string body)
    {
        Converted();

        Assert.True(ConformanceHarness.LuaIsAvailable(out string why),
            $"A C toolchain is required to build the Lua host. {why}");

        var result = ConformanceHarness.RunLuaSnippet(Scenario, body, BinaryDir());

        Assert.True(result.Succeeded,
            $"Reading through the generated Lua failed.{Environment.NewLine}{result.Output}");
    }

    private static string BinaryDir()
        => Path.Combine(RepoLayout.OutputDir(Scenario), "binary");

    private static void Converted()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }
}
