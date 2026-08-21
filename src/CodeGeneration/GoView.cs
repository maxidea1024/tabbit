using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything the Go template needs, worked out in advance.
/// </summary>
internal sealed class GoFileView
{
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public required string AccessorName { get; set; }

    public required string PackageName { get; set; }

    public required IReadOnlyList<GoEnumView> Enums { get; set; }
    public required IReadOnlyList<GoConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<GoTableView> Tables { get; set; }
    public required GoAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// Carries its own imports, because an unused one does not compile in Go - every other
/// language here could hand each file the same list.
/// </remarks>
internal sealed class GoPartView
{
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public string? AccessorName { get; set; }

    public string? PackageName { get; set; }

    /// <summary>Import lines, already quoted, with a blank entry where gofmt wants a gap.</summary>
    public IReadOnlyList<string>? Imports { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public GoTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public GoEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public GoConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public GoAccessorView? Accessor { get; set; }
}

internal sealed class GoEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<GoEnumLabelView> Labels { get; set; }
}

internal sealed class GoEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class GoConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<GoConstantView> Constants { get; set; }
}

internal sealed class GoConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class GoTableView
{
    /// <summary>Table name as the sheet spelled it, used in the data file name.</summary>
    public required string RawName { get; set; }

    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<GoIndexView> Indexes { get; set; }

    public required IReadOnlyList<GoFieldView> Fields { get; set; }

    /// <summary>
    /// The columns whose value is a row of one of several tables.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Fields"/>: such a column is not one record and keeps carrying
    /// the key, and what is added beside it is a method per target.
    /// spec/multi-target-accessors.md.
    /// </remarks>
    public required IReadOnlyList<GoMultiReferenceView> MultiReferences { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields, because declaring a member is per field and reading
    /// is per column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<GoColumnView> Columns { get; set; }

    /// <summary>
    /// Whether any field is a fixed-length record array, and so the read creates those
    /// arrays with the rows.
    /// </summary>
    public required bool NeedsRecordInit { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class GoIndexView
{
    /// <summary>The record member holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `FindByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's Go type.</summary>
    public required string KeyType { get; set; }

    /// <summary>The table member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
    public required string FieldName { get; set; }
}

/// <summary>
/// One member of the record struct: what is declared, and nothing about reading it.
/// </summary>
internal sealed class GoFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The member declarations. Two for a reference, which keeps the raw index beside
    /// the resolved value.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>Whether this field is a record group, so a struct is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// That struct's name, which carries the table's.
    /// </summary>
    /// <remarks>
    /// A Go directory is a package and this output is one directory, so every generated
    /// type shares a namespace - two tables each holding a `Slot` group would be the same
    /// name declared twice. Empty for an ordinary field.
    /// </remarks>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Every struct this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<GoRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<GoRecordTypeView>();

    /// <summary>The members of that struct. Empty for an ordinary field.</summary>
    public required IReadOnlyList<GoRecordMemberView> Members { get; set; }

    /// <summary>
    /// Whether this field is a record array whose length is the sheet's column count.
    /// </summary>
    /// <remarks>
    /// Those arrays are created with the rows rather than by whichever member column arrives
    /// first. A Go struct has no field initializer, so the alternative is a slice that stays
    /// nil until the first member is read - and a file that no longer carries that member
    /// would leave the ones after it indexing into nothing.
    /// </remarks>
    public required bool IsFixedRecordArray { get; set; }

    /// <summary>
    /// Whether this record group is one record whose members are the arrays. Their slices
    /// are made with the row for the same reason a record array's is.
    /// </summary>
    public bool MembersAreArrays { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares `[][]T` and generates no
    /// element type - there is no name for the outer level.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>How many inner slices there are. Zero unless the group is one.</summary>
    public int OuterCount { get; set; }

    /// <summary>The type of one value, for the group whose element is the inner slice.</summary>
    public string ElementTypeName { get; set; } = "";

    /// <summary>The slice type that array's make call needs. Empty for everything else.</summary>
    public required string ArrayType { get; set; }

    /// <summary>How long it is. Zero for everything else.</summary>
    public required int ElementCount { get; set; }

    /// <summary>Whether the sheet marked this field optional, so a row may have no value.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";
}

/// <summary>One member of a record group's generated struct.</summary>
internal sealed class GoRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The declaration lines, name and type.
    ///
    /// More than one for a reference member, which holds the row it resolved to as well as
    /// the key that came off the wire. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// Field name on its own, for the shape whose members are the arrays and so have to be
    /// made with the row.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>The slice type to make, or empty when this member is not an array.</summary>
    public required string SliceType { get; set; }

    /// <summary>How long that slice is. Zero when the member is not an array.</summary>
    public required int ElementCount { get; set; }

    /// <summary>
    /// The slice type of the stored keys, for a reference member that is itself the array -
    /// one key per element, exactly as there is one row per element. Empty otherwise.
    /// spec/references-in-records.md.
    /// </summary>
    public string RefKeySliceType { get; set; } = "";

    /// <summary>
    /// The slot and the discriminator of a member reaching several tables, so the methods can
    /// be written beside the struct. Empty for every other member.
    /// spec/multi-target-accessors.md.
    /// </summary>
    public GoMultiMemberView? Multi { get; set; }
}

/// <summary>
/// One record member whose value is a row of one of several tables.
/// </summary>
/// <remarks>
/// The member keeps the key it already carried; beside it go one slot for the resolved row and
/// the discriminator saying which table filled it, at the member's own arity. `any` for the
/// slot, for the reason the row-level view gives. spec/multi-target-accessors.md.
/// </remarks>
internal sealed class GoMultiMemberView
{
    /// <summary>The struct the methods hang off.</summary>
    public required string ElementTypeName { get; set; }

    /// <summary>The key, the slot and the discriminator, by member name.</summary>
    public required string KeyMember { get; set; }
    public required string SlotMember { get; set; }
    public required string TargetMember { get; set; }

    /// <summary>The generated enumeration's type name.</summary>
    public required string TargetTypeName { get; set; }

    /// <summary>Whether the member is the array, so a method takes an element number.</summary>
    public required bool IsArray { get; set; }

    public required IReadOnlyList<GoMultiTargetView> Targets { get; set; }
}

/// <summary>
/// One generated struct of a record group - the group's own element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax. Innermost first.
/// spec/nested-multi-level.md.
/// </remarks>
internal sealed class GoRecordTypeView
{
    /// <summary>Name of the struct.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of the struct.</summary>
    public required IReadOnlyList<GoRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct is called in its doc comment.</summary>
    public required string Owner { get; set; }

    /// <summary>
    /// Those of its members that reach several tables, for the methods written beside the
    /// struct. spec/multi-target-accessors.md.
    /// </summary>
    public IReadOnlyList<GoMultiMemberView> MultiMembers { get; set; }
        = System.Array.Empty<GoMultiMemberView>();
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
internal sealed class GoColumnView
{
    /// <summary>The column's wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>
    /// Which read shape applies: `record_var`, `record_serial`, `var_array`, `serial_ref`,
    /// `serial`, `scalar_ref` or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The rendered CheckColumn call for this column.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
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
    /// The Go spelling of the key a reference array stores, for the read to allocate.
    /// </summary>
    /// <remarks>
    /// Written into the template as `int32` before, which is the assumption
    /// spec/reference-key-types.md removed everywhere a scalar reference reads and left
    /// standing where an array of them allocates. Empty for a column that is not one.
    /// </remarks>
    public required string RefKeyType { get; set; }

    /// <summary>
    /// The cursor's run method for a scalar whose column can arrive run-length encoded -
    /// `NextSameI32` or `NextSameString` - or empty for one that reads row by row.
    /// </summary>
    /// <remarks>
    /// A run of a hundred thousand rows costs one call through this and a hundred
    /// thousand plain assignments, instead of a hundred thousand calls that each
    /// re-dispatch on the encoding.
    /// </remarks>
    public required string RunCall { get; set; }

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

    /// <summary>
    /// Which member of the group this column is. What an unnamed outer level is indexed by,
    /// where a named one uses <see cref="MemberAccess"/>.
    /// </summary>
    public int MemberAt { get; set; }

    /// <summary>
    /// What a reference member's read appends to the member's name, so it lands in the key
    /// rather than in the row that key will resolve to. Empty for everything else.
    /// </summary>
    /// <remarks>
    /// On the member and before any subscript, because a member that is an array holds one
    /// key per element: `ItemIdIndex[j]`, not `ItemId[j]Index`.
    /// spec/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Element count of a fixed array, which is its column count.</summary>
    public required int ElementCount { get; set; }

    /// <summary>The slice type a make call needs.</summary>
    public required string ArrayType { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that
    /// allocates when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    /// <summary>The read call for one value, whether it is a row's or an array element's.</summary>
    public required string ReadValue { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>What an absent row's value is put back to, so both read paths agree.</summary>
    public required string EmptyValue { get; set; }
}

internal sealed class GoAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<GoTableSlotView> Tables { get; set; }
    public required IReadOnlyList<GoCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class GoTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class GoCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<GoReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<GoRecordReferenceView> RecordFields { get; set; }

    /// <summary>
    /// The columns reaching several tables, which resolve by trying each in turn.
    /// spec/multi-target-accessors.md.
    /// </summary>
    public required IReadOnlyList<GoMultiReferenceView> MultiFields { get; set; }

    /// <summary>
    /// The columns reaching several tables that are members of a record, which resolve per
    /// element. spec/multi-target-accessors.md.
    /// </summary>
    public required IReadOnlyList<GoMultiRecordReferenceView> MultiRecordFields { get; set; }
}

/// <summary>
/// One multi-target column that is a member of a record, as the linking pass writes it.
/// </summary>
internal sealed class GoMultiRecordReferenceView
{
    /// <summary>The key this resolves through, loop variable included.</summary>
    public required string Key { get; set; }

    /// <summary>The slot the resolved row lands in, and the discriminator beside it.</summary>
    public required string Slot { get; set; }
    public required string Target { get; set; }

    /// <summary>
    /// What the loop ranges over, or empty where the group is one record and there is nothing
    /// to walk.
    /// </summary>
    public required string Range { get; set; }

    /// <summary>The generated enumeration's type name.</summary>
    public required string TargetTypeName { get; set; }

    /// <summary>What follows the key to ask whether it points anywhere.</summary>
    public required string KeyIsSet { get; set; }

    public required IReadOnlyList<GoMultiTargetView> Targets { get; set; }
}

/// <summary>
/// One column whose value is a row of one of several tables.
/// </summary>
/// <remarks>
/// The key stays the column's value. Beside it: one slot for the resolved row whatever table
/// it came from, and the discriminator saying which. `any` for the slot, because the target
/// records share no interface and giving them one would be a sum type - the assertion back out
/// lives in the generated method, where the discriminator has already answered.
/// spec/multi-target-accessors.md.
/// </remarks>
internal sealed class GoMultiReferenceView
{
    /// <summary>The member holding the key.</summary>
    public required string KeyMember { get; set; }

    /// <summary>The slot the resolved row lands in, and the discriminator beside it.</summary>
    public required string SlotMember { get; set; }
    public required string TargetMember { get; set; }

    /// <summary>The generated enumeration's type name.</summary>
    public required string TargetTypeName { get; set; }

    /// <summary>What follows the key to ask whether it points anywhere.</summary>
    public required string KeyIsSet { get; set; }

    public required IReadOnlyList<GoMultiTargetView> Targets { get; set; }
}

/// <summary>One table a multi-target column may point at.</summary>
internal sealed class GoMultiTargetView
{
    /// <summary>The accessor member holding the table.</summary>
    public required string Table { get; set; }

    /// <summary>The record type a resolved row has.</summary>
    public required string RecordName { get; set; }

    /// <summary>The method this target is read through.</summary>
    public required string Method { get; set; }

    /// <summary>The enum constant for this target, already carrying the type's name.</summary>
    public required string Constant { get; set; }

    /// <summary>The target's lookup, which answers nil rather than an error.</summary>
    public required string Lookup { get; set; }
}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class GoRecordReferenceView
{
    /// <summary>The resolved row this writes, loop variable included.</summary>
    public required string Access { get; set; }

    /// <summary>The stored key it resolves through.</summary>
    public required string Key { get; set; }

    /// <summary>
    /// What the loop ranges over, or empty where the group is one record and there is
    /// nothing to walk.
    /// </summary>
    public required string Range { get; set; }

    public required string RefTable { get; set; }
    public required string RefLookup { get; set; }
}

internal sealed class GoReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}
