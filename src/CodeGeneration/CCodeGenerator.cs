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
/// Settings for the C target.
/// </summary>
public sealed class CRecipe : IOutputRecipe
{
    /// <summary>Directory the header, the source and the reader are written into.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Name of the accessor, which also names the two files and prefixes every
    /// generated identifier.
    ///
    /// C has no namespaces, so this prefix is the whole of the collision avoidance -
    /// which is the one place this target departs from the style it otherwise follows.
    /// Doom and Quake put a subsystem prefix on functions (`P_SpawnMobj`) and none on
    /// types (`mobj_t`), because a game is one program. Generated code is dropped into
    /// somebody else's, so the types carry it too.
    /// </summary>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Extension of the table files the generated reader opens. Must match what the
    /// binary exporter was told to write.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    /// </summary>
    /// <remarks>
    /// It fetches the manifest and the changed data files over HTTP and keeps a local
    /// copy current, so a program can take new data without being redeployed.
    ///
    /// Off by default, and here that means more than elsewhere: C has no HTTP client
    /// and no portable one to fall back on, so this is the only emitted file that links
    /// against anything - libcurl, and nothing else. Leave it off and the generated C
    /// depends on the C standard library alone, which is what it did before.
    /// </remarks>
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
/// Emits a C header per generated type, a source beside the ones that need code, an umbrella
/// header a consumer includes, and the binary reader.
///
/// Two questions C asks that none of the other targets do.
///
/// Who owns the strings. Each table owns one arena; its records hold pointers into it
/// and the whole thing is released in one call. The alternative - a malloc per string
/// and a free the caller has to find - is how a generated API turns into a leak.
///
/// What happens on a bad file. C has nothing to throw, so the reader returns false and
/// remembers why, and a failed load frees what it had and leaves the table empty. A
/// caller that ignores the return value still sees no rows rather than half of them.
///
/// A third question, which arrived with the split. What includes what. A reference between two
/// tables is a cycle as often as not, and a pointer member needs only an incomplete type - so
/// every record is forward declared in one header that every table header includes, and no
/// table header includes another. An enum is different: a field declared with one is a value,
/// so its complete type has to be there.
///
/// The shapes live in templates/c-*.sbn, one per kind of file, over the shared heads in
/// c-header-head.sbn and c-source-head.sbn. What a file needs comes from
/// <see cref="TypeDependencies"/>.
/// </summary>
[TabbitTarget("c", TargetKind.CodeGeneration, Order = 86)]
public class CCodeGenerator : CodeGenerator<CRecipe>
{

    /// <summary>
    /// A record group generates a struct and either a fixed array or a pointer and a count.
    /// </summary>
    /// <remarks>
    /// The fifth of the thirteen, following the same split - declaration per field, reading
    /// per wire column.
    /// </remarks>
    /// <summary>
    /// MSVC reads a source file with no byte order mark in the system codepage, which
    /// turns a comment taken from a Korean sheet into a line continuation.
    /// </summary>
    protected override bool WritesByteOrderMark => true;

    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a struct declared before the element type and held by value, and the
    /// read reaches it with a longer member path. It is the record's own storage, so nothing
    /// frees it. spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>An optional column becomes a `has_{name}` member beside the value.</summary>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// The per-element answer beside the value, filled from the element bitmap the file
    /// carries. spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private CRecipe _recipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Snake;

    protected override void Run(TargetContext context, CRecipe recipe)
    {
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        SweepStaleOutput(recipe.Path, recipe.Sweep);

        _recipe = recipe;
        _model = context.Model;
        _memberCase = MemberCasing.From(recipe.MemberCase, NameCase.Snake, "c");

        Generate();

        WriteBinaryReaderRuntime();
    }

    private void Generate()
    {
        var view = BuildView();

        Log.Information(
            $"Generating codes for C into `{System.IO.Path.GetFullPath(_recipe.Path)}`");

        // Every record as an incomplete type. This is C's answer to a reference between two
        // tables: a pointer member needs no more than this, so no table header includes
        // another and a cycle between them is not a cycle here.
        // The two keys and the MAC switch are declared here too, and extern "C", stdint.h
        // and stdbool.h come with them: they are the model's rather than any one table's,
        // and this is the only header both the tables and the accessor already include.
        //
        // stdbool.h because the switch is a `bool`, and this header is checked on its own -
        // a header that only compiles after something else has been included is one a
        // consuming project meets as a compile error rather than as a design.
        Write(ForwardHeader, "c-forward.sbn", new CPartView
        {
            Guard = Guard("FORWARD"),
            Includes = new[] { "#include <stdbool.h>", "#include <stdint.h>" },
            Forwards = Array.Empty<string>(),
            ExternC = true,
            Records = view.Tables.Select(table => table.RecordName).ToList(),
            Accessor = view.Accessor,
        });

        foreach (var enumm in view.Enums)
        {
            // An enum needs nothing: its labels are integers, and neither a typedef nor an
            // enum has linkage, so no extern "C" either.
            Write(EnumHeader(enumm), "c-enum.sbn", new CPartView
            {
                Guard = Guard("ENUM_" + enumm.RawName.ToUpperSnakeCase()),
                Includes = Array.Empty<string>(),
                Forwards = Array.Empty<string>(),
                Enumm = enumm,
            });
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            bool anyExtern = pair.rendered.Constants.Any(constant => constant.IsExtern);

            // A uuid constant's type is the reader's, and an enum-typed one names an enum by
            // its complete type. extern "C" because an `extern const` has linkage.
            Write(ConstantsHeader(pair.rendered), "c-constants-header.sbn", new CPartView
            {
                Guard = Guard("CONST_" + pair.rendered.Name.ToUpperSnakeCase()),
                Includes = Includes(
                    reader: NamesUuid(pair.model),
                    headers: TypeDependencies.EnumsNamedBy(pair.model)
                        .Select(EnumHeaderFor)),
                Forwards = Array.Empty<string>(),
                ExternC = anyExtern,
                Set = pair.rendered,
            });

            // And nothing at all when there is none to define: a translation unit holding one
            // include is still one a build system has to be told about.
            if (anyExtern)
            {
                Write(ConstantsSource(pair.rendered), "c-constants-source.sbn", new CPartView
                {
                    Includes = Includes(reader: false, headers: new[] { ConstantsHeader(pair.rendered) }),
                    Set = pair.rendered,
                });
            }
        }

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // The reader for the arena and the index, the forward header for the records it
            // points at, and the complete type of every enum a field is declared with - an
            // enum member is a value, not a pointer, so an incomplete type will not do.
            Write(TableHeader(pair.rendered), "c-table-header.sbn", new CPartView
            {
                Guard = Guard(pair.rendered.RawName.ToUpperSnakeCase()),
                Includes = Includes(
                    reader: true,
                    headers: new[] { ForwardHeader }
                        .Concat(TypeDependencies.EnumsNamedBy(pair.model)
                                .Concat(TypeDependencies.MultiTargetDiscriminatorsOf(pair.model))
                                .Select(EnumHeaderFor))),
                Forwards = Array.Empty<string>(),
                ExternC = true,
                Table = pair.rendered,
            });

            // Its own header first, which is what makes the header prove it compiles alone.
            // The accessor comes along for the name the encryption key is declared under -
            // through the forward header the table header already includes, not through the
            // accessor's own.
            Write(TableSource(pair.rendered), "c-table-source.sbn", new CPartView
            {
                Includes = Includes(reader: false, headers: new[] { TableHeader(pair.rendered) })
                    .Append("").Append("#include <string.h>").ToList(),
                Table = pair.rendered,
                Accessor = view.Accessor,
            });
        }

        // The umbrella. A consumer's `#include "X.h"` is unchanged: it still reaches every
        // generated type, only now by including the headers that declare them.
        Write(FileBase + ".h", "c-accessor-header.sbn", new CPartView
        {
            Guard = Guard(null),
            Includes = Includes(
                reader: false,
                headers: view.Enums.Select(EnumHeader)
                    .Concat(view.ConstantSets.Select(ConstantsHeader))
                    .Concat(view.Tables.Select(TableHeader))),
            Forwards = Array.Empty<string>(),
            ExternC = true,
            Accessor = view.Accessor,
        });

        // snprintf explicitly: the reader's header only reaches for stdio.h inside its
        // implementation branch, which used to be this file and is now its own.
        Write(FileBase + ".c", "c-accessor-source.sbn", new CPartView
        {
            Includes = Includes(reader: false, headers: new[] { FileBase + ".h" })
                .Append("").Append("#include <stdio.h>").Append("#include <string.h>").ToList(),
            Accessor = view.Accessor,
        });

        Write(FileBase + "_Reader.c", "c-reader-source.sbn", new CPartView());

        // Asked for rather than assumed. It reaches the network, and it is the only
        // emitted file that needs a link flag.
        if (_recipe.WriteUpdater)
            Write(FileBase + "_Updater.c", "c-updater-source.sbn", new CPartView());
    }

    // --------------------------------------------------------- file layout

    /// <summary>
    /// Flat, one header per generated type and a source beside the ones that need code.
    /// </summary>
    /// <remarks>
    /// The names carry the grouping rather than directories, as they do for Go, Python, Rust
    /// and Java - and here there is a further reason: an include path is written into the
    /// generated text, so a directory is a string every file has to agree on rather than
    /// something the compiler works out.
    /// </remarks>
    private string ForwardHeader => FileBase + "_Forward.h";

    /// <summary>
    /// Where each kind of generated file goes, and what it is called.
    /// </summary>
    /// <remarks>
    /// The directory is the layout every target shares - `tables/`, `enums/`,
    /// `constants/` - and the `#include` lines come from these same helpers, so a file
    /// and the line that reaches for it cannot disagree.
    ///
    /// The accessor prefix stays in the name even inside a directory. It is what keeps
    /// two Tabbit outputs on one include path from colliding, and a directory does not
    /// take that over: `tables/Template.h` from two of them is the same path twice.
    /// </remarks>
    private string EnumHeader(CEnumView enumm) => $"enums/{FileBase}_Enum{enumm.RawName}.h";
    private string EnumHeaderFor(Models.Enum enumm) => $"enums/{FileBase}_Enum{enumm.Name.ToPascalCase()}.h";

    private string ConstantsHeader(CConstantSetView set) => $"constants/{FileBase}_Const{set.Name}.h";
    private string ConstantsSource(CConstantSetView set) => $"constants/{FileBase}_Const{set.Name}.c";

    private string TableHeader(CTableView table) => $"tables/{FileBase}_{table.RawName.ToPascalCase()}.h";
    private string TableSource(CTableView table) => $"tables/{FileBase}_{table.RawName.ToPascalCase()}.c";

    /// <summary>
    /// An include guard. <paramref name="suffix"/> null gives the umbrella's own, which is
    /// what it has always been, so a consumer testing for it still can.
    /// </summary>
    private string Guard(string? suffix)
        => suffix is null ? UpperPrefix + "_H" : $"{UpperPrefix}_{suffix}_H";

    /// <summary>
    /// Include lines, reader first and then this tool's own, with a blank line between the
    /// groups.
    /// </summary>
    /// <remarks>
    /// The reader comes first because everything else depends on it and nothing in it depends
    /// on anything here - which is the whole of the ordering, the graph being a DAG once the
    /// table-to-table edges are forward declarations instead of includes.
    /// </remarks>
    private static IReadOnlyList<string> Includes(bool reader, IEnumerable<string> headers)
    {
        var lines = new List<string>();

        if (reader)
            lines.Add("#include \"tabbit/tabbit_tcb_reader.h\"");

        var own = headers.Distinct().ToList();

        if (own.Count > 0 && lines.Count > 0)
            lines.Add("");

        foreach (var header in own)
            lines.Add($"#include \"{header}\"");

        return lines;
    }

    /// <summary>
    /// Whether a constant set has a uuid in it, which is the only way its header reaches the
    /// reader - a uuid's type is the reader's own struct.
    /// </summary>
    private static bool NamesUuid(ConstantSet set)
        => set.Constants.Any(constant => constant.Type == ValueType.Uuid);

    /// <summary>
    /// What every generated name starts with, PascalCase.
    ///
    /// The naming here follows the one C has actually settled on for readable systems
    /// code - Doom's and Quake's. A type is PascalCase with a `_t` suffix, a function is
    /// the subsystem prefix and then PascalCase, a struct member is snake_case, and a
    /// constant is SCREAMING_SNAKE. The prefix stands in for the namespace C does not
    /// have, so `TabbitData_ItemRecord_t` and `TabbitData_ItemLoad` rather than the
    /// bare `mobj_t` and `P_SpawnMobj` a single program can get away with.
    /// </summary>
    private string Prefix => _recipe.AccessorName.ToPascalCase();

    /// <summary>The files are named as the recipe named the accessor, unchanged.</summary>
    private string FileBase => _recipe.AccessorName;

    /// <summary>The include guard and the constant names.</summary>
    private string UpperPrefix => _recipe.AccessorName.ToUpperSnakeCase();

    private void Write(string filename, string templateName, CPartView view)
    {
        string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_recipe.Path, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view), WritesByteOrderMark);
    }

    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.C.tabbit_tcb_reader.h",
            System.IO.Path.Combine(_recipe.Path, "tabbit", "tabbit_tcb_reader.h"));

        if (_recipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.C.tabbit_updater.h",
                System.IO.Path.Combine(_recipe.Path, "tabbit", "tabbit_updater.h"));
        }
    }

    // --------------------------------------------------------------- view

    private CFileView BuildView() => new CFileView
    {
        Prefix = Prefix,
        UpperPrefix = UpperPrefix,
        HeaderName = FileBase + ".h",
        Enums = _model.Enums.Select(BuildEnum).ToList(),

        // The names are flat - C has nothing to nest a set in, so the set's name becomes part
        // of each constant's name rather than a scope around them - but they are still
        // grouped by set, because that is the unit a file corresponds to.
        ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),

        Tables = _model.Tables.Select(BuildTable).ToList(),
        Accessor = BuildAccessor(),
    };

    private CEnumView BuildEnum(Models.Enum enumm) => new CEnumView
    {
        RawName = enumm.Name.ToPascalCase(),
        Name = EnumName(enumm),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select(label => new CEnumLabelView
        {
            Name = ConstantName(enumm.Name, label.Name),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };

    private CConstantSetView BuildConstantSet(ConstantSet set) => new CConstantSetView
    {
        Name = set.Name.ToPascalCase(),
        Location = set.Location.ToString(),
        Comment = CommentLines(set.Comment),
        Constants = set.Constants.Select(constant => BuildConstant(set, constant)).ToList(),
    };

    private CConstantView BuildConstant(ConstantSet set, ConstantSet.Constant constant)
    {
        // A uuid is a struct, and a struct defined in a header would be a separate
        // object in every translation unit including it. Those go in the .c.
        bool isStruct = constant.Type == ValueType.Uuid;

        return new CConstantView
        {
            Name = ConstantName(set.Name, constant.Name),
            Type = ScalarTypeName(constant.Type, constant.Enum),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
            IsExtern = isStruct,
        };
    }

    private CTableView BuildTable(Table table) => new CTableView
    {
        RawName = table.Name,
        RecordName = RecordName(table),
        TableName = TableTypeName(table),
        FunctionPrefix = FunctionPrefix(table),
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        MultiReferences = MultiTargetColumns.Of(table)
                                            .Select(column => BuildMultiReference(table, column))
                                            .ToList(),

        // Only the scalars. An array is a pointer now, so a column the file does not carry
        // leaves it NULL with a count of zero - which is an empty array rather than a row of
        // NULL strings, and there is nothing to pre-fill.
        HasStringFields = table.SerialFields.Any(
            sf => !sf.IsRef && !sf.IsArray && sf.ElementType == ValueType.String),

        // One cursor variable for the whole parse rather than one per column: the
        // declarations sit at the top of the function, and each encodable column
        // re-initializes it.
        NeedsCursor = table.WireColumns.Any(UsesCursor),
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
        NeedsPresence = table.WireColumns.Any(wire => wire.IsNullable),
        NeedsElementPresence = table.WireColumns.Any(wire => wire.HasOptionalElements),

        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<CIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
        {
            string family = IndexFamily(sf);

            return new CIndexView
            {
                Member = CName(sf.Name),
                Suffix = sf.Name.ToPascalCase(),
                KeyType = ScalarTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull),
                EntryType = family + "_entry",
                SortCall = family + "_sort",
                FindCall = family + "_find",
                ArrayName = "by_" + sf.Name.ToSnakeCase(),
                FieldName = sf.Name.ToPascalCase(),
            };
        }).ToList();

    /// <summary>
    /// Which of the reader's four index families holds this key.
    /// </summary>
    /// <remarks>
    /// An enum is stored as its number and a bool as one of two, so both order as
    /// int32_t; the three tick-or-64-bit types share the wider one. The field's own C
    /// type is what the lookup takes either way - the family only decides how the
    /// entries are sorted and searched.
    /// </remarks>
    private static string IndexFamily(SerialField sf)
    {
        switch (sf.ElementType)
        {
            case ValueType.String: return "tb_string_index";
            case ValueType.Uuid: return "tb_uuid_index";

            case ValueType.Int64:
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return "tb_index64";

            default: return "tb_index";
        }
    }

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// Read off the referenced table rather than assumed to be `FindByIndex`. The primary
    /// index is whatever the sheet put in the first column, and a sheet that calls it
    /// `Id` generates `FindById`.
    /// </remarks>
    private string PrimaryLookup(Table? refTable)
        => FunctionPrefix(refTable)
           + "FindBy"
           + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();

    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// The key type's empty value means "points at nothing", and a multi-target column honours
    /// it in every language: the discriminator is a value a consumer reads.
    /// spec/reference-optionality.md.
    /// </remarks>
    private static string KeyIsSetSuffix(ValueType keyType)
        => keyType switch
        {
            ValueType.String => "!= NULL && record->$KEY$[0] != 0",
            _ => "!= 0",
        };

    /// <summary>
    /// One column whose value is a row of one of several tables.
    /// </summary>
    private CMultiReferenceView BuildMultiReference(Table table, MultiTargetColumn column)
        => new CMultiReferenceView
        {
            KeyMember = CName(column.Group.Name),
            SlotMember = CName(column.Group.Name + "Row"),
            TargetMember = CName(column.Group.Name + "Target"),
            TargetTypeName = EnumName(column.Discriminator),
            NoneLabel = ConstantName(column.Discriminator.Name, "None"),

            // The string case names the key twice - C has no truthiness - so the suffix
            // carries the member rather than being appended blindly.
            KeyIsSet = KeyIsSetSuffix(column.Field.RefKeyType)
                .Replace("$KEY$", CName(column.Group.Name)),
            Targets = column.Targets.Select(target => new CMultiTargetView
            {
                Table = CName(target.Name),
                RecordName = RecordName(target),
                Function = FunctionPrefix(table)
                    + (column.Group.Name + "As" + target.Name.ToPascalCase()).ToPascalCase(),
                Label = ConstantName(column.Discriminator.Name, target.Name),
                Lookup = PrimaryLookup(target),
            }).ToList(),
        };

    /// <summary>
    /// Members of one level of a record, declaring a struct for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the structs it produces - innermost first, which C requires rather than merely
    /// prefers: a struct has to be complete before another declares a member of it.
    ///
    /// A nested member is the struct by value, so it is the record's own storage and nothing
    /// frees it - the same choice a fixed-length array member here already made.
    /// spec/nested-multi-level.md.
    /// </remarks>
    private List<CRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<CRecordTypeView> declared)
    {
        var result = new List<CRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(new CRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),

                    // The array is the member's when the group is one record - same columns,
                    // same wire, and only which of the two owns it differs. A fixed length, so
                    // it is the member's own storage and nothing frees it.
                    Declaration = MemberDeclaration(member),
                });

                continue;
            }

            // A level below. The tag carries the path: C has one namespace for struct tags, so
            // two records each holding a `Position` would otherwise name one struct twice.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new CRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{RecordName(table)}::{CName(group.Name)}",
            });

            result.Add(new CRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Declaration = $"struct {typeName} {CName(member.Name)};",
            });
        }

        return result;
    }

    private CFieldView BuildField(Table table, SerialField sf)
    {
        string name = CName(sf.Name);

        var recordTypes = new List<CRecordTypeView>();

        var members = sf.IsRecord
            ? BuildRecordMembers(sf.Members, RecordEntryName(table, sf), table, sf, recordTypes)
            : new List<CRecordMemberView>();

        if (sf.IsRecord)
        {
            recordTypes.Add(new CRecordTypeView
            {
                TypeName = RecordEntryName(table, sf),
                Members = members,
                IsOutermost = true,
                Owner = $"{RecordName(table)}::{name}",
            });
        }

        return new CFieldView
        {
            // A record group has no header cell of its own, so the first member's column
            // comment is the nearest thing the sheet said about the group.
            Comment = CommentLines(
                sf.IsRecord ? sf.Members[0].FirstField!.Comment : sf.FirstField!.Comment),
            Name = name,
            IsString = !sf.IsRecord && !sf.IsRef && sf.ElementType == ValueType.String,
            IsFixedArray = !table.IsVariableLength(sf) && sf.IsArray,
            IsVarArray = table.IsVariableLength(sf),
            ElementCount = sf.IsRecord ? sf.RecordElementCount : sf.Fields.Count,
            Declarations = Declarations(table, sf, name),
            IsRecord = sf.IsRecord,
            MembersAreAnonymous = sf.IsRecord && sf.MembersAreAnonymous,
            RecordTypeName = sf.IsRecord ? RecordEntryName(table, sf) : "",
            Members = members,
            RecordTypes = recordTypes,

            // A record group has no presence of its own: absence inside one is the array's
            // length, not a bit per member.
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = "has_" + name,
            ElementPresenceMember = "has_" + name + "_at",
        };
    }

    /// <summary>
    /// The element type declared for a record group.
    /// </summary>
    /// <remarks>
    /// Prefixed with the record's own name because C has one namespace for struct tags: two
    /// tables each holding a `Slot` group would otherwise declare the same type twice.
    /// </remarks>
    private string RecordEntryName(Table table, SerialField sf)
        => RecordName(table) + "_" + CName(sf.Name) + "_entry";

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where it lands.
    /// </summary>
    private CColumnView BuildColumn(Table table, WireColumn wire)
    {
        string name = CName(wire.Group.Name);
        string member = wire.Member is null ? "" : string.Concat(wire.MemberPath.Select(part => "." + CName(part)));
        bool isEnum = !wire.IsRef && wire.ElementType == ValueType.Enum;

        return new CColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            Kind = ReadKind(wire),
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire)
                ? "(void)tb_cursor_next_length(&cursor, &element_count);"
                : "(void)tb_read_counter32(reader, &element_count);",
            RunCall = RunCall(wire),
            RunValueDeclaration = RunValueDeclaration(wire),
            RunSpend = RunSpend(wire),
            Name = name,
            MemberAccess = member,
            MemberAt = wire.MemberAt,
            ElementCount = wire.Cells.Count,
            ElementType = ResolvedElementType(wire),
            ReferenceType = wire.IsRef
                ? (wire.ElementType == ValueType.ForeignRecord
                    ? $"const {ResolvedElementType(wire)}*"
                    : ResolvedElementType(wire))
                : "",
            KeyType = wire.IsRef ? ScalarTypeName(wire.TagCarrier.RefKeyType, null) : "",
            RecordTypeName = wire.Group.IsRecord ? RecordEntryName(table, wire.Group) : "",
            IsFirstMember = wire.IsFirstMember,
            NeedsScratch = isEnum,
            EnumType = isEnum ? EnumName(wire.TagCarrier.Enum) : null,
            // A reference reads into the stored key; what it resolves to is filled in once
            // every table is loaded. The template used to spell this read itself, with the
            // int32 call written in, which is why it had a shape of its own.
            ReadScalar = UsesCursor(wire)
                ? CursorReadCall(wire, ScalarTarget(wire, name, member))
                : ReadCall(wire, ScalarTarget(wire, name, member)),
            ReadElement = ElementRead(wire, name, member),
            ReadScratch = UsesCursor(wire)
                ? "tb_cursor_next_i32(&cursor, &scratch)"
                : "tb_read_enum(reader, &scratch)",
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = "has_" + name,
            ElementPresenceMember = "has_" + name + "_at",
            EmptyAssignment = EmptyAssignmentOf(wire, $"table->records[row].{name}"),
        };
    }

    /// <summary>
    /// The whole statement that puts an absent row's value back, so both read paths land on
    /// the same thing.
    /// </summary>
    /// <remarks>
    /// A statement rather than an expression because not every empty value is one in C: a
    /// uuid is a struct, and `= 0` does not compile for it. `memset` is the portable answer
    /// and needs the target rather than a value.
    ///
    /// A string goes back to `""` rather than NULL, which is the guarantee the rest of this
    /// output makes - a NULL is a crash one printf later.
    ///
    /// An array is a pointer and a count, so both go back: zeroing the pointer alone would
    /// leave a count saying how many elements are behind a NULL, and a consumer walking the
    /// count is then one dereference from a crash. A reference array carries its keys in a
    /// second pointer, which goes with them.
    /// </remarks>
    private string EmptyAssignmentOf(WireColumn wire, string target)
    {
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
        {
            string keys = wire.IsRef ? $" {target}_index = NULL;" : "";

            return $"{{ {target} = NULL;{keys} {target}_count = 0; }}";
        }

        if (wire.ElementType == ValueType.Uuid)
            return $"memset(&{target}, 0, sizeof {target});";

        string value = wire.ElementType switch
        {
            ValueType.String => "\"\"",
            ValueType.Bool => "false",
            ValueType.Enum => $"({EnumName(wire.TagCarrier.Enum)})0",
            _ => "0",
        };

        return $"{target} = {value};";
    }

    /// <summary>
    /// The rendered tb_check_column call: kind, count, and the set of elements this
    /// member accepts - its own plus the lossless promotions, decided here at
    /// generation time rather than in the reader.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsVariableLengthArray
            ? "TB_KIND_VAR_ARRAY"
            : (wire.IsFixedArray ? "TB_KIND_FIXED_ARRAY" : "TB_KIND_SCALAR");

        // -1 where one column owns the whole array: the file states how many elements it
        // holds and the read takes it from there, so there is no length here to hold it to.
        // A record member keeps its count - several columns fill one array and the number
        // they agree on is part of the generated shape, so a disagreement is a schema change
        // rather than data. spec/nullable-array-elements.md.
        bool ownsItsArray = wire.IsFixedArray && wire.Member is null;

        int count = wire.IsVariableLengthArray ? 0 : (ownsItsArray ? -1 : wire.Cells.Count);

        string[] accepted;

        if (wire.IsRef)
        {
            // The key the target is addressed by. `TB_ELEMENT_I32` on its own is what a
            // reference accepted back when a key could only be an int, and it stays the
            // answer for one. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => new[] { "TB_ELEMENT_STRING" },
                ValueType.Int64 => new[] { "TB_ELEMENT_I64", "TB_ELEMENT_I32", "TB_ELEMENT_VARINT" },
                ValueType.Uuid => new[] { "TB_ELEMENT_UUID" },
                _ => new[] { "TB_ELEMENT_I32" },
            };
        }
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = new[] { "TB_ELEMENT_I32", "TB_ELEMENT_VARINT" }; break;
                case ValueType.Int64:
                    accepted = new[] { "TB_ELEMENT_I64", "TB_ELEMENT_I32", "TB_ELEMENT_VARINT" }; break;
                case ValueType.Double:
                    accepted = new[] { "TB_ELEMENT_F64", "TB_ELEMENT_F32", "TB_ELEMENT_I32" }; break;
                case ValueType.Float: accepted = new[] { "TB_ELEMENT_F32" }; break;
                case ValueType.Bool: accepted = new[] { "TB_ELEMENT_BOOL" }; break;
                case ValueType.String: accepted = new[] { "TB_ELEMENT_STRING" }; break;
                case ValueType.Uuid: accepted = new[] { "TB_ELEMENT_UUID" }; break;
                case ValueType.Enum: accepted = new[] { "TB_ELEMENT_VARINT" }; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = new[] { "TB_ELEMENT_I64" }; break;

                default:
                    throw new TabbitException($"The c generator cannot check type `{wire.Type}`.");
            }
        }

        string mask = string.Join(" | ", accepted.Select(name => $"TB_ELEMENT_MASK({name})"));

        // Nullability rides with kind and count: a file that says optional puts a presence
        // bitmap in front of the block, and code not expecting one reads it as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument. A function of its own because C has
        // no default arguments. spec/nullable-array-elements.md.
        if (wire.HasOptionalElements)
        {
            return $"(void)tb_check_column_elements(reader, column, \"{tableName}.{wire.Name}\", " +
                   $"{kind}, {count}, {nullable}, {mask}, true);";
        }

        return $"(void)tb_check_column(reader, column, \"{tableName}.{wire.Name}\", " +
               $"{kind}, {count}, {nullable}, {mask});";
    }

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from - the elements that stay raw by spec keep
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

        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional
        // yes was the same int32 assumption worn differently: a target keyed by `uuid` has
        // no cursor path any more than a `uuid` column does. spec/reference-key-types.md.
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
    /// The rendered tb_cursor_init call ahead of an encodable column's row loop, or
    /// nothing for a column that reads the reader directly.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"(void)tb_cursor_init(&cursor, reader, column, table->count, \"{tableName}.{wire.Name}\");"
            : "";

    /// <summary>
    /// The cursor's run call for a scalar whose values the run encodings cover, or empty
    /// for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to next_i32 or next_string: int32
    /// members, enums, references and strings. The other cursor scalars stay per-row -
    /// their encodings are dictionaries, where the per-row work is already one index
    /// lookup.
    /// </remarks>
    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row does
        // not have one of. Its elements are read one at a time.
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "";

        // A reference runs on the key it carries. `tb_cursor_next_same_i32` was the only
        // answer while a key could only be an int. spec/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                ValueType.Int32 => "tb_cursor_next_same_i32",
                ValueType.String => "tb_cursor_next_same_string",
                _ => "",
            };
        }

        if (wire.ElementType == ValueType.Enum)
            return "tb_cursor_next_same_i32";

        return wire.ElementType switch
        {
            ValueType.Int32 => "tb_cursor_next_same_i32",
            ValueType.String => "tb_cursor_next_same_string",
            _ => "",
        };
    }

    /// <summary>
    /// The declaration of the local the run's value is decoded into, initialized - C
    /// wants both at the top of the block the run loop opens.
    /// </summary>
    private static string RunValueDeclaration(WireColumn wire)
        => RunCall(wire) switch
        {
            "tb_cursor_next_same_string" => "const char* value = NULL;",
            "tb_cursor_next_same_i32" => "int32_t value = 0;",
            _ => "",
        };

    /// <summary>
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// the template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = CName(wire.Group.Name);
        string member = wire.Member is null ? "" : string.Concat(wire.MemberPath.Select(part => "." + CName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"table->records[row].{name}_index = value;"
                : $"table->records[row].{name}{member}_index = value;";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"table->records[row].{name}{member} = ({EnumName(wire.TagCarrier.Enum)})value;";

        return $"table->records[row].{name}{member} = value;";
    }

    /// <summary>
    /// Where one column's value lives in the generated record: the path down to it, and the
    /// subscript that follows when the array is the member's own rather than the group's.
    /// </summary>
    /// <remarks>
    /// Where the element number goes is the whole difference between the record shapes - an
    /// array of records indexes the group and then names the member, a record whose members
    /// are arrays names the member and then indexes it, and a record of one indexes nothing.
    /// spec/nested-multi-level.md.
    ///
    /// The two are kept apart because a reference member declares its key on the member
    /// rather than on the element it holds: `slots.item_id_index[element]`, not
    /// `slots.item_id[element]_index`. spec/references-in-records.md.
    /// </remarks>
    private static (string Path, string Subscript) MemberPlace(
        WireColumn wire, string name, string member, string element)
    {
        if (!wire.IsFixedArray && !wire.IsVariableLengthArray)
            return ($"record->{name}{member}", "");

        if (wire.Group.MembersAreAnonymous)
            return ($"record->{name}[{wire.MemberAt}]", $"[{element}]");

        if (wire.Group.MembersAreArrays)
            return ($"record->{name}{member}", $"[{element}]");

        return ($"record->{name}[{element}]{member}", "");
    }

    /// <summary>
    /// Where a scalar column's value lands.
    /// </summary>
    /// <remarks>
    /// A reference lands in the stored key rather than in the member it resolves to: the
    /// file carries the target's index, and the pointer beside it is filled in once every
    /// table is loaded. spec/reference-key-types.md.
    ///
    /// A record member keeps that key inside the element - `main.item_id_index` rather than
    /// a name built from the group, which nothing declares. A scalar column of a record
    /// group is a group holding one record, so there is no element number here at all.
    /// spec/references-in-records.md.
    /// </remarks>
    private static string ScalarTarget(WireColumn wire, string name, string member)
    {
        if (!wire.IsRef)
            return $"&record->{name}{member}";

        return (wire.Member is null)
            ? $"&record->{name}_index"
            : $"&record->{name}{member}_index";
    }

    /// <summary>
    /// The read filling one element of a row's array.
    /// </summary>
    /// <remarks>
    /// An array's elements arrive encoded exactly as a scalar column's do, so this is the
    /// same call the scalar path makes one level down; what differs is only where the value
    /// lands.
    /// </remarks>
    private static string ElementRead(WireColumn wire, string name, string member)
    {
        var (path, subscript) = MemberPlace(wire, name, member, "element");

        // A reference reads into the index that came off the wire; what it resolves to is
        // filled in once every table is loaded. A member's key sits on the member, and the
        // group's on the group - the two are named differently because only one of them is
        // inside an element type.
        string address = wire.IsRef
            ? (wire.Member is null
                ? $"&record->{name}_index[element]"
                : $"&{path}_index{subscript}")
            : $"&{path}{subscript}";

        return UsesCursor(wire) ? CursorReadCall(wire, address) : ReadCall(wire, address);
    }

    /// <summary>
    /// A complete cursor call filling the given address - the cursor carries the
    /// lossless promotions, so nothing here looks at the column's element.
    /// </summary>
    private static string CursorReadCall(WireColumn wire, string address)
    {
        // An enum's underlying value is an int32. A reference's is whatever key the target
        // is addressed by, so it goes through the switch below on that type rather than
        // being answered here. spec/reference-key-types.md.
        if (wire.ElementType == ValueType.Enum && !wire.IsRef)
            return $"tb_cursor_next_i32(&cursor, {address})";

        switch (wire.IsRef ? wire.RefKeyType : wire.ElementType)
        {
            case ValueType.Int32:
                return $"tb_cursor_next_i32(&cursor, {address})";
            case ValueType.Int64:
                return $"tb_cursor_next_i64(&cursor, {address})";
            case ValueType.Double:
                return $"tb_cursor_next_f64(&cursor, {address})";
            case ValueType.Float:
                return $"tb_cursor_next_f32(&cursor, {address})";
            case ValueType.Bool:
                return $"tb_cursor_next_bool(&cursor, {address})";

            // Ticks, and the member is the ticks - C has no time type that holds
            // either range - so the i64 the column carries is the whole of it.
            case ValueType.DateTime:
            case ValueType.TimeSpan:
                return $"tb_cursor_next_i64(&cursor, {address})";

            default: // String; UsesCursor admits nothing else.
                return $"tb_cursor_next_string(&cursor, {address})";
        }
    }

    /// <summary>
    /// The member declarations.
    ///
    /// A reference contributes the index that came off the wire as well as what it
    /// resolves to, and a variable length array contributes a count beside its pointer -
    /// C has nowhere else to keep either.
    /// </summary>
    private IReadOnlyList<string> Declarations(Table table, SerialField sf, string name)
    {
        // A record group declares the element type above the row struct, so the member is of
        // that type - a fixed array of it, or a pointer and a count when the table trims.
        if (sf.IsRecord)
        {
            // An array of arrays declares no element type: the outer level has no name for
            // one to belong to, so it is a plain two-dimensional array. Fixed on both levels,
            // so it is the record's own storage and nothing frees it.
            if (sf.MembersAreAnonymous)
            {
                string inner = ScalarTypeName(sf.Members[0].ElementType, sf.Members[0].FirstField!.EnumOrNull);
                return new[] { $"{inner} {name}[{sf.Members.Count}][{sf.RecordElementCount}];" };
            }

            string entry = "struct " + RecordEntryName(table, sf);

            if (!sf.IsArray)
                return new[] { $"{entry} {name};" };

            return table.TrimTrailingArrayElements
                ? new[] { $"{entry}* {name};", $"int32_t {name}_count;" }
                : new[] { $"{entry} {name}[{sf.RecordElementCount}];" };
        }

        string elementType = ResolvedElementType(sf);

        if (sf.IsRef)
        {
            // What a reference resolves to depends on which kind it is, and getting this
            // wrong compiled for a while: every scenario that reached the C target had no
            // reference in it, so nothing crossed a table until the conformance corpus
            // grew one.
            //
            // A whole-row reference resolves to the other table's row, so it is a pointer
            // to const - the row belongs to the table it came from, and writing through
            // this one would edit that table's copy.
            //
            // A field reference resolves to one of that row's values, so it is that
            // value's own type. Declaring a pointer there gave `const int32_t* tier` and
            // an assignment of an int32_t to it.
            string resolved = sf.ElementType == ValueType.ForeignRecord
                ? $"const {elementType}*"
                : elementType;

            // The stored key, whose type the target decides. `int32_t` was written here as
            // a constant, which is one of the places that kept a table keyed by anything
            // else from being pointed at. spec/reference-key-types.md.
            string keyType = ScalarTypeName(sf.FirstField!.RefKeyType, null);

            // A pointer and a count rather than a fixed array, for the reason below: how
            // many elements a row holds is what the file states. Both arrays are the same
            // length, so one count answers for the pair.
            return sf.IsArray
                ? new[]
                {
                    $"{resolved}* {name};",
                    $"{keyType}* {name}_index;",
                    $"int32_t {name}_count;",
                }
                : new[]
                {
                    $"{resolved} {name};",
                    $"{keyType} {name}_index;",
                };
        }

        // Every array is a pointer and a count, whether the file writes the length per row
        // or states it once in the column descriptor. A fixed array here would be the length
        // this sheet had when the code was generated, built into the size of the struct - and
        // C cannot size a struct from data, so the choice is between the number and the
        // pointer. The pointer is the shape a trimming table already produced, so a consumer
        // that reads one table reads both. spec/nullable-array-elements.md.
        if (sf.IsArray)
        {
            return new[]
            {
                $"{elementType}* {name};",
                $"int32_t {name}_count;",
            };
        }

        return new[] { $"{elementType} {name};" };
    }

    private static string ReadKind(WireColumn wire)
    {
        // A record's member: the elements came with the row, so a member fills a field of
        // each rather than re-creating them.
        if (wire.Member is not null)
        {
            if (wire.IsVariableLengthArray)
                return "record_var";

            if (!wire.IsFixedArray)
                return "record_member";

            // Which of the two owns the array decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_serial" : "record_serial";
        }

        if (wire.IsVariableLengthArray)
            // A trimmed array of references: the length is the row's, and the keys still go in
            // an array beside the resolved rows. Read as a plain `var_array` it allocated one
            // array and wrote keys into a pointer array, which does not compile - and nothing
            // held the shape, because `foreign[]` is refused and this is only reachable through
            // a folded group with trimming on. spec/variable-length-record-arrays.md.
            return wire.IsRef ? "var_array_ref" : "var_array";

        if (wire.IsFixedArray)
            return wire.IsRef ? "serial_ref" : "serial";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private CAccessorView BuildAccessor() => new CAccessorView
    {
        Name = Prefix,
        TypeName = Prefix + "_t",
        FileExtension = _recipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new CTableSlotView
        {
            Name = CSnakeName(table.Name),
            TableName = TableTypeName(table),
            FunctionPrefix = FunctionPrefix(table),

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
                // key landed. spec/references-in-records.md.
                RecordFields = table.WireColumns
                                    .Where(wire => wire.Member is not null && wire.IsRef)
                                    .Select(BuildRecordReference)
                                    .ToList(),

                // A column reaching several tables is looked up in each of them in turn, so it
                // is a loop of its own too. spec/multi-target-accessors.md.
                MultiFields = MultiTargetColumns.Of(table)
                                                .Select(column => BuildMultiReference(table, column))
                                                .ToList(),
            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0 || x.MultiFields.Count > 0)
            .Select(x => new CCrossReferenceView
            {
                Table = CName(x.Table.Name),
                FunctionPrefix = FunctionPrefix(x.Table),
                RecordName = RecordName(x.Table),
                Fields = x.Fields.Select(BuildReferenceField).ToList(),
                RecordFields = x.RecordFields,
                MultiFields = x.MultiFields,
            })
            .ToList(),
    };

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// No resolution flag: a pointer says whether it resolved by being NULL, which is how
    /// this output already answers that for a reference outside a record. The loop bound says
    /// which of the three record shapes this is - the group's array, the member's, or neither.
    /// spec/references-in-records.md.
    /// </remarks>
    private CRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = CName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + CName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        var (path, subscript) = MemberPlace(wire, name, member, "element");

        return new CRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "_index" + subscript,

            // Whichever array holds the elements. A record group's is the length the member
            // columns agree on and it is written here; every other array carries its own
            // count, so that is read instead.
            Count = wire.IsVariableLengthArray || (wire.IsFixedArray && wire.Member is null)
                ? $"record->{name}_count"
                : wire.IsFixedArray
                    ? wire.Cells.Count.ToString(CultureInfo.InvariantCulture)
                    : "",

            RefTable = CName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
            RefRecordName = RecordName(refTable),
        };
    }

    private CReferenceFieldView BuildReferenceField(SerialField sf)
    {
        string name = CName(sf.Name);
        var refTable = sf.FirstField!.ResolvedRefTable;
        string refRecord = RecordName(refTable);

        return new CReferenceFieldView
        {
            Name = name,
            RefTable = CName(refTable!.Name),
            RefFunctionPrefix = FunctionPrefix(refTable),
            RefLookup = PrimaryLookup(refTable),
            RefRecordName = refRecord,

            // Only a whole-record reference resolves to a pointer. A field reference
            // stores the target's value, and the member is declared as that value's
            // type - so there is nothing to point at.
            Value = sf.ElementType == ValueType.ForeignRecord
                ? "target"
                : "target->" + CName(sf.FirstField!.ResolvedRefField!.Name),

            IsArray = sf.IsArray,

            // The array's own count either way: a reference array is one column's, so its
            // length is the file's. spec/nullable-array-elements.md.
            CountExpression = $"record->{name}_count",
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>A complete reader call filling the given address.</summary>
    private static string ReadCall(WireColumn wire, string address)
    {
        // The key the target is addressed by, which is not always an int32 - a `uuid` key
        // has no cursor path at all, so this is the only call that reads one.
        // spec/reference-key-types.md.
        if (wire.IsRef)
            return LanguageProfile.C.ReadCall(wire.RefKeyType, address);

        // Handled with a scratch int32 and a cast; nothing calls this for one.
        if (wire.ElementType == ValueType.Enum)
            return $"tb_read_enum(reader, {address})";

        // The rest are named in the profile. C is the one language whose reader fills an
        // out-parameter rather than returning the value, which is what the `{0}` in those
        // entries is for.
        return LanguageProfile.C.ReadCall(wire.ElementType, address);
    }

    private string ResolvedElementType(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return RecordName(sf.FirstField!.ResolvedRefTable);

        return ScalarTypeName(sf.FirstField!.ElementType, sf.FirstField!.EnumOrNull);
    }

    /// <summary>
    /// The element type of one wire column.
    /// </summary>
    /// <remarks>
    /// A record group's has to come from the member rather than the group: the group has no
    /// single field - FirstField answers null for one by design - and its members each have
    /// a type of their own, which is the whole reason a record is worth writing.
    /// </remarks>
    private string ResolvedElementType(WireColumn wire)
    {
        // Both, because the group and the column it carries can disagree: a reference
        // resolves to the target's own type on the group while the field still says
        // ForeignRecord, and rendering that as a scalar is not a type C has.
        if (wire.ElementType == ValueType.ForeignRecord
            || wire.TagCarrier.ElementType == ValueType.ForeignRecord)
        {
            return RecordName(wire.TagCarrier.ResolvedRefTable);
        }

        return ScalarTypeName(wire.TagCarrier.ElementType, wire.TagCarrier.EnumOrNull);
    }

    /// <summary>
    /// One leaf member of a record element, declared.
    /// </summary>
    /// <remarks>
    /// A reference member is two declarations rather than one: the row it resolved to, and the
    /// key that came off the wire. Both inside the element, because a group may hold more than
    /// one reference and a name built from the group and the target would collide the moment
    /// two members point at one table.
    ///
    /// No third member for whether the resolution happened - a pointer answers that by being
    /// NULL, which is how this output already answers it for a reference that is not in a
    /// record. spec/references-in-records.md.
    /// </remarks>
    private string MemberDeclaration(RecordMember member)
    {
        string name = CName(member.Name);

        if (member.IsRef)
        {
            string row = $"const {RecordName(member.FirstField!.ResolvedRefTable)}*";
            string key = ScalarTypeName(member.FirstField!.RefKeyType, null);

            return member.IsArray
                ? $"{row} {name}[{member.Fields.Count}]; "
                  + $"{key} {name}_index[{member.Fields.Count}];"
                : $"{row} {name}; {key} {name}_index;";
        }

        string type = ScalarTypeName(member.ElementType, member.FirstField!.EnumOrNull);

        return member.IsArray
            ? $"{type} {name}[{member.Fields.Count}];"
            : $"{type} {name};";
    }

    private string ScalarTypeName(ValueType type, Models.Enum? enumm)
    {
        if (ValueTypes.ElementOf(type) == ValueType.Enum)
            return EnumName(enumm!);

        return LanguageProfile.C.ScalarTypeName(type);
    }

    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        switch (constant.Type)
        {
            case ValueType.String:
                return Quote((string)constant.Value!);

            // C has no bool literal without <stdbool.h>, which the reader includes -
            // and the header includes the reader, so these are safe.
            case ValueType.Bool:
                return (bool)constant.Value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)constant.Value!).ToString(CultureInfo.InvariantCulture);

            // The suffix matters: without it the literal is an int and the value is
            // truncated before it ever reaches the constant.
            case ValueType.Int64:
                return ((long)constant.Value!).ToString(CultureInfo.InvariantCulture) + "LL";

            case ValueType.Float:
                return ((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture) + "f";

            case ValueType.Double:
                return ((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            case ValueType.DateTime:
                return ((DateTime)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture) + "LL";

            case ValueType.TimeSpan:
                return ((TimeSpan)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture) + "LL";

            case ValueType.Uuid:
                return "{ { " + string.Join(", ",
                    ((Guid)constant.Value!).ToByteArray()
                        .Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture))) + " } }";

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel(constant.Value!, constant.Location);

                return ConstantName(constant.Enum.Name, label.Name);
            }

            default:
                throw new TabbitException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the c generator cannot render.");
        }
    }

    /// <summary>
    /// A C string literal.
    ///
    /// Non-ASCII goes through as UTF-8 bytes rather than as an escape: the generated
    /// files are UTF-8 and so is the format, and \u in a narrow literal is
    /// implementation-defined. A question mark is escaped because three of them in a
    /// row start a trigraph in C99.
    /// </summary>
    private static string Quote(string value)
    {
        var literal = new StringBuilder("\"");

        foreach (var c in value ?? "")
        {
            switch (c)
            {
                case '"': literal.Append("\\\""); break;
                case '\\': literal.Append(@"\\"); break;
                case '\n': literal.Append(@"\n"); break;
                case '\r': literal.Append(@"\r"); break;
                case '\t': literal.Append(@"\t"); break;
                case '?': literal.Append(@"\?"); break;

                default:
                    if (c < 0x20)
                        literal.Append(@"\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else
                        literal.Append(c);

                    break;
            }
        }

        return literal.Append('"').ToString();
    }

    // ------------------------------------------------------------- helpers

    // The naming, in one place. Doom and Quake's conventions: a type is PascalCase with
    // a `_t` suffix, a function is a subsystem prefix and then PascalCase, a member is
    // snake_case and a constant is SCREAMING_SNAKE. The prefix is what stands in for
    // the namespace C does not have.

    private string EnumName(Models.Enum enumm) => $"{Prefix}_{enumm.Name.ToPascalCase()}_t";

    private string RecordName(Table? table) => $"{Prefix}_{table!.Name.ToPascalCase()}Record_t";

    private string TableTypeName(Table table) => $"{Prefix}_{table.Name.ToPascalCase()}Table_t";

    /// <summary>
    /// What a table's functions are called, minus the verb.
    ///
    /// `TabbitData_Item`, so the template appends `Load`, `Free` or `Find` and gets
    /// `TabbitData_ItemLoad` - one underscore, at the subsystem boundary, as in
    /// `P_SpawnMobj`.
    /// </summary>
    private string FunctionPrefix(Table? table) => $"{Prefix}_{table!.Name.ToPascalCase()}";

    private string ConstantName(params string[] parts)
        => (UpperPrefix + "_" + string.Join("_", parts.Select(p => p.ToSnakeCase())))
           .ToUpperInvariant();

    /// <summary>A member name, snake_case as Doom writes them.</summary>
    private string CName(string? name) => LanguageProfile.C.MemberName(name!.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// snake_case because that is how C writes an identifier, not because a member is
    /// spelled that way. Sharing one function let the two look like one rule.
    /// </remarks>
    private static string CSnakeName(string name) => LanguageProfile.C.MemberName(name.ToSnakeCase());

}
