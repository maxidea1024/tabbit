using Tabbit.Extensions;

namespace Tabbit.CodeGeneration;

/// <summary>
/// How a target's `MemberCase` setting is read.
/// </summary>
/// <remarks>
/// One place rather than one per generator, because the answer is the same everywhere and a
/// misspelled value should be reported the same way in all of them. What differs per language
/// is only the default, which is that language's own convention and is passed in.
///
/// Read once per run, before anything is generated, so a bad value is reported on its own
/// rather than as a verdict about whichever member reached it first.
///
/// spec/naming-conventions.md.
/// </remarks>
internal static class MemberCasing
{
    /// <summary>
    /// Reads one target's setting, rejecting values that are not spellings of anything.
    /// </summary>
    /// <param name="value">What the recipe entry said.</param>
    /// <param name="languageDefault">
    /// The spelling this language uses when the recipe says nothing, which is the spelling it
    /// has always used.
    /// </param>
    /// <param name="target">The target's id, for the message.</param>
    public static NameCase From(string value, NameCase languageDefault, string target)
    {
        // Blank is the language's own convention rather than an error: it is what a recipe
        // written before this setting existed holds, and what deleting the line leaves
        // behind. Every generated file stays as it was until somebody asks otherwise.
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return languageDefault;

        // Hyphen and underscore taken as one separator, as the recipe's other policy
        // settings do: `upper_snake` is nobody's mistake.
        switch (text.ToLowerInvariant().Replace("_", "-"))
        {
            case "pascal": return NameCase.Pascal;
            case "camel": return NameCase.Camel;
            case "snake": return NameCase.Snake;
            case "upper-snake": return NameCase.UpperSnake;
        }

            throw new TabbitException(null,
                Messages.Message.Of(Recipe.RecipeMessages.MemberCaseUnknown,
                    ("Target", target), ("Value", text)));
    }
}
