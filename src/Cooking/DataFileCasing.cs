using Tabbit.Extensions;

namespace Tabbit.Cooking;

/// <summary>
/// How the recipe's `DataFileCase` setting is read.
/// </summary>
/// <remarks>
/// Its own type rather than a method on the cooker, because what it produces is not a
/// cooking decision: it settles a name the exporter writes and fifteen generated readers
/// open. Read once per run, before any table is named.
///
/// spec/naming-conventions.md.
/// </remarks>
internal static class DataFileCasing
{
    /// <summary>
    /// The spelling the recipe asked for, or null to keep each table's own name.
    /// </summary>
    public static NameCase? From(string value)
    {
        // Blank keeps the table's name rather than being an error: it is what every recipe
        // written before this setting existed holds, so every data file keeps the name it
        // had - and a run that renames its data files without being asked to is a run that
        // silently breaks whatever was already reading them.
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return null;

        // Hyphen and underscore taken as one separator, as the recipe's other policy
        // settings do.
        switch (text.ToLowerInvariant().Replace("_", "-"))
        {
            case "pascal": return NameCase.Pascal;
            case "camel": return NameCase.Camel;
            case "snake": return NameCase.Snake;
            case "upper-snake": return NameCase.UpperSnake;
        }

            throw new TabbitException(null,
                Messages.Message.Of(Recipe.RecipeMessages.DataFileCaseUnknown, ("Value", text)));
    }
}
