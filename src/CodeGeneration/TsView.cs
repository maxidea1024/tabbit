using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Views for the TypeScript templates.
///
/// Unlike the other three generators this one writes a module per entity, so there is a
/// view per output file rather than one for the whole thing.
/// </summary>
internal sealed class TsIndexView
{
    /// <summary>What the accessor class is called.</summary>
    public required string AccessorName { get; set; }

    /// <summary>Its file, extension left off, as an import path spells it.</summary>
    public required string AccessorFile { get; set; }
    /// <summary>`namespace X {` line, or empty when no namespace is set.</summary>
    public required string NamespaceOpen { get; set; }

    /// <summary>The matching closer, or empty.</summary>
    public required string NamespaceClose { get; set; }

    /// <summary>
    /// What is exported, and out of which file. The two differ: a type keeps its Pascal
    /// name and the file it lives in is kebab-case.
    /// </summary>
    public required IReadOnlyList<TsExportView> Enums { get; set; }
    public required IReadOnlyList<TsExportView> Tables { get; set; }
    public required IReadOnlyList<TsExportView> ConstantSets { get; set; }
}

internal sealed class TsExportView
{
    /// <summary>The exported name, as declared.</summary>
    public required string Name { get; set; }

    /// <summary>The file it is in, without the extension.</summary>
    public required string File { get; set; }
}

internal sealed class TsTableSetView
{
    /// <summary>What the accessor class is called.</summary>
    public required string AccessorName { get; set; }

    /// <summary>Its file, extension left off, as an import path spells it.</summary>
    public required string AccessorFile { get; set; }
    public required IReadOnlyList<TsTableSlotView> Tables { get; set; }

    /// <summary>
    /// Default extension of the binary data files, as the recipe told the exporter to
    /// write them.
    /// </summary>
    public required string BinaryFileExtension { get; set; }

    /// <summary>
    /// The tables holding reference columns, and what each one has to be linked to.
    /// </summary>
    /// <remarks>
    /// Empty until now, and so was the method it renders: TypeScript generated the
    /// `setReference_*_INTERNAL` methods and never called one, so `record.categoryId`
    /// was the raw key from JSON or nothing at all from binary.
    /// </remarks>
    public required IReadOnlyList<TsCrossReferenceView> CrossReferences { get; set; }
}

/// <summary>One table's reference columns, for the linking pass.</summary>
internal sealed class TsCrossReferenceView
{
    /// <summary>The accessor member holding the table.</summary>
    public required string Table { get; set; }

    public required IReadOnlyList<TsReferenceFieldView> Fields { get; set; }

    /// <summary>
    /// The references that are members of a record, which resolve inside the element rather
    /// than beside it. spec/references-in-records.md.
    /// </summary>
    public required IReadOnlyList<TsRecordReferenceView> RecordFields { get; set; }
}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// </remarks>
internal sealed class TsRecordReferenceView
{
    /// <summary>The resolved row this writes, loop variable included.</summary>
    public required string Access { get; set; }

    /// <summary>The stored key it resolves through.</summary>
    public required string Key { get; set; }

    /// <summary>The flag recording that the resolution happened.</summary>
    public required string Flag { get; set; }

    /// <summary>
    /// The loop bound, or empty where the group is one record and there is nothing to walk.
    /// </summary>
    public required string Count { get; set; }

    public required string RefTable { get; set; }
    public required string RefLookup { get; set; }

    /// <summary>What follows the stored key to ask whether it points anywhere.</summary>
    public required string RefIsSet { get; set; }
}

internal sealed class TsReferenceFieldView
{
    /// <summary>The record's property name, which names the setter.</summary>
    public required string PropName { get; set; }

    /// <summary>The record's backing member, which holds the key.</summary>
    public required string FieldName { get; set; }

    /// <summary>The accessor member holding the table being pointed at.</summary>
    public required string RefTable { get; set; }

    /// <summary>The referenced table's class name, which names the index member.</summary>
    public required string RefTableType { get; set; }

    /// <summary>
    /// The referenced table's throwing lookup, which is what a key resolves through.
    /// </summary>
    public required string RefLookup { get; set; }

    /// <summary>What the resolved reference yields: the row, or one of its fields.</summary>
    public required string Value { get; set; }

    /// <summary>Whether the column is a fixed group of references rather than one.</summary>
    public required bool IsArray { get; set; }

    /// <summary>How many, when it is a group.</summary>
    public required int ElementCount { get; set; }

    /// <summary>
    /// What follows the stored key to ask whether it points anywhere, `> 0` for a number.
    /// </summary>
    /// <remarks>
    /// Zero is the convention for "points at nothing", and it needs a spelling per key type:
    /// a string key has no zero, and a 64-bit one compares against `0n` rather than `0`.
    /// spec/reference-optionality.md · spec/reference-key-types.md.
    /// </remarks>
    public required string RefIsSet { get; set; }
}

internal sealed class TsTableSlotView
{
    /// <summary>
    /// The local the accessor binds this table to while reading, which is not always the
    /// member name: `package` is a legal property and an illegal `const`.
    /// </summary>
    public required string Local { get; set; }

    /// <summary>Accessor member name, camelCase and escaped.</summary>
    public required string Member { get; set; }

    /// <summary>Table name as declared, which is also the class prefix.</summary>
    public required string Name { get; set; }

    /// <summary>The file the table is in, without the extension.</summary>
    public required string File { get; set; }
}

internal sealed class TsEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<TsEnumLabelView> Labels { get; set; }
}

internal sealed class TsEnumLabelView
{
    public required string Name { get; set; }

    /// <summary>The rendered initializer - a number, or the label's own name quoted.</summary>
    public required string Value { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }
    public required bool IsLast { get; set; }
}

internal sealed class TsConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<string> Imports { get; set; }
    public required IReadOnlyList<TsConstantView> Constants { get; set; }
}

internal sealed class TsConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class TsTableView
{
    /// <summary>What the accessor class is called.</summary>
    public required string AccessorName { get; set; }

    /// <summary>Its file, extension left off, as an import path spells it.</summary>
    public required string AccessorFile { get; set; }
    /// <summary>Table name as declared; the classes are this plus Record and Table.</summary>
    public required string Name { get; set; }

    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Import statements for the enums and records this module names.</summary>
    public required IReadOnlyList<string> Imports { get; set; }

    public required IReadOnlyList<TsFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the binary read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from <see cref="Fields"/>: declaring a member is per field and
    /// reading is per column. They are the same list for every table written before
    /// records existed, and a record group is one column per member.
    /// </remarks>
    public required IReadOnlyList<TsColumnView> Columns { get; set; }

/// <summary>The fields that reference another table, and so get a wiring method.</summary>
    public required IReadOnlyList<TsFieldView> ReferenceFields { get; set; }

            /// <summary>The fields a lookup map is built for.</summary>
    public required IReadOnlyList<TsFieldView> IndexedFields { get; set; }

    /// <summary>
    /// Whether the read declares the column cursor: true when any scalar column can
    /// arrive encoded, which is what the cursor exists to decode.
    /// </summary>
    public required bool NeedsCursor { get; set; }

    /// <summary>
    /// Whether the read declares the presence buffer: true when any column is optional.
    /// </summary>
    public required bool NeedsPresence { get; set; }

    /// <summary>Whether any column of this table carries an element bitmap.</summary>
    public bool NeedsElementPresence { get; set; }
}

/// <summary>
/// One column of a data file, as the binary read switch sees it.
/// </summary>
/// <remarks>
/// Everything here answers "how is this column read". Where the value lands comes along
/// because the read has to assign somewhere; the shape of the declaration is
/// <see cref="TsFieldView"/>'s business. spec/nested-fields.md has the split.
/// </remarks>
internal sealed class TsColumnView
{
    /// <summary>The column's wire tag.</summary>
    public required int Tag { get; set; }

    /// <summary>The rendered checkColumn call.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// The rendered cursor construction placed ahead of the row loop, or empty for a
    /// column that never arrives encoded.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>
    /// How an array column's row learns how many elements it holds.
    /// </summary>
    /// <remarks>
    /// From the cursor where the column reads through one, because an encoded array's
    /// lengths are their own stream at the front of the block rather than a number in front
    /// of each row. The cursor answers the same call either way, so this is one expression
    /// and not a branch in the emitted loop.
    /// </remarks>
    public required string LengthRead { get; set; }

    /// <summary>
    /// The cursor's run method for a scalar whose column can arrive run-length encoded -
    /// `nextSameI32` or `nextSameString` - or empty for one that reads row by row.
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

    /// <summary>
    /// Which read shape applies: `var_array`, `array_ref`, `array`, `scalar_ref`,
    /// `record_array_member`, `record_member` or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>The expression reading one value of the element type.</summary>
    public required string BinaryRead { get; set; }

    /// <summary>Backing member this column fills, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>
    /// The field of the element type this column fills, with a leading dot, or empty when
    /// the column is not a record member.
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
    /// key per element: `itemId_index[j]`, not `itemId[j]_index`.
    /// spec/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Element count of a fixed array.</summary>
    public required int ElementCount { get; set; }

    /// <summary>Referenced table's name, for the stored-index member.</summary>
    public required string RefTable { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that creates
    /// the elements when the count is the row's rather than the table's.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    /// <summary>
    /// An object literal of every member's empty value, for the member column that has to
    /// create the elements before any of them is filled in.
    /// </summary>
    public required string RecordLiteral { get; set; }

    /// <summary>
    /// `Table.Group`, for the diagnostic a member column raises when the file disagrees with
    /// itself about how many elements a row has.
    /// </summary>
    public required string QualifiedGroupName { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in, for a nullable column.</summary>
    public required string PresenceField { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The property holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceField { get; set; } = "";

    /// <summary>
    /// What an absent row's value is set to, so both read paths land on the same thing.
    /// </summary>
    /// <remarks>
    /// The block carries a value for every row - that is what keeps the encodings out of
    /// this - so an absent row has just been given whatever was there. Putting the declared
    /// empty value back makes this path agree with the JSON one, where an absent value is
    /// `null` and the member is simply never assigned.
    /// </remarks>
    public required string EmptyValue { get; set; }
}

/// <summary>
/// One member of a record group: a property of the generated element interface.
/// </summary>
internal sealed class TsRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Property name on the element interface, camelCase and escaped.</summary>
    public required string PropName { get; set; }

    /// <summary>That property's type.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// Its type in the JSON export, which is not always the member type: a 64-bit integer
    /// is exported as a string because JSON's single numeric type would round it.
    /// </summary>
    public required string JsonWireType { get; set; }

    /// <summary>An empty value of the member's own type.</summary>
    public required string DefaultValue { get; set; }

    /// <summary>
    /// The type of the stored key where the member is a reference, and empty otherwise.
    /// </summary>
    /// <remarks>
    /// A reference member carries three things where an ordinary one carries a value: the row
    /// it resolved to, the key that came off the wire, and whether the resolution happened.
    /// All three inside the element, because a group may hold more than one reference and a
    /// name built from the group and the target would collide the moment two members point at
    /// one table. spec/references-in-records.md.
    /// </remarks>
    public string RefKeyTypeName { get; set; } = "";

    /// <summary>The type of the resolution flag, on the same condition.</summary>
    public string RefFlagTypeName { get; set; } = "";

    /// <summary>What the stored key and the flag hold before a row is read.</summary>
    public string RefKeyDefault { get; set; } = "";
    public string RefFlagDefault { get; set; } = "";

    /// <summary>
    /// Whether this member is itself a record, so its type is another generated interface.
    /// </summary>
    /// <remarks>
    /// `Star1.Position.X` makes `Position` one of these. The interface it names is declared
    /// alongside the others in <see cref="TsFieldView.RecordTypes"/>, so the template does not
    /// have to know how deep it is. See spec/nested-multi-level.md.
    /// </remarks>
    public bool IsRecord { get; set; }
}

/// <summary>
/// One generated element interface of a record group - the group's own, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax.
/// </remarks>
internal sealed class TsRecordTypeView
{
    /// <summary>Name of the interface.</summary>
    public required string TypeName { get; set; }

    /// <summary>Properties of the interface.</summary>
    public required IReadOnlyList<TsRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }
}

/// <summary>
/// One serial field, in every shape the generated module distinguishes.
/// </summary>
internal sealed class TsFieldView
{
    /// <summary>
    /// Whether the sheet marked this field optional, so a row may have no value for it.
    /// </summary>
    /// <remarks>
    /// Adds a `has{Prop}` accessor and the member behind it. The value accessor is
    /// unchanged and reads the type's empty value where a row had none.
    /// </remarks>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in. Empty when not optional.</summary>
    public required string PresenceField { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The property holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceField { get; set; } = "";

    /// <summary>
    /// Whether this field is a record group, so the module declares an element interface
    /// for it and the member is of that type.
    /// </summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares no element interface - the
    /// outer level has no name. See spec/nested-multi-level.md.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>How many inner arrays there are. Zero unless the group is one.</summary>
    public int OuterCount { get; set; }

    /// <summary>Name of the generated element interface, for a record group.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>Properties of that interface. Empty unless <see cref="IsRecord"/>.</summary>
    public required IReadOnlyList<TsRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every interface this group declares, innermost first. One entry for a record whose
    /// members are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<TsRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<TsRecordTypeView>();

    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Public accessor name, camelCase and escaped.</summary>
    public required string PropName { get; set; }

    /// <summary>Private backing member, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>Property name in Pascal case, used for the index map members.</summary>
    public required string PascalName { get; set; }

    /// <summary>Member type.</summary>
    public required string FieldType { get; set; }

    /// <summary>What the member is declared as, when no column fills it.</summary>
    public required string DefaultValue { get; set; }

    /// <summary>
    /// Type the value has in the JSON export, which is not always the member type: a
    /// 64-bit integer is exported as a string, because JSON's single numeric type is a
    /// double and would round it.
    /// </summary>
    public required string JsonWireType { get; set; }

    public required int ElementCount { get; set; }

    /// <summary>Referenced table's name, without a suffix.</summary>
    public required string RefTable { get; set; }

    /// <summary>
    /// The TypeScript type of the stored key, which is the target's primary index.
    /// </summary>
    /// <remarks>
    /// `number` was written into the template, which is one of the places that kept a table
    /// keyed by anything else from being pointed at. spec/reference-key-types.md.
    /// </remarks>
    public required string RefKeyTypeName { get; set; }

    /// <summary>The value that key member starts at, before a row is read.</summary>
    public required string RefKeyInitial { get; set; }

    /// <summary>
    /// Which declaration shape applies: `array_ref`, `var_array`, `array`, `scalar_ref`
    /// or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    public required bool IsArray { get; set; }

    /// <summary>Type of the setter a resolved reference is assigned through.</summary>
    public required string ReferenceSetterType { get; set; }

    /// <summary>
    /// Whether the reference names a whole row rather than one of its fields.
    ///
    /// The two setters differ by a semicolon, which is arbitrary but is what the
    /// generated modules have always contained.
    /// </summary>
    public required bool ReferenceIsRecord { get; set; }

    /// <summary>The assignment reading this field out of a named JSON row.</summary>
    public required string FromNamedRow { get; set; }

    /// <summary>The statements reading this field out of a compact JSON row.</summary>
    public required IReadOnlyList<string> FromCompactRow { get; set; }
}
