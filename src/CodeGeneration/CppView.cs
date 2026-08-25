using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything the C++ template needs, worked out in advance.
///
/// The division is deliberate: anything that depends on the model - a read call for a
/// particular element type, a default initializer, an escaped identifier - is computed
/// here and arrives as a finished string, and the template only decides where things go.
/// A template that had to reason about value types would be as hard to read as the
/// printer calls it replaced, and harder to debug.
/// </summary>
internal sealed class CppFileView
{
    public required string IncludeGuard { get; set; }

    /// <summary>`namespace x {` lines, outermost first. Empty when no namespace is set.</summary>
    public required IReadOnlyList<string> NamespaceOpen { get; set; }

    /// <summary>The matching closers, innermost first.</summary>
    public required IReadOnlyList<string> NamespaceClose { get; set; }

    public required IReadOnlyList<CppEnumView> Enums { get; set; }

    public required IReadOnlyList<CppConstantSetView> ConstantSets { get; set; }

    public required IReadOnlyList<CppTableView> Tables { get; set; }

    public required CppAccessorView Accessor { get; set; }
    /// <summary>
    /// What the accessor type is called. A view field rather than a literal in the templates,
    /// because it was a literal and the recipe's `AccessorName` therefore named only the file.
    /// </summary>
    public required string AccessorName { get; set; }
}

/// <summary>
/// One generated header: its guard, what it includes, the namespace, and the single thing it
/// declares.
/// </summary>
/// <remarks>
/// Header only, so an include here is a real dependency rather than something a source file
/// happened to pull in first - which is why they are worked out rather than handed to every
/// file alike. An include a file does not need is a compile the consumer pays for on every
/// translation unit that reaches it.
/// </remarks>
internal sealed class CppPartView
{
    public string? IncludeGuard { get; set; }

    /// <summary>`#include` lines, standard library first and then this tool's own.</summary>
    public IReadOnlyList<string>? Includes { get; set; }

    public IReadOnlyList<string>? NamespaceOpen { get; set; }
    public IReadOnlyList<string>? NamespaceClose { get; set; }

    /// <summary>Record type names, for the forward header.</summary>
    public IReadOnlyList<string>? Records { get; set; }

    /// <summary>The table this file is for, when it is a table header.</summary>
    public CppTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum header.</summary>
    public CppEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants header.</summary>
    public CppConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor header.</summary>
    public CppAccessorView? Accessor { get; set; }
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public string? AccessorName { get; set; }
}

internal sealed class CppEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }

    /// <summary>Comment text, already split into lines; the template adds the `///`.</summary>
    public required IReadOnlyList<string> Comment { get; set; }

    public required IReadOnlyList<CppEnumLabelView> Labels { get; set; }
}

internal sealed class CppEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class CppConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CppConstantView> Constants { get; set; }
}

internal sealed class CppConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class CppTableView
{
    /// <summary>Table name as the sheet spelled it. Names the table's header.</summary>
    public required string RawName { get; set; }

    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }


    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<CppIndexView> Indexes { get; set; }

    public required IReadOnlyList<CppFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from <see cref="Fields"/> because they are separate units:
    /// declaring a member is per field, and reading is per column. A record group declares
    /// one member and is read as one column per member of it.
    /// </remarks>
    public required IReadOnlyList<CppColumnView> Columns { get; set; }

    /// <summary>Whether the read declares the presence buffer: true when any column is optional.</summary>
    public required bool NeedsPresence { get; set; }

    /// <summary>Whether any column of this table carries an element bitmap.</summary>
    public bool NeedsElementPresence { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class CppIndexView
{
    /// <summary>The record member holding the key, escaped.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The map's key type.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take: a const reference where a copy would cost, the value
    /// itself where it would not.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>How the key reaches the message, since a std::string concatenates and a number does not.</summary>
    public required string KeyText { get; set; }

    /// <summary>The field as the sheet spells it, for the exception message.</summary>
    public required string FieldName { get; set; }
    /// <summary>Whether the key is several columns taken together.</summary>
    public required bool IsComposite { get; set; }

    /// <summary>The columns making it up - one entry unless it is composite.</summary>
    public required IReadOnlyList<KeyComponentView> Components { get; set; }

    /// <summary>The lookup's parameter list, one entry per column of the key.</summary>
    public required string Params { get; set; }

    /// <summary>What the map is subscripted with, given those parameters.</summary>
    public required string Argument { get; set; }

    /// <summary>The format the miss message writes the key with.</summary>
    public required string ValueFormat { get; set; }

    /// <summary>What that format is given.</summary>
    public required string ValueArgs { get; set; }

}

/// <summary>
/// One serial field, in the five shapes the generated read distinguishes.
/// </summary>
internal sealed class CppFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The member declarations. Two lines for a reference, which keeps the raw index
    /// beside the resolved value.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>Whether this field is a record group, so an element type is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>Whether that record group is an array of elements rather than one.</summary>
    public required bool IsRecordArray { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares no element type - the outer
    /// level has no name. See spec/nested-multi-level.md.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>How many inner vectors there are. Zero unless the group is one.</summary>
    public int OuterCount { get; set; }

    /// <summary>The type of one value, for the group whose element is the inner vector.</summary>
    public string ElementTypeName { get; set; } = "";

    /// <summary>Name of the generated element type, for a record group. Empty otherwise.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The members of that element type. Empty for an ordinary field.</summary>
    public required IReadOnlyList<CppRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every struct this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<CppRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<CppRecordTypeView>();

    /// <summary>
    /// Whether the sheet marked this field optional, so a row may have no value for it.
    /// </summary>
    /// <remarks>
    /// Adds a `has_{name}` member beside the value. The value member is unchanged and holds
    /// the type's empty value where a row had none - see spec/optional-fields.md for why
    /// this rather than `std::optional`.
    /// </remarks>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>The member's name, escaped for C++.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Elements per row: a serial field's column count, or a record group's element count.
    /// </summary>
    public required int ElementCount { get; set; }

}

internal sealed class CppAccessorView
{
    public required string FileExtension { get; set; }

    public required IReadOnlyList<CppTableSlotView> Tables { get; set; }

    public required IReadOnlyList<CppCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class CppTableSlotView
{
    /// <summary>Escaped member name of the table within the accessor.</summary>
    public required string Name { get; set; }

    public required string TableName { get; set; }

    /// <summary>Table name as the exporter spells the data file, unescaped.</summary>
    public required string DataFileName { get; set; }
}

internal sealed class CppCrossReferenceView
{
    /// <summary>Escaped accessor member holding the table whose records are linked.</summary>
    public required string Table { get; set; }

    public required IReadOnlyList<CppReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<CppRecordReferenceView> RecordFields { get; set; }


}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class CppRecordReferenceView
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

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }
}

internal sealed class CppReferenceFieldView
{
    /// <summary>
    /// Where the resolved row goes - the derived name for a whole-row reference, the
    /// column's own name for a dotted one.
    /// spec/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    public required string Name { get; set; }

    /// <summary>Escaped accessor member of the table being pointed at.</summary>
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    /// <summary>What the resolved reference yields - the record, or one of its fields.</summary>
    public required string Value { get; set; }

    public required string RefDefault { get; set; }

    /// <summary>Whether the field holds several references.</summary>
    public required bool IsArray { get; set; }
}

/// <summary>One member of a record group's generated element type.</summary>
internal sealed class CppRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The member's name, escaped for C++.</summary>
    public required string Name { get; set; }


    /// <summary>
    /// The declaration lines, type and initializer included.
    ///
    /// More than one for a reference member, which holds the row it resolved to as well as
    /// the key that came off the wire. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }
}

/// <summary>
/// One generated struct of a record group - the group's own element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax.
///
/// Innermost first, and here that is not only tidiness - a C++ struct has to be a complete
/// type before another declares a member of it. spec/nested-multi-level.md.
/// </remarks>
internal sealed class CppRecordTypeView
{
    /// <summary>Name of the struct.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of the struct.</summary>
    public required IReadOnlyList<CppRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct belongs to, for its doc comment.</summary>
    public required string Owner { get; set; }

}


/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
/// <remarks>
/// Everything here answers "how is this column read". Where the value lands comes along
/// because the read has to assign somewhere; the shape of the declaration is
/// <see cref="CppFieldView"/>'s business.
/// </remarks>
internal sealed class CppColumnView
{
    /// <summary>Whether this column is a reference at all.</summary>
    public bool IsReference { get; set; }

    /// <summary>
    /// Where the resolved rows go for a whole-row reference, and the member access ending in
    /// that name for a member. spec/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    public string RowMemberAccess { get; set; } = "";

    public required int WireTag { get; set; }

    /// <summary>The rendered check_column call.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>The cursor construction placed ahead of the row loop, or empty.</summary>
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
    /// The cursor's run method for a scalar whose column can arrive run-length encoded,
    /// or empty for one that reads row by row.
    /// </summary>
    /// <remarks>
    /// A run of a hundred thousand rows costs one call through this and a hundred
    /// thousand plain assignments, instead of a hundred thousand calls that each
    /// re-dispatch on the encoding.
    /// </remarks>
    public required string RunCall { get; set; }

    /// <summary>The type the run's value is held in while it is spent over the rows.</summary>
    public required string RunValueType { get; set; }

    /// <summary>
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// <see cref="RunCall"/> opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required string RunSpend { get; set; }

    /// <summary>Which read shape applies.</summary>
    public required string Kind { get; set; }

    /// <summary>The member this column fills, without any element or field access.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The member this column fills, as `.name`, or empty for a column that is not one.
    /// </summary>
    /// <remarks>
    /// The read expressions already have it baked in; this is for the one place that needs
    /// the member on its own - sizing a member that is itself the vector.
    /// </remarks>
    public string MemberAccess { get; set; } = "";

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>
    /// Set where the column is a reference that is a record member, so the sizing knows the
    /// read fills a key vector beside the row one. Empty for everything else.
    /// spec/references-in-records.md.
    /// </summary>
    public string MemberRefSuffix { get; set; } = "";


    /// <summary>How many inner vectors the group has, so a column can size the outer level.</summary>
    public int OuterCount { get; set; }

    /// <summary>How many columns fill this field, where that is a generated shape.</summary>
    public required int ElementCount { get; set; }

    /// <summary>What a reference member starts as, before the linking pass.</summary>
    public required string RefDefault { get; set; }

    /// <summary>The rendered read for a scalar row.</summary>
    public required string ReadScalar { get; set; }

    /// <summary>The rendered read for one element of an array.</summary>
    public required string ReadElement { get; set; }

    /// <summary>The rendered read for one element of a variable-length array.</summary>
    public required string ReadVarElement { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that sizes
    /// the vector when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    /// <summary>The element type of the record group this column belongs to, or empty.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>What an absent row's value is set to, so both read paths agree.</summary>
    public required string EmptyValue { get; set; }
}

