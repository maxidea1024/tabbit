using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The Unreal target: USTRUCT rows, UENUM enums, a static accessor and a module the
/// project can add as it stands.
///
/// The wire format is checked by the conformance corpus through the plain C++ reader,
/// which reads the same bytes. What is checked here is everything that makes this an
/// Unreal module rather than C++ in a folder: that Unreal Header Tool accepts it, and
/// that it is written in the engine's own types and error handling.
///
/// The last two matter because the target shipped for a while with the plain C++
/// reader inside it. That built, and UHT accepted it, and the corpus passed - and it
/// was still wrong: std::string and a Tabbit uuid struct where FString and FGuid
/// belonged, costing an allocation per string cell and a text parse per uuid, and a
/// reader that reported a malformed file by throwing inside a module that Unreal
/// builds with exceptions disabled. Nothing in the suite noticed, so these do.
/// </summary>
public class UnrealTargetTests
{
    private const string Scenario = "unreal";

    private static string ModuleDir(string scenario, string moduleName)
        => Path.Combine(RepoLayout.OutputDir(scenario), "Source", moduleName);

    /// <summary>
    /// Every generated line of the module that is not a comment.
    ///
    /// Comments are dropped because the ones explaining why the standard library is
    /// not used here would otherwise fail the tests that check it is not used.
    /// </summary>
    private static IReadOnlyList<(string File, int Line, string Text)> CodeLines()
    {
        var lines = new List<(string, int, string)>();

        string module = ModuleDir(Scenario, "TabbitCore");

        foreach (var path in Directory.EnumerateFiles(module, "*.*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(path) != ".h" && Path.GetExtension(path) != ".cpp")
                continue;

            var text = File.ReadAllLines(path);

            for (int i = 0; i < text.Length; i++)
            {
                string trimmed = text[i].TrimStart();

                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("*", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    continue;
                }

                lines.Add((Path.GetFileName(path), i + 1, text[i]));
            }
        }

        Assert.NotEmpty(lines);

        return lines;
    }

    private static void NothingContains(string needle, string why)
    {
        var offenders = CodeLines()
            .Where(line => line.Text.Contains(needle, StringComparison.Ordinal))
            .Select(line => $"  {line.File}:{line.Line}  {line.Text.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{why}{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    [Fact]
    public void Generates_a_module_that_needs_no_wiring_up()
    {
        var result = TabbitRunner.Convert(Scenario);
        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string module = ModuleDir(Scenario, "TabbitCore");

        // A module is these four things. Anything missing and a project has work to do
        // before the output compiles, which is the thing this target is for.
        Assert.True(File.Exists(Path.Combine(module, "TabbitCore.Build.cs")));
        Assert.True(File.Exists(Path.Combine(module, "Public", "FTabbitCore.h")));
        Assert.True(File.Exists(Path.Combine(module, "Private", "FTabbitCore.cpp")));
        Assert.True(File.Exists(Path.Combine(module, "Public", "TabbitTcbReader.h")));
    }

    /// <summary>
    /// The module is written in the engine's types, not the standard library's.
    ///
    /// Unreal has an equivalent for every type a table holds, and going through the
    /// standard library's meant building an FString from a std::string and an FGuid by
    /// parsing text a uuid struct had just printed. Both are gone; this is what keeps
    /// them gone, because nothing else in the suite can tell the difference.
    /// </summary>
    [Fact]
    public void The_module_is_written_in_engine_types()
    {
        TabbitRunner.Convert(Scenario);

        NothingContains("std::", "The module uses a standard library type where the engine has one:");

        // A standard library header is how one gets in. Engine headers are quoted.
        NothingContains("#include <", "The module includes a standard library header:");
    }

    /// <summary>
    /// Nothing in the module throws.
    ///
    /// Unreal builds a module with exceptions disabled unless its Build.cs asks
    /// otherwise, so a throw is not a failure a caller can handle - it is the process
    /// ending. The reader reports a malformed file by returning false instead, which
    /// is what `bool Read(const FString&)` has always claimed to do.
    /// </summary>
    [Fact]
    public void Nothing_in_the_module_throws()
    {
        TabbitRunner.Convert(Scenario);

        NothingContains("throw", "The module throws, and Unreal builds it with exceptions disabled:");

        // And the Build.cs must not quietly turn exceptions on to make the above safe.
        // That would work, and it would also mean every module depending on this one
        // pays for it. The assignment rather than the word: the file says in a comment
        // why it does not set this, and saying so is the opposite of an offence.
        string build = File.ReadAllText(
            Path.Combine(ModuleDir(Scenario, "TabbitCore"), "TabbitCore.Build.cs"));

        Assert.DoesNotMatch(@"bEnableExceptions\s*=\s*true", build);
    }

    /// <summary>
    /// A malformed table is refused rather than half-loaded.
    ///
    /// Checked on the generated text rather than by running it, because running it
    /// needs an engine. What is pinned is that the load looks at the reader's failure
    /// after the row loop and returns false - the loop itself cannot, since the reader
    /// keeps going quietly by design so that twenty fields need no twenty checks.
    /// </summary>
    [Fact]
    public void A_malformed_table_is_refused()
    {
        TabbitRunner.Convert(Scenario);

        string source = File.ReadAllText(Path.Combine(
            ModuleDir(Scenario, "TabbitCore"), "Private", "FTabbitCore.cpp"));

        Assert.Contains("if (Reader.HasFailed())", source);

        // The row loop stops on failure too. Without it a corrupt row count spins,
        // appending a default record per turn until the allocator gives up.
        Assert.Contains("&& !Reader.HasFailed())", source);
    }

    /// <summary>
    /// The rows are reachable from a Blueprint graph.
    ///
    /// Every row is a USTRUCT marked BlueprintType with BlueprintReadOnly properties,
    /// which says they are meant to be used from Blueprint - and for a long time there
    /// was no way to obtain one. The accessor is a plain C++ class and a static method on
    /// one is not something a graph can call, so a designer could declare a variable of a
    /// row type and had nothing to put in it.
    ///
    /// Checked on the generated text, because running it needs an engine. What is pinned
    /// is that the library exists, that its name is not double-prefixed, and that every
    /// table has a getter - a library with one table's worth of functions would look
    /// perfectly fine to a compiler.
    /// </summary>
    [Fact]
    public void Every_table_is_reachable_from_blueprint()
    {
        TabbitRunner.Convert(Scenario);

        string header = File.ReadAllText(Path.Combine(
            ModuleDir(Scenario, "TabbitCore"), "Public", "FTabbitCore.h"));

        Assert.Contains(": public UBlueprintFunctionLibrary", header);

        // Unreal's prefix says what a type is: `U` for a UObject, `F` for a plain class.
        // Prefixing the accessor name blindly produced `UFTabbitCoreLibrary`.
        Assert.Contains("class TABBITCORE_API UTabbitCoreLibrary", header);
        Assert.DoesNotContain("UFTabbitCore", header);

        // A getter per table, by primary index and by position. The names come from the
        // sheet, so this reads them out of the accessor's own table list rather than
        // repeating the fixture here.
        foreach (Match slot in Regex.Matches(
                     header, @"static const F(?<name>\w+)Table& \k<name>\(\)"))
        {
            string name = slot.Groups["name"].Value;

            // `int32 Key`, not `int32 Index`: the primary index is whatever the sheet put
            // in the first column, so the parameter is named after what it is rather than
            // after a column name that is only usually `Index`.
            Assert.Contains($"static F{name}Row Get{name}Row(int32 Key, bool& bFound);", header);
            Assert.Contains($"static F{name}Row Get{name}RowAt(int32 Position, bool& bFound);", header);
            Assert.Contains($"static int32 Get{name}RowCount();", header);
        }

        // And the module can actually link UBlueprintFunctionLibrary.
        string build = File.ReadAllText(
            Path.Combine(ModuleDir(Scenario, "TabbitCore"), "TabbitCore.Build.cs"));

        Assert.Contains("\"Engine\"", build);
    }

    /// <summary>
    /// Nothing hands a Blueprint a reference or a whole table.
    ///
    /// Unreal Header Tool does not accept a reference return on a UFUNCTION, and a
    /// TArray return would copy every row of the table on every call - which for a
    /// localization table is megabytes per node evaluation. A count and an indexed getter
    /// let a graph walk the table one row at a time instead.
    /// </summary>
    [Fact]
    public void No_blueprint_function_returns_a_reference_or_a_whole_table()
    {
        TabbitRunner.Convert(Scenario);

        var lines = File.ReadAllLines(Path.Combine(
            ModuleDir(Scenario, "TabbitCore"), "Public", "FTabbitCore.h"));

        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("UFUNCTION", StringComparison.Ordinal))
                continue;

            // The declaration follows its UFUNCTION, past the meta line it wraps onto.
            string declaration = string.Join(" ", lines.Skip(i).Take(4))
                                       .Replace("UFUNCTION", "", StringComparison.Ordinal);

            int returns = declaration.IndexOf("static ", StringComparison.Ordinal);
            if (returns < 0)
                continue;

            string signature = declaration.Substring(returns);
            string returnType = signature.Split(' ').Skip(1).FirstOrDefault() ?? "";

            Assert.False(returnType.EndsWith("&", StringComparison.Ordinal),
                $"A UFUNCTION returns a reference, which UHT refuses: {signature.Trim()}");

            Assert.False(returnType.StartsWith("TArray", StringComparison.Ordinal),
                $"A UFUNCTION returns a whole table by value: {signature.Trim()}");
        }
    }

    /// <summary>
    /// A failed load names the packaging setting that is usually the reason.
    ///
    /// A `.tcb` is not an asset, so Unreal ignores it unless the project lists its
    /// directory under Packaging -> "Additional Non-Asset Directories to Package". Miss
    /// that and everything works in the editor and the file is simply absent from the
    /// build - which reads as "the loader is broken" to whoever finds it.
    ///
    /// FFileHelper itself is right: it goes through IPlatformFile, which mounts the .pak
    /// and reads out of it as though the file were loose. So the code needs no change for
    /// a packaged build; only the project setting does, and the message says so.
    /// </summary>
    [Fact]
    public void A_missing_table_names_the_packaging_setting()
    {
        TabbitRunner.Convert(Scenario);

        string source = File.ReadAllText(Path.Combine(
            ModuleDir(Scenario, "TabbitCore"), "Private", "FTabbitCore.cpp"));

        Assert.Contains("Additional Non-Asset Directories to Package", source);
    }

    /// <summary>
    /// The generated include must be the last one.
    ///
    /// Unreal Header Tool requires it, and when it is not, the error it reports names
    /// some other line entirely - so this is worth pinning rather than rediscovering.
    /// </summary>
    [Fact]
    public void The_generated_include_comes_last()
    {
        TabbitRunner.Convert(Scenario);

        var includes = File.ReadAllLines(
                Path.Combine(ModuleDir(Scenario, "TabbitCore"), "Public", "FTabbitCore.h"))
            .Where(line => line.TrimStart().StartsWith("#include", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(includes);
        Assert.Contains(".generated.h", includes[includes.Count - 1]);
    }

    /// <summary>
    /// A BlueprintType enum is uint8, so a label outside 0 to 255 makes the enum widen to
    /// int32 and give up Blueprint rather than failing the conversion.
    /// </summary>
    /// <remarks>
    /// It used to refuse outright, which made the Unreal target the only one that could not
    /// read a model the other eleven read. The values belong to the sheet - an enum of error
    /// codes or bit flags is ordinary - and a code generator does not get to reject one.
    ///
    /// So it degrades and says which label did it. The enum stays a UENUM, so it is still
    /// reflected and still serialises; it loses BlueprintType, and every field declared with
    /// it loses its UPROPERTY, because UHT will not expose a property whose type Blueprint
    /// cannot see. All of it still reads from C++, which is where the data is used.
    ///
    /// The conformance corpus is what made this matter rather than a preference: its `Flag`
    /// enum has a label at 1048576 - three varint bytes, which is the point of it - so the
    /// Unreal target could not read the corpus at all while this threw.
    /// </remarks>
    [Fact]
    public void An_enum_value_outside_a_byte_widens_instead_of_failing()
    {
        var result = TabbitRunner.Convert("unreal-enum-range");

        Assert.True(result.Succeeded,
            $"An enum value a uint8 cannot hold failed the conversion.{Environment.NewLine}{result.Describe()}");

        // Warned, so a project that wanted the enum in Blueprint does not find out from a
        // missing pin.
        Assert.Contains("1048576", result.StdOut);
        Assert.Contains("uint8", result.StdOut);

        string header = File.ReadAllText(Path.Combine(
            ModuleDir("unreal-enum-range", "TabbitOutOfRange"), "Public", "FTabbitOutOfRange.h"));

        Assert.Contains(": int32", header);
        Assert.DoesNotContain("UENUM(BlueprintType)", header);

        // And the field declared with it is written, without a UPROPERTY.
        Assert.Contains("// No UPROPERTY:", header);
    }

    /// <summary>
    /// Unreal Header Tool accepts the generated module.
    ///
    /// This is the only check that reaches the Unreal-specific part - the reflection
    /// macros, the include order, the property types UHT will and will not take - and
    /// it needs an engine, which CI does not have. Point TABBIT_UE_ROOT at an engine
    /// and it runs; leave it unset and it does not.
    ///
    /// Verified by hand against 4.27.2 when the target was written. UE4 is the stricter
    /// of the two the target supports: its header tool rejects a double property, which
    /// is why a double member here carries no UPROPERTY.
    /// </summary>
    /// <summary>
    /// The generated updater, built by UnrealBuildTool against a real engine and run.
    /// </summary>
    /// <remarks>
    /// The only thing in this repository that compiles Unreal C++ the way Unreal does.
    /// Everything else Unreal-shaped is checked by the header tool, which parses headers,
    /// or by an off-engine build against hand-written stubs - and a stub cannot tell you
    /// whether `IHttpRequest::SetTimeout` exists, only that your code agrees with your own
    /// idea of the engine.
    ///
    /// Which is not a hypothetical worry. The first run of this gate failed on a `#if`
    /// comparing `ENGINE_MAJOR_VERSION`, which a Program target does not define: the
    /// updater picked the wrong ticker type on every engine, and the stubs had said it was
    /// fine.
    ///
    /// Needs an engine, which CI does not have. Point TABBIT_UE_ROOT at one and it runs;
    /// leave it unset and it does not. Verified against 4.27.2.
    /// </remarks>
    [Fact]
    public void The_updater_builds_with_unreal_build_tool()
    {
        string engineRoot = Environment.GetEnvironmentVariable("TABBIT_UE_ROOT");

        if (string.IsNullOrEmpty(engineRoot))
            return;

        TabbitRunner.Convert(Scenario);

        var result = UnrealToolchain.BuildUpdaterWithUbt(
            engineRoot,
            Path.Combine(RepoLayout.OutputDir(Scenario), "Source", "TabbitCore"));

        Assert.True(result.Succeeded,
            $"The generated updater did not build against the engine at {engineRoot}." +
            $"{Environment.NewLine}{result.Output}");

        // The program checks the manifest parser and the hash against known values, so a
        // zero exit is more than "it linked".
        Assert.Contains("compiles, links and runs", result.Output);
    }

    [Fact]
    public void Unreal_header_tool_accepts_the_generated_module()
    {
        string engineRoot = Environment.GetEnvironmentVariable("TABBIT_UE_ROOT");

        if (string.IsNullOrEmpty(engineRoot))
            return;

        TabbitRunner.Convert(Scenario);

        var result = UnrealToolchain.RunHeaderTool(
            engineRoot,
            ModuleDir(Scenario, "TabbitCore"),
            moduleName: "TabbitCore",
            headerName: "FTabbitCore.h");

        Assert.True(result.Succeeded,
            $"Unreal Header Tool rejected the generated module.{Environment.NewLine}{result.Output}");
    }
}
