using Tabbit.Validation;

// A `pre` rule: runs before any workbook is opened, so it has no model to look at.
//
// What it does have is the recipe's own settings, which is what makes this the place for a
// project's conventions about its inputs.

internal static class EnvironmentRules
{
    public static void Validate(IPreContext context)
    {
        context.Info($"Pre-validation ran with Locale={context.Option("Locale", "none")}.");

        if (context.Option("Locale", "KR").Length != 2)
            context.Error("The `Locale` validation option should be a two-letter code.");
    }
}
