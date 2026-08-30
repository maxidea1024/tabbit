using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>Everything the PHP template needs, worked out in advance.</summary>
internal sealed class PhpFileView
{
    /// <summary>Namespace every generated type is declared in.</summary>
    public required string Namespace { get; set; }

    public required IReadOnlyList<PhpEnumView> Enums { get; set; }
    public required IReadOnlyList<PhpConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<PhpTableView> Tables { get; set; }
    public required PhpAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// Carries the requires as finished lines. PHP has no autoloader here, so a split file
/// has to require what it uses and how deep it sits decides the path - both worked out in
/// the generator, because path arithmetic in a template is arithmetic nothing can test.
/// </remarks>
internal sealed class PhpPartView
{
    /// <summary>The declared record type this file is for.</summary>
    public PhpRecordFileView? Record { get; set; }

    public string? Namespace { get; set; }

    /// <summary>Complete `require_once` lines, in the order they must run.</summary>
    public IReadOnlyList<string>? Requires { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public PhpTableView? Table { get; set; }

    /// <summary>The abstract type this file declares, when it declares one.</summary>
    public PhpPolymorphicTypeView? Structure { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public PhpEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public PhpConstantSetView? Set { get; set; }

    /// <summary>Every table, for the accessor.</summary>
    public IReadOnlyList<PhpTableView>? Tables { get; set; }

    /// <summary>The accessor's own shape, for the accessor file.</summary>
    public PhpAccessorView? Accessor { get; set; }
}

/// <summary>
/// A backed enum.
///
/// PHP has had these since 8.1 and they carry the declared value, so nothing here has
/// to invent a lookup table the way the Ruby and Python outputs do.
/// </summary>
internal sealed class PhpEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The case a value the sheet never declared falls back to.
    ///
    /// `from` throws on an undeclared value and a typed property cannot hold null, so a
    /// read goes through `tryFrom` and lands here instead - which is what every other
    /// generated reader does with the same situation.
    /// </summary>
    public required string DefaultCase { get; set; }

    public required IReadOnlyList<PhpEnumCaseView> Cases { get; set; }
}

internal sealed class PhpEnumCaseView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class PhpConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<PhpConstantView> Constants { get; set; }
}

internal sealed class PhpConstantView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

/// <summary>A grid's accessor, spelled the way PHP spells it.</summary>
/// <remarks>
/// <see cref="MatrixPlan"/> decides the shape; this is its spelling.
/// spec/layout/matrix-declaration.md.
/// </remarks>
internal sealed class PhpMatrixView
{
    public required string ColumnTable { get; init; }

    public required string ColumnTableName { get; init; }

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

internal sealed class PhpTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }

    /// <summary>Whether the table's primary key is one column, so a subscript is generated.</summary>
    /// <remarks>
    /// PHP is the one target where the subscript is an interface rather than a member, so the
    /// class declaration has to know before the index loop reaches it. A table declaring
    /// ArrayAccess without the four methods would not load at all.
    /// spec/targets/table-collection-surface.md section 5.4.
    /// </remarks>
    public required bool HasKeySubscript { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<PhpIndexView> Indexes { get; set; }

    /// <summary>The grid this table holds the values of, or null when it is not one.</summary>
    public PhpMatrixView? Matrix { get; set; }

    /// <summary>
    /// The statements filling every `set` and `map` lookup in the table, ready to paste.
    /// </summary>
    /// <remarks>
    /// Once every column is in: a map needs its key column and how long it is, and the
    /// columns arrive one at a time. spec/types/set-and-map.md section 7.3.
    /// </remarks>
    public IReadOnlyList<string> ContainerFill { get; set; } = System.Array.Empty<string>();


    public required IReadOnlyList<PhpFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields, because declaring a property is per field and
    /// reading is per column, and a record group is one column per member of it.
    /// </remarks>
    public required IReadOnlyList<PhpColumnView> Columns { get; set; }

    /// <summary>
    /// Whether the record declares a constructor, which it does when a field's starting
    /// value is not a constant expression.
    /// </summary>
    public required bool NeedsConstructor { get; set; }

    /// <summary>
    /// Whether any column reads through a cursor. PHP declares nothing ahead of an
    /// assignment, so the template needs no line from this - it exists so every
    /// language's view answers the same questions.
    /// </summary>
    public required bool NeedsCursor { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
internal sealed class PhpIndexView
{
    /// <summary>The record property holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type, as a parameter declaration.</summary>
    public required string KeyType { get; set; }

    /// <summary>The key's type for a docblock, which wants the array's key type.</summary>
    public required string KeyDocType { get; set; }

    /// <summary>
    /// The `$key` parameter as an array offset.
    /// </summary>
    /// <remarks>
    /// `$key` itself for the types PHP already accepts as one, and a conversion for the two
    /// it does not - a uuid and an enum. Subscripting a PHP array with either is a runtime
    /// `TypeError`, so this cannot be left to the template.
    /// </remarks>
    public required string KeyOffset { get; set; }

    /// <summary>The record's own key property as an array offset, for building the map.</summary>
    public required string MemberOffset { get; set; }

    /// <summary>The property holding the map from key to row.</summary>
    public required string MapName { get; set; }

    /// <summary>The local the read builds before publishing it.</summary>
    public required string LocalName { get; set; }

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

internal sealed class PhpFieldView
{
    /// <summary>
    /// The variants of a polymorphic group, or empty when the group is one fixed shape.
    /// </summary>
    /// <remarks>
    /// The type itself is declared once, elsewhere; this is what the table needs to build a
    /// value of it - which number means which variant, and which of the entry's fields each
    /// one carries. spec/types/polymorphism.md sections 7.1 and 7.2.
    /// </remarks>
    public IReadOnlyList<PhpVariantView> Variants { get; set; } = new List<PhpVariantView>();

    /// <summary>The abstract type the variants make up, or empty.</summary>
    public string AbstractTypeName { get; set; } = "";

    /// <summary>
    /// Whether the polymorphic group is an array, so each element carries its own
    /// discriminator and what a consumer reads is a list of the abstract type.
    /// spec/types/polymorphism.md section 5.3.
    /// </summary>
    public bool VariantsAreArray { get; set; }

    /// <summary>The members every variant carries.</summary>
    public IReadOnlyList<PhpStructMemberView> BaseMembers { get; set; }
        = new List<PhpStructMemberView>();

    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The property declarations, each with its type and its initializer.
    ///
    /// A list, because a reference contributes two: the index that came off the wire
    /// and the record it is resolved to once every table is loaded.
    /// </summary>
    public required IReadOnlyList<string> Declarations { get; set; }

    /// <summary>
    /// The lines the record's constructor runs for this field, or empty when the
    /// declaration said everything.
    /// </summary>
    /// <remarks>
    /// A record group needs these because a PHP property initializer has to be a constant
    /// expression, and `new SlotEntry()` is not one.
    /// </remarks>
    public required IReadOnlyList<string> ConstructorLines { get; set; }

    /// <summary>Whether this field is a record group, so a class is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which needs no element type - the outer
    /// level has no name to declare one for.
    /// </summary>
    /// <remarks>See spec/types/nested-multi-level.md.</remarks>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>
    /// That class's name, which carries the table's - every generated class shares one
    /// namespace. Empty for an ordinary field.
    /// </summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The properties of that class. Empty for an ordinary field.</summary>
    public required IReadOnlyList<PhpRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every class this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<PhpRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<PhpRecordTypeView>();

    /// <summary>Whether the sheet marked this field optional, so a row may have no value.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The property the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";
}

/// <summary>One property of a record group's generated class.</summary>
internal sealed class PhpRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The declaration lines, which include the doc line an array needs.</summary>
    public required IReadOnlyList<string> Declarations { get; set; }

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
/// <summary>One declared record type, and the levels inside it that are its own.</summary>
/// <remarks>spec/types/declared-struct-identity.md.</remarks>
internal sealed class PhpRecordFileView
{
    /// <summary>The declaration itself, for what its members ask the file to bring in.</summary>
    public required Models.RecordType Declared { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<string> Comment { get; init; }

    public required IReadOnlyList<PhpRecordTypeView> Types { get; init; }
}

internal sealed class PhpRecordTypeView
{
    /// <summary>Name of the class.</summary>
    public required string TypeName { get; set; }


    /// <summary>Properties of the class.</summary>
    public required IReadOnlyList<PhpRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>Whether the type is a declaration's, so it is written once elsewhere.</summary>
    /// <remarks>spec/types/declared-struct-identity.md.</remarks>
    public bool IsShared { get; set; }

    /// <summary>
    /// The lookups this class declares beside its arrays, for a `set` or a `map`.
    /// </summary>
    /// <remarks>
    /// The second layer of a container's surface - the list is the file's order and this is
    /// the lookup. spec/types/set-and-map.md section 7.1.
    /// </remarks>
    public IReadOnlyList<string> Lookups { get; set; } = System.Array.Empty<string>();

    /// <summary>What the class belongs to, for its doc comment.</summary>
    public required string Owner { get; set; }

    /// <summary>
    /// Lines of the class's constructor, or empty when it needs none.
    /// </summary>
    /// <remarks>
    /// A member that is itself a record cannot be built at its declaration - a PHP property
    /// initializer has to be a constant expression and `new PositionEntry()` is not one - and a
    /// typed property left unset is an error to read rather than a null. So the level below is
    /// made here, which is the same thing the record class's own constructor does one level up.
    /// </remarks>
    public required IReadOnlyList<string> ConstructorLines { get; set; }
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
internal sealed class PhpColumnView
{
    /// <summary>
    /// Where the resolved rows go for a whole-row reference, or the column's own name for a
    /// dotted one. spec/references/reference-surface-naming.md sections 5 and 9.
    /// </summary>
    public string RowName { get; set; } = "";

    /// <summary>
    /// Where the keys off the wire go: the column's own name for a whole-row reference, and
    /// the `Index` one for a dotted reference. spec/references/reference-surface-naming.md sections 5
    /// and 9.
    /// </summary>
    public string KeyName { get; set; } = "";

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
    /// The cursor construction ahead of an encodable column's row loop, or empty for
    /// a column that reads the reader directly.
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

    /// <summary>The property this column fills, without any element or member access.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The property of the element type this column fills, with a leading arrow, or empty
    /// when the column is not a record member.
    /// </summary>
    public required string MemberAccess { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>
    /// What a reference member's read appends to the member's name, so it lands in the key
    /// rather than in the row that key will resolve to. Empty for everything else.
    /// </summary>
    /// <remarks>
    /// On the member and before any subscript, because a member that is an array holds one
    /// key per element: `itemId[$j]`, not the member's own name.
    /// spec/references/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>The record group's element class, which the read constructs.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that
    /// builds the list when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    public required int ElementCount { get; set; }

    /// <summary>
    /// The expression reading one value, at whatever depth the template places it - through
    /// the cursor where the column reads through one, and off the reader where it does not.
    /// </summary>
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

internal sealed class PhpAccessorView
{
    public required string Name { get; set; }
    public required string FileExtension { get; set; }
    public required IReadOnlyList<PhpTableSlotView> Tables { get; set; }
    public required IReadOnlyList<PhpCrossReferenceView> CrossReferences { get; set; }

    /// <summary>Every grid, as the pass that hands each one its axis names it.</summary>
    public IReadOnlyList<PhpGridLinkView> Grids { get; set; }
        = System.Array.Empty<PhpGridLinkView>();
}

/// <summary>One grid, as the accessor's linking pass names it.</summary>
internal sealed class PhpGridLinkView
{
    public required string Values { get; init; }

    public required string Columns { get; init; }
}

internal sealed class PhpTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }
    public required string DataFileName { get; set; }
}

internal sealed class PhpCrossReferenceView
{
    public required string Table { get; set; }
    public required IReadOnlyList<PhpReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<PhpRecordReferenceView> RecordFields { get; set; }


}


/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class PhpRecordReferenceView
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

internal sealed class PhpReferenceFieldView
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
internal sealed class PhpPolymorphicTypeView
{
    /// <summary>The abstract type's name.</summary>
    public required string Name { get; set; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required IReadOnlyList<PhpStructMemberView> BaseMembers { get; set; }

    /// <summary>What one of its values may be.</summary>
    public required IReadOnlyList<PhpVariantView> Variants { get; set; }
}

/// <summary>One variant of a <see cref="PhpPolymorphicTypeView"/>.</summary>
internal sealed class PhpVariantView
{
    /// <summary>The variant's declared name - the type a consumer narrows to.</summary>
    public required string TypeName { get; set; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; set; }

    /// <summary>The members this variant declares, beside the base ones.</summary>
    public required IReadOnlyList<PhpStructMemberView> Members { get; set; }
}

/// <summary>One member of an abstract type or of one of its variants.</summary>
/// <remarks>
/// A view of its own rather than the record member's, because none of the reference machinery
/// applies: the model refuses a reference inside a polymorphic group.
/// </remarks>
internal sealed class PhpStructMemberView
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
