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
/// </remarks>
[TabbitMessages("tabbit")]
public static class TabbitLayoutMessages
{
    #region Declaration cells

    /// <summary>A `:table` · `:enum` · `:const` cell with no name after the keyword.</summary>
    public const string DeclarationNeedsName = "tabbit.declaration-needs-name";

    /// <summary>A declaration's bracket meta that never closes.</summary>
    public const string DeclarationMetaUnclosed = "tabbit.declaration-meta-unclosed";

    /// <summary>A key in a declaration's brackets that this layout does not define.</summary>
    public const string DeclarationMetaKeyUnknown = "tabbit.declaration-meta-key-unknown";

    /// <summary>A key written twice in one declaration's brackets.</summary>
    public const string DeclarationMetaKeyRepeated = "tabbit.declaration-meta-key-repeated";

    /// <summary>A declaration meta key written with no value where it takes one.</summary>
    public const string DeclarationMetaValueMissing = "tabbit.declaration-meta-value-missing";

    /// <summary>Two entities of one run with the same name, whatever their kind.</summary>
    public const string EntityNameDuplicated = "tabbit.entity-name-duplicated";

    #endregion

    #region Marker column and header rows

    /// <summary>A marker-column cell holding something this layout does not define.</summary>
    public const string MarkerColumnUnknown = "tabbit.marker-column-unknown";

    /// <summary>The same header row key written twice in one entity.</summary>
    public const string RowKeyRepeated = "tabbit.row-key-repeated";

    /// <summary>A header row key below a row that already held data.</summary>
    public const string RowKeyBelowData = "tabbit.row-key-below-data";

    /// <summary>An entity with no `:field` row, which nothing can be read without.</summary>
    public const string FieldRowMissing = "tabbit.field-row-missing";

    /// <summary>A table with no `:type` row.</summary>
    public const string TypeRowMissing = "tabbit.type-row-missing";

    /// <summary>A `:type` row on an enum or a constant set, whose columns are named instead.</summary>
    public const string TypeRowNotOnEntity = "tabbit.type-row-not-on-entity";

    /// <summary>A header row key an enum or a constant set has no use for.</summary>
    public const string RowKeyNotOnEntity = "tabbit.row-key-not-on-entity";

    #endregion

    #region Columns

    /// <summary>A column with no name in `:field` whose data cells hold values.</summary>
    public const string ColumnUnnamedWithData = "tabbit.column-unnamed-with-data";

    /// <summary>Two columns of one entity that normalize to the same name.</summary>
    public const string ColumnNameDuplicated = "tabbit.column-name-duplicated";

    /// <summary>A field column whose `:type` cell is blank.</summary>
    public const string ColumnTypeMissing = "tabbit.column-type-missing";

    /// <summary>An entity with no column that carries a field.</summary>
    public const string NoFieldColumns = "tabbit.no-field-columns";

    /// <summary>A reference naming several tables and a field of one of them.</summary>
    public const string MultiTargetNamesAField = "tabbit.multi-target-names-a-field";

    /// <summary>A type cell whose bracket meta never closes.</summary>
    public const string ColumnMetaUnclosed = "tabbit.column-meta-unclosed";

    /// <summary>A `key` naming a column the entity does not have.</summary>
    public const string KeyColumnNotFound = "tabbit.key-column-not-found";

    /// <summary>A `key` naming a column that is not one value of the row itself.</summary>
    public const string KeyColumnNotScalar = "tabbit.key-column-not-scalar";

    #endregion

    #region Column paths

    /// <summary>A column path this layout's notation cannot read, with its own reason.</summary>
    public const string PathProblem = "tabbit.path-problem";

    /// <summary>An element number that is not a whole number.</summary>
    public const string ElementNumberNotInteger = "tabbit.element-number-not-integer";

    /// <summary>An element-numbered group whose numbers do not start at zero.</summary>
    public const string ElementNumbersNotFromZero = "tabbit.element-numbers-not-from-zero";

    /// <summary>An element-numbered group with a number missing from its run.</summary>
    public const string ElementNumbersNotConsecutive = "tabbit.element-numbers-not-consecutive";

    /// <summary>A `*` secondary index on a column that holds an array.</summary>
    public const string IndexMarkOnArrayColumn = "tabbit.index-mark-on-array-column";

    /// <summary>More than one `*` in front of a column name.</summary>
    public const string RepeatedIndexMark = "tabbit.repeated-index-mark";

    /// <summary>A `*` on a column that is a member of a group rather than a field of its own.</summary>
    public const string IndexMarkOnGroupMember = "tabbit.index-mark-on-group-member";

    #endregion

    #region Multi-row

    /// <summary>A `[]` level beside another `[]` or a numbered one.</summary>
    public const string MultiRowNested = "tabbit.multi-row-nested";

    /// <summary>A `[]` column whose type is also an array, so a row would hold an array.</summary>
    public const string MultiRowCellArray = "tabbit.multi-row-cell-array";

    /// <summary>A `[]` column in the place the primary index is read from.</summary>
    public const string MultiRowOnIndexColumn = "tabbit.multi-row-on-index-column";

    /// <summary>A value on an extension row in a column that is not `[]`.</summary>
    public const string ExtensionRowHasScalarValue = "tabbit.extension-row-has-scalar-value";

    /// <summary>An extension row before any record has begun.</summary>
    public const string ExtensionRowWithoutRecord = "tabbit.extension-row-without-record";

    #endregion

    #region Not yet in this layout

    /// <summary>A `:field` name this layout reserves for a notation a later spec settles.</summary>
    public const string ReservedColumnNotYetSupported = "tabbit.reserved-column-not-yet-supported";

    /// <summary>A `key` naming several columns, whose lookup surface is a step of its own.</summary>
    public const string CompositeKeyNotYetSupported = "tabbit.composite-key-not-yet-supported";

    #endregion

    #region Field variants

    /// <summary>Two columns of one field claiming the same variant.</summary>
    public const string VariantDuplicated = "tabbit.variant-duplicated";

    /// <summary>A variant asked for that no column of the field names.</summary>
    public const string VariantNotFound = "tabbit.variant-not-found";

    /// <summary>A field whose every column names a variant, with none asked for.</summary>
    public const string VariantNotChosen = "tabbit.variant-not-chosen";

    /// <summary>A variant column whose header disagrees with the group's.</summary>
    public const string VariantHeaderDisagrees = "tabbit.variant-header-disagrees";

    /// <summary>A variant on a column the row is addressed by.</summary>
    public const string VariantOnKeyColumn = "tabbit.variant-on-key-column";

    /// <summary>A variant on a member of a group or an element of an array.</summary>
    public const string VariantOnGroupColumn = "tabbit.variant-on-group-column";

    #endregion

    #region Enums

    /// <summary>A column of an enum whose name this layout does not define.</summary>
    public const string EnumColumnUnknown = "tabbit.enum-column-unknown";

    /// <summary>An enum with no `label` or no `value` column.</summary>
    public const string EnumColumnMissing = "tabbit.enum-column-missing";

    /// <summary>Two labels of one enum with the same name.</summary>
    public const string EnumLabelRedefined = "tabbit.enum-label-redefined";

    /// <summary>An enum label whose value cell is not a whole number.</summary>
    public const string EnumLabelValueNotInteger = "tabbit.enum-label-value-not-integer";

    /// <summary>An enum label or value cell left blank, both of which are required.</summary>
    public const string EnumCellRequired = "tabbit.enum-cell-required";

    /// <summary>Two labels of one enum claiming the same alias.</summary>
    public const string EnumAliasDuplicated = "tabbit.enum-alias-duplicated";

    /// <summary>An alias that is already the name of another label of the same enum.</summary>
    public const string EnumAliasShadowsLabel = "tabbit.enum-alias-shadows-label";

    #endregion

    #region Constant sets

    /// <summary>A column of a constant set whose name this layout does not define.</summary>
    public const string ConstantColumnUnknown = "tabbit.constant-column-unknown";

    /// <summary>A constant set missing one of its required columns.</summary>
    public const string ConstantColumnMissing = "tabbit.constant-column-missing";

    /// <summary>Two constants of one set with the same name.</summary>
    public const string ConstantRedefined = "tabbit.constant-redefined";

    /// <summary>A constant typed optional, which a single value cannot be.</summary>
    public const string ConstantCannotBeOptional = "tabbit.constant-cannot-be-optional";

    /// <summary>A constant's name, type or value cell left blank, all of which are required.</summary>
    public const string ConstantCellRequired = "tabbit.constant-cell-required";

    #endregion
}
