using Tabbit.Rules;
using Tabbit.Validation;

// A table rule: the file name is the table it is about, and a name no table has is an error
// rather than a rule that quietly stops running.
//
// `Tables` is the accessor generated from these sheets - the same type the consuming project's
// own code uses - so the fields are typed and a misspelling is a compile error.

internal static class ItemRules
{
    public static void Validate(ITableContext context)
    {
        context.Info($"Item rules ran over {Tables.Item.Records.Count} row(s). Limit is {Limits.Describe(Tables.Item.Records.Count)}.");

        foreach (var row in Tables.Item.Records)
        {
            // A reference arrives resolved, which is what the memory round trip buys: the record
            // rather than the key it was stored as.
            if (row.CategoryId is null)
                context.Error(row, nameof(row.CategoryId), "Every item should belong to a category.");

            // An enum column is the generated enum, so this compares labels rather than numbers.
            if (row.GradeField == Grade.Epic && row.Price <= 0)
                context.Error(row, nameof(row.Price), "An epic item should carry a price.");
        }

        context.Warn("A warning does not stop the run unless the recipe promotes it.");
    }
}
