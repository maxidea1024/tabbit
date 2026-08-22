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
}
