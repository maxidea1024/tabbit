using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tabbit.Targets;

namespace Tabbit.Recipe;

public class RecipeModel
{
    #region Source group

    /// <summary>
    /// Where the sheets are read from.
    ///
    /// Several sources combine into one model, so a project can split its data across
    /// workbooks and Google Sheets documents however suits the people editing it.
    /// </summary>
    public class SourceRecipeGroup
    {
        /// <summary>
        /// A directory of Excel workbooks.
        /// </summary>
        public class XlsxRecipe : SheetSourceRecipe
        {
            /// <summary>
            /// Directory to search, including subdirectories.
            ///
            /// Any file or directory whose name begins with `#` is skipped, which is
            /// how work in progress is kept out of a build.
            /// </summary>
            public string Path { get; set; } = "";

            /// <summary>
            /// Semicolon-separated extensions to pick up. Everything else in the
            /// directory is ignored.
            /// </summary>
            public string FileExtensionPatterns { get; set; } = ".xls;.xlsx";
        }

        /// <summary>
        /// A Google Sheets document, fetched over the API.
        /// </summary>
        public class GoogleSheetsRecipe : SheetSourceRecipe
        {
            /// <summary>
            /// Path to the OAuth client secret downloaded from the Google Cloud
            /// console.
            ///
            /// Do not commit this file. The first run opens a browser for consent and
            /// caches the resulting token under the user's profile, so only the first
            /// run is interactive.
            /// </summary>
            public string ClientSecretFilename { get; set; } = "";

            /// <summary>
            /// The document id, which is the long identifier in its URL.
            /// </summary>
            public string SheetsId { get; set; } = "";
        }

        /// <summary>Excel sources.</summary>
        public List<XlsxRecipe> Xlsx { get; set; } = new List<XlsxRecipe>();

        /// <summary>Google Sheets sources.</summary>
        public List<GoogleSheetsRecipe> GoogleSheets { get; set; } = new List<GoogleSheetsRecipe>();
    }

    /// <summary>Where the sheets are read from.</summary>
    public SourceRecipeGroup Sources { get; set; } = new SourceRecipeGroup();

    /// <summary>
    /// Inserts a `None = 0` label into any enum that declares neither the name `None`
    /// nor the value zero.
    ///
    /// On by default, because a field of an enum type has to hold something before it
    /// is assigned and a nameless zero is worse than a named one. Turn it off for a
    /// project that would rather its enums contain exactly what the sheets say - at
    /// the cost of a default-constructed field holding a value with no label.
    /// </summary>
    public bool AutoInsertEnumNoneLabel { get; set; } = true;

    /// <summary>
    /// Character separating the elements of an array cell, for fields typed
    /// `int[]`, `string[]` and so on.
    ///
    /// Semicolon by default rather than comma, because comma appears constantly in
    /// ordinary prose and in numbers formatted for humans. Whitespace around each
    /// element is trimmed, so `1; 2 ;3` reads the same as `1;2;3`.
    /// </summary>
    public string ArrayDelimiter { get; set; } = ";";
    #endregion


    #region Export group

    /// <summary>
    /// Where the converted data is written.
    ///
    /// File targets stage their output and commit it at the end of a successful run;
    /// database targets load into shadow storage and swap it in. Either way a failed
    /// run leaves the previous output untouched.
    /// </summary>
    public class ExportRecipeGroup
    {
        /// <summary>
        /// One binary file per table, in Tabbit's own Tcb format.
        ///
        /// This is what the generated C# and C++ readers consume.
        /// </summary>
        public class BinaryRecipe : IOutputRecipe
        {
            /// <summary>Output directory. Created if it does not exist.</summary>
            public string Path { get; set; } = "";

            /// <summary>
            /// Extension of each table file. Must match the extension the code
            /// generators are told to expect.
            /// </summary>
            public string FileExtension { get; set; } = ".tcb";

            /// <summary>
            /// Reserved. Not implemented: the format writes a reserved byte where a
            /// compression flag would go, but nothing sets or reads it, and the
            /// generated readers reject a non-zero value.
            /// </summary>
            public bool Compress { get; set; } = false;

            /// <summary>
            /// Which side this output is built for: "c", "s", or "cs"/blank for
            /// both. Entities and fields marked for the other side are left out.
            ///
            /// Declare the same side on the exporter and on the code generator
            /// that reads its files: the two must agree on the column set or the
            /// generated reader will not match the data.
            /// </summary>
            public string TargetSide { get; set; } = "cs";

            /// <summary>Removes files this run did not write.</summary>
            /// <remarks>
            /// On, because the output is a file per table: rename or delete a table and
            /// its old file stays behind. A stale data file is worse than a stale source
            /// file - it ships, it costs transfer, and a build still asking for the old
            /// name reads it, which is old values from a rollback nobody performed.
            ///
            /// Only files the manifest already lists are removed. That ledger is this
            /// tool's own record of what it put here, so a directory holding anything
            /// else is untouchable - the file has to have been written by a previous run
            /// to be removable by this one.
            /// </remarks>
            public bool Sweep { get; set; } = true;

            /// <summary>
            /// Where to keep the record of the columns data was last written with.
            ///
            /// Commit the file. Every run compares the schema against it and refuses a
            /// change that a reader already built from the previous schema would not
            /// survive - a deleted column whose tag is left free, a type that changed,
            /// a fixed array that grew. Blank switches the check off, which leaves the
            /// generated readers' own refusals as the only guard, and those fire in the
            /// client rather than here.
            /// </summary>
            public string SchemaBaseline { get; set; } = "";

            /// <summary>
            /// Columns whose changed shape is deliberate, as `Table.Column`.
            ///
            /// A type change is not a thing the baseline can wave through on its own:
            /// an already-deployed reader refuses the column rather than reading it
            /// wrongly, so the change only works if regenerated code ships with the
            /// data. Naming the column here says that it does.
            ///
            /// An acknowledgment is spent once. The next run compares against a baseline
            /// that already has the new shape, so the entry can be taken back out.
            /// </summary>
            public List<string> AcceptSchemaChanges { get; set; } = new List<string>();

            /// <summary>
            /// Where to write a report of what every column measured. Blank switches it off.
            /// </summary>
            /// <remarks>
            /// The exporter already encodes every applicable candidate in full and keeps the
            /// smallest, so the sizes are measurements rather than estimates - the report
            /// states the same numbers the choice was made on, plus what the alternatives
            /// would have come to.
            ///
            /// It also measures layouts the format does not have, over the distinct values of
            /// each string column, so that adding one can be argued from a number. Doing that
            /// costs real time on a large export, which is why it happens only when a path is
            /// named here.
            /// </remarks>
            public string EncodingReport { get; set; } = "";

            /// <summary>
            /// The environment variable holding the encryption key, as 64 hexadecimal
            /// characters. Blank leaves the files unencrypted.
            /// </summary>
            /// <remarks>
            /// The name of the variable, never the key. A recipe is committed and handed
            /// around, and a key written into one is in a repository's history from then on.
            ///
            /// What the encryption is for is stated in the format's own documentation, and
            /// the short of it is that it stops a data file from opening as plain text and
            /// from accepting an edit, not from being read by someone who can take the key
            /// out of the client that carries it.
            /// </remarks>
            public string EncryptionKeyVariable { get; set; } = "";

            /// <summary>
            /// A file holding the encryption key, as 64 hexadecimal characters. An
            /// alternative to <see cref="EncryptionKeyVariable"/>; naming both is refused.
            /// </summary>
            public string EncryptionKeyFile { get; set; } = "";

            /// <summary>
            /// The environment variable holding the MAC key, as 64 hexadecimal characters.
            /// Blank leaves the files without a MAC, and a reader without a key to check.
            /// </summary>
            /// <remarks>
            /// The name of the variable, never the key, for the same reason as the encryption
            /// key - and a different key from that one, because a file can be authenticated
            /// without being encrypted and the other way round.
            ///
            /// What it adds is the one thing encryption does not: a file that was edited after
            /// it was written stops loading. The structural checks cannot do this, because a
            /// fixed-width value accepts every bit pattern, and neither can the cipher, whose
            /// keystream XOR lets a bit be flipped through the ciphertext without a key.
            ///
            /// Turning it on has an order to it. Export the data with a MAC first, then ship
            /// the key in the client - a client that holds a MAC key refuses files that carry
            /// no MAC, which is what stops the check being removed by zeroing sixteen bytes.
            /// </remarks>
            public string MacKeyVariable { get; set; } = "";

            /// <summary>
            /// A file holding the MAC key, as 64 hexadecimal characters. An alternative to
            /// <see cref="MacKeyVariable"/>; naming both is refused.
            /// </summary>
            public string MacKeyFile { get; set; } = "";
        }

        /// <summary>
        /// One .json file per table.
        ///
        /// This is what the generated TypeScript reads.
        /// </summary>
        public class JsonRecipe : IOutputRecipe
        {
            /// <summary>Output directory. Created if it does not exist.</summary>
            public string Path { get; set; } = "";

            /// <summary>
            /// Writes each row as a bare array of values instead of an object with
            /// field names.
            ///
            /// Smaller, at the cost of being unreadable on its own. The generated
            /// readers handle both, deciding from the shape of the first row.
            /// </summary>
            public bool UseCompactRowFormat { get; set; } = false;

            /// <summary>
            /// Pretty-prints the output. Worth it while inspecting data by hand, not
            /// for something a program will read.
            /// </summary>
            public bool Indented { get; set; } = false;

            /// <summary>
            /// Which side this output is built for: "c", "s", or "cs"/blank for
            /// both. Entities and fields marked for the other side are left out.
            ///
            /// Declare the same side on the exporter and on the code generator
            /// that reads its files: the two must agree on the column set or the
            /// generated reader will not match the data.
            /// </summary>
            public string TargetSide { get; set; } = "cs";

            /// <summary>Removes files this run did not write.</summary>
            /// <remarks>
            /// On, because the output is a file per table: rename or delete a table and
            /// its old file stays behind. A stale data file is worse than a stale source
            /// file - it ships, it costs transfer, and a build still asking for the old
            /// name reads it, which is old values from a rollback nobody performed.
            ///
            /// Only files the manifest already lists are removed. That ledger is this
            /// tool's own record of what it put here, so a directory holding anything
            /// else is untouchable - the file has to have been written by a previous run
            /// to be removable by this one.
            /// </remarks>
            public bool Sweep { get; set; } = true;
        }

        /// <summary>
        /// Shared settings for the database export targets.
        ///
        /// Each target loads into shadow tables and then swaps them in, so a run
        /// that fails partway leaves the live data untouched. Atomicity is per
        /// store: files and four databases cannot be committed as one transaction
        /// without a distributed coordinator, so each is made atomic on its own
        /// rather than pretending otherwise.
        /// </summary>
        public abstract class DatabaseRecipe : IOutputRecipe
        {
            /// <summary>
            /// Connection string. Supports `${NAME}` placeholders filled from the
            /// environment, so a recipe holding no secrets can be committed:
            ///
            ///     "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}"
            /// </summary>
            public string ConnectionString { get; set; } = "";

            /// <summary>
            /// Prefix applied to every table, collection or key name written.
            /// Lets one database hold several independent sets of exported data.
            /// </summary>
            public string NamePrefix { get; set; } = "";

            /// <summary>
            /// Which side this output is built for: "c", "s", or "cs"/blank for
            /// both. Entities and fields marked for the other side are left out.
            /// </summary>
            public string TargetSide { get; set; } = "cs";
        }

        /// <summary>
        /// MongoDB target. One collection per table, one document per row.
        /// </summary>
        public class MongoDbRecipe : DatabaseRecipe
        {
        }

        /// <summary>
        /// MySQL target. One table per table, recreated on each run.
        /// </summary>
        public class MySqlRecipe : DatabaseRecipe
        {
        }

        /// <summary>
        /// PostgreSQL target. One table per table, recreated on each run.
        /// </summary>
        public class PostgreSqlRecipe : DatabaseRecipe
        {
            /// <summary>Schema the tables are created in.</summary>
            public string Schema { get; set; } = "public";
        }

        /// <summary>
        /// Redis target. One hash per row, plus an index set per table.
        /// </summary>
        public class RedisRecipe : DatabaseRecipe
        {
            /// <summary>Database number to select on the server.</summary>
            public int Database { get; set; } = 0;
        }

        /// <summary>Binary file targets.</summary>
        public List<BinaryRecipe> Binary { get; set; } = new List<BinaryRecipe>();

        /// <summary>JSON file targets.</summary>
        public List<JsonRecipe> Json { get; set; } = new List<JsonRecipe>();

        /// <summary>MongoDB targets.</summary>
        public List<MongoDbRecipe> MongoDb { get; set; } = new List<MongoDbRecipe>();

        /// <summary>MySQL targets.</summary>
        public List<MySqlRecipe> MySql { get; set; } = new List<MySqlRecipe>();

        /// <summary>PostgreSQL targets.</summary>
        public List<PostgreSqlRecipe> PostgreSql { get; set; } = new List<PostgreSqlRecipe>();

        /// <summary>Redis targets.</summary>
        public List<RedisRecipe> Redis { get; set; } = new List<RedisRecipe>();
    }

    /// <summary>Where the converted data is written.</summary>
    public ExportRecipeGroup Exports { get; set; } = new ExportRecipeGroup();
    #endregion


    #region Code generation group

    /// <summary>
    /// What source code to emit for reading the exported data.
    ///
    /// The point of the tool: a project uses the declared entities without writing
    /// any loading code of its own.
    /// </summary>
    public class CodeGenerationRecipeGroup
    {
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
        }

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
        /// TypeScript modules. Read the JSON export.
        /// </summary>
        public class TypescriptRecipe : IOutputRecipe
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
            ///
            /// It fetches the manifest and the changed data files over HTTP and keeps a
            /// local copy current, so a build can take new data without shipping a new
            /// one. Off by default: a project that ships its data alongside its code has
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
            /// Emits enums as string unions rather than numeric enums.
            ///
            /// Readable in a debugger and in logs, at the cost of not matching the
            /// integers the exported data actually carries.
            /// </summary>
            public bool UseStringEnum { get; set; }
        }

        /// <summary>
        /// Browsable documentation of the converted data.
        ///
        /// Not consumed by any program: it exists so the data that reached a build can
        /// be checked by eye, with links back to the cell each value came from.
        /// </summary>
        public class HtmlRecipe : IOutputRecipe
        {
            /// <summary>Output directory. Created if it does not exist.</summary>
            public string Path { get; set; } = "";

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

        /// <summary>C++ targets.</summary>
        public List<CppRecipe> Cpp { get; set; } = new List<CppRecipe>();

        /// <summary>C# targets.</summary>
        public List<CSharpRecipe> CSharp { get; set; } = new List<CSharpRecipe>();

        /// <summary>TypeScript targets.</summary>
        public List<TypescriptRecipe> Typescript { get; set; } = new List<TypescriptRecipe>();

        /// <summary>HTML documentation targets.</summary>
        public List<HtmlRecipe> Html { get; set; } = new List<HtmlRecipe>();
    }

    /// <summary>What source code to emit for reading the exported data.</summary>
    public CodeGenerationRecipeGroup CodeGenerations { get; set; } = new CodeGenerationRecipeGroup();
    #endregion


    #region Target group

    /// <summary>
    /// Output entries named by target id rather than by recipe section.
    ///
    /// <code>
    /// "Targets": [
    ///   { "Type": "python", "Path": "./out/py", "PackageName": "gamedata" },
    ///   { "Type": "binary", "Path": "./out/data" }
    /// ]
    /// </code>
    ///
    /// `Type` picks the target; everything beside it is that target's own settings, the
    /// same fields its dedicated section would take. Any registered target can be used
    /// here, including the ones that have a section of their own, so a recipe may use
    /// either form or both.
    ///
    /// This exists so that adding a target does not mean extending this class. The
    /// sections above are the targets that predate it and stay for the recipes that
    /// already use them; a target added since is reached only through here.
    ///
    /// Held as raw JSON because the entry type is not known until `Type` is read. The
    /// registry deserializes each one into its target's entry type, rejecting an
    /// unrecognized `Type` and any field the target does not have - a misspelled
    /// setting is a mistake worth reporting, not a default worth taking silently.
    /// </summary>
    public List<JObject> Targets { get; set; } = new List<JObject>();
    #endregion


    #region Assets group

    /// <summary>
    /// Where the files an `asset` column names are looked for.
    /// </summary>
    /// <remarks>
    /// Here rather than on a target because it is not about output: the check runs while the
    /// model is being cooked, so it reports the cell, and it runs whether or not anything is
    /// being written. Leaving it out switches the check off.
    /// </remarks>
    public AssetsRecipe Assets { get; set; } = new AssetsRecipe();
    #endregion


    #region Validation group

    /// <summary>
    /// Where the validation rules are, and what they may reach.
    ///
    /// The rules themselves are C# files in the folder this names, so a project's rules
    /// live with the project. Blank switches the pipeline off, and that is the only way
    /// to switch it off.
    /// </summary>
    public ValidationRecipe Validation { get; set; } = new ValidationRecipe();
    #endregion


    /// <summary>
    /// Reads a recipe. Comments are permitted, which is why recipes can explain
    /// themselves in place.
    /// </summary>
    public static RecipeModel? LoadFromFile(string filename)
    {
        string json = File.ReadAllText(filename);
        return JsonConvert.DeserializeObject<RecipeModel>(json);
    }

    /// <summary>
    /// The most recently constructed recipe.
    ///
    /// Ambient state, and dubious: deserialization and `--new-recipe` both construct
    /// one, so this points at whichever happened last rather than at the recipe being
    /// run. Nothing reads it today; prefer passing the recipe explicitly, as the
    /// exporters and generators do.
    /// </summary>
    public static RecipeModel Current { get; private set; } = null!;

    /// <summary>
    /// Publishes the new instance as <see cref="Current"/>.
    /// </summary>
    public RecipeModel()
    {
        Current = this;
    }
}
