using Tabbit.Messages;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The reports the `rescue` layout writes.
/// </summary>
/// <remarks>
/// Prefixed with the layout's id, and its text is in `Messages/Catalog/rescue.en.json`. See
/// <see cref="TabbitLayoutMessages"/> for why the ids and the file are named after the layout
/// rather than gathered anywhere central.
///
/// Some of these read like the core's or another layout's - two columns colliding is a thing
/// that happens in any notation. They stay separate anyway, because what a sheet must do
/// instead differs per layout: here a table is named by its sheet tab, and the sentence says
/// so.
/// </remarks>
[TabbitMessages("rescue")]
public static class RescueLayoutMessages
{
    /// <summary>Two sheets declaring one enum name.</summary>
    public const string EnumRedefined = "rescue.enum-redefined";

    /// <summary>Two sheet tabs whose names agree once `Table` is dropped.</summary>
    public const string TableRedefined = "rescue.table-redefined";

    /// <summary>Two labels of one enum with the same name.</summary>
    public const string EnumLabelRedefined = "rescue.enum-label-redefined";

    /// <summary>Two columns that normalize to one member name.</summary>
    public const string ColumnNameClash = "rescue.column-name-clash";

    /// <summary>A table sheet with nothing on it this layout can read as a column.</summary>
    public const string TableHasNoColumns = "rescue.table-has-no-columns";

    /// <summary>`enum:` with no enum name after it.</summary>
    public const string EnumNameMustFollowMarker = "rescue.enum-name-must-follow-marker";

    /// <summary>A column typed with an enum no sheet declared.</summary>
    public const string EnumNotDeclared = "rescue.enum-not-declared";

    /// <summary>A type that cannot be the element type of an array.</summary>
    public const string TypeNotArrayElement = "rescue.type-not-array-element";

    /// <summary>A log line: Dropping row {Row} of `{Table}`: `{Field}` is `{Value}`, which an earlier row already used.</summary>
    public const string LogDroppingDuplicateRow = "rescue.log-dropping-duplicate-row";

    /// <summary>A log line: Replacing an earlier row of `{Table}` with row {Row}: `{Field}` is `{Value}`, which both use.</summary>
    public const string LogReplacingDuplicateRow = "rescue.log-replacing-duplicate-row";

    /// <summary>A log line: Skipping sheet `{Sheet}`: row {NameRow} should open with a field name and row {TypeRow} with its.</summary>
    public const string LogSkippingSheetBadHeader = "rescue.log-skipping-sheet-bad-header";

    /// <summary>A log line: Skipping sheet `{Sheet}`: it has no data rows under a three-row header.</summary>
    public const string LogSkippingSheetNoData = "rescue.log-skipping-sheet-no-data";

    /// <summary>A log line: Skipping row {Row} of `{Table}`: it has no `{Field}` but is not empty, so it is an unfinished ro.</summary>
    public const string LogSkippingUnfinishedRow = "rescue.log-skipping-unfinished-row";
}
