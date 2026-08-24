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
    /// asked for no report. spec/build-report.md.
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
        BindDeclaredStructs(context, result, declarations, diagnostics);


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
        // spec/table-row-sets.md.
        TableRowSets.Fold(context, rawModel.Sheets, diagnostics);

        // What each table's data file is called, settled here rather than by each of the
        // seventeen programs that need it. After the fold, so a table that turned out to be
        // another table's extra rows is not given a file name of its own.
        NameDataFiles(result, recipeModel);

        // A column that named a variant set names the tables in it from here on. Before
        // promotion, because everything below this line is meant to meet the expanded list
        // and never the name it was written as. spec/polymorphism.md section 4.
        ExpandVariantSetReferences(result, declarations, diagnostics);

        // A column whose sheet named the tables its value belongs to is a reference, and
        // this is where it becomes one. Before resolution, because resolution is what it is
        // being handed to.
        PromoteReferencedTablesToReferences(result);

        result.SolveTableCrossReferencings(diagnostics);

        // Now that the targets are known, the type that says which of them a row landed in.
        // After resolution because it is built from the resolved tables, and before anything
        // is generated because every generator emits it through the enumeration machinery it
        // already has.
        DeclareMultiTargetDiscriminators(result, diagnostics);

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

        // The reports this recipe has written down stop being reports that end the run, and
        // start being reports that say so. After every check, because an entry states how many
        // it accounts for and that can only be counted once. spec/known-problems.md.
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
    /// spec/naming-conventions.md.
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
    /// <summary>
    /// Replaces a reference that named a variant set with the tables in that set.
    /// </summary>
    /// <remarks>
    /// **`foreign Reward` and `foreign Item|CEquip` are one thing from here down.** The set is
    /// a name for the list, so expanding it early means the multi-target machinery - the
    /// existence check, the key-type agreement, the disjoint key bands, the per-target
    /// accessors - is reached without any of it learning a second spelling.
    ///
    /// What the name buys is where the list is written. A catalogue joins the set by declaring
    /// `extends=` on its own sheet, and every column pointing at the set follows without being
    /// edited. spec/polymorphism.md sections 3 and 4.
    ///
    /// The contract the set promises - that every variant carries the abstract struct's fields -
    /// is checked here too, because this is the first pass holding every table with its types
    /// settled.
    /// </remarks>
    private static void ExpandVariantSetReferences(
        Model model, Schema.SchemaDeclarations declarations, Diagnostics diagnostics)
    {
        if (declarations.IsEmpty)
            return;

        var sets = new Dictionary<string, List<Table>>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var table in model.Tables)
        {
            if (table.VariantOf is not { Length: > 0 } set)
                continue;

            if (!sets.TryGetValue(set, out var members))
                sets[set] = members = [];

            members.Add(table);
        }

        foreach (var (set, members) in sets)
            CheckVariantSurface(declarations, set, members, diagnostics);

        foreach (var table in model.Tables)
        {
            foreach (var field in table.Fields)
            {
                // Exactly one name, because a set is named on its own: mixing it with other
                // targets would be a list holding a list, and what that means for the
                // discriminator is not something the notation says.
                if (field.RefTableNames is not { Count: 1 } named)
                    continue;

                var declared = declarations.FindAbstract(named[0]);
                if (declared is null)
                    continue;

                if (!sets.TryGetValue(declared.Name.ToPascalCase(), out var members))
                {
                    diagnostics.Error(field.DetailTypeLocation, Messages.Message.Of(
                        CookingMessages.VariantSetHasNoTables,
                        ("Table", table.Name), ("Field", field.Name), ("Set", declared.Name)));
                    continue;
                }

                field.RefTableNames = members.Select(member => member.Name).ToList();
                field.RefTableName = members.Count == 1 ? members[0].Name : null;
            }
        }
    }

    /// <summary>
    /// Checks that every table in a set carries the columns the set's abstract struct declares.
    /// </summary>
    /// <remarks>
    /// This is the whole of what an abstract struct's members mean on the reference path: they
    /// are the surface a consumer may read without knowing which variant it holds, so a table
    /// missing one is a table that would break that promise for every column naming the set.
    ///
    /// A set whose struct declares no members promises nothing and is checked for nothing.
    /// That is a real case - section 3 keeps it, because a set is worth naming for the list
    /// alone.
    /// </remarks>
    private static void CheckVariantSurface(
        Schema.SchemaDeclarations declarations,
        string set,
        List<Table> members,
        Diagnostics diagnostics)
    {
        var declared = declarations.FindAbstract(set);
        if (declared is null)
            return;

        foreach (var promised in declared.LiveFields)
        {
            foreach (var table in members)
            {
                var carried = table.Fields
                    .FirstOrDefault(field => string.Equals(
                        field.Name, promised.Name.ToPascalCase(),
                        System.StringComparison.OrdinalIgnoreCase));

                if (carried is null)
                {
                    diagnostics.Error(table.VariantOfLocation ?? table.Location,
                        Messages.Message.Of(CookingMessages.VariantMissingSurface,
                            ("Table", table.Name), ("Set", declared.Name),
                            ("Member", promised.Name), ("Type", promised.Type.ToString())));

                    continue;
                }

                if (SurfaceTypeOf(carried) is { Length: > 0 } written
                    && !string.Equals(written, Promised(promised.Type),
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Error(carried.TypeLocation,
                        Messages.Message.Of(CookingMessages.VariantSurfaceTypeDiffers,
                            ("Table", table.Name), ("Set", declared.Name),
                            ("Member", promised.Name),
                            ("Type", Promised(promised.Type)), ("Written", written)));
                }
            }
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
        if (field.RefTableNames is { Count: > 0 } targets)
            return "foreign " + string.Join("|", targets.Select(name => name.ToPascalCase()));

        if (field.TypeName is not { Length: > 0 } written || written == "$Unresolved$")
            return "";

        return written + (field.IsArray ? "[]" : "");
    }

    private static void PromoteReferencedTablesToReferences(Model model)
    {
        foreach (var table in model.Tables)
        {
            // A member of a record group is promoted like any other column, whether it names
            // one table or several: the generated element carries the key, the row it
            // resolved to - or the slot and the discriminator where there is more than one
            // target - and the linking that fills them.
            // spec/references-in-records.md ~ spec/multi-target-accessors.md.
            //
            // One kind is still held back, and for a reason about names rather than targets.
            // An anonymous level is reached by number, so a reference in one has no name to
            // keep its key under - the same thing `ValidateRecordGroup` refuses for a column
            // declared `foreign`. It cannot refuse this one: it runs while the groups are
            // built, which is before this pass makes the column a reference at all.
            // spec/references-in-records.md.
            var heldBack = new HashSet<Field>(
                table.SerialFields
                    .Where(group => group.IsRecord && group.MembersAreAnonymous)
                    .SelectMany(group => group.Leaves.SelectMany(member => member.Fields)));

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
    /// Declares, for every column reaching several tables, the enumeration that says which
    /// of them a row's value is in.
    /// </summary>
    /// <remarks>
    /// **In the model rather than in each generator.** Every generator already turns
    /// <see cref="Model.Enums"/> into its language's enumeration, so declaring the type here
    /// means every one of them emits it with no new code - and they all spell it and case it
    /// the way they spell every other enumeration, which is what stops the same type being
    /// named three ways across a project's languages.
    ///
    /// One per declaration rather than one per distinct target list. Lists do repeat - a
    /// project's reward tables name the same sixteen catalogues from several columns - but
    /// merging them would mean inventing a name for the list, and the name of a thing the
    /// sheets did not declare is not this tool's to choose. A project that wants one
    /// declares an enum and points its columns at that instead.
    ///
    /// spec/multi-target-accessors.md.
    /// </remarks>
    private static void DeclareMultiTargetDiscriminators(Model model, Diagnostics diagnostics)
    {
        // Snapshotted, because the loop adds to the list it would otherwise be walking.
        var tables = model.Tables.ToList();

        foreach (var table in tables)
        {
            // A record group's member first, because its columns are one member: an array of
            // records spreads it over a column per element, and those elements share the
            // question "which table is this one in". Named after the member rather than the
            // column, or a group of two elements would declare two types for one member.
            // spec/multi-target-accessors.md.
            foreach (var group in table.SerialFields.Where(g => g.IsRecord))
            {
                foreach (var (path, leaf) in LeavesWithPath(group))
                {
                    var columns = leaf.Fields
                                      .Where(f => f.ResolvedRefTables is { Count: > 1 })
                                      .ToList();

                    if (columns.Count == 0)
                        continue;

                    string memberName = table.Name.ToPascalCase()
                        + group.Name.ToPascalCase()
                        + string.Concat(path.Select(part => part.ToPascalCase()))
                        + "Target";

                    var shared = DeclareDiscriminator(
                        model, table, columns[0], memberName, diagnostics);

                    if (shared is null)
                        continue;

                    // Every element of the member points at the one type.
                    foreach (var column in columns)
                        column.MultiTargetEnum = shared;
                }
            }

            foreach (var field in table.Fields)
            {
                if (field.ResolvedRefTables is not { Count: > 1 } || field.MultiTargetEnum is not null)
                    continue;

                string name = $"{table.Name.ToPascalCase()}{field.Name.ToPascalCase()}Target";

                var declared = DeclareDiscriminator(model, table, field, name, diagnostics);

                if (declared is not null)
                    field.MultiTargetEnum = declared;
            }
        }
    }

    /// <summary>
    /// Every leaf of a record group, with the member names that reach it.
    /// </summary>
    /// <remarks>
    /// The leaf itself does not carry its path - a member knows its own name and nothing
    /// above it - and a name built from the last part alone would collide the moment two
    /// levels used it. spec/nested-multi-level.md.
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
    /// The enumeration for one declaration, or null when the name is taken.
    /// </summary>
    private static Models.Enum? DeclareDiscriminator(
        Model model, Table table, Field field, string name, Diagnostics diagnostics)
    {
        // A sheet may already have declared this name, and then two different types
        // would be generated under it. Reported rather than renamed: a name this
        // tool made up silently is one nobody can search for.
        if (model.Enums.Exists(existing => existing.Name == name))
        {
            diagnostics.Error(field.DetailTypeLocation,
                Messages.Message.Of(CookingMessages.MultiTargetEnumNameTaken,
                    ("Table", table.Name), ("Field", field.Name), ("Enum", name)));
            return null;
        }

        var discriminator = new Models.Enum
        {
            Location = field.DetailTypeLocation ?? field.NameLocation,
            TargetSide = table.TargetSide,
            RawName = name,
            Name = name,
            Synthesized = true,
            Comment =
                $"Which table `{table.Name}.{field.Name}` points at. "
                + "The column carries one id and the tables it may be a row of take "
                + "separate id bands, so exactly one of them answers.",
        };

        // Zero is "points at nothing", which is what a column with no value holds and
        // what a key found in none of the targets leaves behind. Every other
        // enumeration in the model has a zero for the same reason.
        discriminator.Labels.Add(new Models.Enum.Label
        {
            RawName = "None",
            Name = "None",
            Value = 0,
            Synthesized = true,
            Location = discriminator.Location,
            Comment = "No row of any of them.",
        });

        int value = 1;
        foreach (var target in field.ResolvedRefTables!)
        {
            discriminator.Labels.Add(new Models.Enum.Label
            {
                RawName = target.Name,
                Name = target.Name.ToPascalCase(),
                Value = value++,
                Synthesized = true,
                Location = discriminator.Location,
                Comment = $"A row of `{target.Name}`.",
            });
        }

        model.Enums.Add(discriminator);
        return discriminator;
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

                // Every set of rows the table has, not only its own: a reference cell that
                // was not converted keeps the text the sheet wrote, and then matches nothing
                // the target holds. spec/table-row-sets.md.
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
        // Every target, not the resolved one: a column naming several has no single resolved
        // table, and reading the singular here dereferenced null the moment such a column held
        // a key it could not parse.
        string targets = field.ResolvedRefTables is not null
            ? string.Join("`, `", field.ResolvedRefTables.Select(t => t.Name))
            : field.ResolvedRefTable!.Name;

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
