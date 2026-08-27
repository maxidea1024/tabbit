using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The shape of the generated Python package: what each module imports, and what the
/// package's `__init__` re-exports.
///
/// Text only, no interpreter. The conformance harness already proves the package loads on a
/// machine that has Python, but that machine is not every machine, and a broken import is
/// decidable from the files alone - a `from .x import Y` names a module and a name, and both
/// either are there or are not. This runs everywhere and fails with the offending line
/// rather than a traceback.
///
/// Which is the point: splitting the output into a file per table is what created the
/// possibility of an import that resolves to nothing, and the dependency graph the imports
/// come from is worth gating on its own rather than only through a language that happens to
/// be installed.
/// </summary>
[Collection("conformance-tree")]
public class PythonPackageTests
{
    /// <summary>
    /// Both corpora with a Python target, which between them cover what the graph has edges
    /// for: `conformance` has enums and two tables referencing each other's rows,
    /// `reserved-words` has a table whose every field is named after a keyword.
    /// </summary>
    private static readonly string[] Scenarios = { "conformance", "reserved-words" };

    /// <summary>`from .module import A, B` - the only import form the generator emits.</summary>
    /// <summary>
    /// A relative import, wherever it sits. Indented ones count: a table reaches the
    /// accessor for the encryption key and the accessor imports the table back, so that one
    /// import is inside the method that uses it - at import time the accessor is half built
    /// and at call time it is not. The name is no less available for being asked for late.
    /// </summary>
    private static readonly Regex RelativeImport = new Regex(
        @"^[ \t]*from \.(?<module>\w+) import (?<names>.+)$", RegexOptions.Multiline);

    /// <summary>A top level `class Name` or `class Name(Base)`.</summary>
    private static readonly Regex Declaration = new Regex(
        @"^class (?<name>\w+)", RegexOptions.Multiline);

    [Fact]
    public void Every_relative_import_resolves_to_a_module_that_declares_the_name()
    {
        var offenders = new List<string>();

        foreach (var package in Packages())
        {
            var declaredBy = package.Modules.ToDictionary(
                module => module.Key,
                module => new HashSet<string>(
                    Declaration.Matches(module.Value).Select(m => m.Groups["name"].Value)));

            foreach (var module in package.Modules)
            {
                foreach (Match import in RelativeImport.Matches(module.Value))
                {
                    string target = import.Groups["module"].Value;

                    // The reader is emitted rather than generated, so it declares its names
                    // somewhere this does not parse. Its presence is all that is checked.
                    if (target == "tabbit")
                    {
                        if (!package.Modules.ContainsKey("tabbit"))
                            offenders.Add($"  {package.Name}/{module.Key}.py imports the reader, which is not there");

                        continue;
                    }

                    if (!declaredBy.TryGetValue(target, out var declared))
                    {
                        offenders.Add($"  {package.Name}/{module.Key}.py: `{import.Value}` - no module `{target}.py`");
                        continue;
                    }

                    foreach (string name in Names(import))
                    {
                        if (!declared.Contains(name))
                            offenders.Add($"  {package.Name}/{module.Key}.py: `{import.Value}` - `{target}.py` declares no `{name}`");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Generated Python imports do not resolve:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The other direction, and the one that matters: every generated type a module names is
    /// one it declares itself or imports.
    ///
    /// An import that resolves to nothing is caught by the test above, and would be caught by
    /// anything that ran the code. A *missing* import is not: the module is well formed, it
    /// imports less than it needs, and it fails at the moment the interpreter reaches the
    /// name - which for a table nothing loads is never. That is the failure mode of getting
    /// the dependency graph wrong, so it gets the gate.
    /// </summary>
    [Fact]
    public void Every_generated_type_a_module_names_is_declared_there_or_imported()
    {
        var offenders = new List<string>();

        foreach (var package in Packages())
        {
            // Every generated type in the package, and which module declares it.
            //
            // Not the reader's: it is emitted rather than generated, and everything reaches
            // its classes through the module - `tabbit.Reader` - so an unqualified `Reader`
            // in a generated file would be a different type entirely.
            var declaringModule = new Dictionary<string, string>();

            foreach (var module in package.Modules.Where(m => m.Key != "tabbit"))
            {
                foreach (Match declaration in Declaration.Matches(module.Value))
                    declaringModule[declaration.Groups["name"].Value] = module.Key;
            }

            foreach (var module in package.Modules)
            {
                // `__init__` imports everything by construction and is checked separately.
                if (module.Key == "__init__")
                    continue;

                string code = Code(module.Value);

                var available = new HashSet<string>(
                    Declaration.Matches(module.Value).Select(m => m.Groups["name"].Value));

                foreach (Match import in RelativeImport.Matches(module.Value))
                    available.UnionWith(Names(import));

                foreach (var pair in declaringModule)
                {
                    if (available.Contains(pair.Key))
                        continue;

                    if (Regex.IsMatch(code, $@"\b{Regex.Escape(pair.Key)}\b"))
                    {
                        offenders.Add(
                            $"  {package.Name}/{module.Key}.py names `{pair.Key}` and neither declares " +
                            $"nor imports it - it is in {pair.Value}.py");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"Generated Python modules name types they cannot see:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// `__all__` and the import lines above it have to agree, because they are read by
    /// different things: `from gamedata import *` reads the first and `gamedata.Name` the
    /// second. A name in one and not the other is a type that exists and cannot be reached,
    /// or a name that can be reached one way and not the other.
    /// </summary>
    [Fact]
    public void The_package_init_re_exports_exactly_what_it_imports()
    {
        var quoted = new Regex(@"^\s*""(?<name>\w+)"",$", RegexOptions.Multiline);

        foreach (var package in Packages())
        {
            string init = package.Modules["__init__"];

            var imported = RelativeImport.Matches(init).SelectMany(Names).ToList();
            var exported = quoted.Matches(init).Select(m => m.Groups["name"].Value).ToList();

            Assert.Equal(imported, exported);

            // And every one of them is a generated type, not a module that leaked in.
            Assert.Contains("Tables", exported);
            Assert.DoesNotContain("tabbit", exported);
        }
    }

    /// <summary>
    /// A star import would re-export whatever else a module happens to hold - `enum`, `os`,
    /// the reader - and leave a consumer no way to see what the package offers.
    /// </summary>
    [Fact]
    public void Nothing_is_re_exported_with_a_star()
    {
        foreach (var package in Packages())
        {
            foreach (var module in package.Modules)
                Assert.DoesNotContain("import *", module.Value);
        }
    }

    /// <summary>
    /// Each generated module declares one thing, which is what makes deleting a table from
    /// the sheets remove a file rather than edit one.
    /// </summary>
    [Fact]
    public void A_table_gets_a_module_of_its_own()
    {
        foreach (var package in Packages())
        {
            var tableModules = package.Modules
                .Where(module => module.Key.EndsWith("_table", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(tableModules);

            foreach (var module in tableModules)
            {
                // A record and its table, and nothing else.
                var declared = Declaration.Matches(module.Value)
                    .Select(m => m.Groups["name"].Value).ToList();

                Assert.Equal(2, declared.Count);
                Assert.EndsWith("Record", declared[0]);
                Assert.EndsWith("Table", declared[1]);
            }
        }
    }

    // --------------------------------------------------------------- corpus

    private sealed class Package
    {
        public string Name;

        /// <summary>Module name without the extension, to the file's text.</summary>
        public Dictionary<string, string> Modules;
    }

    private static IReadOnlyList<Package> Packages()
    {
        var packages = new List<Package>();

        foreach (var scenario in Scenarios)
        {
            var conversion = TabbitRunner.Convert(scenario);

            Assert.True(conversion.Succeeded,
                $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

            string root = Path.Combine(RepoLayout.OutputDir(scenario), "python");

            Assert.True(Directory.Exists(root), $"`{scenario}` generated no Python at {root}.");

            // One directory below, named after the package.
            foreach (var directory in Directory.GetDirectories(root))
            {
                packages.Add(new Package
                {
                    Name = Path.GetFileName(directory),
                    Modules = Directory.GetFiles(directory, "*.py").ToDictionary(
                        path => Path.GetFileNameWithoutExtension(path),
                        path => File.ReadAllText(path)),
                });
            }
        }

        Assert.NotEmpty(packages);

        return packages;
    }

    private static IEnumerable<string> Names(Match import)
        => import.Groups["names"].Value.Split(',').Select(name => name.Trim());

    /// <summary>
    /// The module with its prose removed, so that a type named in a docstring or a comment is
    /// not mistaken for one the code reaches for.
    /// </summary>
    /// <remarks>
    /// Import lines go too. They are what the caller is checking against, and leaving them in
    /// would make every import its own justification.
    /// </remarks>
    private static string Code(string module)
    {
        string withoutDocstrings = Regex.Replace(module, "\"\"\".*?\"\"\"", "", RegexOptions.Singleline);
        string withoutComments = Regex.Replace(withoutDocstrings, "#.*$", "", RegexOptions.Multiline);

        return RelativeImport.Replace(withoutComments, "");
    }
}
