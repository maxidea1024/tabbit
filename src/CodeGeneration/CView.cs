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
}

internal sealed class CFieldView
{
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
    /// Whether this field is a fixed array, so the string initialisation loops its elements.
    /// </summary>
    /// <remarks>
    /// Was read off the field's read kind, which moved to the column view when declaring and
    /// reading became separate lists - and the loop then emitted nothing, leaving an unused
    /// local that the C build treats as an error. Declaring needs its own answer.
    /// </remarks>
    public required bool IsFixedArray { get; set; }

    /// <summary>
    /// Whether this field's length is the row's, so the string initialisation skips it.
    /// </summary>
    /// <remarks>
    /// There is nothing to point at yet: the array is allocated by the read, out of the
    /// arena, once the count is known. The pass that gives string members `""` runs before
    /// that and would be assigning to a pointer-to-pointer.
    /// </remarks>
    public required bool IsVarArray { get; set; }

    /// <summary>Whether this field is a record group, so a struct is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares no element type - the outer
    /// level has no name. See spec/nested-multi-level.md.
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

internal sealed class CAccessorView
{
    /// <summary>The prefix its functions carry: `TabbitData`, giving `TabbitData_Free`.</summary>
    public required string Name { get; set; }

    /// <summary>Its struct's name, which is the prefix with the type suffix.</summary>
    public required string TypeName { get; set; }

    public required string FileExtension { get; set; }
    public required IReadOnlyList<CTableSlotView> Tables { get; set; }
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
    /// than beside it. spec/references-in-records.md.
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
/// another declares a member of it. spec/nested-multi-level.md.
/// </remarks>
internal sealed class CRecordTypeView
{
    /// <summary>Name of the struct tag, which carries the table's and the group's.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of the struct.</summary>
    public required IReadOnlyList<CRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct belongs to, for its comment.</summary>
    public required string Owner { get; set; }
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
internal sealed class CColumnView
{
    public required int Tag { get; set; }

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

    /// <summary>Elements per row for a fixed array.</summary>
    public required int ElementCount { get; set; }

    /// <summary>The element's C type.</summary>
    public required string ElementType { get; set; }

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

    /// <summary>
    /// The whole statement putting an absent row's value back, so both read paths agree.
    /// </summary>
    /// <remarks>
    /// A statement, not a value: a uuid is a struct in C and `= 0` does not compile for one.
    /// </remarks>
    public required string EmptyAssignment { get; set; }
}
