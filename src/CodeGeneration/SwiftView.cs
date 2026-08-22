using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>Everything the Swift template needs, worked out in advance.</summary>
internal sealed class SwiftFileView
{
    /// <summary>Name of the accessor class.</summary>
    public required string AccessorName { get; set; }

    public required IReadOnlyList<SwiftEnumView> Enums { get; set; }
    public required IReadOnlyList<SwiftConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<SwiftTableView> Tables { get; set; }
    public required SwiftAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// The output is a file per table, per enum and per constant set. Handing each template only
/// what it is for means it cannot reach a table it is not writing.
///
/// No package name, unlike the Kotlin and Java views: a Swift file declares nothing about
/// where it lives. The module is the directory the build system points at, which is the
/// consumer's decision and not something to write into the file.
/// </remarks>
internal sealed class SwiftPartView
{
    /// <summary>The accessor's type name, for the doc comments that point at it.</summary>
    public string? AccessorName { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public SwiftTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public SwiftEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public SwiftConstantSetView? Set { get; set; }
}

internal sealed class SwiftEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<SwiftEnumLabelView> Labels { get; set; }

    /// <summary>Label an undeclared value falls back to.</summary>
    public required string DefaultLabel { get; set; }
}

internal sealed class SwiftEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class SwiftConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<SwiftConstantView> Constants { get; set; }
}

internal sealed class SwiftConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class SwiftTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The columns whose value is a row of one of several tables.
    /// spec/multi-target-accessors.md.
    /// </summary>
    public required IReadOnlyList<SwiftMultiReferenceView> MultiReferences { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<SwiftIndexView> Indexes { get; set; }

    public required IReadOnlyList<SwiftFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read's `switch` dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields, because declaring a property is per field and reading
    /// is per column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<SwiftColumnView> Columns { get; set; }
}

/// <summary>One indexed field, and the lookups generated for it.</summary>
internal sealed class SwiftIndexView
{
    /// <summary>The record property holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type.</summary>
    public required string KeyType { get; set; }

    /// <summary>The property holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the thrown error's message.</summary>
    public required string FieldName { get; set; }
}

internal sealed class SwiftFieldView
{
    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The property declarations, each with an initializer.
    /// </summary>
    /// <remarks>
    /// Initialized rather than optional, which is the shape Swift would suggest: a caller
    /// reading a value should not answer for a row the read never reached, and an optional
    /// would move that question to every value in the table.
    /// spec/optional-fields.md · spec/swift-language-support.md.
    /// </remarks>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>Whether this field is a record group, so a struct is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares a nested array and no
    /// element type - the outer level has no name to declare one for.
    /// </summary>
    /// <remarks>See spec/nested-multi-level.md.</remarks>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>
    /// That struct's name, unqualified: it is nested in the record, which is what scopes it.
    /// Empty for an ordinary field.
    /// </summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The properties of that struct. Empty for an ordinary field.</summary>
    public required IReadOnlyList<SwiftRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every struct this group declares, innermost first. One entry for a record whose
    /// members are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<SwiftRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<SwiftRecordTypeView>();

    /// <summary>Whether the sheet marked this field optional, so a row may have no value.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The property the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";
}

/// <summary>One property of a record group's generated struct.</summary>
internal sealed class SwiftRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The whole declaration line, name, type and initializer.</summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// The slot and the discriminator of a member reaching several tables, so the accessors can
    /// be written on the element struct. Null for every other member.
    /// spec/multi-target-accessors.md.
    /// </summary>
    public SwiftMultiMemberView? Multi { get; set; }
}

/// <summary>
/// One record member whose value is a row of one of several tables.
/// </summary>
/// <remarks>
/// The member keeps the key it already carried; beside it go one slot for the resolved row and
/// the discriminator saying which table filled it, at the member's own arity. `AnyObject?` for
/// the slot, as the row-level shape has it - the target records share no protocol, and the
/// conditional cast back out sits in the generated accessor where the discriminator has already
/// answered. spec/multi-target-accessors.md.
/// </remarks>
internal sealed class SwiftMultiMemberView
{
    /// <summary>The key, the slot and the discriminator, by property name.</summary>
    public required string KeyMember { get; set; }
    public required string SlotMember { get; set; }
    public required string TargetMember { get; set; }

    /// <summary>The generated enumeration's type name, and its `None` label.</summary>
    public required string TargetTypeName { get; set; }
    public required string NoneLabel { get; set; }

    /// <summary>Whether the member is the array, so the accessor takes an element number.</summary>
    public required bool IsArray { get; set; }

    public required IReadOnlyList<SwiftMultiTargetView> Targets { get; set; }
}

/// <summary>
/// One generated struct of a record group - the group's own element type, or a level below
/// it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree, for the same reason the other targets keep one: the
/// recursion belongs in the view, so no template has to reason about depth in template
/// syntax. Innermost first. spec/nested-multi-level.md.
/// </remarks>
internal sealed class SwiftRecordTypeView
{
    /// <summary>Name of the struct.</summary>
    public required string TypeName { get; set; }

    /// <summary>Properties of the struct.</summary>
    public required IReadOnlyList<SwiftRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct belongs to, for its doc comment.</summary>
    public required string Owner { get; set; }

    /// <summary>
    /// Those of its members that reach several tables, for the accessors written on the struct.
    /// spec/multi-target-accessors.md.
    /// </summary>
    public IReadOnlyList<SwiftMultiMemberView> MultiMembers { get; set; }
        = System.Array.Empty<SwiftMultiMemberView>();
}

/// <summary>One column of a data file, as the read's `switch` sees it.</summary>
internal sealed class SwiftColumnView
{
    /// <summary>The column wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>
    /// Which read shape applies: `record_var`, `record_serial`, `var_array`, `serial_ref`,
    /// `serial`, `scalar_ref` or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The rendered checkColumn call for this column.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>How an array column's row learns how many elements it holds.</summary>
    public required string LengthRead { get; set; }

    /// <summary>
    /// The cursor's run method for a scalar whose column can arrive run-length encoded,
    /// or empty for one that reads row by row.
    /// </summary>
    public required string RunCall { get; set; }

    /// <summary>
    /// The line assigning one row from the value the run decoded, inside the loop
    /// <see cref="RunCall"/> opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required string RunSpend { get; set; }

    /// <summary>The property this column fills, without any element or member access.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The property of the element type this column fills, with a leading dot, or empty when
    /// the column is not a record member.
    /// </summary>
    public required string MemberAccess { get; set; }

    /// <summary>
    /// What a reference member's read appends to the member's name, so it lands in the key
    /// rather than in the row that key will resolve to. Empty for everything else.
    /// </summary>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>
    /// The record group's element struct, qualified by the record it is nested in - this is
    /// named from the table class beside it.
    /// </summary>
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

    /// <summary>The property the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>What an absent row's value is put back to, so both read paths agree.</summary>
    public required string EmptyValue { get; set; }
}

internal sealed class SwiftAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<SwiftTableSlotView> Tables { get; set; }
    public required IReadOnlyList<SwiftCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class SwiftTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class SwiftCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<SwiftReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<SwiftRecordReferenceView> RecordFields { get; set; }

    /// <summary>
    /// The columns reaching several tables, which resolve by trying each in turn.
    /// spec/multi-target-accessors.md.
    /// </summary>
    public required IReadOnlyList<SwiftMultiReferenceView> MultiFields { get; set; }

    /// <summary>
    /// The columns reaching several tables that are members of a record, which resolve per
    /// element. spec/multi-target-accessors.md.
    /// </summary>
    public required IReadOnlyList<SwiftMultiRecordReferenceView> MultiRecordFields { get; set; }
}

/// <summary>One reference that is a member of a record, as the linking pass writes it.</summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class SwiftRecordReferenceView
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
    public required string RefLookup { get; set; }
}

internal sealed class SwiftReferenceFieldView
{
    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}

/// <summary>
/// One column whose value is a row of one of several tables.
/// </summary>
/// <remarks>
/// One property for the resolved row whatever table it came from, and the discriminator
/// saying which. `AnyObject?` for the slot, because the target records share no supertype -
/// the property below casts it, having asked the discriminator first.
/// spec/multi-target-accessors.md.
/// </remarks>
internal sealed class SwiftMultiReferenceView
{
    /// <summary>The property holding the key.</summary>
    public required string KeyMember { get; set; }

    /// <summary>The property the resolved row lands in, and the discriminator beside it.</summary>
    public required string SlotMember { get; set; }
    public required string TargetMember { get; set; }

    /// <summary>The generated enumeration's type name.</summary>
    public required string TargetTypeName { get; set; }

    /// <summary>The label standing for "no row of any of them".</summary>
    public required string NoneLabel { get; set; }

    /// <summary>What follows the key to ask whether it points anywhere.</summary>
    public required string KeyIsSet { get; set; }

    public required IReadOnlyList<SwiftMultiTargetView> Targets { get; set; }
}

/// <summary>One table a multi-target column may point at.</summary>
internal sealed class SwiftMultiTargetView
{
    /// <summary>The accessor's local name for the table.</summary>
    public required string Table { get; set; }

    /// <summary>The record type a resolved row has.</summary>
    public required string RecordName { get; set; }

    /// <summary>The member this target is read through.</summary>
    public required string Method { get; set; }

    /// <summary>The enum label for this target.</summary>
    public required string Label { get; set; }

    /// <summary>The target's lookup, which answers nil rather than throwing.</summary>
    public required string Lookup { get; set; }
}

/// <summary>
/// One multi-target column that is a member of a record, as the linking pass writes it.
/// </summary>
internal sealed class SwiftMultiRecordReferenceView
{
    /// <summary>The key this resolves through, loop variable included.</summary>
    public required string Key { get; set; }

    /// <summary>The slot the resolved row lands in, and the discriminator beside it.</summary>
    public required string Slot { get; set; }
    public required string Target { get; set; }

    /// <summary>
    /// The loop bound, or empty where the group is one record and there is nothing to walk.
    /// </summary>
    public required string Count { get; set; }

    /// <summary>The generated enumeration's type name, and its `None` label.</summary>
    public required string TargetTypeName { get; set; }
    public required string NoneLabel { get; set; }

    /// <summary>What follows the key to ask whether it points anywhere.</summary>
    public required string KeyIsSet { get; set; }

    public required IReadOnlyList<SwiftMultiTargetView> Targets { get; set; }
}
