using System.Collections.Generic;
using System.Linq;
using Serilog;
using Tabbit.Models;
using Tabbit.Recipe;
using Tabbit.Targets;

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
    private readonly HashSet<Field> _unknownKinds = new HashSet<Field>();

    /// <summary>
    /// How much of the "this id is a row of one of these tables" check actually ran.
    /// </summary>
    /// <remarks>
    /// Reported beside the findings because a zero on its own does not distinguish "the
    /// sheets are clean" from "the check never ran" - and this check is one whose columns
    /// go unjudged for a legitimate reason, so the two really do have to be told apart.
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

        foreach (var table in model.Tables)
        {
            // Once per set of rows the table has. A table with one set - nearly all of them -
            // runs this once, and every rule below asks its questions of the set rather than
            // of the table, because a second set is data the first knows nothing about.
            // spec/table-row-sets.md.
            foreach (var rowSet in table.RowSets)
            {
                ValidateIndexUniqueness(table, rowSet, diagnostics);
                ValidateReferences(model, table, rowSet, diagnostics);
                ValidateReferencedTables(model, table, rowSet, diagnostics);
                ValidateColumnConstraints(table, rowSet, diagnostics);
                ValidateArrayGaps(table, rowSet, diagnostics);
                ValidateRequiredInRecord(table, rowSet, diagnostics);
            }
        }

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
    /// spec/variable-length-record-arrays.md has the rule and `AllowArrayGaps`.
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
            // asked for it. spec/nullable-array-elements.md.
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
                        $"`{table.Name}.{group.Name}` leaves element {element} empty while a "
                        + $"later element has a value. An array with a gap in it reads as one "
                        + $"whose middle holds the type's empty value, which a consumer cannot "
                        + $"tell from a value that was written. Fill it, move the later values "
                        + $"up, or set `AllowArrayGaps` on the source entry.");
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
    /// spec/record-member-optionality.md.
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
                                $"`{table.Name}.{field.Name}` has no value, and the sheet marks "
                                + $"the column as required. It is marked required inside an "
                                + $"object and is not part of one, which reads as required.");
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
                                $"`{table.Name}.{group.Name}` element {element} exists and its "
                                + $"`{member.Name}` is empty, which the sheet marks as required "
                                + $"inside the object. Give it a value, or clear the rest of the "
                                + $"element so that there is no record here.");
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
                $"{columns} column(s) are typed `asset` and no folders are configured to check "
                + $"them against, so nothing was checked. Name the folders in the recipe's "
                + $"`Assets.Roots` to switch the check on.");
            return;
        }

        if (_assets.OnMissingSeverity is null)
        {
            diagnostics.Info(
                $"{columns} column(s) are typed `asset`, and `Assets.OnMissing` is `ignore`.");
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
                diagnostics.Error(field.TypeLocation,
                    $"Index field `{table.Name}.{field.Name}` is `{field.TypeName}`, {why}" +
                    $" Use a whole-number, string, uuid or enum column as an index.");
                continue;
            }

            // An optional index would let several rows share the type's empty value, which
            // the uniqueness check below would then report as duplicates. Saying it here
            // names the mistake - the `?` - instead of the symptom two steps later.
            if (!field.IsRequired)
            {
                diagnostics.Error(field.TypeLocation,
                    $"Index field `{table.Name}.{field.Name}` cannot be optional: " +
                    $"drop the `?` from its type, because every row must have a value for an index.");
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
                        $"Index field `{table.Name}.{field.Name}` repeats the value `{cell.Value}`, " +
                        $"first used at {firstLocation}. Values in an index field must be unique.");
                    continue;
                }

                seen.Add(cell.Value!, cell.RawCell!.Location);
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
            // is what used to happen. spec/reference-key-types.md.
            //
            // `enum` is the one exception, and it is a gap rather than a rule: an enum's
            // value travels zig-zag encoded rather than at a fixed width, so the read is the
            // one call the shared table has no entry for - by design, since each language
            // spells its own enum. Said here rather than left to the generators, where
            // it would surface as whichever of them a project reached first.
            if (field.RefKeyType == Models.ValueType.Enum)
            {
                diagnostics.Error(field.DetailTypeLocation,
                    $"`{table.Name}.{field.Name}` references `{field.ResolvedRefTable.Name}`, whose "
                    + $"index is an enum. Every other key type can be referenced; this one cannot "
                    + $"yet, because an enum travels in an encoding of its own and the generated "
                    + $"readers have no call for it here. Key `{field.ResolvedRefTable.Name}` by the "
                    + $"enum's underlying `int`, or carry the value here as that enum and look the "
                    + $"row up through `{field.ResolvedRefTable.Name}`'s own index.");
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
        // would be a refusal quietly lost. spec/reference-optionality.md.
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
                        $"Field `{table.Name}.{field.Name}` references `{foreignTable.Name}` and "
                        + $"this row leaves the cell empty. Write the key of a row to point at, or "
                        + $"`-` to say this row points at none.");
                    continue;
                }

                // `-`, in a column that did not say a row may have none. Both ways out are
                // named because which one is right is the author's call - the row may be
                // missing a target, or the column may have been marked required by habit.
                if (field.IsRequired)
                {
                    diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                        $"Field `{table.Name}.{field.Name}` references `{foreignTable.Name}` and this "
                        + $"row says it points at none, but the column is declared required. Give it "
                        + $"a row to point at, or declare the column optional.");
                }
            }
        }

        if (foreignTable.Fields.Count == 0)
            return;

        // Whichever form a reference takes, the cell stores the target's primary
        // index, so the keys to match against all live in its first column.
        var foreignKeys = new HashSet<object>();
        foreach (var foreignRow in RowsToMatchAgainst(foreignTable, rowSet))
            foreignKeys.Add(foreignRow[foreignTable.Fields[0].Index].Value!);

        foreach (var row in rowSet.Rows)
        {
            var cell = row[field.Index];

            // Absence is not a key to look up, and where it was not allowed it has been
            // reported above.
            if (!cell.HasValue)
                continue;

            // Zero is the conventional "points at nothing". Index values start at
            // one, so it can never collide with a real row. Left alone deliberately: what
            // is being caught above is a cell that was never filled in acquiring a meaning,
            // not the meaning of a value somebody typed.
            if (cell.Value is int key && key == 0)
                continue;

            if (foreignKeys.Contains(cell.Value!))
                continue;

            diagnostics.Error(cell.RawCell.Location,
                $"Field `{table.Name}.{field.Name}` references `{foreignTable.Name}` row `{cell.Value}`, " +
                $"which does not exist.");
        }
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
    /// check match what happens at runtime, and it is stated in spec/table-row-sets.md.
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
    /// spec/multi-target-references.md.
    /// </remarks>
    /// <remarks>
    /// Internal rather than private so a layout test can hand it a parsed model: what is
    /// worth pinning is the rule reading a sheet, and running the whole cooker to reach it
    /// would test the cooker instead.
    /// </remarks>
    internal void ValidateReferencedTables(Model model, Table table, RowSet rowSet, Diagnostics diagnostics)
    {
        foreach (var field in table.Fields)
        {
            var named = field.Constraints.ReferencedTables;
            if (named is null || named.Count == 0)
                continue;

            // Promoted to a reference with one target, so ValidateReferences answers for it
            // and saying the same thing twice would report every fault twice.
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
                Log.Warning(
                    $"`{table.Name}.{field.Name}` says its value is a row of "
                    + $"`{string.Join("`, `", missing)}`, which this build does not contain. "
                    + $"The column is not checked.\n    at {field.Constraints.ReferencedTablesLocation}");

                _uncheckedReferencedTables++;
                continue;
            }

            if (targets.Count == 0)
                continue;

            _checkedReferencedTables++;
            _rowsAgainstReferencedTables += rowSet.Rows.Count;

            CheckKeyBandsDoNotOverlap(table, field, targets, diagnostics);
            CheckValuesExistIn(table, rowSet, field, targets, diagnostics);
        }
    }

    /// <summary>
    /// That no two of a column's targets hold the same id.
    /// </summary>
    /// <remarks>
    /// A column reaching several tables gets one accessor per target, and what makes that
    /// usable rather than merely correct is that exactly one of them ever answers. That is
    /// true of the sheets this came from - 58 declarations over 94,748 rows, and not one id
    /// in two tables - because the id bands are split by table. It is a property of the
    /// data, though, not of the declaration, so it is checked rather than assumed.
    ///
    /// Reported once per pair rather than once per shared id: what is wrong is that two
    /// catalogues overlap, and a row is only where it shows.
    ///
    /// spec/multi-target-references.md.
    /// </remarks>
    private static void CheckKeyBandsDoNotOverlap(
        Table table, Field field, List<Table> targets, Diagnostics diagnostics)
    {
        if (targets.Count < 2)
            return;

        var keys = new List<HashSet<object>>();
        foreach (var target in targets)
        {
            var set = new HashSet<object>();
            if (target.Fields.Count > 0)
            {
                foreach (var row in target.Data)
                    set.Add(ComparableKey(row[target.Fields[0].Index].Value)!);
            }

            keys.Add(set);
        }

        for (int a = 0; a < targets.Count; a++)
        {
            for (int b = a + 1; b < targets.Count; b++)
            {
                var shared = keys[a].Where(key => keys[b].Contains(key)).Take(3).ToList();
                if (shared.Count == 0)
                    continue;

                diagnostics.Error(field.Constraints.ReferencedTablesLocation ?? field.NameLocation,
                    $"`{table.Name}.{field.Name}` may be a row of `{targets[a].Name}` or of "
                    + $"`{targets[b].Name}`, and both hold `{string.Join("`, `", shared)}`. An id "
                    + $"in two of them makes the generated accessors answer together, and which "
                    + $"row the column meant is then not in the data. Give the two tables "
                    + $"separate id bands, or point at them from separate columns.");
            }
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
                keys.Add(ComparableKey(targetRow[target.Fields[0].Index].Value)!);
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

            if (keys.Contains(ComparableKey(cell.Value)!))
                continue;

            diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                $"`{table.Name}.{field.Name}` holds `{cell.Value}`, which is not a row of "
                + $"`{names}`.");
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
                        $"In a `{TargetSides.Describe(side)}` build, field `{table.Name}.{field.Name}` references table " +
                        $"`{field.RefTableName}`, which that build excludes by target side.");
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
    /// spec/column-constraints.md.
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
    private static IEnumerable<IReadOnlyList<Field>> Columns(SerialField group)
        => group.IsRecord
            ? group.Members.Select(member => (IReadOnlyList<Field>)member.Fields)
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
            // spec/blank-and-null-cells.md.
            if (!row[field.Index].HasValue)
            {
                diagnostics.Error(row[field.Index].RawCell?.Location ?? field.NameLocation,
                    $"`{table.Name}.{field.Name}` identifies the row, and this row says it has no "
                    + $"value there. Write one: an index cannot be absent.");
            }

            return;
        }

        var cell = row[field.Index];

        if (!cell.HasValue)
        {
            if (field.IsRequired)
            {
                diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                    $"`{table.Name}.{field.Name}` has no value, and the sheet declares the "
                    + $"column required. Write one, or type the column `{field.TypeName}?` so "
                    + $"that a row may say it has none.");
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
            if (_unknownKinds.Add(field))
            {
                string configured = string.Join(", ",
                    _assets.Kinds.Select(known => known.Length == 0 ? "(no kind)" : known));

                diagnostics.Error(field.TypeLocation,
                    $"`{table.Name}.{field.Name}` is typed "
                    + (kind.Length == 0 ? "`asset`" : $"`asset({kind})`")
                    + $", and the recipe configures no folder for "
                    + (kind.Length == 0 ? "a column without a kind" : $"kind `{kind}`")
                    + $". Configured: {configured}.");
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
                $"`{table.Name}.{field.Name}` names `{name}`, and no file of that name is in "
                + (kind.Length == 0 ? "the configured folders" : $"the folders for kind `{kind}`")
                + $".");
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
    private void CheckAgainstConstraints(Table table, Field field, Cell cell, Diagnostics diagnostics)
    {
        if (cell.Value is System.Array array)
        {
            for (int at = 0; at < array.Length; at++)
            {
                // An element the sheet said has no value holds the type's empty one, which is
                // not a value to hold to a bound - the same answer this check gives a whole
                // cell with no value. spec/nullable-array-elements.md.
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
        string where = elementAt < 0 ? "" : $" (element {elementAt})";

        if (constraints.AllowedValues is { Count: > 0 } allowed)
        {
            string? text = Text(value!);

            if (!allowed.Contains(text!))
            {
                diagnostics.Error(location,
                    $"`{table.Name}.{field.Name}`{where} is `{text}`, which the column's list of "
                    + $"allowed values does not name. Allowed: {string.Join(", ", allowed)}.");
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
            diagnostics.Error(location,
                $"`{table.Name}.{field.Name}`{where} is {Text(value!)}, "
                + $"below the minimum {Text(min)} the column declares.");
        }

        if (constraints.Maximum is double max && number > max)
        {
            diagnostics.Error(location,
                $"`{table.Name}.{field.Name}`{where} is {Text(value!)}, "
                + $"above the maximum {Text(max)} the column declares.");
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
