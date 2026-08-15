using System;
using System.Collections.Generic;
using System.IO;
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
    /// There used to be a section per target for the ten that predate this list, and
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
