using Tabbit.Models;
using Tabbit.Extensions;
using Tabbit.Helpers;
using Tabbit.Recipe;
using Tabbit.Targets;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Settings for the Python target.
/// </summary>
public sealed class PythonRecipe : IOutputRecipe
{
    /// <summary>Directory the package is written into. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Name of the generated package, which is also the directory it goes in and how a
    /// consumer imports it.
    /// </summary>
    public string PackageName { get; set; } = "gamedata";

    /// <summary>Module the generated types live in, inside the package.</summary>
    public string ModuleName { get; set; } = "tables";

    /// <summary>
    /// Name of the generated accessor class.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ModuleName"/>, which names the file it lands in. The default
    /// pair gives `tables.Tables`, which is where this target already put it before either
    /// was something a recipe could set.
    /// </remarks>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed. Off by
    /// default: one that ships its data alongside its code has no use for it.
    /// </summary>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// Whether generated files this run did not write are removed from <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// On, because the output is a file per table: delete a table from the sheets and its
    /// file stays behind naming types nothing declares any more. Only files carrying this
    /// tool's own header are removed, so a directory holding your own source is safe.
    ///
    /// Turn it off if you edit the generated files, which is a decision worth a line in a
    /// recipe.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
    public string TargetSide { get; set; } = "cs";

    /// <summary>
    /// Spelling of the generated record members. Blank keeps the one this language normally
    /// uses.
    /// </summary>
    /// <remarks>
    /// Takes `pascal`, `camel`, `snake` or `upper-snake`. For a project whose own code has a
    /// convention the generated code should match, which is the one place the two meet: every
    /// other generated name is a type, a file or a method, and those follow the language.
    ///
    /// It moves the members and nothing else. The type names, the lookup methods, the
    /// element-count constants and the data files stay as they are - a member's spelling is
    /// not a fact about any of them, and a setting that moved all of them together would be
    /// renaming the output rather than spelling it.
    /// </remarks>
    public string MemberCase { get; set; } = "";
}

/// <summary>
/// Emits a Python package: a module per table, per enum and per constant set, the accessor,
/// the binary reader, and an `__init__` that re-exports every generated name.
///
/// Records use `__slots__`. A localization table is tens of thousands of rows, and a
/// per-instance dictionary on each is the difference between tens of megabytes and a
/// few.
///
/// The shape lives in templates/python-*.sbn, one per kind of file, over the shared header
/// in python-file-head.sbn. Which siblings a file imports comes from
/// <see cref="TypeDependencies"/>.
/// </summary>
[TabbitTarget("python", TargetKind.CodeGeneration, Order = 70)]
public class PythonCodeGenerator : CodeGenerator<PythonRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private PythonRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting
    // is reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Snake;

    /// <summary>
    /// A record group generates a class and a list of it; a member column fills one of its
    /// attributes.
    /// </summary>
    /// <remarks>
    /// The tenth of the thirteen, and the same split as the nine before it - declaration per
    /// field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a class declared before the element type, and the read reaches it with a
    /// longer member path. The constructor makes it, so every value inside it starts where a
    /// scalar member would. spec/types/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// `dict` and `set`. The first keeps insertion order in this language; the second does
    /// not, which is what the list beside it is for. spec/types/set-and-map.md section 7.
    /// </summary>
    protected override bool SupportsContainers => true;

    /// <summary>
    /// An optional column becomes a `has_{field}` attribute beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `None`. Python would let a member simply be None and the check would read
    /// naturally, but `None` is also what an unresolved reference is - and a `str` column
    /// that reads a blank as `""` would then have two ways to say the same nothing.
    /// spec/types/optional-fields.md has the reasoning, which is the same one the other targets
    /// follow.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`find_by_stage_and_slot(stage_key, slot_key)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/types/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, PythonRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Snake, "python");

        Generate();
        WriteBinaryReaderRuntime();
        WriteInit();
    }

    private string PackageDir
        => System.IO.Path.Combine(_recipe.Path, _recipe.PackageName);

    /// <summary>The accessor type's name, in the casing a Python class uses.</summary>
    private string AccessorType => _recipe.AccessorName.ToPascalCase();

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Python into `{System.IO.Path.GetFullPath(PackageDir)}`");

        // The accessor constructs every table and links the references between them, so it
        // names each table class and no record type.
        Write(_recipe.ModuleName + ".py", "python-accessor.sbn", new PythonPartView
        {
            AccessorName = AccessorType,

            Imports = _model.Tables.Select(TableImport).ToList(),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table names the enums its fields are typed with. Not the tables it
            // references: resolution happens in the accessor, and importing them here
            // would turn two tables pointing at each other into an import cycle.
            Write(TableModule(pair.model) + ".py", "python-table.sbn", new PythonPartView
            {
                AccessorName = AccessorType,
                Imports = TypeDependencies.EnumsNamedBy(pair.model)
                                          .Select(EnumImport)
                                          .Concat(PolymorphicImports(pair.model))

                                          // And the declarations its groups are typed by,
                                          // each in a module of its own.
                                          // spec/types/declared-struct-identity.md.
                                          .Concat(pair.model.SerialFields
                                              .Where(group => group.DeclaredType.Length > 0)
                                              .Select(group =>
                                                  $"from .struct_{group.DeclaredType.ToSnakeCase()}"
                                                  + $" import {group.DeclaredType}")
                                              .Distinct())
                                          .ToList(),
                AccessorModule = _recipe.ModuleName,
                Table = pair.rendered,
            });
        }

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            // An enum is a leaf: enum.IntEnum comes from the standard library.
            Write(EnumModule(pair.model) + ".py", "python-enum.sbn", new PythonPartView
            {
                AccessorName = AccessorType,
                Imports = Array.Empty<string>(),
                Enumm = pair.rendered,
            });
        }

        // A struct is an entity beside a table and an enum, so it gets a module of its own -
        // one per declaration however many tables named it. spec/types/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            var structure = BuildStruct(declared);

            Write(structure.ModuleName + ".py", "python-struct.sbn", new PythonPartView
            {
                AccessorName = AccessorType,
                Imports = Array.Empty<string>(),
                Structure = structure,
            });
        }

        // And the declarations whose value is one shape, beside them for the same reason: the
        // package is one namespace, so a class declared per table would be two classes of one
        // name. spec/types/declared-struct-identity.md.
        foreach (var record in BuildRecordFiles())
        {
            Write(record.ModuleName + ".py", "python-record.sbn", new PythonPartView
            {
                AccessorName = AccessorType,
                Imports = RecordImports(record.Declared),
                Record = record,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant of an enum type renders as one of that enum's labels.
            Write(ConstantsModule(pair.model) + ".py", "python-constants.sbn", new PythonPartView
            {
                AccessorName = AccessorType,
                Imports = TypeDependencies.EnumsNamedBy(pair.model)
                                          .Select(EnumImport).ToList(),
                Set = pair.rendered,
            });
        }
    }

    /// <summary>
    /// Flat inside the package rather than in `tables/`, `enums/` and `constants/` as most
    /// targets do.
    /// </summary>
    /// <remarks>
    /// A Python subdirectory is a subpackage, so each would need an `__init__` of its own
    /// and every import would gain a level. Worse, `ModuleName` defaults to `tables`, and a
    /// `tables/` package sitting beside `tables.py` is resolved in favour of the package -
    /// the accessor would quietly stop being importable. The names carry the grouping
    /// instead, as they do for Go.
    /// </remarks>
    private void Write(string filename, string templateName, PythonPartView view)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(PackageDir, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    // ------------------------------------------------------- module layout

    /// <remarks>
    /// A table's own name first, as Go spells it: `template_table.py`, not
    /// `table_template.py`. An enum and a constant set take the prefix instead, because
    /// neither has a noun of its own to carry - `flag` alone does not say what it is, and
    /// `flag_enum` reads like a field.
    /// </remarks>
    private static string TableModule(Table table) => table.Name.ToSnakeCase() + "_table";
    private static string EnumModule(Models.Enum enumm) => "enum_" + enumm.Name.ToSnakeCase();
    private static string ConstantsModule(ConstantSet set) => "const_" + set.Name.ToSnakeCase();

    private static string TableImport(Table table)
        => $"from .{TableModule(table)} import {table.Name.ToPascalCase()}Table";

    private static string EnumImport(Models.Enum enumm)
        => $"from .{EnumModule(enumm)} import {enumm.Name.ToPascalCase()}";

    private void WriteBinaryReaderRuntime()
    {
        string runtime = System.IO.Path.Combine(PackageDir, "tabbit");

        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Python.tcb_reader.py",
            System.IO.Path.Combine(runtime, "tcb_reader.py"));

        // The subpackage's `__init__`, so `from . import tabbit` keeps naming the
        // reader's own symbols - which is what every generated module reaches for.
        // Two lines rather than making the reader itself the `__init__`: the file is
        // called tcb_reader in every language, and it should be here
        // too.
        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(runtime, "__init__.py")),
            string.Join("\n", new[]
            {
                "# ------------------------------------------------------------------------------",
                $"# {GeneratedFileMarker.Banner}",
                "# ------------------------------------------------------------------------------",
                "",
                "from .tcb_reader import *  # noqa: F401,F403",
                "",
            }));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // program that ships its data alongside its code.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Python.updater.py",
                System.IO.Path.Combine(runtime, "updater.py"));
        }
    }

    /// <summary>
    /// Writes the package's `__init__`, which re-exports every generated name so a consumer
    /// imports the package rather than a file inside it.
    /// </summary>
    /// <remarks>
    /// One `from .module import Name` per type, not `import *`. A star import would re-export
    /// whatever else a module happens to hold - `enum`, `os`, `tabbit` - and give a
    /// consumer no way to see what the package offers. It also means `__all__` is exact,
    /// which is what `from gamedata import *` reads.
    ///
    /// The order follows the dependency graph, so an interpreter that reads this file top to
    /// bottom loads enums before the tables that name them. Python does not require that -
    /// each module's own imports would pull what it needs - but a file whose order says
    /// something true is worth more than one whose order says nothing.
    /// </remarks>
    private void WriteInit()
    {
        var exported = new List<string>();
        var text = new StringBuilder();

        text.Append("# ------------------------------------------------------------------------------\n");
        text.Append($"# {GeneratedFileMarker.Banner}\n");
        text.Append("#\n");
        text.Append("# Changes to this file may cause incorrect behavior and will be lost if the code is\n");
        text.Append("# regenerated.\n");
        text.Append("# ------------------------------------------------------------------------------\n");
        text.Append('\n');

        foreach (var enumm in _model.Enums)
            Export(text, exported, EnumModule(enumm), enumm.Name.ToPascalCase());

        foreach (var set in _model.ConstantSets)
            Export(text, exported, ConstantsModule(set), set.Name.ToPascalCase());

        foreach (var table in _model.Tables)
        {
            // A record group's element type as well, so a caller can name what
            // `record.slot[0]` is by the same import as the row.
            var names = new List<string>
            {
                table.Name.ToPascalCase() + "Record",
                table.Name.ToPascalCase() + "Table",
            };

            // An array of arrays declares no element type - its outer level has no name - so
            // there is nothing to re-export and naming one would import what does not exist.
            // A shared type is exported from its own module, so naming it here would import
            // it from a module that does not hold it.
            // spec/types/declared-struct-identity.md.
            names.AddRange(table.SerialFields
                                .Where(sf => sf.IsRecord && !sf.MembersAreAnonymous
                                             && sf.DeclaredType.Length == 0)
                                .Select(sf => RecordTypeName(table, sf)));

            Export(text, exported, TableModule(table), names.ToArray());
        }

        // The declared record types, each from the module that holds it.
        // spec/types/declared-struct-identity.md.
        foreach (var record in BuildRecordFiles())
            Export(text, exported, record.ModuleName, record.Types.Select(t => t.TypeName).ToArray());

        Export(text, exported, _recipe.ModuleName, "Tables");

        text.Append('\n');
        text.Append("__all__ = [\n");

        foreach (string name in exported)
            text.Append("    \"").Append(name).Append("\",\n");

        text.Append("]\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(PackageDir, "__init__.py")),
            text.ToString());
    }

    private static void Export(StringBuilder text, List<string> exported, string module, params string[] names)
    {
        text.Append("from .").Append(module).Append(" import ").Append(string.Join(", ", names)).Append('\n');

        exported.AddRange(names);
    }

    // --------------------------------------------------------------- view

    private PythonFileView BuildView() => new PythonFileView
    {
        AccessorName = AccessorType,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private PythonEnumView BuildEnum(Models.Enum enumm)
    {
        var fallback = enumm.Labels.FirstOrDefault(label => label.Value == 0) ?? enumm.Labels[0];

        return new PythonEnumView
        {
            Name = enumm.Name.ToPascalCase(),
            Location = DocText(enumm.Location.ToString()),
            Comment = CommentLines(enumm.Comment),
            DefaultValue = fallback.Value.ToString(CultureInfo.InvariantCulture),
            Labels = enumm.Labels.Select(label => new PythonEnumLabelView
            {
                Name = PythonSnakeName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
            }).ToList(),
        };
    }

    private PythonConstantSetView BuildConstantSet(ConstantSet constantSet) => new PythonConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = DocText(constantSet.Location.ToString()),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new PythonConstantView
        {
            // Python constants are SCREAMING_SNAKE_CASE by convention.
            Name = constant.Name.ToUpperSnakeCase(),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    /// <summary>
    /// The lookups one record class declares beside its lists.
    /// </summary>
    /// <remarks>
    /// A `dict` keeps insertion order here and a `set` does not, which is the whole of why
    /// the list stays beside them. spec/types/set-and-map.md section 7.2.
    /// </remarks>
    private List<string> LookupLines(
        List<RecordMember> members, Models.ContainerKind own, SerialField group)
    {
        var lines = new List<string>();

        foreach (var plan in ContainerPlan.Of(members, own, group))
        {
            if (plan.IsMap)
            {
                string name = plan.ValueIsOneColumn ? "by_key" : "index_by_key";

                lines.Add(plan.ValueIsOneColumn
                    ? "# What each key is mapped to. Insertion order, which is the file's."
                    : "# Where each key sits among the entries - this map's value is an "
                      + "object, which is an attribute per column, so there is no one value "
                      + "to answer with.");

                lines.Add($"self.{name} = {{}}");

                continue;
            }

            lines.Add($"# The elements of {PythonName(plan.Source.Name)}, for asking whether "
                      + "one is there.");
            lines.Add($"self.{PythonName(plan.Source.Name)}_set = set()");
        }

        return lines;
    }

    /// <summary>The statements filling every lookup in a table, once the rows are read.</summary>
    private List<string> ContainerFillLines(Table table)
    {
        var lines = new List<string>();

        foreach (var plan in ContainerPlan.Of(table))
        {
            string access = "record." + PythonName(plan.Group.Name)
                + string.Concat(plan.Path.Select(name => "." + PythonName(name)));

            string source = access + "." + PythonName(plan.Source.Name);

            if (plan.IsMap)
            {
                string name = access + "."
                    + (plan.ValueIsOneColumn ? "by_key" : "index_by_key");

                string stored = plan.ValueIsOneColumn
                    ? access + "." + PythonName(plan.Value!.Name) + "[j]"
                    : "j";

                lines.Add($"for j in range(len({source})):");
                lines.Add($"    {name}[{source}[j]] = {stored}");

                continue;
            }

            lines.Add($"for j in range(len({source})):");
            lines.Add($"    {access}.{PythonName(plan.Source.Name)}_set.add({source}[j])");
        }

        return lines;
    }

    private PythonTableView BuildTable(Table table)
    {
        var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();

        // A reference contributes its index as well as its value, and an optional column its
        // presence flag. Every one of them needs a slot.
        var slots = new List<string>();
        foreach (var sf in table.SerialFields)
        {
            // The column's name is the key's; the row takes the derived one.
            // spec/references/reference-surface-naming.md sections 4 and 5.
            slots.Add(PythonName(sf.Name));

            // Where the built variant is kept, so a row read twice builds once. A slot rather
            // than a dict entry, because `__slots__` is what keeps a row from carrying one.
            // spec/types/polymorphism.md section 7.2.
            if (sf.IsRecord && sf.Members.Any(
                    m => m.IsLeaf && m.FirstField is { IsDiscriminator: true }))
            {
                slots.Add("_" + PythonName(sf.Name) + "_value");
            }

            if (sf.IsRef)
                slots.Add(ResolvesToRow(sf.FirstField!)
                    ? PythonName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                    : PythonName(sf.Name) + "_index");

            if (sf.RowMayBeAbsent)
                slots.Add(PresenceMember(sf));

            // And the per-element answer, which is a list rather than a flag.
            // spec/types/nullable-array-elements.md.
            if (sf.ElementMayBeAbsent)
                slots.Add(ElementPresenceMember(sf));
        }


        return new PythonTableView
        {
            RawName = table.Name,
            RecordName = table.Name.ToPascalCase() + "Record",
            TableName = table.Name.ToPascalCase() + "Table",
            Location = DocText(table.Location.ToString()),
            Comment = CommentLines(table.Comment),
            Indexes = Indexes(table),
            ContainerFill = ContainerFillLines(table),
            // The column axis is held on the table, so it is a slot like the maps are - a
            // class with `__slots__` has nowhere else to put it.
            TableSlotNames = Tuple(
                new[] { "records" }
                    .Concat(Indexes(table).Select(index => index.MapName))
                    .Concat(table.Matrix is null
                        ? System.Array.Empty<string>()
                        : new[] { "_column_axis" })
                    .ToList()),
            SlotNames = Tuple(slots),
            ReprFormat = string.Join(", ", table.SerialFields.Select(sf => PythonName(sf.Name) + "=%r")),
            ReprValues = Tuple(table.SerialFields.Select(sf => "self." + PythonName(sf.Name)).ToList(),
                               quote: false),
            Fields = fields,

            // A separate list, because declaring an attribute is per field and reading is
            // per column - and a record group is one column per member of it.
            Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
            Matrix = BuildMatrix(table),
        };
    }

    /// <summary>The grid accessor for this table, or null when it is not a grid's values.</summary>
    private PythonMatrixView? BuildMatrix(Table table)
    {
        if (MatrixPlans.Of(table, _model) is not { } plan)
            return null;

        return new PythonMatrixView
        {
            // The table's name rather than its class: Python needs no type here, and the only
            // place the name appears is a message - where the table is what a reader is
            // looking for. Naming the class would also be naming a type this module cannot
            // see, which is a thing the generated packages are checked for.
            ColumnTable = plan.Columns.Name.ToPascalCase(),
            RowKeyMember = PythonName(plan.RowKey.Name),
            RowLookup = "find_by_" + plan.RowKey.Name.ToSnakeCase(),
            ColumnKeyMember = PythonName(plan.ColumnKey.Name),
            ColumnLookup = "find_by_" + plan.ColumnKey.Name.ToSnakeCase(),
            AtMember = PythonName(plan.At.Name),
            GridMember = PythonName(plan.Grid.Name),
            GridHasMember = PythonName("has_" + plan.Grid.Name + "_at"),
            CellsAreOptional = plan.CellsAreOptional,
        };
    }

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<PythonIndexView> Indexes(Table table)
        => KeyPlans.Of(table).Select(plan =>
        {
            string suffix = plan.Suffix(name => name.ToSnakeCase(), "_and_");

            // **A component that is a reference carries the target's key, not its row.**
            // The two are one edit apart - the column's own name holds the key and the
            // derived name holds the row - and a lookup taking rows is one nobody can
            // call. `KeyComponentView.TypeOf` is the one place that decides, so the type and the
            // shape the key text is built from cannot disagree.
            var components = plan.Components.Select(component =>
            {
                var (keyType, keyEnum) = KeyComponentView.TypeOf(component);

                return new KeyComponentView
                {
                    Param = KeyComponentView.ParamOf(component.Name).ToSnakeCase(),
                    Type = "",
                    Member = PythonName(component.Name),
                    Kind = KeyComponentView.KindOf(keyType),
                };
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new PythonIndexView
            {
                Member = PythonName(plan.Only.Name),
                Suffix = suffix,
                MapName = "by_" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                IsPrimary = plan.IsPrimary,
                Components = components,

                Params = plan.IsComposite ? args : "key",

                Argument = plan.IsComposite
                    ? "self._key_of_" + suffix + "(" + args + ")"
                    : "key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(_ => "%r")) + ")"
                    : "%r",

                ValueArgs = plan.IsComposite ? args : "key",
            };
        }).ToList();

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `find_by_index`. The
    /// primary index is whatever the sheet put in the first column, and a sheet that
    /// calls it `Id` generates `find_by_id`.
    /// </remarks>
    private static string PrimaryLookup(Table? refTable)
        => "find_by_" + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToSnakeCase();

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// The key type's empty value means "points at nothing", and a multi-target column honours
    /// that in every language: the discriminator is a value a consumer reads, so a language
    /// that resolved a zero where another did not would answer a different table for the same
    /// row. spec/references/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType == ValueType.String ? "!= \"\"" : "!= 0";


    private PythonFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = PythonName(sf.Name);
        bool nullable = sf.RowMayBeAbsent;

        var initializers = Initializers(sf, name).ToList();

        // False until the read says otherwise, so a file that does not carry the column
        // leaves the attribute absent rather than claiming a value it never got.
        if (nullable)
            initializers.Add($"self.{PresenceMember(sf)} = False");

        // Empty until the read fills it, for the same reason. An index into an empty list is
        // out of range, and `has_x_at` answers true there.
        if (sf.ElementMayBeAbsent)
            initializers.Add($"self.{ElementPresenceMember(sf)} = []");

        return new PythonFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Initializers = initializers,
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<PythonRecordMemberView>(),
            RecordSlotNames = "",
            RecordReprFormat = "",
            RecordReprValues = "",
            IsNullable = nullable,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = ElementPresenceMember(sf),
        };
    }

    /// <summary>
    /// A record group: the class to declare for one element, and the attribute holding one
    /// or a list of them.
    /// </summary>
    /// <remarks>
    /// A module-level class rather than a nested one, because the package re-exports every
    /// generated name and a consumer should be able to reach an element type by the same
    /// import as the row. That is also why the name carries the table's.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index list a reference would be carried as.
    /// </remarks>
    /// <summary>
    /// Members of one level of a record, declaring a class for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the classes it produces. A nested member is constructed in the constructor,
    /// which is how the values inside it reach the empty values a scalar member gets - and it
    /// means the nested class has to be declared first, because that call runs at construction
    /// against a name resolved at import. spec/types/nested-multi-level.md.
    /// </remarks>
    /// <param name="inShared">
    /// Whether these members sit inside a type a declaration owns. Everything below such
    /// a level is written where that type is. spec/types/declared-struct-identity.md.
    /// </param>
    private List<PythonRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<PythonRecordTypeView> declared, bool inShared = false)
    {
        var result = new List<PythonRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(new PythonRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The list is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    Initializers = MemberInitializers(member),
                });

                continue;
            }

            // A level below. The class name carries the path so two records each holding a
            // `Position` do not name one class twice.
            string typeName = member.DeclaredType.Length > 0
                ? member.DeclaredType
                : prefix + member.Name.ToPascalCase();

            bool nestedIsShared = inShared || member.DeclaredType.Length > 0;
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared, nestedIsShared);

            declared.Add(new PythonRecordTypeView
            {
                TypeName = typeName,
                IsShared = nestedIsShared,
                Members = nested,
                IsOutermost = false,
                Lookups = LookupLines(member.Members, member.Container, group),
                Owner = $"{table.Name.ToPascalCase()}Record.{PythonName(group.Name)}",
                SlotNames = Tuple(MemberSlotNames(member.Members, member.Container)),
                ReprFormat = string.Join(", ", member.Members.Select(m => PythonName(m.Name) + "=%r")),
                ReprValues = Tuple(
                    member.Members.Select(m => "self." + PythonName(m.Name)).ToList(), quote: false),
            });

            result.Add(new PythonRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Initializers = new[] { $"self.{PythonName(member.Name)} = {typeName}()" },
            });
        }

        return result;
    }

    /// <summary>
    /// One abstract type and its variants, as the template reads them.
    /// </summary>
    /// <remarks>
    /// Classes and `isinstance`. This language is not in section 7's third group after all - it
    /// has inheritance, so the same shape C# takes reads naturally here.
    /// spec/types/polymorphism.md section 7.
    /// </remarks>
    /// <summary>
    /// The imports a table needs for the abstract types its groups are.
    /// </summary>
    /// <remarks>
    /// The variants and not the base: the built value names each variant, and the base is only
    /// what they inherit. spec/types/polymorphism.md section 7.1.
    /// </remarks>
    private IEnumerable<string> PolymorphicImports(Table table)
    {
        foreach (string name in table.Fields
                     .Where(field => field.IsDiscriminator && field.AbstractTypeName is not null)
                     .Select(field => field.AbstractTypeName!.ToPascalCase())
                     .Distinct())
        {
            var declared = _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == name);

            if (declared is null)
                continue;

            string module = "struct_" + declared.Name.ToSnakeCase();
            string names = string.Join(
                ", ", new[] { name }.Concat(declared.Variants.Select(v => v.Name)));

            yield return $"from .{module} import {names}";
        }
    }

    /// <summary>The declared record types, each with the levels inside it that are its own.</summary>
    /// <remarks>spec/types/declared-struct-identity.md.</remarks>
    private IReadOnlyList<PythonRecordFileView> BuildRecordFiles()
        => _model.RecordTypes
            .Select(declared =>
            {
                var types = new List<PythonRecordTypeView>();

                var owner = _model.Tables.FirstOrDefault(
                                table => table.SerialFields.Any(
                                    group => group.DeclaredType == declared.Name))
                            ?? _model.Tables.First(
                                table => table.SerialFields.Any(group => group.IsRecord));

                var group = owner.SerialFields.FirstOrDefault(
                                candidate => candidate.DeclaredType == declared.Name)
                            ?? owner.SerialFields.First(candidate => candidate.IsRecord);

                var members = BuildRecordMembers(
                    declared.Members, declared.Name, owner, group, types);

                types.Add(new PythonRecordTypeView
                {
                    TypeName = declared.Name,
                    IsShared = true,
                    Members = members,
                    IsOutermost = true,
                    Lookups = LookupLines(declared.Members, Models.ContainerKind.None, group),
                    Owner = declared.Name,
                    SlotNames = Tuple(MemberSlotNames(declared.Members, Models.ContainerKind.None)),
                    ReprFormat = string.Join(
                        ", ", declared.Members.Select(m => PythonName(m.Name) + "=%r")),
                    ReprValues = Tuple(
                        declared.Members.Select(m => "self." + PythonName(m.Name)).ToList(),
                        quote: false),
                });

                return new PythonRecordFileView
                {
                    Declared = declared,
                    Name = declared.Name,
                    ModuleName = "struct_" + declared.Name.ToSnakeCase(),
                    Comment = CommentLines(declared.Comment),
                    Types = types.Where(type => type.TypeName == declared.Name
                                                || !type.IsShared).ToList(),
                };
            })
            .ToList();

    /// <summary>
    /// What a declared record type's module has to bring in.
    /// </summary>
    /// <remarks>
    /// The enums its members are typed with, and the declarations it holds. A reference member
    /// carries a key rather than a row here, so no table is named.
    /// spec/types/declared-struct-identity.md.
    /// </remarks>
    private IReadOnlyList<string> RecordImports(Models.RecordType declared)
    {
        var lines = new List<string>();

        foreach (var field in declared.Members.SelectMany(member => member.AllFields))
        {
            if (field.ElementType == ValueType.Enum)
                Add($"from .enum_{field.Enum.Name.ToSnakeCase()} import {field.Enum.Name}");
        }

        foreach (string nested in NestedDeclarations(declared.Members))
            Add($"from .struct_{nested.ToSnakeCase()} import {nested}");

        return lines;

        void Add(string line)
        {
            if (!lines.Contains(line))
                lines.Add(line);
        }
    }

    /// <summary>Every declaration named below these members, at any depth.</summary>
    private static IEnumerable<string> NestedDeclarations(IEnumerable<RecordMember> members)
    {
        foreach (var member in members)
        {
            if (member.DeclaredType.Length > 0)
                yield return member.DeclaredType;

            foreach (string deeper in NestedDeclarations(member.Members))
                yield return deeper;
        }
    }

    private PythonPolymorphicTypeView BuildStruct(Models.PolymorphicType declared)
        => new PythonPolymorphicTypeView
        {
            Name = declared.Name,
            ModuleName = "struct_" + declared.Name.ToSnakeCase(),
            BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
            Variants = declared.Variants
                .Select(variant => new PythonVariantView
                {
                    TypeName = variant.Name,
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),
        };

    /// <summary>One member of an abstract type or of one of its variants.</summary>
    /// <remarks>
    /// **A reference member is two of these**, as a reference is anywhere: the declared name is
    /// the key's and the row it resolves to takes the derived one. A variant carrying only the
    /// key would hand a consumer a key where the declaration promised a row.
    /// spec/references/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private PythonStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;
        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new PythonStructMemberView
        {            Name = PythonName(raw),
            TypeName = "",
            Comment = CommentLines(field.Comment),
            RowName = toRow
                ? PythonName(RowAccessorName(field.ResolvedRefTable!.Name, raw))
                : "",
            KeyTypeName = field.IsRef
                ? ""
                : "",
        };
    }

    private PythonFieldView BuildRecordField(Table table, SerialField sf)
    {
        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it. spec/types/polymorphism.md section 7.1.
        var declaredType = sf.Members
                .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                ?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        string name = PythonName(sf.Name);
        string entry = RecordTypeName(table, sf);

        // Innermost first, and here that is required rather than tidy: a class body naming
        // another runs at import time, so the one it names has to exist already.
        var recordTypes = new List<PythonRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, entry, table, sf, recordTypes,
            inShared: sf.DeclaredType.Length > 0);

        recordTypes.Add(new PythonRecordTypeView
        {
            TypeName = entry,
            IsShared = sf.DeclaredType.Length > 0,
            Members = members,
            IsOutermost = true,
            Lookups = LookupLines(sf.Members, sf.Container, sf),
            Owner = $"{table.Name.ToPascalCase()}Record.{name}",
            SlotNames = Tuple(MemberSlotNames(sf.Members, sf.Container)),
            ReprFormat = string.Join(", ", sf.Members.Select(m => PythonName(m.Name) + "=%r")),
            ReprValues = Tuple(
                sf.Members.Select(m => "self." + PythonName(m.Name)).ToList(), quote: false),
        });

        // A list with its elements already made, where the length is the sheet's column
        // count. A trimmed group starts empty, because its length is the row's.
        // An array of arrays needs no element type: the outer level has no name for one to
        // belong to, so the inner list is what an element is. spec/types/nested-multi-level.md.
        string initializer = sf.MembersAreAnonymous
            ? $"self.{name} = [[{MemberDefault(sf.Members[0])}] * {sf.RecordElementCount} "
              + $"for _ in range({sf.Members.Count})]"
            : sf.IsArray
                ? (table.TrimTrailingArrayElements
                    ? $"self.{name} = []"
                    : $"self.{name} = [{entry}() for _ in range({sf.RecordElementCount})]")
                : $"self.{name} = {entry}()";

        return new PythonFieldView
        {
            VariantsAreArray = declaredType is not null && sf.IsArray,
            EntryAccess = "entry",
            AbstractTypeName = declaredType?.Name ?? "",
            DiscriminatorName = declaredType is null
                ? ""
                : PythonName(sf.Members
                    .First(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                    .Name),
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new PythonVariantView
                {
                    TypeName = variant.Name,
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),

            // A record has no header cell of its own, so the first member's column comment is
            // the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),

            Name = name,
            Initializers = new[] { initializer },
            IsRecord = true,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            RecordTypeName = entry,
            Members = members,
            RecordTypes = recordTypes,
            RecordSlotNames = Tuple(MemberSlotNames(sf.Members, sf.Container)),
            RecordReprFormat = string.Join(", ", sf.Members.Select(m => PythonName(m.Name) + "=%r")),
            RecordReprValues = Tuple(
                sf.Members.Select(m => "self." + PythonName(m.Name)).ToList(), quote: false),

            // A record group has no presence of its own: absence inside one is the list's
            // length, not a bit per member. WireColumn.Of says the same about the wire.
            IsNullable = false,
            PresenceMember = "",
        };
    }

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where its values
    /// land.
    /// </summary>
    /// <remarks>
    /// The member it fills is the group's for a scalar column. A record group's member
    /// columns each fill one attribute of the generated element type, which is the whole of
    /// the difference - see spec/types/nested-fields.md.
    /// </remarks>
    private PythonColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new PythonColumnView
        {
            WireTag = wire.TagCarrier.WireTag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = PythonName(wire.Group.Name),

            // A record's member column assigns one attribute of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + PythonName(part))),
            // A reference member reads into the key beside the row it will resolve to, and the
            // suffix goes on the member rather than after the subscript, because a member that
            // is an array holds one key per element. spec/references/references-in-records.md.
            MemberRefSuffix = "",

            KeyName = wire.IsRef && !ResolvesToRow(wire.TagCarrier)
                ? PythonName(wire.Group.Name) + "_index"
                : PythonName(wire.Group.Name),

            RowName = wire.IsRef && wire.TagCarrier.ResolvedRefTable is not null
                        && ResolvesToRow(wire.TagCarrier)
                ? PythonName(RowAccessorName(wire.TagCarrier.ResolvedRefTable.Name, wire.Group.Name))
                : PythonName(wire.Group.Name),
            MemberAt = wire.MemberAt,

            RecordTypeName = wire.Group.IsRecord ? RecordTypeName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ReadScalar = UsesCursor(wire) ? CursorReadExpression(wire) : ReadExpression(wire),
            ReadElement = UsesCursor(wire) ? CursorReadExpression(wire) : ReadExpression(wire),
            LengthRead = UsesCursor(wire)
                ? "element_count = cursor.next_length()"
                : "element_count = reader.read_counter32()",
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = PresenceMember(wire.Group),
            ElementPresenceMember = ElementPresenceMember(wire.Group),
            EmptyValue = EmptyValue(wire),
        };
    }

    /// <summary>
    /// The element type of a record group, which carries the table's name.
    /// </summary>
    /// <remarks>
    /// The package's `__init__` re-exports every generated name into one namespace, so two
    /// tables each holding a `Slot` group would be the same name exported twice.
    /// </remarks>
    private static string RecordTypeName(Table table, SerialField sf)
        => sf.DeclaredType.Length > 0
            ? sf.DeclaredType
            : table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    /// <summary>
    /// The attribute a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : PythonName("has_" + sf.Name);

    /// <summary>The attribute holding which of an array's elements have a value.</summary>
    private string ElementPresenceMember(SerialField sf)
        => sf.IsRecord ? "" : PythonName("has_" + sf.Name + "_at");

    /// <summary>What one record member starts at, for the same reason an ordinary one does.</summary>
    /// <summary>
    /// What one leaf member of a record element starts as.
    /// </summary>
    /// <remarks>
    /// Two lines for a reference member rather than one: the row it resolved to, and the key
    /// that came off the wire. Both inside the element, because a group may hold more than one
    /// reference and a name built from the group and the target would collide the moment two
    /// members point at one table.
    ///
    /// Nothing for whether it resolved - the row is None until the linking pass fills it, and
    /// that is the same answer a reference outside a record gives.
    /// spec/references/references-in-records.md.
    /// </remarks>
    private IReadOnlyList<string> MemberInitializers(RecordMember member)
    {
        string name = PythonName(member.Name);

        if (member.IsRef)
        {
            string key = RefKeyDefault(member.FirstField!.RefKeyType);

            // The member's own name is the key's; the row takes the derived one.
            // spec/references/reference-surface-naming.md sections 4 and 5.
            bool toRow = ResolvesToRow(member.FirstField!);
                    string rowName = toRow
                        ? PythonName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                        : PythonName(member.Name);
                    string keyName = toRow ? PythonName(member.Name) : PythonName(member.Name) + "_index";

            return member.IsArray
                ? new[]
                {
                    $"self.{keyName} = [{key}] * {member.Fields.Count}",
                    $"self.{rowName} = [None] * {member.Fields.Count}",
                }
                : new[]
                {
                    $"self.{keyName} = {key}",
                    $"self.{rowName} = None",
                };
        }


        // The list is the member's when the group is one record - same columns, same wire,
        // and only which of the two owns it differs. Or its own cell holds the list, which
        // is what a `set` and a `map` are: empty then, because no declaration knows how long
        // a row's list is. spec/types/set-and-map.md section 4.
        return member.ListIsInTheCell
            ? new[] { $"self.{name} = []" }
            : member.IsArray
                ? new[] { $"self.{name} = [{MemberDefault(member)}] * {member.Fields.Count}" }
                : new[] { $"self.{name} = {MemberDefault(member)}" };
    }


    /// <summary>What a stored key holds before a row is read.</summary>
    private static string RefKeyDefault(ValueType keyType)
        => keyType switch
        {
            ValueType.String or ValueType.Uuid => "\"\"",
            _ => "0",
        };

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// No resolution flag: an unresolved row stays as it started, which is how this output
    /// already answers that for a reference outside a record. What the loop walks says which
    /// of the three record shapes this is - the group's list, the member's, or neither.
    /// spec/references/references-in-records.md.
    /// </remarks>
    private PythonRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = PythonName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + PythonName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsArray;

        string rowLeaf = wire.Member is not null
            ? PythonName(RowAccessorName(refTable!.Name, wire.MemberPath[^1]))
            : PythonName(RowAccessorName(refTable!.Name, wire.Group.Name));

        string rowMember = wire.Member is not null
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + PythonName(part))) + "." + rowLeaf
            : "";

        string rowPath = wire.Member is not null
            ? (!isArray || wire.MemberOwnsTheArray
                ? $"record.{name}{rowMember}"
                : $"record.{name}[i]{rowMember}")
            : $"record.{rowLeaf}";

        string path = !isArray || wire.MemberOwnsTheArray
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";
        string subscript = (isArray && wire.MemberOwnsTheArray) ? "[i]" : "";

        return new PythonRecordReferenceView
        {
            Access = rowPath + subscript,
            Key = path + subscript,

            // Whichever list holds the elements. Its own length rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Range = isArray
                ? (wire.MemberOwnsTheArray
                    ? $"range(len({path}))"
                    : $"range(len(record.{name}))")
                : "",

            RefTable = PythonName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    /// <summary>
    /// The `__slots__` one level of a record declares.
    /// </summary>
    /// <remarks>
    /// A reference member is two rather than one: the row it resolved to, and the key that
    /// came off the wire. Built from the same list the initializers are, because a slot list
    /// that is short by one turns an assignment the read makes into an AttributeError - which
    /// is what `__slots__` is for. spec/references/references-in-records.md.
    /// </remarks>
    private IReadOnlyList<string> MemberSlotNames(
        IEnumerable<RecordMember> members, Models.ContainerKind own = Models.ContainerKind.None)
    {
        var result = new List<string>();
        var listed = members.ToList();

        foreach (var member in listed)
        {
            result.Add(PythonName(member.Name));

            if (member.IsLeaf && member.IsRef)
                result.Add(ResolvesToRow(member.FirstField!)
                    ? PythonName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                    : PythonName(member.Name) + "_index");
        }

        // A lookup is an attribute like any other, and `__slots__` naming none of them would
        // make assigning to one raise. From the same plan the declarations come from, so the
        // two cannot drift. spec/types/set-and-map.md section 7.
        foreach (var plan in ContainerPlan.Of(listed, own, EmptyGroup))
        {
            result.Add(plan.IsMap
                ? (plan.ValueIsOneColumn ? "by_key" : "index_by_key")
                : PythonName(plan.Source.Name) + "_set");
        }

        return result;
    }

    /// <summary>
    /// A stand-in for the group, where what is being asked about is one record type rather
    /// than where in the table it sits.
    /// </summary>
    private static readonly SerialField EmptyGroup = new SerialField();

    private string MemberDefault(RecordMember member)
    {
        switch (member.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "False";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "tabbit.Uuid()";
            case ValueType.Enum: return $"{member.FirstField!.Enum.Name.ToPascalCase()}(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// What an absent row's attribute is set back to, so the binary path lands where the
    /// JSON one does.
    /// </summary>
    /// <remarks>
    /// The attribute's own shape rather than its element's: an optional array holds a list,
    /// and its empty value is an empty list rather than a zero of what it holds.
    /// </remarks>
    private string EmptyValue(WireColumn wire)
    {
        if (wire.IsArray)
            return "[]";

        // The resolved attribute points at the target row, and absence there is what None
        // says.
        if (wire.ElementType == ValueType.ForeignRecord)
            return "None";

        switch (wire.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "False";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "tabbit.Uuid()";
            case ValueType.Enum: return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from - the elements that stay raw by spec excepted.
    /// </summary>
    private static bool UsesCursor(WireColumn wire)
    {
        // Uuid is the exception, and the same one it has always been: no encoding applies
        // to it, so it has no cursor path to reach.
        if (wire.ElementType == ValueType.Uuid)
            return false;

        // Arrays go through it too. An array block states an encoding for its elements and
        // one for its rows' lengths, and the cursor is what decodes both - so an array's
        // elements are read exactly the way a scalar column's are, one level down.
        if (wire.IsArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional yes
        // was the int32 assumption in another place: a target keyed by `uuid` has no cursor
        // path any more than a `uuid` column does. spec/references/reference-key-types.md.
        if (wire.IsRef)
            return wire.RefKeyType != ValueType.Uuid;

        switch (wire.ElementType)
        {
            // Int64 and Double are here for their promotions as well as their own
            // dictionaries: the file may carry an i32 column - encoded - where the
            // member has since widened.
            case ValueType.Int32:
            case ValueType.Int64:
            case ValueType.Double:
            case ValueType.Float:
            case ValueType.Bool:
            case ValueType.Enum:
            case ValueType.String:

            // Ticks are an i64 column, so they meet the i64 dictionary like any other.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly. Python has no block scope, so the
    /// assignment needs no declaration to sit under.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor = tabbit.ColumnCursor(reader, column, count, \"{tableName}.{wire.Name}\")"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to the int32 or the string read:
    /// int32 members, enums, references and strings. The other cursor scalars stay
    /// per-row - their encodings are dictionaries, where the per-row work is already one
    /// index lookup.
    /// </remarks>
    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row
        // does not have one of. Its elements are read one at a time.
        if (wire.IsArray)
            return "";

        // A reference runs on the key it carries, which is not always an int32. An enum's
        // underlying value is one. spec/references/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int32 => "next_same_i32",
                ValueType.String => "next_same_string",
                _ => "",
            };
        }

        if (wire.ElementType == ValueType.Enum)
            return "next_same_i32";

        return wire.ElementType switch
        {
            ValueType.Int32 => "next_same_i32",
            ValueType.String => "next_same_string",
            _ => "",
        };
    }

    /// <summary>
    /// The line assigning one row from the value the run decoded, inside the loop the
    /// template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = PythonName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + PythonName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references/references-in-records.md.
        if (wire.IsRef)
        {
            // A dotted reference resolves to a value rather than a row, so there the column's
            // own name belongs to the value and the key takes the `_index` one.
            // spec/references/reference-surface-naming.md section 9.
            string keySuffix = ResolvesToRow(wire.TagCarrier) ? "" : "_index";

            return (wire.Member is null)
                ? $"records[i].{name}{keySuffix} = value"
                : $"records[i].{name}{memberAccess}{keySuffix} = value";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"records[i].{name}{memberAccess} = {wire.TagCarrier.Enum.Name.ToPascalCase()}(value)";

            return $"records[i].{name}{memberAccess} = value";
    }

    /// <summary>
    /// The read for a scalar that goes through the cursor - which is what carries the
    /// encodings, and the lossless promotions with them.
    /// </summary>
    private static string CursorReadExpression(WireColumn wire)
    {
        // Only the stored index is on the wire; the accessor fills the value in once
        // every table is loaded.
        // The key the target is addressed by, which is not always an int32. `next_i32` for
        // every reference is what kept a table keyed by anything else from being pointed at
        // from this language. spec/references/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "cursor.next_i64()",
                ValueType.String => "cursor.next_string()",
                _ => "cursor.next_i32()",
            };
        }

        switch (wire.ElementType)
        {
            // An enum travels as an int32 through the cursor, exactly as a raw one does.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}(cursor.next_i32())";

            case ValueType.Int32: return "cursor.next_i32()";
            case ValueType.Int64: return "cursor.next_i64()";
            case ValueType.Double: return "cursor.next_f64()";
            case ValueType.Float: return "cursor.next_f32()";
            case ValueType.Bool: return "cursor.next_bool()";

            // Ticks, which is what the member holds here - so the i64 column's value
            // is the member, exactly as read_datetime_ticks gives it raw.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "cursor.next_i64()";

            default: // String; UsesCursor admits nothing else here.
                return "cursor.next_string()";
        }
    }

    /// <summary>
    /// The constructor's assignments, so that a record is fully formed before it is read
    /// into and a consumer never meets a half-built one.
    /// </summary>
    private IReadOnlyList<string> Initializers(SerialField sf, string name)
    {
        if (sf.IsRef)
        {
            bool toRow = ResolvesToRow(sf.FirstField!);
            string rowName = toRow
                ? PythonName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                : name;
            string keyName = toRow ? name : name + "_index";

            return sf.IsArray
                ? new[] { $"self.{keyName} = []", $"self.{rowName} = []" }
                : new[] { $"self.{keyName} = 0", $"self.{rowName} = None" };
        }

        if (sf.IsArray)
            return new[] { $"self.{name} = []" };

        return new[] { $"self.{name} = {DefaultValue(sf)}" };
    }

    private string DefaultValue(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "\"\"";
            case ValueType.Bool: return "False";
            case ValueType.Float:
            case ValueType.Double: return "0.0";
            case ValueType.Uuid: return "tabbit.Uuid()";
            case ValueType.Enum: return $"{sf.FirstField!.Enum.Name.ToPascalCase()}(0)";
            default: return "0";
        }
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "tabbit.KIND_ARRAY" : "tabbit.KIND_SCALAR";


        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `tabbit.ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/references/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "tabbit.ELEMENT_STRING,",
                ValueType.Int64 => "tabbit.ELEMENT_I64, tabbit.ELEMENT_I32, tabbit.ELEMENT_VARINT,",
                ValueType.Uuid => "tabbit.ELEMENT_UUID,",
                _ => "tabbit.ELEMENT_I32,",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "tabbit.ELEMENT_I32, tabbit.ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "tabbit.ELEMENT_I64, tabbit.ELEMENT_I32, tabbit.ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "tabbit.ELEMENT_F64, tabbit.ELEMENT_F32, tabbit.ELEMENT_I32"; break;
                case ValueType.Float: accepted = "tabbit.ELEMENT_F32,"; break;
                case ValueType.Bool: accepted = "tabbit.ELEMENT_BOOL,"; break;
                case ValueType.String: accepted = "tabbit.ELEMENT_STRING,"; break;
                case ValueType.Uuid: accepted = "tabbit.ELEMENT_UUID,"; break;
                case ValueType.Enum: accepted = "tabbit.ELEMENT_VARINT,"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "tabbit.ELEMENT_I64,"; break;

                default:
                    throw new TabbitDefectException($"The python generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "True" : "False";

        // And the other bitmap, by the same argument as the row one.
        string elements = wire.HasOptionalElements ? ", True" : "";

        return $"tabbit.check_column(column, \"{tableName}.{wire.Name}\", {kind}, "
            + $"{nullable}, ({accepted}){elements})";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member walks the elements without building them - the list was made
    /// with the record - while a trimmed one reads its length from the row, and there the
    /// first member does build because no constructor could have known how long this row's
    /// is.
    /// </remarks>
    private static string ReadKind(WireColumn wire)
    {
        if (wire.Member is not null)
        {
            if (!wire.IsArray)
                return "scalar";

            // Which of the two owns the list decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.MemberOwnsTheArray ? "record_member_var" : "record_var";
        }

        if (wire.IsArray)
            // A trimmed array of references: the length is the row's, and the key still goes
            // in the array beside the values. Read as a plain `var_array` it put the keys where
            // the resolved rows belong, and the linking pass then found nothing to resolve -
            // silently, because this language does not type them apart. Nothing held the shape:
            // `foreign[]` is refused, so it is only reachable through a folded group with
            // trimming on. spec/types/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private PythonAccessorView BuildAccessor() => new PythonAccessorView
    {
        FileExtension = _recipe.BinaryTableFileExtension,
        SlotNames = Tuple(_model.Tables.Select(table => PythonSnakeName(table.Name)).ToList()),

        Grids = _model.Tables
                      .Select(table => MatrixPlans.Of(table, _model))
                      .Where(plan => plan is not null)
                      .Select(plan => new PythonGridLinkView
                      {
                          Values = PythonSnakeName(plan!.Values.Name),
                          Columns = PythonSnakeName(plan.Columns.Name),
                      })
                      .ToList(),

        Tables = _model.Tables.Select(table => new PythonTableSlotView
        {
            Name = PythonSnakeName(table.Name),
            TableName = table.Name.ToPascalCase() + "Table",

            // Unescaped: this one names the file the exporter wrote.
            DataFileName = table.DataFileName,
        }).ToList(),

        CrossReferences = _model.Tables
            .Select(table => new
            {
                Table = table,
                Fields = table.SerialFields.Where(sf => sf.IsRef).ToList(),

                // A reference that is a member of a record resolves inside the element rather
                // than beside it, so it is a loop of its own. Read off the wire columns, which
                // is the same list the read path walks - the two have to agree about where the
                // key landed. spec/references/references-in-records.md.
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),


            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0
                         )
            .Select(x => new PythonCrossReferenceView
            {
                Table = PythonName(x.Table.Name),
                RecordFields = x.RecordFields,
                Fields = x.Fields.Select(sf => new PythonReferenceFieldView
                {
                    Name = ResolvesToRow(sf.FirstField!)
                        ? PythonName(sf.Name)
                        : PythonName(sf.Name) + "_index",

                    RowName = ResolvesToRow(sf.FirstField!)
                        ? PythonName(RowAccessorName(sf.FirstField!.ResolvedRefTable!.Name, sf.Name))
                        : PythonName(sf.Name),

                    RefTable = PythonName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = sf.ElementType == ValueType.ForeignRecord
                        ? "target"
                        : "target." + PythonName(sf.FirstField!.ResolvedRefField!.Name),
                    IsArray = sf.IsArray,
                }).ToList(),
            })
            .ToList(),
    };

    // ----------------------------------------------------------- rendering

    private string ReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {

            // Enum values travel zig-zag encoded rather than fixed width.
            case ValueType.Enum:
                return $"{wire.TagCarrier.Enum.Name.ToPascalCase()}(reader.read_enum())";

                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/references/reference-key-types.md.
                case ValueType.ForeignRecord:
                    return LanguageProfile.Python.ReadCall(wire.RefKeyType);

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            default: return LanguageProfile.Python.ReadCall(wire.ElementType);
        }
    }

    /// <summary>
    /// The literal a constant is written as.
    /// </summary>
    /// <remarks>
    /// **An array constant is its elements in this language's list literal.** The element
    /// spelling is the scalar one, so this wraps rather than repeats it - and the wrapping is
    /// where the languages differ far more than the elements do.
    ///
    /// A constant never reaches the file, so there is no wire question here: what a language
    /// needs is an expression its compiler accepts in the place a constant is declared.
    /// spec/layout/primary-layout.md section 8.5.
    /// </remarks>
    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        if (!ValueTypes.IsArray(constant.Type))
            return RenderConstantScalar(constant, constant.Type, constant.Value);

        string joined = string.Join(", ",
            ((System.Array)constant.Value!).Cast<object?>()
                .Select(value => RenderConstantScalar(
                    constant, ValueTypes.ElementOf(constant.Type), value)));

        return "[" + joined + "]";
    }

    /// <summary>One element, or a constant that is one value.</summary>
    private string RenderConstantScalar(
        ConstantSet.Constant constant, ValueType type, object? value)
    {
        switch (type)
        {
            case ValueType.String:
                return Quote((string)value!);

            case ValueType.Bool:
                return (bool)value! ? "True" : "False";

            case ValueType.Int32:
                return ((int)value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Float:
                return ((float)value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.Double:
                return ((double)value!).ToString("R", CultureInfo.InvariantCulture);

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "tabbit.Uuid(bytes([" + string.Join(", ",
                    ((Guid)value!).ToByteArray()
                        .Select(b => b.ToString(CultureInfo.InvariantCulture))) + "]))";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}.{PythonSnakeName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", type),
                            ("Generator", "python")));
        }
    }

    private static string Quote(string value)
    {
        var literal = new StringBuilder("\"");

        foreach (var c in value ?? "")
        {
            if (c == '"')
                literal.Append("\\\"");
            else if (c == '\\')
                literal.Append(@"\\");
            else if (c == '\n')
                literal.Append(@"\n");
            else if (c == '\r')
                literal.Append(@"\r");
            else if (c == '\t')
                literal.Append(@"\t");
            else if (c < 0x20)
                literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                literal.Append(c);
        }

        return literal.Append('"').ToString();
    }

    /// <summary>
    /// A Python tuple literal, with the trailing comma a one-element tuple needs.
    /// </summary>
    private static string Tuple(IReadOnlyList<string> items, bool quote = true)
    {
        if (items.Count == 0)
            return "";

        string rendered = string.Join(", ", items.Select(item => quote ? $"\"{item}\"" : item));

        return items.Count == 1 ? rendered + "," : rendered;
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// An attribute name.
    ///
    /// snake_case, and escaped when it lands on a keyword - which it can, because
    /// Python members are lowercase and so is nearly every Python keyword.
    /// </summary>
    private string PythonName(string name) => LanguageProfile.Python.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for the names that are not members.
    /// </summary>
    /// <remarks>
    /// An enum label and an accessor's per-table slot are snake_case here because that is
    /// how Python writes an identifier, not because a member is. They read the same
    /// function today and would follow a member's spelling anywhere - which is a
    /// coincidence of shared code rather than an intention, so it is spelled out.
    /// </remarks>
    private static string PythonSnakeName(string name) => LanguageProfile.Python.MemberName(name.ToSnakeCase());

    // `new`, and not the base one: each line goes through this target's own doc
    // escaping on the way out.
    private static new IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Array.Empty<string>();

        return comment.Replace("\r\n", "\n").Split('\n').Select(DocText).ToArray();
    }

    /// <summary>
    /// Text safe to put inside a docstring.
    ///
    /// A sheet location contains a backslash on Windows, and a docstring is not raw, so
    /// the path reads as an escape sequence - which Python warns about today and will
    /// reject eventually. A triple quote inside one would end it.
    /// </summary>
    private static string DocText(string text)
        => (text ?? "").Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\\\"\\\"");
}
