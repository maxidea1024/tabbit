using Tabbit.Rules;
using Tabbit.Validation;

// A rule for a table this model does not have. The point is that this is an error: a table
// gets renamed, its rule file keeps the old name, and every check in it silently stops
// happening while the run reports nothing at all.

internal static class NoSuchTableRules
{
    public static void Validate(ITableContext context)
    {
        context.Error("This rule should never run - the pipeline should refuse the file name first.");
    }
}
