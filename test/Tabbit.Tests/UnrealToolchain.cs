using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Tabbit.Tests;

/// <summary>
/// Runs Unreal Header Tool over a generated module.
///
/// UHT is normally invoked by UnrealBuildTool as part of a build, and takes a manifest
/// UBT writes describing every module in the target. Building a whole target to check
/// one generated header would take minutes; a manifest naming CoreUObject and the
/// module under test takes seconds and checks the same thing - the reflection macros,
/// the include order, and which property types the tool will accept.
///
/// CoreUObject is there because every USTRUCT depends on it. Its own generated headers
/// are already built in an engine that has been compiled, which is the only kind this
/// can run against.
/// </summary>
internal static class UnrealToolchain
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>
    /// What this platform is called in an engine tree.
    /// </summary>
    /// <remarks>
    /// The engine names a directory after the host platform in three places that matter
    /// here - the build script, the binaries, and the target UnrealBuildTool is asked for -
    /// and all three used to be spelled `Win64` outright. An engine on Linux or macOS then
    /// failed on the first path rather than on anything to do with the generated code, and
    /// the message named a `Build.bat` that platform has never had.
    /// </remarks>
    private static string EnginePlatform
        => OnWindows ? "Win64"
         : OperatingSystem.IsMacOS() ? "Mac"
         : "Linux";

    /// <summary>The suffix an executable carries here, which off Windows is none.</summary>
    private static string ExeSuffix => OnWindows ? ".exe" : "";

    /// <summary>
    /// The script that drives UnrealBuildTool.
    /// </summary>
    /// <remarks>
    /// Windows keeps it directly under BatchFiles; every other platform keeps its own in a
    /// subdirectory named for it. Both are returned to the caller as something to run, and
    /// off Windows that is the shell script itself rather than an interpreter plus a script.
    /// </remarks>
    private static string BuildScript(string engineRoot)
        => OnWindows
            ? Path.Combine(engineRoot, "Engine", "Build", "BatchFiles", "Build.bat")
            : Path.Combine(engineRoot, "Engine", "Build", "BatchFiles", EnginePlatform, "Build.sh");

    /// <summary>
    /// Builds a scenario's generated Unreal module against the off-engine stubs and runs a
    /// harness over it.
    /// </summary>
    /// <remarks>
    /// The Unreal target was the one output whose values nobody checked. Every other language
    /// has a conformance harness whose result is compared field by field against what the
    /// exporter wrote; this one had "does it compile, does it use engine types, does it avoid
    /// throwing" - because running it meant an engine, and a test machine does not have one.
    ///
    /// So it builds against test/fixtures/tools/unreal-stubs, which is enough of CoreMinimal
    /// to run. What that proves and what it does not is written down in the stub header
    /// itself; the short version is that the decoding under test is the generated code's, and
    /// what the stubs supply is storage and formatting.
    ///
    /// C++20, because UE 5.3 made UTF8CHAR a distinct type and the stubs spell it char8_t -
    /// which is the spelling the reader casts to, so building it any other way would check a
    /// cast the engine does not make.
    ///
    /// A `.generated.h` is written empty. UHT produces it during a real build and the header
    /// includes it by name; nothing in it matters here, because the reflection data is what
    /// RunHeaderTool checks and this checks the reading.
    /// </remarks>
    public static ToolResult BuildAndRunOffEngine(
        string workDir, string moduleDir, string accessorName, string harness, string dataDir)
    {
        Directory.CreateDirectory(workDir);

        string publicDir = Path.Combine(moduleDir, "Public");
        string privateDir = Path.Combine(moduleDir, "Private");
        string stubs = Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "unreal-stubs");

        File.WriteAllText(Path.Combine(publicDir, accessorName + ".generated.h"),
            "// Written by the test suite. UHT produces this during a real build; nothing in\n" +
            "// it is needed to read a table, which is what the off-engine harness checks.\n" +
            "#pragma once\n");

        var sources = new[] { harness, Path.Combine(privateDir, accessorName + ".cpp") };
        var includes = new[] { publicDir, stubs };

        // <MODULE>_API, which UnrealBuildTool defines per module and which the generated
        // types carry so they export from a DLL build. Empty here: one executable, nothing
        // to export from.
        string apiMacro = Path.GetFileName(moduleDir).ToUpperInvariant() + "_API";

        string exe = Path.Combine(workDir, OnWindows ? "conformance-unreal.exe" : "conformance-unreal");

        var build = OnWindows
            ? BuildOffEngineWithMsvc(workDir, includes, sources, exe, accessorName, apiMacro)
            : BuildOffEngineWithGcc(workDir, includes, sources, exe, accessorName, apiMacro);

        if (!build.Succeeded)
            return build;

        return Execute(exe, workDir, dataDir);
    }

    private static ToolResult BuildOffEngineWithMsvc(
        string workDir, IReadOnlyList<string> includes, IReadOnlyList<string> sources,
        string exe, string accessorName, string apiMacro)
    {
        var arguments = new List<string>
        {
            "/nologo", "/std:c++20", "/EHsc", "/W3", "/utf-8",
            $"/DTABBIT_ACCESSOR_HEADER=\\\"{accessorName}.h\\\"",
            $"/D{apiMacro}=",
        };

        arguments.AddRange(includes.Select(dir => $"/I \"{dir}\""));
        arguments.AddRange(sources.Select(file => $"\"{file}\""));
        arguments.Add($"/Fo:\"{workDir}\\\\\"");
        arguments.Add($"/Fe:\"{exe}\"");

        return CppToolchain.RunMsvc(workDir, "build-off-engine", arguments);
    }

    private static ToolResult BuildOffEngineWithGcc(
        string workDir, IReadOnlyList<string> includes, IReadOnlyList<string> sources,
        string exe, string accessorName, string apiMacro)
    {
        var arguments = new List<string>
        {
            "-std=c++20", "-Wall", "-Wextra",
            $"-DTABBIT_ACCESSOR_HEADER=\"{accessorName}.h\"",
            $"-D{apiMacro}=",
        };

        foreach (var dir in includes)
        {
            arguments.Add("-I");
            arguments.Add(dir);
        }

        arguments.AddRange(sources);
        arguments.Add("-o");
        arguments.Add(exe);

        return Execute("g++", workDir, arguments.ToArray());
    }

    /// <summary>
    /// Builds the generated updater with UnrealBuildTool, against a real engine.
    /// </summary>
    /// <remarks>
    /// The one gate that compiles Unreal C++ as Unreal compiles it. The off-engine build
    /// beside it uses hand-written stubs, which answer "does this agree with what I think
    /// the engine looks like" - a question that cannot fail usefully, because the same
    /// hand wrote both sides. This one asks the engine.
    ///
    /// It found its keep immediately: `ENGINE_MAJOR_VERSION` is not defined in a Program
    /// target, so the `#if` picking between FTicker and FTSTicker was comparing an
    /// undefined name - which the engine builds as an error and no stub would have
    /// noticed.
    ///
    /// A Program target rather than a game or an editor one, because the updater needs
    /// Core and HTTP and nothing else. An editor target would link the whole engine to
    /// answer the same question in twenty minutes instead of two seconds.
    ///
    /// The program is copied into the engine's Source/Programs, built, run, and removed.
    /// Writing into somebody's engine is not free, so what goes in is one directory named
    /// after this tool and what comes out is that directory plus what UBT put beside it.
    /// </remarks>
    public static ToolResult BuildUpdaterWithUbt(string engineRoot, string moduleDir)
    {
        string build = BuildScript(engineRoot);

        if (!File.Exists(build))
        {
            return new ToolResult
            {
                Succeeded = false,
                Output = $"No {Path.GetFileName(build)} at {build}. TABBIT_UE_ROOT must name an engine.",
            };
        }

        const string Program = "TabbitUpdaterCheck";

        string skeleton = Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "unreal-ubt");
        string programDir = Path.Combine(engineRoot, "Engine", "Source", "Programs", Program);

        try
        {
            if (Directory.Exists(programDir))
                Directory.Delete(programDir, recursive: true);

            Directory.CreateDirectory(Path.Combine(programDir, "Private"));

            foreach (string path in Directory.GetFiles(skeleton))
                File.Copy(path, Path.Combine(programDir, Path.GetFileName(path)));

            File.Copy(Path.Combine(skeleton, "Private", Program + ".cpp"),
                      Path.Combine(programDir, "Private", Program + ".cpp"), overwrite: true);

            // The generated files, not the ones in lib/. What is compiled here is what a
            // consumer's project would get.
            foreach (var (from, to) in new[]
                     {
                         (Path.Combine(moduleDir, "Public", "TabbitUpdater.h"), "TabbitUpdater.h"),
                         (Path.Combine(moduleDir, "Private", "TabbitUpdater.cpp"), "TabbitUpdater.cpp"),
                     })
            {
                if (!File.Exists(from))
                {
                    return new ToolResult
                    {
                        Succeeded = false,
                        Output = $"The module at {moduleDir} has no {Path.GetFileName(from)}. " +
                                 "WriteUpdater has to be on for this scenario.",
                    };
                }

                File.Copy(from, Path.Combine(programDir, "Private", to), overwrite: true);
            }

            // Through cmd on Windows because Build.bat is a batch file and cannot be started
            // as a process; elsewhere Build.sh is executable and is the process.
            var built = OnWindows
                ? Execute("cmd.exe", engineRoot,
                          "/c", build, Program, EnginePlatform, "Development", "-WaitMutex")
                : Execute(build, engineRoot,
                          Program, EnginePlatform, "Development", "-WaitMutex");

            if (!built.Succeeded)
                return built;

            // Built is most of it, but a header that declares what the .cpp does not
            // define links and then does nothing. Running it also checks the two pieces
            // that can be checked without a server: the manifest parser and the hash.
            string exe = Path.Combine(
                engineRoot, "Engine", "Binaries", EnginePlatform, Program + ExeSuffix);

            return Execute(exe, Path.GetDirectoryName(exe));
        }
        finally
        {
            TryDelete(programDir);

            foreach (string leftover in new[]
                     {
                         Path.Combine(engineRoot, "Engine", "Intermediate", "Build", EnginePlatform, Program),
                     })
            {
                TryDelete(leftover);
            }

            // `Program*` rather than `Program.*`: off Windows the executable itself carries
            // no extension, so the pattern that matched every leftover beside it did not
            // match the one thing this certainly wrote.
            //
            // The directory is checked first because this runs in a finally. An engine that
            // never got as far as producing one would otherwise throw from here and replace
            // the real failure with a DirectoryNotFoundException.
            string binaries = Path.Combine(engineRoot, "Engine", "Binaries", EnginePlatform);

            if (Directory.Exists(binaries))
            {
                foreach (string path in Directory.EnumerateFiles(binaries, Program + "*"))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }
    }

    /// <summary>Removes a directory, and does not mind if it cannot.</summary>
    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static ToolResult RunHeaderTool(
        string engineRoot, string moduleDir, string moduleName, string headerName)
    {
        string headerTool = Path.Combine(
            engineRoot, "Engine", "Binaries", EnginePlatform, "UnrealHeaderTool" + ExeSuffix);

        if (!File.Exists(headerTool))
        {
            return new ToolResult
            {
                Succeeded = false,
                Output = $"No UnrealHeaderTool at {headerTool}. TABBIT_UE_ROOT must name a built engine.",
            };
        }

        // One directory per module rather than one for the gate. Four test classes call
        // this and xUnit runs classes in parallel, so a shared directory meant one run
        // deleting what another was writing into - which showed up as a UHT failure that
        // passed when the same test ran on its own. The module name is distinct per call
        // site, and repeats within a class are sequential.
        string workDir = Path.Combine(RepoLayout.OutputDir("_uht"), moduleName);

        if (Directory.Exists(workDir))
            Directory.Delete(workDir, recursive: true);

        string outputDir = Path.Combine(workDir, "Inc", moduleName);
        Directory.CreateDirectory(outputDir);

        string borrowed = FindEngineManifest(engineRoot);

        if (borrowed == null)
        {
            return new ToolResult
            {
                Succeeded = false,
                Output =
                    "No .uhtmanifest naming both CoreUObject and Engine was found under the engine's " +
                    "Intermediate directory. The gate borrows those two module entries from one, because " +
                    "their header lists are curated - globbing the directories pulls in headers the tool " +
                    "rejects. Build the engine editor target once, or point TABBIT_UE_MANIFEST at a " +
                    "manifest that has both.",
            };
        }

        string manifest = Path.Combine(workDir, "TabbitVerify.uhtmanifest");

        File.WriteAllText(manifest,
            Manifest(engineRoot, borrowed, moduleDir, moduleName, headerName, outputDir, workDir));

        // A .uproject is required even though nothing in it is used here; UHT resolves
        // engine paths from it.
        string project = Path.Combine(engineRoot, "Engine", "Engine.uproject");

        return Execute(headerTool, workDir,
            File.Exists(project) ? project : engineRoot,
            manifest,
            "-Unattended",
            "-WarningsAsErrors",
            "-installed");
    }

    /// <summary>
    /// Engine, cut down to the one header the generated module needs from it.
    /// </summary>
    /// <remarks>
    /// The generated Blueprint library derives from UBlueprintFunctionLibrary, so UHT has to
    /// have parsed that declaration before it reaches ours. Borrowing Engine's whole entry
    /// would do it and costs more than it is worth: those 1084 headers reach for
    /// UDeveloperSettings and a long tail behind it, so the manifest grows until it is the
    /// editor - and this gate is asking whether UHT accepts *our* module, not Engine's.
    ///
    /// One header instead. UBlueprintFunctionLibrary's own parent is UObject, which
    /// CoreUObject has already supplied, so the chain stops there.
    ///
    /// The rest of the entry is Engine's own, so the paths and the module type are whatever
    /// the real build said they were.
    /// </remarks>
    private static object OneHeaderOfEngine(IReadOnlyList<JsonElement> borrowed)
    {
        var engine = borrowed.First(module => module.GetProperty("Name").GetString() == "Engine");

        string classes = Path.Combine(
            engine.GetProperty("BaseDirectory").GetString(), "Classes",
            "Kismet", "BlueprintFunctionLibrary.h");

        return new
        {
            Name = "Engine",
            ModuleType = engine.GetProperty("ModuleType").GetString(),
            OverrideModuleType = "None",
            BaseDirectory = engine.GetProperty("BaseDirectory").GetString(),
            IncludeBase = engine.GetProperty("IncludeBase").GetString(),
            OutputDirectory = engine.GetProperty("OutputDirectory").GetString(),
            ClassesHeaders = new[] { classes },
            PublicHeaders = Array.Empty<string>(),
            PrivateHeaders = Array.Empty<string>(),
            GeneratedCPPFilenameBase = engine.GetProperty("GeneratedCPPFilenameBase").GetString(),
            SaveExportedHeaders = false,
            UHTGeneratedCodeVersion = "None",
        };
    }

    /// <summary>
    /// A manifest the engine or a project has already produced, whose CoreUObject and Engine
    /// entries this borrows.
    ///
    /// Borrowed rather than reconstructed: those entries list a curated set of headers, and
    /// globbing the directories instead pulls in ones the tool rejects - the first attempt at
    /// this failed inside ObjectMacros.h, nowhere near the code under test.
    /// </summary>
    private static string FindEngineManifest(string engineRoot)
    {
        string configured = Environment.GetEnvironmentVariable("TABBIT_UE_MANIFEST");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            return configured;

        string intermediate = Path.Combine(engineRoot, "Engine", "Intermediate", "Build");
        if (!Directory.Exists(intermediate))
            return null;

        return Directory.EnumerateFiles(intermediate, "*.uhtmanifest", SearchOption.AllDirectories)
                        .FirstOrDefault(HasTheDependencies);
    }

    /// <summary>
    /// Whether a manifest describes both modules the generated one needs.
    /// </summary>
    /// <remarks>
    /// CoreUObject alone was enough until the target grew a Blueprint function library, whose
    /// parent type is in Engine. Most manifests under Intermediate are for a single program
    /// and have neither; the editor target's has both.
    /// </remarks>
    private static bool HasTheDependencies(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));

            var names = document.RootElement.GetProperty("Modules").EnumerateArray()
                                .Select(module => module.GetProperty("Name").GetString())
                                .ToHashSet(StringComparer.Ordinal);

            return names.Contains("CoreUObject") && names.Contains("Engine");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The manifest UHT reads: CoreUObject as some real build described it, and the
    /// module under test.
    /// </summary>
    private static string Manifest(
        string engineRoot, string borrowedManifest, string moduleDir, string moduleName,
        string headerName, string outputDir, string workDir)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(borrowedManifest));

        var borrowed = source.RootElement.GetProperty("Modules").EnumerateArray().ToList();

        // In dependency order, because UHT resolves a parent type against what it has parsed
        // so far.
        var coreUObject = borrowed.First(module => module.GetProperty("Name").GetString() == "CoreUObject");

        var dependencies = new object[]
        {
            JsonSerializer.Deserialize<object>(coreUObject.GetRawText()),
            OneHeaderOfEngine(borrowed),
        };

        var manifest = new
        {
            IsGameTarget = true,
            RootLocalPath = engineRoot,
            TargetName = "TabbitVerify",
            ExternalDependenciesFile = Path.Combine(workDir, "TabbitVerify.deps"),
            Modules = dependencies.Append<object>(
                new
                {
                    Name = moduleName,
                    ModuleType = "GameRuntime",
                    OverrideModuleType = "None",
                    BaseDirectory = moduleDir,
                    IncludeBase = Path.Combine(moduleDir, "Public"),
                    OutputDirectory = outputDir,
                    ClassesHeaders = Array.Empty<string>(),
                    PublicHeaders = new[] { Path.Combine(moduleDir, "Public", headerName) },
                    PrivateHeaders = Array.Empty<string>(),
                    GeneratedCPPFilenameBase = Path.Combine(outputDir, moduleName + ".gen"),
                    SaveExportedHeaders = true,
                    UHTGeneratedCodeVersion = "None",
                }).ToArray(),
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // UTF-8 explicitly. Without it the child's output is decoded as the system
            // codepage, which on a Korean Windows turned the conformance harness's `é한Ａ`
            // into `챕?쒙샥` - the bytes were right and the reading of them was not, which
            // is the most misleading way for a test to fail.
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var output = new StringBuilder();

        // Kept apart as well as together: a harness's answer is on stdout and has to be
        // parsed, while `Output` is the pair of them for a failure message.
        var standardOutput = new StringBuilder();

        using var process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                return;

            standardOutput.AppendLine(e.Data);
            output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(milliseconds: 600_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("UnrealHeaderTool did not finish within ten minutes.");
        }

        process.WaitForExit();

        return new ToolResult
        {
            Succeeded = process.ExitCode == 0,
            StdOut = standardOutput.ToString(),
            Output = output.ToString(),
        };
    }
}
