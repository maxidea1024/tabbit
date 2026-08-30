using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>Everything the C templates need, worked out in advance.</summary>
internal sealed class CFileView
{
    /// <summary>
    /// What every generated identifier starts with.
    ///
    /// C has no namespaces, so the prefix is the whole of the collision avoidance.
    /// Taken from the accessor name, lower_snake_case.
    /// </summary>
    public required string Prefix { get; set; }

    /// <summary>The prefix in upper case, for the include guard and the enum constants.</summary>
    public required string UpperPrefix { get; set; }

    /// <summary>Name of the header, so the .c can include it.</summary>
    public required string HeaderName { get; set; }

    public required IReadOnlyList<CEnumView> Enums { get; set; }
    public required IReadOnlyList<CConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<CTableView> Tables { get; set; }
    public required CAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: what it has to say at the top, and the single thing it declares.
/// </summary>
/// <remarks>
/// C is the one target where the top of the file is not bookkeeping. An include has to come
/// before what uses it, a struct member of struct type needs the complete type, and a header
/// included twice in one translation unit has to be harmless. So the three are separate
/// fields rather than one list of lines: they are answered differently and they go in a
/// particular order.
/// </remarks>
internal sealed class CPartView
{
    /// <summary>The declared record type this header is for.</summary>
    public CRecordFileView? Record { get; set; }

    /// <summary>The include guard macro. Empty for a source file, which needs none.</summary>
    public string? Guard { get; set; }

    /// <summary>`#include` lines, in the order they have to appear.</summary>
    public IReadOnlyList<string>? Includes { get; set; }

    /// <summary>
    /// Forward declaration lines. Only the forward header itself has any; every other file
    /// includes that instead.
    /// </summary>
    public IReadOnlyList<string>? Forwards { get; set; }

    /// <summary>
    /// Whether to wrap the file in `extern "C"`.
    ///
    /// Only where it means something: a typedef, an enum and a struct have no linkage, so an
    /// enum header does not need it. A function declaration and an `extern const` do.
    /// </summary>
    public bool ExternC { get; set; }

    /// <summary>Record type names, for the forward header.</summary>
    public IReadOnlyList<string>? Records { get; set; }

    /// <summary>The table this file is for, when it is a table header or source.</summary>
    public CTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum header.</summary>
    public CEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants header or source.</summary>
    public CConstantSetView? Set { get; set; }

    /// <summary>The abstract type this header declares, when it declares one.</summary>
    public CPolymorphicTypeView? Structure { get; set; }

    /// <summary>The accessor's own shape, for its header and source.</summary>
    public CAccessorView? Accessor { get; set; }
}

/// <summary>
/// One constant set.
/// </summary>
/// <remarks>
/// C has nothing to nest a set in, so the constants themselves are flat and each carries its
/// set's name. They are still grouped here, because the set is the unit the sheets add and
/// remove and so the unit a file corresponds to.
/// </remarks>
internal sealed class CConstantSetView
{
    /// <summary>The set's name, PascalCase, which names its files.</summary>
    public required string Name { get; set; }

    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CConstantView> Constants { get; set; }
}

internal sealed class CEnumView
{
    /// <summary>Enum name as the sheet spelled it, PascalCase. Names its header.</summary>
    /// <remarks>
    /// Separate from <see cref="Name"/> because that one already carries the accessor prefix -
    /// it is the C type name - and a file named from it comes out as
    /// `X_EnumX_Flag_t.h`.
    /// </remarks>
    public required string RawName { get; set; }

    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CEnumLabelView> Labels { get; set; }
}

internal sealed class CEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

/// <summary>
/// One constant, already flattened out of its set.
///
/// C has nothing to nest them in, so the set's name becomes part of each constant's
/// name rather than a scope around them.
/// </summary>
internal sealed class CConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// Whether the value can be an initializer in a header.
    ///
    /// A uuid cannot: it is a struct, and one defined in a header would be a separate
    /// object in every translation unit that included it. Those go in the .c and are
    /// declared `extern` instead.
    /// </summary>
    public required bool IsExtern { get; set; }

    /// <summary>
    /// What follows the name in the declaration, which for an array is `[]`.
    /// </summary>
    /// <remarks>
    /// **C puts an array's brackets after the name**, so the type alone cannot say it - and
    /// this language's array format is the element pointer, which is what a member of a row
    /// needs and not what a file-scope array of static data is. So a constant carries the
    /// brackets separately, and a count beside it because there is nowhere in the type for
    /// one. spec/layout/primary-layout.md section 8.5.
    /// </remarks>
    public required string NameSuffix { get; set; }

    /// <summary>
    /// The name of the count constant, or empty when this constant is one value.
    /// </summary>
    public required string CountName { get; set; }

    /// <summary>How many elements, as text. Empty when this constant is one value.</summary>
    public required string Count { get; set; }
}

/// <summary>A grid's accessor, spelled the way C spells it.</summary>
/// <remarks>
/// <see cref="MatrixPlan"/> decides the shape; this is its spelling.
/// spec/layout/matrix-declaration.md.
/// </remarks>
internal sealed class CMatrixView
{
    public required string ColumnTable { get; init; }

    public required string ColumnTableName { get; init; }

    public required string ColumnRecord { get; init; }

    public required string ColumnPrefix { get; init; }

    public required string ColumnLookup { get; init; }

    public required string RowKeyMember { get; init; }

    public required string RowKeyParam { get; init; }

    public required string RowKeyType { get; init; }

    public required string RowLookup { get; init; }

    public required string ColumnKeyMember { get; init; }

    public required string ColumnKeyParam { get; init; }

    public required string ColumnKeyType { get; init; }

    public required string AtMember { get; init; }

    public required string GridMember { get; init; }

    public required string GridHasMember { get; init; }

    public required string CellType { get; init; }

    public required bool CellsAreOptional { get; init; }
}

internal sealed class CTableView
{
    public required string RawName { get; set; }

    /// <summary>The record struct's name, already prefixed.</summary>
    public required string RecordName { get; set; }

    /// <summary>The table struct's name, already prefixed.</summary>
    public required string TableName { get; set; }


    /// <summary>What the table's functions are called, minus the verb.</summary>
    public required string FunctionPrefix { get; set; }

    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<CIndexView> Indexes { get; set; }

    /// <summary>The grid this table holds the values of, or null when it is not one.</summary>
    public CMatrixView? Matrix { get; set; }

    /// <summary>Whether any member holds strings, and so needs the pre-read pass.</summary>
    public required bool HasStringFields { get; set; }

    /// <summary>
    /// Whether any column reads through the cursor, and so the parse declares one.
    ///
    /// One cursor variable for the whole function rather than one per column: C89
    /// declarations sit at the top of the block, and each encodable column
    /// re-initializes it.
    /// </summary>
    public required bool NeedsCursor { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields: declaring a member is per field and reading is per
    /// column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<CColumnView> Columns { get; set; }

    /// <summary>Whether the read declares the presence buffer: true when any column is optional.</summary>
    public required bool NeedsPresence { get; set; }

    /// <summary>Whether any column of this table carries an element bitmap.</summary>
    public bool NeedsElementPresence { get; set; }

    public required IReadOnlyList<CFieldView> Fields { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
/// <remarks>
/// Two lookups rather than the three every other target gets. C has nothing to throw,
/// so there is no honest `GetBy...OrThrow` to generate - a caller that needs the row
/// to be there checks the NULL, which is the same check it would write anyway.
/// </remarks>
internal sealed class CIndexView
{
    /// <summary>The record member holding the key, escaped.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `...FindByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type, as a parameter declaration.</summary>
    public required string KeyType { get; set; }

    /// <summary>The runtime's entry type for this key: `tb_index_entry` and its kin.</summary>
    public required string EntryType { get; set; }

    /// <summary>The runtime's sort for this key.</summary>
    public required string SortCall { get; set; }

    /// <summary>The runtime's bisection for this key.</summary>
    public required string FindCall { get; set; }

    /// <summary>The table member holding the sorted entries.</summary>
    public required string ArrayName { get; set; }

    /// <summary>The field as the sheet spells it, for the doc comment.</summary>
    public required string FieldName { get; set; }

    /// <summary>Whether the key is several columns taken together.</summary>
    public required bool IsComposite { get; set; }

    /// <summary>The columns making it up - one entry unless it is composite.</summary>
    public required IReadOnlyList<KeyComponentView> Components { get; set; }

    /// <summary>The lookup's parameter list, one entry per column of the key.</summary>
    public required string Params { get; set; }

    /// <summary>What the lookup passes on, given those parameters.</summary>
    public required string Argument { get; set; }

    /// <summary>The function that joins a composite key's columns into its text.</summary>
    public required string KeyBuilder { get; set; }
}

internal sealed class CFieldView
{
    /// <summary>
    /// How the builder reaches the entry it reads from: the row's own member, or the element it
    /// was handed.
    /// </summary>
    /// <remarks>
    /// One name for both shapes, so the body that fills a variant is written once. A
    /// polymorphic array's builder takes an element; a scalar group's reads the row.
    /// spec/types/polymorphism.md section 5.3.
    /// </remarks>
    public string EntryAccess { get; set; } = "";

    /// <summary>
    /// Whether the polymorphic group is an array, so each element carries its own variant.
    /// </summary>
    /// <remarks>
    /// Asked rather than worked out from the read kind, which is spelled differently in every
    /// generator - and the two shapes differ in more than one line: the accessor loops, and
    /// the value it hands back is an array of the abstract type.
    /// spec/types/polymorphism.md section 5.3.
    /// </remarks>
    public bool VariantsAreArray { get; set; }

    /// <summary>
    /// The variants of a polymorphic group, or empty when the group is one fixed shape.
    /// </summary>
    /// <remarks>
    /// The type itself is declared once, elsewhere; this is what the table needs to build a
    /// value of it - which number means which variant, and which of the entry's fields each
    /// one carries. spec/types/polymorphism.md sections 7.1 and 7.2.
    /// </remarks>
    public IReadOnlyList<CVariantView> Variants { get; set; } = new List<CVariantView>();

    /// <summary>The abstract type the variants make up, or empty.</summary>
    public string AbstractTypeName { get; set; } = "";

    /// <summary>The enum naming which variant a value is, or empty.</summary>
    public string KindEnumName { get; set; } = "";

    /// <summary>
    /// The group's name as a function name carries it, which this target spells in Pascal.
    /// </summary>
    /// <remarks>
    /// The member is `effect` and the function is `SkilleffectKind` without this - the field
    /// name is the struct member's spelling and a function name is not.
    /// </remarks>
    public string PascalName { get; set; } = "";

    /// <summary>What the discriminator member is called on the flat entry.</summary>
    public string DiscriminatorName { get; set; } = "";

    /// <summary>The members every variant carries.</summary>
    public IReadOnlyList<CStructMemberView> BaseMembers { get; set; }
        = new List<CStructMemberView>();

    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The member declarations.
    ///
    /// A list, because a variable length array contributes a pointer and a count, and a
    /// reference contributes an index as well as the resolved pointer.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// Whether this field is an array, so the string initialisation skips it.
    /// </summary>
    /// <remarks>
    /// There is nothing to point at yet: the array is allocated by the read, out of the
    /// arena, once the count is known. The pass that gives string members `""` runs before
    /// that and would be assigning to a pointer-to-pointer.
    ///
    /// Two flags stood here until v107, one for each array kind, and the fixed one made the
    /// pass loop the elements instead. There is one kind now, so there is one answer.
    /// </remarks>
    public required bool IsArray { get; set; }

    /// <summary>Whether this field is a record group, so a struct is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares no element type - the outer
    /// level has no name. See spec/types/nested-multi-level.md.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>Name of that struct's tag. Empty for an ordinary field.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The members of that struct. Empty for an ordinary field.</summary>
    public required IReadOnlyList<CRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every struct this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<CRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<CRecordTypeView>();

    /// <summary>
    /// Whether the sheet marked this field optional, so a row may have no value for it.
    /// </summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";


    /// <summary>
    /// Whether this member holds strings, and so needs pointing at something before
    /// the read.
    /// </summary>
    /// <remarks>
    /// The arena hands back zeroed memory, which for a `const char*` is NULL - and a
    /// column the file does not carry leaves it that way. Every other language gives an
    /// empty string there; in C a NULL reaches printf and takes the process with it, so
    /// the generated parse points every string member at "" before reading a column.
    /// </remarks>
    public required bool IsString { get; set; }


    public required int ElementCount { get; set; }


}

/// <summary>One grid, as the accessor's linking pass names it.</summary>
internal sealed class CGridLinkView
{
    public required string Values { get; init; }

    public required string Columns { get; init; }

    public required string Prefix { get; init; }
}

internal sealed class CAccessorView
{
    /// <summary>The prefix its functions carry: `TabbitData`, giving `TabbitData_Free`.</summary>
    public required string Name { get; set; }

    /// <summary>Its struct's name, which is the prefix with the type suffix.</summary>
    public required string TypeName { get; set; }

    public required string FileExtension { get; set; }
    public required IReadOnlyList<CTableSlotView> Tables { get; set; }

    /// <summary>Every grid, as the pass that hands each one its axis names it.</summary>
    public IReadOnlyList<CGridLinkView> Grids { get; set; }
        = System.Array.Empty<CGridLinkView>();
    public required IReadOnlyList<CCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class CTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string FunctionPrefix { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class CCrossReferenceView
{
    public required string Table { get; set; }
    public required string FunctionPrefix { get; set; }

    /// <summary>The record struct being walked, which the loop declares a pointer to.</summary>
    public required string RecordName { get; set; }

    public required IReadOnlyList<CReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<CRecordReferenceView> RecordFields { get; set; }


}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class CRecordReferenceView
{
    /// <summary>The resolved row this writes, loop variable included.</summary>
    public required string Access { get; set; }

    /// <summary>The stored key it resolves through.</summary>
    public required string Key { get; set; }

    /// <summary>
    /// The loop bound, or empty where the group is one record and there is nothing to walk.
    /// </summary>
    public required string Count { get; set; }

    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, prefix and all.</summary>
    public required string RefLookup { get; set; }

    /// <summary>The referenced record's struct, which the resolved member points at.</summary>
    public required string RefRecordName { get; set; }
}

internal sealed class CReferenceFieldView
{
    /// <summary>
    /// Where the resolved row is stored - the derived name, where this reference is to a
    /// whole row. spec/references/reference-surface-naming.md section 5.
    /// </summary>
    public string RowName { get; set; } = "";

    public required string Name { get; set; }
    public required string RefTable { get; set; }
    public required string RefFunctionPrefix { get; set; }

    /// <summary>
    /// The referenced table's primary lookup, prefix and all, which is what a key
    /// resolves through.
    /// </summary>
    public required string RefLookup { get; set; }

    /// <summary>The referenced record's struct, which the resolved member points at.</summary>
    public required string RefRecordName { get; set; }

    /// <summary>What the resolved member is assigned, with `target` naming the row.</summary>
    public required string Value { get; set; }

    public required bool IsArray { get; set; }

    /// <summary>
    /// How many elements the resolution loop runs over.
    ///
    /// A literal for a serial field, whose length is fixed at generation, and the
    /// record's own count member for a variable length one.
    /// </summary>
    public required string CountExpression { get; set; }
}

/// <summary>One member of a record group's generated struct.</summary>
internal sealed class CRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The whole declaration line, type and name included.</summary>
    public required string Declaration { get; set; }

}


/// <summary>
/// One generated struct of a record group - the group's own element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax.
///
/// Innermost first, and here that is not only tidiness - a struct has to be complete before
/// another declares a member of it. spec/types/nested-multi-level.md.
/// </remarks>
/// <summary>One declared record type, and the levels inside it that are its own.</summary>
/// <remarks>spec/types/declared-struct-identity.md.</remarks>
internal sealed class CRecordFileView
{
    public required Models.RecordType Declared { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<string> Comment { get; init; }

    public required IReadOnlyList<CRecordTypeView> Types { get; init; }
}

internal sealed class CRecordTypeView
{
    /// <summary>Whether the type is a declaration's, so it is written once elsewhere.</summary>
    /// <remarks>spec/types/declared-struct-identity.md.</remarks>
    public bool IsShared { get; set; }

    /// <summary>Name of the struct tag, which carries the table's and the group's.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of the struct.</summary>
    public required IReadOnlyList<CRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct belongs to, for its comment.</summary>
    public required string Owner { get; set; }

    /// <summary>
    /// The lookup functions this struct publishes for a `set` or a `map`.
    /// </summary>
    /// <remarks>
    /// **Functions rather than containers, because this language has neither.** The arrays
    /// are already there and already in the file's order, so what a lookup needs is a way to
    /// ask - and a scan over a row's entries needs no second structure and no allocation.
    /// spec/types/set-and-map.md section 7.2.
    /// </remarks>
    public IReadOnlyList<string> Lookups { get; set; } = System.Array.Empty<string>();

}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
internal sealed class CColumnView
{
    /// <summary>
    /// The member access ending in the row's derived name, for a reference member.
    /// spec/references/reference-surface-naming.md section 5.
    /// </summary>
    public string RowMemberAccess { get; set; } = "";

    /// <summary>
    /// Where the resolved rows go - the derived name for a whole-row reference, the column's
    /// own name for a dotted one. spec/references/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    public required int WireTag { get; set; }

    /// <summary>Which read shape applies.</summary>
    public required string Kind { get; set; }

    /// <summary>The rendered tb_check_column call.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>The cursor opening placed ahead of the row loop, or empty.</summary>
    public required string CursorOpen { get; set; }

    /// <summary>
    /// How an array column's row learns how many elements it holds.
    /// </summary>
    /// <remarks>
    /// From the cursor where the column reads through one, because an encoded array's
    /// lengths are their own stream at the front of the block rather than a number in front
    /// of each row. The cursor answers the same call either way, so this is one line and not
    /// a branch in the emitted loop.
    /// </remarks>
    public required string LengthRead { get; set; }

    /// <summary>
    /// The cursor's run call for a scalar whose column can arrive run-length encoded, or
    /// empty for one that reads row by row.
    /// </summary>
    /// <remarks>
    /// A run of a hundred thousand rows costs one call through this and a hundred
    /// thousand plain assignments, instead of a hundred thousand calls that each
    /// re-dispatch on the encoding.
    /// </remarks>
    public required string RunCall { get; set; }

    /// <summary>The declaration of the local the run decodes into, initialized.</summary>
    public required string RunValueDeclaration { get; set; }

    /// <summary>
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// <see cref="RunCall"/> opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required string RunSpend { get; set; }

    /// <summary>The member this column fills, without any element or field access.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The field of the element type this column fills, with a leading dot, or empty when
    /// the column is not a record member.
    /// </summary>
    public required string MemberAccess { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>How many columns fill this field, where that is a generated shape.</summary>
    public required int ElementCount { get; set; }

    /// <summary>The element's C type.</summary>
    public required string ElementType { get; set; }

    /// <summary>
    /// What one element of a reference array resolves to, written as it is declared, or
    /// empty for a column that is not a reference.
    /// </summary>
    /// <remarks>
    /// A whole-row reference resolves to a pointer to const, so this carries the `const` and
    /// the star; a field reference resolves to that value's own type and carries neither.
    /// The read allocates the resolved array beside the keys and leaves it NULL, which is
    /// what says the resolution has not happened yet.
    /// </remarks>
    public required string ReferenceType { get; set; }

    /// <summary>The type of the stored key of a reference column, or empty.</summary>
    public required string KeyType { get; set; }

    /// <summary>The struct tag of the record group this column belongs to, or empty.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that
    /// allocates when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    /// <summary>Whether the read needs the int32 scratch an enum is cast from.</summary>
    public required bool NeedsScratch { get; set; }

    /// <summary>The enum's type name, when this column reads one.</summary>
    public required string? EnumType { get; set; }

    /// <summary>The rendered read for a scalar row.</summary>
    public required string ReadScalar { get; set; }

    /// <summary>The rendered read for one element.</summary>
    public required string ReadElement { get; set; }

    /// <summary>
    /// The rendered read filling the int32 scratch an enum is cast from, whatever depth
    /// the template places it at.
    /// </summary>
    /// <remarks>
    /// Its own line rather than part of <see cref="ReadScalar"/> or
    /// <see cref="ReadElement"/> because C will not let the member be filled directly: an
    /// enum has an implementation-defined underlying type, so the value is read as an
    /// int32 and cast.
    /// </remarks>
    public required string ReadScratch { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>
    /// The whole statement putting an absent row's value back, so both read paths agree.
    /// </summary>
    /// <remarks>
    /// A statement, not a value: a uuid is a struct in C and `= 0` does not compile for one.
    /// </remarks>
    public required string EmptyAssignment { get; set; }
}

/// <summary>
/// An abstract type and its variants, as this target declares them.
/// </summary>
/// <remarks>
/// One per declaration however many tables named it. A struct is an entity beside a table and
/// an enum, and emitting it inside each table that used it would give them types that share a
/// name and are not the same type. spec/types/polymorphism.md section 7.1.
/// </remarks>
internal sealed class CPolymorphicTypeView
{
    /// <summary>The abstract type's name, already carrying this target's prefix.</summary>
    public required string Name { get; set; }

    /// <summary>The enum naming which variant a value is.</summary>
    /// <remarks>
    /// **The one place in this repository where a discriminator enum earns its keep.** There
    /// are no variant types to test against here, so a consumer has to branch on a number - and
    /// a number with no name is a magic one. Section 7.1 declined to put this in the model for
    /// every language; this generator emits its own, which is what that section said it would.
    /// </remarks>
    public required string KindEnumName { get; set; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required IReadOnlyList<CStructMemberView> BaseMembers { get; set; }

    /// <summary>What one of its values may be.</summary>
    public required IReadOnlyList<CVariantView> Variants { get; set; }
}

/// <summary>One variant of a <see cref="CPolymorphicTypeView"/>.</summary>
internal sealed class CVariantView
{
    /// <summary>The variant's struct name, already carrying this target's prefix.</summary>
    public required string TypeName { get; set; }

    /// <summary>The enum constant naming this variant.</summary>
    public required string KindName { get; set; }

    /// <summary>The suffix a per-variant accessor is named with.</summary>
    public required string Suffix { get; set; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; set; }

    /// <summary>The members this variant declares, beside the base ones.</summary>
    public required IReadOnlyList<CStructMemberView> Members { get; set; }
}

/// <summary>One member of an abstract type or of one of its variants.</summary>
/// <remarks>
/// A view of its own rather than the record member's, because none of the reference machinery
/// applies: the model refuses a reference inside a polymorphic group.
/// </remarks>
internal sealed class CStructMemberView
{
    /// <summary>
    /// Where the row a reference member resolved to goes, or empty when the member is a value.
    /// </summary>
    /// <remarks>
    /// **A reference member is two fields here, the same two a reference column is anywhere.**
    /// The declared name is the key's - that is what the cell holds - and the row it resolves to
    /// takes the derived one. spec/references/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    public string RowName { get; set; } = "";

    /// <summary>The key's type, for a reference member.</summary>
    public string KeyTypeName { get; set; } = "";

    /// <summary>The member's name in the generated type.</summary>
    public required string Name { get; set; }

    /// <summary>Its type in this language.</summary>
    public required string TypeName { get; set; }

    /// <summary>The documentation lines the declaration carried.</summary>
    public required IReadOnlyList<string> Comment { get; set; }
}
