using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

namespace Tabbit.Recipe;

/// <summary>
/// Fills the `${NAME}` placeholders in a recipe from the environment.
/// </summary>
/// <remarks>
/// A recipe is committed, so anything that differs between the machines running it
/// cannot be written into one. That was already true of passwords, which is why the
/// database connection strings have taken `${NAME}` from the start; it is equally true
/// of the things that separate one environment from another - which document the sheets
/// come from, where the output goes.
///
/// So the substitution is the whole file rather than one setting. One recipe and two
/// sets of variables then describe two environments, instead of two recipes that have to
/// be kept in step by hand and are not.
///
/// Applied to the parsed document rather than to its text: a value holding a quote or a
/// backslash would otherwise have to be escaped back into JSON, and getting that wrong
/// turns a wrong value into a file that no longer parses.
/// </remarks>
internal static class RecipeVariables
{
    private static readonly Regex Placeholder =
        new Regex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// Expands every placeholder in the document, in place.
    /// </summary>
    /// <param name="root">The parsed recipe.</param>
    /// <param name="filename">Path of the recipe, for error messages.</param>
    public static void Expand(JToken root, string filename)
    {
        // Collected rather than thrown at the first one. Somebody setting up a machine
        // has all of them to set, and finding out one at a time is one run per variable.
        var missing = new List<(string Name, string Path)>();

        Walk(root, missing);

        if (missing.Count == 0)
            return;

        var listed = new StringBuilder();

        foreach (var (name, path) in missing)
            listed.Append($"{Environment.NewLine}  {name} — at `{path}`");

        // The one variable worth naming a remedy for. A recipe carrying it is a recipe
        // written for more than one environment, and somebody meeting it for the first
        // time is usually running a colleague's recipe rather than their own - so the
        // useful sentence is which flag says which environment, not what a variable is.
        // Two ids: the same report, with one paragraph more when the environment variable is
        // among the missing. A recipe carrying that one is written for more than one
        // environment, and the reader is usually running somebody else's.
        bool namesEnvironment = missing.Exists(entry => entry.Name == RunEnvironment.Variable);

        throw new TabbitException(null, Messages.Message.Of(
            namesEnvironment
                ? RecipeMessages.VariablesNotSetWithEnvironment
                : RecipeMessages.VariablesNotSet,
            ("Filename", filename), ("Listed", listed.ToString()),
            ("Variable", RunEnvironment.Variable)));
    }

    private static void Walk(JToken token, List<(string, string)> missing)
    {
        switch (token)
        {
            case JProperty property:
                Walk(property.Value, missing);
                break;

            case JContainer container:
                // Over a copy: expanding a value replaces it, and the list of children
                // is being read while that happens.
                foreach (var child in new List<JToken>(container.Children()))
                    Walk(child, missing);
                break;

            case JValue value when value.Type == JTokenType.String && !IsResolvedLater(value):
                Substitute(value, missing);
                break;
        }
    }

    private static void Substitute(JValue value, List<(string, string)> missing)
    {
        string text = (string)value.Value!;

        if (text.Length == 0 || text.IndexOf("${", StringComparison.Ordinal) < 0)
            return;

        string path = value.Path;

        value.Value = Placeholder.Replace(text, match =>
        {
            string name = match.Groups["name"].Value;
            string? resolved = Environment.GetEnvironmentVariable(name);

            if (string.IsNullOrEmpty(resolved))
            {
                missing.Add((name, path));
                return match.Value;
            }

            return resolved;
        });
    }

    /// <summary>
    /// Whether this value belongs to something that resolves its own placeholders when
    /// it runs, and is therefore left alone here.
    /// </summary>
    /// <remarks>
    /// The connection strings, which predate this and keep their behaviour on purpose. A
    /// database target that is in the recipe and not being run should not stop a run that
    /// does not touch it - `--validate-only` on a recipe that also exports to a live
    /// database is the ordinary case, and the person running it is not supposed to hold
    /// that password.
    ///
    /// Nothing else has that property. A source path or an output path is read by every
    /// run that reads the recipe at all, so leaving one unresolved only moves the failure
    /// to somewhere that reports a directory named `${…}`.
    /// </remarks>
    private static bool IsResolvedLater(JValue value)
    {
        if (value.Parent is not JProperty property)
            return false;

        if (string.Equals(property.Name, "ConnectionString", StringComparison.Ordinal))
            return true;

        // `Validation.Connections` holds one connection string per name, and the names
        // are the project's own - so it is the owning property that identifies them.
        return property.Parent?.Parent is JProperty owner
            && string.Equals(owner.Name, "Connections", StringComparison.Ordinal);
    }
}
