using System;
using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Everything the C# template needs, worked out in advance.
///
/// Same division as the C++ view: the template decides where things go, and anything
/// that depends on the model - a type name, a read call, a rendered literal - arrives
/// already finished.
/// </summary>
internal sealed class CsFileView
{
    /// <summary>The namespace, or empty. The template wraps the file in it when set.</summary>
    public required string Namespace { get; set; }

    /// <summary>
    /// What the accessor type is called, and what the files naming it say.
    /// </summary>
    /// <remarks>
    /// A view field rather than a literal in the templates, because it was a literal in the
    /// templates and the recipe's `AccessorName` therefore named only the file. A project
    /// setting it got a file called one thing holding a type called another.
    /// </remarks>
    public required string AccessorName { get; set; }

    /// <summary>
    /// Extension the recipe told the exporter to write, which is what the accessor's read
    /// defaults to.
    /// </summary>
    /// <remarks>
    /// It was a `".tcb"` literal in the template until this existed - so a recipe that set
    /// the extension on both the export and this target got the right file names out of the
    /// exporter and a reader that looked for the default anyway.
    /// </remarks>
    public required string FileExtension { get; set; }

    public required IReadOnlyList<CsTableView> Tables { get; set; }

    public required IReadOnlyList<CsEnumView> Enums { get; set; }

    public required IReadOnlyList<CsConstantSetView> ConstantSets { get; set; }

    /// <summary>
    /// Only the tables that reference another.
    ///
    /// A separate list rather than a test inside the template, because the blank line
    /// separating one table's resolution block from the next has to count these and not
    /// every table - which is what the hand-written version did with its own counter.
    /// </summary>
    public required IReadOnlyList<CsTableView> TablesWithReferences { get; set; }
}

/// <summary>
/// One generated file, for the templates that render one thing.
/// </summary>
/// <remarks>
/// The output is a file per table, per enum and per constant set, and each of those
/// templates needs the namespace as well as its own subject. Rather than hand every
/// template the whole model and trust it to loop over only the right part, each gets a
/// view holding exactly what it is for - so a template cannot reach a table it is not
/// writing.
///
/// One class with one payload property rather than four near-identical ones, because the
/// only thing they would differ in is the name of that property and Scriban addresses it
/// by name from the template.
/// </remarks>
internal sealed class CsPartView
{
    /// <summary>The namespace, or empty. The head template wraps the file in it when set.</summary>
    public string? Namespace { get; set; }

    /// <summary>What the accessor type is called. A table's read reaches it for the keys.</summary>
    public string? AccessorName { get; set; }

    /// <summary>The table this file is for, when it is a table file.</summary>
    public CsTableView? Table { get; set; }

    /// <summary>The enum this file is for, when it is an enum file.</summary>
    public CsEnumView? Enumm { get; set; }

    /// <summary>The constant set this file is for, when it is a constants file.</summary>
    public CsConstantSetView? Set { get; set; }
}

internal sealed class CsTableView
{
    /// <summary>Table name in Pascal case; the class is this plus `Table`.</summary>
    public required string Name { get; set; }

    /// <summary>Table name as the sheet spelled it, used in the data file name.</summary>
    public required string RawName { get; set; }

    /// <summary>Doc-comment lines, already split. Empty when the sheet had no comment.</summary>
    public required IReadOnlyList<string> Comment { get; set; }

    public required IReadOnlyList<CsFieldView> Fields { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from <see cref="Fields"/> because they are separate units:
    /// declaring a member is per field, and reading is per column. They are the same
    /// thing for every table written before records existed - a folded group is one
    /// column - and a record group is one column per member.
    ///
    /// Keeping them apart is what makes record support a different list rather than a
    /// second branch through the read path. See spec/nested-fields.md.
    /// </remarks>
    public required IReadOnlyList<CsColumnView> Columns { get; set; }

    /// <summary>The fields a lookup dictionary is built for.</summary>
    public required IReadOnlyList<CsFieldView> IndexedFields { get; set; }

    /// <summary>The fields that point at another table.</summary>
    public required IReadOnlyList<CsFieldView> ReferenceFields { get; set; }

    /// <summary>
    /// Record groups holding at least one reference member, for the linking pass.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ReferenceFields"/> because the loop differs: a reference
    /// that is a member is resolved per element, so the generated code walks the array
    /// before it looks anything up. spec/references-in-records.md.
    /// </remarks>
    public required IReadOnlyList<CsRecordReferenceView> RecordReferenceFields { get; set; }

    /// <summary>
    /// Whether the read needs a scratch int for enum casting.
    ///
    /// The reader hands back an int and the field is an enum, so one temporary is
    /// declared for the whole method rather than one per field.
    /// </summary>
    public required bool NeedsEnumTemp { get; set; }

    /// <summary>
    /// Whether the read declares the column cursor: true when any scalar column can
    /// arrive encoded, which is what the cursor exists to decode.
    /// </summary>
    public required bool NeedsCursor { get; set; }

    /// <summary>
    /// Whether the read declares the presence buffer: true when any column is optional.
    /// </summary>
    /// <remarks>
    /// Declared once outside the switch for the same reason the cursor is - a `case` is not
    /// its own scope in C#, so two of them declaring it would not compile.
    /// </remarks>
    public required bool NeedsPresence { get; set; }

    /// <summary>Whether any column of this table carries an element bitmap.</summary>
    public bool NeedsElementPresence { get; set; }

    /// <summary>`"A", "B"` - the field-name array literal's contents.</summary>
    public required string FieldNameLiterals { get; set; }

    /// <summary>`r.A, r.B` - the value-map row's contents.</summary>
    public required string FieldValueExpressions { get; set; }
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
/// <remarks>
/// Everything here answers "how is this column read", which is a question about the file.
/// What the value is stored in - the member's name, its type, its element count - comes
/// along because the read has to assign somewhere, but the shape of the declaration is
/// <see cref="CsFieldView"/>'s business.
/// </remarks>
internal sealed class CsColumnView
{
    /// <summary>The column's wire tag, which is how the read matches it in a file.</summary>
    public required int Tag { get; set; }

    /// <summary>
    /// The rendered CheckColumn call: kind, count and the elements this column accepts -
    /// its own, plus the lossless promotions.
    /// </summary>
    public required string ColumnCheck { get; set; }

    /// <summary>
    /// Which read shape applies: `var_array`, `serial`, `record_serial`, `record_var` or
    /// `scalar`.
    /// </summary>
    public required string ReadKind { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group, which is the one
    /// that allocates when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    /// <summary>The element type of the record group, for the member columns that allocate it.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>
    /// Whether the element type has a member the factory has to fill - a string, which
    /// starts null and would be a crash one field later.
    /// </summary>
    public required bool RecordNeedsInit { get; set; }

    /// <summary>
    /// Whether the file states, per row, which of this column's values are there.
    /// </summary>
    /// <remarks>
    /// When true the block starts with a presence bitmap, the read pulls it before the row
    /// loop, and each row records what it said.
    /// </remarks>
    public required bool IsNullable { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>
    /// Which element of the outer level this column is, for an array of arrays.
    /// </summary>
    /// <remarks>
    /// The outer index is which column this is rather than something read per row, so the
    /// read fills that one element and the array itself came with the record.
    /// spec/nested-multi-level.md.
    /// </remarks>
    public int MemberAt { get; set; }

    /// <summary>The field holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceField { get; set; } = "";

    /// <summary>The backing field the presence flag lands in, for a nullable column.</summary>
    public required string PresenceField { get; set; }

    /// <summary>
    /// What an absent row's value is set to, so both read paths land on the same thing.
    /// </summary>
    /// <remarks>
    /// The values are decoded for every row - that is what keeps the encodings out of this -
    /// so an absent row has just been given whatever the block held for it. Putting the
    /// declared empty value back makes the binary path agree with the JSON one, where an
    /// absent value is `null` and the member is simply never assigned.
    /// </remarks>
    public required string EmptyValue { get; set; }

    /// <summary>
    /// The rendered cursor construction placed ahead of the row loop, or empty for a
    /// column that never arrives encoded and keeps reading the reader directly.
    /// </summary>
    public required string CursorOpen { get; set; }

    /// <summary>The lines reading one element, at whatever depth the template places them.</summary>
    public required IReadOnlyList<string> ElementRead { get; set; }

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
    public required string RunCall { get; set; }

    /// <summary>
    /// The lines assigning one row from `value`, inside the loop <see cref="RunCall"/>
    /// opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required IReadOnlyList<string> RunRead { get; set; }

    /// <summary>
    /// The arrays a reference column fills beside its values, allocated by the read.
    /// </summary>
    /// <remarks>
    /// A reference holds the key that came off the wire and whether it resolved, and an array
    /// of references holds one of each per element. The declaration cannot size them - how
    /// many elements a row holds is the file's answer now - so the read allocates them where
    /// it allocates the values. Empty for every column that is not one.
    /// </remarks>
    public IReadOnlyList<string> ParallelArrays { get; set; } = System.Array.Empty<string>();

    /// <summary>Backing field this column fills, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>Element type name of that member.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// Property name of the member, which is how the generated element-count constant
    /// (`Record.{PropName}_N`) is reached.
    /// </summary>
    public required string PropName { get; set; }
}

/// <summary>
/// One member of a record group: a field of the generated element type.
/// </summary>
internal sealed class CsRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Field name on the element type, Pascal cased.</summary>
    public required string PropName { get; set; }

    /// <summary>That field's type name.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// When the member is a reference, the type of the key it stores. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// A reference member carries three things where an ordinary one carries a value: the
    /// row it resolved to, the key that came off the wire, and whether the resolution
    /// happened. They live in the element rather than beside it because a group may hold
    /// more than one reference, and a name built from the group and the target would
    /// collide the moment two members point at the same table.
    /// spec/references-in-records.md.
    /// </remarks>
    public required string RefKeyTypeName { get; set; }

    /// <summary>
    /// The type of the resolution flag - `bool`, or an array of them where the member is an
    /// array. Empty when the member is not a reference.
    /// </summary>
    public required string RefFlagTypeName { get; set; }

    /// <summary>
    /// What allocates the stored keys where the member is an array, and nothing otherwise.
    /// </summary>
    /// <remarks>
    /// A record of arrays holds one key per element exactly as it holds one row per element,
    /// so the key and the flag are allocated with the member. A scalar member needs neither:
    /// C#'s own default for an `int` and a `bool` is already the unset one.
    /// </remarks>
    public required string RefKeyInitializer { get; set; }

    /// <summary>What allocates the resolution flags, on the same condition.</summary>
    public required string RefFlagInitializer { get; set; }

    /// <summary>The referenced table, for the accessor's linking pass. Empty otherwise.</summary>
    public required string RefTable { get; set; }

    /// <summary>The referenced table's throwing lookup. Empty otherwise.</summary>
    public required string RefLookup { get; set; }

    /// <summary>What follows the stored key to ask whether it points anywhere.</summary>
    public required string RefIsSet { get; set; }

    /// <summary>
    /// What follows the declaration to initialize it, or nothing where C#'s own default
    /// is already an empty value.
    /// </summary>
    public required string Initializer { get; set; }

    /// <summary>Whether a comma follows in the element type's ToString.</summary>
    public required bool IsFirst { get; set; }

    /// <summary>
    /// Whether this member is itself the array, which it is when the group is one record
    /// rather than an array of them.
    /// </summary>
    /// <remarks>
    /// The array is allocated by the element factory rather than by the read, exactly as
    /// an array of records is. The read then fills `record._g.Member[j]` and never
    /// allocates - which is what lets the members be read in any order without one
    /// discarding what another wrote. See spec/nested-multi-level.md.
    /// </remarks>
    public required bool IsArray { get; set; }

    /// <summary>
    /// What each element of an array member is set to, for the types whose C# default is
    /// not already the empty value. Empty for everything else and for a scalar member.
    /// </summary>
    public required string ElementInitializer { get; set; }

    /// <summary>
    /// Whether this member is itself a record, so its type is another generated struct
    /// rather than a primitive.
    /// </summary>
    /// <remarks>
    /// `Star1.Position.X` makes `Position` one of these. The template does not need to know
    /// how deep it is: the type it names is declared alongside the others in
    /// <see cref="CsFieldView.RecordTypes"/>, and every level is built the same way.
    /// See spec/nested-multi-level.md.
    /// </remarks>
    public bool IsRecord { get; set; }
}

/// <summary>
/// One generated struct of a record group - the outermost element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree, because the recursion belongs in the view: Scriban has no
/// recursive include in use anywhere in these templates, and a template that walked a tree
/// would be the one place where declaration order and depth had to be reasoned about in
/// template syntax. So the view flattens, innermost first, and the template loops.
/// </remarks>
internal sealed class CsRecordTypeView
{
    /// <summary>Name of the struct, declared inside `Record`.</summary>
    public required string TypeName { get; set; }

    /// <summary>Fields of this struct.</summary>
    public required IReadOnlyList<CsRecordMemberView> Members { get; set; }

    /// <summary>
    /// Whether any member needs setting past C#'s own default, so a factory is generated.
    /// </summary>
    public required bool NeedsInit { get; set; }

    /// <summary>
    /// Whether this is the group's own element type rather than a level below it.
    /// </summary>
    /// <remarks>
    /// The outermost type is the one whose factory may take a length - an array of records
    /// allocates its elements together. A level below is always one value at a time.
    /// </remarks>
    public required bool IsOutermost { get; set; }
}

/// <summary>
/// One serial field, in every shape the generated class distinguishes.
/// </summary>
internal sealed class CsFieldView
{
    /// <summary>
    /// Whether this field is a record group, so the template declares an element type for
    /// it and the member is of that type rather than a primitive.
    /// </summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether the sheet marked this field optional, so a row may have no value for it.
    /// </summary>
    /// <remarks>
    /// Adds a `Has{Prop}` accessor and the flag behind it. The value accessor is unchanged
    /// and reads the type's empty value where a row had none.
    /// </remarks>
    public required bool IsNullable { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The field holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceField { get; set; } = "";

    /// <summary>The backing field the presence flag lands in. Empty when not optional.</summary>
    public required string PresenceField { get; set; }

    /// <summary>
    /// Name of the generated element type, for a record group. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// The group name plus `Entry`, declared inside `Record`. It cannot simply be the
    /// group name: that is already the property's name, and C# does not allow a nested
    /// type and a member to share one.
    /// </remarks>
    public required string RecordTypeName { get; set; }

    /// <summary>Fields of the element type. Empty unless <see cref="IsRecord"/>.</summary>
    public required IReadOnlyList<CsRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every struct this group declares, innermost first. One entry for a record whose
    /// members are all values; one more per level below that.
    /// </summary>
    /// <remarks>
    /// The last entry is the group's own element type, and <see cref="Members"/> is its
    /// members - kept so the declaration and read paths that only ever look at the outermost
    /// level read as they did. spec/nested-multi-level.md.
    /// </remarks>
    public IReadOnlyList<CsRecordTypeView> RecordTypes { get; set; } = Array.Empty<CsRecordTypeView>();

    /// <summary>
    /// Whether the element type needs a factory that fills its string fields, because a
    /// struct cannot initialize its own.
    /// </summary>
    /// <remarks>
    /// Field initializers in a struct are C# 10 and need an explicit parameterless
    /// constructor; the generated code has to compile as C# 8, which is what Unity 2020.3
    /// accepts. So a static factory sets them instead, and the record's field initializer
    /// calls it.
    ///
    /// It is not cosmetic. A file written before a member existed carries no column for
    /// it, so nothing writes that field - and the guarantee everywhere else in this
    /// generator is that such a string arrives empty rather than null, because null is a
    /// crash one field later.
    /// </remarks>
    public required bool NeedsElementInit { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Public property name.</summary>
    public required string PropName { get; set; }

    /// <summary>Private backing field name, including its leading underscore.</summary>
    public required string FieldName { get; set; }

    /// <summary>Element type name.</summary>
    public required string FieldType { get; set; }

    /// <summary>
    /// What follows the member's declaration to initialize it, or nothing when C#'s own
    /// default is already an empty value.
    /// </summary>
    public required string Initializer { get; set; }

    /// <summary>Element count of a serial field, which is its column count.</summary>
    public required int ElementCount { get; set; }

    /// <summary>
    /// Whether this record group is one record whose members are arrays, rather than an
    /// array of records.
    /// </summary>
    /// <remarks>
    /// The group declares no array of its own, so its `_N` belongs to the members - one
    /// number, because the folding requires them to agree on it.
    /// </remarks>
    public bool MembersAreArrays { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, so it declares `T[][]` and no element
    /// type: there is no name for the outer level to be a property of.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>How many inner arrays there are. Zero unless the group is one.</summary>
    public int OuterCount { get; set; }

    /// <summary>
    /// The type one value has, for the group whose declared type is the inner array. Empty
    /// otherwise.
    /// </summary>
    public string ElementTypeName { get; set; } = "";

    /// <summary>
    /// What each value of an array of arrays is set to, for the types whose C# default is
    /// not the empty value. Empty otherwise.
    /// </summary>
    public string ElementInitializer { get; set; } = "";

    /// <summary>Referenced table's class name, without the `Table` suffix.</summary>
    public required string RefTable { get; set; }

    /// <summary>
    /// The referenced table's throwing lookup, which is what a key resolves through.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `GetByIndexOrThrow`. The
    /// primary index is whatever the sheet put in the first column - its type is checked
    /// to be `int`, but its name is not - so a sheet that calls it `Id` generates
    /// `GetByIdOrThrow`, and the accessor is the only place that has to know.
    /// </remarks>
    public required string RefLookup { get; set; }

    /// <summary>Referenced field's property name, empty when the reference names a whole row.</summary>
    public required string RefField { get; set; }

    /// <summary>
    /// The type of the stored key, which is the target's primary index.
    /// </summary>
    /// <remarks>
    /// `int` was written into the template, which is one of the places that kept a table
    /// keyed by anything else from being pointed at. spec/reference-key-types.md.
    /// </remarks>
    public required string RefKeyTypeName { get; set; }

    /// <summary>
    /// What follows the stored key to ask whether it points anywhere, `> 0` for a number.
    /// </summary>
    /// <remarks>
    /// Zero is the convention for "points at nothing", and it needs a spelling per key type:
    /// a string key has no zero, and comparing one against a number does not compile. Given
    /// as a suffix so the template composes the member name itself.
    /// spec/reference-optionality.md · spec/reference-key-types.md.
    /// </remarks>
    public required string RefIsSet { get; set; }

    /// <summary>
    /// Which declaration shape applies: `array_ref`, `var_array`, `array`, `scalar_ref`
    /// or `scalar`.
    /// </summary>
    public required string Kind { get; set; }

    /// <summary>Type of the setter a resolved reference is assigned through.</summary>
    public required string ReferenceSetterType { get; set; }

    /// <summary>Whether the reference names a field of the target rather than the row.</summary>
    public required bool ReferencesField { get; set; }
}

internal sealed class CsEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CsEnumLabelView> Labels { get; set; }
}

internal sealed class CsEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>Whether a trailing comma follows. C# allows one; the generator omits it.</summary>
    public required bool IsLast { get; set; }
}

internal sealed class CsConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<CsConstantView> Constants { get; set; }
}

internal sealed class CsConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

/// <summary>
/// One reference that is a member of a record, as the linking pass writes it.
/// </summary>
/// <remarks>
/// Whole expressions rather than the parts to build them from, because which of the three
/// record shapes this is decides where the element number sits - on the group, on the
/// member, or nowhere - and the template should not be the place that knows.
/// spec/references-in-records.md.
/// </remarks>
internal sealed class CsRecordReferenceView
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

    /// <summary>The referenced table, and the lookup a key resolves through.</summary>
    public required string RefTable { get; set; }
    public required string RefLookup { get; set; }

    /// <summary>What follows the stored key to ask whether it points anywhere.</summary>
    public required string RefIsSet { get; set; }
}
