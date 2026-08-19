using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Identifiers taken from a sheet that collide with a keyword in an output language.
///
/// Whether this matters depends on how a generator cases an identifier, and the three
/// disagree. C# renders members PascalCase, which lifts every all-lowercase keyword out
/// of the way. TypeScript renders them camelCase. C++ renders them snake_case, so a
/// field called `Int` becomes `int` and a field called `Class` becomes `class`.
///
/// Both keyword lists in the repository - CsCodeGenerator.Keywords.cs and
/// TsCodeGenerator.Keywords.cs - were declared and never read by anything, and the C++
/// generator had no list at all. The C# one carried a note claiming escaping made the
/// problem moot. For C# that happens to be true; for C++ it was not, and the generator
/// emitted `std::string class;` while the conversion reported success.
///
/// These tests exist so the compilers answer the question rather than a comment - and
/// every language Tabbit generates is here, not the three whose answer somebody had
/// already worked out. Extending it to the other seven found one immediately: a field
/// named `Int` became `int` in Dart, which shadows the type inside its own class, so
/// `int int = 0;` did not compile and neither did any declaration after it. Dart's
/// keyword list did not catch it because `int` is not a keyword - it is an ordinary
/// identifier that happens to name a type, which is exactly why it collides.
/// </summary>
public class ReservedWordTests
{
    private const string Scenario = "reserved-words";

    /// <summary>
    /// C++ members are snake_case, which is where this actually bites.
    /// </summary>
    [Fact]
    public void Generated_cpp_compiles_with_keyword_named_fields()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++17 compiler is required to check the generated C++. {why}");

        var result = CppToolchain.Compile(Scenario, "ReservedAccessor");

        Assert.True(result.Succeeded,
            $"Generated C++ does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// C# members are PascalCase, so a lowercase keyword cannot survive into one. The
    /// test records that rather than assuming it.
    /// </summary>
    [Fact]
    public void Generated_csharp_compiles_with_keyword_named_fields()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.Compile(Scenario, "ReservedAccessor");

        Assert.True(result.Succeeded,
            $"Generated C# does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// TypeScript members are camelCase. Most reserved words are legal as member names,
    /// but `constructor` is not something a class can define as an accessor.
    /// </summary>
    [Fact]
    public void Generated_typescript_type_checks_with_keyword_named_fields()
    {
        Assert.True(TypescriptToolchain.IsAvailable(out string why),
            $"Node toolchain required to type-check generated TypeScript. {why}");

        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string generatedDir = Path.Combine(RepoLayout.OutputDir(Scenario), "typescript");

        var check = TypescriptToolchain.TypeCheck(generatedDir);

        Assert.True(check.Succeeded,
            $"Generated TypeScript does not compile.{Environment.NewLine}{check.Output}");
    }

    // ------------------------------------------------- the other seven languages

    /// <summary>
    /// Converts once and hands back nothing: each language's test calls this and then
    /// compiles its own output.
    /// </summary>
    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");
    }

    /// <summary>
    /// Go members are PascalCase and every Go keyword is lower case, so nothing should
    /// collide - and an exported member has to start with a capital anyway. Recorded
    /// rather than assumed.
    /// </summary>
    [Fact]
    public void Generated_go_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.GoIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileGo(Scenario);

        Assert.True(result.Succeeded, $"Generated Go does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>Rust members are snake_case, which is where its keywords live.</summary>
    [Fact]
    public void Generated_rust_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.RustIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileRust(Scenario);

        Assert.True(result.Succeeded, $"Generated Rust does not compile.{Environment.NewLine}{result.Output}");
    }

    [Fact]
    public void Generated_python_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.PythonIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompilePython(Scenario);

        Assert.True(result.Succeeded, $"Generated Python does not compile.{Environment.NewLine}{result.Output}");
    }

    [Fact]
    public void Generated_java_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.JavaIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileJava(Scenario);

        Assert.True(result.Succeeded, $"Generated Java does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// Kotlin escapes with backticks rather than by changing the name, so the generated
    /// member really is called `class`. Whether that compiles is the question.
    /// </summary>
    [Fact]
    public void Generated_kotlin_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.KotlinIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileKotlin(Scenario);

        Assert.True(result.Succeeded, $"Generated Kotlin does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// Swift escapes with backticks, as Kotlin does, so the generated member really is
    /// called `class`.
    /// </summary>
    /// <remarks>
    /// This gate carries two more things with it. The recipe turns the updater on, so the
    /// only check that compiles `Updater.swift` at all is this one. And `CompileSwift`
    /// type-checks in both language modes with warnings as errors, which is what a keyword
    /// name would fail loudly rather than subtly.
    /// </remarks>
    [Fact]
    public void Generated_swift_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.SwiftIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileSwift(Scenario);

        Assert.True(result.Succeeded, $"Generated Swift does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// Ruby members are snake_case and nearly every Ruby keyword is lower case, so this
    /// is the language with the most ways to collide.
    /// </summary>
    [Fact]
    public void Generated_ruby_parses_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.RubyIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileRuby(Scenario);

        Assert.True(result.Succeeded, $"Generated Ruby does not parse.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// The one that found a defect. A field named `Int` became `int`, which is not a
    /// Dart keyword but is the name of a type - and a field of that name shadows the
    /// type inside its own class, so the declaration after it does not compile.
    /// </summary>
    [Fact]
    public void Generated_dart_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.DartIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileDart(Scenario);

        Assert.True(result.Succeeded, $"Generated Dart does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// C members are snake_case and every C keyword is lower case, so the whole keyword
    /// list can collide - the same situation C++ is in, and C has no escape at all. The
    /// name has to change.
    /// </summary>
    [Fact]
    public void Generated_c_compiles_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.CIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileC(Scenario, "ReservedData");

        Assert.True(result.Succeeded, $"Generated C does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// And the generated C header compiles as C++, which is what its `extern "C"` says.
    ///
    /// This is the check that made the C profile carry the C++ keyword list as well.
    /// `class`, `delete`, `operator` and `namespace` are all perfectly good C member
    /// names - the C build was green with every one of them in the header - and every
    /// one stops a C++ compiler at the declaration. A header that offers itself to C++
    /// and cannot be included from it is worse than one that does not offer.
    /// </summary>
    [Fact]
    public void Generated_c_header_can_be_included_from_cpp()
    {
        Assert.True(CppToolchain.IsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompileCAsCpp(Scenario, "ReservedData");

        Assert.True(result.Succeeded,
            $"The generated C header does not compile as C++.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// PHP escapes nothing, and this is what says that is right rather than hopeful.
    ///
    /// A property or method may be named after a reserved word in PHP 7 and later, so a
    /// field called `class` needs nothing done to it - and renaming one would change the
    /// generated API for no reason. The claim is only worth making because the
    /// interpreter is asked.
    /// </summary>
    [Fact]
    public void Generated_php_parses_with_keyword_named_fields()
    {
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why), why);

        Convert();

        var result = ConformanceHarness.CompilePhp(Scenario, "ReservedData");

        Assert.True(result.Succeeded, $"Generated PHP does not parse.{Environment.NewLine}{result.Output}");
    }
}
