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
/// C++17 header. Reads the binary export.
/// </summary>
public class CppRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Name of the generated accessor, which also names the file it lands in.
    ///
    /// The other generated types are files of their own beside it, named after
    /// themselves - a table, an enum and a constant set each get one.
    /// </summary>
    public string AccessorName { get; set; } = "Tables";

    /// <summary>
    /// Namespace to wrap the generated code in. Omitting it puts everything
    /// in the global namespace, where the names may collide with something.
    /// </summary>
    public string Namespace { get; set; } = "";

    /// <summary>
    /// Extension the generated reader expects on table files. Must match the
    /// binary export's FileExtension.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    /// </summary>
    /// <remarks>
    /// It fetches the manifest and the changed data files over HTTP and keeps
    /// a local copy current, so a program can take new data without being
    /// redeployed.
    ///
    /// Off by default, and here that means more than elsewhere: C++ has no
    /// HTTP client in its standard library, so this is the only emitted file
    /// that links against anything - libcurl, and nothing else. Leave it off
    /// and the generated C++ depends on the standard library alone.
    /// </remarks>
    public bool WriteUpdater { get; set; } = false;

    /// <summary>
    /// Whether generated files this run did not write are removed from
    /// <see cref="Path"/>.
    /// </summary>
    /// <remarks>
    /// On, because the output is a file per table: delete a table from the sheets
    /// and its file stays behind naming types nothing declares any more. Only
    /// files carrying this tool's own header are removed, so a directory holding
    /// your own source is safe.
    ///
    /// Turn it off if you edit the generated files, which is a decision worth a
    /// line in a recipe.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for
    /// both. Entities and fields marked for the other side are left out.
    ///
    /// Declare the same side on the exporter and on the code generator
    /// that reads its files: the two must agree on the column set or the
    /// generated reader will not match the data.
    /// </summary>
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
/// Emits a header per generated type, plus an umbrella header a consumer includes.
///
/// Splitting a C++ target used to be the thing not worth doing, on the grounds that it would
/// push include-order management for the references between tables onto whoever read the
/// output. It does not: a record holding a whole-row reference has a pointer member, a pointer
/// needs only an incomplete type, and so every record is forward declared in one header that
/// all the table headers include. No table header includes another, which is what makes two
/// tables pointing at each other not a cycle - and a cycle between include-guarded headers
/// does not fail loudly, it resolves differently depending on which translation unit got
/// there first.
///
/// An enum is the opposite case: a field declared with one is a value, so its complete type
/// has to be there and its header is a real include.
///
/// The shapes live in templates/cpp-*.sbn, one per kind of file, over the shared head and
/// foot in cpp-file-head.sbn and cpp-file-foot.sbn. This file works out the values
/// that shape needs - read calls, defaults, escaped names, rendered literals - and
/// nothing else. Everything here used to be printer calls with the header's structure
/// spread through string literals across several hundred lines, which made the part a
/// reviewer cares about the part hardest to see.
///
/// Reading is done by lib/cpp/tabbit/tcb_reader.h, which the emitted
/// header includes. That reader is the C++ half of the format the binary exporter
/// writes, so the two have to change together.
/// </summary>
[TabbitTarget("cpp", TargetKind.CodeGeneration, Order = 10)]
public class CppCodeGenerator : CodeGenerator<CppRecipe>
{

    /// <summary>
    /// A record group generates a plain struct and a vector of it; a member column fills one
    /// of its fields.
    /// </summary>
    /// <remarks>
    /// The third of the thirteen, following the same split as csharp and typescript -
    /// declaration per field, reading per wire column.
    /// </remarks>
    /// <summary>
    /// MSVC reads a source file with no byte order mark in the system codepage, which
    /// turns a comment taken from a Korean sheet into a line continuation.
    /// </summary>
    protected override bool WritesByteOrderMark => true;

    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a struct declared before the element type, and the read reaches it with
    /// a longer member path. Its own members carry their initializers, so declaring it is
    /// enough. spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `has_{name}` member beside the value.
    /// </summary>
    /// <remarks>
    /// Not `std::optional`, so that one shape covers every language and C++ does not
    /// split from Unreal - where the member is a UPROPERTY and cannot be optional at all.
    /// spec/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// `has_x_at(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private CppRecipe _cppRecipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Snake;

    protected override void Run(TargetContext context, CppRecipe cppRecipe)
    {
        if (string.IsNullOrEmpty(cppRecipe.Path))
            return;

        SweepStaleOutput(cppRecipe.Path, cppRecipe.Sweep);

        _cppRecipe = cppRecipe;

        // Already narrowed to the side this entry is built for. Both (the default)
        // leaves the model unchanged.
        _model = context.Model;
        _memberCase = MemberCasing.From(cppRecipe.MemberCase, NameCase.Snake, "cpp");

        GenerateModel();
        WriteBinaryReaderRuntime();
    }

    /// <summary>
    /// Writes the Tcb reader beside the generated header.
    ///
    /// Emitted rather than left in lib/cpp for the consumer to put on an include
    /// path. The generated header includes it by a relative path, so the output
    /// directory is self-contained and there is no way to pair generated code with a
    /// reader of a different vintage.
    ///
    /// The source is an embedded resource taken from lib/cpp, so there is one copy to
    /// maintain and it cannot drift from what is shipped.
    /// </summary>
    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Cpp.tcb_reader.h",
            Path.Combine(_cppRecipe.Path, "tabbit", "tcb_reader.h"));

        // Asked for rather than assumed. It reaches the network, and it is the only
        // emitted file that needs a link flag.
        if (_cppRecipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Cpp.updater.h",
                Path.Combine(_cppRecipe.Path, "tabbit", "updater.h"));
        }
    }

    /// <summary>The accessor type's name, in the casing this language's types use.</summary>
    private string AccessorType => _cppRecipe.AccessorName.ToPascalCase();

    private void GenerateModel()
    {
        var view = BuildView();

        Log.Information(
            $"Generating codes for C++ into `{Path.GetFullPath(_cppRecipe.Path)}`");

        // Every record as an incomplete type, which is what a pointer member needs and all a
        // reference between two tables needs - so no table header includes another. The
        // encryption key rides along, for the vector and the byte it is made of.
        Write(ForwardHeader, "cpp-forward.sbn", Part(
            Guard("FORWARD"),
            new[] { "<cstdint>", "<vector>" },
            part => part.Records = view.Tables.Select(table => table.RecordName).ToList()));

        foreach (var pair in _model.Enums.Zip(view.Enums, (model, rendered) => (model, rendered)))
        {
            // An enum names its underlying type and nothing else.
            Write(EnumHeader(pair.rendered), "cpp-enum.sbn", Part(
                Guard("ENUM_" + pair.rendered.Name.ToSnakeCase()),
                new[] { "<cstdint>" },
                part => part.Enumm = pair.rendered));
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            // A constant set names the types of its own constants: an integer type, a string,
            // one of the reader's for a datetime, timespan or uuid, and an enum where one is
            // declared with it.
            Write(ConstantsHeader(pair.rendered), "cpp-constants.sbn", Part(
                Guard("CONST_" + pair.rendered.Name.ToSnakeCase()),
                StandardHeadersFor(pair.model.Constants.Select(constant => constant.Type))
                    .Concat(NeedsReader(pair.model) ? new[] { ReaderInclude } : Array.Empty<string>())
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumHeaderFor)),
                part => part.Set = pair.rendered));
        }

        foreach (var pair in _model.Tables.Zip(view.Tables, (model, rendered) => (model, rendered)))
        {
            // A table always holds a vector of rows and a map from index to position, always
            // takes a filename as a string, and always reads through the reader. On top of
            // that: the forward header for the records it points at, and the complete type of
            // every enum a field is declared with - an enum member is a value, not a pointer.
            Write(TableHeader(pair.rendered), "cpp-table.sbn", Part(
                Guard(pair.rendered.RawName.ToSnakeCase()),
                new[] { "<cstddef>", "<cstdint>", "<string>", "<unordered_map>", "<vector>", ReaderInclude }
                    .Append(ForwardHeader)
                    .Concat(TypeDependencies.EnumsNamedBy(pair.model).Select(EnumHeaderFor)),
                part => part.Table = pair.rendered));
        }

        // The umbrella. A consumer's include is unchanged - same file name, same guard, same
        // types reachable from it - only now it reaches them by including the headers that
        // declare them.
        Write(AccessorType + ".h", "cpp-accessor.sbn", Part(
            IncludeGuard(_cppRecipe.AccessorName),
            new[] { "<cstddef>", "<string>" }
                .Concat(view.Enums.Select(EnumHeader))
                .Concat(view.ConstantSets.Select(ConstantsHeader))
                .Concat(view.Tables.Select(TableHeader)),
            part => part.Accessor = view.Accessor));
    }

    // --------------------------------------------------------- file layout

    /// <summary>
    /// Flat, one header per generated type.
    /// </summary>
    /// <remarks>
    /// The names carry the grouping rather than directories. C++ has namespaces, so
    /// subdirectories would be possible - but an include path is written into the generated
    /// text, so a directory is a string every file has to agree on rather than something the
    /// compiler works out. Same reasoning as C, and the two targets are better off answering
    /// it the same way.
    /// </remarks>
    private string ForwardHeader => _cppRecipe.AccessorName + "_forward.h";

    /// <summary>
    /// Where each kind of generated header goes, and what it is called.
    /// </summary>
    /// <remarks>
    /// The directory is the layout every target shares - `tables/`, `enums/`,
    /// `constants/` - and the `#include` lines come from these same helpers, so a file
    /// and the line that reaches for it cannot disagree.
    ///
    /// The accessor prefix stays in the name even inside a directory. It is what keeps
    /// two Tabbit outputs on one include path from colliding, and a directory does not
    /// take that over: `tables/template.h` from two of them is the same path twice.
    /// </remarks>
    private string EnumHeader(CppEnumView enumm) => $"enums/{_cppRecipe.AccessorName}_enum_{enumm.Name.ToSnakeCase()}.h";
    private string EnumHeaderFor(Models.Enum enumm) => $"enums/{_cppRecipe.AccessorName}_enum_{enumm.Name.ToSnakeCase()}.h";

    private string ConstantsHeader(CppConstantSetView set) => $"constants/{_cppRecipe.AccessorName}_const_{set.Name.ToSnakeCase()}.h";

    private string TableHeader(CppTableView table) => $"tables/{_cppRecipe.AccessorName}_{table.RawName.ToSnakeCase()}.h";

    private const string ReaderInclude = "\"tabbit/tcb_reader.h\"";

    private string Guard(string? suffix) => IncludeGuard($"{_cppRecipe.AccessorName}_{suffix}");

    private void Write(string filename, string templateName, CppPartView view)
    {
        string full = Path.GetFullPath(Path.Combine(_cppRecipe.Path, filename));

        StagingFiles.WriteAllTextToFile(full, TemplateEngine.Render(templateName, view), WritesByteOrderMark);
    }

    /// <summary>
    /// The common shape of every part: the guard, the namespace, and the includes - standard
    /// library first, then this tool's own, with a blank line between.
    /// </summary>
    private CppPartView Part(string guard, IEnumerable<string> includes, Action<CppPartView> subject)
    {
        var parts = NamespaceParts().ToList();

        var part = new CppPartView
        {
            IncludeGuard = guard,
            AccessorName = AccessorType,
            Includes = IncludeLines(includes),
            NamespaceOpen = parts.Select(name => $"namespace {name} {{").ToList(),
            NamespaceClose = Enumerable.Reverse(parts).Select(name => $"}}  // namespace {name}").ToList(),
        };

        subject(part);

        return part;
    }

    /// <summary>
    /// `#include` lines, angle-bracketed ones first and quoted ones after, each group in the
    /// order given and separated by a blank line.
    /// </summary>
    private static IReadOnlyList<string> IncludeLines(IEnumerable<string> includes)
    {
        var all = includes.Distinct().ToList();

        var standard = all.Where(name => name.StartsWith("<", StringComparison.Ordinal)).ToList();
        var own = all.Where(name => !name.StartsWith("<", StringComparison.Ordinal)).ToList();

        var lines = standard.Select(name => $"#include {name}").ToList();

        if (standard.Count > 0 && own.Count > 0)
            lines.Add("");

        lines.AddRange(own.Select(name => name.StartsWith("\"", StringComparison.Ordinal)
            ? $"#include {name}"
            : $"#include \"{name}\""));

        return lines;
    }

    /// <summary>
    /// The standard headers a set of value types names between them.
    /// </summary>
    private static IEnumerable<string> StandardHeadersFor(IEnumerable<ValueType> types)
    {
        var seen = types.Select(ValueTypes.ElementOf).ToList();

        if (seen.Any(type => type == ValueType.Int32 || type == ValueType.Int64))
            yield return "<cstdint>";

        if (seen.Contains(ValueType.String))
            yield return "<string>";
    }

    /// <summary>
    /// Whether a constant set names one of the reader's own types: a datetime, a timespan or
    /// a uuid. Those are the only three a constant can be that C++ has no built-in for.
    /// </summary>
    private static bool NeedsReader(ConstantSet set)
        => set.Constants.Any(constant =>
            constant.Type == ValueType.DateTime
            || constant.Type == ValueType.TimeSpan
            || constant.Type == ValueType.Uuid);

    // --------------------------------------------------------------- view

    private CppFileView BuildView()
    {
        var parts = NamespaceParts().ToList();

        return new CppFileView
        {
            IncludeGuard = IncludeGuard(_cppRecipe.AccessorName),
            AccessorName = AccessorType,

            NamespaceOpen = parts.Select(part => $"namespace {part} {{").ToList(),

            // Innermost first, and each closer names its namespace, because a header
            // that ends in a run of bare braces is unreadable.
            NamespaceClose = Enumerable.Reverse(parts).Select(part => $"}}  // namespace {part}").ToList(),

            Enums = _model.Enums.Select(BuildEnum).ToList(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
            Tables = _model.Tables.Select(BuildTable).ToList(),
            Accessor = BuildAccessor(),
        };
    }

    private CppEnumView BuildEnum(Models.Enum enumm) => new CppEnumView
    {
        // Fixed underlying type because values travel as int32, and scoped so label
        // names cannot collide across declarations - both decided in the template.
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select(label => new CppEnumLabelView
        {
            Name = label.Name.ToPascalCase(),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
        }).ToList(),
    };

    private CppConstantSetView BuildConstantSet(ConstantSet constantSet) => new CppConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new CppConstantView
        {
            Name = constant.Name.ToPascalCase(),
            Type = ToCppTypeName(constant.Type, constant.Enum),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    private CppTableView BuildTable(Table table) => new CppTableView
    {
        RawName = table.Name,
        RecordName = RecordName(table),
        TableName = TableName(table),
        Location = table.Location.ToString(),
        Comment = CommentLines(table.Comment),
        Indexes = Indexes(table),
        Fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList(),
        Columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList(),
        NeedsPresence = table.WireColumns.Any(wire => wire.IsNullable),
        NeedsElementPresence = table.WireColumns.Any(wire => wire.HasOptionalElements),
    };

    /// <summary>
    /// The indexed fields of a table: the sheet's first column, plus every one marked
    /// with a `*`.
    /// </summary>
    private IReadOnlyList<CppIndexView> Indexes(Table table)
        => table.SerialFields.Where(sf => sf.IsIndexer).Select(sf =>
        {
            string keyType = ToCppTypeName(sf.FirstField);
            bool copyCosts = keyType == "std::string";

            return new CppIndexView
            {
                Member = CppName(sf.Name),
                Suffix = sf.Name.ToSnakeCase(),
                KeyType = keyType,
                KeyParam = copyCosts ? "const " + keyType + "&" : keyType,

                KeyText = KeyText(sf, copyCosts),

                MapName = "by_" + sf.Name.ToSnakeCase() + "_",
                FieldName = sf.Name.ToPascalCase(),
            };
        }).ToList();

    /// <summary>
    /// The key as text, for the message a missing row throws.
    /// </summary>
    /// <remarks>
    /// `std::to_string` covers the arithmetic types and nothing else, so each key type that
    /// is not one needs saying. A `std::string` concatenates as it is; an enum is cast to its
    /// number first, because `enum class` does not convert on its own; and a uuid has its own
    /// `to_string`, which is the canonical 8-4-4-4-12 form the other languages also print.
    ///
    /// The uuid case was missing and the compiler is what said so - `std::to_string` has no
    /// overload for it, so a `uuid`-keyed table did not build at all. The enum case was
    /// already here, which is why an `enum`-keyed table did.
    /// </remarks>
    private static string KeyText(SerialField sf, bool isString)
    {
        if (isString)
            return "key";

        switch (sf.ElementType)
        {
            case Models.ValueType.Enum:
                return "std::to_string(static_cast<std::int64_t>(key))";

            case Models.ValueType.Uuid:
                return "key.to_string()";

            default:
                return "std::to_string(key)";
        }
    }

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
    /// One declared member of the record: what it is called and what it holds.
    /// </summary>
    /// <remarks>
    /// Declaration only. How a column is read is <see cref="BuildColumn"/>'s business,
    /// and the two are not the same unit - a record group declares one member and is read
    /// as one column per member of it. spec/nested-fields.md has the split.
    /// </remarks>
    /// <summary>
    /// Members of one level of a record, declaring a struct for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/>
    /// collects the structs it produces - innermost first, which C++ requires rather than
    /// merely prefers: a struct has to be a complete type before another declares a member of
    /// it.
    ///
    /// A nested member takes no initializer. Its own struct's members carry theirs, so
    /// declaring it is enough for every value inside it to start where a scalar member would.
    /// spec/nested-multi-level.md.
    /// </remarks>
    private List<CppRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, Table table, SerialField group,
        List<CppRecordTypeView> declared)
    {
        var result = new List<CppRecordMemberView>();

        foreach (var member in members)
        {
            if (member.IsLeaf)
            {
                result.Add(new CppRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    Name = CppName(member.Name),
                    Declarations = MemberDeclarations(member),
                });

                continue;
            }

            // A level below. The type name carries the path so two records each holding a
            // `Position` do not name one struct twice.
            string typeName = prefix + member.Name.ToPascalCase();
            var nested = BuildRecordMembers(member.Members, typeName, table, group, declared);

            declared.Add(new CppRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                IsOutermost = false,
                Owner = $"{table.Name.ToPascalCase()}Record::{CppName(group.Name)}",
            });

            result.Add(new CppRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                Name = CppName(member.Name),
                Declarations = new[] { $"{typeName} {CppName(member.Name)};" },
            });
        }

        return result;
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
    /// null, which is how this output already answers it for a reference that is not in a
    /// record. spec/references-in-records.md.
    /// </remarks>
    private IReadOnlyList<string> MemberDeclarations(RecordMember member)
    {
        string name = CppName(member.Name);

        if (member.IsRef)
        {
            string row = "const " + RecordName(member.FirstField!.ResolvedRefTable) + "*";
            string key = ToCppTypeName(member.FirstField!.RefKeyType, null);

            return member.IsArray
                ? new[]
                {
                    $"std::vector<{row}> {name};",
                    $"std::vector<{key}> {name}_index;",
                }
                : new[]
                {
                    $"{row} {name} = nullptr;",
                    $"{key} {name}_index{RefKeyInitializer(member.FirstField!.RefKeyType)};",
                };
        }

        // The vector is the member's when the group is one record - same columns, same wire,
        // and only which of the two owns it differs.
        return member.IsArray
            ? new[] { $"std::vector<{ToCppTypeName(member.FirstField)}> {name};" }
            : new[]
            {
                $"{ToCppTypeName(member.FirstField)} {name}{DefaultInitializer(member.FirstField)};",
            };
    }

    private CppFieldView BuildField(Table table, SerialField sf)
    {
        string name = CppName(sf.Name);

        var recordTypes = new List<CppRecordTypeView>();

        var members = sf.IsRecord
            ? BuildRecordMembers(sf.Members, RecordEntryName(table, sf), table, sf, recordTypes)
            : new List<CppRecordMemberView>();

        if (sf.IsRecord)
        {
            recordTypes.Add(new CppRecordTypeView
            {
                TypeName = RecordEntryName(table, sf),
                Members = members,
                IsOutermost = true,
                Owner = $"{table.Name.ToPascalCase()}Record::{name}",
            });
        }

        return new CppFieldView
        {
            // A record group has no header cell of its own, so the first member's column
            // comment is the nearest thing the sheet said about the group.
            Comment = CommentLines(sf.IsRecord ? sf.Members[0].FirstField!.Comment : sf.FirstField!.Comment),
            Declarations = Declarations(table, sf, name),
            Name = name,
            ElementCount = sf.IsRecord ? sf.RecordElementCount : sf.Fields.Count,
            IsRecord = sf.IsRecord,
            RecordTypeName = sf.IsRecord ? RecordEntryName(table, sf) : "",
            RecordTypes = recordTypes,
            Members = members,
            MembersAreAnonymous = sf.IsRecord && sf.MembersAreAnonymous,
            OuterCount = sf.IsRecord ? sf.Members.Count : 0,
            ElementTypeName = (sf.IsRecord && sf.MembersAreAnonymous)
                ? ToCppTypeName(sf.Members[0].FirstField)
                : "",
            IsRecordArray = sf.IsRecord && sf.IsArray,

            // A record group has no presence of its own: absence inside one is the array's
            // length, not a bit per member.
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceMember = "has_" + name,
            ElementPresenceMember = "has_" + name + "_at_",
        };
    }

    /// <summary>
    /// The element type declared for a record group.
    /// </summary>
    /// <remarks>
    /// Prefixed with the record's own name because these are file-scope structs: two tables
    /// each holding a `Pos` group would otherwise declare `pos_entry` twice, and C++ has no
    /// namespace here to keep them apart. The same reasoning gives the accessor its prefix.
    /// </remarks>
    private string RecordEntryName(Table table, SerialField sf)
        => RecordName(table) + "_" + CppName(sf.Name) + "_entry";

    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where it lands.
    /// </summary>
    /// <remarks>
    /// A record group's member columns each fill one field of the generated element type,
    /// which is what `MemberAccess` carries - `record.slot[j].id` instead of
    /// `record.slot[j]`.
    /// </remarks>
    private CppColumnView BuildColumn(Table table, WireColumn wire)
    {
        string name = CppName(wire.Group.Name);
        string member = wire.Member is null ? "" : string.Concat(wire.MemberPath.Select(part => "." + CppName(part)));

        return new CppColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            LengthRead = UsesCursor(wire)
                ? "const std::int32_t element_count = cursor.next_length();"
                : "const std::int32_t element_count = reader.read_counter32();",
            RunCall = RunCall(wire),
            RunValueType = RunValueType(wire),
            RunSpend = RunSpend(wire),
            Kind = ReadKind(wire),
            Name = name,
            MemberAccess = member,
            MemberAt = wire.MemberAt,

            // A reference member that is the vector holds one key per element as well as one
            // row, so both are sized before the read - the read writes into the key, and
            // sizing only the row left it writing past the end.
            // spec/references-in-records.md.
            MemberRefSuffix = (wire.Member is not null && wire.IsRef) ? "_index" : "",
            OuterCount = wire.Group.IsRecord ? wire.Group.Members.Count : 0,
            ElementCount = wire.Cells.Count,
            RefDefault = RefDefault(wire.Group),
            ReadScalar = ScalarReadExpression(wire, "record." + name + member),

            // Only the stored index of a reference is on the wire, so that is what an
            // element read fills; the value it resolves to is assigned once every table is
            // loaded. A record member keeps that key on the member and before any subscript -
            // `slots.item_id_index[j]` rather than `slots.item_id[j]_index`, which is not an
            // expression at all. spec/references-in-records.md.
            ReadElement = ReadElementExpression(wire, ElementTarget(wire, name, member, "j")),
            ReadVarElement = ReadElementExpression(
                wire, ElementTarget(wire, name, member, "static_cast<std::size_t>(j)")),
            IsFirstMember = wire.IsFirstMember,
            RecordTypeName = wire.Group.IsRecord ? RecordEntryName(table, wire.Group) : "",
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            PresenceMember = "has_" + name,
            ElementPresenceMember = "has_" + name + "_at_",
            EmptyValue = EmptyValueOf(wire),
        };
    }

    /// <summary>
    /// What an absent row's value is set to, so both read paths land on the same thing.
    /// </summary>
    private string EmptyValueOf(WireColumn wire)
    {
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "{}";

        return wire.ElementType switch
        {
            ValueType.String => "std::string()",
            ValueType.Bool => "false",
            ValueType.Uuid => "tabbit::Uuid()",
            ValueType.DateTime => "tabbit::DateTime()",
            ValueType.TimeSpan => "tabbit::TimeSpan()",
            ValueType.Enum => $"static_cast<{ToCppTypeName(wire.TagCarrier)}>(0)",
            _ => "0",
        };
    }

    /// <summary>
    /// The member declarations for a field.
    ///
    /// A reference gets two: the resolved value, and the raw index it was read as. The
    /// target is not known until every table is loaded, so the first starts empty.
    /// </summary>
    private IReadOnlyList<string> Declarations(Table table, SerialField sf, string name)
    {
        if (sf.IsRef)
        {
            string resolved = ResolvedRefTypeName(sf);

            return sf.IsArray
                ? new[]
                {
                    $"std::vector<{resolved}> {name};",
                    $"std::vector<std::int32_t> {name}_index;",
                }
                : new[]
                {
                    $"{resolved} {name} = {RefDefault(sf)};",
                    $"std::int32_t {name}_index = 0;",
                };
        }

        // A record group declares the element type above it, so the member is of that type.
        if (sf.IsRecord)
        {
            // An array of arrays has no element type to name, so the inner vector is the
            // type - see spec/nested-multi-level.md.
            if (sf.MembersAreAnonymous)
                return new[] { $"std::vector<std::vector<{ToCppTypeName(sf.Members[0].FirstField)}>> {name};" };

            string entry = RecordEntryName(table, sf);

            return sf.IsArray
                ? new[] { $"std::vector<{entry}> {name};" }
                : new[] { $"{entry} {name};" };
        }

        string type = ToCppTypeName(sf.FirstField);

        return sf.IsArray
            ? new[] { $"std::vector<{type}> {name};" }
            : new[] { $"{type} {name}{DefaultInitializer(sf.FirstField)};" };
    }

    /// <summary>
    /// Which of the five read shapes a field takes.
    ///
    /// A variable-length array is tested first because it is also an array: its length
    /// varies per row and so precedes the elements on the wire, where a serial field's
    /// length is its column count and the generated code already knows it.
    /// </summary>
    private static string ReadKind(WireColumn wire)
    {
        // A record's member: the elements are filled in place, because the vector and its
        // elements came with the record and re-creating it per member would discard what
        // the members before it wrote.
        if (wire.Member is not null)
        {
            if (wire.IsVariableLengthArray)
                return "record_var";

            if (!wire.IsFixedArray)
                return "record_member";

            // Which of the two owns the vector decides where the index goes, and an unnamed
            // outer level is indexed rather than named.
            if (wire.Group.MembersAreAnonymous)
                return "array_of_arrays_member";

            return wire.Group.MembersAreArrays ? "record_member_serial" : "record_serial";
        }

        if (wire.IsVariableLengthArray)
            return "var_array";

        if (wire.IsFixedArray)
            return wire.IsRef ? "serial_ref" : "serial";

        return wire.IsRef ? "scalar_ref" : "scalar";
    }

    private CppAccessorView BuildAccessor() => new CppAccessorView
    {
        FileExtension = _cppRecipe.BinaryTableFileExtension,

        Tables = _model.Tables.Select(table => new CppTableSlotView
        {
            Name = CppSnakeName(table.Name),
            TableName = TableName(table),

            // Unescaped: this one names the file the exporter wrote, not an identifier.
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
            })
            .Where(x => x.Fields.Count > 0 || x.RecordFields.Count > 0)
            .Select(x => new CppCrossReferenceView
            {
                Table = CppName(x.Table.Name),
                Fields = x.Fields.Select(sf => new CppReferenceFieldView
                {
                    Name = CppName(sf.Name),
                    RefTable = CppName(sf.FirstField!.ResolvedRefTable!.Name),
                    RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
                    Value = ReferenceValueExpression(sf, "target"),
                    RefDefault = RefDefault(sf),
                    IsArray = sf.IsArray,
                }).ToList(),
                RecordFields = x.RecordFields,
            })
            .ToList(),
    };

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// No resolution flag: a pointer says whether it resolved by being null, which is how this
    /// output already answers that for a reference outside a record. The loop bound says which
    /// of the three record shapes this is - the group's vector, the member's, or neither.
    /// spec/references-in-records.md.
    /// </remarks>
    private CppRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string name = CppName(wire.Group.Name);
        string member = string.Concat(wire.MemberPath.Select(part => "." + CppName(part)));
        var refTable = wire.TagCarrier.ResolvedRefTable;

        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        string path = !isArray || wire.Group.MembersAreArrays
            ? $"record.{name}{member}"
            : $"record.{name}[i]{member}";
        string subscript = (isArray && wire.Group.MembersAreArrays) ? "[i]" : "";

        return new CppRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "_index" + subscript,

            // Whichever vector holds the elements. Its own size rather than the column count,
            // because a trimming group's rows differ in how many they carry.
            Count = isArray
                ? (wire.Group.MembersAreArrays ? $"{path}_index.size()" : $"record.{name}.size()")
                : "",

            RefTable = CppName(refTable!.Name),
            RefLookup = PrimaryLookup(refTable),
        };
    }

    // ----------------------------------------------------------- rendering

    /// <summary>
    /// Whether a field's column reads through the cursor: every column whose element the
    /// encodings apply to, or promote from. The scalar elements that stay raw by spec keep
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

        // A reference reaches the cursor when the key it carries does. It used to be an
        // unconditional yes, which was the same int32 assumption in another place: a target
        // keyed by `uuid` has no cursor path any more than a `uuid` column does.
        // spec/reference-key-types.md.
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
    /// a column that reads the reader directly. A declaration rather than an assignment:
    /// unlike C#'s shared switch scope, every generated case body is a block of its own.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"tabbit::TcbColumnCursor cursor(reader, column, header.row_count, \"{tableName}.{wire.Name}\");"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
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

        // A reference runs on the key it carries. `next_same_i32` was the only answer while
        // a key could only be an int. spec/reference-key-types.md.
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
    /// The type the run's value is held in while it is spent over the rows.
    /// </summary>
    private static string RunValueType(WireColumn wire)
        => RunCall(wire) == "next_same_string" ? "std::string" : "std::int32_t";

    /// <summary>
    /// The line assigning one row from `value`, the run's decoded value, inside the loop
    /// the template builds around <see cref="RunCall"/>.
    /// </summary>
    private string RunSpend(WireColumn wire)
    {
        if (RunCall(wire).Length == 0)
            return "";

        string name = CppName(wire.Group.Name);
        string memberAccess = (wire.Member is null) ? "" : string.Concat(wire.MemberPath.Select(part => "." + CppName(part)));

        // Only the stored index is on the wire; the value is filled in once every table
        // is loaded, exactly as the per-row shape does it.
        //
        // A run is one value for many rows, which an array column has none of - so a record
        // member reaching this is a member of a record of one, and its key sits on the member
        // like every other member's does. spec/references-in-records.md.
        if (wire.IsRef)
        {
            return (wire.Member is null)
                ? $"records[i].{name}_index = value;"
                : $"records[i].{name}{memberAccess}_index = value;";
        }

        if (wire.ElementType == ValueType.Enum)
            return $"records[i].{name}{memberAccess} = static_cast<{ToCppTypeName(wire.TagCarrier)}>(value);";

        return $"records[i].{name}{memberAccess} = value;";
    }

    /// <summary>
    /// The read for a scalar field's row, which is one element's read at the row's own
    /// target - and, for a reference, at the index member, because only the stored index is
    /// on the wire.
    /// </summary>
    private string ScalarReadExpression(WireColumn wire, string target)
        => ReadElementExpression(wire, wire.IsRef ? target + "_index" : target);

    /// <summary>
    /// Where one element of a row's array lands.
    /// </summary>
    /// <remarks>
    /// Where the element number goes is the whole difference between the record shapes - an
    /// array of records indexes the group and then names the member, a record whose members are
    /// arrays names the member and then indexes it. spec/nested-multi-level.md.
    ///
    /// A reference lands in the key, whose name goes on the member and before the subscript:
    /// one key per element, exactly as there is one row per element.
    /// spec/references-in-records.md.
    /// </remarks>
    private static string ElementTarget(WireColumn wire, string name, string member, string element)
    {
        string path;
        string subscript;

        if (wire.Group.MembersAreAnonymous)
        {
            path = $"record.{name}[{wire.MemberAt}]";
            subscript = $"[{element}]";
        }
        else if (wire.Group.MembersAreArrays)
        {
            path = $"record.{name}{member}";
            subscript = $"[{element}]";
        }
        else
        {
            path = $"record.{name}[{element}]{member}";
            subscript = "";
        }

        return wire.IsRef
            ? (wire.Member is null
                ? $"record.{name}_index[{element}]"
                : path + "_index" + subscript)
            : path + subscript;
    }

    /// <summary>
    /// The call reading one value of a field's element type into
    /// <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// An array's elements read through the cursor by the same calls a scalar column's row
    /// does: what differs is only that the row's length comes from the cursor first.
    /// </remarks>
    private string ReadElementExpression(WireColumn wire, string target)
    {
        if (UsesCursor(wire))
        {
            if (wire.ElementType == ValueType.Enum)
                return $"{target} = static_cast<{ToCppTypeName(wire.TagCarrier)}>(cursor.next_i32())";

            // The key the target is addressed by, which the switch below already spells
            // for every type it can be.
            var read = wire.IsRef ? wire.RefKeyType : wire.ElementType;

            return read switch
            {
                ValueType.Int32 => $"{target} = cursor.next_i32()",
                ValueType.Int64 => $"{target} = cursor.next_i64()",
                ValueType.Double => $"{target} = cursor.next_f64()",
                ValueType.Float => $"{target} = cursor.next_f32()",
                ValueType.Bool => $"{target} = cursor.next_bool()",

                // Ticks, so the member is built from what the i64 column carried - the same
                // construction the direct read does, from the same number.
                ValueType.DateTime => $"{target} = tabbit::from_net_ticks(cursor.next_i64())",
                ValueType.TimeSpan => $"{target} = tabbit::TimeSpan(cursor.next_i64())",

                _ => $"{target} = cursor.next_string()",
            };
        }

        // Enum values are zig-zag encoded rather than fixed width, so they need
        // the dedicated overload.
        if (wire.ElementType == ValueType.Enum)
            return $"reader.read_enum({target})";

        if (wire.IsRef)
            return $"reader.read({target})";

        // The three promotable members read through the as-helpers, so a file written
        // before the column was widened still reads.
        return wire.ElementType switch
        {
            ValueType.Int32 => $"reader.read_i32_as(column.element, {target})",
            ValueType.Int64 => $"reader.read_i64_as(column.element, {target})",
            ValueType.Double => $"reader.read_f64_as(column.element, {target})",
            _ => $"reader.read({target})",
        };
    }

    /// <summary>
    /// The rendered check_column call: kind, count, and the elements this member accepts -
    /// its own plus the lossless promotions, decided here at generation time.
    /// </summary>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsVariableLengthArray
            ? "tabbit::kKindVarArray"
            : (wire.IsFixedArray ? "tabbit::kKindFixedArray" : "tabbit::kKindScalar");

        // -1 where one column owns the whole array: the file states how many elements it
        // holds and the read takes it from there, so there is no length here to hold it to.
        // A record member keeps its count - several columns fill one array and the number
        // they agree on is part of the generated shape, so a disagreement is a schema change
        // rather than data. spec/nullable-array-elements.md.
        bool ownsItsArray = wire.IsFixedArray && wire.Member is null;

        int count = wire.IsVariableLengthArray ? 0 : (ownsItsArray ? -1 : wire.Cells.Count);

        string accepted;

        if (wire.IsRef)
        {
            // The key the target is addressed by. `kElementI32` alone is what a reference
            // accepted while a key could only be an int, and the writer meanwhile emits the
            // key's own element - so the reader would refuse a file this build wrote. A
            // mismatch a compiler cannot see. spec/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                ValueType.String => "tabbit::kElementString",
                ValueType.Int64 => "tabbit::kElementI64, tabbit::kElementI32, tabbit::kElementVarint",
                ValueType.Uuid => "tabbit::kElementUuid",
                _ => "tabbit::kElementI32",
            };
        }
        else
        {
            switch (wire.ElementType)
            {
                case ValueType.Int32:
                    accepted = "tabbit::kElementI32, tabbit::kElementVarint"; break;
                case ValueType.Int64:
                    accepted = "tabbit::kElementI64, tabbit::kElementI32, tabbit::kElementVarint"; break;
                case ValueType.Double:
                    accepted = "tabbit::kElementF64, tabbit::kElementF32, tabbit::kElementI32"; break;
                case ValueType.Float: accepted = "tabbit::kElementF32"; break;
                case ValueType.Bool: accepted = "tabbit::kElementBool"; break;
                case ValueType.String: accepted = "tabbit::kElementString"; break;
                case ValueType.Uuid: accepted = "tabbit::kElementUuid"; break;
                case ValueType.Enum: accepted = "tabbit::kElementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be lossless
                // and semantically wrong, so no promotion.
                case ValueType.DateTime:
                case ValueType.TimeSpan:
                    accepted = "tabbit::kElementI64"; break;

                default:
                    throw new TabbitException($"The cpp generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability rides with kind and count, because it is the same kind of fact: a file
        // that says optional puts a presence bitmap in front of the block, and code not
        // expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument as the row one.
        string elements = wire.HasOptionalElements ? ", true" : "";

        return $"tabbit::check_column(column, \"{tableName}.{wire.Name}\", {kind}, {count}, "
            + $"{nullable}, {{{accepted}}}{elements});";
    }

    /// <summary>
    /// What a resolved reference yields: the record itself, or one of its fields
    /// when the reference names a field.
    /// </summary>
    private string ReferenceValueExpression(SerialField sf, string targetVariable)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return targetVariable;

        return $"{targetVariable}->{CppName(sf.FirstField!.ResolvedRefField!.Name)}";
    }

    /// <summary>
    /// The type a resolved reference is stored as: a pointer to the referenced
    /// record, or a copy of the referenced field's value.
    /// </summary>
    private string ResolvedRefTypeName(SerialField sf)
    {
        if (sf.ElementType == ValueType.ForeignRecord)
            return "const " + RecordName(sf.FirstField!.ResolvedRefTable) + "*";

        return ToCppTypeName(sf.FirstField);
    }

    private string RefDefault(SerialField sf)
        => sf.ElementType == ValueType.ForeignRecord ? "nullptr" : DefaultValueLiteral(sf.ElementType);

    // ------------------------------------------------------------- types

    /// <summary>
    /// A member or member-function name.
    ///
    /// snake_case, which is what makes the escape necessary here and nowhere else: every
    /// C++ keyword is lowercase, so `Int` becomes `int` and `Class` becomes `class`. The
    /// generator used to emit those verbatim - `std::string class;` - and report success,
    /// because nothing compiled the result.
    /// </summary>
    private string CppName(string name) => LanguageProfile.Cpp.MemberName(name.ToCase(_memberCase));

    /// <summary>
    /// The same spelling, for a name that is not a member - the accessor's slot per table.
    /// </summary>
    /// <remarks>
    /// snake_case because that is how C++ writes an identifier, not because a member is
    /// spelled that way. Sharing one function let the two look like one rule.
    /// </remarks>
    private static string CppSnakeName(string name) => LanguageProfile.Cpp.MemberName(name.ToSnakeCase());

    /// <summary>
    /// The C++ type a field's values have. A reference's is the key it carries rather than
    /// the record it presents - the member holding it is `<name>_index`, and what the file
    /// put there is the target's primary index. spec/reference-key-types.md.
    /// </summary>
    private string ToCppTypeName(Field? field)
        => ValueTypes.ElementOf(field!.ElementType) == ValueType.ForeignRecord
            ? ToCppTypeName(field!.RefKeyType, field.ResolvedRefTable?.PrimaryIndexField?.EnumOrNull)
            : ToCppTypeName(field!.ElementType, field!.EnumOrNull);

    private string ToCppTypeName(ValueType type, Models.Enum? enumm)
    {
        switch (ValueTypes.ElementOf(type))
        {
            // The two that name something from the model rather than the language.
            case ValueType.Enum:
                return enumm!.Name.ToPascalCase();

            // A reference is carried as the target row's primary index; the generated
            // read turns it into a pointer once every table is loaded.
            case ValueType.ForeignRecord:
                return "std::int32_t";

            default:
                return LanguageProfile.Cpp.ScalarTypeName(type);
        }
    }

    /// <summary>
    /// Initializer for a scalar member, so a default-constructed record holds
    /// defined values rather than whatever was on the stack.
    /// </summary>
    private string DefaultInitializer(Field? field)
    {
        switch (field!.ElementType)
        {
            // These default-construct themselves.
            case ValueType.String:
            case ValueType.DateTime:
            case ValueType.TimeSpan:
            case ValueType.Uuid:
                return "";

            case ValueType.Enum:
                return $" = static_cast<{ToCppTypeName(field)}>(0)";

            default:
                return $" = {DefaultValueLiteral(field!.ElementType)}";
        }
    }

    /// <summary>
    /// What a stored reference key is initialized to.
    /// </summary>
    /// <remarks>
    /// Nothing for the types that default-construct themselves: `tabbit::Uuid` is an
    /// aggregate and `= 0` has no conversion to it, which is a compile error rather than a
    /// wrong value. spec/reference-key-types.md.
    /// </remarks>
    private static string RefKeyInitializer(ValueType keyType)
        => keyType switch
        {
            ValueType.String => " = std::string()",
            ValueType.Uuid => "",
            _ => " = 0",
        };

    private string DefaultValueLiteral(ValueType type)
    {
        return ValueTypes.ElementOf(type) switch
        {
            ValueType.Bool => "false",
            ValueType.Float => "0.0f",
            ValueType.Double => "0.0",
            ValueType.String => "std::string()",
            _ => "0",
        };
    }

    private string RenderConstantValue(ConstantSet.Constant constant)
    {
        switch (constant.Type)
        {
            case ValueType.String:
                return $"\"{EscapeCppString((string)constant.Value!)}\"";

            case ValueType.Bool:
                return (bool)constant.Value! ? "true" : "false";

            case ValueType.Int32:
                return ((int)constant.Value!).ToString(CultureInfo.InvariantCulture);

            case ValueType.Int64:
                return ((long)constant.Value!).ToString(CultureInfo.InvariantCulture) + "LL";

            case ValueType.Float:
                return ((float)constant.Value!).ToString("R", CultureInfo.InvariantCulture) + "f";

            case ValueType.Double:
                return ((double)constant.Value!).ToString("R", CultureInfo.InvariantCulture);

            // A time_point and a duration, built from the tick counts the sheet holds.
            // from_net_ticks does the epoch shift, so a constant and a column read from
            // a file are the same value.
            case ValueType.DateTime:
                return "tabbit::from_net_ticks(" +
                       ((DateTime)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture) + "LL)";

            case ValueType.TimeSpan:
                return "tabbit::TimeSpan(" +
                       ((TimeSpan)constant.Value!).Ticks.ToString(CultureInfo.InvariantCulture) + "LL)";

            case ValueType.Uuid:
                return RenderUuidLiteral((Guid)constant.Value!);

            case ValueType.Enum:
            {
                var label = constant.Enum.GetLabel((int)constant.Value!, constant.Location);
                return $"{constant.Enum.Name.ToPascalCase()}::{label.Name.ToPascalCase()}";
            }

            default:
                throw new TabbitException(constant.Location,
                    $"Constant `{constant.Name}` has type `{constant.Type}`, which the C++ generator cannot render.");
        }
    }

    /// <summary>
    /// A Uuid as its raw bytes, in the order the reader expects.
    /// </summary>
    private string RenderUuidLiteral(Guid value)
    {
        var parts = value.ToByteArray().Select(b => "0x" + b.ToString("x2", CultureInfo.InvariantCulture));
        return $"tabbit::Uuid{{ {{ {string.Join(", ", parts)} }} }}";
    }

    private string EscapeCppString(string input)
    {
        var literal = new StringBuilder(input.Length + 2);

        foreach (var c in input)
        {
            switch (c)
            {
                case '"': literal.Append("\\\""); break;
                case '\\': literal.Append(@"\\"); break;
                case '\n': literal.Append(@"\n"); break;
                case '\r': literal.Append(@"\r"); break;
                case '\t': literal.Append(@"\t"); break;
                default:
                    if (c < 0x20)
                        literal.Append(@"\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                    else
                        literal.Append(c);  // Non-ASCII passes through; the file is UTF-8.
                    break;
            }
        }

        return literal.ToString();
    }

    // ----------------------------------------------------------- helpers

    private string RecordName(Table? table) => table!.Name.ToPascalCase() + "Record";

    private string TableName(Table table) => table.Name.ToPascalCase() + "Table";

    private static string IncludeGuard(string accessorName)
    {
        var guard = new StringBuilder("TABBIT_GENERATED_");

        foreach (var c in accessorName)
            guard.Append(char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');

        guard.Append("_H");
        return guard.ToString();
    }

    private IEnumerable<string> NamespaceParts()
        => _cppRecipe.Namespace.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries);
}
