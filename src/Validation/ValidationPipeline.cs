using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Tabbit.Models;
using Tabbit.Recipe;
using SheetLocation = Tabbit.Models.Location;

namespace Tabbit.Validation;

/// <summary>
/// The validation stages of one run, and the gate they form.
/// </summary>
/// <remarks>
/// Two entry points, because the two halves see different things. `pre` runs before anything
/// is read and so has no model; the rest run on a cooked model and before any target, which is
/// what makes a failed run leave no trace - a database target swaps its shadow in while it
/// runs, so validating afterwards would land a failure on data that has already changed.
///
/// spec/validation/validation-pipeline.md §1.
/// </remarks>
public sealed class ValidationPipeline
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Validating;

    private readonly Options _options;
    private readonly RecipeModel _fullRecipe;
    private readonly ValidationRecipe _recipe;
    private readonly RuleFolders _folders;
    private readonly RuleCompiler _compiler;
    private CellLocator _cells = null!;
    private SchemaView _schema = null!;

    /// <summary>
    /// This run's accessor instance, which `context.Tables` answers with.
    /// </summary>
    /// <remarks>
    /// Held rather than read off the accessor's static `Current`, so that what a rule reaches
    /// through the context is genuinely the instance this run loaded - the point of the accessor
    /// being an object at all. Null until the sheets are read, which is why the `pre` context
    /// does not carry it.
    /// </remarks>
    private object _snapshot = null!;
    private RuntimeStores _stores = null!;
    private readonly ExternalFiles _files = new ExternalFiles();

    /// <summary>
    /// Where this stage's reports are kept so that they survive the run.
    /// </summary>
    /// <remarks>
    /// Null when the recipe asked for no report. Every stage below hands its whole collector
    /// over where it prints it, so the page holds what the console held rather than only what
    /// stopped the run. spec/ops/build-report.md.
    /// </remarks>
    private readonly Reporting.BuildReport? _report;

    private ValidationPipeline(
        Options options, RecipeModel recipe, RuleFolders folders, Reporting.BuildReport? report)
    {
        _options = options;
        _fullRecipe = recipe;
        _recipe = recipe.Validation;
        _folders = folders;
        _report = report;
        _compiler = new RuleCompiler(folders);
    }

    /// <summary>
    /// Reads the validation folder, or answers null when the recipe asks for none.
    /// </summary>
    /// <remarks>
    /// Called before the sources are imported, so a folder that does not exist is reported
    /// with nothing read yet.
    /// </remarks>
    public static ValidationPipeline? Create(
        Options options, RecipeModel recipe, Reporting.BuildReport? report = null)
    {
        var folders = RuleFolders.Discover(recipe.Validation);
        if (folders is null)
            return null;

        Log.Information($"Validation rules in `{folders.Root}`.");

        return new ValidationPipeline(options, recipe, folders, report);
    }

    /// <summary>
    /// Runs the `pre` rules: what can be checked before a workbook is opened.
    /// </summary>
    public void RunPre()
    {
        var diagnostics = NewDiagnostics();

        WarnIfEmpty(diagnostics);

        // Before anything runs, because both are a tier somebody wrote and nobody would get.
        RefuseTiersThatDoNothing(diagnostics);

        RunStage(RuleStage.Pre, diagnostics);

        Finish(diagnostics, "Pre-validation", _report);
    }

    /// <summary>
    /// Runs the rules that need the model: one stage per folder, in order.
    /// </summary>
    public void RunPost(Model model)
    {
        var diagnostics = NewDiagnostics();

        // Reported and then skipped: a rule for a table that is not there has nothing to
        // check, and running it would report a second problem about the first one.
        var orphaned = RefuseRulesForTablesThatDoNotExist(model, diagnostics);

        // Generate, compile and fill the project's own C# accessor, then let the rules
        // reference it. Everything below this reads the data through the same types the
        // consuming project uses.
        var accessor = RuleAccessor.Build(
            _options, _fullRecipe, model, _folders,
            _compiler.References, _compiler.LoadContext, diagnostics);

        if (accessor is null)
        {
            // The generated code did not compile, which is ours to fix rather than an
            // author's. Running the rules now would bury that under a hundred failures
            // about types that were never emitted.
            Finish(diagnostics, "Validation", _report);
            return;
        }

        _compiler.UseAccessor(accessor);
        _cells = new CellLocator(model);
        _schema = new SchemaView(model);
        _snapshot = accessor.Snapshot;

        RunStage(RuleStage.Table, diagnostics, orphaned);
        RunStage(RuleStage.Global, diagnostics);

        if (_options.SkipRuntimeValidation)
        {
            int skipped = _folders.Of(RuleStage.Runtime).Count;

            if (skipped > 0)
            {
                // Recorded rather than merely left undone. A gate that is switched off has to
                // say so, or a run that skipped it reads exactly like a run that passed it.
                diagnostics.Info(null,
                    Messages.Message.Of(ValidationMessages.RuntimeRulesSkipped,
                        ("Skipped", skipped)));
            }
        }
        else
        {
            // Opened only for this stage, so a table rule reaching for a store is a message
            // telling the author which folder it belongs in rather than a silent connection.
            _stores = new RuntimeStores(_recipe.Connections);

            RunStage(RuleStage.Runtime, diagnostics);

            _stores.LogOpened();
            _stores = null!;
        }

        Finish(diagnostics, "Validation", _report);
    }

    // ------------------------------------------------------------- stages

    /// <summary>Compiles and runs every rule file of one stage.</summary>
    /// <param name="skip">
    /// Files already reported as unrunnable, so they are not reported a second time by
    /// whatever they would have done.
    /// </param>
    private void RunStage(RuleStage stage, Diagnostics diagnostics, ISet<RuleFile>? skip = null)
    {
        var files = _folders.Of(stage)
                            .Where(file => skip is null || !skip.Contains(file))
                            .ToList();

        if (files.Count == 0)
            return;

        var stopwatch = Stopwatch.StartNew();

        if (RunsInParallel(stage) && files.Count > 1)
        {
            // Table rules are independent of each other: each is compiled on its own and each
            // reports into a collector that locks. What they must not do is depend on order,
            // and nothing here lets them - a rule that needs to see several tables at once
            // belongs in `global/`.
            Parallel.ForEach(files, file => RunOne(file, diagnostics));
        }
        else
        {
            RunInTiers(files, diagnostics);
        }

        stopwatch.Stop();

        Log.Debug($"Validation stage `{stage}`: {files.Count} rule(s) in {stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <summary>
    /// Whether a stage's rules may run at the same time as each other.
    /// </summary>
    /// <remarks>
    /// Only the table stage. The other three are sequential on purpose: `pre` is a handful of
    /// files before any work, a global rule is usually accumulating across tables, and a runtime
    /// rule holds a connection - running twenty of those at once turns a validation into a load
    /// test against somebody's database.
    /// </remarks>
    private static bool RunsInParallel(RuleStage stage) => stage == RuleStage.Table;

    /// <summary>
    /// Runs a sequential stage a tier at a time, stopping at the first tier that failed.
    /// </summary>
    /// <remarks>
    /// The order was never the missing part - rule files are collected by name, so it was already
    /// settled. What the tiers add is the barrier: a rule checking an invariant that everything
    /// after it assumes, failing, and the rest of the stage reporting the consequences instead of
    /// the cause.
    ///
    /// The barrier is whatever would stop the run, so `TreatWarningsAsErrors` moves it without
    /// this having to know about the setting.
    ///
    /// A folder that declares nothing has one tier and no barrier in it, which is what every
    /// folder had before.
    /// </remarks>
    private void RunInTiers(List<RuleFile> files, Diagnostics diagnostics)
    {
        var tiers = files
            .GroupBy(file => file.EffectiveTier)
            .OrderBy(tier => tier.Key)
            .ToList();

        for (int index = 0; index < tiers.Count; index++)
        {
            int before = diagnostics.Count;

            foreach (var file in tiers[index])
                RunOne(file, diagnostics);

            if (diagnostics.Count == before || index == tiers.Count - 1)
                continue;

            // Said rather than merely done. A tier that stops the ones after it has to report what
            // did not run, or a folder half of which never executed reads exactly like a folder
            // that passed.
            var skipped = tiers.Skip(index + 1).SelectMany(tier => tier).ToList();

            diagnostics.Info(null,
                Messages.Message.Of(ValidationMessages.RulesSkippedAfterTier,
                    ("Skipped", skipped.Count), ("Tier", tiers[index].Key),
                    ("Files", string.Join(", ", skipped.Select(file => file.Display)))));

            return;
        }
    }

    /// <summary>Compiles one rule file and runs it, reporting whatever it says or throws.</summary>
    private void RunOne(RuleFile file, Diagnostics diagnostics)
    {
        var compiled = _compiler.Compile(file, diagnostics);
        if (compiled is null)
            return;

        var scope = new RuleScope(
            diagnostics, _recipe.Options, file, _cells, _schema, _stores, _files, _snapshot);

        try
        {
            compiled.Invoke(new RuleContext(scope));
        }
        catch (TabbitException failure)
        {
            // A rule reaching for a setting nobody configured, and anything else this
            // pipeline itself refuses. The message is already written for an author.
            diagnostics.Error(failure.Location ?? SheetLocation.OfTextFile(file.Path, 1, 1),
                Messages.Message.Of(ValidationMessages.RuleRefused,
                    ("File", file.Display), ("Detail", failure.Message)));
        }
        // A defect is not a rule that failed, so it is not recorded against the rule's file
        // and it does not let the run carry on to its next rule.
        catch (Exception failure) when (failure is not TabbitDefectException)
        {
            diagnostics.Error(WhereItThrew(file, failure),
                Messages.Message.Of(ValidationMessages.RuleThrew,
                    ("File", file.Display), ("Exception", failure.GetType().Name),
                    ("Detail", failure.Message)));
        }

        // Never silently. A rule that reported past the cap says how much was left out, because
        // "100 problems" and "4,500 problems" are different situations and only one of them
        // means the rule itself is wrong.
        if (scope.Suppressed > 0)
        {
            diagnostics.Warn(SheetLocation.OfTextFile(file.Path, 1, 1),
                Messages.Message.Of(ValidationMessages.ReportsOverCap,
                    ("File", file.Display), ("Extra", scope.Suppressed),
                    ("Shown", RuleScope.MostReportsPerRule)));
        }
    }

    /// <summary>
    /// The line a rule threw on, when the symbols can say.
    /// </summary>
    /// <remarks>
    /// The rules are compiled with symbols precisely so this works: an author whose rule
    /// divided by zero should be told which line did it rather than that something in their
    /// folder threw. Frames without a file are the framework's, and are walked past.
    /// </remarks>
    private static SheetLocation WhereItThrew(RuleFile file, Exception failure)
    {
        var trace = new StackTrace(failure, fNeedFileInfo: true);

        for (int at = 0; at < trace.FrameCount; at++)
        {
            var frame = trace.GetFrame(at);
            string? path = frame?.GetFileName();

            if (!string.IsNullOrEmpty(path))
                return SheetLocation.OfTextFile(path, frame!.GetFileLineNumber(), frame.GetFileColumnNumber());
        }

        return SheetLocation.OfTextFile(file.Path, 1, 1);
    }

    // -------------------------------------------------------------- layout

    /// <summary>
    /// Reports a table rule whose table is not in the model.
    /// </summary>
    /// <remarks>
    /// Because the alternative is the rule quietly not running. A table gets renamed, its rule
    /// file keeps the old name, and every check in it stops happening - with the run reporting
    /// nothing at all, which is indistinguishable from the data being correct.
    /// </remarks>
    /// <returns>The rule files that must not run.</returns>
    private ISet<RuleFile> RefuseRulesForTablesThatDoNotExist(Model model, Diagnostics diagnostics)
    {
        var orphaned = new HashSet<RuleFile>();

        foreach (var file in _folders.Of(RuleStage.Table))
        {
            if (file.Subject is null)
            {
                // The suffix is not decoration here: it is what separates the table from the rest
                // of the name, so a file without one names no table at all.
                orphaned.Add(file);

                diagnostics.Error(SheetLocation.OfTextFile(file.Path, 1, 1),
                    Messages.Message.Of(ValidationMessages.TableRuleUnnamed,
                        ("File", file.Display), ("Suffix", RuleFolders.RuleSuffix),
                        ("Name", file.Name)));

                continue;
            }

            if (model.ContainsTable(file.Subject))
                continue;

            orphaned.Add(file);

            var nearest = model.Tables
                .Select(table => table.Name)
                .Where(name => name.StartsWith(file.Subject, StringComparison.OrdinalIgnoreCase)
                               || file.Subject.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Take(3)
                .ToList();

            string hint = nearest.Count > 0
                ? $" Did you mean {string.Join(" or ", nearest.Select(name => $"`{name}`"))}?"
                : "";

            // `Hint` is still a sentence built elsewhere, the way the index-type report's
            // `Why` is. It reads as English inside a translated sentence until it gets an id.
            diagnostics.Error(SheetLocation.OfTextFile(file.Path, 1, 1),
                Messages.Message.Of(ValidationMessages.TableRuleOrphaned,
                    ("File", file.Display), ("Table", file.Subject), ("Hint", hint),
                    ("GlobalFolder", RuleFolders.FolderOf(RuleStage.Global))));
        }

        return orphaned;
    }

    /// <summary>
    /// Reports a declared tier that would not be honoured.
    /// </summary>
    /// <remarks>
    /// Two cases, and refused rather than ignored for the same reason: a tier that is passed over
    /// silently leaves the author believing the run took it.
    ///
    /// Table rules run at the same time as each other and each is about one table, so there is no
    /// order among them to declare. A rule that has to see several tables at once belongs in
    /// `global/`, which is where a tier means something.
    /// </remarks>
    private void RefuseTiersThatDoNothing(Diagnostics diagnostics)
    {
        foreach (var file in _folders.All.Where(file => file.DeclaresTier))
        {
            if (file.Stage == RuleStage.Table)
            {
                diagnostics.Error(SheetLocation.OfTextFile(file.Path, 1, 1),
                    Messages.Message.Of(ValidationMessages.TableRuleDeclaresTier,
                        ("File", file.Display),
                        ("TableFolder", RuleFolders.FolderOf(RuleStage.Table)),
                        ("GlobalFolder", RuleFolders.FolderOf(RuleStage.Global))));

                continue;
            }

            if (file.Tier is null)
            {
                diagnostics.Error(SheetLocation.OfTextFile(file.Path, 1, 1),
                    Messages.Message.Of(ValidationMessages.TierUnreadable,
                        ("File", file.Display)));
            }
        }
    }

    /// <summary>Says so when the folder exists but holds no rules.</summary>
    /// <remarks>
    /// A warning rather than an error: an empty folder is how a project starts. But it is not
    /// silence - a recipe pointing at a folder somebody has not filled in yet should not look
    /// like a recipe whose rules all passed.
    /// </remarks>
    private void WarnIfEmpty(Diagnostics diagnostics)
    {
        if (_folders.All.Any())
            return;

        diagnostics.Warn(null,
            Messages.Message.Of(ValidationMessages.NoRuleFiles,
                ("Root", _folders.Root),
                ("TableFolder", RuleFolders.FolderOf(RuleStage.Table)),
                ("GlobalFolder", RuleFolders.FolderOf(RuleStage.Global)),
                ("RuntimeFolder", RuleFolders.FolderOf(RuleStage.Runtime)),
                ("PreFolder", RuleFolders.FolderOf(RuleStage.Pre))));
    }

    // -------------------------------------------------------------- output

    private Diagnostics NewDiagnostics()
        => new Diagnostics { PromoteWarnings = _recipe.TreatWarningsAsErrors };

    /// <summary>
    /// Prints what did not stop the run, then throws if anything did.
    /// </summary>
    /// <remarks>
    /// The warnings and the records are printed here rather than left in the collector,
    /// because a report nobody sees is a report nobody writes - the Lua validators this
    /// replaces used their warning level six times in 12,245 lines.
    /// </remarks>
    private static void Finish(
        Diagnostics diagnostics, string what, Reporting.BuildReport? report)
    {
        // Before anything is printed, because the table stage ran in parallel and the order
        // reports arrived in is whichever thread finished first.
        diagnostics.SortByLocation();

        foreach (var (severity, detail) in diagnostics.Entries)
        {
            string at = detail.Location is null ? "" : $"\n    at {detail.Location}";

            switch (severity)
            {
                case Severity.Info:
                    Log.Information($"  {detail.Message}{at}");
                    break;

                case Severity.Warning:
                    Log.Warning($"  {detail.Message}{at}");
                    break;
            }
        }

        if (diagnostics.WarningCount > 0 || diagnostics.InfoCount > 0 || diagnostics.ErrorCount > 0)
        {
            Log.Information(
                $"{what}: {diagnostics.ErrorCount} error(s), {diagnostics.WarningCount} warning(s), "
                + $"{diagnostics.InfoCount} note(s).");
        }

        // Taken before the throw, for the same reason it is printed before the throw: what
        // does not stop the run is most of what a stage found, and the exception carries
        // none of it.
        report?.Take(diagnostics);

        diagnostics.ThrowIfAny(
            Messages.Message.Of(ValidationMessages.StageFailed, ("What", what)));
    }
}
