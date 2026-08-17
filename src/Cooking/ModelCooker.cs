using System.Collections.Generic;
using System.Linq;
using Serilog;
using Tabbit.Cooking.Layouts;
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
    public Model Cook(Options options, RecipeModel recipeModel, RawModel rawModel)
    {
        var result = new Model();

        var context = new CookingContext(result, recipeModel);

        ParseRawModel(context, rawModel);

        // Resolution and validation share one collector, so a workbook comes back
        // with everything wrong with it rather than one problem per run.
        //
        // Warnings are promoted here on the same switch the validation pipeline reads. The
        // two stages report different things but they are the same judgement - "not this
        // time" - and a build that says it in one place and not the other has a gate with a
        // hole in it.
        var diagnostics = new Diagnostics
        {
            PromoteWarnings = recipeModel.Validation?.TreatWarningsAsErrors ?? false,
        };

        // Every cell has been read, so the two type kinds that existed for the reading are
        // done. Both are folds and neither is visible below this line.
        ExpandCompositeColumns(context, result, diagnostics);
        FoldBitsetIntoInt64(result);

        // A column whose sheet named the tables its value belongs to is a reference, and
        // this is where it becomes one. Before resolution, because resolution is what it is
        // being handed to.
        PromoteReferencedTablesToReferences(result);

        result.SolveTableCrossReferencings(diagnostics);

        // Only now is it known what a reference cell holds. The layout kept those cells as
        // written because the target's key type is not a fact any one sheet carries, and
        // this turns them into values of that type.
        ConvertReferenceCells(context, result, diagnostics);

        // Runs after resolution: validation follows references to check that what
        // they point at exists.
        //
        // The requested side is passed in so a narrowed run is checked against what it
        // will actually build. Without it, `--target-side client` could fail on a
        // problem that only exists in the server cut it is not producing.
        ValidateModel(result, recipeModel, CommandLineTargetSide.Of(options), diagnostics);

        // Printed before the throw, and printed at all: this stage only ever produced errors,
        // which the exception carried, so nothing here had to say anything about the reports
        // that do not stop a run. An asset that has not been drawn yet is exactly such a
        // report, and one nobody sees is one nobody writes.
        Report(diagnostics);

        diagnostics.ThrowIfAny("The workbook did not pass validation.");

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
    /// thirteen generators render it as their own name for that width, and the databases
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
    /// spec/bitset.md has the notation and why it is a type rather than a role.
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
    /// Turns a column that named the tables its value belongs to into a reference.
    /// </summary>
    /// <remarks>
    /// The declaration those layouts carry is a reference - it says a value is a row of
    /// another table - and the project it comes from stops at checking only because it has
    /// no code generation. This one does, so the column gets the accessor its declaration
    /// was always describing.
    ///
    /// **Only when every named table is in this build.** Generated code for a reference
    /// names the target's type, so a column pointing at a table this recipe does not read
    /// would emit code that cannot compile. Those columns stay what they were: an id, and
    /// the check in ValidateReferencedTables. spec/multi-target-references.md.
    /// </remarks>
    private static void PromoteReferencedTablesToReferences(Model model)
    {
        foreach (var table in model.Tables)
        {
            // A member of a record group is promoted like any other column now - the
            // generated element carries the row, the key, and the linking that fills it
            // (spec/references-in-records.md). Only the ones naming several tables are still
            // held back: what a reference to one of many looks like inside an element is the
            // per-target property that has not been designed yet, and promoting them would
            // turn columns that convert today into a refusal.
            // spec/multi-target-references.md.
            var heldBack = new HashSet<Field>(
                table.SerialFields
                    .Where(group => group.IsRecord)
                    .SelectMany(group => group.MembersAreAnonymous
                        // An anonymous level is reached by number, so a reference in one has
                        // no name to keep its key under - the same thing `ValidateRecordGroup`
                        // refuses for a column declared `foreign`. It cannot refuse this one:
                        // it runs while the groups are built, which is before this pass makes
                        // the column a reference at all. spec/references-in-records.md.
                        ? group.Leaves.SelectMany(member => member.Fields)
                        : group.Leaves
                               .SelectMany(member => member.Fields)
                               .Where(field => field.Constraints.ReferencedTables is { Count: > 1 })));

            foreach (var field in table.Fields)
            {
                if (heldBack.Contains(field))
                    continue;

                var named = field.Constraints.ReferencedTables;

                // Already a reference by its own notation, or not one at all.
                if (field.IsRef || named is null || named.Count == 0)
                    continue;

                if (named.Any(name => model.FindTable(name) is null))
                    continue;

                field.RefTableNames = named.ToList();

                // One target is an ordinary reference and takes the path every `foreign`
                // takes. Several is not one record, so it keeps carrying the key and the
                // generated accessors answer per target.
                if (named.Count == 1)
                {
                    field.RefTableName = named[0];
                    field.RefFieldName = null;
                    field.TypeName = "$Unresolved$";
                }
            }
        }
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
    /// spec/reference-key-types.md.
    /// </remarks>
    private static void ConvertReferenceCells(
        CookingContext context, Model model, Diagnostics diagnostics)
    {
        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields)
            {
                bool resolved = (field.IsRef && field.ResolvedRefTable is not null)
                    || field.ResolvedRefTables is not null;

                if (!resolved)
                    continue;

                foreach (var row in table.Data)
                {
                    if (field.Index >= row.Count)
                        continue;

                    var cell = row[field.Index];

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
                        // `required: false`, so a cell nobody filled in becomes the key
                        // type's empty value rather than a parse failure. Whether that blank
                        // was allowed is the validator's question and `HasValue` still
                        // carries it. spec/reference-optionality.md.
                        cell.Value = context.ParseValue(
                            field.RefKeyType, null, written, cell.RawCell?.Location,
                            required: false);
                    }
                    catch (TabbitException problem)
                    {
                        // Said against the reference rather than against the type, because
                        // the author wrote a key and the type is the target's answer. The
                        // parser's own message names `Int32` and nothing else, which sends
                        // them looking at the wrong column.
                        diagnostics.Error(cell.RawCell?.Location ?? field.NameLocation,
                            $"`{table.Name}.{field.Name}` references `{field.ResolvedRefTable!.Name}`, "
                            + $"which is addressed by `{field.RefKeyType.ToString().ToLowerInvariant()}`, "
                            + $"and `{written}` is not one. {problem.Message}");
                    }
                }
            }
        }
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
