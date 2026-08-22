using Tabbit.Messages;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The reports the `named-range` layout writes.
/// </summary>
/// <remarks>
/// Prefixed with the layout's id, and its text is in `Messages/Catalog/named-range.en.json`. See
/// <see cref="TabbitLayoutMessages"/> for why.
///
/// This layout reads sheets written to somebody else's rules, so most of these say what this
/// layout can and cannot read rather than what is wrong - the difference matters to whoever
/// gets the report, because they may not be able to change the sheet at all.
/// </remarks>
[TabbitMessages("named-range")]
public static class UwoLayoutMessages
{
    /// <summary>Two columns of one table with the same name.</summary>
    public const string ColumnNameClash = "named-range.column-name-clash";

    /// <summary>A table whose first column is not typed `key`.</summary>
    public const string TableHasNoKeyColumn = "named-range.table-has-no-key-column";

    /// <summary>A bracketed group on a type that is not gathered into one.</summary>
    public const string TypeTakesNoGroup = "named-range.type-takes-no-group";

    /// <summary>A column name the nested notation cannot read, with its own reason.</summary>
    public const string ColumnNameProblem = "named-range.column-name-problem";

    /// <summary>A list of a type this layout does not put in a list.</summary>
    public const string ListElementTypeUnsupported = "named-range.list-element-type-unsupported";

    /// <summary>A column type this layout does not know.</summary>
    public const string TypeUnrecognized = "named-range.type-unrecognized";

    /// <summary>The layout's own option set to something it does not take.</summary>
    public const string NumberTypeOptionUnknown = "named-range.number-type-option-unknown";

    /// <summary>A grid whose columns are not all one type.</summary>
    public const string GridColumnsDifferInType = "named-range.grid-columns-differ-in-type";

    /// <summary>A grid column id that will not fit the companion table's key.</summary>
    public const string GridColumnIdNotInt32 = "named-range.grid-column-id-not-int32";
}
