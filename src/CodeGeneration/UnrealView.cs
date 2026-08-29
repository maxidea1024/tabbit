using System.Collections.Generic;

namespace Tabbit.CodeGeneration;

/// <summary>Everything the Unreal templates need, worked out in advance.</summary>
internal sealed class UnrealFileView
{
    /// <summary>Name of the accessor class, which also names the header and the .cpp.</summary>
    public required string AccessorName { get; set; }

    /// <summary>
    /// The module's export macro, `MODULENAME_API`.
    ///
    /// Every public type carries it, or the module links but nothing outside it can
    /// reach the generated types.
    /// </summary>
    public required string ApiMacro { get; set; }

    public required IReadOnlyList<UnrealEnumView> Enums { get; set; }
    public required IReadOnlyList<UnrealTableView> Tables { get; set; }

    /// <summary>
    /// The abstract types the sheets used, one entry per declaration.
    /// </summary>
    /// <remarks>
    /// This target writes one header, so they sit in it beside the enums and the row structs -
    /// but still one per declaration, not one per table that named it.
    /// spec/types/polymorphism.md section 7.1.
    /// </remarks>
    public IReadOnlyList<UnrealPolymorphicTypeView> Structs { get; set; }
        = new List<UnrealPolymorphicTypeView>();

    /// <summary>
    /// The constant sets, which this target did not emit at all until now.
    /// </summary>
    /// <remarks>
    /// **Not reflected.** A constant is a value the generated code hands over, not a row a
    /// designer edits in the editor, and a `UCLASS` of getters would be a second surface for
    /// every set - so these are plain `static inline const` members, the shape the C++ target
    /// writes. spec/layout/primary-layout.md section 8.5.
    /// </remarks>
    public IReadOnlyList<UnrealConstantSetView> ConstantSets { get; set; }
        = new List<UnrealConstantSetView>();

    public required UnrealAccessorView Accessor { get; set; }
}

/// <summary>One constant set, as this target's single header holds it.</summary>
internal sealed class UnrealConstantSetView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<UnrealConstantView> Constants { get; set; }
}

/// <summary>One constant.</summary>
internal sealed class UnrealConstantView
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required string Value { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
}

internal sealed class UnrealEnumView
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }
    public required IReadOnlyList<UnrealEnumLabelView> Labels { get; set; }

    /// <summary>
    /// Whether this enum can be `UENUM(BlueprintType)`, which requires a uint8 underlying
    /// type and so every label between 0 and 255.
    /// </summary>
    /// <remarks>
    /// A label outside that range used to refuse the whole conversion. Which made the Unreal
    /// target the one that could not read a model the other eleven read - and the values are
    /// the sheet's, not something a generator gets to reject. It degrades instead: the enum
    /// widens to int32, stays a UENUM so it is still reflected and still serialises, and
    /// loses only its Blueprint visibility. The fields typed with it lose theirs too, because
    /// UHT will not expose a property whose type Blueprint cannot see.
    /// </remarks>
    public required bool BlueprintVisible { get; set; }

    /// <summary>The underlying type: `uint8` normally, `int32` when a label does not fit.</summary>
    public required string UnderlyingType { get; set; }

    /// <summary>Which label pushed it past uint8, for the comment that says so.</summary>
    public required string? NotVisibleBecause { get; set; }
}

internal sealed class UnrealEnumLabelView
{
    public required string Name { get; set; }
    public required string Value { get; set; }

    /// <summary>What the editor shows, which is the label as the sheet spelled it.</summary>
    public required string DisplayName { get; set; }

    public required IReadOnlyList<string> Comment { get; set; }
}

/// <summary>A grid's accessor, spelled the way Unreal spells it.</summary>
/// <remarks>
/// The axis is a `TMap` from key to position rather than a pointer at the column table: the
/// accessor moves its tables into place after loading, and a pointer at one of them would name
/// the object that was moved from. <see cref="MatrixPlan"/> decides the rest.
/// spec/layout/matrix-declaration.md.
/// </remarks>
internal sealed class UnrealMatrixView
{
    public required string ColumnTable { get; init; }

    public required string ColumnTableName { get; init; }

    /// <summary>The column table's row type.</summary>
    public required string ColumnRecord { get; init; }

    public required string ColumnLookup { get; init; }

    public required string RowKeyMember { get; init; }

    public required string RowKeyParam { get; init; }

    public required string RowKeyType { get; init; }

    public required string RowKeyArg { get; init; }

    public required string RowLookup { get; init; }

    public required string ColumnKeyMember { get; init; }

    public required string ColumnKeyParam { get; init; }

    public required string ColumnKeyType { get; init; }

    public required string ColumnKeyArg { get; init; }

    public required string AtMember { get; init; }

    public required string GridMember { get; init; }

    public required string GridHasMember { get; init; }

    public required string CellType { get; init; }

    public required bool CellsAreOptional { get; init; }
}

internal sealed class UnrealTableView
{
    public required string RawName { get; set; }
    public required string RecordName { get; set; }
    public required string TableName { get; set; }
    public required string Location { get; set; }
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>
    /// The indexed fields: the sheet's first column plus every one marked with `*`.
    /// </summary>
    public required IReadOnlyList<UnrealIndexView> Indexes { get; set; }

    /// <summary>The grid this table holds the values of, or null when it is not one.</summary>
    public UnrealMatrixView? Matrix { get; set; }

    /// <summary>
    /// The statements filling every `set` and `map` lookup in the table, ready to paste.
    /// </summary>
    /// <remarks>
    /// Once every column is in: a map needs its key column and how long it is, and the
    /// columns arrive one at a time. spec/types/set-and-map.md section 7.3.
    /// </remarks>
    public IReadOnlyList<string> ContainerFill { get; set; } = System.Array.Empty<string>();

    public required IReadOnlyList<UnrealFieldView> Fields { get; set; }

    /// <summary>
    /// Whether any column reads through the cursor, and so the read declares one.
    ///
    /// One cursor variable for the whole method: the switch's cases share a scope, and
    /// C++ does not allow a jump past a live constructor, so each encodable column
    /// opens the shared cursor rather than declaring its own.
    /// </summary>
    public required bool NeedsCursor { get; set; }

    /// <summary>
    /// The columns of a data file, which is what the read switch dispatches on.
    /// </summary>
    /// <remarks>
    /// A separate list from the fields because they are separate units: declaring a member
    /// is per field, and reading is per column. A record group declares one member and is
    /// read as one column per member of it.
    /// </remarks>
    public required IReadOnlyList<UnrealColumnView> Columns { get; set; }

    /// <summary>Whether the read declares the presence buffer: true when any column is optional.</summary>
    public required bool NeedsPresence { get; set; }

    /// <summary>Whether any column of this table carries an element bitmap.</summary>
    public bool NeedsElementPresence { get; set; }
}

/// <summary>
/// One indexed field, and the lookups generated for it.
/// </summary>
/// <remarks>
/// Two lookups rather than the three every other target gets. A module built with
/// exceptions disabled - which is every Unreal module unless its Build.cs says
/// otherwise - has nothing to throw, so there is no honest `GetBy...OrThrow` to
/// generate. The same reason the reader reports a malformed file with a flag.
/// </remarks>
internal sealed class UnrealIndexView
{
    /// <summary>The record member holding the key.</summary>
    public required string Member { get; set; }

    /// <summary>What the lookup names end in - `Index` gives `FindByIndex`.</summary>
    public required string Suffix { get; set; }

    /// <summary>The key's type.</summary>
    public required string KeyType { get; set; }

    /// <summary>
    /// The type the lookups take: a const reference where a copy would cost, the value
    /// itself where it would not.
    /// </summary>
    public required string KeyParam { get; set; }

    /// <summary>The member holding the map from key to row position.</summary>
    public required string MapName { get; set; }

    /// <summary>The local the read builds before publishing it.</summary>
    public required string LocalName { get; set; }

    /// <summary>The field as the sheet spells it, for the doc comment.</summary>
    public required string FieldName { get; set; }
    /// <summary>Whether the key is several columns taken together.</summary>
    public required bool IsComposite { get; set; }

    /// <summary>The columns making it up - one entry unless it is composite.</summary>
    public required IReadOnlyList<KeyComponentView> Components { get; set; }

    /// <summary>The lookup's parameter list, one entry per column of the key.</summary>
    public required string Params { get; set; }

    /// <summary>What the map is subscripted with, given those parameters.</summary>
    public required string Argument { get; set; }

}

internal sealed class UnrealFieldView
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
    public IReadOnlyList<UnrealVariantView> Variants { get; set; } = new List<UnrealVariantView>();

    /// <summary>The abstract type the variants make up, or empty.</summary>
    public string AbstractTypeName { get; set; } = "";

    /// <summary>The `UENUM` naming which variant a value is, or empty.</summary>
    public string KindEnumName { get; set; } = "";

    /// <summary>The group's name as a function name carries it, in Pascal.</summary>
    public string PascalName { get; set; } = "";

    /// <summary>The members every variant carries.</summary>
    public IReadOnlyList<UnrealStructMemberView> BaseMembers { get; set; }
        = new List<UnrealStructMemberView>();

    public required IReadOnlyList<string> Comment { get; set; }

    public required string Name { get; set; }

    /// <summary>The member declaration, including its initializer.</summary>
    public required string Declaration { get; set; }

    /// <summary>Whether this field is a record group, so a USTRUCT is declared for it.</summary>
    public required bool IsRecord { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays, which declares no element USTRUCT - the
    /// outer level has no name. See spec/types/nested-multi-level.md.
    /// </summary>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>Name of that USTRUCT. Empty for an ordinary field.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>The members of that USTRUCT. Empty for an ordinary field.</summary>
    public required IReadOnlyList<UnrealRecordMemberView> Members { get; set; }

    /// <summary>
    /// Every USTRUCT this group declares, innermost first. One entry for a record whose members
    /// are all values, one more per level below that.
    /// </summary>
    public IReadOnlyList<UnrealRecordTypeView> RecordTypes { get; set; }
        = System.Array.Empty<UnrealRecordTypeView>();

    /// <summary>
    /// Whether the sheet marked this field optional, so a row may have no value for it.
    /// </summary>
    /// <remarks>
    /// Adds a `bHas{Name}` member beside the value. Not `TOptional`, which is not a property
    /// type UHT knows - the engine's own answer to the same problem is the `bOverride_X`
    /// pair in FPostProcessSettings. spec/types/optional-fields.md has the reasoning.
    /// </remarks>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>
    /// Whether the member carries a UPROPERTY.
    ///
    /// Almost always yes. A double does not, because UE4's header tool rejects the type
    /// outright and the generated module is meant to build on both UE4 and UE5.
    /// </summary>
    public required bool BlueprintVisible { get; set; }

    /// <summary>Why it does not, written into the generated code beside the member.</summary>
    public required string? NotVisibleBecause { get; set; }


    public required int ElementCount { get; set; }


}

/// <summary>One grid, as the accessor's linking pass names it.</summary>
internal sealed class UnrealGridLinkView
{
    public required string Values { get; init; }

    public required string Columns { get; init; }
}

internal sealed class UnrealAccessorView
{
    public required string FileExtension { get; set; }

    /// <summary>
    /// The Blueprint function library's class name.
    /// </summary>
    /// <remarks>
    /// Built in the generator rather than the template, which produced
    /// `UFTabbitCoreLibrary` by putting `U` in front of an accessor already prefixed
    /// `F`. Unreal's prefix says what a type is - `U` for a UObject, `F` for a plain
    /// class - so the old one comes off before the new one goes on.
    /// </remarks>
    public required string LibraryName { get; set; }

    public required IReadOnlyList<UnrealTableSlotView> Tables { get; set; }

    /// <summary>Every grid, as the pass that hands each one its axis names it.</summary>
    public IReadOnlyList<UnrealGridLinkView> Grids { get; set; }
        = System.Array.Empty<UnrealGridLinkView>();
}

internal sealed class UnrealTableSlotView
{
    public required string Name { get; set; }
    public required string TableName { get; set; }

    /// <summary>The row struct, which the Blueprint library hands back by value.</summary>
    public required string RecordName { get; set; }

    /// <summary>The table's name as the sheet spelled it, for the Blueprint category.</summary>
    public required string RawName { get; set; }

    /// <summary>
    /// The primary index's lookup, which is what the Blueprint node calls.
    /// </summary>
    public required string PrimaryLookup { get; set; }

    /// <summary>The primary index's key type, which the Blueprint node takes.</summary>
    public required string PrimaryKeyType { get; set; }

    /// <summary>The primary index's key parameter type.</summary>
    public required string PrimaryKeyParam { get; set; }

    /// <summary>The row getter's parameter list, one entry per column of the primary key.</summary>
    public required string PrimaryParams { get; set; }

    /// <summary>What that getter passes to the lookup.</summary>
    public required string PrimaryArgument { get; set; }

    /// <summary>The primary index's field name, as the sheet spells it.</summary>
    public required string PrimaryFieldName { get; set; }

    public required string DataFileName { get; set; }
}

/// <summary>One member of a record group's generated USTRUCT.</summary>
internal sealed class UnrealRecordMemberView
{
    public required IReadOnlyList<string> Comment { get; set; }

    /// <summary>The member's name, in Unreal's Pascal case.</summary>
    public required string Name { get; set; }

    /// <summary>The whole declaration line, type and initializer included.</summary>
    public required string Declaration { get; set; }

    /// <summary>Whether UHT will accept a UPROPERTY of this type.</summary>
    public required bool BlueprintVisible { get; set; }

    /// <summary>Why not, for the comment written in the UPROPERTY's place.</summary>
    public required string? NotVisibleBecause { get; set; }
}

/// <summary>
/// One generated USTRUCT of a record group - the group's own element type, or a level below it.
/// </summary>
/// <remarks>
/// A flat list rather than a tree: the recursion belongs in the view, because none of these
/// templates has a recursive include and the one that grew a tree walk would be the only place
/// where depth had to be reasoned about in template syntax.
///
/// Innermost first, and here that is required rather than tidy - a USTRUCT member needs its
/// complete type, and UHT reads the header in order.
///
/// A struct member of a USTRUCT type is a property UHT accepts, unlike a nested container. So
/// depth costs this target no reflection, which is the opposite of what an array of arrays cost
/// it. spec/types/nested-multi-level.md.
/// </remarks>
internal sealed class UnrealRecordTypeView
{
    /// <summary>Name of the USTRUCT.</summary>
    public required string TypeName { get; set; }

    /// <summary>Members of the USTRUCT.</summary>
    public required IReadOnlyList<UnrealRecordMemberView> Members { get; set; }

    /// <summary>Whether this is the group's own element type rather than a level below it.</summary>
    public required bool IsOutermost { get; set; }

    /// <summary>What the struct belongs to, for its doc comment.</summary>
    public required string Owner { get; set; }

    /// <summary>
    /// The lookups this struct declares beside its arrays, for a `set` or a `map`.
    /// </summary>
    /// <remarks>
    /// The engine's own containers, and neither keeps an order - the array beside it is what
    /// says what the file held. No UPROPERTY on them: the header tool has no property type
    /// for a map keyed by anything, and what a Blueprint reads is the array.
    /// spec/types/set-and-map.md section 7.1.
    /// </remarks>
    public IReadOnlyList<string> Lookups { get; set; } = System.Array.Empty<string>();
}

/// <summary>
/// One column of a data file, as the read switch sees it.
/// </summary>
internal sealed class UnrealColumnView
{
    /// <summary>A local name for the element count, not taken by any member.</summary>
    public required string CountLocal { get; set; }

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

    public required int WireTag { get; set; }

    /// <summary>Which read shape applies.</summary>
    public required string Kind { get; set; }

    /// <summary>The rendered CheckColumn call.</summary>
    public required string ColumnCheck { get; set; }

    /// <summary>The cursor opening placed ahead of the row loop, or empty.</summary>
    public required string CursorOpen { get; set; }

    /// <summary>
    /// The cursor's run call for a scalar whose column can arrive run-length encoded, or
    /// empty for one that reads row by row.
    /// </summary>
    /// <remarks>
    /// A run of a hundred thousand rows costs one call through this and a hundred
    /// thousand plain assignments, instead of a hundred thousand calls that each
    /// re-dispatch on the encoding.
    /// </remarks>
    public required string RunCall { get; set; }

    /// <summary>The declaration of the local the run decodes into, initialized.</summary>
    public required string RunValueDeclaration { get; set; }

    /// <summary>The local <see cref="RunValueDeclaration"/> declares, by name.</summary>
    public required string RunValueName { get; set; }

    /// <summary>
    /// The line assigning one row from the value the run decoded, inside the loop
    /// <see cref="RunCall"/> opens. Empty exactly when <see cref="RunCall"/> is.
    /// </summary>
    public required string RunSpend { get; set; }

    /// <summary>The one-line read through the cursor, or empty.</summary>
    public required string CursorRead { get; set; }

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
    /// key per element: `ItemIdIndex[ElementAt]`, not `ItemId[ElementAt]Index`.
    /// spec/references/references-in-records.md.
    /// </remarks>
    public required string MemberRefSuffix { get; set; }

    /// <summary>Which member of the group this column is, for an unnamed outer level.</summary>
    public int MemberAt { get; set; }

    /// <summary>How many inner arrays the group has, so a column can size the outer level.</summary>
    public int OuterCount { get; set; }

    /// <summary>Elements per row for a fixed array.</summary>
    public required int ElementCount { get; set; }

    /// <summary>The reader method one value is read with.</summary>
    public required string ReadCall { get; set; }

    /// <summary>
    /// Whether this column holds the first member of its record group - the one that sizes
    /// the array when the element count comes from the row.
    /// </summary>
    public required bool IsFirstMember { get; set; }

    /// <summary>The element type of the record group this column belongs to, or empty.</summary>
    public required string RecordTypeName { get; set; }

    /// <summary>Whether the file states, per row, which of this column's values are there.</summary>
    public required bool IsNullable { get; set; }

    /// <summary>The member the presence flag lands in.</summary>
    public required string PresenceMember { get; set; }

    /// <summary>Whether the column states which of an array's elements hold a value.</summary>
    public bool HasOptionalElements { get; set; }

    /// <summary>The member holding that answer per element, or blank when there is none.</summary>
    public string ElementPresenceMember { get; set; } = "";

    /// <summary>What an absent row's value is set to, so both read paths agree.</summary>
    public required string EmptyValue { get; set; }
}

/// <summary>
/// An abstract type and its variants, as this target declares them.
/// </summary>
/// <remarks>
/// One per declaration however many tables named it. A struct is an entity beside a table and
/// an enum, and emitting it inside each table that used it would give them types that share a
/// name and are not the same type. spec/types/polymorphism.md section 7.1.
/// </remarks>
internal sealed class UnrealPolymorphicTypeView
{
    /// <summary>The abstract type's name.</summary>
    public required string Name { get; set; }

    /// <summary>Its own fields, which every variant carries.</summary>
    public required IReadOnlyList<UnrealStructMemberView> BaseMembers { get; set; }

    /// <summary>What one of its values may be.</summary>
    public required IReadOnlyList<UnrealVariantView> Variants { get; set; }
}

/// <summary>One variant of a <see cref="UnrealPolymorphicTypeView"/>.</summary>
internal sealed class UnrealVariantView
{
    /// <summary>The variant's `USTRUCT` name, already carrying this target's `F`.</summary>
    public required string TypeName { get; set; }

    /// <summary>The enum constant naming this variant.</summary>
    public required string KindName { get; set; }

    /// <summary>The suffix a per-variant accessor is named with.</summary>
    public required string Suffix { get; set; }

    /// <summary>The number the file carries for it.</summary>
    public required int Discriminator { get; set; }

    /// <summary>The members this variant declares, beside the base ones.</summary>
    public required IReadOnlyList<UnrealStructMemberView> Members { get; set; }
}

/// <summary>One member of an abstract type or of one of its variants.</summary>
/// <remarks>
/// A view of its own rather than the record member's, because none of the reference machinery
/// applies: the model refuses a reference inside a polymorphic group.
/// </remarks>
internal sealed class UnrealStructMemberView
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
