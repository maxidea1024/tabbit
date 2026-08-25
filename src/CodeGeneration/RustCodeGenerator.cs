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
/// Settings for the Rust target.
/// </summary>
public sealed class RustRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Crate name the generated Cargo.toml declares. Also how a consumer refers to the
    /// generated types.
    /// </summary>
    public string CrateName { get; set; } = "gamedata";

    /// <summary>
    /// Name of the generated accessor struct.
    /// </summary>
    /// <remarks>
    /// The module it lives in is named from it too, lower_snake_case - so the default gives
    /// `tables::Tables`, which is where this target already put it before the name was
    /// something a recipe could set.
    /// </remarks>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Whether to write a Cargo.toml beside the generated source.
    ///
    /// On by default, so the output builds as it stands. Turn it off when vendoring the
    /// module into a crate that already has one.
    /// </summary>
    public bool WriteCargoToml { get; set; } = true;

    /// <summary>Rust edition the generated Cargo.toml declares.</summary>
    public string Edition { get; set; } = "2021";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    /// </summary>
    /// <remarks>
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed.
    ///
    /// Off by default, and here that means more than elsewhere: Rust's standard library
    /// has no HTTP client, so this is the one thing that puts a dependency in the
    /// generated Cargo.toml. Exactly one - `ureq` - because the manifest parser and the
    /// digest are written out in the module rather than pulled in. Leave it off and the
    /// crate builds with no registry access at all.
    /// </remarks>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// The `ureq` requirement the generated Cargo.toml declares, when the updater is on.
    /// </summary>
    /// <remarks>
    /// A recipe setting rather than a constant, because the crate that has to build is
    /// the consumer's and its lockfile is theirs to pin.
    /// </remarks>
    public string UreqVersion { get; set; } = "2";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

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
/// Emits a Rust crate: a module per table, per enum and per constant set, the accessor, the
/// binary reader, and a lib.rs declaring the tree and re-exporting every type at the path it
/// had before the output was split.
///
/// References are kept as indices rather than resolved into borrows. A record holding a
/// reference to another record is a graph, and Rust will not let one own its
/// neighbours; the alternatives are lifetimes threaded through every generated type or
/// a reference-counted cell around every row. The index plus a lookup reads better and
/// costs the caller one call, which is the same trade the database exporters make.
///
/// The shape lives in templates/rust-*.sbn, one per kind of file, over the shared header in
/// rust-file-head.sbn. Which siblings a file brings into scope comes from
/// <see cref="TypeDependencies"/>.
/// </summary>
[TabbitTarget("rust", TargetKind.CodeGeneration, Order = 60)]
public class RustCodeGenerator : CodeGenerator<RustRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private RustRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Snake;

    /// <summary>
    /// A record group generates a struct and a `Vec` of it; a member column fills one of its
    /// fields.
    /// </summary>
    /// <remarks>
    /// The seventh of the thirteen, and the same split as the six before it - declaration per
    /// field, reading per wire column.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a struct declared beside the element type, and the read reaches it with
    /// a longer member path. The struct derives `Default`, so there is nothing to fill for it.
    /// spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has_{field}` member beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `Option<T>`, which is the one place this target's answer looks wrong for the
    /// language. The record derives Clone and Default and is read field by field into a row
    /// that already exists, and every generated member is written the same way; making the
    /// optional ones a different shape would mean two read paths and a `match` at every call
    /// site that only ever wants the value. spec/optional-fields.md has the reasoning, which
    /// is the same one the other targets follow.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`find_by_stage_and_slot(stage_key, slot_key)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// `has_x_at(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, RustRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Snake, "rust");

        Generate();
        WriteBinaryReaderRuntime();

        if (_recipe.WriteCargoToml)
            WriteCargoToml();
    }

    /// <summary>Module holding the accessor, and so the file it is written to.</summary>
    /// <remarks>
    /// Named from the accessor rather than fixed, so a recipe that renames the struct renames
    /// the module with it - the two are one thing to a consumer, who writes `tables::Tables`.
    ///
    /// A constant set with the same name would want this file too. That is caught rather than
    /// silently resolved: <see cref="StagingFiles.WriteAllTextToFile"/> refuses to write two
    /// different files to one path.
    /// </remarks>
    private string AccessorModule => _recipe.AccessorName.ToSnakeCase();

    /// <summary>The accessor struct's name, in the casing a Rust type uses.</summary>
    private string AccessorType => _recipe.AccessorName.ToPascalCase();

    private void Generate()
    {
        var view = BuildView();

        Log.Information($"Generating codes for Rust into `{System.IO.Path.GetFullPath(SourceDir)}`");

        // The accessor joins paths and delegates; the errors it returns come from the tables
        // already wrapped.
        Write(AccessorModule, "rust-accessor.sbn", new RustPartView
        {
            AccessorName = AccessorType,
            Uses = Uses(
                new[]
                {
                    "std::path::Path",
                    "std::sync::atomic::AtomicBool",
                    "std::sync::OnceLock",
                },
                reader: true)
                .Concat(_model.Tables.Select(TableUse)).ToList(),
            Accessor = view.Accessor,
        });

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table indexes its rows and opens its own file, and names the enums its
            // fields are typed with. Not the tables it references: a reference is kept as an
            // index, so a record never names another record's type.
            Write(TableModule(pair.model), "rust-table.sbn", new RustPartView
            {
                AccessorName = AccessorType,
                // And the abstract types this table's groups are. Declared in modules of their
                // own - one per declaration - so the table brings the enum in rather than
                // declaring its own. spec/polymorphism.md section 7.1.
                Uses = Uses(new[] { "std::collections::HashMap", "std::path::Path" }, reader: true)
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumUse))
                    .Concat(PolymorphicUses(pair.model))
                    .ToList(),
                Table = pair.rendered,
            });
        }

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            // An enum is a leaf: it names nothing but the integers it is built from.
            Write(EnumModule(pair.model), "rust-enum.sbn", new RustPartView
            {
                AccessorName = AccessorType,
                Uses = Array.Empty<string>(),
                Enumm = pair.rendered,
            });
        }

        // A struct is an entity beside a table and an enum, so it gets a module of its own -
        // one per declaration however many tables named it. spec/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            var structure = BuildStruct(declared);

            // The types its members name: a declared enum, and the row of a table a reference
            // member points at. Both are re-exported from the crate root, so the path is the
            // same one a table module writes.
            Write(structure.ModuleName, "rust-struct.sbn", new RustPartView
            {
                AccessorName = AccessorType,
                Uses = StructUses(declared).ToList(),
                Structure = structure,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names no standard library type, reaches the reader only for a
            // uuid - whose value is rendered as a `tabbit::Uuid` literal - and names an
            // enum when one of its constants is typed with one.
            Write(pair.rendered.ModuleName, "rust-constants.sbn", new RustPartView
            {
                AccessorName = AccessorType,
                Uses = Uses(Array.Empty<string>(), reader: NamesUuid(pair.model))
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumUse)).ToList(),
                ModuleDoc = pair.rendered.Comment,
                Set = pair.rendered,
            });
        }

        WriteLib(view);
    }

    /// <summary>
    /// Where the crate's source goes: src/, because the reader sits beside the generated
    /// files and `mod tabbit;` only resolves if it does.
    /// </summary>
    private string SourceDir => System.IO.Path.Combine(_recipe.Path, "src");

    /// <summary>
    /// Flat inside src/ rather than in submodule directories.
    ///
    /// A Rust module can be a directory, but only with a mod.rs or a same-named file beside
    /// it, and every path a consumer writes would gain a level for nothing. The names carry
    /// the grouping instead, as they do for Go and Python.
    /// </summary>
    private void Write(string module, string templateName, RustPartView view)
    {
        string full = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(SourceDir, module + ".rs"));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view));
    }

    /// <summary>The `use` lines one abstract type's module needs for the types its members name.</summary>
    private IEnumerable<string> StructUses(Models.PolymorphicType declared)
    {
        var lines = new List<string>();

        foreach (var field in declared.BaseMembers
                     .Concat(declared.Variants.SelectMany(variant => variant.Members)))
        {
            string line = field.ElementType switch
            {
                ValueType.Enum => EnumUse(field.Enum),
                ValueType.ForeignRecord when field.ResolvedRefTable is not null =>
                    $"use crate::{field.ResolvedRefTable.Name.ToPascalCase()}Record;",
                _ => "",
            };

            if (line.Length > 0 && !lines.Contains(line))
                lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// The `use` lines a table needs for the abstract types its groups are.
    /// </summary>
    /// <remarks>
    /// Both the enum and every variant, because the built value names all of them.
    /// spec/polymorphism.md section 7.1.
    /// </remarks>
    private IEnumerable<string> PolymorphicUses(Table table)
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

            yield return $"use crate::{module}::{name};";

            foreach (var variant in declared.Variants)
                yield return $"use crate::{module}::{variant.Name};";
        }
    }

    /// <summary>
    /// Writes lib.rs: the crate lints, the module tree, and the re-exports.
    /// </summary>
    /// <remarks>
    /// The re-exports are why this is worth doing rather than declaring the modules and
    /// leaving it there. Before the split every generated type was declared in lib.rs, so a
    /// consumer wrote `gamedata::VectorsRecord`. `pub use` keeps that path exactly, and the
    /// module a type lives in becomes an implementation detail nobody has to follow.
    ///
    /// A constant set is the exception: it was already a module of its own, so it stays one
    /// and its path is unchanged without any re-export.
    /// </remarks>
    private void WriteLib(RustFileView view)
    {
        var text = new StringBuilder();

        text.Append("// ------------------------------------------------------------------------------\n");
        text.Append($"// {GeneratedFileMarker.TextWithWarning}\n");
        text.Append("//\n");
        text.Append("// Changes to this file may cause incorrect behavior and will be lost if the code is\n");
        text.Append("// regenerated.\n");
        text.Append("// ------------------------------------------------------------------------------\n");
        text.Append('\n');

        // Crate scope, so no generated file repeats them. Generated code is allowed to
        // declare more than a given consumer uses, and clippy's opinions are not this
        // tool's to answer for.
        text.Append("#![allow(dead_code)]\n");
        text.Append("#![allow(clippy::all)]\n");
        text.Append('\n');

        // One declaration for the whole runtime. The updater is a child of it
        // rather than a sibling, so `tabbit` is the one name a consumer has to
        // know for anything that is not their own data - the same shape the other
        // targets get from a `tabbit/` directory.
        text.Append("pub mod tabbit;\n");

        Section(text, "The enums.", view.Enums.Count > 0);

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            text.Append("mod ").Append(EnumModule(pair.model)).Append(";\n");
            text.Append("pub use ").Append(EnumModule(pair.model))
                .Append("::").Append(pair.rendered.Name).Append(";\n");
        }

        Section(text, "The declared abstract types, one module each.",
                _model.PolymorphicTypes.Count > 0);

        // Re-exported the way an enum is, so a consumer writes `gamedata::Effect` and the
        // module the type lives in stays an implementation detail.
        // spec/polymorphism.md section 7.1.
        foreach (var declared in _model.PolymorphicTypes)
        {
            string module = "struct_" + declared.Name.ToSnakeCase();

            text.Append("mod ").Append(module).Append(";\n");
            text.Append("pub use ").Append(module).Append("::").Append(declared.Name)
                .Append(";\n");

            foreach (var variant in declared.Variants)
            {
                text.Append("pub use ").Append(module).Append("::").Append(variant.Name)
                    .Append(";\n");
            }
        }

        Section(text, "The constant sets, each keeping the module path it always had.",
                view.ConstantSets.Count > 0);

        foreach (var set in view.ConstantSets)
            text.Append("pub mod ").Append(set.ModuleName).Append(";\n");

        Section(text, "A record and a table type per table.", view.Tables.Count > 0);

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            text.Append("mod ").Append(TableModule(pair.model)).Append(";\n");
            text.Append("pub use ").Append(TableModule(pair.model))
                .Append("::{").Append(pair.rendered.RecordName)
                .Append(", ").Append(pair.rendered.TableName);

            // A record group's element type as well, so a caller can name what
            // `record.slot[0]` is without following the module it was declared in. An array
            // of arrays declares none - its outer level has no name - so there is nothing to
            // re-export and naming one here would be an import of a type that does not exist.
            foreach (var field in pair.rendered.Fields.Where(f => f.IsRecord && !f.MembersAreAnonymous))
                text.Append(", ").Append(field.RecordTypeName);

            text.Append("};\n");
        }

        Section(text, "The accessor.", true);

        text.Append("mod ").Append(AccessorModule).Append(";\n");

        // The keys and the MAC switch come out at the crate root beside `Tables` because that
        // is where a consuming project sets them, and because the module they live in is
        // meant to stay an implementation detail like every other one here.
        text.Append("pub use ").Append(AccessorModule)
            .Append("::{Tables, ENCRYPTION_KEY, MAC_KEY, VERIFY_MAC};\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(SourceDir, "lib.rs")),
            text.ToString());
    }

    private static void Section(StringBuilder text, string heading, bool any)
    {
        if (!any)
            return;

        text.Append('\n');
        text.Append("// ").Append(heading).Append('\n');
    }

    // ------------------------------------------------------- module layout

    private static string TableModule(Table table) => table.Name.ToSnakeCase() + "_table";
    private static string EnumModule(Models.Enum enumm) => "enum_" + enumm.Name.ToSnakeCase();

    private static string TableUse(Table table)
        => $"use crate::{TableModule(table)}::{table.Name.ToPascalCase()}Table;";

    private static string EnumUse(Models.Enum enumm)
        => $"use crate::{EnumModule(enumm)}::{RustPascalName(enumm.Name)};";

    /// <summary>
    /// The standard library and reader uses a file needs, in the order rustfmt groups them:
    /// std first, then the crate's own.
    /// </summary>
    private static IEnumerable<string> Uses(IReadOnlyList<string> standard, bool reader)
    {
        foreach (var path in standard)
            yield return $"use {path};";

        if (reader)
            yield return "use crate::tabbit;";
    }

    /// <summary>
    /// Whether a constant set has a uuid in it, which is the only way its file reaches the
    /// reader - the value renders as a `tabbit::Uuid` literal.
    /// </summary>
    private static bool NamesUuid(ConstantSet set)
        => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

    private void WriteBinaryReaderRuntime()
    {
        string runtime = System.IO.Path.Combine(SourceDir, "tabbit");

        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Rust.tcb_reader.rs",
            System.IO.Path.Combine(runtime, "tcb_reader.rs"));

        // Asked for rather than assumed. It reaches the network, and it is the only
        // thing in this output that wants a crate.
        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Rust.updater.rs",
                System.IO.Path.Combine(runtime, "updater.rs"));
        }

        // The module file for that directory. Two lines, so that `use crate::tabbit`
        // keeps naming the reader's own symbols - which is what every generated file
        // reaches for - while the updater sits under it as `tabbit::updater`.
        var module = new StringBuilder();

        module.Append("// ------------------------------------------------------------------------------\n");
        module.Append($"// {GeneratedFileMarker.TextWithWarning}\n");
        module.Append("// ------------------------------------------------------------------------------\n");
        module.Append('\n');
        module.Append("mod tcb_reader;\n");
        module.Append("pub use tcb_reader::*;\n");

        if (_recipe.WriteUpdater)
            module.Append("\npub mod updater;\n");

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(runtime, "mod.rs")),
            module.ToString());
    }

    private void WriteCargoToml()
    {
        var text = new StringBuilder();
        text.Append("[package]\n");
        text.Append("name = \"").Append(_recipe.CrateName).Append("\"\n");
        text.Append("version = \"0.0.0\"\n");
        text.Append("edition = \"").Append(_recipe.Edition).Append("\"\n");
        text.Append('\n');
        if (_recipe.WriteUpdater)
        {
            text.Append("# One dependency, and only because `WriteUpdater` is on: Rust's standard\n");
            text.Append("# library has no HTTP client. The manifest parser and the MD5 are written\n");
            text.Append("# out in src/updater.rs rather than pulled in, so this is the whole of it.\n");
            text.Append("# Turn the updater off and this section is empty again.\n");
            text.Append("[dependencies]\n");
            text.Append("ureq = \"").Append(_recipe.UreqVersion).Append("\"\n");
        }
        else
        {
            text.Append("# No dependencies on purpose: the reader is core and std only, so the\n");
            text.Append("# generated crate builds without registry access.\n");
            text.Append("[dependencies]\n");
        }

        StagingFiles.WriteAllTextToFile(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, "Cargo.toml")),
            text.ToString());
    }

    // --------------------------------------------------------------- view

    private RustFileView BuildView() => new RustFileView
    {
        AccessorName = AccessorType,
        Enums = _model.Enums.Select(BuildEnum).ToList(),
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = new RustAccessorView
        {
            FileExtension = _recipe.BinaryTableFileExtension,
            Tables = _model.Tables.Select(table => new RustTableSlotView
            {
                Name = RustSnakeName(table.Name),
                TableName = table.Name.ToPascalCase() + "Table",

                // Unescaped: this one names the file the exporter wrote.
                DataFileName = table.DataFileName,
            }).ToList(),
        },
    };

    private RustEnumView BuildEnum(Models.Enum enumm)
    {
        // Deriving Default needs exactly one variant marked, so the zero label gets it
        // when there is one and the first otherwise.
        int defaultIndex = enumm.Labels.FindIndex(label => label.Value == 0);
        if (defaultIndex < 0)
            defaultIndex = 0;

        return new RustEnumView
        {
            Name = RustPascalName(enumm.Name),
            Location = enumm.Location.ToString(),
            Comment = CommentLines(enumm.Comment),
            Labels = enumm.Labels.Select((label, index) => new RustEnumLabelView
            {
                Name = RustPascalName(label.Name),
                Value = label.Value.ToString(CultureInfo.InvariantCulture),
                Comment = CommentLines(label.Comment),
                IsDefault = index == defaultIndex,
            }).ToList(),
        };
    }

    private RustConstantSetView BuildConstantSet(ConstantSet constantSet) => new RustConstantSetView
    {
        ModuleName = constantSet.Name.ToSnakeCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new RustConstantView
        {
            // Rust constants are SCREAMING_SNAKE_CASE, and the compiler warns otherwise.
            Name = constant.Name.ToUpperSnakeCase(),
            Type = ConstantTypeName(constant),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private RustTableView BuildTable(Table table) => new RustTableView
    {
        RawName = table.Name,
        RecordName = table.Name.ToPascalCase() + "Record",
        TableName = table.Name.ToPascalCase() + "Table",
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),

        // A separate list, because declaring a member is per field and reading is per
        // column - and a record group is one column per member of it.
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),

        NeedsRecordInit = table.SerialFields.Any(
            sf => sf.IsRecord
                  && (sf.MembersAreArrays || (sf.IsArray && !table.TrimTrailingArrayElements))),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<RustIndexView> Indexes(Table table)
        => KeyPlans.Of(table).Select(plan =>
        {
            string keyType = ToRustTypeName(
                plan.Only.FirstField!.ElementType, plan.Only.FirstField!.EnumOrNull);

            bool owned = keyType == "String";
            string suffix = plan.Suffix(name => name.ToSnakeCase(), "_and_");

            var components = plan.Components.Select(component => new KeyComponentView
            {
                Param = KeyComponentView.ParamOf(component.Name).ToSnakeCase(),
                Type = RustKeyParam(component),
                Member = RustName(component.Name),
                Kind = KeyComponentView.KindOf(component.FirstField!.ElementType),
            }).ToList();

            string args = string.Join(", ", components.Select(component => component.Param));

            return new RustIndexView
            {
                Member = RustName(plan.Only.Name),
                Suffix = suffix,
                KeyType = plan.IsComposite ? "String" : keyType,
                KeyParam = plan.IsComposite ? "&str" : owned ? "&str" : keyType,
                KeyBorrow = plan.IsComposite ? "key" : owned ? "key" : "&key",
                MapName = "by_" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                IsComposite = plan.IsComposite,
                Components = components,

                Params = plan.IsComposite
                    ? string.Join(", ", components.Select(c => c.Param + ": " + c.Type))
                    : "key: " + (owned ? "&str" : keyType),

                Argument = plan.IsComposite
                    ? "&Self::key_of_" + suffix + "(" + args + ")"
                    : owned ? "key" : "&key",

                ValueFormat = plan.IsComposite
                    ? "(" + string.Join(", ", components.Select(_ => "{:?}")) + ")"
                    : "{:?}",

                ValueArgs = plan.IsComposite ? args : "key",
            };
        }).ToList();

    /// <summary>How a key column arrives at a lookup: borrowed where owning it would copy.</summary>
    private string RustKeyParam(SerialField component)
    {
        string type = ToRustTypeName(
            component.FirstField!.ElementType, component.FirstField!.EnumOrNull);

        return type == "String" ? "&str" : type;
    }

    private RustFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string name = RustName(sf.Name);
        string elementType = ToRustTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull);

        return new RustFieldView
        {
            Comment = CommentLines(sf.FirstField!.Comment),
            Name = name,
            Declarations = Declarations(sf, name, elementType),
            IsRecord = false,
            RecordTypeName = "",
            Members = Array.Empty<RustRecordMemberView>(),
            IsFixedRecordArray = false,
            ElementCount = 0,
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = PresenceMember(sf),
            ElementPresenceMember = PresenceMember(sf) + "_at",
        };
    }

    /// <summary>
    /// A record group: the struct to declare for one element, and the member holding one or
    /// a vector of them.
    /// </summary>
    /// <remarks>
    /// A `Vec` rather than an array even where the length is fixed, so the two record shapes
    /// declare the same thing and only the read differs - and so the record keeps deriving
    /// Default, which an array longer than 32 does not.
    ///
    /// No reference members: a reference belongs to a member and the model refuses one there,
    /// so nothing here has the index a reference would be carried as.
    /// </remarks>
    /// <summary>
    /// Members of one level of a record, declaring a struct for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the structs it produces. A nested member needs nothing beyond its declaration
    /// line: the struct derives `Default`, so there is nothing to fill for it.
    /// spec/nested-multi-level.md.
    /// </remarks>
    private List<RustRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<RustRecordTypeView> declared)
    {
        var result = new List<RustRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                // A reference member is the key and nothing else, exactly as a reference
                // outside a record is: this output keeps references as indices rather than
                // resolving them, for the reason on the type. So the only thing a member being
                // a reference changes is its name and the type of its key - and the name has
                // to say what it holds, because the row is not there beside it.
                // spec/references-in-records.md.
                string memberType = member.IsRef
                    ? ToRustTypeName(member.FirstField!.RefKeyType, null)
                    : ToRustTypeName(member.FirstField!.ElementType, member.FirstField!.EnumOrNull);
                // No linking in this language, so a reference column carries the key and
                // nothing else - and the key wears the column's own name.
                // spec/reference-surface-naming.md sections 4 and 6.
                string memberName = RustName(member.Name);

                result.Add(new RustRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The vector is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs.
                    Declaration = $"{memberName}: "
                                + (member.IsArray ? $"Vec<{memberType}>" : memberType)
                                + ",",
                    Name = memberName,
                    ElementType = member.IsArray ? memberType : "",
                    ElementCount = member.IsArray ? member.Fields.Count : 0,
                });

                continue;
            }

            // A level below. The type name carries the path so two records each holding a
            // `Position` do not name one struct twice.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new RustRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record::{RustName(group.Name)}",
            });

            result.Add(new RustRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declaration = $"{RustName(member.Name)}: {typeName},",
                Name = RustName(member.Name),
                ElementType = "",
                ElementCount = 0,
            });
        }

        return result;
    }

    /// <summary>
    /// One abstract type and its variants, as the template reads them.
    /// </summary>
    /// <remarks>
    /// An `enum`, which is this language's sum type and the shape the spec named for it
    /// outright: a `match` over it that misses a variant does not compile.
    /// spec/polymorphism.md section 7.
    /// </remarks>
    private RustPolymorphicTypeView BuildStruct(Models.PolymorphicType declared)
        => new RustPolymorphicTypeView
        {
            Name = declared.Name,
            ModuleName = "struct_" + declared.Name.ToSnakeCase(),
            BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
            Variants = declared.Variants
                .Select(variant => new RustVariantView
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
    /// spec/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private RustStructMemberView StructMember(Models.Field field)
    {
        string raw = field.NamePath is { Count: > 1 } ? field.NamePath[^1].Name : field.Name;

        // **No second name here.** This language does not link, so a reference column is one
        // key wearing the column's own name - the row it would resolve to does not exist to be
        // carried. spec/reference-surface-naming.md, "링킹이 없는 언어".
        const bool toRow = false;

        return new RustStructMemberView
        {            Name = RustName(raw),
            TypeName = toRow
                ? "Option<" + field.ResolvedRefTable!.Name.ToPascalCase() + "Record>"
                : ToRustTypeName(field.Type, field.EnumOrNull),
            Comment = CommentLines(field.Comment),
            RowName = toRow
                ? RustName(RowAccessorName(field.ResolvedRefTable!.Name, raw))
                : "",
            KeyTypeName = field.IsRef
                ? ToRustTypeName(field.RefKeyType, null)
                : "",
        };
    }

    private RustFieldView BuildRecordField(Table table, SerialField sf)
    {
        // Which abstract type this group is, if it is one. One per declaration however many
        // tables named it. spec/polymorphism.md section 7.1.
        var declaredType = sf.Members
                .FirstOrDefault(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                ?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        string name = RustName(sf.Name);
        string elementType = RecordTypeName(table, sf);

        // Innermost first, so a struct is declared before the one naming it.
        var recordTypes = new List<RustRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, elementType, table, sf, recordTypes);

        recordTypes.Add(new RustRecordTypeView
        {
            TypeName = elementType,
            Members = members,
            IsOutermost = true,
            Owner = $"{table.Name.ToPascalCase()}Record::{name}",
        });

        return new RustFieldView
        {
            AbstractTypeName = declaredType?.Name ?? "",
            DiscriminatorName = declaredType is null
                ? ""
                : RustName(sf.Members
                    .First(m => m.IsLeaf && m.FirstField is { IsDiscriminator: true })
                    .Name),
            BaseMembers = (declaredType?.BaseMembers ?? []).Select(StructMember).ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new RustVariantView
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
            Declarations = new[]
            {
                // An array of arrays has no element type to name, so the inner vector is
                // the type - see spec/nested-multi-level.md.
                sf.MembersAreAnonymous
                    ? $"{name}: Vec<Vec<{ToRustTypeName(sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull)}>>,"
                    : sf.IsArray ? $"{name}: Vec<{elementType}>," : $"{name}: {elementType},",
            },
            IsRecord = true,
            RecordTypeName = elementType,

            Members = members,
            RecordTypes = recordTypes,

            IsFixedRecordArray = sf.IsArray && !table.TrimTrailingArrayElements,
            MembersAreArrays = sf.MembersAreArrays,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            OuterCount = sf.Members.Count,
            ElementTypeName = sf.MembersAreAnonymous
                ? ToRustTypeName(sf.Members[0].FirstField!.ElementType, sf.Members[0].FirstField!.EnumOrNull)
                : "",
            ElementCount = sf.RecordElementCount,

            // A record group has no presence of its own: absence inside one is the vector's
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
    /// columns each fill one field of the generated element type, which is the whole of the
    /// difference - see spec/nested-fields.md.
    /// </remarks>
    private RustColumnView BuildColumn(Table table, WireColumn wire)
    {
        return new RustColumnView
        {
            WireTag = wire.TagCarrier.WireTag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire) ? "cursor.next_length()?" : "reader.read_counter32()?",
            RunCall = RunCall(wire),
            RunSpend = RunSpend(wire),
            Name = RustName(wire.Group.Name),

            // A record's member column assigns one field of the element rather than the
            // member itself: `record.slot[j].id` instead of `record.slot[j]`.
            MemberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + RustName(part))),

            // A reference member is declared as the key it holds, so the read names the key -
            // and the suffix goes on the member rather than after the subscript, because a
            // member that is an array holds one key per element.
            // spec/references-in-records.md.
            MemberRefSuffix = "",
            MemberAt = wire.MemberAt,

            RecordTypeName = wire.Group.IsRecord ? RecordTypeName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            ElementCount = wire.Cells.Count,
            ReadScalar = ScalarReadExpression(wire),

            // The same expression: an array's elements read through the cursor by the calls
            // a scalar's row does, one level down.
            ReadElement = ScalarReadExpression(wire),
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = PresenceMember(wire.Group),
            ElementPresenceMember = PresenceMember(wire.Group) + "_at",
        };
    }

    /// <summary>
    /// The element type of a record group, which carries the table's name.
    /// </summary>
    /// <remarks>
    /// The generated modules are re-exported side by side from lib.rs, so two tables each
    /// holding a `Slot` group would be the same path exported twice.
    /// </remarks>
    private static string RecordTypeName(Table table, SerialField sf)
        => table.Name.ToPascalCase() + sf.Name.ToPascalCase() + "Entry";

    /// <summary>
    /// The member a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever reads
    /// it, and the model has already required its columns to agree about being optional.
    /// </remarks>
    private string PresenceMember(SerialField sf)
        => sf.IsRecord ? "" : RustName("has_" + sf.Name);

    private IReadOnlyList<string> Declarations(SerialField sf, string name, string elementType)
    {
        if (sf.IsRef)
        {
            // Only the index. See the type remarks for why it is not resolved.
            return sf.IsArray
                ? new[] { $"{name}: Vec<i32>," }
                : new[] { $"{name}: i32," };
        }

        return sf.IsArray
            ? new[] { $"{name}: Vec<{elementType}>," }
            : new[] { $"{name}: {elementType}," };
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "tabbit::KIND_ARRAY" : "tabbit::KIND_SCALAR";


        string accepted;

        if (wire.IsRef)
            // The key the target is addressed by. `tabbit::ELEMENT_I32` alone is what a reference
            // accepted while a key could only be an int, and the writer has meanwhile learned
            // to emit the key's own element - so a reader told only this would refuse a file
            // this build wrote. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "tabbit::ELEMENT_STRING",
                ValueType.Int64 => "tabbit::ELEMENT_I64, tabbit::ELEMENT_I32, tabbit::ELEMENT_VARINT",
                ValueType.Uuid => "tabbit::ELEMENT_UUID",
                _ => "tabbit::ELEMENT_I32",
            };
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "tabbit::ELEMENT_I32, tabbit::ELEMENT_VARINT"; break;
                case ValueType.Int64:
                    accepted = "tabbit::ELEMENT_I64, tabbit::ELEMENT_I32, tabbit::ELEMENT_VARINT"; break;
                case ValueType.Double:
                    accepted = "tabbit::ELEMENT_F64, tabbit::ELEMENT_F32, tabbit::ELEMENT_I32"; break;
                case ValueType.Float: accepted = "tabbit::ELEMENT_F32"; break;
                case ValueType.Bool: accepted = "tabbit::ELEMENT_BOOL"; break;
                case ValueType.String: accepted = "tabbit::ELEMENT_STRING"; break;
                case ValueType.Uuid: accepted = "tabbit::ELEMENT_UUID"; break;
                case ValueType.Enum: accepted = "tabbit::ELEMENT_VARINT"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "tabbit::ELEMENT_I64"; break;

                default:
                    throw new TabbitDefectException($"The rust generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one. A function of its own
        // because Rust has no default arguments.
        string check = wire.HasOptionalElements ? "check_column_with_elements" : "check_column";

        return $"tabbit::{check}(column, \"{tableName}.{wire.Name}\", {kind}, "
            + $"{nullable}, &[{accepted}])?;";
    }

    /// <summary>
    /// Which read shape a column takes.
    /// </summary>
    /// <remarks>
    /// A record group's member loops the elements without allocating - the vector was created
    /// with the row - while a trimmed one reads its length from the row, and there the first
    /// member does allocate because no declaration could have known how long this row's is.
    /// </remarks>
    private static string ReadKind(WireColumn wire)
    {
        if (wire.Member is not null)
        {
            if (!wire.IsArray)
                return "scalar";

            // Which of the two owns the vector decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_var" : "record_var";
        }

        if (wire.IsArray)
            // A trimmed array of references: the length is the row's, and the key still goes
            // in the vector beside the values. Read as a plain `var_array` it pushed the key
            // into the vector of rows, which does not compile - and nothing held the shape,
            // because `foreign[]` is refused and this is only reachable through a folded group
            // with trimming on. spec/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from. The elements that stay raw by spec keep
    /// reading the reader directly.
    /// </summary>
    private static bool UsesCursor(WireColumn wire)
    {
        // Arrays go through it too. An array block states an encoding for its elements and
        // one for its rows' lengths, and the cursor is what decodes both - so an array's
        // elements are read exactly the way a scalar column's are, one level down.
        //
        // Uuid is the exception, and the same one it has always been: no encoding applies to
        // it, so it has no cursor path to reach.
        if (wire.ElementType == ValueType.Uuid)
            return false;

        if (wire.IsArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional yes
        // was the int32 assumption in another place: a target keyed by `uuid` has no cursor
        // path any more than a `uuid` column does. spec/reference-key-types.md.
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

            // Uuid is the one scalar left raw: sixteen-byte entries rarely repeat
            // enough to pay for the index beside them.
            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly. The match arm is its own scope, so the
    /// binding lives exactly as long as the column it decodes.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? "let mut cursor = tabbit::TcbColumnCursor::new(" +
              $"&mut reader, column, header.row_count, \"{tableName}.{wire.Name}\")?;"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to next_i32 or next_string: int32
    /// members, enums, references and strings. The other cursor scalars stay per-row -
    /// their encodings are dictionaries, where the per-row work is already one index
    /// lookup - as do arrays, whose rows are loops rather than values.
    /// </remarks>
    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row does
        // not have one of. Its elements are read one at a time.
        if (wire.IsArray)
            return "";

        // A reference runs on the key it carries, which is not always an int32. An enum's
        // underlying value is one. spec/reference-key-types.md.
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
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// the template builds around <see cref="RunCall"/>.
    /// </summary>
    /// <remarks>
    /// A string is cloned per row, because every record owns its own - which is what the
    /// per-row shape does too, one dictionary lookup earlier.
    /// </remarks>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = RustName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + RustName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            // Cloned where the key is a String: the run's value is assigned once per row it
            // covers, and a String moves on the first of them.
            string spend = wire.RefKeyType == ValueType.String ? "value.clone()" : "value";

            // No `Index` suffix and no second name: this language does not link, so every
            // reference column is one key wearing the column's own name - dotted or not.
            // spec/reference-surface-naming.md, "링킹이 없는 언어".
            return (wire.Member is null)
                ? $"records[at].{name} = {spend};"
                : $"records[at].{name}{memberAccess} = {spend};";
        }

        if (wire.ElementType == ValueType.Enum)
        {
            return $"records[at].{name}{memberAccess} = " +
                   $"{RustPascalName(wire.TagCarrier.Enum.Name)}::from_value(value).unwrap_or_default();";
        }

        if (wire.ElementType == ValueType.String)
            return $"records[at].{name}{memberAccess} = value.clone();";

        return $"records[at].{name}{memberAccess} = value;";
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// The expression that reads one value: through the cursor where the column can arrive
    /// encoded - which also carries the lossless promotions - and straight off the reader
    /// otherwise. One value is one row for a scalar column and one element for an array,
    /// and the call is the same either way.
    /// </summary>
    private string ScalarReadExpression(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return ReadExpression(wire);

        // The key the target is addressed by, which is not always an int32. `next_i32` for
        // every reference is what kept a table keyed by anything else from being pointed at
        // from this language. spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int64 => "cursor.next_i64()?",
                ValueType.String => "cursor.next_string()?",
                _ => "cursor.next_i32()?",
            };
        }

        switch (wire.ElementType)
        {
            case ValueType.Enum:
                return $"{RustPascalName(wire.TagCarrier.Enum.Name)}::from_value(cursor.next_i32()?)" +
                       ".unwrap_or_default()";

            case ValueType.Int32: return "cursor.next_i32()?";
            case ValueType.Int64: return "cursor.next_i64()?";
            case ValueType.Double: return "cursor.next_f64()?";
            case ValueType.Float: return "cursor.next_f32()?";
            case ValueType.Bool: return "cursor.next_bool()?";

            // Ticks, which is what the member holds - std has no date type - so the
            // i64 column's value is the member, dictionary or not.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "cursor.next_i64()?";

            default: return "cursor.next_string()?";
        }
    }

    private string ReadExpression(WireColumn wire)
    {
        switch (wire.ElementType)
        {

            // Enum values travel zig-zag encoded. A value the sheet never declared
            // falls back to the default rather than failing the whole read, matching
            // what the other generated readers do with an unknown label.
            case ValueType.Enum:
                return $"{RustPascalName(wire.TagCarrier.Enum.Name)}::from_value(reader.read_enum()?)" +
                       ".unwrap_or_default()";

                // The key the target is addressed by, which is not always an int32 -
                // that constant is what kept a table keyed by anything else from
                // being pointed at. spec/reference-key-types.md.
                case ValueType.ForeignRecord:
                    return LanguageProfile.Rust.ReadCall(wire.RefKeyType);

            // Everything else is a plain call named in the profile, which is where the
            // nine of them live now rather than here and in every other generator.
            default: return LanguageProfile.Rust.ReadCall(wire.ElementType);
        }
    }

    private string ToRustTypeName(ValueType type, Models.Enum? enumm)
    {
        switch (ValueTypes.ElementOf(type))
        {
            case ValueType.Enum:
                return RustPascalName(enumm!.Name);

            // A reference is carried as the target row's index.
            case ValueType.ForeignRecord:
                return "i32";

            default:
                return LanguageProfile.Rust.ScalarTypeName(type);
        }
    }

    /// <summary>
    /// The type of a constant, which is not always the type of a field.
    ///
    /// A `String` cannot be a constant - it allocates - so a string constant is a
    /// static string slice instead.
    /// </summary>
    private string ConstantTypeName(ConstantSet.Constant constant)
        => constant.Type == ValueType.String
            ? "&'static str"
            : ToRustTypeName(constant.Type, constant.Enum);

    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        switch (constant.Type)
        {
            case ValueType.String:
                return Quote((string)constant.Value!);

            case ValueType.Bool:
                return (bool)constant.Value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)constant.Value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)constant.Value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Float:
                return Suffixed(((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture));

            case ValueType.Double:
                return Suffixed(((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture));

            // Ticks, matching what the generated fields hold and for the same reason.
            case ValueType.DateTime:
                return ((DateTime)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.TimeSpan:
                return ((TimeSpan)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture);

            case ValueType.Uuid:
                return "tabbit::Uuid([" + string.Join(", ",
                    ((Guid)constant.Value!).ToByteArray()
                        .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + "])";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);
                return $"{RustPascalName(constant.Enum.Name)}::{RustPascalName(label.Name)}";
            }

            default:
                throw new TabbitException(constant.Location,
                        Messages.Message.Of(Exporters.ExportMessages.ConstantTypeNotRendered,
                            ("Name", constant.Name), ("Type", constant.Type),
                            ("Generator", "rust")));
        }
    }

    /// <summary>
    /// Gives a rendered float a decimal point when it has none.
    ///
    /// `3` is an integer literal in Rust and will not initialize an f32; `3.0` will.
    /// A value in exponent form already parses as a float.
    /// </summary>
    private static string Suffixed(string rendered)
        => rendered.Contains('.') || rendered.Contains('E') || rendered.Contains('e')
            ? rendered
            : rendered + ".0";

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
                literal.Append(@"\u{").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append('}');
            else
                literal.Append(c);
        }

        return literal.Append('"').ToString();
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// A struct member name.
    ///
    /// snake_case, and escaped when it lands on a keyword - which it can, unlike Go and
    /// C#, because Rust members are lowercase and so is every Rust keyword.
    /// </summary>
    private string RustName(string name) => LanguageProfile.Rust.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// snake_case because that is how Rust writes an identifier, not because a member is
    /// spelled that way. Sharing one function let the two look like one rule.
    /// </remarks>
    private static string RustSnakeName(string name) => LanguageProfile.Rust.MemberName(name.ToSnakeCase());

    /// <summary>
    /// A PascalCase identifier Rust will accept: an enum's type name, or one of its labels.
    /// </summary>
    /// <remarks>
    /// The same escape a member gets, because the same list holds `Self` - the one Rust
    /// keyword that is PascalCase, and so the one an enum label can collide with. A sheet
    /// with a label called `Self` used to generate `Self = 1`, which stops the crate
    /// compiling; the table struct names cannot reach this because they are suffixed.
    /// </remarks>
    private static string RustPascalName(string name)
        => LanguageProfile.Rust.MemberName(name.ToPascalCase());

}
