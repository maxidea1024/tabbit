using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Compiles generated C# into an assembly, for a project that wants one file instead of a hundred.
/// </summary>
/// <remarks>
/// The same work the validation host already does to give a rule typed data - generate, compile,
/// hand over - with the result written out instead of loaded. That path having existed and run on
/// every conversion for months is most of why this is a small file.
///
/// **netstandard2.1**, not this tool's framework: the consumer is somebody else's project. That is
/// also why the Unity adapter and the updater are still written as source beside it - they name
/// `UnityEngine`, which only the engine's own compiler can resolve. spec section 7.
/// </remarks>
internal static class CsAssemblyEmitter
{
    /// <summary>
    /// Compiles every `.cs` under a folder into one assembly.
    /// </summary>
    /// <param name="sources">Folder holding the generated sources, at any depth.</param>
    /// <param name="assemblyName">What the assembly is called.</param>
    /// <param name="skip">Files to leave out - what the engine has to compile itself.</param>
    /// <returns>The assembly, and its documentation.</returns>
    internal static (byte[] Assembly, byte[] Documentation) Emit(
        string sources, string assemblyName, IReadOnlyCollection<string> skip)
    {
        var trees = new List<SyntaxTree>();

        // Ordered, so the same sources produce the same assembly wherever this runs. The
        // filesystem's own order is not the same on ext4 as on NTFS, and the order trees go
        // into a compilation is visible in what comes out of it.
        foreach (string path in Helpers.PathNames.InOrder(
                     Directory.EnumerateFiles(sources, "*.cs", SearchOption.AllDirectories)))
        {
            if (skip.Any(name => path.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            // The encoding has to be given, or emitting symbols fails on a file that has any.
            trees.Add(CSharpSyntaxTree.ParseText(
                SourceText.From(File.ReadAllText(path), Encoding.UTF8),
                path: path));
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            CarriedReferences.Of(CarriedReferences.ForGeneratedCode),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,

                // Byte-for-byte reproducible. This lands in somebody's repository beside their
                // code, and a build that changed nothing about the data must not show up there as
                // a change.
                deterministic: true,

                // The generated code documents itself, and the one warning that says otherwise is
                // about a member the generator deliberately leaves undocumented.
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1591"] = ReportDiagnostic.Suppress,
                }));

        using var assembly = new MemoryStream();
        using var documentation = new MemoryStream();

        // Symbols inside the assembly rather than beside it: a consumer stepping into generated
        // code should not have to have kept a second file, and a `.pdb` next to a `.dll` is the
        // file that goes missing.
        var emitted = compilation.Emit(assembly, xmlDocumentationStream: documentation,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded));

        if (!emitted.Success)
        {
            var errors = emitted.Diagnostics
                .Where(problem => problem.Severity == DiagnosticSeverity.Error)
                .Take(5)
                .Select(problem => problem.ToString());

            // Whose fault it is and what to do about it used to be spelled out here, and
            // the exception's own type says both now.
            throw new TabbitDefectException(
                "The generated C# did not compile."
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        return (assembly.ToArray(), documentation.ToArray());
    }
}
