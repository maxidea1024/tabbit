using System.Collections.Generic;

namespace Tabbit.Recipe;

/// <summary>
/// Where the validation rules are, and what they are allowed to reach.
/// </summary>
/// <remarks>
/// The rules themselves are not here and never will be: they are C# files in the folder
/// this points at, so a project's rules live with the project rather than in a schema this
/// repository has to extend. spec/validation-pipeline.md.
/// </remarks>
public class ValidationRecipe
{
    /// <summary>
    /// Folder holding the rules, which sit in `rules/` laid out as `pre/` `tables/` `global/`
    /// `runtime/` `shared/`.
    /// </summary>
    /// <remarks>
    /// Blank switches the whole pipeline off, and that is the only way to switch it off -
    /// a path that is set but missing is an error rather than a quiet pass, because a
    /// gate that turns itself off is worse than no gate at all.
    /// </remarks>
    public string Path { get; set; } = "";

    /// <summary>
    /// Free key/value pairs only the rules read.
    /// </summary>
    /// <remarks>
    /// The same pattern as a layout's options: the core does not know the keys and does
    /// not check them. A locale code and a content root are the cases this exists for, and
    /// both are things this tool must not learn the meaning of.
    /// </remarks>
    public Dictionary<string, string> Options { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Connections the `rules/runtime/` rules open by name. `${NAME}` is filled from the
    /// environment, so a recipe holding no secrets can be committed.
    /// </summary>
    /// <remarks>
    /// Read-only accounts. The gateway a rule gets offers queries and nothing else, but
    /// that is a convenience rather than a guarantee - a rule is arbitrary C# and can open
    /// its own connection. The credential is the boundary, not the API.
    /// </remarks>
    public Dictionary<string, string> Connections { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Whether to write a project file beside the generated accessor, for an editor to read.
    /// </summary>
    /// <remarks>
    /// On, and the default matters here. A rule file belongs to no project, so an editor has
    /// nothing to resolve `Tables` through and completes nothing in it - which is the whole
    /// claim this pipeline makes about rules being typed. Writing rules with no completion is
    /// writing them against a schema that is only in the sheets.
    ///
    /// It was off for a while, on the argument that this is a file nobody asked for appearing in
    /// a project's own folder. That argument does not survive the accessor: the generated
    /// sources go into `.generated/` on every run whether or not anybody asked, and this is the
    /// one file that makes them reachable. Off meant writing the sources and then withholding
    /// the thing that reads them.
    ///
    /// Turn it off for an editor too old to load it. A Visual Studio predating this framework
    /// finds the loose project and fails on `Microsoft.NET.Sdk` - an error dialog rather than a
    /// completion - and such an editor cannot open `src/Tabbit.csproj` either, so it was never
    /// going to get the completion the file exists for.
    /// </remarks>
    public bool EmitIdeProject { get; set; } = true;

    /// <summary>
    /// Whether a warning stops the run the way an error does.
    /// </summary>
    /// <remarks>
    /// Off by default and turned on in CI. `Info` is never promoted by this: a report that
    /// can become an error is a judgement, and `Info` is a record.
    /// </remarks>
    public bool TreatWarningsAsErrors { get; set; } = false;

    /// <summary>
    /// The places whose reports this run knows about and does not stop for.
    /// </summary>
    /// <remarks>
    /// For the data somebody else owns. Each entry says where and why, the report comes out as
    /// `Info` rather than disappearing, and an entry that matches nothing - or a count that no
    /// longer holds - is an error. spec/known-problems.md.
    /// </remarks>
    public List<KnownProblemRecipe> KnownProblems { get; set; } = new List<KnownProblemRecipe>();
}
