using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything Rust needs, worked out in advance.
///
/// Built whole and then dealt out one subject at a time, so the naming is decided in one
/// place whether or not the file it lands in holds anything else.
/// </summary>
internal sealed class RustFileView
{
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public required string AccessorName { get; set; }

    public required IReadOnlyList<RustEnumView> Enums { get; set; }
    public required IReadOnlyList<RustConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<RustTableView> Tables { get; set; }
    public required RustAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: what it brings into scope, and the single thing it declares.
/// </summary>
internal sealed class RustPartView
{
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public string? AccessorName { get; set; }

    /// <summary>
    /// `use` lines, from <see cref="TypeDependencies"/> and from what the file's own text
    /// reaches for. Exact rather than generous, because an unused one is a warning.
    /// </summary>
    public IReadOnlyList<string>? Uses { get; set; }

    /// <summary>
    /// Lines for an inner doc comment, for the file whose whole contents are the subject.
    /// Empty for the files whose comment attaches to an item instead.
    /// </summary>
    public IReadOnlyList<string>? ModuleDoc { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public RustTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public RustEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public RustConstantSetView? Set { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public RustAccessorView? Accessor { get; set; }
}

internal sealed class RustEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<RustEnumLabelView> Labels { get; set; }
}

internal sealed class RustEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// Whether this label carries the `#[default]` attribute.
    ///
    /// Deriving Default on an enum needs exactly one variant marked, so the choice is
    /// made here rather than left to the template: the zero label when there is one,
    /// and the first otherwise.
    /// </summary>
    public required bool IsDefault { get; set; }
}

internal sealed class RustConstantSetView
{
    public required string ModuleName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<RustConstantView> Constants { get; set; }
}

internal sealed class RustConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class RustTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<RustIndexView> Indexes { get; set; }

    public required IReadOnlyList<RustFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read's match dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields, because declaring a member is per field and reading
    /// is per column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<RustColumnView> Columns { get; set; }

    /// <summary>
    /// Whether any field is a fixed-length record array, and so the read creates those
    /// vectors with the rows.
    /// </summary>
    public required bool NeedsRecordInit { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class RustIndexView
{
    /// <summary>The record member holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The map's key type.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take. `&amp;str` where the map is keyed by `String`, so a
    /// caller with a literal does not have to build one to ask a question.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The key as the map wants it: `key` when already a borrow, `&amp;key` otherwise.</summary>
    public required string KeyBorrow { get; set; }

    /// <summary>The table member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class RustFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The struct's field declarations, `name: type,` each.
    ///
    /// A reference contributes only its index. Resolving it into a borrow of another
    /// record would make the row own its neighbours, which Rust does not allow without
    /// lifetimes through every generated type or a cell around every row; the caller
    /// looks the index up instead.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>Whether this field is a record group, so a struct is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// That struct's name, which carries the table's.
    /// </summary>
    /// <remarks>
    /// The generated modules are re-exported side by side, so two tables each holding a
    /// `Slot` group would collide. Empty for an ordinary field.
    /// </remarks>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Every struct this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<RustRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<RustRecordTypeView>();

    /// <summary>The members of that struct. Empty for an ordinary field.</summary>
    public required IReadOnlyList<RustRecordMemberView> Members { get; set; }

    /// <summary>
    /// Whether this field is a record array whose length is the sheet's column count.
    /// </summary>
    /// <remarks>
    /// Those vectors are created with the rows rather than by whichever member column
    /// arrives first. `#[derive(Default)]` gives an empty one, so the alternative leaves a
    /// file that no longer carries the first member indexing past the end.
    /// </remarks>
    public required bool IsFixedRecordArray { get; set; }

    /// <summary>
    /// Whether this record group is one record whose members are the vectors. They are
    /// sized with the row for the same reason a record array is.
    /// </summary>
    public bool MembersAreArrays { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares `Vec<Vec<T>>` and no element
    /// type - the outer level has no name to be a struct.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>How many inner vectors there are. Zero unless the group is one.</summary>
    public int OuterCount { get; set; }

    /// <summary>The type of one value, for the group whose element is the inner vector.</summary>
    public string ElementTypeName { get; set; } = "";

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
internal sealed class RustRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The whole declaration line, `name: type,`.</summary>
    public required string Declaration { get; set; }

    /// <summary>
    /// Field name on its own, for the shape whose members are the vectors and so have to be
    /// sized with the row.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>The element type to fill that vector with, or empty when it is not one.</summary>
    public required string ElementType { get; set; }

    /// <summary>How long the vector is. Zero when the member is not one.</summary>
    public required int ElementCount { get; set; }
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
internal sealed class RustRecordTypeView
{
    /// <summary>Name of the struct.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of the struct.</summary>
    public required IReadOnlyList<RustRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct belongs to, for its doc comment.</summary>
    public required string Owner { get; set; }
}

/// <summary>
/// One column of a data file, as the read's match sees it.
/// </summary>
internal sealed class RustColumnView
{
    /// <summary>The column wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>
    /// Which read shape applies: `record_var`, `record_serial`, `var_array`, `serial_ref`,
    /// `serial`, `scalar_ref` or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The rendered check_column call for this column.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor construction ahead of the row loop, for a scalar column that can
    /// arrive encoded. Empty for everything that reads the reader directly, and the
    /// template emits nothing - the binding only exists where the reads use it.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>
    /// How an array column's row learns how many elements it holds.
    /// </summary>
    /// <remarks>
    /// From the cursor where the column reads through one, because an encoded array's
    /// lengths are their own stream at the front of the block rather than a number in front
    /// of each row. The cursor answers the same question either way, so this is one
    /// expression and not a branch in the emitted loop.
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
    /// What a reference member's read appends to the member's name, so it lands in the key it
    /// is declared as. Empty for everything else.
    /// </summary>
    /// <remarks>
    /// On the member and before any subscript, because a member that is an array holds one
    /// key per element: `item_id_index[element]`, not `item_id[element]_index`.
    /// spec/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>The record group's element type, for the vector a member column fills.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that
    /// allocates when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    public required int ElementCount { get; set; }

    public required string ReadScalar { get; set; }

    public required string ReadElement { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";
}

internal sealed class RustAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<RustTableSlotView> Tables { get; set; }
}

internal sealed class RustTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}
