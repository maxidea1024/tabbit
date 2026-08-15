using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Tabbit.Extensions;
using Tabbit.Models;
using Tabbit.Recipe;
using Serilog;

using ValueType = Tabbit.Models.ValueType;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// Shared settings for the database export targets.
///
/// Each target loads into shadow tables and then swaps them in, so a run
/// that fails partway leaves the live data untouched. Atomicity is per
/// store: files and four databases cannot be committed as one transaction
/// without a distributed coordinator, so each is made atomic on its own
/// rather than pretending otherwise.
/// </summary>
public abstract class DatabaseRecipe : IOutputRecipe
{
    /// <summary>
    /// Connection string. Supports `${NAME}` placeholders filled from the
    /// environment, so a recipe holding no secrets can be committed:
    ///
    ///     "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}"
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Prefix applied to every table, collection or key name written.
    /// Lets one database hold several independent sets of exported data.
    /// </summary>
    public string NamePrefix { get; set; } = "";

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for
    /// both. Entities and fields marked for the other side are left out.
    /// </summary>
    public string TargetSide { get; set; } = "cs";
}

/// <summary>
/// Shared scaffolding for the database export targets.
///
/// Every one of them follows the same shape: narrow the model to the requested
/// target side, then for each table write a shadow copy and swap it in only once
/// the whole thing loaded. A run that fails partway therefore leaves the live data
/// exactly as it was, which is the same promise the file exporters make through
/// their staging area.
///
/// Atomicity is per store, deliberately. Committing files and four databases as one
/// transaction would need a distributed coordinator; making each store individually
/// atomic is achievable and is what this does.
/// </summary>
public abstract class DatabaseExporterBase<TEntry> : Target<TEntry>
    where TEntry : DatabaseRecipe
{
    /// <summary>
    /// Suffix of the shadow table, collection or key namespace that a run loads
    /// into before swapping it over the live one.
    /// </summary>
    protected const string ShadowSuffix = "__tabbit_new";

    /// <summary>Human-readable name of the target, used in log lines.</summary>
    protected abstract string TargetName { get; }

    /// <summary>
    /// Dotted recipe path of this target, used in error messages.
    ///
    /// Read from the entry being run rather than declared again in each exporter, so a
    /// message quotes the section the registry actually took the entry from and the
    /// two cannot disagree.
    /// </summary>
    protected string RecipeSection { get; private set; } = "";

    /// <summary>
    /// Runs one recipe entry: connect, load every table into shadow storage, swap.
    /// </summary>
    protected abstract void ExportTo(DatabaseRecipe recipe, Model model);

    protected override void Run(TargetContext context, TEntry recipe)
    {
        // An entry left in the recipe with no connection string is treated as
        // switched off, matching how the file exporters skip a blank Path.
        if (string.IsNullOrWhiteSpace(recipe.ConnectionString))
        {
            Log.Debug($"Skipping {TargetName} export: no ConnectionString configured.");
            return;
        }

        RecipeSection = context.Section ?? "";

        Log.Information($"Exporting {context.Model.Tables.Count} table(s) to {TargetName}");

        // context.Model is already narrowed to this entry's target side.
        ExportTo(recipe, context.Model);
    }

    /// <summary>
    /// Storage name for a table, including the recipe's prefix.
    /// </summary>
    protected static string StorageName(DatabaseRecipe recipe, Table table)
        => (recipe.NamePrefix ?? "") + table.Name;

    /// <summary>
    /// Columns to write for a table, one per serial field.
    ///
    /// Serial fields rather than raw fields, so a group of numbered columns lands
    /// in a single array-valued column instead of being spread across several -
    /// matching how every other exporter presents them.
    /// </summary>
    protected static IReadOnlyList<SerialField> Columns(Table table) => table.SerialFields;

    /// <summary>
    /// Column name for a serial field. Snake case because that is the convention in
    /// every database this exports to.
    /// </summary>
    protected static string ColumnName(SerialField sf) => sf.Name.ToSnakeCase();

    /// <summary>
    /// The value to bind for one row and column.
    ///
    /// Arrays - from either a serial field or a delimited cell - are rendered as
    /// JSON. A relational column cannot hold a variable number of values, and JSON
    /// keeps them queryable in both MySQL and PostgreSQL rather than flattening
    /// them into opaque text.
    /// </summary>
    protected static object CellValue(List<Cell> row, SerialField sf)
    {
        if (sf.IsVariableLengthArray)
            return JsonConvert.SerializeObject(NormalizeForJson(row[sf.FirstField!.Index].Value!));

        if (sf.IsArray)
            return JsonConvert.SerializeObject(sf.Fields.Select(f => NormalizeForJson(row[f.Index].Value!)));

        return NormalizeScalar(row[sf.FirstField!.Index].Value!);
    }

    /// <summary>
    /// The value a scalar column binds: mostly itself, but the types no database
    /// driver knows about are converted first.
    /// </summary>
    protected static object NormalizeScalar(object value)
    {
        switch (value)
        {
            case null:
                return DBNull.Value;

            // Ticks rather than an interval type: the precision is exact and every
            // engine stores a bigint the same way, whereas interval support varies.
            case TimeSpan span:
                return span.Ticks;

            default:
                return value!;
        }
    }

    private static object NormalizeForJson(object? value)
    {
        if (value is TimeSpan span)
            return span.Ticks;

        if (value is Array array)
        {
            var items = new List<object>(array.Length);
            foreach (var item in array)
                items.Add(NormalizeForJson(item));

            return items;
        }

        return value!;
    }

    /// <summary>
    /// Renders a value as text, for the targets that store everything as strings.
    /// </summary>
    protected static string? ToText(object value)
    {
        return value switch
        {
            null => "",
            bool b => b ? "1" : "0",
            string s => s,
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            TimeSpan span => span.Ticks.ToString(CultureInfo.InvariantCulture),
            Guid guid => guid.ToString(),
            Array _ => JsonConvert.SerializeObject(NormalizeForJson(value)),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Whether a column holds an array, and so needs a JSON-shaped destination.
    /// </summary>
    protected static bool IsJsonColumn(SerialField sf) => sf.IsArray;

    /// <summary>
    /// Element type behind a column, looking through both array kinds.
    /// </summary>
    /// <summary>
    /// The type of one value in a column, as a database column has to hold it.
    /// </summary>
    /// <remarks>
    /// A reference answers with the key it carries rather than with `ForeignRecord`. A
    /// database column holds the target's primary index - there is no record to store - and
    /// the type of that index is the target's to decide. The three exporters each used to
    /// map `ForeignRecord` to their integer type, which is one of the places that kept a
    /// table keyed by anything else from being pointed at. spec/reference-key-types.md.
    /// </remarks>
    protected static ValueType ElementTypeOf(SerialField sf)
        => sf.ElementType == ValueType.ForeignRecord && sf.FirstField is not null
            ? sf.FirstField!.RefKeyType
            : sf.ElementType;
}
