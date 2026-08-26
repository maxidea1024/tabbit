using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Tabbit.Tests;

/// <summary>
/// Drives the language toolchains: the conformance harnesses, and the compile-only
/// checks that ask whether generated code is even valid.
///
/// One method per language, each about as long as the harness it drives. Adding a
/// language means adding one of each, which is the whole point of the corpus: the
/// comparison in ConformanceTests is language-agnostic and does not grow.
///
/// The Compile* methods below exist for the reserved-word fixture, where the question
/// is not what a value reads back as but whether the file compiles at all. Finding a
/// toolchain is the same problem for both, and it lives here once.
/// </summary>
internal static class ConformanceHarness
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    private static string HarnessDir(string language)
        => Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "conformance", language);

    /// <summary>
    /// The variable every harness reads its MAC key out of.
    /// </summary>
    /// <remarks>
    /// One variable set in one place, rather than an argument threaded through thirteen
    /// runners: what each harness does with it is three lines, and the runners stay the
    /// shape they were.
    ///
    /// It is set on every harness process whether or not that harness reads it, so a
    /// language whose harness has not been taught about the key yet reads the corpus
    /// without checking it - which is what `ConformanceMacTests` is there to catch, since
    /// nothing else would.
    /// </remarks>
    public const string MacKeyVariable = "TABBIT_TEST_TCB_MAC_KEY";

    /// <summary>
    /// The corpus's MAC key, as the recipe's own key file holds it.
    /// </summary>
    /// <remarks>
    /// Read from that file rather than repeated here, so the converter and the readers
    /// cannot drift into signing and checking with different keys - which would look
    /// exactly like a broken port.
    /// </remarks>
    public static string MacKey => _macKey.Value;

    private static readonly Lazy<string> _macKey = new Lazy<string>(() =>
        File.ReadAllText(Path.Combine(
            RepoLayout.Root, "test", "fixtures", "keys", "conformance-mac.key")).Trim());

    /// <summary>
    /// Puts the corpus's MAC key in this process's own environment, so that every harness
    /// inherits it however it was launched.
    /// </summary>
    /// <remarks>
    /// Four of the thirteen do not go through <see cref="Execute"/> at all - TypeScript,
    /// C, C++ and Unreal each have a toolchain of their own that compiles before it runs -
    /// and setting the variable in each of those is four places to forget. A child process
    /// inherits its parent's environment, so setting it once here reaches all of them.
    ///
    /// Safe to leak into every other subprocess the suite starts: the converter reads a
    /// variable only when a recipe names one, and no recipe names this. The corpus points
    /// at the key file instead, which is what lets any test convert it without arranging
    /// anything.
    /// </remarks>
    static ConformanceHarness()
        => Environment.SetEnvironmentVariable(MacKeyVariable, MacKey);

    /// <summary>
    /// Where the data a harness reads comes from.
    /// </summary>
    /// <remarks>
    /// Usually the same scenario the reader was generated from. Passing a different one is
    /// the skew question: code built against one schema, a file written by another. Only C#
    /// could be asked that until the corpus's own drivers were pointed at a second
    /// generation of its data - see SchemaSkewCorpusTests.
    /// </remarks>
    private static string BinaryDir(string scenario)
        => Path.Combine(RepoLayout.OutputDir(scenario), "binary");

    public static ToolResult RunCsharp(string scenario, string dataScenario = null)
    {
        string workDir = WorkDir(scenario, "csharp");

        var build = Execute("dotnet", RepoLayout.Root,
            "build",
            Path.Combine(HarnessDir("csharp"), "conformance-csharp.csproj"),
            "--nologo",
            $"-p:GeneratedDir={Generated(scenario, "csharp")}",
            "-o", workDir);

        if (!build.Succeeded)
            return build;

        return Execute(Path.Combine(workDir, OnWindows ? "conformance-csharp.exe" : "conformance-csharp"),
                       workDir, BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>
    /// Whether the generated Unreal module can be built off-engine.
    /// </summary>
    /// <remarks>
    /// The same C++ toolchain the C++ harness needs. There is no engine involved, which is
    /// the point - the module builds against the stubs in tools/unreal-stubs.
    /// </remarks>
    public static bool UnrealOffEngineIsAvailable(out string reason)
    {
        bool available = CppToolchain.IsAvailable(out reason);

        if (!available)
            reason = $"A C++ compiler is required to build the generated Unreal off-engine. {reason}";

        return available;
    }

    /// <summary>
    /// Builds and runs the Unreal harness against the stubs, with no engine.
    /// </summary>
    public static ToolResult RunUnreal(string scenario, string dataScenario = null)
        => UnrealToolchain.BuildAndRunOffEngine(
            WorkDir(scenario, "unreal"),
            moduleDir: Path.Combine(Generated(scenario, "unreal"), "Conformance"),
            accessorName: "ConformanceData",
            harness: Path.Combine(HarnessDir("unreal"), "main.cpp"),
            dataDir: BinaryDir(dataScenario ?? scenario));

    public static ToolResult RunCpp(string scenario, string dataScenario = null)
    {
        string workDir = WorkDir(scenario, "cpp");

        var build = CppToolchain.CompileHarness(
            workDir,
            includeDir: Generated(scenario, "cpp"),
            source: Path.Combine(HarnessDir("cpp"), "main.cpp"),
            accessorName: "ConformanceAccessor",
            exeName: "conformance-cpp");

        if (!build.Succeeded)
            return build;

        return Execute(Path.Combine(workDir, OnWindows ? "conformance-cpp.exe" : "conformance-cpp"),
                       workDir, BinaryDir(dataScenario ?? scenario));
    }

    public static ToolResult RunTypescript(string scenario, string dataScenario = null)
    {
        // The harness is copied in beside the generated modules rather than importing
        // across directories, because its import paths are the ones a consumer would
        // write and those are relative to the generated output.
        string generatedDir = Generated(scenario, "typescript");
        string entry = Path.Combine(generatedDir, "conformance-main.ts");

        File.Copy(Path.Combine(HarnessDir("ts"), "main.ts"), entry, overwrite: true);

        return TypescriptToolchain.RunScript(entry, BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a Go toolchain is on the path.</summary>
    public static bool GoIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute("go", RepoLayout.Root, "version");
            reason = probe.Succeeded ? null : $"`go version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`go` could not be started: {ex.Message}";
            return false;
        }
    }

    public static ToolResult RunGo(string scenario, string dataScenario = null)
    {
        // The harness goes inside the generated module, as a package of its own, because
        // Go has no relative imports and the generated code is only importable from
        // within the module its go.mod declares.
        string moduleDir = Generated(scenario, "go");
        string harnessDir = Path.Combine(moduleDir, "harness");

        Directory.CreateDirectory(harnessDir);
        File.Copy(Path.Combine(HarnessDir("go"), "main.go"),
                  Path.Combine(harnessDir, "main.go"), overwrite: true);

        return Execute("go", moduleDir, "run", "./harness", BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a Rust toolchain is on the path.</summary>
    public static bool RustIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute("cargo", RepoLayout.Root, "--version");
            reason = probe.Succeeded ? null : $"`cargo --version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`cargo` could not be started: {ex.Message}";
            return false;
        }
    }

    public static ToolResult RunRust(string scenario, string dataScenario = null)
    {
        // As a binary inside the generated crate, for the same reason the Go harness is
        // a package inside the generated module: that is the only place the generated
        // types are importable from.
        string crateDir = Generated(scenario, "rust");
        string binDir = Path.Combine(crateDir, "src", "bin");

        Directory.CreateDirectory(binDir);
        File.Copy(Path.Combine(HarnessDir("rust"), "harness.rs"),
                  Path.Combine(binDir, "harness.rs"), overwrite: true);

        return Execute("cargo", crateDir,
                       "run", "--quiet", "--bin", "harness", "--", BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a Python interpreter is on the path.</summary>
    public static bool PythonIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute(PythonExecutable, RepoLayout.Root, "--version");
            reason = probe.Succeeded ? null : $"`python --version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`{PythonExecutable}` could not be started: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// `python` on Windows, `python3` elsewhere - the name that exists on each.
    /// </summary>
    private static string PythonExecutable => OnWindows ? "python" : "python3";

    public static ToolResult RunPython(string scenario, string dataScenario = null)
    {
        // Beside the generated package rather than inside it, so the package's own
        // directory holds only generated files and the import reads as a consumer's
        // would.
        string root = Generated(scenario, "python");
        string harness = Path.Combine(root, "harness.py");

        File.Copy(Path.Combine(HarnessDir("python"), "harness.py"), harness, overwrite: true);

        // Python writes its own standard output through an encoding of its choosing,
        // which on Windows is the console codepage and mangles anything non-ASCII.
        var environment = new Dictionary<string, string> { { "PYTHONIOENCODING", "utf-8" } };

        return Execute(PythonExecutable, root, environment, "harness.py", BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>
    /// Whether the Lua host can be built - which is the C toolchain question: nothing
    /// Lua is looked for on PATH. The suite compiles the vendored interpreter, the
    /// embedder and the generated native module into one executable, the way a game
    /// engine embeds Lua. spec/targets/lua-language-support.md.
    /// </summary>
    public static bool LuaIsAvailable(out string reason) => LuaToolchain.IsAvailable(out reason);

    public static ToolResult RunLua(
        string scenario, string dataScenario = null,
        IReadOnlyDictionary<string, string> environment = null)
    {
        // From the generated output directory, so `require("tables")` resolves through
        // the default package.path; the harness file itself stays where it is.
        string root = Generated(scenario, "lua");

        return Execute(LuaToolchain.HostExecutable, root, environment,
                       Path.Combine(HarnessDir("lua"), "harness.lua"),
                       BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>
    /// Where a LuaJIT executable is, when one is here to run the FFI backend under.
    /// </summary>
    /// <remarks>
    /// An opt-in gate like the Unreal one: machines without the variable skip it, and
    /// CI that wants it sets `TABBIT_LUAJIT` to a `luajit` executable. The run is
    /// keyless - a keyed run needs the native module built against that LuaJIT's own
    /// import library, which is a consumer's build system's job rather than this
    /// suite's - and a keyless reader reading a signed corpus is a legal path of its
    /// own, so the values still have to match.
    /// </remarks>
    public static string LuaJitExecutable => Environment.GetEnvironmentVariable("TABBIT_LUAJIT");

    public static ToolResult RunLuaJit(string scenario, string dataScenario = null)
    {
        string root = Generated(scenario, "lua");

        var environment = new Dictionary<string, string> { [MacKeyVariable] = "" };

        return Execute(LuaJitExecutable, root, environment,
                       Path.Combine(HarnessDir("lua"), "harness.lua"),
                       BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a JDK is on the path.</summary>
    public static bool JavaIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute("javac", RepoLayout.Root, "-version");
            reason = probe.Succeeded ? null : $"`javac -version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`javac` could not be started: {ex.Message}";
            return false;
        }
    }

    public static ToolResult RunJava(string scenario, string dataScenario = null)
    {
        // Beside the generated packages, because a Java source tree is rooted at the
        // package directories and the harness is in the default package.
        string root = Generated(scenario, "java");
        string classes = Path.Combine(root, "classes");

        File.Copy(Path.Combine(HarnessDir("java"), "Harness.java"),
                  Path.Combine(root, "Harness.java"), overwrite: true);

        Directory.CreateDirectory(classes);

        var sources = Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories).ToList();

        var arguments = new List<string> { "-encoding", "UTF-8", "-d", classes };
        arguments.AddRange(sources);

        var build = Execute("javac", root, arguments.ToArray());
        if (!build.Succeeded)
            return build;

        return Execute("java", root, "-cp", classes, "Harness", BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a Kotlin compiler and a JVM to run it on are both here.</summary>
    public static bool KotlinIsAvailable(out string reason)
    {
        if (!JavaIsAvailable(out string why))
        {
            reason = $"The Kotlin compiler runs on a JVM. {why}";
            return false;
        }

        if (KotlinCompilerJar() == null)
        {
            reason = "`kotlin-compiler.jar` was not found on the path or in a known install.";
            return false;
        }

        reason = null;
        return true;
    }

    public static ToolResult RunKotlin(string scenario, string dataScenario = null)
    {
        // Beside the generated package, in the default package, for the same reason the
        // Java harness is: a JVM source tree is rooted at the package directories.
        string root = Generated(scenario, "kotlin");
        string jar = Path.Combine(root, "harness.jar");

        File.Copy(Path.Combine(HarnessDir("kotlin"), "Harness.kt"),
                  Path.Combine(root, "Harness.kt"), overwrite: true);

        // Through the compiler jar rather than the `kotlinc` launcher, which on Windows
        // is a batch file and cannot be started as a process at all.
        var arguments = new List<string>
        {
            "-jar", KotlinCompilerJar(),
            "-nowarn",

            // A fat jar, so running it needs nothing but a JVM.
            "-include-runtime",
            "-d", jar,
        };

        arguments.AddRange(Directory.EnumerateFiles(root, "*.kt", SearchOption.AllDirectories));

        var build = Execute("java", root, arguments.ToArray());
        if (!build.Succeeded)
            return build;

        return Execute("java", root, "-jar", jar, BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a Swift toolchain is here.</summary>
    /// <remarks>
    /// One probe for both of this language's gates: the harness builds through SwiftPM
    /// because verifying the corpus MAC needs a crypto package, and the compile-only check
    /// runs `swiftc` over the same output with no package at all. Both need the toolchain
    /// and nothing else.
    /// </remarks>
    public static bool SwiftIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute(SwiftTool("swift"), RepoLayout.Root, SwiftEnvironment(), "--version");

            reason = probe.Succeeded
                ? null
                : $"`swift --version` failed.{Environment.NewLine}{probe.Output}";

            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`swift` could not be started: {ex.Message}. "
                   + "Install a toolchain from https://www.swift.org/install/ - on Windows "
                   + "the Visual Studio C++ tools and a complete Windows SDK have to be "
                   + "there first, and a partial SDK shows up as a missing UCRT header "
                   + "rather than as a missing toolchain.";
            return false;
        }
    }

    public static ToolResult RunSwift(string scenario, string dataScenario = null)
    {
        // Into the generated directory rather than beside it: the manifest declares one
        // target over the files as they are, which is the layout the generator writes and
        // the one a consumer drops into a project.
        string root = Generated(scenario, "swift");

        // `main.swift` and not `harness.swift`: top-level statements are only allowed in a
        // file of that name, and every other harness in this corpus is top-level code.
        File.Copy(Path.Combine(HarnessDir("swift"), "main.swift"),
                  Path.Combine(root, "main.swift"), overwrite: true);

        File.Copy(Path.Combine(HarnessDir("swift"), "Package.swift"),
                  Path.Combine(root, "Package.swift"), overwrite: true);

        // Built into a scratch directory outside the scenario's output, for two reasons.
        // SwiftPM's own `.build` holds the package checkouts, whose files come out of git
        // read-only - and the next conversion's clean of the output directory cannot delete
        // those, which fails a test that has nothing to do with Swift. Keeping it out also
        // keeps it between runs, so swift-crypto is fetched once rather than every time.
        string scratch = SwiftScratch(scenario);

        var build = Execute(
            SwiftTool("swift"), root, SwiftEnvironment(), "build", "--scratch-path", scratch);

        if (!build.Succeeded)
            return build;

        // Asked for rather than assembled: the layout under a scratch path is the build
        // triple and then the configuration, and on the platforms that have symlinks there is
        // a `debug` link beside it that Windows does not get. One call spares this method
        // knowing any of that.
        var binPath = Execute(
            SwiftTool("swift"), root, SwiftEnvironment(),
            "build", "--scratch-path", scratch, "--show-bin-path");

        if (!binPath.Succeeded)
            return binPath;

        string product = Path.Combine(
            binPath.StdOut.Trim(), OnWindows ? "harness.exe" : "harness");

        return Execute(product, root, SwiftEnvironment(), BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>
    /// Type-checks the generated Swift with no package at all, warnings included.
    /// </summary>
    /// <remarks>
    /// Two things at once, and the second is the reason this is not just a compile check.
    ///
    /// The reader gets its HMAC-SHA-256 from CryptoKit on Apple platforms and from
    /// swift-crypto elsewhere, and when neither is present it still has to compile - a
    /// project that reads plain files should not be made to add a package. That third state
    /// is only ever exercised here, because the harness build has the package.
    ///
    /// And warnings are errors, because generated code lands in somebody else's build. A
    /// subnormal literal was a warning this found: it parses to the right value and would
    /// have failed a consumer who builds with warnings as errors.
    /// </remarks>
    public static ToolResult CompileSwift(string scenario)
    {
        string root = Generated(scenario, "swift");

        var sources = Directory
            .EnumerateFiles(root, "*.swift", SearchOption.AllDirectories)
            .Where(file => !file.Contains(".build", StringComparison.Ordinal))
            .Where(file => Path.GetFileName(file) != "Package.swift")

            // The harness, which is top-level code and belongs to the other gate.
            .Where(file => Path.GetFileName(file) != "main.swift")
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        // Both language modes. Swift 6 turns concurrency checks that are warnings in 5 into
        // errors, and a consuming project chooses its own mode - so generated code that only
        // builds in one of them is generated code that does not build. This found a real
        // one: a `uuid` constant is a static of the reader's `Uuid`, which in Swift 6 has to
        // be `Sendable` and was not.
        ToolResult check = null;

        foreach (string mode in new[] { null, "6" })
        {
            var arguments = new List<string> { "-typecheck", "-warnings-as-errors" };

            if (mode != null)
            {
                arguments.Add("-swift-version");
                arguments.Add(mode);
            }

            arguments.AddRange(sources);

            check = Execute(SwiftTool("swiftc"), root, SwiftEnvironment(), arguments.ToArray());

            if (!check.Succeeded)
                return check;
        }

        return check;
    }

    /// <summary>
    /// The environment a Swift process needs beyond the path, or null where it needs none.
    /// </summary>
    /// <remarks>
    /// Windows only, and one variable: `SDKROOT`, which the installer writes into the user's
    /// environment. A process that inherited its environment from a shell started before the
    /// install does not have it, and without it `swiftc` cannot find the standard library -
    /// which reads as a broken toolchain rather than as a stale shell.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> SwiftEnvironment()
    {
        if (!OnWindows)
            return null;

        var environment = new Dictionary<string, string>();

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SDKROOT")))
        {
            foreach (string sdk in WindowsSwiftSdks())
            {
                if (Directory.Exists(sdk))
                {
                    environment["SDKROOT"] = sdk;
                    break;
                }
            }
        }

        // The toolchain's directory and the runtime's, when the path does not already have
        // them. `swift.exe` loads DLLs out of both, so starting it by its full path out of a
        // shell that predates the install fails with a missing DLL rather than with anything
        // that names Swift - which is a worse way to find out than either of the two this
        // method exists to prevent.
        var directories = WindowsSwiftToolchains()
                          .Concat(WindowsSwiftRuntimes())
                          .Where(Directory.Exists)
                          .ToList();

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";

        var already = path.Split(Path.PathSeparator)
                          .Select(entry => entry.TrimEnd('\\'))
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = directories
                      .Where(dir => !already.Contains(dir.TrimEnd('\\')))
                      .ToList();

        if (missing.Count > 0)
            environment["PATH"] = string.Join(Path.PathSeparator, missing) + Path.PathSeparator + path;

        return environment.Count == 0 ? null : environment;
    }

    /// <summary>Where the Windows installer puts the runtime DLLs, newest first.</summary>
    private static IEnumerable<string> WindowsSwiftRuntimes()
    {
        if (!OnWindows)
            yield break;

        string runtimes = Path.Combine(
            HomeDir, "AppData", "Local", "Programs", "Swift", "Runtimes");

        if (!Directory.Exists(runtimes))
            yield break;

        var versions = Directory.EnumerateDirectories(runtimes)
                                .OrderByDescending(dir => dir, StringComparer.Ordinal);

        foreach (string version in versions)
            yield return Path.Combine(version, "usr", "bin");
    }

    /// <summary>
    /// Compiles a set of Swift sources into one executable, with no package involved.
    /// </summary>
    /// <remarks>
    /// For the updater's own gate, which is the one Swift program in the suite that is not
    /// generated code: the updater takes Foundation and nothing else, so it compiles as a
    /// plain `swiftc` invocation - and that it still does is half of what the gate asks.
    ///
    /// The entry point has to be in a file called `main.swift`; Swift allows top-level
    /// statements nowhere else.
    /// </remarks>
    public static ToolResult CompileSwiftProgram(
        string workDir, string exeName, params string[] sources)
    {
        var arguments = new List<string> { "-warnings-as-errors", "-o", ExeName(exeName) };
        arguments.AddRange(sources);

        return Execute(SwiftTool("swiftc"), workDir, SwiftEnvironment(), arguments.ToArray());
    }

    /// <summary>Runs a program built by <see cref="CompileSwiftProgram"/>.</summary>
    /// <remarks>
    /// Through <see cref="SwiftEnvironment"/> like the compiler, because the executable links
    /// against the Swift runtime and on Windows that lives in a directory of its own.
    /// </remarks>
    public static ToolResult RunSwiftProgram(
        string workDir, string exeName, params string[] args)
        => Execute(
            Path.Combine(workDir, ExeName(exeName)), workDir, SwiftEnvironment(), args);

    private static string ExeName(string name) => OnWindows ? name + ".exe" : name;

    /// <summary>
    /// Where SwiftPM builds, which is deliberately not under the scenario's output.
    /// </summary>
    /// <remarks>
    /// Not cleared: the package checkouts under it are what make a second run offline, and
    /// SwiftPM decides for itself what to rebuild.
    /// </remarks>
    private static string SwiftScratch(string scenario)
    {
        string dir = Path.Combine(RepoLayout.OutputDir("_swift"), scenario);

        Directory.CreateDirectory(dir);

        return dir;
    }

    /// <summary>
    /// A Swift tool by name, or its full path where the name is not on this process's path.
    /// </summary>
    /// <remarks>
    /// The Windows installer puts the toolchain on the user's path, which a shell started
    /// before the install does not have - and a test run from that shell would report a
    /// missing toolchain when what is missing is a restart. The same courtesy the Kotlin and
    /// Dart runners already extend to their own.
    /// </remarks>
    private static string SwiftTool(string name)
    {
        if (FindOnPath(name) != null)
            return name;

        foreach (string directory in WindowsSwiftToolchains())
        {
            string candidate = Path.Combine(directory, name + ".exe");

            if (File.Exists(candidate))
                return candidate;
        }

        // Unfound, so the name goes back as it came and the failure names the tool.
        return name;
    }

    /// <summary>Where the Windows installer puts the toolchains, newest first.</summary>
    private static IEnumerable<string> WindowsSwiftToolchains()
    {
        if (!OnWindows)
            yield break;

        string toolchains = Path.Combine(
            HomeDir, "AppData", "Local", "Programs", "Swift", "Toolchains");

        if (!Directory.Exists(toolchains))
            yield break;

        var versions = Directory.EnumerateDirectories(toolchains)
                                .OrderByDescending(dir => dir, StringComparer.Ordinal);

        foreach (string version in versions)
            yield return Path.Combine(version, "usr", "bin");
    }

    /// <summary>Where the Windows installer puts the platform SDKs, newest first.</summary>
    private static IEnumerable<string> WindowsSwiftSdks()
    {
        string platforms = Path.Combine(
            HomeDir, "AppData", "Local", "Programs", "Swift", "Platforms");

        if (!Directory.Exists(platforms))
            yield break;

        var versions = Directory.EnumerateDirectories(platforms)
                                .OrderByDescending(dir => dir, StringComparer.Ordinal);

        foreach (string version in versions)
        {
            yield return Path.Combine(
                version, "Windows.platform", "Developer", "SDKs", "Windows.sdk");
        }
    }

    /// <summary>Whether a Ruby interpreter is here.</summary>
    public static bool RubyIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute(RubyExecutable, RepoLayout.Root, "--version");
            reason = probe.Succeeded ? null : $"`ruby --version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`{RubyExecutable}` could not be started: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Runs a Ruby script from a directory of the caller's choosing.
    /// </summary>
    /// <remarks>
    /// For the updater gate, whose work directory is neither a scenario's output nor a
    /// conformance harness - it holds the shipped updater and a driver, and nothing
    /// generated at all.
    /// </remarks>
    public static ToolResult RunRubyScript(string workingDirectory, string script, params string[] args)
    {
        var arguments = new List<string> { script };
        arguments.AddRange(args);

        return Execute(RubyExecutable, workingDirectory, arguments.ToArray());
    }

    /// <summary>
    /// Compiles every Java source under a directory into `classes` beside them.
    /// </summary>
    /// <remarks>
    /// For the updater gate, whose work directory holds the shipped updater and a
    /// driver rather than anything generated.
    /// </remarks>
    public static ToolResult CompileJavaSources(string root)
    {
        var arguments = new List<string> { "-encoding", "UTF-8", "-d", Path.Combine(root, "classes") };
        arguments.AddRange(Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories));

        return Execute("javac", root, arguments.ToArray());
    }

    /// <summary>Runs the `Main` class compiled by <see cref="CompileJavaSources"/>.</summary>
    public static ToolResult RunJavaMain(string root, params string[] args)
    {
        var arguments = new List<string> { "-cp", Path.Combine(root, "classes"), "Main" };
        arguments.AddRange(args);

        return Execute("java", root, arguments.ToArray());
    }

    /// <summary>
    /// Compiles every Kotlin source under a directory into a runnable jar.
    /// </summary>
    /// <remarks>
    /// A fat jar (`-include-runtime`), so running it needs nothing but a JVM - the same
    /// shape the conformance harness uses, for the same reason.
    /// </remarks>
    public static ToolResult CompileKotlinJar(string root, string jarName)
    {
        var arguments = new List<string>
        {
            "-jar", KotlinCompilerJar(),
            "-nowarn",
            "-include-runtime",
            "-d", Path.Combine(root, jarName),
        };

        arguments.AddRange(Directory.EnumerateFiles(root, "*.kt", SearchOption.AllDirectories));

        return Execute("java", root, arguments.ToArray());
    }

    /// <summary>Runs a jar built by <see cref="CompileKotlinJar"/>.</summary>
    public static ToolResult RunJar(string root, string jarName, params string[] args)
    {
        var arguments = new List<string> { "-jar", Path.Combine(root, jarName) };
        arguments.AddRange(args);

        return Execute("java", root, arguments.ToArray());
    }

    /// <summary>Builds a crate in a directory of the caller's choosing.</summary>
    public static ToolResult CargoBuild(string crateDir)
        => Execute("cargo", crateDir, "build", "--quiet");

    /// <summary>Runs a binary of a crate built by <see cref="CargoBuild"/>.</summary>
    public static ToolResult CargoRun(string crateDir, string binary, params string[] args)
    {
        var arguments = new List<string> { "run", "--quiet", "--bin", binary, "--" };
        arguments.AddRange(args);

        return Execute("cargo", crateDir, arguments.ToArray());
    }

    /// <summary>Runs a PHP script from a directory of the caller's choosing.</summary>
    public static ToolResult RunPhpScript(string workingDirectory, string script, params string[] args)
    {
        var arguments = new List<string> { script };
        arguments.AddRange(args);

        return Execute(PhpExecutable, workingDirectory, arguments.ToArray());
    }

    /// <summary>Runs a Dart program from a directory of the caller's choosing.</summary>
    public static ToolResult RunDartScript(string workingDirectory, string script, params string[] args)
    {
        var arguments = new List<string> { "run", script };
        arguments.AddRange(args);

        return Execute(DartExecutable, workingDirectory, arguments.ToArray());
    }

    public static ToolResult RunRuby(string scenario, string dataScenario = null)
    {
        // Beside the generated file, because `require_relative` resolves against the
        // requiring file and that is the import a consumer would write.
        string root = Generated(scenario, "ruby");

        File.Copy(Path.Combine(HarnessDir("ruby"), "harness.rb"),
                  Path.Combine(root, "harness.rb"), overwrite: true);

        return Execute(RubyExecutable, root, "harness.rb", BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>
    /// Whether a C compiler is here.
    ///
    /// The same one the C++ gate uses - MSVC on Windows, gcc elsewhere - because a
    /// machine with one has the other, and a second discovery routine would be a second
    /// thing to get wrong.
    /// </summary>
    public static bool CIsAvailable(out string reason) => CToolchain.IsAvailable(out reason);

    public static ToolResult RunC(string scenario, string dataScenario = null)
    {
        string workDir = WorkDir(scenario, "c");
        string generated = Generated(scenario, "c");

        // Every generated .c, not a named one.
        //
        // The target used to write exactly one, so naming it was the same thing as building
        // the output. Now it writes a source per table, one per constant set that has a
        // value a header cannot hold, and one whose only job is the reader's implementation -
        // and a list of names here would have quietly stopped covering them, which is how a
        // gate ends up proving less than it reads as proving.
        var build = CToolchain.CompileHarness(
            workDir,
            includeDir: generated,
            source: Path.Combine(HarnessDir("c"), "main.c"),
            accessorHeader: "ConformanceData.h",
            sources: Directory.GetFiles(generated, "*.c", SearchOption.AllDirectories)
                              .OrderBy(path => path).ToArray(),
            exeName: "conformance-c");

        if (!build.Succeeded)
            return build;

        return Execute(Path.Combine(workDir, OnWindows ? "conformance-c.exe" : "conformance-c"),
                       workDir, BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>Whether a PHP interpreter is here.</summary>
    public static bool PhpIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute(PhpExecutable, RepoLayout.Root, "--version");
            reason = probe.Succeeded ? null : $"`php --version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`{PhpExecutable}` could not be started: {ex.Message}";
            return false;
        }
    }

    public static ToolResult RunPhp(string scenario, string dataScenario = null)
    {
        // Beside the generated file, because `require_once __DIR__ . '/...'` resolves
        // against the including file and that is the import a consumer would write.
        string root = Generated(scenario, "php");

        File.Copy(Path.Combine(HarnessDir("php"), "harness.php"),
                  Path.Combine(root, "harness.php"), overwrite: true);

        // serialize_precision -1 is "as many digits as the value needs and no more".
        // The default rounds to 14 significant digits, which loses the corpus's float
        // boundaries - and would look like a reader defect rather than a printing one.
        //
        // No openssl here, deliberately. The corpus's files are not sealed, and the reader
        // claims that reading one needs nothing but core - so running this with the
        // extension off is what keeps that claim a fact rather than a comment. The gate
        // that does need it turns it on for itself.
        return Execute(PhpExecutable, root,
                       "-d", "serialize_precision=-1", "harness.php", BinaryDir(dataScenario ?? scenario));
    }

    /// <summary>
    /// Whether this interpreter can open an encrypted table, and the command-line settings
    /// it takes to get there.
    /// </summary>
    /// <remarks>
    /// The generated PHP reader takes ChaCha20 from ext-openssl. On a distribution build
    /// that is compiled in and there is nothing to do; on the Windows package it is a DLL
    /// beside the interpreter and no `php.ini` exists to name it, so it has to be asked for
    /// on the command line - and `extension=openssl` alone is not enough, because the
    /// extension directory compiled into that build points at an install path that is not
    /// where the package put it.
    ///
    /// Probed rather than assumed, in that order, because passing `extension=openssl` to a
    /// build that already has it is a start-up warning on every line of output.
    /// </remarks>
    /// <param name="settings">
    /// What to pass ahead of the script - empty when the interpreter already has it.
    /// </param>
    public static bool PhpOpensslIsAvailable(out string[] settings, out string reason)
    {
        settings = Array.Empty<string>();

        if (Probe(settings))
        {
            reason = null;
            return true;
        }

        string extensionDir = PhpExtensionDir();

        if (extensionDir != null)
        {
            var withExtension = new[] { "-d", "extension_dir=" + extensionDir, "-d", "extension=openssl" };

            if (Probe(withExtension))
            {
                settings = withExtension;
                reason = null;
                return true;
            }
        }

        reason =
            $"`{PhpExecutable}` cannot load ext-openssl, which the generated reader needs to open a "
            + $"sealed table. Tried it as compiled in, and then from `{extensionDir ?? "an unknown directory"}`.";

        return false;

        static bool Probe(string[] settings)
        {
            try
            {
                var result = Execute(PhpExecutable, RepoLayout.Root,
                    settings.Concat(new[]
                    {
                        "-r", "echo function_exists('openssl_decrypt') ? 'openssl-ready' : 'openssl-absent';",
                    }).ToArray());

                return result.StdOut.Contains("openssl-ready");
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Where the interpreter's own extensions sit: `ext` beside the binary, which is the
    /// layout of every Windows PHP package.
    /// </summary>
    /// <remarks>
    /// Asked of the interpreter rather than derived from how it was resolved, because
    /// `PHP_BINARY` is the real path whatever a launcher or a PATH entry did to get there.
    /// </remarks>
    private static string PhpExtensionDir()
    {
        try
        {
            var result = Execute(PhpExecutable, RepoLayout.Root, "-r", "echo PHP_BINARY;");

            string binary = result.StdOut.Trim();

            if (binary.Length == 0 || !File.Exists(binary))
                return null;

            return Path.Combine(Path.GetDirectoryName(binary), "ext");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies a read-back harness in beside a scenario's generated PHP and runs it on the
    /// binary the exporter wrote, returning what it printed.
    /// </summary>
    /// <remarks>
    /// The same placement as <see cref="RunPhp"/> and for the same reason: the harness's
    /// `require_once __DIR__ . '/...'` is the include a consumer would write, and it
    /// resolves against the generated accessor's own directory.
    ///
    /// A harness of its own rather than the conformance one, because a read-back harness
    /// names the tables of the fixture it reads - there is no generic driver.
    /// </remarks>
    /// <param name="harness">
    /// A directory under `test/fixtures/tools/` holding `harness.php`.
    /// </param>
    /// <param name="settings">
    /// Interpreter settings to pass ahead of the script, from
    /// <see cref="PhpOpensslIsAvailable"/>.
    /// </param>
    /// <param name="extraArgs">
    /// Further arguments for the harness, after the directory - a key, for the gate whose
    /// subject is something the load path is given rather than something it reads.
    /// </param>
    public static ToolResult ReadBackPhp(
        string scenario, string harness, string[] settings, params string[] extraArgs)
    {
        string root = Generated(scenario, "php");

        File.Copy(Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", harness, "harness.php"),
                  Path.Combine(root, "harness.php"), overwrite: true);

        var arguments = new List<string>(settings ?? Array.Empty<string>());

        // As in the conformance run: enough digits that a float survives being printed.
        arguments.Add("-d");
        arguments.Add("serialize_precision=-1");

        arguments.Add("harness.php");
        arguments.Add(BinaryDir(scenario));
        arguments.AddRange(extraArgs ?? Array.Empty<string>());

        return Execute(PhpExecutable, root, arguments.ToArray());
    }

    /// <summary>Whether a Dart SDK is here.</summary>
    public static bool DartIsAvailable(out string reason)
    {
        try
        {
            var probe = Execute(DartExecutable, RepoLayout.Root, "--version");
            reason = probe.Succeeded ? null : $"`dart --version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }
        catch (Exception ex)
        {
            reason = $"`{DartExecutable}` could not be started: {ex.Message}";
            return false;
        }
    }

    public static ToolResult RunDart(string scenario, string dataScenario = null)
    {
        // Beside the generated library, whose import of the reader is relative.
        string root = Generated(scenario, "dart");

        File.Copy(Path.Combine(HarnessDir("dart"), "harness.dart"),
                  Path.Combine(root, "harness.dart"), overwrite: true);

        return Execute(DartExecutable, root, "run", "harness.dart", BinaryDir(dataScenario ?? scenario));
    }

    // -------------------------------------------------- compile-only checks

    /// <summary>
    /// Compiles a scenario's generated Go. Nothing is run: the question is whether the
    /// names the generator chose are legal.
    /// </summary>
    public static ToolResult CompileGo(string scenario)
        => Execute("go", Generated(scenario, "go"), "build", "./...");

    public static ToolResult CompileRust(string scenario)
        => Execute("cargo", Generated(scenario, "rust"), "build", "--quiet");

    /// <summary>
    /// Byte-compiles the generated Python.
    ///
    /// A name that collides with a keyword is a syntax error there - `self.class = x`
    /// does not parse - so compiling is the whole check.
    /// </summary>
    public static ToolResult CompilePython(string scenario)
        => Execute(PythonExecutable, Generated(scenario, "python"), "-m", "compileall", "-q", ".");

    /// <summary>
    /// Runs a snippet against a scenario's generated Python package, from the directory the
    /// package sits in so the import resolves.
    /// </summary>
    /// <remarks>
    /// For the questions a harness would be too much for - whether a parameter is wired
    /// through, say. Everything the conformance harness does needs a file; this needs a line.
    /// </remarks>
    /// <param name="arguments">
    /// Passed on as `sys.argv[1]` and up, for a snippet that has to be told a path.
    /// </param>
    public static ToolResult RunPythonSnippet(string scenario, string snippet, params string[] arguments)
        => Execute(PythonExecutable, Generated(scenario, "python"),
                   new[] { "-c", snippet }.Concat(arguments).ToArray());

    /// <summary>
    /// Runs a Lua snippet from a scenario's generated output directory, under the same
    /// host the conformance run builds.
    /// </summary>
    /// <remarks>
    /// Written to a file rather than passed inline - the host takes a script path - and
    /// beside the generated modules so the default package.path resolves them, the way
    /// the Python snippet runs from the generated package's parent.
    /// </remarks>
    public static ToolResult RunLuaSnippet(string scenario, string snippet, params string[] arguments)
    {
        string root = Generated(scenario, "lua");
        string script = Path.Combine(root, "snippet.lua");

        File.WriteAllText(script, snippet);

        return Execute(LuaToolchain.HostExecutable, root,
                       new[] { script }.Concat(arguments).ToArray());
    }

    /// <summary>Runs a Lua script that is already in `workDir`, under the same host.</summary>
    public static ToolResult RunLuaScript(string workDir, string script, params string[] arguments)
        => Execute(LuaToolchain.HostExecutable, workDir,
                   new[] { Path.Combine(workDir, script) }.Concat(arguments).ToArray());

    public static ToolResult CompileJava(string scenario)
    {
        string root = Generated(scenario, "java");

        var arguments = new List<string> { "-encoding", "UTF-8", "-d", Path.Combine(root, "classes") };
        arguments.AddRange(Directory.EnumerateFiles(root, "*.java", SearchOption.AllDirectories));

        return Execute("javac", root, arguments.ToArray());
    }

    public static ToolResult CompileKotlin(string scenario)
    {
        string root = Generated(scenario, "kotlin");

        var arguments = new List<string>
        {
            "-jar", KotlinCompilerJar(), "-nowarn", "-d", Path.Combine(root, "classes"),
        };

        arguments.AddRange(Directory.EnumerateFiles(root, "*.kt", SearchOption.AllDirectories));

        return Execute("java", root, arguments.ToArray());
    }

    /// <summary>
    /// Syntax-checks every generated Ruby file.
    ///
    /// Ruby compiles nothing ahead of time, so `-c` is as far as a static check goes -
    /// which is far enough: a keyword where a method name belongs does not parse.
    /// </summary>
    /// <summary>
    /// Compiles the generated C. Nothing is run: the question is whether the names the
    /// generator chose are legal, which in C means every one of them, since a member is
    /// snake_case and every keyword is lowercase.
    /// </summary>
    /// <param name="accessorName">
    /// Names the umbrella header, which is the one a consumer includes.
    /// </param>
    public static ToolResult CompileC(string scenario, string accessorName)
    {
        string root = Generated(scenario, "c");

        return CToolchain.CompileOnly(
            Path.Combine(WorkDir(scenario, "c"), "compile-only"),
            includeDir: root,
            sources: Directory.GetFiles(root, "*.c", SearchOption.AllDirectories)
                              .OrderBy(path => path).ToArray(),
            accessorHeader: accessorName + ".h");
    }

    /// <summary>
    /// Compiles each generated header on its own, as the only thing a translation unit
    /// includes.
    /// </summary>
    /// <remarks>
    /// Which is the question the split created and nothing else asks. Compiling the sources
    /// says the headers work in the order those sources include them; it says nothing about a
    /// header a consumer reaches for directly. A table header that needed an enum's complete
    /// type and did not include it still compiles inside a source file that included the
    /// umbrella first.
    ///
    /// Returns the first failure, so the message names one header rather than all of them.
    /// </remarks>
    public static ToolResult CompileEachCHeaderAlone(string scenario)
    {
        string root = Generated(scenario, "c");

        foreach (var header in Directory.GetFiles(root, "*.h", SearchOption.AllDirectories)
                                        .OrderBy(path => path))
        {
            // The path relative to the output, because that is how the umbrella includes
            // a header now that they sit in `tables/`, `enums/` and `constants/`.
            string name = Path.GetRelativePath(root, header).Replace('\\', '/');

            // No sources, so the translation unit is the one include and nothing else.
            var result = CToolchain.CompileOnly(
                Path.Combine(WorkDir(scenario, "c"), "alone",
                             Path.GetFileNameWithoutExtension(name).Replace('/', '-')),
                includeDir: root,
                sources: Array.Empty<string>(),
                accessorHeader: name);

            if (!result.Succeeded)
            {
                return new ToolResult
                {
                    Succeeded = false,
                    StdOut = result.StdOut,
                    Output = $"{name} does not compile on its own.{Environment.NewLine}{result.Output}",
                };
            }
        }

        return new ToolResult { Succeeded = true, StdOut = "", Output = "" };
    }

    /// <summary>
    /// Compiles the generated C header as C++, which is what its `extern "C"` claims.
    /// </summary>
    public static ToolResult CompileCAsCpp(string scenario, string accessorName)
        => CToolchain.CompileAsCpp(
            Path.Combine(WorkDir(scenario, "c"), "as-cpp"), Generated(scenario, "c"), accessorName);

    /// <summary>
    /// Parses the generated PHP without running it.
    ///
    /// `-l` is a syntax check, which is the whole question here: a property named after
    /// a reserved word either parses or it does not, and PHP has accepted them since
    /// 7.0 - so this is the check that turns that claim into a fact.
    /// </summary>
    public static ToolResult CompilePhp(string scenario, string accessorName)
    {
        string root = Generated(scenario, "php");

        var lintAccessor = Execute(PhpExecutable, root, "-l", accessorName + ".php");
        if (!lintAccessor.Succeeded)
            return lintAccessor;

        // Every table, enum and constant file as well. This used to be the accessor and the
        // reader only, which left the files holding the record classes and the read loops -
        // most of what the generator emits - unchecked by anything.
        foreach (var file in Directory.EnumerateFiles(root, "*.php", SearchOption.AllDirectories)
                                      .OrderBy(path => path))
        {
            var result = Execute(PhpExecutable, root, "-l", file);

            if (!result.Succeeded)
                return result;
        }

        return lintAccessor;
    }

    /// <summary>
    /// Runs a snippet against a scenario's generated PHP, from the directory the accessor
    /// sits in so `require_once __DIR__ . '/...'` resolves.
    /// </summary>
    /// <remarks>
    /// `php -l` is a parse, and a parse says nothing about whether a class the generated
    /// code names actually exists. For the shapes where that is the question - a record
    /// group's element type, built in a constructor by name - the snippet is the check.
    /// </remarks>
    public static ToolResult RunPhpSnippet(string scenario, string snippet, params string[] arguments)
        => Execute(PhpExecutable, Generated(scenario, "php"),
                   // zend.assertions=1, because a production ini compiles `assert` away and
                   // a snippet of assertions would then pass by not running.
                   new[] { "-d", "zend.assertions=1", "-r", snippet }.Concat(arguments).ToArray());

    /// <summary>
    /// Runs a snippet against a scenario's generated Ruby, from the directory the accessor
    /// sits in so its `require_relative` resolves.
    /// </summary>
    /// <remarks>
    /// `ruby -c` is a parse, and a parse says nothing about whether a constant the generated
    /// code names actually exists. For the shapes where that is the question - a record
    /// group's element class, built by name in a read - the snippet is the check.
    /// </remarks>
    public static ToolResult RunRubySnippet(string scenario, string snippet, params string[] arguments)
        => Execute(RubyExecutable, Generated(scenario, "ruby"),
                   new[] { "-e", snippet }.Concat(arguments).ToArray());

    public static ToolResult CompileRuby(string scenario)
    {
        string root = Generated(scenario, "ruby");

        foreach (var file in Directory.EnumerateFiles(root, "*.rb", SearchOption.AllDirectories))
        {
            var result = Execute(RubyExecutable, root, "-c", file);

            if (!result.Succeeded)
                return result;
        }

        return new ToolResult { Succeeded = true, StdOut = "", Output = "" };
    }

    /// <summary>
    /// Compiles the generated Dart, by running a program that imports it.
    ///
    /// `dart analyze` on a directory with no package config cannot resolve the core
    /// library and reports every `int` as undefined, so it answers a different question.
    /// A program that imports the library is resolved properly, and a name that does not
    /// compile fails.
    /// </summary>
    public static ToolResult CompileDart(string scenario)
    {
        string root = Generated(scenario, "dart");

        File.Copy(Path.Combine(HarnessDir("..", "compile", "dart"), "check.dart"),
                  Path.Combine(root, "check.dart"), overwrite: true);

        return Execute(DartExecutable, root, "run", "check.dart");
    }

    /// <summary>
    /// A scenario's generated sources for one language, in a copy nothing else looks at.
    /// </summary>
    /// <remarks>
    /// **Nothing builds inside a generated tree.** Every harness here puts its own file into
    /// the tree it is about to compile - a `main.go` beside the module, a `harness.rs` in the
    /// crate - and the tool then leaves its working files there as well: a `go.sum`, a Cargo
    /// target directory, a Dart package config. Three tests walk a generated tree and judge
    /// every file in it, and to them all of that is output nothing generated.
    ///
    /// Making the copy is what lets those three read a shared conversion instead of clearing
    /// the tree and converting again - and clearing is the one thing that cannot be done
    /// while another class is compiling in there, which is what kept the collections serial.
    ///
    /// Copied once per language per scenario per test run, so whatever build caching
    /// accumulates inside survives from one test to the next exactly as it did when this was
    /// the tree itself.
    /// </remarks>
    private static string Generated(string scenario, string language)
        => StagedTrees.GetOrAdd(
            (scenario, language),
            key => new Lazy<string>(
                () => Stage(key.Scenario, key.Language),
                System.Threading.LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        (string Scenario, string Language), Lazy<string>> StagedTrees = new();

    /// <summary>
    /// Copies one language's generated sources out of the tree they were written into.
    /// </summary>
    /// <remarks>
    /// Under a scenario of its own - `_lang` - because the output directories are keyed by
    /// scenario and a name no recipe uses cannot collide with one. `_cscheck` is the same
    /// device for the C# compile check.
    /// </remarks>
    private static string Stage(string scenario, string language)
    {
        string source = Path.Combine(RepoLayout.OutputDir(scenario), language);
        string staged = Path.Combine(RepoLayout.OutputDir("_lang"), scenario, language);

        if (Directory.Exists(staged))
            Directory.Delete(staged, recursive: true);

        Directory.CreateDirectory(staged);

        // A language the recipe did not ask for. The caller is about to fail on the file it
        // wanted, which says more than a message about an empty directory would.
        if (!Directory.Exists(source))
            return staged;

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(staged, Path.GetRelativePath(source, file));

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, overwrite: true);
        }

        return staged;
    }

    private static string HarnessDir(params string[] parts)
        => Path.GetFullPath(Path.Combine(
            new[] { RepoLayout.Root, "test", "fixtures", "tools", "conformance" }.Concat(parts).ToArray()));

    // ------------------------------------------------------- finding a tool

    /// <summary>
    /// Where a toolchain lives: the bare command when the path has it, and otherwise the
    /// first well-known install location that exists.
    ///
    /// The fallback is not convenience. An installer appends to the user's path, and a
    /// shell that was already open - which is the one running these tests - keeps the
    /// path it started with. A probe that asked only the path would then report the
    /// language missing and skip its check, which is the one answer a conformance suite
    /// must not give quietly.
    /// </summary>
    private static string Resolve(string command, params string[] candidates)
        => FindOnPath(command) ?? candidates.FirstOrDefault(File.Exists) ?? command;

    private static string FindOnPath(string command)
    {
        var extensions = OnWindows
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';')
            : new[] { "" };

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                                  .Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
                continue;

            foreach (var extension in extensions)
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(directory, command + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry, which is common enough on Windows.
                    break;
                }

                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// The directories a Unix package manager puts a command in.
    /// </summary>
    /// <remarks>
    /// The same reason the Windows fallbacks exist, and it bites harder here: Homebrew on
    /// Apple Silicon installs under `/opt/homebrew`, which is on the path of a login shell
    /// and is not on the path of a test host launched from an IDE or a launchd agent. A
    /// probe that asked only the path would report the language missing and skip its check,
    /// which is the one answer a conformance suite must not give quietly.
    ///
    /// `/opt/homebrew` first, because a Mac that has both has the Intel one under Rosetta.
    /// </remarks>
    private static IEnumerable<string> UnixInstalls(string command)
    {
        if (OnWindows)
            yield break;

        yield return "/opt/homebrew/bin/" + command;
        yield return "/usr/local/bin/" + command;
        yield return "/usr/bin/" + command;
    }

    /// <summary>
    /// The PHP interpreter.
    ///
    /// The winget package puts it under Packages and appends a Links directory to the
    /// path, which a shell that was already open does not see - so both are looked at.
    /// </summary>
    private static string PhpExecutable => Resolve("php", PhpInstalls().ToArray());

    private static IEnumerable<string> PhpInstalls()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "php.exe");

            string packages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");

            if (Directory.Exists(packages))
            {
                foreach (var directory in Directory.EnumerateDirectories(packages, "PHP.PHP*")
                                                   .OrderByDescending(path => path))
                {
                    yield return Path.Combine(directory, "php.exe");
                }
            }
        }

        yield return @"C:\php\php.exe";

        foreach (var path in UnixInstalls("php"))
            yield return path;
    }

    private static string RubyExecutable => Resolve("ruby", RubyInstalls().ToArray());

    /// <summary>Where RubyInstaller puts an interpreter, newest first.</summary>
    private static IEnumerable<string> RubyInstalls()
    {
        if (!OnWindows)
        {
            foreach (var path in UnixInstalls("ruby"))
                yield return path;

            yield break;
        }

        string[] roots;

        try
        {
            roots = Directory.GetDirectories(@"C:\", "Ruby*");
        }
        catch (IOException)
        {
            yield break;
        }

        Array.Sort(roots, StringComparer.OrdinalIgnoreCase);

        for (int i = roots.Length - 1; i >= 0; i--)
            yield return Path.Combine(roots[i], "bin", "ruby.exe");
    }

    private static string DartExecutable => Resolve("dart", DartInstalls().ToArray());

    private static IEnumerable<string> DartInstalls()
    {
        yield return Path.Combine(HomeDir, "tools", "dart-sdk", "bin", "dart.exe");
        yield return Path.Combine(HomeDir, "tools", "dart-sdk", "bin", "dart");
        yield return @"C:\tools\dart-sdk\bin\dart.exe";

        // Homebrew's `dart-sdk` puts the launcher in bin like any other formula, and its
        // Flutter counterpart hides one under libexec.
        foreach (var path in UnixInstalls("dart"))
            yield return path;

        yield return "/opt/homebrew/opt/dart-sdk/bin/dart";
        yield return "/usr/local/opt/dart-sdk/bin/dart";
        yield return "/usr/lib/dart/bin/dart";
    }

    /// <summary>
    /// The Kotlin compiler jar, found beside whichever launcher is here.
    /// </summary>
    private static string KotlinCompilerJar()
    {
        foreach (string home in KotlinHomes())
        {
            if (home == null)
                continue;

            string jar = Path.Combine(home, "lib", "kotlin-compiler.jar");

            if (File.Exists(jar))
                return jar;
        }

        return null;
    }

    private static IEnumerable<string> KotlinHomes()
    {
        // The launcher sits in <home>/bin, so its grandparent is the install.
        string launcher = FindOnPath("kotlinc");

        if (launcher != null)
            yield return Path.GetDirectoryName(Path.GetDirectoryName(launcher));

        yield return Path.Combine(HomeDir, "tools", "kotlinc");
        yield return @"C:\tools\kotlinc";

        // Homebrew's `kotlin` formula keeps the distribution under libexec and puts only
        // the launcher in bin, so the grandparent rule above does not reach it.
        yield return "/opt/homebrew/opt/kotlin/libexec";
        yield return "/usr/local/opt/kotlin/libexec";

        // SDKMAN, which is how a JVM developer most often has one.
        yield return Path.Combine(HomeDir, ".sdkman", "candidates", "kotlin", "current");

        yield return "/usr/share/kotlin";
    }

    private static string WorkDir(string scenario, string language)
    {
        string dir = Path.Combine(RepoLayout.OutputDir("_conformance"), scenario, language);

        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
        => Execute(fileName, workingDirectory, null, args);

    /// <summary>
    /// Runs a tool, and never two at once in one directory.
    /// </summary>
    /// <remarks>
    /// Two classes ask for the same language of the same scenario - the corpus and the
    /// tampered-corpus tests both run every reader against `conformance` - and they get the
    /// same staged tree, because staging it twice would build it twice. Two builds in one
    /// directory then write over each other's object files.
    ///
    /// A lock per directory rather than one for everything: what must not overlap is two
    /// tools in one place, and Go compiling `conformance` has nothing to do with Rust
    /// compiling `nested`.
    /// </remarks>
    private static ToolResult Execute(
        string fileName,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        params string[] args)
    {
        lock (WorkingDirectoryLocks.GetOrAdd(
                  Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar)
                      .ToLowerInvariant(),
                  _ => new object()))
        {
            return ExecuteHere(fileName, workingDirectory, environment, args);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object>
        WorkingDirectoryLocks = new();

    private static ToolResult ExecuteHere(
        string fileName,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        params string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Every harness, always. The corpus is signed, so a harness that reads this walks
        // the MAC path on every conformance run rather than only when a test remembers.
        psi.Environment[MacKeyVariable] = MacKey;

        if (environment != null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

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

        return new ToolResult
        {
            Succeeded = process.ExitCode == 0,
            StdOut = stdout.ToString(),
            Output = combined.ToString(),
        };
    }
}
