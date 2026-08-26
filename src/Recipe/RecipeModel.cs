using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            ///
            /// This authenticates as the person running the conversion, which is what a
            /// developer's machine wants and what a build server does not - see
            /// <see cref="ServiceAccountKeyVariable"/>.
            /// </summary>
            public string ClientSecretFilename { get; set; } = "";

            /// <summary>
            /// Path to a service account key file.
            ///
            /// Authenticates as the job rather than as a person: nothing is interactive,
            /// and the document is shared with the service account's address the way it
            /// would be with a colleague. Do not commit this file.
            /// </summary>
            public string ServiceAccountKeyFile { get; set; } = "";

            /// <summary>
            /// Name of the environment variable holding a service account key.
            ///
            /// The name, not the key: recipes are committed. What a CI job wants, since a
            /// key put in a secret store never becomes a file on the runner.
            ///
            /// Naming this and <see cref="ServiceAccountKeyFile"/> together is refused,
            /// and so is naming either alongside <see cref="ClientSecretFilename"/>:
            /// those authenticate as different identities, and picking one silently is
            /// how a job comes to read a document as somebody it is not.
            /// </summary>
            public string ServiceAccountKeyVariable { get; set; } = "";

            /// <summary>
            /// The document id, which is the long identifier in its URL.
            /// </summary>
            public string SheetsId { get; set; } = "";
        }

        /// <summary>Excel sources.</summary>
        public List<XlsxRecipe> Xlsx { get; set; } = new List<XlsxRecipe>();

        /// <summary>Google Sheets sources.</summary>
        public List<GoogleSheetsRecipe> GoogleSheets { get; set; } = new List<GoogleSheetsRecipe>();

        /// <summary>
        /// Every sheet-reading entry this group holds, whichever source presents it.
        /// </summary>
        /// <remarks>
        /// For the settings that a command-line option forces over the recipe: the override
        /// has to reach each entry, and an entry's own value is exactly what it has to
        /// replace. Found by walking this object's own lists rather than naming them, so a
        /// third source becomes readable here by existing.
        /// </remarks>
        public IEnumerable<SheetSourceRecipe> SheetEntries()
        {
            foreach (var property in GetType().GetProperties(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetValue(this) is not System.Collections.IEnumerable entries)
                    continue;

                foreach (object? entry in entries)
                {
                    if (entry is SheetSourceRecipe sheets)
                        yield return sheets;
                }
            }
        }
    }

    /// <summary>Where the sheets are read from.</summary>
    public SourceRecipeGroup Sources { get; set; } = new SourceRecipeGroup();

    /// <summary>
    /// Where the type declarations are read from.
    /// </summary>
    /// <remarks>
    /// <code>
    /// "Schemas": [{ "Path": "./schemas" }]
    /// </code>
    ///
    /// Empty is a project whose types are all written in its sheets, which is every project
    /// that existed before these files did. Nothing here changes what a sheet may say - a
    /// declaration made in one of these files is another way to say it, and one that does
    /// not have to be repeated in every table that uses the type.
    ///
    /// notes/struct-dsl-design.md section 7.4.
    /// </remarks>
    public List<SchemaSourceRecipe> Schemas { get; set; } = new List<SchemaSourceRecipe>();

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

    /// <summary>
    /// Which time zone the wall clock in a `datetime` cell was written in, so the value
    /// stored for it is the moment it names.
    ///
    /// Named as `Asia/Seoul` or `Korea Standard Time`, or written as a fixed offset:
    /// `+09:00`, `-05:30`, `+0900`, `+09`, or `Z` for UTC itself.
    ///
    /// Blank, which reads a cell as already being in UTC. Data leaves this tool in UTC
    /// whatever this says - the setting decides how a sheet's wall clock is read, not
    /// what is stored. A source entry may override it for its own sheets.
    /// </summary>
    /// <remarks>
    /// A cell that wrote its own offset - `2022-01-24T10:30:00Z`, or the same with
    /// `+09:00` - already names a moment, and this does not move it.
    ///
    /// A name and an offset are not the same answer. The name carries the region's
    /// history, so a date from a summer under daylight saving converts by the offset that
    /// was in force then; a fixed offset is the same all year, which is what sheets
    /// written to one office's clock usually mean. spec/types/datetime-timezone.md.
    /// </remarks>
    public string TimeZone { get; set; } = "";

    /// <summary>
    /// Spelling of the exported data files' names. Blank keeps the table's own name.
    /// </summary>
    /// <remarks>
    /// Takes `pascal`, `camel`, `snake` or `upper-snake`.
    ///
    /// Here rather than on a target, which is the one thing about it worth explaining. Every
    /// other spelling setting belongs to whatever reads it, but this name is a contract
    /// between programs: the exporter writes the file and the reader generated for each
    /// language opens it, and the two computing it separately is how a build comes to produce
    /// data that its own reader cannot find. One setting for the whole recipe is what makes
    /// that impossible to get wrong.
    ///
    /// A row set's suffix is appended after the spelling is applied, as the sheet wrote it -
    /// separator included, which is the existing rule for those. So `snake` turns table
    /// `ItemDrop` with set `_alt` into `item_drop_alt`.
    /// </remarks>
    public string DataFileCase { get; set; } = "";

    /// <summary>
    /// Colour palettes this build knows, each name mapped to the file it is read from.
    ///
    /// <code>
    /// "Palettes": { "material": "art/palettes/material.json" }
    /// </code>
    ///
    /// A palette file is a JSON object of colour name to `#RRGGBB` or `#RRGGBBAA`. A cell
    /// reaches one by naming it - `material.blue.500` - and a bare name is always the
    /// built-in `css` palette, which is what keeps two palettes from disagreeing about what
    /// `red` means.
    ///
    /// Data rather than code, so adding one is a file rather than a build of this tool, and
    /// a project's own palette never reaches this repository. `css` cannot be replaced.
    ///
    /// spec/types/composite-value-types.md section 4.4.
    /// </summary>
    public Dictionary<string, string> Palettes { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Which variant of a field this build takes, by `Table.Field`.
    /// </summary>
    /// <remarks>
    /// <code>
    /// "Variants": { "Item.Price": "kr" }
    /// </code>
    ///
    /// A sheet may write one field's value column several times over and name each one, and
    /// this is where a build says which of them it wants - a field with no entry here takes
    /// the column that named no variant. The produced files know nothing about any of it: one
    /// column becomes the field and the rest are not in the build, so the model, the wire and
    /// every generated reader are the same as if the sheet had one column.
    ///
    /// Recipe-wide rather than per source entry, because a table is one table however many
    /// sheets it was read from - and a build that took `kr` prices from one workbook and the
    /// default from another would be a build nobody asked for.
    ///
    /// The command line writes the same thing as `--variant Item.Price=kr`, which is what makes
    /// a one-off build of the other variant possible without editing the recipe.
    ///
    /// spec/layout/primary-layout.md section 3.6.
    /// </remarks>
    public Dictionary<string, string> Variants { get; set; } = new Dictionary<string, string>();
    #endregion


    #region Target group

    /// <summary>
    /// Everything this run produces, each entry naming the target that produces it.
    ///
    /// <code>
    /// "Targets": [
    ///   { "Type": "python", "Path": "./out/py", "PackageName": "gamedata" },
    ///   { "Type": "binary", "Path": "./out/data" }
    /// ]
    /// </code>
    ///
    /// `Type` picks the target; everything beside it is that target's own settings. This
    /// is the only way output is requested - there is no section per target, which is
    /// what keeps this class from growing one member per language and keeps a target
    /// deletable by deleting its file.
    ///
    /// There used to be a section per target for the targets that predate this list, and
    /// nothing distinguished those ten but their age. Two ways to say the same thing is
    /// one more than a reader of a recipe can derive a rule from.
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


    #region Naming group

    /// <summary>
    /// Which spelling the names in the sheets have to follow.
    /// </summary>
    /// <remarks>
    /// Top level rather than per source, because a name belongs to the model: the case
    /// worth reporting is the same name written one way in one workbook and another way
    /// in the next, and a setting held per source could not see it.
    ///
    /// Leaving the section out still leaves two checks running - see
    /// <see cref="NamingRecipe"/> for which and why.
    /// </remarks>
    public NamingRecipe Naming { get; set; } = new NamingRecipe();
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

    /// <summary>
    /// What a run does with the problems it found: where the report goes, and when it
    /// opens itself.
    ///
    /// Written whether the run succeeded or not, because a run that stopped is the one
    /// whose report is worth reading. spec/ops/build-report.md.
    /// </summary>
    public ReportRecipe Report { get; set; } = new ReportRecipe();
    #endregion


    /// <summary>
    /// Reads a recipe. Comments are permitted, which is why recipes can explain
    /// themselves in place.
    /// </summary>
    /// <remarks>
    /// Parsed to a document first so that <see cref="RecipeVariables"/> can fill the
    /// `${NAME}` placeholders before anything is bound to a property. Substituting into
    /// the text instead would mean escaping each value back into JSON, and a value
    /// holding a quote would then produce a file that no longer parses rather than a
    /// wrong setting.
    /// </remarks>
    public static RecipeModel? LoadFromFile(string filename) => LoadFromFile(filename, out _);

    /// <param name="document">
    /// The parsed recipe, after substitution and with its comments already gone.
    /// </param>
    /// <remarks>
    /// Handed out for the build cache, which keys on the document rather than on this object.
    /// Two things follow from that choice and both are wanted: a setting added to the recipe
    /// schema later is in the key without anybody putting it there, and editing a comment
    /// costs nothing because the parser has dropped them by now.
    /// </remarks>
    public static RecipeModel? LoadFromFile(string filename, out JObject? document)
    {
        document = null;

        string json = File.ReadAllText(filename);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        // Comments dropped rather than kept as tokens: they are here to explain the
        // recipe to whoever opens it, and nothing downstream reads them.
        var parsed = JObject.Parse(json, new JsonLoadSettings
        {
            CommentHandling = CommentHandling.Ignore,
        });

        RecipeVariables.Expand(parsed, filename);

        document = parsed;

        return parsed.ToObject<RecipeModel>();
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
