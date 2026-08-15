namespace Tabbit.Validation;

/// <summary>
/// The verbs a rule calls before a single sheet has been read.
/// </summary>
/// <remarks>
/// A `pre` rule receives one: `public static void Validate(IPreContext context)`. What it can ask
/// about is what exists before the data does - file names, settings, the environment.
///
/// **There is no `Tables` and no `Schema` here, and that is the point.** A rule in the wrong
/// folder used to compile and then fail at run time with a message naming the folder to move to.
/// Now the name it reaches for is not there, so an editor says so while it is being typed.
///
/// **Handed over rather than ambient.** An earlier version was a static class opened with a
/// synthesized `global using static`, so `Error` arrived from a file the author never saw. An
/// editor cannot resolve a name like that until it has compiled the generated file that supplies
/// it, which is precisely how completion failed the first three times it was attempted. A
/// parameter is visible in the signature and needs nothing.
///
/// One instance per rule file, which is also what makes the report cap per rule and lets the table
/// stage run in parallel with nothing shared.
/// spec/validation-usability-and-assembly-output.md and spec/accessor-instances.md section 3.1.
/// </remarks>
public interface IPreContext
{
    // ----------------------------------------------------------- reporting

    /// <summary>Reports a problem that stops the run.</summary>
    /// <remarks>
    /// With no cell to point at, so the report names the rule file and nothing narrower. In the
    /// stages that have a model, prefer the overloads that take a row or a column: those name the
    /// cell an author has to open, which is the whole reason validation moved inside the converter.
    /// </remarks>
    void Error(string message);

    /// <summary>Reports something worth seeing that does not stop the run on its own.</summary>
    void Warn(string message);

    /// <summary>Records what the validation did. Never stops the run, and never promoted.</summary>
    void Info(string message);

    // ------------------------------------------------------------- settings

    /// <summary>
    /// A value the recipe's `Validation.Options` carries.
    /// </summary>
    /// <remarks>
    /// Throws when the key is absent, listing the ones that are there. A rule reading a setting that
    /// nobody configured is a mistake in one of the two, and quietly answering with an empty string
    /// turns it into a rule that checks the wrong thing - a locale comparison against `""` matches
    /// nothing and reports nothing.
    ///
    /// Use <see cref="Option(string, string)"/> where absence is a legitimate case.
    /// </remarks>
    string Option(string key);

    /// <summary>The same, answering <paramref name="fallback"/> when the recipe is silent.</summary>
    string Option(string key, string fallback);

    /// <summary>Whether the recipe sets this option at all.</summary>
    bool HasOption(string key);

    // --------------------------------------------------------- external files

    /// <summary>
    /// The files under a folder, by name.
    /// </summary>
    /// <remarks>
    /// Whether an asset a sheet names exists is not a question this tool can answer - it does not
    /// know what an asset is. What it can do is hand over the folder: the rule decides which
    /// extension matters and what a missing one means.
    ///
    /// Scanned once per folder and pattern, and shared by every rule, because a content tree is
    /// large and a rule asks about it per row.
    /// </remarks>
    IFileMap Files(string root, string pattern);

    /// <summary>
    /// A JSON document that is not a table.
    /// </summary>
    /// <remarks>
    /// Tables come from the accessor, typed. This is for the data a project keeps beside its
    /// sheets. Parsed once per path.
    /// </remarks>
    Newtonsoft.Json.Linq.JToken Json(string path);
}

/// <summary>
/// The verbs a rule calls once there is a model: everything a `pre` rule has, and the data.
/// </summary>
/// <remarks>
/// A `global` rule receives one: `public static void Validate(IGlobalContext context)`. Its
/// subject is more than one table - a reference that crosses them, a convention over all of them -
/// which is what the enumerating <see cref="Schema"/> is for.
///
/// The tables themselves come from the generated accessor, named by an ordinary `using` line in
/// the rule file. They cannot come through here because its members are static.
/// </remarks>
public interface IGlobalContext : IPreContext
{
    // ------------------------------------------------- reporting about a row

    /// <summary>
    /// Reports a problem against one field of one row, naming the cell it came from.
    /// </summary>
    /// <remarks>
    /// This is the call a rule should make. The cell is what an author opens, and pointing at it is
    /// the difference between this pipeline and one that reads the exported files: a report says
    /// `workbook : sheet : AF12` rather than the 1,847th entry of a JSON array.
    ///
    /// The field is passed as `nameof(row.Field)` rather than a literal so the compiler checks it.
    /// A name that does not invert to one column - a folded array, a record group - points at the
    /// group's first column; pass <paramref name="at"/> to name an element.
    /// </remarks>
    void Error(object row, string field, string message, int at = -1);

    /// <summary>The same, at a severity that does not stop the run on its own.</summary>
    void Warn(object row, string field, string message, int at = -1);

    /// <summary>The same, as a record of what was seen rather than a judgement.</summary>
    void Info(object row, string field, string message, int at = -1);

    /// <summary>
    /// Reports against a whole row, when no single column is the one at fault.
    /// </summary>
    /// <remarks>
    /// Points at the row's primary index cell, which is where a reader looking for the row would
    /// start. A combination of columns being wrong together is the case for this.
    /// </remarks>
    void ErrorAtRow(object row, string message);

    /// <summary>The same, at warning severity.</summary>
    void WarnAtRow(object row, string message);

    // -------------------------------------------------- reporting about schema

    /// <summary>
    /// The tables and columns as something to walk, for a rule whose subject is not one table.
    /// </summary>
    /// <remarks>
    /// The counterpart to the accessor: that one is typed and needs a name, this one enumerates and
    /// does not. A convention over every table can only be written against this.
    /// </remarks>
    ISchemaView Schema { get; }

    /// <summary>
    /// The accessor's snapshot for this run, untyped. **Rules use `context.Tables` instead.**
    /// </summary>
    /// <remarks>
    /// The one bridge in this contract, and it exists because of an ordering: this assembly is
    /// compiled and shipped long before the accessor, which is generated from somebody's sheets
    /// while the tool runs - so there is no type here to declare `Tables` as.
    ///
    /// The generated assembly closes the gap from its own side. It declares an extension property
    /// on this interface that casts this to the snapshot type it does know, so a rule writes
    /// `context.Tables.Item` and gets the generated type with every field typed. Which is why this
    /// member is hidden from completion: it is the plumbing, and the typed name is right beside it.
    ///
    /// spec/accessor-instances.md section 3.2.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    object TableSnapshot { get; }

    /// <summary>Reports against a column, naming the header cell it was declared in.</summary>
    void Error(IFieldSchema field, string message);

    /// <summary>The same, at a severity that does not stop the run on its own.</summary>
    void Warn(IFieldSchema field, string message);

    /// <summary>The same, as a record rather than a judgement.</summary>
    void Info(IFieldSchema field, string message);

    /// <summary>Reports against a table, naming the cell its marker is in.</summary>
    void Error(ITableSchema table, string message);

    /// <summary>The same, at a severity that does not stop the run on its own.</summary>
    void Warn(ITableSchema table, string message);

    /// <summary>The same, as a record rather than a judgement.</summary>
    void Info(ITableSchema table, string message);
}

/// <summary>
/// The same, for a rule about one table - which it is told rather than having to name.
/// </summary>
/// <remarks>
/// A `tables` rule receives one: `public static void Validate(ITableContext context)`. The file
/// name already says which table it is for, and <see cref="Table"/> is that table - so a rule
/// checking its own columns does not repeat the name a rename would leave behind.
/// </remarks>
public interface ITableContext : IGlobalContext
{
    /// <summary>
    /// The table this rule file is about, as the file name declares it.
    /// </summary>
    /// <remarks>
    /// `ItemRules.cs` gets `Item`. Reaching for it rather than writing `Schema.Table("Item")` is
    /// one fewer place a renamed table has to be found - the file name is already checked against
    /// the model, so this cannot be a name nothing has.
    /// </remarks>
    ITableSchema Table { get; }
}

/// <summary>
/// The same as a global rule, plus the stores outside the sheets.
/// </summary>
/// <remarks>
/// A `runtime` rule receives one: `public static void Validate(IRuntimeContext context)`. What a
/// sheet points at is sometimes outside the sheets, and a coupon code already issued or a product
/// already listed can only be checked against the store that holds it.
///
/// **Only this stage has them**, and the type is how that is said. `runtime` is the folder
/// `--skip-runtime-validation` leaves out, and that only works if the rules needing a store are
/// in it.
/// </remarks>
public interface IRuntimeContext : IGlobalContext
{
    /// <summary>A read-only SQL store, by the name the recipe's `Validation.Connections` gave it.</summary>
    ISqlStore Db(string name);

    /// <summary>A read-only Redis store, by the name the recipe gave it.</summary>
    IRedisStore Redis(string name);
}
