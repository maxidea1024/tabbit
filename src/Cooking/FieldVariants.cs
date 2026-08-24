using System;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Messages;

namespace Tabbit.Cooking;

/// <summary>
/// Which variant of each field a build takes, from the recipe and the command line.
/// </summary>
/// <remarks>
/// **A sheet may write one field's value column several times over**, naming each one, and a
/// build picks. What is picked is not in the produced files - the chosen column becomes the
/// field and the others are not in the build - so this decides what gets converted rather than
/// how it is written. spec/primary-layout.md section 3.6.
///
/// Resolved once here rather than read in two places. The command line overrides the recipe
/// because that is what a one-off build of the other variant is, and a key written twice on the
/// command line is a mistake rather than a last-one-wins.
/// </remarks>
public sealed class FieldVariants
{
    /// <summary>Nothing asked for, so every field takes its default column.</summary>
    public static readonly FieldVariants None = new FieldVariants(
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private readonly IReadOnlyDictionary<string, string> _chosen;

    private FieldVariants(IReadOnlyDictionary<string, string> chosen)
    {
        _chosen = chosen;
    }

    /// <summary>
    /// Merges what the recipe asked for with what the command line asked for.
    /// </summary>
    /// <remarks>
    /// The command line is applied second and wins, which is what makes a build of the other
    /// variant possible without editing the recipe. Its entries are written `Table.Field=name`,
    /// and anything else is reported rather than skipped - a misspelled override that silently
    /// does nothing produces the default build under a name that says otherwise.
    /// </remarks>
    public static FieldVariants Of(
        IReadOnlyDictionary<string, string>? fromRecipe, IEnumerable<string>? fromCommandLine)
    {
        var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (fromRecipe is not null)
        {
            foreach (var (key, value) in fromRecipe)
                chosen[Normalize(key)] = value.Trim();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string written in fromCommandLine ?? [])
        {
            int equals = written.IndexOf('=');

            if (equals <= 0 || equals == written.Length - 1)
            {
                throw new TabbitException(null, Message.Of(
                    RunMessages.VariantOptionMalformed, ("Written", written)));
            }

            string key = Normalize(written.Substring(0, equals));
            string value = written.Substring(equals + 1).Trim();

            if (!key.Contains('.'))
            {
                throw new TabbitException(null, Message.Of(
                    RunMessages.VariantOptionNotAField, ("Written", written)));
            }

            if (!seen.Add(key))
            {
                throw new TabbitException(null, Message.Of(
                    RunMessages.VariantOptionRepeated, ("Field", key)));
            }

            chosen[key] = value;
        }

        return chosen.Count == 0 ? None : new FieldVariants(chosen);
    }

    /// <summary>The variant asked for on one field, or null when none was.</summary>
    public string? Of(string table, string field)
        => _chosen.TryGetValue(Normalize($"{table}.{field}"), out string? variant) ? variant : null;

    /// <summary>Whether anything was asked for at all.</summary>
    public bool Any => _chosen.Count > 0;

    /// <summary>
    /// Every `Table.Field` asked for, for the report that no column answered one of them.
    /// </summary>
    public IEnumerable<string> Fields => _chosen.Keys;

    private static string Normalize(string key) => key.Trim();
}
