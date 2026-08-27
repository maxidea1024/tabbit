using Tabbit.Recipe;
using Tabbit.Models;
using Tabbit.Targets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;
using Tabbit.Helpers;
using Tabbit.Extensions;

namespace Tabbit.CodeGeneration;

/// <summary>
/// C# source. Reads the binary export, and is Unity-compatible.
/// </summary>
public class CSharpRecipe : IOutputRecipe
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
    /// Whether to write the generated C# as sources or as one compiled assembly.
    /// </summary>
    /// <remarks>
    /// `"source"` by default, which is the folder of files a project includes.
    /// `"assembly"` writes a `.dll` instead - for a project that checks the output in and
    /// reads it rather than edits it, where a hundred generated files are noise in every
    /// diff and every search.
    ///
    /// The two are exclusive. Reading the code is what any IDE's decompiler does, and
    /// stepping into it works because the symbols are inside the assembly, so there is
    /// nothing the pair would give that one does not.
    ///
    /// **Unity still gets one source file.** The adapter names `UnityEngine`, which only
    /// the engine's own compiler resolves, so it is written beside the assembly either
    /// way - and so is the updater, for the same reason.
    /// </remarks>
    public string Output { get; set; } = "source";

    /// <summary>
    /// Name of the assembly, when <see cref="Output"/> asks for one.
    /// </summary>
    /// <remarks>
    /// Defaults to the namespace, or to the accessor's name when there is none, so the
    /// file is called after what a consumer types.
    /// </remarks>
    public string AssemblyName { get; set; } = "";

    /// <summary>
    /// Extension the generated reader expects on table files. Must match the
    /// binary export's FileExtension.
    /// </summary>
    public string BinaryTableFileExtension { get; set; } = ".tcb";

    /// <summary>
    /// Whether to write the data updater beside the reader.
    ///
    /// It fetches the manifest and the changed data files over HTTP and keeps a
    /// local copy current, so a build can take new data without shipping a new
    /// binary. Off by default: a project that ships its data inside the build has
    /// no use for it, and a file nobody calls is a file to explain.
    /// </summary>
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
/// Emits a single self-contained C# file per recipe entry, plus the binary reader.
///
/// The file's shape lives in templates/csharp.sbn. This file works out the values that
/// shape needs - type names, read calls, rendered literals - and nothing else.
///
/// The pieces that are the same in every output - the exception type, the collection
/// and ToString helpers, the reader delegates - are template partials. They used to be
/// verbatim string constants in a Snippets file, indented at run time by the printer's
/// scope, which is why they carried their own escaping for `$` and `"`.
/// </summary>
[TabbitTarget("csharp", TargetKind.CodeGeneration, Order = 20)]
public class CsCodeGenerator : CodeGenerator<CSharpRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private CSharpRecipe _csharpReceipe = null!;

    // Resolved once in `Run`, before anything is generated, so a misspelled setting is
    // reported on its own rather than as a verdict about one member.
    private NameCase _memberCase = NameCase.Pascal;

    /// <summary>
    /// A record group generates a struct and an array of it; a member column fills one of
    /// its fields.
    /// </summary>
    /// <remarks>
    /// The first of the thirteen, and the others followed the same split - declaration per
    /// field, reading per wire column. All thirteen take a record now.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level below is a struct declared beside the element type, and the read reaches it
    /// with a longer member path - `record._star[j].Position.X`. Neither the declaration nor
    /// the read counts the levels. spec/types/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// A `set` and a `map` in two layers: the arrays the file holds, in its order, and a
    /// `HashSet` and a `Dictionary` beside them for the lookups.
    /// </summary>
    /// <remarks>
    /// Both, because the file's order is the sheet's and nothing sorts it - a hash container
    /// alone would hand every language an order of its own, and the conformance driver would
    /// then get a different answer per language for the same file.
    /// spec/types/set-and-map.md section 7.
    /// </remarks>
    protected override bool SupportsContainers => true;

    /// <summary>
    /// An optional column becomes a `Has{Prop}` accessor beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `T?`. It has to work the same for `string` as for `int`, and a nullable
    /// reference type needs a nullable context this output does not have - it compiles as
    /// the C# 8 Unity 2020.3 accepts. spec/types/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>`FindByStageAndSlot(stageKey, slotKey)`. See <see cref="KeyPlans"/>.</summary>
    protected override bool SupportsCompositeKeys => true;

    /// <summary>
    /// `HasXAt(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/types/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, CSharpRecipe csharpRecipe)
    {
        // A blank path means the entry is inert - which is what every list in the
        // skeleton recipe holds. Without this, `Path.Combine("", "GameData.cs")` is a
        // relative path, so the accessor and the reader land in the working directory:
        // two files in the repository root that got committed before anyone noticed,
        // because the run succeeded.
        if (string.IsNullOrEmpty(csharpRecipe.Path))
            return;

        SweepStaleOutput(csharpRecipe.Path, csharpRecipe.Sweep);

        _csharpReceipe = csharpRecipe;
        _memberCase = MemberCasing.From(csharpRecipe.MemberCase, NameCase.Pascal, "csharp");

        // Already narrowed to the side this entry is built for. Both (the default)
        // leaves the model unchanged.
        _model = context.Model;

        if (WritesAssembly)
        {
            GenerateAssembly();
            return;
        }

        GenerateModel();
        WriteBinaryReaderRuntime();
    }

    /// <summary>Whether this entry asks for a compiled assembly rather than sources.</summary>
    private bool WritesAssembly
        => string.Equals(_csharpReceipe.Output, "assembly", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the assembly is called: the recipe's name, or what a consumer types.</summary>
    private string AssemblyName
        => !string.IsNullOrWhiteSpace(_csharpReceipe.AssemblyName) ? _csharpReceipe.AssemblyName
           : !string.IsNullOrWhiteSpace(_csharpReceipe.Namespace) ? _csharpReceipe.Namespace
           : AccessorType;

    /// <summary>
    /// The files the engine has to compile itself, so they stay beside the assembly as source.
    /// </summary>
    /// <remarks>
    /// Both name `UnityEngine`, which only Unity's own compiler resolves - and both are already
    /// written behind a symbol nothing else defines, so a plain .NET project compiles them to
    /// nothing.
    /// </remarks>
    private static readonly string[] EngineSources =
        ["TabbitUnityAdapter.cs", "TabbitUpdater.cs"];

    /// <summary>
    /// Writes the same output as <see cref="GenerateModel"/>, compiled.
    /// </summary>
    /// <remarks>
    /// The sources are generated into a folder of their own and compiled from there, so nothing a
    /// consumer sees is written twice. What lands is the assembly, its documentation, and the one
    /// or two files an engine has to compile - see <see cref="EngineSources"/>.
    /// </remarks>
    private void GenerateAssembly()
    {
        string staging = Path.Combine(
            Path.GetTempPath(), "tabbit-assembly", Guid.NewGuid().ToString("N"));

        string outputPath = _csharpReceipe.Path;

        bool staged = WritesWithoutStaging;

        try
        {
            _csharpReceipe.Path = staging;

            // Straight to disk for this half. The staging area exists so a failed run leaves the
            // output tree untouched, and these files are never part of that tree - they are read
            // back by the compiler and deleted. Going through it would mean they arrived only
            // after the run had already committed.
            WritesWithoutStaging = true;

            GenerateModel();
            WriteBinaryReaderRuntime();
        }
        finally
        {
            _csharpReceipe.Path = outputPath;
            WritesWithoutStaging = staged;
        }

        var (assembly, documentation) =
            CsAssemblyEmitter.Emit(staging, AssemblyName, EngineSources);

        Log.Information($"Generating codes for CSharp into `{Path.GetFullPath(outputPath)}` as an assembly");

        EmitBytes(Path.GetFullPath(Path.Combine(outputPath, AssemblyName + ".dll")), assembly);
        EmitBytes(Path.GetFullPath(Path.Combine(outputPath, AssemblyName + ".xml")), documentation);

        foreach (string name in EngineSources)
        {
            string written = Path.Combine(staging, "tabbit", name);

            if (File.Exists(written))
            {
                Emit(Path.GetFullPath(Path.Combine(outputPath, "tabbit", name)),
                     File.ReadAllText(written));
            }
        }

        Directory.Delete(staging, recursive: true);
    }

    /// <summary>
    /// Writes Tabbit's binary reader beside the generated accessor.
    ///
    /// Emitted rather than installed. The generated code used to reference a runtime
    /// that a Unity project had to carry as a plugin - 3,600 lines of read and write
    /// machinery, of which the generated code called four members - so a consumer had
    /// setup to do before the output would compile. The C++ and TypeScript outputs
    /// already ship their own reader; this brings C# in line.
    ///
    /// The source is an embedded resource taken from lib/cs, so there is one copy to
    /// maintain and it cannot drift from what is shipped.
    /// </summary>
    private void WriteBinaryReaderRuntime()
    {
        WriteBinaryReaderRuntime(
            "Tabbit.Runtime.Cs.TcbReader.cs",
            Path.Combine(_csharpReceipe.Path, "tabbit", "TabbitBinaryReader.cs"));

        // Asked for rather than assumed. It reaches the network and it is of no use to a
        // project that ships its data inside the build.
        if (_csharpReceipe.WriteUpdater)
        {
            WriteBinaryReaderRuntime(
                "Tabbit.Runtime.Cs.TabbitUpdater.cs",
                Path.Combine(_csharpReceipe.Path, "tabbit", "TabbitUpdater.cs"));
        }
    }

    /// <summary>
    /// Writes a file per table, per enum and per constant set, plus the accessor and the
    /// helpers.
    /// </summary>
    /// <remarks>
    /// It used to be one file holding all of it, which made a deleted table a hunk of dead
    /// code inside a file that still compiled - and a diff of a generated file show the
    /// helpers as changed every time a table was added, because they moved down the page.
    ///
    /// The layout is the one the TypeScript target has always had, because a consumer
    /// working in two languages should not have to learn two shapes.
    /// </remarks>
    /// <summary>The accessor type's name, in the casing this language's types use.</summary>
    private string AccessorType => _csharpReceipe.AccessorName.ToPascalCase();

    private void GenerateModel()
    {
        var view = BuildView();

        Log.Information($"Generating codes for CSharp into `{Path.GetFullPath(_csharpReceipe.Path)}`");

        Write(AccessorType + ".cs", "csharp-accessor.sbn", view);
        Write(Path.Combine("tabbit", "TabbitHelpers.cs"), "csharp-helpers.sbn",
              Part(usings: HelperUsings()));

        // The only file that knows what Unity is, and it is written whether or not the
        // consumer is Unity: outside the engine its body is behind a symbol nothing defines,
        // so it compiles to nothing. Written as source rather than folded into the accessor
        // because the engine's own compiler is what has to see those branches - and because
        // everything else this target writes stays plain netstandard as a result.
        Write(Path.Combine("tabbit", "TabbitUnityAdapter.cs"), "csharp-unity-adapter.sbn",
              Part(usings: UnityAdapterUsings()));

        foreach (var table in view.Tables)
        {
            Write(Path.Combine("tables", table.Name + "Table.cs"), "csharp-table.sbn",
                  Part(table: table, usings: TableUsings()));
        }

        // An enum declaration names no type outside itself, so it opens with nothing.
        foreach (var enumm in view.Enums)
            Write(Path.Combine("enums", enumm.Name + ".cs"), "csharp-enum.sbn", Part(enumm: enumm));

        // A struct is an entity beside a table and an enum, so it gets a file of its own and
        // every table that uses it refers to the one type. spec/types/polymorphism.md section 7.1.
        foreach (var structure in view.Structs)
        {
            Write(Path.Combine("structs", structure.Name + ".cs"), "csharp-struct.sbn",
                  Part(structure: structure, usings: TableUsings()));
        }

        foreach (var pair in _model.ConstantSets.Zip(view.ConstantSets, (model, rendered) => (model, rendered)))
        {
            Write(Path.Combine("constants", pair.rendered.Name + ".cs"), "csharp-constants.sbn",
                  Part(set: pair.rendered, usings: ConstantUsings(pair.model)));
        }
    }

    /// <summary>A view for one of the single-subject templates.</summary>
    private CsPartView Part(
        CsTableView? table = null, CsEnumView? enumm = null, CsConstantSetView? set = null,
        IReadOnlyList<string>? usings = null, CsPolymorphicTypeView? structure = null)
        => new CsPartView
        {
            Namespace = _csharpReceipe.Namespace,
            AccessorName = AccessorType,
            Usings = usings ?? NoUsings,
            Table = table,
            Enumm = enumm,
            Set = set,
            Structure = structure,
        };

    /// <summary>
    /// The abstract types the sheets used, as the templates read them.
    /// </summary>
    /// <remarks>
    /// The members are columns, so their types come out of the same conversion a table's
    /// members do - which is the point of the model handing over the columns rather than the
    /// declaration. spec/types/polymorphism.md section 7.1.
    /// </remarks>
    private IReadOnlyList<CsPolymorphicTypeView> BuildStructs()
        => _model.PolymorphicTypes
            .Select(declared => new CsPolymorphicTypeView
            {
                Name = declared.Name,
                BaseMembers = declared.BaseMembers.Select(StructMember).ToList(),
                Variants = declared.Variants
                    .Select(variant => new CsVariantView
                    {
                        TypeName = variant.Name,
                        Discriminator = variant.Discriminator,
                        Members = variant.Members.Select(StructMember).ToList(),
                    })
                    .ToList(),
            })
            .ToList();

    /// <summary>One member of an abstract type or of one of its variants.</summary>
    /// <remarks>
    /// **A reference member is two fields**, as a reference is anywhere: the declared name is
    /// the key's and the row it resolves to takes the derived one. Getting this wrong is not a
    /// naming problem - a builder that assigned the key into a field declared as a row does not
    /// compile. spec/references/reference-surface-naming.md sections 4 and 5.
    /// </remarks>
    private CsStructMemberView StructMember(Models.Field field)
    {
        string name = field.NamePath is { Count: > 1 }
            ? field.NamePath[^1].Name
            : field.Name.ToPascalCase();

        bool toRow = field.IsRef && field.ResolvedRefTable is not null && ResolvesToRow(field);

        return new CsStructMemberView
        {
            PropName = name,
            FieldType = ToCSharpTypeName(field),
            Comment = CommentLines(field.Comment),
            RowPropName = toRow
                ? RowAccessorName(field.ResolvedRefTable!.Name, name)
                : "",
            RefKeyTypeName = field.IsRef
                ? ToCSharpTypeName(field.RefKeyType, null, null)
                : "",
        };
    }

    /// <summary>
    /// The `using` lines one generated file needs.
    /// </summary>
    /// <remarks>
    /// Asked per file. Every file used to open with the same six namespaces plus the binary
    /// reader, which for an enum file meant seven lines declaring nothing it could reach - and
    /// for a reviewer, seven lines to check against a file that names one type.
    ///
    /// What each kind reaches, and nothing more:
    ///
    ///   enum        nothing. The declaration names no type outside itself.
    ///   constants   `System`, for the constants whose type is `Guid`, `DateTime` or
    ///               `TimeSpan`. Asked of the set rather than assumed.
    ///   table       the collections its lookups are built from, the reader it reads through,
    ///               `StringBuilder` for `ToString`, and `Task` for the read.
    ///   accessor    the same minus the reader's own types, plus `Path`.
    ///   helpers     the non-generic `IEnumerable` those two helpers compare through, and
    ///               `System` for the types `ToString` special-cases.
    ///
    /// `System.Array` and `System.Serializable` are written out in full where they appear, so
    /// `System` is not what carries them.
    /// </remarks>
    private static readonly string[] NoUsings = System.Array.Empty<string>();

    private static IReadOnlyList<string> TableUsings()
        => new[]
        {
            "using System;",
            "using System.Text;",
            "using System.Collections.Generic;",
            "using System.Threading.Tasks;",
            "",
            "// Tabbit's binary reader, written into this directory beside the accessor.",
            "// Nothing has to be installed for the generated code to compile.",
            "using Tabbit.Binary;",
        };

    private static IReadOnlyList<string> AccessorUsings()
        => new[]
        {
            "using System;",
            "using System.Collections.Generic;",
            "using System.IO;",
            "using System.Threading.Tasks;",
        };

    private static IReadOnlyList<string> HelperUsings()
        => new[]
        {
            "using System;",
            "using System.Text;",
            "using System.Collections;",
        };

    private static IReadOnlyList<string> UnityAdapterUsings()
        => new[]
        {
            "using System.IO;",
            "using System.Threading.Tasks;",
        };

    /// <summary>
    /// What a constant set reaches: `System`, and only when one of its constants is a type
    /// declared there.
    /// </summary>
    private static IReadOnlyList<string> ConstantUsings(ConstantSet set)
        => set.Constants.Any(constant => constant.Type is Models.ValueType.Uuid
                                            or Models.ValueType.DateTime
                                            or Models.ValueType.TimeSpan)
            ? new[] { "using System;" }
            : NoUsings;

    private void Write(string relative, string templateName, object view)
    {
        string filename = Path.GetFullPath(Path.Combine(_csharpReceipe.Path, relative));

        Emit(filename, Tidy(Outdent(TemplateEngine.Render(templateName, view))));
    }

    /// <summary>
    /// Drops the blank lines that mean nothing.
    /// </summary>
    /// <remarks>
    /// Two of them, and both were everywhere:
    ///
    ///   - a blank line in front of a closing brace, so every type ended with a gap and
    ///     every file ended with two - one before the type's brace and one before the
    ///     namespace's.
    ///   - two or more blank lines in a row.
    ///
    /// Here rather than in the templates because the templates are not where it came from.
    /// A tag written `{{~ end }}` rather than `{{~ end ~}}` keeps the newline after itself,
    /// and the difference is invisible while reading the template - so the same blank line
    /// arrived from six of them, and would arrive again from the seventh. What the output
    /// looks like is stated once, here, in the terms it is read in.
    ///
    /// Only the templates' output. The runtime sources this target copies in are written by
    /// hand and are not this function's to reformat.
    /// </remarks>
    private static string Tidy(string rendered)
    {
        var lines = rendered.Split('\n');
        var result = new List<string>(lines.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                // The last segment is what the trailing newline leaves behind rather than a
                // line of the file, and `TemplateEngine` has already settled the ending.
                if (i == lines.Length - 1)
                {
                    result.Add(lines[i]);
                    continue;
                }

                if (result.Count > 0 && result[^1].Length == 0)
                    continue;

                if (IsClosingBrace(lines[i + 1]))
                    continue;
            }

            result.Add(lines[i]);
        }

        return string.Join("\n", result);
    }

    /// <summary>Whether a line is a closing brace and nothing else a reader would miss.</summary>
    private static bool IsClosingBrace(string line)
    {
        string text = line.Trim();

        if (!text.StartsWith("}", StringComparison.Ordinal))
            return false;

        // `}` alone, and `} // namespace X` - the one the file ends with.
        text = text.Substring(1).TrimStart();

        return text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>
    /// Takes one level of indentation back off when there is no namespace to sit inside.
    ///
    /// The template's indentation is literal and written for the nested case, which is
    /// the normal one. The printer this replaced got its indentation from a scope stack,
    /// so it handled both without anything like this.
    /// </summary>
    private string Outdent(string rendered)
    {
        if (!string.IsNullOrEmpty(_csharpReceipe.Namespace))
            return rendered;

        var result = new StringBuilder(rendered.Length);

        foreach (var line in rendered.Split('\n'))
        {
            result.Append(line.StartsWith("    ", StringComparison.Ordinal) ? line.Substring(4) : line);
            result.Append('\n');
        }

        // Split on the final newline yields one empty segment, which the loop above has
        // already given a newline of its own, so the trailing blank line survives.
        return result.ToString(0, result.Length - 1);
    }

    // --------------------------------------------------------------- view

    private CsFileView BuildView()
    {
        var tables = _model.Tables.Select(BuildTable).ToList();

        return new CsFileView
        {
            Namespace = _csharpReceipe.Namespace ?? "",
            AccessorName = AccessorType,
            Usings = AccessorUsings(),
            FileExtension = _csharpReceipe.BinaryTableFileExtension,
            Tables = tables,
            TablesWithReferences = tables
                .Where(t => t.ReferenceFields.Count > 0
                            || t.RecordReferenceFields.Count > 0)
                .ToList(),
            Enums = _model.Enums.Select(BuildEnum).ToList(),
            Structs = BuildStructs(),
            ConstantSets = _model.ConstantSets.Select(BuildConstantSet).ToList(),
        };
    }

    private CsTableView BuildTable(Table table)
    {
        var fields = table.SerialFields.Select(sf => BuildField(table, sf)).ToList();
        var columns = table.WireColumns.Select(wire => BuildColumn(table, wire)).ToList();

        return new CsTableView
        {
            Name = table.Name.ToPascalCase(),
            DataFileName = table.DataFileName,
            RawName = table.Name,
            Comment = CommentLines(table.Comment),
            Fields = fields,
            Columns = columns,

            IndexedFields = table.SerialFields
                                 .Select((sf, i) => new { sf, view = fields[i] })
                                 .Where(x => x.sf.IsIndexer)
                                 .Select(x => x.view)
                                 .ToList(),

            CompositeKeys = CompositeKeys(table),
            Containers = ContainersOf(table),

            ReferenceFields = table.SerialFields
                                   .Select((sf, i) => new { sf, view = fields[i] })
                                   .Where(x => x.sf.IsRef)
                                   .Select(x => x.view)
                                   .ToList(),

            // A reference that is a member of a record resolves per element, so it needs a
            // loop of its own rather than a place in the list above. Read off the wire
            // columns, which is the same list the read path walks - the two have to agree
            // about where the key landed. spec/references/references-in-records.md.
            RecordReferenceFields = table.WireColumns
                                         .Where(wire => wire.Member is not null && wire.IsRef)
                                         .Select(BuildRecordReference)
                                         .ToList(),


            // One scratch int for the whole method rather than one per field: the reader
            // hands back an int and an enum field needs a cast through something. Scalar
            // enums read through the cursor and cast inline, so only arrays still need it.
            NeedsEnumTemp = table.SerialFields.Any(
                sf => sf.ElementType == Models.ValueType.Enum && sf.IsArray),

            // One cursor variable for the whole method: switch cases share a scope, so
            // each encodable column assigns it rather than declaring its own. Asked of the
            // columns, because that is what the switch has a case for.
            NeedsCursor = table.WireColumns.Any(UsesCursor),
            NeedsPresence = table.WireColumns.Any(c => c.IsNullable),
            NeedsElementPresence = table.WireColumns.Any(c => c.HasOptionalElements),

            // Pascal-casing a folded group's name gives the property it is exposed under
            // - `TextEn_array` becomes `TextEnArray` - so these literals name the very
            // members BuildObjectValueMap reads.
            FieldNameLiterals = string.Join(", ", table.SerialFields.Select(sf => $"\"{CsName(sf.Name)}\"")),
            FieldValueExpressions = string.Join(", ", table.SerialFields.Select(sf => "r." + CsName(sf.Name))),
        };
    }

    /// <summary>
    /// The lookup a reference is resolved through: the referenced table's primary index,
    /// which is the key a `foreign` column carries.
    /// </summary>
    /// <remarks>
    /// The name of that index is not fixed - only its type is - so this reads it off the
    /// table being pointed at. A non-reference field has no table to read, and the value
    /// is never rendered for one.
    /// </remarks>
    /// <summary>
    /// What follows a stored key to ask whether it points at anything.
    /// </summary>
    /// <remarks>
    /// Zero is the convention for "points at nothing" and it needs a spelling per key type -
    /// a string has no zero, and a uuid's is its empty value rather than a number. Written as
    /// the tail of a condition so the generated line names the member once.
    ///
    /// The numeric case is `> 0`, which is what the template used to say for every key.
    /// spec/references/reference-optionality.md · spec/references/reference-key-types.md.
    /// </remarks>
    private static string RefIsSetSuffix(Models.ValueType keyType)
        => keyType switch
        {
            Models.ValueType.String => "is { Length: > 0 }",
            Models.ValueType.Uuid => "!= System.Guid.Empty",
            _ => "> 0",
        };

    private static string PrimaryLookup(Table? refTable)
        => refTable is null
            ? ""
            : "GetBy" + refTable!.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase() + "OrThrow";

    /// <summary>
    /// The lookup that answers with null rather than throwing.
    /// </summary>
    /// <remarks>
    /// What a multi-target column resolves through. A key absent from one of its targets is
    /// the ordinary case - it means the row is in another of them - so the miss has to come
    /// back as an answer. spec/references/multi-target-accessors.md.
    /// </remarks>
    private static string PrimaryFind(Table table)
        => "FindBy" + table.SerialFields.First(sf => sf.IsIndexer).Name.ToPascalCase();


    /// <summary>
    /// One column of the file: how it is checked, how it is decoded, and where its values
    /// land.
    /// </summary>
    /// <remarks>
    /// The member it fills is the group's for a scalar column. A record group's member
    /// columns each fill one field of the generated element type, which is where these
    /// three differ from the group's - see spec/types/nested-fields.md.
    /// </remarks>
    private CsColumnView BuildColumn(Table table, WireColumn wire)
    {
        string fieldType = ToCSharpTypeName(wire.TagCarrier);
        string fieldName = "_" + wire.Group.Name.ToCamelCase();
        string refTable = wire.TagCarrier.RefTableName.ToPascalCase() ?? "";

        // A record's member column assigns one field of the element rather than the member
        // itself: `record._slot[j].Id` instead of `record._slot[j]`. Everything else about
        // reading it is the same, which is why this is a suffix and not a second path.
        //
        // The whole path, so a level further in costs nothing here - `.Position.X` reads the
        // same way `.Id` does, and the read switch never learns how deep it is.
        // spec/types/nested-multi-level.md.
        string memberAccess = (wire.Member is null)
            ? ""
            : string.Concat(wire.MemberPath.Select(name => "." + CsName(name)));

        // A member of an array-valued record loops over the elements without allocating:
        // the array is a struct array created with the record, so unlike a serial field
        // there is nothing to `new` per column - and doing so per member would throw away
        // whatever the previous member had written.
        //
        // `record_var` is the same loop with the length read from the row instead of a
        // constant, and there the first member does have to allocate, because no declaration
        // could have known how long this row's array is.
        string readKind = wire.Member switch
        {
            not null when wire.IsArray && wire.Group.MembersAreAnonymous
                => "array_of_arrays_member",

            // The member is the array rather than the record: one record, and each of its
            // members is as long as this row says. Read as `record_var` it allocated an
            // array of records over a field that is one.
            //
            // Two notations reach it. A group whose element number is on a level below it -
            // `Pos["M"][0]` - and a member whose own cell is delimited, which is what a
            // `set` and a `map` are: `Bag.Tags` typed `string[]` is one cell holding the
            // list. spec/types/set-and-map.md section 4.
            not null when wire.IsArray
                          && (wire.Group.MembersAreArrays || wire.LengthIsInTheCell)
                => "record_member_var",

            not null when wire.IsArray => "record_var",
            _ => ReadKind(wire),
        };

        return new CsColumnView
        {
            RowMemberAccess = (wire.Member is not null && wire.IsRef
                                   && ResolvesToRow(wire.TagCarrier))
                ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                    .Select(part => "." + CsName(part)))
                  + "." + CsName(RowAccessorName(
                        wire.TagCarrier.ResolvedRefTable!.Name, wire.MemberPath[^1]))
                : string.Concat(wire.MemberPath.Select(part => "." + CsName(part))),

            MemberKeyType = wire.IsRef
                ? ToCSharpTypeName(wire.RefKeyType, null, null)
                : "",

            WireTag = wire.TagCarrier.WireTag!.Value,
            ColumnCheck = ColumnCheck(wire, table.Name.ToPascalCase()),
            ReadKind = readKind,
            IsFirstMember = wire.IsFirstMember,
            IsNullable = wire.IsNullable,
            HasOptionalElements = wire.HasOptionalElements,
            MemberAt = wire.MemberAt,
            PresenceField = PresenceField(wire.Group),
            ElementPresenceField = ElementPresenceField(wire.Group),
            EmptyValue = EmptyValue(wire, fieldType),
            RecordTypeName = wire.Group.Name.ToPascalCase() + "Entry",
            RecordNeedsInit = wire.Group.IsRecord && RecordNeedsFactory(wire.Group),
            CursorOpen = CursorOpen(wire, table.Name.ToPascalCase()),
            MemberAccess = memberAccess,
            ElementRead = ElementReadLines(wire, fieldName, fieldType, refTable, memberAccess),
            ParallelArrays = ParallelArrayLines(wire, fieldName, refTable),
            LengthRead = UsesCursor(wire)
                ? "elementCount = cursor.NextLength();"
                : "reader.TryReadCounter32(out elementCount);",
            RunCall = RunCall(wire),
            RunRead = RunReadLines(wire, fieldName, fieldType, refTable, memberAccess),
            FieldName = fieldName,
            FieldType = fieldType,
            PropName = CsName(wire.Group.Name),
            PascalName = wire.Group.Name.ToPascalCase(),
        };
    }

    /// <summary>
    /// The keys made of several columns, each with the lookup it generates.
    /// </summary>
    /// <remarks>
    /// Beside <c>IndexedFields</c> rather than folded into it: a single key publishes its
    /// dictionary and a composite one does not, and a table that declares none generates what
    /// it generated before this notation existed. See <see cref="CompositeKeyView"/>.
    /// </remarks>
    private IReadOnlyList<CompositeKeyView> CompositeKeys(Table table)
        => KeyPlans.Of(table).Where(plan => plan.IsComposite).Select(plan =>
        {
            string suffix = plan.Suffix(name => name.ToPascalCase(), "And");

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
                    Param = KeyComponentView.ParamOf(component.Name).ToCamelCase(),
                    Type = ToCSharpTypeName(keyType, keyEnum, null),
                    Member = CsName(component.Name),
                    Kind = KeyComponentView.KindOf(keyType),
                };
            }).ToList();

            return new CompositeKeyView
            {
                Suffix = suffix,
                MapName = "_recordsBy" + suffix,
                FieldName = plan.Suffix(name => name.ToPascalCase(), " and "),
                Components = components,

                Params = string.Join(", ", components.Select(c => c.Type + " " + c.Param)),

                Argument = "KeyOf" + suffix + "("
                           + string.Join(", ", components.Select(c => c.Param)) + ")",

                ValueFormat = "(" + string.Join(
                    ", ", components.Select(c => "{" + c.Param + "}")) + ")",
            };
        }).ToList();

    private CsFieldView BuildField(Table table, SerialField sf)
    {
        if (sf.IsRecord)
            return BuildRecordField(table, sf);

        string fieldType = ToCSharpTypeName(sf.FirstField);
        string fieldName = "_" + sf.Name.ToCamelCase();
        string refTable = sf.FirstField!.RefTableName.ToPascalCase() ?? "";

        return new CsFieldView
        {
            IsRecord = false,
            IsNullable = sf.RowMayBeAbsent,
            HasOptionalElements = sf.ElementMayBeAbsent,
            PresenceField = PresenceField(sf),
            ElementPresenceField = ElementPresenceField(sf),
            RecordTypeName = "",
            Members = Array.Empty<CsRecordMemberView>(),
            NeedsElementInit = false,
            Comment = CommentLines(sf.FirstField!.Comment),
            PropName = CsName(sf.Name),

            // Only a reference to a whole row: the column's name is the key's and this is
            // what the row takes.
            RowPropName = sf.IsRef && refTable.Length > 0
                           && sf.FirstField!.ResolvedRefField is null
                ? CsName(RowAccessorName(refTable, sf.Name))
                : "",

            PascalName = sf.Name.ToPascalCase(),
            FieldName = fieldName,
            FieldType = fieldType,
            Initializer = Initializer(sf),
            ElementCount = sf.Fields.Count,
            RefTable = refTable,
            RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
            RefField = CsName(sf.FirstField!.RefFieldName ?? ""),
            RefKeyTypeName = ToCSharpTypeName(sf.FirstField!.RefKeyType, null, null),
            IndexKeyType = IndexKeyType(sf),
            RefIsSet = RefIsSetSuffix(sf.FirstField!.RefKeyType),
            Kind = DeclarationKind(table, sf),

            // A reference to a whole row is assigned the target record; one that names a
            // field is assigned that field's value, which is the field's own type.
            ReferenceSetterType = sf.ElementType == Models.ValueType.ForeignRecord
                ? refTable + "Table.Record"
                : fieldType,

            ReferencesField = !string.IsNullOrEmpty(sf.FirstField!.RefFieldName),
        };
    }

    /// <summary>
    /// A record group: the element type to declare, and the member holding one or an array
    /// of them.
    /// </summary>
    /// <remarks>
    /// The element type is a struct. Two reasons, both about the read path: an array of
    /// structs needs no per-element allocation, so a table of twenty thousand rows with a
    /// four-element record does not make eighty thousand objects; and `array[j].Member = x`
    /// is a legal assignment on a struct array, which is exactly what each member column's
    /// read does.
    ///
    /// No reference members - the model refuses those - so nothing here has the index
    /// arrays and setters a reference would need.
    /// </remarks>
    private CsFieldView BuildRecordField(Table table, SerialField sf)
    {
        // Trimmed, so the length is per row: no `_N` constant to declare and nothing to
        // allocate at declaration time. The read path creates the array once it has read how
        // long this row's is.
        bool perRowLength = sf.IsArray && table.TrimTrailingArrayElements;

        // Innermost first, so a struct is declared before the one that holds it - and so the
        // outermost, which every existing path reads, is last.
        var recordTypes = new List<CsRecordTypeView>();
        var members = BuildRecordMembers(sf.Members, sf.Name.ToPascalCase(), recordTypes);

        recordTypes.Add(new CsRecordTypeView
        {
            TypeName = sf.Name.ToPascalCase() + "Entry",
            Members = members,
            // And the slot a member reaching several tables holds per element, which a struct
            // cannot size at its declaration either. spec/references/multi-target-accessors.md.
            NeedsInit = members.Any(m => m.Initializer.Length > 0),
            IsOutermost = true,
        });

        // Which abstract type this group is, if it is one. The variants and their members are
        // the shared declaration's - one per declaration however many tables named it - so this
        // looks them up rather than working them out again from the columns.
        // spec/types/polymorphism.md section 7.1.
        var discriminator = sf.Members.FirstOrDefault(
            member => member.IsLeaf && member.FirstField is { IsDiscriminator: true });

        var declaredType = discriminator?.FirstField?.AbstractTypeName is { } abstractName
            ? _model.PolymorphicTypes.FirstOrDefault(
                candidate => candidate.Name == abstractName.ToPascalCase())
            : null;

        return new CsFieldView
        {
            IsRecord = true,

            // A record group has no presence of its own: absence inside one is the array's
            // length, not a bit per member. WireColumn.Of says the same about the wire.
            IsNullable = false,
            PresenceField = "",
            RecordTypeName = sf.Name.ToPascalCase() + "Entry",
            Members = members,
            RecordTypes = recordTypes,

            AbstractTypeName = declaredType?.Name ?? "",
            BaseMembers = (declaredType?.BaseMembers ?? [])
                .Select(StructMember)
                .ToList(),
            Variants = (declaredType?.Variants ?? [])
                .Select(variant => new CsVariantView
                {
                    TypeName = variant.Name,
                    Discriminator = variant.Discriminator,
                    Members = variant.Members.Select(StructMember).ToList(),
                })
                .ToList(),
            MembersAreArrays = sf.MembersAreArrays,
            MembersAreAnonymous = sf.MembersAreAnonymous,
            OuterCount = sf.Members.Count,
            ElementTypeName = sf.MembersAreAnonymous
                ? ToCSharpTypeName(sf.Members[0].FirstField)
                : "",
            ElementInitializer = sf.MembersAreAnonymous
                                 && sf.Members[0].ElementType == Models.ValueType.String
                ? " = \"\""
                : "",
            // The same question the read asks through `RecordNeedsInit`: a record array is
            // allocated by the read now, and if it needs a factory there it has to be
            // declared here. Asking two different things produced a call to a method that
            // was never emitted. spec/types/nullable-array-elements.md.
            NeedsElementInit = members.Any(m => m.Initializer.Length > 0)
                || (sf.IsRecord && RecordNeedsFactory(sf)),

            // The group's own comment is the first member's column comment - a record has
            // no header cell of its own, so that is the nearest thing the sheet said.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),
            PropName = CsName(sf.Name),
            PascalName = sf.Name.ToPascalCase(),
            FieldName = "_" + sf.Name.ToCamelCase(),

            // An array of arrays has no element type to name, so the inner array is the
            // type - see spec/types/nested-multi-level.md.
            FieldType = sf.MembersAreAnonymous
                ? ToCSharpTypeName(sf.Members[0].FirstField) + "[]"
                : sf.Name.ToPascalCase() + "Entry",
            Initializer = "",
            ElementCount = sf.RecordElementCount,
            Kind = sf.MembersAreAnonymous
                ? "array_of_arrays"
                : sf.IsArray ? "record_var_array" : "record",

            // None of these apply to a record. A reference belongs to a member and members
            // cannot be references yet, so there is nothing to resolve.
            RefTable = "",
            RefLookup = "",
            RefField = "",
            RefKeyTypeName = "",
            RefIsSet = "",
            ReferenceSetterType = "",
            ReferencesField = false,
        };
    }


    private CsRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string fieldName = "_" + wire.Group.Name.ToCamelCase();
        string memberAccess = string.Concat(wire.MemberPath.Select(name => "." + CsName(name)));

        var (path, subscript) = MemberPlace(wire, fieldName, memberAccess);

        // The member's own name is the key's; the row takes the derived one.
        // spec/references/reference-surface-naming.md sections 4, 5 and 9.
        bool toRow = ResolvesToRow(wire.TagCarrier);

        string rowAccess = toRow
            ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                .Select(part => "." + CsName(part)))
              + "." + CsName(RowAccessorName(
                    wire.TagCarrier.ResolvedRefTable!.Name, wire.MemberPath[^1]))
            : memberAccess;

        var (rowPath, rowSubscript) = MemberPlace(wire, fieldName, rowAccess);

        return new CsRecordReferenceView
        {
            Access = rowPath + rowSubscript,
            Key = toRow ? path + subscript : path + "_index" + subscript,
            Flag = path + "_F" + subscript,

            // The member's own array when the number is on the member, the group's when it
            // is on the group, and nothing to walk when the group is one record. `Length`
            // rather than the column count, because a trimming group's rows differ in how
            // many they carry.
            Count = subscript.Length > 0
                ? path + ".Length"
                : wire.IsArray
                    ? $"record.{fieldName}.Length"
                    : "",

            RefTable = wire.TagCarrier.RefTableName.ToPascalCase() ?? "",
            RefLookup = PrimaryLookup(wire.TagCarrier.ResolvedRefTable),
            RefIsSet = RefIsSetSuffix(wire.RefKeyType),
        };
    }

    /// <summary>
    /// Whether a record group's element type gets a factory, which it does when some member
    /// needs setting past C#'s own default.
    /// </summary>
    /// <remarks>
    /// Asked of the model rather than of the built view, because the read path needs the same
    /// answer the declaration reached and the two are built in different places. The three
    /// causes are a string, an array, and a member that is itself a record - the last because
    /// its own factory has to be called for it to reach its defaults at all.
    /// </remarks>
    private static bool RecordNeedsFactory(SerialField group)
        => group.Members.Any(member => !member.IsLeaf)
           || group.Members.Any(member => member.IsArray)
           || group.Leaves.Any(leaf => leaf.ElementType == Models.ValueType.String);

    /// <summary>
    /// Members of one level of a record, declaring a struct for each member that is itself a
    /// record.
    /// </summary>
    /// <remarks>
    /// The recursion is here rather than in the template, and <paramref name="declared"/> is
    /// where the structs it produces are collected - innermost first, so each is declared
    /// before the one naming it.
    ///
    /// A member that is a record gets the nested struct as its type and that struct's factory
    /// as its initializer. Everything else about a member is what it always was, which is
    /// what makes depth cost nothing here: `Position` is declared exactly as `Id` is, with a
    /// different type name. spec/types/nested-multi-level.md.
    /// </remarks>
    /// <summary>
    /// Every container in a table, with what reaches it from a record.
    /// </summary>
    /// <remarks>
    /// **Built after every column is in, beside the table's own index maps.** A map's
    /// dictionary needs its key column and how long it is, and the columns arrive one at a
    /// time - so nothing on the way through can build one. Eagerly rather than on first use,
    /// because a struct has nowhere to cache a lazy answer and the array it reads is already
    /// there. spec/types/set-and-map.md section 7.3.
    /// </remarks>
    private List<CsContainerView> ContainersOf(Table table)
    {
        var result = new List<CsContainerView>();

        foreach (var group in table.SerialFields.Where(group => group.IsRecord))
        {
            string root = "._" + group.Name.ToCamelCase();

            foreach (var member in group.Members)
                Collect(member, root);
        }

        return result;

        void Collect(RecordMember member, string path)
        {
            string here = path + "." + CsName(member.Name);

            if (member.Container == Models.ContainerKind.Set)
            {
                result.Add(new CsContainerView
                {
                    IsMap = false,
                    Access = path,
                    LookupField = "_" + member.Name.ToCamelCase() + "Set",
                    SourceField = CsName(member.Name),
                    LookupType = $"HashSet<{ToCSharpTypeName(member.FirstField)}>",
                });
            }

            if (member.Container == Models.ContainerKind.Map
                && member.Members.Find(below => below.Name == Models.ContainerMembers.Key) is { } key)
            {
                result.Add(new CsContainerView
                {
                    IsMap = true,
                    Access = here,
                    LookupField = "_at",
                    SourceField = Models.ContainerMembers.Key,
                    LookupType = $"Dictionary<{ToCSharpTypeName(key.FirstField)}, int>",
                });
            }

            foreach (var below in member.Members)
                Collect(below, here);
        }
    }

    private List<CsRecordMemberView> BuildRecordMembers(
        List<RecordMember> members, string prefix, List<CsRecordTypeView> declared)
    {
        var result = new List<CsRecordMemberView>();

        for (int at = 0; at < members.Count; at++)
        {
            var member = members[at];

            if (member.IsLeaf)
            {
                result.Add(new CsRecordMemberView
                {
                    Comment = CommentLines(member.FirstField!.Comment),
                    RowPropName = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                                   && ResolvesToRow(member.FirstField!)
                        ? CsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                        : "",

                    PropName = CsName(member.Name),
                    PascalName = member.Name.ToPascalCase(),
                    FieldType = ToCSharpTypeName(member.FirstField) + (member.HoldsList ? "[]" : ""),

                    // An array member allocates; a string one starts empty. Both for the same
                    // reason: a file predating the column leaves nothing to write it, and null
                    // one field later is a crash rather than a missing value.
                    //
                    // A member whose own cell is the list starts empty rather than sized: no
                    // declaration knows how long a row's list is, and the read makes the
                    // array it fills. spec/types/set-and-map.md section 4.
                    Initializer = member.ListIsInTheCell
                        ? $" = System.Array.Empty<{ToCSharpTypeName(member.FirstField)}>()"
                        : member.IsArray
                            ? $" = new {ToCSharpTypeName(member.FirstField)}[{member.Fields.Count}]"
                            : member.ElementType == Models.ValueType.String ? " = \"\"" : "",
                    IsFirst = at == 0,
                    IsArray = member.HoldsList,
                    ElementInitializer = member.IsArray && member.ElementType == Models.ValueType.String
                        ? " = \"\""
                        : "",

                    // A set is the array beside a lookup into it. Both, because the array is
                    // the file's order and sorting is not this tool's to do - the set alone
                    // would give each language an order of its own.
                    // spec/types/set-and-map.md section 7.1.
                    IsSet = member.Container == Models.ContainerKind.Set,
                    SetElementType = member.Container == Models.ContainerKind.Set
                        ? ToCSharpTypeName(member.FirstField)
                        : "",
                    ContainsMethod = member.Container == Models.ContainerKind.Set
                        ? "Contains" + member.Name.ToPascalCase()
                        : "",

                    // A reference member carries the key and the resolution flag beside the
                    // row it resolved to, all three inside the element - and all three at
                    // the member's own arity, because a record of arrays holds one key per
                    // element just as it holds one row per element.
                    // spec/references/references-in-records.md.
                    RefKeyTypeName = member.IsRef
                        ? ToCSharpTypeName(member.FirstField!.RefKeyType, null, null)
                          + (member.IsArray ? "[]" : "")
                        : "",
                    RefFlagTypeName = member.IsRef ? (member.IsArray ? "bool[]" : "bool") : "",
                    RefKeyInitializer = member.IsRef && member.IsArray
                        ? $" = new {ToCSharpTypeName(member.FirstField!.RefKeyType, null, null)}"
                          + $"[{member.Fields.Count}]"
                        : "",
                    RefFlagInitializer = member.IsRef && member.IsArray
                        ? $" = new bool[{member.Fields.Count}]"
                        : "",
                    RefTable = member.IsRef ? member.FirstField!.RefTableName.ToPascalCase() ?? "" : "",
                    RefLookup = member.IsRef ? PrimaryLookup(member.FirstField!.ResolvedRefTable) : "",
                    RefIsSet = member.IsRef ? RefIsSetSuffix(member.FirstField!.RefKeyType) : "",
                });

                continue;
            }

            // A level below. Its type name carries the path so two records holding a
            // `Position` do not name one struct twice, and `Entry` is what keeps it off the
            // property's own name - C# does not allow a nested type and a member to share one.
            string typeName = prefix + member.Name.ToPascalCase() + "Entry";
            var nested = BuildRecordMembers(member.Members, prefix + member.Name.ToPascalCase(), declared);
            bool needsInit = nested.Any(m => m.Initializer.Length > 0);

            // A map's own struct: the key column, whatever the entries hold, and the
            // dictionary that turns a key into a position among them.
            var key = member.Container == Models.ContainerKind.Map
                ? member.Members.Find(below => below.Name == Models.ContainerMembers.Key)
                : null;

            var held = member.Container == Models.ContainerKind.Map
                ? member.Members.Find(below => below.Name == Models.ContainerMembers.Value)
                : null;

            declared.Add(new CsRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                NeedsInit = needsInit,
                IsOutermost = false,
                IsMap = key is not null,
                MapKeyType = key is null ? "" : ToCSharpTypeName(key.FirstField),

                // Only where the value is one column. A struct value is a member per column
                // and has no single object a lookup could hand back, so there the position
                // is the answer and `Value.ItemId[at]` is how the entry is read.
                MapValueType = held is { IsLeaf: true } ? ToCSharpTypeName(held.FirstField) : "",
            });

            result.Add(new CsRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                RowPropName = member.IsRef && member.FirstField!.ResolvedRefTable is not null
                               && ResolvesToRow(member.FirstField!)
                    ? CsName(RowAccessorName(member.FirstField!.ResolvedRefTable!.Name, member.Name))
                    : "",

                PropName = CsName(member.Name),
                PascalName = member.Name.ToPascalCase(),
                FieldType = typeName,

                // Only when the level below has something to set. A struct of plain numbers
                // zero-initializes to a usable value, and calling a factory that does nothing
                // would be a line of generated code that means nothing.
                Initializer = needsInit ? $" = New{typeName}()" : "",
                IsFirst = at == 0,
                IsArray = false,
                ElementInitializer = "",
                IsRecord = true,

                // A level below is a record, never a reference: the reference belongs to a
                // leaf and this member is the struct holding them.
                RefKeyTypeName = "",
                RefFlagTypeName = "",
                RefKeyInitializer = "",
                RefFlagInitializer = "",
                RefTable = "",
                RefLookup = "",
                RefIsSet = "",
            });
        }

        return result;
    }

    /// <summary>
    /// Which member-declaration shape a field takes.
    ///
    /// A variable-length array declares no `_N` constant: its length differs per row, so
    /// there is no element count to expose, and the array is allocated by the read path
    /// once it knows how long this row's is.
    /// </summary>
    /// <summary>
    /// What a member is initialized to, as the text that follows its declaration -
    /// nothing at all where C#'s own default is already an empty value.
    /// </summary>
    /// <remarks>
    /// A column the file does not carry leaves its member at its default, and that is
    /// not a hypothetical: delete a column and every build made before the deletion
    /// reads files with nothing for it. Every type here zero-initializes to something
    /// usable except a string, which starts null - and a null string is a crash one
    /// field later rather than an empty one.
    ///
    /// A reference is left alone: the absence of a referenced row is what null means
    /// here, and there is nothing to put in its place.
    /// </remarks>
    private static string Initializer(SerialField sf)
        => !sf.IsRef && sf.ElementType == Models.ValueType.String ? " = \"\"" : "";

    private static string DeclarationKind(Table table, SerialField sf)
    {
        if (sf.IsArray)
        {
            if (sf.IsRef)
                return "array_ref";

            // One array declaration since v107. Trimming decides how many elements a row
            // carries, not whether the length is known at generation time - the file states
            // it either way. spec/wire/tcb-v107-dynamic-arrays.md.
            return "var_array";
        }

        return sf.IsRef ? "scalar_ref" : "scalar";
    }

    /// <summary>
    /// The backing field a nullable column's presence lands in.
    /// </summary>
    /// <remarks>
    /// One per group rather than one per sheet column: a group is one value to whoever
    /// reads it, and the model has already required its columns to agree about being
    /// optional.
    /// </remarks>
    /// <summary>The field holding which of an array's elements the sheet gave a value.</summary>
    /// <remarks>
    /// A `bool` per element rather than the bitmap the file carries: the row-level answer is
    /// a `bool` per row for the same reason, and a consumer asking `HasCostsAt(2)` should not
    /// pay for a shift and a mask it did not ask for. spec/types/nullable-array-elements.md.
    /// </remarks>
    private static string ElementPresenceField(SerialField sf)
        => "_" + sf.Name.ToCamelCase() + "HasValueAt";

    private static string PresenceField(SerialField sf)
        => "_" + sf.Name.ToCamelCase() + "HasValue";

    /// <summary>
    /// What an absent row's member is set back to, so the binary path lands where the JSON
    /// one does.
    /// </summary>
    /// <remarks>
    /// The member's own type, which for an array is not its element's. `default(int)`
    /// assigned to an `int[]` is the file not compiling, and nothing said so: the `optional`
    /// fixture's C# had no gate that built it, so the golden recorded a page that was never
    /// a program. <see cref="CsNestedAndOptionalTests"/> is that gate.
    ///
    /// An empty array rather than a zeroed one of the declared length, because that is what
    /// the declaration already starts every array member at - including a fixed one, whose
    /// length is restored by the read and not by the declaration.
    /// </remarks>
    private static string EmptyValue(WireColumn wire, string fieldType)
    {
        if (wire.IsArray)
            return $"System.Array.Empty<{fieldType}>()";

        return wire.ElementType == Models.ValueType.String && !wire.IsRef
            ? "\"\""
            : $"default({fieldType})";
    }

    /// <summary>
    /// The arrays a reference column fills beside its values, for the read to allocate.
    /// </summary>
    /// <remarks>
    /// Only where one column owns the array: a member of a record group keeps its key and its
    /// flag inside the element, which the record allocates. spec/references/references-in-records.md.
    ///
    /// **Both array kinds, from their own length.** A folded group's length is the column count
    /// the file states; a trimmed one's is the row's, read a line earlier. This asked only for
    /// the fixed kind, so a trimmed array of references allocated its values and its presence
    /// bitmap per row and left the key array at `Array.Empty` - and the first element written
    /// into it was an index out of range. `foreign[]` is refused, so the only way to that shape
    /// is a folded group with trimming turned on for the entry, and no fixture held one.
    /// spec/types/variable-length-record-arrays.md.
    /// </remarks>
    private IReadOnlyList<string> ParallelArrayLines(
        WireColumn wire, string fieldName, string refTable)
    {
        if (!wire.IsRef || wire.Member is not null)
            return Array.Empty<string>();

        if (!wire.IsArray)
            return Array.Empty<string>();

        const string length = "elementCount";

        string keyType = ToCSharpTypeName(wire.RefKeyType, null, null);

        return new[]
        {
            $"record.{fieldName}_{refTable}_index = new {keyType}[{length}];",
            $"record.{fieldName}_F = new bool[{length}];",
        };
    }

    private static string ReadKind(WireColumn wire)
        => wire.IsArray ? "var_array" : "scalar";

    /// <summary>
    /// The lines that read one element, whether the template places them in a loop or
    /// straight into the method body.
    /// </summary>
    /// <summary>
    /// The rendered CheckColumn call for one field: its kind, its count, and every wire
    /// element this member reads - its own plus the lossless promotions.
    /// </summary>
    /// <remarks>
    /// The accepted list is decided here, at generation time, so the runtime carries no
    /// table of what-converts-to-what: an int member says it takes i32 and varint, a
    /// double member says f64, f32 and i32, and everything else is exact. Anything not
    /// listed is refused by name before a byte of the block is read.
    /// </remarks>
    private static string ColumnCheck(WireColumn wire, string tableName)
    {
        string kind = wire.IsArray ? "TcbTable.KindArray" : "TcbTable.KindScalar";

        string accepted;

        if (wire.IsRef)
        {
            // The key the target is addressed by. `ElementI32` alone is what a reference
            // accepted while a key could only be an int - and the writer had meanwhile
            // learned to emit the key's own element, so the reader would have refused a file
            // this build wrote. A mismatch a compiler cannot see.
            // spec/references/reference-key-types.md.
            accepted = wire.RefKeyType switch
            {
                Models.ValueType.String => "TcbTable.ElementString",
                Models.ValueType.Int64 => "TcbTable.ElementI64, TcbTable.ElementI32, TcbTable.ElementVarint",
                Models.ValueType.Uuid => "TcbTable.ElementUuid",
                _ => "TcbTable.ElementI32",
            };
        }
        else
        {
            switch (wire.ElementType)
            {
                case Models.ValueType.Int32:
                    accepted = "TcbTable.ElementI32, TcbTable.ElementVarint";
                    break;
                case Models.ValueType.Int64:
                    accepted = "TcbTable.ElementI64, TcbTable.ElementI32, TcbTable.ElementVarint";
                    break;
                case Models.ValueType.Double:
                    accepted = "TcbTable.ElementF64, TcbTable.ElementF32, TcbTable.ElementI32";
                    break;
                case Models.ValueType.Float: accepted = "TcbTable.ElementF32"; break;
                case Models.ValueType.Bool: accepted = "TcbTable.ElementBool"; break;
                case Models.ValueType.String: accepted = "TcbTable.ElementString"; break;
                case Models.ValueType.Uuid: accepted = "TcbTable.ElementUuid"; break;
                case Models.ValueType.Enum: accepted = "TcbTable.ElementVarint"; break;

                // Ticks are exact i64: reading an int as a datetime would be numerically
                // lossless and semantically wrong, so no promotion.
                case Models.ValueType.DateTime:
                case Models.ValueType.TimeSpan:
                    accepted = "TcbTable.ElementI64";
                    break;

                default:
                    throw new TabbitDefectException($"The csharp generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument. Passed by name because it comes after
        // the accepted elements, of which there may be one, two or three.
        string elements = wire.HasOptionalElements ? ", elementNullable: true" : "";

        return $"TcbTable.CheckColumn(column, \"{tableName}.{wire.Name}\", {kind}, "
            + $"{nullable}, {accepted}{elements});";
    }

    /// <summary>
    /// The lines that read one element into a record, inside the columnar fill loop.
    ///
    /// `record` is the row being filled and `column` the descriptor in scope; an array
    /// element is at `[j]`, the template's inner loop variable.
    /// </summary>
    /// <summary>
    /// Whether a field's column reads through the cursor: every scalar column whose
    /// element the encodings apply to, or promote from. Arrays are always raw and keep
    /// reading the reader directly, as do the scalar elements that stay raw by spec.
    /// </summary>
    private static bool UsesCursor(WireColumn wire)
    {
        // Arrays go through it too. An array block states an encoding for its elements and
        // one for its rows' lengths, and the cursor is what decodes both - so an array's
        // elements are read exactly the way a scalar column's are, one level down.
        //
        // Uuid is the exception, and the same one it has always been: no encoding applies to
        // it, so it has no cursor path to reach.
        if (wire.ElementType == Models.ValueType.Uuid)
            return false;

        if (wire.IsArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional yes
        // was the int32 assumption in another place: a target keyed by `uuid` has no cursor
        // path any more than a `uuid` column does. spec/references/reference-key-types.md.
        if (wire.IsRef)
            return wire.RefKeyType != Models.ValueType.Uuid;

        switch (wire.ElementType)
        {
            // Int64 and Double are here for their promotions as well as their own
            // dictionaries: the file may carry an i32 column - encoded - where the
            // member has since widened.
            case Models.ValueType.Int32:
            case Models.ValueType.Int64:
            case Models.ValueType.Double:
            case Models.ValueType.Float:
            case Models.ValueType.Bool:
            case Models.ValueType.Enum:
            case Models.ValueType.String:

            // Ticks are an i64 column, so they meet the i64 dictionary like any other.
            case Models.ValueType.DateTime:
            case Models.ValueType.TimeSpan:
                return true;

            // Uuid is the one scalar left raw: sixteen-byte entries rarely repeat
            // enough to pay for the index beside them.
            default:
                return false;
        }
    }

    /// <summary>
    /// The cursor construction ahead of an encodable column's row loop, or nothing for
    /// a column that reads the reader directly.
    /// </summary>
    private static string CursorOpen(WireColumn wire, string tableName)
        => UsesCursor(wire)
            ? $"cursor = new TcbColumnCursor(reader, column, count, \"{tableName}.{wire.Name}\");"
            : "";

    /// <summary>
    /// The cursor's run method for a scalar whose values the run encodings cover, or
    /// empty for everything else - which then reads row by row as before.
    /// </summary>
    /// <remarks>
    /// Exactly the scalars whose cursor calls come down to NextI32 or NextString: int32
    /// members, enums, references and strings. The other cursor scalars stay per-row -
    /// their encodings are dictionaries, where the per-row work is already one index
    /// lookup - as do arrays, which are always raw.
    /// </remarks>
    /// <summary>
    /// The cursor call that reads one value of a key's type.
    /// </summary>
    /// <remarks>
    /// Only the types a key may be, because that is the one place this is asked. A `uuid` is
    /// absent for the reason it is absent from the cursor at all - no encoding applies to it,
    /// so <see cref="UsesCursor"/> answers no and the plain read path takes over.
    /// spec/references/reference-key-types.md.
    /// </remarks>
    private static string CursorCallFor(Models.ValueType keyType)
        => keyType switch
        {
            Models.ValueType.Int64 => "NextI64()",
            Models.ValueType.String => "NextString()",
            _ => "NextI32()",
        };

    private static string RunCall(WireColumn wire)
    {
        if (!UsesCursor(wire))
            return "";

        // A run says "this many rows hold the same value", which an array column's row does
        // not have one of. Its elements are read one at a time.
        if (wire.IsArray)
            return "";

        // A reference runs on the key it carries. `NextSameI32` was the only answer while a
        // key could only be an int. spec/references/reference-key-types.md.
        if (wire.IsRef)
        {
            return wire.RefKeyType switch
            {
                Models.ValueType.Int32 => "NextSameI32",
                Models.ValueType.String => "NextSameString",
                _ => "",
            };
        }

        if (wire.ElementType == Models.ValueType.Enum)
            return "NextSameI32";

        switch (wire.ElementType)
        {
            case Models.ValueType.Int32:
                return "NextSameI32";
            case Models.ValueType.String:
                return "NextSameString";
            default:
                return "";
        }
    }

    /// <summary>
    /// The lines assigning one row from `value`, a run's decoded value, inside the loop
    /// the template builds around <see cref="RunCall"/>.
    /// </summary>
    private IReadOnlyList<string> RunReadLines(
        WireColumn wire, string fieldName, string fieldType, string refTable, string memberAccess)
    {
        if (RunCall(wire).Length == 0)
            return Array.Empty<string>();

        if (wire.ElementType == Models.ValueType.Enum)
            return new[] { $"record.{fieldName}{memberAccess} = ({fieldType})value;" };

        if (wire.IsRef)
        {
            // A run is one value for many rows, which an array column has none of - so this
            // is only ever reached by a scalar, and a record member reaching it is a member
            // of a record of one. Its key lives on the member like every other member's
            // does; naming the group alone wrote into a field nothing declared.
            // spec/references/references-in-records.md.
            string key = (wire.Member is null)
                ? $"record.{fieldName}"
                : $"record.{fieldName}{memberAccess}";

            // The member's own name is the key's, and the row sits beside it under the
            // derived one. spec/references/reference-surface-naming.md sections 4, 5 and 9.
            string rowAccess = (wire.Member is not null && ResolvesToRow(wire.TagCarrier))
                ? string.Concat(wire.MemberPath.Take(wire.MemberPath.Count - 1)
                                    .Select(part => "." + CsName(part)))
                  + "." + CsName(RowAccessorName(
                        wire.TagCarrier.ResolvedRefTable!.Name, wire.MemberPath[^1]))
                : memberAccess;

            string target = $"record.{fieldName}{rowAccess}";

            string index = (wire.Member is null)
                ? $"record.{fieldName}_{refTable}_index"
                : ResolvesToRow(wire.TagCarrier) ? key : key + "_index";

            return new[]
            {
                $"{index} = value;",
                $"{target} = default({fieldType}); // will be assigned.",
                $"{key}_F = false;",
            };
        }

        return new[] { $"record.{fieldName}{memberAccess} = value;" };
    }

    /// <summary>
    /// Where a column's value lives in the generated record: the path down to it, and the
    /// subscript that follows when the array is the member's own rather than the group's.
    /// </summary>
    /// <remarks>
    /// Where the element number goes is the whole difference between the record shapes. An
    /// array of records indexes the group and then names the member; a record whose members
    /// are arrays names the member and then indexes it; a record of one indexes nothing.
    /// Same columns, same loop, same wire - see spec/types/nested-multi-level.md.
    ///
    /// The two are kept apart because a reference member declares its key and its flag on
    /// the member rather than on the element it holds: `Slots.ItemId[j]`, not
    /// `Slots.ItemId[j]_index`. spec/references/references-in-records.md.
    /// </remarks>
    private static (string Path, string Subscript) MemberPlace(
        WireColumn wire, string fieldName, string memberAccess)
    {
        if (!wire.IsArray)
            return ($"record.{fieldName}{memberAccess}", "");

        if (wire.Group.MembersAreAnonymous)
            return ($"record.{fieldName}[{wire.MemberAt}]", "[j]");

        // The member is the array, so the subscript goes on it rather than on the group -
        // `_bag.Tags[j]`, not `_bag[j].Tags`. A group whose element number is on a level
        // below it says so, and so does a member whose own cell holds the list.
        // spec/types/set-and-map.md section 4.
        if (wire.Group.MembersAreArrays || wire.LengthIsInTheCell)
            return ($"record.{fieldName}{memberAccess}", "[j]");

        return ($"record.{fieldName}[j]{memberAccess}", "");
    }

    private IReadOnlyList<string> ElementReadLines(
        WireColumn wire, string fieldName, string fieldType, string refTable, string memberAccess)
    {
        bool isArray = wire.IsArray;

        var (path, subscript) = MemberPlace(wire, fieldName, memberAccess);
        string target = path + subscript;

        // A record member keeps its key and flag inside the element, beside the row they
        // belong to - `record._slot[j].ItemId` rather than a parallel array named
        // after the group, which two members pointing at one table would collide in.
        // spec/references/references-in-records.md.
        string flag;
        string index;

        if (wire.Member is not null)
        {
            flag = path + "_F" + subscript;

            // The member's own name is the key's, and the row it resolves to sits beside it
            // under the derived one. spec/references/reference-surface-naming.md sections 4, 5 and 9.
            if (wire.IsRef && ResolvesToRow(wire.TagCarrier))
            {
                index = path + subscript;

                string rowAccess = string.Concat(
                        wire.MemberPath.Take(wire.MemberPath.Count - 1)
                            .Select(part => "." + CsName(part)))
                    + "." + CsName(RowAccessorName(
                        wire.TagCarrier.ResolvedRefTable!.Name, wire.MemberPath[^1]));

                var (rowPath, rowSubscript) = MemberPlace(wire, fieldName, rowAccess);
                target = rowPath + rowSubscript;
            }
            else
            {
                index = path + "_index" + subscript;
            }
        }
        else
        {
            flag = isArray ? $"record.{fieldName}_F[j]" : $"record.{fieldName}_F";
            index = isArray
                ? $"record.{fieldName}_{refTable}_index[j]"
                : $"record.{fieldName}_{refTable}_index";
        }

        // A column can arrive encoded, so it reads through the cursor - which also carries
        // the lossless promotions. An array's elements read through it as well, by the same
        // calls: what differs is only that the row's length comes from the cursor first.
        if (UsesCursor(wire))
        {
            if (wire.ElementType == Models.ValueType.Enum)
                return new[] { $"{target} = ({fieldType})cursor.NextI32();" };

            if (wire.IsRef)
            {
                // Only the stored key is on the wire; the value is filled in once every
                // table is loaded, and the flag records whether that happened. The call is
                // the key's own - `NextI32` for every reference is what kept a table keyed
                // by anything else from being pointed at. spec/references/reference-key-types.md.
                return new[]
                {
                    $"{index} = cursor.{CursorCallFor(wire.RefKeyType)};",
                    $"{target} = default({fieldType}); // will be assigned.",
                    $"{flag} = false;",
                };
            }

            switch (wire.ElementType)
            {
                case Models.ValueType.Int32:
                    return new[] { $"{target} = cursor.NextI32();" };
                case Models.ValueType.Int64:
                    return new[] { $"{target} = cursor.NextI64();" };
                case Models.ValueType.Double:
                    return new[] { $"{target} = cursor.NextF64();" };
                case Models.ValueType.Float:
                    return new[] { $"{target} = cursor.NextF32();" };
                case Models.ValueType.Bool:
                    return new[] { $"{target} = cursor.NextBool();" };

                // Ticks, so the member is built from what the i64 column carried.
                case Models.ValueType.DateTime:
                    return new[] { $"{target} = new System.DateTime(cursor.NextI64());" };
                case Models.ValueType.TimeSpan:
                    return new[] { $"{target} = new System.TimeSpan(cursor.NextI64());" };

                default:
                    return new[] { $"{target} = cursor.NextString();" };
            }
        }

        if (wire.ElementType == Models.ValueType.Enum)
        {
            // Enum values are zig-zag encoded, and the reader hands back an int.
            return new[]
            {
                "reader.ReadOptimalInt32(out tempEnumInt);",
                $"{target} = ({fieldType})tempEnumInt;",
            };
        }

        if (wire.IsRef)
        {
            return new[]
            {
                $"reader.Read(out {index});",
                $"{target} = default({fieldType}); // will be assigned.",
                $"{flag} = false;",
            };
        }

        // The three promotable members read through the As-helpers, so a file written
        // before the column was widened still reads. Everything else is exact.
        switch (wire.ElementType)
        {
            case Models.ValueType.Int32:
                return new[] { $"{target} = reader.ReadI32As(column.Element);" };
            case Models.ValueType.Int64:
                return new[] { $"{target} = reader.ReadI64As(column.Element);" };
            case Models.ValueType.Double:
                return new[] { $"{target} = reader.ReadF64As(column.Element);" };
            default:
                return new[] { $"reader.Read(out {target});" };
        }
    }

    private CsEnumView BuildEnum(Models.Enum enumm) => new CsEnumView
    {
        Name = enumm.Name.ToPascalCase(),
        Location = enumm.Location.ToString(),
        Comment = CommentLines(enumm.Comment),
        Labels = enumm.Labels.Select((label, index) => new CsEnumLabelView
        {
            Name = label.Name.ToPascalCase(),
            Value = label.Value.ToString(CultureInfo.InvariantCulture),
            Comment = CommentLines(label.Comment),
            IsLast = index == enumm.Labels.Count - 1,
        }).ToList(),
    };

    private CsConstantSetView BuildConstantSet(ConstantSet constantSet) => new CsConstantSetView
    {
        Name = constantSet.Name.ToPascalCase(),
        Location = constantSet.Location.ToString(),
        Comment = CommentLines(constantSet.Comment),
        Constants = constantSet.Constants.Select(constant => new CsConstantView
        {
            Name = constant.Name.ToPascalCase(),
            Type = ConstantTypeName(constant),
            Value = RenderConstantValue(constant),
            Comment = CommentLines(constant.Comment),
        }).ToList(),
    };

    // ------------------------------------------------------------- types

    private string ToCSharpTypeName(Field? field, bool asArray = false)
    {
        // ElementType, not Type: an array field is rendered by naming its element
        // and letting the caller add the brackets, exactly as a serial field is.
        return ToCSharpTypeName(field!.ElementType, field!.EnumOrNull, field!.RefTableName, asArray);
    }

    private string ToCSharpTypeName(Models.ValueType type, Models.Enum? enumm, string? refTableName, bool asArray = false)
    {
        string result;
        switch (type)
        {
            // The two that name something from the model rather than the language.
            case Models.ValueType.Enum:
                result = QualifiedNamespacePrefix + enumm!.Name.ToPascalCase();
                break;

            case Models.ValueType.ForeignRecord:
                result = $"{refTableName.ToPascalCase()}Table.Record";
                break;

            default:
                result = LanguageProfile.CSharp.ScalarTypeName(type);
                break;
        }

        return asArray ? LanguageProfile.CSharp.ArrayOf(result) : result;
    }

    /// <summary>
    /// How a constant's type is spelled, arrays included.
    /// </summary>
    /// <remarks>
    /// The type functions answer for an element and let the caller add the brackets, exactly
    /// as a field's do - so an array constant asks for the element and wraps it here.
    /// spec/layout/primary-layout.md section 8.5.
    /// </remarks>
    private string ConstantTypeName(ConstantSet.Constant constant)
        => ToCSharpTypeName(
            Models.ValueTypes.ElementOf(constant.Type), constant.Enum, null,
            asArray: Models.ValueTypes.IsArray(constant.Type));

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
        if (!Models.ValueTypes.IsArray(constant.Type))
        {
            return RenderConstantScalar(
                constant.Type, constant.Enum, constant.Value, constant.Location);
        }

        var element = Models.ValueTypes.ElementOf(constant.Type);

        string joined = string.Join(", ",
            ((System.Array)constant.Value!).Cast<object?>()
                .Select(value => RenderConstantScalar(
                    element, constant.Enum, value, constant.Location)));

        // The element type is written out rather than left to `new[]`: an empty list has
        // nothing to infer from, and a one-element list of an enum infers the enum only
        // because the label spells it.
        return $"new {ToCSharpTypeName(element, constant.Enum, null)}[] {{ {joined} }}";
    }

    /// <summary>One element, or a constant that is one value.</summary>
    private string RenderConstantScalar(
        Models.ValueType valueType, Models.Enum enumm, object? value, Location location)
    {
        switch (valueType)
        {
            case Models.ValueType.String:
                return $"\"{EscapeString((string)value!)}\"";

            case Models.ValueType.Bool:
                return (bool)value! ? "true" : "false";

            case Models.ValueType.Int32:
                return ((int)value!).ToString(CultureInfo.InvariantCulture);

            case Models.ValueType.Int64:
                return ((long)value!).ToString(CultureInfo.InvariantCulture);

            // Round-trip format, and invariant. The current culture would write a
            // comma for the decimal separator wherever the build machine uses one,
            // and `1,5f` is not a C# literal.
            case Models.ValueType.Float:
                return ((float)value!).ToString("R", CultureInfo.InvariantCulture) + "f";

            case Models.ValueType.Double:
                return ((double)value!).ToString("R", CultureInfo.InvariantCulture);

            // These three used to be written as their default ToString, which is not a
            // literal in any of the three cases - a constant of one of these types
            // produced a file that did not compile. Ticks and a uuid string are exact
            // and need no parsing at a culture's mercy.
            case Models.ValueType.TimeSpan:
                return $"new System.TimeSpan({((TimeSpan)value!).Ticks.ToString(CultureInfo.InvariantCulture)}L)";

            case Models.ValueType.DateTime:
                return $"new System.DateTime({((DateTime)value!).Ticks.ToString(CultureInfo.InvariantCulture)}L)";

            case Models.ValueType.Uuid:
                return $"new System.Guid(\"{(Guid)value!}\")";

            case Models.ValueType.Enum:
            {
                var label = enumm.GetLabel(value!, location);
                return $"{QualifiedNamespacePrefix}{enumm.Name.ToPascalCase()}.{label.Name.ToPascalCase()}";
            }

            default:
                throw new TabbitDefectException($"unsupported constant type `{valueType}`");
        }
    }

    private string EscapeString(string input)
    {
        var literal = new StringBuilder(input.Length + 2);

        foreach (var c in input)
        {
            switch (c)
            {
                case '\'': literal.Append("\\\'"); break;
                case '\\': literal.Append(@"\\"); break;
                case '\0': literal.Append(@"\0"); break;
                case '\a': literal.Append(@"\a"); break;
                case '\b': literal.Append(@"\b"); break;
                case '\f': literal.Append(@"\f"); break;
                case '\n': literal.Append(@"\n"); break;
                case '\r': literal.Append(@"\r"); break;
                case '\t': literal.Append(@"\t"); break;
                case '\v': literal.Append(@"\v"); break;
                default:
                    if (c >= 0x20 && c <= 0x7e)
                        literal.Append(c);
                    else
                        literal.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
            }
        }

        return literal.ToString();
    }

    // ----------------------------------------------------------- helpers

    /// <summary>
    /// Fully qualified so a generated reference to an enum cannot be captured by a type
    /// of the same name in the consumer's own namespace.
    /// </summary>
    private string QualifiedNamespacePrefix
        => string.IsNullOrEmpty(_csharpReceipe.Namespace)
            ? ""
            : "global::" + _csharpReceipe.Namespace + ".";

    /// <summary>
    /// A comment split into the lines the template will wrap in a doc comment. Empty
    /// when there is no comment, so the template needs no test of its own.
    /// </summary>
    // `new`, and not the base one: this tests IsNullOrEmpty rather than
    // IsNullOrWhiteSpace, so a comment of nothing but spaces reaches the template as
    // one blank line instead of none - and the golden pages record that.
    private static new IReadOnlyList<string> CommentLines(string comment)
    {
        if (string.IsNullOrEmpty(comment))
            return Array.Empty<string>();

        return comment.Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>
    /// What a member of the generated types is called.
    /// </summary>
    /// <remarks>
    /// Every other generator has had one of these; this one spelled the casing inline at
    /// each of its member sites instead, which is why nothing here ever asked
    /// <see cref="LanguageProfile"/> whether the answer was usable. At Pascal case the
    /// question has no teeth - every C# keyword is lower case, so no member can collide with
    /// one - and that is exactly the assumption a funnel has to hold in one place rather than
    /// in seventeen, because it stops being true the moment the spelling is anything else.
    ///
    /// Members only. The type names, the `Entry` record names, the lookup methods and the
    /// column literals are spelled where they are built, because none of them is a member
    /// and none of them should move when a member's spelling does.
    /// </remarks>
    private string CsName(string name)
        => LanguageProfile.CSharp.MemberName(name.ToCase(_memberCase));

    /// <summary>The type a single-column lookup takes.</summary>
    /// <remarks>
    /// **A reference column's index is keyed by the target's key, not the target's row.**
    /// The column's own name holds the key and the derived name holds the row, so a lookup
    /// typed by the row is one nobody can call - what a caller has is the id it read
    /// somewhere else. `KeyComponentView.TypeOf` is the one place that decides, and the
    /// composite path already asks it; this is the single-column path asking the same.
    /// </remarks>
    private string IndexKeyType(SerialField sf)
    {
        var (type, enumm) = KeyComponentView.TypeOf(sf);
        return ToCSharpTypeName(type, enumm, null);
    }
}
