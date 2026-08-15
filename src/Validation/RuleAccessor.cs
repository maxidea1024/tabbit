using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Serilog;
using Tabbit.CodeGeneration;
using Tabbit.Exporters;
using Tabbit.History;
using Tabbit.Models;
using Tabbit.Recipe;
using Tabbit.Targets;
using SheetLocation = Tabbit.Models.Location;

namespace Tabbit.Validation;

/// <summary>
/// The typed view a rule file reads the data through: the project's own generated C#,
/// compiled and loaded with this run's data already in it.
/// </summary>
/// <remarks>
/// Generated whether or not the recipe asks for a csharp target, because a rule needs types
/// either way. When the recipe does ask, the same generator with the same settings produces the
/// same source - the difference is only where it lands.
///
/// Data arrives by a memory round trip: the binary exporter encodes each table into a buffer
/// and the generated reader is pointed at those buffers instead of at files. Nothing is written
/// and nothing is read from disk, and what a rule sees is what the consuming project will see -
/// including references resolved, which a view over the model would not give. The cost is one
/// encode and one decode, and the by-product is that every run exercises the generated code,
/// the reader and the format against real data.
///
/// spec/validation-pipeline.md §3.
/// </remarks>
internal sealed class RuleAccessor
{
    /// <summary>
    /// Namespace the generated accessor lands in, and the one a rule file's preamble opens.
    /// </summary>
    /// <remarks>
    /// Its own rather than the project's, so `Tables` cannot collide with a type the recipe's
    /// real csharp target puts in the same namespace - a rule file may end up seeing both.
    /// </remarks>
    internal const string Namespace = "Tabbit.Rules";

    /// <summary>
    /// What the accessor type is called here, whatever the recipe names its own.
    /// </summary>
    /// <remarks>
    /// A recipe's `AccessorName` renames the accessor the conversion writes for a project. This one
    /// is not that: it is built for the rules to read and nothing else, so its name is fixed and
    /// every rule file in every project opens the same one.
    /// </remarks>
    internal const string AccessorType = "Tables";

    /// <summary>Table file extension the round trip uses. Never written, so it only has to agree with itself.</summary>
    private const string Extension = ".tcb";

    /// <summary>
    /// What lets a rule write `context.Tables` rather than the accessor's static name.
    /// </summary>
    /// <remarks>
    /// **Written here rather than in the templates**, because it is true of the accessor built for
    /// validation and of no other. Every project gets an accessor from the same templates, and one
    /// carrying an extension on `IGlobalContext` would be an accessor that does not compile without
    /// this tool's contract assembly beside it.
    ///
    /// An extension property, which C# 14 allows. The compilation here is the host's - net10, latest
    /// language - so Unity's floor does not reach it; this assembly is never shipped to a game.
    ///
    /// The cast cannot fail: the object came from this same assembly's `LoadAsync`.
    /// spec/accessor-instances.md section 3.2.
    /// </remarks>
    private const string ContextBridge = """
        namespace Tabbit.Rules;

        /// <summary>Reaches this run's tables through the context a rule was handed.</summary>
        public static class RuleContextTables
        {
            extension(global::Tabbit.Validation.IGlobalContext context)
            {
                /// <summary>
                /// The tables this run read, typed - `context.Tables.Item.Records`.
                /// </summary>
                /// <remarks>
                /// The same data the static `Tables` answers with, reached through the run rather
                /// than through a global. Which one a rule uses is a choice: this one is the
                /// instance, and it is what a rule should reach for when either would do.
                /// </remarks>
                public global::Tabbit.Rules.Tables.Snapshot Tables
                    => (global::Tabbit.Rules.Tables.Snapshot)context.TableSnapshot;
            }
        }

        """;

    private RuleAccessor(
        MetadataReference reference, Assembly assembly, string sourcePath, object snapshot)
    {
        Reference = reference;
        Assembly = assembly;
        SourcePath = sourcePath;
        Snapshot = snapshot;
    }

    /// <summary>This run's accessor instance, which the context hands to the rules.</summary>
    internal object Snapshot { get; }

    /// <summary>The compiled accessor, for the rule compilations to reference.</summary>
    internal MetadataReference Reference { get; }

    /// <summary>The loaded accessor, already holding this run's data.</summary>
    internal Assembly Assembly { get; }

    /// <summary>Where the sources were written, for the IDE to open.</summary>
    internal string SourcePath { get; }

    /// <summary>
    /// Generates, compiles, loads and fills the accessor.
    /// </summary>
    /// <remarks>
    /// Reports rather than throws when the generated code does not compile: that is a defect in
    /// this repository rather than in a rule file, and the message has to say so, but it belongs
    /// in the same report as everything else so a run answers once.
    /// </remarks>
    internal static RuleAccessor? Build(
        Options options,
        RecipeModel recipe,
        Model model,
        RuleFolders folders,
        IReadOnlyList<MetadataReference> references,
        AssemblyLoadContext into,
        Diagnostics diagnostics)
    {
        // Into a folder of its own, and deleted at the end. These sources are read by the
        // compiler and by nothing else: what a project keeps is the assembly they become, which
        // is one file instead of the hundred a schema of any size produces.
        string sourcePath = Path.Combine(
            Path.GetTempPath(), "tabbit-accessor", Guid.NewGuid().ToString("N"));

        Generate(options, recipe, model, sourcePath);

        File.WriteAllText(Path.Combine(sourcePath, "RuleContextTables.cs"), ContextBridge);

        var compilation = Compile(sourcePath, references);

        using var assemblyStream = new MemoryStream();
        using var documentationStream = new MemoryStream();

        var emitted = compilation.Emit(assemblyStream, xmlDocumentationStream: documentationStream);

        if (!emitted.Success)
        {
            foreach (var problem in emitted.Diagnostics
                                          .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                                          .Take(10))
            {
                var span = problem.Location.GetMappedLineSpan();

                diagnostics.Error(
                    SheetLocation.OfTextFile(span.Path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1),
                    $"The accessor generated for validation does not compile: {problem.GetMessage()} "
                    + $"This is a defect in Tabbit rather than in a rule file.");
            }

            return null;
        }

        byte[] image = assemblyStream.ToArray();

        // What the project keeps: the assembly, beside the contract. An editor references it the
        // same way, and a validation folder holds two files instead of a hundred generated ones.
        if (recipe.Validation.EmitIdeProject)
            RuleScaffold.WriteIdeProject(folders, options, image, documentationStream.ToArray());

        Directory.Delete(sourcePath, recursive: true);

        // The same context the rules are loaded into, so a rule assembly can resolve the types
        // it was compiled against.
        var assembly = into.LoadFromStream(new MemoryStream(image));

        return new RuleAccessor(
            MetadataReference.CreateFromImage(image), assembly, sourcePath, Fill(assembly, model));
    }

    // ---------------------------------------------------------- generation

    /// <summary>
    /// Runs the C# generator into <paramref name="sourcePath"/>.
    /// </summary>
    /// <remarks>
    /// The real target, driven directly rather than reimplemented: the whole value of this is
    /// that a rule sees the same types the project's own code does, and a second generator
    /// written for validation would drift from the first.
    ///
    /// Target side is both, always. A narrowed accessor would be missing fields, and a rule
    /// reaching for one would fail to compile with a message about a column that is in the
    /// sheet - so the run is validated against everything the sheets declare, and narrowing
    /// stays a property of output.
    /// </remarks>
    private static void Generate(Options options, RecipeModel recipe, Model model, string sourcePath)
    {
        if (Directory.Exists(sourcePath))
            Directory.Delete(sourcePath, recursive: true);

        var entry = new RecipeModel.CodeGenerationRecipeGroup.CSharpRecipe
        {
            Path = sourcePath,
            Namespace = Namespace,
            AccessorName = "Tables",
            BinaryTableFileExtension = Extension,
            WriteUpdater = false,

            // Nothing to sweep - the folder was just emptied - and asking for one would
            // register a directory with the staging area, which this output is not part of.
            Sweep = false,
            TargetSide = "cs",
        };

        var generator = new CsCodeGenerator { WritesWithoutStaging = true };

        ITarget target = generator;

        target.Run(new TargetContext(
            options,
            recipe,
            model,
            model,
            new Lazy<CommitInfo>(() => null!),
            entry,
            "Validation.Path"));

        Log.Debug($"Generated the validation accessor into `{sourcePath}`.");
    }

    /// <summary>Compiles every generated source into one library.</summary>
    private static CSharpCompilation Compile(
        string sourcePath, IReadOnlyList<MetadataReference> references)
    {
        var trees = new List<SyntaxTree>();

        foreach (string path in Directory.EnumerateFiles(sourcePath, "*.cs", SearchOption.AllDirectories))
        {
            trees.Add(CSharpSyntaxTree.ParseText(
                SourceText.From(File.ReadAllText(path), Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None),
                path: path));
        }

        return CSharpCompilation.Create(
            "Tabbit.Rules.Data",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,

                // Byte-for-byte reproducible, because this assembly is written into a project's
                // validation folder and committed. Without it every conversion would be a diff
                // in somebody's repository that says nothing.
                deterministic: true,

                // The generated code is written for consumers who may or may not have nullable
                // reference types on, and it documents itself heavily. Neither is a rule
                // author's problem, so nothing here is reported to one.
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1591"] = ReportDiagnostic.Suppress,
                }));
    }

    // ---------------------------------------------------------------- data

    /// <summary>
    /// Loads this run's data into the accessor, through the generated reader.
    /// </summary>
    /// <remarks>
    /// The reader's file access is a replaceable delegate - the generated code offers that so a
    /// consumer can read from a pack file or a CDN - and this is the same door: it answers with
    /// a buffer the exporter just encoded. So the bytes are the file format, read by the shipped
    /// reader, without a file existing anywhere.
    /// </remarks>
    /// <returns>The snapshot it loaded, which is what `context.Tables` answers with.</returns>
    private static object Fill(Assembly assembly, Model model)
    {
        var tables = assembly.GetType($"{Namespace}.{AccessorType}", throwOnError: true);

        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in model.Tables)
            bytes.Add(table.Name + Extension, BinaryExporter.Encode(table).WrittenSpan.ToArray());

        var source = new ByteSource(bytes);

        var field = tables!.GetField("ReadAllBytesAsync", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new TabbitException(
                        "The generated accessor has no `ReadAllBytesAsync` to point at the "
                        + "encoded tables. This is a defect in Tabbit.");

        field.SetValue(null, Delegate.CreateDelegate(
            field.FieldType, source, typeof(ByteSource).GetMethod(nameof(ByteSource.Read))!));

        // Loaded and published as two steps rather than as `ReadAllAsync`, which is the two run
        // together. The instance is what the context hands to a rule; publishing it is what keeps
        // the static `Tables.Item` working for a rule that reaches that way instead.
        var load = tables.GetMethod("LoadAsync", BindingFlags.Public | BindingFlags.Static)
                   ?? throw new TabbitException(
                       "The generated accessor has no `LoadAsync`. This is a defect in Tabbit.");

        // An empty base path, because the delegate above keys on the file name alone. The
        // reader still builds a path and still asks for it, which is the point - nothing about
        // its own code path is special-cased for validation.
        var loading = (Task)load.Invoke(null, new object[] { "", Extension })!;

        loading.GetAwaiter().GetResult();

        object snapshot = loading.GetType().GetProperty("Result")?.GetValue(loading)
                          ?? throw new TabbitException(
                              "The generated accessor's `LoadAsync` answered with nothing. This is "
                              + "a defect in Tabbit.");

        var publish = tables.GetMethod("Publish", BindingFlags.Public | BindingFlags.Static)
                      ?? throw new TabbitException(
                          "The generated accessor has no `Publish`. This is a defect in Tabbit.");

        publish.Invoke(null, new[] { snapshot });

        return snapshot;
    }

    /// <summary>Answers the generated reader's file reads from memory.</summary>
    private sealed class ByteSource
    {
        private readonly Dictionary<string, byte[]> _bytes;

        internal ByteSource(Dictionary<string, byte[]> bytes) => _bytes = bytes;

        /// <summary>Public because the delegate is built from this method by reflection.</summary>
        public Task<byte[]> Read(string filename)
        {
            string name = Path.GetFileName(filename);

            return _bytes.TryGetValue(name, out byte[]? found)
                ? Task.FromResult(found)
                : throw new TabbitException(
                    $"Validation asked the generated reader for `{name}`, which this run did not "
                    + $"encode. This is a defect in Tabbit.");
        }
    }
}
