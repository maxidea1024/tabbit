using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything Java needs, worked out in advance.
///
/// Built whole and then dealt out one subject at a time, so the naming is decided in one
/// place whether or not the file it lands in holds anything else.
/// </summary>
internal sealed class JavaFileView
{
    public required string PackageName { get; set; }

    /// <summary>Name of the accessor class, and so of its file.</summary>
    public required string AccessorName { get; set; }

    public required IReadOnlyList<JavaEnumView> Enums { get; set; }
    public required IReadOnlyList<JavaConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<JavaTableView> Tables { get; set; }
    public required JavaAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: the package it declares, its imports, and the single type in it.
/// </summary>
internal sealed class JavaPartView
{
    public string? PackageName { get; set; }

    /// <summary>
    /// The accessor's class name: for the file named after it, and for a table file, which
    /// reaches the accessor for the encryption key it holds.
    /// </summary>
    public string? AccessorName { get; set; }

    /// <summary>
    /// Import lines, with a blank entry where Java convention wants a gap. Nothing here ever
    /// imports another generated type: they are all one package.
    /// </summary>
    public IReadOnlyList<string>? Imports { get; set; }

    /// <summary>
    /// The table this file is for, when it is a record file or a table file. Both are
    /// rendered from the same view, since both are named from it.
    /// </summary>
    public JavaTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public JavaEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public JavaConstantSetView? Set { get; set; }

    /// <summary>The abstract type this file declares, when it declares one.</summary>
    public JavaPolymorphicTypeView? Structure { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public JavaAccessorView? Accessor { get; set; }
}

internal sealed class JavaEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<JavaEnumLabelView> Labels { get; set; }

    /// <summary>Label an undeclared value falls back to.</summary>
    public required string DefaultLabel { get; set; }
}

internal sealed class JavaEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// What follows the constant: a comma, or the semicolon that ends the list. Decided
    /// here because Java's enum body needs one and not the other.
    /// </summary>
    public required string Separator { get; set; }
}

internal sealed class JavaConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<JavaConstantView> Constants { get; set; }
}

internal sealed class JavaConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

/// <summary>A grid's accessor, spelled the way Java spells it.</summary>
/// <remarks>
/// The same pieces every language's grid view holds. <see cref="MatrixPlan"/> decides the
/// shape; this is its spelling. spec/layout/matrix-declaration.md.
/// </remarks>
internal sealed class JavaMatrixView
{
    public required string ColumnTable { get; init; }

    public required string ColumnTableName { get; init; }

    /// <summary>The column table's record class.</summary>
    public required string ColumnRecord { get; init; }

    /// <summary>The lookup on the column table that the column axis key goes through.</summary>
    public required string ColumnLookup { get; init; }

    public required string RowKeyMember { get; init; }

    public required string RowKeyParam { get; init; }

    public required string RowKeyType { get; init; }

    /// <summary>The same type where a generic needs a reference - `List&lt;Integer&gt;`.</summary>
    public required string RowKeyBoxed { get; init; }

    public required string RowLookup { get; init; }

    public required string ColumnKeyMember { get; init; }

    public required string ColumnKeyParam { get; init; }

    public required string ColumnKeyType { get; init; }

    public required string ColumnKeyBoxed { get; init; }

    public required string AtMember { get; init; }

    public required string GridMember { get; init; }

    public required string GridHasMember { get; init; }

    public required string CellType { get; init; }

    public required bool CellsAreOptional { get; init; }
}

internal sealed class JavaTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }


    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<JavaIndexView> Indexes { get; set; }

    /// <summary>The grid this table holds the values of, or null when it is not one.</summary>
    public JavaMatrixView? Matrix { get; set; }

    /// <summary>
    /// The statements filling every `set` and `map` lookup in the table, ready to paste.
    /// </summary>
    /// <remarks>
    /// Written out here rather than shaped in the template, which is how a record member's
    /// own declaration already reaches it. Filled once every column is in: a map needs its
    /// key column and how long it is, and the columns arrive one at a time.
    /// spec/types/set-and-map.md section 7.3.
    /// </remarks>
    public IReadOnlyList<string> ContainerFill { get; set; } = System.Array.Empty<string>();

    /// <summary>Whether any column reads through the cursor, and so whether the read
    /// method declares its one cursor variable.</summary>
    public required bool NeedsCursor { get; set; }

    /// <summary>Whether any column is optional, and so the read declares the presence buffer.</summary>
    public required bool NeedsPresence { get; set; }

    /// <summary>Whether any column of this table carries an element bitmap.</summary>
    public bool NeedsElementPresence { get; set; }

    public required IReadOnlyList<JavaFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields, because declaring a member is per field and reading
    /// is per column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<JavaColumnView> Columns { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class JavaIndexView
{
    /// <summary>The record field holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The map's key type, boxed where the field is a primitive.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take, which is the field's own - a caller passing an `int`
    /// should not have to think about the box the map needs.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The field holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the exception message.</summary>
    public required string FieldName { get; set; }
    /// <summary>Whether the key is several columns taken together.</summary>
    public required bool IsComposite { get; set; }

    /// <summary>Whether this is the key the rows are identified by.</summary>
    /// <remarks>
    /// What the key-and-row view is generated for, and only for: a table whose primary key
    /// is several columns has no single key value to pair a row with.
    /// spec/targets/table-collection-surface.md section 4.2.
    /// </remarks>
    public required bool IsPrimary { get; set; }

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

internal sealed class JavaFieldView
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
    public IReadOnlyList<JavaVariantView> Variants { get; set; } = new List<JavaVariantView>();

    /// <summary>The abstract type the variants make up, or empty.</summary>
    public string AbstractTypeName { get; set; } = "";

    /// <summary>The members every variant carries.</summary>
    public IReadOnlyList<JavaStructMemberView> BaseMembers { get; set; }
        = new List<JavaStructMemberView>();

    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The field declarations. Two for a reference, which keeps the raw index beside
    /// the resolved value.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>Whether this field is a record group, so a class is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares a nested array and no
    /// element type - the outer level has no name to declare one for.
    /// </summary>
    /// <remarks>See spec/types/nested-multi-level.md.</remarks>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>
    /// That class's name, unqualified: it is nested in the record, which is what scopes it.
    /// Empty for an ordinary field.
    /// </summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The fields of that class. Empty for an ordinary field.</summary>
    public required IReadOnlyList<JavaRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every class this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<JavaRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<JavaRecordTypeView>();

    /// <summary>
    /// Whether this field is a record array whose length is the sheet's column count, and so
    /// the record declares the helper that builds one with its elements constructed.
    /// </summary>
    public required bool IsFixedRecordArray { get; set; }

    /// <summary>How long that array is. Zero for everything else.</summary>
    public required int ElementCount { get; set; }

    /// <summary>Whether the sheet marked this field optional, so a row may have no value.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The field the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";
}

/// <summary>One field of a record group's generated class.</summary>
internal sealed class JavaRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The whole declaration line, type, name and initializer.</summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// The initializer-block line that fills an array member whose element type Java
    /// defaults to null, or empty when there is nothing to fill.
    /// </summary>
    /// <remarks>
    /// The same guarantee a scalar member's `= ""` gives: a file predating the column
    /// leaves nothing to write it, and null one field later is a crash rather than a
    /// missing value.
    /// </remarks>
    public required string Fill { get; set; }

}


/// <summary>
/// One generated class of a record group - the group's own element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax. Innermost first.
/// spec/types/nested-multi-level.md.
/// </remarks>
internal sealed class JavaRecordTypeView
{
    /// <summary>Name of the class.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of the class.</summary>
    public required IReadOnlyList<JavaRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the class belongs to, for its doc comment.</summary>
    public required string Owner { get; set; }

    /// <summary>
    /// The lookups this class declares beside its arrays, for a `set` or a `map`.
    /// </summary>
    /// <remarks>
    /// The second layer of a container's surface - the array is the file's order and this is
    /// the lookup. Both, because sorting is not this tool's to do.
    /// spec/types/set-and-map.md section 7.1.
    /// </remarks>
    public IReadOnlyList<string> Lookups { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
internal sealed class JavaColumnView
{
    /// <summary>
    /// The member access ending in the row's derived name, where this column is a reference
    /// member. spec/references/reference-surface-naming.md section 5.
    /// </summary>
    public string RowMemberAccess { get; set; } = "";

    /// <summary>The row's derived name, where this column is a reference.</summary>
    public string RowName { get; set; } = "";

    /// <summary>The column wire tag.</summary>
    public required int WireTag { get; set; }

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
    /// The Java spelling of the key a reference array stores, for the read to allocate.
    /// </summary>
    /// <remarks>
    /// Written into the template as `int` before, which is the assumption
    /// spec/references/reference-key-types.md removed where a scalar reference reads and left standing
    /// where an array of them allocates. Empty for a column that is not one.
    /// </remarks>
    public required string RefKeyType { get; set; }

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

    /// <summary>The field this column fills, without any element or member access.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The field of the element type this column fills, with a leading dot, or empty when
    /// the column is not a record member.
    /// </summary>
    public required string MemberAccess { get; set; }

    /// <summary>
    /// What a reference member's read appends to the member's name, so it lands in the key
    /// rather than in the row that key will resolve to. Empty for everything else.
    /// </summary>
    /// <remarks>
    /// On the member and before any subscript, because a member that is an array holds one
    /// key per element: `itemId[j]`, which is the member's own name.
    /// spec/references/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>
    /// The record group's element class, qualified by the record it is nested in - this is
    /// named from the table class next door.
    /// </summary>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that
    /// allocates when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    public required int ElementCount { get; set; }

    /// <summary>Element type, which an array allocation names.</summary>
    public required string ElementType { get; set; }

    public required string ReadScalar { get; set; }

    public required string ReadElement { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The field the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>What an absent row's value is put back to, so both read paths agree.</summary>
    public required string EmptyValue { get; set; }
}

internal sealed class JavaAccessorView
{
    public required string FileExtension { get; set; }
    public required IReadOnlyList<JavaTableSlotView> Tables { get; set; }
    public required IReadOnlyList<JavaCrossReferenceView> CrossReferences { get; set; }

    /// <summary>Every grid, as the pass that hands each one its axis names it.</summary>
    public IReadOnlyList<JavaGridLinkView> Grids { get; set; }
        = System.Array.Empty<JavaGridLinkView>();
}

/// <summary>One grid, as the accessor's linking pass names it.</summary>
internal sealed class JavaGridLinkView
{
    public required string Values { get; init; }

    public required string Columns { get; init; }
}

internal sealed class JavaTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class JavaCrossReferenceView
{
    public required string Table { get; set; }

    /// <summary>Record type of the table being walked, which the loop declares.</summary>
    public required string RecordName { get; set; }

    public required IReadOnlyList<JavaReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<JavaRecordReferenceView> RecordFields { get; set; }


}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class JavaRecordReferenceView
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

    /// <summary>The referenced record's class, which the local the lookup lands in is typed as.</summary>
    public required string RefRecordName { get; set; }
}

internal sealed class JavaReferenceFieldView
{
    /// <summary>
    /// Where the resolved row goes - the derived name for a whole-row reference, the
    /// column's own name for a dotted one.
    /// spec/references/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    public required string Name { get; set; }
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup, which is what a key resolves through.</summary>
    public required string RefLookup { get; set; }

    /// <summary>Record type of the table being pointed at, which the lookup declares.</summary>
    public required string RefRecordName { get; set; }

    public required string Value { get; set; }
    public required bool IsArray { get; set; }
}

/// <summary>
/// An abstract type and its variants, as this target declares them.
/// </summary>
/// <remarks>
/// One per declaration however many tables named it. A struct is an entity beside a table and
/// an enum, and emitting it inside each table that used it would give them types that share a
/// name and are not the same type. spec/types/polymorphism.md section 7.1.
/// </remarks>
internal sealed class JavaPolymorphicTypeView
{
    /// <summary>The abstract type's name.</summary>
    public required string Name { get; set; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required IReadOnlyList<JavaStructMemberView> BaseMembers { get; set; }

    /// <summary>What one of its values may be.</summary>
    public required IReadOnlyList<JavaVariantView> Variants { get; set; }
}

/// <summary>One variant of a <see cref="JavaPolymorphicTypeView"/>.</summary>
internal sealed class JavaVariantView
{
    /// <summary>The variant's declared name - the type a consumer narrows to.</summary>
    public required string TypeName { get; set; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; set; }

    /// <summary>The members this variant declares, beside the base ones.</summary>
    public required IReadOnlyList<JavaStructMemberView> Members { get; set; }
}

/// <summary>One member of an abstract type or of one of its variants.</summary>
/// <remarks>
/// A view of its own rather than the record member's, because none of the reference machinery
/// applies: the model refuses a reference inside a polymorphic group.
/// </remarks>
internal sealed class JavaStructMemberView
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
