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
    // Set by `Generate` before anything reads them, and they stay set for the whole of one
    // generation. `null!` says that to the compiler, which can only see the declaration.
    private Model _model = null!;
    private CSharpRecipe _csharpReceipe = null!;

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
    /// the read counts the levels. spec/nested-multi-level.md.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// An optional column becomes a `Has{Prop}` accessor beside the value one.
    /// </summary>
    /// <remarks>
    /// Not `T?`. It has to work the same for `string` as for `int`, and a nullable
    /// reference type needs a nullable context this output does not have - it compiles as
    /// the C# 8 Unity 2020.3 accepts. spec/optional-fields.md has the reasoning.
    /// </remarks>
    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// `HasXAt(i)` beside the value, filled from the element bitmap the file carries.
    /// spec/nullable-array-elements.md.
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
        Write(Path.Combine("tabbit", "TabbitHelpers.cs"), "csharp-helpers.sbn", Part());

        // The only file that knows what Unity is, and it is written whether or not the
        // consumer is Unity: outside the engine its body is behind a symbol nothing defines,
        // so it compiles to nothing. Written as source rather than folded into the accessor
        // because the engine's own compiler is what has to see those branches - and because
        // everything else this target writes stays plain netstandard as a result.
        Write(Path.Combine("tabbit", "TabbitUnityAdapter.cs"), "csharp-unity-adapter.sbn", Part());

        foreach (var table in view.Tables)
            Write(Path.Combine("tables", table.Name + "Table.cs"), "csharp-table.sbn", Part(table: table));

        foreach (var enumm in view.Enums)
            Write(Path.Combine("enums", enumm.Name + ".cs"), "csharp-enum.sbn", Part(enumm: enumm));

        foreach (var set in view.ConstantSets)
            Write(Path.Combine("constants", set.Name + ".cs"), "csharp-constants.sbn", Part(set: set));
    }

    /// <summary>A view for one of the single-subject templates.</summary>
    private CsPartView Part(
        CsTableView? table = null, CsEnumView? enumm = null, CsConstantSetView? set = null)
        => new CsPartView
        {
            Namespace = _csharpReceipe.Namespace,
            AccessorName = AccessorType,
            Table = table,
            Enumm = enumm,
            Set = set,
        };

    private void Write(string relative, string templateName, object view)
    {
        string filename = Path.GetFullPath(Path.Combine(_csharpReceipe.Path, relative));

        Emit(filename, Outdent(TemplateEngine.Render(templateName, view)));
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
            FileExtension = _csharpReceipe.BinaryTableFileExtension,
            Tables = tables,
            TablesWithReferences = tables
                .Where(t => t.ReferenceFields.Count > 0 || t.RecordReferenceFields.Count > 0)
                .ToList(),
            Enums = _model.Enums.Select(BuildEnum).ToList(),
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
            RawName = table.Name,
            Comment = CommentLines(table.Comment),
            Fields = fields,
            Columns = columns,

            IndexedFields = table.SerialFields
                                 .Select((sf, i) => new { sf, view = fields[i] })
                                 .Where(x => x.sf.IsIndexer)
                                 .Select(x => x.view)
                                 .ToList(),

            ReferenceFields = table.SerialFields
                                   .Select((sf, i) => new { sf, view = fields[i] })
                                   .Where(x => x.sf.IsRef)
                                   .Select(x => x.view)
                                   .ToList(),

            // A reference that is a member of a record resolves per element, so it needs a
            // loop of its own rather than a place in the list above. Read off the wire
            // columns, which is the same list the read path walks - the two have to agree
            // about where the key landed. spec/references-in-records.md.
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
            FieldNameLiterals = string.Join(", ", table.SerialFields.Select(sf => $"\"{sf.Name.ToPascalCase()}\"")),
            FieldValueExpressions = string.Join(", ", table.SerialFields.Select(sf => "r." + sf.Name.ToPascalCase())),
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
    /// spec/reference-optionality.md · spec/reference-key-types.md.
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
    /// One column of the file: how it is checked, how it is decoded, and where its values
    /// land.
    /// </summary>
    /// <remarks>
    /// The member it fills is the group's for a scalar column. A record group's member
    /// columns each fill one field of the generated element type, which is where these
    /// three differ from the group's - see spec/nested-fields.md.
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
        // spec/nested-multi-level.md.
        string memberAccess = (wire.Member is null)
            ? ""
            : string.Concat(wire.MemberPath.Select(name => "." + name.ToPascalCase()));

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
            not null when wire.IsVariableLengthArray => "record_var",
            not null when wire.IsFixedArray && wire.Group.MembersAreAnonymous
                => "array_of_arrays_member",
            not null when wire.IsFixedArray => "record_serial",
            _ => ReadKind(wire),
        };

        return new CsColumnView
        {
            Tag = wire.TagCarrier.Tag!.Value,
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
            ElementRead = ElementReadLines(wire, fieldName, fieldType, refTable, memberAccess),
            ParallelArrays = ParallelArrayLines(wire, fieldName, refTable),
            LengthRead = UsesCursor(wire)
                ? "elementCount = cursor.NextLength();"
                : "reader.TryReadCounter32(out elementCount);",
            RunCall = RunCall(wire),
            RunRead = RunReadLines(wire, fieldName, fieldType, refTable, memberAccess),
            FieldName = fieldName,
            FieldType = fieldType,
            PropName = wire.Group.Name.ToPascalCase(),
        };
    }

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
            PropName = sf.Name.ToPascalCase(),
            FieldName = fieldName,
            FieldType = fieldType,
            Initializer = Initializer(sf),
            ElementCount = sf.Fields.Count,
            RefTable = refTable,
            RefLookup = PrimaryLookup(sf.FirstField!.ResolvedRefTable),
            RefField = sf.FirstField!.RefFieldName.ToPascalCase() ?? "",
            RefKeyTypeName = ToCSharpTypeName(sf.FirstField!.RefKeyType, null, null),
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
            NeedsInit = members.Any(m => m.Initializer.Length > 0),
            IsOutermost = true,
        });

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
            // was never emitted. spec/nullable-array-elements.md.
            NeedsElementInit = members.Any(m => m.Initializer.Length > 0)
                || (sf.IsRecord && RecordNeedsFactory(sf)),

            // The group's own comment is the first member's column comment - a record has
            // no header cell of its own, so that is the nearest thing the sheet said.
            Comment = CommentLines(sf.Members[0].FirstField!.Comment),
            PropName = sf.Name.ToPascalCase(),
            FieldName = "_" + sf.Name.ToCamelCase(),

            // An array of arrays has no element type to name, so the inner array is the
            // type - see spec/nested-multi-level.md.
            FieldType = sf.MembersAreAnonymous
                ? ToCSharpTypeName(sf.Members[0].FirstField) + "[]"
                : sf.Name.ToPascalCase() + "Entry",
            Initializer = "",
            ElementCount = sf.RecordElementCount,
            Kind = sf.MembersAreAnonymous
                ? "array_of_arrays"
                : perRowLength
                    ? "record_var_array"
                    : sf.IsArray ? "record_array" : "record",

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

    /// <summary>
    /// One reference that is a member of a record, as the linking pass needs it.
    /// </summary>
    /// <remarks>
    /// Built from the wire column rather than from the declaration, so the name the linking
    /// pass writes and the name the read wrote come from one place. The loop bound says which
    /// of the three record shapes this is: the group's array, the member's, or neither.
    /// spec/references-in-records.md.
    /// </remarks>
    private static CsRecordReferenceView BuildRecordReference(WireColumn wire)
    {
        string fieldName = "_" + wire.Group.Name.ToCamelCase();
        string memberAccess = string.Concat(wire.MemberPath.Select(name => "." + name.ToPascalCase()));

        var (path, subscript) = MemberPlace(wire, fieldName, memberAccess);

        return new CsRecordReferenceView
        {
            Access = path + subscript,
            Key = path + "_index" + subscript,
            Flag = path + "_F" + subscript,

            // The member's own array when the number is on the member, the group's when it
            // is on the group, and nothing to walk when the group is one record. `Length`
            // rather than the column count, because a trimming group's rows differ in how
            // many they carry.
            Count = subscript.Length > 0
                ? path + ".Length"
                : (wire.IsFixedArray || wire.IsVariableLengthArray)
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
    /// different type name. spec/nested-multi-level.md.
    /// </remarks>
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
                    PropName = member.Name.ToPascalCase(),
                    FieldType = ToCSharpTypeName(member.FirstField) + (member.IsArray ? "[]" : ""),

                    // An array member allocates; a string one starts empty. Both for the same
                    // reason: a file predating the column leaves nothing to write it, and null
                    // one field later is a crash rather than a missing value.
                    Initializer = member.IsArray
                        ? $" = new {ToCSharpTypeName(member.FirstField)}[{member.Fields.Count}]"
                        : member.ElementType == Models.ValueType.String ? " = \"\"" : "",
                    IsFirst = at == 0,
                    IsArray = member.IsArray,
                    ElementInitializer = member.IsArray && member.ElementType == Models.ValueType.String
                        ? " = \"\""
                        : "",

                    // A reference member carries the key and the resolution flag beside the
                    // row it resolved to, all three inside the element - and all three at
                    // the member's own arity, because a record of arrays holds one key per
                    // element just as it holds one row per element.
                    // spec/references-in-records.md.
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

            declared.Add(new CsRecordTypeView
            {
                TypeName = typeName,
                Members = nested,
                NeedsInit = needsInit,
                IsOutermost = false,
            });

            result.Add(new CsRecordMemberView
            {
                Comment = CommentLines(member.FirstField!.Comment),
                PropName = member.Name.ToPascalCase(),
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

            return table.IsVariableLength(sf) ? "var_array" : "array";
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
    /// pay for a shift and a mask it did not ask for. spec/nullable-array-elements.md.
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
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
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
    /// flag inside the element, which the record allocates. spec/references-in-records.md.
    /// </remarks>
    private IReadOnlyList<string> ParallelArrayLines(
        WireColumn wire, string fieldName, string refTable)
    {
        if (!wire.IsRef || wire.Member is not null || !wire.IsFixedArray)
            return Array.Empty<string>();

        string keyType = ToCSharpTypeName(wire.RefKeyType, null, null);

        return new[]
        {
            $"record.{fieldName}_{refTable}_index = new {keyType}[column.Count];",
            $"record.{fieldName}_F = new bool[column.Count];",
        };
    }

    private static string ReadKind(WireColumn wire)
    {
        if (wire.IsVariableLengthArray)
            return "var_array";

        return wire.IsFixedArray ? "serial" : "scalar";
    }

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
        string kind = wire.IsVariableLengthArray
            ? "TcbTable.KindVarArray"
            : (wire.IsFixedArray ? "TcbTable.KindFixedArray" : "TcbTable.KindScalar");

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
            // The key the target is addressed by. `ElementI32` alone is what a reference
            // accepted while a key could only be an int - and the writer had meanwhile
            // learned to emit the key's own element, so the reader would have refused a file
            // this build wrote. A mismatch a compiler cannot see.
            // spec/reference-key-types.md.
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
                    throw new TabbitException($"The csharp generator cannot check type `{wire.Type}`.");
            }
        }

        // Nullability goes through the same check as kind and count, because it is the same
        // kind of fact: a file that says optional puts a presence bitmap in front of the
        // block, and code not expecting one would read the bitmap as values.
        string nullable = wire.IsNullable ? "true" : "false";

        // And the other bitmap, by the same argument. Passed by name because it comes after
        // the accepted elements, of which there may be one, two or three.
        string elements = wire.HasOptionalElements ? ", elementNullable: true" : "";

        return $"TcbTable.CheckColumn(column, \"{tableName}.{wire.Name}\", {kind}, {count}, "
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

        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return true;

        // A reference reaches the cursor when the key it carries does. An unconditional yes
        // was the int32 assumption in another place: a target keyed by `uuid` has no cursor
        // path any more than a `uuid` column does. spec/reference-key-types.md.
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
    /// spec/reference-key-types.md.
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
        if (wire.IsFixedArray || wire.IsVariableLengthArray)
            return "";

        // A reference runs on the key it carries. `NextSameI32` was the only answer while a
        // key could only be an int. spec/reference-key-types.md.
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
    private static IReadOnlyList<string> RunReadLines(
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
            // spec/references-in-records.md.
            string target = (wire.Member is null)
                ? $"record.{fieldName}"
                : $"record.{fieldName}{memberAccess}";
            string index = (wire.Member is null)
                ? $"record.{fieldName}_{refTable}_index"
                : target + "_index";

            return new[]
            {
                $"{index} = value;",
                $"{target} = default({fieldType}); // will be assigned.",
                $"{target}_F = false;",
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
    /// Same columns, same loop, same wire - see spec/nested-multi-level.md.
    ///
    /// The two are kept apart because a reference member declares its key and its flag on
    /// the member rather than on the element it holds: `Slots.ItemId_index[j]`, not
    /// `Slots.ItemId[j]_index`. spec/references-in-records.md.
    /// </remarks>
    private static (string Path, string Subscript) MemberPlace(
        WireColumn wire, string fieldName, string memberAccess)
    {
        if (!wire.IsFixedArray && !wire.IsVariableLengthArray)
            return ($"record.{fieldName}{memberAccess}", "");

        if (wire.Group.MembersAreAnonymous)
            return ($"record.{fieldName}[{wire.MemberAt}]", "[j]");

        if (wire.Group.MembersAreArrays)
            return ($"record.{fieldName}{memberAccess}", "[j]");

        return ($"record.{fieldName}[j]{memberAccess}", "");
    }

    private static IReadOnlyList<string> ElementReadLines(
        WireColumn wire, string fieldName, string fieldType, string refTable, string memberAccess)
    {
        bool isArray = wire.IsFixedArray || wire.IsVariableLengthArray;

        var (path, subscript) = MemberPlace(wire, fieldName, memberAccess);
        string target = path + subscript;

        // A record member keeps its key and flag inside the element, beside the row they
        // belong to - `record._slot[j].ItemId_index` rather than a parallel array named
        // after the group, which two members pointing at one table would collide in.
        // spec/references-in-records.md.
        string flag;
        string index;

        if (wire.Member is not null)
        {
            flag = path + "_F" + subscript;
            index = path + "_index" + subscript;
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
                // by anything else from being pointed at. spec/reference-key-types.md.
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
            Type = ToCSharpTypeName(constant.Type, constant.Enum, null),
            Value = RenderConstantValue(constant.Type, constant.Enum, constant.Value, constant.Location),
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

    private string RenderConstantValue(
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
                throw new TabbitException(location, $"unsupported constant type `{valueType}`");
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
}
