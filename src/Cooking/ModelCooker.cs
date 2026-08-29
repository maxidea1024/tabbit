using System.Collections.Generic;
using System.Linq;
using Serilog;
using Tabbit.Cooking.Layouts;
using Tabbit.Extensions;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Tabbit.Targets;

namespace Tabbit.Cooking;

/// <summary>
/// Turns the cells the sources read into the model everything downstream consumes.
/// </summary>
/// <remarks>
/// The interpreting itself belongs to a <see cref="ILayoutParser"/>, chosen per sheet from
/// the recipe entry that imported it. What is left here is the part that is true whatever
/// the sheets looked like: run every layout's declarations before any layout's tables,
/// resolve references across the lot, and check the result once.
/// </remarks>
public partial class ModelCooker
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Cooking;

    /// <param name="report">
    /// Where everything found here is kept so that it survives the run. Null when the recipe
    /// asked for no report. spec/ops/build-report.md.
    /// </param>
    public Model Cook(
        Options options,
        RecipeModel recipeModel,
        RawModel rawModel,
        Reporting.BuildReport? report = null)
    {
        var result = new Model();

        // Made before parsing rather than after it: a table a layout cannot read is a
        // finding about that table, and stopping there would hide every finding behind it.
        var diagnostics = new Diagnostics
        {
            PromoteWarnings = recipeModel.Validation?.TreatWarningsAsErrors ?? false,
        };

        // What the schema files declared, gathered from all of them at once so that a type
        // may be named before it is declared and in whichever file the recipe listed first.
        // Before the context, which holds them: they decide whether a type cell naming a
        // struct - or left empty inside a group that one covers - is something a layout may
        // hand over unresolved.
        var declarations = Schema.SchemaDeclarations.Read(rawModel.SchemaFiles, diagnostics);

        var context = new CookingContext(result, recipeModel, diagnostics, declarations)
        {
            // Resolved before a sheet is read, because which column of a variant group becomes
            // the field is a question the layout asks while reading the header.
            Variants = FieldVariants.Of(recipeModel.Variants, options.Variants),
        };

        // The enums go in before a sheet is read, because a type cell may name one and the
        // check that a type name is recognized asks the model.
        declarations.DeclareEnums(result, diagnostics);

        ParseRawModel(context, rawModel);

        // And the rest of what the declarations say, now that the sheets are here too: a name
        // a table has already taken, and a member typed with an enum a sheet declared rather
        // than these files. notes/struct-dsl-design.md section 4.4.
        declarations.Resolve(result, context, diagnostics);

        // What was worth saying once per column rather than once per cell, now that every
        // cell has been read and the counts are final.
        context.ReportCellNotices();

        // Column groups whose sheet named a struct for them take their members' types from
        // it, and their cells are read again now that there is a type to read them as. Before
        // the folds below and before references are promoted, because a bound column is an
        // ordinary column from here on and those passes should meet it as one.
        //
        // Before the extra row sets are folded as well, and that is not a gap: a set that has
        // not been folded yet is still a table of its own, with the same columns, so this
        // pass reaches its cells while it is one. notes/struct-dsl-design.md section 7.2.

        // A container written in a sheet's own type cell, ahead of the binding: the columns
        // it becomes are ordinary columns from there on, and a sheet with no `.tbs` file at
        // all reaches this - which the binding, by definition, does not.
        // spec/types/set-and-map.md section 2.3.
        ExpandSheetContainers(context, result, declarations, diagnostics);

        BindDeclaredStructs(context, result, declarations, diagnostics);

        // What a container promises about its values, now that the binding has read the
        // cells as the type they were declared. Before the folds, because a set's column
        // and a map's pair are what the sheet wrote and every pass below meets them as
        // ordinary array columns. spec/types/set-and-map.md sections 5 and 6.
        CheckContainers(result, diagnostics);


        // Every cell has been read, so the two type kinds that existed for the reading are
        // done. Both are folds and neither is visible below this line.
        ExpandCompositeColumns(context, result, diagnostics);
        FoldBitsetIntoInt64(result);

        // Tables that are really another set of some table's rows become that, before
        // anything downstream can take them for tables of their own.
        //
        // After every layout, because a table and the extra sets of its rows can be read
        // under different ones and arrive in whatever order the sheets are in. And here
        // rather than at the end of parsing so that every mismatched pair is reported
        // together: a project turning this on wants the list, not the first one.
        //
        // spec/layout/table-row-sets.md.
        TableRowSets.Fold(context, rawModel.Sheets, diagnostics);

        // What each table's data file is called, settled here rather than by each of the
        // seventeen programs that need it. After the fold, so a table that turned out to be
        // another table's extra rows is not given a file name of its own.
        NameDataFiles(result, recipeModel);

        result.SolveTableCrossReferencings(diagnostics);

        // Only now is it known what a reference cell holds. The layout kept those cells as
        // written because the target's key type is not a fact any one sheet carries, and
        // this turns them into values of that type.
        ConvertReferenceCells(context, result, diagnostics);

        // A `$type` cell waits for the same reason a reference cell does - which variants
        // exist is not a fact any one sheet carries - so it is settled here, beside it. Then
        // the union's own refusal, which needs each row's variant to be a number already.
        // spec/types/polymorphism.md sections 5.2 and 8.
        ConvertDiscriminatorCells(result, diagnostics);
        RefuseValuesOutsideTheRowsVariant(result, diagnostics);

        // And the rows are put in discriminator order, so that how the sheet was written stops
        // deciding how the file encodes. Only tables that have such a group move at all.
        // spec/types/polymorphism.md section 6.3.
        SortRowsByDiscriminator(result);

        // And the abstract types the sheets used, gathered once. A struct is an entity beside
        // a table and an enum, so the generators get one list rather than finding it again per
        // table - which is also what keeps two tables from each declaring their own `Effect`.
        // spec/types/polymorphism.md section 7.1.
        GatherPolymorphicTypes(result);

        // And the declarations whose value is one shape, gathered the same way and for the
        // same reason. spec/types/declared-struct-identity.md.
        GatherRecordTypes(result, declarations);

        // Runs after resolution: validation follows references to check that what
        // they point at exists.
        //
        // The requested side is passed in so a narrowed run is checked against what it
        // will actually build. Without it, `--target-side client` could fail on a
        // problem that only exists in the server cut it is not producing.
        ValidateModel(result, recipeModel, CommandLineTargetSide.Of(options), diagnostics);

        // The reports this recipe has written down stop being reports that end the run, and
        // start being reports that say so. After every check, because an entry states how many
        // it accounts for and that can only be counted once. spec/ops/known-problems.md.
        diagnostics.ApplyKnownProblems(recipeModel.Validation?.KnownProblems
                                       ?? (IReadOnlyList<Recipe.KnownProblemRecipe>)System.Array.Empty<Recipe.KnownProblemRecipe>());

        // Printed before the throw, and printed at all: this stage only ever produced errors,
        // which the exception carried, so nothing here had to say anything about the reports
        // that do not stop a run. An asset that has not been drawn yet is exactly such a
        // report, and one nobody sees is one nobody writes.
        Report(diagnostics);

        // Taken before the throw, so the report holds what did not stop the run as well as
        // what did. The throw below carries only the stopping half, and a report of only that
        // half is a report that loses every warning the moment one error appears.
        report?.Take(diagnostics);

        diagnostics.ThrowIfAny(Messages.Message.Of(CookingMessages.ValidationFailed));

        return result;
    }

    /// <summary>
    /// Turns every `bitset` field into the 64-bit integer it is, now that every cell has
    /// been read.
    /// </summary>
    /// <remarks>
    /// `bitset` is a type for exactly as long as parsing lasts. What made it one is the
    /// notation it accepts - no sign, no thousands separator, no fractional part, and `0x`
    /// reaching all 64 bits - and that question is settled once a cell has become a value.
    /// Past here it is a 64-bit integer to every consumer: the wire carries it as i64, the
    /// the generators render it as their own name for that width, and the databases
    /// store it in a BIGINT.
    ///
    /// **Folded rather than written into each of those.** Around a hundred switches select
    /// on this width across the generators, the exporters and the history store, and the
    /// compiler checks none of them - a lookup table that was never told about a new member
    /// throws at generation time, and a `switch` with a default answers wrongly instead.
    /// A fold cannot be half done, which is the property those hundred sites would not have.
    ///
    /// Every call that reads a cell is inside a layout parser, so all of them have run by
    /// here. <see cref="Field.TypeName"/> keeps saying `bitset`, because that is what the
    /// sheet says and what a report about that column should call it.
    ///
    /// spec/types/bitset.md has the notation and why it is a type rather than a role.
    /// </remarks>
    private static void FoldBitsetIntoInt64(Model model)
    {
        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields)
            {
                field.Type = field.Type switch
                {
                    ValueType.Bitset => ValueType.Int64,
                    ValueType.BitsetArray => ValueType.Int64Array,
                    _ => field.Type,
                };
            }

            // The wire columns snapshot a field's type and tag assignment has already built
            // them, so the fold is not visible until they are rebuilt.
            table.InvalidateDerivedColumns();
        }
    }

    /// <summary>
    /// Settles what each table's exported data file is called.
    /// </summary>
    /// <remarks>
    /// The one name in the model that several programs have to agree on: the exporter writes
    /// the file and the reader generated for each language opens it, and nothing downstream
    /// checks that the two arrived at the same string. So it is computed once, here, and read
    /// everywhere else.
    ///
    /// Blank spelling keeps the table's own name, which is what every recipe written before
    /// the setting existed holds - so every data file keeps the name it had.
    ///
    /// spec/targets/naming-conventions.md.
    /// </remarks>
    private static void NameDataFiles(Model model, RecipeModel recipeModel)
    {
        var spelling = DataFileCasing.From(recipeModel?.DataFileCase ?? "");

        foreach (var table in model.Tables)
        {
            table.DataFileName = spelling is null
                ? table.Name
                : table.Name.ToCase(spelling.Value);
        }
    }



    /// <summary>
    /// What a set promises for one member, as a sheet would write it in a `:type` cell.
    /// </summary>
    /// <remarks>
    /// Optionality is deliberately not part of it. Whether a blank cell is allowed is a fact
    /// about that table's data rather than about the surface a consumer reads, and a variant
    /// tightening or loosening it breaks no promise.
    /// </remarks>
    private static string Promised(Schema.SchemaTypeRef type)
        => type.Form == Schema.SchemaTypeForm.Foreign
            ? "foreign " + string.Join("|", type.ForeignTables.Select(name => name.ToPascalCase()))
            : type.Name + (type.IsArray ? "[]" : "");

    /// <summary>
    /// The same spelling for a column, or empty when this pass cannot tell what it holds.
    /// </summary>
    /// <remarks>
    /// Empty rather than a guess. A column whose type nothing here recognizes is one this
    /// check has no opinion about, and reporting an opinion it does not have would be a
    /// report the author cannot act on.
    /// </remarks>
    private static string SurfaceTypeOf(Field field)
    {
        if (field.RefTableName is { Length: > 0 } target)
            return "foreign " + target.ToPascalCase();

        if (field.TypeName is not { Length: > 0 } written || written == "$Unresolved$")
            return "";

        return written + (field.IsArray ? "[]" : "");
    }



    /// <summary>
    /// Every leaf of a record group, with the member names that reach it.
    /// </summary>
    /// <remarks>
    /// The leaf itself does not carry its path - a member knows its own name and nothing
    /// above it - and a name built from the last part alone would collide the moment two
    /// levels used it. spec/types/nested-multi-level.md.
    /// </remarks>
    private static IEnumerable<(IReadOnlyList<string> Path, RecordMember Leaf)> LeavesWithPath(
        SerialField group)
    {
        var stack = new List<string>();

        IEnumerable<(IReadOnlyList<string>, RecordMember)> Walk(RecordMember member)
        {
            stack.Add(member.Name);

            if (member.IsLeaf)
            {
                yield return (stack.ToList(), member);
            }
            else
            {
                foreach (var below in member.Members)
                foreach (var found in Walk(below))
                    yield return found;
            }

            stack.RemoveAt(stack.Count - 1);
        }

        foreach (var member in group.Members)
        foreach (var found in Walk(member))
            yield return found;
    }


    /// <summary>
    /// Turns every reference cell into a value of the key its target is addressed by, now
    /// that the target is known.
    /// </summary>
    /// <remarks>
    /// A reference cell holds the target's primary index, and what type that is only becomes
    /// answerable once references are resolved: the layout reading the cell has a table name
    /// and nothing else, and the table it names may not have been read yet. So the cell is
    /// kept as the sheet wrote it and converted here.
    ///
    /// The same shape as <see cref="FoldBitsetIntoInt64"/> - a pass that settles a type after
    /// every cell has been read - and for the same reason: the answer is a property of the
    /// whole model rather than of one sheet.
    ///
    /// A field whose reference did not resolve is skipped. Its failure is already reported
    /// and the run stops before anything reads these values.
    ///
    /// spec/references/reference-key-types.md.
    /// </remarks>
    private static void ConvertReferenceCells(
        CookingContext context, Model model, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields)
            {
                bool resolved = (field.IsRef && field.ResolvedRefTable is not null)
                    ;

                if (!resolved)
                    continue;

                // Every set of rows the table has, not only its own: a reference cell that
                // was not converted keeps the text the sheet wrote, and then matches nothing
                // the target holds. spec/layout/table-row-sets.md.
                foreach (var rowSet in table.RowSets)
                foreach (var row in rowSet.Rows)
                {
                    if (field.Index >= row.Count)
                        continue;

                    var cell = row[field.Index];

                    // An array of references arrives already split: the column was read as a
                    // delimited array of text while the sheet was open, because the key type
                    // was not known then. Each element is converted the way a scalar cell is.
                    if (cell.Value is string?[] parts)
                    {
                        try
                        {
                            cell.Value = context.ParseReferenceKeys(
                                field.RefKeyType, parts, cell.RawCell?.Location);
                        }
                        catch (TabbitException problem)
                        {
                            ReportUnparsableKey(
                                table, field, cell, string.Join(", ", parts), problem, diagnostics);
                        }

                        continue;
                    }

                    // A layout that parsed the cell itself hands over a value rather than
                    // the text of one - a column promoted from "these are the tables its
                    // value belongs to" was read as an ordinary number. Narrowed to the key
                    // the target is addressed by, which is what the column was holding all
                    // along and is the width it now travels at.
                    if (cell.Value is not string written)
                    {
                        cell.Value = NarrowToKey(cell.Value, field.RefKeyType)!;
                        continue;
                    }

                    try
                    {
                        // **A blank is an absent value, and this is where that gets recorded.**
                        // The comment here used to say `HasValue` carried it already - it did
                        // not: nothing on the reference path ever set it, because a reference
                        // column skips the conversion that does so for every other type. So a
                        // blank reference cell read as present holding the key type's empty
                        // value, and a polymorphic group's untouched columns - which are blank
                        // by design - were reported as values the row should not have.
                        // spec/references/reference-optionality.md and spec/types/polymorphism.md section 8.
                        if (written.Length == 0)
                            cell.HasValue = false;

                        // `required: false`, so a cell nobody filled in becomes the key
                        // type's empty value rather than a parse failure. Whether that blank
                        // was allowed is the validator's question.
                        cell.Value = context.ParseValue(
                            field.RefKeyType, null, written, cell.RawCell?.Location,
                            required: false);
                    }
                    catch (TabbitException problem)
                    {
                        ReportUnparsableKey(table, field, cell, written, problem, diagnostics);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Says a reference cell held something its target's key cannot be.
    /// </summary>
    /// <remarks>
    /// Said against the reference rather than against the type, because the author wrote a key
    /// and the type is the target's answer. The parser's own message names `Int32` and nothing
    /// else, which sends them looking at the wrong column.
    /// </remarks>
    private static void ReportUnparsableKey(
        Table table, Field field, Cell cell, string written,
        TabbitException problem, Diagnostics diagnostics)
    {
        string targets = field.ResolvedRefTable!.Name;

        // `Detail` is the caught parser's own message. The frame around it is translatable;
        // what it quotes stays as it arrived.
        diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
            Messages.Message.Of(CookingMessages.ReferenceKeyUnparsable,
                ("Table", table.Name), ("Field", field.Name),
                ("Targets", targets),
                ("KeyType", field.RefKeyType.ToString().ToLowerInvariant()),
                ("Written", written), ("Detail", problem.Message)));
    }

    /// <summary>
    /// A value a layout already parsed, in the type the key it stands for is written as.
    /// </summary>
    /// <remarks>
    /// The layouts that declare a reference by naming tables read the column as an ordinary
    /// number first, which for one of them means a `double`. The value is the target's key
    /// and always was; this is only the width it is carried at, and a whole number does not
    /// change crossing it. Anything that is not a whole number, or already the right type,
    /// is left exactly as it is.
    /// </remarks>
    private static object? NarrowToKey(object? value, ValueType keyType)
    {
        if (value is null)
            return null;

        double number;

        switch (value)
        {
            case double d: number = d; break;
            case float f: number = f; break;
            default: return value;
        }

        if (number != System.Math.Floor(number))
            return value;

        return keyType switch
        {
            ValueType.Int32 when number >= int.MinValue && number <= int.MaxValue => (int)number,
            ValueType.Int64 => (long)number,
            _ => value,
        };
    }

    /// <summary>
    /// Prints the reports that do not stop the run, and a count of everything found.
    /// </summary>
    /// <remarks>
    /// The same shape the validation pipeline uses on its own collector. Two copies of six
    /// lines rather than one shared helper, because the two stages differ in what they sort
    /// and when: that one runs its table rules in parallel and has to order the reports before
    /// printing them, and this one is single threaded and already in sheet order.
    /// </remarks>
    private static void Report(Diagnostics diagnostics)
    {
        foreach (var (severity, detail) in diagnostics.Entries)
        {
            string at = detail.Location is null ? "" : $"\n    at {detail.Location}";

            switch (severity)
            {
                case Severity.Info:
                    Log.Information($"  {detail.Message}{at}");
                    break;

                // Only the ones that let the run continue. A promoted warning is about to be
                // thrown with every other stopping report, and printing it here as well says
                // the same thing twice - which reads, to somebody counting, as twice as many
                // problems.
                case Severity.Warning when !diagnostics.PromoteWarnings:
                    Log.Warning($"  {detail.Message}{at}");
                    break;
            }
        }

        if (diagnostics.WarningCount > 0 || diagnostics.InfoCount > 0)
        {
            Log.Information(
                $"Validation: {diagnostics.ErrorCount} error(s), "
                + $"{diagnostics.WarningCount} warning(s), {diagnostics.InfoCount} note(s).");
        }
    }

    /// <summary>
    /// Hands each layout the sheets that named it, declarations first.
    /// </summary>
    /// <remarks>
    /// Two passes over the layouts rather than one pass each: a table column typed with an
    /// enum resolves by name, and in a project part-way through being converted the enum
    /// and the table that uses it will be in workbooks read under different layouts. Doing
    /// one layout completely before starting the next would make that work or not work
    /// depending on which order the recipe happened to list its sources in.
    /// </remarks>
    private void ParseRawModel(CookingContext context, RawModel rawModel)
    {
        Log.Information("Parsing raw-model...");

        var byLayout = GroupByLayout(rawModel);

        var parsers = byLayout
            .Select(group => (Parser: LayoutRegistry.Get(group.Key).CreateParser(), group.Value))
            .ToList();

        foreach (var (parser, sheets) in parsers)
            parser.ParseDeclarations(context, sheets);

        foreach (var (parser, sheets) in parsers)
            parser.ParseTables(context, sheets);

        ReportRowTags(context);
    }

    /// <summary>
    /// Lists the row tags this run saw and what they left out.
    /// </summary>
    /// <remarks>
    /// **The one place a misspelled tag shows.** Tag names are not declared, so a build that
    /// excludes `wip` and meets `wpi` drops nothing and has nothing to report as wrong - the
    /// line below is what lets somebody see it anyway. Silent otherwise, so a run of sheets
    /// that use no tags reads as it always has. spec/layout/tags.md.
    /// </remarks>
    private static void ReportRowTags(CookingContext context)
    {
        if (context.RowTags.Count == 0)
            return;

        context.Model.RowTags =
        [
            .. context.RowTags.Values
                .OrderBy(tag => tag.Written, System.StringComparer.OrdinalIgnoreCase)
                .Select(tag => new Models.RowTagUse
                {
                    Tag = tag.Written,
                    Rows = tag.Rows,
                    Omitted = tag.Omitted,
                }),
        ];

        var listed = context.RowTags.Values
            .OrderBy(tag => tag.Written, System.StringComparer.OrdinalIgnoreCase)
            .Select(tag => $"{tag.Written} on {tag.Rows} row(s), {tag.Omitted} left out");

        Log.Information($"Row tags: {string.Join("; ", listed)}.");
    }

    /// <summary>
    /// Sorts the sheets by the layout their source stamped on them, keeping the order the
    /// importers produced them in.
    /// </summary>
    private static Dictionary<string, List<RawSheet>> GroupByLayout(RawModel rawModel)
    {
        // The value type is the concrete list rather than IReadOnlyList. Holding the
        // interface and casting back to add read the same until a collection expression
        // was applied to the `new List<RawSheet>()`: an empty `[]` for an IReadOnlyList
        // target is an array, and the cast then threw on the first sheet of every run.
        // A parser takes IReadOnlyList, which List satisfies, so nothing needed the
        // wider type here.
        var result = new Dictionary<string, List<RawSheet>>();

        foreach (var sheet in rawModel.Sheets)
        {
            string id = (sheet.Layout ?? SheetLayout.Default).Id;

            if (!result.TryGetValue(id, out var sheets))
            {
                sheets = [];
                result.Add(id, sheets);
            }

            sheets.Add(sheet);
        }

        return result;
    }
}
