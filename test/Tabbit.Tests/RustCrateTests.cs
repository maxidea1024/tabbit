using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The shape of the generated Rust crate: the module tree in lib.rs, the re-exports that keep
/// a consumer's paths where they were, and what each file brings into scope.
///
/// Text only, no cargo. The conformance harness compiles and runs the crate, which is a far
/// better check - but only on a machine with a Rust toolchain, and the tree is decidable from
/// the files. This runs everywhere and fails with the offending line.
/// </summary>
public class RustCrateTests
{
    /// <summary>
    /// Both corpora with a Rust target. `conformance` has an enum and two tables referencing
    /// each other's rows; `reserved-words` has a table whose fields are named after keywords.
    /// </summary>
    private static readonly string[] Scenarios = { "conformance", "reserved-words" };

    private static readonly Regex ModDeclaration = new Regex(
        @"^(?:pub )?mod (?<module>\w+);$", RegexOptions.Multiline);

    private static readonly Regex ReExport = new Regex(
        @"^pub use (?<module>\w+)::(?:\{(?<names>[^}]+)\}|(?<names>\w+));$", RegexOptions.Multiline);

    private static readonly Regex CrateUse = new Regex(
        @"^use crate::(?<module>\w+)(?:::(?<name>\w+))?;$", RegexOptions.Multiline);

    /// <summary>
    /// What a module can offer a re-export. A `static` alongside the types because the
    /// accessor module declares one - the encryption key - and it is re-exported for the
    /// same reason the types are: so a consumer's path stays at the crate root.
    /// </summary>
    private static readonly Regex Declaration = new Regex(
        @"^pub (?:struct|enum|static) (?<name>\w+)", RegexOptions.Multiline);

    [Fact]
    public void Every_module_lib_declares_is_a_file_beside_it()
    {
        foreach (var crate in Crates())
        {
            foreach (Match declaration in ModDeclaration.Matches(crate.Lib))
            {
                string module = declaration.Groups["module"].Value;

                Assert.True(crate.Modules.ContainsKey(module),
                    $"{crate.Name}/src/lib.rs declares `mod {module};` and there is no {module}.rs.");
            }
        }
    }

    /// <summary>
    /// The other direction: a file nothing declares is a file rustc never reads, so a table
    /// would be generated and simply not be in the crate.
    /// </summary>
    [Fact]
    public void Every_generated_file_is_declared_by_lib()
    {
        foreach (var crate in Crates())
        {
            var declared = new HashSet<string>(
                ModDeclaration.Matches(crate.Lib).Select(m => m.Groups["module"].Value));

            foreach (var module in crate.Modules.Keys)
            {
                // The harness the conformance suite drops in is not this tool's writing, and
                // src/bin is cargo's own convention for one.
                if (module == "harness")
                    continue;

                Assert.True(declared.Contains(module),
                    $"{crate.Name}/src/{module}.rs is generated and lib.rs declares no `mod {module};`.");
            }
        }
    }

    /// <summary>
    /// Every re-export names a type the module it points at actually declares.
    ///
    /// This is what keeps a consumer's paths where they were: before the split every type was
    /// declared in lib.rs, so `gamedata::VectorsRecord` was the path. The `pub use` lines are
    /// the only thing holding that, and a wrong one moves a type without saying so.
    /// </summary>
    [Fact]
    public void Every_re_export_names_a_type_its_module_declares()
    {
        var offenders = new List<string>();

        foreach (var crate in Crates())
        {
            foreach (Match export in ReExport.Matches(crate.Lib))
            {
                string module = export.Groups["module"].Value;

                if (!crate.Modules.TryGetValue(module, out string text))
                {
                    offenders.Add($"  {crate.Name}: `{export.Value}` - no {module}.rs");
                    continue;
                }

                var declared = new HashSet<string>(
                    Declaration.Matches(text).Select(m => m.Groups["name"].Value));

                foreach (string name in Names(export))
                {
                    if (!declared.Contains(name))
                        offenders.Add($"  {crate.Name}: `{export.Value}` - {module}.rs declares no `{name}`");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Generated Rust re-exports do not resolve:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// And every `use crate::...` in a generated file resolves too, which is the dependency
    /// graph's own answer being checked rather than lib.rs's.
    /// </summary>
    [Fact]
    public void Every_crate_use_resolves()
    {
        var offenders = new List<string>();

        foreach (var crate in Crates())
        {
            foreach (var module in crate.Modules)
            {
                foreach (Match use in CrateUse.Matches(module.Value))
                {
                    string target = use.Groups["module"].Value;

                    // The reader is emitted rather than generated; `use crate::tabbit;`
                    // brings in the module and everything reaches its types through it.
                    if (target == "tabbit")
                    {
                        if (!crate.Modules.ContainsKey("tabbit"))
                            offenders.Add($"  {crate.Name}/src/{module.Key}.rs uses the reader, which is not there");

                        continue;
                    }

                    if (!crate.Modules.TryGetValue(target, out string text))
                    {
                        offenders.Add($"  {crate.Name}/src/{module.Key}.rs: `{use.Value}` - no {target}.rs");
                        continue;
                    }

                    string name = use.Groups["name"].Value;

                    if (name.Length > 0 && !Declaration.Matches(text).Any(m => m.Groups["name"].Value == name))
                        offenders.Add($"  {crate.Name}/src/{module.Key}.rs: `{use.Value}` - {target}.rs declares no `{name}`");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Generated Rust uses do not resolve:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A table gets a module declaring its record and its table and nothing else, which is
    /// what makes deleting a table from the sheets remove a file rather than edit one.
    /// </summary>
    [Fact]
    public void A_table_gets_a_module_of_its_own()
    {
        foreach (var crate in Crates())
        {
            var tableModules = crate.Modules
                .Where(module => module.Key.EndsWith("_table", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(tableModules);

            foreach (var module in tableModules)
            {
                var declared = Declaration.Matches(module.Value)
                    .Select(m => m.Groups["name"].Value).ToList();

                Assert.Equal(2, declared.Count);
                Assert.EndsWith("Record", declared[0]);
                Assert.EndsWith("Table", declared[1]);
            }
        }
    }

    /// <summary>
    /// The crate lints are declared once, at crate scope in lib.rs. A generated file repeating
    /// them would be an inner attribute in a module, which applies to that module only and
    /// reads as though somebody had a reason.
    /// </summary>
    [Fact]
    public void Only_lib_carries_the_crate_lints()
    {
        foreach (var crate in Crates())
        {
            Assert.Contains("#![allow(dead_code)]", crate.Lib);

            foreach (var module in crate.Modules)
                Assert.DoesNotContain("#![allow(", module.Value);
        }
    }

    // --------------------------------------------------------------- corpus

    private sealed class Crate
    {
        public string Name;

        /// <summary>lib.rs, which is the tree rather than a module of its own.</summary>
        public string Lib;

        /// <summary>Module name to the file's text, lib.rs excluded.</summary>
        public Dictionary<string, string> Modules;
    }

    private static IReadOnlyList<Crate> Crates()
    {
        var crates = new List<Crate>();

        foreach (var scenario in Scenarios)
        {
            var conversion = TabbitRunner.Convert(scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

            string source = Path.Combine(RepoLayout.OutputDir(scenario), "rust", "src");

            Assert.True(Directory.Exists(source), $"`{scenario}` generated no Rust at {source}.");

            crates.Add(new Crate
            {
                Name = scenario,
                Lib = File.ReadAllText(Path.Combine(source, "lib.rs")),
                // A module is `src/<name>.rs` or `src/<name>/mod.rs` - both are how Rust
                // spells one, and the runtime is the second kind now that the reader and
                // the updater sit together under `tabbit/`.
                Modules = Directory.GetFiles(source, "*.rs")
                    .Where(path => Path.GetFileName(path) != "lib.rs")
                    .Select(path => (Name: Path.GetFileNameWithoutExtension(path), Path: path))
                    .Concat(Directory.GetDirectories(source)
                        .Select(directory => (Name: Path.GetFileName(directory),
                                              Path: Path.Combine(directory, "mod.rs")))
                        .Where(module => File.Exists(module.Path)))
                    .ToDictionary(
                        module => module.Name,
                        module => File.ReadAllText(module.Path)),
            });
        }

        Assert.NotEmpty(crates);

        return crates;
    }

    private static IEnumerable<string> Names(Match export)
        => export.Groups["names"].Value.Split(',').Select(name => name.Trim());
}
