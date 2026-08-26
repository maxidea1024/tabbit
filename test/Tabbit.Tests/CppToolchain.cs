using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Tabbit.Tests;

internal sealed class ToolResult
{
    public bool Succeeded;
    public string StdOut;
    public string Output;
}

/// <summary>
/// Compiles and runs the generated C++ so the suite can check two things a golden
/// comparison cannot: that the emitted header is valid C++ at all, and that the
/// C++ reader agrees with the C# writer about the binary format.
///
/// Those are separate programs that have to stay in lockstep, and a byte-level
/// disagreement would otherwise only surface in someone's game build.
///
/// MSVC on Windows, g++ elsewhere. MSVC needs the environment vcvars64.bat exports,
/// which is why the Windows path goes through a script rather than invoking cl.exe
/// directly - see <see cref="RunMsvc"/>.
/// </summary>
internal static class CppToolchain
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static bool IsAvailable(out string reason)
    {
        if (!OnWindows)
        {
            var probe = Execute("g++", RepoLayout.Root, "--version");
            reason = probe.Succeeded ? null : $"`g++ --version` failed.{Environment.NewLine}{probe.Output}";
            return probe.Succeeded;
        }

        if (FindVcVars() == null)
        {
            reason = "No Visual Studio C++ toolchain found (looked for vcvars64.bat under the standard install paths).";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Builds the round-trip program against a scenario's generated header and
    /// runs it over that scenario's binary tables.
    /// </summary>
    public static ToolResult BuildAndRun(string scenario, string accessorName)
    {
        string workDir = Path.Combine(RepoLayout.OutputDir("_cppcheck"), scenario);
        Directory.CreateDirectory(workDir);

        string includeDir = Path.Combine(RepoLayout.OutputDir(scenario), "cpp");
        string runtimeDir = Path.Combine(RepoLayout.Root, "lib", "cpp");
        string source = Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "cpp-check", "main.cpp");
        string binaryDir = Path.Combine(RepoLayout.OutputDir(scenario), "binary");

        string exe = Path.Combine(workDir, OnWindows ? "cpp-check.exe" : "cpp-check");

        var build = OnWindows
            ? BuildWithMsvc(workDir, includeDir, runtimeDir, source, exe, accessorName)
            : BuildWithGcc(workDir, includeDir, runtimeDir, source, exe, accessorName);

        if (!build.Succeeded)
            return build;

        return Execute(exe, workDir, binaryDir);
    }

    /// <summary>
    /// Compiles a scenario's generated header on its own, without running anything.
    ///
    /// For the scenarios where the question is only whether the emitted code is valid
    /// C++ - identifiers taken from a sheet, say - and the round-trip program cannot be
    /// reused because it names the tables of one particular fixture.
    ///
    /// The translation unit is generated here rather than committed: it is two lines,
    /// and the accessor name differs per scenario.
    /// </summary>
    public static ToolResult Compile(string scenario, string accessorName)
    {
        string workDir = Path.Combine(RepoLayout.OutputDir("_cppcheck"), scenario + "-compile");
        Directory.CreateDirectory(workDir);

        string includeDir = Path.Combine(RepoLayout.OutputDir(scenario), "cpp");
        string runtimeDir = Path.Combine(RepoLayout.Root, "lib", "cpp");

        string source = Path.Combine(workDir, "compile-only.cpp");
        File.WriteAllText(source, string.Join(Environment.NewLine, new[]
        {
            "// Written by the test suite. Includes the generated header and nothing else,",
            "// so a failure here is the generated code and not the harness.",
            "#include TABBIT_ACCESSOR_HEADER",
            "int main() { return 0; }",
            "",
        }));

        string exe = Path.Combine(workDir, OnWindows ? "compile-only.exe" : "compile-only");

        return OnWindows
            ? BuildWithMsvc(workDir, includeDir, runtimeDir, source, exe, accessorName)
            : BuildWithGcc(workDir, includeDir, runtimeDir, source, exe, accessorName);
    }

    /// <summary>
    /// Builds an arbitrary harness against a scenario's generated header.
    ///
    /// The conformance harnesses use this rather than BuildAndRun, which names the
    /// output executable and the source for the one round-trip program.
    /// </summary>
    public static ToolResult CompileHarness(
        string workDir, string includeDir, string source, string accessorName, string exeName)
    {
        string runtimeDir = Path.Combine(RepoLayout.Root, "lib", "cpp");
        string exe = Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName);

        return OnWindows
            ? BuildWithMsvc(workDir, includeDir, runtimeDir, source, exe, accessorName)
            : BuildWithGcc(workDir, includeDir, runtimeDir, source, exe, accessorName);
    }

    /// <summary>
    /// Builds a harness for the updater: the same C++ build, plus libcurl.
    /// </summary>
    /// <remarks>
    /// The runtime directory is on the include path as `tabbit/...`, because that is
    /// how a consumer includes it. libcurl is found the same way the C gate finds it -
    /// through <see cref="CToolchain"/>, so there is one answer to where it is.
    /// </remarks>
    public static ToolResult CompileUpdaterHarness(string workDir, string source, string exeName)
    {
        Directory.CreateDirectory(workDir);

        string runtimeParent = Path.Combine(RepoLayout.Root, "lib", "cpp");
        string exe = Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName);

        if (!OnWindows)
        {
            return Execute("g++", workDir,
                "-std=c++17", "-Wall", "-Wextra", "-Werror",
                "-I", runtimeParent,
                source, "-o", exe, "-lcurl");
        }

        string libcurl = CToolchain.LibcurlRoot;

        var built = RunMsvc(workDir, "build-updater", new[]
        {
            "/nologo", "/std:c++17", "/EHsc", "/W3", "/utf-8",
            $"/I \"{runtimeParent}\"",
            $"/I \"{Path.Combine(libcurl, "include")}\"",
            $"\"{source}\"",
            $"/Fo:\"{workDir}\\\\\"",
            $"/Fe:\"{exe}\"",
        },
        libraries: new[] { Path.Combine(libcurl, "lib", "libcurl.lib") });

        if (!built.Succeeded)
            return built;

        foreach (var dll in Directory.EnumerateFiles(Path.Combine(libcurl, "bin"), "*.dll"))
            File.Copy(dll, Path.Combine(workDir, Path.GetFileName(dll)), overwrite: true);

        return built;
    }

    /// <summary>Runs an executable built by <see cref="CompileUpdaterHarness"/>.</summary>
    public static ToolResult RunHarness(string workDir, string exeName, params string[] args)
        => Execute(Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName),
                   workDir, args);

    private static ToolResult BuildWithMsvc(string workDir, string includeDir, string runtimeDir,
                                            string source, string exe, string accessorName)
    {
        return RunMsvc(workDir, "build", new[]
        {
            "/nologo", "/std:c++17", "/EHsc", "/W3", "/utf-8",
            $"/DTABBIT_ACCESSOR_HEADER=\\\"{accessorName}.h\\\"",
            $"/I \"{includeDir}\"",
            $"/I \"{runtimeDir}\"",
            $"\"{source}\"",
            $"/Fo:\"{workDir}\\\\\"",
            $"/Fe:\"{exe}\"",
        });
    }

    /// <summary>
    /// Runs `cl` with the environment vcvars64.bat exports.
    /// </summary>
    /// <remarks>
    /// `cl` cannot find its own include and library paths; vcvars64.bat exports them, and a
    /// process that never ran it cannot inherit them. So something has to run that script and
    /// hand the result to the compiler, and this is the one place that happens - the C, C++
    /// and Unreal gates all come through here rather than each writing a script of its own.
    ///
    /// PowerShell rather than a batch file, because this repository's scripts are PowerShell.
    /// vcvars64.bat is Microsoft's and stays a batch file, so it is run through `cmd` and the
    /// environment it leaves behind is read back out of `set` and imported here.
    ///
    /// The compiler options go in a response file rather than on the command line. `cl` has
    /// read those since forever, and it sidesteps the one hard part of calling a native
    /// program from PowerShell: an argument like /DTABBIT_ACCESSOR_HEADER=\"Foo.h\" has to
    /// reach `cl` with its quotes intact, and PowerShell 5.1's rules for that are not
    /// something to rely on. A response file has one argument per line and no such rules.
    /// </remarks>
    /// <param name="libraries">
    /// What to link against, if anything. These go after the response file on the command line
    /// rather than inside it, because `/link` takes the rest of the *command line* and a
    /// response file is expanded before that is decided. Inside the file, `/link` was read as
    /// an ordinary option and the library after it as an input file - which under `/TC` means
    /// "a C source", so the build failed with syntax errors inside libcurl.lib.
    /// </param>
    internal static ToolResult RunMsvc(
        string workDir, string name, IEnumerable<string> arguments,
        IEnumerable<string> libraries = null)
    {
        Directory.CreateDirectory(workDir);

        string responseFile = Path.Combine(workDir, name + ".rsp");
        File.WriteAllText(responseFile, string.Join(Environment.NewLine, arguments) + Environment.NewLine);

        string link = libraries == null
            ? ""
            : string.Concat(libraries.Select(path => $" '{path.Replace("'", "''")}'"));

        if (link.Length > 0)
            link = " /link" + link;

        string script = Path.Combine(workDir, name + ".ps1");

        File.WriteAllText(script, string.Join(Environment.NewLine, new[]
        {
            "# Written by the test suite. See CppToolchain.RunMsvc for why it looks like this.",
            "$ErrorActionPreference = 'Stop'",
            "",
            // `$env:ComSpec` and not `cmd`: a runner that has set Ruby up puts msys2 on the
            // path ahead of System32, and msys2 ships an extensionless `cmd` that PowerShell
            // resolves first and then refuses to start - the failure reads as though the
            // compiler rejected the generated code.
            $"& $env:ComSpec /c \"call `\"{FindVcVars()}`\" >nul 2>&1 && set\" | ForEach-Object {{",
            "    if ($_ -match '^([^=]+)=(.*)$') {",
            "        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2])",
            "    }",
            "}",
            "",
            $"Set-Location -LiteralPath '{workDir.Replace("'", "''")}'",
            "",
            $"& cl '@{responseFile.Replace("'", "''")}'{link}",
            "",
            "# cl is a native program, so a non-zero exit is not a PowerShell error.",
            "exit $LASTEXITCODE",
            "",
        }));

        return Execute("powershell", workDir,
                       "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                       "-File", script);
    }

    private static ToolResult BuildWithGcc(string workDir, string includeDir, string runtimeDir,
                                           string source, string exe, string accessorName)
    {
        return Execute("g++", workDir,
            "-std=c++17", "-Wall", "-Wextra", "-Werror",
            $"-DTABBIT_ACCESSOR_HEADER=\"{accessorName}.h\"",
            "-I", includeDir, "-I", runtimeDir,
            source, "-o", exe);
    }

    /// <summary>
    /// Locates vcvars64.bat across the Visual Studio editions and years that might
    /// be installed, newest first.
    /// </summary>
    internal static string FindVcVars()
    {
        var roots = new List<string>();

        foreach (var programFiles in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                     Environment.GetEnvironmentVariable("ProgramFiles"),
                 })
        {
            if (string.IsNullOrEmpty(programFiles))
                continue;

            string vsRoot = Path.Combine(programFiles, "Microsoft Visual Studio");
            if (Directory.Exists(vsRoot))
                roots.Add(vsRoot);
        }

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "vcvars64.bat", SearchOption.AllDirectories))
            .OrderByDescending(path => path)
            .FirstOrDefault();
    }

    internal static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
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

        // The two streams are read on two threads and both append to `combined`.
        // StringBuilder is not safe for that, and the failure is not a garbled line -
        // it is `Destination is too short` from inside AppendLine when both threads
        // grow the buffer at once, thrown on a thread pool worker where nothing catches
        // it and the test host dies. Rare until a build writes to both at speed, which
        // is what the updater gate does.
        var writing = new object();

        using (var process = new Process { StartInfo = psi })
        {
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
}
