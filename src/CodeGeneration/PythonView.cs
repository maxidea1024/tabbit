using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything Python needs, worked out in advance.
///
/// Built whole and then dealt out one subject at a time, so the naming is decided in one
/// place whether or not the file it lands in holds anything else.
/// </summary>
internal sealed class PythonFileView
{
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public required string AccessorName { get; set; }

    public required IReadOnlyList<PythonEnumView> Enums { get; set; }
    public required IReadOnlyList<PythonConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<PythonTableView> Tables { get; set; }
    public required PythonAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: the imports it needs, and the single thing it declares.
/// </summary>
internal sealed class PythonPartView
{
    /// <summary>What the accessor type is called, for the files that name it.</summary>
    public string? AccessorName { get; set; }

    /// <summary>
    /// Relative imports naming the generated types this file uses, from
    /// <see cref="TypeDependencies"/>. The standard library ones every file gets are in the
    /// shared header.
    /// </summary>
    public IReadOnlyList<string>? Imports { get; set; }

    /// <summary>
    /// The module the accessor lives in, for a table file that has to reach the encryption
    /// key it holds. Named rather than assumed because `ModuleName` is the recipe's to pick.
    /// </summary>
    public string? AccessorModule { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public PythonTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public PythonEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public PythonConstantSetView? Set { get; set; }

    /// <summary>The abstract type this module declares, when it declares one.</summary>
    public PythonPolymorphicTypeView? Structure { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public PythonAccessorView? Accessor { get; set; }
}

internal sealed class PythonEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<PythonEnumLabelView> Labels { get; set; }

    /// <summary>
    /// The value an undeclared one falls back to: the zero label when there is one, and
    /// the first otherwise.
    /// </summary>
    public required string DefaultValue { get; set; }
}

internal sealed class PythonEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class PythonConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<PythonConstantView> Constants { get; set; }
}

internal sealed class PythonConstantView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class PythonTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<PythonIndexView> Indexes { get; set; }

    /// <summary>
    /// The table class's `__slots__`: the rows and one map per index, already quoted
    /// and comma separated.
    /// </summary>
    public required string TableSlotNames { get; set; }


    /// <summary>
    /// The `__slots__` tuple's contents, already quoted and comma separated.
    ///
    /// Slots rather than a plain class: a table is tens of thousands of rows and a
    /// per-instance dictionary on each is the difference between tens of megabytes and
    /// a few.
    /// </summary>
    public required string SlotNames { get; set; }

    /// <summary>Format string for `__repr__`.</summary>
    public required string ReprFormat { get; set; }

    /// <summary>Values for `__repr__`, comma separated.</summary>
    public required string ReprValues { get; set; }

    public required IReadOnlyList<PythonFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read's tag chain dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields, because declaring an attribute is per field and
    /// reading is per column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<PythonColumnView> Columns { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class PythonIndexView
{
    /// <summary>The record attribute holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `index` gives `find_by_index`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The table attribute holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
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

internal sealed class PythonFieldView
{
    /// <summary>
    /// How the builder reaches the entry it reads from: the row's own member, or the element it
    /// was handed.
    /// </summary>
    /// <remarks>
    /// One name for both shapes, so the body that fills a variant is written once. A
    /// polymorphic array's builder takes an element; a scalar group's reads the row.
    /// spec/polymorphism.md section 5.3.
    /// </remarks>
    public string EntryAccess { get; set; } = "";

    /// <summary>
    /// Whether the polymorphic group is an array, so each element carries its own variant.
    /// </summary>
    /// <remarks>
    /// Asked rather than worked out from the read kind, which is spelled differently in every
    /// generator - and the two shapes differ in more than one line: the accessor loops, and
    /// the value it hands back is an array of the abstract type.
    /// spec/polymorphism.md section 5.3.
    /// </remarks>
    public bool VariantsAreArray { get; set; }

    /// <summary>
    /// The variants of a polymorphic group, or empty when the group is one fixed shape.
    /// </summary>
    /// <remarks>
    /// The type itself is declared once, elsewhere; this is what the table needs to build a
    /// value of it - which number means which variant, and which of the entry's fields each
    /// one carries. spec/polymorphism.md sections 7.1 and 7.2.
    /// </remarks>
    public IReadOnlyList<PythonVariantView> Variants { get; set; } = new List<PythonVariantView>();

    /// <summary>The abstract type the variants make up, or empty.</summary>
    public string AbstractTypeName { get; set; } = "";

    /// <summary>
    /// What the discriminator member is called on the flat entry.
    /// </summary>
    /// <remarks>
    /// Asked rather than assumed: `type` is a keyword in several of these languages, so the
    /// name-escaping rule has already renamed it and a template spelling it by hand would be
    /// spelling a field that is not there.
    /// </remarks>
    public string DiscriminatorName { get; set; } = "";

    /// <summary>The members every variant carries.</summary>
    public IReadOnlyList<PythonStructMemberView> BaseMembers { get; set; }
        = new List<PythonStructMemberView>();

    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The assignments the constructor makes, so that a record is fully formed before
    /// it is read into. Two for a reference, which keeps the raw index beside the value.
    /// </summary>
    public required IReadOnlyList<string> Initializers { get; set; }

    /// <summary>Whether this field is a record group, so a class is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which needs no element type - the outer
    /// level has no name to declare one for.
    /// </summary>
    /// <remarks>See spec/nested-multi-level.md.</remarks>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>
    /// That class's name, which carries the table's - the package re-exports every
    /// generated name side by side. Empty for an ordinary field.
    /// </summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The attributes of that class. Empty for an ordinary field.</summary>
    public required IReadOnlyList<PythonRecordMemberView> Members { get; set; }

    /// <summary>That class's `__slots__`, already quoted and comma separated.</summary>
    public required string RecordSlotNames { get; set; }

    /// <summary>Format string for that class's `__repr__`.</summary>
    public required string RecordReprFormat { get; set; }

    /// <summary>Values for that class's `__repr__`, comma separated.</summary>
    public required string RecordReprValues { get; set; }

    /// <summary>
    /// Every class this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<PythonRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<PythonRecordTypeView>();

    /// <summary>Whether the sheet marked this field optional, so a row may have no value.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The attribute the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The attribute holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";
}

/// <summary>One attribute of a record group's generated class.</summary>
internal sealed class PythonRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The constructor assignment, `self.x = 0.0`.</summary>
    public required IReadOnlyList<string> Initializers { get; set; }

}


/// <summary>
/// One generated class of a record group - the group's own element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax.
///
/// Innermost first, and here that is required rather than tidy - a class body naming another
/// runs at import time, so the one it names has to exist already.
/// spec/nested-multi-level.md.
/// </remarks>
internal sealed class PythonRecordTypeView
{
    /// <summary>Name of the class.</summary>
    public required string TypeName { get; set; }

    /// <summary>The constructor's assignments.</summary>
    public required IReadOnlyList<PythonRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the class belongs to, for its docstring.</summary>
    public required string Owner { get; set; }


    /// <summary>Its `__slots__` tuple.</summary>
    public required string SlotNames { get; set; }

    /// <summary>The `%`-format its `__repr__` uses.</summary>
    public required string ReprFormat { get; set; }

    /// <summary>The values that format is applied to.</summary>
    public required string ReprValues { get; set; }
}

/// <summary>
/// One column of a data file, as the read's tag chain sees it.
/// </summary>
internal sealed class PythonColumnView
{
    /// <summary>Where the keys read off the wire go, which is the column's own name unless
    /// the reference is dotted. spec/reference-surface-naming.md sections 4 and 9.</summary>
    public string KeyName { get; set; } = "";

    /// <summary>
    /// Where the resolved rows go for a whole-row reference, or the column's own name for a
    /// dotted one. spec/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    /// <summary>The column's wire tag.</summary>
    public required int WireTag { get; set; }

    /// <summary>
    /// Which read shape applies: `record_var`, `record_serial`, `var_array`, `serial_ref`,
    /// `serial`, `scalar_ref` or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The rendered check_column call for this column.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or empty for a
    /// column that reads the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

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
    /// The line assigning one row from the value the run decoded, inside the loop
    /// <see cref="RunCall"/> opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required string RunSpend { get; set; }

    /// <summary>The attribute this column fills, without any element or member access.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The attribute of the element type this column fills, with a leading dot, or empty
    /// when the column is not a record member.
    /// </summary>
    public required string MemberAccess { get; set; }
    /// <summary>
    /// What a reference member's read appends to the member's name, so it lands in the key
    /// rather than in the row that key will resolve to. Empty for everything else.
    /// </summary>
    /// <remarks>
    /// On the member and before any subscript, because a member that is an array holds one
    /// key per element. spec/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>The record group's element class, which the read constructs.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that
    /// builds the list when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    public required int ElementCount { get; set; }

    public required string ReadScalar { get; set; }

    public required string ReadElement { get; set; }

    /// <summary>
    /// How an array column's row learns how many elements it holds.
    /// </summary>
    /// <remarks>
    /// From the cursor where the column reads through one, because an encoded array's
    /// lengths are their own stream at the front of the block rather than a number in
    /// front of each row. The cursor answers the same call either way, so this is one line
    /// and not a branch in the emitted loop.
    /// </remarks>
    public required string LengthRead { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The attribute the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The attribute holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>What an absent row's value is put back to, so both read paths agree.</summary>
    public required string EmptyValue { get; set; }
}

internal sealed class PythonAccessorView
{
    public required string FileExtension { get; set; }

    /// <summary>The accessor's `__slots__` contents, already quoted and comma separated.</summary>
    public required string SlotNames { get; set; }

    public required IReadOnlyList<PythonTableSlotView> Tables { get; set; }
    public required IReadOnlyList<PythonCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class PythonTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class PythonCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<PythonReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<PythonRecordReferenceView> RecordFields { get; set; }


}


/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class PythonRecordReferenceView
{
    /// <summary>The resolved row this writes, loop variable included.</summary>
    public required string Access { get; set; }

    /// <summary>The stored key it resolves through.</summary>
    public required string Key { get; set; }

    /// <summary>
    /// What the loop walks, or empty where the group is one record and there is nothing
    /// to walk.
    /// </summary>
    public required string Range { get; set; }

    public required string RefTable { get; set; }
    public required string RefLookup { get; set; }
}

internal sealed class PythonReferenceFieldView
{
    /// <summary>
    /// Where the resolved row goes - the derived name for a whole-row reference, the
    /// column's own name for a dotted one.
    /// spec/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}

/// <summary>
/// An abstract type and its variants, as this target declares them.
/// </summary>
/// <remarks>
/// One per declaration however many tables named it. A struct is an entity beside a table and
/// an enum, and emitting it inside each table that used it would give them types that share a
/// name and are not the same type. spec/polymorphism.md section 7.1.
/// </remarks>
internal sealed class PythonPolymorphicTypeView
{
    /// <summary>The abstract type's name.</summary>
    public required string Name { get; set; }

    /// <summary>The module the type lives in, which this language spells in snake case.</summary>
    public required string ModuleName { get; set; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required IReadOnlyList<PythonStructMemberView> BaseMembers { get; set; }

    /// <summary>What one of its values may be.</summary>
    public required IReadOnlyList<PythonVariantView> Variants { get; set; }
}

/// <summary>One variant of a <see cref="PythonPolymorphicTypeView"/>.</summary>
internal sealed class PythonVariantView
{
    /// <summary>The variant's declared name - the type a consumer narrows to.</summary>
    public required string TypeName { get; set; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; set; }

    /// <summary>The members this variant declares, beside the base ones.</summary>
    public required IReadOnlyList<PythonStructMemberView> Members { get; set; }
}

/// <summary>One member of an abstract type or of one of its variants.</summary>
/// <remarks>
/// A view of its own rather than the record member's, because none of the reference machinery
/// applies: the model refuses a reference inside a polymorphic group.
/// </remarks>
internal sealed class PythonStructMemberView
{
    /// <summary>
    /// Where the row a reference member resolved to goes, or empty when the member is a value.
    /// </summary>
    /// <remarks>
    /// **A reference member is two fields here, the same two a reference column is anywhere.**
    /// The declared name is the key's - that is what the cell holds - and the row it resolves to
    /// takes the derived one. spec/reference-surface-naming.md sections 4 and 5.
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
