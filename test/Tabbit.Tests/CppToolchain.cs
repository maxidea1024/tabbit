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
/// MSVC on Windows, g++ elsewhere. MSVC needs its environment set up by
/// vcvars64.bat, which is why the Windows path goes through a batch file rather
/// than invoking cl.exe directly.
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
        string script = Path.Combine(workDir, "build-updater.bat");

        File.WriteAllText(script, string.Join(Environment.NewLine, new[]
        {
            "@echo off",
            $"call \"{FindVcVars()}\" >nul",
            $"cd /d \"{workDir}\"",
            $"cl /nologo /std:c++17 /EHsc /W3 /utf-8 /I \"{runtimeParent}\" " +
            $"/I \"{Path.Combine(libcurl, "include")}\" \"{source}\" " +
            $"/Fo:\"{workDir}\\\\\" /Fe:\"{exe}\" " +
            $"/link \"{Path.Combine(libcurl, "lib", "libcurl.lib")}\"",
            "exit /b %ERRORLEVEL%",
        }));

        var built = Execute("cmd.exe", workDir, "/c", script);

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
        string vcvars = FindVcVars();

        // A batch file rather than a direct cl.exe launch: cl needs the include and
        // library paths that vcvars64.bat exports, and those cannot be inherited
        // from a process that never ran it.
        string script = Path.Combine(workDir, "build.bat");
        File.WriteAllText(script, string.Join(Environment.NewLine, new[]
        {
            "@echo off",
            $"call \"{vcvars}\" >nul",
            $"cl /nologo /std:c++17 /EHsc /W3 /utf-8 /DTABBIT_ACCESSOR_HEADER=\\\"{accessorName}.h\\\" " +
            $"/I \"{includeDir}\" /I \"{runtimeDir}\" \"{source}\" " +
            $"/Fo:\"{workDir}\\\\\" /Fe:\"{exe}\"",
            "exit /b %ERRORLEVEL%",
        }));

        return Execute("cmd.exe", workDir, "/c", script);
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
