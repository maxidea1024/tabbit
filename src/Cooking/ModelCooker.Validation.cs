using System.Collections.Generic;
using System.Linq;
using Serilog;
using Tabbit.Models;
using Tabbit.Recipe;
using Tabbit.Targets;
using Tabbit.Messages;
using System.Threading;

namespace Tabbit.Cooking;

public partial class ModelCooker
{
    /// <summary>
    /// Checks a cooked model and reports everything wrong with it in one go.
    ///
    /// This ran nowhere at all until now - the method existed but nothing called
    /// it, and its uniqueness loop skipped precisely the fields it was meant to
    /// check. Catching this class of mistake statically is the point of the tool,
    /// so the checks are back, corrected, and wired into Cook.
    /// </summary>
    /// <summary>
    /// The folders `asset` columns are checked against, or null when the recipe named none.
    /// </summary>
    /// <remarks>
    /// Held on the cooker for the length of one cook rather than passed down through every
    /// check, because the only thing that reads it is a leaf - one cell against one folder -
    /// and threading it through four call sites that have no interest in it is worse than
    /// this. Set at the top of <see cref="ValidateModel"/> and read nowhere else.
    /// </remarks>
    private AssetRoots _assets = null!;

    /// <summary>
    /// The `asset` columns whose kind the recipe has no folder for, so each is reported once.
    /// </summary>
    /// <remarks>
    /// A misspelled kind is a fact about the column, and a table of 90,000 rows would
    /// otherwise state it 90,000 times - which buries every other finding in the run.
    /// </remarks>
    /// <remarks>
    /// Concurrent because the per-table checks run in parallel and this is written from
    /// inside that loop. A plain <c>HashSet</c> throws there - intermittently, and only once
    /// the run is large enough for two tables to reach a misspelled kind at the same moment.
    /// <c>TryAdd</c> keeps "reported once" exact: only one caller sees true.
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Field, byte>
        _unknownKinds = new();

    /// <summary>
    /// How much of the "this id is a row of one of these tables" check actually ran.
    /// </summary>
    /// <remarks>
    /// Reported beside the findings because a zero on its own does not distinguish "the
    /// sheets are clean" from "the check never ran" - and this check is one whose columns
    /// go unjudged for a legitimate reason, so the two really do have to be told apart.
    /// </remarks>
    /// <remarks>
    /// Added to with <see cref="System.Threading.Interlocked"/>, because the tables that count
    /// into these are checked at the same time. A `++` on a shared field loses increments
    /// under a race, and a coverage number that is quietly low is worse than none - the whole
    /// reason these are reported is to tell "nothing was wrong" from "nothing was checked".
    /// </remarks>
    private int _checkedReferencedTables;
    private int _uncheckedReferencedTables;
    private int _rowsAgainstReferencedTables;

    private void ValidateModel(Model model, RecipeModel recipeModel, TargetSide requested, Diagnostics diagnostics)
    {
        // Scanned once for the whole run, before any cell asks. Throws on a root that is not
        // there, which is a recipe mistake rather than a finding about the data.
        _assets = AssetRoots.From(recipeModel.Assets)!;

        ReportAssetSetup(model, diagnostics);

        // Before the checks about what the cells hold, because this one is about the model's
        // own vocabulary: a name written two ways is what makes the reports below name two
        // things where the sheets meant one. Read here rather than earlier so a misspelled
        // setting is reported alongside the rest instead of stopping the run on its own.
        ValidateNaming(model, NamingRules.From(recipeModel.Naming), diagnostics);

        // Checked one table at a time, at the same time.
        //
        // **A table's checks read that table and the tables it points at, and write nothing to
        // either.** What they produce is reports, and each table's go into a collector of its
        // own - which is then absorbed below in the model's own table order, so the report
        // reads exactly as it did when this loop was sequential. That order is the whole of
        // what a reader of the report relies on, and it is not something a thread schedule
        // should get to decide. spec/ops/conversion-time.md section 5.
        var perTable = new Diagnostics[model.Tables.Count];

        System.Threading.Tasks.Parallel.For(0, model.Tables.Count, at =>
        {
            var table = model.Tables[at];
            var found = new Diagnostics { PromoteWarnings = diagnostics.PromoteWarnings };

            perTable[at] = found;

            // Once per set of rows the table has. A table with one set - nearly all of them -
            // runs this once, and every rule below asks its questions of the set rather than
            // of the table, because a second set is data the first knows nothing about.
            // spec/layout/table-row-sets.md.
            foreach (var rowSet in table.RowSets)
            {
                ValidateIndexUniqueness(table, rowSet, found);
                ValidateCompositeKeyUniqueness(table, rowSet, found);
                ValidateReferences(model, table, rowSet, found);
                ValidateReferencedTables(model, table, rowSet, found);
                ValidateColumnConstraints(table, rowSet, found);
                ValidateArrayGaps(table, rowSet, found);
                ValidateRequiredInRecord(table, rowSet, found);
            }
        });

        foreach (var found in perTable)
            diagnostics.Absorb(found);

        ReportReferencedTableCoverage();

        ValidateTargetSideReachability(model, recipeModel, requested, diagnostics);
    }

    /// <summary>
    /// How many columns the "one of these tables has this id" check judged, and how many it
    /// could not.
    /// </summary>
    /// <remarks>
    /// Said even when nothing was found, which is the point: this check reports nothing both
    /// when the sheets are right and when no column reached it, and those are opposite
    /// facts. The unchecked count is the one to watch - it means the build is missing a
    /// table some declaration names.
    /// </remarks>
    private void ReportReferencedTableCoverage()
    {
        if (_checkedReferencedTables == 0 && _uncheckedReferencedTables == 0)
            return;

        Log.Information(
            $"Checked {_checkedReferencedTables} column(s) against the tables their sheet "
            + $"names, over {_rowsAgainstReferencedTables} row(s). "
            + $"{_uncheckedReferencedTables} column(s) went unchecked because this build does "
            + $"not contain every table they name.");
    }

    /// <summary>
    /// An array element left empty between two filled ones, which is a mistake unless the
    /// source says otherwise.
    /// </summary>
    /// <remarks>
    /// Almost always a row whose `Slot2` was cleared and whose `Slot3` was left alone. It
    /// travels today as an array whose middle element holds the type's empty value, and a
    /// consumer indexing into it cannot tell "absent" from "zero" - so the sheet says one
    /// thing and the data says another, quietly.
    ///
    /// **After trimming, not before.** Where a table trims, the blanks past the last value
    /// are outside the array and are not gaps; where it does not, the array is as long as
    /// the columns and every blank is inside it.
    ///
    /// Delimited cells are not checked: one cell carries its own list, so a gap there is a
    /// value the author typed rather than a column they left alone.
    ///
    /// spec/types/variable-length-record-arrays.md has the rule and `AllowArrayGaps`.
    /// </remarks>
    private static void ValidateArrayGaps(Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        if (table.AllowArrayGaps)
            return;

        foreach (var group in table.SerialFields)
        {
            if (!group.IsArray || group.IsVariableLengthArray)
                continue;

            // A column that says its elements may be absent has said what a hole means, and
            // it is not a mistake. What this check exists for is the array whose elements are
            // required, where a hole reads as the type's empty value and nothing in the sheet
            // asked for it. spec/types/nullable-array-elements.md.
            if (group.ElementMayBeAbsent)
                continue;

            foreach (var row in rowSet.Rows)
            {
                int count = table.ElementCountIn(group, row);

                for (int element = 0; element < count; element++)
                {
                    if (Table.IsElementFilled(group, row, element))
                        continue;

                    // The first field of the element is where the author would look, whether
                    // the group is a record or a scalar one. The first *leaf*, because a
                    // member that is itself a record holds no columns to point at.
                    var cell = row[(group.IsRecord
                        ? group.Leaves.First().Fields[element]
                        : group.Fields[element]).Index];

                    diagnostics.Error(cell.RawCell?.Location,
                        Message.Of(CookingMessages.ArrayGap,
                            ("Table", table.Name), ("Group", group.Name), ("Element", element)));
                }
            }
        }
    }

    /// <summary>
    /// A record member the sheet marked required inside its object, left empty in a record
    /// that exists.
    /// </summary>
    /// <remarks>
    /// **A validation rule rather than a shape the wire has to carry.** The sheets that
    /// declare this are saying what must be true, not what must be representable - and
    /// enforcing it here means "the Id is there and the Count is not" never reaches a file,
    /// so there is nothing left for the format to express. That is the whole of why this
    /// costs no format version and no generator change.
    ///
    /// A record exists when **any** of its members has a value. The same definition the
    /// trimming of a record array uses, because the two ask the same question - one decides
    /// whether to drop the element and the other whether to refuse it.
    ///
    /// spec/types/record-member-optionality.md.
    /// </remarks>
    /// <remarks>
    /// Internal rather than private so a layout test can hand it one parsed table: what is
    /// worth pinning is the rule reading a sheet, and running the whole cooker to reach it
    /// would test the cooker instead.
    /// </remarks>
    internal static void ValidateRequiredInRecord(Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        foreach (var group in table.SerialFields)
        {
            if (!group.IsRecord)
            {
                // A column outside any record, marked required inside one. There is no object
                // for it to be inside, so what the mark can mean is the one thing left: the
                // column must hold a value.
                //
                // **Read that way rather than refused.** It was refused, on the grounds that
                // the mark belonged on the `required` row instead - but the checker these
                // sheets are written for does not draw that line. Its helper takes the row as
                // the object and reports a column of it that holds nothing, so a flat column
                // marked this way is checked there exactly as `required` would be. Refusing
                // it here made 105 marks across 22 tables an error in a corpus where they are
                // not one, and those tables export without complaint today.
                foreach (var field in group.Fields)
                {
                    if (!field.Constraints.RequiredInRecord)
                        continue;

                    foreach (var row in rowSet.Rows)
                    {
                        if (field.Index < row.Count && !row[field.Index].HasValue)
                        {
                            diagnostics.Error(row[field.Index].RawCell?.Location,
                                Message.Of(CookingMessages.RequiredInObjectOutsideObject,
                                    ("Table", table.Name), ("Field", field.Name)));
                        }
                    }
                }

                continue;
            }

            foreach (var member in group.Members)
            {
                if (!member.Leaves.Any(leaf => leaf.Fields.Any(f => f.Constraints.RequiredInRecord)))
                    continue;

                foreach (var row in rowSet.Rows)
                {
                    int count = table.ElementCountIn(group, row);

                    for (int element = 0; element < count; element++)
                    {
                        if (!Table.IsElementFilled(group, row, element))
                            continue;

                        foreach (var leaf in member.Leaves)
                        {
                            var field = leaf.Fields[element];

                            if (!field.Constraints.RequiredInRecord)
                                continue;

                            if (row[field.Index].HasValue)
                                continue;

                            diagnostics.Error(row[field.Index].RawCell?.Location,
                                Message.Of(CookingMessages.RecordMemberRequiredEmpty,
                                    ("Table", table.Name), ("Group", group.Name),
                                    ("Element", element), ("Member", member.Name)));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Says what the asset check is about to do, or why it is not going to.
    /// </summary>
    /// <remarks>
    /// A column typed `asset` in a recipe with no roots is the case worth a word. Nothing is
    /// wrong - declaring the columns before wiring up the content tree is a reasonable order
    /// to work in - but silence there reads exactly like a check that ran and found nothing,
    /// and that is the reading that gets a project shipping broken references.
    /// </remarks>
    private void ReportAssetSetup(Model model, Diagnostics diagnostics)
    {
        int columns = model.Tables
            .SelectMany(table => table.Fields)
            .Count(field => field.Role == StringRole.Asset);

        if (columns == 0)
            return;

        if (_assets is null)
        {
            diagnostics.Warn(null,
                Message.Of(CookingMessages.AssetNoRoots, ("Columns", columns)));
            return;
        }

        if (_assets.OnMissingSeverity is null)
        {
            diagnostics.Info(null,
                Message.Of(CookingMessages.AssetCheckIgnored, ("Columns", columns)));
        }
    }

    /// <summary>
    /// Every field acting as an index must hold distinct values.
    ///
    /// The first column is always an index; further ones opt in with a `*` prefix
    /// on the field name. The previous version skipped a field when
    /// `field.Indexing` was set, the exact inverse of what it wanted, so it only
    /// ever examined the columns where duplicates are perfectly legal.
    /// </summary>
    private void ValidateIndexUniqueness(Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        foreach (var field in table.Fields)
        {
            if (!field.Indexing)
                continue;

            if (!Models.ValueTypes.CanBeIndexKey(field.Type, out string? why))
            {
                // `Why` is still a sentence built elsewhere - ValueTypes hands it over as
                // text. It reads as English inside a translated sentence until that call
                // site gets an id of its own, which is the next thing owed here.
                diagnostics.Error(field.TypeLocation,
                    Message.Of(CookingMessages.IndexTypeUnusable,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Type", field.TypeName), ("Why", why)));
                continue;
            }

            // An optional index would let several rows share the type's empty value, which
            // the uniqueness check below would then report as duplicates. Saying it here
            // names the mistake - the `?` - instead of the symptom two steps later.
            if (!field.IsRequired)
            {
                diagnostics.Error(field.TypeLocation,
                    Message.Of(CookingMessages.IndexOptional,
                        ("Table", table.Name), ("Field", field.Name)));
                continue;
            }

            // Keyed lookup rather than comparing every row against every other.
            // The original shape was quadratic, which on a table of any size is
            // the slowest thing the converter does.
            var seen = new Dictionary<object, Location>();

            foreach (var row in rowSet.Rows)
            {
                var cell = row[field.Index];

                // Values are boxed, so equality has to go through Equals. A
                // reference comparison reports every boxed int as distinct and
                // therefore never finds a duplicate at all.
                if (seen.TryGetValue(cell.Value!, out var firstLocation))
                {
                    diagnostics.Error(cell.RawCell.Location,
                        Message.Of(CookingMessages.IndexDuplicate,
                            ("Table", table.Name), ("Field", field.Name),
                            ("Value", cell.Value), ("First", firstLocation)));
                    continue;
                }

                seen.Add(cell.Value!, cell.RawCell!.Location);
            }
        }
    }

    /// <summary>
    /// Every declared key holds distinct values, taking its columns together.
    /// </summary>
    /// <remarks>
    /// **The combination, not the columns.** `stage,slot` allows the same stage on many rows
    /// and the same slot on many rows; what it does not allow is the pair repeating. A check
    /// per column would refuse data the key permits, and no check at all would let a lookup
    /// find whichever of two rows it happened to reach first.
    ///
    /// Single-column keys are left to `ValidateIndexUniqueness`, which already holds every
    /// `Indexing` column to this and reports at the cell. spec/layout/primary-layout.md section 3.5.
    /// </remarks>
    private void ValidateCompositeKeyUniqueness(Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        foreach (var key in table.Keys)
        {
            if (!key.IsComposite)
                continue;

            var columns = new List<Field>();

            foreach (string name in key.FieldNames)
            {
                var field = table.Fields.Find(column => column.Name == name);

                if (field is null)
                    continue;

                // Each component is held to what a key column has always had to be. A
                // composite key narrows what has to be unique; it does not widen what may
                // sit in the columns.
                if (!Models.ValueTypes.CanBeIndexKey(field.Type, out string? why))
                {
                    diagnostics.Error(field.TypeLocation,
                        Message.Of(CookingMessages.IndexTypeUnusable,
                            ("Table", table.Name), ("Field", field.Name),
                            ("Type", field.TypeName), ("Why", why)));
                    continue;
                }

                if (!field.IsRequired)
                {
                    diagnostics.Error(field.TypeLocation,
                        Message.Of(CookingMessages.IndexOptional,
                            ("Table", table.Name), ("Field", field.Name)));
                    continue;
                }

                columns.Add(field);
            }

            if (columns.Count != key.FieldNames.Count)
                continue;

            var seen = new Dictionary<string, Location>(System.StringComparer.Ordinal);

            foreach (var row in rowSet.Rows)
            {
                var values = columns
                    .Select(column => row[column.Index].Value?.ToString() ?? "")
                    .ToList();

                // **Each part carries its own length.** A plain separator would let two
                // different combinations collide into one string - `("a b", "c")` and
                // `("a", "b c")` joined by a space are the same text - and a key check that
                // reports a duplicate nobody wrote is worse than one that misses.
                string combination = string.Concat(
                    values.Select(value => value.Length.ToString() + ":" + value));

                var at = row[columns[0].Index].RawCell.Location;

                if (seen.TryGetValue(combination, out var first))
                {
                    diagnostics.Error(at,
                        Message.Of(CookingMessages.CompositeKeyDuplicate,
                            ("Table", table.Name), ("Key", key.ToString()),
                            ("Value", string.Join(", ", values)), ("First", first)));
                    continue;
                }

                seen.Add(combination, at);
            }
        }
    }

    /// <summary>
    /// Checks that every foreign reference points at something that exists: the
    /// table, the field within it, and a row carrying the referenced key.
    /// </summary>
    private void ValidateReferences(Model model, Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        // Which array element each column is, for the same reason the constraint walk goes
        // by group: in a table that trims, the columns past a row's last value are not empty
        // cells but absent elements, and a check that walks the flat columns would hold every
        // short row's tail against rules meant for values.
        var arrayElements = new Dictionary<Field, (SerialField Group, int Element)>();
        foreach (var group in table.SerialFields)
        {
            if (!group.IsArray)
                continue;

            foreach (var member in Columns(group))
            {
                for (int at = 0; at < member.Count; at++)
                    arrayElements[member[at]] = (group, at);
            }
        }

        foreach (var field in table.Fields)
        {
            if (!field.IsRef)
                continue;

            // A reference that failed to resolve has already been reported by
            // SolveTableCrossReferencings, which knows exactly which link in the
            // chain broke. Repeating it here would just say the same thing twice.
            if (field.ResolvedRefTable is null)
                continue;

            // A reference carries the target's primary index whatever its type, so there is
            // no list of accepted key types here - being able to tell rows apart is the one
            // rule, and the index itself is where it is asked. Refusing anything but `int32`
            // is what used to happen. spec/references/reference-key-types.md.
            //
            // `enum` is the one exception, and it is a gap rather than a rule: an enum's
            // value travels zig-zag encoded rather than at a fixed width, so the read is the
            // one call the shared table has no entry for - by design, since each language
            // spells its own enum. Said here rather than left to the generators, where
            // it would surface as whichever of them a project reached first.
            // **A reference carries one key value, and a composite primary key is not one.**
            // The target's identity is a combination spread over several columns, and there
            // is no cell shape that holds it - so this is refused rather than guessed at, and
            // the form is decided when a need for it is measured. Secondary keys are not
            // involved: a reference points at the primary one. spec/layout/primary-layout.md 3.5.
            if (field.ResolvedRefTable.Keys.Find(key => key.IsPrimary) is { IsComposite: true } composite)
            {
                diagnostics.Error(field.DetailTypeLocation,
                    Message.Of(CookingMessages.ReferenceCompositeKey,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Target", field.ResolvedRefTable.Name), ("Key", composite.ToString())));
                continue;
            }

            if (field.RefKeyType == Models.ValueType.Enum)
            {
                diagnostics.Error(field.DetailTypeLocation,
                    Message.Of(CookingMessages.ReferenceEnumKey,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Target", field.ResolvedRefTable.Name)));
                continue;
            }

            ValidateReferencedKeysExist(table, rowSet, field, field.ResolvedRefTable, arrayElements, diagnostics);
        }
    }

    /// <summary>
    /// Checks the referencing cells themselves: whatever a reference column holds
    /// has to match a row in the target table, and a column that says it must hold
    /// something has to have been filled in.
    /// </summary>
    private void ValidateReferencedKeysExist(
        Table table, RowSet rowSet, Field field, Table foreignTable,
        Dictionary<Field, (SerialField Group, int Element)> arrayElements, Diagnostics diagnostics)
    {
        // Whether a row filled the cell in at all is a question about this column and
        // nothing else, so it is asked before the target is read. Asked after, a target
        // with no columns takes the empty cells out with it through the return below -
        // and those used to be stopped by the value parser, so letting them past here
        // would be a refusal quietly lost. spec/references/reference-optionality.md.
        // An empty cell in a column typed `foreign Item[]` is an empty array, which is what
        // an empty cell means for every other array type - so the question this asks does not
        // arise. What a row with no targets writes is nothing, not `-`: the mark says one
        // element has no value, and there is no element here to say it of.
        // spec/types/polymorphism.md section 4.
        if (!field.IsArray)
        {
            bool isArrayElement = arrayElements.TryGetValue(field, out var place);

            foreach (var row in rowSet.Rows)
            {
                var cell = row[field.Index];
                if (cell.HasValue)
                    continue;

                // Past the row's last value in a table that trims, this column is not an
                // empty cell but an element the row does not have - the same answer the
                // constraint checks and the exporters give.
                if (isArrayElement && place.Element >= table.ElementCountIn(place.Group, row))
                    continue;

                // And a column this row's variant does not have, which is the same distinction
                // one level over: not a cell nobody filled in but a column that is not this
                // row's at all. A polymorphic group's columns are every variant's members side
                // by side, so every row has blank ones by design - and a reference among them
                // would otherwise be reported as a blank the author left.
                // spec/types/polymorphism.md section 5.2.
                if (!IsThisRowsVariantColumn(table, row, field))
                    continue;

                // A blank cell is refused whether or not the column allows absence: it is a
                // cell nobody filled in, and a row that points at nothing says so with `-`.
                // The reading is not refused where it happens, because whether an empty cell
                // is a cell at all is the question the two skips above answer.
                //
                // Read off the cell's own text rather than a flag, so that the one absence
                // nobody wrote - a column another set of this table's rows does not have -
                // is not reported as a blank the author left. Its cell borrows the row's
                // first location, which holds the index and is never empty.
                if (string.IsNullOrEmpty((cell.RawCell?.Value ?? "").Trim()))
                {
                    diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                        Message.Of(CookingMessages.ReferenceBlank,
                            ("Table", table.Name), ("Field", field.Name),
                            ("Target", foreignTable.Name)));
                    continue;
                }

                // `-`, in a column that did not say a row may have none. Both ways out are
                // named because which one is right is the author's call - the row may be
                // missing a target, or the column may have been marked required by habit.
                if (field.IsRequired)
                {
                    diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                        Message.Of(CookingMessages.ReferenceNoneButRequired,
                            ("Table", table.Name), ("Field", field.Name),
                            ("Target", foreignTable.Name)));
                }
            }
        }

        if (foreignTable.Fields.Count == 0)
            return;

        // Whichever form a reference takes, the cell stores the target's primary
        // index, so the keys to match against all live in its first column.
        var foreignKeys = KeysOf(foreignTable, rowSet);

        foreach (var row in rowSet.Rows)
        {
            var cell = row[field.Index];

            // Absence is not a key to look up, and where it was not allowed it has been
            // reported above.
            if (!cell.HasValue)
                continue;

            // An array of references is a key per element, and each is looked up the way a
            // scalar one is. Reported per element, so a cell holding three keys of which one
            // is wrong names the one rather than the cell.
            if (cell.Value is System.Array keys && field.IsArray)
            {
                foreach (object? element in keys)
                    CheckKeyExists(table, field, foreignTable, foreignKeys, cell, element, diagnostics);

                continue;
            }

            CheckKeyExists(table, field, foreignTable, foreignKeys, cell, cell.Value, diagnostics);
        }
    }

    /// <summary>
    /// Says so when one key a reference cell holds is not a row of its target.
    /// </summary>
    /// <remarks>
    /// Zero is the conventional "points at nothing". Index values start at one, so it can
    /// never collide with a real row. Left alone deliberately: what the checks above catch is
    /// a cell that was never filled in acquiring a meaning, not the meaning of a value
    /// somebody typed.
    /// </remarks>
    private static void CheckKeyExists(
        Table table, Field field, Table foreignTable,
        HashSet<object> foreignKeys, Cell cell, object? value, Diagnostics diagnostics)
    {
        if (value is int key && key == 0)
            return;

        if (value is not null && foreignKeys.Contains(value))
            return;

        diagnostics.Error(cell.RawCell.Location,
            Message.Of(CookingMessages.ReferenceMissingRow,
                ("Table", table.Name), ("Field", field.Name),
                ("Target", foreignTable.Name), ("Value", value)));
    }

    /// <summary>
    /// A referenced table's primary keys, for the set of rows a reference belongs to.
    /// </summary>
    /// <remarks>
    /// **Kept, because a popular table is referenced by many columns.** The set is a
    /// property of the target and of which set of its rows is in view, and neither of those
    /// changes as the referencing columns are walked - so building it per column meant
    /// walking one table's rows once for every column in the project that points at it. On
    /// the sample project that was 2.71 s of the validation pass.
    ///
    /// Keyed by the target itself rather than by its name: two tables with one name is a
    /// finding somewhere else, and this is not the place that should quietly merge them.
    /// spec/ops/conversion-time.md section 4.
    /// </remarks>
    /// <remarks>
    /// Concurrent because the tables are checked at the same time, and a popular target is
    /// exactly the one several of them ask about at once. Two threads may both build the set
    /// for the same target - which costs one wasted pass and yields the same set either way -
    /// and only one of them is kept.
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Table Target, string RowSet), HashSet<object>> _foreignKeys = new();

    private HashSet<object> KeysOf(Table foreignTable, RowSet rowSet)
    {
        if (_foreignKeys.TryGetValue((foreignTable, rowSet.Name), out var cached))
            return cached;

        var keys = new HashSet<object>();

        foreach (var foreignRow in RowsToMatchAgainst(foreignTable, rowSet))
            keys.Add(foreignRow[foreignTable.PrimaryIndexField!.Index].Value!);

        return _foreignKeys.GetOrAdd((foreignTable, rowSet.Name), keys);
    }

    /// <summary>
    /// Which of a referenced table's rows a reference in this set of rows points into.
    /// </summary>
    /// <remarks>
    /// The set with the same name when the target has one, and the target's own set
    /// otherwise. A second set of rows is a second world: a row written for one build refers
    /// to the rows that build loads, so checking it against the first set's ids answers a
    /// question nobody asked.
    ///
    /// The fallback is not a leniency but the common case. Only a handful of tables have
    /// more than one set, so a row in one nearly always points at a table that has just the
    /// one - measured on the sample project: 81 tables with a second set out of 537.
    ///
    /// **What this asks of whoever loads the files**: a build reading one set has to read
    /// that set of every table that has one. That condition is the whole of what makes this
    /// check match what happens at runtime, and it is stated in spec/layout/table-row-sets.md.
    /// </remarks>
    private static List<List<Cell>> RowsToMatchAgainst(Table foreignTable, RowSet rowSet)
    {
        if (rowSet.Name.Length == 0)
            return foreignTable.Data;

        foreach (var theirs in foreignTable.ExtraRowSets)
        {
            if (string.Equals(theirs.Name, rowSet.Name, System.StringComparison.Ordinal))
                return theirs.Rows;
        }

        return foreignTable.Data;
    }

    /// <summary>
    /// Checks the columns whose sheet named the tables their value has to exist in.
    /// </summary>
    /// <remarks>
    /// Not a reference. The value stays what it was and nothing downstream learns the
    /// column has this on it - what a sheet is saying here is "whatever id this holds, one
    /// of these tables has a row for it", and the whole of honouring that is looking.
    /// Several tables is the ordinary case, and one of them holding the id is enough:
    /// which one is deliberately not recorded, because recording it is resolution.
    ///
    /// spec/references/multi-target-references.md.
    /// </remarks>
    /// <remarks>
    /// Internal rather than private so a layout test can hand it a parsed model: what is
    /// worth pinning is the rule reading a sheet, and running the whole cooker to reach it
    /// would test the cooker instead.
    /// </remarks>
    /// <summary>
    /// Whether a column belongs to the variant the given row is, for a polymorphic group.
    /// </summary>
    /// <remarks>
    /// True for everything that is not a variant member - a plain column, a base field, a
    /// group that is not polymorphic - so a caller can ask without knowing which it has.
    /// spec/types/polymorphism.md section 5.2.
    /// </remarks>
    private static bool IsThisRowsVariantColumn(Table table, List<Cell> row, Field field)
    {
        if (field.VariantsDeclaringThis.Count == 0)
            return true;

        // **The discriminator of this element, not of the group.** A multi-row group has one
        // per element, and the one that answers for a member is the one at the same element.
        // spec/types/polymorphism.md section 5.3.
        int? element = field.NamePath is { Count: > 0 } ? field.NamePath[0].Index : null;

        var discriminator = table.Fields.FirstOrDefault(
            candidate => candidate.IsDiscriminator
                         && candidate.GroupName == field.GroupName
                         && (candidate.NamePath is { Count: > 0 }
                             ? candidate.NamePath[0].Index
                             : null) == element);

        if (discriminator is null || discriminator.Index >= row.Count)
            return true;

        if (row[discriminator.Index].Value is not int written)
            return true;

        string? variant = discriminator.Variants
            .FirstOrDefault(candidate => candidate.Discriminator == written)
            ?.Name;

        return variant is not null
               && field.VariantsDeclaringThis.Contains(
                   variant, System.StringComparer.OrdinalIgnoreCase);
    }

    internal void ValidateReferencedTables(Model model, Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        foreach (var field in table.Fields)
        {
            var named = field.Constraints.ReferencedTables;
            if (named is null || named.Count == 0)
                continue;

            // Already a reference, so `ValidateReferences` answers for it and saying the
            // same thing twice would report every fault twice.
            if (field.IsRef)
                continue;

            var targets = new List<Table>();
            var missing = new List<string>();

            foreach (string name in named)
            {
                var target = model.FindTable(name);

                if (target is null)
                    missing.Add(name);
                else
                    targets.Add(target);
            }

            // All or nothing, and this is the important part. A build reads the workbooks
            // its recipe names, so a table the declaration lists may simply not be in this
            // one - and checking against what is left would report every id that lives in
            // the absent table. That is not a hypothetical failure mode: checking a shop's
            // item id against one catalogue instead of the two it names gives thousands of
            // findings and none of them are real. Either the whole catalogue is here or the
            // column is not judged.
            if (missing.Count > 0)
            {
                // Warned rather than refused, because the model cannot tell a misspelling
                // from a table this build does not read, and refusing would stop every
                // narrow build over a declaration aimed at a wider one. `TreatWarningsAsErrors`
                // is what a pipeline that wants the stricter reading already has.
                Log.Warning(Message.Of(CookingMessages.LogReferencedTablesUnchecked,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Missing", string.Join("`, `", missing)),
                    ("At", field.Constraints.ReferencedTablesLocation))
                    .In(MessageCatalog.Current));

                Interlocked.Increment(ref _uncheckedReferencedTables);
                continue;
            }

            if (targets.Count == 0)
                continue;

            Interlocked.Increment(ref _checkedReferencedTables);
            Interlocked.Add(ref _rowsAgainstReferencedTables, rowSet.Rows.Count);

            CheckValuesExistIn(table, rowSet, field, targets, diagnostics);
        }
    }

    /// <summary>
    /// One column's values against the union of the tables named for it.
    /// </summary>
    /// <remarks>
    /// The keys are collected once for the column rather than looked up per cell, which is
    /// the same shape <see cref="ValidateReferencedKeysExist"/> uses and for the same
    /// reason: the alternative walks every target table once per row.
    /// </remarks>
    private void CheckValuesExistIn(
        Table table, RowSet rowSet, Field field, List<Table> targets, Diagnostics diagnostics)
    {
        // One set for all of them. Which table an id came from is not being asked - it is
        // in one of them or it is in none.
        var keys = new HashSet<object>();
        foreach (var target in targets)
        {
            if (target.Fields.Count == 0)
                continue;

            // The same set-first rule the resolved reference follows, for the same reason.
            foreach (var targetRow in RowsToMatchAgainst(target, rowSet))
                keys.Add(ComparableKey(targetRow[target.PrimaryIndexField!.Index].Value)!);
        }

        string names = string.Join("`, `", targets.Select(t => t.Name));

        foreach (var row in rowSet.Rows)
        {
            var cell = row[field.Index];

            // Nothing written is nothing to look up. The checker this came from returns on
            // a missing value before it looks at all, and a column that must hold something
            // says so as being required, which is a different declaration.
            if (!cell.HasValue)
                continue;

            // A key per element, each looked up the way a scalar one is - so a cell holding
            // three keys of which one is wrong names the one rather than the cell.
            if (cell.Value is System.Array elements && field.IsArray)
            {
                foreach (object? element in elements)
                {
                    if (keys.Contains(ComparableKey(element)!))
                        continue;

                    diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                        Message.Of(CookingMessages.MultiTargetMissingRow,
                            ("Table", table.Name), ("Field", field.Name),
                            ("Value", element), ("Targets", names)));
                }

                continue;
            }

            if (keys.Contains(ComparableKey(cell.Value)!))
                continue;

            diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                Message.Of(CookingMessages.MultiTargetMissingRow,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Value", cell.Value), ("Targets", names)));
        }
    }

    /// <summary>
    /// A value in the form the two sides of this check can be compared in.
    /// </summary>
    /// <remarks>
    /// The id and the key it has to match are separate columns with separate types, and a
    /// boxed `75203300.0` is not equal to a boxed `75203300`. That is not hypothetical: a
    /// layout that narrows its key columns to `int` and leaves ordinary number columns
    /// `double` produces exactly this pair, and comparing them as they came made every row
    /// of every checked column a finding - 59,352 of them, which is the shape a check takes
    /// when it is really answering "these are different types".
    ///
    /// Whole numbers become `long` whatever width they arrived in. Anything else is left
    /// alone: strings compare as strings, and a fractional value is not an id.
    /// </remarks>
    private static object? ComparableKey(object? value)
        => value switch
        {
            long l => l,
            int i => (long)i,
            short s => (long)s,
            byte b => (long)b,
            sbyte sb => (long)sb,
            ushort us => (long)us,
            uint u => (long)u,
            ulong ul when ul <= long.MaxValue => (long)ul,
            double d when IsWholeAndFits(d) => (long)d,
            float f when IsWholeAndFits(f) => (long)f,
            _ => value,
        };

    /// <summary>Whether a floating value is a whole number a `long` can hold.</summary>
    private static bool IsWholeAndFits(double value)
        => value >= -9.2233720368547758e18 && value <= 9.2233720368547758e18
            && value == System.Math.Floor(value);

    /// <summary>
    /// Checks that no build leaves a reference dangling.
    ///
    /// Target-side filtering removes whole entities from an output. If a table that
    /// survives references one that does not, the generated code names a type that
    /// was never emitted, and the failure surfaces in the consuming project's
    /// compiler instead of here.
    ///
    /// Only the sides the recipe actually asks for are checked, so a workbook is
    /// never rejected over a combination nobody builds.
    /// </summary>
    private void ValidateTargetSideReachability(
        Model model, RecipeModel recipeModel, TargetSide requested, Diagnostics diagnostics)
    {
        foreach (var side in RequestedTargetSides(recipeModel, requested))
        {
            if (side == TargetSide.Both)
                continue;

            var visibleTables = new HashSet<string>(
                model.Tables.Where(t => TargetSides.Includes(side, t.TargetSide)).Select(t => t.Name));

            foreach (var table in model.Tables)
            {
                if (!TargetSides.Includes(side, table.TargetSide))
                    continue;

                foreach (var field in table.Fields)
                {
                    if (!field.IsRef)
                        continue;

                    // Already reported as unresolvable; whether it would also be
                    // filtered out is beside the point.
                    if (field.ResolvedRefTable is null)
                        continue;

                    if (!TargetSides.Includes(side, field.TargetSide))
                        continue;

                    if (visibleTables.Contains(field.RefTableName!))
                        continue;

                    diagnostics.Error(field.DetailTypeLocation,
                        Message.Of(CookingMessages.ReferenceExcludedBySide,
                            ("Side", TargetSides.Describe(side)),
                            ("Table", table.Name), ("Field", field.Name),
                            ("Target", field.RefTableName)));
                }
            }
        }
    }

    /// <summary>
    /// The distinct target sides any output entry in the recipe asks for.
    ///
    /// Taken from the target registry, which is the same list the run itself works
    /// through, so every target that will produce output is covered.
    ///
    /// It was previously a hand-written enumeration of six recipe sections, and the
    /// four database sections were missing from it - added later, to the run but not
    /// here. A recipe whose only server-side output was a database export therefore
    /// had its server side left unvalidated, and a table referencing a client-only
    /// table would reach the exporter unreported.
    /// </summary>
    private static IEnumerable<TargetSide> RequestedTargetSides(RecipeModel recipeModel, TargetSide requested)
    {
        var sides = new HashSet<TargetSide>();

        foreach (var planned in TargetRegistry.Plan(recipeModel, requested))
            sides.Add(planned.Side);

        return sides;
    }


    /// <summary>
    /// Checks every cell against what its column declared it may hold: a value where one is
    /// required, a number inside its bounds, a value the whitelist names.
    /// </summary>
    /// <remarks>
    /// The type has already been enforced by the time a cell reaches here - a `number`
    /// column holds a number. What is left is what the type cannot say, and what a sheet
    /// with somewhere to say it does say: a range, a list, and whether a blank is allowed.
    ///
    /// Requiredness is checked here rather than at parse time because a layout may read a
    /// blank as the type's empty value and still record that the sheet gave none. The
    /// layout that refuses a blank outright never reaches this, and the one that reads
    /// `-` as "no value" is exactly the case this exists for.
    ///
    /// A reference column is left alone: whether the row it names exists is
    /// <see cref="ValidateReferencedKeysExist"/>'s question, and a bound written on a
    /// reference is the id band it points into rather than a range its own value must sit
    /// in - reporting both would be the same mistake twice.
    ///
    /// spec/layout/column-constraints.md.
    /// </remarks>
    internal void ValidateColumnConstraints(Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        // Walked by group rather than by column, because "is a value missing" is a question
        // about the group. An array that ends where its values end has no missing tail - the
        // columns past the end are not empty values but absent elements, and checking them
        // one by one reports the length of every short row as a fault.
        foreach (var group in table.SerialFields)
        {
            foreach (var row in rowSet.Rows)
            {
                int elements = table.ElementCountIn(group, row);

                foreach (var member in Columns(group))
                {
                    for (int at = 0; at < member.Count && at < elements; at++)
                        CheckCell(table, member[at], row, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// A group's columns, one list per member: the group's own for a scalar, and one per
    /// member for a record.
    /// </summary>
    /// <remarks>
    /// Each list is in element order, so a trimmed group's length applies to all of them -
    /// which is the same thing the wire and the exporters rely on.
    /// </remarks>
    /// <remarks>
    /// **The leaves, not the members.** A member may be a record and then holds no columns of
    /// its own - `Star1.Position.X` gives the group one member, `Position`, whose `Fields` is
    /// empty - so walking the members alone checked nothing a level further in. A constraint
    /// written on such a member was read, stored on its column, and never asked about.
    /// spec/types/set-and-map.md section 6.3.
    /// </remarks>
    private static IEnumerable<IReadOnlyList<Field>> Columns(SerialField group)
        => group.IsRecord
            ? group.Leaves.Select(leaf => (IReadOnlyList<Field>)leaf.Fields)
            : new[] { (IReadOnlyList<Field>)group.Fields };

    /// <summary>One cell against what its column declared.</summary>
    private void CheckCell(Table table, Field field, List<Cell> row, Diagnostics diagnostics)
    {
        if (field.IsRef || field.Index >= row.Count)
            return;

        // Before the index check below, not after: whether a value names a file that exists
        // has nothing to do with whether the column identifies rows, and a table keyed by an
        // asset name is a shape sheets have.
        if (field.Role == StringRole.Asset && row[field.Index].HasValue)
            CheckAsset(table, field, row[field.Index], diagnostics);

        // A bound written on an index states the id band it points into rather than a range
        // its own value must sit in.
        if (field.Indexing)
        {
            // An index has no absence to express. It is what identifies the row, and every
            // reference into this table resolves through it, so `-` here would leave the row
            // unidentifiable - and, since absence parses to the type's empty value, leave
            // every such row sharing one key. `int?` on an index is refused where the column
            // is declared; this is the same refusal against a cell.
            // spec/types/blank-and-null-cells.md.
            if (!row[field.Index].HasValue)
            {
                diagnostics.Error(row[field.Index].RawCell?.Location ?? field.NameLocation,
                    Message.Of(CookingMessages.IndexAbsent,
                        ("Table", table.Name), ("Field", field.Name)));
            }

            return;
        }

        var cell = row[field.Index];

        if (!cell.HasValue)
        {
            if (field.IsRequired)
            {
                diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                    Message.Of(CookingMessages.RequiredEmpty,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Type", field.TypeName)));
            }

            // Nothing there to hold to a bound either: the empty value is the type's rather
            // than the author's.
            return;
        }

        if (!field.Constraints.IsEmpty)
            CheckAgainstConstraints(table, field, cell, diagnostics);
    }

    /// <summary>
    /// One cell of an `asset` column against the folders configured for its kind.
    /// </summary>
    /// <remarks>
    /// Every element of a list cell, because the role is what the column holds and a list
    /// holds more of the same - the same reading the gathering makes of `text[]`.
    ///
    /// A kind with no folders behind it is an error however `OnMissing` is set, and that is
    /// the one thing here that is not about the data: a column saying `asset(icnos)` would
    /// otherwise be a column nothing ever checks, reported as nothing at all. The recipe's
    /// leniency is about assets that have not been made yet, not about kinds that do not
    /// exist.
    /// </remarks>
    private void CheckAsset(Table table, Field field, Cell cell, Diagnostics diagnostics)
    {
        if (_assets is null)
            return;

        string kind = field.RoleGroup ?? "";

        if (!_assets.Knows(kind))
        {
            // The column is wrong rather than the row, so it is reported once and its cells
            // are not each reported as a missing file on top of it. A table with 90,000 rows
            // and one misspelled kind should produce one message.
            if (_unknownKinds.TryAdd(field, 0))
            {
                string configured = string.Join(", ",
                    _assets.Kinds.Select(known => known.Length == 0 ? "(no kind)" : known));

                // Two ids rather than one sentence with two conditionals in it. The text was
                // already two sentences pretending to be one, and a catalog entry cannot hold
                // an `if` - nor could a translator do anything useful with a fragment that is
                // sometimes `asset` and sometimes `asset(icon)`.
                diagnostics.Error(field.TypeLocation, kind.Length == 0
                    ? Message.Of(CookingMessages.AssetNoFolderWithoutKind,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Configured", configured))
                    : Message.Of(CookingMessages.AssetNoFolderForKind,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Kind", kind), ("Configured", configured)));
            }

            return;
        }

        var severity = _assets.OnMissingSeverity;

        if (severity is null)
            return;

        foreach (string name in AssetNames(cell.Value))
        {
            if (string.IsNullOrWhiteSpace(name) || _assets.Has(kind, name))
                continue;

            diagnostics.Add(severity.Value, cell.RawCell?.Location ?? field.NameLocation,
                kind.Length == 0
                    ? Message.Of(NamingMessages.AssetFileMissing,
                        ("Table", table.Name), ("Field", field.Name), ("Name", name))
                    : Message.Of(NamingMessages.AssetFileMissingForKind,
                        ("Table", table.Name), ("Field", field.Name), ("Name", name),
                        ("Kind", kind)));
        }
    }

    /// <summary>The names one asset cell holds: one, or each element of a list.</summary>
    private static IEnumerable<string> AssetNames(object? value)
    {
        if (value is string single)
        {
            yield return single;
            yield break;
        }

        if (value is string[] many)
        {
            foreach (string element in many)
                yield return element;
        }
    }

    /// <summary>
    /// One cell against its column's bounds and whitelist.
    /// </summary>
    /// <remarks>
    /// An array cell is checked element by element: the bound is on the column and every
    /// element of it is a value of that column.
    /// </remarks>
    /// <summary>
    /// How many elements an array cell holds, against what its column allows.
    /// </summary>
    /// <remarks>
    /// About the cell rather than about any one value in it, which is why it is here and not
    /// in the per-value check. Reported on the row that breaks it: the column is right and
    /// the row is short.
    /// </remarks>
    private static void CheckLength(
        Table table, Field field, Cell cell, System.Array array, Diagnostics diagnostics)
    {
        var constraints = field.Constraints;
        var location = cell.RawCell?.Location ?? field.NameLocation;

        if (constraints.MinimumLength is int least && array.Length < least)
        {
            diagnostics.Error(location, Message.Of(CookingMessages.ArrayTooShort,
                ("Table", table.Name), ("Field", field.Name),
                ("Given", array.Length), ("Wanted", least)));
        }

        if (constraints.MaximumLength is int most && array.Length > most)
        {
            diagnostics.Error(location, Message.Of(CookingMessages.ArrayTooLong,
                ("Table", table.Name), ("Field", field.Name),
                ("Given", array.Length), ("Wanted", most)));
        }
    }

    /// <summary>
    /// A column's pattern, compiled once and kept.
    /// </summary>
    /// <remarks>
    /// Compiling per row would compile it per row - a table of ninety thousand rows pays for
    /// the same expression ninety thousand times. A pattern that will not compile is the
    /// column's mistake rather than any row's, so it is reported once against the column and
    /// then stops being asked about.
    /// </remarks>
    private System.Text.RegularExpressions.Regex? PatternFor(
        Table table, Field field, string pattern, Diagnostics diagnostics)
    {
        if (_patterns.TryGetValue(field, out var known))
            return known;

        try
        {
            known = new System.Text.RegularExpressions.Regex(pattern);
        }
        catch (System.ArgumentException problem)
        {
            diagnostics.Error(field.Constraints.PatternLocation ?? field.TypeLocation,
                Message.Of(CookingMessages.PatternUnreadable,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Pattern", pattern), ("Detail", problem.Message)));

            known = null;
        }

        _patterns[field] = known;
        return known;
    }

    /// <remarks>
    /// Concurrent for the same reason as <see cref="_unknownKinds"/>. Two threads compiling
    /// the same pattern is harmless - the result is the same object's worth of behavior and
    /// the last write wins - but a plain <c>Dictionary</c> written from both corrupts.
    /// </remarks>
    private readonly System.Collections.Concurrent
        .ConcurrentDictionary<Field, System.Text.RegularExpressions.Regex?> _patterns = new();

    /// <summary>
    /// Whether a value is the one a type holds when nobody wrote anything.
    /// </summary>
    /// <remarks>
    /// Compared as text against the type's own empty value, so every type answers the same
    /// way and none of them needs a case here. What makes the question worth asking is that a
    /// blank cell and a written zero reach everything downstream identically - so a column
    /// where zero means nothing has no other way to refuse it.
    /// </remarks>
    private static bool IsTheTypesEmptyValue(Models.ValueType type, object? value)
    {
        if (value is null)
            return true;

        var element = Models.ValueTypes.ElementOf(type);

        return Text(value) == Text(CookingContext.EmptyValueOfType(element));
    }

    private void CheckAgainstConstraints(Table table, Field field, Cell cell, Diagnostics diagnostics)
    {
        if (cell.Value is System.Array array)
        {
            CheckLength(table, field, cell, array, diagnostics);

            for (int at = 0; at < array.Length; at++)
            {
                // An element the sheet said has no value holds the type's empty one, which is
                // not a value to hold to a bound - the same answer this check gives a whole
                // cell with no value. spec/types/nullable-array-elements.md.
                if (cell.ElementHasValue is { } present && at < present.Length && !present[at])
                    continue;

                CheckOneValue(table, field, cell, array.GetValue(at)!, at, diagnostics);
            }

            return;
        }

        CheckOneValue(table, field, cell, cell.Value, -1, diagnostics);
    }

    /// <param name="elementAt">Which element of an array cell, or -1 for a scalar.</param>
    private void CheckOneValue(
        Table table, Field field, Cell cell, object? value, int elementAt, Diagnostics diagnostics)
    {
        var constraints = field.Constraints;
        var location = cell.RawCell?.Location ?? field.NameLocation;
        // Whether this is about a row's value or one element of its array picks the id
        // rather than a fragment glued into the sentence. `where` used to be `""` or
        // ` (element 3)`, which is a conditional inside a message - the one thing a catalog
        // entry cannot hold, and a phrase a translator would have been handed blind.
        bool isElement = elementAt >= 0;

        if (constraints.AllowedValues is { Count: > 0 } allowed)
        {
            string? text = Text(value!);

            if (!allowed.Contains(text!))
            {
                diagnostics.Error(location, isElement
                    ? Message.Of(CookingMessages.ElementValueNotAllowed,
                        ("Table", table.Name), ("Field", field.Name), ("Element", elementAt),
                        ("Value", text), ("Allowed", string.Join(", ", allowed)))
                    : Message.Of(CookingMessages.ValueNotAllowed,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Value", text), ("Allowed", string.Join(", ", allowed))));
            }
        }

        if (constraints.NotDefault && IsTheTypesEmptyValue(field.Type, value))
        {
            diagnostics.Error(location, isElement
                ? Message.Of(CookingMessages.ElementValueIsTheDefault,
                    ("Table", table.Name), ("Field", field.Name), ("Element", elementAt),
                    ("Value", Text(value!)))
                : Message.Of(CookingMessages.ValueIsTheDefault,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Value", Text(value!))));
        }

        if (constraints.Pattern is { } pattern && value is string written)
        {
            if (PatternFor(table, field, pattern, diagnostics) is { } compiled
                && !compiled.IsMatch(written))
            {
                diagnostics.Error(location, isElement
                    ? Message.Of(CookingMessages.ElementValueDoesNotMatch,
                        ("Table", table.Name), ("Field", field.Name), ("Element", elementAt),
                        ("Value", written), ("Pattern", pattern))
                    : Message.Of(CookingMessages.ValueDoesNotMatch,
                        ("Table", table.Name), ("Field", field.Name),
                        ("Value", written), ("Pattern", pattern)));
            }
        }

        if (constraints.Minimum is null && constraints.Maximum is null)
            return;

        // Bounds are about numbers. A column of another type carrying them is a sheet
        // saying something this cannot check, and saying so per row would bury the point.
        if (!TryAsNumber(value!, out double number))
            return;

        if (constraints.Minimum is double min && number < min)
        {
            diagnostics.Error(location, isElement
                ? Message.Of(CookingMessages.ElementValueBelowMinimum,
                    ("Table", table.Name), ("Field", field.Name), ("Element", elementAt),
                    ("Value", Text(value!)), ("Minimum", Text(min)))
                : Message.Of(CookingMessages.ValueBelowMinimum,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Value", Text(value!)), ("Minimum", Text(min))));
        }

        if (constraints.Maximum is double max && number > max)
        {
            diagnostics.Error(location, isElement
                ? Message.Of(CookingMessages.ElementValueAboveMaximum,
                    ("Table", table.Name), ("Field", field.Name), ("Element", elementAt),
                    ("Value", Text(value!)), ("Maximum", Text(max)))
                : Message.Of(CookingMessages.ValueAboveMaximum,
                    ("Table", table.Name), ("Field", field.Name),
                    ("Value", Text(value!)), ("Maximum", Text(max))));
        }
    }

    /// <summary>
    /// A value as a diagnostic writes it, and as a whitelist compares it.
    /// </summary>
    /// <remarks>
    /// Invariant, so a build machine whose culture writes `1,5` does not report a bound the
    /// sheet never wrote - and does not fail a whitelist match on the decimal separator.
    /// </remarks>
    private static string? Text(object value)
        => value switch
        {
            null => "",
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            System.IFormattable formattable
                => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

    /// <summary>The value as a double, for the numeric types a bound can apply to.</summary>
    private static bool TryAsNumber(object value, out double number)
    {
        switch (value)
        {
            case int i: number = i; return true;
            case long l: number = l; return true;
            case float f: number = f; return true;
            case double d: number = d; return true;
            default: number = 0; return false;
        }
    }
}
