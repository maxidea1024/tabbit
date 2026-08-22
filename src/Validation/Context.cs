using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Tabbit.Models;
using Tabbit.Messages;

namespace Tabbit.Validation;

/// <summary>
/// What a rule file is running against: where to report, and what the recipe passed in.
/// </summary>
internal sealed class RuleScope
{
    public RuleScope(
        Diagnostics diagnostics,
        IReadOnlyDictionary<string, string> options,
        RuleFile file,
        CellLocator cells,
        SchemaView schema,
        RuntimeStores stores,
        ExternalFiles files,
        object snapshot)
    {
        Diagnostics = diagnostics;
        Options = options;
        File = file;
        Cells = cells;
        Schema = schema;
        Stores = stores;
        Files = files;
        Snapshot = snapshot;
    }

    public Diagnostics Diagnostics { get; }

    public IReadOnlyDictionary<string, string> Options { get; }

    /// <summary>The rule file being run, which is where a report with no cell points.</summary>
    public RuleFile File { get; }

    /// <summary>
    /// Turns a record and a field name back into the cell they came from. Null in the `pre`
    /// stage, where there is no model yet.
    /// </summary>
    public CellLocator Cells { get; }

    /// <summary>
    /// The tables and columns as something to walk. Null in the `pre` stage, where there is
    /// nothing to walk yet.
    /// </summary>
    public ISchemaView Schema { get; }

    /// <summary>The external stores a `runtime` rule may read.</summary>
    public RuntimeStores Stores { get; }

    /// <summary>Scanned folders and parsed JSON, shared by every rule of the run.</summary>
    public ExternalFiles Files { get; }

    /// <summary>
    /// The generated accessor's instance for this run, which `context.Tables` reaches. Null in
    /// the `pre` stage, where there is nothing loaded yet.
    /// </summary>
    public object Snapshot { get; }

    /// <summary>
    /// The most reports one rule file may make before the rest are counted instead of printed.
    /// </summary>
    /// <remarks>
    /// One wrong rule over a large table is tens of thousands of identical lines - the port of a
    /// live project's shop rule produced 4,400 on its first run, and the column-constraint work
    /// once produced 269,426. Past the first hundred nothing is learned, and a report nobody can
    /// scroll through is a report nobody reads.
    ///
    /// Per rule file rather than per run, so one noisy rule does not hide every other rule's
    /// findings.
    /// </remarks>
    public const int MostReportsPerRule = 100;

    private int _reported;

    /// <summary>How many reports this rule made past the cap.</summary>
    public int Suppressed { get; private set; }

    /// <summary>
    /// Whether this report should be recorded, counting it either way.
    /// </summary>
    /// <remarks>
    /// Interlocked because the table stage runs in parallel - though one scope belongs to one
    /// rule file, a rule that spawns its own tasks would otherwise lose the count.
    /// </remarks>
    public bool Admits()
    {
        if (System.Threading.Interlocked.Increment(ref _reported) <= MostReportsPerRule)
            return true;

        Suppressed++;
        return false;
    }
}

/// <summary>
/// The verbs a rule calls: reporting, settings, files and stores.
/// </summary>
/// <remarks>
/// A rule receives one: `public static void Validate(IContext context)`. Everything a rule does that is
/// not reading a table goes through it - `context.Error(...)`, `context.Option(...)`, `context.Db(...)` -
/// and typing `context.` is how an author finds out what those are.
///
/// **Handed over rather than ambient**, and that is the point. An earlier version was a static
/// class opened with a synthesized `global using static`, so `Error` arrived from a file the
/// author never saw. An editor cannot resolve a name like that until it has compiled the
/// generated file that supplies it, which is precisely how completion failed the first three
/// times it was attempted. A parameter is visible in the signature and needs nothing.
///
/// The other root is `Tables`, the accessor generated from the sheets - the same type the
/// consuming project's own code uses. It cannot come through here because its members are
/// static, so a rule file names its namespace with an ordinary `using`. One visible line
/// instead of an invisible mechanism.
///
/// One instance per rule file, which is also what makes the report cap per rule and lets the
/// table stage run in parallel with nothing shared.
/// </remarks>
internal sealed class RuleContext : ITableContext, IRuntimeContext
{
    private readonly RuleScope _scope;

    internal RuleContext(RuleScope scope) => _scope = scope;

    /// <summary>
    /// What this rule is running against: the collector, the recipe's settings, and the lookups
    /// that turn a row and a field name back into a cell.
    /// </summary>
    private RuleScope Scope => _scope;

    // ----------------------------------------------------------- reporting

    /// <summary>Reports a problem that stops the run.</summary>
    /// <remarks>
    /// With no cell to point at, so the report names the rule file and nothing narrower.
    /// Prefer the overloads that take a row or a column: those name the cell an author has
    /// to open, which is the whole reason validation moved inside the converter.
    /// </remarks>
    public void Error(string message) => Report(Severity.Error, message);

    /// <summary>Reports something worth seeing that does not stop the run on its own.</summary>
    public void Warn(string message) => Report(Severity.Warning, message);

    /// <summary>Records what the validation did. Never stops the run, and never promoted.</summary>
    public void Info(string message) => Report(Severity.Info, message);

    private void Report(Severity severity, string message)
    {
        var scope = Scope;

        if (!scope.Admits())
            return;

        scope.Diagnostics.Add(severity, LocationOf(scope.File), Describe(scope.File, message));
    }

    // ------------------------------------------------- reporting about a row

    /// <summary>
    /// Reports a problem against one field of one row, naming the cell it came from.
    /// </summary>
    /// <remarks>
    /// This is the call a rule should make. The cell is what an author opens, and pointing at
    /// it is the difference between this pipeline and one that reads the exported files: a
    /// report says `workbook : sheet : AF12` rather than the 1,847th entry of a JSON array.
    ///
    /// The field is passed as `nameof(row.Field)` rather than a literal so the compiler checks
    /// it. A name that does not invert to one column - a folded array, a record group - points
    /// at the group's first column; pass <paramref name="at"/> to name an element.
    /// </remarks>
    public void Error(object row, string field, string message, int at = -1)
        => Report(Severity.Error, row, field, message, at);

    /// <summary>The same, at a severity that does not stop the run on its own.</summary>
    public void Warn(object row, string field, string message, int at = -1)
        => Report(Severity.Warning, row, field, message, at);

    /// <summary>The same, as a record of what was seen rather than a judgement.</summary>
    public void Info(object row, string field, string message, int at = -1)
        => Report(Severity.Info, row, field, message, at);

    /// <summary>
    /// Reports against a whole row, when no single column is the one at fault.
    /// </summary>
    /// <remarks>
    /// Points at the row's primary index cell, which is where a reader looking for the row
    /// would start. A combination of columns being wrong together is the case for this.
    /// </remarks>
    public void ErrorAtRow(object row, string message)
        => Report(Severity.Error, row, null, message, -1);

    /// <summary>The same, at warning severity.</summary>
    public void WarnAtRow(object row, string message)
        => Report(Severity.Warning, row, null, message, -1);

    private void Report(Severity severity, object row, string? field, string message, int at)
    {
        var scope = Scope;

        if (!scope.Admits())
            return;

        var where = (scope.Cells is null ? null : scope.Cells.Find(row, field!, at)) ?? LocationOf(scope.File);

        scope.Diagnostics.Add(severity, where, Describe(scope.File, message));
    }

    /// <summary>The rule file itself, as a position a terminal can click.</summary>
    private static Location? LocationOf(RuleFile? file)
        => file is null ? null : Location.OfTextFile(file.Path, 1, 1);

    /// <summary>
    /// The header cell a column was declared in, or the marker cell of a table.
    /// </summary>
    /// <remarks>
    /// Reached by casting back to what this host built. The contract answers with an interface so a
    /// rule compiles without the model in scope, and the cell a schema item came from is not part of
    /// that surface - a rule points at things, it does not compute where they are. Every one of
    /// these objects is made here, so the cast is a fact rather than a hope.
    /// </remarks>
    private static Location? LocationOf(IFieldSchema field) => (field as FieldSchema)?.Location;

    private static Location? LocationOf(ITableSchema table) => (table as TableSchema)?.Location;

    /// <summary>
    /// A report with the rule that made it named in front.
    /// </summary>
    /// <remarks>
    /// Because 141 rule files reporting into one list is 141 authors, and "which rule said
    /// this" is the first question about any of them.
    /// </remarks>
    private static string Describe(RuleFile? file, string message)
        => file is null ? message : $"[{file.Display ?? file.Name}] {message}";

    // -------------------------------------------------- reporting about schema

    /// <summary>
    /// The tables and columns as something to walk, for a rule whose subject is not one table.
    /// </summary>
    /// <remarks>
    /// The counterpart to `Tables`: that one is typed and needs a name, this one enumerates and
    /// does not. A convention over every table can only be written against this.
    /// </remarks>
    /// <summary>
    /// The table a `tables` rule is about, taken from the file name that already declares it.
    /// </summary>
    /// <remarks>
    /// Cannot be a name nothing has: a rule file whose name matches no table is refused before any
    /// rule runs, so by the time one asks, this is there.
    /// </remarks>
    public ITableSchema Table => Scope.Schema!.Table(Scope.File!.Subject ?? "")!;

    /// <summary>
    /// The accessor instance, handed over untyped because this assembly is older than its type.
    /// </summary>
    /// <remarks>
    /// A rule never writes this - the generated assembly declares `context.Tables` on top of it,
    /// which is the same object with the generated type on it.
    /// </remarks>
    public object TableSnapshot
        => Scope.Snapshot
           ?? throw new TabbitException(null,
               Message.Of(ValidationMessages.TablesBeforeSheetsRead,
                   ("PreFolder", RuleFolders.FolderOf(RuleStage.Pre)),
                   ("TableFolder", RuleFolders.FolderOf(RuleStage.Table))));

    public ISchemaView Schema
        => Scope.Schema
           ?? throw new TabbitException(null,
               Message.Of(ValidationMessages.SchemaBeforeSheetsRead,
                   ("PreFolder", RuleFolders.FolderOf(RuleStage.Pre)),
                   ("TableFolder", RuleFolders.FolderOf(RuleStage.Table))));

    /// <summary>Reports against a column, naming the header cell it was declared in.</summary>
    public void Error(IFieldSchema field, string message)
        => Report(Severity.Error, LocationOf(field), message);

    /// <summary>The same, at a severity that does not stop the run on its own.</summary>
    public void Warn(IFieldSchema field, string message)
        => Report(Severity.Warning, LocationOf(field), message);

    /// <summary>The same, as a record rather than a judgement.</summary>
    public void Info(IFieldSchema field, string message)
        => Report(Severity.Info, LocationOf(field), message);

    /// <summary>Reports against a table, naming the cell its marker is in.</summary>
    public void Error(ITableSchema table, string message)
        => Report(Severity.Error, LocationOf(table), message);

    /// <summary>The same, at a severity that does not stop the run on its own.</summary>
    public void Warn(ITableSchema table, string message)
        => Report(Severity.Warning, LocationOf(table), message);

    /// <summary>The same, as a record rather than a judgement.</summary>
    public void Info(ITableSchema table, string message)
        => Report(Severity.Info, LocationOf(table), message);

    private void Report(Severity severity, Location? where, string message)
    {
        var scope = Scope;

        if (!scope.Admits())
            return;

        scope.Diagnostics.Add(severity, where ?? LocationOf(scope.File), Describe(scope.File, message));
    }

    // ------------------------------------------------------------- settings

    /// <summary>
    /// A value the recipe's `Validation.Options` carries.
    /// </summary>
    /// <remarks>
    /// Throws when the key is absent, listing the ones that are there. A rule reading a
    /// setting that nobody configured is a mistake in one of the two, and quietly answering
    /// with an empty string turns it into a rule that checks the wrong thing - a locale
    /// comparison against `""` matches nothing and reports nothing.
    ///
    /// Use <see cref="Option(string, string)"/> where absence is a legitimate case.
    /// </remarks>
    public string Option(string key)
    {
        if (Scope.Options.TryGetValue(key, out string? value))
            return value;

        string known = Scope.Options.Count == 0
            ? "the recipe sets none"
            : string.Join(", ", Scope.Options.Keys.OrderBy(name => name, StringComparer.Ordinal));

        throw new TabbitException(null,
            Message.Of(ValidationMessages.OptionNotSet, ("Key", key), ("Known", known)));
    }

    /// <summary>The same, answering <paramref name="fallback"/> when the recipe is silent.</summary>
    public string Option(string key, string fallback)
        => Scope.Options.TryGetValue(key, out string? value) ? value : fallback;

    /// <summary>Whether the recipe sets this option at all.</summary>
    public bool HasOption(string key) => Scope.Options.ContainsKey(key);

    // --------------------------------------------------------- external files

    /// <summary>
    /// The files under a folder, by name.
    /// </summary>
    /// <remarks>
    /// Whether an asset a sheet names exists is not a question this tool can answer - it does not
    /// know what an asset is, and the `:asset` row of the sheets it was built from is left alone
    /// for exactly that reason. What it can do is hand over the folder: the rule decides which
    /// extension matters and what a missing one means.
    ///
    /// Scanned once per folder and pattern, and shared by every rule, because a content tree is
    /// large and a rule asks about it per row.
    /// </remarks>
    public IFileMap Files(string root, string pattern) => Scope.Files.Map(root, pattern);

    /// <summary>
    /// A JSON document that is not a table.
    /// </summary>
    /// <remarks>
    /// Tables come from `Tables`, typed. This is for the data a project keeps beside its sheets,
    /// which is what the Lua validators reached for most after the tables themselves. Parsed once
    /// per path.
    /// </remarks>
    public Newtonsoft.Json.Linq.JToken Json(string path) => Scope.Files.Json(path);

    // -------------------------------------------------------- external stores

    /// <summary>
    /// A read-only SQL store, by the name the recipe's `Validation.Connections` gave it.
    /// </summary>
    /// <remarks>
    /// For the `runtime/` rules: what a sheet points at is sometimes outside the sheets, and a
    /// coupon code already issued or a product already listed can only be checked against the
    /// store that holds it.
    ///
    /// A store that cannot answer is a failed validation rather than a passed one. Where that is
    /// not wanted - a laptop with no access - `--skip-runtime-validation` says so out loud and
    /// the run records how many rules it skipped.
    /// </remarks>
    public ISqlStore Db(string name) => Stores().Sql(name);

    /// <summary>A read-only Redis store, by the name the recipe gave it.</summary>
    public IRedisStore Redis(string name) => Stores().Redis(name);

    private RuntimeStores Stores()
        => Scope.Stores
           ?? throw new TabbitException(null,
               Message.Of(ValidationMessages.StoreOutsideRuntimeStage,
                   ("RuntimeFolder", RuleFolders.FolderOf(RuleStage.Runtime))));
}
