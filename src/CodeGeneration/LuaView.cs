using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything Lua needs, worked out in advance.
///
/// Access paths arrive rendered - `.hp` or `["end"]` - because Lua keeps a keyword-named
/// field's name and reaches it with bracket syntax, and which of the two forms applies is
/// decided per name here rather than in template syntax. spec/targets/lua-language-support.md.
/// </summary>
internal sealed class LuaFileView
{
    public required IReadOnlyList<LuaEnumView> Enums { get; set; }
    public required IReadOnlyList<LuaConstantSetView> ConstantSets { get; set; }
    public required IReadOnlyList<LuaTableView> Tables { get; set; }
    public required LuaAccessorView Accessor { get; set; }
}

/// <summary>
/// One generated file: how it finds its siblings, and the single thing it declares.
/// </summary>
internal sealed class LuaPartView
{
    /// <summary>
    /// The pattern that takes this file's own module name down to the mount prefix, so
    /// the output is relocatable: a file one directory deep strips two components, the
    /// accessor at the top strips one.
    /// </summary>
    public required string RootPattern { get; set; }

    /// <summary>
    /// Rendered `local X = require(_root .. "...")` lines naming the generated types
    /// this file uses, from <see cref="TypeDependencies"/>.
    /// </summary>
    public required IReadOnlyList<string> Requires { get; set; }

    /// <summary>The accessor's module path piece, for a table file to require lazily.</summary>
    public string? AccessorModule { get; set; }

    public LuaTableView? Table { get; set; }
    public LuaEnumView? Enumm { get; set; }
    public LuaConstantSetView? Set { get; set; }

    /// <summary>The abstract type this module declares, when it declares one.</summary>
    public LuaPolymorphicTypeView? Structure { get; set; }
    public LuaAccessorView? Accessor { get; set; }
}

internal sealed class LuaEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<LuaEnumLabelView> Labels { get; set; }
}

internal sealed class LuaEnumLabelView
{
    /// <summary>The key in table-constructor form: `monday` or `["end"]`.</summary>
    public required string Key { get; set; }

    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class LuaConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<LuaConstantView> Constants { get; set; }
}

internal sealed class LuaConstantView
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class LuaTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    public required IReadOnlyList<LuaIndexView> Indexes { get; set; }

    /// <summary>
    /// The statements filling every `set` and `map` lookup in the table, ready to paste.
    /// </summary>
    /// <remarks>
    /// Once every column is in: a map needs its key column and how long it is, and the
    /// columns arrive one at a time. spec/types/set-and-map.md section 7.3.
    /// </remarks>
    public IReadOnlyList<string> ContainerFill { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// The record's declared field names, quoted and comma separated, for the strict
    /// metatable - the same list Python's `__slots__` carries, for the same reason:
    /// a name outside it is a typo, and here that is enforced at run time.
    /// </summary>
    public required string RecordFieldNames { get; set; }


    /// <summary>The table instance's field names, quoted and comma separated.</summary>
    public required string TableFieldNames { get; set; }

    /// <summary>The lua-language-server `---@field` lines of the record class.</summary>
    public required IReadOnlyList<string> Annotations { get; set; }

    public required IReadOnlyList<LuaFieldView> Fields { get; set; }
    public required IReadOnlyList<LuaColumnView> Columns { get; set; }
}

internal sealed class LuaIndexView
{
    /// <summary>The record access of the key field, rendered: `.index`.</summary>
    public required string Access { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `findByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The table field holding the map from key to row: `byIndex`.</summary>
    public required string MapName { get; set; }

    /// <summary>The field as the sheet spells it, for the error message.</summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// Whether keys go through <c>tcb.int64String</c> before touching the map. An int64
    /// is FFI cdata under LuaJIT, and cdata table keys compare by identity rather than
    /// value - so an int64-keyed map is keyed by the decimal string in both runtimes.
    /// </summary>
    public required bool NormalizesInt64 { get; set; }
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

internal sealed class LuaFieldView
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
    public IReadOnlyList<LuaVariantView> Variants { get; set; } = new List<LuaVariantView>();

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

    /// <summary>
    /// The group's own name, for the accessor built beside it.
    /// </summary>
    /// <remarks>
    /// This view had no name of its own: everything else it feeds is written from the
    /// initializers, which already carry theirs. spec/types/polymorphism.md section 7.2.
    /// </remarks>
    public string Name { get; set; } = "";

    /// <summary>The members every variant carries.</summary>
    public IReadOnlyList<LuaStructMemberView> BaseMembers { get; set; }
        = new List<LuaStructMemberView>();

    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// Constructor entries, `hp = 0,` - key and value with the trailing comma, ready to
    /// sit inside the record's table constructor.
    /// </summary>
    public required IReadOnlyList<string> Initializers { get; set; }

    public required bool IsRecord { get; set; }
    public bool MembersAreAnonymous { get; set; }

    /// <summary>
    /// Every element type this group declares, innermost first - required rather than
    /// tidy, because a constructor names the level below and that local has to exist by
    /// the time the line runs.
    /// </summary>
    public IReadOnlyList<LuaRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<LuaRecordTypeView>();
}

internal sealed class LuaRecordTypeView
{
    public required string TypeName { get; set; }
    public required string Owner { get; set; }
    public required bool IsOutermost { get; set; }
    public required IReadOnlyList<LuaRecordMemberView> Members { get; set; }

    /// <summary>The element's declared field names, quoted and comma separated.</summary>
    public required string FieldNames { get; set; }

    /// <summary>The lua-language-server `---@field` lines of the element class.</summary>
    public required IReadOnlyList<string> Annotations { get; set; }

    /// <summary>
    /// The lookups this record declares beside its arrays, for a `set` or a `map`.
    /// </summary>
    /// <remarks>
    /// Tables keyed by the value, which is what this language has instead of either - and a
    /// table has no order, so the array beside it is what says what the file held.
    /// spec/types/set-and-map.md section 7.1.
    /// </remarks>
    public IReadOnlyList<string> Lookups { get; set; } = System.Array.Empty<string>();

    /// <summary>Their `---@field` lines, so the language server knows them too.</summary>
    public IReadOnlyList<string> LookupAnnotations { get; set; } = System.Array.Empty<string>();
}

internal sealed class LuaRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<string> Initializers { get; set; }

}


/// <summary>
/// One column of a data file, as the read's tag chain sees it.
///
/// The assignment targets arrive whole - `record.slots[element].itemId` - because
/// three things vary inside them: bracket against dotted access, where the element
/// subscript sits, and whether a reference's key name replaces the member's. The loop
/// variables they mention are the ones the template declares: `record`, `element`, `i`.
/// </summary>
internal sealed class LuaColumnView
{
    public required int WireTag { get; set; }

    /// <summary>
    /// Which read shape applies: `scalar`, `scalar_ref`, `serial`, `serial_ref`,
    /// `var_array`, `record_var`, `record_serial`, `record_member_serial` or
    /// `array_of_arrays_member`.
    /// </summary>
    public required string Kind { get; set; }

    public required string ColumnCheck { get; set; }
    public required string CursorOpen { get; set; }
    public required string RunCall { get; set; }
    public required string RunSpend { get; set; }

    /// <summary>A scalar's assignment target: `record.hp`, `record.ownerIndex`.</summary>
    public required string ScalarTarget { get; set; }

    /// <summary>
    /// An element's assignment target inside the loop over `element`, for the record
    /// shapes and the array-of-arrays member.
    /// </summary>
    public required string ElementTarget { get; set; }

    /// <summary>Where a whole values list lands: `record.ints`, `record.ownerIndex`.</summary>
    public required string ValuesTarget { get; set; }

    /// <summary>
    /// The line clearing a fixed reference array's resolved list, beside the keys the
    /// read fills - empty for every other shape.
    /// </summary>
    public required string SecondaryClear { get; set; }

    /// <summary>The list a record_var's first member builds: `record.slots`.</summary>
    public required string GroupTarget { get; set; }

    /// <summary>The list this column's elements go in, where the read builds it per row.</summary>
    /// <remarks>
    /// Not the group: a member that is itself the array owns its own list, and one inner
    /// level of an array of arrays is a slot of the outer one. Empty for the kinds that do
    /// not build a list. spec/wire/tcb-v107-dynamic-arrays.md.
    /// </remarks>
    public required string ElementContainer { get; set; }

    /// <summary>The element constructor of the record shapes: `newVectorsSlotsEntry`.</summary>
    public required string RecordConstructor { get; set; }

    public required bool IsFirstMember { get; set; }
    public required int ElementCount { get; set; }

    public required string ReadScalar { get; set; }
    public required string ReadElement { get; set; }

    /// <summary>`local elementCount = ...` - from the cursor or the reader.</summary>
    public required string LengthRead { get; set; }

    public required bool IsNullable { get; set; }

    /// <summary>The presence flag's target: `record.hasBonus`.</summary>
    public required string PresenceTarget { get; set; }

    public bool HasOptionalElements { get; set; }

    /// <summary>The per-element answer's target: `record.hasIntsAt`.</summary>
    public string ElementPresenceTarget { get; set; } = "";

    /// <summary>The value field's target, which is what an absent row's reset writes.</summary>
    public required string EmptyTarget { get; set; }

    public required string EmptyValue { get; set; }
}

internal sealed class LuaAccessorView
{
    public required string Name { get; set; }
    public required string FileExtension { get; set; }

    /// <summary>The instance's field names, quoted and comma separated.</summary>
    public required string FieldNames { get; set; }

    public required IReadOnlyList<LuaTableSlotView> Tables { get; set; }
    public required IReadOnlyList<LuaCrossReferenceView> CrossReferences { get; set; }
}

internal sealed class LuaTableSlotView
{
    /// <summary>The instance field, rendered as an access: `.vectors`.</summary>
    public required string Access { get; set; }

    /// <summary>The same field as a constructor key: `vectors` or `["end"]`.</summary>
    public required string Key { get; set; }

    /// <summary>The local the load builds into: `loadedVectors`.</summary>
    public required string Loaded { get; set; }

    /// <summary>The table class local this file required: `VectorsTable`.</summary>
    public required string TableName { get; set; }

    /// <summary>Unescaped: this one names the file the exporter wrote.</summary>
    public required string DataFileName { get; set; }
}

internal sealed class LuaCrossReferenceView
{
    /// <summary>The loaded local whose records the linking pass walks.</summary>
    public required string Loaded { get; set; }

    public required IReadOnlyList<LuaReferenceFieldView> Fields { get; set; }
    public required IReadOnlyList<LuaRecordReferenceView> RecordFields { get; set; }


}

internal sealed class LuaReferenceFieldView
{
    /// <summary>The resolved row's target: `record.owner`.</summary>
    public required string Access { get; set; }

    /// <summary>The stored key's field: `record.ownerIndex`.</summary>
    public required string KeyAccess { get; set; }

    /// <summary>The loaded local of the referenced table.</summary>
    public required string RefTable { get; set; }

    /// <summary>The referenced table's primary lookup: `findByIndex`.</summary>
    public required string RefLookup { get; set; }

    /// <summary>What lands in the value: `target` or `target.name`.</summary>
    public required string Value { get; set; }

    public required bool IsArray { get; set; }
}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it. Whole
/// expressions, because which of the three record shapes this is decides where the
/// element number sits - on the group, on the member, or nowhere.
/// spec/references/references-in-records.md.
/// </summary>
internal sealed class LuaRecordReferenceView
{
    /// <summary>The resolved row this writes, loop variable included.</summary>
    public required string Access { get; set; }

    /// <summary>The stored key it resolves through.</summary>
    public required string Key { get; set; }

    /// <summary>The list whose length the loop runs, or empty when there is no loop.</summary>
    public required string Range { get; set; }

    public required string RefTable { get; set; }
    public required string RefLookup { get; set; }
}

/// <summary>
/// An abstract type and its variants, as this target declares them.
/// </summary>
/// <remarks>
/// One per declaration however many tables named it. A struct is an entity beside a table and
/// an enum, and emitting it inside each table that used it would give them types that share a
/// name and are not the same type. spec/types/polymorphism.md section 7.1.
/// </remarks>
internal sealed class LuaPolymorphicTypeView
{
    /// <summary>The abstract type's name.</summary>
    public required string Name { get; set; }

    /// <summary>The module the type lives in, which this language spells in snake case.</summary>
    public required string ModuleName { get; set; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required IReadOnlyList<LuaStructMemberView> BaseMembers { get; set; }

    /// <summary>What one of its values may be.</summary>
    public required IReadOnlyList<LuaVariantView> Variants { get; set; }
}

/// <summary>One variant of a <see cref="LuaPolymorphicTypeView"/>.</summary>
internal sealed class LuaVariantView
{
    /// <summary>The variant's declared name - what its `kind` holds.</summary>
    public required string TypeName { get; set; }

    /// <summary>
    /// Every field one of this variant's values has, as the strict metatable's list.
    /// </summary>
    /// <remarks>
    /// Both the base members and its own, and `kind`. A field left out of this list is an error
    /// to read, which is the whole reason this target declares them at all - a misspelling in a
    /// dynamic language is otherwise a nil that compares false with everything.
    /// spec/targets/lua-language-support.md.
    /// </remarks>
    public required string FieldNames { get; set; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; set; }

    /// <summary>The members this variant declares, beside the base ones.</summary>
    public required IReadOnlyList<LuaStructMemberView> Members { get; set; }
}

/// <summary>One member of an abstract type or of one of its variants.</summary>
/// <remarks>
/// A view of its own rather than the record member's, because none of the reference machinery
/// applies: the model refuses a reference inside a polymorphic group.
/// </remarks>
internal sealed class LuaStructMemberView
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
