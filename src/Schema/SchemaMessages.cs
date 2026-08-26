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

    /// <summary>`abstract` with something other than `struct` after it.</summary>
    public const string AbstractNeedsStruct = "schema.abstract-needs-struct";

    /// <summary>`extends` starting a line of its own.</summary>
    public const string ExtendsOnStructLine = "schema.extends-on-struct-line";

    /// <summary>`abstract struct X extends Y` - a variant that is itself abstract.</summary>
    public const string AbstractCannotExtend = "schema.abstract-cannot-extend";

    /// <summary>`@N` on a struct that extends nothing.</summary>
    public const string VariantDiscriminatorWithoutBase = "schema.variant-discriminator-without-base";

    /// <summary>`extends` naming something that is not declared anywhere.</summary>
    public const string BaseUnknown = "schema.base-unknown";

    /// <summary>`extends` naming a struct that is not `abstract`, or an enum.</summary>
    public const string BaseNotAbstract = "schema.base-not-abstract";

    /// <summary>An `abstract struct` nothing extends.</summary>
    public const string AbstractWithoutVariants = "schema.abstract-without-variants";

    /// <summary>Two variants of one abstract struct carrying the same discriminator.</summary>
    public const string VariantDiscriminatorsCollide = "schema.variant-discriminators-collide";

    /// <summary>A variant claiming a discriminator a tombstone holds.</summary>
    public const string VariantDiscriminatorReserved = "schema.variant-discriminator-reserved";

    /// <summary>`(removed)` on a struct that extends nothing.</summary>
    public const string RemovedVariantWithoutBase = "schema.removed-variant-without-base";

    /// <summary>`(removed)` on a variant carrying no `@N`.</summary>
    public const string RemovedVariantWithoutDiscriminator =
        "schema.removed-variant-without-discriminator";

    /// <summary>`(removed)` on an `abstract struct`.</summary>
    public const string AbstractCannotBeRemoved = "schema.abstract-cannot-be-removed";

    /// <summary>Some variants of one abstract struct numbered and some not.</summary>
    public const string VariantDiscriminatorsPartial = "schema.variant-discriminators-partial";

    /// <summary>An `abstract struct` written where a column's type belongs.</summary>
    public const string AbstractTypeNotEmbeddable = "schema.abstract-type-not-embeddable";

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

    /// <summary>`int(min=1)[]` - metadata brackets between a type and its array marker.</summary>
    public const string TypeMetaBeforeArray = "schema.type-meta-before-array";

    /// <summary>An element constraint written on a container rather than on its argument.</summary>
    public const string ContainerElementMetaOutside = "schema.container-element-meta-outside";

    /// <summary>A shape a container's argument cannot be: another container, a reference, an array, optional.</summary>
    public const string ContainerArgumentUnsupported = "schema.container-argument-unsupported";

    /// <summary>A `map` key whose equality is not in the value itself.</summary>
    public const string MapKeyTypeNotAllowed = "schema.map-key-type-not-allowed";

    /// <summary>`set&lt;T&gt;[]` or `map&lt;K,V&gt;[]` - an array whose elements are containers.</summary>
    public const string ContainerArrayNotSupported = "schema.container-array-not-supported";

    /// <summary>Two elements of one `set` cell holding the same value.</summary>
    public const string SetDuplicateElement = "schema.set-duplicate-element";

    /// <summary>Two entries of one `map` under the same key.</summary>
    public const string MapDuplicateKey = "schema.map-duplicate-key";

    /// <summary>A `map` row whose key column and value column hold different counts.</summary>
    public const string MapLengthMismatch = "schema.map-length-mismatch";

    /// <summary>A `map` group with a key column and no value column, or the other way round.</summary>
    public const string MapHalfWritten = "schema.map-half-written";

    /// <summary>An entry of a paired `map` cell with no `:` in it.</summary>
    public const string MapPairMalformed = "schema.map-pair-malformed";

    /// <summary>A `map` whose value is a struct, written as pairs in one cell.</summary>
    public const string MapPairsHoldAStruct = "schema.map-pairs-hold-a-struct";

    /// <summary>A paired `map` cell in a table that writes its own wire tags.</summary>
    public const string MapPairsTableWritesTags = "schema.map-pairs-table-writes-tags";

    /// <summary>A struct a container holds whose own member is already an array.</summary>
    public const string ContainerHeldStructIsAList = "schema.container-held-struct-is-a-list";

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

    /// <summary>`refs` on a column that `foreign` already resolves.</summary>
    public const string MetaRefsIsForeign = "schema.meta-refs-is-foreign";

    /// <summary>A default written beside a `?`, which says two things about one blank.</summary>
    public const string DefaultAndOptional = "schema.default-and-optional";

    /// <summary>A default the member's own type cannot read.</summary>
    public const string DefaultUnreadable = "schema.default-unreadable";

    /// <summary>A default on the column that identifies the row.</summary>
    public const string DefaultOnAnIndex = "schema.default-on-an-index";

    /// <summary>A defaulted member on a column that wrote its own type.</summary>
    public const string DefaultNeedsAnEmptyTypeCell = "schema.default-needs-an-empty-type-cell";

    /// <summary>`text` or `asset` on a member that is not a string.</summary>
    public const string RoleNotAString = "schema.role-not-a-string";

    /// <summary>Both `text` and `asset` on one member.</summary>
    public const string RoleWrittenTwice = "schema.role-written-twice";

    /// <summary>A `namespace` on a member that is neither `text` nor `asset`.</summary>
    public const string RoleSpaceWithoutText = "schema.role-space-without-text";

    /// <summary>A `namespace` on an `asset`, whose folders the recipe keys by kind.</summary>
    public const string RoleSpaceNotText = "schema.role-space-not-text";

    /// <summary>A bound that is not a number.</summary>
    public const string BoundNotANumber = "schema.bound-not-a-number";

    /// <summary>`allowed` with nothing in it.</summary>
    public const string AllowedEmpty = "schema.allowed-empty";

    /// <summary>A sheet's whitelist and a declaration's with nothing in common.</summary>
    public const string AllowedIntersectionEmpty = "schema.allowed-intersection-empty";

    /// <summary>`refs` with no table in it.</summary>
    /// <summary>
    /// An abstract type written on a member column rather than on the group's `$type` one.
    /// </summary>
    public const string AbstractTypeOnAMemberColumn = "schema.abstract-type-on-a-member-column";

    /// <summary>A member column no variant of the abstract type declares.</summary>
    public const string MemberNoVariantDeclares = "schema.member-no-variant-declares";

    /// <summary>One member name declared with two types by two variants.</summary>
    public const string MemberTypeVariesByVariant = "schema.member-type-varies-by-variant";

    /// <summary>A `$type` cell nobody filled in.</summary>
    public const string DiscriminatorCellBlank = "schema.discriminator-cell-blank";

    /// <summary>A `$type` cell naming something that is not a variant of that type.</summary>
    public const string DiscriminatorCellUnknown = "schema.discriminator-cell-unknown";

    /// <summary>A value in a member column the row's own variant does not declare.</summary>
    public const string ValueOutsideTheRowsVariant = "schema.value-outside-the-rows-variant";

    public const string RefsEmpty = "schema.refs-empty";

    /// <summary>A sheet's table list and a declaration's with nothing in common.</summary>
    public const string RefsIntersectionEmpty = "schema.refs-intersection-empty";

    /// <summary>A pattern on a column that already has a different one.</summary>
    public const string PatternWrittenTwice = "schema.pattern-written-twice";

    /// <summary>`size` given something that is not a count.</summary>
    public const string SizeNotACount = "schema.size-not-a-count";

    /// <summary>`regex` on a member that is not a string.</summary>
    public const string PatternNotAString = "schema.pattern-not-a-string";

    /// <summary>`size` on a member that is not an array.</summary>
    public const string SizeNotAnArray = "schema.size-not-an-array";

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
