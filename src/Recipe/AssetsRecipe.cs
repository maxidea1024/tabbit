using System.Collections.Generic;

namespace Tabbit.Recipe;

/// <summary>
/// Where the files an `asset` column names are looked for.
/// </summary>
/// <remarks>
/// This is the whole of what makes the check possible. The tool has no idea what an asset is
/// - not what a texture is, not which extension matters, not whether the file is any good -
/// and it never learns. What it can do, once a recipe says which folders hold which kind, is
/// ask whether a file of that name is in one of them.
///
/// Leaving this out switches the check off. That is deliberate: an `asset` column still says
/// what it says, and a project that has not wired up its content tree yet should be able to
/// write one without the conversion refusing to run.
/// </remarks>
public class AssetsRecipe
{
    /// <summary>
    /// One folder to look in, and the kind of asset it holds.
    /// </summary>
    public class RootRecipe
    {
        /// <summary>
        /// Which kind of asset this folder holds - what an `asset(icon)` column writes in its
        /// brackets. Blank is the folder for a column that names no kind.
        /// </summary>
        /// <remarks>
        /// Matched without regard to case, because a kind is a word somebody types into a
        /// sheet cell and `Icon` and `icon` are not two kinds.
        /// </remarks>
        public string Kind { get; set; } = "";

        /// <summary>
        /// Directory to scan, including subdirectories. Blank switches this root off.
        /// </summary>
        /// <remarks>
        /// A path that is set but missing is an error rather than a root with nothing in it,
        /// because every value checked against it would be reported missing - which is a
        /// message about the recipe wearing the clothes of a message about the data.
        /// </remarks>
        public string Path { get; set; } = "";

        /// <summary>
        /// Which files in it count. `*` takes everything.
        /// </summary>
        /// <remarks>
        /// Narrowing it is worth doing where the tree holds more than the assets: a scan that
        /// takes every file will happily match a sheet's `Ship_Galleon` against somebody's
        /// `Ship_Galleon.txt` notes and report nothing wrong.
        /// </remarks>
        public string Pattern { get; set; } = "*";
    }

    /// <summary>
    /// The folders, one entry per kind and per place. Empty switches the check off.
    /// </summary>
    /// <remarks>
    /// Several entries may share a kind, and a value matching any of them passes. A content
    /// tree that grew in two places is ordinary, and making a project merge them before this
    /// tool will read either is asking it to reorganize itself for a checker.
    /// </remarks>
    public List<RootRecipe> Roots { get; set; } = new List<RootRecipe>();

    /// <summary>
    /// What to do about a value naming a file that is not there: `warn`, `error` or `ignore`.
    /// </summary>
    /// <remarks>
    /// `warn` is the default, and the default is the point of the setting. Data is routinely
    /// written before the art exists - a designer fills in the row today and the icon lands
    /// next week - and a conversion that refuses to run until every asset is drawn stops work
    /// for a reason that is not the data's.
    ///
    /// A build that has to be sure turns them into errors, and does it with the switch that
    /// already exists for exactly this: `Validation.TreatWarningsAsErrors`. So the same recipe
    /// serves both, and CI is the one place that says "not this time".
    ///
    /// `error` is for a project past that stage, and `ignore` records that the columns are
    /// declared and deliberately unchecked - which is a different thing from having no roots
    /// configured, and reads differently to the next person.
    /// </remarks>
    public string OnMissing { get; set; } = "warn";
}
