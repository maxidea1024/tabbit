using Tabbit.Messages;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The reports the primary layout writes about a sheet written in its notation.
/// </summary>
/// <remarks>
/// **The prefix is the layout's id**, as it is for every layout - these reports are about a
/// notation this one layout defines (`:table`, `:field`, `slots[0].id`) and none of it means
/// anything to a sheet read by another one. So they are declared beside the parser and their
/// text lives in a catalog file with the layout's name, and deleting the layout is deleting
/// its files.
///
/// The id is `primary` while this layout is built beside the one it replaces, because the
/// registry refuses two layouts claiming one id. It becomes `tabbit` when the old parser goes
/// - spec/primary-layout.md section 16, step 6.
/// </remarks>
[TabbitMessages("primary")]
public static class PrimaryLayoutMessages
{
    #region Declaration cells

    /// <summary>A `:table` · `:enum` · `:const` cell with no name after the keyword.</summary>
    public const string DeclarationNeedsName = "primary.declaration-needs-name";

    /// <summary>A declaration's bracket meta that never closes.</summary>
    public const string DeclarationMetaUnclosed = "primary.declaration-meta-unclosed";

    /// <summary>A key in a declaration's brackets that this layout does not define.</summary>
    public const string DeclarationMetaKeyUnknown = "primary.declaration-meta-key-unknown";

    /// <summary>A key written twice in one declaration's brackets.</summary>
    public const string DeclarationMetaKeyRepeated = "primary.declaration-meta-key-repeated";

    /// <summary>A declaration meta key written with no value where it takes one.</summary>
    public const string DeclarationMetaValueMissing = "primary.declaration-meta-value-missing";

    /// <summary>Two entities of one run with the same name, whatever their kind.</summary>
    public const string EntityNameDuplicated = "primary.entity-name-duplicated";

    #endregion

    #region Marker column and header rows

    /// <summary>A marker-column cell holding something this layout does not define.</summary>
    public const string MarkerColumnUnknown = "primary.marker-column-unknown";

    /// <summary>The same header row key written twice in one entity.</summary>
    public const string RowKeyRepeated = "primary.row-key-repeated";

    /// <summary>A header row key below a row that already held data.</summary>
    public const string RowKeyBelowData = "primary.row-key-below-data";

    /// <summary>An entity with no `:field` row, which nothing can be read without.</summary>
    public const string FieldRowMissing = "primary.field-row-missing";

    /// <summary>A table with no `:type` row.</summary>
    public const string TypeRowMissing = "primary.type-row-missing";

    /// <summary>A `:type` row on an enum or a constant set, whose columns are named instead.</summary>
    public const string TypeRowNotOnEntity = "primary.type-row-not-on-entity";

    /// <summary>A header row key an enum or a constant set has no use for.</summary>
    public const string RowKeyNotOnEntity = "primary.row-key-not-on-entity";

    #endregion

    #region Columns

    /// <summary>A column with no name in `:field` whose data cells hold values.</summary>
    public const string ColumnUnnamedWithData = "primary.column-unnamed-with-data";

    /// <summary>Two columns of one entity that normalize to the same name.</summary>
    public const string ColumnNameDuplicated = "primary.column-name-duplicated";

    /// <summary>A field column whose `:type` cell is blank.</summary>
    public const string ColumnTypeMissing = "primary.column-type-missing";

    /// <summary>An entity with no column that carries a field.</summary>
    public const string NoFieldColumns = "primary.no-field-columns";

    #endregion

    #region Column paths

    /// <summary>A column path this layout's notation cannot read, with its own reason.</summary>
    public const string PathProblem = "primary.path-problem";

    /// <summary>An element number that is not a whole number.</summary>
    public const string ElementNumberNotInteger = "primary.element-number-not-integer";

    /// <summary>An element-numbered group whose numbers do not start at zero.</summary>
    public const string ElementNumbersNotFromZero = "primary.element-numbers-not-from-zero";

    /// <summary>An element-numbered group with a number missing from its run.</summary>
    public const string ElementNumbersNotConsecutive = "primary.element-numbers-not-consecutive";

    /// <summary>A `*` secondary index on a column that holds an array.</summary>
    public const string IndexMarkOnArrayColumn = "primary.index-mark-on-array-column";

    /// <summary>More than one `*` in front of a column name.</summary>
    public const string RepeatedIndexMark = "primary.repeated-index-mark";

    /// <summary>A `*` on a column that is a member of a group rather than a field of its own.</summary>
    public const string IndexMarkOnGroupMember = "primary.index-mark-on-group-member";

    #endregion

    #region Multi-row

    /// <summary>A `[]` level beside another `[]` or a numbered one.</summary>
    public const string MultiRowNested = "primary.multi-row-nested";

    /// <summary>A `[]` column whose type is also an array, so a row would hold an array.</summary>
    public const string MultiRowCellArray = "primary.multi-row-cell-array";

    /// <summary>A `[]` column in the place the primary index is read from.</summary>
    public const string MultiRowOnIndexColumn = "primary.multi-row-on-index-column";

    /// <summary>A value on an extension row in a column that is not `[]`.</summary>
    public const string ExtensionRowHasScalarValue = "primary.extension-row-has-scalar-value";

    /// <summary>An extension row before any record has begun.</summary>
    public const string ExtensionRowWithoutRecord = "primary.extension-row-without-record";

    #endregion

    #region Not yet in this layout

    /// <summary>A `:variant` row, which arrives with step 3'.</summary>
    public const string VariantNotYetSupported = "primary.variant-not-yet-supported";

    /// <summary>Bracket meta on a type cell, which arrives with step 3.</summary>
    public const string ColumnMetaNotYetSupported = "primary.column-meta-not-yet-supported";

    /// <summary>
    /// A `key` on a declaration, which moves the primary index off the first column.
    /// </summary>
    public const string KeyMetaNotYetSupported = "primary.key-meta-not-yet-supported";

    #endregion

    #region Enums

    /// <summary>A column of an enum whose name this layout does not define.</summary>
    public const string EnumColumnUnknown = "primary.enum-column-unknown";

    /// <summary>An enum with no `label` or no `value` column.</summary>
    public const string EnumColumnMissing = "primary.enum-column-missing";

    /// <summary>Two labels of one enum with the same name.</summary>
    public const string EnumLabelRedefined = "primary.enum-label-redefined";

    /// <summary>An enum label whose value cell is not a whole number.</summary>
    public const string EnumLabelValueNotInteger = "primary.enum-label-value-not-integer";

    /// <summary>An enum label or value cell left blank, both of which are required.</summary>
    public const string EnumCellRequired = "primary.enum-cell-required";

    /// <summary>Two labels of one enum claiming the same alias.</summary>
    public const string EnumAliasDuplicated = "primary.enum-alias-duplicated";

    /// <summary>An alias that is already the name of another label of the same enum.</summary>
    public const string EnumAliasShadowsLabel = "primary.enum-alias-shadows-label";

    #endregion

    #region Constant sets

    /// <summary>A column of a constant set whose name this layout does not define.</summary>
    public const string ConstantColumnUnknown = "primary.constant-column-unknown";

    /// <summary>A constant set missing one of its required columns.</summary>
    public const string ConstantColumnMissing = "primary.constant-column-missing";

    /// <summary>Two constants of one set with the same name.</summary>
    public const string ConstantRedefined = "primary.constant-redefined";

    /// <summary>A constant typed optional, which a single value cannot be.</summary>
    public const string ConstantCannotBeOptional = "primary.constant-cannot-be-optional";

    /// <summary>A constant's name, type or value cell left blank, all of which are required.</summary>
    public const string ConstantCellRequired = "primary.constant-cell-required";

    #endregion
}
