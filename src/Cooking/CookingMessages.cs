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

    /// <summary>A name that cannot be an identifier in the generated languages.</summary>
    public const string InvalidIdentifier = "cook.invalid-identifier";

    /// <summary>An unrecognized type whose spelling has a `?` in the wrong place.</summary>
    public const string UnrecognizedTypeQuestionMark = "cook.unrecognized-type-question-mark";

    /// <summary>An unrecognized type.</summary>
    public const string UnrecognizedType = "cook.unrecognized-type";

    /// <summary>Brackets after a type that does not take a bracketed name.</summary>
    public const string TypeTakesNoBrackets = "cook.type-takes-no-brackets";

    /// <summary>A type that cannot be the element type of an array.</summary>
    public const string TypeNotArrayElement = "cook.type-not-array-element";

    /// <summary>A type name this tool does not map.</summary>
    public const string UnsupportedType = "cook.unsupported-type";

    /// <summary>A target side that is not one of the ones there are.</summary>
    public const string IllegalTargetSide = "cook.illegal-target-side";

    /// <summary>A cell holding a formula that evaluated to an error.</summary>
    public const string FormulaError = "cook.formula-error";

    /// <summary>An empty cell in a required column.</summary>
    public const string BlankCellRequired = "cook.blank-cell-required";

    /// <summary>An empty cell in an optional column that still needs a mark.</summary>
    public const string BlankCellOptional = "cook.blank-cell-optional";

    /// <summary>A cell whose text will not read as its column's type.</summary>
    public const string ValueUnparsable = "cook.value-unparsable";

    /// <summary>A cell that is not any spelling of true or false.</summary>
    public const string NotABoolean = "cook.not-a-boolean";

    /// <summary>An element marked as having no value where elements are required.</summary>
    public const string ArrayElementNoValueMark = "cook.array-element-no-value-mark";

    /// <summary>A wall-clock time the zone skipped when daylight saving began.</summary>
    public const string TimeInDstGap = "cook.time-in-dst-gap";

    /// <summary>A magnitude too large for a signed 64-bit value.</summary>
    public const string MagnitudeTooLarge = "cook.magnitude-too-large";

    /// <summary>An integer above what the float type holds exactly.</summary>
    public const string FloatLosesExactness = "cook.float-loses-exactness";

    /// <summary>More digits than 64 bits hold in that base.</summary>
    public const string RadixTooManyDigits = "cook.radix-too-many-digits";

    /// <summary>A character that is not a digit of the base it was written in.</summary>
    public const string RadixBadDigit = "cook.radix-bad-digit";

    /// <summary>An empty cell in a required `bitset` column.</summary>
    public const string BitsetEmpty = "cook.bitset-empty";

    /// <summary>A signed value in a `bitset`, which holds a pattern rather than a magnitude.</summary>
    public const string BitsetSigned = "cook.bitset-signed";

    /// <summary>A decimal point in a `bitset`.</summary>
    public const string BitsetDecimalPoint = "cook.bitset-decimal-point";

    /// <summary>A thousands separator in a `bitset`.</summary>
    public const string BitsetThousandsSeparator = "cook.bitset-thousands-separator";

    /// <summary>Exponent notation in a `bitset`.</summary>
    public const string BitsetExponent = "cook.bitset-exponent";

    /// <summary>A digit separator in a `bitset`.</summary>
    public const string BitsetDigitSeparator = "cook.bitset-digit-separator";

    /// <summary>Anything else that is not a decimal digit in a `bitset`.</summary>
    public const string BitsetNotADigit = "cook.bitset-not-a-digit";

    /// <summary>A `bitset` written in decimal above where a numeric cell stays exact.</summary>
    public const string BitsetAbove253 = "cook.bitset-above-2-53";

    /// <summary>Formula errors read as the empty value because the source says to.</summary>
    public const string NoticeFormulaErrorEmpty = "cook.notice-formula-error-empty";

    /// <summary>Blank cells read as the empty value because the source says to.</summary>
    public const string NoticeBlankFilled = "cook.notice-blank-filled";

    /// <summary>Blank cells now meaning the type's empty value rather than "no value".</summary>
    public const string NoticeBlankIsEmptyValue = "cook.notice-blank-is-empty-value";

    /// <summary>Times that occur twice in the zone, read as the standard-time one.</summary>
    public const string NoticeAmbiguousTime = "cook.notice-ambiguous-time";
    /// <summary>A type with no empty value declared optional.</summary>
    public const string TypeHasNoEmptyValue = "cook.type-has-no-empty-value";

    /// <summary>A table's index column typed something that cannot key rows.</summary>
    public const string IndexTypeUnusableInTable = "cook.index-type-unusable-in-table";

    /// <summary>A table's index column typed optional.</summary>
    public const string IndexFieldOptional = "cook.index-field-optional";

    /// <summary>A table's index column restricted to one target side.</summary>
    public const string IndexFieldTargetSide = "cook.index-field-target-side";

    /// <summary>A wire tag on a serial field member other than the first.</summary>
    public const string WireTagOnSerialMember = "cook.wire-tag-on-serial-member";

    /// <summary>A reserved wire tag with no live field carrying one.</summary>
    public const string WireTagOnlyOnTombstone = "cook.wire-tag-only-on-tombstone";

    /// <summary>A table where some fields carry a wire tag and some do not.</summary>
    public const string WireTagsPartial = "cook.wire-tags-partial";

    /// <summary>A wire tag another column already holds.</summary>
    public const string WireTagReused = "cook.wire-tag-reused";
}
