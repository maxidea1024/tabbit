using Tabbit.Messages;

namespace Tabbit.Cooking;

/// <summary>
/// The reports cooking writes, named.
/// </summary>
/// <remarks>
/// `cook` because that is the step of the run these come from - the same names
/// <see cref="LogCategory"/> uses, so a report and the log lines around it agree about where
/// in a run they happened.
///
/// The names here say what was wrong, not what the sentence says. A message can be reworded
/// without the id moving, which is the point of having ids at all.
///
/// Where one call site had a conditional inside its sentence, it has two ids here instead -
/// a catalog entry cannot hold an `if`, and a translator handed `{Where}` cannot know whether
/// it will be empty. Splitting is what turns those into two sentences somebody can translate.
/// </remarks>
[TabbitMessages("cook")]
public static class CookingMessages
{
    /// <summary>A role's brackets were opened and nothing was named in them.</summary>
    public const string RoleGroupEmpty = "cook.role-group-empty";

    /// <summary>A second name in the brackets of a role that takes one name.</summary>
    public const string RoleSpaceNotText = "cook.role-space-not-text";

    /// <summary>A trailing comma in a role's brackets with no namespace after it.</summary>
    public const string RoleSpaceEmpty = "cook.role-space-empty";

    /// <summary>An array element left empty with a later one filled in.</summary>
    public const string ArrayGap = "cook.array-gap";

    /// <summary>A column marked required inside an object that is not in one.</summary>
    public const string RequiredInObjectOutsideObject = "cook.required-in-object-outside-object";

    /// <summary>A record element exists and a member it declares required is empty.</summary>
    public const string RecordMemberRequiredEmpty = "cook.record-member-required-empty";

    /// <summary>A required column left empty.</summary>
    public const string RequiredEmpty = "cook.required-empty";

    /// <summary>Asset columns with no folders configured to check them against.</summary>
    public const string AssetNoRoots = "cook.asset-no-roots";

    /// <summary>Asset columns present and the recipe asks for missing files to be ignored.</summary>
    public const string AssetCheckIgnored = "cook.asset-check-ignored";

    /// <summary>An `asset` column with no kind, and no folder configured for that.</summary>
    public const string AssetNoFolderWithoutKind = "cook.asset-no-folder-without-kind";

    /// <summary>An `asset(kind)` column whose kind has no folder configured.</summary>
    public const string AssetNoFolderForKind = "cook.asset-no-folder-for-kind";

    /// <summary>An index column of a type that cannot key rows.</summary>
    public const string IndexTypeUnusable = "cook.index-type-unusable";

    /// <summary>An index column typed optional.</summary>
    public const string IndexOptional = "cook.index-optional";

    /// <summary>Two rows with the same value in an index column.</summary>
    public const string IndexDuplicate = "cook.index-duplicate";

    /// <summary>A row with no value in the column that identifies it.</summary>
    public const string IndexAbsent = "cook.index-absent";

    /// <summary>A reference to a table whose index is an enum.</summary>
    public const string ReferenceEnumKey = "cook.reference-enum-key";

    /// <summary>A reference column left blank.</summary>
    public const string ReferenceBlank = "cook.reference-blank";

    /// <summary>A required reference column saying it points at nothing.</summary>
    public const string ReferenceNoneButRequired = "cook.reference-none-but-required";

    /// <summary>A reference to a row that is not in the target table.</summary>
    public const string ReferenceMissingRow = "cook.reference-missing-row";

    /// <summary>A reference to a table this build's target side excludes.</summary>
    public const string ReferenceExcludedBySide = "cook.reference-excluded-by-side";

    /// <summary>A reference key that will not parse as the target's key type.</summary>
    public const string ReferenceKeyUnparsable = "cook.reference-key-unparsable";

    /// <summary>A reference naming a table that does not exist.</summary>
    public const string ReferenceTableMissing = "cook.reference-table-missing";

    /// <summary>A reference naming a field of its own table, which would loop.</summary>
    public const string ReferenceFieldOfOwnTable = "cook.reference-field-of-own-table";

    /// <summary>A reference naming a field the target table does not have.</summary>
    public const string ReferenceFieldMissing = "cook.reference-field-missing";

    /// <summary>A reference chain that returns to where it started.</summary>
    public const string ReferenceCycle = "cook.reference-cycle";

    /// <summary>A reference inside a record group naming a field rather than a table.</summary>
    public const string RecordReferenceNamesField = "cook.record-reference-names-field";

    /// <summary>Two tables a column may point at that share an id.</summary>
    public const string MultiTargetIdOverlap = "cook.multi-target-id-overlap";

    /// <summary>A value that is not a row of any table the column points at.</summary>
    public const string MultiTargetMissingRow = "cook.multi-target-missing-row";

    /// <summary>Tables a column points at that are keyed differently from each other.</summary>
    public const string MultiTargetKeysDiffer = "cook.multi-target-keys-differ";

    /// <summary>The enum a multi-target column needs is a name already taken.</summary>
    public const string MultiTargetEnumNameTaken = "cook.multi-target-enum-name-taken";

    /// <summary>A value the column's list of allowed values does not name.</summary>
    public const string ValueNotAllowed = "cook.value-not-allowed";

    /// <summary>As <see cref="ValueNotAllowed"/>, for one element of an array.</summary>
    public const string ElementValueNotAllowed = "cook.element-value-not-allowed";

    /// <summary>A value below the minimum the column declares.</summary>
    public const string ValueBelowMinimum = "cook.value-below-minimum";

    /// <summary>As <see cref="ValueBelowMinimum"/>, for one element of an array.</summary>
    public const string ElementValueBelowMinimum = "cook.element-value-below-minimum";

    /// <summary>A value above the maximum the column declares.</summary>
    public const string ValueAboveMaximum = "cook.value-above-maximum";

    /// <summary>As <see cref="ValueAboveMaximum"/>, for one element of an array.</summary>
    public const string ElementValueAboveMaximum = "cook.element-value-above-maximum";

    /// <summary>A set of rows whose owning table this run does not have.</summary>
    public const string RowSetOwnerMissing = "cook.row-set-owner-missing";

    /// <summary>A set of rows holding a column its owning table does not declare.</summary>
    public const string RowSetColumnsDiffer = "cook.row-set-columns-differ";
}
