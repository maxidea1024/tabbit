using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Tabbit.Recipe;

namespace Tabbit.Validation;


/// <summary>One rule file found on disk.</summary>
public sealed class RuleFile
{
    internal RuleFile(RuleStage stage, string path)
    {
        Stage = stage;
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);

        Subject = Name.Length > RuleFolders.RuleSuffix.Length
                  && Name.EndsWith(RuleFolders.RuleSuffix, StringComparison.Ordinal)
            ? Name[..^RuleFolders.RuleSuffix.Length]
            : null;
    }

    /// <summary>Which folder it came from, which is when it runs.</summary>
    public RuleStage Stage { get; }

    /// <summary>Absolute path.</summary>
    public string Path { get; }

    /// <summary>
    /// File name without its extension - `ItemRules` for `tables/ItemRules.cs`.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// What the rule is about: the name without its suffix, or null when the name carries none.
    /// For a table rule this is the table it is about.
    /// </summary>
    /// <remarks>
    /// The suffix is what keeps a rule file and the generated record type for the same table from
    /// both being `Item.cs` in one project, and it puts a file name back in step with the class
    /// inside it.
    /// </remarks>
    public string? Subject { get; }

    /// <summary>Path as a report writes it: relative to the validation root.</summary>
    public string Display { get; internal set; } = "";

    /// <summary>
    /// The tier the file declares, or null when it declares none or the value is not a plain
    /// number.
    /// </summary>
    public int? Tier { get; internal set; }

    /// <summary>
    /// Whether the file carries the attribute at all, readable value or not.
    /// </summary>
    /// <remarks>
    /// Apart from <see cref="Tier"/> because the two disagree in the case worth reporting: an
    /// attribute whose argument is not a plain number is a tier somebody meant and nobody gets.
    /// </remarks>
    public bool DeclaresTier { get; internal set; }

    /// <summary>The tier this file runs in, its own or the default.</summary>
    public int EffectiveTier => Tier ?? RuleFolders.DefaultTier;

    public override string ToString() => Display ?? Path;
}

/// <summary>
/// The layout of a validation folder, checked and enumerated.
/// </summary>
/// <remarks>
/// The folder is the whole interface: a project adds a rule by putting a file in it, and
/// this repository learns nothing about that rule. What is here is only the layout - which
/// subfolder means what, and which mistakes in it are reported rather than passed over.
///
/// spec/validation-pipeline.md.
/// </remarks>
public sealed class RuleFolders
{
    /// <summary>Folder name per stage, and the only names allowed beside `shared`.</summary>
    private static readonly (RuleStage Stage, string Folder)[] StageFolders =
    [
        (RuleStage.Pre, "pre"),
        (RuleStage.Table, "tables"),
        (RuleStage.Global, "global"),
        (RuleStage.Runtime, "runtime"),
    ];

    /// <summary>
    /// The one folder the stages sit in, so that what a person writes and what this tool writes
    /// are told apart at a glance.
    /// </summary>
    /// <remarks>
    /// The stages used to be at the root, next to `lib`, `.generated` and a project file - so a
    /// listing of a validation folder mixed four hand-written folders with three generated ones and
    /// gave no clue which was which. Everything under here is written by hand and nothing else is.
    /// </remarks>
    public const string RulesFolder = "rules";

    /// <summary>Code the rules share. Never executed on its own.</summary>
    public const string SharedFolder = "shared";

    /// <summary>
    /// What a rule file's name ends with, and the class inside it too.
    /// </summary>
    /// <remarks>
    /// Required of a table rule, because that is where the name is read rather than only written:
    /// `ItemRules.cs` is the rule for `Item`. Elsewhere it is a convention the scaffolding and the
    /// samples keep, since nothing binds a `global/` file to anything.
    /// </remarks>
    public const string RuleSuffix = "Rules";

    /// <summary>
    /// The tier a rule file that declares none runs in.
    /// </summary>
    /// <remarks>
    /// Zero rather than first or last, so a folder that marks one rule can put it on either side
    /// of everything unmarked without having to mark the rest.
    /// </remarks>
    public const int DefaultTier = 0;

    /// <summary>What a rule file declares its tier with, suffix left off.</summary>
    private const string TierAttribute = "RulePriority";

    /// <summary>
    /// What this tool writes for the IDE. Ours, rewritten every run, and never scanned for
    /// rules.
    /// </summary>
    public const string GeneratedFolder = ".generated";

    /// <summary>
    /// Where the contract is written, so the editor's project can name it by a relative path.
    /// </summary>
    /// <remarks>
    /// Ours as well, and committed - which is what lets a clone have completion before anything
    /// has been run. Not a dot folder, because unlike the generated sources these are meant to be
    /// seen in a listing.
    /// </remarks>
    public const string ContractFolder = "lib";

    /// <summary>
    /// Where building the editor's project puts its output, hidden and in one place.
    /// </summary>
    /// <remarks>
    /// A dot folder because this one is meant to be opened and read: `bin/` and `obj/` at its top
    /// were the first two things a listing showed, ahead of the rules. The project relocates them
    /// itself rather than this being a name to ignore - a folder that is not created needs no
    /// ignore rule, in this repository or in the one that holds the rules.
    /// </remarks>
    public const string BuildFolder = ".build";

    /// <summary>
    /// Folders a build or an editor leaves behind, which are not rule folders and never were.
    /// </summary>
    /// <remarks>
    /// Only reachable because the editor's project sits at this folder's root - which it has to,
    /// for an editor to find it - and building it puts its output beside it. Reporting those as an
    /// unknown stage would be this tool objecting to its own artifacts.
    ///
    /// `.vs` is here for the same reason and arrives the same way: the documented way to get
    /// completion is to open this folder in an editor, and Visual Studio writes its cache into it
    /// the moment you do. Refusing that is refusing the thing the documentation asked for.
    /// </remarks>
    private static readonly string[] BuildFolders =
        [BuildFolder, "bin", "obj", ".vs", ".vscode", ".idea", ContractFolder];

    private readonly Dictionary<RuleStage, List<RuleFile>> _byStage = new Dictionary<RuleStage, List<RuleFile>>();

    private RuleFolders(string root)
    {
        Root = root;

        foreach (var (stage, _) in StageFolders)
            _byStage[stage] = new List<RuleFile>();
    }

    /// <summary>Absolute path of the validation folder.</summary>
    public string Root { get; }

    /// <summary>Code the rules share, in no particular order.</summary>
    public IReadOnlyList<string> SharedSources { get; private set; } = Array.Empty<string>();

    /// <summary>Where the generated accessor and its IDE project go.</summary>
    public string GeneratedPath => Path.Combine(Root, GeneratedFolder);

    /// <summary>Rule files of one stage, in file-name order.</summary>
    public IReadOnlyList<RuleFile> Of(RuleStage stage) => _byStage[stage];

    /// <summary>The folder a stage's rules sit in, as a report and a message write it.</summary>
    public static string FolderOf(RuleStage stage)
        => $"{RulesFolder}/{StageFolders.First(entry => entry.Stage == stage).Folder}";

    /// <summary>Where the shared helpers sit, written the same way.</summary>
    public static string SharedPath => $"{RulesFolder}/{SharedFolder}";

    /// <summary>Every rule file, whatever its stage.</summary>
    public IEnumerable<RuleFile> All => StageFolders.SelectMany(entry => _byStage[entry.Stage]);

    /// <summary>
    /// Reads the layout, or returns null when the recipe asks for no validation at all.
    /// </summary>
    /// <remarks>
    /// A blank `Path` is the only way to switch the pipeline off, and it is visible in a
    /// diff. A path that is set but missing is an error: the alternative is a run that
    /// reports nothing because of a typo and looks exactly like a run that passed.
    /// </remarks>
    public static RuleFolders? Discover(ValidationRecipe recipe)
    {
        if (recipe is null || string.IsNullOrWhiteSpace(recipe.Path))
            return null;

        string root = Path.GetFullPath(recipe.Path);

        if (!Directory.Exists(root))
        {
            throw new TabbitException(
                $"The recipe's `Validation.Path` is `{recipe.Path}`, which resolves to "
                + $"`{root}` - and there is no folder there. Create it with the "
                + $"`{RulesFolder}/pre/` `{RulesFolder}/tables/` `{RulesFolder}/global/` "
                + $"`{RulesFolder}/runtime/` layout, or clear `Validation.Path` to run without "
                + $"validation.");
        }

        var folders = new RuleFolders(root);

        folders.RefuseUnknownSubfolders();
        folders.CollectRules();
        folders.CollectSharedSources();

        return folders;
    }

    /// <summary>
    /// Reports a subfolder this layout has no meaning for.
    /// </summary>
    /// <remarks>
    /// `table/` instead of `tables/` is a folder whose rules never run, and nothing about
    /// the output would say so. Naming the ones that exist is cheaper than the afternoon
    /// spent finding out that a whole folder was ignored.
    /// </remarks>
    private void RefuseUnknownSubfolders()
    {
        RefuseStagesLeftAtTheRoot();

        Refuse(Root,
            BuildFolders.Append(RulesFolder).Append(GeneratedFolder),
            $"Rules go under `{RulesFolder}/`");

        string rules = Path.Combine(Root, RulesFolder);

        if (Directory.Exists(rules))
        {
            Refuse(rules,
                StageFolders.Select(entry => entry.Folder).Append(SharedFolder),
                "Use `pre`, `tables`, `global`, `runtime` or `shared`");
        }
    }

    /// <summary>Reports one folder's unexpected subfolders.</summary>
    private static void Refuse(string parent, IEnumerable<string> allowed, string advice)
    {
        var known = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);

        // Ordered, so the folder this names when there are several is the same one on every
        // platform - the message reports the first it meets, and an unordered scan made that
        // "whichever the filesystem handed over first".
        foreach (string directory in Helpers.PathNames.InOrder(Directory.EnumerateDirectories(parent)))
        {
            string name = Path.GetFileName(directory);

            // The same convention the sources use: a `#` prefix is work in progress.
            if (name.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (known.Contains(name))
                continue;

            throw new TabbitException(
                $"`{parent}` has a subfolder `{name}`, which is not one this layout runs. "
                + $"{advice} - or prefix it with `#` to have it skipped.");
        }
    }

    /// <summary>
    /// Reports a folder still laid out the way this tool used to read.
    /// </summary>
    /// <remarks>
    /// The stages moved under `rules/`, and a folder written before that has rules the run would
    /// otherwise pass over without a word - which is the one outcome worth a message of its own,
    /// because a validation that finds nothing looks exactly like a validation that passed.
    /// </remarks>
    private void RefuseStagesLeftAtTheRoot()
    {
        var stray = StageFolders
            .Select(entry => entry.Folder)
            .Append(SharedFolder)
            .Where(folder => Directory.Exists(Path.Combine(Root, folder)))
            .ToList();

        if (stray.Count == 0)
            return;

        throw new TabbitException(
            $"The validation folder `{Root}` has {string.Join(", ", stray.Select(name => $"`{name}/`"))} "
            + $"at its root, which is where the stages were before they moved under "
            + $"`{RulesFolder}/`. Move {(stray.Count == 1 ? "it" : "them")} there - the rules "
            + $"themselves need no change.");
    }

    /// <summary>Every rule file of every stage.</summary>
    private void CollectRules()
    {
        foreach (var (stage, folder) in StageFolders)
        {
            string path = Path.Combine(Root, RulesFolder, folder);
            if (!Directory.Exists(path))
                continue;

            // Flat, and deliberately: a rule file's folder is what decides when it runs and
            // what it is handed, so a file one level down would be a rule with no stage.
            // Shared code has a folder of its own for that.
            foreach (string file in Directory.EnumerateFiles(path, "*.cs").OrderBy(name => name, StringComparer.Ordinal))
            {
                var (declared, tier) = ReadTier(file);

                _byStage[stage].Add(new RuleFile(stage, Path.GetFullPath(file))
                {
                    Display = $"{RulesFolder}/{folder}/{Path.GetFileName(file)}",
                    DeclaresTier = declared,
                    Tier = tier,
                });
            }
        }
    }

    /// <summary>
    /// The tier a file declares, read as syntax rather than by compiling it.
    /// </summary>
    /// <remarks>
    /// Which tier runs when has to be settled before the first rule runs, and compilation happens
    /// one file at a time as each is reached - so this is read the same way the entry method is
    /// found, off the syntax tree.
    ///
    /// Only a plain number counts. A named constant would need the compilation this deliberately
    /// does not have, and the case is reported rather than passed over: the caller can tell an
    /// attribute nobody could read from a file that carries none.
    /// </remarks>
    private static (bool Declared, int? Tier) ReadTier(string path)
    {
        SyntaxNode root;

        try
        {
            root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
        }
        catch (IOException)
        {
            // Whatever is wrong with the file is said properly when it is compiled. Here it is
            // only a file with no tier in it.
            return (false, null);
        }

        foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
        {
            string name = attribute.Name.ToString();
            name = name[(name.LastIndexOf('.') + 1)..];

            if (name != TierAttribute && name != TierAttribute + nameof(Attribute))
                continue;

            return (true, ReadInteger(attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression!));
        }

        return (false, null);
    }

    /// <summary>A plain integer literal, its sign included, or null for anything else.</summary>
    private static int? ReadInteger(ExpressionSyntax expression)
    {
        int sign = 1;

        if (expression is PrefixUnaryExpressionSyntax prefix
            && (prefix.IsKind(SyntaxKind.UnaryMinusExpression) || prefix.IsKind(SyntaxKind.UnaryPlusExpression)))
        {
            sign = prefix.IsKind(SyntaxKind.UnaryMinusExpression) ? -1 : 1;
            expression = prefix.Operand;
        }

        return expression is LiteralExpressionSyntax literal && literal.Token.Value is int value
            ? sign * value
            : null;
    }

    /// <summary>Code under `shared/`, at any depth.</summary>
    /// <remarks>
    /// Recursive, unlike the rule folders: nothing here runs on its own, so a subfolder is
    /// somebody organizing their helpers rather than a rule with no stage.
    /// </remarks>
    private void CollectSharedSources()
    {
        string path = Path.Combine(Root, RulesFolder, SharedFolder);
        if (!Directory.Exists(path))
            return;

        SharedSources = Directory
            .EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>This file's path as a report writes it.</summary>
    public string Relative(string path)
    {
        string full = Path.GetFullPath(path);

        // Compared the way this platform's filesystem compares paths: on Linux a root of
        // `/x/rules` does not contain `/x/Rules`, and stripping it as though it did would
        // put a nonsense relative path in a report.
        return full.StartsWith(Root, Tabbit.Helpers.PathNames.Comparison)
            ? full.Substring(Root.Length).TrimStart('/', '\\').Replace('\\', '/')
            : full.Replace('\\', '/');
    }
}
