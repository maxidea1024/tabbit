using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything Lua needs, worked out in advance.
///
/// Access paths arrive rendered - `.hp` or `["end"]` - because Lua keeps a keyword-named
/// field's name and reaches it with bracket syntax, and which of the two forms applies is
/// decided per name here rather than in template syntax. spec/lua-language-support.md.
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

}

internal sealed class LuaRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<string> Initializers { get; set; }

}


/// <summary>
/// One column of a data file, as the read's tag chain sees it.
///
/// The assignment targets arrive whole - `record.slots[element].itemIdIndex` - because
/// three things vary inside them: bracket against dotted access, where the element
/// subscript sits, and whether a reference's key name replaces the member's. The loop
/// variables they mention are the ones the template declares: `record`, `element`, `i`.
/// </summary>
internal sealed class LuaColumnView
{
    public required int Tag { get; set; }

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
    /// not build a list. spec/tcb-v107-dynamic-arrays.md.
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
/// spec/references-in-records.md.
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

