using Tabbit.Messages;

namespace Tabbit.Cooking.Layouts;

/// <summary>
/// The reports the `tabbit` layout writes about a sheet written in its notation.
/// </summary>
/// <remarks>
/// **The prefix is the layout's id, and that is the whole point.** These reports are about a
/// notation this one layout defines - `Slot1.Id`, `Name@3`, the entity markers - and none of
/// it means anything to a sheet read by another layout. So they are declared beside the parser
/// and their text lives in `Messages/Catalog/tabbit.en.json`, which is a file with the same
/// name as the layout.
///
/// Deleting a layout is then deleting its files: the parser, this class, and its catalog. The
/// core keeps no list of any of the three - <see cref="MessageRegistry"/> finds ids by
/// scanning for the attribute and <see cref="MessageCatalog"/> finds text by scanning resource
/// names. That is the test CLAUDE.md sets for a layout, applied to its messages.
/// </remarks>
[TabbitMessages("tabbit")]
public static class TabbitLayoutMessages
{
    /// <summary>Two labels of one enum with the same name.</summary>
    public const string EnumLabelRedefined = "tabbit.enum-label-redefined";

    /// <summary>An enum label whose value cell is not a whole number.</summary>
    public const string EnumLabelValueNotInteger = "tabbit.enum-label-value-not-integer";

    /// <summary>Two constants of one set with the same name.</summary>
    public const string ConstantRedefined = "tabbit.constant-redefined";

    /// <summary>A constant typed optional, which a single value cannot be.</summary>
    public const string ConstantCannotBeOptional = "tabbit.constant-cannot-be-optional";

    /// <summary>An `enum` type with no enum named in the detail-type cell.</summary>
    public const string EnumNeedsDetailType = "tabbit.enum-needs-detail-type";

    /// <summary>A `foreign` type with no target named in the detail-type cell.</summary>
    public const string ForeignNeedsDetailType = "tabbit.foreign-needs-detail-type";

    /// <summary>A field name with two runs of digits, so which one is the element is unclear.</summary>
    public const string FieldNameAmbiguousElementNumber = "tabbit.field-name-ambiguous-element-number";

    /// <summary>An element number that is not a whole number.</summary>
    public const string FieldNameElementNumberNotInteger = "tabbit.field-name-element-number-not-integer";

    /// <summary>A field name the nested notation cannot read, with its own reason.</summary>
    public const string FieldNameNestingProblem = "tabbit.field-name-nesting-problem";

    /// <summary>Two columns of one table that normalize to the same name.</summary>
    public const string FieldNameDuplicated = "tabbit.field-name-duplicated";

    /// <summary>More than one `*` in front of a field name.</summary>
    public const string FieldNameMultipleIndexMarks = "tabbit.field-name-multiple-index-marks";

    /// <summary>The first column marked as omitted.</summary>
    public const string PrimaryIndexCannotBeOmitted = "tabbit.primary-index-cannot-be-omitted";

    /// <summary>The first column written as a member of a record group.</summary>
    public const string PrimaryIndexInRecordGroup = "tabbit.primary-index-in-record-group";

    /// <summary>A record group marked as a secondary index.</summary>
    public const string RecordMemberMarkedSecondaryIndex = "tabbit.record-member-marked-secondary-index";

    /// <summary>An element-optional `?` on a column that is not an array.</summary>
    public const string ElementOptionalMarkOnNonArray = "tabbit.element-optional-mark-on-non-array";

    /// <summary>`foreign[]`, which has no representation the readers can follow.</summary>
    public const string ForeignArrayUnsupported = "tabbit.foreign-array-unsupported";

    /// <summary>A multi-target reference that also names a field of one target.</summary>
    public const string MultiTargetNamesAField = "tabbit.multi-target-names-a-field";

    /// <summary>A type that cannot be the element type of an array.</summary>
    public const string TypeNotArrayElement = "tabbit.type-not-array-element";

    /// <summary>A type named in the type cell and again in the detail-type cell.</summary>
    public const string TypeNamedTwice = "tabbit.type-named-twice";

    /// <summary>Two entities of one kind with the same name.</summary>
    public const string EntityNameDuplicated = "tabbit.entity-name-duplicated";

    /// <summary>An entity marker pointing past the edge of the sheet.</summary>
    public const string EntityStartsOutsideSheet = "tabbit.entity-starts-outside-sheet";

    /// <summary>An entity with fewer cells than its kind needs.</summary>
    public const string EntityTooSmall = "tabbit.entity-too-small";

    /// <summary>A marker cell holding something that is not a marker.</summary>
    public const string UnexpectedEntityMarker = "tabbit.unexpected-entity-marker";

    /// <summary>An `@` in a field name with no positive integer after it.</summary>
    public const string WireTagNotPositiveInteger = "tabbit.wire-tag-not-positive-integer";

    /// <summary>A wire tag below one.</summary>
    public const string WireTagBelowOne = "tabbit.wire-tag-below-one";
}
