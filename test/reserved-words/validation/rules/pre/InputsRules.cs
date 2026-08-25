using Tabbit.Validation;

// A `pre` rule: it runs before a workbook is opened, so there is no model to look at yet.
//
// What it does have is the recipe's own options, which is why this is the folder for a
// project's conventions about its inputs rather than about its data.

internal static class InputsRules
{
    public static void Validate(IPreContext context)
    {
        context.Info($"Pre-validation ran with Locale={context.Option("Locale", "none")}.");

        if (context.Option("Locale", "KR").Length != 2)
            context.Error("The `Locale` validation option should be a two-letter code.");
    }
}
