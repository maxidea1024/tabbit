using Tabbit.Messages;

namespace Tabbit.Schema;

/// <summary>
/// The reports reading a schema file writes, named.
/// </summary>
/// <remarks>
/// `schema` because that is what the files are - the declarations a sheet's type cell names,
/// kept where a person can edit them without opening a workbook. The prefix is the step, the
/// same convention <see cref="Cooking.CookingMessages"/> follows.
///
/// Every one of these points at a line and a column of a text file, which is what
/// <see cref="Models.Location.OfTextFile"/> is for: a report from here is meant to be
/// clickable in the same terminal a compiler's is.
/// </remarks>
[TabbitMessages("schema")]
public static class SchemaMessages
{
    /// <summary>A directory of schema files the recipe names and there is none.</summary>
    public const string PathMissing = "schema.path-missing";

    /// <summary>A line whose first word is not one of the seven keywords.</summary>
    public const string UnknownKeyword = "schema.unknown-keyword";

    /// <summary>`abstract` or `extends`, which are reserved and not settled yet.</summary>
    public const string PolymorphismReserved = "schema.polymorphism-reserved";

    /// <summary>A `field` line with no `struct` open before it.</summary>
    public const string FieldOutsideStruct = "schema.field-outside-struct";

    /// <summary>A `field` line where the declaration it would join is an `enum`.</summary>
    public const string FieldInEnum = "schema.field-in-enum";

    /// <summary>A `value` line with no `enum` open before it.</summary>
    public const string ValueOutsideEnum = "schema.value-outside-enum";

    /// <summary>A `value` line where the declaration it would join is a `struct`.</summary>
    public const string ValueInStruct = "schema.value-in-struct";

    /// <summary>A declaration with no name after its keyword.</summary>
    public const string NameExpected = "schema.name-expected";

    /// <summary>A name that is not spelled as an identifier.</summary>
    public const string NameNotIdentifier = "schema.name-not-identifier";

    /// <summary>A `field` line that names no type.</summary>
    public const string TypeExpected = "schema.type-expected";

    /// <summary>`foreign` with no table named after it.</summary>
    public const string ForeignTargetExpected = "schema.foreign-target-expected";

    /// <summary>A second `[]` on one type.</summary>
    public const string TypeNestedArray = "schema.type-nested-array";

    /// <summary>A container type given the wrong number of arguments.</summary>
    public const string ContainerArity = "schema.container-arity";

    /// <summary>Type arguments on a type that takes none.</summary>
    public const string TypeTakesNoArguments = "schema.type-takes-no-arguments";

    /// <summary>Something written after a declaration was already complete.</summary>
    public const string UnexpectedToken = "schema.unexpected-token";

    /// <summary>A `(` with no `)` before the end of the line.</summary>
    public const string MetaUnclosed = "schema.meta-unclosed";

    /// <summary>Metadata brackets with something other than a key in them.</summary>
    public const string MetaKeyExpected = "schema.meta-key-expected";

    /// <summary>A metadata `=` with nothing after it.</summary>
    public const string MetaValueExpected = "schema.meta-value-expected";

    /// <summary>The same metadata key written twice on one declaration.</summary>
    public const string MetaDuplicateKey = "schema.meta-duplicate-key";

    /// <summary>`comment=` in metadata, which is what `///` is for.</summary>
    public const string MetaCommentKey = "schema.meta-comment-key";

    /// <summary>A quoted string with no closing quote before the end of the line.</summary>
    public const string StringUnterminated = "schema.string-unterminated";

    /// <summary>A `/*` with no `*/` before the end of the file.</summary>
    public const string BlockCommentUnterminated = "schema.block-comment-unterminated";

    /// <summary>A character the notation has no meaning for.</summary>
    public const string CharacterUnexpected = "schema.character-unexpected";

    /// <summary>A wire tag that is not a whole number.</summary>
    public const string WireTagNotANumber = "schema.wire-tag-not-a-number";

    /// <summary>A wire tag of zero or less.</summary>
    public const string WireTagNotPositive = "schema.wire-tag-not-positive";

    /// <summary>Two members of one struct given the same wire tag.</summary>
    public const string WireTagDuplicate = "schema.wire-tag-duplicate";

    /// <summary>A struct where some members carry a wire tag and some do not.</summary>
    public const string WireTagPartial = "schema.wire-tag-partial";

    /// <summary>Two members of one struct with the same name.</summary>
    public const string MemberDuplicate = "schema.member-duplicate";

    /// <summary>Two entries of one enum with the same name.</summary>
    public const string EnumValueDuplicate = "schema.enum-value-duplicate";

    /// <summary>Two entries of one enum given the same number.</summary>
    public const string EnumNumberDuplicate = "schema.enum-number-duplicate";

    /// <summary>An enum entry whose value is not a whole number.</summary>
    public const string EnumValueNotANumber = "schema.enum-value-not-a-number";

    /// <summary>A `///` block with no declaration after it.</summary>
    public const string DocCommentAttachedToNothing = "schema.doc-comment-attached-to-nothing";

    /// <summary>A `=` with nothing after it where a default value belongs.</summary>
    public const string DefaultValueExpected = "schema.default-value-expected";

    // ------------------------------------------------------ once every file is read

    /// <summary>One name declared by two declarations.</summary>
    public const string DeclaredTwice = "schema.declared-twice";

    /// <summary>A declaration whose name a sheet has already given to something.</summary>
    public const string NameTakenBySheet = "schema.name-taken-by-sheet";

    /// <summary>A member typed with a name nothing declares.</summary>
    public const string TypeUnknown = "schema.type-unknown";

    /// <summary>A member typed with an enum a sheet declared rather than these files.</summary>
    public const string TypeIsSheetEnum = "schema.type-is-sheet-enum";

    /// <summary>A container type, which the notation reads and this does not yet carry.</summary>
    public const string ContainerNotSupported = "schema.container-not-supported";

    /// <summary>A struct that holds itself, however far around.</summary>
    public const string StructCycle = "schema.struct-cycle";

    /// <summary>An enum entry whose number will not fit what the data carries.</summary>
    public const string EnumNumberOutOfRange = "schema.enum-number-out-of-range";

    // ------------------------------------------------- binding a group to a declaration

    /// <summary>Two columns of one group naming a struct.</summary>
    public const string GroupTypedTwice = "schema.group-typed-twice";

    /// <summary>A column of a typed group that the struct has no member for.</summary>
    public const string ColumnNotAMember = "schema.column-not-a-member";

    /// <summary>A column that wrote a type the declaration disagrees with.</summary>
    public const string ColumnTypeDisagrees = "schema.column-type-disagrees";

    /// <summary>A member whose type cannot be what a single column holds.</summary>
    public const string MemberTypeUnusable = "schema.member-type-unusable";

    /// <summary>A member of a typed group that the sheet gave no column.</summary>
    public const string MemberHasNoColumn = "schema.member-has-no-column";

    /// <summary>A column left untyped that no group claimed.</summary>
    public const string ColumnHasNoType = "schema.column-has-no-type";

    /// <summary>A struct named on a column that is not a group.</summary>
    public const string ColumnTypedWithAStruct = "schema.column-typed-with-a-struct";

    // ------------------------------------------------------- what the brackets said

    /// <summary>A metadata key nothing defines.</summary>
    public const string MetaKeyUnknown = "schema.meta-key-unknown";

    /// <summary>A key the notation defines and this build does not act on yet.</summary>
    public const string MetaKeyNotCarried = "schema.meta-key-not-carried";

    /// <summary>`refs`, which says what `foreign` already says in this tool.</summary>
    public const string MetaRefsIsForeign = "schema.meta-refs-is-foreign";

    /// <summary>A default value, which nothing applies yet.</summary>
    public const string DefaultNotCarried = "schema.default-not-carried";

    /// <summary>`text` or `asset` on a member that is not a string.</summary>
    public const string RoleNotAString = "schema.role-not-a-string";

    /// <summary>Both `text` and `asset` on one member.</summary>
    public const string RoleWrittenTwice = "schema.role-written-twice";

    /// <summary>A bound that is not a number.</summary>
    public const string BoundNotANumber = "schema.bound-not-a-number";

    /// <summary>`allowed` with nothing in it.</summary>
    public const string AllowedEmpty = "schema.allowed-empty";

    /// <summary>A sheet's whitelist and a declaration's with nothing in common.</summary>
    public const string AllowedIntersectionEmpty = "schema.allowed-intersection-empty";

    // ------------------------------------------------ a whole value written in one cell

    /// <summary>`sep` given something other than a single character.</summary>
    public const string SepNotOneCharacter = "schema.sep-not-one-character";

    /// <summary>A member of a `sep` struct that one component cannot hold.</summary>
    public const string SepMemberNotScalar = "schema.sep-member-not-scalar";

    /// <summary>A `sep` column used as an index.</summary>
    public const string SepColumnIsAnIndex = "schema.sep-column-is-an-index";

    /// <summary>A `sep` column in a table that writes its wire tags out.</summary>
    public const string SepTableWritesTags = "schema.sep-table-writes-tags";

    /// <summary>A column constraint on a `sep` column.</summary>
    public const string SepColumnHasConstraints = "schema.sep-column-has-constraints";

    /// <summary>A cell with the wrong number of components in it.</summary>
    public const string SepComponentCount = "schema.sep-component-count";
}
