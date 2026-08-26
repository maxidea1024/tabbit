using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SheetLocation = Tabbit.Models.Location;
using RoslynSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Tabbit.Validation;

/// <summary>One rule file compiled and ready to run.</summary>
internal sealed class CompiledRule
{
    internal CompiledRule(RuleFile file, MethodInfo entryPoint)
    {
        File = file;
        EntryPoint = entryPoint;
    }

    public RuleFile File { get; }

    private MethodInfo EntryPoint { get; }

    /// <summary>
    /// Runs the rule, handing it the context it reports through.
    /// </summary>
    /// <remarks>
    /// `Validate` may return a Task - a rule awaiting a database query is ordinary - and that is waited
    /// on here rather than constrained in the file.
    /// </remarks>
    public void Invoke(IPreContext context)
    {
        object result;

        try
        {
            result = EntryPoint.Invoke(null, [context])!;
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
        {
            // Reflection's wrapper says nothing; the rule's own exception says everything.
            throw wrapped.InnerException;
        }

        if (result is Task task)
            task.GetAwaiter().GetResult();
    }
}

/// <summary>
/// Compiles rule files as ordinary C#, in this process.
/// </summary>
/// <remarks>
/// Plain <see cref="CSharpCompilation"/> rather than the Roslyn scripting layer, because what
/// scripting adds - `#r`, `#load`, a globals object - is what a host makes unnecessary.
/// References arrive as <see cref="MetadataReference"/>s and shared code arrives as another
/// syntax tree in the same compilation, so a rule file needs no directive of its own.
/// spec/validation/validation-pipeline.md §3.
///
/// One compilation per rule file, which is a choice here rather than a language constraint.
/// Two things follow and both are wanted - shared static state is per file, so a parallel stage
/// has nothing to race on, and one file's compile error does not stop another from being
/// compiled and reported.
/// </remarks>
internal sealed class RuleCompiler
{
    /// <summary>
    /// What every rule file gets without asking.
    /// </summary>
    /// <remarks>
    /// A separate syntax tree rather than text prepended to the file, so the file's own line
    /// numbers stay its own - a report has to name the line an author is looking at.
    /// </remarks>
    private const string Preamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        """;

    /// <summary>
    /// The same list once there is an accessor, and deliberately no more.
    /// </summary>
    /// <remarks>
    /// `Tables` and `Context` are named by ordinary `using` lines in the rule file itself. A name
    /// that arrives from a synthesized file is a name an editor cannot resolve until it compiles
    /// that file, which is what broke completion in three earlier attempts. What stays here is
    /// only what the language gives a modern project anyway - the generated project says
    /// `ImplicitUsings`, and this is its equivalent.
    /// </remarks>
    private static string PreambleWithData => Preamble;

    /// <summary>What a rule file calls its entry.</summary>
    private const string EntryMethod = "Validate";

    /// <summary>
    /// The context type each stage hands over, which is also the signature its rules must have.
    /// </summary>
    /// <remarks>
    /// One type per stage rather than one for all of them, so that what a rule may reach for is
    /// decided by where its file sits. A `pre` rule asking for the tables used to compile and then
    /// fail at run time with a message naming the folder to move to; now the name is not on the
    /// type it was handed, so an editor says so while it is being typed.
    ///
    /// The types nest - table and runtime both extend the global one - so `shared/` helpers can
    /// take the widest thing they actually use and be called from every stage that has it.
    /// </remarks>
    internal static Type ContextTypeOf(RuleStage stage) => stage switch
    {
        RuleStage.Pre => typeof(IPreContext),
        RuleStage.Table => typeof(ITableContext),
        RuleStage.Runtime => typeof(IRuntimeContext),
        _ => typeof(IGlobalContext),
    };

    private static readonly CSharpParseOptions ParseOptions =
        new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    private static readonly Dictionary<string, ReportDiagnostic> Suppressed =
        new Dictionary<string, ReportDiagnostic>
        {
            // An unused using, and a using that repeats one of the preamble's global ones.
            // Both are the normal state of a rule file rather than something to tell an
            // author about.
            ["CS8019"] = ReportDiagnostic.Suppress,
            ["CS8933"] = ReportDiagnostic.Suppress,

            // "Assuming assembly reference X matches Y". The framework a rule compiles against
            // is this process's own, and a package inside it that was built against an older
            // framework says this about every compilation. It is true, it is unavoidable, and it
            // is nothing an author can act on.
            ["CS1701"] = ReportDiagnostic.Suppress,
            ["CS1702"] = ReportDiagnostic.Suppress,
        };

    private readonly List<MetadataReference> _references;
    private readonly List<SyntaxTree> _sharedTrees = new List<SyntaxTree>();
    private SyntaxTree _preambleTree;
    private readonly AssemblyLoadContext _context;
    private int _serial;

    internal RuleCompiler(RuleFolders folders)
    {
        _references = FrameworkReferences();
        _preambleTree = CSharpSyntaxTree.ParseText(Source(Preamble), ParseOptions, path: "<preamble>");

        // Collectible so a long-running process - the test suite, or `--serve` - does not
        // accumulate one assembly per rule file per conversion.
        _context = new AssemblyLoadContext("Tabbit.Validation", isCollectible: true);

        foreach (string path in folders.SharedSources)
        {
            _sharedTrees.Add(CSharpSyntaxTree.ParseText(
                Source(ReadSource(path)), ParseOptions, path: path));
        }
    }

    /// <summary>What every rule compilation is given: the framework, and this assembly.</summary>
    internal IReadOnlyList<MetadataReference> References => _references;

    /// <summary>
    /// Where every assembly of this validation run is loaded.
    /// </summary>
    /// <remarks>
    /// One for the run, shared with the generated accessor, because a rule assembly has to be
    /// able to resolve it - two contexts means the rule compiles against types it cannot load,
    /// and the failure arrives as a FileNotFoundException naming an assembly nobody wrote.
    /// Collectible, so a long-running process does not accumulate one set per conversion.
    /// </remarks>
    internal AssemblyLoadContext LoadContext => _context;

    /// <summary>
    /// Adds the generated accessor, so the rules compiled from here can read the data.
    /// </summary>
    /// <remarks>
    /// Called between the stages rather than in the constructor, because the accessor is built
    /// from a cooked model and the `pre` rules run before there is one.
    /// </remarks>
    internal void UseAccessor(RuleAccessor accessor)
    {
        _references.Add(accessor.Reference);

        _preambleTree = CSharpSyntaxTree.ParseText(
            Source(PreambleWithData), ParseOptions, path: "<preamble>");
    }

    /// <summary>
    /// Compiles one rule file, or reports why it did not compile and answers null.
    /// </summary>
    /// <remarks>
    /// Reporting rather than throwing, so a folder of 141 files answers with every broken one
    /// at once. A compile error goes through the same collector as a data error and in the same
    /// shape - file, line, column - because to the person fixing it there is no difference
    /// worth a second format.
    /// </remarks>
    internal CompiledRule? Compile(RuleFile file, Diagnostics diagnostics)
    {
        var trees = new List<SyntaxTree> { _preambleTree };

        trees.Add(CSharpSyntaxTree.ParseText(
            Source(ReadSource(file.Path)), ParseOptions, path: file.Path));

        trees.AddRange(_sharedTrees);

        // The name has to be unique per assembly in the load context, and the counter behind
        // it is shared by every thread compiling a rule.
        int serial;

        lock (_context)
            serial = ++_serial;

        var compilation = CSharpCompilation.Create(
            $"Tabbit.Rule.{file.Stage}.{file.Name}.{serial}",
            trees,
            _references,
            new CSharpCompilationOptions(
                // A library, always. A rule file is a class with a `Validate(Context)` method, so there
                // is no entry point to synthesize and nothing to run as a program.
                OutputKind.DynamicallyLinkedLibrary,

                // Debug rather than Release: a rule that throws should name the line it threw
                // on, and that needs sequence points and the symbols emitted below.
                optimizationLevel: OptimizationLevel.Debug,
                specificDiagnosticOptions: Suppressed));

        using var assemblyStream = new MemoryStream();
        using var symbolStream = new MemoryStream();

        // Portable symbols rather than the platform's own. Windows PDBs are written by
        // `diasymreader.dll`, which is part of a .NET installation and not of a self-contained
        // publish - so asking for them there fails with a message about a version, on a build
        // where nobody installed anything. Portable ones are written by Roslyn itself, work
        // everywhere, and are what the line numbers in a rule's stack trace come from either way.
        var emitted = compilation.Emit(assemblyStream, symbolStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        if (!ReportCompileProblems(file, emitted.Diagnostics, diagnostics) || !emitted.Success)
            return null;

        assemblyStream.Position = 0;
        symbolStream.Position = 0;

        Assembly assembly;

        lock (_context)
            assembly = _context.LoadFromStream(assemblyStream, symbolStream);

        var entry = FindEntryMethod(assembly, trees[1], file, diagnostics);

        if (entry is null)
            return null;

        return new CompiledRule(file, entry);
    }

    /// <summary>
    /// The entry method of a rule file written as declarations.
    /// </summary>
    /// <remarks>
    /// Found through the syntax tree rather than by scanning the assembly, because the assembly
    /// also holds every `shared/` type and one of those may have an entry of its own. The file says
    /// which type is its own.
    /// </remarks>
    private static MethodInfo? FindEntryMethod(
        Assembly assembly, SyntaxTree tree, RuleFile file, Diagnostics diagnostics)
    {
        var declared = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Identifier.ValueText == EntryMethod
                             && method.ParameterList.Parameters.Count == 1
                             && method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            .ToList();

        string contextName = ContextTypeOf(file.Stage).Name;

        if (declared.Count == 0)
        {
            diagnostics.Error(SheetLocation.OfTextFile(file.Path, 1, 1),
                Messages.Message.Of(ValidationMessages.RuleHasNoEntry,
                    ("File", file.Display), ("Entry", EntryMethod),
                    ("Context", contextName), ("SharedPath", RuleFolders.SharedPath)));

            return null;
        }

        if (declared.Count > 1)
        {
            var second = declared[1].Identifier.GetLocation().GetMappedLineSpan();

            diagnostics.Error(
                SheetLocation.OfTextFile(file.Path, second.StartLinePosition.Line + 1, 1),
                Messages.Message.Of(ValidationMessages.RuleEntryDeclaredTwice,
                    ("File", file.Display), ("Entry", EntryMethod)));

            return null;
        }

        string owner = OwnerName(declared[0]);

        var type = assembly.GetTypes().FirstOrDefault(candidate => candidate.FullName == owner)
                   ?? assembly.GetTypes().FirstOrDefault(candidate => candidate.Name == owner);

        var method = type?.GetMethod(EntryMethod,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null, types: [ContextTypeOf(file.Stage)], modifiers: null);

        if (method is null)
        {
            diagnostics.Error(SheetLocation.OfTextFile(file.Path, 1, 1),
                Messages.Message.Of(ValidationMessages.RuleEntryWrongContext,
                    ("File", file.Display), ("Owner", owner), ("Entry", EntryMethod),
                    ("Context", contextName),
                    ("Folder", RuleFolders.FolderOf(file.Stage))));
        }

        return method;
    }

    /// <summary>The dotted name of the type a method is declared in, namespaces included.</summary>
    private static string OwnerName(MethodDeclarationSyntax method)
    {
        var names = new List<string>();

        for (var node = method.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax type:
                    names.Insert(0, type.Identifier.ValueText);
                    break;

                case BaseNamespaceDeclarationSyntax space:
                    names.Insert(0, space.Name.ToString());
                    break;
            }
        }

        return string.Join(".", names);
    }

    /// <summary>
    /// Turns the compiler's own diagnostics into ours.
    /// </summary>
    /// <returns>False when anything was an error, so the caller does not run it.</returns>
    private static bool ReportCompileProblems(
        RuleFile file, IEnumerable<Diagnostic> problems, Diagnostics diagnostics)
    {
        bool clean = true;

        foreach (var problem in problems)
        {
            if (problem.Severity == RoslynSeverity.Hidden)
                continue;

            var span = problem.Location.GetMappedLineSpan();
            string path = string.IsNullOrEmpty(span.Path) ? file.Path : span.Path;

            var where = SheetLocation.OfTextFile(
                path, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);

            if (problem.Severity == RoslynSeverity.Error)
            {
                clean = false;
                diagnostics.Error(where,
                    Messages.Message.Of(ValidationMessages.RuleCompileError,
                        ("File", file.Display), ("Detail", Explain(file, problem))));
            }
            else if (problem.Severity == RoslynSeverity.Warning)
            {
                diagnostics.Warn(where,
                    Messages.Message.Of(ValidationMessages.RuleCompileWarning,
                        ("File", file.Display), ("Detail", problem.GetMessage())));
            }
        }

        return clean;
    }

    /// <summary>
    /// A compiler error, with the ones a rule author is likely to meet put in the terms of
    /// this pipeline rather than of the language.
    /// </summary>
    private static string Explain(RuleFile file, Diagnostic problem)
    {
        string message = problem.GetMessage();

        return problem.Id switch
        {
            // "'X' does not contain a definition for 'Y'". Every stage hands over a different
            // context, so the usual cause is a rule sitting in the wrong folder - which the
            // compiler cannot know and this can.
            "CS1061" or "CS0117" => ExplainMissingMember(file, message),

            // "The name 'Tables' does not exist" and "The type or namespace name 'Rules' does not
            // exist". Both mean the accessor, which the `pre` stage runs before there is one.
            "CS0103" or "CS0246" => ExplainMissingAccessor(file, message),

            // "Program does not contain a static 'Main' method suitable for an entry point"
            "CS5001" =>
                "this file has no statements to run. A rule file is a list of statements rather "
                + $"than a class; put helpers in `{RuleFolders.SharedPath}/`.",

            // "Only one compilation unit can have top-level statements"
            "CS8802" =>
                $"{message} This is a bug in Tabbit: every rule file is meant to be compiled "
                + "on its own.",

            _ => message,
        };
    }

    /// <summary>
    /// Members only some stages carry, and which stage carries each.
    /// </summary>
    /// <remarks>
    /// The list a rule author is most likely to reach across a stage boundary for. It exists to
    /// turn "does not contain a definition for `Db`" into the folder the file belongs in.
    /// </remarks>
    private static readonly (string Member, RuleStage Stage)[] StageOnlyMembers =
    [
        ("Db", RuleStage.Runtime),
        ("Redis", RuleStage.Runtime),
        ("Table", RuleStage.Table),
        ("Schema", RuleStage.Global),
        ("ErrorAtRow", RuleStage.Global),
        ("WarnAtRow", RuleStage.Global),
    ];

    /// <summary>A missing member, with the stage that has it named when this is one of those.</summary>
    private static string ExplainMissingMember(RuleFile file, string message)
    {
        foreach (var (member, stage) in StageOnlyMembers)
        {
            if (!message.Contains($"'{member}'", StringComparison.Ordinal) || stage == file.Stage)
                continue;

            return $"{message} `{member}` is on the context `{RuleFolders.FolderOf(stage)}/` hands "
                   + $"over, and this file is in `{RuleFolders.FolderOf(file.Stage)}/`. Each stage "
                   + $"is given what it can answer for - move the file, or check something this "
                   + $"stage has.";
        }

        return message;
    }

    /// <summary>
    /// A name that is missing because the accessor does not exist yet.
    /// </summary>
    /// <remarks>
    /// Only in `pre`, and only worth saying there: everywhere else an unresolved name is an
    /// ordinary typo and the compiler's own message is the better one.
    /// </remarks>
    private static string ExplainMissingAccessor(RuleFile file, string message)
    {
        if (file.Stage != RuleStage.Pre)
            return message;

        bool accessor = message.Contains($"'{RuleAccessor.AccessorType}'", StringComparison.Ordinal)
                        || message.Contains($"'{RuleAccessor.Namespace}'", StringComparison.Ordinal)
                        || message.Contains($"'{RuleAccessor.Namespace.Split('.').Last()}'", StringComparison.Ordinal);

        if (!accessor)
            return message;

        return $"{message} `{RuleFolders.FolderOf(RuleStage.Pre)}/` runs before a sheet is opened, "
               + $"so the accessor it would come from does not exist yet. A rule that reads the "
               + $"data belongs in `{RuleFolders.FolderOf(RuleStage.Table)}/` or "
               + $"`{RuleFolders.FolderOf(RuleStage.Global)}/`.";
    }

    /// <summary>
    /// Source text an emit with symbols will accept.
    /// </summary>
    /// <remarks>
    /// The encoding is not decoration: a <see cref="SourceText"/> without one cannot carry
    /// debug information, and the emit fails with a message about encoding rather than about
    /// the rule. Symbols are what let a rule that throws name the line it threw on, so this is
    /// what pays for that.
    /// </remarks>
    private static SourceText Source(string text) => SourceText.From(text, Encoding.UTF8);

    /// <summary>Reads a rule file, with the SDK's own directives taken out of the way.</summary>
    /// <remarks>
    /// `#:project`, `#:package` and `#:sdk` are how a project-less `.cs` file names what it needs,
    /// and the SDK strips them before the compiler sees them - they are not C# syntax. Nothing this
    /// tool writes uses them, but a file may carry one, and a rule that fails to compile over a
    /// line the SDK would have removed is a poor way to find that out.
    ///
    /// Blanked rather than removed, so every line number stays where the author sees it.
    /// </remarks>
    private static string ReadSource(string path)
    {
        string[] lines = File.ReadAllLines(path);

        for (int at = 0; at < lines.Length; at++)
        {
            if (lines[at].TrimStart().StartsWith("#:", StringComparison.Ordinal))
                lines[at] = "";
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Everything this process is running against, plus this assembly.
    /// </summary>
    /// <remarks>
    /// Taken from the runtime's own list rather than assembled by hand, so a rule file may use
    /// any part of the framework the converter itself can - which is the point of choosing a
    /// real language rather than a sandbox somebody has to maintain.
    ///
    /// It reads paths, so it does not survive a single-file publish, where the assemblies live
    /// inside the executable and have no path to open. That is reported rather than left to fail
    /// as a missing type: the alternative is a published binary whose validation cannot compile
    /// anything, reporting that `object` is undefined.
    /// </remarks>
    internal static List<MetadataReference> FrameworkReferences()
        => new List<MetadataReference>(CarriedReferences.Of(CarriedReferences.ForRules));

    /// <summary>
    /// One carried file by name, or null when this build carries none of that name.
    /// </summary>
    /// <remarks>
    /// For the editor's project, which needs the contract as a file beside the rules rather than
    /// as metadata in memory. Taken from the archive rather than from disk because in a
    /// single-file build there is no disk copy to take - and because a project that referenced
    /// one would then name a path on the machine that ran the conversion.
    /// </remarks>
    internal static byte[]? ReadCarriedFile(string name)
        => CarriedReferences.File(CarriedReferences.ForRules, name);
}
