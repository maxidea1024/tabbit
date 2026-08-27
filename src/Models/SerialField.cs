using System;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Models;

/// <summary>
/// Where the sequence number sits in a serial field's column names.
///
/// Columns only fold together when they agree on this, so `Text1`/`Text2` and
/// `Item1Bonus`/`Item2Bonus` form separate groups rather than one confused one.
/// </summary>
public enum SerialFieldPattern
{
    /// <summary>Not a serial column: no digits, or more than one run of them.</summary>
    None,

    /// <summary>The name ends in the number, as in `Text1`.</summary>
    TrailingNumber,

    /// <summary>The number sits inside the name, as in `Item1Bonus`.</summary>
    MiddleNumber,
}

/// <summary>
/// What one entry of a table holds: a scalar, or a record built from several columns.
///
/// The distinction is what separates `Slot1`/`Slot2` - two numbers folded into one
/// `int[]` - from `Slot1.Id`/`Slot1.Count`/`Slot2.Id`/`Slot2.Count`, which is an array of
/// two records. spec/types/nested-fields.md has the notation and why it looks like that.
/// </summary>
public enum SerialFieldKind
{
    /// <summary>One value per element. Every table written before nesting existed.</summary>
    Scalar,

    /// <summary>
    /// Several named values per element, each from its own column with its own type.
    /// The members are in <see cref="SerialField.Members"/> and
    /// <see cref="SerialField.Fields"/> is not used.
    /// </summary>
    Record,
}

/// <summary>
/// One level below a record group: its name, and either the columns filling it or the
/// members it is itself built from.
/// </summary>
/// <remarks>
/// A member is a leaf or a group, and <see cref="Members"/> is what says which. A leaf
/// holds the columns; a group holds members that hold columns, as far down as the sheet
/// wrote. Nothing here counts the levels - depth is a property of the data, not a limit of
/// the model. See spec/types/nested-multi-level.md.
/// </remarks>
public class RecordMember
{
    /// <summary>
    /// Member name as generated code sees it, Pascal cased - or the level's element number
    /// when it has no name of its own.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The columns holding this member, one per element, in element order. Empty unless
    /// this member is a leaf.
    ///
    /// Its length is the array length, and it is the same for every member of a group -
    /// the folding requires that, because an element missing one member would generate a
    /// record with a value nothing ever writes.
    /// </summary>
    public List<Field> Fields { get; set; } = new List<Field>();

    /// <summary>
    /// Members this one is built from, when it is a group rather than a leaf. Empty for a
    /// leaf, which is every member of a record one level deep.
    /// </summary>
    public List<RecordMember> Members { get; set; } = new List<RecordMember>();

    /// <summary>Whether this member holds columns rather than further members.</summary>
    public bool IsLeaf => Members.Count == 0;

    /// <summary>
    /// The leaves at or below this member, which is the unit the wire and the generators
    /// count in - one leaf is one wire column.
    /// </summary>
    public IEnumerable<RecordMember> Leaves
        => IsLeaf ? new[] { this } : Members.SelectMany(member => member.Leaves);

    /// <summary>Every column at or below this member.</summary>
    public IEnumerable<Field> AllFields => Leaves.SelectMany(leaf => leaf.Fields);

    /// <summary>
    /// The first column at or below this member, which carries the properties every column
    /// of it shares - and which a diagnostic points at.
    /// </summary>
    /// <remarks>
    /// Descends rather than answering null for a group member. A caller here is asking
    /// "where in the sheet is this", and a group has an answer to that even though it has
    /// no single column of its own.
    /// </remarks>
    public Field? FirstField => AllFields.FirstOrDefault();

    /// <summary>Type of this member. The folding has already required the elements to agree.</summary>
    public ValueType Type => (Fields.Count > 0) ? Fields[0].Type : ValueType.None;

    /// <summary>Element type behind this member, looking through the array kinds.</summary>
    public ValueType ElementType => (Fields.Count > 0) ? ValueTypes.ElementOf(Fields[0].Type) : ValueType.None;

    /// <summary>Whether this member references another table.</summary>
    public bool IsRef => (Fields.Count > 0) && Fields[0].IsRef;

    /// <summary>
    /// Whether this level repeats, so the level above it holds several of these.
    /// </summary>
    /// <remarks>
    /// Set by the folding from the level's own step in the column path, not derived from
    /// the column count: one column is a one-element array here and a plain value in an
    /// array of records, and the same count meaning two things is exactly what this
    /// separates.
    /// </remarks>
    public bool IsArray { get; set; }

    /// <summary>
    /// Whether this member's own cell holds the whole list, rather than the level repeating.
    /// </summary>
    /// <remarks>
    /// A member typed `string[]` is one column and one cell per row, and that cell holds as
    /// many values as the author typed. `Bag.Tags` is that; `Slot1.Id`/`Slot2.Id` is the
    /// other. The two arrive at the same file - the wire has written a length per row since
    /// v107 - and they are read from different places, which is what this separates.
    ///
    /// spec/types/set-and-map.md section 4.
    /// </remarks>
    public bool ListIsInTheCell
        => IsLeaf && Fields.Count > 0 && ValueTypes.IsArray(Fields[0].Type);

    /// <summary>
    /// Whether this member holds several values, whichever of the two ways said so.
    /// </summary>
    /// <remarks>
    /// What a generator declaring the member asks: `string[]` either way. Where the values
    /// are read from is <see cref="IsArray"/> and <see cref="ListIsInTheCell"/>.
    /// </remarks>
    public bool HoldsList => IsArray || ListIsInTheCell;

    /// <summary>
    /// The container this member was declared as, or none.
    /// </summary>
    /// <remarks>
    /// Set by the folding, which is the one place that knows what level a member is at - a
    /// `map`'s columns all carry the mark, and only the level says that `Prices` is the map
    /// and `Prices.Value` is what it holds. spec/types/set-and-map.md section 3.
    /// </remarks>
    public ContainerKind Container { get; set; }

    /// <summary>Whether this level is reached by number rather than by name.</summary>
    /// <remarks>
    /// <see cref="Name"/> still holds the number, because a diagnostic and a column check
    /// have to be able to say which one they mean.
    /// </remarks>
    public bool IsAnonymous { get; set; }
}

/// <summary>
/// How a table's columns are presented to the exporters and generators.
///
/// Every column belongs to exactly one of these. Most are a group of one, but
/// consecutively numbered columns fold into a single array-valued entry - so
/// `Text1`, `Text2` become one `TextArray` rather than two fields, which is what
/// makes them usable as an array in generated code.
///
/// A group's element is a scalar or a record; see <see cref="SerialFieldKind"/>.
/// </summary>
public class SerialField
{
    /// <summary>Whether one element of this group is a scalar or a record.</summary>
    public SerialFieldKind Kind { get; set; } = SerialFieldKind.Scalar;

    /// <summary>
    /// Members of the record, in the order their columns appear in the sheet. Empty
    /// unless <see cref="Kind"/> is Record.
    /// </summary>
    public List<RecordMember> Members { get; set; } = new List<RecordMember>();

    /// <summary>Whether one element of this group is a record rather than a scalar.</summary>
    public bool IsRecord => Kind == SerialFieldKind.Record;

    /// <summary>
    /// The container this whole group was declared as, or none.
    /// </summary>
    /// <remarks>
    /// **The group rather than a member of it**, which is what a container written in the
    /// sheet's own type cell is: `map&lt;int,int&gt;` on a column called `Prices` makes `Prices`
    /// the map. A container declared as a struct member is one level in and marks a
    /// <see cref="RecordMember"/> instead. spec/types/set-and-map.md section 2.3.
    /// </remarks>
    public ContainerKind Container { get; set; }

    /// <summary>
    /// Whether this group is one record whose members are arrays, rather than an array of
    /// records.
    /// </summary>
    /// <remarks>
    /// The same columns and the same wire either way - a record is stored one column per
    /// member, so a record of arrays is what the file already holds. What differs is the
    /// shape the group is assembled into: `{ M: [a, b] }` against `[{ M: a }, { M: b }]`.
    ///
    /// Set from the columns by the folding, which takes it from whichever notation the
    /// layout read. False for every table written before this existed.
    ///
    /// See spec/types/nested-multi-level.md.
    /// </remarks>
    public bool MembersAreArrays { get; set; }

    /// <summary>
    /// Whether this group is an array of arrays: the same shape as above, with the members
    /// unnamed.
    /// </summary>
    /// <remarks>
    /// `name[0][1]` numbers the outer level where `name["M"][1]` names it. The columns, the
    /// wire and the read loop are identical - only the declaration differs, because there is
    /// no name to declare a member by. So a consumer writes `g[i][j]` rather than `g.M[j]`,
    /// and no element type is generated.
    ///
    /// Implies <see cref="MembersAreArrays"/>; the two are set together.
    /// </remarks>
    public bool MembersAreAnonymous { get; set; }

    /// <summary>
    /// How many elements a record group has, which is how many columns each of its
    /// members has. Zero for a scalar group, which reports its length through
    /// <see cref="Fields"/> instead.
    /// </summary>
    public int RecordElementCount
        => (Members.Count > 0) ? Members[0].Leaves.First().Fields.Count : 0;

    /// <summary>
    /// Every leaf at or below this group - the unit the wire counts in, one leaf to one
    /// wire column. Empty for a scalar group, which is one column and answers through
    /// <see cref="Fields"/>.
    /// </summary>
    public IEnumerable<RecordMember> Leaves
        => IsRecord ? Members.SelectMany(member => member.Leaves) : Enumerable.Empty<RecordMember>();

    /// <summary>
    /// Every column this group covers, whichever kind it is. For the passes that need to
    /// reach each underlying column - tag assignment, target-side filtering, the data
    /// walk - and should not have to know which shape they are looking at.
    /// </summary>
    public IEnumerable<Field> AllFields
        => IsRecord ? Members.SelectMany(member => member.AllFields) : Fields;

    /// <summary>
    /// Columns that must not carry a tag of their own, because another column of the same
    /// wire column already does.
    /// </summary>
    public IEnumerable<Field> NonTagCarryingFields
        => IsRecord ? Leaves.SelectMany(leaf => leaf.Fields.Skip(1)) : Fields.Skip(1);


    /// <summary>
    /// Name this group is exposed under. The field's own name for a group of one, or
    /// the shared stem with `_array` appended when several columns folded together.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>Columns in this group, in ascending order of their sequence number.</summary>
    public List<Field> Fields { get; set; } = new List<Field>();

    /// <summary>The column name with its digits removed, which is what groups them.</summary>
    public string NamePart { get; set; } = "";

    /// <summary>Where the sequence number sits. Columns only fold together when this matches.</summary>
    public SerialFieldPattern Pattern { get; set; } = SerialFieldPattern.None;

    /// <summary>
    /// Forces a single column to be presented as a one-element array.
    ///
    /// For a table that will grow more numbered columns later: without it, `Text1`
    /// alone would be a scalar and adding `Text2` would change the generated API
    /// from a value to an array.
    /// </summary>
    public bool TreatAsArrayEvenIfSingleItem { get; set; } = false;

    /// <summary>
    /// Whether this group is an index, so its values must be unique.
    ///
    /// Arrays are excluded: uniqueness of a list of values is not a useful key, and
    /// none of the generated lookups can index by one.
    /// </summary>
    /// <remarks>
    /// A record group is never one. There is nothing to be unique about - the key would
    /// have to be the whole record - and none of the generated lookups can index by one.
    /// </remarks>
    public bool IsIndexer => !IsRecord && (Fields.Count > 0) && !IsArray && FirstField!.Indexing;

    /// <summary>Whether this group references another table.</summary>
    /// <remarks>
    /// False for a record group: a reference belongs to a member rather than to the
    /// record, so the question is asked of <see cref="RecordMember.IsRef"/> instead.
    /// </remarks>
    public bool IsRef => !IsRecord && (Fields.Count > 0) && Fields[0].IsRef;

    /// <summary>
    /// Whether consumers should see this as an array, from either cause: several
    /// numbered columns folded together, or a single column holding a delimited
    /// list.
    /// </summary>
    /// <remarks>
    /// For a record group the count of elements decides it, exactly as the count of
    /// columns does for a scalar group: `Pos.X`/`Pos.Y` is one record and
    /// `Slot1.Id`/`Slot2.Id` is two.
    ///
    /// Unless the arrays are the members - then the group is one record however many
    /// elements its columns hold, and the array-ness belongs to
    /// <see cref="RecordMember.IsArray"/> instead.
    /// </remarks>
    public bool IsArray => IsRecord
                        ? (!MembersAreArrays
                           && (RecordElementCount > 1 || (RecordElementCount == 1 && TreatAsArrayEvenIfSingleItem)))
                        : Fields.Count > 1
                          || (Fields.Count == 1 && TreatAsArrayEvenIfSingleItem)
                          || IsVariableLengthArray;

    /// <summary>
    /// Whether the length varies per row, which is true only of delimited array
    /// cells.
    ///
    /// This is what separates the two array kinds on the wire. A serial field has
    /// as many elements as it has columns, so the count is known at generation
    /// time and nothing needs to be written. A delimited cell has to carry its
    /// length, and its reader has to allocate per row.
    /// </summary>
    public bool IsVariableLengthArray => Fields.Count == 1 && FirstField is not null && FirstField.IsArray;

    /// <summary>
    /// Whether this group is an array because several columns fold into one, rather than
    /// because a column's type says so.
    /// </summary>
    /// <remarks>
    /// The two are told apart by where the array is written. A delimited cell declares
    /// `T[]` in one type cell, so that cell can carry the array's own marker as well as the
    /// element's. A folded group declares one element per column and has **no cell that
    /// stands for the array**, which is what makes the two answer the marker differently.
    /// spec/types/nullable-array-elements.md.
    /// </remarks>
    public bool IsFoldedArray => IsArray && !IsVariableLengthArray && !IsRecord;

    /// <summary>Whether a row may leave this group without a value at all.</summary>
    /// <remarks>
    /// Never true of a folded array. Its columns exist in every row, so "the array is absent"
    /// could only mean "every element is absent" - which <see cref="ElementMayBeAbsent"/>
    /// says per element, and which a trimming table already writes as a length of zero.
    ///
    /// It used to be read off the first column's `?`, and answered by that column's cell:
    /// a row whose element 0 was absent had the whole array reported as absent, taking the
    /// values of elements 1..N with it.
    /// </remarks>
    public bool RowMayBeAbsent
        => !IsRecord && !IsFoldedArray && Fields.Count > 0 && !Fields[0].IsRequired;

    /// <summary>Whether an element of this group's array may have no value.</summary>
    /// <remarks>
    /// For a folded array this is what the columns' `?` says - the cell declares one
    /// element's type, so its marker is that element's. For a delimited cell it is the
    /// marker inside the brackets, `T?[]`.
    /// </remarks>
    public bool ElementMayBeAbsent
        => !IsRecord && Fields.Count > 0
            && (IsFoldedArray ? !Fields[0].IsRequired : !Fields[0].ElementsRequired);

    /// <summary>
    /// Element type behind this field, looking through both array kinds.
    /// </summary>
    public ValueType ElementType => (Fields.Count > 0) ? ValueTypes.ElementOf(Fields[0].Type) : ValueType.None;

    /// <summary>
    /// Type of the group's columns, which the cooker has already required to agree.
    /// This is the array type itself for a delimited column - see
    /// <see cref="ElementType"/> for the type of one value.
    /// </summary>
    public ValueType Type => (Fields.Count > 0 ) ? Fields[0].Type : ValueType.None;

    /// <summary>Target side of the group's columns.</summary>
    /// <remarks>
    /// Taken from the first column whichever kind the group is. The folding requires a
    /// record group's members to agree on it, because a record half of whose members are
    /// absent from a build is not a shape any generator has.
    /// </remarks>
    public TargetSide TargetSide
    {
        get
        {
            var first = AnyField;
            return (first is not null) ? first.TargetSide : TargetSide.Both;
        }
    }

    /// <summary>
    /// First column of the group, which carries the properties shared by all of them.
    /// Null only for an empty group, which should not occur.
    /// </summary>
    /// <remarks>
    /// Scalar groups only. A record group has no single column that speaks for it, so it
    /// answers null here on purpose - a caller reaching for this on a record is asking a
    /// question that has no answer, and null surfaces that rather than hiding it behind
    /// one arbitrary member's column. Use <see cref="AnyField"/> for the properties every
    /// column of the group does share, such as target side.
    /// </remarks>
    public Field? FirstField => (!IsRecord && Fields.Count > 0) ? Fields[0] : null;

    /// <summary>
    /// Some column of this group, for the properties every column shares regardless of
    /// kind. Null only for an empty group.
    /// </summary>
    public Field? AnyField => IsRecord ? Members.FirstOrDefault()?.FirstField : FirstField;
}
