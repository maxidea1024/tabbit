using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tabbit.Tests;

/// <summary>
/// Builds the host the Lua gates run scripts under.
///
/// No Lua is looked for on PATH. The vendored 5.4 sources (test/fixtures/tools/lua), the
/// embedder (test/fixtures/tools/conformance/lua/main.c) and the runtime's native module
/// source are compiled into one executable by the same C toolchain every C gate already
/// finds - so the Lua gates are available exactly where the C ones are, and the host is
/// the same integration shape a game engine embedding Lua has.
/// spec/targets/lua-language-support.md.
/// </summary>
internal static class LuaToolchain
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static bool IsAvailable(out string reason) => CppToolchain.IsAvailable(out reason);

    /// <summary>
    /// The host executable, built on first use and reused while its sources are older
    /// than it - so one suite run builds once, and an offline rerun not at all.
    /// </summary>
    public static string HostExecutable => _host.Value;

    private static readonly Lazy<string> _host = new Lazy<string>(BuildHost);

    private static string BuildHost()
    {
        string workDir = Path.Combine(
            RepoLayout.Root, "test", "fixtures", "output", "_lua");
        string exe = Path.Combine(workDir, OnWindows ? "lua-host.exe" : "lua-host");

        Directory.CreateDirectory(workDir);

        var sources = Directory
            .GetFiles(Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "lua"), "*.c")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        sources.Add(Path.Combine(
            RepoLayout.Root, "test", "fixtures", "tools", "conformance", "lua", "main.c"));

        // The runtime's own copy: the same bytes the generator embeds, so building the
        // host is also the check that the shipped .c compiles.
        sources.Add(Path.Combine(
            RepoLayout.Root, "lib", "lua", "tabbit", "native", "tabbit_native.c"));

        if (File.Exists(exe))
        {
            var built = File.GetLastWriteTimeUtc(exe);

            if (sources.All(source => File.GetLastWriteTimeUtc(source) < built))
                return exe;
        }

        string includeDir = Path.Combine(RepoLayout.Root, "test", "fixtures", "tools", "lua");

        ToolResult result;

        if (OnWindows)
        {
            // /W3 rather than the /W4 /WX the generated C gets: two thirds of these
            // sources are vendored, and their warnings are upstream's.
            var arguments = new List<string>
            {
                "/nologo", "/O2", "/MD", "/W3", "/utf-8",
                $"/I \"{includeDir}\"",
            };

            arguments.AddRange(sources.Select(path => $"\"{path}\""));
            arguments.Add($"/Fo:\"{workDir}\\\\\"");
            arguments.Add($"/Fe\"{exe}\"");

            result = CppToolchain.RunMsvc(workDir, "build-lua-host", arguments);
        }
        else
        {
            var arguments = new List<string> { "-std=gnu99", "-O2", "-I", includeDir };

            arguments.AddRange(sources);
            arguments.Add("-o");
            arguments.Add(exe);
            arguments.Add("-lm");

            result = CppToolchain.Execute("gcc", workDir, arguments.ToArray());
        }

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"The Lua host did not build.{Environment.NewLine}{result.Output}");

        return exe;
    }
}
