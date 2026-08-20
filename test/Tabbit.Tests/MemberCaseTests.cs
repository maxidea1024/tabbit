using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A recipe asking for a member spelling other than the language's own.
/// </summary>
/// <remarks>
/// Two questions, and a compiler is the only thing that answers either.
///
/// **Does the spelling move the members, and only the members?** Nearly every other name in
/// the output is built by joining a member's name into a longer identifier - `HasFoo`,
/// `NewFoo()`, `Foo_N`, `FindByFoo`, `SetReference_Foo_INTERNAL` - and those read correctly
/// only if the joined word stays capitalized. A setting that moved them too would not be
/// spelling the output, it would be renaming it.
///
/// **Does a name that was safe at one spelling stay safe at another?** The reserved-word
/// lists were written assuming a language's own spelling, and the assumption is load-bearing:
/// C# left its list empty because members are PascalCase and every C# keyword is lower case,
/// which is exactly the argument that stops holding at camel case.
///
/// The fixture reads two workbooks. One has columns named after keywords, which is what makes
/// the escaping real; the other has multi-word and optional columns, without which half of
/// this is invisible - `label` is `label` in camel and in snake alike, and a presence accessor
/// only exists where a column may be absent.
///
/// spec/naming-conventions.md.
/// </remarks>
public class MemberCaseTests
{
    private const string Scenario = "member-case";

    /// <summary>Everything the fixture asks for is generated before anything reads it.</summary>
    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(Scenario);

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }

    private static string ReadOutput(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoLayout.OutputDir(Scenario), Path.Combine(parts)));

    /// <summary>
    /// C# at camel case: the members move, and the names built out of them do not.
    /// </summary>
    [Fact]
    public void The_setting_moves_the_members_and_not_the_names_built_from_them()
    {
        Convert();

        string source = ReadOutput("csharp", "tables", "TemplateTable.cs");

        // The member.
        Assert.Contains("public int index => _index;", source);

        // The identifiers that join it into a longer name. `Index` stays capitalized in all
        // of them, because in `FindByIndex` the field is one word of a compound name rather
        // than the name itself.
        Assert.Contains("FindByIndex(", source);
        Assert.Contains("GetByIndexOrThrow(", source);
        Assert.Contains("ContainsIndex(", source);
        Assert.DoesNotContain("FindByindex(", source);
        Assert.DoesNotContain("GetByindexOrThrow(", source);
    }

    /// <summary>
    /// A column named after a keyword is escaped at camel case, where at Pascal case it
    /// never could have collided.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the C# reserved-word list stopped being empty. Without it the
    /// generated file declares `public string class => _class;` and does not compile - so the
    /// compile below is the assertion, and these are here to say what it is checking.
    /// </remarks>
    [Fact]
    public void A_member_that_lands_on_a_keyword_is_escaped()
    {
        Convert();

        string source = ReadOutput("csharp", "tables", "TemplateTable.cs");

        Assert.Contains("public string @class => _class;", source);
        Assert.Contains("public int @int => _int;", source);
        Assert.Contains("public string @operator => _operator;", source);
        Assert.Contains("public string @namespace => _namespace;", source);

        // And a name that is not a keyword is left alone, rather than everything being
        // escaped to be safe.
        Assert.Contains("public bool delete => _delete;", source);
        Assert.Contains("public string function => _function;", source);
    }

    /// <summary>
    /// A presence accessor is a member too, so it follows the spelling rather than gluing a
    /// fixed prefix onto a differently spelled name.
    /// </summary>
    /// <remarks>
    /// `has_open_at` at snake case, from a column called `OpenAt`. Built by spelling the
    /// composed name rather than by joining `has_` to an already spelled one, which is what
    /// makes one function give the right answer for all four spellings.
    /// </remarks>
    [Fact]
    public void A_presence_accessor_follows_the_member_spelling()
    {
        Convert();

        string source = ReadOutput("java", "membercase", "DropRecord.java");

        Assert.Contains("public long open_at;", source);
        Assert.Contains("public boolean has_open_at;", source);
        Assert.DoesNotContain("hasOpenAt", source);
    }

    /// <summary>The generated C# compiles, keywords and all.</summary>
    [Fact]
    public void The_generated_csharp_compiles()
    {
        Convert();

        var result = CsToolchain.Compile(Scenario, "MemberCaseAccessor");

        Assert.True(result.Succeeded,
            $"The generated C# at camel case does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>The generated C++ compiles at camel case.</summary>
    [Fact]
    public void The_generated_cpp_compiles()
    {
        Convert();

        Assert.True(CppToolchain.IsAvailable(out string why),
            $"A C++ toolchain is required to check the generated code. {why}");

        var result = CppToolchain.Compile(Scenario, "MemberCaseAccessor");

        Assert.True(result.Succeeded,
            $"The generated C++ at camel case does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>The generated Java compiles at snake case.</summary>
    [Fact]
    public void The_generated_java_compiles()
    {
        Convert();

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.JavaIsAvailable(out string why),
            $"A Java toolchain is required to check the generated code. {why}");

        var result = ConformanceHarness.CompileJava(Scenario);

        Assert.True(result.Succeeded,
            $"The generated Java at snake case does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>The generated Python compiles at Pascal case.</summary>
    [Fact]
    public void The_generated_python_compiles()
    {
        Convert();

        Assert.True(ConformanceHarness.PythonIsAvailable(out string why),
            $"A Python interpreter is required to check the generated code. {why}");

        var result = ConformanceHarness.CompilePython(Scenario);

        Assert.True(result.Succeeded,
            $"The generated Python at Pascal case does not compile.{Environment.NewLine}{result.Output}");
    }

    /// <summary>A value that is not a spelling of anything is refused, and names what it takes.</summary>
    [Fact]
    public void A_setting_that_is_not_a_spelling_is_refused()
    {
        var thrown = Assert.Throws<TabbitException>(
            () => Tabbit.CodeGeneration.MemberCasing.From(
                "PascalCase", Tabbit.Extensions.NameCase.Pascal, "csharp"));

        Assert.Contains("`csharp`", thrown.Message);
        Assert.Contains("`pascal`, `camel`, `snake` or `upper-snake`", thrown.Message);

        // Blank keeps the language's own spelling rather than being an error, which is what a
        // recipe written before this setting existed holds.
        Assert.Equal(
            Tabbit.Extensions.NameCase.Snake,
            Tabbit.CodeGeneration.MemberCasing.From("", Tabbit.Extensions.NameCase.Snake, "python"));

        // Hyphen and underscore are one separator, as they are for the recipe's other policy
        // settings.
        Assert.Equal(
            Tabbit.Extensions.NameCase.UpperSnake,
            Tabbit.CodeGeneration.MemberCasing.From("upper_snake", Tabbit.Extensions.NameCase.Pascal, "c"));
    }
}
