using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tabbit.Tests;

/// <summary>
/// Compiles and runs the generated C.
///
/// Separate from <see cref="CppToolchain"/> even though it finds the same compiler,
/// because what it asks of it is different: C rather than C++, more than one
/// translation unit, and the strict warning set that catches what C lets through
/// quietly. Discovery is shared - a machine with a C++ compiler has a C one.
///
/// Warnings are errors here. C will happily compile an implicit declaration or a
/// pointer conversion that is wrong, and generated code is exactly where nobody is
/// reading closely enough to notice.
/// </summary>
internal static class CToolchain
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static bool IsAvailable(out string reason) => CppToolchain.IsAvailable(out reason);

    /// <summary>
    /// Builds a harness against a scenario's generated C and leaves the executable in
    /// <paramref name="workDir"/>.
    /// </summary>
    public static ToolResult CompileHarness(
        string workDir, string includeDir, string source, string accessorHeader,
        IReadOnlyList<string> sources, string exeName)
    {
        Directory.CreateDirectory(workDir);

        var all = new List<string> { source };
        all.AddRange(sources);

        string exe = Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName);

        return Build(workDir, includeDir, all, accessorHeader, exe);
    }

    /// <summary>
    /// Compiles a scenario's generated C without running anything.
    ///
    /// For the reserved-word fixture, where the question is only whether the names the
    /// generator chose are legal C. The translation unit is written here rather than
    /// committed: it is three lines, and the header differs per scenario.
    /// </summary>
    public static ToolResult CompileOnly(
        string workDir, string includeDir, IReadOnlyList<string> sources, string accessorHeader)
    {
        Directory.CreateDirectory(workDir);

        string main = Path.Combine(workDir, "compile-only.c");

        File.WriteAllText(main, string.Join(Environment.NewLine, new[]
        {
            "/* Written by the test suite. Includes the generated header and nothing",
            "   else, so a failure here is the generated code and not the harness. */",
            "#include TABBIT_ACCESSOR_HEADER",
            "int main(void) { return 0; }",
            "",
        }));

        var all = new List<string> { main };
        all.AddRange(sources);

        string exe = Path.Combine(workDir, OnWindows ? "compile-only.exe" : "compile-only");

        return Build(workDir, includeDir, all, accessorHeader, exe);
    }

    /// <summary>
    /// Compiles the generated header as C++.
    ///
    /// The header wraps its declarations in `extern "C"`, which says it may be included
    /// from C++ - and nothing checked that. A member named `class` or `delete` is
    /// perfectly good C and stops a C++ compiler dead, so the C build stayed green
    /// while the header was unusable from the language it advertised.
    /// </summary>
    public static ToolResult CompileAsCpp(string workDir, string includeDir, string accessorName)
    {
        Directory.CreateDirectory(workDir);

        string source = Path.Combine(workDir, "include-from-cpp.cpp");

        File.WriteAllText(source, string.Join(Environment.NewLine, new[]
        {
            "// Written by the test suite. Includes the generated C header from C++ and",
            "// nothing else, which is the whole of what `extern \"C\"` promises.",
            "#include TABBIT_ACCESSOR_HEADER",
            "int main() { return 0; }",
            "",
        }));

        return CppToolchain.CompileHarness(
            workDir, includeDir, source, accessorName, "include-from-cpp");
    }

    /// <summary>
    /// Where libcurl's headers and import library are, when they are not where the
    /// compiler already looks.
    /// </summary>
    /// <remarks>
    /// The updater is the one emitted file that links against something, so it is the
    /// one gate that has to find it. On Linux the distribution's `libcurl4-openssl-dev`
    /// puts both where gcc looks and `-lcurl` is the whole of it; on Windows there is
    /// no such place, so `TABBIT_LIBCURL_ROOT` names a prefix holding `include` and
    /// `lib` - a vcpkg install being the usual one, and the default below.
    /// </remarks>
    internal static string LibcurlRoot
        => Environment.GetEnvironmentVariable("TABBIT_LIBCURL_ROOT")
           ?? @"C:\vcpkg\installed\x64-windows";

    /// <summary>Whether the updater's one dependency can be found.</summary>
    public static bool LibcurlIsAvailable(out string reason)
    {
        if (!IsAvailable(out reason))
            return false;

        if (!OnWindows)
        {
            // gcc finds a system libcurl by itself, and says so plainly if it cannot.
            reason = null;
            return true;
        }

        string header = Path.Combine(LibcurlRoot, "include", "curl", "curl.h");

        if (!File.Exists(header))
        {
            reason = $"libcurl was not found at `{LibcurlRoot}`. Install it "
                     + "(`vcpkg install curl:x64-windows`) or point TABBIT_LIBCURL_ROOT "
                     + "at a prefix holding include/ and lib/.";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Builds a harness for the updater: the same C build, plus libcurl.
    /// </summary>
    /// <remarks>
    /// The runtime directory is on the include path as `tabbit/...`, because that is
    /// how a consumer includes it and the updater includes the reader beside it by that
    /// same path.
    /// </remarks>
    public static ToolResult CompileUpdaterHarness(string workDir, string source, string exeName)
    {
        Directory.CreateDirectory(workDir);

        string runtimeParent = Path.Combine(RepoLayout.Root, "lib", "c");
        string exe = Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName);

        string implementation = Path.Combine(workDir, "tabbit-implementation.c");

        // The one translation unit that carries both runtimes, written here for the
        // same reason the generator writes one: the macros have to be defined exactly
        // once, and a harness that forgot would fail at link with nothing to read.
        File.WriteAllText(implementation, string.Join(Environment.NewLine, new[]
        {
            "/* Written by the test suite. */",

            // Before any include, and this is the reason the generated updater source
            // says it too: glibc reads the macro in the first libc header a translation
            // unit pulls in. This file includes the reader first, so asking inside the
            // updater header would already be too late - and the updater needs nanosleep
            // and strcasecmp, neither of which is ISO C.
            "#define _POSIX_C_SOURCE 200809L",
            "",
            "#define TABBIT_TCB_IMPLEMENTATION",
            "#include \"tabbit/tabbit_tcb_reader.h\"",
            "#define TABBIT_UPDATER_IMPLEMENTATION",
            "#include \"tabbit/tabbit_updater.h\"",
            "",
        }));

        var sources = new List<string> { source, implementation };

        if (!OnWindows)
        {
            var arguments = new List<string>
            {
                "-std=c99", "-Wall", "-Wextra", "-Werror", "-pedantic",
                "-I", runtimeParent,
            };

            arguments.AddRange(sources);
            arguments.Add("-o");
            arguments.Add(exe);
            arguments.Add("-lcurl");

            return Execute("gcc", workDir, arguments.ToArray());
        }

        var clArguments = new List<string>
        {
            "/nologo", "/TC", "/W4", "/WX", "/utf-8",
            $"/I \"{runtimeParent}\"",
            $"/I \"{Path.Combine(LibcurlRoot, "include")}\"",
        };

        clArguments.AddRange(sources.Select(path => $"\"{path}\""));
        clArguments.Add($"/Fo:\"{workDir}\\\\\"");
        clArguments.Add($"/Fe\"{exe}\"");

        var built = CppToolchain.RunMsvc(
            workDir, "build-updater", clArguments,
            libraries: new[] { Path.Combine(LibcurlRoot, "lib", "libcurl.lib") });

        if (!built.Succeeded)
            return built;

        // The DLLs go beside the executable, because a vcpkg libcurl is not on the
        // path and a run that cannot start says so with a dialog rather than a line.
        foreach (var dll in Directory.EnumerateFiles(Path.Combine(LibcurlRoot, "bin"), "*.dll"))
            File.Copy(dll, Path.Combine(workDir, Path.GetFileName(dll)), overwrite: true);

        return built;
    }

    /// <summary>Runs an executable built by <see cref="CompileUpdaterHarness"/>.</summary>
    public static ToolResult RunHarness(string workDir, string exeName, params string[] args)
        => Execute(Path.Combine(workDir, OnWindows ? exeName + ".exe" : exeName),
                   workDir, args);

    private static ToolResult Build(
        string workDir, string includeDir, IReadOnlyList<string> sources,
        string accessorHeader, string exe)
    {
        string runtimeDir = Path.Combine(RepoLayout.Root, "lib", "c");

        return OnWindows
            ? BuildWithMsvc(workDir, includeDir, runtimeDir, sources, accessorHeader, exe)
            : BuildWithGcc(workDir, includeDir, runtimeDir, sources, accessorHeader, exe);
    }

    private static ToolResult BuildWithMsvc(
        string workDir, string includeDir, string runtimeDir,
        IReadOnlyList<string> sources, string accessorHeader, string exe)
    {
        // /TC forces C even for a file cl would otherwise take for C++, and /utf-8 says
        // the sources are UTF-8 - which they are, and the corpus depends on it.
        var arguments = new List<string>
        {
            "/nologo", "/TC", "/W4", "/WX", "/utf-8",
            $"/DTABBIT_ACCESSOR_HEADER=\\\"{accessorHeader}\\\"",
            $"/I \"{includeDir}\"",
            $"/I \"{runtimeDir}\"",
        };

        arguments.AddRange(sources.Select(path => $"\"{path}\""));
        arguments.Add($"/Fo:\"{workDir}\\\\\"");
        arguments.Add($"/Fe\"{exe}\"");

        return CppToolchain.RunMsvc(workDir, "build", arguments);
    }

    private static ToolResult BuildWithGcc(
        string workDir, string includeDir, string runtimeDir,
        IReadOnlyList<string> sources, string accessorHeader, string exe)
    {
        var arguments = new List<string>
        {
            "-std=c99", "-Wall", "-Wextra", "-Werror", "-pedantic",
            $"-DTABBIT_ACCESSOR_HEADER=\"{accessorHeader}\"",
            "-I", includeDir, "-I", runtimeDir,
        };

        arguments.AddRange(sources);
        arguments.Add("-o");
        arguments.Add(exe);

        return Execute("gcc", workDir, arguments.ToArray());
    }

    // No FindVcVars here any more. It used to be borrowed from the C++ toolchain so that two
    // searches could not disagree about which compiler is in use; now the whole MSVC launch is
    // borrowed - CppToolchain.RunMsvc - and the search went with it.

    private static ToolResult Execute(string fileName, string workingDirectory, params string[] args)
        => CppToolchain.Execute(fileName, workingDirectory, args);
}
