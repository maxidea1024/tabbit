using System.Collections.Generic;
using System.Linq;
using Tabbit.Models.Raw;

namespace Tabbit.Models;

/// <summary>
/// One column of a binary file: the unit a wire tag identifies and a reader skips past.
/// </summary>
/// <remarks>
/// Not the same unit as a <see cref="SerialField"/>, and that is the whole reason this
/// type exists.
///
///   * a scalar group is one wire column, however many sheet columns folded into it -
///     `Text1`/`Text2` is a single fixed-array column;
///   * a record group is one wire column **per member**, because the file stores a struct
///     of arrays where the API presents an array of structs.
///
/// Keeping the difference in one place is what stops the writer, the tag assignment and
/// the baseline check from each deciding it separately. They disagreed once already: tag
/// assignment assumed one tag per group, which is right for every table written before
/// records existed and wrong the moment one is not.
///
/// spec/types/nested-fields.md has the layout and why it is a struct of arrays.
/// </remarks>
public sealed class WireColumn
{
    /// <summary>The group this column belongs to.</summary>
    public required SerialField Group { get; init; }

    /// <summary>
    /// The member this column holds, or null when <see cref="Group"/> is a scalar one.
    /// </summary>
    public required RecordMember? Member { get; init; }

    /// <summary>
    /// Whether this column holds the first member of its record group.
    /// </summary>
    /// <remarks>
    /// Which member allocates matters only when the length is per row. All of a group's
    /// members carry the same length, so the first one to be read creates the element array
    /// and the rest check the length they read against it - a member that allocated too
    /// would throw away what the members before it had written.
    /// </remarks>
    public bool IsFirstMember { get; init; }

    /// <summary>
    /// Which member of the group this column is under, counting from zero.
    /// </summary>
    /// <remarks>
    /// A name is enough to reach a named member. An array of arrays has no name for its
    /// outer level, so the position is what a reader indexes by - `g[2][j]` where a record
    /// of arrays would write `g.M[j]`.
    /// </remarks>
    public int MemberAt { get; init; }

    /// <summary>
    /// The names from the group's first level down to this leaf.
    /// </summary>
    /// <remarks>
    /// One entry for a record one level deep, which is every table written before nesting
    /// went further. More than one is how a consumer reaches the value - `g.Position.X`
    /// rather than `g.X` - and it is what the generators build their assignment target out
    /// of, so that the read switch never has to know how deep it is.
    /// </remarks>
    public IReadOnlyList<string> MemberPath { get; init; } = System.Array.Empty<string>();

    /// <summary>
    /// How this column is named in a diagnostic and in the baseline: the group's name, or
    /// `Group.Member` for a record's.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The cells this column reads from each row, in element order. One entry for a
    /// scalar, one per element for a fixed array.
    /// </summary>
    public required IReadOnlyList<Field> Cells { get; init; }

    /// <summary>
    /// The column that carries the wire tag, which is the first of <see cref="Cells"/>.
    /// </summary>
    public Field TagCarrier => Cells[0];

    /// <summary>Whether this column's values are references stored as a target's index.</summary>
    public bool IsRef { get; init; }

    /// <summary>
    /// When <see cref="IsRef"/>, the type of the key that actually travels.
    /// </summary>
    /// <remarks>
    /// <see cref="ElementType"/> is what the generated code presents - a record - and this is
    /// what the file holds. They differ for exactly this one kind of column, which is why the
    /// two are not one property. spec/references/reference-key-types.md.
    /// </remarks>
    public ValueType RefKeyType => IsRef ? TagCarrier.RefKeyType : ElementType;

    /// <summary>
    /// Whether the file states, per row, which of this column's values are there.
    /// </summary>
    /// <remarks>
    /// True when the sheet marked the column optional. A required column has a value in
    /// every row by definition, so saying so per row would be a bit that never varies.
    ///
    /// One answer for the whole column even when it gathers several sheet columns: a group
    /// is one thing to its consumer, and half of it being optional is not a shape the API
    /// has. <see cref="Of"/> requires the members to agree.
    /// </remarks>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Whether the file states, per element, which of an array's places hold a value.
    /// </summary>
    /// <remarks>
    /// True when the sheet wrote the marker inside the brackets. Independent of
    /// <see cref="IsNullable"/>: `int?[]?` says both, and each is a bit of its own.
    ///
    /// spec/types/nullable-array-elements.md.
    /// </remarks>
    public bool HasOptionalElements { get; init; }

    /// <summary>The type of one value.</summary>
    public ValueType ElementType { get; init; }

    /// <summary>
    /// The declared type, which is the array type itself for a delimited cell. Only
    /// <see cref="ElementType"/> differs from it, and only for those.
    /// </summary>
    public ValueType Type { get; init; }

    /// <summary>
    /// Whether each row carries its own length.
    /// </summary>
    /// <remarks>
    /// True of a delimited array cell, where the length is what the author typed, and of a
    /// record member in a table that trims - there the length is the group's element count
    /// for that row, so every member of one group carries the same one.
    ///
    /// It is not "this member holds an array". Nesting an array inside a record is a shape
    /// the notation refuses; this says the number of elements is per row rather than per
    /// table.
    /// </remarks>
    public bool IsVariableLengthArray { get; init; }

    /// <summary>
    /// Whether one cell holds the whole list, rather than the group's columns holding one
    /// element each.
    /// </summary>
    /// <remarks>
    /// Both are variable length and the file writes them the same way, so
    /// <see cref="IsVariableLengthArray"/> cannot tell a writer where to read the elements
    /// from - one reads a cell that already parsed into an array, the other reads as many of
    /// the group's columns as the row filled.
    ///
    /// The question used to be asked as "is the group itself delimited", which answered for
    /// a scalar group and was wrong the moment a record's member was: `Bag.Tags` typed
    /// `string[]` is one delimited cell under a group that is not one.
    /// spec/types/set-and-map.md section 4.
    /// </remarks>
    public bool LengthIsInTheCell
        => Member is null ? Group.IsVariableLengthArray : ValueTypes.IsArray(Member.Type);

    /// <summary>
    /// Whether the member holds the elements, rather than the group holding one record per
    /// element.
    /// </summary>
    /// <remarks>
    /// **The question every generator asks about a record column.** It decides where the
    /// element number goes - `g.M[j]` against `g[j].M` - and nothing else about the read
    /// differs between the two.
    ///
    /// Two notations reach it: a group whose element number is on a level below it, and a
    /// member whose own cell is delimited. The second is what a `set` and a `map` are.
    /// spec/types/set-and-map.md section 4.
    /// </remarks>
    public bool MemberOwnsTheArray => Group.MembersAreArrays || LengthIsInTheCell;

    /// <summary>Whether every row holds the same number of elements, known at generation time.</summary>
    /// <remarks>
    /// Asks the group rather than counting cells, because a group of one element can still be
    /// an array: a layout that writes `name[0]` has said array outright, and a group that
    /// happens to have one column today would otherwise change shape the day a second is
    /// added.
    ///
    /// Counting cells here made the declaration and the read disagree for exactly that case -
    /// the member was declared `number[]` and the column written as a scalar, so the generated
    /// TypeScript assigned a number to an array and did not compile. Two places deciding the
    /// same thing, which is what this type exists to prevent.
    ///
    /// Or the member, when the group is one record whose members are arrays. The question is
    /// the same one - is there more than one element per row - and only which of the two owns
    /// the array differs. Asking the group alone read this shape's columns as scalars.
    /// </remarks>
    public bool IsFixedArray
        => !IsVariableLengthArray && (Group.IsArray || AnyLevelIsArray);

    /// <summary>Whether the column holds an array at all, however its length is decided.</summary>
    /// <remarks>
    /// What the wire asks since v107, where the only array kind carries its length per row.
    /// The distinction the two properties above draw is about where the length comes from -
    /// the sheet's column count or the row's own data - and the file stopped caring: it
    /// writes the length either way. Generated code asks this one, so that adding a column
    /// to a group does not change the shape the consumer was built against.
    /// </remarks>
    public bool IsArray => IsVariableLengthArray || IsFixedArray;

    /// <summary>
    /// Whether any level from the group down to this leaf repeats.
    /// </summary>
    /// <remarks>
    /// Asked of the whole path rather than of the leaf, because the level that repeats is
    /// not always the leaf's own: `Pos.Sub1.X` numbers the level between them, and the
    /// column still holds one cell per element of it. Asking the leaf alone read those
    /// columns as scalars.
    /// </remarks>
    public bool AnyLevelIsArray { get; init; }

    /// <summary>
    /// The wire columns of a table, in the order they are written to a file.
    /// </summary>
    /// <remarks>
    /// Group order, and within a record group, member order - so a table's layout follows
    /// the sheet and adding a member appends rather than inserts.
    /// </remarks>
    public static List<WireColumn> Of(Table table)
    {
        var result = new List<WireColumn>();

        foreach (var group in table.SerialFields)
        {
            if (!group.IsRecord)
            {
                result.Add(new WireColumn
                {
                    Group = group,
                    Member = null,
                    Name = group.Name,
                    Cells = group.Fields,
                    IsRef = group.IsRef,
                    ElementType = group.ElementType,
                    Type = group.Type,
                    // A delimited cell always carries its own length. A serial group does
                    // too once the table trims, because then the row decides how many of its
                    // columns were elements - the same rule a record group follows.
                    IsVariableLengthArray = table.IsVariableLength(group),

                    // Which of the two the group's `?` answers depends on where the array was
                    // written: a delimited cell has a marker for each, and a folded group has
                    // one cell per element and none for the array.
                    // spec/types/nullable-array-elements.md.
                    IsNullable = group.RowMayBeAbsent,
                    HasOptionalElements = group.ElementMayBeAbsent,
                });

                continue;
            }

            for (int at = 0; at < group.Members.Count; at++)
                AddLeaves(result, table, group, group.Members[at], at, new List<string>());
        }

        return result;
    }

    /// <summary>
    /// Adds the wire columns under one member: itself when it is a leaf, and its members
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// One leaf is one wire column, at every depth. That is what keeps the format out of
    /// this: a record has always been stored one column per member, so a member that is
    /// itself a record just means the path to the column is longer - the file holds the
    /// same fixed-array columns either way. See spec/types/nested-multi-level.md.
    /// </remarks>
    private static void AddLeaves(
        List<WireColumn> result, Table table, SerialField group, RecordMember member,
        int memberAt, List<string> prefix, bool arrayAbove = false)
    {
        var path = new List<string>(prefix) { member.Name };
        bool anyLevelIsArray = arrayAbove || member.IsArray;

        if (!member.IsLeaf)
        {
            foreach (var child in member.Members)
                AddLeaves(result, table, group, child, memberAt, path, anyLevelIsArray);

            return;
        }

        result.Add(new WireColumn
                {
                    Group = group,
                    Member = member,
                    // Which column allocates, so the first leaf of the group answers rather
                    // than the first member of each level. All of a group's leaves carry the
                    // same length, and only the one read first can create the element array.
                    IsFirstMember = result.Count == 0 || result[^1].Group != group,
                    MemberAt = memberAt,
                    MemberPath = path,
                    AnyLevelIsArray = anyLevelIsArray,
                    // An anonymous member's name is its index, which is the only thing it
                    // has - and it still has to be one, because this is what a column check
                    // names when a file does not match.
                    Name = $"{group.Name}{Helpers.NestedName.MemberSeparator}"
                         + string.Join(Helpers.NestedName.MemberSeparator, path),
                    Cells = member.Fields,
                    IsRef = member.IsRef,
                    ElementType = member.ElementType,
                    Type = member.Type,

                    // Two ways a leaf's length is the row's. Its own cell is delimited, so
                    // the author typed the length; or the table trims an array of elements,
                    // so the row decides how many of its columns were filled.
                    //
                    // **The first is what a `map` and a `set` inside a group are.** A member
                    // typed `int[]` holds one cell per row and that cell holds a list -
                    // which is a column the file already writes, and describing it as a
                    // scalar here handed the encoder an array where it expected one value.
                    // spec/types/set-and-map.md section 4.
                    //
                    // A group whose element number is on the group needs neither: its
                    // member's columns are the array, at the same width they would be in an
                    // array of records - the wire has stored records as one column per
                    // member all along.
                    IsVariableLengthArray = ValueTypes.IsArray(member.Type)
                        || (table.TrimTrailingArrayElements && group.IsArray),

                    // Never, for a record's member. A record is one thing to its consumer,
                    // and "the Id is there but the Count is not" is not a shape its API has.
                    // What a record array does express is how many elements a row filled in,
                    // and that is the length rather than a bitmap - see
                    // spec/types/variable-length-record-arrays.md.
                    //
                    // Members are still marked `?` in sheets that trim, because that is how a
                    // cell says it holds no value; it just does not reach the wire.
                    IsNullable = false,
                });
    }

    /// <summary>
    /// Whether a wire column's values can be absent, which the first of its columns decides.
    /// </summary>
    /// <remarks>
    /// A group is one thing to whoever reads it, so "is there a value here" has one answer
    /// per column and per row - a presence bitmap, not a presence per element.
    ///
    /// The columns behind one group may still disagree, and that is not a mistake. A sheet
    /// marking `Name[0]` required and `Name[1]` optional is saying the array must hold at
    /// least one element, which is a **length** rather than a per-element presence: an array
    /// cannot be missing its middle, so the marks collapse to a minimum count. That count is
    /// <see cref="SerialField.MinimumElementCount"/> and it is checked when the model is
    /// validated; here all that is left is whether the group can be absent altogether, and
    /// the first element answers it.
    ///
    /// This used to refuse the disagreement, and an entry could set
    /// `OnMixedOptionality: "first-column"` to get past it. Both are gone: what the sheet
    /// says is neither ambiguous nor wrong. spec/types/array-optionality.md has the reasoning
    /// and the notation it came from.
    /// </remarks>
}
