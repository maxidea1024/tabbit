using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Models.Raw;
using Tabbit.Helpers;
using Serilog;
using System.Globalization;

namespace Tabbit.Models;

/// <summary>
/// A table declared with a `~~table:Name~~` marker: a field list and its rows.
/// </summary>
public class Table
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Cooking;

    /// <summary>Cell holding the entity marker that declared this table.</summary>
    [JsonIgnore]
    public required Location Location { get; set; }

    /// <summary>Target side filtering option</summary>
    public TargetSide TargetSide { get; set; }

    /// <summary>Name exactly as written in the sheet.</summary>
    public required string RawName { get; set; }

    /// <summary>Name normalized to Pascal case, which is what generated code uses.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Base name of the file this table's rows are exported to, without a row-set suffix and
    /// without an extension.
    /// </summary>
    /// <remarks>
    /// **A contract, and the one name in the model that more than one program computes.** The
    /// exporter writes the file; the reader generated for each of fifteen languages opens it.
    /// Nothing checks that the two agree - a reader looking for the wrong name finds no file,
    /// which is a run-time failure in somebody else's program.
    ///
    /// They did not agree. Sixteen places derived it from <see cref="Name"/> and the C#
    /// accessor derived it from <see cref="RawName"/>, so a table the sheet wrote as
    /// `item_drop` was exported as `ItemDrop.tcb` and looked for as `item_drop.tcb`. No
    /// fixture had a table whose two names differed, so nothing said so.
    ///
    /// Stamped once while cooking, and read everywhere else. That is the whole point: a value
    /// several programs have to agree on is one somebody has to own.
    ///
    /// spec/naming-conventions.md.
    /// </remarks>
    public string DataFileName { get; set; } = "";

    /// <summary>
    /// Columns of the table, excluding any commented out with `#`.
    ///
    /// Narrowed by target-side filtering without the rows being touched, so a field's
    /// Index still addresses the right cell of every row.
    /// </summary>
    public List<Field> Fields { get; set; } = new List<Field>();

    /// <summary>
    /// The column rows are addressed by, or null for a table with no columns.
    /// </summary>
    /// <remarks>
    /// The first one. Every layout marks its first column as the primary index - the name is
    /// the author's, the position is not - and the places that need the key were each writing
    /// `Fields[0]` with a comment saying why. Said once here instead, so that "the primary
    /// index is column zero" is one fact rather than a repeated assumption.
    /// </remarks>
    [JsonIgnore]
    public Field? PrimaryIndexField => Fields.Count > 0 ? Fields[0] : null;

    /// <summary>
    /// Wire tags reserved by `#`-excluded columns (`#OldColor@4`).
    /// </summary>
    /// <remarks>
    /// A deleted column's tombstone. Its tag must never be handed to another column: a file
    /// written before the deletion still carries data under that tag, and a reader built
    /// after a reuse would read that data as the new column - the silent-wrong-value failure
    /// the tags exist to prevent. AssignTags refuses a duplicate against this list.
    /// </remarks>
    /// <summary>
    /// Whether the columns spell their tags out with `@N` rather than taking them from
    /// their position.
    ///
    /// It is what decides how much a schema change can be trusted: with explicit tags a
    /// column keeps its identity through a rename, a reorder and a deletion, and without
    /// them a deletion shifts every tag after it.
    /// </summary>
    public bool HasExplicitTags { get; set; }

    public List<int> ReservedTags { get; set; } = new List<int>();

    /// <summary>
    /// Rows, each a flat list of cells addressed by <see cref="Field.Index"/>.
    ///
    /// Always holds every column the sheet declared, even where the field list has
    /// been narrowed - which is why readers must go through a field's Index rather
    /// than walking a row positionally.
    /// </summary>
    public List<List<Cell>> Data { get; set; } = new List<List<Cell>>();

    /// <summary>
    /// Further sets of rows for this same table, beyond <see cref="Data"/>.
    /// </summary>
    /// <remarks>
    /// Empty for almost every table. A source whose sheets fill the same columns in more than
    /// once - so that a build can be made with one set of rows or another - says so by naming
    /// the extra sets after the table, and the pattern that recognizes those names is the
    /// source entry's to declare.
    ///
    /// They are one table. The schema is shared, the generated type is one, and what differs
    /// is the rows and the file they are written to. <see cref="Data"/> is the set with no
    /// tail on its name, so everything that reads rows without asking about sets reads that
    /// one - which is what keeps this from reaching the generators at all.
    ///
    /// spec/table-row-sets.md.
    /// </remarks>
    [JsonIgnore]
    public List<RowSet> ExtraRowSets { get; set; } = new List<RowSet>();

    /// <summary>
    /// Every set of rows this table has, the untailed one first.
    /// </summary>
    /// <remarks>
    /// What an output target walks: one file per entry, the same schema behind each. A table
    /// with no extra sets yields exactly one, so a target written against this needs no
    /// special case for the ordinary table.
    /// </remarks>
    [JsonIgnore]
    public IEnumerable<RowSet> RowSets
    {
        get
        {
            yield return new RowSet { Name = "", Rows = Data };

            foreach (var extra in ExtraRowSets)
                yield return extra;
        }
    }

    /// <summary>Description from the sheet, emitted as a doc comment.</summary>
    public required string Comment { get; set; }

    /// <summary>
    /// Whether consecutively numbered columns fold into one array-valued entry.
    /// </summary>
    /// <remarks>
    /// Off unless a recipe entry asked for it, and off is the default because a number in a
    /// column name does not say whether it means an array. `Text1`/`Text2` usually do mean one
    /// array of two; `Condition_1`, `Condition_2` and `Condition_3` of one real workbook are
    /// three different enums, and folding them is not a nicer API but a wrong one.
    ///
    /// Being wrong is quiet, which is why the author has to say. A folded group takes a name
    /// the sheet never used - `Text_array` - and several fields become one, so a consumer
    /// reads an array where the author wrote separate things.
    ///
    /// A layout whose sheets have no such convention never turns it on, whatever the recipe
    /// says: there is nothing there for the rule to be right about.
    /// </remarks>
    [JsonIgnore]
    public bool FoldSerialFields { get; set; }

    /// <summary>
    /// The fields as the exporters and generators see them, with consecutively
    /// numbered columns folded into single array-valued entries.
    ///
    /// Computed once and cached, since the folding walks every field pair.
    /// </summary>
    [JsonIgnore]
    public List<SerialField> SerialFields
    {
        get
        {
            // Record groups are collected whichever way this table folds. The two are not
            // the same decision: serial folding reads digits and can be wrong about what a
            // number in a name means, which is why a layout may switch it off, while a
            // record group is stated outright by the column's name. Switching one off used
            // to switch the other off with it, and a table's records then came out as the
            // flat columns they were written as.
            if (_serialFields is null)
            {
                _serialFields = FoldSerialFields
                    ? BuildSerialFieldsFromPlainFields(Fields)
                    : BuildRecordGroupsOnly(Fields);

                foreach (var group in _serialFields)
                    TakeRequirednessFromFirstElement(group);
            }

            return _serialFields;
        }
    }
    private List<SerialField>? _serialFields;

    /// <summary>
    /// Whether a record array drops the elements at its end that no row filled in.
    /// </summary>
    /// <remarks>
    /// Off unless a recipe entry asked for it, for the same reason as
    /// <see cref="FoldSerialFields"/>: turning it on makes arrays shorter, and shorter is
    /// quiet. A consumer indexing `Slot[2]` finds it missing on some rows and present on
    /// others, which is exactly what the feature is for and not something to be given by
    /// surprise.
    ///
    /// A layout whose sheets always trim ignores this and trims regardless. That rule lives
    /// in the layout, where the sheets it describes are.
    ///
    /// spec/variable-length-record-arrays.md has the shape and what it costs on the wire.
    /// </remarks>
    [JsonIgnore]
    public bool TrimTrailingArrayElements { get; set; }

    /// <summary>
    /// Whether an array of this table may have an empty element between two filled ones.
    /// </summary>
    /// <remarks>
    /// Carried onto the table so validation can ask without reaching back to the recipe -
    /// the same way trimming is. spec/variable-length-record-arrays.md has the rule.
    /// </remarks>
    [JsonIgnore]
    public bool AllowArrayGaps { get; set; }

    /// <summary>
    /// How many elements of a group one row actually holds.
    /// </summary>
    /// <remarks>
    /// The declared count when this table does not trim, and otherwise one past the last
    /// element the sheet gave a value for. Only the end: an empty element between two filled
    /// ones stays, because dropping it would move every element after it and then index `k`
    /// would no longer be the column named `k`.
    ///
    /// Both group kinds. A record's element is filled if any of its members is - `{Id:0}`
    /// with a Count is still an element - and a scalar's is filled if its own cell is.
    /// Trimming used to be records only, which left `Name[0]`/`Name[1]`/`Name[2]` always
    /// three long in a table whose sheets end an array where the values end.
    ///
    /// Reads <see cref="Cell.HasValue"/> rather than comparing against the type's empty
    /// value, because a cell holding `0` and a cell holding nothing both parse to zero and
    /// only one of them is the author saying zero.
    /// </remarks>
    public int ElementCountIn(SerialField group, List<Cell> row)
    {
        int declared = group.IsRecord ? group.RecordElementCount : group.Fields.Count;

        // A delimited cell carries its own length in the one cell it occupies, so there is
        // no tail of columns to trim; the count is whatever the author typed.
        if (!TrimTrailingArrayElements || !group.IsArray || group.IsVariableLengthArray)
            return declared;

        for (int element = declared - 1; element >= 0; element--)
        {
            if (IsElementFilled(group, row, element))
                return element + 1;
        }

        return 0;
    }

    /// <summary>
    /// Gives every element of an array the first one's answer about being optional.
    /// </summary>
    /// <remarks>
    /// The same rule the group already follows for its type: `Name[0]` decides, and the rest
    /// of the columns are the same field seen again. A sheet that marks only the first is not
    /// saying anything about the others - there is one column here as far as a consumer is
    /// concerned, and one answer.
    ///
    /// Without this the columns can disagree, and every reader of the model then has to pick
    /// one. They did not pick the same one: the wire took the first column's answer while the
    /// cell parser took each column's own, so a blank in `Name[1]` was read as absent by one
    /// and as an empty value by the other.
    ///
    /// A record's members each answer for themselves, because a member is its own column with
    /// its own type - which is the distinction the sheets this came from draw between marking
    /// a field and marking the fields inside an object.
    /// </remarks>
    private static void TakeRequirednessFromFirstElement(SerialField group)
    {
        if (group.IsRecord)
        {
            foreach (var member in group.Members)
                Apply(member.Fields);

            return;
        }

        Apply(group.Fields);

        static void Apply(List<Field> elements)
        {
            if (elements.Count < 2)
                return;

            for (int at = 1; at < elements.Count; at++)
                elements[at].IsRequired = elements[0].IsRequired;
        }
    }

    /// <summary>
    /// Whether a group's length is the row's rather than the table's.
    /// </summary>
    /// <remarks>
    /// True of a delimited cell, whose length is what the author typed, and of any array in
    /// a table that trims, where the row decides how many of the columns were elements.
    ///
    /// Asked of the table rather than the group because trimming is the table's answer, and
    /// asked in one place because two would drift: declaring a member and reading it have to
    /// agree about this, and when they did not the generated C declared a fixed array and
    /// then read a count into a member that did not exist.
    /// </remarks>
    public bool IsVariableLength(SerialField group)
        => group.IsVariableLengthArray || (TrimTrailingArrayElements && group.IsArray);

    /// <summary>Whether the sheet gave this element of this group a value.</summary>
    /// <remarks>
    /// Over the leaves rather than the members, because a member may be a record and then
    /// holds no columns of its own - `Rig1.Core.ItemId` gives the group one member, `Core`,
    /// whose <see cref="RecordMember.Fields"/> is empty. Reading the members worked for as
    /// long as the first of them was a leaf that every row filled, which is what kept this
    /// from being noticed: the loop returned before it reached the record.
    /// </remarks>
    public static bool IsElementFilled(SerialField group, List<Cell> row, int element)
    {
        if (!group.IsRecord)
            return row[group.Fields[element].Index].HasValue;

        foreach (var leaf in group.Leaves)
        {
            if (row[leaf.Fields[element].Index].HasValue)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The columns of this table as a binary file holds them.
    /// </summary>
    /// <remarks>
    /// Not the same list as <see cref="SerialFields"/>: a record group is one column per
    /// member. Everything that has to agree about what a wire tag identifies - the writer,
    /// the tag assignment, the baseline check - reads this rather than deciding for itself.
    /// </remarks>
    [JsonIgnore]
    public List<WireColumn> WireColumns => _wireColumns ??= WireColumn.Of(this);
    private List<WireColumn>? _wireColumns;

    /// <summary>
    /// Drops the column views derived from the field list, so the next reader rebuilds them.
    /// </summary>
    /// <remarks>
    /// Both are caches over <see cref="Fields"/>, and a wire column **snapshots** a field's
    /// type - <see cref="WireColumn.ElementType"/> is `init`, so one built before a type
    /// changed keeps the old one. Tag assignment reads the wire columns while a sheet is
    /// still being parsed, which is enough to warm both caches before the cooker has
    /// finished with the field list.
    ///
    /// No tag is lost by this. A tag lives on the field that carries it, and the rebuild
    /// reads the same fields in the same order.
    /// </remarks>
    public void InvalidateDerivedColumns()
    {
        _serialFields = null;
        _wireColumns = null;
    }

    /// <summary>
    /// Every column its own group, except that record members still gather.
    /// </summary>
    /// <remarks>
    /// For a layout that does not fold by serial number. The columns of a record are named
    /// as one - the notation says so rather than the digits implying it - so there is
    /// nothing here for the type check that folding needs, and no way for it to be wrong.
    /// </remarks>
    private List<SerialField> BuildRecordGroupsOnly(List<Field> fields)
    {
        var result = new List<SerialField>();
        var visits = new bool[fields.Count];

        for (int i = 0; i < fields.Count; i++)
        {
            if (visits[i])
                continue;

            if (fields[i].IsRecordMember)
            {
                result.Add(BuildRecordField(fields, i, visits));
                continue;
            }

            if (fields[i].IsArrayElement)
            {
                result.Add(BuildArrayField(fields, i, visits));
                continue;
            }

            result.Add(OneColumnSerialField(fields[i]));
        }

        return result;
    }

    /// <summary>
    /// Collects the elements of an array of plain values into one group.
    /// </summary>
    /// <remarks>
    /// The same thing <see cref="BuildSerialFieldsFromPlainFields"/> arrives at by reading
    /// digits out of names, for a layout that says the element number outright. So it needs
    /// no numbering pattern and cannot mistake a name for one - but it does need the type
    /// check, because an array of two different types is not an array.
    /// </remarks>
    private SerialField BuildArrayField(List<Field> fields, int index, bool[] visits)
    {
        string groupName = fields[index].GroupName!;

        var result = new SerialField
        {
            Name = groupName,
            NamePart = groupName,

            // None, so the serial-number folding never adds to this group or takes from it.
            Pattern = SerialFieldPattern.None,

            // One element is still an array here. The sheet wrote `name[0]`, which says
            // array; a group that happens to have one element today would otherwise
            // generate a scalar and change shape the day a second column is added.
            TreatAsArrayEvenIfSingleItem = true,
        };

        for (int j = index; j < fields.Count; j++)
        {
            if (visits[j])
                continue;

            var field = fields[j];
            if (!field.IsArrayElement || field.GroupName != groupName)
                continue;

            visits[j] = true;
            result.Fields.Add(field);
        }

        result.Fields.Sort((a, b) => ElementNumber(a).CompareTo(ElementNumber(b)));

        var first = result.Fields[0];
        foreach (var field in result.Fields)
        {
            if (field.Type == first.Type)
                continue;

                throw new TabbitException(field.TypeLocation,
                    Messages.Message.Of(Cooking.CookingMessages.ArrayMixesTypes,
                        ("Table", Name), ("Group", groupName),
                        ("FirstElement", ElementNumber(first)), ("FirstType", first.TypeName),
                        ("Element", ElementNumber(field)), ("Type", field.TypeName)));
        }

        return result;
    }

    /// <summary>
    /// Presents one column as its own group, for a table that does not fold.
    /// </summary>
    private static SerialField OneColumnSerialField(Field field)
    {
        // Pattern deliberately None: it is what stops NextSerialField taking anything,
        // and a group of one is what every non-folding column is anyway.
        return new SerialField
        {
            Name = field.Name,
            NamePart = field.Name,
            Pattern = SerialFieldPattern.None,
            Fields = new List<Field> { field },
        };
    }

    /// <summary>
    /// Checks whether the specified field exists. It is not case sensitive.
    /// </summary>
    public bool ContainsField(string nameToFind) => FindField(nameToFind) is not null;

    /// <summary>
    /// Get the specified field. Throws a TabbitException if not found.
    /// </summary>
    public Field GetField(string nameToFind, Location callerLocation)
    {
        var found = FindField(nameToFind);
        if (found is null)
                throw new TabbitException(callerLocation,
                    Messages.Message.Of(Cooking.CookingMessages.FieldNotFound,
                        ("Name", nameToFind), ("Table", Name)));

        return found;
    }

    /// <summary>
    /// Find the specified field. Returns null if not found.
    /// </summary>
    public Field? FindField(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return null;

        return Fields.Find(x => x.Name == fieldName);
    }

    /// <summary>
    /// Whether any row holds this value in the given column.
    /// </summary>
    public bool ContainsValueAt(int fieldIndex, object value)
    {
        if (fieldIndex < 0 || fieldIndex >= Fields.Count)
            return false;

        for (int rowIndex = 0; rowIndex < Data.Count; rowIndex++)
        {
            if (Data[rowIndex][fieldIndex].Value!.Equals(value))
                return true;
        }

        return false;
    }


    #region Serial Fields

    /// <summary>
    /// Folds consecutively numbered columns into array-valued groups.
    ///
    /// Each unclaimed field opens a group, and every later field that shares its stem
    /// and numbering pattern joins it - so the columns of a group need not be adjacent
    /// in the sheet.
    /// </summary>
    private List<SerialField> BuildSerialFieldsFromPlainFields(List<Field> fields)
    {
        var result = new List<SerialField>();

        var visits = new bool[fields.Count];
        for (int i = 0; i < visits.Length; i++)
            visits[i] = false;

        for (int i = 0; i < fields.Count; i++)
        {
            if (visits[i])
                continue;

            // A record group claims its columns by name rather than by the serial-number
            // rules, so it is decided first and separately. The two cannot be confused:
            // only the `Group.Member` notation produces a record member, and a name with
            // a `.` in it was an error before that notation existed.
            if (fields[i].IsRecordMember)
            {
                result.Add(BuildRecordField(fields, i, visits));
                continue;
            }

            var serialField = BeginSerialField(fields, i);
            if (serialField is null)
                continue;

            for (int j = i + 1; j < fields.Count; j++)
            {
                if (NextSerialField(serialField, fields, j))
                    visits[j] = true;
            }

            result.Add(serialField);
        }

        return result;
    }

    /// <summary>
    /// Collects every column of one record group into a single entry.
    /// </summary>
    /// <remarks>
    /// Members keep the order their columns first appear in the sheet, and each member's
    /// columns are ordered by <see cref="Field.GroupOrdinal"/> - so the generated record
    /// reads down the sheet and its array reads in the sheet's numbering, whatever base
    /// that numbering uses.
    /// </remarks>
    private SerialField BuildRecordField(List<Field> fields, int index, bool[] visits)
    {
        string groupName = fields[index].GroupName!;

        var result = new SerialField
        {
            Kind = SerialFieldKind.Record,
            Name = groupName,
            NamePart = groupName,
            // None, so the serial-number folding never takes anything from this group and
            // never adds anything to it. A record group's membership is settled here.
            Pattern = SerialFieldPattern.None,
        };

        var claimed = new List<Field>();

        for (int j = index; j < fields.Count; j++)
        {
            if (visits[j])
                continue;

            var field = fields[j];
            if (!field.IsRecordMember || field.GroupName != groupName)
                continue;

            visits[j] = true;
            claimed.Add(field);
        }

        // Which levels there are, and which of them repeat. Settled once for the group
        // rather than per column, because a group whose columns disagree about that is not
        // one shape read two ways - it is two shapes under one name.
        var shape = RequireOneShape(claimed, groupName);
        int repeating = RequireOneRepeatingLevel(claimed, shape, groupName);

        // Which level the element number sits on. On the group it makes an array of
        // records; anywhere below, the group is one record and the array is further in.
        // Taken from the columns because it is the notation that says it, and only the
        // layout reads the notation.
        result.MembersAreArrays = repeating > 0;

        // An anonymous level is reached by number, so the level **above** it turns into one
        // member per element: there is no word a consumer could write, so it indexes. That
        // is the one shape whose outer level becomes members rather than cells, and it is
        // held this way because the file already holds it this way - one column per outer
        // element. See spec/nested-multi-level.md.
        result.MembersAreAnonymous =
            claimed.All(field => field.NamePath!.Count == 2 && field.NamePath![1].IsAnonymous);

        result.Members.AddRange(result.MembersAreAnonymous
            ? BuildAnonymousMembers(claimed)
            : BuildMembers(claimed, level: 1, repeating: repeating));

        ValidateRecordGroup(result);

        return result;
    }

    /// <summary>
    /// Members of one level of a record group, and their members in turn.
    /// </summary>
    /// <remarks>
    /// Levels are grouped by name in the order the sheet first mentions them, so the
    /// generated record reads down the sheet. Nothing here counts the levels: the recursion
    /// stops when a level is the last one the path names, which is what makes depth a
    /// property of the columns rather than of this method.
    /// </remarks>
    private List<RecordMember> BuildMembers(List<Field> columns, int level, int repeating)
    {
        var result = new List<RecordMember>();
        var byName = new Dictionary<string, List<Field>>();

        foreach (var field in columns)
        {
            string name = field.NamePath![level].Name;

            if (!byName.TryGetValue(name, out var own))
            {
                own = new List<Field>();
                byName.Add(name, own);

                result.Add(new RecordMember
                {
                    Name = name,
                    IsArray = level == repeating,
                });
            }

            own.Add(field);
        }

        foreach (var member in result)
        {
            var own = byName[member.Name];

            // One level cannot be both. `Star1.Position` holding a value in one column and
            // a record in another is not a shape a declaration can have, and saying so here
            // beats generating a member whose type depends on which column was read last.
            var deeper = own.Where(field => field.NamePath!.Count - 1 > level).ToList();
            if (deeper.Count > 0 && deeper.Count < own.Count)
            {
                    throw new TabbitException(deeper[0].NameLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordLevelIsValueAndRecord,
                            ("Table", Name), ("Group", columns[0].GroupName),
                            ("Member", member.Name),
                            ("Ends", FieldPath.Describe(
                                own.First(f => f.NamePath!.Count - 1 == level).NamePath!)),
                            ("Deeper", FieldPath.Describe(deeper[0].NamePath!))));
            }

            // The last level the path names is where the columns live. Above it, the
            // member holds members and no columns of its own - `IsLeaf` is what says
            // which, and every consumer asks that rather than counting depth.
            if (level == own[0].NamePath!.Count - 1)
            {
                member.Fields.AddRange(own);
                member.Fields.Sort((a, b) => ElementNumber(a).CompareTo(ElementNumber(b)));
                continue;
            }

            member.Members.AddRange(BuildMembers(own, level + 1, repeating));
        }

        return result;
    }

    /// <summary>
    /// Members of an array of arrays: one per element of the outer level, named by its
    /// number because that is the only name it has.
    /// </summary>
    private static List<RecordMember> BuildAnonymousMembers(List<Field> columns)
    {
        var result = new List<RecordMember>();
        var byOuter = new Dictionary<int, RecordMember>();

        foreach (var field in columns)
        {
            int outer = field.NamePath![0].Index ?? 0;

            if (!byOuter.TryGetValue(outer, out var member))
            {
                member = new RecordMember
                {
                    Name = outer.ToString(CultureInfo.InvariantCulture),
                    IsArray = true,
                    IsAnonymous = true,
                };

                byOuter.Add(outer, member);
                result.Add(member);
            }

            member.Fields.Add(field);
        }

        foreach (var member in result)
            member.Fields.Sort((a, b) => ElementNumber(a).CompareTo(ElementNumber(b)));

        result.Sort((a, b) =>
            int.Parse(a.Name, CultureInfo.InvariantCulture)
               .CompareTo(int.Parse(b.Name, CultureInfo.InvariantCulture)));

        return result;
    }

    /// <summary>
    /// The element number a column carries, which is the number on the one level that
    /// repeats.
    /// </summary>
    private static int ElementNumber(Field field)
    {
        if (field.NamePath is null)
            return 0;

        for (int level = field.NamePath!.Count - 1; level >= 0; level--)
        {
            if (field.NamePath![level].IsIndexed)
                return field.NamePath![level].Index!.Value;
        }

        return 0;
    }

    /// <summary>
    /// Requires every column of a group to number the same levels, and returns the first
    /// column's path as the shape to describe the group by.
    /// </summary>
    /// <remarks>
    /// Names are not required to agree, and that is the point: `Pos.X` and `Pos.Y` are
    /// siblings, and a record may hold a value beside a record. What has to agree is where
    /// the element number sits, because that is what says which shape the group is.
    /// </remarks>
    private List<FieldPathStep> RequireOneShape(List<Field> columns, string groupName)
    {
        var shape = columns[0].NamePath!;

        foreach (var field in columns)
        {
            if (FieldPath.SameRepeatingLevels(shape, field.NamePath!))
                continue;

            throw new TabbitException(field.NameLocation,
                Messages.Message.Of(Cooking.CookingMessages.RecordNumbersTwoLevels,
                    ("Table", Name), ("Group", groupName),
                    ("Shape", FieldPath.Describe(shape)),
                    ("Other", FieldPath.Describe(field.NamePath!))));
        }

        return shape;
    }

    /// <summary>
    /// Requires at most one level of a group to repeat, and returns which one.
    /// </summary>
    /// <remarks>
    /// Two repeating levels would mean the wire holds one column per combination of the
    /// two, which is a layout the format can express but nothing generates yet - so it is
    /// an explicit error rather than a file no reader agrees about. The one exception is an
    /// array of arrays, whose outer level becomes members: there the two numbers are the
    /// member and its element, which is how the file has always held a record.
    ///
    /// Returns -1 when no level repeats, which is a single record.
    /// </remarks>
    private int RequireOneRepeatingLevel(List<Field> columns, List<FieldPathStep> shape, string groupName)
    {
        var repeating = new List<int>();

        for (int level = 0; level < shape.Count; level++)
        {
            if (shape[level].IsIndexed)
                repeating.Add(level);
        }

        if (repeating.Count <= 1)
            return (repeating.Count == 1) ? repeating[0] : -1;

        // An array of arrays: the outer level is numbered and the inner has no name, so the
        // outer becomes one member per element and the inner becomes that member's columns.
        if (repeating.Count == 2 && repeating[0] == 0 && repeating[1] == 1 && shape[1].IsAnonymous)
            return 1;

            throw new TabbitException(columns[0].NameLocation,
                Messages.Message.Of(Cooking.CookingMessages.RecordNumbersTooManyLevels,
                    ("Table", Name), ("Group", groupName), ("Count", repeating.Count),
                    ("Shape", FieldPath.Describe(shape)),
                    ("Separator", Helpers.NestedName.MemberSeparator)));
    }

    /// <summary>
    /// Checks the two things a record group has to satisfy for the generated code to be
    /// writable at all.
    /// </summary>
    private void ValidateRecordGroup(SerialField group)
    {
        var leaves = group.Leaves.ToList();
        var first = leaves[0];

        // A group deeper than one level carries its element number on the group, and not
        // further in. Both are shapes the wire holds, but the second multiplies the cases
        // every generator has to write - the array would sit below a record rather than above
        // it - and no measured sheet uses it. So it is refused by name rather than supported
        // in some languages and not others. spec/nested-multi-level.md.
        if (group.MembersAreArrays)
        {
            var deep = group.Members.Find(member => !member.IsLeaf);
            if (deep is not null)
            {
                    throw new TabbitException(deep.FirstField?.NameLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordNumbersInnerAndNests,
                            ("Table", Name), ("Group", group.Name), ("Member", deep.Name),
                            ("Separator", Helpers.NestedName.MemberSeparator),
                            ("Inner", deep.Members[0].Name)));
            }
        }

        // An anonymous level is reached by number rather than by name, so a reference in one
        // has nowhere to keep the key it came off the wire with: the stored key and the
        // resolution flag are declared beside the member, and here there is no member name to
        // declare them beside. Refused rather than given a name this tool invented, which
        // consumers would then have to learn. spec/references-in-records.md.
        if (group.MembersAreAnonymous)
        {
            var referencing = leaves.Find(member => member.IsRef);
            if (referencing is not null)
            {
                    throw new TabbitException(referencing.FirstField!.DetailTypeLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordNumberedLevelReferences,
                            ("Table", Name), ("Group", group.Name),
                            ("Element", referencing.Name),
                            ("Separator", Helpers.NestedName.MemberSeparator)));
            }
        }

        foreach (var member in leaves)
        {
            // Every member present in every element. A hole would generate a record with
            // a value that nothing ever writes - which reads as a default rather than as
            // the missing column it is.
            if (member.Fields.Count != first.Fields.Count)
            {
                    throw new TabbitException(member.FirstField!.NameLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordMemberElementCountsDiffer,
                            ("Table", Name), ("Group", group.Name),
                            ("FirstCount", first.Fields.Count), ("First", first.Name),
                            ("Count", member.Fields.Count), ("Member", member.Name)));
            }

            // And the elements lined up: element k of one member has to be element k of
            // the next, or the record built from position k mixes two of the sheet's rows
            // of columns.
            for (int i = 0; i < member.Fields.Count; i++)
            {
                if (ElementNumber(member.Fields[i]) == ElementNumber(first.Fields[i]))
                    continue;

                    throw new TabbitException(member.Fields[i].NameLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordNumberedInconsistently,
                            ("Table", Name), ("Group", group.Name), ("First", first.Name),
                            ("FirstElement", ElementNumber(first.Fields[i])),
                            ("Member", member.Name),
                            ("Element", ElementNumber(member.Fields[i]))));
            }

            // Which level carries the element number is settled for the whole group before
            // this runs - see `RequireOneShape`. Half a group written `G["M"][0]` and half
            // `G[0]["M"]` is refused there, as a group whose columns describe two shapes,
            // rather than here per column.

            // And one type per member, across every element of it. The file states one
            // element type per column and a column is one member, so two elements of
            // differing types would be written at two widths under one declaration - and a
            // reader that trusts the declaration then walks off the end of the block.
            //
            // Found by a layout that picks each numeric column's type from its own values:
            // `Effect1.Val` fitting an int while `Effect2.Val` needed a double gave one
            // member two types, and the file it wrote could not be read back. Deciding a
            // narrower type per column is a reasonable thing for a layout to do; doing it
            // per column of one member is not, and this is where that gets said.
            foreach (var field in member.Fields)
            {
                if (field.Type == member.FirstField!.Type)
                    continue;

                    throw new TabbitException(field.TypeLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordMemberTypesDiffer,
                            ("Table", Name), ("Group", group.Name), ("Member", member.Name),
                            ("FirstType", member.FirstField!.TypeName),
                            ("FirstElement", ElementNumber(member.FirstField)),
                            ("Type", field.TypeName), ("Element", ElementNumber(field))));
            }

            // Target side belongs to the record, not to its members. Half a record in one
            // build is not a shape any generator has.
            if (member.FirstField!.TargetSide != first.FirstField!.TargetSide)
            {
                    throw new TabbitException(member.FirstField!.TargetSideLocation,
                        Messages.Message.Of(Cooking.CookingMessages.RecordMixesTargetSides,
                            ("Table", Name), ("Group", group.Name),
                            ("First", first.Name), ("FirstSide", first.FirstField!.TargetSide),
                            ("Member", member.Name),
                            ("Side", member.FirstField!.TargetSide)));
            }
        }
    }

    /// <summary>
    /// Opens a group around one field, which is also the answer for a column that
    /// turns out to have no siblings.
    /// </summary>
    private SerialField BeginSerialField(List<Field> fields, int index)
    {
        var field = fields[index];
        var fieldName = field.Name;

        var result = new SerialField
        {
            Name = fieldName,
            NamePart = Helper.StripNumber(fieldName),
            Pattern = GetSerialFieldPattern(fieldName),
            Fields = new List<Field>()
        };
        result.Fields.Add(field);

        return result;
    }

    /// <summary>
    /// Adds a field to a group if it belongs there.
    /// </summary>
    /// <returns>True when the field was taken, so the caller can mark it claimed.</returns>
    private bool NextSerialField(SerialField output, List<Field> fields, int index)
    {
        if (output.Pattern == SerialFieldPattern.None)
            return false;

        if (output.Fields.Count == 0)
            return false;

        var field = fields[index];
        var fieldName = field.Name;

        // Two delimited-array columns must not fold into one serial field: the
        // result would be an array of arrays, which no exporter or generator has
        // a shape for.
        if (field.IsArray || output.FirstField!.IsArray)
            return false;

        string namePart = Helper.StripNumber(fieldName);
        if (namePart != output.NamePart)
            return false;

        var pattern = GetSerialFieldPattern(fieldName);
        if (pattern != output.Pattern)
            return false;

        string numberPart = Helper.ExtractNumber(fieldName);
        int number = int.Parse(numberPart, CultureInfo.InvariantCulture);
        string prevNumberPart = Helper.ExtractNumber(output.Fields[^1].Name);
        int prevNumber = int.Parse(prevNumberPart, CultureInfo.InvariantCulture);
        // Strictly less than, not less than or equal: two columns cannot carry the
        // same number, because duplicate field names are rejected before this runs.
        if (number < prevNumber)
        {
            // A warning rather than an error: the columns still fold into an array,
            // just in an order the sheet does not read in. Whether that is a mistake
            // depends on intent, so it is reported and left to the author.
            //
            // `{field.Name}`, not `field.Name` - the placeholder used to be written
            // without braces, so every one of these warnings named the literal text
            // "field.Name" instead of the column it was about.
            Log.Warning(
                $"Columns folded into an array are numbered out of order in table `{Name}`.\n" +
                $"`{field.Name}` follows `{output.Fields[^1].Name}` but carries a lower number, " +
                $"so the array elements will not be in sheet order.\n" +
                $"    at {field.NameLocation}");
        }

        var expectedType = output.Fields[0].Type;
        if (field.Type != expectedType)
        {
            throw new TabbitException(field.NameLocation,
                Messages.Message.Of(Cooking.CookingMessages.FoldedColumnTypesDiffer,
                    ("Index", field.Index), ("Type", expectedType)));
        }

        if (output.Fields.Count == 1)
            output.Name = output.NamePart + "_array";

        output.Fields.Add(field);

        return true;
    }

    /// <summary>
    /// Classifies where a column name's sequence number sits, or reports that it has
    /// no usable one.
    /// </summary>
    private SerialFieldPattern GetSerialFieldPattern(string name)
    {
        if (string.IsNullOrEmpty(name))
            return SerialFieldPattern.None;

        // If there is no number pattern or more than once, it is not recognized.
        // ex) "item", "item01_1"
        int toggles = 0;
        bool digit = false;
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
            {
                if (!digit)
                    toggles++;
                digit = true;
            }
            else
            {
                digit = false;
            }
        }

        if (toggles == 0 || toggles > 1)
            return SerialFieldPattern.None;

        // Trailing when the name ends in the digit run, as in `Text1`.
        //
        // Only the last character is examined. This used to scan backwards over the
        // whole name and report TrailingNumber on finding a digit anywhere, which -
        // since reaching here means there is exactly one digit run - was always. So
        // `Item1Bonus` was classified as trailing and MiddleNumber was unreachable.
        if (char.IsDigit(name[name.Length - 1]))
            return SerialFieldPattern.TrailingNumber;

        // Otherwise the digits sit in the middle, as in `Item1Bonus`. A column name
        // cannot begin with a digit - the identifier check upstream rejects that -
        // so anything left is a middle run.
        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
                return SerialFieldPattern.MiddleNumber;
        }

        return SerialFieldPattern.None;
    }
    #endregion
}
