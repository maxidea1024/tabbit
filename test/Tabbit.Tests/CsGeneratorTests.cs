using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The generated C# and the binary reader emitted with it.
///
/// C# went without this check for a long time, and its absence is why nothing noticed
/// that the writer truncated every 64-bit value: the reader and writer were two
/// halves of one shared runtime, so a value that survived a round trip inside C#
/// looked correct whatever it did on the wire. The writer now lives in the exporter
/// and the reader is emitted separately, which makes them independent enough to be
/// worth comparing.
///
/// It also checks the thing that motivated the split: that the generated output
/// compiles on its own, with nothing installed.
/// </summary>
public class CsGeneratorTests
{
    private const string Scenario = "core";
    private const string Accessor = "CoreAccessor";

    /// <summary>
    /// A recipe can ask for one assembly instead of a folder of sources, and get the same output
    /// compiled.
    /// </summary>
    /// <remarks>
    /// Loaded and looked at rather than compared byte for byte: an assembly's bytes are the
    /// compiler's business and change with it, while what a consumer depends on is the surface -
    /// the accessor, a table on it, the type a row is. That is what this asserts.
    ///
    /// The one file the engine has to compile itself stays beside it as source, because it names
    /// `UnityEngine` and only Unity's compiler resolves that.
    /// </remarks>
    [Fact]
    public void An_assembly_can_be_asked_for_instead_of_sources()
    {
        var result = TabbitRunner.Convert("csharp-assembly");

        Assert.True(result.Succeeded, $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        string folder = Path.Combine(RepoLayout.OutputDir("csharp-assembly"), "cs");

        string assembly = Path.Combine(folder, "Tabbit.Fixtures.Assembly.dll");

        Assert.True(File.Exists(assembly), $"The assembly should be at `{assembly}`.");

        // And nothing else of the hundred files: that is what asking for an assembly is for.
        Assert.Empty(Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly));

        Assert.True(
            File.Exists(Path.Combine(folder, "tabbit", "TabbitUnityAdapter.cs")),
            "The engine's own file should still be written as source.");

        // The summaries travel with it, so an editor completing against the assembly can say what
        // a member is for.
        Assert.True(File.Exists(Path.Combine(folder, "Tabbit.Fixtures.Assembly.xml")),
            "The documentation should be written beside the assembly.");

        var loaded = System.Reflection.Assembly.Load(File.ReadAllBytes(assembly));

        var accessor = loaded.GetType("Tabbit.Fixtures.Assembly.CoreAccessor");

        Assert.NotNull(accessor);
        Assert.NotNull(accessor.GetNestedType("Snapshot"));
        Assert.NotNull(accessor.GetMethod("LoadAsync"));
        Assert.NotNull(accessor.GetProperty("Item"));

        // A row is the type the sources would have given, which is the whole claim: the same
        // output, compiled.
        Assert.NotNull(loaded.GetType("Tabbit.Fixtures.Assembly.ItemTable+Record"));
    }

    /// <summary>
    /// The generated code compiles for a plain .NET consumer with nothing defined.
    ///
    /// It did not use to. The read path switched on `NO_UNITY`, a symbol nobody defines
    /// by default, so the default branch was the Unity one - and it carried a
    /// `using Cysharp.Threading.Tasks;` that nothing in the generated code referenced.
    /// A .NET project without UniTask installed therefore failed to compile on a line
    /// that bought it nothing.
    /// </summary>
    [Fact]
    public void Generated_code_compiles_with_nothing_defined()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.Compile(Scenario, Accessor);

        Assert.True(result.Succeeded,
            $"Generated C# does not compile for a plain consumer.{Environment.NewLine}{result.Output}");
    }

    /// <summary>
    /// A path the File API cannot read goes through UnityWebRequest on every Unity
    /// platform, not only WebGL.
    ///
    /// StreamingAssets is shipped everywhere, and on two platforms what comes back is a
    /// URL rather than a path: Android leaves it inside the APK, so
    /// `Application.streamingAssetsPath` is `jar:file:///...!/assets`, and WebGL serves it
    /// over HTTP. The check for it used to sit inside the WebGL branch, which left Android
    /// handing an APK URL to File.ReadAllBytesAsync - a runtime failure on the platform
    /// where "it worked in the editor" helps least.
    ///
    /// Checked on the generated text, because the alternative is an Android device.
    ///
    /// It lives in the adapter now rather than in the accessor, which is the other half of
    /// what this pins: the engine's branches are in one file, and every other file the
    /// target writes is plain netstandard.
    /// </summary>
    [Fact]
    public void A_url_is_read_through_unity_web_request_on_every_unity_platform()
    {
        TabbitRunner.Convert(Scenario);

        string folder = Path.Combine(RepoLayout.OutputDir(Scenario), "csharp");

        string adapter = File.ReadAllText(
            Path.Combine(folder, "tabbit", "TabbitUnityAdapter.cs"));

        int guard = adapter.IndexOf("if (filename.Contains(\"://\"))", StringComparison.Ordinal);

        Assert.True(guard > 0, "Nothing routes a URL away from the File API.");

        // The directive above it decides which platforms get the check. It has to be the
        // one that means "any Unity", not the WebGL one.
        string before = adapter.Substring(0, guard);
        int directive = before.LastIndexOf("#if ", StringComparison.Ordinal);

        Assert.True(directive >= 0, "The URL check is not inside any #if.");

        string line = before.Substring(directive).Split('\n')[0].Trim();

        Assert.Equal("#if UNITY_5_3_OR_NEWER", line);

        // And the accessor knows nothing about any of it. This is the claim that lets the
        // same output be compiled into an assembly instead of shipped as sources.
        string accessor = File.ReadAllText(Path.Combine(folder, Accessor + ".cs"));

        Assert.DoesNotContain("UNITY", accessor);
        Assert.DoesNotContain("UnityEngine", accessor);
    }

    /// <summary>
    /// And it compiles as Unity would compile it.
    ///
    /// One symbol set now rather than two: Unity 6.5 is the floor, so the API level is
    /// netstandard 2.1 throughout and the branch that fell back to a worker thread is gone
    /// with the versions that needed it.
    ///
    /// The WebGL branch is not here, and neither is the adapter. Both name
    /// UnityEngine.Networking, so checking them needs an engine - the same limitation the
    /// Unreal target's header-tool gate has.
    /// </summary>
    [Theory]
    [InlineData("UNITY_5_3_OR_NEWER")]
    public void Generated_code_compiles_for_unity(string symbols)
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        var result = CsToolchain.Compile(Scenario, Accessor, symbols);

        Assert.True(result.Succeeded,
            $"Generated C# does not compile with `{symbols}` defined." +
            $"{Environment.NewLine}{result.Output}");
    }

    private static JsonElement RunGeneratedReader()
    {
        var conversion = TabbitRunner.Convert(Scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion failed.{Environment.NewLine}{conversion.Describe()}");

        string workDir = Path.Combine(RepoLayout.OutputDir("_cscheck"), Scenario);
        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        Directory.CreateDirectory(workDir);

        string generatedDir = Path.Combine(RepoLayout.OutputDir(Scenario), "csharp");

        var build = Execute("dotnet", RepoLayout.Root,
            "build",
            Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "cs-check", "cs-check.csproj"),
            "--nologo",
            $"-p:GeneratedDir={generatedDir}",
            "-o", workDir);

        Assert.True(build.Succeeded,
            $"Generated C# failed to compile on its own.{Environment.NewLine}{build.Output}");

        var run = Execute(Path.Combine(workDir, OnWindows ? "cs-check.exe" : "cs-check"),
                          workDir,
                          Path.Combine(RepoLayout.OutputDir(Scenario), "binary"));

        Assert.True(run.Succeeded,
            $"Generated C# failed to read the exported binary.{Environment.NewLine}{run.Output}");

        return JsonDocument.Parse(run.StdOut).RootElement.Clone();
    }

    private static JsonElement ExporterRows(string table)
    {
        string json = File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "json-named", table + ".json"));

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// The output has to build with nothing added to the project.
    ///
    /// This is what the reader being emitted rather than installed buys: before, a
    /// consuming project had to carry a 3,600-line runtime as a plugin, of which the
    /// generated code called four members.
    /// </summary>
    [Fact]
    public void Generated_csharp_compiles_without_anything_installed()
    {
        // Compiling is the assertion; the reader would fail to resolve otherwise.
        RunGeneratedReader();

        // And the emitted reader is genuinely there, in the `tabbit` directory every
        // target puts its runtime in.
        Assert.True(File.Exists(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "tabbit", "TabbitBinaryReader.cs")));

        // Nothing points at the runtime that used to be required.
        string accessor = File.ReadAllText(Path.Combine(
            RepoLayout.OutputDir(Scenario), "csharp", "CoreAccessor.cs"));

        Assert.DoesNotContain("Tabbit.Runtime", accessor);
    }

    [Fact]
    public void Generated_csharp_reads_back_every_primitive_type()
    {
        var actual = RunGeneratedReader().GetProperty("TestFieldTypes");
        var expected = ExporterRows("TestFieldTypes");

        Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());

        for (int i = 0; i < expected.GetArrayLength(); i++)
        {
            Assert.Equal(expected[i].GetProperty("index").GetInt32(),
                         actual[i].GetProperty("index").GetInt32());
            Assert.Equal(expected[i].GetProperty("stringField").GetString(),
                         actual[i].GetProperty("stringField").GetString());
            Assert.Equal(expected[i].GetProperty("boolField").GetBoolean(),
                         actual[i].GetProperty("boolField").GetBoolean());
            Assert.Equal(expected[i].GetProperty("intField").GetInt32(),
                         actual[i].GetProperty("intField").GetInt32());
            Assert.Equal(expected[i].GetProperty("uuidField").GetString(),
                         actual[i].GetProperty("uuidField").GetString());

            // Exported as a string so JSON cannot round it; compared as text.
            Assert.Equal(expected[i].GetProperty("bigIntField").GetString(),
                         actual[i].GetProperty("bigIntField").GetString());
        }
    }

    /// <summary>
    /// A17 - the writer used to cast a 64-bit value through uint, truncating it.
    ///
    /// Now that the writer is the exporter's own and the reader is emitted, the two
    /// are separate implementations and this comparison means something.
    /// </summary>
    [Fact]
    public void A17_sixty_four_bit_values_survive_the_round_trip()
    {
        var records = RunGeneratedReader().GetProperty("TestFieldTypes");

        Assert.Equal("9007199254740993", records[0].GetProperty("bigIntField").GetString());
        Assert.Equal("-9007199254740993", records[1].GetProperty("bigIntField").GetString());
    }

    [Fact]
    public void Generated_csharp_reads_both_array_kinds()
    {
        var records = RunGeneratedReader().GetProperty("ArrayTypes");

        string[] Texts(JsonElement row, string name)
            => row.GetProperty(name).EnumerateArray().Select(e => e.GetRawText().Trim('"')).ToArray();

        // Delimited: a different length in every row, including an empty one.
        Assert.Equal(new[] { "red", "green", "blue" }, Texts(records[0], "tags"));
        Assert.Empty(Texts(records[2], "tags"));

        // Serial: fixed width, unaffected by the delimited columns beside it.
        Assert.Equal(new[] { "5", "6" }, Texts(records[2], "slot"));
    }

    [Fact]
    public void Generated_csharp_resolves_cross_table_references()
    {
        var records = RunGeneratedReader().GetProperty("Item");

        Assert.Equal("Weapon", records[0].GetProperty("categoryName").GetString());
        Assert.Equal("Armor", records[1].GetProperty("categoryName").GetString());
        Assert.Equal("Potion", records[2].GetProperty("categoryName").GetString());
    }

    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    private sealed class ToolRun
    {
        public bool Succeeded;
        public string StdOut;
        public string Output;
    }

    private static ToolRun Execute(string fileName, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var stdout = new StringBuilder();
        var combined = new StringBuilder();

        using var process = new Process { StartInfo = psi };

        // Two streams, two threads, one StringBuilder. Locked because it is not safe
        // for that, and the failure is not a garbled line - it is an exception from
        // inside AppendLine, on a thread pool worker where nothing catches it.
        var writing = new object();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            lock (writing)
            {
                stdout.AppendLine(e.Data);
                combined.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            lock (writing)
                combined.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(milliseconds: 300_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`{fileName}` did not finish within 5 minutes.");
        }

        process.WaitForExit();

        return new ToolRun
        {
            Succeeded = process.ExitCode == 0,
            StdOut = stdout.ToString(),
            Output = combined.ToString(),
        };
    }
}
