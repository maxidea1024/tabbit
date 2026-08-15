using Tabbit.Rules;
using Tabbit.Validation;

// A table rule beside a runtime one, to show the two stages are independent: this runs whether
// or not `--skip-runtime-validation` leaves the other out.

internal static class ItemRules
{
    public static void Validate(ITableContext context)
    {
        context.Info("Item rules ran.");
    }
}
